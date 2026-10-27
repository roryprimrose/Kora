# Implementation Status and Delivery Roadmap

Status: live delivery inventory and remaining work, reviewed against main
`e93a927` on 2026-10-11. Bounded delivery is not complete capability, milestone
or release qualification. Detailed dated receipts are preserved in the
[historical roadmap](Archive/Implementation_Roadmap_2026-10-11.md).

This document owns delivery status and dependencies. Topic documents own
contracts; [Acceptance Criteria](Acceptance_Criteria.md) owns qualification;
[Deferred Validation](Deferred_Validation.md) owns outstanding environment
trials. Update those owners rather than copying status narratives between them.

## Product Realignment and Delivery Priority

Apply the [product owner's approved scope](MVP_Scope.md#product-admission-and-realignment) before selecting work from this inventory. Dated delivery entries record what was implemented, not approval to keep extending it.
The [completed realignment](#completed-product-realignment) removes native utilities, saved-draft document management, engineering-only normal browsing filters and public diagnostic scheduling. Existing read-only legacy recovery, data/audit compatibility and internal security/scheduler contracts remain.

Prioritize the approved removals and compatibility safeguards, then complete useful conversation/model turns, shared setting proposals, governed tool/result and MCP/skill/agent workflows, voice interaction, coordinated sessions and readable results. Choose a bounded increment toward one of those outcomes, not an unrelated convenience feature because a dependency is available.

Retired PRs must not be re-enrolled or recreated without a new explicit product decision. Deferred preparation-only work needs an approved complete interaction before resumption. Bug fixes and proportionate usability within retained outcomes remain valid. The existing I/E/Q distinction below continues to govern implementation and enablement independently of product admission.

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

## Approved Speech-Input Direction - 2026-10-22

The [local speech-input contract](Speech_Input.md) records approved configurable
wake detection, activated local ASR, resource/residency choices and optional
bounded personalization, without changing text-to-action routing or adding
hosted audio. D-002 engine selection, D-006 learning/verifier qualification and
D-007 hardware evidence remain open. New profiles/settings and production wake
are not delivered or enabled by this documentation.
Future qualified optional speech components must support both WiX/Burn
install-time preparation and runtime Settings through shared setup services;
application/native payload, model data and input consent remain distinct.

Implement only the consumed provider/settings/generation/quiescence subset
with deterministic seams; do not wait for unrelated R09/R24 physical evidence.
Exact offered speech profiles still need applicable E evidence, and the final
enabled manifest needs Q evidence. Optional learning is not a baseline voice
dependency and does not authorize a blanket owner-only command policy.

## Completed product realignment

The bounded R08 [provisional local answer increment](Information_Display.md#bounded-provisional-local-answers) adds explicit local tool-free incremental presentation to the ordinary conversation owner, with exact-turn publication, independent cancellation/resource join and final-only ASSISTANT retention. Existing tool/clarification/ordinary hosted paths stay buffered; local enablement and broader R08 qualification remain separate ([deterministic host tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.Streaming.cs), [native transport tests](../tests/Kora.Windows.IntegrationTests/Dependencies/WindowsOllamaToolTransportTests.Streaming.cs)).

The separate bounded R08 [native Claude text-stream increment](Runtime_Provider_Feasibility.md#native-text-stream-implementation-not-enablement) implements explicitly selected original-user SSE text through the same provisional owner, independently consented Count Tokens/Messages POSTs and final-only retention. Exact deployment/account/model stream enablement remains unavailable by default; no provider trial or broader R08 qualification is claimed ([runtime](../src/Kora.Windows/Dependencies/ClaudeMessagesRuntime.Streaming.cs), [composed deterministic evidence](../tests/Kora.Windows.IntegrationTests/HostedClaudeSessionRequestTests.Streaming.cs)).

- Product-admission rules are merged in [#419](https://github.com/roryprimrose/Kora/pull/419); native calculator/clock/clipboard-analysis utilities are removed in [#420](https://github.com/roryprimrose/Kora/pull/420).
- Memory-only unsent input and read-only legacy recovery replace draft documents in [#421](https://github.com/roryprimrose/Kora/pull/421). Read separation, composer chrome and shutdown protection follow in [#424](https://github.com/roryprimrose/Kora/pull/424), [#425](https://github.com/roryprimrose/Kora/pull/425) and [#426](https://github.com/roryprimrose/Kora/pull/426).
- Engineering-only normal browsing predicates and public diagnostic scheduling are retired in [#422](https://github.com/roryprimrose/Kora/pull/422); historical link correction is [#423](https://github.com/roryprimrose/Kora/pull/423). Internal scheduler/domain/audit/schema compatibility, safe existing-work controls and Evidence/grant diagnostics remain.
- [#428](https://github.com/roryprimrose/Kora/pull/428) corrects ordinary submitted-date/task-state and interaction-receipt filters without restoring technical IDs/ranges. Its final hosted admission is tracked separately; this section does not claim provider, hardware or installed qualification.

The earlier detailed retirement slices are superseded by these current contracts, not pending implementation requirements. Historical archives remain historical. Broader aligned voice/model/tool/session/rich-display work is still subject to its own implementation and enablement gates.

## Current Delivery

### Adaptive response/session navigation - 2026-10-25

The primary response window expands in place into a resizable left-session-list/right-
conversation workspace. Explicit reads project existing retained user/assistant/host
records and ordered read-only activity; a named composer shares existing memory-only
per-session input and admitted submission. Tray, typed/current-name-activated show,
expand/collapse and exact presented-page ordinal selection reuse the same native owner.
Source changes refuse stale positions; historical events never answer or grant.
Advanced session management and authoritative question windows remain separate.

The approved relatedness policy is mode-independent, with selection as context rather
than a routing lock. The bounded reviewed operation now composes one tools-free
LocalOnly inference over the existing permitted metadata page, followed by exact
acknowledgment/correction and unchanged canonical original-user answering. Routing
has its own canonical control domain and physically joins before destination review.
Unqualified local inference remains unavailable; there is no hosted matching or
automatic submission. This implementation does not qualify providers,
installed accessibility or physical display behavior. See
[design and evidence](UI_Workspace_And_Windows.md#adaptive-primary-interaction-window---2026-10-25).

The later [integrated session content-pane decision](UI_Workspace_And_Windows.md#integrated-session-content-pane---2026-10-25)
is implemented for bounded passive native-text/Markdown, original/assistant
conversation records, selected activity, ordinary current responses and fixed
local-UI session log/audit queries. Brief/full-answer presentation and explicit
native/activated-command offers preserve complete sources without another model
call. This does not enable general result routing, Mermaid, static HTML or
embedded browsing; standalone documentation and specialized previews remain.

The bounded native **Back to conversation** increment completes exact-origin
focus return from retained message Details without closing the reader. Explicit
Hide shares origin-aware recovery across native and existing typed/activated
routes. Missing or non-message origins remain unavailable; no session selection,
history read, model call or submission is added. See the
[content-pane delivery boundary](UI_Workspace_And_Windows.md#delivery-boundary)
and [focused regressions](../tests/Kora.Windows.IntegrationTests/AdaptiveInteractionTests.ReturnNavigation.cs).
Installed accessibility and broader R14 qualification remain open.

### Bounded provider-neutral local-model tool continuation implementation - 2026-10-20

The [tool-loop contract](Internal_Model_Tools.md#bounded-provider-neutral-continuation-implementation---2026-10-20)
adds immutable Core protocol/transport contracts, one strict shared input
policy, finite complete/cumulative byte/call/round/deadline budgets and an
Application engine behind `IModelTurnAdapter`. Its only tool dispatch is the
existing `ModelTurnHost` callback; additional host-owned continuation
revalidation closes every send on stale session/task/generation/policy,
privacy, expiry or qualification. Exact successful results continue as inert
data, and only a genuine final answer enters the existing response contract.

The consumed I subset is the existing host admission/single-use/caller/audit/
quiescence foundation and six read-only catalogue actions. Production
transport composition belongs to its native adapter owner, and conversation
retention/presentation to its session owner; neither is replaced here.
Deterministic real-host/fake-transport tests and related documentation are
authored. [Maintained build/test execution and exact raw 100% portable line **and** branch
coverage](../CONTRIBUTING.md#development-checks) remain mandatory before READY.
Qualified fake evidence cannot qualify production. Runtime/profile
enablement and RC E/Q remain unavailable, independent of this bounded I
implementation; no provider/hardware/network/experiment is executed.

### Bounded admitted session model answer implementation - 2026-10-20

The [exact-session execution contract](Interaction_And_Sessions.md#bounded-admitted-local-answer-execution---2026-10-20)
connects original USER retention to configured turn admission, the same
ModelTurnHost single-use run and host registry callback, validated final
ASSISTANT output and atomic original-task/answer/audit completion with exact
readback. Sessions/compact, New request, expanded-prompt submit and passive
Details share the path. Legacy USER/search/draft/reuse semantics remain distinct.

Consumed I is the existing admitted host execution, read-only registry and
private original conversation/task/audit store, not complete R11/R12/R14 or an
installed provider. Additive response format 1 / message format 2 reuse existing
cells, cursor revisions and private retention/removal lifecycle, with a
[transactional schema-11-to-12 migration](Interaction_And_Sessions.md#bounded-admitted-local-answer-execution---2026-10-20)
for terminal message revision 3.
Unknown dispatch/cancellation/commit/source state fails closed without an answer,
provider fallback, replay or automatic session Done.

Implementation, deterministic real-host/fake-adapter, private SQLite and headless
native tests are authored. This lane is **IMPLEMENTED_AWAITING_VALIDATION**, not
READY until [exact-head maintained checks and raw 100% portable line AND branch proof](../CONTRIBUTING.md#development-checks)
pass. E/Q runtime/provider/installed/hardware evidence is unchanged;
production unqualified profiles remain unavailable. No experiment is a prerequisite.

### Bounded exact sparse session-provider copy implementation - 2026-10-19

The [session-provider copy contract](Model_Providers_Memory_And_Knowledge.md#exact-sparse-session-provider-copy-implementation---2026-10-19) adds explicit native/typed/current-name activated review of a source's exact sparse overrides for a distinct active destination. Complete old/new policy and effective/unavailable disclosure precedes separate expiring one-shot confirmation; null inheritance is never flattened into a source effective tuple.

The existing Application review action, strict sparse/domain rules and private destination audit/save/readback/terminal writer remain authoritative. Both policy/lifecycle/app/private/account/catalogue snapshots are fenced; each destination transaction boundary rereads the exact source, and uncertain outcomes retain destination-only holds without retry. Source authority, credentials/grants/identities, app defaults, runtime/provider execution and network are not transferred or enabled.

Deterministic workflow, command, native and private SQLite tests are authored. Current-head maintained builds, tests and exact portable line-and-branch coverage are required before merge; authored tests and incomplete validation are not READY or passing proof. This consumes the merged session-provider configuration I subset only. Broader provider/runtime/egress E/Q qualification remains unavailable and is not an implementation prerequisite.

### Bounded retained original USER text reuse implementation — 2026-10-19

The [explicit original-text reuse contract](Interaction_And_Sessions.md#explicit-retained-original-user-text-reuse--2026-10-19) adds actual admitted USER-row review and separate one-shot confirmation into the single New request memory buffer. Complete source/body/provenance rereads, explicit dirty-buffer replacement, inert panes and private/source/buffer/native-opening fences preserve immutable originals and fresh eventual submission lineage. No task replay, HOST/model promotion, target creation, Save/Submit, queue/approval, clipboard, activity renewal or provider operation is implicit.

Implementation and deterministic tests are authored, including cancellation during synchronous memory handoff. Exact-head maintained builds, tests and raw line/branch coverage remain required before merge; authored tests and incomplete local execution are not passing validation. Only the existing canonical original-reader/New request/private-pane I subset is consumed; runtime/provider/egress/wake/profile-original writers remain unavailable and unrelated E/Q or physical/installed qualification is not claimed.

### Bounded exact immutable LocalFile Find count and ordinal - 2026-10-18

The existing [native current-source Find](File_And_Folder_Ingestion.md#exact-immutable-localfile-occurrence-count-and-ordinal---2026-10-18) reports an accessible exact total and selected ordinal only for its admitted displayed immutable file, never corpus/retrieval/live disk.
Both LocalFile and clipboard counted adapters consume the [shared forward nonoverlapping sequence](../src/Kora.Core/Context/SnapshotLiteralFind.cs); Previous reverses that sequence without changing ordinary uncounted Find.
Exhaustion preserves total/anchor ordinal without wrap or a current-match/focus claim; invalid input rejects before retained read, and async exact-source/privacy/generation/cancellation fences still gate disclosure ([portable tests](../tests/Kora.Core.UnitTests/Context/LocalFileLiteralFindTests.cs), [native tests](../tests/Kora.Windows.IntegrationTests/LocalFileLiteralFindWindowTests.cs)).
This consumes only the merged immutable-source/shared-Find/native I subset. No extra read, persisted count/index, model/provider/device/clipboard-write/authority change is added; E/Q and installed accessibility remain separate, and unqualified runtime/provider/egress/wake/profile-original writers remain unavailable ([contract](File_And_Folder_Ingestion.md#exact-immutable-localfile-occurrence-count-and-ordinal---2026-10-18)).

### Bounded owned draft source-preserving Rebase - 2026-10-18

The [owned definition contract](Definition_Management.md#bounded-owned-instruction-and-prompt-editor) adds explicit Rebase of an already open instruction/prompt draft, not an automatic merge or source writer.
The [typed admitted workflow](../src/Kora.Application/Definitions/OwnedInstructionEditorActions.Rebase.cs) freezes the old baseline, explicitly observed latest source and complete raw draft, reuses shared structural validation, and rereads the exact source revision on confirmation.
The [private native review](../src/Kora/OwnedInstructionRebaseReviewWindow.cs) discloses all three bodies and exact identity/byte truth; Cancel retains the previous baseline/draft. Confirmation adopts only latest baseline in memory, preserves raw text and retires old editor validation/use-review eligibility ([lifecycle](../src/Kora/OwnedInstructionEditorController.Rebase.cs)).
Missing/incompatible sources, changed identity/private/control/editor epochs and uncertain publication fail closed. Ordinary Save remains separate with full exact confirmation and can conflict again
([portable cases](../tests/Kora.Application.UnitTests/Definitions/InstructionEditorTests.Rebase.cs), [native cases](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.Rebase.cs)).
This consumes only the existing owned reader, parser, admission and editor I subset. Deterministic builds/tests and exact portable line/branch coverage gate implementation readiness; current-head receipts record their outcomes separately. No runtime/profile-original writer enablement or E/Q qualification is claimed.

### Bounded selected-subset session memory disable implementation - 2026-10-18

The [memory contract](Model_Providers_Memory_And_Knowledge.md#bounded-selected-subset-session-memory-disable---2026-10-18) adds explicit complete-content review/count and separate one-shot confirmation of a bounded selected enabled subset in one exact active session. Full preflight precedes sequential authoritative per-record disables; partial/unknown outcomes stop without retry or rollback.

The [Application orchestration](../src/Kora.Application/Memory/MemoryBatchWorkflow.cs) and [memory-only native region](../src/Kora/SessionsWindow.axaml) preserve single-record actions and retire stale private/session/source/edit receipts. [Deterministic tests](../tests/Kora.Application.UnitTests/Memory/MemoryBatchDisableWorkflowTests.cs) passed local serial validation on 2026-10-19: zero-warning/error Release builds, 14,338 full-suite passes, exact portable 28,393/28,393 lines and 19,772/19,772 branches (raw 1/1).

The orchestration is now shared with [reviewed selected-subset Forget](Model_Providers_Memory_And_Knowledge.md#reviewed-selected-subset-session-memory-forget---2026-10-19). That extension removes content through the existing per-record action, retains non-reusable tombstones, and discloses partial commits and logical/physical/forensic limits. Current-head maintained builds, deterministic/native/SQLite tests and exact portable line-and-branch coverage are required before merge; the preceding disable-only figures are historical, not proof for the extension.

This is an existing management I-subset increment, not batch admission/review acceptance, scope promotion, recall, model disclosure, retention-policy deletion, grants or export. No E/Q or installed/hardware/provider qualification is claimed; unqualified capability profiles remain unavailable ([policy](#delivery-and-qualification-dependency-policy)).

### Bounded native Evidence retained due ranges - 2026-10-18

The [existing Evidence inspector](Information_Display.md#delivered-bounded-native-evidence-inspection)
adds optional inclusive FromDueUtc/UntilDueUtc, independently AND-composed with
commit/observation time and every existing predicate. Shared canonical UTC
admission rejects repair; UNKNOWN due metadata cannot match a constrained range.
Both bounds bind the complete versioned query digest/authenticated cursor.
Labelled native fields retain no-read edit/Clear/Escape and stale-result fences
([deterministic tests](../tests/Kora.Windows.IntegrationTests/EvidenceAdvancedFilterTests.Due.cs)).
This consumes the existing Evidence I subset only: no schema, retention writes,
pruning, expiry/lifecycle, capture, body/path read or authority change. Due is
deadline metadata, not deletion/current-expiry/policy/permission proof.
E/Q, installed accessibility and unqualified providers/egress/wake/executors/
profile writers remain separate and unavailable.

### Bounded exact-grant collateral count ranges - 2026-10-18

The existing [one-exact-grant history](User_Configuration.md#delivered-native-exact-operation-grants--2026-10-10)
adds optional inclusive nonnegative Int32 Min/Max ranges for other committed
changes and requested grant references, AND-composed with every existing predicate.
The [query](../src/Kora.Core/Authorization/ExactGrantHistoryQuery.cs) owns strict
shared-decimal admission and versioned full-query digest; the
[reader](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.ExactGrantHistory.cs)
validates the bounded prefix even beyond a full result budget.
Accessible [native fields](../src/Kora/ExactGrantsWindow.axaml) retain read-free
edit/Clear/Escape retirement and all admission fences. Counts add no collateral
identity/detail disclosure, execution/completeness proof or authority.
This consumes only the existing ExactGrants I subset; no service/schema/source,
logging/audit mutation or runtime/provider/egress/wake/profile-writer enablement
is added. Broader installed/native/hardware/provider E/Q qualification is unchanged.

### Bundled and shared skill selected-identity metadata literal Find - 2026-10-18

The [bounded inspection increment](Skill_Storage.md#selected-displayed-identity-metadata-literal-find---2026-10-18) adds private First/Next/Previous/Clear over only frozen, already displayed selected identity text. It shares the renamed [attachment navigation state](../src/Kora.Application/Presentation/AdmittedMetadataFindState.cs) and unchanged Core Find, with read-free retirement and no catalogue filtering or source discovery.

The [native tests](../tests/Kora.Windows.IntegrationTests/SkillIdentityMetadataFindTests.cs) cover identity-only scope, private controls and retirement; existing [portable state tests](../tests/Kora.Application.UnitTests/Presentation/AttachmentMetadataFindStateTests.cs) retain attachment behavior. I consumes only merged inspection and literal-Find contracts; no runtime/model/provider/egress/wake/profile writer enablement or E/Q qualification is claimed.

### Retained attachment selected-metadata literal Find - 2026-10-18

The [retained catalogue](File_And_Folder_Ingestion.md#delivered-retained-attachment-metadata-catalogue---2026-10-11) adds private First/Next/Previous/Clear over only the frozen displayed selected metadata. Shared Core matching is unchanged. Read-free edits, tombstone suppression and selection/operation/privacy/control/source/close retirement preserve authority and filters.

This I increment has [portable tests](../tests/Kora.Application.UnitTests/Presentation/AttachmentMetadataFindStateTests.cs) and [native tests](../tests/Kora.Windows.IntegrationTests/AttachmentCatalogueWindowTests.Find.cs). No schema, capture, retention mutation, body/path/clipboard read or new authority is added. E/Q is separate; unqualified runtime/providers/egress/wake/profile writers stay unavailable.

### Bounded owned-inventory stable metadata ordering - 2026-10-18

The [owned inventory](Definition_Management.md#bounded-owned-instruction-and-prompt-editor) adds ordinal filename/display-name ascending/descending order before bounded pagination; filename ascending stays default. Keys/ties use extensionless owned filename IDs for prefix compatibility; missing display names stay last. Order binds continuation identity ([tests](../tests/Kora.Application.UnitTests/Definitions/OwnedDefinitionInventoryTests.Ordering.cs)).
The accessible native selector/Clear/Escape reuses no-read retirement while retaining exact unsaved draft/frozen baseline and separately admitted historical Compare ([native tests](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.InventoryOrdering.cs)). This consumes only existing inventory/editor I contracts; no persisted preference/schema, writer, runtime, model/provider/device or new authority is added.
Unqualified runtime/agent/profile writers, providers, egress and wake remain unavailable. Installed/physical/hardware/acoustic/provider/containment and retired experiment evidence is not a prerequisite for this bounded implementation; E/RC qualification remains separate and unclaimed.

### Bounded exact-grant committed request correlation predicates - 2026-10-17

The [one-exact-grant history](../docs/settings.md#exact-operation-grants)
adds optional exact committed HostRequest SessionId/RequestId/TaskId, AND-composed
with every existing predicate. The [query](../src/Kora.Core/Authorization/ExactGrantHistoryQuery.cs)
reuses strict canonical GUID parsing and binds each field in its versioned digest.
Named [native fields](../src/Kora/ExactGrantsWindow.axaml) retain no-read edits,
clear/reset, review retirement and existing bounded reader/admission fences.
Only committed request metadata matches, never viewer/original-proposal authority.
Deterministic Core/Application/private SQLite/headless tests cover this consumed
I subset; no schema, log source, service, external action or E/Q qualification is
added. Unavailable runtime/provider/egress/wake/profile-original writers stay disabled.

### Passive immutable skill catalogue stable ordering - 2026-10-18

The [admitted catalogue query](../src/Kora.Application/Skills/SkillCatalogueSearch.cs) adds Source-default and ordinal name ascending/descending views, unknown names last and exact canonical identity ascending ties. Existing literal/availability/compatibility/digest predicates compose unchanged after complete domain admission.

[Native selectors and lifecycle tests](../tests/Kora.Windows.IntegrationTests/SkillMetadataSearchWindowTests.cs) cover read-free edits, private pane retirement, shared generation fences, Clear/Escape and layout. This consumes only existing I contracts; no persistence, resource/digest-byte change, authority/completeness or E/Q runtime claim is added ([storage boundary](Skill_Storage.md#delivered-bounded-r21-native-inspection)).

### Bounded native Evidence observed parent kind - 2026-10-18

Built on freshly fetched main `6f1afd471af8c48059f34d8392b811426127974e`.
The [Evidence inspector](Information_Display.md#delivered-bounded-native-evidence-inspection)
adds Any/ObservedRoot/ObservedChild retained trace predicates and visible
accessible Any/Root/Child choices. Valid null parent is observed root; valid
nonnull parent is child; null trace is unknown. Complete trace validation
precedes exclusion, every filter composes with AND, and the versioned complete
semantic digest binds authenticated continuation. Existing bounded reads,
source/privacy/retention fences and no-read edit/Clear/Escape retirement remain.
This consumes the existing Evidence I subset with deterministic portable,
private SQLite and native headless checks. It adds no schema, authority,
device/provider/model route, complete-trace or causality claim. Installed
accessibility, runtime enablement and E/Q qualification remain separate and
unclaimed; unrelated whole-Rxx, hardware/provider and retired proofs are not
implementation prerequisites.

### Bounded owned-inventory exact identity navigation - 2026-10-18

The [owned metadata inventory](Definition_Management.md#bounded-owned-instruction-and-prompt-editor) adds explicit Literal (default), ExactOwnedId and ExactArtifactId modes over its admitted snapshot. Canonical owned-name/artifact admission is shared; exact artifact matches exclude unknown/unmapped rows.
Modes AND-compose with existing predicates and bind continuation identity; accessible mode/Clear/Escape retires selection/review state without source reads or raw-draft loss ([tests](../tests/Kora.Application.UnitTests/Definitions/OwnedDefinitionInventoryTests.QueryModes.cs)).
This consumes only the existing inventory/editor I subset. Historical Compare semantics remain separate; no profile writer, agent runtime, model/provider, schema or new enablement is added.
Broader E/Q qualification remains separate ([native lifecycle](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.Inventory.cs)).

### Bounded native session-list ExactName - 2026-10-16

Native Browse/name substring/ExactName/ExactId retain bounded source validation and duplicate-name disambiguation. Engineering generation/name-revision/name-length widgets are gone; names are never action authority ([current session contract](Interaction_And_Sessions.md#delivered-passive-session-list-name-and-exact-id-search---2026-10-10)).

### Bounded exact-grant committed correlation metadata predicates - 2026-10-16

Built on freshly fetched main `afdee7f508bc2e186bf56ef5f65cb27711e83bd1`.
The [one-exact-grant committed history](../docs/settings.md#exact-operation-grants)
adds optional exact committed audit correlation ID and explicit approval metadata
Any/Present/Absent/Exact, AND-composed with outcome/reason/initiator/retention/
committed-time/due-time. Core owns strict canonical lowercase nonempty D GUID
admission, Exact-only approval ID and versioned digest/cursor identity. Exact
matches only the committed event carrying that ID; Absent is validated absence,
not unknown. Complete envelope/prefix/provenance validation precedes exclusion.
Requested-review details remain undisclosed. Volatile named native controls
preserve inert exact target, no-read edits, selection/review/continuation
retirement, cancellation/stale fences and all existing admission ceilings.
IDs remain correlation metadata only, not authority identity, approve/grant/
renew/revoke/retarget or execution authorization. Deterministic portable/private
SQLite/headless checks and exact 100% portable line/branch coverage gate this
consumed I subset. No new production service, schema, retention policy, network/
device/provider action or experiment is required; E/Q and installed accessibility
remain separate and unclaimed. Saved drafts #282, Presence/native presenter,
build/performance, attachment file identities, memory scope identity and Evidence
trace metadata are unchanged.

### Bounded native Evidence exact trace metadata predicates - 2026-10-16

Based on freshly fetched main `afdee7f508bc2e186bf56ef5f65cb27711e83bd1`.
The [Evidence inspector](Information_Display.md#delivered-bounded-native-evidence-inspection)
adds optional exact source, source version, activity operation name, ActivityKind
and trace-flags predicates, AND-composed with all merged filters. Source version
has explicit Any/Absent/Present/Exact semantics; null trace is unknown, not absent
version. Strict bounded exact text, defined kind/flags, full trace validation
before exclusion and complete signed query identity preserve source compatibility,
page-budget failure, cancellation/privacy, stale-result and selection retirement.
Native visible non-null Any choices and read-free clear/edit behavior have
deterministic portable, reader and headless accessibility checks. Trace metadata
is untrusted diagnostic correlation only, never session selection, identity,
intent, permission, approval or authority; no baggage/tags/content disclosure,
storage schema, provider/device/network/experiment or sibling-domain changes.
This consumed I subset does not claim installed accessibility, full Evidence,
Ask Evidence or E/Q qualification, and requires no unrelated proof.

### Bounded native selected-session memory snapshot ordering - 2026-10-18

The [memory filter contract](Model_Providers_Memory_And_Knowledge.md#delivered-native-content-free-session-memory-filters---2026-10-14) adds Source-default, Created oldest/newest and Revision lowest/highest ordering with canonical ordinal GUID ties over fully validated loaded metadata.
This is consumed I only: no read, persistence, scope/authority expansion or provider/runtime enablement. Installed accessibility and broader memory E/Q remain unclaimed; no unrelated physical or retired experiment proof is required.

### Bounded native selected-session memory scope identity predicate - 2026-10-16

Built from freshly fetched canonical main `afdee7f508bc2e186bf56ef5f65cb27711e83bd1`.
The [memory snapshot filter contract](Model_Providers_Memory_And_Knowledge.md#delivered-native-content-free-session-memory-filters---2026-10-14)
adds optional exact scope identity GUID, AND-composed with exact memory ID,
Scope kind, Review, Retention and inclusive revision/created-time ranges.
Shared strict complete canonical D admission treats only empty as Any;
complete-row validation precedes exclusion and invalid metadata rejects the
whole result. Identity describes the admitted row, never permission, device/
provider ownership, scope authority, content or mutation authority. Accessible
native edits/Clear/Escape remain snapshot-only, with no store read, activity,
logging or persistence, and retire selection, inspection, mutation readiness,
drafts and stale callbacks under existing session/privacy/control fences.
The consumed I subset is the existing bounded admitted metadata snapshot and
portable/private SQLite/headless checks, including exact portable line/branch
coverage. No schema, command syntax, runtime exposure, provider/device/network
operation or experiment is added. Installed accessibility and broader R12/E/Q
remain separate and unclaimed. Saved drafts (#282), Presence/native presenter,
unknown build/performance work and sibling attachment file identities,
exact-grant audit correlation/approval and Evidence trace metadata are unchanged.

### Bounded exact-grant committed history retention and UTC ranges - 2026-10-16

Built on freshly fetched main `aee89d2380a25cd89c77ec032eafd2d7e5ecbc31`.
The [one-exact-grant committed history](../docs/settings.md#exact-operation-grants)
adds optional exact Present/ExpiredButPresent retention and inclusive canonical
UTC Committed/Due From/Until ranges, AND-composed with outcome, exact reason
and committed initiator. Core owns strict UTC/order admission and the full
versioned continuation digest; complete envelope/prefix/provenance validation
precedes exclusion, including nonmatches. Requested-review details remain
undisclosed. Accessible native inputs preserve no-read edits, inert exact target,
selection/review/continuation retirement, cancellation and stale-result fences.
Existing 4096-row/5-second, 50-envelope/64-KiB and 15-minute ceilings remain.
Focused portable/private SQLite/headless tests and exact portable line/branch
coverage gate this consumed I subset. No grant/renew/revoke/target/approval/
execution authority, schema, retention policy, model/device/provider/network
operation or experiment is added. Broader R05 and installed accessibility/E/Q
qualification remain separate and unclaimed; excluded peer scopes are unchanged.

### Bounded native session-list Browse and name availability - 2026-10-16

Ordinary Browse, lifecycle and named/unnamed navigation remain useful without engineering range controls. Edits perform no read, exact source changes invalidate continuation, and each complete candidate is validated before exclusion ([native presenter](../src/Kora/SessionsViewModel.ListSearch.cs)).

### Bounded native Evidence diagnostic names and sequence ranges - 2026-10-16

The existing [Evidence inspector](Information_Display.md#delivered-bounded-native-evidence-inspection)
adds optional exact retained event name/exception type and inclusive positive
audit-sequence bounds, AND-composed through the authoritative retained-metadata
matcher. Strict canonical native input, full-record validation before exclusion,
null nonmatching and query-bound signed continuations preserve existing source,
page-budget, cancellation/privacy, stale-result and selection-retirement behavior.
Accessible empty/Any and Clear advanced remain read-free. Names are diagnostic
metadata only; sequence is committed ordering, not authority, completeness,
freshness, replay position or permission. Portable and deterministic real-reader/
headless tests gate this consumed I subset. No storage schema, capture, provider,
network/device, experiment, content restoration or execution route is added;
Ask Evidence, installed accessibility and E/Q qualification remain separate.

### Bounded selected-session memory metadata ranges - 2026-10-16

The [native frozen memory filter](Model_Providers_Memory_And_Knowledge.md#delivered-native-content-free-session-memory-filters---2026-10-14)
adds optional inclusive positive revision and canonical UTC Created ranges,
AND-composed with exact GUID/Scope/Review/Retention. Portable parsing, ordering
and full-row validation plus deterministic native no-read, retirement,
cancellation/privacy and accessible-control tests cover this consumed I subset.
Malformed input explicitly fails closed. This is metadata only, not permission,
provider, memory-content or mutation authority. Full memory, installed
accessibility, provider and E/Q qualification remain unchanged and unclaimed;
unqualified recall/global catalogue/model consumers remain unavailable.

### Bounded All Work exact observed task state - 2026-10-17

All Work retains Any or actual observed task state with lifecycle, names and Needs answer. Missing/omitted records are not positive evidence or all-clear. Raw task/request queries and authority/queue/count ranges are removed ([window](../src/Kora/AllWorkWindow.axaml), [overview contract](Work_Management.md#delivered-passive-cross-session-work-observation---2026-10-11)).

### Bounded native Evidence retained metadata predicates - 2026-10-15

The existing [Evidence inspector](Information_Display.md#delivered-bounded-native-evidence-inspection)
now exposes exact Parent Span ID, exact retained `HostOperationOutcome`, and an
explicit Any/Present/Omitted content discriminator. Empty parent means Any;
nonempty input shares the portable strict W3C rule. Visible accessible Any
selections, AND composition with every existing filter, full bounded record
validation, signed query/cursor binding and edit cancellation/retirement use
the existing query/reader/presentation I subset. No storage schema/index/cache,
authority, activity renewal, model/tool/speech, body restoration, navigation,
export/clipboard or filter persistence/logging is added. Focused deterministic
Core/Application/SQLite/native checks are implementation evidence only; general
Ask Evidence and installed/accessibility E/Q qualification remain unavailable.

### Bounded Presence renderer work reduction - 2026-10-15

The [native particle-cloud scheduling boundary](Ambient_UI.md#bounded-native-renderer-scheduling)
adds renderability/privacy/disposal-fenced one-shot frames, immediate authoritative
speech/state wake, bounded 16/33/198-ms cadence, invariant seeded depth order and
effective-colour/theme/opacity brush reuse. Visible fades remain distinct from
actual hidden state; requested hide relinquishes mouse interception immediately.
Windows animation preferences freeze geometry without reseeding.
Focused deterministic scheduler/headless/native-input tests are implementation
evidence for this existing presentation consumer's I subset. No new authority,
audio/model/network path, package, compositor or hardware prerequisite is added.
Whole-app CPU savings and reproduction/resolution of the earlier whole-core
observation are not claimed; installed/accessibility/hardware and E/Q qualification
remain separate and unchanged.

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
| Sessions and work | Durable conversation/lifecycle/history/retention, session-local unsent input and safe retained-work inspection/cancellation | Qualified production conversation execution, arbitrary effects, concurrent workers and model tools | [Sessions](Interaction_And_Sessions.md), [work](Work_Management.md) |
| Native workspace and evidence | Conversation/history search, passive cross-session attention, bounded SQLite/DailyLog/CombinedLog/AuthorityAudit inspection | General Ask Evidence/model retrieval/export and full installed accessibility | [Workspace](UI_Workspace_And_Windows.md), [display](Information_Display.md) |
| Preferences and audio | Admitted native/exact preferences, consented PTT, installed/local speech, visual recovery, run-only manual call protection and [bounded communications-observation implementation](Call_Aware_Speech.md#bounded-windows-communications-activity-observations---2026-10-26) | Exact Windows observation-profile enablement, broader automatic call detectors, call-policy downgrades, temporary speech exceptions and acoustic qualification | [Configuration](User_Configuration.md), [call policy](Call_Aware_Speech.md) |
| File/clipboard inspection | Immutable previews, folder lexical retrieval, reviewed refresh, one retained file/session, replacement/removal, metadata catalogue, opt-in Markdown, exact citations and native current-file literal Find | Managed sources, model grounding/egress, indexes, screen/OCR context | [Ingestion](File_And_Folder_Ingestion.md), [clipboard](Security_Data_Flows.md) |
| Memory and providers | Original-user memory review/admission and session-only durable storage with [native frozen-list GUID/Scope/Review/Retention metadata filters and portable full-row validation](Model_Providers_Memory_And_Knowledge.md#delivered-native-content-free-session-memory-filters---2026-10-14); host policy/handoff, initial mode, audited app profiles and durable exact-session sparse overrides with native/exact review and subsequent-turn capture | Authentication/qualified hosted adapters, generic/one-turn profile directives, memory content search/global catalogue/model recall or automatic handoff; full memory and installed-accessibility qualification | [Profiles and session resolution](Model_Providers_Memory_And_Knowledge.md#session-overrides-and-effective-resolution) |
| Definitions | Categorized Built-in/Kora/User Customizations editor for skills, instructions, agents, MCP and prompts; persisted runtime exclusions; recoverable reviewed profile-original/component writes; activation-bound foreground editor voice intent with native clarification and separate tool-free local-model authoring reviews; existing owned instruction/prompt and literal-input routes remain | Automatic registration/applicability, arbitrary execution, spoken editor choice-answer capture, automatic session submission, production provider enablement and full qualification matrix remain unavailable or unqualified | [Management](Definition_Management.md), [Customizations](../docs/customizations.md), [owned editor](../docs/instruction-editor.md) |
| Captions and local events | Off-default playback-bound captions, run-only pin/display choice; trusted visual notices, cached maintenance and routine quiet mode | Sentence alignment, general rich rendering, proactive speech and broad reminder scheduling | [Display](Information_Display.md), [proactive interaction](Proactive_Interaction.md) |
| Distribution | MSI/custom Burn packaging, release/source-tool automation and reviewed build-only source workflow | Full installed/protected-byte/runtime qualification and install-capable updater | [Distribution](Distribution_And_Updates.md), [installer](../installer/README.md) |

Session permission levels remain [proposed contracts](Session_Permission_Levels.md).
Expanded definition authoring does not confer new runtime authority.
Existing exact lock behavior and non-executing power proposals are unchanged.

The delivered [passive exact-conversation search](Interaction_And_Sessions.md#delivered-passive-exact-conversation-lexical-search--2026-10-13)
also composes an optional All/USER/HOST field filter in Sessions and shared compact
projection, plus optional exact terminal outcome/reason/expected-task-state
predicates with Any. One portable filter ANDs typed terminal HOST metadata with
query/role; USER-only plus a predicate truthfully yields zero without repair.
Every edit revokes shared results/navigation/continuation without reads.
Immutable bodies, typed HOST fields, bounded validated chronological
scan and exact query/filter/source/private snapshot fences remain authoritative.
No global/history search, storage/index/schema/cache, model/provider authority or
installed accessibility/full conversation qualification is added.

## Ordered Outstanding Work

### Bounded native Evidence exact Span ID filter - 2026-10-15

The [native Evidence reader](Information_Display.md#delivered-bounded-native-evidence-inspection)
exposes the already-authoritative optional exact Span ID beside Trace ID:
empty-only Any, otherwise existing nonzero 16-lowercase-hex W3C validation.
Every existing filter composes with AND; edits revoke page/selection/cursor
and late callbacks without reading. Clear advanced preserves both basic W3C
fields; close clears both. Deterministic native/portable/real-reader checks
cover this consumed implementation subset, not installed accessibility or RC
qualification. Private/source/authority fences, complete bounded validation,
snapshots and omission remain unchanged. No ParentSpanId filter, new navigation,
model/tool/speech/export/clipboard route, storage/schema/index/cache, persistence,
filter-value telemetry, activity renewal or generated content is delivered.

### Bounded native speech-caption reader text scale - 2026-10-15

The confirmed device-local [reader scale](Information_Display.md#bounded-native-reader-text-scale)
now includes only currently observed native speech-caption content, in new and
open windows, using original typography. Caption chrome/accessibility names,
geometry/display choice, playback identity, pin/dismissal and privacy/call/source
retirement remain unchanged; unavailable preference evidence explicitly retires
text without replay. This consumes the existing preference/typography/caption I
subset. No other reader behavior, global UI scaling, caption enablement, speech,
model/provider/audio path or voice inference changes; deterministic checks do not
claim real audio, hardware or installed accessibility E/Q qualification.

### Bounded native Evidence reader text scale - 2026-10-15

The confirmed device-local [reader scale](Information_Display.md#bounded-native-reader-text-scale)
now covers only admitted complete immutable serialized Evidence page bodies
and their inert focused literal-Find spans, including new windows and live
confirmed changes from original typography. Chrome, strings/bytes/offsets,
navigation, evidence reads, activity and authority remain unchanged.
Preference/source/private closure and page retirement fail closed.
This consumes the existing preference/typography/Evidence I subset; no global
UI scaling, model/runtime route, voice inference, installed accessibility or
broader E/Q qualification is claimed.

### Bounded native session retention impact planner implementation - 2026-10-18

The [read-only session planner](User_Configuration.md#read-only-hypothetical-session-retention-planner) adds explicit proposal/preview controls, content-free hypothetical-versus-recorded due counts and conservative exclusions. Bounded metadata and exact source/preference/policy/admission revisions fence results; incomplete/unavailable observations expose no aggregate.

Consumed I is existing policy/private authority and native lifetime. No apply-now, mutation, cleanup, revocation, schema/index/cache/scanner or broader runtime is enabled. Deterministic tests cover policy, budgets, exclusions, currentness and lifecycle; exact-head validation receipts remain separate from E/Q qualification ([planner source](../src/Kora.Application/Configuration/SessionRetentionPlannerService.cs)).

### Bounded read-only audit shortening impact preview - 2026-10-15

[Native Settings preview](User_Configuration.md#read-only-audit-shortening-impact-preview)
requires an explicit shorter 30–365 day choice. It reports separately qualified,
content-free newly-due aggregates for required authority and diagnostic audit at
one fixed cutoff. Finite row/byte/time limits return Incomplete/Unavailable, not
truncated success or false zero. Exact proposal, preference/readback, private
store snapshot and host/control revisions fence publication; edits, refresh,
changes, close, cancellation/failure and timeout retire it without replay.
There is no activity renewal, mutation, model/tool/voice/network route or periodic
scan. Future-only Set/Reset is unchanged; apply-now/deletion/pruning remain
unavailable. Existing storage/configuration supplies the consumed I subset;
installed accessibility, audit cleanup/forensic disposal and broader E/Q remain
unqualified, not implementation prerequisites.

### Bounded native Sessions local-event filters - 2026-10-14

Normal event browsing retains category and reason. Technical source/profile/correlation/generation/lifetime predicates are no longer user controls; internal broker contracts and source validation remain ([event boundary](Proactive_Interaction.md#native-content-free-loaded-event-filters)).

### Bounded native Evidence page literal Find — 2026-10-14

The [Evidence inspector](Information_Display.md#delivered-bounded-native-evidence-inspection)
adds first/next/previous/clear over only its current successful complete
serialized in-memory page, using the shared literal policy and focused inert
UTF-16 span. Query/match retirement covers reads, filters, replacement,
failure/cancellation and private closure
([focused native tests](../tests/Kora.Windows.IntegrationTests/EvidenceLiteralFindTests.cs)).
No retained-source/body expansion, schema/index, audit authority, activity
identity, copy/export, speech/model/execution or network capability is added.
General Ask Evidence and installed accessibility/full qualification remain open.

### Bounded exact-grant committed-history typed filters - 2026-10-14

The existing [one-exact-grant history](../docs/settings.md#exact-operation-grants) admits native Any/defined committed-event outcome and bounded exact canonical ReasonCode predicates after explicit retained-record inspection. The 2026-10-15 increment adds Any/every defined committed `SecurityAuditInitiator`, exact AND composition and Core-owned typed native options/parsing/digest. The trusted reader validates exact membership and the authoritative prefix before matching; authenticated continuation binds inspected grant/store, viewer/read admission/control revision, the complete outcome/reason/initiator digest and finite ceiling/digest. Edits retire rows/continuation/actionable reviews without reading, retaining only the inert explicitly inspected target. Filter values are volatile, with no Requested-review predicate/disclosure expansion or activity renewal. Scan/nonmatch/zero-match progress retains 4096 rows/5 seconds, 50 results/64 KiB and 15-minute bounds.

No schema/pruning/retention change, authority expansion, diagnostic-mirror reconstruction, execution proof, exhaustive/forensic claim, replay/export/model/egress or installed accessibility/full qualification is delivered. The archived history receipt is historical contract evidence, not qualification of this increment.

### R14 bounded All work private metadata query - 2026-10-14

The [overview contract](Work_Management.md#delivered-passive-cross-session-work-observation---2026-10-11) retains name/exact-session navigation, lifecycle, observed task state and Needs answer. Explicit Search/Refresh/Next uses bounded validated independent snapshots; edits read nothing and retire stale selection.

Technical availability/content query widgets are removed. Source changed/read unavailable/output-bound and omitted/nonmatch states remain truthful displayed observations, never an all-clear or authority. Internal full-source/continuation validation remains ([All Work](UI_Workspace_And_Windows.md#delivered-bounded-passive-all-work--attention-overview---2026-10-11)).

### Bounded native admitted Detail literal Find - 2026-10-14

The [Detail literal Find contract](Information_Display.md#native-admitted-text-literal-find) delivers Find first/Next/Previous/Cancel/Clear on only frozen admitted in-memory text, with retained original USER and separate typed HOST targets. Shared snapshot query policy, nonwrapping exact navigation, source/private/close fences, inert focus/accessibility status and reader-scale tests preserve origin and bytes. No broader admission, source read, persistence/logging/export/clipboard, provider/egress, generated response or installed/accessibility/hardware/full qualification is claimed.

### Bounded native Evidence exact-field filters — 2026-10-14

The [Evidence contract](Information_Display.md#delivered-bounded-native-evidence-inspection) exposes existing EventId, Category and ActionId through optional exact native filters with source-specific unsupported recovery and the existing snapshot/cursor/private-closure lifecycle ([native and real-reader tests](../tests/Kora.Windows.IntegrationTests/EvidenceAdvancedFilterTests.cs)).
No schema/index, authority, execution, egress, scan-budget or qualification boundary changes; general Ask Evidence/export and installed/accessibility qualification remain unavailable.

### R12/R14 exact-session conversation/composer foundation — 2026-10-12

The [session conversation contract](Interaction_And_Sessions.md#delivered-exact-session-conversationcomposer-foundation--2026-10-12) now uses per-session memory-only unsent input, shared Sessions/compact views and explicit submission. Switching topics retains independent buffers; last-view/voluntary application closure uses Discard/Cancel, not Save.

Existing saved text has only exact-session read-only recovery into empty input. Historical schema, audit, retention and deletion stay compatible; ordinary saved-draft writers, catalogue, Compare and rebase UI are removed ([buffers](../src/Kora/SessionsViewModel.Unsent.cs), [shutdown guard](../src/Kora/ConversationShutdownGuard.cs)).

The admitted answer path is implemented but an unqualified provider remains explicitly unavailable. Retained USER, optional ASSISTANT and typed HOST outcomes remain distinct; reopening history neither runs a model nor renews authority.

### R14 native question terminal-publication correctness - 2026-10-12

The [native terminal publication fix](Interaction_Fallback.md#synchronized-native-terminal-publication---2026-10-12) synchronizes closed state and its original-await receipt across committed cancellation, decision, denial and failure. Deterministic private-SQLite/native recovery regressions preserve cancellation without bypassing earlier withdrawal; later clearing and exact pre-dispatch checks remain mandatory.

This bounded correctness change adds no runtime, permission, privacy fallback, replay or physical cessation claim. Installed-native/accessibility, physical storage, containment and retained inference/runtime qualification gates remain open.

### R26 retained attachment catalogue exact-ID query increment - 2026-10-13

The [stored lifecycle filtering increment](File_And_Folder_Ingestion.md#delivered-retained-attachment-metadata-catalogue---2026-10-11) adds native All/Active/Done/Removed over validated current metadata, independently of exact ID and removed attachment slots. Removed sessions expose only tombstones with Include removed slots. Bounds, corruption admission, authenticated continuations and exact navigation are unchanged; no broader R26, search, storage, model, experiment or qualification boundary is advanced.

The [retained attachment metadata catalogue](File_And_Folder_Ingestion.md#delivered-retained-attachment-metadata-catalogue---2026-10-11) delivers native Browse/default and canonical ExactId Search/Enter, Next/Cancel/Clear with volatile private query state.
Fully validated fixed scans and bounded audit-prefix admission remain mandatory, including irrelevant records and empty nonmatching pages. Query/removal flag/source/profile/viewer/authority bindings refuse stale or malformed continuations; selection/navigation remains separately explicit and full-byte verified.
No name/title/path/body/digest search, new metadata reads, schema/index/cache/layout/copy mutation, count cap, activity renewal, model/tool/speech route or saved query is added. Storage optimizations and profile-original/hidden-import work remain deferred.
Focused Core/Application/private SQLite/native checks qualify this consumer only, not broader R26, provider/hardware, installed screen-reader/DPI, physical protected-storage/deletion or STOP. Existing audit/retention/deadline and enablement/RC gates are unchanged; no experiment is edited.

### R26 retained attachment copy-status and byte-range filters - 2026-10-15

The [retained attachment catalogue](File_And_Folder_Ingestion.md#delivered-retained-attachment-metadata-catalogue---2026-10-11)
adds optional exact Any/Confirmed/Held/Unavailable and inclusive retained
StoredBytes bounds composed with all existing predicates. A shared strict
unsigned-decimal parser, typed limits, validated-before-exclusion finite scans,
authenticated query/source/private/head continuations and synchronous
edit-without-read revocation preserve the existing I contract. Unavailable
tombstones never match byte bounds or present their zero sentinel as original
size. Deterministic Core/Application/SQLite/native accessibility tests cover
composition, malformed input, tampering, zero matches and late revocation.
No schema/index/cache, content/path read, mutation, aggregate/persistence,
diagnostic/audit payload, model/tool/speech/export or activity renewal is added.
This bounded I increment does not advance broader R26 or E/Q qualification;
Presence/native-presenter and unrelated filter/review work remain separate.

### R26 exact retained attachment media-type filter - 2026-10-15

The bounded exact retained media-type increment adds All / `text/plain` / `text/markdown`, composed with exact ID, lifecycle, removed-slot and literal filters inside the existing fully validated scan. One extracted Core strict type rule, authenticated query/authority digest continuations and immediate native edit revocation preserve fail-closed nonmatch/corruption admission; tombstones match only All. Deterministic portable/private SQLite/headless control checks cover this consumer only. No schema/index/cache, body/OCR/path disclosure, count-total claim, activity renewal, model/tool/speech route, authority expansion or broader R26/installed-accessibility qualification is added.

### R07 native immutable clipboard literal Find increment - 2026-10-14

The [clipboard contract](Security_Data_Flows.md#delivered-r07-local-clipboard-preview---2026-10-07) adds private native First/Next/Previous/Clear over the exact existing capture, sharing portable literal query/offset policy with file Find.
One inert focused case-sensitive span, unchanged spaces, bounded strict Unicode, nonoverlap/no wrap and exact capture/view/privacy/owner/call fences apply. No clipboard read/write/reuse/revoke query route, provider/egress/context/history/index/persistence, timer or activity renewal is added.
[Portable](../tests/Kora.Core.UnitTests/Context/ClipboardLiteralFindTests.cs) and [headless native/controller](../tests/Kora.Windows.IntegrationTests/ClipboardLiteralFindWindowTests.cs) evidence is bounded to this consumer; no experiment, installed/privacy, hardware/acoustic or inference qualification gate is closed.

### Clipboard literal count and ordinal increment - 2026-10-18

Explicit native Find now reports the complete nonoverlapping count and selected ordinal for only the admitted immutable clipboard capture. Edits clear count and match without reads; counted Previous follows the forward sequence, with no wrap or overlapping alternatives ([clipboard contract](Security_Data_Flows.md#delivered-r07-local-clipboard-preview---2026-10-07)).
This bounded implementation adds no source/disclosure/model authority or runtime enablement. Deterministic [Core](../tests/Kora.Core.UnitTests/Context/ClipboardLiteralFindTests.cs) and [headless lifecycle/keyboard/viewport](../tests/Kora.Windows.IntegrationTests/ClipboardLiteralFindWindowTests.cs) checks remain distinct from installed-native and RC qualification.

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
The bounded 2026-10-15 exact artifact SHA-256 predicate adds only an optional native metadata filter for those same two kinds: empty Any or exactly 64 lowercase hexadecimal characters under the shared portable artifact policy, ordinal Digest equality, AND composition and truthful zero matches. Complete validation precedes exclusion; page continuation binds inventory/query/use/digest. Editing retires selections, details, validation, pending review/use and late callbacks without reads or activity renewal. No NativeRevision predicate, source/runtime/use authority, persistence, logging/export or integrity proof is added; installed/accessibility and broader Definitions qualification remain unchanged.
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

### R10 retained playback-volume suggestion increment

The bounded implementation accepts one exact retained successful assistant
`set speech.playback-volume to <canonical integer 0-100>` answer as inert data.
Expanded/compact Sessions and pinned typed/current-name activated review routes
require separate fresh confirmation within 60 seconds through the existing
admitted volume writer. Source/session/channel/privacy/call/preference changes
invalidate the review; voice origin cannot be laundered through UI confirmation.
See the [owning setting contract](User_Configuration.md#retained-model-playback-volume-suggestion).

This is implementation of an approved registered-setting proposal, not broader
R10 completion, model execution authority, provider/audio enablement or physical
qualification. Unavailable runtime/adapter paths remain unavailable; no retired,
hardware, provider, acoustic or installed experiments are required or changed.
Hosted protected candidate validation retains the full Windows/coverage/installer
gates independently of focused deterministic implementation checks.

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
| R22 | Owned editing/copy/Disable/Validate/search, prompt v2 required inputs and bounded [owned Instruction body refinement](../docs/instruction-editor.md#refine-the-current-owned-instruction-body) through complete comparison and confirmed ordinary Save/readback. Production generation remains unavailable; changed revisions stay disabled. Next: broader conversational/package authoring and applicability. Full matrix/runtime unqualified | R05/R10/R12/R14/R20/R21 |
| R22 bounded Compare | Owned frozen-baseline/raw-draft [Compare](../docs/instruction-editor.md#compare-the-open-draft-without-saving), independent bounded private ordinal literal Find, exact UTF-16 locations, no wrap; absent baseline is not searchable. Historical memory only; no reload/mutation/clipboard/model authority or expanded qualification | Existing editor / Validate / Core literal Find |
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

## R02 Runtime/Provider Follow-Up Gates

The [reviewed expired current-run pending removal/new-decision increment](Work_Management.md#authoritative-work-ledger) joins native/exact/current-name activated consumers to one audited private-store transaction: old Removed/Cancelled history and a separate fresh same-profile task at session tail, never replay or extension.
Current settings, source/private revisions, capacity and exact dependency lineage are revalidated. Running callbacks, normal manual dispatch/fairness, original deadlines/digests and restart no-replay are unchanged.

Deterministic parser/domain/application and genuine private SQLite tests retain the applicable MG1 envelope, audit/commit/cancellation/race and Unknown/no-retry obligations.
No experiment or consumer is retired; exact equivalence to retained RT1/RT2/MG1/PV1/offline inference, containment W1-W4, hardware/audio, native installed/accessibility or physical-storage/termination evidence is not established. Full R13 and those separate qualifications remain open.

The [reviewed adjacent pending-order increment](Work_Management.md#delivered-deterministic-local-version-queue) adds exact native and original typed/activated pair review/confirmation, shared ordering and atomic audited lineage at the existing store seam.
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
