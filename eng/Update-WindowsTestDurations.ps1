[CmdletBinding()]
param(
    # Complete twelve-report TRX evidence from one successful post-merge Windows run.
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $RetainedResultsDirectory,

    [Parameter(Mandatory)]
    [ValidateRange(1, [long]::MaxValue)]
    [long] $SourceRun,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string] $SourceRevision
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WindowsTestEvidence.ps1')

$reports = @(Get-ChildItem -LiteralPath $RetainedResultsDirectory -Filter '*.trx' -Recurse -File)
if ($reports.Count -ne 12) {
    throw 'Measured partition durations require one complete twelve-report Windows run.'
}
$durations = ConvertTo-WindowsTestDurations $RetainedResultsDirectory $SourceRun $SourceRevision
$path = Join-Path $PSScriptRoot 'windows-test-durations.json'
[IO.File]::WriteAllText($path, ($durations | ConvertTo-Json -Depth 4) + "`n", [Text.UTF8Encoding]::new($false))
Write-Host "Wrote $($durations.Classes.Count) class and $($durations.Methods.Count) split-method durations from run $SourceRun."
