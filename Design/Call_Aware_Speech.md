# Call-Aware Speech and Voice Configuration

Status: proposed. The speech gate and voice configuration are required from Slice A; automatic tool detection is best-effort and capability-tested, with Teams the first target.

Related: [Proactive Interaction](Proactive_Interaction.md), [Task Lifecycle](Task_Lifecycle.md), [Environment Setup](Environment_Setup.md), [Security](Security_Data_Flows.md).

## Default Behaviour

Suppress automatic speech during a detected/suspected call, or while an enabled detector cannot establish current state.
This covers requested answers, proactive suggestions, summaries, approval readbacks, and setup/maintenance prompts.
Use visual output and explain the suppression reason; do not silently discard the answer or cancel its task.
Only an explicit speak-once request can bypass this call rule under the default policy.

Call gating does not itself disable wake listening.
The user can still say "Kora, ..." to request work or inspect preferences, but voice-initiated voice-setting and in-call-option changes are rejected while call protection applies.
Voice activation during detected calls is an independent device-local setting
and remains enabled by default. If the user disables it, an Active or Suspected
observation closes active capture and blocks new activation until the call
clears; it never grants listening consent or silently reopens capture.
The same gate blocks Awaiting Conversational Reply and prefix-free answer
capture. A visual question shown during that state cannot bypass disabled
call-time activation merely because Kora asked it before the observation
changed.
Locked-session microphone and speech restrictions remain mandatory and cannot be overridden.

The separate in-call feedback override defaults to UI-only, with Voice/UI/Both/Inherit choices through the shared settings registry.
It takes precedence over ordinary task/queue/session/device response mode, subject to mandatory privacy/lock/mute/speech policy and an explicitly permitted single-response exception.
Inherit restores ordinary output precedence; this setting remains independent of voice activation and does not implicitly relax separate speech-suppression rules.
The model-facing `calls.*`, `speech.*`, and `settings.*` boundaries are defined in [Internal Model Tools](Internal_Model_Tools.md).

## Ignoring Reusable Grants During Calls

`calls.ignoreReusableGrants` is a separate device-local Boolean setting, **On by default**, discoverable/changeable through the shared voice/UI settings registry.
When it applies, the host ignores Session and Perpetual grants for authorization and requires a fresh single-use approval for each exact operation that normally requires a grant.
It does not block all operations: trusted voice or UI approval can authorize that invocation once, subject to normal input, risk, resource, and OS/provider checks.
Grant-free help/status and deterministic safety controls remain available.

Use normalized call evidence, not the selected feedback mode: manual Active or any enabled Active/Suspected source activates this gate.
An enabled Unknown source is conservatively protected while this setting is On; stale/failed detection is not clearance.
With no configured detector and no manual call state, disclose unavailable automatic protection and use ordinary grant rules.
Speak-once requests, UI-only/Voice/Both feedback changes, and relaxed speech suppression do not disable this independent authorization setting.

Ignoring is temporary non-use, not revocation, consumption, editing, expiry, or eviction of the reusable grant.
The inventory/explanation shows "Ignored during call; single-use approval required" and retains the stored scope.
After verified clearance or explicit disabling of this setting, still-applicable reusable grants become eligible under ordinary policy; ended session grants and removed/inapplicable grants do not.
Do not turn call clearance into approval of an unanswered prompt or replay an interrupted invocation.
Creating/editing a Session/Perpetual grant during a call can configure future reuse, but cannot bypass the fresh single-use requirement for the current invocation.

Revalidate call evidence and setting/policy generation immediately before every dispatch, including each step of background/queued work.
Advance this generation on effective protection/setting transitions, not unchanged detector polls; overlapping Active sources do not create a false clearance.
A call entering the protected state invalidates undispatched authorizations relying on reuse or a pre-call approval; request fresh single-use approval under the current call policy.
Bind that approval to the exact invocation and call-policy generation and consume it atomically once; changed effects or a new protected-call generation require reassessment.
Already dispatched effects are not undone or claimed cancelled solely because a call starts; report actual completion/cancellation/unknown outcomes.
Models, skills, and detector adapters cannot toggle the setting directly. Turning it Off is a protection downgrade requiring exact trusted confirmation; during a protected call it must originate from a new UI request, not voice.
Approval output follows the separate feedback policy (UI-only by default during calls); voice input remains independently eligible.

## In-Call Settings Origin Gate

While manual Active, any enabled Active/Suspected source, or enabled Unknown call evidence applies, the host rejects voice-initiated changes to voice settings and **any setting related to in-call options**.
This covers input/output/activation preferences, in-call feedback, grant-ignore, call speech/Unknown handling, detector enablement/account, manual call-state changes, temporary overrides, speak-once call exceptions, and reset/undo operations affecting these options.
The restriction applies to all scopes, including ordinary device/session/task settings whose voice effects would otherwise be masked by an in-call override.
Inspecting settings/status remains permitted; unrelated settings are not made UI-only.

Mark affected options/effects in the host-owned registry. Preserve trusted initiating-channel lineage across model interpretation, tasks, proposed settings, and tool calls; model claims or a later approval click cannot relabel a voice-originated request.
Reject such requests without mutation, a confirmation proposal, or deferred application when the call ends.
Explain the rejection visually and offer the settings surface; the user must initiate a fresh UI/mouse/keyboard change with normal validation and confirmation.
Unknown initiating-channel provenance is not treated as UI authorization.
Check protected call evidence both when accepting the request and immediately before applying it; entering a protected call invalidates pending voice-originated changes.
These controls are host policy, not another voice-configurable bypass switch.

Deterministic safety actions such as stop speech, mute/release capture, and cancellation remain eligible through voice; they are not configuration writes and cannot implicitly unmute, reopen capture, or clear call protection.
Fresh single-use approval for an ordinary operation remains independently eligible through voice/UI under the action-authorization policy; the settings restriction does not prove that voice input is free from conference/media playback.
It also covers frequent-speaker learning/profile changes; read-only profile status and default voice availability remain independent.
This is an explicit privacy/security exception to ordinary voice/UI parity, not a claim that all mouse input is stronger authentication.
The [accepted Windows-session trust boundary](Security_Data_Flows.md#trust-boundaries) does not make speaker identity or perfect external-playback rejection mandatory for baseline voice authorization.

## Built-In Gate, Extensible Detectors

All TTS/playback paths use one host-owned speech-policy service.
Detectors supply observations; they cannot play audio, change policy, or grant an override.
The host evaluates session state, voice consent, mute/quiet settings, playback safety, capture/prompt ownership, and call policy before enqueueing and starting speech.
Re-evaluate during playback; a new blocking call observation stops current speech rather than only affecting the next response.

Detector observations contain source/account/device scope, state, observation time, freshness limit, evidence class, and health/limitations.
Normalised states:

- Active: a supported call signal reports active communication.
- Suspected: evidence suggests communication but cannot establish an actual call.
- Clear: the detector's supported checks currently report no call, within its declared scope.
- Unknown: stale, inaccessible, ambiguous, or failed state.
- Disabled/Not Configured: no detection promise for that source.

Any enabled Active/Suspected source gates speech.
Without such a source, an enabled Unknown source also gates speech by default.
An enabled detector does not become Clear merely because a request failed, credentials expired, or the app produced no recent audio.
Clear means clear within tested coverage, not proof that the user is in no call anywhere.

If no detector is configured, show "automatic call detection unavailable", offer setup/manual call mode, and preserve ordinary speech policy.
Do not silently claim call protection or make first-run speech permanently unavailable.
Disabling a detector is an explicit preference change, not an automatic recovery from a failed probe.

## Teams Detection

Prefer supported local metadata/signals where validated; do not rely on private Teams databases, unsupported log scraping, or screen/audio recording.
Windows audio-session metadata can provide supporting evidence, but audio activity alone does not identify a call.
Exclude Kora's own capture/playback from candidate activity.
Test muted calls, output-only calls, device changes, client upgrades, and multiple clients; a process/window being open is insufficient.

An optional Microsoft Graph adapter can read the configured user's Teams presence:

- Use delegated `Presence.Read` for the user's own work/school account; do not request tenant-wide or write permission for this feature.
- Personal Microsoft accounts are not supported by this Graph operation.
- Interpret `inACall` and `presenting` as blocking communication/activity signals.
- Calendar-derived `inAMeeting` is a conservative Suspected signal, not proof of joining a call.
- Generic Busy/DND is not asserted as a call; an independent preference can suppress speech for those states.
- Account presence is aggregated across sessions/devices and can be delayed or overridden; disclose that scope.
- Offline, missing, stale, unsupported, or inconsistent state must not be interpreted as authoritative local clearance.

Proposed polling interval: 15 seconds when enabled, with bounded requests/backoff; a sample older than 60 seconds becomes Unknown.
Those limits describe observation freshness, not guaranteed Teams backend latency.
Graph detection requires explicit account/network consent and is unavailable during local-only operation.
If an enabled Graph detector becomes unavailable under that policy, it remains Unknown until the user chooses another detector or disables it.

Teams automatic detection needs an integration proof on supported clients/accounts.
If no reliable local signal is available and Graph cannot be used, offer manual call mode and explain the limitation.
Do not make an unsupported platform claim to satisfy the feature.

## Other Communication Tools

Use narrow detector adapters for supported Zoom, Slack, Discord, browser calling, or other tools when reliable signals are available.
These are future integrations, not claims of initial coverage.
A generic "communication app audio active" heuristic is Suspected and labelled as such.
Do not request meeting contents, participants, transcripts, or audio samples solely to control speech.
Calendar events may support a separate conservative meeting preference but cannot establish actual attendance.

## Voice Configuration

Settings are host-owned, device-local preferences, not editable application policy code.
Use the shared registry and voice/UI contract in [User Configuration](User_Configuration.md); detector-specific settings are not UI-only.
Outside protected calls, route unambiguous voice commands to typed preference operations; ask for clarification about ambiguous scope/duration.
During protected calls, reject affected voice-originated changes under the [settings origin gate](#in-call-settings-origin-gate); do not offer a voice-confirmable downgrade.
Apply the chosen setting immediately, display the effective policy, and speak acknowledgement only if the resulting policy allows it.

Examples:

| Utterance after "Kora" | Effect |
|---|---|
| "Don't speak while I'm in a call" | Persist suppression for all automatic speech during calls |
| "During calls, only speak when I ask" | Select default suppression with speak-once override |
| "Only suppress unsolicited suggestions during calls" | Persist a policy allowing requested answers but not proactive speech |
| "Stay silent when you can't tell whether I'm in a call" | Persist conservative Unknown handling |
| "Use normal speech when call detection is unavailable" | Open an exact voice/UI confirmation for the reduced-protection Unknown fallback |
| "I'm in a call" | Set manual Active override until explicitly cleared |
| "My call has ended" | During protected calls, reject the voice-originated manual-clear change; require a new UI request, and enabled automatic sources still apply |
| "Ignore saved grants while I'm in a call" | Enable fresh single-use approval instead of Session/Perpetual reuse during calls; preserve stored grants |
| "Use my saved grants during calls" | Outside protected calls, propose the confirmed downgrade; during protected calls reject voice origin and require a new UI request |
| "Read that answer aloud once" | Normal speech policy applies outside protected calls; a speak-once call exception must be UI-initiated while protected |
| "Allow normal speech for this call" | Open exact voice/UI confirmation for a bounded call-wide relaxation; default expiry 1 hour or observed call end, whichever is first |
| "Restore call-aware speech defaults" | Reset configurable call preferences and temporary overrides |
| "What are my call speech settings?" | Show policy, sources, freshness, and override state; speak only if permitted |

All example setters/resets/temporary overrides follow the origin gate; voice availability outside calls does not permit voice configuration changes during calls.
An explicit persistent request can propose normal speech during calls, but deliberate exact voice or UI confirmation is required before the privacy downgrade and it never overrides lock, microphone consent, user mute, an enabled owner-aware private-speech policy, or another mandatory rule.
Speak-once tokens bind a response/prompt ID and current policy generation, expire after 2 minutes, and are consumed once.
They cannot drain accumulated speech or change persistent settings.
Outside protected calls, voice may request a single identified response; a call-policy exception during a protected call requires fresh UI initiation. Ambiguous intent or an unsatisfied optional speaker preference requires clarification/explicit alternate-channel confirmation.
Temporary overrides are revoked on lock/sign-out/restart; unresolved call-end detection cannot extend their time limit.
Intent/target ambiguity produces visual clarification while speech is gated.

## Deferred Speech

Continue visual results and task state while speech is suppressed.
Do not queue a backlog of spoken answers or replay all old suggestions when the call ends.
At most offer one still-relevant summary after call clearance, subject to proactive policy.
An unseen approval does not become eligible for voice acceptance merely because its readback was suppressed.
Keep prompt identity/expiry and visual approval behaviour intact.
Activation cues must also respect call quieting; use visual feedback rather than an audible cue when gated.

## Reference Evidence

- [Teams presence behaviour](https://learn.microsoft.com/en-us/microsoftteams/presence-admins): calendar-derived meetings and multi-device status.
- [Graph presence permutations](https://learn.microsoft.com/en-us/graph/cloud-communications-manage-presence-state): `busy/inACall`, `busy/inAMeeting`, and session aggregation.
- [Get own presence](https://learn.microsoft.com/en-us/graph/api/presence-get?view=graph-rest-1.0): delegated `Presence.Read` and account support.
- [Windows audio sessions](https://learn.microsoft.com/en-us/windows/win32/coreaudio/audio-sessions): audio stream grouping, not a universal call-state API.
