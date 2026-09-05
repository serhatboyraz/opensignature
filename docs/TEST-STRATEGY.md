# OpenSignature Test Strategy

This document defines how OpenSignature is tested. It aligns with `docs/PRODUCT-SPEC.en.md` §25, the Testing section of the development skill, and the projects under `tests/`.

## 1. Goals and principles

**Goals**

- Prove correctness of domain rules, async signing flow, storage, queueing, and cryptographic output.
- Catch regressions early with a fast unit layer and targeted integration/E2E coverage.
- Validate signatures with an independent implementation whenever practical (interop).

**Principles**

| Principle | Rule |
| --- | --- |
| Deterministic | Prefer fixed clocks, seeded IDs, and stable fixtures; avoid flaky timing and order-dependent assertions. |
| No secrets in repo | Never commit PFX with private keys, passwords, PINs, HSM credentials, or production certificates. |
| Safe logging in tests | Never log private keys, PINs, passwords, full document contents, or full CMS/XML/PDF signature payloads. |
| Security-aware | Treat uploaded samples as untrusted; assert path-safe storage keys and tenant isolation where relevant. |
| No profile faking | If a library cannot satisfy a profile, mark it unsupported and test the rejection—do not fake LT/LTA. |
| English | Test names, messages, and fixtures descriptions are English. |

Every implementation task must include appropriate tests. No task is `DONE` until those tests pass.

## 2. Test pyramid

```text
        ┌─────────────────────┐
        │  Crypto interop     │  Independent validation of signed artifacts
        ├─────────────────────┤
        │  E2E / worker pipe  │  API → storage → DB → outbox → MQ → worker → signed → download
        ├─────────────────────┤
        │  Integration        │  PostgreSQL, RabbitMQ, storage, API, worker (Testcontainers)
        ├─────────────────────┤
        │  Unit               │  Domain, application, signing helpers (fast, no Docker)
        └─────────────────────┘
```

| Layer | Scope | Speed | External deps |
| --- | --- | --- | --- |
| **Unit** | Pure logic: state machine, validators, provider selection, idempotency keys, storage key generation, hashing/crypto helpers | Fast | None |
| **Integration** | EF/PostgreSQL, RabbitMQ topology, file storage adapters, API host, worker host | Medium | Prefer Testcontainers |
| **E2E** | Full async signing pipeline through download of completed content | Slower | PG + RabbitMQ + storage |
| **Crypto interop** | Positive/negative validation of PAdES/XAdES/CAdES/ASiC outputs with an independent validator | Medium–slow | Signing libs + validator |

Additional categories (expand as the product matures): API contract checks, security-focused tests (authz, upload limits, path traversal), and performance benchmarks (see product spec §26)—not required for every task.

## 3. Mapping to test projects

| Project | Layer | Owns |
| --- | --- | --- |
| `OpenSignature.Domain.Tests` | Unit | Signature request state machine, domain invariants, value objects, status transitions, cancel/complete rules |
| `OpenSignature.Application.Tests` | Unit | Command/query handlers, validators, idempotency handling, provider selection policy, DTO mapping |
| `OpenSignature.Infrastructure.Tests` | Unit + integration | Storage key builders, file storage adapters, EF repositories/migrations, outbox persistence; PG via Testcontainers when needed |
| `OpenSignature.Signing.Tests` | Unit + signing | Format/profile builders, crypto helpers, PFX (dev) provider behavior with **local-only** key material, provider failure modes |
| `OpenSignature.Api.IntegrationTests` | Integration | Minimal API endpoints, Problem Details, `202 Accepted`, authz/tenant checks, upload limits, OpenAPI smoke |
| `OpenSignature.Worker.IntegrationTests` | Integration + E2E | Message consume, job lock/idempotency, retries/DLQ, restart safety, full pipeline to completed state |
| `OpenSignature.Interop.Tests` | Crypto interop | Independent validation of generated signatures; positive and negative cases per format/profile |

Solution entry point: `OpenSignature.slnx`. Run all tests with `dotnet test`.

## 4. Required coverage areas

### Unit (must cover when the area is touched)

- Domain **state machine** (allowed transitions; reject illegal ones)
- **Validators** (request shape, format/profile, size limits)
- **Provider selection** (capability match; no silent profile downgrade)
- **Idempotency** (duplicate `Idempotency-Key` / job lock behavior)
- **Storage keys** (server-generated paths; no client filenames; no traversal)
- **Cryptographic helpers** (hashing, digest preparation, attribute builders)

### Integration (Testcontainers preferred)

- **PostgreSQL**: persistence, constraints, migrations, job/outbox state
- **RabbitMQ**: publish/consume, retry, DLQ; messages carry references only—**never document binaries**
- **Storage**: write/read input and signed objects under generated keys
- **API** and **worker** hosts wired to real dependencies in tests

### E2E pipeline

Assert the happy path end-to-end:

```text
API → storage → PostgreSQL → outbox → RabbitMQ → worker → signing provider
  → signed storage → completed state → download
```

Also cover reliability scenarios when implementing queue/worker work:

- duplicate message / double delivery
- worker restart mid-job
- retryable vs permanent failure → DLQ
- output storage failure
- database failure after signing (recoverable; incomplete output never exposed as completed)
- graceful shutdown

### Cryptographic interoperability

For each supported format/profile, cover at least:

| Case | Expectation |
| --- | --- |
| Valid signature | Independent validator accepts |
| Modified document | Validation fails |
| Modified signature | Validation fails |
| Wrong certificate | Validation fails / signing rejected as designed |
| Expired certificate | Rejected or validation fails as designed |
| Missing certificate / provider failure | Permanent failure; no infinite retry |

Use an **independent** validator/library. Document known interoperability limitations; do not claim unsupported profiles.

## 5. Local vs CI execution

### Local

```text
dotnet test
```

Optional filters:

```text
dotnet test --filter "FullyQualifiedName~OpenSignature.Domain.Tests"
dotnet test --filter "Category!=Slow"
```

- Unit tests: no Docker required.
- Integration/E2E: Docker available for Testcontainers (PostgreSQL, RabbitMQ). `docker-compose.yml` may be used for manual exploration; automated tests should prefer Testcontainers for isolation.
- Signing/interop: use public samples and **developer-local** key material outside the repo (env vars / user secrets), never committed PFX.

### CI

- Run `dotnet test` on every PR/main build (see CI task T140).
- Fail the build on any failing test.
- Prefer the same Testcontainers-based integration suite as local so results match.
- Do not inject production secrets; use ephemeral containers and public/sample material only.
- Keep the default CI gate green; mark genuinely slow suites so they can be filtered if needed, but do not skip required coverage for P0 paths.

## 6. Test data and certificates policy

| Allowed in repo (`samples/`) | Forbidden in repo |
| --- | --- |
| Public sample PDFs/XML/binaries for signing input | PFX/PKCS#12 containing private keys |
| Public certificates / trust material (CER/CRT/PEM **without** private key) | PFX passwords, PINs, HSM credentials |
| Synthetic unsigned fixtures | Production certificates or customer documents |
| Documented how-to for generating local test PFX | Secrets in appsettings committed to git |

Rules:

1. Put shared public fixtures under `samples/` (see `samples/README.md`).
2. Generate private-key material on the developer machine or in CI secrets stores; reference via configuration, not files under source control.
3. Hardware-backed providers: tests may mock the device boundary or use lab tokens; **never** export or assert on private key bytes.
4. Assertions may check hashes, statuses, and validation results—not full document or signature payload dumps in logs.

## 7. Definition of done (tests)

A task meets the testing bar when:

1. **Scope-matched tests exist** for the behavior changed (unit and/or integration/interop as appropriate).
2. **Happy path and relevant failure paths** are covered (especially permanent crypto failures, idempotency, and storage key safety).
3. **`dotnet test` passes** for affected projects (full solution preferred before marking `DONE`).
4. **No secrets** or private-key PFX were added to the tree.
5. **Logs/assertions** do not print private keys, PINs, passwords, or full document/signature contents.
6. **Docs** updated if behavior or test conventions changed (`docs/TEST-STRATEGY.md` or related).
7. Only then may `docs/TASKS.md` status move to `DONE` and a Conventional Commit be created (e.g. `test(signing): …` / `docs(test): …`).

### Quick checklist

- [ ] Right project(s) under `tests/` updated
- [ ] Deterministic fixtures
- [ ] Failure paths covered
- [ ] Testcontainers used where PG/RabbitMQ are required
- [ ] Interop positive/negative cases for new crypto surfaces
- [ ] `dotnet test` green
- [ ] No secrets committed
