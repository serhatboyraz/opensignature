# Generates an ephemeral development PKCS#12 (.pfx) for OpenSignature local POC.
# Output is written under ./data/certs/ (gitignored). Never commit the PFX or password.

[CmdletBinding()]
param(
    [string]$OutputPath = "",
    [string]$Password = "opensignature-dev",
    [string]$Subject = "CN=OpenSignature Dev"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot ".." "data" "certs" "dev.pfx"
}

$resolved = [System.IO.Path]::GetFullPath($OutputPath)
$directory = Split-Path -Parent $resolved
if (-not (Test-Path -LiteralPath $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

Write-Host "Generating development certificate:"
Write-Host "  Subject : $Subject"
Write-Host "  Path    : $resolved"

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("opensignature-cert-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
try {
    $csproj = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
'@
    $program = @'
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

var password = args[0];
var path = args[1];
var subject = args[2];

using var rsa = RSA.Create(2048);
var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
request.CertificateExtensions.Add(
    new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));
using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
var bytes = certificate.Export(X509ContentType.Pkcs12, password);
File.WriteAllBytes(path, bytes);
Console.WriteLine($"Wrote {bytes.Length} bytes to {path}");
'@
    Set-Content -LiteralPath (Join-Path $tempDir "Gen.csproj") -Value $csproj -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $tempDir "Program.cs") -Value $program -Encoding UTF8
    & dotnet run --project (Join-Path $tempDir "Gen.csproj") -- $Password $resolved $Subject
    if ($LASTEXITCODE -ne 0) {
        throw "Certificate generation failed with exit code $LASTEXITCODE"
    }
}
finally {
    Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Configure the PFX password via user-secrets (do not commit passwords):"
Write-Host "  dotnet user-secrets set `"Signing:Pfx:Password`" `"$Password`" --project src/OpenSignature.Api"
Write-Host "  dotnet user-secrets set `"Signing:Pfx:Password`" `"$Password`" --project src/OpenSignature.Worker"
Write-Host ""
Write-Host "Or set environment variable Signing__Pfx__Password=$Password"
Write-Host "Ensure appsettings Signing:Pfx:Path points at: $resolved"
