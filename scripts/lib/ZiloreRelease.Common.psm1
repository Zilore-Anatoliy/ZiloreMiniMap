<#
.SYNOPSIS
    Общая модульная библиотека продакшен-конвейера ZiloreMiniMap.
    Поддерживает аудит артефактов, пользовательские описания (RU/EN),
    синхронизацию папок релиза, update.txt, README.md, README_EN.md,
    а также актуальных страниц сайта website\index.html и website\en\index.html
    (с экспортом готовых HTML в release\website\).
    Совместима с Windows PowerShell 5.1 и PowerShell 7+.
#>

function Get-ZiloreVersionInfo {
    param([Parameter(Mandatory = $true)][string]$Root)
    $mmPath = Join-Path $Root "src\Engine\MiniMap.cs"
    if (-not (Test-Path -LiteralPath $mmPath)) {
        throw "Не найден файл версии: $mmPath"
    }
    $txt = [IO.File]::ReadAllText($mmPath, [Text.Encoding]::UTF8)
    $ver = ""
    $bld = ""
    $ts  = ""
    if ($txt -match 'public const string VersionString\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)";') { $ver = $matches[1] }
    if ($txt -match 'public const string BuildNumber\s*=\s*"([^"]+)";') { $bld = $matches[1] }
    if ($txt -match 'public const string BuildTimestamp\s*=\s*"([^"]+)";') { $ts = $matches[1] }
    if ([string]::IsNullOrWhiteSpace($ver)) {
        throw "Не удалось извлечь VersionString из $mmPath"
    }
    return [PSCustomObject]@{
        Version   = $ver
        Build     = $bld
        Timestamp = $ts
        Display   = ("v{0} Build ({1})" -f $ver, $bld)
    }
}

function Get-ZiloreReleaseArtifacts {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Version
    )
    $releaseDir = Join-Path $Root "release"
    $candidates = @(
        @{ Key = "InstallerExe";  Path = (Join-Path $releaseDir ("installer\ZiloreMiniMap-Setup-v{0}.exe" -f $Version)) },
        @{ Key = "PortableZip";   Path = (Join-Path $releaseDir ("portable\ZiloreMiniMap-Portable-v{0}.zip" -f $Version)) },
        @{ Key = "SingleFileExe"; Path = (Join-Path $releaseDir "portable\ZiloreMiniMap.exe") },
        @{ Key = "UpdateExe";     Path = (Join-Path $releaseDir "ZiloreMiniMap.exe") },
        @{ Key = "UpdateManifest";Path = (Join-Path $releaseDir "update.txt") },
        @{ Key = "UpdateSig";     Path = (Join-Path $releaseDir "update.txt.p7s") },
        @{ Key = "Sha256Sums";    Path = (Join-Path $releaseDir "SHA256SUMS.txt") },
        @{ Key = "WebsiteRuHtml"; Path = (Join-Path $releaseDir "website\index.html") },
        @{ Key = "WebsiteEnHtml"; Path = (Join-Path $releaseDir "website\en\index.html") }
    )

    $result = @{}
    foreach ($c in $candidates) {
        $exists = Test-Path -LiteralPath $c.Path
        $size = 0L
        $sha = ""
        if ($exists) {
            $fi = Get-Item -LiteralPath $c.Path
            $size = $fi.Length
            $sha = (Get-FileHash -LiteralPath $c.Path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        $result[$c.Key] = [PSCustomObject]@{
            Key    = $c.Key
            Path   = $c.Path
            Exists = $exists
            Size   = $size
            Sha256 = $sha
        }
    }
    return $result
}

function Test-ZiloreAuthenticodeStatus {
    param([Parameter(Mandatory = $true)][string]$FilePath)
    if (-not (Test-Path -LiteralPath $FilePath)) {
        return [PSCustomObject]@{ Path = $FilePath; Status = "Missing"; Subject = ""; Timestamped = $false }
    }
    $sig = Get-AuthenticodeSignature -LiteralPath $FilePath
    $subj = ""
    if ($null -ne $sig.SignerCertificate) { $subj = $sig.SignerCertificate.Subject }
    $hasTs = ($null -ne $sig.TimeStamperCertificate)
    return [PSCustomObject]@{
        Path        = $FilePath
        Status      = $sig.Status.ToString()
        Subject     = $subj
        Timestamped = $hasTs
    }
}

function Update-ZiloreSha256Sums {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Version
    )
    $releaseDir = Join-Path $Root "release"
    if (-not (Test-Path -LiteralPath $releaseDir)) { return $null }
    $targets = @(
        (Join-Path $releaseDir ("installer\ZiloreMiniMap-Setup-v{0}.exe" -f $Version)),
        (Join-Path $releaseDir ("portable\ZiloreMiniMap-Portable-v{0}.zip" -f $Version)),
        (Join-Path $releaseDir "ZiloreMiniMap.exe"),
        (Join-Path $releaseDir "update.txt")
    )
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($t in $targets) {
        if (Test-Path -LiteralPath $t) {
            $h = (Get-FileHash -LiteralPath $t -Algorithm SHA256).Hash.ToLowerInvariant()
            $name = Split-Path $t -Leaf
            $lines.Add(("{0}  {1}" -f $h, $name))
        }
    }
    $outPath = Join-Path $releaseDir "SHA256SUMS.txt"
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllLines($outPath, $lines.ToArray(), $utf8NoBom)
    return $outPath
}

function Write-ZiloreAuditReport {
    param(
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [Parameter(Mandatory = $true)][hashtable]$ReportData
    )
    $dir = Split-Path $OutputPath -Parent
    if (-not [string]::IsNullOrWhiteSpace($dir) -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $json = $ReportData | ConvertTo-Json -Depth 6
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($OutputPath, $json, $utf8NoBom)
    return $OutputPath
}

function Format-ZiloreUserReleaseNotes {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [ValidateSet("ru", "en")]
        [string]$Language = "ru",
        [int]$MaxManifestChars = 1400
    )
    $fileName = if ($Language -eq "en") { "release-notes.en.txt" } else { "release-notes.txt" }
    $rnPath = Join-Path $Root $fileName
    if ($Language -eq "en" -and -not (Test-Path -LiteralPath $rnPath)) {
        $rnPath = Join-Path $Root "release-notes.txt"
    }

    $rawLines = @()
    if (Test-Path -LiteralPath $rnPath) {
        $rawLines = [IO.File]::ReadAllLines($rnPath, [Text.Encoding]::UTF8)
    }

    $cleanLines = New-Object System.Collections.Generic.List[string]
    $mdLines    = New-Object System.Collections.Generic.List[string]
    $htmlItems  = New-Object System.Collections.Generic.List[string]

    foreach ($raw in $rawLines) {
        $line = $raw.Trim()
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line.StartsWith("#")) { continue }

        # Удаляем случайный технический шум (пути к исходникам, фазы, маркеры Tier-1 / P0)
        $line = [regex]::Replace($line, '\bTier-\d+\s*(Enterprise)?\b', '', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        $line = [regex]::Replace($line, '\bP[0-3]\b\s*:?', '')
        $line = [regex]::Replace($line, '\((?:src|scripts|cloud|docs|installer)\\[^)]+\)', '')
        $line = [regex]::Replace($line, '\b(?:src|scripts|cloud|docs|installer)\\[^\s,;)]+', '')
        $line = [regex]::Replace($line, '\s{2,}', ' ').Trim()
        if ([string]::IsNullOrWhiteSpace($line)) { continue }

        if ($line -match '^\[(.+)\]$') {
            $sec = $matches[1].Trim()
            if ($cleanLines.Count -gt 0) {
                $cleanLines.Add("")
                $mdLines.Add("")
            }
            $cleanLines.Add($sec + ":")
            $mdLines.Add("**" + $sec + ":**")
        } else {
            $bullet = $line.TrimStart("•", "-", "–", "*", " ").Trim()
            if (-not [string]::IsNullOrWhiteSpace($bullet)) {
                $cleanLines.Add("• " + $bullet)
                $mdLines.Add("- " + $bullet)
                $enc = [System.Net.WebUtility]::HtmlEncode($bullet)
                $htmlItems.Add("              <li>" + $enc + "</li>")
            }
        }
    }

    if ($cleanLines.Count -eq 0) {
        $fallback = if ($Language -eq "en") {
            "Performance, stability improvements, and bug fixes."
        } else {
            "Улучшения стабильности, производительности и исправления ошибок."
        }
        $cleanLines.Add("• " + $fallback)
        $mdLines.Add("- " + $fallback)
        $htmlItems.Add("              <li>" + [System.Net.WebUtility]::HtmlEncode($fallback) + "</li>")
    }

    $plain = ($cleanLines.ToArray() -join [Environment]::NewLine).Trim()
    $md    = ($mdLines.ToArray() -join "`n").Trim()
    $html  = ($htmlItems.ToArray() -join "`n")

    # Формат для update.txt: одна строка с разделителями \n и ограничением длины
    $manifestList = New-Object System.Collections.Generic.List[string]
    $accLen = 0
    foreach ($cl in $cleanLines) {
        if ([string]::IsNullOrWhiteSpace($cl)) { continue }
        $piece = $cl.Replace("\", "/").Replace("`r", "").Replace("`n", " ")
        $extra = if ($manifestList.Count -gt 0) { 2 } else { 0 }
        if (($accLen + $extra + $piece.Length) -gt $MaxManifestChars) { break }
        $manifestList.Add($piece)
        $accLen += $extra + $piece.Length
    }
    $manifestNotes = ($manifestList.ToArray() -join "\n")

    $shortSummary = ""
    foreach ($cl in $cleanLines) {
        if ($cl.StartsWith("• ")) { $shortSummary = $cl; break }
    }
    if ([string]::IsNullOrWhiteSpace($shortSummary)) { $shortSummary = $cleanLines[0] }

    return [PSCustomObject]@{
        Language            = $Language
        PlainText           = $plain
        MarkdownText        = $md
        HtmlListItems       = $html
        UpdateManifestNotes = $manifestNotes
        ShortSummary        = $shortSummary
    }
}

function Sync-ZiloreReleaseFolderNotes {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Version,
        [string]$Build = "",
        [switch]$DryRun
    )
    $releaseDir = Join-Path $Root "release"
    if (-not (Test-Path -LiteralPath $releaseDir)) {
        return [PSCustomObject]@{ UpdatedFiles = @(); UpdateTxtChanged = $false; NeedsManifestResign = $false; Notes = (Format-ZiloreUserReleaseNotes -Root $Root -Language "ru") }
    }

    $notes = Format-ZiloreUserReleaseNotes -Root $Root -Language "ru"
    $headerVer = if ([string]::IsNullOrWhiteSpace($Build)) { ("v{0}" -f $Version) } else { ("v{0} (сборка {1})" -f $Version, $Build) }
    $utf8Bom = New-Object System.Text.UTF8Encoding($true)
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $updated = New-Object System.Collections.Generic.List[string]
    $updateTxtChanged = $false

    # 1. Короткий файл с описанием изменений в корне release\
    $rootNotesFile = Join-Path $releaseDir "RELEASE-NOTES.txt"
    $rootNotesBody = ("ZiloreMiniMap {0}`r`n`r`n{1}`r`n" -f $headerVer, $notes.PlainText)
    if (-not $DryRun) {
        [IO.File]::WriteAllText($rootNotesFile, $rootNotesBody, $utf8Bom)
    }
    $updated.Add($rootNotesFile)

    # 2. Понятные памятки в подпапках готового билда (portable\ и installer\)
    $portableDir = Join-Path $releaseDir "portable"
    if (Test-Path -LiteralPath $portableDir) {
        $portableReadme = Join-Path $portableDir "README.txt"
        $portableBody = @"
ZiloreMiniMap $headerVer — Портативная версия

Как запустить:
1. Распакуйте весь архив в любую удобную папку (рядом должна остаться папка assets).
2. Запустите ZiloreMiniMap.exe. Установка не требуется.

Что нового:
$($notes.PlainText)

Сайт: https://minimap.zilore.app
"@
        if (-not $DryRun) {
            [IO.File]::WriteAllText($portableReadme, ($portableBody.Trim() + "`r`n"), $utf8Bom)
        }
        $updated.Add($portableReadme)
    }

    $installerDir = Join-Path $releaseDir "installer"
    if (Test-Path -LiteralPath $installerDir) {
        $installerReadme = Join-Path $installerDir "README.txt"
        $installerBody = @"
ZiloreMiniMap $headerVer — Установщик для Windows 10 / 11

Как установить:
1. Запустите ZiloreMiniMap-Setup-v$Version.exe и следуйте шагам мастера.
2. При обновлении поверх старой версии все ваши настройки, метки и маршруты сохранятся.

Что нового:
$($notes.PlainText)

Сайт: https://minimap.zilore.app
"@
        if (-not $DryRun) {
            [IO.File]::WriteAllText($installerReadme, ($installerBody.Trim() + "`r`n"), $utf8Bom)
        }
        $updated.Add($installerReadme)
    }

    # 3. Обновление строки notes= в release\update.txt без нарушения формата UpdateRelease.Parse
    $updateTxt = Join-Path $releaseDir "update.txt"
    if (Test-Path -LiteralPath $updateTxt) {
        $origLines = [IO.File]::ReadAllLines($updateTxt, [Text.Encoding]::UTF8)
        $outLines = New-Object System.Collections.Generic.List[string]
        $foundNotes = $false
        foreach ($ln in $origLines) {
            $trimmed = $ln.TrimStart([char]0xFEFF)
            if ($trimmed.StartsWith("notes=", [StringComparison]::Ordinal)) {
                $outLines.Add("notes=" + $notes.UpdateManifestNotes)
                $foundNotes = $true
            } elseif (-not [string]::IsNullOrWhiteSpace($trimmed)) {
                $outLines.Add($trimmed)
            }
        }
        if (-not $foundNotes) {
            $outLines.Add("notes=" + $notes.UpdateManifestNotes)
        }
        $newContent = ($outLines.ToArray() -join "`n") + "`n"
        $origContent = [IO.File]::ReadAllText($updateTxt, [Text.Encoding]::UTF8)
        if ($newContent -ne $origContent) {
            $updateTxtChanged = $true
            if (-not $DryRun) {
                [IO.File]::WriteAllText($updateTxt, $newContent, $utf8NoBom)
            }
            $updated.Add($updateTxt)
        }
    }

    $sigExists = Test-Path -LiteralPath (Join-Path $releaseDir "update.txt.p7s")
    return [PSCustomObject]@{
        UpdatedFiles        = $updated.ToArray()
        UpdateTxtChanged    = $updateTxtChanged
        NeedsManifestResign = ($updateTxtChanged -and $sigExists)
        Notes               = $notes
    }
}

function Update-ZiloreVersionTokens {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Version,
        [string]$Build = ""
    )
    $res = $Content
    # Обновляем ссылки скачивания и имена файлов установщика / портативного архива
    $res = [regex]::Replace($res, 'ZiloreMiniMap-Setup-v[0-9]+\.[0-9]+\.[0-9]+\.exe', ("ZiloreMiniMap-Setup-v{0}.exe" -f $Version))
    $res = [regex]::Replace($res, 'ZiloreMiniMap-Portable-v[0-9]+\.[0-9]+\.[0-9]+\.zip', ("ZiloreMiniMap-Portable-v{0}.zip" -f $Version))
    $res = [regex]::Replace($res, 'releases/download/v[0-9]+\.[0-9]+\.[0-9]+/', ("releases/download/v{0}/" -f $Version))
    $res = [regex]::Replace($res, 'releases/tag/v[0-9]+\.[0-9]+\.[0-9]+', ("releases/tag/v{0}" -f $Version))
    $res = [regex]::Replace($res, '(badge/version-v)[0-9]+\.[0-9]+\.[0-9]+', ('${1}' + $Version), [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    $res = [regex]::Replace($res, '(badge/release-v)[0-9]+\.[0-9]+\.[0-9]+', ('${1}' + $Version), [Text.RegularExpressions.RegexOptions]::IgnoreCase)

    # Обновляем актуальные HTML-токены версии (не затрагивая исторический таймлайн прошлых версий)
    $res = [regex]::Replace($res, '(itemprop="softwareVersion"\s+content=")[0-9]+\.[0-9]+\.[0-9]+(")', ('${1}' + $Version + '${2}'))
    $res = [regex]::Replace($res, '(<span\s+class="version-tag">v)[0-9]+\.[0-9]+\.[0-9]+(</span>)', ('${1}' + $Version + '${2}'))
    $res = [regex]::Replace($res, '(ZiloreMiniMap\s+v)[0-9]+\.[0-9]+\.[0-9]+', ('${1}' + $Version))
    $res = [regex]::Replace($res, '(Скачать установщик\s+v)[0-9]+\.[0-9]+\.[0-9]+', ('${1}' + $Version))
    $res = [regex]::Replace($res, '(Download Installer\s+v)[0-9]+\.[0-9]+\.[0-9]+', ('${1}' + $Version))
    $res = [regex]::Replace($res, '(⬇\s*(?:Скачать|Download)\s+v)[0-9]+\.[0-9]+\.[0-9]+', ('${1}' + $Version))
    $res = [regex]::Replace($res, '(Функциональность\s+v)[0-9]+\.[0-9]+\.[0-9]+', ('${1}' + $Version))
    $res = [regex]::Replace($res, '(Features\s*·\s*v)[0-9]+\.[0-9]+\.[0-9]+', ('${1}' + $Version))
    $res = [regex]::Replace($res, '(GitHub Releases\s+v)[0-9]+\.[0-9]+\.[0-9]+', ('${1}' + $Version))
    $res = [regex]::Replace($res, '(>(?:Скачать|Download)\s+v)[0-9]+\.[0-9]+\.[0-9]+(</a>)', ('${1}' + $Version + '${2}'))
    $res = [regex]::Replace($res, '(data-version=")v?[0-9]+\.[0-9]+\.[0-9]+(")', ('${1}v' + $Version + '${2}'))

    if (-not [string]::IsNullOrWhiteSpace($Build)) {
        $res = [regex]::Replace($res, '(v)[0-9]+\.[0-9]+\.[0-9]+(\s+[Bb]uild\s*\()[0-9]+(\))', ('${1}' + $Version + '${2}' + $Build + '${3}'))
        $res = [regex]::Replace($res, '(v)[0-9]+\.[0-9]+\.[0-9]+(\s*\(build\s+)[0-9]+(\))', ('${1}' + $Version + '${2}' + $Build + '${3}'), [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        $res = [regex]::Replace($res, '(data-build=")[0-9]+(")', ('${1}' + $Build + '${2}'))
        $res = [regex]::Replace($res, '(\.css\?v=|\.svg\?v=|\.ico\?v=|\.webmanifest\?v=)[0-9A-Za-z._-]+', ('${1}' + $Build))
    }
    return $res
}

function Update-ZiloreMarkdownReadme {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string]$Version,
        [string]$Build = "",
        [Parameter(Mandatory = $true)]$NotesObj,
        [ValidateSet("ru", "en")]
        [string]$Language = "ru",
        [switch]$DryRun
    )
    if (-not (Test-Path -LiteralPath $FilePath)) {
        return [PSCustomObject]@{ Path = $FilePath; Found = $false; Changed = $false; NotesSynced = $false }
    }

    $orig = [IO.File]::ReadAllText($FilePath, [Text.Encoding]::UTF8)
    $updated = Update-ZiloreVersionTokens -Content $orig -Version $Version -Build $Build
    $notesSynced = $false

    $headerVer = if ([string]::IsNullOrWhiteSpace($Build)) { ("v{0}" -f $Version) } else { ("v{0} build ({1})" -f $Version, $Build) }
    $headingTitle = if ($Language -eq "en") { "What's New in $headerVer" } else { "Что нового в $headerVer" }
    $blockBody = "<!-- RELEASE_NOTES_START -->`n### $headingTitle`n`n$($NotesObj.MarkdownText)`n<!-- RELEASE_NOTES_END -->"

    if ($updated -match '(?s)<!--\s*RELEASE_NOTES_START\s*-->.*?<!--\s*RELEASE_NOTES_END\s*-->') {
        $updated = [regex]::Replace(
            $updated,
            '(?s)<!--\s*RELEASE_NOTES_START\s*-->.*?<!--\s*RELEASE_NOTES_END\s*-->',
            { param($m) $blockBody }
        )
        $notesSynced = $true
    } else {
        $secPattern = if ($Language -eq "en") {
            '(?ms)(^#{2,3}\s+[^\r\n]*(?:What''s New|Latest Release|Changelog|Release Notes)[^\r\n]*\r?\n)(.*?)(?=^#{1,3}\s|\z)'
        } else {
            '(?ms)(^#{2,3}\s+[^\r\n]*(?:Что нового|Последний релиз|Список изменений|История изменений)[^\r\n]*\r?\n)(.*?)(?=^#{1,3}\s|\z)'
        }
        if ([regex]::IsMatch($updated, $secPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            $updated = [regex]::Replace(
                $updated,
                $secPattern,
                { param($m) $m.Groups[1].Value + "`n" + $blockBody + "`n`n" },
                [Text.RegularExpressions.RegexOptions]::IgnoreCase
            )
            $notesSynced = $true
        }
    }

    $changed = ($updated -ne $orig)
    if ($changed -and -not $DryRun) {
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [IO.File]::WriteAllText($FilePath, $updated, $utf8NoBom)
    }
    return [PSCustomObject]@{ Path = $FilePath; Found = $true; Changed = $changed; NotesSynced = $notesSynced }
}

function Find-ZiloreWebsiteRoots {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [string]$WebsiteDir = "",
        [string]$ExplicitPath = ""
    )
    $roots = New-Object System.Collections.Generic.List[string]
    $seen = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)

    $addDir = {
        param([string]$dirPath)
        if ([string]::IsNullOrWhiteSpace($dirPath)) { return }
        if (-not (Test-Path -LiteralPath $dirPath)) { return }
        $full = (Resolve-Path -LiteralPath $dirPath).Path
        $hasRu = Test-Path -LiteralPath (Join-Path $full "index.html")
        $hasEn = Test-Path -LiteralPath (Join-Path $full "en\index.html")
        if (($hasRu -or $hasEn) -and $seen.Add($full)) {
            $roots.Add($full)
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($WebsiteDir)) {
        & $addDir $WebsiteDir
    }
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath) -and (Test-Path -LiteralPath $ExplicitPath)) {
        $resolved = (Resolve-Path -LiteralPath $ExplicitPath).Path
        if (Test-Path -LiteralPath $resolved -PathType Container) {
            & $addDir $resolved
        } else {
            $parent = Split-Path $resolved -Parent
            if ((Split-Path $parent -Leaf) -ieq "en" -and (Test-Path -LiteralPath (Join-Path (Split-Path $parent -Parent) "index.html"))) {
                & $addDir (Split-Path $parent -Parent)
            } else {
                & $addDir $parent
            }
        }
    }

    foreach ($sub in @("website", "web", "site", "docs", "cloud", "")) {
        $cand = if ([string]::IsNullOrWhiteSpace($sub)) { $Root } else { Join-Path $Root $sub }
        & $addDir $cand
    }

    return $roots.ToArray()
}

function Find-ZiloreIndexHtml {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [string]$ExplicitPath = ""
    )
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath) -and (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }
    $dirs = Find-ZiloreWebsiteRoots -Root $Root -ExplicitPath $ExplicitPath
    foreach ($d in $dirs) {
        $idx = Join-Path $d "index.html"
        if (Test-Path -LiteralPath $idx) { return $idx }
    }
    return $null
}

function Update-ZiloreIndexHtml {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string]$Version,
        [string]$Build = "",
        [Parameter(Mandatory = $true)]$RuNotes,
        [Parameter(Mandatory = $true)]$EnNotes,
        [switch]$DryRun
    )
    if (-not (Test-Path -LiteralPath $FilePath)) {
        return [PSCustomObject]@{ Path = $FilePath; Found = $false; Changed = $false; NotesSynced = $false }
    }

    $orig = [IO.File]::ReadAllText($FilePath, [Text.Encoding]::UTF8)
    $updated = Update-ZiloreVersionTokens -Content $orig -Version $Version -Build $Build
    $notesSynced = $false

    # 1. Обновляем маркеры HTML-комментариев (сохраняем сами маркеры для всех будущих релизов)
    $markerPairs = @(
        @{ Start = 'RELEASE_NOTES_RU_START'; End = 'RELEASE_NOTES_RU_END'; Html = $RuNotes.HtmlListItems },
        @{ Start = 'RELEASE_NOTES_EN_START'; End = 'RELEASE_NOTES_EN_END'; Html = $EnNotes.HtmlListItems },
        @{ Start = 'RELEASE_NOTES_START';    End = 'RELEASE_NOTES_END';    Html = $RuNotes.HtmlListItems }
    )
    foreach ($mp in $markerPairs) {
        $pat = '(?s)(<!--\s*' + $mp.Start + '\s*-->).*?(<!--\s*' + $mp.End + '\s*-->)'
        if ([regex]::IsMatch($updated, $pat)) {
            $innerHtml = $mp.Html
            $updated = [regex]::Replace($updated, $pat, { param($m) $m.Groups[1].Value + "`n" + $innerHtml + "`n              " + $m.Groups[2].Value })
            $notesSynced = $true
        }
    }

    # 2. Фоллбек по id списка (выполняется ТОЛЬКО если в файле нет HTML-комментариев маркеров)
    if (-not $notesSynced) {
        $ulTargets = @(
            @{ Id = 'release-notes-ru'; Html = $RuNotes.HtmlListItems },
            @{ Id = 'release-notes-en'; Html = $EnNotes.HtmlListItems },
            @{ Id = 'release-notes';    Html = $RuNotes.HtmlListItems },
            @{ Id = 'changelog-list';   Html = $RuNotes.HtmlListItems }
        )
        foreach ($ut in $ulTargets) {
            $pat = '(?is)(<ul\b[^>]*\bid=["'']' + $ut.Id + '["''][^>]*>).*?(</ul>)'
            if ([regex]::IsMatch($updated, $pat)) {
                $innerHtml = $ut.Html
                $updated = [regex]::Replace($updated, $pat, { param($m) $m.Groups[1].Value + "`n" + $innerHtml + "`n            " + $m.Groups[2].Value })
                $notesSynced = $true
            }
        }
    }

    # 3. Обновляем даты актуального релиза (ISO YYYY-MM-DD и RU DD.MM.YYYY)
    $now = Get-Date
    $todayIso = $now.ToString("yyyy-MM-dd")
    $todayRu  = $now.ToString("dd.MM.yyyy")
    $updated = [regex]::Replace($updated, '(data-release-date=")[0-9]{4}-[0-9]{2}-[0-9]{2}(")', ('${1}' + $todayIso + '${2}'))
    $updated = [regex]::Replace($updated, '(itemprop="dateModified"\s+content=")[0-9]{4}-[0-9]{2}-[0-9]{2}(")', ('${1}' + $todayIso + '${2}'))
    $updated = [regex]::Replace($updated, '(<span\s+class="release-date">)[0-9]{4}-[0-9]{2}-[0-9]{2}(</span>)', ('${1}' + $todayIso + '${2}'))
    $updated = [regex]::Replace($updated, '(Актуальный релиз\s*·\s*)[0-9]{2}\.[0-9]{2}\.[0-9]{4}', ('${1}' + $todayRu))
    $updated = [regex]::Replace($updated, '(Latest Release\s*·\s*)[0-9]{4}-[0-9]{2}-[0-9]{2}', ('${1}' + $todayIso))
    if (-not [string]::IsNullOrWhiteSpace($Build)) {
        $updated = [regex]::Replace($updated, '(v' + [regex]::Escape($Version) + '\s+build\s*\(' + [regex]::Escape($Build) + '\)\s*<span>)[0-9]{2}\.[0-9]{2}\.[0-9]{4}(</span>)', ('${1}' + $todayRu + '${2}'))
        $updated = [regex]::Replace($updated, '(v' + [regex]::Escape($Version) + '\s+build\s*\(' + [regex]::Escape($Build) + '\)\s*<span>)[0-9]{4}-[0-9]{2}-[0-9]{2}(</span>)', ('${1}' + $todayIso + '${2}'))
    }

    $changed = ($updated -ne $orig)
    if ($changed -and -not $DryRun) {
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [IO.File]::WriteAllText($FilePath, $updated, $utf8NoBom)
    }
    return [PSCustomObject]@{ Path = $FilePath; Found = $true; Changed = $changed; NotesSynced = $notesSynced }
}

function Sync-ZiloreDocsAndWeb {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Version,
        [string]$Build = "",
        [string]$WebsiteDir = "",
        [string]$IndexHtmlPath = "",
        [switch]$ExportToRelease,
        [switch]$DryRun
    )
    $ruNotes = Format-ZiloreUserReleaseNotes -Root $Root -Language "ru"
    $enNotes = Format-ZiloreUserReleaseNotes -Root $Root -Language "en"

    $results = New-Object System.Collections.Generic.List[object]

    $readmeRu = Join-Path $Root "README.md"
    if (Test-Path -LiteralPath $readmeRu) {
        $results.Add((Update-ZiloreMarkdownReadme -FilePath $readmeRu -Version $Version -Build $Build -NotesObj $ruNotes -Language "ru" -DryRun:$DryRun))
    }

    $readmeEn = Join-Path $Root "README_EN.md"
    if (Test-Path -LiteralPath $readmeEn) {
        $results.Add((Update-ZiloreMarkdownReadme -FilePath $readmeEn -Version $Version -Build $Build -NotesObj $enNotes -Language "en" -DryRun:$DryRun))
    }

    $webRoots = Find-ZiloreWebsiteRoots -Root $Root -WebsiteDir $WebsiteDir -ExplicitPath $IndexHtmlPath
    $primaryRuHtml = $null
    $primaryEnHtml = $null
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $todayIso = (Get-Date).ToString("yyyy-MM-dd")

    foreach ($wr in $webRoots) {
        $ruPath = Join-Path $wr "index.html"
        if (Test-Path -LiteralPath $ruPath) {
            if ($null -eq $primaryRuHtml) { $primaryRuHtml = $ruPath }
            $results.Add((Update-ZiloreIndexHtml -FilePath $ruPath -Version $Version -Build $Build -RuNotes $ruNotes -EnNotes $enNotes -DryRun:$DryRun))
        }
        $enPath = Join-Path $wr "en\index.html"
        if (Test-Path -LiteralPath $enPath) {
            if ($null -eq $primaryEnHtml) { $primaryEnHtml = $enPath }
            $results.Add((Update-ZiloreIndexHtml -FilePath $enPath -Version $Version -Build $Build -RuNotes $ruNotes -EnNotes $enNotes -DryRun:$DryRun))
        }
        # Опционально обновляем сопутствующие 404.html, sitemap.xml и tactical-preview.svg если они рядом
        $p404 = Join-Path $wr "404.html"
        if (Test-Path -LiteralPath $p404) {
            $orig404 = [IO.File]::ReadAllText($p404, [Text.Encoding]::UTF8)
            $upd404 = Update-ZiloreVersionTokens -Content $orig404 -Version $Version -Build $Build
            $chg404 = ($upd404 -ne $orig404)
            if ($chg404 -and -not $DryRun) { [IO.File]::WriteAllText($p404, $upd404, $utf8NoBom) }
            $results.Add([PSCustomObject]@{ Path = $p404; Found = $true; Changed = $chg404; NotesSynced = $true })
        }
        $pSitemap = Join-Path $wr "sitemap.xml"
        if (Test-Path -LiteralPath $pSitemap) {
            $origSm = [IO.File]::ReadAllText($pSitemap, [Text.Encoding]::UTF8)
            $updSm = [regex]::Replace($origSm, '(<lastmod>)[0-9]{4}-[0-9]{2}-[0-9]{2}(</lastmod>)', ('${1}' + $todayIso + '${2}'))
            $chgSm = ($updSm -ne $origSm)
            if ($chgSm -and -not $DryRun) { [IO.File]::WriteAllText($pSitemap, $updSm, $utf8NoBom) }
            $results.Add([PSCustomObject]@{ Path = $pSitemap; Found = $true; Changed = $chgSm; NotesSynced = $true })
        }
        $pSvg = Join-Path $wr "img\tactical-preview.svg"
        if (Test-Path -LiteralPath $pSvg) {
            $origSvg = [IO.File]::ReadAllText($pSvg, [Text.Encoding]::UTF8)
            $updSvg = [regex]::Replace($origSvg, '(>v)[0-9]+\.[0-9]+\.[0-9]+(</text>)', ('${1}' + $Version + '${2}'))
            $chgSvg = ($updSvg -ne $origSvg)
            if ($chgSvg -and -not $DryRun) { [IO.File]::WriteAllText($pSvg, $updSvg, $utf8NoBom) }
            $results.Add([PSCustomObject]@{ Path = $pSvg; Found = $true; Changed = $chgSvg; NotesSynced = $true })
        }
    }

    # Экспорт актуальных index.html и en\index.html прямо в release\website\ готового билда
    $releaseDir = Join-Path $Root "release"
    if ($ExportToRelease -and (Test-Path -LiteralPath $releaseDir)) {
        if ($null -ne $primaryRuHtml) {
            $dstRu = Join-Path $releaseDir "website\index.html"
            if (-not $DryRun) {
                $dstDir = Split-Path $dstRu -Parent
                if (-not (Test-Path -LiteralPath $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
                Copy-Item -LiteralPath $primaryRuHtml -Destination $dstRu -Force
            }
            $results.Add([PSCustomObject]@{ Path = $dstRu; Found = $true; Changed = $true; NotesSynced = $true })
        }
        if ($null -ne $primaryEnHtml) {
            $dstEn = Join-Path $releaseDir "website\en\index.html"
            if (-not $DryRun) {
                $dstDir = Split-Path $dstEn -Parent
                if (-not (Test-Path -LiteralPath $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
                Copy-Item -LiteralPath $primaryEnHtml -Destination $dstEn -Force
            }
            $results.Add([PSCustomObject]@{ Path = $dstEn; Found = $true; Changed = $true; NotesSynced = $true })
        }
    }

    return [PSCustomObject]@{
        Items   = $results.ToArray()
        RuNotes = $ruNotes
        EnNotes = $enNotes
    }
}

Export-ModuleMember -Function @(
    "Get-ZiloreVersionInfo",
    "Get-ZiloreReleaseArtifacts",
    "Test-ZiloreAuthenticodeStatus",
    "Update-ZiloreSha256Sums",
    "Write-ZiloreAuditReport",
    "Format-ZiloreUserReleaseNotes",
    "Sync-ZiloreReleaseFolderNotes",
    "Find-ZiloreWebsiteRoots",
    "Find-ZiloreIndexHtml",
    "Sync-ZiloreDocsAndWeb"
)
