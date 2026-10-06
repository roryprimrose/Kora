#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string] $OutputDirectory)
. (Join-Path $PSScriptRoot 'SourceTools.Common.ps1')
New-ProofDirectory $OutputDirectory
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$repositoryPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$revision = (& git -C $repositoryPath rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify contract source.' }
$version = '0.1.0-beta9'
$tree = @(Get-LocalSourceToolTree $repositoryPath $revision)
$archivePath = Join-Path $OutputDirectory "Kora-$version-source-tools.zip"
$passed = [Collections.Generic.List[string]]::new()
function Check {
    param([bool] $Condition, [string] $Name)
    if (!$Condition) { throw $Name }
    $passed.Add($Name)
}
function Reject {
    param([string] $Name, [scriptblock] $Operation)
    $failure = $null
    try { & $Operation | Out-Null } catch { $failure = $_ }
    Check ($null -ne $failure) $Name
}
New-SourceToolArchive $repositoryPath $revision $version $archivePath $tree
$manifest = Test-SourceToolArchive $archivePath $revision $version $tree
Check (@($manifest.files).Count -eq 8 -and $manifest.unsigned -and !$manifest.productionAccepted) `
    'Complete eight-tool inventory, exact Git blobs/digests and truthful unsigned provenance'
foreach ($record in $tree) {
    $text = [Text.Encoding]::UTF8.GetString((Read-SourceCommandBytes 'git' @('-C', $repositoryPath, 'cat-file', 'blob', $record.sha)))
    foreach ($match in [regex]::Matches($text, 'Join-Path \$PSScriptRoot ''([^'']+\.ps1)''')) {
        Check (('eng/' + $match.Groups[1].Value.Replace('\', '/')) -cin (Get-SourceToolPaths)) `
            "Declared tool closure includes dependency of $($record.path): $($match.Groups[1].Value)"
    }
}
$second = Join-Path $OutputDirectory 'second.zip'
New-SourceToolArchive $repositoryPath $revision $version $second $tree
Check ((Get-FileHash $archivePath).Hash -ceq (Get-FileHash $second).Hash) 'Same exact tool bytes produce a retry-stable ZIP'
Reject 'Packaging never overwrites an existing archive' { New-SourceToolArchive $repositoryPath $revision $version $archivePath $tree }
Reject 'Missing canonical tool tree fails closed' { Test-SourceToolArchive $archivePath $revision $version @($tree | Select-Object -Skip 1) }
Reject 'Wrong source revision refused even with unchanged archive' { Test-SourceToolArchive $archivePath ('0' * 40) $version $tree }
Reject 'Wrong release version refused' { Test-SourceToolArchive $archivePath $revision '9.9.9' $tree }
$wrongTree = $tree | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$wrongTree[0].sha = '0' * 40
Reject 'Self-described provenance cannot replace canonical tool-byte identity' { Test-SourceToolArchive $archivePath $revision $version $wrongTree }
$wrongTree[0].mode = '120000'
Reject 'Canonical source links refused' { Test-SourceToolArchive $archivePath $revision $version $wrongTree }

function New-HostileArchive {
    param([string] $Case)
    $path = Join-Path $OutputDirectory "$Case.zip"
    $original = [IO.Compression.ZipFile]::OpenRead($archivePath)
    $zip = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $original.Entries) {
            $name = $entry.FullName
            $bytes = Read-SourceZipEntry $entry
            if ($name -ceq 'source-tools.json') {
                if ($Case -eq 'missing-manifest') { continue }
                $value = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json -AsHashtable
                switch ($Case) {
                    'invalid-json' { $bytes = [Text.Encoding]::UTF8.GetBytes('{broken') }
                    'missing-field' { $value.Remove('revision'); $bytes = [Text.Encoding]::UTF8.GetBytes(($value | ConvertTo-Json -Depth 8)) }
                    'changed-manifest' { $value.files[0].sha256 = '0' * 64; $bytes = [Text.Encoding]::UTF8.GetBytes(($value | ConvertTo-Json -Depth 8)) }
                    'omitted-tool-record' { $value.files = @($value.files | Select-Object -Skip 1); $bytes = [Text.Encoding]::UTF8.GetBytes(($value | ConvertTo-Json -Depth 8)) }
                    'duplicate-tool-record' { $value.files[0] = $value.files[1]; $bytes = [Text.Encoding]::UTF8.GetBytes(($value | ConvertTo-Json -Depth 8)) }
                    'hostile-repository' { $value.repository = 'https://example.invalid/fork.git'; $bytes = [Text.Encoding]::UTF8.GetBytes(($value | ConvertTo-Json -Depth 8)) }
                    'false-signature' { $value.unsigned = $false; $bytes = [Text.Encoding]::UTF8.GetBytes(($value | ConvertTo-Json -Depth 8)) }
                    'trusted-production' { $value.productionAccepted = $true; $bytes = [Text.Encoding]::UTF8.GetBytes(($value | ConvertTo-Json -Depth 8)) }
                    'wrong-type' { $value.files[0].bytes = "$($value.files[0].bytes)"; $bytes = [Text.Encoding]::UTF8.GetBytes(($value | ConvertTo-Json -Depth 8)) }
                    'forged-tool' {
                        $forged = [Text.Encoding]::UTF8.GetBytes('throw "Downloaded code must never run"')
                        $value.files[0].bytes = $forged.Length
                        $value.files[0].gitBlob = Get-SourceBlobId $forged
                        $value.files[0].sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($forged)).ToLowerInvariant()
                        $bytes = [Text.Encoding]::UTF8.GetBytes(($value | ConvertTo-Json -Depth 8))
                    }
                }
            }
            if ($name -ceq $manifest.files[0].path) {
                if ($Case -eq 'omitted-tool') { continue }
                switch ($Case) {
                    'traversal' { $name = '../escaped.ps1' }
                    'absolute' { $name = 'C:/escaped.ps1' }
                    'backslash-alias' { $name = $name.Replace('/', '\') }
                    'case-alias' { $name = $name.ToUpperInvariant() }
                    'ads' { $name += ':stream' }
                    'dot-alias' { $name = 'eng/./Invoke-SourceBootstrap.ps1' }
                    'directory' { $name = 'eng/' }
                    'changed-tool' { $bytes = [Text.Encoding]::UTF8.GetBytes('throw "Downloaded code must never run"') }
                    'forged-tool' { $bytes = [Text.Encoding]::UTF8.GetBytes('throw "Downloaded code must never run"') }
                    'empty-tool' { $bytes = [byte[]]::new(0) }
                    'oversized-entry' { $bytes = [byte[]]::new(2097153) }
                }
            }
            $attributes = if ($Case -eq 'symlink' -and $name -ceq $manifest.files[0].path) {
                [int]::MinValue -bor 0x21ff0000
            } elseif ($Case -eq 'reparse' -and $name -ceq $manifest.files[0].path) { 1024 }
            else { 0 }
            Write-SourceZipEntry $zip $name $bytes -ExternalAttributes $attributes
        }
        if ($Case -eq 'duplicate') {
            Write-SourceZipEntry $zip $manifest.files[0].path ([Text.Encoding]::UTF8.GetBytes('duplicate'))
        }
        if ($Case -eq 'extra-tool') {
            Write-SourceZipEntry $zip 'eng/extra.ps1' ([Text.Encoding]::UTF8.GetBytes('unlisted code'))
        }
    } finally { $zip.Dispose(); $original.Dispose() }
    return $path
}
foreach ($case in 'missing-manifest', 'invalid-json', 'missing-field', 'changed-manifest', 'omitted-tool-record',
    'duplicate-tool-record', 'hostile-repository', 'false-signature', 'trusted-production', 'wrong-type',
    'traversal', 'absolute', 'backslash-alias', 'case-alias', 'ads', 'dot-alias', 'omitted-tool',
    'changed-tool', 'forged-tool', 'symlink', 'reparse', 'directory', 'empty-tool', 'oversized-entry', 'duplicate', 'extra-tool') {
    $hostile = New-HostileArchive $case
    $output = Join-Path $OutputDirectory "extract-$case"
    Reject "Hostile $case refused before extraction" { Test-SourceToolArchive $hostile $revision $version $tree -ExtractTo $output }
    Check (!(Test-Path -LiteralPath $output)) "Hostile $case created no extraction root"
}
Check (!(Test-Path -LiteralPath (Join-Path $OutputDirectory 'escaped.ps1'))) 'Traversal never escapes extraction'
$oversizedArchive = Join-Path $OutputDirectory 'oversized-archive.zip'
[IO.File]::WriteAllBytes($oversizedArchive, [byte[]]::new(16777217))
Reject 'Archive over the exact 16 MiB bound refused before ZIP parsing' {
    Test-SourceToolArchive $oversizedArchive $revision $version $tree
}
Remove-Item -LiteralPath $oversizedArchive

function New-Release {
    param([string] $Version, [bool] $Draft = $false, [string] $Revision = $revision)
    [pscustomobject]@{
        id = 17; draft = $Draft; prerelease = $Version -like '*-beta*'; tag_name = "v$Version"
        target_commitish = $Revision; body = "<!-- kora-source: $Revision -->"
        assets = @([pscustomobject]@{ id = 42; name = "Kora-$Version-source-tools.zip"; state = 'uploaded'
            size = (Get-Item $archivePath).Length; digest = "sha256:$((Get-FileHash $archivePath).Hash.ToLowerInvariant())" })
    }
}
$stable = New-Release '0.0.9'
$preview = New-Release $version
$draft = New-Release '9.0.0' $true
$releases = @($draft, $preview, $stable)
Check ((Select-SourceToolRelease $releases).Version -ceq '0.0.9') 'Default production excludes drafts and prereleases'
Check ((Select-SourceToolRelease $releases preview).Version -ceq $version) 'Explicit preview admits only published prereleases, not drafts'
Check ((Select-SourceToolRelease @((New-Release '0.1.0-beta10'), $preview) preview).Version -ceq '0.1.0-beta10') `
    'Beta increments are ordered numerically'
Check ((Select-SourceToolRelease @((New-Release '0.1.0-beta100'), (New-Release '0.1.0')) preview).Version -ceq '0.1.0') `
    'Stable wins over prerelease for the same numeric version'
Reject 'Draft-only channel has no usable candidate' { Select-SourceToolRelease @($draft) preview }
Reject 'Preview requires explicit opt-in under production' { Select-SourceToolRelease @($preview) }
Reject 'Missing exact-revision release refused' { Select-SourceToolRelease $releases preview ('0' * 40) }
Reject 'Ambiguous releases refused' { Select-SourceToolRelease @($preview, $preview) preview }
$mutable = New-Release $version $false 'main'
Reject 'Moving main cannot establish immutable source identity' { Select-SourceToolRelease @($mutable) preview }
foreach ($field in 'digest', 'name', 'state', 'size') {
    $bad = New-Release $version
    if ($field -eq 'size') { $bad.assets[0].size = 0 } else { $bad.assets[0].$field = 'wrong' }
    Reject "Invalid asset $field refused" { Select-SourceToolRelease @($bad) preview }
}
$bad = New-Release $version
$bad.assets = @($bad.assets[0], $bad.assets[0])
Reject 'Duplicate source-tool assets refused' { Select-SourceToolRelease @($bad) preview }
$bad.assets = @()
Reject 'Legacy release without source tools fails visibly; no silent fallback' { Select-SourceToolRelease @($bad) preview }
$bad = New-Release $version
$bad.body = "<!-- kora-source: $('0' * 40) -->"
Reject 'Descriptive provenance mismatch refused' { Select-SourceToolRelease @($bad) preview }
$bad.prerelease = 'true'
Reject 'Unknown typed channel fails closed' { Select-SourceToolRelease @($bad) preview }
$bad = New-Release $version
$bad.id = '17'
Reject 'String release IDs are not canonical typed identity' { Select-SourceToolRelease @($bad) preview }
$bad = New-Release $version
$bad.assets[0].id = '42'
Reject 'String asset IDs are not canonical typed identity' { Select-SourceToolRelease @($bad) preview }
$bad.assets[0].id = 42
$bad.assets[0].size = "$($bad.assets[0].size)"
Reject 'String asset sizes are not valid final-byte identity' { Select-SourceToolRelease @($bad) preview }

$script:tagRevision = $revision
$script:remoteRelease = $preview
$script:readRelease = $null
$script:remoteTree = $tree
$script:treeTruncated = $false
$script:commitRevision = $revision
$script:ghCalls = [Collections.Generic.List[string]]::new()
function Get-CanonicalReleases { @($script:remoteRelease) }
function Get-GitHubRecord {
    param([string] $Path)
    $script:ghCalls.Add($Path)
    if ($Path -like '*/git/ref/tags/*') { return @{ object = @{ type = 'commit'; sha = $script:tagRevision } } }
    if ($Path -like '*/releases/17') {
        if ($null -ne $script:readRelease) { return $script:readRelease }
        return $script:remoteRelease
    }
    throw "Unexpected static API lookup: $Path"
}
function Invoke-Gh {
    param([string[]] $Arguments)
    $script:ghCalls.Add(($Arguments -join ' '))
    if ($Arguments[1] -like '*/git/commits/*') {
        return @{ sha = $script:commitRevision; tree = @{ sha = 'a' * 40 } } | ConvertTo-Json
    }
    if ($Arguments[1] -like '*/git/trees/*') {
        return @{ sha = 'a' * 40; truncated = $script:treeTruncated; tree = $script:remoteTree } | ConvertTo-Json -Depth 6
    }
    throw "Unexpected static API operation: $Arguments"
}
$resolution = Get-SourceToolResolution preview $revision
Check ($resolution.revision -ceq $revision -and $resolution.assetId -eq 42 -and $resolution.activation -ceq 'unavailable') `
    'Preview binds release/tag/asset ID, digest, canonical commit and complete tree without executing code'
$script:readRelease = $preview | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$script:readRelease.assets[0].id = 43
Reject 'Asset replacement between list and record lookup refused' { Get-SourceToolResolution preview $revision }
$script:readRelease = $preview | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$script:readRelease.assets[0] | Add-Member -NotePropertyName download_count -NotePropertyValue 123
Get-SourceToolResolution preview $revision | Out-Null
Check $true 'Unrelated download counters cannot invalidate immutable release identity'
$script:readRelease = $null
$script:tagRevision = 'b' * 40
Reject 'Moved tag refused even with matching source marker' { Get-SourceToolResolution preview $revision }
$script:tagRevision = $revision
$script:treeTruncated = $true
Reject 'Truncated canonical source tree refused' { Get-SourceToolResolution preview $revision }
$script:treeTruncated = $false
$script:commitRevision = 'b' * 40
Reject 'Canonical API commit substitution refused' { Get-SourceToolResolution preview $revision }
$script:commitRevision = $revision
$script:remoteTree = @($tree | Select-Object -Skip 1)
Reject 'Canonical API tool omission refused' { Get-SourceToolResolution preview $revision }
$script:remoteTree = $tree
Check (@($script:ghCalls | Where-Object { $_ -like '*release create*' -or $_ -like '*PATCH*' }).Count -eq 0) `
    'Resolver has no release/tag write authority'

$script:downloadBytes = [IO.File]::ReadAllBytes($archivePath)
$script:downloads = 0
function Read-SourceCommandBytes {
    param([string] $Command, [string[]] $Arguments)
    if ($Command -cne 'gh' -or $Arguments[1] -cne "repos/roryprimrose/Kora/releases/assets/42") {
        throw 'Acquisition must download by resolved immutable asset ID, never a tag URL.'
    }
    $script:downloads++
    return ,$script:downloadBytes
}
$acquired = Join-Path $OutputDirectory 'acquired'
$result = Invoke-SourceToolAcquisition $resolution $acquired
Check ($result.verified -and !$result.codeExecuted -and $script:downloads -eq 1) 'Acquire verifies and safely extracts; no downloaded code runs'
$receiptHash = (Get-FileHash (Join-Path $acquired 'acquisition.json')).Hash
Invoke-SourceToolAcquisition $resolution $acquired | Out-Null
Check ($script:downloads -eq 1 -and (Get-FileHash (Join-Path $acquired 'acquisition.json')).Hash -ceq $receiptHash) `
    'Verified rerun neither redownloads nor overwrites original receipts'
$toolFile = Join-Path $acquired "tools\$($manifest.files[0].path)"
$toolBytes = [IO.File]::ReadAllBytes($toolFile)
[IO.File]::WriteAllText($toolFile, 'changed')
Reject 'Changed extracted tool refused on rerun' { Invoke-SourceToolAcquisition $resolution $acquired }
[IO.File]::WriteAllBytes($toolFile, $toolBytes)
$manifestPath = Join-Path $acquired 'tools\source-tools.json'
$manifestBytes = [IO.File]::ReadAllBytes($manifestPath)
[IO.File]::WriteAllText($manifestPath, '{}')
Reject 'Changed extracted manifest refused on rerun' { Invoke-SourceToolAcquisition $resolution $acquired }
[IO.File]::WriteAllBytes($manifestPath, $manifestBytes)
[IO.File]::WriteAllText($manifestPath, [Text.Encoding]::UTF8.GetString($manifestBytes) + ' ')
Reject 'Even semantically equivalent manifest byte changes fail rerun identity' { Invoke-SourceToolAcquisition $resolution $acquired }
[IO.File]::WriteAllBytes($manifestPath, $manifestBytes)
$extra = Join-Path $acquired 'extra.ps1'
[IO.File]::WriteAllText($extra, 'unlisted')
Reject 'Extra acquisition-root executable is refused on rerun' { Invoke-SourceToolAcquisition $resolution $acquired }
Remove-Item -LiteralPath $extra
$different = $resolution | ConvertTo-Json -Depth 10 | ConvertFrom-Json
$different.assetId = 43
Reject 'Changed asset identity cannot overwrite an existing acquisition' { Invoke-SourceToolAcquisition $different $acquired }
$script:downloadBytes = [Text.Encoding]::UTF8.GetBytes('hostile download')
$failed = Join-Path $OutputDirectory 'failed'
Reject 'Changed downloaded final bytes refused before extraction' { Invoke-SourceToolAcquisition $resolution $failed }
Check (!(Test-Path -LiteralPath (Join-Path $failed 'tools'))) 'Failed download never extracts tools'
Reject 'Incomplete output is retained, not adopted or overwritten' { Invoke-SourceToolAcquisition $resolution $failed }
$script:downloadBytes = [IO.File]::ReadAllBytes($archivePath)
$unowned = Join-Path $OutputDirectory 'unowned'
New-ProofDirectory $unowned
Reject 'Unowned existing output refused' { Invoke-SourceToolAcquisition $resolution $unowned }
$extracted = Join-Path $OutputDirectory 'extracted'
Test-SourceToolArchive $archivePath $revision $version $tree -ExtractTo $extracted | Out-Null
Reject 'Extraction output is never overwritten' { Test-SourceToolArchive $archivePath $revision $version $tree -ExtractTo $extracted }
if ($IsWindows) {
    $link = Join-Path $OutputDirectory 'linked'
    New-Item -ItemType Junction -Path $link -Target $acquired | Out-Null
    try { Reject 'Linked acquisition output refused' { Invoke-SourceToolAcquisition $resolution $link } }
    finally { Remove-Item -LiteralPath $link }
}
. (Join-Path $PSScriptRoot 'SourceBootstrap.Common.ps1')
$bundle = Join-Path $OutputDirectory 'bundle-guard'
New-ProofDirectory $bundle
New-Item -ItemType Directory -Path (Join-Path $bundle 'eng') | Out-Null
foreach ($relative in Get-SourceToolPaths) {
    Copy-Item -LiteralPath (Join-Path $repositoryPath $relative) -Destination (Join-Path $bundle $relative)
}
Reject 'Missing distributed manifest cannot silently become an unrestricted developer bootstrap' {
    Assert-DistributedSourceTools $revision $bundle
}
$bundleManifest = [ordered]@{
    schema = 1; bootstrapVersion = '1.1.0'; repository = $script:KoraSourceRepository; revision = $revision
    unsigned = $true; productionAccepted = $false; activation = 'unavailable'
    files = @((Get-SourceToolFiles $bundle) | ForEach-Object {
        @{ path = $_.path.Replace('\', '/'); sha256 = $_.sha256; bytes = (Get-Item (Join-Path $bundle $_.path)).Length }
    })
}
Write-ProofJson $bundleManifest (Join-Path $bundle 'source-tools.json')
Assert-DistributedSourceTools $revision $bundle
Check $true 'Reviewed complete bundle passes local pre-build guard without executing its scripts'
Reject 'Acquired tools cannot build a different source revision' { Assert-DistributedSourceTools ('0' * 40) $bundle }
$bundleManifest.files[0].sha256 = '0' * 64
Write-ProofJson $bundleManifest (Join-Path $bundle 'source-tools.json')
Reject 'Changed acquired tool digests refuse build' { Assert-DistributedSourceTools $revision $bundle }
$bundleManifest.files = @($bundleManifest.files | Select-Object -Skip 1)
Write-ProofJson $bundleManifest (Join-Path $bundle 'source-tools.json')
Reject 'Incomplete distributed manifest refuses build' { Assert-DistributedSourceTools $revision $bundle }
Write-ProofJson ([ordered]@{ schema = 1; revision = $revision; count = $passed.Count
    staticOnly = $true; tests = @($passed) }) (Join-Path $OutputDirectory 'tests.json')
Write-Host "$($passed.Count) immutable source-tool contracts passed; no downloaded code, installer or application executed."
