#requires -Version 7.5
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PreparedDirectory,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [Parameter(Mandatory)][DateTimeOffset] $DeadlineUtc,
    [switch] $ApproveDesktopAutomation,
    [switch] $ValidateOnly
)
. (Join-Path $PSScriptRoot 'NativeUxValidation.Common.ps1')
$retentionRoot = Assert-NativeUxOutput $OutputDirectory
$retentionPreparedRoot = Assert-NativeUxOutput $PreparedDirectory
$retentionDriverHash = (Get-FileHash -LiteralPath $PSCommandPath).Hash.ToLowerInvariant()
$baselineDriver = Join-Path $PSScriptRoot 'Invoke-NativeUxMechanics.ps1'
& $baselineDriver -PreparedDirectory $retentionPreparedRoot -OutputDirectory $retentionRoot `
    -DeadlineUtc $DeadlineUtc -ValidateOnly
if ($ValidateOnly) { return }
if (!$ApproveDesktopAutomation) { throw 'Explicit desktop automation approval is required for the retention trial.' }
New-ProofDirectory $retentionRoot
# Reuse the established PID/HWND, foreground keyboard, deadline and receipt guards.
. $baselineDriver -PreparedDirectory $retentionPreparedRoot -OutputDirectory (Join-Path $retentionRoot 'baseline') `
    -DeadlineUtc $DeadlineUtc -ApproveDesktopAutomation
$originalOwnedWindowDiscovery = (Get-Command Get-OwnedWindows).ScriptBlock
$originalNamedElementDiscovery = (Get-Command Get-NamedElement).ScriptBlock
$OutputDirectory = Join-Path $retentionRoot 'retention'
New-ProofDirectory $OutputDirectory
$scratch = Join-Path $OutputDirectory 'scratch'
New-Item -ItemType Directory -Path $scratch | Out-Null
$prepared = Assert-NativeUxBundle $retentionPreparedRoot
$process = [Diagnostics.Process]::new()
$rows = [Collections.Generic.List[object]]::new()
$receipt = [ordered]@{
    schema = 1; status = 'Running'; startedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    deadlineUtc = $DeadlineUtc.ToUniversalTime().ToString('O'); driverSha256 = $retentionDriverHash
    baselineDriverSha256 = (Get-FileHash -LiteralPath $baselineDriver).Hash.ToLowerInvariant()
    preparationSha256 = (Get-FileHash -LiteralPath (Join-Path $retentionPreparedRoot 'automated.json')).Hash.ToLowerInvariant()
    source = $prepared.source; processId = $null; readyObserved = $false
    providerReadinessDeferrals = 0; retiredWindowElements = 0; retiredDescendantElements = 0
    exitCode = $null; scratchCleaned = $false; forcedTermination = $false; rows = @()
    scope = 'Exact owned native scratch retention inspection/review/hold controls and refusal mechanics. No timer cleanup, power-loss, forensic erasure, Narrator, audio, normal profile, installation or real OS transition qualification.'
}
$script:fixturePid = 0
$script:launcher = $null
$script:retentionWindow = $null
$script:retentionA = $null
$script:retentionB = $null

function Get-OwnedWindows {
    $discoveryUntil = [DateTimeOffset]::UtcNow.AddSeconds(2)
    do {
        Assert-TrialTime
        try { return @(& $originalOwnedWindowDiscovery) }
        catch [Management.Automation.MethodInvocationException] {
            if ($_.Exception.InnerException -isnot [Windows.Automation.ElementNotAvailableException]) { throw }
            $receipt.retiredWindowElements++
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $discoveryUntil)
    throw 'Owned HWND observation did not stabilize; no retention action was retried.'
}
function Get-NamedElement {
    param([Windows.Automation.AutomationElement] $Root, [string] $Name, [switch] $Prefix)
    $discoveryUntil = [DateTimeOffset]::UtcNow.AddSeconds(2)
    do {
        Assert-TrialTime
        try { return & $originalNamedElementDiscovery -Root $Root -Name $Name -Prefix:$Prefix }
        catch [Management.Automation.MethodInvocationException] {
            if ($_.Exception.InnerException -isnot [Windows.Automation.ElementNotAvailableException]) { throw }
            $receipt.retiredDescendantElements++
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $discoveryUntil)
    throw "Owned retention observation did not stabilize for '$Name'; no action was retried."
}
function Retention-Control {
    param([string] $Id)
    Assert-OwnedElement $script:retentionWindow
    $matches = @($script:retentionWindow.FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)))
    if ($matches.Count -ne 1) { throw "Expected one owned retention control '$Id', observed $($matches.Count)." }
    Assert-OwnedElement $matches[0]
    $matches[0]
}
function Invoke-RetentionControl {
    param([string] $Id)
    $null = Wait-Native { (Retention-Control $Id).Current.IsEnabled } "enabled exact retention control $Id"
    $control = Retention-Control $Id
    Check-Native (![string]::IsNullOrWhiteSpace($control.Current.Name)) "Retention control $Id must have a native name."
    Invoke-Named $script:retentionWindow $control.Current.Name
}
function Retention-Text {
    (Get-NamedElement $script:retentionWindow `
        'Exact selected session retention UTC clocks, kept and purged state; passive observation only' -Prefix).Current.Name
}
function Retention-Preview {
    (Get-NamedElement $script:retentionWindow `
        'Exact retention confirmation; ordinary policy may already be due; session hold is not an operation grant' -Prefix).Current.Name
}
function Read-RetentionObservation {
    $text = Retention-Text
    if ($text -cnotmatch 'Exact session ([a-f0-9-]{36}) \| generation ([0-9]+) \| exemption audit revision ([0-9]+)') {
        throw 'Native retention status has no exact immutable identity and audit revision.'
    }
    $state = [ordered]@{ sessionId = $Matches[1]; generation = [long]$Matches[2]; auditRevision = [long]$Matches[3] }
    foreach ($label in 'Last meaningful activity UTC', 'Recorded archive due UTC', 'Recorded delete due UTC') {
        if ($text -cnotmatch ([regex]::Escape($label) + ': ([^\r\n]+)')) { throw "Native retention status lacks $label." }
        $value = $Matches[1]
        $parsed = [DateTimeOffset]::Parse($value, [Globalization.CultureInfo]::InvariantCulture)
        Check-Native ($parsed.Offset -eq [TimeSpan]::Zero) "The recorded $label must be UTC."
        $state[$label] = $value
    }
    if ($text -cnotmatch 'Kept: (True|False) \| purged: (True|False) \| removed: (True|False)') {
        throw 'Native retention state lacks explicit kept/purged/removed values.'
    }
    $state.kept = $Matches[1] -ceq 'True'
    $state.purged = $Matches[2] -ceq 'True'
    $state.removed = $Matches[3] -ceq 'True'
    $state.workHoldObserved = $text.Contains('Observed hold: live/Unknown work', [StringComparison]::Ordinal)
    $state
}
function Assert-RetentionUnchanged {
    param($Before, $After, [switch] $AllowHoldChange)
    foreach ($field in 'sessionId', 'generation', 'Last meaningful activity UTC', 'Recorded archive due UTC',
        'Recorded delete due UTC', 'purged', 'removed') {
        Check-Native ($Before[$field] -ceq $After[$field]) "Retention control must preserve exact $field."
    }
    if (!$AllowHoldChange) {
        foreach ($field in 'auditRevision', 'kept', 'workHoldObserved') {
            Check-Native ($Before[$field] -ceq $After[$field]) "Passive/refused retention work must preserve $field."
        }
    }
}
function Open-RetentionWindow {
    $script:retentionWindow = Open-Window 'Sessions (scratch IDs, guarded Done/resume)' `
        'Sessions - authoritative work and durable history'
    $header = Retention-Control 'ExpanderHeader'
    Check-Native ($header.Current.Name -ceq 'Selected-session retention: status and exact review') 'Retention header must have its exact native label.'
    $pattern = [Windows.Automation.TogglePattern]$header.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    if ($pattern.Current.ToggleState -eq [Windows.Automation.ToggleState]::Off) { $pattern.Toggle() }
    $null = Wait-Native { $pattern.Current.ToggleState -eq [Windows.Automation.ToggleState]::On } 'expanded native retention header'
}
function Read-ExactRetention {
    param([string] $Id)
    $field = Get-NamedElement $script:retentionWindow 'Exact immutable session ID for passive history; names do not select sessions'
    $value = [Windows.Automation.ValuePattern]$field.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
    $value.SetValue($Id)
    $null = Wait-Native { $value.Current.Value -ceq $Id } 'canonical exact retention subject'
    Invoke-RetentionControl 'ReadExactRetention'
    $null = Wait-Native {
        (Retention-Text).Contains("Exact session $Id |", [StringComparison]::Ordinal) -and
        (Get-NamedElement $script:retentionWindow 'Sessions status and unavailable reasons' -Prefix).Current.Name.Contains(
            'Exact-ID retention metadata only', [StringComparison]::Ordinal) -and
        (Retention-Control 'RefreshRetention').Current.IsEnabled
    } 'fresh exact-ID metadata-only retention read'
    Read-RetentionObservation
}
function Create-RetentionSession {
    param([string] $Name)
    $previousStatus = (Get-NamedElement $script:retentionWindow 'Sessions status and unavailable reasons' -Prefix).Current.Name
    $field = Get-NamedElement $script:retentionWindow 'Session name draft; names never resolve authority'
    ([Windows.Automation.ValuePattern]$field.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)).SetValue($Name)
    Invoke-RetentionControl 'CreateSession'
    Wait-Native {
        $text = (Get-NamedElement $script:retentionWindow 'Sessions status and unavailable reasons' -Prefix).Current.Name
        if ($text -cne $previousStatus -and $text -cmatch 'Committed empty Active session ([a-f0-9-]{36})\.') { $Matches[1] }
    } 'new synthetic retention subject'
}
function Confirm-Retention {
    param([bool] $Keep, $Before)
    Invoke-RetentionControl $(if ($Keep) { 'KeepSession' } else { 'OrdinaryRetention' })
    $preview = Retention-Preview
    Check-Native ($preview.Contains("Exact session $($Before.sessionId) |", [StringComparison]::Ordinal)) 'Review must bind the exact subject.'
    if (!$Keep) {
        Check-Native ($preview.Contains('background maintenance may archive or delete soon', [StringComparison]::Ordinal) -and
            $preview.Contains('No clock reset or apply-now cleanup', [StringComparison]::Ordinal)) 'Ordinary retention must disclose deferred cleanup without clock reset.'
    }
    Invoke-RetentionControl 'ConfirmRetention'
    $expected = 'Kept: ' + $Keep.ToString()
    $null = Wait-Native {
        (Retention-Text).Contains($expected, [StringComparison]::Ordinal) -and
        (Get-NamedElement $script:retentionWindow 'Sessions status and unavailable reasons' -Prefix).Current.Name.Contains(
            'readback committed', [StringComparison]::Ordinal) -and
        !(Retention-Control 'ConfirmRetention').Current.IsEnabled
    } 'committed exact retention readback'
    $after = Read-RetentionObservation
    Assert-RetentionUnchanged $Before $after -AllowHoldChange
    Check-Native ($after.kept -eq $Keep -and $after.auditRevision -gt $Before.auditRevision) 'Hold commit needs exact value and a newer audit revision.'
    $after
}

try {
    $process.StartInfo.FileName = Join-Path $retentionPreparedRoot 'payload\Kora.NativeUxFixture.exe'
    $process.StartInfo.WorkingDirectory = Join-Path $retentionPreparedRoot 'payload'
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    foreach ($argument in '--launch-native-fixtures', '--scratch-parent', $scratch) { $process.StartInfo.ArgumentList.Add($argument) }
    if (!$process.Start()) { throw 'The scratch retention fixture did not start.' }
    $receipt.processId = $process.Id
    $script:fixturePid = $process.Id
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $script:launcher = Wait-Native {
        $found = @(Get-OwnedWindows | Where-Object { $_.Current.Name -cin @(
            'Kora synthetic native UX fixture launcher', 'Kora SYNTHETIC native UX fixture - not the production host') })
        if ($found.Count -eq 1) { $found[0] }
    } 'owned retention launcher'
    $null = Wait-Native { (Get-NamedElement $script:launcher 'Native fixture status' -Prefix).Current.Name.Contains('READY:', [StringComparison]::Ordinal) } 'READY retention fixture' -Seconds 30
    $receipt.readyObserved = $true

    Trial 'T01-passive-exact-retention-and-isolation' {
        Open-RetentionWindow
        $script:retentionA = Create-RetentionSession 'Synthetic retention subject A'
        $script:retentionB = Create-RetentionSession 'Synthetic retention subject B'
        Check-Native ($script:retentionA -cne $script:retentionB) 'Retention subjects must have different immutable IDs.'
        $script:retentionBeforeA = Read-ExactRetention $script:retentionA
        $script:retentionBeforeB = Read-ExactRetention $script:retentionB
        foreach ($id in $script:retentionA, $script:retentionB, $script:retentionA) {
            $state = Read-ExactRetention $id
            Assert-RetentionUnchanged $(if ($id -ceq $script:retentionA) { $script:retentionBeforeA } else { $script:retentionBeforeB }) $state
            Check-Native (!(Retention-Control 'Done').Current.IsEnabled -and !(Retention-Control 'Resume').Current.IsEnabled) 'Exact metadata reads cannot create selected lifecycle authority.'
        }
        @{ subjectA = $script:retentionBeforeA; subjectB = $script:retentionBeforeB; passiveReads = 5; implicitLifecycleAuthority = $false }
    }
    Trial 'T02-preview-cancel-does-not-commit' {
        Invoke-RetentionControl 'KeepSession'
        Check-Native ((Retention-Preview).Contains("Exact session $script:retentionA |", [StringComparison]::Ordinal) -and
            (Retention-Control 'ConfirmRetention').Current.IsEnabled) 'Keep preview must disclose its exact identity before confirmation.'
        Assert-RetentionUnchanged $script:retentionBeforeA (Read-RetentionObservation)
        Invoke-RetentionControl 'CancelRetentionReview'
        $null = Wait-Native { !(Retention-Control 'ConfirmRetention').Current.IsEnabled } 'cancelled retention confirmation disabled'
        Check-Native (!(Retention-Preview).Contains($script:retentionA, [StringComparison]::Ordinal)) 'Cancelled review must remove its exact subject.'
        Assert-RetentionUnchanged $script:retentionBeforeA (Read-ExactRetention $script:retentionA)
        @{ previewOnly = $true; cancelled = $true; auditRevisionUnchanged = $true }
    }
    Trial 'T03-keep-commit-and-window-reopen' {
        $script:retentionKeptA = Confirm-Retention $true $script:retentionBeforeA
        Close-OwnedWindow $script:retentionWindow
        Open-RetentionWindow
        Assert-RetentionUnchanged $script:retentionKeptA (Read-ExactRetention $script:retentionA)
        Assert-RetentionUnchanged $script:retentionBeforeB (Read-ExactRetention $script:retentionB)
        $null = Read-ExactRetention $script:retentionA
        @{ before = $script:retentionBeforeA; after = $script:retentionKeptA; freshWindowReadback = $true; processRestartTest = $false; unrelatedUnchanged = $true }
    }
    Trial 'T04-subject-and-refresh-revoke-review' {
        Invoke-RetentionControl 'OrdinaryRetention'
        $null = Read-ExactRetention $script:retentionB
        Check-Native (!(Retention-Control 'ConfirmRetention').Current.IsEnabled -and
            !(Retention-Preview).Contains($script:retentionA, [StringComparison]::Ordinal)) 'Changing the exact subject must revoke the previous retention review.'
        Assert-RetentionUnchanged $script:retentionBeforeB (Read-RetentionObservation)
        $null = Read-ExactRetention $script:retentionA
        Invoke-RetentionControl 'OrdinaryRetention'
        Invoke-RetentionControl 'RefreshRetention'
        $null = Wait-Native { !(Retention-Control 'ConfirmRetention').Current.IsEnabled -and
            (Retention-Control 'OrdinaryRetention').Current.IsEnabled } 'refresh revokes pending retention review'
        Assert-RetentionUnchanged $script:retentionKeptA (Read-RetentionObservation)
        @{ subjectChangeRevoked = $true; refreshRevoked = $true; staleConfirmationInvocations = 0; holdAndAuditUnchanged = $true }
    }
    Trial 'T05-ordinary-commit-preserves-clocks-and-other-session' {
        $script:retentionOrdinaryA = Confirm-Retention $false $script:retentionKeptA
        Check-Native (!$script:retentionOrdinaryA.kept -and (Retention-Control 'KeepSession').Current.IsEnabled) 'Ordinary retention must restore its explicit non-kept state.'
        Assert-RetentionUnchanged $script:retentionBeforeB (Read-ExactRetention $script:retentionB)
        $null = Read-ExactRetention $script:retentionA
        @{ before = $script:retentionKeptA; after = $script:retentionOrdinaryA; clockReset = $false; immediateCleanup = $false; unrelatedUnchanged = $true }
    }
    Trial 'T06-live-work-hold-is-observation-not-cleanup' {
        $null = Read-ExactRetention $script:retentionA
        Invoke-Named $script:launcher 'Open synthetic stale-revision question'
        $null = Wait-Native { (Get-NamedElement $script:launcher 'Native fixture status' -Prefix).Current.Name.Contains('Opened exact synthetic question', [StringComparison]::Ordinal) } 'actual unresolved synthetic question'
        $questionWindow = Wait-Native {
            $found = @(Get-OwnedWindows | Where-Object { $_.Current.NativeWindowHandle -ne $script:launcher.Current.NativeWindowHandle -and
                $_.Current.NativeWindowHandle -ne $script:retentionWindow.Current.NativeWindowHandle })
            if ($found.Count -eq 1) { $found[0] }
        } 'owned unresolved-question window'
        Close-OwnedWindow $questionWindow
        Invoke-RetentionControl 'Refresh'
        $null = Wait-Native { (Retention-Control 'Refresh').Current.IsEnabled } 'completed bounded session page refresh'
        $list = Retention-Control 'Records'
        $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $scroll = $null
        $hasScroll = $list.TryGetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern, [ref]$scroll)
        foreach ($percent in 0, 25, 50, 75, 100) {
            Assert-OwnedElement $list
            if ($hasScroll -and $scroll.Current.VerticallyScrollable) {
                $scroll.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, $percent)
                Start-Sleep -Milliseconds 150
            }
            $items = @($list.FindAll([Windows.Automation.TreeScope]::Descendants,
                [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem)))
            foreach ($item in $items) {
                Assert-OwnedElement $item
                foreach ($match in [regex]::Matches($item.Current.Name, '[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}')) { $null = $ids.Add($match.Value) }
            }
        }
        Check-Native ($ids.Count -gt 0) 'Hold observation must use actual exact synthetic session IDs.'
        $held = [Collections.Generic.List[object]]::new()
        foreach ($id in $ids) {
            $observation = Read-ExactRetention $id
            if ($observation.workHoldObserved) { $held.Add($observation) }
        }
        Check-Native ($held.Count -gt 0) 'At least one real live/unresolved scratch-work hold must be observed.'
        Assert-RetentionUnchanged $script:retentionOrdinaryA (Read-ExactRetention $script:retentionA)
        Assert-RetentionUnchanged $script:retentionBeforeB (Read-ExactRetention $script:retentionB)
        @{ exactHeldSubjects = @($held); observationOnly = $true; timerCleanupInvocations = 0; queueDispatchInvocations = 0; forensicErasureClaim = $false }
    }
    Trial 'T07-synthetic-gate-revokes-uncommitted-retention' {
        $null = Read-ExactRetention $script:retentionA
        Invoke-RetentionControl 'KeepSession'
        Check-Native ((Retention-Control 'ConfirmRetention').Current.IsEnabled) 'Gate trial must begin with an actual uncommitted exact review.'
        $oldHwnd = $script:retentionWindow.Current.NativeWindowHandle
        Invoke-Named $script:launcher 'Close synthetic privacy/ownership gate'
        $null = Wait-Native { @(Get-OwnedWindows | Where-Object { $_.Current.NativeWindowHandle -eq $oldHwnd }).Count -eq 0 } 'closed retained review HWND after synthetic gate'
        Invoke-Named $script:launcher 'Reopen synthetic gate (no audio recovery)'
        $null = Wait-Native { (Get-NamedElement $script:launcher 'Native fixture status' -Prefix).Current.Name.Contains('Synthetic gate reopened', [StringComparison]::Ordinal) } 'fresh synthetic gate reopen'
        Open-RetentionWindow
        Assert-RetentionUnchanged $script:retentionOrdinaryA (Read-ExactRetention $script:retentionA)
        Check-Native (!(Retention-Control 'ConfirmRetention').Current.IsEnabled) 'Reopening cannot resurrect a prior retention approval.'
        Assert-RetentionUnchanged $script:retentionBeforeB (Read-ExactRetention $script:retentionB)
        @{ revokedHwnd = $oldHwnd; exactUncommittedStatePreserved = $true; replayedConfirmation = $false; realOsTransition = $false }
    }

    Invoke-Named $script:launcher 'Stop native fixture and clean up scratch state'
    if (!$process.WaitForExit(15000)) { throw 'Retention fixture did not stop within fifteen seconds.' }
    $receipt.exitCode = $process.ExitCode
    $receipt.scratchCleaned = @(Get-ChildItem -LiteralPath $scratch -Force).Count -eq 0
    Check-Native ($receipt.exitCode -eq 0 -and $receipt.scratchCleaned -and $stderr.GetAwaiter().GetResult().Length -eq 0) 'Retention trial needs exit 0, empty stderr and exact scratch cleanup.'
    $null = Assert-NativeUxBundle $retentionPreparedRoot
    $receipt.status = 'ScopedPass'
}
catch {
    $receipt.status = 'Failed'
    $receipt.failure = $_.Exception.Message
    $receipt.scriptStack = $_.ScriptStackTrace
    throw
}
finally {
    if ($null -ne $receipt.processId -and !$process.HasExited) {
        try {
            if ($null -ne $script:launcher) {
                $stop = $script:launcher.FindFirst([Windows.Automation.TreeScope]::Descendants,
                    [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Stop native fixture and clean up scratch state'))
                if ($null -ne $stop -and $stop.Current.ProcessId -eq $receipt.processId) {
                    ([Windows.Automation.InvokePattern]$stop.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
                }
            }
            if (!$process.WaitForExit(15000)) { $receipt.forcedTermination = $true; Stop-Process -Id $receipt.processId -ErrorAction Stop; $process.WaitForExit() }
        }
        catch {
            $receipt.status = 'Failed'
            $receipt.cleanupFailure = $_.Exception.Message
            if (!$process.HasExited) { $receipt.forcedTermination = $true; Stop-Process -Id $receipt.processId -ErrorAction Stop; $process.WaitForExit() }
        }
        $receipt.exitCode = $process.ExitCode
        $receipt.scratchCleaned = @(Get-ChildItem -LiteralPath $scratch -Force).Count -eq 0
    }
    if ($null -ne $receipt.processId) {
        [IO.File]::WriteAllText((Join-Path $OutputDirectory 'stdout.local.txt'), $stdout.GetAwaiter().GetResult())
        [IO.File]::WriteAllText((Join-Path $OutputDirectory 'stderr.local.txt'), $stderr.GetAwaiter().GetResult())
    }
    $receipt.rows = @($rows)
    $receipt.completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Write-ProofJson $receipt (Join-Path $OutputDirectory 'retention.json')
    $process.Dispose()
}
Write-Host "Native retention $($receipt.status): $($rows.Count) scoped rows; no human or protected acceptance inferred."
