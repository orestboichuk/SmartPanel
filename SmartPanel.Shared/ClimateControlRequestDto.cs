namespace SmartPanel.Shared;

public class ClimateControlRequestDto
{
    public string EntityId { get; set; } = string.Empty;
    public string? HvacMode { get; set; }
    public double? TargetTemperature { get; set; }
    public bool? PowerOn { get; set; }
}
