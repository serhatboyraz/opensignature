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
