# SmartPanel v2 Implementation Plan

## Phase 1: Foundation
- Keep existing `SmartPanel.Api` and `SmartPanel.React` projects.
- Add role model: `Admin`, `Client`.
- Add auth endpoints and JWT middleware.
- Add DB entities for users, roles, device registry, panel layout.

## Phase 2: Home Assistant integration hardening
- Separate provider layer:
  - `IHomeAssistantClient`
  - `IHomeDeviceService`
- Add retry and timeout policies.
- Add mapping cache with DB as source of truth.

## Phase 3: Admin app
- Routes under `/admin`
- Pages:
  - Login
  - Devices Registry
  - Rooms
  - Scenes
  - Users/Roles
- CRUD connected to API

## Phase 4: Client panel
- Routes under `/panel`
- Large-touch UI for TV/tablet wall display.
- Read-only widgets + control actions.
- Optional kiosk mode.

## Phase 5: Deploy on Raspberry Pi
- Build multi-arch Docker images.
- Configure compose with env vars and volumes.
- Add health checks and auto-restart policy.

## Phase 6: Observability and safety
- Action audit logs (who changed what).
- Device command throttling/rate limits.
- Backup script for DB.
