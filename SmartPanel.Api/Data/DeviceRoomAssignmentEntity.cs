namespace SmartPanel.Api.Data;

public class DeviceRoomAssignmentEntity
{
    public int Id { get; set; }
    public string EntityId { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
