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
