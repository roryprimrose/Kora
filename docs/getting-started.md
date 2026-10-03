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
- local application-data storage; and
- available Windows speech dependencies.

The initial microphone and speaker setting is **System**. System is not a saved
endpoint snapshot. It follows later Windows default-device changes, including
changes during active capture or playback.

When readiness succeeds, Kora starts listening automatically. If no usable
microphone or recognizer is available, Kora remains visual and reports what
needs attention.

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
