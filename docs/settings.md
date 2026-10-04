# All settings

Open Settings by:

- right-clicking the tray icon and selecting **Kora Settings**;
- double-clicking the tray icon;
- saying **"Kora, open settings"**; or
- entering **open settings** as a typed command.

The Settings window is single-instance. Changes use the same live application
state as the constellation and response surfaces.

## Approvals

Model-suggested actions that need approval offer **Once**, **This session**,
**Always**, and **Reject** on a visible response card. Kora also accepts spoken
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
Preferences folder and remain until revoked. Any action that requires approval can use any of
the three scopes; the model cannot choose the scope for you.

## Readiness and required tools

The setup queue checks local storage, SQLite, PowerShell 7 (`pwsh.exe`), and
Ollama/model inference on startup and refresh. A missing or unhealthy PowerShell
runtime shows a PowerShell setup task. **Review PowerShell 7 setup** explains
the per-user winget installation and asks for explicit consent; installation
and a no-profile health check run only after approval. **Review local model
setup** separately asks permission to install Ollama and download the pinned
model. Neither setup button grants permission to run an arbitrary script.
The optional Kokoro speech provider is offered separately and is not a
required queued setup task.

## Appearance

### Application theme

- **System** - default; follows live Windows light or dark appearance.
- **Light** - always use Kora's light palette.
- **Dark** - always use Kora's dark palette.

The theme applies immediately to Settings, the constellation, response surface,
documentation, and other Kora-owned visual surfaces.

### Visible timeout

- Default: **5 seconds**
- Range: **1 to 60 seconds**

The shared timer restarts whenever voice, typed, pointer, or keyboard
interaction occurs. If no further interaction occurs before the timeout, Kora
hides the constellation and any response window that is not set to **Always
show**. **Always show** affects only the response window; the constellation
continues to use this timeout.

### Constellation appearance

The three sliders apply immediately and are stored on this device:

- **Constellation size** - default **360 px**; range **240-600 px**.
- **Dot size** - default **100%**; range **50-200%**.
- **Dot movement speed** - default **100%**; range **25-200%**.

Changing the overall size keeps the constellation anchored to the bottom-right
of the active display's working area until you move it. Drag the visible
constellation to reposition it. Kora restores its last position when that
position remains on a connected display and otherwise falls back to the
bottom-right working area. Dot size changes particle diameter without changing
the number of particles. Movement speed scales the motion associated with each
assistant state without changing the state itself.

### Response window

Configure the response window's display behavior under **Appearance > Visual
feedback**:

- Drag the title area to reposition the window. Kora restores the last position
  when it remains on a connected display.
- **Always show** keeps the current response visible until **Dismiss** is
  selected. Default: off.
- **Stay on top** controls whether the response remains above other windows.
  Default: on.
- **Visible timeout** changes the shared 1-60 second timeout used by both the
  response window and constellation.

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

- The card reports whether Windows microphone access for desktop applications
  is **allowed**, **blocked**, or could not be confirmed.
- **System** follows the live Windows multimedia-default microphone.
- A named endpoint creates a pinned local override.
- Selection does not enable listening.

When access is blocked, select **Open Windows microphone settings** in Kora,
enable **Microphone access** and **Let desktop apps access your microphone**,
then refresh readiness.

### Voice activation

- Listening starts automatically when Kora starts and the microphone is ready.
- The status explains when listening is paused because Windows reports the
  session as locked, microphone access is blocked, call policy prevents
  activation, or listening was disabled manually.
- **Enable listening** reopens the selected microphone after manual disablement.
- **Disable listening** releases it.

Manual disablement is not persisted across application restarts; Kora returns
to its default listening behavior on the next start.

### Speech provider

- **Windows** is built in, uses installed SAPI voices, and requires no download.
- **Kokoro** is an optional local neural provider. Kora does not download it
  automatically.
- Select Kokoro and choose **Download** to retrieve its pinned model and voice
  assets. The download is approximately 219 MiB.
- Kora validates both assets by exact size and SHA-256 before installing them.
- Kokoro becomes available immediately after preparation; Kora does not need to
  restart.
- **Remove downloaded model** deletes Kokoro's device-local model and voices
  without affecting the built-in Windows provider.

Provider and voice selections are stored on this device.

### Speech voice

- Select a voice supplied by the chosen provider.
- **Preview** speaks a short identity phrase with the selected voice and output
  while wake listening remains active.
- **Stop** cancels active speech.

While speech is playing, start a command with the configured assistant name to
interrupt it, for example **"Kora, stop"** or **"Kora, open settings"**.
Unprefixed recognition is ignored during playback to reduce accidental
self-triggering.

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

### Effective output

Read-only explanation of the currently resolved mode and any forced visual
fallback.

## Calls

### Call detection

Read-only status from the configured detector. Automatic detection is currently
unavailable in the Windows bootstrap.

### Visual responses during calls

Default: on. Controls whether Active or Suspected calls override ordinary output
with visual responses.

### Voice activation during calls

Default: on. Turning it off closes capture when a call is detected and blocks
re-enabling until the call clears.

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

They do not roam with documentation, skills, or user content.
