function Set-DeterministicPackageTimestamp([string] $Path)
{
    # NuGet pack timestamps vary; normalize ZIP metadata only, never entry contents.
    $archive = [System.IO.Compression.ZipFile]::Open($Path, [System.IO.Compression.ZipArchiveMode]::Update)
    try
    {
        foreach ($entry in $archive.Entries)
        {
            $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        }
    }
    finally { $archive.Dispose() }
}

function Get-ReviewedSourceBuildComparison([string] $PackageHash, [string] $AssemblyHash)
{
    $reviewed = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence\disposition.json') -Raw |
        ConvertFrom-Json
    foreach ($hash in @($PackageHash, $AssemblyHash, $reviewed.sdkPackageSha256, $reviewed.sdkAssemblySha256))
    {
        if ($hash -cnotmatch '^[a-f0-9]{64}$') { throw 'Missing or malformed source-build identity.' }
    }
    [ordered] @{
        reviewedPackageSha256 = $reviewed.sdkPackageSha256
        reviewedAssemblySha256 = $reviewed.sdkAssemblySha256
        packageMatchesReviewed = $PackageHash -ceq $reviewed.sdkPackageSha256
        assemblyMatchesReviewed = $AssemblyHash -ceq $reviewed.sdkAssemblySha256
        profileMatchesReviewed = ($PackageHash -ceq $reviewed.sdkPackageSha256) -and
            ($AssemblyHash -ceq $reviewed.sdkAssemblySha256)
    }
}
