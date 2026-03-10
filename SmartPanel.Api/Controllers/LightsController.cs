using Microsoft.AspNetCore.Mvc;
using SmartPanel.Api.Services;
using SmartPanel.Shared;

namespace SmartPanel.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LightsController : ControllerBase
{
    private readonly ISmartHomeService _service;

    public LightsController(ISmartHomeService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        return Ok(await _service.GetLightsAsync());
    }

    [HttpPost("control")]
    public async Task<IActionResult> Control([FromBody] LightControlRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.EntityId))
            return BadRequest("entityId is required.");

        await _service.ControlLightAsync(request);
        return Ok(new { message = "Light command sent." });
    }
}
