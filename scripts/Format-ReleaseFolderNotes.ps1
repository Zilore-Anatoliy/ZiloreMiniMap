<#
.SYNOPSIS
    Обновляет короткие пользовательские описания изменений:
      1) в папках готового билда (release\RELEASE-NOTES.txt, release\portable\README.txt, release\installer\README.txt)
      2) в release\update.txt (с автоматической переподписью update.txt.p7s и обновлением SHA256SUMS.txt)
      3) в README.md (RU), README_EN.md (EN), website\index.html и website\en\index.html
         (версия, билд, ссылки скачивания, дата и список изменений + экспорт в release\website\).
.EXAMPLE
    .\scripts\Format-ReleaseFolderNotes.ps1 -DryRun
    .\scripts\Format-ReleaseFolderNotes.ps1
    .\scripts\Format-ReleaseFolderNotes.ps1 -WebsiteDir "C:\Users\AsRock\Downloads\ZiloreMiniMap-Site"
#>
param(
    [string]$Root = "",
    [string]$CertThumbprint = "",
    [string]$WebsiteDir = "",
    [string]$IndexHtmlPath = "",
    [switch]$SkipDocs,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path (Split-Path $MyInvocation.MyCommand.Path -Parent) -Parent
}
$Root = (Resolve-Path -LiteralPath $Root).Path

Import-Module (Join-Path $Root "scripts\lib\ZiloreRelease.Common.psm1") -Force

$verInfo = Get-ZiloreVersionInfo -Root $Root
$displayVer = $verInfo.Version
$csprojPath = Join-Path $Root "ZiloreMiniMap.csproj"
if (Test-Path -LiteralPath $csprojPath) {
    $csprojTxt = [IO.File]::ReadAllText($csprojPath, [Text.Encoding]::UTF8)
    if ($csprojTxt -match '<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>') {
        $displayVer = $matches[1]
    }
}

Write-Host ("=== Обновление пользовательских описаний релиза v{0} (build {1}) ===" -f $displayVer, $verInfo.Build) -ForegroundColor Cyan

# 1. Папки готового билда (release\) и release\update.txt
$sync = Sync-ZiloreReleaseFolderNotes -Root $Root -Version $displayVer -Build $verInfo.Build -DryRun:$DryRun
if ($sync.UpdatedFiles.Count -eq 0) {
    Write-Host "  [INFO]  Папка release\ не найдена — пропуск обновления release\ и update.txt." -ForegroundColor Yellow
} else {
    foreach ($f in $sync.UpdatedFiles) {
        $tag = if ($DryRun) { "[DRYRUN]" } else { "[OK]    " }
        Write-Host ("  {0} {1}" -f $tag, $f) -ForegroundColor Green
    }
}

# 2. Документация и страницы сайта (README.md, README_EN.md, website\index.html, website\en\index.html -> release\website\)
$docSync = $null
if (-not $SkipDocs) {
    $docSync = Sync-ZiloreDocsAndWeb -Root $Root -Version $displayVer -Build $verInfo.Build -WebsiteDir $WebsiteDir -IndexHtmlPath $IndexHtmlPath -ExportToRelease -DryRun:$DryRun
    foreach ($item in $docSync.Items) {
        $leaf = Split-Path $item.Path -Leaf
        if ($item.Changed) {
            $tag = if ($DryRun) { "[DRYRUN]" } else { "[OK]    " }
            $noteHint = if ($item.NotesSynced) { "версия + описание" } else { "версия/ссылки" }
            Write-Host ("  {0} {1} ({2})" -f $tag, $item.Path, $noteHint) -ForegroundColor Green
        } else {
            $hint = if ($item.NotesSynced) { "уже актуален" } else { "без изменений" }
            Write-Host ("  [SKIP]   {0} — {1}" -f $item.Path, $hint) -ForegroundColor DarkGray
        }
    }
}

if ($DryRun) {
    Write-Host ""
    Write-Host "--- Предпросмотр RU (release\, README.md и website\index.html) ---" -ForegroundColor Cyan
    Write-Host $sync.Notes.PlainText
    if ($null -ne $docSync) {
        Write-Host ""
        Write-Host "--- Предпросмотр EN (README_EN.md и website\en\index.html) ---" -ForegroundColor Cyan
        Write-Host $docSync.EnNotes.PlainText
    }
    Write-Host ""
    Write-Host "--- Предпросмотр строки notes= для update.txt ---" -ForegroundColor Cyan
    Write-Host ("notes=" + $sync.Notes.UpdateManifestNotes)
    return
}

# 3. Переподпись update.txt.p7s (PKCS#7), если update.txt изменился
if ($sync.NeedsManifestResign) {
    $updatePath = Join-Path $Root "release\update.txt"
    $sigPath = Join-Path $Root "release\update.txt.p7s"
    $resigned = $false
    try {
        Add-Type -AssemblyName System.Security
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store("My", "CurrentUser")
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
        try {
            $cert = $null
            if (-not [string]::IsNullOrWhiteSpace($CertThumbprint)) {
                $cleanTp = $CertThumbprint.Replace(" ", "").ToUpperInvariant()
                $cert = $store.Certificates | Where-Object { $_.Thumbprint -eq $cleanTp -and $_.HasPrivateKey } | Select-Object -First 1
            } else {
                $cert = $store.Certificates | Where-Object {
                    $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) -and ($_.Subject -match 'Zilore' -or $_.EnhancedKeyUsageList.Oid.Value -contains '1.3.6.1.5.5.7.3.3')
                } | Sort-Object NotAfter -Descending | Select-Object -First 1
            }
            if ($null -ne $cert) {
                $bytes = [IO.File]::ReadAllBytes($updatePath)
                $contentInfo = New-Object System.Security.Cryptography.Pkcs.ContentInfo(,$bytes)
                $signedCms = New-Object System.Security.Cryptography.Pkcs.SignedCms($contentInfo, $true)
                $signer = New-Object System.Security.Cryptography.Pkcs.CmsSigner($cert)
                $signer.DigestAlgorithm = New-Object System.Security.Cryptography.Oid("2.16.840.1.101.3.4.2.1")
                $signedCms.ComputeSignature($signer)
                [IO.File]::WriteAllBytes($sigPath, $signedCms.Encode())
                $resigned = $true
                Write-Host ("  [SIGN]   Переподписан update.txt.p7s ({0})" -f $cert.Thumbprint) -ForegroundColor Cyan
            }
        } finally {
            $store.Close()
        }
    } catch {
        Write-Host ("  [WARN]   Не удалось переподписать update.txt.p7s: {0}" -f $_.Exception.Message) -ForegroundColor Yellow
    }
    if (-not $resigned) {
        Write-Host "  [WARN]   update.txt обновлён, но сертификат для переподписи update.txt.p7s не найден — пересоберите релиз через Build-FullRelease.ps1." -ForegroundColor Yellow
    }
}

if ($sync.UpdateTxtChanged) {
    $sums = Update-ZiloreSha256Sums -Root $Root -Version $displayVer
    if (-not [string]::IsNullOrWhiteSpace($sums)) {
        Write-Host ("  [HASH]   Обновлён {0}" -f $sums) -ForegroundColor Cyan
    }
}

Write-Host "=== Готово ===" -ForegroundColor Green
