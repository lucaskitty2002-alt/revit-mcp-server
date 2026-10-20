#Requires -Version 5.1
$script:REPO = 'lucaskitty2002-alt/revit-mcp-server'
$script:PLUGIN_FOLDER = 'revit_mcp_plugin'
$script:ADDIN_FILE = 'mcp-servers-for-revit.addin'
function Get-CodexPath {
    param([string]$ExplicitPath)
    if ($ExplicitPath) {
        if (!(Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) { throw 'Codex executable not found.' }
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }
    $command = Get-Command codex.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    foreach ($bin in @((Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin'), (Join-Path $env:APPDATA 'npm\node_modules\@openai\codex'))) {
        if (Test-Path -LiteralPath $bin) {
            $exe = Get-ChildItem -LiteralPath $bin -Filter codex.exe -Recurse -File | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
            if ($exe) { return $exe.FullName }
        }
    }
    return $null
}
function Register-CodexRevit {
    param([string]$PluginDirectory, [string]$CodexExe)
    $exe = Get-CodexPath $CodexExe
    if (!$exe) { throw 'Install Codex and sign in, then run scripts/fix-mcp.ps1.' }
    $server = Join-Path $PluginDirectory 'Commands\RevitMCPCommandSet\server'
    $node = Join-Path $server 'runtime\node.exe'
    $entry = Join-Path $server 'build\index.js'
    foreach ($file in @($node,$entry)) { if (!(Test-Path -LiteralPath $file)) { throw "Missing server file: $file" } }
    & $exe mcp add revit-mcp -- $node $entry
    if ($LASTEXITCODE -ne 0) { throw 'Codex MCP registration failed.' }
}
