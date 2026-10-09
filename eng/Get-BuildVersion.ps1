[CmdletBinding()]
param(
    [string] $RepositoryPath = (Join-Path $PSScriptRoot '..'),
    [string] $OutputFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepositoryPath = [IO.Path]::GetFullPath($RepositoryPath)

function Invoke-Git {
    param([string[]] $Arguments)
    $output = & git -C $RepositoryPath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed." }
    return $output
}

$revision = Invoke-Git -Arguments @('rev-parse', 'HEAD')
$branch = Invoke-Git -Arguments @('branch', '--show-current')
$actions = $env:GITHUB_ACTIONS -eq 'true'
if ($actions -and $env:GITHUB_SHA -ne $revision) { throw 'Checkout revision differs from the workflow source revision.' }
$isPullRequest = $actions -and $env:GITHUB_EVENT_NAME -eq 'pull_request'
$isTag = $actions -and $env:GITHUB_REF -like 'refs/tags/*'
$main = -not $isPullRequest -and (($actions -and $env:GITHUB_REF -eq 'refs/heads/main') -or
    (-not $actions -and $branch -eq 'main'))
$checkoutTags = @(Invoke-Git -Arguments @('tag', '--points-at', 'HEAD'))
$stableTags = @($checkoutTags |
    Where-Object { $_ -cmatch '^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$' } |
    ForEach-Object { $_ -creplace '^v', '' } | Sort-Object -Unique)

if ($isTag) {
    if ($env:GITHUB_REF -cnotmatch '^refs/tags/v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        throw 'Release tag builds require v<major>.<minor>.<patch>; beta releases are produced by main builds.'
    }
    if ($checkoutTags -cnotcontains $env:GITHUB_REF.Substring('refs/tags/'.Length)) {
        throw 'The workflow release tag does not identify the checkout revision.'
    }
    & git -C $RepositoryPath merge-base --is-ancestor $revision refs/remotes/origin/main
    if ($LASTEXITCODE -ne 0) { throw 'A release tag must point to a revision on origin/main.' }
    $main = $true
}

$version = '0.1.0'
$productVersion = '0.1.0'
if ($main) {
    if ($stableTags.Count -gt 1) { throw 'Multiple different stable versions tag the same revision.' }
    Push-Location $RepositoryPath
    try {
        $json = & dotnet gitversion /nofetch /nonormalize /output json
        if ($LASTEXITCODE -ne 0) { throw "GitVersion failed. Restore the pinned local tools and use complete history/tags. $($json -join "`n")" }
    }
    finally { Pop-Location }
    $resolved = ($json -join "`n") | ConvertFrom-Json
    if ($resolved.Sha -ne $revision) { throw 'GitVersion calculated a different source revision.' }
    $productVersion = [string] $resolved.MajorMinorPatch
    if ($productVersion -cnotmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        throw 'GitVersion did not return a valid major.minor.patch.'
    }
    if ($stableTags.Count -eq 1) {
        if ($stableTags[0] -ne $productVersion) { throw "GitVersion $productVersion and stable tag $($stableTags[0]) disagree." }
        $version = $productVersion
    }
    else {
        if ($null -eq $resolved.PreReleaseNumber -or [string] $resolved.PreReleaseNumber -cnotmatch '^\d+$') {
            throw 'An untagged main build requires a GitVersion beta increment.'
        }
        $version = "$productVersion-beta$($resolved.PreReleaseNumber)"
    }
}

$result = [pscustomobject] @{
    Version = $version
    ProductVersion = $productVersion
    SourceRevision = $revision
    Prerelease = $version -like '*-beta*'
    Publish = $main -and $actions -and $env:GITHUB_EVENT_NAME -in @('push', 'workflow_dispatch') -and
        $env:GITHUB_REPOSITORY -eq 'roryprimrose/Kora'
}
if ($OutputFile) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputFile))) | Out-Null
    [IO.File]::WriteAllText($OutputFile, $version)
}
else { return $result }
