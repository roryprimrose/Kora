# Ambient UI study

Status: native transparent presence and separate response surface
implemented; advanced approval, placement customization, and fullscreen
suppression remain future work.

Open [the interactive prototype](Prototypes/ambient-ui.html) in a modern browser. It is
self-contained, works offline, and uses no external assets, dependencies, telemetry,
microphone, clipboard, or runtime. Controls and task results are simulations.

## Presence terminology

**Presence** is the name of Kora's abstract desktop state-feedback surface.
It appears when needed and communicates the assistant's state through colour
and motion, without becoming a dashboard or a percentage-complete indicator.
The **particle cloud** is its current visual treatment; ribbon orbit and signal
lattice are alternative treatments of the same presence, not different features.
The presence remains distinct from response windows, speech text, native
approval/error panels, the tray icon, and microphone status. Hiding it changes
presentation only; it does not mute listening or stop work.

## Visual directions

1. **Particle cloud (recommended):** a small, original three-dimensional particle
   swarm. Each dot has its own position, velocity, random steering changes, and
   occasional short dart, like an individual insect in a collective. Gentle
   attraction and local separation keep the cloud loosely gathered, not arranged
   on a sphere or rotating as one object. No connecting lines or synchronised
   flicker. Motion is faster during calculation/execution, slower while waiting
   or informing, settles on success, and stops on failure. Reduced motion freezes
   the current arrangement without jumping to a different shape.
2. **Ribbon orbit:** four fine moving curves form an open sphere. Less shimmer,
   softer movement, and a quieter peripheral footprint.
3. **Signal lattice:** a sparse rotating cubic lattice with travelling node
   highlights. A restrained geometric alternative to cinematic compiler visuals.

The desktop preview initially shows Calculating so the design can be evaluated.
Select Hidden to inspect the intended runtime default: no visible idle object.

## Placement and shell behaviour

- Native default: bottom-right of the primary display's **working area**, with a
  proposed 24-DIP margin. Use the OS-reported working area, not a hard-coded taskbar
  height; this covers taskbars at other edges and display scaling.
- A small transparent, undecorated, topmost presence surface. No rectangular
  background behind the animation. Results/decisions may unfold into a readable
  translucent surface; purely transparent text over arbitrary content is unreliable.
- The presence HWND is mouse click-through by default, including the visible
  dots. Either Ctrl key temporarily makes it interactive, even while another
  application has focus; Ctrl + left-drag repositions it without activating it.
  While interactive, hovering over the presence uses the four-way move cursor;
  pressing Ctrl updates it even without a pointer move, never over another window.
  Releasing Ctrl returns to click-through after any in-progress mouse gesture
  finishes. Gestures beginning underneath must not transfer to Kora mid-press.
  Only Ctrl and mouse-button states are polled; no global keyboard hook or
  input forwarding is used. Hidden or privacy-ineligible presence stays
  non-interactive regardless of Ctrl. Native routing failures hide the presence
  and surface recovery through the separate response UI.
- Hidden means no animation, input region, focus capture, or invisible hot corner.
  Summon primarily by saying an active name ("Kora" by default) while local wake listening is available.
  Optional push-to-talk, a keyboard shortcut, or a tray command provide alternatives.
  Hiding the presence is not microphone mute: a persistent tray/status indicator must
  distinguish Wake Listening, Muted, and Unavailable and expose an explicit mute control.
  It also opens native questions and microphone selection/recovery by mouse under
  [Interaction Fallback](Interaction_Fallback.md); voice must not be required to restore voice.
  The system tray icon/context menu is the primary non-voice recovery route, with
  detected microphone selection, Disable listening while capture is active,
  Enable listening after manual disablement or failure, and accessible state.
  Clicking the icon never implicitly starts capture; Windows controls overflow placement.
  The prototype uses page-local Space, a Wake button, and Escape; it does not
  register global shortcuts or position a real window.
- Passive status never steals focus. Only explicit interaction opens interactive
  detail; expose context, cancellation, and approvals through equivalent visual
  controls. A real approval must not be satisfied by the push-to-talk key.
- Hidden regions are always non-hit-testable. The presence's transparent footprint
  is interactive only during explicit Ctrl interaction. Native approval controls
  accept input only inside their visible bounds, remain unarmed for at least 500 ms
  after presentation, and ignore mouse/key gestures that began before the approval
  appeared. Focus may describe the proposal but never lands on an armed affirmative
  action as a side effect of showing the surface.
- Keep the presence above ordinary app windows without claiming it can appear
  above secure desktops or exclusive fullscreen. Offer user-controlled placement
  and suppression in presentation/fullscreen scenarios as future design decisions.
- Native work must prove Avalonia/Windows transparency, hit testing, DPI handling,
  display/work-area changes, focus behaviour, and accessibility before adopting
  the shell design.

## State language

| State | Colour family | Motion | Persistence |
|---|---|---|---|
| Idle / Wake Listening | None on presence surface | None | Presence hidden; wake-listening status remains visible in tray/status UI |
| Capturing Command | Sea glass / teal | Attentive wandering; other concepts gently breathe | Visible throughout wake-activated or optional PTT capture |
| Transcribing / preparing / calculating | Lavender | Independent darting in the presence; orbital motion in other concepts | Until preparation completes |
| Clarification / approval wait | Warm amber | Almost still | Until resolved, cancelled, or expired |
| Executing | Clear blue | Directional flow / scanning | Until terminal outcome |
| Success | Sage green | Settle | Brief receipt, then fade if not engaged |
| Failure / outcome unknown | Soft coral | Stable | Until acknowledged; never imply success |
| Providing information | Ice blue | Slow, open motion | Until dismissed in this study |

Animation is an activity indicator, **not a percentage complete**. The prototype's
listening motion is synthetic; native amplitude response must reflect actual audio
only during command capture. A hidden presence must not conceal enabled microphone
listening; the separate tray/status indicator communicates local wake listening.

Native state changes cross-fade between the current and target state colours rather
than replacing the palette in one frame. Showing and hiding the presence also
fades over a short interval; a request to show it again cancels a pending hide.
Native presence also automatically hides after 10 seconds of inactivity when
idle, listening, or showing a completed result without required attention.
The timer does not depend on microphone capture being active. Busy or executing
work, speech, pending approvals/questions, response actions, grant editing, and
unacknowledged failures suspend auto-hide; clearing them starts a fresh period.
Hiding is presentation-only and never cancels work, closes capture, dismisses a
prompt, or asks the user for confirmation.
Displaying the presence is an optional device-local interactive feature and
is enabled by default. Disabling it keeps the presence hidden without disabling
response, settings, approval, or other interactive surfaces. Presence timeout
(default 10 seconds) and response timeout (default 5 seconds) are separate
device-local settings, each adjustable from 1 to 60 seconds.
Showing or interacting with a surface restarts its deadline; repeated status
notifications alone do not extend an existing presence deadline. Response
**Always show** does not pin presence. Legacy shared timeout values are
retained for the response window, while the new presence setting defaults to 10.

During Kora speech playback, the presence contracts and expands between 90%
and 112% of its resting size at the default speech scale amount. A normalized
20-ms RMS envelope from Kora's synthesized output follows the audio device's
playback position, so the motion reflects the speech rhythm rather than a
synthetic pulse. Both Windows and Kokoro output use this path. Synthesis alone
does not animate the presence, and stopping, cancelling, or invalidating playback
returns it to its resting size. This is a speech playback dimension, not task
progress, phoneme recognition, microphone input, or audio from other applications.
The state-driven colour palette remains independent of speech sizing.
Speech sizing uses two cascaded low-pass stages with a 35-ms time constant.
This eases both the size and its velocity through speech edges instead of
snapping to small level changes, without overshooting the configured range.
It responds by more than 90% within 150 ms, retaining the syllable rhythm;
stopping speech or switching sizing off smoothly settles back to normal.
Animation uses measured elapsed time so delayed UI frames do not accumulate lag.

The native Settings surface provides device-local sliders for the overall
presence footprint (240-600 px, default 360), particle diameter (50-200%,
default 100%), dot density (25-200%, default 100%, or 150 particles), and particle
movement speed (25-200%, default 100%). Density changes the particle count
independently of dot size: 25% gives 38 particles and 200% gives 300.
Speech sizing has an enabled-by-default toggle and an amount slider (0-200%,
default 100%). The amount scales the deviation from resting size: 0% holds
normal size, 100% uses 90-112%, and 200% uses 80-124%. Turning it off disables
only speech-driven sizing, not speech playback, colour transitions, or dot motion;
the amount is retained for re-enabling. Changes apply live and persist on this device.
Resizing preserves the bottom-right working-area anchor; movement speed scales
state-driven velocity without changing lifecycle state or particle count.

Keep the normal presence entirely abstract: no letters, symbols, or visible status
caption in or beneath the presence. Colour and motion convey state; voice
output provides context without narrating every routine state transition. Keep
screen-reader state announcements and accessible control names, and offer expandable
visual details as a non-voice alternative. Effective processing mode/runtime remains
visible in those details; explicit approval and error panels remain available.

Keep speech playback separate from task state as required by
[Task Lifecycle](Task_Lifecycle.md). This browser prototype demonstrates the visual
design only; it does not produce voice output or demonstrate simultaneous speech
playback and execution.

## Interaction proposal

Native rich output follows [Information Display](Information_Display.md): optional speech
text is a separate readable surface, not a caption beneath the presence.
Markdown/diagrams and browser/HTML views use explicit detail/viewer surfaces; this
offline prototype does not implement captions, Markdown rendering, or browser isolation.

- Native activation uses the local "Kora" detector, including wake-triggered speech
  interruption, as defined in [Task Lifecycle](Task_Lifecycle.md#wake-listening-and-command-capture).
  Configured custom-name profiles follow [Assistant and Activation Name](Activation_Name.md);
  the desktop bootstrap implements display/command renaming but not production detector profiles.
  The browser study has no microphone access and does not demonstrate real wake detection.
- Native status/queue interaction remains available during execution as defined in
  [Work Management](Work_Management.md). A status response must not replace the active
  task's progress or approval identity; this browser study does not simulate the queue.
- Native Windows lock releases microphone capture, stops speech, and hides sensitive
  interaction surfaces under [session policy](Built_In_Skills.md#mandatory-locked-session-microphone-policy).
  Hiding the animation alone is not microphone shutdown; this study does not implement OS session events.
- Native presence can appear for a Kora-initiated suggestion under
  [Proactive Interaction](Proactive_Interaction.md). Eligibility, quiet preferences,
  and trusted approval routing govern delivery; this study's visual tour does not authorise proactive actions.
- Native detail shows call-state source/freshness and why speech is gated under
  [Call-Aware Speech](Call_Aware_Speech.md). Visual results stay usable during calls;
  this browser study does not detect calls or exercise voice-configured overrides.
- Hold Space on the page background to simulate capture; release to prepare.
  Space retains its normal behaviour on buttons and form controls.
- The simulated utterance reaches a transmission review. Approve once leads to
  execution and a sample answer; Keep local sends nothing and explains the local
  runtime requirement. No real data is transmitted.
- Click the visual to expand/collapse routine detail. Approval and error details
  remain open even with the detail toggle off. The initial activity view is
  presence-only; Wake Kora opens an illustrative information response.
- Escape or the close control cancels active capture/work first and displays the
  cancellation result. A subsequent dismissal hides it. Remote cancellation does
  not assert that side effects were undone.
- Success starts a 6.5-second display period. Hover, focus, or Keep visible prevents
  automatic dismissal; in this study an engaged result then remains until dismissed.
- Information and errors remain visible until dismissed. Native wait expiry,
  cancellation reconciliation, transcription review, and exact selected-content
  preview must follow the existing lifecycle/security contracts; the study's
  approval scope is illustrative and cannot serve as production consent UI.
- Play sequence is a visual tour, not a lifecycle implementation. It automatically
  cycles past Waiting solely to compare appearances, with no permission or action.
- Reduced motion defaults to the system preference and freezes the visual. Quiet,
  Balanced, and Expressive adjust density, size, and opacity. The shared
  System/Light/Dark application theme controls the visual background and
  surrounding result surface; the presence uses darker particles in the
  effective light theme rather than bright glow to maintain legibility.
- Stop animation work while hidden or the browser tab is inactive. Reduced motion
  redraws only on relevant changes.

## Questions for the next iteration

- Which feels like Kora: organic cloud, fluid orbit, or precise lattice?
- When should voice provide a brief explanation versus remain silent during ordinary activity?
- Should routine information fade after speech finishes, or remain until dismissed?
- Should success receipts fade sooner than the proposed 6.5 seconds?
- What is the smallest still-readable native footprint on a high-DPI display?

This study supplements, but does not change, [MVP Scope](MVP_Scope.md),
[Task Lifecycle](Task_Lifecycle.md), or [Security and Data Flows](Security_Data_Flows.md).
