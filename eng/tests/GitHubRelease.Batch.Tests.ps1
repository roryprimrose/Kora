function Test-BatchReleaseSafety {
    $prefix = 'repos/roryprimrose/Kora'
    $queueRefPath = "$prefix/git/ref/heads/automation/merge-queue-state"
    $queueCommitPath = "$prefix/git/commits/$('f' * 40)"
    $queueTreePath = "$prefix/git/trees/$('e' * 40)"
    $queuePath = "$prefix/contents/queue.json?ref=$('f' * 40)"
    $mainPath = "$prefix/branches/main"
    $runPath = "$prefix/actions/runs/123"
    $workflowPath = "$prefix/actions/workflows/ci.yml"
    $jobsPath = "$prefix/actions/runs/123/attempts/1/jobs?per_page=100&page=1"
    $sourcePath = "$prefix/contents/.github/workflows/ci.yml?ref=$source"
    $environment = @{
        KORA_MERGE_QUEUE_PROFILE = 'batch-v1'; GITHUB_JOB = 'release-batch'; GITHUB_RUN_ATTEMPT = '1'
        KORA_MERGE_QUEUE_DELIVERY_PROFILE = ''
        GITHUB_WORKFLOW_REF = 'roryprimrose/Kora/.github/workflows/ci.yml@refs/heads/main'
        GITHUB_WORKFLOW_SHA = $source
    }
    $savedEnvironment = @{}
    foreach ($key in $environment.Keys) { $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key) }
    $before = $script:assertions
    try {
        foreach ($snapshot in @('', 'singleton-v1')) {
            foreach ($live in @('absent', 'v1', 'explicit-v1')) {
                $env:KORA_MERGE_QUEUE_PROFILE = $snapshot
                $global:KoraReleaseTestState = New-ReleaseFixtureState
                if ($live -ne 'absent') {
                    $legacy = @{ version = 1; active = $null; blocked = @() }
                    if ($live -eq 'explicit-v1') { $legacy.profile = 'singleton-v1' }
                    $global:KoraReleaseTestState.SafetyRecords[$queueRefPath] = New-ReleaseQueueReference
                    $global:KoraReleaseTestState.SafetyRecords[$queuePath] =
                        New-ReleaseSafetyFile 'queue.json' ($legacy | ConvertTo-Json -Depth 10)
                }
                Assert (-not (Invoke-Release).AlreadyPublished) 'Legacy profile changed Check behavior.'
                $expectedCalls = if ($live -eq 'absent') { 4 } else { 8 }
                Assert ($global:KoraReleaseTestState.Calls.Count -eq $expectedCalls) 'Legacy Check unexpectedly consumed batch-only evidence.'
            }
        }
        foreach ($key in $environment.Keys) { [Environment]::SetEnvironmentVariable($key, $environment[$key]) }
        $global:KoraReleaseTestState = New-ReleaseFixtureState
        Set-ReleaseBatchFixture $source
        $good = Copy-State $global:KoraReleaseTestState
        $queue = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($good.SafetyRecords[$queuePath].Record.content)) |
            ConvertFrom-Json -AsHashtable
        foreach ($snapshot in @('', 'singleton-v1', 'batch-v1')) {
            $env:KORA_MERGE_QUEUE_PROFILE = $snapshot
            foreach ($action in @('Check', 'Publish')) {
                $global:KoraReleaseTestState = Copy-State $good
                $next = New-ReleaseQueueReference
                $next.Record.object.sha = 'a' * 40
                $text = if ($snapshot -eq 'batch-v1') { $queue | ConvertTo-Json -Depth 10 }
                    else { @{ version = 1; active = @{ phase = 'validating' }; blocked = @() } | ConvertTo-Json -Depth 10 }
                if ($snapshot -ne 'batch-v1') {
                    $global:KoraReleaseTestState.SafetyRecords[$queuePath] =
                        New-ReleaseSafetyFile 'queue.json' (@{ version = 1; active = $null; blocked = @() } | ConvertTo-Json)
                }
                Set-ReleaseQueueRevisionFixture ('a' * 40) ('c' * 40) $text
                $global:KoraReleaseTestState.SafetySequences[$queueRefPath] = @((New-ReleaseQueueReference), $next)
                $result = Invoke-Release $action
                Assert (@($global:KoraReleaseTestState.Calls | Where-Object { $_ -contains "$prefix/contents/queue.json?ref=$('a' * 40)" }).Count -gt 0) 'Moved queue contents were not revalidated at the new exact commit.'
                if ($action -eq 'Publish') {
                    Assert (-not $global:KoraReleaseTestState.Releases[0].draft) 'Snapshot retry did not complete publication.'
                } else {
                    Assert (-not $result.AlreadyPublished) 'A stable verified checkpoint after ref movement was rejected.'
                    Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Snapshot retry made Check mutate publication.'
                }
            }
        }
        $env:KORA_MERGE_QUEUE_PROFILE = 'batch-v1'
        foreach ($settles in @($false, $true)) {
            $global:KoraReleaseTestState = Copy-State $good
            Set-ReleaseQueueRevisionFixture ('a' * 40) ('c' * 40) ($queue | ConvertTo-Json -Depth 10)
            $first = New-ReleaseQueueReference
            $second = New-ReleaseQueueReference
            $second.Record.object.sha = 'a' * 40
            $last = if ($settles) { $second } else { $first }
            $global:KoraReleaseTestState.SafetySequences[$queueRefPath] =
                @($first, $second, $first, $second, $first, $second, $last)
            if ($settles) {
                Assert (-not (Invoke-Release Check).AlreadyPublished) 'The sixth verified stable attempt was rejected.'
            } else {
                Reject { Invoke-Release Publish } 'after six attempts'
            }
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Unstable snapshot caused release mutation.'
            Assert (@($global:KoraReleaseTestState.Calls | Where-Object { $_ -contains $queueRefPath }).Count -eq 7) 'Snapshot acquisition exceeded its six-attempt request bound.'
        }
        foreach ($change in @('active-batch', 'legacy', 'malformed', 'missing', 'forbidden', 'blob')) {
            $global:KoraReleaseTestState = Copy-State $good
            $next = New-ReleaseQueueReference
            $next.Record.object.sha = 'a' * 40
            $invalid = Copy-State $queue
            if ($change -eq 'active-batch') { $invalid.active = @{ index = 1 } }
            $text = switch ($change) {
                'legacy' { '{"version":1,"active":null,"blocked":[]}' }
                'malformed' { '{broken' }
                default { $invalid | ConvertTo-Json -Depth 10 }
            }
            Set-ReleaseQueueRevisionFixture ('a' * 40) ('c' * 40) $text
            $nextPath = "$prefix/contents/queue.json?ref=$('a' * 40)"
            switch ($change) {
                'missing' { $global:KoraReleaseTestState.SafetyRecords[$nextPath] = @{ Status = 404; Record = $null } }
                'forbidden' { $global:KoraReleaseTestState.SafetyRecords[$nextPath] = @{ Status = 403; Record = $null } }
                'blob' { $global:KoraReleaseTestState.SafetyRecords[$nextPath].Record.sha = '0' * 40 }
            }
            $global:KoraReleaseTestState.SafetySequences[$queueRefPath] = @((New-ReleaseQueueReference), $next)
            Reject { Invoke-Release Publish }
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) "Retry accepted invalid replacement state: $change."
        }
        foreach ($snapshot in @('', 'singleton-v1')) {
            $env:KORA_MERGE_QUEUE_PROFILE = $snapshot
            $global:KoraReleaseTestState = Copy-State $good
            $global:KoraReleaseTestState.SafetyRecords[$queuePath] =
                New-ReleaseSafetyFile 'queue.json' '{"version":1,"active":null,"blocked":[]}'
            $invalid = Copy-State $queue
            $invalid.active = @{ index = 1 }
            Set-ReleaseQueueRevisionFixture ('a' * 40) ('c' * 40) ($invalid | ConvertTo-Json -Depth 10)
            $next = New-ReleaseQueueReference
            $next.Record.object.sha = 'a' * 40
            $global:KoraReleaseTestState.SafetySequences[$queueRefPath] = @((New-ReleaseQueueReference), $next)
            Reject { Invoke-Release Publish } 'active explicitly null'
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Snapshot retry downgraded a concurrent batch migration.'
        }
        $env:KORA_MERGE_QUEUE_PROFILE = 'batch-v1'
        $global:KoraReleaseTestState = Copy-State $good
        Assert (-not (Invoke-Release).AlreadyPublished) 'Completed batch tip cannot pass Check.'
        Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Check mutated release state.'
        Invoke-Release Publish | Out-Null
        Assert (-not $global:KoraReleaseTestState.Releases[0].draft) 'Completed batch tip was not published.'
        Assert ($global:KoraReleaseTestState.Tag.object.sha -ceq $source) 'Batch publication tagged a candidate instead of actual source.'
        Assert ($global:KoraReleaseTestState.Releases[0].target_commitish -ceq $source) 'Batch release source changed.'
        Assert (@($global:KoraReleaseTestState.Calls | Where-Object { ($_ -join ' ') -like '*/actions/variables/*' }).Count -eq 0) 'Release unexpectedly required Variables API permission.'
        Assert (@($global:KoraReleaseTestState.Calls | Where-Object { $_ -contains $queuePath }).Count -eq 4) 'Batch did not re-read durable state before tag and publication.'
        Assert (@($global:KoraReleaseTestState.Calls | Where-Object { $_ -contains $queueRefPath }).Count -eq 8) 'Batch did not resolve and revalidate each durable snapshot reference.'
        Assert (@($global:KoraReleaseTestState.Calls | Where-Object { ($_ -join ' ') -like '*contents/queue.json?ref=automation*' }).Count -eq 0) 'Queue content was read through a mutable branch name.'
        $publishedBatch = Copy-State $global:KoraReleaseTestState
        Assert ((Invoke-Release).AlreadyPublished) 'Completed batch publication is not idempotent.'
        Assert ($global:KoraReleaseTestState.Writes.Count -eq $publishedBatch.Writes.Count) 'Completed batch no-op mutated release.'
        $global:KoraReleaseTestState = New-ReleaseFixtureState
        Set-ReleaseBatchFixture $source -CurrentWorkflow
        Invoke-Release Publish | Out-Null
        Assert (-not $global:KoraReleaseTestState.Releases[0].draft) 'Current CI workflow header did not admit completed batch publication.'
        Assert ($global:KoraReleaseTestState.Tag.object.sha -ceq $source) 'Current CI workflow changed exact batch release source.'

        foreach ($unknown in @('batch-v2', 'BATCH-v1', 'disabled', ' batch-v1')) {
            $global:KoraReleaseTestState = Copy-State $good
            $env:KORA_MERGE_QUEUE_PROFILE = $unknown
            Reject { Invoke-Release Publish } 'Unknown release merge queue profile'
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Unknown snapshot wrote a release.'
            $env:KORA_MERGE_QUEUE_PROFILE = 'batch-v1'
            $invalid = Copy-State $queue
            $invalid.profile = $unknown
            $global:KoraReleaseTestState.SafetyRecords[$queuePath] =
                New-ReleaseSafetyFile 'queue.json' ($invalid | ConvertTo-Json -Depth 10)
            Reject { Invoke-Release Publish } 'Unknown durable release merge queue profile'
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Unknown live profile wrote a release.'
        }
        foreach ($snapshot in @('', 'singleton-v1')) {
            $global:KoraReleaseTestState = Copy-State $good
            $env:KORA_MERGE_QUEUE_PROFILE = $snapshot
            Assert (-not (Invoke-Release Check).AlreadyPublished) 'Stale singleton snapshot prevented durable batch checks.'
            Assert (@($global:KoraReleaseTestState.Calls | Where-Object { $_ -contains $mainPath }).Count -eq 1) 'Stale snapshot bypassed protected-main batch evidence.'
            $env:GITHUB_JOB = 'release'
            Reject { Invoke-Release Publish } 'trusted serialized'
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Cached singleton snapshot bypassed serialized batch caller proof.'
            $env:GITHUB_JOB = 'release-batch'
            $invalid = Copy-State $queue
            $invalid.active = @{ ticket = $queue.completedTip.ticket }
            $global:KoraReleaseTestState.SafetyRecords[$queuePath] =
                New-ReleaseSafetyFile 'queue.json' ($invalid | ConvertTo-Json -Depth 10)
            Reject { Invoke-Release Check } 'active explicitly null'
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Empty cached snapshot bypassed an active durable batch.'
        }
        $global:KoraReleaseTestState = Copy-State $good
        $env:KORA_MERGE_QUEUE_PROFILE = ''
        Invoke-Release Publish | Out-Null
        Assert (-not $global:KoraReleaseTestState.Releases[0].draft) 'Durable completed batch tip could not publish through a proven static caller with an empty snapshot.'
        Assert ($global:KoraReleaseTestState.Tag.object.sha -ceq $source) 'Empty snapshot changed the completed batch source.'
        $env:KORA_MERGE_QUEUE_PROFILE = 'batch-v1'
        foreach ($path in @($queueRefPath, $queueCommitPath, $queueTreePath, $queuePath, $mainPath, $runPath, $workflowPath, $jobsPath, $sourcePath)) {
            foreach ($status in @(401, 403, 429, 500, 503)) {
                $global:KoraReleaseTestState = Copy-State $good
                $global:KoraReleaseTestState.SafetyRecords[$path] = @{ Status = $status; Record = $null }
                Reject { Invoke-Release Publish } 'Cannot establish GitHub publication state'
                Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) "Safety HTTP $status allowed a write."
            }
            $global:KoraReleaseTestState = Copy-State $good
            $global:KoraReleaseTestState.SafetyRecords[$path] = @{ Status = 404; Record = $null }
            Reject { Invoke-Release Publish }
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Missing safety evidence allowed a write.'
        }
        foreach ($snapshot in @('', 'singleton-v1')) {
            $global:KoraReleaseTestState = Copy-State $good
            $env:KORA_MERGE_QUEUE_PROFILE = $snapshot
            $global:KoraReleaseTestState.SafetyRecords[$queuePath] = @{ Status = 404; Record = $null }
            Reject { Invoke-Release Publish } 'Cannot establish release safety file'
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Existing queue ref with missing file was reinterpreted as legacy.'
        }
        $env:KORA_MERGE_QUEUE_PROFILE = 'batch-v1'
        foreach ($change in @('ref-name', 'ref-type', 'ref-sha', 'ref-sha-type', 'commit-sha', 'commit-tree',
            'tree-sha', 'truncated-tree', 'missing-file', 'duplicate-file', 'symlink', 'file-type',
            'blob-sha', 'blob-sha-type', 'tree-blob-sha-type', 'content-type', 'bytes', 'size', 'stale-content')) {
            $global:KoraReleaseTestState = Copy-State $good
            $commit = @{ Status = 200; Record = @{ sha = 'f' * 40; tree = @{ sha = 'e' * 40 } } }
            $tree = @{ Status = 200; Record = @{ sha = 'e' * 40; truncated = $false
                tree = @(@{ path = 'queue.json'; mode = '100644'; type = 'blob'; sha = $good.SafetyRecords[$queuePath].Record.sha }) } }
            $global:KoraReleaseTestState.SafetyRecords[$queueCommitPath] = $commit
            $global:KoraReleaseTestState.SafetyRecords[$queueTreePath] = $tree
            switch ($change) {
                'ref-name' { $global:KoraReleaseTestState.SafetyRecords[$queueRefPath].Record.ref = 'refs/heads/main' }
                'ref-type' { $global:KoraReleaseTestState.SafetyRecords[$queueRefPath].Record.object.type = 'tag' }
                'ref-sha' { $global:KoraReleaseTestState.SafetyRecords[$queueRefPath].Record.object.sha = 'invalid' }
                'ref-sha-type' { $global:KoraReleaseTestState.SafetyRecords[$queueRefPath].Record.object.sha = @('f' * 40) }
                'commit-sha' { $commit.Record.sha = 'a' * 40 }
                'commit-tree' { $commit.Record.tree.sha = 'invalid' }
                'tree-sha' { $tree.Record.sha = 'a' * 40 }
                'truncated-tree' { $tree.Record.truncated = $true }
                'missing-file' { $tree.Record.tree = @() }
                'duplicate-file' { $tree.Record.tree += $tree.Record.tree[0] }
                'symlink' { $tree.Record.tree[0].mode = '120000' }
                'file-type' { $tree.Record.tree[0].type = 'tree' }
                'blob-sha' { $global:KoraReleaseTestState.SafetyRecords[$queuePath].Record.sha = '0' * 40 }
                'blob-sha-type' { $global:KoraReleaseTestState.SafetyRecords[$queuePath].Record.sha = @($tree.Record.tree[0].sha) }
                'tree-blob-sha-type' { $tree.Record.tree[0].sha = @($tree.Record.tree[0].sha) }
                'content-type' {
                    $global:KoraReleaseTestState.SafetyRecords[$queuePath].Record.content =
                        @($global:KoraReleaseTestState.SafetyRecords[$queuePath].Record.content)
                }
                'bytes' {
                    $bytes = [Convert]::FromBase64String($global:KoraReleaseTestState.SafetyRecords[$queuePath].Record.content)
                    $bytes[0] = $bytes[0] -bxor 1
                    $global:KoraReleaseTestState.SafetyRecords[$queuePath].Record.content = [Convert]::ToBase64String($bytes)
                }
                'size' { $global:KoraReleaseTestState.SafetyRecords[$queuePath].Record.size++ }
                'stale-content' {
                    $stale = Copy-State $queue
                    $stale.completedTip.sha = 'a' * 40
                    $global:KoraReleaseTestState.SafetyRecords[$queuePath] =
                        New-ReleaseSafetyFile 'queue.json' ($stale | ConvertTo-Json -Depth 10)
                }
            }
            $diagnostic = if ($change -in @('blob-sha', 'bytes', 'stale-content')) { 'bytes/blob differ' } else { '' }
            Reject { Invoke-Release Publish } $diagnostic
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) "Invalid pinned queue identity $change allowed mutation."
        }
        foreach ($change in @('moved', 'disappeared', 'forbidden')) {
            $global:KoraReleaseTestState = Copy-State $good
            $next = New-ReleaseQueueReference
            switch ($change) {
                'moved' { $next.Record.object.sha = 'a' * 40 }
                'disappeared' { $next = @{ Status = 404; Record = $null } }
                'forbidden' { $next = @{ Status = 403; Record = $null } }
            }
            $global:KoraReleaseTestState.SafetySequences[$queueRefPath] = @((New-ReleaseQueueReference), $next)
            Reject { Invoke-Release Publish }
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Queue ref changed during snapshot read but publication proceeded.'
            Assert (@($global:KoraReleaseTestState.Calls | Where-Object { $_ -contains $queuePath }).Count -eq 1) 'Pinned contents read was omitted or retried after ref drift.'
            Assert (@($global:KoraReleaseTestState.Calls | Where-Object { ($_ -join ' ') -like "*contents/queue.json?ref=$('a' * 40)*" }).Count -eq 0) 'Snapshot silently followed a moved ref.'
        }
        foreach ($value in @($null, 1, @('batch-v1'))) {
            $global:KoraReleaseTestState = Copy-State $good
            $invalid = Copy-State $queue
            $invalid.profile = $value
            $global:KoraReleaseTestState.SafetyRecords[$queuePath] =
                New-ReleaseSafetyFile 'queue.json' ($invalid | ConvertTo-Json -Depth 10)
            Reject { Invoke-Release Publish }
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Untyped profile allowed a write.'
        }

        foreach ($change in @('active-batch', 'active-prefix', 'active-false', 'missing-active', 'version',
            'profile', 'missing-tip', 'prefix', 'sha', 'tree', 'ticket', 'missing-version', 'missing-profile')) {
            $global:KoraReleaseTestState = Copy-State $good
            $invalid = Copy-State $queue
            switch ($change) {
                'active-batch' { $invalid.active = @{ ticket = $queue.completedTip.ticket; members = @(1, 2, 3, 4, 5) } }
                'active-prefix' { $invalid.active = @{ index = 4; completedPrefix = $source } }
                'active-false' { $invalid.active = $false }
                'missing-active' { $invalid.Remove('active') | Out-Null }
                'version' { $invalid.version = 1 }
                'profile' { $invalid.profile = 'singleton-v1' }
                'missing-tip' { $invalid.Remove('completedTip') | Out-Null }
                'prefix' { $invalid.completedTip.sha = 'b' * 40 }
                'sha' { $invalid.completedTip.sha = @($source) }
                'tree' { $invalid.completedTip.tree = 'not-a-tree' }
                'ticket' { $invalid.completedTip.ticket = 'not-a-ticket' }
                'missing-version' { $invalid.Remove('version') | Out-Null }
                'missing-profile' { $invalid.Remove('profile') | Out-Null }
            }
            $global:KoraReleaseTestState.SafetyRecords[$queuePath] = New-ReleaseSafetyFile 'queue.json' ($invalid | ConvertTo-Json -Depth 10)
            Reject { Invoke-Release Publish }
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) "Invalid batch state $change wrote a release."
            Reject { Invoke-Release Check }
        }
        foreach ($text in @('{broken', 'null', '[]', '{}')) {
            $global:KoraReleaseTestState = Copy-State $good
            $global:KoraReleaseTestState.SafetyRecords[$queuePath] = New-ReleaseSafetyFile 'queue.json' $text
            Reject { Invoke-Release Publish }
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Malformed batch state wrote a release.'
        }
        $global:KoraReleaseTestState = Copy-State $good
        $qualified = Copy-State $queue
        $qualified.completedTip.qualification = @{ schema = 'fixture'; checks = @('portable', 'windows') }
        $global:KoraReleaseTestState.SafetyRecords[$queuePath] = New-ReleaseSafetyFile 'queue.json' ($qualified | ConvertTo-Json -Depth 10)
        Assert (-not (Invoke-Release Check).AlreadyPublished) 'Optional qualification prevented completed-tip admission.'
        foreach ($field in @('sha', 'tree', 'protected')) {
            $global:KoraReleaseTestState = Copy-State $good
            $record = $global:KoraReleaseTestState.SafetyRecords[$mainPath].Record
            switch ($field) {
                'sha' { $record.commit.sha = 'b' * 40 }
                'tree' { $record.commit.commit.tree.sha = 'b' * 40 }
                'protected' { $record.protected = $false }
            }
            Reject { Invoke-Release Publish } 'actual protected main'
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Unprotected or stale main allowed publication.'
        }
        foreach ($key in $environment.Keys | Where-Object { $_ -notin @('KORA_MERGE_QUEUE_PROFILE', 'KORA_MERGE_QUEUE_DELIVERY_PROFILE') }) {
            $global:KoraReleaseTestState = Copy-State $good
            [Environment]::SetEnvironmentVariable($key, 'spoofed')
            Reject { Invoke-Release Publish } 'trusted serialized'
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Spoofed caller allowed publication.'
            [Environment]::SetEnvironmentVariable($key, $environment[$key])
        }
        $env:GITHUB_REF = "refs/tags/v$version"
        $global:KoraReleaseTestState = Copy-State $good
        Reject { Invoke-Release Publish } 'trusted serialized'
        Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Tag workflow claimed batch lock.'
        $env:GITHUB_REF = 'refs/heads/main'
        foreach ($field in @('id', 'run_attempt', 'repository', 'head_repository', 'path', 'workflow_id', 'head_branch', 'head_sha', 'event', 'status')) {
            $global:KoraReleaseTestState = Copy-State $good
            $record = $global:KoraReleaseTestState.SafetyRecords[$runPath].Record
            if ($field -in @('repository', 'head_repository')) { $record[$field].full_name = 'untrusted/fork' }
            else { $record[$field] = 'wrong' }
            Reject { Invoke-Release Publish }
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) "Unauthenticated run $field allowed publication."
        }
        foreach ($change in @('missing', 'duplicate', 'completed', 'sha', 'run', 'incomplete')) {
            $global:KoraReleaseTestState = Copy-State $good
            $record = $global:KoraReleaseTestState.SafetyRecords[$jobsPath].Record
            switch ($change) {
                'missing' { $record.jobs = @() }
                'duplicate' { $record.jobs += $record.jobs[0] }
                'completed' { $record.jobs[0].status = 'completed' }
                'sha' { $record.jobs[0].head_sha = 'b' * 40 }
                'run' { $record.jobs[0].run_id = 456 }
                'incomplete' { $record.total_count = 101 }
            }
            Reject { Invoke-Release Publish }
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Unknown or duplicate job allowed publication.'
        }
        $workflow = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($good.SafetyRecords[$sourcePath].Record.content))
        foreach ($text in @(
            $workflow.Replace('group: merge-queue-controller', 'group: release-${{ needs.version.outputs.version }}'),
            $workflow.Replace('group: merge-queue-controller', 'group: ${{ vars.KORA_MERGE_QUEUE_PROFILE }}'),
            $workflow.Replace('cancel-in-progress: false', 'cancel-in-progress: true'),
            $workflow.Replace("      cancel-in-progress: false", ''),
            $workflow.Replace("      queue: max", ''),
            $workflow.Replace('queue: max', 'queue: single'),
            $workflow.Replace('queue: max', 'queue: ${{ vars.KORA_MERGE_QUEUE_PROFILE }}'),
            $workflow.Replace('queue: max', "queue: max`n      queue: max"),
            $workflow.Replace('  release-batch:', '  release:'),
            $workflow.Replace('    concurrency:', "    concurrency:`n      group: merge-queue-controller"),
            $workflow.Replace('    concurrency:', "    'concurrency':"),
            $workflow.Replace('jobs:', "jobs:`njobs:"),
            $workflow.Replace('  release-batch:', "  release-batch:`n  release-batch:"),
            $workflow.Replace('  release-batch:', "  'release-batch':"),
            $workflow.Replace('    name: Publish completed batch release', '    name: Other job'),
            $workflow.Replace('    runs-on: ubuntu-latest', '    runs-on: [self-hosted]'),
            $workflow.Replace('    concurrency:', "    <<: *lock`n    concurrency:")
        )) {
            $global:KoraReleaseTestState = Copy-State $good
            $global:KoraReleaseTestState.SafetyRecords[$sourcePath] = New-ReleaseSafetyFile '.github/workflows/ci.yml' $text
            Reject { Invoke-Release Publish } 'Batch release workflow'
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Unproved static lock allowed publication.'
        }
        foreach ($field in @('encoding', 'size', 'path', 'content')) {
            $global:KoraReleaseTestState = Copy-State $good
            $global:KoraReleaseTestState.SafetyRecords[$sourcePath].Record[$field] = 'invalid'
            Reject { Invoke-Release Publish }
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Malformed protected workflow source allowed publication.'
        }

        $active = Copy-State $queue
        $active.active = @{ ticket = $queue.completedTip.ticket }
        $late = @{
            $queuePath = (New-ReleaseSafetyFile 'queue.json' ($active | ConvertTo-Json -Depth 10))
            $mainPath = (Copy-State $good.SafetyRecords[$mainPath])
            $sourcePath = (New-ReleaseSafetyFile '.github/workflows/ci.yml' ($workflow.Replace('group: merge-queue-controller', 'group: unlocked')))
            $runPath = (Copy-State $good.SafetyRecords[$runPath])
            $jobsPath = (Copy-State $good.SafetyRecords[$jobsPath])
        }
        $late[$mainPath].Record.commit.sha = 'b' * 40
        $late[$runPath].Record.status = 'cancelled'
        $late[$jobsPath].Record.jobs[0].status = 'completed'
        foreach ($boundary in @('tag', 'publication')) {
            foreach ($change in @('stable', 'active', 'legacy', 'missing')) {
                $global:KoraReleaseTestState = Copy-State $good
                $next = New-ReleaseQueueReference
                $next.Record.object.sha = 'a' * 40
                $replacement = Copy-State $queue
                if ($change -eq 'active') { $replacement.active = @{ index = 1 } }
                $text = if ($change -eq 'legacy') { '{"version":1,"active":null,"blocked":[]}' }
                    else { $replacement | ConvertTo-Json -Depth 10 }
                Set-ReleaseQueueRevisionFixture ('a' * 40) ('c' * 40) $text
                if ($change -eq 'missing') {
                    $global:KoraReleaseTestState.SafetyRecords["$prefix/contents/queue.json?ref=$('a' * 40)"] =
                        @{ Status = 404; Record = $null }
                }
                $sequence = @((New-ReleaseQueueReference), (New-ReleaseQueueReference), (New-ReleaseQueueReference))
                if ($boundary -eq 'publication') { $sequence += @((New-ReleaseQueueReference), (New-ReleaseQueueReference)) }
                $global:KoraReleaseTestState.SafetySequences[$queueRefPath] = @($sequence) + @($next)
                if ($change -eq 'stable') {
                    Invoke-Release Publish | Out-Null
                    Assert (-not $global:KoraReleaseTestState.Releases[0].draft) "Verified late $boundary snapshot retry failed."
                    Assert ($global:KoraReleaseTestState.Tag.object.sha -ceq $source) 'Late retry changed the release source.'
                } else {
                    Reject { Invoke-Release Publish }
                    Assert ($global:KoraReleaseTestState.Writes -notcontains 'publish') "Late $boundary retry accepted $change state."
                    Assert (($global:KoraReleaseTestState.Writes -contains 'tag') -eq ($boundary -eq 'publication')) 'Retried snapshot crossed the wrong mutation boundary.'
                }
            }
            foreach ($path in $late.Keys) {
                $global:KoraReleaseTestState = Copy-State $good
                if ($boundary -eq 'tag') { $global:KoraReleaseTestState.SafetyAfterUpload[$path] = $late[$path] }
                else { $global:KoraReleaseTestState.SafetyAfterTag[$path] = $late[$path] }
                Reject { Invoke-Release Publish }
                Assert ($global:KoraReleaseTestState.Releases[0].draft) "Late $path change removed draft boundary."
                Assert ($global:KoraReleaseTestState.Writes -notcontains 'publish') "Late $path change published a release."
                Assert (($global:KoraReleaseTestState.Writes -contains 'tag') -eq ($boundary -eq 'publication')) 'Wrong late revalidation boundary.'
            }
            $global:KoraReleaseTestState = Copy-State $good
            $env:KORA_MERGE_QUEUE_PROFILE = 'singleton-v1'
            $global:KoraReleaseTestState.SafetyRecords[$queuePath] =
                New-ReleaseSafetyFile 'queue.json' '{"version":1,"active":null,"blocked":[]}'
            if ($boundary -eq 'tag') {
                $global:KoraReleaseTestState.SafetyAfterUpload[$queuePath] = $good.SafetyRecords[$queuePath]
            } else { $global:KoraReleaseTestState.SafetyAfterTag[$queuePath] = $good.SafetyRecords[$queuePath] }
            Reject { Invoke-Release Publish } 'profile changed'
            Assert ($global:KoraReleaseTestState.Writes -notcontains 'publish') 'Queued legacy job bypassed live batch opt-in.'
            Assert (($global:KoraReleaseTestState.Writes -contains 'tag') -eq ($boundary -eq 'publication')) 'Legacy profile transition crossed the late tag fence.'
            $env:KORA_MERGE_QUEUE_PROFILE = 'batch-v1'
            $global:KoraReleaseTestState = Copy-State $good
            $legacy = New-ReleaseSafetyFile 'queue.json' '{"version":1,"active":null,"blocked":[]}'
            if ($boundary -eq 'tag') { $global:KoraReleaseTestState.SafetyAfterUpload[$queuePath] = $legacy }
            else { $global:KoraReleaseTestState.SafetyAfterTag[$queuePath] = $legacy }
            Reject { Invoke-Release Publish } 'profile changed'
            Assert ($global:KoraReleaseTestState.Writes -notcontains 'publish') 'Durable profile downgrade bypassed late batch fence.'
            Assert (($global:KoraReleaseTestState.Writes -contains 'tag') -eq ($boundary -eq 'publication')) 'Durable profile downgrade crossed the late tag fence.'
            $global:KoraReleaseTestState = Copy-State $good
            $missing = @{ Status = 404; Record = $null }
            if ($boundary -eq 'tag') { $global:KoraReleaseTestState.SafetyAfterUpload[$queuePath] = $missing }
            else { $global:KoraReleaseTestState.SafetyAfterTag[$queuePath] = $missing }
            Reject { Invoke-Release Publish } 'Cannot establish release safety file'
            Assert ($global:KoraReleaseTestState.Writes -notcontains 'publish') 'Late missing queue file bypassed publication fence.'
            Assert (($global:KoraReleaseTestState.Writes -contains 'tag') -eq ($boundary -eq 'publication')) 'Late missing queue file crossed the tag fence.'
        }
        $global:KoraReleaseTestState = Copy-State $publishedBatch
        $global:KoraReleaseTestState.SafetyRecords[$queuePath] = $late[$queuePath]
        Reject { Invoke-Release Publish } 'active explicitly null'
        Assert ($global:KoraReleaseTestState.Writes.Count -eq $publishedBatch.Writes.Count) 'Published no-op bypassed active batch fence.'
        $env:KORA_MERGE_QUEUE_DELIVERY_PROFILE = 'independent-v1'
        $global:KoraReleaseTestState = Copy-State $good
        $independentQueue = Copy-State $queue
        $independentQueue.deliveryProfile = 'independent-v1'
        $independentQueue.active = @{ kind = 'batch'; ticket = 'new-candidate' }
        $independentQueue.completedTip.sha = 'a' * 40
        $global:KoraReleaseTestState.SafetyRecords[$queuePath] =
            New-ReleaseSafetyFile 'queue.json' ($independentQueue | ConvertTo-Json -Depth 10)
        $record = @{ schema = 'completed-source-v1'; source = $source; tree = 'd' * 40
            base = 'b' * 40; combined = 'c' * 40; ticket = $queue.completedTip.ticket
            validation = @{ run = 42; attempt = 1; source = 'c' * 40; workflow = 'b' * 40
                inventory = 'b' * 40; schema = 'windows-12-delivery-v1' } }
        $recordPath = "$prefix/contents/releases/$source.json?ref=$('f' * 40)"
        $global:KoraReleaseTestState.SafetyRecords[$recordPath] =
            New-ReleaseSafetyFile "releases/$source.json" ($record | ConvertTo-Json -Depth 10)
        $global:KoraReleaseTestState.SafetyRecords["$prefix/git/commits/$source"] = @{ Status = 200
            Record = @{ sha = $source; tree = @{ sha = 'd' * 40 } } }
        $global:KoraReleaseTestState.SafetyRecords["$prefix/git/commits/$('c' * 40)"] = @{ Status = 200
            Record = @{ sha = 'c' * 40; tree = @{ sha = 'd' * 40 } } }
        $global:KoraReleaseTestState.SafetyRecords[$mainPath].Record.commit.sha = 'a' * 40
        $comparisonPath = "$prefix/compare/$source...$('a' * 40)"
        $global:KoraReleaseTestState.SafetyRecords[$comparisonPath] = @{ Status = 200; Record = @{ status = 'ahead' } }
        $independent = Copy-State $global:KoraReleaseTestState
        Invoke-Release Publish | Out-Null
        Assert (-not $global:KoraReleaseTestState.Releases[0].draft) 'New active work revoked an earlier independently admitted beta.'
        Assert ($global:KoraReleaseTestState.Tag.object.sha -ceq $source) 'Independent publication substituted the newer main source.'
        Assert (@($global:KoraReleaseTestState.Calls | Where-Object { $_ -contains 'make_latest=false' }).Count -eq 1) 'Out-of-order beta publication changed the latest channel.'
        $withManifest = Copy-State $record
        $withManifest.admission = @{ schema = 'batch-manifest-v1'; ticket = $record.ticket
            base = $record.base; combined = $record.combined
            inventory = @{ blob = $record.validation.inventory; schema = $record.validation.schema }
            prefixes = @(@{ sha = $record.combined; tree = $record.tree }) }
        $document = [System.Text.Json.JsonDocument]::Parse(($withManifest | ConvertTo-Json -Depth 10 -Compress))
        try {
            $manifestBytes = [Text.Encoding]::UTF8.GetBytes($document.RootElement.GetProperty('admission').GetRawText())
            $withManifest.manifest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($manifestBytes)).ToLowerInvariant()
        } finally { $document.Dispose() }
        $global:KoraReleaseTestState = Copy-State $independent
        $global:KoraReleaseTestState.SafetyRecords[$recordPath] =
            New-ReleaseSafetyFile "releases/$source.json" ($withManifest | ConvertTo-Json -Depth 10 -Compress)
        Invoke-Release Publish | Out-Null
        Assert (-not $global:KoraReleaseTestState.Releases[0].draft) 'Exact completed-source batch manifest was rejected.'
        $withManifest.admission.base = 'a' * 40
        $global:KoraReleaseTestState = Copy-State $independent
        $global:KoraReleaseTestState.SafetyRecords[$recordPath] =
            New-ReleaseSafetyFile "releases/$source.json" ($withManifest | ConvertTo-Json -Depth 10 -Compress)
        Reject { Invoke-Release Publish }
        Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Mismatched batch manifest crossed publication.'
        foreach ($change in @('missing', 'blob', 'source', 'tree', 'combined', 'attempt', 'unreachable', 'profile')) {
            $global:KoraReleaseTestState = Copy-State $independent
            $invalid = Copy-State $record
            switch ($change) {
                'missing' { $global:KoraReleaseTestState.SafetyRecords[$recordPath].Status = 404 }
                'blob' { $global:KoraReleaseTestState.SafetyRecords[$recordPath].Record.sha = '0' * 40 }
                'source' { $invalid.source = 'a' * 40 }
                'tree' { $invalid.tree = 'a' * 40 }
                'combined' { $invalid.combined = 'a' * 40 }
                'attempt' { $invalid.validation.attempt = 2 }
                'unreachable' { $global:KoraReleaseTestState.SafetyRecords[$comparisonPath].Record.status = 'diverged' }
                'profile' { $env:KORA_MERGE_QUEUE_DELIVERY_PROFILE = 'future' }
            }
            if ($change -in @('source', 'tree', 'combined', 'attempt')) {
                $global:KoraReleaseTestState.SafetyRecords[$recordPath] =
                    New-ReleaseSafetyFile "releases/$source.json" ($invalid | ConvertTo-Json -Depth 10)
            }
            Reject { Invoke-Release Publish }
            Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) "Invalid independent proof crossed a publication boundary: $change."
            $env:KORA_MERGE_QUEUE_DELIVERY_PROFILE = 'independent-v1'
        }
        foreach ($boundary in @('tag', 'publication')) {
            $global:KoraReleaseTestState = Copy-State $independent
            $missing = @{ Status = 404; Record = $null }
            if ($boundary -eq 'tag') { $global:KoraReleaseTestState.SafetyAfterUpload[$recordPath] = $missing }
            else { $global:KoraReleaseTestState.SafetyAfterTag[$recordPath] = $missing }
            Reject { Invoke-Release Publish }
            Assert ($global:KoraReleaseTestState.Writes -notcontains 'publish') "Revoked source proof crossed late $boundary publication."
        }
        Write-Host "Batch release safety tests passed: $($script:assertions - $before) assertions; durable live profile, stale snapshots, completed-tip/main, authenticated static lock and both late mutation fences."
    } finally {
        foreach ($key in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key]) }
        $env:GITHUB_REF = 'refs/heads/main'
    }
}
