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
| `appearance.presence-display` | `true`, `false` | Original local UI, native review and confirmation |
| `appearance.presence-timeout` | Integer 1-60 seconds | Original local UI, native review and confirmation |
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

For example, the complete answer may be `set appearance.theme to dark`,
`set appearance.movement-speed to 50`, `set speech.summary-words to 40`, `set speech.playback-volume to 30`, or
`set responses.default-mode to VoiceOnly`.
For slower Windows speech, the complete retained answer may be
`set speech.windows-rate to -1`. Rate alone permits canonical negative integers;
plus signs, negative zero, padding, leading zeroes, suffixes and commentary are rejected.
Other numeric speech suggestions use canonical decimal digits, without signs,
leading zeroes, fractions or commentary. Mixed settings, batches, resets,
relative changes and other setting families are not admitted as suggestions.
Response-mode answers require the exact spelling and case shown above; numeric
enum values, aliases, surrounding commentary and batches are rejected.
Presence suggestions likewise require the exact lowercase syntax and IDs above,
canonical decimal integers or lowercase `true`/`false`, with no surrounding text.
Response timeout and native renderer selection are not admitted model suggestions.
This suggestion registry does not expand setting inspection: inspection remains
limited to theme, reader text scale, both summary caps, playback volume and response mode.

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
   Only the already available Windows provider/adapter can apply; Kokoro is unchanged.
   Presence changes affect the existing display and animation immediately after a
   confirmed save. Disabling presence hides it; speech scaling only changes its
   animation, never speech permission, synthesis or playback.
3. Separately choose **Confirm exact setting change** to consume this review
   once through the same validated, audited writer as the existing controls.
   Or choose **Cancel setting review**; no new write is requested.

Only one native setting review is active at a time. A new review retires the
old one. The original request, answer, session, preference revision, private
control/ownership and call admission remain bound throughout source checks and
confirmation. A copied message, stale page, changed settings or a changed source
cannot silently select a newer answer. Speech, volume and response-mode reviews expire after
60 seconds, including Windows rate. Closing or losing private control retires the review.

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

Original voice provenance survives later typed/native confirmation; a protected
or unknown call cannot be laundered into local UI authority. Appearance stays
native-only and requires an original local UI request. Authentication, secure
native workflows, privacy and pending question/approval exceptions are unchanged.
A generic "yes", the model's answer or the original request cannot confirm.

## Recovery and availability

After a confirmed registered change, [Undo the last settings change](settings.md#undo-the-last-registered-settings-change) offers one exact run-only preference restoration through separate review and confirmation. A model suggestion cannot invent the prior value or create undo authority; the host receipt and current authoritative writer decide eligibility ([undo owner](../src/Kora.Application/Configuration/RegisteredSettings/RegisteredSettingUndo.cs)).
Windows rate is not added to that undo subset; direct rate Reset remains available
through its existing admitted controls.

Review and cancellation never invoke a model, synthesize audio or replay an
answer. Confirmed speech changes apply to the **next** eligible ordinary speech,
not truncation or regeneration of the current answer. No system/device/call
volume, provider, microphone, consent, grant or unrelated preference is changed.
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
