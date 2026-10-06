[CmdletBinding()]
param([string] $ApprovedPackagePath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$rt1 = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\r02-dotnet-control-proof'))
$expectedPackage = 'c5518980b71d0ef0abd39ecdec7834898878222290c7fd1099a9040c8c5bf2ef'
$expectedAssembly = '6ed0b19fd2f9cf525074830784bb255245f15b8f08a3d6aa78fb116be5ba668b'
$package = Join-Path $PSScriptRoot '.inputs\GitHub.Copilot.SDK.1.0.16.nupkg'
if ($ApprovedPackagePath) {
    if ((Get-FileHash -LiteralPath $ApprovedPackagePath).Hash -ine $expectedPackage) {
        throw 'Supplied SDK package does not match separate released-profile approval.'
    }
    Copy-Item -LiteralPath $ApprovedPackagePath -Destination $package
}
$checks = @(
    @{ Name = 'source-archive'; Path = (Join-Path $PSScriptRoot '.inputs\sdk-source.zip'); Expected = 'a73f4f2d8cb1eb9fd5353d3867707094e1efb7581599669afb55c2dcf48a53c3' },
    @{ Name = 'runtime-archive'; Path = (Join-Path $PSScriptRoot '.inputs\runtime.tgz'); Expected = '2b2c23816461f7616eb16c7836af7701d6683367029405d51054c4366d1d41c0' },
    @{ Name = 'sdk-package'; Path = $package; Expected = $expectedPackage },
    @{ Name = 'native-launcher'; Path = (Join-Path $PSScriptRoot '.inputs\runtime\copilot-runtime.exe'); Expected = '7021cf1f25eb6b75e64c05e8f805747c62dd420dc8760659660291809e92603a' },
    @{ Name = 'native-payload'; Path = (Join-Path $PSScriptRoot '.inputs\runtime\runtime.node'); Expected = '41ebb48367f96c984babde61afb68f22a85ab8fd9c037fdccf1778881c4bba05' },
    @{ Name = 'historical-node-witness'; Path = (Join-Path $rt1 '..\r02-runtime-proof\evidence\results.json'); Expected = 'c1a40817bf37a125c08abe1eaa1dd31f5ec04fb959cc8ce3890cbfe95b9f0331' }
)
$rows = @($checks | ForEach-Object {
    $actual = if (Test-Path -LiteralPath $_.Path) { (Get-FileHash -LiteralPath $_.Path).Hash.ToLowerInvariant() } else { 'missing' }
    [ordered] @{ name = $_.Name; expectedSha256 = $_.Expected; actualSha256 = $actual; status = $(if ($actual -ceq $_.Expected) {'Pass'} else {'Blocked'}) }
})
if ($rows[2].status -eq 'Pass') {
    $zip = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $entry = $zip.GetEntry('lib/net10.0/GitHub.Copilot.SDK.dll')
        if ($null -eq $entry) { throw 'Approved package has no net10 SDK assembly.' }
        $stream = $entry.Open()
        try { $actualAssembly = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        $rows += [ordered] @{ name = 'sdk-package-assembly'; expectedSha256 = $expectedAssembly; actualSha256 = $actualAssembly; status = $(if ($actualAssembly -ceq $expectedAssembly) {'Pass'} else {'Blocked'}) }
    }
    finally { $zip.Dispose() }
}
$failed = @($rows | Where-Object status -NE 'Pass')
$report = [ordered] @{
    observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    baseRevision = (& git -C $PSScriptRoot rev-parse HEAD)
    sdkToolchain = (& dotnet --version)
    sdkSourceCommit = 'f8ae645902b74b62cd47aac1fd9b29adaec3aff2'
    sdkVersion = '1.0.16'
    expectedAssemblySha256 = $expectedAssembly
    status = $(if ($failed.Count -eq 0) { 'Pass' } else { 'Blocked' })
    checks = $rows
    runtimeTrialsStarted = 0
    profile = 'Released NuGet 1.0.16; separately approved and RT1 regressions required; not source-built byte equivalence'
}
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'evidence') -Force | Out-Null
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence\input-verification.json') -Encoding utf8
if ($failed.Count -ne 0) { throw 'MG1 exact-profile inputs blocked; refusing different bytes before runtime startup.' }
Write-Host 'MG1 separately approved released SDK and unchanged RT1 native hashes verified.'
