# Review a registered setting suggestion

Ask for a suggested change to an existing supported Kora preference in an
Active conversation. An admitted model's answer is **inert text**, never a
command, approval or write receipt.

Only the following settings support this workflow. The exact option IDs are
shown because the suggested command must use them; ordinary Settings controls
do not require you to remember these names.

| Setting | Suggested value | Review channel |
| --- | --- | --- |
| `appearance.theme` | `system`, `light`, `dark` | Original local UI, native review and confirmation |
| `appearance.reader-text-scale` | Integer 75-200 percent | Original local UI, native review and confirmation |
| `appearance.reduced-motion` | `true`, `false` | Original local UI, native review and confirmation |
| `appearance.presence-display` | `true`, `false` | Original local UI, native review and confirmation |
| `appearance.presence-timeout` | Integer 1-60 seconds | Original local UI, native review and confirmation |
| `appearance.response-timeout` | Canonical integer 1-60 seconds, default 5 | Native or exact typed/activated-voice review |
| `appearance.presence-size` | Integer 240-600 pixels | Original local UI, native review and confirmation |
| `appearance.dot-size` | Integer 50-200 percent | Original local UI, native review and confirmation |
| `appearance.dot-density` | Integer 25-200 percent | Original local UI, native review and confirmation |
| `appearance.movement-speed` | Integer 25-200 percent | Original local UI, native review and confirmation |
| `appearance.speech-scaling` | `true`, `false` | Original local UI, native review and confirmation |
| `appearance.speech-scale-amount` | Integer 0-200 percent | Original local UI, native review and confirmation |
| `speech.summary-words` | Integer 1-80 | Native or existing exact typed/activated-voice review |
| `speech.summary-sentences` | Integer 1-3 | Native or existing exact typed/activated-voice review |
| `speech.playback-volume` | Integer 0-100 | Native or existing exact typed/activated-voice review |
| `responses.default-mode` | Exact `Hybrid`, `VoiceOnly`, `VisualOnly` | Native or exact typed/activated-voice review |
| `speech.windows-rate` | Canonical integer -10 through 10, normal 0 | Native or exact typed/activated-voice review |
| `speech.voice` | Exact installed `provider / voice ID` supplied in your request | Native or exact typed/activated-voice review |
| `voice.command-start-wait` | Exact decimal 2-10 seconds, default 5 | Separate current native review and confirmation |
| `voice.reply-start-wait` | Exact decimal 5-60 seconds, default 15 | Separate current native review and confirmation |
| `voice.trailing-silence` | Exact decimal 0.5-3 seconds, default 1 | Separate current native review and confirmation |

For example, the complete answer may be `set appearance.theme to dark`,
`set appearance.reduced-motion to true`, `set appearance.movement-speed to 50`,
`set speech.summary-words to 40`, `set speech.playback-volume to 30`, or
`set responses.default-mode to VoiceOnly`.
For slower Windows speech, the complete retained answer may be
`set speech.windows-rate to -1`. Rate alone permits canonical negative integers;
plus signs, negative zero, padding, leading zeroes, suffixes and commentary are rejected.
Other numeric speech suggestions use canonical decimal digits, without signs,
leading zeroes, fractions or commentary. Mixed settings, batches,
relative changes and other setting families are not admitted as suggestions.
Response-mode answers require the exact spelling and case shown above; numeric
enum values, aliases, surrounding commentary and batches are rejected.
Presence suggestions likewise require the exact lowercase syntax and IDs above,
canonical decimal integers or lowercase `true`/`false`, with no surrounding text.
Response-timeout suggestions use the same canonical lowercase syntax, for example `set appearance.response-timeout to 15`. This is the response-window inactivity hide delay, not a provider/request deadline ([existing preference](settings.md#independent-visibility-timeouts)).
Native renderer selection is not an admitted model suggestion.
During a protected call, a fresh native appearance review and separate local
confirmation remain available under the table's existing channel restrictions.
They never clear call protection or enable speech. A changed call observation,
private host or source requires a fresh review; a later click cannot relabel a
voice-originated suggestion as an original local source.

For capture timing, the complete retained answer may be `set voice.trailing-silence to 2.5000001` or `reset voice.reply-start-wait`.
Seconds use a canonical dot decimal with at most seven fractional digits; no exponent, suffix, padding, redundant zeros, rounding or batch is accepted.
Review reads the authoritative current timing service and shows all three old/new values and saved/default provenance.
Separate native confirmation saves only the selected field's change, preserving companions.
Timing and appearance use distinct review owners; an appearance review cannot accept a timing Reset.
The save affects future eligible captures only; it never replaces an in-flight snapshot, opens a microphone, selects a provider or grants consent.
An unavailable timing service blocks review. Timing values remain unavailable through `settings.get`; suggestion metadata is not confirmed-value read authority.
This suggestion registry does not expand setting inspection: inspection remains
limited to theme, reader text scale, both summary caps, playback volume and response mode.

For an [installed voice](voice-and-audio.md#review-an-installed-voice-suggestion), supply its exact qualified ID in your request. The complete answer must be `set speech.voice to <provider / voice ID>`.
Kora validates that exact installed pair privately; the model cannot enumerate choices, select a provider alone, download assets or invent an available voice ([parser](../src/Kora.Application/Configuration/RegisteredSettings/RegisteredSettingSuggestions.cs)).

## Propose one Reset

The complete retained answer may instead be exactly `reset <exact ID>` for one setting in the table except `speech.voice`, for example `reset speech.playback-volume`.
Installed voice remains Set-only and requires an exact installed pair. No value, model-supplied default, aliases, commentary or batch is accepted ([parser](../src/Kora.Application/Configuration/RegisteredSettings/RegisteredSettingSuggestions.cs)).

Use the same separate review and confirmation below. Review resolves the default through the host, shows current and proposed values/provenance and preserves the exact source and original channel. Reset does not mean every owner removes its saved preference:

- Appearance saves the option's default as a **saved** value when changed. If it already equals the default, Reset is unchanged and preserves its existing saved/default provenance; other appearance options are unchanged ([owner](../src/Kora.Application/Configuration/AppearanceConfigurationService.cs)).
- Response-timeout Reset follows those same appearance-owner semantics with the authoritative 5-second default. It retains its existing native/typed/activated-voice review, 60-second expiry and original-channel call/privacy checks; other appearance settings remain native-only ([review](../src/Kora.Application/Configuration/AppearanceSuggestionReview.cs)).
- A summary cap saves the complete pair with that cap reset and the known companion unchanged. An unreadable companion blocks review; it is never defaulted ([owner](../src/Kora.Application/Configuration/SpeechConfigurationService.cs)).
- A capture timing Reset saves the complete timing tuple with only that field restored to its default and both known companions unchanged; it does not remove the saved tuple ([owner](../src/Kora.Application/Configuration/VoiceCaptureTimingConfigurationService.cs)).
- Volume and Windows rate remove their saved preference and use the authoritative **unsaved default** ([volume](../src/Kora.Application/Configuration/PlaybackVolumeConfigurationService.cs), [rate](../src/Kora.Application/Configuration/WindowsSpeechRateConfigurationService.cs)).
- Response mode saves **Hybrid**, matching native Reset; it does not remove the preference or change session/task/call overrides ([native route](../src/Kora.Application/ViewModels/MainViewModel.ResponseModeConfiguration.cs)).

Cancel requests no write. Reset grants no model undo authority and adds no settings, provider/device, consent, grant, retention or speech operation.
Existing review-channel restrictions and uncertain-write recovery remain unchanged ([review owners](../src/Kora.Application/Configuration/AppearanceSuggestionReview.cs), [volume review](../src/Kora.Application/Configuration/PlaybackVolumeSuggestionReview.cs)).

## Review, confirm or cancel

1. In Sessions or its compact conversation, choose **Review setting suggestion...**
   on the exact retained successful answer. The supported IDs and bounds are
   listed beside the review. Opening or reading that list saves nothing.
2. Inspect the exact current/proposed value, saved/default provenance, units,
   shared device-local scope and effects. Speech limits show **both** caps and
   preserve the companion cap. Volume shows desired/effective percentages and
   explains that zero blocks future synthesis while retaining full visual output.
   Response mode shows saved/default provenance, desired and configured effective
   modes, the exact proposal, future-output timing and mandatory visual/privacy/call
   exceptions. It changes the shared device default, not session/task/queue/call overrides.
   Windows rate shows provider/support, saved/default provenance and desired/effective
   native units (-10 through 10, normal 0), not percentages or a universal speed scale.
   It uses the same guarded, validated writer as Settings > Speech & audio.
   Capture timing likewise uses its existing timing writer, requires separate current native confirmation, and expires after 60 seconds.
   Cancel remains available while its source check or confirmation is awaiting. Late completions cannot revive the cancelled review.
   A write already started is joined, not rolled back; an unconfirmed save requires Settings and audit inspection, never automatic retry.
   Only the already available Windows provider/adapter can apply; Kokoro is unchanged.
   Installed voice review shows both current/proposed provider and voice, the current effective voice, saved/default provenance and the retained-answer/installed-catalogue binding. Confirmation saves the pair atomically, then checks exact persisted/current readback ([review owner](../src/Kora.Application/Configuration/InstalledSpeechVoiceSuggestionReview.cs)).
   Presence changes affect the existing display and animation immediately after a
   confirmed save. Disabling presence hides it; speech scaling only changes its
   animation, never speech permission, synthesis or playback.
   Reduced motion freezes presence particle movement in both animation modes.
   False/reset follows Windows and cannot enable motion against Windows reduced
   motion. Speech sizing and brief colour and visibility transitions remain independent.
   Response timeout changes only the existing inactivity hide delay. Always show, presence timeout, questions, approvals and response action links retain their existing visibility behavior ([visibility rules](settings.md#independent-visibility-timeouts)).
3. Separately choose **Confirm exact setting change** to consume this review
   once through the same validated, audited writer as the existing controls.
   Or choose **Cancel setting review**; no new write is requested.

Keyboard activation of **Review setting suggestion...** moves focus to the
complete readback in either conversation view. Focus and Enter on the readback
do not confirm a change. **Escape** from the readback or its confirmation/cancel
buttons cancels only this setting review and returns focus to the same answer's
review action when that exact source is still available. Escape outside these
review controls keeps its existing search/window behavior. After cancellation,
review the same answer again and confirm separately; no new model request is
needed. Confirmation likewise returns to the source unless focus has moved
elsewhere while the operation was pending.

Only one native setting review is active at a time. A new review retires the
old one. The original request, answer, session, preference revision, private
control/ownership and call admission remain bound throughout source checks and
confirmation. A copied message, stale page, changed settings or a changed source
cannot silently select a newer answer. Speech, volume and response-mode reviews expire after
60 seconds, including Windows rate, response timeout and installed voice. Closing or losing private control retires the review.

**Cancel setting review** remains available while review or confirmation is checking its source. Cancellation retires that exact review, including work that has not returned yet; a late result cannot restore confirmation or authorize a new write. Wait for the operation to finish, then explicitly review the retained answer again and confirm its fresh current/proposed values.
This also applies to the installed provider/voice pair and native-only capture timing review in both conversation windows; a fresh confirmation still requires the exact installed catalogue or complete timing tuple and persisted/current readback ([shared review owner](../src/Kora/RegisteredSettings/SessionsViewModel.RegisteredSettings.cs), [timing review](../src/Kora/RegisteredSettings/SessionsViewModel.VoiceCaptureTimingSuggestions.cs)).
Cancellation is not rollback: if persistence has already started, inspect Settings and the exact audit receipts before a fresh review. Unconfirmed saves are never automatically retried ([shared review owner](../src/Kora/RegisteredSettings/SessionsViewModel.RegisteredSettings.cs)).

## Activated voice and mandatory exceptions

The existing exact **review speech limit suggestion**, **confirm speech limit
change**, **cancel speech limit review** and **review volume suggestion**,
**confirm volume change**, **cancel volume review** operations remain available.
Response mode uses **review response mode suggestion**, **confirm response mode
change**, and **cancel response mode review** through the same retained-source
and admitted preference writer as native review.
Activated voice requires the current assistant-name prefix, voice consent and
eligible call policy. They use the exact host-pinned successful session request,
never a guessed latest answer. No new static setting aliases are added; these
review commands do not execute model text as a direct setting command.
Windows rate uses **review rate suggestion**, **confirm rate change**, and
**cancel rate review** against that same exact successful-submission pin.
Response timeout uses **review response timeout suggestion**, **confirm response timeout change**, and **cancel response timeout review**, using the same exact retained answer and existing validated, audited appearance writer ([shared review](../src/Kora.Application/Configuration/AppearanceSuggestionReview.cs), [command route](../src/Kora.Application/ViewModels/MainViewModel.ResponseTimeoutSuggestions.cs)).
Installed voice uses **review installed voice suggestion**, **confirm installed voice change**, and **cancel installed voice review**, with the same original-source and channel restrictions ([commands](../src/Kora.Application/Configuration/InstalledSpeechVoiceSuggestionCommand.cs)).

Original voice provenance survives later typed/native confirmation; a protected
or unknown call cannot be laundered into local UI authority. Other appearance settings stay
native-only and require an original local UI request. Authentication, secure
native workflows, privacy and pending question/approval exceptions are unchanged.
A generic "yes", the model's answer or the original request cannot confirm.

## Recovery and availability

After a confirmed registered change, [Undo the last settings change](settings.md#undo-the-last-registered-settings-change) offers one exact run-only preference restoration through separate review and confirmation. A model suggestion cannot invent the prior value or create undo authority; the host receipt and current authoritative writer decide eligibility ([undo owner](../src/Kora.Application/Configuration/RegisteredSettings/RegisteredSettingUndo.cs)).
Windows rate and installed voice are not added to that undo subset; direct Reset remains available
through its existing admitted controls.
Response timeout supports the same separate native/typed/activated-voice undo review and confirmation. Undo restores one compatible saved value or the unsaved default, with no redo or provider effect ([undo writer](../src/Kora.Application/Configuration/RegisteredSettings/AppearanceConfigurationService.Undo.cs)).

Review and cancellation never invoke a model, synthesize audio or replay an
answer. Confirmed speech changes apply to the **next** eligible ordinary speech,
not truncation or regeneration of the current answer. No system/device/call
volume, microphone, consent, grant or unrelated preference is changed. Only `speech.voice` changes the provider together with its exact installed voice ([atomic writer](../src/Kora.Application/Configuration/SpeechConfigurationService.cs)).
Windows rate applies only to future eligible Windows synthesis. It never switches
provider, sets system speech speed, qualifies an adapter or claims acoustic results.

Response-mode confirmation affects the **next** eligible response only. Even
`VoiceOnly` retains the complete visual answer for warnings, security, questions,
approvals, interrupted or unavailable speech and other mandatory recovery.
Privacy and call suppression always win; saving a mode never grants speech
permission or restarts the selected answer.

A failed terminal audit or readback can follow a persisted write. **Unconfirmed
is not success or rollback.** Inspect Settings and audit receipts, explicitly
repair unavailable/unconfirmed state where required, then start a fresh review.
There is no automatic retry, rollback or successful-write fallback.

Unqualified/unavailable models remain unavailable. **This build cannot generate
new model answers**; it approves no local model and hosted account-use approval
is unavailable. Review cannot install or enable one. Use [Settings](settings.md)
for direct controls and recovery without AI.
