# SmartPanel

SmartPanel is a Home Assistant dashboard built on:
- `SmartPanel.Api` - ASP.NET Core 8 Web API
- `SmartPanel.Web` - Blazor Server UI
- `SmartPanel.Shared` - shared DTO/contracts

Current working mode:
- Home Assistant data is synced into a local SQLite database
- UI reads normalized snapshots from the API
- sensor diagnostics and history are loaded on demand
- no direct live push from Home Assistant to the UI

## Quick Start

### 1. Configure API
Check `SmartPanel.Api/appsettings.json` values:
- `HomeAssistant:BaseUrl`

Set local secrets in `SmartPanel.Api/appsettings.Development.json`:
- `HomeAssistant:Token`

`appsettings.Development.json` is intended for local-only values and is ignored by git.

Default local DB:
- `ConnectionStrings:SmartPanelDb = Data Source=smartpanel.db`

### 2. Configure Web
Check `SmartPanel.Web/appsettings.json`:
- `Api:BaseUrl`

Default value:
- `http://localhost:5203/`

### 3. Build
```powershell
dotnet build SmartPanel.Api/SmartPanel.Api.csproj -v minimal
dotnet build SmartPanel.Web/SmartPanel.Web.csproj -v minimal
```

### 4. Run API
```powershell
Set-Location C:\Projects\SmartPanel\SmartPanel.Api
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:5203"
dotnet bin/Debug/net8.0/SmartPanel.Api.dll
```

### 5. Run Web
```powershell
Set-Location C:\Projects\SmartPanel\SmartPanel.Web
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:5037"
dotnet bin/Debug/net8.0/SmartPanel.Web.dll
```

### 6. Open UI
- `http://localhost:5037`
- Swagger: `http://localhost:5203/swagger`

### 7. Background launch variant
```powershell
Start-Process -FilePath "cmd.exe" -ArgumentList '/c','set ASPNETCORE_ENVIRONMENT=Development&& set ASPNETCORE_URLS=http://localhost:5203&& dotnet bin/Debug/net8.0/SmartPanel.Api.dll' -WorkingDirectory "C:\Projects\SmartPanel\SmartPanel.Api"
Start-Process -FilePath "cmd.exe" -ArgumentList '/c','set ASPNETCORE_ENVIRONMENT=Development&& set ASPNETCORE_URLS=http://localhost:5037&& dotnet bin/Debug/net8.0/SmartPanel.Web.dll' -WorkingDirectory "C:\Projects\SmartPanel\SmartPanel.Web"
```

## What Is Implemented

### Latest update branch
- branch: `feature/climate-room-type-overrides`
- climate page now resolves the same AC device/name/room as `Rooms`
- climate control panel in `Rooms` matches the dedicated `/climate` control layout
- each device can now have a manual SmartPanel-only type override from `Rooms`
- climate button highlight logic is aligned with device state:
  - when on: `Power On` and the active mode are highlighted
  - when off: only `Power Off` is highlighted

### Backend and data model
- normalized Home Assistant snapshot storage in `ha_entity_snapshots`
- room binding storage in `device_room_bindings`
- device type override storage in `device_type_overrides`
- legacy room assignment migration during bootstrap
- local SQLite bootstrap without manual migration step
- admin bootstrap user and JWT auth configuration

### Dashboard
- KPI cards and room/camera overview
- `External Services` block for `sun` and similar service entities
- admin actions:
  - `clear-data`
  - `clear-and-resync`

### Rooms and diagnostics
- neo-style Rooms UI
- sensor details panel with tabs
- `Logs` tab for motion sensors
- motion detection from history with live fallback
- `RUH ZARAZ` state banner in UI
- manual device type override in `Rooms -> Details`
- climate device details in `Rooms` use the same control style as `/climate`
- endpoint: `GET /api/sensors/motion/debug`

### Cameras
- camera list, snapshot and stream proxy endpoints
- camera control endpoint: `POST /api/cameras/control`
- supported commands include PTZ, recording, motion detection toggles

### Other device areas
- scenes page and activation
- TV list and control API
- climate control from sensor details
- dedicated climate page uses the same device grouping/display metadata as `Rooms`

## Main Endpoints

### Snapshot and pages
- `GET /api/dashboard`
- `GET /api/rooms`
- `GET /api/cameras`
- `GET /api/tvs`
- `GET /api/scenes`

### Details and diagnostics
- `GET /api/sensors/details?entityId=<id>&hours=24`
- `GET /api/sensors/motion/debug?entityId=<id>&hours=24&limit=50`

### Control
- `POST /api/sensors/climate/control`
- `POST /api/rooms/device-type`
- `POST /api/tvs/control`
- `POST /api/cameras/control`
- `POST /api/scenes/{sceneName}/activate`

### Admin actions
- `POST /api/dashboard/clear-data`
- `POST /api/dashboard/clear-and-resync`

### Home Assistant connectivity test
- `GET /api/homeassistanttest/status`
- `GET /api/homeassistanttest/states`

## Main UI Routes
- `/` - Dashboard
- `/rooms`
- `/cameras`
- `/tv`
- `/scenes`

## Auth and admin

Configured in `SmartPanel.Api/appsettings.json`:
- `Jwt`
- `AdminBootstrap`

Default bootstrap credentials:
- username: `admin`
- password: `admin12345`

Change these before production use.

## Troubleshooting

### UI or API is not reachable
Check that both apps are running:
```powershell
Invoke-WebRequest http://localhost:5203/swagger
Invoke-WebRequest http://localhost:5037
```

If you launch the compiled DLLs directly, start each app from its own project directory. `SmartPanel.Web` can fail if it is started from the solution root because static files and content root resolution depend on the working directory.

### Home Assistant returns empty data
Check:
- `HomeAssistant:BaseUrl`
- `HomeAssistant:Token`
- Home Assistant availability
- recorder/history availability for motion and sensor history

### Build fails because files are locked
Stop running processes first:
```powershell
Get-Process SmartPanel.Api,SmartPanel.Web,dotnet -ErrorAction SilentlyContinue | Stop-Process -Force
```

## Notes
- `SmartPanel.React` exists in the repo, but the main documented local run path is `SmartPanel.Api` + `SmartPanel.Web`.
- current docs are aligned to the Blazor UI and normalized DB-backed backend flow.
