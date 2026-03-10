namespace SmartPanel.Shared;

public class DirectCameraProfileDto
{
    public string Token { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool SupportsPtz { get; set; }
}
