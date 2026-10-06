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

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = 'roryprimrose/Kora'
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
    'installer-build.json', 'payload-manifest.json', 'SHA256SUMS.txt', 'release-manifest.json')

function Invoke-Gh {
    param([string[]] $Arguments)
    $output = & gh @Arguments
    if ($LASTEXITCODE -ne 0) { throw "GitHub operation failed: $($Arguments[0])" }
    return $output
}

function Get-GitHubRecord {
    param([string] $Path)
    $response = @(& gh api --include $Path 2>&1)
    $exit = $LASTEXITCODE
    $text = ($response | ForEach-Object { $_.ToString() }) -join "`n"
    $status = [regex]::Match($text, '(?m)^HTTP/\S+ (\d{3})')
    if ($status.Success -and $status.Groups[1].Value -eq '404' -and $exit -ne 0) { return $null }
    if ($exit -ne 0 -or -not $status.Success -or $status.Groups[1].Value -ne '200') {
        throw "Cannot establish GitHub publication state. $text"
    }
    $body = [regex]::Match($text, '(?s)\r?\n\r?\n(.*)$')
    if (-not $body.Success) { throw 'GitHub response has no JSON body.' }
    return $body.Groups[1].Value | ConvertFrom-Json
}

function Assert-TagSource {
    param($Reference)
    $target = $Reference.object
    $depth = 0
    while ($target.type -eq 'tag' -and $depth -lt 8) {
        $annotated = (Invoke-Gh -Arguments @('api', "repos/$repository/git/tags/$($target.sha)")) -join "`n" | ConvertFrom-Json
        $target = $annotated.object
        $depth++
    }
    if ($target.type -ne 'commit' -or $target.sha -ne $SourceRevision) {
        throw 'Published tag does not resolve to the exact source revision.'
    }
}

function Assert-PublishedRelease {
    param($Release)
    if ($Release.draft -or $Release.prerelease -ne $prerelease -or $Release.tag_name -ne $tag -or
        $Release.body -notlike "*<!-- kora-source: $SourceRevision -->*") {
        throw 'Existing release is a draft or conflicts with source/channel. Reconcile it manually; no promotion or overwrite is permitted.'
    }
    $reference = Get-GitHubRecord "repos/$repository/git/ref/tags/$tag"
    if ($null -eq $reference) { throw 'Published release has no tag.' }
    Assert-TagSource $reference
    $actualNames = @($Release.assets.name | Sort-Object)
    if (($actualNames -join '|') -ne (($names | Sort-Object) -join '|')) {
        throw 'Published release has missing or unexpected assets.'
    }
    $asset = @($Release.assets | Where-Object name -eq 'release-manifest.json')[0]
    $manifestText = Invoke-Gh -Arguments @('api', "repos/$repository/releases/assets/$($asset.id)",
        '-H', 'Accept: application/octet-stream')
    $manifest = ($manifestText -join "`n") | ConvertFrom-Json
    if ($manifest.version -ne $Version -or $manifest.sourceRevision -ne $SourceRevision -or
        $manifest.unsigned -ne $true -or $manifest.productionAccepted -ne $false -or
        (@($manifest.assets.name | Sort-Object) -join '|') -ne
        (($names | Where-Object { $_ -notin @('release-manifest.json', 'SHA256SUMS.txt') } | Sort-Object) -join '|')) {
        throw 'Published provenance does not describe this exact POC candidate.'
    }
    foreach ($record in $manifest.assets) {
        $published = @($Release.assets | Where-Object name -eq $record.name)[0]
        if ($record.sha256 -cnotmatch '^[A-Fa-f0-9]{64}$' -or
            $published.digest -ine "sha256:$($record.sha256)") {
            throw "Published asset digest conflicts with provenance: $($record.name)"
        }
    }
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
}

$existing = Get-GitHubRecord "repos/$repository/releases/tags/$tag"
if ($null -ne $existing) {
    Assert-PublishedRelease $existing
    Write-Host "Already published: $tag. No rebuild, asset replacement or tag movement."
    return [pscustomobject] @{ AlreadyPublished = $true }
}
$reference = Get-GitHubRecord "repos/$repository/git/ref/tags/$tag"
if ($null -ne $reference) { Assert-TagSource $reference }
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

$output = Join-Path $ArtifactPath "release-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $output | Out-Null
foreach ($rid in @('win-x64', 'win-x86')) {
    [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $ArtifactPath "Kora-$rid"),
        (Join-Path $output "Kora-$Version-$rid.zip"))
}
Copy-Item -LiteralPath (Join-Path $installer "Kora-$($receipt.productVersion)-win-x64.msi") `
    -Destination (Join-Path $output "Kora-$Version-win-x64.msi")
foreach ($name in @("Kora-Setup-$Version-win-x64.exe", 'installer-build.json', 'payload-manifest.json')) {
    Copy-Item -LiteralPath (Join-Path $installer $name) -Destination $output
}
$digests = @(Get-ChildItem -LiteralPath $output -File | Sort-Object Name | ForEach-Object {
    [ordered] @{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
[ordered] @{
    version = $Version; sourceRevision = $SourceRevision; unsigned = $true; productionAccepted = $false
    workflowRun = "https://github.com/$repository/actions/runs/$env:GITHUB_RUN_ID"
    assets = $digests
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'release-manifest.json') -Encoding utf8NoBOM
$checksumRecords = $digests + @([ordered] @{ name = 'release-manifest.json'; sha256 =
    (Get-FileHash -LiteralPath (Join-Path $output 'release-manifest.json') -Algorithm SHA256).Hash })
$checksumRecords | ForEach-Object { "$($_.sha256)  $($_.name)" } |
    Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding utf8NoBOM
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
- MSI uses the numeric $($receipt.productVersion) version; beta builds sharing it are not upgrade-ordered.
- Installed lifecycle/protection and encrypted-storage/native admission remain separate gates.
  Validation is front-loaded and risk-based; each release is not exhaustively manually installed.
- CI: portable/Windows tests, locked dependencies/licences, exact payload/native inspection and full MSI ICE.

Source: $SourceRevision
<!-- kora-source: $SourceRevision -->

$($generated.body)
"@
$notesFile = Join-Path $ArtifactPath "release-notes-$Version.txt"
Set-Content -LiteralPath $notesFile -Value $notes -Encoding utf8NoBOM
$create = @('release', 'create', $tag, '--repo', $repository, '--target', $SourceRevision,
    '--draft', '--title', "Kora $Version", '--notes-file', $notesFile)
if ($prerelease) { $create += '--prerelease' }
Invoke-Gh -Arguments $create | Out-Null
$assets = @($names | ForEach-Object { Join-Path $output $_ })
Invoke-Gh -Arguments (@('release', 'upload', $tag, '--repo', $repository) + $assets) | Out-Null
$reference = Get-GitHubRecord "repos/$repository/git/ref/tags/$tag"
if ($null -eq $reference) { throw 'New release tag is missing; the draft was not published.' }
Assert-TagSource $reference
$latest = if ($prerelease) { 'false' } else { 'true' }
Invoke-Gh -Arguments @('release', 'edit', $tag, '--repo', $repository, '--draft=false', "--latest=$latest") | Out-Null
Assert-PublishedRelease (Get-GitHubRecord "repos/$repository/releases/tags/$tag")
Write-Host "Published unsigned POC release: $tag"
