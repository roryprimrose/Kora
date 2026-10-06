. (Join-Path $PSScriptRoot 'GitHubRelease.Common.ps1')

$script:SourceToolVersion = '1.1.0'

function Read-SourceCommandBytes {
    param([string] $Command, [string[]] $Arguments, [int] $Limit = 16777216)
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = (Get-Command $Command -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    $buffer = [IO.MemoryStream]::new()
    try {
        if (!$process.Start()) { throw "$Command did not start." }
        $errorText = $process.StandardError.ReadToEndAsync()
        $chunk = [byte[]]::new(8192)
        while (($count = $process.StandardOutput.BaseStream.Read($chunk, 0, $chunk.Length)) -gt 0) {
            if ($buffer.Length + $count -gt $Limit) {
                $process.Kill($true)
                throw "$Command output exceeds the source-tool size bound."
            }
            $buffer.Write($chunk, 0, $count)
        }
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw "$Command failed: $($errorText.GetAwaiter().GetResult())" }
        return ,$buffer.ToArray()
    } finally {
        $buffer.Dispose()
        $process.Dispose()
    }
}

function Get-SourceBlobId {
    param([byte[]] $Bytes)
    $header = [Text.Encoding]::ASCII.GetBytes("blob $($Bytes.Length)`0")
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA1)
    try {
        $hash.AppendData($header)
        $hash.AppendData($Bytes)
        return [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
    } finally { $hash.Dispose() }
}

function Get-LocalSourceToolTree {
    param([string] $RepositoryPath, [ValidatePattern('^[a-f0-9]{40}$')][string] $Revision)
    Assert-NoLinks $RepositoryPath -AncestorsOnly
    $origin = (& git -C $RepositoryPath remote get-url origin | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $origin -cne $script:KoraSourceRepository) {
        throw 'Source-tool packaging requires the canonical origin.'
    }
    $resolved = (& git -C $RepositoryPath rev-parse "$Revision^{commit}" | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $resolved -cne $Revision) { throw 'Source-tool commit identity mismatch.' }
    $lines = @(& git -C $RepositoryPath ls-tree -r --full-tree $Revision -- (Get-SourceToolPaths))
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read the exact source-tool Git tree.' }
    $tree = @(
        foreach ($line in $lines) {
            if ($line -cnotmatch '^100644 blob ([a-f0-9]{40})\t(.+)$') {
                throw 'Source-tool tree contains a link or unsupported Git mode.'
            }
            @{ path = $Matches[2]; sha = $Matches[1]; type = 'blob'; mode = '100644' }
        }
    )
    Assert-SourceToolTree $tree
    return $tree
}

function Get-CanonicalSourceToolTree {
    param([ValidatePattern('^[a-f0-9]{40}$')][string] $Revision)
    $commit = ((Invoke-Gh @('api', "repos/$script:KoraRepository/git/commits/$Revision")) -join "`n") | ConvertFrom-Json
    if ($commit.sha -cne $Revision -or $commit.tree.sha -cnotmatch '^[a-f0-9]{40}$') {
        throw 'Canonical source commit identity mismatch.'
    }
    $tree = ((Invoke-Gh @('api', "repos/$script:KoraRepository/git/trees/$($commit.tree.sha)?recursive=1")) -join "`n") |
        ConvertFrom-Json
    if ($tree.sha -cne $commit.tree.sha -or $tree.truncated -isnot [bool] -or $tree.truncated) {
        throw 'Canonical source tree is incomplete or changed.'
    }
    $tools = @($tree.tree | Where-Object { $_.path -cin (Get-SourceToolPaths) })
    Assert-SourceToolTree $tools
    return $tools
}

function Assert-SourceToolTree {
    param($Tree)
    $paths = @(Get-SourceToolPaths)
    if (@($Tree).Count -ne $paths.Count) { throw 'Canonical source tree omits required tools.' }
    foreach ($path in $paths) {
        $match = @($Tree | Where-Object path -CEQ $path)
        if ($match.Count -ne 1 -or $match[0].type -cne 'blob' -or $match[0].mode -cne '100644' -or
            $match[0].sha -cnotmatch '^[a-f0-9]{40}$') {
            throw "Canonical tool identity/mode is invalid: $path"
        }
    }
}

function New-SourceToolArchive {
    param([string] $RepositoryPath, [string] $Revision, [string] $Version, [string] $Path, $Tree)
    Assert-SourceToolTree $Tree
    if (Test-Path -LiteralPath $Path) { throw 'Refusing to overwrite a source-tool archive.' }
    Assert-NoLinks (Split-Path -Parent ([IO.Path]::GetFullPath($Path))) -AncestorsOnly
    $archive = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $files = @(
            foreach ($relative in Get-SourceToolPaths) {
                $record = @($Tree | Where-Object path -CEQ $relative)[0]
                $bytes = Read-SourceCommandBytes 'git' @('-C', $RepositoryPath, 'cat-file', 'blob', $record.sha) -Limit 2097152
                if ((Get-SourceBlobId $bytes) -cne $record.sha) { throw "Git blob changed: $relative" }
                Write-SourceZipEntry $archive $relative $bytes
                [ordered]@{ path = $relative; bytes = $bytes.Length; gitBlob = $record.sha
                    sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() }
            }
        )
        $manifest = [ordered]@{
            schema = 1; bootstrapVersion = $script:SourceToolVersion; repository = $script:KoraSourceRepository
            revision = $Revision; version = $Version; unsigned = $true; productionAccepted = $false
            activation = 'unavailable'; provenance = 'exact canonical Git blobs; not an independent signature'
            files = $files
        }
        Write-SourceZipEntry $archive 'source-tools.json' ([Text.Encoding]::UTF8.GetBytes(
            ($manifest | ConvertTo-Json -Depth 8)))
    } finally { $archive.Dispose() }
}

function Write-SourceZipEntry {
    param([IO.Compression.ZipArchive] $Archive, [string] $Name, [byte[]] $Bytes, [int] $ExternalAttributes = 0)
    $entry = $Archive.CreateEntry($Name)
    $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
    $entry.ExternalAttributes = $ExternalAttributes
    $stream = $entry.Open()
    try { $stream.Write($Bytes, 0, $Bytes.Length) } finally { $stream.Dispose() }
}

function Read-SourceZipEntry {
    param([IO.Compression.ZipArchiveEntry] $Entry)
    if ($Entry.Length -le 0 -or $Entry.Length -gt 2097152 -or
        (($Entry.ExternalAttributes -shr 16) -band 0xf000) -notin @(0, 0x8000) -or
        ($Entry.ExternalAttributes -band [int][IO.FileAttributes]::ReparsePoint)) {
        throw 'Source-tool ZIP contains an empty, oversized or linked entry.'
    }
    $stream = $Entry.Open()
    $buffer = [IO.MemoryStream]::new()
    try {
        $chunk = [byte[]]::new(8192)
        while (($count = $stream.Read($chunk, 0, $chunk.Length)) -gt 0) {
            if ($buffer.Length + $count -gt $Entry.Length) { throw 'Source-tool ZIP entry length changed.' }
            $buffer.Write($chunk, 0, $count)
        }
        if ($buffer.Length -ne $Entry.Length) { throw 'Source-tool ZIP entry length changed.' }
        return ,$buffer.ToArray()
    } finally { $buffer.Dispose(); $stream.Dispose() }
}

function Test-SourceToolArchive {
    param([string] $Path, [string] $Revision, [string] $Version, $Tree, [string] $ExtractTo)
    Assert-SourceToolTree $Tree
    Assert-NoLinks $Path -AncestorsOnly
    if ((Get-Item -LiteralPath $Path).Length -gt 16777216) { throw 'Source-tool ZIP exceeds the size bound.' }
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $names = @((Get-SourceToolPaths)) + @('source-tools.json')
        if ($archive.Entries.Count -ne $names.Count) { throw 'Source-tool ZIP omits or adds tools/manifest.' }
        $content = [Collections.Generic.Dictionary[string, byte[]]]::new([StringComparer]::Ordinal)
        foreach ($entry in $archive.Entries) {
            # The closed exact path set rejects traversal, ADS, separator/case aliases and directories.
            if ($entry.FullName -cnotin $names -or $content.ContainsKey($entry.FullName)) {
                throw 'Source-tool ZIP has an unknown, aliased or duplicate path.'
            }
            $content.Add($entry.FullName, (Read-SourceZipEntry $entry))
        }
        $manifest = [Text.Encoding]::UTF8.GetString($content['source-tools.json']) | ConvertFrom-Json -AsHashtable
        if ($manifest.schema -isnot [long] -and $manifest.schema -isnot [int]) { throw 'Invalid source-tool manifest schema.' }
        if ($manifest.schema -ne 1 -or $manifest.bootstrapVersion -cne $script:SourceToolVersion -or
            $manifest.repository -cne $script:KoraSourceRepository -or $manifest.revision -cne $Revision -or
            $manifest.version -cne $Version -or $manifest.unsigned -isnot [bool] -or !$manifest.unsigned -or
            $manifest.productionAccepted -isnot [bool] -or $manifest.productionAccepted -or
            $manifest.activation -cne 'unavailable' -or
            $manifest.provenance -cne 'exact canonical Git blobs; not an independent signature' -or
            @($manifest.files).Count -ne (Get-SourceToolPaths).Count) {
            throw 'Source-tool manifest identity/assurance mismatch.'
        }
        foreach ($relative in Get-SourceToolPaths) {
            $records = @($manifest.files | Where-Object path -CEQ $relative)
            $canonical = @($Tree | Where-Object path -CEQ $relative)[0]
            $bytes = $content[$relative]
            if ($records.Count -ne 1 -or $records[0].bytes -isnot [long] -and $records[0].bytes -isnot [int] -or
                $records[0].bytes -ne $bytes.Length -or $records[0].gitBlob -cne $canonical.sha -or
                (Get-SourceBlobId $bytes) -cne $canonical.sha -or
                $records[0].sha256 -cne [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()) {
                throw "Source-tool bytes/manifest differ from exact canonical source: $relative"
            }
        }
        if ($ExtractTo) {
            New-ProofDirectory $ExtractTo
            New-Item -ItemType Directory -Path (Join-Path $ExtractTo 'eng') | Out-Null
            foreach ($relative in $names) {
                $target = Join-Path $ExtractTo $relative
                $stream = [IO.File]::Open($target, 'CreateNew', 'Write', 'None')
                try { $stream.Write($content[$relative], 0, $content[$relative].Length) } finally { $stream.Dispose() }
            }
        }
        return $manifest
    } finally { $archive.Dispose() }
}

function Select-SourceToolRelease {
    param($Releases, [ValidateSet('production', 'preview')][string] $Channel = 'production',
        [ValidatePattern('^[a-f0-9]{40}$')][string] $Revision)
    $eligible = @(
        foreach ($release in $Releases) {
            if ($release.draft -isnot [bool] -or $release.prerelease -isnot [bool]) { throw 'Unknown release channel state.' }
            if ($release.draft -or ($Channel -eq 'production' -and $release.prerelease)) { continue }
            if ($release.tag_name -cnotmatch '^v((0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-beta(0|[1-9]\d*))?)$') {
                continue
            }
            $version = $Matches[1]
            if ($Revision -and $release.target_commitish -cne $Revision) { continue }
            if (($release.id -isnot [long] -and $release.id -isnot [int]) -or $release.id -le 0) {
                throw 'Release has invalid typed identity.'
            }
            if ($release.target_commitish -cnotmatch '^[a-f0-9]{40}$') { throw 'Release has mutable source identity.' }
            Assert-CanonicalReleaseIdentity $release $version $release.target_commitish
            [pscustomobject]@{ Release = $release; Version = $version
                NumericVersion = [version]($version -split '-')[0]
                Stable = !$release.prerelease
                Beta = if ($release.prerelease) { [long](($version -split '-beta')[1]) } else { 0 } }
        }
    )
    if (!$eligible.Count) { throw "No published source-tool release matches channel '$Channel' and exact revision." }
    if (@($eligible | Group-Object Version | Where-Object Count -GT 1).Count) { throw 'Ambiguous release version.' }
    $selected = @($eligible | Sort-Object NumericVersion, Stable, Beta -Descending)[0]
    $assets = @($selected.Release.assets | Where-Object name -CEQ "Kora-$($selected.Version)-source-tools.zip")
    if ($assets.Count -ne 1 -or $assets[0].state -cne 'uploaded' -or
        ($assets[0].id -isnot [long] -and $assets[0].id -isnot [int]) -or
        ($assets[0].size -isnot [long] -and $assets[0].size -isnot [int]) -or
        $assets[0].id -le 0 -or $assets[0].size -le 0 -or $assets[0].size -gt 16777216 -or
        $assets[0].digest -cnotmatch '^sha256:[a-fA-F0-9]{64}$') {
        throw 'Selected release has missing, duplicate or invalid source-tool asset identity.'
    }
    return $selected
}

function Get-SourceToolResolution {
    param([ValidateSet('production', 'preview')][string] $Channel = 'production', [string] $Revision)
    $selection = @{ Releases = @(Get-CanonicalReleases); Channel = $Channel }
    if ($Revision) { $selection.Revision = $Revision }
    $selected = Select-SourceToolRelease @selection
    $release = Get-GitHubRecord "repos/$script:KoraRepository/releases/$($selected.Release.id)"
    if ($null -eq $release -or $release.id -ne $selected.Release.id) { throw 'Release changed during resolution; retry read-only.' }
    $current = Select-SourceToolRelease -Releases @($release) -Channel $Channel -Revision $selected.Release.target_commitish
    $originalAsset = @($selected.Release.assets | Where-Object name -CEQ "Kora-$($selected.Version)-source-tools.zip")[0]
    $asset = @($release.assets | Where-Object name -CEQ "Kora-$($current.Version)-source-tools.zip")[0]
    if ($current.Version -cne $selected.Version -or $asset.id -ne $originalAsset.id -or
        $asset.size -ne $originalAsset.size -or $asset.digest -ine $originalAsset.digest) {
        throw 'Release asset identity changed during resolution; retry read-only.'
    }
    $reference = Get-GitHubRecord "repos/$script:KoraRepository/git/ref/tags/$($release.tag_name)"
    if ($null -eq $reference) { throw 'Published source-tool release has no tag.' }
    Assert-TagSource $reference $release.target_commitish
    $tree = @(Get-CanonicalSourceToolTree $release.target_commitish)
    return [pscustomobject]@{
        repository = $script:KoraSourceRepository; revision = $release.target_commitish
        version = $selected.Version; channel = $Channel; releaseId = $release.id
        assetId = $asset.id; assetName = $asset.name; bytes = $asset.size
        sha256 = $asset.digest.Substring(7).ToLowerInvariant(); tree = $tree
        unsigned = $true; productionAccepted = $false; activation = 'unavailable'
    }
}

function Save-SourceToolAsset {
    param($Resolution, [string] $Path)
    $bytes = Read-SourceCommandBytes 'gh' @('api',
        "repos/$script:KoraRepository/releases/assets/$($Resolution.assetId)", '-H', 'Accept: application/octet-stream')
    if ($bytes.Length -ne $Resolution.bytes -or
        [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() -cne $Resolution.sha256) {
        throw 'Downloaded source-tool asset differs from resolved final bytes.'
    }
    $stream = [IO.File]::Open($Path, 'CreateNew', 'Write', 'None')
    try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
}

function Invoke-SourceToolAcquisition {
    param($Resolution, [string] $OutputDirectory)
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    $archive = Join-Path $OutputDirectory $Resolution.assetName
    $tools = Join-Path $OutputDirectory 'tools'
    $receiptPath = Join-Path $OutputDirectory 'acquisition.json'
    if (Test-Path -LiteralPath $OutputDirectory) {
        Assert-NoLinks $OutputDirectory
        if (!(Test-Path -LiteralPath $receiptPath -PathType Leaf)) { throw 'Unowned/incomplete source-tool output retained; choose a new directory.' }
        $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json -AsHashtable
        if (($receipt | ConvertTo-Json -Depth 20 -Compress) -cne
            ($Resolution | ConvertTo-Json -Depth 20 -Compress)) { throw 'Source-tool acquisition identity changed; no overwrite.' }
    } else {
        New-ProofDirectory $OutputDirectory
        Save-SourceToolAsset $Resolution $archive
        Test-SourceToolArchive $archive $Resolution.revision $Resolution.version $Resolution.tree -ExtractTo $tools | Out-Null
        Write-ProofJson $Resolution $receiptPath
    }
    if ((Get-Item -LiteralPath $archive).Length -ne $Resolution.bytes -or
        (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() -cne $Resolution.sha256) {
        throw 'Acquired source-tool archive changed.'
    }
    $manifest = Test-SourceToolArchive $archive $Resolution.revision $Resolution.version $Resolution.tree
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try { $manifestBytes = Read-SourceZipEntry ($zip.GetEntry('source-tools.json')) }
    finally { $zip.Dispose() }
    $expected = @($manifest.files) + @(@{ path = 'source-tools.json'
        bytes = $manifestBytes.Length
        sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($manifestBytes)).ToLowerInvariant() })
    $actual = @(Get-PayloadFiles $tools)
    if ($actual.Count -ne $expected.Count) { throw 'Acquired tool inventory changed.' }
    foreach ($record in $expected) {
        $match = @($actual | Where-Object path -CEQ $record.path)
        if ($match.Count -ne 1 -or $match[0].bytes -ne $record.bytes -or $match[0].sha256 -cne $record.sha256) {
            throw "Acquired tool bytes changed: $($record.path)"
        }
    }
    $rootEntries = @(Get-ChildItem -LiteralPath $OutputDirectory -Force)
    $actualNames = @($rootEntries.Name | Sort-Object) -join '|'
    $expectedNames = @($Resolution.assetName, 'acquisition.json', 'tools') | Sort-Object
    if ($rootEntries.Count -ne 3 -or $actualNames -cne ($expectedNames -join '|')) {
        throw 'Source-tool acquisition output inventory changed.'
    }
    return [pscustomobject]@{ tools = $tools; revision = $Resolution.revision; verified = $true
        codeExecuted = $false; activation = 'unavailable' }
}
