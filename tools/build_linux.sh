#!/usr/bin/env bash
set -euo pipefail
umask 022
cd "$(dirname "$0")/.."

project=Linux/Kotoba.Linux/Kotoba.Linux.csproj
out="$PWD/build/linux"
work="$(mktemp -d -t kotoba-linux.XXXXXXXX)"
trap 'rm -rf -- "$work"' EXIT
mkdir -p "$out"

python3 tools/extract_database.py --ensure-legacy-export
dictionary="$PWD/Data/dictionary.sqlite"
if [[ -f Data/yarxi.db ]]; then
    python3 tools/extract_database.py --sqlite-source Data/yarxi.db --legacy "$dictionary" --output "$work"
    dictionary="$work/dictionary.windows.sqlite"
fi

version="$(dotnet msbuild "$project" -nologo -getProperty:Version)"
app="$work/kotoba"
dotnet publish "$project" -c Release -r linux-x64 --self-contained -o "$app" "-p:KotobaDictionaryPath=$dictionary"
cmp "$dictionary" "$app/Data/dictionary.sqlite"
install -m 0644 Linux/Licenses/*.txt "$app/Licenses/"
python3 - "$project" "$app" <<'PY'
import json
from pathlib import Path
import shutil
import sys

project, app = map(Path, sys.argv[1:])
assets = json.loads((project.parent / 'obj/project.assets.json').read_text())
runtime = json.loads((app / 'kotoba.runtimeconfig.json').read_text())['runtimeOptions']['includedFrameworks'][0]
packages = {
    'skiasharp.nativeassets.linux': 'SkiaSharp',
    'harfbuzzsharp.nativeassets.linux': 'HarfBuzzSharp',
    'microsoft.netcore.app.runtime.linux-x64': 'Microsoft.NETCore.App',
}
paths = {value['path'].split('/')[0]: value['path'] for value in assets['libraries'].values()
         if 'path' in value}
paths['microsoft.netcore.app.runtime.linux-x64'] = f"microsoft.netcore.app.runtime.linux-x64/{runtime['version']}"
for package, name in packages.items():
    library = next(Path(root) / paths[package] for root in assets['packageFolders']
                   if (Path(root) / paths[package]).is_dir())
    notices = [file for file in library.iterdir()
               if file.name.lower().startswith(('license.', 'third-party-notices.'))]
    if not notices:
        raise FileNotFoundError(f'License files missing for {package}')
    for file in notices:
        shutil.copyfile(file, app / 'Licenses' / f'{name}-{file.name}')
PY
find "$app" -type d -exec chmod 0755 {} +
find "$app" -type f -exec chmod 0644 {} +
chmod 0755 "$app/kotoba"
if [[ -f "$app/createdump" ]]; then
    chmod 0755 "$app/createdump"
fi

package="$work/package"
install -d "$package/DEBIAN" "$package/opt" "$package/usr/bin" "$package/usr/share/doc/kotoba"
cp -a "$app" "$package/opt/kotoba"
ln -s ../../opt/kotoba/kotoba "$package/usr/bin/kotoba"
install -Dm0644 Linux/kotoba.desktop "$package/usr/share/applications/kotoba.desktop"
gzip -n -c Linux/CHANGELOG.txt > "$package/usr/share/doc/kotoba/changelog.gz"
install -m 0644 LICENSE "$package/usr/share/doc/kotoba/copyright"
cat > "$package/DEBIAN/control" <<EOF
Package: kotoba
Version: $version
Section: education
Priority: optional
Architecture: amd64
Maintainer: Kotoba project
Homepage: https://github.com/igoriilla-projects/Kotoba-Dictionary
Depends: libc6 (>= 2.34), libgcc-s1, libstdc++6, libgssapi-krb5-2, libssl3t64 | libssl3, libicu78 | libicu76 | libicu74 | libicu72, zlib1g, libsqlite3-0, libfontconfig1, libx11-6, libx11-xcb1, libxcb1, libice6, libsm6, libxext6, libxrandr2, libxi6, libxrender1, libxcursor1, libgl1
Recommends: xwayland
Description: Offline Japanese-Russian dictionary and study cards
 Kotoba includes dictionary search, saved lists, FSRS review and a desktop card.
EOF

deb="kotoba_${version}_amd64.deb"
portable="kotoba-${version}-linux-x64.tar.gz"
dpkg-deb --root-owner-group --build "$package" "$work/$deb"
tar -C "$work" -czf "$work/$portable" kotoba
for artifact in "$deb" "$portable"; do
    install -m 0644 "$work/$artifact" "$out/$artifact.tmp"
    mv -f -- "$out/$artifact.tmp" "$out/$artifact"
done
printf '\nBuilt:\n  %s\n  %s\n' "$out/$deb" "$out/$portable"
