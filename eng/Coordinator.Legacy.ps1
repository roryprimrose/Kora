#requires -Version 7.5

function Get-CoordinatorLegacyLocations {
    param($Identity)
    return @(
        (Join-Path (Join-Path $Identity.commonGit '.scratch') 'roadmap-coordinator'),
        (Join-Path (Join-Path $Identity.workspace '.scratch') 'pr-repair-orchestration')
    ) | Where-Object { Test-Path -LiteralPath $_ }
}

function Assert-CoordinatorTerminationEvidence {
    param($Evidence, [string] $Subject)
    Assert-CoordinatorFields $Evidence @('kind', 'subject', 'reference', 'executionIdentity', 'hostIdentity', 'continuationBoundary')
    foreach ($key in $Evidence.Keys) { Assert-CoordinatorText $Evidence[$key] }
    if ($Evidence.kind -cnotin @('joined-command', 'host-lifecycle') -or $Evidence.subject -cne $Subject) {
        throw 'Exact joined-command or host-lifecycle evidence required; age, merge and missing chats are not termination.'
    }
}

function Open-CoordinatorLegacyLocks {
    param($Sources)
    $locks = @()
    try {
        foreach ($path in @($Sources.lockPath | Sort-Object -Unique)) {
            Assert-NoLinks $path -AncestorsOnly
            $locks += [IO.FileStream]::new($path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        }
        return ,$locks
    } catch {
        foreach ($handle in $locks) { $handle.Dispose() }
        throw
    }
}

function Assert-CoordinatorLegacySnapshot {
    param($State, [string] $Directory)
    if (!$State.Contains('legacy')) { return ,@() }
    $snapshot = Read-CoordinatorJson (Join-Path $Directory 'legacy-snapshot.json') -MaximumBytes 1048576
    if ((Get-CoordinatorDigest $snapshot) -cne $State.legacy.snapshotDigest) { throw 'Legacy snapshot digest mismatch.' }
    $locks = Open-CoordinatorLegacyLocks $snapshot.sources
    try {
        foreach ($source in $snapshot.sources) {
            $current = Read-CoordinatorJson $source.path -MaximumBytes 1048576
            if ((Get-CoordinatorDigest $current) -cne $source.digest) {
                throw 'Retired legacy source changed; canonical admission is blocked.'
            }
        }
        return ,$locks
    } catch {
        foreach ($handle in $locks) { $handle.Dispose() }
        throw
    }
}

function Invoke-CoordinatorAdoption {
    param($Identity, $Request, [string] $Session)
    Assert-CoordinatorFields $Request @('legacyDirectory', 'datasetDigest', 'registryDigest', 'repairDigest',
        'authorityId', 'inventoryEvidence', 'quiescenceEvidence')
    Assert-CoordinatorText $Session
    Assert-CoordinatorText $Request.authorityId
    Assert-CoordinatorText $Request.inventoryEvidence
    if (![IO.Path]::IsPathFullyQualified($Request.legacyDirectory)) { throw 'Legacy directory must be absolute.' }
    $legacyDirectory = (Resolve-Path -LiteralPath $Request.legacyDirectory).Path
    Assert-NoLinks $legacyDirectory -AncestorsOnly
    if ($legacyDirectory.StartsWith('\\') -or $legacyDirectory -eq $Identity.directory) { throw 'Invalid legacy locator.' }
    if (Test-Path -LiteralPath $Identity.directory) { throw 'Canonical dataset exists; Read it, never adopt twice.' }
    $knownScheduler = Join-Path (Join-Path $Identity.commonGit '.scratch') 'roadmap-coordinator'
    if ((Test-Path -LiteralPath $knownScheduler) -and $legacyDirectory -ne $knownScheduler) {
        throw 'Known legacy scheduler cannot be omitted from adoption.'
    }
    $datasetPath = Join-Path $legacyDirectory 'dataset.json'
    $registryPath = Join-Path $legacyDirectory 'registry.json'
    $sources = @(
        @{ path = $datasetPath; lockPath = (Join-Path $legacyDirectory 'writer.lock'); digest = $Request.datasetDigest },
        @{ path = $registryPath; lockPath = (Join-Path $legacyDirectory 'writer.lock'); digest = $Request.registryDigest }
    )
    $repairDirectory = Join-Path (Join-Path $Identity.workspace '.scratch') 'pr-repair-orchestration'
    if (Test-Path -LiteralPath $repairDirectory) {
        $sources += @{ path = (Join-Path $repairDirectory 'coordinator.json')
            lockPath = (Join-Path $repairDirectory 'coordinator.lock'); digest = $Request.repairDigest }
    } elseif ($null -ne $Request.repairDigest) { throw 'Recorded repair ledger is missing.' }
    $locks = Open-CoordinatorLegacyLocks $sources
    $staging = $null
    try {
        foreach ($source in $sources) {
            if ($source.digest -cnotmatch '^[0-9A-F]{64}$') { throw 'Exact legacy source digest required.' }
            $source.value = Read-CoordinatorJson $source.path -MaximumBytes 1048576
            if ((Get-CoordinatorDigest $source.value) -cne $source.digest) { throw 'Legacy source digest changed.' }
        }
        $dataset = $sources[0].value
        $registry = $sources[1].value
        foreach ($value in @($dataset, $registry)) {
            Assert-CoordinatorNumber $value.protocolVersion
            Assert-CoordinatorNumber $value.schemaVersion
            if ($value.protocolVersion -cne 2 -or $value.schemaVersion -cne 2 -or
                $value.repository -cne $Identity.repository -or $value.commonGitDirectory -ne $Identity.commonGit) {
                throw 'Unsupported legacy repository/schema identity.'
            }
        }
        if ($dataset.datasetId -cnotmatch '^[0-9a-f-]{36}$' -or $registry.datasetId -cne $dataset.datasetId) {
            throw 'Legacy dataset identity mismatch.'
        }
        foreach ($key in @('revision', 'coordinatorEpoch')) { Assert-CoordinatorNumber $registry[$key] }
        foreach ($key in @('activeExecutionAllocations', 'externalExecutionCharges', 'retainedOwnership', 'runs')) {
            if ($registry[$key] -isnot [array]) { throw "Missing legacy index: $key" }
        }
        foreach ($run in $registry.runs) {
            if ($run.publicationPolicy -cne 'local-only') { throw 'Adoption supports only the retained local-only authority scope.' }
        }
        $writers = @($registry.leader.session)
        $records = @()
        foreach ($allocation in $registry.activeExecutionAllocations) {
            Assert-CoordinatorNumber $allocation.allocationCharge
            if ($allocation.allocationCharge -cne 1) { throw 'Unsupported legacy allocation charge.' }
            Assert-CoordinatorText $allocation.executionTransitionId
            $records += @{ key = "active/$($allocation.executionTransitionId)"; source = $registryPath
                nativeIdentity = $allocation.ownerSession; branch = $null; worktree = $null; charged = $true }
        }
        foreach ($allocation in $registry.externalExecutionCharges) {
            Assert-CoordinatorNumber $allocation.allocationCharge
            if ($allocation.allocationCharge -cne 1) { throw 'Unsupported legacy external charge.' }
            Assert-CoordinatorText $allocation.chargeId
            $records += @{ key = "external/$($allocation.chargeId)"; source = $registryPath
                nativeIdentity = $allocation.owner; branch = $null; worktree = $null; charged = $true }
        }
        foreach ($owner in $registry.retainedOwnership) {
            Assert-CoordinatorText $owner.feature
            $records += @{ key = "retained/$($owner.feature)"; source = $registryPath
                nativeIdentity = $owner.ownerSession; branch = $owner.branch; worktree = $owner.worktree; charged = $false }
        }
        if ($sources.Count -eq 3) {
            $repair = $sources[2].value
            if ($repair.repository -cne $Identity.repository -or $repair.currentOwners -isnot [array]) {
                throw 'Unsupported companion repair ledger.'
            }
            Assert-CoordinatorNumber $repair.revision
            $writers += $repair.schedulingOwner.session
            foreach ($owner in $repair.currentOwners) {
                Assert-CoordinatorNumber $owner.pr
                $key = if ($owner.Contains('reservationId')) { $owner.reservationId } else { "pr-$($owner.pr)" }
                Assert-CoordinatorText $key
                $records += @{ key = "repair/$key"; source = $sources[2].path; nativeIdentity = $owner.session
                    branch = $owner.branch; worktree = $owner.actualWorktree; charged = $true }
            }
        }
        if ($Request.quiescenceEvidence -isnot [array] -or $Request.quiescenceEvidence.Count -ne $writers.Count) {
            throw 'Quiescence evidence for every legacy writer required.'
        }
        for ($index = 0; $index -lt $writers.Count; $index++) {
            Assert-CoordinatorText $writers[$index]
            Assert-CoordinatorTerminationEvidence $Request.quiescenceEvidence[$index] $writers[$index]
        }
        $now = [DateTimeOffset]::UtcNow
        $snapshot = @{ sources = $sources; authorityId = $Request.authorityId; inventoryEvidence = $Request.inventoryEvidence
            quiescenceEvidence = $Request.quiescenceEvidence; adoptedUtc = $now.ToString('o') }
        $state = [ordered]@{ version = 1; datasetId = $dataset.datasetId; identity = $Identity
            revision = $registry.revision; epoch = $registry.coordinatorEpoch + 1; leader = $Session
            leaseUntil = $now.AddMinutes(3).ToString('o'); lastUtc = $now.ToString('o')
            authorityId = $Request.authorityId; publicationPolicy = 'local-only'; publisher = $null
            owners = @(); executions = @(); events = @(); continuation = 'unavailable'
            legacy = @{ snapshotDigest = (Get-CoordinatorDigest $snapshot); executionIds = @(); retirements = @() } }
        foreach ($record in $records) {
            $suffix = (Get-CoordinatorDigest @{ source = $record.source; key = $record.key }).Substring(0, 24).ToLowerInvariant()
            $ownerId = "legacy-$suffix"
            if ($ownerId -cin @($state.owners | ForEach-Object id)) { throw 'Duplicate legacy execution/ownership identity.' }
            $state.owners += @{ id = $ownerId; operation = $ownerId; scopes = @('src', 'tests', 'eng', '.github', 'Design', 'docs')
                kind = 'repair'; sourceRetained = $true; candidate = 'none'; certification = $null
                nativeIdentity = $record.nativeIdentity; branch = $record.branch; worktree = $record.worktree
                delivery = 'none'; publication = $null; sourceRelease = $null; receiptRevision = 0
                recovery = @{ reason = "Retained legacy ownership: $($record.source) [$($record.key)]"; issued = $false } }
            if ($record.charged) {
                $id = "import-$suffix"
                $state.executions += @{ id = $id; ownerId = $ownerId; epoch = $registry.coordinatorEpoch
                    dispatcher = $registry.leader.session; phase = 'uncertain'; evidence = $record.source; recoveryIssued = $false }
                $state.legacy.executionIds += @{ id = $id; source = $record.source; key = $record.key }
            }
        }
        Assert-CoordinatorState $state $Identity
        $staging = Join-Path (Split-Path -Parent $Identity.directory) ("coordinator-adoption-" + [guid]::NewGuid().ToString('N'))
        New-ProofDirectory $staging
        Write-CoordinatorJson (Join-Path $staging 'legacy-snapshot.json') $snapshot
        Write-CoordinatorJson (Join-Path $staging 'dataset.json') @{ version = 1; datasetId = $state.datasetId; identity = $Identity }
        Write-CoordinatorJson (Join-Path $staging 'registry.json') $state
        [IO.Directory]::Move($staging, $Identity.directory)
        return @{ kind = 'adopted'; locator = $Identity.directory; state = $state
            charged = $state.executions.Count; monitoring = 'resumable-not-continuously-monitoring' }
    } finally {
        foreach ($handle in $locks) { $handle.Dispose() }
        if ($staging -and (Test-Path -LiteralPath $staging)) { [IO.Directory]::Delete($staging, $true) }
    }
}
