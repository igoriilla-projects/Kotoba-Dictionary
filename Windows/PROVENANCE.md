# Windows dependencies and scheduling

The Windows application is an independent C#/WPF implementation of Kotoba 1.2.8.
The macOS sources and supplied dictionary inputs are unchanged.

Windows 1.4.0 also ports macOS 1.2.9–1.3.2's kanji drill-down, complete related-word
lists, per-character native hit/hover handling, copy feedback and Russian UI.
`UiText` localizes display strings only: durable enum values, IDs, SQLite schema,
user list names, widget configuration keys and the single-card policy stay intact.

`Kotoba.Core/ReviewScheduler.cs` is a mechanical adaptation of the MIT-licensed
Swift FSRS v5.0.0 subset already in `Vendor/SwiftFSRS`, release commit `129e4dc`.
It preserves all 19 weights, 90% retention, eight-decimal intermediate rounding,
short-term scheduling, no-fuzz interval rounding, and the original review log
semantics (which log the pre-transition current card). Hard is supported by the
scheduler for parity but, as on macOS, only Again/Good/Easy are exposed in the UI.
The original MIT license is distributed in `Licenses/SwiftFSRS-LICENSE.txt`.
Tests include numeric new-card/learning cases and transition invariants; a live
Swift-vs-C# differential run has not been performed on this Windows machine.

`Kotoba.Core/Sqlite.cs` is a small direct wrapper around Windows' `winsqlite3.dll`.
It is not Microsoft's NuGet provider. NuGet was blocked in the implementation
environment, so the wrapper avoids any package restore or native DLL download.
It uses UTF-8, bound parameters, native result checks and deterministic disposal.

The learning database uses the same schema-v2 tables and canonical IDs. Windows
JSON timestamps use Unix seconds; this is not an interchange implementation of
Swift Codable's date representation. Import/export and cross-platform database
interchange are not supported in this release.

`Kotoba.Windows/Assets/NotoSerifCJKjp-Regular.otf` comes from
https://github.com/notofonts/noto-cjk/tree/main/Serif/OTF/Japanese, downloaded
2026-11-11. SHA-256:
`d9854c7a8ef170b5a7932558856fd64eb8de0b007cd823fed6f9f514ad2803d3`.
It is embedded unmodified; its SIL OFL is in `Assets/OFL.txt` and ships in the app.
Segoe UI, Georgia and Consolas are referenced as Windows system fonts, not copied.

`tools/prepare_windows.py` pins and verifies the official .NET 10.0.15 runtime
archives (Microsoft release-metadata SHA-512) and Inno Setup 7.1.1 installer
(SHA-256). Inno is prepared in portable mode inside `build/windows-tools`.
The distributed app includes an app-local `.runtime` selected by .NET 10's
AppHostDotNetSearch/AppHostRelativeDotNet settings. It requires no global .NET
installation. Official runtime license and notices accompany that directory.
The setup is an unsigned local build; no signing certificate was supplied.
