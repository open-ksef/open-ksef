# Works with PowerShell 5.1+
<#
.SYNOPSIS
    Fresh-install smoke test of docker-compose.yml (Production) using locally built images.
.DESCRIPTION
    1. Builds openksef-{api,worker,portal-web}:dev-local via docker-compose.dev.yml (NuGet creds from .env.test)
    2. Stops the dev stack if running (no -v, dev volumes kept); fixed container names would clash
    3. Starts docker-compose.yml as project openksef-smoke on clean volumes, api/worker/portal-web on :dev-local
    4. Checks: API health, ASPNETCORE_ENVIRONMENT=Production, Swagger 404, setup wizard via API,
       password grant on openksef-portal-web, GET /api/tenants 200, worker started, no IDX/HTTPS-scheme errors in API log
    5. Removes the smoke stack with its volumes and restores the dev stack if it was running
.PARAMETER SkipBuild
    Reuse existing :dev-local images.
.PARAMETER TimeoutSeconds
    Max seconds to wait for each readiness check. Default: 240.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [int]$TimeoutSeconds = 240
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$devCompose = Join-Path $root 'docker-compose.dev.yml'
$prodCompose = Join-Path $root 'docker-compose.yml'
$project = 'openksef-smoke'
$adminEmail = 'smoke-admin@openksef.local'
$adminPassword = 'Smoke1234!'
$kcAdmin = 'admin'
$kcAdminPassword = 'smoke-kc-admin'

$results = New-Object System.Collections.Generic.List[object]
function Add-Result {
    param([string]$Check, [bool]$Ok, [string]$Detail)
    $results.Add([pscustomobject]@{ Check = $Check; Result = $(if ($Ok) { 'PASS' } else { 'FAIL' }); Detail = $Detail })
    $color = if ($Ok) { 'Green' } else { 'Red' }
    Write-Host ("  [{0}] {1} {2}" -f $(if ($Ok) { 'PASS' } else { 'FAIL' }), $Check, $Detail) -ForegroundColor $color
}

function Get-HttpStatus {
    param([string]$Url)
    try {
        return [int](Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop).StatusCode
    } catch {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        return 0
    }
}

function Wait-ForStatus {
    param([string]$Url, [int]$Expected)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ((Get-HttpStatus $Url) -eq $Expected) { return $true }
        Start-Sleep -Seconds 3
    }
    return $false
}

function Get-ContainerLog {
    param([string]$Name)
    $ErrorActionPreference = 'Continue'
    & docker logs $Name 2>&1 | ForEach-Object { "$_" }
}

function Invoke-Compose {
    param([string[]]$Arguments)
    $ErrorActionPreference = 'Continue'
    $output = & docker compose @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | Select-Object -Last 30 | ForEach-Object { Write-Host "    $_" }
        throw "docker compose $($Arguments -join ' ') failed"
    }
}

$testEnv = @{}
Get-Content (Join-Path $root '.env.test') | ForEach-Object {
    if ($_ -match '^\s*([^#][^=]+?)\s*=\s*(.+?)\s*$') { $testEnv[$Matches[1]] = $Matches[2] }
}

if (-not $SkipBuild) {
    Write-Host "`n[build] openksef-{api,worker,portal-web}:dev-local ..." -ForegroundColor Cyan
    $env:NUGET_CIRFMF_USER = $testEnv['NUGET_CIRFMF_USER']
    $env:NUGET_CIRFMF_PAT = $testEnv['NUGET_CIRFMF_PAT']
    try {
        Invoke-Compose @('-f', $devCompose, 'build', 'api', 'worker', 'portal-web')
    } finally {
        Remove-Item Env:NUGET_CIRFMF_USER, Env:NUGET_CIRFMF_PAT -ErrorAction SilentlyContinue
    }
}

$devWasRunning = [bool](& docker compose -f $devCompose ps -q)
if ($devWasRunning) {
    Write-Host "`n[dev] Stopping dev stack (volumes kept) ..." -ForegroundColor Cyan
    Invoke-Compose @('-f', $devCompose, 'down')
}

$tmpDir = Join-Path ([System.IO.Path]::GetTempPath()) "openksef-smoke-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $tmpDir | Out-Null
$override = Join-Path $tmpDir 'override.yml'
@"
services:
  api:
    image: openksef-api:dev-local
  worker:
    image: openksef-worker:dev-local
  portal-web:
    image: openksef-portal-web:dev-local
"@ | Set-Content -Path $override -Encoding ASCII
# --env-file keeps the repo .env (dev ENCRYPTION_KEY etc.) out, so this is a true fresh install.
$envFile = Join-Path $tmpDir 'smoke.env'
@"
POSTGRES_USER=openksef
POSTGRES_PASSWORD=smoke_pg_password
KEYCLOAK_ADMIN=$kcAdmin
KEYCLOAK_ADMIN_PASSWORD=$kcAdminPassword
"@ | Set-Content -Path $envFile -Encoding ASCII
$smokeArgs = @('-p', $project, '--env-file', $envFile, '-f', $prodCompose, '-f', $override)

try {
    Write-Host "`n[smoke] Fresh Production stack ($project) ..." -ForegroundColor Cyan
    Invoke-Compose ($smokeArgs + @('down', '-v'))
    Invoke-Compose ($smokeArgs + @('up', '-d'))

    Add-Result 'api /health 200' (Wait-ForStatus 'http://localhost:8081/health' 200) ''

    $aspEnv = (& docker exec openksef-api printenv ASPNETCORE_ENVIRONMENT) -join ''
    Add-Result 'api ASPNETCORE_ENVIRONMENT=Production' ($aspEnv -eq 'Production') $aspEnv

    $swagger = Get-HttpStatus 'http://localhost:8081/swagger/index.html'
    Add-Result 'swagger 404' ($swagger -eq 404) "status $swagger"

    Wait-ForStatus 'http://localhost:8082/auth/realms/master' 200 | Out-Null
    $setupOk = $false
    try {
        $auth = Invoke-RestMethod -Uri 'http://localhost:8080/api/system/setup/authenticate' -Method Post `
            -ContentType 'application/json' -Body (@{ username = $kcAdmin; password = $kcAdminPassword } | ConvertTo-Json)
        $apply = Invoke-RestMethod -Uri 'http://localhost:8080/api/system/setup/apply' -Method Post `
            -ContentType 'application/json' -Headers @{ 'X-Setup-Token' = $auth.setupToken } -Body (@{
                externalBaseUrl = 'http://localhost:8080'
                ksefBaseUrl = 'https://ksef-test.mf.gov.pl/api'
                adminEmail = $adminEmail
                adminPassword = $adminPassword
            } | ConvertTo-Json)
        $setupOk = [bool]$apply.success
        Add-Result 'setup wizard apply' $setupOk ''
    } catch {
        Add-Result 'setup wizard apply' $false $_.Exception.Message
    }

    $token = $null
    try {
        $token = (Invoke-RestMethod -Uri 'http://localhost:8080/auth/realms/openksef/protocol/openid-connect/token' `
            -Method Post -ContentType 'application/x-www-form-urlencoded' -Body @{
                client_id = 'openksef-portal-web'; grant_type = 'password'; username = $adminEmail; password = $adminPassword
            }).access_token
        Add-Result 'password grant openksef-portal-web' ([bool]$token) ''
    } catch {
        Add-Result 'password grant openksef-portal-web' $false $_.Exception.Message
    }

    if ($token) {
        try {
            $resp = Invoke-WebRequest -Uri 'http://localhost:8080/api/tenants' -UseBasicParsing `
                -Headers @{ Authorization = "Bearer $token" } -ErrorAction Stop
            Add-Result 'GET /api/tenants 200' ($resp.StatusCode -eq 200) "status $($resp.StatusCode)"
        } catch {
            $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
            Add-Result 'GET /api/tenants 200' $false "status $status"
        }
    } else {
        Add-Result 'GET /api/tenants 200' $false 'no token'
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $workerStarted = $false
    while (-not $workerStarted -and (Get-Date) -lt $deadline) {
        $workerStarted = [bool]((Get-ContainerLog 'openksef-worker') -match 'System initialized')
        if (-not $workerStarted) { Start-Sleep -Seconds 5 }
    }
    Add-Result 'worker log "System initialized"' $workerStarted ''

    $apiErrors = @((Get-ContainerLog 'openksef-api') | Where-Object { $_ -cmatch 'IDX\d+|HTTPS scheme|InvalidOperationException' })
    Add-Result 'api log free of IDX/HTTPS scheme/InvalidOperationException' ($apiErrors.Count -eq 0) "$($apiErrors.Count) lines"
    $apiErrors | Select-Object -First 10 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkYellow }
} finally {
    Write-Host "`n[smoke] Removing smoke stack and volumes ..." -ForegroundColor Cyan
    & { $ErrorActionPreference = 'Continue'; & docker compose @smokeArgs down -v 2>&1 | Out-Null }
    Remove-Item -Recurse -Force $tmpDir
    if ($devWasRunning) {
        Write-Host "`n[dev] Restoring dev stack ..." -ForegroundColor Cyan
        & (Join-Path $PSScriptRoot 'dev-env-up.ps1') -SkipNgrok -SkipBuild
    }
}

Write-Host "`n[summary]" -ForegroundColor Cyan
$results | Format-Table -AutoSize | Out-String | Write-Host
$failed = @($results | Where-Object { $_.Result -eq 'FAIL' }).Count
if ($failed -gt 0) {
    Write-Host "$failed check(s) failed" -ForegroundColor Red
    exit 1
}
Write-Host 'All smoke checks passed' -ForegroundColor Green
