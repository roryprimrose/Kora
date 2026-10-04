# Privacy, safety, and logs

## Local speech

Built-in speech recognition uses the installed Windows recognizer and a
host-owned fixed grammar. The built-in text-to-speech provider uses installed
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
commands, file paths, or model-generated code. Rejecting, dismissing, or
making a new request discards the pending suggestion. The exact built-in
voice and typed commands remain deterministic and take precedence over the
model.

Future skill tasks that launch applications or scripts will require
content-bound execution grants, **not** the current built-in-action grants.
Changing an approved executable or script's SHA-256 digest invalidates every
dependent task's grant, even an always grant; missing or unverifiable files
also block execution. Kora must check hashes again immediately before each
launch and execute only the verified bytes. This also applies to a standalone
application the user asks Kora to launch: an updated binary at the same path
requires a new approval. See the
[skill and task execution design](skill-and-task-execution-design.md) for the
planned approval, skill-management, and task-runner requirements. Stored
skill scripts and arbitrary application launches are **not implemented yet**.
The proposed built-in task architecture is `.ps1`-first for side-effecting
work, with read-only commands and Kora's trust boundary remaining in C#.
Built-in scripts would receive the same content-bound approvals as skill
scripts, and a separate read-only, syntax-highlighted review window would
show the exact PowerShell before approval on request. This is not how the
current built-in commands execute.
PowerShell 7 (`pwsh.exe`) is now a required readiness task; Kora probes a
known installation location with a no-profile version command. Installing
the `Microsoft.PowerShell` winget package requires separate on-screen
consent, and successful installation is verified. This setup does not
authorize or execute any skill or built-in `.ps1`.

The approval request always appears on screen, including in VoiceOnly mode.
When speech is available, Kora pauses microphone capture while speaking the
question, then resumes listening for an answer. By default, spoken approvals
must start with the assistant name. Settings can allow an unprefixed spoken
reply; this changes the recognition gate, not the scope of an existing grant.

Kokoro is an optional local neural text-to-speech provider. Kora makes no
Kokoro network request unless the user explicitly selects **Download**. That
action downloads a pinned model and voice archive from a fixed GitHub release,
validates both with SHA-256, and stores them under
`%LOCALAPPDATA%\Kora\Speech\Kokoro`. Spoken response text is not sent to GitHub
or another service; synthesis runs locally after installation.

## Microphone consent

Device discovery and selection do not record audio by themselves. Kora opens
capture automatically on startup when voice readiness succeeds because
listening is its primary interaction mode. **Disable listening** releases the
microphone for the current run. Listening is also closed on exit and before
Kora locks Windows.

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
