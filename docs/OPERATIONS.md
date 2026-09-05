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
