#requires -Version 7.5
[CmdletBinding()]
param([Parameter(Mandatory)][string] $OutputDirectory)
. (Join-Path $PSScriptRoot 'Coordinator.Common.ps1')
New-ProofDirectory $OutputDirectory
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$entry = Join-Path $PSScriptRoot 'Invoke-Coordinator.ps1'
$passed = [Collections.Generic.List[string]]::new()
$script:sequence = 0
$script:repositoryPath = ''
$script:state = $null

function Check {
    param([bool] $Condition, [string] $Name)
    if (!$Condition) { throw "Failed: $Name" }
    $passed.Add($Name)
}

function Invoke-EntryProcess {
    param([string] $Action, $Request = @{}, [switch] $Recovery, [string] $Leader = 'fixture-leader', $Fence = $script:state)
    $script:sequence++
    $prefix = Join-Path $OutputDirectory "process-$script:sequence"
    $requestPath = "$prefix.request.json"
    Write-ProofJson $Request $requestPath
    $arguments = @('-NoProfile', '-File', $entry, '-RepositoryPath', $script:repositoryPath, '-Action', $Action,
        '-Session', $Leader, '-RequestPath', $requestPath)
    if ($Recovery) { $arguments += '-RecoverEvidenceTerminalMetadata' }
    if ($null -ne $Fence) { $arguments += @('-ExpectedEpoch', [string]$Fence.epoch, '-ExpectedRevision', [string]$Fence.revision) }
    $info = [Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })))
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    if (!$process.Start()) { throw 'Fixture process did not start.' }
    return @{ process = $process; output = $process.StandardOutput.ReadToEndAsync()
        error = $process.StandardError.ReadToEndAsync(); prefix = $prefix; arguments = $arguments }
}

function Complete-Entry {
    param($Invocation, [string] $Failure = '', [switch] $Concurrent)
    try {
        $Invocation.process.WaitForExit()
        $stdout = $Invocation.output.GetAwaiter().GetResult()
        $stderr = $Invocation.error.GetAwaiter().GetResult()
        [IO.File]::WriteAllText(($Invocation.prefix + '.stdout.json'), $stdout)
        [IO.File]::WriteAllText(($Invocation.prefix + '.stderr.txt'), $stderr)
        Write-ProofJson @{ arguments = $Invocation.arguments; exitCode = $Invocation.process.ExitCode } ($Invocation.prefix + '.result.json')
        if ($Failure) {
            if ($Invocation.process.ExitCode -eq 0 -or !$stderr.Contains($Failure)) {
                throw "Expected '$Failure', got $($Invocation.process.ExitCode): $stderr $stdout"
            }
            return $null
        }
        if ($Concurrent -and $Invocation.process.ExitCode -eq 1 -and $stderr.Contains('Stale epoch/revision.')) {
            return @{ kind = 'stale-fence' }
        }
        if ($Invocation.process.ExitCode -ne 0) { throw "Entry failed: $stderr $stdout" }
        $result = $stdout | ConvertFrom-Json -AsHashtable -DateKind String
        if ($result.Contains('state')) { $script:state = $result.state }
        elseif ($result.Contains('revision')) { $script:state = @{ epoch = $result.epoch; revision = $result.revision } }
        return $result
    } finally { $Invocation.process.Dispose() }
}

function Entry {
    param([string] $Action, $Request = @{}, [switch] $Recovery, [string] $Failure = '',
        [string] $Leader = 'fixture-leader', $Fence = $script:state)
    return Complete-Entry (Invoke-EntryProcess $Action $Request -Recovery:$Recovery -Leader $Leader -Fence $Fence) $Failure
}

function Register-Owner {
    param([string] $Id, [string] $Kind = 'evidence', [string] $Allocation = 'none')
    Entry Register @{ ownerId = $Id; operation = $Id; scopes = @("eng/$Id"); kind = $Kind
        allocation = $Allocation; evidence = 'Offline fixture ownership; no real native session.' } | Out-Null
}

function Approve-FixtureOwner {
    param([string] $Id)
    Entry Certify @{ ownerId = $Id; repair = $false; certification = @{
        sourceHead = 'cb7e54403a9444a66683036330f779c32d51de16'; inputDigest = 'b' * 64
        prObservation = 'offline fixture'; observedUtc = [DateTimeOffset]::UtcNow.ToString('o')
        approvedOutcome = 'Existing coordinator evidence scheduling reliability'; placement = 'Host engineering fixture'
    }} | Out-Null
}

function Invoke-FixtureSetup {
    param([string] $Name, [string] $Policy = 'ready-auto', [string] $Kind = 'evidence')
    $script:repositoryPath = Join-Path $OutputDirectory $Name
    $script:state = $null
    New-ProofDirectory $script:repositoryPath
    Invoke-Checked git @('init', '--quiet', $script:repositoryPath)
    Invoke-Checked git @('-C', $script:repositoryPath, 'remote', 'add', 'origin', 'https://github.com/roryprimrose/Kora.git')
    '.scratch/' | Set-Content -LiteralPath (Join-Path $script:repositoryPath '.gitignore')
    Entry Initialize @{ legacyAbsenceConfirmed = $true; authorityId = 'offline-fixture-authority'
        publicationPolicy = $Policy; publisher = 'fixture-publisher' } | Out-Null
    Register-Owner 'unrelated-old-intent' -Kind feature -Allocation uncertain
    Register-Owner 'delivery-evidence' -Kind $Kind
    Approve-FixtureOwner 'delivery-evidence'
    $dispatch = Entry Next
    Entry Reconcile @{ executionId = $dispatch.action.executionId; outcome = 'accepted'
        dispatcherJoined = $true; nativeIdentity = 'agent-host-session://fixture?chat=fc7073eb'
        branch = 'main'; worktree = $script:repositoryPath; evidence = 'Offline exact native creation fixture' } | Out-Null
    $snapshot = (Entry Read).state
    $ownerDirectory = Join-Path (Join-Path $snapshot.identity.directory 'owners') 'delivery-evidence'
    $assessmentPath = Join-Path $ownerDirectory 'assessment-voice-output-epoch1-revision3.json'
    $pointerPath = Join-Path $ownerDirectory 'assessment.json'
    $assessment = [ordered]@{
        version = 1; datasetId = $snapshot.datasetId; ownerId = 'delivery-evidence'
        executionId = $dispatch.action.executionId; epoch = $snapshot.epoch; dispatchRevision = $dispatch.revision
        sourceHead = 'cb7e54403a9444a66683036330f779c32d51de16'
        workspaceHead = 'd23e2d617bea59ca87a4506a0ed74309ead137d5'; candidateCount = 0
        candidates = @(); summary = 'Offline evidence only'
    }
    Write-ProofJson $assessment $assessmentPath
    $hash = (Get-FileHash -LiteralPath $assessmentPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $pointer = [ordered]@{}
    foreach ($key in @('version', 'datasetId', 'ownerId', 'executionId', 'epoch', 'dispatchRevision', 'sourceHead')) {
        $pointer[$key] = $assessment[$key]
    }
    $pointer.revision = 1; $pointer.kind = 'assessment-pointer'; $pointer.assessmentPath = $assessmentPath
    $pointer.assessmentSha256 = $hash; $pointer.candidateCount = 0; $pointer.summary = 'Offline evidence only'
    Write-ProofJson $pointer $pointerPath
    $receipt = [ordered]@{
        version = 1; datasetId = $snapshot.datasetId; eventId = '3483ad2504cf4a30af7b24abe29db43d'
        ownerId = 'delivery-evidence'; executionId = $dispatch.action.executionId
        epoch = $snapshot.epoch; dispatchRevision = $dispatch.revision; revision = 1
        nativeIdentity = 'agent-host-session://fixture?chat=fc7073eb'; branch = 'main'; worktree = $script:repositoryPath
        head = $assessment.sourceHead; sourceHead = $assessment.sourceHead; workspaceHead = $assessment.workspaceHead
        kind = 'evidence-completed'; workState = 'stopped'; finiteCommandsJoined = $true
        runningCommands = @(); heldDecision = $null; publicationPolicy = $Policy; prUrl = $null; autoMerge = $null
        assessmentPath = $assessmentPath; assessmentSha256 = $hash; pointerPath = $pointerPath
        completedUtc = '2026-10-27T07:34:24.2829947+00:00'
    }
    $path = Join-Path $ownerDirectory 'terminal.json'
    Write-ProofJson $receipt $path
    return @{ path = $path; receipt = $receipt; snapshot = $snapshot; assessment = $assessment; pointer = $pointer }
}

try {
    $fixture = Invoke-FixtureSetup 'metadata-recovery'
    $request = @{ receiptPath = $fixture.path }
    $originalBytes = [IO.File]::ReadAllBytes($fixture.path)
    $chargedDigest = Get-CoordinatorDigest @($fixture.snapshot.executions | Where-Object ownerId -CEQ unrelated-old-intent)[0]
    $ownerDigest = Get-CoordinatorDigest (Get-CoordinatorOwner $fixture.snapshot 'unrelated-old-intent')
    Entry Consume $request -Failure 'Unknown field: epoch' | Out-Null
    Entry Read $request -Recovery -Failure 'only supported for Consume' | Out-Null
    Entry Consume $request -Recovery -Leader 'stale-leader' -Failure 'Current unexpired leader' | Out-Null
    Entry Consume $request -Recovery -Fence @{ epoch = 0; revision = $script:state.revision } -Failure 'Stale epoch/revision' | Out-Null
    Entry Consume $request -Recovery -Fence @{ epoch = $script:state.epoch; revision = 0 } -Failure 'Stale epoch/revision' | Out-Null
    Entry Consume @{ receiptPath = $fixture.path; publicationCorrection = @{} } -Recovery -Failure 'Unknown field' | Out-Null
    Check $true 'Strict default, action restriction, current leader/fence and no publication correction'

    $cases = @(
        @{ key = 'command'; value = 'must never execute'; failure = 'Unknown field' },
        @{ key = 'epoch'; value = 99; failure = 'epoch/dispatch binding mismatch' },
        @{ key = 'epoch'; value = '1'; failure = 'nonnegative integer' },
        @{ key = 'dispatchRevision'; value = 9999; failure = 'epoch/dispatch binding mismatch' },
        @{ key = 'workspaceHead'; value = 'invalid'; failure = 'source head' },
        @{ key = 'sourceHead'; value = 'a' * 40; failure = 'source/digest mismatch' },
        @{ key = 'assessmentSha256'; value = 'a' * 64; failure = 'assessmentSha256' },
        @{ key = 'completedUtc'; value = 'yesterday'; failure = 'String' },
        @{ key = 'datasetId'; value = [guid]::NewGuid().ToString(); failure = 'binding mismatch' },
        @{ key = 'executionId'; value = 'wrong-execution'; failure = 'execution/epoch/dispatch' },
        @{ key = 'ownerId'; value = 'unrelated-old-intent'; failure = 'binding mismatch' },
        @{ key = 'nativeIdentity'; value = 'foreign-child'; failure = 'identity/authority mismatch' },
        @{ key = 'branch'; value = 'foreign-branch'; failure = 'identity/authority mismatch' },
        @{ key = 'worktree'; value = $OutputDirectory; failure = 'identity/authority mismatch' },
        @{ key = 'head'; value = 'a' * 40; failure = 'source/digest mismatch' },
        @{ key = 'workState'; value = 'running'; failure = 'not proved quiescent' },
        @{ key = 'finiteCommandsJoined'; value = $false; failure = 'not proved quiescent' },
        @{ key = 'finiteCommandsJoined'; value = 'true'; failure = 'not proved quiescent' },
        @{ key = 'runningCommands'; value = @('unjoined'); failure = 'not proved quiescent' },
        @{ key = 'heldDecision'; value = 'pending'; failure = 'not proved quiescent' },
        @{ key = 'publicationPolicy'; value = 'local-only'; failure = 'identity/authority mismatch' },
        @{ key = 'kind'; value = 'pr-enrolled-ci-wait'; failure = 'without publication' },
        @{ key = 'prUrl'; value = 'https://github.com/roryprimrose/Kora/pull/1'; failure = 'without publication' },
        @{ key = 'autoMerge'; value = @{ method = 'SQUASH'; enabledAt = '2026-10-27T07:34:24.2829947+00:00' }; failure = 'without publication' }
    )
    $index = 0
    foreach ($case in $cases) {
        $index++
        $copy = $fixture.receipt | ConvertTo-Json -Depth 40 | ConvertFrom-Json -AsHashtable -DateKind String
        $copy[$case.key] = $case.value
        $path = Join-Path (Split-Path -Parent $fixture.path) "bad-$index.json"
        Write-ProofJson $copy $path
        Entry Consume @{ receiptPath = $path } -Recovery -Failure $case.failure | Out-Null
        Check (((Entry Read).state.executions | Where-Object phase -CNE released).Count -eq 2) "Rejected $($case.key) case $index preserves every charge"
    }
    $parent = Split-Path -Parent $fixture.path
    $duplicatePath = Join-Path $parent 'duplicate-property.json'
    $duplicateJson = [Text.Encoding]::UTF8.GetString($originalBytes).TrimEnd()
    $duplicateJson = $duplicateJson.Substring(0, $duplicateJson.Length - 1) + ',"epoch":1}'
    [IO.File]::WriteAllText($duplicatePath, $duplicateJson)
    Entry Consume @{ receiptPath = $duplicatePath } -Recovery -Failure 'Duplicate recovery input field' | Out-Null
    $missingMetadata = $fixture.receipt | ConvertTo-Json -Depth 40 | ConvertFrom-Json -AsHashtable -DateKind String
    $missingMetadata.Remove('workspaceHead')
    $missingPath = Join-Path $parent 'missing-metadata.json'
    Write-ProofJson $missingMetadata $missingPath
    Entry Consume @{ receiptPath = $missingPath } -Recovery -Failure 'Missing field: workspaceHead' | Out-Null
    Entry Consume @{ receiptPath = (Join-Path $parent '..\delivery-evidence\terminal.json') } -Recovery -Failure 'canonical registered' | Out-Null
    $outside = Join-Path $OutputDirectory 'outside.json'
    Write-ProofJson $fixture.assessment $outside
    $savedAssessmentPath = $fixture.receipt.assessmentPath
    $fixture.receipt.assessmentPath = $outside
    Write-ProofJson $fixture.receipt $fixture.path
    Entry Consume $request -Recovery -Failure 'canonical registered' | Out-Null
    $link = Join-Path $parent 'linked'
    New-Item -ItemType $(if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }) -Path $link -Target $OutputDirectory | Out-Null
    $fixture.receipt.assessmentPath = Join-Path $link 'outside.json'
    Write-ProofJson $fixture.receipt $fixture.path
    Entry Consume $request -Recovery -Failure 'canonical registered' | Out-Null
    $fixture.receipt.assessmentPath = $savedAssessmentPath
    [IO.File]::WriteAllBytes($fixture.path, $originalBytes)
    $linkFixture = Invoke-FixtureSetup 'reparse-proof'
    New-Item -ItemType $(if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }) `
        -Path (Join-Path $linkFixture.snapshot.identity.directory 'terminal-recovery') -Target $OutputDirectory | Out-Null
    Entry Consume @{ receiptPath = $linkFixture.path } -Recovery -Failure 'Linked/reparse' | Out-Null
    Check $true 'Canonical path traversal, out-of-owner metadata and reparse evidence root fail closed'

    $script:repositoryPath = $fixture.snapshot.identity.workspace
    $null = Entry Read
    $savedPointer = [IO.File]::ReadAllBytes($fixture.receipt.pointerPath)
    $fixture.pointer.epoch = 99
    Write-ProofJson $fixture.pointer $fixture.receipt.pointerPath
    Entry Consume $request -Recovery -Failure 'binding mismatch: epoch' | Out-Null
    [IO.File]::WriteAllBytes($fixture.receipt.pointerPath, $savedPointer)
    $savedAssessment = [IO.File]::ReadAllBytes($fixture.receipt.assessmentPath)
    [IO.File]::AppendAllText($fixture.receipt.assessmentPath, ' ')
    Entry Consume $request -Recovery -Failure 'assessmentSha256' | Out-Null
    [IO.File]::WriteAllBytes($fixture.receipt.assessmentPath, $savedAssessment)
    $lockPath = Join-Path $fixture.snapshot.identity.directory 'terminal-recovery.lock'
    $lock = [IO.FileStream]::new($lockPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try { Check ((Entry Consume $request -Recovery).reason -ceq 'terminal-recovery-lock-unavailable') 'Contending helper defers without breaking lock' }
    finally { $lock.Dispose() }
    $writer = [IO.FileStream]::new((Join-Path $fixture.snapshot.identity.directory 'writer.lock'),
        [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try { Check ((Entry Consume $request -Recovery).reason -ceq 'writer-lock-unavailable') 'Core writer lock remains authoritative' }
    finally { $writer.Dispose() }
    $fence = $script:state
    $one = Invoke-EntryProcess Consume $request -Recovery
    $two = Invoke-EntryProcess Consume $request -Recovery
    $results = @((Complete-Entry $one -Concurrent), (Complete-Entry $two -Concurrent))
    Check (@($results | Where-Object kind -CEQ released).Count -eq 1) 'Concurrent real processes release once'
    $released = @($results | Where-Object kind -CEQ released)[0]
    $successfulInvocation = if ($results[0].kind -ceq 'released') { $one } else { $two }
    $after = (Entry Read).state
    Check ($after.revision -eq $fence.revision + 1 -and
        @($after.executions | Where-Object phase -CNE released).Count -eq 1) 'One commit and exactly one unrelated charge remains'
    $projection = Read-CoordinatorJson $released.terminalRecovery.projectionPath
    Check ($projection.Count -eq 18) 'Projection contains only all eighteen authoritative fields'
    foreach ($key in $projection.Keys) {
        Check ((Get-CoordinatorDigest @{ value = $projection[$key] }) -ceq
            (Get-CoordinatorDigest @{ value = $fixture.receipt[$key] })) "Authoritative value preserved: $key"
    }
    $proof = Read-CoordinatorJson $released.terminalRecovery.proofPath -MaximumBytes 1048576
    Check ($proof.producer -ceq 'Coordinator.TerminalRecovery' -and
        $proof.originalBytesBase64 -ceq [Convert]::ToBase64String($originalBytes) -and
        $proof.originalSha256 -ceq (Get-FileHash -LiteralPath $fixture.path).Hash) 'Separate helper proof retains exact original bytes, digest and path'
    Check ([Convert]::ToBase64String([IO.File]::ReadAllBytes($fixture.path)) -ceq [Convert]::ToBase64String($originalBytes)) 'Original bytes remain unchanged'
    Check ((Entry Consume $request -Recovery).kind -ceq 'duplicate') 'Opt-in exact replay is duplicate'
    Entry Consume $request -Failure 'Unknown field: epoch' | Out-Null
    Check $true 'Default remains strict even after successful opt-in'
    $after = (Entry Read).state
    Check ((Get-CoordinatorDigest @($after.executions | Where-Object ownerId -CEQ unrelated-old-intent)[0]) -ceq $chargedDigest -and
        (Get-CoordinatorDigest (Get-CoordinatorOwner $after 'unrelated-old-intent')) -ceq $ownerDigest) 'Unrelated charged intent and source ownership are byte-semantically unchanged'
    Check (!(Get-CoordinatorOwner $after 'delivery-evidence').sourceRetained -and
        $null -eq (Get-CoordinatorOwner $after 'delivery-evidence').sourceRelease) 'Assessment collision scope released without fictional merge provenance'
    $proofBytes = [IO.File]::ReadAllBytes($released.terminalRecovery.proofPath)
    $hiddenProof = $released.terminalRecovery.proofPath + '.fixture-held'
    [IO.File]::Move($released.terminalRecovery.proofPath, $hiddenProof)
    try { Entry Consume $request -Recovery -Failure 'Missing immutable recovery evidence' | Out-Null }
    finally { [IO.File]::Move($hiddenProof, $released.terminalRecovery.proofPath) }
    [IO.File]::AppendAllText($released.terminalRecovery.proofPath, ' ')
    Entry Consume $request -Recovery -Failure 'Immutable recovery evidence changed' | Out-Null
    [IO.File]::WriteAllBytes($released.terminalRecovery.proofPath, $proofBytes)
    [IO.File]::AppendAllText($fixture.path, ' ')
    Entry Consume $request -Recovery -Failure 'Immutable recovery evidence changed' | Out-Null
    [IO.File]::WriteAllBytes($fixture.path, $originalBytes)
    [IO.File]::AppendAllText($fixture.receipt.pointerPath, ' ')
    Entry Consume $request -Recovery -Failure 'Immutable recovery evidence changed' | Out-Null
    [IO.File]::WriteAllBytes($fixture.receipt.pointerPath, $savedPointer)
    $projectionBytes = [IO.File]::ReadAllBytes($released.terminalRecovery.projectionPath)
    $hiddenProjection = $released.terminalRecovery.projectionPath + '.fixture-held'
    [IO.File]::Move($released.terminalRecovery.projectionPath, $hiddenProjection)
    try { Entry Consume $request -Recovery -Failure 'Missing immutable recovery evidence' | Out-Null }
    finally { [IO.File]::Move($hiddenProjection, $released.terminalRecovery.projectionPath) }
    [IO.File]::AppendAllText($released.terminalRecovery.projectionPath, ' ')
    Entry Consume $request -Recovery -Failure 'Immutable recovery evidence changed' | Out-Null
    [IO.File]::WriteAllBytes($released.terminalRecovery.projectionPath, $projectionBytes)
    $consumePath = $released.terminalRecovery.proofPath.Replace('.proof.json', '.request.json')
    $consumeBytes = [IO.File]::ReadAllBytes($consumePath)
    [IO.File]::AppendAllText($consumePath, ' ')
    Entry Consume $request -Recovery -Failure 'Immutable recovery evidence changed' | Out-Null
    [IO.File]::WriteAllBytes($consumePath, $consumeBytes)
    Check ((Entry Consume $request -Recovery).kind -ceq 'duplicate') 'Missing/changed proof, original, pointer, projection and request reject; exact retry remains once-only'
    Register-Owner 'subsequent-evidence'
    Approve-FixtureOwner 'subsequent-evidence'
    $next = Entry Next
    Check ($next.kind -ceq 'dispatch' -and $next.action.ownerId -ceq 'subsequent-evidence') 'Subsequent evidence admission proceeds without retiring unrelated charged intent'

    foreach ($policy in @('local-only', 'pr-only')) {
        $policyFixture = Invoke-FixtureSetup $policy -Policy $policy
        $result = Entry Consume @{ receiptPath = $policyFixture.path } -Recovery
        Check ($result.kind -ceq 'released') "Evidence-only recovery preserves $policy authority"
    }
    $featureFixture = Invoke-FixtureSetup 'feature-forbidden' -Kind feature
    Entry Consume @{ receiptPath = $featureFixture.path } -Recovery -Failure 'only schema1 evidence-completed' | Out-Null
    Check $true 'Feature owner cannot use assessment recovery'
    $summary = @{ version = 1; passed = $passed.ToArray(); processInvocations = $script:sequence
        successfulFixtureCommand = $successfulInvocation.arguments
        proofPath = $released.terminalRecovery.proofPath; liveDatasetTouched = $false }
    Write-ProofJson $summary (Join-Path $OutputDirectory 'summary.json')
    $summary | ConvertTo-Json -Depth 10
} catch {
    Write-ProofJson @{ passed = $passed.ToArray(); failure = $_.Exception.Message; processInvocations = $script:sequence } `
        (Join-Path $OutputDirectory 'failure.json')
    throw
}
