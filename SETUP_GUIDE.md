# SmartPanel v2 Setup Guide

## 1. Required software
- `Node.js 22 LTS`
- `npm 10+`
- `.NET SDK 8`
- `Docker Desktop` (for local PostgreSQL)
- `Git`

## 2. Home Assistant prerequisites
- Running Home Assistant (you already have `http://localhost:8123/`)
- Create Long-Lived Access Token:
  - Profile -> Security -> Long-Lived Access Tokens -> Create

## 3. Environment configuration
Create/update these values in API config:
- `HomeAssistant:BaseUrl`
- `HomeAssistant:Token`
- `ConnectionStrings:Default`
- `Jwt:Issuer`
- `Jwt:Audience`
- `Jwt:Key`

Example DB connection (Docker PostgreSQL):
`Host=localhost;Port=5432;Database=smartpanel;Username=smartpanel;Password=smartpanel`

## 4. Local run commands
API:
```powershell
dotnet run --project SmartPanel.Api/SmartPanel.Api.csproj
```

React:
```powershell
cd SmartPanel.React
npm install
npm run dev
```

## 5. First MVP scope
- Panel client:
  - dashboard
  - rooms
  - device controls
- Admin:
  - login
  - devices mapping (entity -> room/name/type)
  - scene management
  - user/role basics

## 6. Raspberry deployment target
- Use Docker Compose with containers:
  - `smartpanel-api`
  - `smartpanel-react` (or static Nginx)
  - `postgres`
- Keep Home Assistant URL in env vars so you can switch from local to LAN URL.
