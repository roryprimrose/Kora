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

## Local speech text

Speech text is **Off** by default. In response settings select **Inspect speech
text**, choose **CurrentUtterance**, then **Save speech text only**. Reset saves
**Off**. The native caption shows selectable exact approved text only during
matching actual response playback, not while synthesis is pending or speech is
suppressed. It does not steal focus when shown.

Exact typed or current-name activated commands are `list speech text settings`,
`get display.speech-text`, `status display.speech-text`,
`set display.speech-text to Off`, `set display.speech-text to CurrentUtterance`,
and `reset display.speech-text`. These only inspect/save the device-local
preference; they never speak, replay or open the microphone.

Captions clear immediately on completion, interruption, replacement, lock,
ownership/privacy/call change or unconfirmed configuration. Corrupt or pending
preferences keep captions off; inspect saved state and audit receipts before
explicit repair. Required approval/error panels remain visible independently.
Sentence timing, pinning, placement preferences, dismissal delays, broad caption
commands and rich browser/HTML/diagram rendering are not implemented.

## Windows-native speech rate

**Settings > Speech & audio** provides a bounded draft, **Refresh Windows rate
preference only**, **Save Windows-native rate only** and **Reset Windows rate to
normal (0)**. The independent `speech.windows-rate` accepts canonical integer
**-10 through 10**, default/reset **0**. These are native Windows engine units,
not percent, a multiplier or universal words per minute. See the
[Microsoft API](https://learn.microsoft.com/en-us/dotnet/api/system.speech.synthesis.speechsynthesizer.rate?view=net-10.0-pp).

Discovery/status explicitly qualifies provider support. Save/reset requires
the installed available **Windows** provider and qualified Kora-owned adapter.
**Kokoro is unsupported and unchanged**; a saved Windows rate is retained but
never reported as a Kokoro speed effect. Unknown state stays unavailable.
[Exact typed/current-name ACTIVATED commands](commands.md#inspect-or-change-windows-native-speech-rate)
use the same admitted host workflow, not model settings tools.

Saving/resetting retires active and queued speech. Only future eligible Windows
synthesis consumes the owned engine rate, after the previous synthesis lifetime
quiesces; changing it never starts or replays speech. Full interrupted responses
and mandatory previews stay visual. No capture/consent, provider/voice/output
selection, asset install/download, global SAPI/mixer/default-device, zero-volume/
protected-call policy or retention/grant/history setting changes.

Original input, host-owned audio session/generation, current name/provider/source/
policy/privacy/topology and visible native-lifetime identity must remain current.
Required requested/terminal audit, atomic save/exact readback and committed intent
precede activation. Corrupt or unconfirmed files cannot silently default on restart.
Inspect saved state and audit/intent receipts before explicit repair and refresh;
refresh does not clear an unconfirmed marker. Reset removes only the rate override.
Deterministic source/native-seam tests do not establish acoustic or installed
acceptance.

## SQLite diagnostic retention

**Settings > Logging** provides Refresh, Save future retention only and Reset to
30 days. The single `logging.sqlite-diagnostic-retention-days` option accepts
an exact integer **1–365**, default/reset **30**. Discovery/status shows saved or
default provenance, desired/effective days, fresh revisions and explicit recovery.
The [exact typed/current-name activated commands](commands.md#sqlite-diagnostic-retention)
call the same host workflow; configuration is not a model tool or effect grant.

Only newly committed **ordinary SQLite** logs, completed spans and their owned
links use the confirmed policy. Existing deadlines never change, including
across restart. **Apply-now and immediate deletion are unavailable.** Saving
or resetting does not run pruning or change cleanup scheduling.

Independently configured audit retention, daily files **30 days/30 files**,
session/chat/history, every approval and every grant are unchanged by this option.
Perpetual grants never expire or undergo time retention/eviction. Future
session/history suggestions are not current configuration options.

Writes require original local input, owning unlocked host/privacy/call/input
revalidation, independent host-resolved diagnostic session/generation, exact
proposal/revisions, required typed audit and atomic durable save/readback.
Closing/replacing the native surface invalidates its callback; no pending exact
question/approval is answered or replaced. Corrupt or unconfirmed preferences
stay unavailable across restart: inspect saved state and audit/intent receipts,
explicitly repair and refresh. No silent default or automatic marker clearing.
Ordinary SQLite delivery reports an explicit gap while independent file and
required audit paths remain separate.

## Future-only audit retention

**Settings > Logging** also provides independent audit Refresh, Save future
audit retention only and Reset to **90**. `logging.audit-retention-days` admits
canonical integers **30–365**, default/reset **90**; typed/current-name
ACTIVATED get/status/set/reset use this same workflow. `list logging settings`
reports both complete admitted options and saved/default/effective provenance.

Only NEW committed required authority audit and independently qualified SQLite
diagnostic audit projections use the confirmed days. Their source identities
remain distinct; neither diagnostic projections nor daily files become
permission authority. Existing audit deadlines, payloads, hashes and citations
stay exactly unchanged across restart and migration. **Apply-now, immediate
deletion and audit pruning are unavailable.** Save/reset never runs cleanup.

REQUESTED and terminal preference receipts use the prior policy; activation
follows required audit, atomic save/exact readback and durable intent outcome.
The separate durable unconfirmed marker prevents activation after lost
evidence or late admission/confirmation failure. Corrupt/unknown/pending data
refuses, never silently falls back to 90. Inspect saved state and audit/intent
receipts before explicit repair and refresh. At startup, unconfirmed audit
policy refuses authority recovery/writes; in-run failure shows held native
status without replay. Native hide/reopen/dispose invalidates old callbacks.

Audit days are not grant lifetime. All grant records and existing
validity/scopes remain; Perpetual records have no expiry/retention/eviction.
Session/chat/history, approvals/tasks/questions, ordinary SQLite
**1–365/default-reset 30**, daily files **30 days/30 files**, resource/call/audio
behavior and cleanup schedules are unchanged. No full retention-cleanup,
forensic/encryption or installed/native/acoustic qualification is claimed.
See the [canonical bounded contract](../Design/User_Configuration.md#delivered-bounded-future-only-audit-retention-r10r04).

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

Exact typed/activated **maintenance status**, **maintenance review** and
**maintenance snooze** use this same cached native workflow. Status never
checks or calls missing/failed metadata current; review displays the same
exact immutable record/source/trust details and opens this native surface,
not the browser. Snooze targets only the fresh reviewed Available notice
for 24 hours in the current run, not approvals/security prompts or a saved
reminder policy. Pending exact questions/approvals remain fully unchanged.
Commands cannot grant/renew network permission or check/refresh/open/download/
install/elevate/activate anything. They require current original input, owning
private host, clear/unavailable call admission, durable session generation and
exact cache revision/identity; failures require explicit recovery.
See [exact cached commands and bounds](commands.md#exact-cached-release-maintenance).
This does not complete the general R18 broker or R17 installed acceptance.

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
**list appearance settings** to inspect the ten admitted options, units,
defaults, bounds, scope and application timing.

| ID | Default | Values / units |
|---|---|---|
| `appearance.theme` | `system` | `system`, `light`, `dark` |
| `appearance.presence-display` | `true` | `true`, `false` |
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

The **Show presence** control and `appearance.presence-display` setting make
the animated presence optional. It is displayed by default. Turning it off
keeps the presence hidden across restarts while response, settings, approval
and other interactive surfaces remain available.

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
- **Light** - always use Kora's light palette, with darker secondary text for
  descriptions and inactive navigation labels.
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

### Assistant display / PTT command-prefix name

- Default: **Kora**
- Length: 1 to 3 words and no more than 32 UTF-16 characters after whitespace
  trimming/collapse (the existing name rules, not session-name NFC rules)
- Allowed: letters, numbers, spaces, apostrophes, and hyphens

**Apply name** and **Reset name to Kora** use the same typed, audited atomic
workflow as [exact assistant settings commands](commands.md#assistant-display--ptt-command-prefix-setting).
Discovery/get show schema 1, `assistant.name` (spoken: **assistant name**),
string type, bounds/default, device-local scope, effect/timing, process-local
revision and saved-versus-unsaved-default provenance. Reset affects this option
only; a missing file remains an unsaved domain default until a changed choice
is explicitly saved.

The name updates window titles, tray labels, command/help prefixes, visual
responses, spoken identity and voice preview. The next explicitly activated
PTT uses the new grammar; the old prefix is not an alias, including session
and artifact commands. A mutation retires capture and queued transcript/
completion generations and leaves listening held. Use **Enable listening**
explicitly before a new PTT; rename/reset never opens the microphone or clears
an existing run hold. Failed capture shutdown or persistence retains the prior
name and reports failure. A later grammar-start failure keeps the saved name
but capture unavailable; there is no old-grammar fallback.

Invalid/corrupt/unknown saved state is visible and disables prefix routing and
capture instead of silently using Kora. Native recovery and exact unprefixed
get/set/reset remain available. Repair storage access, explicitly set/reset or
refresh; an audit-completion failure after replacement requires inspection and
explicit recovery, not a reported successful mutation.
Protected/unknown call state rejects original voice-channel set/reset even
when later delivered through a button; initiate a new eligible local request.

This is presentation/routing, **not an authority identity or a qualified
production wake name**. Reset does not change session/task/grant/approval/
instance IDs or stored session names. The executable remains `Kora.exe`;
namespaces and application data paths remain fixed. No wake detector, custom
profile, learning/enrollment, assets, download or OS/global setting is added.

### Microphone

The existing input preference is also available as exact `speech.input-device`
[commands](commands.md#exact-input-device-preference). List/get reports
desired/effective and saved/default/unavailable provenance; set/reset uses the
same native audited atomic preference path. Reset selects System only and
cannot grant consent/permission, enable listening or release a manual/run hold.

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
Normal authoritative unlock automatically restores PTT readiness if it was
enabled before locking, resource closure succeeds and all fresh consent,
ownership, session, microphone and call-policy checks pass. It never resumes
recording: use a new PTT press. Manual disablement, withdrawal or intervening
failures prevent automatic restoration. Resume, restored permission and device
reconnection require explicit Enable listening; selection or refresh does not
release those holds. Future qualified always-on wake detection follows the same
prior-enabled-mode rule; production wake remains unavailable today.

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
add rate, call exceptions or model settings tools. Volume is the independent
bounded preference below.

### Kora playback volume

Under **Speech & audio**, select **Kora playback volume (0-100%)** then **Save
Kora volume only**. Highlighting is an unsaved draft. **Reset volume to 100%**
removes only the volume override; **Refresh volume preference only** reads
saved state without testing audio. Default **100** preserves original unscaled
output. **0** prevents synthesis/automatic playback and retains the complete
visual response, warnings and required prompts. Raising/resetting never replays
stopped or queued output.

Exact typed/ACTIVATED commands share these controls: `list volume settings`,
`get/status speech.playback-volume`, `set speech.playback-volume to 30`,
`reset speech.playback-volume`. Use canonical integers 0-100, without signs,
padding, fractions, `%` or leading zeros. The current assistant-name prefix
works. Results show saved/default/unavailable source, desired/effective percent,
bounds/default, revisions and recovery. Protected/unknown calls deny original
voice mutations; use a new eligible typed/Settings request.

Only Kora-owned Windows speech instance gain and Kokoro PCM attenuation change.
No Windows/system/call volume, mute, microphone, consent, provider, output
endpoint, summary, name, approval or retention setting changes. Unknown adapter
capability, invalid saved data, failed atomic save/readback/audit or stale
admission is explicit visual unavailability, never a silent default. A write
may precede failed terminal evidence: inspect/repair saved state and explicitly
refresh. An atomic unconfirmed-write marker keeps ambiguous writes unavailable
across restart until explicit evidence inspection/repair; refresh cannot silently
clear it. No automatic retry, test playback or acoustic audibility claim.

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
- An existing saved endpoint remains pinned; a missing pin is not replaced.

Choose **Refresh output metadata only**, select one exact endpoint choice, then
**Save output preference only**. The draft alone changes nothing. **Reset output
to System** removes only Kora's output override. The visible status distinguishes
desired/saved/default, effective/unavailable route, mute and recovery.
Metadata discovery has a five-second deadline and cannot accumulate workers.
Names can duplicate; endpoint IDs, host-held choice, session/generation and
current ownership/privacy/call/input revisions must match. Stale/foreign choices
require fresh discovery. Selecting/resetting cancels retired speech but never
plays a trial, resumes stopped output, changes Windows defaults/volume/mute,
opens capture, grants consent or changes provider/voice/summary/input settings.
Failed persistence/audit may leave a committed file; inspect saved status and
refresh, not automatic retry. Missing/muted/open/playback failures retain full
visual output regardless of the independently stored legacy muted-fallback option.
No acoustic or full R10 acceptance is implied.

### Refresh devices and readiness

Re-enumerates microphones, voices, speakers, Windows defaults, mute state, and
dependency readiness. Refresh does not enable listening.

## Responses

### Device default

Persistent `Hybrid` (**both audible and visual**), `VoiceOnly` (**audible only**)
or `VisualOnly` (**visual only**), default `Hybrid`. Choose **Inspect response
mode**, select a presented mode and **Save device-default mode**.
**Reset device default to Hybrid** saves only this default; it does not clear
queue/task overrides or change call/mute fallback, speech, microphone or consent.
Status reports saved/default/unavailable provenance, revision, desired/configured
effective mode and live output policy. The [exact commands](commands.md#inspect-or-change-the-device-default-response-mode)
use the same admitted audited atomic save/readback workflow.
Stale choices and failed storage/evidence require a fresh inspection; corrupt
saved state is unavailable, never silently Hybrid. Save/reset never plays or
replays speech or opens capture. Required full visual response/preview remains
available in VoiceOnly, including interrupted output, warnings and approvals.
Unconfirmed write evidence survives restart; inspect saved state/audit receipts
and explicitly repair it before refreshing. Inspection never silently clears
an unconfirmed marker or reports success after failed apply evidence.

### Current queue

Existing process-local presentation override, outside the admitted device-default
registry. It takes precedence over the device default.
Select **Inherit** in the dropdown to remove the queue override and use the
device default. This is not a delivered durable session/queue workflow or exact
configuration command.

### Current task

Existing process-local presentation override with the highest precedence,
outside the admitted device-default registry.
Select **Inherit** in the dropdown to remove the task override and use the
queue override, or the device default when no queue override exists. This does
not establish durable task-scoped configuration, a scheduler or task execution.

### Muted speaker fallback

Default: **on**. This independently stored legacy preference is retained for
compatibility; it cannot suppress mandatory complete visual recovery when the
effective output is missing, software-muted, zero-volume, unavailable or failed.
The configured response mode is unchanged. A fresh eligible route can restore
normal voice-only presentation; no recovery or setting change replays speech.
Pending questions and approvals retain their exact visual preview.

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
**Reset manual off** clears only this run layer. **Cached call status** is passive:
it shows the manual flag separately from automatic observation/availability,
saved flags and effective conservative protection at the current source/policy
revision. Native controls and [exact typed/activated commands](commands.md#exact-current-run-manual-call-control)
share genuine committed original-user intent, session generation and required
audited admission; inspection does not create an intent or approval.
Questions/approvals keep their full preview and block mutations. Changed manual
state retires old speech/input/callbacks; off/reset never replay or reopen.
Failed required evidence/retirement is explicit, with conservative protection
held and no automatic retry or false rollback. The manual flag is never saved.

### In-call feedback override

**Settings > Calls** provides an independent draft with **Voice / UI / Both /
Inherit**, plus **Refresh call feedback**, **Save call feedback only**, and
**Reset call feedback to UI**. Missing confirmed storage uses **UI**; reset
removes only this saved override. Highlighting a choice does not save it.
Status shows saved/default/unavailable source, desired/effective selection,
whether it applies, configuration/call revisions, hard policy and recovery.
The [exact typed/current-name activated commands](commands.md#in-call-feedback-override)
use the same host-admitted workflow, not a general model settings tool.

Only manual Active or enabled Active/Suspected applies this before ordinary
response modes. Inherit restores ordinary precedence. Clear/Unavailable retain
ordinary output; automatic detection remains unavailable in this build.
Unknown/invalid evidence keeps speech withheld and complete visual recovery,
not a Voice/Both exception. Voice/Both are preferences, never permission to
bypass call suppression, lock/privacy, microphone-active, mute/zero or safety
previews. Input/consent/grants are unchanged.

Protected original-voice save/reset is refused, even after a later UI click;
initiate a fresh UI change. Exact host-held choices, current name/input/call/
session/configuration revisions and the visible Settings lifetime must remain
current. Old choices cannot revive after hide/reopen. Writes require typed
audits, atomic save/readback and durable intent outcome before activation.
Changing or resetting retires old speech and never replays it.

Corrupt/inaccessible or unconfirmed state stays unavailable across restart:
full visual recovery remains and speech is held. Inspect the saved file and
audit/intent receipts, explicitly repair storage, then refresh. Refresh cannot
clear an unconfirmed marker or claim rollback after a committed file.
No detector, proactive/temporary exception, consent/grant or speech-policy
downgrade is added; native/acoustic acceptance remains separate.

### Call speech suppression (independent of feedback)

Default: on. Manual Active and enabled Active/Suspected suppress all automatic
speech, including previews/readbacks, with complete visual output. Unknown/invalid
enabled evidence always withholds speech. Existing saved
choices are retained. New disabling is unavailable pending complete exact
trusted review; enabling protection is supported.
The separate UI feedback default still selects visual output if an existing
saved suppression preference is Off. Choose feedback explicitly; it never
silently edits this suppression preference.

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
