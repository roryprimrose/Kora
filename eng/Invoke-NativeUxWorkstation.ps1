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
$workstationRoot = Assert-NativeUxOutput $OutputDirectory
$preparedRoot = Assert-NativeUxOutput $PreparedDirectory
$workstationDriverHash = (Get-FileHash -LiteralPath $PSCommandPath).Hash.ToLowerInvariant()
$baselineDriver = Join-Path $PSScriptRoot 'Invoke-NativeUxMechanics.ps1'
& $baselineDriver -PreparedDirectory $preparedRoot -OutputDirectory $workstationRoot `
    -DeadlineUtc $DeadlineUtc -ValidateOnly
if ($ValidateOnly) { return }
if (!$ApproveDesktopAutomation) { throw 'Explicit desktop automation approval is required for the expanded workstation trial.' }
New-ProofDirectory $workstationRoot
# Execute the maintained baseline first and reuse its exact PID/HWND admission, native keyboard and observation helpers.
. $baselineDriver -PreparedDirectory $preparedRoot -OutputDirectory (Join-Path $workstationRoot 'baseline') `
    -DeadlineUtc $DeadlineUtc -ApproveDesktopAutomation
$originalOwnedWindowDiscovery = (Get-Command Get-OwnedWindows).ScriptBlock
$originalNamedElementDiscovery = (Get-Command Get-NamedElement).ScriptBlock
$OutputDirectory = Join-Path $workstationRoot 'expanded'
New-ProofDirectory $OutputDirectory
$scratch = Join-Path $OutputDirectory 'scratch'
New-Item -ItemType Directory -Path $scratch | Out-Null
$prepared = Assert-NativeUxBundle $preparedRoot
$rows = [Collections.Generic.List[object]]::new()
$receipt = [ordered]@{ schema = 1; status = 'Running'; startedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    deadlineUtc = $DeadlineUtc.ToUniversalTime().ToString('O'); driverSha256 = $workstationDriverHash
    baselineDriverSha256 = (Get-FileHash -LiteralPath $baselineDriver).Hash.ToLowerInvariant()
    preparationSha256 = (Get-FileHash -LiteralPath (Join-Path $preparedRoot 'automated.json')).Hash.ToLowerInvariant()
    source = $prepared.source; processId = $null; readyObserved = $false; exitCode = $null
    providerReadinessDeferrals = 0; retiredWindowElements = 0; retiredDescendantElements = 0
    scratchCleaned = $false; forcedTermination = $false; rows = @()
    scope = 'Additional PID-owned native controls, exact-ID scratch history/evidence, immutable bundled resources and window/display mechanics; no physical-display qualification, readable-text/Narrator/audio/real privacy or installed acceptance'
}
$process = [Diagnostics.Process]::new()
$script:fixturePid = 0
$script:launcher = $null
$script:sessionA = $null
$script:sessionB = $null

function Get-OwnedWindows {
    $until = [DateTimeOffset]::UtcNow.AddSeconds(2)
    do {
        Assert-TrialTime
        try { return @(& $originalOwnedWindowDiscovery) }
        catch [Management.Automation.MethodInvocationException] {
            if ($_.Exception.InnerException -isnot [Windows.Automation.ElementNotAvailableException]) { throw }
            # An owned HWND can retire between OS enumeration and provider resolution. Retry discovery, never an action.
            $receipt.retiredWindowElements++
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $until)
    throw 'Owned window discovery did not stabilize after a retired HWND; no input was admitted.'
}
function Get-NamedElement {
    param([Windows.Automation.AutomationElement] $Root, [string] $Name, [switch] $Prefix)
    $until = [DateTimeOffset]::UtcNow.AddSeconds(2)
    do {
        Assert-TrialTime
        try { return & $originalNamedElementDiscovery -Root $Root -Name $Name -Prefix:$Prefix }
        catch [Management.Automation.MethodInvocationException] {
            if ($_.Exception.InnerException -isnot [Windows.Automation.ElementNotAvailableException]) { throw }
            $receipt.retiredDescendantElements++
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $until)
    throw "Owned native discovery did not stabilize after a retired descendant for '$Name'; no action was retried."
}
function Get-IdElement {
    param([Windows.Automation.AutomationElement] $Root, [string] $Id)
    Assert-OwnedElement $Root
    $found = @($Root.FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)))
    if ($found.Count -ne 1) { throw "Expected one exact native automation ID '$Id', observed $($found.Count)." }
    Assert-OwnedElement $found[0]
    $found[0]
}
function Invoke-Id {
    param([Windows.Automation.AutomationElement] $Root, [string] $Id)
    $control = Get-IdElement $Root $Id
    if ([string]::IsNullOrWhiteSpace($control.Current.Name)) { throw "Native control $Id has no accessible name." }
    Invoke-Named $Root $control.Current.Name
}
function Set-NativeValue {
    param([Windows.Automation.AutomationElement] $Root, [string] $Name, [string] $Value)
    $element = Get-NamedElement $Root $Name
    $pattern = [Windows.Automation.ValuePattern]$element.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
    if ($pattern.Current.IsReadOnly) { throw "The native fixture field is read-only: $Name" }
    $pattern.SetValue($Value)
    $null = Wait-Native { $pattern.Current.Value -ceq $Value } "exact value for $Name"
}
function Get-NativeText {
    param([Windows.Automation.AutomationElement] $Element)
    Assert-OwnedElement $Element
    @($Element.Current.Name) + @($Element.FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,
            [Windows.Automation.ControlType]::Text)) | ForEach-Object { $_.Current.Name }) -join "`n"
}
function Get-ListItems {
    param([Windows.Automation.AutomationElement] $List)
    Assert-OwnedElement $List
    @($List.FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,
            [Windows.Automation.ControlType]::ListItem)))
}
function Select-NativeItem {
    param([Windows.Automation.AutomationElement] $Item)
    Assert-OwnedElement $Item
    $pattern = [Windows.Automation.SelectionItemPattern]$Item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)
    $pattern.Select()
    $null = Wait-Native { $pattern.Current.IsSelected } 'native exact item selection'
}
function Get-SessionDetail {
    param([Windows.Automation.AutomationElement] $Window)
    Assert-OwnedElement $Window
    $detail = $Window.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, 'SessionDetail'))
    Assert-OwnedElement $detail
    $detail.Current.Name
}
function Read-NativeJson {
    param([Windows.Automation.AutomationElement] $Window, [string] $Prefix)
    $text = if ($Prefix -ceq 'Typed question, exact task and evidence details; inspection is not approval') {
        Get-SessionDetail $Window
    } else { (Get-NamedElement $Window $Prefix -Prefix).Current.Name }
    $body = $text.Substring($Prefix.Length).TrimStart(':', ' ')
    if (!$body.StartsWith('{', [StringComparison]::Ordinal)) { throw 'Native result has no complete structured JSON object.' }
    $body | ConvertFrom-Json -AsHashtable -DateKind String
}
function Open-NewOwnedWindow {
    param([Windows.Automation.AutomationElement] $Root, [string] $Action)
    $before = @([KoraFixtureKeyboard]::Windows($script:fixturePid) | ForEach-Object { $_.ToInt64() })
    Invoke-Named $Root $Action
    Wait-Native {
        $found = @(Get-OwnedWindows | Where-Object { $_.Current.NativeWindowHandle -notin $before })
        if ($found.Count -eq 1) { $found[0] }
        elseif ($found.Count -gt 1) { throw 'One explicit fixture action unexpectedly opened multiple native windows.' }
    } "new owned window for $Action"
}
function Select-SessionId {
    param([Windows.Automation.AutomationElement] $Window, [string] $Id)
    $list = Get-NamedElement $Window 'Durable session names, exact IDs, lifecycle and optimistic revisions'
    $item = Wait-Native {
        $scroll = $null
        $hasScroll = $list.TryGetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern, [ref]$scroll)
        foreach ($percent in 0, 25, 50, 75, 100) {
            Assert-TrialTime
            Assert-OwnedElement $list
            if ($hasScroll -and $scroll.Current.VerticallyScrollable) {
                $scroll.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, $percent)
                Start-Sleep -Milliseconds 150
            }
            $matches = @(Get-ListItems $list | Where-Object { (Get-NativeText $_).Contains($Id, [StringComparison]::Ordinal) })
            if ($matches.Count -gt 1) { throw 'Exact immutable session ID was not unique in the native snapshot.' }
            if ($matches.Count -eq 1) { return $matches[0] }
        }
    } 'populated native snapshot containing the exact immutable session ID'
    Select-NativeItem $item
    $null = Wait-Native {
        (Get-NamedElement $Window 'Selected exact session work snapshot, revisions, observation time, capacity and gaps' -Prefix).Current.Name.Contains("Exact session $Id |", [StringComparison]::Ordinal)
    } 'authoritative work snapshot for the exact native session selection'
}
function Enqueue-NativeVersion {
    param([Windows.Automation.AutomationElement] $Window, [string] $Id)
    $status = (Get-NamedElement $Window 'Selected exact session work snapshot, revisions, observation time, capacity and gaps' -Prefix).Current.Name
    Check-Native ($status -cmatch 'queue revision ([0-9]+)') 'Enqueue requires an observed native queue revision.'
    $revision = [long]$Matches[1]
    Invoke-Id $Window 'EnqueueVersion'
    $result = Wait-Native {
        if ((Get-SessionDetail $Window).Contains('"queue":', [StringComparison]::Ordinal)) {
            $value = Read-NativeJson $Window 'Typed question, exact task and evidence details; inspection is not approval'
            if ($null -ne $value.queue -and $value.queue.sessionId.value -ceq $Id -and $value.queue.revision -gt $revision) { $value }
        }
    } 'new exact native enqueue receipt, not an old snapshot'
    $entries = @($result.queue.entries)
    Check-Native ($result.outcome -ceq 'committed' -and $entries.Count -eq 1 -and $entries[0].state -ceq 'Pending' -and
        $entries[0].request.sessionId.value -ceq $Id) 'Native enqueue must commit one exact pending local-version entry without dispatch.'
    $entries[0]
}
function Get-NativeQueueRow {
    param([Windows.Automation.AutomationElement] $Window, [string] $TaskId, [string] $State)
    $list = Get-NamedElement $Window 'Authoritative queued, current, waiting, blocked, cancelled and unknown work; selection is passive'
    $until = [DateTimeOffset]::UtcNow.AddSeconds(10)
    $scroll = $null
    $hasScroll = $list.TryGetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern, [ref]$scroll)
    $verticalBar = @($list.FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.AndCondition]::new(
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ScrollBar),
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::OrientationProperty, [Windows.Automation.OrientationType]::Vertical))))
    if ($verticalBar.Count -gt 1) { throw 'Native work list has ambiguous vertical scroll controls.' }
    $range = $null
    if ($verticalBar.Count -eq 1) {
        Assert-OwnedElement $verticalBar[0]
        $null = $verticalBar[0].TryGetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern, [ref]$range)
    }
    do {
        foreach ($percent in 0, 25, 50, 75, 100) {
            Assert-TrialTime
            Assert-OwnedElement $list
            if ($hasScroll -and $scroll.Current.VerticallyScrollable) {
                $scroll.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, $percent)
                Start-Sleep -Milliseconds 150
            } elseif ($null -ne $range -and !$range.Current.IsReadOnly -and $range.Current.Maximum -gt $range.Current.Minimum) {
                Assert-OwnedElement $verticalBar[0]
                $range.SetValue($range.Current.Minimum + ($range.Current.Maximum - $range.Current.Minimum) * $percent / 100)
                Start-Sleep -Milliseconds 150
            }
            $items = Get-ListItems $list
            $found = @($items | Where-Object {
                $text = Get-NativeText $_
                $text.Contains("Task $TaskId |", [StringComparison]::Ordinal) -and $text.Contains("Queue: $State |", [StringComparison]::Ordinal)
            })
            if ($found.Count -gt 1) { throw 'Exact native queue task identity is ambiguous.' }
            if ($found.Count -eq 1) { return $found[0] }
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $until)
    $receipt.queueRowDiscovery = @{ expectedTask = $TaskId; expectedState = $State
        realizedRows = @($items | ForEach-Object { Get-NativeText $_ }); hasScrollPattern = $hasScroll
        lastScrollPercent = if ($hasScroll) { $scroll.Current.VerticalScrollPercent } else { $null }
        verticalBarRange = if ($null -ne $range) { @{ value = $range.Current.Value; minimum = $range.Current.Minimum; maximum = $range.Current.Maximum } } else { $null }
        exactWorkStatus = (Get-NamedElement $Window 'Selected exact session work snapshot, revisions, observation time, capacity and gaps' -Prefix).Current.Name
        lastNativeResult = Get-SessionDetail $Window }
    throw "Exact native queue task in state $State was not found through bounded owned-list scrolling."
}

try {
    $process.StartInfo.FileName = Join-Path $preparedRoot 'payload\Kora.NativeUxFixture.exe'
    $process.StartInfo.WorkingDirectory = Join-Path $preparedRoot 'payload'
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    foreach ($argument in '--launch-native-fixtures', '--scratch-parent', $scratch) { $process.StartInfo.ArgumentList.Add($argument) }
    if (!$process.Start()) { throw 'The expanded synthetic fixture did not start.' }
    $receipt.processId = $process.Id
    $script:fixturePid = $process.Id
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $script:launcher = Wait-Native {
        $found = @(Get-OwnedWindows | Where-Object { $_.Current.Name -ceq 'Kora SYNTHETIC native UX fixture - not the production host' })
        if ($found.Count -eq 1) { $found[0] }
    } 'expanded fixture launcher'
    $null = Wait-Native { (Get-NamedElement $script:launcher 'Native fixture status' -Prefix).Current.Name.Contains('READY:', [StringComparison]::Ordinal) } 'expanded READY' -Seconds 30
    $receipt.readyObserved = $true

    Trial 'W01-native-session-create-rename' {
        $window = Open-Window 'Sessions (scratch IDs, guarded Done/resume)' 'Sessions - authoritative work and durable history'
        Set-NativeValue $window 'Session name draft; names never resolve authority' 'Workstation synthetic session A'
        Invoke-Id $window 'CreateSession'
        $created = Wait-Native {
            $text = (Get-NamedElement $window 'Sessions status and unavailable reasons' -Prefix).Current.Name
            if ($text -cmatch 'Committed empty Active session ([a-f0-9-]{36})\.') { $Matches[1] }
        } 'native created immutable session ID'
        $initial = Get-SessionDetail $window
        Check-Native ($initial.Contains("Session $created | Active | generation 1", [StringComparison]::Ordinal) -and
            $initial.Contains('Metadata revision: 1', [StringComparison]::Ordinal)) 'Fresh native session must have generation and metadata revision 1.'
        Set-NativeValue $window 'Session name draft; names never resolve authority' 'Workstation synthetic session A renamed'
        Invoke-Id $window 'RenameSession'
        $null = Wait-Native { (Get-SessionDetail $window).Contains('Metadata revision: 2', [StringComparison]::Ordinal) } 'native renamed metadata revision'
        $renamed = Get-SessionDetail $window
        Check-Native ($renamed.Contains("Session $created | Active | generation 1", [StringComparison]::Ordinal) -and
            $renamed.Contains('Workstation synthetic session A renamed', [StringComparison]::Ordinal)) 'Rename must preserve exact ID and generation.'
        $script:sessionA = $created
        Close-OwnedWindow $window
        @{ sameImmutableId = $true; initialGeneration = 1; finalGeneration = 1; initialMetadataRevision = 1; finalMetadataRevision = 2 }
    }
    Trial 'W02-native-passive-history-isolation' {
        $window = Open-Window 'Sessions (scratch IDs, guarded Done/resume)' 'Sessions - authoritative work and durable history'
        Set-NativeValue $window 'Session name draft; names never resolve authority' 'Workstation synthetic session B'
        Invoke-Id $window 'CreateSession'
        $second = Wait-Native {
            $text = (Get-NamedElement $window 'Sessions status and unavailable reasons' -Prefix).Current.Name
            if ($text -cmatch 'Committed empty Active session ([a-f0-9-]{36})\.') { $Matches[1] }
        } 'second immutable native session ID'
        $script:sessionB = $second
        Check-Native ($second -cne $script:sessionA) 'Fresh native sessions must have different immutable IDs.'
        $results = @()
        $snapshots = @{}
        # Alternate exact IDs so a retained prior result can never satisfy a new read's completion predicate.
        foreach ($id in $script:sessionA, $second, $script:sessionA, $second) {
            Set-NativeValue $window 'Exact immutable session ID for passive history; names do not select sessions' $id
            Invoke-Id $window 'ReadHistory'
            $history = Wait-Native {
                if ((Get-SessionDetail $window).Contains('"history":', [StringComparison]::Ordinal)) {
                    $value = Read-NativeJson $window 'Typed question, exact task and evidence details; inspection is not approval'
                    if ($value.history.sessionId.value -ceq $id) { $value }
                }
            } 'bounded native history for the newly selected exact ID'
            Check-Native ($history.history.sessionId.value -ceq $id) 'Passive native history returned a different session.'
            $records = @($history.history.records)
            Check-Native ($records.Count -gt 0 -and $records.Count -le 25) 'Metadata history must be nonempty and bounded, not an invented zero-row success.'
            $sequences = @($records.sequence)
            for ($index = 1; $index -lt $sequences.Count; $index++) {
                Check-Native ($sequences[$index] -eq $sequences[$index - 1] + 1) 'Native history sequences must be contiguous and ordered.'
            }
            foreach ($record in $records) {
                Check-Native ($record.sessionId.value -ceq $id -and $record.kind -cin 'Gap', 'Task' -and
                    $null -eq $record.question -and $null -eq $record.answer) 'Fresh named-session history must stay metadata-only and isolated.'
            }
            $snapshot = $history.history | ConvertTo-Json -Depth 30 -Compress
            $repeat = $snapshots.ContainsKey($id)
            if ($repeat) {
                Check-Native ($snapshot -ceq $snapshots[$id]) 'Repeated exact-ID reads must preserve the fixed metadata-only history snapshot.'
            } else { $snapshots[$id] = $snapshot }
            $field = Get-NamedElement $window 'Exact immutable session ID for passive history; names do not select sessions'
            $field.SetFocus()
            $focus = Get-FocusName
            Start-Sleep -Milliseconds 5500
            Assert-TrialTime
            Check-Native ((Get-FocusName) -ceq $focus) 'Passive work refresh must preserve exact history-field focus.'
            $results += @{ records = $records.Count; kinds = @($records.kind); ordered = $true; isolated = $true; repeatedRead = $repeat; passiveFocusPreserved = $true }
        }
        Close-OwnedWindow $window
        $results
    }
    Trial 'W03-native-guide-detail-lifetime' {
        $guide = Open-Window 'Guide and owned immutable details' 'Kora Documentation'
        $results = @()
        foreach ($iteration in 1..3) {
            $detail = Open-NewOwnedWindow $guide 'Open native details for the current embedded page'
            $identity = (Get-IdElement $detail 'DigestLabel').Current.Name
            Check-Native ($identity -cmatch 'SHA-256: ([A-Fa-f0-9]{64})') 'Native detail must expose a complete immutable digest.'
            $expectedDigest = $Matches[1].ToLowerInvariant()
            $reading = Get-NamedElement $detail 'Read and select continuous semantic text'
            ([Windows.Automation.TogglePattern]$reading.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)).Toggle()
            $source = Get-NamedElement $detail 'Show exact immutable source'
            ([Windows.Automation.TogglePattern]$source.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)).Toggle()
            $reader = Get-NamedElement $detail 'Passive immutable detail text'
            $value = [Windows.Automation.ValuePattern]$reader.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
            Check-Native $value.Current.IsReadOnly 'Native immutable detail source must remain read-only.'
            $text = Wait-Native { if (![string]::IsNullOrWhiteSpace($value.Current.Value)) { $value.Current.Value } } 'exact native detail source'
            $bytes = [Text.Encoding]::UTF8.GetBytes($text)
            $digest = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($bytes))
            Check-Native ($digest -ceq $expectedDigest -and $bytes.Length -le 262144) 'Native exact detail source must match its digest and 256 KiB bound.'
            Close-OwnedWindow $detail
            Check-Native (@(Get-OwnedWindows | Where-Object { $_.Current.Name -ceq 'Kora Documentation' }).Count -eq 1) 'Closing a detail must retain its guide owner.'
            $results += @{ iteration = $iteration; exactSourceDigestMatched = $true; utf8Bytes = $bytes.Length; ownerRetained = $true; clipboardOperations = 0 }
        }
        $detail = Open-NewOwnedWindow $guide 'Open native details for the current embedded page'
        Invoke-Named $script:launcher 'Close immutable detail and refuse deferred stale render'
        $null = Wait-Native { (Get-NamedElement $script:launcher 'Native fixture status' -Prefix).Current.Name.Contains('Refused deferred render', [StringComparison]::Ordinal) } 'closed-generation render refused'
        Check-Native (@(Get-OwnedWindows).Count -eq 2) 'Refused deferred detail must not restore a closed native window.'
        Close-OwnedWindow $guide
        @{ repeats = $results; staleRenderRefused = $true; clipboardOperations = 0 }
    }
    Trial 'W04-native-bundled-package-exact-files' {
        $window = Open-Window 'Skill packages (bundled inspection only)' 'Skill packages - inspection only'
        $identity = (Get-NamedElement $window 'Exact package identities, unavailable invocation and transitive disclosure' -Prefix).Current.Name
        Check-Native ($identity -cmatch '[Uu]navailable|not admitted') 'Bundled source review must disclose unavailable execution.'
        $tabs = Get-NamedElement $window 'Every declared immutable package file including manifest and shared helper'
        $items = @($tabs.FindAll([Windows.Automation.TreeScope]::Descendants,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,
                [Windows.Automation.ControlType]::TabItem)))
        Check-Native ($items.Count -ge 4) 'Bundled review must include all declared manifest/instructions/entry/helper file tabs.'
        $files = @()
        foreach ($tab in $items) {
            Assert-OwnedElement $tab
            ([Windows.Automation.SelectionItemPattern]$tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)).Select()
            $name = $tab.Current.Name -replace ' \(shared\)$', ''
            $source = Wait-Native { Get-NamedElement $window "$name exact immutable source" } 'selected declared source tab'
            $pattern = [Windows.Automation.ValuePattern]$source.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
            Check-Native $pattern.Current.IsReadOnly 'Inspection-only package source must remain read-only.'
            $bytes = [Text.Encoding]::UTF8.GetBytes($pattern.Current.Value)
            $identityText = (Get-NamedElement $window "$name identity, digest and byte count" -Prefix).Current.Name
            Check-Native ($identityText -cmatch 'SHA-256: ([a-f0-9]{64}) \| ([0-9]+) original bytes') 'Exact source identity must expose digest and original byte count.'
            $digest = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($bytes))
            Check-Native ($digest -ceq $Matches[1] -and $bytes.Length -eq [int]$Matches[2]) 'Native bundled source bytes must match the exact declared resource digest and size.'
            $files += @{ file = $name; originalBytes = $bytes.Length; exactDigestMatched = $true; readOnly = $true }
        }
        Close-OwnedWindow $window
        @{ files = $files; executions = 0; realProfileSourcesRead = 0; clipboardOperations = 0 }
    }
    Trial 'W05-native-evidence-explicit-links' {
        Invoke-Named $script:launcher 'Seed retained and missing explicit evidence links'
        $status = (Get-NamedElement $script:launcher 'Native fixture status' -Prefix).Current.Name
        Check-Native ($status -cmatch 'linked trace ([a-f0-9]{32}), span ([a-f0-9]{16}); retained target trace ([a-f0-9]{32}), span ([a-f0-9]{16}); missing target trace ([a-f0-9]{32}), span ([a-f0-9]{16})') 'The trusted fixture did not expose exact link identities.'
        $linkedTrace = $Matches[1]; $retainedTrace = $Matches[3]; $missingTrace = $Matches[5]
        $window = Open-Window 'Evidence (scratch records, read-only)' 'Evidence - read-only local inspection'
        Set-NativeValue $window 'Trace correlation filter' $linkedTrace
        Invoke-Id $window 'Search'
        $result = Wait-Native {
            $text = (Get-NamedElement $window 'Bounded structured evidence result, read only, no copy or export' -Prefix).Current.Name
            if ($text.Contains('"Records":', [StringComparison]::Ordinal)) { Read-NativeJson $window 'Bounded structured evidence result, read only, no copy or export' }
        } 'native linked trace result'
        $span = @($result.Records | Where-Object { $_.Reference.Source -ceq 'Span' })
        Check-Native ($span.Count -eq 1 -and @($span[0].RelatedSegments).Count -eq 2) 'Native linked trace must have one exact span and two explicit segments.'
        $retained = @($span[0].RelatedSegments | Where-Object TraceId -CEQ $retainedTrace)
        $missing = @($span[0].RelatedSegments | Where-Object TraceId -CEQ $missingTrace)
        Check-Native ($retained.Count -eq 1 -and $retained[0].Status -ceq 'Present' -and $null -ne $retained[0].Record -and
            $missing.Count -eq 1 -and $missing[0].Status -ceq 'MissingOrRemoved' -and $null -eq $missing[0].Record) 'Native evidence must distinguish retained and missing explicit segments.'
        $list = Get-NamedElement $window 'Cited evidence records'
        $items = Get-ListItems $list
        $target = @($items | Where-Object { (Get-NativeText $_).Contains($span[0].Reference.Citation, [StringComparison]::Ordinal) })
        Check-Native ($target.Count -eq 1) 'Exact span citation must be unique in the native evidence list.'
        Select-NativeItem $target[0]
        $segments = Get-NamedElement $window 'Trace parent and link segment availability'
        $nativeSegments = Wait-Native { $values = Get-ListItems $segments; if ($values.Count -eq 2) { ,$values } } 'native present/missing segment list'
        $texts = @($nativeSegments | ForEach-Object { Get-NativeText $_ })
        Check-Native (@($texts | Where-Object { $_.Contains($retainedTrace, [StringComparison]::Ordinal) -and $_.Contains('Present', [StringComparison]::Ordinal) }).Count -eq 1 -and
            @($texts | Where-Object { $_.Contains($missingTrace, [StringComparison]::Ordinal) -and $_.Contains('MissingOrRemoved', [StringComparison]::Ordinal) }).Count -eq 1) 'Native segment presentation must preserve truthful availability.'
        Set-NativeValue $window 'Session correlation filter' $span[0].Host.SessionId.Value
        Invoke-Id $window 'Search'
        $filtered = Wait-Native {
            $value = Read-NativeJson $window 'Bounded structured evidence result, read only, no copy or export'
            $values = @($value.Records | Where-Object { $_.Reference.Source -ceq 'Span' })
            if ($values.Count -eq 1) {
                $segments = @($values[0].RelatedSegments | Where-Object TraceId -CEQ $retainedTrace)
                if ($segments.Count -eq 1 -and $null -eq $segments[0].Record) { $value }
            }
        } 'fresh session-confined native evidence with cross-session navigation removed'
        $confined = @($filtered.Records | Where-Object { $_.Reference.Source -ceq 'Span' })
        Check-Native ($confined.Count -eq 1 -and
            $null -eq @($confined[0].RelatedSegments | Where-Object TraceId -CEQ $retainedTrace)[0].Record) 'A cross-session retained link must not become a navigable record under session confinement.'
        Close-OwnedWindow $window
        @{ retainedAndMissingDistinguished = $true; crossSessionConfinement = $true; linksConferAuthority = $false; copyOrExportOperations = 0 }
    }
    Trial 'W06-native-stale-session-generation' {
        Invoke-Named $script:launcher 'Identify exact synthetic lifecycle target'
        $text = (Get-NamedElement $script:launcher 'Native fixture status' -Prefix).Current.Name
        Check-Native ($text -cmatch 'session ([a-f0-9-]{36}), generation ([0-9]+), Active True') 'Expected an exact active synthetic lifecycle target.'
        $id = $Matches[1]; $generation = [int]$Matches[2]
        $window = Open-Window 'Sessions (scratch IDs, guarded Done/resume)' 'Sessions - authoritative work and durable history'
        Invoke-Id $window 'Refresh'
        Select-SessionId $window $id
        $oldHandle = $window.Current.NativeWindowHandle
        Invoke-Named $script:launcher 'Advance exact synthetic lifecycle target without UI refresh'
        $null = Wait-Native { (Get-NamedElement $script:launcher 'Native fixture status' -Prefix).Current.Name.Contains("generation $($generation + 1), Active False", [StringComparison]::Ordinal) } 'synthetic lifecycle generation advanced'
        $null = Wait-Native {
            @(Get-OwnedWindows | Where-Object { $_.Current.NativeWindowHandle -eq $oldHandle }).Count -eq 0
        } 'old-generation native session window revoked'
        $window = Open-Window 'Sessions (scratch IDs, guarded Done/resume)' 'Sessions - authoritative work and durable history'
        Invoke-Id $window 'Refresh'
        Select-SessionId $window $id
        $current = Get-SessionDetail $window
        Check-Native ($current.Contains("Session $id | Done | generation $($generation + 1)", [StringComparison]::Ordinal)) 'Reopening the revoked view must preserve the exact session and observe only its newly committed generation.'
        Close-OwnedWindow $window
        @{ oldGenerationRevoked = $true; referencedWindowClosed = $true; immutableIdPreserved = $true; actualGeneration = $generation + 1 }
    }
    Trial 'W07-native-owned-window-display-mechanics' {
        Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class NativeUxWindowPlacement
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    public sealed class Display { public Rect Work; public bool Primary; }
    public sealed class Placement { public Rect Bounds; public uint Dpi; }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr context, ref Rect rectangle, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumDisplayMonitors(IntPtr context, IntPtr clip, MonitorCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    private static IntPtr Owned(int process, int window)
    {
        var handle = new IntPtr(window);
        GetWindowThreadProcessId(handle, out uint owner);
        if (owner != (uint)process || !IsWindowVisible(handle))
            throw new InvalidOperationException("Placement refused: not an exact visible owned fixture HWND.");
        return handle;
    }
    private static IntPtr EnterPhysicalCoordinates()
    {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        if (previous == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Physical coordinate context unavailable.");
        return previous;
    }
    private static void RestoreCoordinateContext(IntPtr previous)
    {
        if (SetThreadDpiAwarenessContext(previous) == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Coordinate context restoration failed.");
    }
    public static Display[] Displays()
    {
        var previous = EnterPhysicalCoordinates();
        try
        {
            var displays = new List<Display>();
            int infoError = 0;
            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr context, ref Rect rectangle, IntPtr parameter) =>
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfo(monitor, ref info)) { infoError = Marshal.GetLastWin32Error(); return false; }
                displays.Add(new Display { Work = info.Work, Primary = (info.Flags & 1) != 0 });
                return true;
            }, IntPtr.Zero)) throw new Win32Exception(infoError != 0 ? infoError : Marshal.GetLastWin32Error(), "Display enumeration failed.");
            if (displays.Count == 0) throw new InvalidOperationException("No display work area was admitted.");
            return displays.ToArray();
        }
        finally { RestoreCoordinateContext(previous); }
    }
    public static Placement Measure(int process, int window)
    {
        var handle = Owned(process, window);
        var previous = EnterPhysicalCoordinates();
        try
        {
            if (!GetWindowRect(handle, out Rect bounds)) throw new Win32Exception(Marshal.GetLastWin32Error());
            uint dpi = GetDpiForWindow(handle);
            if (dpi == 0) throw new InvalidOperationException("The owned fixture has no valid native DPI.");
            return new Placement { Bounds = bounds, Dpi = dpi };
        }
        finally { RestoreCoordinateContext(previous); }
    }
    public static void Place(int process, int window, int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var handle = Owned(process, window);
        var previous = EnterPhysicalCoordinates();
        try
        {
            // Preserve z-order and activation. Only the exact owned fixture receives native size/position changes.
            if (!SetWindowPos(handle, IntPtr.Zero, x, y, width, height, 0x0014))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Owned fixture placement failed.");
        }
        finally { RestoreCoordinateContext(previous); }
    }
}
'@
        $window = Open-Window 'Appearance (scratch settings only)' 'Kora settings - *'
        $handle = $window.Current.NativeWindowHandle
        $control = Get-NamedElement $window 'Select an appearance option to inspect or change'
        [KoraFixtureKeyboard]::Activate($script:fixturePid, $handle)
        $control.SetFocus()
        $focusedId = $control.GetRuntimeId() -join ','
        $original = [NativeUxWindowPlacement]::Measure($script:fixturePid, $handle)
        $displays = [NativeUxWindowPlacement]::Displays()
        $placements = @()
        try {
            foreach ($display in $displays) {
                $work = $display.Work
                Check-Native ($work.Right - $work.Left -gt 900 -and $work.Bottom - $work.Top -gt 700) 'Display work area cannot safely contain the bounded fixture placement.'
                foreach ($size in @(@{ width = 1060; height = 800 }, @{ width = 940; height = 720 })) {
                    Assert-OwnedElement $window
                    $width = [Math]::Min($size.width, $work.Right - $work.Left - 80)
                    $height = [Math]::Min($size.height, $work.Bottom - $work.Top - 80)
                    [NativeUxWindowPlacement]::Place($script:fixturePid, $handle, $work.Left + 40, $work.Top + 40, $width, $height)
                    $placement = Wait-Native {
                        $value = [NativeUxWindowPlacement]::Measure($script:fixturePid, $handle)
                        if ($value.Bounds.Left -eq $work.Left + 40 -and $value.Bounds.Top -eq $work.Top + 40) { $value }
                    } 'owned fixture positioned in the enumerated display work area'
                    $bounds = $placement.Bounds
                    Check-Native ($bounds.Right -le $work.Right -and $bounds.Bottom -le $work.Bottom) 'Actual fixture bounds, including native minimum size, must remain inside the display work area.'
                    $null = Wait-Native {
                        $focus = [Windows.Automation.AutomationElement]::FocusedElement
                        $focus.Current.ProcessId -eq $script:fixturePid -and ($focus.GetRuntimeId() -join ',') -ceq $focusedId
                    } 'exact selector focus retained through native display placement'
                    $selector = (Get-NamedElement $window 'Select an appearance option to inspect or change').Current
                    Check-Native (!$selector.IsOffscreen -and $selector.BoundingRectangle.Width -gt 0 -and
                        $selector.BoundingRectangle.Left -ge $bounds.Left -and $selector.BoundingRectangle.Right -le $bounds.Right -and
                        $selector.BoundingRectangle.Top -ge $bounds.Top -and $selector.BoundingRectangle.Bottom -le $bounds.Bottom) 'The exact focused selector must have nonempty onscreen bounds inside the owned fixture.'
                    $placements += @{ primaryDisplay = $display.Primary; work = $work; actual = $bounds
                        dpi = $placement.Dpi; exactFocusPreserved = $true; selectorBoundsWithinWindow = $true }
                }
            }
        } finally {
            $rect = $original.Bounds
            [NativeUxWindowPlacement]::Place($script:fixturePid, $handle, $rect.Left, $rect.Top, $rect.Right - $rect.Left, $rect.Bottom - $rect.Top)
            $null = Wait-Native {
                $restored = [NativeUxWindowPlacement]::Measure($script:fixturePid, $handle)
                $restored.Bounds.Left -eq $rect.Left -and $restored.Bounds.Top -eq $rect.Top -and
                    $restored.Bounds.Right -eq $rect.Right -and $restored.Bounds.Bottom -eq $rect.Bottom -and $restored.Dpi -eq $original.Dpi
            } 'exact original owned placement and native DPI restored'
        }
        $distinctDpi = @($placements.dpi | Sort-Object -Unique)
        Close-OwnedWindow $window
        @{ enumeratedDisplayCount = $displays.Count; placements = $placements; distinctNativeDpi = $distinctDpi
            mixedNativeDpiObserved = $distinctDpi.Count -gt 1; originalPlacementRestored = $true
            physicalDisplayAndReadabilityAcceptance = 'Pending'; screenshots = 0; globalWindowsSettingChanges = 0 }
    }
    Trial 'W08-native-exact-queue-control-and-dispatch' {
        $window = Open-Window 'Sessions (scratch IDs, guarded Done/resume)' 'Sessions - authoritative work and durable history'
        Invoke-Id $window 'Refresh'
        Select-SessionId $window $script:sessionA
        $first = Enqueue-NativeVersion $window $script:sessionA
        $removedId = $first.request.taskId.value
        $row = Get-NativeQueueRow $window $removedId 'Pending'
        Select-NativeItem $row
        Invoke-Id $window 'InspectWork'
        $null = Wait-Native {
            $result = Read-NativeJson $window 'Typed question, exact task and evidence details; inspection is not approval'
            $null -ne $result.queueEntry -and $result.queueEntry.request.taskId.value -ceq $removedId -and $result.queueEntry.state -ceq 'Pending'
        } 'exact pending task inspected without dispatch'
        Invoke-Id $window 'RemoveQueueEntry'
        $null = Wait-Native {
            $result = Read-NativeJson $window 'Typed question, exact task and evidence details; inspection is not approval'
            $null -ne $result.queue -and $result.queue.sessionId.value -ceq $script:sessionA -and
                $result.queue.revision -gt $first.revision.value -and @($result.queue.entries).Count -eq 0
        } 'exact pending task removed before dispatch'
        $removed = Get-NativeQueueRow $window $removedId 'Removed'
        Select-NativeItem $removed
        Check-Native (!(Get-IdElement $window 'RemoveQueueEntry').Current.IsEnabled) 'A retained Removed receipt must not remain eligible for another removal.'
        Invoke-Id $window 'InspectWork'
        $null = Wait-Native {
            $result = Read-NativeJson $window 'Typed question, exact task and evidence details; inspection is not approval'
            $null -ne $result.queueEntry -and $result.queueEntry.request.taskId.value -ceq $removedId -and $result.queueEntry.state -ceq 'Removed'
        } 'removed exact receipt retained, not erased'
        $queued = @()
        foreach ($id in $script:sessionA, $script:sessionB) {
            Select-SessionId $window $id
            $entry = Enqueue-NativeVersion $window $id
            $queued += $entry
            $row = Get-NativeQueueRow $window $entry.request.taskId.value 'Pending'
            Select-NativeItem $row
            $focusedBefore = [Windows.Automation.AutomationElement]::FocusedElement
            Assert-OwnedElement $focusedBefore
            $focus = $focusedBefore.GetRuntimeId() -join ','
            Start-Sleep -Milliseconds 5500
            Assert-TrialTime
            $focusedAfter = [Windows.Automation.AutomationElement]::FocusedElement
            Assert-OwnedElement $focusedAfter
            $receipt.queueFocusObservation = @{ beforeRuntimeId = $focus; afterRuntimeId = $focusedAfter.GetRuntimeId() -join ','
                afterName = $focusedAfter.Current.Name }
            Check-Native (($focusedAfter.GetRuntimeId() -join ',') -ceq $focus) 'Passive queued-work observation must preserve exact native control focus, independent of changing accessible observation text.'
            $null = Get-NativeQueueRow $window $entry.request.taskId.value 'Pending'
        }
        Invoke-Id $window 'DispatchQueue'
        $result = Wait-Native {
            $value = Read-NativeJson $window 'Typed question, exact task and evidence details; inspection is not approval'
            if ($null -ne $value.queueDispatch -and @($value.queueDispatch).Count -eq 2) { $value }
        } 'explicit manual dispatch committed two exact session receipts'
        $receipts = @($result.queueDispatch)
        $expectedTasks = @($queued | ForEach-Object { $_.request.taskId.value } | Sort-Object)
        $actualTasks = @($receipts | ForEach-Object { $_.entry.request.taskId.value } | Sort-Object)
        Check-Native (($expectedTasks -join ',') -ceq ($actualTasks -join ',') -and $removedId -cnotin $actualTasks) 'Manual dispatch must observe only the two admitted exact tasks, never removed work.'
        foreach ($item in $receipts) {
            Check-Native ($item.entry.state -ceq 'Succeeded' -and $null -ne $item.version -and
                ![string]::IsNullOrWhiteSpace($item.version.version)) 'Each native local-version dispatch needs a truthful terminal success and version observation.'
        }
        foreach ($entry in $queued) {
            Select-SessionId $window $entry.request.sessionId.value
            $null = Get-NativeQueueRow $window $entry.request.taskId.value 'Succeeded'
        }
        Close-OwnedWindow $window
        @{ exactPendingRemoved = $true; removedReceiptRetained = $true; terminalRemovalDisabled = $true
            passiveRefreshDidNotDispatch = $true; dispatchedSessionCount = 2; exactSucceededReceipts = 2
            removedTaskNotDispatched = $true; localVersions = @($receipts | ForEach-Object { $_.version.version })
            providerExecutions = 0; effectExecutions = 0; audioOperations = 0 }
    }
    Trial 'W09-native-private-gate-queue-no-replay' {
        $window = Open-Window 'Sessions (scratch IDs, guarded Done/resume)' 'Sessions - authoritative work and durable history'
        Invoke-Id $window 'Refresh'
        Select-SessionId $window $script:sessionA
        $entry = Enqueue-NativeVersion $window $script:sessionA
        $id = $entry.request.taskId.value
        $newRow = Get-NativeQueueRow $window $id 'Pending'
        Check-Native (!$newRow.Current.IsOffscreen -and $newRow.Current.BoundingRectangle.Height -gt 0) 'The exact newly added pending task must be realized onscreen after scrolling the terminal-populated native list.'
        Invoke-Named $script:launcher 'Close synthetic privacy/ownership gate'
        $null = Wait-Native { [KoraFixtureKeyboard]::Windows($script:fixturePid).Count -eq 1 } 'closed synthetic gate cleared the native work window'
        Check-Native (!(Get-NamedElement $script:launcher 'Sessions (scratch IDs, guarded Done/resume)').Current.IsEnabled) 'Closed synthetic ownership must deny new private work inspection.'
        Invoke-Named $script:launcher 'Reopen synthetic gate (no audio recovery)'
        $null = Wait-Native { (Get-NamedElement $script:launcher 'Sessions (scratch IDs, guarded Done/resume)').Current.IsEnabled } 'fresh synthetic private gate'
        Check-Native ([KoraFixtureKeyboard]::Windows($script:fixturePid).Count -eq 1) 'Synthetic reopen must not replay an old native work window.'
        $window = Open-Window 'Sessions (scratch IDs, guarded Done/resume)' 'Sessions - authoritative work and durable history'
        Invoke-Id $window 'Refresh'
        Select-SessionId $window $script:sessionA
        Invoke-Id $window 'DispatchQueue'
        $result = Wait-Native {
            if ((Get-SessionDetail $window).Contains('"queueDispatch":', [StringComparison]::Ordinal)) {
                $value = Read-NativeJson $window 'Typed question, exact task and evidence details; inspection is not approval'
                if ($null -ne $value.queueDispatch) { $value }
            }
        } 'explicit post-reopen dispatch result'
        Check-Native (@($result.queueDispatch).Count -eq 0 -and @($result.queue.entries).Count -eq 1 -and
            $result.queue.entries[0].request.taskId.value -ceq $id -and $result.queue.entries[0].state -ceq 'Pending') 'Fresh gate approval must not dispatch or replace queued work from its old admission epoch.'
        $priorRevision = $result.queue.revision
        Invoke-Id $window 'ClearQueue'
        $null = Wait-Native {
            $value = Read-NativeJson $window 'Typed question, exact task and evidence details; inspection is not approval'
            $null -ne $value.queue -and $value.queue.revision -gt $priorRevision -and @($value.queue.entries).Count -eq 0
        } 'explicitly cleared the single exact pending entry after old-epoch refusal'
        Close-OwnedWindow $window
        @{ oldNativeWindowCleared = $true; noWindowReplay = $true; oldEpochDispatchRefused = $true
            oldEpochDispatchReceipts = 0; immutableTaskPreservedPending = $true; explicitlyRemovedAfterRefusal = $true
            actualWindowsPrivacyTransition = 'Not performed'; audioRecovery = 0
            newlyAddedRowInTerminalPopulatedNativeList = $true
            newlyAddedExactTaskId = $id }
    }
    Trial 'W10-native-metadata-only-logical-disposition' {
        $window = Open-Window 'Sessions (scratch IDs, guarded Done/resume)' 'Sessions - authoritative work and durable history'
        Set-NativeValue $window 'Session name draft; names never resolve authority' 'Workstation disposable metadata-only session C'
        Invoke-Id $window 'CreateSession'
        $id = Wait-Native {
            $text = (Get-NamedElement $window 'Sessions status and unavailable reasons' -Prefix).Current.Name
            if ($text -cmatch 'Committed empty Active session ([a-f0-9-]{36})\.') { $Matches[1] }
        } 'exact new disposable synthetic session ID'
        Check-Native ($id -cne $script:sessionA -and $id -cne $script:sessionB) 'Logical disposition trial must address only its new metadata-only subject.'
        $oldHandle = $window.Current.NativeWindowHandle
        Invoke-Id $window 'Done'
        $null = Wait-Native {
            @(Get-OwnedWindows | Where-Object { $_.Current.NativeWindowHandle -eq $oldHandle }).Count -eq 0
        } 'Done revokes the exact referenced native window'
        $window = Open-Window 'Sessions (scratch IDs, guarded Done/resume)' 'Sessions - authoritative work and durable history'
        Invoke-Id $window 'Refresh'
        Select-SessionId $window $id
        $null = Wait-Native { (Get-SessionDetail $window).Contains("Session $id | Done | generation 2", [StringComparison]::Ordinal) } 'exact new session made Done before logical disposition'
        Invoke-Id $window 'PreviewDisposition'
        $preview = Wait-Native { $text = Get-SessionDetail $window; if ($text.Contains("Exact session ID: $id", [StringComparison]::Ordinal)) { $text } } 'exact native logical-disposition preview'
        Check-Native ($preview.Contains('Generation: 2', [StringComparison]::Ordinal) -and
            $preview.Contains('Metadata revision: 1', [StringComparison]::Ordinal) -and
            (Get-NamedElement $window 'Sessions status and unavailable reasons' -Prefix).Current.Name.Contains('Preview only; nothing removed.', [StringComparison]::Ordinal)) 'Preview must retain exact revisions and honestly disclose no deletion yet.'
        $oldHandle = $window.Current.NativeWindowHandle
        Invoke-Id $window 'ConfirmDisposition'
        $null = Wait-Native {
            @(Get-OwnedWindows | Where-Object { $_.Current.NativeWindowHandle -eq $oldHandle }).Count -eq 0
        } 'exact confirmed disposition revokes the referenced native window'
        $window = Open-Window 'Sessions (scratch IDs, guarded Done/resume)' 'Sessions - authoritative work and durable history'
        Check-Native (!(Get-IdElement $window 'ConfirmDisposition').Current.IsEnabled -and
            !(Get-IdElement $window 'Resume').Current.IsEnabled -and !(Get-IdElement $window 'EnqueueVersion').Current.IsEnabled) 'Cleared disposed selection must not retain lifecycle, confirmation or execution eligibility.'
        Set-NativeValue $window 'Exact immutable session ID for passive history; names do not select sessions' $id
        Invoke-Id $window 'ReadHistory'
        $history = Wait-Native {
            if ((Get-SessionDetail $window).Contains('"history":', [StringComparison]::Ordinal)) {
                $value = Read-NativeJson $window 'Typed question, exact task and evidence details; inspection is not approval'
                if ($value.history.sessionId.value -ceq $id -and $value.history.disposed) { $value.history }
            }
        } 'disposed exact-ID history retains citations, not replay'
        Check-Native ($history.generation.value -eq 3 -and @($history.records).Count -gt 0 -and
            @($history.records).Count -le 25) 'Disposed metadata-only history must preserve its exact tombstone generation and bounded citations.'
        foreach ($record in $history.records) {
            Check-Native ($record.sessionId.value -ceq $id -and $null -eq $record.question -and $null -eq $record.answer) 'Disposed native history must not introduce foreign sessions or private question/answer content.'
        }
        Invoke-Id $window 'Refresh'
        Select-SessionId $window $script:sessionA
        $null = Wait-Native { (Get-NamedElement $window 'Selected exact session work snapshot, revisions, observation time, capacity and gaps' -Prefix).Current.Name.Contains("Exact session $script:sessionA | generation 1", [StringComparison]::Ordinal) } 'unrelated exact native session retained after disposition'
        Close-OwnedWindow $window
        @{ previewWasNonDestructive = $true; deliberateExactConfirmation = $true; tombstoneGeneration = 3
            referencedWindowsRevoked = $true
            historyCitationCount = @($history.records).Count; metadataOnlyHistoryPreserved = $true
            unrelatedSubjectUnchanged = $true; forbiddenForensicDeletionClaim = $false; privateContentCreated = 0 }
    }

    Invoke-Named $script:launcher 'Stop native fixture and clean up scratch state'
    if (!$process.WaitForExit(15000)) { throw 'Expanded fixture did not stop within fifteen seconds.' }
    $receipt.exitCode = $process.ExitCode
    $receipt.scratchCleaned = @(Get-ChildItem -LiteralPath $scratch -Force).Count -eq 0
    $errors = $stderr.GetAwaiter().GetResult()
    Check-Native ($process.ExitCode -eq 0 -and $receipt.scratchCleaned -and $errors.Length -eq 0) 'Expanded native trial needs exit 0, no stderr and exact scratch cleanup.'
    $null = Assert-NativeUxBundle $preparedRoot
    $receipt.status = 'ScopedPass'
}
catch {
    $receipt.status = 'Failed'
    $receipt.failure = $_.Exception.Message
    $receipt.exceptionType = $_.Exception.GetBaseException().GetType().FullName
    $receipt.exceptionHResult = $_.Exception.GetBaseException().HResult
    $receipt.scriptStack = $_.ScriptStackTrace
    if ($null -ne $receipt.processId -and !$process.HasExited) {
        try {
            $receipt.ownedWindowDiscovery = @(Get-OwnedWindows | ForEach-Object {
                @{ name = $_.Current.Name; handle = $_.Current.NativeWindowHandle }
            })
        } catch { $receipt.discoveryFailure = $_.Exception.Message }
    }
    throw
}
finally {
    if ($null -ne $receipt.processId -and !$process.HasExited) {
        try {
            if ($null -ne $script:launcher) {
                $stop = Get-NamedElement $script:launcher 'Stop native fixture and clean up scratch state'
                ([Windows.Automation.InvokePattern]$stop.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
            }
            if (!$process.WaitForExit(15000)) {
                $receipt.forcedTermination = $true
                Stop-Process -Id $receipt.processId -ErrorAction Stop
                $process.WaitForExit()
            }
        } catch {
            $receipt.cleanupFailure = $_.Exception.Message
            $receipt.status = 'Failed'
            if (!$process.HasExited) {
                $receipt.forcedTermination = $true
                Stop-Process -Id $receipt.processId -ErrorAction Stop
                $process.WaitForExit()
            }
        }
        $receipt.exitCode = $process.ExitCode
        $receipt.scratchCleaned = @(Get-ChildItem -LiteralPath $scratch -Force).Count -eq 0
    }
    if ($null -ne $receipt.processId) {
        [IO.File]::WriteAllText((Join-Path $OutputDirectory 'stdout.local.txt'), $stdout.GetAwaiter().GetResult())
        [IO.File]::WriteAllText((Join-Path $OutputDirectory 'stderr.local.txt'), $stderr.GetAwaiter().GetResult())
    }
    $process.Dispose()
    $receipt.rows = @($rows)
    $receipt.completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Write-ProofJson $receipt (Join-Path $OutputDirectory 'workstation.json')
}
Write-Host "Expanded workstation trial $($receipt.status): $($rows.Count) rows. Human and real privacy/display qualification remain open."
