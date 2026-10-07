#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $AssemblyPath,
    [string] $ProjectPath = (Join-Path $PSScriptRoot '..\src\Kora.Application\Kora.Application.csproj')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Inspect raw PE resource bytes without loading application code or an interpreter.
[xml]$project = Get-Content -LiteralPath $ProjectPath -Raw
$expected = [Collections.Generic.Dictionary[string, byte[]]]::new([StringComparer]::Ordinal)
foreach ($item in $project.SelectNodes('//EmbeddedResource')) {
    $id = $item.GetAttribute('LogicalName')
    if (!$id.StartsWith('Kora.Skills.', [StringComparison]::Ordinal) -and
        !$id.StartsWith('Kora.Scripts.', [StringComparison]::Ordinal)) { continue }
    $path = Join-Path (Split-Path -Parent $ProjectPath) $item.GetAttribute('Include')
    $expected.Add($id, [IO.File]::ReadAllBytes($path))
}
if ($expected.Count -ne 13) { throw 'The fixed three-package resource closure must contain exactly 13 distinct resources.' }
$stream = [IO.File]::OpenRead((Resolve-Path -LiteralPath $AssemblyPath).Path)
$reader = [Reflection.PortableExecutable.PEReader]::new($stream)
try {
    $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($reader)
    $directory = $reader.PEHeaders.CorHeader.ResourcesDirectory
    [byte[]]$blob = $reader.GetSectionData($directory.RelativeVirtualAddress).GetContent(0, $directory.Size)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($handle in $metadata.ManifestResources) {
        $resource = $metadata.GetManifestResource($handle)
        $id = $metadata.GetString($resource.Name)
        if (!$id.StartsWith('Kora.Skills.', [StringComparison]::Ordinal) -and
            !$id.StartsWith('Kora.Scripts.', [StringComparison]::Ordinal)) { continue }
        if (!$resource.Implementation.IsNil -or !$seen.Add($id) -or !$expected.ContainsKey($id)) {
            throw "Missing, external, duplicate or unregistered embedded resource: $id"
        }
        $offset = [int]$resource.Offset
        if ($offset -lt 0 -or $offset + 4 -gt $blob.Length) { throw "Invalid resource offset: $id" }
        $length = [BitConverter]::ToInt32($blob, $offset)
        if ($length -le 0 -or $length -gt 65536 -or $offset + 4 + $length -gt $blob.Length) {
            throw "Invalid resource length: $id"
        }
        [byte[]]$actual = $blob[($offset + 4)..($offset + 3 + $length)]
        if ($actual.Length -ne $expected[$id].Length -or
            [Convert]::ToHexString($actual) -cne [Convert]::ToHexString($expected[$id])) {
            throw "Published bytes differ from the exact declared source: $id"
        }
        [pscustomobject]@{
            resourceId = $id
            bytes = $length
            sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($actual)).ToLowerInvariant()
            invocationAvailable = $false
        }
    }
    if (!$seen.SetEquals($expected.Keys)) { throw 'The final assembly is missing declared skill resources.' }
}
finally { $reader.Dispose(); $stream.Dispose() }
