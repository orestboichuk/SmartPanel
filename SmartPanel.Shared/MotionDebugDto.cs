namespace SmartPanel.Shared;

public class MotionDebugDto
{
    public string EntityId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string CurrentState { get; set; } = string.Empty;
    public DateTime? LastChanged { get; set; }
    public DateTime? LastUpdated { get; set; }
    public DateTime? LastReported { get; set; }
    public bool Online { get; set; }
    public int Hours { get; set; }
    public int Limit { get; set; }
    public List<MotionDebugPointDto> HistoryPoints { get; set; } = [];
}
