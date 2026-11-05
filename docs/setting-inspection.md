# Inspect confirmed current settings

**Answer generation is unavailable in this build.** You can inspect settings
directly without AI; this separate question-and-answer review cannot enable a
local model or change a preference.

Open **Settings > Appearance**, choose **Inspect confirmed current settings with
a local model**, and enter one original question. Opening Settings or the
inspection window does not read settings for a model.

Choose **Inspect confirmed settings and review**. Read the complete source,
saved/default provenance, unchanged question and **NEW Local context conversation**
target. Separately choose **Confirm ONE tool-free LocalOnly request** to submit.
This is not the ordinary model-settings suggestion route: any recommendation is
inert and changing a preference still requires its separate Review/Confirm/Apply.

For the distinct tool-enabled review, select **Use read-only registered-setting
discovery tools** before inspecting. Read the registered metadata, the closed
permitted scalar IDs, full confirmed source, original question, local destination
and expiry; then choose **Confirm ONE settings-discovery LocalOnly request**.
Changing this choice retires the previous review and requires a fresh inspection
([native review owner](../src/Kora/SettingInspection/SettingInspectionPresenter.cs)).

Only this explicit local request advertises `settings.list` and `settings.get`.
The complete reviewed metadata uses a JSON table with named `columns` and
ordered `rows`. Every registered field and confirmed-reader classification is
preserved; column names appear once so repeated local requests leave room for
discovery and the genuine final answer within the unchanged cumulative byte limit.
List accepts bounded `offset`/`count` fields and returns canonical registered
metadata, scope, validation limits, suggested values, native-review classification
and whether a confirmed value reader is composed. Get accepts only one exact
registered `id`; only the eighteen reviewed scalars below can return values.
Other registered values are explicitly unavailable, not guessed or loaded from
another source ([closed discovery contract](../src/Kora.Core/Tools/SettingsDiscoveryTools.cs)).

Each list page has at most six records. The existing serial protocol keeps its
six-call, 15-second, 32-KiB cumulative input and 4-KiB cumulative output ceilings;
an oversized result or continuation is rejected whole, never truncated.
Only the original question and genuine final answer are retained; discovery
results are volatile lower-trust data, not approval or future read authority
([bounded loop](../src/Kora.Application/Dependencies/BoundedModelTurnAdapter.cs),
[exact reviewed binding](../src/Kora.Application/Dependencies/ModelTurnHost.SettingsDiscovery.cs)).

Exactly eighteen registered observations are included: theme, reading text scale,
ordinary spoken-summary word/sentence caps, Kora-only playback volume,
device-default response mode, `appearance.presence-display`, and these existing
presence and capture-timing preferences:

| ID | Confirmed unsaved default | Units |
| --- | --- | --- |
| `appearance.presence-timeout` | 10 | seconds |
| `appearance.presence-size` | 360 | pixels |
| `appearance.dot-size` | 100 | percent |
| `appearance.dot-density` | 100 | percent |
| `appearance.movement-speed` | 100 | percent |
| `appearance.speech-scaling` | true | Boolean |
| `appearance.speech-scale-amount` | 100 | percent |
| `appearance.reduced-motion` | false | Boolean |
| `voice.command-start-wait` | 5 | seconds |
| `voice.reply-start-wait` | 15 | seconds |
| `voice.trailing-silence` | 1 | seconds |

For capture, ask "Why does capture stop or wait, and which command-start,
reply-start and trailing-silence timings are configured?" The complete review
includes all three exact timing values, units and saved/default provenance.
Fractional seconds retain domain precision (up to seven fractional digits),
without rounding or converting to whole seconds. The authoritative timing
service must already agree with the saved/default observation at its revision.
These preferences describe future eligible captures, not current microphone
state, measured acoustic behavior or permission to listen. Inspection never
starts capture, changes timing or repairs an unconfirmed preference.

For example: "Why does Kora disappear so quickly, and what presence size and
movement settings am I using?" Both modes review the full source before a
separate one-send confirmation; neither changes these preferences.
Reduced motion `false` means follow Windows, not force motion; `true` freezes
presence particles. These are confirmed saved/default intentions, not proof of
current visibility, rendered size, animation, Windows motion state or performance.
The appearance values must all match the live service and saved/default provenance
at its observed revision. A change during review, tool work or publication retires
the original source and requires fresh inspection.
Response timeout, Windows speech rate and the registered installed provider/voice
pair remain value-unavailable. Registering the voice-pair proposal does not add
its selected value to this confirmed scalar source.
No provider, account, device, permission, privacy, credential, session, path or
file content is read into this source. No setting is saved, applied or reset.
Service observations are independent, not an atomic cross-service snapshot.
Saved preferences must match the live service state; missing files mean a
confirmed default only when that service has actually observed that default.
Malformed, unconfirmed, externally changed or unavailable state blocks review.
Use existing setting recovery and explicitly inspect again; inspection never
silently reloads or applies an external edit.

The review expires after two minutes without renewal. Question edits, cancellation,
source revisions, privacy/call/input/owner changes or closing retire the review.
Confirmed state is read again before disclosure and across model/storage awaits.
Observed changes, including change-and-return, invalidate earlier observations.
Unobserved external filesystem history cannot be detected or certified.
Cancel closes answer authority immediately, but the operation remains owned until
source/model work physically joins; retained request receipts are not rolled back.

A qualified local runtime is required. The actual answer, unsupported response or
explicit unavailable/unconfirmed outcome is shown without automatic speech.
There is no hosted fallback, memory write, export or automatic retry.
The default inspection remains tool-free; the opt-in discovery mode permits
only the two read-only settings tools, never a setting write or another tool
family ([review policy](../src/Kora.Core/Configuration/SettingInspection/SettingInspectionReview.cs)).
Model interpretation is not semantic verification; compare it with the complete
reviewed values. A saved preference does not establish runtime readiness.
Only the original question and genuine final conversation result use the existing
conversation retention policy; source metadata is not copied into history.

The inspection surface uses original native/typed input and mandatory separate
native one-send review. Existing voice Settings navigation remains navigation
only, not inspection, source consent or a completed answer.
