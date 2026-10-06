[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PayloadPath,
    [Parameter(Mandatory)][string] $Version,
    [Parameter(Mandatory)][string] $SourceRevision,
    [switch] $WriteManifest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PayloadPath)
$manifestPath = Join-Path $root 'payload-manifest.json'
$files = @(Get-ChildItem -LiteralPath $root -Recurse -File -Force |
    Where-Object FullName -NE $manifestPath | ForEach-Object {
        [ordered] @{
            name = [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/')
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    } | Sort-Object { $_.name })
if ($files.Count -eq 0) { throw 'Application payload is empty.' }
if ($WriteManifest) {
    [ordered] @{ version = $Version; sourceRevision = $SourceRevision; files = $files } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
}
else {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.version -cne $Version -or $manifest.sourceRevision -cne $SourceRevision) {
        throw 'Application payload version or source revision does not match this build.'
    }
    if ($manifest.files.Count -ne $files.Count) { throw 'Application payload file count changed in transit.' }
    for ($index = 0; $index -lt $files.Count; $index++) {
        if ($manifest.files[$index].name -cne $files[$index].name -or
            $manifest.files[$index].sha256 -cne $files[$index].sha256) {
            throw "Application payload digest mismatch: $($files[$index].name)"
        }
    }
    Write-Host "Verified $($files.Count) exact payload files for $Version at $SourceRevision."
}
