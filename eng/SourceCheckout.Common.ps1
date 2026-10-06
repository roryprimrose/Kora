. (Join-Path $PSScriptRoot 'Distribution.Common.ps1')

function Assert-ManagedCheckoutIdentity {
    param([string] $Checkout, [string] $Repository, [string] $Revision)
    Assert-NoLinks $Checkout
    $origin = (& git -C $Checkout remote get-url origin | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $origin -cne $Repository) { throw 'Checkout origin changed.' }
    $head = (& git -C $Checkout rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -cne $Revision) { throw 'Checkout revision changed.' }
    $branch = (& git -C $Checkout symbolic-ref -q HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 1 -or $branch) { throw 'Checkout must remain detached.' }
    $status = (& git -C $Checkout status --porcelain=v1 --untracked-files=all | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $status) {
        throw 'Checkout has local edits/untracked files. Preserved; no reset, pull, clean or overwrite permitted.'
    }
}
