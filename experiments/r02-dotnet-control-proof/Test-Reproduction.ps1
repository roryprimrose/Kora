[CmdletBinding()]
param([string] $EvidencePath = (Join-Path $PSScriptRoot '.candidate\source-reproduction-attempt.json'))
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PackageArtifacts.ps1')
if ((& dotnet --version) -ne '10.0.401') { throw 'RT1 requires .NET SDK 10.0.401.' }
$commit = 'f8ae645902b74b62cd47aac1fd9b29adaec3aff2'
$version = '1.0.16-rt1.source.f8ae645.1'
$cache = Join-Path $PSScriptRoot '.candidate'
$zip = Join-Path $cache 'sdk-source.zip'
if ((Get-FileHash -LiteralPath $zip).Hash -ine 'a73f4f2d8cb1eb9fd5353d3867707094e1efb7581599669afb55c2dcf48a53c3')
{
    throw 'SDK source archive mismatch.'
}
$trial = Join-Path $cache ('repro-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $trial | Out-Null
try
{
    Expand-Archive -LiteralPath $zip -DestinationPath $trial
    $source = Join-Path $trial "copilot-sdk-$commit"
    $project = Join-Path $source 'dotnet\src\GitHub.Copilot.SDK.csproj'
    & dotnet restore $project --configfile (Join-Path $PSScriptRoot 'NuGet.Config') `
        --packages (Join-Path $cache 'packages') --locked-mode `
        --lock-file-path (Join-Path $PSScriptRoot 'evidence\sdk-build.lock.json') `
        -p:RestorePackagesWithLockFile=true -p:TargetFrameworks=net10.0 -p:CopilotSkipCliDownload=true
    if ($LASTEXITCODE -ne 0) { throw 'Reproduction restore failed.' }
    $feed = Join-Path $trial 'feed'
    & dotnet pack $project --configuration Release --no-restore --output $feed `
        -p:TargetFrameworks=net10.0 -p:CopilotSkipCliDownload=true -p:CopilotCliVersion=1.0.90 `
        "-p:Version=$version" "-p:RepositoryCommit=$commit" "-p:SourceRevisionId=$commit" `
        -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false -p:ContinuousIntegrationBuild=true `
        "-p:PathMap=$source=Q:\rt1-source\copilot-sdk"
    if ($LASTEXITCODE -ne 0) { throw 'Reproduction pack failed.' }
    $package = Join-Path $feed "GitHub.Copilot.SDK.$version.nupkg"
    Set-DeterministicPackageTimestamp $package
    $original = (Get-FileHash -LiteralPath (Join-Path $cache "feed\GitHub.Copilot.SDK.$version.nupkg")).Hash
    $repeat = (Get-FileHash -LiteralPath $package).Hash
    $assembly = (Get-FileHash -LiteralPath (Join-Path $source 'dotnet\src\bin\Release\net10.0\GitHub.Copilot.SDK.dll')).Hash
    $comparison = Get-ReviewedSourceBuildComparison $repeat.ToLowerInvariant() $assembly.ToLowerInvariant()
    # Retain the observed bytes even on rejection; the clean source-only child is removed below.
    $artifacts = Join-Path $cache 'source-reproduction-artifacts'
    New-Item -ItemType Directory -Force $artifacts | Out-Null
    Copy-Item -LiteralPath $package -Destination (Join-Path $artifacts 'GitHub.Copilot.SDK.nupkg')
    Copy-Item -LiteralPath (Join-Path $source 'dotnet\src\bin\Release\net10.0\GitHub.Copilot.SDK.dll') -Destination $artifacts
    [ordered] @{
        observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        separateCleanSourceDirectory = $true
        sourceCommit = $commit
        sdkPackageSha256 = $original.ToLowerInvariant()
        reproducedPackageSha256 = $repeat.ToLowerInvariant()
        reproducedAssemblySha256 = $assembly.ToLowerInvariant()
        packageBytesEqual = ($original -eq $repeat)
        reviewedIdentity = $comparison
        qualification = if ($original -eq $repeat -and $comparison.profileMatchesReviewed)
        {
            'PASS: exact reviewed RT1 source-built package and assembly'
        } else { 'BLOCKED: repeatability alone does not qualify the reviewed exact-byte profile' }
        packaging = 'Unmodified SDK entry contents; ZIP timestamps normalized to 1980-01-01 UTC'
    } | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 $EvidencePath
    if ($original -ne $repeat) { throw 'Source package is not byte-reproducible.' }
    if (-not $comparison.profileMatchesReviewed)
    {
        throw 'Source build repeats locally but differs from reviewed RT1 package/assembly bytes.'
    }
    Write-Host 'Reviewed exact-byte SDK profile reproduced from a separate clean source directory.'
}
finally
{
    # This exact unique source-only child was created by this invocation.
    Remove-Item -LiteralPath $trial -Recurse -Force
}
