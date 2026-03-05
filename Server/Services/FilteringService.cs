using StrikeballServer.Models;
using System.Collections.Concurrent;

namespace StrikeballServer.Services;

/// <summary>
/// Сервис фильтрации позиций с использованием экспоненциального сглаживания
/// (упрощенная версия, полный Kalman фильтр можно добавить позже)
/// </summary>
public class FilteringService : IFilteringService
{
    private readonly ILogger<FilteringService> _logger;
    private readonly IConfiguration _configuration;

    // Кэш последних позиций для каждого маяка
    private static readonly ConcurrentDictionary<int, Position> _lastPositions = new();
    
    // Коэффициент сглаживания (0.0-1.0), чем выше - тем сильнее сглаживание
    private const double SmoothingFactor = 0.3;

    public FilteringService(ILogger<FilteringService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Фильтрация позиции с использованием экспоненциального сглаживания
    /// </summary>
    public Position FilterPosition(int beaconId, Position rawPosition)
    {
        try
        {
            var kalmanEnabled = _configuration.GetValue<bool>("PositioningSettings:KalmanFilterEnabled", true);

            if (!kalmanEnabled)
            {
                return rawPosition; // Фильтрация отключена
            }

            // Если это первая позиция маяка, просто сохраняем
            if (!_lastPositions.TryGetValue(beaconId, out var lastPosition))
            {
                _lastPositions[beaconId] = rawPosition;
                return rawPosition;
            }

            // Проверка временного интервала (если слишком большой - не фильтруем)
            var timeDelta = (rawPosition.Timestamp - lastPosition.Timestamp).TotalSeconds;
            if (timeDelta > 5.0) // Более 5 секунд - считаем новым треком
            {
                _lastPositions[beaconId] = rawPosition;
                return rawPosition;
            }

            // Экспоненциальное сглаживание
            var filteredX = lastPosition.X * SmoothingFactor + rawPosition.X * (1 - SmoothingFactor);
            var filteredY = lastPosition.Y * SmoothingFactor + rawPosition.Y * (1 - SmoothingFactor);
            var filteredZ = lastPosition.Z * SmoothingFactor + rawPosition.Z * (1 - SmoothingFactor);

            var filteredPosition = new Position
            {
                BeaconId = rawPosition.BeaconId,
                X = filteredX,
                Y = filteredY,
                Z = filteredZ,
                Confidence = rawPosition.Confidence,
                Method = rawPosition.Method + "_EMA",
                Timestamp = rawPosition.Timestamp,
                AnchorsUsed = rawPosition.AnchorsUsed,
                EstimatedError = rawPosition.EstimatedError
            };

            // Обновляем кэш
            _lastPositions[beaconId] = filteredPosition;

            _logger.LogDebug($"Фильтрация маяка {beaconId}: " +
                $"Сырое ({rawPosition.X:F2}, {rawPosition.Y:F2}, {rawPosition.Z:F2}) → " +
                $"Фильтр ({filteredX:F2}, {filteredY:F2}, {filteredZ:F2})");

            return filteredPosition;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Ошибка при фильтрации позиции маяка {beaconId}");
            return rawPosition; // Возвращаем сырую позицию при ошибке
        }
    }

    /// <summary>
    /// Очистка кэша для маяка (например, при перезапуске)
    /// </summary>
    public static void ClearBeaconCache(int beaconId)
    {
        _lastPositions.TryRemove(beaconId, out _);
    }
}
