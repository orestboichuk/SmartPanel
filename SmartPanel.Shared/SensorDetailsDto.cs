namespace SmartPanel.Shared;

public class SensorDetailsDto
{
    public string EntityId { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameOverride { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string? DeviceClass { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public string RawState { get; set; } = string.Empty;
    public string DisplayState { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public double? CurrentValue { get; set; }
    public bool Online { get; set; }
    public DateTime RequestedAt { get; set; }
    public string? CurrentMode { get; set; }
    public double? TargetTemperature { get; set; }
    public List<string> AvailableModes { get; set; } = [];
    public List<string> Capabilities { get; set; } = [];
    public string? Source { get; set; }
    public List<string> AvailableSources { get; set; } = [];
    public string? App { get; set; }
    public int? VolumePercent { get; set; }
    public bool? Muted { get; set; }
    public double? HumidityValue { get; set; }
    public string? HumidityUnit { get; set; }
    public double? BatteryLevel { get; set; }
    public List<SensorHistoryPointDto> TemperatureHistory { get; set; } = [];
    public List<SensorHistoryPointDto> HumidityHistory { get; set; } = [];
    public List<SensorHistoryPointDto> History { get; set; } = [];
}
