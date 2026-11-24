# 🤝 Как помочь проекту / Contributing Guide

[Русский](#-русский) · [English](#-english)

---

## 🇷🇺 Русский

Мы рады любому вкладу игроков и разработчиков! Помочь проекту можно даже без навыков программирования:

1. **Сообщать об ошибках:** используйте форму [Bug Report](https://github.com/Zilore-Anatoliy/ZiloreMiniMap/issues/new?template=bug_report.yml) и прикладывайте диагностический ZIP из программы (*О программе → Выгрузить лог*).
2. **Предлагать улучшения:** описывайте идеи через форму [Feature Request](https://github.com/Zilore-Anatoliy/ZiloreMiniMap/issues/new?template=feature_request.yml).
3. **Улучшать переводы и документацию:** исправления опечаток и уточнения в `README.md`, `docs/` и файлах локализации всегда приветствуются.
4. **Pull Requests (для разработчиков):**
   * Не добавляйте никаких API чтения/записи памяти игры или инъекции DLL (строгая политика **EAC-Safe**).
   * Не коммитьте приватные ключи, `.pfx`, `.key` или содержимое папки `secrets/`.
   * Перед отправкой PR убедитесь, что сборка `.NET 10` и проверки `.\scripts\Test-PerformanceReadiness.ps1` проходят без ошибок.

---

## 🇬🇧 English

Contributions from both players and developers are warmly welcome!

1. **Report Bugs:** Use the [Bug Report](https://github.com/Zilore-Anatoliy/ZiloreMiniMap/issues/new?template=bug_report.yml) template and attach the diagnostic ZIP exported from *About → Export Log*.
2. **Suggest Features:** Share ideas via [Feature Request](https://github.com/Zilore-Anatoliy/ZiloreMiniMap/issues/new?template=feature_request.yml).
3. **Improve Docs & Translations:** Clarifications and fixes in `README.en.md`, `docs/`, or UI localization are always appreciated.
4. **Pull Requests (for developers):**
   * Never introduce game memory reading, DLL injection, or `unsafe` hacks (strict **EAC-Safe** policy).
   * Never commit private keys, `.pfx`, `.key`, or anything inside `secrets/`.
   * Ensure `.NET 10` builds cleanly and `.\scripts\Test-PerformanceReadiness.ps1` passes with zero errors.
