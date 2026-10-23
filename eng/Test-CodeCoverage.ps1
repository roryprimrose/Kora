[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$gate = Join-Path $PSScriptRoot 'Assert-CodeCoverage.ps1'
$temporary = Join-Path ([IO.Path]::GetTempPath()) "kora-coverage-$([Guid]::NewGuid().ToString('N'))"
$baseline = Join-Path $temporary 'baseline'
$working = Join-Path $temporary 'case'
$assemblies = @('Kora.Core', 'Kora.Application', 'Kora.Tools', 'Kora.Definitions')
$passed = 0

function Write-Inputs {
    param([string] $Directory, [switch] $Branchless, [switch] $PartialLine, [switch] $PartialBranch)
    foreach ($assembly in $assemblies) {
        $destination = Join-Path $Directory "$assembly.UnitTests\net10.0_x64"
        $null = New-Item -ItemType Directory -Path $destination -Force
        $noBranches = $Branchless -or $assembly -eq 'Kora.Definitions'
        $lineCovered = if ($PartialLine -and $assembly -eq 'Kora.Core') { 0 } else { 1 }
        $branches = if ($noBranches) { 0 } else { 2 }
        $branchCovered = if ($PartialBranch -and $assembly -eq 'Kora.Core') { 1 } else { $branches }
        $branchRate = if ($branches -eq 0) { 1 } else { $branchCovered / $branches }
        $branch = if ($noBranches) { 'False' } else { 'True' }
        $condition = if ($noBranches) { '' } else {
            " condition-coverage=`"$($branchRate * 100)% ($branchCovered/$branches)`""
        }
        # Match Coverlet MTP's package identities, filename style and duplicate method lines.
        $line = "<line number=`"10`" hits=`"$lineCovered`" branch=`"$branch`"$condition />"
        @"
<?xml version="1.0" encoding="utf-8"?>
<coverage line-rate="$lineCovered" branch-rate="$branchRate" version="1.9" timestamp="1792751545" lines-covered="$lineCovered" lines-valid="1" branches-covered="$branchCovered" branches-valid="$branches">
  <sources><source>/</source></sources>
  <packages><package name="$assembly" line-rate="$lineCovered" branch-rate="$branchRate" complexity="1">
    <classes><class name="$assembly.Example" filename="home/runner/work/Kora/Kora/src/$assembly/Example.cs" line-rate="$lineCovered" branch-rate="$branchRate" complexity="1">
      <methods><method name="Example" signature="()" line-rate="$lineCovered" branch-rate="$branchRate" complexity="1"><lines>$line</lines></method></methods>
      <lines>$line</lines>
    </class></classes>
  </package></packages>
</coverage>
"@ | Set-Content -LiteralPath (Join-Path $destination 'unit.coverage.cobertura.231026103225776.xml')
    }
}

function Merge-Inputs {
    param([string] $Directory)
    & dotnet reportgenerator "-reports:$Directory/raw/**/*.coverage.cobertura.*.xml" `
        "-targetdir:$Directory/merged" '-reporttypes:Cobertura' `
        '-assemblyfilters:+Kora.Core;+Kora.Application;+Kora.Tools;+Kora.Definitions' | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "ReportGenerator failed with exit code $LASTEXITCODE." }
}

function Edit-Report {
    param([scriptblock] $Change, [string] $Path = (Join-Path $working 'merged\Cobertura.xml'))
    [xml] $xml = Get-Content -Raw -LiteralPath $Path
    & $Change $xml
    $xml.Save($Path)
}

function Test-Case {
    param(
        [string] $Name,
        [scriptblock] $Change,
        [string] $ExpectedError = '',
        [double] $MinimumLine = 100,
        [double] $MinimumBranch = 100
    )
    if (Test-Path -LiteralPath $working) { Remove-Item -LiteralPath $working -Recurse -Force }
    Copy-Item -LiteralPath $baseline -Destination $working -Recurse
    & $Change
    $failure = ''
    try {
        & $gate -ReportPath (Join-Path $working 'merged\Cobertura.xml') `
            -InputDirectory (Join-Path $working 'raw') -MinimumLine $MinimumLine -MinimumBranch $MinimumBranch | Out-Null
    }
    catch { $failure = $_.Exception.Message }
    if (($ExpectedError -eq '' -and $failure -ne '') -or
        ($ExpectedError -ne '' -and -not $failure.Contains($ExpectedError, [StringComparison]::Ordinal))) {
        throw "Coverage case '$Name': expected '$ExpectedError', got '$failure'."
    }
    $script:passed++
    Write-Host "PASS: $Name"
}

Push-Location $root
try {
    Write-Inputs (Join-Path $baseline 'raw')
    Merge-Inputs $baseline
    $rawCore = Join-Path $working 'raw\Kora.Core.UnitTests\net10.0_x64\unit.coverage.cobertura.231026103225776.xml'
    Test-Case 'complete actual ReportGenerator output' {}
    foreach ($assembly in $assemblies) {
        Test-Case "missing module $assembly" {
            Remove-Item -LiteralPath (Join-Path $working "raw\$assembly.UnitTests") -Recurse -Force
        } 'Expected exactly one coverage report'
        Test-Case "missing merged assembly $assembly" {
            Edit-Report { param($xml)
                $node = $xml.SelectSingleNode("/coverage/packages/package[@name='$assembly']")
                $null = $node.ParentNode.RemoveChild($node)
            }
        } 'Missing assembly'
    }
    Test-Case 'only Core with perfect rates (original defect)' {
        Edit-Report { param($xml)
            foreach ($node in @($xml.SelectNodes("/coverage/packages/package[@name!='Kora.Core']"))) {
                $null = $node.ParentNode.RemoveChild($node)
            }
        }
    } 'Missing assembly'
    Test-Case 'missing input directory' {
        Remove-Item -LiteralPath (Join-Path $working 'raw') -Recurse -Force
    } 'Coverage input directory was not found'
    Test-Case 'duplicate stale module report' {
        Copy-Item -LiteralPath $rawCore -Destination (Join-Path (Split-Path $rawCore) 'unit.coverage.cobertura.stale.xml')
    } 'Expected exactly one coverage report'
    Test-Case 'unexpected module report' {
        Copy-Item -LiteralPath $rawCore -Destination (Join-Path $working 'raw\extra.coverage.cobertura.1.xml')
    } 'Unexpected portable module'
    Test-Case 'module report missing own assembly' {
        Edit-Report { param($xml) $xml.DocumentElement.packages.package.SetAttribute('name', 'Other') } $rawCore
    } 'Missing assembly'
    Test-Case 'empty raw report' { Set-Content -LiteralPath $rawCore -Value '' } 'Root element is missing'
    Test-Case 'malformed raw report' { Set-Content -LiteralPath $rawCore -Value '<coverage>' } 'Unexpected end of file'
    Test-Case 'missing merged report' {
        Remove-Item -LiteralPath (Join-Path $working 'merged\Cobertura.xml')
    } 'Coverage report was not found'
    Test-Case 'wrong document root' {
        Set-Content -LiteralPath (Join-Path $working 'merged\Cobertura.xml') -Value '<notCoverage />'
    } 'Expected an unnamespaced'
    Test-Case 'empty assembly' {
        Edit-Report { param($xml) $xml.DocumentElement.packages.package[0].classes.RemoveAll() }
    } 'no executable coverage evidence'
    Test-Case 'duplicate assembly' {
        Edit-Report { param($xml)
            $node = $xml.DocumentElement.packages.package[0]
            $null = $node.ParentNode.AppendChild($node.CloneNode($true))
        }
    } 'duplicate assembly identity'
    Test-Case 'unexpected merged assembly' {
        Edit-Report { param($xml) $xml.DocumentElement.packages.package[0].SetAttribute('name', 'Other') }
    } 'Unexpected assembly'
    Test-Case 'duplicate class identity' {
        Edit-Report { param($xml)
            $node = $xml.DocumentElement.packages.package[0].classes.class
            $null = $node.ParentNode.AppendChild($node.CloneNode($true))
        }
    } 'duplicate class identity'
    Test-Case 'duplicate line evidence' {
        Edit-Report { param($xml)
            $node = $xml.DocumentElement.packages.package[0].classes.class.lines.line
            $null = $node.ParentNode.AppendChild($node.CloneNode($true))
        }
    } 'duplicate line number'
    foreach ($attribute in @('line-rate', 'branch-rate')) {
        foreach ($value in @('NaN', 'Infinity', '-Infinity', '-0.1', '1.1', '', 'nonsense')) {
            Test-Case "$attribute rejects '$value'" {
                Edit-Report { param($xml) $xml.DocumentElement.SetAttribute($attribute, $value) }
            } 'Invalid coverage rate'
        }
        Test-Case "missing $attribute" {
            Edit-Report { param($xml) $xml.DocumentElement.RemoveAttribute($attribute) }
        } 'Invalid coverage rate'
        Test-Case "raw $attribute rejects NaN" {
            Edit-Report { param($xml) $xml.DocumentElement.SetAttribute($attribute, 'NaN') } $rawCore
        } 'Invalid coverage rate'
    }
    foreach ($value in @('-1', '1.5', '', 'NaN', '9223372036854775808')) {
        Test-Case "invalid count '$value'" {
            Edit-Report { param($xml) $xml.DocumentElement.SetAttribute('lines-valid', $value) }
        } 'Invalid coverage count'
    }
    Test-Case 'covered count exceeds total' {
        Edit-Report { param($xml) $xml.DocumentElement.SetAttribute('lines-covered', '5') }
    } 'Covered count exceeds'
    Test-Case 'rates contradict counts' {
        Edit-Report { param($xml) $xml.DocumentElement.SetAttribute('lines-covered', '3') }
    } 'rate disagrees'
    Test-Case 'root totals contradict lines' {
        Edit-Report { param($xml)
            $xml.DocumentElement.SetAttribute('lines-valid', '5')
            $xml.DocumentElement.SetAttribute('lines-covered', '5')
        }
    } 'totals disagree'
    Test-Case 'raw line totals contradict lines' {
        Edit-Report { param($xml)
            $xml.DocumentElement.SetAttribute('lines-valid', '2')
            $xml.DocumentElement.SetAttribute('lines-covered', '2')
        } $rawCore
    } 'totals disagree'
    Test-Case 'raw truncated rates and unrepresented compiler branches' {
        Edit-Report { param($xml)
            $xml.DocumentElement.SetAttribute('branches-valid', '3')
            $xml.DocumentElement.SetAttribute('branch-rate', '0.6666')
        } $rawCore
    }
    Test-Case 'raw branch rate contradicts counts' {
        Edit-Report { param($xml) $xml.DocumentElement.SetAttribute('branch-rate', '0.5') } $rawCore
    } 'rate disagrees'
    Test-Case 'empty coverage counts' {
        Edit-Report { param($xml)
            $xml.DocumentElement.SetAttribute('lines-valid', '0')
            $xml.DocumentElement.SetAttribute('lines-covered', '0')
        }
    } 'no executable lines'
    Test-Case 'forged perfect package rate' {
        Edit-Report { param($xml) $xml.DocumentElement.packages.package[0].classes.class.lines.line.SetAttribute('hits', '0') }
    } 'rate disagrees'
    Test-Case 'conflicting class rate' {
        Edit-Report { param($xml) $xml.DocumentElement.packages.package[0].classes.class.SetAttribute('line-rate', '0') }
    } 'rate disagrees'
    Test-Case 'negative hits' {
        Edit-Report { param($xml) $xml.DocumentElement.packages.package[0].classes.class.lines.line.SetAttribute('hits', '-1') }
    } 'Invalid coverage count'
    Test-Case 'invalid branch marker' {
        Edit-Report { param($xml) $xml.DocumentElement.packages.package[0].classes.class.lines.line.SetAttribute('branch', 'maybe') }
    } 'Invalid branch marker'
    Test-Case 'invalid branch counts' {
        Edit-Report { param($xml)
            $xml.SelectSingleNode('//class/lines/line[@branch="true"]').SetAttribute('condition-coverage', '100% (3/2)')
        }
    } 'Inconsistent branch counts'
    Test-Case 'missing branch counts' {
        Edit-Report { param($xml)
            $xml.SelectSingleNode('//class/lines/line[@branch="true"]').RemoveAttribute('condition-coverage')
        }
    } 'Invalid branch counts'
    Test-Case 'conflicting branch total' {
        Edit-Report { param($xml)
            $xml.DocumentElement.SetAttribute('branches-valid', '8')
            $xml.DocumentElement.SetAttribute('branches-covered', '8')
        }
    } 'totals disagree'
    Test-Case 'partial lines fail 100 percent' {
        Write-Inputs (Join-Path $working 'raw') -PartialLine
        Merge-Inputs $working
    } 'below the required threshold'
    Test-Case 'partial branches fail 100 percent' {
        Write-Inputs (Join-Path $working 'raw') -PartialBranch
        Merge-Inputs $working
    } 'below the required threshold'
    Test-Case 'legitimate zero-branch report' {
        Write-Inputs (Join-Path $working 'raw') -Branchless
        Merge-Inputs $working
    }
    Test-Case 'configured lower thresholds remain supported' {
        Write-Inputs (Join-Path $working 'raw') -PartialLine -PartialBranch
        Merge-Inputs $working
    } -MinimumLine 75 -MinimumBranch 80
    Test-Case 'nonfinite threshold fails closed' {} 'thresholds must be finite' -MinimumLine ([double]::NaN)
    Test-Case 'out of range threshold fails closed' {} 'thresholds must be finite' -MinimumBranch 101

    $workflow = Get-Content -Raw -LiteralPath (Join-Path $root '.github\workflows\ci.yml')
    foreach ($required in @(
        '- name: Test coverage evidence contracts'
        'run: ./eng/Test-CodeCoverage.ps1'
        '-InputDirectory .net-test-artifacts/unit'
        '-MinimumLine 100'
        '-MinimumBranch 100'
        'name: test-results'
        'name: coverage-report'
    )) {
        if (-not $workflow.Contains($required, [StringComparison]::Ordinal)) { throw "Missing CI coverage contract: $required" }
    }
    Write-Host "Coverage integrity: $passed cases and 7 CI contracts passed."
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
