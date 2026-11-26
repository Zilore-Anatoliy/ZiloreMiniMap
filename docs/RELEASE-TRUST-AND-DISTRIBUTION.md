# ZiloreMiniMap — Продакшен-дистрибуция, доверенная подпись и телеметрия (Фаза 6D)

## 1. Единый продакшен-конвейер (`Publish-ProductionRelease.ps1`)

Скрипт не изменяет `Build-FullRelease.ps1`, а надстраивается над ним, выполняя финальные этапы публикации:

```powershell
# Полный цикл: сборка + Azure Trusted Signing + зеркало R2 + winget + Pure WPF -Preview гейт
.\scripts\Publish-ProductionRelease.ps1 `
    -RunFullBuild `
    -SigningMode AzureTrustedSigning `
    -PublishMirror -R2Bucket "zilore-releases" `
    -GenerateWinget -ValidateWinget `
    -VerifyPreview

# Безопасный сухой прогон без загрузки и перезаписи файлов
.\scripts\Publish-ProductionRelease.ps1 -SigningMode AzureTrustedSigning -PublishMirror -R2Bucket "zilore-releases" -GenerateWinget -DryRun
```

После выполнения создаётся сводный отчёт `release\release-audit-report.json` с SHA-256 и статусами подписей всех артефактов.

---

## 2. Снятие синего экрана Windows SmartScreen (Azure Trusted Signing / OV-EV)

### Вариант A: Microsoft Azure Artifact Signing (Trusted Signing) — рекомендуемый
1. Создайте аккаунт **Artifact Signing** в Azure Portal, пройдите верификацию личности/организации и создайте профиль сертификата (`Public Trust`).
2. Установите клиентские инструменты (`Microsoft.Trusted.Signing.Client`) и создайте файл `signing\azure-trusted-signing.json` (НЕ коммитить в Git):
   ```json
   {
     "Endpoint": "https://weu.codesigning.azure.net/",
     "CodeSigningAccountName": "zilore-signing",
     "CertificateProfileName": "zilore-public-trust"
   }
   ```
3. Авторизуйтесь через `az login` и запустите подпись:
   ```powershell
   .\scripts\Invoke-TrustedSigning.ps1 -Mode AzureTrustedSigning -MetadataJson .\signing\azure-trusted-signing.json
   ```

### Вариант B: Коммерческий OV / EV сертификат (USB-токен или PFX)
```powershell
# Аппаратный EV USB-токен (SHA-1 отпечаток из certmgr.msc)
.\scripts\Invoke-TrustedSigning.ps1 -Mode OvEvPfx -CertThumbprint "YOUR_CERT_SHA1_THUMBPRINT"
```

---

## 3. Первый PR в `microsoft/winget-pkgs` (`winget install Zilore.ZiloreMiniMap`)

1. Опубликуйте релиз на GitHub (установщик `ZiloreMiniMap-Setup-v<ver>.exe` должен быть доступен по прямой ссылке `releases/download/v<ver>/...`).
2. Сгенерируйте и проверьте манифесты локально:
   ```powershell
   .\scripts\New-WingetManifests.ps1 -Validate -OpenOutputFolder
   ```
3. Сделайте fork репозитория `microsoft/winget-pkgs`, скопируйте папку `release\winget\Zilore.ZiloreMiniMap\<ver>\` в `manifests/z/Zilore/ZiloreMiniMap/<ver>/` и откройте Pull Request.

---

## 4. Сервер opt-in телеметрии (`report.zilore.app`)

Исходный код Cloudflare Worker расположен в `cloud\telemetry-worker\`.
Клиентская санитаризация в `ErrorReportService.cs` и серверная санитаризация в `worker.js` удаляют имя пользователя Windows, ключи `ZLR-POI-...` и токены, а также ограничивают размер отчёта и частоту запросов. См. `cloud\telemetry-worker\README.md`.
