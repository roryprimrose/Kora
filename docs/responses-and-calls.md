# Responses and detected calls

## Response modes

Kora supports three response modes:

- **Both audible and visual** - show text and speak when speech is available.
- **Audible only** - prefer speech without ordinary response text.
- **Visual only** - show text and do not speak.

Safety and recovery information remains visible even in VoiceOnly mode.

## Output precedence

Three scopes can control the effective mode:

1. **Current task** - highest precedence and temporary.
2. **Current queue** - temporary and used when no task override exists.
3. **Device default** - persisted locally.

Choose **Inherit** for the task or queue choice to return to the next broader
scope. Task and queue overrides are not retained after their scope ends.

## Visual response window

Drag the response title area to reposition the window. Its last position is
stored on this device and restored when that position remains on a connected
display.

Configure these controls under **Settings > Appearance > Visual feedback**:

- **Always show** - bypass the response timeout until **Dismiss** is selected;
- **Stay on top** - keep the response above other windows; on by default;
- **Visible timeout** - set the shared 1-60 second inactivity interval.

The presence always hides after the visible timeout. **Always show**
applies only to the response window.

## Forced visual output

Kora always displays text when:

- no compatible speech voice is available;
- no usable audio output is available;
- the output is muted or at zero volume;
- synthesis or playback fails;
- microphone capture is active;
- a response reports failure or safety information; or
- detected-call policy requires visual output.

This fallback overrides VoiceOnly so a failed spoken response is never lost.

## Interrupting spoken responses

Kora keeps wake listening active during previews and ordinary spoken responses.
Begin any supported command with the configured assistant name to interrupt
playback, for example **"Kora, stop"** or **"Kora, open settings"**. Kora stops
the current speech before executing the new command.

During playback, unprefixed recognition is ignored. Kora also suppresses
recognized phrases that occur in its own active speech text. This reduces
self-triggering, but real-world barge-in quality still depends on microphone and
speaker placement, headset use, and acoustic echo cancellation supplied by the
Windows audio device.

## Detected-call settings

When a configured detector reports an **Active** or **Suspected** call:

- **Visual responses during calls** defaults to on. Kora suppresses automatic
  response speech and shows text.
- **Voice activation during calls** defaults to on. Turn it off to close active
  capture and prevent listening until the call clears.

These settings are independent. You can allow listening while forcing visual
responses, or disable listening during calls.

The current Windows bootstrap reports automatic call detection as unavailable.
An open communications application alone is not treated as proof of a call.
