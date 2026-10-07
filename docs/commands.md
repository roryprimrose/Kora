# Voice and typed commands

Voice and typed input use the same exact built-in phrases, which always take
precedence. Voice input may use the configured assistant name as a prefix.
Punctuation and capitalization do not matter for built-in phrases. Once the
verified local model is ready, unmatched requests can receive a local answer;
free-form voice requests must start with the active assistant name.
The alternatives below are explicit phrases, not fuzzy matching: additional
words beyond the phrase do not trigger a built-in action.

If the assistant is renamed, replace Kora with the configured name. The old
name is not retained as a hidden alias.

For the full design-defined catalogue, including capabilities not yet shipped,
see [Tools and built-in skills: current and planned](tools-and-built-in-skills.md).
The exact phrases on this page remain the current-release command reference.

## Assistant display / PTT command-prefix setting

Typed input and explicitly activated voice use the same host configuration
workflow as **Settings > Speech & audio > Apply name / Reset name to Kora**:

| Exact command | Result |
|---|---|
| `list assistant settings` | Schema/default/bounds/scope/effect/timing/reset and current revision/provenance/recovery for the one admitted setting |
| `get assistant.name` | Current display/PTT prefix and saved/default provenance, or explicit unavailable recovery |
| `set assistant.name to Nova Prime` | Validate with the existing name rules and atomically save one device-local value |
| `reset assistant.name` | Restore Kora only; no durable identity, session-name, grant, approval, data-path or namespace reset |

The spoken target **assistant name** is equivalent to `assistant.name`.
The active configured prefix is optional: **Kora, set assistant name to Nova**.
After success use **Nova, get assistant name**; Kora is no hidden alias.
Input is bounded to 320 characters after prefix removal, and names retain
their existing exact case/Unicode validation with whitespace trimming/collapse.
Unknown targets and malformed grammar clarify locally; invalid names are
audited denials, never inference or arbitrary configuration execution.

Set/reset require current host ownership/privacy and configuration/call
revisions. Protected/unknown calls reject original voice requests. These
commands do not answer, replace or approve a pending exact question/approval.
Mutation retires stale capture/transcript/completion generations and does not
replay input or re-enable listening. Explicit **Enable listening** and a new PTT
are required. Grammar-start failure is visible with no old-prefix fallback.
Corrupt saved state disables prefix routing; native recovery and unprefixed
get/set/reset, stop/cancel and Settings remain available.
This is **not production wake-name capability**, a model settings tool, an
acoustic acceptance result or completion of all R10.

## Read-only host discovery

These exact local commands require the active, unlocked Kora host and do not
invoke a model, install anything, or refresh probes:

| Exact command | Result |
|---|---|
| **list capabilities** / **capabilities.list** | Six admitted read-only descriptors with schema version, typed input/output shape, read-only effect, caller lanes, availability and limits |
| **describe capability** followed by a canonical ID / **capabilities.get** followed by that ID | The single admitted descriptor; unknown IDs are denied |
| **show registry version** / **application.get_version** | Actual running version; deployment information is explicitly not observed by the current provider |
| **show dependency readiness** / **readiness.get** | Recorded dependency observations and timestamps; unobserved dependencies are explicit |
| **list runtimes** / **runtime.list** | The existing local inference adapter's recorded status, not a catalogue of planned providers |
| **show local runtime status** / **runtime.get_status** | The same `local.inference` observation; tool-loop qualification remains false |

Optional configured-name prefixes and normal exact-command punctuation/case
handling apply. For example, **Kora, describe capability runtime.get_status**.
The complete serialized UTF-8 response is at most 4,096 bytes; lists contain
at most six records. The host API accepts only `{}` for version,
`{"offset":0,"count":6}` (both fields optional) for lists, and a required
`{"id":"..."}` for descriptor/runtime lookup. Unknown fields, duplicate fields,
invalid ranges, foreign/expired host context and unknown lanes are denied.
The native commands supply those inputs deterministically, rather than accepting
arbitrary JSON from the command box.

These results do not contain raw probe details, paths, endpoints, credentials,
model content, skill instructions, or execution tools. Observations are cached:
use the existing Readiness UI for a deliberate fresh check and recovery details.
An observed ready dependency does not grant execution permission. No hosted
provider, MCP adapter, or model tool/result loop is delivered by this registry.
Existing help, version, setup/status, lock and power phrases retain their behavior.

## Exact input-device preference

Typed input and **activated** voice use:

- **list input settings** — metadata-only discovery with the existing
  five-second single-flight deadline;
- **get speech.input-device** — recorded desired/effective choice, source,
  metadata/call revisions, availability/readiness and recovery;
- **set speech.input-device to {exact listed endpoint ID}** — preference only;
- **reset speech.input-device** — explicitly selects `system-default` (System).

The configured-name prefix is supported. Option grammar is case-insensitive;
endpoint IDs are exact and case-sensitive. Friendly names, indices, fuzzy
targeting and natural-language aliases are not selectors. Duplicate friendly
names are distinct IDs. System follows the Windows multimedia default;
unavailable pins remain pinned. Complete versioned JSON results are at most
64 KiB; original commands are at most 1,024 UTF-8 bytes without controls.
Oversize results are rejected, not partially presented.

These routes share the native Settings/tray/recovery-card preference workflow.
Selection/reset never enables listening, grants consent/permission, tests audio
or changes model/OS settings. Changed selection closes stale input; manual
disablement and recovery holds remain closed. Protected/unknown calls reject
original voice mutations; stale metadata, unknown ownership/privacy/permission,
pending questions/approvals and failures require a new explicit operation.
Terminal audit failure may follow a file replacement: inspect before retrying,
not automatic rollback/replay. There is no model tool or pending-question bridge.

## Explicit local clipboard preview

These exact commands and the tray's **Preview clipboard (local plain text)**
entry use the same request workflow without a model:

| Exact command | Result |
|---|---|
| **preview clipboard** / **preview the clipboard** / **snapshot clipboard** | Read one fresh bounded Unicode plain-text snapshot and open its immutable native preview |
| **explain clipboard** / **explain the clipboard** | The same local preview, with explanation explicitly unavailable |
| **reuse clipboard snapshot {exact snapshot ID}** | Reopen/select that same snapshot only; never reread or silently substitute changed clipboard text |
| **clear clipboard preview** / **revoke clipboard snapshot** | Discard Kora's snapshot and preview, not the Windows clipboard |

Configured-name prefixes and the normal exact-command case/punctuation rules
apply. The preview shows the host source/snapshot IDs, `CF_UNICODETEXT` format,
read version, capture time and exact UTF-8 byte count. Its **Reuse this exact
snapshot ID** button uses the displayed ID; **Revoke and clear**, closing the
preview, cancel, privacy/ownership loss, call-policy change and host exit clear
or suppress it. A new capture replaces the prior selection with a new ID.

The whole text must fit 256 KiB UTF-8, with valid paired Unicode surrogates.
Empty, unsupported, oversize, busy, denied, changed-version and malformed
reads are explicit; there is no truncation, queued retry, background watcher,
URL fetch, HTML/image/file capture, clipboard write, history or persistence.
Whitespace and line endings are preserved. Text is untrusted and may contain
secrets; preview/reuse is neither execution authority nor transmission approval.
Clipboard explanation is unavailable until qualified local tool-loop and
clipboard-answering gates pass. Nothing reaches the current JSON selector,
Ollama or a remote provider. See [privacy](privacy-safety-and-logs.md#clipboard-snapshots).

## Window and application tasks

### Inspect existing minimal durable sessions

- **open sessions**

The configured-name prefix is supported. This opens the same bounded native
[Sessions workspace](windows-and-tray.md#minimal-durable-sessions) as the tray
and compact response's **Ctrl+Shift+S**. It does not change a pending question
or approval target, create a conversation, resume a session or call a model.
Done/resume are explicit selected-ID native actions, not inferred from words
in history or from selecting a row.
The native workspace also offers **Create empty Active session** and **Rename
selected ID** with bounded durable names and optimistic revisions. These are
also available through the bounded exact commands below, but not model tools. A name never selects authority,
and the selected window never redirects global commands. Creation grants no
execution permission; rename/browse never resumes or changes approvals.

### Bounded exact-ID session commands

Typed input and **activated** voice share one grammar, with the configured
assistant-name prefix supported. Use **session help** for the full syntax:

| Command | Actual bounded result |
|---|---|
| `session list [after <exact-id>] [limit <1-50>]` | Active/Done IDs, names, authorization generations and metadata revisions |
| `session status <exact-id>` | Exact authority and optional durable name; not inferred runtime progress |
| `session inspect <exact-id> [tasks\|questions] [after <exact-id>] [limit <1-50>]` | A page of existing task IDs/request IDs/states/revisions/origins or question IDs/revisions/states |
| `session create "<name>"` | Empty named Active session with a fresh host-owned ID |
| `session rename <exact-id> <generation> <metadata-revision> "<name>"` | Rename only, including a Done session |
| `session done <exact-id> <generation>` | Guarded idle lifecycle transition; not cancellation or proof of success |
| `session resume <exact-id> <generation>` | Explicit Active transition; never reruns work or revives approvals |

IDs must be nonempty canonical hyphenated GUIDs. Revisions are unsigned decimal
integers (generation positive, metadata revision zero for absent legacy metadata).
Copy the exact observation; a stale revision fails and requires a fresh request.
Names are NFC single-line Unicode, at most 120 scalars and 480 UTF-8 bytes,
without surrounding whitespace or control/format characters. Names must be
quoted; double an interior quote, e.g. `session create "A ""quoted"" label"`.
No punctuation stripping, name lookup, ordinal/window selection or fuzzy matching
applies to this grammar. Whole input is limited to 1,024 UTF-8 bytes, pages
default to 25/max 50, and the complete structured JSON result is at most 64 KiB.
Overflow fails explicitly rather than truncating. Cursors are exact IDs, not
saved snapshots; refresh for concurrent changes. Lifecycle results omit
unobserved metadata; request status to observe it.

Each accepted command has fresh original-user lineage and a durable host control
intent/terminal receipt; reads do not change lifecycle, metadata, question or
grant authority. Existing partitions must be present. Errors are explicit:
refresh after conflict, resolve live/Unknown work or pending questions, or recover
private storage/ownership before a new deliberate request. A receipt failure
after a commit is not rollback; inspect current state before retrying.
The typed Run entry remains available for this deterministic namespace while
bootstrap work is busy; it does not cancel that work. Mutations still pass the
same exact-subject live-work and current host gates, not a new executor lane.

Activated voice uses the existing enablement/consent/capture/privacy boundary
and retains its originating channel and observed call/recovery revision through
commit. During protected calls, reads require permitted activation and private
presentation; voice mutations are explicitly unavailable under the existing
workspace clear/unavailable-call gate. Nothing is queued for later. Unknown
ownership/privacy fails closed. Pending bootstrap questions/approvals block
session commands without changing their targets; resolve them explicitly first.

**Exact task controls** use the same typed/activated-voice grammar and limits:

| Command | Actual bounded result |
|---|---|
| `task help` | Syntax and availability |
| `task status <session-id> <task-id>` | Exact durable state/revision, session generation, source/current-run and pending/terminal question distinctions |
| `task inspect <session-id> <task-id>` | The same complete bounded authoritative record, not inferred progress or remaining steps |
| `task cancel <session-id> <task-id> <task-revision> <generation> <question-id> <question-revision>` | Atomically cancel only admitted current-run local-version work still waiting before dispatch |

Copy exact IDs and all tokens from a fresh inspection. Only the tray's native
local-version question currently admits that wait. Its question is now before
dispatch; cancellation commits the terminal task, revised cancelled question,
target capability revocation and required audit together. It never creates or
consumes a grant, deletes a task, replays work or terminates a worker.
Unknown/foreign IDs, stale/different questions or revisions, expiry, previous
runs, answered/committed/dispatched/Unknown work and lost host authority refuse.
Already terminal work stays terminal; inspect its real outcome. A confirmed
pre-dispatch cancellation is not a claim that an already invoked effect stopped.
A confirmed cancellation claims no effect termination. This safe cancellation is also available during a protected call with private
ownership and eligible original activation; call/recovery revisions still gate
commit. Pending legacy bootstrap questions/approvals remain unchanged.

This is not conversation/transcript persistence, a queue/executor/scheduler,
general effect cancellation, deletion/retention, routing inference, model tools, or a
session-name inference feature. Native selected-ID Create/Rename/Done/resume
continue to use the same host workspace service and guarded storage transaction.

### Show the Kora window
- **show Kora**
- **open Kora**
- **show your window**
- **open your window**
- **show yourself**
- **bring up Kora**

### Hide the Kora window
- **hide Kora**
- **hide your window**
- **hide yourself**
- **close your window**
- **hide the Kora window**

### Exit Kora
- **exit Kora**
- **quit Kora**
- **close the Kora application**
- **close Kora**
- **exit the application**
- **quit the application**

### Restart the Kora application
- **restart Kora**
- **restart your application**
- **restart the app**
- **restart yourself**
- **relaunch Kora**

Closing or hiding the window leaves Kora running; exiting the application
releases the microphone. Application restart is different from restarting
Windows.

## Settings and guidance tasks

### Inspect or change an admitted appearance option

- **list appearance settings**
- **get appearance.theme**
- **set appearance.theme to dark**
- **reset appearance.theme**

Substitute one of the nine exact IDs in [Appearance settings](settings.md#appearance).
The configured assistant-name prefix is supported for typed input and activated
voice. Exact spoken names also work: replace dots/hyphens in the ID with
spaces, for example **"Kora, set appearance theme to dark"**.
Capitalization is ignored, but this typed value grammar deliberately
preserves signs and decimal punctuation: `-10` and `1.5` are rejected, never
normalized into valid integers. Use numeric whole numbers without appended
units, `system`/`light`/`dark`, or `true`/`false`. Extra words, invented IDs,
relative changes and invalid ranges produce local clarification, without
model/network interpretation. Changes and per-option reset share the direct
UI service, domain validation, revision check, atomic persistence, audit and
live notifications. There is no whole-profile reset or undo, model tool
exposure, arbitrary JSON patch or configuration-file editing authority.

### Inspect or change an exact output choice

Audio **output endpoint** preference changes are separate:

- **list output settings**
- **get speech.output-device**
- **status speech.output-device**
- **set speech.output-device to &lt;exact presented endpoint ID&gt;**
- **reset speech.output-device**

Discover first, then use one exact ID; no name/index/fuzzy alias is accepted.
Native Settings uses the same admitted choice/save/reset workflow. System is
`system-default`; reset removes only Kora's output override. These commands never
play audio, change Windows defaults/volume, grant consent or answer an approval.
Protected/unknown calls block original voice-channel mutations. Stale choices,
ownership/privacy changes and failed evidence require explicit refresh/recovery.

### Inspect or change Kora playback volume

- **list volume settings**
- **get speech.playback-volume**
- **status speech.playback-volume**
- **set speech.playback-volume to 30**
- **reset speech.playback-volume**

Use one canonical integer 0-100 (no `%`, fraction, sign, padding or leading
zero). The current assistant-name prefix works; old names are not aliases.
Native Settings shares the admitted, revisioned atomic/audited workflow.
Default/reset **100** is original unscaled output; **0** blocks synthesis and
keeps the complete visual response. Changes stop stale active/queued speech;
raising/resetting never replays it. Original voice mutations are denied during
protected/unknown calls. These commands never play a trial, change global/call
volume, microphone/consent, other options, retention or pending approvals.
Discovery is metadata only, not a model tool or acoustic test. Input is bounded
to 1,024 UTF-8 bytes without controls; the complete result is bounded to 64 KiB.
See [availability and recovery](settings.md#kora-playback-volume).

### Inspect or change an installed speech choice

- **list speech settings**
- **get speech.provider**
- **get speech.voice**
- **set speech.provider to windows-sapi**
- **set speech.voice to kokoro / af_heart**
- **reset speech.provider**
- **reset speech.voice**

Use the installed IDs actually listed on your device. Exact spoken option
names, such as **speech provider**, and the configured assistant-name prefix
also work. Voice choices use `provider / ID`, or an unambiguous exact voice ID;
selecting a voice explicitly selects that provider too. IDs are not guessed,
translated or case-normalized, and unknown or unavailable choices do not
trigger a model, download or substitute. The command bound is 320 characters
after the optional name prefix; voice IDs are at most 256 characters.

Provider set/reset also restores that provider's advertised default voice;
provider reset chooses Windows. Voice reset restores only the current
provider's advertised default. An unavailable default requires an explicit
installed voice choice. Status reports saved/desired/effective values,
revision and recovery. Protected calls reject original voice-channel set/reset,
including a later UI confirmation; a new eligible Settings/typed request is
required. Rate and model settings tools are not added; volume uses the
independent exact commands above.
See [the shared native controls](settings.md#speech-provider).

### Inspect or lower spoken summary caps

- **list speech settings**
- **get speech.summary-sentences**
- **get speech.summary-words**
- **set speech.summary-sentences to 2**
- **set speech.summary-words to 40**
- **reset speech.summary-sentences**
- **reset speech.summary-words**

The exact spoken names **speech summary sentences** and **speech summary words**
also work, with the configured-name prefix for activated voice.
Values are exact positive integers: sentences **1-3**, words **1-80**.
Defaults are **3 sentences / 80 words**. Reset changes only that cap.
These device-local settings share the installed-speech workflow, original-channel
call/privacy/ownership gate, revisions, audit and live Settings notifications.
Neither command permits a model interpretation, extra model call or asset change.
Ordinary speech includes the title and retained warnings and must fit both caps.
Over-cap results remain fully visual with an explicit refusal status, never
truncated. Exact approval/question readback retains its own mandatory bounds.
See [counting and recovery semantics](settings.md#spoken-summary-limits).

### Open settings
- **open settings**
- **show Kora settings**
- **show settings**
- **open preferences**
- **change settings**
- **settings**

### Open the user guide
- **open documentation**
- **show documentation**
- **show the user guide**
- **open the user guide**
- **open the manual**
- **show me the instructions**

### Open setup and readiness
- **open setup**
- **configure Kora**
- **show what you need**
- **set up local models**
- **check setup**
- **show readiness**
- **check dependencies**
- **review local model setup**

Opening setup shows readiness; it does not install a model without approval.

### Show supported commands
- **what can you do**
- **help**
- **show supported commands**
- **list commands**
- **what commands do you know**
- **what can I say**
- **show me what you can do**

## Information and task-control tasks

### Show the running version
- **what version are you running**
- **show your version**
- **what version is this**
- **tell me your version**
- **which version of Kora is this**

The tray also offers **Review local version (native question)**, an explicit
mouse/keyboard-only route using the same durable local version-query context.
It does not need voice consent, a microphone, a model or network access.
The native window names the original session/task/question, revision and
expiry. Choose **Show local version**, then **Submit answer** to read the
running version and private-storage disclosure. No choice is preselected.
**Save draft** records the current answer without submitting. **Review exact
record** is passive inspection, not approval or execution. **Cancel question**
is explicit; Close/Escape only closes presentation. Stale, expired or
privacy/ownership-unavailable targets cannot be answered.

Storage/audit failure never reports a successful query. Close and start a new
review after correcting the blocker; Kora does not automatically retry uncertain
work. Explicit cancellation may leave an incomplete query dispatch record,
which startup recovers as Unknown without replay. This bounded route does not
enable general effect approvals, change legacy grants or replace the ordinary
version phrases above. Desktop/screen-reader/speech acceptance is not yet
claimed from the automated tests.

### Show activity and the setup queue
- **what are you currently working on**
- **what are you doing**
- **what do you have left to do**
- **what are you working on**
- **what tasks are left**
- **show the task queue**
- **show setup status**
- **what is your status**

### Show current-task progress
- **what is the current task status**
- **what is the current task progress**
- **how far along is the current task**
- **show task progress**
- **show current task status**
- **how is the current task going**
- **what's the progress of the current task**
- **how much of the current task is done**
- **how is setup progressing**

Current-task status reports the active setup stage and any measured download
percentage, or the next setup item requiring action when nothing is running.
It does not invent a completion percentage when the operation provides only
stage updates.

### Cancel the active task
- **cancel task**
- **cancel current task**
- **stop**
- **cancel the current task**
- **stop the current task**
- **stop current task**
- **cancel the download**
- **stop generating**

While a local model request is generating or any response is being spoken, the
response window also shows a separate **Cancel task** button. It stops the
current model request or speech playback; press **Esc** for the same action
while the button is visible. **Dismiss** hides the response without cancelling
model work or stopping speech. **Stop speaking** remains the explicit voice
command for stopping only speech playback.

### Stop speech playback
- **stop speaking**
- **stop talking**
- **be quiet**
- **stop reading aloud**
- **stop the voice**

**Stop** cancels the active task; it does not mean stop speaking. Use one of
the speech-playback phrases to stop the voice instead.

## Windows session task

### Lock Windows
- **lock the machine**
- **lock my computer**
- **lock windows**
- **lock this computer**
- **lock my PC**
- **lock my screen**
- **lock this workstation**

Locking is a real local action. Kora closes microphone capture before asking
Windows to lock. This exact built-in command currently calls a C# handler
without a confirmation prompt. A model-suggested lock requires approval
unless that named action already has a session or always grant.

## Protected power-proposal tasks

### Propose a computer shutdown
- **shut down the computer**
- **shut down this machine**
- **power off the computer**
- **turn off my computer**
- **shut down my PC**
- **power down this computer**

### Propose a computer restart
- **restart the computer**
- **reboot this machine**
- **restart windows**
- **reboot my computer**
- **restart my PC**
- **reboot the computer**

### Cancel a pending power proposal
- **cancel shutdown**
- **cancel that shutdown**
- **cancel computer restart**
- **cancel that reboot**
- **don't shut down the computer**
- **abort shutdown**
- **cancel the reboot**
- **abort restart**

### Show pending power-proposal status
- **what power action is pending**
- **are you about to restart the computer**
- **is a shutdown pending**
- **is a restart pending**
- **show pending power action**

Shutdown and Windows restart are proposals in the current release. Kora shows
the recognized request but does not send a power operation to Windows.

## Grant-management tasks

### List and view grants
- **list grants**
- **show grants**
- **view grants**
- **what grants are active**
- **list my approvals**
- **show my permissions**

The live session and persistent grants appear in a Markdown document window.
Listing never changes permissions and works without a local model.

### Prepare a grant change
- **manage grants**
- **add a grant**
- **edit a grant**
- **remove a grant**
- **change grants**
- **manage approvals**

Select the exact built-in action and scope in the response window: **Add**
creates a session or always grant, **Remove** revokes an existing grant, and
**Move** edits its scope using the destination after "to". The destination
selector is used only for Move. Kora repeats the exact action, operation and
scope; confirm with the button or say "approve once" (prefixed with the
assistant name by default), or reject. "Always allow this" does not confirm
a grant edit: the proposed scope is already fixed. A removal with no specified
scope only proceeds automatically when exactly one grant exists for that
action; otherwise select the existing scope. If a ready model identifies the
grant from a more specific request, it still cannot apply the change without
confirmation. Confirming a grant never executes the named action.

## Model execution settings

These exact commands read or change the same device-local choices shown on the
Settings **Models** tab. They work without invoking a model and are available
through typed input or activated voice.

### Show enabled model locations
- **which models are enabled**
- **show model settings**
- **show model configuration**
- **what models can you use**
- **are local models enabled**
- **are hosted models enabled**

### Enable local model execution
- **enable local models**
- **turn on local models**
- **use local models**
- **allow local models**

### Disable local model execution
- **disable local models**
- **turn off local models**
- **stop using local models**
- **block local models**

Disabling local models cancels an in-flight Ollama request. Built-in commands
continue to work.

### Enable hosted model execution
- **enable hosted models**
- **turn on hosted models**
- **use hosted models**
- **allow hosted models**
- **enable cloud models**

### Disable hosted model execution
- **disable hosted models**
- **turn off hosted models**
- **stop using hosted models**
- **block hosted models**
- **disable cloud models**

Hosted execution is disabled by default. The current build has no hosted
provider or credentials; enabling hosted models records permission but does
not send a request or provide a cloud fallback.

## When a command does not match

When local model execution is enabled and the selected local model is ready,
Kora sends only the current unmatched
request text, the built-in action descriptions, and a limited snapshot of
readiness, task state/progress, listening state, and pending power-proposal
status to it. The model can return an answer, ask a clarification question
with two to four options, propose a grant change, or request one named built-in
action; Kora rejects unknown actions
and runs allowed ones through its existing command handler. Hiding or exiting
Kora, restarting it, cancelling a task, locking Windows, or proposing a power
action requires approval unless you have already granted that specific action
for this session or always. When asked, say **"Kora, approve once"**,
**"Kora, approve for this session"**, **"Kora, always allow this"**, or
**"Kora, reject"**. You may also use the visible scope buttons; the request
remains visible even when voice-only responses are selected. The configured
assistant name is required for spoken approval by default, but can be made
optional in Settings. Session grants end on exit, restart, or locking Windows through Kora.
Always grants persist until revoked under **Settings > Approvals**. Reject
or dismiss the suggestion to discard it. These grants are keyed to action
names, not executable or script hashes, and do not change how direct exact
commands are dispatched. The model cannot
directly access arbitrary files, services, or tools. Without a ready local
model, Kora explains that other requests are unavailable. Use **what can you
do** or open this Documentation window to review exact built-in phrases.

When Kora needs direction before answering, the response window shows the
question and numbered choices, even in VoiceOnly mode. Choose an option by
mouse or say its number or label (for example, **"Kora, option one"**); the
assistant name is required for spoken choices by default, as with spoken
approvals. Say **"Kora, cancel question"** or use **Cancel question** to
dismiss it. The chosen option and the original request go back to the local
model; selecting an option is **not** permission to run an action or create a
grant. A later action proposal still requires its own approval, unless a
specific grant already covers that action. Kora limits consecutive questions
to three; start a new request with more details if it reaches that limit.
## Run skills and future artifacts

Kora can apply a bundled skill's instructions to a local-model request from
the command box or activated voice. Future bundled instructions and prompts
use the same command format.

**Type a slash command**

Type `/` in the command box to open a dropdown of every available bundled and
disk-backed artifact. Continue typing to filter by command name, or type a
kind such as `/skill `, `/prompt `, or `/instruction `. Use Up/Down and Enter
or select an item with the pointer; Escape closes the dropdown.

Use a direct command:

```text
/lock
/restart
/shutdown
```

You can also include the artifact kind:

```text
/skill lock
/skill restart
/skill shutdown
```

An artifact that accepts request text uses the rest of the line as its input:

```text
/prompt explain why this setup failed
```

The current release bundles only the three skills listed above. An unknown,
incomplete, or wrong-kind slash command shows an error and is not sent to the
model as an ordinary question.

**Say an artifact command**

Start with your configured assistant name, then say **run** or **use**:

- "Kora, run lock."
- "Kora, use the restart skill."
- "Kora, run the shut down the machine skill."
- "Kora, use explain to summarize this result." when a future `explain`
  artifact is available.

If you renamed Kora, use the configured name. Voice artifact requests without
the activation name are rejected in the same way as other free-form voice
requests.

**Where disk artifacts are loaded from**

Kora loads compatible files at startup from its roaming `Skills`,
`Instructions`, and `Prompts` folders, recognized personal skill folders
under `.copilot`, `.agents`, and `.claude`, and the VS Code or VS Code Insiders
user prompts folder. It does not scan the rest of your profile or follow
reparse points.

Disk skills use `SKILL.md`; prompts use `*.prompt.md`; instructions use
`*.instructions.md`. Files require bounded UTF-8 content and YAML frontmatter.
Skills marked `user-invocable: false` do not appear. Conflicting command names,
IDs, or spoken names fail closed instead of choosing one source silently.
Restart Kora after adding or changing an artifact.

**What “run” means**

Kora sends the current request and the exact selected bundled instructions
only to the configured local model. The artifact is source- and
version-qualified, and it remains selected if Kora asks a clarification
question.

Selecting an artifact does **not** run its embedded PowerShell, approve a
protected operation, create a grant, or mean an action succeeded. If the model
proposes a registered action, Kora still applies the ordinary host validation,
approval, privacy, audit, and execution rules. The current embedded
session-control scripts remain inspection-only.
