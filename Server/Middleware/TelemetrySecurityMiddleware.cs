using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StrikeballServer.Models;
using StrikeballServer.Services;

namespace StrikeballServer.Middleware;

/// <summary>
/// Криптографическая защита телеметрии ДО бизнес-логики:
/// - rate limiting
/// - проверка timestamp/backlog окна
/// - проверка HMAC-SHA256
/// - replay-защита по sequence
/// При успешной проверке создаёт аутентифицированного пользователя с ролью player.
/// </summary>
public class TelemetrySecurityMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<TelemetrySecurityMiddleware> _logger;

    public TelemetrySecurityMiddleware(RequestDelegate next, ILogger<TelemetrySecurityMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IConfiguration configuration,
        IBeaconKeyStore keyStore,
        IReplayProtectionService replayProtection,
        ITelemetryRateLimiter rateLimiter,
        ISecurityEventLogger securityEventLogger)
    {
        if (!IsTelemetryEndpoint(context.Request))
        {
            await _next(context);
            return;
        }

        var sourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        context.Request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
        }

        MeasurementPacketDto? packet;
        try
        {
            packet = JsonSerializer.Deserialize<MeasurementPacketDto>(rawBody, JsonOptions);
        }
        catch
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid telemetry JSON" });
            return;
        }

        if (packet == null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Empty telemetry packet" });
            return;
        }

        var rateAllowed = await rateLimiter.IsAllowedAsync(packet.BeaconId, sourceIp, context.RequestAborted);
        if (!rateAllowed)
        {
            await securityEventLogger.LogAsync(packet.BeaconId, "RateLimitExceeded", $"ip={sourceIp}");
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded" });
            return;
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var maxDriftMs = configuration.GetValue<int>("TelemetrySecurity:MaxTimestampDriftMs", 5000);
        var maxBacklogAgeMs = configuration.GetValue<int>("TelemetrySecurity:MaxBacklogAgeMs", 120000);

        var drift = Math.Abs(nowMs - packet.Timestamp);
        if (drift > maxDriftMs)
        {
            var age = nowMs - packet.Timestamp;
            if (age > maxBacklogAgeMs)
            {
                await securityEventLogger.LogAsync(packet.BeaconId, "StalePacket", $"timestamp={packet.Timestamp};now={nowMs};age={age}");
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Packet is too old" });
                return;
            }
        }

        if (packet.Sequence <= 0 || string.IsNullOrWhiteSpace(packet.Signature))
        {
            await securityEventLogger.LogAsync(packet.BeaconId, "MissingAuthFields", "Sequence or signature missing");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing sequence/signature" });
            return;
        }

        var keyCandidates = await keyStore.GetVerificationKeysAsync(packet.BeaconId, context.RequestAborted);
        if (keyCandidates.Count == 0)
        {
            await securityEventLogger.LogAsync(packet.BeaconId, "NoBeaconKey", "No key candidates found");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Beacon key is not configured" });
            return;
        }

        var canonical = BuildCanonical(packet);
        var payloadBytes = Encoding.UTF8.GetBytes(canonical);

        byte[] receivedSignature;
        try
        {
            receivedSignature = Convert.FromBase64String(packet.Signature);
        }
        catch
        {
            await securityEventLogger.LogAsync(packet.BeaconId, "MalformedSignature", "Signature is not valid Base64");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Malformed signature" });
            return;
        }

        var isSignatureValid = false;
        var matchedVersion = -1;
        foreach (var key in keyCandidates)
        {
            // Если маяк явно передал KeyVersion, сначала пробуем совпадающую версию.
            if (packet.KeyVersion > 0 && key.KeyVersion != packet.KeyVersion)
            {
                continue;
            }

            using var hmac = new HMACSHA256(key.KeyBytes);
            var computed = hmac.ComputeHash(payloadBytes);
            if (CryptographicOperations.FixedTimeEquals(computed, receivedSignature))
            {
                isSignatureValid = true;
                matchedVersion = key.KeyVersion;
                break;
            }
        }

        if (!isSignatureValid)
        {
            // Если версию не угадали или клиент старый, пытаемся все ключи независимо от версии.
            foreach (var key in keyCandidates)
            {
                using var hmac = new HMACSHA256(key.KeyBytes);
                var computed = hmac.ComputeHash(payloadBytes);
                if (CryptographicOperations.FixedTimeEquals(computed, receivedSignature))
                {
                    isSignatureValid = true;
                    matchedVersion = key.KeyVersion;
                    break;
                }
            }
        }

        if (!isSignatureValid)
        {
            await securityEventLogger.LogAsync(packet.BeaconId, "InvalidSignature", $"seq={packet.Sequence};keyVersion={packet.KeyVersion}");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid signature" });
            return;
        }

        var replayAccepted = await replayProtection.AcceptPacketAsync(packet.BeaconId, packet.Sequence, packet.Timestamp, context.RequestAborted);
        if (!replayAccepted)
        {
            await securityEventLogger.LogAsync(packet.BeaconId, "ReplayDetected", $"seq={packet.Sequence}");
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new { error = "Replay detected" });
            return;
        }

        // Контекст аутентификации для [Authorize] в контроллере.
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, packet.BeaconId.ToString()),
            new(ClaimTypes.Role, "player"),
            new("beacon_id", packet.BeaconId.ToString()),
            new("key_version", matchedVersion.ToString())
        };

        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TelemetryHmac"));

        await _next(context);
    }

    private static bool IsTelemetryEndpoint(HttpRequest request)
    {
        return HttpMethods.IsPost(request.Method)
            && request.Path.Equals("/api/telemetry/measurement", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCanonical(MeasurementPacketDto packet)
    {
        // Строго канонизируем список расстояний по AnchorId, чтобы исключить расхождения
        // при сериализации на разных платформах.
        var ordered = packet.Distances.OrderBy(d => d.AnchorId).ToList();
        var sb = new StringBuilder();
        sb.Append(packet.BeaconId)
          .Append('|')
          .Append(packet.Sequence)
          .Append('|')
          .Append(packet.Timestamp)
          .Append('|');

        foreach (var d in ordered)
        {
            sb.Append(d.AnchorId)
              .Append(':')
              .Append(d.Distance.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append(':')
              .Append(d.Rssi ?? 0)
              .Append(';');
        }

        return sb.ToString();
    }
}
