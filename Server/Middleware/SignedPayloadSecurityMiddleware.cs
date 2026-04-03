using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using StrikeballServer.Services;

namespace StrikeballServer.Middleware;

public class SignedPayloadSecurityMiddleware
{
    public const string SequenceItemKey = "SignedPayload.Sequence";
    public const string TimestampItemKey = "SignedPayload.Timestamp";

    private readonly RequestDelegate _next;

    public SignedPayloadSecurityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IConfiguration configuration,
        IUserHmacKeyStore keyStore,
        IReplayProtectionService replayProtection,
        ITelemetryRateLimiter rateLimiter,
        ISecurityEventLogger securityEventLogger)
    {
        if (!IsProtectedEndpoint(context.Request))
        {
            await _next(context);
            return;
        }

        if (!(context.User.Identity?.IsAuthenticated ?? false))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "JWT is required" });
            return;
        }

        var userIdClaim = context.User.FindFirstValue("user_id")
            ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(userIdClaim, out var userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "JWT user_id is missing" });
            return;
        }

        if (!int.TryParse(context.Request.Headers["X-User-Id"], out var headerUserId) || headerUserId != userId)
        {
            await securityEventLogger.LogAsync(userId, "HeaderUserMismatch", "X-User-Id does not match JWT subject");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "X-User-Id mismatch" });
            return;
        }

        if (!long.TryParse(context.Request.Headers["X-Sequence"], out var sequence) || sequence <= 0)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "X-Sequence is required" });
            return;
        }

        if (!long.TryParse(context.Request.Headers["X-Timestamp"], out var timestampMs) || timestampMs <= 0)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "X-Timestamp is required" });
            return;
        }

        var signature = context.Request.Headers["X-Signature"].ToString();
        if (string.IsNullOrWhiteSpace(signature))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "X-Signature is required" });
            return;
        }

        var sourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var channel = ResolveChannel(context.Request.Path);
        var capacity = configuration.GetValue<int>($"TelemetrySecurity:Limits:{channel}:Capacity", channel == "gps" ? 10 : 5);
        var refillPerSecond = configuration.GetValue<double>($"TelemetrySecurity:Limits:{channel}:RefillPerSecond", channel == "gps" ? 10 : 5);

        var isRateAllowed = await rateLimiter.IsAllowedAsync(userId, channel, sourceIp, capacity, refillPerSecond, context.RequestAborted);
        if (!isRateAllowed)
        {
            await securityEventLogger.LogAsync(userId, "RateLimitExceeded", $"channel={channel};ip={sourceIp}");
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded" });
            return;
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var maxDriftMs = configuration.GetValue<int>("TelemetrySecurity:MaxTimestampDriftMs", 5000);
        if (Math.Abs(nowMs - timestampMs) > maxDriftMs)
        {
            await securityEventLogger.LogAsync(userId, "TimestampDrift", $"channel={channel};timestamp={timestampMs};now={nowMs}");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Packet timestamp drift is too large" });
            return;
        }

        context.Request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync(context.RequestAborted);
            context.Request.Body.Position = 0;
        }

        var keyBytes = await keyStore.GetKeyBytesAsync(userId, context.RequestAborted);
        if (keyBytes == null)
        {
            await securityEventLogger.LogAsync(userId, "MissingHmacKey", $"channel={channel}");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "HMAC key is missing for user" });
            return;
        }

        byte[] receivedSignature;
        try
        {
            receivedSignature = Convert.FromBase64String(signature);
        }
        catch (FormatException)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Malformed signature" });
            return;
        }

        var canonical = $"{userId}|{sequence}|{timestampMs}|{rawBody}";
        byte[] computedSignature;
        using (var hmac = new HMACSHA256(keyBytes))
        {
            computedSignature = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        }

        if (!CryptographicOperations.FixedTimeEquals(computedSignature, receivedSignature))
        {
            await securityEventLogger.LogAsync(userId, "InvalidSignature", $"channel={channel};seq={sequence}");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid signature" });
            return;
        }

        var replayAccepted = await replayProtection.AcceptPacketAsync(userId, channel, sequence, timestampMs, context.RequestAborted);
        if (!replayAccepted)
        {
            await securityEventLogger.LogAsync(userId, "ReplayDetected", $"channel={channel};seq={sequence}");
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new { error = "Replay detected" });
            return;
        }

        context.Items[SequenceItemKey] = sequence;
        context.Items[TimestampItemKey] = timestampMs;

        await _next(context);
    }

    private static bool IsProtectedEndpoint(HttpRequest request)
    {
        return HttpMethods.IsPost(request.Method)
            && (request.Path.Equals("/api/gps", StringComparison.OrdinalIgnoreCase)
                || request.Path.Equals("/api/detections", StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveChannel(PathString path)
    {
        if (path.Equals("/api/gps", StringComparison.OrdinalIgnoreCase))
        {
            return "gps";
        }

        return "detections";
    }
}