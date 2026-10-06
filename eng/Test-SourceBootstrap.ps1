#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string] $OutputDirectory)
$savedActions = $env:GITHUB_ACTIONS
$env:GITHUB_ACTIONS = $null
try {
. (Join-Path $PSScriptRoot 'SourceBootstrap.Common.ps1')
New-ProofDirectory $OutputDirectory
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$passed = [Collections.Generic.List[string]]::new()
function Expect-SourceFailure {
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
foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1') {
    $tokens = $null
    $errors = $null
    [Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count) { throw ($errors | Out-String) }
}
$passed.Add('All maintained eng scripts parse')
$expectedTools = @('eng\Invoke-SourceBootstrap.ps1', 'eng\SourceBootstrap.Common.ps1',
    'eng\Test-SourceStage.ps1', 'eng\Get-BuildVersion.ps1', 'eng\SourceCheckout.Common.ps1',
    'eng\Distribution.Common.ps1', 'eng\NativeInspection.Common.ps1', 'eng\Inspect-Publish.ps1')
$tools = @(Get-SourceToolFiles)
if (($tools.path -join '|') -cne ($expectedTools -join '|')) { throw 'Maintained bootstrap tool closure changed.' }
foreach ($tool in $tools) {
    $path = Join-Path (Join-Path $PSScriptRoot '..') $tool.path
    if ($tool.sha256 -cne (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()) {
        throw "Tool identity does not describe maintained bytes: $($tool.path)"
    }
}
$passed.Add('Exact eight maintained helper paths and actual SHA-256 tool closure; no experiment ownership')
$repository = Join-Path $OutputDirectory 'fixture-repository'
New-ProofDirectory $repository
Invoke-Checked 'git' @('init', $repository)
Invoke-Checked 'git' @('-C', $repository, 'config', 'user.name', 'R17 fixture')
Invoke-Checked 'git' @('-C', $repository, 'config', 'user.email', 'r17@example.invalid')
Write-ProofJson @{ sdk = @{ version = '10.0.401' } } (Join-Path $repository 'global.json')
Write-ProofJson @{ version = 2; dependencies = @{} } (Join-Path $repository 'packages.lock.json')
'fixture source' | Set-Content -LiteralPath (Join-Path $repository 'input.txt')
Invoke-Checked 'git' @('-C', $repository, 'add', '.')
Invoke-Checked 'git' @('-C', $repository, '-c', 'core.hooksPath=', '-c', 'commit.gpgsign=false', 'commit', '-m', 'Owned scratch fixture only')
$revision = (& git -C $repository rev-parse HEAD | Out-String).Trim()
$savedSha = $env:GITHUB_SHA
try {
    $env:GITHUB_ACTIONS = 'true'
    $env:GITHUB_SHA = '0' * 40
    Expect-SourceFailure 'Production version resolver still rejects a mismatched workflow revision' {
        & (Join-Path $PSScriptRoot 'Get-BuildVersion.ps1') -RepositoryPath $repository
    } 'Checkout revision differs from the workflow source revision'
}
finally {
    $env:GITHUB_ACTIONS = $null
    $env:GITHUB_SHA = $savedSha
}
$root = Join-Path $OutputDirectory 'managed'
$preview = Invoke-SourceBootstrap $root $revision -Repository $repository
if ((Test-Path -LiteralPath $root) -or $preview.channel -cne 'local-source' -or $preview.activation -cne $script:SourceActivation) {
    throw 'Preview wrote state or acquired activation.'
}
$passed.Add('Default preview is read-only with explicit provenance/effects/unavailable activation')
Expect-SourceFailure 'Explicit build trust required before mutation' {
    Invoke-SourceBootstrap $root $revision -Action Build -Repository $repository
} 'TrustBuildCode'
if (Test-Path -LiteralPath $root) { throw 'Untrusted invocation wrote files.' }
Expect-SourceFailure 'Mutable revision refused' { Invoke-SourceBootstrap $root 'main' } 'pattern'
Expect-SourceFailure 'Relative path refused' { Invoke-SourceBootstrap 'relative-root' $revision } 'absolute'
Expect-SourceFailure 'Filesystem root refused' { Invoke-SourceBootstrap ([IO.Path]::GetPathRoot($OutputDirectory)) $revision } 'filesystem root'
if ($IsWindows) {
    Expect-SourceFailure 'Live Program Files path unavailable even for preview' {
        Invoke-SourceBootstrap (Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Kora-build-fixture') $revision
    } 'Live Program Files'
}
Expect-SourceFailure 'User repository refused even for preview' { Invoke-SourceBootstrap $repository $revision } 'outside'
Expect-SourceFailure 'Nested user repository root refused' { Invoke-SourceBootstrap (Join-Path $repository 'child') $revision } 'outside'
Expect-SourceFailure 'Unknown existing directory refused' { Invoke-SourceBootstrap $OutputDirectory $revision } 'Unowned'
Expect-SourceFailure 'Public interface cannot choose untrusted repository' {
    & (Join-Path $PSScriptRoot 'Invoke-SourceBootstrap.ps1') -Root $root -Revision $revision -Repository $repository
} 'parameter'
Expect-SourceFailure 'Release-channel claims unavailable' {
    & (Join-Path $PSScriptRoot 'Invoke-SourceBootstrap.ps1') -Root $root -Revision $revision -Channel stable
} 'ValidateSet'
$originalPrerequisites = ${function:Get-SourcePrerequisites}
function Get-SourcePrerequisites { @(@{name = 'dotnet'; available = $false; remediation = 'Install manually, no elevation.'}) }
Expect-SourceFailure 'Missing prerequisite rejected before checkout' {
    Invoke-SourceBootstrap $root $revision -Action Build -TrustBuildCode -Repository $repository
} 'Missing prerequisite dotnet'
if (Test-Path -LiteralPath $root) { throw 'Missing prerequisite created root.' }
Set-Item Function:\Get-SourcePrerequisites $originalPrerequisites

# Compile/inspection/smoke seams model only orchestration; real native checks run separately.
$script:failure = ''
$script:compileCalls = 0
$script:change = ''
$originalCompile = ${function:Invoke-SourceCompile}
function Invoke-SourceCompile {
    param($Checkout, $Payload, $Stage, $Revision)
    $script:compileCalls++
    foreach ($phase in 'restore', 'build', 'publish') {
        $phase | Set-Content -LiteralPath (Join-Path $Stage "$phase.log")
        if ($script:failure -ceq $phase) { throw "fixture $phase failure" }
    }
    if ($script:failure -ceq 'empty') { return @{} }
    Copy-Item -LiteralPath (Join-Path $Checkout 'input.txt') -Destination (Join-Path $Payload 'fixture.txt')
    switch ($script:change) {
        'tracked' { 'operator edit' | Set-Content -LiteralPath (Join-Path $Checkout 'input.txt') }
        'untracked' { 'operator file' | Set-Content -LiteralPath (Join-Path $Checkout 'untracked.txt') }
        'origin' { Invoke-Checked 'git' @('-C', $Checkout, 'remote', 'set-url', 'origin', 'wrong-origin') }
        'revision' { Invoke-Checked 'git' @('-C', $Checkout, '-c', 'core.hooksPath=', 'checkout', '--detach', $script:firstRevision) }
    }
    return [ordered]@{ sdk = '10.0.401'; version = '0.1.0'; rid = 'win-x64'; configuration = 'Release'
        selfContained = $false; dependencyRestore = 'locked'; channel = 'local-source'
        origin = 'Local operator build of canonical source; not an official release binary.'; os = 'fixture' }
}
function Invoke-SourceInspect {
    param($Checkout, $Payload, $Stage, $Revision, $Build)
    if ($script:failure -ceq 'inspection') { throw 'fixture inspection failure' }
    New-ProofDirectory (Join-Path $Stage 'inspection')
    Write-ProofJson @{ schema = 1; revision = $Revision; files = @(Get-PayloadFiles $Payload) } (Join-Path $Stage 'inspection\payload.json')
}
function Assert-SourceInspection {
    param($Payload, $InspectionPath, $Revision, $Version)
    $inspection = Read-SourceJson $InspectionPath
    Assert-SourceFields $inspection @{ schema = 1; revision = $Revision }
    Assert-Payload $Payload $inspection
}
function Assert-SourceSqliteLock { }
$originalSmoke = ${function:Invoke-SourceSmoke}
function Invoke-SourceSmoke {
    if ($script:failure -ceq 'smoke') { throw 'fixture smoke failure' }
    return [ordered]@{ status = 'passed'; kind = 'bounded-static-child'; applicationLaunched = $false; timeoutSeconds = 60 }
}
function Build-Fixture {
    param([string] $Root, [string] $Revision)
    Invoke-SourceBootstrap $Root $Revision -Action Build -TrustBuildCode -Repository $repository
}
Build-Fixture $root $revision | Out-Null
$receiptPath = Join-Path $root "outputs\$revision\source-build.json"
$receiptHash = (Get-FileHash -LiteralPath $receiptPath).Hash
$calls = $script:compileCalls
Build-Fixture $root $revision | Out-Null
if ($calls -ne $script:compileCalls -or (Get-FileHash -LiteralPath $receiptPath).Hash -cne $receiptHash) { throw 'Rerun rebuilt/rewrote output.' }
$passed.Add('Exact detached checkout, pinned inputs, owned root and hash-verified no-build rerun')
$passed.Add('Synthetic checkout versioning is isolated from the caller workflow identity')
$ownerPath = Join-Path $root 'source-owner.json'
$originalOwner = Get-Content -LiteralPath $ownerPath -Raw
foreach ($field in 'schema', 'root', 'repository', 'mode', 'channel', 'bootstrapVersion', 'protected', 'activation') {
    $owner = Read-SourceJson $ownerPath
    $owner[$field] = 'invalid'
    Write-ProofJson $owner $ownerPath
    Expect-SourceFailure "Wrong owner $field refused without mutation" { Build-Fixture $root $revision } 'mismatch'
    $originalOwner | Set-Content -LiteralPath $ownerPath -NoNewline
}
'{ malformed' | Set-Content -LiteralPath $ownerPath
Expect-SourceFailure 'Malformed owner receipt refused' { Build-Fixture $root $revision } 'Invalid source receipt'
$originalOwner | Set-Content -LiteralPath $ownerPath -NoNewline
$originalReceipt = Get-Content -LiteralPath $receiptPath -Raw
foreach ($field in 'schema', 'revision', 'repository', 'checkout', 'deployment', 'channel', 'protected', 'trustAcknowledged') {
    $receipt = Read-SourceJson $receiptPath
    $receipt[$field] = 'invalid'
    Write-ProofJson $receipt $receiptPath
    Expect-SourceFailure "Wrong build receipt $field refused" { Build-Fixture $root $revision } 'mismatch'
    $originalReceipt | Set-Content -LiteralPath $receiptPath -NoNewline
}
foreach ($field in 'sdk', 'version', 'rid', 'dependencyRestore', 'origin', 'selfContained') {
    $receipt = Read-SourceJson $receiptPath
    $receipt.build[$field] = 'invalid'
    Write-ProofJson $receipt $receiptPath
    Expect-SourceFailure "Wrong provenance $field refused" { Build-Fixture $root $revision } 'mismatch'
    $originalReceipt | Set-Content -LiteralPath $receiptPath -NoNewline
}
$receipt = Read-SourceJson $receiptPath
$receipt.protected = 'False'
Write-ProofJson $receipt $receiptPath
Expect-SourceFailure 'String-valued protection boolean refused' { Build-Fixture $root $revision } 'mismatch'
$originalReceipt | Set-Content -LiteralPath $receiptPath -NoNewline
$receipt = Read-SourceJson $receiptPath
$receipt.schema = '1'
Write-ProofJson $receipt $receiptPath
Expect-SourceFailure 'String-valued schema refused' { Build-Fixture $root $revision } 'mismatch'
$originalReceipt | Set-Content -LiteralPath $receiptPath -NoNewline
$receipt = Read-SourceJson $receiptPath
$receipt.bootstrapFiles[0].sha256 = '0' * 64
Write-ProofJson $receipt $receiptPath
Expect-SourceFailure 'Bootstrap tooling hash mismatch refused' { Build-Fixture $root $revision } 'tooling hashes changed'
$originalReceipt | Set-Content -LiteralPath $receiptPath -NoNewline
$receipt = Read-SourceJson $receiptPath
$receipt.bootstrapFiles[-1].path = 'experiments\r02-distribution-proof\Inspect-Publish.ps1'
Write-ProofJson $receipt $receiptPath
Expect-SourceFailure 'Old experiment tool-path receipt refused, never relabelled' { Build-Fixture $root $revision } 'tooling hashes changed'
$originalReceipt | Set-Content -LiteralPath $receiptPath -NoNewline
$receipt = Read-SourceJson $receiptPath
$receipt.bootstrapVersion = '1.0.0'
Write-ProofJson $receipt $receiptPath
Expect-SourceFailure 'Historical bootstrap version refused without migration/adoption' { Build-Fixture $root $revision } 'mismatch'
$originalReceipt | Set-Content -LiteralPath $receiptPath -NoNewline
$receipt = Read-SourceJson $receiptPath
$receipt.inputs[0].sha256 = '0' * 64
Write-ProofJson $receipt $receiptPath
Expect-SourceFailure 'Input hash mismatch refused' { Build-Fixture $root $revision } 'inputs changed'
$originalReceipt | Set-Content -LiteralPath $receiptPath -NoNewline
$receipt = Read-SourceJson $receiptPath
$receipt.smoke.status = 'unavailable'
Write-ProofJson $receipt $receiptPath
Expect-SourceFailure 'Unverified smoke receipt refused' { Build-Fixture $root $revision } 'mismatch'
$originalReceipt | Set-Content -LiteralPath $receiptPath -NoNewline
'{}' | Set-Content -LiteralPath $receiptPath
Expect-SourceFailure 'Incomplete receipt refused' { Build-Fixture $root $revision } 'mismatch'
$originalReceipt | Set-Content -LiteralPath $receiptPath -NoNewline
'{ malformed' | Set-Content -LiteralPath $receiptPath
Expect-SourceFailure 'Malformed build receipt refused' { Build-Fixture $root $revision } 'Invalid source receipt'
$originalReceipt | Set-Content -LiteralPath $receiptPath -NoNewline
$payloadFile = Join-Path $root "outputs\$revision\payload\fixture.txt"
$payloadBytes = [IO.File]::ReadAllBytes($payloadFile)
'tamper' | Set-Content -LiteralPath $payloadFile
Expect-SourceFailure 'Payload hash mismatch refused on rerun' { Build-Fixture $root $revision } 'Payload identity'
[IO.File]::WriteAllBytes($payloadFile, $payloadBytes)
$inspectionPath = Join-Path $root "outputs\$revision\inspection\payload.json"
$inspectionBytes = [IO.File]::ReadAllBytes($inspectionPath)
'{}' | Set-Content -LiteralPath $inspectionPath
Expect-SourceFailure 'Inspection hash mismatch refused' { Build-Fixture $root $revision } 'Payload identity'
[IO.File]::WriteAllBytes($inspectionPath, $inspectionBytes)
$checkout = Join-Path $root "checkouts\$revision"
$longPaths = (& git -C $checkout config --bool core.longpaths | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $longPaths -cne 'true') { throw 'Managed checkout lost long-path support.' }
$passed.Add('Checkout retains checkout-local Windows long-path support')
Invoke-Checked 'git' @('-C', $checkout, '-c', 'core.hooksPath=', 'switch', '-c', 'fixture-attached')
Expect-SourceFailure 'Attached checkout refused even at exact clean revision' { Build-Fixture $root $revision } 'remain detached'
Invoke-Checked 'git' @('-C', $checkout, '-c', 'core.hooksPath=', 'checkout', '--detach', $revision)
'local edit' | Set-Content -LiteralPath (Join-Path $checkout 'input.txt')
Expect-SourceFailure 'Tracked local edit preserved' { Build-Fixture $root $revision } 'local edits'
if ((Get-Content -LiteralPath (Join-Path $checkout 'input.txt') -Raw).Trim() -cne 'local edit') { throw 'Edit lost.' }
$script:firstRevision = $revision
'second source' | Set-Content -LiteralPath (Join-Path $repository 'input.txt')
Invoke-Checked 'git' @('-C', $repository, 'add', '.')
Invoke-Checked 'git' @('-C', $repository, '-c', 'core.hooksPath=', '-c', 'commit.gpgsign=false', 'commit', '-m', 'Second fixture revision')
$second = (& git -C $repository rev-parse HEAD | Out-String).Trim()
$sentinel = Join-Path $root 'previous-runnable-sentinel.txt'
'previous deployment/user data stand-in' | Set-Content -LiteralPath $sentinel
$sentinelHash = (Get-FileHash -LiteralPath $sentinel).Hash
foreach ($phase in 'restore', 'build', 'publish', 'inspection', 'smoke', 'empty') {
    $script:failure = $phase
    $message = if ($phase -ceq 'empty') { 'Empty publish output' } else { "fixture $phase failure" }
    Expect-SourceFailure "Partial $phase failure retains stage and previous output" { Build-Fixture $root $second } $message
    if (Test-Path -LiteralPath (Join-Path $root "outputs\$second")) { throw 'Partial output promoted.' }
}
$script:failure = ''
$failedStages = @(Get-ChildItem -LiteralPath (Join-Path $root 'staging') -Filter failure.json -Recurse)
if ($failedStages.Count -ne 6) { throw 'Failure evidence missing.' }
Build-Fixture $root $second | Out-Null
if ((Get-FileHash -LiteralPath $receiptPath).Hash -cne $receiptHash -or
    (Get-FileHash -LiteralPath $sentinel).Hash -cne $sentinelHash -or
    @(Get-ChildItem -LiteralPath (Join-Path $root 'staging') -Filter failure.json -Recurse).Count -ne 6) {
    throw 'Retry modified earlier output, failure evidence or user-data stand-in.'
}
$passed.Add('Non-destructive fresh-stage retry succeeds; previous output, edits and user-data stand-in retained')
foreach ($change in 'tracked', 'untracked', 'origin', 'revision') {
    $changedRoot = Join-Path $OutputDirectory "changed-$change"
    $script:change = $change
    $message = switch ($change) { 'origin' { 'origin changed' }; 'revision' { 'revision changed' }; default { 'local edits' } }
    Expect-SourceFailure "Post-build $change change prevents promotion and preserves checkout" { Build-Fixture $changedRoot $second } $message
    if (Test-Path -LiteralPath (Join-Path $changedRoot "outputs\$second")) { throw 'Changed checkout promoted.' }
    $changedCheckout = Join-Path $changedRoot "checkouts\$second"
    switch ($change) {
        'tracked' {
            if ((Get-Content -LiteralPath (Join-Path $changedCheckout 'input.txt') -Raw).Trim() -cne 'operator edit') { throw 'Tracked edit lost.' }
        }
        'untracked' {
            if ((Get-Content -LiteralPath (Join-Path $changedCheckout 'untracked.txt') -Raw).Trim() -cne 'operator file') { throw 'Untracked edit lost.' }
        }
        'origin' {
            if ((& git -C $changedCheckout remote get-url origin | Out-String).Trim() -cne 'wrong-origin') { throw 'Origin reset.' }
        }
        'revision' {
            if ((& git -C $changedCheckout rev-parse HEAD | Out-String).Trim() -cne $revision) { throw 'Revision reset.' }
        }
    }
    Expect-SourceFailure "Retry of changed $change checkout refused non-destructively" { Build-Fixture $changedRoot $second } $message
}
$script:change = ''
Expect-SourceFailure 'Unknown exact revision leaves partial checkout for review' {
    Build-Fixture $root ('0' * 40)
} 'git failed'
Expect-SourceFailure 'Partial checkout is not repaired/reset on retry' {
    Build-Fixture $root ('0' * 40)
} 'revision changed'
$partialOutputRoot = Join-Path $OutputDirectory 'partial-output'
Build-Fixture $partialOutputRoot $revision | Out-Null
Remove-Item -LiteralPath (Join-Path $partialOutputRoot "outputs\$revision\source-build.json")
Expect-SourceFailure 'Missing output receipt preserved, never overwritten' { Build-Fixture $partialOutputRoot $revision } 'Invalid source receipt'
$busy = [IO.File]::Open((Join-Path $root 'operator.lock'), 'Open', 'ReadWrite', 'None')
try { Expect-SourceFailure 'Concurrent operator lock refused' { Build-Fixture $root $second } 'being used' }
finally { $busy.Dispose() }
$originalChecked = ${function:Invoke-Checked}
function Invoke-Checked {
    param($Command, $Arguments)
    if ($Command -eq 'git' -and $Arguments -contains 'clone') {
        New-ProofDirectory $Arguments[-1]
        'interrupted clone' | Set-Content -LiteralPath (Join-Path $Arguments[-1] 'partial.txt')
        throw 'fixture clone failure'
    }
    & $originalChecked $Command $Arguments
}
$partialCloneRoot = Join-Path $OutputDirectory 'partial-clone'
Expect-SourceFailure 'Partial clone failure retains owned checkout bytes' { Build-Fixture $partialCloneRoot $revision } 'clone failure'
Set-Item Function:\Invoke-Checked $originalChecked
Expect-SourceFailure 'Partial clone retry refuses adoption/repair' { Build-Fixture $partialCloneRoot $revision } 'origin changed'
if (!(Test-Path -LiteralPath (Join-Path $partialCloneRoot "checkouts\$revision\partial.txt"))) { throw 'Partial clone lost.' }
if ($IsWindows) {
    $linked = Join-Path $OutputDirectory 'linked'
    New-Item -ItemType Junction -Path $linked -Target $root | Out-Null
    Expect-SourceFailure 'Reparse root refused' { Build-Fixture $linked $second } 'reparse'
    Remove-Item -LiteralPath $linked
    $passed.Add('Fixture junction removed without target mutation')
}
Set-Item Function:\Invoke-SourceSmoke $originalSmoke
$child = Join-Path $OutputDirectory 'child.ps1'
'param($Stage,$Root,$Checkout,$Repository,$Revision,$Deployment); exit 7' | Set-Content -LiteralPath $child
Expect-SourceFailure 'Owned child nonzero exit refused with diagnostics' {
    Invoke-SourceSmoke $OutputDirectory $root $checkout $repository $revision $OutputDirectory -VerifierPath $child
} 'child failed'
'param($Stage,$Root,$Checkout,$Repository,$Revision,$Deployment); Start-Sleep -Seconds 30' | Set-Content -LiteralPath $child
Expect-SourceFailure 'Owned child timeout terminates child tree' {
    Invoke-SourceSmoke $OutputDirectory $root $checkout $repository $revision $OutputDirectory -VerifierPath $child -TimeoutSeconds 1
} 'timed out'
$originalCommand = ${function:Invoke-SourceCommand}
$script:sdk = '9.0.999'
function dotnet { $global:LASTEXITCODE = 0; $script:sdk }
$compileStage = Join-Path $OutputDirectory 'compile-arguments'
New-ProofDirectory $compileStage
$cleanCheckout = Join-Path $root "checkouts\$second"
Expect-SourceFailure 'Wrong SDK blocked before restore or build' {
    & $originalCompile $cleanCheckout $compileStage $compileStage $second
} 'Exact pinned SDK'
$script:sdk = '10.0.401'
$script:commands = [Collections.Generic.List[object]]::new()
function Invoke-SourceCommand { param($Command,$Arguments,$Log); $script:commands.Add(@($Arguments)) }
& $originalCompile $cleanCheckout $compileStage $compileStage $second | Out-Null
if ($script:commands.Count -ne 3 -or $script:commands[0][0] -cne 'restore' -or
    $script:commands[0] -notcontains '--locked-mode' -or $script:commands[0] -contains '--runtime' -or
    $script:commands[1] -notcontains '--no-restore' -or $script:commands[2] -notcontains '--no-build' -or
    $script:commands[2] -notcontains '--no-restore' -or $script:commands[2] -notcontains '--artifacts-path') {
    throw 'Locked restore, separated build/publish or versioned artifact paths changed.'
}
$passed.Add('Exact SDK and locked multi-RID restore; separated Release build/no-build publish in owned artifacts')
Remove-Item Function:\dotnet
Set-Item Function:\Invoke-SourceCommand $originalCommand
Expect-SourceFailure 'Real command nonzero exit retains failure log' {
    Invoke-SourceCommand 'pwsh' @('-NoProfile', '-NonInteractive', '-Command', 'exit 7') (Join-Path $OutputDirectory 'command-failure.log')
} 'exit code 7'
Remove-Item -LiteralPath $child
Write-ProofJson ([ordered]@{ schema = 1; tests = @($passed); count = $passed.Count; fixtureOnly = $true
    scope = 'Orchestration/failure fixtures, not installed/native/runtime acceptance. No installer or Kora process executed.' }) `
    (Join-Path $OutputDirectory 'tests.json')
Write-Host "$($passed.Count) source-bootstrap fixture checks passed."
}
finally {
    $env:GITHUB_ACTIONS = $savedActions
}
exit 0
