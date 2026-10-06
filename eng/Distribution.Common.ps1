Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:KoraRepository = 'roryprimrose/Kora'
$script:KoraRepositoryUrl = 'https://github.com/roryprimrose/Kora'
$script:KoraSourceRepository = "$script:KoraRepositoryUrl.git"

function Get-SourceToolPaths {
    @('eng/Invoke-SourceBootstrap.ps1', 'eng/SourceBootstrap.Common.ps1',
        'eng/Test-SourceStage.ps1', 'eng/Get-BuildVersion.ps1',
        'eng/SourceCheckout.Common.ps1', 'eng/Distribution.Common.ps1',
        'eng/NativeInspection.Common.ps1', 'eng/Inspect-Publish.ps1')
}

function Invoke-Checked {
    param([string] $Command, [string[]] $Arguments)
    & $Command @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}

function Assert-NoLinks {
    param([string] $Path, [switch] $AncestorsOnly)
    $item = Get-Item -LiteralPath $Path -Force
    while ($null -ne $item) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Linked/reparse path is not admitted: $($item.FullName)"
        }
        $item = if ($item -is [IO.DirectoryInfo]) { $item.Parent } else { $item.Directory }
    }
    if (!$AncestorsOnly -and (Test-Path -LiteralPath $Path -PathType Container)) {
        foreach ($child in Get-ChildItem -LiteralPath $Path -Recurse -Force) {
            if ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Linked/reparse content is not admitted: $($child.FullName)"
            }
        }
    }
}

function New-ProofDirectory {
    param([string] $Path)
    if (Test-Path -LiteralPath $Path) { throw "Refusing to overwrite existing output: $Path" }
    $parent = Split-Path -Parent ([IO.Path]::GetFullPath($Path))
    $existing = $parent
    while (!(Test-Path -LiteralPath $existing)) { $existing = Split-Path -Parent $existing }
    Assert-NoLinks $existing -AncestorsOnly
    if (!(Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    Assert-NoLinks $parent -AncestorsOnly
    New-Item -ItemType Directory -Path $Path | Out-Null
}

function Write-ProofJson {
    param($Value, [string] $Path)
    $Value | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $Path -Encoding utf8NoBOM
}

function Get-PayloadFiles {
    param([string] $Path)
    Assert-NoLinks $Path
    @(
        Get-ChildItem -LiteralPath $Path -Recurse -File -Force |
            Sort-Object FullName |
            ForEach-Object {
                [ordered]@{
                    path = [IO.Path]::GetRelativePath($Path, $_.FullName).Replace('\', '/')
                    bytes = $_.Length
                    sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                }
            }
    )
}

function Assert-Payload {
    param([string] $Path, $Manifest)
    $actual = @(Get-PayloadFiles $Path)
    $expected = @($Manifest.files)
    if ($actual.Count -ne $expected.Count) { throw 'Payload file count changed.' }
    for ($index = 0; $index -lt $actual.Count; $index++) {
        if ($actual[$index].path -cne $expected[$index].path -or
            $actual[$index].bytes -ne $expected[$index].bytes -or
            $actual[$index].sha256 -cne $expected[$index].sha256) {
            throw "Payload identity changed: $($actual[$index].path)"
        }
    }
}
