<#
.SYNOPSIS
    Генерирует официальные YAML-манифесты Microsoft winget-pkgs (Schema 1.6.0)
    для Zilore.ZiloreMiniMap в release\winget\Zilore.ZiloreMiniMap\<Version>\
    с фактическим SHA-256 установщика Inno Setup и локализацией (en-US + ru-RU).
.EXAMPLE
    .\scripts\New-WingetManifests.ps1
    .\scripts\New-WingetManifests.ps1 -OpenOutputFolder
#>
param(
    [string]$Root = "",
    [string]$Version = "",
    [string]$Repository = "Zilore-Anatoliy/ZiloreMiniMap",
    [switch]$Validate,
    [switch]$OpenOutputFolder
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path (Split-Path $MyInvocation.MyCommand.Path -Parent) -Parent
}
$Root = (Resolve-Path -LiteralPath $Root).Path

if ([string]::IsNullOrWhiteSpace($Version)) {
    $mmPath = Join-Path $Root "src\Engine\MiniMap.cs"
    if (-not (Test-Path -LiteralPath $mmPath)) { throw "src\Engine\MiniMap.cs не найден." }
    $mmTxt = [IO.File]::ReadAllText($mmPath, [Text.Encoding]::UTF8)
    if ($mmTxt -match 'public const string VersionString\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)";') {
        $Version = $matches[1]
    } else {
        throw "Не удалось прочитать VersionString из MiniMap.cs."
    }
}

$setupName = "ZiloreMiniMap-Setup-v{0}.exe" -f $Version
$setupPath = Join-Path $Root ("release\installer\" + $setupName)
if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "Не найден установщик: $setupPath. Сначала соберите релиз через Build-FullRelease.ps1."
}

$sha256 = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToUpperInvariant()
$releaseDate = (Get-Date).ToString("yyyy-MM-dd")
$installerUrl = "https://github.com/{0}/releases/download/v{1}/{2}" -f $Repository, $Version, $setupName
$releaseUrl = "https://github.com/{0}/releases/tag/v{1}" -f $Repository, $Version

$outDir = Join-Path $Root ("release\winget\Zilore.ZiloreMiniMap\" + $Version)
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# 1. Version manifest
$versionYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.6.0.schema.json
PackageIdentifier: Zilore.ZiloreMiniMap
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"@
[IO.File]::WriteAllText((Join-Path $outDir "Zilore.ZiloreMiniMap.yaml"), $versionYaml, $utf8NoBom)

# 2. Installer manifest
$installerYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json
PackageIdentifier: Zilore.ZiloreMiniMap
PackageVersion: $Version
InstallerLocale: en-US
MinimumOSVersion: 10.0.17763.0
InstallerType: inno
Scope: user
InstallModes:
- interactive
- silent
- silentWithProgress
InstallerSwitches:
  Silent: /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS
  SilentWithProgress: /SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS
UpgradeBehavior: install
ReleaseDate: $releaseDate
Installers:
- Architecture: x64
  InstallerUrl: $installerUrl
  InstallerSha256: $sha256
ManifestType: installer
ManifestVersion: 1.6.0
"@
[IO.File]::WriteAllText((Join-Path $outDir "Zilore.ZiloreMiniMap.installer.yaml"), $installerYaml, $utf8NoBom)

# 3. Default Locale (en-US)
$enYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.6.0.schema.json
PackageIdentifier: Zilore.ZiloreMiniMap
PackageVersion: $Version
PackageLocale: en-US
Publisher: Zilore
PublisherUrl: https://minimap.zilore.app
PublisherSupportUrl: https://github.com/$Repository/issues
PackageName: ZiloreMiniMap
PackageUrl: https://github.com/$Repository
License: Proprietary
ShortDescription: 100% EAC-safe tactical real-time GPS minimap, A* road router, voice navigator, and E2EE squad overlay for SCUM.
Tags:
- scum
- minimap
- overlay
- gps
- navigation
- gaming
ReleaseNotesUrl: $releaseUrl
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@
[IO.File]::WriteAllText((Join-Path $outDir "Zilore.ZiloreMiniMap.locale.en-US.yaml"), $enYaml, $utf8NoBom)

# 4. Russian Locale (ru-RU)
$ruYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.locale.1.6.0.schema.json
PackageIdentifier: Zilore.ZiloreMiniMap
PackageVersion: $Version
PackageLocale: ru-RU
Publisher: Zilore
PublisherUrl: https://minimap.zilore.app
PackageName: ZiloreMiniMap
ShortDescription: Безопасная для EAC тактическая GPS-миникарта, оффлайн-навигатор A*, голосовой штурман и шифрованный оверлей отряда для SCUM.
ReleaseNotesUrl: $releaseUrl
ManifestType: locale
ManifestVersion: 1.6.0
"@
[IO.File]::WriteAllText((Join-Path $outDir "Zilore.ZiloreMiniMap.locale.ru-RU.yaml"), $ruYaml, $utf8NoBom)

Write-Host "=== Microsoft winget-pkgs манифесты (Schema 1.6.0) готовы ===" -ForegroundColor Green
Write-Host ("  Версия:  v{0}" -f $Version)
Write-Host ("  SHA-256: {0}" -f $sha256) -ForegroundColor Cyan
Write-Host ("  Папка:   {0}" -f $outDir)
Write-Host "  Проверка: winget validate --manifest `"$outDir`"" -ForegroundColor DarkGray

if ($Validate) {
    $wingetCmd = Get-Command "winget.exe" -ErrorAction SilentlyContinue
    if ($null -eq $wingetCmd) {
        Write-Host "  [WARN] winget.exe не найден в PATH — пропуск автоматической валидации схемы." -ForegroundColor Yellow
    } else {
        Write-Host "  [VALIDATE] Запуск winget validate --manifest ..." -ForegroundColor Cyan
        & $wingetCmd.Source validate --manifest $outDir
        if ($LASTEXITCODE -ne 0) { throw "winget validate завершился с ошибкой (exit $LASTEXITCODE)." }
        Write-Host "  [VALIDATE] winget validate: PASS" -ForegroundColor Green
    }
}

if ($OpenOutputFolder) {
    Start-Process "explorer.exe" -ArgumentList $outDir
}
