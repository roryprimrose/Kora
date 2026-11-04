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

## Response position recovery

When a compact response opens, Kora keeps its saved position if the window fits
inside the connected display's working area. If the origin is still on-screen
but the response extends beyond an edge, Kora moves it back into that area so
the response and command input remain reachable. A disconnected saved display
continues to use the normal placement near presence.

This reuses the same working-area layout as expanding or collapsing Sessions.
Recovery does not activate the window, submit unsent input, replace its response,
start listening or speech, or save a new position by itself. Existing minimum
window sizes still apply; this does not add a small-display layout or qualify
physical multi-display or installed accessibility behavior.

## Reduced motion

Choose **Settings > Appearance > Reduced motion**, or say **"Kora, use reduced motion"** using your current assistant name. This freezes particle movement in both animation modes without hiding state, changing focus or disabling independent speech sizing. Off/reset follows Windows animation settings and never overrides Windows reduced motion; colour and visibility transitions still settle ([settings](settings.md#appearance), [precedence](../src/Kora.Core/Configuration/ReducedMotionSettings.cs), [binding](../src/Kora/Controls/PresenceMotionBinding.cs)).

The saved choice reloads at startup. Model suggestions remain inert until separate native review/confirmation, and available single-change undo uses that same native review requirement. Unknown Windows motion state still hides the presence with recovery guidance, even with explicit reduction selected ([preference owner](../src/Kora.Application/Configuration/AppearanceConfigurationService.cs), [review](../src/Kora.Application/Configuration/AppearanceSuggestionReview.cs), [recovery](../src/Kora/MainWindow.Motion.cs)).
