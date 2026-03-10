namespace SmartPanel.Api.Data;

public class DeviceRoomBindingEntity
{
    public int Id { get; set; }
    public string DeviceKey { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
