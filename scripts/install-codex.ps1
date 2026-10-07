param(
    [Parameter(Mandatory=$true)][string]$ReleaseZip,
    [Parameter(Mandatory=$true)][string]$CodexExe
)
$ErrorActionPreference = 'Stop'
$expectedHash = '2c46ad557b69ac3b36fade076b16e36d7bbb7b1108a2d273eed60a5f2426749b'
if ((Get-FileHash -LiteralPath $ReleaseZip -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'Expected the official v2.2.1 Revit2026 ZIP; SHA256 does not match.'
}
if (Get-Process -Name Revit -ErrorAction SilentlyContinue) { throw 'Close Revit before installing.' }
if (!(Test-Path -LiteralPath $CodexExe -PathType Leaf)) { throw 'Codex executable not found.' }
$addinDir = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2026'
$pluginDir = Join-Path $addinDir 'revit_mcp_plugin'
$manifest = Join-Path $addinDir 'mcp-servers-for-revit.addin'
if ((Test-Path -LiteralPath $pluginDir) -or (Test-Path -LiteralPath $manifest)) {
    throw 'An installation already exists. This script intentionally does not replace it.'
}
$stage = Join-Path ([IO.Path]::GetDirectoryName((Resolve-Path -LiteralPath $ReleaseZip).Path)) 'codex-install-stage'
if (Test-Path -LiteralPath $stage) { throw 'Staging directory already exists.' }
Expand-Archive -LiteralPath $ReleaseZip -DestinationPath $stage
$stagedPlugin = Join-Path $stage 'revit_mcp_plugin'
$serverRelative = 'Commands\RevitMCPCommandSet\server'
foreach ($relative in @('RevitMCPPlugin.dll', "$serverRelative\build\index.js", "$serverRelative\runtime\node.exe", 'Commands\RevitMCPCommandSet\2026\RevitMCPCommandSet.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $stagedPlugin $relative))) { throw "Release file missing: $relative" }
}
New-Item -ItemType Directory -Force -Path $addinDir | Out-Null
Copy-Item -LiteralPath $stagedPlugin -Destination $pluginDir -Recurse
Copy-Item -LiteralPath (Join-Path $stage 'mcp-servers-for-revit.addin') -Destination $manifest
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\LICENSE') -Destination (Join-Path $pluginDir 'LICENSE')
$serverDir = Join-Path $pluginDir $serverRelative
$node = Join-Path $serverDir 'runtime\node.exe'
$entry = Join-Path $serverDir 'build\index.js'
& $CodexExe mcp add revit-mcp -- $node $entry
if ($LASTEXITCODE -ne 0) { throw 'Plugin installed, but MCP registration failed. Inspect Codex configuration.' }
Write-Output 'Revit 2026 plugin installed and revit-mcp registered in Codex.'
Write-Output 'Open Revit, open a document, and enable Revit MCP Switch under Add-Ins.'
