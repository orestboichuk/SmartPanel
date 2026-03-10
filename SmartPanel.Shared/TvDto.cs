namespace SmartPanel.Shared;

public class TvDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public bool Online { get; set; } = true;
    public string? RoomName { get; set; }
    public string? Source { get; set; }
    public List<string> AvailableSources { get; set; } = [];
    public string? App { get; set; }
    public int? VolumePercent { get; set; }
    public bool? Muted { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
