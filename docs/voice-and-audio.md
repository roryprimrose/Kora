# Voice, microphone, and speakers

## Microphone selection

The Settings microphone card reports the current Windows privacy state
separately from device selection. An available **System** or named endpoint does
not by itself prove that desktop microphone access is allowed.

The microphone list contains:

- **System** - follows the live Windows multimedia-default microphone;
- each active Windows capture endpoint - pins Kora to that endpoint.

Choosing a specific microphone stores its stable Windows endpoint ID as a local
Kora override. Choosing **System** removes that override. A missing pinned
microphone is shown as unavailable and is not silently replaced by a same-name
or newly-default device.

Changing the Windows default while System is selected automatically reroutes
active WASAPI capture. It does not reopen capture after listening has been
manually disabled for the current run.

## Voice activation

Kora enables listening automatically during startup when the selected
microphone and local recognizer are ready. **Enable listening** reopens the
selected microphone after manual disablement and loads the fixed local
recognition grammar. **Disable listening** closes capture.

Kora stops capture when:

- you disable listening;
- Kora exits;
- the Windows session is locked through Kora;
- detected-call policy disables voice activation; or
- capture fails.

If Windows reports microphone access as blocked, enable **Microphone access**
and **Let desktop apps access your microphone** in **Settings > Privacy &
security > Microphone**. If access is allowed but listening remains paused,
check the Voice activation status; Kora also blocks automatic capture when
Windows reports the current session as locked or the interactive input desktop
is unavailable.

Changing the assistant name while listening restarts the existing capture
session with the updated grammar. It does not grant new consent.

For a model-suggested action that needs approval, Kora always shows the
request and can speak the question. It releases microphone capture while
speaking, then resumes listening for **approve once**, **approve for this
session**, **always allow this**, or **reject**. Say the assistant name first
unless you turned off that requirement under **Settings > Approvals**.

## Speech providers

Kora always includes the **Windows** provider. It uses installed SAPI voices and
does not require a model, account, or network connection.

**Kokoro** is an optional local neural provider. Its inference support is part
of Kora, but its model and voices are not installed or downloaded by default.
To enable it:

1. Open **Settings > Speech & audio**.
2. Select **Kokoro** under **Speech provider**.
3. Select **Download**.
4. Leave Kora running while it downloads, verifies, installs, and prepares
   approximately 219 MiB of assets.

Kora pins the asset version, size, and SHA-256 digest. An incomplete, modified,
or unexpected download is rejected rather than activated. After preparation,
Kokoro's voices appear immediately without an application restart. Synthesis
then runs on this device and continues to use the selected Windows audio output.

Selecting a provider that still needs to be downloaded does not interrupt the
working speech provider. Kora continues using the current installed voice for
download results and failures. After a successful installation, Kora activates
the downloaded provider and its default voice immediately.

Use **Remove downloaded model** to delete the optional assets. This leaves the
Windows provider intact.

## Speech voice selection

Kora never silently downloads a provider or voice. Without a saved voice
choice, Kora ranks compatible voices from the selected provider in this order:

1. female voice matching the exact Windows profile locale;
2. female voice from the same language family;
3. male voice matching the exact locale;
4. male voice from the same language family;
5. compatible neutral voice;
6. compatible voice with unspecified gender.

Kora does not automatically select an unrelated language. An explicit voice
selection is stored locally and remains selected while that voice is installed.

If the Windows provider has no voice, install a Windows text-to-speech voice
through Windows Settings. If Kokoro is selected but absent, download it from
Kora Settings. Typed commands and visual output continue to work.

Select **Preview** to test the chosen voice and output device. Wake listening
remains active during previews and ordinary spoken responses.

To interrupt speech, begin any supported command with the configured assistant
name, for example **"Kora, stop"** or **"Kora, open settings"**. Kora stops the
current playback before executing the new command. Unprefixed recognition and
phrases detected in Kora's own active speech are ignored during playback to
reduce self-triggering. Results depend on the microphone, speaker placement,
headset use, and any acoustic echo cancellation supplied by the Windows audio
device.

## Audio output selection

The audio output list contains:

- **System** - follows the live Windows multimedia-default output;
- each active Windows render endpoint - pins Kora to that endpoint.

Choosing System removes a saved Kora speaker override. Changing the Windows
default while System is selected automatically reroutes active WASAPI playback.
A pinned endpoint is never silently replaced.

## Mute and playback failures

Kora treats these conditions as unavailable speech output:

- no active selected endpoint;
- no Windows default while System is selected;
- removed or disabled pinned endpoint;
- Windows software mute;
- Windows endpoint volume of zero;
- endpoint-open failure; or
- playback failure.

Kora does not change global Windows mute or volume. It displays the response as
text instead.

Windows cannot reliably detect every physical failure. Speakers may be powered
off, disconnected after an analog output, or muted by hardware without Windows
reporting it.
