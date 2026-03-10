using Microsoft.AspNetCore.Mvc;
using SmartPanel.Api.Services;
using SmartPanel.Shared;

namespace SmartPanel.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SensorsController : ControllerBase
{
    private readonly ISmartHomeService _service;

    public SensorsController(ISmartHomeService service)
    {
        _service = service;
    }

    [HttpGet("details")]
    public async Task<IActionResult> GetDetails([FromQuery] string entityId, [FromQuery] int hours = 24)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return BadRequest("entityId is required.");
        }

        var details = await _service.GetSensorDetailsAsync(entityId, hours);
        if (details is null)
        {
            return NotFound();
        }

        return Ok(details);
    }

    [HttpGet("motion/debug")]
    public async Task<IActionResult> MotionDebug([FromQuery] string entityId, [FromQuery] int hours = 24, [FromQuery] int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return BadRequest("entityId is required.");
        }

        var debug = await _service.GetMotionDebugAsync(entityId, hours, limit);
        if (debug is null)
        {
            return NotFound();
        }

        return Ok(debug);
    }

    [HttpPost("climate/control")]
    public async Task<IActionResult> ControlClimate([FromBody] ClimateControlRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.EntityId))
        {
            return BadRequest("entityId is required.");
        }

        if (request.PowerOn is null && string.IsNullOrWhiteSpace(request.HvacMode) && !request.TargetTemperature.HasValue)
        {
            return BadRequest("At least one control field is required.");
        }

        await _service.ControlClimateAsync(request);
        return Ok(new { message = "Climate command sent" });
    }
}
