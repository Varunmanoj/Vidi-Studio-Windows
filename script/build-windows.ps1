param([ValidateSet('ARM64','x64')][string]$Platform = 'ARM64', [ValidateSet('Debug','Release')][string]$Configuration = 'Debug', [switch]$Package, [switch]$Rebuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
# Piper is bundled with the app; fetch and verify the runtime before MSBuild evaluates content.
& (Join-Path $PSScriptRoot 'fetch-piper.ps1')
if ($LASTEXITCODE -ne 0) { throw 'The bundled Piper speech engine could not be prepared.' }
if (Get-Command python -ErrorAction SilentlyContinue) {
    & python (Join-Path $PSScriptRoot 'sync_version.py') --sync
    if ($LASTEXITCODE -ne 0) { throw 'Desktop version synchronization failed.' }
    & python (Join-Path $PSScriptRoot 'check_menu_parity.py') --allow-missing
    if ($LASTEXITCODE -ne 0) { throw 'Desktop menu parity check failed.' }
    & python (Join-Path $PSScriptRoot 'check_xaml_references.py')
    if ($LASTEXITCODE -ne 0) { throw 'Inspector XAML/source reference check failed.' }
} elseif (Get-Command python3 -ErrorAction SilentlyContinue) {
    & python3 (Join-Path $PSScriptRoot 'sync_version.py') --sync
    if ($LASTEXITCODE -ne 0) { throw 'Desktop version synchronization failed.' }
    & python3 (Join-Path $PSScriptRoot 'check_menu_parity.py') --allow-missing
    if ($LASTEXITCODE -ne 0) { throw 'Desktop menu parity check failed.' }
    & python3 (Join-Path $PSScriptRoot 'check_xaml_references.py')
    if ($LASTEXITCODE -ne 0) { throw 'Inspector XAML/source reference check failed.' }
}
# The app prefers a frozen engine under runtime\engine (copied to engine-bin). A frozen build
# older than the engine sources silently runs outdated logic, for example a pre-VIDISTUDIO_API_KEY
# key handoff, so rebuild it before the app can copy and run it.
$engineDir = Join-Path $projectRoot 'engine'
$frozenEngine = Join-Path $projectRoot 'runtime\engine\vidistudio-engine.exe'
if (Test-Path -LiteralPath $frozenEngine) {
    $frozenTime = (Get-Item -LiteralPath $frozenEngine).LastWriteTimeUtc
    $stale = Get-ChildItem -LiteralPath $engineDir -Filter *.py -File | Where-Object { $_.LastWriteTimeUtc -gt $frozenTime }
    if ($stale) {
        $venvPython = Join-Path $projectRoot '.venv-windows\Scripts\python.exe'
        if (Test-Path -LiteralPath $venvPython) {
            Write-Host 'Rebuilding the stale frozen engine because the engine sources changed...'
            & (Join-Path $PSScriptRoot 'bundle-windows-engine.ps1') -Python $venvPython
            if ($LASTEXITCODE -ne 0) { throw 'The frozen engine rebuild failed.' }
        }
        else {
            throw 'runtime\engine is older than the engine sources and .venv-windows is missing. Run script\setup-windows.ps1 -FreezeEngine, or delete the runtime\engine folder to run the bundled source engine.'
        }
    }
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'Install Visual Studio with WinUI application development tools.' }
# The modern File Explorer context menu uses a native C++ COM surrogate. Resolve
# the VC targets from the selected Visual Studio installation so scripted builds
# work with VS 2022 (v170), newer Visual Studio releases (v180), and the toolset
# installed with that release. Do not hard-code v143.
$msbuildRoot = Split-Path (Split-Path (Split-Path $msbuild -Parent) -Parent) -Parent
$vcRoot = Join-Path $msbuildRoot 'Microsoft\VC'
$vcTargets = $null
if (Test-Path -LiteralPath $vcRoot) {
    $vcTargets = Get-ChildItem -LiteralPath $vcRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^v\d+$' } |
        Sort-Object { [int]$_.Name.Substring(1) } -Descending |
        Select-Object -First 1
}
$vcTargetsArgs = @()
if ($vcTargets) { $vcTargetsArgs += "/p:VCTargetsPath=$($vcTargets.FullName)" }
$packageOptions = @()
if ($Package) { $packageOptions = @('/p:GenerateAppxPackageOnBuild=true', '/p:AppxBundle=Never', '/p:UapAppxPackageBuildMode=SideloadOnly') }
$target = if ($Rebuild) { '/t:Rebuild' } else { '/t:Build' }
if ($Rebuild) {
    foreach ($cache in @((Join-Path $env:LOCALAPPDATA 'Temp\XamlCompiler'), (Join-Path $env:TEMP 'XamlCompiler'))) {
        if ($cache -and (Test-Path $cache)) { Remove-Item $cache -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
& $msbuild (Join-Path $projectRoot 'Vidi Studio Windows\Vidi Studio Windows.csproj') /restore $target /p:Configuration=$Configuration /p:Platform=$Platform /p:AppxPackageSigningEnabled=false /verbosity:minimal @packageOptions @vcTargetsArgs
if ($LASTEXITCODE -ne 0) { throw 'Windows build failed.' }
