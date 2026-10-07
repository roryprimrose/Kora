#requires -Version 7.5
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $OutputDirectory,
    [string] $SourceBuiltPackagePath,
    [string] $ReleasedPackagePath,
    [string] $NativeRuntimeDirectory,
    [switch] $RunHostComponentTests,
    [string] $PackageConfigPath
)
. (Join-Path $PSScriptRoot 'RuntimeValidation.Common.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ($output.Equals($repository, [StringComparison]::OrdinalIgnoreCase) -or
    $output.StartsWith($repository + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Preparation receipts must be outside the repository; never overwrite historical evidence.'
}
$identity = Assert-RuntimePreparationCheckout $repository
$profiles = @(Get-RuntimeHistoricalProfiles $repository)
New-ProofDirectory $output
Write-Host 'Running deterministic preparation contracts; no runtime, trace, account or provider is started.'
& (Join-Path $PSScriptRoot 'Test-RuntimeValidationContracts.ps1') -OutputDirectory (Join-Path $output 'contracts')
& (Join-Path $repository 'experiments\r02-runtime-lifecycle-proof\Test-ReceiptContracts.ps1')
$artifacts = @(
    Test-RuntimeArtifact 'source-built-sdk' $SourceBuiltPackagePath $profiles[0].packageSha256 $profiles[0].assemblySha256
    Test-RuntimeArtifact 'released-sdk' $ReleasedPackagePath $profiles[1].packageSha256 $profiles[1].assemblySha256
    foreach ($name in @('copilot-runtime.exe', 'runtime.node')) {
        $path = if ($NativeRuntimeDirectory) { Join-Path $NativeRuntimeDirectory $name } else { $null }
        $hash = if ($name -eq 'copilot-runtime.exe') { $profiles[0].nativeLauncherSha256 } else { $profiles[0].nativePayloadSha256 }
        Test-RuntimeArtifact $name $path $hash
    }
)
if ($NativeRuntimeDirectory -and (Test-Path -LiteralPath $NativeRuntimeDirectory -PathType Container)) {
    Assert-NoLinks $NativeRuntimeDirectory
    if (@(Get-ChildItem -LiteralPath $NativeRuntimeDirectory -Force |
        Where-Object Name -CNotIn @('copilot-runtime.exe', 'runtime.node', 'LICENSE.md')).Count -ne 0 -or
        !(Test-Path -LiteralPath (Join-Path $NativeRuntimeDirectory 'LICENSE.md') -PathType Leaf)) {
        throw 'Native directory must have only the approved launcher/payload and its reviewed license.'
    }
}
$tools = @(
    foreach ($name in @('dotnet', 'git', 'logman.exe', 'wpr.exe')) {
        [ordered]@{ name = $name; present = $null -ne (Get-Command $name -CommandType Application -ErrorAction SilentlyContinue) }
    }
)
$hostComponents = [ordered]@{ status = 'Not run'; reason = 'Opt in with -RunHostComponentTests; host-only managed build/tests, no Copilot SDK/native launch.' }
$artifactFailures = @($artifacts | Where-Object status -CEQ 'Failed')
if ($artifactFailures.Count -gt 0) {
    $hostComponents.reason = 'Supplied artifact failed verification; no build, restore or host-component tests attempted.'
}
elseif ($RunHostComponentTests) {
    $hostComponents = Invoke-RuntimeHostComponentTests $repository (Join-Path $output 'host-components') $PackageConfigPath
}
$after = Assert-RuntimePreparationCheckout $repository
if (($identity | ConvertTo-Json -Depth 5 -Compress) -cne ($after | ConvertTo-Json -Depth 5 -Compress)) {
    throw 'Historical input/HEAD changed during preparation; no readiness receipt may be recorded.'
}
$scriptFiles = @('RuntimeValidation.Common.ps1', 'Prepare-RuntimeValidation.ps1', 'Test-RuntimeValidationContracts.ps1',
    'Invoke-SyntheticRuntimeValidation.ps1', 'Invoke-ReleasedRuntimeLifecycle.ps1')
$report = [ordered]@{
    schemaVersion = 1; observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    head = $identity.head; base = $identity.base
    historicalFiles = $identity.historicalFiles
    harnessFiles = @(
        foreach ($name in $scriptFiles) {
            [ordered]@{ file = "eng\$name"; sha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $name)).Hash.ToLowerInvariant() }
        }
    )
    preparation = $(if ($artifactFailures.Count -gt 0) {
        'Failed: explicitly supplied input was missing or mismatched'
    } else { 'Pass: offline checks completed; not runtime/provider admission' })
    contracts = Read-RuntimeReceipt (Join-Path $output 'contracts\contracts.json')
    historicalReceiptLocaleContracts = 2
    hostComponents = $hostComponents
    profiles = $profiles; artifacts = $artifacts; commandAvailability = $tools
    toolchain = 'Required for later fixture execution: exact SDK 10.0.401, runtime 10.0.12, Windows x64. Command presence alone does not verify versions.'
    stages = @(Get-RuntimeValidationStages)
    copilotNativeLaunches = 0; traceSessionsStarted = 0; providerAccountOperations = 0; liveProviderCalls = 0
    installsOrPolicyChanges = 0; historicalEvidenceUnchanged = $true
    sourceReleasedByteEquivalence = 'Not claimed'; physicalTermination = 'Unknown / not measured'
    allNativePrivacy = 'Blocked / not proved'; productionEnabled = $false
}
Write-ProofJson $report (Join-Path $output 'readiness.json')
Write-ProofJson (Get-RuntimeOperatorRequests) (Join-Path $output 'operator-requests.json')
if ($artifactFailures.Count -gt 0) {
    throw 'Supplied artifact verification failed. Read the redacted readiness receipt; no native trial may proceed.'
}
Write-Warning 'Offline preparation PASS. Native trials, all-path RT2, PV1, R08 and model-assisted R13 remain BLOCKED. Exit 2 is the expected blocked-admission result, not a test failure.'
exit 2
