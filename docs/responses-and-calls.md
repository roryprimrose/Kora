# Responses and manual call mode

## Response modes

Kora supports three response modes:

- **Both audible and visual** - show text and speak when speech is available.
- **Audible only** - prefer speech without ordinary response text.
- **Visual only** - show text and do not speak.

Safety and recovery information remains visible even in VoiceOnly mode.

Ordinary speech is additionally admitted only when its complete title/body
fits the device-local spoken summary caps (default **3 sentences / 80 words**).
Either cap can be lowered in Speech & audio. Over-cap results retain their full
visual text, with an explicit **Speech withheld** status even in Audible only
mode; there is no truncation, extra model call or replay.
Required questions/options and exact security-sensitive readback keep their
existing mandatory bounds and privacy gates, not ordinary-summary truncation.
See [the exact counting contract](settings.md#spoken-summary-limits).

The independently stored legacy **Muted speaker fallback** preference remains
outside the delivered mode registry. Its default and storage are unchanged;
it cannot suppress mandatory complete visual recovery for missing, muted,
zero-volume, unavailable or failed output. The original response remains available,
not just a warning. A later eligible response uses the freshly resolved output;
unmuting or changing configuration never replays retired speech. This applies
to **System** and a selected speaker. See [the existing fallback boundary](settings.md#muted-speaker-fallback).

## Output precedence

Response selection has this precedence, before all mandatory policy gates:

1. **In-call feedback** - independent device-local UI default, applied only to
   effective Active/Suspected (including manual Active), unless Inherit.
2. **Current task** - temporary.
3. **Current queue** - temporary and used when no task override exists.
4. **Session** - shared resolver seam; no new session control is delivered.
5. **Device default** - persisted locally.

Choose **Inherit** for the task or queue choice to return to the next broader
scope. These presentation controls are outside the admitted device-default
registry, not delivered durable session/task/queue configuration or execution.
The device default and independent in-call preference have shared admitted
native/exact workflows; neither changes call policy or the legacy fallback.

In-call **Voice / UI / Both** map to audible/visual/both preference, never
speech permission. **Inherit** restores ordinary selection. Unknown/invalid call
evidence does not activate feedback but always withholds speech and requires
full visual recovery; Clear/Unavailable uses ordinary output. A Voice/Both choice
cannot bypass independent call suppression, privacy/lock/capture, mute/zero,
native output lifetime or mandatory safety/question/approval/recovery previews.
See [Settings and explicit recovery](settings.md#in-call-feedback-override) and
[exact commands](commands.md#in-call-feedback-override).

## Visual response window

Optional [local speech text](settings.md#local-speech-text) is separate from the
answer panel. It defaults to Off and shows only exact host-admitted current
utterance playback, never queued/failed/suppressed text. It clears immediately
when playback or its response/privacy/call/ownership generation is retired.
Visual-only and call-gated responses use the existing full answer panel, not a
fictitious playback caption. Captions cannot answer questions, grant approval,
replay speech or authorize any action; required native recovery is independent.
No caption content is saved, logged or sent to a model.
If the native caption surface fails, captions stop until restart; the complete
answer and required recovery remain visual. Diagnostics record only the failure
type, never caption content.

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
- the output is muted, at zero volume or otherwise unavailable;
- synthesis or playback fails;
- microphone capture is active;
- a response reports failure or safety information; or
- detected-call policy requires visual output.

These mandatory recovery paths override VoiceOnly; the separately stored legacy
preference cannot disable complete visual recovery or exact pending previews.

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

**Reset manual off** has the same narrow clearing effect. **Cached call status**
shows the current run flag, independent automatic observation/availability and
immutable policy/source revision, without probing or starting work. The same
[exact manual commands](commands.md#exact-current-run-manual-call-control) use
the current prefix and genuine original-user admission. Results do not speak
or replace an existing question, approval or security preview; mutation is
refused while those interactions are pending.

When manual mode or an enabled Active, Suspected or Unknown observation applies:

- **Call speech suppression** defaults to on. Kora suppresses automatic
  response speech and shows text.
- **Voice activation during calls** defaults to on. Turn it off to close active
  capture and prevent listening until the call clears.

These settings are independent. You can allow listening while forcing visual
responses, or disable listening during calls. Existing saved output/activation
choices are retained. New protection downgrades are disabled pending complete
exact trusted review; temporary and speak-once call exceptions are unavailable.
Enabling visual protection or disabling call-time activation remains supported.
The new **In-call feedback override** is independent of both. UI is its default;
even a legacy suppression-Off preference does not disable that UI selection.
Unknown/invalid detector evidence always suppresses speech. Feedback changes
never enable input, clear calls, grant consent/permission or edit reusable grants.

Protected-call entry invalidates pending synthesis/playback before slower UI
work. Results and questions stay visual; clearing protection does not replay
old speech, answer prompts, or automatically reopen the microphone.
Every changed manual layer also retires already admitted input/callback and
output generations, including old requests finishing after off/reset. Complete
late results remain visual; enable listening explicitly before a fresh PTT.
Required audit/receipt failure leaves the cached manual flag truthful and
holds conservative evidence-unavailable protection instead of claiming
rollback or authorizing speech/activation/reuse from unknown state.

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
