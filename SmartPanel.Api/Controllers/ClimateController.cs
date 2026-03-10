using Microsoft.AspNetCore.Mvc;
using SmartPanel.Api.Services;
using SmartPanel.Shared;

namespace SmartPanel.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClimateController : ControllerBase
{
    private readonly ISmartHomeService _service;

    public ClimateController(ISmartHomeService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        return Ok(await _service.GetClimateDevicesAsync());
    }

    [HttpPost("control")]
    public async Task<IActionResult> Control([FromBody] ClimateControlRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.EntityId))
            return BadRequest("entityId is required.");

        await _service.ControlClimateAsync(request);
        return Ok(new { message = "Climate command sent." });
    }
}
