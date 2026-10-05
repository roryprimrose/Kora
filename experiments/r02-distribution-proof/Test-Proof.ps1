[CmdletBinding()]
param([Parameter(Mandatory)][string] $OutputDirectory)
. (Join-Path $PSScriptRoot 'ManagedSource.ps1')
New-ProofDirectory $OutputDirectory
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$script:passed = [Collections.Generic.List[string]]::new()
foreach ($path in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1') {
    $tokens = $null
    $errors = $null
    [Management.Automation.Language.Parser]::ParseFile($path.FullName, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count -gt 0) { throw ($errors | Out-String) }
}
$passed.Add('All proof PowerShell scripts parse without errors')
function Expect-Failure {
    param([string] $Name, [scriptblock] $Action, [string] $Message)
    $observed = $false
    try { & $Action }
    catch {
        if ($_.Exception.Message -notlike "*$Message*") { throw }
        $observed = $true
    }
    if (!$observed) { throw "Expected failure: $Name" }
    $script:passed.Add($Name)
}
$fixture = Join-Path $OutputDirectory 'repository'
New-ProofDirectory $fixture
Invoke-Checked 'git' @('init', $fixture)
Invoke-Checked 'git' @('-C', $fixture, 'config', 'user.name', 'R02 synthetic fixture')
Invoke-Checked 'git' @('-C', $fixture, 'config', 'user.email', 'r02@example.invalid')
'original' | Set-Content -LiteralPath (Join-Path $fixture 'input.txt')
Invoke-Checked 'git' @('-C', $fixture, 'add', 'input.txt')
Invoke-Checked 'git' @('-C', $fixture, '-c', 'core.hooksPath=', '-c', 'commit.gpgsign=false', 'commit', '-m', 'Synthetic fixture')
$revision = (& git -C $fixture rev-parse HEAD | Out-String).Trim()
$root = Join-Path $OutputDirectory 'managed'
$build = {
    param($checkout, $payload, $stage)
    Copy-Item -LiteralPath (Join-Path $checkout 'input.txt') -Destination (Join-Path $payload 'fixture.txt')
}
Invoke-ManagedSource $root $fixture $revision $build
$receiptPath = Join-Path $root "deployments\$revision\deployment.json"
$receiptHash = (Get-FileHash -LiteralPath $receiptPath).Hash
Invoke-ManagedSource $root $fixture $revision { throw 'Rerun must not rebuild' }
if ((Get-FileHash -LiteralPath $receiptPath).Hash -ne $receiptHash) { throw 'Rerun changed receipt.' }
$passed.Add('Exact full revision, dedicated checkout/staging, hash-verified idempotent rerun')
$checkout = Join-Path $root "checkouts\$revision"
'local edit' | Set-Content -LiteralPath (Join-Path $checkout 'input.txt')
Expect-Failure 'Tracked local edit preserved' { Invoke-ManagedSource $root $fixture $revision $build } 'local edits'
if ((Get-Content -LiteralPath (Join-Path $checkout 'input.txt') -Raw).Trim() -ne 'local edit') {
    throw 'Local edit lost.'
}
Expect-Failure 'Unknown existing root refused' { Invoke-ManagedSource $fixture $fixture $revision $build } 'not a recognised'
Expect-Failure 'Wrong repository identity refused' { Invoke-ManagedSource $root 'wrong' $revision $build } 'mismatch'
Expect-Failure 'Mutable branch selection refused' { Invoke-ManagedSource $root $fixture 'main' $build } 'pattern'
'new revision' | Set-Content -LiteralPath (Join-Path $fixture 'input.txt')
Invoke-Checked 'git' @('-C', $fixture, 'add', 'input.txt')
Invoke-Checked 'git' @('-C', $fixture, '-c', 'core.hooksPath=', '-c', 'commit.gpgsign=false', 'commit', '-m', 'Second synthetic revision')
$second = (& git -C $fixture rev-parse HEAD | Out-String).Trim()
$originalRevision = $revision
foreach ($change in 'revision', 'origin', 'tracked', 'untracked') {
    $changedRoot = Join-Path $OutputDirectory "managed-$change-change"
    Invoke-ManagedSource $changedRoot $fixture $revision $build
    $previousReceipt = Join-Path $changedRoot "deployments\$revision\deployment.json"
    $previousHash = (Get-FileHash -LiteralPath $previousReceipt).Hash
    $message = switch ($change) {
        'revision' { 'Checkout revision changed' }
        'origin' { 'Checkout origin changed' }
        default { 'local edits/untracked files' }
    }
    Expect-Failure "Post-build $change change refused before promotion" {
        Invoke-ManagedSource $changedRoot $fixture $second {
            param($checkout, $payload, $stage)
            switch ($change) {
                'revision' {
                    Invoke-Checked 'git' @('-C', $checkout, '-c', 'core.hooksPath=', 'checkout', '--detach', $originalRevision)
                }
                'origin' { Invoke-Checked 'git' @('-C', $checkout, 'remote', 'set-url', 'origin', 'changed-origin') }
                'tracked' { 'changed during build' | Set-Content -LiteralPath (Join-Path $checkout 'input.txt') }
                'untracked' { 'new during build' | Set-Content -LiteralPath (Join-Path $checkout 'untracked.txt') }
            }
            & $build $checkout $payload $stage
        }
    } $message
    if (Test-Path -LiteralPath (Join-Path $changedRoot "deployments\$second")) {
        throw 'Changed source was promoted under the requested revision.'
    }
    if ((Get-FileHash -LiteralPath $previousReceipt).Hash -ne $previousHash) {
        throw 'Post-build source change modified the previous deployment.'
    }
    $stages = @(Get-ChildItem -LiteralPath (Join-Path $changedRoot 'staging') -Directory)
    if ($stages.Count -ne 1 -or !(Test-Path -LiteralPath (Join-Path $stages[0].FullName 'payload\fixture.txt'))) {
        throw 'Rejected build staging was not retained for operator review.'
    }
    $changedCheckout = Join-Path $changedRoot "checkouts\$second"
    switch ($change) {
        'revision' {
            if ((& git -C $changedCheckout rev-parse HEAD | Out-String).Trim() -cne $revision) {
                throw 'Changed checkout revision was not preserved.'
            }
        }
        'origin' {
            if ((& git -C $changedCheckout remote get-url origin | Out-String).Trim() -cne 'changed-origin') {
                throw 'Changed checkout origin was not preserved.'
            }
        }
        'tracked' {
            if ((Get-Content -LiteralPath (Join-Path $changedCheckout 'input.txt') -Raw).Trim() -cne 'changed during build') {
                throw 'Post-build tracked edit was not preserved.'
            }
        }
        'untracked' {
            if (!(Test-Path -LiteralPath (Join-Path $changedCheckout 'untracked.txt'))) {
                throw 'Post-build untracked file was not preserved.'
            }
        }
    }
}
Expect-Failure 'Build failure keeps previous output' {
    Invoke-ManagedSource $root $fixture $second { throw 'synthetic publish failure' }
} 'synthetic publish failure'
if ((Get-FileHash -LiteralPath $receiptPath).Hash -ne $receiptHash) { throw 'Previous output changed.' }
Expect-Failure 'Empty output is not promoted' { Invoke-ManagedSource $root $fixture $second {} } 'Empty publish'
Invoke-ManagedSource $root $fixture $second $build
$passed.Add('Successful later revision retains previous deployment and earlier checkout edits')
$payload = Join-Path $root "deployments\$second\payload"
'tamper' | Set-Content -LiteralPath (Join-Path $payload 'fixture.txt')
Expect-Failure 'Tampered output refused on rerun' { Invoke-ManagedSource $root $fixture $second $build } 'Payload identity'
$invalidRevision = '0' * 40
Expect-Failure 'Unknown exact revision cannot replace previous output' {
    Invoke-ManagedSource $root $fixture $invalidRevision $build
} 'git failed'
$busyLock = [IO.File]::Open((Join-Path $root 'operator.lock'), 'Open', 'ReadWrite', 'None')
try {
    Expect-Failure 'Concurrent operator refused' { Invoke-ManagedSource $root $fixture $second $build } 'being used'
}
finally { $busyLock.Dispose() }
Write-ProofJson ([ordered]@{ tests = @($passed); count = $passed.Count; fixtureOnly = $true }) `
    (Join-Path $OutputDirectory 'tests.json')
Write-Host "$($passed.Count) orchestration checks passed. Not application/runtime/ACL acceptance."
