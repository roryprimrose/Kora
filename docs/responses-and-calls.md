# Responses and manual call mode

## Response modes

Kora supports three response modes:

- **Both audible and visual** - show text and speak when speech is available.
- **Audible only** - prefer speech without ordinary response text.
- **Visual only** - show text and do not speak.

Safety and recovery information remains visible even in VoiceOnly mode.

**Settings > Responses > Muted speaker fallback** controls whether audible-only
responses fall back to text when the selected Windows output is muted or at zero
volume. It is **enabled by default** and saved on this device. The original
response is displayed, not replaced by a mute warning. Kora checks output
availability before each response, so unmuting restores the configured response
mode on the next response without a manual refresh. This applies to both
**System** output and a selected speaker.

Turning the option off leaves ordinary audible-only responses hidden while the
speaker is muted. Failures, pending questions, approvals, and other safety or
recovery information still remain visible.

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
- **Response timeout** - set the response window's 1-60 second inactivity
  interval, default **5 seconds**.

Presence has a separate **Presence timeout** setting under **Appearance >
Presence appearance**, default **10 seconds**. It automatically hides after
inactivity while idle or listening without a pending prompt, but remains
visible for work, speech, and required attention. **Always show** applies only
to the response window.

Responses can include underlined action links below their message. These links
invoke only the specific in-app action attached to the current response, such
as opening the relevant Settings tab. They are separate from question choices
and approval controls, disappear when the response is dismissed or replaced,
and keep the response visible until selected or dismissed.

The bounded passive **Open details** viewer is currently available from
Documentation for an exact embedded page. It is not yet general compact
response routing or durable response history. Model prose cannot open/focus
that viewer, and viewer close does not cancel or approve the current request.
See [passive document details](windows-and-tray.md#passive-document-details).

## Forced visual output

Kora always displays text when:

- no compatible speech voice is available;
- no usable audio output is available;
- the output is muted or at zero volume and **Muted speaker fallback** is enabled;
- synthesis or playback fails;
- microphone capture is active;
- a response reports failure or safety information; or
- detected-call policy requires visual output.

These fallbacks override VoiceOnly. Only the muted-output fallback can be
disabled with **Muted speaker fallback**.

## Interrupting spoken responses

No ambient command recognizer runs during playback. Explicit push-to-talk stops
Kora speech before opening capture. **Stop speaking** is also available in the
tray without model, network or recognition. Production wake and acoustic
barge-in/playback-rejection quality require separate real-hardware proof.

## Manual call mode and call settings

In **Settings > Calls**, choose **I'm in a call** to protect this run. Choose
**Clear manual call mode** when it ends. Manual mode is not saved across
restart. The status keeps automatic availability separate: this build still
reports **automatic call detection unavailable**, including while manual mode
is active. It does not detect Teams or any other communication provider.
Clearing manual mode does not clear an enabled automatic Active, Suspected or
Unknown observation, nor fabricate detector Clear.

When manual mode or an enabled Active, Suspected or Unknown observation applies:

- **Visual responses during calls** defaults to on. Kora suppresses automatic
  response speech and shows text.
- **Voice activation during calls** defaults to on. Turn it off to close active
  capture and prevent listening until the call clears.

These settings are independent. You can allow listening while forcing visual
responses, or disable listening during calls. Existing saved output/activation
choices are retained. New protection downgrades are disabled pending complete
exact trusted review; temporary and speak-once call exceptions are unavailable.
Enabling visual protection or disabling call-time activation remains supported.

Protected-call entry invalidates pending synthesis/playback before slower UI
work. Results and questions stay visual; clearing protection does not replay
old speech, answer prompts, or automatically reopen the microphone.

Voice-originated voice/in-call setting changes, including manual clear/reset,
are rejected while protected. A later mouse confirmation does not change their
original voice origin. Start a **new UI change** instead. Manual controls
recheck call revision, ownership and Windows privacy before applying; stale or
denied changes are not deferred. Status, stop speech, disable listening and
cancellation remain available without a call-option mutation.

Session/Always model-action grants are ignored, not removed, during protected
calls. A current Once approval retains its existing action-name-only authority.
Call revision changes invalidate pending approvals and undispatched reuse.

The current Windows bootstrap reports automatic call detection as unavailable.
An open communications application alone is not treated as proof of a call.
Native accessibility, real-call detection, acoustic leakage and output-stop
timing still require separately approved acceptance.
