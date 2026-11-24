# ❓ Частые вопросы / Frequently Asked Questions (FAQ)

[Русский](#-русский) · [English](#-english)

---

## 🇷🇺 Русский

### 1. Можно ли получить бан в SCUM (Easy Anti-Cheat) за ZiloreMiniMap?
**Нет.** ZiloreMiniMap не читает память игры, не модифицирует файлы SCUM и не внедряет DLL. Программа работает как обычное внешнее окно поверх игры и получает ваши координаты через официальную встроенную кнопку SCUM **Copy Location** (`Num /`).

### 2. Почему миникарта не видна поверх игры SCUM?
В 99% случаев SCUM запущен в режиме **«Полный экран» (Fullscreen)**.
**Решение:** откройте в SCUM *Настройки → Видео → Режим отображения* и выберите **«Окно без рамки» (Windowed Fullscreen / Borderless)**, затем нажмите «Применить».

### 3. Карта открылась, но позиция игрока не обновляется
1. Убедитесь, что в настройках управления SCUM на действие **Copy Location** назначена клавиша **`Num /`** (или та же клавиша, которую вы указали в настройках ZiloreMiniMap).
2. Если у вас компактная клавиатура без цифрового блока (Numpad), откройте мастер настройки или *Настройки → Клавиши* и выберите любую удобную клавишу без Numpad.
3. Проверьте, не запущен ли SCUM от имени администратора — если игра запущена с правами администратора, запускайте и ZiloreMiniMap с теми же правами.

### 4. Windows SmartScreen показал предупреждение при запуске установщика — это вирус?
**Нет.** Установщик и `.exe` подписаны цифровой подписью разработчика Zilore (`SHA-256 + RFC 3161 TSA`). Microsoft SmartScreen показывает предупреждение новым сборкам независимых разработчиков, пока они не наберут репутацию.
* Вы всегда можете проверить хэш файла по **`SHA256SUMS.txt`** из релиза.
* Для запуска нажмите **«Подробнее» → «Выполнить в любом случае»**.

### 5. У меня монитор 2K или 4K (144 Гц) — как получить максимальную плавность карты?
Откройте **Настройки → Внешний вид** и включите **«Аппаратное ускорение карты M (GPU)»**, затем нажмите «Перезапустить сейчас». Это переключит полную карту на прямой аппаратный GPU-рендеринг (`DirectTerrain`).

### 6. Сохранятся ли мои метки, маршруты, зоны и настройки при обновлении?
**Да.** Все ваши данные хранятся отдельно в папке профиля Windows (`%LOCALAPPDATA%\ZiloreMiniMap`). При обновлении через установщик или встроенный автообновлятор все настройки остаются на месте. Дополнительно вы можете сделать резервную копию в один клик: **О программе → Поддержка → Бэкап профиля (ZIP)**.

### 7. Как перенести настройки и сохранённые точки на другой компьютер?
1. На старом ПК откройте **О программе → Поддержка → 💾 Бэкап профиля (ZIP)** — архив сохранится на Рабочий стол.
2. На новом ПК установите ZiloreMiniMap, откройте **О программе → Поддержка → ♻ Восстановить из ZIP**, выберите этот архив и перезапустите программу.

### 8. Как настроить серверное время SCUM под мой сервер?
Нажмите на иконку часов **`🕒`** справа на тактической карте (`M`). Выберите скорость цикла вашего сервера (`3.84x Official`, `4x`, `6x`, `8x` или введите точное значение `TimeOfDaySpeed`), укажите текущее время в игре и нажмите **«Применить и синхронизировать»**.

### 9. Что бесплатно, а зачем нужен ключ доступа POI / Zilore AI?
Вся основа программы (миникарта, тактическая карта, маршруты по дорогам, отряд, часы, климат, авиация, зоны, голос, темы) **полностью бесплатна**. Расширенные фильтры базы точек интереса (POI) и AI-модуль активируются офлайн-ключом доступа, который можно получить в благодарность за добровольную поддержку проекта на [Boosty](https://boosty.to/minimap.zilore.app/purchase/4184863?ssource=DIRECT&share=subscription_link) или [ЮMoney](https://yoomoney.ru/to/410013990138306).

---

## 🇬🇧 English

### 1. Can ZiloreMiniMap trigger an Easy Anti-Cheat (EAC) ban in SCUM?
**No.** ZiloreMiniMap never reads game memory, never modifies SCUM files, and never injects DLLs. It operates purely as an external overlay window using SCUM's built-in **Copy Location** hotkey (`Num /`).

### 2. Why is the minimap not showing on top of SCUM?
SCUM is likely running in exclusive **Fullscreen** mode.
**Fix:** Open SCUM *Settings → Video → Display Mode* and switch to **Windowed Fullscreen (Borderless)**, then apply.

### 3. The map opens, but my player position does not update
1. Verify that **Copy Location** in SCUM *Settings → Controls* is bound to **`Num /`** (matching the key configured in ZiloreMiniMap).
2. If you use a compact/TKL keyboard without a Numpad, open the Setup Wizard or *Settings → Shortcuts* and assign a non-Numpad key.
3. If SCUM is running as Administrator, launch ZiloreMiniMap with matching permissions.

### 4. Why does Windows SmartScreen warn me when launching Setup?
Windows SmartScreen warns about newly published software signed by independent developers until download reputation builds up. You can verify the file's digital signature and compare its checksum against **`SHA256SUMS.txt`** from the release page, then click **More info → Run anyway**.

### 5. How do I get maximum smoothness on a 2K / 4K 144Hz monitor?
Open **Settings → Appearance**, enable **Hardware-accelerated M map (GPU)**, and click **Restart now**.

### 6. Will my waypoints, routes, zones, and settings survive an update?
**Yes.** All user data lives in `%LOCALAPPDATA%\ZiloreMiniMap` and is preserved across updates. You can also export a complete backup anytime via **About → Support → 💾 Backup Profile (ZIP)**.

### 7. How do I transfer my profile to another PC?
Click **About → Support → 💾 Backup Profile (ZIP)** on your old PC, copy the generated Desktop ZIP to your new PC, and click **♻ Restore from ZIP** inside ZiloreMiniMap.

### 8. How do I sync the SCUM server clock?
Click the clock icon **`🕒`** on the tactical map (`M`), choose your server's cycle speed (`3.84x Official`, `4x`, `6x`, `8x`, or custom), enter the current in-game time, and click **Apply & Sync Clock**.

### 9. What is free vs. what uses an access key?
The core app (HUD minimap, tactical map, road navigation, squad radar, server clock, climate model, custom zones, aviation mode, voice guidance, profile backup, and themes) is **100% free**. Extended POI database filters and Zilore AI unlock via offline access keys available through voluntary project support on [Boosty](https://boosty.to/minimap.zilore.app/purchase/4184863?ssource=DIRECT&share=subscription_link) or [YooMoney](https://yoomoney.ru/to/410013990138306).
