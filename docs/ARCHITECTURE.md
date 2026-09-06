# OpenSignature Architecture

Engineering architecture for **OpenSignature**, the asynchronous digital-signature platform in this repository. Product and task sources of truth: `docs/PRODUCT-SPEC.en.md`, `docs/TASKS.md`. Code namespaces and project names use the `OpenSignature.*` prefix.

## 1. Overview / Goals

OpenSignature is a centralized enterprise signing platform built on .NET 10. Clients submit documents via REST; signing runs asynchronously on workers. Goals:

- Support PAdES, XAdES, CAdES, and phased ASiC (profiles B → T → LT → LTA).
- Keep the API non-blocking: accept, persist, enqueue; never sign synchronously in the API.
- Isolate cryptography behind replaceable signing providers (PFX for demo; PKCS#11 / smart card / HSM for production).
- Store document binaries in file/object storage; PostgreSQL holds metadata, job state, and audit only.
- Transport job references over RabbitMQ — never document binaries.
- Enforce tenant isolation, idempotency, and observable end-to-end flows.

Local dependencies: PostgreSQL and RabbitMQ via `docker-compose.yml`. Solution file: `OpenSignature.slnx`.

## 2. Component Diagram

```text
                    +-------------------+
                    |   OpenSignature.Web       |
                    |   (React / Vite)  |
                    +---------+---------+
                              |
                              v
                    +-------------------+
                    |   OpenSignature.Api       |
                    | ASP.NET Core      |
                    | Minimal API       |
                    +----+---------+----+
                         |         |
              OpenSignature.Application    |
                         |         |
              OpenSignature.Domain (pure)  |
                         |         |
              OpenSignature.Infrastructure |
                         |         |
              +----------+---------+----------+
              |          |                    |
         PostgreSQL   IFileStorage      Outbox publisher
         (metadata)   (input/signed)         |
                                             v
                                      +------------+
                                      |  RabbitMQ  |
                                      |  (refs only)|
                                      +------+-----+
                                             |
                                             v
                                      +------------+
                                      | OpenSignature.Worker|
                                      +------+-----+
                                             |
                              OpenSignature.Signing (+ Contracts)
                                             |
                    +------------------------+------------------------+
                    |            |           |                        |
                   PFX        PKCS#11    SmartCard                   HSM
                    |            |           |                        |
                    +------------+-----------+------------------------+
                                             |
                                       Signed file (storage)
```

| Project | Role |
|---------|------|
| `OpenSignature.Api` | HTTP boundary; validate, accept, return `202` |
| `OpenSignature.Application` | Use cases, orchestration, DTOs |
| `OpenSignature.Domain` | Entities, state machine, value objects — no infrastructure |
| `OpenSignature.Infrastructure` | EF Core, PostgreSQL, storage adapters, RabbitMQ, outbox |
| `OpenSignature.Signing.Contracts` | `ISigningProvider` and shared signing contracts |
| `OpenSignature.Signing` | Provider implementations and format engines |
| `OpenSignature.Validation` | Certificate + signature validation (Baseline B) and JSON reports |
| `OpenSignature.Worker` | Consumes jobs; signs; updates state |
| `OpenSignature.Web` | React admin/demo UI |

## 3. Async Signing Flow

```text
Client
  |  POST /api/v1/signatures (+ Idempotency-Key)
  v
Api
  |  validate → generate signatureId / jobId
  |  store input via IFileStorage (server-generated key)
  |  persist SignatureRequest + SigningJob + OutboxMessage (same DB transaction)
  |  return 202 Accepted
  v
Outbox publisher
  |  publish small message to RabbitMQ (no binary)
  v
Worker
  |  consume → idempotent lock → load input → resolve provider → sign
  |  write signed.bin → update PostgreSQL → ACK
  v
Client
  |  GET /api/v1/signatures/{id}  (status)
  |  GET /api/v1/signatures/{id}/content  (when Completed)
```

Incomplete outputs must never be exposed as completed. Storage or post-sign DB failures block or recover without falsely completing.

## 4. Layering & Dependency Rules

```text
Web ──► Api ──► Application ──► Domain
                      │
                      ▼
               Infrastructure ──► Domain, Signing.Contracts
                      │
Worker ──► Application / Infrastructure / Signing
Signing ──► Signing.Contracts
```

Rules:

- **Domain** depends on nothing outside itself.
- **Application** depends on Domain and abstractions; not on RabbitMQ or concrete storage SDKs.
- **Signing** must not depend on RabbitMQ; transport stays in Infrastructure / Worker.
- **Api** must not perform cryptographic signing.
- Prefer dependency inversion: `IFileStorage`, `ISigningProvider`, messaging ports.

## 5. Signing Provider Abstraction

```text
ISigningProvider
 ├── PfxSigningProvider          (dev/demo; PKCS#12)
 ├── Pkcs11SigningProvider
 ├── SmartCardSigningProvider
 └── HsmSigningProvider
```

Provider capabilities: list certificates, metadata, create digest, sign digest, health/availability, session cleanup.

**PFX (dev/demo):** `PfxSigningProvider` loads PKCS#12 from a configured path or in-memory bytes (`PfxSigningProviderOptions`). Password may come from options (development) or `ISigningSecretProvider` via `PasswordSecretName`, backed by `ISecretStore` (`ConfigurationSecretStore` / `EnvironmentSecretStore` / `RotatingSecretStore`). Certificates without a usable RSA/ECDSA private key, or outside their validity window, are listed with `CanSign=false`. Health reflects load success (`Healthy` / `Degraded` / `Unavailable`). Never commit PFX files or passwords.

**PKCS#11 (T100–T102):** Abstractions as `IPkcs11Library` / `IPkcs11Slot` / `IPkcs11Session` / `IPkcs11LibraryFactory` under `OpenSignature.Signing.Pkcs11`. Providers call `SignDigest` on-device; private keys are never exported. `MockPkcs11Library` holds an in-memory RSA/ECDSA key for CI (no real hardware). `SmartCardSigningProvider` uses short-lived sessions (open → login → use → close) via `AddSmartCardSigningProvider`. `HsmSigningProvider` uses a bounded `Pkcs11SessionPool` (`MaxConcurrentSessions`) via `AddHsmSigningProvider`; health reports module and pool availability. Token PIN is resolved only via `PinSecretName` + `ISigningSecretProvider` and must never be logged.

**Provider selection:** `ISigningProviderResolver` / `SigningProviderSelector` resolves a registered `ISigningProvider` by `SigningProviderType` and optional provider id from the request/configuration. Providers are registered in DI as `IEnumerable<ISigningProvider>` (e.g. `AddPfxSigningProvider`, `AddSmartCardSigningProvider`, `AddHsmSigningProvider`). Unsupported or ambiguous selections throw `UnsupportedSigningProviderException` (`SIGNING_PROVIDER_UNSUPPORTED`).

**Signature engine (T050–T054):** `AddSignatureEngine` registers PFX + CAdES/XAdES/PAdES Baseline B format signers + `SignatureOrchestrator` as `ISignatureCreationService`. Crypto primitives live under `OpenSignature.Signing.Crypto`. Format signers never export private keys; digests are signed via `ISigningProvider.SignDigestAsync`. Unsupported profiles/formats throw `SIGNATURE_PROFILE_UNSUPPORTED` / `SIGNATURE_FORMAT_UNSUPPORTED` (no silent downgrade). See `docs/SIGNATURE-PROFILES.md`.

**Validation (T090–T092):** Library-first (`OpenSignature.Validation`). `ICertificateValidator` runs PRODUCT-SPEC §20 pipeline (validity → chain/trust → key usage → EKU → revocation → policy) with machine-readable codes. `IRevocationChecker` supports Offline/Online/SoftFail (OCSP then CRL; live fetch not enabled in MVP — stub-friendly). `ISignatureValidator` verifies CAdES/XAdES/PAdES Baseline B crypto + certificate path. `IValidationReportBuilder` emits JSON-serializable reports. Register via `AddOpenSignatureValidation`. No `/api/v1/validations` endpoints in MVP (avoids Program.cs coupling). Not full ETSI EN 319 102-1 AdES conformance.

**Hardware rule:** for PKCS#11, smart card, and HSM providers, private keys never leave the device. Prefer:

```text
certificate → digest → device.Sign(digest)
```

Do not export private keys to the API, Worker process memory as extractable material, or PostgreSQL. PFX is acceptable only for development/demo.

## 6. Storage Keys Pattern

Do not use client filenames as paths. Server-generated keys:

```text
tenants/{tenantId}/signatures/{yyyy}/{MM}/{dd}/{signatureId}/input.bin
tenants/{tenantId}/signatures/{yyyy}/{MM}/{dd}/{signatureId}/signed.bin
```

`IFileStorage`: `SaveAsync`, `OpenReadAsync`, `ExistsAsync`, `DeleteAsync`, `GetMetadataAsync`.

MVP: local filesystem. Production adapters: S3-compatible, Azure Blob, S3. PostgreSQL does not store document binaries or private keys by default.

## 7. RabbitMQ Topology

```text
Exchange:     esign.signature (durable, direct)
Routing key:  signature.created
Queue:        esign.signature.worker (x-dead-letter-exchange → esign.signature.dlx)
DLX:          esign.signature.dlx (durable, fanout)
DLQ:          esign.signature.dlq
```

Publisher: `ISigningJobPublisher` / `RabbitMqSigningJobPublisher` publishes JSON metadata only (`SigningJobMessage`) to the exchange.

Messages carry job metadata and storage references only (e.g. `jobId`, `tenantId`, `signatureId`, `inputPath`, format/profile, `attempt`). **No document binaries in messages.**

Processing: deserialize → validate → acquire job lock → verify input → sign → store output → persist final state → ACK.

Retry / DLQ (worker):

- Classify failures as **transient** vs **permanent** (`SigningJobFailureClassifier`; processors may throw `TransientSigningJobException` / `PermanentSigningJobException`).
- Transient: bounded retries with exponential backoff (`SigningJobRetry` options; attempt tracked via `x-attempt` and `SigningJobMessage.Attempt`).
- After `MaxAttempts` or on permanent failure: explicit publish to `esign.signature.dlx` → `esign.signature.dlq`, then ACK the original delivery.
- Worker queue DLX args are a safety net for NACK-without-requeue fallbacks.

## 8. Outbox Pattern

Reliable publication without dual-write loss:

1. In one PostgreSQL transaction: persist signature metadata/job state **and** an `OutboxMessage` (`Id`, `Type`, `Payload`, `OccurredAt`, `PublishedAt`, `RetryCount`, `Error`).
2. A publisher drains unpublished outbox rows to RabbitMQ.
3. Mark published only after successful broker handoff.

Worker consumption must be **idempotent** so at-least-once delivery does not create duplicate successful signatures:

1. Skip when the signature request or job is already terminal (`Completed` / `Cancelled` / `Rejected`).
2. Atomically try-acquire a job lock (`ISigningJobLockService` / conditional PostgreSQL `UPDATE`): only one worker may transition `Pending`/`Failed` → `Locked`, or reclaim `Locked`/`Processing` when `LockedUntil` is missing or in the past.
3. If the lock is not acquired (duplicate delivery or another worker holds a non-expired lock), log and ACK without signing.
4. After crash/restart, expired `Locked`/`Processing` jobs may be reclaimed; active (non-expired) locks must not be stolen. Lock duration must exceed expected signing time.
5. Before writing signed output, re-check terminal state so a late worker never double-writes after another completion.

## 9. Multi-Tenancy Notes

- Domain entities and queue messages include `TenantId`.
- Storage keys are tenant-prefixed.
- Idempotency is scoped as `TenantId + IdempotencyKey`.
- Application port `ISignatureRequestIdempotencyStore` (EF implementation) looks up by tenant + key, creates when missing, and recovers from unique-constraint races so concurrent callers observe one `SignatureRequest` row.
- At most one `SigningJob` per signature request (`IX_SigningJobs_SignatureRequestId` unique); `GetOrCreateSigningJobAsync` uses the same conflict-recovery pattern.
- Auth roadmap: MVP API keys; production OAuth2/OIDC + JWT with tenant isolation and RBAC (Administrator, Signer, Operator, Auditor, Developer).
- Queries, authorization, and indexes must enforce tenant boundaries; never cross-tenant file or job access by ID alone.

## 10. Observability

OpenTelemetry for traces, metrics, and structured logs.

Every operation should carry:

```text
correlationId · traceId · signatureId · jobId
```

Key metrics: `signature_requests_total`, `signature_completed_total`, `signature_failed_total`, `signature_duration_seconds`, `signature_queue_wait_seconds`, `rabbitmq_messages_total`, `rabbitmq_retry_total`, `provider_sign_operations_total`, `provider_sign_failures_total`, `storage_operations_total`.

Never log private keys, passwords, PINs, secrets, or document contents. Prefer machine-readable error codes at the API; keep sensitive crypto/device detail in secure diagnostics only.

## 11. Current Repo Status

| Area | Status |
|------|--------|
| Phase 0 (T001–T003) | **Done** — solution bootstrap (`OpenSignature.slnx`), engineering standards, `docker-compose.yml` (Postgres + RabbitMQ) |
| Domain / persistence | **WIP** — domain model and later Phase 1 tasks |
| Signing / providers | **WIP** — contracts and engines not yet complete |
| API async pipeline, outbox, worker | Outbox writer/processor + RabbitMQ publisher/worker skeleton in place; full API pipeline still planned |
| Docs | This architecture doc (T150); API/security/ops docs follow their tasks |

This document describes the target architecture. Implementation progresses task-by-task in `docs/TASKS.md`; do not assume runtime behavior exists until the corresponding tasks are `DONE`.
