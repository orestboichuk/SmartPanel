# New Chat Prompt

Paste the text below into a new chat, then add your next task at the end.

```text
Continue work on SmartPanel in `c:\Projects\SmartPanel`.

Before changing anything, read:
- `HANDOFF.md`
- `INSTRUCTIONS.md`
- `CHANGELOG.md`
- `CHECKPOINT.md`

Then inspect the current implementation in the files relevant to the task. Do not rely on documentation alone.

Current confirmed state:
- stack: `SmartPanel.Api` + `SmartPanel.Web` + `SmartPanel.Shared`
- active UI path is Blazor (`SmartPanel.Web`); `SmartPanel.React` is still in the repo but is not the primary app
- Home Assistant data flows through a normalized SQLite-backed model instead of directly into the UI
- main tables: `ha_entity_snapshots`, `device_room_bindings`, `device_display_overrides`, `direct_cameras`
- room assignment priority is manual binding, HA registry mapping, `device_key` fallback, then text heuristics
- devices are grouped at the presentation-device level using Home Assistant registry metadata where possible
- display-name overrides are persisted without changing the original Home Assistant name
- TVs and cameras are first-class controllable devices, not generic sensors
- environment devices expose separate temperature and humidity history plus battery level
- motion diagnostics, camera control, display-name editing, and dashboard admin actions are already implemented
- direct LAN cameras can now be discovered over ONVIF WS-Discovery or added manually by IP / `device_service`
- direct cameras have protected credentials, ONVIF metadata sync, built-in browser live view, and ONVIF PTZ controls

High-value files:
- `SmartPanel.Api/Services/HomeAssistantSmartHomeService.cs`
- `SmartPanel.Api/Services/HomeAssistantClient.cs`
- `SmartPanel.Api/Services/DirectCameraService.cs`
- `SmartPanel.Api/Data/SmartPanelDbBootstrapper.cs`
- `SmartPanel.Api/Data/SmartPanelDbContext.cs`
- `SmartPanel.Api/Data/DirectCameraEntity.cs`
- `SmartPanel.Api/Controllers/RoomsController.cs`
- `SmartPanel.Api/Controllers/CamerasController.cs`
- `SmartPanel.Web/Components/Pages/Rooms.razor`
- `SmartPanel.Web/Components/Pages/Cameras.razor`
- `SmartPanel.Web/Services/SmartHomeApiClient.cs`
- `SmartPanel.Shared/SensorDetailsDto.cs`

Locally verified URLs:
- API: `http://localhost:5203`
- Web: `http://localhost:5037`

Working expectations:
- inspect the current code before proposing or making changes
- prefer the real implementation over any stale documentation
- keep docs aligned if the task changes behavior materially

My next task:
<paste the next request here>
```
