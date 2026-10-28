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

### Exact pending model-action attention

An already-admitted model-action proposal keeps presence in warm-amber Waiting
until the authoritative proposal is confirmed, rejected or retired. Attention
contains no proposal text or technical identifiers. It is not a second review
registry, a grant, or evidence that the action has completed.

Ctrl + right-click on interactive presence opens that exact captured proposal;
Ctrl + left-drag remains reposition-only. The keyboard-accessible native tray
command **Review pending action**, or already-activated
**"Kora, review pending action"** (using the configured assistant name), opens the
same original review. No new listening or capture path is enabled. Passive
attention never activates a window.

Opening another named conversation remains unavailable while this approval owns
input; refusal leaves the exact original review and its separate decision intact.

Opening uses the existing response surface's original action description and
separate Once / This session / Always / Reject controls, with existing artifact
single-use and call-channel restrictions. It never approves or executes.
Affirmative controls require a fresh gesture after their initial 500-ms
presentation guard; focus goes to the composer, not approval.

The volatile host-owned target binds the original host request/session, review
epoch, audit, host ownership generation/binding/revision and privacy topology.
Selection and voice-capture generation are not review targets. Voice callbacks
retain their existing activation/generation fences. The original confirmation
path still owns its input-preparation and grant-revision checks across awaits;
a separate confirmation gesture does not create a newer review target.
Named-skill choice captures remain independent: they cannot open, replace or
approve a pending action, and review activation cannot authorize a skill choice.
Replacement, withdrawal, privacy/ownership loss, changed artifact, disposal or
late callbacks fail closed without following a newer review or changing another
session. Closing/hiding presentation is not action completion; only the original
owner's retirement clears attention. Targets are not saved, replayable or
model-supplied. Deterministic native tests are implementation evidence, not
physical keyboard, screen-reader, provider or installed qualification.

| State | Colour family | Motion | Persistence |
|---|---|---|---|
| Idle / Wake Listening | None on presence surface | None | Presence hidden; wake-listening status remains visible in tray/status UI |
| Capturing Command | Sea glass / teal | Attentive wandering; other concepts gently breathe | Visible throughout wake-activated or optional PTT capture |
| Transcribing / preparing / calculating | Lavender | Independent darting in the presence; orbital motion in other concepts | Until preparation completes |
| Clarification / approval wait | Warm amber | Almost still | Until resolved, cancelled, or expired |
| Executing | Clear blue | Directional flow / scanning | Until terminal outcome |
| Success | Sage green | Settle | Brief receipt, then fade if not engaged |
| Failure / outcome unknown | Soft coral | Gentle continuous wandering | Until acknowledged; never imply success |
| Providing information | Ice blue | Slow, open motion | Until dismissed in this study |

Animation is an activity indicator, **not a percentage complete**. The prototype's
listening motion is synthetic; native amplitude response must reflect actual audio
only during command capture. A hidden presence must not conceal enabled microphone
listening; the separate tray/status indicator communicates local wake listening.
Every visible state retains particle motion in both renderers when system motion
preferences allow it; reduced motion freezes the arrangement. Coral can also
represent a background host or setup failure, not only a command failure.
Failure details require visual feedback whenever private presentation is eligible
and remain exempt from response-window inactivity auto-hide until dismissed or
replaced. The coral colour alone is not an explanation of the failure.

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

### Bounded native renderer scheduling

The existing particle-cloud renderer uses cancellable one-shot dispatcher work,
not a permanently running frame timer. Detached, effectively hidden,
minimized, zero-size/zero-opacity, disposed or privacy-ineligible controls
schedule no animation work or renderer invalidations. Privacy defaults to
ineligible and each admitted callback rechecks the host's current presentation
gate. A visible requested hide (including the Hidden-state target) still completes
its 220-ms fade; reaching zero
stops work. Requested hides immediately relinquish mouse interception, including
the final transparent interval before the HWND hides.

Reattachment/reveal starts from the current state colour and resting speech
scale, fading in without integrating the hidden interval or replaying frames.
State and speech-input edges wake synchronously; speech start refreshes the
authoritative playback envelope instead of waiting for a quiet-state poll.
Active speech work keeps authoritative playback sampling awake at 16 ms,
including synthesis before the actual playing envelope is available. Only that
envelope drives speech sizing; a static non-playing frame does not invalidate.
Actual speech playback retains the current-main 16-ms cadence. All nonzero
particle motion and non-speaking colour/opacity/scale transitions use 33 ms,
independent of movement speed. The speed setting changes velocity, not frame
rate. The earlier 198-ms low-activity cadence produced stepped motion at lower
speeds and is no longer used for visible particle movement. Settled motionless
and reduced-motion states request no recurring drawing after transitions settle.
This deliberately trades the old sixfold quiet-motion frame reduction for
consistent smoothness; no battery or energy improvement is claimed.
Elapsed-time motion retains the nominal velocity and caps
delayed integration at 198 ms. Windows' animation preference is read at startup
and each explicit show; reduced motion freezes the current particle arrangement
without reseeding it. Colour, visibility and actual speech sizing remain separate.

Depth order is computed once over the deterministic maximum seeded cloud, then
filtered by the original density prefix; changing density never selects a
different sorted subset. Six immutable brushes are reused until the effective
state colour/theme or animated opacity changes. Geometry, density, diameter,
speed and speech scale alone do not rebuild brushes. Particle limits and palettes
are unchanged.

[Deterministic renderer/native input tests](../tests/Kora.Windows.IntegrationTests/PresenceRendererTests.cs)
exercise hidden/detached/privacy/disposal fences, immediate wake, exact cadence
ratio, direct bounded drawing, seeded ordering, brush identity, input-edge fades
and retired callbacks. These are bounded scheduling/resource assertions, not
measured whole-application CPU savings, reproduction of a whole-core report,
GPU/hardware evidence or installed/accessibility qualification.

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

### Opt-in native presence renderer

Every left tray click requests reveal immediately, including both clicks in a
double-click. Show Kora is the bold default menu action; Settings remains an
explicit menu/command route. There is no click-sequence timer or delayed reveal.
Privacy, display and instance-ownership gates still apply.
The responsiveness target is under one second from the tray-handler entry to
the first positive-opacity frame, preferably under 500 ms. The private-desktop
fixture measures both renderers with a deliberately blocked scratch SQLite
commit; these submission/state checks are not composed-pixel or physical-laptop
latency qualification.

The existing Avalonia presence remains the default. Settings > Appearance >
Presence appearance > **Presence renderer** selects **Avalonia** or
**Native (experimental)** at runtime. Both display the same particle-cloud style.
The choice uses the shared audited appearance preference
`appearance.native-presence-renderer` (`false` for Avalonia, `true` for native).
A saved choice takes precedence over `KORA_PRESENCE_RENDERER`. Without a saved
choice, the exact value `native` selects the native startup default; unset or
`avalonia` retains Avalonia. Unknown environment values fail explicitly, even
when a saved preference exists, without fallback or preference-file changes
([selection](../src/Kora/Controls/PresenceRendererSelection.cs)).

The renderer selector uses the existing single-flight appearance-persistence
queue, retaining at most the latest pending absolute choice per appearance
option. Its binding reads the last confirmed choice without taking the storage
lock while audit or preference I/O is pending. Startup still reads and validates
the saved renderer before creating the first window. Requested and terminal
audits remain durable, privacy is rechecked before writing, and only confirmed
state requests window replacement. Handoff refuses pending persistence and
shutdown joins it; a stale or failed write is explicitly reported.

The desktop's [bounded evidence segments](Architecture.md#bounded-desktop-evidence-segments)
limit the database checked by each new renderer-configuration audit. Existing
history remains readable and sealed rather than being cleared to improve
latency. Requested and terminal audits still complete their checked FULL commits
before publication or fade-out; a file-only receipt, queued audit or uncertain
rotation cannot activate the renderer. Synthetic checked-write and rollover
timings are not a physical sub-second responsiveness qualification.

Changing renderer defers until initial host startup, mouse-button gestures and
presence-owned dialogs/file pickers finish. New choices replace pending choices;
choosing the current renderer cancels a pending replacement. Fade preparation,
cancellation and actual replacement create linked presentation activities, not
every read-only waiting poll.
An eligible visible renderer first fades to zero using the existing 220 ms
visibility animation. The existing UI timer polls readiness without blocking
Settings, tray actions or shutdown. Input stops at the outgoing visibility edge;
no second window or worker exists during the fade. A fade that has not reached
zero within one second fails explicitly without retirement or automatic fallback.
Hidden, minimized and privacy-ineligible presentation never waits for cosmetics.
Choosing the current renderer during a fade resumes that same window, subject to
the current privacy/display gates and the original remaining inactivity deadline.
After zero opacity, the old window is hidden, its timers and input are stopped,
its rendering worker is joined and its popup/root are retired before a replacement
is constructed. Fresh admission fades the replacement in from zero over 220 ms;
this is sequential fade-out/fade-in, not overlapping rendering authorities.
The replacement uses the same host view model, preserves position,
hidden/minimized state (including changes during the fade) and the remaining
inactivity deadline, and rechecks privacy
and display eligibility before revealing. It never reruns host/provider/voice
startup, changes microphone consent or replaces other application services.
File and question surfaces resolve the current owner instead of retaining a
retired window. Creation/presentation failures leave the new presence unavailable
with an explicit settings status; there is no automatic renderer fallback.
Uncertain old-window retirement prevents further replacement until normal exit
([controller](../src/Kora/PresenceWindowController.cs),
[window lifecycle](../src/Kora/MainWindow.RendererSwitch.cs)).

For an explicitly approved local timing trial only, `KORA_PRESENCE_TIMING=1`
enables a content-free, bounded stdout capture. It is absent/off by default;
unknown values fail explicitly. The versioned `Kora.PresenceTiming` meter emits
only fixed `PresenceSwitchPhase` names and monotonic `Stopwatch` timestamps:
selection/queue/worker, configuration lock, Requested audit, preference write,
Succeeded audit, publication, actual Apply return/UI continuation, confirmed
notification, first renderer timer tick, owner/window/fade waits, fade start,
retirement, creation and restore. `ApplyReturning` precedes activity disposal;
`ApplyReturned` follows the actual call, so disposal delays are not mislabelled.
The capture does not subscribe to general logs, activities or private stores,
and records no text, paths, setting values, host identities or credentials.
Its 256-entry context-free output lane never performs I/O in the metric callback;
shutdown joins accepted output and reports overflow/invalid measurements/I/O
failures explicitly rather than calling an incomplete trial successful. Timing
observations are not audit authority or physical-pixel evidence. Compare one
switch at a time using `(endTimestamp-startTimestamp)/frequency`; output order
across threads is not an operation identity. A timing-enabled physical launch
still requires fresh approval, normal ownership admission and disclosure of
possible already-consented microphone/provider startup; it grants no recovery,
restart or private-profile collection permission.
For the native-renderer configuration audit action only, the same opt-in also
separates required-copy/projection gate wait and delivery, SQLite admission,
lease, restricted open/connection/schema/quick-check, before/after digest,
changed-history validation, transaction begin, insert, file verification and
FULL commit. These remain fixed phase names with no store size or row contents.
No audit is skipped, deferred, batched or reclassified by this observation.

The native path retains the Avalonia window for position, size, DPI observation, click-through/Ctrl-drag routing, timeout, privacy and shutdown. A same-process, same-UI-thread native popup is owned by that window, not parented as a child. The `asInvoker`, `uiAccess=false` Windows manifest does not request elevation
([host](../src/Kora/MainWindow.NativePresence.cs), [manifest](../src/Kora/app.manifest)).

The popup has a private window class without `CS_OWNDC` or `CS_CLASSDC`, uses
per-pixel `UpdateLayeredWindow` only, and always passes pointer input through.
It never uses constant-alpha initialization. In native mode the Avalonia shell
is not forced into `WS_EX_LAYERED`: Avalonia 12.1.3 uses `CS_OWNDC`, which
[Windows documents as incompatible with that style](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles).
Instead the existing Ctrl/button/eligibility policy applies a real empty window
region while passive and a full-footprint region during admitted Ctrl interaction.
NULL is not an empty region. Successful region transfer gives Windows ownership;
failed transfers release only the untransferred handle. Stable polling does not
allocate regions, resize updates physical bounds, and reentrant native position
notifications cannot commit an input mode before the outer transfer succeeds
([input](../src/Kora.Windows/Presentation/WindowsPresenceWindowInput.cs),
[regions](../src/Kora.Windows/Presentation/WindowsPresenceRegion.cs)).

The default Avalonia input backend remains unchanged. Native selection happens
before constructing either backend. Unknown/conflicting region ownership,
foreign/thread-mismatched windows, forced layered shells and unavailable transparent
backends fail explicitly, without native class-style resets or silent fallback.
The inherited default backend's forced-layering contract is not claimed fixed by
this opt-in native change.

The layout control is only a host-owned geometry/lifetime adapter, not a native
container. It resolves physical client coordinates to screen coordinates and
reconciles popup movement after owner position notifications. Effective visibility,
privacy admission and attachment generation are rechecked before queued presentation.
Hide/minimize suspends presentation; restoration requires fresh admission. Detach
joins the worker and destroys the popup; reattachment creates a fresh surface.
Owner destruction is an OS safety net, not a replacement for explicit shutdown.
No color key, alpha-1 background, separate renderer process or altered parent
alpha is used. Region failure still hides the host in finally.

The previous direct-child laptop build was invisible despite a visible,
correctly positioned 300-by-300 child and root. That observation is historical,
not a qualification of this owned-popup build. Native remains unqualified pending
separately approved integrated appearance/input/normal-shutdown testing.
Window visibility and successful submissions do not establish composed pixels.

One rendering worker owns the particle field and reusable DIB resources. The UI presents at most one completed frame at a time and discards queued pixels after suspension or a geometry-generation change. Shutdown does not require draining UI callbacks. Rendering/presentation errors make the native presence unavailable and report explicit selection of Avalonia in Settings or normal-exit recovery; the default renderer is not silently substituted
([worker](../src/Kora.Application/Visuals/PresenceRenderWorker.cs)).

The worker emits at most 16 content-free transition records (event 1), one explicit
limit notice (event 2), and one terminal counter summary (event 3) per lifetime.
Transitions describe admission, generation, state, requested visibility, reduced
motion, dimensions/scaling and whether a positive-opacity frame was presented.
They contain no text, paths, pixels, speech levels or provider data. Repeated
frames do not produce repeated records. Writes run on the rendering worker,
outside its state lock, using a new linked activity for the captured admission;
ordinary evidence uses the upstream shared deferred-evidence path, with
call-time sink admission and immutable snapshots before its bounded 1,024-item
queue. File and SQLite delivery run on that worker; there is no separate
immediate-file/projection queue. Queue exhaustion fails closed rather than
dropping projections. Delivery failures remain explicit and sticky for
subsequent admission and joined shutdown. A blocked ordinary diagnostic write
does not delay the worker's first frame while queue admission remains available.
Typed audit writes are not deferred. Existing error event 0 is
unchanged. Positive frame opacity is not a DIB-pixel or compositor-visibility
measurement. The cap can omit later transitions and the limit notice makes this
explicit ([events](../src/Kora.Application/Visuals/PresenceRenderWorker.Logging.cs)).

Speech visualization samples the existing host-owned playback frame while speech is active. Neither the presenter nor its worker owns microphone capture, synthesis, audio output, caption text or task state. Host privacy/visibility remains the admission boundary. Particle density, dot size, footprint, speed, theme, state transitions, visibility fades and speech-scale settings remain live inputs
([integration](../src/Kora/MainWindow.NativePresence.cs)).

Both presenters consume a typed Windows animation preference. The presence owner
subscribes before its first read and follows live preference notifications on the
UI thread, including while already visible. Privacy withdrawal and retirement
unsubscribe; restoration takes a fresh read and ignores callbacks from the old
subscription or queued generation. Read or notification-subscription failure
hides the presence and reports Settings/restart recovery, never assumes motion
is enabled. This does not change saved theme/renderer choices or add a new
reduced-motion setting. Windows high-contrast versus explicit theme/renderer
precedence remains outside this implementation.

As in the default renderer, reduced motion freezes particle movement without disabling the independent speech-sizing setting; colour/visibility transitions still settle. The native buffer bounds each physical dimension to 4096 pixels; invalid geometry is explicitly unavailable
([inputs](../src/Kora.Application/Visuals/PresenceRenderInputs.cs)).

The native path preserves #250's eligibility, seeded-prefix selection, motion rates and bounded elapsed-time integration. Static and fully faded frames stop recurring drawing; all nonzero particle motion uses 33 ms even at low configured speed. The 198-ms value remains an elapsed-integration bound and native static backoff, not a moving-particle frame interval. A host-owned 16-ms sampler observes authoritative playback during speech work even before audio starts; it does not drive particle calculations or
fabricate audio levels ([worker](../src/Kora.Application/Visuals/PresenceRenderWorker.cs), [host](../src/Kora/Controls/NativePresenceHost.cs)).

To select the native presenter for one manually approved launch, set the environment
only on the child process. Do not use `setx` or change device preferences. Normal
host startup can initialize previously consented microphone/provider services.
An ownership refusal is not permission to reset state or retry automatically.

```powershell
$executable = (Resolve-Path .\src\Kora\bin\Release\net10.0-windows10.0.19041.0\Kora.exe).Path
$start = [Diagnostics.ProcessStartInfo]::new($executable)
$start.UseShellExecute = $false
$start.Environment['KORA_PRESENCE_RENDERER'] = 'native'
[Diagnostics.Process]::Start($start)
```

Inspect the tray Show action immediately, before the configured inactivity
timeout. Check visible dots without a rectangular background, passive pointer
passthrough and Ctrl-drag across the whole footprint. Close through normal
application shutdown. A passing synthetic fixture does not qualify those physical
observations, microphone/provider behavior or same-build renderer performance.

This is an **I - Implementation** increment, not default enablement or **Q - RC** qualification. Earlier laptop results compare standalone prototypes, not integrated Kora. Before changing the default, check the integrated path on the laptop for composed appearance, DPI/display changes, Ctrl-drag, speech/caption alignment and representative longer-run resource use. Post-composition pixel fidelity
and energy savings are not claimed ([evaluation scope](../experiments/presence-performance/REPORT.md)).

The isolated fixture exercises the production worker, owned popup and region input
on a disposable, never-activated desktop, avoiding unrelated active-desktop windows.
The fixture thread exits before its desktop handle is released. No application
service composition, devices, saved preferences, clipboard or network are used.
Its Fluent shell starts at zero opacity and contains the hidden animation-disabled
legacy sibling in a Grid. Synthetic Ctrl/button/cursor inputs replace physical
input polling. It checks Hidden-to-Information/reduced-motion admission,
positive-opacity presentation, owned-popup identity, a non-layered root, empty
region passthrough to a synthetic underlying top level, full-footprint Ctrl
routing, restored passthrough, screen-relative layout/movement, resize/DPI
projection, playback sampling, hiding, reveal, detach/reattach and disposal.
It does not compose MainWindow's view model/bindings or prove visible desktop
pixels, physical Ctrl-drag, audio/captions or performance
([fixture](../tests/Kora.NativeUxFixture/NativeUx/NativePresenceFixture.cs)):

```powershell
dotnet build .\Kora.slnx --configuration Release --no-restore
$scratch = Join-Path $env:TEMP ('KoraPresence-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
& .\tests\Kora.NativeUxFixture\bin\Release\net10.0-windows10.0.19041.0\Kora.NativeUxFixture.exe --launch-native-fixtures --scratch-parent $scratch --native-presence-fixture
```

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
