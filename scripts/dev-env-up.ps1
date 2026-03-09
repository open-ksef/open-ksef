# Works with PowerShell 5.1+
<#
.SYNOPSIS
    Brings up the full OpenKSeF dev environment with optional ngrok HTTPS tunnel.
.DESCRIPTION
    1. Copies .env.test -> .env if .env is missing
    2. docker compose -f docker-compose.dev.yml up -d --build
    3. Waits for Keycloak, API, and Portal health endpoints
    4. Detects or starts ngrok tunnel for HTTPS access (needed by Android OIDC)
    5. Updates all Keycloak client redirect URIs with the ngrok URL
    6. Creates a test user in Keycloak
    7. Prints a summary with all URLs and credentials
.PARAMETER SkipBuild
    Skip Docker image rebuild.
.PARAMETER SkipNgrok
    Skip ngrok tunnel setup (portal-only dev, no mobile).
.PARAMETER NgrokPort
    Local port ngrok tunnels to. Default: 8080 (gateway).
.PARAMETER TimeoutSeconds
    Max seconds to wait for health checks. Default: 180.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$SkipNgrok,
    [int]$NgrokPort = 8080,
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot

# ── Helpers ───────────────────────────────────────────────────────────
function Wait-ForUrl {
    param([string]$Url, [string]$Label, [int]$Timeout)
    $deadline = (Get-Date).AddSeconds($Timeout)
    $attempt = 0
    while ((Get-Date) -lt $deadline) {
        $attempt++
        try {
            $resp = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
            if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 400) {
                Write-Host "  [OK] $Label ($($resp.StatusCode)) after $attempt attempts" -ForegroundColor Green
                return $true
            }
        } catch { }
        Start-Sleep -Seconds 3
    }
    Write-Warning "Timed out waiting for $Label at $Url"
    return $false
}

function Get-KeycloakAdminToken {
    param([string]$KcBaseUrl, [string]$User, [string]$Pass)
    $body = @{ client_id = 'admin-cli'; username = $User; password = $Pass; grant_type = 'password' }
    $resp = Invoke-RestMethod -Uri "$KcBaseUrl/auth/realms/master/protocol/openid-connect/token" `
        -Method Post -Body $body -ContentType 'application/x-www-form-urlencoded'
    return $resp.access_token
}

function Update-KeycloakClientRedirects {
    param([string]$KcBaseUrl, [string]$Token, [string]$Realm, [string]$ClientId, [string[]]$RedirectUris)

    $headers = @{ Authorization = "Bearer $Token"; 'Content-Type' = 'application/json' }
    $clients = Invoke-RestMethod -Uri "$KcBaseUrl/auth/admin/realms/$Realm/clients?clientId=$ClientId" -Headers $headers
    if ($clients.Count -eq 0) { Write-Warning "Client '$ClientId' not found"; return }

    $id = $clients[0].id
    $current = @($clients[0].redirectUris)

    $merged = @($current) + @($RedirectUris) | Select-Object -Unique
    $payload = @{ redirectUris = $merged; webOrigins = @('*') } | ConvertTo-Json

    Invoke-RestMethod -Uri "$KcBaseUrl/auth/admin/realms/$Realm/clients/$id" `
        -Headers $headers -Method Put -Body $payload | Out-Null

    Write-Host "  [OK] $ClientId redirects: $($merged -join ', ')" -ForegroundColor Green
}

# ── 1. Ensure .env exists ────────────────────────────────────────────
$envFile = Join-Path $root '.env'
$envTestFile = Join-Path $root '.env.test'

if (-not (Test-Path $envFile)) {
    Write-Host "[env] .env not found, copying .env.test ..." -ForegroundColor Yellow
    Copy-Item $envTestFile $envFile
}

$testEnv = @{}
Get-Content $envTestFile | ForEach-Object {
    if ($_ -match '^\s*([^#][^=]+?)\s*=\s*(.+?)\s*$') {
        $testEnv[$Matches[1]] = $Matches[2]
    }
}

# ── 2. Start Docker services ─────────────────────────────────────────
$composeFile = Join-Path $root 'docker-compose.dev.yml'
$buildFlag = if ($SkipBuild) { @() } else { @('--build') }

Write-Host "`n[docker] Starting services ..." -ForegroundColor Cyan
& docker compose -f $composeFile up -d @buildFlag
if ($LASTEXITCODE -ne 0) { throw "docker compose up failed" }

# ── 3. Wait for health endpoints ─────────────────────────────────────
Write-Host "`n[health] Waiting for services (timeout ${TimeoutSeconds}s) ..." -ForegroundColor Cyan

Wait-ForUrl -Url 'http://localhost:8082/auth/realms/openksef' -Label 'Keycloak realm' -Timeout $TimeoutSeconds | Out-Null
Wait-ForUrl -Url 'http://localhost:8081/health' -Label 'API direct' -Timeout $TimeoutSeconds | Out-Null
Wait-ForUrl -Url 'http://localhost:8080/' -Label 'Portal (via gateway)' -Timeout $TimeoutSeconds | Out-Null

# ── 4. Ngrok tunnel ──────────────────────────────────────────────────
$ngrokUrl = $null

if (-not $SkipNgrok) {
    Write-Host "`n[ngrok] Setting up HTTPS tunnel ..." -ForegroundColor Cyan

    # Try common ngrok API ports to find a running instance
    $ngrokApiPorts = @(4040, 4041, 4042)
    foreach ($port in $ngrokApiPorts) {
        try {
            $tunnels = Invoke-RestMethod -Uri "http://127.0.0.1:$port/api/tunnels" -TimeoutSec 3 -ErrorAction Stop
            $match = $tunnels.tunnels | Where-Object {
                $_.config.addr -match ":$NgrokPort$" -or $_.config.addr -eq "http://localhost:$NgrokPort"
            } | Select-Object -First 1

            if ($match) {
                $ngrokUrl = $match.public_url
                Write-Host "  [OK] Found running ngrok: $ngrokUrl -> localhost:$NgrokPort" -ForegroundColor Green
                break
            }
        } catch { }
    }

    if (-not $ngrokUrl) {
        Write-Host "  [start] No ngrok tunnel found, starting one ..." -ForegroundColor Yellow

        $ngrokCmd = Get-Command ngrok -ErrorAction SilentlyContinue
        if (-not $ngrokCmd) { throw "ngrok is not installed. Install from https://ngrok.com/download" }

        Start-Process -FilePath 'ngrok' -ArgumentList "http $NgrokPort" -WindowStyle Minimized
        Start-Sleep -Seconds 5

        foreach ($port in $ngrokApiPorts) {
            try {
                $tunnels = Invoke-RestMethod -Uri "http://127.0.0.1:$port/api/tunnels" -TimeoutSec 3 -ErrorAction Stop
                $match = $tunnels.tunnels | Where-Object {
                    $_.config.addr -match ":$NgrokPort$" -or $_.config.addr -eq "http://localhost:$NgrokPort"
                } | Select-Object -First 1

                if ($match) {
                    $ngrokUrl = $match.public_url
                    Write-Host "  [OK] ngrok started: $ngrokUrl -> localhost:$NgrokPort" -ForegroundColor Green
                    break
                }
            } catch { }
        }

        if (-not $ngrokUrl) { throw "Failed to start ngrok tunnel. Check ngrok auth token." }
    }
}

# ── 5. Update Keycloak clients with ngrok URL ────────────────────────
$kcBaseUrl = 'http://localhost:8082'
$adminUser = $testEnv['KEYCLOAK_ADMIN']
$adminPass = $testEnv['KEYCLOAK_ADMIN_PASSWORD']
$testUser  = $testEnv['E2E_TEST_USER']
$testPass  = $testEnv['E2E_TEST_PASSWORD']

Write-Host "`n[keycloak] Getting admin token ..." -ForegroundColor Cyan
$token = Get-KeycloakAdminToken -KcBaseUrl $kcBaseUrl -User $adminUser -Pass $adminPass

if ($ngrokUrl) {
    Write-Host "[keycloak] Updating client redirect URIs with $ngrokUrl ..." -ForegroundColor Cyan

    $ngrokRedirects = @("$ngrokUrl/*")

    Update-KeycloakClientRedirects -KcBaseUrl $kcBaseUrl -Token $token -Realm 'openksef' `
        -ClientId 'openksef-mobile' -RedirectUris $ngrokRedirects

    Update-KeycloakClientRedirects -KcBaseUrl $kcBaseUrl -Token $token -Realm 'openksef' `
        -ClientId 'openksef-portal-web' -RedirectUris $ngrokRedirects

    Update-KeycloakClientRedirects -KcBaseUrl $kcBaseUrl -Token $token -Realm 'openksef' `
        -ClientId 'openksef-api' -RedirectUris $ngrokRedirects
}

# ── 6. Configure openksef-api service account + token exchange ────────
Write-Host "`n[keycloak] Configuring openksef-api client for token exchange ..." -ForegroundColor Cyan

$kcHeaders = @{ Authorization = "Bearer $token"; 'Content-Type' = 'application/json' }

# Get openksef-api client
$apiClients = Invoke-RestMethod -Uri "$kcBaseUrl/auth/admin/realms/openksef/clients?clientId=openksef-api" -Headers $kcHeaders
if ($apiClients.Count -eq 0) { throw "openksef-api client not found in Keycloak" }
$apiClientUuid = $apiClients[0].id

# Get the client secret (Keycloak generates one for confidential clients)
$secretResp = Invoke-RestMethod -Uri "$kcBaseUrl/auth/admin/realms/openksef/clients/$apiClientUuid/client-secret" -Headers $kcHeaders
$apiClientSecret = $secretResp.value

if ([string]::IsNullOrEmpty($apiClientSecret)) {
    # Generate a new secret
    $secretResp = Invoke-RestMethod -Uri "$kcBaseUrl/auth/admin/realms/openksef/clients/$apiClientUuid/client-secret" `
        -Headers $kcHeaders -Method Post
    $apiClientSecret = $secretResp.value
}
Write-Host "  [OK] openksef-api client secret: $($apiClientSecret.Substring(0,8))..." -ForegroundColor Green

# Enable token-exchange permission on the openksef-api client
# 1) Enable permissions on the client
$permPayload = @{ enabled = $true } | ConvertTo-Json
try {
    Invoke-RestMethod -Uri "$kcBaseUrl/auth/admin/realms/openksef/clients/$apiClientUuid/management/permissions" `
        -Headers $kcHeaders -Method Put -Body $permPayload | Out-Null
    Write-Host "  [OK] Client permissions enabled" -ForegroundColor Green
} catch {
    Write-Host "  [skip] Permissions may already be enabled: $($_.Exception.Message)" -ForegroundColor Yellow
}

# 2) Get the service account user for openksef-api
$saUser = Invoke-RestMethod -Uri "$kcBaseUrl/auth/admin/realms/openksef/clients/$apiClientUuid/service-account-user" -Headers $kcHeaders
$saUserId = $saUser.id
Write-Host "  [OK] Service account user: $saUserId" -ForegroundColor Green

# 3) Get the realm-management client (holds token-exchange role)
$rmClients = Invoke-RestMethod -Uri "$kcBaseUrl/auth/admin/realms/openksef/clients?clientId=realm-management" -Headers $kcHeaders
if ($rmClients.Count -gt 0) {
    $rmClientUuid = $rmClients[0].id

    # Get available roles for the service account user on realm-management client
    $availableRoles = Invoke-RestMethod `
        -Uri "$kcBaseUrl/auth/admin/realms/openksef/users/$saUserId/role-mappings/clients/$rmClientUuid/available" `
        -Headers $kcHeaders

    $tokenExchangeRole = $availableRoles | Where-Object { $_.name -eq 'token-exchange' }
    if ($tokenExchangeRole) {
        $rolePayload = @( @{ id = $tokenExchangeRole.id; name = $tokenExchangeRole.name } ) | ConvertTo-Json
        Invoke-RestMethod -Uri "$kcBaseUrl/auth/admin/realms/openksef/users/$saUserId/role-mappings/clients/$rmClientUuid" `
            -Headers $kcHeaders -Method Post -Body $rolePayload | Out-Null
        Write-Host "  [OK] token-exchange role assigned to service account" -ForegroundColor Green
    } else {
        # Check if already assigned
        $assignedRoles = Invoke-RestMethod `
            -Uri "$kcBaseUrl/auth/admin/realms/openksef/users/$saUserId/role-mappings/clients/$rmClientUuid" `
            -Headers $kcHeaders
        $alreadyAssigned = $assignedRoles | Where-Object { $_.name -eq 'token-exchange' }
        if ($alreadyAssigned) {
            Write-Host "  [skip] token-exchange role already assigned" -ForegroundColor Yellow
        } else {
            Write-Host "  [warn] token-exchange role not found on realm-management client. Ensure Keycloak has token-exchange feature enabled." -ForegroundColor Yellow
        }
    }
} else {
    Write-Host "  [warn] realm-management client not found" -ForegroundColor Yellow
}

# 4) Write API_CLIENT_SECRET to .env if not already set
$envContent = Get-Content $envFile -Raw
if ($envContent -match '(?m)^API_CLIENT_SECRET=\s*$' -or $envContent -notmatch 'API_CLIENT_SECRET=') {
    $envContent = $envContent -replace '(?m)^API_CLIENT_SECRET=.*$', "API_CLIENT_SECRET=$apiClientSecret"
    if ($envContent -notmatch 'API_CLIENT_SECRET=') {
        $envContent += "`nAPI_CLIENT_SECRET=$apiClientSecret`n"
    }
    Set-Content -Path $envFile -Value $envContent -NoNewline
    Write-Host "  [OK] API_CLIENT_SECRET written to .env" -ForegroundColor Green

    # Restart API container to pick up the new secret
    Write-Host "  [restart] Restarting API container ..." -ForegroundColor Cyan
    & docker compose -f $composeFile restart api | Out-Null
    Wait-ForUrl -Url 'http://localhost:8081/health' -Label 'API restart' -Timeout 60 | Out-Null
} else {
    Write-Host "  [skip] API_CLIENT_SECRET already set in .env" -ForegroundColor Yellow
}

# ── 7. Create test user ──────────────────────────────────────────────
Write-Host "`n[keycloak] Provisioning test user '$testUser' ..." -ForegroundColor Cyan

$headers = @{ Authorization = "Bearer $token" }
$existingUsers = Invoke-RestMethod -Uri "$kcBaseUrl/auth/admin/realms/openksef/users?username=$testUser&exact=true" `
    -Headers $headers -Method Get

if ($existingUsers.Count -gt 0) {
    Write-Host "  [skip] User '$testUser' already exists (id=$($existingUsers[0].id))" -ForegroundColor Yellow
} else {
    $userPayload = @{
        username = $testUser; enabled = $true
        email = "$testUser@openksef.test"; firstName = 'Test'; lastName = 'User'
    } | ConvertTo-Json

    Invoke-RestMethod -Uri "$kcBaseUrl/auth/admin/realms/openksef/users" `
        -Headers ($headers + @{ 'Content-Type' = 'application/json' }) `
        -Method Post -Body $userPayload | Out-Null

    $createdUsers = Invoke-RestMethod -Uri "$kcBaseUrl/auth/admin/realms/openksef/users?username=$testUser&exact=true" `
        -Headers $headers -Method Get
    $userId = $createdUsers[0].id

    $passPayload = @{ type = 'password'; value = $testPass; temporary = $false } | ConvertTo-Json
    Invoke-RestMethod -Uri "$kcBaseUrl/auth/admin/realms/openksef/users/$userId/reset-password" `
        -Headers ($headers + @{ 'Content-Type' = 'application/json' }) `
        -Method Put -Body $passPayload | Out-Null

    Write-Host "  [OK] User '$testUser' created (id=$userId)" -ForegroundColor Green
}

# ── 8. Provision test data (tenant + KSeF creds + sync) ───────────────
$setupScript = Join-Path $PSScriptRoot 'setup-test-data.ps1'
if (Test-Path $setupScript) {
    Write-Host ""
    & $setupScript
}

# ── 9. Summary ────────────────────────────────────────────────────────
Write-Host "`n" -NoNewline
Write-Host ('=' * 65) -ForegroundColor DarkGray
Write-Host " OpenKSeF Dev Environment Ready" -ForegroundColor Green
Write-Host ('=' * 65) -ForegroundColor DarkGray
Write-Host ""
Write-Host "  Gateway (portal+API+auth)  : http://localhost:8080"
Write-Host "  Keycloak admin console      : http://localhost:8082/auth/admin"
Write-Host "  API direct                  : http://localhost:8081"
Write-Host "  API Swagger                 : http://localhost:8081/swagger"
Write-Host "  Portal direct               : http://localhost:8083"
if ($ngrokUrl) {
    Write-Host ""
    Write-Host "  HTTPS (ngrok)               : $ngrokUrl" -ForegroundColor Yellow
    Write-Host "  Mobile app server URL       : $ngrokUrl" -ForegroundColor Yellow
    Write-Host "  (Android emulator needs this HTTPS URL for OIDC login)"
}
Write-Host ""
Write-Host "  Keycloak admin              : $adminUser / $adminPass"
Write-Host "  Test user                   : $testUser / $testPass"
$testNip = $testEnv['E2E_TEST_NIP']
if ($testNip) {
    Write-Host "  Test company NIP            : $testNip"
}
Write-Host ""
Write-Host ('=' * 65) -ForegroundColor DarkGray
