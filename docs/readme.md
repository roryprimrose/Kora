# Kora user guide

Kora is a Windows-first, local-first voice assistant. The current release can
recognize a fixed set of local voice commands, speak responses with installed
Windows voices or an explicitly downloaded local Kokoro provider, accept typed
commands, manage its audio and response settings, lock Windows, and prepare
non-destructive shutdown or restart proposals.

No model, cloud account, or network connection is required for the built-in
tasks and their exact command variants in the
[voice and typed commands](commands.md) guide. A separately approved local
model can answer unmatched requests, ask a bounded clarification question with
spoken or clickable choices, or suggest a named built-in action. Kora validates
the action and asks for approval before disruptive ones. Choosing an answer to
a question never grants permission to execute an action.

## Start here

1. Open Kora.
2. Review the readiness information.
3. Leave the microphone and audio output on **System** to follow the Windows
   defaults, or choose specific devices.
4. Kora starts listening automatically when the selected microphone is ready.
5. Say **"Kora, what can you do?"** or enter a command in the typed command box.
6. Select **Disable listening** whenever you want Kora to release the microphone.
7. Use **Settings** to change the theme, assistant name, speech provider,
   voice, devices, response output, and detected-call behavior.

Listening is Kora's default startup behavior and primary interaction mode. A
device change by itself does not open capture after you have disabled listening
for the current run; use **Enable listening** to resume it.

## Guide

- [Getting started](getting-started.md)
- [Voice, microphone, and speakers](voice-and-audio.md)
- [Responses and detected calls](responses-and-calls.md)
- [All settings](settings.md)
- [All built-in tasks and command variants](commands.md)
- [Windows, tray, and appearance](windows-and-tray.md)
- [Privacy, safety, and logs](privacy-safety-and-logs.md)
- [Skill and task execution design (planned)](skill-and-task-execution-design.md)
- [Troubleshooting](troubleshooting.md)

## Open this guide

- Right-click the Kora tray icon and select **Documentation**.
- Say **"Kora, open documentation"** while listening is enabled.
- Enter **open documentation** in the typed command box.

If you renamed the assistant, use the configured name instead of Kora.

## Current limitations

- Built-in actions use an exact phrase grammar, not fuzzy matching. Unmatched
  typed requests and assistant-name-prefixed voice requests can use the
  verified local model for answers or suggestions from the built-in action
  list. Disruptive suggestions require an on-screen approval.
- Automatic call detection is not currently available. The call-aware settings
  take effect when a supported detector reports an Active or Suspected call.
- Kora does not speak while its microphone is actively capturing because
  playback rejection is not yet implemented.
- Shutdown and computer-restart commands create visible proposals only. They do
  not send a power request to Windows.
- Physical speaker failures beyond Windows cannot always be detected. A powered
  off speaker or disconnected analog cable may not be visible to Kora.
