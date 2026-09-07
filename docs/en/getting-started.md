# Getting started

Run OpenSignature locally with Docker Compose (full stack) or with Docker infrastructure plus host-run Api / Worker / Web.

## Prerequisites

- Docker Desktop (or compatible engine)
- For host development: .NET 10 SDK, Node.js 20+, PowerShell 7+ on Windows

## 1. Full stack with Docker Compose

```bash
cp .env.example .env
docker compose up -d --build
docker compose ps
```

| Service | Endpoint |
| --- | --- |
| Web | http://localhost:5173 |
| API | http://localhost:5270 |
| API health | http://localhost:5270/health |
| PostgreSQL | `localhost:5432` (`esign` / `esign` / `opensignature`) |
| RabbitMQ AMQP | `localhost:5672` |
| RabbitMQ UI | http://localhost:15672 |

`pfx-init` creates a disposable development PFX in a named volume. Set `PFX_PASSWORD` in `.env` (default `opensignature-dev`). Optional TSA: `TIMESTAMPING_URL` / `TIMESTAMPING_USERNAME` / `TIMESTAMPING_PASSWORD`.

Stop and remove containers (volumes kept):

```bash
docker compose down
```

USB / smart-card signing is not available inside containers — use the host development path below for PKCS#11.

## 2. One-command host development

Infrastructure only, then Api + Worker + Vite on the host:

```bash
docker compose up -d postgres rabbitmq
pwsh ./scripts/Start-Development.ps1
```

| App | URL |
| --- | --- |
| Web | http://localhost:5173 |
| API | http://localhost:5270 |

Optional flags: `-SkipInfrastructure`, `-SkipCertificate`, `-ApiProfile https`.

If host port `5432` is busy, set `POSTGRES_PORT=5433` in `.env` and align `ConnectionStrings:PostgreSQL` in Api/Worker Development settings.

## 3. Manual POC path

```bash
docker compose up -d postgres rabbitmq
pwsh ./scripts/Generate-DevCertificate.ps1

dotnet build OpenSignature.slnx
dotnet run --project src/OpenSignature.Api --launch-profile http
dotnet run --project src/OpenSignature.Worker
```

Create a Baseline-B CAdES signature:

```bash
curl -s -X POST "http://localhost:5270/api/v1/signatures" \
  -H "X-Tenant-Id: tenant-demo" \
  -H "Idempotency-Key: demo-1" \
  -F "file=@samples/poc.txt;type=text/plain" \
  -F "format=CAdES" \
  -F "profile=B" \
  -F "signingProvider=Pfx"
```

Poll and download:

```bash
curl -s "http://localhost:5270/api/v1/signatures/{id}" -H "X-Tenant-Id: tenant-demo"
curl -s -o signed.bin "http://localhost:5270/api/v1/signatures/{id}/content" -H "X-Tenant-Id: tenant-demo"
```

## 4. Tests

```bash
dotnet test OpenSignature.slnx
```

## 5. Documentation site

Published: [https://serhatboyraz.github.io/opensignature/](https://serhatboyraz.github.io/opensignature/) (Türkçe: [/tr/](https://serhatboyraz.github.io/opensignature/tr/)).

Preview locally:

```bash
pip install -r requirements-docs.txt
mkdocs serve
```

Open http://127.0.0.1:8000 — use the language switcher for Türkçe.

## Next

- [Architecture](architecture.md)
- [Flows & diagrams](flows.md)
- [Operations](operations.md)
