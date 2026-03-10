namespace SmartPanel.Shared;

public class ClimateDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsOn { get; set; }
    public bool Online { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public string CurrentMode { get; set; } = string.Empty;
    public double? CurrentTemperature { get; set; }
    public double? TargetTemperature { get; set; }
    public List<string> AvailableModes { get; set; } = [];
    public DateTime UpdatedAt { get; set; }
}
