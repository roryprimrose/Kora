. (Join-Path $PSScriptRoot 'Distribution.Common.ps1')

$script:PublishInspectionVersion = '1.0.0'
$script:PublishInspectionProfile = 'windows-framework-dependent-v1'

function Get-NativeImports {
    param([System.Reflection.PortableExecutable.PEReader] $Reader)
    $rva = $Reader.PEHeaders.PEHeader.ImportTableDirectory.RelativeVirtualAddress
    if ($rva -eq 0) { return @() }
    [byte[]]$descriptors = $Reader.GetSectionData($rva).GetContent()
    $terminated = $false
    $imports = @(
        for ($offset = 0; $offset + 20 -le $descriptors.Length; $offset += 20) {
            $nameRva = [BitConverter]::ToInt32($descriptors, $offset + 12)
            if ($nameRva -eq 0) { $terminated = $true; break }
            [byte[]]$name = $Reader.GetSectionData($nameRva).GetContent()
            $length = 0
            while ($length -lt $name.Length -and $name[$length] -ne 0) { $length++ }
            if ($length -eq 0 -or $length -eq $name.Length) { throw 'Malformed native import name.' }
            [Text.Encoding]::ASCII.GetString($name, 0, $length)
        }
    )
    if (!$terminated) { throw 'Malformed native import table: missing terminator.' }
    @($imports | Sort-Object -Unique)
}

function Get-PublishPeFiles {
    param([string] $Payload, [ValidateSet('win-x64', 'win-x86')][string] $Rid)
    $machine = if ($Rid -ceq 'win-x64') { 'Amd64' } else { 'I386' }
    @(
        foreach ($file in Get-ChildItem -LiteralPath $Payload -Recurse -File -Force |
            Where-Object Extension -In '.exe', '.dll' | Sort-Object FullName) {
            $stream = [IO.File]::OpenRead($file.FullName)
            $reader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
            try {
                if ($null -eq $reader.PEHeaders.PEHeader) { throw "Missing PE header: $($file.Name)" }
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
                if (!$reader.HasMetadata -and $reader.PEHeaders.CoffHeader.Machine.ToString() -cne $machine) {
                    throw "Wrong native architecture: $($file.Name); expected $machine for $Rid."
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
}

function Assert-PublishRuntime {
    param($Runtime, $Dependencies, [ValidateSet('win-x64', 'win-x86')][string] $Rid)
    $target = $Dependencies.runtimeTarget.name
    if ($target -cnotmatch ("/" + [regex]::Escape($Rid) + '$') -or !$Dependencies.targets.Contains($target)) {
        throw "Expected $Rid dependency target."
    }
    $frameworks = @($Runtime.runtimeOptions.frameworks)
    if ($frameworks.Count -ne 2 -or
        @($frameworks | Where-Object { $_.name -ceq 'Microsoft.NETCore.App' -and $_.version -ceq '10.0.0' }).Count -ne 1 -or
        @($frameworks | Where-Object { $_.name -ceq 'Microsoft.WindowsDesktop.App' -and $_.version -ceq '10.0.0' }).Count -ne 1) {
        throw 'Runtime contract changed; review inspector and WiX prerequisite checks.'
    }
}

function Get-PublishNativeAssets {
    param([string] $Payload, $Dependencies, $PeFiles, [ValidateSet('win-x64', 'win-x86')][string] $Rid)
    @(
        foreach ($library in $Dependencies.targets[$Dependencies.runtimeTarget.name].GetEnumerator() | Sort-Object Key) {
            if (!$library.Value.Contains('native')) { continue }
            foreach ($asset in $library.Value.native.Keys | Sort-Object) {
                if ($asset -cnotmatch ('^runtimes/' + [regex]::Escape($Rid) + '/native/[^/\\]+$') -or
                    ($asset -split '/')[-1] -in '.', '..') { throw "Declared native asset RID/path mismatch: $asset" }
                $leaf = ($asset -split '/')[-1]
                $file = @($PeFiles | Where-Object path -CEQ $leaf)
                $extension = [IO.Path]::GetExtension($leaf)
                $isPe = $extension -in '.dll', '.exe'
                if (!(Test-Path -LiteralPath (Join-Path $Payload $leaf) -PathType Leaf) -or
                    ($isPe -and ($file.Count -ne 1 -or $file[0].managed))) {
                    throw "Declared native asset absent or not a native PE: $asset"
                }
                if (!$isPe -and $extension -notin '.pdb', '.lib') { throw "Unreviewed native asset kind: $asset" }
                [ordered]@{
                    package = $library.Key; asset = $asset; published = $leaf
                    kind = if ($isPe) { 'native-pe' } else { 'symbols-or-import-library' }
                }
            }
        }
    )
}
