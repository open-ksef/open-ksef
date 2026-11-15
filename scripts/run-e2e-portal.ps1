# Works with PowerShell 5.1+
<#
.SYNOPSIS
    Runs the Portal E2E (src/OpenKSeF.Portal.Web/e2e/portal.e2e.mjs) against the running dev environment.
.DESCRIPTION
    Hosted login, 2FA, register, logout, session expiry and axe (serious+) per page.
    Requires the dev environment (scripts/dev-env-up.ps1) and npm ci in the portal.
    E2E_TEST_USER from .env.test is the data user: enrolled in 2FA during the run and restored to password-only after.
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:8080',
    [string]$KeycloakUrl = 'http://localhost:8082/auth',
    [ValidateSet('chrome', 'msedge')]
    [string]$Browser = 'chrome',
    [string]$ShotDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$testEnv = @{}
Get-Content (Join-Path $root '.env.test') | ForEach-Object {
    if ($_ -match '^\s*([^#][^=]+?)\s*=\s*(.+?)\s*$') {
        $testEnv[$Matches[1]] = $Matches[2]
    }
}

$env:BASE_URL = $BaseUrl
$env:KC_URL = $KeycloakUrl
$env:KC_ADMIN_USER = if ($testEnv['KEYCLOAK_ADMIN']) { $testEnv['KEYCLOAK_ADMIN'] } else { 'admin' }
$env:KC_ADMIN_PASSWORD = if ($testEnv['KEYCLOAK_ADMIN_PASSWORD']) { $testEnv['KEYCLOAK_ADMIN_PASSWORD'] } else { 'admin' }
$env:DATA_USER = $testEnv['E2E_TEST_USER']
$env:DATA_PASSWORD = $testEnv['E2E_TEST_PASSWORD']
$env:BROWSER_CHANNEL = $Browser
if ($ShotDir) { $env:SHOT_DIR = $ShotDir }

Push-Location (Join-Path $root 'src/OpenKSeF.Portal.Web')
try {
    node e2e/portal.e2e.mjs
} finally {
    Pop-Location
}
exit $LASTEXITCODE
