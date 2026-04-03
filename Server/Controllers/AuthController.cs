using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using StrikeballServer.Data;
using StrikeballServer.Models;
using StrikeballServer.Services;
using Microsoft.EntityFrameworkCore;

namespace StrikeballServer.Controllers;

[ApiController]
[Route("auth")]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<AuthController> _logger;
    private readonly ApplicationDbContext _db;
    private readonly IJwtDenylistService _denylist;
    private readonly IUserHmacKeyStore _userHmacKeyStore;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AuthController(
        IConfiguration config,
        ILogger<AuthController> logger,
        ApplicationDbContext db,
        IJwtDenylistService denylist,
        IUserHmacKeyStore userHmacKeyStore)
    {
        _config = config;
        _logger = logger;
        _db = db;
        _denylist = denylist;
        _userHmacKeyStore = userHmacKeyStore;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] AuthRequestDto request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new AuthResponseDto { Success = false, Message = "login/password required" });
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Login == request.Login, cancellationToken);

        if (user == null)
        {
            _logger.LogWarning("Auth failed: invalid credentials");
            return Unauthorized(new AuthResponseDto { Success = false, Message = "invalid credentials" });
        }

        var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Auth failed: invalid credentials for {login}", request.Login);
            return Unauthorized(new AuthResponseDto { Success = false, Message = "invalid credentials" });
        }

        var (accessToken, jti, expiresAt) = GenerateAccessToken(user);
        if (accessToken == null)
        {
            return StatusCode(500, new AuthResponseDto { Success = false, Message = "Server auth config error" });
        }

        var refreshToken = await IssueRefreshTokenAsync(user.Id.ToString(), cancellationToken);
        var hmacKey = await _userHmacKeyStore.EnsureKeyBase64Async(user.Id, cancellationToken);
        user.LastLoginAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new AuthResponseDto
        {
            Success = true,
            Token = accessToken,
            Message = "ok",
            Role = user.Role,
            UserId = user.Id,
            HmacKey = hmacKey,
            ExpiresAtUtc = expiresAt,
            RefreshToken = refreshToken.Token,
            RefreshExpiresAtUtc = refreshToken.ExpiryUtc
        });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.RefreshToken))
        {
            return BadRequest(new AuthResponseDto { Success = false, Message = "refresh_token required" });
        }

        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == request.RefreshToken, cancellationToken);

        if (stored == null || stored.IsRevoked || stored.ExpiryUtc < DateTime.UtcNow)
        {
            return Unauthorized(new AuthResponseDto { Success = false, Message = "invalid or expired refresh token" });
        }

        // Revoke used refresh token (single-use rotation).
        stored.IsRevoked = true;
        await _db.SaveChangesAsync(cancellationToken);

        if (!int.TryParse(stored.UserId, out var userId))
        {
            return Unauthorized(new AuthResponseDto { Success = false, Message = "invalid refresh token owner" });
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("Refresh: user {userId} not found in database", stored.UserId);
            return Unauthorized(new AuthResponseDto { Success = false, Message = "user not found" });
        }

        var (accessToken, jti, expiresAt) = GenerateAccessToken(user);
        if (accessToken == null)
        {
            return StatusCode(500, new AuthResponseDto { Success = false, Message = "Server auth config error" });
        }

        var newRefresh = await IssueRefreshTokenAsync(user.Id.ToString(), cancellationToken);

        return Ok(new AuthResponseDto
        {
            Success = true,
            Token = accessToken,
            Message = "ok",
            Role = user.Role,
            UserId = user.Id,
            HmacKey = await _userHmacKeyStore.EnsureKeyBase64Async(user.Id, cancellationToken),
            ExpiresAtUtc = expiresAt,
            RefreshToken = newRefresh.Token,
            RefreshExpiresAtUtc = newRefresh.ExpiryUtc
        });
    }

    [HttpPost("logout")]
    [Authorize(Roles = "observer,player,admin")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequestDto? request, CancellationToken cancellationToken)
    {
        // Put current access token JTI to denylist.
        var jti = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
        if (!string.IsNullOrEmpty(jti))
        {
            // Compute remaining TTL from exp claim.
            var expStr = User.FindFirstValue(JwtRegisteredClaimNames.Exp);
            TimeSpan ttl = TimeSpan.FromMinutes(30);
            if (long.TryParse(expStr, out var expUnix))
            {
                var expAt = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;
                ttl = expAt - DateTime.UtcNow;
                if (ttl <= TimeSpan.Zero) ttl = TimeSpan.FromSeconds(1);
            }

            await _denylist.AddAsync(jti, ttl, cancellationToken);
        }

        // Revoke refresh token too when provided.
        if (!string.IsNullOrWhiteSpace(request?.RefreshToken))
        {
            var stored = await _db.RefreshTokens
                .FirstOrDefaultAsync(t => t.Token == request.RefreshToken, cancellationToken);
            if (stored != null && !stored.IsRevoked)
            {
                stored.IsRevoked = true;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        return NoContent();
    }

    [HttpGet("me")]
    [Authorize(Roles = "observer,player,admin")]
    public IActionResult Me()
    {
        return Ok(new
        {
            userId = User.Claims.FirstOrDefault(c => c.Type == "user_id")?.Value,
            name = User.Identity?.Name,
            role = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value,
            login = User.Identity?.Name
        });
    }

    // Private helpers

    private (string? token, string jti, DateTime expiresAt) GenerateAccessToken(User user)
    {
        var signingKey = _config["Jwt:SigningKey"]
            ?? Environment.GetEnvironmentVariable("TACID_JWT_SIGNING_KEY");

        if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
        {
            _logger.LogError("JWT signing key is not configured or too short");
            return (null, string.Empty, default);
        }

        var issuer = _config["Jwt:Issuer"] ?? "tacid-server";
        var audience = _config["Jwt:Audience"] ?? "tacid-clients";
        var expiresMinutes = _config.GetValue<int>("Jwt:AccessTokenMinutes", 30);
        var expiresAt = DateTime.UtcNow.AddMinutes(Math.Max(5, expiresMinutes));
        var jti = Guid.NewGuid().ToString();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Login),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(JwtRegisteredClaimNames.NameId, user.Id.ToString()),
            new(ClaimTypes.Name, user.Login),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("user_id", user.Id.ToString()),
            new(ClaimTypes.Role, user.Role)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var jwt = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(jwt), jti, expiresAt);
    }

    private async Task<RefreshToken> IssueRefreshTokenAsync(string userId, CancellationToken cancellationToken)
    {
        var days = _config.GetValue<int>("Jwt:RefreshTokenDays", 30);
        var rawBytes = RandomNumberGenerator.GetBytes(64);
        var token = Convert.ToHexString(rawBytes).ToLowerInvariant();

        var rt = new RefreshToken
        {
            UserId = userId,
            Token = token,
            ExpiryUtc = DateTime.UtcNow.AddDays(days),
            IsRevoked = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.RefreshTokens.Add(rt);
        await _db.SaveChangesAsync(cancellationToken);
        return rt;
    }
}
