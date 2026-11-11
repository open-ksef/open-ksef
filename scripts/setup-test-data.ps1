<#
.SYNOPSIS
    Provisions test company data via the OpenKSeF API.
.DESCRIPTION
    1. Gets an OIDC token for the test user (via Keycloak direct access grant)
    2. Creates a tenant with the test NIP if it doesn't exist
       (plus a second one from KSEF_TEST_NIP_2 / KSEF_TEST_TOKEN_2 when both are set)
    3. Adds KSeF credentials (token) to each tenant if missing
    4. Triggers invoice sync per tenant
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
$testNip2      = $envVars['KSEF_TEST_NIP_2']
$testKsefToken2 = $envVars['KSEF_TEST_TOKEN_2']

if (-not $testNip -or -not $testKsefToken) {
    Write-Warning "E2E_TEST_NIP or E2E_TEST_KSEF_TOKEN not set in .env.test -- skipping test data provisioning"
    return
}

$companies = @(@{ Nip = $testNip; Token = $testKsefToken })
if ($testNip2 -and $testKsefToken2) {
    $companies += @{ Nip = $testNip2; Token = $testKsefToken2 }
} else {
    Write-Host "[test-data] KSEF_TEST_NIP_2 or KSEF_TEST_TOKEN_2 not set -- second tenant skipped" -ForegroundColor DarkGray
}

Write-Host "`n[test-data] Provisioning $($companies.Count) test companies ...`n" -ForegroundColor Cyan

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
    exit 1
}
$bearerToken = $tokenResp.access_token
$authHeaders = @{
    Authorization  = "Bearer $bearerToken"
    'Content-Type' = 'application/json'
}

# KSeF TEST allows 20 metadata queries/hour per NIP; a tenant synced within the hour (worker or previous run) is not synced again.
function Initialize-TestCompany([string]$nip, [string]$ksefToken) {
    Write-Host "  [tenant] Checking for NIP $nip ..." -ForegroundColor DarkGray
    $tenants = Invoke-RestMethod -Uri "$ApiBaseUrl/api/tenants" -Headers $authHeaders -Method Get
    $tenant = $tenants | Where-Object { $_.nip -eq $nip } | Select-Object -First 1

    if ($tenant) {
        Write-Host "  [skip] Tenant already exists: $($tenant.displayName) (id=$($tenant.id))" -ForegroundColor Yellow
    } else {
        Write-Host "  [create] Creating tenant for NIP $nip ..." -ForegroundColor DarkGray
        $createBody = @{
            nip         = $nip
            displayName = "Firma testowa $nip"
        } | ConvertTo-Json

        $tenant = Invoke-RestMethod -Uri "$ApiBaseUrl/api/tenants" -Headers $authHeaders -Method Post -Body $createBody
        Write-Host "  [OK] Tenant created: $($tenant.displayName) (id=$($tenant.id))" -ForegroundColor Green
    }

    $tenantId = $tenant.id

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
        $credBody = @{ token = $ksefToken } | ConvertTo-Json

        Invoke-RestMethod -Uri "$ApiBaseUrl/api/tenants/$tenantId/credentials" `
            -Headers $authHeaders -Method Post -Body $credBody | Out-Null
        Write-Host "  [OK] KSeF credentials added" -ForegroundColor Green
    }

    $summary = (Invoke-RestMethod -Uri "$ApiBaseUrl/api/dashboard" -Headers $authHeaders -Method Get) |
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
            $response = if ($_.Exception.PSObject.Properties['Response']) { $_.Exception.Response }
            $status = if ($response) { [int]$response.StatusCode } else { 0 }
            $detail = if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $_.ErrorDetails.Message }
                elseif ($response -is [System.Net.WebResponse]) { (New-Object System.IO.StreamReader($response.GetResponseStream())).ReadToEnd() }
                else { "$_" }
            Write-Warning "Sync failed (HTTP $status): $detail"
            if ($status -in 429, 502 -or $detail -match '429|limit') {
                Write-Warning "Possible KSeF TEST rate limit (20 metadata queries/hour). Wait up to 1 hour, then re-run: powershell -File scripts/setup-test-data.ps1"
            }
        }
    }

    $invoiceCount = ((Invoke-RestMethod -Uri "$ApiBaseUrl/api/dashboard" -Headers $authHeaders -Method Get) |
        Where-Object { $_.tenantId -eq $tenantId }).totalInvoices

    if ($syncFailed -or -not $invoiceCount) {
        Write-Host "  [PARTIAL] Tenant '$nip' provisioned, $invoiceCount invoices. Sync did not complete." -ForegroundColor Yellow
        return $false
    }
    Write-Host "  [OK] Tenant '$nip' is ready ($invoiceCount invoices)." -ForegroundColor Green
    return $true
}

$failed = @($companies | Where-Object { -not (Initialize-TestCompany $_.Nip $_.Token) })

if ($failed.Count -gt 0) {
    Write-Host "`n[test-data] PARTIAL: $($failed.Count) of $($companies.Count) tenants did not complete sync.`n" -ForegroundColor Yellow
    exit 1
}

Write-Host "`n[test-data] Done. $($companies.Count) tenants ready for testing.`n" -ForegroundColor Green
exit 0
