[CmdletBinding()]
param([switch] $UpdateFixtureLock)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PackageArtifacts.ps1')
$root = $PSScriptRoot
$cache = Join-Path $root '.candidate'
$sourceCommit = 'f8ae645902b74b62cd47aac1fd9b29adaec3aff2'
$sourceHash = 'a73f4f2d8cb1eb9fd5353d3867707094e1efb7581599669afb55c2dcf48a53c3'
$runtimeHash = '2b2c23816461f7616eb16c7836af7701d6683367029405d51054c4366d1d41c0'
$config = Join-Path $root 'NuGet.Config'
$version = '1.0.16-rt1.source.f8ae645.1'
$packages = Join-Path $cache 'packages'
if ((& dotnet --version) -ne '10.0.401') { throw 'RT1 source build requires exactly .NET SDK 10.0.401.' }

function Get-VerifiedArchive([string] $Url, [string] $Path, [string] $Hash)
{
    if (-not (Test-Path -LiteralPath $Path))
    {
        & curl.exe --fail --location --silent --show-error $Url --output $Path
        if ($LASTEXITCODE -ne 0) { throw 'Public dependency acquisition failed.' }
    }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ine $Hash)
    {
        throw 'Public archive digest does not match the reviewed candidate.'
    }
}

New-Item -ItemType Directory -Force $cache | Out-Null
$sourceZip = Join-Path $cache 'sdk-source.zip'
$runtimeZip = Join-Path $cache 'runtime.tgz'
Get-VerifiedArchive "https://codeload.github.com/github/copilot-sdk/zip/$sourceCommit" $sourceZip $sourceHash
Get-VerifiedArchive 'https://github.com/github/copilot-cli/releases/download/v1.0.90/github-copilot-1.0.90-win32-x64.tgz' $runtimeZip $runtimeHash
$source = Join-Path $cache "source\copilot-sdk-$sourceCommit"
if (-not (Test-Path -LiteralPath $source))
{
    Expand-Archive -LiteralPath $sourceZip -DestinationPath (Join-Path $cache 'source')
}
# Verify reused source against the reviewed archive before compiling it.
$zip = [System.IO.Compression.ZipFile]::OpenRead($sourceZip)
$sourceFiles = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
try
{
    foreach ($entry in $zip.Entries | Where-Object { $_.Name -ne '' })
    {
        $relative = $entry.FullName.Substring($entry.FullName.IndexOf('/') + 1).Replace('/', '\')
        $file = Join-Path $source $relative
        $sourceFiles.Add([System.IO.Path]::GetFullPath($file)) | Out-Null
        $stream = $entry.Open()
        try { $expected = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        if (-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file).Hash -ine $expected)
        {
            throw 'Reviewed SDK source was altered; refusing to compile.'
        }
    }
}
finally { $zip.Dispose() }
$sdkDirectory = Join-Path $source 'dotnet\src'
foreach ($file in Get-ChildItem -LiteralPath $sdkDirectory -Filter '*.cs' -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' })
{
    if (-not $sourceFiles.Contains($file.FullName)) { throw 'Unreviewed SDK compile input detected.' }
}
$runtime = Join-Path $cache 'runtime'
if (-not (Test-Path -LiteralPath (Join-Path $runtime 'runtime.node')))
{
    $archive = Join-Path $cache 'runtime-review'
    New-Item -ItemType Directory -Force $archive | Out-Null
    & tar -xzf $runtimeZip -C $archive
    if ($LASTEXITCODE -ne 0) { throw 'Runtime archive extraction failed.' }
    New-Item -ItemType Directory -Force $runtime | Out-Null
    foreach ($file in @('copilot-runtime.exe', 'runtime.node'))
    {
        Copy-Item -LiteralPath (Join-Path $archive "package\prebuilds\win32-x64\$file") -Destination $runtime
    }
    Copy-Item -LiteralPath (Join-Path $archive 'package\LICENSE.md') -Destination $runtime
}

$project = Join-Path $source 'dotnet\src\GitHub.Copilot.SDK.csproj'
$localFeed = Join-Path $cache 'feed'
New-Item -ItemType Directory -Force $localFeed | Out-Null
$lock = Join-Path $root 'evidence\sdk-build.lock.json'
[string[]] $sdkLockOption = @(if (Test-Path -LiteralPath $lock) { '--locked-mode' })
# Archive provenance replaces VCS discovery; normalize paths without modifying SDK source.
& dotnet restore $project --configfile $config --packages $packages --lock-file-path $lock @sdkLockOption `
    -p:RestorePackagesWithLockFile=true -p:TargetFrameworks=net10.0 -p:CopilotSkipCliDownload=true
if ($LASTEXITCODE -ne 0) { throw 'Unmodified SDK source restore failed.' }
if (-not (Test-Path -LiteralPath (Join-Path $localFeed "GitHub.Copilot.SDK.$version.nupkg")))
{
    & dotnet pack $project --configuration Release --no-restore --output $localFeed `
        -p:TargetFrameworks=net10.0 -p:CopilotSkipCliDownload=true -p:CopilotCliVersion=1.0.90 `
        "-p:Version=$version" "-p:RepositoryCommit=$sourceCommit" "-p:SourceRevisionId=$sourceCommit" `
        -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false -p:ContinuousIntegrationBuild=true `
        "-p:PathMap=$source=Q:\rt1-source\copilot-sdk"
    if ($LASTEXITCODE -ne 0) { throw 'Unmodified SDK source pack failed.' }
}
Set-DeterministicPackageTimestamp (Join-Path $localFeed "GitHub.Copilot.SDK.$version.nupkg")
[string[]] $fixtureLockOption = @(if ((Test-Path -LiteralPath (Join-Path $root 'packages.lock.json')) -and -not $UpdateFixtureLock)
{
    '--locked-mode'
} elseif ($UpdateFixtureLock) { '--force-evaluate' })
& dotnet restore (Join-Path $root 'ControlProof.csproj') --configfile $config --packages $packages @fixtureLockOption
if ($LASTEXITCODE -ne 0) { throw 'Fixture restore failed.' }
