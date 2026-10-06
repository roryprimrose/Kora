[CmdletBinding()]
param([Parameter(Mandatory)][string] $VcVarsPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Windows x64 toolchain required.' }
$setup = [IO.Path]::GetFullPath($VcVarsPath)
if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) { throw 'Existing approved vcvars64.bat missing; no tool acquisition is performed.' }
$source = Join-Path $PSScriptRoot 'native\payload.c'
foreach ($variant in @('declared', 'undeclared')) {
    $output = Join-Path $PSScriptRoot "artifacts\$variant"
    if (-not (Test-Path -LiteralPath $output -PathType Container)) { throw 'Build managed payloads first.' }
    $define = if ($variant -eq 'undeclared') { '/DUNDECLARED' } else { '' }
    & $env:ComSpec /c "call `"$setup`" >nul && cd /d `"$output`" && cl /nologo /W4 /WX /LD /MT /Brepro $define `"$source`" /link /OUT:W2Native.dll /Brepro"
    if ($LASTEXITCODE -ne 0) { throw "Native $variant fixture compile failed." }
}
Write-Output 'Synthetic DLLs compiled only; native trials must still run. No installed/runtime/security-policy change.'
