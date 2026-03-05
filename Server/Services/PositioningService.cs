using Microsoft.EntityFrameworkCore;
using StrikeballServer.Data;
using StrikeballServer.Models;
using MathNet.Numerics.LinearAlgebra;

namespace StrikeballServer.Services;

/// <summary>
/// Вспомогательный класс для калиброванных измерений
/// </summary>
internal class CalibratedMeasurement
{
    public Anchor? Anchor { get; set; }
    public double Distance { get; set; }
}

/// <summary>
/// Сервис вычисления позиции маяка методом трилатерации (TWR)
/// </summary>
public class PositioningService : IPositioningService
{
    private readonly ApplicationDbContext _context;
    private readonly IFilteringService _filteringService;
    private readonly ILogger<PositioningService> _logger;
    private readonly IConfiguration _configuration;

    public PositioningService(
        ApplicationDbContext context,
        IFilteringService filteringService,
        ILogger<PositioningService> logger,
        IConfiguration configuration)
    {
        _context = context;
        _filteringService = filteringService;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Вычисление 3D позиции маяка по измерениям расстояний до якорей
    /// Метод: Least Squares (наименьшие квадраты)
    /// </summary>
    public async Task<Position?> CalculatePositionAsync(int beaconId, List<Measurement> measurements)
    {
        try
        {
            var minAnchors = _configuration.GetValue<int>("PositioningSettings:MinimumAnchorsRequired", 3);

            if (measurements.Count < minAnchors)
            {
                _logger.LogWarning($"Недостаточно якорей для вычисления позиции маяка {beaconId}: {measurements.Count} < {minAnchors}");
                return null;
            }

            // Получаем координаты якорей
            var anchorIds = measurements.Select(m => m.AnchorId).ToList();
            var anchors = await _context.Anchors
                .Where(a => anchorIds.Contains(a.Id) && a.Status == AnchorStatus.Active)
                .ToListAsync();

            if (anchors.Count < minAnchors)
            {
                _logger.LogWarning($"Недостаточно активных якорей для маяка {beaconId}");
                return null;
            }

            // Применяем калибровочные смещения
            var calibratedMeasurements = measurements.Select(m =>
            {
                var anchor = anchors.FirstOrDefault(a => a.Id == m.AnchorId);
                return new CalibratedMeasurement
                {
                    Anchor = anchor,
                    Distance = m.Distance + (anchor?.CalibrationOffset ?? 0.0)
                };
            }).Where(x => x.Anchor != null).ToList();

            if (calibratedMeasurements.Count < minAnchors)
            {
                return null;
            }

            // Трилатерация методом наименьших квадратов
            var position = CalculatePosition3D(calibratedMeasurements);

            if (position == null)
            {
                _logger.LogWarning($"Не удалось вычислить позицию для маяка {beaconId}");
                return null;
            }

            // Вычисление уверенности (confidence)
            var confidence = CalculateConfidence(calibratedMeasurements, position.Value);

            var result = new Position
            {
                BeaconId = beaconId,
                X = position.Value.X,
                Y = position.Value.Y,
                Z = position.Value.Z,
                Confidence = confidence,
                Method = "TWR",
                Timestamp = DateTime.UtcNow,
                AnchorsUsed = calibratedMeasurements.Count
            };

            // Фильтрация (Kalman)
            var filteredPosition = _filteringService.FilterPosition(beaconId, result);

            return filteredPosition;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Ошибка при вычислении позиции маяка {beaconId}");
            return null;
        }
    }

    /// <summary>
    /// Вычисление 3D координат методом наименьших квадратов
    /// </summary>
    private (double X, double Y, double Z)? CalculatePosition3D(
        List<CalibratedMeasurement> measurements)
    {
        try
        {
            int n = measurements.Count;

            // Для системы уравнений используем первый якорь как референсный
            var anchor0 = measurements[0].Anchor!;
            double x0 = anchor0.X, y0 = anchor0.Y, z0 = anchor0.Z;
            double d0 = measurements[0].Distance;

            // Построение матрицы A и вектора b
            var matrixData = new double[n - 1, 3];
            var vectorData = new double[n - 1];

            for (int i = 1; i < n; i++)
            {
                var anchor = measurements[i].Anchor!;
                double xi = anchor.X, yi = anchor.Y, zi = anchor.Z;
                double di = measurements[i].Distance;

                matrixData[i - 1, 0] = 2 * (xi - x0);
                matrixData[i - 1, 1] = 2 * (yi - y0);
                matrixData[i - 1, 2] = 2 * (zi - z0);

                vectorData[i - 1] = (d0 * d0 - di * di) + (xi * xi - x0 * x0) + (yi * yi - y0 * y0) + (zi * zi - z0 * z0);
            }

            var A = Matrix<double>.Build.DenseOfArray(matrixData);
            var b = Vector<double>.Build.Dense(vectorData);

            // Решение: x = (A^T * A)^-1 * A^T * b
            var AtA = A.TransposeThisAndMultiply(A);
            var Atb = A.Transpose() * b;

            var solution = AtA.Solve(Atb);

            return (solution[0], solution[1], solution[2]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при вычислении 3D позиции методом наименьших квадратов");
            return null;
        }
    }

    /// <summary>
    /// Вычисление уверенности в позиции (0.0-1.0)
    /// Основано на геометрии якорей и остаточной ошибке
    /// </summary>
    private double CalculateConfidence(List<CalibratedMeasurement> measurements, (double X, double Y, double Z) position)
    {
        try
        {
            // Вычисляем среднеквадратичную ошибку
            double sumSquaredErrors = 0;
            int count = 0;

            foreach (var measurement in measurements)
            {
                var anchor = measurement.Anchor!;
                double measuredDistance = measurement.Distance;

                // Вычисленное расстояние от позиции до якоря
                double calculatedDistance = Math.Sqrt(
                    Math.Pow(position.X - anchor.X, 2) +
                    Math.Pow(position.Y - anchor.Y, 2) +
                    Math.Pow(position.Z - anchor.Z, 2)
                );

                double error = Math.Abs(calculatedDistance - measuredDistance);
                sumSquaredErrors += error * error;
                count++;
            }

            double rmse = Math.Sqrt(sumSquaredErrors / count);

            // Конвертируем RMSE в confidence (0.0-1.0)
            // Если RMSE < 0.5м → confidence = 1.0
            // Если RMSE > 5.0м → confidence = 0.0
            double confidence = Math.Max(0.0, Math.Min(1.0, 1.0 - (rmse / 5.0)));

            return confidence;
        }
        catch
        {
            return 0.5; // Средняя уверенность при ошибке
        }
    }
}
