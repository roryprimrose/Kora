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
Assert-NativeUxProfile
$preparedRoot = Assert-NativeUxOutput $PreparedDirectory
$trialRoot = Assert-NativeUxOutput $OutputDirectory
$prepared = Assert-NativeUxBundle $preparedRoot
$driverHash = (Get-FileHash -LiteralPath $PSCommandPath).Hash.ToLowerInvariant()
$baselineDriver = Join-Path $PSScriptRoot 'Invoke-NativeUxMechanics.ps1'
& $baselineDriver -PreparedDirectory $preparedRoot -OutputDirectory $trialRoot -DeadlineUtc $DeadlineUtc -ValidateOnly
if ($ValidateOnly) { return }
if (!$ApproveDesktopAutomation) {
    throw 'Explicit desktop automation approval is required for the bounded All Work native trial.'
}
$source = Get-NativeUxSourceIdentity ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')))
if ($source.revision -cne $prepared.source.revision -or $source.buildInputSha256 -cne $prepared.source.buildInputSha256) {
    throw 'The frozen All Work worktree changed after preparation.'
}
. $baselineDriver -PreparedDirectory $preparedRoot -OutputDirectory $trialRoot -DeadlineUtc $DeadlineUtc -LoadHelpersOnly
$originalNamedElementDiscovery = (Get-Command Get-NamedElement).ScriptBlock
New-ProofDirectory $trialRoot
$OutputDirectory = $trialRoot
$scratch = Join-Path $trialRoot 'scratch'
New-Item -ItemType Directory -Path $scratch | Out-Null
$process = [Diagnostics.Process]::new()
$rows = [Collections.Generic.List[object]]::new()
$receipt = [ordered]@{
    schema = 1; status = 'Running'; startedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    deadlineUtc = $DeadlineUtc.ToUniversalTime().ToString('O'); driverSha256 = $driverHash
    mechanicsSha256 = (Get-FileHash -LiteralPath $baselineDriver).Hash.ToLowerInvariant()
    preparationSha256 = (Get-FileHash -LiteralPath (Join-Path $preparedRoot 'automated.json')).Hash.ToLowerInvariant()
    source = $source; processId = $null; readyObserved = $false; exitCode = $null
    providerReadinessDeferrals = 0; retiredWindowHandles = 0; forcedTermination = $false
    providerDiscoveryRetries = 0
    scratchCleaned = $false; rows = @()
    scope = 'Scratch-only PID-owned All Work native paging, passive selection/navigation, filter and privacy mechanics. No production profile, execution, dispatch, audio, network, installed or human accessibility acceptance.'
}
$script:fixturePid = 0
$script:launcher = $null
$script:sessionsWindow = $null
$script:allWorkWindow = $null
$stopAttempted = $false

function Get-NamedElement {
    param([Windows.Automation.AutomationElement] $Root, [string] $Name, [switch] $Prefix)
    $until = [DateTimeOffset]::UtcNow.AddSeconds(2)
    do {
        Assert-OwnedElement $Root
        try { return & $originalNamedElementDiscovery -Root $Root -Name $Name -Prefix:$Prefix }
        catch [Management.Automation.MethodInvocationException] {
            if ($_.Exception.InnerException -isnot [Runtime.InteropServices.COMException] -and
                $_.Exception.InnerException -isnot [Windows.Automation.ElementNotAvailableException]) { throw }
            $receipt.providerDiscoveryRetries++
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $until)
    throw "Owned passive UIA discovery did not stabilize for '$Name'; no action was retried."
}
function Work-List {
    Get-NamedElement $script:allWorkWindow 'Independent work grouped by exact immutable session; selection is passive and grants nothing'
}
function Get-WorkIdElement {
    param([string] $Id)
    Assert-OwnedElement $script:allWorkWindow
    $found = @($script:allWorkWindow.FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)))
    if ($found.Count -ne 1) { throw "Expected one exact All Work automation ID '$Id', observed $($found.Count)." }
    Assert-OwnedElement $found[0]
    $found[0]
}
function Invoke-WorkId {
    param([string] $Id)
    $element = Get-WorkIdElement $Id
    $receipt.lastAction = $Id
    [KoraFixtureKeyboard]::Activate($script:fixturePid, $script:allWorkWindow.Current.NativeWindowHandle)
    $element.SetFocus()
    $null = Wait-Native {
        $focused = [Windows.Automation.AutomationElement]::FocusedElement
        $focused.Current.ProcessId -eq $script:fixturePid -and
            ($focused.GetRuntimeId() -join ',') -ceq ($element.GetRuntimeId() -join ',')
    } "exact focus before All Work $Id"
    if (!$element.Current.IsEnabled) { throw "All Work native action is disabled: $Id" }
    [KoraFixtureKeyboard]::Space($script:fixturePid, $script:allWorkWindow.Current.NativeWindowHandle)
}
function Work-Items {
    $list = Work-List
    $until = [DateTimeOffset]::UtcNow.AddSeconds(2)
    do {
        Assert-OwnedElement $list
        try {
            return @($list.FindAll([Windows.Automation.TreeScope]::Descendants,
                [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [Windows.Automation.ControlType]::ListItem)))
        }
        catch [Management.Automation.MethodInvocationException] {
            if ($_.Exception.InnerException -isnot [Runtime.InteropServices.COMException] -and
                $_.Exception.InnerException -isnot [Windows.Automation.ElementNotAvailableException]) { throw }
            $receipt.providerDiscoveryRetries++
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $until)
    throw 'Owned passive All Work list discovery did not stabilize; no action was retried.'
}
function Work-ItemText {
    param([Windows.Automation.AutomationElement] $Item)
    Assert-OwnedElement $Item
    @($Item.Current.Name) + @($Item.FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,
            [Windows.Automation.ControlType]::Text)) | ForEach-Object { $_.Current.Name }) -join "`n"
}
function Work-Ids {
    @(
        foreach ($item in Work-Items) {
            $match = [regex]::Match((Work-ItemText $item), '[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}',
                [Text.RegularExpressions.RegexOptions]::IgnoreCase)
            if (!$match.Success) { throw 'Every All Work native row must disclose one exact immutable session ID.' }
            $match.Value.ToLowerInvariant()
        }
    )
}
function Work-Status {
    $label = 'Bounded overview scope, scanned and omission markers; no global totals or all-clear'
    $name = (Get-NamedElement $script:allWorkWindow $label -Prefix).Current.Name
    if (!$name.StartsWith("$label`: ", [StringComparison]::Ordinal)) {
        throw 'The All Work status peer did not retain its descriptive accessible label.'
    }
    $name.Substring($label.Length + 2)
}
function Wait-WorkPage {
    $null = Wait-Native {
        (Work-Status).StartsWith('This page:', [StringComparison]::Ordinal) -and @(Work-Items).Count -gt 0
    } 'bounded All Work page'
}
function Open-AllWork {
    $script:sessionsWindow = Open-Window 'Sessions (scratch IDs, guarded Done/resume)' 'Sessions - authoritative work and durable history'
    Invoke-Named $script:sessionsWindow 'Open passive bounded cross-session work observations; opening grants nothing and does not resume or dispatch'
    $script:allWorkWindow = Wait-Native {
        $found = @(Get-OwnedWindows | Where-Object { $_.Current.Name -ceq 'All work / attention overview - passive observations' })
        if ($found.Count -eq 1) { $found[0] }
        elseif ($found.Count -gt 1) { throw 'Ambiguous owned All Work window.' }
    } 'owned All Work window'
}

try {
    $process.StartInfo.FileName = Join-Path $preparedRoot 'payload\Kora.NativeUxFixture.exe'
    $process.StartInfo.WorkingDirectory = Join-Path $preparedRoot 'payload'
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    foreach ($argument in '--launch-native-fixtures', '--scratch-parent', $scratch, '--all-work-fixture') {
        $process.StartInfo.ArgumentList.Add($argument)
    }
    if ([DateTimeOffset]::UtcNow -ge $DeadlineUtc) { throw 'Desktop deadline expired before launch.' }
    if (!$process.Start()) { throw 'The scratch All Work fixture did not start.' }
    $receipt.processId = $process.Id
    $script:fixturePid = $process.Id
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $script:launcher = Wait-Native {
        $found = @(Get-OwnedWindows | Where-Object { $_.Current.Name -cin @(
            'Kora synthetic native UX fixture launcher', 'Kora SYNTHETIC native UX fixture - not the production host') })
        if ($found.Count -eq 1) { $found[0] }
    } 'owned All Work launcher'
    $null = Wait-Native {
        (Get-NamedElement $script:launcher 'Native fixture status' -Prefix).Current.Name.Contains('READY:', [StringComparison]::Ordinal)
    } 'scratch All Work initialization' -Seconds 120
    $receipt.readyObserved = $true
    Open-AllWork

    Trial 'AW01-passive-open-and-bounded-refresh' {
        Check-Native (@(Work-Items).Count -eq 0) 'Opening All Work must not collect automatically.'
        Check-Native ((Work-Status).StartsWith('Refresh manually', [StringComparison]::Ordinal)) 'Opening must disclose manual bounded collection.'
        Invoke-WorkId 'Refresh'
        Wait-WorkPage
        $ids = @(Work-Ids)
        Check-Native ($ids.Count -gt 0 -and $ids.Count -le 10 -and
            @($ids | Sort-Object -Unique).Count -eq $ids.Count) 'The first All Work page must contain a nonempty bounded set of unique exact sessions.'
        Check-Native ((Get-WorkIdElement 'Next').Current.IsEnabled) 'The bounded first page must expose its continuation.'
        Check-Native (!(Get-NamedElement $script:allWorkWindow 'Navigate this immutable session ID through Sessions private workspace admission; no resume, dispatch, cancel, enqueue or question retarget').Current.IsEnabled) 'Passive refresh cannot navigate without exact selection.'
        @{ sessionIds = $ids; boundedCount = $ids.Count; automaticCollection = $false; continuation = $true }
    }
    $firstIds = @(Work-Ids)
    Trial 'AW02-selection-details-and-fresh-navigation' {
        $item = @(Work-Items | Where-Object { (Work-ItemText $_).Contains('Observed', [StringComparison]::Ordinal) })[0]
        $selection = [Windows.Automation.SelectionItemPattern]$item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)
        $selection.Select()
        $null = Wait-Native {
            (Get-NamedElement $script:allWorkWindow 'Exact observation provenance, stored lifecycle, queue/task states, deadlines and pending-question metadata; no bodies or authority' -Prefix).Current.Name.Contains('Exact session ', [StringComparison]::Ordinal)
        } 'complete exact selected All Work details'
        $detail = (Get-NamedElement $script:allWorkWindow 'Exact observation provenance, stored lifecycle, queue/task states, deadlines and pending-question metadata; no bodies or authority' -Prefix).Current.Name
        $selectedId = ([regex]::Match($detail, '[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}',
            [Text.RegularExpressions.RegexOptions]::IgnoreCase)).Value.ToLowerInvariant()
        Check-Native ($selectedId -in $firstIds) 'Selected detail must retain a current exact page identity.'
        Invoke-Named $script:allWorkWindow 'Navigate this immutable session ID through Sessions private workspace admission; no resume, dispatch, cancel, enqueue or question retarget'
        $null = Wait-Native {
            (Work-Status).StartsWith('Fresh passive exact-session navigation', [StringComparison]::Ordinal)
        } 'fresh passive exact-session navigation'
        Check-Native (@(Get-OwnedWindows | Where-Object { $_.Current.Name -ceq 'Sessions - authoritative work and durable history' }).Count -eq 1) 'Navigation must reuse one owned Sessions window.'
        @{ selectedSessionId = $selectedId; freshPassiveNavigation = $true; authorityRenewed = $false }
    }
    Trial 'AW03-next-page-is-disjoint-and-bounded' {
        Invoke-WorkId 'Next'
        Wait-WorkPage
        $nextIds = @(Work-Ids)
        Check-Native ($nextIds.Count -gt 0 -and $nextIds.Count -le 10) 'The continuation page must be nonempty and bounded.'
        Check-Native (@($nextIds | Where-Object { $_ -in $firstIds }).Count -eq 0) 'Mutable keyset continuation duplicated a prior exact session.'
        Check-Native ((Work-Status).Contains('End of current metadata observation; not a global all-clear', [StringComparison]::Ordinal)) 'Final page must deny a global all-clear claim.'
        @{ sessionIds = $nextIds; disjoint = $true; globalAllClearClaimed = $false }
    }
    Trial 'AW04-filter-change-retires-private-page' {
        Invoke-Named $script:allWorkWindow 'Pending-question metadata in scanned snapshots only; unavailable sources remain explicit, never a fresh reply target'
        $null = Wait-Native {
            @(Work-Items).Count -eq 0 -and (Work-Status).StartsWith('Needs-answer filter changed', [StringComparison]::Ordinal)
        } 'filter change retired All Work page'
        Check-Native (!(Get-WorkIdElement 'Next').Current.IsEnabled) 'A changed filter cannot retain continuation.'
        Invoke-Named $script:allWorkWindow 'Pending-question metadata in scanned snapshots only; unavailable sources remain explicit, never a fresh reply target'
        @{ privateRowsCleared = $true; continuationRetired = $true; replyAuthorityCreated = $false }
    }
    Trial 'AW05-privacy-close-and-fresh-empty-window' {
        Invoke-Named $script:launcher 'Close synthetic privacy/ownership gate'
        $null = Wait-Native {
            @(Get-OwnedWindows | Where-Object { $_.Current.Name -in @(
                'All work / attention overview - passive observations', 'Sessions - authoritative work and durable history') }).Count -eq 0
        } 'privacy retired All Work and Sessions windows'
        Invoke-Named $script:launcher 'Reopen synthetic gate (no audio recovery)'
        $null = Wait-Native {
            (Get-NamedElement $script:launcher 'Sessions (scratch IDs, guarded Done/resume)').Current.IsEnabled
        } 'fresh synthetic private admission'
        Open-AllWork
        Check-Native (@(Work-Items).Count -eq 0) 'Fresh All Work window cannot inherit private rows.'
        Check-Native ((Work-Status).StartsWith('Refresh manually', [StringComparison]::Ordinal)) 'Fresh window requires a new bounded observation.'
        @{ priorWindowsRetired = $true; staleRowsReplayed = $false; realOsTransition = $false }
    }

    Close-OwnedWindow $script:allWorkWindow
    Close-OwnedWindow $script:sessionsWindow
    $stopAttempted = $true
    Invoke-Named $script:launcher 'Stop native fixture and clean up scratch state'
    if (!$process.WaitForExit(15000)) { throw 'Owned All Work fixture did not stop normally.' }
    $receipt.exitCode = $process.ExitCode
    $receipt.scratchCleaned = @(Get-ChildItem -LiteralPath $scratch -Force).Count -eq 0
    if ($receipt.exitCode -ne 0 -or !$receipt.scratchCleaned -or $stderr.GetAwaiter().GetResult().Length -gt 0) {
        throw 'All Work fixture cleanup or stderr failed; inspect durable local output.'
    }
    $null = Assert-NativeUxBundle $preparedRoot
    $receipt.status = 'Pass'
}
catch {
    $receipt.status = 'Failed'
    $receipt.failure = $_.Exception.Message
    $receipt.failureType = $_.Exception.GetType().FullName
    throw
}
finally {
    if ($null -ne $receipt.processId -and !$process.HasExited) {
        try {
            if (!$stopAttempted -and $null -ne $script:launcher -and [DateTimeOffset]::UtcNow -lt $DeadlineUtc) {
                $stopAttempted = $true
                Invoke-Named $script:launcher 'Stop native fixture and clean up scratch state'
            }
            if (!$process.WaitForExit(15000)) {
                $receipt.forcedTermination = $true
                Stop-Process -Id $receipt.processId -ErrorAction Stop
                $process.WaitForExit()
            }
        }
        catch {
            $receipt.cleanupFailure = $_.Exception.Message
            if (!$process.HasExited) {
                $receipt.forcedTermination = $true
                Stop-Process -Id $receipt.processId -ErrorAction Stop
                $process.WaitForExit()
            }
        }
        $receipt.status = 'Failed'
    }
    if ($null -ne $receipt.processId) {
        $receipt.exitCode = $process.ExitCode
        $receipt.scratchCleaned = @(Get-ChildItem -LiteralPath $scratch -Force).Count -eq 0
        [IO.File]::WriteAllText((Join-Path $trialRoot 'stdout.local.txt'), $stdout.GetAwaiter().GetResult())
        [IO.File]::WriteAllText((Join-Path $trialRoot 'stderr.local.txt'), $stderr.GetAwaiter().GetResult())
    }
    $receipt.completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $receipt.rows = @($rows)
    Write-ProofJson $receipt (Join-Path $trialRoot 'all-work.json')
    $process.Dispose()
}
