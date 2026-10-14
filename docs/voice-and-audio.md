# Voice, microphone, and speakers

## Microphone selection

[Windows-native speech rate](settings.md#windows-native-speech-rate) is a
separate Kora-owned Windows-provider preference: canonical integer -10..10,
engine-normal/default/reset 0, with no percentage or words-per-minute mapping.
Native Settings and exact `list rate settings` / get/status/set/reset
`speech.windows-rate` share admitted audited persistence. Kokoro explicitly
does not support this option and its synthesis remains unchanged. Saving retires
active/queued output; only future eligible Windows synthesis consumes the native
setter. There is no replay, automatic trial, capture/consent, provider/asset or
global SAPI/mixer/default-device effect. Unknown/corrupt/unconfirmed state stays
explicitly unavailable; acoustic qualification is not claimed.

[Kora playback volume](settings.md#kora-playback-volume) is independent of
microphone selection and consent. It is integer 0-100, default 100 (original
unscaled output), applying only to owned Windows/Kokoro speech. Zero prevents
synthesis and automatic playback with full visual recovery. Settings and exact
`list volume settings` / `get/status/set/reset speech.playback-volume` share
audited admitted persistence. Saving retires old output; raising/resetting
never replays it. No global/call volume, microphone, permission, provider,
rate or output-device policy changes. Unknown capability/evidence is explicit
visual unavailability; hardware/acoustic audibility is not claimed.

[Current-run manual call commands](commands.md#exact-current-run-manual-call-control)
are reserved exact typed/PTT phrases, also installed in the current-prefix
activation/recognition grammar. They are not ambient call detection or a wake
engine. Inspection is cached and never speaks or opens capture. Changed manual
state retires pending speech/input generations and late speech from old
requests; off/reset does not replay, grant consent or release the listening
hold. Use a separate fresh Enable listening/PTT when all current gates allow.
Protected/Unknown calls refuse original voice mutations, not merely the final
confirmation channel. Required evidence failure stays explicitly conservative.

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
The microphone stays closed until explicit push-to-talk or an admitted native
question reply action. Hold **Push to talk**
in the native Speech & audio settings (mouse, Space or Enter), speak one
command, then release to finish. Losing the capture control/window also ends
capture. Each answer or approval requires a new activation. Transcripts use the
same deterministic command pipeline as typed input; activation alone approves
nothing.

### Answer one native question by voice

The live foreground native question offers **Answer by voice** and the separate
**Hear question, then answer by voice** action. Neither enables listening,
grants microphone consent, selects another microphone, or releases a run hold.
All existing Windows privacy, ownership, local-recognizer, exact-device,
readiness and call-policy checks must already pass. Unknown state denies capture.
There is no global conversational-replies preference in this increment.

Only the authoritative host's exact current question/revision and original
session/generation can offer a reply. Retained/background questions, generated
prose, tools, skills, duplicate presenters and selected-session metadata cannot.
The hear-then-answer action keeps the microphone closed throughout playback and
opens only after successful completion of that exact question's speech.
Playback rejection, cancellation or failure never opens capture.

An accessible **Awaiting reply** countdown gives 15 seconds to begin speech.
Silence does not extend it. **Capturing reply** uses the existing local
single-utterance recognizer: one-second trailing silence and a maximum of
60 seconds. **Cancel reply** closes capture without answering the question.
Timeout leaves the native question answerable. No button release, assistant-name
prefix, wake listener or during-speech barge-in is used.

Only the exact question schema accepts a reply: a complete option label,
one-based option number / `option N`, or a permitted bounded text answer.
Yes/No are accepted only when that schema permits them; the local-version
question instead requires **Show local version** or its option number.
Proposal/approval questions are refused by this voice path. A mismatch reports
no answer and cannot become a command, tool request, approval, composer input,
session switch or answer to another question.

Answer, cancellation (including external cancellation of the original waiting
task), timeout, expiry, edits, foreground/presenter/session or
control changes, mute/listening disable, privacy/ownership loss, call/device/
topology changes, recognition/playback failure and app close retire the target,
invalidate delayed generations and release capture. Afterwards the microphone
is closed; ordinary explicit PTT readiness may remain, never ambient listening.
Audio and rejected/raw transcripts are not persisted or logged, sent to a model
or network, or added to telemetry. An accepted schema-valid structured answer
uses the existing question record and typed audit transaction, not a new
transcript archive. Deterministic fake-platform tests establish this bounded
implementation; they do not claim wake/acoustic or installed qualification.

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

The separate [device-default response mode](settings.md#device-default) has
shared native and exact typed/activated get/set/reset/discovery for Hybrid,
VoiceOnly and VisualOnly only. It does not change endpoints, speech choices,
capture, gain or Windows mixer settings. Task/queue and call/privacy suppression
retain precedence. Save/reset retires stale Kora output without autoplay/replay;
required complete visual warnings/questions/approvals and interrupted responses
remain visible even in VoiceOnly. Invalid storage/evidence requires explicit
inspection/recovery, not a silent default.

The audio output list contains:

- **System** - follows the live Windows multimedia-default output;
- each active Windows render endpoint - displays an existing exact saved pin.

Refresh metadata, choose an exact presented endpoint, and explicitly save; reset
selects System and removes only Kora's override. Native Settings and exact
`list output settings` / get/status/set/reset `speech.output-device` commands
share persisted host session/generation admission, original-channel call/privacy
gates and audited atomic persistence. Neither draft selection nor save/reset
starts audio or changes Windows defaults, global mute/volume, microphone consent,
provider/voice or summary settings. Stale choices/evidence failures require
explicit recovery, not substituted endpoints or replay. Full visual recovery is
mandatory for missing/muted/zero-volume/open/playback failure; acoustic audibility
and full R10/native audio acceptance are not claimed.

System resolves the current multimedia default for each fresh eligible request.
Changed output metadata retires stale queued/in-flight Kora output without replay;
the underlying System WASAPI route follows Windows routing, not a saved endpoint pin.
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

Maintained deterministic/native-seam checks and bounded physical PTT/handoff
trials cover only their recorded scope. They do not qualify production wake,
the complete A01-A07 matrix, acoustic playback rejection or installed profiles.
Failed and successful historical attempts remain in the
[R03 receipts](../Design/Archive/Deferred_Validation_2026-10-11.md#r03-windows-ownership-and-audio-privacy).
Use the [live validation register](../Design/Deferred_Validation.md#r03-windows-ownership-and-audio-privacy)
for remaining evidence and approval prerequisites.
