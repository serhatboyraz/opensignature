# Operations Guide

## Health endpoints

Expose:

```text
/health/live
/health/ready
```

Readiness must include required infrastructure dependencies.

## Metrics

Monitor:

- request rate
- completion rate
- failure rate
- queue depth
- processing duration
- queue wait duration
- provider errors
- storage errors
- retry count
- DLQ count.

## Backup

Back up:

- PostgreSQL metadata
- object/file storage
- configuration excluding secrets.

Test restore procedures regularly.

## Retention

Document configurable retention for:

- input files
- signed files
- audit events
- job metadata.

Deletion must never accidentally remove audit records required by policy.

## Incident handling

For signing failures:

1. inspect correlation ID
2. inspect signature/job ID
3. inspect provider health
4. inspect queue state (`esign.signature.worker` and `esign.signature.dlq`)
5. inspect storage
6. inspect cryptographic error code
7. retry only if the failure is classified transient.

Worker retry defaults (`SigningJobRetry`):

- `MaxAttempts`: 5
- exponential backoff from `InitialBackoffMilliseconds` (1s) with multiplier 2, capped by `MaxBackoffMilliseconds` (60s)
- permanent failures (bad payload, cryptographic errors) go to the DLQ without further retry
- DLQ messages include `x-attempt`, `x-failure-kind`, and `x-failure-reason` headers

### Job lock and duplicate delivery

Workers acquire an idempotent processing lock before signing (`ISigningJobLockService`):

- Only one consumer may hold the lock for a given `SigningJob`.
- Duplicate RabbitMQ deliveries or concurrent workers that lose the lock race ACK without signing again.
- After a retryable signing failure, the worker releases the lock and sets the request to `RetryScheduled` before the bounded retry is published. Leaving `Processing` with an unexpired `LockedUntil` would ACK the retry and strand the job.
- Permanent failures (bad payload, cryptographic errors, unreadable PDF structure) mark the job and request `Failed` and go to the DLQ without further retry.
- After a worker crash/restart, `Locked` or `Processing` jobs whose `LockedUntil` is in the past (or unset) may be reclaimed; non-expired locks are not stolen.
- Default lock lease is 15 minutes (must exceed expected signing duration).

## Timestamping (RFC 3161)

T/LT/LTA profiles need a timestamp authority. The worker defaults to an unavailable TSA so those profiles fail closed (`TIMESTAMP_AUTHORITY_UNAVAILABLE`) instead of signing as Baseline B.

Set `Timestamping:Url` on the worker to enable the HTTP RFC 3161 client:

```json
"Timestamping": {
  "Url": "https://tsa.example.invalid/",
  "PolicyOid": ""
}
```

HTTP TSA failures are classified as retryable (`TIMESTAMP_OPERATION_FAILED`) until the bounded retry budget is exhausted.

LT/LTA also require CRL or OCSP evidence registered via `AddLongTermValidationData`. The default provider only includes the signing certificate, so LT/LTA fail with `SIGNATURE_VALIDATION_DATA_UNAVAILABLE` until revocation material is supplied. OpenSignature does not fabricate timestamps or revocation data.

## USB tokens and smart cards (PKCS#11)

The Providers page lists **configured signing providers**, not a live scan of every USB device. A dongle is visible only after a PKCS#11 module is registered.

### Why a dongle might be missing

1. Only the Development PFX provider is registered unless SmartCard/HSM is configured.
2. Vendor middleware (PKCS#11 DLL / SO) must be installed, and its architecture must match the API/Worker process (typically **x64**).
3. The API and Worker must run **on the host** (not inside Docker) so they can open the USB device. `docker-compose.yml` starts PostgreSQL and RabbitMQ only.
4. Restart the API and Worker after plugging the token in or installing middleware.

### Development auto-detect

With `Signing:SmartCard:AutoDetect=true` (default in `appsettings.Development.json`), OpenSignature probes well-known middleware libraries, including:

- `akisp11.dll` (AKİS)
- `eTPKCS11.dll` / `eToken.dll` (SafeNet / e-Güven)
- `aetpkss1.dll`
- `ngp11v211.dll` (TürkTrust)
- `opensc-pkcs11.dll`

If a library is found, a **USB Token / Smart Card** row appears. If middleware is missing, the same row still appears as **Unavailable** so the gap is visible.

### Manual module path

Set the vendor PKCS#11 path when auto-detect picks the wrong library or finds nothing:

```json
"Signing": {
  "SmartCard": {
    "ProviderId": "usb-token",
    "Name": "USB Token / Smart Card",
    "ModulePath": "C:\\Windows\\System32\\akisp11.dll",
    "AutoDetect": false,
    "TokenLabel": "",
    "PinSecretName": "smartcard-pin"
  }
}
```

If several tokens are present, set `SlotId` or `TokenLabel`.

### PIN

There is no secrets file in the git repository. `PinSecretName` (`Signing:SmartCard:Pin`) is the lookup key. Put the PIN in user secrets (preferred).

`dotnet user-secrets set` treats `:` as a nested path. Set the full configuration key on **both** projects:

```bash
dotnet user-secrets set "Secrets:Values:Signing:SmartCard:Pin" "<token-pin>" --project src/OpenSignature.Api
dotnet user-secrets set "Secrets:Values:Signing:SmartCard:Pin" "<token-pin>" --project src/OpenSignature.Worker
```

List / remove:

```bash
dotnet user-secrets list --project src/OpenSignature.Api
dotnet user-secrets remove "Secrets:Values:Signing:SmartCard:Pin" --project src/OpenSignature.Api
```

User secrets files (outside the repo, Windows):

```text
%APPDATA%\Microsoft\UserSecrets\opensignature-api-dev\secrets.json
%APPDATA%\Microsoft\UserSecrets\dotnet-OpenSignature.Worker-1710ba6a-80c6-474d-af7e-f5bea1dafad0\secrets.json
```

Set the PIN on **both** the API and the Worker. Health checks do **not** log in. Listing certificates and signing do. Wrong PIN attempts can lock the token.

The same `Signing:SmartCard` section must be configured on **both** the API (so the provider is listed) and the Worker (so signing can use the token).

### Expired USB-token certificates

By default, certificates outside `NotBefore`/`NotAfter` cannot sign (`CanSign=false`). For local development with an expired token certificate, set `Signing:AllowExpiredCertificates` on **both** the API and the Worker:

```json
"Signing": {
  "AllowExpiredCertificates": true,
  "SmartCard": {
    "ModulePath": "C:\\Windows\\System32\\eTPKCS11.dll",
    "PreferFirstSlotWhenAmbiguous": true,
    "PinSecretName": "Signing:SmartCard:Pin"
  }
}
```

Development `appsettings.Development.json` sets `AllowExpiredCertificates` and `PreferFirstSlotWhenAmbiguous` to `true`, and pins SafeNet `eTPKCS11.dll` when that middleware is installed. `PreferFirstSlotWhenAmbiguous` probes token-present slots until one exposes certificates (eToken/Aladdin often registers several virtual readers). Leave both flags `false` in production. Signature verification still reports that the certificate is expired; these flags only allow creating a signature for local testing.
