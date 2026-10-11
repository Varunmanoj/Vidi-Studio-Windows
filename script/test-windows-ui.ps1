param([ValidateSet('ARM64','x64')][string]$Platform = 'ARM64', [switch]$SkipBuild, [switch]$FrozenEngine, [string]$AppDirectory, [switch]$RequirePackage)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $projectRoot "build\ui-test-$Platform"
$appRoot = Join-Path $projectRoot ('build\smoke-' + $Platform.ToLowerInvariant())
if ($AppDirectory) { $appRoot = (Resolve-Path -LiteralPath $AppDirectory).Path; $SkipBuild = $true }
New-Item -ItemType Directory -Force $testRoot | Out-Null
if (!$SkipBuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    & $msbuild (Join-Path $projectRoot 'Vidi Studio Windows\Vidi Studio Windows.csproj') /restore /t:Build /p:Configuration=Debug /p:Platform=$Platform /p:WindowsPackageType=None /p:WindowsAppSDKSelfContained=true "/p:IntermediateOutputPath=obj\smoke-$Platform/" "/p:OutDir=$appRoot/" /verbosity:minimal
    if ($LASTEXITCODE -ne 0) { throw 'UI smoke build failed.' }
}
$folder = Join-Path $testRoot 'project'
New-Item -ItemType Directory -Force $folder | Out-Null
$project = @{
    schemaVersion=2; id='windows-ui-smoke'; name='Windows UI smoke fixture'; source='source.mp4'; duration=8; width=320; height=180; hasAudio=$true
    createdAt=0; updatedAt=0; thumbnails=@('thumbnail.jpg'); transcript=@(@{id='t1';start=0;end=1;text='Hello.'}); cues=@(@{id='a1';start=2;end=5;text='A blue screen appears.';origin='ai'})
    summary="# Smoke test`n`n- Native **Windows** summary.`n- Multilingual: नमस्ते"; exports=@(); status='Ready'
}
[System.IO.File]::WriteAllText((Join-Path $folder 'project.json'), ($project | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
& ffmpeg -y -v error -f lavfi -i 'color=c=blue:s=320x180:r=24' -f lavfi -i 'sine=frequency=440:sample_rate=48000' -t 8 -c:v libx264 -c:a aac (Join-Path $folder 'source.mp4')
if ($LASTEXITCODE -ne 0) { throw 'Test media creation failed.' }
& ffmpeg -y -v error -i (Join-Path $folder 'source.mp4') -frames:v 1 (Join-Path $folder 'thumbnail.jpg')
$configuration = @{
    pythonPath=(Join-Path $projectRoot '.venv-windows\Scripts\python.exe'); ttsMode='windows-core'; libraryPath=$testRoot
    ffmpegPath=(Get-Command ffmpeg).Source; ffprobePath=(Get-Command ffprobe).Source
}
if ($FrozenEngine) {
    if (!(Test-Path -LiteralPath (Join-Path $appRoot 'engine-bin\vidistudio-engine.exe'))) { throw 'Build the frozen engine with setup-windows.ps1 -FreezeEngine first.' }
    # A successful run proves that the app used its bundled engine, not source Python.
    $configuration.pythonPath = Join-Path $testRoot 'intentionally-missing-python.exe'
}
$configFile = Join-Path $testRoot 'config.json'
[System.IO.File]::WriteAllText($configFile, ($configuration | ConvertTo-Json), [System.Text.UTF8Encoding]::new($false))
$report = Join-Path $testRoot ('report-' + [Guid]::NewGuid() + '.txt')
$oldConfig = $env:VIDISTUDIO_CONFIG
try {
    $env:VIDISTUDIO_CONFIG = $configFile
    $env:VIDISTUDIO_UI_SMOKE_PROJECT = $folder
    $env:VIDISTUDIO_UI_SMOKE_REPORT = $report
    if ($RequirePackage) {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IVidiActivation {
    void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.LPWStr)] string args, uint options, out uint processId);
}
public static class VidiActivation {
    public static uint Launch(string id, string args) {
        var manager = (IVidiActivation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")));
        try { uint processId; manager.ActivateApplication(id, args, 2, out processId); return processId; }
        finally { Marshal.ReleaseComObject(manager); }
    }
}
"@
        $installed = Get-AppxPackage -Name '7b6f5f53-8ba2-429e-a747-0e2393ce9cee'
        if (!$installed) { throw 'Register the development package first.' }
        $payload = @{VIDISTUDIO_UI_SMOKE_REPORT=$report;VIDISTUDIO_UI_SMOKE_PROJECT=$folder;VIDISTUDIO_CONFIG=$configFile} | ConvertTo-Json -Compress
        $launchArgs = '--vidi-smoke ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($payload))
        $testProcessId = [VidiActivation]::Launch(($installed.PackageFamilyName + '!App'), $launchArgs)
        $app = Get-Process -Id $testProcessId
    } else {
        $app = Start-Process -FilePath (Join-Path $appRoot 'Vidi Studio Windows.exe') -WindowStyle Hidden -PassThru
    }
    if ($RequirePackage) {
        Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class VidiPackageIdentity {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetPackageFullName(IntPtr process, ref uint length, StringBuilder name);
}
"@
        [uint32]$length = 0
        $identityResult = [VidiPackageIdentity]::GetPackageFullName($app.Handle, [ref]$length, $null)
        if ($identityResult -ne 122) { $app.Kill(); throw "Process has no package identity (code $identityResult)." }
        $packageName = [Text.StringBuilder]::new([int]$length)
        $identityResult = [VidiPackageIdentity]::GetPackageFullName($app.Handle, [ref]$length, $packageName)
        if ($identityResult -ne 0 -or !$packageName.ToString().StartsWith('7b6f5f53-8ba2-429e-a747-0e2393ce9cee_')) { $app.Kill(); throw 'Unexpected package identity.' }
        Write-Output "Package identity: $packageName"
    }
    if (!$app.WaitForExit(120000)) { $app.Kill(); throw 'UI smoke test timed out.' }
    if (!(Test-Path -LiteralPath $report)) { throw "App exited with $($app.ExitCode) before creating a smoke report." }
    $result = Get-Content -Raw $report
    Write-Output $result
    if (!$result.StartsWith('PASS:')) { throw 'Native UI smoke test failed.' }
} finally {
    $env:VIDISTUDIO_CONFIG = $oldConfig
    Remove-Item Env:VIDISTUDIO_UI_SMOKE_PROJECT -ErrorAction SilentlyContinue
    Remove-Item Env:VIDISTUDIO_UI_SMOKE_REPORT -ErrorAction SilentlyContinue
}


