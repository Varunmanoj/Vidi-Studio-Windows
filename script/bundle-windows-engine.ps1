param([string]$Python)
$ErrorActionPreference = 'Stop'
$engineProjectRoot = Split-Path $PSScriptRoot -Parent
if (!$Python) { $Python = Join-Path $engineProjectRoot '.venv-windows\Scripts\python.exe' }
Push-Location $engineProjectRoot
try {
    & $Python -m PyInstaller --noconfirm --onedir --name vidistudio-engine --distpath dist/windows --workpath build/engine --specpath build --add-data "$engineProjectRoot/engine/ai-defaults.json;." --collect-all google.genai --collect-all yt_dlp --collect-all yt_dlp_ejs --copy-metadata yt-dlp --copy-metadata yt-dlp-ejs engine/vidistudio_engine.py
    if ($LASTEXITCODE -ne 0) { throw 'Engine packaging failed.' }
    $denoBinary = & $Python -c 'from deno._find_deno import find_deno_bin; print(find_deno_bin())'
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $denoBinary)) { throw 'The locked YouTube JavaScript runtime is unavailable.' }
    New-Item -ItemType Directory -Force dist/windows/vidistudio-engine/media | Out-Null
    Copy-Item -LiteralPath $denoBinary -Destination dist/windows/vidistudio-engine/media/deno.exe -Force
    New-Item -ItemType Directory -Force runtime/engine | Out-Null
    Copy-Item -Path 'dist/windows/vidistudio-engine/*' -Destination runtime/engine -Recurse -Force
} finally { Pop-Location }
