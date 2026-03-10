namespace SmartPanel.Shared;

public class CameraControlRequestDto
{
    public string EntityId { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public double? Step { get; set; }
    public int? DurationSeconds { get; set; }
}
