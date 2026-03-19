using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StrikeballServer.Services;

namespace StrikeballServer.Controllers;

/// <summary>
/// Административные security-операции: загрузка и ротация ключей маяков.
/// </summary>
[ApiController]
[Route("api/security")]
[Authorize(Roles = "admin")]
public class SecurityController : ControllerBase
{
    private readonly IBeaconKeyStore _keyStore;
    private readonly ILogger<SecurityController> _logger;

    public SecurityController(IBeaconKeyStore keyStore, ILogger<SecurityController> logger)
    {
        _keyStore = keyStore;
        _logger = logger;
    }

    [HttpPost("beacons/{beaconId:int}/key")]
    public async Task<IActionResult> ProvisionOrUpdateKey(int beaconId, [FromBody] ProvisionKeyRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.KeyBase64))
        {
            return BadRequest(new { error = "keyBase64 is required" });
        }

        byte[] rawKey;
        try
        {
            rawKey = Convert.FromBase64String(request.KeyBase64);
        }
        catch
        {
            return BadRequest(new { error = "keyBase64 must be valid Base64" });
        }

        var version = request.KeyVersion <= 0 ? 1 : request.KeyVersion;
        await _keyStore.UpsertActiveKeyAsync(beaconId, version, rawKey, TimeSpan.FromDays(Math.Max(1, request.PreviousGraceDays)), cancellationToken);

        _logger.LogInformation("Admin provisioned key for beacon {beaconId}, version {version}", beaconId, version);
        return Ok(new { success = true, beaconId, keyVersion = version });
    }

    [HttpPost("beacons/{beaconId:int}/rotate")]
    public async Task<IActionResult> RotateKey(int beaconId, [FromBody] RotateKeyRequest request, CancellationToken cancellationToken)
    {
        var graceDays = Math.Max(1, request.PreviousGraceDays);
        var newVersion = await _keyStore.RotateKeyAsync(beaconId, TimeSpan.FromDays(graceDays), cancellationToken);
        return Ok(new { success = true, beaconId, newVersion, previousGraceDays = graceDays });
    }

    public sealed class ProvisionKeyRequest
    {
        public string KeyBase64 { get; set; } = string.Empty;
        public int KeyVersion { get; set; } = 1;
        public int PreviousGraceDays { get; set; } = 7;
    }

    public sealed class RotateKeyRequest
    {
        public int PreviousGraceDays { get; set; } = 7;
    }
}
