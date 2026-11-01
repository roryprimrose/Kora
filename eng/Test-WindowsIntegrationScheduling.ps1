[CmdletBinding()]
param(
    # Optional complete retained TRX evidence checks every recorded case against the current partition.
    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string] $RetainedResultsDirectory
)

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

function Get-MatchingShard {
    param([string] $QualifiedName, [object[]] $Definitions = $all)

    $Definitions | Where-Object {
        $matchesFilter = $false
        foreach ($alternative in ($_.Filter -split '\|')) {
            $matchesAll = $true
            foreach ($predicate in ($alternative -split '&')) {
                if ($predicate -cmatch '^FullyQualifiedName(!?)~(.+)$') {
                    $contains = $QualifiedName.Contains($Matches[2], [StringComparison]::Ordinal)
                    $matchesAll = $matchesAll -and $(if ($Matches[1] -ceq '!') { -not $contains } else { $contains })
                }
                else { throw "Unsupported scheduling predicate: $predicate." }
            }
            $matchesFilter = $matchesFilter -or $matchesAll
        }
        $matchesFilter
    }
}

$expected = @('storage-heavy', 'storage-other', 'ui-definitions', 'native-ux', 'ui-platform')
$all = @(& $runner -ListShards)
$partitions = @(& $runner -ListPartitions)
$legacyPartitions = @('storage-heavy', 'storage-other', 'ui-definitions', 'ui-conversations', 'native-ux', 'ui-platform', 'ui-editors')
$expectedPartitions = @('storage-heavy', 'storage-other', 'ui-definitions', 'ui-definition-dialogs', 'ui-conversations', 'ui-conversation-actions', 'native-ux', 'ui-platform', 'ui-editors')
Assert (($partitions.Name -join ',') -ceq ($expectedPartitions -join ',')) 'Execute exactly nine disjoint partitions within the five hosted jobs.'
Assert ($all.Count -eq 5) 'Default scheduling must retain all five shards.'
Assert (($all.Name -join ',') -ceq ($expected -join ',')) 'Shard identities or order changed.'
foreach ($name in $expected) {
    $selected = @(& $runner -Shard $name -ListShards)
    Assert ($selected.Count -eq 1) "Selector must return exactly one shard: $name."
    Assert ($selected[0].Name -ceq $name) "Selector returned a different shard: $name."
    Assert ($selected[0].Filter -ceq ($all | Where-Object Name -EQ $name).Filter) 'Selection changed a filter.'
    $selectedPartitions = @(& $runner -Shard $name -ListPartitions)
    $expectedCount = switch ($name) { 'ui-definitions' { 4 }; 'ui-platform' { 2 }; default { 1 } }
    Assert ($selectedPartitions.Count -eq $expectedCount) "Wrong execution-partition count for $name."
    Assert (@($selectedPartitions | Where-Object Shard -CNE $name).Count -eq 0) 'A hosted selector must never execute another job''s partitions.'
}
$rejected = $false
try { & $runner -Shard 'unknown' -ListShards | Out-Null }
catch [System.Management.Automation.ParameterBindingException] { $rejected = $true }
Assert $rejected 'Unknown shards must fail, not run a default or empty success.'
$rejected = $false
try { & $runner -ListShards -ListPartitions | Out-Null }
catch { $rejected = $true }
Assert $rejected 'Ambiguous discovery must fail explicitly.'

$classes = [ordered]@{
    'Storage.WindowsSqliteSessionAttachmentTests' = 'storage-heavy'
    'Storage.WindowsSqliteConversationTests' = 'storage-heavy'
    'Storage.WindowsSqliteSessionQueueTests' = 'storage-heavy'
    'Storage.WindowsSqliteMemoryTests' = 'storage-heavy'
    'Storage.WindowsSqliteExactGrantFilterTests' = 'storage-heavy'
    'Storage.WindowsSqliteEvidenceQueryTests' = 'storage-other'
    'Storage.FutureStorageTests' = 'storage-other'
    'ArtifactDiscovery.OwnedDefinitionEditorWindowTests' = 'ui-platform'
    'ArtifactDiscovery.OwnedDefinitionEditorWindowTestsExtra' = 'ui-definitions'
    'ArtifactDiscovery.CustomizationWindowTests' = 'ui-definitions'
    'ArtifactDiscovery.PromptParameterWindowTests' = 'ui-definitions'
    'ArtifactDiscovery.CustomizationWindowTestsExtra' = 'ui-definitions'
    'ArtifactDiscovery.PromptParameterWindowTestsExtra' = 'ui-definitions'
    'ArtifactDiscovery.FutureDefinitionTests' = 'ui-definitions'
    'EvidenceLiteralFindTests' = 'ui-platform'
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
    'SessionsConversationTests' = 'ui-definitions'
    'SessionsConversationTestsExtra' = 'ui-platform'
    'SettingsSectionNavigationTests' = 'ui-definitions'
    'SettingsResetMenuTests' = 'ui-definitions'
    'SettingsSpecialistNavigationTests' = 'ui-definitions'
    'SettingsSectionNavigationTestsExtra' = 'ui-platform'
    'SettingsResetMenuTestsExtra' = 'ui-platform'
    'SettingsSpecialistNavigationTestsExtra' = 'ui-platform'
    'SessionRoutingTests' = 'ui-definitions'
    'PendingQuestionNavigationTests' = 'ui-definitions'
    'AllWorkWaitingQuestionNavigationTests' = 'ui-definitions'
    'SessionRoutingTestsExtra' = 'ui-platform'
    'PendingQuestionNavigationTestsExtra' = 'ui-platform'
    'AllWorkWaitingQuestionNavigationTestsExtra' = 'ui-platform'
    'NativeQuestionTests' = 'ui-platform'
    'FuturePlatformTests' = 'ui-platform'
}
foreach ($class in $classes.Keys) {
    $qualified = "Kora.Windows.IntegrationTests.$class.Test"
    $matching = @(Get-MatchingShard $qualified)
    Assert ($matching.Count -eq 1) "Partition must cover $class exactly once."
    Assert ($matching[0].Name -ceq $classes[$class]) "Partition placed $class in the wrong shard."
    $execution = @(Get-MatchingShard "Kora.Windows.IntegrationTests.$class.Test" -Definitions $partitions)
    Assert ($execution.Count -eq 1) "Execution must cover $class exactly once."
    Assert ($execution[0].Shard -ceq $classes[$class]) "Execution changed the hosted owner of $class."
}
$conversation = @(Get-MatchingShard 'Kora.Windows.IntegrationTests.SessionsConversationTests.Test' -Definitions $partitions)
$editor = @(Get-MatchingShard 'Kora.Windows.IntegrationTests.ArtifactDiscovery.OwnedDefinitionEditorWindowTests.Test' -Definitions $partitions)
Assert ($conversation[0].Name -ceq 'ui-conversations') 'New unclassified conversation methods must remain in the default serial conversation process.'
Assert ($editor[0].Name -ceq 'ui-editors') 'Isolate the complete editor class in its own serial UI process.'
foreach ($method in @('NativeExample', 'OwnedExample', 'NamedExample')) {
    $execution = @(Get-MatchingShard "Kora.Windows.IntegrationTests.SessionsConversationTests.$method" -Definitions $partitions)
    Assert ($execution.Count -eq 1 -and $execution[0].Name -ceq 'ui-conversation-actions') 'Keep each native/owned/named conversation method and all its theory rows in one action process.'
}
foreach ($method in @(
    'CriteriaEditsFenceLateDetailCallbacksWithoutClearingIndependentComposer'
    'InvalidConversationDatesRetainEditableInputsAndRefuseBeforeAnyBodyRead'
    'ModelAgentProposalCompletesOnlyThroughExactNativeReviewSeparateConsentAndJoinedOriginalSessionAnswer'
)) {
    $execution = @(Get-MatchingShard "Kora.Windows.IntegrationTests.SessionsConversationTests.$method" -Definitions $partitions)
    Assert ($execution.Count -eq 1 -and $execution[0].Name -ceq 'ui-conversations') 'All methods unfinished at the hosted timeout must remain scheduled, not disappear from the repair.'
}
foreach ($class in @('CustomizationWindowTests', 'PromptParameterWindowTests')) {
    $execution = @(Get-MatchingShard "Kora.Windows.IntegrationTests.ArtifactDiscovery.$class.Test" -Definitions $partitions)
    Assert ($execution.Count -eq 1 -and $execution[0].Name -ceq 'ui-definition-dialogs') 'Split the two complete measured dialog classes from remaining definitions work.'
}
foreach ($qualifiedName in @(
    'Kora.Windows.IntegrationTests.SessionsConversationTestsExtra.Test'
    'Kora.Windows.IntegrationTests.ArtifactDiscovery.OwnedDefinitionEditorWindowTestsExtra.Test'
    'Kora.Windows.IntegrationTests.SessionsConversationTestsExtra.NativeExample'
    'Kora.Windows.IntegrationTests.ArtifactDiscovery.CustomizationWindowTestsExtra.Test'
    'Kora.Windows.IntegrationTests.ArtifactDiscovery.PromptParameterWindowTestsExtra.Test'
)) {
    $execution = @(Get-MatchingShard $qualifiedName -Definitions $partitions)
    Assert ($execution.Count -eq 1 -and $execution[0].Name -notin @('ui-conversations', 'ui-conversation-actions', 'ui-definition-dialogs', 'ui-editors')) 'Exact class boundaries must not capture similarly named future classes.'
}

Assert ($workflow.Contains('shard: [storage-heavy, storage-other, ui-definitions, native-ux, ui-platform]',
    [StringComparison]::Ordinal)) 'CI matrix must include every maintained shard.'
Assert ($workflow.Contains('fail-fast: false', [StringComparison]::Ordinal)) 'One failure must not cancel other evidence.'
Assert ($workflow.Contains('-Shard ''${{ matrix.shard }}''', [StringComparison]::Ordinal)) 'CI must execute its exact matrix shard.'
Assert ($workflow.Contains('name: windows-test-results-${{ matrix.shard }}',
    [StringComparison]::Ordinal)) 'Matrix artifacts must have distinct names.'
Assert ($workflow.Contains('needs: [admission, version, windows-integration-shards]',
    [StringComparison]::Ordinal)) 'The required aggregate must depend on the complete matrix.'
Assert ($workflow.Contains("if: always() && needs.version.outputs.already-published != 'true' && (needs.admission.result != 'success' || needs.admission.outputs.full != 'false')",
    [StringComparison]::Ordinal)) 'Failed or cancelled dependencies must not skip the required gate.'
Assert ($workflow.Contains('name: Windows integration tests', [StringComparison]::Ordinal)) 'Preserve the required check identity.'
$shardJob = [regex]::Match($workflow, '(?ms)^  windows-integration-shards:\r?\n(?<job>.*?)(?=^  [a-z][a-z-]*:|\z)')
Assert $shardJob.Success 'The consumed Windows matrix job was not found.'
$job = $shardJob.Groups['job'].Value
$deadline = [regex]::Match($job, '(?m)^    timeout-minutes: (?<minutes>\d+)\s*$')
Assert ($deadline.Success -and $deadline.Groups['minutes'].Value -ceq '30') 'Preserve the exact whole-job 30-minute deadline.'
Assert ($job.Contains('ref: ${{ needs.version.outputs.source-revision }}', [StringComparison]::Ordinal)) 'Candidate jobs must consume the admitted source, including its scheduling script.'
Assert ($job.Contains('run: .\eng\Run-WindowsIntegrationTests.ps1 -Shard ''${{ matrix.shard }}''',
    [StringComparison]::Ordinal)) 'The trusted main workflow must consume candidate-owned shard assignments.'
Assert ($workflow.Contains('KORA_TEST_NATIVE_EVIDENCE_MODE: ${{ matrix.shard == ''native-ux'' && ''buffered'' || ''synchronous'' }}',
    [StringComparison]::Ordinal)) 'Only the native-UX CI shard may select the measured buffered fixture profile.'
$nativeFilter = @(
    'Kora.Windows.IntegrationTests.NativeUx.'
    'Kora.Windows.IntegrationTests.Presentation.DispatcherEvidenceResponsivenessTests.'
    'Kora.Windows.IntegrationTests.NativeReaderTextScaleTests.'
    'Kora.Windows.IntegrationTests.SpeechCaptionDisplayTests.'
) | ForEach-Object { "FullyQualifiedName~$_" }
Assert (($all | Where-Object Name -EQ 'native-ux').Filter -ceq ($nativeFilter -join '|')) 'Rebalancing must not move synchronous classes into the buffered profile.'
$collection = Get-Content -Raw -LiteralPath (Join-Path $root 'tests\Kora.Windows.IntegrationTests\HeadlessUiTestGroup.cs')
Assert ($collection.Contains('[CollectionDefinition(nameof(HeadlessUiTestGroup), DisableParallelization = true)]',
    [StringComparison]::Ordinal)) 'Headless UI tests must remain serial within each process.'

# Run 40468340968: whole test-step wall times, not sums of concurrently executed test durations.
# Conservatively charge every shard the largest observed setup (including storage-only checks)
# and post-test cost. This is a fixed scheduling regression, not a hosted speed guarantee.
$testSeconds = [ordered]@{
    'storage-heavy' = 311.0
    'storage-other' = 252.0
    'ui-definitions' = 477.0
    'native-ux' = 463.0
    'ui-platform' = 1536.0
}
$setupSeconds = 460.0
$postTestSeconds = 6.0
$conversationSeconds = 544.348
$deadlineSeconds = [int] $deadline.Groups['minutes'].Value * 60
Assert (($testSeconds['ui-platform'] + 264 + 6) -gt $deadlineSeconds) 'The fixture must reproduce the observed whole-job exhaustion despite passing tests.'
$destination = @(Get-MatchingShard 'Kora.Windows.IntegrationTests.SessionsConversationTests.Test')
Assert ($destination.Count -eq 1 -and $destination[0].Name -ceq 'ui-definitions') 'The measured conversation class must move intact to the synchronous definitions shard.'
$testSeconds['ui-platform'] -= $conversationSeconds
$testSeconds[$destination[0].Name] += $conversationSeconds
foreach ($name in $expected) {
    $wholeJobSeconds = $setupSeconds + $testSeconds[$name] + $postTestSeconds
    Assert ($wholeJobSeconds -lt $deadlineSeconds) "Measured $name workload plus complete conservative overhead exceeds the unchanged deadline: $wholeJobSeconds seconds."
}
Assert (($testSeconds.Values | Measure-Object -Maximum).Maximum -lt 1536) 'Rebalancing must reduce the measured critical path, not just move the deadline failure.'

# Run 40781657098: all 6,284 cases passed, but definitions' complete job was cancelled
# at 1,804s. Keep each shard's measured setup, storage-only checks and teardown cost.
# These are retained-run predictions, not execution or timing guarantees for a new head.
$measuredJobs = [ordered]@{
    'storage-heavy' = @{ Cases = 475; Setup = 231.0; Test = 201.0; Post = 5.0 }
    'storage-other' = @{ Cases = 897; Setup = 582.0; Test = 156.0; Post = 6.0 }
    'ui-definitions' = @{ Cases = 1450; Setup = 264.0; Test = 1534.0; Post = 6.0 }
    'native-ux' = @{ Cases = 98; Setup = 241.0; Test = 344.0; Post = 6.0 }
    'ui-platform' = @{ Cases = 3364; Setup = 233.0; Test = 1190.0; Post = 5.0 }
}
$findCases = 115
$findSeconds = 132.769
$originalJobSeconds = @{}
$predictedJobSeconds = @{}
$predictedCases = @{}
foreach ($name in $expected) {
    $measurement = $measuredJobs[$name]
    $originalJobSeconds[$name] = $measurement.Setup + $measurement.Test + $measurement.Post
    $predictedJobSeconds[$name] = $originalJobSeconds[$name]
    $predictedCases[$name] = $measurement.Cases
}
Assert ($originalJobSeconds['ui-definitions'] -eq 1804 -and $originalJobSeconds['ui-definitions'] -gt $deadlineSeconds) 'The exact-run fixture must reproduce the cancelled whole job, not just its successful test step.'
Assert ($originalJobSeconds['ui-platform'] -eq 1428) 'The destination budget must include its complete measured job.'
$findDestination = @(Get-MatchingShard 'Kora.Windows.IntegrationTests.EvidenceLiteralFindTests.Test')
Assert ($findDestination.Count -eq 1 -and $findDestination[0].Name -ceq 'ui-platform') 'Move the complete measured Find class to the synchronous platform shard.'
$predictedJobSeconds['ui-definitions'] -= $findSeconds
$predictedJobSeconds[$findDestination[0].Name] += $findSeconds
$predictedCases['ui-definitions'] -= $findCases
$predictedCases[$findDestination[0].Name] += $findCases
foreach ($name in $expected) {
    Assert ($predictedJobSeconds[$name] -lt $deadlineSeconds) "Run 40781657098 predicted $name whole job exceeds the unchanged deadline: $($predictedJobSeconds[$name]) seconds."
}
Assert (($predictedJobSeconds.Values | Measure-Object -Maximum).Maximum -lt
    ($originalJobSeconds.Values | Measure-Object -Maximum).Maximum) 'Reduce the exact-run critical path without moving its timeout to another shard.'
Assert (($predictedCases.Values | Measure-Object -Sum).Sum -eq 6284) 'The measured rebalance must preserve every recorded case.'
Assert ($predictedCases['ui-definitions'] -eq 1335 -and $predictedCases['ui-platform'] -eq 3479) 'Move all 115 Find cases exactly once.'

# Run 40815883491 cancelled platform at the whole-job cap with 2,516 passed cases
# and no failed assertions; definitions completed in 885s. This partial-run fixture
# checks measured work redistribution, not completion or hosted timing guarantees.
$settingsClasses = [ordered]@{
    'SettingsSectionNavigationTests' = @{ Cases = 14; Seconds = 175.465 }
    'SettingsResetMenuTests' = @{ Cases = 49; Seconds = 118.958 }
    'SettingsSpecialistNavigationTests' = @{ Cases = 7; Seconds = 90.980 }
}
$observedPlatformSeconds = 1818.0
$observedDefinitionsSeconds = 885.0
$settingsSeconds = 0.0
$settingsCases = 0
Assert ($observedPlatformSeconds -gt $deadlineSeconds) 'Reproduce the observed whole-job breach, not a test assertion failure.'
foreach ($class in $settingsClasses.Keys) {
    $target = @(Get-MatchingShard "Kora.Windows.IntegrationTests.$class.Test")
    Assert ($target.Count -eq 1 -and $target[0].Name -ceq 'ui-definitions') "Move the entire measured $class class to synchronous definitions."
    $settingsSeconds += $settingsClasses[$class].Seconds
    $settingsCases += $settingsClasses[$class].Cases
}
Assert ($settingsCases -eq 70) 'Preserve every measured settings case.'
$redistributedPlatformSeconds = $observedPlatformSeconds - $settingsSeconds
$redistributedDefinitionsSeconds = $observedDefinitionsSeconds + $settingsSeconds
Assert ($redistributedPlatformSeconds -lt $deadlineSeconds) 'The retained platform work must fit without extending the job cap.'
Assert ($redistributedDefinitionsSeconds -lt $deadlineSeconds) 'Do not transfer the measured budget breach to definitions.'
Assert ([Math]::Max($redistributedPlatformSeconds, $redistributedDefinitionsSeconds) -lt
    $observedPlatformSeconds) 'Reduce the measured critical path.'

# Run 40836599680 retained only 1,400 of 1,421 definitions cases before cancellation.
# Project only recorded work; the remaining cases still require new-head execution.
$editorCases = 288
$editorSeconds = 381.4
$cancelledDefinitionsSeconds = 1819.0
$completedPlatformSeconds = 1114.0
Assert ($cancelledDefinitionsSeconds -gt $deadlineSeconds) 'Reproduce the exact definitions whole-job cancellation.'
$editorDestination = @(Get-MatchingShard 'Kora.Windows.IntegrationTests.ArtifactDiscovery.OwnedDefinitionEditorWindowTests.Test')
Assert ($editorDestination.Count -eq 1 -and $editorDestination[0].Name -ceq 'ui-platform') 'Move the complete measured editor class to synchronous platform.'
$projectedDefinitionsSeconds = $cancelledDefinitionsSeconds - $editorSeconds
$projectedPlatformSeconds = $completedPlatformSeconds + $editorSeconds
Assert ($projectedDefinitionsSeconds -lt 0.85 * $deadlineSeconds) 'Leave headroom for unfinished definitions work without extending the cap.'
Assert ($projectedPlatformSeconds -lt 0.85 * $deadlineSeconds) 'Do not transfer the measured budget breach to platform.'
Assert ([Math]::Max($projectedDefinitionsSeconds, $projectedPlatformSeconds) -lt
    $cancelledDefinitionsSeconds) 'Reduce the recorded critical path.'
Assert ((1400 - $editorCases) -eq 1112 -and (3454 + $editorCases) -eq 3742) 'Move all 288 recorded editor cases exactly once.'
Assert (((1400 - $editorCases) + (3454 + $editorCases)) -eq (1400 + 3454)) 'Preserve every recorded case despite partial definitions evidence.'

# Run 40840133406 cancelled definitions at 1,815s with only partial results.
# Charge complete measured jobs; serial UI durations only redistribute recorded work.
$editorDestination = @(Get-MatchingShard 'Kora.Windows.IntegrationTests.ArtifactDiscovery.OwnedDefinitionEditorWindowTests.Test')
Assert ($editorDestination.Count -eq 1 -and $editorDestination[0].Name -ceq 'ui-platform') 'Keep the complete editor class on synchronous platform.'
$navigationClasses = [ordered]@{
    'SessionRoutingTests' = @{ Cases = 35; Seconds = 75.08 }
    'PendingQuestionNavigationTests' = @{ Cases = 18; Seconds = 74.11 }
    'AllWorkWaitingQuestionNavigationTests' = @{ Cases = 55; Seconds = 71.86 }
}
$navigationSeconds = 0.0
$navigationCases = 0
foreach ($class in $navigationClasses.Keys) {
    $target = @(Get-MatchingShard "Kora.Windows.IntegrationTests.$class.Test")
    Assert ($target.Count -eq 1 -and $target[0].Name -ceq 'ui-definitions') "Move the entire measured $class class to synchronous definitions."
    $navigationSeconds += $navigationClasses[$class].Seconds
    $navigationCases += $navigationClasses[$class].Cases
    $source = Get-Content -Raw -LiteralPath (Join-Path $root "tests\Kora.Windows.IntegrationTests\$class.cs")
    Assert ($source.Contains('[Collection(nameof(HeadlessUiTestGroup))]', [StringComparison]::Ordinal)) 'Redistributed UI durations must belong to the serial dispatcher collection.'
}
$candidateDefinitionsSeconds = 1815.0 - 376.0 + $navigationSeconds
$candidatePlatformSeconds = 1405.0 + 376.0 - $navigationSeconds
Assert (1815.0 -gt $deadlineSeconds) 'Reproduce the candidate definitions whole-job cancellation.'
Assert ($candidateDefinitionsSeconds -lt $deadlineSeconds - 120) 'Retain two minutes of measured definitions headroom for unfinished work.'
Assert ($candidatePlatformSeconds -lt $deadlineSeconds - 120) 'Do not transfer the measured budget breach to platform.'
Assert ($navigationCases -eq 108) 'Move all recorded navigation cases intact.'
Assert ((1412 - 288 + $navigationCases) + (3475 + 288 - $navigationCases) -eq 4887) 'Preserve every recorded candidate case despite partial definitions evidence.'

# Complete successful runs; retain measured setup/post costs rather than timing test steps alone.
foreach ($measurement in @(
    @{ Run = 41223826625; Definitions = 1483.0; Conversation = 872.376; Actions = 453.666; Dialogs = 202.146; DefinitionsOverhead = 245.0;
       Platform = 1536.0; Editor = 536.596; PlatformOverhead = 257.0 }
    @{ Run = 41229047847; Definitions = 1303.0; Conversation = 592.119; Actions = 304.304; Dialogs = 124.647; DefinitionsOverhead = 244.0;
       Platform = 993.0; Editor = 277.799; PlatformOverhead = 212.0 }
)) {
    $before = [Math]::Max($measurement.Definitions + $measurement.DefinitionsOverhead,
        $measurement.Platform + $measurement.PlatformOverhead)
    $definitionProcesses = @(
        $measurement.Actions
        ($measurement.Conversation - $measurement.Actions)
        $measurement.Dialogs
        ($measurement.Definitions - $measurement.Conversation - $measurement.Dialogs)
    )
    $definitionsAfter = ($definitionProcesses | Measure-Object -Maximum).Maximum + $measurement.DefinitionsOverhead
    $platformAfter = [Math]::Max($measurement.Editor,
        $measurement.Platform - $measurement.Editor) + $measurement.PlatformOverhead
    $after = [Math]::Max($definitionsAfter, $platformAfter)
    Assert ($after -lt 0.8 * $deadlineSeconds) "Run $($measurement.Run) must predict at least 20 percent deadline headroom."
    Assert ($after -lt 0.75 * $before) "Run $($measurement.Run) must predict at least a 25 percent shorter UI critical path."
    Write-Host ("Run {0}: projected UI critical path {1:N1}s -> {2:N1}s; shared-runner contention still requires new-head timing evidence." -f $measurement.Run, $before, $after)
}

# Run 41255145262 cancelled after 842/857 conversation cases; only project recorded work.
$recordedProcesses = @(803.976, 724.243, 582.331, 725.102)
$recordedCriticalPath = ($recordedProcesses | Measure-Object -Maximum).Maximum + 266 + 7
Assert ((266 + 1535 + 7) -eq 1808) 'Reproduce the hosted whole-job timeout, including setup and cleanup.'
Assert ($recordedCriticalPath -lt 0.8 * $deadlineSeconds) 'Recorded work must leave headroom without dropping the 15 unrecorded cases or extending the job deadline.'
Write-Host ("Run 41255145262: recorded-work projection {0:N1}s; partial evidence does not establish completion or shared-runner timing." -f $recordedCriticalPath)

if ($PSBoundParameters.ContainsKey('RetainedResultsDirectory')) {
    $reports = @(Get-ChildItem -LiteralPath $RetainedResultsDirectory -Filter '*.trx' -Recurse -File)
    Assert ($reports.Count -in @($expected.Count, $legacyPartitions.Count, $expectedPartitions.Count)) 'Retained evidence must contain a complete five-shard, legacy seven-partition or current nine-partition report set.'
    $reportNames = if ($reports.Count -eq $expected.Count) { $expected }
        elseif ($reports.Count -eq $legacyPartitions.Count) { $legacyPartitions }
        else { $expectedPartitions }
    $executions = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $recordedClasses = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $recordedCases = 0
    $partitionCases = @{}
    foreach ($name in $expectedPartitions) { $partitionCases[$name] = 0 }
    foreach ($name in $reportNames) {
        $report = @($reports | Where-Object { $_.Directory.Name -ceq $name })
        Assert ($report.Count -eq 1) "Retained evidence must contain exactly one $name report."
        [xml] $trx = Get-Content -Raw -LiteralPath $report[0].FullName
        $definitions = @{}
        foreach ($test in $trx.TestRun.TestDefinitions.UnitTest) {
            $definitions.Add($test.id, $test.TestMethod)
        }
        $results = @($trx.TestRun.Results.UnitTestResult)
        Assert ($results.Count -gt 0 -and $results.Count -eq [int] $trx.TestRun.ResultSummary.Counters.total) "Incomplete or empty $name report."
        Assert ([int] $trx.TestRun.ResultSummary.Counters.passed -eq $results.Count) "Retained $name evidence must be fully passing, not partial cancellation evidence."
        foreach ($result in $results) {
            Assert ($executions.Add($result.executionId)) 'Retained reports contain a duplicate test execution.'
            Assert ($definitions.ContainsKey($result.testId)) 'A retained result has no test definition.'
            $method = $definitions[$result.testId]
            Assert (-not [string]::IsNullOrWhiteSpace($method.className)) 'A retained test has no class identity.'
            $recordedClasses.Add($method.className) | Out-Null
            $matching = @(Get-MatchingShard "$($method.className).$($method.name)")
            Assert ($matching.Count -eq 1) "Partition must cover retained case $($result.testName) exactly once."
            $execution = @(Get-MatchingShard "$($method.className).$($method.name)" -Definitions $partitions)
            Assert ($execution.Count -eq 1) "Execution must cover retained case $($result.testName) exactly once."
            $sourceName = $execution[0].Name
            if ($reports.Count -eq $expected.Count) { $sourceName = $execution[0].Shard }
            elseif ($reports.Count -eq $legacyPartitions.Count) {
                $sourceName = switch ($sourceName) {
                    'ui-definition-dialogs' { 'ui-definitions' }
                    'ui-conversation-actions' { 'ui-conversations' }
                    default { $sourceName }
                }
            }
            Assert ($sourceName -ceq $name) 'A retained case must belong to its recorded source partition.'
            $partitionCases[$execution[0].Name]++
            $recordedCases++
        }
    }
    Write-Host "Retained partition: $recordedCases cases across $($recordedClasses.Count) classes covered exactly once."
    foreach ($name in $expectedPartitions) {
        Assert ($partitionCases[$name] -gt 0) "Projected execution partition $name must not be empty."
        Write-Host "$name`: $($partitionCases[$name]) retained cases."
    }
}

$fixtureRoot = Join-Path $root ".scratch\windows-scheduling-tests\$([guid]::NewGuid().ToString('N'))"
$fakeDirectory = Join-Path $fixtureRoot 'cli'
$fakePath = Join-Path $fakeDirectory 'dotnet.ps1'
New-Item -ItemType Directory -Path $fakeDirectory -Force | Out-Null
@'
$arguments = @($args)
$directory = $arguments[[Array]::IndexOf($arguments, '--results-directory') + 1]
$name = Split-Path -Leaf $directory
$arguments | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'command.json')
$mode = if ($name -ceq 'ui-conversations') { $env:KORA_WINDOWS_SCHEDULING_FIXTURE } else { 'success' }
Write-Output "Fixture execution: $name ($mode)"
try {
    if ($mode -ceq 'exit') { exit 7 }
    if ($mode -ceq 'missing') { exit 0 }
    $report = '<TestRun><Results><UnitTestResult outcome="Passed"/></Results><ResultSummary><Counters total="1" passed="1"/></ResultSummary></TestRun>'
    if ($mode -ceq 'empty') { $report = '<TestRun><Results/><ResultSummary><Counters total="0" passed="0"/></ResultSummary></TestRun>' }
    if ($mode -ceq 'incomplete') { $report = '<TestRun><Results><UnitTestResult outcome="Passed"/></Results><ResultSummary><Counters total="2" passed="2"/></ResultSummary></TestRun>' }
    if ($mode -ceq 'failed') { $report = '<TestRun><Results><UnitTestResult outcome="Failed"/></Results><ResultSummary><Counters total="1" passed="0"/></ResultSummary></TestRun>' }
    if ($mode -ceq 'skipped') { $report = '<TestRun><Results><UnitTestResult outcome="NotExecuted"/></Results><ResultSummary><Counters total="1" passed="0"/></ResultSummary></TestRun>' }
    if ($mode -ceq 'malformed') { $report = '<TestRun>' }
    Set-Content -LiteralPath (Join-Path $directory 'results.trx') -Value $report
    if ($mode -ceq 'duplicate') { Set-Content -LiteralPath (Join-Path $directory 'duplicate.trx') -Value $report }
    exit 0
}
finally { Set-Content -LiteralPath (Join-Path $directory 'terminal.txt') -Value 'joined' }
'@ | Set-Content -LiteralPath $fakePath
$savedPath = $env:PATH
$savedFixture = [Environment]::GetEnvironmentVariable('KORA_WINDOWS_SCHEDULING_FIXTURE')
try {
    $env:PATH = $fakeDirectory + [IO.Path]::PathSeparator + $savedPath
    $resolved = Get-Command dotnet -CommandType Application, ExternalScript | Select-Object -First 1
    Assert ($resolved.Source -ceq $fakePath) 'Execution fixtures must resolve only the fake CLI, never real tests or restores.'
    $env:KORA_WINDOWS_SCHEDULING_FIXTURE = 'success'
    $executionRoot = Join-Path $fixtureRoot 'success'
    & $runner -ResultsDirectory $executionRoot
    $commands = @(Get-ChildItem -LiteralPath $executionRoot -Filter 'command.json' -Recurse -File)
    Assert ($commands.Count -eq $expectedPartitions.Count) 'Run every execution partition exactly once.'
    foreach ($partition in $partitions) {
        $receipt = @($commands | Where-Object { $_.Directory.Name -ceq $partition.Name })
        Assert ($receipt.Count -eq 1) "Missing or duplicate actual invocation: $($partition.Name)."
        $arguments = @(Get-Content -Raw -LiteralPath $receipt[0].FullName | ConvertFrom-Json)
        Assert ($arguments[0] -ceq 'test') 'Only the test operation may execute.'
        Assert ($arguments[[Array]::IndexOf($arguments, '--filter') + 1] -ceq $partition.Filter) 'The actual process must use its exact disjoint filter.'
        Assert ($arguments -ccontains '--no-build' -and $arguments -ccontains '--report-trx') 'Each process must use the same built source and retain test evidence.'
        Assert ($arguments[[Array]::IndexOf($arguments, '--max-threads') + 1] -eq 2) 'Partitioning must preserve the existing per-process thread limit.'
        Assert (Test-Path -LiteralPath (Join-Path $receipt[0].Directory.FullName 'console.log')) 'Each invocation must retain its own console output.'
    }
    foreach ($mode in @('exit', 'missing', 'empty', 'incomplete', 'failed', 'skipped', 'malformed', 'duplicate')) {
        $env:KORA_WINDOWS_SCHEDULING_FIXTURE = $mode
        $modeRoot = Join-Path $fixtureRoot $mode
        $failure = $null
        try { & $runner -Shard 'ui-definitions' -ResultsDirectory $modeRoot }
        catch { $failure = $_ }
        Assert ($null -ne $failure) "Execution must fail explicitly for $mode partition evidence."
        $terminal = @(Get-ChildItem -LiteralPath $modeRoot -Filter 'terminal.txt' -Recurse -File)
        Assert ($terminal.Count -eq 4) 'Failure reporting must wait for all four selected execution partitions to finish.'
    }
    $env:KORA_WINDOWS_SCHEDULING_FIXTURE = 'success'
    & $runner -Shard 'ui-definitions' -ResultsDirectory $executionRoot
    $freshReports = @(Get-ChildItem -LiteralPath $executionRoot -Filter 'results.trx' -Recurse -File)
    Assert ($freshReports.Count -eq 13) 'A repeat run must retain four fresh reports without overwriting or consuming the previous nine reports.'
}
finally {
    $env:PATH = $savedPath
    [Environment]::SetEnvironmentVariable('KORA_WINDOWS_SCHEDULING_FIXTURE', $savedFixture)
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
}

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
