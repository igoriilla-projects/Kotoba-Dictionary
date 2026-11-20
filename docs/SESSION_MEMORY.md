# Project memory

Last updated: **2026-11-20**, Asia/Yekaterinburg.
Current versions: **macOS 1.3.2 (19)** and **Windows 1.4.4**.

## Current project state

- This Git repository contains the macOS app/WidgetKit extension, independent
  Windows WPF app/desktop card, source converter, tests and development documentation.
- The app and widget UI are Russian. Hiragana is the default reading format;
  source romaji remains selectable. Russian meanings come from the source.
- Small/medium/large widgets support Save and Next, dictionary and learning-list
  feeds, separate timer/reading/display preferences and numeric 50–150% sizes.
- App text sizes independently support 50–200%. Copy shows a success/failure
  result only after the clipboard write. Success lasts two seconds.
- Clicking a kanji in a word opens its readings/meaning and all words containing
  it. Normal dictionary kanji cards show the same complete related-word list.
- Native TextKit headwords keep full-word text selection, per-character blue-gray
  hover, a hand cursor and Russian tooltips; kana and adjacent kanji are unaffected.
- Learning includes Inbox/named lists, shared per-entry progress, FSRS reviews,
  daily new-entry quota, history/Undo and resumable sessions.

## Local source data and build

Source images and dictionary databases are **not tracked**. See
[Data/README.md](../Data/README.md) to prepare a local `YARXI.dmg`, unchanged
`Data/yarxice.db`, and generated `Data/dictionary.sqlite`. The checked-in manifest
records the verified source hashes, schema and counts. Do not force-add these
files, learning data, app bundles, previews or runtime logs to Git.

The export preserves 6,355 kanji and 60,000 words; 6,286 kanji and 55,915 words
are eligible for study/search. The raw DB is a custom binary format, not SQLite.
Changes to the export must be reproduced through tools/extract_database.py.

Use project.yml as configuration source, regenerate with `xcodegen generate`
after changing target/source/resource membership, and package with tools/build.sh.
Both bundle versions must stay aligned. The output is `build/Kotoba.app`.
The script verifies nested signatures and keeps stable bundle-identifier
requirements for local ad hoc builds. Compiler plugins in a restricted runner
may need the documented `-Xfrontend -disable-sandbox` flag; app/extension App
Sandbox entitlements remain enabled.

## Compatibility and behavioral invariants

- Keep the unique `Window` scene with ID `main`; widget links focus/update it.
- Keep existing bundle IDs, App Group `group.local.yarxi.study`, URL scheme,
  widget kind/module and serialized enum/parameter identities across updates.
- Dictionary bundles are read-only. Learning uses the shared App Group database;
  never fall back to a private learning DB on an access failure.
- App and individual widget preferences remain independent. Optional reading
  style must retain its hiragana fallback for older widget configurations.
- Save must commit before reporting success and must preserve widget selection.
  Next atomically compares displayed entry ID and selection token. Neither intent
  explicitly reloads WidgetKit; the extra reload previously caused two advances.
- Widget selection plans stay in private extension storage. Identical resolved
  configurations share a stream; font changes do not change that selection key.
- Progress belongs to canonical entry IDs and survives list removal/restoration.
  Ratings/history/session/Undo remain atomic. New-entry quota counts first ratings
  across all lists using local calendar-day boundaries.
- Native headword hit testing must use actual glyph bounds and a single composed
  character range; nearest-glyph and merged-link-run ranges are insufficient.
  Independent TextKit fitting avoids stale-width measurements on font/width changes.

## Confirmed widget recovery findings

Two distinct update failures were observed and recovered on the desktop:

1. A worker from an older build can survive a local update. Host archive rejection
   then reports `Bundle version did not match` / `bundleStubNotSupported`.
   Run `bash tools/restart_widget.command` in normal Terminal, then in the app
   choose **Настройки → Настройка виджетов → Обновить виджеты**.
2. Moving/renaming the app while its worker/host still uses the old path can leave
   a launch job in chronod's PID domain. launchd logs `Attempt to re-bootstrap
   service from different path, will use existing`; the old executable can trap
   in ExtensionFoundation with `Invalid bundle record for current process`.
   The diagnosed recovery is `bash tools/restart_widget.command --refresh-host`,
   which also restarts the user's chronod and NotificationCenter. It briefly
   reloads desktop widgets and does not delete placement/settings/learning data.

Do not remove the previous active bundle path before its worker has exited.
Merely copying sources into a different checkout does not switch the active app.
Check the actual registered and running bundle path when changing checkouts.

During the Kotoba rename, the old worker also cached an old gallery display name
under the new build number. Advancing aligned versions forced a fresh descriptor;
read-only inspection and the user's gallery check confirmed the corrected name.
Do not edit Apple's widget database or reset all system widget state for this case.

The old enum font controls failed in the locally ad hoc-signed host environment.
The confirmed repair was primitive integer size fields, with hidden optional enum
inputs retained for compatibility. Keep the current numeric parameter metadata.

## Validation and desktop confirmations

Automated checks cover source hashes/lossless extraction, all record counts,
search/voicing/reading formats, the timer, complete related-word lists (including
901 words for 一), learning concurrency/transactions/reviews/Undo/quota/session
recovery, widget provider/selection behavior and packaged metadata/signatures.
Native headword tests cover individual hover, kana/blank-space exclusion, wrapped
200% text, repeated kanji, text selection and canonical click routing.

User-confirmed desktop behavior includes:

- 1.2.0: bidirectional shared access, Save with app closed preserving the card,
  one-step Next, saved Inbox entry, both feeds/all sizes, single-window links,
  review/Undo and session resume.
- 1.2.6: numeric widget font fields work from 50–150%.
- 1.2.8: widget/card naming and rendering recovered after the rename.
- 1.2.9: word → individual kanji → related words and Done return.
- 1.3.1: only the hovered kanji changes color; clicking opens its card.
- 1.3.2: direct dictionary kanji 図 → 84 related words → opening a word.

Actual macOS permission denial/recovery remains untested; automated checks use
injected unavailable storage. Do not equate builds, provider tests or PNG previews
with desktop host or App Group authorization. Drag-copy and review keyboard
behavior with the new native headword were not separately desktop-confirmed.
See [DEVELOPMENT.md](DEVELOPMENT.md) for commands and architecture.

## Windows import — 2026-11-12

The user requested adding the already developed Windows version to this macOS
repository. Imported Windows 1.4.1 sources/solution, OFL Japanese font, provenance,
four Windows build/test helpers and the Windows guide. macOS source/configuration
and generated Xcode project are unchanged. README now documents both platforms;
data setup and third-party notices cover Windows too. The Windows package also
includes the project's MIT license and third-party notices when built.

Windows uses C#/WPF/.NET 10 and the native winsqlite3 API, with a Russian interface,
the same read-only local dictionary, independent user data in `%LOCALAPPDATA%\Kotoba`,
lists/FSRS/history/Undo/resumable reviews and kanji drill-down. Its one desktop card
supports separate display/timer/feed options, Save/Next, positioning, Always on top
and tray control. Size selection updates the same card; legacy duplicates collapse
to the first card with a backup. Its 1.4.1 click fix observes already-handled WPF
mouse events; related words flow under the meaning with one outer scrollbar.

Build/test commands and environmental limits are in [WINDOWS.md](WINDOWS.md).
No installers, caches, source image, original/SQLite dictionaries or learning data
are included in the commit. The font's OFL and vendored FSRS MIT notices are retained.
Real tray/Win+D/DPI/clipboard acceptance remains distinct from offscreen WPF tests;
this restricted runner cannot access the Explorer shell and denies named-pipe
client access. Only an explicit `-AllowSandboxIpcSkip` permits that IPC test skip.

Verification repeated from this repository checkout: 85 C# contracts and all 11
Python dictionary checks pass; Release builds without warnings and offscreen WPF
smoke renders 38 states. The newly packaged installer passes install → installed
smoke → reinstall → uninstall while preserving learning.sqlite/widgets.json.
The own-code MIT license and third-party notices are present in the app package.
Documentation links and ignore rules were checked; dictionary inputs, installers,
bin/obj and learning state stay untracked. No macOS build was rerun because its
implementation/project files did not change.

## Windows 10 installer target — 2026-11-16

The user requested a Windows 10 build. Windows 1.4.2 lowers the installer minimum
from Windows 11 (`10.0.22000`) to Windows 10 22H2 (`10.0.19045`), retaining x64,
the app-local .NET 10.0.15 runtime, the existing AppId and user-data paths.
Assembly/project, manifest, installer and About versions are aligned at 1.4.2.
No application logic or macOS sources/configuration changed.

This checkout initially lacked local dictionaries and dependency archives. Copied
YARXI.dmg, unchanged yarxice.db, dictionary.sqlite and the three pinned dependency
archives from `D:\PetProjects\yarxi-mac-os`; source/hash tests and dependency hash
verification passed. All local data, caches and outputs remain ignored.

`tools/build_windows.ps1` initially hit the known named-pipe sandbox denial.
Rerunning outside the sandbox passed all 87 C# checks, including IPC, without a
skip. The Release build has zero warnings/errors; all 11 Python dictionary checks
and the 38-state offscreen WPF smoke passed via `tools/test_windows.ps1`.
`tools/test_windows_installer.ps1` also passed isolated install → installed WPF
smoke → reinstall → uninstall, preserving learning.sqlite and widgets.json.

The installer is `build/windows/Kotoba-Setup-x64.exe` (about 87 MiB). Validation
runs on Windows 11 build 26200, not Windows 10. Actual Windows 10 22H2 startup,
tray/card/DPI behavior and normal desktop acceptance remain unverified. The .NET
10 support matrix currently lists Windows 10 LTSC/Enterprise editions; the wider
Windows 10 22H2 installer target is not a Microsoft support claim.

## Windows dictionary search — 2026-11-18

The user reported long pauses while typing Russian queries. Each keystroke
previously ran a synchronous SQLite search and rebuilt up to 100 WPF rows.
Version 1.4.3 defers typing-triggered searches for 120 ms, runs SQLite on a
background thread, and discards superseded results when the query, filter or
view changes. The list creates rows for the current viewport. Search matching,
ordering and the 100-result cap still use the existing read-only dictionary.

All 87 C# contracts, 11 Python dictionary checks and 38 offscreen WPF states
passed. The WPF test drives the real textbox, verifies Russian and hiragana
matches, changes the filter during a pending search, and scrolls to and opens
the last of 100 matches. On the development PC, six rapid textbox updates took
about 9 ms in total and the final «сигнал» results arrived in about 205 ms
including the debounce. These timings are from the offscreen Windows 11 test.
The rebuilt `build/windows/Kotoba-Setup-x64.exe` passed isolated installation,
installed WPF smoke, reinstall and uninstall with learning and widget files
preserved.

## Updated Windows dictionary — 2026-11-20

The user supplied the local SQLite source `Data/yarxi.db`. The Windows build
converts it through `tools/extract_database.py --sqlite-source Data/yarxi.db`
and packages `Data/dictionary.windows.sqlite` as `Data/dictionary.sqlite` in the
app. The original DMG, extracted binary, legacy export and tracked legacy
manifest are preserved. The Windows source and generated databases stay ignored.

`Tango.Nomer` changed between editions. The updated export preserves legacy IDs
for unambiguous matches by kanji character or by word spelling and pronunciation.
It retains 4,058 legacy word cards when a match is missing or ambiguous. A full
comparison verified that every previous study ID still resolves to the same
headword. The new source contains 13,108 kanji and 231,572 word rows. The packaged
study pool has 7,000 kanji and 76,618 words with usable Japanese spelling and
Russian text, including the retained cards. The source SHA-256 is
`fafb16d3c17f35637782498bafdc4a8cef1b16a41b35370305922dc06c9f8aab`.

Built `build/windows/Kotoba-Setup-x64.exe` as 1.4.4. All 87 C# contracts passed
with IPC outside the sandbox, and all 12 Python checks passed. Isolated upgrade
from 1.4.3 on Windows 11 installed the exact exported database, passed all 38
offscreen WPF states, and preserved the test learning and widget files through
upgrade and uninstall.
