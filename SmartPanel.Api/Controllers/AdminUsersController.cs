using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartPanel.Api.Data;

namespace SmartPanel.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/users")]
public class AdminUsersController : ControllerBase
{
    private readonly SmartPanelDbContext _db;

    public AdminUsersController(SmartPanelDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<UserDto>>> GetUsers()
    {
        var users = await _db.AppUsers
            .AsNoTracking()
            .Include(x => x.UserRoles)
            .ThenInclude(x => x.Role)
            .OrderBy(x => x.Username)
            .ToListAsync();

        return Ok(users.Select(x => new UserDto(
            x.Id,
            x.Username,
            x.IsActive,
            x.UserRoles.Select(ur => ur.Role.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            x.UpdatedAtUtc
        )).ToList());
    }

    public sealed record UserDto(
        int Id,
        string Username,
        bool IsActive,
        IReadOnlyCollection<string> Roles,
        DateTime UpdatedAtUtc);
}
