namespace SmartPanel.Shared;

public class DirectCameraDiscoveryDto
{
    public string DiscoveryId { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string DeviceServiceUrl { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? Hardware { get; set; }
    public string Types { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = [];
}
