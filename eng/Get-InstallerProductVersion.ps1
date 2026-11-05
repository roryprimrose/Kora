[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Version -cnotmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-beta([1-9]\d*))?$') {
    throw 'Installer versions require major.minor.patch or major.minor.patch-betaN without metadata.'
}
$major = [uint32]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
$minor = [uint32]::Parse($Matches[2], [Globalization.CultureInfo]::InvariantCulture)
$patch = [uint32]::Parse($Matches[3], [Globalization.CultureInfo]::InvariantCulture)
$beta = if ($Matches.ContainsKey(4)) {
    [uint32]::Parse($Matches[4], [Globalization.CultureInfo]::InvariantCulture)
} else { $null }
if ($major -gt 255 -or $minor -gt 255 -or $patch -gt 63 -or ($null -ne $beta -and $beta -gt 1022)) {
    throw 'Installer mapping supports major/minor 0..255, patch 0..63 and beta 1..1022. Advance the release version; never wrap or reuse an MSI version.'
}
# Reserve 1024 ordered MSI build values per semantic patch; stable is above every admitted beta.
$build = ($patch * 1024) + $(if ($null -eq $beta) { 1023 } else { $beta })
return "$major.$minor.$build"
