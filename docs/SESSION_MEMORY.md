# Project memory

Last updated: **2026-11-12**, Asia/Yekaterinburg.
Current versions: **macOS 1.3.2 (19)** and **Windows 1.4.1**.

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
