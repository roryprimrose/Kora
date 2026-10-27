#requires -Version 7.5
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $RepositoryPath,
    [ValidateSet('Read', 'Initialize', 'Adopt', 'Claim', 'Register', 'Certify', 'Recover', 'Reconcile', 'Consume', 'NarrowScopes', 'RetireLegacy', 'ReleaseSource', 'Next')]
    [string] $Action = 'Read',
    [string] $Session,
    [long] $ExpectedEpoch = -1,
    [long] $ExpectedRevision = -1,
    [string] $RequestPath,
    [switch] $RecoverEvidenceTerminalMetadata
)
. (Join-Path $PSScriptRoot 'Coordinator.Common.ps1')
try {
    if ($RecoverEvidenceTerminalMetadata) {
        if ($Action -cne 'Consume') { throw 'Evidence terminal recovery is only supported for Consume.' }
        . (Join-Path $PSScriptRoot 'Coordinator.TerminalRecovery.ps1')
        Invoke-CoordinatorTerminalRecovery -RepositoryPath $RepositoryPath -Session $Session `
            -ExpectedEpoch $ExpectedEpoch -ExpectedRevision $ExpectedRevision -RequestPath $RequestPath |
            ConvertTo-Json -Depth 40
    } else {
        Invoke-Coordinator -RepositoryPath $RepositoryPath -Action $Action -Session $Session `
            -ExpectedEpoch $ExpectedEpoch -ExpectedRevision $ExpectedRevision -RequestPath $RequestPath |
            ConvertTo-Json -Depth 40
    }
} catch {
    [Console]::Error.WriteLine("Coordinator rejected operation: $($_.Exception.Message)")
    exit 1
}
