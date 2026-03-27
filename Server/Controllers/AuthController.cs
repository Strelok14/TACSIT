using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
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
[Authorize(Roles = "observer,player,admin")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<AuthController> _logger;
    private readonly ApplicationDbContext _db;
    private readonly IJwtDenylistService _denylist;

    public AuthController(
        IConfiguration config,
        ILogger<AuthController> logger,
        ApplicationDbContext db,
        IJwtDenylistService denylist)
    {
        _config = config;
        _logger = logger;
        _db = db;
        _denylist = denylist;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] AuthRequestDto request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new AuthResponseDto { Success = false, Message = "login/password required" });
        }

        var user = ResolveUsers().FirstOrDefault(u =>
            string.Equals(u.Login, request.Login, StringComparison.Ordinal));

        if (user != null && !FixedTimeSecretEquals(user.Password, request.Password))
        {
            user = null;
        }

        if (user == null)
        {
            _logger.LogWarning("Auth failed: invalid credentials");
            return Unauthorized(new AuthResponseDto { Success = false, Message = "invalid credentials" });
        }

        var (accessToken, jti, expiresAt) = GenerateAccessToken(user);
        if (accessToken == null)
        {
            return StatusCode(500, new AuthResponseDto { Success = false, Message = "Server auth config error" });
        }

        var refreshToken = await IssueRefreshTokenAsync(user.Login, cancellationToken);

        return Ok(new AuthResponseDto
        {
            Success = true,
            Token = accessToken,
            Message = "ok",
            Role = user.Role,
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

        var users = ResolveUsers();
        var user = users.FirstOrDefault(u => string.Equals(u.Login, stored.UserId, StringComparison.Ordinal));
        if (user == null)
        {
            _logger.LogWarning("Refresh: user {userId} not found in env configuration", stored.UserId);
            return Unauthorized(new AuthResponseDto { Success = false, Message = "user not found" });
        }

        var (accessToken, jti, expiresAt) = GenerateAccessToken(user);
        if (accessToken == null)
        {
            return StatusCode(500, new AuthResponseDto { Success = false, Message = "Server auth config error" });
        }

        var newRefresh = await IssueRefreshTokenAsync(user.Login, cancellationToken);

        return Ok(new AuthResponseDto
        {
            Success = true,
            Token = accessToken,
            Message = "ok",
            Role = user.Role,
            ExpiresAtUtc = expiresAt,
            RefreshToken = newRefresh.Token,
            RefreshExpiresAtUtc = newRefresh.ExpiryUtc
        });
    }

    [HttpPost("logout")]
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
    public IActionResult Me()
    {
        return Ok(new
        {
            name = User.Identity?.Name,
            role = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value,
            beaconId = User.Claims.FirstOrDefault(c => c.Type == "beacon_id")?.Value
        });
    }

    // Private helpers

    private (string? token, string jti, DateTime expiresAt) GenerateAccessToken(AuthUser user)
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
            new(ClaimTypes.Name, user.Login),
            new(ClaimTypes.Role, user.Role)
        };

        if (user.BeaconId.HasValue)
        {
            claims.Add(new Claim("beacon_id", user.BeaconId.Value.ToString()));
        }

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

    private IEnumerable<AuthUser> ResolveUsers()
    {
        var users = new List<AuthUser>();
        AddIfPresent(users, "TACID_ADMIN_LOGIN", "TACID_ADMIN_PASSWORD", "admin", null);
        AddIfPresent(users, "TACID_OBSERVER_LOGIN", "TACID_OBSERVER_PASSWORD", "observer", null);

        var playerBeacon = Environment.GetEnvironmentVariable("TACID_PLAYER_BEACON_ID");
        int? playerBeaconId = int.TryParse(playerBeacon, out var bId) ? bId : null;
        AddIfPresent(users, "TACID_PLAYER_LOGIN", "TACID_PLAYER_PASSWORD", "player", playerBeaconId);

        return users;
    }

    private static void AddIfPresent(List<AuthUser> users, string loginEnv, string passEnv, string role, int? beaconId)
    {
        var login = Environment.GetEnvironmentVariable(loginEnv);
        var pass = Environment.GetEnvironmentVariable(passEnv);
        if (!string.IsNullOrWhiteSpace(login) && !string.IsNullOrWhiteSpace(pass))
        {
            users.Add(new AuthUser(login, pass, role, beaconId));
        }
    }

    private static bool FixedTimeSecretEquals(string expected, string provided)
    {
        if (expected == null || provided == null)
        {
            return false;
        }

        // Compare SHA-256 digests in fixed time to reduce timing side channels.
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }

    private sealed record AuthUser(string Login, string Password, string Role, int? BeaconId);
}
