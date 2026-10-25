#requires -Version 7.5
. (Join-Path $PSScriptRoot 'Distribution.Common.ps1')
. (Join-Path $PSScriptRoot 'Coordinator.Legacy.ps1')

function Assert-CoordinatorFields {
    param($Value, [string[]] $Required, [string[]] $Optional = @())
    if ($Value -isnot [System.Collections.IDictionary]) { throw 'Expected a JSON object.' }
    foreach ($key in $Required) {
        if (!$Value.Contains($key)) { throw "Missing field: $key" }
    }
    foreach ($key in $Value.Keys) {
        if ($key -cnotin ($Required + $Optional)) { throw "Unknown field: $key" }
    }
}

function Assert-CoordinatorText {
    param($Value, [switch] $Identifier)
    if ($Value -isnot [string] -or !$Value -or $Value.Length -gt 2048 -or $Value -match '[\x00-\x1f]') {
        throw 'Invalid coordinator text.'
    }
    if ($Identifier -and $Value -cnotmatch '^[a-z0-9][a-z0-9-]{0,79}$') { throw 'Invalid coordinator identifier.' }
}

function Assert-CoordinatorNumber {
    param($Value)
    if (($Value -isnot [long] -and $Value -isnot [int]) -or $Value -lt 0) { throw 'Expected a nonnegative integer.' }
}

function ConvertTo-CoordinatorTime {
    param($Value)
    Assert-CoordinatorText $Value
    $time = [DateTimeOffset]::ParseExact($Value, 'o', [Globalization.CultureInfo]::InvariantCulture)
    if ($time.Offset -ne [TimeSpan]::Zero) { throw 'Expected a UTC timestamp.' }
    return $time
}

function Read-CoordinatorJson {
    param([string] $Path, [int] $MaximumBytes = 32768)
    Assert-NoLinks $Path -AncestorsOnly
    if ((Get-Item -LiteralPath $Path).Length -gt $MaximumBytes) { throw 'Oversized coordinator JSON.' }
    return [IO.File]::ReadAllText($Path) | ConvertFrom-Json -AsHashtable -DateKind String -ErrorAction Stop
}

function Write-CoordinatorJson {
    param([string] $Path, $Value)
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 40 -Compress))
    if ($bytes.Length -gt 1048576) { throw 'Coordinator state is full; explicit checkpoint maintenance required.' }
    $temporary = Join-Path (Split-Path -Parent $Path) ("pending-" + [guid]::NewGuid().ToString('N') + '.json')
    Assert-NoLinks (Split-Path -Parent $Path) -AncestorsOnly
    if (Test-Path -LiteralPath $Path) { Assert-NoLinks $Path -AncestorsOnly }
    try {
        $stream = [IO.FileStream]::new($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Write($bytes); $stream.Flush($true) } finally { $stream.Dispose() }
        if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temporary, $Path, [NullString]::Value) }
        else { [IO.File]::Move($temporary, $Path) }
        if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($Path)) -cne [Convert]::ToBase64String($bytes)) {
            throw 'Coordinator publication readback mismatch.'
        }
    } finally {
        if (Test-Path -LiteralPath $temporary) { [IO.File]::Delete($temporary) }
    }
}

function Get-CoordinatorDigest {
    param($Value)
    $bytes = [Text.Encoding]::UTF8.GetBytes(((ConvertTo-CoordinatorCanonicalValue $Value) | ConvertTo-Json -Depth 40 -Compress))
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
}

function ConvertTo-CoordinatorCanonicalValue {
    param($Value)
    if ($Value -is [System.Collections.IDictionary]) {
        $result = [ordered]@{}
        foreach ($key in @($Value.Keys | Sort-Object -CaseSensitive)) {
            $result[$key] = ConvertTo-CoordinatorCanonicalValue $Value[$key]
        }
        return $result
    }
    if ($Value -is [array]) {
        $result = @()
        foreach ($item in $Value) { $result += ,(ConvertTo-CoordinatorCanonicalValue $item) }
        return ,$result
    }
    return $Value
}

function Get-CoordinatorLocation {
    param([string] $RepositoryPath)
    $root = (Resolve-Path -LiteralPath $RepositoryPath).Path
    Assert-NoLinks $root -AncestorsOnly
    $origin = (& git -C $root remote get-url origin | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read coordinator repository origin.' }
    Assert-CanonicalSourceOrigin $origin
    $common = (& git -C $root rev-parse --path-format=absolute --git-common-dir | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read common Git identity.' }
    $worktrees = @(& git -C $root worktree list --porcelain)
    if ($LASTEXITCODE -ne 0 -or $worktrees[0] -notmatch '^worktree (.+)$') { throw 'Cannot locate primary workspace.' }
    $workspace = (Resolve-Path -LiteralPath $Matches[1]).Path
    $common = (Resolve-Path -LiteralPath $common).Path
    Assert-NoLinks $workspace -AncestorsOnly
    Assert-NoLinks $common -AncestorsOnly
    if ($workspace.StartsWith('\\')) { throw 'Network coordinator storage is unsupported.' }
    $directory = Join-Path (Join-Path $workspace '.scratch') 'roadmap-coordinator'
    $existing = $directory
    while (!(Test-Path -LiteralPath $existing)) { $existing = Split-Path -Parent $existing }
    Assert-NoLinks $existing -AncestorsOnly
    & git -C $workspace check-ignore --quiet -- $directory
    if ($LASTEXITCODE -ne 0) { throw 'Canonical coordinator .scratch directory must already be ignored.' }
    return @{ repository = $script:KoraRepository; commonGit = $common; workspace = $workspace; directory = $directory }
}

function Assert-CoordinatorScope {
    param($Scopes)
    if ($Scopes -isnot [array] -or $Scopes.Count -lt 1 -or $Scopes.Count -gt 16) { throw 'Invalid source scopes.' }
    foreach ($scope in $Scopes) {
        if ($scope -isnot [string] -or $scope -cnotmatch '^[a-zA-Z0-9_.-]+(/[a-zA-Z0-9_.-]+)*$' -or
            ($scope -split '/') -contains '..' -or ($scope -split '/') -contains '.') { throw 'Invalid source scope path.' }
    }
}

function Test-CoordinatorOverlap {
    param($Left, $Right)
    foreach ($leftScope in $Left) {
        foreach ($rightScope in $Right) {
            if ($leftScope.Equals($rightScope, [StringComparison]::OrdinalIgnoreCase) -or
                $leftScope.StartsWith("$rightScope/", [StringComparison]::OrdinalIgnoreCase) -or
                $rightScope.StartsWith("$leftScope/", [StringComparison]::OrdinalIgnoreCase)) { return $true }
        }
    }
    return $false
}

function Assert-CoordinatorState {
    param($State, $Identity)
    Assert-CoordinatorFields $State @('version', 'datasetId', 'identity', 'revision', 'epoch', 'leader', 'leaseUntil',
        'lastUtc', 'authorityId', 'publicationPolicy', 'publisher', 'owners', 'executions', 'events', 'continuation') @('legacy')
    Assert-CoordinatorNumber $State.version
    if ($State.version -cne 1 -or $State.datasetId -cnotmatch '^[0-9a-f-]{36}$' -or
        (Get-CoordinatorDigest $State.identity) -cne (Get-CoordinatorDigest $Identity)) { throw 'Invalid dataset identity/version.' }
    foreach ($value in @($State.revision, $State.epoch)) { Assert-CoordinatorNumber $value }
    Assert-CoordinatorText $State.leader
    Assert-CoordinatorText $State.authorityId
    $null = ConvertTo-CoordinatorTime $State.leaseUntil
    $null = ConvertTo-CoordinatorTime $State.lastUtc
    if ($State.publicationPolicy -cnotin @('ready-auto', 'pr-only', 'local-only') -or
        $State.continuation -cnotin @('unavailable', 'host-verified')) { throw 'Invalid authority/continuation policy.' }
    if ($State.publicationPolicy -cne 'local-only' -or $null -ne $State.publisher) {
        Assert-CoordinatorText $State.publisher
    }
    foreach ($name in @('owners', 'executions', 'events')) {
        if ($State[$name] -isnot [array] -or $State[$name].Count -gt 1000) { throw 'Invalid or full coordinator index.' }
    }
    $ownerIds = @()
    foreach ($owner in $State.owners) {
        Assert-CoordinatorFields $owner @('id', 'operation', 'scopes', 'kind', 'sourceRetained', 'candidate', 'certification',
            'nativeIdentity', 'branch', 'worktree', 'delivery', 'publication', 'sourceRelease', 'receiptRevision', 'recovery')
        Assert-CoordinatorText $owner.id -Identifier
        Assert-CoordinatorText $owner.operation -Identifier
        Assert-CoordinatorScope $owner.scopes
        Assert-CoordinatorNumber $owner.receiptRevision
        if ($owner.id -cin $ownerIds -or $owner.kind -cnotin @('feature', 'repair', 'evidence', 'historical') -or
            $owner.sourceRetained -isnot [bool] -or $owner.candidate -cnotin @('none', 'ready', 'reserved') -or
            $owner.delivery -cnotin @('none', 'pr-ci-wait', 'local-completed')) { throw 'Invalid owner state.' }
        foreach ($key in @('nativeIdentity', 'branch', 'worktree')) {
            if ($null -ne $owner[$key]) { Assert-CoordinatorText $owner[$key] }
        }
        if ($owner.candidate -ceq 'ready' -or $null -ne $owner.certification) {
            Assert-CoordinatorFields $owner.certification @('sourceHead', 'inputDigest', 'prObservation', 'observedUtc', 'approvedOutcome', 'placement')
            if ($owner.certification.sourceHead -cnotmatch '^[0-9a-f]{40}$' -or
                $owner.certification.inputDigest -cnotmatch '^[0-9A-Fa-f]{64}$') { throw 'Invalid certification identity.' }
            foreach ($key in @('prObservation', 'approvedOutcome', 'placement')) { Assert-CoordinatorText $owner.certification[$key] }
            $null = ConvertTo-CoordinatorTime $owner.certification.observedUtc
        }
        if ($null -ne $owner.recovery) {
            Assert-CoordinatorFields $owner.recovery @('reason', 'issued')
            Assert-CoordinatorText $owner.recovery.reason
            if ($owner.recovery.issued -isnot [bool]) { throw 'Invalid recovery state.' }
        }
        if ($null -ne $owner.publication) {
            Assert-CoordinatorFields $owner.publication @('head', 'prUrl', 'autoMerge', 'receiptPath')
            if ($owner.publication.head -cnotmatch '^[0-9a-f]{40}$') { throw 'Invalid publication head.' }
            Assert-CoordinatorText $owner.publication.receiptPath
        }
        if (!$owner.sourceRetained) {
            Assert-CoordinatorFields $owner.sourceRelease @('mergeSha', 'freshMainSha', 'reachabilityEvidence')
            if ($owner.sourceRelease.mergeSha -cnotmatch '^[0-9a-f]{40}$' -or
                $owner.sourceRelease.freshMainSha -cnotmatch '^[0-9a-f]{40}$') { throw 'Invalid source release provenance.' }
            Assert-CoordinatorText $owner.sourceRelease.reachabilityEvidence
        }
        $ownerIds += $owner.id
    }
    $executionIds = @()
    $chargedOwners = @()
    foreach ($execution in $State.executions) {
        Assert-CoordinatorFields $execution @('id', 'ownerId', 'epoch', 'dispatcher', 'phase', 'evidence', 'recoveryIssued')
        Assert-CoordinatorText $execution.id -Identifier
        Assert-CoordinatorNumber $execution.epoch
        Assert-CoordinatorText $execution.dispatcher
        if ($execution.id -cin $executionIds -or $execution.ownerId -cnotin $ownerIds -or
            $execution.phase -cnotin @('dispatching', 'accepted', 'uncertain', 'released') -or
            $execution.recoveryIssued -isnot [bool]) { throw 'Invalid execution allocation.' }
        if ($null -ne $execution.evidence) { Assert-CoordinatorText $execution.evidence }
        if ($execution.phase -cne 'released') {
            if ($execution.ownerId -cin $chargedOwners) { throw 'Duplicate active allocation for one owner.' }
            $chargedOwners += $execution.ownerId
        }
        $executionIds += $execution.id
    }
    $charged = @($State.executions | Where-Object phase -cne 'released')
    if ($State.Contains('legacy')) {
        Assert-CoordinatorFields $State.legacy @('snapshotDigest', 'executionIds', 'retirements')
        if ($State.legacy.snapshotDigest -cnotmatch '^[0-9A-F]{64}$' -or
            $State.legacy.executionIds -isnot [array] -or $State.legacy.retirements -isnot [array]) { throw 'Invalid legacy provenance.' }
        $importedIds = @()
        foreach ($entry in $State.legacy.executionIds) {
            Assert-CoordinatorFields $entry @('id', 'source', 'key')
            foreach ($key in $entry.Keys) { Assert-CoordinatorText $entry[$key] }
            if ($entry.id -cnotin $executionIds -or $entry.id -cin $importedIds) { throw 'Lost or duplicate legacy execution.' }
            $importedIds += $entry.id
        }
        $retiredIds = @()
        foreach ($retirement in $State.legacy.retirements) {
            Assert-CoordinatorFields $retirement @('executionId', 'retirementAuthority', 'termination', 'remoteReconciliation')
            Assert-CoordinatorText $retirement.retirementAuthority
            Assert-CoordinatorText $retirement.remoteReconciliation
            Assert-CoordinatorTerminationEvidence $retirement.termination $retirement.executionId
            if ($retirement.executionId -cnotin $importedIds -or $retirement.executionId -cin $retiredIds) {
                throw 'Invalid legacy retirement identity.'
            }
            $execution = @($State.executions | Where-Object id -CEQ $retirement.executionId)[0]
            if ($execution.phase -cne 'released' -or $execution.evidence -cne (Get-CoordinatorDigest $retirement)) {
                throw 'Legacy retirement provenance mismatch.'
            }
            $retiredIds += $retirement.executionId
        }
        foreach ($execution in $State.executions | Where-Object id -CIn $importedIds) {
            if (($execution.phase -ceq 'released') -ne ($execution.id -cin $retiredIds)) {
                throw 'Imported execution release lacks retirement evidence.'
            }
        }
    }
    if ($charged.Count -gt 10 -and (!$State.Contains('legacy') -or
        @($charged | Where-Object id -CNotIn @($State.legacy.executionIds | ForEach-Object id)).Count)) {
        throw 'Shared execution ceiling exceeded.'
    }
    $eventIds = @()
    foreach ($event in $State.events) {
        Assert-CoordinatorFields $event @('id', 'digest')
        Assert-CoordinatorText $event.id -Identifier
        if ($event.id -cin $eventIds -or $event.digest -cnotmatch '^[0-9A-F]{64}$') { throw 'Invalid event deduplication state.' }
        $eventIds += $event.id
    }
}

function Get-CoordinatorOwner {
    param($State, [string] $Id)
    $owners = @($State.owners | Where-Object id -CEQ $Id)
    if ($owners.Count -ne 1) { throw 'Unregistered owner.' }
    return $owners[0]
}

function Invoke-CoordinatorOperation {
    param($State, [string] $Action, $Request, [string] $Session, [DateTimeOffset] $Now, [string] $Directory)
    switch ($Action) {
        'Claim' {
            Assert-CoordinatorFields $Request @()
            if ($State.leader -cne $Session -and (ConvertTo-CoordinatorTime $State.leaseUntil) -gt $Now) {
                throw 'Another leader has an unexpired lease.'
            }
            if ($State.leader -cne $Session) { $State.epoch++; $State.leader = $Session }
            return @{ kind = 'leadership'; epoch = $State.epoch }
        }
        'Register' {
            Assert-CoordinatorFields $Request @('ownerId', 'operation', 'scopes', 'kind', 'allocation', 'evidence')
            Assert-CoordinatorText $Request.ownerId -Identifier
            Assert-CoordinatorText $Request.operation -Identifier
            Assert-CoordinatorText $Request.evidence
            Assert-CoordinatorScope $Request.scopes
            if ($Request.kind -cnotin @('feature', 'repair', 'evidence', 'historical') -or
                $Request.allocation -cnotin @('none', 'uncertain')) { throw 'Invalid registration.' }
            if (@($State.owners | Where-Object id -CEQ $Request.ownerId).Count) { throw 'Owner already registered.' }
            if ($Request.allocation -ceq 'uncertain' -and @($State.executions | Where-Object phase -CNE released).Count -ge 10) {
                throw 'Shared execution ceiling exceeded.'
            }
            $State.owners += @{
                id = $Request.ownerId; operation = $Request.operation; scopes = $Request.scopes; kind = $Request.kind
                sourceRetained = $true; candidate = 'none'; certification = $null; nativeIdentity = $null
                branch = $null; worktree = $null; delivery = 'none'; publication = $null; sourceRelease = $null; receiptRevision = 0
                recovery = if ($Request.allocation -ceq 'uncertain') { @{ reason = $Request.evidence; issued = $false } } else { $null }
            }
            $receiptDirectory = Join-Path (Join-Path $Directory 'owners') $Request.ownerId
            if (!(Test-Path -LiteralPath $receiptDirectory)) { New-ProofDirectory $receiptDirectory }
            Assert-NoLinks $receiptDirectory -AncestorsOnly
            if ($Request.allocation -ceq 'uncertain') {
                $State.executions += @{ id = 'legacy-' + [guid]::NewGuid().ToString('N'); ownerId = $Request.ownerId
                    epoch = $State.epoch; dispatcher = $Session; phase = 'uncertain'; evidence = $Request.evidence; recoveryIssued = $false }
            }
            return @{ kind = 'registered'; ownerId = $Request.ownerId }
        }
        'Certify' {
            Assert-CoordinatorFields $Request @('ownerId', 'certification', 'repair')
            $owner = Get-CoordinatorOwner $State $Request.ownerId
            if ($owner.kind -ceq 'historical' -or $Request.repair -isnot [bool] -or !$owner.sourceRetained -or
                @($State.executions | Where-Object { $_.ownerId -ceq $owner.id -and $_.phase -cne 'released' }).Count) {
                throw 'Cannot certify historical or executing work.'
            }
            $owner.certification = $Request.certification
            $owner.candidate = 'ready'
            if ($Request.repair) { $owner.kind = 'repair' }
            return @{ kind = 'certified'; ownerId = $owner.id }
        }
        'Recover' {
            Assert-CoordinatorFields $Request @('ownerId', 'reason')
            $owner = Get-CoordinatorOwner $State $Request.ownerId
            Assert-CoordinatorText $Request.reason
            if ($null -eq $owner.recovery) { $owner.recovery = @{ reason = $Request.reason; issued = $false } }
            if ($owner.candidate -ceq 'ready') { $owner.candidate = 'none'; $owner.certification = $null }
            return @{ kind = 'recovery-recorded'; ownerId = $owner.id }
        }
        'ReleaseSource' {
            Assert-CoordinatorFields $Request @('ownerId', 'mergeSha', 'freshMainSha', 'reachabilityEvidence')
            $owner = Get-CoordinatorOwner $State $Request.ownerId
            if ($Request.mergeSha -cnotmatch '^[0-9a-f]{40}$' -or $Request.freshMainSha -cnotmatch '^[0-9a-f]{40}$' -or
                $owner.delivery -cne 'pr-ci-wait' -or
                @($State.executions | Where-Object { $_.ownerId -ceq $owner.id -and $_.phase -cne 'released' }).Count) {
                throw 'Source release needs merged/reachable delivery evidence and joined execution.'
            }
            Assert-CoordinatorText $Request.reachabilityEvidence
            $owner.sourceRetained = $false
            $owner.sourceRelease = @{ mergeSha = $Request.mergeSha; freshMainSha = $Request.freshMainSha
                reachabilityEvidence = $Request.reachabilityEvidence }
            $owner.candidate = 'none'
            return @{ kind = 'source-released'; ownerId = $owner.id }
        }
        'Reconcile' {
            Assert-CoordinatorFields $Request @('executionId', 'outcome', 'evidence', 'dispatcherJoined', 'nativeIdentity', 'branch', 'worktree')
            $executions = @($State.executions | Where-Object id -CEQ $Request.executionId)
            if ($executions.Count -ne 1) { throw 'Unknown execution intent.' }
            $execution = $executions[0]
            if ($State.Contains('legacy') -and $execution.id -cin @($State.legacy.executionIds | ForEach-Object id)) {
                throw 'Imported execution requires exact RetireLegacy evidence.'
            }
            $owner = Get-CoordinatorOwner $State $execution.ownerId
            Assert-CoordinatorText $Request.evidence
            if ($Request.outcome -cnotin @('accepted', 'uncertain', 'not-dispatched') -or
                $Request.dispatcherJoined -isnot [bool] -or $execution.phase -ceq 'released') { throw 'Invalid dispatch reconciliation.' }
            if ($Request.outcome -ceq 'not-dispatched') {
                if (!$Request.dispatcherJoined -or $execution.phase -ceq 'accepted') { throw 'No proof of joined non-dispatch.' }
                $execution.phase = 'released'
                $owner.candidate = 'none'
            } elseif ($Request.outcome -ceq 'accepted') {
                foreach ($key in @('nativeIdentity', 'branch', 'worktree')) {
                    Assert-CoordinatorText $Request[$key]
                    if ($null -ne $owner[$key] -and $owner[$key] -cne $Request[$key]) { throw 'Dispatch identity changed.' }
                    $owner[$key] = $Request[$key]
                }
                $execution.phase = 'accepted'
            } else {
                if ($execution.phase -ceq 'accepted') { throw 'Accepted execution cannot lose identity.' }
                $execution.phase = 'uncertain'
            }
            $execution.evidence = $Request.evidence
            return @{ kind = 'dispatch-reconciled'; executionId = $execution.id; phase = $execution.phase }
        }
        'RetireLegacy' {
            Assert-CoordinatorFields $Request @('executionId', 'retirementAuthority', 'termination', 'remoteReconciliation')
            if (!$State.Contains('legacy') -or $Request.executionId -cnotin @($State.legacy.executionIds | ForEach-Object id)) {
                throw 'Unregistered imported execution.'
            }
            Assert-CoordinatorText $Request.retirementAuthority
            Assert-CoordinatorText $Request.remoteReconciliation
            Assert-CoordinatorTerminationEvidence $Request.termination $Request.executionId
            $prior = @($State.legacy.retirements | Where-Object executionId -CEQ $Request.executionId)
            if ($prior.Count) {
                if ((Get-CoordinatorDigest $prior[0]) -cne (Get-CoordinatorDigest $Request)) { throw 'Legacy retirement evidence changed.' }
                return @{ kind = 'duplicate'; executionId = $Request.executionId }
            }
            $execution = @($State.executions | Where-Object id -CEQ $Request.executionId)[0]
            if ($execution.phase -ceq 'released') { throw 'Imported execution already released outside retirement.' }
            $execution.phase = 'released'
            $execution.evidence = Get-CoordinatorDigest $Request
            $State.legacy.retirements += $Request
            return @{ kind = 'legacy-retired'; executionId = $execution.id; sourceOwnership = 'retained' }
        }
        'Consume' {
            Assert-CoordinatorFields $Request @('receiptPath')
            if (![IO.Path]::IsPathFullyQualified($Request.receiptPath)) { throw 'Receipt path must be absolute.' }
            $path = [IO.Path]::GetFullPath($Request.receiptPath)
            $ownersRoot = Join-Path $Directory 'owners'
            $parent = Split-Path -Parent $path
            $registeredId = Split-Path -Leaf $parent
            $owner = Get-CoordinatorOwner $State $registeredId
            $registeredDirectory = Join-Path $ownersRoot $owner.id
            if ($parent -cne $registeredDirectory) { throw 'Receipt outside registered owner directory.' }
            if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Missing receipt.' }
            Assert-NoLinks $path -AncestorsOnly
            $receipt = Read-CoordinatorJson $path
            Assert-CoordinatorFields $receipt @('version', 'datasetId', 'eventId', 'ownerId', 'executionId', 'revision',
                'nativeIdentity', 'branch', 'worktree', 'head', 'kind', 'workState', 'finiteCommandsJoined',
                'runningCommands', 'heldDecision', 'publicationPolicy', 'prUrl', 'autoMerge')
            if ($receipt.ownerId -cne $owner.id) { throw 'Cross-owner receipt.' }
            Assert-CoordinatorText $receipt.eventId -Identifier
            Assert-CoordinatorNumber $receipt.revision
            Assert-CoordinatorNumber $receipt.version
            if ($receipt.version -cne 1 -or $receipt.datasetId -cne $State.datasetId -or
                $receipt.nativeIdentity -cne $owner.nativeIdentity -or !$owner.nativeIdentity -or
                $receipt.branch -cne $owner.branch -or $receipt.worktree -cne $owner.worktree -or
                $receipt.head -cnotmatch '^[0-9a-f]{40}$' -or $receipt.publicationPolicy -cne $State.publicationPolicy) {
                throw 'Receipt identity/authority mismatch.'
            }
            $digest = Get-CoordinatorDigest $receipt
            $prior = @($State.events | Where-Object id -CEQ $receipt.eventId)
            if ($prior.Count) {
                if ($prior[0].digest -cne $digest) { throw 'Event ID was reused with different content.' }
                return @{ kind = 'duplicate'; eventId = $receipt.eventId }
            }
            if ($receipt.revision -le $owner.receiptRevision) { throw 'Superseded receipt revision.' }
            $executions = @($State.executions | Where-Object { $_.id -ceq $receipt.executionId -and $_.ownerId -ceq $owner.id })
            if ($executions.Count -ne 1) { throw 'Receipt does not name its admitted execution.' }
            $execution = $executions[0]
            if ($State.Contains('legacy') -and $execution.id -cin @($State.legacy.executionIds | ForEach-Object id)) {
                throw 'Imported execution requires exact RetireLegacy evidence.'
            }
            if ($receipt.workState -cne 'stopped' -or $receipt.finiteCommandsJoined -isnot [bool] -or
                !$receipt.finiteCommandsJoined -or $receipt.runningCommands -isnot [array] -or
                $receipt.runningCommands.Count -ne 0 -or $null -ne $receipt.heldDecision) { throw 'Execution is not proved quiescent.' }
            if ($receipt.kind -ceq 'pr-enrolled-ci-wait') {
                if ($State.publicationPolicy -ceq 'local-only' -or $receipt.prUrl -cnotmatch '^https://github.com/roryprimrose/Kora/pull/[1-9][0-9]*$') {
                    throw 'PR publication not authorized.'
                }
                if ($State.publicationPolicy -ceq 'ready-auto') {
                    Assert-CoordinatorFields $receipt.autoMerge @('method', 'enabledAt')
                    if ($receipt.autoMerge.method -cne 'SQUASH') { throw 'Protected squash readback required.' }
                    $null = ConvertTo-CoordinatorTime $receipt.autoMerge.enabledAt
                } elseif ($null -ne $receipt.autoMerge) { throw 'PR-only authority forbids Auto.' }
                $owner.delivery = 'pr-ci-wait'
            } elseif ($receipt.kind -ceq 'completed' -and $State.publicationPolicy -ceq 'local-only' -and
                $null -eq $receipt.prUrl -and $null -eq $receipt.autoMerge) {
                $owner.delivery = 'local-completed'
            } elseif ($receipt.kind -ceq 'blocked-quiescent' -and $null -eq $receipt.prUrl -and $null -eq $receipt.autoMerge) {
                $owner.delivery = 'none'
            } else { throw 'Unsupported terminal handoff.' }
            if ($execution.phase -ceq 'released') { throw 'Execution already released; no second release.' }
            $execution.phase = 'released'
            $owner.receiptRevision = $receipt.revision
            $owner.candidate = 'none'
            $owner.publication = @{ head = $receipt.head; prUrl = $receipt.prUrl; autoMerge = $receipt.autoMerge; receiptPath = $path }
            $State.events += @{ id = $receipt.eventId; digest = $digest }
            return @{ kind = 'released'; executionId = $execution.id; sourceOwnership = 'retained' }
        }
        'Next' {
            Assert-CoordinatorFields $Request @()
            $charged = @($State.executions | Where-Object phase -CNE 'released')
            if ($State.Contains('legacy') -and
                @($charged | Where-Object id -CIn @($State.legacy.executionIds | ForEach-Object id)).Count) {
                return @{ kind = 'blocked'; reason = 'legacy-reconciliation-required'; charged = $charged.Count
                    missingInput = 'Exact imported execution termination, retirement authority and remote-mutation reconciliation; no native dispatch.' }
            }
            $constraints = @()
            $candidates = @($State.owners | Where-Object candidate -CEQ 'ready' | Sort-Object @{ Expression = { $_.kind -cne 'repair' } }, id)
            if ($charged.Count -lt 10) {
                foreach ($candidate in $candidates) {
                    $observed = ConvertTo-CoordinatorTime $candidate.certification.observedUtc
                    if ($observed -gt $Now -or ($Now - $observed).TotalSeconds -gt 60) {
                        $constraints += @{ ownerId = $candidate.id; reason = 'refresh-pr-observation' }
                        continue
                    }
                    $conflicts = @($State.owners | Where-Object {
                        $_.id -cne $candidate.id -and $_.sourceRetained -and $_.candidate -cne 'ready' -and
                        ($_.operation -ceq $candidate.operation -or (Test-CoordinatorOverlap $_.scopes $candidate.scopes))
                    })
                    if ($conflicts.Count) {
                        $constraints += @{ ownerId = $candidate.id; reason = 'source-conflict'; owners = @($conflicts.id) }
                        continue
                    }
                    if ($candidate.kind -ceq 'evidence' -and @($charged | Where-Object {
                        (Get-CoordinatorOwner $State $_.ownerId).kind -ceq 'evidence'
                    }).Count) { continue }
                    $id = 'execution-' + [guid]::NewGuid().ToString('N')
                    $State.executions += @{ id = $id; ownerId = $candidate.id; epoch = $State.epoch
                        dispatcher = $Session; phase = 'dispatching'; evidence = $null; recoveryIssued = $false }
                    $candidate.candidate = 'reserved'
                    return @{ kind = 'dispatch'; executionId = $id; ownerId = $candidate.id; epoch = $State.epoch
                        publicationPolicy = $State.publicationPolicy; authorityId = $State.authorityId; publisher = $State.publisher
                        instruction = 'Native dispatch once; on ambiguity reconcile this intent, never replay.' }
                }
            }
            $unknown = @($charged | Where-Object { $_.phase -cin @('dispatching', 'uncertain') -and !$_.recoveryIssued } | Select-Object -First 3)
            if ($unknown.Count) {
                foreach ($execution in $unknown) {
                    $execution.recoveryIssued = $true
                    $owner = Get-CoordinatorOwner $State $execution.ownerId
                    if ($null -ne $owner.recovery) { $owner.recovery.issued = $true }
                }
                return @{ kind = 'reconcile-dispatch'; executionIds = @($unknown.id); charged = $charged.Count
                    missingInput = 'Exact original caller/runner outcome and joined-command evidence; no launch replay.' }
            }
            $recovery = @($State.owners | Where-Object { $null -ne $_.recovery -and !$_.recovery.issued } | Select-Object -First 3)
            if ($recovery.Count) {
                foreach ($owner in $recovery) { $owner.recovery.issued = $true }
                return @{ kind = 'recover-scoped'; ownerIds = @($recovery.id); maximumRecords = 3
                    missingInput = 'Same-owner terminal receipt or exact command completion; no fleet inventory.' }
            }
            return @{ kind = 'blocked'; reason = if ($charged.Count -ge 10) { 'capacity' } else { 'no-safe-certified-candidate' }
                charged = $charged.Count; constraints = $constraints
                unresolvedExecutions = @($charged | Where-Object phase -CIN @('dispatching', 'uncertain') | ForEach-Object id)
                missingInput = 'Certified approved operation, affected observation/ownership delta or exact stopped handoff.' }
        }
        default { throw 'Unsupported coordinator operation.' }
    }
}

function Invoke-Coordinator {
    param([string] $RepositoryPath, [string] $Action, [string] $Session, [long] $ExpectedEpoch,
        [long] $ExpectedRevision, [string] $RequestPath)
    $identity = Get-CoordinatorLocation $RepositoryPath
    $directory = $identity.directory
    $request = if ($RequestPath) { Read-CoordinatorJson $RequestPath } else { @{} }
    if ($Action -ceq 'Adopt') { return Invoke-CoordinatorAdoption $identity $request $Session }
    if ($Action -ceq 'Initialize' -and @(Get-CoordinatorLegacyLocations $identity).Count) {
        throw 'Known legacy state exists; initialization refused. Resolve quiescence and use Adopt.'
    }
    if (!(Test-Path -LiteralPath $directory)) {
        if ($Action -cne 'Initialize') {
            throw "Canonical dataset absent. Resolve legacy locator/authority before explicit initialization. Known legacy locations: $(@(Get-CoordinatorLegacyLocations $identity) -join '; ')"
        }
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    Assert-NoLinks $directory -AncestorsOnly
    $lockPath = Join-Path $directory 'writer.lock'
    if (Test-Path -LiteralPath $lockPath) { Assert-NoLinks $lockPath -AncestorsOnly }
    $lock = $null
    $legacyLocks = @()
    try {
        $lock = [IO.FileStream]::new($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    } catch [IO.IOException] {
        return @{ kind = 'blocked'; reason = 'writer-lock-unavailable'; missingInput = 'Retry on supported resume; never break the lock.' }
    }
    try {
        $datasetPath = Join-Path $directory 'dataset.json'
        $statePath = Join-Path $directory 'registry.json'
        $redoPath = Join-Path $directory 'pending-transition.json'
        $now = [DateTimeOffset]::UtcNow
        if ($Action -ceq 'Initialize') {
            Assert-CoordinatorFields $request @('legacyAbsenceConfirmed', 'authorityId', 'publicationPolicy', 'publisher')
            if ($request.legacyAbsenceConfirmed -isnot [bool] -or !$request.legacyAbsenceConfirmed) {
                throw 'Explicit legacy absence/initialization authority required; no live migration.'
            }
            Assert-CoordinatorText $Session
            if (Test-Path -LiteralPath $datasetPath) { throw 'Dataset already exists; resume it, do not reset.' }
            if (@(Get-ChildItem -LiteralPath $directory -Force | Where-Object Name -CNE 'writer.lock').Count) {
                throw 'Unrecognized existing coordinator data; initialization refused.'
            }
            $state = [ordered]@{ version = 1; datasetId = [guid]::NewGuid().ToString(); identity = $identity
                revision = 0; epoch = 1; leader = $Session; leaseUntil = $now.AddMinutes(3).ToString('o'); lastUtc = $now.ToString('o')
                authorityId = $request.authorityId; publicationPolicy = $request.publicationPolicy; publisher = $request.publisher
                owners = @(); executions = @(); events = @(); continuation = 'unavailable' }
            Assert-CoordinatorState $state $identity
            Write-CoordinatorJson $datasetPath @{ version = 1; datasetId = $state.datasetId; identity = $identity }
            Write-CoordinatorJson $statePath $state
            return @{ kind = 'initialized'; locator = $directory; state = $state; monitoring = 'resumable-not-continuously-monitoring' }
        }
        $dataset = Read-CoordinatorJson $datasetPath
        Assert-CoordinatorFields $dataset @('version', 'datasetId', 'identity')
        Assert-CoordinatorNumber $dataset.version
        if ($dataset.version -cne 1 -or (Get-CoordinatorDigest $dataset.identity) -cne (Get-CoordinatorDigest $identity)) {
            throw 'Dataset locator/identity mismatch; no legacy adoption.'
        }
        $state = Read-CoordinatorJson $statePath -MaximumBytes 1048576
        Assert-CoordinatorState $state $identity
        if ($state.datasetId -cne $dataset.datasetId) { throw 'Registry dataset mismatch.' }
        $legacyLocks = Assert-CoordinatorLegacySnapshot $state $directory
        if (Test-Path -LiteralPath $redoPath) {
            $redo = Read-CoordinatorJson $redoPath -MaximumBytes 1048576
            Assert-CoordinatorFields $redo @('previousDigest', 'nextDigest', 'next')
            Assert-CoordinatorState $redo.next $identity
            $digest = Get-CoordinatorDigest $state
            if ($redo.nextDigest -cne (Get-CoordinatorDigest $redo.next) -or $redo.next.datasetId -cne $state.datasetId) {
                throw 'Corrupt pending transition.'
            }
            if ($digest -ceq $redo.previousDigest -and $redo.next.revision -eq $state.revision + 1) {
                Write-CoordinatorJson $statePath $redo.next
                $state = $redo.next
            } elseif ($digest -cne $redo.nextDigest) { throw 'Noncontiguous pending transition; recovery required.' }
        }
        if ($Action -ceq 'Read') {
            return @{ kind = 'snapshot'; locator = $directory; state = $state
                monitoring = if ($state.continuation -ceq 'unavailable') { 'resumable-not-continuously-monitoring' } else { 'host-verified-at-registration-not-a-wakeup-guarantee' } }
        }
        Assert-CoordinatorText $Session
        if ($state.revision -ne $ExpectedRevision -or $state.epoch -ne $ExpectedEpoch) { throw 'Stale epoch/revision.' }
        if ($now -lt (ConvertTo-CoordinatorTime $state.lastUtc)) { throw 'Clock moved backwards; authority unknown.' }
        if ($Action -cne 'Claim' -and ($state.leader -cne $Session -or (ConvertTo-CoordinatorTime $state.leaseUntil) -le $now)) {
            throw 'Current unexpired leader required.'
        }
        $previous = Get-CoordinatorDigest $state
        $result = Invoke-CoordinatorOperation $state $Action $request $Session $now $directory
        $state.revision++
        $state.lastUtc = $now.ToString('o')
        $state.leaseUntil = $now.AddMinutes(3).ToString('o')
        Assert-CoordinatorState $state $identity
        Write-CoordinatorJson $redoPath @{ previousDigest = $previous; nextDigest = (Get-CoordinatorDigest $state); next = $state }
        Write-CoordinatorJson $statePath $state
        return @{ kind = $result.kind; action = $result; revision = $state.revision; epoch = $state.epoch
            charged = @($state.executions | Where-Object phase -CNE 'released').Count
            monitoring = 'resumable-not-continuously-monitoring' }
    } finally {
        foreach ($handle in $legacyLocks) { $handle.Dispose() }
        $lock.Dispose()
    }
}
