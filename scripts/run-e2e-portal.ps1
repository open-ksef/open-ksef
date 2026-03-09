#Requires -Version 7.0
<#
.SYNOPSIS
    Runs the Portal E2E tests against the running dev environment.
.DESCRIPTION
    Sets required environment variables (PORTAL_BASE_URL, Keycloak creds)
    and executes dotnet test on the Portal.E2E project.
    Requires the dev environment to be running (scripts/dev-env-up.ps1).
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:8080',
    [string]$Browser = 'chromium',
    [switch]$Headed
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Load test credentials from .env.test
$envTestFile = Join-Path $root '.env.test'
$testEnv = @{}
Get-Content $envTestFile | ForEach-Object {
    if ($_ -match '^\s*([^#][^=]+?)\s*=\s*(.+?)\s*$') {
        $testEnv[$Matches[1]] = $Matches[2]
    }
}

$env:PORTAL_BASE_URL = $BaseUrl
$env:PLAYWRIGHT_BROWSER = $Browser
$env:PLAYWRIGHT_HEADLESS = if ($Headed) { 'false' } else { 'true' }
$env:KEYCLOAK_USERNAME = $testEnv['E2E_TEST_USER']
$env:KEYCLOAK_PASSWORD = $testEnv['E2E_TEST_PASSWORD']
$env:E2E_TEST_KSEF_CERT_PASSWORD = $testEnv['E2E_TEST_KSEF_CERT_PASSWORD']

$pgUser = $testEnv['POSTGRES_USER']
$pgPass = $testEnv['POSTGRES_PASSWORD']
if ($pgUser -and $pgPass) {
    $env:TEST_DATABASE_CONNECTION_STRING = "Host=localhost;Port=5432;Database=openksef;Username=$pgUser;Password=$pgPass"
}

$project = Join-Path $root 'src/OpenKSeF.Portal.E2E/OpenKSeF.Portal.E2E.csproj'

Write-Host "[e2e] Running Portal E2E tests" -ForegroundColor Cyan
Write-Host "      Base URL : $BaseUrl"
Write-Host "      Browser  : $Browser"
Write-Host "      Headless : $(-not $Headed)"
Write-Host ""

dotnet test $project --nologo --verbosity normal

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n[e2e] All Portal E2E tests passed." -ForegroundColor Green
} else {
    Write-Host "`n[e2e] Some Portal E2E tests failed (exit code $LASTEXITCODE)." -ForegroundColor Red
}

exit $LASTEXITCODE
