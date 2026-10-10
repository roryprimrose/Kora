#requires -Version 7.5
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PreparedDirectory,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [Parameter(Mandatory)][DateTimeOffset] $DeadlineUtc,
    [switch] $ApproveDesktopAutomation,
    [switch] $ValidateOnly,
    [switch] $LoadHelpersOnly
)
. (Join-Path $PSScriptRoot 'NativeUxValidation.Common.ps1')
Assert-NativeUxProfile
$PreparedDirectory = Assert-NativeUxOutput $PreparedDirectory
$OutputDirectory = Assert-NativeUxOutput $OutputDirectory
$prepared = Assert-NativeUxBundle $PreparedDirectory
if ($DeadlineUtc -le [DateTimeOffset]::UtcNow -or $DeadlineUtc -gt [DateTimeOffset]::UtcNow.AddHours(1)) {
    throw 'Supply a future deadline no more than one hour away; expired desktop approval cannot launch.'
}
if (!$ValidateOnly -and !$LoadHelpersOnly -and !$ApproveDesktopAutomation) {
    throw 'Explicit -ApproveDesktopAutomation is required: this trial opens windows, changes fixture focus and sends guarded Tab/Shift+Tab input.'
}
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
if ($ValidateOnly) {
    Write-Host 'Native automation prerequisites verified. No process, window or input was created.'
    return
}
if (![Environment]::Is64BitProcess) { throw 'This native keyboard driver requires an x64 PowerShell process.' }
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class KoraFixtureKeyboard
{
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort Key;
        [FieldOffset(12)] public uint Flags;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    private delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    public static IntPtr[] Windows(int process)
    {
        var windows = new List<IntPtr>();
        if (!EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner == (uint)process && IsWindowVisible(window)) windows.Add(window);
            return true;
        }, IntPtr.Zero)) throw new Win32Exception("Owned native window enumeration failed.");
        return windows.ToArray();
    }
    public static uint WindowProcess(IntPtr window)
    {
        return GetWindowThreadProcessId(window, out uint owner) == 0 ? 0 : owner;
    }
    public static void Activate(int process, int window)
    {
        var handle = new IntPtr(window);
        GetWindowThreadProcessId(handle, out uint owner);
        if (owner != (uint)process || !IsWindowVisible(handle)) throw new InvalidOperationException("Activation refused: window is not a visible owned fixture.");
        if (!SetForegroundWindow(handle)) throw new InvalidOperationException("Windows refused foreground activation of the owned fixture.");
    }
    public static void Tab(int process, int window, bool reverse) => Press(process, window, 9, reverse);
    public static void Space(int process, int window) => Press(process, window, 0x20, false);
    private static void Press(int process, int window, ushort key, bool reverse)
    {
        var handle = GetForegroundWindow();
        GetWindowThreadProcessId(handle, out uint foreground);
        if (foreground != (uint)process || handle != new IntPtr(window)) throw new InvalidOperationException("Keyboard input refused: foreground PID/HWND is not the exact owned fixture window.");
        Input Down(ushort key) => new Input { Type = 1, Key = key };
        Input Up(ushort key) => new Input { Type = 1, Key = key, Flags = 2 };
        var inputs = reverse ? new[] { Down(0x10), Down(key), Up(key), Up(0x10) } : new[] { Down(key), Up(key) };
        if (SendInput((uint)inputs.Length, inputs, 40) != inputs.Length) throw new Win32Exception(Marshal.GetLastWin32Error(), "Incomplete guarded keyboard input.");
    }
}
'@
if (!$LoadHelpersOnly) {
    New-ProofDirectory $OutputDirectory
    $scratch = Join-Path $OutputDirectory 'scratch'
    New-Item -ItemType Directory -Path $scratch | Out-Null
}
$rows = [Collections.Generic.List[object]]::new()
$receipt = [ordered]@{ schema = 1; status = 'Running'; startedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    deadlineUtc = $DeadlineUtc.ToUniversalTime().ToString('O')
    driverSha256 = (Get-FileHash -LiteralPath $PSCommandPath).Hash.ToLowerInvariant()
    preparationSha256 = (Get-FileHash -LiteralPath (Join-Path $PreparedDirectory 'automated.json')).Hash.ToLowerInvariant()
    source = $prepared.source; processId = $null; exitCode = $null; readyObserved = $false
    providerReadinessDeferrals = 0
    retiredWindowHandles = 0
    retiredChoiceElements = 0
    scratchCleaned = $false; forcedTermination = $false; rows = @()
    scope = 'PID-scoped synthetic native UI Automation and guarded keyboard mechanics, not Narrator audio, visual readability, text-scale/mixed-DPI, real privacy/ownership or full qualification'
}
$process = [Diagnostics.Process]::new()
$script:fixturePid = 0
$script:launcher = $null

function Assert-TrialTime {
    if ([DateTimeOffset]::UtcNow -ge $DeadlineUtc) { throw 'The desktop approval deadline has expired; stopping this owned trial.' }
    if ($process.HasExited) { throw 'The owned fixture exited before the trial finished.' }
}
function Assert-OwnedElement {
    param([Windows.Automation.AutomationElement] $Element)
    Assert-TrialTime
    if ($null -eq $Element -or $Element.Current.ProcessId -ne $script:fixturePid) {
        throw 'UI Automation refused: element is not in the exact owned fixture PID.'
    }
}
function Get-OwnedWindows {
    @(
        foreach ($handle in [KoraFixtureKeyboard]::Windows($script:fixturePid)) {
            try { $element = [Windows.Automation.AutomationElement]::FromHandle($handle) }
            catch [Management.Automation.MethodInvocationException] {
                if ([KoraFixtureKeyboard]::WindowProcess($handle) -ne 0) { throw }
                $receipt.retiredWindowHandles++
                Write-Host 'An enumerated owned HWND retired before provider resolution; no action is retried.'
                continue
            }
            if ($null -eq $element -or $null -eq $element.Current -or $element.Current.ProcessId -le 0) {
                # A newly created HWND can precede its UIA provider; admit no operation until both identities resolve.
                if ($receipt.providerReadinessDeferrals -eq 0) { Write-Host 'Waiting for an owned HWND automation provider; no input admitted while its identity is unknown.' }
                $receipt.providerReadinessDeferrals++
                continue
            }
            if ($element.Current.ProcessId -ne $script:fixturePid) {
                throw "Owned HWND $handle resolved to automation PID $($element.Current.ProcessId), expected $script:fixturePid; no input admitted."
            }
            $element
        }
    )
}
function Wait-Native {
    param([scriptblock] $Condition, [string] $Description, [int] $Seconds = 10)
    $until = [DateTimeOffset]::UtcNow.AddSeconds($Seconds)
    do {
        Assert-TrialTime
        $value = & $Condition
        if ($value) { return $value }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $until)
    throw "Native observation timed out: $Description"
}
function Get-NamedElement {
    param([Windows.Automation.AutomationElement] $Root, [string] $Name, [switch] $Prefix)
    Assert-OwnedElement $Root
    $condition = if ($Prefix) { [Windows.Automation.Condition]::TrueCondition } else {
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $Name)
    }
    $matches = @($Root.FindAll([Windows.Automation.TreeScope]::Descendants, $condition) |
        Where-Object { !$Prefix -or $_.Current.Name.StartsWith($Name, [StringComparison]::Ordinal) })
    if ($matches.Count -ne 1) { throw "Expected one owned native control '$Name', found $($matches.Count)." }
    Assert-OwnedElement $matches[0]
    $matches[0]
}
function Invoke-Named {
    param([Windows.Automation.AutomationElement] $Root, [string] $Name)
    $element = Get-NamedElement $Root $Name
    $receipt.lastAction = $Name
    [KoraFixtureKeyboard]::Activate($script:fixturePid, $Root.Current.NativeWindowHandle)
    $element.SetFocus()
    $null = Wait-Native {
        $focused = [Windows.Automation.AutomationElement]::FocusedElement
        $focused.Current.ProcessId -eq $script:fixturePid -and
            ($focused.GetRuntimeId() -join ',') -ceq ($element.GetRuntimeId() -join ',')
    } "exact focus before $Name"
    if (!$element.Current.IsEnabled) { throw "Native action is disabled: $Name" }
    [KoraFixtureKeyboard]::Space($script:fixturePid, $Root.Current.NativeWindowHandle)
}
function Close-OwnedWindow {
    param([Windows.Automation.AutomationElement] $Window)
    Assert-OwnedElement $Window
    $runtimeId = $Window.GetRuntimeId() -join ','
    ([Windows.Automation.WindowPattern]$Window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern)).Close()
    $null = Wait-Native { @(Get-OwnedWindows | Where-Object { ($_.GetRuntimeId() -join ',') -ceq $runtimeId }).Count -eq 0 } 'owned window closure'
}
function Open-Window {
    param([string] $Action, [string] $Title)
    Invoke-Named $script:launcher $Action
    Wait-Native {
        $found = @(Get-OwnedWindows | Where-Object { $_.Current.Name -clike $Title })
        if ($found.Count -eq 1) { return $found[0] }
        if ($found.Count -gt 1) { throw "Ambiguous owned window title: $Title" }
    } "open $Title"
}
function Check-Native {
    param([bool] $Condition, [string] $Description)
    if (!$Condition) { throw $Description }
}
function Trial {
    param([string] $Id, [scriptblock] $Action)
    Assert-TrialTime
    Write-Host "Native trial $Id"
    $started = [DateTimeOffset]::UtcNow
    try {
        $detail = & $Action
        $rows.Add([ordered]@{ id = $Id; status = 'Pass'; detail = $detail; startedUtc = $started.ToString('O')
            completedUtc = [DateTimeOffset]::UtcNow.ToString('O') })
    } catch {
        $rows.Add([ordered]@{ id = $Id; status = 'Failed'; error = $_.Exception.Message; startedUtc = $started.ToString('O')
            completedUtc = [DateTimeOffset]::UtcNow.ToString('O') })
        throw
    }
}
function Get-QuestionStatus {
    param([Windows.Automation.AutomationElement] $Window)
    (Get-NamedElement $Window 'Question status and recovery' -Prefix).Current.Name
}
function Get-FocusName {
    $focus = [Windows.Automation.AutomationElement]::FocusedElement
    Assert-OwnedElement $focus
    $focus.Current.Name
}

if ($LoadHelpersOnly) {
    $process.Dispose()
    Write-Host 'Owned native mechanics helpers loaded only. No process, window, input or proof directory created.'
    return
}

try {
    $process.StartInfo.FileName = Join-Path $PreparedDirectory 'payload\Kora.NativeUxFixture.exe'
    $process.StartInfo.WorkingDirectory = Join-Path $PreparedDirectory 'payload'
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    foreach ($argument in '--launch-native-fixtures', '--scratch-parent', $scratch) { $process.StartInfo.ArgumentList.Add($argument) }
    if (!$process.Start()) { throw 'The synthetic fixture did not start.' }
    $receipt.processId = $process.Id
    $script:fixturePid = $process.Id
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    Write-Host "Owned fixture PID $script:fixturePid. No audio, models, network, screenshots, real profile data or Windows setting changes."
    $script:launcher = Wait-Native {
        $found = @(Get-OwnedWindows | Where-Object {
            $_.Current.Name -cin @('Kora synthetic native UX fixture launcher',
                'Kora SYNTHETIC native UX fixture - not the production host')
        })
        if ($found.Count -eq 1) { $found[0] }
    } 'fixture launcher'
    $null = Wait-Native {
        (Get-NamedElement $script:launcher 'Native fixture status' -Prefix).Current.Name.Contains('READY:', [StringComparison]::Ordinal)
    } 'READY synthetic initialization' -Seconds 30
    $receipt.readyObserved = $true

    Trial 'M01-maintenance-no-network' {
        $window = Open-Window 'Notify-only maintenance (network disabled)' 'Kora release maintenance (notify-only)'
        $consent = Get-NamedElement $window 'Permit public release metadata checks for this run only'
        Check-Native (!$consent.Current.IsEnabled) 'Fixture network consent must remain disabled.'
        $state = ([Windows.Automation.TogglePattern]$consent.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)).Current.ToggleState
        Check-Native ($state -eq [Windows.Automation.ToggleState]::Off) 'Fixture network consent must remain off.'
        Close-OwnedWindow $window
        'Native network consent disabled and off; no metadata check invoked.'
    }
    Trial 'M02-appearance-keyboard-cycles' {
        $window = Open-Window 'Appearance (scratch settings only)' 'Kora settings - *'
        $first = Get-NamedElement $window 'Select an appearance option to inspect or change'
        $expected = @('Appearance', 'Select an appearance option to inspect or change',
            'Reset the selected appearance option to its default', 'Show animated presence', 'Application theme',
            'Always show', 'Stay on top', 'Response timeout in seconds', 'Presence timeout in seconds',
            'Presence size in pixels', 'Dot size percent', 'Dot density percent', 'Movement speed percent',
            'Scale presence with speech playback', 'Speech scale amount percent')
        $cycles = @()
        foreach ($reverse in $false, $true) {
            $previous = $null
            foreach ($iteration in 1..2) {
                $first.SetFocus()
                $names = [Collections.Generic.List[string]]::new()
                foreach ($index in 1..15) {
                    $names.Add((Get-FocusName))
                    [KoraFixtureKeyboard]::Tab($script:fixturePid, $window.Current.NativeWindowHandle, $reverse)
                    Start-Sleep -Milliseconds 100
                    Assert-TrialTime
                }
                Check-Native ((Get-FocusName) -ceq $names[0]) 'The native tab cycle must return to its starting control after exactly 15 entries.'
                Check-Native ((($names | Sort-Object) -join '|') -ceq (($expected | Sort-Object) -join '|')) 'The complete expected native Appearance tab cycle was not observed.'
                $order = $names -join '|'
                if ($null -ne $previous) { Check-Native ($previous -ceq $order) 'Repeated keyboard order changed.' }
                $previous = $order
                $cycles += @{ reverse = $reverse; iteration = $iteration; names = @($names) }
            }
        }
        Close-OwnedWindow $window
        $cycles
    }
    Trial 'M03-question-review-draft-submit' {
        $window = Open-Window 'Review local version (scratch native question)' 'Kora - exact host question'
        $null = Wait-Native {
            $radio = Get-NamedElement $window 'Show local version'
            if (!$radio.Current.IsEnabled) { return $false }
            try {
                $choice = [Windows.Automation.SelectionItemPattern]$radio.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)
                if (!$choice.Current.IsSelected) { $choice.Select() }
                return $choice.Current.IsSelected
            } catch [Management.Automation.MethodInvocationException] {
                if ($_.Exception.InnerException -isnot [Windows.Automation.ElementNotAvailableException]) { throw }
                $receipt.retiredChoiceElements++
                Write-Host 'A native choice element retired during initialization; re-resolving the exact scratch choice. No draft, submit or other persistent action is retried.'
                return $false
            }
        } 'enabled exact show choice selected'
        (Get-NamedElement $window 'Review exact original question or operation - no grant use').SetFocus()
        Invoke-Named $window 'Review exact original question or operation - no grant use'
        $review = Get-NamedElement $window 'Immutable exact host record review - not approval or execution'
        $reviewPattern = [Windows.Automation.ValuePattern]$review.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
        Check-Native $reviewPattern.Current.IsReadOnly 'Exact native review must be read-only.'
        $text = Wait-Native { if (![string]::IsNullOrWhiteSpace($reviewPattern.Current.Value)) { $reviewPattern.Current.Value } } 'complete immutable native review'
        Check-Native (![string]::IsNullOrWhiteSpace($text)) 'Exact review must expose nonempty immutable content.'
        $original = $text | ConvertFrom-Json -AsHashtable -DateKind String
        (Get-NamedElement $window 'Save answer draft without submitting').SetFocus()
        Invoke-Named $window 'Save answer draft without submitting'
        $null = Wait-Native { (Get-QuestionStatus $window).EndsWith('answer-recorded', [StringComparison]::Ordinal) } 'committed draft status'
        Invoke-Named $window 'Review exact original question or operation - no grant use'
        $draftText = Wait-Native { if (![string]::IsNullOrWhiteSpace($reviewPattern.Current.Value)) { $reviewPattern.Current.Value } } 'revised immutable draft review'
        $draft = $draftText | ConvertFrom-Json -AsHashtable -DateKind String
        Check-Native (($draft.Key.Request | ConvertTo-Json -Depth 20 -Compress) -ceq
            ($original.Key.Request | ConvertTo-Json -Depth 20 -Compress)) 'Draft must retain the exact original request/session/task.'
        Check-Native ($draft.Key.QuestionId.Value -ceq $original.Key.QuestionId.Value -and
            $draft.Key.Revision.Value -eq $original.Key.Revision.Value + 1 -and
            $draft.Status -eq $original.Status -and @($draft.Draft.Choices).Count -eq 1 -and
            $draft.Draft.Choices[0] -ceq 'show') 'Draft review must preserve the question, advance one revision and retain the unsubmitted show choice.'
        (Get-NamedElement $window 'Submit answer to the displayed exact question').SetFocus()
        Invoke-Named $window 'Submit answer to the displayed exact question'
        $terminal = Wait-Native { $status = Get-QuestionStatus $window; if ($status.Contains('Local version:', [StringComparison]::Ordinal)) { $status } } 'committed local-version terminal status'
        foreach ($name in 'Review exact original question or operation - no grant use',
            'Save answer draft without submitting', 'Submit answer to the displayed exact question') {
            Check-Native (!(Get-NamedElement $window $name).Current.IsEnabled) 'Terminal answer actions must be disabled.'
        }
        $close = Get-NamedElement $window 'Close native presentation without approval or execution'
        $null = Wait-Native { (Get-FocusName) -ceq $close.Current.Name } 'terminal Close focus'
        Start-Sleep -Milliseconds 1500
        Check-Native ((Get-QuestionStatus $window) -ceq $terminal) 'Passive native refresh must preserve terminal disclosure.'
        Check-Native ((Get-FocusName) -ceq $close.Current.Name) 'Passive native refresh must preserve Close focus.'
        Close-OwnedWindow $window
        'Exact review exposed; draft then submit completed; terminal disclosure/actions/focus preserved through native refresh. Narrator speech not observed.'
    }
    Trial 'M04-stale-question-revision' {
        $window = Open-Window 'Open synthetic stale-revision question' 'Kora - exact host question'
        $null = Wait-Native { (Get-NamedElement $window 'Review exact original question or operation - no grant use').Current.IsEnabled } 'initial stale-question controls admitted'
        Invoke-Named $window 'Review exact original question or operation - no grant use'
        $review = [Windows.Automation.ValuePattern](Get-NamedElement $window 'Immutable exact host record review - not approval or execution').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
        $originalReview = Wait-Native { if (![string]::IsNullOrWhiteSpace($review.Current.Value)) { $review.Current.Value } } 'original immutable stale-question review'
        Invoke-Named $script:launcher 'Advance exact synthetic question revision without retargeting'
        $null = Wait-Native {
            (Get-NamedElement $script:launcher 'Native fixture status' -Prefix).Current.Name.Contains('revision 2.', [StringComparison]::Ordinal)
        } 'advanced exact question revision'
        [KoraFixtureKeyboard]::Activate($script:fixturePid, $window.Current.NativeWindowHandle)
        $status = Wait-Native {
            $text = Get-QuestionStatus $window
            if (!(Get-NamedElement $window 'Submit answer to the displayed exact question').Current.IsEnabled) { $text }
        } 'stale answer actions refused by native refresh'
        Check-Native ($status -cmatch '[Ss]tale|[Rr]evision|[Uu]navailable|[Cc]hanged') 'Stale native presentation needs an explicit refusal reason.'
        foreach ($name in 'Review exact original question or operation - no grant use',
            'Save answer draft without submitting', 'Submit answer to the displayed exact question', 'Cancel the displayed exact question') {
            Check-Native (!(Get-NamedElement $window $name).Current.IsEnabled) 'Stale revision actions must be disabled.'
        }
        Check-Native ($review.Current.Value -ceq $originalReview) 'Stale refusal must preserve the original immutable review, not retarget it.'
        Close-OwnedWindow $window
        $status
    }
    Trial 'M05-synthetic-private-window-closure' {
        $window = Open-Window 'Sessions (scratch IDs, guarded Done/resume)' 'Sessions - authoritative work and durable history'
        $null = Get-NamedElement $window 'Session name draft; names never resolve authority'
        Invoke-Named $script:launcher 'Close synthetic privacy/ownership gate'
        $null = Wait-Native { [KoraFixtureKeyboard]::Windows($script:fixturePid).Count -eq 1 } 'synthetic private window closure'
        Check-Native (!(Get-NamedElement $script:launcher 'Sessions (scratch IDs, guarded Done/resume)').Current.IsEnabled) 'Synthetic closure must disable private entry actions.'
        Invoke-Named $script:launcher 'Reopen synthetic gate (no audio recovery)'
        $null = Wait-Native { (Get-NamedElement $script:launcher 'Sessions (scratch IDs, guarded Done/resume)').Current.IsEnabled } 'fresh synthetic gate reopened'
        Check-Native ([KoraFixtureKeyboard]::Windows($script:fixturePid).Count -eq 1) 'Reopening the synthetic gate must not replay a private window.'
        'Actual fixture window closed; private actions disabled; reopening required fresh request. No real OS privacy transition qualified.'
    }
    Invoke-Named $script:launcher 'Stop native fixture and clean up scratch state'
    if (!$process.WaitForExit(15000)) { throw 'The owned fixture did not stop within 15 seconds.' }
    $receipt.exitCode = $process.ExitCode
    $output = $stdout.GetAwaiter().GetResult()
    $errors = $stderr.GetAwaiter().GetResult()
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'stdout.local.txt'), $output)
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'stderr.local.txt'), $errors)
    $receipt.scratchCleaned = @(Get-ChildItem -LiteralPath $scratch -Force).Count -eq 0
    Check-Native ($receipt.exitCode -eq 0 -and $receipt.scratchCleaned -and $errors.Length -eq 0) 'Native trial needs exit 0, empty stderr and exact scratch cleanup.'
    $null = Assert-NativeUxBundle $PreparedDirectory
    $receipt.status = 'ScopedPass'
}
catch {
    $receipt.status = 'Failed'
    $receipt.failure = $_.Exception.Message
    if ($null -ne $receipt.processId -and !$process.HasExited) {
        try {
            $receipt.ownedWindowDiscovery = @(Get-OwnedWindows | ForEach-Object {
                @{ name = $_.Current.Name; controlType = $_.Current.ControlType.ProgrammaticName
                    nativeHandle = $_.Current.NativeWindowHandle }
            })
        } catch { $receipt.discoveryFailure = $_.Exception.Message }
    }
    throw
}
finally {
    if ($null -ne $receipt.processId -and !$process.HasExited) {
        try {
            if ($null -ne $script:launcher) {
                $stop = $script:launcher.FindFirst([Windows.Automation.TreeScope]::Descendants,
                    [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,
                        'Stop native fixture and clean up scratch state'))
                if ($null -ne $stop -and $stop.Current.ProcessId -eq $script:fixturePid) {
                    ([Windows.Automation.InvokePattern]$stop.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
                }
            }
            if (!$process.WaitForExit(15000)) {
                $receipt.forcedTermination = $true
                Stop-Process -Id $receipt.processId -ErrorAction Stop
                $process.WaitForExit()
            }
        } catch {
            $receipt.status = 'Failed'
            $receipt.cleanupFailure = $_.Exception.Message
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
    Write-ProofJson $receipt (Join-Path $OutputDirectory 'mechanics.json')
}
Write-Host "Native mechanics $($receipt.status): $($rows.Count) rows, exit $($receipt.exitCode), cleanup $($receipt.scratchCleaned). Human observations remain pending."
