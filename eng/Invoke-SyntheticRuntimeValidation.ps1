#requires -Version 7.5
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $OutputDirectory,
    [Parameter(Mandatory)][string] $PackageConfigPath,
    [switch] $ApproveSyntheticNativeTrials,
    [switch] $ReleasedOnly,
    [string] $VerifiedArchiveDirectory
)
. (Join-Path $PSScriptRoot 'RuntimeValidation.Common.ps1')
if (!$ApproveSyntheticNativeTrials) { throw 'Explicit operator approval for pinned acquisition and synthetic native trials is required.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
$configPath = [IO.Path]::GetFullPath($PackageConfigPath)
foreach ($path in @($output, $configPath)) {
    if ($path.Equals($repository, [StringComparison]::OrdinalIgnoreCase) -or
        $path.StartsWith($repository + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Receipts and machine-local package configuration must be outside the repository.'
    }
}
Assert-NoLinks $configPath -AncestorsOnly
$identity = Assert-RuntimePreparationCheckout $repository
$profiles = @(Get-RuntimeHistoricalProfiles $repository)
New-ProofDirectory $output
$runId = [Guid]::NewGuid().ToString('N')
$stage = Join-Path $repository ".net-test-artifacts\runtime-native-$runId"
New-ProofDirectory $stage
$primary = Join-Path ([IO.Path]::GetTempPath()) ('rt2-' + $runId.Substring(0, 8))
New-ProofDirectory $primary
$routingChanges = [Collections.Generic.List[object]]::new()
$started = [DateTimeOffset]::UtcNow
$archive = Join-Path $output 'fixture-snapshot.zip'
& git -C $repository archive --format=zip "--output=$archive" $identity.base `
    global.json experiments/r02-runtime-proof experiments/r02-dotnet-control-proof `
    experiments/r02-runtime-lifecycle-proof experiments/r02-dotnet-management-proof
if ($LASTEXITCODE -ne 0) { throw 'Exact-base fixture snapshot failed.' }
Expand-Archive -LiteralPath $archive -DestinationPath $stage
Assert-NoLinks $stage

function Set-StagedRouting {
    param([string] $Path, [string] $Old, [string] $New)
    $text = [IO.File]::ReadAllText($Path)
    if ([regex]::Matches($text, [regex]::Escape($Old)).Count -ne 1) { throw 'Staged preparation recipe differs; refusing an unreviewed routing edit.' }
    $before = (Get-FileHash -LiteralPath $Path).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($Path, $text.Replace($Old, $New), [Text.UTF8Encoding]::new($false))
    $routingChanges.Add([ordered]@{
        file = (Split-Path $Path -Leaf); beforeSha256 = $before
        afterSha256 = (Get-FileHash -LiteralPath $Path).Hash.ToLowerInvariant()
        reason = 'Private-copy package configuration or source-repository metadata location only; SDK/native and runtime control code unchanged'
    })
}

function New-SourceRoutingConfig {
    param([string] $Path)
    [xml] $config = Get-Content -LiteralPath $configPath -Raw
    $sources = $config.configuration.packageSources
    $firstSource = @($sources.add)[0]
    $local = $config.CreateElement('add')
    $local.SetAttribute('key', 'rt1-source-build')
    $local.SetAttribute('value', (Join-Path $primary '.candidate\feed'))
    $null = $sources.InsertBefore($local, $firstSource)
    $mapping = $config.CreateElement('packageSource')
    $mapping.SetAttribute('key', 'rt1-source-build')
    $pattern = $config.CreateElement('package')
    $pattern.SetAttribute('pattern', 'GitHub.Copilot.SDK')
    $null = $mapping.AppendChild($pattern)
    $null = $config.configuration.packageSourceMapping.AppendChild($mapping)
    $config.Save($Path)
}

$rt1 = Join-Path $stage 'experiments\r02-dotnet-control-proof'
$rt2 = Join-Path $stage 'experiments\r02-runtime-lifecycle-proof'
$mg1 = Join-Path $stage 'experiments\r02-dotnet-management-proof'
$sourceConfig = Join-Path $output 'source-routing.config'
$rt2Config = Join-Path $output 'rt2-routing.config'
New-SourceRoutingConfig $sourceConfig
New-SourceRoutingConfig $rt2Config
$sourceLiteral = "'" + $sourceConfig.Replace("'", "''") + "'"
$rt2Literal = "'" + $rt2Config.Replace("'", "''") + "'"
Set-StagedRouting (Join-Path $rt1 'Prepare-Candidate.ps1') '$config = Join-Path $root ''NuGet.Config''' ('$config = ' + $sourceLiteral)
Set-StagedRouting (Join-Path $rt1 'Test-Reproduction.ps1') '(Join-Path $PSScriptRoot ''NuGet.Config'')' $sourceLiteral
Set-StagedRouting (Join-Path $rt2 'Prepare-Fixture.ps1') '(Join-Path $PSScriptRoot ''NuGet.Config'')' $rt2Literal
Set-StagedRouting (Join-Path $rt2 'Prepare-Fixture.ps1') '$configPath = Join-Path $cache ''NuGet.Config''' ('$configPath = ' + $rt2Literal)
$candidate = Join-Path $rt2 '.candidate'
New-ProofDirectory $candidate
$escaped = [Security.SecurityElement]::Escape($primary)
"<Project><PropertyGroup><Rt1Stage>$escaped</Rt1Stage></PropertyGroup></Project>" |
    Set-Content -LiteralPath (Join-Path $candidate 'Stage.props') -Encoding utf8NoBOM

$sourceResult = [ordered]@{ status = 'Not run'; phase = 'Preparation'; rt1 = 'Not run'; rt2 = 'Blocked'; nativeTrials = 0 }
$releasedResult = [ordered]@{ status = 'Not run'; phase = 'Preparation'; rt1 = 'Not run'; mg1 = 'Not run'; nativeTrials = 0 }
$telemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$testTelemetry = $env:TESTINGPLATFORM_TELEMETRY_OPTOUT
Push-Location $stage
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
    if ($ReleasedOnly) {
        $sourceResult.status = 'Not run: explicitly released-only continuation; previous source blocker is not resolved'
    }
    else {
    Write-Host 'SOURCE PROFILE: original clean-source preparation/reproduction; exact pins required before any native launch.'
    try {
        & (Join-Path $rt2 'Prepare-Fixture.ps1')
        & (Join-Path $rt2 'Test-ApprovedInputs.ps1')
        $sourceResult.phase = 'RT1 conformance'
        $trialStart = [DateTimeOffset]::UtcNow
        $project = Join-Path $primary 'ControlProof.csproj'
        & dotnet build $project --configuration Release --no-restore | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Source RT1 regression build failed.' }
        $resultsDirectory = Join-Path $output 'source-rt1-tests'
        New-ProofDirectory $resultsDirectory
        & dotnet test --project $project --configuration Release --no-build --results-directory $resultsDirectory --report-trx | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Source RT1 regression tests failed.' }
        [xml] $trx = Get-Content -LiteralPath (Join-Path $resultsDirectory 'ControlProof_net10.0_x64.trx') -Raw
        $counts = $trx.TestRun.ResultSummary.Counters
        if ([int]$counts.total -ne 45 -or [int]$counts.passed -ne 45 -or
            [int]$counts.failed -ne 0 -or [int]$counts.notExecuted -ne 0) {
            throw 'Complete unfiltered source RT1 regression suite required.'
        }
        $regression = Read-RuntimeReceipt (Join-Path $primary 'evidence\results.json')
        Assert-RuntimeRegressionRows $regression 'staged source RT1'
        if ([DateTimeOffset]::Parse($regression.recordedAtUtc, [Globalization.CultureInfo]::InvariantCulture) -lt $trialStart -or
            @($regression.rows | Where-Object { $_.counters.ownedCleanupCompleted -ne 1 }).Count -ne 0 -or
            @($regression.rows | Where-Object { $_.counters.deniedMarkerForwarded -ne 0 }).Count -ne 0) {
            throw 'Stale source RT1 receipts, incomplete cleanup or denied-marker forwarding.'
        }
        $sourceResult.rt1 = 'Pass: 45/45; expected hook-only FAIL retained'
        $sourceResult.phase = 'RT2 bounded observations'
        # Run the script in its own process: its truthful Blocked exit 2 must not terminate this coordinator.
        & pwsh -NoProfile -NonInteractive -File (Join-Path $rt2 'Run-Observations.ps1')
        if ($LASTEXITCODE -ne 2) { throw 'RT2 must complete its full suite and retain Blocked exit 2.' }
        $sourceResult.status = 'Pass: bounded controls only'
        $sourceResult.rt2 = '20/20 bounded tests Pass; all-path admission Blocked'
        $sourceResult.nativeTrials = 58
    } catch {
        $sourceResult.status = $(if ($sourceResult.phase -eq 'Preparation') { 'Blocked: candidate preparation did not qualify' } else { 'Failed: synthetic trials did not pass' })
        $sourceResult.exceptionType = $_.Exception.GetType().FullName
        Write-Warning "Source profile stopped during $($sourceResult.phase): $($_.Exception.Message). No different bytes or release equivalence substituted."
    }
    $sourceResult.inputVerification = @(
        Test-RuntimeArtifact 'source-built-package' (Join-Path $primary '.candidate\feed\GitHub.Copilot.SDK.1.0.16-rt1.source.f8ae645.1.nupkg') $profiles[0].packageSha256
        Test-RuntimeArtifact 'source-built-assembly' (Join-Path $primary '.candidate\source\copilot-sdk-f8ae645902b74b62cd47aac1fd9b29adaec3aff2\dotnet\src\bin\Release\net10.0\GitHub.Copilot.SDK.dll') $profiles[0].assemblySha256
    )
    }
    Write-ProofJson $sourceResult (Join-Path $output 'source-outcome.json')

    Write-Host 'RELEASED PROFILE: separately pinned SDK 1.0.16/native 1.0.90; independent released RT1 before MG1.'
    try {
        # Short owned copies avoid MAX_PATH failures without changing host policy,
        # fixture control code or the identity of restored packages.
        $shortRt1 = Join-Path $primary 'r02-dotnet-control-proof'
        Copy-Item -LiteralPath $rt1 -Destination $shortRt1 -Recurse
        Copy-Item -LiteralPath (Join-Path $stage 'experiments\r02-runtime-proof') -Destination (Join-Path $primary 'r02-runtime-proof') -Recurse
        $shortMg1 = Join-Path $primary 'm'
        Copy-Item -LiteralPath $mg1 -Destination $shortMg1 -Recurse
        Assert-NoLinks $shortMg1
        $repositoryLiteral = "'" + $repository.Replace("'", "''") + "'"
        Set-StagedRouting (Join-Path $shortMg1 'Prepare-Fixture.ps1') 'git -C $PSScriptRoot rev-parse --show-toplevel' ('git -C ' + $repositoryLiteral + ' rev-parse --show-toplevel')
        Set-StagedRouting (Join-Path $shortMg1 'Test-Inputs.ps1') 'git -C $PSScriptRoot rev-parse HEAD' ('git -C ' + $repositoryLiteral + ' rev-parse HEAD')
        if ($VerifiedArchiveDirectory) {
            $cache = Join-Path $shortRt1 '.candidate'
            New-ProofDirectory $cache
            $inputs = Read-RuntimeReceipt (Join-Path $repository 'experiments\r02-dotnet-management-proof\evidence\input-verification.json')
            foreach ($input in @(
                @{ name = 'source-archive'; file = 'sdk-source.zip' },
                @{ name = 'runtime-archive'; file = 'runtime.tgz' }
            )) {
                $hash = @($inputs.checks | Where-Object name -CEQ $input.name)[0].expectedSha256
                $path = Join-Path $VerifiedArchiveDirectory $input.file
                $check = Test-RuntimeArtifact $input.name $path $hash
                if ($check.status -cne 'Pass') { throw 'Explicitly supplied archive does not match the retained approved identity.' }
                Copy-Item -LiteralPath $path -Destination (Join-Path $cache $input.file)
            }
        }
        $mg1 = $shortMg1
        & (Join-Path $mg1 'Prepare-Fixture.ps1') -PackageConfigPath $configPath
        $releasedResult.phase = 'Released RT1 and MG1 trials'
        & (Join-Path $mg1 'Run-Proof.ps1') -NoRestore
        $releasedResult.status = 'Pass: released synthetic controls/envelope only'
        $releasedResult.rt1 = '45/45 released regressions; expected hook-only FAIL retained'
        $releasedResult.mg1 = '22/22 host + 16/16 actual runtime cases'
        $releasedResult.nativeTrials = 61
        $releasedResult.evidenceSha256 = @(Export-SyntheticReleasedEvidence $mg1 $output $started $profiles[1])
    } catch {
        $releasedResult.status = $(if ($releasedResult.phase -eq 'Preparation') { 'Blocked: candidate preparation did not qualify' } else { 'Failed: synthetic trials did not pass' })
        $releasedResult.exceptionType = $_.Exception.GetType().FullName
        Write-Warning "Released profile stopped during $($releasedResult.phase): $($_.Exception.Message). No alternative SDK/runtime selected."
    }
    Write-ProofJson $releasedResult (Join-Path $output 'released-outcome.json')
} finally {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $telemetry
    $env:TESTINGPLATFORM_TELEMETRY_OPTOUT = $testTelemetry
    Pop-Location
    foreach ($path in @($sourceConfig, $rt2Config)) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
}
$after = Assert-RuntimePreparationCheckout $repository
if (($identity | ConvertTo-Json -Depth 5 -Compress) -cne ($after | ConvertTo-Json -Depth 5 -Compress)) {
    throw 'Original historical files/HEAD changed during synthetic trials.'
}
Write-ProofJson ([ordered]@{
    schemaVersion = 1; startedUtc = $started.ToString('O'); observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    base = $identity.base; head = $identity.head; profiles = $profiles
    fixtureSnapshotSha256 = (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
    stagingRelativeDirectory = [IO.Path]::GetRelativePath($repository, $stage)
    ownedTemporaryStagingLeaf = (Split-Path $primary -Leaf)
    stagedRoutingChanges = @($routingChanges); source = $sourceResult; released = $releasedResult
    historicalEvidenceUnchanged = $true
    historicalFiles = $identity.historicalFiles
    coordinatorSha256 = (Get-FileHash -LiteralPath $PSCommandPath).Hash.ToLowerInvariant()
    scope = 'Explicitly approved pinned acquisition and bounded synthetic native trials only; no privileged tracing, account or live provider use'
    stagingRetainedForDiagnosis = $true; cleanup = 'Individual trial runners require owned cleanup; staging/dependency inputs retained separately for reproduction'
    allPathRt2 = 'Blocked'; pv1 = 'Not run'; r08 = 'Blocked'; r13ModelAssisted = 'Blocked'
    physicalComputationTermination = 'Unknown'; sourceReleasedByteEquivalence = 'Not claimed'
    privilegedTraceSessions = 0; liveProviderCalls = 0; productionEnabled = $false
}) (Join-Path $output 'synthetic-readiness.json')
if ((!$ReleasedOnly -and $sourceResult.status -notlike 'Pass:*') -or $releasedResult.status -notlike 'Pass:*') {
    throw 'One or more synthetic profiles failed or were blocked. Inspect the distinct redacted outcome receipts; no gate was closed.'
}
Write-Warning 'Synthetic regressions completed. All-path RT2, PV1 and production admission remain Blocked; expected exit 2.'
exit 2
