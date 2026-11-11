#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ ! -f Data/dictionary.sqlite ]]; then
    if [[ ! -f YARXI.dmg ]]; then
        printf 'Нет локального словаря. Поместите свой YARXI.dmg в корень проекта и выполните python3 tools/extract_database.py YARXI.dmg. См. Data/README.md.\n' >&2
        exit 1
    fi
    python3 tools/extract_database.py
fi
if [[ ! -f YarxiStudy.xcodeproj/project.pbxproj ]]; then
    xcodegen generate
fi
task_build_root="${YARXI_BUILD_ROOT:-${TMPDIR:-/tmp}/yarxi-study-build}"
xcodebuild -project YarxiStudy.xcodeproj -scheme YarxiStudy \
    -configuration Release -destination 'platform=macOS' \
    -derivedDataPath "$task_build_root" \
    CLANG_MODULE_CACHE_PATH="$task_build_root/ClangCache" \
    SWIFT_MODULE_CACHE_PATH="$task_build_root/SwiftCache" \
    "$@" build
mkdir -p build
task_package_root="$(mktemp -d "$PWD/build/.yarxi-package.XXXXXX")"
trap 'rm -rf "$task_package_root"' EXIT
task_staged_app="$task_package_root/Kotoba.app"
ditto "$task_build_root/Build/Products/Release/Kotoba.app" "$task_staged_app"

# The default ad hoc DR is tied to each binary's cdhash. Give local-only builds
# stable bundle requirements so WidgetKit can recognize them after a rebuild.
# Preserve Xcode's signing when a real development/distribution identity is used.
task_signature="$(codesign --display --verbose=2 "$task_staged_app" 2>&1)"
if [[ "$task_signature" == *"Signature=adhoc"* ]]; then
    codesign --force --sign - --timestamp=none --generate-entitlement-der \
        --entitlements Config/Widget.entitlements --requirements Config/Widget.requirements \
        "$task_staged_app/Contents/PlugIns/YarxiStudyWidget.appex"
    codesign --force --sign - --timestamp=none --generate-entitlement-der \
        --entitlements Config/App.entitlements --requirements Config/App.requirements \
        "$task_staged_app"
fi
codesign --verify --deep --strict "$task_staged_app"
if [[ -e "build/Kotoba.app" ]]; then
    mv "build/Kotoba.app" "$task_package_root/previous.app"
fi
if ! mv "$task_staged_app" "build/Kotoba.app"; then
    if [[ -d "$task_package_root/previous.app" ]]; then
        mv "$task_package_root/previous.app" "build/Kotoba.app"
    fi
    exit 1
fi
printf '\nBuilt: %s/build/Kotoba.app\n' "$PWD"
