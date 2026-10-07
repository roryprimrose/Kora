#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Payload,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string] $Revision,
    [Parameter(Mandatory)][string] $EvidenceDirectory,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string] $BuildOrigin,
    [ValidateSet('win-x64', 'win-x86')][string] $Rid = 'win-x64',
    [string] $PackageCache = $(if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
        Join-Path $HOME '.nuget\packages'
    } else { $env:NUGET_PACKAGES })
)
. (Join-Path $PSScriptRoot 'NativeInspection.Common.ps1')
$Payload = (Resolve-Path -LiteralPath $Payload).Path
Assert-NoLinks $Payload
$EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
if ($EvidenceDirectory.Equals($Payload, [StringComparison]::OrdinalIgnoreCase) -or
    $EvidenceDirectory.StartsWith($Payload.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Inspection evidence must be outside the immutable payload.'
}
New-ProofDirectory $EvidenceDirectory

foreach ($required in 'Kora.exe', 'Kora.dll', 'Kora.deps.json', 'Kora.runtimeconfig.json',
    'Kora.Application.dll', 'Kora.Core.dll', 'Kora.Definitions.dll', 'Kora.Tools.dll', 'Kora.Windows.dll', 'Microsoft.Data.Sqlite.dll',
    'SQLitePCLRaw.core.dll', 'SQLitePCLRaw.provider.e_sqlite3.dll', 'e_sqlite3.dll') {
    if (!(Test-Path -LiteralPath (Join-Path $Payload $required) -PathType Leaf)) {
        throw "Missing launch-critical file: $required"
    }
}
$runtime = Get-Content -LiteralPath (Join-Path $Payload 'Kora.runtimeconfig.json') -Raw | ConvertFrom-Json -AsHashtable
$deps = Get-Content -LiteralPath (Join-Path $Payload 'Kora.deps.json') -Raw | ConvertFrom-Json -AsHashtable
Assert-PublishRuntime $runtime $deps $Rid
if (Test-Path -LiteralPath (Join-Path $Payload 'coreclr.dll')) {
    throw 'Expected framework-dependent payload, not a bundled CLR.'
}
$frameworks = @($runtime.runtimeOptions.frameworks)
$peFiles = @(Get-PublishPeFiles $Payload $Rid)
$native = @(Get-PublishNativeAssets $Payload $deps $peFiles $Rid)
$sqlite = @($native | Where-Object published -CEQ 'e_sqlite3.dll')
if ($sqlite.Count -ne 1 -or $sqlite[0].package -cnotmatch '^SQLitePCLRaw\.lib\.e_sqlite3/\d+\.\d+\.\d+$') {
    throw 'Standard SQLite must be a pinned declared native payload, not an ambient setup dependency.'
}
$licences = @(
    foreach ($entry in $deps.libraries.GetEnumerator() | Sort-Object Key) {
        if ($entry.Value.type -cne 'package') { continue }
        $parts = $entry.Key -split '/'
        if ($parts.Count -ne 2 -or $parts[0] -notmatch '^[A-Za-z0-9_.-]+$' -or
            $parts[1] -notmatch '^[0-9][A-Za-z0-9_.+-]*$' -or [string]::IsNullOrWhiteSpace($entry.Value.sha512)) {
            throw "Invalid package identity/content hash: $($entry.Key)"
        }
        $packagePath = Join-Path $PackageCache ($parts[0].ToLowerInvariant() + '\' + $parts[1])
        $nuspecs = @(if (Test-Path -LiteralPath $packagePath -PathType Container) {
            Get-ChildItem -LiteralPath $packagePath -Filter '*.nuspec'
        })
        $licence = $null
        $licenceType = $null
        $licenceFileHash = $null
        $url = $null
        if ($nuspecs.Count -eq 1) {
            [xml]$xml = Get-Content -LiteralPath $nuspecs[0].FullName -Raw
            $node = $xml.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='license']")
            if ($null -ne $node) {
                $licence = $node.InnerText
                $licenceType = $node.GetAttribute('type')
                if ($licenceType -ceq 'file') {
                    $licencePath = [IO.Path]::GetFullPath((Join-Path $packagePath $licence))
                    if (!$licencePath.StartsWith([IO.Path]::GetFullPath($packagePath) + [IO.Path]::DirectorySeparatorChar,
                        [StringComparison]::OrdinalIgnoreCase)) { throw "Licence path escapes package: $($entry.Key)" }
                    if (Test-Path -LiteralPath $licencePath -PathType Leaf) {
                        Assert-NoLinks $licencePath -AncestorsOnly
                        $licenceFileHash = (Get-FileHash -LiteralPath $licencePath).Hash.ToLowerInvariant()
                    }
                }
            }
            $urlNode = $xml.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='licenseUrl']")
            if ($null -ne $urlNode) { $url = $urlNode.InnerText }
        }
        if ($nuspecs.Count -ne 1 -or [string]::IsNullOrWhiteSpace($licence)) {
            Write-Warning "Licence metadata incomplete for $($entry.Key); redistribution review remains blocked."
        }
        [ordered]@{
            package = $entry.Key; nugetContentHash = $entry.Value.sha512
            declaredLicence = $licence; licenceType = $licenceType; licenceFileSha256 = $licenceFileHash
            legacyLicenceUrl = $url; metadataAvailable = ($nuspecs.Count -eq 1)
            review = 'Not legal clearance; review bundled third-party native/model/text notices separately.'
        }
    }
)
$files = @(Get-PayloadFiles $Payload)
$releaseBlockers = [Collections.Generic.List[string]]::new()
$nativeCapabilityGaps = @(
    if (@($deps.libraries.Keys | Where-Object { $_ -like 'Microsoft.ML.OnnxRuntime*/*' }).Count -gt 0 -and
        @($native | Where-Object published -CEQ 'onnxruntime.dll').Count -eq 0) {
        "$Rid declares ONNX Runtime packages without an ONNX Runtime native payload; inference closure is not qualified."
    }
)
foreach ($gap in $nativeCapabilityGaps) { $releaseBlockers.Add($gap) }
$licensingEvidence = @(
    foreach ($path in 'LICENSE', 'THIRD-PARTY-NOTICES.md') {
        $file = $files | Where-Object path -CEQ $path | Select-Object -First 1
        if ($null -eq $file) { $releaseBlockers.Add("Missing distribution licensing file: $path") }
        [ordered]@{
            path = $path; present = ($null -ne $file)
            bytes = if ($null -ne $file) { $file.bytes } else { $null }
            sha256 = if ($null -ne $file) { $file.sha256 } else { $null }
        }
    }
)
$releaseBlockers.Add('Per-release redistribution clearance is not established by static inspection.')
$releaseBlockers.Add('Installed Windows protection and runtime-only acceptance require separate evidence.')
$releaseBlockers.Add('Bundled-resource and worker acceptance require separate evidence.')
$manifest = [ordered]@{
    schema = 2; inspectorVersion = $script:PublishInspectionVersion; inspectionProfile = $script:PublishInspectionProfile
    repository = 'https://github.com/roryprimrose/Kora'; revision = $Revision; buildOrigin = $BuildOrigin; rid = $Rid
    frameworks = $frameworks; files = $files; peFiles = $peFiles; nativeAssets = $native
    nativeImportReview = 'Static normal-import inventory only; delay loads and dynamic loading need real Windows trials.'
    licences = $licences; licensingEvidence = $licensingEvidence; nativeCapabilityGaps = $nativeCapabilityGaps
    releaseBlockers = @($releaseBlockers)
    releaseAcceptance = 'BLOCKED: ' + ($releaseBlockers -join ' ')
}
Write-ProofJson $manifest (Join-Path $EvidenceDirectory 'payload.json')
$manifest.files | ForEach-Object { "$($_.sha256)  $($_.path)" } |
    Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'SHA256SUMS') -Encoding utf8NoBOM
Write-Host "Inspected $($manifest.files.Count) files for $Rid; static evidence only, release acceptance remains blocked."
