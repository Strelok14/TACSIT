using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using StrikeballServer.Data;
using StrikeballServer.Models;

namespace StrikeballServer.Controllers;

/// <summary>
/// Контроллер для управления маяками
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "admin")]
public class BeaconsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<BeaconsController> _logger;

    public BeaconsController(ApplicationDbContext context, ILogger<BeaconsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Получить список всех маяков
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<Beacon>>> GetAllBeacons()
    {
        return await _context.Beacons.ToListAsync();
    }

    /// <summary>
    /// Получить маяк по ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Beacon>> GetBeacon(int id)
    {
        var beacon = await _context.Beacons.FindAsync(id);
        if (beacon == null)
        {
            return NotFound($"Маяк с ID {id} не найден");
        }
        return beacon;
    }

    /// <summary>
    /// Зарегистрировать новый маяк
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Beacon>> CreateBeacon([FromBody] Beacon beacon)
    {
        try
        {
            beacon.CreatedAt = DateTime.UtcNow;
            beacon.LastSeen = DateTime.UtcNow;

            _context.Beacons.Add(beacon);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"✅ Зарегистрирован маяк {beacon.Name}");

            return CreatedAtAction(nameof(GetBeacon), new { id = beacon.Id }, beacon);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при создании маяка");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Обновить информацию о маяке
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBeacon(int id, [FromBody] Beacon beacon)
    {
        if (id != beacon.Id)
        {
            return BadRequest("ID маяка не совпадает");
        }

        var existingBeacon = await _context.Beacons.FindAsync(id);
        if (existingBeacon == null)
        {
            return NotFound($"Маяк с ID {id} не найден");
        }

        existingBeacon.Name = beacon.Name;
        existingBeacon.MacAddress = beacon.MacAddress;
        existingBeacon.BatteryLevel = beacon.BatteryLevel;
        existingBeacon.Status = beacon.Status;

        await _context.SaveChangesAsync();

        _logger.LogInformation($"✅ Обновлен маяк {existingBeacon.Name}");

        return NoContent();
    }

    /// <summary>
    /// Удалить маяк
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBeacon(int id)
    {
        var beacon = await _context.Beacons.FindAsync(id);
        if (beacon == null)
        {
            return NotFound($"Маяк с ID {id} не найден");
        }

        _context.Beacons.Remove(beacon);
        await _context.SaveChangesAsync();

        _logger.LogInformation($"🗑️ Удален маяк {beacon.Name}");

        return NoContent();
    }
}
