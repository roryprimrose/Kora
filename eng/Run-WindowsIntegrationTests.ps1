[CmdletBinding()]
param(
    [Parameter()]
    [ValidateRange(1, 64)]
    [int] $MaxThreads = 1,

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string] $ResultsDirectory = '.net-test-artifacts\windows'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj'
$resultsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $ResultsDirectory))

$shards = @(
    [pscustomobject]@{
        Name = 'storage-heavy-a'
        Filter = 'FullyQualifiedName~Kora.Windows.IntegrationTests.Storage.WindowsSqliteSessionAttachmentTests|FullyQualifiedName~Kora.Windows.IntegrationTests.Storage.WindowsSqliteMemoryTests|FullyQualifiedName~Kora.Windows.IntegrationTests.Storage.WindowsSqliteExactGrantFilterTests'
    }
    [pscustomobject]@{
        Name = 'storage-heavy-b'
        Filter = 'FullyQualifiedName~Kora.Windows.IntegrationTests.Storage.WindowsSqliteConversationTests|FullyQualifiedName~Kora.Windows.IntegrationTests.Storage.WindowsSqliteSessionQueueTests'
    }
    [pscustomobject]@{
        Name = 'storage-other'
        Filter = 'FullyQualifiedName~Kora.Windows.IntegrationTests.Storage.&FullyQualifiedName!~Kora.Windows.IntegrationTests.Storage.WindowsSqliteSessionAttachmentTests&FullyQualifiedName!~Kora.Windows.IntegrationTests.Storage.WindowsSqliteConversationTests&FullyQualifiedName!~Kora.Windows.IntegrationTests.Storage.WindowsSqliteSessionQueueTests&FullyQualifiedName!~Kora.Windows.IntegrationTests.Storage.WindowsSqliteMemoryTests&FullyQualifiedName!~Kora.Windows.IntegrationTests.Storage.WindowsSqliteExactGrantFilterTests'
    }
    [pscustomobject]@{
        Name = 'ui-heavy'
        Filter = 'FullyQualifiedName~Kora.Windows.IntegrationTests.ArtifactDiscovery.OwnedDefinitionEditorWindowTests|FullyQualifiedName~Kora.Windows.IntegrationTests.NativeUx.NativeUxFixtureContractTests'
    }
    [pscustomobject]@{
        Name = 'ui-platform'
        Filter = 'FullyQualifiedName!~Kora.Windows.IntegrationTests.Storage.&FullyQualifiedName!~Kora.Windows.IntegrationTests.ArtifactDiscovery.OwnedDefinitionEditorWindowTests&FullyQualifiedName!~Kora.Windows.IntegrationTests.NativeUx.NativeUxFixtureContractTests'
    }
)

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
foreach ($shard in $shards) {
    $run = $runs | Where-Object Name -EQ $shard.Name
    Write-Host "::group::Windows integration tests ($($shard.Name))"
    Get-Content -LiteralPath $run.LogPath
    Write-Host '::endgroup::'

    if ($run.ExitCode -ne 0) {
        $failedShards += $run.Name
    }
}

if ($failedShards.Count -gt 0) {
    throw "Windows integration test shards failed: $($failedShards -join ', ')."
}
