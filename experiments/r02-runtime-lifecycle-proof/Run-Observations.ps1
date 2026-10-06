[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
if ((& dotnet --version) -ne '10.0.401') { throw 'RT2 requires SDK 10.0.401.' }
$project = Join-Path $PSScriptRoot 'LifecycleProof.csproj'
$started = [DateTimeOffset]::UtcNow
$results = Join-Path $PSScriptRoot ("TestResults\run-" + [Guid]::NewGuid().ToString('N'))
& (Join-Path $PSScriptRoot 'Test-ApprovedInputs.ps1')
& dotnet build $project --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'RT2 build failed.' }
& dotnet test --project $project --configuration Release --no-build --results-directory $results --report-trx
if ($LASTEXITCODE -ne 0) { throw 'RT2 tests failed; no passing disposition may be recorded.' }
& (Join-Path $PSScriptRoot 'Test-ApprovedInputs.ps1')
[xml] $trx = Get-Content -LiteralPath (Join-Path $results 'LifecycleProof_net10.0_x64.trx') -Raw
$counts = $trx.TestRun.ResultSummary.Counters
if ([int] $counts.total -ne 20 -or [int] $counts.passed -ne 20 -or
    [int] $counts.failed -ne 0 -or [int] $counts.notExecuted -ne 0)
{
    throw 'RT2 requires all 20 real/control/deterministic tests, not a filtered run.'
}
$trials = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'evidence\trials') -Filter '*.json' -File |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json -DateKind String })
if ($trials.Count -ne 13 -or @($trials | Where-Object {
    $_.trial -ne 'PASS' -or [DateTimeOffset]::Parse($_.startedUtc) -lt $started -or
    $_.cleanupCompleted -ne 1 -or $_.liveObservedOwnedProcessesAfterShutdown -ne 0 -or
    $_.deniedMarkersAtProvider -ne 0 -or $_.credentialInModelBody -ne 0 -or
    $_.observation.fileOverflow -ne 0 -or $_.observation.processes.Count -lt 1
}).Count -ne 0)
{
    throw 'Incomplete, stale, failed, overflowing or unclean RT2 trial evidence.'
}
$control = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence\positive-controls.json') -Raw | ConvertFrom-Json -DateKind String
if ($control.status -ne 'PASS' -or [DateTimeOffset]::Parse($control.startedUtc) -lt $started)
{
    throw 'Missing current positive controls.'
}
$identities = foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -File |
    Where-Object Extension -in '.cs', '.ps1', '.csproj', '.props', '.Config', '.json')
{
    [ordered] @{ file = $file.Name; sha256 = (Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant() }
}
[ordered] @{
    observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    startedUtc = $started.ToString('O')
    rt2 = 'BLOCKED'
    reason = 'All-path process/file/network/diagnostic observation and native content prevention are unproved. User retained fail-closed runtime contract; dedicated-host tracing deferred.'
    sdk = '1.0.16-rt1.source.f8ae645.1'
    source = 'f8ae645902b74b62cd47aac1fd9b29adaec3aff2'
    runtime = '1.0.90'
    protocol = 3
    sdkAssemblySha256 = ($trials.sdkAssemblySha256 | Sort-Object -Unique)
    fixtureAssemblySha256 = ($trials.fixtureAssemblySha256 | Sort-Object -Unique)
    nativeLauncherSha256 = ($trials.nativeLauncherSha256 | Sort-Object -Unique)
    nativePayloadSha256 = ($trials.nativePayloadSha256 | Sort-Object -Unique)
    fixtureSources = @($identities)
    tests = @{ total = [int] $counts.total; passed = [int] $counts.passed; failed = [int] $counts.failed; skipped = 0 }
    realRuntimeTrials = 13
    observerDeterministicTests = 6
    livePositiveControlTests = 1
    providerRequests = ($trials | Measure-Object providerRequests -Sum).Sum
    blockedModelRequests = ($trials.boundaryReceipts | Where-Object blocked).Count
    deniedMarkersAtProvider = ($trials | Measure-Object deniedMarkersAtProvider -Sum).Sum
    credentialInModelBody = ($trials | Measure-Object credentialInModelBody -Sum).Sum
    observedOwnedProcesses = @($trials.observation.processes).Count
    observedOwnedDescendants = @($trials.observation.processes | Where-Object role -eq descendant).Count
    observedSurvivorsAfterShutdown = ($trials | Measure-Object liveObservedOwnedProcessesAfterShutdown -Sum).Sum
    watcherOverflows = ($trials.observation | Measure-Object fileOverflow -Sum).Sum
    maximumSampleGapMs = ($trials.observation | Measure-Object maximumSampleGapMs -Maximum).Maximum
    syntheticOnly = $true
    liveAccounts = 0
    productionEnabled = $false
    nativePrevention = 'BLOCKED: observer does not prevent outside-scratch writes or direct native egress'
    optionalTransports = 'NOT RUN/unavailable: WebSockets/MCP/agents/plugins/tools/memory/spill/export'
    pv1 = 'BLOCKED: no intended-account/terms/cost approval'
    gate0 = 'OPEN'
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence\disposition.json') -Encoding utf8
Write-Warning '20/20 tests PASS; RT2 acceptance BLOCKED. No production capability enabled.'
exit 2
