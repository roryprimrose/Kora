. (Join-Path $PSScriptRoot 'Distribution.Common.ps1')

function Invoke-Gh {
    param([string[]] $Arguments)
    $output = @(& gh @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0) { throw "GitHub operation failed: $($Arguments[0]). $($output -join "`n")" }
    return $output
}

function Get-GitHubRecord {
    param([string] $Path)
    $response = @(& gh api --include $Path 2>&1)
    $exit = $LASTEXITCODE
    $text = ($response | ForEach-Object { $_.ToString() }) -join "`n"
    $status = [regex]::Match($text, '(?m)^HTTP/\S+ (\d{3})')
    if ($status.Success -and $status.Groups[1].Value -eq '404' -and $exit -ne 0) {
        # Preserve the accepted-absence exit contract under the Actions pwsh epilogue.
        $global:LASTEXITCODE = 0
        return $null
    }
    if ($exit -ne 0 -or -not $status.Success -or $status.Groups[1].Value -ne '200') {
        throw "Cannot establish GitHub publication state. $text"
    }
    $body = [regex]::Match($text, '(?s)\r?\n\r?\n(.*)$')
    if (-not $body.Success) { throw 'GitHub response has no JSON body.' }
    return $body.Groups[1].Value | ConvertFrom-Json
}

function Assert-TagSource {
    param($Reference, [string] $SourceRevision, [string] $Repository = $script:KoraRepository)
    $target = $Reference.object
    $depth = 0
    while ($target.type -eq 'tag' -and $depth -lt 8) {
        $annotated = (Invoke-Gh -Arguments @('api', "repos/$Repository/git/tags/$($target.sha)")) -join "`n" | ConvertFrom-Json
        if ($annotated.sha -cne $target.sha) { throw 'Annotated tag identity changed.' }
        $target = $annotated.object
        $depth++
    }
    if ($target.type -ne 'commit' -or $target.sha -cne $SourceRevision) {
        throw 'Published tag does not resolve to the exact source revision.'
    }
}

function Get-CanonicalReleases {
    $records = @()
    for ($page = 1; ; $page++) {
        $batch = @(((Invoke-Gh -Arguments @('api', "repos/$script:KoraRepository/releases?per_page=100&page=$page")) -join "`n") |
            ConvertFrom-Json)
        $records += $batch
        if ($batch.Count -lt 100) { break }
    }
    return $records
}

function Assert-CanonicalReleaseIdentity {
    param($Release, [string] $Version, [string] $SourceRevision)
    $markers = [regex]::Matches($Release.body, '<!-- kora-source: ([a-f0-9]{40}) -->')
    if ($Release.draft -isnot [bool] -or $Release.prerelease -isnot [bool] -or
        $Release.prerelease -ne ($Version -like '*-beta*') -or $Release.tag_name -cne "v$Version" -or
        $Release.target_commitish -cne $SourceRevision -or $markers.Count -ne 1 -or
        $markers[0].Groups[1].Value -cne $SourceRevision) {
        throw 'Release conflicts with exact source/channel; no promotion or overwrite is permitted.'
    }
}
