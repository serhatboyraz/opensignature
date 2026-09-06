# OpenSignature Worker image (background signing consumer)
# Build from repository root:
#   docker build -f docker/worker/Dockerfile -t opensignature-worker .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/OpenSignature.Domain/ src/OpenSignature.Domain/
COPY src/OpenSignature.Signing.Contracts/ src/OpenSignature.Signing.Contracts/
COPY src/OpenSignature.Application/ src/OpenSignature.Application/
COPY src/OpenSignature.Infrastructure/ src/OpenSignature.Infrastructure/
COPY src/OpenSignature.Signing/ src/OpenSignature.Signing/
COPY src/OpenSignature.Validation/ src/OpenSignature.Validation/
COPY src/OpenSignature.Worker/ src/OpenSignature.Worker/

RUN dotnet restore src/OpenSignature.Worker/OpenSignature.Worker.csproj \
 && dotnet publish src/OpenSignature.Worker/OpenSignature.Worker.csproj \
      -c Release \
      -o /app/publish \
      --no-restore \
      /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Docker \
    DOTNET_ENVIRONMENT=Docker \
    DOTNET_EnableDiagnostics=0

RUN mkdir -p /data/storage /data/certs \
 && useradd --uid 10001 --create-home --home-dir /home/appuser --shell /usr/sbin/nologin appuser \
 && chown -R appuser:appuser /app /data

COPY --from=build /app/publish .
COPY docker/scripts/dotnet-entrypoint.sh /dotnet-entrypoint.sh
RUN chmod +x /dotnet-entrypoint.sh

ENTRYPOINT ["/dotnet-entrypoint.sh", "dotnet", "OpenSignature.Worker.dll"]
