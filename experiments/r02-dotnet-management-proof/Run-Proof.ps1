[CmdletBinding()]
param([string] $ApprovedPackagePath, [string] $PackageConfigPath = (Join-Path $PSScriptRoot 'NuGet.Config'), [switch] $NoRestore)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
if ((& dotnet --version) -ne '10.0.401') { throw 'MG1 requires .NET SDK 10.0.401.' }
& (Join-Path $PSScriptRoot 'Test-Inputs.ps1') -ApprovedPackagePath $ApprovedPackagePath
function Invoke-FullTests([string] $Project, [string] $Name, [string] $Directory, [int] $Expected) {
    if (-not $NoRestore) {
        & dotnet restore $Project --locked-mode --configfile $PackageConfigPath | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Locked restore failed: $Name" }
    }
    & dotnet build $Project --configuration Release --no-restore | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $Name" }
    $results = Join-Path $PSScriptRoot "TestResults\$Directory"
    & dotnet test --project $Project --configuration Release --no-build --results-directory $results --report-trx | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Full tests failed: $Name" }
    [xml] $trx = Get-Content -LiteralPath (Join-Path $results "${Name}_net10.0_x64.trx") -Raw
    $counts = $trx.TestRun.ResultSummary.Counters
    if ([int]$counts.total -ne $Expected -or [int]$counts.passed -ne $Expected -or [int]$counts.failed -ne 0) {
        throw "Complete $Name test count required; zero/filtered/sibling counts are not evidence."
    }
    return @{ total = $Expected; passed = [int]$counts.passed; failed = [int]$counts.failed }
}
$regressionProject = Join-Path $PSScriptRoot '.regression\experiments\r02-dotnet-control-proof\ControlProof.csproj'
$rt1 = Invoke-FullTests $regressionProject 'ControlProof' 'rt1-released' 45
$regression = Get-Content -LiteralPath (Join-Path (Split-Path $regressionProject) 'evidence\results.json') -Raw | ConvertFrom-Json
if ($regression.sdk -ne '1.0.16' -or $regression.rows.Count -ne 45 -or
    @($regression.rows | Where-Object {$_.id -ne 'hook-only-failure-witness' -and $_.status -ne 'PASS'}).Count -ne 0 -or
    @($regression.rows | Where-Object {$_.id -eq 'hook-only-failure-witness' -and $_.status -eq 'FAIL'}).Count -ne 1 -or
    @($regression.rows | Where-Object {$_.counters.ownedCleanupCompleted -ne 1}).Count -ne 0) {
    throw 'Released-profile RT1 conformance matrix incomplete.'
}
$regression | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence\rt1-released-regression.json') -Encoding utf8
$hostTests = Invoke-FullTests (Join-Path $PSScriptRoot 'HostTests.csproj') 'HostTests' 'host' 22
$runtimeTests = Invoke-FullTests (Join-Path $PSScriptRoot 'ManagementProof.csproj') 'ManagementProof' 'runtime' 16
$runtime = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence\runtime-results.json') -Raw | ConvertFrom-Json
if ($runtime.rows.Count -ne 16 -or @($runtime.rows | Where-Object status -NE 'Pass').Count -ne 0 -or
    @($runtime.rows | Where-Object {$_.counts.ownedCleanupCompleted -ne 1}).Count -ne 0) {
    throw 'MG1 real-runtime rows or cleanup are incomplete.'
}
[ordered] @{
    observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    mg1 = 'Pass: separately approved released NuGet 1.0.16 / unchanged RT1 native runtime 1.0.90 minimal HTTP/stdio'
    originalSourceBuiltProfile = 'Historical RT1 unchanged; reproduction blocker retained; no byte equivalence claimed'
    tests = @{ rt1ReleasedRegression = $rt1; hostComponents = $hostTests; actualRuntime = $runtimeTests }
    physicalComputationTermination = 'Unknown; socket closure and SDK acknowledgement are not physical stop/rollback'
    rt2 = 'Independent lifecycle gate; this fixture does not close it'
    pv1 = 'Blocked: no account/terms/usage approval'
    production = 'Disabled; R13 scheduler/ledger protocol and R04 durable authority/storage not established'
} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence\disposition.json') -Encoding utf8
Write-Host 'MG1 released profile Pass: 45 released RT1 regressions, 22 host component tests and 16 actual runtime cases. Production/account gates remain open.'
