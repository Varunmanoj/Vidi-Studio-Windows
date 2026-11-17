param([Parameter(Mandatory=$true)][string]$VideoId, [Parameter(Mandatory=$true)][string]$ExpectedTitle)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
if (!('VidiActivation' -as [type])) {
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
}

$appId=(Get-AppxPackage -Name '7b6f5f53-8ba2-429e-a747-0e2393ce9cee').PackageFamilyName + '!App'

$ringCondition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty,'shared-youtube-opening')
$descendants = [Windows.Automation.TreeScope]::Descendants
$url = 'vidistudio://share?kind=youtube&source=' + [Uri]::EscapeDataString('https://www.youtube.com/watch?v=' + $VideoId)
[VidiActivation]::Launch($appId, $url) | Out-Null
$sawRing = $false
$timer = [Diagnostics.Stopwatch]::StartNew()
do {
    foreach ($process in (Get-Process -Name 'Vidi Studio Windows' -ErrorAction SilentlyContinue)) {
        $handle = $process.MainWindowHandle
        if ($handle -eq [IntPtr]::Zero) { continue }
        try {
            $window = [Windows.Automation.AutomationElement]::FromHandle($handle)
            $ring = $window.FindFirst($descendants, $ringCondition)
            if ($ring -and !$ring.Current.IsOffscreen) {
                if (!$sawRing) { Write-Output ("Observed " + $ring.Current.Name) }
                $sawRing = $true
            } elseif ($sawRing -and $window.Current.Name.Contains($ExpectedTitle)) {
                Write-Output ("PASS indicator cleared with editor open: " + $window.Current.Name)
                return
            }
        } catch [Windows.Automation.ElementNotAvailableException] { continue }
    }
    Start-Sleep -Milliseconds 25
} while ($timer.Elapsed.TotalSeconds -lt 60)
throw "Expected spinner and editor for $ExpectedTitle. Observed spinner: $sawRing"
