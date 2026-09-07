# Development Tasks

> This file is the authoritative implementation checklist.  
> Update task status only after implementation, tests and required documentation are complete.  
> Code and commit messages must be English.

## Status Legend

- `TODO`
- `IN_PROGRESS`
- `BLOCKED`
- `DONE`

## Rules

1. Work in dependency order unless a dependency is explicitly mocked.
2. Before starting a task, change it to `IN_PROGRESS`.
3. After implementation, run the required tests.
4. Update this file immediately when the task is genuinely complete.
5. Mark `DONE` only after tests pass.
6. Create a meaningful Conventional Commit for every meaningful completed task or coherent task group.
7. Never commit secrets, PFX passwords, private keys or production certificates.
8. Keep documentation under `docs/`.
9. Keep code and identifiers fully English.
10. Do not silently reduce a requested signature profile when a dependency fails.

---

## Phase 0 — Product Foundation

### T001 — Repository Bootstrap
- Status: `DONE`
- Priority: P0
- Depends on: none
- Scope:
  - Create repository structure.
  - Create .NET 10 solution.
  - Add source and test projects.
  - Add React application.
  - Add docs structure.
- Acceptance:
  - Solution builds.
  - React app starts.
  - Test projects run.
- Tests:
  - `dotnet build`
  - `dotnet test`
- Commit:
  - `feat(repo): bootstrap e-signature platform`

### T002 — Engineering Standards
- Status: `DONE`
- Priority: P0
- Depends on: T001
- Scope:
  - Directory.Build.props
  - package management
  - analyzers
  - nullable reference types
  - warnings policy
  - formatting
  - EditorConfig
- Acceptance:
  - Clean build with configured warnings policy.
- Commit:
  - `chore(build): configure engineering standards`

### T003 — Docker Compose Development Environment
- Status: `DONE`
- Priority: P0
- Depends on: T001
- Scope:
  - PostgreSQL
  - RabbitMQ
  - application dependencies
- Acceptance:
  - One command starts dependencies.
  - Health checks are available.
- Commit:
  - `chore(dev): add local infrastructure compose`

---

## Phase 1 — Domain and Persistence

### T010 — Domain Model
- Status: `DONE`
- Priority: P0
- Depends on: T001
- Scope:
  - SignatureRequest
  - SigningJob
  - StoredFile
  - Certificate
  - AuditEvent
  - OutboxMessage
  - enums and value objects
- Acceptance:
  - Domain has no infrastructure dependencies.
- Tests:
  - state transition tests
- Commit:
  - `feat(domain): add signature domain model`

### T011 — Signature State Machine
- Status: `DONE`
- Priority: P0
- Depends on: T010
- Acceptance:
  - valid transitions enforced
  - invalid transitions rejected
- Tests:
  - complete transition matrix
- Commit:
  - `feat(domain): enforce signature state transitions`

### T012 — PostgreSQL Persistence
- Status: `DONE`
- Priority: P0
- Depends on: T010
- Scope:
  - EF Core
  - migrations
  - indexes
  - constraints
- Acceptance:
  - database initializes from migrations.
- Tests:
  - integration persistence tests
- Commit:
  - `feat(persistence): add PostgreSQL storage`

### T013 — Idempotency
- Status: `DONE`
- Priority: P0
- Depends on: T012
- Acceptance:
  - same tenant + idempotency key returns same operation.
  - duplicate signing job cannot be created.
- Tests:
  - concurrency test
- Commit:
  - `feat(api): add signing request idempotency`

---

## Phase 2 — File Storage

### T020 — Storage Abstraction
- Status: `DONE`
- Priority: P0
- Depends on: T010
- Scope:
  - IFileStorage
  - metadata
  - streaming
- Commit:
  - `feat(storage): add file storage abstraction`

### T021 — Local File Storage
- Status: `DONE`
- Priority: P0
- Depends on: T020
- Acceptance:
  - safe generated paths
  - streaming read/write
  - SHA-256 calculation
  - atomic output handling
- Tests:
  - path traversal
  - concurrent writes
  - hash correctness
- Commit:
  - `feat(storage): add local file storage adapter`

---

## Phase 3 — Messaging

### T030 — RabbitMQ Contracts
- Status: `DONE`
- Priority: P0
- Depends on: T010
- Scope:
  - signing job message
  - routing keys
  - queue names
- Commit:
  - `feat(queue): define signing job contracts`

### T031 — RabbitMQ Publisher
- Status: `DONE`
- Priority: P0
- Depends on: T030
- Commit:
  - `feat(queue): add RabbitMQ publisher`

### T032 — Outbox
- Status: `DONE`
- Priority: P0
- Depends on: T012, T031
- Acceptance:
  - DB state and outbound event are durable.
  - failed publishing is retried.
- Tests:
  - publish retry
  - restart recovery
- Commit:
  - `feat(queue): implement transactional outbox`

### T033 — Worker Skeleton
- Status: `DONE`
- Priority: P0
- Depends on: T030
- Scope:
  - consumer
  - cancellation
  - graceful shutdown
  - structured logs
- Commit:
  - `feat(worker): add RabbitMQ signing worker`

### T034 — Retry and Dead Letter
- Status: `DONE`
- Priority: P0
- Depends on: T033
- Acceptance:
  - bounded retry
  - backoff
  - DLQ
  - permanent vs transient errors
- Tests:
  - retry matrix
- Commit:
  - `feat(worker): add retry and dead-letter handling`

---

## Phase 4 — Signing Provider Abstraction

### T040 — Signing Provider Contract
- Status: `DONE`
- Priority: P0
- Depends on: T010
- Scope:
  - certificate discovery
  - certificate metadata
  - digest signing
  - health
- Commit:
  - `feat(signing): add signing provider abstraction`

### T041 — PFX Provider
- Status: `DONE`
- Priority: P0
- Depends on: T040
- Scope:
  - PKCS#12 loading
  - password secret handling
  - certificate selection
  - RSA/ECDSA where supported
- Tests:
  - valid certificate
  - invalid password
  - expired certificate
  - missing private key
- Commit:
  - `feat(signing): add PFX signing provider`

### T042 — Provider Selection
- Status: `DONE`
- Priority: P0
- Depends on: T041
- Acceptance:
  - provider selected from request/configuration.
  - unsupported provider rejected.
- Commit:
  - `feat(signing): add provider selection`

---

## Phase 5 — Signature Formats

### T050 — Cryptographic Primitives
- Status: `DONE`
- Priority: P0
- Depends on: T040
- Scope:
  - digest
  - certificate
  - CMS/XML/PDF signing primitives
  - canonicalization helpers
- Tests:
  - known vectors
- Commit:
  - `feat(crypto): add signing primitives`

### T051 — CAdES-B
- Status: `DONE`
- Priority: P0
- Depends on: T050
- Acceptance:
  - detached CAdES-B
  - encapsulated content
  - certificate inclusion rules
- Tests:
  - independent validation
- Commit:
  - `feat(signing): add CAdES baseline B`

### T052 — XAdES-B
- Status: `DONE`
- Priority: P0
- Depends on: T050
- Acceptance:
  - enveloped
  - enveloping
  - detached
- Tests:
  - XMLDSig validation
  - independent XAdES validation
- Commit:
  - `feat(signing): add XAdES baseline B`

### T053 — PAdES-B
- Status: `DONE`
- Priority: P0
- Depends on: T050
- Acceptance:
  - PDF incremental update
  - ByteRange
  - CMS container
  - certificate material
- Tests:
  - independent PDF signature validation
  - corrupted PDF
  - corrupted signature
- Commit:
  - `feat(signing): add PAdES baseline B`

### T054 — Signature Service Orchestration
- Status: `DONE`
- Priority: P0
- Depends on: T051, T052, T053
- Scope:
  - resolve format
  - resolve profile
  - resolve provider
  - sign
  - output storage
  - status transition
- Commit:
  - `feat(signing): orchestrate signature creation`

### T055 — PAdES Visible Appearance
- Status: `DONE`
- Priority: P1
- Depends on: T053, T054, T060, T131
- Scope:
  - Optional visible PAdES signature widget (non-zero Rect + appearance stream).
  - Stamp text: digitally signed by certificate CN, signing time, optional note.
  - Optional JPEG/PNG appearance image stored as a separate file (not in RabbitMQ).
  - API/UI opt-in for PAdES only; default remains invisible.
- Acceptance:
  - Invisible signing still uses `/Rect [0 0 0 0]` and validates.
  - Visible signing draws on the selected page and remains cryptographically valid.
  - Note and image appear in the PDF appearance; image binaries are not placed on the queue.
- Tests:
  - visible text appearance
  - visible JPEG/PNG appearance
  - invisible default unchanged
  - PAdES-only validation for appearance fields
- Commit:
  - `feat(signing): add PAdES visible signature appearance`

### T055a — PAdES Additional Signatures
- Status: `DONE`
- Priority: P1
- Depends on: T053, T055
- Scope:
  - Append a new PAdES signature when the PDF already contains one (incremental update).
  - Do not patch or replace a previous signature dictionary (`/ByteRange`, `/Contents`, field).
  - Unique AcroForm field names (`OpenSignatureN`).
  - Visible widgets must not overlap existing page annotations.
- Acceptance:
  - Signing an already-signed PDF adds a new signature; previous CMS still validates.
  - Field names are unique.
  - Visible stamps stack instead of covering the previous widget.
- Tests:
  - two sequential invisible signatures
  - two sequential visible signatures (non-overlapping rects)
  - previous ByteRange preserved
- Commit:
  - `feat(signing): append additional PAdES signatures`

---

## Phase 6 — API

### T060 — Signature API
- Status: `DONE`
- Priority: P0
- Depends on: T013, T021, T032, T054
- Scope:
  - POST signature
  - GET status
  - GET content
  - cancel
- Acceptance:
  - API never blocks waiting for signing.
- Commit:
  - `feat(api): add asynchronous signature endpoints`

### T061 — Certificate API
- Status: `DONE`
- Priority: P1
- Depends on: T040
- Commit:
  - `feat(api): add certificate endpoints`

### T062 — Provider API
- Status: `DONE`
- Priority: P1
- Depends on: T040
- Commit:
  - `feat(api): add signing provider endpoints`

### T063 — OpenAPI
- Status: `DONE`
- Priority: P0
- Depends on: T060
- Commit:
  - `docs(api): document signing API`

---

## Phase 7 — Worker End-to-End

### T070 — End-to-End Signing Pipeline
- Status: `DONE`
- Priority: P0
- Depends on: T034, T054, T060
- Flow:
  - API upload
  - persistent file
  - DB record
  - outbox
  - RabbitMQ
  - worker
  - signing
  - signed file
  - DB completion
  - download
- Tests:
  - complete E2E test for each MVP format
- Commit:
  - `feat(worker): complete asynchronous signing pipeline`

### T071 — Duplicate Processing Protection
- Status: `DONE`
- Priority: P0
- Depends on: T070
- Tests:
  - duplicate message
  - worker restart
  - concurrent workers
- Commit:
  - `fix(worker): prevent duplicate signature processing`

### T072 — Release Job Lock On Transient Failure
- Status: `DONE`
- Priority: P0
- Depends on: T071
- Scope:
  - Transient signing failures must release the job lock (`LockedUntil`) and move the request to `RetryScheduled` so the next delivery can acquire the lock.
  - Permanent signing failures (including unreadable PDF structure) must mark the job/request failed instead of leaving `Processing`.
  - Duplicate deliveries that lose the lock race still ACK without signing.
- Acceptance:
  - A worker that fails after acquiring the lock does not leave the job stuck in `Processing`.
  - A subsequent retry can acquire the lock and continue.
  - Unreadable PDF input fails permanently with a machine-readable error rather than retry-ACK.
- Tests:
  - domain lock release
  - processor transient failure then successful retry
  - processor permanent PDF parse failure marks failed
- Commit:
  - `fix(worker): release signing job lock on retryable failure`

---

## Phase 8 — ASiC and Advanced Profiles

### T080 — ASiC-S
- Status: `DONE`
- Priority: P1
- Depends on: T051, T052
- Scope:
  - ZIP ASiC-S with uncompressed `mimetype` first and detached CAdES in `META-INF/signature.p7s`.
  - Orchestrator + validator unpack/verify inner CAdES.
- Tests:
  - `AsicSSignerTests`, orchestrator ASiC-S, validation tamper, interop.
- Commit:
  - `feat(signing): add ASiC-S container support`

### T081 — ASiC-E
- Status: `DONE`
- Priority: P1
- Depends on: T080
- Scope:
  - ZIP ASiC-E with `ASiCManifest.xml`; CAdES over the manifest.
- Tests:
  - `AsicESignerTests`, orchestrator ASiC-E, interop.
- Commit:
  - `feat(signing): add ASiC-E container support`

### T082 — RFC 3161 Timestamping
- Status: `DONE`
- Priority: P1
- Depends on: T051, T052, T053
- Scope:
  - HTTP RFC 3161 client and in-process TSA for tests.
  - Default unavailable TSA; worker enables HTTP TSA when `Timestamping:Url` is set.
- Tests:
  - `Rfc3161TimestampAuthorityTests`, orchestrator T without TSA.
- Commit:
  - `feat(timestamp): add RFC 3161 timestamp provider`

### T082a — TSA HTTP Basic Auth
- Status: `DONE`
- Priority: P1
- Depends on: T082, T113
- Scope:
  - Optional RFC 3161 HTTP Basic Auth (`Timestamping:Username`).
  - Password via `PasswordSecretName` / `ISigningSecretProvider` (no committed secrets).
  - Omit `Authorization` when credentials are not configured.
- Tests:
  - `Rfc3161TimestampAuthorityTests` Basic Auth header and secret resolution.
- Commit:
  - `feat(timestamp): add RFC 3161 TSA Basic Auth`

### T083 — PAdES-T/LT/LTA
- Status: `DONE`
- Priority: P1
- Depends on: T053, T082
- Scope:
  - T: CMS signature timestamp; LT: DSS; LTA: document timestamp `/ETSI.RFC3161`.
- Tests:
  - `XadesAndPadesAdvancedProfileTests`
- Commit:
  - `feat(signing): add advanced PAdES profiles`

### T084 — XAdES-T/LT/LTA
- Status: `DONE`
- Priority: P1
- Depends on: T052, T082
- Scope:
  - Unsigned `SignatureTimeStamp`, `CertificateValues`, `RevocationValues`, `ArchiveTimeStamp`.
- Tests:
  - `XadesAndPadesAdvancedProfileTests`
- Commit:
  - `feat(signing): add advanced XAdES profiles`

### T085 — CAdES-T/LT/LTA
- Status: `DONE`
- Priority: P1
- Depends on: T051, T082
- Scope:
  - Signature timestamp token, cert/revocation values, archive-time-stamp-v3.
- Tests:
  - `CadesAdvancedProfileTests`
- Commit:
  - `feat(signing): add advanced CAdES profiles`

---

## Phase 9 — Validation

### T090 — Certificate Validation Engine
- Status: `DONE`
- Priority: P1
- Depends on: T040
- Scope:
  - chain
  - trust
  - validity
  - key usage
  - EKU
  - OCSP
  - CRL
  - policies
- Commit:
  - `feat(validation): add certificate validation engine`

### T091 — Signature Validation
- Status: `DONE`
- Priority: P1
- Depends on: T090, T051, T052, T053
- Commit:
  - `feat(validation): add signature validation service`

### T092 — Validation Reports
- Status: `DONE`
- Priority: P1
- Depends on: T091
- Commit:
  - `feat(validation): add validation reports`

---

## Phase 10 — Hardware

### T100 — PKCS#11 Abstraction
- Status: `DONE`
- Priority: P1
- Depends on: T040
- Commit:
  - `feat(signing): add PKCS11 provider abstraction`

### T101 — USB Token / Smart Card Provider
- Status: `DONE`
- Priority: P1
- Depends on: T100
- Scope:
  - certificate enumeration
  - token selection
  - PIN handling
  - sign digest
  - session lifecycle
- Tests:
  - mock provider
  - real-device manual interoperability test
- Commit:
  - `feat(signing): add smart card signing provider`

### T102 — HSM Provider
- Status: `DONE`
- Priority: P1
- Depends on: T100
- Scope:
  - PKCS#11
  - session pool
  - health
  - concurrency limits
- Commit:
  - `feat(signing): add HSM signing provider`

### T103 — Native PKCS#11 Host Wiring
- Status: `DONE`
- Priority: P1
- Depends on: T101, T102
- Scope:
  - Load vendor PKCS#11 modules via Pkcs11Interop (`IPkcs11LibraryFactory` production backend).
  - Register SmartCard / HSM providers in API and Worker from configuration.
  - Auto-detect well-known USB-token PKCS#11 libraries in Development.
  - Health checks must not log in (avoid PIN lockout).
- Tests:
  - missing module unavailable
  - auto-detect probe
  - SmartCard registered from options
- Commit:
  - `feat(signing): load native PKCS11 USB token providers`

---

## Phase 11 — Security

### T110 — Authentication
- Status: `DONE`
- Priority: P1
- Depends on: T060
- Commit:
  - `feat(security): add API authentication`

### T111 — Authorization / RBAC
- Status: `DONE`
- Priority: P1
- Depends on: T110
- Commit:
  - `feat(security): add role based authorization`

### T112 — Tenant Isolation
- Status: `DONE`
- Priority: P1
- Depends on: T111
- Tests:
  - cross-tenant access denial
- Commit:
  - `feat(security): enforce tenant isolation`

### T113 — Secret Management
- Status: `DONE`
- Priority: P0
- Depends on: T041
- Scope:
  - development secrets
  - production secret abstraction
  - secret rotation
- Commit:
  - `feat(security): add secret management abstraction`

---

## Phase 12 — Audit and Observability

### T120 — Audit Trail
- Status: `TODO`
- Priority: P1
- Depends on: T012, T060
- Commit:
  - `feat(audit): add immutable signing audit trail`

### T121 — OpenTelemetry
- Status: `TODO`
- Priority: P1
- Depends on: T060, T070
- Commit:
  - `feat(observability): add OpenTelemetry instrumentation`

### T122 — Metrics
- Status: `TODO`
- Priority: P1
- Depends on: T121
- Commit:
  - `feat(observability): add signing metrics`

---

## Phase 13 — React

### T130 — React Application Shell
- Status: `DONE`
- Priority: P1
- Depends on: T001
- Commit:
  - `feat(web): bootstrap React application`

### T131 — Signature Dashboard
- Status: `DONE`
- Priority: P1
- Depends on: T130, T060
- Commit:
  - `feat(web): add signature dashboard`

### T132 — Signature Detail
- Status: `DONE`
- Priority: P1
- Depends on: T131
- Commit:
  - `feat(web): add signature detail page`

### T133 — Provider and Certificate UI
- Status: `DONE`
- Priority: P1
- Depends on: T061, T062
- Commit:
  - `feat(web): add provider and certificate views`

---

## Phase 17 — Signature Verification

> Exposes the Phase 9 validation engine (`OpenSignature.Validation`) through REST APIs and the React UI. Cryptographic verification covers Baseline B CAdES/XAdES/PAdES; results are shown in detail (overall status, crypto check, certificate path, revocation, reason codes). Not a full ETSI EN 319 102-1 AdES conformance report.

### T160 — Verification Application Service
- Status: `DONE`
- Priority: P0
- Depends on: T021, T091, T092
- Scope:
  - Application port `ISignatureVerificationService`.
  - Verify a completed platform signature from stored signed bytes (and original content for detached CAdES when supplied).
  - Verify an uploaded signed document (ad-hoc) without persisting binaries.
  - Structured report DTO: overall status, crypto, certificate path, revocation, reason codes.
- Acceptance:
  - Completed signatures can be verified without signing again.
  - Incomplete signatures are rejected (not found / output not ready).
  - Uploaded files are size-limited and never written to RabbitMQ.
  - Private keys are never loaded or returned.
- Tests:
  - valid attached CAdES stored signature
  - modified signed bytes fail
  - queued signature is not verifiable
  - ad-hoc upload of a valid signature
- Commit:
  - `feat(validation): add signature verification application service`

### T161 — Verification API
- Status: `DONE`
- Priority: P0
- Depends on: T160, T060
- Scope:
  - `GET /api/v1/signatures/{id}/verification`
  - `POST /api/v1/verifications`
  - RFC 7807 Problem Details
  - `SignaturesRead` authorization
- Acceptance:
  - API never performs signing.
  - Detailed JSON report is returned for completed signatures.
  - Ad-hoc verification accepts `file`, `format`, and optional `originalFile` (detached CAdES).
- Tests:
  - API integration for stored and uploaded verification
  - 404 / 409 paths
  - OpenAPI includes verification routes
- Commit:
  - `feat(api): add signature verification endpoints`

### T162 — Verification UI
- Status: `DONE`
- Priority: P0
- Depends on: T161, T132
- Scope:
  - Signature detail page shows a detailed verification report for completed signatures.
  - Dedicated Verify page for uploaded signed documents.
  - Display overall status, reason codes, cryptographic check, certificate path, and revocation.
- Acceptance:
  - A completed signature shows VALID / INVALID / INDETERMINATE with supporting detail.
  - Users can upload a signed file and see the same detailed report.
- Commit:
  - `feat(web): show detailed signature verification results`

---

## Phase 14 — CI/CD and Operations

### T140 — CI
- Status: `DONE`
- Priority: P0
- Depends on: T001
- Pipeline:
  - restore
  - build
  - test
  - lint
  - security checks
  - publish Api, Worker, and Web images to GHCR on `main`
- Commit:
  - `ci: add build, test, and container publish pipeline`

### T141 — Container Images
- Status: `DONE`
- Priority: P1
- Depends on: T070
- Scope:
  - Dockerfiles for Api, Worker, and Web.
  - Full-stack `docker-compose.yml` (Postgres, RabbitMQ, Api, Worker, Web, shared storage/certs).
  - Ephemeral development PFX init for compose (never bake secrets into images).
- Commit:
  - `build: add application container images`

### T142 — Kubernetes Deployment
- Status: `TODO`
- Priority: P2
- Depends on: T141
- Commit:
  - `feat(deploy): add Kubernetes manifests`

---

## Phase 15 — Documentation

### T150 — Architecture Documentation
- Status: `DONE`
- Priority: P0
- Depends on: T001
- Deliverable:
  - `docs/ARCHITECTURE.md`
- Commit:
  - `docs(architecture): document platform architecture`

### T151 — API Documentation
- Status: `TODO`
- Priority: P0
- Depends on: T060
- Deliverable:
  - `docs/API.md`
- Commit:
  - `docs(api): document REST API`

### T152 — Security Documentation
- Status: `TODO`
- Priority: P0
- Depends on: T110
- Deliverable:
  - `docs/SECURITY.md`
- Commit:
  - `docs(security): document security model`

### T153 — Device Integration Documentation
- Status: `TODO`
- Priority: P1
- Depends on: T100
- Deliverable:
  - `docs/DEVICE-INTEGRATION.md`
- Commit:
  - `docs(signing): document device integration`

### T154 — Signature Profile Matrix
- Status: `TODO`
- Priority: P0
- Depends on: T051, T052, T053
- Deliverable:
  - `docs/SIGNATURE-PROFILES.md`
- Commit:
  - `docs(signing): document signature profile matrix`

### T155 — Test Strategy
- Status: `DONE`
- Priority: P0
- Depends on: T001
- Deliverable:
  - `docs/TEST-STRATEGY.md`
- Commit:
  - `docs(test): document test strategy`

### T156 — Operations Guide
- Status: `TODO`
- Priority: P1
- Depends on: T141
- Deliverable:
  - `docs/OPERATIONS.md`
- Commit:
  - `docs(ops): document operational procedures`

---

## Phase 16 — Release Candidate

### T200 — Full Regression
- Status: `TODO`
- Priority: P0
- Depends on: all P0 tasks
- Acceptance:
  - all P0 tests pass
  - no known critical/high security issue
  - Docker environment reproducible
  - docs synchronized.

### T201 — Cryptographic Interoperability Matrix
- Status: `TODO`
- Priority: P0
- Depends on: T051, T052, T053, T091
- Acceptance:
  - independently generated signatures validate
  - generated signatures validate independently
  - negative cases are covered.

### T202 — Security Review
- Status: `TODO`
- Priority: P0
- Depends on: T110, T111, T112, T113
- Acceptance:
  - no secrets in repository
  - upload/path traversal checks
  - authorization tests
  - tenant isolation tests
  - logging review
  - dependency vulnerability review.

### T203 — Release Candidate
- Status: `TODO`
- Priority: P0
- Depends on: T200, T201, T202
- Acceptance:
  - all P0 tasks `DONE`
  - all required documentation `DONE`
  - CI green
  - release notes prepared.
- Commit:
  - `chore(release): prepare release candidate`
