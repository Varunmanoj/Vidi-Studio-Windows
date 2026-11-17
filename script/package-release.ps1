param(
    [Parameter(Mandatory)][ValidateSet('ARM64','x64')][string]$Platform,
    [Parameter(Mandatory)][string]$FfmpegDirectory,
    [string]$CertificateThumbprint,
    [switch]$CreateTestCertificate,
    [string]$Python
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$Python) { $Python = Join-Path $root '.venv-windows\Scripts\python.exe' }
if ($CreateTestCertificate -and $CertificateThumbprint) { throw 'Choose a test certificate or an existing thumbprint.' }
if (!$CreateTestCertificate -and !$CertificateThumbprint) { throw 'Supply -CertificateThumbprint or explicitly choose -CreateTestCertificate.' }
[xml]$manifest = Get-Content (Join-Path $root 'Vidi Studio Windows\Package.appxmanifest')
$publisher = [string]$manifest.Package.Identity.Publisher
$version = [string]$manifest.Package.Identity.Version
$output = Join-Path $root "dist\release\$version\$Platform"
New-Item -ItemType Directory -Force $output | Out-Null
if ($CreateTestCertificate) {
    $certificate = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object { $_.Subject -eq $publisher -and $_.FriendlyName -eq 'VidiStudio TEST package signing' -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
        Sort-Object NotAfter -Descending | Select-Object -First 1
    if (!$certificate) {
        $certificate = New-SelfSignedCertificate -Type Custom -Subject $publisher `
            -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
            -KeyExportPolicy NonExportable -CertStoreLocation Cert:\CurrentUser\My `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}') `
            -FriendlyName 'VidiStudio TEST package signing' -NotAfter (Get-Date).AddYears(1)
    }
} else {
    $certificate = Get-Item "Cert:\CurrentUser\My\$CertificateThumbprint"
}
if (!$certificate.HasPrivateKey -or $certificate.Subject -cne $publisher -or $certificate.NotAfter -lt (Get-Date)) {
    throw 'A valid private signing key with a Subject exactly matching manifest Publisher is required.'
}
$signTool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter signtool.exe -Recurse |
    Where-Object { $_.Directory.Name -eq 'arm64' -or $_.Directory.Name -eq 'x64' } |
    Sort-Object FullName -Descending | Select-Object -First 1
if (!$signTool) { throw 'Install Windows SDK SignTool.' }
# Suppress interactive archive progress in automated builds.
$previousProgress = $ProgressPreference
$ProgressPreference = 'SilentlyContinue'
$engine = Join-Path $root 'runtime\engine\vidistudio-engine.exe'
if (!(Test-Path $engine)) { throw 'Run setup-windows.ps1 -FreezeEngine with Python for this CPU architecture first.' }
# The engine resolves these tools beside its executable. They must not depend on
# the publisher's PATH or a media-tool installation on the customer's computer.
$media = Join-Path $root 'runtime\engine\media'
New-Item -ItemType Directory -Force $media | Out-Null
foreach ($name in @('ffmpeg.exe','ffprobe.exe')) {
    Copy-Item -LiteralPath (Join-Path $FfmpegDirectory $name) -Destination (Join-Path $media $name) -Force
}
# MSBuild must see the same ReadyToRun/RID values during restore and publish.
$previousSelfContained = $env:SelfContained
$previousReadyToRun = $env:PublishReadyToRun
$previousRuntimeIdentifier = $env:RuntimeIdentifier
$env:SelfContained = 'true'
$env:PublishReadyToRun = 'true'
$env:RuntimeIdentifier = 'win-' + $Platform.ToLowerInvariant()
Push-Location $root
try {
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    # Content globs are evaluated before the app's BeforeBuild helper target.
    # Publish the speech host first so a fresh CPU build includes its files.
    & dotnet publish (Join-Path $root 'SpeechHost\SpeechHost.csproj') -c Release -r $env:RuntimeIdentifier --self-contained true "-p:Platform=$Platform" -o (Join-Path $root "runtime\speech\$Platform") 2>&1 | ForEach-Object { Write-Host $_ }
    $speechExit = $LASTEXITCODE
    if ($speechExit -ne 0) {
        $ErrorActionPreference = $oldPreference
        throw 'Speech helper publishing failed.'
    }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build-windows.ps1') -Platform $Platform -Configuration Release -Package 2>&1 | ForEach-Object { Write-Host $_ }
    $buildExit = $LASTEXITCODE
    $ErrorActionPreference = $oldPreference
    if ($buildExit -ne 0) { throw 'Release build failed.' }
    # The normal build may refresh a stale frozen engine. Check after that build.
    foreach ($name in @('ffmpeg.exe','ffprobe.exe')) {
        if (!(Test-Path (Join-Path $media $name))) { throw 'Engine was refreshed. Rerun packaging to include FFmpeg in the next build.' }
    }
    $package = Get-ChildItem 'Vidi Studio Windows\AppPackages' -Filter *.msix -Recurse |
        Where-Object { $_.Name -like "*$version*" -and $_.Name -notmatch 'Debug' -and $_.Directory.Name -match "(?i)_$Platform(?:_|$)" } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (!$package) { throw 'No matching Release MSIX was generated.' }
    & $Python (Join-Path $PSScriptRoot 'verify-windows-package.py') $package.FullName --platform $Platform.ToLowerInvariant() --release
    if ($LASTEXITCODE -ne 0) { throw 'Package inspection failed.' }
    $suffix = if ($CreateTestCertificate) { '-SELF-SIGNED' } else { '' }
    $name = "VidiStudio-$version-windows-$($Platform.ToLowerInvariant())$suffix"
    $msix = Join-Path $output "$name.msix"
    $baseName = "VidiStudio-$version-windows-$($Platform.ToLowerInvariant())"
    foreach ($oldName in @("$baseName.msix", "$baseName-TEST.msix", "$baseName-SELF-SIGNED.msix")) {
        $oldPackage = Join-Path $output $oldName
        if ($oldPackage -ne $msix -and (Test-Path $oldPackage)) { Remove-Item $oldPackage -Force }
    }
    Copy-Item $package.FullName $msix -Force
    $signArguments = @('sign','/fd','SHA256','/sha1',$certificate.Thumbprint)
    if (!$CreateTestCertificate) { $signArguments += @('/tr','http://timestamp.digicert.com','/td','SHA256') }
    & $signTool.FullName @signArguments $msix
    if ($LASTEXITCODE -ne 0) { throw 'MSIX signing failed.' }
    # /pa also checks local trust. A new test certificate is deliberately not
    # installed in Trusted People automatically, so inspect its signer instead.
    $signature = Get-AuthenticodeSignature -LiteralPath $msix
    if (!$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint -or $signature.Status -notin @('Valid','UnknownError','NotTrusted')) {
        throw "Unexpected package signature: $($signature.Status)"
    }
    Export-Certificate -Cert $certificate -FilePath (Join-Path $output 'VidiStudio-signing.cer') -Force | Out-Null
    $dependencies = Join-Path $package.Directory.FullName 'Dependencies'
    if (Test-Path (Join-Path $output 'Dependencies')) { Remove-Item (Join-Path $output 'Dependencies') -Recurse -Force }
    if (Test-Path $dependencies) { Copy-Item $dependencies $output -Recurse -Force }
    @"
VidiStudio $version / $Platform
Signing subject: $publisher
Thumbprint: $($certificate.Thumbprint)
Test certificate: $CreateTestCertificate
This folder includes the MSIX, public certificate and runtime dependencies.
For a self-signed build only, an administrator must explicitly trust the public CER in
Cert:\LocalMachine\TrustedPeople before installation. Never distribute private keys.
Install dependencies from the matching CPU folder, then install the MSIX using
Add-AppxPackage -Path <MSIX> -DependencyPath <dependency MSIX paths>.
The ARM64 package includes x64 Piper/FFmpeg and requires Windows 11 ARM emulation.
Browser extensions still require separate browser installation/consent.
Public release also requires completing the repository FFmpeg distribution review.
"@ | Set-Content (Join-Path $output 'INSTALL.txt') -Encoding UTF8
    Get-FileHash $msix -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_.Path))" } |
        Set-Content (Join-Path $output 'SHA256SUMS.txt') -Encoding ASCII
    Compress-Archive -CompressionLevel NoCompression -Path (Join-Path $output '*') -DestinationPath (Join-Path (Split-Path $output -Parent) "$name.zip") -Force
    Write-Output "Created $msix"
    Write-Output "Signer $($certificate.Thumbprint); trust status $($signature.Status)"
} finally {
    Pop-Location
    $ProgressPreference = $previousProgress
    $env:SelfContained = $previousSelfContained
    $env:PublishReadyToRun = $previousReadyToRun
    $env:RuntimeIdentifier = $previousRuntimeIdentifier
}
