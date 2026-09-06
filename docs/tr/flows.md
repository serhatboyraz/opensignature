# Akışlar ve diyagramlar

OpenSignature istek işleme, durum makineleri, outbox güvenilirliği ve sağlayıcı imzalama için görsel referans.

![Asenkron akış SVG](../assets/images/async-flow-diagram.svg){ .architecture-hero }

## Uçtan uca imza sırası

```mermaid
sequenceDiagram
  autonumber
  actor Client as İstemci
  participant Api as OpenSignature.Api
  participant DB as PostgreSQL
  participant FS as Dosya depolama
  participant OB as Outbox yayıncı
  participant MQ as RabbitMQ
  participant Wrk as Worker
  participant Prov as ISigningProvider

  Client->>Api: POST /api/v1/signatures (+ Idempotency-Key)
  Api->>Api: Multipart / format / profil / sağlayıcı doğrula
  Api->>FS: input.bin kaydet (sunucu anahtarı)
  Api->>DB: SignatureRequest + SigningJob + OutboxMessage (1 TX)
  Api-->>Client: 202 Accepted { id, statusUrl }

  OB->>DB: Yayımlanmamış outbox tara
  OB->>MQ: SigningJobMessage yayımla (yalnızca referans)
  OB->>DB: PublishedAt işaretle

  MQ->>Wrk: Mesaj teslim et
  Wrk->>DB: Idempotent iş kilidi al
  Wrk->>FS: input.bin aç
  Wrk->>Prov: Sağlayıcı çöz · SignDigest
  Prov-->>Wrk: İmza baytları / CMS / XML / PDF
  Wrk->>FS: signed.bin yaz
  Wrk->>DB: Completed + denetim
  Wrk->>MQ: ACK

  Client->>Api: GET /api/v1/signatures/{id}
  Api-->>Client: status Completed
  Client->>Api: GET /api/v1/signatures/{id}/content
  Api->>FS: signed.bin akışı
  Api-->>Client: İmzalı belge
```

## İmza isteği durum makinesi

```mermaid
stateDiagram-v2
  [*] --> Created
  Created --> Queued: kabul
  Created --> Rejected: doğrulama / politika
  Queued --> Processing: işçi kilidi
  Queued --> RetryScheduled: geçici erteleme
  Processing --> Completed: imzalandı + saklandı
  Processing --> Failed: kalıcı hata
  Processing --> RetryScheduled: geçici hata
  RetryScheduled --> Queued: yeniden yayın
  Queued --> Cancelled: başlamadan iptal
  Created --> Cancelled: başlamadan iptal

  Completed --> [*]
  Failed --> [*]
  Rejected --> [*]
  Cancelled --> [*]
```

Terminal durumlar: **Completed**, **Failed**, **Rejected**, **Cancelled**.

## İşçi kilidi ve idempotency

```mermaid
flowchart TD
  A[RabbitMQ mesajını tüket] --> B{İstek/iş terminal mi?}
  B -->|Evet Completed/Cancelled/Rejected| ACK1[İmzasız ACK]
  B -->|Hayır| C{Kilit dene}
  C -->|Kaybetti / tutuluyor| ACK2[İmzasız ACK]
  C -->|Kilitlendi| D[Girdi yükle · imzala · sakla]
  D --> E{Başarılı?}
  E -->|Evet| F[Completed kaydet · ACK]
  E -->|Geçici| G[Kilidi bırak · RetryScheduled · sınırlı yeniden deneme]
  E -->|Kalıcı| H[Failed · DLQ · ACK]
```

Kurallar:

- En az bir kez teslimat, yinelenen başarılı imza üretmemelidir.
- Kilit süresi beklenen imza süresinden uzun olmalıdır (varsayılan 15 dakika).
- Çökme sonrası süresi dolmuş `Locked`/`Processing` geri alınabilir; aktif kilitler çalınmaz.

## Outbox güvenilirliği

```mermaid
flowchart LR
  subgraph same_tx [Aynı DB işlemi]
    R[SignatureRequest]
    J[SigningJob]
    O[OutboxMessage]
  end
  same_tx --> Pub[Outbox yayıncı]
  Pub -->|başarı| MQ[[RabbitMQ]]
  Pub -->|işaret| O2[PublishedAt]
```

PostgreSQL ile broker arasında çift yazım kaybını önler.

## Sağlayıcı özet-imza akışı

```mermaid
sequenceDiagram
  participant Wrk as Worker / Orchestrator
  participant Eng as Biçim imzalayıcı
  participant Prov as Donanım sağlayıcı
  participant Dev as Token / HSM

  Wrk->>Eng: İmza oluştur (biçim + profil)
  Eng->>Eng: Özet / ToBeSigned oluştur
  Eng->>Prov: SignDigestAsync(digest)
  Prov->>Dev: C_Sign / cihaz işlemi
  Dev-->>Prov: imza değeri
  Prov-->>Eng: imza (anahtar dışa aktarılmaz)
  Eng->>Eng: PAdES/XAdES/CAdES/ASiC içine yerleştir
  Eng-->>Wrk: imzalı çıktı baytları
```

## Doğrulama akışı

```mermaid
flowchart TD
  A[GET .../verification veya POST /verifications] --> B[ISignatureValidator]
  B --> C[Kripto kontrolü CAdES/XAdES/PAdES/ASiC]
  B --> D[ICertificateValidator]
  D --> E[geçerlilik · zincir · KU/EKU · iptal · politika]
  B --> F[IValidationReportBuilder]
  F --> G[JSON rapor istemci / UI]
```

## Yeniden deneme ve DLQ

| Hata sınıfı | Davranış |
| --- | --- |
| Geçici (ağ, TSA zaman aşımı, kilit yarışı) | Üstel geri çekilme; sınırlı `MaxAttempts` |
| Kalıcı (bozuk yük, kripto hatası, okunamayan PDF) | Hemen DLQ; sonsuz yeniden deneme yok |

DLQ: `esign.signature.dlq` ← `esign.signature.dlx`.


