# Troubleshooting

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
