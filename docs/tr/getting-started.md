# Başlangıç

OpenSignature’ı yerel olarak Docker (PostgreSQL + RabbitMQ), ASP.NET Core API, imza işçisi ve React arayüzü ile çalıştırın.

## Önkoşullar

- .NET 10 SDK
- Node.js 20+
- Docker Desktop (veya uyumlu motor)
- Windows’ta PowerShell 7+ önerilir

## 1. Ortam

```bash
cp .env.example .env
docker compose up -d
docker compose ps
```

Varsayılanlar:

| Servis | Uç nokta |
| --- | --- |
| PostgreSQL | `localhost:5432` (`esign` / `esign` / `opensignature`) |
| RabbitMQ AMQP | `localhost:5672` |
| RabbitMQ UI | http://localhost:15672 |

Host’ta `5432` doluysa `.env` içinde `POSTGRES_PORT=5433` ayarlayın ve Api/Worker Development bağlantı dizisini eşleştirin.

## 2. Tek komutla geliştirme

```bash
pwsh ./scripts/Start-Development.ps1
```

| Uygulama | URL |
| --- | --- |
| Web | http://localhost:5173 |
| API | http://localhost:5270 |

İsteğe bağlı bayraklar: `-SkipInfrastructure`, `-SkipCertificate`, `-ApiProfile https`.

## 3. Elle POC yolu

```bash
docker compose up -d
pwsh ./scripts/Generate-DevCertificate.ps1

dotnet build OpenSignature.slnx
dotnet run --project src/OpenSignature.Api --launch-profile http
dotnet run --project src/OpenSignature.Worker
```

Baseline-B CAdES imzası oluşturma:

```bash
curl -s -X POST "http://localhost:5270/api/v1/signatures" \
  -H "X-Tenant-Id: tenant-demo" \
  -H "Idempotency-Key: demo-1" \
  -F "file=@samples/poc.txt;type=text/plain" \
  -F "format=CAdES" \
  -F "profile=B" \
  -F "signingProvider=Pfx"
```

Durum sorgulama ve indirme:

```bash
curl -s "http://localhost:5270/api/v1/signatures/{id}" -H "X-Tenant-Id: tenant-demo"
curl -s -o signed.bin "http://localhost:5270/api/v1/signatures/{id}/content" -H "X-Tenant-Id: tenant-demo"
```

## 4. Testler

```bash
dotnet test OpenSignature.slnx
```

## 5. Dokümantasyon sitesi

```bash
pip install -r requirements-docs.txt
mkdocs serve
```

http://127.0.0.1:8000 adresini açın — dil seçiciden English’e geçebilirsiniz.

## Sonraki adımlar

- [Mimari](architecture.md)
- [Akışlar ve diyagramlar](flows.md)
- [Operasyon](operations.md)
