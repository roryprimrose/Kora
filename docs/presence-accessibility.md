# Presence accessibility

The particle presence remains abstract, without a visible status caption. When
it is visible and presentation is permitted, its accessible name describes the
current assistant state: listening, calculating, waiting, executing, success,
failure or information.

The name separately says whether **speech playback** is active or inactive.
Preparing speech is not playback, and speaking does not replace the task state.
Semantic name changes use polite screen-reader live-region updates. Animation,
particle movement and speech-volume frames do not repeat announcements.

These meanings apply to the standard presence display. An experimental display
is not enabled by these accessibility controls.

Hidden, minimized, disabled, private, detached or unavailable presence does not
expose its previous state or announce hidden transitions. An unknown state is
unavailable, not idle or successful. Accessibility never reads answer text,
starts Kora speech, changes focus or approves an action. Existing presence
activation and review requirements are unchanged.

Screen-reader behavior can depend on your Windows and assistive-software setup;
the presence's accessible name does not promise that every combination has
been verified.

## Compact responses on small working areas

The compact response fits within the display's available working area, including
scaled displays smaller than its ordinary 480 by 570 layout. The content keeps
its readable layout inside native horizontal and vertical scrolling; it is not
shortened or scaled down. Tab reaches the original controls and command input.
Focus either named scrolling area and use Page Up/Page Down to read its content;
native scrollbars and accessibility scrolling remain available.

If the existing expanded Sessions minimum (820 by 560) cannot fit, Kora stays
compact and reports the limitation without replacing the response or input.
Side-by-side conversation and Details still requires its separate 1040-wide
minimum; it is not silently stacked or shrunk.

Fitting and scrolling do not activate the window, run a command, read a new
source, copy text, start capture or speech, change permissions, or renew retained
content. Existing explicit controls and privacy retirement remain unchanged.
Recovery immediately after an already-open window's display topology changes
is separate; this behavior applies when opening or explicitly changing layout.
Deterministic native tests do not qualify physical monitors, Windows DPI or
installed assistive technology.

## Reduced motion

Choose **Settings > Appearance > Reduced motion**, or say **"Kora, use reduced motion"** using your current assistant name. This freezes particle movement in both animation modes without hiding state, changing focus or disabling independent speech sizing. Off/reset follows Windows animation settings and never overrides Windows reduced motion; colour and visibility transitions still settle ([settings](settings.md#appearance), [precedence](../src/Kora.Core/Configuration/ReducedMotionSettings.cs), [binding](../src/Kora/Controls/PresenceMotionBinding.cs)).

The saved choice reloads at startup. Model suggestions remain inert until separate native review/confirmation, and available single-change undo uses that same native review requirement. Unknown Windows motion state still hides the presence with recovery guidance, even with explicit reduction selected ([preference owner](../src/Kora.Application/Configuration/AppearanceConfigurationService.cs), [review](../src/Kora.Application/Configuration/AppearanceSuggestionReview.cs), [recovery](../src/Kora/MainWindow.Motion.cs)).
