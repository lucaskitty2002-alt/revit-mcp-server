param([string]$TestRoot = (Join-Path $env:TEMP ('revit-installer-test-' + [guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
$installer = (Resolve-Path "$PSScriptRoot\..\..\scripts\install.ps1").Path
$previousAppData = $env:APPDATA
$previousLocal = $env:LOCALAPPDATA
try {
    $env:APPDATA = Join-Path $TestRoot 'Roaming'
    $env:LOCALAPPDATA = Join-Path $TestRoot 'Local'
    $package = Join-Path $TestRoot 'package'
    $plugin = Join-Path $package 'revit_mcp_plugin'
    foreach ($relative in @('RevitMCPPlugin.dll','RevitMCPSDK.dll','Newtonsoft.Json.dll','tool_schemas.json','Commands\RevitMCPCommandSet\2026\RevitMCPCommandSet.dll','Commands\RevitMCPCommandSet\server\runtime\node.exe','Commands\RevitMCPCommandSet\server\build\index.js')) {
        $file = Join-Path $plugin $relative
        New-Item -ItemType Directory -Path (Split-Path $file) -Force | Out-Null
        Set-Content -LiteralPath $file -Value 'test package content'
    }
    Copy-Item -LiteralPath "$PSScriptRoot\..\..\plugin\bin\Release\2026\RevitMCPPlugin.dll" -Destination "$plugin\RevitMCPPlugin.dll" -Force
    Copy-Item -LiteralPath "$PSScriptRoot\..\..\plugin\mcp-servers-for-revit.addin" -Destination $package
    & $installer -LocalZip $package -SkipMcpConfig
    $target = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2026'
    $installed = Join-Path $target 'revit_mcp_plugin'
    if (!(Test-Path -LiteralPath "$installed\RevitMCPPlugin.dll")) { throw 'Fresh install failed' }
    Set-Content -LiteralPath "$target\other.addin" -Value 'preserve me'
    Set-Content -LiteralPath "$installed\old-file.txt" -Value 'old version'
    $rejected = $false
    try { & $installer -LocalZip $package -SkipMcpConfig } catch { $rejected = $true }
    if (!$rejected) { throw 'Existing install was overwritten without Force' }
    & $installer -LocalZip $package -Force -SkipMcpConfig
    if (Test-Path -LiteralPath "$installed\old-file.txt") { throw 'Stale files remained after upgrade' }
    $backups = Get-ChildItem -LiteralPath "$env:LOCALAPPDATA\RevitMCP\Backups" -Directory
    if (!(Test-Path -LiteralPath (Join-Path $backups[0].FullName 'revit_mcp_plugin\old-file.txt'))) { throw 'Backup did not preserve old install' }
    Remove-Item -LiteralPath "$plugin\RevitMCPPlugin.dll"
    $rejected = $false
    try { & $installer -LocalZip $package -Force -SkipMcpConfig } catch { $rejected = $true }
    if (!$rejected -or !(Test-Path -LiteralPath "$installed\RevitMCPPlugin.dll")) { throw 'Invalid package damaged the existing install' }
    if ((Get-Content -LiteralPath "$target\other.addin" -Raw).Trim() -ne 'preserve me') { throw 'Other add-in was modified' }
    Write-Output 'PASS: fresh install, explicit replacement, backup, stale-file removal, invalid-package rejection, and preservation of other add-ins.'
} finally {
    $env:APPDATA = $previousAppData
    $env:LOCALAPPDATA = $previousLocal
    # TestRoot is deliberately retained for inspection; no recursive cleanup of caller-supplied paths.
}
