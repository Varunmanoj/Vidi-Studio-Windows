param(
    [Parameter(Mandatory)][string]$ManifestPath,
    [Parameter(Mandatory)][string]$PackageName,
    [Parameter(Mandatory)][string]$Publisher,
    [Parameter(Mandatory)][string]$PublisherDisplayName
)
$ErrorActionPreference = 'Stop'
$path = (Resolve-Path -LiteralPath $ManifestPath).ProviderPath
[xml]$manifest = Get-Content -LiteralPath $path -Raw
if ($manifest.Package.Identity.Publisher -ne 'CN=personal' -or $manifest.Package.Properties.DisplayName -ne 'Vidi Studio') {
    throw 'Expected the Vidi Studio development manifest from a staged source copy.'
}

$manifest.Package.Identity.SetAttribute('Name', $PackageName)
$manifest.Package.Identity.SetAttribute('Publisher', $Publisher)
$manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName

# The Store build uses manifest registrations and the browser-store extension.
# Let Windows isolate per-user registry and AppData writes using its defaults.
$developerEntries = $manifest.SelectNodes("//*[local-name()='PhoneIdentity' or local-name()='RegistryWriteVirtualization' or local-name()='FileSystemWriteVirtualization' or (local-name()='Capability' and @Name='unvirtualizedResources')]")
foreach ($entry in $developerEntries) {
    $null = $entry.ParentNode.RemoveChild($entry)
}
$manifest.Save($path)
