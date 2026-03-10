namespace SmartPanel.Shared;

public class RoomDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public List<SensorDto> Sensors { get; set; } = new();
}