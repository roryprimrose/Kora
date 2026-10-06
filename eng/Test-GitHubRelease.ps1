[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$fixture = Join-Path ([IO.Path]::GetTempPath()) "KoraReleaseTests-$([guid]::NewGuid().ToString('N'))"
$saved = @{}
foreach ($name in @('GITHUB_ACTIONS', 'GITHUB_EVENT_NAME', 'GITHUB_REF', 'GITHUB_REPOSITORY', 'GITHUB_SHA', 'GITHUB_RUN_ID')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
}
$source = 'a' * 40
$version = '0.1.0-beta4'
$global:KoraReleaseTestState = @{ Status = 404; Release = $null; Tag = $null; Content = @{}; Writes = 0; Calls = @() }

# Fake gh is inherited by the publication script; no network/token/release mutation is possible.
function gh {
    $arguments = @($args)
    $global:LASTEXITCODE = 0
    $state = $global:KoraReleaseTestState
    $state.Calls += ,$arguments
    if ($arguments[0] -eq 'api' -and $arguments -contains '--include') {
        $status = $state.Status
        $record = $state.Release
        if ($arguments[-1] -like '*/git/ref/tags/*') {
            $record = $state.Tag
            $status = if ($null -eq $record) { 404 } else { 200 }
        }
        if ($status -ne 200) { $global:LASTEXITCODE = 1 }
        "HTTP/2.0 $status Test`ncontent-type: application/json`n`n$(if ($status -eq 200) { $record | ConvertTo-Json -Depth 8 } else { '{}' })"
        return
    }
    if ($arguments[0] -eq 'api' -and ($arguments -join ' ') -like '*generate-notes*') {
        return '{"body":"## Changes\n* Customer-visible fixture improvement (#1)"}'
    }
    if ($arguments[0] -eq 'api' -and $arguments[1] -like '*/git/ref/tags/*') {
        return (@{ object = @{ type = 'commit'; sha = $source } } | ConvertTo-Json)
    }
    if ($arguments[0] -eq 'api' -and $arguments[1] -like '*/releases/assets/*') {
        return $state.Content[$arguments[1].Split('/')[-1]]
    }
    $state.Writes++
    if ($arguments[1] -eq 'create') {
        $notes = Get-Content -LiteralPath $arguments[($arguments.IndexOf('--notes-file') + 1)] -Raw
        if ($notes -notlike '*Unsigned proof-of-concept*' -or $notes -notlike '*Customer-visible fixture*' -or
            $notes -notlike '*not upgrade-ordered*' -or $notes -notlike '*Silent related-bundle upgrades are unsupported*') {
            throw 'Release notes lack signing/upgrade disclosure or generated changes.'
        }
        $state.Release = [pscustomobject] @{
            tag_name = $arguments[2]; target_commitish = $source; body = $notes
            prerelease = $arguments -contains '--prerelease'; draft = $true; assets = @()
        }
        $state.Status = 200
        $state.Tag = @{ object = @{ type = 'commit'; sha = $source } }
        return
    }
    if ($arguments[1] -eq 'upload') {
        foreach ($file in $arguments[5..($arguments.Count - 1)]) {
            $id = [string] ($state.Release.assets.Count + 1)
            $state.Release.assets += [pscustomobject] @{
                id = $id; name = [IO.Path]::GetFileName($file)
                digest = "sha256:$((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash)"
            }
            if ([IO.Path]::GetExtension($file) -in @('.json', '.txt')) {
                $state.Content[$id] = Get-Content -LiteralPath $file -Raw
            }
        }
        return
    }
    if ($arguments[1] -eq 'edit') { $state.Release.draft = $false; return }
    throw "Unexpected fake GitHub operation: $($arguments -join ' ')"
}
function Invoke-Release {
    param([string] $Action = 'Check')
    & (Join-Path $PSScriptRoot 'Publish-GitHubRelease.ps1') -Action $Action -Version $version `
        -SourceRevision $source -ArtifactPath $fixture
}
function Assert {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}
function Reject {
    param([scriptblock] $Operation)
    $rejected = $false
    try { & $Operation | Out-Null }
    catch { $rejected = $true }
    Assert $rejected 'Expected publication to fail closed.'
}

function Test-RunnerExit {
    $runner = Join-Path $fixture 'runner.ps1'
    [IO.File]::WriteAllText($runner, @'
param(
    [string] $PublicationScript,
    [string] $Version,
    [string] $SourceRevision,
    [int] $ReleaseStatus,
    [int] $TagStatus
)
$ErrorActionPreference = 'Stop'
$pwsh = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
function gh {
    if ($args.Count -ne 3 -or $args[0] -ne 'api' -or $args[1] -ne '--include') {
        throw 'Runner fixture permits only read-only publication lookups.'
    }
    $status = switch -Wildcard ($args[2]) {
        '*/releases/tags/*' { $ReleaseStatus; break }
        '*/git/ref/tags/*' { $TagStatus; break }
        default { throw 'Unexpected publication lookup.' }
    }
    $body = if ($status -eq 200) {
        @{ object = @{ type = 'commit'; sha = $SourceRevision } } | ConvertTo-Json -Compress
    } else { '{}' }
    "HTTP/2.0 $status Test`ncontent-type: application/json`n`n$body"
    # Exercise native failure status in the child, not just the returned state.
    & $pwsh -NoProfile -NonInteractive -Command "exit $(if ($status -eq 200) { 0 } else { 1 })"
    $global:LASTEXITCODE = $LASTEXITCODE
}
$state = & $PublicationScript -Action Check -Version $Version -SourceRevision $SourceRevision
"already-published=$($state.AlreadyPublished.ToString().ToLowerInvariant())"
if (Test-Path variable:\LASTEXITCODE) { exit $LASTEXITCODE }
'@)
    $pwsh = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
    $cases = @(
        @{ Release = 404; Tag = 404; Exit = 0 }
        @{ Release = 404; Tag = 200; Exit = 0 }
    )
    foreach ($status in @(401, 403, 429, 500)) {
        $cases += @{ Release = $status; Tag = 404; Exit = 1 }
        $cases += @{ Release = 404; Tag = $status; Exit = 1 }
    }
    foreach ($case in $cases) {
        $output = @(& $pwsh -NoProfile -NonInteractive -File $runner `
            -PublicationScript (Join-Path $PSScriptRoot 'Publish-GitHubRelease.ps1') `
            -Version $version -SourceRevision $source -ReleaseStatus $case.Release -TagStatus $case.Tag 2>&1)
        $exit = $LASTEXITCODE
        Assert ($exit -eq $case.Exit) "Runner release/tag $($case.Release)/$($case.Tag): expected exit $($case.Exit), got $exit. $output"
        if ($case.Exit -eq 0) {
            Assert ($output -contains 'already-published=false') 'Runner did not report an absent release.'
        } else {
            Assert (($output -join "`n") -like '*Cannot establish GitHub publication state*') 'Runner failure lost the lookup diagnostic.'
            Assert ($output -notcontains 'already-published=false') 'Failed lookup returned success-shaped publication state.'
        }
    }
    Write-Host "Runner process tests passed: $($cases.Count) cases; missing release/tag exit 0, non-404 release/tag failures exit 1."
}

try {
    $env:GITHUB_ACTIONS = 'true'
    $env:GITHUB_EVENT_NAME = 'push'
    $env:GITHUB_REF = 'refs/heads/main'
    $env:GITHUB_REPOSITORY = 'roryprimrose/Kora'
    $env:GITHUB_SHA = $source
    $env:GITHUB_RUN_ID = '123'
    New-Item -ItemType Directory -Path $fixture | Out-Null
    Test-RunnerExit
    Assert (-not (Invoke-Release).AlreadyPublished) 'Known 404 was not absent.'
    foreach ($status in @(401, 403, 429, 500)) {
        $global:KoraReleaseTestState.Status = $status
        Reject { Invoke-Release }
    }
    $global:KoraReleaseTestState.Status = 404
    $global:KoraReleaseTestState.Tag = @{ object = @{ type = 'commit'; sha = ('b' * 40) } }
    Reject { Invoke-Release Publish }
    $global:KoraReleaseTestState.Tag = $null
    $env:GITHUB_EVENT_NAME = 'pull_request'
    Reject { Invoke-Release Publish }
    $env:GITHUB_EVENT_NAME = 'push'
    $env:GITHUB_REPOSITORY = 'fork/Kora'
    Reject { Invoke-Release Publish }
    $env:GITHUB_REPOSITORY = 'roryprimrose/Kora'
    $env:GITHUB_REF = 'refs/heads/feature'
    Reject { Invoke-Release Publish }
    $env:GITHUB_REF = 'refs/heads/main'
    foreach ($rid in @('win-x64', 'win-x86')) {
        $path = Join-Path $fixture "Kora-$rid"
        New-Item -ItemType Directory -Path $path | Out-Null
        [IO.File]::WriteAllText((Join-Path $path 'Kora.dll'), "Fake bytes $rid")
        [IO.File]::WriteAllText((Join-Path $path '.hidden-notice'), 'ZIP must retain hidden files.')
        & (Join-Path $PSScriptRoot 'Test-InstallerPayload.ps1') -PayloadPath $path `
            -Version $version -SourceRevision $source -WriteManifest
    }
    $installer = Join-Path $fixture 'installer'
    New-Item -ItemType Directory -Path $installer | Out-Null
    $files = @()
    foreach ($name in @('Kora-0.1.0-win-x64.msi', "Kora-Setup-$version-win-x64.exe")) {
        [IO.File]::WriteAllText((Join-Path $installer $name), "Fake inspected $name")
        $files += @{ name = $name; sha256 = (Get-FileHash -LiteralPath (Join-Path $installer $name) -Algorithm SHA256).Hash }
    }
    Copy-Item -LiteralPath (Join-Path $fixture 'Kora-win-x64\payload-manifest.json') -Destination $installer
    $receipt = @{
        version = $version; productVersion = '0.1.0'; sourceRevision = $source; sourceDirty = $false
        buildOrigin = 'github-actions'; msiIceValidation = 'skipped-explicitly'; packageInspection = 'passed'
        unsigned = $true; files = $files
    }
    $receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $installer 'installer-build.json')
    Reject { Invoke-Release Publish }
    Assert ($global:KoraReleaseTestState.Writes -eq 0) 'Invalid candidate performed a release write.'
    $receipt.msiIceValidation = 'passed'
    $receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $installer 'installer-build.json')
    Invoke-Release Publish | Out-Null
    Assert ($global:KoraReleaseTestState.Writes -eq 3) 'Expected draft-create, upload and final publish only.'
    Assert ((Invoke-Release).AlreadyPublished) 'Exact complete release is not idempotent.'
    Assert ($global:KoraReleaseTestState.Writes -eq 3) 'Idempotent check rewrote assets.'
    $zip = @(Get-ChildItem -LiteralPath $fixture -Recurse -Filter "Kora-$version-win-x64.zip")[0]
    $archive = [IO.Compression.ZipFile]::OpenRead($zip.FullName)
    try { Assert ($archive.Entries.FullName -contains '.hidden-notice') 'ZIP omitted a payload file.' }
    finally { $archive.Dispose() }
    $global:KoraReleaseTestState.Release.draft = $true
    Reject { Invoke-Release Publish }
    $global:KoraReleaseTestState.Release.draft = $false
    $global:KoraReleaseTestState.Release.prerelease = $false
    Reject { Invoke-Release }
    $global:KoraReleaseTestState.Release.prerelease = $true
    $global:KoraReleaseTestState.Release.assets[0].digest = "sha256:$('0' * 64)"
    Reject { Invoke-Release }
    Assert ($global:KoraReleaseTestState.Writes -eq 3) 'Conflicting release was modified.'
    $version = '0.1.0'
    $env:GITHUB_REF = 'refs/tags/v0.1.0'
    $global:KoraReleaseTestState.Status = 404
    Assert (-not (Invoke-Release).AlreadyPublished) 'Stable tag lookup failed.'
    foreach ($rid in @('win-x64', 'win-x86')) {
        & (Join-Path $PSScriptRoot 'Test-InstallerPayload.ps1') -PayloadPath (Join-Path $fixture "Kora-$rid") `
            -Version $version -SourceRevision $source -WriteManifest
    }
    Copy-Item -LiteralPath (Join-Path $fixture 'Kora-win-x64\payload-manifest.json') -Destination $installer -Force
    [IO.File]::WriteAllText((Join-Path $installer "Kora-Setup-$version-win-x64.exe"), 'Fake inspected stable bundle')
    $receipt.version = $version
    $receipt.files = @($files[0], @{ name = "Kora-Setup-$version-win-x64.exe"; sha256 =
        (Get-FileHash -LiteralPath (Join-Path $installer "Kora-Setup-$version-win-x64.exe") -Algorithm SHA256).Hash })
    $receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $installer 'installer-build.json')
    Invoke-Release Publish | Out-Null
    Assert (-not $global:KoraReleaseTestState.Release.prerelease) 'Stable release was classified as beta.'
    Assert ((Invoke-Release).AlreadyPublished) 'Stable publication is not idempotent.'
    $creates = @($global:KoraReleaseTestState.Calls | Where-Object { $_[0] -eq 'release' -and $_[1] -eq 'create' })
    Assert ($creates[0] -contains '--prerelease' -and $creates[1] -notcontains '--prerelease') 'Incorrect release channels.'
    $edits = @($global:KoraReleaseTestState.Calls | Where-Object { $_[0] -eq 'release' -and $_[1] -eq 'edit' })
    Assert ($edits[0] -contains '--latest=false' -and $edits[1] -contains '--latest=true') 'Incorrect latest policy.'
    Write-Host 'Publication tests passed: beta/stable assets/notes, exact-byte checks, idempotence, conflicts, failed lookups and PR/fork/ref rejection.'
}
finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    Remove-Variable -Name KoraReleaseTestState -Scope Global
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
