namespace SmartPanel.Shared;

public class ServiceStatusDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Online { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
