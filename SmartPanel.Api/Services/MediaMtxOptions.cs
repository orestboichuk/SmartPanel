namespace SmartPanel.Api.Services;

public class MediaMtxOptions
{
    public const string SectionName = "MediaMtx";

    public bool Enabled { get; set; } = true;
    public string BinaryPath { get; set; } = "mediamtx";
    public string WorkingDirectory { get; set; } = "C:\\Projects\\SmartPanel\\.runtime\\mediamtx";
    public string ConfigFilePath { get; set; } = "C:\\Projects\\SmartPanel\\.runtime\\mediamtx\\mediamtx.yml";
    public string LogLevel { get; set; } = "warn";
    public string ApiAddress { get; set; } = "127.0.0.1:9997";
    public string RtspAddress { get; set; } = "127.0.0.1:8554";
    public string HlsAddress { get; set; } = "127.0.0.1:8888";
    public string WebRtcAddress { get; set; } = "127.0.0.1:8889";
    public string WebRtcLocalUdpAddress { get; set; } = ":8189";
    public string WebRtcLocalTcpAddress { get; set; } = string.Empty;
    public List<string> WebRtcAdditionalHosts { get; set; } = [];
    public string WebRtcPlayerBaseUrl { get; set; } = "http://localhost:8889";
    public string HlsPlayerBaseUrl { get; set; } = "http://localhost:8888";
    public string HlsVariant { get; set; } = "lowLatency";
    public string HlsSegmentDuration { get; set; } = "1s";
    public string HlsPartDuration { get; set; } = "200ms";
    public bool HlsAlwaysRemux { get; set; } = true;
    public bool SourceOnDemand { get; set; } = false;
    public string SourceRtspTransport { get; set; } = "tcp";
}
