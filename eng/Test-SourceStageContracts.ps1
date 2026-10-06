#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $VerifiedOutput,
    [Parameter(Mandatory)][string] $OutputDirectory
)
. (Join-Path $PSScriptRoot 'SourceBootstrap.Common.ps1')
Assert-NoLinks $VerifiedOutput
New-ProofDirectory $OutputDirectory
$receipt = Read-SourceJson (Join-Path $VerifiedOutput 'source-build.json')
Assert-SourceStage $VerifiedOutput $receipt.root $receipt.checkout $receipt.repository $receipt.revision $receipt.deployment -RequireSmoke | Out-Null
$payload = Join-Path $OutputDirectory 'payload'
Copy-Item -LiteralPath (Join-Path $VerifiedOutput 'payload') -Destination $payload -Recurse
$originalInspection = Read-SourceJson (Join-Path $VerifiedOutput 'inspection\payload.json')
$inspectionPath = Join-Path $OutputDirectory 'inspection.json'
$passed = [Collections.Generic.List[string]]::new()
function Expect-InspectionFailure {
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
Write-ProofJson $originalInspection $inspectionPath
Assert-SourceInspection $payload $inspectionPath $receipt.revision $receipt.build.version
$passed.Add('Actual final output has coherent first-party versions, embedded resources and standard pinned SQLite')
foreach ($field in 'schema', 'inspectorVersion', 'inspectionProfile', 'repository', 'revision', 'rid') {
    $inspection = Read-SourceJson $inspectionPath
    $inspection[$field] = 'invalid'
    Write-ProofJson $inspection $inspectionPath
    Expect-InspectionFailure "Inspection $field mismatch refused" {
        Assert-SourceInspection $payload $inspectionPath $receipt.revision $receipt.build.version
    } 'mismatch'
    Write-ProofJson $originalInspection $inspectionPath
}
$inspection = Read-SourceJson $inspectionPath
$sqlite = @($inspection.nativeAssets | Where-Object { $_.published -ceq 'e_sqlite3.dll' })[0]
$sqlite.package = 'SQLitePCLRaw.lib.e_sqlite3/99.99.99'
Expect-InspectionFailure 'Pinned SQLite declaration must match selected checkout lock on initial build and rerun' {
    Assert-SourceSqliteLock $receipt.checkout $inspection
} 'selected revision dependency lock'
Expect-InspectionFailure 'Wrong published assembly version refused without loading assemblies' {
    Assert-SourceInspection $payload $inspectionPath $receipt.revision '9.9.9'
} 'Published version mismatch'
foreach ($file in 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'Microsoft.Data.Sqlite.dll',
    'SQLitePCLRaw.core.dll', 'SQLitePCLRaw.provider.e_sqlite3.dll', 'e_sqlite3.dll') {
    $path = Join-Path $payload $file
    [IO.File]::Move($path, (Join-Path $OutputDirectory 'held-file'))
    $inspection = Read-SourceJson $inspectionPath
    $inspection.files = @(Get-PayloadFiles $payload)
    Write-ProofJson $inspection $inspectionPath
    Expect-InspectionFailure "Missing launch-critical $file refused even with matching hash inventory" {
        Assert-SourceInspection $payload $inspectionPath $receipt.revision $receipt.build.version
    } 'Missing launch-critical'
    [IO.File]::Move((Join-Path $OutputDirectory 'held-file'), $path)
    Write-ProofJson $originalInspection $inspectionPath
}
$inspection = Read-SourceJson $inspectionPath
$sqlite = @($inspection.nativeAssets | Where-Object { $_.published -ceq 'e_sqlite3.dll' })[0]
$sqlite.package = 'custom-unpinned/1.0.0'
Write-ProofJson $inspection $inspectionPath
Expect-InspectionFailure 'Custom/ambient SQLite declaration refused' {
    Assert-SourceInspection $payload $inspectionPath $receipt.revision $receipt.build.version
} 'Standard SQLite'
Write-ProofJson $originalInspection $inspectionPath
$inspection = Read-SourceJson $inspectionPath
@($inspection.peFiles | Where-Object { $_.path -ceq 'Kora.dll' })[0].embeddedResources = @()
Write-ProofJson $inspection $inspectionPath
Expect-InspectionFailure 'Missing embedded-resource contract refused' {
    Assert-SourceInspection $payload $inspectionPath $receipt.revision $receipt.build.version
} 'Missing embedded Avalonia'
Write-ProofJson $originalInspection $inspectionPath
$inspection = Read-SourceJson $inspectionPath
$inspection.releaseAcceptance = 'passed'
Write-ProofJson $inspection $inspectionPath
Expect-InspectionFailure 'Static inspection cannot clear installed/release gates' {
    Assert-SourceInspection $payload $inspectionPath $receipt.revision $receipt.build.version
} 'must not claim release acceptance'
Write-ProofJson ([ordered]@{ schema = 1; count = $passed.Count; tests = @($passed); staticOnly = $true
    revision = $receipt.revision; originalReceiptSha256 = (Get-FileHash -LiteralPath (Join-Path $VerifiedOutput 'source-build.json')).Hash
    scope = 'Owned copies and synthetic inspection mutations. No application or native payload executed.' }) `
    (Join-Path $OutputDirectory 'tests.json')
Write-Host "$($passed.Count) source-stage inspection contract checks passed."
exit 0
