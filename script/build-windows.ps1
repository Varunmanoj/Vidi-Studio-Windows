param([ValidateSet('ARM64','x64')][string]$Platform = 'ARM64', [ValidateSet('Debug','Release')][string]$Configuration = 'Debug', [switch]$Package)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (Get-Command python -ErrorAction SilentlyContinue) {
    & python (Join-Path $PSScriptRoot 'sync_version.py') --sync
    if ($LASTEXITCODE -ne 0) { throw 'Desktop version synchronization failed.' }
    & python (Join-Path $PSScriptRoot 'check_menu_parity.py') --allow-missing
    if ($LASTEXITCODE -ne 0) { throw 'Desktop menu parity check failed.' }
} elseif (Get-Command python3 -ErrorAction SilentlyContinue) {
    & python3 (Join-Path $PSScriptRoot 'sync_version.py') --sync
    if ($LASTEXITCODE -ne 0) { throw 'Desktop version synchronization failed.' }
    & python3 (Join-Path $PSScriptRoot 'check_menu_parity.py') --allow-missing
    if ($LASTEXITCODE -ne 0) { throw 'Desktop menu parity check failed.' }
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'Install Visual Studio with WinUI application development tools.' }
$packageOptions = @()
if ($Package) { $packageOptions = @('/p:GenerateAppxPackageOnBuild=true', '/p:AppxBundle=Never', '/p:UapAppxPackageBuildMode=SideloadOnly') }
& $msbuild (Join-Path $projectRoot 'Vidi Studio Windows\Vidi Studio Windows.csproj') /restore /t:Build /p:Configuration=$Configuration /p:Platform=$Platform /p:AppxPackageSigningEnabled=false /verbosity:minimal @packageOptions
if ($LASTEXITCODE -ne 0) { throw 'Windows build failed.' }
