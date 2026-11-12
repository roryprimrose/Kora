function Get-WindowsTestCatalog {
    param([string] $DotnetPath, [string] $ModuleDirectory, [string] $SourceRevision = '')

    $assembly = Join-Path $ModuleDirectory 'Kora.Windows.IntegrationTests.dll'
    $discovery = @(& $DotnetPath exec $assembly -preEnumerateTheories -list full/json)
    if ($LASTEXITCODE -ne 0) { throw 'Unfiltered Windows test artifact discovery failed.' }
    $tests = @(($discovery -join "`n") | ConvertFrom-Json)
    if ($tests.Count -eq 0) { throw 'The Windows test catalog is empty.' }
    $methods = @{}
    $names = @{}
    foreach ($test in $tests) {
        $method = "$($test.Class).$($test.Method)"
        if ($method -cnotmatch '^Kora\.Windows\.IntegrationTests\.[A-Za-z0-9_.]+$' -or
            [string]::IsNullOrWhiteSpace($test.DisplayName) -or $test.Explicit -isnot [bool]) {
            throw 'The Windows test catalog contains an invalid test identity or explicit classification.'
        }
        if (-not $methods.ContainsKey($method)) { $methods[$method] = 0 }
        $methods[$method]++
        if (-not $names.ContainsKey($test.DisplayName)) {
            $names[$test.DisplayName] = [pscustomobject]@{ Name = $test.DisplayName; Cases = 0; Explicit = $test.Explicit }
        }
        if ($names[$test.DisplayName].Explicit -ne $test.Explicit) {
            throw 'The Windows test catalog contains conflicting explicit classifications.'
        }
        $names[$test.DisplayName].Cases++
    }
    [pscustomobject]@{
        Version = 2
        SourceRevision = $SourceRevision
        Total = $tests.Count
        Methods = @($methods.Keys | Sort-Object | ForEach-Object { [pscustomobject]@{ Name = $_; Cases = $methods[$_] } })
        Tests = @($names.Keys | Sort-Object | ForEach-Object { $names[$_] })
    }
}

function Assert-WindowsTestReport {
    param([xml] $Report, $Catalog)

    $explicitNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    if ($null -ne $Catalog) {
        if ($Catalog.Version -notin @(1, 2)) { throw 'Unsupported Windows test catalog version.' }
        if ($Catalog.Version -eq 2) {
            foreach ($test in $Catalog.Tests) {
                if ($test.Explicit -isnot [bool]) { throw 'Missing or invalid explicit test classification.' }
                if ($test.Explicit) { $explicitNames.Add($test.Name) | Out-Null }
            }
        }
    }
    $results = @($Report.TestRun.Results.UnitTestResult)
    $counters = $Report.TestRun.ResultSummary.Counters
    $passed = 0
    $notExecuted = 0
    foreach ($result in $results) {
        if ($explicitNames.Contains($result.GetAttribute('testName'))) {
            if ($result.outcome -cne 'NotExecuted') {
                throw 'Explicit qualification tests must remain unexecuted during deterministic validation.'
            }
            $notExecuted++
        }
        elseif ($result.outcome -ceq 'Passed') { $passed++ }
        else { throw 'Mandatory Windows tests must pass; unexpected skipped or failing evidence is not admissible.' }
    }
    if ($passed -eq 0 -or $results.Count -ne [int] $counters.total -or $passed -ne [int] $counters.passed -or
        $notExecuted -ne [int] $counters.GetAttribute('notExecuted')) {
        throw 'Windows tests did not produce complete, nonempty deterministic passing evidence.'
    }
    if ($notExecuted -gt 0) {
        Write-Host "Windows test evidence: $passed mandatory cases passed; $notExecuted explicit qualification cases remain unexecuted."
    }
}
