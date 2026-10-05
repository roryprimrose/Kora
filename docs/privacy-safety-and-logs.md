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

## Visual safety fallback

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

## Local preferences

Preferences are stored under:

`%LOCALAPPDATA%\Kora\Preferences`

They are device-local and do not roam with documentation or future skills.
