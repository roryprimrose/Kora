[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path (Join-Path (Join-Path $PSScriptRoot '..') 'artifacts') 'license-compliance'),
    [switch] $UpdateNotice
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
$artifactPrefix = $artifactRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $outputPath.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase))
{
    throw "OutputDirectory must be a child of $artifactRoot."
}

$generatedNotice = Join-Path $outputPath 'THIRD-PARTY-NOTICES.md'
$jsonReport = Join-Path $outputPath 'dependency-licenses.json'
$committedNotice = Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md'
$downloadPath = Join-Path $outputPath 'licenses'
$packageNoticePath = Join-Path $outputPath 'package-notices'
$licenseDirectory = Join-Path $PSScriptRoot 'licenses'
$allowedLicenses = Join-Path $licenseDirectory 'allowed-licenses.json'
$packageOverrides = Join-Path $licenseDirectory 'package-overrides.json'
$packageRoot = if ($env:NUGET_PACKAGES)
{
    [System.IO.Path]::GetFullPath($env:NUGET_PACKAGES)
}
else
{
    Join-Path (Join-Path $HOME '.nuget') 'packages'
}
$fileLicenseMappings = Join-Path $outputPath 'license-file-mappings.json'
$identities = Get-Content -LiteralPath (Join-Path $licenseDirectory 'license-file-identities.json') -Raw |
    ConvertFrom-Json
$mappings = @{}
foreach ($identity in $identities)
{
    $packageDirectory = Join-Path (Join-Path $packageRoot $identity.Id.ToLowerInvariant()) $identity.Version
    $licensePath = Join-Path $packageDirectory $identity.File
    if ((Get-FileHash -LiteralPath $licensePath -Algorithm SHA256).Hash -cne $identity.Sha256)
    {
        throw "Reviewed license bytes changed for $($identity.Id) $($identity.Version)."
    }
    $mappings[$licensePath] = $identity.License
}

New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$mappings | ConvertTo-Json | Set-Content -LiteralPath $fileLicenseMappings
foreach ($generatedDirectory in @($downloadPath, $packageNoticePath))
{
    if (Test-Path $generatedDirectory)
    {
        Remove-Item -Path $generatedDirectory -Recurse -Force
    }
}

& dotnet nuget-license `
    --input (Join-Path $repositoryRoot 'Kora.slnx') `
    --include-transitive `
    --allowed-license-types $allowedLicenses `
    --override-package-information $packageOverrides `
    --licensefile-to-license-mappings $fileLicenseMappings `
    --output Markdown `
    --file-output $generatedNotice `
    --license-information-download-location $downloadPath

if ($LASTEXITCODE -ne 0)
{
    throw "Dependency license validation failed with exit code $LASTEXITCODE. Review $generatedNotice."
}

if (-not (Test-Path $generatedNotice))
{
    throw "The dependency license scanner did not create $generatedNotice."
}

& dotnet nuget-license `
    --input (Join-Path $repositoryRoot 'Kora.slnx') `
    --include-transitive `
    --allowed-license-types $allowedLicenses `
    --override-package-information $packageOverrides `
    --licensefile-to-license-mappings $fileLicenseMappings `
    --output JsonPretty `
    --file-output $jsonReport

if ($LASTEXITCODE -ne 0)
{
    throw "Dependency license JSON generation failed with exit code $LASTEXITCODE. Review $jsonReport."
}

New-Item -ItemType Directory -Path $packageNoticePath -Force | Out-Null
$packages = Get-Content -Path $jsonReport -Raw | ConvertFrom-Json
foreach ($package in $packages | Sort-Object PackageId, PackageVersion -Unique)
{
    $packageId = $package.PackageId.ToLowerInvariant()
    $packageVersion = $package.PackageVersion.ToLowerInvariant()
    $packageDirectory = Join-Path (Join-Path $packageRoot $packageId) $packageVersion
    $packageArchive = Join-Path $packageDirectory "$packageId.$packageVersion.nupkg"
    if (-not (Test-Path $packageArchive))
    {
        throw "Cannot inspect notices because the restored package archive is missing: $packageArchive"
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($packageArchive)
    try
    {
        foreach ($entry in $archive.Entries | Where-Object Name -Match '^(NOTICE|THIRD[-_. ]?PARTY)')
        {
            $entryName = $entry.FullName -replace '[^A-Za-z0-9._-]', '_'
            $destinationName = "$($package.PackageId)__$($package.PackageVersion)__$entryName"
            $destination = Join-Path $packageNoticePath $destinationName
            $entryStream = $entry.Open()
            $destinationStream = [System.IO.File]::Create($destination)
            try
            {
                $entryStream.CopyTo($destinationStream)
            }
            finally
            {
                $destinationStream.Dispose()
                $entryStream.Dispose()
            }
        }
    }
    finally
    {
        $archive.Dispose()
    }
}

if ($UpdateNotice)
{
    Copy-Item -Path $generatedNotice -Destination $committedNotice -Force
    Write-Host "Updated $committedNotice."
}
elseif (-not (Test-Path $committedNotice))
{
    throw "Missing $committedNotice. Run this script with -UpdateNotice after reviewing the report."
}
else
{
    $generatedContent = Get-Content -Path $generatedNotice -Raw
    $committedContent = Get-Content -Path $committedNotice -Raw
    if ($generatedContent -cne $committedContent)
    {
        throw 'THIRD-PARTY-NOTICES.md is stale. Review dependency changes, then run this script with -UpdateNotice.'
    }
}

Copy-Item -Path (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $outputPath 'KORA-LICENSE.txt') -Force
Write-Host "Dependency licenses are approved and notices are current. Audit files: $outputPath"
