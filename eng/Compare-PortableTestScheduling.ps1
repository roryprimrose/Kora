[CmdletBinding()]
param(
    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string] $ResultsDirectory = 'artifacts\portable-test-scheduling'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'Kora.UnitTests.slnx'
$resultsRoot = [IO.Path]::GetFullPath($ResultsDirectory, $root)
New-Item -ItemType Directory -Path $resultsRoot -ErrorAction Stop | Out-Null
$runs = @()
# Counterbalance run order rather than always giving parallel execution the warmer filesystem cache.
$order = @('sequential', 'parallel', 'parallel', 'sequential')

foreach ($index in 0..3) {
    $mode = $order[$index]
    $runDirectory = Join-Path $resultsRoot "$($index + 1)-$mode"
    $selections = @([pscustomobject]@{ option = '--solution'; path = $solution })
    if ($mode -eq 'sequential') {
        $selections = @(
            'Kora.Core.UnitTests', 'Kora.Application.UnitTests',
            'Kora.Tools.UnitTests', 'Kora.Definitions.UnitTests'
        ) | ForEach-Object {
            [pscustomobject]@{ option = '--project'; path = Join-Path (Join-Path $root 'tests') (Join-Path $_ "$_.csproj") }
        }
    }
    New-Item -ItemType Directory -Path $runDirectory | Out-Null
    Write-Host "::group::Portable test comparison $($index + 1)/4 ($mode)"
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        foreach ($selection in $selections) {
            $arguments = @(
                'test'
                $selection.option
                $selection.path
                '--configuration'
                'Release'
                '--no-build'
                '--results-directory'
                (Join-Path $runDirectory 'results')
                '--results-directory-layout'
                'per-module'
                '--report-trx'
                '--coverlet'
                '--coverlet-output-format'
                'cobertura'
                '--coverlet-file-prefix'
                'unit'
            )
            & dotnet @arguments 2>&1 | Tee-Object -FilePath (Join-Path $runDirectory 'console.log') -Append
            $exitCode = $LASTEXITCODE
            if ($exitCode -ne 0) { break }
        }
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
        throw "Portable test comparison failed during $mode run $($index + 1), exit code $exitCode."
    }
}

$sequential = ($runs | Where-Object mode -EQ 'sequential').elapsedSeconds | Measure-Object -Average
$parallel = ($runs | Where-Object mode -EQ 'parallel').elapsedSeconds | Measure-Object -Average
$summary = [pscustomobject]@{
    sourceRevision = $env:GITHUB_SHA
    processorCount = [Environment]::ProcessorCount
    sequentialMeanSeconds = $sequential.Average
    parallelMeanSeconds = $parallel.Average
    savedMeanSeconds = $sequential.Average - $parallel.Average
    improvementPercent = 100 * ($sequential.Average - $parallel.Average) / $sequential.Average
    runs = $runs
}
$summary | ConvertTo-Json -Depth 4 | Tee-Object -FilePath (Join-Path $resultsRoot 'summary.json')
