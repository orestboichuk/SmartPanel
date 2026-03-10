namespace SmartPanel.Api.Data;

public class HomeAssistantEntitySnapshotEntity
{
    public int Id { get; set; }
    public string EntityId { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string DeviceKey { get; set; } = string.Empty;
    public string? RegistryDeviceId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? RoomName { get; set; }
    public string RawState { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public double? NumericValue { get; set; }
    public bool IsOnline { get; set; }
    public string AttributesJson { get; set; } = "{}";
    public string CapabilitiesJson { get; set; } = "[]";
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
