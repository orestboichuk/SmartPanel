# SmartPanel Instructions

## 1. Purpose

SmartPanel is a Home Assistant control panel with:
- `SmartPanel.Api` - ASP.NET Core 8 Web API
- `SmartPanel.Web` - Blazor Server UI
- `SmartPanel.Shared` - shared DTOs

Current architecture is database-backed:
- Home Assistant entities are normalized into `ha_entity_snapshots`
- room assignment is stored in `device_room_bindings`
- legacy `device_room_assignments` data is migrated during bootstrap
- UI works against API snapshots, not against raw Home Assistant state directly
- direct LAN cameras can also be managed outside Home Assistant and are stored in `direct_cameras`

Core principle:
- no direct live push to the Blazor UI
- API performs snapshot sync from Home Assistant
- details and diagnostics are loaded on demand
- direct ONVIF cameras use a separate path and do not require a Home Assistant `camera.*` entity

## 2. Run Locally

### Prerequisites
- .NET 8 SDK
- running Home Assistant instance
- Home Assistant Long-Lived Access Token
- `ffmpeg` installed for direct-camera RTSP fallback
- `MediaMTX` installed for low-latency direct-camera playback

### API config
File:
- `SmartPanel.Api/appsettings.json`

Required values:
- `HomeAssistant:BaseUrl`
- `HomeAssistant:Token`

Other important values:
- `ConnectionStrings:SmartPanelDb`
- `Jwt`
- `AdminBootstrap`
- `CameraTranscoding`
- `MediaMtx`

Default DB:
- `Data Source=smartpanel.db`

### Web config
File:
- `SmartPanel.Web/appsettings.json`

Required value:
- `Api:BaseUrl`

Default value:
- `http://localhost:5203/`

### Start
1. Run API:
```powershell
dotnet run --project SmartPanel.Api/SmartPanel.Api.csproj
```
2. Run Web:
```powershell
dotnet run --project SmartPanel.Web/SmartPanel.Web.csproj
```
3. Open:
- `http://localhost:5037`
- `http://localhost:5203/swagger`

## 3. Current Data Flow

### Snapshot sync
Main API services call Home Assistant REST and persist normalized snapshots in SQLite.

Main table:
- `ha_entity_snapshots`

Fields include:
- `entity_id`
- `domain`
- `device_key`
- `display_name`
- `type`
- `room_name`
- `raw_state`
- `numeric_value`
- `attributes_json`
- `capabilities_json`
- `last_updated_utc`
- `last_seen_utc`
- `is_active`

Room binding table:
- `device_room_bindings`

Direct camera table:
- `direct_cameras`

This makes it possible to:
- group multiple HA entities into one logical device
- keep room mapping stable
- re-sync data without losing room assignments

Direct camera records store:
- ONVIF `device_service` URL
- media and PTZ service metadata
- snapshot URI and RTSP URI
- protected credentials
- discovered profiles and selected profile token
- optional manual snapshot and MJPEG override URLs

Current direct-camera playback priority:
1. MediaMTX WebRTC player
2. manual MJPEG override
3. browser-safe snapshot loop
4. `ffmpeg` RTSP fallback

### Legacy migration
During bootstrap the app:
- ensures required tables exist
- migrates legacy `device_room_assignments` into `device_room_bindings`
- ensures auth tables exist
- creates bootstrap admin user if needed

## 4. Main API Endpoints

### Snapshot endpoints
- `GET /api/dashboard`
- `GET /api/rooms`
- `GET /api/cameras`
- `GET /api/tvs`
- `GET /api/scenes`

### Direct camera endpoints
- `GET /api/cameras/direct`
- `POST /api/cameras/direct/discover`
- `POST /api/cameras/direct/manual`
- `POST /api/cameras/direct/{cameraId}/refresh`
- `DELETE /api/cameras/direct/{cameraId}`
- `GET /api/cameras/snapshot?entityId=<direct:id>`
- `GET /api/cameras/stream?entityId=<direct:id>`

### Sensor details and diagnostics
- `GET /api/sensors/details?entityId=<id>&hours=24`
- `GET /api/sensors/motion/debug?entityId=<id>&hours=24&limit=50`

Motion diagnostics endpoint returns:
- current state
- last changed timestamps
- online flag
- debug history points

### Controls
- `POST /api/sensors/climate/control`
- `POST /api/tvs/control`
- `POST /api/cameras/control`
- `POST /api/scenes/{sceneName}/activate`

### Dashboard admin actions
- `POST /api/dashboard/clear-data`
- `POST /api/dashboard/clear-and-resync`

### Home Assistant test endpoints
- `GET /api/homeassistanttest/status`
- `GET /api/homeassistanttest/states`

## 5. Current UI Areas

### Dashboard
- neo-style dashboard layout
- KPI cards for house state, alerts, sensors, cameras
- room heatmap and environment summary
- scenes block
- camera overview
- `External Services` block for `sun` and similar service entities
- admin buttons:
  - `Clear All Data`
  - `Clear + Resync`

### Rooms
- neo-style room cards and room overview
- search and status filters
- grouped device rendering by logical device
- sensor detail card
- climate controls inside sensor details
- history chart for numeric sensors

### Motion diagnostics
- `Logs` tab in Sensor Details
- motion history from Home Assistant history API
- live fallback when recorder history is sparse
- visual state banner `RUH ZARAZ`
- debug metadata from `/api/sensors/motion/debug`

### Cameras
- camera list page
- snapshot proxy
- stream proxy
- control support through `/api/cameras/control`
- direct LAN camera manager in `/cameras`
- ONVIF discovery, manual IP add, and built-in browser live view for direct cameras
- MediaMTX-backed WebRTC and HLS player URLs for direct RTSP cameras
- `ffmpeg` RTSP fallback when the browser cannot read the source directly
- direct cameras now support separate control and live profiles
- `/cameras` now exposes profile selectors for:
  - control profile token
  - live preview profile token
  - auto live-profile selection prefers likely substream profiles when available

Supported camera command types:
- PTZ direction
- zoom
- recording
- motion detection enable or disable

Supported direct camera command types:
- PTZ direction
- zoom
- stop
- home

### TVs and scenes
- TV page with device control support
- scenes page with activation

## 6. Authentication and Admin API

Configured in:
- `SmartPanel.Api/appsettings.json`

Relevant sections:
- `Jwt`
- `AdminBootstrap`

Default bootstrap credentials:
- username: `admin`
- password: `admin12345`

Protected admin endpoints present in the API:
- `GET/PUT /api/admin/devices/{id}`
- `GET/PUT /api/admin/layouts/{id}`
- `GET /api/admin/users`
- `POST /api/auth/login`
- `GET /api/auth/me`

Change bootstrap credentials and JWT key before any production use.

## 7. Main Blazor Routes

- `/` - Dashboard
- `/rooms`
- `/cameras`
- `/tv`
- `/scenes`

Other pages exist in the project, but the main control surface is built around the routes above.

## 8. Key Files

### API
- `SmartPanel.Api/Program.cs`
- `SmartPanel.Api/Services/ISmartHomeService.cs`
- `SmartPanel.Api/Services/HomeAssistantSmartHomeService.cs`
- `SmartPanel.Api/Services/HomeAssistantClient.cs`
- `SmartPanel.Api/Services/IDirectCameraService.cs`
- `SmartPanel.Api/Services/DirectCameraService.cs`
- `SmartPanel.Api/Services/CameraTranscodingOptions.cs`
- `SmartPanel.Api/Services/MediaMtxOptions.cs`
- `SmartPanel.Api/Services/MediaMtxService.cs`
- `SmartPanel.Api/Data/SmartPanelDbContext.cs`
- `SmartPanel.Api/Data/SmartPanelDbBootstrapper.cs`

### Controllers
- `SmartPanel.Api/Controllers/DashboardController.cs`
- `SmartPanel.Api/Controllers/RoomsController.cs`
- `SmartPanel.Api/Controllers/SensorsController.cs`
- `SmartPanel.Api/Controllers/CamerasController.cs`
- `SmartPanel.Api/Controllers/TvsController.cs`
- `SmartPanel.Api/Controllers/ScenesController.cs`

### Web
- `SmartPanel.Web/Services/SmartHomeApiClient.cs`
- `SmartPanel.Web/Components/Pages/Home.razor`
- `SmartPanel.Web/Components/Pages/Rooms.razor`
- `SmartPanel.Web/Components/Pages/Cameras.razor`
- `SmartPanel.Web/Components/Pages/Tv.razor`
- `SmartPanel.Web/Components/Pages/Scenes.razor`
- `SmartPanel.Web/wwwroot/app.css`

## 9. Troubleshooting

### API starts but data is empty
Check:
- `HomeAssistant:BaseUrl`
- `HomeAssistant:Token`
- Home Assistant availability
- whether the entities really exist in Home Assistant

Useful test endpoints:
- `/api/homeassistanttest/status`
- `/api/homeassistanttest/states`

### Motion logs are missing
Check:
- Home Assistant recorder/history availability
- correct sensor entity id
- whether current state is still available through live fallback

### Direct camera discovery finds nothing
Check:
- camera ONVIF support is enabled
- local firewall or VLAN is not blocking UDP multicast discovery on `239.255.255.250:3702`
- try manual add with explicit `http://<ip>/onvif/device_service`

### Direct camera live view is unavailable
Check:
- camera exposes an ONVIF snapshot URI
- stored credentials are valid
- refresh the direct camera metadata from `/cameras`
- `ffmpeg` exists at the configured `CameraTranscoding:FfmpegPath`
- `mediamtx.exe` exists at the configured `MediaMtx:BinaryPath`
- WebRTC/HLS base URLs are correct for the device that opens SmartPanel
- `MediaMtx:SourceRtspTransport` is currently expected to be `automatic`
- if the camera is `Online` and snapshot works but WebRTC/HLS is black, the failure is usually in the RTSP source path, not in the Blazor page itself

### Direct camera is online but WebRTC/HLS preview is black
Most likely causes:
- ONVIF returned an RTSP URI that accepts the session but does not deliver video frames
- selected live profile points to a broken substream
- camera RTSP path or port differs from what ONVIF reported

Practical checks:
- compare control profile vs live preview profile in `/cameras`
- try forcing the live preview profile back to the control/main profile
- verify the RTSP URL in VLC:
  - `rtsp://<user>:<pass>@<ip>:<port>/main`
  - `rtsp://<user>:<pass>@<ip>:<port>/sub`
- if VLC also hangs or shows no frames, the issue is the camera RTSP source or path
- if VLC works but SmartPanel stays black, inspect MediaMTX path stats on `http://localhost:9997`

Known diagnostic sign:
- MediaMTX path is `ready` and `online`, but `bytesReceived` stays near zero and does not grow
- this means the RTSP source was opened but real video frames are not arriving

### API runs as a service but `/api/dashboard`, `/api/rooms`, and `/api/cameras` return 500
If the log says:
- `System.InvalidOperationException: HomeAssistant:BaseUrl is not configured`

Then the usual cause is:
- `systemd` service uses the solution root as `WorkingDirectory`
- environment-specific `appsettings.Development.json` is not being resolved from the project folder

Required fix on Raspberry Pi:
- API service must use:
  - `WorkingDirectory=/home/orest/projects/SmartPanel/SmartPanel.Api`
- Web service must use:
  - `WorkingDirectory=/home/orest/projects/SmartPanel/SmartPanel.Web`

After updating the unit files:
```bash
sudo systemctl daemon-reload
sudo systemctl restart smartpanel-api.service
sudo systemctl restart smartpanel-web.service
```

Quick verification:
```bash
curl http://127.0.0.1:5203/api/dashboard
curl http://127.0.0.1:5037
```

### Direct camera credentials stop working after moving the DB from Windows to Raspberry Pi
Typical log warning:
- `Unable to unprotect direct camera credential`

Cause:
- direct-camera passwords are protected with ASP.NET Data Protection keys
- Windows-encrypted values cannot be decrypted on the Raspberry Pi key ring

Fix:
- open the direct camera in `/cameras`
- re-enter the username and password on Raspberry Pi
- save the camera again so credentials are re-protected with the Pi key set

### Build fails with locked files
Stop running processes first:
```powershell
Get-Process SmartPanel.Api,SmartPanel.Web,dotnet -ErrorAction SilentlyContinue | Stop-Process -Force
```

### Ports
Default local ports:
- API: `5203`
- Web: `5037`
- MediaMTX RTSP: `8554`
- MediaMTX HLS: `8888`
- MediaMTX WebRTC player: `8889`
- MediaMTX API: `9997`
- MediaMTX WebRTC local UDP: `8189`

## 10. Notes

- `SmartPanel.React` is still present in the repo, but the primary documented local run flow is `SmartPanel.Api` + `SmartPanel.Web`.
- current docs describe the Blazor UI and the normalized DB-backed backend, because that is the active working path in code.
- there is no `.git` directory in this workspace right now, so a git commit checkpoint cannot be created from here.

## 11. Checkpoint 2026-03-07

Current verified direct-camera state:
- direct ONVIF cameras are persisted in `direct_cameras`
- direct cameras support ONVIF discovery, manual add/edit, metadata refresh, PTZ, stop, and home
- manual snapshot and MJPEG override URLs are supported
- local `ffmpeg` fallback is enabled for RTSP snapshot and MJPEG-style browser fallback
- local `MediaMTX` is integrated and preferred for low-latency playback
- selected direct camera preview in `/cameras` now prefers MediaMTX WebRTC over MJPEG fallback

Current local runtime:
- `SmartPanel.Api` serves on `http://localhost:5203`
- `SmartPanel.Web` serves on `http://localhost:5037`
- `MediaMTX` serves WebRTC player pages on `http://localhost:8889`
- `MediaMTX` serves HLS on `http://localhost:8888`

Current tested camera:
- host `192.168.1.40`
- ONVIF `device_service`: `http://192.168.1.40:835/onvif/device_service`
- selected profile token: `Profile_token2`
- current RTSP source: `rtsp://192.168.1.40:8554/sub`
- MediaMTX player URL pattern: `http://localhost:8889/direct_<cameraId>`

Important deployment note:
- current `MediaMtx:WebRtcPlayerBaseUrl` and `MediaMtx:HlsPlayerBaseUrl` use `localhost`
- this is correct only when SmartPanel is opened on the same machine
- for phone/tablet/another PC access, replace `localhost` with the LAN IP of the SmartPanel host

## 12. Checkpoint 2026-03-08

Current direct-camera low-latency state:
- direct cameras now store separate control and live profile metadata
- new DB fields exist for:
  - `preferred_live_profile_token`
  - `live_profile_token`
  - `live_snapshot_uri`
  - `live_stream_uri`
- `/cameras` now shows dropdown selectors for control and live profile tokens when ONVIF profiles are known
- MediaMTX config now uses:
  - `hlsVariant: lowLatency`
  - `hlsSegmentDuration: 1s`
  - `hlsPartDuration: 200ms`
  - `rtspTransport: automatic`

Current tested camera findings:
- host `192.168.1.40`
- ONVIF profiles discovered:
  - `Profile_token1`
  - `Profile_token2`
- control profile can be separated from live preview profile
- in the observed failing state, ONVIF discovery and snapshot path worked, but RTSP live preview did not deliver frames reliably
- MediaMTX path could become `ready` and `online` while still receiving almost no bytes from the RTSP source

Important diagnostic conclusion:
- a black built-in WebRTC/HLS player does not automatically mean the UI or iframe is broken
- if direct-camera snapshot works and RTSP does not, troubleshoot the camera RTSP URI, profile, and port first

## 13. Raspberry Pi 4 Test Deployment 2026-03-08

Target that was tested:
- Raspberry Pi 4
- Debian 12 / `aarch64`
- VS Code Remote SSH workflow

Verified runtime dependencies on the Pi:
- `.NET 8` installed under `/home/orest/.dotnet`
- `ffmpeg` available at `/usr/bin/ffmpeg`
- `MediaMTX` binary placed at `/home/orest/projects/SmartPanel/tools/mediamtx`

Verified API development override on the Pi:
- file: `SmartPanel.Api/appsettings.Development.json`
- key values:
  - original migration value: `HomeAssistant:BaseUrl = http://192.168.1.180:8123`
  - after Home Assistant restore on the same Pi: `HomeAssistant:BaseUrl = http://127.0.0.1:8123`
  - `CameraTranscoding:FfmpegPath = /usr/bin/ffmpeg`
  - `MediaMtx:BinaryPath = /home/orest/projects/SmartPanel/tools/mediamtx`
  - `MediaMtx:WorkingDirectory = /home/orest/projects/SmartPanel/.runtime/mediamtx`
  - `MediaMtx:ConfigFilePath = /home/orest/projects/SmartPanel/.runtime/mediamtx/mediamtx.yml`
  - `MediaMtx:RtspAddress = 0.0.0.0:8554`
  - `MediaMtx:HlsAddress = 0.0.0.0:8888`
  - `MediaMtx:WebRtcAddress = 0.0.0.0:8889`
  - `MediaMtx:WebRtcAdditionalHosts = [\"192.168.1.96\"]`
  - `MediaMtx:WebRtcPlayerBaseUrl = http://192.168.1.96:8889`
  - `MediaMtx:HlsPlayerBaseUrl = http://192.168.1.96:8888`
  - `MediaMtx:SourceRtspTransport = automatic`

Verified Web development override on the Pi:
- file: `SmartPanel.Web/appsettings.Development.json`
- key value:
  - `Api:BaseUrl = http://127.0.0.1:5203/`

Manual run that was verified:
```bash
cd ~/projects/SmartPanel
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5203 dotnet run --no-launch-profile --project SmartPanel.Api/SmartPanel.Api.csproj
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5037 dotnet run --no-launch-profile --project SmartPanel.Web/SmartPanel.Web.csproj
```

Working `systemd` layout that was verified:
- `smartpanel-api.service`
  - `WorkingDirectory=/home/orest/projects/SmartPanel/SmartPanel.Api`
  - `ExecStart=/home/orest/.dotnet/dotnet /home/orest/projects/SmartPanel/SmartPanel.Api/bin/Debug/net8.0/SmartPanel.Api.dll`
  - `Environment=ASPNETCORE_ENVIRONMENT=Development`
  - `Environment=ASPNETCORE_URLS=http://0.0.0.0:5203`
- `smartpanel-web.service`
  - `WorkingDirectory=/home/orest/projects/SmartPanel/SmartPanel.Web`
  - `ExecStart=/home/orest/.dotnet/dotnet /home/orest/projects/SmartPanel/SmartPanel.Web/bin/Debug/net8.0/SmartPanel.Web.dll`
  - `Environment=ASPNETCORE_ENVIRONMENT=Development`
  - `Environment=ASPNETCORE_URLS=http://0.0.0.0:5037`

Minimal future restart flow on the Pi:
```bash
sudo systemctl restart smartpanel-api.service
sudo systemctl restart smartpanel-web.service
curl http://127.0.0.1:5203/api/dashboard
curl http://127.0.0.1:5037
```

What is already confirmed on the Pi:
- API service starts
- Web service starts
- Home Assistant is reachable from the Pi
- Home Assistant backup was restored onto the same Raspberry Pi
- SmartPanel API successfully reads Home Assistant from `http://127.0.0.1:8123`
- Dashboard and main Blazor UI render correctly from `http://127.0.0.1:5037`

What remains unresolved from the test deployment:
- direct-camera live preview is still camera/RTSP-path dependent and was not fixed by moving to Raspberry Pi
- old direct-camera credentials copied from Windows need to be re-saved on the Pi
- Chromium full-screen kiosk autostart was not yet made reliable on this test image and should be treated as a separate follow-up task

### Home Assistant Container move from laptop to Raspberry Pi

Migration approach that was used successfully:
1. create a Home Assistant backup on the laptop installation
2. install Docker on Raspberry Pi OS
3. run Home Assistant Container on the Pi
4. restore the backup onto the Pi installation
5. switch SmartPanel API from laptop HA URL to `http://127.0.0.1:8123`

Example container run:
```bash
docker run -d \
  --name homeassistant \
  --restart=unless-stopped \
  --network=host \
  -e TZ=Europe/Kiev \
  -v /home/orest/homeassistant/config:/config \
  ghcr.io/home-assistant/home-assistant:stable
```

Minimal verification after restore:
```bash
curl http://127.0.0.1:8123
curl http://127.0.0.1:5203/api/dashboard
```

Operational result:
- Raspberry Pi no longer depends on the laptop being powered on
- Home Assistant and SmartPanel can run as a self-contained local stack on the Pi

Minimal restart flow for the autonomous Pi setup:
```bash
docker restart homeassistant
sudo systemctl restart smartpanel-api.service
sudo systemctl restart smartpanel-web.service
curl http://127.0.0.1:8123
curl http://127.0.0.1:5203/api/dashboard
curl http://127.0.0.1:5037
```
