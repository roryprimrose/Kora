# Implementation Status and Delivery Roadmap

Status: live delivery inventory and remaining work, reviewed against main
`e93a927` on 2026-10-11. Bounded delivery is not complete capability, milestone
or release qualification. Detailed dated receipts are preserved in the
[historical roadmap](Archive/Implementation_Roadmap_2026-10-11.md).

This document owns delivery status and dependencies. Topic documents own
contracts; [Acceptance Criteria](Acceptance_Criteria.md) owns qualification;
[Deferred Validation](Deferred_Validation.md) owns outstanding environment
trials. Update those owners rather than copying status narratives between them.

## Delivery and Qualification Dependency Policy

Follow the [three-tier qualification policy](Acceptance_Criteria.md#three-tier-qualification-policy):
directly applicable checks gate a change, exact-profile evidence gates
capability exposure, and an explicit enabled-capability manifest gates an RC.
Unrelated experiments do not block normal feature development. Unknown
authority, consent, privacy, integrity or resource state still fails closed.

### Reading Needs

- **I - Implementation:** the consumed contract/service and its affected checks
  must exist. A bounded subset can satisfy I without completing its whole Rxx.
- **E - Enablement:** exact capability/profile evidence must pass before enabling,
  advertising, packaging as available or materially changing that path.
- **Q - RC:** integrated final-byte/environment evidence applies to the reviewed
  enabled-capability manifest, not unrelated feature work.

Unlabelled package dependencies are I for the consumed subset. Hardware,
provider, containment and installed trials are E/Q for their affected profile.
Clipboard preview does not depend on model explanation, deterministic queues
do not depend on hosted management, and speech preferences do not qualify wake.

## Current Delivery

The [exact command reference](../docs/commands.md) and
[settings reference](../docs/settings.md) describe usable routes. The
[canonical tool catalogue](Internal_Model_Tools.md) separately labels model
descriptors; native delivery does not make a model tool available.

| Area | Delivered boundary | Still unavailable or unqualified | Contract |
|---|---|---|---|
| Desktop and exact routing | Windows Avalonia shell, presence, tray, typed/PTT routing and native recovery | Production wake, full product/installed acceptance | [Architecture](Architecture.md), [Task lifecycle](Task_Lifecycle.md) |
| Local reasoning | Consented pinned Ollama/model setup, verified loopback bootstrap, bounded answers/questions/action proposals | Generic tool/result iteration, CPU-floor/context/offline qualification and physical server-stop proof | [Local inference](Local_Inference.md), [model inventory](Internal_Model_Tools.md#implemented-bootstrap-surface) |
| Discovery and actions | Six-ID read-only registry; shared C# clipboard/file/network actions and bounded exact/native workflows | Qualified generic model adapter; parameterized web model route | [Tools](../src/Kora.Tools/README.md), [catalogue](Internal_Model_Tools.md) |
| Authority and storage | Private standard SQLite, typed task/question/grant/audit transactions, no-replay recovery, exact native grant inspection/revocation | General worker/effect admission, audit anchors/pruning/whole-store rollback and managed-copy/installed qualification | [Security](Security_Data_Flows.md), [grant controls](User_Configuration.md) |
| Sessions and work | Durable IDs/names, exact create/rename/lifecycle/task controls, ordered bounded host history, retention/Keep review, four fixed no-argument metadata queue profiles with retained page results, exact current-run pause and question recovery | Full conversation execution, arbitrary queues/effects, resource-leased concurrent workers and model session tools | [Sessions](Interaction_And_Sessions.md), [workspace](UI_Workspace_And_Windows.md), [work](Work_Management.md) |
| Native workspace and evidence | Selected work and passive cross-session attention overview, metadata/history search, bounded SQLite/DailyLog/CombinedLog/AuthorityAudit inspection and native filters | General Ask Evidence/model retrieval/export; full screen-reader/readability/physical-DPI acceptance | [Workspace](UI_Workspace_And_Windows.md), [display](Information_Display.md) |
| Preferences and audio | Admitted native/exact preferences, consented PTT, installed/local speech, visual recovery and run-only manual call protection | Automatic call detectors, call-policy downgrades, temporary speech exceptions and acoustic qualification | [Configuration](User_Configuration.md), [call policy](Call_Aware_Speech.md) |
| File/clipboard inspection | Explicit immutable previews, folder lexical retrieval, reviewed refresh, one retained file/session, replacement/removal, metadata catalogue, opt-in Markdown structure and exact citation navigation | Managed knowledge sources, model grounding/egress, persistent/vector indexes, screen/OCR/image context | [Ingestion](File_And_Folder_Ingestion.md), [clipboard](Security_Data_Flows.md) |
| Memory and providers | Original-user memory review/admission and session-only durable storage; host policy/handoff, initial mode, audited app profiles and durable exact-session sparse overrides with native/exact review and subsequent-turn capture | Authentication/qualified hosted adapters, generic/one-turn profile directives, model memory recall or automatic handoff | [Profiles and session resolution](Model_Providers_Memory_And_Knowledge.md#session-overrides-and-effective-resolution) |
| Definitions | Fixed immutable bundled packages, explicit artifact instruction selection, bounded shared-profile inspection and native single-file owned-instruction editing with optimistic conflicts and separate exact-revision use review | Profile/other-kind/conversational editors, script execution, agent runtime and full common-management qualification | [Definitions](../src/Kora.Definitions/README.md), [source storage](Skill_Storage.md), [authoring](Skill_Authoring.md), [editor](../docs/instruction-editor.md) |
| Captions and local events | Off-default playback-bound captions, run-only pin/display choice; trusted visual notices, cached maintenance and routine quiet mode | Sentence alignment, general rich rendering, proactive speech and broad reminder scheduling | [Display](Information_Display.md), [proactive interaction](Proactive_Interaction.md) |
| Distribution | MSI/custom Burn packaging, release/source-tool automation and reviewed build-only source workflow | Full installed/protected-byte/runtime qualification and install-capable updater | [Distribution](Distribution_And_Updates.md), [installer](../installer/README.md) |

Session permission levels and expanded skill/instruction authoring are
[proposed contracts](Session_Permission_Levels.md), not new runtime authority.
Existing exact lock behavior and non-executing power proposals are unchanged.

## Ordered Outstanding Work

These are work packages, not tool IDs. Preserve the original detailed
[completion conditions](Archive/Implementation_Roadmap_2026-10-11.md#ordered-outstanding-work)
when planning an increment; the table identifies the next gap, not a waiver
of the topic contract or acceptance fixtures.

| ID | Next completion gap | Implementation dependencies |
|---|---|---|
| R01 | Policy reconciliation accepted; maintain consistency as scope changes | None |
| R02 | Qualify each offered feasibility profile independently | R01; E/Q only for the affected profile |
| R03 | Complete native ownership/privacy/device and capture/output closure evidence | R01 |
| R04 | Broader migrations, managed artifacts/backups, audit anchors/pruning/rollback and copy lifecycle | R01, R03; approved D-009 standard-SQLite baseline |
| R05 | Generic exact review, foreground voice targeting and shared pre-effect worker/adapter admission | R03, R04 |
| R06 | Qualified registry adapters and admitted model/tool/result continuation | Consumed R04/R05; E: local/runtime envelope |
| R07 | Approved clipboard explanation, secret/context budgeting and real offline answering | R03/R04/R05; R06 for explanation only |
| R08 | Controlled remote adapter and streaming/late-output integration | R04/R05/R06/R07; E: applicable RT1/RT2/PV1 or local profile |
| R09 | Production wake/endpointing and packaged acoustic lifecycle | R03/R05/R06; E: affected speech profile |
| R10 | Remaining typed settings, scoped setup and qualified consumers | Consumed R04/R05/R06/R09; E only where setup/profile claims need it |
| R11 | Admitted embedded multi-script runner and containment | R03/R04/R05/R06/R10; E: W1-W4/D-013 |
| R12 | Full conversation/artifact lifecycle, routing and recoverable-copy deletion | Consumed R04/R05/R10 and bounded R13; R11 only for executing consumers |
| R13 | Resource-leased concurrent execution and optional model-assisted management | R05/R06/R10/R12; R08 only for model assistance; E: actual budgets/MG1/PV1 |
| R14 | Full coordinated workspace, exact questions/review and native accessibility | Consumed R05/R12/R13 and delivered retrieval/retention; R09 for remaining voice UX |
| R15 | Automatic call evidence and remaining exact feedback/origin controls | R03/R05/R09/R10/R13/R14 |
| R16 | Graceful protected power and all-session application lifecycle | R01/R05/R11/R12/R13/R15 |
| R17 | Supported startup and final installed/protected distribution evidence | Consumed R03/R09/R10/R11/R16; E/Q: delivered distribution profile |
| R18 | Trusted proactive/reminder integration beyond bounded local notices | R05/R10/R13/R14/R15/R17 |
| R19 | Manifest-scoped RC qualification and truthful Slice A reporting | Q: included R07-R18 and shared/transitive controls; not an I prerequisite |
| R20 | Select and admit one read-only MCP connector and bundled integration skill | R06/R10/R11; E: connector profile |
| R21 | Shared-source enablement/invocation and bounded common reload across all supported definition kinds; reconcile owned/profile files, UI/voice lookup, and dependent revisions without automatically activating changes | R05/R06/R10/R11; E: source/format profile |
| R22 | Bounded owned-instruction native editor delivered; next: profile/other-kind formats, conversational/refinement channels, forks and expanded applicability through the same management contract. Existing exact save/reload/conflict/revision review does not qualify the full matrix or agent runtime | R05/R10/R12/R14/R20/R21 |
| R23 | Slice B/C and manifest-scoped release regression | Q: R19 and included R20-R22 scope |

Partial packages may progress in parallel once their consumed I subset exists.
Do not schedule live trials merely because a implementation dependency merged.

[Definition Management](Definition_Management.md) owns the expanded R21/R22
source/editor/reload contract; [authoring](Skill_Authoring.md) retains the
single-file instruction experience and skill package checks. Implement formats
incrementally. Current R21 read consent is not silently expanded to editor
scope, and profile editing never becomes a generic filesystem/tool permission.

### R02 Local-Inference Continuation

| Step | Remaining action |
|---|---|
| L1 | Approve operator, reference CPU/8-logical-core/16-GiB/SSD floor, isolation and numerical budgets |
| L2 | Separately consent to pinned provisioning and record exact licence/digest/staging-volume facts |
| L3 | Run at least 30 cold and 30 paired warm trials, human quality review, context/resource/cancellation/recovery measurements |
| L4 | Independently enforce and observe remote-egress denial with loopback retained; may run beside L3 after L2 |
| L5 | Record candidate/hardware disposition and D-003/D-007 evidence after L3/L4 |
| L6 | Apply the qualified envelope to dependent host workflows and repeat integrated evidence |

Retain the pinned candidate for trials; do not substitute cloud or lower the
floor silently. The [technical plan](Local_Inference.md) and
[LI01-LI07 procedure](../experiments/r02-local-inference-proof/README.md#outstanding-testing-checklist)
own the details.

## R13 exact current-run queue pause receipt - 2026-10-11

The [fixed local-version increment](Work_Management.md#delivered-exact-current-run-dispatch-pause---2026-10-11) adds exact native/original typed/activated Pause/Resume and observational Work/All work/status.
Pause shares the physical atomic admission fence, skips only the addressed session and lets committed callbacks drain; Resume never dispatches or replays.
Captured limits/deadlines/FIFO/fairness, private epochs, Unknown quarantine and meaningful-activity retention remain authoritative.
Deterministic domain/parser tests and genuine private-SQLite commit/callback races cover this bounded control, not MG1 inference or general workers/providers/leases. The enabled consumer closure is queue admission, Work/All work, local event eligibility and native/exact command routing; question waits, grants, captions, quiet mode, memory and provider/definition authorities are unchanged.
Applicable MG1 envelope/no-retry/Unknown assertions remain in maintained queue tests; RT1 final-request/all-status controls and RT2 native mediation/loss/quiescence, containment W1-W4, hardware/audio/accessibility and historical storage/recovery evidence are retained.
No experiment executable or shared consumer is retired: exact runtime/profile/native equivalence has not been established by these deterministic tests.
Full R13/A3, installed-native qualification and physical-stop evidence remain open under the three-tier policy.

## R02 Runtime/Provider Follow-Up Gates

| Gate | Current disposition | Next required evidence |
|---|---|---|
| RT1 | Scoped source-built and separately approved released-profile control-point evidence; identities remain distinct | Repeat exact-byte final-request/all-status/pre-effect controls on profile changes; source reproduction limitations remain recorded |
| RT2 | Bounded observations pass; all-path admission remains Blocked | Attributable native file/destination/diagnostic/lifecycle coverage with loss controls and actual mediation/prevention |
| MG1 | Separately approved released-profile host envelope passes | Integrate exact host authority/leases and repeat applicable RT2 and account budgets; no physical-stop claim |
| PV1 | Live account/provider trial not approved or run | Explicit account/terms/model/region/spend approval and actual auth, isolation, quota/cost and cancellation evidence |

Use the [runtime plan](Runtime_Provider_Feasibility.md) and
[validation register](Deferred_Validation.md#runtimeprovider-follow-up).
Passing synthetic controls never enables an unqualified production adapter.

### R02 Windows Containment Follow-Up

W1-W4 require reviewed worker/dependency profiles, real token/ACL/network/child
boundaries, fixed-action adapter behavior and truthful cancellation/quiescence.
Strict dependency mechanisms rejected by W2 remain rejected; best-effort
tracking does not relax runtime privacy or certify containment. Follow the
[retained proof and gates](../experiments/r02-containment-proof/README.md).

### R02 Storage/Key Outcome and Follow-On Work

D-009 selects standard SQLite under supplied profile paths with verified
private permissions and readable-copy disclosure. Mandatory encryption/key/
rekey/native-codec admission is superseded. Remaining migration, recovery,
managed-copy deletion, audit and installed/power-loss evidence belongs to
R04/R12 and the [storage register](Deferred_Validation.md#storage-admission-follow-up).

### R04 Foundation Delivery

The bounded composed task/question/grant/audit/evidence stores and no-replay
recovery establish exact host attribution, not general effect execution.
See [architecture](Architecture.md), [security](Security_Data_Flows.md) and the
[historical foundation inventory](Archive/Implementation_Roadmap_2026-10-11.md#r04-foundation-delivery)
for exact implementation and original receipts.

### R02 Distribution Follow-Up and R17 Delivery

Maintained source, publish and installer tooling replaces retired distribution
executables. Final installed protection/native loading and runtime-only
acceptance remain separate from unsigned publication/static checks. Follow
[distribution](Distribution_And_Updates.md) and [installer qualification](../installer/README.md).

## Optional and Deferred Work

| ID | Scope and gate |
|---|---|
| R24 | Frequent-speaker learning/enrolled verification: separate consent, private storage and playback/drift/privacy proof; not authentication by default |
| R25 | Extend bounded captions to qualified alignment/rich rendering; existing utterance captions do not qualify it |
| R26 | Managed file/folder/image/screen knowledge and grounded model use beyond admitted immutable local retrieval; separate source/egress/lifecycle proof |
| R27 | General executable imports/application launch: rollback decision, complete immutable dependency/resource review and real containment |
| R28 | Write-capable connectors/Git/desktop automation: per-domain identity, resource and uncertain-write recovery; no self-modification |
| R29 | MCP export, install-capable updates and executable/render extensions: explicit scope, disabled defaults and dedicated admission |
| R30 | Optional supported database encryption: compatibility/licence/key/recovery/servicing review; never restore it as an R04 prerequisite |

## Experiment Disposition Inventory

The [app profile](Model_Providers_Memory_And_Knowledge.md#delivered-bounded-app-provider-profile-configuration) and [durable exact-session sparse override](Model_Providers_Memory_And_Knowledge.md#session-overrides-and-effective-resolution) increments consume existing provider-neutral host mode/turn seams without adapter/auth/network execution. Native/exact review, private SQLite policy retention/deletion and immutable subsequent-turn tuple capture do not reroute the buffered local bootstrap reasoner.
Deterministic exact tuple/configuration and subsequent-turn tests are not assertion-equivalent to SDK-native RT1 bytes, RT2 lifecycle/egress, PV1 account, local-quality/offline or native/hardware procedures. Runtime preparation consumers and all unique proof/receipt assets remain retained; no experiment or release qualification is removed.

Experiments are opt-in evidence, not default-CI suites or production contracts.
This documentation consolidation archives narratives only; **no experiment
executable or historical evidence is deleted, modified or relabelled**.

| Harness | Disposition | Rerun or retirement boundary |
|---|---|---|
| [Local inference](../experiments/r02-local-inference-proof/README.md) | Retain actual quality, CPU-floor/context/resources, server cessation and independent offline trials | Offered or materially changed inference profiles; migrate reusable deterministic cases, not unique physical/offline proof |
| [Containment](../experiments/r02-containment-proof/README.md) | Retain actual token/ACL/child/network/lifetime enforcement | Enabled/changed worker profiles with separate lab approval; timeouts are not denial |
| [Speech](../experiments/r02-speech-proof/README.md) | Retain physical acoustics, playback rejection and packaged capture/privacy timing | Affected speech/wake/provider/device profiles, not unrelated preferences |
| [Node runtime](../experiments/r02-runtime-proof/README.md) | Migrate all-status mediation, denial/cancellation/serialization assertions; retain rejected hook-only witnesses | Archive executable only after exact maintained equivalence and explicit candidate disposition; Node never qualifies .NET |
| [RT2 lifecycle](../experiments/r02-runtime-lifecycle-proof/README.md) | Migrate lifecycle/loss/quiescence cases; retain native procedures and observer limitations | Required native coverage must be maintained or unsupported profile explicitly excluded |
| [Storage](../experiments/r02-storage-proof/README.md) | Migrate applicable standard-SQLite recovery/migration/capacity/retention/copy cases | Partial equivalence does not retire shared crypto/native consumers; superseded crypto is historical unless R30 is pursued |
| [RT1 control](../experiments/r02-dotnet-control-proof/README.md) | Migrate exact-profile final-request, all-status, denial and cancellation cases | Exact production equivalents and failure witnesses required; changed bytes/mechanisms repeat affected evidence |
| [MG1 management](../experiments/r02-dotnet-management-proof/README.md) | Migrate envelope, independent admission, no-retry and Unknown/quarantine cases | Exact equivalents required; account/cost/native gaps remain E/Q, not local-core dependencies |
| [Distribution](../experiments/r02-distribution-proof/README.md) | Retain historical receipts; superseded executables already retired | Reopen only on mechanism changes; old NSIS evidence never qualifies WiX |
| [W2 dependencies](../experiments/r02-w2-dependency-proof/README.md) | Retain historical rejection/decision receipts and remaining shared code | Reopen changed discovery/helper/runtime/worker mechanisms; W1/W3/W4 remain separate |

Before executable retirement, map every applicable assertion and mandatory
failure witness to its maintained owner/test, run exact equivalence checks and
check all build/script/document consumers. Partial equivalence or live shared
consumers means retain, not delete. The
[original detailed inventory](Archive/Implementation_Roadmap_2026-10-11.md#experiment-disposition-inventory)
preserves the migration conditions.

### Cost Versus Unique Risk Reduction

Use maintained deterministic tests for affected regressions, and opt-in lab
proof for unique native/hardware/provider/installed risk. Reuse valid exact-
profile receipts; repeat affected evidence after material changes. Neither
duplicate prototype suites nor stale receipts replace current checks.

## Acceptance Checkpoint Mapping

A0-A4/B/C retain their complete stated outcomes in
[MVP Scope](MVP_Scope.md) and [Acceptance Criteria](Acceptance_Criteria.md).
An RC may explicitly exclude unfinished capabilities but must not claim the
complete milestone. Failed or missing mandatory evidence blocks the included
profile, not unrelated feature development.

## Capability Coverage and Keeping the Roadmap Current

Update one current row when a capability changes: implemented boundary,
unavailable routes, next gap, actual dependencies and canonical contract link.
Store exact revisions, test output, failed attempts and native trial outcomes
in dated evidence records. Link receipts instead of appending their full text
here. Historical receipts do not acquire authority from being linked.
