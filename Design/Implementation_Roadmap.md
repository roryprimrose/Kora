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

### R26 retained attachment inclusive generation/revision predicates - 2026-10-16

From canonical main `74cd8517e5b5412deea706d1125a9b6e156a49e9`, the
[retained catalogue contract](File_And_Folder_Ingestion.md#delivered-retained-attachment-metadata-catalogue---2026-10-11)
adds optional inclusive session-generation, captured-generation and storage-
revision ranges AND-composed with every existing predicate, including the
merged digest/source/profile/captured-time behavior. Positive `HostRevision`
admission, range ordering, validation before exclusion and complete cursor
identity fail closed. Only Confirmed copies match; tombstone rows/details/
serialization suppress former versions. No version grants consent, session/
file-read authority, currentness or proof of current copy existence.
No-read edits, selection retirement, cancellation/revocation and bounded
complete-page behavior retain deterministic portable/SQLite/headless checks.
This consumed I subset changes no other catalogue/domain, storage schema,
source read, provider or enabled runtime profile. Installed accessibility,
broader R26 and E/Q remain separate and unclaimed; no experiment is required.

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

### Bounded native owned-definition revision and observation-time predicates - 2026-10-16

The [owned metadata inventory](Definition_Management.md#bounded-owned-instruction-and-prompt-editor)
AND-composes optional exact native owned-instruction revision and inclusive canonical
UTC Observed From/Until with literal filename/display/source-ID query, use state and
exact digest for Instructions and Prompts. Complete mapped candidates and revision
syntax are validated before exclusion; unknown/unmapped rows gain no revision.
Continuations bind every predicate and the immutable inventory; bounded complete
UTF-8 pages, no-read edit/selection/review retirement, dirty draft preservation,
late-callback cancellation fences and accessible Clear remain mandatory.
Observation time is snapshot metadata only, never freshness, trust, approval,
ownership or use authority. This consumes the existing inventory/editor I subset;
no new execution, provider, persistence, source matrix or Definitions authority
is enabled. Deterministic portable/headless checks cover this increment; full
Definitions management, installed accessibility and unrelated E/Q qualification
remain separate and unclaimed. Saved drafts, Evidence, attachment, session Browse,
Presence and peer conversation/grant/local-event predicates are unchanged.

### Bounded selected-conversation sequence and submitted-time ranges - 2026-10-16

The existing [passive exact-session conversation search](Interaction_And_Sessions.md#delivered-passive-exact-conversation-lexical-search--2026-10-13)
adds optional inclusive original message-sequence and canonical UTC Submitted
ranges, AND-composed with lexical, USER/HOST and typed terminal predicates.
One portable policy owns strict parsing, ordered authoritative bounds and
matching after complete candidate validation. Every bound joins semantic
query/page/cursor identity; admitted bounded scans, whole-row byte budgets,
snapshot revalidation and cancellation/stale-result fences remain unchanged.
Sessions and Compact share volatile accessible controls with no-read edits,
selection/detail retirement and private/source reset, not new authority,
assistant output, body sources, persistence, SQL/indexes or egress.
The consumed I subset is existing admitted conversation pages and deterministic
portable/private-storage/headless tests. No excluded saved-draft, Evidence,
attachment, Browse, Presence, grant-history, local-event or owned-definition
scope is extended. No provider/device/experiment work or unrelated Rxx proof
is required; installed accessibility and broader E/Q remain separate and
unclaimed.

### Bounded native session-list Browse and name availability - 2026-10-16

The [passive session-list contract](Interaction_And_Sessions.md#delivered-passive-session-list-name-and-exact-id-search---2026-10-10)
adds explicit empty-text Browse and Any/Named/Unnamed name-metadata availability,
AND-composed with lifecycle and the merged generation/name-revision ranges.
Browse reuses the canonical-ID ordered 50-row source scan, native 25-result and
64 KiB page bounds and complete semantic digest-bound continuations; it never
selects implicitly or becomes an unbounded inventory. NameSubstring remains
nonempty literal search; revision bounds require Named metadata. Unnamed is not
empty name, revision zero, missing authority or historical deletion.
Full candidate validation, exact-ID mismatch rejection, cancellation,
stale-admission fences and selection retirement remain mandatory.
This is the consumed I subset of volatile passive metadata navigation, not
ownership, permission, freshness, completeness or action authority. Deterministic
portable/private-store/headless accessibility checks gate implementation; no
device, network, provider, experiment or installed-native qualification is added.
Saved drafts, Evidence, All Work, attachment ranges and sibling work remain
unchanged; broader capability enablement and E/Q qualification remain separate.

### Bounded local-event retained queue profile and target predicates - 2026-10-16

Built on freshly fetched main `afdee7f508bc2e186bf56ef5f65cb27711e83bd1`.
The [native loaded-event contract](Proactive_Interaction.md#native-content-free-loaded-event-filters)
adds explicit Any/Absent/exact canonical fixed-read profile and optional exact
catalogue capability target, AND-composed with all existing category/reason/
source/type/origin/queue-reason/identity/revision/generation/lifetime predicates.
Complete event/profile/target validation precedes exclusion; unknown/partial
state fails closed, and absence is never inferred from source omissions or
truncated output. Every predicate joins a versioned continuation digest and
monotonic presentation epochs. Accessible volatile controls preserve no-read
edits, selection/detail retirement, cancellation and stale/private-source
fences. Exact target requires retained target metadata; no runtime target is
inferred. This consumed I subset is historical presentation only, not session
selection, queue admission, readiness, ownership, permission or dispatch.
Deterministic portable/headless tests, exact portable coverage and Release
analyzers gate implementation. Installed accessibility, broader R18 and E/Q
qualification remain separate; no external runtime, authority expansion or
experiment is added. All work and sibling metadata domains are unchanged.

### Bounded native local-event revision/generation/lifetime ranges - 2026-10-16

The existing [loaded-event filter](Proactive_Interaction.md#native-content-free-loaded-event-filters)
adds independent optional inclusive event revision, generation, observed-at
and original expires-at ranges, AND-composed with category/reason/source/type/
origin/queue-reason and exact session/task/subject IDs. Portable strict positive
Int64/canonical UTC admission and per-range ordering reject malformed input
without repair; complete event/view validation precedes exclusion. Native
accessible fields only filter the loaded at-most-eight-row snapshot, without
broker reads, and preserve selection/detail/action retirement, cancellation,
revocation and stale-generation fences. These are correlation/lifecycle
metadata, never navigation, dispatch, retry, review or maintenance authority.
This bounded consumed I subset uses deterministic portable/headless tests;
no real device, network, provider, experiment or new runtime profile is enabled.
Installed accessibility, broader proactive functionality and E/Q qualification
remain separate and unclaimed. Saved drafts, Evidence, attachments, session
Browse, Presence/presenter and sibling metadata scopes are unchanged.

### R26 retained attachment exact correlation and captured-time predicates - 2026-10-16

The [retained catalogue contract](File_And_Folder_Ingestion.md#delivered-retained-attachment-metadata-catalogue---2026-10-11)
adds optional exact observation-source/device-profile GUIDs and inclusive
canonical UTC Captured bounds, AND-composed with Browse/literal/exact-session/
lifecycle/type/copy-status/bytes/digest predicates. Shared canonical parsing,
validation before exclusion, complete query-bound authenticated continuations,
no-read edits and stale/cancellation/selection retirement remain mandatory.
Only Confirmed copies match; unavailable/removed projections suppress prior
source/profile/time, and no correlation field grants consent, ownership,
session/file-read authority or current availability.
Deterministic portable, private SQLite and headless native tests cover this
bounded I subset. No unrelated Rxx completion, physical/provider/experiment
proof or installed accessibility is required or claimed; broader R26/E/Q
remain separate. No schema, body/path read, persistence or egress is added.

### Bounded native session-list numeric metadata ranges - 2026-10-16

The existing [passive metadata search](Interaction_And_Sessions.md#delivered-passive-session-list-name-and-exact-id-search---2026-10-10)
now AND-composes optional inclusive positive authority-generation and named
metadata-revision ranges with name/exact-ID and lifecycle predicates.
Portable admission/order rules, full candidate validation and semantic
digest-bound cursors preserve exact-ID mismatch rejection, canonical ordering,
bounded scans/serialized pages and unnamed semantics. Native accessible fields
retain cancellation, stale-admission/result fences and selection retirement.
This consumed I subset is deterministic metadata navigation only, not ownership,
permission, freshness, historical completeness or action authority. Saved drafts,
Evidence, All Work and other excluded peer scopes are not extended. No device,
network, provider, experiment or new runtime capability is enabled; installed
accessibility and broader E/Q qualification remain separate and unclaimed.

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

### Bounded native local-event exact correlation-ID filters - 2026-10-16

The existing [loaded-event filters](Proactive_Interaction.md#native-content-free-loaded-event-filters)
now compose optional exact session, task and subject IDs with the six existing
metadata predicates. Complete lowercase `D` GUID admission reuses host-ID
parsing; invalid text fails closed, all candidates are validated before
exclusion, and missing tasks never match exact task IDs. Native edits and clear
operate on the loaded snapshot without reads, retire exact selection/details,
and retain cancellation, revocation and stale-generation fences. Correlation
does not retarget work or grant ownership, navigation or action authority.
Deterministic portable and headless native checks are I evidence only.
Installed accessibility, broader proactive functionality and E/Q qualification
remain separate; no network, device, provider or experiment path is enabled.

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

### Bounded positive All Work observation predicates - 2026-10-16

The [passive All Work observation](Work_Management.md#delivered-passive-cross-session-work-observation---2026-10-11)
adds one native positive-content choice for retained queue receipts, tasks or
pending questions, independently AND-composed with existing availability,
Browse/name/exact-ID/lifecycle/Needs answer. Unavailable and omitted-only sources
never satisfy positive predicates or establish absence, completion or authority.
Existing bounded reads, exact continuations, private cancellation/source fences,
selection retirement and explicit non-null Any accessibility remain authoritative.
Portable and deterministic headless/private-store checks gate this consumed I
subset; no storage/model/provider/execution capability is enabled and full work
management, installed-accessibility and existing E/Q qualification remain separate.

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
| Sessions and work | Durable identity/lifecycle/history/retention, six closed metadata queue profiles with retained snapshots, current-run pause and question recovery | Full conversation execution, arbitrary queues/effects, concurrent workers and model tools | [Sessions](Interaction_And_Sessions.md), [workspace](UI_Workspace_And_Windows.md), [work](Work_Management.md) |
| Native workspace and evidence | Selected work and passive cross-session attention overview, metadata/history search, saved-composer catalogue with native name/exact-ID metadata queries, bounded SQLite/DailyLog/CombinedLog/AuthorityAudit inspection and native filters | General Ask Evidence/model retrieval/export; full screen-reader/readability/physical-DPI acceptance | [Workspace](UI_Workspace_And_Windows.md), [display](Information_Display.md) |
| Preferences and audio | Admitted native/exact preferences, consented PTT, installed/local speech, visual recovery and run-only manual call protection | Automatic call detectors, call-policy downgrades, temporary speech exceptions and acoustic qualification | [Configuration](User_Configuration.md), [call policy](Call_Aware_Speech.md) |
| File/clipboard inspection | Immutable previews, folder lexical retrieval, reviewed refresh, one retained file/session, replacement/removal, metadata catalogue, opt-in Markdown, exact citations and native current-file literal Find | Managed sources, model grounding/egress, indexes, screen/OCR context | [Ingestion](File_And_Folder_Ingestion.md), [clipboard](Security_Data_Flows.md) |
| Memory and providers | Original-user memory review/admission and session-only durable storage with [native frozen-list GUID/Scope/Review/Retention metadata filters and portable full-row validation](Model_Providers_Memory_And_Knowledge.md#delivered-native-content-free-session-memory-filters---2026-10-14); host policy/handoff, initial mode, audited app profiles and durable exact-session sparse overrides with native/exact review and subsequent-turn capture | Authentication/qualified hosted adapters, generic/one-turn profile directives, memory content search/global catalogue/model recall or automatic handoff; full memory and installed-accessibility qualification | [Profiles and session resolution](Model_Providers_Memory_And_Knowledge.md#session-overrides-and-effective-resolution) |
| Definitions | Bundles/profile reads with [native bounded snapshot metadata search and distinct compatibility/availability filters](Definition_Management.md#delivered-native-skill-metadata-navigation); owned instruction/prompt Validate/Save/copy/Disable/search; native v2 required literal prompt inputs, whole expanded preview and separate fresh admission | Profile-original writers/forks, broader formats/refinement/applicability/runtime and full matrix unqualified; generation unavailable | [Management](Definition_Management.md), [editor](../docs/instruction-editor.md) |
| Captions and local events | Off-default playback-bound captions, run-only pin/display choice; trusted visual notices, cached maintenance and routine quiet mode | Sentence alignment, general rich rendering, proactive speech and broad reminder scheduling | [Display](Information_Display.md), [proactive interaction](Proactive_Interaction.md) |
| Distribution | MSI/custom Burn packaging, release/source-tool automation and reviewed build-only source workflow | Full installed/protected-byte/runtime qualification and install-capable updater | [Distribution](Distribution_And_Updates.md), [installer](../installer/README.md) |

Session permission levels and expanded skill/instruction authoring are
[proposed contracts](Session_Permission_Levels.md), not new runtime authority.
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

### Bounded native saved-draft revision/UTF-8 metadata filters - 2026-10-15

The [saved-draft catalogue](Interaction_And_Sessions.md#saved-conversation-draft-catalogue--2026-10-13)
now composes optional exact positive revision and inclusive UTF-8 byte and Unicode scalar bounds
with Browse/name/exact-ID and All/Active/Done. One portable contract owns strict
parsing and matching; validated metadata scans and signed continuations retain
full private/source/authority binding. Edits revoke observations without I/O;
invalid input refuses reads and zero matches remain truthful. This consumes only
the existing catalogue I subset. Scalar bounds use only validated exposed summary
metadata, with the authoritative conversation maximum and the same strict numeric
parser. No code-unit filter, body import/inference, schema,
index/cache, aggregate quota/count, mutation, activity renewal, model/tool/speech
route, new source authority or installed accessibility/E/Q qualification is added.

### Bounded saved-draft exact source/profile metadata predicates - 2026-10-15

The [saved-draft catalogue](Interaction_And_Sessions.md#saved-conversation-draft-catalogue--2026-10-13)
adds optional exact observation-source and device-profile GUID fields, ANDed with
Browse/name/exact session-ID, lifecycle, saved revision and both length ranges.
One portable policy accepts empty Any or complete nonempty canonical lowercase
`D` GUIDs, never repaired input. Filter digests and signed continuations bind every
predicate under existing source/private/control/head/authority fences. Every
scanned summary/name and authority prefix is validated before exclusion; edits
perform no read and immediately revoke observations and delayed callbacks.
This consumes only the existing catalogue I subset. IDs grant no ownership,
identity or authority. No body read, aggregate total, autosave/reload/mutation/
submit, schema/index/cache, persisted filter, filter logging/tags/audit payload,
model/tool/speech/clipboard/export route or activity renewal is added.
Headless accessible controls and deterministic tests are implementation evidence,
not installed-accessibility or E/Q qualification. Presence/native-presenter,
All Work follow-up, memory, attachment, Evidence and independent build/review
work remain outside this boundary.

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

[Category, exact reason, authoritative source/type, request-origin and queue-reason filters](Proactive_Interaction.md#native-content-free-loaded-event-filters)
compose over only the existing already-loaded bounded content-free rows.
The 2026-10-15 Source/Type increment preserves exact AND and zero matches
for impossible pairs, with no new source read or normalized choice.
The bounded Origin/QueueReason increment adds exact `RequestOrigin` and explicit
portable Any/None/Exact eligibility predicates. None means not applicable for
questions/maintenance, never success or authority; all six predicates are AND
and full row/view validation precedes exclusion.
Edits immediately retire exact selection; source/privacy/workspace changes,
refresh, failure/cancellation and stale callbacks remain fail closed.
Matching/empty counts do not replace broker omissions or quiet status.
No read/index/persistence, event body, logging, route, authority, dispatch or
activity renewal is added. This consumes the delivered local broker's I
subset; hardware, experiments and installed accessibility/E/Q qualification
are not implementation prerequisites or claimed by this increment.

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

The [passive overview contract](Work_Management.md#delivered-passive-cross-session-work-observation---2026-10-11) now composes the authoritative Sessions/saved-draft literal saved-name or canonical exact-GUID query with All/Active/Done and Needs answer. Explicit Search/Refresh/Next retains ten-session canonical-ID scans, fifty task/question records per session, complete 64 KiB pages, truthful zero-match continuations, independent unavailable sources and private admission/source invalidation. Query text is volatile; typing performs no read and close clears it. Focused portable and headless/private-SQLite tests exercise the delivered boundary. No global/body search, atomic all-clear, model context, execution/provider authority or installed-accessibility/full R14 qualification is claimed.

The same bounded All Work path adds exact native observation availability: Any / Observed / Source changed / Read unavailable / Output bound, AND-composed with lifecycle/query/Needs answer. Portable matching refuses undefined availability; all scanned metadata and admitted observations retain validation before exclusion. Exact availability is continuation-bound, edits read nothing and revoke private rows/selection/late callbacks, and unavailable never means empty work. No schema, cache, persistence, telemetry payload, mutation, runtime authority or aggregation is added. Deterministic portable and headless native/private-source checks are the implementation gate; installed-accessibility/full work-management qualification remains separate.

### Bounded native admitted Detail literal Find - 2026-10-14

The [Detail literal Find contract](Information_Display.md#native-admitted-text-literal-find) delivers Find first/Next/Previous/Cancel/Clear on only frozen admitted in-memory text, with retained original USER and separate typed HOST targets. Shared snapshot query policy, nonwrapping exact navigation, source/private/close fences, inert focus/accessibility status and reader-scale tests preserve origin and bytes. No broader admission, source read, persistence/logging/export/clipboard, provider/egress, generated response or installed/accessibility/hardware/full qualification is claimed.

### Bounded native Evidence exact-field filters — 2026-10-14

The [Evidence contract](Information_Display.md#delivered-bounded-native-evidence-inspection) exposes existing EventId, Category and ActionId through optional exact native filters with source-specific unsupported recovery and the existing snapshot/cursor/private-closure lifecycle ([native and real-reader tests](../tests/Kora.Windows.IntegrationTests/EvidenceAdvancedFilterTests.cs)).
No schema/index, authority, execution, egress, scan-budget or qualification boundary changes; general Ask Evidence/export and installed/accessibility qualification remain unavailable.

### R12/R14 exact-session conversation/composer foundation — 2026-10-12

The bounded [R04/R05 retained original USER / typed HOST detail handoff](Information_Display.md#delivered-retained-original-user--typed-host-details---2026-10-14) is delivered through canonical exact-record admission and the existing native viewer. Explicit shared-row actions preserve body bytes, typed provenance, immutable reference/revision/digest, private/source revocation and passive Done. No generated response, new copy/export authority, provider/egress or full R12/R14/runtime/hardware/accessibility qualification is claimed.

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
The [compact projection](UI_Workspace_And_Windows.md#delivered-selected-conversation-lexical-search--2026-10-13) now exposes the same authoritative private Search/Next/Cancel/Clear state as Sessions, with no automatic reads, second composer baseline, provider/runtime enablement or installed/physical qualification.

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

### R26 exact retained attachment SHA-256 predicate - 2026-10-16

The [retained metadata catalogue](File_And_Folder_Ingestion.md#delivered-retained-attachment-metadata-catalogue---2026-10-11)
adds an optional exact canonical original-byte digest predicate composed with
Browse/literal/exact session ID/lifecycle/media type/copy status/stored-byte
bounds. One portable strict syntax rule is shared with file-reference validation.
Only Confirmed copies match; unavailable/removed provenance never displays or
serializes a prior digest, even from malformed objects. Fully validated finite
scans, keyed signed-continuation binding, stale/cancellation fences and native
no-read edits/selection retirement remain mandatory.
Deterministic Core/Application/private SQLite/headless accessible-control tests
qualify this bounded I consumer. No schema/index/cache, body/path/hash read,
mutation, aggregate, persistence, audit/diagnostic payload, model/network/device
or authority expansion is introduced. Broader R26, installed accessibility,
physical storage and E/Q gates remain unchanged; no experiment evidence is
required, rerun or retired, and unrelated live ownership scopes remain separate.

### R26 exact retained attachment media-type filter - 2026-10-15

The bounded exact retained media-type increment adds All / `text/plain` / `text/markdown`, composed with exact ID, lifecycle, removed-slot and literal filters inside the existing fully validated scan. One extracted Core strict type rule, authenticated query/authority digest continuations and immediate native edit revocation preserve fail-closed nonmatch/corruption admission; tombstones match only All. Deterministic portable/private SQLite/headless control checks cover this consumer only. No schema/index/cache, body/OCR/path disclosure, count-total claim, activity renewal, model/tool/speech route, authority expansion or broader R26/installed-accessibility qualification is added.

### R26 private native retained metadata literal search - 2026-10-14

The [retained attachment catalogue](File_And_Folder_Ingestion.md#delivered-retained-attachment-metadata-catalogue---2026-10-11) adds explicit volatile case-sensitive literal filtering of already exposed Session/source/revision/item IDs, digest and media type, composed with exact ID/lifecycle/removal filters. Shared Unicode bounds, fully validated finite scans, keyed authenticated query continuations, source/private revocation and separately explicit exact navigation remain mandatory. Removed tombstones never match literal search; no query content is serialized, persisted, audited, logged, exported or sent to a model.
No hidden filename/path disclosure, file-content/OCR search, index, model grounding, authority expansion, installed/accessibility/hardware or full R26 qualification is claimed. Focused portable/Application/private SQLite/headless native evidence covers this consumer only; existing limits, corruption failure and experiment/enablement gates remain unchanged.

### R07 native immutable clipboard literal Find increment - 2026-10-14

The [clipboard contract](Security_Data_Flows.md#delivered-r07-local-clipboard-preview---2026-10-07) adds private native First/Next/Previous/Clear over the exact existing capture, sharing portable literal query/offset policy with file Find.
One inert focused case-sensitive span, unchanged spaces, bounded strict Unicode, nonoverlap/no wrap and exact capture/view/privacy/owner/call fences apply. No clipboard read/write/reuse/revoke query route, provider/egress/context/history/index/persistence, timer or activity renewal is added.
[Portable](../tests/Kora.Core.UnitTests/Context/ClipboardLiteralFindTests.cs) and [headless native/controller](../tests/Kora.Windows.IntegrationTests/ClipboardLiteralFindWindowTests.cs) evidence is bounded to this consumer; no experiment, installed/privacy, hardware/acoustic or inference qualification gate is closed.

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
| R22 bounded Compare | Explicit same-kind owned frozen-baseline/raw-draft native [Compare](../docs/instruction-editor.md#compare-the-open-draft-without-saving); historical memory only, Close-only, no disk reload/mutation/activation/clipboard/model authority. No algorithmic patch, profile writer or expanded qualification | Existing owned editor / Validate |
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
