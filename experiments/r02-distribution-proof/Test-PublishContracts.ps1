[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Payload,
    [Parameter(Mandatory)][string] $Inspection,
    [Parameter(Mandatory)][string] $MakeNsis,
    [Parameter(Mandatory)][string] $OutputDirectory
)
. (Join-Path $PSScriptRoot 'Common.ps1')
New-ProofDirectory $OutputDirectory
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$manifest = Get-Content -LiteralPath $Inspection -Raw | ConvertFrom-Json
Assert-Payload (Resolve-Path -LiteralPath $Payload).Path $manifest
$copy = Join-Path $OutputDirectory 'payload'
Copy-Item -LiteralPath $Payload -Destination $copy -Recurse
$passed = [Collections.Generic.List[string]]::new()
$runtime = $manifest.frameworks
if ($runtime.Count -ne 2 -or
    !($runtime.name -contains 'Microsoft.WindowsDesktop.App') -or
    !($runtime.name -contains 'Microsoft.NETCore.App')) { throw 'Runtime requirements changed.' }
$passed.Add('Actual runtimeconfig requires both x64 .NET 10 shared frameworks')
$imports = @($manifest.peFiles | Where-Object path -EQ 'onnxruntime.dll' | ForEach-Object imports)
foreach ($name in 'VCRUNTIME140.dll', 'VCRUNTIME140_1.dll', 'MSVCP140.dll', 'MSVCP140_1.dll') {
    if ($imports -notcontains $name) { throw "Expected observed ONNX Runtime import: $name" }
}
$passed.Add('Observed ONNX Runtime VC++ import requirements retained')
$file = Join-Path $copy 'Kora.runtimeconfig.json'
Move-Item -LiteralPath $file -Destination "$file.missing"
$caught = $false
try {
    & (Join-Path $PSScriptRoot 'Inspect-Publish.ps1') -Payload $copy -Revision $manifest.revision `
        -EvidenceDirectory (Join-Path $OutputDirectory 'missing-runtime') -BuildOrigin 'Synthetic negative fixture'
}
catch {
    if ($_.Exception.Message -notlike '*Missing launch-critical*') { throw }
    $caught = $true
}
if (!$caught) { throw 'Missing runtimeconfig was accepted.' }
Move-Item -LiteralPath "$file.missing" -Destination $file
$passed.Add('Missing runtimeconfig explicitly rejected')
$nativeFile = Join-Path $copy 'e_sqlite3.dll'
$bytes = [IO.File]::ReadAllBytes($nativeFile)
$coffOffset = [BitConverter]::ToInt32($bytes, 0x3c) + 4
$bytes[$coffOffset] = 0x4c
$bytes[$coffOffset + 1] = 0x01
[IO.File]::WriteAllBytes($nativeFile, $bytes)
$caught = $false
try {
    & (Join-Path $PSScriptRoot 'Inspect-Publish.ps1') -Payload $copy -Revision $manifest.revision `
        -EvidenceDirectory (Join-Path $OutputDirectory 'wrong-native') -BuildOrigin 'Synthetic negative fixture'
}
catch {
    if ($_.Exception.Message -notlike '*Wrong native architecture*') { throw }
    $caught = $true
}
if (!$caught) { throw 'Wrong native architecture was accepted.' }
$passed.Add('Wrong native PE architecture explicitly rejected')
$caught = $false
try {
    & (Join-Path $PSScriptRoot 'Build-Setup.ps1') -Payload $copy -Inspection $Inspection `
        -MakeNsis $MakeNsis -OutputDirectory (Join-Path $OutputDirectory 'must-not-exist')
}
catch {
    if ($_.Exception.Message -notlike '*Payload identity changed*') { throw }
    $caught = $true
}
if (!$caught -or (Test-Path -LiteralPath (Join-Path $OutputDirectory 'must-not-exist'))) {
    throw 'Tampered payload reached packaging.'
}
$passed.Add('Tampered final bytes rejected before compiler invocation')
Write-ProofJson ([ordered]@{ tests = @($passed); count = $passed.Count; staticOnly = $true }) `
    (Join-Path $OutputDirectory 'tests.json')
Write-Host "$($passed.Count) static publish/packaging contract checks passed; no app or installer executed."
