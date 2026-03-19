using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StrikeballServer.Data;
using StrikeballServer.Hubs;
using StrikeballServer.Models;
using StrikeballServer.Services;

namespace StrikeballServer.Controllers;

/// <summary>
/// Контроллер для приема телеметрии от маяков
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "player,admin")]
public class TelemetryController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IPositioningService _positioningService;
    private readonly IHubContext<PositioningHub> _hubContext;
    private readonly ILogger<TelemetryController> _logger;
    private readonly ISecurityEventLogger _securityEventLogger;
    private readonly IConfiguration _configuration;

    public TelemetryController(
        ApplicationDbContext context,
        IPositioningService positioningService,
        IHubContext<PositioningHub> hubContext,
        ILogger<TelemetryController> logger,
        ISecurityEventLogger securityEventLogger,
        IConfiguration configuration)
    {
        _context = context;
        _positioningService = positioningService;
        _hubContext = hubContext;
        _logger = logger;
        _securityEventLogger = securityEventLogger;
        _configuration = configuration;
    }

    /// <summary>
    /// Прием пакета измерений от маяка
    /// </summary>
    [HttpPost("measurement")]
    public async Task<IActionResult> ReceiveMeasurement([FromBody] MeasurementPacketDto packet)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            // Физические границы расстояний дублируем явно на уровне контроллера,
            // даже при наличии DataAnnotations, чтобы иметь аудитируемую защиту в одном месте.
            if (packet.Distances.Any(d => d.Distance <= 0 || d.Distance > 200))
            {
                await _securityEventLogger.LogAsync(packet.BeaconId, "InvalidDistance", "Distance out of physical bounds");
                return BadRequest(new { error = "Distance out of physical bounds" });
            }

            if (packet.Distances == null || packet.Distances.Count < 1)
            {
                return BadRequest("Необходимо минимум 1 измерение до якорей");
            }

            // Проверка существования маяка; при первом пакете создаем автоматически.
            var beacon = await _context.Beacons.FindAsync(packet.BeaconId);
            if (beacon == null)
            {
                beacon = new Beacon
                {
                    Id = packet.BeaconId,
                    Name = $"Beacon_{packet.BeaconId}",
                    BatteryLevel = packet.BatteryLevel ?? 100,
                    Status = BeaconStatus.Active,
                    LastSeen = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                await _context.Beacons.AddAsync(beacon);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Auto-registered beacon {id}", packet.BeaconId);
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
                    Timestamp = timestamp,
                    PacketSequence = packet.Sequence
                };
                measurements.Add(measurement);
            }

            await _context.Measurements.AddRangeAsync(measurements);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Если sequence уже есть в БД, значит backlog повторно прилетел после сетевого сбоя.
                await _securityEventLogger.LogAsync(packet.BeaconId, "DuplicateSequenceInDb", $"seq={packet.Sequence}", LogLevel.Information);
                return Conflict(new { error = "Duplicate sequence" });
            }

            // Вычисление позиции
            var position = await _positioningService.CalculatePositionAsync(packet.BeaconId, measurements);

            if (position != null)
            {
                // Дополнительная проверка физически возможной скорости перемещения.
                var maxSpeed = _configuration.GetValue<double>("TelemetrySecurity:MaxSpeedMetersPerSec", 10.0);
                var previous = await _context.Positions
                    .Where(p => p.BeaconId == packet.BeaconId && p.Timestamp < position.Timestamp)
                    .OrderByDescending(p => p.Timestamp)
                    .FirstOrDefaultAsync();

                if (previous != null)
                {
                    var dt = (position.Timestamp - previous.Timestamp).TotalSeconds;
                    if (dt > 0)
                    {
                        var dx = position.X - previous.X;
                        var dy = position.Y - previous.Y;
                        var dz = position.Z - previous.Z;
                        var speed = Math.Sqrt(dx * dx + dy * dy + dz * dz) / dt;
                        if (speed > maxSpeed)
                        {
                            await _securityEventLogger.LogAsync(packet.BeaconId, "UnrealisticSpeed", $"speed={speed:F2} m/s");
                            return BadRequest(new { error = "Unrealistic speed detected" });
                        }
                    }
                }

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
            await _securityEventLogger.LogAsync(packet.BeaconId, "TelemetryProcessingError", "Unhandled exception in telemetry processing", LogLevel.Error);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
