<#
.SYNOPSIS
    Фаза 6D — Единый оркестратор продакшен-выпуска ZiloreMiniMap.
    Интегрирует Build-FullRelease.ps1 (опционально), облачную/OV-EV подпись,
    публикацию зеркала (Cloudflare R2 / Publish-MirrorRelease.ps1),
    генерацию и валидацию Microsoft winget-pkgs и Pure WPF -Preview гейт.
.EXAMPLE
    .\scripts\Publish-ProductionRelease.ps1 -GenerateWinget -ValidateWinget -VerifyPreview
    .\scripts\Publish-ProductionRelease.ps1 -RunFullBuild -SigningMode AzureTrustedSigning -PublishMirror -GenerateWinget -ValidateWinget -VerifyPreview
    .\scripts\Publish-ProductionRelease.ps1 -SigningMode AzureTrustedSigning -PublishMirror -GenerateWinget -DryRun
#>
param(
    [string]$Root = "",
    [switch]$RunFullBuild,
    [ValidateSet("None", "AzureTrustedSigning", "OvEvPfx", "SelfSigned")]
    [string]$SigningMode = "None",
    [string]$SigningMetadataJson = "",
    [string]$SigningThumbprint = "",
    [switch]$PublishMirror,
    [string]$R2Bucket = "",
    [switch]$GenerateWinget,
    [switch]$ValidateWinget,
    [string]$WebsiteDir = "",
    [switch]$RunBench,
    [switch]$VerifyPreview,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path (Split-Path $MyInvocation.MyCommand.Path -Parent) -Parent
}
$Root = (Resolve-Path -LiteralPath $Root).Path

Import-Module (Join-Path $Root "scripts\lib\ZiloreRelease.Common.psm1") -Force

$sw = [Diagnostics.Stopwatch]::StartNew()
$verInfo = Get-ZiloreVersionInfo -Root $Root
Write-Host ("=== ZiloreMiniMap Production Distribution Pipeline · {0} ===" -f $verInfo.Display) -ForegroundColor Cyan

$stepsExecuted = New-Object System.Collections.Generic.List[string]

# Шаг 1: Опциональный запуск Build-FullRelease.ps1 (не модифицирует существующий пайплайн)
if ($RunFullBuild) {
    $buildScript = Join-Path $Root "scripts\Build-FullRelease.ps1"
    if (-not (Test-Path -LiteralPath $buildScript)) {
        throw "Не найден scripts\Build-FullRelease.ps1."
    }
    if ($DryRun) {
        Write-Host "[DRYRUN] Step 1: .\scripts\Build-FullRelease.ps1 -CleanOldReleases" -ForegroundColor Yellow
    } else {
        Write-Host "[STEP 1] Запуск Build-FullRelease.ps1..." -ForegroundColor Cyan
        & $buildScript -CleanOldReleases
    }
    $stepsExecuted.Add("BuildFullRelease")
}

# Шаг 2: Облачная / коммерческая подпись кода (Azure Trusted Signing / OV-EV)
if ($SigningMode -ne "None") {
    $signScript = Join-Path $Root "scripts\Invoke-TrustedSigning.ps1"
    Write-Host ("[STEP 2] Доверенная подпись кода ({0})..." -f $SigningMode) -ForegroundColor Cyan
    $signParams = @{
        Root   = $Root
        Mode   = $SigningMode
        DryRun = [bool]$DryRun
    }
    if (-not [string]::IsNullOrWhiteSpace($SigningMetadataJson)) { $signParams["MetadataJson"] = $SigningMetadataJson }
    if (-not [string]::IsNullOrWhiteSpace($SigningThumbprint)) { $signParams["CertThumbprint"] = $SigningThumbprint }
    & $signScript @signParams
    $stepsExecuted.Add("TrustedSigning:" + $SigningMode)
}

# Шаг 2.5: Синхронизация коротких пользовательских описаний в папках билда и update.txt
$notesSyncScript = Join-Path $Root "scripts\Format-ReleaseFolderNotes.ps1"
if (Test-Path -LiteralPath $notesSyncScript) {
    Write-Host "[STEP 2.5] Обновление описаний релиза (release\, update.txt, README и website\index.html + en\index.html)..." -ForegroundColor Cyan
    & $notesSyncScript -Root $Root -WebsiteDir $WebsiteDir -DryRun:$DryRun
    $stepsExecuted.Add("UserFriendlyReleaseNotes")
}

# Шаг 3: Инвентаризация артефактов и проверка подписей
$arts = Get-ZiloreReleaseArtifacts -Root $Root -Version $verInfo.Version
Write-Host "[STEP 3] Аудит релизных артефактов..." -ForegroundColor Cyan
$sigStatuses = @{}
foreach ($k in @("InstallerExe", "PortableZip", "SingleFileExe", "UpdateExe", "UpdateManifest", "UpdateSig", "Sha256Sums")) {
    $a = $arts[$k]
    $state = if ($a.Exists) { ("OK ({0:N1} KB)" -f ($a.Size / 1024.0)) } else { "MISSING" }
    Write-Host ("  - {0,-16} : {1}" -f $k, $state)
    if ($a.Exists -and ($k -eq "InstallerExe" -or $k -eq "SingleFileExe" -or $k -eq "UpdateExe")) {
        $sigStatuses[$k] = Test-ZiloreAuthenticodeStatus -FilePath $a.Path
    }
}

# Шаг 4: Публикация зеркала (Publish-MirrorRelease.ps1 или Cloudflare R2 через wrangler)
if ($PublishMirror) {
    Write-Host "[STEP 4] Публикация зеркала обновлений..." -ForegroundColor Cyan
    $mirrorScript = Join-Path $Root "scripts\Publish-MirrorRelease.ps1"
    if (Test-Path -LiteralPath $mirrorScript) {
        if ($DryRun) {
            Write-Host ("[DRYRUN] Вызов {0}" -f $mirrorScript) -ForegroundColor Yellow
        } else {
            & $mirrorScript
        }
        $stepsExecuted.Add("MirrorScript")
    } elseif (-not [string]::IsNullOrWhiteSpace($R2Bucket)) {
        $wrangler = Get-Command "wrangler" -ErrorAction SilentlyContinue
        if ($null -eq $wrangler -and -not $DryRun) {
            throw "Не найден CLI 'wrangler' для публикации в Cloudflare R2 (npm i -g wrangler)."
        }
        $r2Keys = @("UpdateManifest", "UpdateSig", "UpdateExe", "InstallerExe", "Sha256Sums")
        foreach ($rk in $r2Keys) {
            $item = $arts[$rk]
            if (-not $item.Exists) { continue }
            $leaf = Split-Path $item.Path -Leaf
            $obj = ("{0}/latest/{1}" -f $R2Bucket, $leaf)
            if ($DryRun) {
                Write-Host ("[DRYRUN] wrangler r2 object put {0} --file `"{1}`"" -f $obj, $item.Path) -ForegroundColor Yellow
            } else {
                & wrangler r2 object put $obj --file $item.Path
                if ($LASTEXITCODE -ne 0) { throw "Ошибка загрузки $leaf в R2 ($obj)." }
            }
        }
        $stepsExecuted.Add("CloudflareR2:" + $R2Bucket)
    } else {
        Write-Host "[WARN] Publish-MirrorRelease.ps1 не найден и не указан -R2Bucket — публикация зеркала пропущена." -ForegroundColor Yellow
    }
}

# Шаг 5: Генерация и валидация Microsoft winget-pkgs (Schema 1.6.0)
$wingetDir = Join-Path $Root ("release\winget\Zilore.ZiloreMiniMap\" + $verInfo.Version)
if ($GenerateWinget) {
    Write-Host "[STEP 5] Генерация манифестов Microsoft winget-pkgs..." -ForegroundColor Cyan
    $wgScript = Join-Path $Root "scripts\New-WingetManifests.ps1"
    if (-not (Test-Path -LiteralPath $wgScript)) { throw "Не найден $wgScript" }
    if ($DryRun) {
        Write-Host ("[DRYRUN] {0} -Version {1} -Validate:{2}" -f $wgScript, $verInfo.Version, [bool]$ValidateWinget) -ForegroundColor Yellow
    } else {
        if ($ValidateWinget) {
            & $wgScript -Root $Root -Version $verInfo.Version -Validate
        } else {
            & $wgScript -Root $Root -Version $verInfo.Version
        }
    }
    $stepsExecuted.Add("WingetManifests")
}

# Шаг 6: Pure WPF -Preview + Runtime -Bench + SelfCheck гейт на обфусцированном SingleFile EXE
if ($VerifyPreview -or $RunBench) {
    Write-Host "[STEP 6] Финальный Runtime -Bench / Pure WPF -Preview & SelfCheck гейт..." -ForegroundColor Cyan
    $gateScript = Join-Path $Root "scripts\Invoke-FpsBenchGate.ps1"
    if ($DryRun) {
        Write-Host ("[DRYRUN] {0} -RunBench:{1} -VerifyPreview:{2}" -f $gateScript, [bool]($RunBench -or $VerifyPreview), [bool]$VerifyPreview) -ForegroundColor Yellow
    } elseif (Test-Path -LiteralPath $gateScript) {
        & $gateScript -Root $Root -RunBench:($RunBench -or $VerifyPreview) -VerifyPreview:$VerifyPreview
        $stepsExecuted.Add("BenchAndPreviewGate")
    }
}

$sw.Stop()
$auditPath = Join-Path $Root "release\release-audit-report.json"
$report = @{
    generatedUtc  = (Get-Date).ToUniversalTime().ToString("o")
    version       = $verInfo.Version
    build         = $verInfo.Build
    dryRun        = [bool]$DryRun
    elapsedSec    = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
    steps         = $stepsExecuted.ToArray()
    wingetPath    = $wingetDir
    artifacts     = $arts
    signatures    = $sigStatuses
}
if (-not $DryRun) {
    Write-ZiloreAuditReport -OutputPath $auditPath -ReportData $report | Out-Null
    Write-Host ("[REPORT] Сохранён аудит релиза: {0}" -f $auditPath) -ForegroundColor Green
}

Write-Host ("=== Production Distribution Pipeline завершён за {0:N1} с ===" -f $sw.Elapsed.TotalSeconds) -ForegroundColor Green
