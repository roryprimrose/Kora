[CmdletBinding()]
param([switch] $AllowFailedIndependentReproduction)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
$original = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\r02-dotnet-control-proof'))
$cache = Join-Path $PSScriptRoot '.candidate'
$stageRecord = Join-Path $cache 'Stage.props'
New-Item -ItemType Directory -Force $cache | Out-Null
if (Test-Path -LiteralPath $stageRecord)
{
    [xml] $existing = Get-Content -LiteralPath $stageRecord -Raw
    $stage = [string] $existing.Project.PropertyGroup.Rt1Stage
    if (-not $stage.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path $stage -Leaf) -notmatch '^(rt2-[a-f0-9]{8}|kora-rt2-[a-f0-9]{32})$')
    {
        throw 'Unsafe RT2 staging cache location.'
    }
}
else
{
    # Short, uniquely owned path avoids native MSBuild MAX_PATH failures without
    # changing machine-wide long-path policy or the approved SDK build recipe.
    $stage = Join-Path ([IO.Path]::GetTempPath()) ('rt2-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    if (Test-Path -LiteralPath $stage) { throw 'RT2 staging collision; refusing existing unowned directory.' }
    $escaped = [Security.SecurityElement]::Escape($stage)
    "<Project><PropertyGroup><Rt1Stage>$escaped</Rt1Stage></PropertyGroup></Project>" |
        Set-Content -LiteralPath $stageRecord -Encoding utf8
}
$evidence = Join-Path $PSScriptRoot 'evidence'
New-Item -ItemType Directory -Force $stage, $evidence | Out-Null
$files = @(Get-ChildItem -LiteralPath $original -File -Force)
$files += @(Get-ChildItem -LiteralPath (Join-Path $original 'evidence') -File)
$identities = foreach ($file in $files)
{
    $relative = [IO.Path]::GetRelativePath($original, $file.FullName)
    $destination = Join-Path $stage $relative
    New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
    $hash = (Get-FileHash -LiteralPath $file.FullName).Hash
    if ((Get-FileHash -LiteralPath $destination).Hash -ne $hash) { throw "Staged RT1 bytes differ: $relative" }
    [ordered] @{ file = $relative; sha256 = $hash.ToLowerInvariant() }
}
# Invoke the original recipes only in the private staging area. Their generated
# evidence cannot overwrite the retained RT1 witness.
& (Join-Path $stage 'Prepare-Candidate.ps1')
try { & (Join-Path $stage 'Test-Reproduction.ps1') }
catch [System.Management.Automation.RuntimeException]
{
    if (-not (Test-Path -LiteralPath (Join-Path $stage 'evidence\source-reproduction.json'))) { throw }
    $failed = Get-Content -LiteralPath (Join-Path $stage 'evidence\source-reproduction.json') -Raw | ConvertFrom-Json
    if ($failed.packageBytesEqual -or -not $AllowFailedIndependentReproduction) { throw }
    Write-Warning 'Independent RT1 source reproduction FAILED. Only the byte-verified primary build may be trialled; RT2 cannot pass.'
}
$reproduced = Get-Content -LiteralPath (Join-Path $stage 'evidence\source-reproduction.json') -Raw | ConvertFrom-Json
$primaryAssembly = Join-Path $stage '.candidate\source\copilot-sdk-f8ae645902b74b62cd47aac1fd9b29adaec3aff2\dotnet\src\bin\Release\net10.0\GitHub.Copilot.SDK.dll'
if ($reproduced.sdkPackageSha256 -ne '0b609b73c868099d64e7d5320b927d760d528756ab7c5db0b5692f5dccbf9b3f')
{
    throw 'Primary package bytes differ from the approved RT1 candidate.'
}
if ((Get-FileHash -LiteralPath $primaryAssembly).Hash -ine 'afb0715225f794d1b663ef72da1336b44b665e50b330087e1356380a5eac10d6' -or
    ($reproduced.packageBytesEqual -and $reproduced.reproducedAssemblySha256 -ne 'afb0715225f794d1b663ef72da1336b44b665e50b330087e1356380a5eac10d6'))
{
    throw 'Reproduced bytes do not match the approved RT1 candidate.'
}
[xml] $config = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'NuGet.Config') -Raw
$config.configuration.packageSources.add[0].SetAttribute('value', (Join-Path $stage '.candidate\feed'))
$configPath = Join-Path $cache 'NuGet.Config'
$config.Save($configPath)
& dotnet restore (Join-Path $PSScriptRoot 'LifecycleProof.csproj') --locked-mode `
    --configfile $configPath --packages (Join-Path $stage '.candidate\packages')
if ($LASTEXITCODE -ne 0) { throw 'RT2 locked restore failed.' }
[ordered] @{
    observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    baseCommit = (& git rev-parse HEAD)
    rt1SourceFiles = $identities
    cleanSourceReproduction = $reproduced
    independentReproduction = $(if ($reproduced.packageBytesEqual) { 'PASS' } else { 'FAIL: not byte-identical; RT2 acceptance blocked' })
    originalEvidenceUnchanged = $true
} | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 (Join-Path $evidence 'preparation.json')
