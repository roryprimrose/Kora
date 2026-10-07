#requires -Version 7.5
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PreparedReleasedFixtureRoot,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [Parameter(Mandatory)][string] $PackageConfigPath,
    [switch] $ApproveReleasedBoundedTrials
)
. (Join-Path $PSScriptRoot 'RuntimeValidation.Common.ps1')
if (!$ApproveReleasedBoundedTrials) { throw 'Separate operator approval for released-profile bounded RT2 trials is required.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
$config = [IO.Path]::GetFullPath($PackageConfigPath)
foreach ($path in @($output, $config)) {
    if ($path.Equals($repository, [StringComparison]::OrdinalIgnoreCase) -or
        $path.StartsWith($repository + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Receipts and machine package configuration must remain outside the repository.'
    }
}
Assert-NoLinks $config -AncestorsOnly
Assert-NoLinks $PreparedReleasedFixtureRoot
$identity = Assert-RuntimePreparationCheckout $repository
$profile = @(Get-RuntimeHistoricalProfiles $repository)[1]
$regression = Read-RuntimeReceipt (Join-Path $PreparedReleasedFixtureRoot 'evidence\rt1-released-regression.json')
Assert-RuntimeRegressionRows $regression 'approved released RT1 prerequisite'
$disposition = Read-RuntimeReceipt (Join-Path $PreparedReleasedFixtureRoot 'evidence\disposition.json')
if ($disposition.tests.rt1ReleasedRegression.passed -ne 45 -or
    $disposition.tests.hostComponents.passed -ne 22 -or $disposition.tests.actualRuntime.passed -ne 16 -or
    $regression.sdkPackageSha256 -cne $profile.packageSha256 -or
    $regression.sdkAssemblySha256 -cne $profile.assemblySha256) {
    throw 'The explicit prepared fixture has not qualified the separate released RT1/MG1 profile.'
}
$runtimeReport = Read-RuntimeReceipt (Join-Path $PreparedReleasedFixtureRoot 'evidence\runtime-results.json')
if ($runtimeReport.sdkPackageSha256 -cne $profile.packageSha256 -or
    $runtimeReport.sdkAssemblySha256 -cne $profile.assemblySha256 -or
    $runtimeReport.nativeLauncherSha256 -cne $profile.nativeLauncherSha256 -or
    $runtimeReport.nativePayloadSha256 -cne $profile.nativePayloadSha256 -or
    @($runtimeReport.rows).Count -ne 16 -or @($runtimeReport.rows.id | Sort-Object -Unique).Count -ne 16 -or
    @($runtimeReport.rows | Where-Object { $_.status -cne 'Pass' -or $_.counts.ownedCleanupCompleted -ne 1 }).Count -ne 0 -or
    @($regression.rows | Where-Object { $_.counters.ownedCleanupCompleted -ne 1 }).Count -ne 0) {
    throw 'Released RT1/MG1 prerequisite receipts must be complete, uniquely identified, cleaned and match approved byte pins.'
}
$package = Join-Path $PreparedReleasedFixtureRoot '.inputs\GitHub.Copilot.SDK.1.0.16.nupkg'
$packageCheck = Test-RuntimeArtifact 'released-sdk' $package $profile.packageSha256 $profile.assemblySha256
if ($packageCheck.status -cne 'Pass') { throw 'Released SDK input no longer matches approved bytes.' }
New-ProofDirectory $output
$owned = Join-Path ([IO.Path]::GetTempPath()) ('rt2-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-ProofDirectory $owned
$fixture = Join-Path $owned 'l'
$control = Join-Path $owned 'c'
New-ProofDirectory $fixture
New-ProofDirectory $control
$started = [DateTimeOffset]::UtcNow
Write-Host ("Retaining explicitly owned fixture for diagnosis: " + $owned)
$files = [Collections.Generic.List[object]]::new()
foreach ($name in @('Candidate.cs', 'RuntimeFixture.cs', 'RequestBoundary.cs', 'VolatileSessionFs.cs', 'SyntheticProvider.cs')) {
    $source = Join-Path $repository "experiments\r02-dotnet-control-proof\$name"
    $destination = Join-Path $control $name
    Copy-Item -LiteralPath $source -Destination $destination
    if ($name -eq 'Candidate.cs') {
        $text = [IO.File]::ReadAllText($destination)
        $text = $text.Replace('1.0.16-rt1.source.f8ae645.1', '1.0.16').
            Replace('afb0715225f794d1b663ef72da1336b44b665e50b330087e1356380a5eac10d6', $profile.assemblySha256).
            Replace('0b609b73c868099d64e7d5320b927d760d528756ab7c5db0b5692f5dccbf9b3f', $profile.packageSha256)
        [IO.File]::WriteAllText($destination, $text)
    }
    $expected = @($regression.fixtureSources | Where-Object file -CEQ $name)
    $actual = (Get-FileHash -LiteralPath $destination).Hash.ToLowerInvariant()
    if ($expected.Count -ne 1 -or $expected[0].sha256 -cne $actual) {
        throw 'Released lifecycle control code differs from the completed released RT1 prerequisite.'
    }
    $files.Add([ordered]@{ group = 'released-rt1-controls'; file = $name; sha256 = $actual })
}
$native = Join-Path $control '.candidate\runtime'
$feed = Join-Path $control '.candidate\feed'
New-ProofDirectory $native
New-ProofDirectory $feed
Copy-Item -LiteralPath $package -Destination (Join-Path $feed 'GitHub.Copilot.SDK.1.0.16.nupkg')
foreach ($name in @('copilot-runtime.exe', 'runtime.node', 'LICENSE.md')) {
    Copy-Item -LiteralPath (Join-Path $PreparedReleasedFixtureRoot ".inputs\runtime\$name") -Destination (Join-Path $native $name)
}
$nativeChecks = @(
    Test-RuntimeArtifact 'native-launcher' (Join-Path $native 'copilot-runtime.exe') $profile.nativeLauncherSha256
    Test-RuntimeArtifact 'native-payload' (Join-Path $native 'runtime.node') $profile.nativePayloadSha256
)
if (@($nativeChecks | Where-Object { $_.status -cne 'Pass' }).Count -ne 0) { throw 'Released native inputs differ from approved bytes.' }
$originalRt2 = Join-Path $repository 'experiments\r02-runtime-lifecycle-proof'
foreach ($name in @('DiagnosticObserver.cs', 'LifecycleObserver.cs', 'LifecycleTests.cs', 'NativeSnapshot.cs', 'ObserverTests.cs',
    'Directory.Build.props', 'LifecycleProof.csproj')) {
    Copy-Item -LiteralPath (Join-Path $originalRt2 $name) -Destination (Join-Path $fixture $name)
}
$trialSource = Join-Path $fixture 'LifecycleTests.cs'
$text = [IO.File]::ReadAllText($trialSource)
$old = 'unchanged linked RT1 minimal HTTP/stdio profile, approved primary source-built bytes'
if ([regex]::Matches($text, [regex]::Escape($old)).Count -ne 1) { throw 'Unexpected lifecycle profile label; do not relabel unknown controls.' }
[IO.File]::WriteAllText($trialSource, $text.Replace($old, $script:ReleasedRuntimeLifecycleProfile))
$project = Join-Path $fixture 'LifecycleProof.csproj'
[xml] $xml = Get-Content -LiteralPath $project -Raw
foreach ($reference in $xml.Project.ItemGroup.PackageReference) { $reference.RemoveAttribute('Version') }
$xml.Save($project)
$centralSource = Join-Path $repository 'experiments\r02-dotnet-management-proof\Directory.Packages.props'
$centralExpected = @($regression.fixtureSources | Where-Object file -CEQ 'Directory.Packages.props')
if ($centralExpected.Count -ne 1 -or (Get-FileHash -LiteralPath $centralSource).Hash.ToLowerInvariant() -cne $centralExpected[0].sha256) {
    throw 'Released RT2 central package metadata differs from the completed released RT1 prerequisite.'
}
Copy-Item -LiteralPath $centralSource -Destination (Join-Path $fixture 'Directory.Packages.props')
$releasedControl = Join-Path $PreparedReleasedFixtureRoot '.regression\experiments\r02-dotnet-control-proof'
Copy-Item -LiteralPath (Join-Path $releasedControl 'packages.lock.json') -Destination (Join-Path $fixture 'packages.lock.json')
New-ProofDirectory (Join-Path $fixture '.candidate')
$escaped = [Security.SecurityElement]::Escape($control)
"<Project><PropertyGroup><Rt1Stage>$escaped</Rt1Stage></PropertyGroup></Project>" |
    Set-Content -LiteralPath (Join-Path $fixture '.candidate\Stage.props') -Encoding utf8NoBOM
foreach ($file in Get-ChildItem -LiteralPath $fixture -File) {
    $files.Add([ordered]@{ group = 'released-rt2-observers'; file = $file.Name; sha256 = (Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant() })
}
$results = Join-Path $output 'tests'
$cache = [IO.Path]::GetFullPath((Join-Path $PreparedReleasedFixtureRoot '..\r02-dotnet-control-proof\.candidate\packages'))
$telemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$testTelemetry = $env:TESTINGPLATFORM_TELEMETRY_OPTOUT
Push-Location $repository
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
    if ((& dotnet --version) -cne '10.0.401') { throw 'Released RT2 requires exact SDK 10.0.401.' }
    & dotnet restore $project --locked-mode --configfile $config --packages $cache | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Released RT2 closure differs or locked approved-feed restore failed; no lock update or feed fallback.' }
    & dotnet build $project --configuration Release --no-restore | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Released RT2 build failed.' }
    New-ProofDirectory $results
    & dotnet test --project $project --configuration Release --no-build --results-directory $results --report-trx | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Released RT2 bounded tests failed; no passing receipt may be recorded.' }
} finally {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $telemetry
    $env:TESTINGPLATFORM_TELEMETRY_OPTOUT = $testTelemetry
    Pop-Location
}
[xml] $trx = Get-Content -LiteralPath (Join-Path $results 'LifecycleProof_net10.0_x64.trx') -Raw
$counts = $trx.TestRun.ResultSummary.Counters
if ([int]$counts.total -ne 20 -or [int]$counts.passed -ne 20 -or
    [int]$counts.failed -ne 0 -or [int]$counts.notExecuted -ne 0) { throw 'All 20 released lifecycle/control/observer cases must pass.' }
$trialDirectory = Join-Path $fixture 'evidence\trials'
$trials = @(Get-ChildItem -LiteralPath $trialDirectory -Filter '*.json' -File | ForEach-Object { Read-RuntimeReceipt $_.FullName })
$positive = Read-RuntimeReceipt (Join-Path $fixture 'evidence\positive-controls.json')
Assert-ReleasedRuntimeLifecycleReceipts $trials $positive $started $profile
foreach ($scratch in @((Join-Path $fixture '.scratch'), (Join-Path $control '.scratch'))) {
    if (Test-Path -LiteralPath $scratch) {
        Assert-NoLinks $scratch
        if (@(Get-ChildItem -LiteralPath $scratch -Force).Count -ne 0) { throw 'Released RT2 left owned trial scratch; cleanup is not confirmed.' }
    }
}
$after = Assert-RuntimePreparationCheckout $repository
if (($identity | ConvertTo-Json -Depth 5 -Compress) -cne ($after | ConvertTo-Json -Depth 5 -Compress)) {
    throw 'Historical fixtures/evidence or source HEAD changed during released RT2.'
}
Copy-Item -LiteralPath (Join-Path $fixture 'evidence') -Destination (Join-Path $output 'evidence') -Recurse
$exportedEvidence = Get-PayloadFiles (Join-Path $fixture 'evidence')
Assert-Payload (Join-Path $output 'evidence') @{ files = $exportedEvidence }
$managedDependencies = @(
    Get-ChildItem -LiteralPath (Join-Path $fixture 'bin\Release\net10.0') -Filter '*.dll' -File |
        Sort-Object Name | ForEach-Object {
            [ordered]@{ file = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() }
        }
)
Write-ProofJson ([ordered]@{
    observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); startedUtc = $started.ToString('O')
    base = $identity.base; head = $identity.head; profile = $profile
    boundedReleasedRt2 = 'Pass: 20/20 (13 actual lifecycle, six deterministic observers, one positive control)'
    tests = @{ total = 20; passed = 20; failed = 0; skipped = 0 }; realRuntimeTrials = 13
    sdkPackageCheck = $packageCheck; nativeChecks = $nativeChecks
    providerRequests = ($trials | Measure-Object providerRequests -Sum).Sum
    deniedMarkersAtProvider = 0; credentialInModelBody = 0; watcherOverflows = 0; recordedQueryGaps = 0
    observedOwnedProcesses = @($trials.observation.processes).Count
    observedOwnedDescendants = @($trials.observation.processes | Where-Object role -CEQ 'descendant').Count
    unreviewedHelperObservations = @($trials.observation.processes | Where-Object imageClass -CEQ 'unreviewed-helper').Count
    maximumSampleGapMs = ($trials.observation | Measure-Object maximumSampleGapMs -Maximum).Maximum
    sampledOwnedSurvivorsAfterShutdown = 0; ownedCleanup = 'All 13 trial receipts confirmed cleanup'
    fixtureInputs = @($files); fixtureAssemblySha256 = @($trials.fixtureAssemblySha256 | Sort-Object -Unique)
    deployedManagedDependencies = $managedDependencies; exportedEvidence = $exportedEvidence
    coordinatorSha256 = (Get-FileHash -LiteralPath $PSCommandPath).Hash.ToLowerInvariant()
    ownedTemporaryStagingLeaf = (Split-Path $owned -Leaf)
    historicalEvidenceUnchanged = $true
    historicalSourceRt2 = 'Separate bounded Pass retained; current source rebuild/reproduction blocker unchanged'
    allPathRt2 = 'Blocked: unobserved/unmediated native network, filesystem, diagnostics and descendants'
    privilegedTraceSessions = 0; liveProviderCalls = 0; pv1 = 'Not run'
    sourceReleasedByteEquivalence = 'Not claimed'; physicalComputationTermination = 'Unknown'
    r08 = 'Blocked'; r13ModelAssisted = 'Blocked'; productionEnabled = $false
}) (Join-Path $output 'released-rt2-readiness.json')
Write-Warning 'Released-profile bounded RT2 PASS; all-path RT2/PV1/production remain Blocked. Expected exit 2.'
exit 2
