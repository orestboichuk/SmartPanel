namespace SmartPanel.Shared;

public class DirectCameraDto
{
    public string CameraId { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Scheme { get; set; } = "http";
    public string DeviceServiceUrl { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? SerialNumber { get; set; }
    public string? HardwareId { get; set; }
    public string? Username { get; set; }
    public bool HasPassword { get; set; }
    public bool Online { get; set; }
    public string? SnapshotUrlOverride { get; set; }
    public string? MjpegUrlOverride { get; set; }
    public string SnapshotUrl { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string WebRtcPlayerUrl { get; set; } = string.Empty;
    public string HlsPlayerUrl { get; set; } = string.Empty;
    public string RawStreamUrl { get; set; } = string.Empty;
    public string? SelectedProfileToken { get; set; }
    public string? PreferredLiveProfileToken { get; set; }
    public string? LiveProfileToken { get; set; }
    public List<DirectCameraProfileDto> Profiles { get; set; } = [];
    public List<string> Capabilities { get; set; } = [];
    public string? LastError { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
