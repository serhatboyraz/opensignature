# E-Signature Platform — Product & Engineering Specification (TR)

> **Durum:** Ana geliştirme kaynağı  
> **Dil:** Bu doküman Türkçe'dir. Kod, API alan adları, sınıf/metot isimleri, commit mesajları ve teknik semboller İngilizce olacaktır.  
> **Hedef:** TÜBİTAK ESYA/MA3 benzeri kullanım senaryolarını, ancak bağımsız ve modern bir .NET tabanlı servis mimarisiyle sağlamak.  
> **Önemli:** Bu proje ESYA'nın kodunu, lisansını veya kapalı uygulama detaylarını kopyalamaz. "ESYA uyumluluğu" burada desteklenen imza formatları, sertifika/akıllı kart/HSM entegrasyonları, doğrulama davranışları ve entegrasyon kolaylığı hedefidir.

## 1. Ürün Tanımı

Ürün, kurumsal uygulamaların elektronik imza işlemlerini merkezi bir servis üzerinden gerçekleştirmesini sağlayan bir **Digital Signature Platform** olacaktır.

Ana hedefler:

- PAdES, XAdES ve CAdES imza oluşturma.
- ASiC-S / ASiC-E desteğini mimari olarak desteklemek ve uygun fazda uygulamak.
- Demo/development ortamında PFX/PKCS#12 ile imzalama.
- Production ortamında USB token / smart card / HSM gibi private key'i dışarı çıkarmayan güvenli imza cihazlarıyla çalışma.
- PKCS#11 tabanlı donanım entegrasyonuna izin veren adapter mimarisi.
- İmzalama işlemlerini asynchronous consumer/producer modeliyle yürütme.
- Gelen payload'ı önce güvenli bir dosyaya kaydetme, sonra RabbitMQ üzerinden işleme.
- Consumer'ın imzalanmış çıktıyı ayrı bir dosya olarak üretmesi.
- PostgreSQL üzerinde request/job/audit metadata saklama.
- Dosya binary'sini varsayılan olarak PostgreSQL'e koymama; object/file storage kullanma.
- İmzalama durumlarının idempotent ve izlenebilir olması.
- REST API üzerinden polling ve ileride webhook desteği.
- React tabanlı yönetim/demo arayüzü.
- Türkçe ve İngilizce teknik dokümantasyon.
- Tamamen İngilizce kod tabanı.
- Unit, integration, contract, cryptographic interoperability ve end-to-end testleri.

## 2. Standart ve Uyumluluk Hedefleri

Platform, güncel ETSI AdES ailesini temel alacaktır:

- PAdES: ETSI EN 319 142-1
- XAdES: ETSI EN 319 132-1
- CAdES: ETSI EN 319 122-1
- ASiC: ETSI EN 319 162-1 / EN 319 162-2
- Signature validation: ETSI EN 319 102-1
- RFC 3161 timestamping
- X.509 / PKI
- CMS / Cryptographic Message Syntax
- XMLDSig
- PKCS#12
- PKCS#11
- OCSP / CRL
- SHA-256 ve güncel güvenli digest algoritmaları
- RSA ve ECDSA, sağlayıcı/donanım desteklediği ölçüde.

2026 itibarıyla AB tarafında yeni teknik referanslar yayımlandığı için standart sürümleri hard-code edilmemeli; implementasyon bir **Standards Compatibility Matrix** ile takip edilmelidir.

### 2.1 İmza seviyeleri

MVP:

- PAdES-B
- XAdES-B
- CAdES-B

Sonraki fazlar:

- PAdES-T / LT / LTA
- XAdES-T / LT / LTA
- CAdES-T / LT / LTA
- ASiC-S
- ASiC-E
- Signature validation/reporting
- Long-term validation
- Timestamp authority integration
- OCSP/CRL evidence embedding.

> B/T/LT/LTA seviyeleri sadece isim olarak expose edilmeyecek; profile'ın gerektirdiği signed/unsigned attributes, timestamp, certificate/revocation evidence ve validation material gerçekten üretilecektir.

## 3. ESYA Benzeri Özellik Kapsamı

ESYA'da görülen aşağıdaki kavramlar ürün tasarımında karşılanacaktır:

### 3.1 Certificate handling

- Certificate discovery
- Certificate metadata extraction
- Certificate chain building
- Trusted root store
- Certificate validity period
- Key usage / extended key usage
- Revocation checking
- OCSP
- CRL
- Validation policy
- Test certificate support
- Production trust store
- Certificate fingerprinting.

### 3.2 Signing devices

Adapter interface:

```text
ISigningProvider
 ├── PfxSigningProvider
 ├── Pkcs11SigningProvider
 ├── SmartCardSigningProvider
 └── HsmSigningProvider
```

Private key hiçbir durumda API katmanına düz metin olarak dönmemelidir.

### 3.3 Signing provider contract

Provider aşağıdaki operasyonları sağlayacaktır:

- List certificates
- Get certificate metadata
- Create digest
- Sign digest
- Validate provider availability
- Health check
- Dispose/release session

Özellikle HSM/token senaryosunda imza oluşturma mümkün olduğunca **digest -> device sign** akışıyla yapılmalıdır.

## 4. Kritik Mimari Kural

API request'i doğrudan RabbitMQ mesajına binary olarak koymayacaktır.

Akış:

```text
Client
  |
  | POST /api/v1/signatures
  v
Signing API
  |
  | 1. Validate request
  | 2. Generate request/job ID
  | 3. Write input file
  | 4. Persist metadata
  | 5. Publish small queue message
  v
RabbitMQ
  |
  v
Signing Worker / Consumer
  |
  | 6. Read input file
  | 7. Resolve signing provider
  | 8. Create signature
  | 9. Write signed file
  | 10. Update PostgreSQL
  v
Completed
  |
  v
Client
  |
  | GET /api/v1/signatures/{id}
  | GET /api/v1/signatures/{id}/content
  | GET /api/v1/signatures/{id}/verification
  v
Signed File
```

RabbitMQ mesajı binary taşımaz.

Örnek:

```json
{
  "jobId": "0198...",
  "tenantId": "tenant-001",
  "signatureId": "0198...",
  "inputPath": "pending/2026/09/05/...",
  "requestedFormat": "PAdES",
  "requestedProfile": "B",
  "createdAt": "2026-09-05T17:00:00Z",
  "attempt": 1
}
```

## 5. Proje Yapısı

Önerilen repository:

```text
/
├── src/
│   ├── OpenSignature.Api/
│   ├── OpenSignature.Application/
│   ├── OpenSignature.Domain/
│   ├── OpenSignature.Infrastructure/
│   ├── OpenSignature.Signing/
│   ├── OpenSignature.Signing.Contracts/
│   ├── OpenSignature.Worker/
│   └── OpenSignature.Web/
│
├── tests/
│   ├── OpenSignature.Domain.Tests/
│   ├── OpenSignature.Application.Tests/
│   ├── OpenSignature.Infrastructure.Tests/
│   ├── OpenSignature.Signing.Tests/
│   ├── OpenSignature.Api.IntegrationTests/
│   ├── OpenSignature.Worker.IntegrationTests/
│   └── OpenSignature.Interop.Tests/
│
├── docs/
│   ├── PRODUCT-SPEC.tr.md
│   ├── PRODUCT-SPEC.en.md
│   ├── TASKS.md
│   ├── ARCHITECTURE.md
│   ├── API.md
│   ├── SECURITY.md
│   ├── SIGNATURE-PROFILES.md
│   ├── DEVICE-INTEGRATION.md
│   ├── TEST-STRATEGY.md
│   └── OPERATIONS.md
│
├── deploy/
│   ├── docker/
│   ├── kubernetes/
│   └── compose/
│
├── samples/
│   ├── certificates/
│   └── requests/
│
├── Directory.Build.props
├── Directory.Packages.props
├── docker-compose.yml
├── README.md
└── LICENSE
```

## 6. Backend

Backend:

- .NET 10
- ASP.NET Core Minimal API
- PostgreSQL
- Entity Framework Core
- RabbitMQ
- Docker
- OpenTelemetry
- Structured logging
- Health checks
- FluentValidation veya eşdeğer validation yaklaşımı
- ProblemDetails
- OpenAPI.

Kod kuralları:

- Kod tamamen İngilizce.
- Public API isimleri İngilizce.
- XML documentation İngilizce.
- Değişkenler İngilizce.
- Log mesajları İngilizce.
- Test isimleri İngilizce.
- Commit mesajları İngilizce.

## 7. Domain Model

### SignatureRequest

```text
Id
TenantId
CorrelationId
Status
Format
Profile
InputFileId
OutputFileId
CertificateId
SigningProvider
CreatedAt
QueuedAt
StartedAt
CompletedAt
FailedAt
RetryCount
ErrorCode
ErrorMessage
CreatedBy
```

### StoredFile

```text
Id
StorageKey
OriginalFileName
ContentType
Size
Sha256
CreatedAt
DeletedAt
```

### Certificate

```text
Id
Thumbprint
Subject
Issuer
SerialNumber
NotBefore
NotAfter
ProviderType
ProviderReference
CreatedAt
```

### SigningJob

```text
Id
SignatureRequestId
Status
Attempt
LastError
LockedUntil
CreatedAt
StartedAt
CompletedAt
```

### AuditEvent

```text
Id
TenantId
EntityType
EntityId
EventType
Actor
Timestamp
CorrelationId
Metadata
```

## 8. Durum Makinesi

```text
Created
  -> Queued
  -> Processing
  -> Completed

Created -> Rejected
Queued -> RetryScheduled
Processing -> RetryScheduled
Processing -> Failed
```

Terminal durumlar:

- Completed
- Failed
- Rejected
- Cancelled

Aynı job iki consumer tarafından başarıyla işlenmemelidir.

## 9. REST API

### Create signature

`POST /api/v1/signatures`

Request:

- multipart/form-data
- file
- format
- profile
- signingProvider
- certificateSelector
- optional signature metadata

Response:

```json
{
  "id": "0198...",
  "status": "Queued",
  "createdAt": "2026-09-05T17:00:00Z",
  "statusUrl": "/api/v1/signatures/0198..."
}
```

### Get status

`GET /api/v1/signatures/{id}`

Response:

```json
{
  "id": "0198...",
  "status": "Completed",
  "format": "PAdES",
  "profile": "B",
  "sha256": "...",
  "createdAt": "...",
  "completedAt": "...",
  "downloadUrl": "/api/v1/signatures/0198.../content"
}
```

### Download signed content

`GET /api/v1/signatures/{id}/content`

Sadece Completed durumda dosya döndürülür.

### Cancel

`POST /api/v1/signatures/{id}/cancel`

Sadece henüz signing operation başlamamış job'larda güvenilir şekilde uygulanmalıdır.

### Certificate endpoints

```text
GET /api/v1/certificates
GET /api/v1/certificates/{id}
GET /api/v1/providers
GET /api/v1/providers/{id}/health
```

### Doğrulama

```text
GET  /api/v1/signatures/{id}/verification
POST /api/v1/verifications
```

`GET /api/v1/signatures/{id}/verification` tamamlanmış bir platform imzasını doğrular ve ayrıntılı rapor döner (genel durum, kriptografik kontrol, sertifika yolu, iptal, neden kodları). Yalnızca `Completed` durumunda kullanılabilir.

`POST /api/v1/verifications` yüklenen imzalı bir belgeyi doğrular (`multipart/form-data`: `file`, `format`, ayrık CAdES için isteğe bağlı `originalFile`). Yüklenen dosya bellekte doğrulanır ve saklanmaz.

Doğrulama Baseline B CAdES, XAdES ve PAdES kriptografik kontrolleri ile sertifika yolu doğrulamasını kapsar. Tam ETSI EN 319 102-1 AdES uygunluk raporu değildir.

## 10. Idempotency

Client aynı isteği iki kere gönderirse duplicate signing oluşmaması için:

`Idempotency-Key` header desteklenecektir.

DB'de:

```text
TenantId + IdempotencyKey
```

unique constraint olacaktır.

## 11. File Storage

MVP:

- Local filesystem storage adapter.

Production:

- S3-compatible object storage adapter.
- Azure Blob adapter.
- S3 adapter.

Interface:

```text
IFileStorage
 ├── SaveAsync
 ├── OpenReadAsync
 ├── ExistsAsync
 ├── DeleteAsync
 └── GetMetadataAsync
```

Dosya isimleri kullanıcı tarafından belirlenmemelidir.

Örnek storage key:

```text
tenants/{tenantId}/signatures/{yyyy}/{MM}/{dd}/{signatureId}/input.bin
tenants/{tenantId}/signatures/{yyyy}/{MM}/{dd}/{signatureId}/signed.bin
```

## 12. RabbitMQ

Exchange:

```text
esign.signature
```

Routing key:

```text
signature.created
```

Queue:

```text
esign.signature.worker
```

Dead-letter:

```text
esign.signature.dlq
```

Mesaj işleme:

1. Deserialize
2. Validate
3. Acquire job lock
4. Verify input exists
5. Sign
6. Persist output
7. Update transaction state
8. ACK

Transient failure:

- retry with bounded retry count
- exponential backoff
- dead-letter after final attempt.

Permanent cryptographic errors:

- no infinite retry.

## 13. Transaction / Queue Consistency

PostgreSQL transaction commit edilmeden RabbitMQ message ACK edilmemelidir.

Publisher reliability için:

- Outbox pattern kullanılacaktır.

Tablo:

```text
OutboxMessage
Id
Type
Payload
OccurredAt
PublishedAt
RetryCount
Error
```

Worker tarafında idempotent processing uygulanacaktır.

## 14. PAdES

PAdES yalnızca PDF üzerinde çalışmalıdır.

MVP:

- PAdES Baseline B
- Existing PDF preservation
- Incremental update
- Signature dictionary
- ByteRange
- CMS signature container
- Signing certificate embedding where required
- Signature appearance optional (uygulandı: PAdES görünür damga, not ve/veya JPEG/PNG).

Sonraki:

- mevcut imza alanı yeniden kullanımı
- multiple signatures
- VRI sözlüğü.

Uygulandı (Faz 8): RFC 3161 imza zaman damgası (T), DSS (sertifika/CRL/OCSP, LT), belge zaman damgası `/SubFilter /ETSI.RFC3161` (LTA). Profiller sessizce B'ye düşürülmez.

## 15. XAdES

MVP:

- XML input
- XMLDSig
- enveloped
- enveloping
- detached
- XAdES Baseline B.

Sonraki:

- multiple signatures
- signature policy.

Uygulandı (Faz 8): T (`SignatureTimeStamp`), LT (`CertificateValues` / `RevocationValues`), LTA (`ArchiveTimeStamp`). Profiller sessizce B'ye düşürülmez.

## 16. CAdES

MVP:

- CMS SignedData
- detached
- attached/encapsulated content
- CAdES Baseline B.

Sonraki:

- signature policy.

Uygulandı (Faz 8): T (imza zaman damgası), LT (sertifika/iptal kanıtı), LTA (arşiv zaman damgası). Profiller sessizce B'ye düşürülmez.

## 17. ASiC

ASiC-S:

- one associated data/signature package (ZIP, uncompressed `mimetype` first, detached CAdES in `META-INF/signature.p7s`).

ASiC-E:

- multiple files and signatures (`ASiCManifest.xml` plus CAdES over the manifest).

Container implementation:

- ZIP-based container
- deterministic metadata rules
- MIME metadata
- signature relationship validation.

T/LT/LTA inner CAdES üzerinde uygulanır.

## 18. Timestamp

Interface:

```text
ITimestampAuthority
{
    GetTimestampAsync(byte[] imprint)
}
```

RFC 3161:

- TSA URL
- nonce
- message imprint
- timestamp token
- certificate validation.

Worker varsayılanı: `Timestamping:Url` yapılandırılana kadar TSA yoktur. Testler, özel anahtarı dışa aktarmayan süreç-içi RFC 3161 TSA kullanabilir.

Timestamp failures must never silently downgrade a requested T/LT/LTA signature to B.

## 19. Certificate Validation

Validation pipeline:

```text
Parse certificate
 -> Check validity
 -> Build chain
 -> Check trust anchor
 -> Key usage
 -> EKU
 -> Revocation
 -> OCSP
 -> CRL fallback
 -> Policy checks
 -> Result
```

Validation result must contain machine-readable reason codes.

## 20. Security

### Private key rules

- PFX passwords must never be committed.
- PFX files must not be stored in git.
- Production secrets must come from secret management.
- HSM private keys must never leave HSM.
- PKCS#11 PINs must not be logged.
- Certificate serials/thumbprints may be logged where appropriate.
- Raw signed documents must not appear in application logs.
- API file uploads must have size limits.
- MIME type must not be trusted.
- Path traversal must be prevented.
- Storage keys must be generated server-side.
- TLS required in production.

### Authentication

MVP:

- API key.

Production-ready design:

- OAuth2/OIDC
- JWT
- tenant isolation
- RBAC.

Roles:

- Administrator
- Signer
- Operator
- Auditor
- Developer.

## 21. Audit

Her signing request must produce auditable events:

```text
SignatureRequested
FileStored
JobQueued
JobStarted
CertificateResolved
SigningProviderSelected
SigningCompleted
OutputStored
SignatureDownloaded
SigningFailed
RetryScheduled
```

Audit records must be append-only from the application perspective.

## 22. React Frontend

Stack:

- React
- TypeScript
- Vite
- TanStack Query
- React Router
- modern component library.

Pages:

```text
/dashboard
/signatures
/signatures/:id
/certificates
/providers
/jobs
/audit
/settings
```

Dashboard:

- queued jobs
- processing jobs
- completed jobs
- failed jobs
- recent signatures
- provider health.

Signature detail:

- input metadata
- output metadata
- status timeline
- certificate
- provider
- hashes
- errors
- download.

## 23. Observability

OpenTelemetry:

- traces
- metrics
- logs.

Metrics:

```text
signature_requests_total
signature_completed_total
signature_failed_total
signature_duration_seconds
signature_queue_wait_seconds
rabbitmq_messages_total
rabbitmq_retry_total
provider_sign_operations_total
provider_sign_failures_total
storage_operations_total
```

Every request/job gets:

- correlation ID
- trace ID
- job ID
- signature ID.

## 24. Testing

Minimum:

### Unit

- Domain state machine
- validators
- hash calculation
- idempotency
- storage key generation
- provider selection.

### Integration

- PostgreSQL
- RabbitMQ
- filesystem/object storage
- API
- worker.

Testcontainers tercih edilecektir.

### Cryptographic interoperability

Generated signatures must be validated using an independent implementation/library where possible.

Test matrix:

| Format | Profile | Input | Expected |
|---|---|---|---|
| PAdES | B | PDF | valid |
| XAdES | B | XML | valid |
| CAdES | B | binary | valid |
| PAdES | T | PDF | valid |
| XAdES | T | XML | valid |
| CAdES | T | binary | valid |

T/LT/LTA tests require a real or controlled TSA/OCSP/CRL environment.

## 25. Performance

Initial targets:

- API upload should return quickly after durable persistence + queue publication.
- API must not wait for signing completion.
- Worker concurrency configurable.
- Device concurrency configurable.
- HSM/token sessions pooled where supported.
- Large files streamed whenever practical.
- RabbitMQ message size kept small.

Benchmark scenarios:

- 1 MB PDF
- 10 MB PDF
- 100 MB PDF
- 1,000 concurrent requests
- 1,000 queued jobs
- single HSM slot
- multiple signing workers.

## 26. Reliability

Requirements:

- no lost job after API confirms acceptance
- no duplicate successful signature from same idempotency key
- retryable errors retried
- permanent errors dead-lettered
- incomplete output never exposed as completed
- storage failure prevents Completed state
- DB failure after signing must be recoverable.

## 27. Developer Workflow

Development MUST be task-driven.

`docs/TASKS.md` is the source of truth for implementation progress.

Each task contains:

- ID
- title
- priority
- dependencies
- scope
- acceptance criteria
- test requirements
- status.

Allowed statuses:

```text
TODO
IN_PROGRESS
BLOCKED
DONE
```

Task is `DONE` only when:

1. Implementation complete.
2. Tests written.
3. Tests pass.
4. Relevant documentation updated.
5. No known blocker remains.
6. Commit created.

## 28. Commit Policy

Commits must be small and meaningful.

Use Conventional Commits:

```text
feat(api): add asynchronous signature request endpoint
feat(signing): add PAdES baseline B signer
feat(queue): add RabbitMQ signing worker
feat(storage): add local file storage adapter
test(signing): add PAdES interoperability tests
fix(worker): prevent duplicate job processing
docs(api): document signature endpoints
refactor(signing): extract signing provider abstraction
```

Never use:

```text
update
changes
fix stuff
work
final
test
```

After important tasks pass:

```text
git status
git diff
dotnet test
git add ...
git commit -m "..."
```

Push only when explicitly requested.

## 29. Definition of Done

A release candidate requires:

- API documented.
- Worker documented.
- Queue topology documented.
- Storage documented.
- PAdES/XAdES/CAdES tested.
- PFX demo provider tested.
- Provider abstraction tested.
- PostgreSQL migrations applied.
- RabbitMQ retry/DLQ tested.
- Idempotency tested.
- Security tests passed.
- Audit events verified.
- React UI usable.
- Docker Compose environment reproducible.
- CI green.
- docs/TR and docs/EN synchronized.

## 30. Explicit Non-Goals

MVP does NOT claim:

- qualified electronic signature status by itself,
- trust service provider status,
- legal certification,
- HSM certification,
- automatic compliance with Turkish legislation,
- production suitability without security review.

A PFX private key is a development/demo mechanism. Qualified signing requires an appropriate qualified certificate and qualified signature creation device/environment.

## 31. Architecture Decision Rules

1. Do not couple domain logic to RabbitMQ.
2. Do not couple signing logic to PFX.
3. Do not couple signing logic to a specific HSM.
4. Do not put private keys into PostgreSQL.
5. Do not put document binaries into RabbitMQ.
6. Do not expose incomplete output.
7. Do not silently downgrade requested signature profiles.
8. Do not mark tasks done before tests pass.
9. Do not create huge commits.
10. Do not mix Turkish identifiers into code.

## 32. First Milestone

Milestone 1:

```text
.NET 10 solution
PostgreSQL
RabbitMQ
Minimal API
Worker
Local file storage
PFX signing provider
PAdES-B
XAdES-B
CAdES-B
React UI
Docker Compose
Integration tests
CI
```

Only after Milestone 1 is stable should advanced profiles, HSM/PKCS#11 and long-term validation be implemented.
