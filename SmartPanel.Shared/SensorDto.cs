namespace SmartPanel.Shared;

public class SensorDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OriginalName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double? Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public bool Online { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
