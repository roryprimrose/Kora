# Ambient UI study

Status: exploratory visual/interaction proposal, not an implemented desktop capability.

Open [the interactive prototype](Prototypes/ambient-ui.html) in a modern browser. It is
self-contained, works offline, and uses no external assets, dependencies, telemetry,
microphone, clipboard, or runtime. Controls and task results are simulations.

## Visual directions

1. **Constellation (recommended):** a small, original three-dimensional particle
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
- Hidden means no animation, input region, focus capture, or invisible hot corner.
  Summon primarily by saying an active name ("Kora" by default) after enabling local wake listening.
  Optional push-to-talk, a keyboard shortcut, or a tray command provide alternatives.
  Hiding the presence is not microphone mute: a persistent tray/status indicator must
  distinguish Wake Listening, Muted, and Unavailable and expose an explicit mute control.
  It also opens native questions and microphone selection/recovery by mouse under
  [Interaction Fallback](Interaction_Fallback.md); voice must not be required to restore voice.
  The system tray icon/context menu is the primary non-voice recovery route, with
  detected microphone selection, explicit Enable listening, and accessible state.
  Clicking the icon never implicitly starts capture; Windows controls overflow placement.
  The prototype uses page-local Space, a Wake button, and Escape; it does not
  register global shortcuts or position a real window.
- Passive status never steals focus. Only explicit interaction opens interactive
  detail; expose context, cancellation, and approvals through equivalent visual
  controls. A real approval must not be satisfied by the push-to-talk key.
- Transparent/hidden regions are always non-hit-testable. Native approval controls
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
| Transcribing / preparing / calculating | Lavender | Independent darting in the constellation; orbital motion in other concepts | Until preparation completes |
| Clarification / approval wait | Warm amber | Almost still | Until resolved, cancelled, or expired |
| Executing | Clear blue | Directional flow / scanning | Until terminal outcome |
| Success | Sage green | Settle | Brief receipt, then fade if not engaged |
| Failure / outcome unknown | Soft coral | Stable | Until acknowledged; never imply success |
| Providing information | Ice blue | Slow, open motion | Until dismissed in this study |

Animation is an activity indicator, **not a percentage complete**. The prototype's
listening motion is synthetic; native amplitude response must reflect actual audio
only during command capture. A hidden presence must not conceal enabled microphone
listening; the separate tray/status indicator communicates local wake listening.

Keep the normal presence entirely abstract: no letters, symbols, or visible status
caption in or beneath the constellation. Colour and motion convey state; voice
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
text is a separate readable surface, not a caption beneath the constellation.
Markdown/diagrams and browser/HTML views use explicit detail/viewer surfaces; this
offline prototype does not implement captions, Markdown rendering, or browser isolation.

- Native activation uses the local "Kora" detector, including wake-triggered speech
  interruption, as defined in [Task Lifecycle](Task_Lifecycle.md#wake-listening-and-command-capture).
  Custom-only/dual-name profiles follow [Custom Activation Names](Activation_Name.md); this prototype does not implement renaming or detector profiles.
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
  constellation-only; Wake Kora opens an illustrative information response.
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
  Balanced, and Expressive adjust density, size, and opacity. Light-desktop mode
  uses darker particles rather than bright glow to maintain legibility.
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
