[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tests\GitHubRelease.Fixture.ps1')
$fixture = Join-Path ([IO.Path]::GetTempPath()) "KoraReleaseTests-$([guid]::NewGuid().ToString('N'))"
$saved = @{}
foreach ($name in @('GITHUB_ACTIONS', 'GITHUB_EVENT_NAME', 'GITHUB_REF', 'GITHUB_REPOSITORY', 'GITHUB_SHA', 'GITHUB_RUN_ID')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
}
$source = 'a' * 40
$version = '0.1.0-beta4'
$global:KoraReleaseTestState = New-ReleaseFixtureState
$assertions = 0

function Invoke-Release {
    param([string] $Action = 'Check')
    & (Join-Path $PSScriptRoot 'Publish-GitHubRelease.ps1') -Action $Action -Version $version `
        -SourceRevision $source -ArtifactPath $fixture
}
function Assert {
    param([bool] $Condition, [string] $Message)
    $script:assertions++
    if (-not $Condition) { throw $Message }
}
function Reject {
    param([scriptblock] $Operation, [string] $Diagnostic)
    $failure = $null
    try { & $Operation | Out-Null }
    catch { $failure = $_ }
    Assert ($null -ne $failure) 'Expected publication to fail closed.'
    if ($Diagnostic) { Assert ($failure.ToString() -like "*$Diagnostic*") "Lost diagnostic: $failure" }
}
function Copy-State {
    param($State)
    return ($State | ConvertTo-Json -Depth 15 | ConvertFrom-Json -AsHashtable)
}
function New-Candidate {
    foreach ($rid in @('win-x64', 'win-x86')) {
        $path = Join-Path $fixture "Kora-$rid"
        New-Item -ItemType Directory -Path $path -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $path 'Kora.dll'), "Fake bytes $rid")
        [IO.File]::WriteAllText((Join-Path $path '.hidden-notice'), 'ZIP must retain hidden files.')
        & (Join-Path $PSScriptRoot 'Test-InstallerPayload.ps1') -PayloadPath $path `
            -Version $version -SourceRevision $source -WriteManifest
    }
    $installer = Join-Path $fixture 'installer'
    New-Item -ItemType Directory -Path $installer -Force | Out-Null
    $files = @()
    foreach ($name in @('Kora-0.1.0-win-x64.msi', "Kora-Setup-$version-win-x64.exe")) {
        [IO.File]::WriteAllText((Join-Path $installer $name), "Fake inspected $name")
        $files += @{ name = $name; sha256 = (Get-FileHash -LiteralPath (Join-Path $installer $name) -Algorithm SHA256).Hash }
    }
    Copy-Item -LiteralPath (Join-Path $fixture 'Kora-win-x64\payload-manifest.json') -Destination $installer -Force
    @{
        version = $version; productVersion = '0.1.0'; sourceRevision = $source; sourceDirty = $false
        buildOrigin = 'github-actions'; msiIceValidation = 'passed'; packageInspection = 'passed'
        unsigned = $true; files = $files
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $installer 'installer-build.json') -Encoding utf8NoBOM
}

function Test-RunnerExit {
    $runner = Join-Path $fixture 'runner.ps1'
    [IO.File]::WriteAllText($runner, @'
param([string] $PublicationScript, [string] $FakeScript, [string] $Version, [string] $SourceRevision,
    [string] $ArtifactPath, [string] $StatePath, [string] $ResultPath, [string] $Action)
. $FakeScript
$global:KoraReleaseTestState = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json -AsHashtable
try {
    $state = & $PublicationScript -Action $Action -Version $Version -SourceRevision $SourceRevision -ArtifactPath $ArtifactPath
    if ($Action -eq 'Check') { "already-published=$($state.AlreadyPublished.ToString().ToLowerInvariant())" }
    'publication-step-succeeded'
} finally {
    $global:KoraReleaseTestState | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $ResultPath
}
'@)
    $wrapper = Join-Path $fixture 'actions-wrapper.ps1'
    $statePath = Join-Path $fixture 'process-state.json'
    $resultPath = Join-Path $fixture 'process-result.json'
    $pwsh = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
    $scriptPath = Join-Path $PSScriptRoot 'Publish-GitHubRelease.ps1'
    $fakePath = Join-Path $PSScriptRoot 'tests\GitHubRelease.Fixture.ps1'
    $cases = @(
        @{ Action = 'Check'; Release = 404; Tag = 404; Exit = 0 }
        @{ Action = 'Check'; Release = 404; Tag = 200; Exit = 0 }
        @{ Action = 'Publish'; Exit = 0 }
    )
    foreach ($status in @(401, 403, 429, 500)) {
        $cases += @{ Action = 'Check'; Release = $status; Tag = 404; Exit = 1 }
        $cases += @{ Action = 'Check'; Release = 404; Tag = $status; Exit = 1 }
        $cases += @{ Action = 'Check'; List = $status; Exit = 1 }
    }
    foreach ($operation in @('create', 'upload', 'tag', 'publish')) {
        foreach ($after in @($false, $true)) {
            $cases += @{ Action = 'Publish'; Fail = $operation; After = $after; Exit = 1 }
        }
        $cases += @{ Action = 'Publish'; HardStop = $operation; Exit = 137 }
    }
    $cases += @(
        @{ Action = 'Publish'; Fail = 'notes'; After = $false; Legacy = $true; Exit = 1 }
        @{ Action = 'Publish'; Fail = 'notes'; After = $true; Legacy = $true; Exit = 1 }
        @{ Action = 'Publish'; HardStop = 'notes'; Legacy = $true; Exit = 137 }
    )
    foreach ($case in $cases) {
        $state = if ($case.ContainsKey('Legacy')) { Copy-State $legacyDraft } else { New-ReleaseFixtureState }
        if ($case.ContainsKey('Release')) { $state.ReleaseStatus = $case.Release; $state.UseNativeExit = $true }
        if ($case.ContainsKey('Tag')) {
            $state.TagStatus = $case.Tag
            if ($case.Tag -eq 200) { $state.Tag = @{ object = @{ type = 'commit'; sha = $source } } }
        }
        if ($case.ContainsKey('List')) { $state.ListStatus = $case.List }
        if ($case.ContainsKey('Fail')) { $state.Fail = $case.Fail; $state.FailAfter = $case.After }
        if ($case.ContainsKey('HardStop')) { $state.HardStop = $case.HardStop; $state.ResultPath = $resultPath }
        $state | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $statePath
        # Exact Actions pwsh prologue, dot-source command and native-status epilogue.
        [IO.File]::WriteAllText($wrapper, @"
`$ErrorActionPreference = 'Stop'
. '$runner' -PublicationScript '$scriptPath' -FakeScript '$fakePath' -Version '$version' -SourceRevision '$source' -ArtifactPath '$fixture' -StatePath '$statePath' -ResultPath '$resultPath' -Action '$($case.Action)'
if (Test-Path -LiteralPath variable:\LASTEXITCODE) { exit `$LASTEXITCODE }
"@)
        $output = @(& $pwsh -NoProfile -NonInteractive -Command ". '$wrapper'" 2>&1)
        $exit = $LASTEXITCODE
        Assert ($exit -eq $case.Exit) "Actions process $(ConvertTo-Json -InputObject $case -Compress): expected $($case.Exit), got $exit. $output"
        Assert (($output -contains 'publication-step-succeeded') -eq ($case.Exit -eq 0)) "Incorrect process success output: $output"
        if ($case.Action -eq 'Check' -and $case.Exit -eq 0) {
            Assert ($output -contains 'already-published=false') 'Missing state was not reported.'
        }
        if ($case.ContainsKey('Fail') -or $case.ContainsKey('HardStop')) {
            $partial = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json -AsHashtable
            $partial.Fail = ''
            $partial.HardStop = ''
            $partial | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $statePath
            $interruptedStaging = @(Get-ChildItem -LiteralPath $fixture -Directory -Filter 'release-*')
            $output = @(& $pwsh -NoProfile -NonInteractive -Command ". '$wrapper'" 2>&1)
            $exit = $LASTEXITCODE
            Assert ($exit -eq 0 -and $output -contains 'publication-step-succeeded') "Process retry failed: $output"
            $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json -AsHashtable
            Assert ($result.Releases.Count -eq 1 -and -not $result.Releases[0].draft) 'Retry did not reconcile a single publication.'
            foreach ($directory in $interruptedStaging) { Remove-Item -LiteralPath $directory.FullName -Recurse -Force }
        }
    }
    Write-Host "Actions pwsh process tests passed: $($cases.Count) cases plus 15 partial-write/hard-interruption retries."
}

try {
    $env:GITHUB_ACTIONS = 'true'
    $env:GITHUB_EVENT_NAME = 'push'
    $env:GITHUB_REF = 'refs/heads/main'
    $env:GITHUB_REPOSITORY = 'roryprimrose/Kora'
    $env:GITHUB_SHA = $source
    $env:GITHUB_RUN_ID = '123'
    New-Item -ItemType Directory -Path $fixture | Out-Null
    Assert (-not (Invoke-Release).AlreadyPublished) 'Known 404 was not absent.'
    Assert ($global:LASTEXITCODE -eq 0) 'Expected 404 escaped to the Actions epilogue.'
    foreach ($status in @(401, 403, 429, 500)) {
        $global:KoraReleaseTestState.ReleaseStatus = $status
        Reject { Invoke-Release } 'Cannot establish GitHub publication state'
    }
    $global:KoraReleaseTestState = New-ReleaseFixtureState
    $global:KoraReleaseTestState.Tag = @{ object = @{ type = 'commit'; sha = ('b' * 40) } }
    Reject { Invoke-Release Publish } 'exact source revision'
    $global:KoraReleaseTestState = New-ReleaseFixtureState
    foreach ($name in @('GITHUB_ACTIONS', 'GITHUB_EVENT_NAME', 'GITHUB_REF', 'GITHUB_REPOSITORY', 'GITHUB_SHA')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, 'untrusted')
        Reject { Invoke-Release Publish }
        [Environment]::SetEnvironmentVariable($name, $value)
    }
    New-Candidate
    $receiptPath = Join-Path $fixture 'installer\installer-build.json'
    $receiptBytes = [IO.File]::ReadAllBytes($receiptPath)
    $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    $receipt.msiIceValidation = 'skipped-explicitly'
    $receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $receiptPath
    Reject { Invoke-Release Publish } 'full ICE'
    Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Invalid candidate performed a release write.'
    [IO.File]::WriteAllBytes($receiptPath, $receiptBytes)
    Invoke-Release Publish | Out-Null
    $complete = Copy-State $global:KoraReleaseTestState
    Assert (($complete.Writes -join ',') -eq "create,$((@('upload') * 8) -join ','),tag,publish") 'Incorrect mutation ordering.'
    Assert ((Invoke-Release).AlreadyPublished) 'Exact published release is not idempotent.'
    Invoke-Release Publish | Out-Null
    Assert ($global:KoraReleaseTestState.Writes.Count -eq $complete.Writes.Count) 'No-op publication changed assets.'

    foreach ($field in @('target_commitish', 'body', 'prerelease', 'digest', 'size', 'name', 'state', 'tag')) {
        $global:KoraReleaseTestState = Copy-State $complete
        $release = $global:KoraReleaseTestState.Releases[0]
        switch ($field) {
            'target_commitish' { $release.target_commitish = 'main' }
            'body' { $release.body = "<!-- kora-source: $('b' * 40) -->" }
            'prerelease' { $release.prerelease = $false }
            'digest' { $release.assets[0].digest = "sha256:$('0' * 64)" }
            'size' { $release.assets[0].size = 0 }
            'name' { $release.assets[0].name = 'unexpected.zip' }
            'state' { $release.assets[0].state = 'starter' }
            'tag' { $global:KoraReleaseTestState.Tag.object.sha = 'b' * 40 }
        }
        Reject { Invoke-Release Publish }
        Assert ($global:KoraReleaseTestState.Writes.Count -eq $complete.Writes.Count) "Immutable $field conflict was modified."
    }
    $global:KoraReleaseTestState = Copy-State $complete
    $global:KoraReleaseTestState.Releases[0].assets += $global:KoraReleaseTestState.Releases[0].assets[0]
    Reject { Invoke-Release } 'duplicate'
    $global:KoraReleaseTestState = Copy-State $complete
    $global:KoraReleaseTestState.Releases += $global:KoraReleaseTestState.Releases[0]
    Reject { Invoke-Release } 'Multiple releases'

    # The draft-only state remains absent from releases/tags even with all assets uploaded.
    $draftOnly = Copy-State $complete
    $draftOnly.Releases[0].draft = $true
    $draftOnly.Tag = $null
    $draftOnly.Writes = @()
    $global:KoraReleaseTestState = Copy-State $draftOnly
    Assert (-not (Invoke-Release).AlreadyPublished) 'Draft was treated as published.'
    Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Read-only Check promoted a draft.'
    $env:GITHUB_RUN_ID = '456'
    Get-ChildItem -LiteralPath (Join-Path $fixture 'Kora-win-x64') -File -Force |
        ForEach-Object { $_.LastWriteTimeUtc = [datetime]::UtcNow.AddDays(-1) }
    Invoke-Release Publish | Out-Null
    Assert (($global:KoraReleaseTestState.Writes -join ',') -eq 'tag,publish') 'Complete draft retry rewrote existing bytes.'
    Assert ([Convert]::ToBase64String([byte[]] $global:KoraReleaseTestState.Content['8']) -ceq
        [Convert]::ToBase64String([byte[]] $draftOnly.Content['8'])) 'Original exact provenance bytes were replaced.'
    $env:GITHUB_RUN_ID = '123'

    $global:KoraReleaseTestState = Copy-State $draftOnly
    $global:KoraReleaseTestState.Releases[0].body = [regex]::Replace($global:KoraReleaseTestState.Releases[0].body,
        '- D-009 uses private-profile standard SQLite; encryption is optional future R30 work\.\r?\n  Installed lifecycle/protection, ordinary packaged-native loading and durable recovery/deletion remain separate gates\.',
        '- Installed lifecycle/protection and encrypted-storage/native admission remain separate gates.')
    $legacyDraft = Copy-State $global:KoraReleaseTestState
    foreach ($after in @($false, $true)) {
        $global:KoraReleaseTestState = Copy-State $legacyDraft
        $global:KoraReleaseTestState.Fail = 'notes'
        $global:KoraReleaseTestState.FailAfter = $after
        Reject { Invoke-Release Publish } 'Synthetic API failure'
        Assert ($global:KoraReleaseTestState.Releases[0].draft -and $null -eq $global:KoraReleaseTestState.Tag) 'Failed disclosure update crossed publication boundary.'
        $global:KoraReleaseTestState.Fail = ''
        Invoke-Release Publish | Out-Null
        Assert ($global:KoraReleaseTestState.Releases[0].body -notlike '*encrypted-storage/native admission*') 'Legacy draft retained obsolete mandatory encryption gate.'
        Assert ($global:KoraReleaseTestState.Writes -notcontains 'upload') 'Disclosure repair changed asset bytes.'
    }

    foreach ($field in @('target_commitish', 'body', 'prerelease')) {
        $global:KoraReleaseTestState = Copy-State $draftOnly
        $release = $global:KoraReleaseTestState.Releases[0]
        if ($field -eq 'prerelease') { $release.prerelease = $false } else { $release.$field = 'wrong' }
        Reject { Invoke-Release Publish } 'source/channel'
        Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Mismatched draft was changed.'
    }
    $global:KoraReleaseTestState = Copy-State $draftOnly
    $global:KoraReleaseTestState.DownloadCorrupt = $true
    Reject { Invoke-Release Publish } 'Downloaded draft asset'
    foreach ($status in @(401, 403, 429, 500)) {
        $global:KoraReleaseTestState = Copy-State $draftOnly
        $global:KoraReleaseTestState.DownloadStatus = $status
        Reject { Invoke-Release Publish } 'Synthetic download API failure'
        Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Failed download was treated as absent content.'
        $global:KoraReleaseTestState = Copy-State $complete
        $global:KoraReleaseTestState.ContentStatus = $status
        Reject { Invoke-Release } 'Synthetic asset API failure'
    }
    $global:KoraReleaseTestState = Copy-State $complete
    $global:KoraReleaseTestState.Releases[0].assets = @($global:KoraReleaseTestState.Releases[0].assets | Select-Object -Skip 1)
    Reject { Invoke-Release } 'missing or unexpected assets'
    foreach ($field in @('version', 'sourceRevision', 'unsigned', 'productionAccepted', 'workflowRun', 'sha256', 'duplicate')) {
        $global:KoraReleaseTestState = Copy-State $complete
        $manifest = [Text.Encoding]::UTF8.GetString([byte[]] $complete.Content['8']) | ConvertFrom-Json
        switch ($field) {
            'unsigned' { $manifest.unsigned = 'true' }
            'productionAccepted' { $manifest.productionAccepted = $true }
            'sha256' { $manifest.assets[0].sha256 = '0' * 64 }
            'duplicate' { $manifest.assets += $manifest.assets[0] }
            default { $manifest.$field = 'wrong' }
        }
        $global:KoraReleaseTestState.Content['8'] = [Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Depth 6))
        Reject { Invoke-Release } 'provenance'
        Assert ($global:KoraReleaseTestState.Writes.Count -eq $complete.Writes.Count) 'Invalid published provenance caused a mutation.'
    }
    foreach ($checksums in @('malformed', ("$('0' * 64)  unexpected.zip`n" * 7),
        (([Text.Encoding]::UTF8.GetString([byte[]] $complete.Content['7']) -split '\r?\n')[0] + "`n") * 7)) {
        $global:KoraReleaseTestState = Copy-State $complete
        $global:KoraReleaseTestState.Content['7'] = [Text.Encoding]::UTF8.GetBytes($checksums)
        Reject { Invoke-Release } 'checksum'
    }
    $x86File = Join-Path $fixture 'Kora-win-x86\Kora.dll'
    $x86Manifest = Join-Path $fixture 'Kora-win-x86\payload-manifest.json'
    $x86ManifestBytes = [IO.File]::ReadAllBytes($x86Manifest)
    [IO.File]::WriteAllText($x86File, 'Changed x86 candidate bytes')
    & (Join-Path $PSScriptRoot 'Test-InstallerPayload.ps1') -PayloadPath (Split-Path $x86File) `
        -Version $version -SourceRevision $source -WriteManifest
    $global:KoraReleaseTestState = Copy-State $draftOnly
    Reject { Invoke-Release Publish } 'Draft ZIP differs'
    Assert ($global:KoraReleaseTestState.Writes.Count -eq 0) 'Mismatched draft ZIP was replaced.'
    [IO.File]::WriteAllText($x86File, 'Fake bytes win-x86')
    [IO.File]::WriteAllBytes($x86Manifest, $x86ManifestBytes)
    $global:KoraReleaseTestState = Copy-State $draftOnly
    $global:KoraReleaseTestState.Releases[0].assets[2].digest = "sha256:$('0' * 64)"
    Reject { Invoke-Release Publish } 'exact final bytes'
    $global:KoraReleaseTestState = Copy-State $draftOnly
    $global:KoraReleaseTestState.DriftOnTag = $true
    Reject { Invoke-Release Publish } 'source/channel'
    Assert ($global:KoraReleaseTestState.Releases[0].draft) 'Source drift was published.'
    $global:KoraReleaseTestState = Copy-State $draftOnly
    $global:KoraReleaseTestState.ReadStatus = 500
    Reject { Invoke-Release } 'Cannot establish GitHub publication state'
    $global:KoraReleaseTestState = Copy-State $draftOnly
    $global:KoraReleaseTestState.Pages[1] = @(@{ id = 999; tag_name = 'unrelated' }) * 100
    $global:KoraReleaseTestState.Pages[2] = $draftOnly.Releases
    Assert (-not (Invoke-Release).AlreadyPublished) 'Paginated draft lookup failed.'
    Assert (@($global:KoraReleaseTestState.Calls | Where-Object { ($_ -join ' ') -like '*page=2*' }).Count -gt 0) 'Lookup omitted the second page.'
    $global:KoraReleaseTestState = Copy-State $complete
    $global:KoraReleaseTestState.Tag = @{ object = @{ type = 'tag'; sha = 'c' * 40 } }
    $global:KoraReleaseTestState.Annotated[('c' * 40)] = @{ object = @{ type = 'commit'; sha = $source } }
    Assert ((Invoke-Release).AlreadyPublished) 'Annotated exact-source tag was rejected.'
    $global:KoraReleaseTestState.Annotated[('c' * 40)] = @{ object = @{ type = 'tag'; sha = 'c' * 40 } }
    Reject { Invoke-Release } 'exact source revision'

    Test-RunnerExit
    $version = '0.1.0'
    $env:GITHUB_REF = 'refs/tags/v0.1.0'
    $global:KoraReleaseTestState = New-ReleaseFixtureState
    $global:KoraReleaseTestState.Tag = @{ object = @{ type = 'commit'; sha = $source } }
    New-Candidate
    Invoke-Release Publish | Out-Null
    Assert ((Invoke-Release).AlreadyPublished) 'Stable publication is not idempotent.'
    Assert ($global:KoraReleaseTestState.Writes -notcontains 'tag') 'Existing stable tag was replaced.'
    $publish = @($global:KoraReleaseTestState.Calls | Where-Object { $_ -contains 'PATCH' })[0]
    Assert ($publish -contains 'make_latest=true') 'Incorrect stable latest policy.'
    $betaPublish = @($complete.Calls | Where-Object { $_ -contains 'PATCH' })[0]
    Assert ($betaPublish -contains 'make_latest=false') 'Incorrect beta latest policy.'
    Assert (@(Get-ChildItem -LiteralPath $fixture -Directory -Filter 'release-*').Count -eq 0) 'Publication staging leaked after success/failure.'
    Write-Host "Publication tests passed: $assertions assertions; draft lifecycle, exact bytes, collisions, retries, source/channel, immutable no-op and Actions process exit."
}
finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    Remove-Variable -Name KoraReleaseTestState -Scope Global
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
