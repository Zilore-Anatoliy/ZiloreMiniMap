<#
.SYNOPSIS
    Запускает Obfuscar на промежуточной сборке ZiloreMiniMap.dll прямо перед упаковкой в SingleFile EXE.
    Вызывается автоматически из Directory.Build.targets при Release-сборке.
#>
param(
    [Parameter(Mandatory = $true)][string]$AssemblyPath,
    [string]$RefDirsFile = ""
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $AssemblyPath)) {
    Write-Warning "Obfuscar skipped: assembly not found at $AssemblyPath"
    exit 0
}

$fullAsmPath = (Resolve-Path $AssemblyPath).Path

$obfExe = Get-Command obfuscar.console -ErrorAction SilentlyContinue
if (-not $obfExe) {
    $fallback = Join-Path $env:USERPROFILE ".dotnet\tools\obfuscar.console.exe"
    if (Test-Path $fallback) { $obfExe = Get-Item $fallback }
}

if (-not $obfExe) {
    Write-Warning "Obfuscar is not installed. Run .\scripts\Setup-Obfuscar.ps1 to enable IL obfuscation."
    exit 0
}

$asmDir = Split-Path $fullAsmPath -Parent
$asmName = Split-Path $fullAsmPath -Leaf
$outDir = Join-Path $asmDir "obf_out"
$cfgPath = Join-Path $asmDir "obfuscar.runtime.xml"

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

# Collect unique assembly search paths
$searchPaths = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
[void]$searchPaths.Add($asmDir)
if ($RefDirsFile -and (Test-Path $RefDirsFile)) {
    foreach ($d in (Get-Content $RefDirsFile -Encoding UTF8)) {
        $t = $d.Trim().TrimEnd('\')
        if ($t -and (Test-Path $t)) { [void]$searchPaths.Add($t) }
    }
}

$searchXml = ($searchPaths | ForEach-Object { "  <AssemblySearchPath path=`"$([Security.SecurityElement]::Escape($_))`" />" }) -join "`r`n"

$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<Obfuscator>
  <Var name="InPath" value="$([Security.SecurityElement]::Escape($asmDir))" />
  <Var name="OutPath" value="$([Security.SecurityElement]::Escape($outDir))" />
  <Var name="KeepPublicApi" value="true" />
  <Var name="HidePrivateApi" value="true" />
  <Var name="RenameProperties" value="false" />
  <Var name="RenameEvents" value="false" />
  <Var name="RenameFields" value="true" />
  <Var name="ReuseNames" value="true" />
  <Var name="HideStrings" value="true" />
  <Var name="OptimizeMethods" value="true" />
  <Var name="UseUnicodeNames" value="false" />
  <Var name="SuppressIldasm" value="true" />
$searchXml
  <Module file="`$(InPath)\$asmName">
    <SkipType name="*CustomZoneListItem*" />
    <SkipType name="*MapPreset*" />
    <SkipType name="*AdminCommand*" />
    <SkipType name="*SharedMapMarker*" />
    <SkipType name="*WpfMapState*" />
    <SkipType name="*Position*" />
    <SkipType name="*TransportProfile*" />
    <SkipType name="*ViewModel*" />
    <SkipType name="*ErrorReportPayload*" />
    <SkipType name="*ErrorReportJsonContext*" />
  </Module>
</Obfuscator>
"@

[IO.File]::WriteAllText($cfgPath, $xml, (New-Object Text.UTF8Encoding($false)))

Write-Host "=== Running Obfuscar on $asmName ===" -ForegroundColor Cyan
$exePath = if ($obfExe.Source) { $obfExe.Source } else { $obfExe.FullName }
& $exePath $cfgPath
if ($LASTEXITCODE -ne 0) {
    throw "Obfuscar failed with exit code $LASTEXITCODE"
}

$obfAsm = Join-Path $outDir $asmName
if (-not (Test-Path $obfAsm)) {
    throw "Obfuscated assembly not produced: $obfAsm"
}

Copy-Item $obfAsm $fullAsmPath -Force
Write-Host "=== Obfuscation complete: $fullAsmPath ===" -ForegroundColor Green
