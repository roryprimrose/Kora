# Human Interaction and Persistent Sessions

Status: durable conversation/question/session authority, exact work/lifecycle
controls, memory-only per-session composers and bounded admitted answering are
implemented. Reviewed relatedness and context workflows remain subject to
runtime qualification; general effects and consequential concurrent workers
remain gated. The fixed local-version queue is internal compatibility
infrastructure, not a public scheduling product. See the
[current inventory](Implementation_Roadmap.md#current-delivery).

Related: [Architecture](Architecture.md), [Work Management](Work_Management.md), [Information Display](Information_Display.md), [Security](Security_Data_Flows.md), [User Configuration](User_Configuration.md), [Acceptance Criteria](Acceptance_Criteria.md).

This is the canonical interaction/session contract. It replaces the former single-conversation, single-executor, memory-only-history direction.
Windows login sessions, voice capture generations, provider/SDK conversations, and Kora work sessions are different identities; none substitutes for another.

## Provisional local answer interaction

Use **Answer locally (no tools)** for bounded incremental text in the originating conversation. Provisional text is labelled, volatile and not spoken automatically; **Stop this answer** in the primary pane and the existing exact-task cancellation controls in Sessions/compact retire it independently of the composer. Only validated completed answers are retained as ASSISTANT; failure/privacy/source/view closure clears or invalidates provisional presentation without manufacturing success ([display contract](Information_Display.md#bounded-provisional-local-answers), [native behavior tests](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.Streaming.cs)).

Ordinary tool-capable Send, clarification and hosted requests retain their existing buffered contracts. Local qualification, session instructions, privacy, current-source generation, input/output budgets, original USER and trusted terminal audit rules remain authoritative; this is no runtime auto-enablement or hosted streaming change ([submission owner](../src/Kora.Application/Hosting/SessionConversationService.SessionInstructions.cs), [host selection](../src/Kora.Application/Dependencies/ModelTurnHost.Questions.cs), [retention regressions](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.Streaming.cs)).

The separate native-only **Stream explicitly with Claude (text only, separately qualified)** action now uses the same provisional lifecycle for original-user, tool-free requests. It adds exact-profile stream evidence and separate native physical-send consent, without changing the buffered routes above or enabling the shipped unavailable Claude source ([operation](../docs/claude-requests.md#separately-qualified-native-text-streaming), [host route](../src/Kora.Application/Hosting/SessionConversationService.Claude.cs)).

## Cross-conversation content search and exact message opening

The [native content search owner](../src/Kora/SessionsViewModel.ContentSearch.cs)
completes ordinary query -> bounded retained USER/ASSISTANT matches -> deliberate
selection -> exact owning-conversation message placement and matched-field
scroll/focus. This supports the approved independent-conversation/readable-result
outcomes; storage permission, source validation and native presentation belong to
the host, not a model, tool or semantic-search extension.

It composes existing metadata browse, exact-session lexical search, exact retained
detail revalidation and passive session navigation. Continuations retain original
source generation, sequence ceiling and head revision internally. Names remain
labels; copied/equal rows are not native selections. Every opening revalidates
the message and snapshot without substituting a newer record. Existing message
actions retain original source authority and unsent session buffers are unchanged.

Queries/results are volatile. Explicit clear/edit/close/privacy/source retirement
fences late reads; bounds, empty-page continuation and unavailable states are
truthful. No new schema, index, model/provider/network action, clipboard authority,
retention renewal or history write is introduced. See the [user operation](../docs/windows-and-tray.md#find-a-message-across-conversations)
and [native/storage regressions](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.ContentSearch.cs).

## Conversation Input Realignment

### Stop all submitted work while staying open

The exact original-user `stop all work` action and explicit **All Work > Stop all
work…** control use the same host operation, not a loop over per-session Cancel.
The host reviews every accepted original across sessions independently of the
overview's loaded page, filters and selection. Its reference-owned reversible
barrier fences admission, dispatch and Resume while the review is current.
Preparing/in-admission/completing producers that cannot be represented make the
review unavailable; omissions never prove an empty set.

A separate `stop-all-work` question binds this operation, exact set, original
channel, private/control/source authority and expiry. It cannot authorize Exit or
Restart. Cancel, closing the review or stale authority releases only that attempt,
with no stop. After acceptance, pending originals are withdrawn, running originals
cancelled and already-cancelling work physically joined. Already admitted finite
compatibility reads finish and join; their pending successors retire.

The typed result retains exact original stop acceptance, physical joins and
terminal states plus compatibility terminal facts. Partial audit/storage/source/
release failures remain **PartialUnconfirmed**, never rollback or all-cancelled.
Captured accepted work is awaited even when another participant fails. No stopped
original replays and existing per-session holds still require fresh Resume.
Kora remains open; composers, editors, history, grants and future submissions are
not discarded, deleted or globally paused. A model suggestion is denied even
with a generic reusable grant. Missing native binding is explicitly unavailable.

### Reviewed voluntary application Exit and Restart

Deliberate native, typed and permitted activated-voice application Exit/Restart
protect unsent topic/New request buffers and definition-editor changes independently
of already submitted work. Empty foreground input is not evidence that other
conversations have finished.

The [host lifecycle owner](../src/Kora.Application/Hosting/ApplicationLifecycle/SessionConversationService.ApplicationLifecycle.cs)
holds a reversible, attempt-owned dispatch barrier and snapshots accepted originals
across every session, including pending continuations and explicit MCP execution.
The [native operation](../src/Kora/ApplicationLifecycle/ApplicationLifecycleController.cs)
shows the affected work and separately asks Confirm/Cancel through the existing
private question owner. Model action approval, opening Exit or discarding unsent
text never supplies submitted-work cancellation authority. Confirmation retains
the original channel; stale source, work revision, generation or private ownership
requires a fresh deliberate operation.

Cancel releases only that attempt's barrier without stopping work. After current
confirmation and editor approval, trusted work-control audits precede exact pending
withdrawal/running cancellation. Every accepted provider/native resource is
physically joined before the existing desktop Close or application-process restart.
Already dispatched finite compatibility reads finish under their existing rules;
reviewed pending compatibility reads are cancelled, not restored as a product.
Failed audit, terminal readback or uncertain resource release blocks clean success
with a visible recovery message. Stopped requests are never retried or resurrected;
retained history and grants are not deleted.

Mandatory lock/privacy/ownership retirement and OS-required shutdown remain
prompt-free. This does not add computer power actions, input autosave or provider
qualification. [Host regressions](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.ApplicationLifecycle.cs)
and [native regressions](../tests/Kora.Windows.IntegrationTests/ApplicationLifecycle/SessionsConversationTests.ApplicationLifecycle.cs)
use deterministic provider/process seams, not installed-provider or OS power trials.

Ordinary Send and the exact typed/activated-voice request-confirm route share
the [submitted-turn queue](Work_Management.md#ordinary-submitted-conversation-turns).
A busy existing Active conversation can accept an immutable pending request
without waiting for its previous provider turn. Sessions/compact show the
actual submitted request and position and let the user cancel exactly that
pending request. Only confirmed success and physical resource closure permit
automatic next dispatch; held work needs deliberate resume and unchanged
original authority. Questions, approvals, specialized source workflows and
unqualified providers retain their existing unavailable/review fences.
Acceptance is not execution success, action approval, renewed source consent or
foreground voice targeting. Unsent input remains memory-only and separate.

Explicit Claude streaming remains outside the ordinary submitted-turn queue:
neither route replaces an active turn owned by the other. Refusal retains no
new input and does not change pending order or renew provider/egress authority.
Streaming cancellation still joins the physical worker before terminal
retention, and provisional or late cancelled text is never an ASSISTANT answer
([combined regressions](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.ClaudeStreaming.cs),
[ordinary FIFO regression](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.Pending.cs)).

Each queued execution confirms its original terminal readback before publishing
terminal state and releasing its physical-completion lease. Returning the
confirmed receipt does not reread that source after release, preserving the
reviewed stop-and-delete boundary without changing FIFO or original authority
([combined FIFO/readback regression](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.Pending.cs),
[tool-join/deletion regression](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.StopAndDelete.cs)).

The [approved product decision](MVP_Scope.md#approved-retirement-and-simplification) replaces first-class saved-conversation-draft management with unsent composer text. A session is an ongoing conversation/work stream, not a draft document; Active/Done lifecycle is independent of whether text is waiting in its input box.

Keep unsent input per exact conversation in memory across view/session switching, share it consistently between Sessions and compact views, and never submit or merge it implicitly. Do not add disk autosave. Protect deliberate discard/exit and apply privacy/ownership retirement without cross-session leakage.

### Deliberate exact retained-answer replies

The native [Reply interaction](../docs/reviewed-context.md#reply-deliberately-to-one-exact-assistant-answer) completes one follow-up in the same exact Active conversation: choose a displayed retained ASSISTANT answer, write unchanged new input in its memory-only composer, inspect the complete bounded answer as separate inert LocalOnly evidence, separately confirm once, and retain a genuine admitted answer or truthful HOST failure. The original USER is not implicitly included; this is the exact-answer subset of the existing [mixed-context workflow](../src/Kora.Application/Hosting/ReviewedContext/MixedContextWorkflow.cs), not automatic recall or another context engine.

The [native owner](../src/Kora/ReviewedContext/SessionsViewModel.AssistantReply.cs) binds the source to the exact presented row/page, selection, private control revision, new input and existing retained-source revalidation. Source/page/selection/lifecycle changes retire review and block plain-body fallback until explicit Cancel or a fresh exact Reply selection. Per-session unsent text preserves that refusal across switching; privacy clearing and STOP/resource joins remain authoritative. There is no new voice/typed ordinal route, provider enablement, hosted egress, permission, memory promotion or disk draft storage ([behavioral evidence](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.AssistantReply.cs)).

Saved-draft catalogue, ordinary Save/Reload/Clear, comparison and rebase management are removed.
Preserve only necessary read-only recovery of already saved text, historical audit/schema compatibility and existing retention/deletion handling. Question-answer drafts and skill/instruction/prompt editing buffers are separate concepts and remain supported.

## Exact current-host pending-question focus - 2026-10-21

`open pending question <session-id> <task-id>` is typed/current-name-activated
navigation only. Both canonical nonempty lowercase D IDs must match the existing
host-owned current native controller and admitted original wait. The binding is
captured before either UI queue or generic feedback; IDs and selected-session/
All work metadata never confer question authority. `pending question help` is
constant and reads no question.

The delivered production host has only one current local-version wait. Navigation
does not add a question catalogue, rebind a session, reopen a closed presenter,
restore history, refresh a question, or read persisted question bodies. A
metadata-only exact wait/run/task/session and trusted-question-digest check
validates the original record without replacing it. Original input, private/
ownership/call/name/control/voice admission and native lifetime remain checked
through both queues, activation and native acknowledgement. Unbound, changed,
expired, terminal or uncertain sources fail closed, never focus another target.

The same window, question, generation/revision, local answer and original expiry
remain unchanged. Focus activation skips the ordinary source-refresh path only
for this bounded request; existing keyboard/accessibility and explicit answer
admission remain unchanged. No question creation, answer, approval, dispatch,
replay, selection, source renewal, speech, clipboard, activity renewal or deadline
extension is implied. Navigation has its own content-free status, not the
question/security/response/transcript/speech surfaces.

This consumes the existing original wait and presenter contracts only. No
provider, hardware, installed-native, retired experiment or broader E/Q evidence
is an implementation prerequisite. Deterministic parser/application/headless
native and durable metadata tests own this boundary.

## Bounded admitted local answer execution - 2026-10-20

Explicit Sessions, shared compact, fresh New request and expanded-prompt
submissions now consume the same [conversation execution workflow](../src/Kora.Application/Hosting/SessionConversationService.Execution.cs).
The original USER body/task/request is committed first. Exactly one configured
`IModelTurnAdmission.AdmitAsync` may issue a turn. Only the same
[ModelTurnHost execution port](../src/Kora.Application/Dependencies/IModelTurnExecution.cs)
calls its existing single-use `RunAsync`; no legacy reasoner, model-supplied
delegate, bootstrap action, provider switch, retry or installation is used.
Existing production registrations remain unqualified: their actual
UnregisteredModel/QualificationPending/unavailable reason is retained without
an assistant answer. This implementation does not qualify any runtime.

The [canonical original-user submit](../src/Kora.Application/Hosting/SessionConversationService.cs)
preserves either LocalUi or ActivatedVoice origin through the original message,
model provenance, separate assistant response and typed audit. The existing
[source-bound review/confirmation workflow](../src/Kora.Application/Hosting/SessionRequestCommandService.cs)
remains authoritative; voice gains no native saved-draft writer authority and
cannot be relabelled as LocalUi. Deterministic
[host execution tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.ConversationExecution.cs)
and [private completion/restart tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteConversationTests.Execution.cs)
cover both original channels.

For an admitted qualified local turn, one audited private transaction records
DispatchRecorded on the **original** task and binds the original message to
the host-issued turn/provenance and exact session metadata revision. The host
keeps its owner/issuer/single-use/policy/provider/qualification/context/deadline,
session/task/generation, registry and resource-quiescence checks. Consumer
private/source callbacks and exact metadata are revalidated before/after model
and tool continuations. Expanded-source asynchronous validation is also retained
around the host callback. Only the host's existing read-only registry is callable;
this creates no grant, approval, question or arbitrary-effect authority.

Only a genuine successful **answer-only** host result can become
[ConversationResponse](../src/Kora.Core/Conversation/ConversationResponse.cs).
Exact request/session/task/turn/provider provenance, strict Unicode and complete
serialized `LocalModelResponse` <=4096 UTF-8 bytes are validated, using shared
ConversationText bounds and credential rejection without normalization,
truncation or repair. Action/grant/question/malformed/oversized output cannot
be silently retained as successful prose. A rejected answer is Unknown /
InvalidProviderResponse, not an answer or successful task.

Assistant data is a distinct nullable field of its original message, never USER
text or HOST status. The [private completion transaction](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.ConversationExecution.cs)
commits the complete response, terminal task and required content-free typed
requested/terminal audit together. Strict exact-message and exact-task readback
precedes publication. The host itself does not append a second task terminal.
Cancellation/timeout after possible dispatch maps conservatively to an Unknown
task rather than claiming physical cancellation; caller cancellation, source
loss or uncertain commit/readback may leave DispatchRecorded held for inspection.
Restart recovers that dispatched task as Unknown with no answer/replay. Intent-only
legacy recovery remains Interrupted. Late output cannot append an answer or release
an unquiesced provider slot prematurely.

Schema 12 [migrates the message revision constraint](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.ConversationExecution.cs)
transactionally from schema 11 to admit terminal revision 3; exact legacy schema,
integrity and authority are validated before replacement. It retains every
original payload, sequence, task and audit byte and the private-copy lifecycle. Legacy
message format 1 canonical bytes/digests are unchanged (new null fields are
omitted). Format 2 is admitted only for a bound execution: revision 2 is
dispatched and revision 3 terminal, with independently versioned response
format 1. Unknown/corrupt/noncanonical formats fail closed. Execution and
terminal revisions invalidate existing conversation snapshot cursors. The
100-message allowance and chronological sequence count original USER submissions,
not assistant bodies. Complete 50-record/64-KiB pages include the response once;
no separate body cache/table/index or unowned copy is added. Existing disposition,
retention, private/tombstone and journal/free-page cleanup remove the entire cell,
including assistant output, while preserving independent audit/grants.

Sessions/compact rows, immediate submit outcomes, New request and retained
Details show complete inert **ASSISTANT** text separately from original
**USER** and typed **HOST**. Details exact-source/digest and original-text
reuse remain USER-only; assistant output acquires no clipboard, export,
rich-link or reuse authority. Existing USER/HOST lexical filters still search
only original USER and typed HOST fields, not generated prose or full chat.
All private/source/lifecycle/selection/close fences clear assistant projections.
Saved-draft recovery, Compare, dirty-buffer decisions and immutable submission
origins stay unchanged. No history/recall/egress assembly or automatic session Done
is added.

Deterministic [Core response tests](../tests/Kora.Core.UnitTests/Conversation/ConversationResponseTests.cs),
[integrated retained loop tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.IntegratedExecution.cs),
[private SQLite tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteConversationTests.Execution.cs)
and [native tests](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.Execution.cs)
cover this path, not installed/model/provider/hardware E/Q qualification.
Follow [maintained development checks](../CONTRIBUTING.md#development-checks)
for affected changes; unrelated qualification is not an implementation prerequisite.

## Delivered passive exact-conversation lexical search — 2026-10-13

The [conversation search workflow](../src/Kora.Application/Hosting/SessionWorkspaceService.ConversationSearch.cs) reads only the addressed retained session under current private/source admission. USER text, separately typed HOST terminal metadata and retained ASSISTANT output remain distinct; search never promotes source words to host authority.

Sessions/compact retain lexical queries, observed task-state and inclusive submitted-date choices through [#428](https://github.com/roryprimrose/Kora/pull/428). Engineering ID/sequence/count/generation/revision query widgets are retired; internal domain validation and source-bound cursors remain.

Every consumed page/record is validated before exclusion or byte/result limits. The existing 100-message bound, complete 64 KiB page budget, explicit chronological ceiling/head revision and authenticated query-bound continuation remain. Invalid input is explicit, never truncation, normalization repair or a successful empty read.

Query/filter edits, cancellation, source/private/session changes and closure retire results, navigation and late callbacks without reading or changing any conversation's unsent input. Passive Done reads neither Resume nor renew activity. Submitted-date/state predicates are metadata constraints, not intent, approval, source consent, model context or execution permission.

### Exact-address original-user conversation search commands - 2026-10-20

The [reserved command family](../src/Kora.Core/Commands/SessionCommand.ConversationSearch.cs) and [host service](../src/Kora.Application/Hosting/ConversationSearchCommandService.cs) retain their exact addressed-source contract. An explicit search is not a global conversation read, draft search, implicit submission or replay. Unknown/stale targets do not select a newer or similarly named session.

The [user guide](../docs/commands.md#search-one-explicitly-addressed-retained-conversation) owns the current syntax and unavailable boundaries; no additional preparation-only command is approved by the existence of this implementation.

### Explicit retained ASSISTANT search

The existing [role policy](../src/Kora.Core/Conversation/ConversationSearch.cs) admits **ASSISTANT** only when explicitly selected, matching `AssistantResponse.Body` and no USER, HOST, provider or provenance fields. **All** remains USER/HOST-only for compatibility. Absent answers do not fall back; complete record/provenance validation precedes all exclusion. Lexical grammar and retained bytes remain unchanged.

The [exact command service](../src/Kora.Application/Hosting/ConversationSearchCommandService.cs) binds original role/query/channel and source to its original nonrenewing 60-second continuation. Native [Search/Next and detail retirement](../src/Kora/SessionsViewModel.ConversationSearch.cs) uses the same canonical search. [Complete page validation](../src/Kora.Application/Hosting/SessionWorkspaceService.ConversationSearch.cs) admits terminal assistant revision 3 while preserving bounded head revisions, full serialized byte budgets and exact cursor/digest/source fences.

The [complete interaction](../docs/windows-and-tray.md#search-retained-assistant-answers) is explicit role/query, Search/Next, inspect complete inert answer, Cancel/Clear. This grants no new context/reuse/export, retention renewal, index, persistence, model/provider invocation or profile enablement. Deterministic [portable tests](../tests/Kora.Application.UnitTests/Hosting/SessionWorkspaceServiceTests.AssistantSearch.cs) and [private SQLite/native tests](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.AssistantSearch.cs) cover the bounded implementation, not installed hardware/provider qualification.

## Delivered exact-session conversation/composer foundation — 2026-10-12

### Session-local unsent input and legacy compatibility - 2026-10-21

Sessions and compact share independent memory-only input for each exact session. Switching topics preserves it; last-view and voluntary shutdown use Discard/Cancel. Privacy/ownership retirement releases volatile state. There is no ordinary conversation Save/Reload/Clear, saved-draft catalogue, comparison, rebase or disk autosave ([input owner](../src/Kora/SessionsViewModel.Unsent.cs)).

Legacy tables/audit/schema/retention stay compatible. Exact-session recovery copies observed old text only into EMPTY input; changed source/revision refuses publication. It neither submits nor renews activity ([read boundary](../src/Kora.Application/Hosting/SessionWorkspaceService.Conversation.cs), [tests](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.Unsent.cs)).

The [conversation service](../src/Kora.Application/Hosting/SessionConversationService.cs) retains exact original submitted USER content and its original host-issued request/task under private ownership, source, generation/revision and audit checks. Credential-like structural screening rejects before body writes; malformed/oversized content never becomes truncated or repaired success.

Original task/message/audit admission and final provider-neutral outcomes remain atomic and truthfully unavailable when runtime qualification is missing. The [admitted answer path](#bounded-admitted-local-answer-execution---2026-10-20) is separate from retaining original input. A model result never becomes USER or typed HOST authority, and uncertainty never authorizes duplicate Submit or replay.

The 100-message per-session bound is not an aggregate session/storage quota. Complete chronological pages retain the 50-record/64 KiB limit, explicit original generation/sequence ceiling/head revision and source-bound continuation. Later append cannot expand the ceiling; changed original terminal observation requires a fresh read ([page policy](../src/Kora.Core/Conversation/ConversationText.cs)).

Passive history/Details do not Resume, renew activity, run cleanup, submit or grant export/model authority. Private/source/session loss fences retire display and late callbacks. Audit stays content-minimized, separate from bodies ([retention boundary](Security_Data_Flows.md#exact-session-conversation-retention-boundary)).

Disposition and retention inventory actual conversation and legacy saved-text cells, owned journal/free pages and known copies. Unknown/live work and uninventoried copies hold completion; unrelated sessions, independent Perpetual grants and required audit remain ([SQLite authority tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteConversationTests.cs)).

## Delivered explicit expanded-prompt submission — 2026-10-13
The native owned-v2 prompt form can independently review an exact existing Active target and explicitly retain its current expanded USER text through the same `SessionConversationService`, screening, atomic message/task/audit and provider-neutral admission. Preview and old Use remain nonpersisting. No new schema, separate form-value storage, source-authority inheritance or draft import is introduced.
The final confirmation names immutable target identity/revisions and displays the entire body, with private readable SQLite/retention and unavailable-generation disclosure. Source provenance is volatile only. Cancel writes no message; stale target/source/private admission refuses, and uncertain COMMIT requires exact receipt inspection without replay. See [prompt controls](../docs/instruction-editor.md#submit-an-expanded-request-to-one-exact-session).

## Exact-session provider policy

The [durable sparse provider override](Model_Providers_Memory_And_Knowledge.md#session-overrides-and-effective-resolution) belongs to one exact existing eligible session. Schema 9 adds only its audited private policy table; legacy sessions inherit without fabricated choices and old immutable history/audit bytes remain unchanged.

Native selected-session and exact typed/current-name activated review/confirmation share original-user/private admission, generation/policy/app/catalogue/account fences. Session retention/disposition inventory and remove the policy; passive status does not renew activity or create dispatch authority. Provider-neutral turn admission captures immutable resolution and policy revision; later policy edits never mutate an admitted turn. Generic routing, one-turn directives, authentication/providers and buffered-bootstrap rerouting remain unavailable.

## Durable Single-File Session Attachment

The [bounded R26.1e attachment](File_And_Folder_Ingestion.md#delivered-durable-single-file-session-attachment---2026-10-10) belongs to one exact host-owned session, not the picker request's random session, a selected display name, provider conversation or model context. Native metadata review and separate durable-copy confirmation admit only one immutable UTF-8 text/Markdown file, up to 256 KiB original bytes, within the sixteen-retained-file private-profile bound; no eviction or silent replacement.

Interaction schema 8 preserves existing memory/grant, queue/admitted-deadline, history/retention rows and old audit hashes. Source format 1 stores exact original bytes and validated historical source/revision/item/digest, profile/session/generation and capture provenance. Neither the retained descriptor nor its old path can authorize dispatch, fresh filesystem reads or egress.

Native full-source inspection and the same lexical-lines citations remain available after restart and for retained Done sessions, without resuming them. Browsing does not renew meaningful activity or invoke cleanup. Existing passive-inspection/source-copy/retention fences, original/current privacy/control generations and actual quiescence remain mandatory.

Exact reviewed attachment removal revokes native view/search before atomic body/path-metadata removal and owned-journal verification. Session disposition and ordinary deletion inventory the same copies; live/Unknown work and uninventoried copies hold completion. Content-free provenance remains, original user files and unrelated/kept sessions/Perpetual grants do not change. No body is added to history, memory, preferences, models, logs or audit text.

The separate [retained metadata catalogue](File_And_Folder_Ingestion.md#delivered-retained-attachment-metadata-catalogue---2026-10-11) retains Browse/ExactId, lifecycle, media type, copy status, byte bounds, ordering and Find within selected displayed metadata. Technical correlation/generation/digest query widgets are removed.

Displayed provenance and complete-source/cursor validation remain internal requirements. Catalogue metadata never proves body availability, current source integrity, session authority, consent or activity. Inspect separately revalidates the exact retained copy; no original-path read or automatic refresh is implied.

## Current Implementation and Design Gap

Public diagnostic scheduling/preparation/settings are retired. Inspection, reviewed cancellation/removal/clear and internal scheduler/store/configuration/audit invariants remain. Passive refresh never replays old work ([compatibility contract](Work_Management.md#delivered-deterministic-local-version-queue)).

Current-run pending-question recovery uses only the host's original live wait/key/revision/expiry. It restores committed QUESTION drafts, not conversation input, and creates no task/question/grant/dispatch. Restart has no live registration ([question recovery](Interaction_Fallback.md#current-run-dismissal-recovery---2026-10-11)).

Normal loaded-event browsing retains category/reason only. It clears stale selection/review on edits, source/private retirement and refresh; internal source/correlation/profile validation remains. Event text cannot retarget questions, authorize work or establish a global all-clear ([local event contract](Proactive_Interaction.md#native-content-free-loaded-event-filters)).

The remaining sections distinguish implemented session metadata/history/retention and original-task controls from proposed general execution. Neither retained diagnostics infrastructure nor deterministic test evidence qualifies production providers, arbitrary workers, effects or model routing.

### R12 Bounded Session Retention Delivered - 2026-10-09

Schema v6 preserves v5 queue authority and adds one durable per-session meaningful-activity timestamp. Default
archive is 24 hours and deletion 30 days from that same timestamp, not from
creation/archive. Create/resume, admitted substantive original-user work,
accepted final answers/cancellation and real dispatch/terminal progress refresh
it monotonically. Drafts, passive browse, rename and control/configuration
bookkeeping do not. Unknown historical activity gets a conservative migration
baseline; no chronology is fabricated. Native future-only
[configuration](User_Configuration.md#r12-bounded-session-retention-delivered---2026-10-09)
does not alter existing due dates without subsequent meaningful activity.

Startup checks and an owned one-minute host-only maintenance timer
process at most 32 candidates, never execute or schedule work. Intent-recorded,
dispatched, Unknown work, unresolved questions and current-run control
authorities hold rather than being abandoned; held controls cannot starve due
idle sessions. Exact-generation audited Perpetual session marking
holds the whole session, independently of Perpetual permission grants. There
was no marking UI in this original slice; the native increment below supplies it. Archive revokes source/generation/scoped authority but
preserves content and does not reset the activity clock.

Deletion awaits source/presentation revocation, removes owned history,
question/draft/answer, task/event/wait/run, terminal queue, observation, scoped-grant and metadata
content, legacy task copies and authenticated owned artifacts/staging, and
clears SQLite row/index/free-page and committed rollback-journal copies.
Unrelated sessions, independent artifact deletion owners and independently
Perpetual grants survive. Only exact-ID content-free authority tombstones,
redacted history gaps and content-minimised independent required audit remain;
late append/publication is denied. Removing authority alone is not acceptance:
a separate final inventory receipt is required and interrupted acceptance
retries storage cleanup without execution.

Inactivity deletion uses the same in-memory SQLite rebuild as explicit
disposition, including space freed by earlier revisions or schemas, before
committing final `Purged` acceptance. Copy inventory is rechecked at that
commit fence. A rebuild, inventory or final acceptance failure after revocation
is explicitly incomplete, not rollback or a successful deletion count;
maintenance can retry storage cleanup without executing retained work.
Post-revocation caller cancellation does not interrupt this finite acceptance.
Unrelated live content and the independent audit chain are preserved.

Unrecognised ownership, malformed envelopes, uncertain key publication,
uninventoried storage/backup entries or hot/nonempty journals fail closed.
No managed-backup publisher/ownership format is delivered; arbitrary copies
are held, not guessed or claimed erased. No provider/user-export, forensic or
media-erasure acceptance is claimed. This entry supersedes older statements
below that retention/timers were absent from their historical bounded slices.
Full R12 remains partial; blocked R11 is required only for the remaining
general execution/queue integration, not the delivered fixed local-version queue
or this non-executing maintenance increment.

Validation after rebase onto `5755aa7d` (#118): Release 0 warnings/errors using
existing locked dependency assets; no restore/feed change needed.
Core 1,028, Application 2,860, Tools 69, Definitions 6, Windows 1,215 passed,
zero failures/skips;
portable coverage 100% lines/branches. Tests check actual plaintext absence
from database/journal, owned file removal, preservation, holds, cancellation,
invalid persistence/migration, interrupted acceptance, bounded fairness,
queue holds/inventoried deletion, restart no-replay and retained-source
invalidation of #118's selected-session work surface without losing unrelated
work or its passive-refresh/question coexistence contracts.
See the [dated roadmap receipt](Archive/Implementation_Roadmap_2026-10-11.md#r12-bounded-session-retention-delivered---2026-10-09);
installed/native/RC and full R12/A3 qualification remain open.

### R12 Native Exact-Session Retention Controls - 2026-10-10

The native Sessions panel passively reads the selected immutable ID's actual UTC meaningful-activity, archive due and delete due dates, generation, exemption audit revision and kept/purged state.
It reports only observed work/control holds; copy inventory and broader maintenance availability are explicitly unobserved, never inferred eligible. Names, traces, history, grants and old previews supply no authority.

**Keep this session** and **Use ordinary retention** each stage a native review, followed by a separate exact confirmation. Ordinary review displays the unchanged recorded dates, labels already-due observations and warns that background archive/delete may follow soon or already be due.
This is a SESSION retention hold, not a Perpetual OPERATION grant, consent, durable memory, cross-session recall or execution.

Fresh original native input, existing `SessionWorkspaceService` committed control intent, current ownership/privacy/call/input lifetime and exact nonremoved session admit the setting.
The private SQLite transaction compares generation, all displayed recorded state and the last exemption's committed audit sequence, including hold-change/restore ABA. Clock/activity, policy or another hold mutation conflicts rather than silently changing reviewed consequences. Done sessions use supported metadata-control authority without resume.

Required typed audit and hold update share one commit; terminal bookkeeping is non-substantive, followed by exact readback. Commit/receipt/readback uncertainty is not rollback or success: refresh durable state and review again, never automatically replay.
Selection changes, closure, privacy loss, purged/removed/unknown or corrupt state refuse and clear previews. Independent grants, memory bodies, source/copy inventory, unrelated sessions and the private timer are unchanged.

Inspection no longer runs maintenance. The replacement passive pre/post admission preserves confirmed retention-policy, ownership/privacy/call and verified copy-inventory checks.
Due ordinary candidates without existing live/Unknown/question/control holds deny content inspection pending authoritative background maintenance; source authority revision changes suppress late results. Exact retention-status readback is non-content observation, not a cleanup bypass.
No new apply-now, archive/delete, global default, direct typed/voice hold mutation or model descriptor is available.
The existing exact-ID field also supports native **Read exact ID retention**, using canonical immutable-ID validation. It keeps retention status/review available when due-content inspection is held, without reading names/history/work or fabricating active/resume authority; removed/purged status remains nonmutable.

Maintained [actual SQLite controls](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionRetentionControlTests.cs), [portable admission](../tests/Kora.Application.UnitTests/Hosting/SessionWorkspaceServiceTests.Retention.cs) and [headless native review](../tests/Kora.Windows.IntegrationTests/SessionsRetentionViewModelTests.cs) cover this subset.
This does not qualify installed native/accessibility, forensic/full-copy deletion, general execution or full R12/RC acceptance.

### Exact Typed And Activated-Voice Retention Review - 2026-10-20

The strict reserved grammar accepts only canonical nonempty immutable session GUIDs, never names or current selection.
Activated voice requires the current assistant-name prefix; invalid reserved input is not a question answer or model request ([parser](../src/Kora.Core/Commands/SessionCommand.Retention.cs), [input routing](../src/Kora.Application/ViewModels/MainViewModel.Sessions.cs)).

```text
session retention status <session-id>
session retention review <session-id> keep
session retention review <session-id> ordinary
session retention confirm <session-id> <review-id>
session retention cancel <session-id> <review-id>
```

Status is a passive exact metadata observation, not maintenance or cleanup eligibility.
Review discloses the original recorded UTC dates, generation, committed exemption audit revision, channel and 60-second expiry; ordinary retention may already be due. Confirmation or cancellation consumes the memory-only review once.
A changed source, privacy/call/input revision, expiry, generation, date or hold-change/restore ABA refuses the handoff ([command workflow](../src/Kora.Application/Hosting/SessionRetentionCommandService.cs)).

**Route limit:** command confirmation opens the same exact native review; it does **not** change the hold.
The original command channel remains attached to the review and linked presentation activity. A genuine **new native decision** is required because both owning application and SQLite write contracts require fresh native input.
No voice input is relabelled LocalUi, and no requested/terminal hold audit is manufactured for opening a panel ([native handoff](../src/Kora/SessionsViewModel.Retention.cs), [owning action](../src/Kora.Application/Hosting/SessionWorkspaceService.Retention.cs), [private transaction](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.RetentionControl.cs)).

The subsequent native decision uses the unchanged committed control intent, atomic audit/hold transaction, physical COMMIT admission and terminal/readback path. The shared recorded-state comparison includes the exemption audit sequence.
Done sessions remain Done without resume; opening/status never reads content, renews activity or runs maintenance.
Unknown/unconfirmed opening or commit outcomes are not success or rollback and are never automatically retried ([recorded-state rule](../src/Kora.Core/Storage/SessionRetentionObservation.cs), [deterministic native/private SQLite tests](../tests/Kora.Windows.IntegrationTests/SessionsRetentionCommandTests.cs)).

This is one SESSION hold, not a grant, consent, memory, provider permission, cleanup action or global retention setting. Model retention control remains unavailable; the six read-only descriptor families are unchanged.
Implementation consumes only the existing native retention seam with deterministic fakes/private SQLite tests; installed-native, acoustic, provider, hardware and RC qualification are not claimed ([bounded command tests](../tests/Kora.Application.UnitTests/Hosting/SessionWorkspaceServiceTests.RetentionCommands.cs)).

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
dependency. Full composer, model history reasoning, Ask Evidence, broad
export, queues and scheduler remain unavailable. Unique storage/interruption/
capacity/copy evidence in the storage experiment is retained, not deleted or
claimed replaced by this increment.

### Delivered Passive Session-List Name and Exact-ID Search - 2026-10-10

The [session list workflow](../src/Kora.Application/Hosting/SessionWorkspaceService.ListSearch.cs) supports bounded Browse, literal name substring, whole exact name and immutable exact session ID. Lifecycle and observed name availability remain ordinary filters; duplicate names do not confer identity or choose action/voice authority.

Native generation, name-revision and Unicode-name-length query ranges are removed. Original metadata/name validation, full-row validation, optimistic source identity and private host admission remain authoritative. Explicit Search/Next reads; edits/Clear retire stale pages and selection without reading.

Pages are bounded mutable keysets, not a historical or exhaustive inventory. Zero matches may have Next; omissions and changed/unavailable sources stay truthful. Names cannot retarget a session action or infer permission, and switching topics retains independent unsent input ([presentation](../src/Kora/SessionsViewModel.ListSearch.cs)).

### Delivered Passive Exact-Session Lexical History Search - 2026-10-09

The [receipt search presenter](../src/Kora/SessionsViewModel.Search.cs) reads supported retained interaction receipts for the exact session, separately from ordinary conversation-body search. Meaningful receipt kind, task state, decision, question state and answer-channel choices retain explicit Any through the [ordinary-search correction](https://github.com/roryprimrose/Kora/pull/428).

Raw task/request/question/source IDs, revision/generation and technical range query widgets are removed. Internal typed metadata, provenance and complete-source validation remain unchanged. Source/private/selection changes, invalid input and cancellation retire pages and late callbacks; reading never answers/replays work, resumes a session or renews activity.

Result Details use their original immutable cited source and independent disclosure checks. Finite pages with Next and unavailable/omitted data are explicit, not a global or exhaustive history claim ([guide](../docs/windows-and-tray.md#bounded-passive-interaction-history)).

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

**Preview-launch consumer - 2026-10-20:** original typed/current-name activated voice accepts only `review session disposition <canonical-D-guid> <positive-generation> <nonnegative-metadata-revision>`. The reserved grammar rejects malformed, oversized, control-bearing, quoted/name/selected and command-confirmation inputs before question/model fallback ([parser](../src/Kora.Core/Commands/SessionDispositionReviewCommand.cs)).

Application admission delegates to a narrow private-native presenter; desktop opens the existing authoritative preview action for the supplied revisions, without following a newer generation or metadata revision. The native surface shows the complete eight-count inventory and existing scope/exclusions; none is returned to speech, model context or automatic clipboard ([Application seam](../src/Kora.Application/Hosting/ISessionDispositionReviewPresenter.cs), [shared action](../src/Kora/SessionsViewModel.cs)).

The command cannot remove data. Confirmation remains a separate deliberate native input and fresh LocalUi request; original voice origin is preserved for preview, never promoted to native confirmation authority. A single current host-held preview is required, replaced/late previews cannot revive, and native-owner loss fails closed ([host gate](../src/Kora.Application/Hosting/SessionWorkspaceService.Disposition.cs), [native owner](../src/Kora/SessionsWindowController.cs)).

Review navigation preserves session-local unsent input without a Save decision. Confirmed disposition retires affected volatile text; stale store/control/session/copy/resource/private admission retains refusal and recovery, never retry or synthetic consent ([native tests](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.DispositionReview.cs), [store tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionDispositionTests.cs)).

This consumes the delivered preview/private-native-confirmation Implementation subset only. It adds no cleanup, forensic-erasure, provider, physical/audio or installed-native qualification claim; unrelated Enablement and RC qualification remain outside this bounded command ([scope](../src/Kora.Core/Storage/SessionDispositionPreview.cs)).

New original input retires an in-flight launch without overwriting the newer response. Preview has its own exact-session request with original origin and a causal ActivityLink; a later native confirmation gets only its ordinary fresh native authority ([routing](../src/Kora.Application/ViewModels/MainViewModel.SessionDispositionReview.cs), [desktop action](../src/Kora/SessionsViewModel.cs)).

An existing private draft-recovery review blocks command launch rather than being stolen; passive refresh pauses during that review, while explicit source/lifecycle controls retain their existing revocation behavior ([presentation gate](../src/Kora/SessionsViewModel.Work.cs), [regression tests](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.DispositionReview.cs)).

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

The [native presenter](Interaction_Fallback.md#synchronized-native-terminal-publication---2026-10-12) now publishes terminal state and completion together. The original and external cancellation channels retain the same committed receipt without overriding an earlier denial/failure. Private clearing remains mandatory after completion; session/task/question/revision/generation admission and the durable atomic cancellation/audit contract remain unchanged.

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

The [native exact-grant management subset](User_Configuration.md#delivered-native-exact-operation-grants--2026-10-10) now adds bounded passive inventory/current inspection and explicit one-ID/revision revocation. Reads create no task intent or meaningful-activity extension. Revocation uses a fresh [original-user control admission](../src/Kora.Application/Interaction/ExactGrantControlAdmission.cs), never the stored proposal/request/trace or a retired origin session as authority.

The [narrow store seam](../src/Kora.Core/Storage/IExactGrantStore.cs) uses the shared initialized private store/read lease. The [SQLite implementation](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.ExactGrants.cs) updates only the exact unchanged active record under the same committed-intent lease and transaction used by consume/lifecycle. It increments revision, retains safe revoked provenance and preserves unrelated records and all independent Perpetual records.

One atomic authority envelope retains the correlated typed **RequestedAudit** and terminal **Audit**, with the same host request/task/session, approval and W3C lineage. This optional content-free pair is omitted for historical envelopes, preserving their exact bytes/digests; tables/schema and legacy preferences are unchanged. Committed-authority inspection carries the pair in typed provenance; diagnostic sinks are not fallback authority. Cancellation before commit rolls back both state/audit, while uncertain commit/readback is explicit recovery.

The native presenter invalidates immutable preview/confirmation on selection, refresh, close, privacy or control-generation changes and rejects late output. Current applicability/work status is unknown without evidence. This management path neither dispatches nor physically cancels/rolls back effects; general approval creation, source/resource review, generic voice targeting and immediate pre-effect dispatch qualification remain gated.

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

The [durable slice handoff](Archive/Implementation_Roadmap_2026-10-11.md#r04r05-durable-interaction-and-minimal-session-authority---2026-10-06)
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

### Local read-only conversational clarification - 2026-10-26

The approved outcome is governed model/tool conversation with equivalent native and admitted activated-voice replies, not a new diagnostic utility. An ambiguous observation request selects **Ask locally with observations and one question** in Sessions/compact; the model may call the existing observation tools, ask one bounded question, consume the exact host-accepted answer, call another admitted observation, and return a genuine retained ASSISTANT answer ([native consumer](../src/Kora/SessionsViewModel.QuestionContinuation.cs), [submission](../src/Kora.Application/Hosting/SessionConversationService.Questions.cs)).

The host owns original request/origin, unique task/session/generation, question identity/revision, exact schema and selection bounds, input channels, expiry and live source/privacy/permission checks. Existing native Submit/draft/cancel and exact activated-voice/readback controls remain authoritative; selecting another conversation cannot retarget this wait. No automatic microphone opening or relaxation of OS/provider authentication is added ([question continuation](../src/Kora.Application/Hosting/ConversationQuestionContinuation.cs), [native owner](../src/Kora/NativeQuestionHost.Conversation.cs)).

The implementation is restricted to local Ollama admission and the original six `ReadOnlyCapabilityCatalog` IDs, or an already constrained subset. The native `ask_user` tool is advertised only in the two explicit clarification paths, once per original turn, separately from observation tool batches. It accepts `prompt`, `options`, `minimum` and `maximum`: no options with bounds 1..1 requests bounded text; two-to-four unambiguous labels with bounds 1..1 requests radio buttons; other valid selection bounds request checkboxes. Text is bounded to 500 characters. The session-owned native question window and exact voice reply share the same validation, including multi-answer selection. Spoken readback includes labels/bounds; spoken lists use comma or "and" separators, with whole-label matching taking precedence. A reply is returned as a correlated `ask_user` tool result, never as a fresh conversation or instruction. Repeated/mixed calls are refused. Legacy structured questions and ordinary read-only tools keep their separate paths and wire versions. Web, MCP, session discovery, hosted questions and effectful/setting operations remain excluded ([protocol](../src/Kora.Core/Dependencies/ModelToolProtocol.Questions.cs), [catalogue](../src/Kora.Core/Tools/ReadOnlyCapabilityCatalog.cs)).

The accepted reply stays typed lower-trust user data. It is not approval, grant, source/context permission, final hosted-send consent or tool-input authority. Existing question/decision retention and content-free audit remain separate from USER, HOST and final ASSISTANT retention; no conversation schema or profile format changes. Unqualified/default profiles remain unavailable ([answer message](../src/Kora.Core/Dependencies/ModelQuestionAnswerMessage.cs), [canonical retained execution](../src/Kora.Application/Hosting/SessionConversationService.Execution.cs)).

### Explicit hosted conversational clarification - 2026-10-29

The [approved bounded Claude-only operation](Decision_Register.md#bounded-explicit-claude-clarification---2026-10-29) is separate from the local paths above. In private Sessions or compact view, enter the unchanged original request and select **Ask Claude with one clarification (separate disclosure reviews)**.
The host captures `HostedClarification`, an empty observation catalogue and no source evidence, not an ordinary tool-free request ([submission](../src/Kora.Application/Hosting/SessionConversationService.Claude.cs)).

Only that explicit Claude mode may advertise `ask_user`. The model can return a final answer immediately or ask one isolated bounded text, single-choice or multiple-choice question ([provider guard](../src/Kora.Core/Dependencies/ModelToolProtocol.Questions.cs)).
The existing session-owned question window and eligible exact activated-voice reply share canonical schema/draft/submit/cancel validation. The [existing continuation owner](../src/Kora.Application/Hosting/ConversationQuestionContinuation.cs) rechecks original task/session/generation, question/revision/spec, channel, canonical stored answer, expiry and live authority.

Answering does not transmit or approve anything. The unchanged original request, exact question and canonical answer enter a new Count Tokens body and then Messages body ([native transport](../src/Kora.Windows/Dependencies/ClaudeMessagesRuntime.Tools.cs)).
**Each** physical POST requires a separate complete-final-byte native disclosure and affirmative one-send consent. Earlier consent and voice answers cannot approve either send ([review owner](../src/Kora.Application/Dependencies/HostedTransmissionReviewService.cs)).

The original USER and genuine final ASSISTANT answer retain the existing lineage; question/answer retention and content-free audit stay separate ([host regressions](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.HostedClarification.cs)).
Reviews, reply and transport share the existing two-minute hosted deadline and cumulative 32768-byte input/4096-byte output bounds ([bounded loop](../src/Kora.Application/Dependencies/BoundedModelTurnAdapter.cs)).
Repeated/mixed questions, arbitrary tools, source access, cross-session replies, stale/canceled/expired answers and revoked authority fail closed; actual resources remain owned until joined ([physical regressions](../tests/Kora.Windows.IntegrationTests/HostedClaudeSessionRequestTests.Questions.cs)).

This does not change ordinary local/hosted text, observations or streaming. Copilot/Codex questions remain unsupported ([provider guard](../src/Kora.Core/Dependencies/ModelToolProtocol.Questions.cs)).
No provider/account approval, profile activation, automatic microphone opening, retry or production enablement is introduced; the shipped Claude deployment source stays unavailable ([availability](Runtime_Provider_Feasibility.md#native-claude-messages-profile---2026-10-24), [native controls](../tests/Kora.Windows.IntegrationTests/ClaudeUiContractTests.Questions.cs)).

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

### Explicit original request to the foreground conversation

The approved voice/UI-equivalent conversation operation accepts `ask this conversation <original body>` with the current assistant-name activation for voice. The native [presentation owner](../src/Kora/SessionsPresentationOwner.Foreground.cs) publishes one irreversible snapshot of its unique visible, foreground, eligible Active presenter. Selection alone, names, input/model IDs and cached targets cannot provide that snapshot.

The [host input route](../src/Kora.Application/ViewModels/MainViewModel.cs) captures that snapshot before queued voice dispatch. The [request service](../src/Kora.Application/Hosting/SessionRequestCommandService.cs) freezes the complete body and target for a 60-second one-use review and revalidates original input, exact session/generation/metadata and private control at confirmation and canonical submission fences.

Native **Confirm original request once** and separate `session request confirm` consume the same review without cleansing voice origin ([native decision binding](../src/Kora.Application/ViewModels/MainViewModel.ForegroundRequests.cs)).

This route requests only a local original answer through [SessionConversationService](../src/Kora.Application/Hosting/SessionConversationService.cs), not ordinary scoped-instruction preparation or relatedness routing. It authorizes no prior history, memory or instruction sources. It preserves per-session unsent input, existing unsolicited/new-request behavior and mandatory authentication/hosted physical-send exceptions. Missing local readiness remains a truthful HOST outcome without hosted fallback.

The [native regressions](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.ForegroundRequests.cs) traverse parsing, foreground binding, accessible exact preview, separate confirmation, actual deterministic ModelTurnHost execution and private SQLite answer retention. [Portable regressions](../tests/Kora.Application.UnitTests/Hosting/NewRequestComposerTests.ForegroundRequests.cs) cover missing/stale snapshots, expiry, cancellation and replay denial. These are implementation evidence, not production model, microphone or installed-native qualification.

### Delivered explicit exact-session request commands

Typed input and current-name activated voice can deliberately address one exact Active session using the [strict reserved request grammar](../src/Kora.Core/Commands/SessionCommand.Request.cs). This does not add selected-window, session-title, relatedness, pending-question or approval routing.

```text
session request review <session-id> <generation> <metadata-revision> -- <original body>
session request review <session-id> <generation> <metadata-revision> copilot -- <original body>
session request confirm <session-id> <review-id>
session request cancel <session-id> <review-id>
```

For voice, prefix each command with the current assistant name, for example `Kora, session request review ...`. The body starts immediately after the single separator space in ` -- `; every subsequent character, including case, leading/trailing spaces, tabs and line endings, is original content. [ConversationText](../src/Kora.Core/Conversation/ConversationText.cs) supplies the existing independent bounds and reject-only credential rules.

The [application command workflow](../src/Kora.Application/Hosting/SessionRequestCommandService.cs) returns the exact frozen body, target, provider (`Default` or `Copilot`), original channel, host-issued review ID and 60-second expiry for explicit review. Confirmation consumes that review before asynchronous work. It revalidates original source admission, private ownership/control revision, exact session/generation/metadata and expiry before retention, at its commit fence and before model admission.

A local confirmation of an ActivatedVoice review still submits ActivatedVoice; confirmation cannot cleanse origin or reopen stale admission. Call/privacy/voice recovery, name changes and host closure retire pending review authority through the [host entry points](../src/Kora.Application/ViewModels/MainViewModel.Sessions.cs). Invalid reserved commands are rejected before the question/model fallback; current questions and approvals remain separate decisions.

Ordinary command confirmation and native Sessions/compact/New request Submit call [SessionConversationService.SubmitAsync](../src/Kora.Application/Hosting/SessionConversationService.cs), not parallel adapters. Explicit `copilot` confirmation uses the [reviewed Copilot entry](../src/Kora.Application/Hosting/SessionConversationService.CopilotRequests.cs) into that same private submission/execution core, preserving original channel and causal links. Omission of `copilot` leaves default/local-only behavior unchanged; neither a local failure nor body text can select hosted execution.

The complete original-body envelope is bounded before retention. Copilot requires current exact session provider-profile/account authorization and the existing host admission. Request review/confirmation is **not** final transmission approval: each physical send still requires the separate [native exact disclosure](../src/Kora/CopilotTransmissionWindowController.cs) of destination, account/model, headers and complete final body, with the native **Transmit this exact request once** action. Voice confirmation cannot replace that native action. Missing, denied, changed, expired or cancelled authorization cannot produce a send, invented answer, fallback or automatic retry. A genuine bounded answer is retained on the original USER request; denial/unavailability retains a truthful HOST/task outcome without an assistant answer. Exact running cancellation retains ownership through the existing resource join.

The [SQLite writer](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.Conversation.cs) and [audit readback validator](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.cs) preserve ActivatedVoice on request/task/audit records; draft writes remain native-only. Request IDs and deferred activity links carry correlation without body text in logs, tags or baggage. Model/provider submission remains unavailable because the [registered read-only descriptors](../src/Kora.Core/Tools/ReadOnlyCapabilityCatalog.cs) grant no original-user submission authority.

[Grammar cases](../tests/Kora.Core.UnitTests/Commands/SessionRequestCommandTests.cs), [source/one-shot cases](../tests/Kora.Application.UnitTests/Hosting/NewRequestComposerTests.SessionRequests.cs), [host routing cases](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.SessionRequests.cs) and [private-store restart/audit cases](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteConversationTests.VoiceRequests.cs) exercise this bounded I implementation. No runtime/provider/wake/hardware/acoustic/containment or installed-accessibility enablement/qualification is claimed.

The [hosted request regressions](../tests/Kora.Windows.IntegrationTests/HostedSessionRequestTests.cs) compose the session conversation services with a deterministic runtime seam for separate disclosure, original-request answer retention, denial and joined cancellation. See the [embedded hosted request guide](../docs/hosted-requests.md) for the user interaction. Existing [Copilot runtime readiness](Runtime_Provider_Feasibility.md#maintained-copilot-sdk-boundary---2026-10-23) remains independently required; these changes do not enable or qualify a deployment, account or voice recognizer.

### Delivered bounded native-question increment

The current implementation provides **Answer by voice** for the authoritative
host's exact live foreground native question, plus the deliberately selected
**Hear question, then answer by voice** action. The latter opens one reply only
after successful playback of that same question. Existing consent, listening hold,
unlocked interactive Windows session, ownership/private-control revision,
exact device/readiness, local recognizer and call-policy gates remain required;
unknown state denies capture.

One application-wide turn binds the original question key/revision,
session/generation and unique visible presenter. Its accessible countdown allows
15 seconds to start speech without extending on silence. The existing local
single-utterance recognizer supplies one-second trailing silence and a 60-second
maximum utterance; no PTT button-release substitute is used. Cancel/timeout leave
the native question answerable. Target/foreground/session/control changes,
question edits/expiry/answer, privacy/ownership/call/device loss, failures and
closure synchronously retire admission and fence delayed callbacks. Capture
returns to closed/ordinary explicit PTT readiness, not Wake Listening.

Choice answers match the existing bare schema first, then may use one optional
validated current configured activation-name prefix, separated by whitespace or
a comma, before that same exact label/number/`option N` answer (for example,
`Nova Yes` or `Nova, option 2` when the configured name is Nova). Bare labels,
including ambiguous labels starting with the name, retain their existing meaning;
free-text answers are never stripped or normalized. The question-specific
admission captures the name, and name changes retire playback/capture/delayed
delivery through the same admission fence without retry or rearm.
There is no command, tool, composer or session-routing fallback; the existing
privacy-reducing Stop-listening route remains before schema parsing, even for
an assistant named Stop or a matching option label. Proposal/approval questions
are refused; approval readback specificity and grants are unchanged.
Host-refused replies show an explicit **Reply was not accepted** recovery status,
not ordinary turn completion; the acceptance callback is reserved for an Answered
receipt and the reply trace ends Failed. Recovery directs the user to review native
question status before retry, without retargeting or automatic replay ([reply delivery](../src/Kora.Application/Interaction/HostQuestionVoiceReplyService.cs),
[native recovery regression](../tests/Kora.Windows.IntegrationTests/NativeQuestionWindowContractTests.Voice.cs)).
No audio or raw-transcript archive, transcript/content logging, model/network
effects, wake integration or during-TTS capture is added. Accepted structured
answers retain the existing question persistence/audit policy. Maintained
deterministic production-path tests validate this implementation independently
of later hardware/acoustic/installed qualification.
Host-resolved traces preserve same-request presentation parentage and link
deferred reply/recovery causes; incoming correlation never selects the question
or session and carries no transcript content.

### Opt-in exact-playback conversational replies

The device-local **Conversational voice replies** preference starts Off ([shared preference](../src/Kora.Application/Configuration/ConversationalReplyConfigurationService.cs)).
Enabling it in **Settings > Speech & audio** permits the first foreground presentation of a new exact host clarification to speak that question and then open the same one-shot reply, without another click or activation name ([native routing](../src/Kora/NativeQuestionViewModel.cs)).

Only canonical host clarification/local-version questions and host-validated model clarifications qualify. Approvals, egress/consent questions and arbitrary source purposes do not. Successful completion of the exact playback is required before capture; a visual-only card never directly starts transcription ([reply admission](../src/Kora.Application/Interaction/HostQuestionVoiceReplyService.cs)).

The preference revision joins the existing exact question/session/presenter, microphone, ownership, privacy and call fences. Replacement, cancellation, timeout, failure, editing or foreground loss retires that attempt ([portable regressions](../tests/Kora.Application.UnitTests/Interaction/HostQuestionVoiceReplyServiceTests.Conversational.cs)).
Re-activation, preference changes and late callbacks cannot retry or rearm it; native answers and the two explicit voice actions remain available under their existing gates ([native regressions](../tests/Kora.Windows.IntegrationTests/NativeQuestionWindowContractTests.Conversational.cs)).

The existing 15-second start window and 60-second utterance limit, accessible status and **Cancel reply** apply ([voice lifecycle](../src/Kora.Application/Interaction/HostQuestionVoiceReplyService.cs)).
This saves only an audited On/Off preference, not audio or transcripts; an invalid/unconfirmed preference holds automatic replies unavailable ([preference storage](../src/Kora.Application/Configuration/LocalConversationalReplyPreferences.cs)).
It grants no microphone consent, ambient wake, provider readiness, approval or egress authority. Exact-profile installed/acoustic/hardware enablement and qualification remain separate ([delivery policy](Implementation_Roadmap.md#delivery-and-qualification-dependency-policy)).

### Broader target behavior

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

The [2026-10-25 adaptive-window increment](UI_Workspace_And_Windows.md#adaptive-primary-interaction-window---2026-10-25)
combines the primary compact response and bounded session browser into two modes of
one native window. The legacy management workspace remains a supporting surface. The
[integrated content-pane direction](UI_Workspace_And_Windows.md#integrated-session-content-pane---2026-10-25)
now places bounded original/assistant, activity, current-response and session
log/audit details beside the persistent conversation/composer. Exact links, native
brief-answer offers and explicit detail commands do not rerun a model or grant
authority. Standalone documentation and specialized preview windows remain.
Automatic relatedness and model-visible list context
remain proposed; deterministic show/select navigation does not imply their delivery.

The delivered [compact selected-session projection](UI_Workspace_And_Windows.md#delivered-compact-selected-session-projection) shares one authoritative presentation selection and memory draft with the Sessions workspace.
Explicit reading and canonical draft/submission actions remain separate from passive navigation, New request and standalone question/approval/voice authority.

1. **Compact session interaction:** evolve the response window into the latest interaction for the selected session, with its name, actual work state, concise answer/progress, structured question or approval, Details/History, and typed composer.
2. **Sessions workspace:** reachable from tray, keyboard, compact UI, and verbal commands; Active/Done list and search beside the selected session's full conversation/history, pending cards, and work/queue. A bounded Evidence mode searches and correlates permitted session, diagnostic and audit records without making diagnostics part of the conversation. Shows unread/attention, meaningful activity/due dates, and actual work state, with rename/Done/resume/delete and All work/evidence views.
3. **Integrated Details pane:** expands immutable Markdown, admitted static HTML, plain text, diffs, `.ps1` source, task evidence and admitted session-command log/audit results inside the primary expanded window. Conversation/messages and composer remain visible beside a resizable Details pane, with minimum pane/window widths, explicit Show/Hide and Show disabled without available detail references. Each conversation entry can open its own exact result in that pane; there is no single session-wide latest-result target. No detachable content window is added in the first increment. Items retain exact session/artifact identity, and switching sessions hides unrelated content rather than retargeting it. Evidence authority/retention, Mermaid and isolated browsing remain separately governed; viewing is not execution.

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

The proposed [Session Permission Levels](Session_Permission_Levels.md) contract adds grant-first Review all / Model review / Approve all to each admitted action route. The risk treatment below describes review when required, not an unconditional prompt under every level. Mandatory prohibited-effect, consent, OS/provider and execution boundaries always apply.

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
In Model review, a valid separate tools-disabled reviewer Safe verdict may satisfy ordinary action authority for that exact invocation; it cannot lower host risk, prove arbitrary code safe, create a grant, or supply independent consent. Approve all skips discretionary analysis, not mandatory boundaries.
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

Show the session level and spoken-approval eligibility separately. Calls default to UI approval without ignoring grants or changing the level. Changes need exact user confirmation; Active restart preserves the level, Done resets it, and resume restores no old level. [Lifecycle/race rules](Session_Permission_Levels.md#session-ux-lifecycle-and-races) bind pending decisions to policy revision.

## Session Identity, State, and Contents

A Kora session is a durable user work stream containing conversation, tasks, and evidence, not an execution slot or provider memory.
Its immutable ID survives renaming, selection, archive, and explicit resume.
Lifecycle is **Active** or **Done**; work state is separate: idle, queued, running, waiting for user/approval, blocked, failed, cancelled, interrupted, or outcome unknown.
Done means archived, not "every action succeeded". Deleted is irreversible removal, not another browsable status.
A terminal task result does not automatically mark the whole session Done.

The [bounded reviewed-memory store](Model_Providers_Memory_And_Knowledge.md#delivered-session-only-durable-memory-storage---2026-10-09) retains only exact admitted session-owned candidates. Authoritative Done/retirement, disposition and retention invalidate their bodies/receipts with typed audit in the existing transaction and retain non-reusable tombstones. Restart reads data, not review/use or egress authority; broader scope storage remains unavailable.

The [native selected-session memory filters](Model_Providers_Memory_And_Knowledge.md#delivered-native-content-free-session-memory-filters---2026-10-14) combine exact memory GUID, optional exact scope identity GUID, Scope kind, Review, Retention and inclusive revision/created-time ranges with AND over the explicit bounded List snapshot. Scope identity uses complete canonical D GUID admission; empty means Any. Scope kind and identity are snapshot metadata, not permission, ownership, scope authority or a session/provider selector. Any is initially selected; defined non-session kinds truthfully yield zero rows, never cross-session reads. Every row's full metadata is validated before exclusion. Filter edits revoke selection, inspection, draft, mutation readiness and pending presentation under the existing source/session/control fences, with no store read or activity renewal. Explicit List refresh and separate fresh inspection remain necessary.

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
The bounded first native composer is an explicit reject-only exception:
detected credential-like material is refused before draft/message persistence
or admission, with user correction and secure-flow guidance, never silent
redaction or a Save anyway bypass. Accepted text stays exact; this does not
weaken the prohibition on credentials/tokens in retained history or qualify
comprehensive detection.
Source/account restrictions can require removal or prevent retention; record an explicit content-unavailable reason rather than promising an unrestricted copy.
Diagnostics remain content-minimising and separate from intentional session history.

## New Requests and Routing

### Delivered native New request fresh-session fallback — 2026-10-13

**Tray > New request (fresh session only)** and **Sessions > New request (fresh target)** activate one native memory-only composer. Relatedness inference remains unavailable: this bounded route offers only an explicit fresh **New conversation**, never the selected, named, recent or Done session.
The generic host label is not inferred from the candidate body, model or background content.

Typed **open new request**, optionally addressed to the current assistant name, and activated voice **Kora, open new request** (using the configured name) navigate to this singleton. The [parser](../src/Kora.Core/Commands/NewRequestOpenCommand.cs) accepts no body, target or trailing argument; malformed reserved input never becomes a model request or question answer.

The [navigation admission](../src/Kora.Application/ViewModels/MainViewModel.NewRequest.cs) preserves pending questions/approvals and refuses opening until resolved. Input generation, privacy, ownership, name revision and call activation restrictions fence asynchronous dispatch. Conflicting names (such as **New Request**) require explicit renaming; ambiguous aliases are not dispatched.

The [native controller](../src/Kora/NewRequestWindowController.cs) shares tray/workspace singleton activation and fences source/private/window closure and disposal before late opening or focus. Opening preserves unsent, reviewed, held and busy memory without reset, resume, target creation, persistence, activity renewal, queue, provider or approval effects.

Create and Submit retain their own native gesture and admission; navigation conveys neither authority and adds no saved-draft operation ([controller regression tests](../tests/Kora.Windows.IntegrationTests/NewRequestControllerTests.Commands.cs)).

The [portable tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.NewRequest.cs) cover bounded navigation only. Model-facing navigation is unavailable: the event is not a registered model capability; the six read-only tools and 25-action bootstrap are unchanged. Hardware/acoustic/provider/installed accessibility qualification is not claimed.

**Create new empty target** owns one exact original native creation attempt and commits through the audited workspace creation seam. Acknowledge the actual ID/name/generation/metadata revision before separate **Review frozen body** and **Submit reviewed exact body**.
Creation and body retention are separate authority operations, not atomic or rollback-capable; cancellation after creation leaves the disclosed empty session Active.

Submission reuses `SessionConversationService`: reject-only credential screening before any body write, exact immutable USER text, retained original task/request and separate truthful HOST outcome through configured provider-neutral admission. Current production generation stays unavailable.
No model answer, automatic routing, context assembly, adapter/bootstrap fallback, network/auth/setup or legacy typed/voice expansion is delivered.

Edits, including same-byte reassignment, cancel and control/private/source changes retire submission review. The original attempt and epoch fence physical COMMIT. **Open this exact session** uses explicit navigation that preserves other topics' memory-only input, never transfers the New request body ([composer](../src/Kora.Application/Hosting/NewRequestComposer.cs)).

The New request composer retains unsent text only in memory; nonempty input is dirty until explicit submission or discard. The native close/application-exit workflow offers Discard/Cancel, not Save-as-document. Topic navigation preserves other sessions' unsent buffers without a decision or persistence; it does not transfer this isolated New request body. Existing saved private text is available only through exact-session read-only compatibility recovery; no catalogue, new save, schema migration or purge is introduced ([composer](../src/Kora.Application/Hosting/NewRequestComposer.cs), [close workflow](../src/Kora/NewRequestWindow.axaml.cs)).

Unknown creation, retention, terminal or receipt outcomes hold the exact attempt without a second target or submission retry. **Inspect exact attempt** reads its retained target/task under current private/retention authority, changes no meaningful activity and grants no submission authority. A changed observed target remains held. Known or possible partial success stays disclosed; no automatic Done/delete is claimed.

Private SQLite and headless native tests are implementation evidence, not installed accessibility, hardware, provider/inference, profile-original, physical-storage or full R12/R14 qualification. Those gates and the storage/profile deferrals remain unchanged.

### Explicit literal New request dictation

The existing native New request memory buffer has an explicit hold gesture,
not a command or navigation route. [ComposerDictationService](../src/Kora.Application/Voice/ComposerDictationService.cs)
owns the cohesive volatile capture/review lifetime. View models expose state
and delegate; the native owner supplies an exact, user-armed generation,
observed through lifecycle events rather than focused-window inference.
There is no latest-target fallback or shared global dictation destination.

The smallest consumed I subset is the existing consented PTT admission,
activated capture generation/acknowledgement/quiescence seam, private workspace
control revision, and NewRequestComposer's revisioned memory publication.
Input/call/recovery/topology/ownership epochs and exact native/buffer identity
are checked before capture, acknowledgement, result acceptance, release,
dispatch and publication. Edits, Cancel, native-arm withdrawal, target rename,
privacy or source lifecycle revocation invalidate late work. Publication rolls
back after synchronous binding revocation without overwriting newer edits.
Close, Stop, cancellation and disposal retain and await native release receipts;
an unconfirmed release remains a sticky unavailable state.

One admitted utterance is literal private memory, never routed to commands,
models, questions, approvals or tools. Even reserved stop/approval words remain
inert; native Cancel and Disable listening still revoke capture. Before an
empty-buffer insert, native release must be confirmed. Every nonempty buffer,
not just one differing from its saved baseline, requires complete old/literal
review and explicit Append/Replace/Cancel. Append concatenates exact bytes with
no inferred separator. No Create, Save, Submit, attachment, activity renewal
or execution is implicit; separate ordinary retention screening is unchanged.
The existing New request window has one buffer; no expanded editor or
navigation behavior is added by dictation.

`IActivatedVoiceRecognitionService.IsLiteralDictationAvailable` defaults false.
Production Windows capture retains this explicit unqualified state: installed
recognizer presence does not enable literal dictation. The gesture cannot
enable listening, grant consent, select devices or perform provider setup.
Deterministic seam fakes qualify only their test instances and cover the full
path without real hardware/provider/experiment execution. E/Q, installed,
hardware, acoustic and provider evidence remains separate and unclaimed;
portable Release checks and raw 100% line/branch coverage remain merge gates.

### Explicit literal existing-session dictation

The same capture/review owner also accepts a host-captured exact Active session
and its revisioned, memory-only unsent buffer. A dedicated native dictation
window is opened from Sessions, compact conversation or the expanded main
conversation without adding a command route. Selection/source/control/buffer
changes retire the target, preserve independent session input and suppress late
publication; synchronous publication revocation rolls back only the owned
insertion. Actual capture release precedes Append/Replace/Cancel review, and
ordinary Submit remains separate. See the [session dictation guide](../docs/session-dictation.md).
Production literal capture remains explicitly unqualified and unavailable;
deterministic platform fakes do not establish speech-profile qualification.

#### Bounded provisional literal feedback

During an explicitly admitted LOCAL existing-session dictation hold, optional
[hypothesis observation](../src/Kora.Core/Voice/IProvisionalVoiceRecognitionService.cs)
uses the actual acknowledged capture generation and exact native target. The
[Windows wrapper](../src/Kora.Windows/Audio/WindowsActivatedCapture.cs) observes
`System.Speech` hypotheses without expanding microphone lifetime. The shared
host forwards only activated observations; wake, command dispatch, approvals,
question replies and New request dictation gain no provisional presentation
([feedback owner](../src/Kora.Application/Voice/ComposerDictationService.Provisional.cs)).

Complete replace-only snapshots are bounded to 512 UTF-16 characters and
1,024 strict UTF-8 bytes. Invalid/oversized input visibly retires preview, not
final-only dictation; older/duplicate/foreign-generation observations cannot
replace current feedback. One pending UI callback holds no captured text or
target. Edit/selection/source/private/control/device/call/lock/cancellation,
expiry, final completion and disposal clear feedback and fence queued work.
No partial input is inserted, dispatched, retained, copied/exported, spoken,
logged or used for activity renewal or source/approval authority
([validation](../src/Kora.Core/Voice/ProvisionalTranscriptSnapshot.cs),
[native surface](../src/Kora/SessionDictation/SessionDictationWindow.cs)).

Native retirement joins actual recorder, recognizer and stream release. A
delayed privacy cleanup with no remaining resources must not enqueue new
release work or revoke the completed stop receipt; outstanding or failed
native work still prevents quiescence. Deterministic lock/device regressions
control that ordering without retries or timing sleeps
([release regressions](../tests/Kora.Windows.IntegrationTests/Audio/ActivatedVoiceRecognitionTests.Quiescence.cs)).

The native read-only `Provisional—may change` area has a stable content-free
automation label and no live region or value/text editing provider; accessible
final review remains separate. Only the accepted final transcript enters the
existing original-input memory publication and subsequent explicitly admitted
Submit. Deterministic tests cover that complete path through a retained real
test-model answer, not a disconnected field preview; production literal/profile
qualification remains unavailable and no hardware/acoustic trial is claimed
([native regression](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.ProvisionalDictation.cs)).

### Explicit retained original USER text reuse — 2026-10-19

An actual currently admitted immutable conversation row in Sessions or compact conversation offers **Review reuse of this original USER text in a New request**. This is content reuse, **never task replay/resumption** or promotion of HOST/model output. Reconstructed/value-equal rows, hidden/private/omitted/revoked records, question answers and generated content are not reuse sources.

The [Application review](../src/Kora.Application/Hosting/OriginalMessageReuseReview.cs) resolves the complete original through `SessionWorkspaceService.ReadConversationDetailAsync`, including its exact retained record revision and source session/generation/lifecycle/metadata checks. Full `ConversationText` validation and reject-only credential screening apply; no trimming, normalization, truncation or save-anyway repair occurs. HOST outcome/reason remains separate historical status, not copied body content.

The [private native review](../src/Kora/OriginalMessageReuseWindow.cs) shows the **entire exact original USER body**, original session/request/task IDs, sequence, record revision and submission time as historical data. It reuses the inert source/transfer-blocking comparison pane; no clipboard authority is added. It also shows the complete existing isolated New request memory buffer. A dirty buffer requires an explicit **Replace reviewed existing New request memory** choice; **Keep existing memory / cancel reuse** never overwrites it.

Only separate confirmation opens/activates the existing single New request path and copies the exact text into **memory only**. An already created/held/busy/unavailable New request cannot receive reuse; close it through its ordinary decisions before explicitly starting another review. No session/target creation, persistence, Save, Submit, task enqueue, approval, activity heartbeat or provider operation occurs during review or copying. Fresh target creation and separately reviewed Submit retain ordinary screening and new host-owned identities; historical IDs confer no authority.

The source is authoritatively reread after native opening/activation and immediately before seeding. Source/private/control/generation/lifecycle/selection/search edits, target buffer edits (including same-byte reassignment), cancellation, closing or reentrant opening retire the whole proposal; repeated confirmation and late callbacks cannot revive it. Native owner visibility is observed on the UI thread and published as a thread-safe one-way admission fence for storage continuations: hiding and showing the owner cannot revive a withdrawn proposal. Source revocation or cancellation-token cancellation during synchronous binding handoff cancels and erases the uncompleted seed, preserving newer independent user edits. After a completed explicit handoff the new memory is independent user-confirmed content, not a live source reference or replay grant.

[Portable cases](../tests/Kora.Application.UnitTests/Hosting/NewRequestComposerTests.MessageReuse.cs) and [native/private-store cases](../tests/Kora.Windows.IntegrationTests/NewRequestControllerTests.MessageReuse.cs) cover the original-record reader, private admission, New request composer and inert-pane path. This adds no runtime/model/provider/egress/wake/profile-original writer authority or E/Q qualification.

### Proposed relatedness routing

Explicit non-destructive readable-name browsing is separate from request
relatedness. `open conversation "<name>"` / `read conversation "<name>"`
uses the existing exact-name metadata search, one bounded currently permitted
page, no preselected row and a separate native exact-choice acknowledgment.
Original LocalUi/ActivatedVoice admission, private control revision, source
metadata/generation/lifecycle and a 60-second lifetime are revalidated before
publishing real retained conversation history in the existing interaction
window. No model, original-request submission, task creation, lifecycle change
or disk unsent-input write is part of browsing. Pending question/review/dictation
input cannot become this global command. The source-owned native chooser
retires on source change, cancellation, replacement, privacy or disposal.

The bounded [reviewed destination operation](../docs/session-routing.md) now
implements host-owned descriptor proposals, exact choice plus New, name
acknowledgment, pre-submit correction and canonical original-user submission in
both window modes. It reads one permitted metadata page through
`SessionWorkspaceService`, not a model-tool catalogue or transcript assembly.
Production composition now includes one tools-none, LocalOnly inference in a
routing-specific canonical original-input control domain established before the
metadata snapshot. The control identity is excluded from routing destinations.
Strict bounded JSON maps only host-presented indexes/evidence into the existing
proposal; the shared routing service validates candidate cardinality, evidence and
revision. Inference resources physically join and canonical control outcomes commit
before review is returned. Unqualified local providers remain unavailable and
explicitly offer reviewed New, with deterministic selection available.
The native review reveals the destination
without switching the previous reader or moving any unsent input. Hands-free
routing replies and automatic selection changes remain unavailable. After separate
confirmation, unchanged canonical conversation submission sends the original body
and LocalUi/ActivatedVoice channel in a new request. Only its genuine final answer,
not the routing proposal, is retained. Cancellation and source closure suppress late
review and join the owned inference without fallback submission.

The [editor-targeting precedence](#editor-targeting-precedence) below
applies before this general relatedness policy. Editor-bound or ambiguously
editor-intended speech is not an unmatched general request.

Explicit targeting wins: input in a session composer continues that session; "new session" starts one; naming a session targets it.
The same policy applies in collapsed and expanded modes. Explicit selection establishes
the current conversational reference, not a routing lock: "Select the third session"
followed by "Make that summary shorter" is evidence of relatedness, whereas a clearly
unrelated general voice request must still consider another Active session or New.
Dictation explicitly directed into a named session composer is addressed input.
Window size, passive focus restoration and background events are not explicit targeting.
An untargeted general voice/global-composer request starts a new session unless clearly related to an Active session.
Model-assisted matching uses bounded, permitted session descriptors/summaries and the current request, not every transcript or tool result.
Return a typed route proposal with candidate IDs, evidence references, and expected registry revision.
The host validates lifecycle, identity, permissions, and revision before admission.

- One clear active match: continue it and acknowledge the session name, with an immediate correction route.
- Several plausible matches: ask a targeted session-choice question, including New session.
- No clear match or unavailable routing inference: create a new session; offer deterministic session selection rather than guess a relationship.
- Archived match: offer discovery/history; only an explicit resume decision reactivates it.

An accepted user-requested route acknowledges its session name and provides a correction
route. In expanded mode it reveals that destination without transferring unsent input
or losing the previous reading position. Background activity never switches selection.
Spoken ordinal navigation uses the exact host-held presented-list snapshot, not a model's
reconstructed order; the bounded implementation counts the loaded page, including Last.
Session descriptors made available to routing inference must be bounded, permitted
and bound to that same presentation; navigation does not authorize transcript assembly
or hosted disclosure.

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

### Editor targeting precedence

**Bounded activated routing with native clarification/review.** A foreground Customizations editor
may supply one activation-time candidate for model-interpreted editing or
discussion intent, without a compulsory editor preface. An editor merely being
open, or a session being selected, is insufficient. The
[definition-management contract](Definition_Management.md#foreground-editor-voice-targeting)
owns the complete snapshot, disclosure, choice and draft-review interaction.

Existing dedicated question/approval/dictation capture keeps exclusive input
ownership and no command fallback. Outside such capture, explicit admitted
editor/session/New request/local commands precede inferred foreground intent.
The host pins the candidate at activation, not final recognition or queued UI
delivery. Routing inference sees only reviewed minimal metadata, not session
transcripts or definition bodies; its typed proposal cannot confer authority.

An ambiguous editor reference or inferred destination change requires an exact
target-choice question before any session submission/creation or authoring
disclosure. A failed, unavailable, stale, cancelled or rejected editor route
must not use this section's ordinary "no clear match" New session fallback,
answer an unrelated pending question or rerun the utterance through the global
parser. A choice authorizes only routing through the selected destination's
ordinary admission, preserving original voice origin and current eligibility.
Delivered clarification selects refinement/discussion of the captured artifact
or Cancel. Selecting another artifact/session/New request requires cancellation
and its explicit existing route, not automatic held-utterance handoff or a
model-selected session. General relatedness routing above remains proposed.
Truly unrelated input without editor context/references retains ordinary
routing. Background tasks remain isolated and cannot acquire editor input.

## Concurrent Work and Focus

Independent Active sessions can execute concurrently, including writes to different resources.
Initial proposed default: two executing session tasks, with a configurable limit admitted only within verified provider/hardware capabilities.
For ordinary retained conversations, the [delivered future-maximum operation](Work_Management.md#ordinary-session-resource-allocation-implementation) reviews and separately confirms desired demand of one or two through native Settings or exact original typed/activated commands.
This is not qualification of capacity two. Each request's host resource status reports its last configured/captured/qualified/effective ceiling and actual admission blocker; unknown readiness remains unavailable.
Lowering the preference leaves active work and its physical joins intact, and never changes per-session pending FIFO.
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

The bounded native **Stop this conversation's running work, then delete this conversation** operation combines existing actions without claiming atomic rollback.
A single-use exact-source review discloses the target, unsent/pending input, owned running work and existing disposition inventory.
Separate native confirmation closes only new conversation dispatch for that exact target, requests its host-owned cancellation, joins actual resources and revalidates the reviewed source before the existing audited disposition transaction.
Opening or declining review has no stop/discard/delete effect ([operation](../src/Kora.Application/Hosting/SessionConversationService.StopAndDelete.cs), [native presentation](../src/Kora/SessionsViewModel.StopAndDelete.cs), [transaction](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.Disposition.cs)).

Only the exact reviewed cancellation's expected terminal changes are permitted; other work, metadata, generation, permission/source or control changes invalidate deletion.
The original dispatched task may remain truthfully Unknown: physical join is separate host-owned evidence, never inferred from that status or a global all-clear. Unknown/failed join forbids deletion.
The conversation executor confirms the original terminal readback before releasing its physical-completion lease. Submission returns that confirmed receipt without rereading the original after release, when the reviewed disposition may already have removed it.
A completed stop with no deletion requires fresh review; uncertain commit/readback requires authoritative inspection, never replay.
A fresh review can consume that same in-memory host-confirmed stopped-work identity without repeating cancellation; no content or new durable authority is stored in that recovery proof
([phase plan](../src/Kora.Application/Hosting/SessionStopAndDeleteReview.cs), [source fence](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.Disposition.cs)).

Typed exact-target review uses the same native action. Original voice-origin compound deletion remains unavailable under the existing native deletion boundary and is rejected before any stop; a later click cannot relabel it.
No model/tool mutation grant, new storage schema, secure physical erasure or remote deletion is added.
Existing independent retention domains and copy exclusions remain unchanged ([command](../src/Kora.Core/Commands/SessionDispositionReviewCommand.cs), [retention domains](Security_Data_Flows.md#retention-defaults)).

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

The [roadmap](Implementation_Roadmap.md#ordered-outstanding-work) owns remaining
delivery/dependencies; [decisions](Decision_Register.md) own unresolved choices.
Do not treat an earlier foundation's missing consumer as a current backlog item.
General scripts, browser isolation, biometrics and provider concurrency retain
their dedicated gates. [Acceptance](Acceptance_Criteria.md) must still exercise
eligible voice/UI/mixed channels, concurrent sessions, restart, expiry/deletion
and hostile history; implementation receipts do not substitute for that evidence.
