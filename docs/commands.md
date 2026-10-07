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
