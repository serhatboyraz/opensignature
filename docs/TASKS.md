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
- Status: `TODO`
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
- Status: `TODO`
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
- Status: `TODO`
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
- Status: `TODO`
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
- Status: `TODO`
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

---

## Phase 6 — API

### T060 — Signature API
- Status: `TODO`
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
- Status: `TODO`
- Priority: P1
- Depends on: T040
- Commit:
  - `feat(api): add certificate endpoints`

### T062 — Provider API
- Status: `TODO`
- Priority: P1
- Depends on: T040
- Commit:
  - `feat(api): add signing provider endpoints`

### T063 — OpenAPI
- Status: `TODO`
- Priority: P0
- Depends on: T060
- Commit:
  - `docs(api): document signing API`

---

## Phase 7 — Worker End-to-End

### T070 — End-to-End Signing Pipeline
- Status: `TODO`
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
- Status: `TODO`
- Priority: P0
- Depends on: T070
- Tests:
  - duplicate message
  - worker restart
  - concurrent workers
- Commit:
  - `fix(worker): prevent duplicate signature processing`

---

## Phase 8 — ASiC and Advanced Profiles

### T080 — ASiC-S
- Status: `TODO`
- Priority: P1
- Depends on: T051, T052
- Commit:
  - `feat(signing): add ASiC-S container support`

### T081 — ASiC-E
- Status: `TODO`
- Priority: P1
- Depends on: T080
- Commit:
  - `feat(signing): add ASiC-E container support`

### T082 — RFC 3161 Timestamping
- Status: `TODO`
- Priority: P1
- Depends on: T051, T052, T053
- Commit:
  - `feat(timestamp): add RFC 3161 timestamp provider`

### T083 — PAdES-T/LT/LTA
- Status: `TODO`
- Priority: P1
- Depends on: T053, T082
- Commit:
  - `feat(signing): add advanced PAdES profiles`

### T084 — XAdES-T/LT/LTA
- Status: `TODO`
- Priority: P1
- Depends on: T052, T082
- Commit:
  - `feat(signing): add advanced XAdES profiles`

### T085 — CAdES-T/LT/LTA
- Status: `TODO`
- Priority: P1
- Depends on: T051, T082
- Commit:
  - `feat(signing): add advanced CAdES profiles`

---

## Phase 9 — Validation

### T090 — Certificate Validation Engine
- Status: `TODO`
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
- Status: `TODO`
- Priority: P1
- Depends on: T090, T051, T052, T053
- Commit:
  - `feat(validation): add signature validation service`

### T092 — Validation Reports
- Status: `TODO`
- Priority: P1
- Depends on: T091
- Commit:
  - `feat(validation): add validation reports`

---

## Phase 10 — Hardware

### T100 — PKCS#11 Abstraction
- Status: `TODO`
- Priority: P1
- Depends on: T040
- Commit:
  - `feat(signing): add PKCS11 provider abstraction`

### T101 — USB Token / Smart Card Provider
- Status: `TODO`
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
- Status: `TODO`
- Priority: P1
- Depends on: T100
- Scope:
  - PKCS#11
  - session pool
  - health
  - concurrency limits
- Commit:
  - `feat(signing): add HSM signing provider`

---

## Phase 11 — Security

### T110 — Authentication
- Status: `TODO`
- Priority: P1
- Depends on: T060
- Commit:
  - `feat(security): add API authentication`

### T111 — Authorization / RBAC
- Status: `TODO`
- Priority: P1
- Depends on: T110
- Commit:
  - `feat(security): add role based authorization`

### T112 — Tenant Isolation
- Status: `TODO`
- Priority: P1
- Depends on: T111
- Tests:
  - cross-tenant access denial
- Commit:
  - `feat(security): enforce tenant isolation`

### T113 — Secret Management
- Status: `TODO`
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
- Status: `TODO`
- Priority: P1
- Depends on: T001
- Commit:
  - `feat(web): bootstrap React application`

### T131 — Signature Dashboard
- Status: `TODO`
- Priority: P1
- Depends on: T130, T060
- Commit:
  - `feat(web): add signature dashboard`

### T132 — Signature Detail
- Status: `TODO`
- Priority: P1
- Depends on: T131
- Commit:
  - `feat(web): add signature detail page`

### T133 — Provider and Certificate UI
- Status: `TODO`
- Priority: P1
- Depends on: T061, T062
- Commit:
  - `feat(web): add provider and certificate views`

---

## Phase 14 — CI/CD and Operations

### T140 — CI
- Status: `TODO`
- Priority: P0
- Depends on: T001
- Pipeline:
  - restore
  - build
  - test
  - lint
  - security checks
- Commit:
  - `ci: add build and test pipeline`

### T141 — Container Images
- Status: `TODO`
- Priority: P1
- Depends on: T070
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
