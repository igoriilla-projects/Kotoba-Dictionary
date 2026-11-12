# Development and operations

The repository also contains the independent Windows 11 port in `Windows/`.
Use [WINDOWS.md](WINDOWS.md) for Windows setup, architecture and verification;
`tools/build_windows.ps1` creates its installer. The macOS source map and commands
below remain unchanged. Platform versions are independent: macOS 1.3.2 (19),
Windows 1.4.1. Both platforms require locally prepared dictionary files.

Read [SESSION_MEMORY.md](SESSION_MEMORY.md) for the last verified version and the confirmed placeholder incident. User-facing setup is in [README.md](../README.md).

## Source map

| File | Responsibility |
| --- | --- |
| `project.yml` | XcodeGen source for app/extension targets, resources, signing, and minimum OS |
| `App/YarxiStudyApp.swift` | App scenes, study model, navigation, study/search views, timer lifecycle, entry links, widget reload requests |
| `App/LearningModel.swift`, `App/LearningViews.swift` | Learning UI, organization, reviews, retry and shared-access checks |
| `Shared/LearningDatabase.swift`, `Shared/LearningStore.swift` | App Group database, migrations, atomic ratings/history/session and Undo |
| `Shared/ReviewScheduler.swift`, `Vendor/SwiftFSRS/` | Adapter and pinned v5.0.0 basic scheduler (90%, deterministic) |
| `Shared/LearningIntents.swift` | Feed enum and list entity/query |
| `Widget/WidgetSelectionStore.swift` | Private persisted selection plans and stale/duplicate Next guard |
| `App/StudySettingsView.swift` | Persistent text sizes, reading format, study refresh and learning preferences |
| `App/AppTypography.swift`, `App/CopyHeadwordButton.swift` | App-only font scales and copying the full Japanese headword |
| `App/KanjiLookupView.swift`, `App/KanjiWordsView.swift`, `App/NativeKanjiHeadword.swift` | Native selectable word headwords with per-glyph link/hover handling, lookup sheet, Back/Done navigation, complete related-word lists |
| `Shared/DictionaryStore.swift` | Read-only SQLite queries and `DictionaryEntry` |
| `Shared/WidgetOptions.swift` | Content, reading-style, and refresh enums; precomputed timeline offsets |
| `Shared/StudyRefreshTimer.swift` | Cancellable asynchronous study-window timer |
| `Shared/StudyStyle.swift` | Shared colors and Japanese font |
| `Widget/YarxiWidget.swift` | AppIntent configuration, resolved display options, timeline provider, views, Next intent |
| `Widget/WidgetDiagnostics.swift` | Actor that records extension lifecycle, timeline success, and failures |
| `tools/extract_database.py` | UDIF/HFS+ reader, original binary parser, gloss cleanup, kana transcription, schema/export |
| `Config/` | App and widget Info.plists, sandbox entitlements, local signing requirements |

## Data flow

```text
YARXI.dmg
  → UDIF/HFS+ extraction
  → unchanged Data/yarxice.db
  → binary parser, display cleanup, phonetic hiragana
  → Data/dictionary.sqlite + Data/manifest.json
  → separately bundled read-only copies in app and extension
  → DictionaryStore
  → study/search views or StudyProvider timelines
```

There is no network dependency. Learning uses App Group `group.local.yarxi.study` and `learning.sqlite`; dictionary bundles remain read-only and unchanged. App `@AppStorage` keys are `studyContent`, `readingStyle`, `refreshInterval`, `dailyNewLimit`, `appHeadwordScale`, `appReadingScale`, and `appTranslationScale`. Widget preferences are stored by WidgetKit per widget instance.

The main scene is `Window("Kotoba", id: "main")`, giving the app one main window. Its root view prefers existing-window delivery for `yarxi-study://entry/` links (plus `yarxi-study://lists` and `yarxi-study://setup`). The URL handler selects the entry, switches to Dictionary details, resets its timer, and calls `openWindow(id: "main")` to focus the same window. Preserve this routing when modifying navigation; a plain `WindowGroup` previously opened another window on each widget click. The native Settings scene remains available through ⌘,.

`StudyConfiguration.readingStyle` is optional to accept configurations made before the field existed. The three optional widget font-size parameters likewise resolve absent legacy values to 100%. `WidgetDisplayOptions` resolves an absent value to hiragana and freezes the display flags before handing entries to the rendering host.

`NextEntryIntent.perform()` relies on the automatic timeline reload caused by an interactive widget button. Keep `WidgetCenter.reloadTimelines` out of that action. It caused double advances in 1.1.x; 1.2.0 additionally protects persisted selections against duplicate requests. App launch/activation and the explicit **Reload widgets** control retain their own reload requests. Diagnostics log the Next invocation and the first entry ID of each generated timeline.

`RefreshInterval.timelineOffsets` returns at most 96 future offsets. Manual-only mode returns one initial entry and uses a `.never` timeline reload policy. Scheduled modes request a fresh timeline after their prepared entries. WidgetKit controls actual delivery timing.

## Extract and build

Run commands from the project root:

```sh
# Re-export when changing dictionary conversion or schema.
python3 tools/extract_database.py YARXI.dmg

# Regenerate after project settings or source-file membership changes.
xcodegen generate

# Normal local build.
bash tools/build.sh

# Build in the restricted coding environment used for the initial session.
bash tools/build.sh 'OTHER_SWIFT_FLAGS=$(inherited) -Xfrontend -disable-sandbox'
```

Output: `build/Kotoba.app`. `YARXI_BUILD_ROOT` can override the temporary derived-data location.

The build script stages a complete bundle, sets stable designated requirements only for ad hoc signatures, verifies nested signing, and replaces the generated app with rollback on a failed move. A plain Xcode build does not execute that packaging step; use an actual development signing identity when running directly from Xcode, or use the script for local deliverables.

When releasing an app change, update matching `CFBundleShortVersionString` and `CFBundleVersion` values in both `Config/App-Info.plist` and `Config/Widget-Info.plist`.

## Verify the change

```sh
# Full extraction/data checks and the production SQLite reader/timer.
bash tools/test.sh

# Learning data, FSRS, lists, sessions, quota, concurrency, rollback and migration.
bash tools/test_learning.sh

# Real provider, extension database, metadata, and packaged signature checks.
# Build first; this defaults to build/Kotoba.app.
bash tools/test_widget.sh

# Optional alternate app bundle to verify.
bash tools/test_widget.sh '/Applications/Kotoba.app'

# Native layout previews, default hiragana and selectable rōmaji.
bash tools/preview.sh
bash tools/preview_learning.sh
```

Previews are under `build/previews/`. The renderer compiles with `WIDGET_RENDERER` to exclude the extension’s `@main` entry point. Its proposed size and per-preview identity are explicit; keep those when modifying the rendering tool.

After changing widget configuration, provider logic, or packaging, also verify a desktop widget in a logged-in macOS session. Check that the current extension version actually initializes and produces new timelines. Do not equate a rendered PNG with a working WidgetKit host.

For documentation-only changes, check local links and referenced paths; recompiling the app is unnecessary.

## Update and recover a local widget

1. Build the app and run the relevant tests.
2. Quit the companion app. If using an Applications copy, replace that copy with the new build.
3. Open the intended app location. The active widget may still use an older worker even when About shows the new main-app version.
4. If it displays placeholder blocks, run this in a normal Terminal session:

   ```sh
   bash tools/restart_widget.command
   ```

5. Choose **Настройки → Настройка виджетов → Обновить виджеты** in the app.
6. Read the current status:

   ```sh
   python3 tools/widget_status.py
   ```

The restart helper prefers `/Applications/Kotoba.app` when it exists, otherwise the project’s `build/Kotoba.app`. It selects the current user’s workers by full extension executable path, prints their PIDs, sends SIGTERM, and waits up to five seconds to verify exit. It exits with an error if a worker remains. After verified exit, it force-registers only the chosen app with LaunchServices and adds its extension with PluginKit before opening the app. It does not require a global widget or LaunchServices reset.

Run it from normal Terminal if a file link opens it as source text. Look for `Old Kotoba widget worker stopped.` before reloading widgets. This procedure was verified with 1.1.2 after an older worker survived an update and caused an empty card.

## Diagnose a remaining placeholder failure

Start with `tools/widget_status.py`. Compare installed/built versions, metadata optionality, and signatures. Look for a fresh extension initialization and `timeline ready` entry.

Runtime log:

```text
~/Library/Containers/local.yarxi.study.widget/Data/Library/Caches/YarxiWidget.log
```

Cached host timelines:

```text
~/Library/Containers/local.yarxi.study.widget/Data/SystemData/com.apple.chrono/timelines/YarxiStudyWidget/
```

If the coding environment cannot read host logs or process information, have the user run the prepared read-only capture from their normal Terminal session:

```sh
bash tools/capture_widget_logs.command
```

The capture writes `build/diagnostics/widget-host-latest.log`, which can then be inspected from the workspace:

```sh
rg -n 'Bundle version did not match|bundleStubNotSupported|extension initialized|timeline ready|timeline failed' build/diagnostics/widget-host-latest.log
```

Also inspect the report’s **RUNNING APP AND WIDGET HOST PROCESSES** and **PLUGIN REGISTRATION** sections. A worker start time before the app update plus `Bundle version did not match` identifies the stale-worker case confirmed in the session memory. A provider request ending in success can still be followed by host archive rejection.

Distinguish this from a database error: the current provider logs read failures and returns an explicit failure card. Absence of the new diagnostic file can also mean the host is still running code from before diagnostics were added; it does not prove that no extension process is running.

## Learning storage and review invariants (1.2.0)

Both targets have `com.apple.security.application-groups = [group.local.yarxi.study]`.
Resolve the database using `FileManager.containerURL`, with no private fallback.
Local ad hoc builds may need user authorization on macOS; see Apple's
[sandbox guidance](https://developer.apple.com/documentation/security/accessing-files-from-the-macos-app-sandbox).
Keep bundle identities and stable designated requirements across updates.

`LearningDatabase` uses WAL, FULL synchronous writes, foreign keys, a 10-second
busy timeout, and IMMEDIATE writer transactions. Schema v2 stores saved IDs,
active/removal state, Codable FSRS Cards, lists/memberships, review history
(including before/after Card and upstream log), session/reveal state and the
latest Undo marker. Version 1 migrates by adding `fsrs_log`; migration does not
reset cards. Newer unsupported schema versions fail without overwriting data.

Inbox is the set of active saved entries without memberships. Removing an entry
marks it inactive and removes memberships; it retains progress/history. Saving
again restores it to Inbox. List deletion cascades memberships and preserves
entry progress. Ratings and Undo are transactions covering schedule, history,
new-entry consumption and session position. New-entry counts derive from
non-undone first-rating history in `[Calendar.startOfDay, next calendar day)`
using the current local time zone, across all lists including removed entries.
Use calendar addition, not 86,400 seconds, for day boundaries.

`ReviewScheduler` uses the pinned FSRS v5.0.0 basic short-term scheduler in
`Vendor/SwiftFSRS`, retaining its license in the repo and app/extension resources.
The integration disables fuzz; see `PROVENANCE.md` for the exact vendored subset
and single upstream modification. Reveal persists a timestamp so previews and
ratings use the same scheduler inputs. Ratings still consume the allowance on
the day of the rating, even if reveal happened before midnight.

Widget selection plans use `widget-selections.sqlite` in the extension's private
Application Support directory. Learning access failures must not disrupt the
Dictionary feed. Plans are keyed by resolved configuration values; identical
configurations share a stream. Each scheduled selection has a random generation
token. Next compares the displayed canonical ID and token inside a transaction,
consumes it once and promotes the next scheduled entry. Save never changes the
selection plan. The provider resolves Saved only from a successful shared-store
read; failed saves persist a private error for the setup/retry UI. Neither
interactive intent explicitly reloads timelines.

## Desktop acceptance for 1.2.0

Automated tests and previews do **not** complete the shared-container milestone. The user subsequently confirmed “Verified: app ↔ widget shared access (1.2.0)” in the actual app; closed-app Save/card preservation, one-step Next and one Inbox entry on reopening were also user-confirmed. The user also confirmed both feeds/all widget sizes, repeated links, review controls/Undo and session recovery. The checklist below remains useful for future updates; live macOS permission denial/recovery has not been exercised.
On 2026-11-04, opening the then-named companion app failed in the coding environment with
LaunchServices `-10827 kLSNoExecutableErr`, although the packaged executable exists
and signature verification succeeds. The restart helper's `ps` call was denied
with `Operation not permitted`. Neither proves an entitlement defect or a new
stale-worker incident. Direct runtime-log evidence is still old; distinguish this from the user-confirmed bidirectional access status.

1. Quit the old main app, then run `bash tools/restart_widget.command` in normal
   Terminal. Confirm the helper verifies that the old worker exited.
2. Open the built 1.2.0 app. In Settings → Widget setup, Check / Retry access,
   allow the macOS prompt if shown, then Reload widgets and check again. The
   success message requires a fresh app-launch nonce acknowledged by the widget
   and matching app/extension versions, rather than a stale flag.
3. **User-confirmed for widget Save/card preservation, Next and Inbox uniqueness.** Save in the app and confirm the widget reads Saved. Quit the app; Save a
   different widget card. It must remain visible and show a checkmark. Reopen
   My Lists → Inbox and confirm that exact entry exists once.
4. **User-confirmed for both feeds/all sizes and repeated entry links.** Check one-step Next and repeated Save in all three sizes, with Dictionary and
   Learning list feeds, including a one-entry list. Delete/empty a selected list
   and check its explicit message and list-selection link.
5. **Not manually exercised; automated storage failure/retry paths pass.** Exercise the actual local permission denial/recovery flow. Dictionary browsing
   must work; denied saves must not show a new Saved checkmark. Retry must save
   exactly once after authorization.
6. **User-confirmed review controls, Undo, resume and single-window links; list persistence is covered by automated tests.** Review with Space, 1–3 and Undo, quit/relaunch mid-review, organize lists, and
   confirm repeated widget entry links reuse the unique main window.
7. Capture `python3 tools/widget_status.py` and the existing diagnostic helper.
   Record confirmed runtime outcomes in SESSION_MEMORY; keep pending items clear.

## App text selection and font sizes (1.2.1)

Entry/Explore and review headwords use native SwiftUI text selection, with an
explicit **Copy word / kanji** button writing the exact headword to the clipboard.
Reading text is selectable too. Dictionary and saved-list rows expose the same
copy command through their context menus, retaining normal row navigation.

Settings → Text size exposes three independent 50–200% scales in 5% increments,
a live Japanese/reading/Russian preview, and Reset text sizes. `AppTypography`
reads app-local `@AppStorage` values and scales the existing contextual font
sizes for details, Explore, review and result/list rows. Large card headwords
wrap in their scroll view rather than shrinking back to fit a single line.
Widget fonts and widget preferences are independent.

`tools/preview_learning.sh` now also renders all-three-sizes-at-200%, a reading-only
increase, and a long-word review at 200%. Its volatile UserDefaults domain avoids
changing real app preferences. These are layout checks; interactive desktop
selection/copy and slider changes have not been user-confirmed for 1.2.1.

## Widget font sizes (1.2.2)

Edit Kotoba has defaulted `headwordSize`, `readingSize`, `translationSize`
parameters, using the extension-only `WidgetFontSize` (50/75/100/125/150%). They resolve to three
numeric scales in `WidgetDisplayOptions` before rendering. Defaults and nil
legacy values both resolve to 1.0; app font preferences remain independent.

Font options are deliberately excluded from `WidgetSelectionStore.key` so a
size edit keeps current and future cards, including after an update from older
configurations. The key format and selection database schema are unchanged.

Small cards reserve their header/footer before budgeting the headword, reading
and translation. They reduce translation lines when space is tight. Medium
cards use a bounded body between a fixed header/footer; this prevents dense
readings and enlarged kanji from overlapping controls. Long headwords still
shrink to available space. Enlarged words in large widgets can use two lines;
large kanji details use fewer lines at enlarged translation sizes.

Provider checks add 30 font variants across both feeds to the existing 84 cases.
They verify independent resolution, nil defaults, and unchanged selection tokens
and IDs after size edits. Packaged metadata checks all three new fields remain
optional. Widget previews cover minimum/maximum and individual category changes
in every size and both reading styles, plus dense medium readings/translations.
Previews and builds alone do not verify the live desktop configuration editor.

### Widget font configuration investigation (1.2.4)

The 1.2.3 desktop log received 100% while the editor showed 150%; this was not
reproduced by programmatic setters/previews, even in optimized builds. In 1.2.4,
font enums are registered only by the extension (new WidgetFontSize type identity,
same saved case IDs), and WidgetKit callbacks freeze plain display options before
calling async helpers. The helper accepts that snapshot rather than reading the
wrappers again. This change targets the observed decode/handoff mismatch; a
confirmed framework-level root cause has not been established.

For runtime checks, compare `intent received ...` and `configuration version=...`
events after committing the widget editor with **Done**. Reading percent150 must
produce a reading scale of 1.5, and the large widget's serif reading uses 18 pt
instead of its 12 pt default. Keep 1.2.3's diagnostic-only status distinct from
any later desktop-confirmed repair. Do not infer the exact executing binary only
from a dynamically read Info.plist version; new code-path log markers are useful.

### Defaulted required font fields (1.2.5)

1.2.4 did not fix the reported failure: the direct callback still logged 100%.
1.2.5 makes font fields required with explicit percent100 defaults and lists all
configuration fields in parameterSummary. The original optional reading-style
migration remains unchanged. Font metadata tests now require the defaults rather
than optionality. Runtime `schema=required-fonts-v1` and parameter valueState
markers identify new code and explicit/default values. This candidate subsequently
failed the live check; see the confirmed 1.2.6 numeric-field repair below.

### Primitive numeric font controls (1.2.6)

The normal-Terminal host capture revealed App Intents enum metadata failures
following linkd's `Unable to get teamId` errors for the local ad hoc binary.
1.2.6 uses primitive Int `headwordPercent`/`readingPercent`/`translationPercent`
parameters, each defaulting to 100 with range 50–150. parameterSummary shows these
fields; obsolete size enums remain hidden compatibility inputs and do not affect
rendering. Existing app preferences, selection keys, dictionary and learning
schemas remain unchanged. Re-enter font percentages in the new numeric fields.
Tests require primitive metadata rather than enumeration references, verify
integer defaults, clamp invalid input, and ensure obsolete enums cannot override
new numeric selections. Runtime schema marker is `primitive-percent-v1`.
Desktop verification completed on 2026-11-11: the user confirmed all three fields
work from 50–150%. Fresh runtime logs independently show numeric values reaching
the provider with correct scales (including 0.5/1.5/1.5), and the displayed entry
remains unchanged through those edits. See SESSION_MEMORY for exact evidence.
Enum-based content/timer/reading options were not redesigned in this font-only
update.


## Kotoba naming (1.2.7)

The user chose Kotoba for the app and widgets. Visible titles, card headers,
gallery/configuration names, setup/error text, bundle names, helper output and
the build product now use Kotoba (`build/Kotoba.app`). Versions are aligned at
1.2.7 (13). Regenerate the project after changing PRODUCT_NAME.

Bundle IDs, designated requirements, App Group, URL scheme, extension module,
widget kind and selection keys retain their existing identities. This preserves
app preferences, learning data and existing widget configurations. The supplied
image, extracted source database, attribution and historical diagnostic evidence
retain their source names. Do not globally replace these compatibility values.
The restart/status helpers now prefer `/Applications/Kotoba.app`, then the build.

Keep the previous bundle path available until the old main app and widget worker
have exited and the renamed bundle is running. A worker can still request bundled
resources after packaging. Archiving the old bundle prematurely caused a confirmed
dictionary-open failure during the rename; see SESSION_MEMORY. The helper now also
refreshes the chosen app/extension registration, but this new registration step
still requires verification in a normal logged-in Terminal session.


### Renamed bundle still launches from the old path

If a normal-Terminal capture shows launchd reporting `Attempt to re-bootstrap
service from different path, will use existing` in chronod's PID domain, the
extension launch job is retaining the old bundle path. A freshly spawned worker
can then trap in ExtensionFoundation with `Invalid bundle record for current
process`. This was captured for the Kotoba rename even though PluginKit selected
the new 1.2.7 bundle. Killing only the extension and re-registering is insufficient.

For this diagnosed condition, close the gallery and run in normal Terminal:

```sh
bash tools/restart_widget.command --refresh-host
```

This additionally restarts the current user's chronod and NotificationCenter
processes, briefly reloading all desktop widgets/gallery, without deleting caches,
configuration or learning data. The helper verifies old PID exit and records
`build/diagnostics/widget-restart-latest.log`. Reopen the gallery and check fresh
runtime initialization/timelines and the visible card. The host-refresh procedure was desktop-confirmed on 2026-11-11: runtime logs
show new 1.2.7 initialization and successful timelines from Kotoba, and the user
provided rendered cards in all gallery sizes. The remaining gallery-name cache
issue is separate; see below.


### Cards renamed, gallery title still old (1.2.8)

A previous worker can populate descriptor metadata using the new bundle version
but its own old displayName. During the rename, chronod cached YARXI Study under
build-13 metadata even though build 13's code names the widget Kotoba. The host
restart repaired card rendering but retained this persistent descriptor.

Build 1.2.8 (14) advances both version keys to request normal metadata refresh,
keeping widget identities/configurations intact. This title repair was verified
on 2026-11-11: the cached name/version became Kotoba/build 14 and the user
confirmed the visible gallery title. `python3 tools/widget_status.py` prints the extension's cached
gallery name and metadata version using read-only SQLite access. Do not directly
modify Apple's descriptor database or delete global widget state for this case.


## Kanji links in word cards (1.2.9)

`InteractiveHeadword` keeps the headword as one native selectable SwiftUI Text.
It resolves the word's unique characters against real eligible kanji records,
then adds internal AttributedString links only to those characters. Character
order/wrapping, full-word selection and Copy are retained. Links are handled by a
local OpenURLAction and open an in-app sheet, without routing external entry URLs
or creating app windows. Kana and repetition marks without cards stay plain text.

Dictionary details/Explore and Today review use this component. A lookup sheet
shows kanji meanings/readings and all eligible words whose actual spelling
contains that character, using a bound SQLite `instr(text, ?)` query and lazy
rows with no 100/500-result cap. Related words open within the same sheet; its
Back stack supports further kanji exploration. Done dismisses it without changing
source selection, search filters, reveal state or review position. Explore's timer
is cancelled while its sheet is open and starts a fresh interval after dismissal.
Learning Save/Organize reuse existing transaction-backed controls.

The dictionary/schema/export, shared learning store, widget configuration/kind,
App Group, stable bundle identities and unique main Window are unchanged.
App/widget versions are aligned at 1.2.9 (15), with the new App source included
through `xcodegen generate`. Dictionary smoke checks cover kanji resolution,
kana/repetition exclusion, literal containment, canonical IDs and all 901 words
for 一. Native app previews include the lookup, linked compound, related-word
card, and 200% text sizes. The user confirmed the logged-in app sequence 合図 → click 図 → its kanji card
and 84 words → Done returns to 合図 on 2026-11-11. Drag-copy, nested Back and
review-return have not separately been user-confirmed; screenshots/provider tests
do not establish those interactions.


## Interaction feedback (1.2.10)

CopyHeadwordButton checks the pasteboard write result before showing Copied with
a checkmark (or Copy failed). A view-scoped, cancellable task resets the label
after two seconds; repeating the action restarts that duration, and a different
headword clears the old confirmation. The original label reserves width so other
controls stay in place. Copy hover/press background and a small press scale provide
immediate feedback; animations respect Reduce Motion. Context-menu copy still uses
the same control/action.

InteractiveHeadword underlines its dictionary-backed kanji when the linked
headword is hovered, shows explanatory help and a hand pointer over that headword,
and briefly highlights the chosen kanji on activation. Navigation runs immediately,
without an artificial delay. Native selectable Text and exact full-word copying
remain intact. LinkPointer uses native macOS 15+ pointerStyle(.link); macOS 14
uses a balanced cursor push/pop, including disable and view disappearance.

Both bundles are 1.2.10 (16). Release/signature checks and native layout previews
pass. A private-pasteboard probe from the coding environment returned false even
for a direct NSPasteboard write; it does not verify successful clipboard access in
the signed app. Test copy confirmation/reset, actual pasted text and hover/cursor
behavior in the logged-in app. No global clipboard was overwritten by that probe.


## Russian UI (1.3.0)

The app/widget UI now uses Russian literals and Russian AppIntent display labels.
Bundle development language/localizations and shared ru.lproj resources declare
Russian; app/settings/widget roots use ru_RU, with explicit number/percent formats
where formatting precedes Text. Main Page raw values remain internal English IDs
and expose separate Russian titles. Review grade values, intervals and scheduling
math are unchanged; only titles and duration units were translated. Inbox remains
id `inbox` with display name Входящие. User list names stay untouched.

Do not translate stored enum cases, parameter names, selection keys, IDs or SQL.
Keep runtime diagnostic markers in English so existing investigations/tests remain
searchable. User-facing setup is documented in the Russian README. Packaged tests
assert Russian bundle resources and extracted configuration titles as well as the
existing compatibility metadata and signing invariants.

Kanji hover uses StudyStyle.linkHover (blue-gray), with no underlining. A single
selectable attributed Text still preserves Japanese wrapping and full-word copy.
The user screenshot verified 1.2.10's hover, but 1.3.0's new color/Russian UI and
native menu labels still need a desktop check. Automated tests/previews are not
that acceptance signal.


## Per-character hover (1.3.1)

The user confirmed 1.3.0 changed all kanji in a word together, and clarified the
hover must affect only the character under the pointer. InteractiveHeadword now
hosts NativeKanjiHeadword (NSTextView/TextKit) rather than SwiftUI Text with a
word-level onHover. The same native glyph layout supplies drawing, selection,
wrapping and pointer hit testing. Check both glyph bounds and the composed
character range: NSLayoutManager otherwise returns a nearest glyph over whitespace,
and adjacent identical links can coalesce into one attributed run.

Only the hovered grapheme receives a temporary blue-gray foreground attribute.
The pointer is a hand only over real dictionary-backed kanji; kana and blank space
are text-selection regions. Tooltips name that particular kanji in Russian. Mouse
exit/navigation clears the temporary style. Native link delegation preserves the
existing in-sheet navigation and short activation flash, without opening URLs
externally. One NSTextView preserves full-word drag selection and Copy. Explicit
font sizes use AppTypography's same resolved scale at all three call sites.

Fitting height is measured with an independent TextKit layout at the proposed
width; the live NSTextView container can otherwise lag a resize by one pass.
`bash tools/test_headword.sh` checks actual glyph geometry, one-character hover,
kana/blank-space exclusion, wrapping at 200%, adjacent identical kanji, selection
preservation and click routing. Native app previews and an isolated rendered
食べ物 hover show 食 blue-gray, べ black, 物 red. The user subsequently confirmed the logged-in per-character hover/click sequence
on 2026-11-11. Review keyboard and drag-copy have not separately been confirmed.
Both bundles are 1.3.1 (18); Russian
UI, dictionary, learning state and widget identities/configuration stay intact.


## Related words in the normal kanji card (1.3.2)

The main dictionary/Explore card now includes KanjiWordsView beneath a kanji's
meaning/detail in its existing scroll area, when the translation is revealed.
It resolves all eligible words using DictionaryStore.words(containingKanji:),
independent of the main search content filter. Per-entry view identity resets
loading/results when the current kanji changes. Loading/error/empty states are
explicit. KanjiWordsContent is shared with the existing lookup sheet, preserving
identical rows, counts, reading styles, copy menus and unlimited related words.

Rows open a word in the existing lookup sheet with a generic root entry; Done
returns to the original kanji without changing main selection, filters or its
scroll position. Per-kanji links can continue exploring inside that sheet. The
main lookupEntry state still pauses Explore while any lookup sheet is open.
Widget URL delivery and the single main Window remain unchanged. Review front
behavior, dictionary/export, learning data and widget configuration are unchanged.

Both versions are 1.3.2 (19). Regenerated project includes App/KanjiWordsView.swift.
Build/signature checks, dictionary/data/timer/drill-down checks and native learning
previews pass (new kanji-words-inline plus existing lookup). The user confirmed dictionary entry 図 → scroll to its 84 words → open a word
on 2026-11-11. Done/scroll-position restoration was not separately checked in
that final acceptance sequence.
