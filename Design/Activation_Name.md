# Custom Voice Activation Names

Status: proposed feature. "Kora" is the default; users can choose another supported local activation name.

Related: [User Configuration](User_Configuration.md), [Task Lifecycle](Task_Lifecycle.md), [Interaction Fallback](Interaction_Fallback.md), [Acceptance Criteria](Acceptance_Criteria.md).

## Product Behaviour

The application, executable, skill namespace, and update identity remain Kora.
Renaming here changes voice activation, not those identities, storage locations, or publisher trust.
PTT and native tray/mouse controls continue to work independently of the activation name.

The rename process always asks which behaviour the user wants:

1. Respond only to the chosen custom name.
2. Respond to both "Kora" and the chosen custom name.

Do not infer this preference from "call yourself Nova" or silently keep the old activation phrase enabled.
The active name set contains either the default alone, the custom name alone, or "Kora" plus one custom name.
On another rename, the previous custom name is retired; aliases do not accumulate.
If the new name is "Kora", explain that the effective name set is just "Kora", without duplicate detectors.

## Shared Offices

Custom-only is the recommended choice in offices/shared spaces: keeping "Kora" active would still wake nearby installations that respond to that name.
Explain this consequence when offering dual-name mode; do not select it by default or retain a hidden/default recovery alias.
First-run setup offers activation-name configuration before ongoing listening is enabled, with mouse controls for users who cannot yet use voice.
When the user identifies a shared-office use case, suggest a distinctive supported name and custom-only mode, but still obtain the explicit choice.
Do not infer an office from call state or silently change activation settings based on location.

Test neighbouring instances configured with distinct names: a request using one name must not activate another custom-only instance, including through acoustic playback/crosstalk.
Distinct names reduce accidental cross-activation; they are not identity verification or a guarantee against similar-sounding words, a colleague speaking the chosen name, or playback.
Existing shared-space privacy/speaker-confidence and native approval requirements remain effective.
No shared name registry, LAN discovery, or broadcast of users' activation names is required.
If custom-only recognition fails, use tray/PTT recovery rather than enabling "Kora" again.

## Voice Rename Flow

Example using the currently active default:

1. User: "Kora, change your activation name to Nova."
2. Kora resolves the exact spelling/pronunciation/language if ambiguous and validates the proposed name.
3. Kora asks: "Should I respond only to Nova, or to both Kora and Nova?"
4. User: "Kora, respond only to Nova." (Alternatively, explicitly select both.)
5. The host prepares the required local detector profile and presents any explicit setup/calibration requirements.
6. Kora shows the exact resulting active name set and any capability limitations.
7. User confirms the named proposal by voice or an equivalent native Apply control.
8. Only after readiness/validation succeeds does the host atomically publish the new configuration and detector generation.
9. Kora displays: "I now respond only to Nova." Any spoken acknowledgement follows normal playback/privacy policy.

For custom-only, instructions explain that subsequent activation/confirmation uses "Nova"; the command prefix changes only after commit.
Until commit, the existing active name set remains authoritative. A preparation/calibration utterance is not a task command or action approval.
Cancel, invalid name, unsupported profile, failed calibration/download, expiry, or persistence failure leaves the prior active set unchanged.
Use the same prompt identity, revision, expiry, and typed validation as other settings.

## Mouse and Recovery Flow

The native settings panel, reached from the system tray, contains:

- Custom activation name entry, with supported-name/pronunciation guidance.
- An explicit choice of "Custom name only" or "Kora and custom name".
- Current effective active names and pending proposal state.
- Apply, Cancel, and Restore default activation controls.

Mouse-only operation supports ordinary text entry with the on-screen keyboard route if needed; detected/supported preset names may also be selectable.
Renaming without a usable microphone can save/apply a profile only if it can be validated without live capture.
If calibration requires audio, show Pending Setup, keep the prior active configuration, and offer microphone recovery; do not claim the new name works.
Changing a name never opens a muted/locked microphone.
Restore default switches to verified "Kora" only and retires the custom alias, after explicit confirmation.
If a committed custom profile becomes unavailable/corrupt, mark voice activation Unavailable and offer tray recovery; do not silently reactivate "Kora" against the custom-only choice.

## Supported Names and Detector Requirements

Customisation requires a local detector that actually supports validated custom names/profiles.
A text setting, an arbitrary transcript filter, or ambient speech-to-text is not a custom wake detector.
Do not promise unrestricted names, languages, or pronunciation variants before the implementation proves support.

Initial proposed bounds are 1-3 words and at most 32 Unicode characters in a supported local language, subject to the detector's tighter declared phonetic/temporal constraints.
Normalise and display the exact value; reject empty/control-character/unsupported input and redundant phonetic aliases explicitly.
Explain poor candidates such as very short/common words or indistinguishable names; propose alternatives rather than silently substituting.
Preserve the 2-second maximum ambient pre-roll and local-only processing; a longer name cannot expand the privacy bound.
Name recognition remains independent of speaker-owner confidence and never enrols/authenticates a user.

The host may select a verified bundled profile or prepare a supported local data-only profile through a protected first-party setup handler.
Any required model-data download/calibration has explicit scope, provenance, consent, and an offline-unavailable outcome.
Optional calibration samples are bounded, local, memory-only and discarded after the declared operation; no raw-audio history, cloud training, or automatic speaker-profile creation.
Custom profile data lives in the device-local voice data partition, not the embedded built-in skill store or executable loading roots.
The model/skill cannot provide a builder executable, arbitrary model URL, plugin, script, resource mapping, or unvalidated profile path.
Changing activation names does not modify Kora binaries, embedded skills, wake-engine code, or security policy.

## Safe Activation Switching

Prepare/validate the entire proposed active set, including combined-detector behaviour in dual-name mode.
Atomic publication covers the saved option, selected profiles/digests, and audio generation; no half-applied preference/profile state.
Pause capture at cutover, invalidate old callbacks/activation candidates, clear old pre-roll, and resume only if the existing explicit listening consent and authoritative session state still permit it.
If muted or after restart/unlock, the name preference persists but listening remains disabled until explicit re-enabling.
Discard late old-generation results; never route a partial command across the switch or duplicate a task from overlapping detectors.
After commit, removed names cannot activate capture; spoken references to "Kora" inside a command still refer to the application.
TTS rejection covers every active name and the spoken rename prompts themselves.
No custom name or alias changes call gating, private-output rules, approval identity, lock policy, context consent, or tool permissions.

## Settings and Phrases

| Phrase after any current active name | Effect |
|---|---|
| "Change your activation name to {name}" | Start name validation and explicit custom-only/both choice |
| "Respond only to {configured custom name}" | Confirm switching the existing valid name set to custom-only |
| "Respond to Kora as well" | Confirm enabling the default alongside the configured custom name |
| "What names do you respond to?" | Report effective active names, saved preference, and readiness |
| "Restore your default activation name" | Confirm "Kora" only; retire the custom profile binding |

Names and alias mode are device-local preferences, verbally and visually settable through the same host registry.
Catalogue examples elsewhere use "Kora" as the default; use any effective active name instead after renaming.
Generic "yes" or model/skill instructions cannot invent the alias-mode selection or accept a stale proposal.

## Release Gates

Keep default "Kora" activation mandatory from Slice A.
Custom name/alias mode is an included opt-in capability whose advertised supported names/profiles must pass the same wake recall, noise, false-positive, latency, pre-roll, and playback-rejection gates.
Test both custom-only and combined-name sets, not only profiles in isolation.
Runtime calibration can check readiness but cannot substitute for release-quality measurements.
If a requested profile fails, keep the previous configuration and explain the unavailable choice; do not silently fall back to PTT, cloud transcription, or a different name.
