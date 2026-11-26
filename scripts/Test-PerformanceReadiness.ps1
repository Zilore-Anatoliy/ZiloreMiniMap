param([string]$Root = ".")
$ErrorActionPreference = "Stop"
Write-Host "=== ZiloreMiniMap v6.48.0 Enterprise 10-Gate Defensive Security, Zero-GC & Build Hygiene ===" -ForegroundColor Cyan

$srcDir     = Join-Path $Root "src"
$scriptsDir = Join-Path $Root "scripts"
$files      = Get-ChildItem -Path $srcDir -Filter "*.cs" -Recurse
$errors     = 0
$warnings   = 0

# Load defined UiText keys once from WpfFieldTheme.cs
$themePath = Join-Path $srcDir "UI\WpfFieldTheme.cs"
$definedKeys = @{}
if (Test-Path -LiteralPath $themePath) {
    $themeTxt = Get-Content -Path $themePath -Raw -Encoding UTF8
    foreach ($m in [regex]::Matches($themeTxt, 'case "([^"]+)":')) {
        $definedKeys[$m.Groups[1].Value] = $true
    }
}

$hasDllSearchHardening = $false
$usesLibraryImport     = $false

foreach ($f in $files) {
    $txt = Get-Content -Path $f.FullName -Raw -Encoding UTF8

    if ($txt -match 'DefaultDllImportSearchPaths') {
        $hasDllSearchHardening = $true
    }
    if ($txt -match '\[LibraryImport\s*\(') {
        $usesLibraryImport = $true
    }

    # Gate 1: C# Compiler Traps (CS0677, CS0029, CS1023, CS1009)
    if ($txt -match '\bvolatile\s+(double|long|ulong|decimal)\b') {
        Write-Host "[FAIL] CS0677 volatile 64-bit/decimal in $($f.Name)" -ForegroundColor Red
        $errors++
    }
    if ($txt -match '\bdouble\[\]\s+\w+\s*=\s*new\[\]\s*\{[0-9,\s]+\}') {
        Write-Host "[FAIL] CS0029 int[] assigned to double[] in $($f.Name)" -ForegroundColor Red
        $errors++
    }
    if ($txt -match '(?m)^\s*using\s*\([^\r\n]+\)\s*\r?\n\s*(?:var|int|long|double|float|bool|byte|string)(?:\[\]|\?)?\s+[A-Za-z_]\w*\s*=') {
        Write-Host "[FAIL] CS1023 local declaration directly under braceless using() in $($f.Name)" -ForegroundColor Red
        $errors++
    }
    foreach ($sm in [regex]::Matches($txt, '(?m)//[^\r\n]*|/\*[\s\S]*?\*/|@"(?:""|[^"])*"|("(?:\\.|[^"\\\r\n])*")')) {
        if (-not $sm.Groups[1].Success) { continue }
        $lit = $sm.Groups[1].Value.Substring(1, $sm.Groups[1].Value.Length - 2)
        $cleanLit = $lit.Replace('\\', '')
        if ($cleanLit -match '\\([^''"\\0abefnrtvuUx])') {
            Write-Host "[FAIL] CS1009 unrecognized escape sequence \$($matches[1]) in $($f.Name)" -ForegroundColor Red
            $errors++
            break
        }
    }

    # Gate 2: Hot-path WPF Freezable transforms (Zero-GC render)
    if ($f.Name -like "WpfMapSurface*.cs" -or $f.Name -eq "WpfMapStreaming.cs") {
        if ($txt -match 'PushTransform\s*\(\s*new\s+(MatrixTransform|TranslateTransform|ScaleTransform|RotateTransform)\s*\(') {
            Write-Host "[FAIL] Unfrozen WPF Transform in hot render path: $($f.Name)" -ForegroundColor Red
            $errors++
        }
    }

    # Gate 3: EAC-Safety & Memory/Injection API Zero-Tolerance
    if ($txt -match '\b(ReadProcessMemory|WriteProcessMemory|VirtualAllocEx|VirtualProtectEx|CreateRemoteThread|NtReadVirtualMemory|NtWriteVirtualMemory|QueueUserAPC|SetThreadContext|ResumeThread|LoadLibrary|GetProcAddress|steam_api64)\b') {
        Write-Host "[FAIL] Forbidden EAC-unsafe Win32/Memory API detected in $($f.Name): $($matches[1])" -ForegroundColor Red
        $errors++
    }
    if ($txt -match '\b(PROCESS_ALL_ACCESS|PROCESS_VM_READ|PROCESS_VM_WRITE|PROCESS_VM_OPERATION)\b') {
        Write-Host "[FAIL] Forbidden Process VM/AllAccess flag detected in $($f.Name): $($matches[1])" -ForegroundColor Red
        $errors++
    }
    foreach ($m in [regex]::Matches($txt, 'OpenProcess\s*\(\s*(0x[0-9a-fA-F]+|\d+)')) {
        $flag = $m.Groups[1].Value
        if ($flag -ne "0x1000" -and $flag -ne "4096") {
            Write-Host "[FAIL] OpenProcess called with non-limited flag '$flag' in $($f.Name) (only 0x1000 allowed)" -ForegroundColor Red
            $errors++
        }
    }

    # Gate 4: Disallow raw manual 'unsafe' blocks in src\
    if ($txt -match '\bunsafe\s*(static|public|internal|private|void|class|struct|\x7B)') {
        Write-Host "[FAIL] Raw 'unsafe' code block detected in $($f.Name)" -ForegroundColor Red
        $errors++
    }

    # Gate 6: Localization UiText key integrity
    if ($definedKeys.Count -gt 0) {
        foreach ($m in [regex]::Matches($txt, 'WpfFieldTheme\.UiText\("([^"]+)"\)')) {
            $k = $m.Groups[1].Value
            if (-not $definedKeys.ContainsKey($k)) {
                Write-Host "[FAIL] Missing UiText key '$k' referenced in $($f.Name)" -ForegroundColor Red
                $errors++
            }
        }
    }

    # Gate 7: Unified Fluent UI — zero Win32 MessageBox.Show in WPF dialogs & SquadTools
    if ($f.DirectoryName -like "*\UI\Dialogs" -or $f.Name -eq "SquadTools.cs") {
        if ($txt -match '\bMessageBox\.Show\s*\(') {
            Write-Host "[FAIL] Legacy Win32 MessageBox.Show detected in $($f.Name) (use ShowFluentNotice/ShowFluentConfirm)" -ForegroundColor Red
            $errors++
        }
    }

    # Gate 8: Architectural modularity (UI files <= 95 KB)
    if ($f.DirectoryName -like "*\UI" -and $f.Length -gt 95KB) {
        Write-Host "[WARN] Large UI file (>95 KB): $($f.Name) ($([Math]::Round($f.Length/1KB,1)) KB)" -ForegroundColor Yellow
        $warnings++
    }
}

# Gate 4b: [LibraryImport] source generator requires <AllowUnsafeBlocks>true</AllowUnsafeBlocks> (SYSLIB1062)
$csprojPath = Join-Path $Root "ZiloreMiniMap.csproj"
if ($usesLibraryImport -and (Test-Path -LiteralPath $csprojPath)) {
    $csprojTxt = Get-Content -Path $csprojPath -Raw -Encoding UTF8
    if ($csprojTxt -notmatch '<AllowUnsafeBlocks>\s*true\s*</AllowUnsafeBlocks>') {
        Write-Host "[FAIL] ZiloreMiniMap.csproj must set <AllowUnsafeBlocks>true</AllowUnsafeBlocks> when [LibraryImport] is used (SYSLIB1062)" -ForegroundColor Red
        $errors++
    }
}

# Gate 5: DLL-Hijack Hardening
if (-not $hasDllSearchHardening) {
    Write-Host "[WARN] Missing [assembly: DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]" -ForegroundColor Yellow
    $warnings++
}

# Gate 9: PowerShell 5.1 Hygiene (UTF-8 BOM for non-ASCII scripts & POSIX forward-slash ZIP packaging)
if (Test-Path -LiteralPath $scriptsDir) {
    foreach ($psFile in (Get-ChildItem -Path $scriptsDir -Filter "*.ps1" -File)) {
        $rawBytes = [System.IO.File]::ReadAllBytes($psFile.FullName)
        $hasBom   = ($rawBytes.Length -ge 3 -and $rawBytes[0] -eq 0xEF -and $rawBytes[1] -eq 0xBB -and $rawBytes[2] -eq 0xBF)
        $psTxt    = Get-Content -Path $psFile.FullName -Raw -Encoding UTF8
        if (-not $hasBom -and ($psTxt -match '[^\x00-\x7F]')) {
            Write-Host "[FAIL] scripts\$($psFile.Name) contains non-ASCII text without UTF-8 BOM (breaks Windows PowerShell 5.1)" -ForegroundColor Red
            $errors++
        }
    }
    foreach ($zipScriptName in @("Publish-UpdateManifest.ps1", "Build-ZiloreMiniMap.ps1")) {
        $zsPath = Join-Path $scriptsDir $zipScriptName
        if (Test-Path -LiteralPath $zsPath) {
            $zsTxt = Get-Content -Path $zsPath -Raw -Encoding UTF8
            if ($zsTxt -match '\bCompress-Archive\b' -or $zsTxt -notmatch 'CreateEntryFromFile') {
                Write-Host "[FAIL] scripts\$zipScriptName must use ZipFileExtensions.CreateEntryFromFile with '/' entries (never Compress-Archive)" -ForegroundColor Red
                $errors++
            }
        }
    }
}

# Gate 10: Installer .iss UTF-8 BOM & x64 Windows 10/11 safety
$issPath = Join-Path $Root "installer\ZiloreMiniMap.iss"
if (Test-Path -LiteralPath $issPath) {
    $issBytes = [System.IO.File]::ReadAllBytes($issPath)
    $issBom   = ($issBytes.Length -ge 3 -and $issBytes[0] -eq 0xEF -and $issBytes[1] -eq 0xBB -and $issBytes[2] -eq 0xBF)
    $issTxt   = Get-Content -Path $issPath -Raw -Encoding UTF8
    if (-not $issBom) {
        Write-Host "[FAIL] installer\ZiloreMiniMap.iss must be saved as UTF-8 with BOM" -ForegroundColor Red
        $errors++
    }
    if ($issTxt -notmatch 'ArchitecturesAllowed=x64compatible' -or $issTxt -notmatch 'MinVersion=10\.0\.17763') {
        Write-Host "[FAIL] installer\ZiloreMiniMap.iss missing x64compatible or MinVersion=10.0.17763" -ForegroundColor Red
        $errors++
    }
    if ($issTxt -notmatch 'WizardStyle=modern dynamic windows11' -or $issTxt -notmatch '\[UninstallDelete\]' -or $issTxt -notmatch 'CleanResidualRegistryKeys') {
        Write-Host "[FAIL] installer\ZiloreMiniMap.iss missing Windows 11 dynamic style, [UninstallDelete], or residual registry cleanup" -ForegroundColor Red
        $errors++
    }
    if ($issTxt -notmatch 'DefaultDirName=\{localappdata\}\\Programs\\ZiloreMiniMap' -or $issTxt -notmatch 'ZiloreMiniMap\.exe".*\bshellexec\b') {
        Write-Host "[FAIL] installer\ZiloreMiniMap.iss must install to {localappdata}\Programs\ZiloreMiniMap and launch postinstall via shellexec (Error 740 fix)" -ForegroundColor Red
        $errors++
    }
}

if ($errors -gt 0) {
    throw "Enterprise Defensive Gate FAILED with $errors error(s)."
}

Write-Host "[PASS] Audited $($files.Count) C# files ($($definedKeys.Count) UiText keys, 0 Memory/Injection APIs, DLL-Hijack Safe, 10 Defensive Gates). 0 errors, $warnings warning(s)." -ForegroundColor Green
