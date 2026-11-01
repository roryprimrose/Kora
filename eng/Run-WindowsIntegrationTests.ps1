[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('All', 'storage-heavy', 'storage-other', 'ui-definitions', 'native-ux', 'ui-platform')]
    [string] $Shard = 'All',

    [Parameter()]
    [ValidateSet('storage-heavy', 'storage-other', 'ui-definitions', 'ui-definition-dialogs',
        'ui-conversations', 'ui-conversation-core', 'ui-conversation-actions', 'native-ux',
        'ui-platform', 'ui-workflows', 'ui-presentation', 'ui-editors')]
    [string] $Partition,

    [Parameter()]
    [string] $TestModuleDirectory,

    [Parameter()]
    [string] $ExpectedSourceRevision,

    [Parameter()]
    [switch] $ListShards,

    [Parameter()]
    [switch] $ListPartitions,

    [Parameter()]
    [ValidateRange(1, 64)]
    [int] $MaxThreads = 2,

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string] $ResultsDirectory = '.net-test-artifacts\windows'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj'
$resultsRoot = [System.IO.Path]::GetFullPath($ResultsDirectory, $repositoryRoot)

if ($ListShards -and $ListPartitions) {
    throw 'Select either shard or execution-partition discovery, not both.'
}
if ($Partition -and ($Shard -cne 'All' -or $ListShards)) {
    throw 'Select either a legacy hosted shard or an execution partition.'
}
if ([bool] $TestModuleDirectory -ne [bool] $ExpectedSourceRevision) {
    throw 'Artifact execution requires both a test-module directory and its expected source revision.'
}

$platformEditorPrefix = 'Kora.Windows.IntegrationTests.ArtifactDiscovery.OwnedDefinitionEditorWindowTests.'
$conversationPrefix = 'Kora.Windows.IntegrationTests.SessionsConversationTests.'
$definitionUiPrefixes = @(
    'Kora.Windows.IntegrationTests.ArtifactDiscovery.'
    'Kora.Windows.IntegrationTests.BoundedSurfaceAccessibilityTests.'
    $conversationPrefix
    'Kora.Windows.IntegrationTests.SettingsSectionNavigationTests.'
    'Kora.Windows.IntegrationTests.SettingsResetMenuTests.'
    'Kora.Windows.IntegrationTests.SettingsSpecialistNavigationTests.'
    'Kora.Windows.IntegrationTests.SessionRoutingTests.'
    'Kora.Windows.IntegrationTests.PendingQuestionNavigationTests.'
    'Kora.Windows.IntegrationTests.AllWorkWaitingQuestionNavigationTests.'
)
$nativeUiPrefixes = @(
    'Kora.Windows.IntegrationTests.NativeUx.'
    'Kora.Windows.IntegrationTests.Presentation.DispatcherEvidenceResponsivenessTests.'
    'Kora.Windows.IntegrationTests.NativeReaderTextScaleTests.'
    'Kora.Windows.IntegrationTests.SpeechCaptionDisplayTests.'
)
$uiPlatformExclusions = @('Kora.Windows.IntegrationTests.Storage.') + $definitionUiPrefixes + $nativeUiPrefixes

$shards = @(
    [pscustomobject]@{
        Name = 'storage-heavy'
        Filter = 'FullyQualifiedName~Kora.Windows.IntegrationTests.Storage.WindowsSqliteSessionAttachmentTests|FullyQualifiedName~Kora.Windows.IntegrationTests.Storage.WindowsSqliteConversationTests|FullyQualifiedName~Kora.Windows.IntegrationTests.Storage.WindowsSqliteSessionQueueTests|FullyQualifiedName~Kora.Windows.IntegrationTests.Storage.WindowsSqliteMemoryTests|FullyQualifiedName~Kora.Windows.IntegrationTests.Storage.WindowsSqliteExactGrantFilterTests'
    }
    [pscustomobject]@{
        Name = 'storage-other'
        Filter = 'FullyQualifiedName~Kora.Windows.IntegrationTests.Storage.&FullyQualifiedName!~Kora.Windows.IntegrationTests.Storage.WindowsSqliteSessionAttachmentTests&FullyQualifiedName!~Kora.Windows.IntegrationTests.Storage.WindowsSqliteConversationTests&FullyQualifiedName!~Kora.Windows.IntegrationTests.Storage.WindowsSqliteSessionQueueTests&FullyQualifiedName!~Kora.Windows.IntegrationTests.Storage.WindowsSqliteMemoryTests&FullyQualifiedName!~Kora.Windows.IntegrationTests.Storage.WindowsSqliteExactGrantFilterTests'
    }
    [pscustomobject]@{
        Name = 'ui-definitions'
        Filter = ($definitionUiPrefixes | ForEach-Object { "FullyQualifiedName~$_&FullyQualifiedName!~$platformEditorPrefix" }) -join '|'
    }
    [pscustomobject]@{
        Name = 'native-ux'
        Filter = ($nativeUiPrefixes | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'
    }
    [pscustomobject]@{
        Name = 'ui-platform'
        Filter = (($uiPlatformExclusions | ForEach-Object { "FullyQualifiedName!~$_" }) -join '&') + "|FullyQualifiedName~$platformEditorPrefix"
    }
)

if ($ListShards) {
    return @($shards | Where-Object { $Shard -ceq 'All' -or $_.Name -ceq $Shard })
}

function Add-FilterExclusions {
    param([string] $Filter, [string[]] $Prefixes)

    ($Filter -split '\|' | ForEach-Object {
        $alternative = $_
        foreach ($prefix in $Prefixes) {
            $alternative += "&FullyQualifiedName!~$prefix"
        }
        $alternative
    }) -join '|'
}

# Method-family filters keep every theory row together without changing hosted job identities.
$conversationActions = @(
    "${conversationPrefix}Native"
    "${conversationPrefix}Owned"
    "${conversationPrefix}Named"
)
$conversationCore = @('B', 'E', 'M') | ForEach-Object { "$conversationPrefix$_" }
$executionSplits = @{
    'ui-definitions' = @(
        @{ Name = 'ui-definition-dialogs'; Prefixes = @(
            'Kora.Windows.IntegrationTests.ArtifactDiscovery.CustomizationWindowTests.'
            'Kora.Windows.IntegrationTests.ArtifactDiscovery.PromptParameterWindowTests.'
        ); Exclusions = @() }
        @{ Name = 'ui-conversations'; Prefixes = @($conversationPrefix); Exclusions = ($conversationActions + $conversationCore) }
        @{ Name = 'ui-conversation-core'; Prefixes = $conversationCore; Exclusions = @() }
        @{ Name = 'ui-conversation-actions'; Prefixes = $conversationActions; Exclusions = @() }
    )
    'ui-platform' = @(
        @{ Name = 'ui-editors'; Prefixes = @($platformEditorPrefix); Exclusions = @() }
        @{ Name = 'ui-workflows'; Prefixes = @(
            'Kora.Windows.IntegrationTests.OwnedSkillAuthoringNativeTests.'
            'Kora.Windows.IntegrationTests.HostedClaudeSessionRequestTests.'
            'Kora.Windows.IntegrationTests.ClaudeUiContractTests.'
            'Kora.Windows.IntegrationTests.NewRequestControllerTests.'
        ); Exclusions = @() }
        @{ Name = 'ui-presentation'; Prefixes = @(
            'Kora.Windows.IntegrationTests.LocalFileWrappingWindowTests.'
            'Kora.Windows.IntegrationTests.AgentProfileConversationalAuthoringTests.'
            'Kora.Windows.IntegrationTests.SessionsMemoryFilterTests.'
            'Kora.Windows.IntegrationTests.AdaptiveInteractionTests.'
            'Kora.Windows.IntegrationTests.EvidenceLiteralFindTests.'
        ); Exclusions = @() }
    )
}
$partitions = @(
    foreach ($definition in $shards) {
        if ($executionSplits.ContainsKey($definition.Name)) {
            $splits = $executionSplits[$definition.Name]
            $excluded = @($splits.Prefixes | Sort-Object -Unique)
            [pscustomobject]@{
                Name = $definition.Name
                Shard = $definition.Name
                Filter = Add-FilterExclusions $definition.Filter $excluded
            }
            foreach ($split in $splits) {
                $filter = ($split.Prefixes | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'
                [pscustomobject]@{
                    Name = $split.Name
                    Shard = $definition.Name
                    Filter = Add-FilterExclusions $filter $split.Exclusions
                }
            }
        }
        else {
            [pscustomobject]@{
                Name = $definition.Name
                Shard = $definition.Name
                Filter = $definition.Filter
            }
        }
    }
)
$hostedAssignments = @{
    'storage-heavy' = 'storage-heavy'
    'ui-conversations' = 'storage-heavy'
    'ui-conversation-core' = 'storage-heavy'
    'storage-other' = 'storage-other'
    'ui-conversation-actions' = 'storage-other'
    'ui-definition-dialogs' = 'storage-other'
    'ui-definitions' = 'ui-definitions'
    'ui-editors' = 'ui-definitions'
    'native-ux' = 'native-ux'
    'ui-platform' = 'ui-platform'
    'ui-workflows' = 'ui-platform'
    'ui-presentation' = 'ui-platform'
}
foreach ($executionPartition in $partitions) {
    if (-not $hostedAssignments.ContainsKey($executionPartition.Name) -or
        $hostedAssignments[$executionPartition.Name] -cnotin $shards.Name) {
        throw "Windows partition $($executionPartition.Name) has no valid hosted assignment."
    }
    $executionPartition | Add-Member -NotePropertyName HostedShard -NotePropertyValue $hostedAssignments[$executionPartition.Name]
}
if ($Shard -cne 'All') {
    $partitions = @($partitions | Where-Object HostedShard -CEQ $Shard)
}
if ($Partition) {
    $partitions = @($partitions | Where-Object Name -CEQ $Partition)
}
if ($ListPartitions) {
    return $partitions
}

$dotnetPath = (Get-Command dotnet -CommandType Application, ExternalScript -ErrorAction Stop |
    Select-Object -First 1).Source
$moduleRoot = $null
if ($TestModuleDirectory) {
    $moduleRoot = [IO.Path]::GetFullPath($TestModuleDirectory, $repositoryRoot)
    $receipt = Get-Content -Raw -LiteralPath (Join-Path $moduleRoot 'kora-test-build.json') | ConvertFrom-Json
    if ($receipt.Version -ne 1 -or $ExpectedSourceRevision -cnotmatch '^[0-9a-f]{40}$' -or
        $receipt.SourceRevision -cne $ExpectedSourceRevision -or $receipt.Files.Count -eq 0) {
        throw 'The Windows test artifact does not match the admitted source revision.'
    }
    foreach ($file in $receipt.Files) {
        if ($file.Path -match '(^|[\\/])\.\.([\\/]|$)' -or [IO.Path]::IsPathRooted($file.Path) -or
            $file.Hash -cnotmatch '^[A-F0-9]{64}$') {
            throw 'The Windows test artifact contains an invalid file identity.'
        }
        $path = Join-Path $moduleRoot $file.Path
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $file.Hash) {
            throw "The Windows test artifact file failed integrity validation: $($file.Path)."
        }
    }
    if (@($receipt.Files | Where-Object Path -CEQ 'Kora.Windows.IntegrationTests.dll').Count -ne 1) {
        throw 'The Windows test artifact must identify exactly one test assembly.'
    }
}
$resultsRoot = Join-Path $resultsRoot ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null

$runs = @(
    $partitions | ForEach-Object -Parallel {
        $shardDirectory = Join-Path $using:resultsRoot $_.Name
        $logPath = Join-Path $shardDirectory 'console.log'
        New-Item -ItemType Directory -Path $shardDirectory -Force | Out-Null

        $arguments = @('test')
        if ($using:moduleRoot) {
            $arguments += @('--root-directory', $using:moduleRoot, '--test-modules', 'Kora.Windows.IntegrationTests.dll')
        }
        else {
            $arguments += @('--project', $using:projectPath, '--configuration', 'Release', '--runtime', 'win-x64', '--no-build')
        }
        $arguments += @(
            '--filter'
            $_.Filter
            '--max-threads'
            $using:MaxThreads
            '--results-directory'
            $shardDirectory
            '--report-trx'
            '--diagnostic'
            '--diagnostic-output-directory'
            $shardDirectory
        )

        try {
            & $using:dotnetPath @arguments *> $logPath
            $exitCode = $LASTEXITCODE
        }
        finally {
            foreach ($diagnostic in Get-ChildItem -LiteralPath $shardDirectory -Filter '*.diag' -File) {
                Add-Content -LiteralPath $logPath -Value "MTP diagnostic tail: $($diagnostic.Name)"
                Get-Content -LiteralPath $diagnostic.FullName -Tail 80 | Add-Content -LiteralPath $logPath
            }
        }

        [pscustomobject]@{
            Name = $_.Name
            ExitCode = $exitCode
            LogPath = $logPath
        }
    } -ThrottleLimit $partitions.Count
)

$failedShards = @()
foreach ($definition in $partitions) {
    $run = @($runs | Where-Object Name -CEQ $definition.Name)
    if ($run.Count -ne 1) {
        throw "Missing or duplicate Windows execution partition: $($definition.Name)."
    }
    $run = $run[0]
    Write-Host "::group::Windows integration tests ($($definition.Name))"
    Get-Content -LiteralPath $run.LogPath
    Write-Host '::endgroup::'

    if ($run.ExitCode -eq 0) {
        $directory = Split-Path -Parent $run.LogPath
        $reports = @(Get-ChildItem -LiteralPath $directory -Filter '*.trx' -File -Recurse)
        if ($reports.Count -ne 1) {
            throw "Windows partition $($run.Name) must produce exactly one fresh TRX report."
        }
        [xml] $report = Get-Content -Raw -LiteralPath $reports[0].FullName
        $results = @($report.TestRun.Results.UnitTestResult)
        $counters = $report.TestRun.ResultSummary.Counters
        if ($results.Count -eq 0 -or $results.Count -ne [int] $counters.total -or
            [int] $counters.passed -ne $results.Count -or
            @($results | Where-Object outcome -CNE 'Passed').Count -gt 0) {
            throw "Windows partition $($run.Name) did not produce complete, nonempty passing test evidence."
        }
    }

    if ($run.ExitCode -ne 0) {
        $failedShards += $run.Name
    }
}

if ($failedShards.Count -gt 0) {
    throw "Windows integration test shards failed: $($failedShards -join ', ')."
}
