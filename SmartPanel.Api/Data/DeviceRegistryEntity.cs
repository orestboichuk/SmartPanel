namespace SmartPanel.Api.Data;

public class DeviceRegistryEntity
{
    public int Id { get; set; }
    public string EntityId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
