# Başlangıç

OpenSignature’ı Docker Compose ile tam yığın olarak veya yalnızca altyapı container’ları + host’ta Api / Worker / Web ile çalıştırın.

## Önkoşullar

- Docker Desktop (veya uyumlu motor)
- Host geliştirmesi için: .NET 10 SDK, Node.js 20+, Windows’ta PowerShell 7+

## 1. Docker Compose ile tam yığın

```bash
cp .env.example .env
docker compose up -d --build
docker compose ps
```

| Servis | Uç nokta |
| --- | --- |
| Web | http://localhost:5173 |
| API | http://localhost:5270 |
| API health | http://localhost:5270/health |
| PostgreSQL | `localhost:5432` (`esign` / `esign` / `opensignature`) |
| RabbitMQ AMQP | `localhost:5672` |
| RabbitMQ UI | http://localhost:15672 |

`pfx-init` adlı birim volume’da geçici bir geliştirme PFX’i üretir. `.env` içinde `PFX_PASSWORD` (varsayılan `opensignature-dev`). İsteğe bağlı TSA: `TIMESTAMPING_URL` / `TIMESTAMPING_USERNAME` / `TIMESTAMPING_PASSWORD`.

Durdurma (volume’lar kalır):

```bash
docker compose down
```

USB / akıllı kart imzalama container içinde yoktur — PKCS#11 için aşağıdaki host geliştirme yolunu kullanın.

## 2. Tek komutla host geliştirme

Yalnızca altyapı, ardından host’ta Api + Worker + Vite:

```bash
docker compose up -d postgres rabbitmq
pwsh ./scripts/Start-Development.ps1
```

| Uygulama | URL |
| --- | --- |
| Web | http://localhost:5173 |
| API | http://localhost:5270 |

İsteğe bağlı bayraklar: `-SkipInfrastructure`, `-SkipCertificate`, `-ApiProfile https`.

Host’ta `5432` doluysa `.env` içinde `POSTGRES_PORT=5433` ayarlayın ve Api/Worker Development bağlantı dizisini eşleştirin.

## 3. Elle POC yolu

```bash
docker compose up -d postgres rabbitmq
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

http://127.0.0.1:8000 — dil seçici ile English.

## Sonraki

- [Mimari](architecture.md)
- [Akışlar](flows.md)
- [Operasyon](operations.md)
