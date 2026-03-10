using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartPanel.Api.Data;

namespace SmartPanel.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/devices")]
public class AdminDevicesController : ControllerBase
{
    private readonly SmartPanelDbContext _db;

    public AdminDevicesController(SmartPanelDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<DeviceRegistryDto>>> GetAll()
    {
        var devices = await _db.DeviceRegistry
            .AsNoTracking()
            .OrderBy(x => x.RoomName)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName)
            .Select(x => new DeviceRegistryDto(
                x.Id,
                x.EntityId,
                x.DisplayName,
                x.RoomName,
                x.Type,
                x.IsVisible,
                x.SortOrder,
                x.UpdatedAtUtc))
            .ToListAsync();

        return Ok(devices);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<DeviceRegistryDto>> Upsert(int id, [FromBody] DeviceRegistryUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EntityId) ||
            string.IsNullOrWhiteSpace(request.DisplayName) ||
            string.IsNullOrWhiteSpace(request.RoomName) ||
            string.IsNullOrWhiteSpace(request.Type))
        {
            return BadRequest("EntityId, DisplayName, RoomName and Type are required.");
        }

        var entityId = request.EntityId.Trim();
        var duplicate = await _db.DeviceRegistry
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.EntityId == entityId && x.Id != id);
        if (duplicate is not null)
        {
            return Conflict("Device with this EntityId already exists.");
        }

        var entity = await _db.DeviceRegistry.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null)
        {
            entity = new DeviceRegistryEntity();
            _db.DeviceRegistry.Add(entity);
        }

        entity.EntityId = entityId;
        entity.DisplayName = request.DisplayName.Trim();
        entity.RoomName = request.RoomName.Trim();
        entity.Type = request.Type.Trim().ToLowerInvariant();
        entity.IsVisible = request.IsVisible;
        entity.SortOrder = request.SortOrder;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return Ok(new DeviceRegistryDto(
            entity.Id,
            entity.EntityId,
            entity.DisplayName,
            entity.RoomName,
            entity.Type,
            entity.IsVisible,
            entity.SortOrder,
            entity.UpdatedAtUtc));
    }

    public sealed record DeviceRegistryUpsertRequest(
        string EntityId,
        string DisplayName,
        string RoomName,
        string Type,
        bool IsVisible,
        int SortOrder);

    public sealed record DeviceRegistryDto(
        int Id,
        string EntityId,
        string DisplayName,
        string RoomName,
        string Type,
        bool IsVisible,
        int SortOrder,
        DateTime UpdatedAtUtc);
}
