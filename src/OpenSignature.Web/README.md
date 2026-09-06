# OpenSignature Web

React administration/demo UI for **OpenSignature**.

## Stack

- React 19 + TypeScript
- Vite 8
- React Router
- TanStack Query

## Routes

| Path | Purpose |
|------|---------|
| `/dashboard` | Create signature + session-tracked status list |
| `/signatures` | Same create/list UX as dashboard |
| `/signatures/:id` | Status detail, cancel, download when completed |
| `/certificates` | Public certificate inventory |
| `/providers` | Providers + per-provider health |
| `/jobs`, `/audit`, `/settings` | Stubs / placeholders |

## Run locally

1. Start the API (`OpenSignature.Api`) — default HTTPS profile listens on **https://localhost:7010** (`Properties/launchSettings.json`).
2. From this folder:

```bash
npm install
npm run dev
```

Open http://localhost:5173

By default `.env.development` leaves `VITE_API_BASE_URL` empty so `/api` requests go through the Vite proxy to `https://localhost:7010` (avoids browser CORS and self-signed certificate issues).

### Environment

See `.env.example`:

| Variable | Meaning |
|----------|---------|
| `VITE_API_BASE_URL` | API origin (no trailing slash). Unset → `https://localhost:7010`. Empty → same-origin / proxy. |
| `VITE_TENANT_ID` | Optional `X-Tenant-Id` header |
| `VITE_API_KEY` | Optional `X-Api-Key` header |
| `VITE_PROXY_TARGET` | Vite proxy target (dev server only), default `https://localhost:7010` |

## Build

```bash
npm run build
```
