<#
.SYNOPSIS
    Boots the published app and verifies that it serves the web layer.

.DESCRIPTION
    Two phases:

    1. Backend (always). Launches the app with INSTSANITY_HEADLESS=1 — no WebView, no
       desktop session needed — and asserts that the loopback host answers, that the web
       document is served with the expected markers, that range requests work (media
       playback depends on them) and that the /api/* pipeline responds. This is fully
       deterministic and is what CI gates on.

    2. UI (optional, -IncludeUi). Launches the app normally and checks that it stays alive
       while still answering /__health, which is as far as a headless runner can go without
       inspecting pixels. Needs the Evergreen WebView2 runtime.

    Failures are reported with ::error::/::warning:: workflow commands so they surface as
    GitHub annotations, in addition to the log file.

.PARAMETER Exe
    Path to InStSanity.exe (the published, unpackaged build).

.PARAMETER Port
    Port the host is expected to listen on. Defaults to 8000, the port the web layer
    prefers; if that port was busy the app serves on the next free one and these checks
    fail fast.

.PARAMETER TimeoutSeconds
    How long to wait for the host to answer /__health in each phase.

.PARAMETER IncludeUi
    Also run the WebView phase.

.EXAMPLE
    pwsh -File tools/smoke-test.ps1 -Exe artifacts/InStSanity-win-x64/InStSanity.exe -IncludeUi
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Exe,

    [int] $Port = 8000,

    [int] $TimeoutSeconds = 90,

    [switch] $IncludeUi
)

$ErrorActionPreference = 'Stop'
$logPath = 'smoke-test.log'
$baseUrl = "http://localhost:$Port"

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

function Write-Annotation {
    param([string] $Level, [string] $Title, [string] $Message)
    # Workflow command: readable from the check run's annotations even when job logs are
    # not reachable (self-hosted/proxied environments).
    $flat = ($Message -replace "`r?`n", ' ').Trim()
    Write-Host "::$Level title=$Title::$flat"
}

function Wait-ForHost {
    param([System.Diagnostics.Process] $Process)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        if ($Process.HasExited) {
            throw "The app exited early with code $($Process.ExitCode) (see the log above)."
        }

        try {
            return Invoke-RestMethod -Uri "$baseUrl/__health" -TimeoutSec 5
        }
        catch {
            Start-Sleep -Milliseconds 750
        }
    }

    throw "No answer from $baseUrl/__health within $TimeoutSeconds s."
}

function Start-App {
    param([bool] $Headless)

    $environment = @{}
    if ($Headless) { $environment['INSTSANITY_HEADLESS'] = '1' }

    # Set for this process so the child inherits it; Start-Process -Environment needs PS 7.4.
    $env:INSTSANITY_HEADLESS = if ($Headless) { '1' } else { $null }

    $process = Start-Process -FilePath $Exe -PassThru

    if (-not $Headless) { Remove-Item Env:\INSTSANITY_HEADLESS -ErrorAction SilentlyContinue }

    return $process
}

Set-Content -Path $logPath -Value "smoke test $(Get-Date -Format o)"

if (-not (Test-Path -LiteralPath $Exe)) {
    Write-Annotation 'error' 'smoke-test' "Executable not found: $Exe"
    throw "Executable not found: $Exe"
}

$Exe = (Resolve-Path -LiteralPath $Exe).Path

# ── phase 1: backend, deterministic ────────────────────────────────────────────
$process = $null
try {
    Write-Step "phase 1: launching $Exe (headless)"
    $process = Start-App -Headless $true

    Write-Step "waiting for $baseUrl/__health"
    $health = Wait-ForHost -Process $process

    Write-Detail "host      ok=$($health.ok), port=$($health.port), uptime=$($health.uptimeSeconds)s"
    Write-Detail "web root  $($health.webRoot) (exists=$($health.webRootExists))"
    Write-Detail "endpoints $($health.endpoints -join ', ')"

    if ($health.endpoints -notcontains '/api/browse-folder') {
        throw "The native folder picker route is missing from the running app."
    }

    if ($health.endpoints -notcontains '/api/download-selected') {
        throw "The selected-photo download route is missing from the running app."
    }

    if (-not $health.webRootExists) {
        throw "The app is running but the web layer was not found next to the executable (looked in $($health.webRoot))."
    }

    Write-Step 'fetching the web app document'
    $document = Invoke-WebRequest -Uri "$baseUrl/" -TimeoutSec 15 -UseBasicParsing

    if ($document.StatusCode -ne 200) { throw "GET / returned $($document.StatusCode)." }

    # String.Contains, not -like: the marker contains brackets, which PowerShell wildcards
    # would read as a character class.
    foreach ($marker in @('In[st]Sanity', 'lsBoot')) {
        if (-not $document.Content.Contains($marker)) {
            throw "The served document does not contain the expected marker '$marker'."
        }
    }

    Write-Detail "document  GET / -> 200, $($document.RawContentLength) bytes, markers present"

    Write-Step 'checking range support (media playback depends on it)'
    $range = Invoke-WebRequest -Uri "$baseUrl/" -Headers @{ Range = 'bytes=0-99' } -TimeoutSec 15 -UseBasicParsing

    if ($range.StatusCode -ne 206) { throw "A range request returned $($range.StatusCode), expected 206." }

    Write-Detail "range     bytes=0-99 -> 206 ($($range.RawContentLength) bytes)"

    Write-Step 'exercising the /api/* pipeline'
    $api = Invoke-WebRequest -Uri "$baseUrl/api/image" -TimeoutSec 15 -UseBasicParsing -SkipHttpErrorCheck

    if ($api.StatusCode -ne 400) { throw "/api/image without a url returned $($api.StatusCode), expected 400." }

    Write-Detail "api       GET /api/image -> 400 ($($api.Content))"

    Write-Step 'verifying a selected photo is written to disk'
    $downloadFolder = Join-Path $env:TEMP "InStSanity-download-smoke-$([Guid]::NewGuid().ToString('N'))"
    $pngBase64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/WMcAAAAASUVORK5CYII='
    $downloadFile = Join-Path $downloadFolder 'smoke-image.png'

    try {
        $downloadRequest = @{
            folder = $downloadFolder
            items = @(@{
                shortcode = 'smoke'
                photoIndex = 0
                filename = 'smoke-image.png'
                rawUrl = ''
                dataUri = "data:image/png;base64,$pngBase64"
                svgMarkup = ''
            })
        } | ConvertTo-Json -Depth 8 -Compress

        $downloadResult = Invoke-RestMethod `
            -Uri "$baseUrl/api/download-selected" `
            -Method Post `
            -ContentType 'application/json' `
            -Body $downloadRequest `
            -TimeoutSec 60

        if (-not $downloadResult.ok -or $downloadResult.savedCount -ne 1) {
            throw "Download endpoint did not report one saved image: $($downloadResult | ConvertTo-Json -Compress)."
        }

        if (-not (Test-Path -LiteralPath $downloadFile)) {
            throw "Download endpoint reported success but did not create $downloadFile."
        }

        $savedBytes = [System.IO.File]::ReadAllBytes($downloadFile)
        if ([Convert]::ToBase64String($savedBytes) -ne $pngBase64) {
            throw 'The saved image bytes do not match the submitted image.'
        }

        Write-Detail "download  POST /api/download-selected -> saved 1 image to $downloadFile"
    }
    finally {
        if (Test-Path -LiteralPath $downloadFolder) {
            Remove-Item -LiteralPath $downloadFolder -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Step 'phase 1 passed' 'Green'
}
catch {
    Write-Annotation 'error' 'smoke-test-backend' $_.Exception.Message

    if (Test-Path -LiteralPath $logPath) {
        Get-Content -LiteralPath $logPath | Select-Object -Last 8 | ForEach-Object {
            Write-Annotation 'warning' 'smoke-test-log' $_
        }
    }

    throw
}
finally {
    if ($process -and -not $process.HasExited) {
        Write-Step "stopping pid $($process.Id)"
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
}

# ── phase 2: the WebView actually loads (best effort, needs a desktop session) ──
if (-not $IncludeUi) {
    Write-Step 'phase 2 skipped (-IncludeUi not set)'
    Write-Step 'all checks passed' 'Green'
    return
}

$process = $null
try {
    Write-Step 'phase 2: launching with the WebView'
    $process = Start-App -Headless $false

    $health = Wait-ForHost -Process $process
    Write-Detail "host      ok=$($health.ok), port=$($health.port)"

    # Let the WebView initialise; a crash here (missing runtime, no desktop) shows up as
    # the process going away.
    Start-Sleep -Seconds 10

    if ($process.HasExited) {
        throw "The app exited (code $($process.ExitCode)) after the host came up — the WebView phase did not survive."
    }

    $stillAlive = Invoke-RestMethod -Uri "$baseUrl/__health" -TimeoutSec 5
    Write-Detail "ui        process alive after 10s, host still answering (uptime=$($stillAlive.uptimeSeconds)s)"

    Write-Step 'phase 2 passed' 'Green'
    Write-Step 'all checks passed' 'Green'
}
catch {
    Write-Annotation 'error' 'smoke-test-ui' $_.Exception.Message
    throw
}
finally {
    if ($process -and -not $process.HasExited) {
        Write-Step "stopping pid $($process.Id)"
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
}
