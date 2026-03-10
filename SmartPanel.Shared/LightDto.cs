namespace SmartPanel.Shared;

public class LightDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsOn { get; set; }
    public bool Online { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public int? Brightness { get; set; }
    public DateTime UpdatedAt { get; set; }
}
