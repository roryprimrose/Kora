# Human Interaction and Persistent Sessions

Status: agreed product direction; bounded R05 core/application question and
authorization foundation implemented, full interaction/session integration proposed.

Related: [Architecture](Architecture.md), [Work Management](Work_Management.md), [Information Display](Information_Display.md), [Security](Security_Data_Flows.md), [User Configuration](User_Configuration.md), [Acceptance Criteria](Acceptance_Criteria.md).

This is the canonical interaction/session contract. It replaces the former single-conversation, single-executor, memory-only-history direction.
Windows login sessions, voice capture generations, provider/SDK conversations, and Kora work sessions are different identities; none substitutes for another.

## Current Implementation and Design Gap

The runnable bootstrap has one response title/body and latest transcript in `MainViewModel`, displayed by `ResponseWindow`.
Typed and recognized spoken commands converge on the deterministic built-in command router.
The updated bootstrap also handles unmatched requests with a verified local model, including clarifying questions and host-validated registered action/grant proposals with once/session/always choices.
These named model-action preferences are not the future executable/script digest-bound grants; direct exact lock is still ungated while model-suggested lock asks for approval.
The compact surface has a text field, Run, and Dismiss, with positioning and auto-hide preferences.
The separate Documentation window renders trusted embedded documentation, not arbitrary session artifacts.

The uncomposed R05 [question service](../src/Kora.Application/Interaction/HostQuestionService.cs)
now implements bounded single/multiple-choice and text questions, explicit
draft/submit/cancel, host owner/revision checks and expiry. Its
[authorization service](../src/Kora.Application/Interaction/HostAuthorizationService.cs)
uses exact host-resolved proposals and a separate atomic storage/audit seam;
it does not use legacy action-name preferences.
There is no production durable question/grant adapter, native shared question
presenter, typed form service, session selection/history, concurrent task
scheduler, or model-facing session tool API.
Existing proposals for native questions, rich details, a work ledger, and scoped grants are foundations, not proof those capabilities already exist.

### Bounded R05 Foundation Boundary

Questions, answers and drafts use existing host session/request/task/question
IDs and a positive revision. Every accepted draft edit advances that revision;
submission of an earlier draft conflicts instead of overwriting newer input.
The service accepts explicitly addressed UI or activated-voice replies against
the same key. It does not implement generic spoken-reply focus or a speech/UI
parser/presenter; trusted channel acquisition, focus targeting and native
readback remain integration gates.

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
Tests exercise this seam with a test-only
serialized adapter, not production SQLite persistence. Done/delete/resume
must advance the session authorization generation; restart of an Active
persisted session preserves it. Pending questions and Once/Session grants
cannot regain eligibility after resume. Perpetual records are independent
and have no expiry/retention/eviction field.

The verified R04 composition handoff, agreed durable adapter/schema ownership,
native trusted review/input and immediate pre-effect dispatch revalidation
are still required. This foundation does not alter bootstrap dispatch,
microphone capture, direct lock, power operations, execution or UI composition.

## Interaction Principle

Every supported Kora workflow has equivalent verbal and mouse/keyboard-accessible operations.
Users can interact entirely by voice, entirely through UI, or change channels at any step without restarting the workflow.
Voice-first means convenient hands-free operation, not reduced UI capability or compulsory speech.
Input and output choices are independent: typing does not require speech output; visual-only output does not mute input.

Questions, answers, summaries, detail navigation, approvals, session management, and settings all use the same host services and validation.
No UI action requires a spoken acknowledgment, and no ordinary Kora approval requires a click merely because it is high risk.
The physical limits of a closed/unavailable microphone and mandatory OS/provider authentication, credential entry, and secure-desktop prompts remain explicit exceptions.
The [in-call settings origin gate](Call_Aware_Speech.md#in-call-settings-origin-gate) is also explicit: during protected calls, voice-initiated voice-setting and in-call-option changes are rejected and require a new UI request; operation approvals, read-only inspection, and deterministic safety controls follow their independent policy.
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
