namespace SmartPanel.Shared;

public class DeviceDisplayNameRequestDto
{
    public string EntityId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}
