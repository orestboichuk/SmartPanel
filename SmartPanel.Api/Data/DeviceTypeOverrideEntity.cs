namespace SmartPanel.Api.Data;

public class DeviceTypeOverrideEntity
{
    public int Id { get; set; }
    public string PresentationKey { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string OriginalType { get; set; } = string.Empty;
    public string? TypeOverride { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
