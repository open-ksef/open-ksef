#Requires -Version 7.0
<#
.SYNOPSIS
    Shuts down the OpenKSeF dev environment and optionally removes volumes.
.PARAMETER RemoveVolumes
    Also remove Docker volumes (database data). Defaults to $false.
#>
[CmdletBinding()]
param(
    [switch]$RemoveVolumes
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $root 'docker-compose.dev.yml'

$downArgs = @('-f', $composeFile, 'down')
if ($RemoveVolumes) { $downArgs += '-v' }

Write-Host "[docker] Stopping services ..." -ForegroundColor Cyan
& docker compose @downArgs

if ($LASTEXITCODE -eq 0) {
    $msg = if ($RemoveVolumes) { "All services stopped and volumes removed." } else { "All services stopped. Volumes preserved." }
    Write-Host "[done] $msg" -ForegroundColor Green
} else {
    Write-Error "docker compose down failed with exit code $LASTEXITCODE"
}
