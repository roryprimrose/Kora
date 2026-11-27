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

# Classes measured above this many seconds are packed as whole methods rather than one class unit.
$WindowsSplitClassSeconds = 240.0
# Windows limits a process command line to 32,767 characters; keep each generated filter well inside it.
$WindowsMaximumFilterLength = 28000

function ConvertTo-WindowsTestDurations {
    param([string] $RetainedResultsDirectory, [long] $SourceRun, [string] $SourceRevision)

    $classSeconds = @{}
    $methodSeconds = @{}
    $cases = 0
    foreach ($report in Get-ChildItem -LiteralPath $RetainedResultsDirectory -Filter '*.trx' -Recurse -File) {
        [xml] $trx = Get-Content -Raw -LiteralPath $report.FullName
        $definitions = @{}
        foreach ($test in $trx.TestRun.TestDefinitions.UnitTest) { $definitions[$test.id] = $test.TestMethod }
        foreach ($result in @($trx.TestRun.Results.UnitTestResult)) {
            if ($result.outcome -cne 'Passed') { continue }
            $method = $definitions[$result.testId]
            $seconds = [TimeSpan]::Parse($result.duration).TotalSeconds
            $classSeconds[$method.className] = $(if ($classSeconds.ContainsKey($method.className)) { $classSeconds[$method.className] } else { 0.0 }) + $seconds
            $qualified = "$($method.className).$($method.name)"
            $methodSeconds[$qualified] = $(if ($methodSeconds.ContainsKey($qualified)) { $methodSeconds[$qualified] } else { 0.0 }) + $seconds
            $cases++
        }
    }
    if ($cases -eq 0) { throw 'Duration evidence requires passing retained results.' }
    $total = ($classSeconds.Values | Measure-Object -Sum).Sum
    $split = @($classSeconds.Keys | Where-Object { $classSeconds[$_] -gt $WindowsSplitClassSeconds })
    $classes = [ordered]@{}
    foreach ($name in ($classSeconds.Keys | Sort-Object { $_ } -CaseSensitive)) { $classes[$name] = [Math]::Round($classSeconds[$name], 1) }
    $methods = [ordered]@{}
    foreach ($name in ($methodSeconds.Keys | Sort-Object { $_ } -CaseSensitive)) {
        if ($split -ccontains $name.Substring(0, $name.LastIndexOf('.'))) { $methods[$name] = [Math]::Round($methodSeconds[$name], 1) }
    }
    [ordered]@{
        Version = 1
        SourceRun = $SourceRun
        SourceRevision = $SourceRevision
        DefaultCaseSeconds = [Math]::Round($total / $cases, 3)
        Classes = $classes
        Methods = $methods
    }
}

function Get-WindowsExecutionPartitions {
    param($Catalog, $Durations, [string[]] $Names, [string] $NativeName, [string[]] $NativePrefixes)

    $default = [double] $Durations.DefaultCaseSeconds
    $classWeights = @{}; foreach ($p in $Durations.Classes.PSObject.Properties) { $classWeights[$p.Name] = [double] $p.Value }
    $methodWeights = @{}; foreach ($p in $Durations.Methods.PSObject.Properties) { $methodWeights[$p.Name] = [double] $p.Value }
    $splitClasses = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $methodWeights.Keys) { $splitClasses.Add($name.Substring(0, $name.LastIndexOf('.'))) | Out-Null }

    $nativeSeconds = 0.0
    $nativeCases = 0
    $byClass = [ordered]@{}
    foreach ($method in $Catalog.Methods) {
        $class = $method.Name.Substring(0, $method.Name.LastIndexOf('.'))
        if (@($NativePrefixes | Where-Object { $method.Name.StartsWith($_, [StringComparison]::Ordinal) }).Count -gt 0) {
            $nativeCases += $method.Cases
            continue
        }
        if (-not $byClass.Contains($class)) { $byClass[$class] = [Collections.Generic.List[object]]::new() }
        $byClass[$class].Add($method)
    }
    foreach ($class in @($Catalog.Methods.Name | ForEach-Object { $_.Substring(0, $_.LastIndexOf('.')) } | Sort-Object -Unique)) {
        if (@($NativePrefixes | Where-Object { "$class.".StartsWith($_, [StringComparison]::Ordinal) }).Count -gt 0) {
            $nativeSeconds += $(if ($classWeights.ContainsKey($class)) { $classWeights[$class] } else { 0.0 })
        }
    }

    $units = [Collections.Generic.List[object]]::new()
    foreach ($class in $byClass.Keys) {
        $methods = $byClass[$class]
        if ($splitClasses.Contains($class)) {
            # Whole methods keep every theory row together; a method name that prefixes another joins its unit.
            $ordered = @($methods | Sort-Object { $_.Name.Length }, { $_.Name } -CaseSensitive)
            $owners = @{}
            foreach ($method in $ordered) {
                $owner = $method.Name
                foreach ($candidate in $owners.Keys) {
                    if ($method.Name.StartsWith($candidate, [StringComparison]::Ordinal)) { $owner = $owners[$candidate]; break }
                }
                $owners[$method.Name] = $owner
            }
            $groups = [ordered]@{}
            foreach ($method in $ordered) {
                $owner = $owners[$method.Name]
                if (-not $groups.Contains($owner)) { $groups[$owner] = [Collections.Generic.List[object]]::new() }
                $groups[$owner].Add($method)
            }
            foreach ($owner in $groups.Keys) {
                $seconds = 0.0
                foreach ($method in $groups[$owner]) {
                    $seconds += $(if ($methodWeights.ContainsKey($method.Name)) { $methodWeights[$method.Name] } else { $method.Cases * $default })
                }
                $units.Add([pscustomobject]@{ Key = $owner; Seconds = $seconds; Cases = ($groups[$owner] | Measure-Object Cases -Sum).Sum
                    Needles = @($groups[$owner] | ForEach-Object Name) })
            }
        }
        else {
            $cases = ($methods | Measure-Object Cases -Sum).Sum
            $seconds = if ($classWeights.ContainsKey($class)) { $classWeights[$class] } else { $cases * $default }
            $units.Add([pscustomobject]@{ Key = $class; Seconds = $seconds; Cases = $cases; Needles = @("$class.") })
        }
    }

    $bins = [ordered]@{}
    foreach ($name in $Names) { if ($name -cne $NativeName) { $bins[$name] = [pscustomobject]@{ Seconds = 0.0; Cases = 0; Needles = [Collections.Generic.List[string]]::new() } } }
    $binNames = @($bins.Keys)
    foreach ($unit in ($units | Sort-Object @{ Expression = 'Seconds'; Descending = $true }, @{ Expression = 'Key'; Descending = $false } -CaseSensitive)) {
        $target = $binNames[0]
        foreach ($name in $binNames) { if ($bins[$name].Seconds -lt $bins[$target].Seconds) { $target = $name } }
        $bins[$target].Seconds += $unit.Seconds
        $bins[$target].Cases += $unit.Cases
        foreach ($needle in $unit.Needles) { $bins[$target].Needles.Add($needle) }
    }
    foreach ($name in $Names) {
        if ($name -ceq $NativeName) {
            [pscustomobject]@{ Name = $name; Filter = (($NativePrefixes | ForEach-Object { "FullyQualifiedName~$_" }) -join '|')
                EstimatedSeconds = [Math]::Round($nativeSeconds, 1); Cases = $nativeCases }
        }
        else {
            $bin = $bins[$name]
            if ($bin.Needles.Count -eq 0) { throw "Windows partition $name has no assigned tests." }
            $filter = ($bin.Needles | Sort-Object -CaseSensitive | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'
            if ($filter.Length -gt $WindowsMaximumFilterLength) {
                throw "Windows partition $name filter is $($filter.Length) characters; raise the method split threshold or regenerate durations."
            }
            [pscustomobject]@{ Name = $name; Filter = $filter; EstimatedSeconds = [Math]::Round($bin.Seconds, 1); Cases = $bin.Cases }
        }
    }
}

function Test-WindowsFilterMatch {
    param([string] $QualifiedName, [string] $Filter)

    foreach ($alternative in ($Filter -split '\|')) {
        $matchesAll = $true
        foreach ($predicate in ($alternative -split '&')) {
            if ($predicate -cmatch '^FullyQualifiedName(!?)~(.+)$') {
                $contains = $QualifiedName.Contains($Matches[2], [StringComparison]::Ordinal)
                $matchesAll = $matchesAll -and $(if ($Matches[1] -ceq '!') { -not $contains } else { $contains })
            }
            else { throw "Unsupported scheduling predicate: $predicate." }
        }
        if ($matchesAll) { return $true }
    }
    $false
}

function Assert-WindowsPartitionCoverage {
    param($Catalog, [object[]] $Partitions)

    $matchers = foreach ($partition in $Partitions) {
        $needles = @($partition.Filter -split '\|' | ForEach-Object {
                if ($_ -cnotmatch '^FullyQualifiedName~([^&|]+)$') { throw "Generated partition filter has an unsupported predicate: $_." }
                [regex]::Escape($Matches[1])
            })
        [regex]::new(($needles -join '|'), [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    }
    foreach ($method in $Catalog.Methods) {
        $owners = 0
        foreach ($matcher in $matchers) { if ($matcher.IsMatch($method.Name)) { $owners++ } }
        if ($owners -ne 1) { throw "Windows test method $($method.Name) is assigned to $owners partitions." }
    }
}
