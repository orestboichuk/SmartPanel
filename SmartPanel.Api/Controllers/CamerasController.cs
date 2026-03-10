using Microsoft.AspNetCore.Mvc;
using SmartPanel.Api.Services;
using SmartPanel.Shared;

namespace SmartPanel.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CamerasController : ControllerBase
{
    private readonly ISmartHomeService _service;
    private readonly HomeAssistantClient _client;
    private readonly IDirectCameraService _directCameraService;

    public CamerasController(
        ISmartHomeService service,
        HomeAssistantClient client,
        IDirectCameraService directCameraService)
    {
        _service = service;
        _client = client;
        _directCameraService = directCameraService;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        return Ok(await _service.GetCamerasAsync());
    }

    [HttpPost("ha/resync")]
    public async Task<IActionResult> ResyncHa()
    {
        await _service.ClearAllDataAndResyncAsync();
        return Ok(new { message = "HA data resynced." });
    }

    [HttpDelete("ha/{entityId}")]
    public async Task<IActionResult> RemoveHaEntity(string entityId)
    {
        await _service.RemoveHaEntityAsync(entityId);
        return NoContent();
    }

    [HttpGet("direct")]
    public async Task<IActionResult> GetDirect(CancellationToken cancellationToken)
    {
        return Ok(await _directCameraService.GetDirectCamerasAsync(cancellationToken));
    }

    [HttpPost("direct/discover")]
    public async Task<IActionResult> DiscoverDirect(CancellationToken cancellationToken)
    {
        return Ok(await _directCameraService.DiscoverAsync(cancellationToken));
    }

    [HttpPost("direct/manual")]
    public async Task<IActionResult> SaveManual([FromBody] DirectCameraSaveRequestDto request, CancellationToken cancellationToken)
    {
        return Ok(await _directCameraService.SaveManualCameraAsync(request, cancellationToken));
    }

    [HttpPost("direct/{cameraId}/refresh")]
    public async Task<IActionResult> RefreshDirect(string cameraId, CancellationToken cancellationToken)
    {
        var camera = await _directCameraService.RefreshCameraAsync(cameraId, cancellationToken);
        return camera is null ? NotFound() : Ok(camera);
    }

    [HttpDelete("direct/{cameraId}")]
    public async Task<IActionResult> DeleteDirect(string cameraId, CancellationToken cancellationToken)
    {
        await _directCameraService.DeleteCameraAsync(cameraId, cancellationToken);
        return NoContent();
    }

    [HttpGet("snapshot")]
    public async Task<IActionResult> Snapshot([FromQuery] string entityId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return BadRequest("entityId is required.");
        }

        var directCameraId = _directCameraService.TryGetCameraId(entityId);
        if (directCameraId is not null)
        {
            var snapshot = await _directCameraService.GetSnapshotAsync(directCameraId, cancellationToken);
            return File(snapshot.Bytes, snapshot.ContentType);
        }

        using var upstream = await _client.GetCameraSnapshotAsync(entityId);
        if (!upstream.IsSuccessStatusCode)
        {
            var details = await upstream.Content.ReadAsStringAsync();
            return StatusCode((int)upstream.StatusCode, string.IsNullOrWhiteSpace(details) ? "Unable to load snapshot." : details);
        }

        var bytes = await upstream.Content.ReadAsByteArrayAsync();
        var contentType = upstream.Content.Headers.ContentType?.ToString() ?? "image/jpeg";
        return File(bytes, contentType);
    }

    [HttpGet("stream")]
    public async Task<IActionResult> Stream([FromQuery] string entityId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return BadRequest("entityId is required.");
        }

        var directCameraId = _directCameraService.TryGetCameraId(entityId);
        if (directCameraId is not null)
        {
            Response.ContentType = await _directCameraService.GetLiveViewContentTypeAsync(directCameraId, cancellationToken);
            Response.StatusCode = 200;
            Response.Headers.CacheControl = "no-store, no-cache";
            await _directCameraService.StreamLiveViewAsync(directCameraId, Response.Body, cancellationToken);
            return new EmptyResult();
        }

        using var upstream = await _client.GetCameraStreamAsync(entityId);
        if (!upstream.IsSuccessStatusCode)
        {
            var details = await upstream.Content.ReadAsStringAsync();
            return StatusCode((int)upstream.StatusCode, string.IsNullOrWhiteSpace(details) ? "Unable to open stream." : details);
        }

        var contentType = upstream.Content.Headers.ContentType?.ToString() ?? "multipart/x-mixed-replace";
        Response.ContentType = contentType;
        Response.StatusCode = 200;
        await using var sourceStream = await upstream.Content.ReadAsStreamAsync();
        await sourceStream.CopyToAsync(Response.Body);
        return new EmptyResult();
    }

    [HttpPost("control")]
    public async Task<IActionResult> Control([FromBody] CameraControlRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EntityId))
        {
            return BadRequest("entityId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return BadRequest("command is required.");
        }

        var directCameraId = _directCameraService.TryGetCameraId(request.EntityId);
        if (directCameraId is not null)
        {
            await _directCameraService.ControlCameraAsync(directCameraId, request, cancellationToken);
            return Ok(new { message = "Direct camera command sent" });
        }

        await _service.ControlCameraAsync(request);
        return Ok(new { message = "Camera command sent" });
    }
}
