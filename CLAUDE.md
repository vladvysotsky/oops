# CLAUDE.md — KeyLangSwitcher

Аналог PuntoSwitcher для Windows. Конвертирует набранный/выделенный текст между
раскладками RU↔EN по горячей клавише, работает глобально во всех приложениях.

## Стек / сборка

- **C# .NET 8 + WinForms** (`net8.0-windows`, `WinExe`, `UseWindowsForms=true`).
- **Только Windows** — WinForms + Win32 P/Invoke. На Linux НЕ собирается и НЕ
  тестируется (нет `Microsoft.NET.Sdk.WindowsDesktop`). Все проверки — на Windows.
- Собрать/запустить (Windows, PowerShell 7 или cmd):
  ```
  dotnet build -c Release
  dotnet run --project KeyLangSwitcher
  dotnet test                     # xunit, только на Windows
  ```
- Single-file exe:
  ```
  dotnet publish KeyLangSwitcher -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
  ```
  Результат: `KeyLangSwitcher\bin\Release\net8.0-windows\win-x64\publish\KeyLangSwitcher.exe`.
- Инсталлятор: `installer\build.ps1` (нужен Inno Setup 6). Скрипт использует
  оператор `?.` — требует **PowerShell 7**, не 5.1.
- Манифест — **asInvoker** (без UAC). ВАЖНО: `requireAdministrator` ломает
  автозапуск через `HKCU\...\Run` (Windows молча игнорирует elevated-записи на
  старте). Не возвращать requireAdministrator.

## Git / рабочий процесс

- Ветка разработки: `claude/amazing-bardeen-ZvoqL`. Пушить только в неё.
- PR #1 уже создан и **замержен**; новые коммиты в ветку обновляют историю.
- Пользователь собирает и тестирует сам на своей Windows-машине. После каждого
  изменения он делает `git pull` + `dotnet clean` + `dotnet publish`. Если баг
  «не воспроизводится» — первым делом проверить, что у него собран последний
  коммит (`git log --oneline -1`).

## Архитектура

Один процесс с трей-иконкой. Ключевые части:

- `Program.cs` — точка входа, single-instance mutex, установка UI SyncContext.
- `App.cs` — **координатор**. Ставит хуки, держит `TypingBuffer`, обрабатывает
  `OnKeyDown`: матчит хоткеи, ведёт буфер (навигация/Backspace/Delete/стрелки),
  запускает конвертацию/смену регистра/автокоррекцию.
- `Hooks/KeyboardHook.cs` — `WH_KEYBOARD_LL`. Резолвит символ через `ToUnicodeEx`
  (флаг 0x4 «не менять состояние»), модификаторы через `GetAsyncKeyState`,
  игнорирует свои инжектированные события (`LLKHF_INJECTED`). Если нажатая
  клавиша сама модификатор — соответствующий флаг ставится сразу (иначе
  Ctrl+Win не матчился из-за тайминга GetAsyncKeyState).
- `Hooks/MouseHook.cs`, `Hooks/ForegroundWatcher.cs` — сброс буфера на клике /
  смене окна.
- `Core/LayoutConverter.cs` — таблица JCUKEN↔QWERTY (`PairsLower`/`PairsUpper`,
  включая Shift-символы `@"`, `#№`, `&?`, `|/`, `~Ё`, `` `ё`` и т.д.).
  - `ToRussian`/`ToEnglish` — чистая 1-в-1 посимвольная конверсия.
  - `AutoConvertWithDirection` — считает латиницу vs кириллицу, конвертит ВЕСЬ
    текст в доминантную сторону (используется для выделения — 1-в-1).
  - `AutoConvertPerWord` — пословно; конвертит ТОЛЬКО уверенно-битые слова
    (через `AutoDetector`), возвращает `(Result, dir, anyChange, anyKnown)`.
    Символы `; ' [ ] \` : " { } ~` считаются частью латинского слова (они =
    русские буквы ж/э/х/ъ/ё).
- `Core/AutoDetector.cs` — определяет «слово набрано не в той раскладке»:
  ALL-CAPS → Keep; словарь (exact + fuzzy ≤1 правка) обеих раскладок; fallback по
  плотности гласных для слов ≥5 симв. `'y'` НЕ считается EN-гласной (маппится на
  «н»).
- `Core/WordDictionary.cs` — embedded словари `Resources/words_ru.txt` (~3400) и
  `words_en.txt` (~1770). ё→е нормализация. Fuzzy: substitution/deletion/
  insertion/adjacent-swap. Пользователь может дополнять
  `%AppData%\KeyLangSwitcher\words_{ru,en}.user.txt`.
- `Core/Sender.cs` — SendInput: `SendBackspaces`, `SendUnicode` (по одному
  символу с задержкой ~25-50мс — Electron/React теряют batched события),
  `SendCtrlKey` (снимает лишние модификаторы перед Ctrl+C/V),
  `ReleaseHotkeyModifiers`, `SendRightArrow`.
- `Core/ClipboardPaste.cs` — атомарная вставка: сохранить clipboard как ТЕКСТ
  (не IDataObject — COM-прокси крашит процесс через combase!), SetText,
  Ctrl+V, отложенное (1с, UI-поток) восстановление.
- `Core/ClipboardSafe.cs` — SetText с флагами исключения из истории Win+V и
  cloud clipboard. **copy:true обязателен** (иначе clipboard пустой).
- `Core/SelectionConverter.cs` — Ctrl+C проба (таймаут 120мс) → 1-в-1 конверсия
  → Ctrl+V → переключение раскладки → отложенное восстановление clipboard.
- `Core/CaseConverter.cs` — смена регистра: приоритет выделению (Ctrl+C round-
  trip, ждёт отпускания модификаторов), fallback на буфер. `Toggle`: есть
  заглавная → всё lower, иначе всё upper.
- `Core/LayoutSwitcher.cs` — `WM_INPUTLANGCHANGEREQUEST` активному окну.
- `Core/LayoutTracker.cs` — сброс буфера при ручной смене раскладки.
- `Core/TypingBuffer.cs` — буфер с курсором; Backspace/Delete/Left/Right/Home/
  End редактируют; выход за границы или Enter/Tab/Esc/Up/Down → Clear; idle-
  таймаут.
- `Core/NeverFixList.cs` — слова, которые автокоррекция не трогает (обучение
  через Backspace после автокоррекции). Файл `%AppData%\...\never_fix.txt`.
- `Core/Typography.cs` — `FixAccidentalCapsLock` (пРИВЕТ→Привет),
  `FixDoubleCapital` (ПРивет→Привет), аббревиатуры не трогает.
- `UI/TrayContext.cs` — NotifyIcon + меню. `UI/SettingsForm.cs` — настройки
  (два хоткея, чекбоксы, idle-таймаут; layout через per-row FlowLayoutPanel).
- `Settings/AppSettings.cs` — JSON в `%AppData%\KeyLangSwitcher\settings.json`.
- `Settings/Autostart.cs` — реестр `HKCU\...\Run`.

## ПОВЕДЕНИЕ КОНВЕРТАЦИИ — не ломать (пользователь требовал это многократно)

Хоткей конвертации (`App.RunConvert`):
1. **ЕСТЬ ВЫДЕЛЕНИЕ → ВСЕГДА 1-в-1 конверсия всего выделенного + переключение
   раскладки.** Независимо от буфера. Никаких словарей/пословных догадок.
   Это главное требование. Выделил = явное намерение = переверни ровно это.
2. Нет выделения → пословная умная конверсия буфера: конвертит ТОЛЬКО
   уверенно-битые слова. **НИКОГДА не трогает корректный текст. Нет whole-buffer
   fallback** (он раньше портил двуязычный текст в гибериш — запрещён).

Хоткей смены регистра (`App.RunCaseToggle`): приоритет выделению, иначе буфер.

Автокоррекция при печати — **опция, выкл по умолчанию** («эксперимент»).

## Хоткеи по умолчанию

- Конвертация: **Ctrl+Win** (modifier-only). Настраивается.
- Смена регистра: **Alt+Shift+S**. Настраивается.

## Известные грабли

- LL keyboard hook отключается Windows при отладке (F5) из-за таймаута — тестить
  через **Ctrl+F5** (без дебаггера) или опубликованный exe.
- Восстановление clipboard — ТОЛЬКО как текст, отложенно на UI-потоке. Хранение
  `IDataObject` через задержку → краш combase.dll (0xc000041d).
- SendInput большими пачками теряется в Electron/React — слать по одному
  символу с задержкой.
- Не гонять `dotnet test` на Linux — не соберётся (WinForms SDK).
