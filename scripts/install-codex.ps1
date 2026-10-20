param(
    [Parameter(Mandatory=$true)][string]$ReleaseZip,
    [string]$CodexExe, [switch]$Force
)
& "$PSScriptRoot\install.ps1" -RevitVersion 2026 -LocalZip $ReleaseZip -CodexExe $CodexExe -Force:$Force
