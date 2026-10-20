[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('All', 'storage-heavy', 'storage-other', 'ui-definitions', 'native-ux', 'ui-platform')]
    [string] $Shard = 'All',

    [Parameter()]
    [switch] $ListShards,

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
$resultsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $ResultsDirectory))

# Balance slow headless UI classes across processes without parallelizing the process-wide UI thread.
$definitionUiPrefixes = @(
    'Kora.Windows.IntegrationTests.ArtifactDiscovery.'
    'Kora.Windows.IntegrationTests.EvidenceLiteralFindTests.'
    'Kora.Windows.IntegrationTests.BoundedSurfaceAccessibilityTests.'
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
        Filter = ($definitionUiPrefixes | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'
    }
    [pscustomobject]@{
        Name = 'native-ux'
        Filter = ($nativeUiPrefixes | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'
    }
    [pscustomobject]@{
        Name = 'ui-platform'
        Filter = ($uiPlatformExclusions | ForEach-Object { "FullyQualifiedName!~$_" }) -join '&'
    }
)

if ($Shard -cne 'All') {
    $shards = @($shards | Where-Object Name -EQ $Shard)
}
if ($ListShards) {
    return $shards
}

New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null

$runs = @(
    $shards | ForEach-Object -Parallel {
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

        & dotnet @arguments *> $logPath

        [pscustomobject]@{
            Name = $_.Name
            ExitCode = $LASTEXITCODE
            LogPath = $logPath
        }
    } -ThrottleLimit $shards.Count
)

$failedShards = @()
foreach ($definition in $shards) {
    $run = $runs | Where-Object Name -EQ $definition.Name
    Write-Host "::group::Windows integration tests ($($definition.Name))"
    Get-Content -LiteralPath $run.LogPath
    Write-Host '::endgroup::'

    if ($run.ExitCode -ne 0) {
        $failedShards += $run.Name
    }
}

if ($failedShards.Count -gt 0) {
    throw "Windows integration test shards failed: $($failedShards -join ', ')."
}
