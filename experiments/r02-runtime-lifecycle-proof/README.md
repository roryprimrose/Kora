# R02-RT2 bounded runtime lifecycle observation

**RT2 BLOCKED, not runtime isolation acceptance.** This is a runnable Windows
x64 experiment, not production SDK composition. The final run passes 20/20
tests: 13 actual runtime lifecycle trials, six deterministic observer tests,
and one live file/socket/diagnostic positive-control test. Two PowerShell
receipt-locale contracts also pass. See [evidence](EVIDENCE.md) and
[disposition](evidence/disposition.json).

## Exact candidate, not an equivalent cache

The experiment consumes byte-for-byte copies of the original
[RT1 fixture](../r02-dotnet-control-proof/README.md), without editing its source
or historical evidence. The copied fixture owns its generated evidence and
scratch. Five original RT1 source files are linked into this test assembly;
the client, runtime configuration, public request gate and volatile session
filesystem are unchanged. RT2 only adds observers, synthetic trials and receipts.
The complete original 45-test RT1 suite is also built/run in that private copy;
[regression results](evidence/rt1-regression.json) retain 44 PASS capability rows
and the expected rejected hook-only FAIL.

- SDK release v1.0.16, source
  `f8ae645902b74b62cd47aac1fd9b29adaec3aff2`.
- Approved unmodified source-build version `1.0.16-rt1.source.f8ae645.1`.
- Native runtime 1.0.90/protocol 3, minimal HTTP/SSE plus child stdio.
- Installed .NET SDK 10.0.401/runtime 10.0.12, Windows x64 only.
- SDK package SHA-256
  `0b609b73c868099d64e7d5320b927d760d528756ab7c5db0b5692f5dccbf9b3f`;
  SDK assembly
  `afb0715225f794d1b663ef72da1336b44b665e50b330087e1356380a5eac10d6`.
- Launcher
  `7021cf1f25eb6b75e64c05e8f805747c62dd420dc8760659660291809e92603a`;
  native payload
  `41ebb48367f96c984babde61afb68f22a85ab8fd9c037fdccf1778881c4bba05`.

The final original preparation and independent clean-source reproduction
produce those exact package/assembly bytes. [Preparation](evidence/preparation.json)
records original source/evidence digests and the reproduction receipt. Earlier
acquisition/build/reproduction failures are retained in [evidence](EVIDENCE.md);
no mismatching artifact was used in a trial, no lock/pin was relaxed, and
released-NuGet byte parity is still Blocked. Do not infer reproducibility from
an existing local package or equivalent IL. Retest and obtain approval for
different SDK/runtime/profile bytes.

## Reproduce from repository root

Use installed, reviewed tools only (PowerShell 7.5+ for invariant JSON date
handling). Preparation downloads verified public archives and restores the
already reviewed closure into a unique RT2-owned cache; it installs no machine
dependency. Its short temporary source cache avoids imposing a global long-path
policy change. The staging location is recorded in the ignored
`.candidate\Stage.props`, not a fixed user path. Active runtime trials use the
original RT1 replacement environment and owned scratch.

```powershell
.\experiments\r02-runtime-lifecycle-proof\Prepare-Fixture.ps1
.\experiments\r02-runtime-lifecycle-proof\Test-ApprovedInputs.ps1
.\experiments\r02-runtime-lifecycle-proof\Test-ReceiptContracts.ps1

# The full original suite writes only the staged copy's evidence.
[xml] $stage = Get-Content .\experiments\r02-runtime-lifecycle-proof\.candidate\Stage.props -Raw
$project = Join-Path $stage.Project.PropertyGroup.Rt1Stage 'ControlProof.csproj'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
dotnet build $project --configuration Release --no-restore
dotnet test --project $project --configuration Release --no-build --results-directory .net-test-artifacts\rt2-latest\rt1 --report-trx

.\experiments\r02-runtime-lifecycle-proof\Run-Observations.ps1
```

The last command deliberately exits **2** after successful tests because the
RT2 gate is Blocked. Build/test/receipt failures throw instead. It requires all
20 tests and 13 current, successful, overflow-free, cleaned real-trial receipts;
stale/filtered results cannot qualify. It verifies original/staged inputs both
before build and after tests. For debugging use the normal `dotnet test --project`
runner; exit 0 there means tests passed, never RT2 acceptance.

Preparation defaults to rejecting independent reproduction failure.
`-AllowFailedIndependentReproduction` permits only further **blocked research**
on a primary build whose exact approved package/assembly bytes are still
verified; it cannot approve changed bytes or close RT2. Do not update the lock
to make a mismatching package restore.

## Observer capability and bounds

| Observer | Attributable coverage | Limits / enforcement |
|---|---|---|
| Toolhelp snapshot and process/module queries | Exact launcher path + fixture host parent, PID + UTC creation time, live parent chain; root/native payload and Windows helper image inventory/hashes; observed termination | 20-ms target, measured maximum gap in each receipt; short-lived/detached/pre-sample processes and images can be missed. Detection only. No process-name killing. |
| Windows IP Helper TCP/UDP v4/v6 tables | Fixture host and sampled live owned PIDs; endpoint/state inventory during the interval | Misses short-lived connections, UDP remote destinations, DNS queries, ICMP/non-IP transport and payloads. Host test-platform IPC is not runtime-provider traffic. Detection only. |
| ReadDirectoryChangesW via FileSystemWatcher | Live owned-scratch create/change/delete/rename notifications, overflow counter, startup-through-shutdown phases | No writer PID, contents, outside-root writes, ADS/registry/reparse targets or deleted contents. No prevention. |
| Before-cleanup recoverable-file inspection | Owned file size/hash and synthetic marker booleans; intentional discovery input separately classified | Not an all-disk or deleted-write oracle. No zero-persistence assertion for unobserved paths. |
| Managed System.Net.Http/System.Net.Sockets EventListener | Host-PID fixed event IDs/counts only; actual socket positive control and model requests | Never reads/retains payloads. Does not observe native stderr, native ETW, SDK-disabled logs or other diagnostic channels. |
| Original RT1 public callbacks | Complete serialized model requests, credential-header/body separation, retries/cancellation, volatile session I/O, fixed runtime event types | Prevention/mediation for these specific paths, not native network/files containment. |

Observers start before runtime launch and end after shutdown/observed
quiescence. Per-trial phase/UTC receipts identify startup, initialized/status
verification, session path completion, shutdown and observed quiescence.
Activities use `Kora.Experiments.Rt2` version 1.0.0 and stable
`runtime.lifecycle.observation`; their status is Error for incomplete RT2
observation. Trace IDs are causal experimental correlation, not durable
identity, audit authority or incoming-header authentication.

The original RT1 bounds apply: start/session 15 s, trial action 45 s, provider
arrival 10 s/completion 12 s, abort 5 s, shutdown 10 s then owned SDK force-stop.
The observer's runtime-arrival fence and positive controls are bounded to 5 s.
Only synthetic loopback listeners, a noncredential sentinel, harmless owned
reads/writes and the runtime's observed descendants are used. No ambient
credentials/default hosted routing, account inference, sign-in, application or
installer launch, global tracing/policy changes, elevation or installs occur.

## Maintained unavailable surfaces and handoffs

WebSockets, MCP, automatic agents/plugins/tools, memory, spill and export
remain disabled/unavailable. Empty catalogues and process separation are not
containment. Native initialization still launches PowerShell/console helpers
and creates/deletes scratch policy-test files. Their complete content and
all-system writes are not observed.

The user retained the fail-closed RT2 contract and deferred privileged tracing
to a separately scheduled dedicated host. [Handoffs](EVIDENCE.md#handoffs)
distinguish R08, MG1/PV1, R04 and W2. No production adapter, D-001/D-004/D-010,
Gate 0, worker, protected-deployment or encrypted-storage gate closes here.
The fixture, evidence and Design updates are reviewable source-control
deliverables even while RT2 acceptance is Blocked. Merging this research
does not enable a production capability or approve the deferred trials.
