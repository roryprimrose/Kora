# Human Interaction and Persistent Sessions

Status: agreed product direction; bounded durable question/grant/session metadata,
exact session/task controls and consolidated schema-v5 task/question/queue/required-audit
authority with bounded ordered interaction history implemented. The deterministic
local-version queue and its exact pending cancellation are delivered; cancellation
of the separate genuine current-run local-version pre-dispatch question wait
remains gateway-bound. Full conversation, effect and model-assisted routing
integration remains proposed.

Related: [Architecture](Architecture.md), [Work Management](Work_Management.md), [Information Display](Information_Display.md), [Security](Security_Data_Flows.md), [User Configuration](User_Configuration.md), [Acceptance Criteria](Acceptance_Criteria.md).

This is the canonical interaction/session contract. It replaces the former single-conversation, single-executor, memory-only-history direction.
Windows login sessions, voice capture generations, provider/SDK conversations, and Kora work sessions are different identities; none substitutes for another.

## Current Implementation and Design Gap

The [R13 fixed local-version queue](Work_Management.md#delivered-deterministic-local-version-queue)
addresses existing immutable session IDs directly. Pending capacity, FIFO,
fair manual admission, revisions, dependencies and restart interruption are
host-owned; names/selected windows never substitute for IDs. Queue slots cover
only this read-only profile, not providers, workers, audio or arbitrary resources.
Native **Refresh selected work**, **Enqueue local version**, **Dispatch ready local
versions fairly**, exact pending cancellation and separately labelled confirmed
clear use the same workflow as typed/current-name activated commands. Selection
alone neither enqueues nor dispatches. New work in Done/Removed sessions is denied.
Passive history/queue inspection never renews activity or restores authority.

The [R14 coordinated native work increment](UI_Workspace_And_Windows.md#delivered-authoritative-sessions-work-surface---2026-10-09)
delivers list-plus-selected-session work, atomic bounded queue/task/question
observation, stable identity/revision/order, observed eligibility/deadlines/
capacity and explicit recovery/gaps. Native pending cancel/remove/clear and
manual dispatch reuse exact command workflows; in-flight selection changes
invalidate admission. Pending question identities stay visible independently
of history/evidence and never gain a workspace reply/review target.
Five-second passive work refresh preserves focus and does not disable native
input, renew cancellation inspection, extend activity, resume or dispatch.

The runnable bootstrap has one response title/body and latest transcript in `MainViewModel`, displayed by `ResponseWindow`.
Typed and recognized spoken commands converge on the deterministic built-in command router.
The updated bootstrap also handles unmatched requests with a verified local model, including clarifying questions and host-validated registered action/grant proposals with once/session/always choices.
These named model-action preferences are not the future executable/script digest-bound grants; direct exact lock is still ungated while model-suggested lock asks for approval.
The compact surface has a text field, Run, and Dismiss, with positioning and auto-hide preferences.
The separate Documentation window renders trusted embedded documentation, not arbitrary session artifacts.
Its explicit **Open details** action now opens a bounded passive native viewer
for the selected immutable embedded page. The viewer's process-local reference
is not a durable session/history identity. A typed finalized-response handoff
uses existing session/request/task IDs but is not composed into bootstrap
response dispatch. See the
[delivered native profile and authority handoff](Information_Display.md#delivered-native-profile---2026-10-06).
Viewing/search/copy/close never changes session activity, lifecycle, pending
questions or grants; privacy closure clears the viewer independently.

The R05 [question service](../src/Kora.Application/Interaction/HostQuestionService.cs)
now implements bounded single/multiple-choice and text questions, explicit
draft/submit/cancel, host owner/revision checks and expiry. Its
[authorization service](../src/Kora.Application/Interaction/HostAuthorizationService.cs)
uses exact host-resolved proposals and a separate atomic storage/audit seam;
it does not use legacy action-name preferences.
The bounded [production SQLite adapter](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.cs)
now persists typed questions/options/drafts/answers, exact proposals/grants and
minimal session authority. The existing services are registered with that
adapter. A bounded native question/review route now composes the existing
durable local-version query; no effect dispatcher is activated. See the
[native question boundary](Interaction_Fallback.md#delivered-bounded-native-question---2026-10-07).
There is no general typed form service,
full conversation/composer UI, general concurrent provider/effect scheduler,
or model-facing session tool API. The bounded history, native work ledger,
question and passive detail slices do not qualify those broader capabilities.

### Delivered Bounded Ordered Interaction History - 2026-10-09

The authoritative `SessionHistoryEvent` projection records only actually
host-committed question snapshots/final answers, decision metadata and task-state
receipts. It is **not a full conversation store**: bootstrap user/model messages,
response bodies and general conversation composition were never durably admitted
and are unavailable, not fabricated from diagnostics. A successful task receipt
does not prove an external effect. Draft transitions have decision metadata, not
final-answer text; migrated current records are explicitly `Baseline` snapshots.

Schema v4 adds per-session history heads and immutable event/source projections
under the existing authority lease and transaction. Store transitions, not a
model, name, diagnostic mirror or arbitrary append API, author the events.
Stable event GUIDs, session-local monotonic sequences, source IDs/revisions,
session generations and SHA-256 provenance survive restart. Required typed
audit/task provenance is checked; audit is not mined to synthesize turns.
Validated v1/v2/v3 migrations preserve original authority/audit/task bytes.
The v3-to-v4 transaction adds an explicit gap followed by labelled current
snapshots; GUID sorting is not presented as pre-migration chronological order.
Unknown schemas, inconsistent projections/heads and failed migrations are
unavailable; a rollback/retry performs storage maintenance, never execution.

`session history <exact-id>` and `session get <exact-id> <event-id>` share
`SessionWorkspaceService` with the native Sessions viewer. Typed and currently
configured-name activated command prefixes select only the grammar; names never
resolve sessions. These routes write no control intent, terminal receipt, audit,
meaningful-activity timestamp or lifecycle state. Private ownership/inspection
and originating channel/revision are rechecked before presentation.
Native history uses an explicit exact-ID field, bounded inert text, keyboard
buttons and accessible names; it changes no question, approval, focus or voice
target and provides no reply/playback controls.

Pages default to 25, at most 50 records/64 KiB complete output. Continuations
bind the exact session, authorization generation, original sequence ceiling and
last returned sequence; later appends cannot expand a snapshot. Lifecycle
changes invalidate continuations. Oversized individual content is explicitly
`Unavailable` with stable metadata/citation retained, never silently truncated.
Exact event lookup cannot cross sessions. Gaps, metadata-only, unavailable and
redacted records are distinct. Passive reads do not resume a Done session.

Logical disposition retains readable exact history citations but atomically
redacts history source/content alongside the existing removal of live question/
answer/name/observation/scoped-grant rows. Event ID, sequence, source revision
and provenance digest remain stable; the Removed identity remains excluded from
the live workspace and cannot resume or gain meaningful appends. Its known ID
can inspect only redacted history. The preview digest includes history, excluding
only the confirmation's own fresh control receipt; concurrent addressed work
invalidates the preview. Cancellation/audit/admission failures roll back together.
No history timer, automatic purge, apply-now or physical-copy erasure is added.

Shared-profile skill inspection, immutable local file previews and clipboard
inspection remain volatile/read-only control content, not conversation turns,
attachments or model input. Their paths/text/provenance are never imported.
History never reconstructs captions or authorizes replay: only fresh separately
admitted actual playback can create a caption. History content never enters logs,
activity tags/baggage or model context, and retrieval has no network/model
dependency. Full composer, search, model history reasoning, Ask Evidence, broad
export, queues and scheduler remain unavailable. Unique storage/interruption/
capacity/copy evidence in the storage experiment is retained, not deleted or
claimed replaced by this increment.

### Delivered Minimal Sessions Workspace - 2026-10-07

The bounded deterministic command extension now shares this workspace's host
service and guarded transactions: typed and activated voice expose
`session help/list/status/inspect/create/rename/done/resume`. The single
[typed grammar](../src/Kora.Core/Commands/SessionCommand.cs) owns quoting and
input/page/result limits; [user syntax and recovery](../docs/commands.md#bounded-exact-id-session-commands)
describe actual availability. Exact immutable IDs and explicit generations/
metadata revisions are required; names remain labels only. No implicit
selected-window, title or approval target exists. Pending bootstrap questions
and approvals block these commands without cancellation or retargeting.
Every accepted command owns fresh deliberate lineage and a durable control
intent; observational reads never change authority. Voice enablement/consent,
origin, private presentation and call/recovery revisions remain checked.
Protected-call voice mutations are explicitly unavailable, not deferred.
Full session routing, transcript persistence, scheduler, general effect cancellation, recoverable-copy deletion,
retention and model-facing session tools remain unimplemented.

### Delivered Bounded Exact-ID Logical Disposition - 2026-10-08

The native Sessions workspace has separate **Preview logical disposition** and
**Confirm logical disposition** actions. This is explicitly not full R12
deletion. Preview resolves the exact existing ID, displayed authorization
generation and metadata revision under private admission, enumerates live rows
and discloses retained data. It records no control intent and changes nothing.
The Application host holds the single-use preview; confirmation must be fresh
LocalUi input with the same proposal and unchanged control revision. Voice,
typed `session delete`, models, title matching and implicit selection are not
admitted deletion routes in this increment.

The Windows writer revalidates authoritative task/question state and a framed
digest of all addressed live authority records and task revisions, not a
bounded UI page. Unknown IDs, stale generations/metadata, any intervening
addressed work (even if completed), pending questions including expired ones,
nonterminal or Unknown work, lost ownership/privacy/call admission, missing or
corrupt storage fail closed. It never cancels, reconciles or abandons work to
make disposition eligible. Resolve supported pre-dispatch waits explicitly;
general uncertain-effect disposition is unavailable.
The desktop control gate consumes the existing `IsProtected` call policy,
including uncertain manual-call evidence even when automatic state reads
Clear/Unavailable; independent passive inspection does not become mutation
authority.

Under the shared authority lease and one transaction, disposition advances
generation, writes a non-reusable Removed tombstone, removes only that session's
live metadata/name, host observations, questions/drafts/answers, admitted wait
bindings and scoped grants, and commits both fresh control terminal success
and required typed audit. Independent Perpetual records, unrelated sessions,
task/event provenance and audit chain remain unchanged. Removed sessions are
not live-workspace browsable or resumable; the later bounded history increment
permits exact-ID inspection of redacted citations only. The task writer rejects new intents and late
outcomes; interaction/snapshot/lifecycle paths cannot append to or recreate a
Removed identity. A competing append wins before disposition and blocks or
invalidates it, or loses after the committed tombstone.

**Retained/unavailable:** opaque tombstone and task/event IDs/states/revisions,
content-minimising authority audit/digests, independent evidence/diagnostics,
Perpetual provenance, inert legacy migration storage, SQLite journals/free pages
and any copied database remain. General conversations/history, managed session
artifacts/snapshots/indexes/caches and inventoried managed-backup deletion are
not implemented. User exports/provider copies are outside local deletion.
No forensic, cryptographic-erasure or full R12/A3 deletion acceptance is claimed.
No retention setting, inactivity timer, automatic purge or apply-now behavior
is added. See [the user workflow](../docs/windows-and-tray.md#logical-session-disposition).

Precommit audit/gate/cancellation failures roll back the tombstone, live rows
and terminal success together; an incomplete control intent is recoverable
without execution. After COMMIT, terminal success is already durable, so
restart never appends a recovery receipt into a Removed session. Receipt or
commit uncertainty remains an explicit error, not rollback or automatic retry.
Owned-process interruption, actual production SQLite, native-state and portable
host tests maintain these boundaries; installed/live visual/accessibility and
physical power-loss acceptance remain open.

### Bounded Authoritative Task Observation and Pre-dispatch Cancellation

The independent native evidence inspector's opt-in **AuthorityAudit** source
reads this same committed schema-v3 interaction authority, not a diagnostic
copy. Task/question/grant changes are shown as their committed typed revision/
digest references and recorded outcomes; no historical payload or effect is
fabricated. It never records control intent, answers questions, retargets
approvals, refreshes meaningful activity or queries the frozen legacy ledger.
See [bounded source and cursor semantics](Information_Display.md#delivered-bounded-native-evidence-inspection).

Exact typed and activated voice `task status <session-id> <task-id>` and
`task inspect <session-id> <task-id>` expose only the addressed existing
durable task, session generation, admitted source/current-run distinction
and complete bounded question record when present. Unknown/foreign IDs do
not resolve by title, window, model output or trace. States are receipts,
not inferred progress, planned steps, ETAs or physical effect cessation.
The single session-command grammar owns the 1,024-byte input and 64 KiB
complete-output bounds; inspection does not extend meaningful activity.

`task cancel <session-id> <task-id> <task-revision> <generation>
<question-id> <question-revision>` and the native workspace's **Inspect exact
selected task** / separate **Cancel inspected pre-dispatch wait** share one
host service. Fresh deliberate original-user control intent, current
ownership/privacy/channel/call-revision admission and all exact conflict
tokens are required. Safe cancellation introduces no effect and does not
promote unknown user IDs into a fabricated host session: the actual existing
session is resolved under private admission before recording fresh control
intent. Admission is checked again after resolution and at COMMIT. It does not
require the lifecycle mutation's unprotected-call permission; voice still
requires admitted activation. Legacy bootstrap questions/approvals keep their
existing targets and blockers. Always-available stop/recovery is unchanged.

Only an already admitted current-run native local-version wait is cancellable.
Its question precedes dispatch. A separate host-owned run/wait record, not
question purpose/source text, proves the admitted source. The answered exact
key alone reaches the pre-dispatch gateway. Task terminal cancellation,
question revision/status, target observation revocation and required trusted
typed audit share one transactional store and COMMIT. No grant is created or
consumed; unrelated work/sessions and independent Perpetual grants survive.
Answer/cancel/dispatch/revision/expiry races have one truthful winner; stale,
foreign, expired or previous-run targets refuse. Late/disposed input cannot
resume or dispatch. A possible committed outcome followed by receipt failure
requires inspection, never a rollback claim or automatic retry.

Schema v3 consolidates the existing ordered task ledger into the private
interaction partition, preserving identities/events, session generations,
metadata, questions/grants and exact audit bytes. The fully validated legacy
task ledger is frozen before a complete destination schema transaction and
retained inert. Interrupted migration can revalidate/retry storage maintenance
only; lost/corrupt authority is not reconstructed as empty state. Evidence
projections/retention remain independent. Previously dispatched/Unknown work
stays uncertain/quarantined and is never relabelled Cancelled. This bounded
slice closes no full R12/R13/A acceptance, scheduling, queues, workers, content
retention/deletion, runtime/model-management execution or model task tools.

**Sessions** in the tray, exact **open sessions** (configured-name prefix
supported), and **Ctrl+Shift+S** in the compact response open a native
workspace over the existing production authority partition. Refresh and
keyset Next expose Active/Done IDs and authorization generations, not
invented titles or general conversations. The selected passive detail reads
actual typed questions/options/drafts/answers, request/task IDs, revisions,
channels, generations and current durable task records. Separate selected-ID
Evidence uses the existing bounded diagnostic/audit/span/link reader; missing
session/conversation evidence remains explicitly unavailable. Pages default
to 25 records and the store accepts at most 50. They are observations across
separate bounded reads and independent evidence projection, not an atomic runtime ledger; refresh for concurrent
changes. Passive pages require existing private partitions and never create a
replacement when storage is missing. Removed authority tombstones are not
offered as browsable sessions.

Reading, selecting and keyboard navigation never admit a reply, select an
approval target, update activity, resume, or restore model/provider context.
The original minimal slice had no composer, rename/create metadata, scheduling, archive timer, full deletion,
retention, export or model lane is added. Full R12 conversation/work/queue/
retention/delete and full coordinated R14/native acceptance remain open.

**Mark selected ID Done** and **Explicitly resume selected ID** are distinct
trusted local-user initiations. The host creates a fresh exact subject-bound
control intent and activity; the expected generation from the displayed
observation is only an optimistic conflict token. Control intent requires an
existing private task ledger, and lifecycle requires existing session authority;
neither recreates missing storage and guesses that forgotten work is idle.
Current private desktop
ownership, unlocked privacy admission and unprotected known call policy,
including its revision, must still hold at the authoritative commit.
The consolidated authority lease/connection stays held through the single
COMMIT; no second nested interaction lease is acquired. A bounded maintained task query rejects any other
nonterminal or Unknown task (including recovered outcome-unknown work);
only the exact fresh revision-1 control intent is excepted. Pending questions
also block, even when their deadline passed. Nothing is auto-abandoned or
synthetically resolved to permit Done/resume. Unsupported work state or
corrupt/overflowed authority is refusal, not an empty idle result.

The existing lifecycle transaction commits generation advancement, scoped
grant invalidation, fresh-observation removal and required typed authoritative
audit together. Each Done/resume advances generation. Independent Perpetual
records and typed history are preserved. Resume never replays tasks, revives
old questions/approvals or transmits old context. Commit/audit/receipt failure
is visible; cancellation is not a claimed rollback after a possible commit.
Restart reads durable state and performs existing no-replay recovery, not
automatic lifecycle completion. Privacy closure cancels and clears the
workspace, without revealing old content on unlock.

#### Bounded validation and experiment disposition

On the original isolated `281393c` baseline, root Release/analyzers completed with zero
warnings/errors. Maintained suites passed: Core 411, Application 1,322 and
Windows 728, with fresh-only portable **100% line and branch coverage**.
Actual private production SQLite tests cover bounded pages, typed history,
cross-session isolation, Pending/live/Unknown blockers, stale generations,
call/ownership revision changes at commit, required audit failure, concurrent
lifecycle/intent leases and before/after-COMMIT owned-process kill/reopen for
Done/resume. Fake native state covers close/privacy and cancellation-ignoring
late reads. No live app, user-data, microphone, clipboard or OS-session trial
was run; native visual/screen-reader/DPI acceptance remains open.

After rebasing onto peer-merged `1506b7e`, the combined maintained suites passed
Core 450, Application 1,327 and Windows 731, again with fresh-only portable
100% line/branch coverage. Missing-ledger lifecycle admission is now tested:
control cannot initialize a replacement task ledger and infer forgotten work
is idle, or recreate missing session authority.

R02's generic atomicity and intent/no-receipt expectations are maintained
against production semantics, rather than treated as production admission.
No executable proof file is removed: its intertwined SQLCipher/envelope,
WAL engine comparison, crypto/artifact/keyed-backup/rekey/leakage/native-provider
cases remain unique. See the
[equivalence assessment](../experiments/r02-storage-proof/README.md#maintained-minimal-sessions-equivalence-assessment---2026-10-07).

### Delivered Bounded Session Metadata and Explicit Creation - 2026-10-07

On dispatch baseline `d0a8e82` (#68), the native **Sessions** surface adds a
name draft, **Create empty Active session**, and **Rename selected ID** over
the shared [workspace service](../src/Kora.Application/Hosting/SessionWorkspaceService.cs).
Create deliberately allocates a new immutable ID, Active generation 1 and
metadata revision 1. It creates no executor, execution task/context, model
conversation, question, approval or permission. The fresh administrative
control intent/terminal receipt is retained by the existing task ledger for
audit and no-replay recovery; it is not dispatched work.

Names are intentional private user content, persisted only in the interaction
partition, never diagnostic messages, activity names/tags or raw audit
envelopes. The authoritative [domain rule](../src/Kora.Core/Hosting/SessionName.cs)
requires nonblank NFC Unicode, no surrounding whitespace, valid UTF-16,
at most **120 Unicode scalars / 480 UTF-8 bytes**, and no control, format,
line-separator or paragraph-separator characters. Invalid input is rejected,
not truncated, trimmed or normalized silently. Duplicate names are allowed:
neither a title nor the selected window resolves authority. Bounded keyset
pages expose name, metadata revision, exact ID, generation and lifecycle;
selection remains passive and retains the existing separately paged typed
question/task detail. No transcript, summary or arbitrary history schema is added.

Rename requires the exact subject ID, expected authorization generation and
expected metadata revision (0 only for explicitly unnamed legacy records).
Its atomic transaction changes only metadata/revision and the required
content-minimizing typed audit commitment. It does not resume Done sessions,
alter generation/grants, revive questions, dispatch work or extend a meaningful
activity clock. Rename can label a session with Pending/Unknown work without
resolving it; those records still block Done/resume. No inactivity clock or
automatic retention behavior is delivered by this slice.

Create and rename require fresh original LocalUi/ActivatedVoice lineage,
existing private task/interaction stores, current ownership/privacy and known
unprotected call policy/revision, rechecked immediately before COMMIT. Native
controls use the same host service; only **open sessions** is currently admitted
as a typed/voice session command. Name-based targeting, spoken Create/Rename,
model routing and a session composer remain unadmitted. Selection never
retargets global input or approval. Privacy closure clears the draft and all
late content. Failure/cancellation is visible; a possible committed outcome
is inspected before retry, not replayed or claimed rolled back.

The private interaction application ID and partition remain unchanged.
Schema **v1 -> v2** validates exact legacy schema, integrity and all existing
authority/audit records before transactionally adding the bounded metadata
table and version. Legacy IDs remain explicitly unnamed, not fabricated
titles. The migration preserves generations, questions, observations, scoped
and independent Perpetual grants, task records and audit bytes. Unknown/old
unsupported versions, malformed schema/content, downgraded metadata commits,
missing committed metadata, stale row/audit commitments and missing files
refuse without initializing replacement authority. Schema maintenance is
not session admission or meaningful activity.

Maintained tests cover actual owned SQLite durable reopen/migration,
preservation, stale/competing revisions, duplicate-name exact-ID isolation,
privacy/call revision races, cancellation and required audit failure; native
VM/static composition covers explicit controls, passive selection and content
clearing. Owned-process kills before/after Create, Rename and migration COMMIT
verify atomic reopen and no automatic replay. Release/analyzers and portable
**100% line/branch** coverage pass. Native visual/screen-reader/DPI and installed
privacy trials remain open. This is not full R12, R14 or A3 acceptance.

No storage experiment executable is removed: production metadata/migration
and kill/reopen checks supersede only the matching generic expectations,
not unique SQLCipher/envelope, WAL comparison, artifact/backup/rekey/leakage,
DPAPI or native-provider evidence. Historical receipts remain unchanged.

### Bounded R05 Foundation Boundary

Questions, answers and drafts use existing host session/request/task/question
IDs and a positive revision. Every accepted draft edit advances that revision;
submission of an earlier draft conflicts instead of overwriting newer input.
The service accepts explicitly addressed UI or activated-voice replies against
the same key. It does not implement generic spoken-reply focus or a speech/UI
parser or generic voice focus. Trusted local native input and exact-record
review are composed for the bounded version query; general channel acquisition,
voice targeting and native/speech acceptance remain integration gates.

Clarification submission never creates a grant, including when text, purpose,
source labels or option labels claim approval. Only the authorization service
can bind a question to the transaction's current host operation and accept the
exact scope decision. Nothing calls an action handler or dispatches an effect.
Expired/closed questions and changed question/proposal/session generations
reject late replies; explicit cancellation never approves anything.

The [atomic seam](../src/Kora.Core/Storage/IHostInteractionStore.cs) requires
committed matching intent, Active durable work-session identity, live host
activity, authoritative current policy/operation snapshots, and atomic typed
audit plus question/grant commits. A nonterminal task's root intent may omit
invocation ID; exact proposals/questions carry their own host invocation while
preserving root request/session/task/origin. A supplied intent invocation must
match, and terminal/cancelled task records cannot admit new decisions.
Portable tests retain the serialized adapter; Windows tests additionally
exercise the existing services with actual private SQLite transactions,
reopen, owned-child interruption and races. Done/authority removal/resume
must advance the session authorization generation; restart of an Active
persisted session preserves it. Pending questions and Once/Session grants
cannot regain eligibility after resume. Perpetual records are independent
and have no expiry/retention/eviction field.

The [durable slice handoff](Implementation_Roadmap.md#r04r05-durable-interaction-and-minimal-session-authority---2026-10-06)
defines schema/lease/audit ownership. The native presenter adds trusted
local-UI review/input over those services without schema or recovery changes.
Immediate pre-effect dispatch revalidation, complete source/resource review
and general authority composition are still required. This slice does not
alter ordinary bootstrap dispatch, microphone capture, direct lock, power
operations or execution.

### Durable Authority and Typed Presenter Handoff

`InteractionStorageV1/interaction.db` is a separate private, standard-SQLite
authority partition. Its single transaction commits changed questions/grants,
session generation or host observations and typed, hash-sequenced audit plus
the audit head. Audit payloads contain host/W3C identities, intent/session
revisions, decision references and state digests, not question/answer content.
The task partition lease is held while committed matching nonterminal intent
is read and until the interaction COMMIT completes. Cancellation/terminal
task writes use that same lease. There is no cross-database write or diagnostic
sink success fallback. The independent evidence/file sinks are not a source
of grant authority.

The host explicitly creates the durable session, then publishes a fresh
trusted policy/exact-operation snapshot with an optimistic observation revision.
Content observations revoke affected grants and close affected pending
approvals in that same commit, before the changed snapshot is exposed.
Snapshots from a previous adapter lifetime are not eligible until freshly
host-resolved and re-admitted. Active session generation survives restart;
Done/resume each advance it and never revive old scoped authority. Authority
removal advances generation and leaves a non-reusable session tombstone.
Perpetual records have no session foreign key, expiry or retention/eviction;
their approved-proposal deadline is provenance, not a grant lifetime.

`ReadQuestionsAsync(sessionId)` returns passive `HostQuestionRecord` history:
immutable host key/revision, typed spec/options, bounded draft/answer, channel,
generation, status and optional exact proposal. It neither refreshes activity
nor authorizes a reply. A future presenter must pass the returned exact key
to the existing services under a live matching host request, reacquire trusted
input and handle Conflict/Expired/Denied. Rendered labels, detail artifacts,
viewer navigation, provider IDs and trace headers cannot create authority.
No R14 chrome changes or generic foreground voice parser are required by
this handoff. Native immutable review/readback remains a later R05 gate.

The removal operation here removes authority rows, not every recoverable
content copy; PERSIST journals/free pages and future backups still require
the full R12 inventory/deletion contract. No inactivity clock, history/search
UI, queue scheduler, automatic purge, arbitrary dispatcher or legacy-grant
migration is delivered. Audit due dates are recorded, but pruning/anchors
and protection against coherent whole-store replacement remain D-008/R04
work; a local hash chain is not same-user/admin tamper prevention.

## Interaction Principle

Every supported Kora workflow has equivalent verbal and mouse/keyboard-accessible operations.
Users can interact entirely by voice, entirely through UI, or change channels at any step without restarting the workflow.
Voice-first means convenient hands-free operation, not reduced UI capability or compulsory speech.
Input and output choices are independent: typing does not require speech output; visual-only output does not mute input.

Questions, answers, summaries, detail navigation, approvals, session management, and settings all use the same host services and validation.
No UI action requires a spoken acknowledgment, and no ordinary Kora approval requires a click merely because it is high risk.
The physical limits of a closed/unavailable microphone and mandatory OS/provider authentication, credential entry, and secure-desktop prompts remain explicit exceptions.
The [in-call settings origin gate](Call_Aware_Speech.md#in-call-settings-origin-gate) is also explicit: during protected calls, voice-initiated voice-setting and in-call-option changes are rejected and require a new UI request; operation approvals, read-only inspection, and deterministic safety controls follow their independent policy.
Bounded manual call controls now enforce that original origin and observed call
revision immediately before mutation, together with live desktop ownership/
privacy. A later local confirmation cannot relabel voice lineage, and denied or
stale changes are not queued for clearance. Legacy action-name grants are ignored
during protected calls without acquiring R05 exact authority; pending approvals
and undispatched reuse are invalidated on call revision changes. New protection
downgrades remain unavailable until the shared complete exact-review path can
support them safely.
Kora neither bypasses these requirements nor dictates credentials into a model.

## Conversational Voice Turns

The activation name starts an unsolicited user turn; it is not required for
every statement in an already established exchange. When Kora asks a
host-owned question and that question becomes the unique foreground voice
target, Kora may open one bounded **conversational reply turn**. During that
turn, an answer matching the question schema is accepted without the assistant
name or push-to-talk. For example:

> Kora: "This is a large response. Would you like me to show you the details?"
>
> User: "Yes."

`Yes`, `No`, `Not now`, an option name/number, a bounded field value, or a
permitted custom answer is interpreted only against the exact foreground
question/revision. It is not treated as a new general request. The activation
name remains optional inside the reply turn, so "Kora, yes" also works. Speech
that does not satisfy the question schema cannot silently become a command,
tool request, approval, session switch, or answer to another prompt; Kora
reports the mismatch or asks a new clarification turn.

The default prefix-free speech-start window is 15 seconds after Kora finishes
speaking the question. Show an accessible visible listening state/countdown and
use a short non-speech cue when output policy permits. Once speech begins, use
the normal trailing-silence and maximum-utterance bounds. On accepted answer,
explicit cancel, mute, timeout, target change, lock, call-policy loss, device
loss, or question expiry, close the turn, invalidate its audio/transcript
generation, clear buffered audio, and return to Wake Listening when eligible.
Timeout ends only prefix-free eligibility; the unanswered native question
remains available, and the user can later say "Kora, yes to showing the
details" or answer through UI.

Open a conversational reply turn only when all microphone consent, ownership,
unlocked-session, device, local-recognizer, playback-rejection, privacy, and
call gates pass, conversational replies are enabled, Kora actually presented
the exact question, and no higher-priority voice target exists. A visually
displayed but unspoken background question does not silently open transcription.
An explicit native **Answer by voice** action may present/revalidate that exact
question and then open the same bounded turn. During TTS, accept prefix-free
barge-in only after verified self-playback rejection can distinguish the user;
otherwise begin after playback completes.

Only one conversational reply turn exists application-wide. New foreground
questions, session changes, duplicate card hosts, and delayed callbacks cannot
retarget an open microphone generation. Background questions remain UI
answerable but cannot listen. If two possible targets exist, close prefix-free
capture and require an explicitly addressed activation or UI selection.
Generated prose, HTML, Markdown, tools, and skills cannot open a reply turn;
only the host question service can.

Question policy still controls what words are sufficient. Ordinary yes/no
questions such as opening details accept `Yes` or `No`. A high-risk approval
that requires the action and target to be spoken does not become approvable by
generic `Yes` merely because Kora opened a conversational turn. Prefix-free
speech changes input routing, not authorization, confirmation specificity,
speaker confidence, or grant policy.

Conversational turns do not create an unbounded open microphone. After one
answer the capture closes while Kora processes it; if Kora asks another
question, that newly identified prompt may open a fresh bounded turn. Silence
never keeps extending the deadline. The first-run voice explanation and live
presence distinguish ambient Wake Listening from prefix-free Awaiting Reply
and active Capturing Reply.

## Three Connected UI Surfaces

1. **Compact session interaction:** evolve the response window into the latest interaction for the selected session, with its name, actual work state, concise answer/progress, structured question or approval, Details/History, and typed composer.
2. **Sessions workspace:** reachable from tray, keyboard, compact UI, and verbal commands; Active/Done list and search beside the selected session's full conversation/history, pending cards, and work/queue. A bounded Evidence mode searches and correlates permitted session, diagnostic and audit records without making diagnostics part of the conversation. Shows unread/attention, meaningful activity/due dates, and actual work state, with rename/Done/resume/delete and All work/evidence views.
3. **Detail/artifact viewer:** expands immutable Markdown, static HTML, plain text, diffs, `.ps1` source and task evidence without losing the workspace conversation/current question. Explicitly opened items retain their own session/artifact identity when selection changes; viewing is not execution.

[Session Workspace and Coordinated Window Design](UI_Workspace_And_Windows.md) owns concrete layouts, window roles/navigation, structured cards, focus/drafts, concurrent-session UX, supporting settings/grant/setup surfaces, and original-requirement coverage.

The compact view is a projection of durable session events, not the only copy of the conversation.
A concise answer retains a link to its fuller content; expanding it never reruns work.
The host applies the versioned detail-routing policy after response
finalization. Detail-recommended compact responses expose an exact native Open
details action. For a voice-origin request with no subsequent UI interaction,
or effective voice-first/voice-only operation, the default Offer preference
asks once whether to open that response. The offer is a response-bound
non-consequential question, lower priority than approvals/clarifications; a
unique accepted voice/UI answer opens without a model round trip, while
silence/Not now retains the link. Generated prose cannot create the offer or
become a clickable authoritative control.
Streaming output is provisional until finalized, and summaries cannot claim observed success without receipts.
All surfaces share theme, accessibility, safe rendering, and provenance rules in [Information Display](Information_Display.md).

Closing/dismissing a window changes presentation only, not session lifecycle, work, microphone consent, or approval.
Passive updates do not steal focus. Pending questions, grants, errors, and unknown outcomes remain reachable even when a compact surface auto-hides.
Only ordinary non-interactive feedback uses the response timeout; an actively edited question or presented approval does not disappear under that timer.
Prompt expiry still applies independently and is shown honestly.
Switching sessions does not cancel background work. Content and delayed callbacks remain attached to their originating session.

## Structured Questions and Mixed-Channel Replies

Support single-choice options, multi-choice checkbox lists, yes/no decisions, bounded text, and typed forms where appropriate.
Include free-form clarification when predefined options cannot express the user's answer; do not force every question into a yes/no approval.
Each question declares:

- Session, task, question, and revision IDs; purpose and source.
- Question text, stable option/field IDs, labels, types, constraints, and minimum/maximum selections.
- Whether a custom answer is allowed; required fields; submit/cancel meaning.
- Creation/expiry, consequences, and any changed context/proposal digest.

Voice can list/explain options, select or deselect by unambiguous name/number, supply text, review the draft, and submit/cancel.
Mouse and keyboard edit the same draft; a user can speak one selection, click another, and submit either way.
For multi-select/forms, changing a selection is not submission. No consequential affirmative option is preselected.
Submission validates the complete typed answer and commits once against the question revision.
Corrections and stale answers report their conflict; cancel/dismiss/expiry never means approval.
Sensitive credentials use supported secure flows, not ordinary forms, history, or clipboard-answer shortcuts.

Several sessions may await answers. Explicitly addressed visible UI cards can be answered independently.
Only one spoken prompt is the foreground voice-reply target; it is distinct from the selected chat and executing tasks.
A generic spoken reply needs a unique current target. Naming another session requires presenting/revalidating that question before accepting an answer.
Changing voice focus withdraws the old generic voice-reply eligibility, not the old card's explicitly addressed UI eligibility.
Presentation and answer receipts record the channel and exact IDs; model-generated prose or rendered buttons cannot submit answers.

## Approval and Risk: Session Trust, Not Mouse Superiority

The baseline is the active unlocked Windows user session and the user's choice to enable verbal instructions, not protection against someone controlling that already-unlocked account.
Deliberate activated speech and deliberate native UI interaction are both supported expressions of user intent.
Who speaks an activated command is not itself a baseline authorization condition; residual indistinguishable external speech/playback is accepted without compulsory speaker authentication or blanket voice blocking.
Kora remains responsible for self-playback rejection, supported playback discrimination, ambiguous targets, prompt injection, stale proposals, expanded scope, and unintended dispatch.
It is not endpoint security and cannot secure a compromised Windows account.

Optional locally enrolled speaker verification adds confidence and can enforce an explicitly selected owner-voice preference.
It is not guaranteed authentication, and an ordinary click is not reauthentication either.
Optional separately consented [frequent-speaker learning](Security_Data_Flows.md#optional-local-frequent-speaker-learning) may adapt recognition/personalization from new activated commands only, never chat history or ambient audio.
It is not authenticated-owner identity, cannot grant authority, and cannot silently replace a protected verification enrollment.
Uncertain verification explains the limitation and offers an explicit alternate channel; it cannot manufacture consent.
Required Windows/UAC/provider authentication remains mandatory regardless of channel or voice match.
These are accepted design controls, not claims of completed enforcement; [Security](Security_Data_Flows.md#accepted-controls-and-verification-boundary) defines the implementation/acceptance boundary.

Risk is assigned by host policy to an execution proposal, not by a model describing a script as safe.
Evaluate effects, canonical targets/blast radius, reversibility, environment, data exposure/destination, privileges, and confidence in enforced execution constraints.
Use categorical thresholds rather than claiming a numerical probability from script text:

| Class | Typical proposal | Required treatment |
|---|---|---|
| Informational/safety | Help, factual non-sensitive status, stop speech, pause dispatch | Direct deterministic operation under its fixed policy |
| Low | Registered bounded non-sensitive local read without egress | Explicit request/confirmation as defined for the action |
| Medium | Private capture/egress, exact reversible user-file change, skill save/enable, privacy-affecting preference | Exact scope/destination/change review and deliberate voice or UI confirmation |
| High | Irreversible or broad deletion, production/shared-resource change, security/credential change, software installation, arbitrary code with unverified effects | Explicit consequences, targets, source/diff and recovery limitations; action-specific confirmation through either channel; mandatory OS/provider checks |
| Prohibited | Protected Kora mutation, bypassing lock/containment, exposing credential-store secrets, unsupported execution | Reject; approval cannot override policy |

Unknown or mixed effects receive the highest applicable permitted review class.
Administrator elevation is neither necessary nor sufficient for high risk.
Registered constrained actions may have lower risk only when their effects are actually enforced.
Static analysis/model review is advisory; arbitrary PowerShell may import code or construct effects dynamically.
General script execution remains separately gated, not newly enabled by adding a `.ps1` viewer.

A high-risk spoken confirmation names the action and target, not just "yes".
The equivalent UI explicitly identifies the same proposal and requires a fresh deliberate gesture.
Bind each decision to session/task/invocation, reviewed script/dependency digests, parameters, resolved resources, identity, destination, policy revision, and allowed use.
Approval scope is single-use, that operation for the identified Kora work session, or perpetual, as defined in [Grant Types and Inheritance](Security_Data_Flows.md#grant-types-and-inheritance).
Single-use grants are consumed; session grants end with that session. Perpetual grants have no expiry, retention, or eviction and remain until explicitly removed or edited; archive/deletion/restart do not remove them.
Material changes make a grant inapplicable to the changed operation, without deleting its record. No implicit inheritance across sessions or tasks.
Current once/session/always preferences are not blanket trust of future implementations; the future schema binds session grants to durable work-session identity and retains perpetual grants independently.
Future reusable execution grants bind the exact task/invocation and complete host-computed script/executable/dependency digest set; each dispatch revalidates applicability under current policy.
Protected lock uses the same future version-bound action approval gate for direct and suggested requests; priority routing does not waive it.
See [Skill and task execution design](../docs/skill-and-task-execution-design.md) for these execution gates; arbitrary or prohibited effects cannot become a reusable grant.
The host owns trusted question/approval controls and readback; Markdown/HTML/script comments and model/tool statements are not grants.
A historical grant reference is not a live dispatch token; inspect the independent grant store and fresh host policy.
See [Internal Model Tools](Internal_Model_Tools.md#grant-and-feedback-rules) for the shared grant and in-call feedback boundaries.

## Session Identity, State, and Contents

A Kora session is a durable user work stream containing conversation, tasks, and evidence, not an execution slot or provider memory.
Its immutable ID survives renaming, selection, archive, and explicit resume.
Lifecycle is **Active** or **Done**; work state is separate: idle, queued, running, waiting for user/approval, blocked, failed, cancelled, interrupted, or outcome unknown.
Done means archived, not "every action succeeded". Deleted is irreversible removal, not another browsable status.
A terminal task result does not automatically mark the whole session Done.

Host-owned records include:

- Session ID/title, lifecycle, selected state, creation/last meaningful activity, archive time/reason, retention-policy revision, and deletion due time.
- Ordered user/assistant messages with voice transcripts or typed content, channel, timestamps, revisions/corrections, provenance, and incomplete/final status.
- Questions, options, drafts/accepted answers, decisions, denied/cancelled/expired proposals, and routing decisions.
- Tasks, planned versus observed progress, dependencies, action requests/results/receipts, errors, cancellation certainty, and late reconciliation.
- Reviewed script/diff/artifact snapshots, digests, citations, relevant tool exchanges and context snapshots allowed by source/security policy.
- Approval/grant scope and creation/use/denial/edit/removal evidence, plus expired unanswered proposals, linked to the separate security service without persisting reusable dispatch tokens.

Full retained history is the evidence source. Derived summaries/indexes aid discovery, never replace it or silently evict old entries.
Never store raw ambient/command audio, credentials, tokens, enrollment material, or hidden model reasoning in session history.
Secret screening is best-effort: redact detected secrets before durable storage and identify omitted/redacted/restricted content.
Source/account restrictions can require removal or prevent retention; record an explicit content-unavailable reason rather than promising an unrestricted copy.
Diagnostics remain content-minimising and separate from intentional session history.

## New Requests and Routing

Explicit targeting wins: input in a session composer continues that session; "new session" starts one; naming a session targets it.
An untargeted general voice/global-composer request starts a new session unless clearly related to an Active session.
Model-assisted matching uses bounded, permitted session descriptors/summaries and the current request, not every transcript or tool result.
Return a typed route proposal with candidate IDs, evidence references, and expected registry revision.
The host validates lifecycle, identity, permissions, and revision before admission.

- One clear active match: continue it and acknowledge the session name, with an immediate correction route.
- Several plausible matches: ask a targeted session-choice question, including New session.
- No clear match or unavailable routing inference: create a new session; offer deterministic session selection rather than guess a relationship.
- Archived match: offer discovery/history; only an explicit resume decision reactivates it.

The selected UI session does not silently redirect an untargeted voice request or authorize a background action.
The priority standalone lock exception uses deterministic
[control-session binding](Built_In_Skills.md#standalone-lock-work-session-binding):
an unaddressed request creates a new durable Active control session without
relatedness inference. Commit/present its identity before approval/dispatch;
Session requires that binding. This neither guesses another Active target nor
automatically marks the control session Done after its lock task.
Exact session list/switch/new/Done/delete controls and basic factual status work without a model or network.
A status/history question reads the addressed records and does not resume the subject sessions.
Relatedness never transfers grants, provider state, sources, or permissions between sessions.

## Concurrent Work and Focus

Independent Active sessions can execute concurrently, including writes to different resources.
Initial proposed default: two executing session tasks, with a configurable limit admitted only within verified provider/hardware capabilities.
One execution slot per session preserves its ordering unless a later explicit design enables intra-session parallel tasks.
Resource holds, dependency readiness, provider budgets, and fair scheduling govern admission; selected session gets no implicit execution priority.
Management and exact local controls remain responsive while all slots are occupied.

Canonical resource declarations and host-owned shared/exclusive leases prevent conflicting writes and read/write races.
The relevant resource may be a repository/worktree, file tree, remote environment, account-scoped object, or OS lifecycle.
Unknown effects or incomplete resource sets cannot claim safe parallelism; serialize in an enforced exclusive execution domain or reject unsupported execution.
Coordinate with external version changes by revalidating the reviewed base immediately before effects; Kora leases cannot lock out unrelated external applications.
A dependency or unresolved remote outcome blocks conflicting/dependent dispatch, not unrelated sessions.

Each session has isolated context, runtime conversation, cancellations, queues, grants, and event sequence.
Provider session IDs are adapter-owned subordinate references, never the canonical history.
TTS is shared and serialized, with session-labelled readback and one foreground voice question.
Background progress remains in its session; eligible notifications follow proactive/call/privacy policy and never replace an approval.
App exit/restart/power preparation coordinates all sessions, not just the selected one.

## Inactivity, Done, Resume, and Delete

Defaults: **archive after 24 hours of inactivity; delete after 30 days of inactivity**.
Both are device-local configurable settings, not hard-coded safety constants.
Use the same last-meaningful-activity clock for both; deletion is not 30 days after creation or archive.
Validate finite positive durations with deletion later than automatic archive; invalid combinations are rejected, never silently clamped.

Accepted substantive user input, an accepted answer/decision, explicit resume, and actual task progress/completion update meaningful activity.
Selection, browsing, search, history questions, polling/heartbeats, and synthetic reminder events do not.
Record durable UTC timestamps and due times; clock rollback cannot trigger premature expiry.
Automatic lifecycle changes do not themselves reset meaningful activity.

Live queued/executing work or an action awaiting resolution is never silently abandoned by archiving/deletion.
Expired questions and queued-task execution lifetimes follow task policy; expiry releases execution eligibility, not retained history.
Unknown side effects remain visibly flagged and block automatic removal pending reconciliation or an explicit informed disposition.
Evaluate lifecycle on a timer and at startup/access, so overdue records are not exposed merely because Kora was closed.
Record archive reason and outcomes; an automatic Done transition is not success.

"This session is done" archives the identified session after resolving live work: wait, explicitly cancel, or decline.
Archive preserves all permitted history and ends active session work and session-grant eligibility; it does not remove perpetual grants, artifacts, or saved skills.
"Resume that session" explicitly reactivates it and resets activity, but does not rerun tasks, restore approvals, or transmit old context.
Previously Done sessions remain readable without resume.

Explicit deletion previews the exact session/data and work impact and requires action-specific voice or UI confirmation.
Automatic purge uses the disclosed configured retention policy without asking again for every eligible expired session; live/uncertain-work checks still apply.
Cancel/reconcile live work first; prevent further appends and remove messages, artifacts, source snapshots, summaries, search indexes, caches, and recoverable database/journal copies according to the store's deletion contract.
Independent perpetual grants survive session deletion, with minimal scope/provenance retained outside chat history; a missing source can make them inapplicable without removing them. Session grants end with the deleted session.
Content-minimising independent security/diagnostic records follow their own disclosed retention and cannot reconstruct deleted conversation.
User exports and provider-side copies are outside local deletion; disclose that limitation.
Do not promise forensic secure erasure or remote cancellation.

Changing either retention setting shows affected due dates; shortening it previews records that would immediately archive/delete and requires a separate explicit apply-now decision.
Without that decision, retain existing sessions' due dates and apply the new policy to new sessions or subsequent meaningful activity; do not defer an unconfirmed immediate purge to the next timer tick.
No setting change silently purges sessions. Storage pressure reports failure/options, never undocumented eviction.

## Persistence and Restart

Persist accepted input/decisions and finalized artifacts as ordered host events; write intent before dispatch and link observed receipts afterward.
Use transactional revision/sequence updates, standard SQLite and managed artifacts/indexes under verified private LocalApplicationData permissions, with schema/backup/deletion recovery.
Use the pinned Windows provider/native closure, not ambient DLLs or optional database downloads.
The [owner-approved storage baseline](Architecture.md#windows-durable-storage-direction) supersedes mandatory page encryption and database-key/rekey workflows; copied/exported files are readable. Credentials remain OS protected.
Recovery reconciles artifact/backup publication as well as SQLite transactions.
Deletion owns managed recoverable copies and rejects late appends; deleting only current rows is insufficient.
Storage failure is visible and blocks consequential dispatch whose required intent/decision cannot be recorded.

Restart restores Active/Done history and UI selection, not action execution, queue dispatch, or consumed/expired approval tokens.
Independent perpetual records and applicable Active work-session grants survive restart; exact implementation/invocation identity and fresh host revalidation determine applicability, while retained chat alone never restores consumed or ended authority.
Recovered non-terminal work becomes Interrupted or Outcome unknown with an explicit resume/replan/reconciliation route.
Every future dispatch revalidates current policy, source access, context freshness, identities, dependencies, and resources.
Preserved readable content is not permanent authorization to use or transmit it.

## Model-Facing Host Tools

The [Internal Model Tool Catalogue](Internal_Model_Tools.md) owns the complete inventory, including current bootstrap actions versus proposed/deferred tools, typed inputs/results, caller lanes, bounds, and host-only exclusions.
Session/history/artifact, structured interaction/presentation, grant, work, and configuration tools use those canonical IDs; this document owns their session behavior, not a second partial tool list.
History and evidence pages contain at most 50 events within the 64 KiB model-bound limit, with continuation/range references and explicit omissions.

Answer submission and approval consumption are trusted host input events, not model-callable self-answer/self-grant tools.
Models propose mutations; the host checks direct user intent, scope, revision, and required confirmation.
Session-history and cross-source evidence retrieval are context-broker operations subject to source/account access, secret filtering, local-only mode, and exact remote-egress review.
Remote-enabled mode is not consent to upload all sessions, even to classify relatedness.
Retrieved scripts/chats/tool results are historical untrusted data, never current instructions.
Questions spanning sessions, diagnostics and audit records return citations to source/event/artifact and separate confirmed facts from model inference.
The Evidence workspace exposes model-independent list/read/search for both log
and audit records. A user-requested Ask Evidence interaction creates a visible
read-only reasoning request over the selected source filters/records; local
reasoning is preferred, remote use previews the exact payload, and every answer
links claims to stable evidence IDs. Follow-ups cannot silently widen the
selection, refresh retention, execute an evidenced action or treat record text
as current intent.
Each durable session exposes a direct Evidence view filtered by its stable
host-owned session ID. It includes every retained diagnostic event, activity
span/link and audit record across all traces associated with that session, with
pivots to trace, task, invocation, approval and audit correlation. Session
selection is navigation only; the host stamps session identity when accepting
or dispatching work. Deleting session content leaves only independently
retained content-minimising audit/session-reference metadata under its own
retention and never reconstructs the conversation.

## Implementation Discussion and Gates

Implement coherent checkpoints: session/event persistence and migration/deletion; shared structured interaction/presenter; session UI and deterministic controls; isolated concurrent scheduler; then bounded model routing/history tools.
These extend Slice A3/A4 rather than claim that the current bootstrap already implements them.
General scripts, browser isolation, biometrics, and provider concurrency retain their dedicated gates.
Library selection, enforced resource profiles, retention clock/recovery behavior, and actual SDK isolation require evidence in the [Decision Register](Decision_Register.md).
Acceptance must exercise pure voice, pure UI, mixed-channel, concurrent-session, restart, expiry/deletion, and hostile-history scenarios in [Acceptance Criteria](Acceptance_Criteria.md).
