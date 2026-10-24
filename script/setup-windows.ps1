param(
    [ValidateSet('ARM64', 'x64')][string]$Platform = 'ARM64',
    [string]$Python = 'python.exe',
    [switch]$FreezeEngine,
    [switch]$LegacySapiHosts
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    & $Python -m venv .venv-windows
    if ($LASTEXITCODE -ne 0) { throw 'Python 3.11 or later is required. Supply -Python with its full executable path.' }
    $venvPython = Join-Path $projectRoot '.venv-windows\Scripts\python.exe'
    & $venvPython -m pip install -r engine/requirements.txt
    if ($LASTEXITCODE -ne 0) { throw 'Engine dependencies could not be installed.' }
    & (Join-Path $PSScriptRoot 'fetch-piper.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'The bundled Piper speech engine could not be prepared.' }
    $speechPlatforms = @($Platform)
    if ($LegacySapiHosts) { $speechPlatforms = @($Platform, 'x64', 'x86') | Select-Object -Unique }
    foreach ($speechPlatform in $speechPlatforms) {
        $rid = 'win-' + $speechPlatform.ToLowerInvariant()
        dotnet publish SpeechHost/SpeechHost.csproj -c Release -r $rid --self-contained true -p:Platform=$speechPlatform -o "runtime/speech/$speechPlatform"
        if ($LASTEXITCODE -ne 0) { throw "Speech host build failed for $speechPlatform." }
    }
    if ($FreezeEngine) {
        & $venvPython -m pip install pyinstaller
        if ($LASTEXITCODE -ne 0) { throw 'PyInstaller installation failed.' }
        & (Join-Path $PSScriptRoot 'bundle-windows-engine.ps1') -Python $venvPython
    }
    Write-Host "Ready to build $Platform in Visual Studio. For source-engine mode, set Python executable in Settings to: $venvPython"
    Write-Host 'FFmpeg and FFprobe must be on PATH or configured in Settings. Restart Visual Studio after changing PATH.'
} finally { Pop-Location }
