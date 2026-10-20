[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot 'Run-WindowsIntegrationTests.ps1'
$root = Split-Path -Parent $PSScriptRoot
$workflow = Get-Content -Raw -LiteralPath (Join-Path $root '.github\workflows\ci.yml')
$assertions = 0

function Assert {
    param([bool] $Condition, [string] $Message)
    $script:assertions++
    if (-not $Condition) { throw $Message }
}

$expected = @('storage-heavy', 'storage-other', 'ui-definitions', 'native-ux', 'ui-platform')
$all = @(& $runner -ListShards)
Assert ($all.Count -eq 5) 'Default scheduling must retain all five shards.'
Assert (($all.Name -join ',') -ceq ($expected -join ',')) 'Shard identities or order changed.'
foreach ($name in $expected) {
    $selected = @(& $runner -Shard $name -ListShards)
    Assert ($selected.Count -eq 1) "Selector must return exactly one shard: $name."
    Assert ($selected[0].Name -ceq $name) "Selector returned a different shard: $name."
    Assert ($selected[0].Filter -ceq ($all | Where-Object Name -EQ $name).Filter) 'Selection changed a filter.'
}
$rejected = $false
try { & $runner -Shard 'unknown' -ListShards | Out-Null }
catch [System.Management.Automation.ParameterBindingException] { $rejected = $true }
Assert $rejected 'Unknown shards must fail, not run a default or empty success.'

$classes = [ordered]@{
    'Storage.WindowsSqliteSessionAttachmentTests' = 'storage-heavy'
    'Storage.WindowsSqliteConversationTests' = 'storage-heavy'
    'Storage.WindowsSqliteSessionQueueTests' = 'storage-heavy'
    'Storage.WindowsSqliteMemoryTests' = 'storage-heavy'
    'Storage.WindowsSqliteExactGrantFilterTests' = 'storage-heavy'
    'Storage.WindowsSqliteEvidenceQueryTests' = 'storage-other'
    'Storage.FutureStorageTests' = 'storage-other'
    'ArtifactDiscovery.OwnedDefinitionEditorWindowTests' = 'ui-definitions'
    'ArtifactDiscovery.FutureDefinitionTests' = 'ui-definitions'
    'EvidenceLiteralFindTests' = 'ui-definitions'
    'BoundedSurfaceAccessibilityTests' = 'ui-definitions'
    'NativeUx.NativeUxFixtureContractTests' = 'native-ux'
    'NativeUx.FutureNativeUxTests' = 'native-ux'
    'Presentation.DispatcherEvidenceResponsivenessTests' = 'native-ux'
    'NativeReaderTextScaleTests' = 'native-ux'
    'SpeechCaptionDisplayTests' = 'native-ux'
    'EvidenceAdvancedFilterTests' = 'ui-platform'
    'EvidenceLiteralFindTestsExtra' = 'ui-platform'
    'BoundedSurfaceAccessibilityTestsExtra' = 'ui-platform'
    'Presentation.DispatcherEvidenceResponsivenessTestsExtra' = 'ui-platform'
    'NativeReaderTextScaleTestsExtra' = 'ui-platform'
    'SpeechCaptionDisplayTestsExtra' = 'ui-platform'
    'SessionsConversationTests' = 'ui-platform'
    'NativeQuestionTests' = 'ui-platform'
    'FuturePlatformTests' = 'ui-platform'
}
foreach ($class in $classes.Keys) {
    $qualified = "Kora.Windows.IntegrationTests.$class.Test"
    $matching = @($all | Where-Object {
        $matchesFilter = $false
        foreach ($alternative in ($_.Filter -split '\|')) {
            $matchesAll = $true
            foreach ($predicate in ($alternative -split '&')) {
                if ($predicate -cmatch '^FullyQualifiedName(!?)~(.+)$') {
                    $contains = $qualified.Contains($Matches[2], [StringComparison]::Ordinal)
                    $matchesAll = $matchesAll -and $(if ($Matches[1] -ceq '!') { -not $contains } else { $contains })
                }
                else { throw "Unsupported scheduling predicate: $predicate." }
            }
            $matchesFilter = $matchesFilter -or $matchesAll
        }
        $matchesFilter
    })
    Assert ($matching.Count -eq 1) "Partition must cover $class exactly once."
    Assert ($matching[0].Name -ceq $classes[$class]) "Partition placed $class in the wrong shard."
}

Assert ($workflow.Contains('shard: [storage-heavy, storage-other, ui-definitions, native-ux, ui-platform]',
    [StringComparison]::Ordinal)) 'CI matrix must include every maintained shard.'
Assert ($workflow.Contains('fail-fast: false', [StringComparison]::Ordinal)) 'One failure must not cancel other evidence.'
Assert ($workflow.Contains('-Shard ''${{ matrix.shard }}''', [StringComparison]::Ordinal)) 'CI must execute its exact matrix shard.'
Assert ($workflow.Contains('name: windows-test-results-${{ matrix.shard }}',
    [StringComparison]::Ordinal)) 'Matrix artifacts must have distinct names.'
Assert ($workflow.Contains('needs: [version, windows-integration-shards]',
    [StringComparison]::Ordinal)) 'The required aggregate must depend on the complete matrix.'
Assert ($workflow.Contains("if: always() && needs.version.outputs.already-published != 'true'",
    [StringComparison]::Ordinal)) 'Failed or cancelled dependencies must not skip the required gate.'
Assert ($workflow.Contains('name: Windows integration tests', [StringComparison]::Ordinal)) 'Preserve the required check identity.'
Assert ($workflow.Contains('timeout-minutes: 30', [StringComparison]::Ordinal)) 'Preserve the Windows job deadline.'
Assert ($workflow.Contains('KORA_TEST_NATIVE_EVIDENCE_MODE: ${{ matrix.shard == ''native-ux'' && ''buffered'' || ''synchronous'' }}',
    [StringComparison]::Ordinal)) 'Only the native-UX CI shard may select the measured buffered fixture profile.'
Assert ($workflow.Contains("if: github.event_name == 'workflow_dispatch' && inputs.compare_native_evidence",
    [StringComparison]::Ordinal)) 'Native evidence comparisons must remain opt-in and manual-only.'
$comparisonCases = @(& (Join-Path $PSScriptRoot 'Compare-NativeFixtureEvidence.ps1') -ListCases)
Assert ($comparisonCases.Count -eq 4) 'The native evidence comparison must retain its four representative case selectors.'
foreach ($selector in $comparisonCases) {
    Assert ($selector.StartsWith('FullyQualifiedName~Kora.Windows.IntegrationTests.', [StringComparison]::Ordinal)) 'Comparison selectors must stay inside the Windows test assembly.'
}

$gateMatch = [regex]::Match($workflow,
    '(?m)      - name: Require every Windows shard to succeed\r?\n[\s\S]*?        run: \|\r?\n(?<script>(?:          [^\r\n]*(?:\r?\n|$))+)')
Assert $gateMatch.Success 'The maintained aggregate guard was not found.'
$gateText = ($gateMatch.Groups['script'].Value -split '\r?\n' | ForEach-Object { $_ -replace '^          ', '' }) -join "`n"
$gate = [scriptblock]::Create($gateText)
$saved = [Environment]::GetEnvironmentVariable('SHARD_RESULT')
try {
    foreach ($result in @('success', 'failure', 'cancelled', 'skipped', '', 'unknown', 'SUCCESS')) {
        [Environment]::SetEnvironmentVariable('SHARD_RESULT', $result)
        $failed = $false
        try { & $gate }
        catch { $failed = $true }
        Assert ($failed -eq ($result -cne 'success')) "Aggregate outcome was not fail-closed for '$result'."
    }
}
finally {
    [Environment]::SetEnvironmentVariable('SHARD_RESULT', $saved)
}
Write-Host "Windows integration scheduling: $assertions assertions passed."
