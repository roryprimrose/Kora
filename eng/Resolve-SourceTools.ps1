#requires -Version 7.0
<#
.SYNOPSIS
Resolve or statically acquire published exact-source Kora tools without executing downloaded code.
.DESCRIPTION
Run this resolver only from an independently reviewed maintained checkout. Production excludes
drafts/prereleases; preview explicitly admits published prereleases. Neither channel grants
production acceptance, publisher authentication, source-build trust or activation authority.
#>
[CmdletBinding()]
param(
    [ValidateSet('production', 'preview')][string] $Channel = 'production',
    [ValidatePattern('^[a-f0-9]{40}$')][string] $Revision,
    [ValidateSet('Preview', 'Acquire')][string] $Action = 'Preview',
    [string] $OutputDirectory
)
. (Join-Path $PSScriptRoot 'SourceTools.Common.ps1')
if ($Action -eq 'Acquire' -and !$OutputDirectory) { throw 'Acquire requires a dedicated OutputDirectory.' }
$resolution = Get-SourceToolResolution -Channel $Channel -Revision $Revision
if ($Action -eq 'Preview') { return $resolution }
$result = Invoke-SourceToolAcquisition $resolution $OutputDirectory
$current = Get-SourceToolResolution -Channel $Channel -Revision $resolution.revision
if (($current | ConvertTo-Json -Depth 20 -Compress) -cne ($resolution | ConvertTo-Json -Depth 20 -Compress)) {
    throw 'Release/tag/asset identity changed during acquisition; retained bytes are not a successful acquisition.'
}
return $result
