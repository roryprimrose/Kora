# All settings

Open Settings by:

- right-clicking the tray icon and selecting **Kora Settings**;
- double-clicking the tray icon;
- saying **"Kora, open settings"**; or
- entering **open settings** as a typed command.

The Settings window is single-instance. Its title includes the running
application version as a suffix. Source builds default to version `0.1.0`;
release builds can override that version during publishing. Changes use the
same live application state as the presence and response surfaces.

## Release maintenance (notify-only)

Use **Settings > Maintenance > Review / check / open canonical release** or
the Tray **Release maintenance (notify-only)** entry. The existing **Review
local version (native question)** remains a separate local-only workflow.
Select Production (stable only) or explicitly choose Preview (published beta
releases only), then permit **public metadata checks for this run**. Permission
is off by default and not saved. An initial check is scheduled; subsequent
checks use six hours plus jitter and explicit failure/rate-limit backoff.
**Check canonical releases** respects the same deadline, including after
permission/channel changes.

Read the exact version, immutable source, architecture, expected application
ZIP digest, canonical page and unsigned disclosure, then select **Review this
exact verified release** before **Open reviewed
canonical release page**. Browser navigation is not download/install approval.
Any refresh, changed snapshot, expiry or admission closure invalidates this
review; Open/Snooze require a new explicit native review.
**Snooze this version (24h)** affects only this run; there is no automatic
speech, focus or prompt backlog. Lock/disconnect, unknown ownership and
protected call mode close the review and invalidate pending callbacks.
Return explicitly after recovery; Kora does not replay a deferred check.

Available/UpToDate require consistent canonical metadata. Unavailable means
no selected release or HTTP 404, not verified current. RateLimited (403/429)
shows a retry deadline. Unknown includes transport, timeout, cancellation,
malformed/oversized/changed metadata and unsupported architecture.
Last successful verification stays historical after a failed check and is
stale after six hours; stale/failed results cannot open a release page.

Only release metadata and the bounded JSON manifest are fetched. Kora never
obtains ZIP/MSI/EXE/source code, stages, executes, elevates, installs or changes
source. Hashes are not publisher signatures. x86 has an application ZIP but
no x86 installer/native capability acceptance. Deployment mode is unknown;
replacement and prerequisite handling remain external/manual.

## Models

The **Models** tab controls which model locations Kora may use for free-form
commands:

- **Allow local models** is on by default. When enabled and readiness has
  verified Ollama and the pinned model, unmatched requests can run locally.
  Turning it off cancels an in-flight local request and prevents later
  requests from reaching Ollama.
- **Allow hosted models** is off by default. The current build has no hosted
  provider or credentials, so enabling this permission does not send data or
  provide a cloud fallback. The saved opt-in will gate hosted execution when a
  provider is implemented and configured.

These device-local choices are applied immediately and retained across
restarts. Exact built-in commands remain available when both choices are off.
Use **which models are enabled**, **enable/disable local models**, or
**enable/disable hosted models** as typed or activated voice commands to read
or change the same settings. Model-setting writes are security audited.

## Approvals

Model-suggested actions that need approval offer **Once**, **This session**,
**Always**, and **Reject** on a visible response card. Kora also accepts newly activated push-to-talk
replies after it finishes speaking the approval question. By default, say the
assistant name first: **"Kora, approve once"**, **"Kora, approve for this
session"**, **"Kora, always allow this"**, or **"Kora, reject"**. The
**Require the assistant name before a verbal approval** setting is on by
default; turn it off to accept those replies without the name while an
approval is pending.
The same name-prefix setting applies to verbal answers to a model's
clarification question. A clarification choice is not an approval; the
response card shows numbered options separately from grant/action controls.

Approvals are specific to the named built-in action. The **Approvals** tab
lists active session and always grants separately and lets you revoke either
kind per action. Session grants end when Kora exits, restarts, or locks
Windows through Kora. Always grants are stored in Kora's device-local
Preferences folder and remain until revoked. These grants are action-name-only:
they do not authorize a script, executable, or particular implementation
version. They govern model-suggested disruptive actions, not direct exact
built-in phrases (including lock). Any model-suggested action that requires
approval can use any of the three scopes; the model cannot choose the scope
for you.

## Readiness and required tools

The setup queue checks local storage, SQLite, PowerShell 7 (`pwsh.exe`), and
Ollama/model inference on startup and refresh even when local model execution
is disabled, so readiness remains visible before it is re-enabled. A missing or unhealthy PowerShell
runtime shows a PowerShell setup task. The Readiness page displays the detected
PowerShell and Ollama/model status without requiring an approval prompt; review
buttons are shown only when the corresponding dependency needs action.
**Review PowerShell 7 setup** explains
the per-user winget installation and asks for explicit consent; installation
and a no-profile health check run only after approval. **Review local model
setup** separately asks permission to install Ollama and download the pinned
qwen3:1.7b model; Kora checks its digest and actual inference before using
it. Setup shows an indeterminate progress bar while Winget and the runtime are
starting, then a percentage bar with readable MB/GB transfer totals while the
model downloads. After transfer, the same area explicitly reports local
finalization, digest verification, inference testing and readiness refresh.
A newly installed package gets a bounded grace period to start its own runtime;
transient startup timeouts are retried before Kora starts a fallback owned
server. Terminal success or failure replaces the current phase in the same
status area. Missing local
inference opens this tab on startup. PowerShell readiness
does not gate local reasoning or the current C# built-ins. Neither setup button
grants permission to run an arbitrary script.
The optional Kokoro speech provider is offered separately and is not a
required queued setup task.

## Appearance

The existing controls and exact typed/activated-voice commands share one
host-owned, typed appearance service. No model or network is needed. Enter
**list appearance settings** to inspect the nine admitted options, units,
defaults, bounds, scope and application timing.

| ID | Default | Values / units |
|---|---|---|
| `appearance.theme` | `system` | `system`, `light`, `dark` |
| `appearance.presence-timeout` | `10` | 1-60 seconds |
| `appearance.response-timeout` | `5` | 1-60 seconds |
| `appearance.presence-size` | `360` | 240-600 pixels |
| `appearance.dot-size` | `100` | 50-200 percent |
| `appearance.dot-density` | `100` | 25-200 percent |
| `appearance.movement-speed` | `100` | 25-200 percent |
| `appearance.speech-scaling` | `true` | `true`, `false` (visual sizing only) |
| `appearance.speech-scale-amount` | `100` | 0-200 percent |

Use **get appearance.theme**, **set appearance.theme to dark**, or
**reset appearance.theme**; substitute another listed ID and its typed value.
Voice input uses the currently configured assistant name, for example
**"Kora, set appearance.presence-timeout to 15"**.
The listed spoken names replace dots and hyphens with spaces:
**"Kora, set appearance theme to dark"** or
**"Kora, set appearance presence timeout to 15"**.
Integer values are numeric
and use the listed units; relative changes, number words and appended units
are not interpreted. Ambiguous, unknown and out-of-range appearance commands
show clarification instead of reaching a model or silently clamping.

The Appearance tab also offers **Reset selected option**. Reset affects only
that option, not a whole profile or undo history. Updates revalidate the
typed value and current host revision before one atomic preference-file write.
A stale proposal requires a fresh inspection/proposal. A save failure retains
the previous saved/effective value and revision and shows the error.
Get distinguishes a saved preference from an unsaved domain default.
Successful changes update all open visual surfaces without reopening them.
Speech scaling changes only animation, not audio output or call policy.

Response pinning, topmost behavior and window placement retain their existing
direct controls but are not registered: their shared-file writes are outside
this bounded registry. Voice/audio/call, model, grants, retention, setup and
startup settings are not admitted here. Full verbal preferences and
model-facing settings tools remain future work.

### Application theme

- **System** - default; follows live Windows light or dark appearance.
- **Light** - always use Kora's light palette.
- **Dark** - always use Kora's dark palette.

The theme applies immediately to Settings, the presence, response surface,
documentation, and other Kora-owned visual surfaces.

### Independent visibility timeouts

- **Presence timeout** - default **10 seconds**; range **1 to 60 seconds**,
  under **Appearance > Presence appearance**.
- **Response timeout** - default **5 seconds**; range **1 to 60 seconds**,
  under **Appearance > Visual feedback**.

Presence hides automatically and without asking when it is idle or listening
and no prompt needs attention. New Kora interaction restarts its timer.
Active work, speech, approval/question prompts, recovery actions, grant editing,
and unacknowledged failures prevent presence auto-hide. Once these finish,
a fresh inactivity period starts. Listening/capture is not required for the
timer to run, and hiding presence does not stop listening or work.

The response window uses its own timer. **Always show** disables only response
auto-hide; it does not pin presence. Question/approval prompts and response
action links keep the response visible until resolved or dismissed.
Changing one timeout does not change the other. Previously saved response
timeout values are retained when upgrading.

### Presence appearance

The presence is the animated group of dots that communicates Kora's current
state. Under **Settings > Appearance > Presence appearance**, the controls
apply immediately and are stored on this device:

- **Presence size** - default **360 px**; range **240-600 px**.
- **Dot size** - default **100%**; range **50-200%**.
- **Dot density** - default **100%** (150 dots); range **25-200%** (38-300 dots).
- **Movement speed** - default **100%**; range **25-200%**.
- **Speech sizing** - on by default; grows and shrinks the presence to follow
  the rhythm of Kora's actual speaker output.
- **Speech scale amount** - default **100%**; range **0-200%**. This controls
  the deviation from normal size: 100% uses 90-112% of normal size, 200% uses
  80-124%, and 0% holds normal size. The slider is disabled while speech sizing
  is off, but its value is retained.

Changing the overall size keeps the presence anchored to the bottom-right
of the active display's working area until you move it. Hold **Ctrl**, then
left-click and drag the visible presence to reposition it. Without Ctrl,
mouse events pass through to the window underneath. Release the mouse button
and Ctrl when finished; ongoing gestures keep their original recipient.
Kora restores its last position when that
position remains on a connected display and otherwise falls back to the
bottom-right working area. Dot size changes particle diameter without changing
the number of particles. Movement speed scales the motion associated with each
assistant state without changing the state itself.
Dot density changes only how many dots are shown. Speech sizing does not change
the state colours, dot motion, speech volume, or playback policy. It follows the
audio playback envelope rather than microphone input or a simulated pulse, and
smooths rapid changes without losing the syllable rhythm. It eases back to
normal size when playback stops or speech sizing is switched off.

### Response window

Configure the response window's display behavior under **Appearance > Visual
feedback**:

- Drag the title area to reposition the window. Kora restores the last position
  when it remains on a connected display.
- **Always show** keeps the current response visible until **Dismiss** is
  selected. Default: off.
- **Stay on top** controls whether the response remains above other windows.
  Default: on.
- **Response timeout** changes only the response window's 1-60 second inactivity
  interval. Presence uses the separate **Presence timeout** setting.
- Press **Enter** in the typed-command prompt to run the command.
- **Cancel task** is visible while local model work or response speech is
  active. Select it or press **Esc** to stop that work. **Dismiss** only hides
  the response and does not cancel model work or stop speech.

These choices and the window position are stored on this device.

## Speech and audio

### Assistant name

- Default: **Kora**
- Length: 1 to 3 words and no more than 32 Unicode characters
- Allowed: letters, numbers, spaces, apostrophes, and hyphens

The name updates window titles, tray labels, command prefixes, recognition
grammar, visual responses, spoken identity, and voice preview. Names that
collide with built-in command phrases are rejected. The executable remains
`Kora.exe`, and local data remains under `%LOCALAPPDATA%\Kora`.

### Microphone

- **Choose microphone (native recovery)** opens the same passive card as the
  tray entry. Opening/Refresh reads bounded device metadata only. Highlight an
  exact endpoint, then **Save preference only**; System and unavailable saved
  pins remain visible, including after restart.
- Separate **Enable listening (PTT readiness only)** uses the displayed saved
  endpoint and fresh host gates, not the highlight or an old answer. Consent
  remains in the existing separate Settings flow. No combined consent/enable,
  microphone test or durable device-question bridge is available.
- The card reports whether Windows microphone access for desktop applications
  is **allowed**, **blocked**, or could not be confirmed.
- **System** follows the live Windows multimedia-default microphone.
- A named endpoint creates a pinned local override.
- Selection does not enable listening.

When access is blocked, select **Open Windows microphone settings** in Kora,
enable **Microphone access** and **Let desktop apps access your microphone**,
then refresh readiness.

### Voice consent and activation

- First launch requires explicit ongoing consent. Saved consent allows safe
  startup to arm push-to-talk, never ambient command transcription.
- Production wake is unavailable. Hold **Push to talk** (mouse, Space or Enter),
  speak one command, then release.
- The status explains when listening is paused because Windows reports the
  session as locked, microphone access is blocked, call policy prevents
  activation, or listening was disabled manually.
- **Enable listening** re-arms push-to-talk after manual disablement or privacy recovery.
- **Disable listening** releases it.
- **Withdraw voice consent** persists the closed choice across restart.

Manual disablement is not persisted across application restarts; Kora returns
to fresh-gated push-to-talk enablement on the next ordinary start with saved consent.
Unlock, resume, restored permission and device reconnection require explicit
Enable listening in the same run. Device selection or refresh does not release
that hold.

### Speech provider

- **Installed speech provider and voice** selects only ready installed choices.
  The stable provider IDs are `windows-sapi` and `kokoro`.
- **Speech provider assets (review only)** is separate. Reviewing a provider
  never changes the saved selection or working voice.
- **Windows** is built in, uses installed SAPI voices, and requires no download.
- **Kokoro** is an optional local neural provider. Kora does not download it
  automatically.
- Review Kokoro under provider assets and choose **Download** to retrieve its pinned model and voice
  assets. The download is approximately 219 MiB.
- Kora validates both assets by exact size and SHA-256 before installing them.
- Kokoro becomes available immediately after preparation; Kora does not need to
  restart.
- **Remove downloaded model** deletes Kokoro's device-local model and voices
  without affecting the built-in Windows provider.

Provider and voice selections are stored as one atomic device-local choice.
Completing a download makes choices available; explicitly select the installed
provider or voice to change output. Missing assets and invalid saved state
disable speech with a visible recovery message, never silently substitute.

### Speech voice

- Select an installed voice. The displayed `provider / ID` identifies the exact
  choice; selecting a voice explicitly selects its provider too.
- Without an explicit voice, Kora uses that provider's advertised default.
  If no compatible default exists, select an exact installed voice instead.
- **Reset speech provider** restores Windows and its advertised default voice.
  **Reset speech voice** restores only the selected provider's advertised default.
  Neither reset downloads missing assets or chooses an unrelated voice.
- Exact typed/activated-voice commands use the same workflow:
  `list speech settings`, `get speech.provider`, `get speech.voice`,
  `set speech.provider to windows-sapi`,
  `set speech.voice to kokoro / af_heart`, and `reset speech.voice`.
  Use the IDs actually listed on your device. Unqualified voice IDs must be
  unambiguous. Protected calls reject original voice-channel set/reset;
  initiate a new eligible Settings/typed request rather than confirming later.
- **Preview** speaks a short identity phrase with the selected voice and output.
  No ambient recognition is active.
- **Stop** cancels active speech.

Status shows desired/effective voice, saved/default provenance, revision and
recovery. Save failure retains the previous selection. Concurrent or stale
changes require a fresh inspection/request. This bounded configuration does not
add rate, volume, call exceptions or model settings tools.

### Spoken summary limits

Under **Speech & audio**, choose **Maximum sentences (1-3)** and **Maximum
words (1-80)**. Defaults are **3 sentences / 80 words**; either cap can be
lowered independently. **Reset sentence cap** restores 3 while preserving
words; **Reset word cap** restores 80 while preserving sentences.

The registry IDs are `speech.summary-sentences` and `speech.summary-words`;
spoken names are **speech summary sentences** and **speech summary words**.
Discovery/get/set/per-option reset use the same native/typed/activated-voice
workflow. For example, **set speech.summary-words to 40**.
Status distinguishes unsaved defaults from saved limits and reports revision
and recovery. The host checks original channel, live privacy/ownership and call
revision adjacent to audited atomic persistence. Protected calls reject
voice-originated changes, including reset. No provider, voice, output routing,
voice consent or call protection is changed.

Ordinary finalized speech is admitted only if the complete spoken text
(title, answer and retained warnings) fits **both** configured caps. The host
does not infer safe omissions from model prose: over-cap results get a truthful
**Speech withheld** status and forced full visual recovery, even in Audible
only mode. No truncation, success paraphrase, extra model call or queued retry
occurs. Full visual text/details remain unchanged. Exact proposal/approval
readback, required questions/options and voice previews are not ordinary
summaries; their existing exact/bounded/privacy rules still apply.
No arbitrary full-content reading command is added.

Counting is deterministic Unicode text counting, not acoustic duration or
linguistic segmentation:

- A word is a run of Unicode letters/digits. Combining marks and ASCII
  apostrophe, right apostrophe, ASCII hyphen, U+2010 and U+2011 may join a run
  (including repeated joiners). Other punctuation/whitespace splits runs.
  Decimal dots, acronym dots, underscores, slashes and `@` split word runs.
  Emoji/symbols alone are not words; unspaced CJK text is one word run.
- A sentence is a segment containing a word, ended by `.`, `!`, `?`, `。`,
  `！`, `？` or `．`. Repeated terminators/closing quotes do not add empty
  sentences. A trailing word-bearing fragment counts as a sentence.
  Line breaks alone do not end sentences.
- ASCII dots between digits, single ASCII-letter initials and these case-insensitive
  abbreviations do not end sentences: `Mr.`, `Mrs.`, `Ms.`, `Dr.`, `Prof.`,
  `Sr.`, `Jr.`, `e.g.`, `i.e.`, `etc.`, `U.S.`, `U.K.`. Abbreviations must
  end at a non-letter/digit boundary. Other abbreviations and punctuation use
  the rules above; no dictionary/model guess is made.

Limits are a versioned atomic device-local preference independent of the
existing provider/voice file. Upgrades with no limit file use unsaved defaults
without rewriting legacy speech choices. Corrupt/unreadable/unknown formats
disable ordinary speech with visible recovery; they are not defaults.
Repair the local preference file and refresh. A per-option mutation cannot
silently default an unknown companion cap. Mandatory readback and visual output
remain governed by their existing rules. Changes retire pending speech before
UI dispatch; reset or call clearance never replays retired output.

Push-to-talk stops current speech before opening command capture.
**Stop speaking** is also available from the tray without speech or a model.

### Audio output

- **System** follows the live Windows multimedia-default output.
- A named endpoint creates a pinned local override.

### Refresh devices and readiness

Re-enumerates microphones, voices, speakers, Windows defaults, mute state, and
dependency readiness. Refresh does not enable listening.

## Responses

### Device default

Persistent **Both audible and visual**, **Audible only**, or **Visual only**
preference.

### Current queue

Temporary response-mode override. It takes precedence over the device default.
Select **Inherit** in the dropdown to remove the queue override and use the
device default.

### Current task

Temporary response-mode override with the highest precedence.
Select **Inherit** in the dropdown to remove the task override and use the
queue override, or the device default when no queue override exists.

### Muted speaker fallback

Default: **on**. Show audible-only responses as text when the selected Windows
speaker is muted or its endpoint volume is zero. Applies to the device default
and task/queue overrides, using either **System** output or a selected endpoint.
The configured response mode is unchanged; unmuting restores it on the next
response without refreshing devices.

Turning this off suppresses ordinary visual fallback for muted output only.
Failures, pending questions, approvals, missing devices, and unavailable speech
voices still use visual output.

### Effective output

Read-only explanation of the currently resolved mode and any forced visual
fallback.

## Calls

### Call detection

Read-only status keeps manual state and automatic evidence separate. Automatic
detection remains unavailable in this build; no Teams/account/network detector
is implemented.

### Manual call mode

**I'm in a call** activates protection for this run. **Clear manual call mode**
removes only that layer, never an enabled automatic Active/Suspected/Unknown
observation. Manual state is not persisted across restart. Controls recheck
original request origin, observed call revision and live ownership/privacy.
Stale changes are rejected, not queued for call clearance.

### Visual responses during calls

Default: on. Manual Active and enabled Active/Suspected/Unknown override ordinary
output with visual-only responses and suppress previews/readbacks. Existing saved
choices are retained. New disabling is unavailable pending complete exact
trusted review; enabling protection is supported.

### Voice activation during calls

Default: on. Turning it off closes capture when a call is detected and blocks
re-enabling until protection clears. Clearance never automatically reopens
capture. New re-enabling of this preference is unavailable pending exact review.

During protection, all voice-originated voice and in-call preference mutations
are rejected, including manual clear/reset and ordinary output options masked
by the call override. Later UI confirmation cannot change voice lineage; start
a new UI request. Stop speech, disable listening, cancel and read-only status
remain usable. Protected calls ignore Session/Always reusable model-action
grants without changing storage; new grant-ignore disabling, temporary overrides
and speak-once exceptions are unavailable. This is bounded manual behavior, not
full call/provider/native/audio acceptance.

## Readiness

Shows each dependency as Ready, Unavailable, or needing attention, with an
explanation. Status labels use readable text such as **Needs configuration**.
**Refresh readiness** repeats device and dependency discovery without opening
the microphone.

Select **Open Windows microphone settings** to manage Kora's Windows microphone
permission without navigating through Windows Settings manually.

## Where settings are stored

Device-local settings are written under:

`%LOCALAPPDATA%\Kora\Preferences`

They do not roam with documentation or user content. Stored skills are not yet
available.
