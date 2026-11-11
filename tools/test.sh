#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
python3 -m unittest discover -s tests -v
task_temp="$(mktemp -d "${TMPDIR:-/tmp}/yarxi-tests.XXXXXX")"
trap 'rm -rf "$task_temp"' EXIT
xcrun swiftc -module-cache-path "$task_temp/modules" \
    Shared/DictionaryStore.swift Shared/WidgetOptions.swift Shared/StudyRefreshTimer.swift tests/DictionarySmoke.swift \
    -o "$task_temp/dictionary-smoke"
"$task_temp/dictionary-smoke" Data/dictionary.sqlite
