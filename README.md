# KeyLangSwitcher

Аналог PuntoSwitcher для Windows: конвертирует только что набранный или выделенный текст между раскладками RU ↔ EN по горячей клавише, работает глобально во всех приложениях.

## Возможности

- Конвертация **всего недавно набранного текста** (от начала ввода до нажатия хоткея) — пример: `vfvf` → `мама`, `Z nt,z k.,k.` → `Я тебя люблю`.
- Конвертация **выделенного текста** по тому же хоткею (через буфер обмена; clipboard восстанавливается).
- Глобальный low-level keyboard hook — работает в Visual Studio, браузере, мессенджерах, терминале и т.д.
- Сброс буфера на Enter / Tab / Esc / стрелках / клике мыши / смене окна / по таймауту.
- Хоткей по умолчанию — **Ctrl + Win** (настраивается в окне настроек).
- Иконка в трее, автозапуск, JSON-настройки в `%AppData%\KeyLangSwitcher\settings.json`.

## Сборка

Требуется **.NET 8 SDK** на Windows.

```powershell
dotnet build -c Release
# Single-file публикация:
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false
```

Бинарь окажется в `KeyLangSwitcher\bin\Release\net8.0-windows\win-x64\publish\KeyLangSwitcher.exe`.

## Сборка инсталлятора (Windows)

Нужен **.NET 8 SDK** и **Inno Setup 6** (https://jrsoftware.org/isinfo.php).

Одной командой из корня репо в PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

Скрипт сделает `dotnet publish` (single-file, self-contained) и упакует через Inno Setup. Готовый `KeyLangSwitcher-Setup-0.1.0.exe` появится в `dist\`.

Альтернатива вручную:
```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\KeyLangSwitcher.iss
```

## Запуск

Просто запустите `KeyLangSwitcher.exe` — появится иконка в системном трее. Двойной клик по иконке открывает настройки.

### Права администратора

Для работы хоткея и `SendInput` в окнах, запущенных от имени администратора (RegEdit, диспетчер задач и т.п.), запускайте KeyLangSwitcher **тоже от имени администратора**. Иначе обычные приложения работают без UAC.

## Структура проекта

```
KeyLangSwitcher/
  Program.cs                 — точка входа, single-instance mutex
  App.cs                     — координатор: связывает хуки, буфер, хоткей
  Hooks/
    KeyboardHook.cs          — WH_KEYBOARD_LL
    MouseHook.cs             — WH_MOUSE_LL, сброс буфера на кликах
    ForegroundWatcher.cs     — SetWinEventHook, сброс буфера при смене окна
  Core/
    LayoutConverter.cs       — таблица соответствия JCUKEN ↔ QWERTY
    TypingBuffer.cs          — thread-safe буфер набранного текста
    Sender.cs                — SendInput: Backspace, Unicode, Ctrl+C/V
    SelectionConverter.cs    — конвертация выделения через clipboard
    HotkeyConfig.cs          — модель хоткея
  Settings/
    AppSettings.cs           — JSON в %AppData%
    Autostart.cs             — реестр HKCU\...\Run
  UI/
    TrayContext.cs           — NotifyIcon и меню
    SettingsForm.cs          — окно настроек + диалог записи хоткея
```

## Тесты

```powershell
dotnet test
```

Покрытие: `LayoutConverter` (карта раскладок, AutoConvert), `TypingBuffer`
(курсор, Backspace/Delete, навигация), `AutoDetector` (эвристика).

## Авто-исправление при наборе

Включается чекбоксами в настройках:

- **"Автоматически исправлять раскладку (бета)"** — на разделителе
  (пробел/знак препинания) анализируем последнее слово консервативно:
  - Аббревиатуры (всё верхним регистром) никогда не трогаются.
  - Слово ≥2 символов → проверяем по словарю обеих раскладок.
  - Слово ≥5 символов вне словаря → эвристика по гласным.
  - Если последние 3+ слова были на одном языке, неоднозначное слово
    в этом же языке остаётся как есть — не выдёргиваем посреди фразы.
  - После каждой автокоррекции 2 секунды cool-down: следующее слово не
    трогается, чтобы дать возможность набирать без каскадных исправлений.

  Базовые словари (~500 EN + ~500 RU частотных слов) встроены в сборку.
  Их можно расширить:
  - `%AppData%\KeyLangSwitcher\words_en.user.txt`
  - `%AppData%\KeyLangSwitcher\words_ru.user.txt`
  Формат: одно слово в строке, в нижнем регистре, строки с `#` — комментарии.

- **"Авто-правка: CapsLock, ПРивет→Привет"** — две типографических
  автокоррекции:
  - "пРИВЕТ" → "Привет" (случайно нажат CapsLock).
  - "ПРивет" → "Привет" (двойная заглавная в начале слова).
  Аббревиатуры в верхнем регистре (USB, ФБР) не трогаются.

### Откат и обучение

Любая автокоррекция откатывается клавишей **Pause/Break** — на экране
восстанавливается исходное слово, системная раскладка возвращается обратно.

Если в течение **5 секунд** после автокоррекции нажать **Backspace** —
коррекция откатывается, и исходное слово запоминается в
`%AppData%\KeyLangSwitcher\never_fix.txt`. Это слово больше никогда не
будет автокорректироваться. Файл можно редактировать вручную (одна
строка на слово, lowercase).

## Дальнейшее развитие

- Звуковая индикация конвертации.
- "Запомни — это слово я не хочу исправлять": учёт пользовательских откатов.
- N-gram-модель для слов вне словаря.
- Автозамена (свой словарь сокращений: `мдр` → `мне это нравится`).
- Исключения по приложениям.

## Ограничения

- Только Windows 10/11.
- Окна с правами выше — см. раздел выше про администратора.
- Конвертация выделения зависит от Ctrl+C / Ctrl+V — в приложениях без поддержки буфера обмена не сработает.
