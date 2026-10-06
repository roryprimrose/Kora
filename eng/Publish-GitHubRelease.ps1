[CmdletBinding()]
param(
    [ValidateSet('Check', 'Publish')]
    [string] $Action = 'Check',
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-beta\d+)?$')]
    [string] $Version,
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-f0-9]{40}$')]
    [string] $SourceRevision,
    [string] $ArtifactPath
)

. (Join-Path $PSScriptRoot 'SourceTools.Common.ps1')
$repository = $script:KoraRepository
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:GITHUB_REPOSITORY -ne $repository -or
    $env:GITHUB_EVENT_NAME -notin @('push', 'workflow_dispatch')) {
    throw 'Release operations require the canonical repository main/tag workflow, never a local, fork or PR run.'
}
if ($env:GITHUB_REF -ne 'refs/heads/main' -and $env:GITHUB_REF -ne "refs/tags/v$Version") {
    throw 'Release operations require main or the exact stable release tag.'
}
if ($env:GITHUB_SHA -ne $SourceRevision) { throw 'Release source revision does not match the workflow.' }
$tag = "v$Version"
$prerelease = $Version -like '*-beta*'
$names = @("Kora-$Version-win-x64.zip", "Kora-$Version-win-x86.zip",
    "Kora-$Version-win-x64.msi", "Kora-Setup-$Version-win-x64.exe",
    'installer-build.json', 'payload-manifest.json', 'SHA256SUMS.txt', 'release-manifest.json',
    "Kora-$Version-source-tools.zip")

function Get-ReleaseState {
    # The tag endpoint excludes drafts. List all pages as well, and reject ambiguous pending tags.
    $published = Get-GitHubRecord "repos/$repository/releases/tags/$tag"
    $matches = @(Get-CanonicalReleases | Where-Object tag_name -eq $tag)
    if ($null -ne $published -and $published.id -notin @($matches | ForEach-Object id)) { $matches += $published }
    if ($matches.Count -gt 1) { throw 'Multiple releases claim this version; manual reconciliation is required.' }
    if ($matches.Count -eq 0) { return $null }
    $release = Get-GitHubRecord "repos/$repository/releases/$($matches[0].id)"
    if ($null -eq $release) { throw 'Release disappeared during lookup; retry from a fresh state.' }
    return $release
}

function Assert-ReleaseIdentity {
    param($Release)
    Assert-CanonicalReleaseIdentity $Release $Version $SourceRevision
    $seen = @()
    foreach ($asset in $Release.assets) {
        if ($asset.name -cnotin $names -or $asset.name -cin $seen -or $asset.state -ne 'uploaded' -or
            $asset.size -le 0 -or $asset.digest -cnotmatch '^sha256:[A-Fa-f0-9]{64}$' -or
            ($asset.name -ceq "Kora-$Version-source-tools.zip" -and $asset.size -gt 16777216)) {
            throw 'Release has unknown, duplicate or incomplete assets; manual reconciliation is required.'
        }
        $seen += $asset.name
    }
}

function Assert-ReleaseManifest {
    param($Manifest, $Digests)
    $expected = @($names | Where-Object { $_ -notin @('release-manifest.json', 'SHA256SUMS.txt') } | Sort-Object)
    if ($Manifest.version -cne $Version -or $Manifest.sourceRevision -cne $SourceRevision -or
        $Manifest.unsigned -isnot [bool] -or $Manifest.unsigned -ne $true -or
        $Manifest.productionAccepted -isnot [bool] -or $Manifest.productionAccepted -ne $false -or
        $Manifest.workflowRun -cnotmatch '^https://github\.com/roryprimrose/Kora/actions/runs/[1-9]\d*$' -or
        (@($Manifest.assets.name | Sort-Object) -join '|') -cne ($expected -join '|')) {
        throw 'Release provenance does not describe this exact POC candidate.'
    }
    foreach ($record in $Manifest.assets) {
        $actual = @($Digests | Where-Object name -CEQ $record.name)
        if ($actual.Count -ne 1 -or $record.sha256 -cnotmatch '^[A-Fa-f0-9]{64}$' -or
            $record.sha256 -ine $actual[0].sha256) {
            throw "Release provenance differs from final bytes: $($record.name)"
        }
    }
}

function Assert-PublishedRelease {
    param($Release, [switch] $AllowDraft)
    if ($null -eq $Release) { throw 'Release is missing.' }
    Assert-ReleaseIdentity $Release
    if ($Release.draft -and -not $AllowDraft) {
        throw 'Release remains a draft; publication was not established.'
    }
    $reference = Get-GitHubRecord "repos/$repository/git/ref/tags/$tag"
    if ($null -eq $reference) {
        if (-not $AllowDraft) { throw 'Published release has no tag.' }
    } else { Assert-TagSource $reference $SourceRevision }
    $actualNames = @($Release.assets.name | Sort-Object)
    if (($actualNames -join '|') -ne (($names | Sort-Object) -join '|')) {
        throw 'Published release has missing or unexpected assets.'
    }
    $asset = @($Release.assets | Where-Object name -eq 'release-manifest.json')[0]
    $manifestText = Invoke-Gh -Arguments @('api', "repos/$repository/releases/assets/$($asset.id)",
        '-H', 'Accept: application/octet-stream')
    $manifest = ($manifestText -join "`n") | ConvertFrom-Json
    $digests = @($Release.assets | ForEach-Object { @{ name = $_.name; sha256 = $_.digest.Substring(7) } })
    Assert-ReleaseManifest $manifest $digests
    $checksumAsset = @($Release.assets | Where-Object name -eq 'SHA256SUMS.txt')[0]
    $checksums = (Invoke-Gh -Arguments @('api', "repos/$repository/releases/assets/$($checksumAsset.id)",
        '-H', 'Accept: application/octet-stream')) -join "`n"
    $lines = @($checksums.TrimEnd() -split '\r?\n')
    if ($lines.Count -ne $names.Count - 1) { throw 'Published checksum file has an unexpected artifact count.' }
    $checked = @()
    foreach ($line in $lines) {
        if ($line -cnotmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw 'Malformed published checksum.' }
        $digest = $Matches[1]
        $name = $Matches[2]
        if ($name -notin $names -or $name -eq 'SHA256SUMS.txt' -or $name -in $checked) {
            throw 'Unknown or duplicate published checksum entry.'
        }
        $published = @($Release.assets | Where-Object name -eq $name)[0]
        if ($published.digest -ine "sha256:$digest") { throw "Published checksum differs from GitHub digest: $name" }
        $checked += $name
    }
    $toolName = "Kora-$Version-source-tools.zip"
    if (-not $AllowDraft -and $toolName -cin $names) {
        $verification = Join-Path ([IO.Path]::GetTempPath()) "KoraSourceRelease-$([guid]::NewGuid().ToString('N'))"
        New-ProofDirectory $verification
        try {
            Save-DraftAsset $Release $toolName $verification | Out-Null
            $tree = @(Get-CanonicalSourceToolTree $SourceRevision)
            Test-SourceToolArchive (Join-Path $verification $toolName) $SourceRevision $Version $tree | Out-Null
            $reference = Get-GitHubRecord "repos/$repository/git/ref/tags/$tag"
            if ($null -eq $reference) { throw 'Published release tag disappeared.' }
            Assert-TagSource $reference $SourceRevision
        } finally { Remove-Item -LiteralPath $verification -Recurse -Force }
    }
}

function Save-DraftAsset {
    param($Release, [string] $Name, [string] $Directory)
    $asset = @($Release.assets | Where-Object name -CEQ $Name)
    if ($asset.Count -eq 0) { return $false }
    Invoke-Gh -Arguments @('release', 'download', $tag, '--repo', $repository,
        '--pattern', $Name, '--dir', $Directory) | Out-Null
    $file = Get-Item -LiteralPath (Join-Path $Directory $Name)
    if ($file.Length -ne $asset[0].size -or
        "sha256:$((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)" -ine $asset[0].digest) {
        throw "Downloaded draft asset differs from its GitHub digest: $Name"
    }
    return $true
}

function Assert-ZipPayload {
    param([string] $Zip, [string] $Payload)
    $files = @(Get-ChildItem -LiteralPath $Payload -Recurse -File -Force)
    $expected = @($files | ForEach-Object { [IO.Path]::GetRelativePath($Payload, $_.FullName).Replace('\', '/') })
    $archive = [IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        $seen = @()
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName.EndsWith('/')) {
                $directoryNames = @(Get-ChildItem -LiteralPath $Payload -Recurse -Directory -Force |
                    ForEach-Object { [IO.Path]::GetRelativePath($Payload, $_.FullName).Replace('\', '/') + '/' })
                if ($entry.FullName -cnotin $directoryNames -or $entry.Length -ne 0) { throw 'Unexpected ZIP directory.' }
                continue
            }
            if ($entry.FullName -cnotin $expected -or $entry.FullName -cin $seen) { throw 'Unexpected or duplicate ZIP file.' }
            $index = [Array]::IndexOf($expected, $entry.FullName)
            $stream = $entry.Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            if ($entry.Length -ne $files[$index].Length -or
                $hash -cne (Get-FileHash -LiteralPath $files[$index].FullName -Algorithm SHA256).Hash) {
                throw "Draft ZIP differs from the exact downloaded payload: $($entry.FullName)"
            }
            $seen += $entry.FullName
        }
        if ($seen.Count -ne $expected.Count) { throw 'Draft ZIP omits payload files.' }
    } finally { $archive.Dispose() }
}

function New-PayloadZip {
    param([string] $Zip, [string] $Payload)
    $archive = [IO.Compression.ZipFile]::Open($Zip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in @(Get-ChildItem -LiteralPath $Payload -Recurse -File -Force | Sort-Object FullName)) {
            $entry = $archive.CreateEntry([IO.Path]::GetRelativePath($Payload, $file.FullName).Replace('\', '/'))
            # Artifact downloads do not preserve filesystem timestamps across runner retries.
            $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $inputStream = $file.OpenRead()
            try {
                $outputStream = $entry.Open()
                try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose() }
            } finally { $inputStream.Dispose() }
        }
    } finally { $archive.Dispose() }
}

function Assert-StagedAssets {
    param($Release, [string] $Directory)
    Assert-ReleaseIdentity $Release
    foreach ($asset in $Release.assets) {
        $file = Get-Item -LiteralPath (Join-Path $Directory $asset.name)
        if ($file.Length -ne $asset.size -or
            "sha256:$((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)" -ine $asset.digest) {
            throw "Draft asset conflicts with exact final bytes: $($asset.name). No replacement is permitted."
        }
    }
}

$existing = Get-ReleaseState
if ($null -ne $existing) {
    # Old published eight-asset releases are immutable historical no-ops, not source-tool releases.
    if (-not $existing.draft -and "Kora-$Version-source-tools.zip" -cnotin @($existing.assets.name)) {
        $names = @($names | Where-Object { $_ -cne "Kora-$Version-source-tools.zip" })
    }
    Assert-ReleaseIdentity $existing
    if (-not $existing.draft) {
        Assert-PublishedRelease $existing
        Write-Host "Already published: $tag. No rebuild, asset replacement or tag movement."
        return [pscustomobject] @{ AlreadyPublished = $true }
    }
}
$reference = Get-GitHubRecord "repos/$repository/git/ref/tags/$tag"
if ($null -ne $reference) { Assert-TagSource $reference $SourceRevision }
if ($Action -eq 'Check') { return [pscustomobject] @{ AlreadyPublished = $false } }
if (-not $ArtifactPath) { throw 'Publication requires the exact downloaded build artifacts.' }
$ArtifactPath = [IO.Path]::GetFullPath($ArtifactPath)
$installer = Join-Path $ArtifactPath 'installer'
$receipt = Get-Content -LiteralPath (Join-Path $installer 'installer-build.json') -Raw | ConvertFrom-Json
if ($receipt.version -ne $Version -or $receipt.sourceRevision -ne $SourceRevision -or
    $receipt.productVersion -cne ($Version -split '-')[0] -or
    $receipt.sourceDirty -or $receipt.buildOrigin -ne 'github-actions' -or
    $receipt.msiIceValidation -ne 'passed' -or $receipt.packageInspection -ne 'passed' -or -not $receipt.unsigned) {
    throw 'Installer receipt does not establish a clean, exact-version CI candidate with full ICE and package inspection.'
}
foreach ($record in $receipt.files) {
    if ([IO.Path]::GetFileName($record.name) -ne $record.name -or $record.sha256 -cnotmatch '^[A-Fa-f0-9]{64}$') {
        throw 'Invalid installer receipt file identity.'
    }
    if ((Get-FileHash -LiteralPath (Join-Path $installer $record.name) -Algorithm SHA256).Hash -ine $record.sha256) {
        throw "Downloaded installer differs from the inspected bytes: $($record.name)"
    }
}
$expectedInstallerNames = @("Kora-$($receipt.productVersion)-win-x64.msi", "Kora-Setup-$Version-win-x64.exe")
if ((@($receipt.files.name | Sort-Object) -join '|') -ne (($expectedInstallerNames | Sort-Object) -join '|')) {
    throw 'Installer receipt has an unexpected payload set.'
}
& (Join-Path $PSScriptRoot 'Test-InstallerPayload.ps1') -PayloadPath (Join-Path $ArtifactPath 'Kora-win-x64') `
    -Version $Version -SourceRevision $SourceRevision
& (Join-Path $PSScriptRoot 'Test-InstallerPayload.ps1') -PayloadPath (Join-Path $ArtifactPath 'Kora-win-x86') `
    -Version $Version -SourceRevision $SourceRevision
if ((Get-FileHash -LiteralPath (Join-Path (Join-Path $ArtifactPath 'Kora-win-x64') 'payload-manifest.json') -Algorithm SHA256).Hash -cne
    (Get-FileHash -LiteralPath (Join-Path $installer 'payload-manifest.json') -Algorithm SHA256).Hash) {
    throw 'Release application ZIP and MSI must use the exact same x64 payload manifest.'
}

$storageDisclosure = @'
- D-009 uses private-profile standard SQLite; encryption is optional future R30 work.
  Installed lifecycle/protection, ordinary packaged-native loading and durable recovery/deletion remain separate gates.
'@
$output = Join-Path $ArtifactPath "release-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $output | Out-Null
try {
    foreach ($rid in @('win-x64', 'win-x86')) {
        $name = "Kora-$Version-$rid.zip"
        $payload = Join-Path $ArtifactPath "Kora-$rid"
        if ($null -ne $existing -and (Save-DraftAsset $existing $name $output)) {
            Assert-ZipPayload (Join-Path $output $name) $payload
        } else { New-PayloadZip (Join-Path $output $name) $payload }
    }
    Copy-Item -LiteralPath (Join-Path $installer "Kora-$($receipt.productVersion)-win-x64.msi") `
        -Destination (Join-Path $output "Kora-$Version-win-x64.msi")
    foreach ($name in @("Kora-Setup-$Version-win-x64.exe", 'installer-build.json', 'payload-manifest.json')) {
        Copy-Item -LiteralPath (Join-Path $installer $name) -Destination $output
    }
    $toolName = "Kora-$Version-source-tools.zip"
    $toolPath = Join-Path $output $toolName
    $sourceTree = Get-LocalSourceToolTree -RepositoryPath (Join-Path $PSScriptRoot '..') -Revision $SourceRevision
    if ($null -eq $existing -or -not (Save-DraftAsset $existing $toolName $output)) {
        Copy-Item -LiteralPath (Join-Path $ArtifactPath "source-tools\$toolName") -Destination $toolPath
    }
    Test-SourceToolArchive -Path $toolPath -Revision $SourceRevision -Version $Version -Tree $sourceTree | Out-Null
    $digests = @(Get-ChildItem -LiteralPath $output -File | Sort-Object Name | ForEach-Object {
        [ordered] @{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    if ($null -eq $existing -or -not (Save-DraftAsset $existing 'release-manifest.json' $output)) {
        [ordered] @{
            version = $Version; sourceRevision = $SourceRevision; unsigned = $true; productionAccepted = $false
            workflowRun = "https://github.com/$repository/actions/runs/$env:GITHUB_RUN_ID"
            assets = $digests
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'release-manifest.json') -Encoding utf8NoBOM
    } else {
        $manifest = Get-Content -LiteralPath (Join-Path $output 'release-manifest.json') -Raw | ConvertFrom-Json
        Assert-ReleaseManifest $manifest $digests
    }
    $checksumRecords = $digests + @([ordered] @{ name = 'release-manifest.json'; sha256 =
        (Get-FileHash -LiteralPath (Join-Path $output 'release-manifest.json') -Algorithm SHA256).Hash })
    $checksumText = ($checksumRecords | ForEach-Object { "$($_.sha256)  $($_.name)" }) -join "`n"
    if ($null -ne $existing -and (Save-DraftAsset $existing 'SHA256SUMS.txt' $output)) {
        $savedChecksums = Get-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Raw
        if ($savedChecksums.TrimEnd().Replace("`r`n", "`n") -cne $checksumText) {
            throw 'Draft checksums differ from exact final bytes.'
        }
    } else {
        Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Value $checksumText -Encoding utf8NoBOM
    }
    if ($null -eq $existing) {
        $existing = Get-ReleaseState
        if ($null -ne $existing) { Assert-ReleaseIdentity $existing }
    }
    if ($null -eq $existing) {
        $generated = (Invoke-Gh -Arguments @('api', '--method', 'POST', "repos/$repository/releases/generate-notes",
            '-f', "tag_name=$tag", '-f', "target_commitish=$SourceRevision")) -join "`n" | ConvertFrom-Json
        $notes = @"
## Unsigned proof-of-concept build

This is a non-production Kora POC, not a verified-publisher or production-acceptance claim.
Windows may show Unknown Publisher or SmartScreen warnings. Verify downloads using
SHA256SUMS.txt and the exact-source release manifest; these are not independent signatures.

- Windows 11 x64: use Kora-Setup-$Version-win-x64.exe for dependency-aware setup.
- Application ZIPs are framework-dependent compiled binaries, not source bootstrap.
  x86 is a static publish candidate, not an accepted x86 installer/runtime commitment.
- Kora-$Version-source-tools.zip is the complete source-bootstrap v1.1.0 tool closure.
  Acquire/review with the maintained static resolver; building exact source requires explicit trust.
  No source activation, installation, elevation, registration or launch is available.
- MSI uses the numeric $($receipt.productVersion) version; beta builds sharing it are not upgrade-ordered.
- Silent related-bundle upgrades are unsupported. Use explicit external uninstall/reinstall
  retaining user data until that lifecycle path is implemented and validated.
$storageDisclosure
  Validation is front-loaded and risk-based; each release is not exhaustively manually installed.
- CI: portable/Windows tests, locked dependencies/licences, exact payload/native inspection and full MSI ICE.

Source: $SourceRevision
<!-- kora-source: $SourceRevision -->

$($generated.body)
"@
        $notesFile = Join-Path $output 'release-notes.txt'
        Set-Content -LiteralPath $notesFile -Value $notes -Encoding utf8NoBOM
        $create = @('release', 'create', $tag, '--repo', $repository, '--target', $SourceRevision,
            '--draft', '--title', "Kora $Version", '--notes-file', $notesFile)
        if ($prerelease) { $create += '--prerelease' }
        $competing = Get-ReleaseState
        if ($null -eq $competing) { Invoke-Gh -Arguments $create | Out-Null }
        else { Assert-ReleaseIdentity $competing }
    }
    # Never retry a failed write in-place: uncertain partial writes are reconciled on the next invocation.
    $draft = Get-ReleaseState
    if ($null -eq $draft) { throw 'Draft creation was not established.' }
    Assert-StagedAssets $draft $output
    if (-not $draft.draft) {
        Assert-PublishedRelease $draft
        Write-Host "Already published concurrently: $tag. No replacement."
        return [pscustomobject] @{ AlreadyPublished = $true }
    }
    $draftId = $draft.id
    $obsoleteDisclosure = '- Installed lifecycle/protection and encrypted-storage/native admission remain separate gates.'
    if ($draft.body.Contains($obsoleteDisclosure)) {
        # Only the known obsolete draft disclosure changes; source markers and generated notes remain intact.
        $body = $draft.body.Replace($obsoleteDisclosure, $storageDisclosure)
        Invoke-Gh -Arguments @('api', '--method', 'PATCH', "repos/$repository/releases/$draftId",
            '-f', "body=$body") | Out-Null
        $draft = Get-ReleaseState
        if ($null -eq $draft -or $draft.id -ne $draftId -or -not $draft.draft -or $draft.body -cne $body) {
            throw 'Draft disclosure update was not established; retry from a fresh state.'
        }
        Assert-StagedAssets $draft $output
    }
    foreach ($name in $names) {
        if ($name -cin @($draft.assets | ForEach-Object name)) { continue }
        Invoke-Gh -Arguments @('release', 'upload', $tag, '--repo', $repository, (Join-Path $output $name)) | Out-Null
    }
    $draft = Get-ReleaseState
    if ($null -eq $draft -or $draft.id -ne $draftId) { throw 'Draft identity changed during upload.' }
    Assert-StagedAssets $draft $output
    Assert-PublishedRelease $draft -AllowDraft
    if (-not $draft.draft) { return [pscustomobject] @{ AlreadyPublished = $true } }
    $reference = Get-GitHubRecord "repos/$repository/git/ref/tags/$tag"
    if ($null -eq $reference) {
        Invoke-Gh -Arguments @('api', '--method', 'POST', "repos/$repository/git/refs",
            '-f', "ref=refs/tags/$tag", '-f', "sha=$SourceRevision") | Out-Null
        $reference = Get-GitHubRecord "repos/$repository/git/ref/tags/$tag"
        if ($null -eq $reference) { throw 'Exact-source tag creation was not established; draft remains unpublished.' }
    }
    Assert-TagSource $reference $SourceRevision
    # Recheck both identities after the last write and before removing the draft boundary.
    $draft = Get-ReleaseState
    if ($null -eq $draft -or $draft.id -ne $draftId) { throw 'Draft identity changed before publication.' }
    Assert-StagedAssets $draft $output
    Assert-PublishedRelease $draft -AllowDraft
    if (-not $draft.draft) { return [pscustomobject] @{ AlreadyPublished = $true } }
    $latest = if ($prerelease) { 'false' } else { 'true' }
    Invoke-Gh -Arguments @('api', '--method', 'PATCH', "repos/$repository/releases/$draftId",
        '-F', 'draft=false', '-f', "target_commitish=$SourceRevision", '-f', "make_latest=$latest") | Out-Null
    Assert-PublishedRelease (Get-ReleaseState)
    Write-Host "Published unsigned POC release: $tag"
} finally {
    Remove-Item -LiteralPath $output -Recurse -Force
}
