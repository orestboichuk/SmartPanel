namespace SmartPanel.Shared;

public class DeviceDisplayNameDto
{
    public string EntityId { get; set; } = string.Empty;
    public string PresentationKey { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public string? DisplayNameOverride { get; set; }
    public string EffectiveDisplayName { get; set; } = string.Empty;
}
