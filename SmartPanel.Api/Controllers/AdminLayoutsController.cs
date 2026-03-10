using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartPanel.Api.Data;

namespace SmartPanel.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/layouts")]
public class AdminLayoutsController : ControllerBase
{
    private readonly SmartPanelDbContext _db;

    public AdminLayoutsController(SmartPanelDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<PanelLayoutDto>>> GetAll()
    {
        var layouts = await _db.PanelLayouts
            .AsNoTracking()
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .Select(x => new PanelLayoutDto(x.Id, x.Name, x.Route, x.LayoutJson, x.IsDefault, x.UpdatedAtUtc))
            .ToListAsync();

        return Ok(layouts);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<PanelLayoutDto>> Upsert(int id, [FromBody] PanelLayoutUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Route))
        {
            return BadRequest("Name and Route are required.");
        }

        var route = request.Route.Trim();
        var duplicate = await _db.PanelLayouts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Route == route && x.Id != id);
        if (duplicate is not null)
        {
            return Conflict("Layout route must be unique.");
        }

        var entity = await _db.PanelLayouts.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null)
        {
            entity = new PanelLayoutEntity();
            _db.PanelLayouts.Add(entity);
        }

        entity.Name = request.Name.Trim();
        entity.Route = route;
        entity.LayoutJson = string.IsNullOrWhiteSpace(request.LayoutJson) ? "{}" : request.LayoutJson;
        entity.IsDefault = request.IsDefault;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        if (entity.IsDefault)
        {
            var otherDefaults = await _db.PanelLayouts
                .Where(x => x.Id != entity.Id && x.IsDefault)
                .ToListAsync();
            foreach (var other in otherDefaults)
            {
                other.IsDefault = false;
                other.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new PanelLayoutDto(entity.Id, entity.Name, entity.Route, entity.LayoutJson, entity.IsDefault, entity.UpdatedAtUtc));
    }

    public sealed record PanelLayoutUpsertRequest(string Name, string Route, string LayoutJson, bool IsDefault);
    public sealed record PanelLayoutDto(int Id, string Name, string Route, string LayoutJson, bool IsDefault, DateTime UpdatedAtUtc);
}
