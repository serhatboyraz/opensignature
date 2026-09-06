# Signature Profile Matrix

| Format | B | T | LT | LTA | MVP |
|---|---:|---:|---:|---:|---:|
| PAdES | Yes | Yes | Yes | Yes | B |
| XAdES | Yes | Yes | Yes | Yes | B |
| CAdES | Yes | Yes | Yes | Yes | B |
| ASiC-S | Yes | Yes | Yes | Yes | B |
| ASiC-E | Yes | Yes | Yes | Yes | B |

OpenSignature **never silently downgrades** a requested profile. If a TSA is missing or timestamping fails, T/LT/LTA fail with `TIMESTAMP_AUTHORITY_UNAVAILABLE` or `TIMESTAMP_OPERATION_FAILED`. If CRL/OCSP evidence is missing, LT/LTA fail with `SIGNATURE_VALIDATION_DATA_UNAVAILABLE`.

## B

Base signature with the required cryptographic and certificate attributes.

## T

B-level signature plus a trusted RFC 3161 timestamp.

## LT

T-level signature plus the validation material required for long-term validation (certificates and revocation evidence).

## LTA

LT-level signature plus an archival timestamp as required by the applicable profile.

The implementation validates actual profile requirements instead of treating these levels as string labels.

## Configuration

### Timestamp authority

Default DI registration is `UnavailableTimestampAuthority`: T/LT/LTA fail closed.

The worker enables an HTTP RFC 3161 client when `Timestamping:Url` is set:

```json
"Timestamping": {
  "Url": "https://tsa.example.invalid/",
  "PolicyOid": "",
  "Username": "",
  "PasswordSecretName": "Timestamping:Password"
}
```

- HTTP TSA: `AddRfc3161TimestampAuthority` (`application/timestamp-query`, nonce, imprint, token validation, optional HTTP Basic Auth).
- In-process TSA: `AddLocalRfc3161TimestampAuthority` (tests and local development; the TSA private key stays inside that type and is never exported).
- Basic Auth: set `Username`; resolve the password via `PasswordSecretName` / `ISigningSecretProvider`. Omit `Username` for anonymous TSAs.

### Long-term validation data

Default is `SigningCertificateOnlyValidationDataProvider` (signing certificate only). LT/LTA therefore fail unless a caller registers `AddLongTermValidationData` with at least one CRL or OCSP response. OpenSignature does **not** fetch live OCSP/CRL in this phase and does **not** fabricate revocation data.

## Implementation status

### CAdES

- **B:** Detached and attached (encapsulated) CMS `SignedData` via BouncyCastle CMS + `ISigningProvider.SignDigestAsync`. Includes signing certificate, signing-time, and ESS `signing-certificate-v2`. Independently validated with BCL `SignedCms.CheckSignature`.
- **T:** Unsigned `id-aa-signatureTimeStampToken` over the signature value.
- **LT:** Certificate values + revocation values (CRL and/or OCSP required).
- **LTA:** Archive time-stamp v3 (`0.4.0.1733.2.4`) over a hash of the current CMS.
- **Gap:** LTA hashes the CMS as a whole; it is not a full ETSI ATSHashIndex-v3 construction.

### XAdES

- **B:** Enveloped, enveloping, and detached packaging. XMLDSig (`SignedXml`) with XAdES `SigningTime` and `SigningCertificate` under `SignedProperties`. Verifiable with `SignedXml.CheckSignature`.
- **T:** Unsigned `SignatureTimeStamp`.
- **LT:** `CertificateValues` and `RevocationValues`.
- **LTA:** `ArchiveTimeStamp`.
- **Gaps vs full ETSI EN 319 132-1:** Baseline B does not yet claim complete `SigningCertificateV2`, data-object format policies, commitment-type, or production-grade Id/schema handling beyond OpenSignature’s verifier. Detached mode packages content under a stable Id for self-contained verification. LTA timestamps the document bytes before inserting `ArchiveTimeStamp` (not a full ETSI archive-time-stamp covering all evidence objects).

### PAdES

- **B:** PDF incremental update with `/ByteRange`, `/Contents` hex CMS, `/SubFilter /ETSI.CAdES.detached`. Optional visible appearance (stamp text, note, JPEG/PNG). Appearance images are stored as `appearance.bin` and never placed on RabbitMQ. A later PAdES request on an already-signed PDF appends a new signature (unique `OpenSignatureN` field); visible widgets are placed so they do not overlap existing annotations.
- **T:** CMS signature timestamp (same CAdES-T unsigned attribute). Contents reservation is enlarged for advanced profiles.
- **LT:** DSS incremental update (certs, CRLs, OCSPs) plus CAdES LT unsigned attributes in the signature CMS. Stored `/ByteRange` is preserved after DSS; it is not recomputed from the new file length.
- **LTA:** DSS plus a document timestamp (`DocTimeStamp`, `/SubFilter /ETSI.RFC3161`). The CMS is enhanced as LT (not a CAdES archive timestamp); the PDF-level document timestamp is the archival proof.
- **Gaps:** no pre-existing signature field reuse, no VRI dictionary, no interactive page-coordinate placement UI.

### ASiC-S

ZIP container (ETSI EN 319 162-1 style): uncompressed `mimetype` first (`application/vnd.etsi.asic-s+zip`), one data object, detached CAdES in `META-INF/signature.p7s`. T/LT/LTA enhance that inner CAdES. Entry names reject `..`, `mimetype`, and `META-INF` traversal.

### ASiC-E

ZIP container (`application/vnd.etsi.asic-e+zip`) with `ASiCManifest.xml`. Inner CAdES covers the manifest. A ZIP input expands to multiple data objects; a non-ZIP input is a single data object. T/LT/LTA enhance the inner CAdES.

### Orchestration defaults

| Format | Default packaging |
|--------|-------------------|
| CAdES | Attached (encapsulated) |
| XAdES | Enveloped |
| PAdES | Incremental PDF update |
| ASiC-S | ZIP + detached CAdES |
| ASiC-E | ZIP + ASiCManifest + CAdES |

Unknown formats still throw `SIGNATURE_FORMAT_UNSUPPORTED`. Unknown profiles throw `SIGNATURE_PROFILE_UNSUPPORTED`.
