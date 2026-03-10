namespace SmartPanel.Shared;

public class SensorHistoryPointDto
{
    public DateTime Timestamp { get; set; }
    public double? Value { get; set; }
    public string State { get; set; } = string.Empty;
}
