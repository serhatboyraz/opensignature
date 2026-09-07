# OpenSignature

**OpenSignature**, kurumsal PAdES, XAdES, CAdES ve ASiC iş akışları için bağımsız bir .NET dijital imza platformudur. İstemciler belgeleri REST üzerinden gönderir; imzalama işçi süreçlerinde asenkron çalışır. PostgreSQL meta veriyi saklar; dosya depolama ikili dosyaları tutar; RabbitMQ yalnızca iş referanslarını taşır.

![OpenSignature sistem mimarisi](../assets/images/architecture-diagram.svg){ .architecture-hero }
<p class="flow-caption">Sistem genel bakışı — API, depolama, kuyruk, işçi ve imza sağlayıcıları.</p>

## Mimari ilkeler

| İlke | Uygulama |
| --- | --- |
| Engellemesiz API | Doğrula → kaydet → outbox → `202 Accepted`. API sürecinde kriptografi yok. |
| Güvenli taşıma | RabbitMQ mesajları küçük JSON iş referanslarıdır — belge ikilileri asla yok. |
| Anahtar hijyeni | Donanım sağlayıcıları özeti cihazda imzalar; özel anahtarlar dışa aktarılmaz. |
| Kiracı izolasyonu | Depolama anahtarları, idempotency ve sorgular `TenantId` ile sınırlıdır. |
| Gözlemlenebilirlik | Her adımda correlation / trace / signature / job kimlikleri. |

## Hızlı bağlantılar

- [Başlangıç](getting-started.md) — yerel Docker, API, işçi, arayüz
- [Mimari](architecture.md) — bileşenler ve bağımlılık kuralları
- [Akışlar ve diyagramlar](flows.md) — sıra ve durum diyagramları
- [REST API](api.md) — uç noktalar ve hata kodları
- [Ürün spesifikasyonu](product-spec.md) — mühendislik kaynağı

## Asenkron imzalama özeti

![Asenkron imza akışı](../assets/images/async-flow-diagram.svg){ .architecture-hero }

```mermaid
flowchart LR
  C[İstemci] -->|POST /signatures| A[Api]
  A -->|input.bin + meta veri| S[(Depolama + PostgreSQL)]
  A -->|Outbox| Q[[RabbitMQ]]
  Q --> W[Worker]
  W -->|SignDigest| P[ISigningProvider]
  W -->|signed.bin| S
  C -->|GET durum / içerik| A
```

## Dil

Bu site **English** ve **Türkçe** olarak sunulur (üst çubuktaki dil seçici). Kaynak kodu, tanımlayıcılar, loglar, testler ve commit mesajları yalnızca İngilizcedir.

## Lisans

OpenSignature [MIT Lisansı](https://github.com/serhatboyraz/opensignature/blob/main/LICENSE) altındadır.
