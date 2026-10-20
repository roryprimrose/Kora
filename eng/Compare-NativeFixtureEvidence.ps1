[CmdletBinding()]
param(
    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string] $ResultsDirectory = 'artifacts\native-fixture-evidence',

    [Parameter()]
    [switch] $ListCases
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$cases = @(
    'FullyQualifiedName~Kora.Windows.IntegrationTests.NativeReaderTextScaleTests.'
    'FullyQualifiedName~Kora.Windows.IntegrationTests.SpeechCaptionDisplayTests.ProductionCaptionPresentationUsesConfirmedWorkflowForNewAndOpenWindowsWithoutAudio'
    'FullyQualifiedName~Kora.Windows.IntegrationTests.SpeechCaptionDisplayTests.RealPreferenceEvidenceFailureRetiresOpenAndNewCaptionsWithoutDefaultOrResurrection'
    'FullyQualifiedName~Kora.Windows.IntegrationTests.NativeUx.NativeUxFixtureContractTests.Exact_grants_seed_genuine_authority_page_passively_fence_staleness_and_deny_after_single_revoke'
)
if ($ListCases) { return $cases }

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj'
$resultsRoot = [IO.Path]::GetFullPath($ResultsDirectory, $root)
New-Item -ItemType Directory -Path $resultsRoot -ErrorAction Stop | Out-Null
$variable = 'KORA_TEST_NATIVE_EVIDENCE_MODE'
$saved = [Environment]::GetEnvironmentVariable($variable)
$runs = @()
$order = @('synchronous', 'buffered', 'buffered', 'synchronous')
try {
    foreach ($index in 0..3) {
        $mode = $order[$index]
        $runDirectory = Join-Path $resultsRoot "$($index + 1)-$mode"
        New-Item -ItemType Directory -Path $runDirectory | Out-Null
        [Environment]::SetEnvironmentVariable($variable, $mode)
        $arguments = @(
            'test'
            '--project'
            $project
            '--configuration'
            'Release'
            '--runtime'
            'win-x64'
            '--no-build'
            '--filter'
            ($cases -join '|')
            '--max-threads'
            '2'
            '--minimum-expected-tests'
            '17'
            '--results-directory'
            $runDirectory
            '--report-trx'
        )
        Write-Host "::group::Native fixture evidence $($index + 1)/4 ($mode)"
        $timer = [Diagnostics.Stopwatch]::StartNew()
        try {
            & dotnet @arguments 2>&1 | Tee-Object -FilePath (Join-Path $runDirectory 'console.log')
            $exitCode = $LASTEXITCODE
        }
        finally {
            $timer.Stop()
            Write-Host '::endgroup::'
        }
        $runs += [pscustomobject]@{
            order = $index + 1
            mode = $mode
            elapsedSeconds = $timer.Elapsed.TotalSeconds
            exitCode = $exitCode
        }
        $runs | ConvertTo-Json -AsArray | Set-Content -LiteralPath (Join-Path $resultsRoot 'runs.json')
        if ($exitCode -ne 0) {
            throw "Native fixture evidence comparison failed during $mode run $($index + 1), exit code $exitCode."
        }
    }
}
finally {
    [Environment]::SetEnvironmentVariable($variable, $saved)
}

$synchronous = ($runs | Where-Object mode -EQ 'synchronous').elapsedSeconds | Measure-Object -Average
$buffered = ($runs | Where-Object mode -EQ 'buffered').elapsedSeconds | Measure-Object -Average
[pscustomobject]@{
    sourceRevision = $env:GITHUB_SHA
    processorCount = [Environment]::ProcessorCount
    synchronousMeanSeconds = $synchronous.Average
    bufferedMeanSeconds = $buffered.Average
    savedMeanSeconds = $synchronous.Average - $buffered.Average
    improvementPercent = 100 * ($synchronous.Average - $buffered.Average) / $synchronous.Average
    casesPerRun = 17
    runs = $runs
} | ConvertTo-Json -Depth 4 | Tee-Object -FilePath (Join-Path $resultsRoot 'summary.json')
