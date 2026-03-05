using StrikeballServer.Models;

namespace StrikeballServer.Services;

/// <summary>
/// Интерфейс сервиса фильтрации (Kalman и т.д.)
/// </summary>
public interface IFilteringService
{
    /// <summary>
    /// Фильтрация позиции с использованием Kalman фильтра
    /// </summary>
    Position FilterPosition(int beaconId, Position rawPosition);
}
