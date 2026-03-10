namespace SmartPanel.Shared;

public class DeviceTypeOverrideDto
{
    public string EntityId { get; set; } = string.Empty;
    public string PresentationKey { get; set; } = string.Empty;
    public string OriginalType { get; set; } = string.Empty;
    public string? TypeOverride { get; set; }
    public string EffectiveType { get; set; } = string.Empty;
}
