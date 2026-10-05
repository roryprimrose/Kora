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
foreach ($case in @(
    @{ licence = $true; notice = $true }
    @{ licence = $true; notice = $false }
    @{ licence = $false; notice = $true }
    @{ licence = $false; notice = $false }
)) {
    $expectedFiles = [ordered]@{ 'LICENSE' = $case.licence; 'THIRD-PARTY-NOTICES.md' = $case.notice }
    foreach ($path in $expectedFiles.Keys) {
        $target = Join-Path $copy $path
        if ($expectedFiles[$path]) {
            "Synthetic licensing-presence fixture for $path; not redistribution clearance." |
                Set-Content -LiteralPath $target -Encoding utf8NoBOM
        }
        elseif (Test-Path -LiteralPath $target) {
            Remove-Item -LiteralPath $target
        }
    }
    $evidence = Join-Path $OutputDirectory "licensing-$($case.licence)-$($case.notice)"
    & (Join-Path $PSScriptRoot 'Inspect-Publish.ps1') -Payload $copy -Revision $manifest.revision `
        -EvidenceDirectory $evidence -BuildOrigin 'Synthetic licensing-presence fixture'
    $observed = Get-Content -LiteralPath (Join-Path $evidence 'payload.json') -Raw | ConvertFrom-Json
    if ($observed.licensingEvidence.Count -ne 2 -or
        $observed.releaseAcceptance -notlike 'BLOCKED:*' -or
        $observed.releaseAcceptance -like '*no project licence*') {
        throw 'Licensing receipt shape or acceptance boundary is incorrect.'
    }
    foreach ($path in $expectedFiles.Keys) {
        $entry = @($observed.licensingEvidence | Where-Object path -CEQ $path)
        if ($entry.Count -ne 1 -or $entry[0].present -ne $expectedFiles[$path]) {
            throw "Incorrect observed licensing-file presence: $path"
        }
        $missingBlocker = "Missing distribution licensing file: $path"
        if (($observed.releaseBlockers -contains $missingBlocker) -eq $expectedFiles[$path]) {
            throw "Incorrect missing-file release blocker: $path"
        }
        if ($expectedFiles[$path]) {
            $target = Join-Path $copy $path
            if ($entry[0].sha256 -cne (Get-FileHash -LiteralPath $target).Hash.ToLowerInvariant() -or
                $entry[0].bytes -ne (Get-Item -LiteralPath $target).Length) {
                throw "Licensing-file identity was not recorded: $path"
            }
        }
        elseif ($null -ne $entry[0].sha256 -or $null -ne $entry[0].bytes) {
            throw "Absent licensing file acquired a fabricated identity: $path"
        }
    }
    foreach ($requiredBlocker in
        'Per-release redistribution clearance is not established by static inspection.',
        'Installed Windows protection and runtime-only acceptance require separate evidence.',
        'Bundled-resource and worker acceptance require separate evidence.') {
        if ($observed.releaseBlockers -notcontains $requiredBlocker) {
            throw "Static inspection cleared an unproved gate: $requiredBlocker"
        }
    }
    $passed.Add("Licensing receipt reflects licence=$($case.licence), notice=$($case.notice) without clearing release gates")
}
foreach ($path in 'LICENSE', 'THIRD-PARTY-NOTICES.md') {
    $original = Join-Path $Payload $path
    $target = Join-Path $copy $path
    if (Test-Path -LiteralPath $original -PathType Leaf) {
        Copy-Item -LiteralPath $original -Destination $target
    }
    elseif (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target
    }
}
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
exit 0
