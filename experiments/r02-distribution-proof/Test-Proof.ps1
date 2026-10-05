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
