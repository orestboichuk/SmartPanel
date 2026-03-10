namespace SmartPanel.Api.Data;

public class AppUserRoleEntity
{
    public int UserId { get; set; }
    public int RoleId { get; set; }

    public AppUserEntity User { get; set; } = null!;
    public AppRoleEntity Role { get; set; } = null!;
}
