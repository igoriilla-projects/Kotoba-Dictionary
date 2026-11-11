#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
task_temp="$(mktemp -d "${TMPDIR:-/tmp}/yarxi-learning-previews.XXXXXX")"
trap 'rm -rf "$task_temp"' EXIT
xcrun swiftc -swift-version 5 -Xfrontend -disable-sandbox -module-cache-path "$task_temp/modules" -D APP_RENDERER \
    Shared/*.swift Vendor/SwiftFSRS/*/*.swift App/*.swift tools/RenderLearning.swift \
    -o "$task_temp/render-learning"
"$task_temp/render-learning" Data/dictionary.sqlite build/previews/learning
