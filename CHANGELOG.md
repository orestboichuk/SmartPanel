# Changelog

## 2026-03-07

### Device model and naming
- Added persistent display-name overrides in `device_display_overrides`.
- Preserved the original Home Assistant name separately from the SmartPanel display name.
- Added `registry_device_id`-based presentation grouping so one physical device can map to one logical UI device.

### Home Assistant device typing
- Extended device details to expose Home Assistant `domain`, `device_class`, and derived `capabilities`.
- Added media-specific detail fields such as `source`, `availableSources`, `app`, `volumePercent`, and `muted`.
- Updated device classification so TVs are treated as `media_player` devices instead of generic sensors.

### Rooms and controls
- Reworked `Rooms` to be device-centric instead of sensor-centric.
- Added inline display-name editing in device details.
- Added TV controls directly in `Rooms` details: power, play/pause, volume, mute, and source select.
- Added camera controls directly in `Rooms` details: PTZ, zoom, record, motion detection toggle, and snapshot access.

### Cameras
- Grouped camera-related Home Assistant entities into logical camera devices.
- Removed camera child/settings entities from cluttering room device lists.
- Added live camera control actions to the `/cameras` page.

### Environment sensors
- Added separate `TemperatureHistory` and `HumidityHistory` series in sensor details.
- Added `BatteryLevel` to environment device details for replacement planning.
- Replaced the simple numeric history sparkline with separate temperature/humidity charts in `Rooms`.
- Added hover tooltips on environment charts to show time and value for the hovered point.

### Direct LAN cameras
- Added a separate direct-camera path in SmartPanel that does not depend on Home Assistant entities.
- Added ONVIF WS-Discovery support for local-network camera discovery.
- Added manual camera add and edit flow by IP or explicit ONVIF `device_service` URL.
- Added protected credential storage for direct cameras in `direct_cameras`.
- Added ONVIF metadata sync for media/PTZ endpoints, profiles, snapshot URI, and RTSP stream URI.
- Added built-in browser live view for direct cameras via multipart snapshot streaming.
- Added manual snapshot URL and MJPEG live URL overrides for direct cameras that only expose RTSP over ONVIF.
- Added configurable `ffmpeg`-based RTSP fallback so direct cameras can expose browser live view and one-off snapshots even when ONVIF only returns RTSP.
- Added direct PTZ camera control commands: pan, tilt, zoom, stop, and home.

### Notes
- Current device metadata is derived from Home Assistant state attributes and normalized snapshot data.
- Manufacturer/model/integration metadata is not yet surfaced in the UI.
