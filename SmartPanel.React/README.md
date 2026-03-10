# SmartPanel.React

React frontend for SmartPanel API (`SmartPanel.Api`).

## Prerequisites

- Node.js 20+
- Running API on `http://localhost:5203`

## Run

```bash
cd SmartPanel.React
npm install
npm run dev
```

Open: `http://localhost:5173`

## Notes

- Vite proxy is configured so `/api/*` calls go to `http://localhost:5203`.
- Includes pages:
  - Dashboard
  - Rooms (sensor details/history + climate control)
  - Cameras
  - TV (auto scan + TV control)
  - Scenes
