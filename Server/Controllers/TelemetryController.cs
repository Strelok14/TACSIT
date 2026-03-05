using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrikeballServer.Data;
using StrikeballServer.Models;
using StrikeballServer.Services;
using StrikeballServer.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace StrikeballServer.Controllers;

/// <summary>
/// Контроллер для приема телеметрии от маяков
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TelemetryController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IPositioningService _positioningService;
    private readonly IHubContext<PositioningHub> _hubContext;
    private readonly ILogger<TelemetryController> _logger;

    public TelemetryController(
        ApplicationDbContext context,
        IPositioningService positioningService,
        IHubContext<PositioningHub> hubContext,
        ILogger<TelemetryController> logger)
    {
        _context = context;
        _positioningService = positioningService;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Прием пакета измерений от маяка
    /// </summary>
    [HttpPost("measurement")]
    public async Task<IActionResult> ReceiveMeasurement([FromBody] MeasurementPacketDto packet)
    {
        try
        {
            if (packet.Distances == null || packet.Distances.Count < 3)
            {
                return BadRequest("Необходимо минимум 3 измерения до якорей");
            }

            // Проверка существования маяка
            var beacon = await _context.Beacons.FindAsync(packet.BeaconId);
            if (beacon == null)
            {
                return NotFound($"Маяк с ID {packet.BeaconId} не найден");
            }

            // Обновление статуса маяка
            beacon.LastSeen = DateTime.UtcNow;
            if (packet.BatteryLevel.HasValue)
            {
                beacon.BatteryLevel = packet.BatteryLevel.Value;
                beacon.Status = packet.BatteryLevel.Value < 20 
                    ? BeaconStatus.LowBattery 
                    : BeaconStatus.Active;
            }

            // Сохранение измерений в БД
            var measurements = new List<Measurement>();
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(packet.Timestamp).UtcDateTime;

            foreach (var distance in packet.Distances)
            {
                var measurement = new Measurement
                {
                    BeaconId = packet.BeaconId,
                    AnchorId = distance.AnchorId,
                    Distance = distance.Distance,
                    Rssi = distance.Rssi,
                    Timestamp = timestamp
                };
                measurements.Add(measurement);
            }

            await _context.Measurements.AddRangeAsync(measurements);
            await _context.SaveChangesAsync();

            // Вычисление позиции
            var position = await _positioningService.CalculatePositionAsync(packet.BeaconId, measurements);

            if (position != null)
            {
                // Сохранение позиции
                await _context.Positions.AddAsync(position);
                await _context.SaveChangesAsync();

                // Отправка обновления через WebSocket
                var positionDto = new PositionDto
                {
                    BeaconId = position.BeaconId,
                    BeaconName = beacon.Name,
                    X = position.X,
                    Y = position.Y,
                    Z = position.Z,
                    Confidence = position.Confidence,
                    Method = position.Method,
                    Timestamp = position.Timestamp,
                    AnchorsUsed = position.AnchorsUsed
                };

                await _hubContext.Clients.All.SendAsync("PositionUpdate", positionDto);

                _logger.LogInformation(
                    $"✅ Маяк {packet.BeaconId} позиция: ({position.X:F2}, {position.Y:F2}, {position.Z:F2}), уверенность: {position.Confidence:F2}");

                return Ok(new { success = true, position = positionDto });
            }

            return Ok(new { success = true, message = "Измерения сохранены, позиция не вычислена" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Ошибка при обработке пакета от маяка {packet.BeaconId}");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
