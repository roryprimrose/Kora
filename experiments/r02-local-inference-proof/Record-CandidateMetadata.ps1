[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$OutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
if (Test-Path -LiteralPath $OutputPath) { throw 'Refusing to overwrite candidate evidence.' }
$setupPath = Join-Path $PSScriptRoot '..\..\src\Kora.Windows\Dependencies\WindowsOllamaSetupService.cs'
$setup = Get-Content -LiteralPath $setupPath -Raw
function Get-Pin([string] $Name) {
    $match = [regex]::Match($setup, "public const string $Name = `"([^`"]+)`";")
    if (!$match.Success) { throw "Could not read production pin $Name." }
    return $match.Groups[1].Value
}
$version = Get-Pin 'PackageVersion'
$model = Get-Pin 'Model'
$digest = Get-Pin 'ModelDigest'
$parts = $model.Split(':')
if ($parts.Length -ne 2 -or $parts[0] -notmatch '^[a-z0-9]+$' -or $parts[1] -notmatch '^[a-z0-9.]+$') {
    throw 'Unsupported model registry path.'
}
$handler = [Net.Http.HttpClientHandler]::new()
$handler.UseProxy = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(30)
try {
    $registry = "https://registry.ollama.ai/v2/library/$($parts[0])"
    $manifestUrl = "$registry/manifests/$($parts[1])"
    $bytes = $client.GetByteArrayAsync($manifestUrl).GetAwaiter().GetResult()
    $actual = 'sha256:' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
    if ($actual -ine $digest) { throw "Public model tag no longer matches production: $actual." }
    $manifest = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
    $licenseLayer = @($manifest.layers | Where-Object mediaType -EQ 'application/vnd.ollama.image.license')
    if ($licenseLayer.Count -ne 1) { throw 'Expected exactly one model licence layer.' }
    $licenseUrl = "$registry/blobs/$($licenseLayer[0].digest)"
    $licenseBytes = $client.GetByteArrayAsync($licenseUrl).GetAwaiter().GetResult()
    $licenseHash = 'sha256:' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($licenseBytes))
    if ($licenseHash -ine $licenseLayer[0].digest) { throw 'Model licence blob digest mismatch.' }
    $licenseText = [Text.Encoding]::UTF8.GetString($licenseBytes)
    if ($licenseText -notmatch 'Apache License' -or $licenseText -notmatch 'Version 2.0') {
        throw 'Model licence is not the expected Apache-2.0; review explicitly.'
    }
    $releaseUrl = "https://api.github.com/repos/ollama/ollama/releases/tags/v$version"
    $client.DefaultRequestHeaders.UserAgent.ParseAdd('Kora-R02-Public-Metadata-Proof')
    $release = $client.GetStringAsync($releaseUrl).GetAwaiter().GetResult() | ConvertFrom-Json
    $asset = @($release.assets | Where-Object name -EQ 'OllamaSetup.exe')
    if ($asset.Count -ne 1) { throw 'Pinned Windows setup asset not found.' }
    $runtimeLicenseUrl = "https://raw.githubusercontent.com/ollama/ollama/v$version/LICENSE"
    $runtimeLicenseBytes = $client.GetByteArrayAsync($runtimeLicenseUrl).GetAwaiter().GetResult()
    if ([Text.Encoding]::UTF8.GetString($runtimeLicenseBytes) -notmatch '^MIT License') {
        throw 'Runtime source licence changed; review explicitly.'
    }
    [long] $total = $manifest.config.size
    foreach ($layer in $manifest.layers) { $total += $layer.size }
    $report = [ordered]@{
        Schema = 'Kora.R02.PublicCandidateMetadata.v1'
        RetrievedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        Provenance = 'Public metadata only. No runtime installer or model weights downloaded; not an installed-runtime measurement.'
        Runtime = @{
            Version = $version
            ReleaseMetadataUrl = $releaseUrl
            InstallerUrl = $asset[0].browser_download_url
            InstallerDownloadBytes = $asset[0].size
            PublishedInstallerDigest = $asset[0].digest
            InstallerDigestLocallyVerified = $false
            SourceLicense = 'MIT'
            SourceLicenseUrl = $runtimeLicenseUrl
            SourceLicenseSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($runtimeLicenseBytes))
            InstalledExecutableDigest = $null
            ExpandedRuntimeStorageBytes = $null
            Limitation = 'Source MIT licence is not an audit of bundled Windows runtime/native dependency notices.'
        }
        Model = @{
            Name = $model
            ManifestUrl = $manifestUrl
            ManifestDigest = $actual
            ManifestBytes = $bytes.Length
            DownloadBlobBytes = $total
            License = 'Apache-2.0'
            LicenseBlobUrl = $licenseUrl
            LicenseBlobDigest = $licenseHash
            LicenseBlobBytes = $licenseBytes.Length
            AdvertisedContextTokens = 32768
            ContextSource = 'https://huggingface.co/Qwen/Qwen3-1.7B/blob/main/README.md'
            ContextMeasured = $false
            Manifest = $manifest
        }
        Storage = @{
            BootstrapModelFreeSpaceGuardBytes = 2000000000
            ModelBlobPlusManifestBytes = $total + $bytes.Length
            TotalInstallerAndModelDownloadBytes = $asset[0].size + $total
            Requirements = 'Expanded runtime, temporary installer/pull files, filesystem overhead, KV cache and RAM must be measured in an approved provisioned environment. Existing blobs may be reused.'
        }
    }
    $parent = Split-Path -Parent $OutputPath
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding utf8
}
finally {
    $client.Dispose()
}
