#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
task_app="${1:-build/Kotoba.app}"
task_widget="$task_app/Contents/PlugIns/YarxiStudyWidget.appex"
task_temp="$(mktemp -d "${TMPDIR:-/tmp}/yarxi-widget-tests.XXXXXX")"
trap 'rm -rf "$task_temp"' EXIT
xcrun swiftc -module-cache-path "$task_temp/modules" -D WIDGET_RENDERER \
    Shared/*.swift Vendor/SwiftFSRS/*/*.swift Widget/*.swift tests/WidgetSmoke.swift -o "$task_temp/widget-smoke"
"$task_temp/widget-smoke" "$task_widget/Contents/Resources/dictionary.sqlite"
codesign --verify --deep --strict "$task_app"
python3 - "$task_app" "$task_widget" <<'PY'
import json
import plistlib
from pathlib import Path
import subprocess
import sys

for argument in sys.argv[1:]:
    path = Path(argument)
    info = plistlib.loads((path / 'Contents/Info.plist').read_bytes())
    identity = info['CFBundleIdentifier']
    assert info['CFBundleDevelopmentRegion'] == 'ru'
    assert info['CFBundleLocalizations'] == ['ru']
    assert (path / 'Contents/Resources/ru.lproj/InfoPlist.strings').exists()
    entitlements = subprocess.run(['codesign', '--display', '--entitlements', '-', '--xml', str(path)],
                                  capture_output=True, check=True)
    signed = plistlib.loads(entitlements.stdout)
    assert signed.get('com.apple.security.app-sandbox') is True
    assert signed.get('com.apple.security.application-groups') == ['group.local.yarxi.study']
    assert (path / 'Contents/Resources/LICENSE').exists(), 'FSRS license must ship in both bundles'
    result = subprocess.run(['codesign', '--display', '--requirements', '-', str(path)],
                            capture_output=True, text=True, check=True)
    requirement = result.stdout + result.stderr
    if 'designated => identifier' not in requirement or 'cdhash' in requirement:
        raise AssertionError(f'Local build needs a stable designated requirement: {identity}')
    subprocess.run(['codesign', '--verify', '-R=identifier "' + identity + '"', str(path)], check=True)
metadata = json.loads((Path(sys.argv[2]) / 'Contents/Resources/Metadata.appintents/extract.actionsdata').read_bytes())
app_metadata = json.loads((Path(sys.argv[1]) / 'Contents/Resources/Metadata.appintents/extract.actionsdata').read_bytes())
assert not any(e['identifier'] == 'WidgetFontSize' for e in app_metadata['enums']), 'Widget font type must be registered only by the extension'
font_enum = next(e for e in metadata['enums'] if e['identifier'] == 'WidgetFontSize')
assert font_enum['fullyQualifiedTypeName'] == 'YarxiStudyWidget.WidgetFontSize'
parameters = metadata['actions']['StudyConfiguration']['parameters']
assert metadata['actions']['StudyConfiguration']['title']['key'] == 'Настройки обучения'
assert next(p for p in parameters if p['name'] == 'readingPercent')['title']['key'] == 'Размер чтения (%)'
for name in ['readingStyle', 'feed', 'learningList']:
    parameter = next(p for p in parameters if p['name'] == name)
    assert parameter['isOptional'], f'{name} must tolerate absent legacy values'
for name in ['headwordPercent', 'readingPercent', 'translationPercent']:
    parameter = next(p for p in parameters if p['name'] == name)
    assert not parameter['isOptional'], f'{name} must have a defaulted integer value'
    assert 'primitive' in parameter['valueType'], 'Visible font controls must not require enum metadata'
    specifics = parameter['typeSpecificMetadata']
    default = specifics[specifics.index('LNValueTypeSpecificMetadataKeyDefaultValue') + 1]
    assert default == {'int': {'wrapper': 100}}, f'{name} must supply an integer 100% default'
assert 'SaveEntryIntent' in metadata['actions']
assert {'planKey', 'displayedID', 'token'} <= {p['name'] for p in metadata['actions']['NextEntryIntent']['parameters']}
app_info = plistlib.loads((Path(sys.argv[1]) / 'Contents/Info.plist').read_bytes())
widget_info = plistlib.loads((Path(sys.argv[2]) / 'Contents/Info.plist').read_bytes())
for key in ['CFBundleShortVersionString', 'CFBundleVersion']:
    assert app_info[key] == widget_info[key], f'Mismatched {key}'
print('PASS: built extension metadata, aligned versions, App Group entitlements, FSRS licenses, stable signing requirements, and nested signatures')
PY
