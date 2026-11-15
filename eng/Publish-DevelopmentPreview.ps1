#requires -Version 7.5
[CmdletBinding()]
param(
    [string] $OutputDirectory,
    [string] $DotNet = 'dotnet',
    [string] $PackageSource
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = if ($OutputDirectory) { $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory) }
    else { Join-Path $repository ('artifacts\development-preview\run-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $output) { throw 'Use a fresh output directory; earlier preview bytes must be preserved.' }
$source = & git -C $repository rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify development-preview source.' }
$status = @(& git -C $repository status --porcelain)
if ($LASTEXITCODE -ne 0) { throw 'Cannot verify development-preview source status; no build performed.' }
$dirty = $status.Count -ne 0
$project = Join-Path $repository 'src\Kora\Kora.csproj'
$payload = Join-Path $output 'Kora-DevelopmentPreview'
$arguments = @('publish', $project, '--configuration', 'Release', '--runtime', 'win-x64',
    '--self-contained', 'true', '--no-restore', '-p:KoraDevelopmentPreview=true', '-p:DebugType=None', '--output', $payload)
& $DotNet @arguments | Tee-Object -Variable firstOutput
if ($LASTEXITCODE -ne 0) {
    if (($firstOutput -join "`n") -notmatch 'NETSDK1004|NETSDK1047|NETSDK1112') {
        throw 'Preview publish failed; partial output retained. No automatic restore or unrelated retry.'
    }
    Write-Host 'Required publish assets are missing. Restoring the exact locked win-x64 graph before one retry.'
    $restore = @('restore', $project, '--locked-mode', '--runtime', 'win-x64', '-p:KoraDevelopmentPreview=true')
    if ($PackageSource) { $restore += @('--source', $PackageSource) }
    & $DotNet @restore
    if ($LASTEXITCODE -ne 0) { throw 'Locked preview restore failed; no public-feed fallback or source rewrite is performed.' }
    & $DotNet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Preview publish failed after locked restore; partial output retained.' }
}
$instructions = @'
KORA DEVELOPMENT PREVIEW - NOT PRODUCTION QUALIFICATION

Open Kora.exe, then Settings > Set up development preview capabilities.
Select only the capabilities you want to test and approve their preview status.
Admission is memory-only, for at most one host hour. Restart needs fresh opt-in.

Local model: enter the exact name of a model you installed separately in Ollama.
Start that installed runtime yourself; Kora does not auto-start/download it.
Refresh service/model choices after preview review.

Hosted: Copilot keeps its installed-runtime evaluation review. Claude preview
permits native account setup; credentials and every physical content send retain
their own independent review. Account use may incur charges.

Wake: prepare the verified data through existing speech setup, select the native
wake profile and configured name Kora, and explicitly consent to listening.
Accuracy/playback qualification is incomplete. Use PTT/typed recovery.

Bundled fixed worker: prepare the exact protected PowerShell runtime through
existing setup first. AppContainer/network restrictions, content-bound grants,
exact action reviews and resource joins still apply. No arbitrary worker is
enabled. Missing runtime or containment fails explicitly, not as success.

No microphone, model call, authentication or action starts merely from opening
the app or preview setup. Known quality/latency failures can be tested here but
are not fabricated into passed production evidence.
'@
$instructions | Set-Content -LiteralPath (Join-Path $payload 'DEVELOPMENT-PREVIEW.txt') -Encoding utf8NoBOM
$input = [IO.File]::OpenRead((Join-Path $payload 'Kora.dll'))
$pe = [Reflection.PortableExecutable.PEReader]::new($input)
try {
    $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    $markers = 0
    foreach ($handle in $metadata.GetAssemblyDefinition().GetCustomAttributes()) {
        $attribute = $metadata.GetCustomAttribute($handle)
        if ($attribute.Constructor.Kind -ne [Reflection.Metadata.HandleKind]::MemberReference) { continue }
        $member = $metadata.GetMemberReference([Reflection.Metadata.MemberReferenceHandle]$attribute.Constructor)
        if ($member.Parent.Kind -ne [Reflection.Metadata.HandleKind]::TypeReference) { continue }
        $type = $metadata.GetTypeReference([Reflection.Metadata.TypeReferenceHandle]$member.Parent)
        if ($metadata.GetString($type.Name) -ne 'AssemblyMetadataAttribute' -or
            $metadata.GetString($type.Namespace) -ne 'System.Reflection') { continue }
        $blob = $metadata.GetBlobReader($attribute.Value)
        if ($blob.ReadUInt16() -ne 1) { throw 'Invalid assembly-metadata encoding.' }
        $key = $blob.ReadSerializedString()
        $value = $blob.ReadSerializedString()
        if ($key -eq 'KoraBuildProfile' -and $value -eq 'development-preview') { $markers++ }
    }
    if ($markers -ne 1) { throw 'Published payload lacks the exact Development Preview marker.' }
}
finally { $pe.Dispose(); $input.Dispose() }
$manifest = [ordered]@{
    Format = 'kora-development-preview-v1'
    Source = $source.Trim()
    DirtySource = $dirty
    ProductionQualified = $false
    AutomaticCapabilityOptIn = $false
    Files = @(Get-ChildItem -LiteralPath $payload -File -Recurse | ForEach-Object {
        [ordered]@{ Path = [IO.Path]::GetRelativePath($payload, $_.FullName); Bytes = $_.Length
            Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'preview-manifest.json') -Encoding utf8NoBOM
$zip = Join-Path $output 'Kora-DevelopmentPreview-win-x64.zip'
Compress-Archive -LiteralPath $payload -DestinationPath $zip
Write-Host "Development Preview published: $zip"
Write-Host 'This is not an installed or production-qualified release. No app, microphone, model or worker was started.'
