#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
task_temp="$(mktemp -d "${TMPDIR:-/tmp}/kotoba-headword-tests.XXXXXX")"
trap 'rm -rf "$task_temp"' EXIT
xcrun swiftc -swift-version 5 -Xfrontend -disable-sandbox -module-cache-path "$task_temp/modules" \
    Shared/DictionaryStore.swift Shared/WidgetOptions.swift Shared/StudyStyle.swift \
    App/NativeKanjiHeadword.swift tests/KanjiHeadwordSmoke.swift -o "$task_temp/headword-smoke"
"$task_temp/headword-smoke" Data/dictionary.sqlite
