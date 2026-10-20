param(
    [Parameter(Mandatory=$true)][string]$NodeExe,
    [Parameter(Mandatory=$true)][string]$NodeLicense,
    [string]$NugetPackages,
    [Parameter(Mandatory=$true)][string]$OutputZip
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root 'plugin\bin\AddIn 2026 Release R26'
$node = (Resolve-Path -LiteralPath $NodeExe).Path
$license = (Resolve-Path -LiteralPath $NodeLicense).Path
$output = [IO.Path]::GetFullPath($OutputZip)
if (Test-Path -LiteralPath $output) { throw 'Output already exists; choose a new package filename.' }
foreach ($file in @((Join-Path $build 'revit_mcp_plugin\RevitMCPPlugin.dll'), (Join-Path $build 'revit_mcp_plugin\Commands\RevitMCPCommandSet\2026\RevitMCPCommandSet.dll'), (Join-Path $root 'server\build\index.js'), (Join-Path $root 'server\build\sql-wasm.wasm'))) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Build the plugin, command set and server first; missing $file" }
}
$stage = Join-Path $root ('artifacts\package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item -Path (Join-Path $build '*') -Destination $stage -Recurse
$server = Join-Path $stage 'revit_mcp_plugin\Commands\RevitMCPCommandSet\server'
New-Item -ItemType Directory -Path (Join-Path $server 'runtime') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'server\build') -Destination $server -Recurse
Copy-Item -LiteralPath (Join-Path $root 'server\package.json') -Destination $server
Copy-Item -LiteralPath $node -Destination (Join-Path $server 'runtime\node.exe')
Copy-Item -LiteralPath $license -Destination (Join-Path $server 'runtime\NODE-LICENSE')
Copy-Item -LiteralPath (Join-Path $root 'tool-schemas.txt') -Destination (Join-Path $stage 'revit_mcp_plugin\Commands\RevitMCPCommandSet')
Copy-Item -LiteralPath $PSScriptRoot -Destination $stage -Recurse
foreach ($file in @('LICENSE','NOTICE.md','README.md')) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $stage }
$notices = Join-Path $stage 'dependency-notices'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
# Retain dependency metadata and notices for the bundled JS and C# distribution.
# The NuGet cache may also contain compile-only packages; no cached binaries are copied.
if ($NugetPackages) {
    foreach ($package in (Get-ChildItem -LiteralPath $NugetPackages -Directory)) {
        foreach ($version in (Get-ChildItem -LiteralPath $package.FullName -Directory)) {
            $files = Get-ChildItem -LiteralPath $version.FullName -Recurse -File | Where-Object { $_.Name -match '(?i)^(licen[sc]e|notice|third.party)' -or $_.Extension -eq '.nuspec' }
            foreach ($file in $files) {
                $dest = Join-Path $notices (Join-Path ($package.Name + '-' + $version.Name) $file.FullName.Substring($version.FullName.Length + 1))
                New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination $dest
            }
        }
    }
}
$modules = Join-Path $root 'server\node_modules'
if (Test-Path -LiteralPath $modules) {
    $files = Get-ChildItem -LiteralPath $modules -Recurse -File -Force | Where-Object { $_.Name -match '(?i)^(licen[sc]e|notice|third.party)' }
    foreach ($file in $files) {
        $dest = Join-Path $notices (Join-Path 'javascript' $file.FullName.Substring($modules.Length + 1))
        New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $dest
    }
}
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $stage 'revit_mcp_plugin\RevitMCPPlugin.dll')).FileVersion
$commit = (& git -C $root rev-parse HEAD).Trim()
@{ product='VUE Studio'; version=$version; revit='2026'; sourceCommit=$commit; sourceDirty=[bool](& git -C $root status --porcelain); builtAtUtc=[DateTime]::UtcNow.ToString('o'); nodeVersion=(& $node --version).Trim() } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'package-info.json') -Encoding UTF8
New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $output
Get-FileHash -LiteralPath $output -Algorithm SHA256 | Format-List
Write-Output "Package: $output"
Write-Output "Staged: $stage"
