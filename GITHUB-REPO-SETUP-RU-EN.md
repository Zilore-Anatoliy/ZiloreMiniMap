# 🚀 Пошаговое создание репозитория ZiloreMiniMap на GitHub с нуля (RU / EN)

Этот файл — готовая шпаргалка для создания, оформления и публикации вашего репозитория **`ZiloreMiniMap`** на GitHub.

---

## Шаг 1. Создание репозитория на GitHub

1. Войдите в аккаунт GitHub (`Zilore-Anatoliy`) и нажмите **`+` → `New repository`** (или откройте `https://github.com/new`).
2. Заполните поля:
   * **Repository name:** `ZiloreMiniMap`
   * **Visibility:** `Public` *(чтобы обычные игроки могли скачивать релизы и открывать Issues)*
   * **Add a README / .gitignore / license:** оставьте **все галочки СНЯТЫМИ** — все готовые файлы уже находятся в этом комплекте.
3. Нажмите **Create repository**.

---

## Шаг 2. Заполнение карточки «About» (Описание и Теги / Topics)

На главной странице созданного репозитория нажмите на шестерёнку **⚙** справа от заголовка **About** и заполните:

### Описание (Description — выберите RU, EN или двуязычный вариант)

* **Рекомендуемый двуязычный вариант (до 350 символов, идеален для поиска в GitHub):**
  ```text
  Tactical minimap, offline road navigator & squad radar for SCUM (Windows 10/11 Fluent UI, .NET 10, 100% EAC-Safe) | Тактическая миникарта, офлайн-навигатор и командный радар для SCUM
  ```
* **Только EN:**
  ```text
  Next-gen tactical minimap, offline road navigator, squad radar, server clock & survival companion for SCUM (Windows 10/11 Fluent UI, .NET 10, 100% EAC-Safe).
  ```
* **Только RU:**
  ```text
  Тактическая миникарта, офлайн-навигатор по дорогам, командный радар, серверные часы и помощник выживания для SCUM (Windows 10/11 Fluent UI, .NET 10, 100% EAC-Safe).
  ```

### Website (необязательно — ссылка на существующий адрес проекта)
```text
https://minimap.zilore.app
```

### Теги (Topics — скопируйте и добавьте в поле Topics, макс. 20 тегов)
```text
scum
scum-game
scum-map
minimap
tactical-map
game-overlay
eac-safe
windows-11
fluent-ui
wpf
dotnet-10
csharp
offline-navigation
squad-radar
survival-game
scum-grand
zilore
desktop-app
```

### Включите полезные разделы в About и Settings
* В окне **About** отметьте галочками: **Releases** (обязательно) и при желании **Discussions**.
* В **Settings → General → Features** включите:
  * **Issues** (формы багов и идей подхватятся автоматически из `.github/ISSUE_TEMPLATE/`),
  * **Sponsorships** (справа на странице репозитория автоматически появится кнопка **Sponsor / Поддержать** со ссылками на Boosty и ЮMoney из `.github/FUNDING.yml`).
* В **Settings → General → Social preview** загрузите `resources\icons\ZiloreMiniMap-Icons-Preview.png` (красивая карточка при отправке ссылки на репозиторий в Discord и Telegram).

---

## Шаг 3. Распаковка файлов и первая загрузка на GitHub

1. Распакуйте архив `ZiloreMiniMap-GitHub-Repository-Kit-RU-EN.zip` в корень папки `C:\Users\AsRock\Downloads\ZiloreMiniMap` (с заменой `README.md`, `.gitignore` и новых папок `.github/`, `docs/`).
2. **Обязательная проверка безопасности перед загрузкой:** убедитесь, что в `.gitignore` есть `secrets/`, `*.pfx`, `*.key`, `*.dpapi`, `release/`, `bin/`, `obj/`, `logs/`.
3. Откройте PowerShell в папке `C:\Users\AsRock\Downloads\ZiloreMiniMap` и выполните:

```powershell
# 1. Инициализация локального Git-репозитория с веткой main
git init -b main

# 2. Проверка списка файлов (убедитесь, что папки secrets/ и release/ НЕ попали в список!)
git status

# 3. Первый коммит
git add .
git commit -m "Initial release: ZiloreMiniMap v0.0.1 (Fluent UI, RU/EN docs, GitHub community kit)"

# 4. Подключение вашего удалённого репозитория и отправка кода
git remote add origin https://github.com/Zilore-Anatoliy/ZiloreMiniMap.git
git push -u origin main
```

> **Важно:** если вы хотите публиковать на GitHub **только релизы и документацию без исходного кода `src/`**, просто добавьте строку `src/` и `scripts/` в `.gitignore` перед выполнением `git add .`.

---

## Шаг 4. Публикация первого релиза `v0.0.1` в GitHub Releases

1. Соберите чистую подписанную сборку `v0.0.1`:
   ```powershell
   .\scripts\Build-FullRelease.ps1 -CleanOldReleases -OpenReleaseFolder
   ```
2. На странице репозитория GitHub перейдите в **Releases → Draft a new release**.
3. Заполните поля:
   * **Choose a tag:** введите `v0.0.1` → *Create new tag: v0.0.1 on publish*
   * **Release title:** `ZiloreMiniMap v0.0.1`
   * **Describe this release:** скопируйте готовый красивый текст из файла **`release\GITHUB-RELEASE.md`** (конвейер `Build-FullRelease.ps1` генерирует его автоматически!).
4. Перетащите в блок **Attach binaries (Assets)** готовые подписанные файлы из папки `release\`:
   * `release\installer\ZiloreMiniMap-Setup-v0.0.1.exe` *(подписанный установщик)*
   * `release\net10\ZiloreMiniMap.zip` *(портативный архив)*
   * `release\net10\ZiloreMiniMap.zip.p7s` *(подпись архива)*
   * `release\net10\update.txt` и `release\net10\update.txt.p7s` *(для встроенного автообновления)*
   * `release\SHA256SUMS.txt` и `release\SHA256SUMS.txt.p7s` *(контрольные суммы и их подпись)*
   * `release\ZiloreCodeSigning.cer` *(публичный сертификат для проверки подписи)*
5. Отметьте **Set as the latest release** и нажмите **Publish release**.

Готово! Теперь репозиторий выглядит как полноценный Enterprise-продукт, встроенный автообновлятор видит релиз `v0.0.1`, а игроки получают понятную документацию на русском и английском языках.
