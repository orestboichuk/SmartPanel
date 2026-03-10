namespace SmartPanel.Api.Data;

public class DeviceDisplayOverrideEntity
{
    public int Id { get; set; }
    public string PresentationKey { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public string? DisplayNameOverride { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
