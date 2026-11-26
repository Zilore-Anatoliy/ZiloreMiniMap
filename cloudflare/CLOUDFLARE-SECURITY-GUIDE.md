# Защита сайта minimap.zilore.app (Cloudflare WAF + Edge Security Worker)

GitHub Pages отдаёт чистую статику, поэтому реальная блокировка плохих ботов, AI-скраперов (которые игнорируют `robots.txt`) и защита от Clickjacking (`frame-ancestors 'none'`, `X-Frame-Options: DENY`) настраиваются на периметре **Cloudflare**.

## Вариант 1: Деплой готового Edge Worker (максимальная защита, 2 минуты)

1. Убедитесь, что DNS-запись `minimap.zilore.app` в Cloudflare переведена в режим **Proxied** (оранжевое облако), а SSL/TLS стоит в режиме **Full (strict)**.
2. Разверните Worker из папки `cloudflare/`:
   ```powershell
   cd cloudflare
   wrangler deploy
   ```
3. Worker автоматически:
   - отдаёт `403 Forbidden` всем AI-краулерам (`GPTBot`, `ClaudeBot`, `Bytespider`, `CCBot`, `PerplexityBot`, `Meta-ExternalAgent` и др.) и сканерам уязвимостей (`sqlmap`, `nikto`, `nuclei`, `wpscan`, `python-requests`);
   - блокирует эксплойт-запросы (`/.env`, `/.git`, `/wp-admin`, `*.php`, `..`) и методы кроме `GET/HEAD`;
   - добавляет `Content-Security-Policy` с `frame-ancestors 'none'`, `HSTS Preload`, `X-Frame-Options: DENY`, `Permissions-Policy`, `COOP/CORP` и `X-Robots-Tag: noai, noimageai`.

## Вариант 2: Без Worker — правила в Cloudflare Dashboard

1. **Security → Bots**: включите **Bot Fight Mode** и **Block AI Scrapers and Crawlers**.
2. **Security → WAF → Custom Rules** (действие: `Block`):
   ```text
   (http.request.method ne "GET" and http.request.method ne "HEAD")
   or (http.user_agent eq "")
   or (http.request.uri.path contains "/.env")
   or (http.request.uri.path contains "/.git")
   or (http.request.uri.path contains "/wp-")
   or (http.request.uri.path ends_with ".php")
   or (lower(http.user_agent) matches "(gptbot|chatgpt-user|claudebot|anthropic-ai|bytespider|ccbot|perplexitybot|amazonbot|petalbot|ahrefsbot|semrushbot|mj12bot|sqlmap|nikto|nuclei|wpscan|zgrab|masscan)")
   ```
3. **Rules → Transform Rules → Modify Response Header**: добавьте `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Robots-Tag: noai, noimageai`.
