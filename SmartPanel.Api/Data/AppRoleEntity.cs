namespace SmartPanel.Api.Data;

public class AppRoleEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<AppUserRoleEntity> UserRoles { get; set; } = new List<AppUserRoleEntity>();
}
