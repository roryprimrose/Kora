#requires -Version 7.5

function Assert-TerminalRecoveryPath {
    param($Path, [string] $Directory)
    Assert-CoordinatorText $Path
    if (![IO.Path]::IsPathFullyQualified($Path) -or [IO.Path]::GetFullPath($Path) -cne $Path -or
        (Split-Path -Parent $Path) -cne $Directory -or
        (Split-Path -Leaf $Path) -cnotmatch '^[A-Za-z0-9][A-Za-z0-9_.-]*\.json$') {
        throw 'Recovery requires a canonical registered owner JSON path.'
    }
    Assert-NoLinks $Path -AncestorsOnly
}

function Open-TerminalRecoveryInput {
    param([string] $Path, [int] $MaximumBytes, $Handles)
    Assert-NoLinks $Path -AncestorsOnly
    # Keep the original and supporting evidence non-writable through core consumption.
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $Handles.Add($stream)
    if ($stream.Length -gt $MaximumBytes) { throw 'Oversized recovery input.' }
    $bytes = [byte[]]::new([int]$stream.Length)
    $stream.ReadExactly($bytes)
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    $document = [System.Text.Json.JsonDocument]::Parse($text)
    try {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
            throw 'Recovery input must be a JSON object.'
        }
        foreach ($property in $document.RootElement.EnumerateObject()) {
            if (!$names.Add($property.Name)) { throw 'Duplicate recovery input field.' }
        }
    } finally { $document.Dispose() }
    return @{ bytes = $bytes; sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
        value = (Read-CoordinatorJson $Path -MaximumBytes $MaximumBytes) }
}

function Open-TerminalRecoveryEvidence {
    param([string] $Path, $Value, $Handles, [switch] $Existing)
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 40 -Compress))
    if (!(Test-Path -LiteralPath $Path)) {
        if ($Existing) { throw 'Missing immutable recovery evidence.' }
        $temporary = Join-Path (Split-Path -Parent $Path) ("pending-" + [guid]::NewGuid().ToString('N') + '.json')
        try {
            $stream = [IO.FileStream]::new($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try { $stream.Write($bytes); $stream.Flush($true) } finally { $stream.Dispose() }
            [IO.File]::Move($temporary, $Path, $false)
        } finally {
            if (Test-Path -LiteralPath $temporary) { [IO.File]::Delete($temporary) }
        }
    }
    $inputFile = Open-TerminalRecoveryInput $Path 1048576 $Handles
    if ([Convert]::ToBase64String($inputFile.bytes) -cne [Convert]::ToBase64String($bytes)) {
        throw 'Immutable recovery evidence changed.'
    }
    return $inputFile
}

function Invoke-CoordinatorTerminalRecovery {
    param([string] $RepositoryPath, [string] $Session, [long] $ExpectedEpoch,
        [long] $ExpectedRevision, [string] $RequestPath)
    $request = Read-CoordinatorJson $RequestPath
    Assert-CoordinatorFields $request @('receiptPath')
    $snapshot = Invoke-Coordinator -RepositoryPath $RepositoryPath -Action Read
    if ($snapshot.kind -cne 'snapshot') { return $snapshot }
    $state = $snapshot.state
    if ($state.epoch -ne $ExpectedEpoch -or $state.revision -ne $ExpectedRevision) { throw 'Stale epoch/revision.' }
    if ($state.leader -cne $Session -or (ConvertTo-CoordinatorTime $state.leaseUntil) -le [DateTimeOffset]::UtcNow) {
        throw 'Current unexpired leader required.'
    }
    $directory = $state.identity.directory
    $lockPath = Join-Path $directory 'terminal-recovery.lock'
    if (Test-Path -LiteralPath $lockPath) { Assert-NoLinks $lockPath -AncestorsOnly }
    try {
        $recoveryLock = [IO.FileStream]::new($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    } catch [IO.IOException] {
        return @{ kind = 'blocked'; reason = 'terminal-recovery-lock-unavailable'
            missingInput = 'Retry on supported resume with a fresh fence; never break the lock.' }
    }
    $handles = [Collections.Generic.List[IDisposable]]::new()
    try {
        Assert-CoordinatorText $request.receiptPath
        $ownerId = Split-Path -Leaf (Split-Path -Parent $request.receiptPath)
        $owner = Get-CoordinatorOwner $state $ownerId
        $ownerDirectory = Join-Path (Join-Path $directory 'owners') $owner.id
        Assert-TerminalRecoveryPath $request.receiptPath $ownerDirectory
        $original = Open-TerminalRecoveryInput $request.receiptPath 32768 $handles
        $receipt = $original.value
        $terminalFields = @('version', 'datasetId', 'eventId', 'ownerId', 'executionId', 'revision',
            'nativeIdentity', 'branch', 'worktree', 'head', 'kind', 'workState', 'finiteCommandsJoined',
            'runningCommands', 'heldDecision', 'publicationPolicy', 'prUrl', 'autoMerge')
        $metadataFields = @('epoch', 'dispatchRevision', 'sourceHead', 'workspaceHead',
            'assessmentPath', 'assessmentSha256', 'pointerPath', 'completedUtc')
        Assert-CoordinatorFields $receipt ($terminalFields + $metadataFields)
        if ($receipt.version -cne 1 -or $receipt.kind -cne 'evidence-completed' -or $owner.kind -cne 'evidence' -or
            $null -ne $receipt.prUrl -or $null -ne $receipt.autoMerge) {
            throw 'Recovery supports only schema1 evidence-completed assessment terminals without publication.'
        }
        foreach ($field in @('epoch', 'dispatchRevision')) { Assert-CoordinatorNumber $receipt[$field] }
        $execution = @($state.executions | Where-Object { $_.id -ceq $receipt.executionId -and $_.ownerId -ceq $owner.id })
        if ($execution.Count -ne 1 -or $execution[0].phase -cnotin @('accepted', 'released') -or
            $receipt.epoch -ne $execution[0].epoch -or
            $receipt.dispatchRevision -lt 1 -or $receipt.dispatchRevision -gt $state.revision) {
            throw 'Assessment execution/epoch/dispatch binding mismatch.'
        }
        foreach ($field in @('sourceHead', 'workspaceHead')) {
            if ($receipt[$field] -isnot [string] -or $receipt[$field] -cnotmatch '^[0-9a-f]{40}$') {
                throw 'Invalid assessment source head.'
            }
        }
        if ($receipt.sourceHead -cne $receipt.head -or $receipt.assessmentSha256 -isnot [string] -or
            $receipt.assessmentSha256 -cnotmatch '^[0-9a-fA-F]{64}$') { throw 'Assessment source/digest mismatch.' }
        $null = ConvertTo-CoordinatorTime $receipt.completedUtc
        foreach ($field in @('assessmentPath', 'pointerPath')) {
            Assert-TerminalRecoveryPath $receipt[$field] $ownerDirectory
        }
        if (@($request.receiptPath, $receipt.assessmentPath, $receipt.pointerPath | Select-Object -Unique).Count -ne 3) {
            throw 'Assessment, pointer and terminal must be distinct files.'
        }
        $assessment = Open-TerminalRecoveryInput $receipt.assessmentPath 1048576 $handles
        $pointer = Open-TerminalRecoveryInput $receipt.pointerPath 32768 $handles
        if (![string]::Equals($assessment.sha256, $receipt.assessmentSha256, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Assessment bytes do not match assessmentSha256.'
        }
        foreach ($inputFile in @($assessment, $pointer)) {
            foreach ($field in @('version', 'datasetId', 'ownerId', 'executionId', 'epoch', 'dispatchRevision', 'sourceHead')) {
                if (!$inputFile.value.Contains($field) -or $inputFile.value[$field] -cne $receipt[$field] -or
                    $inputFile.value[$field].GetType() -ne $receipt[$field].GetType()) {
                    throw "Assessment/pointer binding mismatch: $field"
                }
            }
        }
        if (!$assessment.value.Contains('workspaceHead') -or $assessment.value.workspaceHead -cne $receipt.workspaceHead) {
            throw 'Assessment workspace head mismatch.'
        }
        $pointerValue = $pointer.value
        Assert-CoordinatorFields $pointerValue @('version', 'datasetId', 'ownerId', 'executionId', 'epoch',
            'dispatchRevision', 'revision', 'kind', 'assessmentPath', 'assessmentSha256', 'sourceHead', 'candidateCount', 'summary')
        Assert-CoordinatorNumber $pointerValue.candidateCount
        Assert-CoordinatorText $pointerValue.summary
        if ($pointerValue.kind -cne 'assessment-pointer' -or $pointerValue.revision -cne $receipt.revision -or
            $pointerValue.revision.GetType() -ne $receipt.revision.GetType() -or
            $pointerValue.assessmentPath -cne $receipt.assessmentPath -or
            $pointerValue.assessmentSha256 -cne $receipt.assessmentSha256 -or
            !$assessment.value.Contains('candidateCount') -or
            $assessment.value.candidateCount -cne $pointerValue.candidateCount -or
            $assessment.value.candidateCount.GetType() -ne $pointerValue.candidateCount.GetType()) {
            throw 'Assessment pointer metadata mismatch.'
        }
        $projection = [ordered]@{}
        foreach ($field in $terminalFields) { $projection[$field] = $receipt[$field] }
        $key = (Get-CoordinatorDigest $request.receiptPath).ToLowerInvariant()
        $projectionPath = Join-Path $ownerDirectory "terminal-recovery-$key.json"
        $proofDirectory = Join-Path $directory 'terminal-recovery'
        if (!(Test-Path -LiteralPath $proofDirectory)) { New-ProofDirectory $proofDirectory }
        Assert-NoLinks $proofDirectory -AncestorsOnly
        $proofPath = Join-Path $proofDirectory "$key.proof.json"
        $consumePath = Join-Path $proofDirectory "$key.request.json"
        $eventKey = Get-CoordinatorEventKey $receipt.eventId
        $consumed = @($state.events | Where-Object id -CEQ $eventKey).Count -gt 0
        $proofRequired = $consumed -or (Test-Path -LiteralPath $projectionPath) -or (Test-Path -LiteralPath $consumePath)
        $projectionBytes = [Text.UTF8Encoding]::new($false).GetBytes(($projection | ConvertTo-Json -Depth 40 -Compress))
        $proof = [ordered]@{
            version = 1; producer = 'Coordinator.TerminalRecovery'; profile = 'schema1-assessment-terminal-v1'
            datasetId = $state.datasetId; authorityId = $state.authorityId
            originalPath = $request.receiptPath; originalSha256 = $original.sha256
            originalBytesBase64 = [Convert]::ToBase64String($original.bytes)
            assessmentPath = $receipt.assessmentPath; assessmentSha256 = $assessment.sha256
            pointerPath = $receipt.pointerPath; pointerSha256 = $pointer.sha256
            projectionPath = $projectionPath
            projectionSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($projectionBytes))
            projectionDigest = Get-CoordinatorDigest $projection
        }
        $provenance = Open-TerminalRecoveryEvidence $proofPath $proof $handles -Existing:$proofRequired
        $null = Open-TerminalRecoveryEvidence $projectionPath $projection $handles -Existing:$consumed
        $null = Open-TerminalRecoveryEvidence $consumePath ([ordered]@{ receiptPath = $projectionPath }) $handles -Existing:$consumed
        # Only the existing locked/fenced transaction is allowed to release execution.
        $result = Invoke-Coordinator -RepositoryPath $RepositoryPath -Action Consume -Session $Session `
            -ExpectedEpoch $ExpectedEpoch -ExpectedRevision $ExpectedRevision -RequestPath $consumePath
        $result.terminalRecovery = @{ originalPath = $request.receiptPath; originalSha256 = $original.sha256
            projectionPath = $projectionPath; proofPath = $proofPath; proofSha256 = $provenance.sha256 }
        return $result
    } finally {
        foreach ($handle in $handles) { $handle.Dispose() }
        $recoveryLock.Dispose()
    }
}
