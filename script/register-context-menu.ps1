<#
.SYNOPSIS
    Registers or unregisters the "Describe with Vidi Studio" context menu entry
    and "Open with" handler for video files in Windows File Explorer.

.DESCRIPTION
    Creates user-level file associations in HKCU:\Software\Classes so that:
    1. A dedicated "Describe with Vidi Studio" menu item (with app icon) appears
       in the Windows 11 context menu and "Show more options" (classic menu) for
       all supported video files (.mp4, .mov, .m4v, .mkv, .avi, .webm, .wmv).
    2. "Vidi Studio" is registered in the "Open with" submenu and handler list.
    3. Notifies Windows Explorer immediately via SHChangeNotify without restarting explorer.exe.

.PARAMETER ExePath
    Explicit path to Vidi Studio Windows.exe. If omitted, the script auto-discovers
    the built executable from bin/ or installed package layout.

.PARAMETER Unregister
    If specified, removes all registry keys associated with Vidi Studio context menus.
#>

[CmdletBinding()]
param(
    [string]$ExePath,
    [switch]$Unregister
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoDir = Split-Path -Parent $scriptDir
if (-not (Test-Path (Join-Path $repoDir "Vidi Studio Windows"))) {
    $winDir = Join-Path $repoDir "Vidi-Studio-Windows\Vidi Studio Windows"
    if (Test-Path $winDir) { $projectRoot = Join-Path $repoDir "Vidi-Studio-Windows" }
    else { $projectRoot = $repoDir }
} else {
    $projectRoot = $repoDir
}

$videoExtensions = @(".mp4", ".mov", ".m4v", ".mkv", ".avi", ".webm", ".wmv")
$verbKey = "DescribeWithVidiStudio"
$legacyVerbKey = "OpenInVidiStudio"
$menuText = "Describe with Vidi Studio"
$appName = "Vidi Studio"

function Notify-ShellChange {
    try {
        $signature = @'
[DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
'@
        $type = Add-Type -MemberDefinition $signature -Name "NativeShellNotify" -Namespace "VidiStudioShell" -PassThru -ErrorAction SilentlyContinue
        if ($type) {
            $type::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
        }
    } catch { }
}

function Remove-RegistryKeySafely([string]$path) {
    if (Test-Path $path) {
        Remove-Item -Path $path -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($Unregister) {
    Write-Host "Unregistering Vidi Studio context menus from HKCU:\Software\Classes..." -ForegroundColor Yellow

    Remove-RegistryKeySafely "HKCU:\Software\Classes\SystemFileAssociations\video\shell\$verbKey"
    Remove-RegistryKeySafely "HKCU:\Software\Classes\SystemFileAssociations\video\shell\$legacyVerbKey"
    Remove-RegistryKeySafely "HKCU:\Software\Classes\*\shell\$verbKey"
    Remove-RegistryKeySafely "HKCU:\Software\Classes\*\shell\$legacyVerbKey"

    foreach ($ext in $videoExtensions) {
        Remove-RegistryKeySafely "HKCU:\Software\Classes\SystemFileAssociations\$ext\shell\$verbKey"
        Remove-RegistryKeySafely "HKCU:\Software\Classes\SystemFileAssociations\$ext\shell\$legacyVerbKey"
        Remove-RegistryKeySafely "HKCU:\Software\Classes\$ext\OpenWithList\Vidi Studio Windows.exe"
        Remove-RegistryKeySafely "HKCU:\Software\Classes\$ext\OpenWithList\Vidi Studio.exe"
        try {
            $owp = Get-Item "HKCU:\Software\Classes\$ext\OpenWithProgids" -ErrorAction SilentlyContinue
            if ($owp -and $owp.GetValue("VidiStudio.Video") -ne $null) {
                Remove-ItemProperty -Path "HKCU:\Software\Classes\$ext\OpenWithProgids" -Name "VidiStudio.Video" -Force -ErrorAction SilentlyContinue
            }
        } catch { }
    }

    Remove-RegistryKeySafely "HKCU:\Software\Classes\VidiStudio.Video"
    Remove-RegistryKeySafely "HKCU:\Software\Classes\Applications\Vidi Studio Windows.exe"
    Remove-RegistryKeySafely "HKCU:\Software\Classes\Applications\Vidi Studio.exe"

    Notify-ShellChange
    Write-Host "Successfully unregistered Vidi Studio context menu entries." -ForegroundColor Green
    return
}

# Auto-discover ExePath if not provided
if (-not $ExePath) {
    $candidates = @(
        (Join-Path $projectRoot "Vidi Studio Windows\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\AppX\Vidi Studio Windows.exe"),
        (Join-Path $projectRoot "Vidi Studio Windows\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\AppX\Vidi Studio Windows.exe"),
        (Join-Path $projectRoot "Vidi Studio Windows\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\Vidi Studio Windows.exe"),
        (Join-Path $projectRoot "Vidi Studio Windows\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\Vidi Studio Windows.exe"),
        (Join-Path $projectRoot "Vidi Studio Windows\bin\ARM64\Debug\net8.0-windows10.0.19041.0\win-arm64\AppX\Vidi Studio Windows.exe"),
        (Join-Path $projectRoot "Vidi Studio Windows\bin\ARM64\Release\net8.0-windows10.0.19041.0\win-arm64\AppX\Vidi Studio Windows.exe")
    )
    foreach ($cand in $candidates) {
        if (Test-Path $cand) {
            $ExePath = (Resolve-Path $cand).Path
            break
        }
    }
}

if (-not $ExePath -or -not (Test-Path $ExePath)) {
    Write-Error "Could not find Vidi Studio executable. Build the solution first or pass -ExePath <path to exe>."
    return
}

$ExePath = (Resolve-Path $ExePath).Path
$exeName = [System.IO.Path]::GetFileName($ExePath)
$command = "`"$ExePath`" --video `"%1`""
$icon = "`"$ExePath`",0"

Write-Host "Registering context menu for: $ExePath" -ForegroundColor Cyan

# 1. Clean up legacy keys
Remove-RegistryKeySafely "HKCU:\Software\Classes\SystemFileAssociations\video\shell\$legacyVerbKey"
Remove-RegistryKeySafely "HKCU:\Software\Classes\*\shell\$legacyVerbKey"
foreach ($ext in $videoExtensions) {
    Remove-RegistryKeySafely "HKCU:\Software\Classes\SystemFileAssociations\$ext\shell\$legacyVerbKey"
}

# 2. SystemFileAssociations\video
$sfPath = "HKCU:\Software\Classes\SystemFileAssociations\video\shell\$verbKey"
New-Item -Path $sfPath -Force | Out-Null
Set-ItemProperty -Path $sfPath -Name "(Default)" -Value $menuText
Set-ItemProperty -Path $sfPath -Name "Icon" -Value $icon
New-Item -Path "$sfPath\command" -Force | Out-Null
Set-ItemProperty -Path "$sfPath\command" -Name "(Default)" -Value $command

# 3. Per-extension SystemFileAssociations
foreach ($ext in $videoExtensions) {
    $extPath = "HKCU:\Software\Classes\SystemFileAssociations\$ext\shell\$verbKey"
    New-Item -Path $extPath -Force | Out-Null
    Set-ItemProperty -Path $extPath -Name "(Default)" -Value $menuText
    Set-ItemProperty -Path $extPath -Name "Icon" -Value $icon
    New-Item -Path "$extPath\command" -Force | Out-Null
    Set-ItemProperty -Path "$extPath\command" -Name "(Default)" -Value $command
}

# 4. HKCU:\Software\Classes\*\shell with AppliesTo filter
$appliesTo = ($videoExtensions | ForEach-Object { "System.FileExtension:=$_" }) -join " OR "
$allFilesPath = "HKCU:\Software\Classes\*\shell\$verbKey"
New-Item -Path $allFilesPath -Force | Out-Null
Set-ItemProperty -Path $allFilesPath -Name "(Default)" -Value $menuText
Set-ItemProperty -Path $allFilesPath -Name "Icon" -Value $icon
Set-ItemProperty -Path $allFilesPath -Name "AppliesTo" -Value $appliesTo
New-Item -Path "$allFilesPath\command" -Force | Out-Null
Set-ItemProperty -Path "$allFilesPath\command" -Name "(Default)" -Value $command

# 5. HKCU:\Software\Classes\VidiStudio.Video ProgID
$progIdPath = "HKCU:\Software\Classes\VidiStudio.Video"
New-Item -Path $progIdPath -Force | Out-Null
Set-ItemProperty -Path $progIdPath -Name "(Default)" -Value "Video (Vidi Studio)"
Set-ItemProperty -Path $progIdPath -Name "FriendlyTypeName" -Value "Video (Vidi Studio)"
New-Item -Path "$progIdPath\DefaultIcon" -Force | Out-Null
Set-ItemProperty -Path "$progIdPath\DefaultIcon" -Name "(Default)" -Value $icon
New-Item -Path "$progIdPath\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path "$progIdPath\shell\open\command" -Name "(Default)" -Value $command
$progIdVerb = "$progIdPath\shell\$verbKey"
New-Item -Path $progIdVerb -Force | Out-Null
Set-ItemProperty -Path $progIdVerb -Name "(Default)" -Value $menuText
Set-ItemProperty -Path $progIdVerb -Name "Icon" -Value $icon
New-Item -Path "$progIdVerb\command" -Force | Out-Null
Set-ItemProperty -Path "$progIdVerb\command" -Name "(Default)" -Value $command

# 6. Applications key & Open with registration
$appPath = "HKCU:\Software\Classes\Applications\$exeName"
New-Item -Path $appPath -Force | Out-Null
Set-ItemProperty -Path $appPath -Name "FriendlyAppName" -Value $appName
Set-ItemProperty -Path $appPath -Name "Icon" -Value $icon
New-Item -Path "$appPath\SupportedTypes" -Force | Out-Null
foreach ($ext in $videoExtensions) {
    Set-ItemProperty -Path "$appPath\SupportedTypes" -Name $ext -Value ""
}
New-Item -Path "$appPath\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path "$appPath\shell\open\command" -Name "(Default)" -Value $command

$appVerb = "$appPath\shell\$verbKey"
New-Item -Path $appVerb -Force | Out-Null
Set-ItemProperty -Path $appVerb -Name "(Default)" -Value $menuText
Set-ItemProperty -Path $appVerb -Name "Icon" -Value $icon
New-Item -Path "$appVerb\command" -Force | Out-Null
Set-ItemProperty -Path "$appVerb\command" -Name "(Default)" -Value $command

foreach ($ext in $videoExtensions) {
    $owlPath = "HKCU:\Software\Classes\$ext\OpenWithList\$exeName"
    New-Item -Path $owlPath -Force | Out-Null
    $owpPath = "HKCU:\Software\Classes\$ext\OpenWithProgids"
    New-Item -Path $owpPath -Force | Out-Null
    Set-ItemProperty -Path $owpPath -Name "VidiStudio.Video" -Value ""
}

Notify-ShellChange
Write-Host "Successfully registered 'Describe with Vidi Studio' context menu and Open With handler." -ForegroundColor Green
