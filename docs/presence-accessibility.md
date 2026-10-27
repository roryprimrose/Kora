# Presence accessibility

The particle presence remains abstract, without a visible status caption. When
it is visible and presentation is permitted, its accessible name describes the
current assistant state: listening, calculating, waiting, executing, success,
failure or information.

The name separately says whether **speech playback** is active or inactive.
Preparing speech is not playback, and speaking does not replace the task state.
Semantic name changes use polite screen-reader live-region updates. Animation,
particle movement and speech-volume frames do not repeat announcements.

The standard presenter and the separately selected experimental native presenter
use the same accessible meanings. This does not enable the experimental renderer
or change its availability requirements.

Hidden, minimized, disabled, private, detached or unavailable presence does not
expose its previous state or announce hidden transitions. An unknown state is
unavailable, not idle or successful. Accessibility never reads answer text,
starts Kora speech, changes focus or approves an action. Existing presence
activation and review requirements are unchanged.

Deterministic software tests cover automation names, semantic updates and
retirement for both presenters without a real native rendering device. Installed
screen-reader and physical-device qualification are separate and are not claimed
by those tests.
