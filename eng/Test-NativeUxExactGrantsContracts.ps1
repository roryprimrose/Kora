#requires -Version 7.5
[CmdletBinding()]
param([Parameter(Mandatory)][string] $OutputDirectory)
. (Join-Path $PSScriptRoot 'NativeUxValidation.Common.ps1')
$OutputDirectory = Assert-NativeUxOutput $OutputDirectory
New-ProofDirectory $OutputDirectory
$driver = Join-Path $PSScriptRoot 'Invoke-NativeUxExactGrants.ps1'
$passed = [Collections.Generic.List[string]]::new()
function Reject-Exact {
    param([string] $Name, [hashtable] $Arguments, [string] $Message)
    $failure = $null
    try { & $driver @Arguments | Out-Null } catch { $failure = $_ }
    if ($null -eq $failure -or !$failure.Exception.Message.Contains($Message, [StringComparison]::Ordinal)) {
        throw "Admission contract failed: $Name"
    }
    $passed.Add($Name)
}
$unused = Join-Path $OutputDirectory 'must-not-be-created'
Reject-Exact 'Prepare refuses desktop approval before any build or launch' @{
    OutputDirectory = $unused; ApproveDesktopAutomation = $true
} 'Prepare accepts no'
Reject-Exact 'Prepare refuses a supplied deadline rather than starting an interactive hour' @{
    OutputDirectory = $unused; DeadlineUtc = [DateTimeOffset]::UnixEpoch
} 'Prepare accepts no'
Reject-Exact 'Prepare refuses ValidateOnly misuse' @{
    OutputDirectory = $unused; ValidateOnly = $true
} 'Prepare accepts no'
Reject-Exact 'Prepare refuses an existing prepared input' @{
    OutputDirectory = $unused; PreparedDirectory = $OutputDirectory
} 'Prepare accepts no'
Reject-Exact 'Run refuses missing deadline even with an approval switch' @{
    Stage = 'Run'; OutputDirectory = $unused; ApproveDesktopAutomation = $true
} 'Run requires an explicit future'
Reject-Exact 'Run refuses expired deadline even with an approval switch' @{
    Stage = 'Run'; OutputDirectory = $unused; DeadlineUtc = [DateTimeOffset]::UnixEpoch; ApproveDesktopAutomation = $true
} 'Run requires an explicit future'
Reject-Exact 'Run refuses overlong deadline even with an approval switch' @{
    Stage = 'Run'; OutputDirectory = $unused; DeadlineUtc = [DateTimeOffset]::MaxValue; ApproveDesktopAutomation = $true
} 'Run requires an explicit future'
Reject-Exact 'ValidateOnly cannot infer a deadline' @{
    Stage = 'Run'; OutputDirectory = $unused; ValidateOnly = $true
} 'Run requires an explicit future'
if (Test-Path -LiteralPath $unused) { throw 'A refusal created a proof directory.' }
$passed.Add('All rejected admissions leave output absent; no current interactive deadline was generated')
$errors = $null
$tokens = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($driver, [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw ($errors.Message -join "`n") }
$passed.Add('Exact native driver parses without execution')
function Assert-PageDiscoveryBody {
    param([Management.Automation.Language.ScriptBlockAst] $Body)
    $commands = @($Body.FindAll({ param($node)
        $node -is [Management.Automation.Language.CommandAst]
    }, $true) | ForEach-Object { $_.GetCommandName() })
    foreach ($forbidden in 'Discover-ExactPage', 'Select-Exact', 'Refresh-Exact', 'Next-Exact') {
        if ($forbidden -in $commands) { throw "Page discovery cannot call $forbidden; it must inspect only the current page." }
    }
}
$discovery = @($ast.FindAll({ param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Discover-ExactPage'
}, $true))
if ($discovery.Count -ne 1) { throw 'One page discovery function is required.' }
Assert-PageDiscoveryBody $discovery[0].Body
$passed.Add('Page discovery AST forbids direct self-recursion, indirect selection recursion and inventory reset/advance')
foreach ($forbidden in 'Discover-ExactPage', 'Select-Exact', 'Refresh-Exact', 'Next-Exact') {
    $invalid = [Management.Automation.Language.Parser]::ParseInput(
        "function Discover-ExactPage { $forbidden }", [ref]$tokens, [ref]$errors)
    $invalidBody = $invalid.Find({ param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst]
    }, $true).Body
    $failure = $null
    try { Assert-PageDiscoveryBody $invalidBody } catch { $failure = $_ }
    if ($null -eq $failure) { throw "Recursion/reset guard failed to reject $forbidden." }
}
$passed.Add('AST guard rejects synthetic direct/indirect recursive and page-reset regressions without executing them')
$trials = @($ast.FindAll({ param($node)
    $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -ceq 'Trial'
}, $true))
$g01 = @($trials | Where-Object { $_.CommandElements[1].Extent.Text -ceq '$acceptance[0]' })
$g02 = @($trials | Where-Object { $_.CommandElements[1].Extent.Text -ceq '$acceptance[1]' })
if ($g01.Count -ne 1 -or $g02.Count -ne 1) { throw 'Unique G01 and G02 trials are required.' }
$setup = $ast.Extent.Text.Substring($g01[0].Extent.EndOffset,
    $g02[0].Extent.StartOffset - $g01[0].Extent.EndOffset)
$setupCalls = @($ast.FindAll({ param($node)
    $node -is [Management.Automation.Language.CommandAst]
}, $true) | Where-Object {
    $_.Extent.StartOffset -ge $g01[0].Extent.EndOffset -and $_.Extent.EndOffset -le $g02[0].Extent.StartOffset
} | ForEach-Object { $_.GetCommandName() })
$setupOrder = ($setupCalls | Where-Object { $_ -cin 'Refresh-Exact', 'Discover-ExactPage', 'Select-Exact' }) -join ','
if ($setupOrder -cne 'Refresh-Exact,Discover-ExactPage,Select-Exact' -or
    $setup -cnotmatch '\$overflowId\s*=\s*\$overflowIds\[0\]') {
    throw 'G02 must refresh/discover the populated first page and inspect its assigned exact overflow ID after G01.'
}
$passed.Add('G02 setup occurs after G01 and binds its inspected overflow ID outside page discovery')

& {
    $displayedId = $ast.Find({ param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Exact-DisplayedId'
    }, $true)
    if ($null -eq $displayedId) { throw 'One displayed exact ID parser is required.' }
    . ([scriptblock]::Create($displayedId.Extent.Text))
    $id = '11111111-1111-1111-1111-111111111111'
    $other = '22222222-2222-2222-2222-222222222222'
    if ((Exact-DisplayedId @("Grant = $id; SessionId = $other", $id, 'Active', 'revision 1')) -cne $id) {
        throw 'Only a displayed ID text element may identify the approval; domain-record names are not exact IDs.'
    }
    $passed.Add('Displayed ID parser ignores domain-record strings containing multiple nonapproval IDs')
    foreach ($texts in @(@("Grant = $id; SessionId = $other"), @($id, $other))) {
        $failure = $null
        try { Exact-DisplayedId $texts | Out-Null } catch { $failure = $_ }
        if ($null -eq $failure) { throw 'Absent or ambiguous displayed approval IDs must fail closed.' }
    }
    $passed.Add('Displayed ID parser rejects absent and ambiguous exact approval text')
}

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
& {
    # Evaluate only extracted helper definitions with local fakes; never dot-source the driver launch path.
    $script:grantWindow = [pscustomobject]@{ SyntheticContractOnly = $true }
    . ([scriptblock]::Create($discovery[0].Extent.Text))
    $next = $ast.Find({ param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Next-Exact'
    }, $true)
    . ([scriptblock]::Create($next.Extent.Text))
    $state = @{
        scrollable = $false; page = 0; positions = [Collections.Generic.List[double]]::new()
        actionCount = 0; waitedReady = 0; ownedChecks = 0
    }
    $mockScroll = [pscustomobject]@{ State = $state; Current = [pscustomobject]@{
        VerticallyScrollable = $true; VerticalViewSize = 20.0; VerticalScrollPercent = 0.0
    } }
    $mockScroll | Add-Member ScriptMethod SetScrollPercent {
        param($Horizontal, $Vertical)
        if ($Horizontal -ne -1) { throw 'Discovery changed horizontal scrolling.' }
        $this.State.positions.Add($Vertical)
        $this.Current.VerticalScrollPercent = $Vertical
    }
    $mockList = [pscustomobject]@{ State = $state; Provider = $mockScroll }
    $mockList | Add-Member ScriptMethod TryGetCurrentPattern {
        param($Pattern, [ref]$Provider)
        $Provider.Value = $this.Provider
        $this.State.scrollable
    }
    function Exact-List { $mockList }
    function Assert-OwnedElement {
        param($Element)
        if ($null -eq $Element) { throw 'Mock owner unresolved.' }
        $state.ownedChecks++
    }
    function Check-Native {
        param([bool]$Condition, [string]$Description)
        if (!$Condition) { throw $Description }
    }
    function Wait-Native {
        param([scriptblock]$Condition, [string]$Description)
        $result = & $Condition
        if (!$result) { throw "Deterministic observation failed: $Description" }
        $result
    }
    function Refresh-Exact { throw 'Current-page discovery or Next must never refresh inventory.' }
    function Select-Exact { throw 'Current-page discovery must never select or inspect a record.' }
    function Exact-Items {
        if ($state.page -eq 1) {
            [pscustomobject]@{ Id = 'next-only' }
        } elseif (!$state.scrollable -or $mockScroll.Current.VerticalScrollPercent -lt 50) {
            [pscustomobject]@{ Id = 'first' }
            [pscustomobject]@{ Id = 'shared' }
        } else {
            [pscustomobject]@{ Id = 'last' }
            [pscustomobject]@{ Id = 'shared' }
        }
    }
    function Exact-ItemId {
        param($Item)
        $Item.Id
    }
    function Invoke-Named {
        param($Root, [string]$Name)
        if ($Name -cne 'Read next bounded exact grant page') { throw 'Unexpected action in mocked helper.' }
        $state.actionCount++
        $state.page++
    }
    function Wait-ExactReady { $state.waitedReady++ }
    $ids = @(Discover-ExactPage)
    if (($ids -join ',') -cne 'first,shared' -or $state.positions.Count -ne 0) {
        throw 'Nonoverflow discovery must enumerate the current page once without scrolling or resetting.'
    }
    $passed.Add('Deterministic nonoverflow helper reads current page without reset, selection or scroll')
    $state.scrollable = $true
    $ids = @(Discover-ExactPage)
    if (($ids -join ',') -cne 'first,last,shared' -or $state.positions.Count -ne 21 -or
        $state.positions[0] -ne 0 -or $state.positions[-1] -ne 100) {
        throw 'Overflow discovery must deduplicate IDs and reach both endpoints in a bounded scan.'
    }
    $passed.Add('Deterministic overflow helper performs bounded endpoint discovery and deduplicates viewport rows')
    $state.positions.Clear()
    $found = @(Discover-ExactPage -FindId 'last')
    if ($found.Count -ne 1 -or $found[0].Id -cne 'last' -or $state.positions[-1] -ne 50) {
        throw 'Exact ID lookup must return one item and stop at its first discovered viewport.'
    }
    $passed.Add('Deterministic exact lookup returns one current-page item without calling selection')
    $missing = @(Discover-ExactPage -FindId 'absent')
    if ($missing.Count -ne 0 -or $state.page -ne 0) { throw 'Missing ID discovery must not advance or reset the page.' }
    $passed.Add('Deterministic missing lookup leaves page authority unchanged and returns no item')
    Next-Exact
    if ($state.page -ne 1 -or $state.actionCount -ne 1 -or $state.waitedReady -ne 1) {
        throw 'Next must advance exactly once, observe changed IDs and wait for readiness without refresh/retry.'
    }
    $passed.Add('Deterministic Next advances once and preserves the next page without refresh or action retry')
}
Write-ProofJson @{
    schema = 1; status = 'Pass'; tests = @($passed); windowsLaunched = $false; desktopInputSent = $false
    approvalStarted = $false; deadlineUtc = $null
} (Join-Path $OutputDirectory 'contracts.json')
Write-Host "Exact-grant no-launch admission contracts passed: $($passed.Count)."
