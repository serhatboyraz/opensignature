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
| `/jobs`, `/audit`, `/settings` | Stubs (not shown in the header) |

## Run locally

From the repository root, start the API, worker, and this UI together:

```bash
pwsh ./scripts/Start-Development.ps1
```

Or start only the UI after the API is already running (`http://localhost:5270` by default):

```bash
npm install
npm run dev
```

Open http://localhost:5173

By default `.env.development` leaves `VITE_API_BASE_URL` empty so `/api` requests go through the Vite proxy to `http://localhost:5270` (avoids browser CORS).

### Environment

See `.env.example`:

| Variable | Meaning |
|----------|---------|
| `VITE_API_BASE_URL` | API origin (no trailing slash). Unset/empty → same-origin / Vite proxy. |
| `VITE_TENANT_ID` | Optional `X-Tenant-Id` header |
| `VITE_API_KEY` | Optional `X-Api-Key` header |
| `VITE_PROXY_TARGET` | Vite proxy target (dev server only), default `http://localhost:5270` |

## Build

```bash
npm run build
```
