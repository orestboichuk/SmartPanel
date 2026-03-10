using Microsoft.AspNetCore.Mvc;
using SmartPanel.Api.Services;

namespace SmartPanel.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HomeAssistantTestController : ControllerBase
{
    private readonly HomeAssistantClient _client;

    public HomeAssistantTestController(HomeAssistantClient client)
    {
        _client = client;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var result = await _client.GetApiStatusAsync();
        return Ok(result);
    }

    [HttpGet("states")]
    public async Task<IActionResult> States()
    {
        var result = await _client.GetStatesRawAsync();
        return Content(result, "application/json");
    }
}