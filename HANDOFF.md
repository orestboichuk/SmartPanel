# SmartPanel Handoff

Last updated: `2026-03-07`

## 1. Project snapshot

SmartPanel is a Home Assistant control panel built as:
- `SmartPanel.Api` - ASP.NET Core 8 API
- `SmartPanel.Web` - Blazor Server UI
- `SmartPanel.Shared` - shared DTOs/contracts

The current architecture is DB-backed. The UI no longer depends on raw Home Assistant state directly.

Current normalized storage:
- `ha_entity_snapshots`
- `device_room_bindings`
- `device_display_overrides`
- `direct_cameras`

## 2. Current runtime

At the moment of this handoff:
- API health checked at `http://localhost:5203`
- Web health checked at `http://localhost:5037`
- `GET /api/rooms` returned `200`
- `GET /rooms` returned `200`

Run commands if restart is needed:

```powershell
dotnet run --project SmartPanel.Api/SmartPanel.Api.csproj
dotnet run --project SmartPanel.Web/SmartPanel.Web.csproj
```

## 3. Major completed work

### Backend and data model
- Reworked backend to a normalized Home Assistant model in DB.
- Added legacy room-binding migration into the new model.
- Added persistent device display-name overrides so SmartPanel display names are editable without changing original HA names.
- Added `registry_device_id`-based presentation grouping.

### Home Assistant integration
- Added analysis of Home Assistant area/device/entity registries.
- Reworked room assignment priority:
  1. manual binding
  2. HA registry mapping
  3. `device_key` fallback
  4. text heuristics as last resort
- Fixed wrong room placement for devices such as kitchen temperature/humidity and media devices.
- Grouped camera child/settings entities under logical camera devices.

### Controls and device typing
- Added camera control via `POST /api/cameras/control`.
- Reintroduced TV/device controls by using HA device metadata instead of treating TVs as generic sensors.
- Exposed device details with HA-derived metadata such as:
  - `domain`
  - `device_class`
  - derived `capabilities`

### UI
- Reworked Blazor UI in neo-style for:
  - Dashboard
  - Rooms
  - layout/navigation
- Rooms became device-centric.
- Added inline display-name editing in device details.
- Added camera controls in Rooms/Cameras.
- Added TV controls in Rooms.

### Direct LAN cameras
- Added direct ONVIF camera support outside Home Assistant.
- Added ONVIF WS-Discovery and manual IP or `device_service` add flow.
- Added protected credential storage and metadata sync in `direct_cameras`.
- Added built-in browser live view for direct cameras through multipart snapshot streaming.
- Added direct ONVIF PTZ commands: pan, tilt, zoom, stop, and home.

### Motion diagnostics
- Added `Logs` tab in device details for motion sensors.
- Added history plus live fallback.
- Added `GET /api/sensors/motion/debug?...`
- Added `RUH ZARAZ` state banner in UI.

### Environment devices
- Added separate temperature and humidity history fields in details DTO.
- Built separate SVG charts for temperature and humidity.
- Added hover tooltips showing time and value.
- Added battery level to device details.

## 4. Key API endpoints

### Data
- `GET /api/dashboard`
- `GET /api/rooms`
- `GET /api/cameras`
- `GET /api/tvs`
- `GET /api/scenes`
- `GET /api/sensors/details?entityId=<id>&hours=24`

### Diagnostics
- `GET /api/sensors/motion/debug?entityId=<id>&hours=24&limit=50`

### Controls
- `POST /api/sensors/climate/control`
- `POST /api/tvs/control`
- `POST /api/cameras/control`
- `POST /api/rooms/display-name`
- `POST /api/scenes/{sceneName}/activate`

### Direct camera management
- `GET /api/cameras/direct`
- `POST /api/cameras/direct/discover`
- `POST /api/cameras/direct/manual`
- `POST /api/cameras/direct/{cameraId}/refresh`
- `DELETE /api/cameras/direct/{cameraId}`

### Admin actions
- `POST /api/dashboard/clear-data`
- `POST /api/dashboard/clear-and-resync`

## 5. Important implementation files

### API
- `SmartPanel.Api/Services/HomeAssistantSmartHomeService.cs`
- `SmartPanel.Api/Services/HomeAssistantClient.cs`
- `SmartPanel.Api/Services/DirectCameraService.cs`
- `SmartPanel.Api/Data/SmartPanelDbContext.cs`
- `SmartPanel.Api/Data/SmartPanelDbBootstrapper.cs`
- `SmartPanel.Api/Data/DeviceDisplayOverrideEntity.cs`
- `SmartPanel.Api/Data/DirectCameraEntity.cs`

### Shared DTOs
- `SmartPanel.Shared/SensorDetailsDto.cs`

### Web
- `SmartPanel.Web/Components/Pages/Rooms.razor`
- `SmartPanel.Web/Components/Pages/Cameras.razor`
- `SmartPanel.Web/Services/SmartHomeApiClient.cs`
- `SmartPanel.Web/wwwroot/app.css`

## 6. Verified details from code

The current codebase includes:
- `device_display_overrides` table in DB bootstrapper/context
- `BatteryLevel`, `TemperatureHistory`, `HumidityHistory` in `SensorDetailsDto`
- `/api/rooms/display-name` client call
- `/api/cameras/control` client call
- `/api/sensors/motion/debug` client call
- environment chart rendering and battery display in `Rooms.razor`

## 7. Good next tasks if work continues

These are sensible follow-ups, not blockers:
- surface `manufacturer`, `model`, and integration metadata from HA
- filter technical/helper entities from room cards even more aggressively
- replace browser-native SVG `<title>` tooltip with a custom floating tooltip
- extend rename UX to cameras/TV pages if needed

## 8. What to read first in a new chat

1. `HANDOFF.md`
2. `INSTRUCTIONS.md`
3. `CHANGELOG.md`
4. `SmartPanel.Api/Services/HomeAssistantSmartHomeService.cs`
5. `SmartPanel.Web/Components/Pages/Rooms.razor`
