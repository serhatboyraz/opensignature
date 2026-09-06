# OpenSignature

Independent .NET digital-signature platform with asynchronous signing, PostgreSQL metadata, RabbitMQ workers, and a React administration UI.

## Documentation

- [Product specification (EN)](docs/PRODUCT-SPEC.en.md)
- [Product specification (TR)](docs/PRODUCT-SPEC.tr.md)
- [Task checklist](docs/TASKS.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Signature profiles](docs/SIGNATURE-PROFILES.md)

All source code, identifiers, logs, tests and commit messages are English-only.

## Prerequisites

- .NET 10 SDK
- Node.js 20+
- Docker (PostgreSQL + RabbitMQ)

## Local infrastructure

```bash
cp .env.example .env
docker compose up -d
docker compose ps
```

Defaults: Postgres `localhost:5432` (`esign`/`esign`/`opensignature`), RabbitMQ `5672` + management UI `15672`. If host port `5432` is busy, set `POSTGRES_PORT=5433` in `.env` and match `ConnectionStrings:PostgreSQL` in the Api/Worker Development settings.

## Start development apps

One command starts Docker dependencies, the API, the signing worker, and the React UI:

```bash
pwsh ./scripts/Start-Development.ps1
```

| App | URL |
| --- | --- |
| Web | http://localhost:5173 |
| API | http://localhost:5270 |

Press Ctrl+C in that terminal to stop the API, worker, and web processes. Containers stay up until `docker compose down`.

Optional flags: `-SkipInfrastructure`, `-SkipCertificate`, `-ApiProfile https`.

## Working POC (async signing)

```bash
# 1) Dependencies
docker compose up -d

# 2) Dev signing certificate (gitignored under data/certs/)
pwsh ./scripts/Generate-DevCertificate.ps1
$env:Signing__Pfx__Password = "opensignature-dev"

# 3) Build and run (two terminals)
dotnet build OpenSignature.slnx
dotnet run --project src/OpenSignature.Api --launch-profile http
dotnet run --project src/OpenSignature.Worker
```

API: http://localhost:5270

Create a Baseline-B signature (CAdES example):

```bash
curl -s -X POST "http://localhost:5270/api/v1/signatures" \
  -H "X-Tenant-Id: tenant-demo" \
  -H "Idempotency-Key: demo-1" \
  -F "file=@samples/poc.txt;type=text/plain" \
  -F "format=CAdES" \
  -F "profile=B" \
  -F "signingProvider=Pfx"
```

Poll status, then download when `Completed`:

```bash
curl -s "http://localhost:5270/api/v1/signatures/{id}" -H "X-Tenant-Id: tenant-demo"
curl -s -o signed.bin "http://localhost:5270/api/v1/signatures/{id}/content" -H "X-Tenant-Id: tenant-demo"
curl -s "http://localhost:5270/api/v1/signatures/{id}/verification" -H "X-Tenant-Id: tenant-demo"
```

Ad-hoc verification of an already-signed file:

```bash
curl -s -X POST "http://localhost:5270/api/v1/verifications" \
  -H "X-Tenant-Id: tenant-demo" \
  -F "file=@signed.bin;type=application/pkcs7-mime" \
  -F "format=CAdES"
```

Supported MVP formats: **CAdES-B**, **XAdES-B**, **PAdES-B** via the development PFX provider. Sample inputs live under `samples/`.

USB tokens appear on the Providers page (`/providers`) when vendor PKCS#11 middleware is installed. Development auto-detects well-known libraries (`Signing:SmartCard:AutoDetect`). Expired token certificates can sign in Development via `Signing:AllowExpiredCertificates` (keep this `false` in production). See [Operations](docs/OPERATIONS.md).

## Tests

```bash
dotnet test OpenSignature.slnx
```

## Repository layout

```text
src/OpenSignature.Api|Application|Domain|Infrastructure|Signing|Signing.Contracts|Worker|Web
tests/
docs/
scripts/Start-Development.ps1
scripts/Generate-DevCertificate.ps1
samples/
```

## Status

POC path is operational: API → storage → PostgreSQL → outbox → RabbitMQ → worker → signing engine → download. USB-token PKCS#11 providers are registered from configuration (auto-detect in Development). Further work continues via `docs/TASKS.md`.
