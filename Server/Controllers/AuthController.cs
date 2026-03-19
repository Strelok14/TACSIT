using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using StrikeballServer.Models;

namespace StrikeballServer.Controllers;

[ApiController]
[Route("auth")]
[Authorize(Roles = "observer,player,admin")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IConfiguration config, ILogger<AuthController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public IActionResult Login([FromBody] AuthRequestDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new AuthResponseDto
            {
                Success = false,
                Message = "login/password required"
            });
        }

        var user = ResolveUsers().FirstOrDefault(u =>
            string.Equals(u.Login, request.Login, StringComparison.Ordinal) &&
            string.Equals(u.Password, request.Password, StringComparison.Ordinal));

        if (user == null)
        {
            _logger.LogWarning("Auth failed for login {login}", request.Login);
            return Unauthorized(new AuthResponseDto
            {
                Success = false,
                Message = "invalid credentials"
            });
        }

        var signingKey = _config["Jwt:SigningKey"]
            ?? Environment.GetEnvironmentVariable("TACID_JWT_SIGNING_KEY");

        if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
        {
            _logger.LogError("JWT signing key is not configured or too short");
            return StatusCode(500, new AuthResponseDto
            {
                Success = false,
                Message = "Server auth config error"
            });
        }

        var issuer = _config["Jwt:Issuer"] ?? "tacid-server";
        var audience = _config["Jwt:Audience"] ?? "tacid-clients";
        var expiresMinutes = _config.GetValue<int>("Jwt:AccessTokenMinutes", 30);
        var expiresAt = DateTime.UtcNow.AddMinutes(Math.Max(5, expiresMinutes));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Login),
            new(ClaimTypes.Name, user.Login),
            new(ClaimTypes.Role, user.Role)
        };

        if (user.BeaconId.HasValue)
        {
            claims.Add(new Claim("beacon_id", user.BeaconId.Value.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: creds);

        var token = new JwtSecurityTokenHandler().WriteToken(jwt);

        return Ok(new AuthResponseDto
        {
            Success = true,
            Token = token,
            Message = "ok",
            Role = user.Role,
            ExpiresAtUtc = expiresAt
        });
    }

    [HttpGet("me")]
    [Authorize(Roles = "observer,player,admin")]
    public IActionResult Me()
    {
        return Ok(new
        {
            name = User.Identity?.Name,
            role = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value,
            beaconId = User.Claims.FirstOrDefault(c => c.Type == "beacon_id")?.Value
        });
    }

    private IEnumerable<AuthUser> ResolveUsers()
    {
        // Секреты берём только из переменных окружения.
        // Формат: логин/пароль + роль. Player можно связать с beacon_id.
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

    private sealed record AuthUser(string Login, string Password, string Role, int? BeaconId);
}
