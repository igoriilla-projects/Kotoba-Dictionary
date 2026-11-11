#!/usr/bin/env python3
"""Read Kotoba's own widget diagnostics without requiring unified-log access."""
from datetime import datetime
import json
from pathlib import Path
import plistlib
import sqlite3
import subprocess

project = Path(__file__).resolve().parents[1]
for app in [Path('/Applications/Kotoba.app'), project / 'build/Kotoba.app']:
    print(f'\nApp: {app}')
    if not app.exists():
        print('Not present')
        continue
    for bundle in [app, app / 'Contents/PlugIns/YarxiStudyWidget.appex']:
        info = plistlib.loads((bundle / 'Contents/Info.plist').read_bytes())
        print(f"  {info['CFBundleIdentifier']} v{info['CFBundleShortVersionString']} ({info['CFBundleVersion']})")
        signature = subprocess.run(['codesign', '--display', '--requirements', '-', str(bundle)],
                                   capture_output=True, text=True)
        for line in (signature.stdout + signature.stderr).splitlines():
            if 'designated =>' in line:
                print('  ' + line)
        metadata = bundle / 'Contents/Resources/Metadata.appintents/extract.actionsdata'
        if bundle.suffix == '.appex' and metadata.exists():
            parameters = json.loads(metadata.read_bytes())['actions']['StudyConfiguration']['parameters']
            reading = next(p for p in parameters if p['name'] == 'readingStyle')
            print(f"  Reading format accepts absent values: {reading['isOptional']}")

container = Path.home() / 'Library/Containers/local.yarxi.study.widget/Data'
print('\nWidget runtime log:')
logs = list((container / 'Library/Caches').rglob('YarxiWidget.log'))
if not logs:
    print('No runtime log yet. Open the updated app and add or reload a widget.')
for path in logs:
    print(path)
    print('\n'.join(path.read_text().splitlines()[-30:]))

print('\nCached timeline modification times:')
timelines = container / 'SystemData/com.apple.chrono/timelines/YarxiStudyWidget'
for path in sorted(timelines.glob('*.chrono-timeline'), key=lambda p: p.stat().st_mtime, reverse=True)[:8]:
    print(datetime.fromtimestamp(path.stat().st_mtime).isoformat(timespec='seconds'), path.name)

# This is host-owned metadata, separate from rendered cards and app preferences.
# Read only our extension's rows; never edit the system's cache to repair labels.
print('\nWidget gallery descriptor cache:')
descriptor_cache = Path.home() / 'Library/Group Containers/group.com.apple.chronod/chronod/chrono.sql'
try:
    connection = sqlite3.connect(descriptor_cache.as_uri() + '?mode=ro', uri=True)
    try:
        rows = connection.execute('''
            SELECT m.version, d.data FROM ExtensionMetadata m
            JOIN Descriptors d USING(bundleIdentifier)
            WHERE m.bundleIdentifier = ? OR m.bundleIdentifier LIKE ?
        ''', ('local.yarxi.study.widget', '%::local.yarxi.study.widget')).fetchall()
    finally:
        connection.close()
    if not rows:
        print('No cached gallery descriptor for this extension.')
    for version, blob in rows:
        archive = plistlib.loads(blob)
        objects = archive.get('$objects', [])
        for item in objects:
            if not isinstance(item, dict):
                continue
            title = item.get('displayName')
            if isinstance(title, plistlib.UID):
                title = objects[title.data]
            if isinstance(title, str):
                print(f'  Cached name: {title}; metadata version: {version}')
except (OSError, sqlite3.Error, ValueError, TypeError, KeyError, IndexError) as error:
    print(f'Gallery cache could not be inspected: {error}')
