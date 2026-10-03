[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ReportPath,

    [double] $MinimumLine = 100,

    [double] $MinimumBranch = 100
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ReportPath -PathType Leaf)) {
    throw "Coverage report was not found at '$ReportPath'."
}

[xml] $report = Get-Content -LiteralPath $ReportPath -Raw
$lineCoverage = [double]::Parse(
    $report.coverage.'line-rate',
    [Globalization.CultureInfo]::InvariantCulture) * 100
$branchCoverage = [double]::Parse(
    $report.coverage.'branch-rate',
    [Globalization.CultureInfo]::InvariantCulture) * 100

Write-Output ('Line coverage: {0:N1}% (minimum {1:N1}%)' -f $lineCoverage, $MinimumLine)
Write-Output ('Branch coverage: {0:N1}% (minimum {1:N1}%)' -f $branchCoverage, $MinimumBranch)

if ($lineCoverage -lt $MinimumLine -or $branchCoverage -lt $MinimumBranch) {
    throw 'Code coverage is below the required threshold.'
}
