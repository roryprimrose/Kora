[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputDirectory,

    [ValidateSet('Active', 'Lifecycle', 'Offscreen', 'Soak', 'Qualifying', 'TopmostLifecycle')]
    [string]$Suite = 'Active',

    [ValidateRange(1, 10)]
    [int]$Repetitions = 3,

    [ValidateRange(2, 300)]
    [int]$Seconds = 15,

    [ValidateRange(1, 10)]
    [int]$WarmupSeconds = 3,

    [ValidateRange(1, 10)]
    [int]$FirstRepetition = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$executable = Join-Path $PSScriptRoot 'bin\Release\net10.0-windows10.0.19041.0\Presence.Performance.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'Build the Release benchmark before running the evaluation.'
}

$sessionId = (Get-Process -Id $PID).SessionId
$dwm = @(Get-Process -Name dwm | Where-Object SessionId -EQ $sessionId)
if ($dwm.Count -ne 1) {
    throw "Expected exactly one DWM process in benchmark session $sessionId."
}
$dwmId = $dwm[0].Id
$metadataPath = Join-Path $outputRoot "$Suite-machine.json"
$metadata = [ordered]@{
    Utc = [DateTimeOffset]::UtcNow
    Suite = $Suite
    MeasuredSeconds = $Seconds
    WarmupSeconds = $WarmupSeconds
    Repetitions = $Repetitions
    BaseCommit = (git -C $PSScriptRoot rev-parse HEAD)
    DwmId = $dwmId
    SessionId = $sessionId
    Processor = @(Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors)
    Video = @(Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion, CurrentHorizontalResolution, CurrentVerticalResolution)
    Memory = Get-CimInstance Win32_OperatingSystem | Select-Object TotalVisibleMemorySize, FreePhysicalMemory
    SourceHashes = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' |
        Get-FileHash -Algorithm SHA256 | Select-Object Path, Hash)
    BaselineHash = Get-FileHash -LiteralPath (Join-Path $PSScriptRoot '..\..\src\Kora\Controls\PresenceControl.cs') -Algorithm SHA256 |
        Select-Object Path, Hash
}
$metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $metadataPath -Encoding utf8

$cases = [Collections.Generic.List[object]]::new()
switch ($Suite) {
    'Qualifying' {
        foreach ($scenario in @('default', 'maximum', 'reactive')) {
            $cases.Add([pscustomobject]@{ Variant = 'baseline'; Backend = 'default'; Scenario = $scenario; Offscreen = $false })
            $cases.Add([pscustomobject]@{ Variant = 'baseline'; Backend = 'software'; Scenario = $scenario; Offscreen = $false })
            $cases.Add([pscustomobject]@{ Variant = 'compositor'; Backend = 'software'; Scenario = $scenario; Offscreen = $false })
            $cases.Add([pscustomobject]@{ Variant = 'native-dib'; Backend = 'default'; Scenario = $scenario; Offscreen = $false })
        }
    }
    'TopmostLifecycle' {
        foreach ($scenario in @('static', 'hidden')) {
            foreach ($variant in @('baseline', 'pr247', 'native-dib')) {
                $cases.Add([pscustomobject]@{ Variant = $variant; Backend = 'default'; Scenario = $scenario; Offscreen = $false })
            }
            $cases.Add([pscustomobject]@{ Variant = 'compositor'; Backend = 'software'; Scenario = $scenario; Offscreen = $false })
        }
    }
    'Active' {
        foreach ($scenario in @('default', 'maximum', 'reactive')) {
            $cases.Add([pscustomobject]@{ Variant = 'baseline'; Backend = 'default'; Scenario = $scenario; Offscreen = $false })
            foreach ($variant in @('baseline', 'cached', 'skia', 'compositor')) {
                $cases.Add([pscustomobject]@{ Variant = $variant; Backend = 'software'; Scenario = $scenario; Offscreen = $false })
            }
            $cases.Add([pscustomobject]@{ Variant = 'native-dib'; Backend = 'default'; Scenario = $scenario; Offscreen = $false })
        }
    }
    'Lifecycle' {
        foreach ($scenario in @('static', 'hidden')) {
            foreach ($variant in @('baseline', 'pr247')) {
                $cases.Add([pscustomobject]@{ Variant = $variant; Backend = 'default'; Scenario = $scenario; Offscreen = $false })
            }
            foreach ($variant in @('cached', 'compositor')) {
                $cases.Add([pscustomobject]@{ Variant = $variant; Backend = 'software'; Scenario = $scenario; Offscreen = $false })
            }
            $cases.Add([pscustomobject]@{ Variant = 'native-dib'; Backend = 'default'; Scenario = $scenario; Offscreen = $false })
        }
    }
    'Offscreen' {
        foreach ($variant in @('baseline', 'cached', 'skia', 'trajectory', 'bitmap')) {
            $cases.Add([pscustomobject]@{ Variant = $variant; Backend = 'software'; Scenario = 'maximum'; Offscreen = $true })
        }
    }
    'Soak' {
        $cases.Add([pscustomobject]@{ Variant = 'native-dib'; Backend = 'default'; Scenario = 'reactive'; Offscreen = $false })
        $cases.Add([pscustomobject]@{ Variant = 'compositor'; Backend = 'software'; Scenario = 'reactive'; Offscreen = $false })
    }
}

for ($repetition = $FirstRepetition; $repetition -lt ($FirstRepetition + $Repetitions); $repetition++) {
    # Rotate order across passes rather than always giving the same renderer the same host-load position.
    for ($offset = 0; $offset -lt $cases.Count; $offset++) {
        $case = $cases[($offset + $repetition * 5) % $cases.Count]
        $name = "$Suite-$repetition-$($case.Scenario)-$($case.Variant)-$($case.Backend)"
        $resultPath = Join-Path $outputRoot "$name.json"
        if (Test-Path -LiteralPath $resultPath) {
            throw "Refusing to overwrite an existing measurement: $resultPath"
        }
        $before = Get-CimInstance Win32_PerfRawData_PerfProc_Process -Filter "IDProcess = $dwmId"
        Write-Output "[$Suite pass $repetition] $($case.Scenario) / $($case.Variant) / $($case.Backend)"
        $startInfo = [Diagnostics.ProcessStartInfo]::new($executable)
        $startInfo.UseShellExecute = $false
        foreach ($argument in @('--variant', $case.Variant, '--scenario', $case.Scenario, '--backend', $case.Backend,
            '--seconds', $Seconds, '--warmup', $WarmupSeconds, '--offscreen', $case.Offscreen.ToString().ToLowerInvariant(),
            '--output', $resultPath)) {
            $startInfo.ArgumentList.Add([string]$argument)
        }
        $child = [Diagnostics.Process]::Start($startInfo)
        try {
            if (-not $child.WaitForExit(($Seconds + $WarmupSeconds + 45) * 1000)) {
                $child.Kill($true)
                $child.WaitForExit()
                throw "Benchmark exceeded its bounded lifetime: $name"
            }
            if ($child.ExitCode -ne 0) {
                throw "Benchmark failed with exit code $($child.ExitCode) : $name"
            }
        }
        finally {
            $child.Dispose()
        }
        $after = Get-CimInstance Win32_PerfRawData_PerfProc_Process -Filter "IDProcess = $dwmId"
        if ($null -eq $before -or $null -eq $after) {
            throw 'DWM accounting became unavailable; the run cannot silently omit compositor accounting.'
        }
        $span = ([double]$after.Timestamp_Sys100NS - [double]$before.Timestamp_Sys100NS) / 10000000
        $system = [ordered]@{
            DwmId = $dwmId
            FullChildLifetimeSeconds = $span
            DwmCpuMillisecondsPerSecond = ([double]$after.PercentProcessorTime - [double]$before.PercentProcessorTime) / 10000 / $span
            DwmPrivateMiB = [double]$after.WorkingSetPrivate / 1048576
            Note = 'Shared session DWM counter across startup, warmup and measurement; not exclusive attribution, not a matched steady-state sum.'
        }
        $system | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot "$name.system.json") -Encoding utf8
    }
}
