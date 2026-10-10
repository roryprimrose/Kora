#requires -Version 7.5
[CmdletBinding()]
param([Parameter(Mandatory)][string] $OutputDirectory)
. (Join-Path $PSScriptRoot 'NativeUxValidation.Common.ps1')
$OutputDirectory = Assert-NativeUxOutput $OutputDirectory
New-ProofDirectory $OutputDirectory
$passed = [Collections.Generic.List[string]]::new()
function Check {
    param([bool] $Condition, [string] $Name)
    if (!$Condition) { throw $Name }
    $passed.Add($Name)
}
function Reject {
    param([string] $Name, [scriptblock] $Action, [string] $Message)
    $failure = $null
    try { & $Action | Out-Null } catch { $failure = $_ }
    Check ($null -ne $failure -and $failure.Exception.Message.Contains($Message, [StringComparison]::Ordinal)) $Name
}
function New-CompletedObservations {
    $value = Get-NativeUxObservationTemplate | ConvertTo-Json -Depth 10 | ConvertFrom-Json -AsHashtable -DateKind String
    $value.operatorConfirmed = $true
    $value.settingsRestored = $true
    $value.narratorStoppedOrRestored = $true
    $value.display = @{ textScalePercent = 100; enlargedTextScalePercent = 150
        primaryDisplayScalePercent = 100; secondDisplayScalePercent = 150; monitorCount = 2 }
    foreach ($row in $value.observations) { $row.outcome = 'Pass'; $row.note = 'Synthetic contract observation, not a native trial.' }
    $value
}

$template = Get-NativeUxObservationTemplate
Check (@($template.observations | Where-Object outcome -CEQ 'Pending').Count -eq 5 -and
    !$template.operatorConfirmed -and !$template.settingsRestored -and !$template.narratorStoppedOrRestored) `
    'Preparation grants no native sign-off or restoration confirmation'
Reject 'Pending observations cannot be signed off' {
    $value = New-CompletedObservations
    $value.observations[0].outcome = 'Pending'
    Get-NativeUxSignOffStatus $value
} 'Every observation'
Check ((Get-NativeUxSignOffStatus (New-CompletedObservations)) -ceq 'ScopedPass') 'Complete operator observations remain scoped, not full qualification'
$value = New-CompletedObservations
$value.observations[4].outcome = 'Blocked'
$value.display.monitorCount = 1
$value.display.secondDisplayScalePercent = $null
Check ((Get-NativeUxSignOffStatus $value) -ceq 'Partial') 'An unavailable mixed-DPI rig stays Partial'
$value.observations[2].outcome = 'Blocked'
$value.display.enlargedTextScalePercent = $null
Check ((Get-NativeUxSignOffStatus $value) -ceq 'Partial') 'Unperformed text enlargement stays unknown rather than requiring an invented percentage'
$value.observations[0].outcome = 'Fail'
Check ((Get-NativeUxSignOffStatus $value) -ceq 'Failed') 'A failure takes precedence over blocked observations'
foreach ($field in 'operatorConfirmed', 'settingsRestored', 'narratorStoppedOrRestored') {
    Reject "$field must be an actual boolean confirmation" {
        $value = New-CompletedObservations
        $value[$field] = 'true'
        Get-NativeUxSignOffStatus $value
    } 'Confirm actual operator'
}
Reject 'Missing observation IDs are refused' {
    $value = New-CompletedObservations
    $value.observations = @($value.observations | Select-Object -Skip 1)
    Get-NativeUxSignOffStatus $value
} 'five unique'
Reject 'Duplicate observation IDs are refused' {
    $value = New-CompletedObservations
    $value.observations[0].id = 'SR02'
    Get-NativeUxSignOffStatus $value
} 'five unique'
Reject 'Unsupported outcomes are refused' {
    $value = New-CompletedObservations
    $value.observations[0].outcome = 'LooksFine'
    Get-NativeUxSignOffStatus $value
} 'Every observation'
Reject 'Empty observation notes are refused' {
    $value = New-CompletedObservations
    $value.observations[0].note = ' '
    Get-NativeUxSignOffStatus $value
} 'Every observation'
Reject 'Text-scale pass needs an actually larger setting' {
    $value = New-CompletedObservations
    $value.display.enlargedTextScalePercent = 100
    Get-NativeUxSignOffStatus $value
} 'actually larger'
Reject 'Text-scale pass needs a recorded enlarged percentage' {
    $value = New-CompletedObservations
    $value.display.enlargedTextScalePercent = $null
    Get-NativeUxSignOffStatus $value
} 'integer display'
Reject 'Mixed-DPI pass cannot be claimed on one monitor' {
    $value = New-CompletedObservations
    $value.display.monitorCount = 1
    Get-NativeUxSignOffStatus $value
} 'two physical monitors'
Reject 'Mixed-DPI pass cannot be claimed on equal scales' {
    $value = New-CompletedObservations
    $value.display.secondDisplayScalePercent = 100
    Get-NativeUxSignOffStatus $value
} 'two physical monitors'
Reject 'Missing primary display scale is refused' {
    $value = New-CompletedObservations
    $value.display.primaryDisplayScalePercent = $null
    Get-NativeUxSignOffStatus $value
} 'integer display'
Reject 'Negative display values are refused' {
    $value = New-CompletedObservations
    $value.display.monitorCount = -1
    Get-NativeUxSignOffStatus $value
} 'positive display'
Reject 'Relative output paths are refused' { Assert-NativeUxOutput '.' } 'absolute output path'
Reject 'Repository-local evidence is refused' { Assert-NativeUxOutput (Join-Path $PSScriptRoot '..') } 'outside the repository'
Reject 'Existing evidence is not overwritten' { New-ProofDirectory $OutputDirectory } 'overwrite existing'

$testRoot = Join-Path $OutputDirectory 'tests'
New-Item -ItemType Directory -Path $testRoot | Out-Null
$report = Join-Path $testRoot 'focused.trx'
function Write-SyntheticTrx {
    param([int] $Total = 6, [int] $Executed = 6, [int] $Passed = 6, [int] $Failed = 0, [switch] $OmitClass)
    $classes = @(Get-NativeUxTestClasses)
    if ($OmitClass) { $classes = @($classes | Select-Object -Skip 1) }
    $definitions = ($classes | ForEach-Object { "<UnitTest><TestMethod className=`"$_`" /></UnitTest>" }) -join ''
    [IO.File]::WriteAllText($report,
        "<TestRun xmlns=`"http://microsoft.com/schemas/VisualStudio/TeamTest/2010`"><TestDefinitions>$definitions</TestDefinitions><ResultSummary><Counters total=`"$Total`" executed=`"$Executed`" passed=`"$Passed`" failed=`"$Failed`" /></ResultSummary></TestRun>")
}
Write-SyntheticTrx
$summary = Get-NativeUxTestSummary $report
Check ($summary.passed -eq 6 -and $summary.skipped -eq 0) 'Actual counters and all six required classes are checked'
Reject 'Skipped tests cannot produce passing preparation' {
    Write-SyntheticTrx -Executed 5 -Passed 5
    Get-NativeUxTestSummary $report
} 'no skips'
Reject 'Failing tests cannot produce passing preparation' {
    Write-SyntheticTrx -Passed 5 -Failed 1
    Get-NativeUxTestSummary $report
} 'no skips'
Reject 'Empty reports cannot produce passing preparation' {
    Write-SyntheticTrx -Total 0 -Executed 0 -Passed 0
    Get-NativeUxTestSummary $report
} 'no skips'
Reject 'Missing selected class cannot produce passing preparation' {
    Write-SyntheticTrx -OmitClass
    Get-NativeUxTestSummary $report
} 'Required test class absent'

Write-SyntheticTrx
$payload = Join-Path $OutputDirectory 'payload'
New-Item -ItemType Directory -Path $payload | Out-Null
$binary = Join-Path $payload 'synthetic.bin'
[IO.File]::WriteAllText($binary, 'Never executable. Synthetic contract input.')
$receipt = @{ schema = 1; status = 'Pass'; nativeAcceptance = 'Pending'
    tests = Get-NativeUxTestSummary $report; payload = @(Get-PayloadFiles $payload) }
$receiptPath = Join-Path $OutputDirectory 'automated.json'
Write-ProofJson $receipt $receiptPath
$null = Assert-NativeUxBundle $OutputDirectory
Check $true 'Unchanged synthetic bundle validates without execution'
Reject 'Payload changes are refused before launch or sign-off' {
    [IO.File]::WriteAllText($binary, 'Changed synthetic bytes.')
    Assert-NativeUxBundle $OutputDirectory
} 'payload changed'
[IO.File]::WriteAllText($binary, 'Never executable. Synthetic contract input.')
Reject 'Test report changes are refused before launch or sign-off' {
    [IO.File]::AppendAllText($report, "`n")
    Assert-NativeUxBundle $OutputDirectory
} 'test report changed'
Write-SyntheticTrx
Reject 'Failed preparation cannot be used as native evidence' {
    $receipt.status = 'Failed'
    Write-ProofJson $receipt $receiptPath
    Assert-NativeUxBundle $OutputDirectory
} 'passing focus-free'
$receipt.status = 'Pass'
Write-ProofJson $receipt $receiptPath
Reject 'The runner does not launch without a separate explicit approval' {
    & (Join-Path $PSScriptRoot 'Invoke-NativeUxValidation.ps1') -Stage Launch -OutputDirectory $OutputDirectory
} 'Supply -ApproveInteractiveLaunch'
Reject 'Headless-only evidence cannot be signed off' {
    & (Join-Path $PSScriptRoot 'Invoke-NativeUxValidation.ps1') -Stage SignOff -OutputDirectory $OutputDirectory
} 'completed interactive trial'
$mechanics = Join-Path $PSScriptRoot 'Invoke-NativeUxMechanics.ps1'
$tokens = $null
$errors = $null
$null = [Management.Automation.Language.Parser]::ParseFile($mechanics, [ref]$tokens, [ref]$errors)
Check ($errors.Count -eq 0) 'Native mechanics script parses'
Reject 'Native desktop automation requires separate explicit approval' {
    & $mechanics -PreparedDirectory $OutputDirectory -OutputDirectory (Join-Path $OutputDirectory 'denied-mechanics') `
        -DeadlineUtc ([DateTimeOffset]::UtcNow.AddMinutes(5))
} 'Explicit -ApproveDesktopAutomation'
Reject 'Expired desktop approval cannot launch' {
    & $mechanics -PreparedDirectory $OutputDirectory -OutputDirectory (Join-Path $OutputDirectory 'expired-mechanics') `
        -DeadlineUtc ([DateTimeOffset]::UtcNow.AddMinutes(-1)) -ApproveDesktopAutomation
} 'future deadline'
Reject 'Unbounded desktop approval cannot launch' {
    & $mechanics -PreparedDirectory $OutputDirectory -OutputDirectory (Join-Path $OutputDirectory 'unbounded-mechanics') `
        -DeadlineUtc ([DateTimeOffset]::UtcNow.AddHours(2)) -ApproveDesktopAutomation
} 'future deadline'
& $mechanics -PreparedDirectory $OutputDirectory -OutputDirectory (Join-Path $OutputDirectory 'validate-mechanics') `
    -DeadlineUtc ([DateTimeOffset]::UtcNow.AddMinutes(5)) -ValidateOnly
Check (!(Test-Path -LiteralPath (Join-Path $OutputDirectory 'validate-mechanics')) -and
    !(Test-Path -LiteralPath (Join-Path $OutputDirectory 'denied-mechanics'))) 'ValidateOnly and denied automation create no native trial or output'
$workstation = Join-Path $PSScriptRoot 'Invoke-NativeUxWorkstation.ps1'
$null = [Management.Automation.Language.Parser]::ParseFile($workstation, [ref]$tokens, [ref]$errors)
Check ($errors.Count -eq 0) 'Expanded workstation driver parses'
Reject 'Expanded workstation automation needs its own explicit desktop approval' {
    & $workstation -PreparedDirectory $OutputDirectory -OutputDirectory (Join-Path $OutputDirectory 'denied-workstation') `
        -DeadlineUtc ([DateTimeOffset]::UtcNow.AddMinutes(5))
} 'Explicit desktop automation approval'
Reject 'Expanded workstation automation refuses an expired deadline before any baseline launch' {
    & $workstation -PreparedDirectory $OutputDirectory -OutputDirectory (Join-Path $OutputDirectory 'expired-workstation') `
        -DeadlineUtc ([DateTimeOffset]::UtcNow.AddMinutes(-1)) -ApproveDesktopAutomation
} 'future deadline'
Reject 'Expanded workstation automation refuses unbounded approval before any baseline launch' {
    & $workstation -PreparedDirectory $OutputDirectory -OutputDirectory (Join-Path $OutputDirectory 'unbounded-workstation') `
        -DeadlineUtc ([DateTimeOffset]::UtcNow.AddHours(2)) -ApproveDesktopAutomation
} 'future deadline'
& $workstation -PreparedDirectory $OutputDirectory -OutputDirectory (Join-Path $OutputDirectory 'validate-workstation') `
    -DeadlineUtc ([DateTimeOffset]::UtcNow.AddMinutes(5)) -ValidateOnly
Check (!(Test-Path -LiteralPath (Join-Path $OutputDirectory 'validate-workstation')) -and
    !(Test-Path -LiteralPath (Join-Path $OutputDirectory 'denied-workstation')) -and
    !(Test-Path -LiteralPath (Join-Path $OutputDirectory 'expired-workstation')) -and
    !(Test-Path -LiteralPath (Join-Path $OutputDirectory 'unbounded-workstation'))) 'Expanded validation and refused admission create no trial, baseline, process or input'
Write-ProofJson ([ordered]@{ schema = 1; count = $passed.Count; checks = @($passed)
    nativeLaunch = $false; scope = 'Synthetic file-only contracts, not native acceptance.' }) `
    (Join-Path $OutputDirectory 'contracts.json')
Write-Host "$($passed.Count) native UX validation contract checks passed; no native window launched."
