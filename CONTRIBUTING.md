# Contributing to Kora

Thank you for helping improve Kora. Issues, design discussion, documentation,
tests, and focused pull requests are welcome.

## Contribution terms

Kora is source-available under the
[PolyForm Shield License 1.0.0](LICENSE), not an OSI-approved open-source
license. By submitting a contribution, you confirm that:

- you have the right to submit it;
- your contribution is provided under the repository's PolyForm Shield 1.0.0
  terms; and
- it does not knowingly introduce code, assets, models, or other dependencies
  whose terms conflict with Kora's license or intended distribution.

Public forks may be used to develop and propose contributions, subject to the
license. Do not market or provide a fork as a practical substitute for Kora.

## Extending Kora

Start with [product admission](Design/MVP_Scope.md#feature-admission): identify the approved outcome, concrete user scenario and host-versus-extension rationale before choosing a code layout.
New material scope and retired-work revival require an explicit product decision; repairs and proportionate usability within approved outcomes do not. Tests and implementation readiness cannot substitute for this decision.

Read [Extending Kora](EXTENDING-KORA.md) for the tool/skill/prompt/instruction/agent
decision guide, layer boundaries and delivered versus gated functionality.
Use the [Tools guide](src/Kora.Tools/README.md) for action classes and gateway/DI
wiring, and the [Definitions guide](src/Kora.Definitions/README.md) for explicit
catalogue registration, immutable resources and package validation.
These are contributor navigation guides; the linked design documents remain
the authoritative policy sources.

## Run a Debug build

On Windows, install Git, PowerShell 7 and the SDK selected by
[global.json](global.json), then run:

```powershell
.\run.ps1
```

The [launcher](run.ps1) fast-forwards the current branch from its configured
upstream, restores dependencies and tools, builds the desktop app in Debug and
runs it. It stops on failure and never stashes changes, rebases or creates a
merge commit. Use a branch with an upstream and resolve any conflicting local
changes before retrying.

To keep your current source without pulling:

```powershell
dotnet restore .\src\Kora\Kora.csproj --locked-mode
dotnet tool restore
dotnet build .\src\Kora\Kora.csproj --configuration Debug --no-restore
dotnet run --project .\src\Kora\Kora.csproj --configuration Debug --no-build --no-restore
```

Run `.\eng\Test-Run.ps1` to check launcher ordering, failure handling and working
directory restoration with fake commands; it never updates source or opens Kora.

## Development checks

PR merge orchestration can run entirely on GitHub, without a local agent.
See [GitHub-hosted merge admission](eng/merge-queue/README.md) for queue intent,
failure/retry behaviour, the disabled-by-default rollout and administrator setup.

Restore, build, and test with the pinned SDK and locked dependencies:

```powershell
dotnet restore .\Kora.slnx --locked-mode
dotnet tool restore
dotnet build .\Kora.slnx --configuration Release --no-restore
dotnet test --solution .\Kora.UnitTests.slnx --configuration Release --no-build
dotnet test --project .\tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj --configuration Release --no-build
```

Run the smallest checks covering the change. Tests use xUnit v3 and
AwesomeAssertions; logging fixtures use Neovolve.Logging.Xunit. Compiler and
analyzer warnings are errors. Portable CI requires **100% line and branch
coverage across Core, Application, Tools and Definitions**, retaining reports
even when the gate fails. Non-destructive Windows integration tests are not
physical audio, provider or installed acceptance; CI must never lock or restart
its runner.

Portable CI runs the four projects in [Kora.UnitTests.slnx](Kora.UnitTests.slnx) together, using [MTP's processor-count-based module concurrency](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-mtp#options). The [workflow](.github/workflows/ci.yml) isolates TRX and coverage output in per-module directories and aggregates every module's coverage before enforcing the existing thresholds.

Portable fixtures that require host-resolved absolute source paths must construct
host-native paths with `Path.GetFullPath`, not hard-coded Windows drive paths.
Keep relative-path rejection intact; fixture portability must not relax source authority.

Queued-conversation fakes must derive each terminal message/task pair from the
exact request-owned retained record before updating shared observation fields.
Concurrent expiry must preserve both originals' identity, body, generation,
sequence and UTC timestamp, with exact terminal revisions and retained readback;
fixture repairs do not relax production authority or receipt checks.
Await interleaving hooks outside the fixture lock, then run the original-source
terminal guard and write together under the lock using that request-local pair.

Run [Test-PortableTestScheduling.ps1](eng/Test-PortableTestScheduling.ps1) to check solution membership, main-build coverage, parallel scheduling, report isolation, and coverage-gate arguments without building or running test modules.

For a controlled comparison, manually dispatch [CI](.github/workflows/ci.yml) with `compare_portable_tests=true`. Its opt-in job builds the main solution once, then runs [Compare-PortableTestScheduling.ps1](eng/Compare-PortableTestScheduling.ps1) in sequential/parallel/parallel/sequential order with identical coverage and reporting options. The artifact retains all four runs and their mean wall times.

The separate `compare_native_evidence=true` input runs [17 representative headless cases](eng/Compare-NativeFixtureEvidence.ps1) in synchronous/buffered/buffered/synchronous order on one Windows runner. The [test-only selector](tests/Kora.Windows.IntegrationTests/NativeUxTestFixture.cs) uses the fixture's existing buffering option and rejects unknown profiles.

Normal CI selects buffered evidence only for those 17 cases in the native-UX shard; other shards and unconfigured local runs retain synchronous delivery ([workflow](.github/workflows/ci.yml), [profile defaults](tests/Kora.Windows.IntegrationTests/NativeFixtureEvidenceMode.cs)). To reproduce the enabled profile locally, set `KORA_TEST_NATIVE_EVIDENCE_MODE=buffered` before running the native-UX shard.

Stop All notification-failure fixtures inject once at the first accepted
cancellation notification and retain the observer through joined terminal
completion. Later Cancelling publications are not additional injected failures;
every accepted request still requires its physical join and terminal receipt,
while the operation truthfully retains its partial/unconfirmed outcome.

Speech-suggestion receipt-pin regressions explicitly join voice-consent
withdrawal and capture closure in both notification orders. Assert the exact
command presentation and canonical request/task receipt independently of the
later consent notification; the shared current response title is not a durable
command outcome. Withdrawal must still prevent review, confirmation, capture
restart and any future speech-limit write.

To reproduce CI's Windows test scheduling after a locked `win-x64` build, run
`.\eng\Run-WindowsIntegrationTests.ps1 -Partition <name>`. CI builds once,
packages the runnable modules and complete unfiltered discovery catalog, then
scales out to twelve independent standard Windows hosted jobs. Consumer jobs
download this run's exact-source artifact, verify its revision and file hashes,
and run MTP through `--root-directory` / `--test-modules`, without restore or
compilation. No larger host or increased per-process thread limit is requested.
`ui-definition-dialogs`
owns the complete customization and prompt-parameter window classes;
`ui-conversation-actions` owns conversation methods beginning Native, Owned or
Named; `ui-conversation-core` owns conversation methods beginning B, E or M;
`ui-conversations` owns all other conversation methods, including future
methods outside those families. `ui-definitions` owns remaining definition classes.
`ui-workflows` and `ui-presentation` isolate measured expensive platform classes;
`ui-platform` catches the remainder, including future classes.
All theory rows for each method stay together. `ui-editors` owns the complete
owned-definition editor class.
All twelve execution partitions preserve serial headless UI
execution within each process and the existing per-process thread limit.
Each partition retains its own TRX and console output under a fresh run directory;
MTP diagnostics retain started-test identities and terminal state, with diagnostic
tails appended to console logs for predecessor artifact compatibility.
nonzero exits or missing, duplicate, empty or incomplete passing reports fail
the run. `-Partition <name>` selects one independent execution process.
`-Shard <name>` retains the five-job bootstrap assignments while trusted main
still consumes the predecessor workflow;
`-ListShards` lists legacy logical ownership filters for retained-data comparison;
`-ListPartitions` lists actual process filters and `HostedShard` assignments
without running tests. `Shard` on each partition retains its legacy logical owner.
CI retains the five legacy Windows job names as lightweight fail-closed joins
of the shared build and twelve execution jobs. This preserves queue retirement
of predecessor runs without adding required job names to queue policy.
`windows-execution-results-<partition>` artifacts retain every TRX and console log.
The unchanged required **Windows integration tests** check validates all twelve
reports against the admitted-source catalog: every method and theory case must
pass exactly once. Failure, cancellation, missing or skipped jobs cannot pass.
Payload/bootstrap fixtures still run once in the build producer.
This scheduling isolates competing test processes without removing tests or
changing test/product or 30-minute Windows job timeouts. Run
`.\eng\Test-WindowsIntegrationScheduling.ps1` for deterministic selection,
partition and aggregate-gate checks.

The [scheduling regression](eng/Test-WindowsIntegrationScheduling.ps1) checks
both hosted ownership and exact process partitions, exercises the real runner
with a fake CLI, and rejects missing/malformed execution evidence. Its optional
`-RetainedResultsDirectory <path>` accepts a complete legacy five-report or
legacy seven-report, nine-report or current twelve-report dataset and checks every recorded case against the new
partition exactly once. Complete successful runs
[41223826625](https://github.com/roryprimrose/Kora/actions/runs/41223826625) and
[41229047847](https://github.com/roryprimrose/Kora/actions/runs/41229047847)
project at least a 25% shorter UI critical path with measured setup/post costs.
Those projections are not new-head execution evidence or a guarantee about
contention between processes sharing a hosted runner. The initial seven-process
repair still timed out in [run 41255145262](https://github.com/roryprimrose/Kora/actions/runs/41255145262):
only 842 of 857 conversation cases were recorded. The nine-process repair
retains every unfinished method and splits recorded conversation/dialog work
further; partial timings are not proof that the repaired hosted job completes.
[Run 41288388916](https://github.com/roryprimrose/Kora/actions/runs/41288388916)
then timed out both UI runners: extra processes on the same runner did not
provide enough execution capacity. The subsequent cross-runner assignment uses
the spare storage-job capacity, leaves native buffering isolated, and sums
serial UI costs per hosted runner in timing regressions instead of assuming
process-level parallel speedup. Every filter and recorded test case is retained.
The twelve-job pipeline replaces those shared-runner assignments after
integration; its larger matrix does not execute during its own candidate's
trusted-main workflow. Local contracts and archive round-trip checks do not
establish hosted timing. Aim for 12-15 minutes end-to-end, but measure actual
runner scheduling, artifact transfer, build and slowest-test time before claiming
that target. More jobs cannot fix a test that stalls.

To reproduce downloaded-artifact execution after packaging:

```powershell
.\eng\Publish-WindowsTestArtifact.ps1 -SourceRevision (git rev-parse HEAD) -OutputArchive '.net-test-artifacts\windows-build\test-modules.zip'
Expand-Archive -LiteralPath '.net-test-artifacts\windows-build\test-modules.zip' -DestinationPath '<fresh-test-module-directory>'
.\eng\Run-WindowsIntegrationTests.ps1 -Partition 'ui-platform' -TestModuleDirectory '<fresh-test-module-directory>' -ExpectedSourceRevision (git rev-parse HEAD)
.\eng\Test-WindowsIntegrationScheduling.ps1 -RetainedResultsDirectory '<complete-twelve-report-directory>' -ExpectedCatalogPath '.net-test-artifacts\windows-build\kora-test-catalog.json' -ExpectedSourceRevision (git rev-parse HEAD)
```

Keep extracted modules beneath the source checkout at the test binary layout
so fixture source/resource discovery retains its repository context.
Approval-flow fixtures use a shared injected clock and explicitly controlled
timers rather than allowing runner load to consume an unrelated review budget.
The lifecycle controller's one-minute cancellation budget uses its injected
clock; shutdown/restart regressions exercise cancellation at 60 seconds without
a Windows effect, separately from the 30-second physical-effect countdown.
The native fixture still defaults to system time when no test clock is supplied.
Cancellation and deferred-cleanup regressions wait for the actual token or
callback signal before releasing their fake work. This preserves exact denial,
resource-release and no-effect assertions without sleeps or widened deadlines.
Native approval fixtures await exact-review publication and restored editability
before selecting an answer; review text can appear before the operation is idle.
Adaptive interaction fixtures join the exposed session-navigation operation
before asserting unchanged selected-row identity and independent composer text.

Portable tests and exact-source payload production run independently after
version resolution. The installer consumes the verified payload immediately,
without waiting for unrelated portable tests. The existing required
**Portable build, tests, coverage, and package** check joins both producers
fail-closed; release still requires this check, all Windows jobs and the
installer. No test category has moved to main-only or nightly validation.

Cancellation-ignoring playback fixtures wait for the fake speech stop to complete
before releasing playback. An incomplete command task alone does not establish
that cancellation was accepted or that the owned resource has joined.
The speech-interruption regression covers both immediate and gated stop completion,
then separately releases and joins the original playback; neither phase may
publish a late successful read.

The Customizations editor publishes its foreground voice target after the
window's `IsActive` property becomes true; it cannot rely on `Activated` alone,
which Avalonia raises before updating that property. The editor push-to-talk
integration cases await the actual active-state notification and cover
consent/read-only denial and capture release without retargeting.

Metadata-search publication regressions revoke admission at the completed service
read activity's disposal, after its final admission check and before native
publication. Keep these cases in the nonparallel `Host tracing` collection; do
not trigger revocation by counting revision-property reads from UI notifications.

The native MCP review/exchange fixture uses its existing frozen test timers:
it verifies exact native approval, real fake-broker results and physical joins,
not elapsed-time policy. Deadline regressions retain their explicit clock/timer
boundaries; production model and transport deadlines are unchanged.
Native stop-and-delete assertions await the exact cancellation-accepted event.
Paging gestures await the existing Next readiness notification when passive
work refresh is outstanding; draining dispatcher jobs does not join that I/O.
The portable speech-quarantine fixture observes the actual output-stop entry
before submitting a new request and releases its held stop in `finally`.
Native cancellation and clarification fixtures separately await exact-task
terminal UI publication after the real operation/resource join.

The [runner's UI filters](eng/Run-WindowsIntegrationTests.ps1) retain the existing shard names: `ui-definitions` also runs conversation and bounded accessibility tests; `native-ux` also runs dispatcher responsiveness, reader scale, and speech caption tests. `ui-platform` runs Evidence Find and the remainder, with exclusions derived from the same prefix lists to avoid gaps or overlaps.

Rebalance classes or explicit method families using per-shard TRX durations, not test counts; keep each method's theory rows together. Keep the [headless UI collection](tests/Kora.Windows.IntegrationTests/HeadlessUiTestGroup.cs) serial within each process; isolated processes must not relax its process-wide dispatcher protection. Update the [scheduling contracts](eng/Test-WindowsIntegrationScheduling.ps1) when changing assignments.
Validate complete retained five-, seven- or nine-report TRX evidence with `.\eng\Test-WindowsIntegrationScheduling.ps1 -RetainedResultsDirectory <results-root>`; this checks every recorded case against the current partition, not revised-source hosted timing.

Documentation-only changes need local link/heading, exact example and availability
checks. Changes to the embedded user guide also run its focused tests. They do
not require device/provider trials. Preserve existing anchors or update their
consumers, including links embedded in the guide and historical experiments.

### Native fixture phase evidence

Headless dispatch and the shared native fixture retain a per-test
`native-fixture-phases.json` attachment through xUnit's existing attachment API.
The adapter writes this string attachment as `native-fixture-phases.json.txt`
under the original test's unique-ID directory and links its retained copy in TRX.
It uses MTP's `TimingProperty`, `StepTimingInfo` and `TimingInfo` field shapes,
with the original xUnit execution `TestUniqueId` (including distinct theory
rows), not a reconstructed name or source attribute. The runner's ordinary
adapter-generated per-case timing remains unchanged; its empty `StepTimings`
is not the fixture attachment.
The attachment's `GlobalTiming` covers its observation window, not the entire
test's execution duration.

The stable phase allowlist is `native-ui`, `owned-storage`, `setup`, `context`,
`join`, `evidence-flush` and `disposal`. `native-ui` measures the dispatched
callback, not its queue wait; storage measures fixture construction, context
measures task/interaction-store initialization, and setup measures the complete
fixture initialization. Join and evidence flush measure the existing shutdown
owners inside disposal. These intervals can nest and must not be summed as
exclusive CPU time. Repeated phases have distinct ordinal IDs.

Each entered phase publishes `running`; only observed terminal phases enter
`Timing.StepTimings`, with a matching `completed`, `cancelled` or `failed`
`PhaseStates` entry. Elapsed durations and timestamp offsets use a monotonic
clock anchored once to UTC, independent of product clocks and deadline modes.
No exception text, display-name arguments, filenames, content, scope or audit
payload is collected. Report-delivery failure is rethrown after the owning
operation's cleanup boundary, failing the test rather than skipping physical
joins or pretending evidence was retained. During a dispatched callback,
fixture verification defers to that callback's exception boundary so disposal
cannot replace an original failure. Concurrent operation and report-delivery
failures surface together as an `AggregateException`, preserving both original
exceptions without adding their payloads to the attachment. Ambient capture is
valid only while its owning callback is active; inherited late callbacks cannot
reuse an ended ambient operation.

Each of the twelve `windows-execution-results-<partition>` artifacts retains
these test attachments alongside TRX, console output and MTP diagnostics under
its fresh execution directory. Phase attachments and started-test diagnostics
do not substitute for the complete admitted-source catalog and passing reports.
A killed host may never publish a terminal test report
or its attachments; absent evidence is not a completed/cancelled phase or a
recoverable historical trace. This instrumentation does not establish the
cause or repair of a hosted job deadline breach, runtime performance
qualification, or eligibility for a different evidence profile. Run the focused
`NativeUx.NativeFixturePhaseTimingTests` with the existing integration test
runner to verify the exact shape, original identity and real headless output.

### Reliable fixture patterns

- Coordinate races with explicit completion signals, not sleeps, property-read
  counts or an incomplete command task. Await cancellation acceptance, owned
  resource completion and the exact UI publication separately. Drain dispatcher
  work only after the operation it presents has joined.
- An immediate dispatcher does not serialize UI-bound collection access across
  worker continuations. Gate persistence while enqueuing native inputs, then join
  the owning update before reading saved values or audit snapshots. Independently
  completing operations share current response state; capture each complete
  presentation and verify its exact receipt rather than treating the last title
  as a durable command outcome.
- Concurrent terminal-storage fakes must build message/task receipts from each
  original request's retained snapshot, not mutable fixture-wide selection.
  Force interleaving deterministically and verify both exact retained readbacks.
- Inject one-shot observer faults atomically: cancellation acceptance and
  completion can publish the same Cancelling state concurrently. Retain the
  observer through physical join and verify the exact terminal receipt.
- Preserve production deadlines; use existing fake clocks for non-deadline
  scenarios. Dispose asynchronous fixture owners and release held operations in
  `finally`. See the [microphone ownership regressions](tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.MicrophoneRefreshOwnership.cs).
- Concurrent conversation expiry fixtures derive the message/task terminal pair
  from the exact request-owned snapshot, not mutable last-observed fixture slots.
  A concurrent dictionary does not make shared selection/commit atomic; force
  interleaving deterministically and preserve each original's exact receipt.
  The [manual-release regressions](tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.ManualDispatch.cs)
  await the shared completion hook for nested completions and held expired
  workers, verifying both exact retained readbacks and preserving original
  authority, timestamps and recovery holds without model execution or answer
  publication.
- Put restricted-storage fixtures under the discovered repository root to avoid
  native path-length limits. Keep unique names, non-reparse checks and owner-only
  ACLs. Explicitly protect every enumerated root, including editor `User`,
  `prompts` and managed `Mcp`; see the [original-edit fixture](tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/WindowsCustomizationStoreTests.InstructionOriginal.cs)
  and [MCP missing-root fixture](tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/WindowsCustomizationStoreTests.Mcp.cs).
  Default directory ownership may differ on elevated runners; a changed-binding
  regression must establish the admitted owner before testing that boundary.
- Reuse migration staging helpers; remove only empty later-version tables,
  never current authority records. Resolve a fresh admitted invocation for each
  grant use. Metadata-only reads must not create/edit files or publish journals.

The [scoped testing guidance](.github/instructions/testing.instructions.md)
owns behavioral regression requirements. Keep case-specific synchronization
details beside the relevant fixture rather than appending delivery notes here.

### Documentation ownership

Use the [design topic index](Design/README.md#topic-owners) to find the canonical
contract. The roadmap owns live delivery/dependencies, Acceptance Criteria owns
qualification, Deferred Validation owns remaining environment trials, Commands
owns exact syntax, and Settings owns current preference controls.
Keep introductions short; link policy rather than paraphrasing it in every
guide. User workflows should not reproduce architecture, test receipts or
planned model schemas. Keep short local safety/unavailability warnings where
their omission could mislead.

Retain dated source/test/trial receipts in evidence or the
[archive](Design/Archive/README.md), not in live status pages. Do not overwrite
failed/blocked observations, transfer old approvals or relabel old bytes as
current qualification.

## Logging conventions

Always use source-generated `[LoggerMessage]` partial methods for `ILogger<T>`
diagnostic messages instead of `Log*` extension methods or `LoggerMessage.Define`
delegates. Keep the declarations in `{ClassName}.Logging.cs` beside the class's
other source files, with the same namespace and a matching partial class.
Logging-only helper classes follow the same filename convention; reuse the
existing layer helpers for shared events.

Use structured templates and typed properties, not interpolated messages.
Preserve event IDs, levels, templates, property names, exception parameters, and
callers when reorganizing definitions. Security audit events must continue
through the trusted typed audit path, not ordinary diagnostic messages.

## Test and dependency policies

Tests that install process-wide activity listeners belong to the nonparallel
`Host tracing` collection. Its collection definition prevents unrelated host
requests in other test collections from entering a test's evidence sink or
changing its sampling assumptions. Other collections remain parallel.
Project-specific action/definition layout and registration belong in the
[extension guides](EXTENDING-KORA.md), not another policy copy here.

Run the dependency-license gate before adding or upgrading a package:

```powershell
.\eng\Test-DependencyLicenses.ps1
```

The gate covers projects in `Kora.slnx`, not isolated experiments. Experimental
dependencies require separate recorded review and cannot enter production or
publish archives without the admission steps in the
[dependency license policy](DEPENDENCY-LICENSES.md#experimental-dependencies).

For changes to source delivery, also run the
[managed-source ownership/failure and stage-inspection tests](Design/Distribution_And_Updates.md#verification-snapshot-2026-10-06)
with fresh dedicated scratch directories outside all repositories. These
script checks do not install or launch Kora; a real exact-revision source
publish/static verification is a separate permitted build check, not
protected/installed acceptance.
Distribution tooling and static native/runtime/import/resource checks now live
under `eng`, including the [both-RID publish contracts](eng/Test-PublishContracts.ps1).
Use fresh inspection evidence outside the payload and fresh fixture outputs
outside all repositories. Source interface 1.1.0 does not adopt/relabel older
owner/tool-path receipts; historical experiment evidence is archived unchanged.
The synthetic source-bootstrap fixtures temporarily isolate `GITHUB_ACTIONS`
and restore it afterward, so their scratch revisions do not inherit the
calling workflow's source identity. Production revision checks remain enforced.

If an intentional dependency change alters the notice report, review the new
license and provenance first. Add an approved license identifier or a
version-specific metadata override only with evidence, then regenerate:

```powershell
.\eng\Test-DependencyLicenses.ps1 -UpdateNotice
```

Commit the resulting `THIRD-PARTY-NOTICES.md` change with the package and lock
file changes.

Package versions are centralized in `Directory.Packages.props`; project-level
overrides are rejected. After an intentional reviewed version change, refresh
locked dependencies with `dotnet restore .\Kora.slnx --force-evaluate`, then run
the license gate and review the lock/notice changes.
