# Security Requirements

## Secrets

Never commit:

- PFX files
- private keys
- PFX passwords
- smart-card PINs
- HSM credentials
- API production secrets / API keys

### Secret management (`ISecretStore`)

OpenSignature resolves named secrets through `ISecretStore` (Application abstraction):

| Implementation | Use |
|----------------|-----|
| `ConfigurationSecretStore` | Development / user-secrets (`Secrets:Values`) |
| `EnvironmentSecretStore` | `OPENSIGNATURE_SECRET_{NAME}` (':' / '.' / '-' → `_`) |
| `RotatingSecretStore` | Wrapper with a rotation hook (`NotifyRotatedAsync`); MVP delegates to the inner chain |

Api and Worker register a rotating chain (configuration, then environment). Signing adapts the store via `SecretStoreSigningSecretProvider` → `ISigningSecretProvider` for PFX password resolution (`Signing:Pfx:PasswordSecretName`).

Never log secret values, PINs, or PFX passwords.

## Authentication (MVP)

Configure under `Authentication`:

```json
{
  "Authentication": {
    "Enabled": false,
    "ApiKeys": [
      {
        "KeyId": "demo-signer",
        "Key": "<from user-secrets>",
        "TenantId": "tenant-demo",
        "Roles": [ "Signer" ]
      }
    ]
  }
}
```

- Default: `Enabled=false` (anonymous API for local smoke tests).
- When `Enabled=true`, protected routes require `Authorization: ApiKey <key>` or `X-Api-Key`.
- `/health` and `/` remain anonymous.
- Missing/invalid key → **401** Problem Details with `errorCode: AUTH_UNAUTHORIZED`.
- Production roadmap: OAuth2 / OIDC + JWT (replace API keys).

## Authorization (RBAC)

Roles (PRODUCT-SPEC §21):

| Role | Capabilities |
|------|----------------|
| Administrator | All policies |
| Signer | Create / cancel signatures; read own status |
| Operator | Read signature status; cancel; provider health |
| Auditor | Read-only signature status / content |
| Developer | Certificates and providers |

Policies: `SignaturesWrite`, `SignaturesRead`, `SignaturesCancel`, `CertificatesRead`, `ProvidersRead`.

Wrong role → **403** with `errorCode: AUTH_FORBIDDEN`.

## Tenant isolation

- Each API key is bound to a `TenantId` claim.
- If `X-Tenant-Id` is present and differs from the key tenant → **403** with `errorCode: TENANT_ACCESS_DENIED`.
- When the header is omitted, the key tenant is used.
- Signature lookups remain tenant-scoped in the service layer (cross-tenant id → 404).

## File handling

- Maximum upload size is configurable.
- Validate file size before storage.
- Never trust client file names for storage paths.
- Generate server-side storage keys.
- Prevent path traversal.
- Compute SHA-256 while storing where practical.
- Do not expose temporary files through static hosting.

## Hardware signing

The platform must treat the signing device as the owner of the private key.

The application may request:

```text
certificate -> digest -> sign(digest)
```

but must not attempt to export the private key.

## Logging

Never log:

- private keys
- PINs
- PFX passwords
- API keys
- full document contents
- full CMS/XML/PDF signature payloads.

## Authorization (resources)

Every resource lookup must enforce tenant boundaries before returning metadata or files.

## Qualified signing

The platform itself does not make a signature legally qualified. Qualification depends on the certificate, trust service, signature-creation device and applicable legal/technical requirements.
