<#
.SYNOPSIS
Fences release mutations against live durable queue state and the completed batch tip.
.NOTES
The workflow profile environment value is only a snapshot. All gates read durable queue
state even when that snapshot is absent. V2 batch state cannot be bypassed by a stale job.
A changed durable profile requires a fresh run. Batch publication also authenticates the
static release-batch job from CI source at the exact protected main commit.
Each state snapshot resolves the queue ref to an exact commit and tree, verifies the file
blob against that tree and its bytes, then checks that the queue ref has not changed.
Verified ref movement restarts the pinned read, at most six attempts without sleeps.
The caller credential needs Actions and contents read access, not Variables API permission.
The static concurrency contract requires queue: max so controller events cannot replace
a pending release while cancel-in-progress remains false.
#>
function Resolve-ReleaseQueueProfile {
    param([AllowNull()][string] $Value)
    if ($Value -cin @('', 'singleton-v1') -or $null -eq $Value) { return 'singleton-v1' }
    if ($Value -ceq 'batch-v1') { return 'batch-v1' }
    throw 'Unknown release merge queue profile.'
}

function Get-ReleaseRepositoryText {
    param([string] $Path, [string] $Revision, [string] $ExpectedBlob)
    if ($Revision -cnotmatch '^[a-f0-9]{40}$') { throw 'Release safety files require an exact commit revision.' }
    $record = Get-GitHubRecord "repos/$script:KoraRepository/contents/${Path}?ref=$([Uri]::EscapeDataString($Revision))"
    if ($null -eq $record -or $record.type -isnot [string] -or $record.type -cne 'file' -or
        $record.path -isnot [string] -or $record.path -cne $Path -or
        $record.encoding -isnot [string] -or $record.encoding -cne 'base64' -or
        $record.content -isnot [string] -or ($record.size -isnot [long] -and $record.size -isnot [int]) -or
        $record.size -le 0 -or $record.size -gt 1048576 -or $record.sha -isnot [string] -or
        $record.sha -cnotmatch '^[a-f0-9]{40}$') {
        throw "Cannot establish release safety file: $Path."
    }
    $bytes = [Convert]::FromBase64String($record.content)
    if ($bytes.Length -ne $record.size) { throw "Incomplete release safety file: $Path." }
    $header = [Text.Encoding]::ASCII.GetBytes("blob $($bytes.Length)`0")
    $blob = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA1)
    try {
        $blob.AppendData($header)
        $blob.AppendData($bytes)
        $actualBlob = [Convert]::ToHexString($blob.GetHashAndReset()).ToLowerInvariant()
    } finally { $blob.Dispose() }
    if ($actualBlob -cne $record.sha -or ($ExpectedBlob -and $record.sha -cne $ExpectedBlob)) {
        throw "Release safety file bytes/blob differ from the pinned identity: $Path."
    }
    return [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
}

function Get-ReleaseQueueSnapshot {
    param([switch] $WithIdentity)
    $refPath = "repos/$script:KoraRepository/git/ref/heads/automation/merge-queue-state"
    $reference = Get-GitHubRecord $refPath
    if ($null -eq $reference) { return $null }
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        if ($reference.ref -isnot [string] -or $reference.ref -cne 'refs/heads/automation/merge-queue-state' -or
            $reference.object.type -isnot [string] -or $reference.object.type -cne 'commit' -or
            $reference.object.sha -isnot [string] -or $reference.object.sha -cnotmatch '^[a-f0-9]{40}$') {
            throw 'Invalid release queue reference identity.'
        }
        $revision = $reference.object.sha
        $commit = Get-GitHubRecord "repos/$script:KoraRepository/git/commits/$revision"
        if ($null -eq $commit -or $commit.sha -isnot [string] -or $commit.sha -cne $revision -or
            $commit.tree.sha -isnot [string] -or $commit.tree.sha -cnotmatch '^[a-f0-9]{40}$') {
            throw 'Release queue commit differs from the resolved reference.'
        }
        $tree = Get-GitHubRecord "repos/$script:KoraRepository/git/trees/$($commit.tree.sha)"
        if ($null -eq $tree -or $tree.sha -isnot [string] -or $tree.sha -cne $commit.tree.sha -or $tree.truncated -isnot [bool] -or
            $tree.truncated -or $tree.tree -isnot [array]) {
            throw 'Cannot establish the complete pinned release queue tree.'
        }
        $files = @($tree.tree | Where-Object path -CEQ 'queue.json')
        if ($files.Count -ne 1 -or $files[0].type -isnot [string] -or $files[0].type -cne 'blob' -or
            $files[0].mode -isnot [string] -or $files[0].mode -cne '100644' -or
            $files[0].sha -isnot [string] -or $files[0].sha -cnotmatch '^[a-f0-9]{40}$') {
            throw 'Pinned release queue tree requires one regular queue.json blob.'
        }
        $text = Get-ReleaseRepositoryText 'queue.json' $revision $files[0].sha
        $current = Get-GitHubRecord $refPath
        if ($null -eq $current -or $current.ref -isnot [string] -or $current.ref -cne $reference.ref -or
            $current.object.type -isnot [string] -or $current.object.type -cne 'commit' -or
            $current.object.sha -isnot [string] -or $current.object.sha -cnotmatch '^[a-f0-9]{40}$') {
            throw 'Invalid release queue reference during snapshot verification.'
        }
        if ($current.object.sha -ceq $revision) {
            if ($WithIdentity) { return @{ Text = $text; Revision = $revision } }
            return $text
        }
        $reference = $current
    }
    throw 'Release queue reference changed during snapshot verification after six attempts.'
}

function Assert-BatchReleaseWorkflow {
    param([string] $Text)
    # This is deliberately a restricted workflow shape, not a permissive YAML parser.
    # An unrecognised/ambiguous header cannot establish the static mutation lock.
    $lines = @($Text.Replace("`r`n", "`n") -split "`n")
    $roots = @{}
    $jobs = @{}
    $inJobs = $false
    foreach ($line in $lines) {
        if ($line -match "`t") { throw 'Batch release workflow contains unsupported indentation.' }
        if ($line -match '^\S' -and $line -notmatch '^#') {
            if ($line -cnotmatch '^([a-z][a-z-]*):(?: .*)?$' -or $roots.ContainsKey($Matches[1])) {
                throw 'Batch release workflow has ambiguous root keys.'
            }
            $roots[$Matches[1]] = $true
            $inJobs = $Matches[1] -ceq 'jobs'
        } elseif ($inJobs -and $line -match '^  \S' -and $line -notmatch '^  #') {
            if ($line -cnotmatch '^  ([a-z][a-z0-9-]*):$' -or $jobs.ContainsKey($Matches[1])) {
                throw 'Batch release workflow has ambiguous job keys.'
            }
            $jobs[$Matches[1]] = $true
        }
    }
    $header = [regex]::Match($Text.Replace("`r`n", "`n"),
        '(?ms)^  release-batch:\n(?<header>.*?)(?=^    steps:\n)')
    if (-not $jobs.ContainsKey('release-batch') -or -not $header.Success -or
        ([regex]::Matches($Text, '(?m)^    name: Publish completed batch release\r?$')).Count -ne 1) {
        throw 'Batch release workflow does not identify a unique trusted release job.'
    }
    $expected = @{
        'name' = 'Publish completed batch release'
        'runs-on' = 'ubuntu-latest'
        'timeout-minutes' = '15'
        'needs' = '[version, portable, windows-integration, installer]'
        'permissions' = ''
        'concurrency' = ''
    }
    $seen = @{}
    $section = ''
    foreach ($line in $header.Groups['header'].Value -split "`n") {
        if ($line -match '^\s*(?:#.*)?$') { continue }
        if ($line -cmatch '^    ([a-z][a-z-]*):(?: (.*))?$') {
            $key = $Matches[1]
            $value = if ($Matches.ContainsKey(2)) { $Matches[2] } else { '' }
            if ($seen.ContainsKey($key) -or ($key -cne 'if' -and
                (-not $expected.ContainsKey($key) -or $value -cne $expected[$key])) -or
                ($key -ceq 'if' -and [string]::IsNullOrWhiteSpace($value))) {
                throw 'Batch release workflow has an unsupported job header.'
            }
            $seen[$key] = $true
            $section = $key
        } elseif ($line -cmatch '^      ([a-z][a-z-]*): (.*)$') {
            $key = "$section.$($Matches[1])"
            $value = $Matches[2]
            $children = @{
                'permissions.contents' = 'write'; 'permissions.actions' = 'read'
                'concurrency.group' = 'merge-queue-controller'; 'concurrency.cancel-in-progress' = 'false'
                'concurrency.queue' = 'max'
            }
            if ($key -ceq 'concurrency.group' -and $value -ceq '${{ vars.KORA_MERGE_QUEUE_DELIVERY_PROFILE == ''independent-v1'' && format(''release-source-{0}'', github.sha) || ''merge-queue-controller'' }}') {
                $children[$key] = $value
            }
            if ($seen.ContainsKey($key) -or -not $children.ContainsKey($key) -or $value -cne $children[$key]) {
                throw 'Batch release workflow does not establish the static controller lock.'
            }
            $seen[$key] = $true
        } else { throw 'Batch release workflow contains an unsupported job header line.' }
    }
    foreach ($key in @($expected.Keys) + @('permissions.contents', 'permissions.actions',
        'concurrency.group', 'concurrency.cancel-in-progress', 'concurrency.queue')) {
        if (-not $seen.ContainsKey($key)) { throw "Batch release workflow is missing $key." }
    }
}

function Assert-SerializedBatchReleaseCaller {
    param([string] $Revision)
    if ($env:GITHUB_REF -cne 'refs/heads/main' -or $env:GITHUB_JOB -cne 'release-batch' -or
        $env:GITHUB_RUN_ID -cnotmatch '^[1-9]\d*$' -or $env:GITHUB_RUN_ATTEMPT -cne '1' -or
        $env:GITHUB_WORKFLOW_REF -cne "$script:KoraRepository/.github/workflows/ci.yml@refs/heads/main" -or
        $env:GITHUB_WORKFLOW_SHA -cne $Revision) {
        throw 'Batch publication requires the trusted serialized release-batch main workflow caller.'
    }
    $run = Get-GitHubRecord "repos/$script:KoraRepository/actions/runs/$env:GITHUB_RUN_ID"
    $workflow = Get-GitHubRecord "repos/$script:KoraRepository/actions/workflows/ci.yml"
    if ($null -eq $run -or $null -eq $workflow -or
        $run.id.ToString() -cne $env:GITHUB_RUN_ID -or $run.run_attempt.ToString() -cne $env:GITHUB_RUN_ATTEMPT -or
        $run.repository.full_name -cne $script:KoraRepository -or
        $run.head_repository.full_name -cne $script:KoraRepository -or
        $run.path -cne '.github/workflows/ci.yml' -or $workflow.path -cne '.github/workflows/ci.yml' -or
        $workflow.state -cne 'active' -or $run.workflow_id -ne $workflow.id -or
        $run.head_branch -cne 'main' -or $run.head_sha -cne $Revision -or
        $run.event -cne $env:GITHUB_EVENT_NAME -or $run.event -cnotin @('push', 'workflow_dispatch') -or
        $run.status -cne 'in_progress') {
        throw 'Batch publication could not authenticate the trusted main workflow run.'
    }
    $matchingJobs = @()
    for ($page = 1; ; $page++) {
        $jobs = Get-GitHubRecord "repos/$script:KoraRepository/actions/runs/$env:GITHUB_RUN_ID/attempts/$env:GITHUB_RUN_ATTEMPT/jobs?per_page=100&page=$page"
        if ($null -eq $jobs -or $jobs.jobs -isnot [array] -or
            ($jobs.total_count -isnot [int] -and $jobs.total_count -isnot [long]) -or $jobs.total_count -lt 1) {
            throw 'Batch publication could not authenticate release jobs.'
        }
        $matchingJobs += @($jobs.jobs | Where-Object name -CEQ 'Publish completed batch release')
        if ($page * 100 -ge $jobs.total_count) { break }
        if ($jobs.jobs.Count -ne 100) { throw 'Incomplete batch publication job evidence.' }
    }
    if ($matchingJobs.Count -ne 1 -or $matchingJobs[0].status -cne 'in_progress' -or
        $matchingJobs[0].run_id.ToString() -cne $env:GITHUB_RUN_ID -or
        $matchingJobs[0].head_sha -cne $Revision) {
        throw 'Batch publication requires a unique active serialized release job.'
    }
    Assert-BatchReleaseWorkflow (Get-ReleaseRepositoryText '.github/workflows/ci.yml' $Revision)
}

function Assert-ReleaseQueueSafety {
    param([string] $Revision, [string] $ExpectedProfile, [switch] $Publishing)
    $snapshot = Resolve-ReleaseQueueProfile $env:KORA_MERGE_QUEUE_PROFILE
    $delivery = $env:KORA_MERGE_QUEUE_DELIVERY_PROFILE
    if ($delivery -cnotin @($null, '', 'independent-v1')) { throw 'Unknown release delivery profile.' }
    $queueSnapshot = if ($delivery -ceq 'independent-v1') { Get-ReleaseQueueSnapshot -WithIdentity } else { $null }
    $text = if ($delivery -ceq 'independent-v1') { $queueSnapshot.Text } else { Get-ReleaseQueueSnapshot }
    $profile = 'singleton-v1'
    $state = $null
    if ($null -ne $text) {
        $state = $text | ConvertFrom-Json -AsHashtable
        if ($state -isnot [System.Collections.IDictionary] -or
            ($state.version -isnot [int] -and $state.version -isnot [long])) {
            throw 'Invalid durable release merge queue state.'
        }
        if ($state.version -eq 2 -and $state.profile -is [string] -and $state.profile -ceq 'batch-v1') {
            $profile = 'batch-v1'
        } elseif ($state.version -ne 1 -or
            ($state.Contains('profile') -and ($state.profile -isnot [string] -or $state.profile -cne 'singleton-v1'))) {
            throw 'Unknown durable release merge queue profile/schema.'
        }
    }
    if ($ExpectedProfile -and $profile -cne $ExpectedProfile) {
        throw 'Release durable merge queue profile changed; a fresh run is required.'
    }
    if ($snapshot -ceq 'batch-v1' -and $profile -cne 'batch-v1') {
        throw 'Batch release requires durable v2 batch-v1 queue state.'
    }
    if ($profile -ceq 'singleton-v1') { return $profile }
    if ($state.ContainsKey('deliveryProfile') -and $state.deliveryProfile -cne 'independent-v1') {
        throw 'Unknown durable release delivery profile.'
    }
    if ($delivery -ceq 'independent-v1') {
        if ($state.deliveryProfile -cne 'independent-v1') { throw 'Independent release authority is not initialized.' }
        Assert-IndependentReleaseSource $Revision $queueSnapshot.Revision
        if ($Publishing) { Assert-SerializedBatchReleaseCaller $Revision }
        return $profile
    }
    if (-not $state.Contains('active') -or $null -ne $state.active) {
        throw 'Batch release requires valid v2 batch-v1 queue state with active explicitly null.'
    }
    $tip = $state.completedTip
    if ($tip -isnot [System.Collections.IDictionary] -or $tip.sha -isnot [string] -or
        $tip.sha -cnotmatch '^[a-f0-9]{40}$' -or $tip.tree -isnot [string] -or
        $tip.tree -cnotmatch '^[a-f0-9]{40}$' -or $tip.ticket -isnot [string] -or
        $tip.ticket -cnotmatch '^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$' -or
        $tip.sha -cne $Revision) {
        throw 'Batch release requires the exact completed batch tip, never an intermediate prefix.'
    }
    $main = Get-GitHubRecord "repos/$script:KoraRepository/branches/main"
    if ($null -eq $main -or $main.name -cne 'main' -or $main.protected -isnot [bool] -or
        -not $main.protected -or $main.commit.sha -cne $Revision -or $main.commit.commit.tree.sha -cne $tip.tree) {
        throw 'Batch release source/tree is not the actual protected main completed tip.'
    }
    if ($Publishing) { Assert-SerializedBatchReleaseCaller $Revision }
    return $profile
}

function Assert-IndependentReleaseSource {
    param([string] $Revision, [string] $QueueRevision)
    if ($env:GITHUB_RUN_ATTEMPT -cne '1' -or $env:GITHUB_REF -cne 'refs/heads/main') {
        throw 'Independent beta publication requires a first-attempt ordinary main run.'
    }
    $recordText = Get-ReleaseRepositoryText "releases/$Revision.json" $QueueRevision
    $record = $recordText | ConvertFrom-Json -AsHashtable
    if ($record.schema -cne 'completed-source-v1' -or $record.source -cne $Revision -or
        $record.tree -isnot [string] -or $record.tree -cnotmatch '^[a-f0-9]{40}$' -or
        $record.base -isnot [string] -or $record.base -cnotmatch '^[a-f0-9]{40}$' -or
        $record.combined -isnot [string] -or $record.combined -cnotmatch '^[a-f0-9]{40}$' -or
        $record.ticket -isnot [string] -or $record.ticket -cnotmatch '^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$' -or
        $record.validation.run -isnot [long] -and $record.validation.run -isnot [int] -or
        $record.validation.run -le 0 -or $record.validation.attempt -ne 1 -or
        $record.validation.source -cne $record.combined -or $record.validation.workflow -cne $record.base -or
        $record.validation.schema -cne 'windows-12-delivery-v1' -or
        $record.validation.inventory -isnot [string] -or $record.validation.inventory -cnotmatch '^[a-f0-9]{40}$') {
        throw 'Invalid independent completed-source authority.'
    }
    if ($record.ContainsKey('manifest')) {
        if ($record.manifest -isnot [string] -or $record.manifest -cnotmatch '^[a-f0-9]{64}$' -or
            $record.admission.schema -cne 'batch-manifest-v1' -or
            $record.admission.ticket -cne $record.ticket -or $record.admission.base -cne $record.base -or
            $record.admission.combined -cne $record.combined -or
            $record.admission.inventory.blob -cne $record.validation.inventory -or
            $record.admission.inventory.schema -cne $record.validation.schema -or
            $record.admission.prefixes[-1].sha -cne $record.combined -or
            $record.admission.prefixes[-1].tree -cne $record.tree) { throw 'Invalid completed-source batch manifest.' }
        $document = [System.Text.Json.JsonDocument]::Parse($recordText)
        try {
            $bytes = [Text.Encoding]::UTF8.GetBytes($document.RootElement.GetProperty('admission').GetRawText())
            $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
            if ($digest -cne $record.manifest) { throw 'Completed-source batch manifest digest differs.' }
        } finally { $document.Dispose() }
    }
    $commit = Get-GitHubRecord "repos/$script:KoraRepository/git/commits/$Revision"
    $combined = Get-GitHubRecord "repos/$script:KoraRepository/git/commits/$($record.combined)"
    $main = Get-GitHubRecord "repos/$script:KoraRepository/branches/main"
    if ($null -eq $commit -or $commit.sha -cne $Revision -or $commit.tree.sha -cne $record.tree -or
        $null -eq $combined -or $combined.sha -cne $record.combined -or $combined.tree.sha -cne $record.tree -or
        $null -eq $main -or $main.name -cne 'main' -or $main.protected -isnot [bool] -or -not $main.protected) {
        throw 'Independent release source/tree is not the admitted completed merge.'
    }
    $comparison = Get-GitHubRecord "repos/$script:KoraRepository/compare/$Revision...$($main.commit.sha)"
    if ($null -eq $comparison -or $comparison.status -cnotin @('ahead', 'identical')) {
        throw 'Independent release source is not reachable from protected main.'
    }
}
