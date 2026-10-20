param([string]$CodexExe, [string]$RevitVersion = '2026')
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"
$plugin = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion\$PLUGIN_FOLDER"
foreach ($relative in @('RevitMCPPlugin.dll','tool_schemas.json',"Commands\RevitMCPCommandSet\$RevitVersion\RevitMCPCommandSet.dll",'Commands\RevitMCPCommandSet\server\runtime\node.exe','Commands\RevitMCPCommandSet\server\build\index.js')) {
    $found = Test-Path -LiteralPath (Join-Path $plugin $relative)
    Write-Output "$relative : $found"
}
$exe = Get-CodexPath $CodexExe
if (!$exe) { throw 'Codex executable not found.' }
& $exe login status
& $exe mcp get revit-mcp
Write-Output 'Open a model and enable Revit MCP Switch. Use Verify Connection in Codex Chat.'
