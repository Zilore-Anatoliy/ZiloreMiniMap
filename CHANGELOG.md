# 📜 История изменений / Changelog

Все значимые изменения проекта **ZiloreMiniMap** документируются в этом файле.
All notable changes to **ZiloreMiniMap** are documented in this file.

---

## [v0.0.1] — 2026-11-24

### 🇷🇺 Русский

#### 🆕 Новые возможности
* **Современный интерфейс Windows 11 Fluent Design:** единая оболочка всех окон и диалогов, собственные Fluent-уведомления и подтверждения, 4 темы (`Системная`, `Тёмная`, `OLED`, `Светлая`) с мгновенным переключением без перезапуска.
* **Адаптивные HD/4K иконки День / Ночь:** фирменная иконка для `.exe`, трея, панели задач, `Alt+Tab`, заголовков окон, установщика (`Setup`) и деинсталлятора (`Uninstall`) с автоматической сменой светлого и тёмного стиля под системную тему Windows.
* **Серверные часы SCUM и Климатическая модель:** точная калибровка времени сервера (`1x`, `3.84x Official`, `4x`, `6x`, `8x` или свой множитель), оценка температуры по координатам/высоте с предупреждением о переохлаждении и опциональный пассивный детектор неба/дождя (`EAC-Safe`).
* **Zilore AI — пассивный помощник выживания:** мультимониторный трекер баров HUD (здоровье, энергия, вода) и адаптивный расчёт времени прибытия (ETA).
* **Офлайн-навигатор и отряд:** прокладка дорожных маршрутов с обходом опасных зон, маршруты до 16 точек с оптимизацией и экспортом кода `ZLR-ROUTE`, авиационный режим (`✈`) с ВПП, индикаторы связи тиммейтов (`●` / `◐` / `○`) и встроенный голосовой навигатор.
* **Удобное обслуживание:** бэкап и восстановление всего профиля в 1 клик (ZIP на Рабочий стол), экспорт безопасных диагностических логов для поддержки и автоматическая цифровая подпись релизов (Authenticode SHA-256 + RFC 3161 TSA + CMS `.p7s`).

#### ⚡ Быстродействие и стабильность
* **Плавность 60–144+ Гц:** Zero-GC оптимизация горячего рендера карты, бесшовный стриминг тайлов, квантование кластеров POI при автозуме и отдельный режим GPU-ускорения (`DirectTerrain`) для мониторов 2K / 4K.
* **100% EAC-Safe и `.NET 10 LTS`:** полностью автономная 64-битная сборка без чтения памяти игры и без необходимости устанавливать сторонние рантаймы.

---

### 🇬🇧 English

#### 🆕 Added
* **Modern Windows 11 Fluent Design UI:** Unified Fluent window chrome and custom dialogs, 4 live themes (`System`, `Dark`, `OLED`, `Light`) with instant switching and zero restarts.
* **Adaptive HD/4K Day/Night Icons:** High-definition icons for `.exe`, system tray, taskbar, `Alt+Tab`, window title bars, Setup installer, and Uninstaller that automatically adapt to Windows Light/Dark themes.
* **SCUM Server Clock & Climate Engine:** Zero-jump server clock calibration (`1x`, `3.84x Official`, `4x`, `6x`, `8x`, or custom multiplier), coordinate/elevation temperature estimation with hypothermia alerts, and optional passive sky/rain detection (`EAC-Safe`).
* **Zilore AI Passive Survival Companion:** Multi-monitor safe HUD bar tracking (health, energy, hydration) and adaptive route ETA calculation.
* **Offline Road Routing & Squad Radar:** Road-network navigation with danger-zone avoidance, multi-stop routes (up to 16 stops) with optimization and `ZLR-ROUTE` sharing, Aviation mode (`✈`) with custom runways, live squad connection badges (`●` / `◐` / `○`), and offline voice navigation.
* **User Convenience & Security:** 1-click Profile Backup & Restore (Desktop ZIP), safe diagnostic log export, and fully signed releases (Authenticode SHA-256 + RFC 3161 TSA + detached CMS `.p7s`).

#### ⚡ Performance & Stability
* **60–144+ Hz Smooth Rendering:** Zero-GC map rendering pipeline, seamless terrain tile streaming, quantized POI clustering during speed auto-zoom, and optional GPU hardware acceleration (`DirectTerrain`) for 2K / 4K monitors.
* **100% EAC-Safe & Self-Contained `.NET 10 LTS`:** Zero game memory reading, zero DLL injection, and zero external runtime prerequisites.
