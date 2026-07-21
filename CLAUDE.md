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

## Архитектура (МАКСИМАЛЬНО ПРОСТАЯ — пользователь требовал упростить)

Один процесс с трей-иконкой. Работает **ТОЛЬКО с ВЫДЕЛЕННЫМ текстом**.
**НЕТ буфера набранного текста, словарей, автокоррекции, пословной логики.**
НЕ возвращать их — пользователь явно потребовал убрать (постоянно ломались).

- `Program.cs` — точка входа, single-instance mutex, установка UI SyncContext.
- `App.cs` — **координатор**. Ставит ТОЛЬКО keyboard hook. `OnKeyDown` матчит
  два хоткея и постит в UI-поток `SelectionConverter.ConvertSelection()` /
  `ToggleSelectionCase()`. Больше ничего.
- `Hooks/KeyboardHook.cs` — `WH_KEYBOARD_LL`, нужен ТОЛЬКО для детекции хоткеев.
  Модификаторы через `GetAsyncKeyState`, игнорирует инжектированные
  (`LLKHF_INJECTED`). Если нажатая клавиша сама модификатор — флаг ставится
  сразу (иначе Ctrl+Win не матчился из-за тайминга).
- `Core/SelectionConverter.cs` — **вся логика**. `ConvertSelection` (раскладка)
  и `ToggleSelectionCase` (регистр). Обе: ждут отпускания модификаторов →
  сохраняют clipboard как ТЕКСТ → Ctrl+C (проба 120мс) → преобразуют →
  **`SendUnicode` печатает результат, заменяя выделение (НИКАКОГО Ctrl+V!)** →
  (для раскладки) переключают системную раскладку → восстанавливают clipboard.
  Конвертированный текст НИКОГДА не кладётся в clipboard → история Win+V чистая.
- `Core/LayoutConverter.cs` — таблица JCUKEN↔QWERTY (`PairsLower`/`PairsUpper`,
  Shift-символы `@"`, `#№`, `&?`, `|/`, `~Ё`, `` `ё`` и т.д.). `ToRussian`/
  `ToEnglish` — 1-в-1. `AutoConvertWithDirection` — считает латиницу vs
  кириллицу, конвертит ВЕСЬ текст в доминантную сторону.
- `Core/Sender.cs` — SendInput: `SendUnicode` (по одному символу с задержкой
  ~25мс — Electron/React теряют batched), `SendCtrlKey` (снимает лишние
  модификаторы перед Ctrl+C), `SendBackspaces`, `ReleaseHotkeyModifiers`,
  `SendRightArrow` (последние три не используются, но оставлены).
- `Core/ClipboardSafe.cs` — SetText с флагами исключения из истории Win+V и
  cloud clipboard. **copy:true обязателен** (иначе clipboard пустой).
- `Core/LayoutSwitcher.cs` — `WM_INPUTLANGCHANGEREQUEST` активному окну.
- `UI/TrayContext.cs` — NotifyIcon + меню. `UI/SettingsForm.cs` — настройки:
  Включено, автозапуск, два хоткея (layout через per-row FlowLayoutPanel).
- `Settings/AppSettings.cs` — JSON в `%AppData%\KeyLangSwitcher\settings.json`
  (Enabled, Autostart, ConvertHotkey, ChangeCaseHotkey).
- `Settings/Autostart.cs` — реестр `HKCU\...\Run`.

## ПОВЕДЕНИЕ — не усложнять

- **Хоткей конвертации**: выделение → Ctrl+C → 1-в-1 смена раскладки (включая
  пунктуацию: `,`→б и т.д.) → SendUnicode заменяет выделение → переключение
  системной раскладки.
- **Хоткей смены регистра**: выделение → Ctrl+C → toggle (есть заглавная → всё
  lower, иначе всё upper) → SendUnicode заменяет выделение.
- Нет выделения → **ничего не делаем**.

НЕ добавлять обратно: буфер, словари, автокоррекцию при печати, пословную
конверсию, Ctrl+V-вставку. Всё это пользователь потребовал убрать.

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
