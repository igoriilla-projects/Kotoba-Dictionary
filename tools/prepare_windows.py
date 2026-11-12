"""Fetch pinned build dependencies into the workspace; Python uses the configured proxy.
Runtime SHA-512 values are from Microsoft's release metadata. Inno is SHA-256 pinned.
No global installation, registry changes, or SDK modifications.
"""
from pathlib import Path
import hashlib
import json
import subprocess
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
CACHE = ROOT / 'build' / 'windows-tools'
RUNTIME = '10.0.15'
FILES = [
    ('runtime.zip', f'https://builds.dotnet.microsoft.com/dotnet/Runtime/{RUNTIME}/dotnet-runtime-{RUNTIME}-win-x64.zip', 'sha512', '06f0bedd59ae4ac722ada805a6d6bbf605bcdc790c71f59b7003878072902b5cde5dec8c40c0baa81192660d4748e9d79afdbdcb0c0ea9d6b821d0a044c4c6dd'),
    ('windowsdesktop.zip', f'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/{RUNTIME}/windowsdesktop-runtime-{RUNTIME}-win-x64.zip', 'sha512', 'd82ee2e70f0c1ee7af3a908a944541993782aa80a3fd2b2e02a3fdf27edc81c0ad946f14a3fe34bae295f1b7a23f5d35a466239726e4457bb200de0344e077c4'),
    ('innosetup.exe', 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe', 'sha256', '8a5a6711c3766a6e5fb74f280ba0e3551e719ce94538d901fdbd1f8f9920b3f0'),
]

def main():
    CACHE.mkdir(parents=True, exist_ok=True)
    for name, url, algorithm, expected in FILES:
        target = CACHE / name
        if not target.exists():
            print(f'Downloading {name}', flush=True)
            with urllib.request.urlopen(url, timeout=120) as response:
                data = response.read()
            if hashlib.new(algorithm, data).hexdigest() != expected:
                raise RuntimeError(f'Hash mismatch for {name}')
            target.write_bytes(data)
        if hashlib.new(algorithm, target.read_bytes()).hexdigest() != expected:
            raise RuntimeError(f'Cached hash mismatch for {name}')
        if name.endswith('.zip'):
            with zipfile.ZipFile(target) as archive:
                archive.extractall(CACHE / 'runtime')
    compiler = CACHE / 'inno' / 'ISCC.exe'
    if not compiler.exists():
        flags = getattr(subprocess, 'CREATE_NO_WINDOW', 0)
        subprocess.run([str(CACHE / 'innosetup.exe'), '/VERYSILENT', '/SUPPRESSMSGBOXES',
                        '/NORESTART', '/PORTABLE=1', '/CURRENTUSER', '/NOICONS', '/TASKS=',
                        f'/DIR={CACHE / "inno"}'], check=True, creationflags=flags)
    if not compiler.exists():
        raise RuntimeError('Inno compiler is missing')
    print('Windows build dependencies ready.', flush=True)

if __name__ == '__main__':
    main()
