. (Join-Path $PSScriptRoot 'Common.ps1')

function Assert-ManagedCheckoutIdentity {
    param([string] $Checkout, [string] $Repository, [string] $Revision)
    Assert-NoLinks $Checkout
    $origin = (& git -C $Checkout remote get-url origin | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $origin -cne $Repository) { throw 'Checkout origin changed.' }
    $head = (& git -C $Checkout rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -cne $Revision) { throw 'Checkout revision changed.' }
    $status = (& git -C $Checkout status --porcelain=v1 --untracked-files=all | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $status) {
        throw 'Checkout has local edits/untracked files. Preserved; no reset, pull, clean or overwrite permitted.'
    }
}

function Invoke-ManagedSource {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Repository,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string] $Revision,
        [Parameter(Mandatory)][scriptblock] $BuildAndInspect
    )
    $Root = [IO.Path]::GetFullPath($Root)
    $ownerFile = Join-Path $Root 'managed-source.json'
    if (Test-Path -LiteralPath $Root) {
        Assert-NoLinks $Root
        if (!(Test-Path -LiteralPath $ownerFile -PathType Leaf)) {
            throw 'Root is not a recognised managed checkout. Choose a new dedicated empty location.'
        }
        $owner = Get-Content -LiteralPath $ownerFile -Raw | ConvertFrom-Json
        if ($owner.schema -ne 1 -or $owner.repository -cne $Repository -or $owner.root -cne $Root) {
            throw 'Managed source ownership/repository mismatch.'
        }
    }
    else {
        New-ProofDirectory $Root
        Write-ProofJson ([ordered]@{ schema = 1; repository = $Repository; root = $Root }) $ownerFile
    }
    $lockFile = Join-Path $Root 'operator.lock'
    $lock = [IO.File]::Open($lockFile, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $checkout = Join-Path $Root "checkouts\$Revision"
        $deployment = Join-Path $Root "deployments\$Revision"
        if (!(Test-Path -LiteralPath $checkout)) {
            $checkoutParent = Split-Path -Parent $checkout
            New-Item -ItemType Directory -Path $checkoutParent -Force | Out-Null
            Assert-NoLinks $checkoutParent
            Invoke-Checked 'git' @('-c', 'core.hooksPath=', 'clone', '--no-checkout', '--', $Repository, $checkout)
            Invoke-Checked 'git' @('-C', $checkout, '-c', 'core.hooksPath=', 'checkout', '--detach', $Revision)
        }
        Assert-ManagedCheckoutIdentity $checkout $Repository $Revision
        if (Test-Path -LiteralPath $deployment) {
            $receiptPath = Join-Path $deployment 'deployment.json'
            if (!(Test-Path -LiteralPath $receiptPath)) { throw 'Partial deployment exists; preserved for operator review.' }
            $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
            if ($receipt.revision -cne $Revision -or $receipt.repository -cne $Repository) {
                throw 'Deployment identity mismatch.'
            }
            Assert-Payload (Join-Path $deployment 'payload') $receipt
            Write-Host "Identical output already verified; reused $deployment (no activation or installation)."
            return
        }
        $stage = Join-Path $Root ('staging\' + $Revision + '-' + [guid]::NewGuid().ToString('N'))
        New-ProofDirectory $stage
        $payload = Join-Path $stage 'payload'
        New-ProofDirectory $payload
        & $BuildAndInspect $checkout $payload $stage
        $files = @(Get-PayloadFiles $payload)
        if ($files.Count -eq 0) { throw 'Empty publish output; previous output preserved.' }
        $receipt = [ordered]@{
            schema = 1
            mode = 'managed-source'
            repository = $Repository
            revision = $Revision
            checkout = $checkout
            deployment = $deployment
            maintenance = 'External operator only; no updater or startup registration.'
            protection = 'User-writable staging is NOT a protected installation; do not enable workers.'
            files = $files
        }
        Write-ProofJson $receipt (Join-Path $stage 'deployment.json')
        Assert-Payload $payload $receipt
        $parent = Split-Path -Parent $deployment
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
        Assert-NoLinks $parent
        Assert-ManagedCheckoutIdentity $checkout $Repository $Revision
        Move-Item -LiteralPath $stage -Destination $deployment
        Write-Host "Verified output: $deployment. Previous outputs retained; no application launched or installed."
    }
    finally { $lock.Dispose() }
}
