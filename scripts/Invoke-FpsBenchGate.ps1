<#
.SYNOPSIS
    Автоматический FPS/GC, -Check, -Bench и Pure WPF -Preview гейт качества для конвейера Build-FullRelease.ps1 (PowerShell 5.1+).
#>
param(
    [string]$Root = ".",
    [double]$MaxP99Ms = 8.5,
    [double]$BenchBudgetMs = 12.0,
    [switch]$SkipSelfCheck,
    [switch]$RunBench,
    [switch]$VerifyPreview
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Invoke-ZiloreSubprocess {
    param(
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $true)][string]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [int]$TimeoutMs = 90000
    )
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FileName
    $psi.Arguments = $Arguments
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    $proc = [System.Diagnostics.Process]::Start($psi)
    try {
        # Параллельное асинхронное чтение stdout и stderr исключает дедлок при переполнении буфера канала ОС
        $outTask = $proc.StandardOutput.ReadToEndAsync()
        $errTask = $proc.StandardError.ReadToEndAsync()
        if (-not $proc.WaitForExit($TimeoutMs)) {
            try { $proc.Kill() } catch { }
            throw "Timeout (${TimeoutMs}ms) while running $FileName $Arguments"
        }
        $stdout = $outTask.GetAwaiter().GetResult()
        $stderr = $errTask.GetAwaiter().GetResult()
        return [PSCustomObject]@{
            ExitCode = $proc.ExitCode
            StdOut   = $stdout
            StdErr   = $stderr
        }
    } finally {
        $proc.Dispose()
    }
}

Write-Host "=== ZiloreMiniMap v6.46.0 Automated FPS, Bench, Self-Check & Binary Gate ===" -ForegroundColor Cyan

$releaseDir = Join-Path $Root "release"
$exe = Join-Path $releaseDir "ZiloreMiniMap.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    $cand = Get-ChildItem -Path (Join-Path $releaseDir "net10") -Filter "ZiloreMiniMap.exe" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\_archive(\\|$)' } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($cand) { $exe = $cand.FullName }
}

if (-not (Test-Path -LiteralPath $exe)) {
    throw "ZiloreMiniMap.exe не найден для проверки FPS/Binary Gate."
}

$fi = Get-Item -LiteralPath $exe
$sizeMb = [Math]::Round($fi.Length / 1MB, 2)
Write-Host ("  [BIN]   {0} ({1} MB)" -f $fi.Name, $sizeMb) -ForegroundColor White

# 1. Authenticode signature check
$sig = Get-AuthenticodeSignature -FilePath ([System.Management.Automation.WildcardPattern]::Escape($exe))
if ($sig.Status -eq "Valid") {
    Write-Host ("  [SIGN]  Authenticode: Valid ({0})" -f $sig.SignerCertificate.Subject) -ForegroundColor Green
} else {
    Write-Host ("  [WARN]  Authenticode status: {0}" -f $sig.Status) -ForegroundColor Yellow
}

# 2. Check latest live perf-telemetry.log if available
$telLog = Join-Path $env:LOCALAPPDATA "ZiloreMiniMap\logs\perf-telemetry.log"
if (Test-Path -LiteralPath $telLog) {
    $lastLine = Get-Content -Path $telLog -Tail 1 -Encoding UTF8
    if ($lastLine -match 'FPS\s+(\d+)\s+·\s+([0-9.]+)/([0-9.]+)ms') {
        $fps = [int]$matches[1]
        $avg = [double]$matches[2]
        $p99 = [double]$matches[3]
        Write-Host ("  [PERF]  Last telemetry: FPS={0}, avg={1}ms, p99={2}ms (limit <= {3}ms)" -f $fps, $avg, $p99, $MaxP99Ms) -ForegroundColor Cyan
        if ($p99 -gt $MaxP99Ms) {
            throw ("FPS Perf Gate FAILED: p99 ({0}ms) exceeds budget ({1}ms)." -f $p99, $MaxP99Ms)
        }
    }
} else {
    Write-Host "  [INFO]  perf-telemetry.log пока не создан (включите Debug Profiler в настройках для сбора живых метрик)." -ForegroundColor DarkGray
}

# 3. Run built-in CoreSelfTests (-Check) on the compiled release binary
if (-not $SkipSelfCheck) {
    Write-Host ("  [TEST]  Running {0} -Check (CoreSelfTests)..." -f $fi.Name) -ForegroundColor Cyan
    $chkRes = Invoke-ZiloreSubprocess -FileName $exe -Arguments "-Check" -WorkingDirectory $fi.DirectoryName
    if ($chkRes.ExitCode -ne 0) {
        if (-not [string]::IsNullOrWhiteSpace($chkRes.StdOut)) { Write-Host $chkRes.StdOut.TrimEnd() -ForegroundColor Yellow }
        if (-not [string]::IsNullOrWhiteSpace($chkRes.StdErr)) { Write-Host $chkRes.StdErr.TrimEnd() -ForegroundColor Red }
        throw ("CoreSelfTests (-Check) failed with exit code {0}." -f $chkRes.ExitCode)
    }
    Write-Host "  [TEST]  CoreSelfTests (-Check): PASS" -ForegroundColor Green
}

# 4. Deterministic headless runtime benchmark (-Bench) on the obfuscated SingleFile EXE
if ($RunBench) {
    Write-Host ("  [BENCH] Running {0} -Bench (budget p99 <= {1}ms)..." -f $fi.Name, $BenchBudgetMs) -ForegroundColor Cyan
    $benchRes = Invoke-ZiloreSubprocess -FileName $exe -Arguments "-Bench" -WorkingDirectory $fi.DirectoryName
    if ($benchRes.ExitCode -ne 0) {
        if (-not [string]::IsNullOrWhiteSpace($benchRes.StdOut)) { Write-Host $benchRes.StdOut.TrimEnd() -ForegroundColor Yellow }
        if (-not [string]::IsNullOrWhiteSpace($benchRes.StdErr)) { Write-Host $benchRes.StdErr.TrimEnd() -ForegroundColor Red }
        throw ("Runtime benchmark (-Bench) failed with exit code {0}." -f $benchRes.ExitCode)
    }
    $benchLine = ($benchRes.StdOut -split "`r?`n" | Where-Object { $_ -match '^BENCH_RESULT\s+' } | Select-Object -Last 1)
    if ([string]::IsNullOrWhiteSpace($benchLine)) {
        throw "Runtime benchmark (-Bench) did not output BENCH_RESULT."
    }
    if ($benchLine -match 'avg_ms=([0-9.]+)\s+p99_ms=([0-9.]+)\s+total_ms=([0-9.]+)\s+iters=(\d+)\s+gc0=(\d+)\s+gc1=(\d+)') {
        $bAvg   = [double]$matches[1]
        $bP99   = [double]$matches[2]
        $bTotal = [double]$matches[3]
        $bIters = [int]$matches[4]
        $bGc0   = [int]$matches[5]
        $bGc1   = [int]$matches[6]
        Write-Host ("  [BENCH] avg={0:F3}ms · p99={1:F3}ms · total={2:F1}ms ({3} frames) · GC0={4}, GC1={5}" -f $bAvg, $bP99, $bTotal, $bIters, $bGc0, $bGc1) -ForegroundColor Green
        $reportObj = @{
            generatedUtc  = (Get-Date).ToUniversalTime().ToString("o")
            binary        = $fi.Name
            sizeMb        = $sizeMb
            avgFrameMs    = $bAvg
            p99FrameMs    = $bP99
            totalMs       = $bTotal
            iterations    = $bIters
            gc0Delta      = $bGc0
            gc1Delta      = $bGc1
            budgetP99Ms   = $BenchBudgetMs
            passed        = ($bP99 -le $BenchBudgetMs)
        }
        $benchJson = $reportObj | ConvertTo-Json -Depth 4
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [IO.File]::WriteAllText((Join-Path $releaseDir "bench-report.json"), $benchJson, $utf8NoBom)
        if ($bP99 -gt $BenchBudgetMs) {
            throw ("Runtime Bench Gate FAILED: p99 ({0:F3}ms) exceeds budget ({1}ms)." -f $bP99, $BenchBudgetMs)
        }
    } else {
        throw ("Unexpected BENCH_RESULT format: {0}" -f $benchLine)
    }
}

# 5. Optional Pure WPF -Preview end-to-end verification on obfuscated SingleFile EXE
if ($VerifyPreview) {
    $prevDir = Join-Path ([IO.Path]::GetTempPath()) ("zilore-preview-gate-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $prevDir -Force | Out-Null
    try {
        Write-Host ("  [PREV]  Running {0} -Preview (Pure WPF render gate)..." -f $fi.Name) -ForegroundColor Cyan
        $pRes = Invoke-ZiloreSubprocess -FileName $exe -Arguments "-Preview" -WorkingDirectory $prevDir
        if ($pRes.ExitCode -ne 0) {
            if (-not [string]::IsNullOrWhiteSpace($pRes.StdOut)) { Write-Host $pRes.StdOut.TrimEnd() -ForegroundColor Yellow }
            if (-not [string]::IsNullOrWhiteSpace($pRes.StdErr)) { Write-Host $pRes.StdErr.TrimEnd() -ForegroundColor Red }
            throw ("Pure WPF -Preview failed with exit code {0}." -f $pRes.ExitCode)
        }
        $wpfPrev = Join-Path $fi.DirectoryName "wpf-preview"
        $expectedPngs = @("tactical-map.png", "hud-circle.png", "hud-square.png", "search.png", "settings-general.png")
        foreach ($png in $expectedPngs) {
            $pf = Join-Path $wpfPrev $png
            if (-not (Test-Path -LiteralPath $pf) -or (Get-Item -LiteralPath $pf).Length -lt 1024) {
                throw "Pure WPF -Preview missing or empty artifact: $pf"
            }
        }
        Write-Host ("  [PREV]  Pure WPF -Preview ({0} PNGs verified): PASS" -f $expectedPngs.Count) -ForegroundColor Green
    } finally {
        if (Test-Path -LiteralPath $prevDir) { Remove-Item -LiteralPath $prevDir -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

Write-Host "[PASS] Automated FPS, Bench & Binary Gate пройден успешно." -ForegroundColor Green
