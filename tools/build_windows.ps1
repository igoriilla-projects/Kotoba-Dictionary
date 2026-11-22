param([switch]$SkipTests,[switch]$AllowSandboxIpcSkip)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
$previousIpcSkip=$env:KOTOBA_ALLOW_IPC_SKIP
try {
    if($AllowSandboxIpcSkip){$env:KOTOBA_ALLOW_IPC_SKIP='1'}
    $dictionary = 'Data/dictionary.sqlite'
    python tools/extract_database.py --ensure-legacy-export
    if ($LASTEXITCODE -ne 0) { throw 'Baseline dictionary export failed' }
    if (Test-Path -LiteralPath 'Data/yarxi.db' -PathType Leaf) {
        python tools/extract_database.py --sqlite-source Data/yarxi.db --legacy $dictionary
        if ($LASTEXITCODE -ne 0) { throw 'Updated dictionary export failed' }
        $dictionary = 'Data/dictionary.windows.sqlite'
    }
    python tools/prepare_windows.py
    if ($LASTEXITCODE -ne 0) { throw 'Dependency preparation failed' }
    if (-not $SkipTests) {
        dotnet run --project Windows/Kotoba.Tests -c Release -- $dictionary
        if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
    }
    $publishDir = Join-Path $repoRoot 'build/windows/app'
    dotnet publish Windows/Kotoba.Windows -c Release --self-contained false -o $publishDir --ignore-failed-sources "-p:KotobaDictionaryPath=$(Join-Path $repoRoot $dictionary)"
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    # .NET 10 apphost searches .runtime before the machine-wide installation.
    # Bundling the official runtime archives also works when NuGet is unavailable.
    New-Item -ItemType Directory -Path (Join-Path $publishDir '.runtime') -Force | Out-Null
    Copy-Item -Path 'build/windows-tools/runtime/*' -Destination (Join-Path $publishDir '.runtime') -Recurse -Force
    $compiler = Join-Path $repoRoot 'build/windows-tools/inno/ISCC.exe'
    & $compiler /Qp ('/DPublishDir=' + $publishDir) ('/DOutputDir=' + (Join-Path $repoRoot 'build/windows')) Windows/Installer/Kotoba.iss
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
    Get-FileHash build/windows/Kotoba-Setup-x64.exe
}
finally { $env:KOTOBA_ALLOW_IPC_SKIP=$previousIpcSkip; Pop-Location }
