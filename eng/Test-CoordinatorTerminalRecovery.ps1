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
    param([string] $Action, $Request = @{}, [switch] $Recovery, [string] $Leader = 'fixture-leader', $Fence = $script:state,
        [string] $WireProfile)
    $script:sequence++
    $prefix = Join-Path $OutputDirectory "process-$script:sequence"
    $requestPath = "$prefix.request.json"
    Write-ProofJson $Request $requestPath
    $arguments = @('-NoProfile', '-File', $entry, '-RepositoryPath', $script:repositoryPath, '-Action', $Action,
        '-Session', $Leader, '-RequestPath', $requestPath)
    if ($Recovery) { $arguments += '-RecoverEvidenceTerminalMetadata' }
    if ($WireProfile) { $arguments += @('-PublishedTerminalWireProfile', $WireProfile) }
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
        [string] $Leader = 'fixture-leader', $Fence = $script:state, [string] $WireProfile)
    return Complete-Entry (Invoke-EntryProcess $Action $Request -Recovery:$Recovery -Leader $Leader -Fence $Fence -WireProfile $WireProfile) $Failure
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

function Copy-FixtureValue {
    param($Value)
    return $Value | ConvertTo-Json -Depth 40 | ConvertFrom-Json -AsHashtable -DateKind String
}

function Get-PublishedRequest {
    param($Fixture)
    $request = @{ receiptPath = $Fixture.path; receiptSha256 = (Get-FileHash -LiteralPath $Fixture.path).Hash
        readbackPath = $Fixture.readbackPath; readbackSha256 = (Get-FileHash -LiteralPath $Fixture.readbackPath).Hash
        publisher = 'fixture-publisher' }
    if ($Fixture.profile -ceq 'HostedDeliveryV1') {
        $request.publicationProofPath = $Fixture.publicationPath
        $request.publicationProofSha256 = (Get-FileHash -LiteralPath $Fixture.publicationPath).Hash
    }
    return $request
}

function Invoke-PublishedFixtureSetup {
    param([string] $Name, [string] $WireProfile = 'UtcTimestampV1', [string] $Time = '2026-10-27T12:21:40Z', [int] $Number = 556,
        [string] $Policy = 'ready-auto')
    $fixture = Invoke-FixtureSetup $Name -Kind feature -Policy $Policy
    foreach ($field in @('epoch', 'dispatchRevision', 'sourceHead', 'workspaceHead',
        'assessmentPath', 'assessmentSha256', 'pointerPath', 'completedUtc')) { $fixture.receipt.Remove($field) }
    $receipt = $fixture.receipt
    $receipt.kind = 'pr-enrolled-ci-wait'; $receipt.prUrl = "https://github.com/roryprimrose/Kora/pull/$Number"
    $receipt.autoMerge = [ordered]@{ method = 'SQUASH'; enabledAt = $Time }
    if ($WireProfile -ceq 'HostedDeliveryV1') {
        $receipt.workState = 'ci-wait'
        $receipt.autoMerge = [ordered]@{ mergeMethod = 'SQUASH'; enabledAt = $Time }
    }
    Write-ProofJson $receipt $fixture.path
    $fixture.profile = $WireProfile
    $fixture.readbackPath = Join-Path (Split-Path -Parent $fixture.path) 'pr-auto.json'
    $fixture.readback = @{ url = $receipt.prUrl; number = $Number; headRefOid = $receipt.head; headRefName = $receipt.branch
        baseRefName = 'main'; state = 'OPEN'; isDraft = $false; author = @{ login = 'fixture-publisher' }
        headRepository = @{ nameWithOwner = 'roryprimrose/Kora' }; isCrossRepository = $false
        autoMergeRequest = @{ mergeMethod = 'SQUASH'; enabledAt = $Time; enabledBy = @{ login = 'fixture-publisher' } } }
    Write-ProofJson $fixture.readback $fixture.readbackPath
    $fixture.publicationPath = Join-Path (Split-Path -Parent $fixture.path) 'proof-publication.json'
    $fixture.publication = @{ version = 1; timestamp = '2026-10-27T12:22:00.0000000+00:00'
        publisher = 'fixture-publisher'; repository = 'github.com/roryprimrose/Kora'
        ownerId = $receipt.ownerId; executionId = $receipt.executionId; nativeIdentity = $receipt.nativeIdentity
        branch = $receipt.branch; worktree = $receipt.worktree; testedHead = $receipt.head; currentRemoteHead = $receipt.head
        prUrl = $receipt.prUrl; state = 'OPEN'; mergeStateStatus = 'BEHIND'; reviewDecision = ''
        autoMerge = @{ mergeMethod = 'SQUASH'; enabledAt = $Time }
        enrollmentCommand = "gh pr merge $Number --repo github.com/roryprimrose/Kora --auto --squash --match-head-commit $($receipt.head)"
        enrollmentResult = 'passed and read back'; currentRequiredAppChecks = @()
        checkMeaning = 'Offline archived publication, not current admission'; implementationProof = 'implementation.json'
        prReadback = 'pr-auto.json'; checkReadback = 'checks.json'; finiteCommandsJoined = $true; runningCommands = @(); heldDecision = $null }
    Write-ProofJson $fixture.publication $fixture.publicationPath
    return $fixture
}

function Test-PublishedSuccess {
    param($Fixture)
    $request = Get-PublishedRequest $Fixture
    $bytes = [IO.File]::ReadAllBytes($Fixture.path)
    $unrelated = Get-CoordinatorDigest (Get-CoordinatorOwner $Fixture.snapshot 'unrelated-old-intent')
    $charge = Get-CoordinatorDigest @($Fixture.snapshot.executions | Where-Object ownerId -CEQ unrelated-old-intent)[0]
    if ($Fixture.profile -ceq 'HostedDeliveryV1' -or $Fixture.receipt.autoMerge.enabledAt -cnotmatch '\.[0-9]{7}Z\z') {
        Entry Consume @{ receiptPath = $Fixture.path } -Failure $(if ($Fixture.profile -ceq 'HostedDeliveryV1') { 'not proved quiescent' } else { 'String' }) | Out-Null
    }
    $before = $script:state
    $result = Entry Consume $request -WireProfile $Fixture.profile
    Check ($result.kind -ceq 'released') "$($Fixture.profile) releases $($Fixture.receipt.autoMerge.enabledAt)"
    $projection = Read-CoordinatorJson $result.terminalRecovery.projectionPath
    foreach ($key in $Fixture.receipt.Keys) {
        if ($key -cin @('autoMerge', 'workState')) { continue }
        Check ((Get-CoordinatorDigest @{ value = $projection[$key] }) -ceq
            (Get-CoordinatorDigest @{ value = $Fixture.receipt[$key] })) "Published projection preserves $key"
    }
    $rawTime = $Fixture.receipt.autoMerge.enabledAt
    $expectedTime = [DateTimeOffset]::Parse($rawTime, [Globalization.CultureInfo]::InvariantCulture).ToUniversalTime().ToString('o')
    Check ($projection.workState -ceq 'stopped' -and $projection.autoMerge.method -ceq 'SQUASH' -and
        $projection.autoMerge.Count -eq 2 -and $projection.autoMerge.enabledAt -ceq $expectedTime) 'Canonical values preserve the exact publication instant'
    $proof = Read-CoordinatorJson $result.terminalRecovery.proofPath -MaximumBytes 1048576
    Check ($proof.profile -ceq "published-wire-$($Fixture.profile)" -and $proof.producer -ceq 'Coordinator.TerminalRecovery' -and
        $proof.originalBytesBase64 -ceq [Convert]::ToBase64String($bytes) -and
        $proof.readbackSha256 -ceq (Get-FileHash -LiteralPath $Fixture.readbackPath).Hash -and
        $proof.originalValues.autoMerge.enabledAt -ceq $rawTime -and
        $proof.normalizedValues.autoMerge.enabledAt -ceq $expectedTime) 'Helper provenance pins raw original, archived readback and exact value mapping'
    Check ((Entry Consume $request -WireProfile $Fixture.profile).kind -ceq 'duplicate') 'Published exact replay remains duplicate'
    $after = (Entry Read).state
    Check ($result.revision -eq $before.revision + 1 -and $after.revision -eq $result.revision + 1 -and
        $after.events.Count -eq 1 -and @($after.executions | Where-Object phase -CEQ released).Count -eq 1 -and
        (Get-CoordinatorDigest (Get-CoordinatorOwner $after 'unrelated-old-intent')) -ceq $unrelated -and
        (Get-CoordinatorDigest @($after.executions | Where-Object ownerId -CEQ unrelated-old-intent)[0]) -ceq $charge -and
        (Get-CoordinatorOwner $after 'delivery-evidence').sourceRetained) 'Published recovery releases once, with a separate duplicate transaction, retaining source and unrelated charge'
    Check ([Convert]::ToBase64String([IO.File]::ReadAllBytes($Fixture.path)) -ceq [Convert]::ToBase64String($bytes)) 'Published original is never rewritten'
    $reordered = [ordered]@{}
    foreach ($field in @($request.Keys | Sort-Object -Descending -CaseSensitive)) { $reordered[$field] = $request[$field] }
    Check ((Entry Consume $reordered -WireProfile $Fixture.profile).kind -ceq 'duplicate') 'Request property ordering cannot change immutable replay evidence'
    Register-Owner 'subsequent-evidence'
    Approve-FixtureOwner 'subsequent-evidence'
    Check ((Entry Next).action.ownerId -ceq 'subsequent-evidence') 'Published recovery admits next independent work'
    return $result
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

    foreach ($shape in @(
        @{ name = 'pr556'; profile = 'UtcTimestampV1'; time = '2026-10-27T12:21:40Z'; number = 556 },
        @{ name = 'pr557'; profile = 'UtcTimestampV1'; time = '2026-10-27T12:33:17Z'; number = 557 },
        @{ name = 'pr555'; profile = 'HostedDeliveryV1'; time = '2026-10-27T12:16:57Z'; number = 555 })) {
        $publishedFixture = Invoke-PublishedFixtureSetup $shape.name $shape.profile $shape.time $shape.number
        $null = Test-PublishedSuccess $publishedFixture
    }
    foreach ($digits in 1..7) {
        $time = '2026-10-27T12:21:40.' + '1234567'.Substring(0, $digits) + 'Z'
        $fractionFixture = Invoke-PublishedFixtureSetup "fraction-$digits" -Time $time
        $fractionFixture.readback.autoMergeRequest.enabledAt = '2026-10-27T12:21:40.' + '1234567'.Substring(0, $digits).PadRight(7, '0') + 'Z'
        Write-ProofJson $fractionFixture.readback $fractionFixture.readbackPath
        $null = Test-PublishedSuccess $fractionFixture
    }
    $strictFraction = Invoke-PublishedFixtureSetup 'strict-roundtrip-z' -Time '2026-10-27T12:21:40.1234567Z'
    Check ((Entry Consume @{ receiptPath = $strictFraction.path }).kind -ceq 'released') 'Existing strict seven-fraction UTC Z roundtrip remains valid'
    Check ((Entry Consume @{ receiptPath = $strictFraction.path }).kind -ceq 'duplicate') 'Existing strict seven-fraction UTC Z replay remains duplicate'
    $wire = Invoke-PublishedFixtureSetup 'wire-rejections'
    $wireRequest = Get-PublishedRequest $wire
    Entry Read $wireRequest -WireProfile $wire.profile -Failure 'only supported for Consume' | Out-Null
    Entry Consume $wireRequest -Recovery -WireProfile $wire.profile -Failure 'exactly one' | Out-Null
    Entry Consume $wireRequest -WireProfile $wire.profile -Leader wrong -Failure 'Current unexpired leader' | Out-Null
    foreach ($fence in @(@{ epoch = 0; revision = $script:state.revision }, @{ epoch = $script:state.epoch; revision = 0 })) {
        Entry Consume $wireRequest -WireProfile $wire.profile -Fence $fence -Failure 'Stale epoch/revision' | Out-Null
    }
    $wireCases = @(
        @{ key = 'workState'; value = 'ci-wait'; failure = 'closed profile' },
        @{ key = 'workState'; value = 'idle'; failure = 'closed profile' },
        @{ key = 'ownerId'; value = 'unrelated-old-intent'; failure = 'owner/execution' },
        @{ key = 'executionId'; value = 'unknown'; failure = 'owner/execution' },
        @{ key = 'nativeIdentity'; value = 'foreign'; failure = 'identity/authority' },
        @{ key = 'datasetId'; value = 'foreign'; failure = 'identity/authority' },
        @{ key = 'branch'; value = 'foreign'; failure = 'readback identity' },
        @{ key = 'head'; value = 'a' * 40; failure = 'readback identity' },
        @{ key = 'publicationPolicy'; value = 'pr-only'; failure = 'existing ready-auto' },
        @{ key = 'finiteCommandsJoined'; value = $false; failure = 'joined/quiescent' },
        @{ key = 'finiteCommandsJoined'; value = 'true'; failure = 'joined/quiescent' },
        @{ key = 'runningCommands'; value = @('held'); failure = 'joined/quiescent' },
        @{ key = 'runningCommands'; value = $null; failure = 'joined/quiescent' },
        @{ key = 'heldDecision'; value = 'decision'; failure = 'joined/quiescent' },
        @{ key = 'autoMerge'; value = @{ mergeMethod = 'SQUASH'; enabledAt = '2026-10-27T12:21:40Z' }; failure = 'Missing field: method' },
        @{ key = 'autoMerge'; value = @{ method = 'SQUASH'; mergeMethod = 'SQUASH'; enabledAt = '2026-10-27T12:21:40Z' }; failure = 'Unknown field' },
        @{ key = 'autoMerge'; value = @{ method = 'MERGE'; enabledAt = '2026-10-27T12:21:40Z' }; failure = 'closed profile' },
        @{ key = 'epoch'; value = 1; failure = 'Unknown field' }
    )
    foreach ($time in @('2026-10-27T12:21:41Z', '2026-10-27T12:21:40', '2026-10-27T12:21:40+01:00',
        '2026-10-27T12:21:40+00:00', '2026-10-27T12:21:40.12345678Z', '2026-02-30T12:21:40Z',
        '10/27/2026 12:21:40Z', '2026-10-27T12:21:40z', '2026-10-27T12:21:60Z')) {
        $wireCases += @{ key = 'autoMerge'; value = @{ method = 'SQUASH'; enabledAt = $time }
            failure = $(if ($time -ceq '2026-10-27T12:21:41Z') { 'readback identity' } elseif ($time -cin @('2026-02-30T12:21:40Z', '2026-10-27T12:21:60Z')) { 'Gregorian UTC' } else { 'explicit UTC' }) }
    }
    $index = 0
    foreach ($case in $wireCases) {
        $index++
        $copy = Copy-FixtureValue $wire.receipt
        $copy[$case.key] = $case.value
        $badPath = Join-Path (Split-Path -Parent $wire.path) "wire-bad-$index.json"
        Write-ProofJson $copy $badPath
        $request = Copy-FixtureValue $wireRequest
        $request.receiptPath = $badPath; $request.receiptSha256 = (Get-FileHash -LiteralPath $badPath).Hash
        Entry Consume $request -WireProfile $wire.profile -Failure $case.failure | Out-Null
        Check $true "Published original rejects $($case.key) case $index"
    }
    foreach ($case in @(
        @{ key = 'publisher'; value = 'wrong'; failure = 'pinned publisher' },
        @{ key = 'receiptSha256'; value = 'a' * 64; failure = 'SHA256 mismatch' },
        @{ key = 'readbackSha256'; value = 'a' * 64; failure = 'SHA256 mismatch' },
        @{ key = 'publicationCorrection'; value = @{}; failure = 'Unknown field' },
        @{ key = 'receiptPath'; value = "$($wire.path):stream"; failure = 'canonical registered' },
        @{ key = 'readbackPath'; value = (Join-Path $OutputDirectory 'outside.json'); failure = 'canonical registered' },
        @{ key = 'readbackPath'; value = (Join-Path (Split-Path -Parent $wire.path) '..\delivery-evidence\pr-auto.json'); failure = 'canonical registered' })) {
        $request = Copy-FixtureValue $wireRequest; $request[$case.key] = $case.value
        Entry Consume $request -WireProfile $wire.profile -Failure $case.failure | Out-Null
        Check $true "Published request rejects $($case.key)"
    }
    $readbackBytes = [IO.File]::ReadAllBytes($wire.readbackPath)
    foreach ($case in @(
        @{ key = 'headRefOid'; value = 'a' * 40 }, @{ key = 'headRefName'; value = 'wrong' },
        @{ key = 'url'; value = 'https://github.com/other/repo/pull/556' }, @{ key = 'number'; value = 557 },
        @{ key = 'baseRefName'; value = 'wrong' }, @{ key = 'state'; value = 'CLOSED' },
        @{ key = 'isDraft'; value = $true }, @{ key = 'isDraft'; value = 'false' },
        @{ key = 'isCrossRepository'; value = $true }, @{ key = 'author'; value = @{ login = 'wrong' } },
        @{ key = 'headRepository'; value = @{ nameWithOwner = 'other/repo' } },
        @{ key = 'unknown'; value = 'rejected' },
        @{ key = 'autoMergeRequest'; value = @{ mergeMethod = 'MERGE'; enabledAt = '2026-10-27T12:21:40Z'; enabledBy = @{ login = 'fixture-publisher' } } },
        @{ key = 'autoMergeRequest'; value = @{ mergeMethod = 'SQUASH'; enabledAt = '2026-10-27T12:21:41Z'; enabledBy = @{ login = 'fixture-publisher' } } },
        @{ key = 'autoMergeRequest'; value = @{ mergeMethod = 'SQUASH'; enabledAt = '2026-10-27T12:21:40Z'; enabledBy = @{ login = 'wrong' } } })) {
        $copy = Copy-FixtureValue $wire.readback; $copy[$case.key] = $case.value
        Write-ProofJson $copy $wire.readbackPath
        $request = Get-PublishedRequest $wire
        Entry Consume $request -WireProfile $wire.profile -Failure $(if ($case.key -ceq 'unknown') { 'Unknown field' } else { 'Published wire' }) | Out-Null
        Check $true "Archived readback rejects $($case.key)"
    }
    [IO.File]::WriteAllBytes($wire.readbackPath, $readbackBytes)
    $duplicate = [Text.Encoding]::UTF8.GetString($readbackBytes).Replace('"mergeMethod": "SQUASH"', '"mergeMethod": "SQUASH", "mergeMethod": "SQUASH"')
    [IO.File]::WriteAllText($wire.readbackPath, $duplicate)
    Entry Consume (Get-PublishedRequest $wire) -WireProfile $wire.profile -Failure 'Duplicate published wire field' | Out-Null
    [IO.File]::WriteAllBytes($wire.readbackPath, $readbackBytes)
    $wireLink = Join-Path (Split-Path -Parent $wire.path) 'linked-readback'
    New-Item -ItemType $(if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }) -Path $wireLink -Target $OutputDirectory | Out-Null
    $linkRequest = Copy-FixtureValue $wireRequest; $linkRequest.readbackPath = Join-Path $wireLink 'outside.json'
    Entry Consume $linkRequest -WireProfile $wire.profile -Failure 'canonical registered' | Out-Null
    Check $true 'Nested duplicates and published path escapes fail closed'

    $hosted = Invoke-PublishedFixtureSetup 'hosted-rejections' HostedDeliveryV1 '2026-10-27T12:16:57Z' 555
    $hostedRequest = Get-PublishedRequest $hosted
    $publicationBytes = [IO.File]::ReadAllBytes($hosted.publicationPath)
    foreach ($case in @(
        @{ key = 'finiteCommandsJoined'; value = $false }, @{ key = 'finiteCommandsJoined'; value = 'true' },
        @{ key = 'runningCommands'; value = @('held') }, @{ key = 'runningCommands'; value = $null },
        @{ key = 'heldDecision'; value = 'held' },
        @{ key = 'autoMerge'; value = @{ mergeMethod = 'MERGE'; enabledAt = '2026-10-27T12:16:57Z' } },
        @{ key = 'autoMerge'; value = @{ mergeMethod = 'SQUASH'; method = 'SQUASH'; enabledAt = '2026-10-27T12:16:57Z' } })) {
        $copy = Copy-FixtureValue $hosted.receipt; $copy[$case.key] = $case.value
        $badPath = Join-Path (Split-Path -Parent $hosted.path) "original-hosted-$([guid]::NewGuid().ToString('N')).json"
        Write-ProofJson $copy $badPath
        $request = Copy-FixtureValue $hostedRequest
        $request.receiptPath = $badPath; $request.receiptSha256 = (Get-FileHash -LiteralPath $badPath).Hash
        Entry Consume $request -WireProfile $hosted.profile -Failure $(if ($copy.autoMerge.Contains('method')) { 'Unknown field' } else { 'Published wire' }) | Out-Null
        Check $true "Hosted original rejects contradictory $($case.key)"
    }
    $copy = Copy-FixtureValue $hosted.receipt; $copy.Remove('finiteCommandsJoined')
    $badPath = Join-Path (Split-Path -Parent $hosted.path) 'original-hosted-incomplete.json'
    Write-ProofJson $copy $badPath
    $request = Copy-FixtureValue $hostedRequest
    $request.receiptPath = $badPath; $request.receiptSha256 = (Get-FileHash -LiteralPath $badPath).Hash
    Entry Consume $request -WireProfile $hosted.profile -Failure 'Missing field' | Out-Null
    foreach ($case in @(
        @{ key = 'finiteCommandsJoined'; value = $false }, @{ key = 'finiteCommandsJoined'; value = 'true' },
        @{ key = 'runningCommands'; value = @('still-running') }, @{ key = 'runningCommands'; value = $null },
        @{ key = 'heldDecision'; value = @{} }, @{ key = 'ownerId'; value = 'wrong' },
        @{ key = 'executionId'; value = 'wrong' }, @{ key = 'nativeIdentity'; value = 'wrong' },
        @{ key = 'branch'; value = 'wrong' }, @{ key = 'worktree'; value = 'wrong' },
        @{ key = 'testedHead'; value = 'a' * 40 }, @{ key = 'currentRemoteHead'; value = 'a' * 40 },
        @{ key = 'publisher'; value = 'wrong' }, @{ key = 'repository'; value = 'github.com/other/repo' },
        @{ key = 'enrollmentResult'; value = 'unknown' }, @{ key = 'enrollmentCommand'; value = 'gh pr merge --admin' },
        @{ key = 'prReadback'; value = 'other.json' },
        @{ key = 'autoMerge'; value = @{ mergeMethod = 'MERGE'; enabledAt = '2026-10-27T12:16:57Z' } },
        @{ key = 'autoMerge'; value = @{ mergeMethod = 'SQUASH'; method = 'SQUASH'; enabledAt = '2026-10-27T12:16:57Z' } },
        @{ key = 'autoMerge'; value = @{ mergeMethod = 'SQUASH'; enabledAt = '2026-10-27T12:16:58Z' } },
        @{ key = 'unknown'; value = 'rejected' })) {
        $copy = Copy-FixtureValue $hosted.publication; $copy[$case.key] = $case.value
        Write-ProofJson $copy $hosted.publicationPath
        Entry Consume (Get-PublishedRequest $hosted) -WireProfile $hosted.profile -Failure $(if ($case.key -cin @('unknown') -or $copy.autoMerge.Contains('method')) { 'Unknown field' } else { 'Published wire' }) | Out-Null
        Check $true "Hosted supplemental proof rejects $($case.key)"
    }
    $copy = Copy-FixtureValue $hosted.publication; $copy.Remove('finiteCommandsJoined')
    Write-ProofJson $copy $hosted.publicationPath
    Entry Consume (Get-PublishedRequest $hosted) -WireProfile $hosted.profile -Failure 'Missing field' | Out-Null
    [IO.File]::WriteAllBytes($hosted.publicationPath, $publicationBytes)
    $missingProofRequest = Copy-FixtureValue $hostedRequest; $missingProofRequest.Remove('publicationProofPath')
    Entry Consume $missingProofRequest -WireProfile $hosted.profile -Failure 'Missing field' | Out-Null
    $null = Entry Consume $hostedRequest -WireProfile UtcTimestampV1 -Failure 'Unknown field'
    Check $true 'Hosted label never implies stopped without the exact supplemental profile'
    $hostedResult = Test-PublishedSuccess $hosted
    $replayRequest = Get-PublishedRequest $hosted
    foreach ($path in @($hosted.path, $hosted.readbackPath, $hosted.publicationPath,
        $hostedResult.terminalRecovery.proofPath, $hostedResult.terminalRecovery.projectionPath,
        $hostedResult.terminalRecovery.proofPath.Replace('.proof.json', '.request.json'))) {
        $saved = [IO.File]::ReadAllBytes($path)
        $hidden = $path + '.fixture-held'
        [IO.File]::Move($path, $hidden)
        try { Entry Consume $replayRequest -WireProfile $hosted.profile -Failure $(if ($path -ceq $hosted.path) { 'does not exist' } elseif ($path -cin @($hosted.readbackPath, $hosted.publicationPath)) { 'does not exist' } else { 'Missing immutable' }) | Out-Null }
        finally { [IO.File]::Move($hidden, $path) }
        [IO.File]::AppendAllText($path, ' ')
        Entry Consume $replayRequest -WireProfile $hosted.profile -Failure $(if ($path -cin @($hosted.path, $hosted.readbackPath, $hosted.publicationPath)) { 'SHA256 mismatch' } else { 'Immutable recovery' }) | Out-Null
        if ($path -cin @($hosted.path, $hosted.readbackPath, $hosted.publicationPath)) {
            Entry Consume (Get-PublishedRequest $hosted) -WireProfile $hosted.profile -Failure 'Immutable recovery evidence changed' | Out-Null
        }
        [IO.File]::WriteAllBytes($path, $saved)
        $held = [IO.FileStream]::new($path, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { Entry Consume $replayRequest -WireProfile $hosted.profile -Failure 'being used by another process' | Out-Null }
        finally { $held.Dispose() }
        Check ((Entry Consume $replayRequest -WireProfile $hosted.profile).kind -ceq 'duplicate') 'Published missing/changed/racing inputs never become a second release'
    }
    $concurrent = Invoke-PublishedFixtureSetup 'published-concurrent'
    $concurrentRequest = Get-PublishedRequest $concurrent
    $one = Invoke-EntryProcess Consume $concurrentRequest -WireProfile $concurrent.profile
    $two = Invoke-EntryProcess Consume $concurrentRequest -WireProfile $concurrent.profile
    $results = @((Complete-Entry $one -Concurrent), (Complete-Entry $two -Concurrent))
    Check (@($results | Where-Object kind -CEQ released).Count -eq 1) 'Concurrent published recovery releases exactly once'
    $null = Entry Read
    Check ((Entry Consume $concurrentRequest -WireProfile $concurrent.profile).kind -ceq 'duplicate') 'Concurrent published exact replay remains duplicate'
    foreach ($policy in @('local-only', 'pr-only')) {
        $forbidden = Invoke-PublishedFixtureSetup "published-$policy" -Policy $policy
        Entry Consume (Get-PublishedRequest $forbidden) -WireProfile $forbidden.profile -Failure 'existing ready-auto' | Out-Null
        Check $true "Published normalization never escalates $policy authority"
    }
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
