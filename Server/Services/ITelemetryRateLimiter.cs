namespace StrikeballServer.Services;

/// <summary>
/// Ограничение частоты запросов телеметрии (token bucket).
/// </summary>
public interface ITelemetryRateLimiter
{
    Task<bool> IsAllowedAsync(int beaconId, string sourceIp, CancellationToken cancellationToken = default);

    Task<bool> IsAllowedAsync(int subjectId, string channel, string sourceIp, int capacity, double refillPerSecond, CancellationToken cancellationToken = default);
}
