#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
task_temp="$(mktemp -d "${TMPDIR:-/tmp}/yarxi-learning-tests.XXXXXX")"
trap 'rm -rf "$task_temp"' EXIT
xcrun swiftc -module-cache-path "$task_temp/modules" \
    Shared/*.swift Vendor/SwiftFSRS/*/*.swift tests/LearningSmoke.swift \
    -o "$task_temp/learning-smoke"
"$task_temp/learning-smoke" "$PWD/Data/dictionary.sqlite"
