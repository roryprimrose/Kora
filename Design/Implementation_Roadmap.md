# Implementation Status and Delivery Roadmap

Status: source-backed implementation inventory and proposed delivery order;
R01 policy reconciliation approved; R02 Windows storage direction and
local-inference/runtime partial outcomes and distribution delivery plan recorded;
standard profile-storage integration/runtime admission, real inference qualification, production
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

R17-D01's source **preview/build/staging/verification** slice was implemented
on 2026-10-06 in isolated `agents/kora-r17-source-bootstrap-20261006`, based on
`d1fc77f8083985c5d86ed0ef3496ac68c4a150ed`. The exact-base canonical source
publish and no-build rerun passed with SDK 10.0.401; root Release, all 1,685
tests and portable 100% line/branch coverage passed. See the
[source interface and verification snapshot](Distribution_And_Updates.md#source-bootstrap).
This records local tooling/evidence, not source activation, official
publication, installed protection or D-005 sign-off. CI repair, R04 composition
and R05 foundation remain independently owned; no sibling changes were merged.

Current-assessment reconciliation on 2026-10-06 after #34/#40: held PTT and
cross-build ownership are implemented, with bounded production-host voice,
x64 handoff and Ollama simple/long-answer/cancellation observations recorded
in the [deferred-validation register](Deferred_Validation.md#2026-10-05-bounded-local-inference-result).
Startup enables gated voice readiness, not ambient capture. The separately
approved [released-profile MG1 proof](../experiments/r02-dotnet-management-proof/EVIDENCE.md)
passes; historical source-built RT1 hashes/blockers remain distinct.
These narrow results do not accept A0/A2/Gate 0, native privacy/permission
polling, CPU-floor/offline qualification, RT2/PV1 or production admission.
Original-baseline and rebase receipts below remain historical.

R04 implementation started on 2026-10-06 in the isolated
`agents/kora-r04-durable-storage-implementation` branch, based on
`d3d2296387e30d2a55850b71d4b8d80be7594ffd` (also its initial `origin/main`
merge base). The worktree was clean. Other inference/design and installer
worktrees were not merged, modified, or used as test evidence.
See [R04 foundation delivery](#r04-foundation-delivery) for the delivered,
partial and blocked boundaries; this is not completion of R04.

On 2026-10-06 the owner approved a local R04 checkpoint and rebase onto
current main. The checkpoint was rebased as `7473902` on
`3e8558f` (WiX/versioning #35), including startup greeting #32, RT1 #33 and
proof/lifecycle #34. The single catalogue conflict was reconciled by
preserving main's WiX/VC++ rows and R04's unavailable-content/native-admission
rows. The original-baseline receipts below remain historical; the
[rebased receipt](#r04-rebase-validation-receipt---2026-10-06) records fresh
combined-source validation. No sibling worktree was modified or unpublished
work imported, and no push or PR was authorized by this rebase.

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
The original separate R02 local-inference follow-up added actual
missing-endpoint/unavailable-path observations and public
identity/licence/download metadata, not successful real model generations.
It performed no reference-floor or independently network-blocked model trials.
The later bounded production-host trial adds real setup, simple/long answers
and cancellation, not reference-floor or independently blocked offline proof;
see the [recorded result](Deferred_Validation.md#2026-10-05-bounded-local-inference-result).
The R02 runtime branch subsequently exercised an actual pinned SDK/runtime
against synthetic loopback providers, not a real hosted model/account.
Its partial results and explicit failures/blockers are recorded below.
Test-project names, mocks, helper truth tables, configured coverage thresholds, and publish jobs are not substitutes for those trials.
An implemented feature may therefore still have outstanding release proof.
The [acceptance criteria](Acceptance_Criteria.md), not this inventory, determine release readiness.

## R14 Native Passive Detail Slice - 2026-10-06

Implemented on isolated `agents/kora-r14-native-details-20261006`, based on
`c5dffabf8f4fa767147be06dd8b296238ea97da0` (main including #41-46 and #44's
detail design). The source checkout and completed sibling trees were not
edited, built, restored or reused. This remains an uncommitted handoff; no
commit, push, PR, merge or native real-effect trial was authorized.

**Delivered narrow path:** Documentation > selected embedded page > native
Open details. The exact page opens in an immutable reference/revision-bound
native passive viewer with provenance/sensitivity/digest chrome, source,
search and Unicode exact-source copy. Same-reference opens activate the
existing viewer; new revisions are distinct and conflicting same-reference
content is rejected. The guide, grants and viewer share one bounded local
Markdig/Avalonia pipeline. Native-text-v1 bounds are 256 KiB UTF-8 / 512 blocks /
4,096 nodes / depth 32 / 8 open viewers / 256 search characters; no silent
truncation or unbounded rendered tree.

**Stable handoff:** Core owns portable immutable classification/reference
validation; Application owns deduplication, search, generation-bound state and
privacy cleanup; desktop owns native presentation and explicit routing.
Finalized-response admission requires existing host session/request/task IDs.
No persisted interaction/question/grant schema, authority composition or
`MainViewModel` action dispatcher was changed. Embedded page references remain
process-local and explicitly not durable session authority.

**Still outstanding:** Sessions list/full conversation/history, work/queue and
Evidence workspace, durable response/artifact access/revocation, automatic
offers and verbal question targeting, shared native approvals, skill/script/diff
review, rich clipboard serializer, syntax grammars, HTML/browser/diagrams/assets
and scoped export. Closing, viewing and copying cannot approve/cancel/delete,
mark Done or refresh session activity. The compact response, pin/timeout and
privacy holds remain unchanged.

Validation is recorded with the final local receipt below; native visual,
screen-reader, contrast/text-scale, DPI, multimonitor, clipboard-platform and
live Kora/audio observations remain **pending separate scoped approval**.
See the [exact delivered profile](Information_Display.md#delivered-native-profile---2026-10-06)
and [window boundary](UI_Workspace_And_Windows.md#delivered-passive-details-boundary).
This does not complete R14 or accept A4.

### R14 Local Validation Receipt

Verified on 2026-10-07 local time in the same isolated uncommitted tree:

| Check | Actual result |
|---|---|
| Locked restore and root Release | Passed; 0 warnings / 0 errors |
| Core suite | 365 passed, 0 skipped |
| Application suite | 1,071 passed, 0 skipped |
| Windows integration suite | 559 passed, 0 skipped |
| Latest-only portable coverage | 100% line / 100% branch, Core + Application; only the fresh `native-details-qualified` pair aggregated |
| Native-detail targeted tests | 64 passed; actual parser/native-control/fake controller and keyboard/XAML contract evidence, not native visual acceptance |
| Dependency licences | Passed; existing notices current, no runtime package/grammar/renderer acquisition |
| Static win-x64 / win-x86 publish and payload checks | Passed; 201 / 197 exact files with reviewed licence texts. Existing Markdig/Avalonia present; no browser/diagram/generated-content or test runtime assets introduced |
| Diff whitespace | Passed |

The payload manifests explicitly label the source as
`c5dffabf8f4fa767147be06dd8b296238ea97da0+uncommitted-native-details`,
not a committed exact-revision release or official publication.
Portable TRX/coverage and Windows TRX are retained under the ignored
`.net-test-artifacts/native-details-qualified` directory; payloads/licence
evidence are under ignored `artifacts/native-details-*` directories.
An initial broad asset scan also matched the scanner's reviewed HTML licence
texts; the corrected runtime scan excludes only the canonical licence-text
directories, not executable application assets.

Tests cover immutable reference/revision conflicts, bounded capacity,
generation/late-render rejection, privacy cleanup, complete UTF-8 source,
native selection versus whole-source copy, disclosure/access races and platform
failure through a fake clipboard. Parser cases include exact/exceeded
byte/block/node/depth limits, hostile HTML/images/URI/diagram content, unknown
nodes, unavailable renderer, definition-only empty projection, and repeated
reference-link expansion bounded during projection building.
The existing host shutdown test fake was extended to hold final speech
completion, deterministically verifying that a real host exit still waits for
response/approval work; no `MainViewModel` production dispatch was edited.

Adding the existing desktop project as a Windows-test reference updated only
that test lock's transitive graph. The installer theme test uses its explicitly
qualified existing theme type to avoid the resulting namespace ambiguity;
installer/release/bootstrap production files were not modified.

No application/audio launch, OS/account/privileged operation, real clipboard
write, installation, sibling merge or publication occurred. The native visual,
assistive-technology/DPI/multimonitor and real platform-copy gates above remain
pending.

## Delivered and Partial Implementation

The identifiers in this table are inventory references, not new capability or model-tool IDs.

| Ref | Status | What exists and its boundary | Source and test evidence |
|---|---|---|---|
| I01 | Delivered bootstrap; deployment proof outstanding | .NET 10/Avalonia Windows composition with portable Core/Application projects and first-party Windows services. No Copilot SDK adapter or MCP runtime is composed. | [Composition](../src/Kora/Program.cs), [desktop project](../src/Kora/Kora.csproj), [dependency versions](../Directory.Packages.props) |
| I02 | Delivered bootstrap | All 20 built-in actions, normalized whole-phrase routing, optional configured-name prefix, and C# dispatch. Exact routes take precedence over inference. This is not registration of the proposed tools or execution of skill packages. | [Actions](../src/Kora.Core/Commands/BuiltInAction.cs), [catalogue](../src/Kora.Core/Commands/BuiltInCommandCatalog.cs), [router](../src/Kora.Core/Commands/BuiltInCommandRouter.cs), [router tests](../tests/Kora.Core.UnitTests/Commands/BuiltInCommandRouterTests.cs), [host dispatch/tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.cs) |
| I03 | Delivered bootstrap; A4 partial | Presence, Settings, compact response, embedded documentation and grant windows; themes, assistant naming, native controls, response pin/topmost/timeout and device-local placement/preferences. The tray exposes Show, Settings, Documentation and Exit with single/double-click behavior. It has no direct microphone-selection/listening-recovery menu, Sessions workspace or full conversation history. | [Desktop composition](../src/Kora/App.axaml.cs), [tray](../src/Kora/SystemTrayController.cs), [response surface](../src/Kora/ResponseWindow.axaml), [application view model](../src/Kora.Application/ViewModels/MainViewModel.cs), [embedded-guide tests](../tests/Kora.Application.UnitTests/Documentation/EmbeddedUserDocumentationProviderTests.cs) |
| I04 | Implemented PTT/ownership foundation; native acceptance partial | Microphone/output enumeration, device preferences, consent/readiness and Enable/Disable listening are wired. Startup may enable readiness after fresh gates; capture remains closed until held PTT. Settings supports mouse/Space/Enter PTT with release/focus-loss closure. A per-SID global owner coordinates cross-build activation/takeover/return; only Owner composes services. Privacy observation/closure is implemented, but complete native lock/disconnect/suspend/device and permission-polling acceptance remains unproved. | [Application orchestration](../src/Kora.Application/ViewModels/MainViewModel.cs), [PTT controls](../src/Kora/SettingsWindow.axaml.cs), [owner coordinator](../src/Kora.Windows/Coordination/WindowsInstanceCoordinator.cs), [Owner-only composition](../src/Kora/Program.cs), [bounded R03 evidence and remaining trials](Deferred_Validation.md#r03-windows-ownership-and-audio-privacy) |
| I05 | Partial voice proof | Windows phrase grammar and assistant-name-prefixed dictation produce local transcripts during an explicit activated capture, not automatically at startup. Held PTT, bounded capture and stale-generation rejection are implemented. This is not the designed wake-only ambient pipeline; production wake/pre-roll/acoustic quality and complete packaged-native acceptance remain outstanding. | [Recognition service](../src/Kora.Windows/Audio/WindowsVoiceRecognitionService.cs), [activation/privacy orchestration](../src/Kora.Application/ViewModels/MainViewModel.VoicePrivacy.cs), [audio stream](../src/Kora.Windows/Audio/BlockingAudioStream.cs), [recognition tests](../tests/Kora.Windows.IntegrationTests/Audio/WindowsVoiceRecognitionServiceTests.cs) |
| I06 | Delivered bootstrap; speech/privacy proof outstanding | Windows TTS plus separately offered optional Kokoro assets/provider; local installation/hash checks, voice/device selection, preview, playback stop and visual fallback for unavailable/muted output. Basic recognition filtering during Kora speech is implemented, not proven acoustic echo/playback rejection or optional owner-aware privacy. | [Speech service](../src/Kora.Windows/Audio/WindowsTextToSpeechService.cs), [Kokoro](../src/Kora.Windows/Audio/KokoroTextToSpeechProvider.cs), [application coordination](../src/Kora.Application/ViewModels/MainViewModel.cs), [speech tests](../tests/Kora.Windows.IntegrationTests/Audio/WindowsTextToSpeechServiceTests.cs), [Kokoro tests](../tests/Kora.Windows.IntegrationTests/Audio/KokoroTextToSpeechProviderTests.cs) |
| I07 | Delivered bootstrap and first bounded durable milestone; full R04 partial | Bootstrap setup ledger remains separate. Standalone exact local version-query input composes private standard-SQLite intent/dispatch/evidence/terminal records and Interrupted/Unknown no-replay startup recovery. Log/audit/span/link due dates are independent; automatic database pruning, history/session lifecycle and managed copy/deletion acceptance remain open. | [Storage probe](../src/Kora.Core/Dependencies/StorageDependencyProbe.cs), [actual task store](../src/Kora.Windows/Storage/WindowsSqliteHostTaskStore.cs), [composed fixtures](../tests/Kora.Windows.IntegrationTests/Storage/DurableStorageCompositionTests.cs), [current receipt](#composed-milestone-validation-receipt) |
| I08 | Delivered bootstrap | Capability readiness and separate consented setup orchestration; PowerShell 7.4+ probing/install/re-probe is independent of local inference. Open Setup installs nothing. No general PowerShell task worker or executable grant is supplied by readiness. | [Application setup](../src/Kora.Application/ViewModels/MainViewModel.cs), [PowerShell setup/tests](../tests/Kora.Windows.IntegrationTests/Dependencies/WindowsPowerShellSetupServiceTests.cs), [dependency bootstrap](../src/Kora.Core/Dependencies/DependencyBootstrapper.cs) |
| I09 | Delivered bootstrap; bounded production evidence, D-003 open | Consented per-user Ollama 0.35.1 setup and pinned `qwen3:1.7b` download; loopback runtime/model/digest checks, no automatic cloud fallback. R02 identity/licence/unavailable-path evidence and 31 deterministic harness tests remain distinct from the later actual production-host setup, simple/long answers and active model/speech cancellation without stale completion. CPU-floor quality, latency/resource/context budgets, repeated race/computation-cessation timing, installer provisioning and independently network-blocked offline qualification remain open. | [Ollama setup](../src/Kora.Windows/Dependencies/WindowsOllamaSetupService.cs), [inference probe](../src/Kora.Windows/Dependencies/LocalInferenceDependencyProbe.cs), [setup tests](../tests/Kora.Windows.IntegrationTests/Dependencies/WindowsOllamaSetupServiceTests.cs), [probe tests](../tests/Kora.Windows.IntegrationTests/Dependencies/LocalInferenceDependencyProbeTests.cs), [bounded production result](Deferred_Validation.md#2026-10-05-bounded-local-inference-result), [R02 technical plan](Local_Inference.md) |
| I10 | Partial model interaction | Unmatched requests use one local reasoning operation with bounded status context and exactly one answer, question, action or grant-change response. Request length is 4,096 characters; inference deadline is two minutes; question prompts/options and three follow-ups are bounded. Generation is buffered, not streamed. Busy guards reject competing requests; handler results are not returned for tool-loop continuation. | [Reasoner](../src/Kora.Windows/Dependencies/WindowsOllamaReasoner.cs), [reasoner tests](../tests/Kora.Windows.IntegrationTests/Dependencies/WindowsOllamaReasonerTests.cs), [application/tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.cs), [current protocol](Internal_Model_Tools.md#implemented-bootstrap-surface) |
| I11 | Partial authorization and questions | Native model-action Once/Session/Always approval, bounded clarification choices, grant Add/Remove/Move/revoke and a grant document/editor. Session is an in-memory action-name set, cleared on Kora's lock path; Always is JSON action-name preference storage. Neither is the content/invocation-bound grant store or durable Kora work-session scope. | [Application/tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.cs), [approval preferences](../src/Kora.Application/Configuration/LocalModelApprovalPreferences.cs), [response controls](../src/Kora/ResponseWindow.axaml) |
| I12 | Partial computer controls | Exact lock stops owned audio then calls the Windows lock API without the model-action gate; model-proposed lock uses that gate. API acceptance is not independent observation of lock completion. Shutdown/restart create inspectable, cancellable proposals only; no OS power request is sent. No embedded lock/power scripts or all-session power coordination are implemented. | [Host action handlers](../src/Kora.Application/ViewModels/MainViewModel.cs), [Windows session controller](../src/Kora.Windows/Session/WindowsSessionController.cs), [application tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.cs), [session-helper tests](../tests/Kora.Windows.IntegrationTests/Session/WindowsSessionControllerTests.cs) |
| I13 | Partial lifecycle/work controls | Show/hide/exit/current-app restart, stop speaking and cancellation of current setup/inference. Cross-build ownership handoff/return and controlled shutdown are implemented, with bounded x64 trials; x86 installed-runtime and full failure/privacy acceptance remain open. Status/progress describe setup/activity, not a general authoritative work ledger. The R04 no-replay store/recovery foundation is not composed into current work; general queues and integrated crash-safe work recovery remain outstanding. | [Host controls](../src/Kora.Application/ViewModels/MainViewModel.cs), [process controller](../src/Kora/DesktopApplicationProcessController.cs), [owner coordinator](../src/Kora.Windows/Coordination/WindowsInstanceCoordinator.cs), [bounded lifecycle evidence](../docs/voice-and-audio.md#r03-validation-evidence), [application tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.cs) |
| I14 | Partial call support | Call-state contracts, preferences and application output/activation policy exist with fake-state tests. The composed Windows service reports Unavailable and supplies no automatic detector. Manual call state, the new reusable-grant/origin policy and genuine provider/signal integration are outstanding. | [Composed service](../src/Kora/Program.cs), [unavailable adapter](../src/Kora.Windows/Communication/UnavailableCallStateService.cs), [call preferences](../src/Kora.Application/Configuration/LocalCallAwarePreferences.cs), [application tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.cs) |
| I15 | Delivered bootstrap; security audit partial | Structured daily JSON logging, a content-minimizing audit bridge and bounded daily-log reader/UI. These are not encrypted session events, tamper-evident operation receipts or model-facing diagnostics tools. | [Logger configuration](../src/Kora/Program.cs), [audit bridge](../src/Kora.Application/Auditing/LoggerSecurityAuditLog.cs), [log reader](../src/Kora.Application/Diagnostics/LocalApplicationLogReader.cs), [reader tests](../tests/Kora.Application.UnitTests/Diagnostics/LocalApplicationLogReaderTests.cs) |
| I16 | Delivered binary installer and release configuration; installed/production acceptance outstanding | WiX 7 MSI/custom Avalonia Burn packages win-x64 with scoped startup, completion launch, read-only preflight and required/optional dependency handling. Linux builds/cross-publishes; Windows packages exact transferred bytes. Shared GitVersion policy and beta/stable GitHub publication with notes/hashes/provenance are configured, not evidence of an actual published release. NSIS-only executable paths are retired; historical receipts and remaining source/native checks are retained. Source bootstrap, silent related-bundle upgrades, beta numeric upgrade ordering, external-asset qualification and protected/runtime-only/resource acceptance remain open; win-x86 output is not x86 installer acceptance. | [Installer](../installer/README.md), [CI workflow](../.github/workflows/ci.yml), [retained R02 checks/results](../experiments/r02-distribution-proof/README.md), [canonical outcomes](Distribution_And_Updates.md#r02-distribution-outcomes-and-direction), [follow-up delivery plan](#r02-distribution-follow-up-and-r17-delivery) |
| I17 | Experimental containment evidence; production admission blocked | Fixed AppContainer/Job Object/PowerShell proof: 63/71 OS assertions met; protected stand-ins/credential denied, descendant identity/lifetime observed, lost/malformed receipts remain Unknown. Eight network-denial assertions unproven; executable dependency allowlisting and normal-host deployment protection not established. No production worker or bundled catalogue. | [Measured snapshot](../experiments/r02-containment-proof/evidence/README.md), [canonical outcomes](Security_Data_Flows.md#r02-windows-containment-outcomes), [continuation gates](Security_Data_Flows.md#windows-containment-continuation-gates) |
| I18 | Experimental Node/.NET RT1, bounded RT2 observations and released MG1; production Gate 0 incomplete | Historical Node/source-built RT1 unchanged; 45/45 RT1 controls with rejected hook-only FAIL. RT2 final source reproduction and staged controls pass; 20/20 tests plus two locale contracts pass, but native helpers/transient writes and observer limits keep all-path admission BLOCKED. Separately approved released SDK 1.0.16 / unchanged native 1.0.90 MG1 repeats 45 controls, 22 host and 16 actual runtime cases: complete bytes, stalled-ack dispatch deadline, monotonic admission/races/no retry, held topology and Unknown quarantine pass. MG1's initial local source reproduction blocker retained; no artifact equivalence claimed. PV1 and production adapter/scheduler remain gated. | [Historical Node evidence](../experiments/r02-runtime-proof/evidence/results.json), [historical .NET RT1](../experiments/r02-dotnet-control-proof/README.md), [RT2 evidence/handoffs](../experiments/r02-runtime-lifecycle-proof/EVIDENCE.md), [MG1 fixture/disposition](../experiments/r02-dotnet-management-proof/evidence/disposition.json), [technical outcome](Runtime_Provider_Feasibility.md) |

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
| W2 - Resolve fixed-action and dependency mechanism | R02 / Windows and security leads | [Owned W2 fixture](../experiments/r02-w2-dependency-proof/README.md) exercised real ACL/child-policy denials and undeclared code bypasses; exact candidates rejected. Separate embedded helper/entry fixed marker and Unknown/cancellation trials measured. Owner accepts best-effort transitive tracking for bundled/future scripts, user responsibility and all manifest files in review tabs; declared bytes remain exact. Native fixture loading blocked by missing compiler; actual OS controls not run. | Dependency/review policy reconciled under D-013, not strict admission success. Prove the actual fixed-control worker/receipt contract and protected runtime resolution under remaining W1/W3 gates; separately decide a typed broker only if needed. No ambient-shell fallback or implicit script replacement. |
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
| R02 storage/key direction - storage and security leads | Historical page-encryption/DPAPI candidate findings retained; owner-approved D-009 now selects standard SQLite with private supplied-profile permissions and readable-copy disclosure. | Encryption/key/rekey admission superseded, not a current R04 prerequisite |
| Standard storage integration - storage, security and release leads | Existing pinned provider/native closure, actual private ACLs, durable transactions/recovery and ordinary package/licence/loading checks. | Actual bounded task store implemented/tested; host/evidence composition and lifecycle remain partial. Installed loading is separate R17 evidence. |
| R04 durable foundation - storage and application leads | Host identity/state, tracing, evidence envelopes and ordered persistence/recovery contracts now have production source and focused tests. Windows key/artifact primitives are independently testable, not composed content persistence. Native admission, actual SQLite commits/schema/conversion/backup rotation and integrated installed recovery remain required. | Partial implementation; content persistence unavailable; see the explicit R04 delivery/handoff below |
| R12 lifecycle/deletion - storage, security and application leads | Inventory/remove or rewrite managed recoverable copies, prevent late appends, preserve unrelated sessions/independent grants, and integrate source revocation, retention/apply-now/live/unknown-work policy. Disclose exported/provider/forensic limits. | Outstanding; deleting rows/current keys alone cannot satisfy D-009 |

Windows is the only supported product OS. Linux runtime support and local Linux-host cross-build validation are outside this storage proof, not additional gates.
Existing Linux-hosted Windows CI/distribution requirements are unchanged.
Ordinary cross-profile isolation is a trusted Windows boundary for this profile-local architecture; a second-account OS-denial trial is optional, not a routine application/merge gate.
The [profile-boundary contract](Architecture.md#profile-boundary-and-validation-responsibility) requires application integration checks and defines changes that would trigger actual multi-account validation.
The [storage deferred-validation checklist](Deferred_Validation.md#storage-admission-follow-up) makes remaining native/deployment and R04/R12 work actionable without requiring an unlocked console for safe scratch reruns.
Other R02 branches remain independently outstanding; the storage results do not close provider, speech, worker or distribution investigations.

### R04 Foundation Delivery

The first bounded **durable** milestone now composes one actual exact local
version-query request/task with correlated diagnostic/audit evidence,
committed terminal state and interrupted-run recovery on private SQLite.
The owner-approved baseline removes encrypted-native/key/rekey gates.
The [composed continuation](#r04-composed-durable-version-query---2026-10-06)
records its precise source, automated interruption evidence and validation;
it is not completion of R04 or an OS-effect receipt.
The existing `kora.db` setup probe remains the
bootstrap-only `setup_tasks` schema; it is neither the new host store
nor a legacy-content migration. No content, grants, receipts or evidence
are added to that database by R04.

| Boundary | State and source/test evidence | Remaining gate |
|---|---|---|
| Provider/native baseline | Existing pinned Microsoft.Data.Sqlite / SQLitePCLRaw e_sqlite3 is the owner-approved standard route. Historical exact 2.4.0 encrypted-candidate evaluation passed basic tests but was rejected; [D-009](Decision_Register.md#approved-profile-secured-sqlite-baseline---2026-10-06) supersedes codec/key admission. No manifests changed. | Normal licence/servicing/native packaging/loading evidence remains; no encrypted-native replacement or owned source-build is required. |
| Host identity and truthful state | Implemented portable [typed identities](../src/Kora.Core/Hosting/HostId.cs), [host request](../src/Kora.Core/Hosting/HostRequest.cs), [task transition/recovery rules](../src/Kora.Core/Hosting/HostTaskRecord.cs), unknown/exclusive resource descriptors; [contract tests](../tests/Kora.Core.UnitTests/Hosting/HostContractTests.cs) | Session registry/routing, durable question/proposal/grant records and concurrent resource leases are not implemented. Bootstrap requests allocate host IDs, not full Sessions UX. |
| Causal and business correlation | Implemented versioned four-source [host activities](../src/Kora.Core/Diagnostics/HostActivity.cs); request routing and typed audit boundaries; ordinary async child context and explicit deferred links; [trace/isolation/spoof tests](../tests/Kora.Application.UnitTests/Diagnostics/EvidenceLoggerProviderTests.cs) | Runtime/tool/queue/presentation/evidence/retention implementations must add their actual boundaries as delivered; operation completion is not proof of OS effect. |
| Independent diagnostic/audit contracts | Implemented [formatter-independent capture](../src/Kora.Application/Diagnostics/EvidenceLoggerProvider.cs), bounded typed properties/scopes, call-time context, spans/links and gaps; production composes [private SQLite evidence](../src/Kora.Windows/Storage/WindowsSqliteEvidenceSink.cs). [Daily JSON](../src/Kora/FileEvidenceSink.cs) preserves trusted categories/IDs independently. Only internal typed audit state routes audits; file copies/lookalikes confer no authority. Required delivery/capture failures report and propagate after independent sink attempts. | Audit tamper checkpoints/pruning, query-visible expired/gap projections and full evidence UI remain. No authorization is inferred from SQLite, file copies or correlation metadata. |
| Intent, dispatch marker, terminal receipt and recovery | Implemented [coordinator](../src/Kora.Application/Hosting/HostTaskCoordinator.cs), actual [standard SQLite store](../src/Kora.Windows/Storage/WindowsSqliteHostTaskStore.cs), [bounded query runner](../src/Kora.Application/Hosting/DurableVersionQuery.cs) and [startup recovery](../src/Kora.Application/Hosting/DurableHostRecovery.cs). Exact typed/activated-voice version input uses required correlated evidence and revision-checked state/event commits. Intent-only becomes Interrupted; dispatched/no receipt becomes Unknown; no executor/replay callback. | The receipt proves only that this local query returned. General dispatch/grants/session registry, managed copies, broader migration/retention/deletion and installed/power-loss acceptance remain. Existing model/OS routes are not promoted to durable effect executors. |
| Retention semantics | Implemented 30-day diagnostic/daily-file and 90-day audit defaults, audit 30-365 validation, UTC due-date and explicit apply-now calculation; [tests](../tests/Kora.Core.UnitTests/Diagnostics/EvidenceRetentionPolicyTests.cs). Composed SQLite evidence receives independent effective due dates transactionally. | Configured diagnostic schema, preview/apply UI, actual pruning/anchors and query-visible expired segments remain R04/R12 work. Due dates alone are not automatic deletion. |
| Profile/artifact primitives | Actual supplied-root/owner/ACL/reparse checks compose the standard task/evidence partitions. Earlier uncomposed [DPAPI keys](../src/Kora.Windows/Storage/WindowsStorageKeyStore.cs) and [encrypted artifacts](../src/Kora.Windows/Storage/WindowsEncryptedArtifactStore.cs) retain historical scratch evidence. | Database key/rekey/encrypted conversion is no longer required. Standard managed backup/artifact provisioning/composition, their process-interruption and deletion boundaries remain; optional crypto primitives do not establish those workflows. |

Artifact reconciliation is observation-only: at most 256 entries/references,
64 MiB examined bytes and 4 MiB plaintext per artifact. It reports staged,
orphan, missing and corrupt items without deleting or promoting them. These
primitive bounds do not specify the future product's large-artifact policy
and do not waive lifecycle/deletion ownership.

Concrete handoffs:

- **R05:** use host-resolved identity, live matching activity and typed audit
  paths; require successful admitted intent/approval commits before new
  consequential dispatch. Legacy action-name preferences gain no authority.
- **R06:** keep provider labels/trace headers as untrusted observations;
  proposals/results use host IDs and truthful Unknown/Unavailable states.
  Do not expose nonexistent session/evidence tools.
  Runtime documentation and experiment evidence from
  [RT1 PR #33](https://github.com/roryprimrose/Kora/pull/33) are now included
  through main. Its original local receipt is against `d3d2296`; the R04
  rebase validation does not rerun that separate fixture or establish NuGet
  release-byte parity, RT2/MG1/PV1 closure or production runtime admission.
- **R07:** bind approved context/artifacts to host session/request identities;
  do not persist clipboard content until private storage composition and
  source/consent/revocation gates pass.
- **R12:** implement actual lifecycle/deletion, late-append holds, recoverable
  copy inventory and retention checkpoints; no browsing refresh or automatic
  replay. Primitive artifact reconciliation is not deletion acceptance.
- **R17:** package the pinned standard SQLite provider/native closure
  for every offered RID; independently prove installed loading, missing/
  corrupted-native fail-closed behavior, CurrentUser/profile/ACL and recovery.
  Current publishes include the bounded standard-SQLite task/evidence
  composition; the bootstrap `kora.db` remains setup-only.
  The WiX implementation is now included through main's #35, targeting
  Windows 11 `win-x64`; R04's x86 static publish does not add a supported
  installer target or establish D-007 acceptance.
  A future reviewed provider/engine handoff must specify exact managed/native
  versions, source/licences/notices, hashes, ABI/exports, transitive runtime
  closure, protected load paths and initialization requirements. Assets must
  flow through normal locked application publish and the installer's exact-file
  manifest, never machine-local DLL copying. Initialization/migration stays
  host-owned, not installer-executed. Neither current-user deployment nor
  Program Files/elevation alone establishes native/deployment admission.
  The installer pins x64 prerequisites of .NET Desktop/base 10.0.12+ and
  VC++ 14.51.36247.0+; R04 changes none of them. Report any
  additional/minimum requirement to that owner before shared-contract edits.
  Installer lifecycle/protection acceptance remains separate and open; no
  historical sibling POC result substitutes for this branch's validation.

  ### R04 Validation Receipt - 2026-10-06

  This is the original `d3d2296`-baseline receipt, not validation of the
  later rebased source. The recorded NSIS-era negative-suite command is
  historical: main retired that tooling; the maintained payload contracts
  used after the rebase are recorded separately below.

  Execution environment: Windows 10.0.26300, SDK 10.0.401/MSBuild 18.9.11,
  .NET runtime 10.0.12, x64 process. Tested this worktree's uncommitted source
  on the `d3d2296` base, not another session/branch. No application, installer,
  real audio/device, elevation, account or security-policy trial was performed.

  | Validation | Actual result |
  |---|---|
  | Initial no-restore Release build | Failed `NETSDK1004` because the fresh worktree lacked assets; this authorized the locked restore, not a dependency upgrade. |
  | `dotnet restore .\Kora.slnx --locked-mode` | Passed; all seven projects; production manifests/locks unchanged. |
  | `dotnet build .\Kora.slnx --configuration Release --no-restore` | Passed; zero warnings/errors with repository analyzers/warnings-as-errors. Intermediate new-code analyzer and assertion/owned-junction cleanup failures were corrected and full suites rerun. |
  | Core full suite | 269 succeeded, zero failed/skipped. |
  | Application full suite | 748 succeeded, zero failed/skipped. |
  | Windows full suite | 212 succeeded, zero failed/skipped; includes actual CurrentUser DPAPI/ACL owned-scratch checks, not installed-lab acceptance. |
  | CI coverage/report threshold | Passed 100% line (5,241/5,241), 100% branch (2,171/2,171), 100% method (599/599), Core/Application only, selecting only the latest final full-suite report from each project. A latest-only intermediate run caught a redundant null branch after live-context tightening; it was corrected and all three suites rerun. ReportGenerator notes absent generated source files but successfully generates the report; no thresholds/suppressions were weakened. |
  | Actual desktop-adapter synthetic check | Four standalone in-memory assertions passed against the compiled desktop adapter: category-level filtering, admitted warning, rejection of category lookalikes, and authoritative envelope identity/scalar-kind projection. Session-artifact fixture, not an added repository test suite or real file-I/O acceptance. Initial PowerShell compilation attempts failed on forwarded runtime types; using the existing .NET 10 reference pack resolved them without package/tool installation. |
  | `.\eng\Test-DependencyLicenses.ps1` | Passed pinned licence policy and notice parity; publish directories receive `licenses` and `package-notices` as in CI. Existing version-specific OpenTK overrides remain unchanged. |
  | Locked framework-dependent publishes | `win-x64` and `win-x86` passed. Neither was launched/installed. |
  | Existing x64 publish inspector and negative-contract suite | Inspected 200 published files; 9 static checks passed. Raw OpenTK nuspec metadata warnings remain (reviewed version-specific solution-gate overrides exist); static inspection still reports release/installed gates blocked. |
  | Both-RID static PE/native closure | Final-source refresh: x64 has 7 native PE files and 10 declared native-asset entries; x86 has 5 files and 6 entries. Matching native machines and declared assets present; framework declarations inspected. `e_sqlite3.dll` remains bootstrap-only; no encrypted engine is shipped. Hash/import receipt is generated locally. |
  | Whitespace and fixture cleanup | `git diff --check` passed; owned test fixtures removed. Uncommitted work is preserved; no commit/push/PR or worktree removal. |

  Exact full-suite commands (Microsoft.Testing.Platform syntax; no zero-test
  result was accepted):

  ```powershell
  dotnet test --project .\tests\Kora.Core.UnitTests\Kora.Core.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\verified-core --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix core
  dotnet test --project .\tests\Kora.Application.UnitTests\Kora.Application.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\verified-application --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix application
  dotnet test --project .\tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\verified-windows --report-trx
  $core = Get-ChildItem -LiteralPath .net-test-artifacts\verified-core -Filter '*.coverage.cobertura.*.xml' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
  $application = Get-ChildItem -LiteralPath .net-test-artifacts\verified-application -Filter '*.coverage.cobertura.*.xml' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
  dotnet reportgenerator "-reports:$($core.FullName);$($application.FullName)" "-targetdir:.net-test-artifacts\coverage-verified" "-reporttypes:Cobertura;TextSummary" "-assemblyfilters:+Kora.Core;+Kora.Application"
  .\eng\Assert-CodeCoverage.ps1 -ReportPath .net-test-artifacts\coverage-verified\Cobertura.xml -MinimumLine 100 -MinimumBranch 100
  dotnet publish .\src\Kora\Kora.csproj --configuration Release --runtime win-x64 --self-contained false --property:RestoreLockedMode=true --output artifacts\Kora-win-x64
  dotnet publish .\src\Kora\Kora.csproj --configuration Release --runtime win-x86 --self-contained false --property:RestoreLockedMode=true --output artifacts\Kora-win-x86
  .\experiments\r02-distribution-proof\Inspect-Publish.ps1 -Payload .\artifacts\Kora-win-x64 -Revision d3d2296387e30d2a55850b71d4b8d80be7594ffd -EvidenceDirectory .\artifacts\r04-publish-x64-inspection-final-category -BuildOrigin 'Windows SDK 10.0.401; d3d2296 base plus finalized uncommitted R04 worktree changes; static inspection only'
  .\experiments\r02-distribution-proof\Test-PublishContracts.ps1 -Payload .\artifacts\Kora-win-x64 -Inspection .\artifacts\r04-publish-x64-inspection-final-category\payload.json -MakeNsis (Get-Command dotnet).Source -OutputDirectory .\artifacts\r04-publish-contract-tests-final-category
  ```

  The static negative suite resolves its supplied tool path before rejecting
  tampered bytes; an initial nonexistent placeholder failed path resolution.
  The verified run supplies the existing dotnet executable as an **inert path
  only**: tampered-payload rejection occurs before any tool invocation. No NSIS
  installation, compiler execution or setup construction is claimed. The
  historical x64-only inspector was not changed to invent x86 acceptance;
  the additional read-only PE/declared-asset check generated
  `artifacts\r04-native-closure.json` for both RIDs using its existing helpers;
  final managed-assembly hashes were captured after the last publish refresh.
  Disposable negative-suite payload copies were removed after verification;
  inspection/test receipts and the two published payloads were retained.

### R04 Composed Durable Version Query - 2026-10-06

This continuation implements the **first bounded composed milestone**, not
complete R04/D-009 acceptance. It runs solely in the collision-checked isolated
`agents/kora-r04-durable-composition-20261006` worktree, created clean from
`origin/main` at `d1fc77f8083985c5d86ed0ef3496ac68c4a150ed` after #39/#40.
The source checkout remained on `feature/personalized-startup-greeting`
`a7bbc04`; no edits/builds/restores ran there. All deliverables remain
uncommitted; publish manifests identify the base HEAD, not a committed release.

**Admitted scope:** standalone exact typed or activated-voice `ShowVersion`
requests with no pending question/grant/action-approval interaction. The
application pins the matched command before asynchronous persistence, rechecks
current host/privacy/interaction eligibility before dispatch, and never
redirects a stale version request into model interpretation. The callback
preserves presentation synchronization context. Subsequent optional speech is
outside the query receipt. Model suggestions and other bootstrap/OS routes
retain their existing behavior and are not promoted to durable effect executors.

[Program](../src/Kora/Program.cs) composes the actual
[task store](../src/Kora.Windows/Storage/WindowsSqliteHostTaskStore.cs) and
[evidence sink](../src/Kora.Windows/Storage/WindowsSqliteEvidenceSink.cs).
`HostStorageV1/host.db` and `EvidenceStorageV1/evidence.db` are distinct private
standard-SQLite partitions; `kora.db/setup_tasks` is unchanged and never
promoted or migrated to content storage. New files have protected current-user
ownership/DACLs before contents are written. Existing missing/corrupt data,
partial initialization, unsupported/altered schemas, reparse paths, permissive
ACLs and missing journals fail without repair, replacement or shared fallback.
Connections select exclusive locking before native hot-journal reads and
`PERSIST`, retaining the privately preowned journal through rollback/reopen;
transactions use FULL synchronization and memory-only temporary storage.
Storage admission/execution is bounded, not an unbounded asynchronous queue.

[DurableVersionQuery](../src/Kora.Application/Hosting/DurableVersionQuery.cs)
orders intent, typed requested audit/diagnostic evidence, dispatch, local query
return, required terminal evidence and terminal receipt. Its Succeeded receipt
establishes only that this bounded local query returned. It is not an OS effect,
API acceptance, abort acknowledgment or process/socket-close receipt.
Required capture/file/database failures propagate after independent delivery
attempts; the daily-file error latch prevents Serilog self-reporting from
silently authorizing admission. Error presentation survives error-logger failure
without swallowing that failure.

Independent log/audit/span/link tables retain formatter-independent typed
envelopes and promoted host/W3C/business fields. Audit request and terminal
events share the host request's business correlation. Ordinary hostless logs
retain explicit capture-owned MissingHostContext/`bootstrap=false` gaps and
null trusted identity columns, not manufactured bootstrap/session authority.
Invalid host-bearing records are never downgraded, and only the trusted typed
audit route writes audit rows.

[DurableHostRecovery](../src/Kora.Application/Hosting/DurableHostRecovery.cs)
admits correlated recovery evidence before committing intent-only Interrupted
or dispatched/unverified Unknown. Fresh recovery traces retain durable host
IDs; there is no executor callback or automatic replay. Startup handles at most
100 incomplete records and fails explicitly if further recovery remains.
Cancellation and disposed-context late callbacks cannot publish success.

First-use greeting, persistent Settings note and version response disclose
readable copies outside the profile boundary, same-user/admin access,
independent diagnostic/audit due dates and **unimplemented database pruning/
deletion**. No transcript, response body, audio, credential or secret is newly
stored. Credentials remain Windows-protected; no database key/rekey is required.

#### Stable R05 Handoff

- Existing `HostId`, `HostRequest`, `HostTaskRecord`, `IHostTaskStore` and
  `HostTaskCoordinator` signatures/transition rules remain unchanged.
  Host/state/correlation metadata still grants no authority.
- New concrete Application services are `DurableVersionQuery.RunAsync(
  RequestOrigin, Func<Task>, CancellationToken)` returning
  `Task<HostTaskRecord>`, and `DurableHostRecovery.RecoverAsync(
  CancellationToken)` returning `Task<IReadOnlyList<HostTaskRecord>>`.
  The query callback is admitted application-owned local-query code, not a
  general tool/effect executor or an R05 authorization gateway.
- Windows adds `WindowsSqliteEvidenceSink(IApplicationDataPaths,
  EvidenceRetentionPolicy? = null, TimeProvider? = null)`, `Initialize()` and
  existing `IEvidenceSink` writes. The task implementation adds concrete
  `InitializeAsync(CancellationToken)` and bounded `ReadTaskAsync(
  HostId<TaskIdentity>, CancellationToken)`; Core store interfaces are unchanged.
- Four host sources remain `Kora.Core`, `Kora.Application`, `Kora.Windows`,
  `Kora.Desktop`, versioned from the Core assembly; this feature build captures
  **0.1.0.0**, not a hard-coded 1.0.0. Reserved context-gap markers cannot be
  selected by caller properties. W3C/correlation IDs never select a grant.
- Attempts to contact orchestration, R05 and reconciliation via `send_message`
  were rejected by the session tool's process-wide message limit. No sibling
  branch or unpublished changes were imported. This durable handoff records
  the integration boundary; it is not a claim that peer agreement was delivered.

#### Composed Milestone Validation Receipt

Final validation is against this **uncommitted working copy on `d1fc77f`**,
Windows 10.0.26300.0/x64, SDK 10.0.401/runtime 10.0.12, feature version 0.1.0.
It is not a union of earlier/sibling receipts. Initial missing restore assets,
an incorrectly scoped test filter, an absolute-path payload check and related
analyzer/test compilation failures were corrected before these final results.

| Check | Actual final result |
|---|---|
| Root eight-project Release build | **0 warnings, 0 errors**, analyzers enabled |
| Core suite | **307 passed**, 0 failed/skipped |
| Application suite | **964 passed**, 0 failed/skipped |
| Windows suite | **494 passed**, 0 failed/skipped; **116 Storage cases** |
| Total | **1,765 passed**, 0 failed/skipped |
| Latest-only portable coverage | **5,742/5,742 lines; 2,311/2,311 branches; 674/674 methods**, all 100%. Exactly the latest terminal Core/Application reports were merged; no old/sibling coverage was used. |
| Real application/storage composition | Both UI/activated-voice cases pass with the actual runner, audit bridge, provider and both private SQLite stores: intent/dispatch/success, matching host/W3C/business fields and nine completed spans without sink recursion. Actual journal failures before requested/terminal audit admission preserve incomplete state, attempt the independent sink and never return a success receipt. |
| Interruption/no replay | Exact fixture-owned test executables are killed after intent, dispatch and an uncommitted native transaction; Interrupted/Unknown and hot-journal rollback pass. Composed runner-child intent/dispatch kills recover with typed evidence and no execution callback/replay. Only fixture-created child PIDs are terminated. |
| Failure/concurrency bounds | Atomic state/event and span/link rollback, concurrent revisions/sink instances, altered schema/data, corrupt/missing database/journal, partial initialization, ACL/reparse, bounded contention/progress, typed spoofing, context gaps/property/scope/byte limits, cancellation, late-context rejection and logging-error visibility pass. |
| Licences/locked closure | Approved/current. No production dependency/notice changes; Windows test reference/lock adds Application project metadata only, no new package. |
| Version/fake release/payload policy | All existing contracts pass; fake GitHub/owned version fixtures only, no real publication or repository commit. |
| Final locked x64/x86 publishes | Both pass. Exact manifests verify **201/197 files**, including licence texts, for base HEAD `d1fc77f`; payloads contain uncommitted compiled changes. |
| Actual native inspection | x64 **7 PE files/10 declarations**, x86 **5/6**; correct machine, normal imports and exact SHA-256 equality to declared pinned package assets, including `e_sqlite3.dll`. No experimental runtime or fixture payload leaked. Static inspection only, no app/installer launch. |
| Bounded effects / still unsupported | Owned scratch file/database/ACL fixtures, fixture child processes and non-disruptive existing integration tests only. No real user storage migration, live app/audio/model, installer, elevation, lock/power or machine-policy trial; no installed or power-loss certification. |

Final structured test/coverage artifacts are under
`.net-test-artifacts\r04-terminal-core`,
`.net-test-artifacts\r04-terminal-application`,
`.net-test-artifacts\r04-terminal-windows` and
`.net-test-artifacts\r04-terminal-coverage`. Retained publish payloads are
`artifacts\r04-composed-win-x64` and `artifacts\r04-composed-win-x86`.
Native inspection JSON and the read-only two-RID inspector are retained in
the session's `files` artifacts; they do not enter production or the repository.
Owned storage fixtures clean up their exact scratch directories; the deliverable
worktree is deliberately retained for uncommitted handoff.

Reproduction commands (run **only in the verified isolated worktree**):

```powershell
dotnet restore .\Kora.slnx --locked-mode
dotnet build .\Kora.slnx --configuration Release --no-restore
dotnet test --project .\tests\Kora.Core.UnitTests\Kora.Core.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\r04-terminal-core --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix core
dotnet test --project .\tests\Kora.Application.UnitTests\Kora.Application.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\r04-terminal-application --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix application
dotnet test --project .\tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\r04-terminal-windows --report-trx
# Select exactly the fresh Core/Application coverage reports; do not merge historical directories.
dotnet reportgenerator "-reports:<fresh-core-report>;<fresh-application-report>" "-targetdir:.net-test-artifacts\r04-terminal-coverage" "-reporttypes:Cobertura;TextSummary" "-assemblyfilters:+Kora.Core;+Kora.Application"
.\eng\Assert-CodeCoverage.ps1 -ReportPath .net-test-artifacts\r04-terminal-coverage\Cobertura.xml -MinimumLine 100 -MinimumBranch 100
.\eng\Test-DependencyLicenses.ps1
.\eng\Test-BuildVersion.ps1
.\eng\Test-GitHubRelease.ps1
.\eng\Test-InstallerPayloadContracts.ps1
dotnet publish .\src\Kora\Kora.csproj --configuration Release --runtime win-x64 --self-contained false --no-restore --property:RestoreLockedMode=true --output artifacts\r04-composed-win-x64
dotnet publish .\src\Kora\Kora.csproj --configuration Release --runtime win-x86 --self-contained false --no-restore --property:RestoreLockedMode=true --output artifacts\r04-composed-win-x86
```

For each payload, copy the approved `licenses`/`package-notices` directories
from `artifacts\license-compliance`, then run `eng\Test-InstallerPayload.ps1`
with an **absolute** payload path, `-Version 0.1.0`,
`-SourceRevision d1fc77f8083985c5d86ed0ef3496ac68c4a150ed`, first
`-WriteManifest` and then verification. The base revision plus dirty-worktree
qualification is intentional; no commit/push/PR/merge is authorized or performed.

#### Composed Milestone PR Rebase Receipt - 2026-10-06

The user subsequently authorized commit, push, PR creation, squash auto-merge,
and synchronization with main. [PR #45](https://github.com/roryprimrose/Kora/pull/45)
publishes this bounded milestone; it does not close the broader R04 gates.
Initial commit `abe6197d1f0cca1744836f3dde012506cc0a6fa2` matched all 33
hashed delivery files. It was cleanly rebased onto main
`d6545a483a8e9612e0685250dc2ebe4cadd3d92d` (#43, #44 and #42), producing
validated code checkpoint `57c136bb748622059f7c238481d37a3709ae9d5d`.
`git range-diff` reports the milestone commit unchanged by the rebase.
This receipt is a subsequent documentation-only update, not a claim that the
earlier uncommitted payloads were built from the new checkpoint.

| Check | Actual rebased result |
|---|---|
| Root eight-project Release build | Zero warnings/errors; existing locked assets remained sufficient, no dependency-manifest changes or new restore required. |
| Core / Application suites | **312 / 989 passed**, zero failed/skipped. |
| Windows suite | First attempt **493 passed, 1 failed**, zero skipped: unrelated native-window hit-testing assertion. After explicit approval, one full rerun **494 passed**, zero failed/skipped; no test/product change or disabled test. Both TRX files retained. |
| Latest completed full-suite total | **1,795 passed**, zero failed/skipped on the completed set; the first-attempt Windows failure is not erased or counted as first-attempt success. |
| Fresh portable coverage | **5,893/5,893 lines; 2,393/2,393 branches; 686/686 methods**, all 100%. Only fresh PR Core/Application reports were merged; the initial timestamped-filename selector was corrected before merging. |
| Licence/version/fake-release/payload policy | All passed; release tests use fake GitHub and owned fixtures, not publication. |
| Locked framework-dependent publishes | x64/x86 passed; exact manifests verify **201/197 files** at validated code checkpoint `57c136b`. |
| Actual native inspection | x64 **7 PE files/10 declarations**, x86 **5/6**; expected machines/imports and pinned package SHA-256 equality passed. Static only, no installed/dynamic loading claim. |
| Effects/ownership | Original orchestration checkout untouched; no sibling branch merged. Only approved Git/PR mutations and existing automated fixtures; no installer execution, elevation, live app/audio/model, real-user migration or machine-policy effects. |

Fresh evidence is retained under `.net-test-artifacts\r04-pr-core`,
`r04-pr-application`, `r04-pr-windows`, `r04-pr-windows-retry` and
`r04-pr-coverage`. Published payloads are retained under
`artifacts\r04-pr-win-x64` and `artifacts\r04-pr-win-x86`; their manifests
identify the validated code checkpoint. The earlier receipt and artifacts
remain historical evidence. Auto-merge remains subject to normal up-to-date
branch and required CI gates, without administrative bypass.

Final main integration includes #41 and #46 on base
`6897d77cfc73a331f3ccf09646fbbcb009fbdcf2`. Validated checkpoint
`bc6014a385ab7f62100d5358e4e5b20a78af332b` retains the reconciled R03/R05
rows and the delivered-but-partial R04 row. The earlier apparent native
hit-test failures occurred at **desktop cleanup**, not a hit-test assertion:
switching back can fail while the worker retains implicit IME windows/hooks.
The user-authorized worker-exit cleanup fix passed twenty separate native
runs and the 494-case Windows suite, but #46 concurrently merged a canonical
thread-local IME/desktop regression fix. With explicit user approval, the
redundant test commit was dropped; #46's test source is retained unchanged.
Historical failed and successful TRX files remain available.

| Final combined check | Actual result |
|---|---|
| Root Release build | Zero warnings/errors. |
| Core / Application / Windows | **338 / 1,049 / 495 passed**, **1,882 total**, zero failed/skipped on the first combined run. Includes main's sixteen-iteration private-desktop restoration/release regression. |
| Fresh portable coverage | **6,246/6,246 lines; 2,685/2,685 branches; 742/742 methods**, all 100%, using only the fresh combined portable reports. |
| Licence/release/payload policy | Passed; #41's ten runner-process exit-code cases and fake-release regression passed. Version policy was also revalidated after #41, before #46; #46 does not alter that policy. |
| Locked publishes / exact native payloads | x64/x86 passed at checkpoint `bc6014a`; **201/197 files** verified. Native machines/imports/package SHA-256 passed, **7/10** and **5/6** PE/declaration counts. Static inspection only. |

Final outputs use `.net-test-artifacts\r04-combined-core`,
`r04-combined-application`, `r04-combined-windows`, `r04-combined-coverage`
and `artifacts\r04-combined-win-x64`/`r04-combined-win-x86`. This final
receipt update is documentation-only. R05 remains a bounded service/contract
foundation; this R04 PR does not activate its durable grant/question adapter
or shared production dispatch, and neither package is falsely closed.

#### Remaining R04 Gates

| Boundary | Explicitly still open |
|---|---|
| Migrations | Supported existing host v1 is validated without conversion. Broader version migrations, legacy content import and migration recovery/backups are not implemented. |
| Managed copies/artifacts | No content-bearing artifact or managed backup is needed/created by this metadata-only query milestone. Standard managed artifact staging/publication/reference and backup-generation interruption/deletion acceptance remains. Historical optional encrypted primitives do not satisfy it. |
| Retention/lifecycle | Independent 30-day diagnostic/span/link and 90-day audit due dates are assigned transactionally. Automatic database pruning, audit continuation anchors, query-visible expired segments, configurable preview/apply, session lifecycle/deletion and late-append/copy revocation remain R04/R12. |
| Audit authority | Typed routing and ordered persistence are implemented; full D-008 tamper checkpoints/rollback guarantees and durable authorization/grant consumption are not. SQLite, ACLs, file copies and query receipts do not provide new authorization. |
| Deployment/recovery | Owned fixture process kills and native rollback are automated evidence. Installed loading/effective ACLs, actual power-loss, all-users/cross-account/shared-storage claims and hardware/OS timing remain separately gated/approved. |
| Other runtime surfaces | General session registry, history/queue/workspace UI, model/tool/worker adapters and consequential receipts are not added; no automatic replay, grant migration, installer execution or optional encryption overhaul. |

### R04 Rebase Validation Receipt - 2026-10-06

Validated code checkpoint `74739023458bae7022cabc72ce1c4b34d8e602e1`
on main `3e8558fbff07943d39721b230599b02062d90d57`, not a union of sibling
test counts. Environment remains Windows build 26300, SDK 10.0.401,
runtime 10.0.12 and x64 test process. Subsequent roadmap changes are
documentation-only. The request wrapper remains after host-input eligibility;
main's readiness/cancellation, presentation-only Dismiss, speech containment,
startup/versioning and installer source were retained.

| Validation | Actual rebased result |
|---|---|
| Locked restore / complete Release solution build | Passed all eight projects, including the setup application; zero warnings/errors. Main changed manifests, justifying this locked restore. R04 adds no dependency/lock changes relative to main. |
| Core / Application / Windows full suites | 269 / 780 / 391 passed: **1,440 total**, zero failed or skipped. Separate TRX/coverage outputs under `.net-test-artifacts\rebased-*`. |
| Core/Application coverage | Passed 100% line (5,404/5,404), branch (2,209/2,209) and method (617/617), using only the latest rebased reports. Missing generated logging-source notices do not invalidate the generated report. |
| Actual desktop adapter | The four session-artifact in-memory assertions were rerun against rebased binaries and passed: category filtering, warning admission, category lookalike rejection and envelope identity/scalar projection. This is not actual file-I/O failure acceptance or an added repository test suite. |
| Licence/notice gate | `eng\Test-DependencyLicenses.ps1` passed against the new main closure. |
| Maintained CI policy contracts | `eng\Test-BuildVersion.ps1`, `eng\Test-GitHubRelease.ps1` and `eng\Test-InstallerPayloadContracts.ps1` passed. Publication tests use fake GitHub calls; their printed publication messages are not actual tags/releases. Payload rejection tests invoke neither compiler nor installer. |
| Locked framework-dependent publishes / exact-file manifests | x64 and x86 passed at feature version 0.1.0. `eng\Test-InstallerPayload.ps1` verified 201 x64 / 197 x86 files, excluding the receipt itself, against the checkpoint revision. Outputs are `artifacts\R04-rebased-win-x64` and `artifacts\R04-rebased-win-x86`. |
| Static native closure | x64: 7 native PE files / 10 native declarations; x86: 5 / 6. Expected machines and declared assets present; managed/native hashes and imports recorded in `artifacts\r04-rebased-native-closure.json`. Bootstrap `e_sqlite3.dll` still ships; no encrypted engine is admitted. |
| Cleanup / scope | Owned storage fixtures removed; whitespace passed. No app launch, real audio/device trial, registry mutation, elevation/install, live account, real GitHub publication or sibling-worktree mutation was performed. MSI/Burn assembly, ICE and installed acceptance were not rerun by this rebase. |

Full-suite commands:

```powershell
dotnet restore .\Kora.slnx --locked-mode
dotnet build .\Kora.slnx --configuration Release --no-restore
dotnet test --project .\tests\Kora.Core.UnitTests\Kora.Core.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\rebased-core --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix core
dotnet test --project .\tests\Kora.Application.UnitTests\Kora.Application.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\rebased-application --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix application
dotnet test --project .\tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\rebased-windows --report-trx
```

**Remaining R04 work, in delivery order:**

1. **Encrypted-provider/source-build prerequisite superseded:** use the existing
   pinned standard SQLite closure under the owner-approved profile baseline.
   Maintain ordinary licence/publish/native-loading checks; no codec admission
   or database key/rekey pipeline is required.
2. Compose the actual version-1 transactional task store into request routing
   with truthful outcomes, then implement separate diagnostic/audit/span/link
   persistence. The evidence sink and current routing store remain unavailable.
3. Complete supplied-profile provisioning, managed artifact/backup publication,
   interrupted recovery and explicitly supported schema migrations. Verify
   private effective permissions and readable-copy disclosure, without replacing
   corrupt/missing existing data or repairing permissions silently.
4. Complete audit ordering/checkpoints, retention assignment/configuration/
   pruning/gap semantics and bounded artifact lifecycle; validate actual sink
   failure/backpressure and interrupted-process behavior.
5. Prove the first on-disk identified request/task/terminal/recovery milestone,
   without replay, then qualify the applicable installed/protection gates.
   Real runtime/tool callback attribution and late-result reconciliation follow
   the R05/R06 handoffs, not the synthetic SDK event schema.

The WiX merge removes source-integration work, not these storage gates.
This remains a reviewable **safe gated foundation**, not complete R04 or
permission to enable clipboard/tool/content persistence.

### R04 Native Evaluation Continuation - 2026-10-06

Production source/dependencies are unchanged from the rebased checkpoint.
Flat-container acquisition still failed with validated OpenSSL transport;
the official v2 endpoint succeeded without disabling certificate validation.
All four packages and Windows release archives are retained in session
artifacts, not production publish output. Normal NuGet repository-signature
verification passed; unsigned native DLLs were not relabelled publisher-signed.

The native fixture's 44 assertions are real x64/.NET 10 owned-scratch
observations, not additions to the 1,440 repository-test receipt or installed
acceptance. Initial fixture assumptions about the codec version's display
string and live-WAL file sharing were corrected before the successful run;
its scratch files were removed. The separate ADO probe did not complete:
temporary `Add-Type` compilation reported CS1701 for .NET 8/10 framework
references. No blanket .NET 10 incompatibility is inferred from that warning.

Most importantly, public source review confirmed post-2.4.0 VFS read-error
and temporary-file fixes, plus notice/permission ambiguities. Exact 2.4.0
must not be enabled merely because basic encryption/tamper tests passed.
See D-009 for immutable fixes and component obligations. An owned native
build would be a delivery/servicing choice, not permission to invent crypto,
ask end users to compile, bypass installed gates or adopt commercial binaries.
That encrypted-build choice is now **historical/superseded**, not a prerequisite
for the approved standard-SQLite implementation below.

### R04 Approved Standard-SQLite Continuation - 2026-10-06

The owner explicitly chose standard SQLite under supplied LocalApplicationData
with verified private permissions, accepting readable copies outside that
boundary. [D-009](Decision_Register.md#approved-profile-secured-sqlite-baseline---2026-10-06)
and the canonical architecture/security/acceptance/lifecycle documents now
reflect this decision. Existing encryption experiments remain historical.
No native build, package upgrade, new dependency or database key is required.

[WindowsSqliteHostTaskStore](../src/Kora.Windows/Storage/WindowsSqliteHostTaskStore.cs)
implements a separate private `HostStorageV1` partition without Keys, actual
owner/ACL/reparse checks, version-1 database identity/schema and integrity checks,
FULL-synchronous transactions, optimistic revisions, immutable request identity
and atomic current-state/ordered-event updates. Existing missing/corrupt data,
unsupported schemas and unexpected permissions fail without replacement/repair.
Recovery reads are bounded to 100 and use the existing Interrupted/Unknown
domain transitions without an executor or replay callback.

[Actual SQLite/ACL tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteHostTaskStoreTests.cs)
cover reopen, ordered receipts, stale identities/revisions, cancellation before
I/O, corrupt/missing databases, unsupported versions, invalid persisted
identity/origin/event projections and permissive files. These are unique owned
scratch tests, not user-data, process-kill, power-loss or installed acceptance.
The store remains **uncomposed**; transcript routing, durable authoritative
evidence, migrations beyond v1, backups, retention and managed-copy deletion
remain the concrete next work. The complete R04 milestone is not claimed.

#### Standard-SQLite Validation Receipt

Validated the **uncommitted continuation on `7473902`, main base `3e8558f`**,
not a newly committed/published revision. Windows build 26300/x64, .NET SDK
10.0.401/runtime 10.0.12, feature version 0.1.0. No dependency manifests or
locks changed; existing locked-restored assets were reused with `--no-restore`.
Earlier analyzer/test-compilation errors were corrected before this final run.

| Check | Final actual result |
|---|---|
| Full eight-project Release solution | Zero warnings/errors, analyzers enabled |
| Core full suite | 269 passed, zero failed/skipped |
| Application full suite | 780 passed, zero failed/skipped |
| Windows full suite | 405 passed, zero failed/skipped; includes 14 actual private SQLite cases |
| Total | **1,454 passed** |
| Fresh Core/Application coverage | 5,404/5,404 lines; 2,209/2,209 branches; 617/617 methods, all 100%. Only latest reports from this continuation were merged. |
| Licence/notices | Approved/current; no notice or lock changes |
| Version/fake-release/payload contracts | All passed. Release tests use fake GitHub; no real publication. |
| x64/x86 framework-dependent publishes | Both passed with existing locked assets; exact manifests verify 201/197 files including licence texts. Manifests identify base HEAD; this working copy remains dirty. |
| Native closure | x64 7 PE files/10 declarations, x86 5/6; exact SHA-256 and machine/declaration match to prior inspected closure. Initial inspection-script property enumeration was corrected; no native asset change. |
| Scope not performed | No real application launch, installer/MSI/Burn/ICE rerun, installed loading, process-kill, power-loss, backup/deletion or user-data mutation. |

Exact build/test/publish and policy commands:

```powershell
dotnet build .\Kora.slnx --configuration Release --no-restore
dotnet test --project .\tests\Kora.Core.UnitTests\Kora.Core.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\profile-sqlite-core --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix core
dotnet test --project .\tests\Kora.Application.UnitTests\Kora.Application.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\profile-sqlite-application --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix application
dotnet test --project .\tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\profile-sqlite-windows --report-trx
.\eng\Test-DependencyLicenses.ps1
.\eng\Test-BuildVersion.ps1
.\eng\Test-GitHubRelease.ps1
.\eng\Test-InstallerPayloadContracts.ps1
.\eng\Assert-CodeCoverage.ps1 -ReportPath .net-test-artifacts\profile-sqlite-coverage\Cobertura.xml -MinimumLine 100 -MinimumBranch 100
dotnet publish .\src\Kora\Kora.csproj --configuration Release --runtime win-x64 --self-contained false --no-restore --property:RestoreLockedMode=true --output artifacts\R04-profile-sqlite-win-x64
dotnet publish .\src\Kora\Kora.csproj --configuration Release --runtime win-x86 --self-contained false --no-restore --property:RestoreLockedMode=true --output artifacts\R04-profile-sqlite-win-x86
```

Both publish directories were passed through `eng\Test-InstallerPayload.ps1`
with `-Version 0.1.0 -SourceRevision 74739023458bae7022cabc72ce1c4b34d8e602e1`,
first `-WriteManifest`, then verification. Tests use unique owned directories
and dispose them; no leaked storage fixture directories remain.

#### PR Integration Rebase - 2026-10-06

Main advanced during publication preparation to `fd71c80` (RT2 PR #37).
Both R04 commits rebased cleanly; combined production source at `3019a2b`
retains the merged bounded RT2 findings/all-path Blocked status and the
owner-approved standard-SQLite baseline with optional future R30 encryption.
The final full Release build has zero warnings/errors; Core 269, Application
780 and Windows 405 all pass (1,454 total, zero failed/skipped).
Fresh Core/Application coverage remains 5,404/5,404 lines, 2,209/2,209 branches
and 617/617 methods. Build/test commands are the same as the standard-SQLite
receipt above, with results under `.net-test-artifacts\r04-pr-*`.
This is this branch's root-solution evidence, not a rerun or enlargement of
the separate RT2 experiment's admission. R04 integration remains partial.

#### PR #39 Windows File-Ownership Correction - 2026-10-06

Both Windows CI jobs at `d39f411` (runs `37432278634` and `37432272073`)
failed **43 of 405 tests**; 362 passed, none skipped. The common first failure
was the exact-user owner check on a newly created `operation.lock`. Ordinary
file creation did not set an owner; an elevated token can default to
Administrators ownership even when the inherited DACL is private. The actual
runner owner SID was not logged, and local elevation was not performed.
This is a creation-policy defect, not evidence that a package upgrade is needed.

The correction supplies current-user ownership and protected user-only ACLs
at atomic `CreateNew`, shared by leases, staging, the initial database and
its rollback journal. Existing files are opened without implicit creation;
no existing ACL/owner is normalized and no existing content is overwritten.
SQLite connections reuse the pre-created journal with `PERSIST`,
`synchronous=FULL` and memory temporary storage. Missing/permissive journals
fail explicitly before database access; missing journals are not recreated.
Earlier uncomposed prototype databases without the journal require explicit
recovery/migration. Hot-journal/process-kill acceptance remains open; this
change does not claim it or expand production composition.

Local validation of the **uncommitted correction on `d39f411`, main base
`b302b86`**: Windows build 26300/x64, SDK 10.0.401/runtime 10.0.12,
feature version 0.1.0. No dependency manifest, lock, native provider or CI
workflow change; existing locked-restored assets were reused.

| Check | Actual result |
|---|---|
| Full Release solution | Zero warnings/errors, analyzers enabled |
| Focused storage suite | 59 passed, zero failed/skipped |
| Full Core / Application / Windows | 269 / 780 / 412 passed; **1,461 total**, zero failed/skipped |
| Fresh Core/Application coverage | 5,404/5,404 lines; 2,209/2,209 branches; 617/617 methods, all 100%; only the two new reports were merged |
| Licence/notices, version, fake-release, payload contracts | Passed; no dependency/notice changes or real release |
| Fresh framework-dependent x64/x86 publishes | Passed; exact manifests verify 201/197 files including licence texts, identifying base HEAD rather than a future commit |
| Native closure | x64 7 and x86 5 native PE files, including app hosts, match the previously inspected publish hashes and expected machine type; no native bytes changed |
| Scope not performed | No elevation, token/security-policy change, real user-data mutation, installer/app launch, installed loading, process-kill or power-loss trial |

Seven new Windows cases cover explicit owner/DACL creation, async handles,
existing-file/no-repair/no-overwrite behavior, permissive leases, path escape,
database/journal ownership across commits/reopens and missing/permissive
journals. Corruption-test connections explicitly retain the same journal
policy, so the intended schema/identity/projection failures are tested rather
than a missing-journal proxy. Synthetic artifact-budget files are created
privately before testing the byte/count limits. Owned fixtures were disposed.

Exact commands:

```powershell
dotnet build .\Kora.slnx --configuration Release --no-restore
dotnet test --project .\tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj --configuration Release --no-build --filter-class 'Kora.Windows.IntegrationTests.Storage.*' --results-directory .net-test-artifacts\r04-ci-storage --report-trx
dotnet test --project .\tests\Kora.Core.UnitTests\Kora.Core.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\r04-ci-core --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix core
dotnet test --project .\tests\Kora.Application.UnitTests\Kora.Application.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\r04-ci-application --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix application
dotnet test --project .\tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\r04-ci-windows --report-trx
dotnet reportgenerator '-reports:.net-test-artifacts\r04-ci-core\*.coverage.cobertura.*.xml;.net-test-artifacts\r04-ci-application\*.coverage.cobertura.*.xml' '-targetdir:.net-test-artifacts\r04-ci-coverage' '-reporttypes:Cobertura;TextSummary' '-assemblyfilters:+Kora.Core;+Kora.Application'
.\eng\Assert-CodeCoverage.ps1 -ReportPath .net-test-artifacts\r04-ci-coverage\Cobertura.xml -MinimumLine 100 -MinimumBranch 100
.\eng\Test-DependencyLicenses.ps1
.\eng\Test-BuildVersion.ps1
.\eng\Test-GitHubRelease.ps1
.\eng\Test-InstallerPayloadContracts.ps1
dotnet publish .\src\Kora\Kora.csproj --configuration Release --runtime win-x64 --self-contained false --no-restore --property:RestoreLockedMode=true --output artifacts\R04-ci-fix-win-x64
dotnet publish .\src\Kora\Kora.csproj --configuration Release --runtime win-x86 --self-contained false --no-restore --property:RestoreLockedMode=true --output artifacts\R04-ci-fix-win-x86
```

Both publishes were passed through `eng\Test-InstallerPayload.ps1` with
`-Version 0.1.0 -SourceRevision d39f411fdacf2bf8e7b5a2ed78b72a2b93982df7`,
first `-WriteManifest`, then exact verification after copying the approved
licence texts. Fresh GitHub checks on the pushed correction are separate from
this local receipt; the earlier failures are not called green or rerun evidence.
R04 remains partial and R30 optional encryption remains deferred.

#### Current-Main Refresh After CI Correction - 2026-10-06

The correction `7795674` passed fresh push CI `37436375408` (Windows 412)
and PR CI `37436381184` (Windows 437), including portable coverage/packaging
and WiX jobs. The differing counts reflect GitHub's merge candidate including
new main, not different test selection or skipped failures. Main advanced to
`90d8f48` (presence PR #36); strict up-to-date protection still marked #39
BEHIND. Merge `8be4f9d` incorporates that approved upstream work cleanly
without rewriting the published correction or changing other worktrees.
The transcript tracing wrapper and upstream presentation changes both remain.

Full local validation at **`8be4f9d`, main base `90d8f48`** uses the same
SDK 10.0.401/runtime 10.0.12, Windows build 26300/x64 and exact commands
above, with result/coverage prefixes `r04-ci-main-*` and publish directories
`artifacts\R04-ci-main-win-x64` / `artifacts\R04-ci-main-win-x86`.
Release has zero warnings/errors; Core **307**, Application **941** and
Windows **437** all pass: **1,685 total**, zero failed/skipped.
Fresh latest-only coverage is **5,633/5,633 lines**, **2,285/2,285 branches**
and **664/664 methods**, all 100%. Licence/notices, version, fake-release and
payload-contract gates pass. Both fresh publishes verify **201/197 files**
including licence texts with source revision
`8be4f9dc26c961d5f0b1b413a2a8729239cfe10d`; all **7/5 native PE files**
retain the prior exact hashes and expected architecture. No new dependencies,
native bytes, privilege/token changes, real app/installer launch or expanded
storage/interruption acceptance are implied. Fresh checks after publishing
this refresh remain distinct from the earlier green runs.

## R02 Runtime/Provider Follow-Up Gates

These are sub-gates of R02, not new acceptance milestones or a claim that
other R02 feasibility branches are complete. The
[technical continuation](Runtime_Provider_Feasibility.md) specifies the
candidate boundaries and unsupported-control decision path.
No gate below is closed by merging the retained Node experiment.
The [deferred runtime/provider checklist](Deferred_Validation.md#runtimeprovider-follow-up)
records preparation and evidence to collect. The separate
[actual .NET RT1 fixture](../experiments/r02-dotnet-control-proof/README.md)
now passes its explicitly approved source-built minimal profile; the separately
approved released-profile MG1 envelope also passes. RT2 all-path admission,
PV1 and Gate 0 remain open. Partial research does not enable production exposure.

| Gate / owner | Current state | Needs / next action | Exit evidence and downstream effect |
|---|---|---|---|
| R02-RT1 - .NET control-point parity; runtime engineering lead | PASS, scoped source-built profile, 2026-10-05 UTC; 45/45 tests; released NuGet byte parity BLOCKED | Preserve [fixture/disposition](../experiments/r02-dotnet-control-proof/evidence/disposition.json): public v1.0.16 exact source, runtime 1.0.90/protocol 3, Windows x64, .NET 10.0.12/SDK 10.0.401. User explicitly approved unmodified source build after NuGet TLS failure; separate locks/license/native hashes and byte reproduction recorded. | Final serialized initial/history/all-status/exception paths, actual pre-effect denial, streaming/errors, session I/O and cancellation/isolation measured; denied effects/forwarded markers zero. Hook-only FAIL retained, Node witness unchanged. Unblocks RT2/MG1 proof for these bytes only; no production adapter, sidecar or D-001 closure. Retest a different artifact/profile. |
| R02-RT2 - Runtime lifecycle observation; runtime and security leads | BLOCKED, 2026-10-06 UTC; 20/20 bounded tests and two locale contracts pass for exact RT1 bytes; P0 | [Independent fixture/receipts](../experiments/r02-runtime-lifecycle-proof/EVIDENCE.md): final source reproduction and full 45/45 staged RT1 regressions pass; PowerShell/conhost startup descendants and transient policy-test files observed. Live watcher/IP Helper/Toolhelp/managed diagnostics are incomplete and do not prevent native paths. User retained fail-closed contract; privileged tracing deferred to separately approved dedicated host. | Obtain attributable all-file/all-destination/native-diagnostic observation with loss controls and actual native mediation/prevention, not model counters/scans or W2 best-effort dependency policy. No denied-marker native egress/recoverable persistence claim until observable/controlled. Repeat account-specific paths in PV1. Keeps R08/runtime unavailable; does not close worker/deployment/storage or durable host authority. |
| R02-MG1 - .NET host management envelope; runtime engineering lead | PASS for separately user-approved released SDK 1.0.16 / unchanged RT1 native 1.0.90 minimal HTTP/stdio, 2026-10-06 UTC; historical source-built/Node witnesses unchanged | [Independent fixture](../experiments/r02-dotnet-management-proof/README.md): released hashes and exact source pinned; 45 RT1 regressions repeated before 22 host + 16 actual runtime cases. Complete 32768/32769 input and 4096/4097 typed JSON, 15000-ms dispatch deadline with real held inference and stalled native abort ack, single admission/30 monotonic rolling failures/no forwarded retry, independent manager and Unknown quarantine pass. Initial source-built reproduction blocker retained, not inferred away. | Supports R13 envelope implementation and a management PV1 proposal after applicable RT2 lifecycle admission. Host controls remain local while quarantined; SDK ack/socket closure never certify rollback or physical computation stop. R04 owns durable identity/audit/no-replay. No account capacity/cost, production protocol, task slots/resource leases or scheduler acceptance; retest any changed artifact/profile. |
| R02-PV1 - Approved account/provider trial; runtime lead with account owner and security/legal review | BLOCKED; no account/live usage approved | RT1/RT2 for live runtime trials; MG1 additionally for management. First prepare intended provider/model/region, user-owned supported auth, permitted assistant/SDK use, plan/policies, published limits and explicit spending controls. Ask for account and usage-budget approval before potentially paid inference/provisioning. | Record real auth/error behavior, destination/content isolation, terms eligibility, quota/rate limits and billed-cost assumptions/limits. Execution profile acceptance feeds D-001/R08; management-specific two-execution-plus-manager capacity and usage envelope additionally feed D-004/R13. Failed/incompatible/unapproved service stays disabled with deterministic local choices. |

**Next sequence:** scoped source-built RT1 and separately approved released
RT1/MG1 controls passed; distinct profile identities remain mandatory.
RT2 safe observations are now recorded but all-path admission is Blocked.
Follow its [dedicated-host/R08/MG1/PV1 handoff](../experiments/r02-runtime-lifecycle-proof/EVIDENCE.md#handoffs).
PV1 live trials follow the relevant technical gates and explicit approval.
If RT1 fails, bring the supported-runtime/sidecar/inference-adapter options
back as a D-001 decision with evidence; do not weaken Gate 0.
Local R06/R07, R03 and the deterministic R12/R13 core retain their own
prerequisites and need not wait for hosted-model trials.
Downstream packages listing R13 require its deterministic core and admitted
scheduler/concurrency evidence; its optional model-assisted stage is not an
implicit prerequisite unless that dependent capability uses management inference.
Neither I18 nor these runtime follow-ups close D-003/local inference,
speech/hardware, worker containment, deployment or R04 private-profile
storage integration/recovery/lifecycle gates. D-009 supersedes mandatory
encrypted-storage admission; R30 remains optional.

### R05 Bounded Authorization/Question Foundation

**Partial, foundation only - 2026-10-06.** New portable core/application
services are implemented and tested against the merged R04 host contracts.
This parallel partition changes no MainViewModel dispatch, action handler,
UI composition or production storage schema. It enables no new execution.

| Delivered boundary | Source and test evidence | Remaining integration gate |
|---|---|---|
| Host-owned typed questions | [Question service](../src/Kora.Application/Interaction/HostQuestionService.cs), bounded single/multiple-choice/text [specification](../src/Kora.Core/Interaction/QuestionSpec.cs), existing HostId/HostRequest/HostRevision; [contract tests](../tests/Kora.Core.UnitTests/Interaction/InteractionContractTests.cs) and [service tests](../tests/Kora.Application.UnitTests/Interaction/HostQuestionServiceTests.cs) | Durable questions/drafts/answers, native exact review/readback, trusted UI/voice input and foreground spoken-target ownership; typed forms/secure flows are not added. |
| Exact host operation and grant applicability | [Binding/proposal contracts](../src/Kora.Core/Authorization/ExactOperationBinding.cs), [authorization service](../src/Kora.Application/Interaction/HostAuthorizationService.cs) and [tests](../tests/Kora.Application.UnitTests/Interaction/HostAuthorizationServiceTests.cs); exact source/content/implementation/invocation/resource/identity/destination/transformation/policy/effect checks, origin preservation and unknown/prohibited denial | Admitted registry/catalogue, actual canonical byte/resource snapshots, immutable review and action-specific ownership/privacy/containment/deployment proofs; no named-action preference migration. |
| Scope, lifecycle and retention separation | Once consumed atomically; Session bound to durable work-session ID/generation; Perpetual record has no expiry/retention/eviction. Tests reject ended/resumed/cross-session authority and preserve independent records across history removal/recreated services. | R04/R12 adapter must advance lifecycle generations, preserve Active-session generations on restart and retain Perpetual records outside session deletion. Synthetic history removal/recreated services are not disk restart/deletion acceptance. |
| Race/revoke/change and audit certainty | [Atomic store seam](../src/Kora.Core/Storage/IHostInteractionStore.cs), matching live HostActivity and typed SecurityAuditEvent; concurrent duplicate approval/Once consumption, revoke/use, stale revisions, observed-content permanent revocation and [storage/audit/cancellation tests](../tests/Kora.Application.UnitTests/Interaction/InteractionCommitTests.cs) | Verified R04 durable task/evidence handoff and agreed adapter/schema ownership; atomic authoritative audit failures must block consequential dispatch. A use receipt is not a reusable dispatch token or effect receipt. |

**Validation receipt:** root Release build passed with zero warnings/errors;
Core 333/333 and Application 1001/1001 passed. Fresh-only combined portable
coverage passed the exact 100% line/branch gate, including named transition
methods (not only callback wrappers). No coverage exclusions or thresholds
were changed. Dependency manifests, native payloads and installer/bootstrap
tooling are unchanged.

Earlier full Windows runs and a user-approved rerun exposed intermittent
private-desktop cleanup failure in the
[native presence hit-testing test](../tests/Kora.Windows.IntegrationTests/Presentation/WindowsPresenceWindowInputTests.cs)
at `SetThreadDesktop`, not the subsequent `CloseDesktop`. The user-authorized
test-only fix disables IME initialization on its disposable native thread
before creating windows, preventing hidden text-service resources from
blocking desktop restoration. It preserves the actual click-through/Ctrl
assertions, checks cleanup errors, waits for worker completion and adds a
16-cycle private-desktop restoration/release regression. Six fresh targeted
runs passed both tests; three fresh full Windows runs each passed 438/438,
with no skips or exclusions. Final root Release and fresh-only portable
coverage also passed again. Failure and passing TRX evidence is retained
under `.net-test-artifacts/r05-native-test-fix`; the final portable report is
under `.net-test-artifacts/r05-coverage-final`. Production Windows/UI code and
system-wide input settings are unchanged. **The automated all-suite gate now
passes; production integration remains pending the handoff below.**

**Required handoff before production wiring:**

1. R04 supplies verified admitted intent/task/evidence composition. Agree the
   durable interaction/grant adapter partition and schema ownership before
   persistence changes; do not alter its dispatch or store schemas in parallel.
2. Resolve current host operation bytes, source access, identity/account,
   destination and mandatory policy inside the atomic admission boundary.
   Complete declared identity remains exact; W2 transitive tracking is best
   effort and neither a containment profile nor broader authority.
3. Serialize task cancellation, session Done/delete/resume, grant revoke/edit,
   observed content changes and policy generations with use. Successful
   intent/approval/audit commits are mandatory before any consequential effect.
4. Compose trusted native review/input, exact foreground voice targeting and
   immediate pre-effect revalidation; repeat real durable restart/deletion/
   interruption and action-specific receipt/containment trials.

This does not close all R05, D-008/D-009/D-013 or A0-A4. General script/worker/
tool/remote adapters, real lock/power, install/account/credential/security
policy changes and microphone/app execution remain unexposed by this slice.
No dependency/profile architecture or sibling merge is introduced.

### Core Foundations and First Useful Interaction

| ID and work package | Starting state | Priority/value | Needs | Completion condition |
|---|---|---|---|---|
| R01 - Reconcile policy, scope and checkpoint contracts | Design reconciliation complete; approved 2026-10-05; no runtime changes | P0 - prevent incompatible authority and consent implementations | None | Initial-release authority, consent, optional privacy and standalone-lock binding recorded and aligned above; A2/A3 evidence remains required. Standalone application rollback remains deferred R27 work. Runtime/enforcement proof is not claimed by this package. |
| R02 - Run release-blocking feasibility proofs | Partial candidates in I01/I06/I09/I16; storage/inference/distribution outcomes recorded; containment I17/runtime I18 retain open gates | P0 - discover runtime/hardware/containment limits | R01 | Complete runtime RT1/RT2/MG1/PV1 as applicable, retaining rejected hook-only and blocked account/global paths as unavailable. Qualify local model/licence/CPU floor, wake, worker containment and deployment. D-009 now selects standard SQLite/private profile permissions; encrypted-native/key admission is superseded. Complete [local L1-L5/L6](#r02-local-inference-continuation), W1-W3 and R02-D01/D02/D03 evidence; historical NSIS assembly is not D-005 closure. Use actual SDK/provider/OS evidence, not aggregate R02 success; preserve unrelated proof gates. |
| R03 - Establish Windows/audio ownership and privacy foundation | PTT/cross-build ownership and privacy closure implemented; bounded x64/audio trials pass; full native acceptance open in I03-I06/I12/I13 | P0 - stop unauthorized capture and overlapping owners | R01 | Complete acceptance of cross-build single-owner activation/handoff/return, consent/enablement generations, explicit PTT, bounded audio/transcript buffers and stale-callback rejection. Enforce wake-only versus activated-transcription separation; never label activated grammar capture as production wake. Prove native lock/disconnect/suspend/permission-polling/device observation and capture/audio/output closure on every required event. Release capture within 500 ms of observed lock in every reference trial. Provide native/tray recovery without model/network/speech. |
| R04 - Introduce durable identities, Activity tracing and authoritative host contracts | **Partial; first bounded composed milestone delivered.** Exact local version-query intent/dispatch/evidence/receipt and no-replay startup recovery compose private standard-SQLite task/log/audit/span/link partitions; [inventory](#r04-foundation-delivery), [baseline](#r04-approved-standard-sqlite-continuation---2026-10-06) and [composed continuation](#r04-composed-durable-version-query---2026-10-06). | P0 - stable attribution and crash-safe intent | R01, R03; approved D-009 standard SQLite/profile baseline | Complete broader supported migrations, backup/artifact publication/interruption, audit checkpoints, retention/query-gap/deletion semantics and relevant runtime/tool boundaries. Preserve the bounded query's required evidence admission and truthful receipts. No database key/rekey/encrypted-native prerequisite, history/queue UI, automatic replay or proof-fixture schema promotion. R04 remains open until all its criteria are met; installed/power-loss evidence is not inferred. |
| R05 - Build the shared authorization/question gateway | **Partial foundation delivered**, in addition to I11/I12: [typed services/contracts and tests](#r05-bounded-authorizationquestion-foundation). Durable adapter/native input/production wiring pending verified R04 handoff. | P0 - one authority path for direct/UI/model/skill requests | R03, R04 | Compose host-owned typed questions, exact proposals and trusted native input; integrate atomic use/audit, operation-bound durable Session and independent Perpetual records. Prove actual immutable bytes/review, durable lifecycle/deletion, edit/removal, audit/receipt certainty and immediate pre-effect revalidation. Apply standalone-lock binding only after its durable host identity is committed/shown. Legacy action-name preferences confer no new executable authority without explicit review/approval. No new execution is enabled by the bounded foundation. |
| R06 - Implement the admitted tool registry and local tool/result loop | Partial JSON selector in I02/I09/I10; R02 harness is not adapter qualification | P1 - natural requests can discover and use Kora capabilities | R02 (local runtime: L1-L5 qualification), R04, R05 | Expose versioned admitted schemas/skill summaries per lane; implement bounded validated proposals, host execution and correlated approved results followed by continued reasoning. Use the R02-L5 tested compatibility/context/resource envelope, preserve digest checks and rerun affected proofs on integration. First deliver discovery/application/readiness/runtime/status tools, deterministic status presenters and typed success/denied/unknown/unavailable behavior. Preserve exact offline safety routes. Add registry/catalogue coverage and hostile-result/unknown-ID/late-cancellation tests; do not expose unimplemented session tools. |
| R07 - Deliver explicit clipboard context and local-first explanation | Outstanding context path; partial inference I09/I10; no R02 real answer/offline-success proof | P1 - first useful private vertical slice | R03, R04, R05, R06; R02 local L5 evidence carried through L6 | Implement request-triggered plain-text clipboard snapshot/preview, immutable context/source IDs, purpose/secret/destination classification, bounded excerpts and explicit reuse/revocation. Fit the complete approved envelope to the qualified context budget or reject explicitly. Reuse R02 fixtures/rubric and isolation evidence, then repeat actual quality/cancellation/no-egress proof on the integrated host; synthetic payload injection is not clipboard-broker acceptance. Local inference missing/unhealthy remains unavailable with no remote fallback. Unsupported clipboard formats are explicit. |
| R08 - Integrate the controlled remote runtime and streaming path | Outstanding production adapter; I18 candidate only; RT2 all-path admission BLOCKED despite passing bounded tests; hook-only path rejected; local production inference remains buffered | P1 - complete A0 and provider-neutral interaction | R02-RT1/RT2 and execution R02-PV1; local L5 envelope for local streaming; R04, R05, R06, R07 | Carry the [RT2 handoff](../experiments/r02-runtime-lifecycle-proof/EVIDENCE.md#handoffs), resolve native observation/prevention before exposure, and integrate the proved .NET/runtime profile through pre-effect authorization, all-status host result sanitization and a final serialized-request egress gate. Disable unverified built-ins/collection/storage/transports; keep credentials host-only and cancellation truthful. Pass Gate 0 including actual account/destination/diagnostic evidence, streamed output/backpressure and zero denied effects/markers. Reuse host contracts and the qualified local envelope for local streaming/iteration; measure user-visible first output and cancellation, not experimental token timing alone. No production Node bridge or alternate provider without an explicit D-001 decision. |
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
| R11 - Deliver registered embedded multi-script skills and containment | Outstanding runner/packages; I17 partial proof is not admission; partial readiness/lock I08/I12 | P0 - finish lock without admitting arbitrary execution | R02 (applicable W1-W3 worker/deployment gates, D-013), R03, R04, R05, R06, R10 | Embed lock manifest/instructions/fixtures, entry script and shared helper; verify `Kora.ScriptSet.v1`/`Kora.SkillDefinition.v1` complete framed identities and dependent-grant revocation. Direct/model/skill routes use the same pinned task exactly once with immutable source review. Complete W4: admit only fixed profiled workers with exact declared-resource review, best-effort transitive tracking/gap disclosure, protected required runtime/adapter admission, attributable network denial, bounded output/cancellation/Unknown receipts and real OS filesystem/child-process/credential/Kora-resource isolation. Prove observed lock outcome. Reject unsupported profiles or an unapproved broker substitution. Prepare power packages but do not enable OS power until R16. |
| R12 - Complete session lifecycle, history and per-session work/queues | Outstanding beyond bootstrap I07/I13; R02 deletion/backup limitations recorded | P1 - durable, inspectable long-running work | R04, R05, R06, R10, R11 | Implement Active/Done, create/rename/select/resume/mark-done/delete, paged history/search/immutable artifacts and authoritative task state. Add ordered per-session queues, admission/deadlines/user waits/pause/cancel/remove/clear controls and no restart replay. Implement configurable 24-hour archive/30-day deletion on the same meaningful-activity clock, source revocation, late-append prevention and inventoried deletion/rewrite across journals/caches/indexes/artifacts/staging/managed backups. Prove preservation of unrelated content and disclose exported/provider/forensic limits; row/key unlink alone is insufficient. Session eligibility ends appropriately; independent Perpetual records survive session cleanup. |
| R13 - Add bounded independent management and concurrent execution | Outstanding production core; released-profile MG1 envelope/topology proof passes, not production integration | P1 - remain responsive while useful work runs | Deterministic core: R05, R06, R10, R12 and applicable R02 local concurrency budgets. Model-assisted stage additionally: R08, R02-MG1 and management R02-PV1 | Deliver deterministic routing/status/choices/cancel first without hosted inference. Carry MG1's complete serialized 32 KiB input/4 KiB typed output, host 15-second dispatch deadline independent of send/abort ack, one in-flight, 30 attempts including failures per rolling hour/profile, fresh conversations and no forwarded retry into the R04-backed host. Reject unknown fields/targets/revisions and late output; Unknown remains quarantined until applicable observed receipts, not SDK ack. Add model assistance only after applicable runtime/account admission. Independently prove two task slots, isolated identities/contexts/grants, resource leases/fairness and reconciliation; synthetic conversation count is not scheduler/account evidence. No management task tools or approval authority; power follows R01. |
| R14 - Build the coordinated Sessions workspace and interaction surfaces | Partial compact/native windows I03/I11 | P1 - make sessions, decisions and results understandable | R05, R09, R12, R13 | Deliver compact latest interaction, list-plus-full-conversation/history, work/queue controls, Evidence mode and separate immutable detail/script surfaces. Evidence mode provides Logs, Audit and All Evidence views with deterministic bounded `evidence.list`/get/search/read_trace, daily-file reads, stable citations and authority/retention/gap status. Every session exposes evidence across all its traces; every row pivots to its W3C trace tree/links and durable session/task/invocation/approval/audit correlation. Add Ask Evidence as a visible read-only reasoning flow over explicit selected records/filters: local by default, exact remote-egress preview, cited observation-versus-inference answers and no recursive model tool or authority. Address native/voice questions explicitly; switching windows never retargets approvals. Add typed full-content views, bounded navigation and scoped evidence/artifact export. |
| R15 - Complete call-aware feedback, authorization and request-origin gates | Partial policy/preferences, unavailable real adapter I14 | P0 - prevent call leakage and reusable-authority surprises | R03, R05, R09, R10, R13, R14 | Deliver manual call state and capability-qualified detectors/signals with truthful Active/Suspected/Unknown/unavailable distinctions. Default feedback to UI-only and `calls.ignoreReusableGrants` to On. Preserve reusable records but require fresh exact Once approvals when protected. Reject voice-originated voice/in-call mutations across every route; a later UI confirmation does not change origin, so require new UI initiation. Race entry/clearance/settings/approval/dispatch and preserve grant-free stop/status controls. |
| R16 - Enable graceful protected power and all-session app controls | Partial proposals and current-app lifecycle I12/I13 | P0 - make disruptive actions safe and truthful | R01, R05, R11, R12, R13, R15 | Register/verify fixed shutdown/restart packages and helpers. Implement all-session impact review, fresh action-specific voice or equivalent UI confirmation, 30-second foreground prompt, two-minute single-use approval and 30-second cancellable host countdown. Perform mandatory real OS/provider checks; no extra UI click solely because risk is high, no forced close and no unrelated OS cancellation. Coordinate exit/restart and resource ownership; reconcile observed receipts rather than claiming success from a proposal. |
| R17 - Finish supported distribution, setup and startup behavior | Binary MSI/custom Burn, scoped logon/completion and release automation implemented; source delivery, upgrade limitations and installed/protection acceptance outstanding | P1 - users can install/run safely without a development checkout | R02 (distribution/hardware: D01/D03 admission; D02 direction selected), R03, R09, R10, R11, R16 | Complete R17-D01/D02/D03 below without another standalone feasibility project. Retain the delivered binary/CI/version/prerequisite/optional-consent slice; finish managed-source delivery, supported upgrades/recovery, protected deployment and per-release native/licence qualification. Risk-based installed trials must cover logon/removal, repair/uninstall/all-users/completion and runtime-only launch against exact hashes after applicable resource/privacy/worker gates exist. Current per-user installs do not establish independent protection. Existing win-x86 output is not x86 acceptance; certify each offered architecture. NSIS-only paths are retired, not the retained source/native checks. No in-app updater/download/install authority is added; reviewed dependency setup remains available. |
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
boundary investigations can proceed now. The owner approved unsigned
beta/stable POC publication with front-loaded/ad-hoc/risk-based installed
validation, not exhaustive manual trials for every MSI. Production R17
acceptance still needs the parent prerequisites, including later real resources
and workers. A blocked trial is pending work, not completed evidence.

| Step / current state | Owner | Needs / next action | Completion condition and consequence |
|---|---|---|---|
| R02-D01 - Clear redistribution and declare launch inputs; repository controls delivered, release review open | Product owner and release engineering lead | Apply PolyForm Shield 1.0.0, the reviewed NuGet gate/version-specific overrides and notices. Audit actual native/font/text/model assets and the selected WiX build-tool terms separately. Use runtimeconfig/import inventory to declare launch requirements apart from optional capability assets. | Per-release redistribution/notices and supported runtime/native policy are reviewed. Binary delivery requires supported .NET 10 x64 Desktop Runtime and the observed VC++ x64 prerequisites, not Git/SDK. A passed NuGet gate or successful build does not clear every external asset. |
| R02-D02 - Select Windows installer direction; selected | Release engineering lead | Adopt WiX MSI + Burn, preserve historical NSIS evidence and hand off reviewed/pinned tooling plus a bounded Windows packaging job to R17. No separate WiX feasibility project or mandatory native Linux NSIS trial. | The direction is selected, not installed acceptance. Linux remains preferred wherever feasible; WiX packaging/lifecycle/protection must pass R17-D01/D02/D03. Retiring the old Linux NSIS gate does not waive Windows/runtime/resource gates or introduce Wine. |
| R02-D03 - Establish the independent Windows deployment boundary; blocked approval | Release and security engineering leads, Windows lab owner | Allocate an approved disposable Windows 11 x64 lab. Validate deployment parent/version protection, independent activation authority, actual non-elevated app token, protected runtime/native load roots and source identities; coordinate worker identity requirements with containment. | Actual identity/effective-access/link/replacement tests prove the boundary or leave affected capabilities disabled. An ACL request or user-writable staging is not protection. Missing workers are recorded as untested and must pass their later gates; do not modify an existing user installation to obtain evidence. |
| R17-D01 - Implement WiX source and binary delivery; binary and source build-only slices implemented, activation/installed acceptance open | Release and application engineering leads | Maintain WiX 7 MSI/custom Avalonia Burn behavior. External source interface v1.0.0 now previews and explicitly trusted-builds an exact canonical detached revision with pinned/locked restore, owned versioned staging, native/resource/hash/receipt and bounded static-child verification, non-destructive retries and no-build reruns. Finish immutable tool distribution/channel resolution and separately approved protected activation/installation/data-retention recovery. Silent old-BA related-bundle upgrades and same-numeric-version beta ordering remain unsupported; retain external uninstall/reinstall guidance. | Build-only output explicitly reports activation unavailable, never installs/launches/registers/elevates. Actual protection/unprivileged launch and lifecycle/runtime trials remain required, preserving local edits/earlier output/user data. Binary users never build/restore; local-source provenance grants no activation or official publication authority. Re-probe optional setup without removing unowned dependencies; startup/completion remain separate from ownership/voice/storage admission. |
| R17-D02 - Integrate Linux-first builds and Windows WiX packaging; automation implemented, CI/release qualification open | Release engineering lead | Shared GitVersion resolves feature 0.1.0, main betaN and stable-tag versions. Configured Linux build/test/cross-publish transfers exact x64 payloads to Windows WiX packaging; canonical main/tag releases include notes/hashes/manifests, serialized non-overwriting publication and early already-published checks. Feature/PR setup is built but not published and has no release-write identity. Require full non-skipped ICE, licence/coverage/tests and per-release native/tool/notice review; migrate remaining native inspection and justify symbols/import libraries. | Passing real Actions receipts and final hashes must identify one exact revision; configured automation/fake CLI tests are not actual publication. Matching versions are no-ops; mismatches/uncertainty fail visibly. POC release notes disclose unsigned status, architecture/upgrade limitations and residual gates. Production protection/resources/SBOM/native qualification remain separate; source revision is provenance, not host authority. |
| R17-D03 - Accept installed final bytes on Windows; blocked later integration/lab | Windows test lead with release and security leads | Use approved R17-D01/D02 candidates after applicable R03/R09/R10/R11/R16 and reference-environment preparation. Risk-based trials cover runtime-only launch, wrong/missing prerequisites, UAC/SmartScreen, effective protection/native loading, privacy/resources/workers, scope/logon/repair/uninstall/completion and external replacement recovery. Do not mutate a user's installation to obtain evidence. | Attach actual receipts to final setup/payload hashes and observed runtime/native identities. Only passing required installed/resource/worker gates signs off D-005/R19 production acceptance. Approved public unsigned POC releases do not close those gates and do not require exhaustive manual installation of every MSI; R18 remains notify-only. |

Immediate handoff: product/release owners complete D01's release-specific
review; release engineering maintains the implemented binary/CI slice and
integrates the new source fixture/stage gates, publishes reviewed immutable
source tooling and completes separately approved activation/upgrades;
release/security and the lab owner arrange D03
approval. Production design can proceed in parallel, but those assignments
and the later integrated Windows evidence cannot be replaced by passing
fixture tests, configured publication or merging installer source.

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
| R30 - Optional database encryption at rest | Future enhancement; P3; revisit when a suitable supported provider becomes available | R04, R12, R17; maintained, redistributable Windows encryption provider/package | Evaluate packaged authenticated SQLite encryption when a maintained, compatible and licence-acceptable distribution avoids a Kora-owned native build. Before offering opt-in encryption, verify migration/source preservation, OS-protected key custody and key-loss recovery, journals/backups and interrupted operations. Keep private-profile standard SQLite supported; this enhancement does not block R04 or the initial release, select a provider now, authorize a commercial dependency or restore superseded encryption admission gates. |

## Acceptance Checkpoint Mapping

Implementing a foundation earlier does not accept its eventual milestone.
Checkpoint sign-off remains A0, then A1, then A2, then A3, then A4; later testing repeats earlier controls.

| Checkpoint | Current assessment | Roadmap evidence required to close it |
|---|---|---|
| Platform/Gate 0 | Not accepted. Shared projects/CI partial; I18 Node/source-built RT1 retained with hook-only FAIL and historical released-byte parity blocker. Separately approved released-profile RT1 regressions/MG1 pass; no source/released byte equivalence inferred. RT2 all-path admission and PV1 remain open. | Preserve distinct exact profiles or repeat for changed bytes; applicable R02-RT2/R02-PV1, then R03/R04/R05/R08/R11/R17 actual integration/OS/provider/deployment evidence; carry scoped MG1 into production only for admitted model-assisted management |
| A0 deterministic shell | Partial shell/setup/transcription/TTS with implemented held PTT and cross-build ownership plus bounded native trials; clipboard, controlled remote path, streaming and complete native privacy/permission-polling acceptance outstanding | R03-R08 foundation/vertical-slice evidence; no dependence on model-assisted management; native setup/recovery and cancellation measured |
| A1 voice-first activation | Grammar proof, not production wake or complete lock/event policy | R02/R03/R09 actual wake, endpointing, privacy and interruption trials |
| A2 local-first answering | Partial: R02 identity/licence/unavailable-path evidence plus actual production Ollama setup, simple/long answers and cancellation. Full clipboard/streaming, CPU-floor quality/resource/context and independently blocked offline qualification remain open. | R02-L1-L5 candidate/floor disposition and L6 handoff; R06/R07/R08 actual local adapter/clipboard/streaming conformance and independently network-blocked host trials. D-003 and applicable D-007 evidence remain open until owner-reviewed passes; bounded host observations, harness tests and merge do not close them |
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
| Optional database encryption at rest, when a suitable provider becomes available | R30 |
| Evidence/diagnostic viewers | R14; optional R25 |
| Host-only maintenance/proactive events and read-only maintenance snapshots | R17, R18; deferred update authority in R29 |
| Connector discovery/configuration/tools, shared skills and declarative authoring | R20, R21, R22 |
| Voice-profile workflows, knowledge and separately gated future integrations | R24, R26, R28, R29 |

For each delivery, update this inventory, the [decision register](Decision_Register.md), the [technical reference](Tool_And_Skill_Reference.md), the [user guide](../docs/tools-and-built-in-skills.md) and actual advertised schemas together.
Record source/test/provider/hardware evidence and the exact acceptance result, not just a merged PR.
Remove proposed labels only for the admitted behavior actually delivered; preserve unavailable/unknown states and the distinction between current host equivalents and model tools.
Keep dependencies explicit and re-run affected earlier gates when a later capability changes shared authority, storage, audio, egress or resource coordination.
