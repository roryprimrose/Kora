#requires -Version 7.5
[CmdletBinding()]
param([Parameter(Mandatory)][string] $OutputDirectory)
. (Join-Path $PSScriptRoot 'RuntimeValidation.Common.ps1')
New-ProofDirectory $OutputDirectory
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$passed = [Collections.Generic.List[string]]::new()
function Check {
    param([bool] $Condition, [string] $Name)
    if (!$Condition) { throw $Name }
    $passed.Add($Name)
}
function Reject {
    param([string] $Name, [scriptblock] $Operation)
    $failure = $null
    try { & $Operation | Out-Null } catch { $failure = $_ }
    Check ($null -ne $failure) $Name
}

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$profiles = @(Get-RuntimeHistoricalProfiles $repository)
Check ($profiles.Count -eq 2 -and $profiles[0].packageSha256 -cne $profiles[1].packageSha256 -and
    $profiles[0].assemblySha256 -cne $profiles[1].assemblySha256) 'Source-built and released SDK identities stay separate'
Check ($profiles[0].nativeLauncherSha256 -ceq $profiles[1].nativeLauncherSha256 -and
    $profiles[0].nativePayloadSha256 -ceq $profiles[1].nativePayloadSha256) 'Shared native identity is recorded without SDK equivalence'
Reject 'Historical receipts cannot be exported as fresh native validation' {
    Export-SyntheticReleasedEvidence (Join-Path $repository 'experiments\r02-dotnet-management-proof') $OutputDirectory ([DateTimeOffset]::MaxValue) $profiles[1]
}
Check (!(Test-Path -LiteralPath (Join-Path $OutputDirectory 'released-evidence'))) 'Rejected stale evidence creates no export files'
$source = Read-RuntimeReceipt (Join-Path $repository 'experiments\r02-dotnet-control-proof\evidence\results.json')
Assert-RuntimeRegressionRows $source 'source'
Check $true 'All 45 historical controls include the expected rejected hook-only FAIL'
$bad = $source | ConvertTo-Json -Depth 30 | ConvertFrom-Json -AsHashtable -DateKind String
$bad.rows = @($bad.rows | Select-Object -Skip 1)
Reject 'Filtered historical controls are not accepted' { Assert-RuntimeRegressionRows $bad 'filtered' }
$bad.rows = @($source.rows | ForEach-Object { $_.Clone() })
@($bad.rows | Where-Object id -EQ 'hook-only-failure-witness')[0].status = 'PASS'
Reject 'Historical hook-only FAIL cannot be relabelled Pass' { Assert-RuntimeRegressionRows $bad 'relabeled' }
$bad.rows = @($source.rows | ForEach-Object { $_.Clone() })
$bad.rows[1].id = $bad.rows[0].id
Reject 'Duplicate trial identity is not complete evidence' { Assert-RuntimeRegressionRows $bad 'duplicate' }

$path = Join-Path $OutputDirectory 'synthetic.bin'
[IO.File]::WriteAllBytes($path, [Text.Encoding]::UTF8.GetBytes('synthetic contract input, never executable'))
$hash = (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
Check ((Test-RuntimeArtifact 'missing' $null $hash).status -ceq 'Blocked') 'Missing inputs stay Blocked without acquisition'
Check ((Test-RuntimeArtifact 'missing' (Join-Path $OutputDirectory 'absent.bin') $hash).status -ceq 'Failed') 'Explicit nonexistent inputs fail rather than produce successful preparation'
Check ((Test-RuntimeArtifact 'exact' $path $hash).status -ceq 'Pass') 'Exact input bytes pass without execution'
Check ((Test-RuntimeArtifact 'changed' $path ('0' * 64)).status -ceq 'Failed') 'Changed input bytes fail rather than produce successful preparation'
Check (!(Test-RuntimeArtifact 'exact' $path $hash).Contains('path')) 'Artifact receipt excludes supplied absolute paths'

$archive = Join-Path $OutputDirectory 'synthetic.nupkg'
$zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
try {
    $entry = $zip.CreateEntry('lib/net10.0/GitHub.Copilot.SDK.dll')
    $stream = $entry.Open()
    try { $bytes = [IO.File]::ReadAllBytes($path); $stream.Write($bytes, 0, $bytes.Length) }
    finally { $stream.Dispose() }
} finally { $zip.Dispose() }
$packageHash = (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
Check ((Test-RuntimeArtifact 'package' $archive $packageHash $hash).status -ceq 'Pass') 'Package and embedded assembly hashes are independently checked'
Check ((Test-RuntimeArtifact 'package' $archive $packageHash ('0' * 64)).status -ceq 'Failed') 'Correct package hash alone cannot admit a wrong assembly'
$zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Update)
try { $null = $zip.CreateEntry('lib/net10.0/GitHub.Copilot.SDK.dll') }
finally { $zip.Dispose() }
$packageHash = (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
Reject 'Duplicate assembly entries fail explicitly' { Test-RuntimeArtifact 'duplicate' $archive $packageHash $hash }
Reject 'Contracts never overwrite an existing output directory' { New-ProofDirectory $OutputDirectory }
Reject 'Preparation cannot write receipts into its repository' {
    & (Join-Path $PSScriptRoot 'Prepare-RuntimeValidation.ps1') -OutputDirectory $repository
}
$mainLike = Join-Path $OutputDirectory 'not-a-linked-worktree'
New-Item -ItemType Directory -Path $mainLike | Out-Null
Reject 'A non-linked checkout cannot run preparation' { Assert-RuntimePreparationCheckout $mainLike }
Remove-Item -LiteralPath $mainLike

$stages = @(Get-RuntimeValidationStages)
Check (@($stages | Where-Object { $_.id -ne 'offline-preparation' -and $_.id -ne 'r13-deterministic-core' -and $_.status -cne 'Blocked' }).Count -eq 0) `
    'Passing preparation cannot admit any native, account or production stage'
Check (($stages | Where-Object id -EQ 'r13-deterministic-core').status -ceq 'Independent') 'Deterministic R13 remains independent of hosted gates'
$requests = Get-RuntimeOperatorRequests
Check (!$requests.approvalGranted -and !$requests.rt2.collectorReviewed -and $null -eq $requests.pv1.provider) 'Generated request is not an approval or provider selection'
Check ($requests.rt2.maximumTrialSeconds -eq 60) 'Dedicated-host trial bound remains 60 seconds'
Check ($requests.pv1.managementLimits.serializedInputUtf8Bytes -eq 32768 -and
    $requests.pv1.managementLimits.completeTypedOutputUtf8Bytes -eq 4096 -and
    $requests.pv1.managementLimits.dispatchDeadlineMilliseconds -eq 15000 -and
    $requests.pv1.managementLimits.inFlight -eq 1 -and
    $requests.pv1.managementLimits.attemptsPerRollingHour -eq 30 -and
    $requests.pv1.managementLimits.forwardedAttemptsPerRequest -eq 1 -and
    $requests.pv1.managementLimits.unknownTerminationQuarantined) 'MG1 complete bounds and Unknown quarantine are retained'
Check ($null -eq $requests.pv1.budget.maximumTrialSpend -and
    $null -eq $requests.pv1.budget.enforceableProviderCap -and
    $requests.pv1.budget.rule.Contains('Alerts are not a hard cap')) 'No guessed cost ceiling or alert-only spending admission'

$culture = [Threading.Thread]::CurrentThread.CurrentCulture
try {
    foreach ($name in @('en-AU', 'en-US')) {
        [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo($name)
        $receipt = '{"observedAtUtc":"2026-10-07T09:21:13.382+00:00"}' | ConvertFrom-Json -AsHashtable -DateKind String
        $timestamp = [DateTimeOffset]::Parse($receipt.observedAtUtc, [Globalization.CultureInfo]::InvariantCulture)
        Check ($receipt.observedAtUtc -is [string] -and $timestamp.Month -eq 10 -and $timestamp.Day -eq 7 -and
            $timestamp.Offset -eq [TimeSpan]::Zero) "UTC receipt stays invariant in $name"
    }
} finally { [Threading.Thread]::CurrentThread.CurrentCulture = $culture }

Reject 'Synthetic native coordinator cannot run without explicit approval' {
    & (Join-Path $PSScriptRoot 'Invoke-SyntheticRuntimeValidation.ps1') -OutputDirectory $OutputDirectory -PackageConfigPath $path
}
Reject 'Released RT2 requires its own explicit bounded-profile approval' {
    & (Join-Path $PSScriptRoot 'Invoke-ReleasedRuntimeLifecycle.ps1') -PreparedReleasedFixtureRoot $repository -OutputDirectory $OutputDirectory -PackageConfigPath $path
}
$releasedTrials = @(1..13 | ForEach-Object {
    @{
        id = "synthetic-$_"; trial = 'PASS'; startedUtc = '2026-10-07T12:00:01Z'
        cleanupCompleted = 1; liveObservedOwnedProcessesAfterShutdown = 0
        deniedMarkersAtProvider = 0; credentialInModelBody = 0
        observation = @{ fileOverflow = 0; queryGaps = @(); processes = @(@{ id = $_ }) }
        sdkAssemblySha256 = $profiles[1].assemblySha256
        nativeLauncherSha256 = $profiles[1].nativeLauncherSha256; nativePayloadSha256 = $profiles[1].nativePayloadSha256
        fixtureAssemblySha256 = 'a' * 64; profile = $script:ReleasedRuntimeLifecycleProfile
        rt2 = 'BLOCKED: bounded fixture is not all-path admission'
    }
})
$positive = @{
    status = 'PASS'; syntheticOnly = $true; startedUtc = '2026-10-07T12:00:01Z'
    markerFileObservedBeforeDeletion = @(@{ deniedMarker = $true; credentialMarker = $true })
    observation = @{
        fileOverflow = 0; queryGaps = @(); socketSnapshots = @(@{ state = 5 })
        managedDiagnosticEventCounts = @{ synthetic = 1 }
    }
}
$started = [DateTimeOffset]::Parse('2026-10-07T12:00:00Z', [Globalization.CultureInfo]::InvariantCulture)
Assert-ReleasedRuntimeLifecycleReceipts $releasedTrials $positive $started $profiles[1]
Check $true 'Complete released bounded receipts preserve all-path Blocked'
Reject 'Released lifecycle cannot reuse stale receipts' {
    Assert-ReleasedRuntimeLifecycleReceipts $releasedTrials $positive $started.AddMinutes(1) $profiles[1]
}
foreach ($mutation in @(
    @{ field = 'id'; value = 'synthetic-2'; name = 'Duplicate lifecycle identity' }
    @{ field = 'profile'; value = 'source-built'; name = 'Source lifecycle mislabeled as released' }
    @{ field = 'sdkAssemblySha256'; value = $profiles[0].assemblySha256; name = 'Source assembly substituted into released receipts' }
    @{ field = 'nativePayloadSha256'; value = '0' * 64; name = 'Changed native payload' }
    @{ field = 'fixtureAssemblySha256'; value = 'b' * 64; name = 'Mixed fixture assemblies' }
    @{ field = 'cleanupCompleted'; value = 0; name = 'Missing owned cleanup' }
    @{ field = 'liveObservedOwnedProcessesAfterShutdown'; value = 1; name = 'Observed owned survivor' }
    @{ field = 'credentialInModelBody'; value = 1; name = 'Credential marker at model boundary' }
    @{ field = 'deniedMarkersAtProvider'; value = 1; name = 'Denied model marker' }
    @{ field = 'rt2'; value = 'PASS'; name = 'Bounded receipt promoting all-path admission' }
)) {
    $changed = $releasedTrials | ConvertTo-Json -Depth 10 | ConvertFrom-Json -AsHashtable -DateKind String
    $changed[0][$mutation.field] = $mutation.value
    Reject "$($mutation.name) rejects released RT2" { Assert-ReleasedRuntimeLifecycleReceipts $changed $positive $started $profiles[1] }
}
foreach ($mutation in @(
    @{ field = 'fileOverflow'; value = 1; name = 'Watcher loss' }
    @{ field = 'queryGaps'; value = @('synthetic query failure'); name = 'Recorded observation query gap' }
)) {
    $changed = $releasedTrials | ConvertTo-Json -Depth 10 | ConvertFrom-Json -AsHashtable -DateKind String
    $changed[0].observation[$mutation.field] = $mutation.value
    Reject "$($mutation.name) rejects released RT2" { Assert-ReleasedRuntimeLifecycleReceipts $changed $positive $started $profiles[1] }
}
$changedPositive = $positive | ConvertTo-Json -Depth 10 | ConvertFrom-Json -AsHashtable -DateKind String
$changedPositive.observation.socketSnapshots = @()
Reject 'Missing live socket positive control rejects released RT2' {
    Assert-ReleasedRuntimeLifecycleReceipts $releasedTrials $changedPositive $started $profiles[1]
}
foreach ($name in @('RuntimeValidation.Common.ps1', 'Prepare-RuntimeValidation.ps1', 'Test-RuntimeValidationContracts.ps1',
    'Invoke-SyntheticRuntimeValidation.ps1', 'Invoke-ReleasedRuntimeLifecycle.ps1')) {
    $tokens = $null
    $errors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name), [ref]$tokens, [ref]$errors)
    Check ($errors.Count -eq 0) "PowerShell syntax: $name"
}
Write-ProofJson ([ordered]@{
    observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); status = 'Pass'
    total = $passed.Count; passed = $passed.Count; failed = 0; checks = @($passed)
    scope = 'Deterministic preparation contracts only; not native runtime, provider or privacy acceptance'
}) (Join-Path $OutputDirectory 'contracts.json')
foreach ($name in @('synthetic.bin', 'synthetic.nupkg')) {
    Remove-Item -LiteralPath (Join-Path $OutputDirectory $name)
}
Write-Host "$($passed.Count)/$($passed.Count) deterministic runtime preparation contracts passed."
