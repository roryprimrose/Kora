#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Stage,
    [Parameter(Mandatory)][string] $Root,
    [Parameter(Mandatory)][string] $Checkout,
    [Parameter(Mandatory)][string] $Repository,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string] $Revision,
    [Parameter(Mandatory)][string] $Deployment
)
. (Join-Path $PSScriptRoot 'SourceBootstrap.Common.ps1')
Assert-ManagedCheckoutIdentity $Checkout $Repository $Revision
Assert-SourceStage $Stage $Root $Checkout $Repository $Revision $Deployment | Out-Null
Write-Host 'Static stage verification passed. No application/native library was loaded or launched.'
