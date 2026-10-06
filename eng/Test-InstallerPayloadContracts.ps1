[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture = Join-Path ([IO.Path]::GetTempPath()) "KoraPayloadTests-$([guid]::NewGuid().ToString('N'))"
$version = '0.1.0'
$source = & git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve payload-test source revision.' }
$staging = Join-Path $root 'artifacts\installer'
$before = @(if (Test-Path -LiteralPath $staging) { Get-ChildItem -LiteralPath $staging -Directory | Select-Object -ExpandProperty Name })
$savedActions = $env:GITHUB_ACTIONS
$env:GITHUB_ACTIONS = $null
$script:compilerCalls = 0

# Invalid fixtures must be refused before any restore, publish or WiX build.
function dotnet {
    $script:compilerCalls++
    throw 'Invalid payload reached dotnet.'
}
function Write-FixtureManifest {
    & (Join-Path $PSScriptRoot 'Test-InstallerPayload.ps1') -PayloadPath $fixture `
        -Version $version -SourceRevision $source -WriteManifest
}
function Assert-RejectedBeforePackaging {
    param([string] $ExpectedMessage)
    $rejected = $false
    try {
        & (Join-Path $PSScriptRoot 'Build-Installer.ps1') -Version $version `
            -ApplicationPayloadPath $fixture -SkipMsiValidation
    }
    catch {
        if ($_.Exception.Message -notlike $ExpectedMessage) { throw }
        $rejected = $true
    }
    if (-not $rejected -or $script:compilerCalls -ne 0) {
        throw 'Invalid payload was not rejected before compiler invocation.'
    }
    $after = @(if (Test-Path -LiteralPath $staging) { Get-ChildItem -LiteralPath $staging -Directory | Select-Object -ExpandProperty Name })
    if (($before -join '|') -cne ($after -join '|')) { throw 'Invalid payload created installer staging.' }
}

try {
    New-Item -ItemType Directory -Path $fixture | Out-Null
    $binary = Join-Path $fixture 'Kora.dll'
    $hidden = Join-Path $fixture '.hidden-notice'
    [IO.File]::WriteAllText($binary, 'Synthetic application bytes; never compiled or executed.')
    [IO.File]::WriteAllText($hidden, 'Synthetic hidden notice.')
    if ($IsWindows) { (Get-Item -LiteralPath $hidden).Attributes = [IO.FileAttributes]::Hidden }
    Write-FixtureManifest
    & (Join-Path $PSScriptRoot 'Test-InstallerPayload.ps1') -PayloadPath $fixture `
        -Version $version -SourceRevision $source

    [IO.File]::WriteAllText($binary, 'Changed application bytes.')
    Assert-RejectedBeforePackaging '*Application payload digest mismatch*'
    Write-FixtureManifest
    if ($IsWindows) { (Get-Item -LiteralPath $hidden -Force).Attributes = [IO.FileAttributes]::Normal }
    [IO.File]::WriteAllText($hidden, 'Changed hidden notice.')
    if ($IsWindows) { (Get-Item -LiteralPath $hidden).Attributes = [IO.FileAttributes]::Hidden }
    Assert-RejectedBeforePackaging '*Application payload digest mismatch*'
    Write-FixtureManifest
    Remove-Item -LiteralPath $hidden -Force
    Assert-RejectedBeforePackaging '*Application payload file count changed*'
    Write-FixtureManifest
    [IO.File]::WriteAllText((Join-Path $fixture 'unexpected.dll'), 'Unexpected bytes.')
    Assert-RejectedBeforePackaging '*Application payload file count changed*'
    Write-FixtureManifest
    $manifestPath = Join-Path $fixture 'payload-manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifest.version = '0.1.0-beta99'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
    Assert-RejectedBeforePackaging '*Application payload version or source revision*'
    $manifest.version = $version
    $manifest.sourceRevision = '0' * 40
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
    Assert-RejectedBeforePackaging '*Application payload version or source revision*'
    Remove-Item -LiteralPath $manifestPath
    Assert-RejectedBeforePackaging '*payload-manifest.json*'
    Write-Host 'Payload contract tests passed: exact/hidden bytes, changed/removed/added files, version/source mismatch and missing manifest; no compiler invocation or installer staging.'
}
finally {
    $env:GITHUB_ACTIONS = $savedActions
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
