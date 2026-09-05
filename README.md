# OpenSignature

Independent .NET digital-signature platform with asynchronous signing, PostgreSQL metadata, RabbitMQ workers, and a React administration UI.

## Documentation

- [Product specification (EN)](docs/PRODUCT-SPEC.en.md)
- [Product specification (TR)](docs/PRODUCT-SPEC.tr.md)
- [Task checklist](docs/TASKS.md)
- [Architecture](docs/ARCHITECTURE.md)

All source code, identifiers, logs, tests and commit messages are English-only.

## Repository layout

```text
src/
  OpenSignature.Api/                 ASP.NET Core Minimal API
  OpenSignature.Application/         Application use cases and ports
  OpenSignature.Domain/              Domain model
  OpenSignature.Infrastructure/      Persistence, messaging, storage
  OpenSignature.Signing/             Signature format implementations
  OpenSignature.Signing.Contracts/   Provider contracts
  OpenSignature.Worker/              RabbitMQ signing worker
  OpenSignature.Web/                 React (Vite + TypeScript) UI
tests/                       Unit, integration and interop tests
docs/                        Product and engineering docs
deploy/                      Deployment assets
samples/                     Sample inputs and certificates (no private keys)
```

## Prerequisites

- .NET 10 SDK
- Node.js 20+
- Docker (for local PostgreSQL / RabbitMQ)

## Local infrastructure

Start PostgreSQL and RabbitMQ with one command from the repository root:

```bash
cp .env.example .env   # once; adjust credentials if needed
docker compose up -d
```

Check service health:

```bash
docker compose ps
```

| Service   | Host      | Port(s)        | Default credentials                          |
|-----------|-----------|----------------|----------------------------------------------|
| PostgreSQL | `localhost` | `5432`       | user/password/db: `esign` / `esign` / `opensignature` |
| RabbitMQ  | `localhost` | `5672` (AMQP), `15672` (management UI) | user/password: `esign` / `esign` |

Connection hints (defaults from `.env.example`):

- PostgreSQL: `Host=localhost;Port=5432;Database=opensignature;Username=esign;Password=esign`
- RabbitMQ AMQP: `amqp://esign:esign@localhost:5672/`
- RabbitMQ management UI: http://localhost:15672

Stop infrastructure:

```bash
docker compose down
```

## Quick start

### Backend

```bash
dotnet restore OpenSignature.slnx
dotnet build OpenSignature.slnx
dotnet test OpenSignature.slnx
dotnet run --project src/OpenSignature.Api
```

### Frontend

```bash
cd src/OpenSignature.Web
npm install
npm run dev
```

## Status

Implementation follows `docs/TASKS.md` in dependency order. Local infrastructure (T003) is available via `docker compose up -d`.
