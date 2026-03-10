namespace SmartPanel.Api.Data;

public class DirectCameraEntity
{
    public int Id { get; set; }
    public string CameraId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Scheme { get; set; } = "http";
    public string DeviceServiceUrl { get; set; } = string.Empty;
    public string? MediaServiceUrl { get; set; }
    public string? MediaNamespace { get; set; }
    public string? PtzServiceUrl { get; set; }
    public string? SnapshotUri { get; set; }
    public string? StreamUri { get; set; }
    public string? SnapshotUrlOverride { get; set; }
    public string? MjpegUrlOverride { get; set; }
    public string? SelectedProfileToken { get; set; }
    public string? PreferredLiveProfileToken { get; set; }
    public string? LiveProfileToken { get; set; }
    public string? LiveSnapshotUri { get; set; }
    public string? LiveStreamUri { get; set; }
    public string? Username { get; set; }
    public string? PasswordProtected { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? SerialNumber { get; set; }
    public string? HardwareId { get; set; }
    public string ProfilesJson { get; set; } = "[]";
    public string CapabilitiesJson { get; set; } = "[]";
    public bool Online { get; set; }
    public string? LastError { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
