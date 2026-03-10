namespace SmartPanel.Shared;

public class DirectCameraSaveRequestDto
{
    public string? CameraId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int? Port { get; set; }
    public bool UseTls { get; set; }
    public string DeviceServiceUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string PreferredProfileToken { get; set; } = string.Empty;
    public string PreferredLiveProfileToken { get; set; } = string.Empty;
    public string SnapshotUrlOverride { get; set; } = string.Empty;
    public string MjpegUrlOverride { get; set; } = string.Empty;
}
