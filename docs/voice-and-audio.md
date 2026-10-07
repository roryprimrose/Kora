# Voice, microphone, and speakers

## Microphone selection

The bounded [exact input-device commands](commands.md#exact-input-device-preference)
also expose this same preference through typed or already **activated** voice.
They do not provide wake listening or receive speech while input is closed.
Use exact listed endpoint IDs, not names/indices. Per-option reset selects
System only; it does not enable listening, change consent/permission or reopen
a run hold. Protected/unknown calls deny original voice mutations.

Choose **Settings > Speech & audio > Choose microphone (native recovery)** or
the same **Choose microphone** tray entry. This native card works without voice,
a model or network. Opening it refreshes metadata only. Review System and active
endpoint names/IDs (duplicate names remain distinct); an unavailable saved pin
stays visible. Highlighting changes only a local draft. **Save preference only**
saves that exact current choice without capture or new consent. Changed selection
closes input; **Enable listening (PTT readiness only)** is a separate fresh input
for the displayed saved endpoint, available only with existing consent and current
ownership/privacy/permission/readiness gates. Refresh after stale input or device
changes. Disable and Stop speaking remain explicit separate actions.

The card has no microphone test or combined consent/selection/enable. Review
consent separately in existing speech Settings; absent/withdrawn consent cannot
be renewed by the card. Closing/Escape changes no consent, task, session or
question. Full first-run onboarding, shared durable device questions, production
wake and native/hardware acceptance are still pending.

The Settings microphone card reports the current Windows privacy state
separately from device selection. An available **System** or named endpoint does
not by itself prove that desktop microphone access is allowed.

The microphone list contains:

- **System** - follows the live Windows multimedia-default microphone;
- each active Windows capture endpoint - pins Kora to that endpoint.

Choosing a specific microphone stores its stable Windows endpoint ID as a local
Kora override. Choosing **System** removes that override. A missing pinned
microphone is shown as unavailable and is not silently replaced by a same-name
or newly-default device.

The [tray microphone/listening recovery](windows-and-tray.md#microphone-and-listening-recovery)
is available without a model, network or working microphone. Opening its menu
refreshes metadata with a five-second single-flight deadline. A native selection
mark means preference only; stale menu selections are refused and a failed save
retains the old preference. Selecting a replacement closes input but does not
record or release a recovery hold. System may be explicitly selected even
without a default; enabling still requires a usable endpoint and all existing
consent/privacy/readiness gates. No test capture or automatic selection is added.

Changing the Windows default while System is selected automatically reroutes
an already active WASAPI capture to the new available default microphone.
Named endpoint selections remain pinned. A missing effective microphone closes
capture and requires explicit recovery; restoration or a default change cannot
reopen capture after it has closed or release a current-run recovery hold.

## Voice consent and explicit push-to-talk

First launch enumerates devices without recording. In **Settings > Speech &
audio**, review the explanation and choose **Enable voice (save consent)** or
**Continue without voice**. Consent is device/profile-local, separate from
Windows permission and endpoint selection. **Withdraw voice consent** persists
the closed choice across restart.

With saved consent and fresh ownership/session/permission/device gates, ordinary
startup enables push-to-talk. **Enable listening** explicitly releases a
current-run recovery hold, but does not open the microphone. **Disable
listening** closes input for the current run without withdrawing saved consent.

The production wake engine is not selected or integrated. The previous ambient
phrase grammar is not production wake and is no longer run on ambient audio.
The microphone stays closed until explicit push-to-talk. Hold **Push to talk**
in the native Speech & audio settings (mouse, Space or Enter), speak one
command, then release to finish. Losing the capture control/window also ends
capture. Each answer or approval requires a new activation. Transcripts use the
same deterministic command pipeline as typed input; activation alone approves
nothing.

The configured [display / PTT command-prefix name](settings.md#assistant-display--ptt-command-prefix-name)
is not a production wake profile. Native Apply/reset and exact
`list assistant settings`, `get/set/reset assistant.name` share typed audited
configuration. A name change closes and retires the old capture/grammar and
queued transcript/completion generations, including session/artifact prefixes.
It never replays a turn, reopens capture or releases a run hold. Explicitly
enable listening, then start a new PTT with the current prefix.
Invalid saved names disable prefix/capture with visible native/unprefixed
recovery. Failed new grammar startup does not restore an old prefix. Original
voice set/reset remains denied during protected/unknown calls. No custom wake
assets, enrollment, acoustic validation or download is implemented.

A result that arrives during microphone startup is staged as one bounded
transcript until the application acknowledges that exact activation generation.
Empty/timeout completion closes the recording and reports that no command was
heard; it does not authorize a late transcript or a new activation.
Releasing PTT while the microphone is still opening cancels that activation.
A late native open or callback cannot restore it. Exit/disposal retires the
activation before accepting any already queued command or completion.

**Settings > Calls** now provides manual protection for this run. Default
protected-call output, previews and approval readbacks are visual-only; pending
speech is invalidated before UI work and is never replayed on clearance.
Automatic detection remains unavailable. Saved activation preferences remain
independent from output and consent. Protected calls reject voice-originated
voice/in-call option writes and manual clear/reset; a later UI confirmation
cannot relabel them. Initiate a new Settings change instead. Status, stop speech,
disable listening and cancellation remain usable. New call-protection
downgrades and temporary/speak-once exceptions are unavailable pending exact
trusted review. See [manual call behavior](responses-and-calls.md#manual-call-mode-and-call-settings).

Kora stops capture when:

- you disable listening;
- Kora exits;
- Windows reports lock (including Win+L/idle lock), disconnect or unknown state;
- Windows suspends;
- microphone permission or the selected endpoint is lost;
- detected-call policy disables voice activation; or
- capture fails.

Windows session, power and endpoint notifications are already observed externally.
Microphone permission also has a one-second polling fallback; it is not detected
only at startup. Unknown session/permission and failed observation fail closed.
Negative session notifications close input/output before slower device requery.
The polling interval is not a guarantee of native detection or release latency;
complete Windows-transition, routing and hardware acceptance is still outstanding.

If Windows reports microphone access as blocked, enable **Microphone access**
and **Let desktop apps access your microphone** in **Settings > Privacy &
security > Microphone**. If access is allowed but listening remains paused,
check the Voice activation status; Kora also blocks automatic capture when
Windows reports the current session as locked or the interactive input desktop
is unavailable.

Unlock, resume, restored permission, hot-plug and refreshed/replaced devices
never release a current-run recovery hold. Use **Enable listening** explicitly.
Changing the assistant name invalidates current capture; enable listening again.
An ordinary restart uses its own saved consent and fresh gates, not old audio.

For a model-suggested action that needs approval, Kora always shows the
request and can speak the question. It releases microphone capture while
speaking. Activate push-to-talk again for **approve once**, **approve for this
session**, **always allow this**, or **reject**. Say the assistant name first
unless you turned off that requirement under **Settings > Approvals**.

## Speech providers

Kora always includes the **Windows** provider. It uses installed SAPI voices and
does not require a model, account, or network connection.

**Kokoro** is an optional local neural provider. Its inference support is part
of Kora, but its model and voices are not installed or downloaded by default.
To enable it:

1. Open **Settings > Speech & audio**.
2. Review **Kokoro** under **Speech provider assets (review only)**.
3. Select **Download**.
4. Leave Kora running while it downloads, verifies, installs, and prepares
   approximately 219 MiB of assets.
5. Explicitly select a ready **Installed speech provider** or an installed
   voice identified by `provider / ID`.

Kora pins the asset version, size, and SHA-256 digest. An incomplete, modified,
or unexpected download is rejected rather than activated. After preparation,
Kokoro's voices become selectable immediately without an application restart. Synthesis
then runs on this device and continues to use the selected Windows audio output.

Selecting a provider that still needs to be downloaded does not interrupt the
working speech provider. Kora continues using the current installed voice for
download results and failures. A successful installation does not silently
change the saved provider or active voice; selection is a separate explicit
configuration operation.

Use **Remove downloaded model** to delete the optional assets. This leaves the
Windows provider intact.

## Speech voice selection

Kora never silently downloads or substitutes a provider or voice. Without a
saved voice choice it uses the selected provider's advertised default.
For Windows that default ranks installed Windows voices in this order:

1. female voice matching the exact Windows profile locale;
2. female voice from the same language family;
3. male voice matching the exact locale;
4. male voice from the same language family;
5. compatible neutral voice;
6. compatible voice with unspecified gender.

Kora does not automatically select an unrelated language. An explicit voice
selection is stored locally and remains selected while that voice is installed.
The provider/voice pair is persisted atomically. Qualified choices select that
exact installed pair; an unqualified voice ID must be unique. A provider with
no compatible advertised default can still be selected by choosing an exact
installed voice. Missing selected assets and malformed/unknown saved state are
explicitly unavailable and require selection/reset or repair and refresh.
No different voice is used while recovery is pending.

If the Windows provider has no voice, install a Windows text-to-speech voice
through Windows Settings. If Kokoro is selected but absent, download it from
Kora Settings. Typed commands and visual output continue to work.

Select an installed voice and available output, then select **Preview** to test
that explicit choice. Preview is disabled when no voice is selected and
revalidates both selections before playback. There is no retained substitute
voice for a missing saved choice. No ambient recognizer
runs during playback. Push-to-talk stops Kora playback before opening command
capture. **Stop speaking** remains available from the tray without speech
recognition. Exit also stops active playback before host teardown. Acoustic
playback rejection for a future production wake pipeline still requires
separate real-hardware proof.

## Ordinary spoken summary caps

**Settings > Speech & audio > Spoken summary limits** and exact
`speech.summary-sentences` / `speech.summary-words` commands share device-local
caps: **1-3 sentences / 1-80 words**, default **3 / 80**.
The complete ordinary result, including spoken title and warnings, must fit
both. Over-cap text is not truncated or paraphrased: speech is withheld with
truthful full visual recovery. No additional model call is made.
Exact approval/proposal readback and required questions/options retain their
existing mandatory bounds; these caps are not permission to shorten them.
Provider/voice, System/pinned output, consent and call/privacy rules are
unchanged. Changes invalidate pending speech; no reset or clearance replays it.
See [exact counting, commands and recovery](settings.md#spoken-summary-limits).

## Audio output selection

The audio output list contains:

- **System** - follows the live Windows multimedia-default output;
- each active Windows render endpoint - displays an existing exact saved pin.

The native selector is read-only: output preference changes and reset currently
fail closed because desktop audio has no admitted session/generation and exact
host-held-choice bridge. Typed/activated output-setting requests also report
unavailable locally, not through a model. Existing saved output files are not
rewritten. This is not an output registry or acoustic acceptance claim.

For existing System routing, an available new Windows
default automatically reroutes active WASAPI playback while System is selected.
Named endpoint selections remain pinned, even when Windows defaults or unrelated
devices change. A missing or muted effective output stops speech without replay;
subsequent eligible speech resolves the selected endpoint. A pinned endpoint is
never silently replaced.
An observed unavailable System output invalidates active speech before queued
endpoint refresh. Loss of the Windows default does not stop an available pinned
output. Restoring output availability does not replay retired speech.

## Mute and playback failures

Kora treats these conditions as unavailable speech output:

- no active selected endpoint;
- no Windows default while System is selected;
- removed or disabled pinned endpoint;
- Windows software mute;
- Windows endpoint volume of zero;
- endpoint-open failure; or
- playback failure.

Kora does not change global Windows mute or volume. It displays the response as
text instead.

Windows cannot reliably detect every physical failure. Speakers may be powered
off, disconnected after an analog output, or muted by hardware without Windows
reporting it.

## Bounds and validation boundary

Activated PCM and transcript handling are bounded and generation-tagged.
Privacy closure invalidates input/output before asynchronous UI recovery;
old opens, samples and transcripts cannot re-enable or dispatch. Audio is
memory-only and cleared on closure, not logged or saved.

Audio failures at UI and host boundaries are logged and shown without escaping
through the desktop dispatcher. Native recognition cleanup and lifecycle-lock
acquisition have hard deadlines. If Kora cannot confirm cleanup, voice remains
disabled and the UI requires restart rather than claiming that capture is safe
to reuse. Exit still enters controlled host shutdown so an unconfirmed cleanup
can retain the unclean-owner marker instead of reporting a clean handoff.
Audio synthesis/playback and provider disposal do not depend on Avalonia's UI
synchronization context. Output invalidation never stops WASAPI while holding
the completion-state lock, so Exit and privacy closure remain responsive even
while speech is playing.

Deterministic policy/race tests are not real Windows acceptance trials.
External lock/disconnect/suspend, microphone-permission/device changes,
cross-build takeover/return and capture-release timings require reference
Windows trials. Those disruptive trials were not authorized for this R03
session. The release requirement remains **at most 500 ms from the observed
lock event in every reference trial**, recording OS notification delay
separately. This implementation does not establish that measured target or
production wake quality. Overall roadmap/decision status is left to integration
review.

The [shared deferred-validation register](../Design/Deferred_Validation.md#r03-windows-ownership-and-audio-privacy)
contains R03's individually approved interactive trial plan alongside the R02
proofs. These unperformed trials remain acceptance/release blockers, not a claim
of failure or success. The scoped implementation can merge after normal checks
and reviews without declaring those gates passed or authorizing a live trial.

### R03 validation evidence

The initial R03 snapshot on approved R01 `7d5e6a3` passed local suites but failed
the 100% coverage gate (98.7% line / 97.5% branch). Hosted Windows CI also found
SID-alias ACL comparison and elevated-runner assumptions. These are historical
failed checks, not waived or relabeled as passing evidence.

After rebasing onto `bcd4b81` (including the merged R02 speech/containment/storage proofs),
the CI fixes and expanded privacy/race tests were validated with .NET SDK
10.0.401 on Windows:

| Check | Result |
| --- | --- |
| Locked solution restore | Passed |
| Fresh Debug and Release solution builds | Passed, zero warnings/errors |
| Release Core unit tests | 251 passed |
| Release application unit tests | 742 passed |
| Release Windows tests | 188 passed |
| Locked framework-dependent win-x64 and win-x86 publish | Passed; binaries not launched |
| Merged portable line/branch coverage | 100% / 100%; passed the unchanged 100% / 100% gate |

Coverage includes 4,982 of 4,982 lines and 1,943 of 1,943 branches. No coverage
exclusions or threshold reductions were introduced. The added tests exercise
queued privacy transitions, output/consent failures, endpoint selection,
approval rechecks, lifecycle admission, SAPI stream compatibility, bounded
native cleanup, transient selector reset and audio-failure containment.
Clearing selection now closes armed input; protected binary ACL comparison
preserves exact SID/rights enforcement, and production elevated-process
admission still denies.
The Windows count combines deterministic fakes with non-disruptive native
object/device enumeration checks; it is not 188 real lifecycle or microphone
trials. Debug/Release publishing is not proof of cross-build or cross-architecture
handoff. Real Windows reference trials remain explicitly outstanding acceptance
evidence. The shared register records the bounded interactive subset and the
deferred cases; hosted CI and required reviews still must pass before this
scoped implementation can merge.

On 2026-10-05, a separately approved bounded trial used the physical HyperX
Cloud Alpha Wireless microphone and headphones in a non-elevated interactive
session. Held PTT recognized "Kora, what can you do?" at confidence
`0.82509285`, admitted one `ShowHelp` command and completed the spoken response.
Kora remained responsive and an ordinary tray exit removed the continuity
marker after verified clean shutdown. This is partial A01/A02/A07 evidence,
not full R03 acceptance. No ambient response without PTT is expected because
production wake is unavailable. Lock/disconnect/suspend, permission/device
mutation, takeover/return, failure recovery, output rerouting and the complete
input/accessibility matrix remain untested; see the
[shared deferred-validation register](../Design/Deferred_Validation.md#2026-10-05-bounded-interactive-result).

A later bounded continuation passed Space and Enter PTT, focus-loss closure,
silent activation, explicit voice Preview and Stop speaking. Exit during active
speech initially exposed UI-context and WASAPI lock deadlocks; after correction,
the process exited, text-to-speech disposed and the continuity marker was
automatically removed. Release x64/Debug x64 ownership decline, accepted
takeover and exact-original return also passed. Release x86 execution remains
blocked by the missing x86 .NET Desktop Runtime and must be proven through the
installer/runtime acceptance session; no runtime was downloaded during this
trial.
