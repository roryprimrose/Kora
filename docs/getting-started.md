# Getting started

You can use Kora without an AI account or a microphone. Start with **help** in
the typed command box, then configure voice only if you want it.

**AI answers are unavailable in this build.** Built-in commands, speech,
local previews and manual editors remain available. Model setup can check or
install components, but does not enable an AI model for this release.

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
- built-in local SQLite storage (the bundled engine and Kora database are
  initialized and checked on each startup; there is no separate SQLite install);
- PowerShell 7 (`pwsh.exe`) in a supported installation location, verified in
  a no-profile process;
- a loopback Ollama endpoint and its model catalogue; and
- available Windows speech dependencies.

The Readiness tab lists setup tasks and their current results. The built-in
**what are you currently working on** and **what do you have left to do**
commands report blockers without requiring a language model. Kora initializes
its own SQLite schema automatically, but preserves an existing database if an
integrity check fails. SQLite is part of Kora rather than an optional setup
choice. If its packaged native component cannot load, the installation is
reported as broken; Kora does not download SQLite or use an arbitrary
machine-installed copy. If local inference is not ready at startup, Kora opens
Settings on the Readiness tab so you can inspect the problem and decide whether
to run setup. Ollama is never installed
automatically: select **Review local model setup** on the Readiness tab,
review the download and machine changes, and explicitly approve the pinned
Ollama 0.35.1 per-user install and qwen3:1.7b model (about 1.36 GB).
Kora reuses healthy components and checks the expected model files. Passing
setup is not permission or release approval to generate answers: this build
still reports model use as **Unavailable**. See [Models](settings.md#models).
Exact built-in commands take precedence and need no model. **Lock Windows
commands act directly**, without model-action confirmation; do not use them
just to test voice recognition.

PowerShell 7 is a separate required setup task for future script-backed
actions. Kora checks it on startup and refresh, including after a previously
working installation disappears or fails. If missing or unhealthy, open
**Settings > Readiness > Review PowerShell 7 setup** and approve the
per-user `Microsoft.PowerShell` installation through winget. Kora reuses a
healthy PowerShell 7.4-or-newer runtime without installing anything, and
verifies the executable after setup. This never approves or runs a skill
script. PowerShell readiness does not block exact built-in commands.
The [tools guide](tools-and-built-in-skills.md) explains what works now; the
[skill and task execution design](skill-and-task-execution-design.md)
describes future script execution, not a setup requirement for ordinary use.

The initial microphone and speaker setting is **System**. System is not a saved
endpoint snapshot. It follows later Windows default-device changes, including
changes during active capture or playback.

First launch requires explicit saved voice consent before voice can be enabled.
Kora greets you using the current Windows profile's local display name when it
is available. This lookup stays on the device and falls back to the local
account name; if neither yields a usable name, the greeting remains generic.
The greeting is visual because Kora does not speak or open the microphone
before consent. It distinguishes Kora's voice-consent choice from Windows
microphone access and explains either gate when attention is required. Response
actions appear as links below the message. Select **Review voice settings** to
open **Settings > Speech & audio** directly; Kora does not open Settings until
you choose that action.
With saved consent, safe startup arms push-to-talk even if
local inference or PowerShell setup still needs attention. It does not open
ambient capture: production wake is unavailable. If no usable
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
2. Review and grant ongoing voice consent, or continue without voice.
3. Enter commands in the response window, not Settings. When the response
   window offers its **Push to talk** microphone control, hold it, say a
   supported phrase such as **"Kora, what can you do?"**, then release to finish.
4. Select **Disable listening** when you want to release the microphone.
5. Select **Enable listening** to re-arm push-to-talk after a recovery hold.

Selecting or refreshing a microphone after manual disablement does not reopen
capture by itself.

## Use typed commands

The typed command box accepts the same exact phrases as voice:

1. Enter a supported command without the assistant name, such as **help**.
2. Select **Run command**.
3. Review the visible response and presence state.

Typed commands are useful when the microphone, speech recognizer, or listening
permission is unavailable.

Use [Commands](commands.md) for accepted phrases. An unmatched question such as
**"why is the sky blue?"** does not currently receive an AI answer, and Kora
never silently switches to a cloud service. [Model settings](settings.md#models)
explain the difference between installation, permission and availability.

To keep work in a named conversation, open [Sessions](windows-and-tray.md#expand-and-collapse-the-interaction-window).
Its composer is separate from the command box: **Submit exact message** saves
an accepted message and its outcome, rather than treating its words as commands.
With the current unavailable provider, that outcome is a Kora status, not an
AI reply. Unsent text stays in memory per conversation and is lost on exit,
forced termination or privacy clearing; it is not automatically saved.

## Preview speech

1. Choose **Windows** or **Kokoro** as the speech provider.
2. If Kokoro is not installed, select **Download** and wait for preparation.
3. Choose a voice.
4. Choose **System** or a specific audio output.
5. Select **Preview**.
6. Select **Stop** if needed.

No ambient recognition runs during preview. Kora forces visual text when
preview or response playback cannot be delivered.

## Keep Kora available

Kora starts with its transparent presence hidden after successful voice
readiness and keeps its native controls available in the background. The presence and compact
response surface appear only during interaction or when Kora has information to
provide. Closing Settings, Documentation, or the response surface leaves Kora
running. Use the tray icon to show Kora again. Choose **Exit Kora** from the
tray to stop the application and release audio resources.

Drag the response title area to place it where you want it. Its pinned controls
can keep the current response visible until dismissed or disable the default
stay-on-top behavior. Settings > Appearance provides separate **Response timeout**
(default 5 seconds) and **Presence timeout** (default 10 seconds) controls.
Idle or listening presence hides automatically without asking when no prompt
needs attention; active work, speech, and required attention keep it visible.

The presence lets mouse events pass through to the window underneath by default.
To move it, **hold Ctrl, then left-click and drag the visible presence**.
Release the mouse button and Ctrl when finished. Its position is retained
across restarts while the selected display remains connected.

## Obtain source tools without installing

**Optional, for people building Kora from source.** Skip this section when using
an existing installation. This is not an in-app installer or updater.

From a separately reviewed checkout, use
[Resolve-SourceTools.ps1](../eng/Resolve-SourceTools.ps1) with PowerShell 7 and
GitHub CLI:

```powershell
.\eng\Resolve-SourceTools.ps1                          # Metadata-only stable selection
.\eng\Resolve-SourceTools.ps1 -Channel preview         # Explicit published-prerelease opt-in
.\eng\Resolve-SourceTools.ps1 -Channel preview `
    -Revision '<reviewed-full-lowercase-40-character-commit>' `
    -Action Acquire -OutputDirectory 'C:\KoraToolReview\candidate-01'
```

Replace the placeholder with the exact reviewed commit. Acquisition verifies
the downloaded files against that source and does not execute them. Missing
published candidates fail rather than using a newer development branch.
Review the acquired code and its dependencies before choosing
`-Action Build -TrustBuildCode`; building needs Git and the source-pinned .NET SDK.
Checksums do not independently prove who published a file.

See the [complete build interface and trust limits](../Design/Distribution_And_Updates.md#immutable-tool-acquisition-and-channel-resolution).
This route builds and stages files only; it does not install, activate, elevate
or launch Kora.
