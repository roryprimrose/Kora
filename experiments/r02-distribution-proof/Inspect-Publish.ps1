[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Payload,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string] $Revision,
    [Parameter(Mandatory)][string] $EvidenceDirectory,
    [Parameter(Mandatory)][string] $BuildOrigin,
    [string] $PackageCache = $(if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
        Join-Path $HOME '.nuget\packages'
    } else { $env:NUGET_PACKAGES })
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$Payload = (Resolve-Path -LiteralPath $Payload).Path
Assert-NoLinks $Payload
New-ProofDirectory $EvidenceDirectory

foreach ($required in 'Kora.exe', 'Kora.dll', 'Kora.deps.json', 'Kora.runtimeconfig.json',
    'Kora.Application.dll', 'Kora.Core.dll', 'Kora.Windows.dll') {
    if (!(Test-Path -LiteralPath (Join-Path $Payload $required) -PathType Leaf)) {
        throw "Missing launch-critical file: $required"
    }
}
$runtime = Get-Content -LiteralPath (Join-Path $Payload 'Kora.runtimeconfig.json') -Raw |
    ConvertFrom-Json -AsHashtable
$deps = Get-Content -LiteralPath (Join-Path $Payload 'Kora.deps.json') -Raw |
    ConvertFrom-Json -AsHashtable
if ($deps.runtimeTarget.name -notmatch '/win-x64$') { throw 'Expected win-x64 dependency target.' }
$frameworks = @($runtime.runtimeOptions.frameworks)
if ($frameworks.Count -ne 2 -or
    @($frameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' -and $_.version -eq '10.0.0' }).Count -ne 1 -or
    @($frameworks | Where-Object { $_.name -eq 'Microsoft.WindowsDesktop.App' -and $_.version -eq '10.0.0' }).Count -ne 1) {
    throw 'Runtime contract changed; review inspector and WiX prerequisite checks.'
}
if (Test-Path -LiteralPath (Join-Path $Payload 'coreclr.dll')) {
    throw 'Expected framework-dependent payload, not a bundled CLR.'
}

$peFiles = @(
    foreach ($file in Get-ChildItem -LiteralPath $Payload -Recurse -File |
        Where-Object Extension -In '.exe', '.dll') {
        $stream = [IO.File]::OpenRead($file.FullName)
        $reader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $resources = @()
            if ($reader.HasMetadata -and $file.Name -like 'Kora*.dll') {
                $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($reader)
                $resources = @(
                    foreach ($handle in $metadata.ManifestResources) {
                        $resource = $metadata.GetManifestResource($handle)
                        $metadata.GetString($resource.Name)
                    }
                )
            }
            if (!$reader.HasMetadata -and $reader.PEHeaders.CoffHeader.Machine.ToString() -ne 'Amd64') {
                throw "Wrong native architecture: $($file.Name)"
            }
            if ($file.Name -like 'Kora*' -and $reader.PEHeaders.PEHeader.CertificateTableDirectory.Size -ne 0) {
                throw "Expected unsigned first-party application bytes: $($file.Name)"
            }
            [ordered]@{
                path = [IO.Path]::GetRelativePath($Payload, $file.FullName).Replace('\', '/')
                machine = $reader.PEHeaders.CoffHeader.Machine.ToString()
                managed = $reader.HasMetadata
                authenticodeCertificateTableBytes = $reader.PEHeaders.PEHeader.CertificateTableDirectory.Size
                embeddedResources = $resources
                imports = @(Get-NativeImports $reader)
            }
        }
        finally { $reader.Dispose(); $stream.Dispose() }
    }
)
$native = @(
    foreach ($target in $deps.targets.Values) {
        foreach ($library in $target.GetEnumerator()) {
            if ($library.Value.ContainsKey('native')) {
                foreach ($asset in $library.Value.native.Keys) {
                    $leaf = ($asset -split '/')[-1]
                    if (!(Test-Path -LiteralPath (Join-Path $Payload $leaf))) {
                        throw "Declared native asset absent: $asset"
                    }
                    [ordered]@{ package = $library.Key; asset = $asset; published = $leaf }
                }
            }
        }
    }
)
$licences = @(
    foreach ($entry in $deps.libraries.GetEnumerator() | Sort-Object Key) {
        if ($entry.Value.type -ne 'package') { continue }
        $parts = $entry.Key -split '/'
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
                if ($licenceType -eq 'file') {
                    $licencePath = Join-Path $packagePath $licence
                    if (Test-Path -LiteralPath $licencePath -PathType Leaf) {
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
            package = $entry.Key
            nugetContentHash = $entry.Value.sha512
            declaredLicence = $licence
            licenceType = $licenceType
            licenceFileSha256 = $licenceFileHash
            legacyLicenceUrl = $url
            metadataAvailable = ($nuspecs.Count -eq 1)
            review = 'Not legal clearance; review bundled third-party native/model/text notices separately.'
        }
    }
)
$files = @(Get-PayloadFiles $Payload)
$releaseBlockers = [Collections.Generic.List[string]]::new()
$licensingEvidence = @(
    foreach ($path in 'LICENSE', 'THIRD-PARTY-NOTICES.md') {
        $file = $files | Where-Object path -CEQ $path | Select-Object -First 1
        if ($null -eq $file) {
            $releaseBlockers.Add("Missing distribution licensing file: $path")
        }
        [ordered]@{
            path = $path
            present = ($null -ne $file)
            bytes = if ($null -ne $file) { $file.bytes } else { $null }
            sha256 = if ($null -ne $file) { $file.sha256 } else { $null }
        }
    }
)
$releaseBlockers.Add('Per-release redistribution clearance is not established by static inspection.')
$releaseBlockers.Add('Installed Windows protection and runtime-only acceptance require separate evidence.')
$releaseBlockers.Add('Bundled-resource and worker acceptance require separate evidence.')
$manifest = [ordered]@{
    schema = 1
    repository = 'https://github.com/roryprimrose/Kora'
    revision = $Revision
    buildOrigin = $BuildOrigin
    rid = 'win-x64'
    frameworks = $frameworks
    files = $files
    peFiles = $peFiles
    nativeAssets = $native
    nativeImportReview = 'Static normal-import inventory only; delay loads and dynamic loading need real Windows trials.'
    licences = $licences
    licensingEvidence = $licensingEvidence
    releaseBlockers = @($releaseBlockers)
    releaseAcceptance = 'BLOCKED: ' + ($releaseBlockers -join ' ')
}
Write-ProofJson $manifest (Join-Path $EvidenceDirectory 'payload.json')
$manifest.files | ForEach-Object { "$($_.sha256)  $($_.path)" } |
    Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'SHA256SUMS') -Encoding utf8NoBOM
Write-Host "Inspected $($manifest.files.Count) files. Evidence is not release acceptance."
