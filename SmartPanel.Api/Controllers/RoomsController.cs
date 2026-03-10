using Microsoft.AspNetCore.Mvc;
using SmartPanel.Api.Services;
using SmartPanel.Shared;

namespace SmartPanel.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RoomsController : ControllerBase
{
    private readonly ISmartHomeService _service;

    public RoomsController(ISmartHomeService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        return Ok(await _service.GetRoomsAsync());
    }

    [HttpPost("assign")]
    public async Task<IActionResult> Assign([FromBody] DeviceRoomAssignmentRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.EntityId))
        {
            return BadRequest("entityId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.RoomName))
        {
            return BadRequest("roomName is required.");
        }

        await _service.AssignDeviceToRoomAsync(request.EntityId, request.RoomName);
        return Ok(new { message = "Device assigned." });
    }

    [HttpPost("display-name")]
    public async Task<IActionResult> UpdateDisplayName([FromBody] DeviceDisplayNameRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.EntityId))
        {
            return BadRequest("entityId is required.");
        }

        var result = await _service.UpdateDeviceDisplayNameAsync(request);
        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpPost("device-type")]
    public async Task<IActionResult> UpdateDeviceType([FromBody] DeviceTypeOverrideRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.EntityId))
        {
            return BadRequest("entityId is required.");
        }

        var result = await _service.UpdateDeviceTypeOverrideAsync(request);
        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }
}
