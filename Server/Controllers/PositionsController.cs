using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using StrikeballServer.Data;
using StrikeballServer.Models;

namespace StrikeballServer.Controllers;

/// <summary>
/// Контроллер для работы с позициями маяков
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "observer,player,admin")]
public class PositionsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<PositionsController> _logger;

    public PositionsController(ApplicationDbContext context, ILogger<PositionsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Получить последние позиции всех активных маяков
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AllPositionsDto>> GetAllPositions()
    {
        try
        {
            var activeBeacons = await _context.Beacons
                .Where(b => b.Status == BeaconStatus.Active || b.Status == BeaconStatus.LowBattery)
                .ToListAsync();

            var positions = new List<PositionDto>();

            foreach (var beacon in activeBeacons)
            {
                var latestPosition = await _context.Positions
                    .Where(p => p.BeaconId == beacon.Id)
                    .OrderByDescending(p => p.Timestamp)
                    .FirstOrDefaultAsync();

                if (latestPosition != null)
                {
                    positions.Add(new PositionDto
                    {
                        BeaconId = beacon.Id,
                        BeaconName = beacon.Name,
                        X = latestPosition.X,
                        Y = latestPosition.Y,
                        Z = latestPosition.Z,
                        Confidence = latestPosition.Confidence,
                        Method = latestPosition.Method,
                        Timestamp = latestPosition.Timestamp,
                        AnchorsUsed = latestPosition.AnchorsUsed
                    });
                }
            }

            return Ok(new AllPositionsDto
            {
                Positions = positions,
                Timestamp = DateTime.UtcNow,
                TotalBeacons = positions.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении позиций");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Получить текущую позицию конкретного маяка
    /// </summary>
    [HttpGet("{beaconId}")]
    public async Task<ActionResult<PositionDto>> GetBeaconPosition(int beaconId)
    {
        try
        {
            var beacon = await _context.Beacons.FindAsync(beaconId);
            if (beacon == null)
            {
                return NotFound($"Маяк с ID {beaconId} не найден");
            }

            var latestPosition = await _context.Positions
                .Where(p => p.BeaconId == beaconId)
                .OrderByDescending(p => p.Timestamp)
                .FirstOrDefaultAsync();

            if (latestPosition == null)
            {
                return NotFound($"Позиция для маяка {beaconId} не найдена");
            }

            return Ok(new PositionDto
            {
                BeaconId = beacon.Id,
                BeaconName = beacon.Name,
                X = latestPosition.X,
                Y = latestPosition.Y,
                Z = latestPosition.Z,
                Confidence = latestPosition.Confidence,
                Method = latestPosition.Method,
                Timestamp = latestPosition.Timestamp,
                AnchorsUsed = latestPosition.AnchorsUsed
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Ошибка при получении позиции маяка {beaconId}");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Получить историю позиций маяка за период
    /// </summary>
    [HttpGet("history/{beaconId}")]
    public async Task<ActionResult<List<PositionDto>>> GetPositionHistory(
        int beaconId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 1000)
    {
        try
        {
            var beacon = await _context.Beacons.FindAsync(beaconId);
            if (beacon == null)
            {
                return NotFound($"Маяк с ID {beaconId} не найден");
            }

            var query = _context.Positions.Where(p => p.BeaconId == beaconId);

            if (from.HasValue)
                query = query.Where(p => p.Timestamp >= from.Value);

            if (to.HasValue)
                query = query.Where(p => p.Timestamp <= to.Value);

            var positions = await query
                .OrderByDescending(p => p.Timestamp)
                .Take(limit)
                .Select(p => new PositionDto
                {
                    BeaconId = beaconId,
                    BeaconName = beacon.Name,
                    X = p.X,
                    Y = p.Y,
                    Z = p.Z,
                    Confidence = p.Confidence,
                    Method = p.Method,
                    Timestamp = p.Timestamp,
                    AnchorsUsed = p.AnchorsUsed
                })
                .ToListAsync();

            return Ok(positions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Ошибка при получении истории позиций маяка {beaconId}");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
