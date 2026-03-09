#Requires -Version 7.0
<#
.SYNOPSIS
    Runs all unit and integration tests across the solution (.NET + Portal Web).
.DESCRIPTION
    Executes dotnet test for Api, Domain, and Api.IntegrationTests projects,
    then npm test for Portal.Web. Reports pass/fail summary.
    Integration tests use Testcontainers (requires Docker Desktop).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$failed = @()

function Invoke-TestStep {
    param([string]$Label, [scriptblock]$Command)
    Write-Host "`n[$Label] Running ..." -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  [FAIL] $Label" -ForegroundColor Red
        $script:failed += $Label
    } else {
        Write-Host "  [PASS] $Label" -ForegroundColor Green
    }
}

Invoke-TestStep 'Api.Tests' {
    dotnet test (Join-Path $root 'src/OpenKSeF.Api.Tests/OpenKSeF.Api.Tests.csproj') --nologo --verbosity minimal
}

Invoke-TestStep 'Domain.Tests' {
    dotnet test (Join-Path $root 'src/OpenKSeF.Domain.Tests/OpenKSeF.Domain.Tests.csproj') --nologo --verbosity minimal
}

Invoke-TestStep 'Api.IntegrationTests' {
    dotnet test (Join-Path $root 'src/OpenKSeF.Api.IntegrationTests/OpenKSeF.Api.IntegrationTests.csproj') --nologo --verbosity minimal
}

Invoke-TestStep 'Portal.Web' {
    Push-Location (Join-Path $root 'src/OpenKSeF.Portal.Web')
    try {
        npm test
    } finally {
        Pop-Location
    }
}

# Summary
Write-Host "`n" -NoNewline
Write-Host ('=' * 50) -ForegroundColor DarkGray
if ($failed.Count -eq 0) {
    Write-Host " All test suites passed." -ForegroundColor Green
} else {
    Write-Host " $($failed.Count) test suite(s) failed:" -ForegroundColor Red
    $failed | ForEach-Object { Write-Host "   - $_" -ForegroundColor Red }
}
Write-Host ('=' * 50) -ForegroundColor DarkGray

exit $failed.Count
