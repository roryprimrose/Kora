#requires -Version 7.0
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$workflow = (Get-Content -Raw -LiteralPath (Join-Path $root '.github\workflows\ci.yml')).Replace("`r`n", "`n")
$assertions = 0

function Assert {
    param([bool] $Condition, [string] $Message)
    $script:assertions++
    if (-not $Condition) { throw $Message }
}

function Get-Job {
    param([string] $Source, [string] $Name)
    $match = [regex]::Match($Source, "(?ms)^  ${Name}:`n(?<job>.*?)(?=^  [a-z][a-z-]*:|\z)")
    if (-not $match.Success) { throw "Missing job: $Name." }
    return $match.Groups['job'].Value
}

function Get-Step {
    param([string] $Job, [string] $Name)
    $match = [regex]::Match($Job, "(?ms)^      - name: $([regex]::Escape($Name))`n(?<step>.*?)(?=^      - |\z)")
    if (-not $match.Success) { throw "Missing step: $Name." }
    return $match.Groups['step'].Value
}

function Get-StepScript {
    param([string] $Step)
    $match = [regex]::Match($Step, '(?m)^        run: \|\n(?<script>(?:          [^\n]*\n?)+)')
    if (-not $match.Success) { throw 'Missing executable step script.' }
    return [scriptblock]::Create(($match.Groups['script'].Value -replace '(?m)^          ', ''))
}

function Assert-Workflow {
    param([string] $Source)
    $version = Get-Job $Source 'version'
    Assert ($Source.Contains('# merge-queue-job-schema: parallel-pr-v1', [StringComparison]::Ordinal)) 'Use the reviewed parallel candidate and complete main inventories.'
    Assert ($version.Contains("needs: admission`n    if: needs.admission.outputs.full == 'true'", [StringComparison]::Ordinal)) 'Preflight must run only after successful full admission.'
    Assert ($version.Contains('ref: ${{ needs.admission.outputs.source }}', [StringComparison]::Ordinal)) 'Preflight must check out the admitted source.'
    Assert ($version.Contains('persist-credentials: false', [StringComparison]::Ordinal)) 'Preflight must not retain checkout credentials.'
    Assert ($version.Contains('preflight-source: ${{ steps.preflight.outputs.source }}', [StringComparison]::Ordinal)) 'Only completed preflight may supply its source receipt.'
    Assert (-not [regex]::IsMatch($version, 'continue-on-error|dotnet (build|publish|test)|Test-Architecture|Test-CodeCoverage')) 'Preflight must not weaken failures or serialize builds, coverage or broad tests.'
    Assert (-not [regex]::IsMatch($version, '(?m)^    permissions:|id-token:|contents: write|actions: write')) 'Candidate preflight must remain unprivileged.'
    $previous = -1
    foreach ($command in @(
        './eng/Test-WindowsIntegrationScheduling.ps1'
        './eng/Test-PortableTestScheduling.ps1'
        './eng/Test-CiPreflight.ps1'
        'node eng/run-policy-tests.cjs'
        './eng/Test-SourceToolsContracts.ps1'
        'dotnet restore Kora.slnx --locked-mode'
        'dotnet tool restore'
        './eng/Test-DependencyLicenses.ps1'
        'id: preflight'
        'name: dependency-license-audit-${{ needs.admission.outputs.source }}'
    )) {
        $position = $version.IndexOf($command, [StringComparison]::Ordinal)
        Assert ($position -gt $previous) "Preflight ordering is missing or incorrect: $command."
        $previous = $position
    }
    foreach ($name in @('Test scheduling and early preflight contracts', 'Test hosted policy modules before fanout', 'Test static immutable source-tool distribution',
        'Enforce dependency license and notice policy before fanout', 'Bind successful preflight to the admitted source')) {
        Assert (-not [regex]::IsMatch((Get-Step $version $name), '(?m)^        if:')) "Required preflight step must not be optionally skipped: $name."
    }
    Assert ((Get-Job $Source 'queue-feedback').Contains('run: node eng/run-policy-tests.cjs', [StringComparison]::Ordinal)) 'Waiting PR feedback must run the same complete policy selector.'
    Assert (-not $Source.Contains('run: node --test eng/merge-queue/*.test.cjs', [StringComparison]::Ordinal)) 'No workflow path may silently omit installed campaign tests.'
    foreach ($name in @('portable-tests', 'portable-policy', 'payload', 'windows-test-build', 'windows-test-partitions', 'installer-validation', 'installer',
        'portable-test-comparison', 'native-evidence-comparison')) {
        $job = Get-Job $Source $name
        $needs = [regex]::Match($job, '(?m)^    needs: (.+)$').Groups[1].Value
        Assert ([regex]::IsMatch($needs, '\bversion\b')) "Expensive job must depend on preflight: $name."
        $condition = [regex]::Match($job, '(?m)^    if: (.+)$').Groups[1].Value
        Assert ($condition.Contains("needs.version.outputs.preflight-source != ''", [StringComparison]::Ordinal) -and
            $condition.Contains('needs.version.outputs.preflight-source == needs.version.outputs.source-revision', [StringComparison]::Ordinal)) "Expensive job must require nonempty exact-source preflight: $name."
        Assert (-not [regex]::IsMatch($condition, 'always\(|failure\(|cancelled\(|\|\|')) "Expensive job must preserve implicit success gating: $name."
    }
    $payload = Get-Job $Source 'payload'
    $download = Get-Step $payload "Download this run's exact-source dependency license audit"
    Assert ($download.Contains('name: dependency-license-audit-${{ needs.version.outputs.source-revision }}', [StringComparison]::Ordinal)) 'Payload must download the exact-source artifact from this run.'
    Assert (-not [regex]::IsMatch($download, 'run-id:|repository:|github-token:|merge-multiple:')) 'Payload must not import license evidence from another run or repository.'
    $verify = $payload.IndexOf('Require admitted license evidence before publishing bytes', [StringComparison]::Ordinal)
    $publish = $payload.IndexOf('dotnet publish', [StringComparison]::Ordinal)
    Assert ($verify -ge 0 -and $publish -gt $verify) 'License source must be verified before any payload publication.'
    Assert (-not $payload.Contains('./eng/Test-DependencyLicenses.ps1', [StringComparison]::Ordinal)) 'Payload must reuse, not repeat, successful exact-source license scanning.'
    Assert (([regex]::Matches($Source, 'run: \./eng/Test-DependencyLicenses\.ps1')).Count -eq 1) 'The complete workflow license gate must run exactly once before fanout.'
    $portable = Get-Job $Source 'portable-tests'
    foreach ($command in @('dotnet build Kora.UnitTests.slnx --configuration Release --no-restore',
        '--solution Kora.UnitTests.slnx', '-MinimumLine 100', '-MinimumBranch 100',
        '-assemblyfilters:+Kora.Core;+Kora.Application;+Kora.Tools;+Kora.Definitions')) {
        Assert ($portable.Contains($command, [StringComparison]::Ordinal)) "Complete downstream portable gate must remain: $command."
    }
    Assert ((Get-Job $Source 'portable-policy').Contains('./eng/Test-Architecture.ps1',
        [StringComparison]::Ordinal)) 'Parallel architecture proof must remain mandatory.'
}

Assert-Workflow $workflow
foreach ($mutation in @(
    @('preflight-source: ${{ steps.preflight.outputs.source }}', 'preflight-source: ${{ needs.admission.outputs.source }}'),
    @("needs.version.outputs.preflight-source != '' && ", ''),
    @('needs.version.outputs.preflight-source == needs.version.outputs.source-revision', "always()"),
    @('run: ./eng/Test-DependencyLicenses.ps1', 'run: Write-Host skipped'),
    @('name: dependency-license-audit-${{ needs.version.outputs.source-revision }}', 'name: dependency-license-audit-other-source'),
    @('name: dependency-license-audit-${{ needs.version.outputs.source-revision }}', "run-id: 123`n          name: dependency-license-audit-`${{ needs.version.outputs.source-revision }}"),
    @('id: licenses', "id: licenses`n        if: false"),
    @('id: scheduling', "id: scheduling`n        continue-on-error: true"),
    @('id: scheduling', "id: scheduling`n        run: dotnet test")
)) {
    $rejected = $false
    try { Assert-Workflow ($workflow.Replace($mutation[0], $mutation[1])) }
    catch { $rejected = $true }
    Assert $rejected "Unsafe workflow mutation was accepted: $($mutation[0])."
}

$revision = 'a' * 40
$version = Get-Job $workflow 'version'
$bind = Get-StepScript (Get-Step $version 'Bind successful preflight to the admitted source')
$verify = Get-StepScript (Get-Step (Get-Job $workflow 'payload') 'Require admitted license evidence before publishing bytes')
$fixture = Join-Path $root ".scratch\ci-preflight\$([Guid]::NewGuid().ToString('N'))"
$originalLocation = Get-Location
$savedEnvironment = @{}
foreach ($name in @('SOURCE_REVISION', 'SCHEDULING_RESULT', 'SOURCE_POLICY_RESULT', 'LICENSE_RESULT', 'POLICY_TEST_RESULT', 'GITHUB_OUTPUT')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}
try {
    [IO.Directory]::CreateDirectory((Join-Path $fixture 'artifacts\license-compliance')) | Out-Null
    Set-Location -LiteralPath $fixture
    $env:GITHUB_OUTPUT = Join-Path $fixture 'output.txt'
    $marker = Join-Path $fixture 'artifacts\license-compliance\source-revision.txt'
    $gitRevision = $revision
    $gitExit = 0
    function git { $global:LASTEXITCODE = $gitExit; return $gitRevision }
    $outcomes = @('success', 'failure', 'cancelled', 'skipped', '', 'unknown', 'SUCCESS')
    foreach ($producer in @('SCHEDULING_RESULT', 'SOURCE_POLICY_RESULT', 'LICENSE_RESULT', 'POLICY_TEST_RESULT')) {
        foreach ($outcome in $outcomes) {
            $env:SOURCE_REVISION = $revision
            $env:SCHEDULING_RESULT = $env:SOURCE_POLICY_RESULT = $env:LICENSE_RESULT = $env:POLICY_TEST_RESULT = 'success'
            [Environment]::SetEnvironmentVariable($producer, $outcome)
            Remove-Item -LiteralPath $marker, $env:GITHUB_OUTPUT -Force -ErrorAction Ignore
            $failed = $false
            $fanout = $false
            try { & $bind; $fanout = $true } catch { $failed = $true }
            Assert ($failed -eq ($outcome -cne 'success')) "Incorrect preflight outcome for $producer '$outcome'."
            Assert ($fanout -eq ($outcome -ceq 'success')) 'Incomplete preflight must never release expensive work.'
            Assert ((Test-Path -LiteralPath $marker) -eq $fanout) 'Incomplete preflight must not retain a source receipt.'
            Assert ((Test-Path -LiteralPath $env:GITHUB_OUTPUT) -eq $fanout) 'Incomplete preflight must not publish job outputs.'
            if ($fanout) {
                Assert ((Get-Content -LiteralPath $marker -Raw) -ceq $revision) 'Successful license evidence must be bound to the exact source.'
                Assert ((Get-Content -LiteralPath $env:GITHUB_OUTPUT -Raw).TrimEnd("`r", "`n") -ceq "source=$revision") 'Successful job output must expose only the admitted source.'
                & $verify
            }
        }
    }
    $env:SCHEDULING_RESULT = $env:SOURCE_POLICY_RESULT = $env:LICENSE_RESULT = $env:POLICY_TEST_RESULT = 'success'
    foreach ($case in @('wrong-checkout', 'git-failure', 'missing-source', 'malformed-source')) {
        $env:SOURCE_REVISION = $revision
        $gitRevision = $revision
        $gitExit = 0
        switch ($case) {
            'wrong-checkout' { $gitRevision = 'b' * 40 }
            'git-failure' { $gitExit = 1 }
            'missing-source' { $env:SOURCE_REVISION = '' }
            'malformed-source' { $env:SOURCE_REVISION = $gitRevision = 'not-a-revision' }
        }
        Remove-Item -LiteralPath $marker, $env:GITHUB_OUTPUT -Force -ErrorAction Ignore
        $failed = $false
        try { & $bind } catch { $failed = $true }
        Assert $failed "Preflight must reject $case."
        Assert (-not (Test-Path -LiteralPath $marker) -and -not (Test-Path -LiteralPath $env:GITHUB_OUTPUT)) 'Rejected source must not publish evidence.'
    }
    $env:SOURCE_REVISION = $revision
    foreach ($content in @($null, '', ('b' * 40), "$revision`n", 'unknown', $revision)) {
        Remove-Item -LiteralPath $marker -Force -ErrorAction Ignore
        if ($null -ne $content) { Set-Content -LiteralPath $marker -Value $content -NoNewline }
        $failed = $false
        try { & $verify } catch { $failed = $true }
        Assert ($failed -eq ($content -cne $revision)) 'Missing, empty, malformed or wrong-source license evidence must fail before publishing.'
    }
} finally {
    Set-Location $originalLocation
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}

foreach ($name in @('portable-tests', 'portable-policy', 'payload', 'windows-test-build', 'windows-test-partitions', 'installer-validation', 'installer',
    'portable-test-comparison', 'native-evidence-comparison')) {
    $condition = [regex]::Match((Get-Job $workflow $name), '(?m)^    if: (.+)$').Groups[1].Value
    $condition = $condition.Replace('needs.version.outputs.preflight-source', '$preflightSource').
        Replace('needs.version.outputs.source-revision', '$revision').
        Replace('needs.version.outputs.already-published', '$alreadyPublished').
        Replace('needs.version.outputs.candidate', '$candidate').
        Replace('github.event_name', '$eventName').
        Replace('github.ref', '$ref').
        Replace('inputs.compare_portable_tests', '$true').
        Replace('inputs.compare_native_evidence', '$true').
        Replace(' != ', ' -cne ').Replace(' == ', ' -ceq ').Replace(' && ', ' -and ')
    $evaluate = [scriptblock]::Create($condition)
    $alreadyPublished = 'false'
    $candidate = 'false'
    $ref = 'refs/heads/main'
    $eventName = 'workflow_dispatch'
    foreach ($result in $outcomes) {
        foreach ($preflightSource in @('', 'unknown', ('b' * 40), $revision)) {
            # Job-level conditions implicitly require successful dependencies unless a status function overrides it.
            $runs = $result -ceq 'success' -and (& $evaluate)
            Assert ($runs -eq ($result -ceq 'success' -and $preflightSource -ceq $revision)) "Incorrect expensive-job admission for $name, '$result', '$preflightSource'."
        }
    }
}
Write-Host "CI early preflight: $assertions assertions passed."
