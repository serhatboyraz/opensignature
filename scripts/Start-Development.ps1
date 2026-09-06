# Starts OpenSignature local development: infrastructure, API, worker, and web UI.
# Usage (from repository root):
#   pwsh ./scripts/Start-Development.ps1
# Press Ctrl+C in this terminal to stop the API, worker, and web processes.
# Docker Compose dependencies are left running (use `docker compose down` to stop them).

[CmdletBinding()]
param(
    [switch]$SkipInfrastructure,
    [switch]$SkipCertificate,
    [ValidateSet("http", "https")]
    [string]$ApiProfile = "http",
    [string]$PfxPassword = "opensignature-dev"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
Set-Location -LiteralPath $repoRoot

$script:childApps = New-Object System.Collections.Generic.List[object]
$script:stopping = $false
$script:onWindows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-Command {
    param(
        [string]$Name,
        [string]$Hint
    )
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name was not found on PATH. $Hint"
    }
}

function Import-DotEnv {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    Get-Content -LiteralPath $Path | ForEach-Object {
        $line = $_.Trim()
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#")) {
            return
        }

        $separator = $line.IndexOf("=")
        if ($separator -lt 1) {
            return
        }

        $key = $line.Substring(0, $separator).Trim()
        $value = $line.Substring($separator + 1).Trim().Trim("'").Trim('"')
        if (-not [string]::IsNullOrWhiteSpace($key) -and -not (Test-Path -LiteralPath "Env:$key")) {
            Set-Item -LiteralPath "Env:$key" -Value $value
        }
    }
}

function Stop-ProcessTree {
    param([int]$ProcessId)

    if ($ProcessId -le 8) {
        return
    }

    if ($script:onWindows) {
        & taskkill.exe /PID $ProcessId /T /F 2>$null | Out-Null
        return
    }

    try {
        $children = Get-CimInstance Win32_Process -Filter "ParentProcessId=$ProcessId" -ErrorAction SilentlyContinue
        foreach ($child in $children) {
            Stop-ProcessTree -ProcessId ([int]$child.ProcessId)
        }
    }
    catch {
        # Best-effort cleanup; continue stopping the parent.
    }

    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
}

function Get-PidsListeningOnPort {
    param([int]$Port)

    $pids = New-Object "System.Collections.Generic.HashSet[int]"
    foreach ($line in (& netstat.exe -ano)) {
        if ($line -notmatch "LISTENING") {
            continue
        }

        if ($line -match ":$Port\s+\S+\s+LISTENING\s+(\d+)\s*$") {
            [void]$pids.Add([int]$Matches[1])
        }
    }

    return @($pids)
}

function Get-DevAppPids {
    param([string[]]$NameFragments)

    $pids = New-Object "System.Collections.Generic.HashSet[int]"
    $processes = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue
    foreach ($process in $processes) {
        if ($process.ProcessId -eq $PID) {
            continue
        }

        $commandLine = $process.CommandLine
        if ([string]::IsNullOrWhiteSpace($commandLine)) {
            continue
        }

        foreach ($fragment in $NameFragments) {
            if ($commandLine -like "*${fragment}*") {
                [void]$pids.Add([int]$process.ProcessId)
                break
            }
        }
    }

    return @($pids)
}

function Stop-ExistingDevApps {
    param([int[]]$Ports)

    $toStop = New-Object "System.Collections.Generic.HashSet[int]"
    foreach ($port in $Ports) {
        foreach ($processId in (Get-PidsListeningOnPort -Port $port)) {
            if ($processId -gt 8) {
                [void]$toStop.Add($processId)
            }
        }
    }

    foreach ($processId in (Get-DevAppPids -NameFragments @("OpenSignature.Api", "OpenSignature.Worker"))) {
        if ($processId -gt 8) {
            [void]$toStop.Add($processId)
        }
    }

    if ($toStop.Count -eq 0) {
        return
    }

    Write-Step "Stopping leftover API, worker, or web processes on development ports"
    foreach ($processId in $toStop) {
        Write-Host "  stopping PID $processId"
        Stop-ProcessTree -ProcessId $processId
    }

    Start-Sleep -Seconds 2
}

function Stop-DevelopmentApps {
    if ($script:stopping) {
        return
    }

    $script:stopping = $true
    Write-Host ""
    Write-Step "Stopping OpenSignature API, worker, and web"

    foreach ($app in $script:childApps.ToArray()) {
        if ($null -eq $app -or $null -eq $app.ChildProcess) {
            continue
        }

        try {
            if (-not $app.ChildProcess.HasExited) {
                Stop-ProcessTree -ProcessId $app.ChildProcess.Id
            }
        }
        catch {
            # Ignore processes that already exited.
        }
    }

    $script:childApps.Clear()
}

function Assert-ChildAppsStillRunning {
    foreach ($app in $script:childApps.ToArray()) {
        $child = $app.ChildProcess
        $child.Refresh()
        if ($child.HasExited) {
            $code = $child.ExitCode
            throw "$($app.Title) exited unexpectedly with code $code. That window was kept open so you can read the log."
        }
    }
}

function Start-DevApp {
    param(
        [string]$Title,
        [string]$Command,
        [string]$WorkingDirectory = $repoRoot
    )

    $hostPath = (Get-Process -Id $PID).Path
    if ([string]::IsNullOrWhiteSpace($hostPath)) {
        $hostPath = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
        if ([string]::IsNullOrWhiteSpace($hostPath)) {
            $hostPath = (Get-Command powershell -ErrorAction Stop).Source
        }
    }

    $escapedDirectory = $WorkingDirectory.Replace("'", "''")
    $script = @"
Set-Location -LiteralPath '$escapedDirectory'
`$Host.UI.RawUI.WindowTitle = '$Title'
$Command
`$exitCode = `$LASTEXITCODE
if (`$null -ne `$exitCode -and `$exitCode -ne 0) {
    Write-Host ""
    Write-Host ('{0} exited with code {1}. This window was kept open so you can read the log above.' -f '$Title', `$exitCode) -ForegroundColor Red
    Read-Host 'Press Enter to close this window'
}
exit `$exitCode
"@

    $process = Start-Process -FilePath $hostPath -ArgumentList @("-NoProfile", "-Command", $script) -PassThru
    if ($null -eq $process) {
        throw "Failed to start $Title."
    }

    $script:childApps.Add([pscustomobject]@{
            Title        = $Title
            ChildProcess = $process
        })
    Write-Host "  started $Title (PID $($process.Id))"
    return $process
}

function Wait-HttpReady {
    param(
        [string]$Name,
        [string]$Url,
        [int]$TimeoutSeconds = 90
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    Write-Host "  waiting for $Name at $Url"
    while ([DateTime]::UtcNow -lt $deadline) {
        Assert-ChildAppsStillRunning
        try {
            $response = Invoke-WebRequest -Uri $Url -Method GET -UseBasicParsing -TimeoutSec 3
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                Write-Host "  $Name is ready"
                return
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    throw "$Name did not become ready at $Url within ${TimeoutSeconds}s."
}

try {
    Write-Host "OpenSignature development start" -ForegroundColor Green
    Write-Host "Repository: $repoRoot"

    Assert-Command -Name "dotnet" -Hint "Install the .NET 10 SDK."
    Assert-Command -Name "node" -Hint "Install Node.js 20+."
    Assert-Command -Name "npm" -Hint "Install Node.js 20+ (includes npm)."

    $envPath = Join-Path $repoRoot ".env"
    $envExamplePath = Join-Path $repoRoot ".env.example"
    if (-not (Test-Path -LiteralPath $envPath) -and (Test-Path -LiteralPath $envExamplePath)) {
        Copy-Item -LiteralPath $envExamplePath -Destination $envPath
        Write-Host "Created .env from .env.example"
    }

    Import-DotEnv -Path $envPath

    if (-not $SkipInfrastructure) {
        Assert-Command -Name "docker" -Hint "Install Docker Desktop and ensure it is running."
        Write-Step "Starting PostgreSQL and RabbitMQ (docker compose)"
        & docker compose up -d --wait
        if ($LASTEXITCODE -ne 0) {
            throw "docker compose up failed with exit code $LASTEXITCODE"
        }
    }

    $pfxPath = Join-Path $repoRoot "data" "certs" "dev.pfx"
    if (-not $SkipCertificate -and -not (Test-Path -LiteralPath $pfxPath)) {
        Write-Step "Generating development signing certificate"
        & (Join-Path $PSScriptRoot "Generate-DevCertificate.ps1") -OutputPath $pfxPath -Password $PfxPassword
    }

    if ([string]::IsNullOrWhiteSpace($env:Signing__Pfx__Password)) {
        $env:Signing__Pfx__Password = $PfxPassword
    }

    $webDir = Join-Path $repoRoot "src" "OpenSignature.Web"
    if (-not (Test-Path -LiteralPath (Join-Path $webDir "node_modules"))) {
        Write-Step "Installing web dependencies"
        Push-Location -LiteralPath $webDir
        try {
            & npm install
            if ($LASTEXITCODE -ne 0) {
                throw "npm install failed with exit code $LASTEXITCODE"
            }
        }
        finally {
            Pop-Location
        }
    }

    $apiUrl = if ($ApiProfile -eq "https") { "https://localhost:7010" } else { "http://localhost:5270" }
    $webUrl = "http://localhost:5173"
    $apiPort = if ($ApiProfile -eq "https") { 7010 } else { 5270 }

    $apiProject = Join-Path $repoRoot "src" "OpenSignature.Api"
    $workerProject = Join-Path $repoRoot "src" "OpenSignature.Worker"

    Stop-ExistingDevApps -Ports @($apiPort, 5173)

    Write-Step "Building OpenSignature.Api and OpenSignature.Worker"
    & dotnet build $apiProject
    if ($LASTEXITCODE -ne 0) {
        throw "OpenSignature.Api build failed with exit code $LASTEXITCODE. See the errors above."
    }

    & dotnet build $workerProject
    if ($LASTEXITCODE -ne 0) {
        throw "OpenSignature.Worker build failed with exit code $LASTEXITCODE. See the errors above."
    }

    Write-Step "Starting OpenSignature.Api, OpenSignature.Worker, and OpenSignature.Web"

    $null = Start-DevApp -Title "OpenSignature.Api" -Command "dotnet run --no-build --project `"$apiProject`" --launch-profile $ApiProfile"
    $null = Start-DevApp -Title "OpenSignature.Worker" -Command "dotnet run --no-build --project `"$workerProject`""
    $null = Start-DevApp -Title "OpenSignature.Web" -Command "npm run dev" -WorkingDirectory $webDir

    Wait-HttpReady -Name "API" -Url "$apiUrl/health"
    Wait-HttpReady -Name "Web" -Url $webUrl

    Write-Host ""
    Write-Host "OpenSignature development apps are running:" -ForegroundColor Green
    Write-Host "  Web    $webUrl"
    Write-Host "  API    $apiUrl"
    Write-Host "  Worker OpenSignature.Worker (separate window)"
    Write-Host ""
    Write-Host "Press Ctrl+C in this terminal to stop the API, worker, and web apps."
    Write-Host "Infrastructure containers stay up. Stop them with: docker compose down"

    while (-not $script:stopping) {
        Assert-ChildAppsStillRunning
        Start-Sleep -Seconds 1
    }
}
catch {
    if (-not $script:stopping) {
        Write-Host ""
        Write-Host $_ -ForegroundColor Red
        exit 1
    }
}
finally {
    Stop-DevelopmentApps
}
