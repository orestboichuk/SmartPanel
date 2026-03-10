using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartPanel.Api.Auth;
using SmartPanel.Api.Data;

namespace SmartPanel.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly SmartPanelDbContext _db;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthController(SmartPanelDbContext db, IJwtTokenService jwtTokenService)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Username and password are required.");
        }

        var user = await _db.AppUsers
            .Include(x => x.UserRoles)
            .ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.Username == request.Username.Trim());

        if (user is null || !user.IsActive)
        {
            return Unauthorized("Invalid credentials.");
        }

        var isValid = PasswordHasher.VerifyPassword(request.Password, user.PasswordHash, user.PasswordSalt);
        if (!isValid)
        {
            return Unauthorized("Invalid credentials.");
        }

        var roles = user.UserRoles.Select(x => x.Role.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var token = _jwtTokenService.GenerateToken(user, roles);

        return Ok(new LoginResponse(token, user.Username, roles));
    }

    [HttpGet("me")]
    [Authorize]
    public ActionResult<object> Me()
    {
        var username = User.Identity?.Name ?? "unknown";
        var roles = User.Claims
            .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Ok(new { username, roles });
    }

    public sealed record LoginRequest(string Username, string Password);
    public sealed record LoginResponse(string Token, string Username, IReadOnlyCollection<string> Roles);
}
