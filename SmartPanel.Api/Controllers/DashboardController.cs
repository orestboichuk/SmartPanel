using Microsoft.AspNetCore.Mvc;
using SmartPanel.Api.Services;

namespace SmartPanel.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly ISmartHomeService _service;

    public DashboardController(ISmartHomeService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        return Ok(await _service.GetDashboardAsync());
    }

    [HttpPost("clear-data")]
    public async Task<IActionResult> ClearData()
    {
        await _service.ClearAllDataAsync();
        return Ok(new { message = "All data cleared." });
    }

    [HttpPost("clear-and-resync")]
    public async Task<IActionResult> ClearAndResync()
    {
        await _service.ClearAllDataAndResyncAsync();
        return Ok(new { message = "Data cleared and synchronized from Home Assistant." });
    }
}
