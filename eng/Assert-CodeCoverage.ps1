[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ReportPath,

    [string] $InputDirectory = (Join-Path $PSScriptRoot '..\.net-test-artifacts\unit'),

    [double] $MinimumLine = 100,

    [double] $MinimumBranch = 100
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$assemblies = @('Kora.Core', 'Kora.Application', 'Kora.Tools', 'Kora.Definitions')
$culture = [Globalization.CultureInfo]::InvariantCulture

function Read-Count {
    param([System.Xml.XmlElement] $Element, [string] $Attribute)
    [long] $value = 0
    if (-not [long]::TryParse($Element.GetAttribute($Attribute),
        [Globalization.NumberStyles]::None, $culture, [ref] $value) -or $value -lt 0) {
        throw "Invalid coverage count '$Attribute' on '$($Element.Name)'."
    }
    return $value
}

function Read-Rate {
    param([System.Xml.XmlElement] $Element, [string] $Attribute)
    [double] $value = 0
    if (-not [double]::TryParse($Element.GetAttribute($Attribute),
        [Globalization.NumberStyles]::Float, $culture, [ref] $value) -or
        -not [double]::IsFinite($value) -or $value -lt 0 -or $value -gt 1) {
        throw "Invalid coverage rate '$Attribute' on '$($Element.Name)'."
    }
    return $value
}

function Assert-Rate {
    param([double] $Rate, [long] $Covered, [long] $Valid, [double] $Tolerance, [string] $Label)
    if ($Covered -gt $Valid) { throw "Covered count exceeds valid count for $Label." }
    # Both maintained serializers use 1 for a denominator of zero.
    $expected = if ($Valid -eq 0) { 1.0 } else { $Covered / [double] $Valid }
    if ([Math]::Abs($Rate - $expected) -gt $Tolerance) {
        throw "Coverage rate disagrees with counts for $Label."
    }
}

function Read-Coverage {
    param([string] $Path, [string[]] $RequiredAssemblies, [switch] $Merged)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Coverage report was not found at '$Path'."
    }
    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Ignore
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create([IO.Path]::GetFullPath($Path), $settings)
    try {
        $document = [System.Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
    }
    finally { $reader.Dispose() }
    $root = $document.DocumentElement
    if ($null -eq $root -or $root.Name -cne 'coverage' -or $root.NamespaceURI -cne '') {
        throw "Expected an unnamespaced Cobertura coverage document at '$Path'."
    }
    # Coverlet truncates rates to four decimals; ReportGenerator writes full precision.
    $tolerance = if ($Merged) { 0.000000001 } else { 0.000100000001 }
    $lineRate = Read-Rate $root 'line-rate'
    $branchRate = Read-Rate $root 'branch-rate'
    $linesValid = Read-Count $root 'lines-valid'
    $linesCovered = Read-Count $root 'lines-covered'
    $branchesValid = Read-Count $root 'branches-valid'
    $branchesCovered = Read-Count $root 'branches-covered'
    if ($linesValid -eq 0) { throw "Coverage report contains no executable lines: '$Path'." }
    Assert-Rate $lineRate $linesCovered $linesValid $tolerance "$Path lines"
    Assert-Rate $branchRate $branchesCovered $branchesValid $tolerance "$Path branches"

    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    [long] $totalLines = 0
    [long] $coveredLines = 0
    [long] $totalBranches = 0
    [long] $coveredBranches = 0
    foreach ($package in $root.SelectNodes('packages/package')) {
        $name = $package.GetAttribute('name')
        if ([string]::IsNullOrWhiteSpace($name) -or -not $names.Add($name)) {
            throw "Missing or duplicate assembly identity in '$Path': '$name'."
        }
        if ($Merged -and $assemblies -cnotcontains $name) {
            throw "Unexpected assembly in merged coverage report: '$name'."
        }
        $packageLineRate = Read-Rate $package 'line-rate'
        $packageBranchRate = Read-Rate $package 'branch-rate'
        [long] $packageLines = 0
        [long] $packageCovered = 0
        [long] $packageBranches = 0
        [long] $packageBranchesCovered = 0
        $classKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($class in $package.SelectNodes('classes/class')) {
            $className = $class.GetAttribute('name')
            $filename = $class.GetAttribute('filename')
            if ([string]::IsNullOrWhiteSpace($className) -or [string]::IsNullOrWhiteSpace($filename) -or
                -not $classKeys.Add("$className`n$filename")) {
                throw "Missing or duplicate class identity in '$name'."
            }
            $classLineRate = Read-Rate $class 'line-rate'
            $classBranchRate = Read-Rate $class 'branch-rate'
            $startLines = $packageLines
            $startCovered = $packageCovered
            $startBranches = $packageBranches
            $startBranchesCovered = $packageBranchesCovered
            $numbers = [Collections.Generic.HashSet[long]]::new()
            foreach ($line in $class.SelectNodes('lines/line')) {
                $number = Read-Count $line 'number'
                $hits = Read-Count $line 'hits'
                if ($number -eq 0 -or -not $numbers.Add($number)) {
                    throw "Invalid or duplicate line number in '$className'."
                }
                $packageLines++
                if ($hits -gt 0) { $packageCovered++ }
                $branch = $line.GetAttribute('branch')
                if ($branch -cnotin @('true', 'false', 'True', 'False')) {
                    throw "Invalid branch marker in '$className' line $number."
                }
                if ($branch -ieq 'true') {
                    $condition = $line.GetAttribute('condition-coverage')
                    if ($condition -notmatch '^([0-9]+(?:\.[0-9]+)?)% \(([0-9]+)/([0-9]+)\)$') {
                        throw "Invalid branch counts in '$className' line $number."
                    }
                    $percent = [double]::Parse($Matches[1], $culture)
                    $covered = [long]::Parse($Matches[2], $culture)
                    $valid = [long]::Parse($Matches[3], $culture)
                    if ($valid -eq 0 -or $percent -gt 100 -or $covered -gt $valid -or
                        [Math]::Abs($percent / 100 - $covered / [double] $valid) -gt 0.01) {
                        throw "Inconsistent branch counts in '$className' line $number."
                    }
                    $packageBranches += $valid
                    $packageBranchesCovered += $covered
                }
                elseif ($line.HasAttribute('condition-coverage')) {
                    throw "Branch counts on a non-branch line in '$className'."
                }
            }
            if ($Merged) {
                Assert-Rate $classLineRate ($packageCovered - $startCovered) ($packageLines - $startLines) $tolerance "$className lines"
                Assert-Rate $classBranchRate ($packageBranchesCovered - $startBranchesCovered) ($packageBranches - $startBranches) $tolerance "$className branches"
            }
        }
        if ($packageLines -eq 0) { throw "Assembly '$name' has no executable coverage evidence." }
        Assert-Rate $packageLineRate $packageCovered $packageLines $tolerance "$name lines"
        # Coverlet includes some compiler-generated branches in totals but not class lines.
        # ReportGenerator's merged totals are defined by the represented class lines.
        if ($Merged) {
            Assert-Rate $packageBranchRate $packageBranchesCovered $packageBranches $tolerance "$name branches"
        }
        $totalLines += $packageLines
        $coveredLines += $packageCovered
        $totalBranches += $packageBranches
        $coveredBranches += $packageBranchesCovered
    }
    foreach ($assembly in $RequiredAssemblies) {
        if (-not $names.Contains($assembly)) { throw "Missing assembly '$assembly' in '$Path'." }
    }
    if ($totalLines -ne $linesValid -or $coveredLines -ne $linesCovered -or
        ($Merged -and ($totalBranches -ne $branchesValid -or $coveredBranches -ne $branchesCovered))) {
        throw "Coverage totals disagree with class-line evidence in '$Path'."
    }
    return [pscustomobject]@{
        LineRate = $linesCovered / [double] $linesValid
        BranchRate = $(if ($branchesValid -eq 0) { 1.0 } else { $branchesCovered / [double] $branchesValid })
    }
}

foreach ($minimum in @($MinimumLine, $MinimumBranch)) {
    if (-not [double]::IsFinite($minimum) -or $minimum -lt 0 -or $minimum -gt 100) {
        throw 'Coverage thresholds must be finite percentages between 0 and 100.'
    }
}
if (-not (Test-Path -LiteralPath $InputDirectory -PathType Container)) {
    throw "Coverage input directory was not found at '$InputDirectory'."
}
$inputs = @(Get-ChildItem -LiteralPath $InputDirectory -Recurse -File -Filter '*.coverage.cobertura.*.xml')
foreach ($assembly in $assemblies) {
    $module = "$assembly.UnitTests"
    $modulePath = [IO.Path]::GetFullPath((Join-Path $InputDirectory $module)) + [IO.Path]::DirectorySeparatorChar
    $reports = @($inputs | Where-Object { $_.FullName.StartsWith($modulePath, [StringComparison]::Ordinal) })
    if ($reports.Count -ne 1) {
        throw "Expected exactly one coverage report from '$module'; found $($reports.Count)."
    }
    $null = Read-Coverage $reports[0].FullName @($assembly)
}
if ($inputs.Count -ne $assemblies.Count) { throw 'Unexpected portable module coverage reports were found.' }
$report = Read-Coverage $ReportPath $assemblies -Merged
$lineCoverage = $report.LineRate * 100
$branchCoverage = $report.BranchRate * 100

Write-Output ('Line coverage: {0:N6}% (minimum {1:N1}%)' -f $lineCoverage, $MinimumLine)
Write-Output ('Branch coverage: {0:N6}% (minimum {1:N1}%)' -f $branchCoverage, $MinimumBranch)

if ($lineCoverage -lt $MinimumLine -or $branchCoverage -lt $MinimumBranch) {
    throw 'Code coverage is below the required threshold.'
}
