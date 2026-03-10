namespace SmartPanel.Api.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "SmartPanel.Api";
    public string Audience { get; set; } = "SmartPanel.Client";
    public string Key { get; set; } = "CHANGE_ME_SMARTPANEL_DEVELOPMENT_KEY_32+";
    public int ExpireMinutes { get; set; } = 480;
}
