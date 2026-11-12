param([switch]$Installer,[switch]$AllowSandboxIpcSkip)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
$previousIpcSkip=$env:KOTOBA_ALLOW_IPC_SKIP
try {
    if($AllowSandboxIpcSkip){$env:KOTOBA_ALLOW_IPC_SKIP='1'}
    dotnet run --project Windows/Kotoba.Tests -c Release -- Data/dictionary.sqlite
    if($LASTEXITCODE -ne 0){throw 'Core tests failed'}
    python -m unittest discover -s tests -p test_database.py
    if($LASTEXITCODE -ne 0){throw 'Dictionary source tests failed'}
    dotnet build Windows/Kotoba.Windows -c Release --ignore-failed-sources
    if($LASTEXITCODE -ne 0){throw 'App build failed'}
    $app=Join-Path $repoRoot 'Windows/Kotoba.Windows/bin/Release/net10.0-windows/Kotoba.exe'
    $previews=Join-Path $repoRoot 'build/windows-previews'
    $process=Start-Process -FilePath $app -ArgumentList '--smoke',('"'+$previews+'"') -WindowStyle Hidden -PassThru
    if(-not $process.WaitForExit(60000)){throw 'WPF smoke test timed out'}
    if($process.ExitCode -ne 0){throw ('WPF smoke failed; see '+$previews+'.error')}
    Get-Content (Join-Path $previews 'report.json')
    if($Installer){ & (Join-Path $PSScriptRoot 'test_windows_installer.ps1') }
}
finally {$env:KOTOBA_ALLOW_IPC_SKIP=$previousIpcSkip;Pop-Location}
