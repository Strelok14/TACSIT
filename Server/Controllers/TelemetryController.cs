using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrikeballServer.Data;
using StrikeballServer.Models;
using StrikeballServer.Services;
using Microsoft.Extensions.Configuration;
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
    private readonly IBeaconKeyStore _keyStore;
    private readonly bool _requireSignature;

    // In-memory last sequence tracking to prevent replay (simple implementation)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, long> _lastSequence =
        new System.Collections.Concurrent.ConcurrentDictionary<int, long>();

        public TelemetryController(
        ApplicationDbContext context,
        IPositioningService positioningService,
        IHubContext<PositioningHub> hubContext,
        ILogger<TelemetryController> logger,
        IBeaconKeyStore keyStore,
        IConfiguration config)
    {
        _context = context;
        _positioningService = positioningService;
        _hubContext = hubContext;
        _logger = logger;
        _keyStore = keyStore;
        _requireSignature = config.GetValue<bool>("Telemetry:RequireSignature", true);
    }

    private async Task<bool> VerifySignatureAsync(MeasurementPacketDto packet)
    {
        var keyBase64 = await _keyStore.GetKeyAsync(packet.BeaconId);
        if (string.IsNullOrEmpty(keyBase64))
        {
            _logger.LogWarning("No key configured for beacon {id}", packet.BeaconId);
            return false;
        }

        try
        {
            var keyBytes = Convert.FromBase64String(keyBase64);

            // Canonical payload: beaconId|sequence|timestamp|distances
            var sb = new System.Text.StringBuilder();
            sb.Append(packet.BeaconId).Append('|').Append(packet.Sequence).Append('|').Append(packet.Timestamp).Append('|');
            foreach (var d in packet.Distances)
            {
                sb.Append(d.AnchorId).Append(':').Append(d.Distance.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.Append(':').Append(d.Rssi ?? 0).Append(';');
            }

            var payload = System.Text.Encoding.UTF8.GetBytes(sb.ToString());

            using var hmac = new System.Security.Cryptography.HMACSHA256(keyBytes);
            var computed = hmac.ComputeHash(payload);

            var received = System.Convert.FromBase64String(packet.Signature!);
            return CryptographicEquals(computed, received);
        }
        catch (FormatException fe)
        {
            _logger.LogWarning(fe, "Signature or key is not valid Base64");
            return false;
        }
    }

    private static bool CryptographicEquals(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        var result = 0;
        for (int i = 0; i < a.Length; i++) result |= a[i] ^ b[i];
        return result == 0;
    }

    private async Task RecordAnomaly(int beaconId, string type, string details)
    {
        try
        {
            var anomaly = new Anomaly { BeaconId = beaconId, Type = type, Details = details, Timestamp = DateTime.UtcNow };
            await _context.Anomalies.AddAsync(anomaly);
            await _context.SaveChangesAsync();
            _logger.LogWarning("Anomaly {type} for beacon {id}: {details}", type, beaconId, details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record anomaly for beacon {id}", beaconId);
        }
    }

    /// <summary>
    /// Прием пакета измерений от маяка
    /// </summary>
    [HttpPost("measurement")]
    public async Task<IActionResult> ReceiveMeasurement([FromBody] MeasurementPacketDto packet)
    {
            try
            {
                // Basic authentication: check signature and sequence/timestamp (can be disabled via config)
                if (_requireSignature)
                {
                    if (string.IsNullOrEmpty(packet.Signature) || !packet.Sequence.HasValue)
                    {
                        await RecordAnomaly(packet.BeaconId, "MissingAuth", "Missing signature or sequence");
                        return Unauthorized("Missing signature or sequence");
                    }

                    var ok = await VerifySignatureAsync(packet);
                    if (!ok)
                    {
                        await RecordAnomaly(packet.BeaconId, "InvalidSignature", "HMAC validation failed");
                        return Unauthorized("Invalid signature");
                    }

                    // Timestamp sanity (5s drift allowed)
                    var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (Math.Abs(nowMs - packet.Timestamp) > 5000)
                    {
                        await RecordAnomaly(packet.BeaconId, "TimestampDrift", $"ts={packet.Timestamp}");
                        // continue processing but log
                        _logger.LogWarning("Timestamp drift for beacon {beacon}: {ts}", packet.BeaconId, packet.Timestamp);
                    }

                    // Sequence replay check
                    var seq = packet.Sequence.Value;
                    var last = _lastSequence.GetOrAdd(packet.BeaconId, -1);
                    if (seq <= last)
                    {
                        await RecordAnomaly(packet.BeaconId, "SequenceReplay", $"seq={seq}, last={last}");
                        return Conflict("Replay detected");
                    }
                    _lastSequence[packet.BeaconId] = seq;
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
