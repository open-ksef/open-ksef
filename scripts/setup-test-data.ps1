<#
.SYNOPSIS
    Provisions test company data via the OpenKSeF API.
.DESCRIPTION
    1. Gets an OIDC token for the test user (via Keycloak direct access grant)
    2. Creates a tenant with the test NIP if it doesn't exist
    3. Adds KSeF credentials (token) to the tenant if missing
    4. Triggers invoice sync
    Designed to be called by dev-env-up.ps1 or run standalone.
.PARAMETER ApiBaseUrl
    Base URL for the API (default: http://localhost:8081).
.PARAMETER KeycloakUrl
    Keycloak base URL (default: http://localhost:8082).
.PARAMETER SkipSync
    Skip the sync step.
#>
[CmdletBinding()]
param(
    [string]$ApiBaseUrl   = 'http://localhost:8081',
    [string]$KeycloakUrl  = 'http://localhost:8080',
    [switch]$SkipSync
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot

# ── Read env ─────────────────────────────────────────────────────────
$envTestFile = Join-Path $root '.env.test'
$envVars = @{}
Get-Content $envTestFile | ForEach-Object {
    if ($_ -match '^\s*([^#][^=]+?)\s*=\s*(.+?)\s*$') {
        $envVars[$Matches[1]] = $Matches[2]
    }
}

$testUser      = $envVars['E2E_TEST_USER']
$testPass      = $envVars['E2E_TEST_PASSWORD']
$testNip       = $envVars['E2E_TEST_NIP']
$testKsefToken = $envVars['E2E_TEST_KSEF_TOKEN']

if (-not $testNip -or -not $testKsefToken) {
    Write-Warning "E2E_TEST_NIP or E2E_TEST_KSEF_TOKEN not set in .env.test -- skipping test data provisioning"
    return
}

Write-Host "`n[test-data] Provisioning test company (NIP: $testNip) ...`n" -ForegroundColor Cyan

# ── 1. Get user token ────────────────────────────────────────────────
# Uses openksef-mobile client which has directAccessGrantsEnabled=true
Write-Host "  [auth] Getting token for '$testUser' ..." -ForegroundColor DarkGray
$tokenBody = @{
    client_id  = 'openksef-mobile'
    username   = $testUser
    password   = $testPass
    grant_type = 'password'
}
try {
    $tokenResp = Invoke-RestMethod `
        -Uri "$KeycloakUrl/auth/realms/openksef/protocol/openid-connect/token" `
        -Method Post -Body $tokenBody -ContentType 'application/x-www-form-urlencoded'
} catch {
    Write-Warning "Failed to get user token. Is the test user created? Run dev-env-up.ps1 first."
    Write-Warning "Error: $_"
    return
}
$bearerToken = $tokenResp.access_token
$authHeaders = @{
    Authorization  = "Bearer $bearerToken"
    'Content-Type' = 'application/json'
}

# ── 2. Ensure tenant exists ──────────────────────────────────────────
Write-Host "  [tenant] Checking for NIP $testNip ..." -ForegroundColor DarkGray
$tenants = Invoke-RestMethod -Uri "$ApiBaseUrl/api/tenants" -Headers $authHeaders -Method Get

$tenant = $tenants | Where-Object { $_.nip -eq $testNip } | Select-Object -First 1

if ($tenant) {
    Write-Host "  [skip] Tenant already exists: $($tenant.displayName) (id=$($tenant.id))" -ForegroundColor Yellow
} else {
    Write-Host "  [create] Creating tenant for NIP $testNip ..." -ForegroundColor DarkGray
    $createBody = @{
        nip         = $testNip
        displayName = "Firma testowa $testNip"
    } | ConvertTo-Json

    $tenant = Invoke-RestMethod -Uri "$ApiBaseUrl/api/tenants" -Headers $authHeaders -Method Post -Body $createBody
    Write-Host "  [OK] Tenant created: $($tenant.displayName) (id=$($tenant.id))" -ForegroundColor Green
}

$tenantId = $tenant.id

# ── 3. Ensure KSeF credentials exist ────────────────────────────────
Write-Host "  [credential] Checking KSeF credentials ..." -ForegroundColor DarkGray
try {
    $credStatus = Invoke-RestMethod -Uri "$ApiBaseUrl/api/tenants/$tenantId/credentials/status" `
        -Headers $authHeaders -Method Get
    $hasCred = $credStatus.exists
} catch {
    $hasCred = $false
}

if ($hasCred) {
    Write-Host "  [skip] KSeF credentials already exist" -ForegroundColor Yellow
} else {
    Write-Host "  [create] Adding KSeF token ..." -ForegroundColor DarkGray
    $credBody = @{ token = $testKsefToken } | ConvertTo-Json

    Invoke-RestMethod -Uri "$ApiBaseUrl/api/tenants/$tenantId/credentials" `
        -Headers $authHeaders -Method Post -Body $credBody | Out-Null
    Write-Host "  [OK] KSeF credentials added" -ForegroundColor Green
}

# ── 4. Trigger sync ──────────────────────────────────────────────────
# KSeF TEST allows 20 metadata queries/hour; skip if the worker (or a previous run) synced within the hour.
$summary = Invoke-RestMethod -Uri "$ApiBaseUrl/api/dashboard" -Headers $authHeaders -Method Get |
    Where-Object { $_.tenantId -eq $tenantId }
$recentSync = $summary.lastSuccessfulSync -and ([datetime]$summary.lastSuccessfulSync).ToUniversalTime() -gt (Get-Date).ToUniversalTime().AddHours(-1)
$syncFailed = $false

if ($SkipSync) {
    Write-Host "  [skip] Sync skipped (-SkipSync)" -ForegroundColor Yellow
} elseif ($recentSync) {
    Write-Host "  [skip] Tenant synced at $($summary.lastSuccessfulSync) UTC (within the last hour)" -ForegroundColor Yellow
} else {
    Write-Host "  [sync] Triggering manual sync ..." -ForegroundColor DarkGray
    try {
        $syncResult = Invoke-RestMethod -Uri "$ApiBaseUrl/api/tenants/$tenantId/credentials/sync" `
            -Headers $authHeaders -Method Post
        Write-Host "  [OK] Sync complete: fetched=$($syncResult.fetchedInvoices), new=$($syncResult.newInvoices)" -ForegroundColor Green
    } catch {
        $syncFailed = $true
        $detail = if ($_.ErrorDetails) { $_.ErrorDetails.Message } else { "$_" }
        Write-Warning "Sync failed: $detail"
        if ($detail -match '429|limit') {
            Write-Warning "KSeF TEST rate limit (20 metadata queries/hour). Wait up to 1 hour, then re-run: powershell -File scripts/setup-test-data.ps1"
        }
    }
}

$invoiceCount = (Invoke-RestMethod -Uri "$ApiBaseUrl/api/dashboard" -Headers $authHeaders -Method Get |
    Where-Object { $_.tenantId -eq $tenantId }).totalInvoices

if ($syncFailed -or $invoiceCount -eq 0) {
    Write-Host "`n[test-data] PARTIAL: tenant '$testNip' provisioned, $invoiceCount invoices. Sync did not complete.`n" -ForegroundColor Yellow
    exit 1
}

Write-Host "`n[test-data] Done. Tenant '$testNip' is ready for testing ($invoiceCount invoices).`n" -ForegroundColor Green
