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

## Дальнейшее развитие

- Автоматическое определение неверной раскладки (n-gram эвристика по словарям) — поле `AutoDetectWrongLayout` в настройках зарезервировано, ядро ещё не реализовано.
- Звуковая индикация конвертации.
- Иконка `.ico` (сейчас используется системная заглушка).
- Юнит-тесты для `LayoutConverter`.

## Ограничения

- Только Windows 10/11.
- Окна с правами выше — см. раздел выше про администратора.
- Конвертация выделения зависит от Ctrl+C / Ctrl+V — в приложениях без поддержки буфера обмена не сработает.
