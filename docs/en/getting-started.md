# Getting started

Run OpenSignature locally with Docker (PostgreSQL + RabbitMQ), the ASP.NET Core API, the signing worker, and the React UI.

## Prerequisites

- .NET 10 SDK
- Node.js 20+
- Docker Desktop (or compatible engine)
- PowerShell 7+ recommended on Windows

## 1. Clone and environment

```bash
cp .env.example .env
docker compose up -d
docker compose ps
```

Defaults:

| Service | Endpoint |
| --- | --- |
| PostgreSQL | `localhost:5432` (`esign` / `esign` / `opensignature`) |
| RabbitMQ AMQP | `localhost:5672` |
| RabbitMQ UI | http://localhost:15672 |

If host port `5432` is busy, set `POSTGRES_PORT=5433` in `.env` and align `ConnectionStrings:PostgreSQL` in Api/Worker Development settings.

## 2. One-command development

```bash
pwsh ./scripts/Start-Development.ps1
```

| App | URL |
| --- | --- |
| Web | http://localhost:5173 |
| API | http://localhost:5270 |

Optional flags: `-SkipInfrastructure`, `-SkipCertificate`, `-ApiProfile https`.

## 3. Manual POC path

```bash
docker compose up -d
pwsh ./scripts/Generate-DevCertificate.ps1
# set Signing__Pfx__Password as required by your shell / appsettings

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

```bash
pip install -r requirements-docs.txt
mkdocs serve
```

Open http://127.0.0.1:8000 — use the language switcher for Türkçe.

## Next

- [Architecture](architecture.md)
- [Flows & diagrams](flows.md)
- [Operations](operations.md)
