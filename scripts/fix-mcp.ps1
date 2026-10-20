param([string]$CodexExe, [string]$RevitVersion = '2026')
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"
$plugin = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion\$PLUGIN_FOLDER"
Register-CodexRevit -PluginDirectory $plugin -CodexExe $CodexExe
Write-Output 'Codex MCP connection registered. Start a new Codex chat to load it.'
