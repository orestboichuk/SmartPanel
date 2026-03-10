namespace SmartPanel.Shared;

public class TvControlRequestDto
{
    public string EntityId { get; set; } = string.Empty;
    public bool? PowerOn { get; set; }
    public bool? TogglePlayPause { get; set; }
    public bool? VolumeUp { get; set; }
    public bool? VolumeDown { get; set; }
    public int? VolumePercent { get; set; }
    public bool? Mute { get; set; }
    public string? Source { get; set; }
}
