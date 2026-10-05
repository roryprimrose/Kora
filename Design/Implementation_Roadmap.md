# Implementation Status and Delivery Roadmap

Status: source-backed implementation inventory and proposed delivery order;
R01 policy reconciliation approved; R02 Windows storage direction and
local-inference/runtime partial outcomes and distribution delivery plan recorded;
native/key/runtime admission, real inference qualification, production
implementation and acceptance proof remain open.
Bootstrap inventory reviewed on 2026-10-05 against repository revision `e4688c3`;
R02 storage evidence reviewed against `59d1eb9` and incorporated into the contracts below.
Local-inference follow-up reviewed against `1bb6dc3` and its recorded evidence.
Supplemented on 2026-10-05 with the R02 Windows containment snapshot at `a74bb3a`;
runtime feasibility evidence updated against `e0af3ea`;
none of these experiments is composed into the application.
Distribution-only addendum reviewed against R01 revision `7d5e6a3` and the
[R02 distribution experiment](../experiments/r02-distribution-proof/README.md);
this does not reclassify unrelated R02 branches or claim release acceptance.
Update this baseline and the evidence below when implementation changes.

Related: [MVP Scope](MVP_Scope.md), [Decision Register](Decision_Register.md), [Acceptance Criteria](Acceptance_Criteria.md), [Canonical Tool Catalogue](Internal_Model_Tools.md), [Technical Capability Reference](Tool_And_Skill_Reference.md).

## How to Read Status

The design describes the intended product; the repository currently implements a runnable Windows bootstrap.
None of A0, A1, A2, A3, A4, B, or C is established as an accepted, complete delivery by this review.
In particular, verified Ollama setup is not the local-first clipboard slice, opening several windows is not concurrent session execution, and installing PowerShell is not a script runner.

- **Delivered bootstrap:** the stated narrow behavior is wired into the application, with source and checked-in test evidence where available.
- **Partial:** an existing component can be reused, but required behavior or enforcement is missing.
- **Outstanding:** the required capability has no complete implementation.
- **Proof outstanding:** implementation or a candidate exists, but the required real-provider, hardware, containment, deployment, or acceptance evidence has not been established.
- **Optional/deferred:** not a prerequisite for baseline voice or the initial release; a separate capability gate applies.

The original inventory review inspected source and test definitions; it did not
perform new microphone, audible playback, real model, OS power or installer
trials. The supplemental R02 evidence below performs narrowly scoped real
Windows containment trials, not installed-application or full release acceptance.
The separate R02 Windows storage investigation performed the synthetic encryption, DPAPI and process-kill trials recorded below; these do not establish production persistence.
The separate R02 local-inference follow-up adds actual
missing-endpoint/unavailable-path observations and public
identity/licence/download metadata, not successful real model generations.
It performed no reference-floor or independently network-blocked model trials.
The R02 runtime branch subsequently exercised an actual pinned SDK/runtime
against synthetic loopback providers, not a real hosted model/account.
Its partial results and explicit failures/blockers are recorded below.
Test-project names, mocks, helper truth tables, configured coverage thresholds, and publish jobs are not substitutes for those trials.
An implemented feature may therefore still have outstanding release proof.
The [acceptance criteria](Acceptance_Criteria.md), not this inventory, determine release readiness.

## Delivered and Partial Implementation

The identifiers in this table are inventory references, not new capability or model-tool IDs.

| Ref | Status | What exists and its boundary | Source and test evidence |
|---|---|---|---|
| I01 | Delivered bootstrap; deployment proof outstanding | .NET 10/Avalonia Windows composition with portable Core/Application projects and first-party Windows services. No Copilot SDK adapter or MCP runtime is composed. | [Composition](../src/Kora/Program.cs), [desktop project](../src/Kora/Kora.csproj), [dependency versions](../Directory.Packages.props) |
| I02 | Delivered bootstrap | All 20 built-in actions, normalized whole-phrase routing, optional configured-name prefix, and C# dispatch. Exact routes take precedence over inference. This is not registration of the proposed tools or execution of skill packages. | [Actions](../src/Kora.Core/Commands/BuiltInAction.cs), [catalogue](../src/Kora.Core/Commands/BuiltInCommandCatalog.cs), [router](../src/Kora.Core/Commands/BuiltInCommandRouter.cs), [router tests](../tests/Kora.Core.UnitTests/Commands/BuiltInCommandRouterTests.cs), [host dispatch/tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.cs) |
| I03 | Delivered bootstrap; A4 partial | Presence, Settings, compact response, embedded documentation and grant windows; themes, assistant naming, native controls, response pin/topmost/timeout and device-local placement/preferences. The tray exposes Show, Settings, Documentation and Exit with single/double-click behavior. It has no direct microphone-selection/listening-recovery menu, Sessions workspace or full conversation history. | [Desktop composition](../src/Kora/App.axaml.cs), [tray](../src/Kora/SystemTrayController.cs), [response surface](../src/Kora/ResponseWindow.axaml), [application view model](../src/Kora.Application/ViewModels/MainViewModel.cs), [embedded-guide tests](../tests/Kora.Application.UnitTests/Documentation/EmbeddedUserDocumentationProviderTests.cs) |
| I04 | Partial | Microphone/output enumeration, System-default or explicit device preferences, readiness, Enable/Disable listening, permission guidance and safe-start checks. Startup can automatically listen if current checks pass; manual disablement lasts for the run. No explicit PTT flow, cross-build audio owner or complete event-driven lock/disconnect/suspend/permission-loss shutdown is established. | [Application orchestration](../src/Kora.Application/ViewModels/MainViewModel.cs), [device preferences](../src/Kora.Application/Configuration/LocalAudioDevicePreferences.cs), [application tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.cs), [microphone access](../src/Kora.Windows/Audio/WindowsMicrophoneAccessService.cs) |
| I05 | Partial voice proof | Windows phrase grammar and assistant-name-prefixed dictation produce local transcripts. Microphone buffers enter the recognizer while listening, before a phrase is matched. This is not the designed wake-only ambient pipeline; bounded pre-roll, explicit endpointing/utterance limits and production wake quality are outstanding. | [Recognition service](../src/Kora.Windows/Audio/WindowsVoiceRecognitionService.cs), [audio stream](../src/Kora.Windows/Audio/BlockingAudioStream.cs), [recognition tests](../tests/Kora.Windows.IntegrationTests/Audio/WindowsVoiceRecognitionServiceTests.cs) |
| I06 | Delivered bootstrap; speech/privacy proof outstanding | Windows TTS plus separately offered optional Kokoro assets/provider; local installation/hash checks, voice/device selection, preview, playback stop and visual fallback for unavailable/muted output. Basic recognition filtering during Kora speech is implemented, not proven acoustic echo/playback rejection or optional owner-aware privacy. | [Speech service](../src/Kora.Windows/Audio/WindowsTextToSpeechService.cs), [Kokoro](../src/Kora.Windows/Audio/KokoroTextToSpeechProvider.cs), [application coordination](../src/Kora.Application/ViewModels/MainViewModel.cs), [speech tests](../tests/Kora.Windows.IntegrationTests/Audio/WindowsTextToSpeechServiceTests.cs), [Kokoro tests](../tests/Kora.Windows.IntegrationTests/Audio/KokoroTextToSpeechProviderTests.cs) |
| I07 | Delivered bootstrap; durable storage partial | Local/roaming directories, SQLite creation/integrity/schema-version checks and a `setup_tasks` table. `SetupTaskLedger` is in-memory, single-running-operation bootstrap state. Neither the table nor the ledger implements encrypted durable conversations, general task scheduling, history or retention. | [Storage probe](../src/Kora.Core/Dependencies/StorageDependencyProbe.cs), [SQLite probe/tests](../tests/Kora.Core.UnitTests/Dependencies/SqliteDependencyProbeTests.cs), [setup ledger/tests](../tests/Kora.Core.UnitTests/Dependencies/SetupTaskLedgerTests.cs) |
| I08 | Delivered bootstrap | Capability readiness and separate consented setup orchestration; PowerShell 7.4+ probing/install/re-probe is independent of local inference. Open Setup installs nothing. No general PowerShell task worker or executable grant is supplied by readiness. | [Application setup](../src/Kora.Application/ViewModels/MainViewModel.cs), [PowerShell setup/tests](../tests/Kora.Windows.IntegrationTests/Dependencies/WindowsPowerShellSetupServiceTests.cs), [dependency bootstrap](../src/Kora.Core/Dependencies/DependencyBootstrapper.cs) |
| I09 | Delivered bootstrap; R02 partial evidence, D-003 open | Consented per-user Ollama 0.35.1 setup and pinned `qwen3:1.7b` download; loopback runtime/model/digest checks and a completed inference response. No automatic cloud fallback. R02 verifies public identity/licence/download metadata and real missing-endpoint/unavailable behavior; 31 deterministic tests validate the harness, not model answers. Actual quality/performance/context/cancellation, floor, distribution and offline-success proof remain blocked. | [Ollama setup](../src/Kora.Windows/Dependencies/WindowsOllamaSetupService.cs), [inference probe](../src/Kora.Windows/Dependencies/LocalInferenceDependencyProbe.cs), [setup tests](../tests/Kora.Windows.IntegrationTests/Dependencies/WindowsOllamaSetupServiceTests.cs), [probe tests](../tests/Kora.Windows.IntegrationTests/Dependencies/LocalInferenceDependencyProbeTests.cs), [R02 outcomes and technical plan](Local_Inference.md) |
| I10 | Partial model interaction | Unmatched requests use one local reasoning operation with bounded status context and exactly one answer, question, action or grant-change response. Request length is 4,096 characters; inference deadline is two minutes; question prompts/options and three follow-ups are bounded. Generation is buffered, not streamed. Busy guards reject competing requests; handler results are not returned for tool-loop continuation. | [Reasoner](../src/Kora.Windows/Dependencies/WindowsOllamaReasoner.cs), [reasoner tests](../tests/Kora.Windows.IntegrationTests/Dependencies/WindowsOllamaReasonerTests.cs), [application/tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.cs), [current protocol](Internal_Model_Tools.md#implemented-bootstrap-surface) |
| I11 | Partial authorization and questions | Native model-action Once/Session/Always approval, bounded clarification choices, grant Add/Remove/Move/revoke and a grant document/editor. Session is an in-memory action-name set, cleared on Kora's lock path; Always is JSON action-name preference storage. Neither is the content/invocation-bound grant store or durable Kora work-session scope. | [Application/tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.cs), [approval preferences](../src/Kora.Application/Configuration/LocalModelApprovalPreferences.cs), [response controls](../src/Kora/ResponseWindow.axaml) |
| I12 | Partial computer controls | Exact lock stops owned audio then calls the Windows lock API without the model-action gate; model-proposed lock uses that gate. API acceptance is not independent observation of lock completion. Shutdown/restart create inspectable, cancellable proposals only; no OS power request is sent. No embedded lock/power scripts or all-session power coordination are implemented. | [Host action handlers](../src/Kora.Application/ViewModels/MainViewModel.cs), [Windows session controller](../src/Kora.Windows/Session/WindowsSessionController.cs), [application tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.cs), [session-helper tests](../tests/Kora.Windows.IntegrationTests/Session/WindowsSessionControllerTests.cs) |
| I13 | Partial lifecycle/work controls | Show/hide/exit/current-app restart, stop speaking and cancellation of current setup/inference. Status/progress describe setup/activity, not a general authoritative work ledger. Restart launches the executable and closes this instance; no cross-build handoff, general queues or crash-safe work recovery is implemented. | [Host controls](../src/Kora.Application/ViewModels/MainViewModel.cs), [process controller](../src/Kora/DesktopApplicationProcessController.cs), [application tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.cs) |
| I14 | Partial call support | Call-state contracts, preferences and application output/activation policy exist with fake-state tests. The composed Windows service reports Unavailable and supplies no automatic detector. Manual call state, the new reusable-grant/origin policy and genuine provider/signal integration are outstanding. | [Composed service](../src/Kora/Program.cs), [unavailable adapter](../src/Kora.Windows/Communication/UnavailableCallStateService.cs), [call preferences](../src/Kora.Application/Configuration/LocalCallAwarePreferences.cs), [application tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.cs) |
| I15 | Delivered bootstrap; security audit partial | Structured daily JSON logging, a content-minimizing audit bridge and bounded daily-log reader/UI. These are not encrypted session events, tamper-evident operation receipts or model-facing diagnostics tools. | [Logger configuration](../src/Kora/Program.cs), [audit bridge](../src/Kora.Application/Auditing/LoggerSecurityAuditLog.cs), [log reader](../src/Kora.Application/Diagnostics/LocalApplicationLogReader.cs), [reader tests](../tests/Kora.Application.UnitTests/Diagnostics/LocalApplicationLogReaderTests.cs) |
| I16 | Delivered CI configuration; R02 proof partial; WiX production direction selected | Ubuntu win-x64 cross-publish at R01 revision verified; static runtime/native/licence inventory, exact-revision managed-source publish/rerun and one Windows-assembled unsigned NSIS 3.13 setup demonstrated. Retain that proof as historical evidence; native Linux NSIS assembly is no longer a gate. Repository licence/NuGet controls are delivered; WiX/external-asset review, production packaging, protected runtime-only Windows trials and future resource/worker evidence remain open. No production bootstrap installer, official release/feed or startup registration is delivered; win-x86 CI output is not x86 acceptance. | [CI workflow](../.github/workflows/ci.yml), [R02 proof/results](../experiments/r02-distribution-proof/README.md), [canonical outcomes](Distribution_And_Updates.md#r02-distribution-outcomes-and-direction), [follow-up delivery plan](#r02-distribution-follow-up-and-r17-delivery) |
| I17 | Experimental containment evidence; production admission blocked | Fixed AppContainer/Job Object/PowerShell proof: 63/71 OS assertions met; protected stand-ins/credential denied, descendant identity/lifetime observed, lost/malformed receipts remain Unknown. Eight network-denial assertions unproven; executable dependency allowlisting and normal-host deployment protection not established. No production worker or bundled catalogue. | [Measured snapshot](../experiments/r02-containment-proof/evidence/README.md), [canonical outcomes](Security_Data_Flows.md#r02-windows-containment-outcomes), [continuation gates](Security_Data_Flows.md#windows-containment-continuation-gates) |
| I18 | Experimental Node and .NET RT1 proofs; production Gate 0 incomplete | Node SDK 1.0.16/runtime 1.0.90 witness unchanged: 16 tests, 13 PASS/1 FAIL/3 BLOCKED rows. Separate .NET RT1 explicitly approved exact-tag source-built profile: 45/45 tests, 44 selected-profile PASS/1 rejected hook-only FAIL; final initial/history/all-status/exception mediation, pre-effect denial, session I/O, streaming/errors/cancellation/isolation measured. Released NuGet bytes, RT2 global lifecycle observation, .NET MG1 and PV1 remain blocked/not run. No production adapter/scheduler delivered. | [Historical Node evidence](../experiments/r02-runtime-proof/evidence/results.json), [actual .NET RT1 fixture](../experiments/r02-dotnet-control-proof/README.md), [technical outcome](Runtime_Provider_Feasibility.md) |

### Most Important Design-to-Code Gaps

1. **Privacy and ownership:** the current grammar recognizer sees pre-activation microphone audio; checking Windows state at startup and stopping for Kora's own lock request do not implement mandatory shutdown on every external lock/disconnect/suspend. Resolve capture policy and prove event-driven ownership before presenting continuous listening as production-ready.
2. **Authority:** exact lock and model lock use different gates. Existing reusable preferences are not complete execution grants. Future direct, UI, model and skill paths must converge on the same host-owned gateway; do not migrate action-name preferences into broader script authority implicitly.
3. **Useful model tools:** no formal typed registry, mediated tool/result iteration, explicit clipboard broker, remote runtime or model-result streaming is delivered. A model answer or action enum is not this contract.
4. **Managed sessions:** there is no durable Kora work-session/event store, full history, general queue, resource-leased scheduler or independent management lane. Bootstrap status cannot answer the proposed session-state tools authoritatively.
5. **Skills and integrations:** no bundled multi-script runner/review, admitted MCP connector, shared-source skill discovery or declarative authoring workflow is delivered.
6. **Release proof:** unit/fake-backed tests, endpoint enumeration, local digest/readiness checks and downloadable CI archives do not close the real Windows, runtime, containment, installation or performance gates.
   The next local-inference action is to assign/approve a reference test owner,
   environment and budgets, then consent to pinned provisioning; it is not to
   implement a tool loop or choose a larger model while evidence is absent.
   Follow the [R02 local-inference continuation](#r02-local-inference-continuation).
7. **Runtime path after R02:** I18 rejects hook-only integration, not Copilot
   outright. Preserve the scoped source-built .NET RT1 pass, then prove global
   runtime observation, the full .NET management envelope and approved provider
   eligibility. Do not count Node
   loopback passes as production authority, account capacity or .NET parity.

## Ordering Rules

The order below is an implementation work-package order, not a renumbering of the product's A0-A4 acceptance checkpoints.
Some A3 primitives, notably session identity, immutable intent, approvals and storage, must be implemented early so A0's context/tool path cannot accumulate incompatible transient authority.
That does not mean the complete A3 product is accepted before A0.

- **P0:** privacy, authority, data integrity or feasibility blocker. Complete the relevant gate before exposing dependent functionality.
- **P1:** core user value and required initial-release capability.
- **P2:** integration/authoring expansion after the core interaction is accepted.
- **P3:** independently optional or deferred expansion.

Earlier numbered work is the preferred start order. The **Needs** column is the actual dependency graph; a higher number does not imply all earlier packages are hard prerequisites.
Independent prototypes/tests can run in parallel after their prerequisites pass.
Do not enable a dependent capability with an unresolved earlier safety/correctness failure.
Use the current router, setup, speech services, preferences, native surfaces and tests as migration foundations; do not discard working bootstrap behavior to replace everything at once.
Keep current/planned labels and unavailable-tool exclusion until the exact replacement path is verified.

## Design Reconciliation Before Expansion

R01's initial-release choices were approved on 2026-10-05 in the reconciliation
session. The [decision register](Decision_Register.md#r01-accepted-policy-reconciliation)
records that approval and separates accepted contracts from implementation
evidence. No runtime behavior was changed or acceptance trial performed by R01.

| Issue | Affected contract | Required resolution and affected work |
|---|---|---|
| Management power authority | [Management boundary](Security_Data_Flows.md#management-power-proposal-authority) and [power proposal tools](Internal_Model_Tools.md#computer-controls-and-notify-only-maintenance) now agree. | Resolved: M/E submit proposals only; host lifecycle owns approval/countdown and gateway/worker dispatch. No M task tools or self-approval. R06/R13/R16 exposure still requires implementation evidence. |
| Capture after restart/unlock/resume | [Canonical matrix](Security_Data_Flows.md#microphone-consent-and-enablement-matrix), [lifecycle](Task_Lifecycle.md), [configuration](User_Configuration.md) and acceptance are aligned. | Resolved: explicit first-launch ongoing consent; saved consent permits fresh-gated ordinary startup/restart. Unlock/resume/manual-disable/loss recovery requires explicit Enable listening within the run; consent withdrawal persists. R03/R09/R17 implement it. |
| Optional private-speech fallback | [Design index](README.md), [lifecycle](Task_Lifecycle.md), [MVP scope](MVP_Scope.md) and [security](Security_Data_Flows.md#explicit-verification-and-output-privacy) condition owner-aware fallback on enablement. | Resolved: baseline voice needs no verifier; selected owner-aware protection falls back visually on uncertain/unavailable confidence and never silently switches Off. R09/R15 and optional R24 use this boundary. |
| Acceptance placement | Earlier additional-capability wording classified Ollama/independent executors outside MVP despite A2/A3 requirements. | The [additional gates](Acceptance_Criteria.md#additional-capability-evidence) are reclassified with this roadmap; retain concrete A2/A3 proof rather than treating it as a later enhancement. |
| Standalone lock Session grants | [Bundled skill binding](Built_In_Skills.md#standalone-lock-work-session-binding) defines durable identity before approval/priority dispatch. | Resolved: create a new Active control work session for an unaddressed standalone request; commit and present binding before offering Session. Deliberate Active-session addressing is preserved; no selected-window authority. R04/R05/R11 prove it. |
| Deferred application rollback | [Execution design](../docs/skill-and-task-execution-design.md) distinguishes permanent bundled-content revocation from standalone application-binary inapplicability. | Before R27, specify whether restoration of old standalone application bytes can restore applicability. This deferred decision does not block read-only/initial fixed bundled capabilities; do not generalize bundled revocation to it without a decision. |

## Ordered Outstanding Work

Every completion condition below also requires directly related documentation, negative-path tests and the applicable [acceptance evidence](Acceptance_Criteria.md).
Opening an interface or passing a fake-only happy path is not completion.
Every body of work must drive an actionable change to implementation or canonical design/technical documentation.
For feasibility work, record the selected direction or rejection, material limitations, remaining gates and concrete downstream requirements here and in the owning contracts.
A checked-in experiment or report alone is supporting evidence, not the outcome or completion of a work package.

R02 contains independently closable runtime/provider, local inference, speech/hardware,
Windows worker/deployment, storage/key and distribution proof branches.
An R02 dependency names the relevant branch below, not a requirement to finish
unrelated vendor/packaging investigations before safe local work can proceed.
R03's existing-host privacy/ownership work can start after R01 and runs its own
actual Windows trials; it need not wait for a Copilot integration decision.
R01's required policy decisions are now resolved: R02 feasibility branches and
R03 ownership/privacy implementation are unblocked at the design dependency.
Their technical and real-boundary acceptance gates remain outstanding.

### R02 Windows Containment Follow-Up

The [measured worker proof](../experiments/r02-containment-proof/evidence/README.md)
narrows the next work rather than closing R02: reject Job-only restricted
execution and retain capability-free AppContainer as a partial candidate.
[D-013](Decision_Register.md#d-013-windows-worker-and-deployment-containment)
owns the unresolved mechanism/admission decision; the
[security contract](Security_Data_Flows.md#windows-containment-continuation-gates)
defines the exact gates without weakening the existing bundled-script design.

The labels below are follow-up work within the existing packages, not new tool
IDs or replacements for the R01-R29 dependency graph.

| Follow-up | Package / owner | Current state and next deliverable | Closure criterion |
|---|---|---|---|
| W1 - Attribute network denial | R02 / Windows and security leads | Eight timed-out assertions remain unproven. Repeat fixed trials on a supported reference OS with positive controls and attributable OS enforcement observations; cover applicable protocols/address families and descendants. | Required paths are actually denied by the worker boundary. Timeouts/unknown diagnostics do not pass; request approval before privileged/disruptive trials, never substitute global policy changes. |
| W2 - Resolve fixed-action and dependency mechanism | R02 / Windows and security leads | AppContainer allows ordinary children and does not prove executable/module admission. Demonstrate denial of undeclared dependencies and feasibility of exact fixed controls; otherwise bring an explicit typed-adapter/broker versus unavailable decision. | Selected mechanism and contract changes, if any, reviewed with enforcement evidence. No ambient-shell fallback or implicit replacement of embedded-script requirements. |
| W3 - Prove identity, protected roots and aliases | R02 deployment feasibility, then R17 installed acceptance / release and security leads | Independently owned payload/parents and actual normal-app/worker tokens UNTESTED. Use [the security identity checklist](Security_Data_Flows.md#protected-deployment-identity-and-validation); distribution packaging/runtime-only follow-up is separate. | Approved Windows fixtures demonstrate effective app/worker denial, protected dependency resolution and alias/TOCTOU handling. Build/inspect-only constraints leave these trials blocked, not waived. |
| W4 - Integrate and accept only the admitted profile | R11 / application and Windows leads; R16 exact effects; R17 installed launch | No production worker/catalogue. After applicable W1-W3 gates pass, wire immutable snapshots, common grants, bounded supervision/receipts; test host death and effect/cancellation races. | R11 fixed-profile admission, R16 actual approved controls and R17 installed evidence pass separately. General executable imports remain R27 work. |

W1/W2 investigation and W3 build/inspection may proceed independently after
R01. No R11 restricted dispatch is exposed from partial I17 evidence. Pure
catalogue/gateway development may remain disabled while proof is outstanding;
R16 cannot expose new script-backed controls until the applicable R11 gate passes.
R17 packaging work can proceed without an unmerged worker dependency, but
absent workers/catalogues and unperformed deployment trials remain explicit
acceptance blockers. The current bootstrap's direct C# lock behavior is unchanged
and still lacks the future common content-bound gate.

The [deferred-validation register](Deferred_Validation.md) and
[containment testing checklist](../experiments/r02-containment-proof/README.md#outstanding-testing-checklist)
make the remaining W1-W4 interactive/privileged trials runnable as separately
approved future work. Merging partial research does not close those gates or
enable the affected profiles.

### R02 Storage/Key Outcome and Follow-On Work

The 2026-10-05 [storage investigation](../experiments/r02-storage-proof/README.md) now drives
the [Windows durable-storage contract](Architecture.md#windows-durable-storage-direction) and
[D-009](Decision_Register.md#d-009-session-persistence-and-retention), rather than leaving recommendations only in an experiment.
Recorded evidence: passing automated assertions, 14 actual terminated/recovered child processes, same-user DPAPI/ACL checks, Windows x64/x86 native publication, and measured small-record read/write performance. Historical snapshots and revised profile-integration results are distinguished in the owning proof.
No production store, schema, grants, lifecycle or dispatch implementation was changed.

| Work / owner | Outcome or remaining action | Status / dependency consequence |
|---|---|---|
| R02 storage/key direction - storage and security leads | Prefer maintained authenticated page encryption for content-bearing SQLite/indexes, AES-GCM artifacts and CurrentUser DPAPI-wrapped keys with restricted ACLs; reject the measured old unofficial native engine. Conditional envelope fallback requires revisiting D-009, not silent downgrade. | Direction recorded and synthetic evidence measured; R02 storage/key admission is not closed |
| R02 storage/key admission - storage, security and release leads | Select/review a maintained native distribution and prove installed Windows x64/x86 loading/protection. Re-run affected proof on the selected engine. | Outstanding capability admission; not a blocker to merging the documented partial research. R04 production persistence remains dependent on this gate |
| R04 durable foundation - storage and application leads | Implement the admitted strategy, CurrentUser/profile-path/effective-ACL integration, ordered durable intent/receipt boundaries, versioned key/backup publication and interrupted rotation recovery, verified legacy conversion, and staged/orphan/missing/corrupt artifact reconciliation with size limits. Define host types separately from private proof fixtures. | Outstanding; concrete requirements derived from R02, not implemented by it |
| R12 lifecycle/deletion - storage, security and application leads | Inventory/remove or rewrite managed recoverable copies, prevent late appends, preserve unrelated sessions/independent grants, and integrate source revocation, retention/apply-now/live/unknown-work policy. Disclose exported/provider/forensic limits. | Outstanding; deleting rows/current keys alone cannot satisfy D-009 |

Windows is the only supported product OS. Linux runtime support and local Linux-host cross-build validation are outside this storage proof, not additional gates.
Existing Linux-hosted Windows CI/distribution requirements are unchanged.
Ordinary cross-profile isolation is a trusted Windows boundary for this profile-local architecture; a second-account OS-denial trial is optional, not a routine application/merge gate.
The [profile-boundary contract](Architecture.md#profile-boundary-and-validation-responsibility) requires application integration checks and defines changes that would trigger actual multi-account validation.
The [storage deferred-validation checklist](Deferred_Validation.md#storage-admission-follow-up) makes remaining native/deployment and R04/R12 work actionable without requiring an unlocked console for safe scratch reruns.
Other R02 branches remain independently outstanding; the storage results do not close provider, speech, worker or distribution investigations.

## R02 Runtime/Provider Follow-Up Gates

These are sub-gates of R02, not new acceptance milestones or a claim that
other R02 feasibility branches are complete. The
[technical continuation](Runtime_Provider_Feasibility.md) specifies the
candidate boundaries and unsupported-control decision path.
No gate below is closed by merging the retained Node experiment.
The [deferred runtime/provider checklist](Deferred_Validation.md#runtimeprovider-follow-up)
records preparation and evidence to collect. The separate
[actual .NET RT1 fixture](../experiments/r02-dotnet-control-proof/README.md)
now passes its explicitly approved source-built minimal profile; RT2/MG1/PV1
and Gate 0 remain open. Partial research does not enable production exposure.

| Gate / owner | Current state | Needs / next action | Exit evidence and downstream effect |
|---|---|---|---|
| R02-RT1 - .NET control-point parity; runtime engineering lead | PASS, scoped source-built profile, 2026-10-05 UTC; 45/45 tests; released NuGet byte parity BLOCKED | Preserve [fixture/disposition](../experiments/r02-dotnet-control-proof/evidence/disposition.json): public v1.0.16 exact source, runtime 1.0.90/protocol 3, Windows x64, .NET 10.0.12/SDK 10.0.401. User explicitly approved unmodified source build after NuGet TLS failure; separate locks/license/native hashes and byte reproduction recorded. | Final serialized initial/history/all-status/exception paths, actual pre-effect denial, streaming/errors, session I/O and cancellation/isolation measured; denied effects/forwarded markers zero. Hook-only FAIL retained, Node witness unchanged. Unblocks RT2/MG1 proof for these bytes only; no production adapter, sidecar or D-001 closure. Retest a different artifact/profile. |
| R02-RT2 - Runtime lifecycle observation; runtime and security leads | Not run; RT1 scoped prerequisite satisfied; P0 | Use exact RT1 source-built minimal profile or retest RT1 for a changed artifact. Prepare attributable observation and obtain approval for any install/elevation/policy change. Observe startup, session, authentication, failure and shutdown network/files/diagnostics; account-specific paths repeated in PV1. Inventory optional helpers, initialization metadata, collection and persistence. | Complete destination/storage inventory with actual prevention/mediation and content-minimizing diagnostics, no denied-marker egress/persistence. RT1 scoped scans/model handler do not replace all-path observation. Unobservable/uncontrollable paths keep runtime unavailable; required before R08, not separate worker/deployment/storage closure. |
| R02-MG1 - .NET host management envelope; runtime engineering lead | Not run in .NET; Node envelope retained; RT1 scoped prerequisite satisfied; P0 for model-assisted management | Can run alongside RT2 for exact RT1 bytes. Implement complete 32768/32769-byte input and 4096/4097-byte typed output, 15000-ms host dispatch deadline independent of stalled acknowledgement, one in-flight/30 attempts per rolling hour/no forwarded retry, independent manager and unknown-effect quarantine. | Real .NET exact-byte and held-request/admission evidence. RT1 shows SDK wait timeout is not abort and admitted effects can complete after acknowledgement; quarantine until observed termination/receipt. Passing supports management PV1/R13 design, not account capacity, production schema or scheduler acceptance. |
| R02-PV1 - Approved account/provider trial; runtime lead with account owner and security/legal review | BLOCKED; no account/live usage approved | RT1/RT2 for live runtime trials; MG1 additionally for management. First prepare intended provider/model/region, user-owned supported auth, permitted assistant/SDK use, plan/policies, published limits and explicit spending controls. Ask for account and usage-budget approval before potentially paid inference/provisioning. | Record real auth/error behavior, destination/content isolation, terms eligibility, quota/rate limits and billed-cost assumptions/limits. Execution profile acceptance feeds D-001/R08; management-specific two-execution-plus-manager capacity and usage envelope additionally feed D-004/R13. Failed/incompatible/unapproved service stays disabled with deterministic local choices. |

**Next sequence:** scoped RT1 passed; RT2 and MG1 may proceed independently
for its exact approved source-built profile;
PV1 live trials follow the relevant technical gates and explicit approval.
If RT1 fails, bring the supported-runtime/sidecar/inference-adapter options
back as a D-001 decision with evidence; do not weaken Gate 0.
Local R06/R07, R03 and the deterministic R12/R13 core retain their own
prerequisites and need not wait for hosted-model trials.
Downstream packages listing R13 require its deterministic core and admitted
scheduler/concurrency evidence; its optional model-assisted stage is not an
implicit prerequisite unless that dependent capability uses management inference.
Neither I18 nor these runtime follow-ups close D-003/local inference,
speech/hardware, worker containment, deployment or encrypted-storage work.

### Core Foundations and First Useful Interaction

| ID and work package | Starting state | Priority/value | Needs | Completion condition |
|---|---|---|---|---|
| R01 - Reconcile policy, scope and checkpoint contracts | Design reconciliation complete; approved 2026-10-05; no runtime changes | P0 - prevent incompatible authority and consent implementations | None | Initial-release authority, consent, optional privacy and standalone-lock binding recorded and aligned above; A2/A3 evidence remains required. Standalone application rollback remains deferred R27 work. Runtime/enforcement proof is not claimed by this package. |
| R02 - Run release-blocking feasibility proofs | Partial candidates in I01/I06/I09/I16; storage, local-inference and distribution outcomes recorded, containment I17 and runtime I18 measured with open gates | P0 - discover impossible runtime, hardware or containment assumptions before expansion | R01 | Complete runtime RT1/RT2/MG1/PV1 as applicable, retaining rejected hook-only and blocked account/global paths as unavailable. Separately prove local model/license/reference CPU floor, wake candidates, OS worker containment, protected deployment and encrypted-storage/key strategy. Update canonical contracts and downstream work with D-001 through D-005/D-007/D-009/D-013 choices, rejected candidates and limitations. Close storage/key native and real Windows admission, [local L1-L5 qualification and L6 handoff](#r02-local-inference-continuation), applicable containment W1-W3 gates and distribution R02-D01/D02/D03 below; Windows NSIS assembly alone does not close D-005. Use actual SDK/provider/OS evidence, not aggregate R02 success; later packages must pass their relevant proof branch before exposure. |
| R03 - Establish Windows/audio ownership and privacy foundation | Partial I03-I06/I12/I13 | P0 - stop unauthorized capture and overlapping owners | R01 | Implement cross-build single-owner activation/handoff/return, consent/enablement generations, explicit PTT, bounded audio/transcript buffers and stale-callback rejection. Enforce wake-only versus activated-transcription separation; never label current ambient grammar capture as production wake. Observe lock/disconnect/suspend/permission/device changes and close capture/clear audio/output on every required event. Release capture within 500 ms of observed lock in every reference trial. Provide native/tray recovery without model/network/speech. |
| R04 - Introduce durable identities, Activity tracing and authoritative host contracts | Partial setup storage/ledger in I07/I15; ad hoc audit correlation only; R02-derived storage contract available, admission open | P0 - stable attribution, causal state and crash-safe intent | R01, R02 (storage/key admission), R03 | Define typed request/origin, work-session/task/question/proposal/invocation/revision/evidence identities, result states and resource descriptors. Introduce stable versioned `Kora.*` ActivitySources and W3C tracing across startup, request/session/task/approval/runtime/tool/storage/presentation/evidence/retention boundaries; use child context or explicit links correctly and stamp trusted durable Kora IDs without baggage. Implement independent daily-file and encrypted SQLite `ILogger` providers: ordinary records enter structured `application_log_events`, typed `SecurityAudit=true` records enter dedicated authoritative `security_audit_events`, and completed span/link metadata enters `activity_spans`/`activity_links`. Capture activity context at the log call; preserve the formatter-independent envelope, promote indexed trace/session/task/invocation/approval/correlation identities, and add fixed audit columns without parsing rendered text. Implement diagnostic 30-day/audit 90-day defaults and audit 30-365 configuration with pruning anchors. Pin/bundle the encrypted SQLite closure and add CurrentUser/ACL integration, ordered commits, indexes, sink/gap markers, key/backup recovery, conversion, artifact reconciliation, migration/integrity/crash recovery and no replay. Lay the foundation, not the full history/queue UI; private proof fixtures are not the host schema. |
| R05 - Build the shared authorization/question gateway | Partial I11/I12 | P0 - one authority path for direct/UI/model/skill requests | R03, R04 | Implement host-owned typed questions, exact proposals, native trusted input, single-use atomic consumption, operation-bound durable Session grants and independently retained Perpetual grants without expiry/retention/eviction. Add complete implementation/invocation/resource identity, immutable review, explicit grant edit/removal, audit/receipt certainty and immediate dispatch revalidation. Apply the resolved standalone-lock rule. Legacy action-name preferences confer no new executable authority without explicit review/approval. |
| R06 - Implement the admitted tool registry and local tool/result loop | Partial JSON selector in I02/I09/I10; R02 harness is not adapter qualification | P1 - natural requests can discover and use Kora capabilities | R02 (local runtime: L1-L5 qualification), R04, R05 | Expose versioned admitted schemas/skill summaries per lane; implement bounded validated proposals, host execution and correlated approved results followed by continued reasoning. Use the R02-L5 tested compatibility/context/resource envelope, preserve digest checks and rerun affected proofs on integration. First deliver discovery/application/readiness/runtime/status tools, deterministic status presenters and typed success/denied/unknown/unavailable behavior. Preserve exact offline safety routes. Add registry/catalogue coverage and hostile-result/unknown-ID/late-cancellation tests; do not expose unimplemented session tools. |
| R07 - Deliver explicit clipboard context and local-first explanation | Outstanding context path; partial inference I09/I10; no R02 real answer/offline-success proof | P1 - first useful private vertical slice | R03, R04, R05, R06; R02 local L5 evidence carried through L6 | Implement request-triggered plain-text clipboard snapshot/preview, immutable context/source IDs, purpose/secret/destination classification, bounded excerpts and explicit reuse/revocation. Fit the complete approved envelope to the qualified context budget or reject explicitly. Reuse R02 fixtures/rubric and isolation evidence, then repeat actual quality/cancellation/no-egress proof on the integrated host; synthetic payload injection is not clipboard-broker acceptance. Local inference missing/unhealthy remains unavailable with no remote fallback. Unsupported clipboard formats are explicit. |
| R08 - Integrate the controlled remote runtime and streaming path | Outstanding production adapter; I18 candidate only, hook-only path rejected; local production inference remains buffered | P1 - complete A0 and provider-neutral interaction | R02-RT1/RT2 and execution R02-PV1; local L5 envelope for local streaming; R04, R05, R06, R07 | Integrate the proved .NET/runtime profile through pre-effect authorization, all-status host result sanitization and a final serialized-request egress gate. Disable unverified built-ins/collection/storage/transports; keep credentials host-only and cancellation truthful. Pass Gate 0 including actual account/destination/diagnostic evidence, streamed output/backpressure and zero denied effects/markers. Reuse host contracts and the qualified local envelope for local streaming/iteration; measure user-visible first output and cancellation, not experimental token timing alone. No production Node bridge or alternate provider without an explicit D-001 decision. |
| R09 - Complete production wake, endpointing and speech lifecycle | Partial I04-I06 | P0 - enable reliable voice-first use only after quality/privacy proof | R02 (speech/hardware), R03, R05, R06 | Package selected licensed detector/VAD/transcription assets; preserve immediate wake-and-command with at most two seconds of overwritten pre-roll and no unrelated pre-activation transcription. Bound command/audio lifetimes; prove playback/echo rejection, voice interruption, TTS stop/shutdown and device recovery. Implement configured activation-name profiles and lifecycle matrix. Meet actual A1 speech/CPU/memory/latency targets; optional learning/verifier is not required. |

### R02 Local-Inference Continuation

The [technical outcomes and qualification contract](Local_Inference.md) turn
the partial proof into the steps below. **First action: assign a test owner,
approve the floor/isolation environments and agree budgets (L1).** No trial
failed model quality; it was blocked by missing runtime/model and consent.
R01 is satisfied. Provisioning, independent network enforcement and actual
runtime/hardware evidence are not satisfied by the harness or its merge.

These are substeps of the existing local-inference branch, not new top-level
roadmap packages, model tools or requirements on unrelated R02 branches.
Owners are accountable roles until individuals are assigned.

| Step | State / owner | Depends on | Next action and completion receipt |
|---|---|---|---|
| R02-L1 - Approve environments and budgets | Outstanding; product and test leads with runtime lead | R01; test-machine ownership and approval | Select supported Windows 11 x64, named reference CPU/8-logical-core/16-GiB/SSD floor and approved disposable isolation environment. Record power profile, UI/services/contention and CPU-only verification plan; agree numerical cold/warm latency, memory/CPU headroom and cancellation/recovery budgets before trials. Record operator/provisioning/isolation consent; a faster host or VM alone is not qualification. |
| R02-L2 - Provision and verify the pinned baseline | Blocked on consent/runtime; runtime and release leads, test owner | L1; separate asset acquisition/startup consent | Reuse or provision only the pinned Ollama/qwen baseline. Verify installed identities and licences/native notices; record download, expanded/peak staging storage and free-space requirements on runtime/model volumes. No changed production pins, automatic replacement or task-time download. Receipt: reproducible version/digest/licence/storage inventory. |
| R02-L3 - Run actual CPU-floor inference trials | Blocked; runtime and test leads with human quality reviewer | L2; L1 budgets | Run at least 30 cold + 30 paired warm synthetic trials and production-default answers; human-score fixture quality. Measure effective context/oversize handling, output/thinking bounds, process RAM/CPU, repeated completion/cancel races, actual timeout, server cessation and recovery. Retain raw outputs/counters, individual failures and p50/p95/max comparisons to agreed budgets; mocks do not count. |
| R02-L4 - Prove independently blocked egress | Blocked on approved isolation; test and security leads | L2; approved whole-environment remote-network block/capture from L1 | Repeat relevant successful answering, cancellation/recovery and missing/unhealthy trials under verified IPv4/IPv6/proxy/NAT denial with loopback retained. Retain approval, effective controls, interval/capture and process correlation covering Kora and Ollama/runner. Receipt: no remote payloads, no fallback and 100% critical policy/cancellation passes; loopback-only code is insufficient. Can run alongside L3 once its environment is ready. |
| R02-L5 - Record candidate and hardware disposition | Open; runtime lead for D-003, product/test leads for D-007, release/security review as applicable | L3 and L4; distribution review | Record selected/rejected candidate, tested compatibility/context/resource envelope, agreed budgets, supported hardware/OS, residual limitations and reconsideration triggers. Update decision/architecture/setup/acceptance documentation with evidence. A failed gate requests a consented alternative trial or explicit hardware/scope decision; never silent cloud substitution or floor revision. Candidate qualification does not accept A2. |
| R02-L6 - Apply and repeat evidence on the host | Pending integration; R06/R07/R08/R10 owners, R19 test lead | L5; each dependent package's existing prerequisites | R06 uses the qualified envelope/digest and unavailable states; R07 budgets immutable selected context and repeats offline quality/cancellation on the clipboard workflow; R08 implements/proves streaming and late-output handling; R10 supplies measured per-volume setup budgets and tested compatibility. R19 compiles actual host/UI/voice evidence. Receipt: applicable integrated conformance and A2 sign-off, not a synthetic harness result. |

The immediate disposition is to **retain the pinned candidate for L2-L4
trials**, not select a new model or claim CPU-floor support. L1-L5 are the
local-runtime qualification dependency for exposure; L6 is the handoff to
implementation and integrated acceptance. R03 and independent R02 branches can
continue while this branch is blocked. Draft proof/documentation review does
not change decision or capability acceptance status.

The [shared deferred-validation register](Deferred_Validation.md) and
[LI01-LI07 interactive checklist](../experiments/r02-local-inference-proof/README.md#outstanding-testing-checklist)
track each unperformed inference trial's prerequisites, approvals, instruments
and completion evidence alongside the independent speech/storage/containment/distribution
proofs. The harness/outcomes/handoff may merge as partial research; L1-L6 and
dependent capability acceptance remain outstanding. Do not hold publication of
that limited scope for live validation or inherit qualification from another
proof's measurements.

### Complete the Required Slice A Product

| ID and work package | Starting state | Priority/value | Needs | Completion condition |
|---|---|---|---|---|
| R10 - Implement typed configuration and capability-scoped setup | Partial native preferences/setup I03/I04/I08/I11/I14; [dependency design inventory](Dependency_Catalogue.md) recorded, executable catalogue incomplete; R02 identifies unmeasured total-install budget and runtime-compatibility gaps | P1 - consistent voice/UI controls without unsafe mutations | R04, R05, R06, R09; R02-L2/L5 evidence for local-model setup claims | Register schemas/defaults/bounds/scopes/revisions for speech/devices/output, queues/deadlines/retention/concurrency, runtimes, grants/calls, appearance/startup and admitted extensions. Supply discovery/get/propose-set/reset, exact previews, safe apply/recovery and audited rollback/conflicts. Turn admitted inventory entries into versioned source/identity/verification/ownership/probe/consent/refusal records; keep experimental and unimplemented adapters unavailable. For local inference, distinguish transfer/model storage from expanded runtime/staging/per-volume headroom and use the tested compatibility envelope; the 2 GB model guard is not a total provisioning budget. Retain app-led dependency detection/installation with or without installer assistance; permit declining all optional providers and preserve the dependency-qualified deterministic subset without repeated prompts/downloads or cloud fallback. Keep installation/sign-in/secure workflows host-owned. Preserve device-local choices and protected-call origin/option gates. |
| R11 - Deliver registered embedded multi-script skills and containment | Outstanding runner/packages; I17 partial proof is not admission; partial readiness/lock I08/I12 | P0 - finish lock without admitting arbitrary execution | R02 (applicable W1-W3 worker/deployment gates, D-013), R03, R04, R05, R06, R10 | Embed lock manifest/instructions/fixtures, entry script and shared helper; verify `Kora.ScriptSet.v1`/`Kora.SkillDefinition.v1` complete framed identities and dependent-grant revocation. Direct/model/skill routes use the same pinned task exactly once with immutable source review. Complete W4: admit only fixed profiled workers with enforced dependency admission, attributable network denial, bounded output/cancellation/Unknown receipts and real OS filesystem/child-process/credential/Kora-resource isolation. Prove observed lock outcome. Reject unsupported profiles or an unapproved broker substitution. Prepare power packages but do not enable OS power until R16. |
| R12 - Complete session lifecycle, history and per-session work/queues | Outstanding beyond bootstrap I07/I13; R02 deletion/backup limitations recorded | P1 - durable, inspectable long-running work | R04, R05, R06, R10, R11 | Implement Active/Done, create/rename/select/resume/mark-done/delete, paged history/search/immutable artifacts and authoritative task state. Add ordered per-session queues, admission/deadlines/user waits/pause/cancel/remove/clear controls and no restart replay. Implement configurable 24-hour archive/30-day deletion on the same meaningful-activity clock, source revocation, late-append prevention and inventoried deletion/rewrite across journals/caches/indexes/artifacts/staging/managed backups. Prove preservation of unrelated content and disclose exported/provider/forensic limits; row/key unlink alone is insufficient. Session eligibility ends appropriately; independent Perpetual records survive session cleanup. |
| R13 - Add bounded independent management and concurrent execution | Outstanding production core; I18 loopback topology/envelope passed only | P1 - remain responsive while useful work runs | Deterministic core: R05, R06, R10, R12 and applicable R02 local concurrency budgets. Model-assisted stage additionally: R08, R02-MG1 and management R02-PV1 | Deliver deterministic routing/status/choices/cancel first without hosted inference. Add model-assisted management only after its .NET/account gates: complete serialized 32 KiB input/4 KiB typed output, host 15-second deadline, one in-flight, 30 attempts/rolling hour and no forwarded automatic retries. Independently prove two task slots, isolated identities/contexts/grants, resource leases/fairness and unknown-effect reconciliation; loopback conversation count is not scheduler evidence. No management task tools or approval authority; power follows R01. |
| R14 - Build the coordinated Sessions workspace and interaction surfaces | Partial compact/native windows I03/I11 | P1 - make sessions, decisions and results understandable | R05, R09, R12, R13 | Deliver compact latest interaction, list-plus-full-conversation/history, work/queue controls, Evidence mode and separate immutable detail/script surfaces. Evidence mode provides Logs, Audit and All Evidence views with deterministic bounded `evidence.list`/get/search/read_trace, daily-file reads, stable citations and authority/retention/gap status. Every session exposes evidence across all its traces; every row pivots to its W3C trace tree/links and durable session/task/invocation/approval/audit correlation. Add Ask Evidence as a visible read-only reasoning flow over explicit selected records/filters: local by default, exact remote-egress preview, cited observation-versus-inference answers and no recursive model tool or authority. Address native/voice questions explicitly; switching windows never retargets approvals. Add typed full-content views, bounded navigation and scoped evidence/artifact export. |
| R15 - Complete call-aware feedback, authorization and request-origin gates | Partial policy/preferences, unavailable real adapter I14 | P0 - prevent call leakage and reusable-authority surprises | R03, R05, R09, R10, R13, R14 | Deliver manual call state and capability-qualified detectors/signals with truthful Active/Suspected/Unknown/unavailable distinctions. Default feedback to UI-only and `calls.ignoreReusableGrants` to On. Preserve reusable records but require fresh exact Once approvals when protected. Reject voice-originated voice/in-call mutations across every route; a later UI confirmation does not change origin, so require new UI initiation. Race entry/clearance/settings/approval/dispatch and preserve grant-free stop/status controls. |
| R16 - Enable graceful protected power and all-session app controls | Partial proposals and current-app lifecycle I12/I13 | P0 - make disruptive actions safe and truthful | R01, R05, R11, R12, R13, R15 | Register/verify fixed shutdown/restart packages and helpers. Implement all-session impact review, fresh action-specific voice or equivalent UI confirmation, 30-second foreground prompt, two-minute single-use approval and 30-second cancellable host countdown. Perform mandatory real OS/provider checks; no extra UI click solely because risk is high, no forced close and no unrelated OS cancellation. Coordinate exit/restart and resource ownership; reconcile observed receipts rather than claiming success from a proposal. |
| R17 - Finish supported distribution, setup and startup behavior | WiX MSI + Burn direction selected; historical NSIS/managed-source proof and prerequisite inventory; production delivery and Windows acceptance outstanding | P1 - users can install/run safely without a development checkout | R02 (distribution/hardware: D01/D03 admission; D02 direction selected), R03, R09, R10, R11, R16 | Execute R17-D01/D02/D03 below: begin with a thin MSI/Burn install/upgrade/repair/uninstall slice, not another standalone feasibility project. Deliver production managed-source and framework-dependent win-x64 options, verified .NET Desktop/VC++ prerequisites, protected deployment and exact release provenance/licences/hashes. Keep Linux for portable build/test/cross-publish and release work wherever feasible; use Windows for WiX packaging. Accept installed final bytes on an external runtime-only Windows lab after required resources/workers and privacy gates exist. Add opt-in per-user logon registration/removal, optional capability consent and data-retaining recovery without adopting the lab-only NSIS installer. Existing win-x86 output is not x86 acceptance; certify each offered architecture. No in-app application-package updater/download/install capability is added; reviewed dependency setup remains available. |
| R18 - Implement trusted proactive and notify-only maintenance flows | Outstanding; some ordinary host notifications exist | P1 - useful feedback without model-created prompts or updates | R05, R10, R13, R14, R15, R17 | Create conversations from trusted task/host events; route decisions through shared native questions with deduplication, deferral, deadlines and rejection/fatigue controls. Check canonical GitHub Releases metadata with bounds/backoff and publish read-only snapshots; production excludes drafts/prereleases, explicit preview selection can include published prereleases, and CI/default-branch outputs are not releases. Deterministic maintenance may open the approved release page. Treat release notes as untrusted. Models cannot trigger checks, invent availability, choose feeds/navigation or download/stage/activate updates. |
| R19 - Accept the complete Slice A and platform/security gates | Proof outstanding | P0 - prevent a bootstrap/demo being released as the designed product | R07, R08, R09, R10, R11, R12, R13, R14, R15, R16, R17, R18 | Report A0-A4 separately with reference-machine actual results, synthetic offline/egress markers, exact provider versions, two-session resource/cancellation races, real containment and installer/lock/call/device trials. Meet each safety criterion and the measured latency/wake/hardware targets. Verify unsupported/disabled paths and no self-modification. Repeat earlier gates after later integration; resolve remaining initial-release decisions before sign-off. |

### R02 Distribution Follow-Up and R17 Delivery

This is the actionable distribution branch of R02, not additional platform
scope or a requirement to finish unrelated R02 investigations. Windows remains
the only deployed runtime; keep Linux for build/test/cross-publish and release
work wherever feasible, with Windows WiX packaging and portable architecture
seams. The
[distribution outcomes](Distribution_And_Updates.md#r02-distribution-outcomes-and-direction)
are the canonical technical input; the experiment is reproducible supporting
evidence, not the production implementation.

The IDs below subdivide existing R02/R17 work and retain their dependencies;
they do not renumber or bypass the parent packages. Private packaging and
boundary investigations can proceed now. R17 acceptance and public release
still need every prerequisite in the parent row, including later real resources
and workers. A blocked trial is pending work, not completed evidence.

| Step / current state | Owner | Needs / next action | Completion condition and consequence |
|---|---|---|---|
| R02-D01 - Clear redistribution and declare launch inputs; repository controls delivered, release review open | Product owner and release engineering lead | Apply PolyForm Shield 1.0.0, the reviewed NuGet gate/version-specific overrides and notices. Audit actual native/font/text/model assets and the selected WiX build-tool terms separately. Use runtimeconfig/import inventory to declare launch requirements apart from optional capability assets. | Per-release redistribution/notices and supported runtime/native policy are reviewed. Binary delivery requires supported .NET 10 x64 Desktop Runtime and the observed VC++ x64 prerequisites, not Git/SDK. A passed NuGet gate or successful build does not clear every external asset. |
| R02-D02 - Select Windows installer direction; selected | Release engineering lead | Adopt WiX MSI + Burn, preserve historical NSIS evidence and hand off reviewed/pinned tooling plus a bounded Windows packaging job to R17. No separate WiX feasibility project or mandatory native Linux NSIS trial. | The direction is selected, not installed acceptance. Linux remains preferred wherever feasible; WiX packaging/lifecycle/protection must pass R17-D01/D02/D03. Retiring the old Linux NSIS gate does not waive Windows/runtime/resource gates or introduce Wine. |
| R02-D03 - Establish the independent Windows deployment boundary; blocked approval | Release and security engineering leads, Windows lab owner | Allocate an approved disposable Windows 11 x64 lab. Validate deployment parent/version protection, independent activation authority, actual non-elevated app token, protected runtime/native load roots and source identities; coordinate worker identity requirements with containment. | Actual identity/effective-access/link/replacement tests prove the boundary or leave affected capabilities disabled. An ACL request or user-writable staging is not protection. Missing workers are recorded as untested and must pass their later gates; do not modify an existing user installation to obtain evidence. |
| R17-D01 - Implement WiX source and binary delivery; outstanding | Release and application engineering leads | Implement a thin MSI/Burn slice: fresh install, upgrade, repair, interrupted/failed operations, recovery and uninstall. Use a branded standard bootstrapper first; review its closure and required launch prerequisites independently of optional capability chains. Turn dedicated source checkout/staging into a reviewed external bootstrap; specify version/activation metadata and data retention. Optional assistance shares the app's consent/refusal/re-entry contract, with no mandatory provider selection. | Both options prove actual protection/unprivileged launch, preserve earlier output/local edits/user data and pass lifecycle/runtime trials. Binary users never build/restore; source provenance stays local. Skipped/failed/successful assistance is re-probed and reconciled by app-led setup without duplicate installs or removing unowned/pre-existing dependencies. Users may decline every optional provider and retain supported deterministic commands; startup remains separate consent. |
| R17-D02 - Integrate Linux-first builds and Windows WiX packaging; outstanding | Release engineering lead | After applicable D01/D03 gates and the selected D02 direction, define protected maintainer-approved jobs: portable build/test/cross-publish and release metadata/publication on Linux wherever feasible, explicit Windows MSI/Burn assembly with reviewed pinned tools. Verify payload digests/source/provenance across jobs; PR checks have no release-write identity. Production uses protected version tags, an early already-published check, per-version serialisation and a final non-overwriting recheck. Reinspect contents and justify symbols/import libraries. | Final setup/MSI/payload hashes, runtime/architecture metadata, SBOM/notices and job OS/tool/source provenance identify one approved revision; packaging never silently rebuilds/relabels transferred bytes. Matching published versions are no-ops; conflicts/uncertain lookups fail visibly; branch builds are not production releases. Public acquisition needs no account/token/Git/SDK. Unknown Publisher/SmartScreen remains explicit. Each assembly has its own digest; replacement is external, not an updater. |
| R17-D03 - Accept installed final bytes on Windows; blocked later integration/lab | Windows test lead with release and security leads | Use R17-D01/D02 candidates after R03/R09/R10/R11/R16 and the reference hardware/environment are ready. Test a clean runtime-only profile, missing/wrong prerequisites, UAC/SmartScreen, effective protection, native loading, setup/tray/privacy, embedded skills/workers, logon opt-in/removal, uninstall/data retention and external replacement recovery. | Attach actual receipts to the exact final setup/payload hashes and observed runtime/native versions. All required installed/resource/worker gates pass with no production-installation mutation during trials; only then sign off D-005 and feed R19 release acceptance. Keep drafts/unreleased status until then; R18 maintenance remains notify-only. |

Immediate handoff: product/release owners complete D01's release-specific
review; release engineering takes the selected D02 direction into R17's thin
MSI/Burn implementation; release/security and the lab owner arrange D03
approval. Production design can proceed in parallel, but those assignments
and the later integrated Windows evidence cannot be replaced by passing
fixture tests or merging this proof.

### Read-Only Integration and Declarative Authoring

| ID and work package | Starting state | Priority/value | Needs | Completion condition |
|---|---|---|---|---|
| R20 - Deliver one admitted read-only MCP integration and bundled integration skill | Outstanding; connector/task choice still open | P2 - first safe external-source value | R06, R10, R11, R19 | Select one supported tool/task; implement host-owned setup/sign-in/credential references, connection lifecycle, capability discovery and exact identity/policy mapping. Pin and enable the bundled read-only skill; enforce bounded provenance, fresh source permission and per-result model egress. Pass a controllable MCP-server suite plus the same tests on the real connector. No arbitrary installation, writes or generic call-anything escape hatch. |
| R21 - Add explicit shared-source skill discovery and enablement | Only roaming directory foundation I07 | P2 - reuse existing skills safely | R05, R06, R10, R11, R19 | Discover explicitly selected profile roots read-only; validate compatibility, source-qualified identity and immutable revision/dependencies. Expose bounded list/inspect/review and separately confirmed enable/disable/invoke with visible unavailable reasons. Copy edits only to the Kora store; never change shared roots, shadow native safety commands or treat imported instructions as authority. Combined R20/R21 evidence closes Slice B. |
| R22 - Deliver voice/UI declarative skill authoring | Outstanding | P2 - create useful workflows without executable imports | R05, R10, R12, R14, R20, R21 | Implement Builder clarification/shared draft, exact diff/capability summary, schema/dependency checks and data-only simulated examples. Stage/save/restore/delete only owned revisions with exact confirmation; save to the Kora user store and confirm enablement separately. Add bounded skill-revision file selection/read, not general filesystem access. Invoke only admitted tools under normal grants/egress; save/tests/enablement confer no execution permission. No compilation, external test/build commands, Git writes or application-code modification. |
| R23 - Accept Slice B/C and initial-release regression | Proof outstanding | P0 - integration/authoring must not weaken the core | R19, R20, R21, R22 | Record actual connector/account/access-revocation, hostile-source, source-revision, draft/save/enable and simulated-test results. Repeat affected A0-A4/privacy/cancellation/egress/grant tests and update capability/reference/user documentation to exact delivered availability. The initial A/B/C scope is complete only after these gates, not after a catalogue or authoring UI exists. |

## Optional and Deferred Work

These packages are ordered after core value by priority, not added to the mandatory initial-release critical path.
An optional branch can proceed once its stated dependencies pass, without requiring unrelated later packages, but must not displace unresolved core safety work.
Do not advertise any deferred capability solely because an interface/schema is documented.

| ID and work package | State/priority | Needs | Separate delivery gate |
|---|---|---|---|
| R24 - Local frequent-speaker learning and enrolled verification | Optional; P3 | R03, R04, R05, R09, R10, R15 | Separate learning consent and verifier enrollment; protected per-SID/device storage, minimization/reset/delete, drift/playback/predominant-speaker tests and verifier FAR/FRR/anti-spoof/secure-OS proof. Learning is personalization, never identity/authority; missing either never blocks baseline voice. Close D-006 only for the advertised capability. |
| R25 - Optional speech captions and richer browser/static HTML/diagram results | Optional/separately gated; P3; native documentation Markdown already exists | R05, R08, R09, R14, R15 | Exact approved playback text/lifecycle for captions; immutable content, renderer isolation, disabled host bridges/active content, approved finite assets/navigation and resource limits for rich viewers. Provide truthful bounded text/source fallback. Viewing is not scraping, form automation or execution. |
| R26 - File/screen/image context and knowledge retrieval/indexing | Deferred; P3 | R04, R05, R06, R07, R08, R12; connector-backed retrieval also R20 | Admit specific source/region/account scopes, explicit capture, permission/secret/provenance controls, freshness/revocation/deletion and bounded citations. No ambient collection or blanket enterprise cache; models cannot choose arbitrary paths or silently reuse context. |
| R27 - General executable imports and standalone application execution | Deferred; P3 | R01, R02, R05, R10, R11, R12, R13, R21 | Resolve standalone-binary rollback policy; prove complete dependency discovery and immutable folder snapshots, registered execution profiles, real OS containment and content-bound applicability/revocation. Do not extend fixed bundled scripts into arbitrary shell strings or user-supplied executable authority. |
| R28 - Write-capable connectors, repository/Git or broader desktop automation | Deferred; P3 | R05, R08, R12, R13, R20, R26 | Add explicit versioned tools and per-domain policy/resource/identity/recovery proofs. Revalidate external changes and uncertain writes; no self-modification, model-selected executable handlers or silent automatic write retries. Declarative authoring is not authorization for these capabilities. |
| R29 - Kora MCP server, install-capable updates, custom executable/render extensions or intra-session parallel agents | Deferred; P3; distinct proposals, not one combined release | R02, R05, R08, R11, R13, R17, R23 | Require a recorded scope/decision and dedicated proofs per proposal: authenticated per-client scopes, future trusted signed update roots/activation path, extension identity/containment, renderer isolation or isolated subtask budgets/leases. Initial notify-only maintenance, fixed renderers and one-task-per-session remain unchanged until that proposal is accepted. |

## Acceptance Checkpoint Mapping

Implementing a foundation earlier does not accept its eventual milestone.
Checkpoint sign-off remains A0, then A1, then A2, then A3, then A4; later testing repeats earlier controls.

| Checkpoint | Current assessment | Roadmap evidence required to close it |
|---|---|---|
| Platform/Gate 0 | Shared projects/CI partial; I18 Node witness retained and scoped source-built .NET RT1 passes; hook-only FAIL, released NuGet byte parity blocked, RT2/MG1/PV1 open | Preserve exact RT1 profile or repeat for changed bytes; R02-RT2 and relevant R02-PV1, then R03/R04/R05/R08/R11/R17 actual integration/OS/provider/deployment evidence; MG1 for model-assisted management |
| A0 deterministic shell | Partial shell/setup/transcription/TTS; no explicit PTT, clipboard, controlled remote path or streamed answer | R03-R08 foundation/vertical-slice evidence; no dependence on model-assisted management; native setup/recovery and cancellation measured |
| A1 voice-first activation | Grammar proof, not production wake or complete lock/event policy | R02/R03/R09 actual wake, endpointing, privacy and interruption trials |
| A2 local-first answering | Bootstrap candidate and R02 identity/licence/unavailable-path evidence; no successful real-model, full clipboard/offline or floor qualification | R02-L1-L5 candidate/floor disposition and L6 handoff; R06/R07/R08 actual local adapter/clipboard/streaming conformance and independently network-blocked host trials. D-003 and applicable D-007 evidence remain open until owner-reviewed passes; harness tests/merge do not close them |
| A3 sessions/work/controls | Bootstrap ledger, named approvals and lock/proposals only | R04/R05/R06/R10/R11/R12/R13/R16 durability, grants, registry, management/concurrency and protected-control evidence |
| A4 workspace/interaction | Compact/native windows and output preferences partial | R09/R14/R15/R18 shared interaction, workspace/history/detail, call and proactive/maintenance evidence; optional R24/R25 only if advertised |
| Complete Slice A | Not accepted | R19 compiles the preceding sign-offs and required installed-app evidence |
| B read-only integration/skills | Outstanding | R20/R21/R23 evidence; controllable server tests alone do not accept a real connector |
| C declarative authoring | Outstanding | R22/R23 evidence; save, enable and execution remain separate decisions |

## Capability Coverage and Keeping the Roadmap Current

The [canonical catalogue](Internal_Model_Tools.md) remains the owner of tool IDs and lanes; this roadmap introduces none.
Track each admitted descriptor and its tests against the package that implements it:

| Capability family | Primary work packages |
|---|---|
| Discovery/application/readiness/runtime; deterministic host-only setup/recovery | R03, R06, R10, R16, R17 |
| Session lifecycle/history/search/artifacts, cross-source evidence query and grounded work/status/queue/routing | R04, R06, R12, R13, R14 |
| Structured questions/presentation/details/navigation/speech, approvals/grants and receipt/audit evidence/export | R04, R05, R09, R14, R15; optional R24/R25 |
| Typed configuration, call feedback/origin/reusable-grant controls | R10, R15 |
| Explicit context and destination egress | R07, R08; deferred R26 |
| Bundled skills/execution/computer controls | R11, R16; deferred R27/R28 |
| Dual file/database logging, dedicated audit table and storage recovery | R04 |
| Evidence/diagnostic viewers | R14; optional R25 |
| Host-only maintenance/proactive events and read-only maintenance snapshots | R17, R18; deferred update authority in R29 |
| Connector discovery/configuration/tools, shared skills and declarative authoring | R20, R21, R22 |
| Voice-profile workflows, knowledge and separately gated future integrations | R24, R26, R28, R29 |

For each delivery, update this inventory, the [decision register](Decision_Register.md), the [technical reference](Tool_And_Skill_Reference.md), the [user guide](../docs/tools-and-built-in-skills.md) and actual advertised schemas together.
Record source/test/provider/hardware evidence and the exact acceptance result, not just a merged PR.
Remove proposed labels only for the admitted behavior actually delivered; preserve unavailable/unknown states and the distinction between current host equivalents and model tools.
Keep dependencies explicit and re-run affected earlier gates when a later capability changes shared authority, storage, audio, egress or resource coordination.
