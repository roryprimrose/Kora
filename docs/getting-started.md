# Getting started

## Requirements

- Windows 10 build 19041 or later. Windows 11 is recommended.
- An English Windows speech-recognition language for voice commands.
- An installed Windows text-to-speech voice, or the optional downloaded Kokoro
  provider, for spoken responses.
- A microphone for voice activation.
- An active Windows audio output for speech.

Typed commands and visual responses remain available when microphone or speech
components are unavailable.

## First launch

Kora performs a readiness check and discovers:

- active Windows microphones;
- the Windows multimedia-default microphone;
- available speech providers and voices;
- active Windows audio outputs;
- the Windows multimedia-default audio output;
- local application-data storage;
- the local SQLite database (created and checked on each startup);
- PowerShell 7 (`pwsh.exe`) in a supported installation location, verified in
  a no-profile process;
- a loopback Ollama endpoint and its model catalogue; and
- available Windows speech dependencies.

The Readiness tab lists setup tasks and their current results. The built-in
**what are you currently working on** and **what do you have left to do**
commands report blockers without requiring a language model. Kora initializes
its own SQLite schema automatically, but preserves an existing database if an
integrity check fails. If local inference is not ready at startup, Kora opens
Settings on the Readiness tab so you can inspect the problem and decide whether
to run setup. Ollama is never installed
automatically: select **Review local model setup** on the Readiness tab,
review the download and machine changes, and explicitly approve the pinned
Ollama 0.35.1 per-user install and qwen3:1.7b model (about 1.36 GB).
Kora reuses a healthy existing runtime and model, verifies the model's digest,
and tests local inference before marking it ready. Exact built-in commands
always take precedence. Once the model is ready, other typed requests and
voice requests prefixed with the active assistant name receive a local model
answer or a suggestion to use an existing built-in action. Kora validates suggested actions; disruptive model suggestions require an
on-screen approval unless already granted. Direct exact commands, including
locking Windows, currently use their C# handlers without that confirmation.

PowerShell 7 is a separate required setup task for future script-backed
actions. Kora checks it on startup and refresh, including after a previously
working installation disappears or fails. If missing or unhealthy, open
**Settings > Readiness > Review PowerShell 7 setup** and approve the
per-user `Microsoft.PowerShell` installation through winget. Kora reuses a
healthy PowerShell 7.4-or-newer runtime without installing anything, and
verifies the executable after setup. This never approves or runs a skill
script. PowerShell readiness does not block local-model reasoning or exact
built-in commands. Built-in commands still use C# handlers; the
[skill and task execution design](skill-and-task-execution-design.md)
describes the planned script runner and content-bound grants.

The initial microphone and speaker setting is **System**. System is not a saved
endpoint snapshot. It follows later Windows default-device changes, including
changes during active capture or playback.

When voice readiness succeeds, Kora starts listening automatically even if
local inference or PowerShell setup still needs attention. If no usable
microphone or recognizer is available, Kora remains visual and reports what
needs attention.

On first launch, if the optional Kokoro speech provider is available but not
installed, Kora asks whether you want to review it in Settings. Declining
does not add a setup task or start a download. Kora asks again only if a
previously selected provider later becomes unavailable; after you respond,
it waits until that provider has recovered and is lost again before repeating
the recovery prompt. The Windows speech provider remains the default.

## Enable listening

1. Confirm the microphone shown in Settings.
2. Wait for the Listening state after startup.
3. Say a supported phrase such as **"Kora, what can you do?"**.
4. Select **Disable listening** when you want to release the microphone.
5. Select **Enable listening** to resume capture during the current run.

Selecting or refreshing a microphone after manual disablement does not reopen
capture by itself.

## Use typed commands

The typed command box uses the same deterministic command router as speech:

1. Enter a supported command without the assistant name, such as **help**.
2. Select **Run command**.
3. Review the visible response and constellation state.

Typed commands are useful when the microphone, speech recognizer, or listening
permission is unavailable.

When the local model is verified, you can also type a question that is not a
built-in command, such as **"why is the sky blue?"**. For voice, say
**"Kora, why is the sky blue?"** (or use your configured assistant name).
The request text, built-in action descriptions, and a limited snapshot of
readiness and task progress are sent to Ollama on `127.0.0.1`; Kora does not
add clipboard contents, files, logs, web access, or earlier conversations.
The model can suggest a built-in action, but it cannot invoke arbitrary APIs.
When an action needs approval, Kora displays the request even in VoiceOnly
mode, speaks it when speech is available, and briefly pauses listening while
speaking to avoid hearing itself. Choose **Once**, **This session**,
**Always**, or **Reject** on screen. You can also say **"Kora, approve once"**,
**"Kora, approve for this session"**, **"Kora, always allow this"**, or
**"Kora, reject"** after Kora resumes listening. The name is required by
default; the **Approvals** settings tab can allow unprefixed replies.
Session approval lasts until Kora exits, restarts, or locks Windows through
Kora. Always approval is stored on this device for that named action until
you revoke it in Settings; grants are not tied to script or executable
hashes. Dismissing the request rejects it.
Say **"cancel task"** to interrupt a running answer. If local inference fails,
Kora reports it and requires a readiness refresh before accepting more
model requests. There is no cloud fallback.

## Preview speech

1. Choose **Windows** or **Kokoro** as the speech provider.
2. If Kokoro is not installed, select **Download** and wait for preparation.
3. Choose a voice.
4. Choose **System** or a specific audio output.
5. Select **Preview**.
6. Select **Stop** if needed.

Wake listening remains active during preview. Kora forces visual text when
preview or response playback cannot be delivered.

## Keep Kora available

Kora starts with its transparent constellation hidden after successful voice
readiness and keeps listening in the background. The constellation and compact
response surface appear only during interaction or when Kora has information to
provide. Closing Settings, Documentation, or the response surface leaves Kora
running. Use the tray icon to show Kora again. Choose **Exit Kora** from the
tray to stop the application and release audio resources.

Drag the response title area to place it where you want it. Its pinned controls
can keep the current response visible until dismissed, change the shared visible
timeout, or disable the default stay-on-top behavior.

You can also drag the visible constellation itself. Its position is retained
across restarts while the selected display remains connected.
