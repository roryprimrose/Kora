[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$receipts = foreach ($rid in @('win-x64', 'win-x86'))
{
    $path = Join-Path $root "artifacts\rt2-publish\Kora-$rid"
    $manifest = Get-Content -LiteralPath (Join-Path $path 'payload-manifest.json') -Raw | ConvertFrom-Json
    & (Join-Path $root 'eng\Test-InstallerPayload.ps1') -PayloadPath $path `
        -Version $manifest.version -SourceRevision $manifest.sourceRevision
    $machines = foreach ($name in @('Kora.exe', 'e_sqlite3.dll', 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll', 'av_libglesv2.dll'))
    {
        $file = Join-Path $path $name
        $stream = [IO.File]::OpenRead($file)
        $reader = [IO.BinaryReader]::new($stream)
        try
        {
            if ($reader.ReadUInt16() -ne 0x5a4d) { throw "Missing DOS header: $rid $name" }
            $stream.Position = 0x3c
            $offset = $reader.ReadUInt32()
            if ($offset -gt $stream.Length - 6) { throw "Invalid PE offset: $rid $name" }
            $stream.Position = $offset
            if ($reader.ReadUInt32() -ne 0x4550) { throw "Missing PE signature: $rid $name" }
            $machine = $reader.ReadUInt16()
            $expected = if ($rid -eq 'win-x64') { 0x8664 } else { 0x14c }
            if ($machine -ne $expected) { throw "Wrong PE architecture: $rid $name" }
            [ordered] @{ file = $name; peMachine = ('0x{0:x4}' -f $machine); sha256 = (Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant() }
        }
        finally { $reader.Dispose(); $stream.Dispose() }
    }
    if (@(Get-ChildItem -LiteralPath $path -File -Recurse | Where-Object Name -match 'Copilot|runtime.node|LifecycleProof|ControlProof').Count -ne 0)
    {
        throw 'Experimental candidate leaked into production publish.'
    }
    [ordered] @{
        rid = $rid; files = $manifest.files.Count
        payloadManifestSha256 = (Get-FileHash -LiteralPath (Join-Path $path 'payload-manifest.json')).Hash.ToLowerInvariant()
        version = $manifest.version; sourceRevision = $manifest.sourceRevision
        native = @($machines); experimentalRuntimeAbsent = $true
        licensesPresent = (Test-Path -LiteralPath (Join-Path $path 'licenses')) -and
            (Test-Path -LiteralPath (Join-Path $path 'package-notices')) -and
            (Test-Path -LiteralPath (Join-Path $path 'LICENSE')) -and
            (Test-Path -LiteralPath (Join-Path $path 'THIRD-PARTY-NOTICES.md'))
        launched = $false
    }
}
[ordered] @{ observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); status = 'PASS: inspection only'; payloads = @($receipts) } |
    ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 (Join-Path $PSScriptRoot 'evidence\publish-inspection.json')
