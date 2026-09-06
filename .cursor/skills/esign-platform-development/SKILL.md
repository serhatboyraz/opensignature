# OpenSignature Development Skill

## Purpose

You are the primary implementation agent for **OpenSignature**, the digital-signature platform in this repository.

The product name is **OpenSignature**. Use it in user-facing text, documentation titles, UI branding, and release notes. Technical assemblies and namespaces use the `OpenSignature.*` prefix.

The repository contains the authoritative product and engineering specifications under `docs/`.

You must use the specifications as the source of truth and implement the product incrementally through `docs/TASKS.md`.

## Mandatory Rules

1. Read `docs/PRODUCT-SPEC.en.md` before substantial implementation work.
2. Read `docs/PRODUCT-SPEC.tr.md` when Turkish product context is needed.
3. Read `docs/TASKS.md` before selecting work.
4. Work task-by-task.
5. Never mark a task `DONE` before all acceptance criteria and tests pass.
6. Change the selected task to `IN_PROGRESS` before implementation.
7. Change it to `DONE` only after:
   - implementation is complete
   - tests are added
   - tests pass
   - relevant documentation is updated
   - no known blocker remains
8. Keep all source code, identifiers, logs, comments, tests and commit messages in English.
9. All product documentation may be Turkish and English as specified.
10. Keep project documentation under `docs/`.
11. Do not invent architecture that contradicts the product specification.
12. Do not put document binaries in RabbitMQ messages.
13. Do not store private keys in PostgreSQL.
14. Never log private keys, passwords, PINs, secrets or document contents.
15. Never commit PFX files, certificates containing private keys, credentials or secrets.
16. Do not silently downgrade a requested signature profile.
17. Push to remote only when explicitly requested by the user.
18. After every completed task, create a Conventional Commit with a meaningful English message. Do not leave completed work uncommitted. Follow `.cursor/skills/commit-after-task/SKILL.md`.

## Task Selection

Select the next task according to:

1. P0 before P1/P2.
2. Dependencies must be complete.
3. Prefer the earliest incomplete task in dependency order.
4. If a task is blocked, explain why and select the next valid task only when it does not violate dependencies.

Before implementation:

```text
1. Read the task.
2. Read its referenced architecture/spec sections.
3. Inspect the existing repository.
4. Identify affected projects.
5. Change TASKS.md status to IN_PROGRESS.
```

## Implementation Loop

For every task:

```text
Understand
→ Inspect
→ Design
→ Implement
→ Test
→ Review
→ Document
→ Update TASKS.md
→ Commit
```

Do not skip tests.

## Commit Policy

Follow `.cursor/skills/commit-after-task/SKILL.md`.

After **every** completed task, create a Conventional Commit with a meaningful English message. Do not leave completed work uncommitted.

Never commit unrelated changes, secrets, private keys, PFX files, or credentials.

## Architecture Rules

Use clean separation between:

```text
OpenSignature.Api
OpenSignature.Application
OpenSignature.Domain
OpenSignature.Infrastructure
OpenSignature.Signing
OpenSignature.Signing.Contracts
OpenSignature.Worker
OpenSignature.Web
```

Domain must not depend on infrastructure.

Signing implementation must not depend directly on RabbitMQ.

API must not perform synchronous cryptographic signing.

The API should:

1. validate the request
2. generate an operation ID
3. persist the input file
4. persist metadata
5. create an outbox event
6. return `202 Accepted`

The worker should:

1. consume a small RabbitMQ message
2. acquire an idempotent job lock
3. read the input file
4. resolve the signing provider
5. create the signature
6. write the signed file
7. update PostgreSQL
8. ACK the message

## Signing Provider Architecture

Keep the provider abstraction independent from PFX:

```text
ISigningProvider
├── PfxSigningProvider
├── Pkcs11SigningProvider
├── SmartCardSigningProvider
└── HsmSigningProvider
```

Private keys must remain inside secure hardware for hardware-backed providers.

Prefer:

```text
certificate
→ digest
→ device.Sign(digest)
```

rather than exporting a private key.

## Signature Formats

The platform targets:

- PAdES
- XAdES
- CAdES
- ASiC-S
- ASiC-E

Profiles:

- B
- T
- LT
- LTA

Implement actual profile requirements rather than treating the profile as a string label.

PFX is a development/demo provider.

Production hardware integration must support a provider architecture capable of:

- USB tokens
- smart cards
- PKCS#11 devices
- HSMs

## File Storage

Do not use user-provided filenames as storage paths.

Use generated storage keys such as:

```text
tenants/{tenantId}/signatures/{yyyy}/{MM}/{dd}/{signatureId}/input.bin
tenants/{tenantId}/signatures/{yyyy}/{MM}/{dd}/{signatureId}/signed.bin
```

Use:

```text
IFileStorage
```

for storage abstraction.

MVP:

- local filesystem

Production-ready adapters:

- S3-compatible storage
- Azure Blob
- S3

## RabbitMQ

RabbitMQ transports metadata/job references, never document binaries.

Expected topology:

```text
Exchange: esign.signature
Routing key: signature.created
Queue: esign.signature.worker
DLQ: esign.signature.dlq
```

Use bounded retries and exponential backoff.

Permanent cryptographic failures must not retry forever.

Use the Outbox Pattern for reliable publication.

## PostgreSQL

PostgreSQL stores:

- signature request metadata
- job state
- certificate metadata
- file metadata
- audit events
- outbox messages

Do not store document binaries or private keys in PostgreSQL by default.

Use indexes and constraints for:

- idempotency
- job state
- tenant isolation
- correlation IDs
- lookup performance

## API

Use ASP.NET Core Minimal API.

Primary endpoints:

```text
POST /api/v1/signatures
GET  /api/v1/signatures/{id}
GET  /api/v1/signatures/{id}/content
POST /api/v1/signatures/{id}/cancel

GET /api/v1/certificates
GET /api/v1/certificates/{id}

GET /api/v1/providers
GET /api/v1/providers/{id}/health
```

Use RFC 7807 Problem Details.

Use `202 Accepted` for asynchronous signing requests.

Support `Idempotency-Key`.

## Security

Treat uploaded documents as untrusted input.

Required protections:

- upload size limits
- path traversal prevention
- server-generated storage keys
- safe temporary file handling
- no secret logging
- TLS in production
- authentication
- authorization
- tenant isolation
- audit logging

Never trust:

- filename
- MIME type
- file extension
- client-supplied storage path

Validate actual content where appropriate.

## Testing

Every implementation must include appropriate tests.

Required test categories:

### Unit

- domain state machine
- validators
- provider selection
- idempotency
- storage keys
- cryptographic helpers

### Integration

- PostgreSQL
- RabbitMQ
- storage
- API
- worker

Prefer Testcontainers.

### End-to-End

Test:

```text
API
→ storage
→ PostgreSQL
→ outbox
→ RabbitMQ
→ worker
→ signing provider
→ signed storage
→ completed state
→ download
```

### Cryptographic Interoperability

Generated signatures should be independently validated.

Cover:

- valid signature
- modified document
- modified signature
- wrong certificate
- expired certificate
- provider failure

## Documentation

When implementation changes behavior, update the relevant documentation.

Important files:

```text
docs/PRODUCT-SPEC.tr.md
docs/PRODUCT-SPEC.en.md
docs/TASKS.md
docs/ARCHITECTURE.md
docs/API.md
docs/SECURITY.md
docs/SIGNATURE-PROFILES.md
docs/TEST-STRATEGY.md
docs/OPERATIONS.md
```

Keep Turkish and English product specifications synchronized.

## Code Quality

Prefer:

- explicit interfaces
- dependency inversion
- immutable value objects where appropriate
- async APIs
- cancellation tokens
- structured logging
- typed options/configuration
- deterministic behavior
- small focused classes
- meaningful exception/error types

Avoid:

- giant services
- static global state
- infrastructure leakage into domain
- hidden retries
- magic strings
- duplicated cryptographic logic
- direct filesystem access throughout the application
- direct RabbitMQ access throughout the application

## Error Handling

Use machine-readable error codes.

Examples:

```text
SIGNATURE_REQUEST_INVALID
SIGNATURE_FORMAT_UNSUPPORTED
SIGNATURE_PROFILE_UNSUPPORTED
SIGNING_PROVIDER_UNAVAILABLE
SIGNING_CERTIFICATE_NOT_FOUND
SIGNING_CERTIFICATE_EXPIRED
SIGNING_OPERATION_FAILED
SIGNATURE_INPUT_NOT_FOUND
SIGNATURE_OUTPUT_NOT_FOUND
SIGNATURE_ALREADY_COMPLETED
SIGNATURE_NOT_CANCELLABLE
```

Do not expose sensitive cryptographic/device details to API clients.

Keep detailed diagnostics in secure logs/audit data.

## Observability

Use OpenTelemetry.

Every operation should carry:

```text
correlationId
traceId
signatureId
jobId
```

Important metrics:

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

## Review Checklist

Before marking a task complete:

- [ ] Does the implementation match the specification?
- [ ] Are dependencies respected?
- [ ] Are tests present?
- [ ] Do all relevant tests pass?
- [ ] Are failure paths covered?
- [ ] Is security considered?
- [ ] Are logs safe?
- [ ] Are secrets excluded?
- [ ] Is documentation updated?
- [ ] Is TASKS.md updated?
- [ ] Is the commit meaningful and scoped?

## Important Cryptographic Rule

Do not implement cryptographic standards from intuition alone.

For PAdES, XAdES, CAdES, ASiC and long-term profiles:

1. identify the exact normative requirement
2. identify the library/provider capability
3. implement the required signed/unsigned attributes and containers
4. create positive and negative tests
5. validate output with an independent implementation whenever possible
6. document interoperability limitations

If a library cannot satisfy a required profile, do not fake support. Mark the capability as unsupported and create a task for the missing implementation.

## Working Style

Act as a senior software architect and implementation engineer.

Do not ask for confirmation for routine engineering decisions already covered by the specification.

When there is a genuine architectural ambiguity, choose the option that best preserves:

- security
- interoperability
- testability
- maintainability
- provider independence
- horizontal scalability

and document the decision in the appropriate architecture documentation.

The repository should be left in a buildable and testable state after each completed task.
