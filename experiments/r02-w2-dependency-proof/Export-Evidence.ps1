[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $RawDirectory,
    [Parameter(Mandatory)][string] $Destination
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$raw = [IO.Path]::GetFullPath($RawDirectory)
$destination = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $destination) { throw 'Evidence destination must be new.' }
$environment = Get-Content -LiteralPath (Join-Path $raw 'environment.json') -Raw | ConvertFrom-Json
$verification = Get-Content -LiteralPath (Join-Path $raw 'verification.json') -Raw | ConvertFrom-Json
$cleanup = Get-Content -LiteralPath (Join-Path $raw 'cleanup.json') -Raw | ConvertFrom-Json
if ($verification.HarnessErrors.Count -ne 0 -or $verification.ProductionProfileCertified) {
    throw 'Only complete diagnostic runs, explicitly uncertified, may be exported.'
}
if ((Test-Path -LiteralPath $environment.Scratch) -or $cleanup.Events.Count -ne 2) {
    throw 'Owned scratch/profile cleanup was not established.'
}
New-Item -ItemType Directory -Path $destination | Out-Null
$escapedScratch = $environment.Scratch.Replace('\', '\\')
foreach ($name in @(
    'environment.json', 'source.json', 'inputs.json', 'acl-inputs.json', 'verification.json', 'cleanup.json',
    'baseline.json', 'acl.json', 'no-child.json', 'scripts-baseline.json', 'scripts-complete.json',
    'scripts-lost.json', 'scripts-malformed.json', 'scripts-cancel.json'
)) {
    $text = Get-Content -LiteralPath (Join-Path $raw $name) -Raw
    $text = $text.Replace($escapedScratch, '%OWNED_SCRATCH%', [StringComparison]::OrdinalIgnoreCase).
        Replace($environment.HostToken.UserSid, '<HOST_USER_SID>')
    $null = $text | ConvertFrom-Json
    Set-Content -LiteralPath (Join-Path $destination $name) -Value $text -NoNewline
}
$export = @{
    utc = [DateTimeOffset]::UtcNow
    rawRun = [IO.Path]::GetFileName($raw)
    redactions = @('Owned scratch absolute path', 'Real host user SID')
    syntheticContainerSidPreserved = $true
    sourceAndNativeByteHashesPreserved = $true
    files = @(Get-ChildItem -LiteralPath $destination -File | Sort-Object Name | ForEach-Object {
        @{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}
$export | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $destination 'export.json')
Write-Host 'Exported measured evidence with narrowly disclosed redactions; raw evidence retained.'
