[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$assertions = 0

function Assert {
    param([bool] $Condition, [string] $Message)
    $script:assertions++
    if (-not $Condition) { throw $Message }
}

[xml] $solution = Get-Content -Raw -LiteralPath (Join-Path $root 'Kora.UnitTests.slnx')
[xml] $mainSolution = Get-Content -Raw -LiteralPath (Join-Path $root 'Kora.slnx')
$projects = @($solution.SelectNodes('/Solution//Project') | ForEach-Object { $_.Path.Replace('\', '/') })
$mainProjects = @($mainSolution.SelectNodes('/Solution//Project') | ForEach-Object { $_.Path.Replace('\', '/') })
$expected = @(
    'tests/Kora.Application.UnitTests/Kora.Application.UnitTests.csproj'
    'tests/Kora.Core.UnitTests/Kora.Core.UnitTests.csproj'
    'tests/Kora.Definitions.UnitTests/Kora.Definitions.UnitTests.csproj'
    'tests/Kora.Tools.UnitTests/Kora.Tools.UnitTests.csproj'
)
Assert ($projects.Count -eq $expected.Count) 'The portable solution must contain exactly four test projects.'
Assert (@($projects | Sort-Object -Unique).Count -eq $expected.Count) 'The portable solution must not duplicate projects.'
foreach ($project in $expected) {
    Assert ($projects -ccontains $project) "The portable solution is missing $project."
    Assert ($mainProjects -ccontains $project) "The main restore/build must include $project before --no-build testing."
    Assert (Test-Path -LiteralPath (Join-Path $root $project.Replace('/', '\')) -PathType Leaf) "Project does not exist: $project."
}

$configuration = Get-Content -Raw -LiteralPath (Join-Path $root 'global.json') | ConvertFrom-Json
Assert ($configuration.test.runner -ceq 'Microsoft.Testing.Platform') 'Solution test execution requires the MTP runner.'
$workflow = Get-Content -Raw -LiteralPath (Join-Path $root '.github\workflows\ci.yml')
$portable = [regex]::Match($workflow, '(?ms)^  portable:\r?\n(?<job>.*?)(?=^  [a-z][a-z-]*:|\z)')
Assert $portable.Success 'The portable CI job was not found.'
$job = $portable.Groups['job'].Value
Assert ([regex]::Matches($job, '(?m)^\s+dotnet test\s*$').Count -eq 1) 'Portable CI must use one multi-project test invocation.'
$step = [regex]::Match($job,
    '(?m)      - name: Test portable projects with coverage\r?\n        run: >\r?\n(?<command>(?:          [^\r\n]*(?:\r?\n|$))+)')
Assert $step.Success 'The portable multi-project coverage command was not found.'
$command = $step.Groups['command'].Value
foreach ($argument in @(
    '--solution Kora.UnitTests.slnx'
    '--configuration Release'
    '--no-build'
    '--results-directory .net-test-artifacts/unit'
    '--results-directory-layout per-module'
    '--report-trx'
    '--coverlet'
    '--coverlet-output-format cobertura'
    '--coverlet-file-prefix unit'
)) {
    Assert ($command.Contains($argument, [StringComparison]::Ordinal)) "Missing test argument: $argument."
}
Assert (-not $command.Contains('--project', [StringComparison]::Ordinal)) 'The test command must not select a single project.'
Assert (-not [regex]::IsMatch($command, '--max-parallel-test-modules\s+1(?:\s|$)')) 'Portable modules must not be forced to run sequentially.'
Assert ($job.Contains('-reports:.net-test-artifacts/**/*.coverage.cobertura.*.xml', [StringComparison]::Ordinal)) 'Coverage aggregation must include every module directory.'
Assert ($job.Contains('-MinimumLine 100', [StringComparison]::Ordinal)) 'Preserve the complete line-coverage gate.'
Assert ($job.Contains('-MinimumBranch 100', [StringComparison]::Ordinal)) 'Preserve the complete branch-coverage gate.'
Assert ($workflow.Contains("if: github.event_name == 'workflow_dispatch' && inputs.compare_portable_tests",
    [StringComparison]::Ordinal)) 'Scheduling comparisons must remain opt-in and manual-only.'
Assert ($workflow.Contains("inputs.compare_portable_tests && '-portable-comparison' || ''",
    [StringComparison]::Ordinal)) 'Manual comparisons must not cancel the PR validation run.'
$comparisonInput = [regex]::Match($workflow, '(?ms)^      compare_portable_tests:\r?\n(?<input>.*?)(?=^\S|\z)')
Assert $comparisonInput.Success 'The manual comparison input was not found.'
Assert ($comparisonInput.Groups['input'].Value.Contains('default: false', [StringComparison]::Ordinal)) 'Scheduling comparisons must default to disabled.'
Write-Host "Portable test scheduling: $assertions assertions passed."
