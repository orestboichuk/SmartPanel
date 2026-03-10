using Microsoft.AspNetCore.Mvc;
using SmartPanel.Api.Services;
using SmartPanel.Shared;

namespace SmartPanel.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TvsController : ControllerBase
{
    private readonly ISmartHomeService _service;

    public TvsController(ISmartHomeService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        return Ok(await _service.GetTvsAsync());
    }

    [HttpPost("control")]
    public async Task<IActionResult> Control([FromBody] TvControlRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.EntityId))
        {
            return BadRequest("entityId is required.");
        }

        if (request.PowerOn is null &&
            request.TogglePlayPause is null &&
            request.VolumeUp is null &&
            request.VolumeDown is null &&
            !request.VolumePercent.HasValue &&
            request.Mute is null &&
            string.IsNullOrWhiteSpace(request.Source))
        {
            return BadRequest("At least one control field is required.");
        }

        await _service.ControlTvAsync(request);
        return Ok(new { message = "TV command sent" });
    }
}
