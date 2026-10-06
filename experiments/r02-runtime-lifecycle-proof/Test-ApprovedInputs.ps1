[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$receipt = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence\preparation.json') -Raw | ConvertFrom-Json
$original = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\r02-dotnet-control-proof'))
[xml] $props = Get-Content -LiteralPath (Join-Path $PSScriptRoot '.candidate\Stage.props') -Raw
$stage = [string] $props.Project.PropertyGroup.Rt1Stage
foreach ($identity in $receipt.rt1SourceFiles)
{
    $originalFile = Join-Path $original $identity.file
    if ((Get-FileHash -LiteralPath $originalFile).Hash -ine $identity.sha256)
    {
        throw "Historical RT1 input changed: $($identity.file)"
    }
    if ($identity.file -notlike 'evidence\*' -and
        (Get-FileHash -LiteralPath (Join-Path $stage $identity.file)).Hash -ine $identity.sha256)
    {
        throw "Staged RT1 source changed: $($identity.file)"
    }
}
$native = @{
    'copilot-runtime.exe' = '7021cf1f25eb6b75e64c05e8f805747c62dd420dc8760659660291809e92603a'
    'runtime.node' = '41ebb48367f96c984babde61afb68f22a85ab8fd9c037fdccf1778881c4bba05'
}
foreach ($file in $native.Keys)
{
    if ((Get-FileHash -LiteralPath (Join-Path $stage ".candidate\runtime\$file")).Hash -ine $native[$file])
    {
        throw "Native RT1 input changed: $file"
    }
}
$rt2 = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'packages.lock.json') -Raw | ConvertFrom-Json -AsHashtable
$rt1 = Get-Content -LiteralPath (Join-Path $original 'packages.lock.json') -Raw | ConvertFrom-Json -AsHashtable
if (($rt2 | ConvertTo-Json -Depth 20 -Compress) -cne ($rt1 | ConvertTo-Json -Depth 20 -Compress))
{
    throw 'RT2 dependency closure differs from the approved RT1 closure.'
}
Write-Host 'Original RT1 source/evidence, staged source/native bytes and identical RT2 managed closure verified.'
