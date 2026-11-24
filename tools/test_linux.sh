#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

project=Linux/Kotoba.Linux/Kotoba.Linux.csproj
version="$(dotnet msbuild "$project" -nologo -getProperty:Version)"
out="$PWD/build/linux"
deb="$out/kotoba_${version}_amd64.deb"
portable="$out/kotoba-${version}-linux-x64.tar.gz"
work="$(mktemp -d -t kotoba-linux-test.XXXXXXXX)"
trap 'rm -rf -- "$work"' EXIT

[[ "$(dpkg-deb -f "$deb" Architecture)" == amd64 ]]
[[ "$(dpkg-deb -f "$deb" Version)" == "$version" ]]
dpkg-deb -x "$deb" "$work/deb"
mkdir "$work/portable"
tar -xzf "$portable" -C "$work/portable"
dictionary="$work/deb/opt/kotoba/Data/dictionary.sqlite"
if find "$work/deb/opt/kotoba" "$work/portable/kotoba" \( -type f -o -type d \) -perm /022 -print -quit | grep -q .; then
    echo 'Unexpected group/world-writable package files' >&2
    exit 1
fi
cmp "$dictionary" "$work/portable/kotoba/Data/dictionary.sqlite"
cmp Linux/CHANGELOG.txt "$work/deb/opt/kotoba/CHANGELOG.txt"
cmp Linux/CHANGELOG.txt "$work/portable/kotoba/CHANGELOG.txt"
if command -v desktop-file-validate >/dev/null; then
    desktop-file-validate "$work/deb/usr/share/applications/kotoba.desktop"
fi
python3 - "$dictionary" <<'PY'
import hashlib
import json
from pathlib import Path
import sqlite3
import sys

target = Path(sys.argv[1])
with sqlite3.connect(target.as_uri() + '?mode=ro', uri=True) as db:
    assert db.execute('PRAGMA integrity_check').fetchone() == ('ok',)
    metadata = {key: json.loads(value) for key, value in db.execute('SELECT key,value FROM metadata')}
assert metadata['hiragana_reading_revision'] == 1
source = Path('Data/yarxi.db') if Path('Data/yarxi.db').is_file() else Path('YARXI.dmg')
assert metadata['source_sha256'] == hashlib.sha256(source.read_bytes()).hexdigest()
print('PASS: the Linux packages contain the same verified dictionary')
PY

dotnet run --project Windows/Kotoba.Tests -c Release -- "$dictionary"
python3 -m unittest discover -s tests -p test_database.py
XDG_SESSION_TYPE=x11 xvfb-run -a -s '-screen 0 1280x900x24' "$work/deb/usr/bin/kotoba" --smoke "$work/deb-smoke"
XDG_SESSION_TYPE=x11 xvfb-run -a -s '-screen 0 1280x900x24' "$work/portable/kotoba/kotoba" --smoke "$work/portable-smoke"
XDG_SESSION_TYPE=x11 xvfb-run -a bash -s -- "$work/deb/usr/bin/kotoba" "$work/single-instance" <<'SH'
set -euo pipefail
export XDG_DATA_HOME="$2/data" XDG_CONFIG_HOME="$2/config"
"$1" &
app_pid=$!
trap 'kill "$app_pid" 2>/dev/null || true; wait "$app_pid" 2>/dev/null || true' EXIT
for attempt in {1..100}; do
    [[ -f "$XDG_DATA_HOME/kotoba/learning.sqlite" ]] && break
    sleep .05
done
[[ -f "$XDG_DATA_HOME/kotoba/learning.sqlite" ]]
timeout 12 "$1" --search
kill -0 "$app_pid"
echo 'PASS: the search command was relayed to the running Linux instance'
SH
install -d "$out/previews"
install -m 0644 "$work/deb-smoke/dictionary.png" "$work/deb-smoke/card.png" "$out/previews/"
printf 'PASS: both Linux packages launched and passed the desktop smoke suite\n'
