#Requires -Version 5.1
<#
.SYNOPSIS
Install VUE Studio for Revit from this fork's release or a local package.
.PARAMETER LocalZip
Path to a package ZIP or its extracted directory.
.PARAMETER Force
Replace this plugin after a dated backup. Other add-ins are preserved.
#>
param(
    [ValidateSet('2023','2024','2025','2026','2027')][string]$RevitVersion = '2026',
    [string]$LocalZip, [string]$Tag = 'latest', [string]$CodexExe,
    [switch]$Force, [switch]$Uninstall, [switch]$SkipMcpConfig
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"
if (Get-Process -Name Revit -ErrorAction SilentlyContinue) { throw 'Close Revit before installing or uninstalling.' }
$addins = [IO.Path]::GetFullPath((Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"))
$plugin = [IO.Path]::GetFullPath((Join-Path $addins $PLUGIN_FOLDER))
$manifest = Join-Path $addins $ADDIN_FILE
$backup = $null
if ([IO.Path]::GetDirectoryName($plugin) -ne $addins) { throw 'Invalid add-in target.' }
$exists = (Test-Path -LiteralPath $plugin) -or (Test-Path -LiteralPath $manifest)
if ($exists -and !$Force) { throw 'Installation exists. Use -Force to back up and replace this plugin.' }
if (!$Uninstall) {
    if (!$LocalZip) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $endpoint = if ($Tag -eq 'latest') { 'latest' } else { "tags/$Tag" }
        $release = Invoke-RestMethod "https://api.github.com/repos/$REPO/releases/$endpoint"
        $asset = $release.assets | Where-Object name -like "*-Revit$RevitVersion.zip" | Select-Object -First 1
        if (!$asset) { throw "No Codex release for Revit $RevitVersion. Build from source or provide -LocalZip." }
        $download = Join-Path $env:TEMP ('revit-codex-' + [guid]::NewGuid().ToString('N') + '.zip')
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $download -UseBasicParsing
        if ((Get-Item -LiteralPath $download).Length -ne $asset.size) { throw 'Incomplete release download.' }
        $LocalZip = $download
    }
    $source = (Resolve-Path -LiteralPath $LocalZip).Path
    if (!(Test-Path -LiteralPath $source -PathType Container)) {
        $source = Join-Path $env:TEMP ('revit-codex-' + [guid]::NewGuid().ToString('N'))
        Expand-Archive -LiteralPath $LocalZip -DestinationPath $source
    }
    $stagedPlugin = Join-Path $source $PLUGIN_FOLDER
    if ([IO.Path]::GetFullPath($stagedPlugin).StartsWith($plugin + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFullPath($stagedPlugin).Equals($plugin, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The installation source must be outside the installed plugin directory.'
    }
    foreach ($relative in @('RevitMCPPlugin.dll','RevitMCPSDK.dll','Newtonsoft.Json.dll','tool_schemas.json',"Commands\RevitMCPCommandSet\$RevitVersion\RevitMCPCommandSet.dll",'Commands\RevitMCPCommandSet\server\runtime\node.exe','Commands\RevitMCPCommandSet\server\build\index.js')) {
        if (!(Test-Path -LiteralPath (Join-Path $stagedPlugin $relative) -PathType Leaf)) { throw "Invalid package; missing $relative" }
    }
    $pluginVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $stagedPlugin 'RevitMCPPlugin.dll')).FileVersion
    if (!$pluginVersion -or [version]$pluginVersion -lt [version]'2.3.0.0') { throw 'This package predates the Codex chat integration. Use a Codex for Revit release from this fork.' }
    $stagedManifest = Join-Path $source $ADDIN_FILE
    if (!(Test-Path -LiteralPath $stagedManifest)) { throw 'Package manifest missing.' }
    [xml]$xml = Get-Content -LiteralPath $stagedManifest
    if ($xml.RevitAddIns.AddIn.FullClassName -ne 'revit_mcp_plugin.Core.Application') { throw 'Unexpected package manifest.' }
    if ($xml.RevitAddIns.AddIn.Assembly.Replace('\','/') -ne 'revit_mcp_plugin/RevitMCPPlugin.dll') { throw 'Unexpected plugin assembly path.' }
}
# Validate package and targets before touching the previous installation.
$installedNode = Join-Path $plugin 'Commands\RevitMCPCommandSet\server\runtime\node.exe'
$installedEntry = Join-Path $plugin 'Commands\RevitMCPCommandSet\server\build\index.js'
# Codex may keep the bundled MCP process running after Revit closes.
# Stop only this installation's Node executable running this exact server entry.
$serverProcesses = Get-CimInstance Win32_Process -Filter "Name='node.exe'" | Where-Object {
    $_.ExecutablePath -eq $installedNode -and $_.CommandLine -and $_.CommandLine.IndexOf($installedEntry, [StringComparison]::OrdinalIgnoreCase) -ge 0
}
foreach ($serverProcess in $serverProcesses) {
    Stop-Process -Id $serverProcess.ProcessId -ErrorAction Stop
    Wait-Process -Id $serverProcess.ProcessId -Timeout 5 -ErrorAction SilentlyContinue
    Write-Output "Stopped this installation's MCP server ($($serverProcess.ProcessId)); Codex can reconnect after installation."
}
if ($exists) {
    $backup = Join-Path $env:LOCALAPPDATA ('RevitMCP\Backups\' + $RevitVersion + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    if (Test-Path -LiteralPath $plugin) { Copy-Item -LiteralPath $plugin -Destination $backup -Recurse }
    if (Test-Path -LiteralPath $manifest) { Copy-Item -LiteralPath $manifest -Destination $backup }
    Write-Output "Previous installation backed up to $backup"
}
if ($Uninstall) {
    if (Test-Path -LiteralPath $manifest) { Remove-Item -LiteralPath $manifest }
    if (Test-Path -LiteralPath $plugin) { Remove-Item -LiteralPath $plugin -Recurse }
    Write-Output 'This Revit add-in was removed. Codex and other add-ins were preserved.'
    exit 0
}
New-Item -ItemType Directory -Path $addins -Force | Out-Null
try {
    if (Test-Path -LiteralPath $manifest) { Remove-Item -LiteralPath $manifest }
    if (Test-Path -LiteralPath $plugin) { Remove-Item -LiteralPath $plugin -Recurse }
    Copy-Item -LiteralPath $stagedPlugin -Destination $plugin -Recurse
    Get-ChildItem -LiteralPath $plugin -Recurse -File | Unblock-File
    Copy-Item -LiteralPath $stagedManifest -Destination $manifest
} catch {
    $installationError = $_
    if ($backup) {
        # Copy over a partial install so a locked file cannot prevent restoration of the manifest.
        $oldPlugin = Join-Path $backup $PLUGIN_FOLDER
        $oldManifest = Join-Path $backup $ADDIN_FILE
        if (Test-Path -LiteralPath $oldManifest) { Copy-Item -LiteralPath $oldManifest -Destination $manifest -Force }
        if (Test-Path -LiteralPath $oldPlugin) {
            New-Item -ItemType Directory -Path $plugin -Force | Out-Null
            Get-ChildItem -LiteralPath $oldPlugin | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $plugin -Recurse -Force -ErrorAction Continue }
        }
    }
    throw $installationError
}
if (!$SkipMcpConfig) { Register-CodexRevit -PluginDirectory $plugin -CodexExe $CodexExe }
Write-Output "VUE Studio for Revit $RevitVersion installed. Open Revit > VUE Studio > MCP slukket to enable the connection, then choose Aabn Studio. MCP taendt means the local listener is running; it does not confirm a Codex client connection."
