# Review a registered setting suggestion

Ask for a suggested change to an existing supported Kora preference in an
Active conversation. An admitted model's answer is **inert text**, never a
command, approval or write receipt.

The supported subset is closed:

| Setting | Suggested value | Review channel |
| --- | --- | --- |
| `appearance.theme` | `system`, `light`, `dark` | Original local UI, native review and confirmation |
| `appearance.reader-text-scale` | Integer 75-200 percent | Original local UI, native review and confirmation |
| `speech.summary-words` | Integer 1-80 | Native or existing exact typed/activated-voice review |
| `speech.summary-sentences` | Integer 1-3 | Native or existing exact typed/activated-voice review |
| `speech.playback-volume` | Integer 0-100 | Native or existing exact typed/activated-voice review |

For example, the complete answer may be `set appearance.theme to dark`,
`set speech.summary-words to 40`, or `set speech.playback-volume to 30`.
Numeric speech suggestions use canonical decimal digits, without signs,
leading zeroes, fractions or commentary. Mixed settings, batches, resets,
relative changes and other setting families are not admitted as suggestions.

## Review, confirm or cancel

1. In Sessions or its compact conversation, choose **Review setting suggestion...**
   on the exact retained successful answer. The supported IDs and bounds are
   listed beside the review. Opening or reading that list saves nothing.
2. Inspect the exact current/proposed value, saved/default provenance, units,
   shared device-local scope and effects. Speech limits show **both** caps and
   preserve the companion cap. Volume shows desired/effective percentages and
   explains that zero blocks future synthesis while retaining full visual output.
3. Separately choose **Confirm exact setting change** to consume this review
   once through the same validated, audited writer as the existing controls.
   Or choose **Cancel setting review**; no new write is requested.

Only one native setting review is active at a time. A new review retires the
old one. The original request, answer, session, preference revision, private
control/ownership and call admission remain bound throughout source checks and
confirmation. A copied message, stale page, changed settings or a changed source
cannot silently select a newer answer. Speech and volume reviews expire after
60 seconds. Closing or losing private control retires the review.

## Activated voice and mandatory exceptions

The existing exact **review speech limit suggestion**, **confirm speech limit
change**, **cancel speech limit review** and **review volume suggestion**,
**confirm volume change**, **cancel volume review** operations remain available.
Activated voice requires the current assistant-name prefix, voice consent and
eligible call policy. They use the exact host-pinned successful session request,
never a guessed latest answer. No new static setting aliases are added.

Original voice provenance survives later typed/native confirmation; a protected
or unknown call cannot be laundered into local UI authority. Appearance stays
native-only and requires an original local UI request. Authentication, secure
native workflows, privacy and pending question/approval exceptions are unchanged.
A generic "yes", the model's answer or the original request cannot confirm.

## Recovery and availability

Review and cancellation never invoke a model, synthesize audio or replay an
answer. Confirmed speech changes apply to the **next** eligible ordinary speech,
not truncation or regeneration of the current answer. No system/device/call
volume, provider, microphone, consent, grant or unrelated preference is changed.

A failed terminal audit or readback can follow a persisted write. **Unconfirmed
is not success or rollback.** Inspect Settings and audit receipts, explicitly
repair unavailable/unconfirmed state where required, then start a fresh review.
There is no automatic retry, rollback or successful-write fallback.

Unqualified/unavailable models remain unavailable. This workflow neither
installs nor enables a provider and makes no hardware, acoustic or installed
accessibility qualification claim. See [Settings](settings.md) for direct
controls and option-specific recovery.
