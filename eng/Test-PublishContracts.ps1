#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Payload,
    [Parameter(Mandatory)][string] $Inspection,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [ValidateSet('win-x64', 'win-x86')][string] $Rid = 'win-x64'
)
. (Join-Path $PSScriptRoot 'NativeInspection.Common.ps1')
New-ProofDirectory $OutputDirectory
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$manifest = Get-Content -LiteralPath $Inspection -Raw | ConvertFrom-Json -AsHashtable
Assert-Payload (Resolve-Path -LiteralPath $Payload).Path $manifest
$copy = Join-Path $OutputDirectory 'payload'
Copy-Item -LiteralPath $Payload -Destination $copy -Recurse
$passed = [Collections.Generic.List[string]]::new()
function Expect-PublishFailure {
    param([string] $Name, [scriptblock] $Action, [string] $Message)
    $caught = $false
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -notlike "*$Message*") { throw }
        $caught = $true
    }
    if (!$caught) { throw "Expected rejection: $Name" }
    $passed.Add($Name)
}
function Inspect-Fixture {
    param([string] $Name)
    & (Join-Path $PSScriptRoot 'Inspect-Publish.ps1') -Payload $copy -Revision $manifest.revision `
        -Rid $Rid -EvidenceDirectory (Join-Path $OutputDirectory $Name) -BuildOrigin 'Owned static contract fixture; never loaded'
}
$expectedMachine = if ($Rid -ceq 'win-x64') { 'Amd64' } else { 'I386' }
if ($manifest.schema -ne 2 -or $manifest.inspectorVersion -cne $script:PublishInspectionVersion -or
    $manifest.inspectionProfile -cne $script:PublishInspectionProfile -or $manifest.rid -cne $Rid -or
    $manifest.repository -cne 'https://github.com/roryprimrose/Kora' -or
    $manifest.releaseAcceptance -notlike 'BLOCKED:*') { throw 'Maintained inspection identity/shape mismatch.' }
$native = @($manifest.peFiles | Where-Object { !$_.managed })
if ($native.Count -eq 0 -or @($native | Where-Object machine -CNE $expectedMachine).Count) {
    throw 'Actual native closure is absent or has wrong RID architecture.'
}
$resources = @($manifest.peFiles | Where-Object path -CEQ 'Kora.dll' | ForEach-Object embeddedResources)
if ($resources -notcontains '!AvaloniaResources') { throw 'Missing actual embedded Avalonia resource inventory.' }
$sums = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $Inspection) 'SHA256SUMS')
$expectedSums = @($manifest.files | ForEach-Object { "$($_.sha256)  $($_.path)" })
if (($sums -join '|') -cne ($expectedSums -join '|')) { throw 'SHA256SUMS does not match full receipt shape.' }
Assert-PublishRuntime (@{ runtimeOptions = @{ frameworks = $manifest.frameworks } }) `
    (Get-Content -LiteralPath (Join-Path $copy 'Kora.deps.json') -Raw | ConvertFrom-Json -AsHashtable) $Rid
$passed.Add("Actual schema-2 receipt, SHA256SUMS, $Rid native closure, resources and both .NET 10 shared frameworks")
if ($Rid -ceq 'win-x64') {
    $imports = @($manifest.peFiles | Where-Object path -CEQ 'onnxruntime.dll' | ForEach-Object imports)
    foreach ($name in 'VCRUNTIME140.dll', 'MSVCP140.dll', 'VCRUNTIME140_1.dll', 'MSVCP140_1.dll') {
        if ($imports -notcontains $name) { throw "Expected observed ONNX Runtime import: $name" }
    }
    $passed.Add('Observed x64 ONNX Runtime VC++ normal imports retained; not runtime loading acceptance')
}
else {
    if (@($manifest.peFiles | Where-Object path -CEQ 'onnxruntime.dll').Count -ne 0 -or
        $manifest.nativeCapabilityGaps -notcontains
        'win-x86 declares ONNX Runtime packages without an ONNX Runtime native payload; inference closure is not qualified.') {
        throw 'x86 ONNX native capability disposition changed; review this baseline explicitly.'
    }
    $passed.Add('Actual x86 ONNX native absence is explicit and blocked, not inferred x86 inference/runtime acceptance')
}
foreach ($case in @(
    @{ licence = $true; notice = $true }, @{ licence = $true; notice = $false },
    @{ licence = $false; notice = $true }, @{ licence = $false; notice = $false }
)) {
    $expectedFiles = [ordered]@{ 'LICENSE' = $case.licence; 'THIRD-PARTY-NOTICES.md' = $case.notice }
    foreach ($path in $expectedFiles.Keys) {
        $target = Join-Path $copy $path
        if ($expectedFiles[$path]) {
            "Synthetic licensing-presence fixture for $path; not redistribution clearance." |
                Set-Content -LiteralPath $target -Encoding utf8NoBOM
        }
        elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target }
    }
    $name = "licensing-$($case.licence)-$($case.notice)"
    Inspect-Fixture $name
    $observed = Get-Content -LiteralPath (Join-Path $OutputDirectory "$name\payload.json") -Raw | ConvertFrom-Json -AsHashtable
    if ($observed.licensingEvidence.Count -ne 2 -or $observed.releaseAcceptance -notlike 'BLOCKED:*') {
        throw 'Licensing receipt shape or acceptance boundary is incorrect.'
    }
    foreach ($path in $expectedFiles.Keys) {
        $entry = @($observed.licensingEvidence | Where-Object path -CEQ $path)
        if ($entry.Count -ne 1 -or $entry[0].present -ne $expectedFiles[$path] -or
            (($observed.releaseBlockers -contains "Missing distribution licensing file: $path") -eq $expectedFiles[$path])) {
            throw "Incorrect licensing-file presence/blocker: $path"
        }
        if ($expectedFiles[$path]) {
            if ($entry[0].sha256 -cne (Get-FileHash -LiteralPath (Join-Path $copy $path)).Hash.ToLowerInvariant() -or
                $entry[0].bytes -ne (Get-Item -LiteralPath (Join-Path $copy $path)).Length) {
                throw "Licensing-file identity was not recorded: $path"
            }
        }
        elseif ($null -ne $entry[0].sha256 -or $null -ne $entry[0].bytes) {
            throw "Absent licensing file acquired a fabricated identity: $path"
        }
    }
    foreach ($blocker in 'Per-release redistribution clearance is not established by static inspection.',
        'Installed Windows protection and runtime-only acceptance require separate evidence.',
        'Bundled-resource and worker acceptance require separate evidence.') {
        if ($observed.releaseBlockers -notcontains $blocker) { throw "Static inspection cleared an unproved gate: $blocker" }
    }
    $passed.Add("Licensing receipt reflects licence=$($case.licence), notice=$($case.notice) with exact/null identities and blocked gates")
}
foreach ($path in 'LICENSE', 'THIRD-PARTY-NOTICES.md') {
    Copy-Item -LiteralPath (Join-Path $Payload $path) -Destination (Join-Path $copy $path)
}
Expect-PublishFailure 'Existing inspection evidence is never overwritten' { Inspect-Fixture 'licensing-True-True' } 'overwrite existing'
Expect-PublishFailure 'Inspection cannot write into or change its immutable payload' {
    & (Join-Path $PSScriptRoot 'Inspect-Publish.ps1') -Payload $copy -Revision $manifest.revision `
        -Rid $Rid -EvidenceDirectory (Join-Path $copy 'disallowed-inspection') -BuildOrigin 'Negative fixture'
} 'outside the immutable payload'
Expect-PublishFailure 'Unapproved RID is refused' {
    & (Join-Path $PSScriptRoot 'Inspect-Publish.ps1') -Payload $copy -Revision $manifest.revision `
        -Rid win-arm64 -EvidenceDirectory (Join-Path $OutputDirectory 'arm64') -BuildOrigin 'Negative fixture'
} 'ValidateSet'
foreach ($path in 'Kora.runtimeconfig.json', 'Kora.deps.json', 'Microsoft.Data.Sqlite.dll',
    'SQLitePCLRaw.core.dll', 'SQLitePCLRaw.provider.e_sqlite3.dll', 'e_sqlite3.dll') {
    $file = Join-Path $copy $path
    $held = Join-Path $OutputDirectory 'held-file'
    [IO.File]::Move($file, $held)
    Expect-PublishFailure "Missing launch-critical $path is rejected" { Inspect-Fixture "missing-$path" } 'Missing launch-critical'
    [IO.File]::Move($held, $file)
}
$runtimePath = Join-Path $copy 'Kora.runtimeconfig.json'
$runtimeBytes = [IO.File]::ReadAllBytes($runtimePath)
foreach ($case in 'missing-framework', 'wrong-version', 'extra-framework') {
    $runtime = Get-Content -LiteralPath $runtimePath -Raw | ConvertFrom-Json -AsHashtable
    switch ($case) {
        'missing-framework' { $runtime.runtimeOptions.frameworks = @($runtime.runtimeOptions.frameworks[0]) }
        'wrong-version' { $runtime.runtimeOptions.frameworks[0].version = '9.0.0' }
        'extra-framework' { $runtime.runtimeOptions.frameworks += @{ name = 'Unexpected'; version = '10.0.0' } }
    }
    Write-ProofJson $runtime $runtimePath
    Expect-PublishFailure "$case is rejected" { Inspect-Fixture $case } 'Runtime contract changed'
    [IO.File]::WriteAllBytes($runtimePath, $runtimeBytes)
}
$depsPath = Join-Path $copy 'Kora.deps.json'
$depsBytes = [IO.File]::ReadAllBytes($depsPath)
$deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json -AsHashtable
$deps.runtimeTarget.name = $deps.runtimeTarget.name -replace 'win-x(64|86)$', 'win-arm64'
Write-ProofJson $deps $depsPath
Expect-PublishFailure 'Wrong dependency RID is rejected' { Inspect-Fixture 'wrong-dependency-rid' } 'dependency target'
[IO.File]::WriteAllBytes($depsPath, $depsBytes)
$deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json -AsHashtable
$target = $deps.targets[$deps.runtimeTarget.name]
$sqliteLibrary = @($target.Keys | Where-Object { $_ -like 'SQLitePCLRaw.lib.e_sqlite3/*' })[0]
$target[$sqliteLibrary].native["runtimes/$Rid/native/declared-missing.dll"] = @{}
Write-ProofJson $deps $depsPath
Expect-PublishFailure 'Declared native asset missing from actual publish is rejected' { Inspect-Fixture 'missing-declared-native' } 'Declared native asset absent'
[IO.File]::WriteAllBytes($depsPath, $depsBytes)
$deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json -AsHashtable
$nativeAssets = $deps.targets[$deps.runtimeTarget.name][$sqliteLibrary].native
$asset = @($nativeAssets.Keys | Where-Object { $_ -like '*/e_sqlite3.dll' })[0]
$nativeAssets.Remove($asset)
$nativeAssets['runtimes/win-arm64/native/e_sqlite3.dll'] = @{}
Write-ProofJson $deps $depsPath
Expect-PublishFailure 'Unapproved native asset RID cannot borrow approved PE bytes' { Inspect-Fixture 'wrong-native-asset-rid' } 'RID/path mismatch'
[IO.File]::WriteAllBytes($depsPath, $depsBytes)
$deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json -AsHashtable
$deps.targets[$deps.runtimeTarget.name][$sqliteLibrary].Remove('native')
Write-ProofJson $deps $depsPath
Expect-PublishFailure 'Ambient SQLite without declared package is rejected' { Inspect-Fixture 'ambient-sqlite' } 'Standard SQLite'
[IO.File]::WriteAllBytes($depsPath, $depsBytes)
$clr = Join-Path $copy 'coreclr.dll'
[IO.File]::WriteAllText($clr, 'Not a runtime; negative fixture only.')
Expect-PublishFailure 'Bundled CLR violates framework-dependent contract' { Inspect-Fixture 'bundled-clr' } 'bundled CLR'
Remove-Item -LiteralPath $clr
$nativeFile = Join-Path $copy 'e_sqlite3.dll'
$originalBytes = [IO.File]::ReadAllBytes($nativeFile)
$bytes = [byte[]]$originalBytes.Clone()
$coffOffset = [BitConverter]::ToInt32($bytes, 0x3c) + 4
$wrongMachine = if ($Rid -ceq 'win-x64') { [byte[]]@(0x4c, 0x01) } else { [byte[]]@(0x64, 0x86) }
$bytes[$coffOffset] = $wrongMachine[0]
$bytes[$coffOffset + 1] = $wrongMachine[1]
[IO.File]::WriteAllBytes($nativeFile, $bytes)
Expect-PublishFailure 'Wrong native PE architecture is rejected' { Inspect-Fixture 'wrong-native' } 'Wrong native architecture'
Expect-PublishFailure 'Tampered transferred file fails complete byte identity' { Assert-Payload $copy $manifest } 'Payload identity changed'
[IO.File]::WriteAllBytes($nativeFile, $originalBytes)
Assert-Payload $copy $manifest
$passed.Add('All actual source payload bytes restored in owned test copy; original payload never mutated')
if ($IsWindows) {
    $link = Join-Path $OutputDirectory 'linked-payload'
    New-Item -ItemType Junction -Path $link -Target $copy | Out-Null
    Expect-PublishFailure 'Linked payload path is rejected before inspection' {
        & (Join-Path $PSScriptRoot 'Inspect-Publish.ps1') -Payload $link -Revision $manifest.revision `
            -Rid $Rid -EvidenceDirectory (Join-Path $OutputDirectory 'linked-evidence') -BuildOrigin 'Negative fixture'
    } 'reparse'
    Remove-Item -LiteralPath $link
}
Write-ProofJson ([ordered]@{ schema = 1; tests = @($passed); count = $passed.Count; rid = $Rid; staticOnly = $true
    sourceRevision = $manifest.revision; inspectionSha256 = (Get-FileHash -LiteralPath $Inspection).Hash.ToLowerInvariant() }) `
    (Join-Path $OutputDirectory 'tests.json')
Write-Host "$($passed.Count) maintained publish contracts passed for $Rid; no app, installer or native library executed."
exit 0
