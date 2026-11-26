# ZiloreMiniMap Opt-In Telemetry Worker (`report.zilore.app`)

Серверная часть для opt-in отправки диагностических отчётов об ошибках из `src/Core/ErrorReportService.cs`.

## Быстрый деплой за 3 шага

1. Установите и авторизуйте Wrangler CLI:
   ```powershell
   npm install -g wrangler
   wrangler login
   ```
2. Создайте R2-бакет и задайте секретный токен:
   ```powershell
   cd cloud\telemetry-worker
   wrangler r2 bucket create zilore-telemetry-reports
   wrangler secret put ZILORE_REPORT_TOKEN
   ```
   *(Опционально для уведомлений об `ERROR` в Discord: `wrangler secret put ALERT_WEBHOOK_URL`.)*
3. Разверните Worker:
   ```powershell
   wrangler deploy
   ```
4. Проверьте работу:
   ```powershell
   curl https://report.zilore.app/health
   ```
   Ожидаемый ответ: `{"ok":true,"service":"zilore-telemetry","schema":1}`.
