[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $OutputDirectory,
    [string] $BudgetsPath,
    [ValidateRange(30, 1000)]
    [int] $Trials = 30,
    [string] $RuntimeVersion,
    [switch] $ComparisonRuntime,
    [switch] $ExclusiveRuntime,
    [switch] $CompareSampling,
    [switch] $CompareStreaming,
    [switch] $ObserverControl,
    [ValidateRange(1, [int]::MaxValue)]
    [int] $ServerProcessId,
    [ValidateRange(1, [long]::MaxValue)]
    [long] $ServerStartedUtcTicks,
    [switch] $ValidateOnly
)

$ErrorActionPreference = 'Stop'
if (@(@($CompareSampling, $CompareStreaming, $ObserverControl) | Where-Object { $_ }).Count -gt 1) {
    throw 'Select one mode: sampling comparison, streaming comparison or bounded observer control.'
}
if ($ServerStartedUtcTicks -and -not $ServerProcessId) { throw 'ServerStartedUtcTicks requires ServerProcessId.' }
if ($ObserverControl -and $PSBoundParameters.ContainsKey('Trials')) { throw 'ObserverControl performs exactly two requests; Trials is not applicable.' }
$pinSource = Join-Path $PSScriptRoot '..\..\src\Kora.Windows\Dependencies\WindowsOllamaSetupService.cs'
$pinMatches = @(Select-String -LiteralPath $pinSource -Pattern 'public const string PackageVersion = "([^"]+)";')
if ($pinMatches.Count -ne 1) { throw 'Could not identify the authoritative production runtime pin.' }
$pin = $pinMatches[0].Matches[0].Groups[1].Value
if (-not $RuntimeVersion) { $RuntimeVersion = $pin }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a new output directory; historical evidence is never overwritten.' }
if (-not $ValidateOnly) {
    if (-not $ExclusiveRuntime) { throw 'Live qualification requires explicit exclusive-runtime and residency-change approval.' }
    if (-not $ServerProcessId) { throw 'Live qualification requires an explicitly selected ServerProcessId; names do not establish ownership.' }
    if (-not $BudgetsPath -or -not (Test-Path -LiteralPath $BudgetsPath -PathType Leaf)) {
        throw 'Live qualification requires a pre-agreed timing-budget JSON file.'
    }
    $budgets = [IO.Path]::GetFullPath($BudgetsPath)
    if ([bool]$ComparisonRuntime -ne ($RuntimeVersion -ne $pin)) {
        throw 'A changed runtime requires ComparisonRuntime; no automatic compatibility or production-pin change is implied.'
    }
    $version = $null
    if (-not [Version]::TryParse($RuntimeVersion, [ref]$version) -or $RuntimeVersion.Trim() -ne $RuntimeVersion) {
        throw 'An exact numerical runtime version is required.'
    }
}

$repo = (& git -C $PSScriptRoot rev-parse --show-toplevel | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not identify source checkout.' }
$revision = (& git -C $repo rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not identify source revision.' }
$status = @(& git -C $repo status --porcelain)
if ($LASTEXITCODE -ne 0) { throw 'Could not identify working-tree changes.' }
[IO.Directory]::CreateDirectory($output) | Out-Null
$receipt = [ordered]@{
    Schema = 'Kora.R02.AutomatedQualification.v1'
    StartedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    BaseRevision = $revision
    WorkingTreeChanges = $status
    ValidateOnly = [bool]$ValidateOnly
    CompareSampling = [bool]$CompareSampling
    CompareStreaming = [bool]$CompareStreaming
    ObserverControl = [bool]$ObserverControl
    RuntimeVersion = $RuntimeVersion
    ComparisonRuntime = [bool]$ComparisonRuntime
    ExclusiveRuntimeApproved = [bool]$ExclusiveRuntime
    RequestedServerProcessId = $ServerProcessId
    RequestedServerStartedUtcTicks = $ServerStartedUtcTicks
    State = 'Started'
    Scope = 'Synthetic CPU-only experiment; not physical-floor, offline, human-rubric or admitted-host acceptance.'
}
$exitCode = 1
Push-Location $repo
try {
    # Package acquisition is intentionally separate; this runner never installs or restores.
    & (Join-Path $PSScriptRoot 'Run-Validation.ps1') `
        -OutputDirectory (Join-Path $output 'validation') -SkipObserve -NoRestore
    if (-not $ValidateOnly) {
        Copy-Item -LiteralPath $budgets -Destination (Join-Path $output 'budgets.json') -ErrorAction Stop
        $arguments = @('run', '--project', (Join-Path $PSScriptRoot 'Proof.csproj'),
            '-c', 'Release', '--no-build', '--no-restore', '--',
            $(if ($ObserverControl) { 'observer-control' } elseif ($CompareSampling) { 'compare-sampling' } elseif ($CompareStreaming) { 'compare-streaming' } else { 'qualify' }),
            '--output', (Join-Path $output 'measurement.json'),
            '--budgets', (Join-Path $output 'budgets.json'), '--exclusive-runtime',
            '--runtime-version', $RuntimeVersion, '--server-pid', "$ServerProcessId")
        if (-not $ObserverControl) { $arguments += @('--trials', "$Trials") }
        if ($ServerStartedUtcTicks) { $arguments += @('--server-started-ticks', "$ServerStartedUtcTicks") }
        if ($ComparisonRuntime) { $arguments += '--comparison-runtime' }
        $receipt.Command = $arguments
        Write-Host "Live CPU-only comparison=$([bool]$ComparisonRuntime), runtime=$RuntimeVersion; Ctrl+C requests stop and owned-model cleanup."
        & dotnet @arguments 2>&1 | Tee-Object -FilePath (Join-Path $output 'measurement.log') | ForEach-Object { Write-Host $_ }
        $exitCode = $LASTEXITCODE
        $receipt.MeasurementExitCode = $exitCode
        $measurement = Join-Path $output 'measurement.json'
        if (Test-Path -LiteralPath $measurement) {
            $data = Get-Content -LiteralPath $measurement -Raw | ConvertFrom-Json
            $receipt.MeasurementSha256 = (Get-FileHash -LiteralPath $measurement -Algorithm SHA256).Hash
            $receipt.AutomatedChecksPassed = $data.automatedQualityAndClientChecksPassed
            if ($ObserverControl) { $receipt.AutomatedChecksPassed = $data.observerControlPassed }
            $receipt.TimingStatus = $data.timingAssessment.status
            $receipt.Cleanup = $data.cleanup
            $receipt.QualificationStatus = 'Incomplete; see measurement timingAssessment.remainingGates.'
            if ($data.humanReviewWorksheet) {
                ConvertTo-Json -InputObject @($data.humanReviewWorksheet) -Depth 8 |
                    Set-Content -LiteralPath (Join-Path $output 'human-review.json') -Encoding utf8
            }
        }
        $receipt.State = if ($exitCode -eq 0) { 'Automated checks passed; qualification incomplete.' } else { 'Blocked or failed; retain all evidence.' }
    } else {
        $exitCode = 0
        $receipt.State = 'Deterministic validation passed; no endpoint observation or generation.'
    }
} catch {
    $receipt.State = 'Failed'
    $receipt.Error = $_.Exception.Message
    throw
} finally {
    $receipt.FinishedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $receipt.ExitCode = $exitCode
    $receipt | ConvertTo-Json -Depth 12 |
        Set-Content -LiteralPath (Join-Path $output 'qualification.json') -Encoding utf8
    Pop-Location
    Write-Host "Qualification receipt: $(Join-Path $output 'qualification.json'); exit $exitCode"
}
if ($exitCode -ne 0) { throw "Automated measurement exited $exitCode; inspect evidence before any replay." }
