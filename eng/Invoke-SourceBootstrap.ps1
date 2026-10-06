#requires -Version 7.0
<#
.SYNOPSIS
Preview or build exact canonical Kora source into owned, verified local staging.
.DESCRIPTION
Version 1.1.0. Review this script, its sibling helpers and selected source/dependency
code before Build with -TrustBuildCode. Local-source channel only: never official
release provenance or protected activation. Preview is read-only and the default.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Root,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string] $Revision,
    [ValidateSet('Preview', 'Build')][string] $Action = 'Preview',
    [ValidateSet('local-source')][string] $Channel = 'local-source',
    [switch] $TrustBuildCode
)
. (Join-Path $PSScriptRoot 'SourceBootstrap.Common.ps1')
Invoke-SourceBootstrap -Root $Root -Revision $Revision -Action $Action -TrustBuildCode:$TrustBuildCode
