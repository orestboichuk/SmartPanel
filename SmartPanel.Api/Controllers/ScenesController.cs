using Microsoft.AspNetCore.Mvc;
using SmartPanel.Api.Services;

namespace SmartPanel.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ScenesController : ControllerBase
{
    private readonly ISmartHomeService _service;

    public ScenesController(ISmartHomeService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        return Ok(await _service.GetScenesAsync());
    }

    [HttpPost("{sceneName}/activate")]
    public async Task<IActionResult> Activate(string sceneName)
    {
        await _service.ActivateSceneAsync(sceneName);
        return Ok(new { message = $"Scene {sceneName} activated" });
    }
}
