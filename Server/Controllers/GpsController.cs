using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StrikeballServer.Data;
using StrikeballServer.Hubs;
using StrikeballServer.Middleware;
using StrikeballServer.Models;

namespace StrikeballServer.Controllers;

[ApiController]
[Route("api/gps")]
[Authorize]
public class GpsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IHubContext<PositioningHub> _hubContext;

    public GpsController(ApplicationDbContext context, IHubContext<PositioningHub> hubContext)
    {
        _context = context;
        _hubContext = hubContext;
    }

    [HttpPost]
    [Authorize(Roles = "player,admin")]
    public async Task<IActionResult> Post([FromBody] GpsPositionRequestDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var userId = GetCurrentUserId();
        var sequence = HttpContext.Items[SignedPayloadSecurityMiddleware.SequenceItemKey] as long? ?? 0L;
        var packetTimestampMs = HttpContext.Items[SignedPayloadSecurityMiddleware.TimestampItemKey] as long? ?? 0L;

        var entity = new GpsPosition
        {
            UserId = userId,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Altitude = request.Altitude,
            Accuracy = request.Accuracy,
            SequenceNumber = sequence,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(packetTimestampMs).UtcDateTime
        };

        _context.GpsPositions.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        var mapDto = await _context.GpsPositions
            .Where(p => p.Id == entity.Id)
            .Select(p => new MapUserPositionDto
            {
                PositionId = p.Id,
                UserId = p.UserId,
                Login = p.User!.Login,
                DisplayName = p.User!.DisplayName,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                Altitude = p.Altitude,
                Accuracy = p.Accuracy,
                Timestamp = p.Timestamp
            })
            .FirstAsync(cancellationToken);

        await _hubContext.SendGpsUpdateAsync(mapDto);
        return Ok(new { success = true, position = mapDto });
    }

    [HttpGet("current")]
    [Authorize(Roles = "observer,player,admin")]
    public async Task<ActionResult<IReadOnlyList<MapUserPositionDto>>> Current(CancellationToken cancellationToken)
    {
        var latestPerUser = await _context.GpsPositions
            .GroupBy(p => p.UserId)
            .Select(group => group.OrderByDescending(p => p.Timestamp).First())
            .Include(p => p.User)
            .OrderBy(p => p.User!.DisplayName)
            .Select(p => new MapUserPositionDto
            {
                PositionId = p.Id,
                UserId = p.UserId,
                Login = p.User!.Login,
                DisplayName = p.User!.DisplayName,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                Altitude = p.Altitude,
                Accuracy = p.Accuracy,
                Timestamp = p.Timestamp
            })
            .ToListAsync(cancellationToken);

        return Ok(latestPerUser);
    }

    [HttpGet("history/{userId:int}")]
    [Authorize(Roles = "observer,player,admin")]
    public async Task<ActionResult<IReadOnlyList<MapUserPositionDto>>> History(int userId, [FromQuery] int take = 500, CancellationToken cancellationToken = default)
    {
        if (User.IsInRole("player") && GetCurrentUserId() != userId)
        {
            return Forbid();
        }

        take = Math.Clamp(take, 1, 2000);
        var items = await _context.GpsPositions
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.Timestamp)
            .Take(take)
            .Include(p => p.User)
            .Select(p => new MapUserPositionDto
            {
                PositionId = p.Id,
                UserId = p.UserId,
                Login = p.User!.Login,
                DisplayName = p.User!.DisplayName,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                Altitude = p.Altitude,
                Accuracy = p.Accuracy,
                Timestamp = p.Timestamp
            })
            .ToListAsync(cancellationToken);

        items.Reverse();
        return Ok(items);
    }

    private int GetCurrentUserId()
    {
        return int.Parse(User.Claims.First(c => c.Type == "user_id" || c.Type == System.Security.Claims.ClaimTypes.NameIdentifier).Value);
    }
}