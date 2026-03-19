using StrikeballServer.Data;
using StrikeballServer.Models;

namespace StrikeballServer.Services;

public class SecurityEventLogger : ISecurityEventLogger
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SecurityEventLogger> _logger;

    public SecurityEventLogger(ApplicationDbContext context, ILogger<SecurityEventLogger> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task LogAsync(int beaconId, string type, string details, LogLevel level = LogLevel.Warning, CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedDetails = details.Length > 1900 ? details[..1900] : details;
            var anomaly = new Anomaly
            {
                BeaconId = beaconId,
                Type = type,
                Details = normalizedDetails,
                Timestamp = DateTime.UtcNow
            };

            await _context.Anomalies.AddAsync(anomaly, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось записать событие безопасности в БД: {type}", type);
        }

        _logger.Log(level, "SECURITY [{type}] beacon={beaconId}: {details}", type, beaconId, details);
    }
}
