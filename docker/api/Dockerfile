# OpenSignature API image (ASP.NET Core Minimal API)
# Build from repository root:
#   docker build -f docker/api/Dockerfile -t opensignature-api .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/OpenSignature.Domain/ src/OpenSignature.Domain/
COPY src/OpenSignature.Signing.Contracts/ src/OpenSignature.Signing.Contracts/
COPY src/OpenSignature.Application/ src/OpenSignature.Application/
COPY src/OpenSignature.Infrastructure/ src/OpenSignature.Infrastructure/
COPY src/OpenSignature.Signing/ src/OpenSignature.Signing/
COPY src/OpenSignature.Validation/ src/OpenSignature.Validation/
COPY src/OpenSignature.Api/ src/OpenSignature.Api/

RUN dotnet restore src/OpenSignature.Api/OpenSignature.Api.csproj \
 && dotnet publish src/OpenSignature.Api/OpenSignature.Api.csproj \
      -c Release \
      -o /app/publish \
      --no-restore \
      /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Docker \
    DOTNET_EnableDiagnostics=0

RUN apt-get update \
 && apt-get install -y --no-install-recommends wget \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /data/storage /data/certs \
 && useradd --uid 10001 --create-home --home-dir /home/appuser --shell /usr/sbin/nologin appuser \
 && chown -R appuser:appuser /app /data

COPY --from=build /app/publish .
COPY docker/scripts/dotnet-entrypoint.sh /dotnet-entrypoint.sh
RUN chmod +x /dotnet-entrypoint.sh

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=40s --retries=5 \
  CMD wget -qO- http://127.0.0.1:8080/health >/dev/null || exit 1

ENTRYPOINT ["/dotnet-entrypoint.sh", "dotnet", "OpenSignature.Api.dll"]
