namespace StrikeballServer.Services;

/// <summary>
/// Сервис телеметрии и метрик
/// </summary>
public class TelemetryService : ITelemetryService
{
    private readonly ILogger<TelemetryService> _logger;

    public TelemetryService(ILogger<TelemetryService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Логирование метрик системы
    /// </summary>
    public void LogMetrics(string metricName, double value)
    {
        _logger.LogInformation($"📊 Метрика: {metricName} = {value}");
    }
}
