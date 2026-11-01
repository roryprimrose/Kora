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
`.\eng\Run-WindowsIntegrationTests.ps1`. Its five concurrent, disjoint local shards
cover heavy storage, other storage, and three balanced UI/platform groups.
Each shard retains its own TRX and console output;
any nonzero shard exit fails the run. `-Shard <name>` selects exactly one existing
shard; `-ListShards` lists the selected names and filters without running tests.
CI isolates these same five shards on separate hosted Windows runners, retaining
per-shard TRX and console artifacts. The unchanged required **Windows integration
tests** check aggregates the matrix fail-closed: failure, cancellation, missing
or skipped shards cannot pass. Payload/bootstrap fixtures still run once.
This scheduling isolates competing test processes without removing tests or
changing test/product or 30-minute Windows job timeouts. Run
`.\eng\Test-WindowsIntegrationScheduling.ps1` for deterministic selection,
partition and aggregate-gate checks.

The [shard runner](eng/Run-WindowsIntegrationTests.ps1) keeps the complete settings section navigation, reset-menu and specialist navigation classes on synchronous `ui-definitions`, separate from the buffered native-UX profile.
The [scheduling regression](eng/Test-WindowsIntegrationScheduling.ps1) checks exact class boundaries and measured redistribution after the [platform job's deadline breach](https://github.com/roryprimrose/Kora/actions/runs/40815883491/job/123256871731); retained partial timings are not a full-shard pass or a hosted timing guarantee.

The [shard runner](eng/Run-WindowsIntegrationTests.ps1) places the whole owned-definition editor class on synchronous `ui-platform`, and session-routing and waiting-question navigation classes on synchronous `ui-definitions`; only the existing native-UX classes use buffered evidence.

[Run 40836599680](https://github.com/roryprimrose/Kora/actions/runs/40836599680) retained partial definitions results before cancellation, so the [scheduling regression](eng/Test-WindowsIntegrationScheduling.ps1) predicts recorded work only, not a new-head completion or timing guarantee.

The [candidate's cancelled definitions job](https://github.com/roryprimrose/Kora/actions/runs/40840133406/job/123334414374) retained incomplete results; [scheduling checks](eng/Test-WindowsIntegrationScheduling.ps1) preserve exact class partitions and the 30-minute cap while predicting recorded-work redistribution, not new-head completion or timing guarantees.

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

Rebalance whole classes using per-shard TRX durations, not test counts. Keep the [headless UI collection](tests/Kora.Windows.IntegrationTests/HeadlessUiTestGroup.cs) serial within each process; moving classes between isolated shard processes must not relax its process-wide dispatcher protection. Update the [scheduling contracts](eng/Test-WindowsIntegrationScheduling.ps1) when changing assignments.
Validate complete retained five-shard TRX evidence with `.\eng\Test-WindowsIntegrationScheduling.ps1 -RetainedResultsDirectory <results-root>`; this checks every recorded case against the current partition, not revised-source hosted timing.

Documentation-only changes need local link/heading, exact example and availability
checks. Changes to the embedded user guide also run its focused tests. They do
not require device/provider trials. Preserve existing anchors or update their
consumers, including links embedded in the guide and historical experiments.

### Reliable fixture patterns

- Coordinate races with explicit completion signals, not sleeps, property-read
  counts or an incomplete command task. Await cancellation acceptance, owned
  resource completion and the exact UI publication separately. Drain dispatcher
  work only after the operation it presents has joined.
- Concurrent terminal-storage fakes must build message/task receipts from each
  original request's retained snapshot, not mutable fixture-wide selection.
  Force interleaving deterministically and verify both exact retained readbacks.
- Preserve production deadlines; use existing fake clocks for non-deadline
  scenarios. Dispose asynchronous fixture owners and release held operations in
  `finally`. See the [microphone ownership regressions](tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.MicrophoneRefreshOwnership.cs).
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
- Pending conversation fixtures build terminal receipts from the exact
  request-keyed record, not shared latest-message/task fields. Expired queued
  workers can complete concurrently; interleaved completions must retain each
  original identity without model execution or answer publication.

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
