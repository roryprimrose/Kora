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
| Sessions and work | Durable identity/lifecycle/history/retention, six closed metadata queue profiles with retained snapshots, current-run pause and question recovery | Full conversation execution, arbitrary queues/effects, concurrent workers and model tools | [Sessions](Interaction_And_Sessions.md), [workspace](UI_Workspace_And_Windows.md), [work](Work_Management.md) |
| Native workspace and evidence | Selected work and passive cross-session attention overview, metadata/history search, saved-composer catalogue with native name/exact-ID metadata queries, bounded SQLite/DailyLog/CombinedLog/AuthorityAudit inspection and native filters | General Ask Evidence/model retrieval/export; full screen-reader/readability/physical-DPI acceptance | [Workspace](UI_Workspace_And_Windows.md), [display](Information_Display.md) |
| Preferences and audio | Admitted native/exact preferences, consented PTT, installed/local speech, visual recovery and run-only manual call protection | Automatic call detectors, call-policy downgrades, temporary speech exceptions and acoustic qualification | [Configuration](User_Configuration.md), [call policy](Call_Aware_Speech.md) |
| File/clipboard inspection | Immutable previews, folder lexical retrieval, reviewed refresh, one retained file/session, replacement/removal, metadata catalogue, opt-in Markdown, exact citations and native current-file literal Find | Managed sources, model grounding/egress, indexes, screen/OCR context | [Ingestion](File_And_Folder_Ingestion.md), [clipboard](Security_Data_Flows.md) |
| Memory and providers | Original-user memory review/admission and session-only durable storage; host policy/handoff, initial mode, audited app profiles and durable exact-session sparse overrides with native/exact review and subsequent-turn capture | Authentication/qualified hosted adapters, generic/one-turn profile directives, model memory recall or automatic handoff | [Profiles and session resolution](Model_Providers_Memory_And_Knowledge.md#session-overrides-and-effective-resolution) |
| Definitions | Bundles/profile reads; owned instruction/prompt Validate/Save/copy/Disable/search; native v2 required literal prompt inputs, whole expanded preview and separate fresh admission | Profile-original writers/forks, broader formats/refinement/applicability/runtime and full matrix unqualified; generation unavailable | [Management](Definition_Management.md), [editor](../docs/instruction-editor.md) |
| Captions and local events | Off-default playback-bound captions, run-only pin/display choice; trusted visual notices, cached maintenance and routine quiet mode | Sentence alignment, general rich rendering, proactive speech and broad reminder scheduling | [Display](Information_Display.md), [proactive interaction](Proactive_Interaction.md) |
| Distribution | MSI/custom Burn packaging, release/source-tool automation and reviewed build-only source workflow | Full installed/protected-byte/runtime qualification and install-capable updater | [Distribution](Distribution_And_Updates.md), [installer](../installer/README.md) |

Session permission levels and expanded skill/instruction authoring are
[proposed contracts](Session_Permission_Levels.md), not new runtime authority.
Existing exact lock behavior and non-executing power proposals are unchanged.

## Ordered Outstanding Work

### R12/R14 exact-session conversation/composer foundation — 2026-10-12

The [compact selected-session projection](UI_Workspace_And_Windows.md#delivered-compact-selected-session-projection) adds explicit tray/workspace single-instance access to the same selection, memory draft and canonical conversation actions.
Shared lifetime, dirty decisions, source revocation and truthful USER/HOST/work observations preserve the workspace and standalone question/voice presenters. This is not generation, routing or full R14/installed qualification.

The [native untargeted New request fallback](Interaction_And_Sessions.md#delivered-native-new-request-fresh-session-fallback--2026-10-13) is delivered from tray and Sessions with single-instance activation and an isolated memory draft. Unavailable relatedness inference offers only explicit fresh generic **New conversation**; no selected/Done/named/recent reuse or body-derived title.
Audited exact-target creation is acknowledged before a separate frozen-body review/Submit through the canonical conversation admission service. Physical COMMIT fences, one-shot review, private close, dirty-buffer protection and exact passive inspection preserve truthful partial success without retry/rollback/Done/delete or body transfer. Production generation, relatedness/voice/compact routing, installed qualification and the paused storage/profile scopes remain unavailable.

The same confirmed created Active target now supports a distinct explicit revision-reviewed draft Save through canonical draft authority. Complete old/new body and confirmed-baseline review, exact metadata/revision COMMIT fencing and separately confirmed eligible close Save/Discard/Cancel preserve named-window concurrency and all unknown holds. No target is created just to save or close.
Saving is not submission, provider admission, conversation execution, approval or meaningful activity. Saved-draft discovery and explicit Sessions Reload stay unchanged. Portable/private SQLite/headless tests are implementation evidence, not installed accessibility, comprehensive credential detection, physical-storage, profile-original or full R12/R14 qualification.

The native [saved composer draft catalogue](Interaction_And_Sessions.md#saved-conversation-draft-catalogue--2026-10-13) delivers explicit bounded metadata-only discovery across uncapped retained sessions and stale-refusing exact Sessions navigation.
No automatic body load/save/overwrite/Submit, global count/quota, schema/index/file-copy change or body authority duplication is added. Reload remains a separate existing Sessions action.
Private metadata/SQL/headless checks do not qualify generated conversation, provider/hardware/installed accessibility, profile-original editing, hidden import, physical storage or STOP. Existing required audit, retention, source privacy and deferred capability gates remain unchanged.

The [passive selected-conversation search](Interaction_And_Sessions.md#delivered-passive-exact-conversation-lexical-search--2026-10-13) now searches actual retained USER words and typed HOST status in one explicitly selected Active or retained Done session.
Shared lexical policy, bounded complete chronological results and exact query/generation/ceiling/head continuation reuse admitted conversation pages.

Native Search/Next/Cancel/Clear preserve composer drafts, saved revisions, question/voice focus and meaningful-activity/retention authority. Final source/private validation fences late publication; query/result text is not persisted, logged or sent as model context.
This is not older receipt `HistorySearch`, global body search, an index, retrieval upload, runtime/inference or full R12/R14/installed-native/physical-storage qualification.

The next bounded integration is delivered: a reviewed native owned-v2 expansion can be separately confirmed as exact USER text for one explicitly selected existing Active session, through the same conversation screening/store/task/audit/admission service. Preview and old Use still do not retain form bodies. There is no auto-routing, draft overwrite, template mutation or generation/auth/network/egress enablement.
Headless/native private-store tests cover exact body, changed source/target/privacy, credential rejection, one-shot admission and uncertain COMMIT inspection. They do not qualify installed accessibility, hardware, inference or the remaining RT1/RT2/MG1/PV1/physical-storage/STOP gates. See [prompt delivery](Definition_Management.md#bounded-owned-instruction-and-prompt-editor).

The [native composer and private authority](Interaction_And_Sessions.md#delivered-exact-session-conversationcomposer-foundation--2026-10-12)
deliver memory-only editing, explicit revision-bound saved draft operations,
immutable submitted original user messages and truthful unavailable HOST/task
terminals through the real configured provider-neutral admission consumer.
Schema 10→11 preserves old task/history/queue/provider/audit bytes and uses
the existing atomic authority/copy/deletion domain. Body/page limits and
100 messages/session do not cap retained sessions or aggregate drafts/messages.
No unexpired content/audit eviction, auto-save/replay, provider/bootstrap
fallback, HTTP/authentication or assistant-output claim is added.

The approved follow-up adds one Core local structural credential-like
reject-only rule at service/store boundaries, before any body persistence or
admission. Content-free required rejection audit retains no candidate/hash;
accepted text remains exact. Known placeholders remain inert, and limitations/
false positives are disclosed without comprehensive DLP qualification.
Physical files stay unchanged; canonical IDs/source hashes/clocks and strict
versioned migrations/copy/journal inventories remain the storage contract.

This substantive host/UI increment follows #217's
[engineering progression policy](Deferred_Validation.md#engineering-progression-policy).
Rejected Ollama candidates and the retired manual proof harness are not
prerequisites or restored validation. Full generated conversation, history
context assembly, global/voice routing, general execution, provider/hardware,
installed-native/accessibility and R12/R14/RC qualification remain gated.

### R14 native question terminal-publication correctness - 2026-10-12

The [native terminal publication fix](Interaction_Fallback.md#synchronized-native-terminal-publication---2026-10-12) synchronizes closed state and its original-await receipt across committed cancellation, decision, denial and failure. Deterministic private-SQLite/native recovery regressions preserve cancellation without bypassing earlier withdrawal; later clearing and exact pre-dispatch checks remain mandatory.

This bounded correctness change adds no runtime, permission, privacy fallback, replay or physical cessation claim. Installed-native/accessibility, physical storage, containment and retained inference/runtime qualification gates remain open.

### R26 retained attachment catalogue exact-ID query increment - 2026-10-13

The [stored lifecycle filtering increment](File_And_Folder_Ingestion.md#delivered-retained-attachment-metadata-catalogue---2026-10-11) adds native All/Active/Done/Removed over validated current metadata, independently of exact ID and removed attachment slots. Removed sessions expose only tombstones with Include removed slots. Bounds, corruption admission, authenticated continuations and exact navigation are unchanged; no broader R26, search, storage, model, experiment or qualification boundary is advanced.

The [retained attachment metadata catalogue](File_And_Folder_Ingestion.md#delivered-retained-attachment-metadata-catalogue---2026-10-11) delivers native Browse/default and canonical ExactId Search/Enter, Next/Cancel/Clear with volatile private query state.
Fully validated fixed scans and bounded audit-prefix admission remain mandatory, including irrelevant records and empty nonmatching pages. Query/removal flag/source/profile/viewer/authority bindings refuse stale or malformed continuations; selection/navigation remains separately explicit and full-byte verified.
No name/title/path/body/digest search, new metadata reads, schema/index/cache/layout/copy mutation, count cap, activity renewal, model/tool/speech route or saved query is added. Storage optimizations and profile-original/hidden-import work remain deferred.
Focused Core/Application/private SQLite/native checks qualify this consumer only, not broader R26, provider/hardware, installed screen-reader/DPI, physical protected-storage/deletion or STOP. Existing audit/retention/deadline and enablement/RC gates are unchanged; no experiment is edited.

### R26 native current-source literal Find increment - 2026-10-12

The shared private original-file viewer delivers native First/Next/Previous/Clear for one exact case-sensitive literal span in the currently inspected immutable single file, captured-folder selector item or revalidated retained Session attachment. Query spaces/case stay exact; nonoverlapping cursor steps never wrap or build all-occurrence indexes/counts.

Existing inert source/span controls, source/view/query generation and citation admission are reused. Retained Find reads only the validated private historical original/session/storage reference, without original-path access, capture or activity renewal.

Ranked lexical/Markdown retrieval/citation policies and limits are unchanged. No typed/PTT query, model tool, export, clipboard, speech or persistence is added.

Maintained [portable](../tests/Kora.Core.UnitTests/Context/LocalFileLiteralFindTests.cs), [native](../tests/Kora.Windows.IntegrationTests/LocalFileLiteralFindWindowTests.cs) and [retained Done/private SQLite after original deletion](../tests/Kora.Windows.IntegrationTests/SessionAttachmentWindowTests.Find.cs) evidence covers this consumer only.

No experiments/receipts or RT1/RT2/MG1/PV1, CPU-floor/offline inference, containment, hardware, installed/accessibility, physical-storage or physical STOP proofs are retired.

See the [canonical Find contract](File_And_Folder_Ingestion.md#delivered-native-current-source-literal-find---2026-10-12). Broader source/grounded reasoning and UI/voice parity remain proposed; profile-original editing remains security-boundary deferred.

### R22 required owned prompt input preparation increment - 2026-10-12

Owned prompt native v2 supports only required `${input:name}` literal text: eight distinct canonical names, native fields bounded to 512 UTF-16 / 2048 UTF-8 each, and complete expanded USER REQUEST bounded to 4096 UTF-16 / 16384 UTF-8. Shared source/parser rules preserve exact bytes/BOM/digest and v1 semantics; no expressions/defaults/reads/recursive substitution.
Exact artifact routes open a private ephemeral native form with explicit whole-request Preview and separately admitted Use. Editing/source/review/native identity/input/call/private changes retire previews; Cancel/close clears values without persistence, clipboard/export or speech.
Use rechecks original template freshness and fresh model context/request admission, with actual serialized 32768-byte envelope enforcement. No source digest is fabricated from expansion; values remain user data, not commands/system/tool authority.
Production generation remains unavailable and both evaluated Ollama candidates remain rejected. This is useful model-free preparation, not provider qualification, profile enablement or an inference fallback. Profile-original editing and existing installed/accessibility/storage/runtime STOP gates remain unchanged.

### R22 open owned draft structural validation increment - 2026-10-12

Native/exact/current-name activated Validate observes only the currently open supported same-kind owned Instruction/literal Prompt original or explicitly created draft. The shared ordinary Save preparation counts BOM/native-marker overhead and retains exact metadata/source/newlines; unchanged compatible legacy bytes remain legacy.
Bounded body-free findings report prepared byte count/digest/format/source/model-context role and unapproved state, not semantic harmlessness, disk integrity, approval or enablement. Source/name/kind/baseline/catalogue/private/input fences retire findings; Save independently prepares and exactly confirms.
No owned-store List/Read/Write, reload/reconciliation, revision-preference hold/write, use mutation, model/network or reference execution is added. Normal observational control receipts/traces remain. Draft/dirty/inventory/selection, original enabled state and uncertain source/publication holds are retained with explicit recovery.
Maintained codec/preparation/application/router and actual native headless/private SQLite consumers cover this bounded production path. No maintained experiment is edited or retired; unique RT1/RT2/MG1/PV1, offline CPU/inference, containment, hardware, installed-native/accessibility, physical storage and physical STOP proof gates remain open.
Profile-original editing remains explicitly security-boundary deferred. Broader formats, templates, skill/agent authoring/runtime and conversational refinement are not delivered.

### R22 owned metadata inventory navigation increment - 2026-10-12

The existing native editor exposes only its two existing separately bounded 128-file owned inventories. Explicit admitted List/Refresh enumerates and reconciles; literal metadata query/use filters and deterministic ≤64 KiB complete-label/details pages are in-memory, without body reads, new roots/indexes, writes, activation or first-result loading.
Observed catalogue use state is disclosed as an observation, not current disk integrity. Exact row Inspect binds original kind/filename/source-qualified ID/digest/native revision/catalogue/private epoch and revalidates native readback. Stale/replaced/unknown rows cannot silently retarget a current-name replacement; failures require Refresh/reselection.
Dirty originals/drafts and existing Save/copy/Disable/review/conflict/close flows remain explicit. Source/use/call changes retire choices; failed refresh removes availability; privacy/ownership/closure clears private controls and suppresses late callbacks. Native filter controls are the search route, not a new model tool or fabricated typed grammar.
Focused deterministic portable and actual headless controller/native/private SQLite checks cover these consumed contracts. They do not replace unique RT1/RT2/PV1, offline-inference/CPU-floor, worker containment, hardware, native installed/accessibility or physical-storage proofs; no maintained experiments are edited or retired.
Profile-original editing remains security-boundary deferred, and the full source/reader/writer/runtime matrix is still unqualified. This increment changes no executable/runtime or broader source authority.

### R22 exact owned reviewed-use disable increment - 2026-10-12

Native/exact/current-name activated **Disable reviewed use** separately confirms the clean inspected canonical native v1 owned instruction/literal prompt revision, without source writes/deletion or source-consent changes.
Existing admission/native freshness, trusted audit/control receipts, strict independent kind-state holds and consumer publication fencing withdraw new selections durably; no-change reload/restart/restored bytes cannot re-enable use. Repeated exact Disable is idempotent; fresh current-run review is required later.
Inspection/editing and immutable already captured reasoning remain, while pending unadmitted choices expire without replay/retargeting. Dirty drafts and failed/unconfirmed audit/state publication retain explicit recovery, never silent Save/discard, rollback or restored enablement.
Maintained deterministic revision/workflow/consumer, private SQLite/native composition and actual controller/window/button tests cover this bounded increment. They do not replace unique RT1/RT2/PV1, offline inference, containment, hardware, installed/accessibility or storage qualification; no experiments are edited/retired.
Profile-original editing/security qualification remains explicitly deferred. No new grants, tools, model adapters, runtime, templates or broader applicability are delivered.

### R22 owned draft copy recovery increment - 2026-10-12

Native/exact/current-name activated **Save as new copy** creates one separate same-kind supported native v1 owned instruction/literal prompt from a frozen inspected base and current draft.
The common review/writer/revision pipeline preserves the original source/draft, changes only the reviewed declared name and atomically refuses any occupied destination.
New copies remain disabled until independent current-run explicit-use review; no profile-original reads/writes, legacy repair, inherited grants or reference retargeting are added.

Maintained codec/workflow, private native SQLite/audit composition, native editor and store regressions consume the production path for this bounded recovery feature.
They do not replace unique RT1/RT2/PV1, offline inference, containment, hardware, installed/native accessibility or storage evidence. No experiments are modified or retired.
Profile-original publication remains explicitly deferred; template/skill/agent runtime, refinement and expanded applicability gates are unchanged.

### R14 optional native prerequisite parity - 2026-10-12

One new fixed-read enqueue can explicitly choose None or an exact same-session prerequisite from the already-observed bounded task metadata list. All six native buttons use the original exact command/application/audited store path and optional real revision guard at COMMIT.
Refresh/private/source changes refuse stale choices; successful enqueue clears the next decision. No existing dependency editing, cross-session native picker, automatic dispatch or model management is delivered.
The bounded list now includes already-observed queued pending/running/recent terminal tasks alongside nonqueue tasks. Optional task metadata comes from actual host task rows in the same atomic work read; restart-divergent host task revisions/states are not guessed from queue-entry authority. Missing/conflicting metadata is unavailable, never a fabricated prerequisite or silent None. Existing work bounds, 64 KiB disclosure and stored digests remain unchanged.

Maintained native event/accessibility-label, real SQLite revision/private-boundary and existing queue/profile/order tests cover this consumer parity. No experiments are changed or retired; unique RT1/RT2/MG1/PV1, offline/containment, hardware/storage, installed-native/accessibility and physical-stop gates remain open.

### R13 closed targeted metadata increment - 2026-10-12

The [targeted queue increment](Work_Management.md#delivered-closed-targeted-metadata-reads---2026-10-12) completes queued access to the existing six registry reads: one exact current descriptor and fixed recorded local runtime join four unchanged profiles.
Actual registry/native/private SQLite consumers validate target/result binding and explicit version-5 compatibility. This is not arbitrary tool arguments, model management, live qualification or broader R13 completion.
Maintained consumer tests do not retire or replace unique RT1/RT2/MG1/PV1, offline CPU/inference, containment, hardware, installed-native/accessibility or physical protected-storage/STOP gates. No experiment is edited.

### R13 selected pending cancellation increment - 2026-10-12

One-session/current-run pending-only subset review and separate atomic confirmation are delivered through native/exact/current-name activated routes.
The existing consolidated schema-10 host/store path validates complete membership and every authority fence, retains typed audit/control/history receipts, removes only selected dispatch IDs and preserves unselected order/deadlines/results and explicit pause.

Maintained domain/parser, shared service, real private SQLite and native presentation tests cover mixed profiles, bounds, stale sets, failures, callback independence, retained dependents and no replay. These are consumed production-path checks, not evidence for arbitrary queues, general batch effects, current-task termination, broad R13 completion or model authority.

No experiments are edited or retired by this increment. Maintained queue/order/storage/native-consumer regressions provide bounded coverage equivalence only; unique RT1/RT2/MG1/PV1, offline/hardware, worker containment, protected-storage, installed-native/accessibility and physical-stop enablement/RC trials remain open at their existing qualification gates.

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
| R22 | Owned editing/copy/Disable/Validate/search and prompt v2 required literal native fields/complete expanded preview/separate fresh admission delivered. Generation unavailable; source-preserving holds require fresh review. Next: deferred profile originals/forks, skill/agent formats, refinement and expanded applicability. Full matrix/runtime unqualified | R05/R10/R12/R14/R20/R21 |
| R23 | Slice B/C and manifest-scoped release regression | Q: R19 and included R20-R22 scope |

Partial packages may progress in parallel once their consumed I subset exists.
Do not schedule live trials merely because a implementation dependency merged.

[Definition Management](Definition_Management.md) owns the expanded R21/R22
source/editor/reload contract; [authoring](Skill_Authoring.md) retains the
single-file instruction experience and skill package checks. Implement formats
incrementally. Current R21 read consent is not silently expanded to editor
scope, and profile editing never becomes a generic filesystem/tool permission.

### R02 Local-Inference Continuation

The Ollama 0.35.1/qwen3:1.7b and Ollama 0.40.2 candidates are rejected. Do not
repeat hardware, offline or human trials for them. Continue provider-neutral
host, UI and orchestration development with explicit unavailable behavior.

If a materially changed candidate is proposed, apply the
[future candidate gate](Local_Inference.md#future-candidate-gate): automated
quality and authority first, then approved identity/licence, reference hardware,
context/cancellation, offline/egress and integrated-host qualification. That
work gates only the candidate's enablement and associated release claims.

## R13 exact current-run queue pause receipt - 2026-10-11

The [fixed local-version increment](Work_Management.md#delivered-exact-current-run-dispatch-pause---2026-10-11) adds exact native/original typed/activated Pause/Resume and observational Work/All work/status.
Pause shares the physical atomic admission fence, skips only the addressed session and lets committed callbacks drain; Resume never dispatches or replays.
Captured limits/deadlines/FIFO/fairness, private epochs, Unknown quarantine and meaningful-activity retention remain authoritative.
Deterministic domain/parser tests and genuine private-SQLite commit/callback races cover this bounded control, not MG1 inference or general workers/providers/leases. The enabled consumer closure is queue admission, Work/All work, local event eligibility and native/exact command routing; question waits, grants, captions, quiet mode, memory and provider/definition authorities are unchanged.
Applicable MG1 envelope/no-retry/Unknown assertions remain in maintained queue tests; RT1 final-request/all-status controls and RT2 native mediation/loss/quiescence, containment W1-W4, hardware/audio/accessibility and historical storage/recovery evidence are retained.
No experiment executable or shared consumer is retired: exact runtime/profile/native equivalence has not been established by these deterministic tests.
Full R13/A3, installed-native qualification and physical-stop evidence remain open under the three-tier policy.

## R02 Runtime/Provider Follow-Up Gates

The [reviewed expired current-run pending removal/new-decision increment](Work_Management.md#delivered-reviewed-expired-pending-removal-and-fresh-tail-decision---2026-10-12) joins native/exact/current-name activated consumers to one audited private-store transaction: old Removed/Cancelled history and a separate fresh same-profile task at session tail, never replay or extension.
Current settings, source/private revisions, capacity and exact dependency lineage are revalidated. Running callbacks, normal manual dispatch/fairness, original deadlines/digests and restart no-replay are unchanged.

Deterministic parser/domain/application and genuine private SQLite tests retain the applicable MG1 envelope, audit/commit/cancellation/race and Unknown/no-retry obligations.
No experiment or consumer is retired; exact equivalence to retained RT1/RT2/MG1/PV1/offline inference, containment W1-W4, hardware/audio, native installed/accessibility or physical-storage/termination evidence is not established. Full R13 and those separate qualifications remain open.

The [reviewed adjacent pending-order increment](Work_Management.md#delivered-reviewed-adjacent-pending-order---2026-10-12) adds exact native and original typed/activated pair review/confirmation, shared ordering and atomic audited lineage at the existing store seam.
Arrival identity/digests/deadlines, dependencies, fairness, independent pause and restart no-replay remain authoritative. No experiment consumer is retired or relabelled; full R13/RT2/PV1, workers/providers/resource leases and installed/hardware/native qualification remain open.

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

Exact-session single-field model/reasoning/context UX now derives and reviews the current whole effective tuple while preserving other null/explicit sparse fields. Native/private composition and actual short typed/current-name activated routes use the existing audited writer and turn consumer; missing/incompatible tuples require complete-profile recovery. No schema, provider adapter, authority or retained-experiment qualification changes.

App profiles now support bounded individual exact model/reasoning/context review with separate native/typed/current-name activated confirmation through the same complete-tuple writer. Missing/incompatible dependents require complete draft/save; app default, other services and session overrides are retained. Production catalogues remain unavailable and hosted authentication/adapter/egress qualification gates stay closed.
Deterministic exact tuple/configuration and subsequent-turn tests are not assertion-equivalent to SDK-native RT1 bytes, RT2 lifecycle/egress, PV1 account, local-quality/offline or native/hardware procedures. Runtime preparation consumers and all unique proof/receipt assets remain retained; no experiment or release qualification is removed.

Experiments are opt-in evidence, not production contracts or open-ended
implementation prerequisites. Deterministic self-tests may run in CI, but
physical/lab qualification runs only when a materially changed capability is
being considered for enablement.
This documentation consolidation archives narratives only; **no experiment
executable or historical evidence is deleted, modified or relabelled**.

| Harness | Disposition | Rerun or retirement boundary |
|---|---|---|
| [Local inference](../experiments/r02-local-inference-proof/README.md) | Current pinned and comparison candidates rejected; retain evidence and deterministic boundary tests | Reopen physical/offline qualification only for a materially changed candidate intended for enablement |
| [Containment](../experiments/r02-containment-proof/README.md) | ACL/no-child/unrestricted-script approaches rejected; retain evidence to constrain the typed broker/admitted-worker replacement | Implement the replacement now; reopen lab qualification only before exposing its consequential worker profile |
| [Speech](../experiments/r02-speech-proof/README.md) | Retain physical acoustics, playback rejection and packaged capture/privacy timing | Affected speech/wake/provider/device profiles, not unrelated preferences |
| [Node runtime](../experiments/r02-runtime-proof/README.md) | Migrate all-status mediation, denial/cancellation/serialization assertions; retain rejected hook-only witnesses | Archive executable only after exact maintained equivalence and explicit candidate disposition; Node never qualifies .NET |
| [RT2 lifecycle](../experiments/r02-runtime-lifecycle-proof/README.md) | Migrate lifecycle/loss/quiescence cases; retain native procedures and observer limitations | Required native coverage must be maintained or unsupported profile explicitly excluded |
| [Storage](../experiments/r02-storage-proof/README.md) | Maintained standard-SQLite recovery/retention tests own current development; superseded SQLCipher/x86 work is historical | Reopen native/encryption qualification only if R30 is pursued; installed/durability evidence gates release claims, not R04/R12 implementation |
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
