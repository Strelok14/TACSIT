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
[Route("api/detections")]
[Authorize]
public class DetectionsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IHubContext<PositioningHub> _hubContext;

    public DetectionsController(ApplicationDbContext context, IHubContext<PositioningHub> hubContext)
    {
        _context = context;
        _hubContext = hubContext;
    }

    [HttpPost]
    [Authorize(Roles = "player,admin")]
    public async Task<IActionResult> Post([FromBody] DetectionsUploadRequestDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var userId = GetCurrentUserId();
        var sequence = HttpContext.Items[SignedPayloadSecurityMiddleware.SequenceItemKey] as long? ?? 0L;
        var packetTimestampMs = HttpContext.Items[SignedPayloadSecurityMiddleware.TimestampItemKey] as long? ?? 0L;
        var packetTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(packetTimestampMs).UtcDateTime;

        var entities = request.Detections.Select(item => new DetectedPerson
        {
            UserId = userId,
            TargetUserId = item.TargetUserId,
            IsAlly = item.IsAlly,
            Label = item.Label,
            SkeletonData = item.SkeletonData,
            Latitude = item.Latitude,
            Longitude = item.Longitude,
            Altitude = item.Altitude,
            Accuracy = item.Accuracy,
            SequenceNumber = sequence,
            Timestamp = packetTimestamp
        }).ToList();

        _context.DetectedPersons.AddRange(entities);
        await _context.SaveChangesAsync(cancellationToken);

        var responseItems = await _context.DetectedPersons
            .Where(d => entities.Select(x => x.Id).Contains(d.Id))
            .Include(d => d.User)
            .OrderByDescending(d => d.Timestamp)
            .Select(d => new MapDetectionDto
            {
                DetectionId = d.Id,
                UserId = d.UserId,
                ReporterLogin = d.User!.Login,
                TargetUserId = d.TargetUserId,
                IsAlly = d.IsAlly,
                Label = d.Label,
                SkeletonData = d.SkeletonData,
                Latitude = d.Latitude,
                Longitude = d.Longitude,
                Altitude = d.Altitude,
                Accuracy = d.Accuracy,
                Timestamp = d.Timestamp
            })
            .ToListAsync(cancellationToken);

        await _hubContext.SendDetectionsAsync(responseItems);
        return Ok(new { success = true, detections = responseItems });
    }

    [HttpGet("recent")]
    [Authorize(Roles = "observer,player,admin")]
    public async Task<ActionResult<IReadOnlyList<MapDetectionDto>>> Recent([FromQuery] int take = 200, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 1000);
        var currentUserId = GetCurrentUserId();
        var query = _context.DetectedPersons
            .Include(d => d.User)
            .OrderByDescending(d => d.Timestamp)
            .AsQueryable();

        if (User.IsInRole("player"))
        {
            query = query.Where(d => d.UserId == currentUserId || d.TargetUserId == currentUserId);
        }

        var items = await query
            .Take(take)
            .Select(d => new MapDetectionDto
            {
                DetectionId = d.Id,
                UserId = d.UserId,
                ReporterLogin = d.User!.Login,
                TargetUserId = d.TargetUserId,
                IsAlly = d.IsAlly,
                Label = d.Label,
                SkeletonData = d.SkeletonData,
                Latitude = d.Latitude,
                Longitude = d.Longitude,
                Altitude = d.Altitude,
                Accuracy = d.Accuracy,
                Timestamp = d.Timestamp
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    private int GetCurrentUserId()
    {
        return int.Parse(User.Claims.First(c => c.Type == "user_id" || c.Type == System.Security.Claims.ClaimTypes.NameIdentifier).Value);
    }
}