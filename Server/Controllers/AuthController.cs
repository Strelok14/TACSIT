using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using StrikeballServer.Models;

namespace StrikeballServer.Controllers;

[ApiController]
[Route("auth")]
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

        var expectedLogin = _config["Auth:Login"] ?? "user_login";
        var expectedPassword = _config["Auth:Password"] ?? "user_password";

        if (!string.Equals(request.Login, expectedLogin, StringComparison.Ordinal) ||
            !string.Equals(request.Password, expectedPassword, StringComparison.Ordinal))
        {
            _logger.LogWarning("Auth failed for login {login}", request.Login);
            return Unauthorized(new AuthResponseDto
            {
                Success = false,
                Message = "invalid credentials"
            });
        }

        // Lightweight token for client compatibility; replace with JWT in production.
        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

        return Ok(new AuthResponseDto
        {
            Success = true,
            Token = token,
            Message = "ok"
        });
    }
}
