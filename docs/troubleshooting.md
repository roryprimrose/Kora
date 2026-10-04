# Troubleshooting

## Local storage or model setup needs attention

Open Settings > Readiness or use **what do you have left to do** to see the
current setup task and blocker. Kora recreates missing Kora-owned directories
and initializes its SQLite schema on startup or refresh. If SQLite fails its
integrity check, the existing database is retained; restore a known-good
backup rather than deleting it. The local Ollama probe checks only
`127.0.0.1:11434`. A responding runtime or installed model does not mean
Kora can use it for reasoning until a compatible adapter and model health
check pass. Select **Review local model setup** and approve the pinned
Ollama 0.35.1 / qwen3:1.7b plan to install and verify them. Setup can be
cancelled with **cancel task**. A model with the selected name but a
different digest is not replaced automatically; inspect it before taking
further action. Built-in commands remain deterministic after inference passes;
other requests are answered by the verified local model. If a running request
fails, Kora marks inference unavailable until you refresh readiness. There is
no cloud fallback.

## PowerShell 7 is missing or failed its check

Open **Settings > Readiness** or ask **what are you currently working on**.
The PowerShell 7 setup task reports whether `pwsh.exe` is missing, too old,
or failed its no-profile version check. Select **Review PowerShell 7 setup**
to approve a per-user winget installation, or **cancel task** to stop a
running install. Refresh readiness afterward. A package-manager success is
not sufficient: Kora must verify PowerShell 7.4 or later before reporting
the task complete. If a corrupt installation or package-manager failure
persists, review the reported error and repair the installation manually.
Kora does not run user-created scripts during this check.

## Kora cannot hear commands

1. Confirm an English Windows speech recognizer is installed.
2. Open Settings, then **Speech and audio**.
3. Confirm the microphone card says Windows desktop-app microphone access is
   allowed. If blocked, select **Open Windows microphone settings** in Kora,
   then enable microphone and desktop-app access.
4. Confirm **System** has an active Windows default microphone, or select a
   specific active microphone.
5. Read the Voice activation status. It identifies a locked-session safety
   pause, call-policy pause, or manual disablement.
6. Select **Refresh devices and readiness**.
7. If listening was manually disabled, select **Enable listening**.
8. Use one of the exact phrases in the command guide.

Kora normally starts listening automatically when voice readiness succeeds.

## The Windows default microphone changed

Leave the Kora microphone on **System**. System follows Windows default changes,
including changes during active capture. A specifically named microphone stays
pinned until you select System or another endpoint.

## Kora does not speak

Check:

- the effective response mode is not **Visual only**;
- listening is disabled;
- a compatible Windows voice is selected;
- System has an active Windows default output, or a specific output is selected;
- Windows output is not muted and volume is above zero; and
- detected-call policy is not forcing visual responses.

Use **Preview voice** to test the selected voice and output. Kora cannot always
detect powered-off speakers, disconnected analog cables, or unreported hardware
mute.

## The Windows default speaker changed

Leave Kora audio output on **System**. System follows Windows default changes,
including changes during active playback. A specifically named output remains
pinned.

## A saved device disappeared

Kora does not replace a pinned device silently. Open Settings and choose:

- **System** to return to Windows default routing; or
- another explicit endpoint.

## No speech voice is available

For the Windows provider, install a Windows text-to-speech voice or language
speech pack, then refresh readiness. For Kokoro, select the provider and choose
**Download**. It becomes available after validation and preparation without a
restart. Typed commands and visual responses remain available.

Kokoro is optional and is not queued automatically. The first-run offer opens
Settings only if you choose to review it; downloading still requires selecting
**Download**. If a previously selected provider disappears, Kora offers a
recovery prompt once per loss episode rather than downloading it silently.

If a Kokoro download fails, Kora leaves it unavailable and removes temporary
asset files. Check network access and free space, then try Download again.
Kora will not activate an asset that has the wrong size or SHA-256 digest.
Validation errors identify the affected asset and report both the expected and
received digests so a release-pin error can be distinguished from a modified
or corrupted download.

## VoiceOnly still shows text

This is expected when speech is unavailable, playback fails, capture is active,
a detected call forces visual output, or Kora must show safety or recovery
information.

## Call settings appear inactive

Automatic call detection is not implemented in the current Windows bootstrap.
The policy is ready for a supported detector, but an open communications
application is not treated as proof of a call.

## Open Kora after hiding it

Single-click the tray icon or use **Show Kora** from its right-click menu.
Double-click opens Settings.

## Find diagnostic logs

Open:

`%LOCALAPPDATA%\Kora\Logs`

Logs are structured JSON, roll daily, and are retained for up to 30 days.
