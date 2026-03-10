namespace SmartPanel.Shared;

public class LightControlRequestDto
{
    public string EntityId { get; set; } = string.Empty;
    public bool? TurnOn { get; set; }
    public int? Brightness { get; set; }
}
