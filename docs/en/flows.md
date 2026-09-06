# Flows & diagrams

Visual reference for OpenSignature request processing, state machines, outbox reliability, and provider signing.

![Async flow SVG](../assets/images/async-flow-diagram.svg){ .architecture-hero }

## End-to-end signing sequence

```mermaid
sequenceDiagram
  autonumber
  actor Client
  participant Api as OpenSignature.Api
  participant DB as PostgreSQL
  participant FS as File Storage
  participant OB as Outbox Publisher
  participant MQ as RabbitMQ
  participant Wrk as Worker
  participant Prov as ISigningProvider

  Client->>Api: POST /api/v1/signatures (+ Idempotency-Key)
  Api->>Api: Validate multipart / format / profile / provider
  Api->>FS: Save input.bin (server-generated key)
  Api->>DB: SignatureRequest + SigningJob + OutboxMessage (1 TX)
  Api-->>Client: 202 Accepted { id, statusUrl }

  OB->>DB: Poll unpublished outbox
  OB->>MQ: Publish SigningJobMessage (refs only)
  OB->>DB: Mark PublishedAt

  MQ->>Wrk: Deliver message
  Wrk->>DB: Acquire idempotent job lock
  Wrk->>FS: Open input.bin
  Wrk->>Prov: Resolve provider · SignDigest
  Prov-->>Wrk: Signature bytes / CMS / XML / PDF update
  Wrk->>FS: Write signed.bin
  Wrk->>DB: Completed + audit
  Wrk->>MQ: ACK

  Client->>Api: GET /api/v1/signatures/{id}
  Api-->>Client: status Completed
  Client->>Api: GET /api/v1/signatures/{id}/content
  Api->>FS: Stream signed.bin
  Api-->>Client: Signed document
```

## Signature request state machine

```mermaid
stateDiagram-v2
  [*] --> Created
  Created --> Queued: accepted
  Created --> Rejected: validation / policy
  Queued --> Processing: worker lock
  Queued --> RetryScheduled: transient defer
  Processing --> Completed: signed + stored
  Processing --> Failed: permanent error
  Processing --> RetryScheduled: transient error
  RetryScheduled --> Queued: retry publish
  Queued --> Cancelled: cancel before start
  Created --> Cancelled: cancel before start

  Completed --> [*]
  Failed --> [*]
  Rejected --> [*]
  Cancelled --> [*]
```

Terminal states: **Completed**, **Failed**, **Rejected**, **Cancelled**.

## Worker job lock & idempotency

```mermaid
flowchart TD
  A[Consume RabbitMQ message] --> B{Request/job terminal?}
  B -->|Yes Completed/Cancelled/Rejected| ACK1[ACK without signing]
  B -->|No| C{Try acquire lock}
  C -->|Lost race / held| ACK2[ACK without signing]
  C -->|Locked| D[Load input · sign · store]
  D --> E{Success?}
  E -->|Yes| F[Persist Completed · ACK]
  E -->|Transient| G[Release lock · RetryScheduled · bounded retry]
  E -->|Permanent| H[Failed · publish DLQ · ACK]
```

Rules:

- At-least-once delivery must not produce duplicate successful signatures.
- Lock lease must exceed expected signing duration (default 15 minutes).
- Expired `Locked`/`Processing` may be reclaimed after crash; active locks are not stolen.

## Outbox reliability

```mermaid
flowchart LR
  subgraph same_tx [Same DB transaction]
    R[SignatureRequest]
    J[SigningJob]
    O[OutboxMessage]
  end
  same_tx --> Pub[Outbox publisher]
  Pub -->|success| MQ[[RabbitMQ]]
  Pub -->|mark| O2[PublishedAt set]
```

Avoids dual-write loss between PostgreSQL and the broker.

## Provider digest-sign flow

```mermaid
sequenceDiagram
  participant Wrk as Worker / Orchestrator
  participant Eng as Format signer
  participant Prov as Hardware provider
  participant Dev as Token / HSM

  Wrk->>Eng: Create signature (format + profile)
  Eng->>Eng: Build digest / ToBeSigned
  Eng->>Prov: SignDigestAsync(digest)
  Prov->>Dev: C_Sign / device op
  Dev-->>Prov: signature value
  Prov-->>Eng: signature (key never exported)
  Eng->>Eng: Embed into PAdES/XAdES/CAdES/ASiC
  Eng-->>Wrk: signed artifact bytes
```

## Verification flow

```mermaid
flowchart TD
  A[GET .../verification or POST /verifications] --> B[ISignatureValidator]
  B --> C[Crypto check CAdES/XAdES/PAdES/ASiC]
  B --> D[ICertificateValidator]
  D --> E[validity · chain · KU/EKU · revocation · policy]
  B --> F[IValidationReportBuilder]
  F --> G[JSON report to client / UI]
```

## Retry vs DLQ

| Failure class | Behavior |
| --- | --- |
| Transient (network, TSA timeout, lock contention) | Exponential backoff; bounded `MaxAttempts` |
| Permanent (bad payload, crypto failure, unreadable PDF) | DLQ immediately; no endless retry |

DLQ: `esign.signature.dlq` via `esign.signature.dlx`.
