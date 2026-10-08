[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $OutputDirectory,
    [switch] $ExpectUnavailable,
    [switch] $SkipObserve,
    [ValidateRange(1, [int]::MaxValue)]
    [int] $ServerProcessId,
    [switch] $NoRestore
)

$ErrorActionPreference = 'Stop'
if ($SkipObserve -and $ExpectUnavailable) { throw 'Cannot expect an unavailable observation when observation is skipped.' }
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a new output directory; evidence is never overwritten.' }
[IO.Directory]::CreateDirectory($output) | Out-Null
$project = Join-Path $PSScriptRoot 'Proof.csproj'
$config = Join-Path $PSScriptRoot 'NuGet.Config'
$revision = (& git rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not identify repository revision.' }
$commands = [Collections.Generic.List[object]]::new()
function Invoke-Dotnet([string[]] $Arguments, [int] $ExpectedExit = 0) {
    $lines = @(& dotnet @Arguments 2>&1 | ForEach-Object { Write-Host $_; $_ })
    $code = $LASTEXITCODE
    $commands.Add(@{ Tool = 'dotnet'; Arguments = $Arguments; ExitCode = $code; ExpectedExitCode = $ExpectedExit; Output = ($lines | Out-String).Trim() })
    if ($code -ne $ExpectedExit) {
        $commands | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'failed-commands.json') -Encoding utf8
        throw "Validation failed: expected exit $ExpectedExit but got $code."
    }
}
if (-not $NoRestore) { Invoke-Dotnet @('restore', $project, '--configfile', $config, '-p:NuGetAudit=false') }
Invoke-Dotnet @('build', $project, '-c', 'Release', '--no-restore')
Invoke-Dotnet @('run', '--project', $project, '-c', 'Release', '--no-build', '--no-restore', '--',
    'self-test', '--output', (Join-Path $output 'self-test.json'))
if (-not $SkipObserve) {
    $observeArguments = @('run', '--project', $project, '-c', 'Release', '--no-build', '--no-restore', '--',
        'observe', '--output', (Join-Path $output 'observed-workflow.json'))
    if ($ServerProcessId) { $observeArguments += @('--server-pid', "$ServerProcessId") }
    Invoke-Dotnet $observeArguments -ExpectedExit $(if ($ExpectUnavailable) { 2 } else { 0 })
    if ($ExpectUnavailable) {
        $observation = Get-Content -LiteralPath (Join-Path $output 'observed-workflow.json') -Raw | ConvertFrom-Json
        if ($observation.probe.state -ne 'Missing' -or $observation.workflow.state -ne 'Unavailable') {
            throw 'Expected specifically a missing endpoint and unavailable answering workflow.'
        }
        if (($observation.calls -join '|') -ne 'GET /api/version|GET /api/tags') {
            throw 'Unexpected network attempt while local inference was missing.'
        }
    }
}
& (Join-Path $PSScriptRoot 'Record-Machine.ps1') -OutputPath (Join-Path $output 'machine.json')
$repo = (& git rev-parse --show-toplevel | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not identify repository root.' }
$inputs = [Collections.Generic.List[string]]::new()
foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -File) {
    if ($file.Extension -in '.cs', '.ps1', '.csproj', '.props', '.Config', '.json') { $inputs.Add($file.FullName) }
}
[xml] $definition = Get-Content -LiteralPath $project -Raw
foreach ($include in $definition.Project.ItemGroup.Compile.Include) {
    foreach ($part in $include.Split(';', [StringSplitOptions]::RemoveEmptyEntries)) {
        foreach ($file in Get-ChildItem -Path (Join-Path $PSScriptRoot $part.Trim()) -File) { $inputs.Add($file.FullName) }
    }
}
$inputs.Add((Join-Path $repo 'global.json'))
$hashes = @($inputs | Sort-Object -Unique | ForEach-Object {
    @{ Path = [IO.Path]::GetRelativePath($repo, $_); Sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
})
$tests = Get-Content -LiteralPath (Join-Path $output 'self-test.json') -Raw | ConvertFrom-Json
$report = @{
    Schema = 'Kora.R02.ValidationEvidence.v1'
    RecordedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    BaseRevisionAtRun = $revision
    SourceInputs = $hashes
    Commands = $commands
    PassedSelfTests = $tests.tests.Count
    ExpectedUnavailable = [bool]$ExpectUnavailable
    ObservationSkipped = [bool]$SkipObserve
    Scope = 'Source-linked experiment, not full application/voice/tool-loop or offline/reference-hardware acceptance.'
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $output 'validation.json') -Encoding utf8
Write-Host "Validated $($tests.tests.Count) self-tests; durable evidence: $output"
