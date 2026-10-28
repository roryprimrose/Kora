# Privacy, safety, and logs

## Reviewed session memory controls

Kora has a provider-independent reviewed memory domain and a bounded session-only private durable store.
[Native and exact typed/activated controls](commands.md#create-and-manage-reviewed-session-memories) create a deliberate original-user volatile proposal and list/inspect/review/admit/edit/disable/forget exact session memories.
There is no cross-session recall, provider-memory write or automatic attachment to model requests. Ordinary conversation is not silently saved as memory.

The foundation separates untrusted proposals, explicit user review and host
admission; edits remove prior review/use authority, disabled memories are
ineligible, and forgetting leaves a content-free non-reusable tombstone. Exact
session/profile/project/source identity and current ownership/privacy/source
lineage are checked before use, with explicit failures for unknown state,
forbidden content classes, size limits and revision conflicts. Credentials,
secrets, health information, inferred traits, transient tasks and model claims
are prohibited classes, not retention preferences. A model's class label is
not evidence that its text is safe or a user approval. An original-user classification is not secrecy detection either.

Any future local-use surface must expose exact memory IDs/revisions and source
provenance. Local retention never grants hosted disclosure. The internal
session-only store shares authoritative audit and session lifecycle/retention;
proposals and edited replacement bodies stay volatile until exact review and admission.
Drafts and unadmitted bodies are discarded on closure, session/privacy/ownership changes or restart.
Uninventoried managed copies hold deletion acceptance. Broader scopes and provider use remain dependent work. Forgetting is not forensic erasure or
removal of previously returned/exported/provider copies.
Logs and Activities carry host IDs and typed outcomes, not memory content.
Native GUID/Scope/Review/Retention filters use only metadata already returned by an explicit List for the selected exact active session. Scope is an exact metadata kind, never permission, session selection or provider authority; Any and every defined kind compose with AND without expanding the inventory. Invalid selectors or malformed row metadata fail closed with explicit recovery, not partial results. Query text and filter state are private and volatile: no storage/content read, persistence, logging, Activity, session-clock renewal or model context follows a filter edit. Edits and Clear retire selection, inspection, replacement draft and mutation eligibility; selection and inspection must be explicit again. Forgotten rows expose only existing content-free identity metadata. Classification is not listed or searched, and no secret-detection claim is made. Explicit List refresh and existing source/session/private closure remain authoritative; no cross-session/global inventory or content search is added.
See the [authoritative proposal boundary](../Design/Model_Providers_Memory_And_Knowledge.md#delivered-original-user-session-memory-proposals---2026-10-10).

## Trusted local event privacy

The [owned draft Validate](instruction-editor.md#validate-the-open-draft-without-saving) control is model-free structural observation of the already open same-kind draft, not a source reader, writer, approval or use action. It retains original/draft/dirty/inventory and held/enabled state, with no owned-store I/O, reload, preference mutation or activation.
Normal observational control receipts/traces remain. Source-generated rejection logs contain fixed reason codes under admitted host context, not definition body, metadata values, paths or excerpts. Bounded text-only findings cannot execute content or authorize Save; input/private/source changes retire findings and Save independently confirms current prepared bytes.

The native run-only routine quiet choice starts Off and applies only to Work/Maintenance first deliveries and notice rows. It lasts until explicitly cleared/reset or restart; the flag is never persisted.
The same bounded suppression history now admits canonical known schema 2 routine-only `RoutineSuppressed` receipts while preserving schema 1 reads and existing expiry/revision/watermark/budget/retention rules. No new preference file or database migration is introduced.

Quiet never suppresses Failure/Attention, required questions/approvals, mandatory security output or independent work/status/citations/recovery. Clear burns current muted sources under existing source locks, without budget consumption or backlog replay.
Exact live native session/control/choice admission, trusted requested/terminal audit and generation-checked snapshots prevent model/system/late-render changes. Failures hold with recovery; quiet grants no speech, capture, consent, effect or egress authority.

The [bounded visual broker](commands.md#trusted-local-events) reads only
authoritative fixed local-version work/question metadata and the already-
verified maintenance cache. No model text can create events. Stable IDs,
revisions, source/generation, UTC expiry and fixed summaries are not grants,
question answers or executable work. Admission is revalidated against current
source, original input/native lifetime and call/privacy/lock/owner epochs.

Only `local-events.json` and `local-events-unconfirmed.txt` are added under
the supplied application Preferences path. At most 64 content-free suppression
receipts, four category budgets and a UTC high watermark are retained; no user
text, paths, remote notes, response/model content or effect payload is stored.
These suppression records are not conversation/history. Requested/terminal
trusted audit and exact atomic readback/confirmation precede delivery. Unknown,
corrupt, obsolete, unconfirmed or future-clock state holds without default,
rollback, automatic repair or replay. Session retirement removes owned records;
unrelated preferences and global fatigue remain independent.

Logs record counts and fixed exception types only. Activities carry admitted
host identities/audit correlation and causal links, never content or Baggage.
Passive delivery/status never renews meaningful activity, steals focus,
replaces a question, changes voice target, consumes grants, resumes/dispatches
work, speaks, opens capture or causes model/network/browser/install activity.
No new consent is created. Restart requires fresh source authority and cannot
replay presented/dismissed/expired effects.

## Fixed deterministic queue privacy

Normal user routes no longer admit or dispatch diagnostic-only queued reads or expose capacity/prerequisite/reorder/expired-replacement controls.
Existing records remain inspectable and cancellable under their exact-source policy; retirement does not drop records, rewrite deadlines, replay work or treat Unknown as success ([admission boundary](../src/Kora.Application/Hosting/SessionWorkspaceService.Commands.cs), [queue controls](../src/Kora/SessionsViewModel.Queue.cs)).

Internal scheduler/store/domain validation, authoritative audit, ownership/private admission and no-replay policy remain.
Direct read-only status queries and actual task lifecycle/cancellation are separate.
Observation remains passive and missing/corrupt authority remains unavailable, not an empty-success inventory ([scheduler](../src/Kora.Application/Hosting/SessionQueueService.cs), [work observation](../src/Kora.Application/Hosting/SessionWorkspaceService.cs)).

## Bounded passive interaction history

The [Sessions history viewer](windows-and-tray.md#bounded-passive-interaction-history)
reads only host-committed questions/final answers, decision metadata and task-state
receipts from the private interaction partition. Stable exact event/session IDs,
sequence, source revisions, generation and digests are citations, never reply,
approval, model-context or replay authority. Passive reads append nothing,
extend no meaningful activity and never resume Done/Removed sessions.
Snapshot pages exclude later appends; lifecycle/privacy/ownership changes,
unknown IDs and corrupt/unsupported storage fail closed.

Bootstrap user/model messages and response bodies were never recorded here and
are explicitly unavailable. History is not reconstructed from diagnostics or
audit mirrors. Drafts are not displayed as final answers; required source
snapshots may retain existing question state under the same private storage
policy. Content never enters logs, activity tags/baggage, model context or a
network request. Shared-profile skill text and immutable file/clipboard preview
paths/text/provenance remain volatile inspection data, not durable conversation
attachments. History cannot reconstruct captions: only fresh independently
admitted actual playback can create them.

Logical disposition redacts history source/content in the same transaction that
removes addressed live authority rows; it retains immutable citation metadata
and the non-reusable tombstone. Exact known Removed IDs can read redacted
citations only. Confirmed exact-session deletion removes addressed name,
question/draft/answer, observation, history and session-owned reviewed-memory
content from owned SQLite cells/free pages and verifies the committed rollback
journal is empty before reporting completion. This also applies to memory-only
or name-only sessions with no attachment, provider override, conversation
message or saved conversation draft. Unknown managed copies hold deletion.
If cleanup, durable flush or readback fails after the logical commit, removal
is explicitly unconfirmed: inspect durable authority and storage recovery;
do not infer rollback or retry automatically.
Independent device-profile memories, Perpetual grants and content-minimising
task/audit provenance remain retained under their separate policies.
Other databases and their journals/free pages, inert migration copies and exports/
provider copies are not erased. No new history retention timer, automatic purge,
general backup deletion or forensic-erasure claim is added. Full composer/search/model
history reasoning, Ask Evidence and broad export remain unavailable; the
separate fixed local-version queue/native work surface is not a general
effect/provider scheduler.

## Committed authority audit inspection

In **Evidence (read-only)**, select a typed audit record and choose **Read
request outcomes** to read that exact original host request and audit
correlation. The fresh read stays in its source and original session, without
the search filters; **Next page** continues the bounded snapshot. The summary
separates committed interaction audit from diagnostic audit copies and shows
recorded outcomes. Requested-only means pending **or** missing terminal
evidence, not success or proof that work is still running. Select an event to
read its exact outcome/reason and retention state. Missing or unavailable
evidence is not replaced with another request.

**Inspect related diagnostics** separately reads the selected activity in the
original session. Select its span, then **Open selected segment** to follow a
recorded parent or explicit causal link. Missing/foreign segments stay
unavailable; expired-but-present evidence is labelled. Diagnostic correlation
never becomes audit, approval or execution authority. Legacy uncorrelated
diagnostics cannot select a host request. Privacy closure and filter changes
retire these views. No retry, execution, grant renewal, copy/export or retention
change occurs.

Native Evidence's optional inclusive **Retained due UTC from / until** filters
match only admitted retained `EvidenceRecord.DueUtc`, independently of existing
commit/observation time bounds and AND with every other predicate
([query](../src/Kora.Core/Diagnostics/EvidenceQuery.cs)).
Only empty means Any; [canonical UTC parsing](../src/Kora.Core/Storage/OptionalCanonicalUtc.cs)
requires `yyyy-MM-ddTHH:mm:ss.fffffff+00:00`, without trimming, repair or offset
normalization. Malformed, whitespace, noncanonical, overflowing or reversed
bounds fail before reading. Missing DueUtc is UNKNOWN, not a match for any
constrained range; DailyLog contributes no due metadata.

Due is a retained metadata deadline, not proof of physical deletion, current
expiry, policy, authority or retention permission. Full source/record/provenance
validation and existing bounded scan, page-byte, timeout, private/source and
cancellation fences remain. Both bounds bind the complete versioned semantic
digest/authenticated cursor; [native edits, Clear and Escape](../src/Kora/EvidenceViewModel.Filters.cs)
read nothing and retire rows, selection/detail, continuation and stale callbacks.
No body/path read, schema, retention write/prune/expiry/lifecycle, audit-write,
logging/capture, device/provider/model/egress or qualification change is added
([owner contract and deterministic tests](../Design/Information_Display.md#delivered-bounded-native-evidence-inspection)).

Native Evidence's **Observed trace parent** selector offers **Any / Root / Child**.
Root means a valid retained trace with null parent span ID; Child means a valid
retained nonnull parent ID. A missing trace is unknown, never an observed root
or child. The choice composes with every other filter, including exact Parent
Span ID; Root with an exact parent ID has no matches. Full trace validation
precedes exclusion, and Next is bound to the complete versioned query
([predicate](../src/Kora.Core/Diagnostics/EvidenceQuery.cs), [query admission](../src/Kora.Application/Diagnostics/DurableEvidenceQuery.cs)).
Editing, Clear advanced and Escape read nothing and retire results, selection,
detail and continuation, including late callbacks. These are diagnostic
observations, not complete traces, causality, current session, identity,
authority, approval or ownership. Existing privacy/source/retention limits
remain; no new read authority, storage schema, tags/baggage or model/device/
provider path is added ([native contract](../Design/Information_Display.md#delivered-bounded-native-evidence-inspection)).

Native Evidence also offers exact diagnostic Event name (max 128 characters)
and Exception type (max 256), plus inclusive positive audit-sequence From/Until
bounds. Empty means Any. Names are case-sensitive bounded plain text without
surrounding whitespace or controls; sequences are canonical positive 64-bit
decimal integers with ordered bounds. Missing metadata never matches a set
predicate. All filters compose with AND; edits clear results/selection and cancel
old reads without starting a new one. Clear advanced resets these fields.
Event/exception names do not expose exception messages, stack traces, sensitive
properties, user content or omitted bodies. Sequence is committed ordering
metadata only, never authority, completeness, freshness, replay position or
permission. Existing source distinctions, validation and page budgets remain.

Native Evidence also offers exact trace source, activity operation name,
ActivityKind and trace flags (None/Recorded), with visible Any choices. Trace
source version offers Any/Absent/Present/Exact: Absent requires an existing trace
with null version; an unknown/null trace never matches a set trace predicate.
Exact version requires text; all other version choices require empty text.
Source/version/name are case-sensitive, at most 128 characters, valid plain
Unicode without surrounding whitespace or controls. These filters compose with
all existing filters using AND, bind Next page, and reset with Clear advanced or
close. Edits read nothing and retire old results/selection. Complete retained
trace validation precedes filtering. Trace metadata is untrusted diagnostic
correlation, not session selection, identity, intent, permission, approval or
authority. No baggage, tags, user content, paths, credentials or omitted content
is searched or restored. AuthorityAudit does not fabricate activity metadata.
Installed accessibility qualification remains separate from deterministic tests.

In **Evidence (read-only)**, select **AuthorityAudit** to inspect committed typed
interaction-store security audit rows. This is a separate source from the
diagnostic **Audit** projection and daily mirrors. **All** remains SQLite
evidence-only; **CombinedLog** remains ordinary diagnostics-only.
Only this store's committed events are included; settings/effect events from
other storage or logging paths are not imported or represented as atomic
interaction commits.

The [manual-call command subset](commands.md#exact-current-run-manual-call-control)
does use this committed authority path: dedicated original-user intent/session
admission, required requested and truthful process-memory outcome audits,
correlated by the same exact host request/session/task. The manual flag itself
is never persisted or restored. Audit/receipt failure keeps explicit
conservative run protection, not fabricated rollback or automatic no-call
evidence. Cached call inspection creates no audit, effect or question answer.

Pages show source-qualified citations, recorded schema/event/outcome,
session/task/approval/trace IDs and exact committed revision/digest references.
They do not reconstruct historical question text or a file/activity graph.
Search starts a bounded snapshot; Next continues its original sequence ceiling,
excluding later appends. Invalid/expired cursors, schema drift, changed stores,
corruption and access failures require a fresh admitted search or explicit
storage recovery, never silently restart as empty success.

The complete output remains at most 50 records / 64 KiB; each request scans at
most 4,096 rows under bounded storage admission/query time. Ownership/privacy
loss clears and cancels the viewer. Reads do not change tasks, approvals,
session activity, lifecycle, audit records or retention. There is no export,
model tool or effect authority. Private files are unencrypted/user-modifiable;
local consistency checks are not forensic tamper resistance or external
checkpoints. Installed/native accessibility acceptance remains outstanding.

## Local speech

Explicitly activated command recognition uses the installed Windows recognizer.
Ambient audio never enters its command transcription pipeline; production wake
is unavailable, not the old ambient grammar. The built-in text-to-speech provider uses installed
Windows SAPI voices. Kora does not need a cloud account or network connection
for these features.

## Local file inspection

An explicit [file preview](commands.md#explicit-local-file-preview) is a
volatile local inspection, not a session attachment, model-context permission
or durable knowledge source. Native selection first opens only metadata.
Confirmation is required in the exact native review before one bounded
strict-UTF-8 read. File and ancestor handles prevent write/replacement races;
two bounded passes must match exact bytes, and identity/path/length are
revalidated before and after reading. Failed inputs
are rejected whole; there are no undisclosed partial-folder exclusions.

The preview can contain secrets. No redaction/safety guarantee is made.
Content, search queries, excerpts, paths and filenames never enter its logs, audit identifiers, activity
tags, transcripts, speech, SQLite, preference/artifact storage, model prompts
or clipboard. Typed requested/terminal audits contain only safe action,
target, outcome/reason and host correlation. The native UI displays the
private canonical path and inert untrusted text intentionally to the user.
Digest/provenance refer to the exact admitted original bytes, not current
filesystem content. Markdown instructions and links have no authority.

Close/clear/cancel, privacy/lock, ownership or originating policy generation
loss discards the selected revision; no refresh is queued. Reviews expire
after two minutes. Raw byte buffers are cleared and owned handles released.
Immutable CLR strings are released for garbage collection, not a guarantee
of memory erasure or exclusion from OS crash dumps. Pending native work and
unverified release cannot claim clean handoff/exit.

Only fixed-drive plain-text/Markdown files up to 256 KiB are supported.
Native lexical search scans only this selected immutable revision in bounded
volatile memory. There is no derived persistent index or enterprise cache.
Requested/terminal typed audit outcomes are required before excerpts are
published; failed audit publication means unavailable, not success.
Origin/session/task and source/revision/digest identities plus privacy and
ownership generations are revalidated at the shared revocation boundary.
Cancellation/revocation suppress late results; outstanding worker work blocks
clean handoff. Native close releases query/result strings for collection, not
claimed memory erasure. Exact excerpts may contain secrets and untrusted
instructions; no semantic answer, rendering, execution or authority is derived.

Folders, knowledge registration/persistence, UNC/removable drives, persistent/
vector indexing, reasoning, hosted egress and content execution remain
unavailable.

## Clipboard snapshots

An exact [clipboard preview request](commands.md#explicit-local-clipboard-preview)
authorizes one ephemeral local plain-text read, not monitoring or model use.
Kora checks current unlocked privacy, instance ownership, original input
origin and call-policy generation before reading and again before presentation.
Unknown/unavailable authority fails closed. Native reads run on a request-owned
STA, close the clipboard and release the borrowed native memory lock; Kora
does not own or free the clipboard data handle.
Unverified native release is latched as unavailable and blocks clean ownership
handoff/exit; cancellation cannot conceal that failure or claim quiescence.

Preview displays the exact immutable text, including whitespace, under separate
host-owned provenance/ID chrome. Quoted commands, HTML, links and credentials
are inert data; they cannot approve, invoke tools, edit settings or select a
destination. No content, digest, title, excerpt or raw exception message enters
diagnostic/audit/status payloads. Diagnostics record only the typed
outcome/failure type under host activity correlation. This slice has no content
store, history index or persisted context.

Local explanation requires both a clipboard tool permission grant and your
separate confirmation of one model use of the exact reviewed snapshot.
You can deny, approve Once, approve for the clipboard explanation session or
approve Always. You are responsible for sensitive material you place in the
clipboard and ask Kora to examine. Kora performs no clipboard sensitivity
classification or credential-pattern analysis and makes no redaction or
secret-free guarantee.

Only a qualified local runtime can receive the exact bounded envelope. Its
answer stays volatile in the same native preview, never conversation bodies,
history, automatic speech or diagnostic/audit sinks. There is no hosted/bootstrap
fallback or model tool access. Preview/reuse alone never run inference.
Missing runtime readiness or permission remains explicitly unavailable; native,
provider and installed qualification are not implied by deterministic tests.

Clear/revoke, close, cancel, privacy/ownership loss, changed call policy,
handoff and disposal retire the selection/render generation and suppress late
reads/callbacks. Unlock does not restore it. Clearing releases Kora's references;
it does not change the shared clipboard or promise managed-memory zeroization.
Automated tests use private fake/native seams, never the user's clipboard.
Real clipboard/native accessibility/latency acceptance remains outstanding.

## Local-model actions

After model setup is approved and inference verified, Kora sends the current
unmatched request, the built-in action names/descriptions, and a limited
snapshot of dependency readiness, task state/progress, listening state, and
pending power-proposal status to the pinned Ollama model on `127.0.0.1`. It
does not include earlier conversations, logs, files, clipboard contents,
device identifiers, or account data. Explicit
[artifact invocation](commands.md#run-skills-and-artifacts) separately
includes the selected bundled/compatible profile instruction content and its
source/version/digest identity in the local-model request, not arbitrary files
or the instruction's scripts. That content remains untrusted guidance and
cannot bypass host action/grant policy. For a clarification, Kora sends the
original request, its question, and the option you selected back to that same
local model; no unrelated conversation history is sent. The model can return
a text answer, a bounded question with choices, a grant-change proposal,
or one registered action identifier. Kora validates the proposal and
performs the action through the same built-in handler used by typed and voice
commands. Arbitrary model-generated commands or API calls are never executed.
Choosing a clarification option does not approve an action or change a grant.

Suggestions to hide or exit Kora, restart it, cancel a task, lock Windows,
or prepare a power proposal require explicit approval unless that exact action
has a current session or persistent grant. All approval scopes (once, this
session, and always) are offered for every action that requests approval,
including Windows lock. A session grant ends on exit, restart, or when Kora
locks Windows; an always grant persists locally until revoked in **Settings >
Approvals**. Grants are keyed to named built-in actions, not arbitrary
commands, file paths, implementation versions, or model-generated code.
Rejecting, dismissing, or making a new request discards the pending
suggestion. This confirmation gate applies to *model-suggested* disruptive
actions only: direct exact built-in voice and typed commands, including lock,
currently dispatch to C# without model-action approval. They remain
deterministic and take precedence over the model.

Protected manual calls or enabled Active/Suspected/Unknown observations
temporarily ignore Session/Always reuse without revoking or consuming those
records. A fresh Once approval retains only existing action-name authority.
Call revision changes invalidate pending approvals and undispatched reuse;
lock/restart recheck immediately after asynchronous audio shutdown. This is not
migration into R05 content/invocation-bound grants or general effect admission.

**Planned, not implemented:** future skill tasks that launch applications or
scripts would require content-bound execution grants, **not** the current
built-in-action grants. The design requires changed or unverifiable
SHA-256 digests to invalidate dependent grants, including always grants, and
execution-time verification of the exact bytes to launch. It also covers
standalone application launches. Kora does not yet launch arbitrary
applications or stored skill scripts; no current grant provides these
protections. See the
[skill and task execution design](skill-and-task-execution-design.md) for the
planned approval, skill-management, and task-runner requirements. That
proposal embeds `.ps1` definitions for suitable side-effecting built-in tasks
and adds a read-only syntax-highlighted script review window. Today the
built-in handlers are C#, not `.ps1` skills, and there is no script review UI.
PowerShell 7 (`pwsh.exe`) is now a required readiness task; Kora probes a
known installation location with a no-profile version command. Installing
the `Microsoft.PowerShell` winget package requires separate on-screen
consent, and successful installation is verified. This setup does not
authorize or execute any skill or built-in `.ps1`, nor gate local inference.

The approval request always appears on screen, including in VoiceOnly mode.
When speech is available, Kora closes command capture while speaking the
question. A spoken answer requires a new explicit push-to-talk activation. By default, spoken approvals
must start with the assistant name. Settings can allow an unprefixed spoken
reply; this changes the recognition gate, not the scope of an existing grant.

Kokoro is an optional local neural text-to-speech provider. Kora makes no
Kokoro network request unless the user explicitly selects **Download**. That
action downloads a pinned model and voice archive from a fixed GitHub release,
validates both with SHA-256, and stores them under
`%LOCALAPPDATA%\Kora\Speech\Kokoro`. Spoken response text is not sent to GitHub
or another service; synthesis runs locally after installation.

## Microphone consent

Device discovery/selection and **Enable listening** do not record audio.
First launch requires explicit ongoing consent; it is saved separately from
endpoint preferences and Windows permission. Safe ordinary startup with saved
consent arms push-to-talk; capture opens only while deliberately activated.
**Disable listening** closes input for this run. **Withdraw voice consent**
keeps it closed across restart. Lock/disconnect/unknown session, suspend,
permission/device loss and capture failure invalidate generations, clear audio
and stop output. Normal authoritative unlock restores previously enabled PTT
readiness only after confirmed closure and fresh consent, ownership, session,
permission, endpoint and call-policy checks; the microphone stays closed until
a new PTT press. Manual disablement, withdrawal and intervening failures prevent
automatic restoration. Disconnect/resume/hot-plug and other failures require
explicit recovery. Native/tray controls require no model, network or speech.

External session/power/endpoint observation is implemented, with a one-second
permission polling fallback. Negative session notifications invalidate capture
and output before slower requery; Unknown or failed observation grants no input
authority. Release during a pending PTT open, shutdown and disposal retire the
activation, including already queued transcripts and cancellation-ignoring late
opens. Recovery never replays buffered audio, transcripts, approvals or tasks.
The equivalent prior-enabled-mode policy applies to future qualified wake-only
detection, but production always-on listening remains unavailable in this build.
Deterministic regression coverage does not certify native notification latency,
the 500 ms reference lock-release target, acoustic playback rejection or the
remaining hardware/device/permission acceptance trials.

## Manual call privacy

Native **Settings > Calls** manual Active/clear is run-scoped and not persisted.
It layers over automatic evidence without claiming a detector or fabricating
Clear. Default protected-call output is visual-only, including previews and
approval readbacks. Pending synthesis/playback is invalidated before UI work;
clearance replays no old speech and grants no input or approval.

Manual controls require original initiating channel, current call revision and
fresh ownership/privacy admission. Protected calls reject every voice-originated
voice/in-call option mutation, including clear/reset. A later UI confirmation
cannot relabel voice intent; new UI initiation is required. Stop/cancel,
disable listening and readable status remain usable. New protection downgrades
and exceptions stay unavailable until complete exact trusted review is composed.
No network/account detector, speaker biometric check, live call trial or
native/acoustic privacy acceptance is claimed.

## Visual safety fallback

The [minimal Sessions workspace](windows-and-tray.md#minimal-durable-sessions)
reads existing IDs/generations, typed question history and current durable
task records. It is not general retained conversation, provider context or a
work scheduler. Selection cannot authorize a reply or retarget an approval.
Explicit Done/resume uses live ownership/privacy/call admission, exact fresh
control intent and atomic authoritative lifecycle audit; a diagnostic
projection is not that authority. Unknown/live work and unresolved questions
remain blockers. Each transition advances generation, invalidates old scoped
authority, preserves independent Perpetual records and replays nothing.
No automatic archive, deletion, retention or export is added.
Native **Create empty Active session** and **Rename selected ID** store bounded
intentional private names in the existing interaction partition. Names never
enter activity tags/names, diagnostic messages or raw audit envelopes; required
typed audit commits retain exact identity/revision and a content digest.
Duplicate names are not authority keys. Rename does not resume, revive old
questions, change scoped/Perpetual grants or extend meaningful activity.
Create grants no permission and creates no executor/model context. Both require
fresh original-user admission, privacy/ownership and call policy/revision at
COMMIT. Privacy closure clears name drafts and late content. Missing/corrupt/
unsupported data is refused rather than replaced; validated v1-to-v2 schema
maintenance preserves authority and never invents titles.

**Settings > Readiness > Question diagnostics > Review local version (native question)** uses trusted native
input over the durable host question service, separately from legacy
model-action approvals. Answers retain the exact original question/session
and request origin; window focus cannot choose their target. Review, saved
drafts, submitted answers, exact approval and grant use are distinct.
This entry opens no microphone and calls no model or effect handler.
Unknown privacy/ownership, stale revisions, expiry and audit failures deny
the affected interaction. Native record review cannot supply missing script
bytes, containment or deployment authority, and no action-name grants are
migrated into exact grants. General effect dispatch remains gated.

### Exact-grant history filters

After explicit exact-record inspection, native history can filter the trusted committed event by defined audit outcome and exact bounded canonical ReasonCode. Any/empty defaults are unfiltered; both predicates match typed fields, never formatted text, diagnostic mirrors or a separately retained Requested review. Filter text remains volatile and is not logged, saved, sent to a model or included in activity tags. The authenticated continuation contains only its digest, not query text.

Edits perform no read and retire rows/continuation, actionable selection and revocation/batch review, cancelling and suppressing late output. A fresh explicit Read retains the same inspected inert target and rechecks current-record/private-source admission. Signed continuations bind inspected identity/digest, store lifetime, complete viewer/read admission/control revision and finite audit ceiling/digest. Scanned/nonmatching counts and zero-match Next remain explicit within 4096 rows/5 seconds, 50 results/64 KiB and a 15-minute snapshot. Retention gaps fail closed; expired-but-present is not physical erasure. No execution proof, exhaustive/forensic history, replay, export, audit pruning, grant expansion or installed accessibility qualification is supplied.

Failures, safety information, and unavailable speech are always visible. A
VoiceOnly preference cannot hide:

- missing voice or audio output;
- mute and playback failure;
- command or dependency failures;
- protected power proposals; or
- other required recovery information.

## Power and session actions

**Lock the machine** is a real local Windows action. Kora releases microphone
capture before locking.

Shutdown and computer restart commands are non-destructive proposals in the
current release. They are recognized, displayed, auditable, inspectable, and
cancellable, but no operating-system power request is sent.

## Logs

Kora writes structured JSON logs under:

`%LOCALAPPDATA%\Kora\Logs`

There is one rolling file per day named `kora-YYYYMMDD.log`. Kora retains up to
30 days and 30 files.

Independent [SQLite diagnostic retention](settings.md#sqlite-diagnostic-retention)
is now configurable as canonical integer 1–365 days, default/reset 30, through
shared native and exact typed/ACTIVATED host admission. It affects only newly
committed ordinary SQLite logs/spans/owned links. Existing effective deadlines
never change; apply-now/immediate deletion is unavailable and no set/reset
starts cleanup. Schema-v2 evolution accepts only exact validated legacy v1
30-day rows, preserving identities, payloads, trace/reference metadata and due
times; unknown/corrupt formats fail closed without replacing authority.
Independent audit retention defaults to 90; file limits, sessions/
history, grants/approvals and cleanup triggers are unchanged. Perpetual grants
are never time-expired/retained/evicted. Ordinary policy corruption/unconfirmed
writes cannot silently activate defaults: they explicitly report delivery gaps
through the file/recovery path, independently of mandatory trusted audit.

[Future-only audit retention](settings.md#future-only-audit-retention) is now
independently configurable from 30–365 days, default/reset 90. Confirmed policy
reaches NEW required host authority audit and independently qualified diagnostic
audit projections; existing deadlines, bytes, hashes and citations never
change. REQUESTED/terminal preference receipts use the prior policy and
activation follows required evidence/atomic readback/intent outcome.
Corrupt or unconfirmed audit preferences hold new required audit/authority
commits and refuse startup recovery/writes, never silently use 90; explicit
inspection/repair/refresh is required. Passive preserved-row inspection does
not activate settings. No audit pruning, apply-now, deletion, session/history,
grant/approval/task/question, ordinary logging or cleanup-schedule change is
delivered. All grants retain their records/validity/scopes; Perpetual remains
without expiry/retention/eviction. These are metadata deadlines, not permission
authority or forensic/encryption/complete-retention acceptance.

Logs support diagnostics and future local reasoning, but intentionally omit:

- spoken or typed command text;
- recognized transcripts;
- response bodies and spoken text;
- raw audio;
- credentials and secrets; and
- preference values such as the selected assistant name or endpoint ID.

Configuration writes, application and script execution categories, approvals,
protected operations, and their outcomes use typed audit events without raw
sensitive content.
Owned instruction/literal-prompt **Save as new copy** is a native-reviewed same-kind create-no-replace operation, not a profile-original writer.
Typed requested/terminal copy audits bind the host request and a bounded hash of source-qualified base/draft/destination/proposal identity; no raw content enters tags or baggage.
Source/draft remain intact, destination is disabled, and uncertain post-publication outcomes hold only the destination without claiming rollback or inherited authority.

The current host writes both ordinary diagnostics and typed audit events
to those files and independent private-profile SQLite projections. Required
capture/file/database failures are reported and fail admission explicitly,
not silently swallowed.

Ordinary diagnostic and completed-activity persistence uses one bounded 1,024-entry background buffer. Buffer exhaustion refuses admission rather than blocking the UI or silently dropping a record. Recent ordinary evidence can lag while queued; abrupt termination can lose queued ordinary records ([writer](../src/Kora.Application/Diagnostics/BufferedEvidenceWriter.cs)).

Capture freezes structured properties, scopes and host correlation before enqueueing; SQLite validates live diagnostic admission before preparing an owned payload ([capture](../src/Kora.Application/Diagnostics/EvidenceLoggerProvider.cs), [SQLite admission](../src/Kora.Windows/Storage/WindowsSqliteEvidenceSink.cs)).

Background failures attempt independent sinks, report gaps and hold later admission; shutdown joins accepted writes. Capture admission does not hold its lifecycle lock during sink I/O ([provider lifecycle](../src/Kora.Application/Diagnostics/EvidenceLoggerProvider.cs)).

Typed security audits are not fire-and-forget or ordinary buffered records. Asynchronous audit callers await durable delivery on a worker; synchronous authority callers retain their acknowledged-write contract ([audit await](../src/Kora.Application/Auditing/SecurityAuditLogExtensions.cs), [typed audit](../src/Kora.Application/Auditing/LoggerSecurityAuditLog.cs)).

Approval preparation and retirement also await worker-side audit delivery. An unpublished requested approval is not actionable; retiring UI state is not a successful audit receipt. Failed required receipts close host input admission rather than admitting an effect or publishing a grant ([approval implementation](../src/Kora.Application/ViewModels/MainViewModel.cs)).

Normal exit joins tracked grant preparation and persistence. Forced service disposal closes admission and joins owned writes without requiring a stopped UI dispatcher to resume ([lifecycle](../src/Kora.Application/ViewModels/MainViewModel.Appearance.cs)).

An interrupted requested-only preparation never becomes an approval. Newer input, private closure and concurrent revocations fence late publication and dispatch ([blocked-boundary tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.ApprovalResponsiveness.cs)).

The bounded durable path currently covers exact
typed or activated-voice **version** commands: task intent, dispatch, terminal
evidence and receipt are committed before completion. Startup marks intent-only
work Interrupted and dispatched work without a verified receipt Unknown;
it never automatically reruns either. The terminal version receipt is not
proof of an operating-system effect or speech-playback completion.
Task/question/required-audit authority is consolidated in the private schema-v3
interaction store with one transactional connection lease. The validated frozen
legacy task ledger is retained only as an inert migration receipt; independent
diagnostic evidence is not authority. Managed journals remain under the same
verified profile boundary. Valid interrupted transactions reopen atomically;
committed approvals/use counts/session generations are not replayed.
Missing journals, corrupt/unsupported data, permissive permissions or unavailable
ownership/access stop admission explicitly, without file replacement or ACL
repair. Process-interruption tests do not guarantee physical power-loss recovery.
There is still no durable conversation/history UI or general task executor.
### Read-only Evidence inspection

Use `prepare evidence trace <trace-id>` to open/focus this same native inspector
and prepare only its exact **Trace correlation filter**; activated voice requires
the current assistant-name prefix. The value is exactly 32 lowercase ASCII hex
characters, nonzero, without trimming or repair; no GUID, span/header, URI or
inferred trace is accepted. `evidence trace help` is constant, with no current
trace/source/metadata read ([command details](commands.md#prepare-an-exact-evidence-trace-filter)).

**Every other filter and the selected source remain unchanged and AND-composed.**
Preparation retires the old page, selection/details, Find, actions and continuation
and cancels pending pages. It does not read, scan, count or run maintenance.
Exact native value/focus acknowledgment is required; stale/unavailable admission
does not fall back to another source or an unfiltered/latest query.
Choose **Search / refresh** separately to run the existing bounded retained reader.
Trace text is untrusted correlation only: it is never session selection or
identity/ownership/permission/approval/authority, cannot set activity context or
baggage, and cannot promote ordinary logs to the dedicated AuthorityAudit path.
No source capture, model/provider/network, copy/export/persistence or session
activity renewal is added.

**Tray > Help > Evidence & diagnostics** opens a native read-only inspector for actual
SQLite diagnostic, typed audit, completed span and explicit link records.
**DailyLog** additionally inspects existing daily diagnostic JSON independently;
**All** still selects SQLite only and does not count mirrored file copies.
Explicit **CombinedLog** opt-in selects only SQLite ordinary logs and independent
DailyLog ordinary records. It does not change the default or include database
audit/span/link records or file audit mirrors. The pair is independently captured,
not an atomic cross-sink snapshot: SQLite records are ordered by commit time/ID,
then daily records by exact daily name/byte offset. Overlapping IDs, text,
timestamps and trace IDs do not deduplicate records or establish causality.
Each citation keeps its original source/provenance; time filters use database
commit time for SQLite and observation time for daily records. Read selected
trace in CombinedLog remains combined ordinary diagnostics, not a merged graph.
Both sources are re-admitted/verified on every page, even before or after the
source boundary. An included source failure discards the combined content and
reports its status; it cannot fall back to a working source or claim empty
success. Restore original access/source availability and explicitly search
again. Expired/evicted/malformed/foreign/tampered cursors never silently restart
or renew expiry; appends/new days do not widen either captured half.
Choose a source, optionally enter safe text or session/task/trace correlation
filters, then select **Search / refresh**. **Next page** continues that exact
snapshot; later records do not silently extend it. Each page includes stable
`kora-evidence` citations and at most 50 records / 64 KiB of serialized output,
including metadata. A record too large for one page is explicitly marked
`ContentOmitted`; content is never silently truncated.

Segmented SQLite inspection retains the same source-qualified citations and
global commit-time/source/ID/link-ordinal ordering across registered segments,
including retained legacy evidence. Its 4,096-row scan budget is shared across
the whole read, not granted separately to each segment. A versioned snapshot
binds the store identity, inventory generation, segment identities, audit offsets
and individual row ceilings. Appends and new segments do not widen those ceilings.
A later inventory generation can continue only while every original segment
identity, audit offset and ceiling remains valid; incompatible maintenance or
removed history invalidates the continuation explicitly and requires a new search.
Segmented cursors use a versioned signed Brotli payload with a 48 KiB wire bound
and 128 KiB inflated bound, authenticated before decompression. Legacy signed
cursors remain readable; the complete serialized page still stays within 64 KiB.
Trace parents and links resolve only within the admitted snapshot and selected
session. Global diagnostic-audit sequence numbers add each segment's registered
offset to its local sequence; these projections remain separate from committed
interaction-store authority. Inspection never creates a missing segment, mutates
retention, or resets the legacy evidence store.

Explicit offline first-cutover recovery is separate from Evidence inspection and
normal startup. Its exact operator argument and native confirmation can complete
only an empty never-published inventory shell under verified instance/storage
gates, retaining legacy database/journal bytes. It does not start host services,
providers, microphone capture, task replay or automatic owner recovery. Unknown
or partially published state remains held and preserved. Its content-free
bootstrap receipt describes storage completion only; it is not a trusted
security-audit event, proof of resource quiescence or authority to remove an
unclean-owner marker or restart. Each live recovery/launch needs separate
approval even after deterministic source tests pass.
Failure stderr receipts also contain only fixed classifications and commit
state, not rendered exceptions or private paths. Unconfirmed completion never
means rolled back, and no failure receipt authorizes an automatic retry.

**Literal Find** searches only the exact serialized page already displayed
after a successful complete explicit Evidence read, including its literal JSON
escapes, citations and disclosure. It performs no new query or storage read.
Find first/next/previous is case-sensitive, preserves spaces, selects one
nonoverlapping literal and never wraps. The focused inert match shows its
end-exclusive UTF-16 location without changing the page or cited-record
selection. No-match is page-local, not a retained-source search or all-clear.
Incomplete/failed/unavailable/content-omitted results cannot enable Find.

Queries must be nonempty well-formed one-line text, at most 256 UTF-16
characters and 512 UTF-8 bytes, without controls or Unicode format/separator
characters. Invalid input is rejected visibly, never truncated. The query
is volatile and never saved, logged/audited as content, copied/exported,
spoken or sent to a model. Clear clears query/match without reading.
Every read, page replacement, filter/source edit, cancellation, failure,
ownership/privacy invalidation and close clears Find; late results cannot
restore it. These controls do not change typed audit authority or source
availability. [Contract and tests](../Design/Information_Display.md#delivered-bounded-native-evidence-inspection)
are bounded automated evidence, not installed accessibility qualification.

Expand **Advanced filters (optional)** for exact request, invocation, approval
and audit-correlation GUIDs, **From / Until** times, severity and typed audit
outcome. Blank fields are unset. GUIDs require nonempty hyphenated form without
surrounding whitespace. Times require ISO `yyyy-MM-ddTHH:mm:ss` with optional
1-7 fractional digits and `Z` or an explicit `+/-HH:mm` offset, normalized to
UTC. **Both bounds are inclusive**: equal bounds select that exact instant.
SQLite/AuthorityAudit compare commit time; DailyLog compares observation time;
CombinedLog does not turn these into an atomic timeline. An ordinary log's
audit correlation does not promote it into committed authority.

Severity supports **All, Log, Audit, DailyLog, CombinedLog**; typed audit outcome
supports **All, Audit, AuthorityAudit** only. Unsupported combinations show an
explicit error with recovery, not a misleading empty success. **Clear advanced
filters** resets all optional fields and typed choices to unset without a read.
Malformed identifiers/times, reversed ranges and undefined choices do not
default or fall back. Editing any filter clears results, selection and Next,
cancels an in-flight read and requires a fresh **Search / refresh**; old/late
results cannot return. Next continues the exact immutable query. These rules
are exercised by [native typed-filter and real-reader tests](../tests/Kora.Windows.IntegrationTests/EvidenceAdvancedFilterTests.cs)
and [signed-cursor tests](../tests/Kora.Application.UnitTests/Diagnostics/DurableEvidenceQueryTests.cs).

The basic **Span ID** field beside **Trace ID** uses the existing exact W3C
predicate: empty means Any; otherwise exactly 16 lowercase hexadecimal
characters, not all zero. No trimming, case folding, partial match or repair.
It composes with all other filters using AND and is correlation only, never
identity or audit authority. Editing/emptying performs no read and retires
results, selection, Next and late callbacks. **Clear advanced filters**
preserves both basic W3C fields; empty either explicitly to unset it. Close
clears both. Zero matches mean only absence in the filtered admitted snapshot.
This adds no ParentSpanId filter, navigation, persistence, export, clipboard,
model/tool/speech route or filter-value telemetry.

The same [native parser](../src/Kora/EvidenceViewModel.Filters.cs) also exposes **Event ID**, **Category** and **Audit action ID**. Only empty text unsets these three fields; whitespace-only/surrounding whitespace, controls, invalid Unicode and over-limit values fail without a read.
Event ID is canonical invariant signed 32-bit decimal, including zero, negatives and both integer bounds; no `+`, leading zeros, rounding or overflow fallback.

Category (maximum 256 characters) and action ID (maximum 128) compare exact ordinal, case-sensitive text, not rendered results. Category means diagnostic category for Log/Audit/DailyLog/CombinedLog, activity source for Span/Link, and typed audit-category name for AuthorityAudit.
Action ID is audit correlation, not identity, permission or audit authority, and imposes no domain action-name syntax on query text
([SQLite projection/matching](../src/Kora.Windows/Storage/WindowsSqliteEvidenceReader.cs), [Daily matching](../src/Kora.Windows/Storage/WindowsDailyEvidenceReader.cs), [authority projection](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.Inspection.cs)).

Event ID supports All/Log/Audit/DailyLog/CombinedLog; action ID supports All/Audit/AuthorityAudit. All remains heterogeneous SQLite-only filtering; CombinedLog cannot accept action ID because DailyLog has none. Category cannot target unavailable Session/Conversation sources. An exact citation under All narrows field support to its cited source.
Unsupported combinations use explicit recovery before reading; unchanged direct query text rules, scan budgets and 50-record/64-KiB bounds remain enforced
([query admission](../src/Kora.Application/Diagnostics/DurableEvidenceQuery.cs), [compatibility/cursor tests](../tests/Kora.Application.UnitTests/Diagnostics/DurableEvidenceQueryTests.cs), [native/real-reader tests](../tests/Kora.Windows.IntegrationTests/EvidenceAdvancedFilterTests.cs)).

Select a record to inspect parent/link status; **Read selected trace** shows
its retained correlated records and **Open selected segment** follows an
available cited span. Session/task IDs and traces are filters, not permission.
`ExpiredButPresent` means a due record remains readable, not that it was
deleted. `MissingOrRemoved` cannot distinguish an unrecorded segment from
physical removal. Session/conversation sources report Unavailable. This view
is not a complete history, authorization or effect receipt. The separate explicit
**AuthorityAudit** source described above reads committed interaction audit rows;
the diagnostic projection and daily mirrors do not.
Ownership/privacy denial, malformed filters/cursors, corrupt data and private
access failures are visible; no store or permission repair is attempted.
Closing or privacy closure clears/cancels the view without changing retained
sources. There is no copy, export, model reasoning, browser, deletion, grant
use or remote transmission from this inspector.

DailyLog reads only exact Kora daily names from the supplied application-data
Logs directory; it accepts no path, creates no file and repairs no permissions.
It checks current-user source ownership/access, rejects redirected paths, and
verifies opened file identity and captured-prefix hashes before presenting a
page. Its limits are 32 files, an 8-MiB earliest complete-line prefix, 4,096
physical lines, 256 KiB per line excluding the final LF and five seconds per
read. Capture plus final prefix verification reads at most 16 MiB total.
The existing 50-record/64-KiB output limit applies. `ScanLimitReached` does not
mean the full file was searched; paging cannot go beyond the captured prefix.
Next page is tied to one of eight host-held 15-minute snapshots. Appends/new
days do not extend it; eviction/expiry requires a fresh search.

Daily citations are separate from SQLite citations and include trusted
file/offset/digest provenance and the original envelope evidence ID.
Observation time, event name, exception type and redacted typed scopes are
retained, but database commit/due dates are absent:
`RetentionUnknown` does not claim expiry or removal. Audit mirrors are
unsupported, never database audit evidence. Activity/legacy copies and gap
markers are explicitly counted in `DailyReport`; skipped copies show `Partial`.
Corrupt/truncated/changed/missing/expired/unavailable/timed-out sources show
explicit status without partial-success content. Read selected trace remains
in DailyLog; the activity graph is unavailable there. These user-modifiable
records and their correlation never supply identity, permission, an execution
receipt or a complete history. CombinedLog's explicit source-major order is not
cross-source chronological or causal ranking.
Database records receive independent 30-day diagnostic and 90-day audit
due dates. Each admitted owner startup, after storage admission and recovery,
prunes at most 128 due ordinary logs and 32 due spans with their at-most-1,024
owned links in one transaction. Due backlog may remain until later startups;
there is no periodic drain or pruning triggered by reading/searching.
Audit records, tasks, sessions, questions and grants (including Perpetual)
are never removed by this operation. Missing/corrupt/inaccessible storage fails
visibly without replacement or permission repair. This is row pruning, not
forensic erasure of journals, free pages, backups or external copies.
The first-use greeting, settings and version response disclose these limits;
daily-file 30-day/30-file retention is unchanged.
An in-progress evidence cursor whose original snapshot ceiling was removed
fails visibly and requires a new query; reused row IDs cannot supply replacement
citations. Retained child/audit/link references to pruned spans report
`MissingOrRemoved`, not a fabricated complete trace or proven deletion cause.
The full durable design continues writing every permitted
`ILogger` event to daily JSON files and private-profile standard SQLite.
Ordinary records use a dedicated `application_log_events` table. Typed events
marked `SecurityAudit=true` use a separate authoritative
`security_audit_events` table and remain present in the JSON stream. This keeps
startup, database/permission/migration failure, fatal crash and storage recovery
diagnosable when the database is unavailable. Unified viewing/search preserves
stable source citations, reports retention or ingestion gaps, and never treats
an ordinary diagnostic event or file audit copy as proof that an action was
authorized or succeeded.

Database encryption is not mandatory. The composed task/evidence stores verify private
permissions under the supplied local application-data path. It does not
protect against code running as you or an administrator; database/artifact/
backup copies outside that private location are readable. Credentials remain
in Windows-protected storage, not ordinary SQLite fields.

The explicit native
[logical session disposition](windows-and-tray.md#logical-session-disposition)
removes only the addressed live name, questions/drafts/answers, observations,
wait bindings and scoped grants after separate exact-ID preview/confirmation.
It preserves unrelated sessions, independent Perpetual grants, opaque
tombstones, task/event provenance, independent diagnostics and content-minimising
authority audit. Journals/free pages, copied databases and inert migration
storage are not erased. Managed session artifacts/backups/history deletion,
automatic retention/purge and forensic erasure are unavailable. Exports and
provider copies remain outside local deletion. The UI and receipt disclose
these limits; no complete R12 deletion guarantee is made.

Both database tables preserve structured logging fields independently of the
human-readable message: event ID/name, level, logger category, original message
template, typed named properties, structured scopes and admitted correlation
identities. Audit rows add fixed typed audit fields. Rendered text is retained
only as a bounded display/search projection; Kora does not parse it to recover
properties, outcomes or authorization evidence.

The composed evidence sink also records local W3C activity trace/span
relationships and host-owned session/task/invocation/approval identifiers on
diagnostic and audit entries. A session may contain many traces. From a session,
you can review its retained Logs, Audit, or combined evidence; from an entry,
you can inspect its parent/linked trace. Trace and session identifiers are
correlation only, never permission or authentication, and no remote telemetry
export is enabled by this design.

The delivered diagnostic database policy is 30 days; this slice adds no setting
or apply-now UI. The audit policy defaults to 90 days and validates 30 through
365 days, but this slice implements no audit pruning or configuration UI.
Audit records are content-minimising and remain
independent of session deletion; they do not retain deleted chat or argument
content. Audit expiry does not delete perpetual grants. Reading, searching,
exporting or asking questions over evidence never extends its retention.
The future audit configuration flow must preview the affected range and require
confirmation before shortening existing due dates.

The delivered bounded native inspector lists, reads and searches actual
SQLite Logs, Audit, spans and links, independent DailyLog diagnostics, or opt-in
CombinedLog ordinary diagnostics,
without using a model. A future explicit
Ask Evidence action would reason over a
bounded selected set, with links to every supporting record and observed facts
separated from inference. Local reasoning is preferred; sending selected
evidence to a remote runtime requires preview and approval of that exact
payload. Log and audit text is historical untrusted data, never an instruction
or permission.

## Local preferences

Preferences are stored under:

`%LOCALAPPDATA%\Kora\Preferences`

They are device-local and do not roam with documentation or future skills.
