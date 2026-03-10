using SmartPanel.Shared;

namespace SmartPanel.Api.Services;

public class MockSmartHomeService : ISmartHomeService
{
    private string _activeScene = "Home";
    private string _climateMode = "cool";
    private double _climateTarget = 23;
    private bool _livingRoomTvOn = true;
    private bool _bedroomTvOn;
    private int _livingRoomTvVolume = 22;
    private int _bedroomTvVolume = 10;
    private readonly Dictionary<string, string> _roomAssignments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _typeOverrides = new(StringComparer.OrdinalIgnoreCase);

    public Task<DashboardDto> GetDashboardAsync()
    {
        var dashboard = new DashboardDto
        {
            CurrentTime = DateTime.Now,
            AlertsCount = 2,
            HouseStatus = _activeScene switch
            {
                "Away" => "Armed Away",
                "Night" => "Night Mode",
                _ => "Home"
            },
            Scenes = BuildScenes(),
            Services =
            [
                new ServiceStatusDto { Id = "sensor.sun_next_dusk", Name = "Sun Наступні сутінки", Kind = "sun", Value = DateTimeOffset.Now.AddHours(2).ToString("yyyy-MM-ddTHH:mm:sszzz"), Online = true, UpdatedAt = DateTime.Now },
                new ServiceStatusDto { Id = "sensor.sun_next_midnight", Name = "Sun Наступна північ", Kind = "sun", Value = DateTimeOffset.Now.AddHours(6).ToString("yyyy-MM-ddTHH:mm:sszzz"), Online = true, UpdatedAt = DateTime.Now },
                new ServiceStatusDto { Id = "sensor.sun_next_rising", Name = "Sun Наступний схід", Kind = "sun", Value = DateTimeOffset.Now.AddHours(10).ToString("yyyy-MM-ddTHH:mm:sszzz"), Online = true, UpdatedAt = DateTime.Now }
            ]
        };

        return Task.FromResult(dashboard);
    }

    public Task<List<RoomDto>> GetRoomsAsync()
    {
        var now = DateTime.Now;
        var rooms = new List<RoomDto>
        {
            new()
            {
                Name = "Living Room",
                Sensors =
                [
                    new SensorDto { Id = "binary_sensor.motion_living", Name = "Motion Sensor", Type = "motion", Status = "clear", Online = true, UpdatedAt = now },
                    new SensorDto { Id = "sensor.temp_living", Name = "Temperature", Type = "temp", Value = 22.5, Unit = "°C", Online = true, UpdatedAt = now },
                    new SensorDto { Id = "sensor.hum_living", Name = "Humidity", Type = "humidity", Value = 48, Unit = "%", Online = true, UpdatedAt = now }
                ]
            },
            new()
            {
                Name = "Kitchen",
                Sensors =
                [
                    new SensorDto { Id = "sensor.temp_kitchen", Name = "Temperature", Type = "temp", Value = 24.1, Unit = "°C", Online = true, UpdatedAt = now },
                    new SensorDto { Id = "sensor.hum_kitchen", Name = "Humidity", Type = "humidity", Value = 52, Unit = "%", Online = true, UpdatedAt = now },
                    new SensorDto { Id = "binary_sensor.leak_kitchen", Name = "Leak Sensor", Type = "leak", Status = "clear", Online = true, UpdatedAt = now }
                ]
            }
        };

        return Task.FromResult(rooms);
    }

    public Task<List<CameraDto>> GetCamerasAsync()
    {
        var cameras = new List<CameraDto>
        {
            new() { Name = "Front Door Camera", Online = true, SnapshotUrl = "/images/mock-camera.jpg", StreamUrl = "rtsp://example.local/stream1" },
            new() { Name = "Garage Camera", Online = false, SnapshotUrl = "/images/mock-camera.jpg", StreamUrl = string.Empty }
        };

        return Task.FromResult(cameras);
    }

    public Task<List<TvDto>> GetTvsAsync()
    {
        var livingRoomName = _roomAssignments.GetValueOrDefault("media_player.living_room_tv", "Living Room");
        var bedroomName = _roomAssignments.GetValueOrDefault("media_player.bedroom_tv", "Bedroom");
        var tvs = new List<TvDto>
        {
            new() { Id = "media_player.living_room_tv", Name = "Living Room TV", State = _livingRoomTvOn ? "on" : "off", Online = true, RoomName = livingRoomName, Source = "HDMI 1", AvailableSources = ["HDMI 1", "Chromecast", "YouTube"], App = _livingRoomTvOn ? "YouTube" : null, VolumePercent = _livingRoomTvVolume, Muted = false, UpdatedAt = DateTime.Now },
            new() { Id = "media_player.bedroom_tv", Name = "Bedroom TV", State = _bedroomTvOn ? "on" : "off", Online = true, RoomName = bedroomName, Source = "Chromecast", AvailableSources = ["Chromecast", "HDMI 2"], App = null, VolumePercent = _bedroomTvVolume, Muted = true, UpdatedAt = DateTime.Now }
        };

        return Task.FromResult(tvs);
    }

    public Task<List<SceneDto>> GetScenesAsync()
    {
        return Task.FromResult(BuildScenes());
    }

    public Task ActivateSceneAsync(string sceneName)
    {
        _activeScene = sceneName;
        return Task.CompletedTask;
    }

    public Task ControlClimateAsync(ClimateControlRequestDto request)
    {
        if (request.PowerOn == false)
        {
            _climateMode = "off";
        }
        else if (request.PowerOn == true && _climateMode == "off")
        {
            _climateMode = "cool";
        }

        if (!string.IsNullOrWhiteSpace(request.HvacMode))
        {
            _climateMode = request.HvacMode;
        }

        if (request.TargetTemperature.HasValue)
        {
            _climateTarget = request.TargetTemperature.Value;
        }

        return Task.CompletedTask;
    }

    public Task ControlTvAsync(TvControlRequestDto request)
    {
        if (request.EntityId.Equals("media_player.living_room_tv", StringComparison.OrdinalIgnoreCase))
        {
            if (request.PowerOn.HasValue) _livingRoomTvOn = request.PowerOn.Value;
            if (request.TogglePlayPause == true) _livingRoomTvOn = true;
            if (request.VolumeUp == true) _livingRoomTvVolume = Math.Clamp(_livingRoomTvVolume + 5, 0, 100);
            if (request.VolumeDown == true) _livingRoomTvVolume = Math.Clamp(_livingRoomTvVolume - 5, 0, 100);
            if (request.VolumePercent.HasValue) _livingRoomTvVolume = Math.Clamp(request.VolumePercent.Value, 0, 100);
        }

        if (request.EntityId.Equals("media_player.bedroom_tv", StringComparison.OrdinalIgnoreCase))
        {
            if (request.PowerOn.HasValue) _bedroomTvOn = request.PowerOn.Value;
            if (request.TogglePlayPause == true) _bedroomTvOn = true;
            if (request.VolumeUp == true) _bedroomTvVolume = Math.Clamp(_bedroomTvVolume + 5, 0, 100);
            if (request.VolumeDown == true) _bedroomTvVolume = Math.Clamp(_bedroomTvVolume - 5, 0, 100);
            if (request.VolumePercent.HasValue) _bedroomTvVolume = Math.Clamp(request.VolumePercent.Value, 0, 100);
        }

        return Task.CompletedTask;
    }

    public Task ControlCameraAsync(CameraControlRequestDto request)
    {
        return Task.CompletedTask;
    }

    public Task<SensorDetailsDto?> GetSensorDetailsAsync(string entityId, int hours = 24)
    {
        var originalType = entityId.Contains("temp", StringComparison.OrdinalIgnoreCase) ? "temp"
            : entityId.Contains("hum", StringComparison.OrdinalIgnoreCase) ? "humidity"
            : entityId.Contains("leak", StringComparison.OrdinalIgnoreCase) ? "leak"
            : "sensor";
        var typeOverride = _typeOverrides.GetValueOrDefault(entityId);

        var details = new SensorDetailsDto
        {
            EntityId = entityId,
            DisplayName = entityId switch
            {
                "sensor.temp_living" => "Температура вітальні",
                "sensor.hum_living" => "Вологість вітальні",
                "sensor.temp_kitchen" => "Температура кухні",
                "sensor.hum_kitchen" => "Вологість кухні",
                "binary_sensor.leak_kitchen" => "Датчик протікання",
                _ => entityId
            },
            OriginalType = originalType,
            TypeOverride = typeOverride,
            Type = typeOverride ?? originalType,
            RoomName = entityId.Contains("kitchen", StringComparison.OrdinalIgnoreCase) ? "Kitchen" : "Living Room",
            RawState = entityId.Contains("leak", StringComparison.OrdinalIgnoreCase) ? "off" : "22.5",
            DisplayState = entityId.Contains("leak", StringComparison.OrdinalIgnoreCase) ? "Сухо" : "Норма",
            Unit = entityId.Contains("temp", StringComparison.OrdinalIgnoreCase) ? "°C"
                : entityId.Contains("hum", StringComparison.OrdinalIgnoreCase) ? "%"
                : string.Empty,
            CurrentValue = entityId.Contains("temp", StringComparison.OrdinalIgnoreCase) ? 22.5
                : entityId.Contains("hum", StringComparison.OrdinalIgnoreCase) ? 48
                : null,
            CurrentMode = entityId.StartsWith("climate.", StringComparison.OrdinalIgnoreCase) ? _climateMode : null,
            TargetTemperature = entityId.StartsWith("climate.", StringComparison.OrdinalIgnoreCase) ? _climateTarget : null,
            AvailableModes = entityId.StartsWith("climate.", StringComparison.OrdinalIgnoreCase) ? ["off", "cool", "heat", "fan_only", "dry", "auto"] : [],
            Online = true,
            RequestedAt = DateTime.Now,
            History = BuildMockHistory(entityId, hours)
        };

        return Task.FromResult<SensorDetailsDto?>(details);
    }

    public Task<MotionDebugDto?> GetMotionDebugAsync(string entityId, int hours = 24, int limit = 50)
    {
        var now = DateTime.Now;
        var points = Enumerable.Range(0, Math.Clamp(limit, 1, 20))
            .Select(i => new MotionDebugPointDto
            {
                Timestamp = now.AddMinutes(-i * 7),
                State = i % 3 == 0 ? "on" : "off"
            })
            .ToList();

        var dto = new MotionDebugDto
        {
            EntityId = entityId,
            DisplayName = entityId,
            CurrentState = "on",
            LastChanged = now.AddMinutes(-2),
            LastUpdated = now.AddMinutes(-2),
            Online = true,
            Hours = Math.Clamp(hours, 1, 168),
            Limit = Math.Clamp(limit, 1, 500),
            HistoryPoints = points
        };

        return Task.FromResult<MotionDebugDto?>(dto);
    }

    private static List<SensorHistoryPointDto> BuildMockHistory(string entityId, int hours)
    {
        var points = new List<SensorHistoryPointDto>();
        var count = Math.Clamp(hours * 2, 6, 72);
        var start = DateTime.Now.AddHours(-hours);

        for (var i = 0; i < count; i++)
        {
            var t = start.AddMinutes(i * (hours * 60.0 / count));
            var value = entityId.Contains("temp", StringComparison.OrdinalIgnoreCase)
                ? 21.5 + Math.Sin(i / 3.0) * 1.8
                : entityId.Contains("hum", StringComparison.OrdinalIgnoreCase)
                    ? 45 + Math.Cos(i / 4.0) * 6
                    : (double?)null;

            points.Add(new SensorHistoryPointDto
            {
                Timestamp = t,
                Value = value.HasValue ? Math.Round(value.Value, 2) : null,
                State = value.HasValue ? value.Value.ToString("0.##") : "off"
            });
        }

        return points;
    }

    private List<SceneDto> BuildScenes()
    {
        return
        [
            new SceneDto { Name = "Home", IsActive = _activeScene == "Home" },
            new SceneDto { Name = "Away", IsActive = _activeScene == "Away" },
            new SceneDto { Name = "Night", IsActive = _activeScene == "Night" }
        ];
    }

    public Task AssignDeviceToRoomAsync(string entityId, string roomName)
    {
        if (!string.IsNullOrWhiteSpace(entityId) && !string.IsNullOrWhiteSpace(roomName))
        {
            _roomAssignments[entityId] = roomName;
        }

        return Task.CompletedTask;
    }

    public Task<DeviceDisplayNameDto?> UpdateDeviceDisplayNameAsync(DeviceDisplayNameRequestDto request)
    {
        var originalName = string.IsNullOrWhiteSpace(request.EntityId) ? string.Empty : request.EntityId;
        var overrideName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();

        return Task.FromResult<DeviceDisplayNameDto?>(new DeviceDisplayNameDto
        {
            EntityId = request.EntityId,
            PresentationKey = request.EntityId,
            OriginalName = originalName,
            DisplayNameOverride = overrideName,
            EffectiveDisplayName = overrideName ?? originalName
        });
    }

    public Task<DeviceTypeOverrideDto?> UpdateDeviceTypeOverrideAsync(DeviceTypeOverrideRequestDto request)
    {
        var originalType = "sensor";
        var overrideType = string.IsNullOrWhiteSpace(request.Type) ? null : request.Type.Trim().ToLowerInvariant();

        if (overrideType is null)
        {
            _typeOverrides.Remove(request.EntityId);
        }
        else
        {
            _typeOverrides[request.EntityId] = overrideType;
        }

        return Task.FromResult<DeviceTypeOverrideDto?>(new DeviceTypeOverrideDto
        {
            EntityId = request.EntityId,
            PresentationKey = request.EntityId,
            OriginalType = originalType,
            TypeOverride = overrideType,
            EffectiveType = overrideType ?? originalType
        });
    }

    public Task ClearAllDataAsync()
    {
        _roomAssignments.Clear();
        return Task.CompletedTask;
    }

    public Task ClearAllDataAndResyncAsync()
    {
        _roomAssignments.Clear();
        return Task.CompletedTask;
    }

    public Task<List<ClimateDto>> GetClimateDevicesAsync() => Task.FromResult(new List<ClimateDto>());
    public Task<List<LightDto>> GetLightsAsync() => Task.FromResult(new List<LightDto>());
    public Task ControlLightAsync(LightControlRequestDto request) => Task.CompletedTask;
    public Task RemoveHaEntityAsync(string entityId) => Task.CompletedTask;
}
