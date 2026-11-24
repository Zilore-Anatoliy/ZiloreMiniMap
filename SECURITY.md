# 🛡️ Политика безопасности и EAC-совместимость / Security Policy

[Русский](#-русский) · [English](#-english)

---

## 🇷🇺 Русский

### 1. Гарантии совместимости с Easy Anti-Cheat (EAC)
ZiloreMiniMap разработана как строго пассивная внешняя утилита:
* **0% чтения/записи памяти игры:** в кодовой базе полностью отсутствуют вызовы `ReadProcessMemory`, `WriteProcessMemory`, `VirtualAllocEx`, `CreateRemoteThread`, `LoadLibrary` и любые механизмы инъекции.
* **Автоматический контроль перед каждым релизом:** конвейер сборки включает статические проверки безопасности (`Test-PerformanceReadiness.ps1`), которые блокируют сборку при обнаружении запрещённых API или небезопасного кода (`unsafe`).
* **Штатный ввод координат:** координаты поступают исключительно из встроенной игровой команды SCUM *Copy Location* (`Num /`).

### 2. Проверка подлинности релизов
Все официальные сборки публикуются только в разделе [GitHub Releases](https://github.com/Zilore-Anatoliy/ZiloreMiniMap/releases) и защищены тремя уровнями проверки:
1. **Authenticode (SHA-256 + RFC 3161 TSA)** на `ZiloreMiniMap-Setup-v*.exe` и `ZiloreMiniMap.exe`.
2. **CMS / PKCS#7 (`.p7s`)** откреплённые криптографические подписи на `ZiloreMiniMap.zip`, `SHA256SUMS.txt` и `update.txt`.
3. **SHA-256 контрольные суммы** в файле `SHA256SUMS.txt` и публичный сертификат `ZiloreCodeSigning.cer`.

### 3. Сообщить об уязвимости
Если вы обнаружили проблему безопасности:
* **Не публикуйте** детали уязвимости в открытых Issues до её устранения.
* Напишите напрямую на **[`support@zilore.app`](mailto:support@zilore.app)** с темой `[SECURITY] ZiloreMiniMap`.
* Мы подтверждаем получение обращения и координируем выпуск исправления в приоритетном порядке.

---

## 🇬🇧 English

### 1. Easy Anti-Cheat (EAC) Compliance Guarantees
ZiloreMiniMap is strictly a passive external companion tool:
* **Zero game memory access:** no `ReadProcessMemory`, `WriteProcessMemory`, `VirtualAllocEx`, `CreateRemoteThread`, `LoadLibrary`, or DLL injection mechanisms exist anywhere in the codebase.
* **Automated pre-release security gates:** every build passes static security checks blocking forbidden memory/injection APIs and `unsafe` code blocks.
* **Official game input only:** coordinates come strictly from SCUM's built-in *Copy Location* command (`Num /`).

### 2. Verifying Release Authenticity
Official releases are distributed exclusively via [GitHub Releases](https://github.com/Zilore-Anatoliy/ZiloreMiniMap/releases) with three layers of cryptographic verification:
1. **Authenticode (SHA-256 + RFC 3161 TSA)** embedded signatures on `ZiloreMiniMap-Setup-v*.exe` and `ZiloreMiniMap.exe`.
2. **Detached CMS / PKCS#7 (`.p7s`)** signatures for `ZiloreMiniMap.zip`, `SHA256SUMS.txt`, and `update.txt`.
3. **SHA-256 checksums** in `SHA256SUMS.txt` alongside the public `ZiloreCodeSigning.cer` certificate.

### 3. Reporting a Vulnerability
If you discover a security vulnerability:
* **Please do not** disclose it in public GitHub Issues before a fix is available.
* Email **[`support@zilore.app`](mailto:support@zilore.app)** with subject `[SECURITY] ZiloreMiniMap`.
* We will acknowledge your report promptly and coordinate a fix.
