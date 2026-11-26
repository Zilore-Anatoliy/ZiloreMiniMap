<#
.SYNOPSIS
    Фаза 5C — Безопасное удаление 3 выведенных legacy WinForms-файлов
    (OverlayWindow.cs, MapCanvas.cs, FieldConsoleUi.cs) после перевода на Pure WPF.
    Перед удалением автоматически создаётся локальный снапшот исходников.
.EXAMPLE
    .\scripts\Cleanup-Phase5CLegacy.ps1 -WhatIf
    .\scripts\Cleanup-Phase5CLegacy.ps1
#>
param(
    [string]$Root = "",
    [switch]$WhatIf,
    [switch]$SkipSnapshot
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path (Split-Path $MyInvocation.MyCommand.Path -Parent) -Parent
}
$Root = (Resolve-Path -LiteralPath $Root).Path

$retiredFiles = @(
    "src\UI\OverlayWindow.cs",
    "src\Engine\MapCanvas.cs",
    "src\UI\FieldConsoleUi.cs"
)

Write-Host "=== ZiloreMiniMap Phase 5C · Retired WinForms Files Cleanup ===" -ForegroundColor Cyan

if (-not $WhatIf -and -not $SkipSnapshot) {
    $snapScript = Join-Path $Root "scripts\Backup-SourceSnapshot.ps1"
    if (Test-Path -LiteralPath $snapScript) {
        & $snapScript -Root $Root -Label "pre-phase5c-cleanup"
    }
}

$removed = 0
foreach ($rel in $retiredFiles) {
    $fp = Join-Path $Root $rel
    if (Test-Path -LiteralPath $fp) {
        $len = (Get-Item -LiteralPath $fp).Length
        if ($len -gt 512) {
            Write-Host ("[SKIP] {0} больше 512 Б ({1} Б) — сначала распакуйте патч v6.32.0." -f $rel, $len) -ForegroundColor Yellow
            continue
        }
        if ($WhatIf) {
            Write-Host ("[WHATIF] Удалить выведенный файл: {0} ({1} Б)" -f $rel, $len) -ForegroundColor Yellow
        } else {
            Remove-Item -LiteralPath $fp -Force
            Write-Host ("[REMOVED] {0} ({1} Б)" -f $rel, $len) -ForegroundColor Green
        }
        $removed++
    }
}

Write-Host ("=== Итог: обработано выведенных файлов = {0} ===" -f $removed) -ForegroundColor Cyan
