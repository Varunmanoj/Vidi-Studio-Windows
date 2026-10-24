param([switch]$Force)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$runtimeRoot = Join-Path $projectRoot 'runtime'
$runtime = Join-Path $runtimeRoot 'piper'
$piper = Join-Path $runtime 'piper.exe'
$version = '2023.11.14-2'
$url = "https://github.com/rhasspy/piper/releases/download/$version/piper_windows_amd64.zip"
# Official rhasspy/piper Windows x64 release used by every Vidi Studio build.
$sha256 = 'F3C58906402B24F3A96D92145F58ACBA6D86C9B5DB896D207F78DC80811EFCEA'
$marker = Join-Path $runtime '.version'
if (!$Force -and (Test-Path -LiteralPath $piper) -and (Test-Path -LiteralPath $marker) -and
    ((Get-Content -LiteralPath $marker -Raw).Trim() -eq $version)) {
    Write-Host "Piper $version is already available in runtime\piper."
    exit 0
}
New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ('piper-' + [guid]::NewGuid().ToString('N') + '.zip')
try {
    Write-Host "Downloading Piper $version..."
    Invoke-WebRequest -Uri $url -OutFile $temp -UseBasicParsing
    $actual = (Get-FileHash -LiteralPath $temp -Algorithm SHA256).Hash
    if ($actual -ne $sha256) { throw "The Piper download failed its SHA-256 check. Expected $sha256, received $actual." }
    if (Test-Path -LiteralPath $runtime) { Remove-Item -LiteralPath $runtime -Recurse -Force }
    # The archive contains a piper\ folder, so extracting into runtime creates runtime\piper.
    Expand-Archive -LiteralPath $temp -DestinationPath $runtimeRoot -Force
    if (!(Test-Path -LiteralPath $piper)) { throw 'The Piper archive did not contain piper.exe.' }
    Set-Content -LiteralPath $marker -Value $version -Encoding ascii
    Write-Host "Piper $version is ready in runtime\piper."
} finally {
    Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
}
