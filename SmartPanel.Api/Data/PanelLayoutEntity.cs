namespace SmartPanel.Api.Data;

public class PanelLayoutEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string LayoutJson { get; set; } = "{}";
    public bool IsDefault { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
