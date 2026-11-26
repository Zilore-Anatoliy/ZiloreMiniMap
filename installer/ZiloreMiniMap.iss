#ifndef AppVersion
  #define AppVersion "0.0.5"
#endif
#ifndef SourceDir
  #define SourceDir "..\release"
#endif
#ifndef OutputDir
  #define OutputDir "..\release\installer"
#endif

[Setup]
AppId={{8F4C2E91-5B3A-4D7E-9A1C-7E6D5F4A3B21}
AppName=ZiloreMiniMap
AppVersion={#AppVersion}
AppVerName=ZiloreMiniMap v{#AppVersion}
AppPublisher=Zilore
AppPublisherURL=https://minimap.zilore.app
AppSupportURL=https://github.com/Zilore-Anatoliy/ZiloreMiniMap/issues
AppUpdatesURL=https://github.com/Zilore-Anatoliy/ZiloreMiniMap/releases
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany=Zilore
VersionInfoProductName=ZiloreMiniMap
VersionInfoDescription=ZiloreMiniMap Tactical Map & GPS Installer
UninstallDisplayName=ZiloreMiniMap v{#AppVersion}
UninstallDisplayIcon={app}\icons\uninstall.ico
SetupIconFile=setup.ico
WizardImageFile=wizard-sidebar.bmp
WizardSmallImageFile=wizard-small.bmp
WizardStyle=modern dynamic windows11
WizardBackColor=#FFFFFF
WizardBackColorDynamicDark=#0B1017
WizardImageBackColor=#0B1017
WizardImageBackColorDynamicDark=#0B1017
WizardSizePercent=115,115
DefaultDirName={localappdata}\Programs\ZiloreMiniMap
DefaultGroupName=ZiloreMiniMap
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=ZiloreMiniMap-Setup-v{#AppVersion}
Compression=lzma2/normal
LZMANumBlockThreads=4
SolidCompression=yes
AppMutex=Local\ZiloreMiniMapOverlay
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=yes
ChangesAssociations=yes
DisableProgramGroupPage=yes
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"; LicenseFile: "..\docs\EULA-RU.txt"
Name: "english"; MessagesFile: "compiler:Default.isl"; LicenseFile: "..\docs\EULA-EN.txt"

[CustomMessages]
russian.GroupShortcuts=Ярлыки и интеграция с Windows:
english.GroupShortcuts=Shortcuts & Windows Integration:
russian.GroupMaintenance=Обслуживание и защита профиля:
english.GroupMaintenance=Maintenance & Profile Safety:
russian.TaskDesktop=Создать ярлык на Рабочем столе
english.TaskDesktop=Create a Desktop shortcut
russian.TaskStartup=Запускать ZiloreMiniMap вместе с Windows
english.TaskStartup=Launch ZiloreMiniMap on Windows startup
russian.TaskAssocTsv=Ассоциировать файлы зон (.tsv) с ZiloreMiniMap
english.TaskAssocTsv=Associate zone files (.tsv) with ZiloreMiniMap
russian.TaskBackupProfile=Сохранить резервную копию профиля на Рабочий стол
english.TaskBackupProfile=Back up current user profile to Desktop before upgrading
russian.TaskCleanCache=Очистить устаревший кэш обновлений и %TEMP%
english.TaskCleanCache=Clean stale update cache and %TEMP% files
russian.TaskMigratePf=Удалить старую копию из Program Files (профиль сохранится)
english.TaskMigratePf=Remove legacy copy from Program Files (profile preserved)
russian.LaunchReleases=Открыть страницу релизов и изменений на GitHub
english.LaunchReleases=Open GitHub Releases & Changelog page
russian.DiagTitle=Диагностика системы, SCUM и профиля
english.DiagTitle=System, SCUM & Profile Diagnostics
russian.DiagSubTitle=Проверка ОС Windows 10/11, GPU, библиотек Microsoft, библиотек Steam и сохранённого профиля.
english.DiagSubTitle=Checking Windows 10/11, GPU, Microsoft runtimes, Steam libraries, and saved user profile.
russian.DiagAutoInstall=Автоматически скачать и установить отсутствующие компоненты Microsoft (VC++ 2015–2022 x64 / DirectX)
english.DiagAutoInstall=Automatically download and install missing Microsoft runtimes (VC++ 2015–2022 x64 / DirectX)
russian.DiagAllOk=Все обязательные системные компоненты установлены — дополнительная загрузка не требуется.
english.DiagAllOk=All required system runtimes are installed — no extra downloads needed.
russian.DiagRecheck=Проверить снова
english.DiagRecheck=Re-check
russian.DiagSaveReport=Сохранить отчёт
english.DiagSaveReport=Save Report
russian.DiagSavedMsg=Отчёт диагностики сохранён на Рабочий стол:%n%1
english.DiagSavedMsg=Diagnostic report saved to Desktop:%n%1
russian.DownloadTitle=Загрузка системных пакетов Microsoft
english.DownloadTitle=Downloading Microsoft System Packages
russian.DownloadSubTitle=Пожалуйста, подождите, пока мастер загружает официальные системные компоненты…
english.DownloadSubTitle=Please wait while Setup downloads official Microsoft runtime packages…
russian.InstallPrereqError=Не удалось автоматически установить системные компоненты (%1).%n%nВы можете установить Microsoft Visual C++ 2015–2022 x64 позже вручную. Продолжить установку ZiloreMiniMap?
english.InstallPrereqError=Could not automatically install system packages (%1).%n%nYou can install Microsoft Visual C++ 2015–2022 x64 manually later. Continue installing ZiloreMiniMap?
russian.UnsupportedOs=Для запуска ZiloreMiniMap требуется 64-разрядная ОС Windows 10 (1809+) или Windows 11.
english.UnsupportedOs=ZiloreMiniMap requires 64-bit Windows 10 (1809+) or Windows 11.
russian.DirConflictProfile=Выбранная папка совпадает с папкой данных профиля (%LocalAppData%\ZiloreMiniMap).%n%nРекомендуется устанавливать программу в отдельную папку (%LocalAppData%\Programs\ZiloreMiniMap), чтобы профиль оставался защищённым при переустановке. Продолжить в эту папку всё равно?
english.DirConflictProfile=The selected folder is the same as the user profile folder (%LocalAppData%\ZiloreMiniMap).%n%nInstalling into %LocalAppData%\Programs\ZiloreMiniMap is strongly recommended to keep your profile isolated. Continue anyway?
russian.UninstallRunning=Обнаружен запущенный процесс ZiloreMiniMap.%n%nКорректно закрыть приложение (с сохранением настроек) для продолжения удаления?
english.UninstallRunning=ZiloreMiniMap is currently running.%n%nClose the application gracefully (saving settings) to continue uninstalling?
russian.UninstDlgTitle=ZiloreMiniMap — Очистка остаточных данных и реестра
english.UninstDlgTitle=ZiloreMiniMap — Residual Data & Registry Cleanup
russian.UninstDlgDesc=Программа удалена. Выберите, какие остаточные записи реестра, кэши и пользовательские файлы очистить (системные библиотеки Microsoft Visual C++ НЕ затрагиваются):
english.UninstDlgDesc=The application has been removed. Select which residual registry entries, caches, and user files to clean up (shared Microsoft Visual C++ runtimes are NEVER removed):
russian.UninstOptRegistry=Очистить остаточный реестр Windows (ассоциации .tsv, OpenWith, App Paths, StartupApproved, MuiCache)
english.UninstOptRegistry=Clean residual Windows registry keys (.tsv, OpenWith, App Paths, StartupApproved, MuiCache)
russian.UninstOptCache=Удалить кэш обновлений, временные папки (%TEMP%) и остатки папки установки
english.UninstOptCache=Remove update cache, temporary folders (%TEMP%), and leftover install-dir files
russian.UninstOptTask=Удалить фоновую задачу Планировщика Windows (SCUM Auto-Launch)
english.UninstOptTask=Remove Windows Scheduled Task (SCUM Auto-Launch watcher)
russian.UninstOptProfile=Удалить профиль пользователя (настройки, зоны, маршруты, ВПП, пресеты, логи)
english.UninstOptProfile=Delete user profile (settings, zones, routes, runways, presets, logs)
russian.UninstOptBackup=Сохранить резервную копию профиля на Рабочий стол перед удалением
english.UninstOptBackup=Save a backup of user profile to Desktop before deleting
russian.UninstBtnApply=Применить и завершить
english.UninstBtnApply=Apply & Finish
russian.UninstBtnSkip=Оставить данные
english.UninstBtnSkip=Keep Data
russian.BackupCreatedMsg=Резервная копия профиля сохранена на Рабочий стол:%n%1
english.BackupCreatedMsg=User profile backup saved to Desktop:%n%1

[Tasks]
Name: "desktopicon"; Description: "{cm:TaskDesktop}"; GroupDescription: "{cm:GroupShortcuts}"
Name: "startup"; Description: "{cm:TaskStartup}"; GroupDescription: "{cm:GroupShortcuts}"; Flags: unchecked
Name: "assoctsv"; Description: "{cm:TaskAssocTsv}"; GroupDescription: "{cm:GroupShortcuts}"
Name: "cleancache"; Description: "{cm:TaskCleanCache}"; GroupDescription: "{cm:GroupMaintenance}"
Name: "backupprofile"; Description: "{cm:TaskBackupProfile}"; GroupDescription: "{cm:GroupMaintenance}"; Flags: unchecked; Check: HasExistingProfile
Name: "migratepf"; Description: "{cm:TaskMigratePf}"; GroupDescription: "{cm:GroupMaintenance}"; Check: HasLegacyProgramFilesInstall

[Files]
Source: "uninstall.ico"; DestDir: "{app}\icons"; Flags: ignoreversion
Source: "app-day.ico"; DestDir: "{app}\icons"; Flags: ignoreversion
Source: "app-night.ico"; DestDir: "{app}\icons"; Flags: ignoreversion
Source: "{#SourceDir}\ZiloreMiniMap.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourceDir}\assets\*"; DestDir: "{app}\assets"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "..\docs\EULA-RU.txt"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\EULA-EN.txt"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\USER-GUIDE-RU.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\USER-GUIDE-EN.md"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{group}\ZiloreMiniMap"; Filename: "{app}\ZiloreMiniMap.exe"; IconFilename: "{code:GetAdaptiveIconFile}"
Name: "{group}\{cm:UninstallProgram,ZiloreMiniMap}"; Filename: "{uninstallexe}"; IconFilename: "{app}\icons\uninstall.ico"
Name: "{userdesktop}\ZiloreMiniMap"; Filename: "{app}\ZiloreMiniMap.exe"; IconFilename: "{code:GetAdaptiveIconFile}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ZiloreMiniMap"; ValueData: """{app}\ZiloreMiniMap.exe"""; Flags: uninsdeletevalue; Tasks: startup
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\ZiloreMiniMap.exe"; ValueType: string; ValueName: ""; ValueData: "{app}\ZiloreMiniMap.exe"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\ZiloreMiniMap.exe"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Zilore\ZiloreMiniMap"; ValueType: string; ValueName: "EulaAcceptedVersion"; ValueData: "2026-11-26"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\.tsv\OpenWithProgids"; ValueType: none; ValueName: "ZiloreMiniMap.ZoneFile"; Flags: uninsdeletevalue; Tasks: assoctsv
Root: HKCU; Subkey: "Software\Classes\ZiloreMiniMap.ZoneFile"; ValueType: string; ValueName: ""; ValueData: "ZiloreMiniMap Zone File"; Flags: uninsdeletekey; Tasks: assoctsv
Root: HKCU; Subkey: "Software\Classes\ZiloreMiniMap.ZoneFile\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\ZiloreMiniMap.exe"",0"; Flags: uninsdeletekey; Tasks: assoctsv
Root: HKCU; Subkey: "Software\Classes\ZiloreMiniMap.ZoneFile\shell\open"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Single"; Flags: uninsdeletekey; Tasks: assoctsv
Root: HKCU; Subkey: "Software\Classes\ZiloreMiniMap.ZoneFile\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ZiloreMiniMap.exe"" --import-zones ""%1"""; Flags: uninsdeletekey; Tasks: assoctsv
Root: HKCU; Subkey: "Software\Classes\Applications\ZiloreMiniMap.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "ZiloreMiniMap"; Flags: uninsdeletekey; Tasks: assoctsv
Root: HKCU; Subkey: "Software\Classes\Applications\ZiloreMiniMap.exe\SupportedTypes"; ValueType: string; ValueName: ".tsv"; ValueData: ""; Flags: uninsdeletekey; Tasks: assoctsv
Root: HKCU; Subkey: "Software\Classes\Applications\ZiloreMiniMap.exe\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ZiloreMiniMap.exe"" --import-zones ""%1"""; Flags: uninsdeletekey; Tasks: assoctsv

[UninstallDelete]
Type: files; Name: "{app}\package.txt"
Type: files; Name: "{app}\ZiloreCodeSigning.cer"
Type: files; Name: "{app}\*.previous"
Type: files; Name: "{app}\*.incoming"
Type: files; Name: "{app}\*.tmp"
Type: files; Name: "{app}\*.partial"
Type: files; Name: "{app}\*.bak"
Type: files; Name: "{app}\*.p7s"
Type: files; Name: "{app}\assets\*.previous"
Type: files; Name: "{app}\assets\*.incoming"
Type: files; Name: "{app}\assets\*.tmp"
Type: dirifempty; Name: "{app}\assets"
Type: dirifempty; Name: "{app}\icons"
Type: dirifempty; Name: "{app}\docs"
Type: dirifempty; Name: "{app}"
Type: dirifempty; Name: "{localappdata}\Programs"

[Run]
Filename: "{app}\ZiloreMiniMap.exe"; Description: "{cm:LaunchProgram,ZiloreMiniMap}"; Flags: nowait postinstall skipifsilent shellexec
Filename: "https://github.com/Zilore-Anatoliy/ZiloreMiniMap/releases"; Description: "{cm:LaunchReleases}"; Flags: shellexec postinstall skipifsilent unchecked

[Code]
var
  DiagPage: TWizardPage;
  DiagMemo: TNewMemo;
  InstallPrereqsCheck: TNewCheckBox;
  RefreshDiagBtn: TNewButton;
  SaveDiagBtn: TNewButton;
  DownloadPage: TDownloadWizardPage;
  NeedsVcRedist: Boolean;
  NeedsDirectX: Boolean;
  LastDiagReport: String;

  UninstProfileCheck: TNewCheckBox;
  UninstBackupCheck: TNewCheckBox;

function IsRu: Boolean;
begin
  Result := ActiveLanguage = 'russian';
end;

function CmdLineHasSwitch(const SwitchName: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    if CompareText(ParamStr(I), SwitchName) = 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function SystemPrefersLightTaskbar: Boolean;
var
  Val: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Themes\Personalize', 'SystemUsesLightTheme', Val) then
    Result := (Val = 1);
end;

function GetAdaptiveIconFile(Param: String): String;
begin
  if SystemPrefersLightTaskbar then
    Result := ExpandConstant('{app}\icons\app-day.ico')
  else
    Result := ExpandConstant('{app}\icons\app-night.ico');
end;

function IsPathInsideProgramFiles(const DirPath: String): Boolean;
var
  Norm, Pf64, Pf32: String;
begin
  Norm := Lowercase(AddBackslash(Trim(DirPath)));
  Pf64 := Lowercase(AddBackslash(ExpandConstant('{commonpf64}')));
  Pf32 := Lowercase(AddBackslash(ExpandConstant('{commonpf32}')));
  Result := ((Pf64 <> '\') and (Pos(Pf64, Norm) = 1)) or ((Pf32 <> '\') and (Pos(Pf32, Norm) = 1));
end;

function GetLegacyProgramFilesUninstaller(var OldDir: String): String;
var
  RegLoc, RegUninst, CandidateDir: String;
begin
  Result := '';
  OldDir := '';
  if RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{8F4C2E91-5B3A-4D7E-9A1C-7E6D5F4A3B21}_is1', 'InstallLocation', RegLoc) then
  begin
    RegLoc := RemoveBackslash(Trim(RegLoc));
    if (RegLoc <> '') and IsPathInsideProgramFiles(RegLoc) and DirExists(RegLoc) then
    begin
      OldDir := RegLoc;
      if RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{8F4C2E91-5B3A-4D7E-9A1C-7E6D5F4A3B21}_is1', 'UninstallString', RegUninst) then
      begin
        RegUninst := Trim(RegUninst);
        if (Length(RegUninst) >= 2) and (RegUninst[1] = '"') and (RegUninst[Length(RegUninst)] = '"') then
          RegUninst := Copy(RegUninst, 2, Length(RegUninst) - 2);
        if FileExists(RegUninst) then
        begin
          Result := RegUninst;
          Exit;
        end;
      end;
      if FileExists(AddBackslash(RegLoc) + 'unins000.exe') then
      begin
        Result := AddBackslash(RegLoc) + 'unins000.exe';
        Exit;
      end;
    end;
  end;

  CandidateDir := AddBackslash(ExpandConstant('{commonpf64}')) + 'ZiloreMiniMap';
  if DirExists(CandidateDir) and (FileExists(CandidateDir + '\ZiloreMiniMap.exe') or FileExists(CandidateDir + '\unins000.exe')) then
  begin
    OldDir := CandidateDir;
    if FileExists(CandidateDir + '\unins000.exe') then
      Result := CandidateDir + '\unins000.exe';
  end;
end;

function HasLegacyProgramFilesInstall: Boolean;
var
  OldDir, UninstExe: String;
begin
  UninstExe := GetLegacyProgramFilesUninstaller(OldDir);
  Result := (OldDir <> '') and (CompareText(RemoveBackslash(OldDir), RemoveBackslash(WizardDirValue)) <> 0);
end;

function HasExistingProfile: Boolean;
var
  DataDir: String;
begin
  DataDir := ExpandConstant('{localappdata}\ZiloreMiniMap');
  Result := DirExists(DataDir) and (
    FileExists(DataDir + '\settings.ini') or
    FileExists(DataDir + '\zones.tsv') or
    FileExists(DataDir + '\saved-route.tsv') or
    FileExists(DataDir + '\saved-routes.tsv') or
    FileExists(DataDir + '\map-presets.dat'));
end;

procedure ComputeDirStats(const DirPath: String; var FileCount: Integer; var TotalKB: Int64);
var
  FindRec: TFindRec;
  SubPath: String;
  FileBytes: Int64;
begin
  if not DirExists(DirPath) then Exit;
  if FindFirst(AddBackslash(DirPath) + '*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          SubPath := AddBackslash(DirPath) + FindRec.Name;
          if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
            ComputeDirStats(SubPath, FileCount, TotalKB)
          else
          begin
            FileCount := FileCount + 1;
            FileBytes := 0;
            if FileSize64(SubPath, FileBytes) then
              TotalKB := TotalKB + ((FileBytes + 1023) div 1024);
          end;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function FormatSizeKB(const TotalKB: Int64): String;
begin
  if TotalKB >= 1048576 then
    Result := IntToStr(TotalKB div 1048576) + '.' + IntToStr(((TotalKB mod 1048576) * 10) div 1048576) + ' GB'
  else if TotalKB >= 1024 then
    Result := IntToStr(TotalKB div 1024) + '.' + IntToStr(((TotalKB mod 1024) * 10) div 1024) + ' MB'
  else
    Result := IntToStr(TotalKB) + ' KB';
end;

function BackupUserProfileToDesktop(const DataDir: String; var BackupDir: String): Integer;
var
  Stamp, SrcFile, DstFile, MapsSrc, MapsDst: String;
  Files: array[0..17] of String;
  I: Integer;
  FindRec: TFindRec;
begin
  Result := 0;
  BackupDir := '';
  if not DirExists(DataDir) then Exit;

  Stamp := GetDateTimeString('yyyymmdd-hhnnss', #0, #0);
  BackupDir := AddBackslash(ExpandConstant('{userdesktop}')) + 'ZiloreMiniMap-Backup-' + Stamp;
  if not ForceDirectories(BackupDir) then Exit;

  Files[0] := 'settings.ini';
  Files[1] := 'flight-ui.ini';
  Files[2] := 'hw-accel.ini';
  Files[3] := 'tactical-layout.ini';
  Files[4] := 'updates.ini';
  Files[5] := 'game-clock.ini';
  Files[6] := 'weather.ini';
  Files[7] := 'zilore-ai-profile.ini';
  Files[8] := 'survival-tracker.ini';
  Files[9] := 'custom-runways.tsv';
  Files[10] := 'saved-route.tsv';
  Files[11] := 'saved-routes.tsv';
  Files[12] := 'map-presets.dat';
  Files[13] := 'preset-extras.ini';
  Files[14] := 'zones.tsv';
  Files[15] := 'customwaypoints.tsv';
  Files[16] := 'admin-commands.tsv';
  Files[17] := 'spawn-cart.tsv';

  for I := 0 to 17 do
  begin
    SrcFile := AddBackslash(DataDir) + Files[I];
    if FileExists(SrcFile) then
    begin
      DstFile := AddBackslash(BackupDir) + Files[I];
      if CopyFile(SrcFile, DstFile, False) then
        Result := Result + 1;
    end;
  end;

  MapsSrc := AddBackslash(DataDir) + 'maps';
  if DirExists(MapsSrc) then
  begin
    MapsDst := AddBackslash(BackupDir) + 'maps';
    if ForceDirectories(MapsDst) and FindFirst(AddBackslash(MapsSrc) + '*', FindRec) then
    begin
      try
        repeat
          if (FindRec.Name <> '.') and (FindRec.Name <> '..') and ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0) then
          begin
            if CopyFile(AddBackslash(MapsSrc) + FindRec.Name, AddBackslash(MapsDst) + FindRec.Name, False) then
              Result := Result + 1;
          end;
        until not FindNext(FindRec);
      finally
        FindClose(FindRec);
      end;
    end;
  end;

  if Result = 0 then
    RemoveDir(BackupDir);
end;

procedure CleanTempByPrefix(const TempRoot, Prefix: String);
var
  FindRec: TFindRec;
  FullPath: String;
begin
  if not DirExists(TempRoot) then Exit;
  if FindFirst(AddBackslash(TempRoot) + Prefix + '*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          FullPath := AddBackslash(TempRoot) + FindRec.Name;
          if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
            DelTree(FullPath, True, True, True)
          else
            DeleteFile(FullPath);
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure CleanAllRuntimeCachesAndTemp;
var
  DataDir, TempRoot: String;
begin
  DataDir := ExpandConstant('{localappdata}\ZiloreMiniMap');
  if DirExists(DataDir + '\updates') then
    DelTree(DataDir + '\updates', True, True, True);
  if DirExists(DataDir + '\field-console-fonts') then
    DelTree(DataDir + '\field-console-fonts', True, True, True);
  if FileExists(DataDir + '\update-result.txt') then
    DeleteFile(DataDir + '\update-result.txt');
  if DirExists(DataDir + '\reports') then
    DelTree(DataDir + '\reports', True, True, True);

  TempRoot := ExpandConstant('{tmp}');
  TempRoot := ExtractFileDir(RemoveBackslash(TempRoot));
  if DirExists(TempRoot) then
  begin
    CleanTempByPrefix(TempRoot, 'ScumMiniMap-update-');
    CleanTempByPrefix(TempRoot, 'minimap-zone-open-');
    CleanTempByPrefix(TempRoot, 'minimap-zone-pack-');
    CleanTempByPrefix(TempRoot, 'scum-zones-');
    CleanTempByPrefix(TempRoot, 'zlr-local-upd-test-');
  end;
end;

procedure SyncAutoLaunchPathToCurrentApp;
var
  EnabledFile, NewExe: String;
begin
  EnabledFile := ExpandConstant('{localappdata}\ZiloreMiniMap\auto-launch\enabled.txt');
  if FileExists(EnabledFile) then
  begin
    NewExe := ExpandConstant('{app}\ZiloreMiniMap.exe');
    SaveStringToFile(EnabledFile, NewExe, False);
  end;
end;

function IsVcRedistX64Installed(var VerInfo: String): Boolean;
var
  Installed, MajorVer, MinorVer, BuildVer: Cardinal;
  SysDir: String;
begin
  Result := False;
  VerInfo := '';
  if RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) and (Installed = 1) then
  begin
    MajorVer := 14;
    MinorVer := 0;
    BuildVer := 0;
    RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Major', MajorVer);
    RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Minor', MinorVer);
    RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Bld', BuildVer);
    VerInfo := Format('%d.%d.%d', [MajorVer, MinorVer, BuildVer]);
    if MajorVer >= 14 then
      Result := True;
  end;
  if not Result then
  begin
    SysDir := ExpandConstant('{sys}');
    if FileExists(SysDir + '\vcruntime140.dll') and FileExists(SysDir + '\vcruntime140_1.dll') and FileExists(SysDir + '\msvcp140.dll') then
    begin
      Result := True;
      VerInfo := 'DLL OK';
    end;
  end;
end;

function GetPrimaryGpuName: String;
var
  I: Integer;
  SubKey, Desc: String;
begin
  Result := '';
  for I := 0 to 3 do
  begin
    SubKey := Format('SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\000%d', [I]);
    if RegQueryStringValue(HKLM64, SubKey, 'DriverDesc', Desc) and (Trim(Desc) <> '') then
    begin
      if (Pos('Basic Display', Desc) = 0) and (Pos('Virtual', Desc) = 0) then
      begin
        Result := Trim(Desc);
        Exit;
      end;
      if Result = '' then
        Result := Trim(Desc);
    end;
  end;
end;

function IsScumDetected(var ScumDir: String): Boolean;
var
  SteamPath, VdfPath, Line, LibPath: String;
  Lines: TArrayOfString;
  I, P1, P2: Integer;
begin
  Result := False;
  ScumDir := '';
  if RegQueryStringValue(HKCU, 'Software\Valve\Steam', 'SteamPath', SteamPath) then
  begin
    StringChangeEx(SteamPath, '/', '\', True);
    if DirExists(SteamPath + '\steamapps\common\SCUM') then
    begin
      ScumDir := SteamPath + '\steamapps\common\SCUM';
      Result := True;
      Exit;
    end;

    VdfPath := SteamPath + '\steamapps\libraryfolders.vdf';
    if FileExists(VdfPath) and LoadStringsFromFile(VdfPath, Lines) then
    begin
      for I := 0 to GetArrayLength(Lines) - 1 do
      begin
        Line := Trim(Lines[I]);
        P1 := Pos('"path"', Lowercase(Line));
        if P1 > 0 then
        begin
          Delete(Line, 1, P1 + 5);
          P1 := Pos('"', Line);
          if P1 > 0 then
          begin
            Delete(Line, 1, P1);
            P2 := Pos('"', Line);
            if P2 > 1 then
            begin
              LibPath := Copy(Line, 1, P2 - 1);
              StringChangeEx(LibPath, '\\', '\', True);
              StringChangeEx(LibPath, '/', '\', True);
              if DirExists(AddBackslash(LibPath) + 'steamapps\common\SCUM') then
              begin
                ScumDir := AddBackslash(LibPath) + 'steamapps\common\SCUM';
                Result := True;
                Exit;
              end;
            end;
          end;
        end;
      end;
    end;
  end;

  if DirExists('C:\Program Files (x86)\Steam\steamapps\common\SCUM') then
  begin
    ScumDir := 'C:\Program Files (x86)\Steam\steamapps\common\SCUM';
    Result := True;
  end;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

procedure RunSystemDiagnostics;
var
  WinVer: TWindowsVersion;
  SysDir, VcVer, DotNetVer, GpuName, ScumDir, DataDir, OldPfDir, Report, OsLabel, DiskLine: String;
  HasVc, HasUcrt, HasD3d11, HasD3d9, HasSqlite, HasDotNet, HasScum, HasRunning, HasProfile: Boolean;
  FreeBytes, TotalBytes, ProfileKB: Int64;
  ProfileFiles: Integer;
begin
  GetWindowsVersionEx(WinVer);
  SysDir := ExpandConstant('{sys}');

  HasVc := IsVcRedistX64Installed(VcVer);
  HasUcrt := FileExists(SysDir + '\ucrtbase.dll');
  HasD3d11 := FileExists(SysDir + '\d3d11.dll');
  HasD3d9 := FileExists(SysDir + '\d3d9.dll');
  HasSqlite := FileExists(SysDir + '\winsqlite3.dll');
  HasDotNet := RegQueryStringValue(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost', 'Version', DotNetVer);
  GpuName := GetPrimaryGpuName;
  HasScum := IsScumDetected(ScumDir);
  HasRunning := CheckForMutexes('Local\ZiloreMiniMapOverlay');
  DataDir := ExpandConstant('{localappdata}\ZiloreMiniMap');
  HasProfile := DirExists(DataDir);
  ProfileFiles := 0;
  ProfileKB := 0;
  if HasProfile then
    ComputeDirStats(DataDir, ProfileFiles, ProfileKB);
  GetLegacyProgramFilesUninstaller(OldPfDir);

  NeedsVcRedist := not HasVc;
  NeedsDirectX := (not HasD3d11) or (not HasD3d9);

  if (WinVer.Major = 10) and (WinVer.Build >= 22000) then
    OsLabel := Format('Windows 11 x64 (Build %d)', [WinVer.Build])
  else
    OsLabel := Format('Windows %d.%d x64 (Build %d)', [WinVer.Major, WinVer.Minor, WinVer.Build]);

  DiskLine := '';
  if GetSpaceOnDisk64(AddBackslash(ExtractFileDrive(ExpandConstant('{localappdata}'))), FreeBytes, TotalBytes) then
    DiskLine := FormatSizeKB(FreeBytes div 1024);

  Report := '';
  if IsRu then
  begin
    Report := Report + '[OK]    Операционная система: ' + OsLabel + #13#10;
    if GpuName <> '' then
      Report := Report + '[OK]    Видеоадаптер (WPF GPU): ' + GpuName + #13#10;
    if DiskLine <> '' then
      Report := Report + '[OK]    Свободно на диске: ' + DiskLine + #13#10;
    Report := Report + '[OK]    Папка установки: ' + ExpandConstant('{localappdata}\Programs\ZiloreMiniMap') + ' (без Program Files)' + #13#10;
    Report := Report + '[OK]    Среда .NET 10 LTS: встроена (Self-Contained)';
    if HasDotNet then
      Report := Report + ' · хост: ' + DotNetVer;
    Report := Report + #13#10;

    if HasVc then
      Report := Report + '[OK]    Microsoft Visual C++ 2015–2022 x64: установлен (' + VcVer + ')' + #13#10
    else
      Report := Report + '[НУЖЕН] Microsoft Visual C++ 2015–2022 x64: НЕ НАЙДЕН (доступна автоустановка)' + #13#10;

    if HasUcrt then
      Report := Report + '[OK]    Universal CRT (ucrtbase.dll): доступен' + #13#10
    else
      Report := Report + '[ВНИМ.] Universal CRT (ucrtbase.dll): отсутствует — рекомендуется Windows Update' + #13#10;

    if HasD3d11 and HasD3d9 then
      Report := Report + '[OK]    DirectX 9 / 11 (d3d9.dll + d3d11.dll): доступен' + #13#10
    else
      Report := Report + '[НУЖЕН] DirectX Runtime (d3d9/d3d11): неполный (доступна автоустановка)' + #13#10;

    if HasSqlite then
      Report := Report + '[OK]    Windows SQLite (winsqlite3.dll): доступен' + #13#10
    else
      Report := Report + '[ВНИМ.] Windows SQLite (winsqlite3.dll): не найден в System32' + #13#10;

    Report := Report + '------------------------------------------------------------------------' + #13#10;
    if HasScum then
      Report := Report + '[INFO]  Игра SCUM обнаружена: ' + ScumDir + #13#10
    else
      Report := Report + '[INFO]  Игра SCUM: не найдена в библиотеках Steam (доп. настройка не нужна)' + #13#10;

    if HasProfile then
      Report := Report + Format('[INFO]  Профиль найден (%d файлов, %s) — настройки, зоны и маршруты сохранятся.', [ProfileFiles, FormatSizeKB(ProfileKB)]) + #13#10
    else
      Report := Report + '[INFO]  Чистая установка — после запуска откроется мастер первого запуска.' + #13#10;

    if OldPfDir <> '' then
      Report := Report + '[ВНИМ.] Найдена старая копия в ' + OldPfDir + ' — будет перенесена в AppData\Local\Programs.' + #13#10;

    if HasRunning then
      Report := Report + '[ВНИМ.] ZiloreMiniMap сейчас запущен и будет автоматически закрыт при установке.' + #13#10;
  end
  else
  begin
    Report := Report + '[OK]      Operating System: ' + OsLabel + #13#10;
    if GpuName <> '' then
      Report := Report + '[OK]      Graphics Adapter (WPF GPU): ' + GpuName + #13#10;
    if DiskLine <> '' then
      Report := Report + '[OK]      Free Disk Space: ' + DiskLine + #13#10;
    Report := Report + '[OK]      Install Folder: ' + ExpandConstant('{localappdata}\Programs\ZiloreMiniMap') + ' (user-scoped, no Program Files)' + #13#10;
    Report := Report + '[OK]      .NET 10 LTS Runtime: bundled (Self-Contained)';
    if HasDotNet then
      Report := Report + ' · host: ' + DotNetVer;
    Report := Report + #13#10;

    if HasVc then
      Report := Report + '[OK]      Microsoft Visual C++ 2015–2022 x64: installed (' + VcVer + ')' + #13#10
    else
      Report := Report + '[MISSING] Microsoft Visual C++ 2015–2022 x64: NOT FOUND (auto-install ready)' + #13#10;

    if HasUcrt then
      Report := Report + '[OK]      Universal CRT (ucrtbase.dll): present' + #13#10
    else
      Report := Report + '[WARN]    Universal CRT (ucrtbase.dll): missing — Windows Update recommended' + #13#10;

    if HasD3d11 and HasD3d9 then
      Report := Report + '[OK]      DirectX 9 / 11 (d3d9.dll + d3d11.dll): present' + #13#10
    else
      Report := Report + '[MISSING] DirectX Runtime (d3d9/d3d11): incomplete (auto-install ready)' + #13#10;

    if HasSqlite then
      Report := Report + '[OK]      Windows SQLite (winsqlite3.dll): present' + #13#10
    else
      Report := Report + '[WARN]    Windows SQLite (winsqlite3.dll): missing in System32' + #13#10;

    Report := Report + '------------------------------------------------------------------------' + #13#10;
    if HasScum then
      Report := Report + '[INFO]    SCUM installation found: ' + ScumDir + #13#10
    else
      Report := Report + '[INFO]    SCUM game folder: not found in Steam libraries (no action needed)' + #13#10;

    if HasProfile then
      Report := Report + Format('[INFO]    User profile found (%d files, %s) — settings, zones & routes preserved.', [ProfileFiles, FormatSizeKB(ProfileKB)]) + #13#10
    else
      Report := Report + '[INFO]    Fresh installation — Setup Wizard will guide you on first launch.' + #13#10;

    if OldPfDir <> '' then
      Report := Report + '[WARN]    Legacy copy found in ' + OldPfDir + ' — will migrate to AppData\Local\Programs.' + #13#10;

    if HasRunning then
      Report := Report + '[WARN]    ZiloreMiniMap is currently running and will be closed automatically.' + #13#10;
  end;

  LastDiagReport := Report;
  if DiagMemo <> nil then
    DiagMemo.Text := Report;

  if InstallPrereqsCheck <> nil then
  begin
    if NeedsVcRedist or NeedsDirectX then
    begin
      InstallPrereqsCheck.Caption := CustomMessage('DiagAutoInstall');
      InstallPrereqsCheck.Enabled := True;
      InstallPrereqsCheck.Checked := True;
    end
    else
    begin
      InstallPrereqsCheck.Caption := CustomMessage('DiagAllOk');
      InstallPrereqsCheck.Checked := False;
      InstallPrereqsCheck.Enabled := False;
    end;
  end;
end;

procedure OnRefreshDiagClick(Sender: TObject);
begin
  RunSystemDiagnostics;
end;

procedure OnSaveDiagClick(Sender: TObject);
var
  OutPath: String;
  Lines: TArrayOfString;
begin
  OutPath := AddBackslash(ExpandConstant('{userdesktop}')) + 'ZiloreMiniMap-InstallDiagnostics.txt';
  SetArrayLength(Lines, 1);
  Lines[0] := LastDiagReport;
  if SaveStringsToUTF8File(OutPath, Lines, False) then
    MsgBox(FmtMessage(CustomMessage('DiagSavedMsg'), [OutPath]), mbInformation, MB_OK);
end;

function InitializeSetup(): Boolean;
var
  WinVer: TWindowsVersion;
begin
  Result := True;
  GetWindowsVersionEx(WinVer);
  if (not IsWin64) or (WinVer.Major < 10) then
  begin
    MsgBox(CustomMessage('UnsupportedOs'), mbCriticalError, MB_OK);
    Result := False;
  end;
end;

procedure InitializeWizard;
begin
  // Never inherit an old Program Files path from previous admin installs — default cleanly to %LocalAppData%\Programs\ZiloreMiniMap
  if IsPathInsideProgramFiles(WizardForm.DirEdit.Text) then
    WizardForm.DirEdit.Text := ExpandConstant('{localappdata}\Programs\ZiloreMiniMap');

  DiagPage := CreateCustomPage(wpWelcome, CustomMessage('DiagTitle'), CustomMessage('DiagSubTitle'));

  DiagMemo := TNewMemo.Create(DiagPage);
  DiagMemo.Parent := DiagPage.Surface;
  DiagMemo.Left := 0;
  DiagMemo.Top := 0;
  DiagMemo.Width := DiagPage.SurfaceWidth;
  DiagMemo.Height := DiagPage.SurfaceHeight - ScaleY(76);
  DiagMemo.ReadOnly := True;
  DiagMemo.ScrollBars := ssVertical;
  DiagMemo.WordWrap := True;
  DiagMemo.Font.Name := 'Consolas';
  DiagMemo.Font.Size := 9;

  InstallPrereqsCheck := TNewCheckBox.Create(DiagPage);
  InstallPrereqsCheck.Parent := DiagPage.Surface;
  InstallPrereqsCheck.Left := 0;
  InstallPrereqsCheck.Top := DiagMemo.Top + DiagMemo.Height + ScaleY(8);
  InstallPrereqsCheck.Width := DiagPage.SurfaceWidth;
  InstallPrereqsCheck.Height := ScaleY(24);

  RefreshDiagBtn := TNewButton.Create(DiagPage);
  RefreshDiagBtn.Parent := DiagPage.Surface;
  RefreshDiagBtn.Caption := CustomMessage('DiagRecheck');
  RefreshDiagBtn.Width := ScaleX(132);
  RefreshDiagBtn.Height := ScaleY(28);
  RefreshDiagBtn.Left := 0;
  RefreshDiagBtn.Top := InstallPrereqsCheck.Top + InstallPrereqsCheck.Height + ScaleY(8);
  RefreshDiagBtn.OnClick := @OnRefreshDiagClick;

  SaveDiagBtn := TNewButton.Create(DiagPage);
  SaveDiagBtn.Parent := DiagPage.Surface;
  SaveDiagBtn.Caption := CustomMessage('DiagSaveReport');
  SaveDiagBtn.Width := ScaleX(140);
  SaveDiagBtn.Height := ScaleY(28);
  SaveDiagBtn.Left := RefreshDiagBtn.Left + RefreshDiagBtn.Width + ScaleX(8);
  SaveDiagBtn.Top := RefreshDiagBtn.Top;
  SaveDiagBtn.OnClick := @OnSaveDiagClick;

  DownloadPage := CreateDownloadPage(CustomMessage('DownloadTitle'), CustomMessage('DownloadSubTitle'), @OnDownloadProgress);
  RunSystemDiagnostics;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = DiagPage.ID then
    RunSystemDiagnostics;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
  VcPath, DxPath, ErrMsg, ChosenDir, ProfileDir: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    ChosenDir := RemoveBackslash(Trim(WizardDirValue));
    ProfileDir := RemoveBackslash(ExpandConstant('{localappdata}\ZiloreMiniMap'));
    if (CompareText(ChosenDir, ProfileDir) = 0) and (not WizardSilent) then
    begin
      if MsgBox(CustomMessage('DirConflictProfile'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDNO then
      begin
        WizardForm.DirEdit.Text := ExpandConstant('{localappdata}\Programs\ZiloreMiniMap');
        Result := False;
        Exit;
      end;
    end;
  end
  else if (CurPageID = DiagPage.ID) and InstallPrereqsCheck.Checked and (NeedsVcRedist or NeedsDirectX) then
  begin
    DownloadPage.Clear;
    if NeedsVcRedist then
      DownloadPage.Add('https://aka.ms/vs/17/release/vc_redist.x64.exe', 'vc_redist.x64.exe', '');
    if NeedsDirectX then
      DownloadPage.Add('https://download.microsoft.com/download/1/7/1/1718CCC4-6315-4D8E-9543-8E28A4E18C4C/dxwebsetup.exe', 'dxwebsetup.exe', '');

    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
        if NeedsVcRedist then
        begin
          VcPath := ExpandConstant('{tmp}\vc_redist.x64.exe');
          if FileExists(VcPath) then
          begin
            if not ShellExec('runas', VcPath, '/install /passive /norestart', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
              RaiseException('VC++ Redistributable launch failed (code ' + IntToStr(ResultCode) + ')');
          end;
        end;
        if NeedsDirectX then
        begin
          DxPath := ExpandConstant('{tmp}\dxwebsetup.exe');
          if FileExists(DxPath) then
            ShellExec('runas', DxPath, '/Q', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode);
        end;
      except
        ErrMsg := GetExceptionMessage;
        if MsgBox(FmtMessage(CustomMessage('InstallPrereqError'), [ErrMsg]), mbConfirmation, MB_YESNO or MB_DEFBUTTON1) = IDNO then
          Result := False;
      end;
    finally
      DownloadPage.Hide;
      RunSystemDiagnostics;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  BackupDir, OldPfDir, OldUninst: String;
  Copied, ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    if WizardIsTaskSelected('backupprofile') then
    begin
      Copied := BackupUserProfileToDesktop(ExpandConstant('{localappdata}\ZiloreMiniMap'), BackupDir);
      if (Copied > 0) and (not WizardSilent) then
        MsgBox(FmtMessage(CustomMessage('BackupCreatedMsg'), [BackupDir]), mbInformation, MB_OK);
    end;
  end
  else if CurStep = ssPostInstall then
  begin
    SyncAutoLaunchPathToCurrentApp;
    if WizardIsTaskSelected('cleancache') then
      CleanAllRuntimeCachesAndTemp;
    if WizardIsTaskSelected('migratepf') then
    begin
      OldUninst := GetLegacyProgramFilesUninstaller(OldPfDir);
      if (OldUninst <> '') and FileExists(OldUninst) and (CompareText(RemoveBackslash(OldPfDir), RemoveBackslash(ExpandConstant('{app}'))) <> 0) then
        ShellExec('runas', OldUninst, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;

// ============================================================================
// ENTERPRISE UNINSTALLER: Graceful close + Full Residual Registry & File Cleaner
// ============================================================================

procedure CloseRunningAppGracefully;
var
  ResultCode, WaitMs: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM ZiloreMiniMap.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  WaitMs := 0;
  while (WaitMs < 2000) and (CheckForMutexes('Local\ZiloreMiniMapOverlay') or CheckForMutexes('Local\ScumMiniMapOverlay')) do
  begin
    Sleep(200);
    WaitMs := WaitMs + 200;
  end;
  if CheckForMutexes('Local\ZiloreMiniMapOverlay') or CheckForMutexes('Local\ScumMiniMapOverlay') then
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM ZiloreMiniMap.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if CheckForMutexes('Local\ZiloreMiniMapOverlay') or CheckForMutexes('Local\ScumMiniMapOverlay') then
  begin
    if UninstallSilent then
      CloseRunningAppGracefully
    else
    begin
      if MsgBox(CustomMessage('UninstallRunning'), mbConfirmation, MB_YESNO or MB_DEFBUTTON1) = IDYES then
        CloseRunningAppGracefully
      else
        Result := False;
    end;
  end;
end;

function CountResidualRegistryEntries: Integer;
var
  AppExe: String;
begin
  Result := 0;
  AppExe := ExpandConstant('{app}\ZiloreMiniMap.exe');
  if RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ZiloreMiniMap') then Result := Result + 1;
  if RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'SkynettMiniMap') then Result := Result + 1;
  if RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'ZiloreMiniMap') then Result := Result + 1;
  if RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'SkynettMiniMap') then Result := Result + 1;
  if RegKeyExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\App Paths\ZiloreMiniMap.exe') then Result := Result + 1;
  if RegKeyExists(HKCU, 'Software\Classes\ZiloreMiniMap.ZoneFile') then Result := Result + 1;
  if RegKeyExists(HKCU, 'Software\Classes\SkynettMiniMap.ZoneFile') then Result := Result + 1;
  if RegValueExists(HKCU, 'Software\Classes\.tsv\OpenWithProgids', 'ZiloreMiniMap.ZoneFile') then Result := Result + 1;
  if RegValueExists(HKCU, 'Software\Classes\.tsv\OpenWithProgids', 'SkynettMiniMap.ZoneFile') then Result := Result + 1;
  if RegKeyExists(HKCU, 'Software\Classes\Applications\ZiloreMiniMap.exe') then Result := Result + 1;
  if RegKeyExists(HKCU, 'Software\Classes\Applications\SkynettMiniMap.exe') then Result := Result + 1;
  if RegValueExists(HKCU, 'Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers', AppExe) then Result := Result + 1;
  if RegValueExists(HKCU, 'Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache', AppExe + '.FriendlyAppName') then Result := Result + 1;
  if RegValueExists(HKCU, 'Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache', AppExe + '.ApplicationCompany') then Result := Result + 1;
end;

procedure CleanResidualRegistryKeys;
var
  AppExe: String;
begin
  AppExe := ExpandConstant('{app}\ZiloreMiniMap.exe');
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ZiloreMiniMap');
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'SkynettMiniMap');
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'ZiloreMiniMap');
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'SkynettMiniMap');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Windows\CurrentVersion\App Paths\ZiloreMiniMap.exe');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\ZiloreMiniMap.ZoneFile');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\SkynettMiniMap.ZoneFile');
  RegDeleteValue(HKCU, 'Software\Classes\.tsv\OpenWithProgids', 'ZiloreMiniMap.ZoneFile');
  RegDeleteValue(HKCU, 'Software\Classes\.tsv\OpenWithProgids', 'SkynettMiniMap.ZoneFile');
  RegDeleteKeyIfEmpty(HKCU, 'Software\Classes\.tsv\OpenWithProgids');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Applications\ZiloreMiniMap.exe');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Applications\SkynettMiniMap.exe');
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers', AppExe);
  RegDeleteValue(HKCU, 'Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache', AppExe + '.FriendlyAppName');
  RegDeleteValue(HKCU, 'Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache', AppExe + '.ApplicationCompany');
  RegDeleteKeyIfEmpty(HKCU, 'Software\ZiloreMiniMap');
end;

procedure UnregisterScumAutoLaunchTask(const DataDir: String);
var
  ResultCode: Integer;
begin
  if FileExists(DataDir + '\auto-launch\enabled.txt') then
    DeleteFile(DataDir + '\auto-launch\enabled.txt');
  if DirExists(DataDir + '\auto-launch') then
    DelTree(DataDir + '\auto-launch', True, True, True);
  Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -WindowStyle Hidden -Command "Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -like ''SCUM MiniMap Auto Launch*'' } | Unregister-ScheduledTask -Confirm:$false -ErrorAction SilentlyContinue"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function BuildUninstallInventorySummary(const DataDir: String; const RegCount, ProfileFiles: Integer; const ProfileKB: Int64): String;
var
  S: String;
begin
  S := '';
  if IsRu then
  begin
    S := S + Format('• Остаточный реестр Windows (HKCU): %d записей', [RegCount]) + #13#10;
    if DirExists(DataDir) then
    begin
      S := S + Format('• Папка профиля (%s): %d файлов, %s', [DataDir, ProfileFiles, FormatSizeKB(ProfileKB)]) + #13#10;
      if FileExists(DataDir + '\settings.ini') then S := S + '    – settings.ini, flight-ui.ini, tactical-layout.ini, updates.ini, game-clock.ini, weather.ini' + #13#10;
      if FileExists(DataDir + '\saved-route.tsv') or FileExists(DataDir + '\saved-routes.tsv') then S := S + '    – сохранённые маршруты (saved-route*.tsv) и пресеты карты' + #13#10;
      if FileExists(DataDir + '\zones.tsv') or FileExists(DataDir + '\customwaypoints.tsv') or DirExists(DataDir + '\maps') then S := S + '    – пользовательские зоны, метки и карты (zones.tsv / maps\)' + #13#10;
      if FileExists(DataDir + '\poi-access.bin') or FileExists(DataDir + '\private-groups.dat') then S := S + '    – ключ доступа POI и приватные группы отряда' + #13#10;
      if FileExists(DataDir + '\auto-launch\enabled.txt') then S := S + '    – задача автозапуска SCUM (Планировщик Windows)' + #13#10;
      if DirExists(DataDir + '\reports') then S := S + '    – архивы диагностических отчётов (reports\)' + #13#10;
    end
    else
      S := S + '• Папка профиля пользователя отсутствует.' + #13#10;
  end
  else
  begin
    S := S + Format('• Residual Windows Registry (HKCU): %d entries', [RegCount]) + #13#10;
    if DirExists(DataDir) then
    begin
      S := S + Format('• User profile folder (%s): %d files, %s', [DataDir, ProfileFiles, FormatSizeKB(ProfileKB)]) + #13#10;
      if FileExists(DataDir + '\settings.ini') then S := S + '    - settings.ini, flight-ui.ini, tactical-layout.ini, updates.ini, game-clock.ini, weather.ini' + #13#10;
      if FileExists(DataDir + '\saved-route.tsv') or FileExists(DataDir + '\saved-routes.tsv') then S := S + '    - saved routes (saved-route*.tsv) & map presets' + #13#10;
      if FileExists(DataDir + '\zones.tsv') or FileExists(DataDir + '\customwaypoints.tsv') or DirExists(DataDir + '\maps') then S := S + '    - custom zones, waypoints & maps (zones.tsv / maps\)' + #13#10;
      if FileExists(DataDir + '\poi-access.bin') or FileExists(DataDir + '\private-groups.dat') then S := S + '    - POI access license & private squad groups' + #13#10;
      if FileExists(DataDir + '\auto-launch\enabled.txt') then S := S + '    - SCUM Auto-Launch Windows Scheduled Task' + #13#10;
      if DirExists(DataDir + '\reports') then S := S + '    - diagnostic error reports (reports\)' + #13#10;
    end
    else
      S := S + '• No user profile folder found.' + #13#10;
  end;
  Result := S;
end;

procedure OnUninstProfileCheckClick(Sender: TObject);
begin
  if (UninstProfileCheck <> nil) and (UninstBackupCheck <> nil) then
  begin
    UninstBackupCheck.Enabled := UninstProfileCheck.Checked;
    if not UninstProfileCheck.Checked then
      UninstBackupCheck.Checked := False;
  end;
end;

procedure ShowEnterpriseUninstallCleanup(const DataDir: String);
var
  Form: TSetupForm;
  DescLbl: TNewStaticText;
  SummaryMemo: TNewMemo;
  ChkReg, ChkCache, ChkTask: TNewCheckBox;
  BtnApply, BtnSkip: TNewButton;
  RegCount, ProfileFiles, Copied: Integer;
  ProfileKB: Int64;
  HasProfileDir, HasAutoTask: Boolean;
  BackupDir: String;
begin
  RegCount := CountResidualRegistryEntries;
  HasProfileDir := DirExists(DataDir);
  HasAutoTask := FileExists(DataDir + '\auto-launch\enabled.txt');
  ProfileFiles := 0;
  ProfileKB := 0;
  if HasProfileDir then
    ComputeDirStats(DataDir, ProfileFiles, ProfileKB);

  Form := CreateCustomForm(520, 405, False, False);
  try
    Form.Caption := CustomMessage('UninstDlgTitle');
    Form.Position := poScreenCenter;
    Form.BorderStyle := bsDialog;

    DescLbl := TNewStaticText.Create(Form);
    DescLbl.Parent := Form;
    DescLbl.Left := ScaleX(16);
    DescLbl.Top := ScaleY(14);
    DescLbl.Width := Form.ClientWidth - ScaleX(32);
    DescLbl.WordWrap := True;
    DescLbl.AutoSize := True;
    DescLbl.Caption := CustomMessage('UninstDlgDesc');

    SummaryMemo := TNewMemo.Create(Form);
    SummaryMemo.Parent := Form;
    SummaryMemo.Left := ScaleX(16);
    SummaryMemo.Top := DescLbl.Top + DescLbl.Height + ScaleY(10);
    SummaryMemo.Width := Form.ClientWidth - ScaleX(32);
    SummaryMemo.Height := ScaleY(112);
    SummaryMemo.ReadOnly := True;
    SummaryMemo.ScrollBars := ssVertical;
    SummaryMemo.WordWrap := True;
    SummaryMemo.Font.Name := 'Consolas';
    SummaryMemo.Font.Size := 9;
    SummaryMemo.Text := BuildUninstallInventorySummary(DataDir, RegCount, ProfileFiles, ProfileKB);

    ChkReg := TNewCheckBox.Create(Form);
    ChkReg.Parent := Form;
    ChkReg.Left := ScaleX(16);
    ChkReg.Top := SummaryMemo.Top + SummaryMemo.Height + ScaleY(12);
    ChkReg.Width := Form.ClientWidth - ScaleX(32);
    ChkReg.Height := ScaleY(22);
    ChkReg.Caption := CustomMessage('UninstOptRegistry');
    ChkReg.Checked := True;

    ChkCache := TNewCheckBox.Create(Form);
    ChkCache.Parent := Form;
    ChkCache.Left := ScaleX(16);
    ChkCache.Top := ChkReg.Top + ScaleY(26);
    ChkCache.Width := Form.ClientWidth - ScaleX(32);
    ChkCache.Height := ScaleY(22);
    ChkCache.Caption := CustomMessage('UninstOptCache');
    ChkCache.Checked := True;

    ChkTask := TNewCheckBox.Create(Form);
    ChkTask.Parent := Form;
    ChkTask.Left := ScaleX(16);
    ChkTask.Top := ChkCache.Top + ScaleY(26);
    ChkTask.Width := Form.ClientWidth - ScaleX(32);
    ChkTask.Height := ScaleY(22);
    ChkTask.Caption := CustomMessage('UninstOptTask');
    ChkTask.Checked := HasAutoTask;
    ChkTask.Enabled := HasAutoTask;

    UninstProfileCheck := TNewCheckBox.Create(Form);
    UninstProfileCheck.Parent := Form;
    UninstProfileCheck.Left := ScaleX(16);
    UninstProfileCheck.Top := ChkTask.Top + ScaleY(26);
    UninstProfileCheck.Width := Form.ClientWidth - ScaleX(32);
    UninstProfileCheck.Height := ScaleY(22);
    if HasProfileDir then
      UninstProfileCheck.Caption := CustomMessage('UninstOptProfile') + ' (' + FormatSizeKB(ProfileKB) + ')'
    else
      UninstProfileCheck.Caption := CustomMessage('UninstOptProfile');
    UninstProfileCheck.Checked := False;
    UninstProfileCheck.Enabled := HasProfileDir;
    UninstProfileCheck.OnClick := @OnUninstProfileCheckClick;

    UninstBackupCheck := TNewCheckBox.Create(Form);
    UninstBackupCheck.Parent := Form;
    UninstBackupCheck.Left := ScaleX(34);
    UninstBackupCheck.Top := UninstProfileCheck.Top + ScaleY(24);
    UninstBackupCheck.Width := Form.ClientWidth - ScaleX(50);
    UninstBackupCheck.Height := ScaleY(22);
    UninstBackupCheck.Caption := CustomMessage('UninstOptBackup');
    UninstBackupCheck.Checked := False;
    UninstBackupCheck.Enabled := False;

    BtnApply := TNewButton.Create(Form);
    BtnApply.Parent := Form;
    BtnApply.Caption := CustomMessage('UninstBtnApply');
    BtnApply.Width := ScaleX(160);
    BtnApply.Height := ScaleY(30);
    BtnApply.Left := Form.ClientWidth - BtnApply.Width - ScaleX(16);
    BtnApply.Top := Form.ClientHeight - BtnApply.Height - ScaleY(14);
    BtnApply.ModalResult := mrOk;
    BtnApply.Default := True;

    BtnSkip := TNewButton.Create(Form);
    BtnSkip.Parent := Form;
    BtnSkip.Caption := CustomMessage('UninstBtnSkip');
    BtnSkip.Width := ScaleX(132);
    BtnSkip.Height := ScaleY(30);
    BtnSkip.Left := BtnApply.Left - BtnSkip.Width - ScaleX(8);
    BtnSkip.Top := BtnApply.Top;
    BtnSkip.ModalResult := mrCancel;
    BtnSkip.Cancel := True;

    if Form.ShowModal = mrOk then
    begin
      if ChkReg.Checked then
        CleanResidualRegistryKeys;
      if ChkTask.Checked then
        UnregisterScumAutoLaunchTask(DataDir);
      if ChkCache.Checked then
        CleanAllRuntimeCachesAndTemp;
      if UninstProfileCheck.Checked and HasProfileDir then
      begin
        if UninstBackupCheck.Checked then
        begin
          Copied := BackupUserProfileToDesktop(DataDir, BackupDir);
          if Copied > 0 then
            MsgBox(FmtMessage(CustomMessage('BackupCreatedMsg'), [BackupDir]), mbInformation, MB_OK);
        end;
        UnregisterScumAutoLaunchTask(DataDir);
        DelTree(DataDir, True, True, True);
      end;
    end
    else
    begin
      // Even if the user keeps profile data, always remove broken registry entries pointing to the deleted .exe
      CleanResidualRegistryKeys;
    end;
  finally
    UninstProfileCheck := nil;
    UninstBackupCheck := nil;
    Form.Free;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\ZiloreMiniMap');
    if UninstallSilent then
    begin
      CleanResidualRegistryKeys;
      CleanAllRuntimeCachesAndTemp;
      if CmdLineHasSwitch('/PURGEDATA') and DirExists(DataDir) then
      begin
        UnregisterScumAutoLaunchTask(DataDir);
        DelTree(DataDir, True, True, True);
      end;
    end
    else
      ShowEnterpriseUninstallCleanup(DataDir);
  end;
end;
