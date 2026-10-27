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

function Assert-PublishedWireJson {
    param([System.Text.Json.JsonElement] $Element)
    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($property in $Element.EnumerateObject()) {
            if (!$names.Add($property.Name)) { throw 'Duplicate published wire field.' }
            Assert-PublishedWireJson $property.Value
        }
    } elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Element.EnumerateArray()) { Assert-PublishedWireJson $item }
    }
}

function Open-PublishedWireInput {
    param([string] $Path, [string] $Directory, $Sha256, $Handles)
    Assert-TerminalRecoveryPath $Path $Directory
    if ($Sha256 -isnot [string] -or $Sha256 -cnotmatch '\A[0-9a-fA-F]{64}\z') { throw 'Expected a raw SHA256 pin.' }
    $inputFile = Open-TerminalRecoveryInput $Path 1048576 $Handles
    if (![string]::Equals($inputFile.sha256, $Sha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Published wire raw SHA256 mismatch.'
    }
    $document = [System.Text.Json.JsonDocument]::Parse([Text.UTF8Encoding]::new($false, $true).GetString($inputFile.bytes))
    try { Assert-PublishedWireJson $document.RootElement } finally { $document.Dispose() }
    return $inputFile
}

function ConvertFrom-PublishedWireUtc {
    param($Value)
    if ($Value -isnot [string] -or $Value -cnotmatch '\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.([0-9]{1,7}))?Z\z') {
        throw 'Published wire timestamp requires explicit UTC Z with zero to seven fractional digits.'
    }
    $format = "yyyy-MM-dd'T'HH:mm:ss"
    if ($Matches[1]) { $format += '.' + ('f' * $Matches[1].Length) }
    try {
        $time = [DateTimeOffset]::ParseExact($Value, ($format + "'Z'"),
            [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal)
    } catch [FormatException] { throw 'Published wire timestamp is not a valid Gregorian UTC date/time.' }
    return $time.ToUniversalTime().ToString('o')
}

function Assert-PublishedWireJoined {
    param($Value)
    if ($Value.finiteCommandsJoined -isnot [bool] -or !$Value.finiteCommandsJoined -or
        $Value.runningCommands -isnot [array] -or $Value.runningCommands.Count -ne 0 -or
        $null -ne $Value.heldDecision) { throw 'Published wire execution is not explicitly joined/quiescent.' }
}

function Assert-PublishedWireStringField {
    param($Value, [string[]] $Fields)
    foreach ($field in $Fields) {
        if ($Value[$field] -isnot [string]) { throw "Published wire field must be a string: $field" }
    }
}

function Get-PublishedWireProjection {
    param($Request, $Receipt, $State, $Owner, [string] $Directory, [string] $WireProfile, $Handles)
    if ($WireProfile -cnotin @('UtcTimestampV1', 'HostedDeliveryV1')) { throw 'Unknown published wire profile.' }
    Assert-PublishedWireStringField $Receipt @('datasetId', 'eventId', 'ownerId', 'executionId', 'nativeIdentity',
        'branch', 'worktree', 'head', 'kind', 'workState', 'publicationPolicy', 'prUrl')
    if ($State.publicationPolicy -cne 'ready-auto' -or $Receipt.publicationPolicy -cne 'ready-auto' -or
        $Receipt.kind -cne 'pr-enrolled-ci-wait' -or $Request.publisher -cne $State.publisher -or
        $Receipt.prUrl -isnot [string] -or
        $Receipt.prUrl -cnotmatch '\Ahttps://github.com/roryprimrose/Kora/pull/([1-9][0-9]*)\z') {
        throw 'Published wire requires existing ready-auto publication and pinned publisher.'
    }
    $number = $Matches[1]
    Assert-PublishedWireJoined $Receipt
    $methodKey = if ($WireProfile -ceq 'HostedDeliveryV1') { 'mergeMethod' } else { 'method' }
    $workState = if ($WireProfile -ceq 'HostedDeliveryV1') { 'ci-wait' } else { 'stopped' }
    Assert-CoordinatorFields $Receipt.autoMerge @($methodKey, 'enabledAt')
    Assert-PublishedWireStringField $Receipt.autoMerge @($methodKey, 'enabledAt')
    if ($Receipt.autoMerge[$methodKey] -cne 'SQUASH' -or $Receipt.workState -cne $workState) {
        throw 'Published wire method/workState does not match the selected closed profile.'
    }
    $enabledAt = ConvertFrom-PublishedWireUtc $Receipt.autoMerge.enabledAt
    $execution = @($State.executions | Where-Object { $_.id -ceq $Receipt.executionId -and $_.ownerId -ceq $Owner.id })
    if ($execution.Count -ne 1 -or $execution[0].phase -cnotin @('accepted', 'released') -or
        $Receipt.ownerId -cne $Owner.id) { throw 'Published wire admitted owner/execution mismatch.' }
    $readback = Open-PublishedWireInput $Request.readbackPath $Directory $Request.readbackSha256 $Handles
    $value = $readback.value
    Assert-CoordinatorFields $value @('url', 'number', 'headRefOid', 'headRefName', 'baseRefName', 'state', 'isDraft', 'author', 'autoMergeRequest') `
    @('mergeCommit', 'mergeStateStatus', 'reviewDecision', 'statusCheckRollup', 'headRepository', 'isCrossRepository')
    Assert-CoordinatorFields $value.author @('login') @('id', 'is_bot', 'name')
    Assert-CoordinatorFields $value.autoMergeRequest @('mergeMethod', 'enabledAt', 'enabledBy') @('authorEmail', 'commitBody', 'commitHeadline')
    Assert-CoordinatorFields $value.autoMergeRequest.enabledBy @('login') @('id', 'is_bot', 'name')
    Assert-PublishedWireStringField $value @('url', 'headRefOid', 'headRefName', 'baseRefName', 'state')
    Assert-PublishedWireStringField $value.author @('login')
    Assert-PublishedWireStringField $value.autoMergeRequest @('mergeMethod', 'enabledAt')
    Assert-PublishedWireStringField $value.autoMergeRequest.enabledBy @('login')
    Assert-CoordinatorNumber $value.number
    if ($value.url -cne $Receipt.prUrl -or [string]$value.number -cne $number -or
        $value.headRefOid -cne $Receipt.head -or $value.headRefName -cne $Receipt.branch -or
        $value.baseRefName -cne 'main' -or $value.state -cne 'OPEN' -or
        $value.isDraft -isnot [bool] -or $value.isDraft -or $value.author.login -cne $State.publisher -or
        $value.autoMergeRequest.enabledBy.login -cne $State.publisher -or
        $value.autoMergeRequest.mergeMethod -cne 'SQUASH' -or
        (ConvertFrom-PublishedWireUtc $value.autoMergeRequest.enabledAt) -cne $enabledAt) {
        throw 'Published wire original readback identity/enrollment mismatch.'
    }
    if ($value.Contains('isCrossRepository') -and ($value.isCrossRepository -isnot [bool] -or $value.isCrossRepository)) {
        throw 'Published wire readback must be same-repository.'
    }
    if ($value.Contains('headRepository')) {
        Assert-CoordinatorFields $value.headRepository @('nameWithOwner') @('id', 'name')
        Assert-PublishedWireStringField $value.headRepository @('nameWithOwner')
        if ($value.headRepository.nameWithOwner -cne 'roryprimrose/Kora') { throw 'Published wire repository mismatch.' }
    }
    if ($value.Contains('mergeCommit') -and $null -ne $value.mergeCommit) {
        Assert-CoordinatorFields $value.mergeCommit @('oid')
        Assert-PublishedWireStringField $value.mergeCommit @('oid')
    }
    if ($value.Contains('statusCheckRollup')) {
        if ($value.statusCheckRollup -isnot [array]) { throw 'Published wire check metadata must be an array.' }
        foreach ($check in $value.statusCheckRollup) {
            Assert-CoordinatorFields $check @('__typename', 'completedAt', 'conclusion', 'detailsUrl', 'name', 'startedAt', 'status', 'workflowName')
            Assert-PublishedWireStringField $check @('__typename', 'name', 'status')
            if ($check.__typename -cne 'CheckRun') { throw 'Unknown published wire check metadata profile.' }
        }
    }
    $publication = $null
    if ($WireProfile -ceq 'HostedDeliveryV1') {
        $publication = Open-PublishedWireInput $Request.publicationProofPath $Directory $Request.publicationProofSha256 $Handles
        $facts = $publication.value
        Assert-CoordinatorFields $facts @('version', 'timestamp', 'publisher', 'repository', 'ownerId', 'executionId',
            'nativeIdentity', 'branch', 'worktree', 'testedHead', 'currentRemoteHead', 'prUrl', 'state', 'mergeStateStatus',
            'reviewDecision', 'autoMerge', 'enrollmentCommand', 'enrollmentResult', 'currentRequiredAppChecks',
            'checkMeaning', 'implementationProof', 'prReadback', 'checkReadback', 'finiteCommandsJoined', 'runningCommands', 'heldDecision')
        Assert-CoordinatorFields $facts.autoMerge @('mergeMethod', 'enabledAt')
        Assert-CoordinatorNumber $facts.version
        Assert-PublishedWireStringField $facts @('timestamp', 'publisher', 'repository', 'ownerId', 'executionId',
            'nativeIdentity', 'branch', 'worktree', 'testedHead', 'currentRemoteHead', 'prUrl', 'state', 'mergeStateStatus',
            'reviewDecision', 'enrollmentCommand', 'enrollmentResult', 'checkMeaning', 'implementationProof', 'prReadback', 'checkReadback')
        Assert-PublishedWireStringField $facts.autoMerge @('mergeMethod', 'enabledAt')
        $null = ConvertTo-CoordinatorTime $facts.timestamp
        if ($facts.currentRequiredAppChecks -isnot [array]) { throw 'Published wire supplemental check metadata must be an array.' }
        Assert-PublishedWireJoined $facts
        foreach ($field in @('ownerId', 'executionId', 'nativeIdentity', 'branch', 'worktree', 'prUrl')) {
            if ($facts[$field] -isnot [string] -or $facts[$field] -cne $Receipt[$field]) {
                throw "Published wire supplemental source mismatch: $field"
            }
        }
        if ($facts.version -cne 1 -or $facts.publisher -cne $State.publisher -or
            $facts.repository -cne 'github.com/roryprimrose/Kora' -or
            $facts.testedHead -cne $Receipt.head -or $facts.currentRemoteHead -cne $Receipt.head -or
            $facts.state -cne 'OPEN' -or $facts.autoMerge.mergeMethod -cne 'SQUASH' -or
            (ConvertFrom-PublishedWireUtc $facts.autoMerge.enabledAt) -cne $enabledAt -or
            $facts.prReadback -cne (Split-Path -Leaf $Request.readbackPath) -or
            $facts.enrollmentCommand -cne "gh pr merge $number --repo github.com/roryprimrose/Kora --auto --squash --match-head-commit $($Receipt.head)" -or
            $facts.enrollmentResult -cne 'passed and read back') {
            throw 'Published wire supplemental protected publication mismatch.'
        }
    }
    $projection = [ordered]@{}
    foreach ($field in $Receipt.Keys) { $projection[$field] = $Receipt[$field] }
    $projection.autoMerge = [ordered]@{ method = $Receipt.autoMerge[$methodKey]; enabledAt = $enabledAt }
    if ($WireProfile -ceq 'HostedDeliveryV1') { $projection.workState = 'stopped' }
    return @{ projection = $projection; readback = $readback; publication = $publication }
}

function Invoke-CoordinatorTerminalRecovery {
    param([string] $RepositoryPath, [string] $Session, [long] $ExpectedEpoch,
        [long] $ExpectedRevision, [string] $RequestPath, [string] $PublishedWireProfile)
    $request = Read-CoordinatorJson $RequestPath
    if ($PublishedWireProfile) {
        $fields = @('receiptPath', 'receiptSha256', 'readbackPath', 'readbackSha256', 'publisher')
        if ($PublishedWireProfile -ceq 'HostedDeliveryV1') { $fields += @('publicationProofPath', 'publicationProofSha256') }
        Assert-CoordinatorFields $request $fields
        Assert-PublishedWireStringField $request $fields
    } else { Assert-CoordinatorFields $request @('receiptPath') }
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
        if ($PublishedWireProfile) {
            Assert-TerminalRecoveryPath $RequestPath (Split-Path -Parent $RequestPath)
            $pinnedRequest = Open-TerminalRecoveryInput $RequestPath 32768 $handles
            $document = [System.Text.Json.JsonDocument]::Parse([Text.UTF8Encoding]::new($false, $true).GetString($pinnedRequest.bytes))
            try { Assert-PublishedWireJson $document.RootElement } finally { $document.Dispose() }
            if ((Get-CoordinatorDigest $pinnedRequest.value) -cne (Get-CoordinatorDigest $request)) { throw 'Published wire request changed.' }
            $original = Open-PublishedWireInput $request.receiptPath $ownerDirectory $request.receiptSha256 $handles
            $receipt = $original.value
            Assert-CoordinatorFields $receipt $terminalFields
            $published = Get-PublishedWireProjection $request $receipt $state $owner $ownerDirectory $PublishedWireProfile $handles
            $projection = $published.projection
        } else {
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
        }
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
        $proof = if ($PublishedWireProfile) { [ordered]@{
                version = 1; producer = 'Coordinator.TerminalRecovery'; profile = "published-wire-$PublishedWireProfile"
                datasetId = $state.datasetId; authorityId = $state.authorityId
                originalPath = $request.receiptPath; originalSha256 = $original.sha256
                originalBytesBase64 = [Convert]::ToBase64String($original.bytes)
                request = ConvertTo-CoordinatorCanonicalValue $request
                readbackPath = $request.readbackPath; readbackSha256 = $published.readback.sha256
                readbackBytesBase64 = [Convert]::ToBase64String($published.readback.bytes)
                publicationProofSha256 = if ($null -ne $published.publication) { $published.publication.sha256 } else { $null }
                publicationProofBytesBase64 = if ($null -ne $published.publication) { [Convert]::ToBase64String($published.publication.bytes) } else { $null }
                semantics = 'Helper-owned same-instant wire projection; hosted label is local quiescence only. Archived publication is not current candidate success.'
                originalValues = [ordered]@{ workState = $receipt.workState; autoMerge = $receipt.autoMerge }
                normalizedValues = [ordered]@{ workState = $projection.workState; autoMerge = $projection.autoMerge }
                projectionPath = $projectionPath
                projectionSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($projectionBytes))
                projectionDigest = Get-CoordinatorDigest $projection
            }} else { [ordered]@{
                version = 1; producer = 'Coordinator.TerminalRecovery'; profile = 'schema1-assessment-terminal-v1'
                datasetId = $state.datasetId; authorityId = $state.authorityId
                originalPath = $request.receiptPath; originalSha256 = $original.sha256
                originalBytesBase64 = [Convert]::ToBase64String($original.bytes)
                assessmentPath = $receipt.assessmentPath; assessmentSha256 = $assessment.sha256
                pointerPath = $receipt.pointerPath; pointerSha256 = $pointer.sha256
                projectionPath = $projectionPath
                projectionSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($projectionBytes))
                projectionDigest = Get-CoordinatorDigest $projection
            }}
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
