# SmartPanel v2 Stack (Recommended)

## Why this stack
- You already have working `ASP.NET Core` + `React` code in this repo.
- Fastest path to production on Raspberry Pi with minimal rewrite.
- Easy to extend with admin features and device mapping.

## Final stack
- Frontend: `React` + `Vite` + `React Router`
- API: `ASP.NET Core 8 Web API`
- Database: `PostgreSQL` in Docker (recommended), `SQLite` for quick local fallback
- ORM: `Entity Framework Core`
- Auth: `JWT` for admin/client roles
- Integration: `Home Assistant REST + WebSocket` via API service layer
- Deployment: Docker Compose on Raspberry Pi

## App structure
- `SmartPanel.React`:
  - `/admin/*` routes for management
  - `/panel/*` routes for wall display/client mode
- `SmartPanel.Api`:
  - Home Assistant adapter
  - business logic (scenes, rooms, devices)
  - auth/roles and API endpoints
- DB stores:
  - users and roles
  - device registry (entity mapping, room, visibility, sort order)
  - panel layouts and preferences
  - action logs

## Why PostgreSQL over only SQLite
- Better for concurrent admin + client requests.
- Safer for future growth (audit logs, schedules, multiple dashboards).
- Works well in Docker on Raspberry Pi.

If you want absolute minimum complexity for MVP, start SQLite first and switch to PostgreSQL later using EF migrations.
