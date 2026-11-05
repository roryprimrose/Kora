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
permitted value IDs, full confirmed source, original question, local destination
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
registered `id`; only the sixteen reviewed observations below can return values.
Other registered values are explicitly unavailable, not guessed or loaded from
another source ([closed discovery contract](../src/Kora.Core/Tools/SettingsDiscoveryTools.cs)).

Each list page has at most six records. The existing serial protocol keeps its
six-call, 15-second, 32-KiB cumulative input and 4-KiB cumulative output ceilings;
an oversized result or continuation is rejected whole, never truncated.
Only the original question and genuine final answer are retained; discovery
results are volatile lower-trust data, not approval or future read authority
([bounded loop](../src/Kora.Application/Dependencies/BoundedModelTurnAdapter.cs),
[exact reviewed binding](../src/Kora.Application/Dependencies/ModelTurnHost.SettingsDiscovery.cs)).

Exactly sixteen registered observations are included: theme, reading text scale,
ordinary spoken-summary word/sentence caps, Kora-only playback volume,
device-default response mode, `appearance.presence-display`, the compound
`speech.voice` preference and the eight presence preferences below. Presence display
is a reviewed Boolean (`true` or `false`), not a claim that the presence renderer
is currently available or visible. Its saved/default provenance and source
invalidation follow the same rules as the other observations
([closed source](../src/Kora.Core/Configuration/SettingInspection/SettingInspectionReview.cs)).

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

For example: "Why does Kora disappear so quickly, and what presence size and
movement settings am I using?" Both modes review the full source before a
separate one-send confirmation; neither changes these preferences.
Reduced motion `false` means follow Windows, not force motion; `true` freezes
presence particles. These are confirmed saved/default intentions, not proof of
current visibility, rendered size, animation, Windows motion state or performance.
The appearance values must all match the live service and saved/default provenance
at its observed revision. A change during review, tool work or publication retires
the original source and requires fresh inspection
([confirmed appearance reader](../src/Kora.Application/Configuration/SettingInspection/AppearanceConfigurationService.SettingInspection.cs)).

For example, ask **Which installed speech provider and voice are configured?**
The complete review includes the exact configured provider ID and voice ID,
saved/default provenance and future-only semantics. `settings.get` returns a
typed `selection` containing `providerId` and an optional `voiceId`, not a scalar
or arbitrary preference text. An absent voice ID means that provider's advertised
default; the exact default voice is **not resolved** by this read. Confirmed absent
storage means the domain default `windows-sapi` with that default-voice policy,
not a fabricated installed voice ID.

The existing atomic selection reader also preserves its legacy-file precedence.
The selection must match the service's already-observed selection and provenance.
No provider/voice catalogue is enumerated or disclosed. A saved choice can refer
to unavailable assets; this read does not establish installation, availability,
effective playback, profile qualification or permission to speak. A selection
change or observed source failure retires an earlier review, including an
observed change-and-return.

Response timeout and Windows speech rate remain value-unavailable
([closed source](../src/Kora.Core/Configuration/SettingInspection/SettingInspectionReview.cs)).
No account, device, permission, privacy, credential, session, path or arbitrary
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
This ownership is established before native busy-state notifications or source
callbacks run. A Cancel or Close triggered during startup still waits for that
exact inspection or confirmed-answer operation; it cannot report completion by
joining an earlier request.

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
