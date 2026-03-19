namespace StrikeballServer.Services;

/// <summary>
/// Централизованный логгер событий безопасности.
/// Пишет в стандартный лог и в таблицу аномалий.
/// </summary>
public interface ISecurityEventLogger
{
    Task LogAsync(int beaconId, string type, string details, LogLevel level = LogLevel.Warning, CancellationToken cancellationToken = default);
}
