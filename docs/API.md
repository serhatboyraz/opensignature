# OpenSignature REST API

Contract for Phase 6 signing, certificate, provider, and Phase 17 verification endpoints.

Base path: `/api/v1`

OpenAPI document (Development and Testing): `GET /openapi/v1.json`

Authentication (`Authorization: ApiKey <key>` or `X-Api-Key`) is enforced when `Authentication:Enabled=true`. Default is off for local smoke tests. Callers identify the tenant with `X-Tenant-Id` (must match the API key tenant when authenticated) or the key / configured default tenant.

Private keys are never returned by any endpoint. Certificate APIs expose public certificate metadata only.

---

## Common headers

| Header | Required | Description |
|--------|----------|-------------|
| `Authorization` / `X-Api-Key` | Required when `Authentication:Enabled=true` | MVP API key (`Authorization: ApiKey <key>` or `X-Api-Key`). Production: OAuth2/OIDC + JWT. |
| `X-Tenant-Id` | Recommended | Tenant scope for the request. If omitted, the API uses `Signatures:DefaultTenantId`. |
| `Idempotency-Key` | Recommended for `POST /signatures` | Opaque client key. Replays with the same tenant + key return the original create result instead of a second job. |
| `X-Correlation-Id` | Optional | Client correlation id propagated into logs, audit, and status responses. |

---

## Error model

Errors use RFC 7807 Problem Details. Machine-readable codes are in the `errorCode` extension (not `code`).

```json
{
  "type": "https://httpstatuses.com/400",
  "title": "Invalid request",
  "status": 400,
  "detail": "A non-empty 'file' form field is required.",
  "errorCode": "SIGNATURE_REQUEST_INVALID",
  "traceId": "..."
}
```

### Machine-readable error codes

| Code | Typical HTTP status | When |
|------|---------------------|------|
| `SIGNATURE_REQUEST_INVALID` | 400, 413 | Missing/invalid multipart, tenant, correlation id, empty upload, or size limit |
| `SIGNATURE_FORMAT_UNSUPPORTED` | 400 | Unknown or unsupported `format` |
| `SIGNATURE_PROFILE_UNSUPPORTED` | 400 | Unknown or unsupported `profile` |
| `SIGNING_PROVIDER_UNAVAILABLE` | 400 | Unknown provider type or provider not available for the request |
| `SIGNING_CERTIFICATE_NOT_FOUND` | 400 | Requested certificate thumbprint not found |
| `SIGNING_CERTIFICATE_EXPIRED` | 422 / job failure | Certificate expired at signing time |
| `SIGNATURE_INPUT_NOT_FOUND` | 404 | Signature id not found for the tenant |
| `SIGNATURE_OUTPUT_NOT_FOUND` | 409 | Signed content requested before completion (or output missing) |
| `SIGNATURE_NOT_CANCELLABLE` | 409 | Cancel requested after signing has progressed past a cancellable state |
| `SIGNATURE_ALREADY_COMPLETED` | 409 | Operation conflicts with a completed signature |
| `SIGNING_OPERATION_FAILED` | 500 / job failure | Unexpected signing or API failure |
| `SIGNING_PROVIDER_UNSUPPORTED` | job failure | Provider type not implemented for the job |
| `TIMESTAMP_AUTHORITY_UNAVAILABLE` | job failure | T/LT/LTA requested but no RFC 3161 TSA is configured |
| `TIMESTAMP_OPERATION_FAILED` | job failure | TSA HTTP/token validation failed (retried, then DLQ) |
| `SIGNATURE_VALIDATION_DATA_UNAVAILABLE` | job failure | LT/LTA requested but CRL/OCSP evidence is missing |

Clients should treat `errorCode` as stable for branching; `detail` is human-readable and may change.

---

## Signatures

### POST /api/v1/signatures

Creates an asynchronous signing job. The API validates input, stores the file, persists metadata, enqueues work via the outbox, and returns immediately. Cryptographic signing runs in the worker — never synchronously in the API.

#### Headers

- `Content-Type: multipart/form-data` (required)
- `X-Tenant-Id` (recommended)
- `Idempotency-Key` (recommended)
- `X-Correlation-Id` (optional)
- `Authorization` (roadmap)

#### Multipart fields

| Field | Required | Values / notes |
|-------|----------|----------------|
| `file` | Yes | Document bytes. Non-empty. Subject to `Signatures:MaxUploadBytes`. |
| `format` | Yes | `PAdES`, `XAdES`, `CAdES`, `ASiC_S`, `ASiC_E` |
| `profile` | Yes | `B`, `T`, `LT`, `LTA` |
| `signingProvider` | Yes | `Pfx`, `Pkcs11`, `SmartCard`, `Hsm` |
| `certificateThumbprint` | No | Selects a certificate known to the provider (public metadata / thumbprint only) |
| `visibleSignature` | No | `true` / `false`. PAdES only. Draws a visible stamp on the selected page. |
| `signatureNote` | No | Optional text on the stamp and PDF `/Reason`. Max 500 characters. PAdES only. Implies visible when set. |
| `signaturePage` | No | 1-based page number for the stamp (default `1`). PAdES only. |
| `signatureImage` | No | Optional JPEG or PNG stamp image (max `Signatures:MaxAppearanceImageBytes`, default 2 MiB). Stored separately from the document; never sent on RabbitMQ. PAdES only. Implies visible when set. |

Filenames and client MIME types are untrusted; storage keys are server-generated.

#### Responses

| Status | Meaning |
|--------|---------|
| `202 Accepted` | Job accepted. Body includes `id`, `status` (`Queued`), `createdAt`, `statusUrl`. `Location` may mirror `statusUrl`. |
| `400 Bad Request` | Invalid form, tenant, correlation id, format, profile, provider, or certificate |
| `413 Payload Too Large` | Upload exceeds configured max size (`SIGNATURE_REQUEST_INVALID`) |

Example `202` body:

```json
{
  "id": "0198...",
  "status": "Queued",
  "createdAt": "2026-09-05T17:00:00Z",
  "statusUrl": "/api/v1/signatures/0198..."
}
```

---

### GET /api/v1/signatures/{id}

Returns the current signing state for the tenant.

#### Headers

- `X-Tenant-Id` (recommended)
- `Authorization` (roadmap)

#### Path

| Parameter | Type | Description |
|-----------|------|-------------|
| `id` | UUID | Signature request id |

#### Responses

| Status | Meaning |
|--------|---------|
| `200 OK` | Status payload |
| `400 Bad Request` | Invalid tenant (`SIGNATURE_REQUEST_INVALID`) |
| `404 Not Found` | Unknown id for tenant (`SIGNATURE_INPUT_NOT_FOUND`) |

Example `200` body:

```json
{
  "id": "0198...",
  "tenantId": "tenant-demo",
  "status": "Queued",
  "format": "PAdES",
  "profile": "B",
  "signingProvider": "Pfx",
  "visibleSignature": false,
  "signatureNote": null,
  "appearancePageNumber": 1,
  "hasAppearanceImage": false,
  "createdAt": "2026-09-05T17:00:00Z",
  "queuedAt": "2026-09-05T17:00:00Z",
  "startedAt": null,
  "completedAt": null,
  "failedAt": null,
  "errorCode": null,
  "errorMessage": null,
  "correlationId": null,
  "statusUrl": "/api/v1/signatures/0198..."
}
```

`status` values include domain states such as `Queued`, `Processing`, `Completed`, `Failed`, `Cancelled`, and related intermediate states.

---

### GET /api/v1/signatures/{id}/content

Downloads the signed document when the request is `Completed`.

#### Headers

- `X-Tenant-Id` (recommended)
- `Authorization` (roadmap)

#### Responses

| Status | Meaning |
|--------|---------|
| `200 OK` | Binary body (`Content-Type` / filename from stored output metadata) |
| `400 Bad Request` | Invalid tenant |
| `404 Not Found` | Unknown signature (`SIGNATURE_INPUT_NOT_FOUND`) |
| `409 Conflict` | Not ready or output missing (`SIGNATURE_OUTPUT_NOT_FOUND`) |

---

### POST /api/v1/signatures/{id}/cancel

Requests cancellation. Reliable only before signing has progressed past a cancellable state (typically before / at early queue processing).

#### Headers

- `X-Tenant-Id` (recommended)
- `Authorization` (roadmap)

#### Responses

| Status | Meaning |
|--------|---------|
| `200 OK` | Cancelled; body is the updated status payload |
| `400 Bad Request` | Invalid tenant |
| `404 Not Found` | Unknown signature (`SIGNATURE_INPUT_NOT_FOUND`) |
| `409 Conflict` | Not cancellable (`SIGNATURE_NOT_CANCELLABLE`) |

---

### GET /api/v1/signatures/{id}/verification

Verifies the stored signed output of a completed signature request. Does not perform signing. Returns a detailed report: overall status (`VALID` / `INVALID` / `INDETERMINATE`), cryptographic check, certificate path, revocation, and machine-readable reason codes.

Coverage: CAdES (attached), XAdES, PAdES, and ASiC-S/E (inner CAdES). Cryptographic and certificate-path checks only — not a full ETSI EN 319 102-1 AdES conformance report (T/LT/LTA evidence is not independently evaluated).

#### Headers

- `X-Tenant-Id` (recommended)
- `Authorization` (when authentication is enabled; `SignaturesRead`)

#### Responses

| Status | Meaning |
|--------|---------|
| `200 OK` | Detailed verification report |
| `400 Bad Request` | Invalid tenant |
| `404 Not Found` | Unknown signature (`SIGNATURE_INPUT_NOT_FOUND`) |
| `409 Conflict` | Not completed / output missing (`SIGNATURE_OUTPUT_NOT_FOUND`) |

Example `200` body:

```json
{
  "overallStatus": "VALID",
  "isValid": true,
  "reasonCodes": ["SIG_VALID", "CERT_VALID"],
  "checkedAt": "2026-09-06T08:00:00Z",
  "source": "StoredSignature",
  "format": "CAdES",
  "signatureId": "0198...",
  "detail": null,
  "limitations": "Cryptographic verification and certificate path checks for CAdES, XAdES, PAdES, and ASiC (ASiC unpacks the inner CAdES). Not a full ETSI EN 319 102-1 AdES conformance report: T/LT/LTA timestamps and revocation evidence are not independently evaluated.",
  "signature": {
    "cryptoValid": true,
    "signerThumbprint": "...",
    "signerSubject": "CN=...",
    "reasonCodes": ["SIG_VALID"]
  },
  "certificate": {
    "isValid": true,
    "subject": "CN=...",
    "issuer": "CN=...",
    "thumbprint": "...",
    "notBefore": "2026-01-01T00:00:00Z",
    "notAfter": "2027-01-01T00:00:00Z",
    "reasonCodes": ["CERT_VALID"],
    "chainStatus": [],
    "revocation": {
      "status": "Unknown",
      "source": "Offline",
      "detail": null
    }
  }
}
```

---

## Ad-hoc verification

### POST /api/v1/verifications

Verifies an uploaded signed document in memory. The file is not persisted and is never placed on RabbitMQ.

#### Multipart fields

| Field | Required | Values / notes |
|-------|----------|----------------|
| `file` | Yes | Signed artifact (CMS, signed XML, or signed PDF). Subject to `Signatures:MaxUploadBytes`. |
| `format` | Yes | `PAdES`, `XAdES`, `CAdES` |
| `originalFile` | No | Original document for detached CAdES |

#### Responses

| Status | Meaning |
|--------|---------|
| `200 OK` | Detailed verification report (`source`: `UploadedDocument`) |
| `400 Bad Request` | Missing file/format (`SIGNATURE_REQUEST_INVALID` / `SIGNATURE_FORMAT_UNSUPPORTED`) |
| `413 Payload Too Large` | Upload exceeds configured max size |

---

## Certificates

Public certificate metadata only. Endpoints never export private keys, PFX material, or hardware secrets.

### GET /api/v1/certificates

Lists certificates visible to configured signing providers for the tenant/context.

#### Headers

- `X-Tenant-Id` (recommended)
- `Authorization` (roadmap)

#### Responses (contract)

| Status | Meaning |
|--------|---------|
| `200 OK` | Array of public certificate descriptors (e.g. id, subject, issuer, notBefore, notAfter, thumbprint, provider id) |
| `401` / `403` | When authentication/authorization is enabled |

Private key material is never included.

---

### GET /api/v1/certificates/{id}

Returns one certificate’s public metadata.

#### Path

| Parameter | Description |
|-----------|-------------|
| `id` | Certificate identifier (implementation-defined: thumbprint or platform id) |

#### Responses (contract)

| Status | Meaning |
|--------|---------|
| `200 OK` | Public certificate metadata |
| `404 Not Found` | Unknown certificate |
| `401` / `403` | When auth is enabled |

Does not return private keys or exportable key blobs.

---

## Providers

### GET /api/v1/providers

Lists configured signing providers (type, id, display name, enabled flag). Does not expose credentials, PIN, or keystore secrets.

#### Headers

- `X-Tenant-Id` (recommended)
- `Authorization` (roadmap)

#### Responses (contract)

| Status | Meaning |
|--------|---------|
| `200 OK` | Provider list |
| `401` / `403` | When auth is enabled |

---

### GET /api/v1/providers/{id}/health

Returns availability / health for a single provider (reachable, certificate store accessible, etc.). Diagnostics must not include secrets.

#### Path

| Parameter | Description |
|-----------|-------------|
| `id` | Provider id |

#### Responses (contract)

| Status | Meaning |
|--------|---------|
| `200 OK` | Health payload (`status`, optional `detail`, checked-at timestamp) |
| `404 Not Found` | Unknown provider |
| `503 Service Unavailable` | Provider unhealthy (optional; may also be `200` with degraded status) |

---

## OpenAPI discovery

| Environment | Document |
|-------------|----------|
| Development | `GET /openapi/v1.json` |
| Testing | `GET /openapi/v1.json` |
| Production | Not mapped by default |

The generated document includes signature routes under `/api/v1/signatures` (including `/verification`) and ad-hoc verification under `/api/v1/verifications`. Certificate and provider paths appear when those endpoint maps are registered.

---

## Notes

- Document binaries are never placed in RabbitMQ messages; only job/metadata references are queued.
- Do not silently downgrade a requested signature profile. T/LT/LTA require worker `Timestamping:Url`; LT/LTA also need CRL or OCSP evidence (`TIMESTAMP_AUTHORITY_UNAVAILABLE` / `SIGNATURE_VALIDATION_DATA_UNAVAILABLE`).
- Verification reports cover CAdES/XAdES/PAdES/ASiC cryptographic and certificate-path checks; they are not full ETSI EN 319 102-1 AdES conformance reports.
