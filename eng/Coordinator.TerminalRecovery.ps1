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

function Get-RecoveryInputProvenance {
    param([string] $Path, $InputFile)
    return [ordered]@{ path = $Path; sha256 = $InputFile.sha256
        bytesBase64 = [Convert]::ToBase64String($InputFile.bytes) }
}

function Assert-LiteralNoHeldDecision {
    param($Value)
    if ($Value.heldDecision -isnot [bool] -or $Value.heldDecision -or
        $Value.finiteCommandsJoined -isnot [bool] -or !$Value.finiteCommandsJoined -or
        $Value.runningCommands -isnot [array] -or $Value.runningCommands.Count -ne 0) {
        throw 'Equivalent absence requires literal false, joined commands and an empty running array.'
    }
}

function Get-RepresentationProjection {
    param($Request, $Receipt, $State, $Owner, [string] $Directory, [string] $CompatibilityProfile, $Handles)
    $projection = [ordered]@{}
    foreach ($field in $Receipt.Keys) { $projection[$field] = $Receipt[$field] }
    $inputs = @()
    if ($CompatibilityProfile -ceq 'NoHeldDecisionV1') {
        Assert-LiteralNoHeldDecision $Receipt
        $delivery = Open-PublishedWireInput $Request.deliveryProofPath $Directory $Request.deliveryProofSha256 $Handles
        $stop = Open-PublishedWireInput $Request.stopProofPath $Directory $Request.stopProofSha256 $Handles
        $facts = $delivery.value
        $stopped = $stop.value
        Assert-CoordinatorFields $facts @('version', 'datasetId', 'ownerId', 'executionId', 'nativeIdentity', 'publisher',
            'repository', 'branch', 'worktree', 'head', 'tree', 'prUrl', 'sourceAndBehaviorProof', 'binaryHashes',
            'terminalReceipt', 'deliveryCommands', 'comparisonCleanup', 'finiteCommandsJoined', 'runningCommands',
            'heldDecision', 'terminalPublication', 'remaining')
        Assert-CoordinatorFields $stopped @('version', 'datasetId', 'ownerId', 'executionId', 'kind', 'nativeIdentity',
            'publisher', 'publicationPolicy', 'implementation', 'prSourceValidation', 'comparisonOnly',
            'preservedFailures', 'unrun', 'toolchain', 'finiteCommandsJoined', 'runningCommands', 'heldDecision', 'next')
        Assert-CoordinatorFields $stopped.implementation @('worktree', 'branch', 'base', 'head', 'tree', 'files',
            'signature', 'call', 'placement', 'semantics', 'documentation', 'sourceBlobManifest', 'declarationSha256', 'pr544Inspection')
        foreach ($value in @($facts, $stopped)) {
            Assert-LiteralNoHeldDecision $value
            Assert-CoordinatorNumber $value.version
            foreach ($field in @('datasetId', 'ownerId', 'executionId', 'nativeIdentity')) {
                if ($value[$field] -isnot [string] -or $value[$field] -cne $Receipt[$field]) {
                    throw "Equivalent absence proof binding mismatch: $field"
                }
            }
            if ($value.version -cne 1 -or $value.publisher -cne $State.publisher) {
                throw 'Equivalent absence proof authority mismatch.'
            }
        }
        foreach ($field in @('branch', 'worktree', 'head', 'prUrl')) {
            if ($facts[$field] -isnot [string] -or $facts[$field] -cne $Receipt[$field]) {
                throw "Equivalent absence delivery source mismatch: $field"
            }
        }
        foreach ($field in @('branch', 'worktree', 'head')) {
            if ($stopped.implementation[$field] -isnot [string] -or $stopped.implementation[$field] -cne $Receipt[$field]) {
                throw "Equivalent absence stopped source mismatch: $field"
            }
        }
        if ($facts.repository -cne 'github.com/roryprimrose/Kora' -or
            $stopped.publicationPolicy -cne $Receipt.publicationPolicy -or $stopped.kind -cne 'prerequisite-ready' -or
            $facts.tree -isnot [string] -or $facts.tree -cnotmatch '\A[0-9a-f]{40}\z' -or
            $stopped.implementation.tree -cne $facts.tree -or
            $facts.sourceAndBehaviorProof -cne (Split-Path -Leaf $Request.stopProofPath) -or
            $facts.terminalReceipt -cne (Split-Path -Leaf $Request.receiptPath) -or
            @($Request.receiptPath, $Request.readbackPath, $Request.deliveryProofPath, $Request.stopProofPath |
                Select-Object -Unique).Count -ne 4) {
            throw 'Equivalent absence requires distinct original source-bound delivery, stop and CLI proof.'
        }
        $projection.heldDecision = $null
        $published = Get-PublishedWireProjection $Request $projection $State $Owner $Directory UtcTimestampV1 $Handles
        $projection = $published.projection
        $inputs = @((Get-RecoveryInputProvenance $Request.deliveryProofPath $delivery),
            (Get-RecoveryInputProvenance $Request.stopProofPath $stop),
            (Get-RecoveryInputProvenance $Request.readbackPath $published.readback))
    } elseif ($CompatibilityProfile -ceq 'UtcRepresentationV1') {
        Assert-PublishedWireJoined $Receipt
        Assert-CoordinatorFields $Receipt.autoMerge @('method', 'enabledAt')
        if ($Receipt.kind -cne 'pr-enrolled-ci-wait' -or $Receipt.workState -cne 'stopped' -or
            $Receipt.autoMerge.method -isnot [string] -or $Receipt.autoMerge.method -cne 'SQUASH') {
            throw 'UTC representation profile changes only a strict stopped SQUASH publication timestamp.'
        }
        $projection.autoMerge = [ordered]@{ method = $Receipt.autoMerge.method
            enabledAt = ConvertFrom-PublishedWireUtc $Receipt.autoMerge.enabledAt }
    } else { throw 'Unknown representation profile.' }
    return @{ projection = $projection; inputs = $inputs
        derivation = [ordered]@{ originalAutoMerge = $Receipt.autoMerge; normalizedAutoMerge = $projection.autoMerge
            originalHeldDecision = $Receipt.heldDecision; normalizedHeldDecision = $projection.heldDecision } }
}

function Assert-OriginalDispatchSource {
    param($Owner, [string] $Head)
    if (![IO.Path]::IsPathFullyQualified($Owner.worktree) -or
        [IO.Path]::GetFullPath($Owner.worktree) -cne $Owner.worktree -or $Head -cnotmatch '\A[0-9a-f]{40}\z') {
        throw 'Original dispatch requires a canonical unchanged source.'
    }
    Assert-NoLinks $Owner.worktree -AncestorsOnly
    foreach ($check in @(
        @{ arguments = @('rev-parse', '--show-toplevel'); expected = $Owner.worktree.Replace('\', '/') },
        @{ arguments = @('remote', 'get-url', 'origin'); expected = 'https://github.com/roryprimrose/Kora.git' },
        @{ arguments = @('branch', '--show-current'); expected = $Owner.branch },
        @{ arguments = @('rev-parse', 'HEAD'); expected = $Head },
        @{ arguments = @('status', '--porcelain=v1', '--untracked-files=all'); expected = '' })) {
        $arguments = $check.arguments
        $actual = @(& git -C $Owner.worktree @arguments)
        if ($LASTEXITCODE -ne 0 -or ($actual -join "`n") -cne $check.expected) {
            throw "Original dispatch source changed or unavailable: $($arguments -join ' ')"
        }
    }
}

function Get-OriginalDispatchProjection {
    param($Request, $Receipt, $State, $Owner, [string] $Directory, [string] $CompatibilityProfile, [string] $Session,
        [string] $OriginalSha256, $Handles)
    $execution = @($State.executions | Where-Object { $_.id -ceq $Receipt.executionId -and $_.ownerId -ceq $Owner.id })
    if ($execution.Count -ne 1 -or $execution[0].phase -cnotin @('accepted', 'released') -or
        $execution[0].epoch -ne $State.epoch -or $execution[0].dispatcher -cne $Session -or
        ($State.Contains('legacy') -and $Receipt.executionId -cin @($State.legacy.executionIds | ForEach-Object id)) -or
        $Receipt.ownerId -cne $Owner.id -or $Receipt.datasetId -cne $State.datasetId -or
        $Session -cnotmatch '\Aahp-session:/([0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12})\z') {
        throw 'Recovery requires the current original dispatcher/epoch and existing accepted non-imported grant.'
    }
    $dispatcherId = $Matches[1]
    Assert-PublishedWireJoined $Receipt
    if ($Receipt.workState -cne 'stopped' -or $Receipt.version -cne 1 -or
        $Receipt.publicationPolicy -cne $State.publicationPolicy) { throw 'Original dispatch source is not stopped under existing authority.' }
    Assert-CoordinatorNumber $Receipt.version
    Assert-TerminalRecoveryPath $Request.hostLaunchProofPath $State.identity.directory
    $hostProof = Open-PublishedWireInput $Request.hostLaunchProofPath $State.identity.directory $Request.hostLaunchProofSha256 $Handles
    $hostFacts = $hostProof.value
    Assert-CoordinatorFields $hostFacts @('version', 'datasetId', 'coordinator', 'source', 'sourceBytesObserved', 'observedUtc', 'launches')
    Assert-CoordinatorNumber $hostFacts.version
    Assert-CoordinatorNumber $hostFacts.sourceBytesObserved
    $observed = ConvertFrom-PublishedWireUtc $hostFacts.observedUtc
    if ($hostFacts.version -cne 1 -or $hostFacts.datasetId -cne $State.datasetId -or $hostFacts.coordinator -cne $Session -or
        $hostFacts.source -cne 'Original host-managed current-dispatcher SDK events; exact paired native create_session start/completion; no title or shared-parent inference' -or
        $hostFacts.sourceBytesObserved -eq 0 -or $hostFacts.launches -isnot [array] -or
        $hostFacts.launches.Count -lt 1 -or $hostFacts.launches.Count -gt 16) { throw 'Invalid original host launch proof envelope.' }
    $unique = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $matching = @()
    foreach ($launch in $hostFacts.launches) {
        Assert-CoordinatorFields $launch @('ownerId', 'executionId', 'dispatcher', 'toolName', 'toolCallId',
            'startEventId', 'completionEventId', 'startedUtc', 'completedUtc', 'nativeIdentity',
            'originalCallSucceeded', 'originalDispatcherCallJoined', 'initialHeaderSha256')
        Assert-PublishedWireStringField $launch @('ownerId', 'executionId', 'dispatcher', 'toolName', 'toolCallId',
            'startEventId', 'completionEventId', 'startedUtc', 'completedUtc', 'nativeIdentity', 'initialHeaderSha256')
        foreach ($field in @('ownerId', 'executionId', 'toolCallId', 'startEventId', 'completionEventId', 'nativeIdentity', 'initialHeaderSha256')) {
            # Start/completion IDs share a namespace; aliases across either are ambiguous too.
            $space = if ($field -cin @('startEventId', 'completionEventId')) { 'event' } else { $field }
            if (!$unique.Add("$space/$($launch[$field])")) { throw 'Ambiguous original host launch proof.' }
        }
        $registered = Get-CoordinatorOwner $State $launch.ownerId
        $grant = @($State.executions | Where-Object { $_.id -ceq $launch.executionId -and $_.ownerId -ceq $launch.ownerId })
        if ($grant.Count -ne 1 -or $grant[0].epoch -ne $State.epoch -or $grant[0].dispatcher -cne $Session -or
            $grant[0].phase -cnotin @('accepted', 'released') -or $launch.dispatcher -cne $Session -or
            ($State.Contains('legacy') -and $launch.executionId -cin @($State.legacy.executionIds | ForEach-Object id)) -or
            $launch.toolName -cne 'create_session' -or $launch.toolCallId -cnotmatch '\Acall_[A-Za-z0-9]+\z' -or
            $launch.startEventId -cnotmatch '\A[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\z' -or
            $launch.completionEventId -cnotmatch '\A[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\z' -or
            $launch.nativeIdentity -cnotmatch "\Aagent-host-session://ahp-session/$dispatcherId\?chat=[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\z" -or
            $launch.nativeIdentity -cne $registered.nativeIdentity -or
            $launch.initialHeaderSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
            $launch.originalCallSucceeded -isnot [bool] -or !$launch.originalCallSucceeded -or
            $launch.originalDispatcherCallJoined -isnot [bool] -or !$launch.originalDispatcherCallJoined) {
            throw 'Original host launch proof does not match the successful joined canonical grant.'
        }
        $start = ConvertFrom-PublishedWireUtc $launch.startedUtc
        $end = ConvertFrom-PublishedWireUtc $launch.completedUtc
        if ((ConvertTo-CoordinatorTime $start) -gt (ConvertTo-CoordinatorTime $end) -or
            (ConvertTo-CoordinatorTime $end) -gt (ConvertTo-CoordinatorTime $observed)) {
            throw 'Original host launch proof chronology mismatch.'
        }
        if ($launch.executionId -ceq $Receipt.executionId -and $launch.ownerId -ceq $Owner.id) { $matching += $launch }
    }
    if ($matching.Count -ne 1) { throw 'Exactly one original host launch is required.' }
    if ($CompatibilityProfile -ceq 'MissingNativeEvidenceV1') {
        Assert-CoordinatorFields $Receipt @('version', 'datasetId', 'eventId', 'ownerId', 'executionId', 'revision',
            'nativeIdentity', 'branch', 'worktree', 'head', 'kind', 'workState', 'finiteCommandsJoined',
            'runningCommands', 'heldDecision', 'publicationPolicy', 'prUrl', 'autoMerge')
        if ($null -ne $Receipt.nativeIdentity -or $Owner.kind -cne 'evidence' -or $Receipt.kind -cne 'evidence-completed' -or
            $null -ne $Receipt.prUrl -or $null -ne $Receipt.autoMerge -or $Receipt.branch -cne $Owner.branch -or
            $Receipt.worktree -cne $Owner.worktree -or $Receipt.head -cne $Request.sourceHead) {
            throw 'Only an exact missing-native completed evidence source is supported.'
        }
        $projection = [ordered]@{}
        foreach ($field in $Receipt.Keys) { $projection[$field] = $Receipt[$field] }
        $projection.nativeIdentity = $matching[0].nativeIdentity
    } elseif ($CompatibilityProfile -ceq 'MissingLaunchBlockerV1') {
        Assert-CoordinatorFields $Receipt @('version', 'datasetId', 'ownerId', 'executionId', 'observedAt', 'receiptKind',
            'requiredLaunchPath', 'observed', 'boundary', 'publisher', 'repository', 'publicationPolicy', 'preflight',
            'effects', 'validation', 'workState', 'finiteCommandsJoined', 'runningCommands', 'heldDecision', 'note')
        Assert-CoordinatorFields $Receipt.preflight @('initialWorktree', 'initialBranch', 'initialHead', 'freshFetchedMain',
            'initialWorkingTreeClean', 'repositoryPushPermission', 'liveQueueMode', 'mainEnforceAdmins', 'requiredCheckPublisherAppId', 'scratchIgnoreVerified')
        Assert-CoordinatorFields $Receipt.effects @('sourceFilesChanged', 'uniqueWorktreeCreated', 'bindingPublished',
            'strictTerminalPublished', 'commitCreated', 'prCreated', 'autoMergeEnrolled')
        foreach ($effect in $Receipt.effects.Values) {
            if ($effect -isnot [bool] -or $effect) { throw 'Missing-launch blocker claims changed or unknown work.' }
        }
        $null = ConvertTo-CoordinatorTime $Receipt.observedAt
        if ($Owner.kind -cne 'feature' -or $Receipt.receiptKind -cne 'missing-launch-mapping' -or
            $Receipt.publisher -cne $State.publisher -or $Receipt.repository -cne 'github.com/roryprimrose/Kora' -or
            $Receipt.preflight.initialWorkingTreeClean -isnot [bool] -or !$Receipt.preflight.initialWorkingTreeClean -or
            $Receipt.preflight.initialBranch -cne $Owner.branch -or $Receipt.preflight.initialWorktree -cne $Owner.worktree -or
            $Receipt.preflight.initialHead -cne $Request.sourceHead -or
            $Receipt.requiredLaunchPath -cne (Join-Path $Directory "launch-$($Receipt.executionId).json")) {
            throw 'Missing-launch blocker original source/authority mismatch.'
        }
        $eventId = 'helper-original-dispatch-' + (Get-CoordinatorDigest @{
            datasetId = $State.datasetId; authorityId = $State.authorityId; executionId = $Receipt.executionId
            ownerId = $Owner.id; epoch = $execution[0].epoch; dispatcher = $execution[0].dispatcher
            nativeIdentity = $matching[0].nativeIdentity; branch = $Owner.branch; worktree = $Owner.worktree
            sourceHead = $Request.sourceHead; originalSha256 = $OriginalSha256 }).ToLowerInvariant()
        $projection = [ordered]@{ version = 1; datasetId = $Receipt.datasetId; eventId = $eventId
            ownerId = $Receipt.ownerId; executionId = $Receipt.executionId; revision = 1
            nativeIdentity = $matching[0].nativeIdentity; branch = $Owner.branch; worktree = $Owner.worktree
            head = $Request.sourceHead; kind = 'blocked-quiescent'; workState = $Receipt.workState
            finiteCommandsJoined = $Receipt.finiteCommandsJoined; runningCommands = $Receipt.runningCommands
            heldDecision = $Receipt.heldDecision; publicationPolicy = $Receipt.publicationPolicy; prUrl = $null; autoMerge = $null }
    } else { throw 'Unknown current original-dispatch profile.' }
    Assert-OriginalDispatchSource $Owner $Request.sourceHead
    return @{ projection = $projection; inputs = @((Get-RecoveryInputProvenance $Request.hostLaunchProofPath $hostProof))
        derivation = [ordered]@{ epoch = $execution[0].epoch; dispatcher = $execution[0].dispatcher
            ownerId = $Owner.id; executionId = $execution[0].id; branch = $Owner.branch; worktree = $Owner.worktree
            sourceHead = $Request.sourceHead; launch = $matching[0]
            nativeIdentitySource = 'Exact successful joined original host creation link, already accepted in canonical binding'
            eventIdentitySource = if ($CompatibilityProfile -ceq 'MissingLaunchBlockerV1') { 'Helper event/revision1 bound to original raw SHA256 and grant, not an actor-authored terminal' } else { 'Original event/revision unchanged' } } }
}

function Invoke-CoordinatorTerminalRecovery {
    param([string] $RepositoryPath, [string] $Session, [long] $ExpectedEpoch,
        [long] $ExpectedRevision, [string] $RequestPath, [string] $PublishedWireProfile, [string] $OriginalDispatchProfile)
    $request = Read-CoordinatorJson $RequestPath
    $derivedProfile = $OriginalDispatchProfile
    if ($PublishedWireProfile -cin @('UtcRepresentationV1', 'NoHeldDecisionV1')) { $derivedProfile = $PublishedWireProfile }
    if ($derivedProfile) {
        $fields = @('receiptPath', 'receiptSha256')
        if ($derivedProfile -ceq 'NoHeldDecisionV1') {
            $fields += @('readbackPath', 'readbackSha256', 'publisher',
                'deliveryProofPath', 'deliveryProofSha256', 'stopProofPath', 'stopProofSha256')
        } elseif ($OriginalDispatchProfile) { $fields += @('hostLaunchProofPath', 'hostLaunchProofSha256', 'sourceHead') }
        Assert-CoordinatorFields $request $fields
        Assert-PublishedWireStringField $request $fields
    } elseif ($PublishedWireProfile) {
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
        if ($PublishedWireProfile -or $OriginalDispatchProfile) {
            Assert-TerminalRecoveryPath $RequestPath (Split-Path -Parent $RequestPath)
            $pinnedRequest = Open-TerminalRecoveryInput $RequestPath 32768 $handles
            $document = [System.Text.Json.JsonDocument]::Parse([Text.UTF8Encoding]::new($false, $true).GetString($pinnedRequest.bytes))
            try { Assert-PublishedWireJson $document.RootElement } finally { $document.Dispose() }
            if ((Get-CoordinatorDigest $pinnedRequest.value) -cne (Get-CoordinatorDigest $request)) { throw 'Published wire request changed.' }
            $original = Open-PublishedWireInput $request.receiptPath $ownerDirectory $request.receiptSha256 $handles
            $receipt = $original.value
            if ($derivedProfile) {
                if ($OriginalDispatchProfile) {
                    $derived = Get-OriginalDispatchProjection $request $receipt $state $owner $ownerDirectory `
                        $OriginalDispatchProfile $Session $original.sha256 $handles
                } else {
                    Assert-CoordinatorFields $receipt $terminalFields
                    $derived = Get-RepresentationProjection $request $receipt $state $owner $ownerDirectory $derivedProfile $handles
                }
                $projection = $derived.projection
            } else {
                Assert-CoordinatorFields $receipt $terminalFields
                $published = Get-PublishedWireProjection $request $receipt $state $owner $ownerDirectory $PublishedWireProfile $handles
                $projection = $published.projection
            }
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
        $eventKey = Get-CoordinatorEventKey $projection.eventId
        $consumed = @($state.events | Where-Object id -CEQ $eventKey).Count -gt 0
        $proofRequired = $consumed -or (Test-Path -LiteralPath $projectionPath) -or (Test-Path -LiteralPath $consumePath)
        $projectionBytes = [Text.UTF8Encoding]::new($false).GetBytes(($projection | ConvertTo-Json -Depth 40 -Compress))
        $proof = if ($derivedProfile) { [ordered]@{
                version = 1; producer = 'Coordinator.TerminalRecovery'; profile = $derivedProfile
                datasetId = $state.datasetId; authorityId = $state.authorityId
                originalPath = $request.receiptPath; originalSha256 = $original.sha256
                originalBytesBase64 = [Convert]::ToBase64String($original.bytes)
                request = ConvertTo-CoordinatorCanonicalValue $request
                supportingInputs = $derived.inputs
                derivation = $derived.derivation
                semantics = 'Helper-owned projection, not a new original-actor claim. Only the recorded closed mapping is derived; strict fenced core consumption remains authoritative.'
                projectionPath = $projectionPath
                projectionSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($projectionBytes))
                projectionDigest = Get-CoordinatorDigest $projection
            }} elseif ($PublishedWireProfile) { [ordered]@{
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
        if ($OriginalDispatchProfile) { Assert-OriginalDispatchSource $owner $request.sourceHead }
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
