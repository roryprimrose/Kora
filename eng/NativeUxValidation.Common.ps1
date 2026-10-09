#requires -Version 7.5
. (Join-Path $PSScriptRoot 'Distribution.Common.ps1')

function Assert-NativeUxProfile {
    if (!$IsWindows -or ![Environment]::UserInteractive) {
        throw 'Native UX validation requires an interactive Windows profile.'
    }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        if ($identity.IsSystem -or $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Use a non-elevated interactive profile, not an administrator or SYSTEM host.'
        }
    } finally { $identity.Dispose() }
}

function Assert-NativeUxOutput {
    param([string] $Path)
    if (![IO.Path]::IsPathFullyQualified($Path) -or $Path -cnotmatch '^[A-Za-z]:\\') {
        throw 'Use an absolute output path on a fixed local drive.'
    }
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($full)).DriveType -ne [IO.DriveType]::Fixed) {
        throw 'Use a fixed local drive, not a network or removable drive.'
    }
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
    if ($full.Equals($repository, [StringComparison]::OrdinalIgnoreCase) -or
        $full.StartsWith("$repository\", [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Keep local proof evidence outside the repository.'
    }
    $full
}

function Read-NativeUxJson {
    param([string] $Path)
    Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable -DateKind String
}

function Get-NativeUxSourceIdentity {
    param([string] $Repository)
    $revision = & git -C $Repository rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the source revision.' }
    $paths = @(& git -C $Repository -c core.quotepath=false ls-files --cached --others --exclude-standard -- `
        src tests installer eng '*.props' '*.targets' global.json Kora.slnx)
    if ($LASTEXITCODE -ne 0 -or $paths.Count -eq 0) { throw 'Could not inventory the build source.' }
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($path in $paths | Sort-Object -Unique) {
        $absolute = Join-Path $Repository $path.Replace('/', '\')
        Assert-NoLinks $absolute -AncestorsOnly
        $hash = (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash.ToLowerInvariant()
        $lines.Add("$path`t$hash")
    }
    $lines.Sort([StringComparer]::Ordinal)
    $digest = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))))
    [ordered]@{ revision = $revision.Trim(); buildInputSha256 = $digest; fileCount = $lines.Count
        scope = 'src, tests, installer, eng, root props/targets, global.json and Kora.slnx; tracked and nonignored untracked files'
        digestDefinition = 'Ordinal-sorted relative-path<TAB>lowercase-SHA256, LF joined, no final LF, UTF-8 without BOM'
    }
}

function Get-NativeUxTestClasses {
    @('Kora.Windows.IntegrationTests.NativeUx.NativeUxFixtureContractTests',
        'Kora.Windows.IntegrationTests.AccessibilityRuntimeContractTests',
        'Kora.Windows.IntegrationTests.BoundedSurfaceAccessibilityTests',
        'Kora.Windows.IntegrationTests.NativeQuestionWindowContractTests',
        'Kora.Windows.IntegrationTests.SessionsViewModelTests',
        'Kora.Windows.IntegrationTests.DetailWindowContractTests')
}

function Get-NativeUxTestSummary {
    param([string] $Path)
    [xml]$trx = Get-Content -LiteralPath $Path -Raw
    $counters = $trx.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
    if ($null -eq $counters -or [int]$counters.total -le 0 -or
        [int]$counters.total -ne [int]$counters.executed -or
        [int]$counters.total -ne [int]$counters.passed -or [int]$counters.failed -ne 0) {
        throw 'The focused test report must contain only executed, passing tests, with no skips.'
    }
    $methods = @($trx.SelectNodes("//*[local-name()='TestDefinitions']/*[local-name()='UnitTest']/*[local-name()='TestMethod']"))
    foreach ($class in Get-NativeUxTestClasses) {
        if (@($methods | Where-Object className -CEQ $class).Count -eq 0) {
            throw "Required test class absent from report: $class"
        }
    }
    [ordered]@{ total = [int]$counters.total; passed = [int]$counters.passed; failed = 0; skipped = 0
        classes = @(Get-NativeUxTestClasses); reportSha256 = (Get-FileHash -LiteralPath $Path).Hash.ToLowerInvariant() }
}

function Get-NativeUxObservationTemplate {
    [ordered]@{ schema = 1; operatorConfirmed = $false
        display = [ordered]@{ textScalePercent = $null; enlargedTextScalePercent = $null
            primaryDisplayScalePercent = $null; secondDisplayScalePercent = $null; monitorCount = $null }
        observations = @(
            [ordered]@{ id = 'SR01'; outcome = 'Pending'; note = 'Narrator names, roles, values and keyboard focus on Appearance, Sessions, Guide/details and the local-version question.' },
            [ordered]@{ id = 'SR02'; outcome = 'Pending'; note = 'Question review/draft/submit and stale-revision refusal are announced; terminal status and focus remain correct without repetitive refresh announcements.' },
            [ordered]@{ id = 'TXT01'; outcome = 'Pending'; note = 'Light and Dark at current and enlarged Windows text scale: descriptions, inactive navigation, focused controls and long fixture content remain readable without clipping or lost actions.' },
            [ordered]@{ id = 'DPI01'; outcome = 'Pending'; note = 'Native windows at the recorded primary-display scale: resizing and keyboard traversal preserve visible focus, readable content and reachable actions.' },
            [ordered]@{ id = 'DPI02'; outcome = 'Pending'; note = 'Move open fixture windows between physical monitors with different recorded scales; check layout, focus, details and question continuity. Blocked if unavailable.' }
        )
        settingsRestored = $false; narratorStoppedOrRestored = $false
        scope = 'Synthetic native UX only, not real privacy/ownership, audio capture, rendered numerical contrast or full R05/R12/R14/A4 qualification.'
    }
}

function Assert-NativeUxBundle {
    param([string] $Directory)
    Assert-NoLinks $Directory
    $receipt = Read-NativeUxJson (Join-Path $Directory 'automated.json')
    if ($receipt.schema -ne 1 -or $receipt.status -cne 'Pass' -or $receipt.nativeAcceptance -cne 'Pending') {
        throw 'A passing focus-free preparation receipt with pending native acceptance is required.'
    }
    $tests = Get-NativeUxTestSummary (Join-Path $Directory 'tests\focused.trx')
    if ($tests.reportSha256 -cne $receipt.tests.reportSha256) { throw 'The test report changed after preparation.' }
    $actual = @(Get-PayloadFiles (Join-Path $Directory 'payload'))
    if (($actual | ConvertTo-Json -Depth 10 -Compress) -cne
        ($receipt.payload | ConvertTo-Json -Depth 10 -Compress)) {
        throw 'The prepared payload changed; prepare a fresh bundle and repeat the observations.'
    }
    $receipt
}

function Get-NativeUxSignOffStatus {
    param($Observations)
    $template = Get-NativeUxObservationTemplate
    if ($Observations.schema -ne 1 -or $Observations.operatorConfirmed -isnot [bool] -or !$Observations.operatorConfirmed -or
        $Observations.settingsRestored -isnot [bool] -or !$Observations.settingsRestored -or
        $Observations.narratorStoppedOrRestored -isnot [bool] -or !$Observations.narratorStoppedOrRestored) {
        throw 'Confirm actual operator review and restoration of display/Narrator settings before sign-off.'
    }
    $rows = @($Observations.observations)
    $expected = @($template.observations.id | Sort-Object)
    $actual = @($rows.id | Sort-Object)
    if (($actual -join ',') -cne ($expected -join ',')) { throw 'All five unique observation IDs are required.' }
    foreach ($row in $rows) {
        if ($row.outcome -cnotin 'Pass', 'Fail', 'Blocked' -or
            $row.note -isnot [string] -or [string]::IsNullOrWhiteSpace($row.note)) {
            throw 'Every observation needs an explicit Pass, Fail or Blocked and a factual note; Pending is not sign-off.'
        }
    }
    foreach ($field in 'textScalePercent', 'enlargedTextScalePercent', 'primaryDisplayScalePercent', 'monitorCount') {
        $value = $Observations.display[$field]
        if ($value -isnot [long] -and $value -isnot [int]) { throw "Record an integer display value: $field" }
        if ($value -le 0) { throw "Record a positive display value: $field" }
    }
    if (($rows | Where-Object id -CEQ 'TXT01').outcome -ceq 'Pass' -and
        $Observations.display.enlargedTextScalePercent -le $Observations.display.textScalePercent) {
        throw 'A text-scale pass requires an actually larger recorded text scale.'
    }
    if (($rows | Where-Object id -CEQ 'DPI02').outcome -ceq 'Pass' -and
        ($Observations.display.monitorCount -lt 2 -or
            ($Observations.display.secondDisplayScalePercent -isnot [long] -and
                $Observations.display.secondDisplayScalePercent -isnot [int]) -or
            $Observations.display.secondDisplayScalePercent -le 0 -or
            $Observations.display.secondDisplayScalePercent -eq $Observations.display.primaryDisplayScalePercent)) {
        throw 'A mixed-DPI pass requires two physical monitors with different recorded positive scale values.'
    }
    if (@($rows | Where-Object outcome -CEQ 'Fail').Count -gt 0) { return 'Failed' }
    if (@($rows | Where-Object outcome -CEQ 'Blocked').Count -gt 0) { return 'Partial' }
    'ScopedPass'
}
