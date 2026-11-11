param([string]$Python)
$ErrorActionPreference = 'Stop'
$engineProjectRoot = Split-Path $PSScriptRoot -Parent
if (!$Python) { $Python = Join-Path $engineProjectRoot '.venv-windows\Scripts\python.exe' }
Push-Location $engineProjectRoot
try {
    # Some managed Python distributions patch ssl.py to load this companion policy.
    # Preserve it in the frozen module directory rather than changing TLS verification.
    $sslPolicy = & $Python -c "import ssl,pathlib; print(pathlib.Path(ssl.__file__).with_name('codex-rejected-cas.pem'))"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect Python SSL support files.' }
    $sslData = @()
    if (Test-Path -LiteralPath $sslPolicy) { $sslData = @('--add-data', ($sslPolicy + ';.')) }
    $oldErrorPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $Python -m PyInstaller --noconfirm --onedir --name vidistudio-engine --distpath dist/windows --workpath build/engine --specpath build --add-data "$engineProjectRoot/engine/ai-defaults.json;." --collect-all google.genai --collect-all yt_dlp --collect-all yt_dlp_ejs --copy-metadata yt-dlp --copy-metadata yt-dlp-ejs @sslData engine/vidistudio_engine.py 2>&1 | ForEach-Object { Write-Host $_ }
    $engineBuildExit = $LASTEXITCODE
    $ErrorActionPreference = $oldErrorPreference
    if ($engineBuildExit -ne 0) { throw 'Engine packaging failed.' }
    $denoBinary = & $Python -c 'from deno._find_deno import find_deno_bin; print(find_deno_bin())'
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $denoBinary)) { throw 'The locked YouTube JavaScript runtime is unavailable.' }
    New-Item -ItemType Directory -Force dist/windows/vidistudio-engine/media | Out-Null
    Copy-Item -LiteralPath $denoBinary -Destination dist/windows/vidistudio-engine/media/deno.exe -Force
    New-Item -ItemType Directory -Force runtime/engine | Out-Null
    Copy-Item -Path 'dist/windows/vidistudio-engine/*' -Destination runtime/engine -Recurse -Force
    $health = & (Join-Path $engineProjectRoot 'runtime/engine/vidistudio-engine.exe') doctor
    if ($LASTEXITCODE -ne 0 -or !(($health | Select-Object -Last 1 | ConvertFrom-Json).sslReady)) {
        throw 'The packaged engine cannot initialize verified TLS. Check bundled SSL support files.'
    }
} finally { Pop-Location }
