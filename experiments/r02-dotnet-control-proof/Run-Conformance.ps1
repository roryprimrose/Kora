[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$project = Join-Path $PSScriptRoot 'ControlProof.csproj'
$results = Join-Path $PSScriptRoot 'TestResults'
$sdk = & dotnet --version
if ($sdk -ne '10.0.401') { throw 'RT1 requires .NET SDK 10.0.401.' }

& dotnet build $project --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'RT1 fixture build failed.' }
& dotnet test --project $project --configuration Release --no-build --results-directory $results --report-trx
if ($LASTEXITCODE -ne 0) { throw 'RT1 conformance tests failed; inspect the synthetic TRX and results matrix.' }

[xml] $trx = Get-Content -LiteralPath (Join-Path $results 'ControlProof_net10.0_x64.trx') -Raw
$counts = $trx.TestRun.ResultSummary.Counters
if ([int] $counts.total -ne 45 -or [int] $counts.passed -ne 45 -or [int] $counts.failed -ne 0)
{
    throw 'RT1 requires all 45 discovered tests, not a filtered or zero-test run.'
}
$evidencePath = Join-Path $PSScriptRoot 'evidence\results.json'
$report = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
if ($report.rows.Count -ne 45) { throw 'Incomplete RT1 matrix.' }
$witness = @($report.rows | Where-Object id -eq 'hook-only-failure-witness')
if ($witness.Count -ne 1 -or $witness[0].status -ne 'FAIL' -or $witness[0].counters.deniedMarkerForwarded -ne 0)
{
    throw 'Expected rejected hook-only failure witness was lost.'
}
if (@($report.rows | Where-Object { $_.id -ne 'hook-only-failure-witness' -and $_.status -ne 'PASS' }).Count -ne 0)
{
    throw 'Selected final-gated .NET profile did not pass.'
}
if (@($report.rows | Where-Object { $_.counters.ownedCleanupCompleted -ne 1 }).Count -ne 0)
{
    throw 'An owned fixture cleanup was not confirmed.'
}
$node = Join-Path $PSScriptRoot '..\r02-runtime-proof\evidence\results.json'
if ((Get-FileHash -LiteralPath $node).Hash -ine 'c1a40817bf37a125c08abe1eaa1dd31f5ec04fb959cc8ce3890cbfe95b9f0331')
{
    throw 'Historical Node evidence changed; keep it separate from .NET observations.'
}
$disposition = [ordered] @{
    observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    rt1 = 'PASS: reviewed exact-tag source-built minimal HTTP/stdio profile only'
    testSdk = $sdk
    tests = @{ total = [int] $counts.total; passed = [int] $counts.passed; failed = [int] $counts.failed }
    capabilityRows = @{ selectedProfilePass = 44; rejectedHookOnlyFail = 1 }
    modelPayloadDeniedMarkersForwarded = ($report.rows | Measure-Object -Property { $_.counters.deniedMarkerForwarded } -Sum).Sum
    deniedToolEffects = ($report.rows | Where-Object id -eq 'denied-tool-zero-effects').counters.deniedToolEffects
    nodeWitnessUnchanged = $true
    releasedNugetByteParity = 'BLOCKED: direct registry TLS acquisition; source-build alternative explicitly approved'
    rt2 = 'NOT RUN: full process lifecycle network/file/diagnostic attribution required'
    mg1 = 'NOT RUN: .NET complete byte/deadline/admission envelope required'
    pv1 = 'BLOCKED: no intended-account or potentially paid-usage approval'
    gate0 = 'OPEN; production adapter disabled'
    sdkAssemblySha256 = $report.sdkAssemblySha256
    sdkPackageSha256 = $report.sdkPackageSha256
    fixtureAssemblySha256 = $report.fixtureAssemblySha256
    sourceCommit = $report.sdkSourceCommit
    commands = @('Prepare-Candidate.ps1', 'dotnet build ControlProof.csproj --configuration Release --no-restore',
        'dotnet test --project ControlProof.csproj --configuration Release --no-build --results-directory TestResults --report-trx')
}
$disposition | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 (Join-Path $PSScriptRoot 'evidence\disposition.json')
Write-Host 'RT1 selected source-built profile PASS (45/45). Hook-only FAIL retained; Gate 0 and RT2/MG1/PV1 remain open.'
