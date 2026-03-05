namespace StrikeballServer.Services;

/// <summary>
/// Интерфейс сервиса телеметрии
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Логирование метрик системы
    /// </summary>
    void LogMetrics(string metricName, double value);
}
