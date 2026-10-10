[CmdletBinding()]
param([string] $EvidencePath = (Join-Path $PSScriptRoot '.candidate\offline-invariants.json'))

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PackageArtifacts.ps1')
$checks = [System.Collections.Generic.List[object]]::new()

function Test-Invariant([string] $Id, [scriptblock] $Check)
{
    try
    {
        & $Check
        $checks.Add([ordered] @{ id = $Id; status = 'PASS' })
    }
    catch
    {
        # Only fixed IDs/statuses are emitted: exception text can contain machine paths.
        $checks.Add([ordered] @{ id = $Id; status = 'FAIL' })
    }
}

function Assert-Equal($Actual, $Expected)
{
    if ($Actual -cne $Expected) { throw 'Invariant differs.' }
}

$cache = Join-Path $PSScriptRoot '.candidate'
$reviewed = Get-Content (Join-Path $PSScriptRoot 'evidence\disposition.json') -Raw | ConvertFrom-Json
Test-Invariant 'reviewed-receipt-agrees-with-runtime-gate' {
    $candidate = Get-Content (Join-Path $PSScriptRoot 'Candidate.cs') -Raw
    if (-not $candidate.Contains('"' + $reviewed.sdkPackageSha256 + '"') -or
        -not $candidate.Contains('"' + $reviewed.sdkAssemblySha256 + '"'))
    {
        throw 'Historical review and runtime gate disagree.'
    }
}
Test-Invariant 'reviewed-source-archive' {
    Assert-Equal (Get-FileHash (Join-Path $cache 'sdk-source.zip')).Hash.ToLowerInvariant() `
        'a73f4f2d8cb1eb9fd5353d3867707094e1efb7581599669afb55c2dcf48a53c3'
}
Test-Invariant 'reviewed-runtime-archive' {
    Assert-Equal (Get-FileHash (Join-Path $cache 'runtime.tgz')).Hash.ToLowerInvariant() `
        '2b2c23816461f7616eb16c7836af7701d6683367029405d51054c4366d1d41c0'
}
Test-Invariant 'reviewed-native-launcher' {
    Assert-Equal (Get-FileHash (Join-Path $cache 'runtime\copilot-runtime.exe')).Hash.ToLowerInvariant() `
        '7021cf1f25eb6b75e64c05e8f805747c62dd420dc8760659660291809e92603a'
}
Test-Invariant 'reviewed-native-payload' {
    Assert-Equal (Get-FileHash (Join-Path $cache 'runtime\runtime.node')).Hash.ToLowerInvariant() `
        '41ebb48367f96c984babde61afb68f22a85ab8fd9c037fdccf1778881c4bba05'
}
Test-Invariant 'minimal-runtime-assets-only' {
    $assets = @(Get-ChildItem (Join-Path $cache 'runtime') -Force | Sort-Object Name | ForEach-Object Name)
    Assert-Equal ($assets -join ',') 'copilot-runtime.exe,LICENSE.md,runtime.node'
}
Test-Invariant 'extracted-sdk-source-exact-archive-bytes' {
    $source = Join-Path $cache "source\copilot-sdk-$($reviewed.sourceCommit)"
    $archive = [System.IO.Compression.ZipFile]::OpenRead((Join-Path $cache 'sdk-source.zip'))
    $files = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    try
    {
        foreach ($entry in $archive.Entries | Where-Object Name -ne '')
        {
            $relative = $entry.FullName.Substring($entry.FullName.IndexOf('/') + 1).Replace('/', '\')
            $file = Join-Path $source $relative
            $files.Add([System.IO.Path]::GetFullPath($file)) | Out-Null
            $stream = $entry.Open()
            try { $hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            Assert-Equal (Get-FileHash -LiteralPath $file).Hash $hash
        }
        foreach ($file in Get-ChildItem (Join-Path $source 'dotnet\src') -Recurse -Filter '*.cs' -File |
            Where-Object FullName -NotMatch '\\(obj|bin)\\')
        {
            if (-not $files.Contains($file.FullName)) { throw 'Extra compile input.' }
        }
    }
    finally { $archive.Dispose() }
}
Test-Invariant 'seven-source-build-locked-restore-content-identities' {
    $lock = Get-Content (Join-Path $PSScriptRoot 'evidence\sdk-build.lock.json') -Raw | ConvertFrom-Json
    $dependencies = @($lock.dependencies.'net10.0'.PSObject.Properties)
    Assert-Equal $dependencies.Count 7
    foreach ($dependency in $dependencies)
    {
        $id = $dependency.Name.ToLowerInvariant()
        $version = $dependency.Value.resolved
        # NuGet's lock content identity is distinct from the raw signed ZIP's SHA-512.
        $hash = (Get-Content (Join-Path $cache "packages\$id\$version\$id.$version.nupkg.sha512") -Raw).Trim()
        $metadata = Get-Content (Join-Path $cache "packages\$id\$version\.nupkg.metadata") -Raw | ConvertFrom-Json
        $stream = [System.IO.File]::OpenRead((Join-Path $cache "packages\$id\$version\$id.$version.nupkg"))
        try { $archiveHash = [Convert]::ToBase64String([System.Security.Cryptography.SHA512]::HashData($stream)) }
        finally { $stream.Dispose() }
        Assert-Equal $archiveHash $hash
        Assert-Equal $metadata.contentHash $dependency.Value.contentHash
    }
}
Test-Invariant 'reviewed-identities-accepted-by-comparison' {
    Assert-Equal (Get-ReviewedSourceBuildComparison $reviewed.sdkPackageSha256 $reviewed.sdkAssemblySha256).profileMatchesReviewed $true
}
Test-Invariant 'changed-package-rejected-by-comparison' {
    Assert-Equal (Get-ReviewedSourceBuildComparison ('0' * 64) $reviewed.sdkAssemblySha256).profileMatchesReviewed $false
}
Test-Invariant 'changed-assembly-rejected-by-comparison' {
    Assert-Equal (Get-ReviewedSourceBuildComparison $reviewed.sdkPackageSha256 ('0' * 64)).profileMatchesReviewed $false
}
Test-Invariant 'malformed-identity-fails-closed' {
    $rejected = $false
    try { Get-ReviewedSourceBuildComparison 'not-a-sha256' $reviewed.sdkAssemblySha256 | Out-Null }
    catch { $rejected = $true }
    Assert-Equal $rejected $true
}
Test-Invariant 'current-build-comparison-requires-both-reviewed-identities' {
    $package = Join-Path $cache 'feed\GitHub.Copilot.SDK.1.0.16-rt1.source.f8ae645.1.nupkg'
    $assembly = Join-Path $cache "source\copilot-sdk-$($reviewed.sourceCommit)\dotnet\src\bin\Release\net10.0\GitHub.Copilot.SDK.dll"
    $packageHash = (Get-FileHash $package).Hash.ToLowerInvariant()
    $assemblyHash = (Get-FileHash $assembly).Hash.ToLowerInvariant()
    $comparison = Get-ReviewedSourceBuildComparison $packageHash $assemblyHash
    Assert-Equal $comparison.profileMatchesReviewed (($packageHash -ceq $reviewed.sdkPackageSha256) -and
        ($assemblyHash -ceq $reviewed.sdkAssemblySha256))
}
Test-Invariant 'historical-node-witness-unchanged' {
    Assert-Equal (Get-FileHash (Join-Path $PSScriptRoot '..\r02-runtime-proof\evidence\results.json')).Hash.ToLowerInvariant() `
        'c1a40817bf37a125c08abe1eaa1dd31f5ec04fb959cc8ce3890cbfe95b9f0331'
}
Test-Invariant 'all-45-source-cases-retained-not-executed' {
    $source = Get-Content (Join-Path $PSScriptRoot 'ConformanceTests.cs') -Raw
    Assert-Equal ([regex]::Matches($source, '\[Fact\]|\[InlineData\(').Count) 45
}
Test-Invariant 'powershell-entry-points-parse' {
    foreach ($file in Get-ChildItem $PSScriptRoot -Filter '*.ps1' -File)
    {
        $tokens = $null
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref] $tokens, [ref] $errors) | Out-Null
        Assert-Equal $errors.Count 0
    }
}

$trial = Join-Path $cache ('offline-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $trial | Out-Null
try
{
    $package = Join-Path $trial 'fixture.nupkg'
    $archive = [System.IO.Compression.ZipFile]::Open($package, [System.IO.Compression.ZipArchiveMode]::Create)
    try
    {
        $entry = $archive.CreateEntry('synthetic.txt')
        $entry.LastWriteTime = [DateTimeOffset]::new(2026, 10, 9, 0, 0, 0, [TimeSpan]::Zero)
        $writer = [System.IO.StreamWriter]::new($entry.Open())
        try { $writer.Write('RT1_SYNTHETIC_PACKAGE_BYTES') }
        finally { $writer.Dispose() }
    }
    finally { $archive.Dispose() }
    Set-DeterministicPackageTimestamp $package
    Test-Invariant 'timestamp-normalization-preserves-entry-bytes' {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($package)
        try
        {
            $reader = [System.IO.StreamReader]::new($archive.GetEntry('synthetic.txt').Open())
            try { Assert-Equal $reader.ReadToEnd() 'RT1_SYNTHETIC_PACKAGE_BYTES' }
            finally { $reader.Dispose() }
            Assert-Equal $archive.GetEntry('synthetic.txt').LastWriteTime.Year 1980
        }
        finally { $archive.Dispose() }
    }
    Test-Invariant 'timestamp-normalization-byte-idempotence' {
        $before = (Get-FileHash $package).Hash
        Set-DeterministicPackageTimestamp $package
        Assert-Equal (Get-FileHash $package).Hash $before
    }
}
finally { Remove-Item -LiteralPath $trial -Recurse -Force }
Test-Invariant 'owned-offline-fixture-cleaned' { Assert-Equal (Test-Path -LiteralPath $trial) $false }

$failed = @($checks | Where-Object status -eq 'FAIL').Count
[ordered] @{
    observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    scope = 'Offline artifact and script invariants only; no SDK conformance or runtime launch'
    total = $checks.Count
    passed = $checks.Count - $failed
    failed = $failed
    checks = $checks
} | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 $EvidencePath
Write-Host "Offline invariants: $($checks.Count - $failed)/$($checks.Count); RT1 conformance remains separately gated."
if ($failed -ne 0) { throw 'Offline invariant failure; inspect the fixed-ID receipt.' }
