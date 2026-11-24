---
name: kotoba-windows-build
description: Build or validate the Kotoba Windows x64 installer, including packaging a newer local YARXI SQLite dictionary and checking that saved cards survive the update.
---

# Build Kotoba for Windows

Run commands from the repository root. Use the [Windows guide](../../../docs/WINDOWS.md) for prerequisites and validation details. See [project memory](../../../docs/SESSION_MEMORY.md) for the last verified release.

1. Check for `Data/dictionary.sqlite`. If it is missing and the local `YARXI.dmg` is available, run `python tools/extract_database.py YARXI.dmg` to prepare the baseline export.
2. For a fresh YARXI database, place the supplied `yarxi.db` in `Data/`. `tools/build_windows.ps1` regenerates `Data/dictionary.windows.sqlite` and packages it as `Data/dictionary.sqlite` inside the app. Without `Data/yarxi.db`, the build packages the baseline directly.
3. Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/build_windows.ps1`. The script runs the core contracts on the selected export and produces `build/windows/Kotoba-Setup-x64.exe` and `build/windows/app/Kotoba.exe`.
4. For a dictionary update, verify that the source hash in the packaged SQLite `metadata` table matches the supplied file and that the packaged database matches the selected export. Run `tools/test_windows_installer.ps1` for the installed-app WPF smoke and learning/widget preservation checks. `tools/test_windows.ps1` exercises the baseline export.

Keep modern SQLite conversions in `tools/extract_database.py`. `Tango.Nomer` differs between the legacy and modern sources. Keep existing card IDs through unambiguous matches and retain legacy cards when a match is uncertain. This keeps saved entries, review progress and the desktop card tied to the same entries.

For a version change, align the Windows project, application manifest, installer and About screen. Keep the original dictionary inputs, generated exports, installer, test output and user data local.
