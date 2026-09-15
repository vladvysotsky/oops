# CLAUDE.md — oops

A Windows utility: fixes the keyboard layout (RU↔EN) and the case of text you
have just typed, on a hotkey. Works globally, in every application.

## Stack / build

- **C# .NET 8 + WinForms** (`net8.0-windows`, `WinExe`, `UseWindowsForms=true`).
- **Windows only.** It does not build on Linux (no `Microsoft.NET.Sdk.WindowsDesktop`)
  — don't try, `dotnet build`/`dotnet test` fail there. Verify on Windows.
- Build and run (PowerShell 7 / cmd):
  ```
  dotnet build -c Release
  dotnet run --project Oops
  dotnet test
  ```
- Single-file exe:
  ```
  dotnet publish Oops -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
  ```
- Build and run in one command: `pwsh -File dev.ps1` (pull → close the running
  copy → tests → publish → run). Switches: `-NoPull`, `-NoTest`, `-NoRun`,
  `-Branch`. Closing a running `oops.exe` is mandatory: it holds the file and
  publish fails with MSB3027.
- Installer: `installer\build.ps1` (needs Inno Setup 6). Our own strings in
  `Oops.iss` go through `[CustomMessages]` with the `russian.`/`english.`
  prefixes and nowhere else: the installer is bilingual, and Russian text hard
  coded into `Description` showed up in an English installation.
- `tools\model-hashes.ps1 <url>` prints a ready-made `ModelFile` line (SHA-256
  and size) for `ModelCatalog`. A checksum cannot be invented or taken from the
  same response as the file — it is measured against the actually published
  file. The script uses `?.`, so it needs **PowerShell 7**, not 5.1.
- The manifest is **asInvoker**. Do NOT set `requireAdministrator`: Windows
  silently ignores `HKCU\...\Run` entries for elevated applications, which
  breaks autostart.

## Git

- Development branch: `prerelease` — all work goes there. The user's local
  checkout is on it too. `claude/amazing-bardeen-ZvoqL` is the old branch, its
  PR #1 has been merged.
- **Commit messages, pull request titles and pull request descriptions are
  written in English.** The repository is public and the code is read by people
  who do not speak Russian; a history they cannot read is a history that does
  not help them. The rule that a message says *why*, not only *what*, stays —
  it just holds in English now.
- **Do not add `Co-Authored-By` trailers to commits.** The repository is public,
  and the contributor list is the author's. Commit messages describe the change
  and why, and nothing else.
- README lives in two files: `README.md` (English, what GitHub shows by default)
  and `README.ru.md`. They are cross-linked at the top; a change to one belongs
  in the other in the same commit.
- The user builds and tests on Windows themselves. If a bug "doesn't
  reproduce", first check that they built the latest commit
  (`git log --oneline -1`).

### Release

The order matters and corners cannot be cut — three tags in a row have already
burned on this:

1. work in `prerelease`, push there;
2. **pull request `prerelease` → `main`**, wait for green CI;
3. merge the PR;
4. the release description goes into `docs/release-notes/vX.Y.Z.md`;
5. the tag — **only via `pwsh -File tools\release.ps1 X.Y.Z`**, never by hand.

The script refuses to create the tag unless the version in csproj on
`origin/main` matches the tag and the release-notes file is there; it warns
when `origin/main` does not yet contain `origin/prerelease`. **Three tags in a
row landed on an old commit** precisely because `git tag origin/main` was run
BEFORE the pull request was merged: the tag lands silently, CI cheerfully
builds the previous version and publishes it under the new number.

A published tag cannot be moved: for anyone who already updated, the number
matches and the update will never arrive again. A spoiled number is burned and
the release goes out as the next one (that is how 2.1.0 became 2.1.1).

Why exactly this way:
- `ci.yml` runs on pull requests into `main`. A tag on an unverified commit
  publishes a broken build — that is how v2.0.0 ended up on a two-week-old
  commit, and the tests caught it only during the release run.
- The tag goes on `origin/main`, not on a local branch: the local `main` turned
  out twice not to be what it was assumed to be.
- Release notes are auto-generated **from merged PRs**. Pushing straight into
  `main` leaves a single "Full Changelog" line in the release — hence the
  `docs/release-notes/` file, which CI substitutes into the description.

## THE CORE IDEA: the "expanding scope" model

The program **never guesses** where the wrong layout began. The user sets the
boundary with repeated hotkey presses:

- 1st press — the last word,
- 2nd press (within the expansion window) — everything typed.

Two steps, not one word at a time: on a five-word phrase, word-by-word
expansion needed five presses. What gets mangled is almost always either the
last word or the whole thing.

Every step is a **1-to-1** transformation of a clearly delimited piece. Correct
text outside the scope is never touched.

**The scope continues by content, not only by the clock.** If the buffer is
still exactly what we left behind, nothing was typed after our edit and the next
press obviously continues the same scope, however long the user took. Going by
the two-second window alone produced the worst possible behaviour: press, look
at the result, press again — a new session started, took the word we had just
fixed and dutifully put it back. The second press was wasted and reaching the
goal took three. The `_step < 2` guard keeps the old way of undoing an edit:
once the scope is fully expanded, a press after the window starts a new session
and converts the last word back.

Text is rewritten with Backspace × N + `SendInput` with `KEYEVENTF_UNICODE`.
The result is **never** written to the clipboard.

### Selection

If the typing buffer is empty, the hotkey tries the selected text and converts
it whole, 1-to-1 (no expansion — the user has already set the boundary).

The order "buffer first, selection second" is not a compromise but a
consequence: text can only be selected with the mouse or Shift+arrows, and both
clear the buffer. So "there is a selection" ⇒ "the buffer is empty". The
reverse order would break in the new Notepad (UWP), where Ctrl+C with no
selection copies the entire current line.

`Core/SelectionReader.cs` is the only place that touches the clipboard, and
only to read: Windows offers no other universal way to learn the selection. The
fact that a copy happened is detected via `GetClipboardSequenceNumber` (it only
grows on a real write), and the previous content is restored immediately
through `ClipboardSafe`, using the formats that exclude it from the Win+V
history.

### What NOT to bring back

All of this existed and caused constant complaints — removed deliberately:
dictionaries (`WordDictionary`, `words_ru/en.txt`), layout guessing
(`AutoDetector`), auto-correction while typing, typography (`Typography`),
word-by-word conversion **while typing** (`AutoConvertPerWord`) — note that
choosing the direction per word inside a piece the user has already delimited is
a different thing and is what `AutoConvertWithDirection` does now,
the whole-buffer fallback,
selection handling via Ctrl+C/Ctrl+V (`SelectionConverter`, `ClipboardPaste`,
`ClipboardSafe`), `NeverFixList`.

## Architecture

- `Program.cs` — entry point, single-instance mutex, UI SyncContext.
- `App.cs` — the coordinator. Installs the hooks, maintains `TypingBuffer`,
  matches the hotkeys, drives `ScopeEditor` and applies the result via `Sender`.
- `Core/ScopeEditor.cs` — **the heart of the model**. Holds a frozen `_original`
  and the word count in the scope; on every press it expands the scope by a word
  and returns `Edit(EraseCount, Text, Direction, NewBufferContent)`.
  The invariant everything rests on: **transformations preserve length**, so
  "how many characters are on screen between the start of the scope and the
  caret" equals `Original.Length - scopeStart` regardless of how many rewrites
  came before. There is an explicit length check for exotic Unicode. Thread-safe
  (the hook and the UI thread).
- `Core/TypingBuffer.cs` — the ribbon of typed characters plus the static
  `StartOfLastWords` / `CountWords`. No cursor and no navigation — deliberately.
- `Core/LayoutConverter.cs` — the JCUKEN↔QWERTY table (`PairsLower`/`PairsUpper`,
  Shift symbols `@"`, `#№`, `&?`, `|/`, `~Ё`, `` `ё``). `ToRussian`/`ToEnglish`
  are 1-to-1; `AutoConvertWithDirection` picks the side **per word**, and
  reports the direction of the last word that had one — the caret sits at the
  end, so the system layout should match what will be typed next.
  Per word rather than per piece because of the ordinary mixed case:
  "Z djn [jxe pfgecnbnm ЬщвудКшыл", where the first words were typed in the
  English layout instead of Russian and the last one the other way round. One
  direction for the whole piece lets the majority of letters win (16 against 9),
  everything goes EN→RU, and the Cyrillic word simply does not appear in that
  table and passes through untouched. No number of presses fixed such text.
  A word is converted only when the result looks **more like a word** of the
  target language than the original does of the source one
  (`LanguageModel`, margin 0.5). Counting letters cannot tell "xtuj" from
  "appconfig" — both are pure Latin, and the only difference is that one is a
  word. Without this "appconfig" became "фззсщташп". The margin is biased
  towards leaving a word alone: a broken word can be forced with another press,
  a corrupted one in mid-phrase has to be retyped by hand. Switched off by
  `AppSettings.SmartWordSelection` when the model gets it wrong and the hotkey
  falls silent.
- `Core/LanguageModel.cs` — how much a piece looks like a word of a given
  language, used for exactly that decision. **Not the dictionary we removed**:
  no word list is stored or searched, only letter-pair frequencies including
  word boundaries — 1156 and 784 bytes, built from open word lists and quantised
  into a byte each. "фззсщташп" is rejected not for being absent from a list but
  because "зз", "сщ" and "шп" hardly occur in Russian; "nginx" and "useState"
  pass even though no list contains them.
  A deliberate fallback to brute force was **tried and rejected**: when the smart
  pass changes nothing, converting everything anyway turns
  "https://example.com" into garbage on a single press, and a second press
  cannot bring it back — the scope is already fully expanded. Silence is the
  safer failure here, and the setting is the way out.
- `Core/Sender.cs` — SendInput: `SendBackspaces`, `SendUnicode` (in small
  batches — Electron/React lose batched events), `WaitForModifiersReleased`,
  `ReleaseHotkeyModifiers`, `CancelMenuActivation`.
- `Core/ModelStore.cs` — the shared store for translation and voice models in
  `%AppData%\Oops\models\<package>`. A `ModelPackage` is a set of `ModelFile`
  (name, URL, SHA-256, size, `.gz` flag); `EnsureAsync` downloads only the
  missing files. The checksum in `ModelFile` is of the file **as published**
  (for `.gz` — of the archive): a resumed download cannot be verified against
  uncompressed data that is not on disk yet.
- `Core/Translator.cs` + `Core/ModelCatalog.cs` — local RU↔EN translation with
  the Bergamot engine (`BergamotTranslatorSharp`, MPL-2.0 — the same engine that
  translates pages in Firefox). The text **never goes to the network**: that is
  the condition under which such a feature can exist at all in a program that
  sees everything you type. The models come from Mozilla's registry; the SHA-256
  checksums were measured against the published archives and are baked into
  `ModelCatalog` (the registry arrives from the same host as the files, so
  having them vouch for each other is meaningless). The engine config is written
  on every creation: names in the config drifting from the files in the folder
  gives "Failed to create translator instance" with no explanation. Translation
  has NO expanding scope — it does not preserve length and is not reversible, so
  a second press would translate the already translated text.
  In csproj `IncludeNativeLibrariesForSelfExtract` is mandatory: without it the
  native `bergamot.dll` ends up as a file next to the exe and the single-file
  portable archive loses translation. **`IncludeAllContentForSelfExtract` is
  mandatory alongside it** — see "Traps".
- `Core/Recorder.cs` + `Core/VoiceInput.cs` — voice input: NAudio captures the
  audio, `Whisper.net` (whisper.cpp) recognises it. The recording format,
  16 kHz/mono/16-bit, is nailed down — whisper.cpp accepts nothing else. The
  audio lives in memory only: the program already sees every keystroke, writing
  speech to disk on top of that is out of the question.
  `WhisperFactory` is created once and lives until exit — the model weighs half
  a gigabyte, and recreating it would cost seconds before every phrase.
  `LimitReached` in `Recorder` is raised OUTSIDE the lock: the subscriber calls
  `Stop()`, which takes the same lock, and inside it that would deadlock on the
  audio driver's thread.
  Recognition is **streaming**: once a second `App.OnVoiceTick` runs everything
  recorded since the start of the phrase and types the difference. Whisper
  cannot "continue" — it re-parses the whole chunk every time and may change its
  mind about what was already said, so what has been typed is compared with the
  new text by common prefix: the diverging tail is erased and retyped. A tick is
  skipped while the previous pass is still running — a queue of passes would only
  fall further behind the speech. Turned off by the `VoiceLiveText` setting.
  `VoiceInput.CleanText` is mandatory: Whisper marks non-speech with text in
  square brackets (`[BLANK_AUDIO]`, `[MUSIC]`), and on silence that went
  straight into the input field instead of the phrase.
- `UI/VoiceOverlay.cs` — the "listening" panel at the bottom of the screen with
  the live transcript. The window MUST be non-activating (`ShowWithoutActivation`
  + `WS_EX_NOACTIVATE`): by taking focus it would take away the very field the
  text is being typed into.
- `Core/TextReplacer.cs` + `UI/ReplaceForm.cs` — find and replace in the selected
  text, plain and by regular expression, with a wizard (ready-made rules +
  building blocks inserted into the search field). The search **must** have a
  timeout: the expression is written by the user, and `(a+)+$` on a long line
  backtracks for hours and would hang the application together with the keyboard
  hook — that is, input across the whole system. A parse error is not thrown
  outwards: an unclosed bracket is the normal state of the field while it is
  being typed. Word boundaries go around the whole expression (`\b(?:…)\b`),
  otherwise `\bcat|dog\b` does not mean what was asked. The preview is computed
  from the ORIGINAL text, which makes "apply twice" impossible by construction.
- `Core/Log.cs` — the detailed log in `%AppData%\Oops\logs`, off by default
  (`AppSettings.VerboseLog`). **Typed text never reaches the log**: keys are
  written as codes (`VK 0x41`), not as characters. The program sees everything
  you type, and the only thing that makes it usable is that it keeps none of it;
  the log cannot be an exception. The file is capped at 8 MB, the last five are
  kept.
- `Core/HotkeyConflicts.cs` — a check at startup for whether a shortcut is
  already taken by another program (`RegisterHotKey` +
  `ERROR_HOTKEY_ALREADY_REGISTERED`). It only catches those who register the
  same way; programs with their own low-level hook cannot be detected this way
  at all. The absence of a conflict here is not a guarantee — its presence is a
  definite answer.
- `Core/LayoutSwitcher.cs` — `WM_INPUTLANGCHANGEREQUEST` to the active window.
- `Core/LayoutTracker.cs` — detects a manual layout change → clears the buffer.
- `Hooks/KeyboardHook.cs` — `WH_KEYBOARD_LL`. The character comes from
  `ToUnicodeEx` (flag 0x4, "do not change the state"), modifiers from
  `GetAsyncKeyState`, and it ignores our own injected events (`LLKHF_INJECTED`).
  If the pressed key is itself a modifier, the flag is set immediately
  (otherwise Ctrl+Win did not match because of timing).
- `Hooks/MouseHook.cs`, `Hooks/ForegroundWatcher.cs` — clear the buffer on a
  click and on a window change.
- `UI/Theme.cs` — the design system: palette, typography, 8px grid, `ThemedForm`,
  `Card`, `FlatButton`, `HotkeyDisplay` (draws a shortcut as "keycaps").
  The palette consists of **properties, not constants**: it reads the Windows
  dark theme (`AppsUseLightTheme`) and `SystemInformation.HighContrast`, and the
  values are fixed once at startup. Every window inherits `ThemedForm`
  (background, font, DPI and the dark title bar via `DwmSetWindowAttribute(20)`);
  the tray menu goes through `ApplyMenuChrome`.
- The interface was reviewed with `emil-design-eng` and `apple-design` from
  `emilkowalski/skills`. The skills themselves are NOT in the repository
  (third-party code, `.agents/` and `.claude/` are gitignored) — install them
  with a single `npx skills add emilkowalski/skills`; the exact set is pinned in
  `skills-lock.json`.
- `UI/SettingsForm.cs` — the settings window. **Three tabs** (general / hotkeys +
  probe / behaviour) on the same `SegmentedControl` as the language switch.
  The probe lives next to the hotkeys: it is needed exactly when a shortcut is
  silent and is being changed. The page height is measured after layout
  (`FitPages`) — there is no scrolling on purpose, it would hide part of the
  settings, and the window does not jump when switching tabs. The root is a
  `TableLayoutPanel` with four rows, NOT Dock.Fill+Dock.Bottom: docking order in
  WinForms depends on z-order.
  A language change is applied on the fly: `L10n.Init` plus rebuilding the
  window and the tray menu (`TrayContext.BuildMenu`), because the texts live
  inside already-created controls.
- `UI/WelcomeForm.cs` — the first-run wizard (two pages: how the model works /
  choosing shortcuts + autostart). Shown from `Program.Main` before the tray is
  created, once, guarded by `AppSettings.FirstRunCompleted`. The program appears
  to do nothing, and without an explanation of the model it is taken for broken.
- `UI/Notice.cs` — message windows instead of `MessageBox`: a badge by kind
  (info / warning / error), a separate "what to do" line, collapsible details
  with a "Copy" button and "Report a problem" (opens an issue with the body
  pre-filled). `Notice.Crash` is wired to `Application.ThreadException` and
  `AppDomain.UnhandledException` in `Program.Main`. **Write new messages only
  through it** — `MessageBox` ignores the dark theme and can offer neither a
  hint nor details.
- `UI/WhatsNewForm.cs` + `Core/Changelog.cs` — "What's new": on the left, every
  version starting from 1.0.0; on the right, what appeared in the selected one.
  Shown once after an update (`AppSettings.LastSeenVersion`) and from the tray
  menu. The body height is **fixed**: versions have different numbers of items,
  and fitting the window to the selected one would make it jump on every click
  in the list. When switching versions the old cards are disposed, not merely
  detached — otherwise they pile up and paint on top of each other (the settings
  tabs already burned on this).
  The program updates silently, and without this window people learn about a new
  feature by accident — and a shortcut nobody knows about is no different from
  one that does not exist. So every item answers "how to use this", not "what was
  done", and the shortcut is taken FROM THE SETTINGS rather than from the
  defaults: the user may have changed it. The version is remembered on every
  start, even when the window was not shown — otherwise it would pop up again
  and again. On a fresh install there is no window: the first-run wizard does
  that job.
- `UI/TrayContext.cs` — NotifyIcon and the menu.
- `Core/L10n.cs` + `Resources/lang_{ru,en}.json` — the interface strings.
  **Embedded resources only, not `.resx` with satellites**: under
  `PublishSingleFile` satellite assemblies do not go inside the exe but are laid
  out in culture subfolders next to it — the portable archive is a single file
  and would lose the translations. An unknown key is returned as-is (visible on
  screen, but it does not bring the window down); a gap in English falls back to
  Russian. `L10nTests` checks that the key sets and the placeholders match — a
  forgotten translation is caught without running the app. Language:
  `AppSettings.Language` = `auto`/`ru`/`en`; `auto` comes from
  `CultureInfo.CurrentUICulture` and the value is fixed at startup.
  **Add new strings only through `L10n.T`** — and into both dictionaries at once.
  **The underscore in the file name is mandatory.** With `lang.ru.json` MSBuild
  sees `.ru.` and treats the file as a culture resource — the dictionary moves
  into the `ru\` satellite assembly and disappears from the main one, and the
  application crashes at startup. csproj also carries `WithCulture="false"` as a
  second guard.
- `Settings/AppSettings.cs` — JSON in `%AppData%\Oops\settings.json`. `Save()`
  returns the error text, `Load()` fills `LoadError` — both failures are shown to
  the user. A silent `catch {}` is unacceptable here: a failed write meant the
  person closed the window believing their shortcuts had been reassigned, and
  got the old ones back after a restart.
  `Sanitize()` repairs settings from old files (null → default, Alt+Shift →
  default, two identical shortcuts → default).
- `Settings/Autostart.cs` — the `HKCU\...\Run` registry key. **The single source
  of truth for autostart**; there is no copy of it in `settings.json` and there
  must not be one.

## Default hotkeys

- Layout: **Ctrl+Win** (modifier-only).
- Case: **Alt+Win** (modifier-only).
- Translate: **Ctrl+Alt+Win** (modifier-only). Not Shift+Win — that is Win+Shift+S
  (screen snip), and the hotkey swallows the key that completes the chord.
- Voice input: **Ctrl+Shift+Win** (modifier-only). A toggle, not a hold:
  dictating a phrase while holding three keys is physically impossible.
- Replace in selection: **Alt+Shift+Win** (modifier-only). Alt+Shift on its own
  is forbidden (the system layout switch), but with Win it is a different chord.
- **Alt+Shift must not be assigned** — it is the Windows system shortcut for
  switching layouts and never reaches us in a usable form. The recording dialog
  rejects it and `Sanitize()` resets such saved values.

## Security

The only place where data from the network turns into executable code is the
auto-update. Invariants that must not be weakened:
- the installer and SHA256SUMS.txt URLs are accepted **only from github.com** /
  `*.githubusercontent.com` over HTTPS (`IsTrustedDownloadUrl`);
- the downloaded exe is **verified by SHA-256** against the release's
  SHA256SUMS.txt before it runs, and deleted on a mismatch; the file name in
  Temp carries a random suffix;
- `Process.Start(url, UseShellExecute)` launches anything, not just a browser
  (file://, UNC) — every URL coming from external data is checked for https.

The second place where external data reaches the disk is the model download
(`Core/ModelStore.cs`). The rules are the same: a host from a closed list over
HTTPS (`IsTrustedUrl`), a mandatory SHA-256 check **before** decompression and
before the file gets its real name (we write to `.part`), the file name taken
from the package description rather than from the server's response and passed
through `Path.GetFileName`, and a size cap. A model is not executed, but a
native library parses it — a corrupted or substituted file there gives exactly
what a substituted exe would. The download is resumable (Range): if the server
did not answer with 206, the `.part` is thrown away and fetched again —
otherwise the beginning would be appended to the remainder.

Typed text lives in memory only (`TypingBuffer`) — it is not logged, not written
to disk, not sent anywhere. The only thing that goes to disk is settings.json.
The mutex is `Local\`, not `Global\`: a global name would let a neighbouring
user block the program from starting forever.

## Traps

- The LL keyboard hook is disabled by Windows on timeout while debugging (F5) —
  test with **Ctrl+F5** or the published exe.
- **"The program stopped working at some point" — two known mechanisms, and both
  are handled by the watchdog in `App.OnHookWatchdog` (every 20 s).**
  1. *The hook was removed by the system.* Windows silently removes
     WH_KEYBOARD_LL if the callback did not answer within
     `LowLevelHooksTimeout` (300 ms). The callback lives on the thread that
     installed the hook — for us that is the UI thread, and the same thread
     waits for modifiers to be released (up to a second), types long text and
     sleeps before restoring focus. `KeyboardHook.Revive()` learns about it from
     `UnhookWindowsHookEx`: it returned false, so the hook was already gone.
  2. *A stuck modifier.* The key-up does not always arrive: it is eaten by the
     secure desktop (a UAC prompt), a session switch, RDP — and for the **Pause**
     key the driver sends an extra Ctrl press that will never get its matching
     release. The modifier is then considered held forever and no chord matches
     any more. `KeyboardHook.DropStuckKeys` throws away anything "held" for
     longer than a minute: a live chord can never fall under such a threshold.
  The watchdog reinstalls the hook only at a quiet moment — reinstalling clears
  the set of held keys, and doing it mid-chord would break the shortcut under
  the user's fingers.
- A hotkey must ignore auto-repeat (`KeyEvent.IsRepeat`): Windows sends a stream
  of WM_KEYDOWN while a key is held, and a modifier-only chord would otherwise
  fire dozens of times per hold. The hook keeps a list of held keys via KEYUP.
- `SendBackspaces` clears the modifiers before EVERY Backspace, not once before
  the loop: a held key restores the state through auto-repeat, and the Backspace
  goes out as Win+Backspace (does nothing) or Ctrl+Backspace (deletes the whole
  word).
- Our own layout switches are marked with `LayoutTracker.NoteSelfSwitch()`,
  otherwise the tracker takes them for manual ones and clears the buffer —
  breaking expansion.
- Any chord with Ctrl/Alt (other than bare modifiers) must clear the buffer: it
  is a command to the application, and it may change the text in any way.
  `Ctrl+A` especially — without a reset the buffer stays non-empty, the scope
  path is taken, and the very first Backspace eats the whole selection. Bare
  modifiers must NOT clear it: modifier-only hotkeys fire on exactly those.
- **A row of buttons goes only through `ButtonBar`, and the width is passed to it
  explicitly.** Neither `Anchor` nor `Dock` will do: both depend on how the
  parent computes its own width, and gave different results in different
  windows — the right edge of the buttons ended up either past the border or
  flush against it. `ButtonBar.Create` takes the window's `contentWidth`, gets
  its height from the font (`Theme.TextRowHeight`) and does not depend on the
  parent at all. Six attempts were burned on this.
- **Tab height is measured AFTER layout (`FitPages` from `OnLoad`), not while
  building.** Before layout the card widths are unknown, paragraphs wrap
  somewhere other than where they will, and the height comes out invented: the
  window once stretched to twice the screen height with gaping holes between the
  cards. WinForms does not lay out an invisible tab at all — during the
  measurement each one is shown for an instant. The `Card` is given its width
  right at construction: with the default 200 px the rows inside wrap paragraphs
  twenty times over. The window height is capped by the screen's working area;
  once capped, the page scrolls rather than the window, and the header and
  buttons stay put. This surfaced when cards with long paragraphs were added to
  the "Behaviour" tab.
- **`IncludeNativeLibrariesForSelfExtract` without
  `IncludeAllContentForSelfExtract` breaks voice input.** As soon as a
  single-file exe extracts anything, `AppContext.BaseDirectory` points at the
  extraction folder rather than at the folder with the exe. Whisper.net looks
  for its libraries strictly in `<BaseDirectory>\runtimes\win-x64` and ships
  them there as ORDINARY files (`None` with `TargetPath`), not as NuGet native
  assets — so the first flag does not pick them up, they stay next to the exe,
  and recognition fails with "Native Library not found in default paths". Both
  flags are required: then everything is extracted with its paths intact.
- **Every `GetPreferredSize` of ours must return no less than `MinimumSize`
  (`Theme.AtLeastMinimum`).** The parent lays the row out by the size the
  control names, and the control then brings its actual width up to
  `MinimumSize` in `SetBoundsCore` anyway. Name a size smaller than the minimum
  and the control spills out of the cell allotted to it, and the PARENT clips
  it. From the outside this looks like "the button is cut off", even though the
  button is exactly the size it needs and every padding is in place. That is how
  "Got it" was clipped: by its text it asked for 98 px, `MinimumSize` held 168,
  the row was laid out at 98, it was drawn at 168, and the extra 70 were cut off
  by the edge of the panel. Six attempts adjusted paddings and window widths —
  that is, the wrong place.
- **NOTHING SCALES US BY DPI — every pixel number goes through `Theme.Px()`.**
  The forms use `AutoScaleMode.Dpi`, but `AutoScaleDimensions` is empty on
  hand-written windows (the Visual Studio designer fills it in, and we have
  none). With an empty value WinForms treats the factor as one and touches no
  size at all: not `Size`, not `MinimumSize`, not absolute `TableLayoutPanel`
  columns. The font meanwhile is specified in points and is drawn larger at 125%
  on its own — the text grows, the box does not. Hence the WHOLE class of
  "clipped" defects: a button does not fit a 420-wide row, a 24×24 badge does
  not fit its column. At 100% there is no discrepancy at all — which is why, on
  screenshots taken with scaling, the cause read as a layout mistake six times
  over.
- **Any background we paint goes through `Theme.EffectiveBackColor(this)`, never
  `Parent.BackColor`.** On a transparent parent `BackColor` returns
  `Color.Transparent`, and `Graphics.Clear(Color.Transparent)` fills with
  ARGB(0,255,255,255) — that is, white. In the dark theme this shows wherever a
  control does not cover its own rectangle: light corners behind the rounded
  cards, a light strip at the bottom where the shadow is, a white square under
  the round badge in `Notice`. Caught three times — on the button, on the badge
  and on the card.
- The window-size safety net in `ThemedForm.OnShown` measures the content INSIDE
  the root panel, not the panel itself (its right edge IS the current width —
  comparing it with itself plus a padding grew the window on every show), and
  when it does grow the window it turns `AutoSize` off: otherwise the next
  layout pass returns the form to its own undersized preferred size and the fix
  does not survive to be painted.
- **No hard-coded heights on controls with text.** `Theme.TextRowHeight` and
  `KeyRowHeight` are computed from the font: at 125–150% Windows scaling a line
  is taller than 30–34 pixels and a constant clips the text. Three things burned
  on this: the language switch, the steppers and the button row (`ButtonBar`
  used to set its own height to 34 and cut the buttons). Height is dictated by
  `GetPreferredSize`, width by the layout column; `MinimumSize` on buttons holds
  the width only.
- In `SettingsForm` row labels must have a `MaximumSize` width: an AutoSize
  label without a limit demands the full width and pushes the right-hand control
  past the card's edge.
- A single tap of Alt activates the menu bar, a single tap of Win opens the
  Start menu — both steal focus, so `Sender.CancelMenuActivation()` (a Ctrl tap
  while the modifier is held) is called before any work. Win must be checked
  too: we only swallow the key that completed the chord, and if Win was pressed
  first, its key-down went to the system intact.
- **`GetAsyncKeyState` lies about our own modifiers — this is the main source of
  "the hotkey does not come together as a chord".** The key state in the system
  is not updated after `Handled = true` (the system never saw the swallowed
  press), and `ReleaseHotkeyModifiers`/`CancelMenuActivation` inject Alt/Win
  key-ups — after which the system considers the key released for the rest of
  the hold. The symptom: "Alt, then Win" fell apart into two separate presses
  while "Win, then Alt" came together. That is why modifiers in `KeyboardHook`
  are computed from `_physicallyDown` (built from the event stream itself), with
  `GetAsyncKeyState` left as a fallback behind an "or". For the same reason
  `HotkeyRecordDialog` keeps its own list of held keys.
- The hook delivers the **specific L/R variants** of the modifiers (`0xA4`/`0xA5`
  for Alt); the generic `VK_MENU` never arrives — both codes must be checked.
- `WaitForModifiersReleased()` is mandatory before Backspace: a held Ctrl turns
  Backspace into Ctrl+Backspace (deleting the whole word).
- SendInput in large batches gets lost in Electron/React. It is precisely the
  LARGE ones that are lost — when the whole string goes out in a single call;
  `Sender.ChunkSize` (8) sends ten at a time and a phrase is rewritten in tens
  of milliseconds instead of a second. The batch must also stay short because
  clearing the modifiers is repeated in each one: no more than the auto-repeat
  interval (~30 ms) may pass between two clears, or a held Alt comes back. The
  "Type slowly, one character at a time" setting restores the old behaviour
  (`ChunkSize = 1`).
- **`SendUnicode` must clear Alt before EVERY character.** Windows chooses
  WM_CHAR or WM_SYSCHAR by the state of Alt specifically: under a held Alt the
  character goes out as WM_SYSCHAR, input fields ignore it, and the text simply
  never appears. Ctrl does not affect KEYEVENTF_UNICODE. Hence the difference
  "Ctrl+Win works, Alt+Win does not": `WaitForModifiersReleased` waits no longer
  than a second, and a chord is held longer than that.
- **The Win key is not delivered to a WinForms form** — the shell takes it, and
  KeyDown never arrives. That is why `HotkeyRecordDialog` listens to its own
  `KeyboardHook` rather than to form events: otherwise a chord ending in Win was
  recorded as a stump without Win and then silently matched nothing. As a bonus,
  exactly what `HotkeyConfig.Matches` will see is recorded — the same vk and the
  same flags.
- The settings have a "PROBE" card with its own hook: it shows what actually
  reached the program and whether it matched. A silent hotkey is
  indistinguishable from a broken program — without that little window the cause
  had to be guessed.
- Autostart must NOT be duplicated in `settings.json`. The installer writes
  `HKCU\...\Run` itself (the `autostart` task), and saving settings with a copy
  of the field (default `false`) wiped that entry the first time the window was
  opened — the first user got "the checkbox in the installer does nothing". The
  window reads `Autostart.IsEnabled()` and writes `Autostart.Set(...)`, and
  nowhere else.
- Hotkeys must not coincide: `App.OnKeyDown` checks layout first and never
  reaches the second comparison — the second hotkey stays silent without a
  single sign. Filtered out in `Sanitize()` and when saving the settings.
- The replace dialog takes focus, so the selection is read BEFORE it is shown,
  and the window the selection lives in is remembered via `GetForegroundWindow`
  and restored via `SetForegroundWindow` before typing. Without a pause after
  the restore the first characters go to the still-closing dialog and vanish.
- An empty scope step (`ScopeEditor`, "nothing left to expand") does NOT update
  `_lastPressUtc`. Otherwise frequent presses extend the expansion window
  forever: a person presses the hotkey once a second and sees nothing at all.
