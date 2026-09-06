# Signature Profile Matrix

| Format | B | T | LT | LTA | MVP |
|---|---:|---:|---:|---:|---:|
| PAdES | Yes | Planned | Planned | Planned | B |
| XAdES | Yes | Planned | Planned | Planned | B |
| CAdES | Yes | Planned | Planned | Planned | B |
| ASiC-S | Planned | Planned | Planned | Planned | No |
| ASiC-E | Planned | Planned | Planned | Planned | No |

## B

Base signature with the required cryptographic and certificate attributes.

## T

B-level signature plus a trusted timestamp.

## LT

T-level signature plus the validation material required for long-term validation.

## LTA

LT-level signature plus archival preservation timestamps/evidence as required by the applicable profile.

The implementation must validate actual profile requirements instead of treating these levels as string labels.

OpenSignature **never silently downgrades** a requested profile. Requests for T/LT/LTA currently fail with `SIGNATURE_PROFILE_UNSUPPORTED`.

## Implementation status (Baseline B)

### CAdES-B

- Detached and attached (encapsulated) CMS `SignedData` via BouncyCastle CMS + `ISigningProvider.SignDigestAsync`.
- Includes signing certificate, signing-time, and ESS `signing-certificate-v2`.
- Independently validated with BCL `SignedCms.CheckSignature`.

### XAdES-B

- Enveloped, enveloping, and detached packaging modes.
- XMLDSig (`SignedXml`) with XAdES qualifying properties: `SigningTime` and `SigningCertificate` under `SignedProperties`.
- Verifiable with `SignedXml.CheckSignature`.
- **Gaps vs full ETSI EN 319 132-1 XAdES-B:** does not yet claim full ETSI conformance tooling coverage (e.g. complete `SigningCertificateV2`, data-object format policies, commitment-type, or production-grade Id/schema handling beyond OpenSignature’s verifier). Detached mode packages content under a stable Id for self-contained verification rather than a pure external URI.

### PAdES-B

- PDF **incremental update** with `/ByteRange`, `/Contents` hex CMS container, `/SubFilter /ETSI.CAdES.detached`, and signing certificate material inside the CMS.
- **Library choice:** `BouncyCastle.Cryptography` for CMS + purpose-built PDF incremental updater (no iText / AGPL dependency).
- Validated by re-hashing ByteRange bytes and verifying the embedded detached CMS.
- Incremental updates preserve the original `/Pages` tree (resolved via classic xref, xref streams, and object streams). A replacement catalog that hardcodes `/Pages 2 0 R` is not used; that previously collapsed real multi-page PDFs to a blank first page.
- Optional **visible appearance**: when requested, the widget uses a non-zero `/Rect`, a Form XObject `/AP`, page `/Annots`, and stamp text `Digitally signed by {CN}` plus signing time, optional note (`/Reason`), and optional JPEG/PNG image. Default remains invisible (`/Rect [0 0 0 0]`). Appearance images are stored as `appearance.bin` and are never placed on RabbitMQ.
- **Gaps:** no pre-existing signature field reuse, no DSS/VRI, no multiple signatures orchestration beyond incremental append basics, no interactive page-coordinate placement UI.

### Orchestration defaults

| Format | Default packaging |
|--------|-------------------|
| CAdES | Attached (encapsulated) |
| XAdES | Enveloped |
| PAdES | Incremental PDF update |

ASiC-S / ASiC-E remain unsupported (`SIGNATURE_FORMAT_UNSUPPORTED`).
