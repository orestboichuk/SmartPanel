using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartPanel.Api.Data;
using SmartPanel.Shared;

namespace SmartPanel.Api.Services;

public class HomeAssistantSmartHomeService : ISmartHomeService
{
    private static readonly string[] DevicePartSuffixes = ["_temperature", "_humidity", "_battery", "_moisture", "_door", "_motion"];
    private static readonly string[] PreferredRoomOrder = ["Kitchen", "Bathroom", "Corridor", "Living Room", "Sofi Room", "Bedroom", "Unassigned"];
    private static readonly Dictionary<string, string> FallbackRoomByDeviceKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ai_pont"] = "Kitchen",
        ["cam_motion_region_detection"] = "Corridor",
        ["conditioner_ac"] = "Bedroom",
        ["contact_sensor"] = "Corridor",
        ["kitchen_water_leak_sensor"] = "Kitchen",
        ["motion_sensor_3"] = "Corridor",
        ["stopcock_water_leak_sensor_3"] = "Bathroom",
        ["temperature_humidity"] = "Kitchen",
        ["temperature_humidity_sensor_4"] = "Bedroom",
        ["temperature_humidity_sensor_5"] = "Sofi Room",
        ["wash_water_leak_sensor_2"] = "Bathroom"
    };
    private static readonly Dictionary<string, string> RoomAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bathroom"] = "Bathroom",
        ["bedroom"] = "Bedroom",
        ["corridor"] = "Corridor",
        ["hall"] = "Corridor",
        ["hallway"] = "Corridor",
        ["kitchen"] = "Kitchen",
        ["living room"] = "Living Room",
        ["sofi"] = "Sofi Room",
        ["sofi room"] = "Sofi Room",
        ["unassigned"] = "Unassigned",
        ["ванна"] = "Bathroom",
        ["вітальня"] = "Living Room",
        ["коридор"] = "Corridor",
        ["кухня"] = "Kitchen",
        ["спальня"] = "Bedroom"
    };
    private static readonly Dictionary<string, string> RoomInferenceTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bathroom"] = "Bathroom",
        ["bedroom"] = "Bedroom",
        ["corridor"] = "Corridor",
        ["hall"] = "Corridor",
        ["hallway"] = "Corridor",
        ["kitchen"] = "Kitchen",
        ["living room"] = "Living Room",
        ["sofi"] = "Sofi Room",
        ["ванна"] = "Bathroom",
        ["вітальня"] = "Living Room",
        ["коридор"] = "Corridor",
        ["кухня"] = "Kitchen",
        ["спальня"] = "Bedroom"
    };

    private static readonly SemaphoreSlim SyncGate = new(1, 1);
    private static DateTime _lastSyncUtc = DateTime.MinValue;
    private static DateTime _lastRegistrySyncUtc = DateTime.MinValue;
    private static HomeAssistantRegistrySnapshot _registrySnapshot = new();

    private readonly HomeAssistantClient _client;
    private readonly SmartPanelDbContext _db;
    private string _activeScene = "Home";

    public HomeAssistantSmartHomeService(HomeAssistantClient client, SmartPanelDbContext db)
    {
        _client = client;
        _db = db;
    }

    public async Task<DashboardDto> GetDashboardAsync()
    {
        await SyncSnapshotsAsync();
        var snapshots = await _db.HomeAssistantEntitySnapshots.AsNoTracking().Where(x => x.IsActive).ToListAsync();
        var door = snapshots.FirstOrDefault(x => x.EntityId == "binary_sensor.contact_sensor_door");
        var motion = snapshots.FirstOrDefault(x => x.EntityId == "binary_sensor.cam_motion_region_detection");
        var leak = snapshots.Count(x => x.Type == "leak" && (x.RawState == "on" || x.RawState == "wet" || x.RawState == "detected"));
        var alerts = 0;
        if (door is not null && (door.RawState == "on" || door.RawState == "open")) alerts++;
        if (motion is not null && (motion.RawState == "on" || motion.RawState == "detected")) alerts++;
        alerts += leak;

        return new DashboardDto
        {
            CurrentTime = DateTime.Now,
            AlertsCount = alerts,
            HouseStatus = alerts == 0 ? "All Normal" : $"Alerts: {alerts}",
            Scenes = await GetScenesAsync(),
            Services = BuildServiceStatuses(snapshots)
        };
    }

    public async Task<List<RoomDto>> GetRoomsAsync()
    {
        await SyncSnapshotsAsync();

        var snapshots = await _db.HomeAssistantEntitySnapshots
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync();

        var overridesByPresentationKey = await GetDisplayOverridesByPresentationKeyAsync(snapshots);
        var projections = snapshots
            .GroupBy(GetPresentationKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => MapRoomSensorGroup(group, overridesByPresentationKey))
            .ToList();

        return projections
            .GroupBy(x => x.RoomName, StringComparer.OrdinalIgnoreCase)
            .Select(room => new RoomDto
            {
                Name = room.Key,
                Sensors = room.Select(x => x.Sensor)
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .OrderBy(x => RoomOrder(x.Name))
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<ClimateDto>> GetClimateDevicesAsync()
    {
        await SyncSnapshotsAsync();

        var snapshots = await _db.HomeAssistantEntitySnapshots
            .AsNoTracking()
            .Where(x => x.IsActive && x.Domain == "climate")
            .ToListAsync();

        return snapshots
            .Select(s =>
            {
                var attrs = DeserializeDictionary(s.AttributesJson);
                return new ClimateDto
                {
                    Id = s.EntityId,
                    Name = s.DisplayName,
                    IsOn = !string.Equals(s.RawState, "off", StringComparison.OrdinalIgnoreCase)
                           && !string.Equals(s.RawState, "unavailable", StringComparison.OrdinalIgnoreCase),
                    Online = s.IsOnline,
                    RoomName = s.RoomName ?? string.Empty,
                    CurrentMode = s.RawState,
                    CurrentTemperature = s.NumericValue,
                    TargetTemperature = double.TryParse(GetString(attrs, "temperature"),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : null,
                    AvailableModes = GetStringList(attrs, "hvac_modes"),
                    UpdatedAt = s.LastUpdatedUtc.ToLocalTime()
                };
            })
            .OrderBy(x => x.RoomName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<LightDto>> GetLightsAsync()
    {
        await SyncSnapshotsAsync();

        var snapshots = await _db.HomeAssistantEntitySnapshots
            .AsNoTracking()
            .Where(x => x.IsActive && x.Domain == "light")
            .ToListAsync();

        return snapshots
            .Select(s =>
            {
                var attrs = DeserializeDictionary(s.AttributesJson);
                var brightnessStr = GetString(attrs, "brightness");
                int? brightness = double.TryParse(brightnessStr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var b)
                    ? (int)Math.Round(b / 255.0 * 100)
                    : null;

                return new LightDto
                {
                    Id = s.EntityId,
                    Name = s.DisplayName,
                    IsOn = string.Equals(s.RawState, "on", StringComparison.OrdinalIgnoreCase),
                    Online = s.IsOnline,
                    RoomName = s.RoomName ?? string.Empty,
                    Brightness = brightness,
                    UpdatedAt = s.LastUpdatedUtc.ToLocalTime()
                };
            })
            .OrderBy(x => x.RoomName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task ControlLightAsync(LightControlRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.EntityId))
            throw new InvalidOperationException("EntityId is required.");

        var id = request.EntityId;
        if (request.TurnOn is null)
        {
            await _client.CallServiceAsync("light", "toggle", new { entity_id = id });
        }
        else if (request.TurnOn == true)
        {
            object data = request.Brightness.HasValue
                ? new { entity_id = id, brightness = (int)Math.Round(request.Brightness.Value / 100.0 * 255) }
                : new { entity_id = id };
            await _client.CallServiceAsync("light", "turn_on", data);
        }
        else
        {
            await _client.CallServiceAsync("light", "turn_off", new { entity_id = id });
        }

        _lastSyncUtc = DateTime.MinValue;
    }

    public async Task<List<CameraDto>> GetCamerasAsync()
    {
        await SyncSnapshotsAsync();

        var snapshots = await _db.HomeAssistantEntitySnapshots
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync();

        var overridesByPresentationKey = await GetDisplayOverridesByPresentationKeyAsync(snapshots);

        return snapshots
            .GroupBy(GetPresentationKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Any(x => x.Domain == "camera"))
            .Select(group => MapCameraGroup(group, overridesByPresentationKey))
            .OrderBy(x => string.IsNullOrWhiteSpace(x.RoomName) ? "zzzz" : x.RoomName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<TvDto>> GetTvsAsync()
    {
        await SyncSnapshotsAsync();

        var snapshots = await _db.HomeAssistantEntitySnapshots
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync();

        var overridesByPresentationKey = await GetDisplayOverridesByPresentationKeyAsync(snapshots);

        return snapshots
            .GroupBy(GetPresentationKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Any(x => x.Domain == "media_player"))
            .Select(group =>
            {
                var items = group.ToList();
                var primary = SelectPrimaryMediaItem(items);
                var overrideEntity = overridesByPresentationKey.GetValueOrDefault(group.Key);
                var originalName = ResolveOriginalDisplayName(items);
                var attrs = DeserializeDictionary(primary.AttributesJson);

                return new TvDto
                {
                    Id = primary.EntityId,
                    Name = GetEffectiveDisplayName(originalName, overrideEntity),
                    State = primary.RawState,
                    Online = primary.IsOnline,
                    RoomName = ResolvePresentationRoom(items),
                    Source = GetString(attrs, "source"),
                    AvailableSources = GetStringList(attrs, "source_list"),
                    App = GetString(attrs, "app_name") ?? GetString(attrs, "media_title"),
                    VolumePercent = ParseVolumePercent(attrs),
                    Muted = GetBool(attrs, "is_volume_muted"),
                    UpdatedAt = items.Max(x => x.LastUpdatedUtc).ToLocalTime()
                };
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<List<SceneDto>> GetScenesAsync() => Task.FromResult(new List<SceneDto>
    {
        new() { Name = "Home", IsActive = _activeScene == "Home" },
        new() { Name = "Away", IsActive = _activeScene == "Away" },
        new() { Name = "Night", IsActive = _activeScene == "Night" }
    });

    public Task ActivateSceneAsync(string sceneName)
    {
        _activeScene = sceneName;
        return Task.CompletedTask;
    }

    public async Task AssignDeviceToRoomAsync(string entityId, string roomName)
    {
        var id = NormalizeEntityId(entityId);
        var room = NormalizeRoom(roomName);
        if (id.Length == 0 || room.Length == 0)
        {
            return;
        }

        await SyncSnapshotsAsync();

        var snapshot = await _db.HomeAssistantEntitySnapshots.FirstOrDefaultAsync(x => x.EntityId == id);
        var groupRows = snapshot is not null
            ? await GetPresentationGroupAsync(snapshot, asNoTracking: false)
            : [];

        var deviceKeys = (groupRows.Count > 0 ? groupRows.Select(x => x.DeviceKey) : [BuildDeviceKey(id)])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var deviceKey in deviceKeys)
        {
            var binding = await _db.DeviceRoomBindings.FirstOrDefaultAsync(x => x.DeviceKey == deviceKey);
            if (room.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
            {
                if (binding is not null)
                {
                    _db.DeviceRoomBindings.Remove(binding);
                }
            }
            else if (binding is null)
            {
                _db.DeviceRoomBindings.Add(new DeviceRoomBindingEntity
                {
                    DeviceKey = deviceKey,
                    RoomName = room,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                binding.RoomName = room;
                binding.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        var legacy = await _db.DeviceRoomAssignments.FirstOrDefaultAsync(x => x.EntityId == id);
        if (legacy is null)
        {
            _db.DeviceRoomAssignments.Add(new DeviceRoomAssignmentEntity
            {
                EntityId = id,
                RoomName = room,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            legacy.RoomName = room;
            legacy.UpdatedAtUtc = DateTime.UtcNow;
        }

        foreach (var item in groupRows)
        {
            item.RoomName = room.Equals("Unassigned", StringComparison.OrdinalIgnoreCase) ? null : room;
        }

        await _db.SaveChangesAsync();
    }

    public async Task<DeviceDisplayNameDto?> UpdateDeviceDisplayNameAsync(DeviceDisplayNameRequestDto request)
    {
        var id = NormalizeEntityId(request.EntityId);
        if (id.Length == 0)
        {
            return null;
        }

        await SyncSnapshotsAsync();

        var snapshot = await _db.HomeAssistantEntitySnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.EntityId == id && x.IsActive);

        if (snapshot is null)
        {
            return null;
        }

        var group = await GetPresentationGroupAsync(snapshot, asNoTracking: true);
        var originalName = ResolveOriginalDisplayName(group);
        var overrideName = NormalizeDisplayNameOverride(request.DisplayName);
        if (string.Equals(originalName, overrideName, StringComparison.OrdinalIgnoreCase))
        {
            overrideName = null;
        }

        var presentationKey = GetPresentationKey(snapshot);
        var representativeEntityId = SelectRepresentativeEntityId(group, ResolvePresentationType(group));
        var existing = await _db.DeviceDisplayOverrides.FirstOrDefaultAsync(x => x.PresentationKey == presentationKey);

        if (overrideName is null)
        {
            if (existing is not null)
            {
                _db.DeviceDisplayOverrides.Remove(existing);
                await _db.SaveChangesAsync();
            }
        }
        else if (existing is null)
        {
            _db.DeviceDisplayOverrides.Add(new DeviceDisplayOverrideEntity
            {
                PresentationKey = presentationKey,
                EntityId = representativeEntityId,
                OriginalName = originalName,
                DisplayNameOverride = overrideName,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
        else
        {
            existing.EntityId = representativeEntityId;
            existing.OriginalName = originalName;
            existing.DisplayNameOverride = overrideName;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return new DeviceDisplayNameDto
        {
            EntityId = representativeEntityId,
            PresentationKey = presentationKey,
            OriginalName = originalName,
            DisplayNameOverride = overrideName,
            EffectiveDisplayName = overrideName ?? originalName
        };
    }

    public async Task ControlClimateAsync(ClimateControlRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.EntityId) || !request.EntityId.StartsWith("climate.", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Climate entity_id is required.");

        var id = request.EntityId;
        if (!string.IsNullOrWhiteSpace(request.HvacMode))
            await _client.CallServiceAsync("climate", "set_hvac_mode", new { entity_id = id, hvac_mode = request.HvacMode });
        else if (request.PowerOn.HasValue)
            await _client.CallServiceAsync("climate", request.PowerOn.Value ? "turn_on" : "turn_off", new { entity_id = id });

        if (request.TargetTemperature.HasValue)
            await _client.CallServiceAsync("climate", "set_temperature", new { entity_id = id, temperature = request.TargetTemperature.Value });

        _lastSyncUtc = DateTime.MinValue;
    }

    public async Task ControlTvAsync(TvControlRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.EntityId) || !request.EntityId.StartsWith("media_player.", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("TV media_player entity_id is required.");

        var id = request.EntityId;
        if (request.PowerOn.HasValue) await _client.CallServiceAsync("media_player", request.PowerOn.Value ? "turn_on" : "turn_off", new { entity_id = id });
        if (request.TogglePlayPause == true) await _client.CallServiceAsync("media_player", "media_play_pause", new { entity_id = id });
        if (request.VolumeUp == true) await _client.CallServiceAsync("media_player", "volume_up", new { entity_id = id });
        if (request.VolumeDown == true) await _client.CallServiceAsync("media_player", "volume_down", new { entity_id = id });
        if (request.VolumePercent.HasValue) await _client.CallServiceAsync("media_player", "volume_set", new { entity_id = id, volume_level = Math.Clamp(request.VolumePercent.Value, 0, 100) / 100.0 });
        if (request.Mute.HasValue) await _client.CallServiceAsync("media_player", "volume_mute", new { entity_id = id, is_volume_muted = request.Mute.Value });
        if (!string.IsNullOrWhiteSpace(request.Source)) await _client.CallServiceAsync("media_player", "select_source", new { entity_id = id, source = request.Source });
        _lastSyncUtc = DateTime.MinValue;
    }

    public async Task ControlCameraAsync(CameraControlRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.EntityId) || !request.EntityId.StartsWith("camera.", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Camera entity_id is required.");

        var id = request.EntityId.Trim();
        var cmd = request.Command.Trim().ToLowerInvariant();
        var step = Math.Clamp(request.Step ?? 0.3, 0.05, 1.0);

        switch (cmd)
        {
            case "left": await _client.CallServiceAsync("onvif", "ptz", new { entity_id = id, pan = -step, tilt = 0, move_mode = "ContinuousMove" }); break;
            case "right": await _client.CallServiceAsync("onvif", "ptz", new { entity_id = id, pan = step, tilt = 0, move_mode = "ContinuousMove" }); break;
            case "up": await _client.CallServiceAsync("onvif", "ptz", new { entity_id = id, pan = 0, tilt = step, move_mode = "ContinuousMove" }); break;
            case "down": await _client.CallServiceAsync("onvif", "ptz", new { entity_id = id, pan = 0, tilt = -step, move_mode = "ContinuousMove" }); break;
            case "zoom_in": await _client.CallServiceAsync("onvif", "ptz", new { entity_id = id, zoom = step, move_mode = "ContinuousMove" }); break;
            case "zoom_out": await _client.CallServiceAsync("onvif", "ptz", new { entity_id = id, zoom = -step, move_mode = "ContinuousMove" }); break;
            case "start_recording": await _client.CallServiceAsync("camera", "record", new { entity_id = id, duration = Math.Clamp(request.DurationSeconds ?? 30, 5, 600), filename = $"/config/www/smartpanel/{id.Replace('.', '_')}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.mp4" }); break;
            case "enable_motion_detection": await _client.CallServiceAsync("camera", "enable_motion_detection", new { entity_id = id }); break;
            case "disable_motion_detection": await _client.CallServiceAsync("camera", "disable_motion_detection", new { entity_id = id }); break;
            default: throw new InvalidOperationException($"Unsupported camera command: {request.Command}");
        }

        _lastSyncUtc = DateTime.MinValue;
    }

    public async Task<SensorDetailsDto?> GetSensorDetailsAsync(string entityId, int hours = 24)
    {
        await SyncSnapshotsAsync();

        var id = NormalizeEntityId(entityId);
        var snapshot = await _db.HomeAssistantEntitySnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.EntityId == id && x.IsActive);

        if (snapshot is null)
        {
            return null;
        }

        var group = await GetPresentationGroupAsync(snapshot, asNoTracking: true);
        var presentationKey = GetPresentationKey(snapshot);
        var overrideEntity = await _db.DeviceDisplayOverrides
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PresentationKey == presentationKey);

        var originalName = ResolveOriginalDisplayName(group);
        var presentationType = ResolvePresentationType(group);
        var attrs = DeserializeDictionary(snapshot.AttributesJson);
        var capabilities = group.SelectMany(x => DeserializeStringList(x.CapabilitiesJson))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var history = await GetNormalizedHistoryAsync(id, hours);
        var temperatureSnapshot = group.FirstOrDefault(x => x.Type == "temp");
        var humiditySnapshot = group.FirstOrDefault(x => x.Type == "humidity");
        var batterySnapshot = group.FirstOrDefault(IsBatteryEntity);
        var temperatureHistory = temperatureSnapshot is not null
            ? await GetNormalizedHistoryAsync(temperatureSnapshot.EntityId, hours)
            : [];
        var humidityHistory = humiditySnapshot is not null
            ? await GetNormalizedHistoryAsync(humiditySnapshot.EntityId, hours)
            : [];

        if (presentationType == "motion" &&
            !history.Any(x => IsMotionDetectedState(x.State)) &&
            IsMotionDetectedState(snapshot.RawState))
        {
            var liveState = (await _client.GetStatesAsync())
                .FirstOrDefault(x => x.EntityId.Equals(id, StringComparison.OrdinalIgnoreCase));

            if (liveState?.LastChanged is DateTime lastChangedUtc)
            {
                history.Add(new HomeAssistantHistoryStateDto
                {
                    EntityId = id,
                    State = snapshot.RawState,
                    LastChanged = lastChangedUtc
                });
            }
        }

        return new SensorDetailsDto
        {
            EntityId = snapshot.EntityId,
            OriginalName = originalName,
            DisplayName = GetEffectiveDisplayName(originalName, overrideEntity),
            DisplayNameOverride = NormalizeDisplayNameOverride(overrideEntity?.DisplayNameOverride),
            Type = presentationType,
            Domain = snapshot.Domain,
            DeviceClass = GetString(attrs, "device_class"),
            RoomName = ResolvePresentationRoom(group),
            RawState = snapshot.RawState,
            DisplayState = snapshot.RawState,
            Unit = snapshot.Unit,
            CurrentValue = snapshot.NumericValue,
            CurrentMode = snapshot.Type == "climate" ? GetString(attrs, "hvac_mode") ?? snapshot.RawState : null,
            TargetTemperature = snapshot.Type == "climate" ? ParseDouble(GetString(attrs, "temperature")) : null,
            AvailableModes = snapshot.Type == "climate" ? GetStringList(attrs, "hvac_modes") : [],
            Capabilities = capabilities,
            Source = snapshot.Domain == "media_player" ? GetString(attrs, "source") : null,
            AvailableSources = snapshot.Domain == "media_player" ? GetStringList(attrs, "source_list") : [],
            App = snapshot.Domain == "media_player" ? GetString(attrs, "app_name") ?? GetString(attrs, "media_title") : null,
            VolumePercent = snapshot.Domain == "media_player" ? ParseVolumePercent(attrs) : null,
            Muted = snapshot.Domain == "media_player" ? GetBool(attrs, "is_volume_muted") : null,
            HumidityValue = humiditySnapshot?.NumericValue ?? ParseDouble(humiditySnapshot?.RawState),
            HumidityUnit = humiditySnapshot?.Unit,
            BatteryLevel = batterySnapshot?.NumericValue ?? ParseDouble(batterySnapshot?.RawState),
            Online = snapshot.IsOnline,
            RequestedAt = DateTime.Now,
            TemperatureHistory = temperatureHistory.Select(MapHistoryPoint).ToList(),
            HumidityHistory = humidityHistory.Select(MapHistoryPoint).ToList(),
            History = history.Select(h => new SensorHistoryPointDto
            {
                Timestamp = h.LastChanged.ToLocalTime(),
                Value = ParseDouble(h.State),
                State = h.State
            }).ToList()
        };
    }

    public async Task<MotionDebugDto?> GetMotionDebugAsync(string entityId, int hours = 24, int limit = 50)
    {
        await SyncSnapshotsAsync();

        var id = NormalizeEntityId(entityId);
        var snapshot = await _db.HomeAssistantEntitySnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.EntityId == id && x.IsActive);

        var liveState = (await _client.GetStatesAsync())
            .FirstOrDefault(x => x.EntityId.Equals(id, StringComparison.OrdinalIgnoreCase));

        if (snapshot is null && liveState is null)
        {
            return null;
        }

        var displayName = liveState?.EntityId ?? id;
        if (snapshot is not null)
        {
            var group = await GetPresentationGroupAsync(snapshot, asNoTracking: true);
            var overrideEntity = await _db.DeviceDisplayOverrides
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.PresentationKey == GetPresentationKey(snapshot));
            var originalName = ResolveOriginalDisplayName(group);
            displayName = GetEffectiveDisplayName(originalName, overrideEntity);
        }

        var boundedHours = Math.Clamp(hours, 1, 168);
        var boundedLimit = Math.Clamp(limit, 1, 500);
        var history = (await _client.GetSensorHistoryAsync(id, boundedHours))
            .OrderByDescending(x => x.LastChanged)
            .Take(boundedLimit)
            .ToList();

        if (IsMotionDetectedState(liveState?.State))
        {
            if (liveState?.LastReported is DateTime lastReportedUtc)
            {
                history.Add(new HomeAssistantHistoryStateDto
                {
                    EntityId = id,
                    State = liveState.State,
                    LastChanged = lastReportedUtc
                });
            }
            else if (liveState?.LastChanged is DateTime lastChangedUtc)
            {
                history.Add(new HomeAssistantHistoryStateDto
                {
                    EntityId = id,
                    State = liveState.State,
                    LastChanged = lastChangedUtc
                });
            }
        }

        history = history
            .OrderByDescending(x => x.LastChanged)
            .GroupBy(x => x.LastChanged)
            .Select(g => g.First())
            .Take(boundedLimit)
            .ToList();

        return new MotionDebugDto
        {
            EntityId = id,
            DisplayName = displayName,
            CurrentState = liveState?.State ?? snapshot?.RawState ?? string.Empty,
            LastChanged = liveState?.LastChanged?.ToLocalTime(),
            LastUpdated = liveState?.LastUpdated?.ToLocalTime(),
            LastReported = liveState?.LastReported?.ToLocalTime(),
            Online = snapshot?.IsOnline ?? true,
            Hours = boundedHours,
            Limit = boundedLimit,
            HistoryPoints = history.Select(x => new MotionDebugPointDto
            {
                Timestamp = x.LastChanged.ToLocalTime(),
                State = x.State
            }).ToList()
        };
    }

    public async Task ClearAllDataAsync()
    {
        var snapshots = await _db.HomeAssistantEntitySnapshots.ToListAsync();
        var bindings = await _db.DeviceRoomBindings.ToListAsync();
        var legacy = await _db.DeviceRoomAssignments.ToListAsync();
        var displayOverrides = await _db.DeviceDisplayOverrides.ToListAsync();

        if (snapshots.Count > 0) _db.HomeAssistantEntitySnapshots.RemoveRange(snapshots);
        if (bindings.Count > 0) _db.DeviceRoomBindings.RemoveRange(bindings);
        if (legacy.Count > 0) _db.DeviceRoomAssignments.RemoveRange(legacy);
        if (displayOverrides.Count > 0) _db.DeviceDisplayOverrides.RemoveRange(displayOverrides);

        await _db.SaveChangesAsync();
        _lastSyncUtc = DateTime.MinValue;
    }

    public async Task ClearAllDataAndResyncAsync()
    {
        await ClearAllDataAsync();
        await SyncSnapshotsAsync();
    }

    public async Task RemoveHaEntityAsync(string entityId)
    {
        var rows = await _db.HomeAssistantEntitySnapshots
            .Where(x => x.EntityId == entityId)
            .ToListAsync();
        if (rows.Count > 0)
        {
            _db.HomeAssistantEntitySnapshots.RemoveRange(rows);
            await _db.SaveChangesAsync();
        }
    }

    private async Task SyncSnapshotsAsync()
    {
        if ((DateTime.UtcNow - _lastSyncUtc) < TimeSpan.FromSeconds(10))
        {
            return;
        }

        await SyncGate.WaitAsync();
        try
        {
            if ((DateTime.UtcNow - _lastSyncUtc) < TimeSpan.FromSeconds(10))
            {
                return;
            }

            var states = await _client.GetStatesAsync();
            var discoverable = states.Where(IsDiscoverable).ToList();
            var bindings = await _db.DeviceRoomBindings.AsNoTracking().ToDictionaryAsync(x => x.DeviceKey, x => x.RoomName);
            var registrySnapshot = await GetRegistrySnapshotCachedAsync();
            var snapshots = await _db.HomeAssistantEntitySnapshots.ToDictionaryAsync(x => x.EntityId);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var state in discoverable)
            {
                var id = NormalizeEntityId(state.EntityId);
                if (id.Length == 0)
                {
                    continue;
                }

                seen.Add(id);
                if (!snapshots.TryGetValue(id, out var row))
                {
                    row = new HomeAssistantEntitySnapshotEntity { EntityId = id };
                    _db.HomeAssistantEntitySnapshots.Add(row);
                    snapshots[id] = row;
                }

                var attrs = NormalizeAttributes(state.Attributes);
                var deviceKey = BuildDeviceKey(id);
                registrySnapshot.DeviceIdByEntityId.TryGetValue(id, out var registryDeviceId);

                row.Domain = Domain(id);
                row.DeviceKey = deviceKey;
                row.RegistryDeviceId = string.IsNullOrWhiteSpace(registryDeviceId) ? null : registryDeviceId.Trim();
                row.DisplayName = NormalizeDisplayName(GetString(attrs, "friendly_name") ?? id);
                row.Type = ResolveType(id);
                row.RoomName = ResolveRoom(id, deviceKey, row.DisplayName, bindings, registrySnapshot.RoomByEntityId);
                row.RawState = state.State ?? string.Empty;
                row.Unit = GetString(attrs, "unit_of_measurement") ?? DefaultUnit(row.Type);
                row.NumericValue = ParseDouble(state.State) ?? ParseDouble(GetString(attrs, "current_temperature"));
                row.IsOnline = !IsUnavailable(state.State);
                row.AttributesJson = JsonSerializer.Serialize(attrs);
                row.CapabilitiesJson = JsonSerializer.Serialize(DetectCapabilities(id, attrs));
                row.LastSeenUtc = DateTime.UtcNow;
                var entityTimestamp = state.LastReported ?? state.LastUpdated ?? state.LastChanged;
                row.LastUpdatedUtc = entityTimestamp?.ToUniversalTime() ?? DateTime.UtcNow;
                row.IsActive = true;
            }

            ApplyInheritedRoomsByPresentationKey(snapshots.Values.Where(x => seen.Contains(x.EntityId)));

            foreach (var row in snapshots.Values.Where(x => !seen.Contains(x.EntityId)))
            {
                row.IsActive = false;
                row.LastUpdatedUtc = DateTime.UtcNow;
            }

            var stale = snapshots.Values
                .Where(x => !x.IsActive && x.LastSeenUtc < DateTime.UtcNow.AddDays(-14))
                .ToList();
            if (stale.Count > 0)
            {
                _db.HomeAssistantEntitySnapshots.RemoveRange(stale);
            }

            await _db.SaveChangesAsync();
            _lastSyncUtc = DateTime.UtcNow;
        }
        finally
        {
            SyncGate.Release();
        }
    }

    private async Task<HomeAssistantRegistrySnapshot> GetRegistrySnapshotCachedAsync()
    {
        if ((DateTime.UtcNow - _lastRegistrySyncUtc) < TimeSpan.FromMinutes(5) &&
            (_registrySnapshot.RoomByEntityId.Count > 0 || _registrySnapshot.DeviceIdByEntityId.Count > 0))
        {
            return _registrySnapshot;
        }

        try
        {
            var snapshot = await _client.GetRegistrySnapshotAsync();
            _registrySnapshot = new HomeAssistantRegistrySnapshot
            {
                RoomByEntityId = snapshot.RoomByEntityId
                    .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
                    .ToDictionary(x => NormalizeEntityId(x.Key), x => NormalizeRoom(x.Value), StringComparer.OrdinalIgnoreCase),
                DeviceIdByEntityId = snapshot.DeviceIdByEntityId
                    .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
                    .ToDictionary(x => NormalizeEntityId(x.Key), x => x.Value.Trim(), StringComparer.OrdinalIgnoreCase)
            };
            _lastRegistrySyncUtc = DateTime.UtcNow;
        }
        catch
        {
            // Fall back to manual bindings and inferred room names if registry is unavailable.
        }

        return _registrySnapshot;
    }

    private async Task<Dictionary<string, DeviceDisplayOverrideEntity>> GetDisplayOverridesByPresentationKeyAsync(IEnumerable<HomeAssistantEntitySnapshotEntity> snapshots)
    {
        var keys = snapshots
            .Select(GetPresentationKey)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (keys.Count == 0)
        {
            return new Dictionary<string, DeviceDisplayOverrideEntity>(StringComparer.OrdinalIgnoreCase);
        }

        var overrides = await _db.DeviceDisplayOverrides
            .AsNoTracking()
            .Where(x => keys.Contains(x.PresentationKey))
            .ToListAsync();

        return overrides.ToDictionary(x => x.PresentationKey, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<List<HomeAssistantEntitySnapshotEntity>> GetPresentationGroupAsync(HomeAssistantEntitySnapshotEntity snapshot, bool asNoTracking)
    {
        IQueryable<HomeAssistantEntitySnapshotEntity> query = _db.HomeAssistantEntitySnapshots.Where(x => x.IsActive);
        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        if (!string.IsNullOrWhiteSpace(snapshot.RegistryDeviceId))
        {
            return await query
                .Where(x => x.RegistryDeviceId == snapshot.RegistryDeviceId)
                .ToListAsync();
        }

        if (!string.IsNullOrWhiteSpace(snapshot.DeviceKey))
        {
            return await query
                .Where(x => x.DeviceKey == snapshot.DeviceKey)
                .ToListAsync();
        }

        return await query
            .Where(x => x.EntityId == snapshot.EntityId)
            .ToListAsync();
    }

    private static RoomSensorProjection MapRoomSensorGroup(
        IGrouping<string, HomeAssistantEntitySnapshotEntity> group,
        IReadOnlyDictionary<string, DeviceDisplayOverrideEntity> overridesByPresentationKey)
    {
        var items = group.ToList();
        var primary = SelectPrimaryItem(items);
        var originalName = ResolveOriginalDisplayName(items);
        var overrideEntity = overridesByPresentationKey.GetValueOrDefault(group.Key);
        var presentationType = ResolvePresentationType(items);
        var temp = items.FirstOrDefault(x => x.Type == "temp");
        var humidity = items.FirstOrDefault(x => x.Type == "humidity");

        var status = presentationType == "environment"
            ? BuildEnvironmentStatus(temp, humidity, primary)
            : primary.RawState;

        var sensor = new SensorDto
        {
            Id = SelectRepresentativeEntityId(items, presentationType),
            OriginalName = originalName,
            Name = GetEffectiveDisplayName(originalName, overrideEntity),
            Type = presentationType,
            Status = status,
            Value = presentationType == "environment" ? temp?.NumericValue ?? humidity?.NumericValue : primary.NumericValue,
            Unit = presentationType == "environment" ? temp?.Unit ?? humidity?.Unit ?? primary.Unit : primary.Unit,
            Online = ResolvePresentationOnline(items, presentationType),
            UpdatedAt = items.Max(x => x.LastUpdatedUtc).ToLocalTime()
        };

        return new RoomSensorProjection(ResolvePresentationRoom(items), sensor);
    }

    private static CameraDto MapCameraGroup(
        IGrouping<string, HomeAssistantEntitySnapshotEntity> group,
        IReadOnlyDictionary<string, DeviceDisplayOverrideEntity> overridesByPresentationKey)
    {
        var items = group.ToList();
        var primary = SelectPrimaryCameraItem(items);
        var originalName = ResolveOriginalDisplayName(items);
        var overrideEntity = overridesByPresentationKey.GetValueOrDefault(group.Key);

        return new CameraDto
        {
            Id = primary.EntityId,
            OriginalName = originalName,
            Name = GetEffectiveDisplayName(originalName, overrideEntity),
            DisplayNameOverride = NormalizeDisplayNameOverride(overrideEntity?.DisplayNameOverride),
            Online = items.Where(x => x.Domain == "camera").Any(x => x.IsOnline),
            RoomName = ResolvePresentationRoom(items),
            SnapshotUrl = $"/api/cameras/snapshot?entityId={WebUtility.UrlEncode(primary.EntityId)}",
            StreamUrl = $"/api/cameras/stream?entityId={WebUtility.UrlEncode(primary.EntityId)}",
            Capabilities = items.SelectMany(x => DeserializeStringList(x.CapabilitiesJson))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static void ApplyInheritedRoomsByPresentationKey(IEnumerable<HomeAssistantEntitySnapshotEntity> rows)
    {
        var roomByPresentationKey = rows
            .Where(x => !string.IsNullOrWhiteSpace(x.RoomName))
            .GroupBy(GetPresentationKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.GroupBy(x => NormalizeRoom(x.RoomName!), StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(x => x.Count())
                    .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.Key)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows.Where(x => string.IsNullOrWhiteSpace(x.RoomName)))
        {
            if (roomByPresentationKey.TryGetValue(GetPresentationKey(row), out var roomName))
            {
                row.RoomName = roomName;
            }
        }
    }

    private static bool IsDiscoverable(HomeAssistantStateDto state) =>
        Domain(state.EntityId) is "sensor" or "binary_sensor" or "climate" or "media_player" or "camera" or "switch" or "light" or "fan" or "lock" or "cover";

    private static string Domain(string entityId) => entityId.Contains('.') ? entityId[..entityId.IndexOf('.')] : "unknown";

    private static string BuildDeviceKey(string entityId)
    {
        var id = NormalizeEntityId(entityId);
        var key = id.Contains('.') ? id[(id.IndexOf('.') + 1)..] : id;
        foreach (var suffix in DevicePartSuffixes)
        {
            if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                key = key[..^suffix.Length];
            }
        }

        return key;
    }

    private static string ResolveType(string entityId)
    {
        var id = NormalizeEntityId(entityId);
        var domain = Domain(id);
        var objectId = id.Contains('.') ? id[(id.IndexOf('.') + 1)..] : id;

        if (domain == "climate") return "climate";
        if (domain == "media_player") return "media";
        if (domain == "camera") return "camera";
        if (domain == "light") return "light";
        if (domain == "cover") return "cover";
        if (domain == "fan") return "fan";
        if (domain == "lock") return "lock";
        if (objectId.EndsWith("_temperature", StringComparison.OrdinalIgnoreCase)) return "temp";
        if (objectId.EndsWith("_humidity", StringComparison.OrdinalIgnoreCase)) return "humidity";
        if (objectId.EndsWith("_motion", StringComparison.OrdinalIgnoreCase) || objectId.Contains("motion", StringComparison.OrdinalIgnoreCase)) return "motion";
        if (objectId.EndsWith("_door", StringComparison.OrdinalIgnoreCase) || objectId.Contains("door", StringComparison.OrdinalIgnoreCase) || objectId.Contains("contact", StringComparison.OrdinalIgnoreCase)) return "door";
        if (objectId.EndsWith("_moisture", StringComparison.OrdinalIgnoreCase) || objectId.Contains("leak", StringComparison.OrdinalIgnoreCase) || objectId.Contains("water", StringComparison.OrdinalIgnoreCase)) return "leak";
        if (objectId.Contains("temperature", StringComparison.OrdinalIgnoreCase)) return "temp";
        if (objectId.Contains("humidity", StringComparison.OrdinalIgnoreCase)) return "humidity";
        return domain;
    }

    private static string? ResolveRoom(
        string entityId,
        string deviceKey,
        string displayName,
        IReadOnlyDictionary<string, string> bindings,
        IReadOnlyDictionary<string, string> registryRoomsByEntityId)
    {
        if (bindings.TryGetValue(deviceKey, out var bound))
        {
            return NormalizeRoom(bound);
        }

        if (registryRoomsByEntityId.TryGetValue(entityId, out var registryRoom))
        {
            return NormalizeRoom(registryRoom);
        }

        if (FallbackRoomByDeviceKey.TryGetValue(deviceKey, out var fallbackRoom))
        {
            return NormalizeRoom(fallbackRoom);
        }

        return InferRoom(displayName, entityId);
    }

    private static string? InferRoom(string name, string entityId)
    {
        var source = $"{name} {entityId}";
        foreach (var candidate in RoomInferenceTokens)
        {
            if (source.Contains(candidate.Key, StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Value;
            }
        }

        return null;
    }

    private static string NormalizeRoom(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return RoomAliases.TryGetValue(normalized, out var canonical)
            ? canonical
            : normalized;
    }

    private static string GetPresentationKey(HomeAssistantEntitySnapshotEntity snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.RegistryDeviceId))
        {
            return $"registry:{snapshot.RegistryDeviceId.Trim().ToLowerInvariant()}";
        }

        if (!string.IsNullOrWhiteSpace(snapshot.DeviceKey))
        {
            return $"device:{snapshot.DeviceKey.Trim().ToLowerInvariant()}";
        }

        return $"entity:{snapshot.EntityId.Trim().ToLowerInvariant()}";
    }

    private static string ResolvePresentationType(IReadOnlyList<HomeAssistantEntitySnapshotEntity> items)
    {
        if (items.Any(x => x.Domain == "camera"))
        {
            return "camera";
        }

        if (items.Any(x => x.Domain == "climate"))
        {
            return "climate";
        }

        if (items.Any(x => x.Domain == "media_player"))
        {
            return "media";
        }

        if (items.Any(x => x.Type == "temp") || items.Any(x => x.Type == "humidity"))
        {
            return "environment";
        }

        return SelectPrimaryItem(items).Type;
    }

    private static string ResolvePresentationRoom(IReadOnlyList<HomeAssistantEntitySnapshotEntity> items)
    {
        var roomName = items
            .Where(x => !string.IsNullOrWhiteSpace(x.RoomName))
            .GroupBy(x => NormalizeRoom(x.RoomName!), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Key)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(roomName) ? "Unassigned" : roomName;
    }

    private static bool ResolvePresentationOnline(IReadOnlyList<HomeAssistantEntitySnapshotEntity> items, string presentationType)
    {
        if (presentationType == "camera")
        {
            var cameraItems = items.Where(x => x.Domain == "camera").ToList();
            return cameraItems.Count == 0 ? items.Any(x => x.IsOnline) : cameraItems.Any(x => x.IsOnline);
        }

        var primary = SelectPrimaryItem(items);
        return primary.IsOnline || items.Any(x => x.IsOnline);
    }

    private static string SelectRepresentativeEntityId(IReadOnlyList<HomeAssistantEntitySnapshotEntity> items, string presentationType)
    {
        if (presentationType == "camera")
        {
            return SelectPrimaryCameraItem(items).EntityId;
        }

        if (presentationType == "media")
        {
            return SelectPrimaryMediaItem(items).EntityId;
        }

        if (presentationType == "environment")
        {
            return items.FirstOrDefault(x => x.Type == "temp")?.EntityId
                ?? items.FirstOrDefault(x => x.Type == "humidity")?.EntityId
                ?? SelectPrimaryItem(items).EntityId;
        }

        if (presentationType is "door" or "motion" or "leak")
        {
            return items
                .OrderBy(x => x.Domain == "binary_sensor" ? 0 : 1)
                .ThenBy(BatteryRank)
                .ThenByDescending(x => x.IsOnline)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.EntityId)
                .First();
        }

        return SelectPrimaryItem(items).EntityId;
    }

    private static HomeAssistantEntitySnapshotEntity SelectPrimaryItem(IReadOnlyList<HomeAssistantEntitySnapshotEntity> items)
    {
        return items
            .OrderBy(TypeRank)
            .ThenBy(BatteryRank)
            .ThenByDescending(x => x.IsOnline)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static HomeAssistantEntitySnapshotEntity SelectPrimaryCameraItem(IReadOnlyList<HomeAssistantEntitySnapshotEntity> items)
    {
        return items
            .OrderBy(CameraRank)
            .ThenByDescending(x => x.IsOnline)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static HomeAssistantEntitySnapshotEntity SelectPrimaryMediaItem(IReadOnlyList<HomeAssistantEntitySnapshotEntity> items)
    {
        return items
            .OrderBy(x => x.Domain == "media_player" ? 0 : 1)
            .ThenByDescending(x => x.IsOnline)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static int TypeRank(HomeAssistantEntitySnapshotEntity snapshot) => snapshot.Domain switch
    {
        "climate" => 0,
        "media_player" => 1,
        "camera" => 2,
        _ when snapshot.Type == "temp" => 3,
        _ when snapshot.Type == "humidity" => 4,
        _ when snapshot.Type == "motion" => 5,
        _ when snapshot.Type == "door" => 6,
        _ when snapshot.Type == "leak" => 7,
        "light" => 8,
        "switch" => 9,
        _ => 20
    };

    private static int BatteryRank(HomeAssistantEntitySnapshotEntity snapshot) =>
        snapshot.EntityId.Contains("_battery", StringComparison.OrdinalIgnoreCase) ||
        snapshot.DisplayName.Contains("battery", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;

    private static int CameraRank(HomeAssistantEntitySnapshotEntity snapshot)
    {
        if (snapshot.Domain == "camera" && !snapshot.EntityId.Contains("profile", StringComparison.OrdinalIgnoreCase)) return 0;
        if (snapshot.Domain == "camera") return 1;
        if (snapshot.Type == "motion") return 2;
        if (snapshot.Domain == "light") return 3;
        if (snapshot.Domain == "switch") return 4;
        return 10;
    }

    private static string ResolveOriginalDisplayName(IReadOnlyList<HomeAssistantEntitySnapshotEntity> items)
    {
        if (items.Count == 0)
        {
            return string.Empty;
        }

        if (items.Any(x => x.Domain == "camera"))
        {
            return NormalizeDisplayName(SelectPrimaryCameraItem(items).DisplayName);
        }

        var environmentNames = items
            .Where(x => x.Type is "temp" or "humidity")
            .Select(x => NormalizeDisplayName(x.DisplayName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sharedEnvironmentPrefix = GetSharedWordPrefix(environmentNames);
        if (!string.IsNullOrWhiteSpace(sharedEnvironmentPrefix))
        {
            return sharedEnvironmentPrefix;
        }

        var groupNames = items
            .Select(x => NormalizeDisplayName(x.DisplayName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sharedPrefix = GetSharedWordPrefix(groupNames);
        if (!string.IsNullOrWhiteSpace(sharedPrefix))
        {
            return sharedPrefix;
        }

        return NormalizeDisplayName(SelectPrimaryItem(items).DisplayName);
    }

    private static string? GetSharedWordPrefix(IReadOnlyList<string> names)
    {
        if (names.Count < 2)
        {
            return null;
        }

        var splitNames = names
            .Select(x => CollapseWhitespace(x)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(x => x.Length > 0)
            .ToList();

        if (splitNames.Count < 2)
        {
            return null;
        }

        var wordCount = splitNames.Min(x => x.Length);
        var sharedWords = new List<string>();

        for (var index = 0; index < wordCount; index++)
        {
            var candidate = splitNames[0][index];
            if (!splitNames.All(words => words[index].Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }

            sharedWords.Add(candidate);
        }

        if (sharedWords.Count < 2)
        {
            return null;
        }

        return NormalizeDisplayName(string.Join(" ", sharedWords));
    }

    private static string GetEffectiveDisplayName(string originalName, DeviceDisplayOverrideEntity? overrideEntity)
    {
        var overrideName = NormalizeDisplayNameOverride(overrideEntity?.DisplayNameOverride);
        return overrideName ?? originalName;
    }

    private static string? NormalizeDisplayNameOverride(string? value)
    {
        var trimmed = NormalizeDisplayName(value ?? string.Empty);
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string NormalizeDisplayName(string value)
    {
        return CollapseWhitespace(value).Trim();
    }

    private static string CollapseWhitespace(string value)
    {
        return string.Join(" ", (value ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string BuildEnvironmentStatus(
        HomeAssistantEntitySnapshotEntity? temp,
        HomeAssistantEntitySnapshotEntity? humidity,
        HomeAssistantEntitySnapshotEntity fallback)
    {
        var segments = new List<string>();
        if (temp?.NumericValue is not null)
        {
            segments.Add($"{temp.NumericValue:0.#}{temp.Unit}".Trim());
        }

        if (humidity?.NumericValue is not null)
        {
            segments.Add($"{humidity.NumericValue:0.#}{humidity.Unit}".Trim());
        }

        return segments.Count > 0 ? string.Join(" | ", segments) : fallback.RawState;
    }

    private static int RoomOrder(string name)
    {
        var idx = Array.FindIndex(PreferredRoomOrder, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        return idx < 0 ? int.MaxValue : idx;
    }

    private static string DefaultUnit(string type) => type switch
    {
        "temp" => "C",
        "humidity" => "%",
        "climate" => "C",
        _ => string.Empty
    };

    private static Dictionary<string, object?> NormalizeAttributes(Dictionary<string, object>? source)
    {
        if (source is null) return new();
        return source.ToDictionary(k => k.Key, v => NormalizeValue(v.Value), StringComparer.OrdinalIgnoreCase);
    }

    private static object? NormalizeValue(object? value) => value is JsonElement element
        ? element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var integerValue) ? integerValue : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray().Select(x => NormalizeValue(x)).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => NormalizeValue(p.Value), StringComparer.OrdinalIgnoreCase),
            _ => null
        }
        : value;

    private static Dictionary<string, object?> DeserializeDictionary(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? new()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(json)?
                .ToDictionary(k => k.Key, v => NormalizeValue(v.Value), StringComparer.OrdinalIgnoreCase)
                ?? new();

    private static List<string> DeserializeStringList(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];

    private static string? GetString(Dictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out var value) && value is not null ? value.ToString() : null;

    private static bool? GetBool(Dictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out var value) && value is not null && bool.TryParse(value.ToString(), out var result) ? result : null;

    private static List<string> GetStringList(Dictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out var value) && value is IEnumerable<object> objects
            ? objects.Select(x => x?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList()
            : [];

    private static int? ParseVolumePercent(Dictionary<string, object?> map)
    {
        var value = ParseDouble(GetString(map, "volume_level"));
        return value.HasValue
            ? Math.Clamp((int)Math.Round(value.Value * 100, MidpointRounding.AwayFromZero), 0, 100)
            : null;
    }

    private async Task<List<HomeAssistantHistoryStateDto>> GetNormalizedHistoryAsync(string entityId, int hours)
    {
        return (await _client.GetSensorHistoryAsync(entityId, hours))
            .OrderBy(x => x.LastChanged)
            .GroupBy(x => x.LastChanged)
            .Select(g => g.Last())
            .ToList();
    }

    private static SensorHistoryPointDto MapHistoryPoint(HomeAssistantHistoryStateDto point)
    {
        return new SensorHistoryPointDto
        {
            Timestamp = point.LastChanged.ToLocalTime(),
            Value = ParseDouble(point.State),
            State = point.State
        };
    }

    private static double? ParseDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return double.TryParse(value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed) ? parsed : null;
    }

    private static List<string> DetectCapabilities(string entityId, Dictionary<string, object?> attrs)
    {
        var caps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (entityId.StartsWith("camera.", StringComparison.OrdinalIgnoreCase))
        {
            caps.Add("snapshot");
            caps.Add("stream");
            caps.Add("record");
            caps.Add("ptz");
        }

        if (entityId.StartsWith("media_player.", StringComparison.OrdinalIgnoreCase))
        {
            caps.Add("power");
            caps.Add("volume");
            if (GetStringList(attrs, "source_list").Count > 0)
            {
                caps.Add("source_select");
            }
        }

        if (entityId.StartsWith("climate.", StringComparison.OrdinalIgnoreCase))
        {
            caps.Add("power");
            caps.Add("mode");
            if (GetString(attrs, "temperature") is not null)
            {
                caps.Add("set_temperature");
            }
        }

        return caps.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsUnavailable(string? state) =>
        state is not null && (state.Equals("unavailable", StringComparison.OrdinalIgnoreCase) || state.Equals("unknown", StringComparison.OrdinalIgnoreCase));

    private static bool IsBatteryEntity(HomeAssistantEntitySnapshotEntity snapshot)
    {
        if (snapshot.EntityId.Contains("_battery", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var attrs = DeserializeDictionary(snapshot.AttributesJson);
        return string.Equals(GetString(attrs, "device_class"), "battery", StringComparison.OrdinalIgnoreCase);
    }

    private static List<ServiceStatusDto> BuildServiceStatuses(List<HomeAssistantEntitySnapshotEntity> snapshots)
    {
        return snapshots
            .Where(IsServiceLike)
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .Select(x => new ServiceStatusDto
            {
                Id = x.EntityId,
                Name = x.DisplayName,
                Kind = x.Domain,
                Value = FormatServiceValue(x.RawState),
                Online = x.IsOnline,
                UpdatedAt = x.LastUpdatedUtc.ToLocalTime()
            })
            .ToList();
    }

    private static bool IsServiceLike(HomeAssistantEntitySnapshotEntity snapshot)
    {
        if (snapshot.Domain is "sun" or "weather" or "calendar" or "update")
        {
            return true;
        }

        if (snapshot.EntityId.StartsWith("sensor.sun_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return snapshot.DisplayName.StartsWith("Sun ", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatServiceValue(string raw)
    {
        if (DateTimeOffset.TryParse(raw, out var timestamp))
        {
            return timestamp.ToLocalTime().ToString("dd.MM HH:mm:ss");
        }

        return raw;
    }

    private static string NormalizeEntityId(string entityId) =>
        (entityId ?? string.Empty).Trim().ToLowerInvariant();

    private static bool IsMotionDetectedState(string? state) =>
        string.Equals(state, "on", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state, "detected", StringComparison.OrdinalIgnoreCase);

    private sealed record RoomSensorProjection(string RoomName, SensorDto Sensor);
}
