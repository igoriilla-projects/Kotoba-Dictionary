# Project instructions

Read [docs/SESSION_MEMORY.md](docs/SESSION_MEMORY.md) when starting a session in this project. It records the last verified state, the user’s choices, and the confirmed widget failure and recovery. Use [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for architecture, commands, and validation; [README.md](README.md) covers user setup.

## Working conventions

- Preserve the supplied `YARXI.dmg` and the unchanged extracted `Data/yarxice.db`. Reproduce SQLite changes through `tools/extract_database.py`.
- Source images, binary/SQLite dictionaries, learning databases, builds and runtime logs are local-only. Do not force-add ignored data to Git. See `Data/README.md` and `THIRD_PARTY_NOTICES.md`.
- Windows lives in `Windows/`; use `tools/build_windows.ps1` and [docs/WINDOWS.md](docs/WINDOWS.md). Keep its assembly, manifest, installer and About versions aligned independently of macOS. Windows-only source additions do not require XcodeGen.
- Windows has one desktop card: size selection updates it. Preserve its current entry/token and display preferences; keep app and card preferences independent. Learning uses `%LOCALAPPDATA%\Kotoba`, never the install directory or a fallback location after failure.
- Treat `project.yml` as the project configuration source. Run `xcodegen generate` after changing targets, resources, or the set of Swift source files.
- Build local deliverables with `tools/build.sh`; it stages the app, applies stable requirements to ad hoc builds, verifies signatures, and replaces the output bundle. Keep app and widget versions aligned in both `Config/*-Info.plist` files.
- Keep app and individual widget display preferences independent unless the user explicitly requests synchronization. Dictionary bundles remain read-only; learning data uses the shared App Group `group.local.yarxi.study`. Never fall back to a private learning database after an access failure.
- Keep the main app scene a single-instance `Window` with ID `main`. Widget entry links should update and focus that window, rather than create a `WindowGroup` instance per click.
- Preserve optional `StudyConfiguration.readingStyle` and its hiragana fallback for older widget configurations. Pass resolved `WidgetDisplayOptions` to rendering views.
- `NextEntryIntent` relies on WidgetKit's automatic reload after the button action. An additional explicit reload causes the card to advance twice; keep reload calls out of this action.
- Save must commit to the learning database before reporting success and must preserve the widget selection. Next must compare the displayed ID and selection token atomically. Keep selection plans in private extension storage.
- Review progress belongs to the canonical entry ID; preserve it across list changes and entry removal/restoration. Keep ratings, history, session position and Undo atomic. Count new entries on the first rating using local calendar-day bounds across every list.
- A successful build, provider test, or PNG preview does not verify the desktop widget host or actual App Group authorization. The user confirmed 1.2.0 bidirectional access and core desktop learning/widget workflows; keep user confirmations distinct from direct runtime logs. Live macOS permission denial/recovery remains untested. For placeholder regressions, inspect runtime logs and worker start time before changing dictionary or layout code.
- After a local update, a stale `YarxiStudyWidget` worker can cause `Bundle version did not match`. The confirmed recovery is `tools/restart_widget.command`, then **Settings → Widget setup → Reload widgets**. See the session memory before investigating this symptom again.
- Update session memory when behavior, build steps, or a verified diagnosis changes. Keep confirmed causes distinct from hypotheses.

<!-- CODEGRAPH_START -->
## CodeGraph

In repositories indexed by CodeGraph (a `.codegraph/` directory exists at the repo root), reach for it BEFORE grep/find or reading files when you need to understand or locate code:

- **MCP tool** (when available): `codegraph_explore` answers most code questions in one call — the relevant symbols' verbatim source plus the call paths between them, including dynamic-dispatch hops grep can't follow. Name a file or symbol in the query to read its current line-numbered source. If it's listed but deferred, load it by name via tool search.
- **Shell** (always works): `codegraph explore "<symbol names or question>"` prints the same output.

If there is no `.codegraph/` directory, skip CodeGraph entirely — indexing is the user's decision.
<!-- CODEGRAPH_END -->
