namespace SmartPanel.Shared;

public class CameraDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OriginalName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? DisplayNameOverride { get; set; }
    public bool Online { get; set; }
    public string SnapshotUrl { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string? RoomName { get; set; }
    public List<string> Capabilities { get; set; } = [];
}
