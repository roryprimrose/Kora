#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string] $Revision,
    [Parameter(Mandatory)][ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-beta(0|[1-9]\d*))?$')][string] $Version,
    [Parameter(Mandatory)][string] $OutputDirectory
)
. (Join-Path $PSScriptRoot 'SourceTools.Common.ps1')
$repositoryPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$tree = @(Get-LocalSourceToolTree $repositoryPath $Revision)
New-ProofDirectory $OutputDirectory
$path = Join-Path $OutputDirectory "Kora-$Version-source-tools.zip"
New-SourceToolArchive $repositoryPath $Revision $Version $path $tree
Test-SourceToolArchive $path $Revision $Version $tree | Out-Null
Write-Host "Packaged exact-source tool closure: $path. Unsigned; no code executed or installed."
