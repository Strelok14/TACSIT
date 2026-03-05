using StrikeballServer.Models;

namespace StrikeballServer.Services;

/// <summary>
/// Интерфейс сервиса позиционирования
/// </summary>
public interface IPositioningService
{
    /// <summary>
    /// Вычисление 3D позиции маяка по измерениям расстояний до якорей
    /// </summary>
    Task<Position?> CalculatePositionAsync(int beaconId, List<Measurement> measurements);
}
