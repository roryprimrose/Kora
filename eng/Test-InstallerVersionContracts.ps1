[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$resolver = Join-Path $PSScriptRoot 'Get-InstallerProductVersion.ps1'
$cases = [ordered]@{
    '0.1.0-beta1' = '0.1.1'
    '0.1.0-beta1022' = '0.1.1022'
    '0.1.0' = '0.1.1023'
    '0.1.1-beta1' = '0.1.1025'
    '0.1.1' = '0.1.2047'
    '0.2.0-beta1' = '0.2.1'
    '1.0.0-beta1' = '1.0.1'
    '255.255.63' = '255.255.65535'
}
$previous = [version]'0.0.0'
foreach ($case in $cases.GetEnumerator()) {
    $actual = & $resolver -Version $case.Key
    if ($actual -cne $case.Value -or [version]$actual -le $previous) {
        throw "Installer mapping/order mismatch for $($case.Key): $actual."
    }
    if ((& $resolver -Version $case.Key) -cne $actual) { throw 'Installer rerun identity changed.' }
    $previous = [version]$actual
}
foreach ($invalid in @('0.1.0-beta0', '0.1.0-beta1023', '0.1.64', '256.0.0', '0.256.0',
    '0.1.0-alpha1', '0.1.0-beta01', '00.1.0', '0.1.0+build', '0.1', '0.1.0.0', '../0.1.0',
    '999999999999999999999999.0.0')) {
    $rejected = $false
    try { & $resolver -Version $invalid | Out-Null }
    catch { $rejected = $true }
    if (-not $rejected) { throw "Invalid installer version was admitted: $invalid." }
}
Write-Host 'Installer version contracts passed: exact mapping, monotonic beta/stable/patch/minor/major ordering, repeatability and bounds.'
