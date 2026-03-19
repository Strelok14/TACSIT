using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using StrikeballServer.Data;
using StrikeballServer.Models;

namespace StrikeballServer.Controllers;

/// <summary>
/// Контроллер для управления якорями
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "admin")]
public class AnchorsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AnchorsController> _logger;

    public AnchorsController(ApplicationDbContext context, ILogger<AnchorsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Получить список всех якорей
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<Anchor>>> GetAllAnchors()
    {
        return await _context.Anchors.ToListAsync();
    }

    /// <summary>
    /// Получить якорь по ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Anchor>> GetAnchor(int id)
    {
        var anchor = await _context.Anchors.FindAsync(id);
        if (anchor == null)
        {
            return NotFound($"Якорь с ID {id} не найден");
        }
        return anchor;
    }

    /// <summary>
    /// Добавить новый якорь
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Anchor>> CreateAnchor([FromBody] Anchor anchor)
    {
        try
        {
            anchor.CreatedAt = DateTime.UtcNow;
            anchor.UpdatedAt = DateTime.UtcNow;

            _context.Anchors.Add(anchor);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"✅ Создан якорь {anchor.Name} в позиции ({anchor.X}, {anchor.Y}, {anchor.Z})");

            return CreatedAtAction(nameof(GetAnchor), new { id = anchor.Id }, anchor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при создании якоря");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Обновить координаты якоря
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAnchor(int id, [FromBody] Anchor anchor)
    {
        if (id != anchor.Id)
        {
            return BadRequest("ID якоря не совпадает");
        }

        var existingAnchor = await _context.Anchors.FindAsync(id);
        if (existingAnchor == null)
        {
            return NotFound($"Якорь с ID {id} не найден");
        }

        existingAnchor.Name = anchor.Name;
        existingAnchor.X = anchor.X;
        existingAnchor.Y = anchor.Y;
        existingAnchor.Z = anchor.Z;
        existingAnchor.MacAddress = anchor.MacAddress;
        existingAnchor.CalibrationOffset = anchor.CalibrationOffset;
        existingAnchor.Status = anchor.Status;
        existingAnchor.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation($"✅ Обновлен якорь {existingAnchor.Name}");

        return NoContent();
    }

    /// <summary>
    /// Удалить якорь
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAnchor(int id)
    {
        var anchor = await _context.Anchors.FindAsync(id);
        if (anchor == null)
        {
            return NotFound($"Якорь с ID {id} не найден");
        }

        _context.Anchors.Remove(anchor);
        await _context.SaveChangesAsync();

        _logger.LogInformation($"🗑️ Удален якорь {anchor.Name}");

        return NoContent();
    }
}
