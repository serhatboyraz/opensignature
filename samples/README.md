# Samples

Sample documents and public test material live here.

## Development signing certificate

Never place PFX files containing private keys, passwords or production certificates in this repository.

For local API/Worker POC, generate an ephemeral development certificate:

```powershell
pwsh ./scripts/Generate-DevCertificate.ps1
```

This writes `data/certs/dev.pfx` (gitignored) and prints commands to set the password via user-secrets or environment variables:

```powershell
dotnet user-secrets set "Signing:Pfx:Password" "opensignature-dev" --project src/OpenSignature.Api
dotnet user-secrets set "Signing:Pfx:Password" "opensignature-dev" --project src/OpenSignature.Worker
```

`appsettings.Development.json` expects `Signing:Pfx:Path` = `./data/certs/dev.pfx`.
