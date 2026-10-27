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
    [switch] $RecoverEvidenceTerminalMetadata,
    [ValidateSet('UtcTimestampV1', 'HostedDeliveryV1', 'UtcRepresentationV1', 'NoHeldDecisionV1')][string] $PublishedTerminalWireProfile,
    [ValidateSet('MissingNativeEvidenceV1', 'MissingLaunchBlockerV1')][string] $CurrentOriginalDispatchProfile
)
. (Join-Path $PSScriptRoot 'Coordinator.Common.ps1')
try {
    if ($RecoverEvidenceTerminalMetadata -or $PublishedTerminalWireProfile -or $CurrentOriginalDispatchProfile) {
        if ($Action -cne 'Consume') { throw 'Terminal recovery is only supported for Consume.' }
        if (@($RecoverEvidenceTerminalMetadata.IsPresent, [bool]$PublishedTerminalWireProfile, [bool]$CurrentOriginalDispatchProfile |
            Where-Object { $_ }).Count -ne 1) { throw 'Select exactly one terminal recovery profile.' }
        . (Join-Path $PSScriptRoot 'Coordinator.TerminalRecovery.ps1')
        Invoke-CoordinatorTerminalRecovery -RepositoryPath $RepositoryPath -Session $Session `
            -ExpectedEpoch $ExpectedEpoch -ExpectedRevision $ExpectedRevision -RequestPath $RequestPath `
            -PublishedWireProfile $PublishedTerminalWireProfile -OriginalDispatchProfile $CurrentOriginalDispatchProfile |
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
