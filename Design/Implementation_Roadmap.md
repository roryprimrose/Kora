# Implementation Status and Delivery Roadmap

Status: current implementation boundaries and remaining-work navigation,
reviewed against source on 2026-10-28. Implementation is not capability
enablement, completed milestone acceptance or release qualification.

This page owns delivery status and dependencies, not a feature/PR diary.
[Topic owners](README.md#topic-owners) define contracts;
[Acceptance Criteria](Acceptance_Criteria.md#three-tier-qualification-policy)
defines qualification; [Deferred Validation](Deferred_Validation.md) records
outstanding environment evidence. Earlier delivery receipts remain in the
[historical roadmap](Archive/Implementation_Roadmap_2026-10-11.md); current
contracts link maintained implementation and regression evidence.

## Product Realignment and Delivery Priority

Apply [product admission](MVP_Scope.md#product-admission-and-realignment)
before choosing work. Prioritize complete conversation/model turns, shared
setting proposals, governed tool/MCP/skill/agent workflows, voice interaction,
independent conversations and readable results. Available dependencies or
unfinished Rxx packages do not approve unrelated features.

Do not recreate retired utilities, saved-draft management, engineering-only
normal browsing filters or public diagnostic scheduling without a new product
decision. Preparation-only work needs an approved complete interaction;
repairs, accessibility and proportionate usability within retained outcomes
remain valid.

## Delivery and Qualification Dependency Policy

Use the [three-tier policy](Acceptance_Criteria.md#three-tier-qualification-policy).
Unknown authority, consent, privacy, integrity, ownership or resource state
always fails closed.

### Reading Needs

- **I - Implementation:** the consumed production contract/service and its
  directly applicable checks must exist. A bounded subset can satisfy I
  without completing its entire Rxx package.
- **E - Enablement:** exact capability/profile evidence must pass before
  enabling, advertising, packaging as available or materially changing that
  path.
- **Q - RC:** integrated final-byte/environment evidence applies to an explicit
  reviewed enabled-capability manifest and its shared/transitive controls.

Unlabelled package dependencies mean I for the consumed subset, not completion
of every listed package. Hardware, provider, containment, acoustic, installed
and accessibility trials are E/Q for the affected profile, not general
implementation prerequisites. Deterministic seams support implementation;
fakes, merged code and source inclusion never enable an unqualified runtime.

## Approved Speech-Input Direction - 2026-10-22

[Speech Input](Speech_Input.md) owns configurable local wake/activated ASR,
resource profiles and optional bounded personalization. D-002's approved
sherpa-onnx/Silero development direction is not exact-byte admission or
production wake enablement; D-006 learning/verifier and D-007 hardware
qualification remain open.

Implement consumed provider/settings/generation/quiescence seams without
waiting for unrelated physical trials. Optional learning is not a baseline
voice dependency or a blanket owner-only command policy. Qualified optional
components must share WiX/Burn and runtime Settings setup services, keeping
application payload, model data and input consent distinct.

## Completed product realignment

Native calculator/clock/clipboard-analysis utilities, saved-draft document
management, engineering-only normal browsing predicates and public diagnostic
scheduling have been removed. Unsent input is per-session memory-only composer
state with read-only legacy recovery and explicit shutdown protection.
Ordinary submitted-date/task-state and interaction-receipt filters remain.

Internal scheduler/domain/schema/audit compatibility, safe retained-work
controls, Evidence and exact-grant diagnostics remain where consumed.
Historical data is not renewed execution authority. See
[scope](MVP_Scope.md#product-admission-and-realignment) and
[composer/recovery](Interaction_And_Sessions.md#conversation-input-realignment)
for the current contracts, not retired implementation slices.

## Current Delivery

These are implemented boundaries, **not an enabled-capability manifest**.
[User commands](../docs/commands.md) and [settings](../docs/settings.md) own
usable syntax and controls; [Internal Model Tools](Internal_Model_Tools.md)
owns descriptors and model exposure.

| Area | Implemented boundary | Remaining or unavailable | Contract / implementation owner |
|---|---|---|---|
| Desktop and routing | Windows Avalonia shell, presence/tray, typed and activated/PTT routes, native recovery; span-ended hands-free capture implementation | No admitted native wake profile; acoustic and complete installed/product acceptance remain open | [Architecture](Architecture.md), [Speech Input](Speech_Input.md) |
| Model execution | Provider-neutral admission, bounded tool/result continuation, native Ollama transport and exact-session retained answers/questions; tool-free provisional local answers | No bundled accepted local-model attestation; deterministic execution evidence does not qualify a production profile | [Tool loop](Internal_Model_Tools.md#bounded-provider-neutral-continuation-implementation---2026-10-20), [local inference](Local_Inference.md) |
| Hosted execution | Maintained Copilot SDK and native Claude text/stream boundaries with independent account/deployment and physical-send review | Shipped Copilot/Claude deployment approval sources are unavailable; account/profile/egress qualification and Codex integration remain separate | [Runtime profiles](Runtime_Provider_Feasibility.md) |
| Discovery and actions | Six-ID read-only registry; separately reviewed session/customization metadata modes; shared admitted clipboard/file/network actions | No automatic exposure of native actions, arbitrary tools or consequential workers | [Catalogue](Internal_Model_Tools.md), [C# actions](../src/Kora.Tools/README.md) |
| Authority and storage | Private standard SQLite, typed task/question/grant/audit transactions, no-replay recovery, exact native grant inspection/revocation | General worker/effect admission, audit anchors/pruning/whole-store rollback and managed-copy/installed qualification | [Security](Security_Data_Flows.md), [storage gates](Deferred_Validation.md#storage-admission-follow-up) |
| Conversations and lifecycle | Durable USER/ASSISTANT/HOST records, session-local unsent input, retained-message search/reuse, reviewed context, pending/running cancellation, reviewed Stop all work and application Exit/Restart | Qualified runtime execution, arbitrary effects and full conversation/artifact/copy lifecycle | [Sessions](Interaction_And_Sessions.md), [work](Work_Management.md) |
| Workspace and evidence | Adaptive session/conversation window, bounded relatedness/destination review, integrated passive text/Markdown Details, origin-aware return; bounded SQLite/log/audit inspection | General result routing, Ask Evidence/model retrieval/export, Mermaid/HTML/browser profiles and full installed accessibility | [Workspace](UI_Workspace_And_Windows.md), [display](Information_Display.md) |
| Preferences and audio | Shared admitted native/exact settings, retained registered-setting proposals, consented activated capture, local speech and visual recovery, manual call protection; Windows communications-observation seam | Exact automatic call profile, production wake, broader setup/scopes and acoustic qualification | [Configuration](User_Configuration.md), [calls](Call_Aware_Speech.md), [setup](Environment_Setup.md) |
| Files and clipboard | Immutable previews/retrieval, reviewed refresh, retained session attachment, named folders with opt-in bounded descendants, citations and native Find; reviewed private-LocalOnly file/folder answer admission and permission-granted clipboard explanation | Model qualification, named-source model use, multi-version/indexed ingestion, OCR/screen capture and hosted grounding remain gated | [Named folders](File_And_Folder_Ingestion.md#named-device-local-folder-lifecycle), [source-use risk policy](Security_Data_Flows.md#reviewed-localonly-source-use-risk-admission), [clipboard policy](Security_Data_Flows.md#delivered-r07-local-clipboard-preview---2026-10-07) |
| Memory and provider policy | Reviewed session-only memory and content-free filters, explicit reviewed context use, app profiles and durable sparse session overrides/copy | No automatic recall, cross-session/global memory, implicit history assembly, automatic handoff or authority transfer | [Memory/providers](Model_Providers_Memory_And_Knowledge.md) |
| Definitions and agents | Categorized Customizations editing/reload/exclusions, reviewed source writes and local-model authoring proposals; bounded declarative skill/local-agent invocation and fixed bundled PowerShell execution gate | No qualified installed contained worker profile; arbitrary execution, automatic activation/applicability and provider qualification remain unavailable | [Management](Definition_Management.md), [invocation](Commands_Tools_And_Skills.md), [script admission](Built_In_Skills.md#script-admission-and-execution) |
| MCP | Native-only Kora HTTPS entry editing saved disabled/excluded; reviewed read-only host/transport path, explicit session requests and governed result follow-up | Editing never connects, authenticates or invokes; no qualified production accounts/effect mappings by default, arbitrary server execution, remote write capability or MCP export | [Entry editor](Definition_Management.md#bounded-kora-owned-https-mcp-entry-editing), [execution profile](Commands_Tools_And_Skills.md#first-https-read-only-execution-profile) |
| Captions and notices | Off-default playback-bound captions, run-only pin/display, SAPI source-range sentence timeline, trusted visual notices, routine quiet mode and run-consented conversation completion speech | Sentence-profile qualification, word highlighting, general rich rendering, other proactive speech and broad reminder scheduling remain open | [Caption implementation and gates](Information_Display.md#optional-speech-text), [completion speech](Proactive_Interaction.md#run-consented-conversation-completion-speech) |
| Distribution | MSI/custom Burn packaging, release/source-tool automation and reviewed build-only source workflow | Installed/protected-byte/runtime acceptance, source activation and install-capable updater | [Distribution](Distribution_And_Updates.md), [installer](../installer/README.md) |

[Exact-session permission policy and native controls](Session_Permission_Levels.md#delivered-bounded-implementation)
are implemented; production Model review has no qualified reviewer and requires
user approval. Broader execution profiles remain independently gated.
Definitions, model recommendations, configuration choices and retained records
never confer execution, session, approval or transmission authority.

## Ordered Outstanding Work

Rxx identifiers remain stable navigation for existing contracts, not mandatory
whole-package prerequisites or an instruction to extend every partial feature.
Choose an approved useful increment and its minimum consumed I subset.
Topic contracts and [original completion conditions](Archive/Implementation_Roadmap_2026-10-11.md#ordered-outstanding-work)
retain requirements not repeated here.

| ID | Remaining focus | Consumed dependencies / gate |
|---|---|---|
| R01 | Maintain policy/scope consistency | Product admission |
| R02 | Qualify each offered feasibility profile independently | E/Q for that profile; see follow-up gates below |
| R03 | Close native ownership/privacy/device and capture/output evidence | R01; affected native/audio E |
| R04 | Broader migrations, managed artifacts/backups, audit anchors/pruning/rollback and copy lifecycle | R03; D-009 standard-SQLite baseline |
| R05 | Generic exact review, foreground voice targeting and pre-effect worker/adapter admission | R03/R04 |
| R06 | Qualify production registry adapters/tool-result execution beyond implemented bounded continuation | R04/R05; E: exact runtime profile |
| R07 | Qualify permission-granted clipboard explanation; remaining approved source/context contracts | Consumed R03/R04/R05/R06; E: qualified local runtime |
| R08 | Qualify hosted/local consumers and extend admitted streaming/late-output integration | R04/R05/R06; R07 only when clipboard is consumed; E: exact local or hosted profile |
| R09 | Production wake/endpointing and packaged acoustic lifecycle | R03/R05; R06 only for model interpretation; E: offered speech profile |
| R10 | Remaining typed settings, scoped setup and qualified consumers | R04/R05; R06/R09 only where consumed |
| R11 | Qualify the fixed bundled script worker and complete approved containment, distinct from declarative skill use | R03/R04/R05/R06/R10; E: W1-W4/D-013 |
| R12 | Complete conversation/artifact routing and recoverable-copy lifecycle | R04/R05/R10; bounded R13; R11 only for executing consumers |
| R13 | Qualified resource-leased concurrency and optional model-assisted management | R05/R06/R10/R12; R08 for model assistance; E: actual budgets/MG1/PV1 |
| R14 | Complete coordinated workspace/question/review and accessibility matrix | R05/R12/R13 and consumed retrieval/retention; R09 for remaining voice UX |
| R15 | Qualify automatic call observations and remaining exact feedback/origin controls | R03/R05/R09/R10/R13/R14 as consumed |
| R16 | Protected OS power and complete all-session lifecycle beyond reviewed application Exit/Restart | R05/R11/R12/R13/R15 as consumed |
| R17 | Supported startup and final installed/protected distribution evidence | R03/R09/R10/R11/R16 as packaged; E/Q: delivered profile |
| R18 | Trusted proactive/reminder integration beyond bounded local notices | R05/R10/R13/R14/R15/R17 as consumed |
| R19 | Manifest-scoped RC qualification and truthful Slice A reporting | Q: included R07-R18 and shared/transitive controls; not an I prerequisite |
| R20 | Qualify a selected read-only MCP connector/account and approved integration workflow | Existing host/transport seam; R06/R10/R11 only where consumed; E: connector profile |
| R21 | Complete shared-source enablement/invocation/reload and dependent-revision handling | R05/R06/R10; R11 for executing consumers; E: source/format profile |
| R22 | Broader approved conversational/package authoring and applicability beyond current editors/refinement | R05/R10/R12/R14 and consumed R20/R21; changed revisions remain disabled |
| R23 | Slice B/C and manifest-scoped release regression | Q: R19 and included R20-R22 scope |

[Definition Management](Definition_Management.md) owns source/editor/reload
contracts; [Skill Authoring](Skill_Authoring.md) owns single-file and package
authoring rules. Shared read consent does not expand into arbitrary profile
editing or generic filesystem permission. Merging an implementation does not
schedule a live trial or open an E/Q gate.

### R02 Local-Inference Continuation

Ollama 0.35.1/qwen3:1.7b and the Ollama 0.40.2 comparison are rejected.
Do not repeat hardware/offline/human trials for unchanged rejected candidates.
Continue host/UI/orchestration implementation with explicit unavailable behavior.

A materially changed candidate must pass the
[future candidate gate](Local_Inference.md#future-candidate-gate): automated
quality/authority, approved identity/licence, reference hardware/resources,
context/cancellation, offline/egress and integrated-host qualification. This
gates that candidate's enablement and release claims, not unrelated work.

## R02 Runtime/Provider Follow-Up Gates

| Gate | Current disposition | Remaining evidence |
|---|---|---|
| RT1 | Scoped source-built and separately approved released-profile control-point evidence; identities remain distinct | Repeat exact-byte final-request/all-status/pre-effect controls on affected profile changes; retain reproduction limitations |
| RT2 | Bounded observations pass; all-path admission remains Blocked | Attributable native file/destination/diagnostic/lifecycle coverage, loss controls and actual mediation/prevention |
| MG1 | Separately approved released-profile host envelope passes | Exact host authority/leases, applicable RT2 and account budgets; no physical-stop claim |
| PV1 | Live account/provider trial not approved or run | Explicit account/terms/model/region/spend approval; actual authentication, isolation, quota/cost and cancellation |

Use the [runtime plan](Runtime_Provider_Feasibility.md) and
[validation register](Deferred_Validation.md#runtimeprovider-follow-up).
Historical Copilot proof does not qualify a maintained adapter, native Claude,
Codex or another account/profile. Synthetic controls never enable production.

### R02 Windows Containment Follow-Up

W1-W4 require reviewed worker/dependency profiles, actual token/ACL/network/child
boundaries, fixed-action adapter behavior and cancellation/quiescence. W2's
rejected dependency mechanisms remain rejected; best-effort tracking is not
containment. Follow the [proof and gates](../experiments/r02-containment-proof/README.md).

### R02 Storage/Key Outcome and Follow-On Work

D-009 selects standard SQLite under supplied profile paths with verified
private permissions and readable-copy disclosure. Mandatory encryption/key/
rekey/native-codec admission is superseded. R04/R12 retain migration, recovery,
managed-copy deletion, audit and installed/power-loss obligations in the
[storage register](Deferred_Validation.md#storage-admission-follow-up).

### R04 Foundation Delivery

Composed task/question/grant/audit/evidence stores and no-replay recovery
establish host attribution, not general effect execution. [Architecture](Architecture.md)
and [Security](Security_Data_Flows.md) own the current contracts; the
[foundation receipt](Archive/Implementation_Roadmap_2026-10-11.md#r04-foundation-delivery)
preserves the original implementation inventory.

### R02 Distribution Follow-Up and R17 Delivery

Maintained source/publish/installer tooling replaces retired distribution
executables. Unsigned publication/static checks do not qualify installed
protection, native loading or runtime-only acceptance. Follow
[Distribution](Distribution_And_Updates.md) and [installer qualification](../installer/README.md).

## Optional and Deferred Work

| ID | Scope and gate |
|---|---|
| R24 | Frequent-speaker learning/enrolled verification: separate consent, private storage and playback/drift/privacy proof; not authentication by default |
| R25 | Qualified caption alignment/rich rendering beyond existing utterance captions |
| R26 | Managed file/folder/image/screen knowledge and qualified grounded model use beyond admitted immutable retrieval; source/egress/lifecycle proof |
| R27 | General executable imports/application launch: rollback decision, immutable dependency/resource review and containment |
| R28 | Write-capable connectors/Git/desktop automation: per-domain identity/resource and uncertain-write recovery; no self-modification |
| R29 | MCP export, install-capable updates and executable/render extensions: explicit scope, disabled defaults and dedicated admission |
| R30 | Optional supported database encryption: compatibility/licence/key/recovery/servicing review; never an R04 prerequisite |

## Experiment Disposition Inventory

Experiments are opt-in evidence, not production contracts or open-ended I
prerequisites. Maintained deterministic tests own affected implementation
regressions; physical/lab trials apply when considering a materially changed
profile for enablement. This cleanup retires no experiment executable or
historical evidence.

| Harness | Disposition and rerun boundary |
|---|---|
| [Local inference](../experiments/r02-local-inference-proof/README.md) | Retain rejected candidates and boundary evidence; reopen physical/offline trials only for a materially changed candidate intended for enablement |
| [Containment](../experiments/r02-containment-proof/README.md) | Retain ACL/no-child/unrestricted-script rejection; implement the typed broker/admitted-worker replacement before its separate consequential-profile qualification |
| [Speech](../experiments/r02-speech-proof/README.md) | Retain acoustic, playback-rejection and packaged privacy/timing evidence for affected speech/wake/provider/device profiles |
| [Node runtime](../experiments/r02-runtime-proof/README.md) | Retain hook-only rejection; migrate all-status/denial/cancellation/serialization assertions only with exact maintained equivalence; Node never qualifies .NET |
| [RT2 lifecycle](../experiments/r02-runtime-lifecycle-proof/README.md) | Retain native procedures, observer limitations and lifecycle/loss/quiescence cases; maintain coverage or explicitly exclude the unsupported profile |
| [Storage](../experiments/r02-storage-proof/README.md) | Maintained standard-SQLite recovery/retention tests own current development; SQLCipher/x86 is historical; reopen encryption only for R30, not R04/R12 |
| [RT1 control](../experiments/r02-dotnet-control-proof/README.md) | Exact production equivalents/failure witnesses required for final-request/all-status/denial/cancellation migration; repeat evidence for changed mechanisms/bytes |
| [MG1 management](../experiments/r02-dotnet-management-proof/README.md) | Exact equivalents required for envelope/admission/no-retry/Unknown quarantine; account/cost/native gaps remain E/Q |
| [Distribution](../experiments/r02-distribution-proof/README.md) | Retain receipts; executables already retired; rerun affected changed mechanisms only; NSIS does not qualify WiX |
| [W2 dependencies](../experiments/r02-w2-dependency-proof/README.md) | Retain rejection/decision receipts and shared consumers; reopen changed dependency/worker mechanisms; W1/W3/W4 remain separate |

Before executable retirement, map every applicable assertion and mandatory
failure witness to maintained owners/tests, execute exact equivalence checks
and inspect all consumers. Partial equivalence or live shared consumers means
retain. The [detailed historical inventory](Archive/Implementation_Roadmap_2026-10-11.md#experiment-disposition-inventory)
preserves migration conditions.

### Cost Versus Unique Risk Reduction

Reuse valid exact-profile receipts and repeat affected evidence after material
changes. Neither duplicate prototype suites nor stale receipts replace current
checks or unique native/provider/hardware/installed evidence.

## Acceptance Checkpoint Mapping

A0-A4/B/C retain their complete outcomes in [MVP Scope](MVP_Scope.md) and
[Acceptance Criteria](Acceptance_Criteria.md). An RC can explicitly exclude
unfinished capabilities, but cannot claim the complete milestone. Missing or
failed mandatory evidence blocks the included profile, not unrelated work.

## Capability Coverage and Keeping the Roadmap Current

Update the current area and remaining-work row when their boundary changes.
Keep contracts, exact syntax and qualification in their owners. Store exact
revisions, PR/test receipts, failed attempts and native trials in evidence
records rather than appending delivery narratives here. Historical receipts
never become current authority merely by being linked.
