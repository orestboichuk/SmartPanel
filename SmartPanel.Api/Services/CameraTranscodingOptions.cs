namespace SmartPanel.Api.Services;

public class CameraTranscodingOptions
{
    public const string SectionName = "CameraTranscoding";

    public bool Enabled { get; set; } = true;
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string RtspTransport { get; set; } = "udp";
    public int SnapshotTimeoutSeconds { get; set; } = 12;
    public int MjpegFrameRate { get; set; } = 10;
    public int MjpegQuality { get; set; } = 8;
}
