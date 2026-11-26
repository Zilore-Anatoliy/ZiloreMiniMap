<#
.SYNOPSIS
    Универсальный модуль доверенной подписи кода для ZiloreMiniMap (Фаза 6D).
    Поддерживает:
      - AzureTrustedSigning (Microsoft Azure Artifact Signing — полное снятие Windows SmartScreen)
      - OvEvPfx (коммерческий OV/EV PFX или аппаратный USB-токен по SHA-1 Thumbprint)
      - SelfSigned (локальный тестовый сертификат разработки)
.EXAMPLE
    .\scripts\Invoke-TrustedSigning.ps1 -Mode AzureTrustedSigning -MetadataJson .\signing\azure-signing.json -DryRun
    .\scripts\Invoke-TrustedSigning.ps1 -Mode OvEvPfx -CertThumbprint "ABCDEF..."
#>
param(
    [string]$Root = "",
    [ValidateSet("AzureTrustedSigning", "OvEvPfx", "SelfSigned")]
    [string]$Mode = "AzureTrustedSigning",
    [string[]]$Files = @(),
    [string]$MetadataJson = "",
    [string]$DlibPath = "",
    [string]$PfxPath = "",
    [string]$CertThumbprint = "",
    [string]$TimestampUrl = "",
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

if ($null -eq $Files -or $Files.Count -eq 0) {
    $arts = Get-ZiloreReleaseArtifacts -Root $Root -Version $verInfo.Version
    $autoList = New-Object System.Collections.Generic.List[string]
    foreach ($k in @("SingleFileExe", "UpdateExe", "InstallerExe")) {
        if ($arts[$k].Exists) { $autoList.Add($arts[$k].Path) }
    }
    $Files = $autoList.ToArray()
}

if ($Files.Count -eq 0) {
    throw "Не найдены исполняемые файлы релиза для подписи. Сначала выполните сборку Build-FullRelease.ps1."
}

function Find-SignTool {
    $cmd = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if ($null -ne $cmd) { return $cmd.Source }
    $sdkRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path -LiteralPath $sdkRoot) {
        $found = Get-ChildItem -Path $sdkRoot -Filter "signtool.exe" -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($null -ne $found) { return $found.FullName }
    }
    return $null
}

Write-Host ("=== ZiloreMiniMap Trusted Signing ({0}) · {1} ===" -f $Mode, $verInfo.Display) -ForegroundColor Cyan

$signTool = Find-SignTool
if ([string]::IsNullOrWhiteSpace($signTool) -and -not $DryRun) {
    throw "Не найден signtool.exe (Windows 10/11 SDK). Установите Windows SDK Signing Tools."
}

if ($Mode -eq "AzureTrustedSigning") {
    if ([string]::IsNullOrWhiteSpace($TimestampUrl)) { $TimestampUrl = "http://timestamp.acs.microsoft.com" }
    if ([string]::IsNullOrWhiteSpace($MetadataJson)) {
        $MetadataJson = if (-not [string]::IsNullOrWhiteSpace($env:ZILORE_AZURE_SIGN_METADATA)) {
            $env:ZILORE_AZURE_SIGN_METADATA
        } else {
            Join-Path $Root "signing\azure-trusted-signing.json"
        }
    }
    if ([string]::IsNullOrWhiteSpace($DlibPath)) {
        $DlibPath = if (-not [string]::IsNullOrWhiteSpace($env:ZILORE_AZURE_SIGN_DLIB)) {
            $env:ZILORE_AZURE_SIGN_DLIB
        } else {
            Join-Path $env:LOCALAPPDATA "Microsoft\MicrosoftArtifactSigningClientTools\Azure.CodeSigning.Dlib.dll"
        }
    }
    if (-not $DryRun) {
        if (-not (Test-Path -LiteralPath $MetadataJson)) {
            throw "Не найден файл метаданных Azure Trusted Signing: $MetadataJson (см. docs\RELEASE-TRUST-AND-DISTRIBUTION.md)."
        }
        if (-not (Test-Path -LiteralPath $DlibPath)) {
            throw "Не найдена библиотека Azure.CodeSigning.Dlib.dll: $DlibPath. Установите пакет Microsoft.Trusted.Signing.Client."
        }
    }

    foreach ($f in $Files) {
        $argsList = @(
            "sign", "/v", "/debug",
            "/fd", "SHA256",
            "/tr", $TimestampUrl,
            "/td", "SHA256",
            "/dlib", $DlibPath,
            "/dmdf", $MetadataJson,
            $f
        )
        if ($DryRun) {
            Write-Host ("[DRYRUN] signtool {0}" -f ($argsList -join " ")) -ForegroundColor Yellow
        } else {
            & $signTool @argsList
            if ($LASTEXITCODE -ne 0) { throw "Azure Trusted Signing завершился с кодом $LASTEXITCODE для $f" }
        }
    }
}
elseif ($Mode -eq "OvEvPfx") {
    if ([string]::IsNullOrWhiteSpace($TimestampUrl)) { $TimestampUrl = "http://timestamp.digicert.com" }
    foreach ($f in $Files) {
        $argsList = New-Object System.Collections.Generic.List[string]
        $argsList.Add("sign")
        $argsList.Add("/fd"); $argsList.Add("SHA256")
        $argsList.Add("/tr"); $argsList.Add($TimestampUrl)
        $argsList.Add("/td"); $argsList.Add("SHA256")
        $argsList.Add("/d");  $argsList.Add("ZiloreMiniMap")
        $argsList.Add("/du"); $argsList.Add("https://minimap.zilore.app")

        if (-not [string]::IsNullOrWhiteSpace($CertThumbprint)) {
            $argsList.Add("/sha1"); $argsList.Add($CertThumbprint.Trim())
        } elseif (-not [string]::IsNullOrWhiteSpace($PfxPath)) {
            if (-not $DryRun -and -not (Test-Path -LiteralPath $PfxPath)) {
                throw "PFX сертификат не найден: $PfxPath"
            }
            $argsList.Add("/f"); $argsList.Add($PfxPath)
            if (-not [string]::IsNullOrWhiteSpace($env:ZILORE_SIGN_PFX_PASSWORD)) {
                $argsList.Add("/p"); $argsList.Add($env:ZILORE_SIGN_PFX_PASSWORD)
            }
        } else {
            throw "Для режима OvEvPfx укажите -CertThumbprint (USB-токен/хранилище) или -PfxPath."
        }
        $argsList.Add($f)

        if ($DryRun) {
            $safePrint = ($argsList.ToArray() -join " ")
            if (-not [string]::IsNullOrWhiteSpace($env:ZILORE_SIGN_PFX_PASSWORD)) {
                $safePrint = $safePrint.Replace($env:ZILORE_SIGN_PFX_PASSWORD, "***")
            }
            Write-Host ("[DRYRUN] signtool {0}" -f $safePrint) -ForegroundColor Yellow
        } else {
            & $signTool @($argsList.ToArray())
            if ($LASTEXITCODE -ne 0) { throw "OV/EV подпись завершилась с кодом $LASTEXITCODE для $f" }
        }
    }
}
elseif ($Mode -eq "SelfSigned") {
    $localSign = Join-Path $Root "scripts\Sign-ReleaseBinaries.ps1"
    if ($DryRun) {
        Write-Host ("[DRYRUN] Вызов локального скрипта подписи: {0}" -f $localSign) -ForegroundColor Yellow
    } elseif (Test-Path -LiteralPath $localSign) {
        & $localSign
    } else {
        Write-Host "[WARN] scripts\Sign-ReleaseBinaries.ps1 не найден — пропуск SelfSigned." -ForegroundColor Yellow
    }
}

if (-not $DryRun) {
    foreach ($f in $Files) {
        $st = Test-ZiloreAuthenticodeStatus -FilePath $f
        Write-Host ("  [VERIFY] {0} -> Status={1}, Timestamped={2}" -f (Split-Path $f -Leaf), $st.Status, $st.Timestamped) -ForegroundColor Green
    }
    $sums = Update-ZiloreSha256Sums -Root $Root -Version $verInfo.Version
    if (-not [string]::IsNullOrWhiteSpace($sums)) {
        Write-Host ("  [HASH]   Обновлён {0}" -f $sums) -ForegroundColor Cyan
    }
}
