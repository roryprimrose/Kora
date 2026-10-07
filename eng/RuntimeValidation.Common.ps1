#requires -Version 7.5
. (Join-Path $PSScriptRoot 'Distribution.Common.ps1')

$script:RuntimeValidationBase = 'd0a8e82ef34b82c4d888803083050c2e9dff43cd'
$script:ReleasedRuntimeLifecycleProfile = 'separately approved released SDK 1.0.16/native 1.0.90 minimal HTTP/stdio; distinct bounded RT2 fixture'

function Read-RuntimeReceipt {
    param([string] $Path)
    Assert-NoLinks $Path -AncestorsOnly
    Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable -DateKind String
}

function Assert-RuntimePreparationCheckout {
    param([string] $RepositoryPath)
    Assert-NoLinks $RepositoryPath -AncestorsOnly
    if (!(Test-Path -LiteralPath (Join-Path $RepositoryPath '.git') -PathType Leaf)) {
        throw 'Runtime preparation requires a linked isolated worktree, not the main checkout.'
    }
    $head = (& git -C $RepositoryPath rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify runtime preparation HEAD.' }
    & git -C $RepositoryPath merge-base --is-ancestor $script:RuntimeValidationBase HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Runtime preparation is not based on the recorded approved main commit.' }
    $paths = @('experiments\r02-runtime-proof', 'experiments\r02-dotnet-control-proof',
        'experiments\r02-runtime-lifecycle-proof', 'experiments\r02-dotnet-management-proof')
    $changed = @(& git -C $RepositoryPath diff --name-only $script:RuntimeValidationBase -- @paths)
    if ($LASTEXITCODE -ne 0 -or $changed.Count -ne 0) {
        throw 'Historical runtime fixtures/evidence changed; refusing to reuse their approval or overwrite them.'
    }
    $files = @(& git -C $RepositoryPath ls-files -- @paths)
    if ($LASTEXITCODE -ne 0 -or $files.Count -eq 0) { throw 'Historical fixture inventory is unavailable.' }
    $identities = @(
        foreach ($file in $files) {
            $relative = $file.Replace('/', '\')
            $path = Join-Path $RepositoryPath $relative
            Assert-NoLinks $path -AncestorsOnly
            [ordered]@{ file = $relative; sha256 = (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() }
        }
    )
    [ordered]@{ head = $head; base = $script:RuntimeValidationBase; historicalFiles = $identities }
}

function Assert-RuntimeRegressionRows {
    param($Receipt, [string] $Name)
    $rows = @($Receipt.rows)
    if ($rows.Count -ne 45 -or
        @($rows | Where-Object { $_.id -eq 'hook-only-failure-witness' -and $_.status -ceq 'FAIL' }).Count -ne 1 -or
        @($rows | Where-Object { $_.id -ne 'hook-only-failure-witness' -and $_.status -ceq 'PASS' }).Count -ne 44 -or
        @($rows.id | Sort-Object -Unique).Count -ne 45) {
        throw "Incomplete historical $Name controls or lost rejected hook-only witness."
    }
}

function Get-RuntimeHistoricalProfiles {
    param([string] $RepositoryPath)
    $source = Read-RuntimeReceipt (Join-Path $RepositoryPath 'experiments\r02-dotnet-control-proof\evidence\results.json')
    $released = Read-RuntimeReceipt (Join-Path $RepositoryPath 'experiments\r02-dotnet-management-proof\evidence\runtime-results.json')
    $regression = Read-RuntimeReceipt (Join-Path $RepositoryPath 'experiments\r02-dotnet-management-proof\evidence\rt1-released-regression.json')
    $rt2 = Read-RuntimeReceipt (Join-Path $RepositoryPath 'experiments\r02-runtime-lifecycle-proof\evidence\disposition.json')
    $mg1 = Read-RuntimeReceipt (Join-Path $RepositoryPath 'experiments\r02-dotnet-management-proof\evidence\disposition.json')
    Assert-RuntimeRegressionRows $source 'source-built RT1'
    Assert-RuntimeRegressionRows $regression 'released RT1'
    if ($source.sdk -cne '1.0.16-rt1.source.f8ae645.1' -or $released.sdk -cne '1.0.16' -or
        $regression.sdk -cne $released.sdk -or $regression.sdkPackageSha256 -cne $released.sdkPackageSha256 -or
        $regression.sdkAssemblySha256 -cne $released.sdkAssemblySha256 -or
        $rt2.rt2 -cne 'BLOCKED' -or $rt2.tests.total -ne 20 -or $rt2.tests.passed -ne 20 -or
        $rt2.tests.failed -ne 0 -or $rt2.tests.skipped -ne 0 -or
        $mg1.tests.hostComponents.passed -ne 22 -or $mg1.tests.actualRuntime.passed -ne 16 -or
        @($released.rows).Count -ne 16 -or @($released.rows | Where-Object status -CNE 'Pass').Count -ne 0) {
        throw 'Historical runtime dispositions do not match the approved separate profiles.'
    }
    if ($source.sdkPackageSha256 -ceq $released.sdkPackageSha256 -or
        $source.sdkAssemblySha256 -ceq $released.sdkAssemblySha256 -or
        $source.sdkSourceCommit -cne $released.sourceCommit -or
        $source.sdkAssemblySha256 -cne $rt2.sdkAssemblySha256 -or
        $released.runtime -cne '1.0.90' -or $released.protocol -ne 3 -or
        $rt2.nativeLauncherSha256 -cne $released.nativeLauncherSha256 -or
        $rt2.nativePayloadSha256 -cne $released.nativePayloadSha256) {
        throw 'Historical source/released/native identities are inconsistent; never infer byte equivalence.'
    }
    $profiles = @(
        [ordered]@{
            id = 'historical-source-built'; sdk = $source.sdk; source = $source.sdkSourceCommit
            packageSha256 = $source.sdkPackageSha256; assemblySha256 = $source.sdkAssemblySha256
            runtime = $rt2.runtime; protocol = $rt2.protocol
            nativeLauncherSha256 = $rt2.nativeLauncherSha256; nativePayloadSha256 = $rt2.nativePayloadSha256
            rt1 = 'Historical Pass'; rt2 = 'Bounded observations Pass; all-path admission Blocked'
            mg1 = 'Not qualified by released-profile MG1'
        },
        [ordered]@{
            id = 'separately-approved-released'; sdk = $released.sdk; source = $released.sourceCommit
            packageSha256 = $released.sdkPackageSha256; assemblySha256 = $released.sdkAssemblySha256
            runtime = $released.runtime; protocol = $released.protocol
            nativeLauncherSha256 = $released.nativeLauncherSha256; nativePayloadSha256 = $released.nativePayloadSha256
            rt1 = 'Historical released regressions Pass'; rt2 = 'Blocked; applicable released-profile paths need qualification'
            mg1 = 'Historical Pass: 45 regressions, 22 host, 16 actual runtime cases'
        }
    )
    foreach ($profile in $profiles) {
        foreach ($field in @('packageSha256', 'assemblySha256', 'nativeLauncherSha256', 'nativePayloadSha256')) {
            if ($profile[$field] -cnotmatch '^[a-f0-9]{64}$') { throw "Invalid retained identity: $field." }
        }
    }
    return $profiles
}

function Test-RuntimeArtifact {
    param([string] $Name, [string] $Path, [string] $ExpectedSha256, [string] $AssemblySha256)
    $row = [ordered]@{ name = $Name; status = 'Blocked'; expectedSha256 = $ExpectedSha256; actualSha256 = $null; reason = 'Not supplied; no ambient cache search or acquisition.' }
    if (!$Path) { return $row }
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        $row.status = 'Failed'
        $row.reason = 'Supplied file is missing.'
        return $row
    }
    Assert-NoLinks $Path -AncestorsOnly
    $row.actualSha256 = (Get-FileHash -LiteralPath $Path).Hash.ToLowerInvariant()
    if ($row.actualSha256 -cne $ExpectedSha256) {
        $row.status = 'Failed'
        $row.reason = 'Bytes differ from the distinct approved profile.'
        return $row
    }
    if ($AssemblySha256) {
        $zip = [IO.Compression.ZipFile]::OpenRead($Path)
        try {
            $entries = @($zip.Entries | Where-Object FullName -CEQ 'lib/net10.0/GitHub.Copilot.SDK.dll')
            if ($entries.Count -ne 1) { throw 'SDK package must contain exactly one net10 assembly.' }
            $stream = $entries[0].Open()
            try { $actual = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            $row.assemblySha256 = $actual
            if ($actual -cne $AssemblySha256) {
                $row.status = 'Failed'
                $row.reason = 'Embedded SDK assembly differs from the approved profile.'
                return $row
            }
        } finally { $zip.Dispose() }
    }
    $row.status = 'Pass'
    $row.reason = 'Exact bytes verified without loading or executing the artifact.'
    return $row
}

function Invoke-RuntimeHostComponentTests {
    param([string] $RepositoryPath, [string] $ResultsDirectory, [string] $PackageConfigPath)
    $telemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
    $testTelemetry = $env:TESTINGPLATFORM_TELEMETRY_OPTOUT
    Push-Location $RepositoryPath
    try {
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        $env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
        $sdk = (& dotnet --version | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $sdk -cne '10.0.401') { throw 'Host components require exact SDK 10.0.401.' }
        $runtimes = @(& dotnet --list-runtimes)
        if ($LASTEXITCODE -ne 0 -or @($runtimes | Where-Object { $_ -cmatch '^Microsoft.NETCore.App 10\.0\.12 ' }).Count -ne 1) {
            throw 'Host components require installed .NET runtime 10.0.12; no installation is authorized.'
        }
        $project = Join-Path $RepositoryPath 'experiments\r02-dotnet-management-proof\HostTests.csproj'
        & dotnet build $project --configuration Release --no-restore 2>&1 | Tee-Object -Variable buildOutput | Out-Host
        if ($LASTEXITCODE -ne 0) {
            if (($buildOutput | Out-String) -notmatch 'NETSDK1004|NETSDK1064|NU1100') {
                throw 'Host component build failed for a reason other than missing restored assets/packages.'
            }
            if (!$PackageConfigPath) { throw 'Missing dependencies require an explicitly supplied, approved machine-local -PackageConfigPath; no automatic feed fallback.' }
            $config = [IO.Path]::GetFullPath($PackageConfigPath)
            if ($config.StartsWith($RepositoryPath + '\', [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Package routing configuration must stay outside the repository.'
            }
            Assert-NoLinks $config -AncestorsOnly
            Write-Host 'Missing restored assets/packages: restoring locked host-only dependencies with the supplied machine-local configuration.'
            & dotnet restore $project --locked-mode --configfile $config | Out-Host
            if ($LASTEXITCODE -ne 0) { throw 'Approved-feed locked restore failed; no alternate-feed fallback.' }
            & dotnet build $project --configuration Release --no-restore | Out-Host
            if ($LASTEXITCODE -ne 0) { throw 'Host component build failed after locked restore.' }
        }
        New-ProofDirectory $ResultsDirectory
        & dotnet test --project $project --configuration Release --no-build --results-directory $ResultsDirectory --report-trx | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Host component tests failed; inspect this invocation TRX.' }
        $reports = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File)
        if ($reports.Count -ne 1) { throw 'Expected one TRX from this new host-component results directory.' }
        $trx = $reports[0].FullName
        [xml] $results = Get-Content -LiteralPath $trx -Raw
        $counts = $results.TestRun.ResultSummary.Counters
        if ([int]$counts.total -ne 22 -or [int]$counts.passed -ne 22 -or
            [int]$counts.failed -ne 0 -or [int]$counts.notExecuted -ne 0) {
            throw 'All 22 host components must pass; zero/filtered/skipped tests are not acceptance.'
        }
        [ordered]@{
            status = 'Pass'; total = 22; passed = 22; failed = 0; skipped = 0
            sdk = $sdk; runtime = '10.0.12'
            trxSha256 = (Get-FileHash -LiteralPath $trx).Hash.ToLowerInvariant()
            scope = 'MG1 host components, including synthetic loopback HTTP only; no Copilot SDK/native or account/provider trials.'
        }
    } finally {
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = $telemetry
        $env:TESTINGPLATFORM_TELEMETRY_OPTOUT = $testTelemetry
        Pop-Location
    }
}

function Get-RuntimeValidationStages {
    # Preparation and self-asserted approvals cannot admit native paths or paid usage.
    @(
        [ordered]@{
            id = 'offline-preparation'; status = 'Ready'
            automation = 'Historical identity checks, exact supplied byte checks, deterministic contracts, redacted receipt generation.'
            missing = @()
        },
        [ordered]@{
            id = 'synthetic-runtime-regression'; status = 'Blocked'
            automation = 'After separate launch approval: private staged copies, 45 source RT1; 45 released RT1 + 22 host + 16 MG1; 20 RT2 + 2 locales. Preserve RT2 Blocked even if every test passes.'
            missing = @('Explicit native-launch approval', 'Verified inputs and locked assets in private staging',
                'Qualified exact toolchain and candidate reproduction; use the isolated synthetic coordinator, not historical runners directly')
        },
        [ordered]@{
            id = 'dedicated-host-rt2'; status = 'Blocked'
            automation = 'Reviewed collector must start before launch, run <=60 seconds/trial and stop after owned cleanup. Repeat source and applicable changed released-profile lifecycle paths independently.'
            missing = @('Named dedicated host/operator and scoped collection approval',
                'Implemented/reviewed privileged collector, required privilege and metadata-only redactor',
                'Attributable network/file/native-diagnostic/descendant coverage with loss and positive controls',
                'Actual native egress/write mediation or isolation; metadata detection is insufficient',
                'R04 admitted host correlation/audit wiring for integration, not fixture IDs')
        },
        [ordered]@{
            id = 'pv1-execution'; status = 'Blocked'
            automation = 'After applicable RT2 and account approval: supported-auth expiry/denial/errors, quota/concurrency, destination/context isolation and truthful cancellation.'
            missing = @('Qualified applicable RT2 profile', 'Selected provider/model/region/auth/terms/tier',
                'Explicit supported-account and potentially paid-usage approval',
                'Enforceable billed-spend ceiling and trusted live usage admission/transport hooks')
        },
        [ordered]@{
            id = 'pv1-management'; status = 'Blocked'
            automation = 'Additionally prove real two-execution-plus-manager capacity/budget; carry MG1 no-retry, byte/deadline/rolling-window controls and Unknown quarantine.'
            missing = @('Execution PV1 eligibility and applicable released RT2',
                'D-004 account envelope and actual independent two-execution-plus-manager capacity',
                'Integrated R13/R04 identity, admission, leases, fairness and recovery evidence')
        },
        [ordered]@{
            id = 'r08'; status = 'Blocked'
            automation = 'Admission remains disabled until RT1, applicable RT2, execution PV1 and integrated host/streaming acceptance pass.'
            missing = @('All-path RT2 admission', 'Execution PV1', 'R08 integrated acceptance')
        },
        [ordered]@{
            id = 'r13-model-assisted'; status = 'Blocked'
            automation = 'MG1 historical Pass is necessary, not sufficient for production model assistance.'
            missing = @('R08', 'Management PV1 / D-004', 'D-010 integrated resource coordination')
        },
        [ordered]@{
            id = 'r13-deterministic-core'; status = 'Independent'
            automation = 'No hosted inference dependency; readiness is owned by R05/R06/R10/R12 and applicable local concurrency budgets.'
            missing = @('Own production prerequisites not evaluated by this preparation')
        }
    )
}

function Export-SyntheticReleasedEvidence {
    param([string] $FixtureRoot, [string] $OutputDirectory, [DateTimeOffset] $Started, $Profile)
    $runtimeReport = Read-RuntimeReceipt (Join-Path $FixtureRoot 'evidence\runtime-results.json')
    $rt1Report = Read-RuntimeReceipt (Join-Path $FixtureRoot 'evidence\rt1-released-regression.json')
    Assert-RuntimeRegressionRows $rt1Report 'fresh released RT1'
    if ([DateTimeOffset]::Parse($runtimeReport.recordedAtUtc, [Globalization.CultureInfo]::InvariantCulture) -lt $Started -or
        [DateTimeOffset]::Parse($rt1Report.recordedAtUtc, [Globalization.CultureInfo]::InvariantCulture) -lt $Started -or
        $runtimeReport.sdkPackageSha256 -cne $Profile.packageSha256 -or
        $runtimeReport.sdkAssemblySha256 -cne $Profile.assemblySha256 -or
        $runtimeReport.nativeLauncherSha256 -cne $Profile.nativeLauncherSha256 -or
        $runtimeReport.nativePayloadSha256 -cne $Profile.nativePayloadSha256 -or
        @($runtimeReport.rows).Count -ne 16 -or
        @($runtimeReport.rows | Where-Object { $_.status -cne 'Pass' -or $_.counts.ownedCleanupCompleted -ne 1 }).Count -ne 0 -or
        @($rt1Report.rows | Where-Object { $_.counters.ownedCleanupCompleted -ne 1 }).Count -ne 0) {
        throw 'Released receipts must be fresh, complete, cleaned and match separately approved SDK/native bytes.'
    }
    $evidence = Join-Path $OutputDirectory 'released-evidence'
    New-ProofDirectory $evidence
    $records = @(
        foreach ($name in @('input-verification.json', 'rt1-released-regression.json', 'runtime-results.json', 'disposition.json')) {
            $source = Join-Path $FixtureRoot "evidence\$name"
            Assert-NoLinks $source -AncestorsOnly
            $destination = Join-Path $evidence $name
            Copy-Item -LiteralPath $source -Destination $destination
            $hash = (Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant()
            if ((Get-FileHash -LiteralPath $destination).Hash.ToLowerInvariant() -cne $hash) { throw 'Released evidence copy changed bytes.' }
            [ordered]@{ file = $name; sha256 = $hash }
        }
    )
    $tests = Join-Path $FixtureRoot 'TestResults'
    Assert-NoLinks $tests
    $destination = Join-Path $OutputDirectory 'released-tests'
    if (Test-Path -LiteralPath $destination) { throw 'Refusing to overwrite existing released test receipts.' }
    Copy-Item -LiteralPath $tests -Destination $destination -Recurse
    return $records
}

function Assert-ReleasedRuntimeLifecycleReceipts {
    param([object[]] $Trials, $PositiveControl, [DateTimeOffset] $Started, $Profile)
    if ($Trials.Count -ne 13 -or @($Trials.id | Sort-Object -Unique).Count -ne 13 -or
        @($Trials.fixtureAssemblySha256 | Sort-Object -Unique).Count -ne 1 -or
        @($Trials | Where-Object {
            [string]::IsNullOrWhiteSpace($_.id) -or $_.trial -cne 'PASS' -or
            [DateTimeOffset]::Parse($_.startedUtc, [Globalization.CultureInfo]::InvariantCulture) -lt $Started -or
            $_.cleanupCompleted -ne 1 -or $_.liveObservedOwnedProcessesAfterShutdown -ne 0 -or
            $_.deniedMarkersAtProvider -ne 0 -or $_.credentialInModelBody -ne 0 -or
            $_.observation.fileOverflow -ne 0 -or @($_.observation.queryGaps).Count -ne 0 -or
            @($_.observation.processes).Count -lt 1 -or $_.fixtureAssemblySha256 -cnotmatch '^[a-f0-9]{64}$' -or
            $_.sdkAssemblySha256 -cne $Profile.assemblySha256 -or
            $_.nativeLauncherSha256 -cne $Profile.nativeLauncherSha256 -or
            $_.nativePayloadSha256 -cne $Profile.nativePayloadSha256 -or
            $_.profile -cne $script:ReleasedRuntimeLifecycleProfile -or !$_.rt2.StartsWith('BLOCKED:', [StringComparison]::Ordinal)
        }).Count -ne 0) {
        throw 'Released RT2 receipts are incomplete, duplicate, stale, mislabeled, unclean, lossy or outside approved byte pins.'
    }
    if ($PositiveControl.status -cne 'PASS' -or $PositiveControl.syntheticOnly -ne $true -or
        [DateTimeOffset]::Parse($PositiveControl.startedUtc, [Globalization.CultureInfo]::InvariantCulture) -lt $Started -or
        $PositiveControl.observation.fileOverflow -ne 0 -or @($PositiveControl.observation.queryGaps).Count -ne 0 -or
        @($PositiveControl.markerFileObservedBeforeDeletion | Where-Object { $_.deniedMarker -eq $true -and $_.credentialMarker -eq $true }).Count -lt 1 -or
        @($PositiveControl.observation.socketSnapshots | Where-Object state -EQ 5).Count -lt 1 -or
        $PositiveControl.observation.managedDiagnosticEventCounts.Count -lt 1) {
        throw 'Released RT2 requires fresh loss-free real file/socket/managed-diagnostic positive controls.'
    }
}

function Get-RuntimeOperatorRequests {
    [ordered]@{
        approvalGranted = $false
        rt2 = [ordered]@{
            order = 1; dedicatedHost = $null; operator = $null; collectorReviewed = $false
            maximumTrialSeconds = 60
            scope = @('Exact owned PID plus UTC creation time, descendants and image identity',
                'Startup/auth/session/history/error/retry/cancellation/disposal/quiescence',
                'Native network: DNS, TCP/UDP IPv4/IPv6, non-IP transports and destinations',
                'Files: transient/deleted/outside-scratch, registry, ADS and crash-dump paths',
                'Native diagnostics: stderr/ETW and helper/detached descendant paths',
                'Independent applicable released-profile and changed dependency/auth paths')
            collectorControls = @('Kernel capture is machine-wide before filtering: explicitly approve this exposure on the dedicated host',
                'Retain only reviewed attributed metadata; no raw payload, credential, unrelated-event or account-name receipts',
                'Bound buffers/duration/storage; prove redaction, start/stop loss counts and file/network/process/diagnostic positive controls',
                'Unattributed events, unsupported paths, event loss, unexpected helpers or lost cleanup fail closed',
                'Named owned trace-session stop in finally plus operator recovery if interrupted',
                'No install, elevation or policy/network change without separate scoped approval')
            prevention = 'A reviewed native mediation/isolation mechanism and controlled negative tests are missing. ETW, Job Objects, empty tools and model-only gates are not substitutes. Otherwise return D-001 for a decision.'
        }
        pv1 = [ordered]@{
            order = 2; provider = $null; model = $null; region = $null; supportedAuthentication = $null
            termsAndSdkEntitlement = $null; organizationPolicy = $null; tier = $null
            evidenceCheckedUtc = $null; concurrency = $null; quotasAndRateLimits = $null
            budget = [ordered]@{
                currency = $null; maximumTrialSpend = $null; billingPeriod = $null
                enforceableProviderCap = $null; capEnforcementAndOvershootEvidence = $null
                reservedCostForInFlightRequests = $null; inputOutputReasoningPrices = $null
                expiryAndResetPolicy = $null
                rule = 'Alerts are not a hard cap. Byte bounds do not cap reasoning charges. Reserve worst-case in-flight/retry costs before dispatch; no defensible ceiling means no call.'
            }
            managementLimits = [ordered]@{
                serializedInputUtf8Bytes = 32768; completeTypedOutputUtf8Bytes = 4096
                dispatchDeadlineMilliseconds = 15000; inFlight = 1; attemptsPerRollingHour = 30
                forwardedAttemptsPerRequest = 1; freshConversationPerRequest = $true
                unknownTerminationQuarantined = $true
            }
            operatorOnly = @('Select intended supported account without disclosing its identity or secrets in receipts',
                'Evaluate terms/organization entitlement and hard cost ceiling',
                'Complete supported secure sign-in outside chat; credentials remain host-only opaque references',
                'Approve account and bounded potentially paid usage only after applicable RT2 admission')
            trials = @('Supported auth, expiry/revocation/denial and 401/429/500 errors',
                'Redirect/proxy/default-destination rejection and model/header/native diagnostic isolation',
                'No automatically forwarded retry; cancellation and late output/effects remain truthful',
                'Measured quotas, rate limits and execution concurrency',
                'Management additionally: two real executions plus independent manager, budget and destination/context/tool separation')
        }
        stopAndCleanup = @('Stop on lost attribution/events, unexpected destinations/content, cap/terms/auth uncertainty or cleanup failure',
            'Close new dispatch/egress before cancellation; suppress late presentation',
            'SDK acknowledgement/socket closure is not physical termination, rollback, billing stop or slot-release evidence',
            'Stop only the named owned trace session and exact verified owned process identities; never name-based process killing',
            'Dispose owned listeners/stores/handles; clean exact owned scratch and raw traces under approved retention',
            'Retain redacted failure/Unknown receipts and quarantine unresolved work; no automatic replay',
            'Operator revokes/signs out only the approved supported-account grant if that secure flow requires it')
        packageRouting = 'Later approved acquisition/restore must use Networking-AAA feed overrides outside source control; existing historical preparation recipes require routing review before use.'
    }
}
