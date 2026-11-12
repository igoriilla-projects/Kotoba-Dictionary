# Kotoba for Windows 11

The Windows port preserves the macOS screens, palette, layout and study
workflows, including the macOS 1.3.2 kanji exploration features. Interface and
installer labels are Russian; dictionary translations remain Russian.
Windows version **1.4.1** is Windows 11 x64 only, with independent local learning
data and desktop cards. Mac import/export and synchronization are not included.

## Install and use

Run `build/windows/Kotoba-Setup-x64.exe`. The per-user installer requires no
administrator rights or separately installed .NET. It installs into
`%LOCALAPPDATA%\Programs\Kotoba`, adds Kotoba to Start, and registers the existing
`yarxi-study:` entry/list/settings links. The package includes its own runtime
under `.runtime`. The main application is `Kotoba.exe`.

The four destinations and study behavior match the Mac: Сегодня, Мои списки,
Словарь (Поиск/Обзор), and Настройки. Save to Входящие, organize into multiple
lists, reveal the reading/meaning and grade Again/Good/Easy. Progress survives
removing an entry and saving it again. Reviews show hiragana regardless of the
Explore reading preference.

Space reveals a review card or advances a Dictionary card; 1/2/3 grade a revealed
review. Ctrl+Z undoes the latest review; Ctrl+, opens Settings; Ctrl+C copies
selected text. Shortcuts do not consume text-box input. The copy command and
result-row context menus copy the complete Japanese headword.

Settings retain separate 50–200% word/reading/translation sizes, a 0–200 daily
new-entry limit (default 20), hiragana/rōmaji, and the same refresh intervals as
macOS (default hourly). Explore timers stop when the app is inactive, another
destination is selected, or a review is in progress.

User files are `%LOCALAPPDATA%\Kotoba\learning.sqlite` and `settings.json`.
Learning writes use WAL, full synchronization and atomic transactions. Errors
show Retry and do not select another data location. Update/uninstall preserves
these user files. The packaged dictionary is always read-only.

This is a local unsigned installer. There is no auto-updater or network service.
Windows system title bars and font metrics differ from macOS; the Japanese font
is bundled Noto Serif CJK JP, with the license included.

## Desktop cards

Open **Настройки → Виджет рабочего стола**, then choose **Малый**, **Средний**, or
**Большой**. Kotoba keeps one card: choosing another size resizes the existing card
and preserves its entry, position, source, display preferences and timer. It is an ordinary frameless window,
not a Windows Widgets Board provider or a desktop-shell extension.

- Drag its KOTOBA header to move it. Position and current entry survive relaunch.
- Click its content to open that exact entry in the single main window.
- **Save** commits to the same learning database, shows Saved only after success,
  and preserves the current card. **Next** or Space advances it once.
- Right-click → **Edit widget** (or click ⋯) to choose size, Dictionary/Learning
  list source, Inbox/named list, content, reading format, visibility, independent
  50–150% word/reading/translation sizes, refresh interval and **Always on top**.
- Empty/deleted lists show instructions; choose another list in the editor.
- Closing the main window keeps cards and timers running. Double-click the
  Kotoba notification-area icon to reopen it; its context menu shows/resizes the card
  or quits Kotoba. Windows may put the icon in the tray overflow.
  **Quit Kotoba** also exists in Settings and each card's context menu.

Main-app settings do not change the card. Its selection is persisted separately
from the app's Explore screen. Display-only edits preserve the current
entry/token and schedule; source/content changes choose a new entry. Changing the
timer restarts its interval. An overdue timer advances once and reschedules, rather
than racing through missed intervals. There is no automatic login-start registration;
launch Kotoba to restore cards after exiting it or restarting Windows.

Card configuration and selections live in `%LOCALAPPDATA%\Kotoba\widgets.json`.
Updates use atomic replacement; a failed write preserves the previous entry and
token. Save/Next use the displayed selection, with stale-token checks for Next.
The installer preserves both this file and learning data on update/uninstall.
When upgrading from 1.3.0 with several cards, Kotoba keeps the original (first)
card unchanged. Previous settings are retained in `widgets.json.multiple-backup`;
the learning database is unaffected. Removing the card disables it; selecting a
size afterwards creates a single new card.

Without Always on top, other windows can cover cards. Win+D and the taskbar follow
normal Windows window rules, so behavior differs from macOS desktop widgets.
Tray → **Показать виджет** brings the card forward. No Explorer/WorkerW hooking is used.

## Kanji exploration and copy feedback (1.4.0)

- In a word card (Dictionary or Review), click an individual red kanji. Only the
  glyph under the pointer becomes muted blue-gray; kana/blank space stay plain.
  Native RichTextBox selection, wrapping, Ctrl+C and keyboard review controls remain.
- The separate **Кандзи и слова** window shows readings, meaning/detail and all
  usable words containing that character in the spelling. Related words ignore
  the dictionary filter and its normal 100-result cap: 図 has 84 results and 一 901.
  The list flows directly below the definition in the same card and uses its
  single outer scrollbar; there is no fixed-height list viewport or nested scrollbar.
- Related rows open words; their kanji allow further exploration. **Назад** pops
  one page and **Готово** returns to the original card with its view/scroll tree,
  review/reveal state and selection intact. Exploration never grades an entry.
  Explore's timer pauses during lookup and restarts a full interval on closing.
- Kanji cards opened directly in Dictionary/Explore also include **Слова с кандзи …**
  below their meaning. Hiding the translation hides this section, matching macOS.
- **Копировать** copies the complete headword and shows **Скопировано** for two
  seconds after a successful clipboard API call, or **Не скопировано** on failure.
  Repeated clicks restart the feedback duration; labels keep a stable width.

Russian localization changes display strings and formatting only. Content/feed
values, list IDs, preference keys, URL routes, database schema and widget tokens
retain their previous identities; user-created list names are never translated.
The single desktop widget policy from 1.3.1 remains enforced. macOS resources,
source files and the original dictionary inputs are unchanged by this port.

## Build

This repository tracks source code, not installers or dictionary databases.
Before the first build after cloning, place your own `YARXI.dmg` in the repository
root and run `python tools/extract_database.py YARXI.dmg`. See
[Data/README.md](../Data/README.md). Alternatively, copy the unchanged local source
image and both database files from an already prepared checkout; do not add them
to Git. The full Python data tests need all three local inputs, while the Windows
app/core tests consume `Data/dictionary.sqlite`.

Use Windows 11 x64, .NET SDK 10, Python 3 and PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build_windows.ps1
```

Outputs: `build/windows/app/Kotoba.exe` and
`build/windows/Kotoba-Setup-x64.exe`. Build configuration lives in the Windows
`.csproj` files and `Windows/Installer/Kotoba.iss`; `project.yml` remains the
macOS configuration source and requires no change for Windows-only files.

`tools/prepare_windows.py` prepares Inno Setup 7.1.1 in portable mode within the
workspace and the official .NET 10.0.15 runtime ZIPs, verifying pinned hashes.
First build requires access to GitHub and builds.dotnet.microsoft.com. Subsequent
builds reuse the verified cache. No NuGet packages or global installations are
required: SQLite comes from Windows. See [provenance](../Windows/PROVENANCE.md).

The app is published framework-dependent and then packaged with an app-local
runtime; its .NET 10 apphost resolves `.runtime` before any global installation.
This achieves independent startup without downloading NuGet runtime packs.

## Validate

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test_windows.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test_windows_installer.ps1
```

The first command runs the C# contracts, the original Python dictionary tests,
and an actual offscreen WPF window with deterministic temporary learning data.
Screenshots and a JSON report are in `build/windows-previews`. Screens cover
Today, lists, search, kanji/word details, both reading styles, review/reveal,
settings, maximum text sizes, minimum window size, an empty list and storage
failure/recovery. Smoke tests invoke real navigation/review buttons.

Widget smoke coverage adds all three sizes, kanji/word/romaji, enlarged fonts,
the settings editor, empty-list state, real Save/Next button handlers, saving
while the main window is hidden, reopening its exact entry and restoring the
single card/position/selection. The combined smoke currently renders 38 states.
Additional assertions cover per-glyph rectangles/colors, repeated kanji, kana and
blank exclusion, wrapping, native selection retention, nested lookup/Back/Done,
unchanged source/review state, copy-feedback timing/failure via an injected writer
(the user's clipboard is not overwritten), and localized options retaining raw IDs.
The 1.4.1 regressions raise handled WPF mouse-down/up events through the registered
input handlers, verify dictionary/review lookup windows actually open, reject
kana and drag gestures, and check that one outer scrollbar reaches the last of all
901 related rows. Injected positions avoid moving the user's cursor. These checks
replace the activation-only coverage that missed native mouse-up consumption.
Native tray menu interaction, mouse dragging, Show Desktop and multi-monitor DPI
behavior require a normal desktop check; offscreen previews do not establish them.
The native tray registration probe returned false in this restricted runner;
neither the Explorer shell window nor its tray window was accessible there, so
tray visibility and menu interaction are not yet desktop-verified. If registration
fails, Settings reports it; card menus still reopen or quit the app.

The installer test uses a named workspace directory and `/TESTMODE=1` to suppress
Start/protocol/uninstall-registry changes. It installs the real payload, runs
its WPF smoke, reinstalls it, uninstalls it and checks that the temporary learning
database was preserved. This does not verify the normal user's Start shortcut or
protocol registration, which need a normal desktop install.

The restricted coding environment denies cross-process named-pipe client access.
In that environment, pass `-AllowSandboxIpcSkip` to the build/test script to
explicitly allow SKIP for UnauthorizedAccessException. Normal runs require IPC
to pass; other failures always remain failures. Single-instance routing is implemented, and
entry/list/settings routing is tested in the WPF window, but production second-launch
activation still needs confirmation outside this sandbox. Manual checks also
remain for monitor DPI switching, Japanese IME, clipboard and real sleep/resume.
Rendered WPF screenshots are visual evidence, not user desktop acceptance.
