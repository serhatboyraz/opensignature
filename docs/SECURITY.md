# Security Requirements

## Secrets

Never commit:

- PFX files
- private keys
- PFX passwords
- smart-card PINs
- HSM credentials
- API production secrets.

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
- full document contents
- full CMS/XML/PDF signature payloads.

## Authorization

Every resource lookup must enforce tenant boundaries before returning metadata or files.

## Qualified signing

The platform itself does not make a signature legally qualified. Qualification depends on the certificate, trust service, signature-creation device and applicable legal/technical requirements.
