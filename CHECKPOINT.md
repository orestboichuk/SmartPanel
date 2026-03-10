# SmartPanel Checkpoint

Last updated: `2026-03-07`

## Project
- Root: `c:\Projects\SmartPanel`
- Backend/API: `SmartPanel.Api`
- Blazor UI: `SmartPanel.Web`
- Shared contracts: `SmartPanel.Shared`
- Legacy frontend still present: `SmartPanel.React`

## Current state
- Home Assistant data is normalized into SQLite-backed snapshots instead of being read directly by the UI.
- Device grouping is presentation-centric and uses Home Assistant registry metadata where available.
- Rooms UI is now device-centric, not sensor-centric.
- Cameras and TVs are treated as controllable devices, not generic sensors.
- Environment devices expose separate temperature and humidity history plus battery level.

## Core DB model
- `ha_entity_snapshots`
- `device_room_bindings`
- `device_display_overrides`
- `direct_cameras`

## What is already implemented
- Legacy room bindings migration into normalized DB model.
- Camera control via `POST /api/cameras/control`:
  - PTZ
  - recording
  - motion detection
  - zoom/snapshot support in UI flow
- Dashboard admin operations:
  - `POST /api/dashboard/clear-data`
  - `POST /api/dashboard/clear-and-resync`
- Neo-style Blazor UI updates for:
  - Dashboard
  - Rooms
  - layout/navigation
- Motion diagnostics:
  - `GET /api/sensors/motion/debug`
  - `Logs` tab in details
  - history plus live fallback
  - `RUH ZARAZ` motion banner
- External services block on Dashboard.
- Device display-name overrides stored in DB without changing original HA names.
- Home Assistant registry-based room resolution using area/device/entity registries.
- Camera-related child entities grouped under logical camera devices instead of cluttering rooms.
- Environment device charts:
  - separate temperature and humidity series
  - SVG hover tooltips with time and value
  - battery level in device details
- Direct LAN camera path:
  - ONVIF WS-Discovery
  - manual IP or `device_service` add
  - protected credentials in DB
  - built-in browser live view
  - ONVIF PTZ control without Home Assistant

## Verified runtime
- API responds on `http://localhost:5203`
- Web responds on `http://localhost:5037`
- Last local check: `/api/rooms` -> `200`, `/rooms` -> `200`

## Main files to inspect first
- `INSTRUCTIONS.md`
- `CHANGELOG.md`
- `HANDOFF.md`
- `SmartPanel.Api/Services/HomeAssistantSmartHomeService.cs`
- `SmartPanel.Api/Services/HomeAssistantClient.cs`
- `SmartPanel.Api/Services/DirectCameraService.cs`
- `SmartPanel.Api/Data/SmartPanelDbBootstrapper.cs`
- `SmartPanel.Api/Data/SmartPanelDbContext.cs`
- `SmartPanel.Api/Data/DirectCameraEntity.cs`
- `SmartPanel.Web/Components/Pages/Rooms.razor`
- `SmartPanel.Web/Components/Pages/Cameras.razor`
- `SmartPanel.Web/Services/SmartHomeApiClient.cs`
- `SmartPanel.Shared/SensorDetailsDto.cs`

## Restart
```powershell
dotnet run --project SmartPanel.Api/SmartPanel.Api.csproj
dotnet run --project SmartPanel.Web/SmartPanel.Web.csproj
```

## Next reference
- Detailed handoff: `HANDOFF.md`
- Ready-to-paste new chat prompt: `NEW_CHAT_PROMPT.md`
