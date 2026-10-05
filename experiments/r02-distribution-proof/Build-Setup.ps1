[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Payload,
    [Parameter(Mandatory)][string] $Inspection,
    [Parameter(Mandatory)][string] $MakeNsis,
    [Parameter(Mandatory)][string] $OutputDirectory
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$Payload = (Resolve-Path -LiteralPath $Payload).Path
$Inspection = (Resolve-Path -LiteralPath $Inspection).Path
$MakeNsis = (Resolve-Path -LiteralPath $MakeNsis).Path
Assert-NoLinks $Payload
$manifest = Get-Content -LiteralPath $Inspection -Raw | ConvertFrom-Json
Assert-Payload $Payload $manifest
if ($manifest.revision -notmatch '^[0-9a-f]{40}$' -or $manifest.rid -ne 'win-x64') {
    throw 'Invalid payload provenance.'
}
$lock = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'tools.lock.json') -Raw | ConvertFrom-Json
$versionFlag = if ($IsWindows) { '/VERSION' } else { '-VERSION' }
$version = (& $MakeNsis $versionFlag | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $version -ne "v$($lock.nsis.version)") {
    throw "Expected NSIS v$($lock.nsis.version); observed $version."
}
New-ProofDirectory $OutputDirectory
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$manifestHash = (Get-FileHash -LiteralPath $Inspection -Algorithm SHA256).Hash.ToLowerInvariant()
$id = $manifest.revision.Substring(0, 12) + '-' + $manifestHash.Substring(0, 12)
$setup = Join-Path $OutputDirectory "Kora-R02-$id-unsigned-setup.exe"
$prefix = if ($IsWindows) { '/' } else { '-' }
Invoke-Checked $MakeNsis @(
    "${prefix}V3", "${prefix}DPAYLOAD=$Payload", "${prefix}DSETUP=$setup",
    "${prefix}DPROOF_ID=$id", "${prefix}DINSPECTION=$Inspection",
    (Join-Path $PSScriptRoot 'setup.nsi')
)
Assert-Payload $Payload $manifest
$stream = [IO.File]::OpenRead($setup)
$reader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
try {
    if ($reader.PEHeaders.PEHeader.CertificateTableDirectory.Size -ne 0) {
        throw 'Expected unsigned NSIS setup bytes.'
    }
}
finally { $reader.Dispose(); $stream.Dispose() }
Copy-Item -LiteralPath $Inspection -Destination (Join-Path $OutputDirectory 'payload.json')
$receipt = [ordered]@{
    schema = 1
    artifact = [IO.Path]::GetFileName($setup)
    sha256 = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    bytes = (Get-Item -LiteralPath $setup).Length
    payloadManifestSha256 = $manifestHash
    revision = $manifest.revision
    buildOrigin = $manifest.buildOrigin
    packagingOS = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    packagingTool = $version
    packagingToolSha256 = (Get-FileHash -LiteralPath $MakeNsis -Algorithm SHA256).Hash.ToLowerInvariant()
    scriptSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'setup.nsi')).Hash.ToLowerInvariant()
    unsigned = $true
    authenticodeCertificateTableBytes = 0
    windowsInstallationAndLaunch = 'BLOCKED: approval required on disposable Windows lab.'
    linuxPackaging = if ($IsLinux) { 'Executed natively; Windows validation still required.' } else { 'NOT PROVEN: this setup was assembled on Windows.' }
}
Write-ProofJson $receipt (Join-Path $OutputDirectory 'setup.json')
"$($receipt.sha256)  $($receipt.artifact)" |
    Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS') -Encoding utf8NoBOM
