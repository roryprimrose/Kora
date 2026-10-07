Set-StrictMode -Version Latest

function New-ReleaseFixtureState {
    return @{
        ReleaseStatus = 404; TagStatus = 404; ListStatus = 200; ReadStatus = 200
        DownloadStatus = 200; ContentStatus = 200
        Releases = @(); Tag = $null; Content = @{}; Calls = @(); Writes = @()
        Fail = ''; FailAfter = $false; DownloadCorrupt = $false; DriftOnTag = $false
        Pages = @{}; UseNativeExit = $false; Annotated = @{}; HardStop = ''; ResultPath = ''
        HideCreatedDrafts = $false; CreateResponse = ''; DirectReadChange = ''; ReadTimeout = $false
        CompetingAfterCreate = $false
        DriftBodyOnUpload = $false
    }
}

function gh {
    $arguments = @($args)
    $state = $global:KoraReleaseTestState
    $state.Calls += ,$arguments
    $global:LASTEXITCODE = 0
    $path = if ($arguments[0] -eq 'api') {
        if ($arguments[1] -eq '--include') { $arguments[2] }
        elseif ($arguments[1] -eq '--method') { $arguments[3] }
        else { $arguments[1] }
    } else { '' }
    $operation = ''
    if ($arguments -contains 'POST' -and $path -match '/releases$') { $operation = 'create' }
    elseif ($arguments -contains 'POST' -and $path -match '^https://uploads\.github\.com/.+/releases/\d+/assets\?name=') { $operation = 'upload' }
    elseif ($path -like '*/git/refs') { $operation = 'tag' }
    elseif ($arguments -contains 'PATCH') {
        $operation = if (@($arguments | Where-Object { $_ -like 'body=*' }).Count -gt 0) { 'notes' } else { 'publish' }
    }
    if ($operation) {
        $state.Writes += $operation
        if ($state.Fail -eq $operation -and -not $state.FailAfter) {
            $global:LASTEXITCODE = 1
            "Synthetic API failure before $operation"
            return
        }
    }
    if ($arguments -contains '--include') {
        if ($state.ReadTimeout -and $path -match '/releases/\d+$') {
            $global:LASTEXITCODE = 1
            return 'Synthetic direct-ID API timeout'
        }
        $record = $null
        if ($path -like '*/releases/tags/*') {
            $record = @($state.Releases | Where-Object { -not $_.draft -and $_.tag_name -eq $path.Split('/')[-1] })
            $status = if ($state.ReleaseStatus -notin @(200, 404)) { $state.ReleaseStatus }
                elseif ($record.Count -gt 0) { 200 } else { 404 }
            if ($record.Count -gt 0) { $record = $record[0] } else { $record = $null }
        } elseif ($path -like '*/git/ref/tags/*') {
            $record = $state.Tag
            $status = if ($state.TagStatus -notin @(200, 404)) { $state.TagStatus }
                elseif ($null -ne $record) { 200 } else { 404 }
        } elseif ($path -match '/releases/(\d+)$') {
            $id = $Matches[1]
            $record = @($state.Releases | Where-Object id -eq $id)
            $status = if ($state.ReadStatus -ne 200) { $state.ReadStatus }
                elseif ($record.Count -eq 1) { 200 } else { 404 }
            if ($record.Count -eq 1) { $record = $record[0] } else { $record = $null }
            if ($null -ne $record -and $state.DirectReadChange) {
                $record = $record | ConvertTo-Json -Depth 12 | ConvertFrom-Json
                switch ($state.DirectReadChange) {
                    'id' { $record.id = 999 }
                    'id-type' { $record.id = '123' }
                    'source' { $record.target_commitish = 'b' * 40 }
                    'body' { $record.body += "`nchanged body" }
                    'marker' { $record.body = "<!-- kora-source: $('b' * 40) -->" }
                    'channel' { $record.prerelease = -not $record.prerelease }
                    'published' { $record.draft = $false }
                }
            }
        } else { throw "Unexpected included lookup: $path" }
        if ($state.UseNativeExit) {
            & (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source `
                -NoProfile -NonInteractive -Command "exit $(if ($status -eq 200) { 0 } else { 1 })"
        }
        $global:LASTEXITCODE = if ($status -eq 200) { 0 } else { 1 }
        "HTTP/2.0 $status Test`ncontent-type: application/json`n`n$(if ($status -eq 200) { $record | ConvertTo-Json -Depth 12 } else { '{}' })"
        return
    }
    if ($path -match '/releases\?per_page=100&page=(\d+)$') {
        if ($state.ListStatus -ne 200) { $global:LASTEXITCODE = 1; return }
        $page = $Matches[1]
        $records = if ($state.Pages.ContainsKey($page)) { $state.Pages[$page] }
            elseif ($page -eq '1') { $state.Releases } else { @() }
        if ($state.HideCreatedDrafts) { $records = @($records | Where-Object { -not $_.draft }) }
        return ConvertTo-Json -InputObject @($records) -Depth 12
    }
    if ($path -like '*/releases/generate-notes') {
        return '{"body":"## Changes\n* Customer-visible fixture improvement (#1)"}'
    }
    if ($path -like '*/git/tags/*') {
        return $state.Annotated[$path.Split('/')[-1]] | ConvertTo-Json -Depth 4
    }
    if ($path -like '*/git/commits/*') {
        return @{ sha = $path.Split('/')[-1]; tree = @{ sha = 'd' * 40 } } | ConvertTo-Json
    }
    if ($path -like '*/git/trees/*') {
        $source = $state.Releases[0].target_commitish
        $tree = @(Get-LocalSourceToolTree (Join-Path $PSScriptRoot '..\..') $source)
        return @{ sha = 'd' * 40; truncated = $false; tree = $tree } | ConvertTo-Json -Depth 6
    }
    if ($path -like '*/releases/assets/*') {
        if ($state.ContentStatus -ne 200) { $global:LASTEXITCODE = 1; return "Synthetic asset API failure $($state.ContentStatus)" }
        return [Text.Encoding]::UTF8.GetString([byte[]] $state.Content[$path.Split('/')[-1]])
    }
    if ($arguments[0] -eq 'release' -and $arguments[1] -eq 'download') {
        if ($state.DownloadStatus -ne 200) { $global:LASTEXITCODE = 1; return "Synthetic download API failure $($state.DownloadStatus)" }
        $name = $arguments[$arguments.IndexOf('--pattern') + 1]
        $directory = $arguments[$arguments.IndexOf('--dir') + 1]
        $asset = @($state.Releases[0].assets | Where-Object name -CEQ $name)[0]
        $bytes = [byte[]] $state.Content[[string] $asset.id]
        if ($state.DownloadCorrupt) { $bytes = [Text.Encoding]::UTF8.GetBytes('corrupt download') }
        [IO.File]::WriteAllBytes((Join-Path $directory $name), $bytes)
        return
    }
    $response = $null
    switch ($operation) {
        'create' {
            $notes = @($arguments | Where-Object { $_ -like 'body=*' })[0].Substring(5)
            if ($notes -notlike '*Unsigned proof-of-concept*' -or $notes -notlike '*Customer-visible fixture*' -or
                $notes -notlike '*not upgrade-ordered*' -or $notes -notlike '*Silent related-bundle upgrades are unsupported*' -or
                $notes -notlike '*private-profile standard SQLite*' -or $notes -notlike '*optional future R30*' -or
                $notes -like '*encrypted-storage/native admission*') {
                throw 'Release notes lack current storage/signing/upgrade disclosure or generated changes.'
            }
            $state.Releases += [pscustomobject] @{
                id = 123; tag_name = @($arguments | Where-Object { $_ -like 'tag_name=*' })[0].Substring(9)
                target_commitish = @($arguments | Where-Object { $_ -like 'target_commitish=*' })[0].Substring(17)
                body = $notes; prerelease = $arguments -contains 'prerelease=true'; draft = $true; assets = @()
            }
            # GitHub drafts have a pending tag name, not a Git ref.
            $created = $state.Releases[-1] | ConvertTo-Json -Depth 12 | ConvertFrom-Json
            if ($state.CompetingAfterCreate) {
                $competing = $created | ConvertTo-Json -Depth 12 | ConvertFrom-Json
                $competing.id = 124
                $state.Releases += $competing
            }
            switch ($state.CreateResponse) {
                'missing-id' { $created.PSObject.Properties.Remove('id') }
                'wrong-id' { $created.id = 999 }
                'id-type' { $created.id = '123' }
                'source' { $created.target_commitish = 'b' * 40 }
                'body' { $created.body += "`nchanged body" }
                'channel' { $created.prerelease = -not $created.prerelease }
                'published' { $created.draft = $false }
            }
            $response = if ($state.CreateResponse -eq 'malformed') { '{broken' } else { $created | ConvertTo-Json -Depth 12 }
        }
        'upload' {
            $idMatch = [regex]::Match($path, '/releases/(\d+)/assets\?')
            if (-not $idMatch.Success -or $state.Releases[0].id -ne [long]$idMatch.Groups[1].Value) {
                throw 'Fixture forbids tag-selected or changed-ID uploads.'
            }
            $release = $state.Releases[0]
            foreach ($file in @($arguments[$arguments.IndexOf('--input') + 1])) {
                $name = [IO.Path]::GetFileName($file)
                if ($name -cin @($release.assets | ForEach-Object name)) { throw 'Fixture forbids overwriting assets.' }
                $id = [string] ($release.assets.Count + 1)
                $release.assets += [pscustomobject] @{
                    id = $id; name = $name; state = 'uploaded'; size = (Get-Item -LiteralPath $file).Length
                    digest = "sha256:$((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash)"
                }
                $state.Content[$id] = [IO.File]::ReadAllBytes($file)
            }
            if ($state.DriftBodyOnUpload) { $release.body += "`nchanged after upload" }
        }
        'tag' {
            if ($null -ne $state.Tag) { throw 'Fixture forbids moving existing tags.' }
            $sha = @($arguments | Where-Object { $_ -like 'sha=*' })[0].Substring(4)
            $state.Tag = @{ object = @{ type = 'commit'; sha = $sha } }
            if ($state.DriftOnTag) { $state.Releases[0].target_commitish = 'b' * 40 }
        }
        'publish' {
            $release = @($state.Releases | Where-Object id -eq $path.Split('/')[-1])[0]
            if ($null -eq $state.Tag -or $state.Tag.object.sha -ne $release.target_commitish -or
                $release.assets.Count -ne 9 -or $arguments -notcontains "target_commitish=$($release.target_commitish)") {
                throw 'Fixture forbids unverified publication.'
            }
            $release.draft = $false
        }
        'notes' {
            $release = @($state.Releases | Where-Object id -eq $path.Split('/')[-1])[0]
            if (-not $release.draft) { throw 'Fixture forbids editing published notes.' }
            $release.body = @($arguments | Where-Object { $_ -like 'body=*' })[0].Substring(5)
        }
        default { throw "Unexpected fake GitHub operation: $($arguments -join ' ')" }
    }
    if ($state.HardStop -eq $operation) {
        $state | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $state.ResultPath
        [Environment]::Exit(137)
    }
    if ($state.Fail -eq $operation -and $state.FailAfter) {
        $global:LASTEXITCODE = 1
        "Synthetic API failure after $operation"
    }
    if ($null -ne $response) { $response }
}
