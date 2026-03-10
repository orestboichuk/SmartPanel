namespace SmartPanel.Shared;

public class DashboardDto
{
    public DateTime CurrentTime { get; set; }
    public int AlertsCount { get; set; }
    public string HouseStatus { get; set; } = string.Empty;
    public List<SceneDto> Scenes { get; set; } = new();
    public List<ServiceStatusDto> Services { get; set; } = new();
}
