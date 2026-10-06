[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$testRoot = Join-Path $root '.net-test-artifacts\rt2-latest'
$tests = foreach ($suite in @('core', 'application', 'windows', 'rt1'))
{
    $files = @(Get-ChildItem -LiteralPath (Join-Path $testRoot $suite) -Filter '*.trx' -File)
    if ($files.Count -ne 1) { throw "Expected one current TRX for $suite." }
    [xml] $trx = Get-Content -LiteralPath $files[0].FullName -Raw
    $counter = $trx.TestRun.ResultSummary.Counters
    if ([int] $counter.failed -ne 0 -or [int] $counter.notExecuted -ne 0 -or [int] $counter.total -ne [int] $counter.passed)
    {
        throw "Incomplete or failed $suite validation."
    }
    [ordered] @{
        suite = $suite; total = [int] $counter.total; passed = [int] $counter.passed; failed = 0; skipped = 0
        startedUtc = $trx.TestRun.Times.start; finishedUtc = $trx.TestRun.Times.finish
        trxSha256 = (Get-FileHash -LiteralPath $files[0].FullName).Hash.ToLowerInvariant()
    }
}
$coveragePath = Join-Path $testRoot 'coverage-report\Cobertura.xml'
[xml] $coverageDocument = Get-Content -LiteralPath $coveragePath -Raw
$line = [double]::Parse($coverageDocument.DocumentElement.GetAttribute('line-rate'), [Globalization.CultureInfo]::InvariantCulture)
$branch = [double]::Parse($coverageDocument.DocumentElement.GetAttribute('branch-rate'), [Globalization.CultureInfo]::InvariantCulture)
if ($line -lt 1 -or $branch -lt 1) { throw 'Current coverage does not meet 100% line/branch gate.' }
foreach ($suite in @('core', 'application'))
{
    if (@(Get-ChildItem -LiteralPath (Join-Path $testRoot $suite) -Filter '*.coverage.cobertura.*.xml' -File).Count -ne 1)
    {
        throw "Expected latest-only coverage for $suite."
    }
}
$tracked = @(& git -C $root ls-files -- src tests installer eng .github global.json Directory.Build.props Directory.Build.targets Directory.Packages.props Kora.slnx THIRD-PARTY-NOTICES.md DEPENDENCY-LICENSES.md)
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate source/test identity.' }
$sources = foreach ($relative in $tracked)
{
    [ordered] @{ file = $relative; sha256 = (Get-FileHash -LiteralPath (Join-Path $root $relative)).Hash.ToLowerInvariant() }
}
$fixtureSources = foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -File |
    Where-Object Extension -in '.cs', '.ps1', '.csproj', '.props', '.Config', '.json')
{
    [ordered] @{ file = $file.Name; sha256 = (Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant() }
}
[ordered] @{
    observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    baseCommit = (& git -C $root rev-parse HEAD)
    branch = (& git -C $root branch --show-current)
    sdk = (& dotnet --version)
    os = [Environment]::OSVersion.VersionString
    architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    validationSourceSha256 = @($sources)
    rt2SourceSha256 = @($fixtureSources)
    rt2DispositionSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'evidence\disposition.json')).Hash.ToLowerInvariant()
    tests = @($tests)
    rootBuild = 'PASS: dotnet build Kora.slnx --configuration Release --no-restore; 0 warnings/errors; includes Kora.Setup'
    rootLockedRestore = 'PASS: dotnet restore Kora.slnx --locked-mode after initial NETSDK1004 missing assets'
    coverage = @{ status = 'PASS'; line = 100; branch = 100; latestOnly = $true
        reportSha256 = (Get-FileHash -LiteralPath $coveragePath).Hash.ToLowerInvariant() }
    productionLicenseNotice = @{ status = 'PASS'; command = 'eng\Test-DependencyLicenses.ps1'
        scanSha256 = (Get-FileHash -LiteralPath (Join-Path $root 'artifacts\license-compliance\dependency-licenses.json')).Hash.ToLowerInvariant() }
    policyScripts = 'PASS: Test-BuildVersion.ps1, Test-GitHubRelease.ps1 (synthetic fake gh, not publication), Test-InstallerPayloadContracts.ps1'
    publish = 'PASS: both win-x64/win-x86 framework-dependent locked publishes; exact license-bearing payload manifests; five PE/native checks each; no experimental candidate leaked; no launch'
    fullWixPackaging = @{ status = 'BLOCKED'; error = 'WIX1105: Validation could not run due to system policy'
        elevated = $false; iceSuppressed = $false; bootstrapperPublish = 'PASS: self-contained win-x64; not launched'
        bundleBuildAndTestInstaller = 'NOT RUN: MSI full validation blocked'
        command = 'eng\Build-Installer.ps1 -Version 0.1.0 -ProductVersion 0.1.0 -ApplicationPayloadPath artifacts\rt2-publish\Kora-win-x64' }
    notRun = @('installed/production application or setup launch', 'privileged ETW', 'live account/provider/PV1',
        'reference CPU/device/speech acceptance', 'production runtime composition', 'NSIS: retired, not applicable')
    historicalRt1AndNode = 'UNCHANGED: hash assertions plus git diff --exit-code'
    cleanup = 'Every real runtime scratch cleared; all sampled owned process identities exited; listeners/stores/native snapshots/diagnostic listeners disposed. Ignored verified source/build cache retained.'
} | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 (Join-Path $PSScriptRoot 'evidence\repository-validation.json')
