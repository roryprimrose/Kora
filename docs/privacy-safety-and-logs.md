# Privacy, safety, and logs

## Local speech

Explicitly activated command recognition uses the installed Windows recognizer.
Ambient audio never enters its command transcription pipeline; production wake
is unavailable, not the old ambient grammar. The built-in text-to-speech provider uses installed
Windows SAPI voices. Kora does not need a cloud account or network connection
for these features.

## Local-model actions

After model setup is approved and inference verified, Kora sends the current
unmatched request, the built-in action names/descriptions, and a limited
snapshot of dependency readiness, task state/progress, listening state, and
pending power-proposal status to the pinned Ollama model on `127.0.0.1`. It
does not include earlier conversations, logs, files, clipboard contents,
device identifiers, or account data. For a clarification, Kora sends the
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
permission/device loss and capture failure invalidate generations, clear audio,
stop output and require explicit recovery. Unlock/resume/hot-plug cannot
silently reopen it. Native/tray controls require no model, network or speech.

External session/power/endpoint observation is implemented, with a one-second
permission polling fallback. Negative session notifications invalidate capture
and output before slower requery; Unknown or failed observation grants no input
authority. Release during a pending PTT open, shutdown and disposal retire the
activation, including already queued transcripts and cancellation-ignoring late
opens. Restored readiness still requires explicit recovery, not buffered replay.
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

The tray's **Review local version (native question)** uses trusted native
input over the durable host question service, separately from legacy
model-action approvals. Answers retain the exact original question/session
and request origin; window focus cannot choose their target. Review, saved
drafts, submitted answers, exact approval and grant use are distinct.
This entry opens no microphone and calls no model or effect handler.
Unknown privacy/ownership, stale revisions, expiry and audit failures deny
the affected interaction. Native record review cannot supply missing script
bytes, containment or deployment authority, and no action-name grants are
migrated into exact grants. General effect dispatch remains gated.

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

The current host writes both ordinary diagnostics and typed audit events
to those files and independent private-profile SQLite projections. Required
capture/file/database failures are reported and fail admission explicitly,
not silently swallowed. The bounded durable path currently covers exact
typed or activated-voice **version** commands: task intent, dispatch, terminal
evidence and receipt are committed before completion. Startup marks intent-only
work Interrupted and dispatched work without a verified receipt Unknown;
it never automatically reruns either. The terminal version receipt is not
proof of an operating-system effect or speech-playback completion.
Private task, interaction and evidence journals are retained under the same
verified profile boundary. Valid interrupted transactions reopen atomically;
committed approvals/use counts/session generations are not replayed.
Missing journals, corrupt/unsupported data, permissive permissions or unavailable
ownership/access stop admission explicitly, without file replacement or ACL
repair. Process-interruption tests do not guarantee physical power-loss recovery.
There is still no durable conversation/history UI or general task executor.
The tray's **Evidence (read-only)** opens a native inspector for actual
SQLite diagnostic, typed audit, completed span and explicit link records.
Choose a source, optionally enter safe text or session/task/trace correlation
filters, then select **Search / refresh**. **Next page** continues that exact
snapshot; later records do not silently extend it. Each page includes stable
`kora-evidence` citations and at most 50 records / 64 KiB of serialized output,
including metadata. A record too large for one page is explicitly marked
`ContentOmitted`; content is never silently truncated.
Select a record to inspect parent/link status; **Read selected trace** shows
its retained correlated records and **Open selected segment** follows an
available cited span. Session/task IDs and traces are filters, not permission.
`ExpiredButPresent` means a due record remains readable, not that it was
deleted. `MissingOrRemoved` cannot distinguish an unrecorded segment from
physical removal. Session/conversation sources report Unavailable. This view
is not a complete history, interaction audit, authorization or effect receipt.
Ownership/privacy denial, malformed filters/cursors, corrupt data and private
access failures are visible; no store or permission repair is attempted.
Closing or privacy closure clears/cancels the view without changing retained
sources. There is no copy, export, model reasoning, browser, deletion, grant
use or remote transmission from this inspector.
Database records receive independent 30-day diagnostic and 90-day audit
due dates, but automatic database pruning/deletion is not yet implemented.
The first-use greeting, settings and version response disclose this limitation;
the daily-file 30-day/30-file retention remains active.
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

Diagnostic database retention defaults to 30 days under its independent
configurable setting. Audit database retention defaults to 90 days and can be
set from 30 through 365 days. Audit records are content-minimising and remain
independent of session deletion; they do not retain deleted chat or argument
content. Audit expiry does not delete perpetual grants. Reading, searching,
exporting or asking questions over evidence never extends its retention.
Reducing audit retention previews the affected range and requires confirmation
before existing due dates are shortened.

The delivered bounded native inspector lists, reads and searches actual
SQLite Logs, Audit, spans and links without using a model. A future explicit
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
