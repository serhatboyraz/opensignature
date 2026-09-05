# API Contract

## POST /api/v1/signatures

Creates an asynchronous signing job.

### Headers

- `Authorization`
- `Idempotency-Key`
- `X-Correlation-Id` (optional)

### Multipart fields

- `file`
- `format`: `PAdES | XAdES | CAdES`
- `profile`: `B | T | LT | LTA`
- `signingProvider`
- `certificateSelector`

### Response

`202 Accepted`

```json
{
  "id": "0198...",
  "status": "Queued",
  "statusUrl": "/api/v1/signatures/0198..."
}
```

## GET /api/v1/signatures/{id}

Returns the current signing state.

## GET /api/v1/signatures/{id}/content

Returns the completed signed document.

## POST /api/v1/signatures/{id}/cancel

Requests cancellation if signing has not started.

## GET /api/v1/certificates

Lists certificates visible to configured signing providers.

## GET /api/v1/providers

Lists configured signing providers.

## GET /api/v1/providers/{id}/health

Returns provider availability.

## Error model

Use RFC 7807 Problem Details.

Example:

```json
{
  "type": "https://example.invalid/problems/signature-failed",
  "title": "Signature creation failed",
  "status": 422,
  "code": "SIGNING_PROVIDER_ERROR",
  "detail": "The signing provider rejected the operation.",
  "traceId": "..."
}
```
