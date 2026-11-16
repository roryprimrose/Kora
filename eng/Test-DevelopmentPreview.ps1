#requires -Version 7.5
[CmdletBinding()]
param([Parameter(Mandatory)][string] $PreviewAssembly)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not (& git -C $repository check-ignore '.scratch\development-preview-tests')) { throw 'Test scratch is not ignored.' }
$root = Join-Path $repository ('.scratch\development-preview-tests\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$assembly = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PreviewAssembly)
if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw 'Supply the already built, marked preview assembly; no build or restore is performed.' }
$fake = Join-Path $root 'fake-dotnet.ps1'
$fixture = @'
if ($args[0] -ne 'publish') { throw 'Unexpected dependency operation in the complete-assets fixture.' }
$position = [Array]::IndexOf($args, '--output')
if ($position -lt 0 -or $position + 1 -ge $args.Count) { throw 'No fixed output argument.' }
if ($args -notcontains '-p:KoraDevelopmentPreview=true') { throw 'Preview profile was omitted.' }
$path = $args[$position + 1]
New-Item -ItemType Directory -Path $path | Out-Null
Copy-Item -LiteralPath $env:KORA_PREVIEW_TEST_ASSEMBLY -Destination (Join-Path $path 'Kora.dll')
exit 0
'@
$fixture | Set-Content -LiteralPath $fake -Encoding utf8NoBOM
$originalLocation = Get-Location
$originalDirectory = [Environment]::CurrentDirectory
$originalAssembly = $env:KORA_PREVIEW_TEST_ASSEMBLY
try {
    $env:KORA_PREVIEW_TEST_ASSEMBLY = $assembly
    Set-Location -LiteralPath $root
    [Environment]::CurrentDirectory = $repository
    & (Join-Path $PSScriptRoot 'Publish-DevelopmentPreview.ps1') -DotNet $fake -OutputDirectory '.\result'
    $expected = Join-Path $root 'result'
    if (-not (Test-Path -LiteralPath (Join-Path $expected 'Kora-DevelopmentPreview-win-x64.zip')) -or
        -not (Test-Path -LiteralPath (Join-Path $expected 'preview-manifest.json'))) {
        throw 'Relative publication output did not bind to the caller PowerShell location.'
    }
    $manifest = Get-Content -LiteralPath (Join-Path $expected 'preview-manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.Format -ne 'kora-development-preview-v1' -or $manifest.ProductionQualified -or $manifest.AutomaticCapabilityOptIn) {
        throw 'Preview receipt falsely claims qualification or opt-in.'
    }
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Publish-DevelopmentPreview.ps1') -DotNet $fake -OutputDirectory '.\result' }
    catch {
        if ($_.Exception.Message -notlike 'Use a fresh output directory*') { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw 'Existing preview output was not preserved.' }
    Write-Host 'Passed: PowerShell-relative output, explicit profile marker, required receipt/archive, no false qualification and overwrite refusal.'
    Write-Host 'This is a publisher-contract fixture, not a full payload or runtime qualification.'
}
finally {
    Set-Location -LiteralPath $originalLocation.Path
    [Environment]::CurrentDirectory = $originalDirectory
    $env:KORA_PREVIEW_TEST_ASSEMBLY = $originalAssembly
}
