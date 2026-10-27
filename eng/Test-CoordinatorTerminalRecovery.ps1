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
$script:fixtureLeader = 'fixture-leader'

function Check {
    param([bool] $Condition, [string] $Name)
    if (!$Condition) { throw "Failed: $Name" }
    $passed.Add($Name)
}

function Invoke-EntryProcess {
    param([string] $Action, $Request = @{}, [switch] $Recovery, [string] $Leader = $script:fixtureLeader, $Fence = $script:state,
        [string] $WireProfile, [string] $DispatchProfile)
    $script:sequence++
    $prefix = Join-Path $OutputDirectory "process-$script:sequence"
    $requestPath = "$prefix.request.json"
    Write-ProofJson $Request $requestPath
    $arguments = @('-NoProfile', '-File', $entry, '-RepositoryPath', $script:repositoryPath, '-Action', $Action,
        '-Session', $Leader, '-RequestPath', $requestPath)
    if ($Recovery) { $arguments += '-RecoverEvidenceTerminalMetadata' }
    if ($WireProfile) { $arguments += @('-PublishedTerminalWireProfile', $WireProfile) }
    if ($DispatchProfile) { $arguments += @('-CurrentOriginalDispatchProfile', $DispatchProfile) }
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

    function Get-DerivedRequest {
        param($Fixture)
        $request = Copy-FixtureValue $Fixture.request
        foreach ($field in @($request.Keys | Where-Object { $_ -clike '*Path' })) {
            $request[$field.Substring(0, $field.Length - 4) + 'Sha256'] = (Get-FileHash -LiteralPath $request[$field]).Hash
        }
        return $request
    }

    function Invoke-RepresentationFixture {
        param([string] $Name, [string] $WireProfile = 'UtcRepresentationV1', [string] $Time = '2026-10-27T12:33:17Z')
        $fixture = Invoke-PublishedFixtureSetup $Name -WireProfile $WireProfile -Time $Time -Number 557
        $fixture.options = @{ WireProfile = $WireProfile }
        $fixture.request = @{ receiptPath = $fixture.path; receiptSha256 = '' }
        if ($WireProfile -ceq 'NoHeldDecisionV1') {
            $fixture.receipt.heldDecision = $false
            $fixture.stopPath = Join-Path (Split-Path -Parent $fixture.path) 'prerequisite-ready.json'
            $fixture.deliveryPath = Join-Path (Split-Path -Parent $fixture.path) 'delivery-proof.json'
            $fixture.stop = @{ version = 1; datasetId = $fixture.receipt.datasetId; ownerId = $fixture.receipt.ownerId
                executionId = $fixture.receipt.executionId; kind = 'prerequisite-ready'; nativeIdentity = $fixture.receipt.nativeIdentity
                publisher = 'fixture-publisher'; publicationPolicy = 'ready-auto'; implementation = @{
                    worktree = $fixture.receipt.worktree; branch = $fixture.receipt.branch; head = $fixture.receipt.head
                    base = 'a' * 40; tree = 'b' * 40; files = @(); signature = 'offline'; call = 'offline'; placement = 'offline'
                    semantics = 'offline'; documentation = 'offline'; sourceBlobManifest = 'offline'; declarationSha256 = 'c' * 64
                    pr544Inspection = 'offline' }; prSourceValidation = @(); comparisonOnly = @{}; preservedFailures = @()
                unrun = @(); toolchain = 'offline'; finiteCommandsJoined = $true; runningCommands = @(); heldDecision = $false; next = 'offline' }
            $fixture.delivery = @{ version = 1; datasetId = $fixture.receipt.datasetId; ownerId = $fixture.receipt.ownerId
                executionId = $fixture.receipt.executionId; nativeIdentity = $fixture.receipt.nativeIdentity; publisher = 'fixture-publisher'
                repository = 'github.com/roryprimrose/Kora'; branch = $fixture.receipt.branch; worktree = $fixture.receipt.worktree
                head = $fixture.receipt.head; tree = 'b' * 40; prUrl = $fixture.receipt.prUrl; sourceAndBehaviorProof = 'prerequisite-ready.json'
                binaryHashes = 'offline'; terminalReceipt = 'terminal.json'; deliveryCommands = @(); comparisonCleanup = 'offline'
                finiteCommandsJoined = $true; runningCommands = @(); heldDecision = $false; terminalPublication = 'offline'; remaining = 'offline' }
            Write-ProofJson $fixture.stop $fixture.stopPath
            Write-ProofJson $fixture.delivery $fixture.deliveryPath
            $fixture.request.readbackPath = $fixture.readbackPath; $fixture.request.readbackSha256 = ''
            $fixture.request.publisher = 'fixture-publisher'
            $fixture.request.deliveryProofPath = $fixture.deliveryPath; $fixture.request.deliveryProofSha256 = ''
            $fixture.request.stopProofPath = $fixture.stopPath; $fixture.request.stopProofSha256 = ''
            Write-ProofJson $fixture.receipt $fixture.path
        }
        return $fixture
    }

    function Invoke-OriginalDispatchFixture {
        param([string] $Name, [string] $DispatchProfile = 'MissingNativeEvidenceV1', [switch] $CombinedHostProof)
        $dispatcher = 'ahp-session:/df16fb09-8fde-4d97-b9ec-a733600c4f1f'
        $native = 'agent-host-session://ahp-session/df16fb09-8fde-4d97-b9ec-a733600c4f1f?chat=a6e48f7f-f83b-4575-991b-6e16fee6ca6d'
        $fixture = Invoke-FixtureSetup $Name -Kind $(if ($DispatchProfile -ceq 'MissingLaunchBlockerV1') { 'feature' } else { 'evidence' }) `
            -Dispatcher $dispatcher -Native $native
        Invoke-Checked git @('-C', $script:repositoryPath, 'add', '.gitignore')
        Invoke-Checked git @('-C', $script:repositoryPath, '-c', 'user.name=Offline Fixture', '-c', 'user.email=fixture@example.invalid',
            'commit', '--quiet', '-m', 'Offline source fixture')
        Invoke-Checked git @('-C', $script:repositoryPath, 'branch', '-m', 'main')
        $head = & git -C $script:repositoryPath rev-parse HEAD
        if ($LASTEXITCODE -ne 0) { throw 'Fixture source head unavailable.' }
        foreach ($field in @('epoch', 'dispatchRevision', 'sourceHead', 'workspaceHead',
            'assessmentPath', 'assessmentSha256', 'pointerPath', 'completedUtc')) { $fixture.receipt.Remove($field) }
        $fixture.receipt.head = $head; $fixture.receipt.nativeIdentity = $null
        if ($DispatchProfile -ceq 'MissingLaunchBlockerV1') {
            $original = $fixture.receipt
            $fixture.receipt = @{ version = 1; datasetId = $original.datasetId; ownerId = $original.ownerId
                executionId = $original.executionId; observedAt = '2026-10-27T13:47:10.7242529+00:00'
                receiptKind = 'missing-launch-mapping'
                requiredLaunchPath = Join-Path (Split-Path -Parent $fixture.path) "launch-$($original.executionId).json"
                observed = 'Original mapping absent'; boundary = 'No identity substituted'; publisher = 'fixture-publisher'
                repository = 'github.com/roryprimrose/Kora'; publicationPolicy = 'ready-auto'
                preflight = @{ initialWorktree = $script:repositoryPath; initialBranch = 'main'; initialHead = $head
                    freshFetchedMain = 'a' * 40; initialWorkingTreeClean = $true; repositoryPushPermission = $true
                    liveQueueMode = 'enabled'; mainEnforceAdmins = $true; requiredCheckPublisherAppId = 5395839; scratchIgnoreVerified = $true }
                effects = @{ sourceFilesChanged = $false; uniqueWorktreeCreated = $false; bindingPublished = $false
                    strictTerminalPublished = $false; commitCreated = $false; prCreated = $false; autoMergeEnrolled = $false }
                validation = 'Not run'; workState = 'stopped'; finiteCommandsJoined = $true
                runningCommands = @(); heldDecision = $null; note = 'Original stopped blocker, not a strict terminal' }
        }
        Write-ProofJson $fixture.receipt $fixture.path
        $fixture.hostPath = Join-Path $fixture.snapshot.identity.directory 'native-launch-proof.json'
        $fixture.host = @{ version = 1; datasetId = $fixture.receipt.datasetId; coordinator = $dispatcher
            source = 'Original host-managed current-dispatcher SDK events; exact paired native create_session start/completion; no title or shared-parent inference'
            sourceBytesObserved = 21038834; observedUtc = '2026-10-27T14:17:26.075Z'; launches = @(@{
                ownerId = $fixture.receipt.ownerId; executionId = $fixture.receipt.executionId; dispatcher = $dispatcher
                toolName = 'create_session'; toolCallId = 'call_tZzaYk58cN9AzNtcmZOo6vMH'
                startEventId = '7aea124e-0a39-40af-aa8a-cdca63a46045'; completionEventId = '2c503a33-6bf8-42c6-bd17-6a2510890824'
                startedUtc = '2026-10-27T12:39:18.166Z'; completedUtc = '2026-10-27T12:39:19.560Z'
                nativeIdentity = $native; originalCallSucceeded = $true; originalDispatcherCallJoined = $true
                initialHeaderSha256 = '2fb7e0c4ed8b34482aee075f31921aed7defacf5d353617f0c82c0cbbb5f2e5b' }) }
        if ($CombinedHostProof) {
            Register-Owner 'second-current-evidence' -Kind feature
            Approve-FixtureOwner 'second-current-evidence'
            $other = Entry Next
            $otherNative = 'agent-host-session://ahp-session/df16fb09-8fde-4d97-b9ec-a733600c4f1f?chat=ac9d5d4d-5c9e-456d-8e0d-4969960d3e66'
            Entry Reconcile @{ executionId = $other.action.executionId; outcome = 'accepted'; dispatcherJoined = $true
                nativeIdentity = $otherNative; branch = 'main'; worktree = $script:repositoryPath
                evidence = 'Second exact original host fixture; no real native session' } | Out-Null
            $otherLaunch = Copy-FixtureValue $fixture.host.launches[0]
            $otherLaunch.ownerId = 'second-current-evidence'; $otherLaunch.executionId = $other.action.executionId
            $otherLaunch.nativeIdentity = $otherNative; $otherLaunch.toolCallId = 'call_XJZUwYBQvQW5nwaZckzuls36'
            $otherLaunch.startEventId = '916c40de-8498-4dcc-9da4-bfbc3f8a6f92'
            $otherLaunch.completionEventId = '001b881b-3ecb-4224-a77d-70b5399cb160'
            $otherLaunch.initialHeaderSha256 = 'f7808422acd0b7d886c790b6328c45ccffaf62e32db380aee296a15b852a103f'
            $fixture.host.launches += ,$otherLaunch
        }
        Write-ProofJson $fixture.host $fixture.hostPath
        $fixture.options = @{ DispatchProfile = $DispatchProfile }
        $fixture.request = @{ receiptPath = $fixture.path; receiptSha256 = ''; hostLaunchProofPath = $fixture.hostPath
            hostLaunchProofSha256 = ''; sourceHead = $head }
        return $fixture
    }

    function Test-DerivedSuccess {
        param($Fixture)
        $options = $Fixture.options
        $request = Get-DerivedRequest $Fixture
        $before = (Entry Read).state
        $unrelated = Get-CoordinatorDigest @($before.executions | Where-Object ownerId -CEQ unrelated-old-intent)[0]
        $result = Entry Consume $request @options
        Check ($result.kind -ceq 'released') "Derived profile releases: $($options.Values -join ',')"
        $projection = Read-CoordinatorJson $result.terminalRecovery.projectionPath
        Check ($projection.Count -eq 18 -and $projection.workState -ceq 'stopped' -and
            $null -eq $projection.heldDecision) 'Derived projection has only the strict eighteen fields and exact stopped/null values'
        if ($Fixture.receipt.Contains('eventId')) {
            foreach ($field in $Fixture.receipt.Keys) {
                if ($field -cin @('autoMerge', 'heldDecision', 'nativeIdentity')) { continue }
                Check ((Get-CoordinatorDigest @{ v = $Fixture.receipt[$field] }) -ceq
                    (Get-CoordinatorDigest @{ v = $projection[$field] })) "Derived projection preserves original $field"
            }
        } else {
            Check ($projection.eventId -clike 'helper-original-dispatch-*' -and $projection.revision -eq 1 -and
                $projection.kind -ceq 'blocked-quiescent' -and $projection.head -ceq $Fixture.receipt.preflight.initialHead) 'Blocker uses a helper-owned source-bound event/revision, not an invented actor terminal'
        }
        $proof = Read-CoordinatorJson $result.terminalRecovery.proofPath -MaximumBytes 1048576
        Check ($proof.producer -ceq 'Coordinator.TerminalRecovery' -and $proof.profile -cin @($options.Values) -and
            $proof.originalBytesBase64 -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($Fixture.path)) -and
            $proof.originalSha256 -ceq (Get-FileHash -LiteralPath $Fixture.path).Hash) 'Derived provenance preserves original raw bytes/path/hash under the helper producer'
        foreach ($inputFile in $proof.supportingInputs) {
            Check ($inputFile.sha256 -ceq (Get-FileHash -LiteralPath $inputFile.path).Hash -and
                $inputFile.bytesBase64 -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($inputFile.path))) 'Supporting proof bytes are unchanged and pinned'
        }
        if ($Fixture.options.Contains('WireProfile')) {
            Check ($projection.autoMerge.method -ceq 'SQUASH' -and $projection.autoMerge.enabledAt -ceq
                ([DateTimeOffset]::Parse($Fixture.receipt.autoMerge.enabledAt, [Globalization.CultureInfo]::InvariantCulture).ToUniversalTime().ToString('o'))) 'Derived UTC representation proves the identical instant'
            Check ($projection.nativeIdentity -ceq $Fixture.receipt.nativeIdentity -and
                $proof.derivation.originalHeldDecision -ceq $Fixture.receipt.heldDecision -and
                $null -eq $proof.derivation.normalizedHeldDecision) 'Representation derivation preserves actor identity and explicitly records only the selected absence mapping'
        } else {
            Check ($projection.nativeIdentity -ceq $Fixture.host.launches[0].nativeIdentity -and
                $proof.derivation.epoch -eq $before.epoch -and $proof.derivation.dispatcher -ceq $before.leader) 'Missing identity derives only from the original joined host link and current grant'
        }
        Check ((Entry Consume $request @options).kind -ceq 'duplicate') 'Derived exact replay is duplicate'
        $after = (Entry Read).state
        Check ($after.events.Count -eq 1 -and @($after.executions | Where-Object phase -CEQ released).Count -eq 1 -and
            (Get-CoordinatorDigest @($after.executions | Where-Object ownerId -CEQ unrelated-old-intent)[0]) -ceq $unrelated) 'Derived recovery releases once and preserves the unrelated old charged intent'
        Check ((Get-CoordinatorDigest (Get-CoordinatorOwner $before 'unrelated-old-intent')) -ceq
            (Get-CoordinatorDigest (Get-CoordinatorOwner $after 'unrelated-old-intent')) -and
            (Get-CoordinatorOwner $after 'delivery-evidence').sourceRetained -eq
                ($projection.kind -cne 'evidence-completed')) 'Derived recovery preserves unrelated ownership and only applies strict core terminal source disposition'
        return $result
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
        [string] $Leader = $script:fixtureLeader, $Fence = $script:state, [string] $WireProfile, [string] $DispatchProfile)
    return Complete-Entry (Invoke-EntryProcess $Action $Request -Recovery:$Recovery -Leader $Leader -Fence $Fence `
        -WireProfile $WireProfile -DispatchProfile $DispatchProfile) $Failure
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
    param([string] $Name, [string] $Policy = 'ready-auto', [string] $Kind = 'evidence',
        [string] $Dispatcher = 'fixture-leader', [string] $Native = 'agent-host-session://fixture?chat=fc7073eb')
    $script:fixtureLeader = $Dispatcher
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
        dispatcherJoined = $true; nativeIdentity = $Native
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
        nativeIdentity = $Native; branch = 'main'; worktree = $script:repositoryPath
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

function Test-ExistingProfileSuite {
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
    return @{ successfulInvocation = $successfulInvocation; released = $released }
}

function Test-DerivedProfileSuite {
    foreach ($digits in 0..7) {
        $time = '2026-10-27T12:33:17' + $(if ($digits) { '.' + '1234567'.Substring(0, $digits) } else { '' }) + 'Z'
        $representation = Invoke-RepresentationFixture "lossless-utc-$digits" -Time $time
        [IO.File]::Delete($representation.readbackPath)
        [IO.File]::Delete($representation.publicationPath)
        $null = Test-DerivedSuccess $representation
        Check $true "Lossless UTC $digits fractions needs no invented historical CLI artifact"
    }
    $representation = Invoke-RepresentationFixture 'lossless-rejections'
    $request = Get-DerivedRequest $representation
    $options = $representation.options
    Entry Consume @{ receiptPath = $representation.path } -Failure 'String' | Out-Null
    foreach ($case in @(
        @{ field = 'workState'; value = 'ci-wait'; failure = 'changes only' },
        @{ field = 'heldDecision'; value = $false; failure = 'joined/quiescent' },
        @{ field = 'heldDecision'; value = ''; failure = 'joined/quiescent' },
        @{ field = 'nativeIdentity'; value = 'foreign'; failure = 'identity/authority' },
        @{ field = 'branch'; value = 'foreign'; failure = 'identity/authority' },
        @{ field = 'datasetId'; value = 'foreign'; failure = 'identity/authority' },
        @{ field = 'executionId'; value = 'foreign'; failure = 'admitted execution' },
        @{ field = 'ownerId'; value = 'foreign'; failure = 'Cross-owner' },
        @{ field = 'publicationPolicy'; value = 'local-only'; failure = 'identity/authority' },
        @{ field = 'autoMerge'; value = @{ mergeMethod = 'SQUASH'; enabledAt = '2026-10-27T12:33:17Z' }; failure = 'Missing field: method' },
        @{ field = 'autoMerge'; value = @{ method = 'SQUASH'; mergeMethod = 'SQUASH'; enabledAt = '2026-10-27T12:33:17Z' }; failure = 'Unknown field' },
        @{ field = 'autoMerge'; value = @{ method = 'MERGE'; enabledAt = '2026-10-27T12:33:17Z' }; failure = 'changes only' },
        @{ field = 'extra'; value = 1; failure = 'Unknown field' })) {
        $copy = Copy-FixtureValue $representation.receipt; $copy[$case.field] = $case.value
        $badPath = Join-Path (Split-Path -Parent $representation.path) "representation-$([guid]::NewGuid().ToString('N')).json"
        Write-ProofJson $copy $badPath
        $badRequest = Copy-FixtureValue $request
        $badRequest.receiptPath = $badPath; $badRequest.receiptSha256 = (Get-FileHash -LiteralPath $badPath).Hash
        Entry Consume $badRequest @options -Failure $case.failure | Out-Null
        Check $true "Lossless representation rejects other normalization: $($case.field)"
    }
    foreach ($time in @('2026-10-27T12:33:17', '2026-10-27T12:33:17+00:00', '2026-10-27T12:33:17+01:00',
        '2026-10-27T12:33:17.12345678Z', '2026-10-27T12:33:17z', '2026-02-30T12:33:17Z', '2026-10-27T12:33:60Z')) {
        $copy = Copy-FixtureValue $representation.receipt; $copy.autoMerge.enabledAt = $time
        Write-ProofJson $copy $representation.path
        Entry Consume (Get-DerivedRequest $representation) @options -Failure $(if ($time -cin
            @('2026-02-30T12:33:17Z', '2026-10-27T12:33:60Z')) { 'Gregorian UTC' } else { 'explicit UTC' }) | Out-Null
        Check $true "Lossless representation rejects invalid time: $time"
    }
    Write-ProofJson $representation.receipt $representation.path

    $absence = Invoke-RepresentationFixture 'absence-558' NoHeldDecisionV1 '2026-10-27T12:44:59Z'
    $absenceOptions = $absence.options
    Entry Consume @{ receiptPath = $absence.path } -Failure 'not proved quiescent' | Out-Null
    Entry Consume (Get-DerivedRequest $absence) -WireProfile UtcRepresentationV1 -Failure 'Unknown field' | Out-Null
    foreach ($inputName in @('receipt', 'stop', 'delivery')) {
        $path = if ($inputName -ceq 'receipt') { $absence.path } else { $absence[$inputName + 'Path'] }
        $saved = [IO.File]::ReadAllBytes($path)
        foreach ($decision in @($true, 'false', '', 0, @{}, @(), $null)) {
            $copy = Copy-FixtureValue $absence[$inputName]; $copy.heldDecision = $decision
            Write-ProofJson $copy $path
            Entry Consume (Get-DerivedRequest $absence) @absenceOptions -Failure 'Equivalent absence' | Out-Null
            Check $true "Equivalent absence rejects non-literal-false $inputName decision $($decision | ConvertTo-Json -Compress)"
        }
        foreach ($case in @(
            @{ field = 'finiteCommandsJoined'; value = $false }, @{ field = 'finiteCommandsJoined'; value = 'true' },
            @{ field = 'runningCommands'; value = @('held') }, @{ field = 'runningCommands'; value = $null },
            @{ field = 'ownerId'; value = 'foreign' }, @{ field = 'nativeIdentity'; value = 'foreign' },
            @{ field = 'executionId'; value = 'foreign' }, @{ field = 'unknown'; value = 'rejected' })) {
            $copy = Copy-FixtureValue $absence[$inputName]; $copy[$case.field] = $case.value
            Write-ProofJson $copy $path
            Entry Consume (Get-DerivedRequest $absence) @absenceOptions -Failure $(if ($case.field -ceq 'unknown') { 'Unknown field' } else { 'Equivalent absence' }) | Out-Null
            Check $true "Equivalent absence rejects contradictory $inputName $($case.field)"
        }
        [IO.File]::WriteAllBytes($path, $saved)
    }
    foreach ($case in @(
        @{ input = 'stop'; field = 'implementation'; value = @{ head = 'a' * 40 }; failure = 'Missing field' },
        @{ input = 'stop'; field = 'kind'; value = 'idle'; failure = 'distinct original' },
        @{ input = 'delivery'; field = 'head'; value = 'a' * 40; failure = 'source mismatch' },
        @{ input = 'delivery'; field = 'sourceAndBehaviorProof'; value = 'wrong.json'; failure = 'distinct original' },
        @{ input = 'delivery'; field = 'terminalReceipt'; value = 'wrong.json'; failure = 'distinct original' },
        @{ input = 'delivery'; field = 'publisher'; value = 'wrong'; failure = 'authority mismatch' },
        @{ input = 'delivery'; field = 'repository'; value = 'github.com/other/repo'; failure = 'distinct original' })) {
        $path = $absence[$case.input + 'Path']; $saved = [IO.File]::ReadAllBytes($path)
        $copy = Copy-FixtureValue $absence[$case.input]; $copy[$case.field] = $case.value
        Write-ProofJson $copy $path
        Entry Consume (Get-DerivedRequest $absence) @absenceOptions -Failure $case.failure | Out-Null
        [IO.File]::WriteAllBytes($path, $saved)
        Check $true "Equivalent absence requires separate source-bound $($case.input) $($case.field)"
    }
    $absence.result = Test-DerivedSuccess $absence

    foreach ($dispatchProfile in @('MissingNativeEvidenceV1', 'MissingLaunchBlockerV1')) {
        $current = Invoke-OriginalDispatchFixture "original-$dispatchProfile" $dispatchProfile -CombinedHostProof
        $options = $current.options; $request = Get-DerivedRequest $current
        Entry Consume @{ receiptPath = $current.path } -Failure $(if ($dispatchProfile -ceq 'MissingNativeEvidenceV1') { 'identity/authority' } else { 'Missing field: eventId' }) | Out-Null
        foreach ($case in @(
            @{ field = 'datasetId'; value = 'foreign'; failure = 'current original dispatcher' },
            @{ field = 'ownerId'; value = 'unrelated-old-intent'; failure = 'current original dispatcher' },
            @{ field = 'executionId'; value = 'foreign'; failure = 'current original dispatcher' },
            @{ field = 'workState'; value = 'idle'; failure = 'not stopped' },
            @{ field = 'finiteCommandsJoined'; value = $false; failure = 'joined/quiescent' },
            @{ field = 'finiteCommandsJoined'; value = 'true'; failure = 'joined/quiescent' },
            @{ field = 'runningCommands'; value = @('unjoined'); failure = 'joined/quiescent' },
            @{ field = 'runningCommands'; value = $null; failure = 'joined/quiescent' },
            @{ field = 'heldDecision'; value = $false; failure = 'joined/quiescent' },
            @{ field = 'heldDecision'; value = 'held'; failure = 'joined/quiescent' },
            @{ field = 'nativeIdentity'; value = 'claimed-different-actor'; failure = $(if ($dispatchProfile -ceq 'MissingNativeEvidenceV1') { 'exact missing-native' } else { 'Unknown field' }) },
            @{ field = 'nativeIdentity'; value = $current.host.launches[0].nativeIdentity; failure = $(if ($dispatchProfile -ceq 'MissingNativeEvidenceV1') { 'exact missing-native' } else { 'Unknown field' }) },
            @{ field = 'publicationPolicy'; value = 'local-only'; failure = 'not stopped' },
            @{ field = 'unknown'; value = 1; failure = 'Unknown field' })) {
            $copy = Copy-FixtureValue $current.receipt; $copy[$case.field] = $case.value
            $badPath = Join-Path (Split-Path -Parent $current.path) "original-bad-$([guid]::NewGuid().ToString('N')).json"
            Write-ProofJson $copy $badPath
            $badRequest = Copy-FixtureValue $request
            $badRequest.receiptPath = $badPath; $badRequest.receiptSha256 = (Get-FileHash -LiteralPath $badPath).Hash
            Entry Consume $badRequest @options -Failure $case.failure | Out-Null
            Check $true "$dispatchProfile rejects original $($case.field)"
        }
        $hostBytes = [IO.File]::ReadAllBytes($current.hostPath)
        foreach ($case in @(
            @{ field = 'dispatcher'; value = 'ahp-session:/f2e00000-0000-0000-0000-000000000000' },
            @{ field = 'executionId'; value = 'execution-5027' }, @{ field = 'ownerId'; value = 'unrelated-old-intent' },
            @{ field = 'toolName'; value = 'read_sessions' }, @{ field = 'toolCallId'; value = '' },
            @{ field = 'startEventId'; value = 'invalid' }, @{ field = 'completionEventId'; value = 'invalid' },
            @{ field = 'nativeIdentity'; value = 'agent-host-session://ahp-session/df16fb09-8fde-4d97-b9ec-a733600c4f1f?chat=00000000-0000-0000-0000-000000000000' },
            @{ field = 'originalCallSucceeded'; value = $false }, @{ field = 'originalCallSucceeded'; value = 'true' },
            @{ field = 'originalDispatcherCallJoined'; value = $false }, @{ field = 'originalDispatcherCallJoined'; value = 'true' },
            @{ field = 'initialHeaderSha256'; value = 'invalid' },
            @{ field = 'completedUtc'; value = '2026-10-27T12:00:00Z' },
            @{ field = 'completedUtc'; value = '2026-10-28T12:00:00Z' })) {
            $copy = Copy-FixtureValue $current.host; $copy.launches[0][$case.field] = $case.value
            Write-ProofJson $copy $current.hostPath
            Entry Consume (Get-DerivedRequest $current) @options -Failure 'host launch proof' | Out-Null
            Check $true "$dispatchProfile rejects forged/unjoined original host $($case.field)"
        }
        foreach ($mode in @('duplicate', 'event-alias', 'wrong-dataset', 'wrong-dispatcher', 'title-inference', 'unknown')) {
            $copy = Copy-FixtureValue $current.host
            switch ($mode) {
                duplicate { $copy.launches += ,(Copy-FixtureValue $copy.launches[0]) }
                event-alias { $copy.launches[0].completionEventId = $copy.launches[0].startEventId }
                wrong-dataset { $copy.datasetId = 'foreign' }
                wrong-dispatcher { $copy.coordinator = 'foreign' }
                title-inference { $copy.source = 'inferred from title/shared parent' }
                unknown { $copy.unknown = 'rejected' }
            }
            Write-ProofJson $copy $current.hostPath
            Entry Consume (Get-DerivedRequest $current) @options -Failure $(if ($mode -ceq 'unknown') { 'Unknown field' } elseif ($mode -cin @('duplicate', 'event-alias')) { 'Ambiguous' } else { 'host launch proof' }) | Out-Null
            Check $true "$dispatchProfile rejects ambiguous or inferred launch: $mode"
        }
        [IO.File]::WriteAllBytes($current.hostPath, $hostBytes)
        foreach ($case in @(
            @{ field = 'sourceHead'; value = 'a' * 40; failure = $(if ($dispatchProfile -ceq 'MissingNativeEvidenceV1') { 'exact missing-native' } else { 'source/authority' }) },
            @{ field = 'hostLaunchProofSha256'; value = 'a' * 64; failure = 'SHA256 mismatch' },
            @{ field = 'hostLaunchProofPath'; value = $current.path; failure = 'canonical registered' },
            @{ field = 'receiptPath'; value = $current.path + ':stream'; failure = 'canonical registered' },
            @{ field = 'command'; value = 'never run'; failure = 'Unknown field' })) {
            $badRequest = Copy-FixtureValue $request; $badRequest[$case.field] = $case.value
            Entry Consume $badRequest @options -Failure $case.failure | Out-Null
            Check $true "$dispatchProfile rejects request $($case.field)"
        }
        if ($dispatchProfile -ceq 'MissingLaunchBlockerV1') {
            foreach ($field in @($current.receipt.effects.Keys)) {
                $copy = Copy-FixtureValue $current.receipt; $copy.effects[$field] = $true
                Write-ProofJson $copy $current.path
                Entry Consume (Get-DerivedRequest $current) @options -Failure 'changed or unknown work' | Out-Null
                Check $true "Blocker rejects claimed effect: $field"
            }
            foreach ($field in @('initialHead', 'initialBranch', 'initialWorktree', 'initialWorkingTreeClean')) {
                $copy = Copy-FixtureValue $current.receipt
                $copy.preflight[$field] = if ($field -ceq 'initialWorkingTreeClean') { $false } else { 'foreign' }
                Write-ProofJson $copy $current.path
                Entry Consume (Get-DerivedRequest $current) @options -Failure 'source/authority' | Out-Null
                Check $true "Blocker rejects mismatched preflight $field"
            }
            Write-ProofJson $current.receipt $current.path
        }
        $dirtyPath = Join-Path $script:repositoryPath 'changed-work.txt'
        [IO.File]::WriteAllText($dirtyPath, 'Uncommitted work must block recovery')
        Entry Consume (Get-DerivedRequest $current) @options -Failure 'source changed or unavailable' | Out-Null
        [IO.File]::Delete($dirtyPath)
        Invoke-Checked git @('-C', $script:repositoryPath, 'switch', '--quiet', '-c', 'different-source')
        Entry Consume (Get-DerivedRequest $current) @options -Failure 'source changed or unavailable' | Out-Null
        Invoke-Checked git @('-C', $script:repositoryPath, 'switch', '--quiet', 'main')
        $current.result = Test-DerivedSuccess $current
        $current.request = Get-DerivedRequest $current
        $current.replayOptions = $options
        if ($dispatchProfile -ceq 'MissingNativeEvidenceV1') { $nativeFixture = $current } else { $blockerFixture = $current }
    }

    foreach ($derived in @($representation, $absence, $nativeFixture, $blockerFixture)) {
        $script:repositoryPath = $derived.snapshot.identity.workspace
        $script:fixtureLeader = if ($derived.options.Contains('DispatchProfile')) { $derived.host.coordinator } else { 'fixture-leader' }
        $null = Entry Read
        $null = Entry Claim
        $options = $derived.options
        $request = Get-DerivedRequest $derived
        Entry Read $request @options -Failure 'only supported for Consume' | Out-Null
        Entry Consume $request @options -Recovery -Failure 'exactly one' | Out-Null
        Entry Consume $request @options -Leader 'foreign' -Failure 'Current unexpired leader' | Out-Null
        Entry Consume $request @options -Fence @{ epoch = 0; revision = $script:state.revision } -Failure 'Stale epoch/revision' | Out-Null
        Entry Consume $request @options -Fence @{ epoch = $script:state.epoch; revision = 0 } -Failure 'Stale epoch/revision' | Out-Null
        $result = if ($derived.Contains('result')) { $derived.result } else { Test-DerivedSuccess $derived }
        $paths = @($request.Keys | Where-Object { $_ -clike '*Path' } | ForEach-Object { $request[$_] })
        $paths += @($result.terminalRecovery.proofPath, $result.terminalRecovery.projectionPath,
            $result.terminalRecovery.proofPath.Replace('.proof.json', '.request.json'))
        foreach ($path in $paths) {
            $saved = [IO.File]::ReadAllBytes($path)
            $hidden = $path + '.fixture-held'
            [IO.File]::Move($path, $hidden)
            try { Entry Consume $request @options -Failure $(if ($path -clike '*terminal-recovery*' -and
                    $path -cne $derived.path) { 'Missing immutable' } else { 'does not exist' }) | Out-Null }
            finally { [IO.File]::Move($hidden, $path) }
            [IO.File]::AppendAllText($path, ' ')
            Entry Consume $request @options -Failure $(if ($path -cin @($request.Keys | Where-Object { $_ -clike '*Path' } |
                ForEach-Object { $request[$_] })) { 'SHA256 mismatch' } else { 'Immutable recovery' }) | Out-Null
            if ($path -cin @($request.Keys | Where-Object { $_ -clike '*Path' } | ForEach-Object { $request[$_] })) {
                Entry Consume (Get-DerivedRequest $derived) @options -Failure 'Immutable recovery evidence changed' | Out-Null
            }
            [IO.File]::WriteAllBytes($path, $saved)
            $held = [IO.FileStream]::new($path, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try { Entry Consume $request @options -Failure 'being used by another process' | Out-Null }
            finally { $held.Dispose() }
            Check ((Entry Consume $request @options).kind -ceq 'duplicate') "Derived missing/changed/racing proof is never a second release: $(Split-Path -Leaf $path)"
        }
    }
    $concurrentDerived = Invoke-OriginalDispatchFixture 'original-dispatch-concurrent'
    $options = $concurrentDerived.options; $request = Get-DerivedRequest $concurrentDerived
    $one = Invoke-EntryProcess Consume $request @options
    $two = Invoke-EntryProcess Consume $request @options
    $results = @((Complete-Entry $one -Concurrent), (Complete-Entry $two -Concurrent))
    Check (@($results | Where-Object kind -CEQ released).Count -eq 1) 'Concurrent current-original-dispatch processes release once'
    $null = Entry Read
    Check ((Entry Consume $request @options).kind -ceq 'duplicate') 'Concurrent current-original-dispatch exact replay remains duplicate'
    $oldDispatch = Invoke-OriginalDispatchFixture 'different-original-epoch-dispatcher'
    $registryPath = Join-Path $oldDispatch.snapshot.identity.directory 'registry.json'
    $oldState = Read-CoordinatorJson $registryPath -MaximumBytes 1048576
    $oldState.leaseUntil = '2000-01-01T00:00:00.0000000+00:00'
    Write-ProofJson $oldState $registryPath
    [IO.File]::Delete((Join-Path $oldDispatch.snapshot.identity.directory 'pending-transition.json'))
    $newLeader = 'ahp-session:/f2e00000-0000-0000-0000-000000000000'
    $null = Entry Read
    $null = Entry Claim -Leader $newLeader
    $options = $oldDispatch.options
    Entry Consume (Get-DerivedRequest $oldDispatch) @options -Leader $newLeader -Failure 'current original dispatcher/epoch' | Out-Null
    Check (@((Entry Read).state.executions | Where-Object phase -CNE released).Count -eq 2) 'Different original epoch/dispatcher cannot release either old charge'
}

try {
    Test-DerivedProfileSuite
    $existing = Test-ExistingProfileSuite
    $summary = @{ version = 1; passed = $passed.ToArray(); processInvocations = $script:sequence
        successfulFixtureCommand = $existing.successfulInvocation.arguments
        proofPath = $existing.released.terminalRecovery.proofPath; liveDatasetTouched = $false }
    Write-ProofJson $summary (Join-Path $OutputDirectory 'summary.json')
    $summary | ConvertTo-Json -Depth 10
} catch {
    Write-ProofJson @{ passed = $passed.ToArray(); failure = $_.Exception.Message; processInvocations = $script:sequence } `
        (Join-Path $OutputDirectory 'failure.json')
    throw
}
