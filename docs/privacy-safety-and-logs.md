# Privacy, safety, and logs

## Local speech

Built-in speech recognition uses the installed Windows recognizer and a
host-owned fixed grammar. The built-in text-to-speech provider uses installed
Windows SAPI voices. Kora does not need a cloud account or network connection
for these features.

Kokoro is an optional local neural text-to-speech provider. Kora makes no
Kokoro network request unless the user explicitly selects **Download**. That
action downloads a pinned model and voice archive from a fixed GitHub release,
validates both with SHA-256, and stores them under
`%LOCALAPPDATA%\Kora\Speech\Kokoro`. Spoken response text is not sent to GitHub
or another service; synthesis runs locally after installation.

## Microphone consent

Device discovery and selection do not record audio by themselves. Kora opens
capture automatically on startup when voice readiness succeeds because
listening is its primary interaction mode. **Disable listening** releases the
microphone for the current run. Listening is also closed on exit and before
Kora locks Windows.

## Visual safety fallback

Failures, safety information, and unavailable speech are always visible. A
VoiceOnly preference cannot hide:

- missing voice or audio output;
- mute and playback failure;
- command or dependency failures;
- protected power proposals; or
- other required recovery information.

## Power and session actions

**Lock the machine** is a real local Windows action. Kora releases microphone
capture before locking.

Shutdown and computer restart commands are non-destructive proposals in the
current release. They are recognized, displayed, auditable, inspectable, and
cancellable, but no operating-system power request is sent.

## Logs

Kora writes structured JSON logs under:

`%LOCALAPPDATA%\Kora\Logs`

There is one rolling file per day named `kora-YYYYMMDD.log`. Kora retains up to
30 days and 30 files.

Logs support diagnostics and future local reasoning, but intentionally omit:

- spoken or typed command text;
- recognized transcripts;
- response bodies and spoken text;
- raw audio;
- credentials and secrets; and
- preference values such as the selected assistant name or endpoint ID.

Configuration writes, application and script execution categories, approvals,
protected operations, and their outcomes use typed audit events without raw
sensitive content.

## Local preferences

Preferences are stored under:

`%LOCALAPPDATA%\Kora\Preferences`

They are device-local and do not roam with documentation or future skills.
