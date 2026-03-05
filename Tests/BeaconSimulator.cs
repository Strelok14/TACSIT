using System.Net.Http.Json;
using System.Text.Json;

namespace StrikeballServer.Tests;

/// <summary>
/// Симулятор маяка для тестирования сервера
/// </summary>
public class BeaconSimulator
{
    private readonly HttpClient _client;
    private readonly Random _random;

    public BeaconSimulator(string serverUrl = "http://localhost:5000")
    {
        _client = new HttpClient { BaseAddress = new Uri(serverUrl) };
        _random = new Random();
    }

    /// <summary>
    /// Симуляция маяка, движущегося по прямой траектории
    /// </summary>
    public async Task SimulateStraightPath(
        int beaconId,
        double startX, double startY, double startZ,
        double endX, double endY, double endZ,
        int steps = 20,
        int delayMs = 200)
    {
        Console.WriteLine($"🎯 Симуляция маяка {beaconId} движется от ({startX:F1}, {startY:F1}, {startZ:F1}) " +
                         $"к ({endX:F1}, {endY:F1}, {endZ:F1})");

        // Координаты 4 якорей (квадрат 50x50м)
        var anchors = new[]
        {
            (Id: 1, X: 0.0, Y: 0.0, Z: 2.0),
            (Id: 2, X: 50.0, Y: 0.0, Z: 2.0),
            (Id: 3, X: 50.0, Y: 50.0, Z: 2.0),
            (Id: 4, X: 0.0, Y: 50.0, Z: 2.0)
        };

        for (int i = 0; i <= steps; i++)
        {
            // Интерполяция позиции
            double t = (double)i / steps;
            double currentX = startX + (endX - startX) * t;
            double currentY = startY + (endY - startY) * t;
            double currentZ = startZ + (endZ - startZ) * t;

            // Вычисление расстояний до якорей с небольшим шумом
            var distances = anchors.Select(anchor =>
            {
                double trueDistance = Math.Sqrt(
                    Math.Pow(currentX - anchor.X, 2) +
                    Math.Pow(currentY - anchor.Y, 2) +
                    Math.Pow(currentZ - anchor.Z, 2)
                );

                // Добавляем шум ±0.2м
                double noise = (_random.NextDouble() - 0.5) * 0.4;
                double measuredDistance = trueDistance + noise;

                return new
                {
                    anchorId = anchor.Id,
                    distance = Math.Max(0.1, measuredDistance) // Минимум 0.1м
                };
            }).ToList();

            // Формирование пакета
            var packet = new
            {
                beaconId = beaconId,
                distances = distances,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                batteryLevel = 100 - (i * 2) // Симуляция разряда батареи
            };

            try
            {
                // Отправка пакета на сервер
                var response = await _client.PostAsJsonAsync("/api/telemetry/measurement", packet);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                    Console.WriteLine($"✅ Шаг {i + 1}/{steps + 1}: Позиция ({currentX:F2}, {currentY:F2}, {currentZ:F2}) → Ответ: {result}");
                }
                else
                {
                    Console.WriteLine($"❌ Шаг {i + 1}/{steps + 1}: Ошибка {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка отправки: {ex.Message}");
            }

            await Task.Delay(delayMs);
        }

        Console.WriteLine($"✅ Симуляция маяка {beaconId} завершена!");
    }

    /// <summary>
    /// Симуляция статичного маяка с шумом
    /// </summary>
    public async Task SimulateStaticBeacon(
        int beaconId,
        double x, double y, double z,
        int iterations = 10,
        int delayMs = 500)
    {
        Console.WriteLine($"🎯 Симуляция статичного маяка {beaconId} в позиции ({x:F1}, {y:F1}, {z:F1})");

        var anchors = new[]
        {
            (Id: 1, X: 0.0, Y: 0.0, Z: 2.0),
            (Id: 2, X: 50.0, Y: 0.0, Z: 2.0),
            (Id: 3, X: 50.0, Y: 50.0, Z: 2.0),
            (Id: 4, X: 0.0, Y: 50.0, Z: 2.0)
        };

        for (int i = 0; i < iterations; i++)
        {
            var distances = anchors.Select(anchor =>
            {
                double trueDistance = Math.Sqrt(
                    Math.Pow(x - anchor.X, 2) +
                    Math.Pow(y - anchor.Y, 2) +
                    Math.Pow(z - anchor.Z, 2)
                );

                double noise = (_random.NextDouble() - 0.5) * 0.3;
                return new
                {
                    anchorId = anchor.Id,
                    distance = Math.Max(0.1, trueDistance + noise)
                };
            }).ToList();

            var packet = new
            {
                beaconId = beaconId,
                distances = distances,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                batteryLevel = 95
            };

            var response = await _client.PostAsJsonAsync("/api/telemetry/measurement", packet);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"✅ Итерация {i + 1}/{iterations}");
            }

            await Task.Delay(delayMs);
        }

        Console.WriteLine($"✅ Симуляция статичного маяка {beaconId} завершена!");
    }
}

/// <summary>
/// Пример использования симулятора
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("🚀 Запуск симулятора маяков...\n");

        var simulator = new BeaconSimulator("http://localhost:5000");

        // Симуляция маяка 1: движется по диагонали
        await simulator.SimulateStraightPath(
            beaconId: 1,
            startX: 10, startY: 10, startZ: 1.5,
            endX: 40, endY: 40, endZ: 1.5,
            steps: 15,
            delayMs: 300
        );

        Console.WriteLine("\n---\n");

        // Симуляция маяка 2: статичная позиция с шумом
        await simulator.SimulateStaticBeacon(
            beaconId: 2,
            x: 25, y: 25, z: 1.5,
            iterations: 10,
            delayMs: 500
        );

        Console.WriteLine("\n✅ Все симуляции завершены!");
    }
}
