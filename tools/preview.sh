#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
task_temp="$(mktemp -d "${TMPDIR:-/tmp}/yarxi-previews.XXXXXX")"
trap 'rm -rf "$task_temp"' EXIT
xcrun swiftc -module-cache-path "$task_temp/modules" -D WIDGET_RENDERER \
    Shared/*.swift Vendor/SwiftFSRS/*/*.swift Widget/*.swift tools/RenderWidgets.swift \
    -o "$task_temp/render-widgets"
"$task_temp/render-widgets" Data/dictionary.sqlite build/previews
