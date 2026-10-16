<#
.SYNOPSIS
    Boots the published app and verifies that it serves the web layer.

.DESCRIPTION
    Proves the port came up end to end on a machine: the loopback host is listening,
    the web app document is served, and an /api/* route answers through the API
    pipeline. Runs the same way locally and in CI.

    The app under test is launched normally (a window will appear briefly) and is
    stopped again when the checks finish.

.PARAMETER Exe
    Path to InStSanity.exe (the published, unpackaged build).

.PARAMETER Port
    Port the host is expected to listen on. Defaults to 8000, the port the web layer
    prefers; if it was busy the app serves on the next free port and this check fails
    fast with a clear message.

.PARAMETER TimeoutSeconds
    How long to wait for the host to answer /__health.

.EXAMPLE
    pwsh -File tools/smoke-test.ps1 -Exe artifacts/InStSanity-win-x64/InStSanity.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Exe,

    [int] $Port = 8000,

    [int] $TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
$logPath = 'smoke-test.log'

function Write-Step {
    param([string] $Message, [string] $Colour = 'Cyan')
    Write-Host "==> $Message" -ForegroundColor $Colour
    Add-Content -Path $logPath -Value "==> $Message"
}

function Write-Detail {
    param([string] $Message)
    Write-Host "    $Message"
    Add-Content -Path $logPath -Value "    $Message"
}

Set-Content -Path $logPath -Value "smoke test $(Get-Date -Format o)"

if (-not (Test-Path -LiteralPath $Exe)) {
    throw "Executable not found: $Exe"
}

$Exe = (Resolve-Path -LiteralPath $Exe).Path
$baseUrl = "http://localhost:$Port"
Write-Step "launching $Exe"

$process = Start-Process -FilePath $Exe -PassThru

try {
    # ── 1. the host answers ────────────────────────────────────────────────────
    Write-Step "waiting for $baseUrl/__health"
    $health = $null
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) {
            throw "The app exited early with code $($process.ExitCode)."
        }

        try {
            $health = Invoke-RestMethod -Uri "$baseUrl/__health" -TimeoutSec 5
            break
        }
        catch {
            Start-Sleep -Milliseconds 750
        }
    }

    if (-not $health) {
        throw "No answer from $baseUrl/__health within $TimeoutSeconds s."
    }

    Write-Detail "ok        host reports ok=$($health.ok), port=$($health.port), uptime=$($health.uptimeSeconds)s"
    Write-Detail "web root  $($health.webRoot) (exists=$($health.webRootExists))"
    Write-Detail "endpoints $($health.endpoints -join ', ')"

    if (-not $health.webRootExists) {
        throw "The app is running but the web layer was not found next to the executable."
    }

    # ── 2. the web app document is served ─────────────────────────────────────
    Write-Step 'fetching the web app document'
    $document = Invoke-WebRequest -Uri "$baseUrl/" -TimeoutSec 15 -UseBasicParsing

    if ($document.StatusCode -ne 200) {
        throw "GET / returned $($document.StatusCode)."
    }

    foreach ($marker in @('In[st]Sanity', 'lsBoot')) {
        if ($document.Content -notlike "*$marker*") {
            throw "The served document does not contain the expected marker '$marker'."
        }
    }

    Write-Detail "ok        GET / -> 200, $($document.RawContentLength) bytes, markers present"

    # ── 3. range requests work (video/audio playback depends on them) ─────────
    Write-Step 'checking range support'
    $range = Invoke-WebRequest -Uri "$baseUrl/" -Headers @{ Range = 'bytes=0-99' } -TimeoutSec 15 -UseBasicParsing

    if ($range.StatusCode -ne 206) {
        throw "A range request returned $($range.StatusCode), expected 206."
    }

    Write-Detail "ok        Range: bytes=0-99 -> 206 ($($range.RawContentLength) bytes)"

    # ── 4. the /api/* pipeline answers ────────────────────────────────────────
    Write-Step 'exercising the API pipeline'
    $api = Invoke-WebRequest -Uri "$baseUrl/api/image" -TimeoutSec 15 -UseBasicParsing -SkipHttpErrorCheck

    if ($api.StatusCode -ne 400) {
        throw "/api/image without a url returned $($api.StatusCode), expected 400."
    }

    Write-Detail "ok        GET /api/image -> 400 ($($api.Content))"

    Write-Step 'all checks passed' 'Green'
}
finally {
    if ($process -and -not $process.HasExited) {
        Write-Step "stopping pid $($process.Id)"
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
}
