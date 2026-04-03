using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace StrikeballServer.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Roles = "observer,player,admin")]
public class DashboardController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public DashboardController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        return Ok(new
        {
            tileUrlTemplate = _configuration["Map:TileUrlTemplate"],
            tileAttribution = _configuration["Map:TileAttribution"] ?? "offline/local",
            defaultLatitude = _configuration.GetValue<double>("Map:DefaultLatitude", 55.751244),
            defaultLongitude = _configuration.GetValue<double>("Map:DefaultLongitude", 37.618423),
            pollIntervalMs = _configuration.GetValue<int>("Map:PollIntervalMs", 2000)
        });
    }
}