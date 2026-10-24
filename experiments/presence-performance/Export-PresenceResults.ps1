[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InputDirectory,

    [Parameter(Mandatory)]
    [string]$OutputDirectory,

    [ValidateRange(0, 10000)]
    [int]$ExpectedRuns = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$inputRoot = (Resolve-Path -LiteralPath $InputDirectory).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

function Get-Median {
    param([double[]]$Values)
    if ($Values.Count -eq 0) {
        throw 'Cannot summarize an empty measurement set.'
    }
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 0) {
        return ($sorted[$middle - 1] + $sorted[$middle]) / 2
    }
    return $sorted[$middle]
}

$measurements = @(
    Get-ChildItem -LiteralPath $inputRoot -Filter '*.json' |
        Where-Object { $_.Name -match '^(Active|Lifecycle|Offscreen|Soak|Qualifying|TopmostLifecycle)-\d+-' -and $_.Name -notlike '*.system.json' } |
        Sort-Object Name |
        ForEach-Object {
            $file = $_
            $run = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
            $systemPath = Join-Path $inputRoot ($file.BaseName + '.system.json')
            $system = Get-Content -LiteralPath $systemPath -Raw | ConvertFrom-Json
            # Timer deadlines may round below the requested duration; retain the actual measured denominator.
            if (-not [double]::IsFinite($run.CpuMillisecondsPerSecond) -or
                $run.ElapsedSeconds -lt ($run.Options.Seconds - 0.033)) {
                throw "Invalid or incomplete benchmark period in $($file.Name)."
            }
            if ($run.Options.Offscreen -and $run.ForcedRendersPerSecond -lt 29) {
                throw "Offscreen workload missed its minimum measured draw rate in $($file.Name)."
            }
            if ($run.Options.Variant -eq 'native-dib' -and $run.Options.Scenario -in @('default', 'maximum', 'reactive') `
                -and $run.DrawsPerSecond -lt 29) {
                throw "Native renderer missed its submission budget in $($file.Name)."
            }
            [pscustomobject]@{
                Id = $file.BaseName
                Suite = $file.Name.Split('-')[0]
                Result = $run
                SharedDwm = $system
            }
        }
)
if ($ExpectedRuns -ne 0 -and $measurements.Count -ne $ExpectedRuns) {
    throw "Expected $ExpectedRuns completed runs; found $($measurements.Count)."
}
$measurements | ConvertTo-Json -Depth 12 |
    Set-Content -LiteralPath (Join-Path $outputRoot 'runs.json') -Encoding utf8

$summary = @(
    $measurements | Group-Object {
        "$($_.Suite)|$($_.Result.Options.Scenario)|$($_.Result.Options.Variant)|$($_.Result.Options.Backend)"
    } | Sort-Object Name | ForEach-Object {
        $runs = @($_.Group.Result)
        $options = $runs[0].Options
        [pscustomobject][ordered]@{
            Suite = $_.Group[0].Suite
            Scenario = $options.Scenario
            Variant = $options.Variant
            Backend = $options.Backend
            Samples = $runs.Count
            CpuMsPerSecondMedian = Get-Median $runs.CpuMillisecondsPerSecond
            CpuMsPerSecondMin = ($runs.CpuMillisecondsPerSecond | Measure-Object -Minimum).Minimum
            CpuMsPerSecondMax = ($runs.CpuMillisecondsPerSecond | Measure-Object -Maximum).Maximum
            DrawsPerSecondMedian = Get-Median $runs.ObservedWindowRendersPerSecond
            UpdatesPerSecondMedian = Get-Median $runs.TicksPerSecond
            ForcedDrawsPerSecondMedian = Get-Median $runs.ForcedRendersPerSecond
            TickP95MillisecondsMedian = Get-Median $runs.TickIntervalP95Milliseconds
            AllocatedBytesPerSecondMedian = Get-Median $runs.AllocatedBytesPerSecond
            WorkingSetMiBMedian = Get-Median $runs.WorkingSetMiB
            PrivateMiBMedian = Get-Median $runs.PrivateMiB
            CacheBytes = $runs[0].CacheBytes
            StartupCpuMillisecondsMedian = Get-Median $runs.StartupCpuMilliseconds
            StartupWallMillisecondsMedian = Get-Median $runs.StartupWallMilliseconds
            SharedDwmCpuMsPerSecondMedian = Get-Median $_.Group.SharedDwm.DwmCpuMillisecondsPerSecond
            WorkingSetGrowthMiBMedian = Get-Median @($runs | ForEach-Object {
                $_.ResourcesAfter.WorkingSetMiB - $_.ResourcesBefore.WorkingSetMiB
            })
            GdiObjectGrowthMax = ($runs | ForEach-Object {
                $_.ResourcesAfter.GdiObjects - $_.ResourcesBefore.GdiObjects
            } | Measure-Object -Maximum).Maximum
            UserObjectGrowthMax = ($runs | ForEach-Object {
                $_.ResourcesAfter.UserObjects - $_.ResourcesBefore.UserObjects
            } | Measure-Object -Maximum).Maximum
        }
    }
)
$summary | Export-Csv -LiteralPath (Join-Path $outputRoot 'summary.csv') -NoTypeInformation -Encoding utf8

$manifest = [ordered]@{
    GeneratedUtc = [DateTimeOffset]::UtcNow
    Runs = $measurements.Count
    MeasuredSeconds = ($measurements.Result.ElapsedSeconds | Measure-Object -Sum).Sum
    WarmupSeconds = ($measurements.Result.Options.WarmupSeconds | Measure-Object -Sum).Sum
    RawDirectory = $inputRoot
    RunFiles = @($measurements.Id)
    MachineMetadata = @(
        Get-ChildItem -LiteralPath $inputRoot -Filter '*-machine.json' | ForEach-Object {
            Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
        }
    )
    Warning = 'Window draw callbacks/submissions are not proof of physical presentation or display latency. Shared DWM intervals include startup and warmup. Offscreen cases also retain ordinary window rendering.'
}
$manifest | ConvertTo-Json -Depth 12 |
    Set-Content -LiteralPath (Join-Path $outputRoot 'manifest.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $inputRoot 'fidelity.json') -Destination (Join-Path $outputRoot 'fidelity.json') -Force
Write-Output "Exported $($measurements.Count) completed runs and $($summary.Count) summary rows."
$summary | Select-Object Suite, Scenario, Variant, Backend, CpuMsPerSecondMedian, DrawsPerSecondMedian, WorkingSetMiBMedian |
    Format-Table -AutoSize
