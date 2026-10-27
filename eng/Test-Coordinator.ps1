#requires -Version 7.5
[CmdletBinding()]
param([Parameter(Mandatory)][string] $OutputDirectory)
. (Join-Path $PSScriptRoot 'Coordinator.Common.ps1')
New-ProofDirectory $OutputDirectory
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$entry = Join-Path $PSScriptRoot 'Invoke-Coordinator.ps1'
$passed = [Collections.Generic.List[string]]::new()
$script:invocations = 0
$script:fixture = $null
$script:snapshot = $null
$stopwatch = [Diagnostics.Stopwatch]::StartNew()

function Check {
    param([bool] $Condition, [string] $Name)
    if (!$Condition) { throw "Failed: $Name" }
    $passed.Add($Name)
}

function Start-Entry {
    param([string] $Action, $Request = @{}, [string] $Leader = 'native-leader', $Snapshot = $null)
    $script:invocations++
    $prefix = Join-Path $OutputDirectory ("process-" + $script:invocations)
    $requestPath = "$prefix.request.json"
    Write-ProofJson $Request $requestPath
    $arguments = @('-NoProfile', '-File', $entry, '-RepositoryPath', $script:fixture,
        '-Session', $Leader, '-RequestPath', $requestPath)
    if ($Action) { $arguments += @('-Action', $Action) }
    if ($null -ne $Snapshot) {
        $arguments += @('-ExpectedEpoch', [string]$Snapshot.epoch, '-ExpectedRevision', [string]$Snapshot.revision)
    }
    $info = [Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })))
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    if (!$process.Start()) { throw 'Fixture process failed to start.' }
    return @{ process = $process; output = $process.StandardOutput.ReadToEndAsync()
        error = $process.StandardError.ReadToEndAsync(); prefix = $prefix; arguments = $arguments }
}

function Complete-Entry {
    param($Invocation, [string] $Failure = '')
    try {
        $Invocation.process.WaitForExit()
        $stdout = $Invocation.output.GetAwaiter().GetResult()
        $stderr = $Invocation.error.GetAwaiter().GetResult()
        [IO.File]::WriteAllText(($Invocation.prefix + '.stdout.json'), $stdout)
        [IO.File]::WriteAllText(($Invocation.prefix + '.stderr.txt'), $stderr)
        Write-ProofJson @{ arguments = $Invocation.arguments; exitCode = $Invocation.process.ExitCode } ($Invocation.prefix + '.result.json')
        if ($Failure) {
            if ($Invocation.process.ExitCode -eq 0 -or !$stderr.Contains($Failure)) {
                throw "Expected '$Failure', got exit $($Invocation.process.ExitCode): $stderr $stdout"
            }
            return $null
        }
        if ($Invocation.process.ExitCode -ne 0) { throw "Entry point failed: $stderr $stdout" }
        return $stdout | ConvertFrom-Json -AsHashtable -DateKind String
    } finally { $Invocation.process.Dispose() }
}

function Entry {
    param([string] $Action, $Request = @{}, [string] $Failure = '', [string] $Leader = 'native-leader', $Snapshot = $null)
    if ($null -eq $Snapshot -and $Action -cnotin @('Initialize', 'Adopt', 'Read')) { $Snapshot = $script:snapshot }
    $result = Complete-Entry (Start-Entry $Action $Request $Leader $Snapshot) $Failure
    if ($null -ne $result) {
        if ($result.Contains('state')) { $script:snapshot = $result.state }
        elseif ($result.Contains('revision')) { $script:snapshot = @{ epoch = $result.epoch; revision = $result.revision } }
    }
    return $result
}

function New-Fixture {
    param([string] $Name, [string] $Policy = 'ready-auto', [switch] $Uninitialized)
    $script:fixture = Join-Path $OutputDirectory $Name
    $script:snapshot = $null
    New-ProofDirectory $script:fixture
    & git init --quiet $script:fixture
    if ($LASTEXITCODE -ne 0) { throw 'Fixture git init failed.' }
    & git -C $script:fixture remote add origin 'https://github.com/roryprimrose/Kora.git'
    if ($LASTEXITCODE -ne 0) { throw 'Fixture origin registration failed.' }
    '.scratch/' | Set-Content -LiteralPath (Join-Path $script:fixture '.gitignore')
    if ($Uninitialized) { return }
    $result = Entry 'Initialize' @{ legacyAbsenceConfirmed = $true; authorityId = 'fixture-user-authority'
        publicationPolicy = $Policy; publisher = 'fixture-publisher' }
    return $result.state
}

function Register-Owner {
    param([string] $Id, [string] $Allocation = 'none', [string] $Kind = 'feature', [string] $Scope = '', [string] $Operation = '',
        [string[]] $Scopes = @())
    if (!$Scope) { $Scope = "src/$Id" }
    if (!$Scopes.Count) { $Scopes = @($Scope) }
    if (!$Operation) { $Operation = $Id }
    Entry 'Register' @{ ownerId = $Id; operation = $Operation; scopes = $Scopes
        kind = $Kind; allocation = $Allocation; evidence = 'Explicit fixture producer lifecycle registration; no native execution.' } | Out-Null
}

function Certify-Owner {
    param([string] $Id, [bool] $Repair = $false, [string] $Observed = ([DateTimeOffset]::UtcNow.ToString('o')))
    Entry 'Certify' @{ ownerId = $Id; repair = $Repair; certification = @{
        sourceHead = 'a' * 40; inputDigest = 'b' * 64; prObservation = 'complete-offline-pr-fixture'
        observedUtc = $Observed; approvedOutcome = 'Explicit fixture approved complete operation'; placement = 'Host owned fixture'
    }} | Out-Null
}

function Accept-Dispatch {
    param($Dispatch, [string] $Id)
    Entry 'Reconcile' @{ executionId = $Dispatch.action.executionId; outcome = 'accepted'; evidence = 'Exact fixture native creation return'
        dispatcherJoined = $true; nativeIdentity = "native-fixture:$Id"; branch = "fixture/$Id"
        worktree = (Join-Path $OutputDirectory $Id) } | Out-Null
}

function New-Handoff {
    param([string] $Id, [string] $ExecutionId, [string] $Event = 'handoff-one', [int] $Revision = 1)
    $state = (Entry 'Read').state
    $owner = @($state.owners | Where-Object id -CEQ $Id)[0]
    $receipt = @{
        version = 1; datasetId = $state.datasetId; eventId = $Event; ownerId = $Id; executionId = $ExecutionId
        revision = $Revision; nativeIdentity = $owner.nativeIdentity; branch = $owner.branch; worktree = $owner.worktree
        head = 'c' * 40; kind = 'pr-enrolled-ci-wait'; workState = 'stopped'; finiteCommandsJoined = $true
        runningCommands = @(); heldDecision = $null; publicationPolicy = $state.publicationPolicy
        prUrl = 'https://github.com/roryprimrose/Kora/pull/1'
        autoMerge = @{ method = 'SQUASH'; enabledAt = [DateTimeOffset]::UtcNow.ToString('o') }
    }
    $path = Join-Path (Join-Path (Join-Path $state.identity.directory 'owners') $Id) "$Event.json"
    Write-ProofJson $receipt $path
    return @{ path = $path; receipt = $receipt }
}

function New-TerminationEvidence {
    param([string] $Subject)
    return @{ kind = 'joined-command'; subject = $Subject; reference = 'offline fixture completion receipt'
        executionIdentity = 'fixture exact command and creation identity'; hostIdentity = 'offline fixture host'
        continuationBoundary = 'Fixture has no native execution or possible later continuation' }
}

function New-LegacyFixture {
    param([string] $Name, [int] $ExternalCharges = 1)
    $null = New-Fixture $Name -Uninitialized
    $identity = Get-CoordinatorLocation $script:fixture
    $root = Join-Path (Join-Path $identity.commonGit '.scratch') 'roadmap-coordinator'
    New-ProofDirectory $root
    [IO.File]::WriteAllBytes((Join-Path $root 'writer.lock'), [byte[]]@())
    $dataset = @{ protocolVersion = 2; schemaVersion = 2; datasetId = [guid]::NewGuid().ToString()
        repository = $identity.repository; commonGitDirectory = $identity.commonGit }
    $registry = @{ protocolVersion = 2; schemaVersion = 2; datasetId = $dataset.datasetId
        repository = $identity.repository; commonGitDirectory = $identity.commonGit
        revision = 12; coordinatorEpoch = 3; leader = @{ session = 'old-native-coordinator' }
        activeExecutionAllocations = @(@{ ownerSession = 'old-search-owner'; executionTransitionId = 'search-one'; allocationCharge = 1 })
        externalExecutionCharges = @(foreach ($index in 1..$ExternalCharges) {
            @{ owner = "external-$index"; chargeId = "external-$index"; allocationCharge = 1 }
        })
        retainedOwnership = @(@{ feature = 'unfinished'; ownerSession = 'retained-original'; branch = 'original/unfinished'
            worktree = (Join-Path $OutputDirectory 'retained-unfinished') })
        runs = @(@{ publicationPolicy = 'local-only'; grants = @('original-unmodified-grant') })
        wakeBudget = @{ spent = 6; remaining = 14; priorExpenditure = 'Unknown' }
        pendingDispatches = @(@{ actionId = 'ambiguous-original-action'; state = 'accepted' })
        processedEventIds = @('original-event'); unknownRetainedField = 'must-not-disappear' }
    $repairRoot = Join-Path (Join-Path $script:fixture '.scratch') 'pr-repair-orchestration'
    New-ProofDirectory $repairRoot
    [IO.File]::WriteAllBytes((Join-Path $repairRoot 'coordinator.lock'), [byte[]]@())
    $repair = @{ repository = $identity.repository; revision = 9; schedulingOwner = @{ session = 'old-repair-writer' }
        currentOwners = @(@{ pr = 473; reservationId = 'original-pr473-repair'; session = 'original-repair-owner'
            branch = 'original/repair'; actualWorktree = (Join-Path $OutputDirectory 'original-repair') }) }
    Write-ProofJson $dataset (Join-Path $root 'dataset.json')
    Write-ProofJson $registry (Join-Path $root 'registry.json')
    Write-ProofJson $repair (Join-Path $repairRoot 'coordinator.json')
    return @{ legacyDirectory = $root; datasetDigest = (Get-CoordinatorDigest $dataset)
        registryDigest = (Get-CoordinatorDigest $registry); repairDigest = (Get-CoordinatorDigest $repair)
        authorityId = 'fixture-authorized-quiescent-adoption'; inventoryEvidence = 'Complete offline two-ledger inventory'
        quiescenceEvidence = @((New-TerminationEvidence 'old-native-coordinator'), (New-TerminationEvidence 'old-repair-writer')) }
}

try {
    foreach ($file in @($entry, (Join-Path $PSScriptRoot 'Coordinator.Common.ps1'),
        (Join-Path $PSScriptRoot 'Coordinator.Legacy.ps1'), $PSCommandPath)) {
        $tokens = $null
        $errors = $null
        [Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$errors) | Out-Null
        Check ($errors.Count -eq 0) "PowerShell parses: $(Split-Path -Leaf $file)"
    }
    $initial = New-Fixture 'restart'
    $second = Entry 'Read'
    Check ($second.state.datasetId -ceq $initial.datasetId -and $second.state.authorityId -ceq 'fixture-user-authority' -and
        $second.state.publicationPolicy -ceq 'ready-auto' -and $second.state.publisher -ceq 'fixture-publisher' -and
        $second.state.revision -eq 0) 'Second independent process recovers same canonical dataset, publication policy and pinned publisher'
    Check ($second.monitoring -ceq 'resumable-not-continuously-monitoring') 'Offline state never claims host continuation'
    Check ((Complete-Entry (Start-Entry '')).kind -ceq 'snapshot') 'Default public entry-point action is read-only recovery'
    Entry 'Initialize' @{ legacyAbsenceConfirmed = $true; authorityId = 'replacement'
        publicationPolicy = 'ready-auto'; publisher = 'fixture-publisher' } -Failure 'Dataset already exists' | Out-Null
    $externalWorktree = Join-Path $OutputDirectory 'linked-worktree'
    & git -C $script:fixture -c user.name=Fixture -c user.email=fixture@example.invalid -c commit.gpgsign=false -c core.hooksPath= add .gitignore
    & git -C $script:fixture -c user.name=Fixture -c user.email=fixture@example.invalid -c commit.gpgsign=false -c core.hooksPath= commit --quiet -m 'Offline fixture'
    if ($LASTEXITCODE -ne 0) { throw 'Fixture commit failed.' }
    & git -C $script:fixture worktree add --quiet -b fixture-linked $externalWorktree
    if ($LASTEXITCODE -ne 0) { throw 'Fixture worktree failed.' }
    $primary = $script:fixture
    $script:fixture = $externalWorktree
    Check ((Entry 'Read').state.datasetId -ceq $initial.datasetId) 'Different native worktree discovers same primary-workspace state'
    $script:fixture = $primary

    $null = New-Fixture 'exact-scopes'
    $exactScopes = @(1..25 | ForEach-Object { "src/shared/File$_.cs" })
    Register-Owner 'exact-owner' -Kind historical -Scopes $exactScopes
    $maximumScopes = @(1..128 | ForEach-Object { "tests/exact/File$_.cs" })
    Register-Owner 'maximum-owner' -Kind historical -Scopes $maximumScopes
    $scopeState = (Entry 'Read').state
    Check ($scopeState.owners[0].scopes.Count -eq 25 -and $scopeState.owners[1].scopes.Count -eq 128) 'Realistic exact-file scopes and the 128-path bound survive independent process readback'
    $invalidScopes = @(
        @{ name = 'over-limit'; scopes = @($maximumScopes + 'tests/exact/File129.cs'); failure = 'Invalid source scopes' },
        @{ name = 'empty'; scopes = @(); failure = 'Invalid source scopes' },
        @{ name = 'scalar'; scopes = 'src/file.cs'; failure = 'Invalid source scopes' },
        @{ name = 'traversal'; scopes = @($exactScopes + 'src/../file.cs'); failure = 'Invalid source scope path' },
        @{ name = 'dot'; scopes = @($exactScopes + 'src/./file.cs'); failure = 'Invalid source scope path' },
        @{ name = 'absolute'; scopes = @($exactScopes + 'Q:\src\file.cs'); failure = 'Invalid source scope path' },
        @{ name = 'backslash'; scopes = @($exactScopes + 'src\file.cs'); failure = 'Invalid source scope path' },
        @{ name = 'wildcard'; scopes = @($exactScopes + 'src/*.cs'); failure = 'Invalid source scope path' },
        @{ name = 'null-path'; scopes = @($exactScopes + $null); failure = 'Invalid source scope path' }
    )
    foreach ($case in $invalidScopes) {
        Entry 'Register' @{ ownerId = $case.name; operation = $case.name; scopes = $case.scopes
            kind = 'feature'; allocation = 'uncertain'; evidence = 'Invalid scope fixture' } -Failure $case.failure | Out-Null
        $unchanged = (Entry 'Read').state
        Check ($unchanged.revision -eq $scopeState.revision -and $unchanged.owners.Count -eq 2 -and
            $unchanged.executions.Count -eq 0) "Rejected $($case.name) scopes do not mutate ownership or allocations"
    }
    Register-Owner 'tail-overlap' -Scope 'SRC/shared/File25.cs'
    Certify-Owner 'tail-overlap'
    Register-Owner 'disjoint' -Scope 'src/shared/File26.cs'
    Certify-Owner 'disjoint'
    $disjoint = Entry 'Next'
    Check ($disjoint.kind -ceq 'dispatch' -and $disjoint.action.ownerId -ceq 'disjoint') 'Exact-file ownership admits a sibling without broad directory serialization'
    Accept-Dispatch $disjoint 'disjoint'
    $tailConflict = Entry 'Next'
    Check (@($tailConflict.action.constraints | Where-Object ownerId -CEQ tail-overlap).Count -eq 1) 'Scope overlap remains case-insensitive beyond the former sixteen-path limit'

    $opaqueCases = @(
        @{ owner = 'pr-541-repair'; event = 'pr541-repair-blocked-20261027T052658Z'; head = 'd4afbcc0bd5d4c29f70e956b3be0daec47ac0a39' },
        @{ owner = 'response-mode-proposal-delivery'; event = 'response-mode-proposal-delivery-execution-2eee84fafcac4e9e829db172544eb304-blocked-1'
            head = 'e225439253eef6e6107002d67995a320a5017db6' }
    )
    foreach ($case in $opaqueCases) {
        $null = New-Fixture "opaque-$($case.owner)"
        Register-Owner $case.owner
        Certify-Owner $case.owner
        $dispatch = Entry 'Next'
        Accept-Dispatch $dispatch $case.owner
        $handoff = New-Handoff $case.owner $dispatch.action.executionId $case.event
        $handoff.receipt.kind = 'blocked-quiescent'
        $handoff.receipt.prUrl = $null
        $handoff.receipt.autoMerge = $null
        $handoff.receipt.head = $case.head
        Write-ProofJson $handoff.receipt $handoff.path
        $originalDigest = Get-CoordinatorDigest (Read-CoordinatorJson $handoff.path)
        $released = Entry 'Consume' @{ receiptPath = $handoff.path }
        $duplicate = Entry 'Consume' @{ receiptPath = $handoff.path }
        $opaqueState = (Entry 'Read').state
        Check ($released.kind -ceq 'released' -and $duplicate.kind -ceq 'duplicate' -and $duplicate.charged -eq 0 -and
            $opaqueState.events.Count -eq 1 -and $opaqueState.events[0].id -ceq (Get-CoordinatorEventKey $case.event) -and
            $opaqueState.events[0].id -cmatch '^[a-z0-9][a-z0-9-]{0,79}$' -and
            $opaqueState.events[0].digest -ceq $originalDigest -and $opaqueState.owners[0].sourceRetained -and
            (Get-CoordinatorDigest (Read-CoordinatorJson $handoff.path)) -ceq $originalDigest) "Actual $($case.owner) opaque ID releases once without changing original evidence or actor identity"
        $handoff.receipt.head = 'd' * 40
        Write-ProofJson $handoff.receipt $handoff.path
        Entry 'Consume' @{ receiptPath = $handoff.path } -Failure 'reused with different content' | Out-Null
        $handoff.receipt.eventId = [guid]::NewGuid().ToString('N')
        $handoff.receipt.revision = 2
        Write-ProofJson $handoff.receipt $handoff.path
        Entry 'Consume' @{ receiptPath = $handoff.path } -Failure 'already released' | Out-Null
        Check ((Entry 'Read').state.events.Count -eq 1) "Opaque $($case.owner) changed-content and second-execution release attempts preserve once-only accounting"
    }

    $null = New-Fixture 'opaque-invalid'
    Register-Owner 'opaque-owner'
    Certify-Owner 'opaque-owner'
    $dispatch = Entry 'Next'
    Accept-Dispatch $dispatch 'opaque-owner'
    $handoff = New-Handoff 'opaque-owner' $dispatch.action.executionId
    foreach ($value in @($null, '', 42, '../escape', 'a/b', 'a\b', 'a b', '-leading', "a`nb", "a`n", "a$([char]127)b", "a$([char]133)b", ('a' * 257))) {
        $handoff.receipt.eventId = $value
        Write-ProofJson $handoff.receipt $handoff.path
        Entry 'Consume' @{ receiptPath = $handoff.path } -Failure 'Invalid opaque terminal event ID' | Out-Null
        $unchanged = (Entry 'Read').state
        Check ($unchanged.events.Count -eq 0 -and $unchanged.executions[0].phase -ceq 'accepted') 'Malformed opaque ID leaves execution charged and evidence unconsumed'
    }
    foreach ($id in @('Uppercase-actor', ('a' * 81))) {
        Entry 'Register' @{ ownerId = $id; operation = 'strict-actor'; scopes = @('src/strict')
            kind = 'feature'; allocation = 'none'; evidence = 'Actor validation remains strict' } -Failure 'Invalid coordinator identifier' | Out-Null
    }
    $handoff.receipt.eventId = 'A' + ('b' * 255)
    Write-ProofJson $handoff.receipt $handoff.path
    Check ((Entry 'Consume' @{ receiptPath = $handoff.path }).kind -ceq 'released') 'Opaque IDs accept exactly 256 safe ASCII characters while actor identifiers remain strict'

    $null = New-Fixture 'publication-correction'
    $id = 'terminal-ui-publication-fixture-repair'
    Register-Owner $id
    Certify-Owner $id
    $dispatch = Entry 'Next'
    Accept-Dispatch $dispatch $id
    $handoff = New-Handoff $id $dispatch.action.executionId '953b9511-6549-44f0-a7ad-80150f23f074'
    $handoff.receipt.head = '10b707ffe10c78c093aaadbb8a7b1b67d8f53b4b'
    $handoff.receipt.prUrl = 'https://github.com/roryprimrose/Kora/pull/549'
    $handoff.receipt.autoMerge.enabledAt = '2026-10-26T18:26:51.0000000+00:00'
    Write-ProofJson $handoff.receipt $handoff.path
    $originalBytes = [Convert]::ToBase64String([IO.File]::ReadAllBytes($handoff.path))
    $readback = @{ number = 549; url = $handoff.receipt.prUrl; headRefOid = $handoff.receipt.head; baseRefName = 'main'
        state = 'OPEN'; isDraft = $false; author = @{ login = 'fixture-publisher' }
        autoMergeRequest = @{ mergeMethod = 'SQUASH'; enabledAt = '2026-10-27T05:26:51Z'; enabledBy = @{ login = 'fixture-publisher' } } }
    $readbackPath = Join-Path $OutputDirectory 'authoritative-publication.json'
    Write-ProofJson $readback $readbackPath
    $correction = @{ receiptDigest = (Get-CoordinatorDigest $handoff.receipt); readbackPath = $readbackPath
        readbackDigest = (Get-CoordinatorDigest $readback); publisher = 'fixture-publisher'
        verificationEvidence = 'Offline fixture for authenticated gh principal/repo/same-PR/head/enrollment verification; not live proof' }
    $correctionRequest = @{ receiptPath = $handoff.path; publicationCorrection = $correction }
    $before = (Entry 'Read').state
    $badCorrections = @(
        @{ key = 'receiptDigest'; value = 'A' * 64; failure = 'exact receipt' },
        @{ key = 'readbackDigest'; value = 'A' * 64; failure = 'readback digest mismatch' },
        @{ key = 'readbackPath'; value = 'relative.json'; failure = 'exact receipt' },
        @{ key = 'readbackPath'; value = (Join-Path $OutputDirectory 'absent.json'); failure = 'does not exist' },
        @{ key = 'publisher'; value = 'other'; failure = 'pinned publisher' },
        @{ key = 'verificationEvidence'; value = ''; failure = 'Invalid coordinator text' },
        @{ key = 'head'; value = 'e' * 40; failure = 'Unknown field' }
    )
    foreach ($case in $badCorrections) {
        $copy = $correction | ConvertTo-Json | ConvertFrom-Json -AsHashtable -DateKind String
        $copy[$case.key] = $case.value
        Entry 'Consume' @{ receiptPath = $handoff.path; publicationCorrection = $copy } -Failure $case.failure | Out-Null
    }
    foreach ($field in @('url', 'number', 'headRefOid', 'baseRefName', 'state', 'isDraft', 'author', 'autoMergeRequest')) {
        $copy = $readback | ConvertTo-Json -Depth 10 | ConvertFrom-Json -AsHashtable -DateKind String
        $copy[$field] = switch ($field) {
            'url' { 'https://github.com/roryprimrose/Kora/pull/548' }
            'number' { 548 }
            'headRefOid' { 'd' * 40 }
            'baseRefName' { 'other' }
            'state' { 'MERGED' }
            'isDraft' { $true }
            'author' { @{ login = 'other' } }
            'autoMergeRequest' { $null }
        }
        Write-ProofJson $copy $readbackPath
        $correction.readbackDigest = Get-CoordinatorDigest $copy
        Entry 'Consume' $correctionRequest -Failure 'readback identity' | Out-Null
    }
    foreach ($case in @(
        @{ key = 'mergeMethod'; value = 'MERGE'; failure = 'identity/enrollment mismatch' },
        @{ key = 'enabledBy'; value = @{ login = 'other' }; failure = 'identity/enrollment mismatch' },
        @{ key = 'enabledAt'; value = '2026-10-27T05:26:51'; failure = 'explicit UTC' },
        @{ key = 'enabledAt'; value = '2026-10-26T18:26:51Z'; failure = 'correct enabledAt only' }
    )) {
        $copy = $readback | ConvertTo-Json -Depth 10 | ConvertFrom-Json -AsHashtable -DateKind String
        $copy.autoMergeRequest[$case.key] = $case.value
        Write-ProofJson $copy $readbackPath
        $correction.readbackDigest = Get-CoordinatorDigest $copy
        Entry 'Consume' $correctionRequest -Failure $case.failure | Out-Null
    }
    $unchanged = (Entry 'Read').state
    Check ((Get-CoordinatorDigest $unchanged) -ceq (Get-CoordinatorDigest $before)) 'Missing, changed-head/PR/publisher/base/enrollment and field-override correction attempts persist nothing'
    Write-ProofJson $readback $readbackPath
    $correction.readbackDigest = Get-CoordinatorDigest $readback
    Entry 'Consume' $correctionRequest -Failure 'Stale epoch/revision' -Snapshot @{ epoch = $before.epoch; revision = $before.revision - 1 } | Out-Null
    $released = Entry 'Consume' $correctionRequest
    $duplicate = Entry 'Consume' $correctionRequest
    $corrected = (Entry 'Read').state
    $proofPath = Join-Path (Join-Path $corrected.identity.directory 'transition-evidence') ($corrected.events[0].id + '.json')
    $proof = Read-CoordinatorJson $proofPath -MaximumBytes 1048576
    $publication = $corrected.owners[0].publication
    Check ($released.kind -ceq 'released' -and $duplicate.kind -ceq 'duplicate' -and $duplicate.charged -eq 0 -and
        $publication.autoMerge.enabledAt -ceq '2026-10-27T05:26:51.0000000+00:00' -and
        $publication.head -ceq $handoff.receipt.head -and $publication.receiptPath -ceq $handoff.path -and
        (Get-CoordinatorDigest $proof) -ceq $corrected.events[0].digest -and
        (Get-CoordinatorDigest $proof.receipt) -ceq $correction.receiptDigest -and
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($handoff.path)) -ceq $originalBytes) 'PR549 eleven-hour transcription correction binds original immutable receipt, authenticated-readback provenance and once-only release'
    Entry 'Consume' @{ receiptPath = $handoff.path } -Failure 'reused with different content' | Out-Null
    $readback.autoMergeRequest.enabledAt = '2026-10-27T05:26:52Z'
    Write-ProofJson $readback $readbackPath
    Entry 'Consume' $correctionRequest -Failure 'readback digest mismatch' | Out-Null
    $correction.readbackDigest = Get-CoordinatorDigest $readback
    Entry 'Consume' $correctionRequest -Failure 'reused with different content' | Out-Null
    Check ((Entry 'Read').state.events.Count -eq 1) 'Changed correction/readback or omitted attestation cannot silently reuse the consumed event'
    $readback.autoMergeRequest.enabledAt = '2026-10-27T05:26:51Z'
    Write-ProofJson $readback $readbackPath
    $correction.readbackDigest = Get-CoordinatorDigest $readback
    $savedProof = [IO.File]::ReadAllBytes($proofPath)
    [IO.File]::Delete($proofPath)
    Entry 'Consume' $correctionRequest -Failure 'Missing immutable transition evidence' | Out-Null
    [IO.File]::WriteAllBytes($proofPath, $savedProof)
    $proof.publicationCorrection.verificationEvidence = 'Changed retained proof'
    Write-ProofJson $proof $proofPath
    Entry 'Consume' $correctionRequest -Failure 'Immutable transition evidence changed' | Out-Null
    [IO.File]::WriteAllBytes($proofPath, $savedProof)
    Check ((Entry 'Read').state.events.Count -eq 1) 'Missing or altered immutable correction evidence fails closed on replay'
    Certify-Owner $id -Repair $true
    $repairDispatch = Entry 'Next'
    $correctedRebind = @{ executionId = $repairDispatch.action.executionId; outcome = 'accepted'; dispatcherJoined = $true
        evidence = 'Exact offline serialized native creation readback after corrected publication'
        nativeIdentity = 'native-fixture:corrected-publication-repair'; branch = 'fixture/corrected-repair'
        worktree = (Join-Path $OutputDirectory 'corrected-publication-repair')
        serializedRebind = @{ authority = 'Explicit same-owner repair after corrected publication'
            previousExecutionId = $dispatch.action.executionId; previousReceiptDigest = $correction.receiptDigest
            previousBinding = @{ nativeIdentity = $handoff.receipt.nativeIdentity; branch = $handoff.receipt.branch; worktree = $handoff.receipt.worktree } } }
    Check ((Entry 'Reconcile' $correctedRebind).kind -ceq 'dispatch-reconciled') 'Serialized rebind preserves and validates the previous composite publication-correction proof'

    $null = New-Fixture 'scope-narrowing'
    $id = 'response-mode-proposal-delivery'
    $oldScope = 'tests/Kora.Application.UnitTests/Configuration'
    $newScopes = @("$oldScope/RegisteredSettingsTests.cs", "$oldScope/ResponseModeConfigurationServiceTests.Suggestions.cs",
        "$oldScope/ResponseModeConfigurationServiceTests.cs")
    Register-Owner $id -Scopes @('src/Kora.Application/Configuration/ResponseModeConfigurationService.cs', $oldScope, 'docs/setting-proposals.md')
    Certify-Owner $id
    $dispatch = Entry 'Next'
    $manifestHead = 'e225439253eef6e6107002d67995a320a5017db6'
    $narrow = @{ ownerId = $id; oldScope = $oldScope; newScopes = $newScopes
        manifest = @{ head = $manifestHead; paths = @($newScopes + 'docs/setting-proposals.md')
            reference = 'Offline exact committed source manifest fixture at e225439; caller must verify actual complete source list' } }
    Entry 'NarrowScopes' $narrow -Failure 'canonically released' | Out-Null
    Accept-Dispatch $dispatch $id
    Entry 'NarrowScopes' $narrow -Failure 'canonically released' | Out-Null
    $handoff = New-Handoff $id $dispatch.action.executionId 'scope-release'
    $handoff.receipt.head = $manifestHead
    $handoff.receipt.kind = 'blocked-quiescent'
    $handoff.receipt.prUrl = $null
    $handoff.receipt.autoMerge = $null
    Write-ProofJson $handoff.receipt $handoff.path
    Entry 'Consume' @{ receiptPath = $handoff.path } | Out-Null
    Register-Owner 'pr-542-repair' -Scope "$oldScope/McpCommandTests.cs"
    Certify-Owner 'pr-542-repair'
    $conflict = Entry 'Next'
    Check ($conflict.kind -ceq 'blocked' -and $conflict.action.constraints[0].reason -ceq 'source-conflict') 'Actual PR542 sibling file is conservatively blocked before explicit released-owner narrowing'
    $before = (Entry 'Read').state
    $badNarrowings = @(
        @{ key = 'oldScope'; value = 'docs'; failure = 'exactly one existing' },
        @{ key = 'newScopes'; value = @('tests/Kora.Application.UnitTests'); failure = 'exact manifested descendants' },
        @{ key = 'newScopes'; value = @("$oldScope/Absent.cs"); failure = 'exact manifested descendants' },
        @{ key = 'newScopes'; value = @($newScopes[0]); failure = 'omitted a manifested' },
        @{ key = 'newScopes'; value = @($newScopes + $newScopes[0]); failure = 'Duplicate narrowed' },
        @{ key = 'scopes'; value = $newScopes; failure = 'Unknown field' },
        @{ key = 'manifest'; value = @{ head = 'd' * 40; paths = $newScopes; reference = 'Wrong head' }; failure = 'canonically released' },
        @{ key = 'manifest'; value = @{ head = $manifestHead; paths = $newScopes; reference = '' }; failure = 'Invalid coordinator text' }
    )
    foreach ($case in $badNarrowings) {
        $copy = $narrow | ConvertTo-Json -Depth 10 | ConvertFrom-Json -AsHashtable -DateKind String
        $copy[$case.key] = $case.value
        Entry 'NarrowScopes' $copy -Failure $case.failure | Out-Null
    }
    Entry 'NarrowScopes' $narrow -Failure 'Stale epoch/revision' -Snapshot @{ epoch = $before.epoch; revision = $before.revision - 1 } | Out-Null
    Entry 'NarrowScopes' $narrow -Failure 'Current unexpired leader' -Leader 'other-leader' | Out-Null
    Check ((Get-CoordinatorDigest (Entry 'Read').state) -ceq (Get-CoordinatorDigest $before)) 'Rejected broadening, missing manifest, other-root removal, stale fencing and foreign caller preserve canonical state'
    $narrowed = Entry 'NarrowScopes' $narrow
    $duplicate = Entry 'NarrowScopes' $narrow
    $after = (Entry 'Read').state
    $beforeOwner = Get-CoordinatorOwner $before $id
    $afterOwner = Get-CoordinatorOwner $after $id
    $beforeOwner.scopes = $afterOwner.scopes
    Check ($narrowed.kind -ceq 'scopes-narrowed' -and $duplicate.kind -ceq 'duplicate' -and $duplicate.charged -eq 0 -and
        (Get-CoordinatorDigest $beforeOwner) -ceq (Get-CoordinatorDigest $afterOwner) -and
        (Get-CoordinatorDigest $before.executions) -ceq (Get-CoordinatorDigest $after.executions) -and
        $afterOwner.sourceRetained -and $afterOwner.scopes.Count -eq 5 -and
        $after.authorityId -ceq $before.authorityId -and $after.publisher -ceq $before.publisher -and
        $after.publicationPolicy -ceq $before.publicationPolicy) 'Atomic same-owner narrowing changes only one scope and evidence, not owner/PR/authority, source retention, executions or capacity'
    Certify-Owner 'pr-542-repair'
    Check ((Entry 'Next').action.ownerId -ceq 'pr-542-repair') 'Manifested response-mode test files no longer falsely conflict with PR542 McpCommandTests.cs'
    Register-Owner 'uncertain-narrowing' -Allocation uncertain -Scope $oldScope
    $narrow.ownerId = 'uncertain-narrowing'
    Entry 'NarrowScopes' $narrow -Failure 'canonically released' | Out-Null
    Check ((Entry 'Read').state.executions[-1].phase -ceq 'uncertain') 'Unknown owner remains charged and cannot narrow its retained scope'

    $null = New-Fixture 'serialized-rebind'
    $id = 'coordinator-current-run-contract-repair'
    Register-Owner $id -Kind repair -Scope 'eng/Coordinator.Common.ps1'
    Certify-Owner $id -Repair $true
    $originalDispatch = Entry 'Next'
    Accept-Dispatch $originalDispatch $id
    $oldHandoff = New-Handoff $id $originalDispatch.action.executionId 'original-publication'
    $oldHandoff.receipt.prUrl = 'https://github.com/roryprimrose/Kora/pull/550'
    Write-ProofJson $oldHandoff.receipt $oldHandoff.path
    Entry 'Consume' @{ receiptPath = $oldHandoff.path } | Out-Null
    $oldReceiptDigest = Get-CoordinatorDigest (Read-CoordinatorJson $oldHandoff.path)
    $previousBinding = @{ nativeIdentity = $oldHandoff.receipt.nativeIdentity; branch = $oldHandoff.receipt.branch
        worktree = $oldHandoff.receipt.worktree }
    Certify-Owner $id -Repair $true
    $newDispatch = Entry 'Next'
    $rebind = @{ executionId = $newDispatch.action.executionId; outcome = 'accepted'; dispatcherJoined = $true
        evidence = 'Original fixture dispatcher made exactly one native creation and joined its actual new isolated worktree readback'
        nativeIdentity = 'native-fixture:serialized-repair2'; branch = 'fixture/serialized-repair2'
        worktree = (Join-Path $OutputDirectory 'serialized-repair2')
        serializedRebind = @{ authority = 'Explicit scoped fixture authority for a new serialized same-owner PR550 repair'
            previousExecutionId = $originalDispatch.action.executionId; previousReceiptDigest = $oldReceiptDigest
            previousBinding = $previousBinding } }
    $withoutRebind = $rebind.Clone()
    $withoutRebind.Remove('serializedRebind')
    Entry 'Reconcile' $withoutRebind -Failure 'Dispatch identity changed' | Out-Null
    $before = (Entry 'Read').state
    $badRebindings = @(
        @{ key = 'authority'; value = ''; failure = 'Invalid coordinator text' },
        @{ key = 'previousExecutionId'; value = $newDispatch.action.executionId; failure = 'all previous executions' },
        @{ key = 'previousReceiptDigest'; value = 'A' * 64; failure = 'exact previous stopped/joined' },
        @{ key = 'previousBinding'; value = @{ nativeIdentity = 'native:wrong'; branch = $previousBinding.branch
                worktree = $previousBinding.worktree }; failure = 'previous binding mismatch' }
    )
    foreach ($case in $badRebindings) {
        $copy = $rebind | ConvertTo-Json -Depth 10 | ConvertFrom-Json -AsHashtable -DateKind String
        $copy.serializedRebind[$case.key] = $case.value
        Entry 'Reconcile' $copy -Failure $case.failure | Out-Null
    }
    $copy = $rebind.Clone()
    $copy.dispatcherJoined = $false
    Entry 'Reconcile' $copy -Failure 'original dispatcher and joined accepted' | Out-Null
    $copy = $rebind.Clone()
    $copy.outcome = 'uncertain'
    Entry 'Reconcile' $copy -Failure 'original dispatcher and joined accepted' | Out-Null
    $copy = $rebind.Clone()
    $copy.nativeIdentity = $previousBinding.nativeIdentity
    Entry 'Reconcile' $copy -Failure 'genuinely new native identity' | Out-Null
    Entry 'Reconcile' $rebind -Failure 'Stale epoch/revision' -Snapshot @{ epoch = $before.epoch; revision = $before.revision - 1 } | Out-Null
    Entry 'Reconcile' $rebind -Failure 'Current unexpired leader' -Leader 'unsolicited-dispatcher' | Out-Null
    Check ((Get-CoordinatorDigest (Entry 'Read').state) -ceq (Get-CoordinatorDigest $before)) 'Unsolicited rollover, wrong old binding/digest, absent authority, unjoined creator and stale fence preserve the charged new intent'
    $registryPath = Join-Path $before.identity.directory 'registry.json'
    $redoPath = Join-Path $before.identity.directory 'pending-transition.json'
    $savedRegistry = [IO.File]::ReadAllBytes($registryPath)
    $savedRedo = [IO.File]::ReadAllBytes($redoPath)
    foreach ($case in @('active-prior', 'uncertain-prior', 'not-original-dispatcher', 'wrong-dispatch-epoch')) {
        $state = $before | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable -DateKind String
        switch ($case) {
            'active-prior' { $state.executions[0].phase = 'accepted' }
            'uncertain-prior' { $state.executions[0].phase = 'uncertain' }
            'not-original-dispatcher' { $state.executions[1].dispatcher = 'previous-leader' }
            'wrong-dispatch-epoch' { $state.executions[1].epoch = 0 }
        }
        Write-ProofJson $state $registryPath
        [IO.File]::Delete($redoPath)
        $failure = if ($case -in @('active-prior', 'uncertain-prior')) { 'Duplicate active allocation' } else { 'original dispatcher and joined accepted' }
        Entry 'Reconcile' $rebind -Failure $failure | Out-Null
        [IO.File]::WriteAllBytes($registryPath, $savedRegistry)
        [IO.File]::WriteAllBytes($redoPath, $savedRedo)
        Check ((Get-CoordinatorDigest (Entry 'Read').state) -ceq (Get-CoordinatorDigest $before)) "Serialized rebind rejects $case without erasing any execution evidence"
    }
    $accepted = Entry 'Reconcile' $rebind
    $duplicate = Entry 'Reconcile' $rebind
    $after = (Entry 'Read').state
    $oldOwner = Get-CoordinatorOwner $before $id
    $newOwner = Get-CoordinatorOwner $after $id
    foreach ($key in @('nativeIdentity', 'branch', 'worktree')) { $oldOwner[$key] = $rebind[$key] }
    $rebindProofPath = Join-Path (Join-Path $after.identity.directory 'transition-evidence') ($after.events[-1].id + '.json')
    $rebindProof = Read-CoordinatorJson $rebindProofPath -MaximumBytes 1048576
    Check ($accepted.kind -ceq 'dispatch-reconciled' -and $duplicate.kind -ceq 'duplicate' -and $duplicate.charged -eq 1 -and
        $after.executions.Count -eq 2 -and $after.executions[0].phase -ceq 'released' -and $after.executions[1].phase -ceq 'accepted' -and
        (Get-CoordinatorDigest $oldOwner) -ceq (Get-CoordinatorDigest $newOwner) -and
        (Get-CoordinatorDigest $before.executions[0]) -ceq (Get-CoordinatorDigest $after.executions[0]) -and
        (Get-CoordinatorDigest $rebindProof.previousReceipt) -ceq $oldReceiptDigest -and
        (Get-CoordinatorDigest (Read-CoordinatorJson $oldHandoff.path)) -ceq $oldReceiptDigest -and
        $before.authorityId -ceq $after.authorityId -and $before.publisher -ceq $after.publisher -and
        $before.publicationPolicy -ceq $after.publicationPolicy) 'Released PR550 owner accepts one genuinely new serialized native/worktree binding, preserving old evidence, PR, source, authority and allocation charge'
    $rebind.nativeIdentity = 'native-fixture:impersonated-later'
    Entry 'Reconcile' $rebind -Failure 'Serialized rebind evidence changed' | Out-Null
    $withoutRebind.nativeIdentity = 'native-fixture:impersonated-later'
    Entry 'Reconcile' $withoutRebind -Failure 'Dispatch identity changed' | Out-Null
    Check ((Entry 'Read').state.executions[1].phase -ceq 'accepted') 'Already accepted execution cannot roll its identity with or without a serialized rebind request'
    $secondHandoff = New-Handoff $id $newDispatch.action.executionId 'repair2-publication' 2
    $secondHandoff.receipt.prUrl = $oldHandoff.receipt.prUrl
    Write-ProofJson $secondHandoff.receipt $secondHandoff.path
    Check ((Entry 'Consume' @{ receiptPath = $secondHandoff.path }).charged -eq 0) 'Revision2 terminal receipt consumes only the new launch-mapped serialized execution'

    foreach ($policy in @('ready-auto', 'pr-only', 'local-only')) {
        $null = New-Fixture "evidence-$policy" -Policy $policy
        Register-Owner 'assessment' -Kind evidence -Scope 'src/shared' -Operation shared-outcome
        Certify-Owner 'assessment'
        $assessment = Entry 'Next'
        Accept-Dispatch $assessment 'assessment'
        $evidence = New-Handoff 'assessment' $assessment.action.executionId
        $evidence.receipt.kind = 'evidence-completed'
        $evidence.receipt.prUrl = $null
        $evidence.receipt.autoMerge = $null
        $invalidEvidence = @(
            @{ name = 'unjoined'; key = 'finiteCommandsJoined'; value = $false; failure = 'not proved quiescent' },
            @{ name = 'running'; key = 'runningCommands'; value = @('command-in-flight'); failure = 'not proved quiescent' },
            @{ name = 'working'; key = 'workState'; value = 'running'; failure = 'not proved quiescent' },
            @{ name = 'decision'; key = 'heldDecision'; value = 'pending'; failure = 'not proved quiescent' },
            @{ name = 'pr'; key = 'prUrl'; value = 'https://github.com/roryprimrose/Kora/pull/1'; failure = 'Unsupported terminal' },
            @{ name = 'auto'; key = 'autoMerge'; value = @{ method = 'SQUASH'; enabledAt = [DateTimeOffset]::UtcNow.ToString('o') }; failure = 'Unsupported terminal' },
            @{ name = 'native'; key = 'nativeIdentity'; value = 'native:other'; failure = 'identity/authority mismatch' },
            @{ name = 'execution'; key = 'executionId'; value = 'unregistered'; failure = 'admitted execution' },
            @{ name = 'policy'; key = 'publicationPolicy'; value = 'other'; failure = 'identity/authority mismatch' }
        )
        foreach ($case in $invalidEvidence) {
            $copy = $evidence.receipt | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable -DateKind String
            $copy[$case.key] = $case.value
            $path = Join-Path (Split-Path -Parent $evidence.path) ($case.name + '.json')
            Write-ProofJson $copy $path
            Entry 'Consume' @{ receiptPath = $path } -Failure $case.failure | Out-Null
            $unchanged = (Entry 'Read').state
            Check ($unchanged.executions[0].phase -ceq 'accepted' -and $unchanged.owners[0].sourceRetained -and
                $unchanged.events.Count -eq 0) "Rejected $policy evidence $($case.name) preserves execution and source ownership"
        }
        Write-ProofJson $evidence.receipt $evidence.path
        $released = Entry 'Consume' @{ receiptPath = $evidence.path }
        $duplicate = Entry 'Consume' @{ receiptPath = $evidence.path }
        $evidenceState = (Entry 'Read').state
        $evidenceOwner = $evidenceState.owners[0]
        Check ($released.kind -ceq 'released' -and $released.action.sourceOwnership -ceq 'released' -and
            $duplicate.kind -ceq 'duplicate' -and $duplicate.charged -eq 0 -and $evidenceState.events.Count -eq 1 -and
            $evidenceOwner.delivery -ceq 'evidence-completed' -and !$evidenceOwner.sourceRetained -and
            $null -eq $evidenceOwner.sourceRelease -and $null -eq $evidenceOwner.publication.prUrl -and
            $null -eq $evidenceOwner.publication.autoMerge) "$policy evidence completion releases once with retained receipt provenance, not fabricated merge evidence"
        $evidence.receipt.eventId = 'second-release'
        $evidence.receipt.revision = 2
        Write-ProofJson $evidence.receipt $evidence.path
        Entry 'Consume' @{ receiptPath = $evidence.path } -Failure 'already released' | Out-Null
        Entry 'Certify' @{ ownerId = 'assessment'; repair = $false; certification = $null } -Failure 'Cannot certify' | Out-Null
        Register-Owner 'product' -Scope 'src/shared/consumer' -Operation shared-outcome
        Certify-Owner 'product'
        Check ((Entry 'Next').action.ownerId -ceq 'product') "$policy completed evidence leaves neither a path nor semantic product-source collision"
    }

    foreach ($kind in @('feature', 'repair', 'historical')) {
        $null = New-Fixture "non-evidence-$kind"
        Register-Owner 'product' -Kind $kind -Allocation uncertain
        $execution = (Entry 'Read').state.executions[0]
        Accept-Dispatch @{ action = @{ executionId = $execution.id } } 'product'
        $product = New-Handoff 'product' $execution.id
        $product.receipt.prUrl = $null
        $product.receipt.autoMerge = $null
        foreach ($terminalKind in @('completed', 'evidence-completed')) {
            $product.receipt.kind = $terminalKind
            Write-ProofJson $product.receipt $product.path
            Entry 'Consume' @{ receiptPath = $product.path } -Failure 'Unsupported terminal' | Out-Null
        }
        $unchanged = (Entry 'Read').state
        Check ($unchanged.executions[0].phase -ceq 'accepted' -and $unchanged.owners[0].sourceRetained -and
            $unchanged.owners[0].delivery -ceq 'none') "$kind cannot use local or evidence completion to bypass ready-auto publication"
    }

    $script:fixture = $primary
    $null = Entry 'Read'
    $null = Entry 'Claim'
    Register-Owner 'assessor' -Kind evidence
    Entry 'Recover' @{ ownerId = 'assessor'; reason = 'Five-minute assessment exhausted with one partial candidate' } | Out-Null
    Register-Owner 'first'
    Certify-Owner 'first'
    Register-Owner 'second'
    Certify-Owner 'second'
    $first = Entry 'Next'
    Check ($first.kind -ceq 'dispatch' -and $first.action.ownerId -ceq 'first') 'Partial independently certified candidate dispatches before assessor recovery'
    Accept-Dispatch $first 'first'
    $next = Entry 'Next'
    Check ($next.kind -ceq 'dispatch' -and $next.action.ownerId -ceq 'second' -and $next.charged -eq 2) 'Independent ready candidate reserves vacancy without waiting for first completion'
    Accept-Dispatch $next 'second'
    $handoff = New-Handoff 'first' $first.action.executionId
    $release = Entry 'Consume' @{ receiptPath = $handoff.path }
    $duplicate = Entry 'Consume' @{ receiptPath = $handoff.path }
    Check ($release.kind -ceq 'released' -and $duplicate.kind -ceq 'duplicate' -and $duplicate.charged -eq 1) 'Exact stopped publication releases execution once across processes and replay'
    $state = (Entry 'Read').state
    $owner = @($state.owners | Where-Object id -CEQ first)[0]
    Check ($owner.sourceRetained -and $owner.delivery -ceq 'pr-ci-wait' -and
        $owner.publication.prUrl -ceq $handoff.receipt.prUrl) 'CI delivery and exact publication persist separately from execution and source ownership'
    $older = New-Handoff 'first' $first.action.executionId 'old-handoff' 0
    Entry 'Consume' @{ receiptPath = $older.path } -Failure 'Superseded receipt' | Out-Null
    $newer = New-Handoff 'first' $first.action.executionId 'new-handoff' 2
    Entry 'Consume' @{ receiptPath = $newer.path } -Failure 'already released' | Out-Null
    $recovery = Entry 'Next'
    $blocked = Entry 'Next'
    Check ($recovery.kind -ceq 'recover-scoped' -and $recovery.action.ownerIds.Count -eq 1 -and
        $blocked.kind -ceq 'blocked') 'No-result assessment returns one scoped recovery then explicit blocker, not repeated discovery'

    Certify-Owner 'first' -Repair $true
    Register-Owner 'third'
    Certify-Owner 'third'
    $repair = Entry 'Next'
    Check ($repair.kind -ceq 'dispatch' -and $repair.action.ownerId -ceq 'first') 'Concrete admitted repair selects same retained owner before new feature'

    $null = New-Fixture 'legacy'
    foreach ($id in 1..6) { Register-Owner "historical-$id" -Kind historical }
    Register-Owner 'unknown' -Allocation uncertain
    Register-Owner 'safe'
    Certify-Owner 'safe'
    $safe = Entry 'Next'
    Check ($safe.kind -ceq 'dispatch' -and $safe.charged -eq 2) 'Historical source labels charge zero while genuinely unknown admitted execution remains charged'
    $targeted = Entry 'Next'
    $once = Entry 'Next'
    Check ($targeted.kind -ceq 'reconcile-dispatch' -and $targeted.action.executionIds.Count -eq 2 -and
        $once.kind -ceq 'blocked' -and $once.charged -eq 2) 'Unknown identities return bounded exact-intent recovery without fleet archaeology or invented release'
    $state = (Entry 'Read').state
    $unknownId = @($state.executions | Where-Object ownerId -CEQ unknown)[0].id
    Entry 'Reconcile' @{ executionId = $unknownId; outcome = 'not-dispatched'; evidence = 'missing chat'
        dispatcherJoined = $false; nativeIdentity = $null; branch = $null; worktree = $null } -Failure 'No proof' | Out-Null

    $null = New-Fixture 'crash'
    Register-Owner 'crash-owner'
    Certify-Owner 'crash-owner'
    $before = (Entry 'Read').state
    $registryPath = Join-Path $before.identity.directory 'registry.json'
    $beforeBytes = [IO.File]::ReadAllBytes($registryPath)
    $intent = Entry 'Next'
    [IO.File]::WriteAllBytes($registryPath, $beforeBytes)
    $recovered = (Entry 'Read').state
    Check ($recovered.executions.Count -eq 1 -and $recovered.executions[0].id -ceq $intent.action.executionId) 'Fresh process replays write-ahead intent after registry-replacement crash boundary'
    $notReplayed = Entry 'Next'
    Check ($notReplayed.kind -ceq 'reconcile-dispatch') 'Intent-before-native-dispatch crash never emits a second launch'
    Accept-Dispatch $intent 'crash-owner'
    $readbackLost = Entry 'Next'
    Check ($readbackLost.kind -ceq 'blocked' -and $readbackLost.charged -eq 1) 'Accepted dispatch survives caller/readback restart without duplicate launch'

    $null = New-Fixture 'races'
    foreach ($id in 1..9) { Register-Owner "uncertain-$id" -Allocation uncertain }
    foreach ($id in @('race-a', 'race-b')) { Register-Owner $id; Certify-Owner $id }
    $state = (Entry 'Read').state
    $left = Start-Entry 'Next' @{} 'native-leader' $state
    $right = Start-Entry 'Next' @{} 'native-leader' $state
    $raceOutputs = @()
    foreach ($run in @($left, $right)) {
        $run.process.WaitForExit()
        if ($run.process.ExitCode -eq 0) { $raceOutputs += ,(Complete-Entry $run) }
        else { Complete-Entry $run -Failure 'Stale epoch/revision' | Out-Null }
    }
    Check (@($raceOutputs | Where-Object kind -CEQ dispatch).Count -eq 1 -and
        @((Entry 'Read').state.executions | Where-Object phase -CNE released).Count -eq 10) 'Concurrent entry points reserve the last slot at most once under exclusive lock/revision fencing'
    Entry 'Next' @{} -Failure 'Stale epoch/revision' -Snapshot $state | Out-Null
    Entry 'Claim' @{} -Failure 'unexpired lease' -Leader 'competing-native-leader' | Out-Null
    $fresh = (Entry 'Read').state
    $expired = $fresh | ConvertTo-Json -Depth 40 | ConvertFrom-Json -AsHashtable -DateKind String
    $expired.leaseUntil = [DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('o')
    Write-ProofJson $expired (Join-Path $fresh.identity.directory 'registry.json')
    [IO.File]::Delete((Join-Path $fresh.identity.directory 'pending-transition.json'))
    $claim = Entry 'Claim' @{} -Leader 'successor-native-leader'
    Check ($claim.epoch -eq 2 -and $claim.charged -eq 10) 'Expired leadership takeover fences previous epoch without releasing unknown execution'
    Entry 'Next' @{} -Failure 'Stale epoch/revision' -Snapshot $fresh | Out-Null
    $lockedPath = Join-Path $fresh.identity.directory 'writer.lock'
    $held = [IO.FileStream]::new($lockedPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try { Check ((Entry 'Read').reason -ceq 'writer-lock-unavailable') 'Contended writer lock returns bounded blocker without retry or lock deletion' }
    finally { $held.Dispose() }

    $null = New-Fixture 'receipts'
    Register-Owner 'receipt-owner'
    Certify-Owner 'receipt-owner'
    $dispatch = Entry 'Next'
    Accept-Dispatch $dispatch 'receipt-owner'
    $original = New-Handoff 'receipt-owner' $dispatch.action.executionId
    $invalidCases = @(
        @{ name = 'string-version'; key = 'version'; value = '1'; failure = 'nonnegative integer' },
        @{ name = 'unjoined'; key = 'finiteCommandsJoined'; value = $false; failure = 'not proved quiescent' },
        @{ name = 'running'; key = 'runningCommands'; value = @('command-in-flight'); failure = 'not proved quiescent' },
        @{ name = 'foreign-dataset'; key = 'datasetId'; value = [guid]::NewGuid().ToString(); failure = 'identity/authority mismatch' },
        @{ name = 'foreign-owner'; key = 'ownerId'; value = 'unregistered'; failure = 'Cross-owner' },
        @{ name = 'foreign-native'; key = 'nativeIdentity'; value = 'native:other'; failure = 'identity/authority mismatch' },
        @{ name = 'foreign-execution'; key = 'executionId'; value = 'unregistered'; failure = 'admitted execution' },
        @{ name = 'broaden-authority'; key = 'publicationPolicy'; value = 'local-only'; failure = 'identity/authority mismatch' },
        @{ name = 'commands'; key = 'command'; value = 'must never execute'; failure = 'Unknown field' }
    )
    foreach ($case in $invalidCases) {
        $copy = $original.receipt | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable -DateKind String
        $copy[$case.key] = $case.value
        $path = Join-Path (Split-Path -Parent $original.path) ($case.name + '.json')
        Write-ProofJson $copy $path
        Entry 'Consume' @{ receiptPath = $path } -Failure $case.failure | Out-Null
        Check (@((Entry 'Read').state.executions | Where-Object phase -CNE released).Count -eq 1) "Rejected $($case.name) receipt preserves charged allocation"
    }
    $outside = Join-Path $OutputDirectory 'outside.json'
    Write-ProofJson $original.receipt $outside
    Entry 'Consume' @{ receiptPath = $outside } -Failure 'Unregistered owner' | Out-Null
    $malformed = Join-Path (Split-Path -Parent $original.path) 'malformed.json'
    '{bad' | Set-Content -LiteralPath $malformed
    Entry 'Consume' @{ receiptPath = $malformed } -Failure 'Conversion from JSON failed' | Out-Null
    $oversized = Join-Path (Split-Path -Parent $original.path) 'oversized.json'
    ('x' * 32769) | Set-Content -LiteralPath $oversized
    Entry 'Consume' @{ receiptPath = $oversized } -Failure 'Oversized coordinator JSON' | Out-Null
    $linked = Join-Path (Split-Path -Parent $original.path) 'linked'
    $linkType = if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }
    New-Item -ItemType $linkType -Path $linked -Target $OutputDirectory | Out-Null
    Entry 'Consume' @{ receiptPath = (Join-Path $linked 'outside.json') } -Failure 'Unregistered owner' | Out-Null
    Check $true 'Out-of-root, malformed, oversized and unregistered linked receipts rejected before release'
    Entry 'ReleaseSource' @{ ownerId = 'receipt-owner'; mergeSha = 'd' * 40; freshMainSha = 'e' * 40
        reachabilityEvidence = 'offline exact reachability fixture' } -Failure 'joined execution' | Out-Null
    Check ((Entry 'Consume' @{ receiptPath = $original.path }).kind -ceq 'released') 'Valid registered handoff remains consumable after hostile receipts'
    $original.receipt.head = 'f' * 40
    Write-ProofJson $original.receipt $original.path
    Entry 'Consume' @{ receiptPath = $original.path } -Failure 'reused with different content' | Out-Null
    Entry 'ReleaseSource' @{ ownerId = 'receipt-owner'; mergeSha = 'd' * 40; freshMainSha = 'e' * 40
        reachabilityEvidence = 'offline exact reachability fixture' } | Out-Null
    $releasedOwner = @((Entry 'Read').state.owners | Where-Object id -CEQ receipt-owner)[0]
    Check (!$releasedOwner.sourceRetained -and $releasedOwner.sourceRelease.mergeSha -ceq ('d' * 40)) 'Separate authorized source release persists exact reachability provenance, never automatic on CI handoff'
    $redoPath = Join-Path (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $original.path))) 'pending-transition.json'
    '{bad' | Set-Content -LiteralPath $redoPath
    Entry 'Read' @{} -Failure 'Conversion from JSON failed' | Out-Null
    Check $true 'Corrupt write-ahead recovery fails closed rather than resetting state'

    $null = New-Fixture 'conflict'
    Register-Owner 'retained' -Kind historical -Scope 'src/shared'
    Register-Owner 'overlap' -Scope 'src/shared/consumer'
    Certify-Owner 'overlap'
    Register-Owner 'duplicate' -Scope 'src/elsewhere' -Operation retained
    Certify-Owner 'duplicate'
    $conflict = Entry 'Next'
    Check ($conflict.kind -ceq 'blocked' -and $conflict.action.constraints.Count -eq 2) 'Source scope and semantic operation conflicts hold new work even on disjoint paths'
    $null = New-Fixture 'stale-observation'
    Register-Owner 'stale'
    Certify-Owner 'stale' -Observed ([DateTimeOffset]::UtcNow.AddMinutes(-2).ToString('o'))
    $stale = Entry 'Next'
    Check ($stale.kind -ceq 'blocked' -and $stale.action.constraints[0].reason -ceq 'refresh-pr-observation') 'Expired PR evidence requires targeted refresh without full-fleet recovery'
    Certify-Owner 'stale'
    Entry 'Recover' @{ ownerId = 'stale'; reason = 'Concrete changed consumed source or PR overlap invalidates this candidate only' } | Out-Null
    Check ((Entry 'Next').kind -ceq 'recover-scoped') 'Known changed inputs invalidate the affected certification instead of dispatching stale work'
    $state = (Entry 'Read').state
    $registryPath = Join-Path $state.identity.directory 'registry.json'
    $savedRegistry = [IO.File]::ReadAllBytes($registryPath)
    $state.version = '1'
    Write-ProofJson $state $registryPath
    Entry 'Read' @{} -Failure 'nonnegative integer' | Out-Null
    [IO.File]::WriteAllBytes($registryPath, $savedRegistry)
    Check $true 'Malformed persisted version is rejected rather than reinterpreted'

    $null = New-Fixture 'local-policy' -Policy local-only
    Register-Owner 'local'
    Certify-Owner 'local'
    $localDispatch = Entry 'Next'
    Accept-Dispatch $localDispatch 'local'
    $local = New-Handoff 'local' $localDispatch.action.executionId
    Entry 'Consume' @{ receiptPath = $local.path } -Failure 'not authorized' | Out-Null
    $local.receipt.kind = 'completed'
    $local.receipt.prUrl = $null
    $local.receipt.autoMerge = $null
    Write-ProofJson $local.receipt $local.path
    Check ((Entry 'Consume' @{ receiptPath = $local.path }).kind -ceq 'released') 'Persisted local-only authority cannot be promoted by a receipt'

    $adoption = New-LegacyFixture 'legacy-adoption'
    $identity = Get-CoordinatorLocation $script:fixture
    Entry 'Read' -Failure 'Known legacy locations' | Out-Null
    Entry 'Initialize' @{ legacyAbsenceConfirmed = $true; authorityId = 'cannot-hide-legacy'
        publicationPolicy = 'ready-auto'; publisher = 'fixture-publisher' } -Failure 'Known legacy state exists' | Out-Null
    Check (!(Test-Path -LiteralPath $identity.directory)) 'Known hidden legacy and companion ledgers prevent a competing initialized dataset'
    $missingProof = $adoption | ConvertTo-Json -Depth 40 | ConvertFrom-Json -AsHashtable -DateKind String
    $missingProof.quiescenceEvidence = @()
    Entry 'Adopt' $missingProof -Failure 'every legacy writer' | Out-Null
    $missingProof.quiescenceEvidence = $adoption.quiescenceEvidence
    $missingProof.quiescenceEvidence[0].kind = 'merged'
    Entry 'Adopt' $missingProof -Failure 'not termination' | Out-Null
    $adoption.quiescenceEvidence[0].kind = 'joined-command'
    $missingDigest = $adoption | ConvertTo-Json -Depth 40 | ConvertFrom-Json -AsHashtable -DateKind String
    $missingDigest.repairDigest = $null
    Entry 'Adopt' $missingDigest -Failure 'Exact legacy source digest' | Out-Null
    Check (!(Test-Path -LiteralPath $identity.directory)) 'Unproved writers, merge-only evidence and omitted companion ledger publish no canonical state'
    $held = [IO.FileStream]::new((Join-Path $adoption.legacyDirectory 'writer.lock'),
        [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try { Entry 'Adopt' $adoption -Failure 'being used' | Out-Null }
    finally { $held.Dispose() }
    $adopted = Entry 'Adopt' $adoption
    $restarted = (Entry 'Read').state
    $legacySnapshot = Read-CoordinatorJson (Join-Path $identity.directory 'legacy-snapshot.json') -MaximumBytes 1048576
    Check ($adopted.charged -eq 3 -and $restarted.datasetId -ceq $legacySnapshot.sources[0].value.datasetId -and
        $restarted.revision -eq 12 -and $restarted.epoch -eq 4) 'Quiescent adoption preserves dataset and revisions, advances fencing and retains every charge across processes'
    Check ($legacySnapshot.sources[1].value.wakeBudget.spent -eq 6 -and
        $legacySnapshot.sources[1].value.pendingDispatches[0].actionId -ceq 'ambiguous-original-action' -and
        $legacySnapshot.sources[1].value.processedEventIds[0] -ceq 'original-event' -and
        $legacySnapshot.sources[1].value.unknownRetainedField -ceq 'must-not-disappear' -and
        $legacySnapshot.sources[2].value.currentOwners[0].session -ceq 'original-repair-owner') 'Complete frozen snapshots retain wake expenditure, ambiguous mutations, event identities and original repair ownership'
    Check ($restarted.publicationPolicy -ceq 'local-only' -and $null -eq $restarted.publisher -and
        @($restarted.owners | Where-Object { !$_.sourceRetained -or $_.candidate -cne 'none' }).Count -eq 0 -and
        @($restarted.owners | Where-Object branch -CEQ 'original/unfinished').Count -eq 1) 'Adoption does not broaden authority, certify work or release unfinished source ownership'
    Register-Owner 'independent-after-adoption' -Scope 'unrelated'
    Certify-Owner 'independent-after-adoption'
    Check ((Entry 'Next').action.reason -ceq 'legacy-reconciliation-required') 'Unresolved imported executions block dispatch even for independently certified unrelated scopes'
    Entry 'Adopt' $adoption -Failure 'never adopt twice' | Out-Null
    $executionId = $restarted.executions[0].id
    Entry 'Reconcile' @{ executionId = $executionId; outcome = 'not-dispatched'; evidence = 'missing chat'
        dispatcherJoined = $true; nativeIdentity = $null; branch = $null; worktree = $null } -Failure 'RetireLegacy' | Out-Null
    $retirement = @{ executionId = $executionId; retirementAuthority = 'fixture exact grant retirement approval'
        termination = (New-TerminationEvidence $executionId); remoteReconciliation = 'Fixture has no remote mutation' }
    $retirement.termination.kind = 'old-timestamp'
    Entry 'RetireLegacy' $retirement -Failure 'not termination' | Out-Null
    $retirement.termination.kind = 'host-lifecycle'
    $retired = Entry 'RetireLegacy' $retirement
    $replayed = Entry 'RetireLegacy' $retirement
    Check ($retired.charged -eq 2 -and $replayed.kind -ceq 'duplicate' -and $replayed.charged -eq 2) 'Exact authorized termination plus remote reconciliation retires one imported grant once, not on merge or age'
    $retirement.remoteReconciliation = 'changed evidence'
    Entry 'RetireLegacy' $retirement -Failure 'evidence changed' | Out-Null
    $sourcePath = $legacySnapshot.sources[1].path
    $changedSource = $legacySnapshot.sources[1].value
    $changedSource.revision++
    Write-ProofJson $changedSource $sourcePath
    Entry 'Read' -Failure 'Retired legacy source changed' | Out-Null
    Check $true 'Unexpected legacy resumption blocks canonical recovery rather than running two ledgers'

    $invalidLegacy = New-LegacyFixture 'legacy-invalid-charge'
    $invalidRegistryPath = Join-Path $invalidLegacy.legacyDirectory 'registry.json'
    $invalidRegistry = Read-CoordinatorJson $invalidRegistryPath -MaximumBytes 1048576
    $invalidRegistry.externalExecutionCharges[0].allocationCharge = '1'
    Write-ProofJson $invalidRegistry $invalidRegistryPath
    $invalidLegacy.registryDigest = Get-CoordinatorDigest $invalidRegistry
    Entry 'Adopt' $invalidLegacy -Failure 'nonnegative integer' | Out-Null
    Check (!(Test-Path -LiteralPath (Get-CoordinatorLocation $script:fixture).directory)) 'Malformed legacy charge types cannot become a normalized counter or publish partial adoption'

    $overcommitted = New-LegacyFixture 'legacy-overcommitted' -ExternalCharges 10
    $over = Entry 'Adopt' $overcommitted
    Check ($over.charged -eq 12 -and (Entry 'Next').kind -cne 'dispatch') 'Already overcommitted legacy ledgers retain all charges without granting another execution'
    Entry 'Register' @{ ownerId = 'extra-unknown'; operation = 'extra-unknown'; scopes = @('src/extra')
        kind = 'repair'; allocation = 'uncertain'; evidence = 'No spare capacity' } -Failure 'ceiling exceeded' | Out-Null
    Write-ProofJson @{ passed = $passed.ToArray(); count = $passed.Count; independentProcesses = $script:invocations
        elapsedSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3); finiteProcessesJoined = $true
        entryPoint = $entry; evidence = 'Actual finite entry-point processes; offline fixtures, no native launches/GitHub/provider calls.'
        nativeAcceptance = 'Not run: actual fresh related session, model selection, mailbox wakeup and re-entry remain unverified.' } (Join-Path $OutputDirectory 'tests.json')
    Write-Output "PASS: $($passed.Count) coordinator checks; $script:invocations independent entry-point processes."
} catch {
    Write-ProofJson @{ passed = $passed.ToArray(); failure = $_.Exception.Message; independentProcesses = $script:invocations
        elapsedSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3) } (Join-Path $OutputDirectory 'tests.json')
    throw
}
