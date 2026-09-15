# oops

**English** · [Русский](README.ru.md)

[![CI](https://github.com/vladvysotsky/oops/actions/workflows/ci.yml/badge.svg)](https://github.com/vladvysotsky/oops/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/vladvysotsky/oops?label=release)](https://github.com/vladvysotsky/oops/releases/latest)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

> `ghjdthrf njuj rfr 'nj hf,jnftn` → **oops** → `проверка того как это работает`

Typed a whole paragraph and only then noticed the layout was wrong? Press the
hotkey and the text falls into place. Named after that very moment.

Works in every Windows application: browsers, messengers, IDEs, the terminal.

## 🧼 What it does

- **Fixes the keyboard layout** RU ↔ EN on text you have just typed.
- **Switches case** — UPPER ↔ lower, by the same logic.
- **Converts a selection** as a whole, if something is selected with the mouse.
- **Translates** RU ↔ EN right in the input field — *beta*.
- **Types what you dictate** — *beta*.
- **Finds and replaces** inside a selection, regular expressions included — *beta*.

Translation and speech recognition run **on your machine**: neither the text nor
the audio is sent anywhere. The models are downloaded once and need no internet
afterwards.

## ⏪ You set the boundary

The program does not guess where you slipped. It does exactly what you asked for
with the number of presses:

| Press | What it captures |
|---|---|
| 1st | the last word |
| 2nd | everything you typed |

```
typed:     ghjdthrf njuj rfr 'nj hf,jnftn
1st        ghjdthrf njuj rfr 'nj работает
2nd        проверка того как это работает
```

Every step is a 1-to-1 transformation of a clearly delimited piece. Text outside
it is never touched.

Each word decides its own direction, and a word that already looks like a real
word is left alone — `appconfig` inside a Russian phrase stays `appconfig`
instead of turning into `фззсщташп`. No dictionary is involved: the check is
letter-pair statistics, under 2 KB, so `nginx` and `useState` pass too. Miss the second press within 2 seconds and a new scope
begins, again from the last word.

## ⌨️ Hotkeys

| Action | Default |
|---|---|
| Layout | `Ctrl` + `Win` |
| Case | `Alt` + `Win` |
| Translate | `Ctrl` + `Alt` + `Win` |
| Voice input | `Ctrl` + `Shift` + `Win` |
| Replace in selection | `Alt` + `Shift` + `Win` |

All of them can be changed in the settings: right-click the tray icon →
Settings.

`Alt` + `Shift` cannot be assigned — Windows keeps that combination for
switching layouts and it never reaches the application. The recording dialog
rejects it.

## 🌐 Translation, voice input and replace — beta

These arrived in 2.0 and 2.1 and have seen less mileage than the rest: expect
mistakes and misses. They are switched on in the settings, on the "Behaviour"
tab — the models are downloaded there too.

- **Translation** — the [Bergamot](https://browser.mt/) engine, the same one
  that translates pages in Firefox. Models from Mozilla's official registry,
  about 45 MB. Translates what you typed or the selection, and picks the
  direction from the text itself.
- **Voice input** — [whisper.cpp](https://github.com/ggerganov/whisper.cpp) via
  Whisper.net, the `small` model, about 465 MB. Press the hotkey and speak;
  press it again to finish. Text appears while you speak and is refined as it
  goes; if the churn distracts you, turn that off in the settings.
- **Replace in selection** — plain search and regular expressions, with a
  preview and the number of replacements shown before anything is typed. The
  wizard offers ready-made rules (collapse double spaces, strip HTML tags,
  typographic quotes) and building blocks that go into the search field.

The audio exists only in memory and only until recognition finishes — it is
never written to disk.

## 📋 The clipboard stays clean

Corrected text is typed through keyboard emulation and never reaches the
clipboard, so your `Win` + `V` history is not polluted.

The clipboard is read only to learn the current selection: Windows offers no
other universal way. The previous content is restored immediately.

## 📦 Installation

Download from the [releases page](https://github.com/vladvysotsky/oops/releases):

- **`oops-Setup-*.exe`** — a normal installation with shortcuts and autostart.
- **`oops-portable-*.zip`** — just unpack and run, no installation.

On the first run a wizard opens: it shows how the model works and offers to pick
your hotkeys. After that the program lives in the tray — double-click the icon
to open the settings.

Windows will show a SmartScreen warning on first run: the builds are not signed
with a certificate. "More info" → "Run anyway".

To check that you downloaded exactly what CI built, use the `SHA256SUMS.txt`
from the same release:

```powershell
Get-FileHash .\oops-Setup-2.1.1.exe -Algorithm SHA256
```

## 🔄 Updates

Once a day the application checks the releases and offers to install a new
version: it downloads the installer and runs it. The downloaded file is verified
against the published SHA-256 before it is launched. Turn the check off with a
checkbox in the settings; run it by hand from "Check for updates" in the tray
menu.

## 🩺 When a hotkey goes silent

Settings → "Hotkeys" → the **PROBE** card shows what actually reached the
program and whether it matched. That immediately separates "Windows kept the
combination for itself" from "the program did not recognise it".

Right below it is **DIAGNOSTICS**: a detailed log for the case where the program
stops responding after a while. Keys are written as codes, not as characters —
what you type never reaches the log.

## 🔨 Building from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
Windows only.

```powershell
git clone https://github.com/vladvysotsky/oops.git
cd oops

dotnet test
dotnet run --project Oops
```

A single-file exe:

```powershell
dotnet publish Oops -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

All of it at once — update the branch, run the tests, build and launch — is what
`dev.ps1` does (PowerShell 7 required):

```powershell
pwsh -ExecutionPolicy Bypass -File dev.ps1
```

Switches: `-NoPull`, `-NoTest`, `-NoRun`, `-Branch <name>`. The script closes a
running copy itself: it holds `oops.exe` open and the build fails otherwise.

For the installer you also need [Inno Setup 6](https://jrsoftware.org/isinfo.php)
and PowerShell 7:

```powershell
pwsh -ExecutionPolicy Bypass -File installer\build.ps1
```

The result lands in `dist\`. Releases are built automatically: pushing a
`vX.Y.Z` tag starts CI, which runs the tests, builds the installer and the
portable archive, and publishes them.

## ⚠️ Limitations

- Windows 10 and 11 only.
- In windows running as administrator (RegEdit, Task Manager) input emulation
  does not work unless the application itself runs elevated. Running it "as
  administrator" fixes that but breaks autostart: Windows ignores `HKCU\...\Run`
  entries for elevated applications.
- Under a debugger Windows disables the keyboard hook on timeout — test with
  **Ctrl+F5** or the published exe.

## 🤝 Contributing

Found a bug — [open an issue](https://github.com/vladvysotsky/oops/issues/new/choose).
The program can do it for you: the error window has a "Report a problem" button
that opens a form with the version and the details already filled in.

Before making changes, read [CLAUDE.md](CLAUDE.md) — it explains the
"expanding scope" model and collects the Win32 traps we have already stepped in:
why `GetAsyncKeyState` lies about our own modifiers, why `SendUnicode` must
clear Alt before every character, why the Win key cannot be recorded through
WinForms events. Most of that list is fixed bugs, not speculation.

## Author

[vladvysotsky](https://github.com/vladvysotsky)

## License

MIT — see [LICENSE](LICENSE).
