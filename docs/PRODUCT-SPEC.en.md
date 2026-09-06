# E-Signature Platform — Product & Engineering Specification (EN)

> **Status:** Primary engineering source  
> **Documentation:** English version  
> **Code:** English only  
> **Goal:** Build an independent .NET-based digital signature platform inspired by the integration model and functional scope of TÜBİTAK ESYA/MA3, without copying proprietary implementation or licensing material.

## 1. Product Definition

The product is a centralized enterprise digital-signature platform.

Core capabilities:

- PAdES, XAdES and CAdES signing.
- ASiC-S / ASiC-E architecture and phased support.
- PFX/PKCS#12 for development/demo.
- USB token, smart-card and HSM integration for production.
- PKCS#11 adapter architecture.
- Asynchronous producer/consumer signing.
- Durable input-file storage before queue publication.
- RabbitMQ job processing.
- Signed output stored separately from input.
- PostgreSQL metadata and audit trail.
- Idempotent signing requests.
- REST API.
- React administration/demo UI.
- Turkish and English documentation.
- English-only code.
- Unit, integration, contract, cryptographic interoperability and end-to-end tests.

## 2. Standards Baseline

Target standards:

- PAdES — ETSI EN 319 142-1
- XAdES — ETSI EN 319 132-1
- CAdES — ETSI EN 319 122-1
- ASiC — ETSI EN 319 162-1 / EN 319 162-2
- Signature validation — ETSI EN 319 102-1
- RFC 3161
- X.509 / PKI
- CMS
- XMLDSig
- PKCS#12
- PKCS#11
- OCSP / CRL
- SHA-256 and currently approved digest algorithms
- RSA and ECDSA where supported by provider/device.

Standards versions must be tracked in a compatibility matrix rather than scattered through code.

## 3. ESYA-Compatible Scope

The platform will provide modern equivalents of the integration concepts commonly required by ESYA users:

- certificate discovery
- certificate metadata
- certificate chain building
- trusted roots
- validity checking
- key usage and EKU
- OCSP
- CRL
- validation policies
- test certificates
- smart cards
- USB tokens
- PKCS#11
- HSMs
- PFX/PKCS#12
- PAdES
- XAdES
- CAdES
- ASiC
- timestamping
- signature validation
- auditability.

## 4. Signing Provider Architecture

```text
ISigningProvider
 ├── PfxSigningProvider
 ├── Pkcs11SigningProvider
 ├── SmartCardSigningProvider
 └── HsmSigningProvider
```

The private key must never be exposed to the API layer.

Provider contract:

- list certificates
- certificate metadata
- create digest
- sign digest
- provider health
- availability check
- session cleanup.

Hardware providers should perform a digest-to-device-sign flow whenever supported.

## 5. Asynchronous Architecture

```text
Client
  |
  | POST /api/v1/signatures
  v
Signing API
  |
  | validate
  | create ID
  | store input
  | persist metadata
  | publish message
  v
RabbitMQ
  |
  v
Signing Worker
  |
  | load input
  | resolve provider
  | sign
  | store output
  | update state
  v
Completed
  |
  v
Client
  |
  | GET status
  | GET signed content
  | GET verification report
```

RabbitMQ must not carry document binaries.

Example message:

```json
{
  "jobId": "0198...",
  "tenantId": "tenant-001",
  "signatureId": "0198...",
  "inputPath": "pending/2026/09/05/...",
  "requestedFormat": "PAdES",
  "requestedProfile": "B",
  "createdAt": "2026-09-05T17:00:00Z",
  "attempt": 1
}
```

## 6. Repository Structure

```text
/
├── src/
│   ├── OpenSignature.Api/
│   ├── OpenSignature.Application/
│   ├── OpenSignature.Domain/
│   ├── OpenSignature.Infrastructure/
│   ├── OpenSignature.Signing/
│   ├── OpenSignature.Signing.Contracts/
│   ├── OpenSignature.Worker/
│   └── OpenSignature.Web/
├── tests/
│   ├── OpenSignature.Domain.Tests/
│   ├── OpenSignature.Application.Tests/
│   ├── OpenSignature.Infrastructure.Tests/
│   ├── OpenSignature.Signing.Tests/
│   ├── OpenSignature.Api.IntegrationTests/
│   ├── OpenSignature.Worker.IntegrationTests/
│   └── OpenSignature.Interop.Tests/
├── docs/
├── deploy/
├── samples/
├── Directory.Build.props
├── Directory.Packages.props
├── docker-compose.yml
├── OpenSignature.slnx
└── README.md
```

## 7. Backend Stack

- .NET 10
- ASP.NET Core Minimal API
- PostgreSQL
- Entity Framework Core
- RabbitMQ
- Docker
- OpenTelemetry
- structured logging
- health checks
- ProblemDetails
- OpenAPI.

All code, logs, test names, identifiers and commit messages are English.

## 8. Domain Model

### SignatureRequest

```text
Id
TenantId
CorrelationId
Status
Format
Profile
InputFileId
OutputFileId
CertificateId
SigningProvider
CreatedAt
QueuedAt
StartedAt
CompletedAt
FailedAt
RetryCount
ErrorCode
ErrorMessage
CreatedBy
```

### StoredFile

```text
Id
StorageKey
OriginalFileName
ContentType
Size
Sha256
CreatedAt
DeletedAt
```

### Certificate

```text
Id
Thumbprint
Subject
Issuer
SerialNumber
NotBefore
NotAfter
ProviderType
ProviderReference
CreatedAt
```

### SigningJob

```text
Id
SignatureRequestId
Status
Attempt
LastError
LockedUntil
CreatedAt
StartedAt
CompletedAt
```

### AuditEvent

```text
Id
TenantId
EntityType
EntityId
EventType
Actor
Timestamp
CorrelationId
Metadata
```

## 9. State Machine

```text
Created
  -> Queued
  -> Processing
  -> Completed

Created -> Rejected
Queued -> RetryScheduled
Processing -> RetryScheduled
Processing -> Failed
```

Terminal states:

- Completed
- Failed
- Rejected
- Cancelled.

## 10. REST API

### Create signature

`POST /api/v1/signatures`

Use `multipart/form-data`.

Fields:

- file
- format
- profile
- signingProvider
- certificateSelector
- optional signature metadata.

Response:

```json
{
  "id": "0198...",
  "status": "Queued",
  "createdAt": "2026-09-05T17:00:00Z",
  "statusUrl": "/api/v1/signatures/0198..."
}
```

### Get status

`GET /api/v1/signatures/{id}`

### Download signed content

`GET /api/v1/signatures/{id}/content`

Only completed signatures may return output.

### Cancel

`POST /api/v1/signatures/{id}/cancel`

Cancellation is reliable only before the signing operation starts.

### Provider/certificate APIs

```text
GET /api/v1/certificates
GET /api/v1/certificates/{id}
GET /api/v1/providers
GET /api/v1/providers/{id}/health
```

### Verification

```text
GET  /api/v1/signatures/{id}/verification
POST /api/v1/verifications
```

`GET /api/v1/signatures/{id}/verification` verifies a completed platform signature and returns a detailed report (overall status, cryptographic check, certificate path, revocation, reason codes). Available only when status is `Completed`.

`POST /api/v1/verifications` accepts an uploaded signed document (`multipart/form-data`: `file`, `format`, optional `originalFile` for detached CAdES). The upload is verified in memory and is not stored.

Verification covers Baseline B CAdES, XAdES, and PAdES cryptographic checks plus certificate path validation. It is not a full ETSI EN 319 102-1 AdES conformance report.

## 11. Idempotency

Support:

`Idempotency-Key`

Unique key:

```text
TenantId + IdempotencyKey
```

A repeated request must not create another successful signing operation.

## 12. Storage

MVP:

- local filesystem adapter.

Production:

- S3-compatible storage
- Azure Blob adapter
- S3 adapter.

Interface:

```text
IFileStorage
 ├── SaveAsync
 ├── OpenReadAsync
 ├── ExistsAsync
 ├── DeleteAsync
 └── GetMetadataAsync
```

Generated storage keys:

```text
tenants/{tenantId}/signatures/{yyyy}/{MM}/{dd}/{signatureId}/input.bin
tenants/{tenantId}/signatures/{yyyy}/{MM}/{dd}/{signatureId}/signed.bin
```

## 13. RabbitMQ

Exchange:

```text
esign.signature
```

Routing key:

```text
signature.created
```

Queue:

```text
esign.signature.worker
```

Dead-letter queue:

```text
esign.signature.dlq
```

Processing:

1. Deserialize.
2. Validate.
3. Acquire job lock.
4. Verify input.
5. Sign.
6. Store output.
7. Persist final state.
8. ACK.

Transient failures use bounded retries and exponential backoff.

Permanent cryptographic errors must not be retried forever.

## 14. Transactional Messaging

Use the **Outbox Pattern**.

```text
OutboxMessage
Id
Type
Payload
OccurredAt
PublishedAt
RetryCount
Error
```

Worker processing must be idempotent.

## 15. PAdES

PAdES operates on PDF documents.

MVP:

- PAdES Baseline B
- PDF preservation
- incremental update
- signature dictionary
- ByteRange
- CMS signature container
- required signing certificate material
- optional visible appearance (implemented: PAdES stamp with note and/or JPEG/PNG).

Later:

- signature field reuse
- multiple signatures
- VRI dictionary.

Implemented (Phase 8): RFC 3161 signature timestamp (T), DSS with certificates/CRLs/OCSPs (LT), document timestamp `/SubFilter /ETSI.RFC3161` (LTA). Profiles never silently downgrade.

## 16. XAdES

MVP:

- XML input
- XMLDSig
- enveloped
- enveloping
- detached
- XAdES Baseline B.

Later:

- multiple signatures
- signature policy.

Implemented (Phase 8): T (`SignatureTimeStamp`), LT (`CertificateValues` / `RevocationValues`), LTA (`ArchiveTimeStamp`). Profiles never silently downgrade.

## 17. CAdES

MVP:

- CMS SignedData
- detached signatures
- encapsulated content
- CAdES Baseline B.

Later:

- signature policy.

Implemented (Phase 8): T (signature timestamp token), LT (certificate/revocation values), LTA (archive timestamp). Profiles never silently downgrade.

## 18. ASiC

ASiC-S:

- one associated signature/data package (ZIP, uncompressed `mimetype` first, detached CAdES in `META-INF/signature.p7s`).

ASiC-E:

- multiple data objects and signatures (`ASiCManifest.xml` plus CAdES over the manifest).

Use ZIP-based containers and validate package relationships. T/LT/LTA apply to the inner CAdES.

## 19. Timestamp Authority

Interface:

```text
ITimestampAuthority
{
    GetTimestampAsync(byte[] imprint)
}
```

Implement RFC 3161 with:

- TSA URL
- optional HTTP Basic Auth (`Timestamping:Username` plus `PasswordSecretName` / secret store; never commit passwords)
- nonce
- message imprint
- timestamp token
- certificate validation.

Worker default: no TSA until `Timestamping:Url` is configured. Tests may use an in-process RFC 3161 TSA whose private key is never exported. Anonymous TSAs omit the `Authorization` header.

A requested T/LT/LTA profile must never silently downgrade to B when timestamping fails.

## 20. Certificate Validation

Pipeline:

```text
Parse certificate
 -> validity
 -> chain building
 -> trust anchor
 -> key usage
 -> EKU
 -> revocation
 -> OCSP
 -> CRL fallback
 -> policy
 -> result
```

Return machine-readable reason codes.

## 21. Security

Rules:

- never commit PFX passwords
- never commit PFX files
- use secret management in production
- HSM private keys never leave HSM
- never log PINs
- never log document contents
- enforce upload limits
- do not trust client MIME types
- prevent path traversal
- generate storage keys server-side
- require TLS in production.

Authentication roadmap:

- MVP API keys
- production OAuth2/OIDC + JWT
- tenant isolation
- RBAC.

Roles:

- Administrator
- Signer
- Operator
- Auditor
- Developer.

## 22. Audit

Events:

```text
SignatureRequested
FileStored
JobQueued
JobStarted
CertificateResolved
SigningProviderSelected
SigningCompleted
OutputStored
SignatureDownloaded
SigningFailed
RetryScheduled
```

Audit records are append-only from the application perspective.

## 23. React Frontend

Stack:

- React
- TypeScript
- Vite
- TanStack Query
- React Router.

Pages:

```text
/dashboard
/signatures/:id
/certificates
/providers
/jobs
/audit
/settings
```

## 24. Observability

Use OpenTelemetry for traces, metrics and logs.

Metrics:

```text
signature_requests_total
signature_completed_total
signature_failed_total
signature_duration_seconds
signature_queue_wait_seconds
rabbitmq_messages_total
rabbitmq_retry_total
provider_sign_operations_total
provider_sign_failures_total
storage_operations_total
```

Every request/job should have correlation ID, trace ID, job ID and signature ID.

## 25. Testing

Unit tests:

- state machine
- validators
- hashing
- idempotency
- storage keys
- provider selection.

Integration tests:

- PostgreSQL
- RabbitMQ
- storage
- API
- worker.

Use Testcontainers where practical.

Cryptographic interoperability must use an independent validator/library.

## 26. Performance

The API returns after durable persistence and queue publication, not after signing completion.

Worker concurrency and device concurrency are configurable.

Benchmark:

- 1 MB
- 10 MB
- 100 MB
- 1,000 concurrent requests
- 1,000 queued jobs
- one HSM slot
- multiple workers.

## 27. Reliability

Requirements:

- accepted requests must not be lost
- idempotency prevents duplicate successful operations
- retryable errors are retried
- permanent failures reach DLQ
- incomplete outputs are never exposed as completed
- storage failures block completion
- post-signing database failures are recoverable.

## 28. Task-Driven Development

`docs/TASKS.md` is the implementation source of truth.

Statuses:

```text
TODO
IN_PROGRESS
BLOCKED
DONE
```

A task can be marked `DONE` only after:

1. implementation
2. tests
3. passing tests
4. documentation update
5. no unresolved blocker
6. meaningful commit.

## 29. Commit Policy

Use Conventional Commits:

```text
feat(api): add asynchronous signature request endpoint
feat(signing): add PAdES baseline B signer
feat(queue): add RabbitMQ signing worker
feat(storage): add local file storage adapter
test(signing): add PAdES interoperability tests
fix(worker): prevent duplicate job processing
docs(api): document signature endpoints
refactor(signing): extract signing provider abstraction
```

Avoid meaningless commits such as `update`, `changes`, `work` or `final`.

## 30. Definition of Done

Release candidate requirements:

- API documented
- worker documented
- queue topology documented
- storage documented
- PAdES/XAdES/CAdES tested
- PFX provider tested
- provider abstraction tested
- PostgreSQL migrations tested
- RabbitMQ retry/DLQ tested
- idempotency tested
- security tests passed
- audit events verified
- React UI functional
- Docker Compose reproducible
- CI green
- TR/EN documentation synchronized.

## 31. Explicit Non-Goals

The software does not automatically claim:

- qualified electronic signature status
- trust service provider status
- legal certification
- HSM certification
- automatic compliance with Turkish law
- production readiness without security review.

PFX is a development/demo mechanism. Qualified signing requires the appropriate qualified certificate and qualified signature creation environment/device.

## 32. First Milestone

Milestone 1:

```text
.NET 10 solution
PostgreSQL
RabbitMQ
Minimal API
Worker
Local file storage
PFX provider
PAdES-B
XAdES-B
CAdES-B
React UI
Docker Compose
Integration tests
CI
```

Advanced profiles, HSM/PKCS#11 and long-term validation follow after Milestone 1 is stable.
