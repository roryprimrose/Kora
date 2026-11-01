[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('All', 'storage-heavy', 'storage-other', 'ui-definitions', 'native-ux', 'ui-platform')]
    [string] $Shard = 'All',

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

if ($Shard -cne 'All') {
    $shards = @($shards | Where-Object Name -EQ $Shard)
}
if ($ListShards) {
    return $shards
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
$executionSplits = @{
    'ui-definitions' = @(
        @{ Name = 'ui-definition-dialogs'; Prefixes = @(
            'Kora.Windows.IntegrationTests.ArtifactDiscovery.CustomizationWindowTests.'
            'Kora.Windows.IntegrationTests.ArtifactDiscovery.PromptParameterWindowTests.'
        ); Exclusions = @() }
        @{ Name = 'ui-conversations'; Prefixes = @($conversationPrefix); Exclusions = $conversationActions }
        @{ Name = 'ui-conversation-actions'; Prefixes = $conversationActions; Exclusions = @() }
    )
    'ui-platform' = @(
        @{ Name = 'ui-editors'; Prefixes = @($platformEditorPrefix); Exclusions = @() }
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
if ($ListPartitions) {
    return $partitions
}

$dotnetPath = (Get-Command dotnet -CommandType Application, ExternalScript -ErrorAction Stop |
    Select-Object -First 1).Source
$resultsRoot = Join-Path $resultsRoot ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null

$runs = @(
    $partitions | ForEach-Object -Parallel {
        $shardDirectory = Join-Path $using:resultsRoot $_.Name
        $logPath = Join-Path $shardDirectory 'console.log'
        New-Item -ItemType Directory -Path $shardDirectory -Force | Out-Null

        $arguments = @(
            'test'
            '--project'
            $using:projectPath
            '--configuration'
            'Release'
            '--runtime'
            'win-x64'
            '--no-build'
            '--filter'
            $_.Filter
            '--max-threads'
            $using:MaxThreads
            '--results-directory'
            $shardDirectory
            '--report-trx'
        )

        & $using:dotnetPath @arguments *> $logPath
        $exitCode = $LASTEXITCODE

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
