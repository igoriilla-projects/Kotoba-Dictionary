$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$buildRoot=Join-Path $repoRoot 'build'
$targetDir=Join-Path $buildRoot 'windows-install-smoke'
$setupExe=Join-Path $buildRoot 'windows/Kotoba-Setup-x64.exe'
function Run-Hidden([string]$exe,[string[]]$arguments){
    $process=Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if(-not $process.WaitForExit(60000)){throw ('Timed out: '+$exe)}
    if($process.ExitCode -ne 0){throw ('Exit '+$process.ExitCode+': '+$exe)}
}
if(-not (Test-Path -LiteralPath $setupExe)){throw 'Build the installer first with tools/build_windows.ps1'}
# TESTMODE suppresses protocol, shortcuts and uninstall-registry registration.
# Both installs and the uninstaller operate only on this named workspace directory.
$resolvedBuild=[IO.Path]::GetFullPath($buildRoot)+[IO.Path]::DirectorySeparatorChar
if(-not ([IO.Path]::GetFullPath($targetDir)).StartsWith($resolvedBuild,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe test target'}
$installArgs=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/TESTMODE=1',('/DIR="'+$targetDir+'"'))
Run-Hidden $setupExe $installArgs
$previews=Join-Path $buildRoot 'windows-installed-previews'
Run-Hidden (Join-Path $targetDir 'Kotoba.exe') @('--smoke',('"'+$previews+'"'))
$report=Get-Content (Join-Path $previews 'report.json') -Raw | ConvertFrom-Json
$database=Join-Path $report.DataDirectory 'learning.sqlite'
$before=(Get-FileHash -LiteralPath $database).Hash
$widgetFile=Join-Path $report.DataDirectory 'widgets.json'
$widgetsBefore=(Get-FileHash -LiteralPath $widgetFile).Hash
Run-Hidden $setupExe $installArgs
if((Get-FileHash -LiteralPath $database).Hash -ne $before){throw 'Upgrade changed learning data'}
if((Get-FileHash -LiteralPath $widgetFile).Hash -ne $widgetsBefore){throw 'Upgrade changed desktop widgets'}
Run-Hidden (Join-Path $targetDir 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
if(Test-Path -LiteralPath (Join-Path $targetDir 'Kotoba.exe')){throw 'Uninstall did not remove app'}
if((Get-FileHash -LiteralPath $database).Hash -ne $before){throw 'Uninstall changed learning data'}
if((Get-FileHash -LiteralPath $widgetFile).Hash -ne $widgetsBefore){throw 'Uninstall changed desktop widgets'}
Write-Output 'PASS: isolated install, installed-app WPF smoke, upgrade, uninstall, retained learning database and desktop widgets.'
