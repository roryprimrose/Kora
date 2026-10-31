# User Configuration and Verbal Settings

Status: partial. Bounded appearance, installed speech choices, assistant display/PTT
prefix, spoken-summary caps, exact input/output preferences, per-Kora playback
volume, Windows-provider-native rate, device-default response mode, independent in-call feedback and future-only SQLite diagnostic/audit retention are delivered through shared native/
exact typed and activated-voice workflows below. Broader scopes, setup and the
complete verbal preference/model-facing contract remain proposed; native future-only
session archive/deletion preferences are also delivered, subject to protected-call origin gates
and mandatory secure workflows.

Related: [OOTB Phrases](OOTB_Phrases.md), [Environment Setup](Environment_Setup.md), [Call-Aware Speech](Call_Aware_Speech.md), [Security](Security_Data_Flows.md), [Interaction and Sessions](Interaction_And_Sessions.md).

The [product-admission decision](MVP_Scope.md#product-admission-and-realignment) prioritizes model reasoning over registered settings through the same typed validation/proposal/approval path as native and exact commands.
Do not substitute ever more static aliases for completing that shared operation,
or restore retired public diagnostic-queue settings. The
[roadmap](Implementation_Roadmap.md#current-delivery) owns delivery status;
the contracts below distinguish implemented controls from proposed scopes.

## Fixed Reviewed Session Memory Management Boundary

The [reviewed selected-subset memory workflow](Model_Providers_Memory_And_Knowledge.md#reviewed-selected-subset-session-memory-forget---2026-10-19)
is an explicit original-user action, not a saved retention setting, model permission or automatic cleanup policy.
In **Sessions > Session memories**, explicitly list, choose **Disable** or **Forget**, select only visible exact rows,
review every complete body/classification/ID/revision and the total count, then separately confirm the displayed operation.
There is no default Select All. Only admitted Enabled records qualify for Disable; Forget also accepts admitted Disabled records.
Pending/unreviewed/rejected, already Forgotten, unknown, duplicate, foreign or stale rows do not qualify.

Disable closes use while retaining content. **Forget irreversibly removes application content** and the review receipt,
retaining a content-free non-reusable identity tombstone; it is not physical/forensic erasure of database pages, backups or copies.
The entire subset is preflighted before any effect, but each action commits separately with its existing audit.
This is **not atomic-all**. At the first conflict, cancellation or uncertainty the sequence stops, earlier commits remain committed,
and later rows are **NotAttempted**. **Unavailable** can mean unknown commit; inspect durable state manually before a fresh review.
Neither rollback nor automatic retry is promised.

Changing operation, selection, filters, source/list, edits/classification, private session/control state or closing/cancelling
retires the review and its private bodies. Cancel before confirmation makes no change; in-flight cancellation cannot undo earlier commits.
Forget results are content-free even on failed/malformed readback, and private-source loss suppresses stale bodies/results.
Single-record controls remain available under their existing policy. No batch acceptance/admission, scope promotion, recall,
provider/network action, session-retention exemption, automatic deletion or broader model behavior is enabled.
Deterministic implementation checks do not qualify providers or broaden this fixed operation.

## Fixed Session Attachment Boundary

The [native single-file Session attachment](File_And_Folder_Ingestion.md#delivered-durable-single-file-session-attachment---2026-10-10) has fixed host policy, not a configurable knowledge or model setting: one immutable UTF-8 `.txt`/`.md`/`.markdown` file per exact session, 256 KiB original bytes including BOM, sixteen retained profile files and no eviction.

Persistence requires separate native metadata/durable-copy review and original-user confirmation. Existing session retention/Keep rules govern the snapshot; passive inspection/search neither renew activity nor run cleanup. Removal has its own exact copy inventory/revision review and confirmation. These controls never save bodies in preferences, change provider mode or grant model use.

No format/limit override, folder attachment, managed source, extra version, model-context inclusion, OCR/vector, refresh/watch, egress or generic reset/export setting is added. Missing/corrupt saved source state fails closed; see the canonical storage/removal contract.

## Configuration Contract

### Conversational voice replies

**Settings > Speech & audio > Conversational voice replies** provides explicit **Enable conversational replies** and **Turn off** actions ([native controls](../src/Kora/SettingsWindow.axaml)).
The shared device-local preference defaults Off and uses current original-user/native/call admission, typed requested/terminal audit, atomic preference replacement, exact readback and an unconfirmed-write hold ([configuration owner](../src/Kora.Application/Configuration/ConversationalReplyConfigurationService.cs)).

This choice permits a new exact foreground host clarification to finish speaking before one bounded schema reply. It never enables listening by itself, replaces microphone consent or approves an operation ([conversational turn contract](Interaction_And_Sessions.md#opt-in-exact-playback-conversational-replies)).
Manual voice/native answers remain available under that contract. This preference is not a new registered model-proposal setting or an alias for session dictation ([shared setting owner](../src/Kora.Application/Configuration/ConversationalReplyConfigurationService.cs)).

### Exact existing Settings-section navigation

Original typed/current-name activated voice uses the [closed seven-phrase grammar](../src/Kora.Application/Configuration/SettingsSectionCommand.cs) to request only Appearance, Speech & audio (speech/audio/microphone aliases), Responses, Sessions or Calls.
No fuzzy, substring, model or external-content selector is admitted; malformed reserved requests clarify before question/approval/model fallthrough ([dispatcher](../src/Kora.Application/ViewModels/MainViewModel.cs)).

The consumer-focused [typed presenter seam](../src/Kora.Application/Presentation/ISettingsSectionNavigation.cs) binds original origin, request continuation, host/privacy/ownership revision, voice generation and native lifetime through UI dispatch.
Missing/disposed/rebound or revoked presentation fails explicitly in the transcript, preserving pending interactions ([Application workflow](../src/Kora.Application/ViewModels/MainViewModel.SettingsSectionNavigation.cs)).

The [desktop controller](../src/Kora/SettingsWindowController.cs) reuses its singleton Show/Activate path and selects [stable native tab anchors](../src/Kora/SettingsWindow.axaml.cs). Normal actual visibility observation alone binds setting controls.
Navigation does not save preferences, choose endpoints, grant consent/permission, refresh providers/network, start capture, activate manual calls or restore retired content ([native tests](../tests/Kora.Windows.IntegrationTests/SettingsSectionNavigationTests.cs), [portable tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.SettingsSectionNavigation.cs)).

This is the bounded I implementation subset: existing singleton/tab/private-native navigation and deterministic fakes. It enables no audio/provider profile and claims no E or RC-Q, installed/hardware/acoustic/accessibility qualification ([delivery policy](Implementation_Roadmap.md#delivery-and-qualification-dependency-policy)).

### Delivered bounded fixed local-version queue settings (R10/R13)

The native and exact fixed-read queue preference editors are removed. Former setting phrases report retirement rather than mutating preferences or falling through to model/approval routing ([workspace contract tests](../tests/Kora.Windows.IntegrationTests/WorkspaceRealignmentContractTests.cs)).

Existing values, validation and unconfirmed-write holds remain in the internal [configuration service](../src/Kora.Application/Configuration/SessionQueueConfigurationService.cs). Retirement never defaults corrupt state, changes captured deadlines or grants dispatch. General work settings need their own approved consumers.

### R18 bounded local event suppression

The [run-only routine quiet increment](Proactive_Interaction.md#delivered-run-only-routine-notice-quiet-mode---2026-10-10) adds native Sessions On/Off/Reset/status, **Work and Maintenance combined only**. It starts Off and lasts until explicit clear/reset or restart, with no saved quiet choice.
First delivery is suppressed before budget consumption; routine rows disappear while On, with truthful omitted/suppressed counts. Failures, Attention, mandatory output, questions/approvals, authoritative work and private recovery are unchanged.

Exact live original native session/control/choice/lifetime admission and requested/terminal typed audit precede activation. Clear permits future new notices, not old deferred/muted backlog.
Canonical schema 1 remains readable without a write; known schema 2 adds only routine suppression history in the same files/bounds, never the quiet flag. Corrupt/unknown/unconfirmed state holds with explicit recovery; no fatigue, expiry, clock or retention policy is reset.

The [local visual broker](Proactive_Interaction.md#delivered-r18-trusted-local-visual-broker---2026-10-09)
adds no speech/network consent or durable quiet preference editor. Fixed category budgets
and fifteen-minute exact defer are host policy, not a reminder scheduler.
Schema-1 `local-events.json` and `local-events-unconfirmed.txt` use the shared
atomic device-local preference store. They retain only 64 bounded content-free
suppression receipts, four fatigue budgets and a UTC high watermark; exact
source expiry is never extended. Unknown/obsolete/corrupt/unconfirmed/future
state holds without defaults or replay. Restoring ownership or clearing a call
does not automatically show/speak notices; only fresh already-open native
observation may admit an unexpired deferral. Session retirement clears owned
receipts, not unrelated preferences or global fatigue budgets. General
reminders, quiet hours, proactive speech configuration and apply-now remain
undelivered.

### R12 Bounded Session Retention Delivered - 2026-10-09

Native **Settings > Sessions** exposes archive-after and delete-after whole-day
choices with explicit Refresh/Save/Reset. Unsaved/default/reset means **1 day
(24 hours) archive / 30 days deletion**, both from the same last-meaningful-
activity timestamp. Archive must be positive, deletion strictly greater than
archive and at most 365 days. Domain validation rejects invalid combinations;
saved malformed/noncanonical/schema/UTF-8 values throw `InvalidDataException`,
never an implicit default. These future-only preferences add no typed, voice or model mutation route.

Schema-1 **`session-retention.txt`** and
**`session-retention-unconfirmed.txt`** use `IApplicationDataPaths` and the shared
atomic store. Original local-user intent, owning unlocked native lifetime and
unchanged host/call revision admission, REQUESTED/terminal trusted audit,
readback and durable completed control receipt precede confirmation/activation.
Unknown/unconfirmed/inaccessible state holds policy and new clock writes.
Inspect saved state and receipts, explicitly repair, then refresh; ordinary
refresh cannot clear an unconfirmed marker or claim rollback.

Changes affect only new sessions or subsequent meaningful activity. Existing
sessions retain their snapshotted archive/deletion intervals and due dates;
shortening or resetting never silently applies an immediate purge on a later
tick. Status discloses future-only behavior; [single-conversation apply-now](#explicit-single-conversation-apply-now) is a separate native decision. Ordinary
diagnostic/audit retention, independent Perpetual grants and other preference
files are unchanged. Audited Perpetual session marking is a separate
exemption, now exposed by the [exact native per-session controls](Interaction_And_Sessions.md#r12-native-exact-session-retention-controls---2026-10-10), not by these future-only preferences.
Native status shows recorded UTC inactivity/due dates. Keep/ordinary requires a separate exact preview/confirmation; ordinary warns about unchanged soon/already-due dates. The exemption is session retention only, not an operation grant, consent, memory/recall or execution. It does not apply the current policy to existing clocks.

#### Read-only hypothetical session retention planner

The native Session Retention card adds separate proposals and **Preview hypothetical session retention only**. Refresh the confirmed future policy first. Selecting a proposal does not save/reset preferences or scan; preview leaves recorded deadlines and cleanup eligibility unchanged ([native integration](../src/Kora.Application/ViewModels/MainViewModel.SessionRetentionPlanner.cs)).

At one fixed UTC cutoff, Complete reports hypothetical newly archive/delete-due counts, already-due recorded deadlines and mutually exclusive removed/kept/work-held/unknown exclusions. Hypothetical deletion takes precedence over archival; archived sessions do not rearchive. Inclusive cutoff and original deadlines are separate ([aggregation](../src/Kora.Core/Storage/SessionRetentionPlanner.cs)).

The reader checks dates, flags, lifecycle, typed changes and authority head. Each scanned candidate is validated before exclusion, including the budget-crossing lookahead.
No session bodies, names, attachment paths or question payloads are projected; retained questions/unavailable tasks are unknown exclusions ([reader](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.RetentionPlanner.cs)).
Preview failure diagnostics retain only the exception type, never exception messages, private paths or source contents ([typed diagnostic](../src/Kora.Application/Diagnostics/ApplicationLog.Logging.cs)).

The scan shares 4,096 admissions and 4 MiB charges across authority, work metadata, clocks and orphan checks.
Entries charge envelope size without returning it; task/session metadata charge 192 bytes, questions 96, ledger rows 40 and clock keys 36. A further validated candidate exhausts a limit, never truncated Complete ([accounting](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.RetentionPlanner.cs)).

Ten-second scan timeout retires the request; receipts expire after thirty seconds. Incomplete/Unavailable have no aggregate, never false zero.
Proposal/control/private/owner/call, preference/policy revisions, source snapshot and expiry fence results. Edits, refresh/save/reset, changes, privacy loss, close or failure retire without replay ([orchestration](../src/Kora.Application/Configuration/SessionRetentionPlannerService.cs)).

The planner has no apply-now, policy write, session mutation/removal/hold, revocation, schema/index change or scanner. Future-only preferences and maintenance remain separate. Existing policy/private authority is consumed, not broader runtime or E/Q qualification ([contract](../src/Kora.Core/Storage/ISessionRetentionPlannerReader.cs)).

#### Explicit single-conversation apply-now

The separate native **Sessions > Selected-session retention** operation previews the confirmed current policy for one existing ordinary conversation. Exact-ID retention access also works when passive content browsing is held. The original meaningful-activity clock is unchanged; old/new UTC archive and deletion deadlines, a fixed cutoff, expiry, inventory and immediate action are disclosed before a separate confirmation ([native workflow](../src/Kora/SessionsViewModel.RetentionApply.cs)).

Confirmation durably changes only that conversation's snapshotted intervals. If due at the reviewed cutoff, it archives to Done or performs the existing guarded exact disposition, with deletion taking precedence. A cutoff crossing into a different action requires fresh review; there is no deferred unreviewed purge, retry renewal, bulk action or new scheduler ([domain rules](../src/Kora.Core/Storage/SessionRetentionApplication.cs), [transaction](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.RetentionApply.cs)).

Perpetual/removed/purged conversations, current-run controls, queued/live/Unknown/unavailable work, unresolved questions, unconfirmed physical release, unsent/held native input and uncertain copy inventory are excluded. Policy/preference, original native source, owner/privacy/call/control revision, session generation, inventory, authority head, storage snapshot, cutoff and expiry changes deny confirmation. A later click cannot promote an original voice-origin retention handoff; start a fresh native metadata review ([admission](../src/Kora.Application/Hosting/SessionRetentionApplyService.cs), [resource guard](../src/Kora.Application/Hosting/SessionConversationService.RetentionApply.cs)).

Requested/terminal authority audit, policy/effect and terminal task receipt share the existing transaction. Exact readback precedes completion; deletion also consumes the existing owned SQLite/free-page/journal cleanup contract. Precommit cancellation changes no policy or lifecycle; failed/unknown commit or postcommit copy/readback failure requires durable inspection, never a rollback or replay claim. Independent Perpetual grants, audit, diagnostics, device memories, original files and external/provider copies remain outside this action ([existing disposition](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.Disposition.cs)).

This is bounded implementation, not broader runtime enablement or E/Q qualification. Future-only Save/Reset and the hypothetical planner retain their no-apply behavior ([delivery policy](Implementation_Roadmap.md#delivery-and-qualification-dependency-policy)).

Live/dispatched/Unknown work, unresolved questions, current-run control
authorities and uncertain copy inventories hold maintenance. See the
[storage/removal contract](Interaction_And_Sessions.md#r12-bounded-session-retention-delivered---2026-10-09).
Schema v6 retains the separately delivered fixed local-version queue and its
no-replay recovery. Full R12 still needs blocked R11 for general execution
integration; this preference slice adds no execution or broader scheduling.

Validation after rebase onto `5755aa7d` (#118): zero-warning/error root Release
build with existing locked dependency assets (no restore/feed change needed);
Core 1,028, Application 2,860, Tools 69, Definitions 6,
Windows 1,215 passed with zero
failures/skips; portable line/branch coverage 100%/100%. See the
[dated evidence receipt](Archive/Implementation_Roadmap_2026-10-11.md#r12-bounded-session-retention-delivered---2026-10-09).

### Delivered bounded device-local in-call feedback (R10/R15)

Schema 1 admits **`calls.feedback-mode`**, with **Voice / UI / Both / Inherit**
and unsaved/default **UI**. Native **Settings > Calls** provides an exact
host-held draft and explicit Refresh/Save/Reset. Exact typed/current-name
ACTIVATED `list call feedback settings`, get/status/set/reset use the same
[configuration service](../src/Kora.Application/Configuration/InCallFeedbackConfigurationService.cs),
common original-input audio-control session/generation and committed-intent
admission. Input/result bounds remain 1,024 UTF-8 bytes/64 KiB. This is not a
general model settings tool or natural-language mutation.

Only manual Active or enabled Active/Suspected evidence applies the feedback
choice ahead of task > queue > session > device output. Voice maps to VoiceOnly,
UI to VisualOnly and Both to Hybrid; Inherit restores ordinary precedence.
The existing presentation exposes task/queue/device, not new durable narrower
scope controls; the shared resolver retains the session precedence seam.
Unknown/invalid evidence never applies a feedback override or grants speech:
mandatory speech suppression and complete visual recovery remain even with a
legacy relaxed call-suppression preference. Clear/Unavailable use ordinary
output, with truthful unavailable automatic detection. No detector is added.

Feedback is **selection, not permission**. Voice/Both/Inherit cannot bypass
independent call speech suppression, ownership/unlocked privacy, mute/zero,
capture exclusion, native synthesis/playback lifetime, summary bounds or
mandatory complete visual safety/question/approval/interruption recovery.
Changing feedback never opens capture, changes input eligibility/consent,
acquires permission, edits/reuses grants, clears a call, synthesizes or replays.
Protected Active/Suspected/Unknown original-voice set/reset is denied without
UI relabelling, downgrade proposal or deferred application; inspection is allowed.

Host-held choices bind exact configuration/call revision, original input,
current name/input generation, host-resolved session/generation and owning
unlocked privacy/topology. Native visible-lifetime identity expires on
hide/reopen/dispose; old choices cannot revive. REQUESTED and terminal trusted
audit, atomic save/reset, exact readback and committed-intent outcome precede
confirmation/activation. Changes retire active/queued output generations.

The independent version-1 **`in-call-feedback.txt`** and
**`in-call-feedback-unconfirmed.txt`** use `IApplicationDataPaths` and the shared
atomic store. Missing confirmed storage means unsaved UI; Reset deletes only
this override. Invalid UTF-8, schema/enum, inaccessible state or an unconfirmed
marker refuses instead of defaulting, including across restart. A file can be
committed before later evidence fails: status remains unavailable, not a
fabricated rollback or success. Inspect saved state and required audit/intent
receipts, explicitly repair, then refresh under fresh admission. Ordinary
refresh cannot clear the marker. Unavailable state withholds speech and keeps
full visual recovery without changing microphone/consent state.

This deterministic source/native-seam slice does not close full R10/R15,
automatic detector/source freshness qualification, native accessibility,
real-call/acoustic leakage/stop timing, speak-once/downgrade review, proactive
configuration, general tools or A0-A4 acceptance. See the
[experiment disposition](Archive/Implementation_Roadmap_2026-10-11.md#bounded-in-call-feedback-experiment-disposition).

### Delivered bounded local speech text (R25)

Schema-1 **`display.speech-text`** supports exact `Off`, `CurrentUtterance` and `CurrentSentence`;
unsaved/default/reset is **Off**. Native **Inspect speech text**, explicit
host-held choice/save and reset share the same service as
`list speech text settings`, `get/status display.speech-text`,
`set display.speech-text to Off|CurrentUtterance|CurrentSentence`, and
`reset display.speech-text`. The current assistant-name prefix is accepted only
on the exact activated route; no fuzzy/model interpretation is advertised.

`CurrentSentence` roundtrips through the same audited atomic preferences,
readback, restart and native choice service. Saving it does not enable alignment,
synthesize or replay speech. It requests only exact source sentences reached by
matching actual playback. No Windows voice/rate/output profile is qualified in
this build, so sentence captions remain explicitly unavailable and the full
visual answer remains available. Missing, malformed, stale or unknown-provider
evidence never falls back to an utterance caption or estimated timing.
See the [versioned source and native alignment contract](Information_Display.md#optional-speech-text).

The [bounded natural caption grammar](Information_Display.md#bounded-natural-speech-caption-commands) now maps original typed/current-name activated whole utterances to these same commands. Show/on saves `CurrentUtterance`; hide/off saves `Off`, not one-shot visibility. `reset speech captions` resets mode only; placement/delay have independent reset phrases ([parser and syntax help](../src/Kora.Application/Configuration/SpeechTextCommand.cs)).

Discovery choices bind owner, original channel, durable admitted configuration
session/generation, preference revision and live ownership/privacy/call gates.
Requested and terminal typed audit outcomes, atomic preference write, exact
readback, durable unconfirmed marker and completed control receipt precede
activation. Corrupt or pending/restart values hold captions off and require
explicit repair, not a default or replay. Captions themselves remain ephemeral:
only actual matching response playback may reveal approved text.
Configuration never speaks, persists content, logs text, opens capture, changes
output mode/call policy or substitutes for required native recovery.
See [bounded delivery and separate proposals](Information_Display.md#delivered-bounded-local-utterance-slice-r25).

Caption preference failures explicitly report **not confirmed** in the transcript while original owning/private presentation admission remains current; captions stay unavailable pending explicit repair ([presentation](../src/Kora.Application/ViewModels/MainViewModel.SpeechText.cs)).

A required review or tracked approval preparation retains its panel. Failure presentation never waits for that preparation or replaces its review during durable audit ([deterministic audit-order regressions](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.CaptionFailureOrdering.cs)).

### Delivered bounded caption UX options (R25) - 2026-10-09

The same schema-1 native/exact typed/current-name ACTIVATED workflow now exposes:

| ID | Type, choices and units | Unsaved/default/reset | Timing and reset |
|---|---|---|---|
| `display.speech-text-placement` | Working-area corner: `BottomRight`, `BottomLeft`, `TopRight`, `TopLeft`; primary by default, or the explicit run-only display below | `BottomRight` | Next eligible playback; retires old caption immediately; reset changes only this option |
| `display.speech-text-dismissal-delay` | Canonical integer seconds, 0-30 inclusive | 5 seconds | Delay after normal completion only; reset preserves placement and mode |
| `display.speech-text-pin` | Boolean `true` / `false`, run-only current-caption state | `false` | Retains already-observed text until unpinned or source retirement; reset unpins, never persists |

`list speech text settings` includes placement/delay IDs, typed choices, bounds,
defaults, saved/effective values, source, revision, scope, timing, reset and
recovery. `get/status <id>`, `set <id> to <exact value>` and `reset <id>` work
for each option. Placement/delay use the existing host-held choice identity,
original channel, admitted session/generation, revision, call/ownership/privacy
gates, requested/terminal typed audit, atomic save/readback, completed intent
receipt and durable unconfirmed marker. Native **Inspect speech text**, option/
value selectors, **Save caption option only** and **Reset selected caption option**
use the same service. Neither option enables speech text or changes its mode.

The independent version-1 **`speech-caption-options.txt`** stores only the exact
placement/delay tuple through `IApplicationDataPaths` and the shared atomic store.
Existing **`speech-text-mode.txt`** bytes and Off default are unchanged; the
existing **`speech-text-mode-unconfirmed.txt`** marker covers all caption writes.
Unknown schema/corner, noncanonical or out-of-range delay, malformed UTF-8,
unreadable storage or pending evidence throws/refuses, holds captions unavailable
and requires explicit repair. No corrupt companion is guessed by reset.

The caption's native **Pin / Unpin** control and exact pin commands affect only
one already-observed response/playback identity. Pin state is passive ephemeral
presentation, not a saved preference or effect authority; no new durable intent,
audit or model tool is advertised for it. Pin get/status is visual-only;
mutation retains original-channel protected-call and current host/private-source
checks. No command speaks, replays, opens capture or replaces required panels.
Normal successful completion labels retained text **PREVIOUS SPEECH**. Unpin
uses the original completion deadline, so overdue text clears immediately.
Stop/cancel/failure, response replacement, preference revision, call, lock,
ownership/privacy/input recovery and disposal still retire immediately,
even when pinned. Unobserved/queued text cannot be pinned or retained.

Placement uses the chosen screen's current working area and 24-DIP margin;
there is no persisted native handle, arbitrary coordinate or display identity.
Missing working area or unconfirmed placement fails presentation explicitly.
Sentence alignment remains unavailable: the composed provider reports only
utterance segment 0, not admitted sentence boundaries. No heuristic splitting,
timing claim or new audio pipeline is introduced. Commands outside the
[bounded natural grammar](Information_Display.md#bounded-natural-speech-caption-commands) and native/accessibility/acoustic qualification remain open.
Existing 3-sentence/80-word speech caps remain unchanged; brief spoken offers for
over-limit detailed results are separate work, not delivered by captions.

### Delivered run-only native caption display choice (R25) - 2026-10-10

**Settings > Speech & audio** and the caption itself expose **Current caption display**, **Use selected display**, and **Return to primary**.
The untouched default is the current primary display; explicitly choosing even the primary binds that actual source for this run, rather than following a later primary designation. Restart forgets selection. There is no new preference ID, model tool or voice route.

Choices are immutable host-issued objects bound to the live desktop display snapshot, actual source lifetime and selection revision. Primary/ordinal/working-area geometry labels are presentation only, never selection identity.
Closing the native selection surface, selecting/resetting, or topology/working-area/DPI changes expire old choices. Unknown, missing or ambiguous targets are unavailable, never invented.

The controller revalidates the actual chosen source, sizes/clips the native surface within its current working area and scaling, and applies the saved corner with a 24-DIP inset (reduced only as necessary on tiny areas). Native resize/reposition hides first to avoid transient spill.
Negative coordinates are valid. No arbitrary coordinates, monitor handles/device IDs or selection data are persisted.

Selection affects only already-observed eligible text or the next eligible caption while captions are Off. It cannot enable captions, play/replay/synthesize, capture audio, extend the original dismissal deadline or pin, reveal unobserved text, or replace required panels.
A removed/unknown explicit target immediately hides and retires even pinned text; it never falls back to another monitor. Explicitly reselect or **Return to primary** after current owning/unlocked/private/call gates permit it. New captions also retire while target recovery remains pending.

Display recovery does not restore retired text or clear an unconfirmed preference, privacy hold, stale request/generation or ownership failure. Existing lock/call/input/response/stop/failure/disposal retirement remains authoritative.
No content storage, logging, clipboard expansion, stable admission identity or task/grant authority is added. Headless/fake display observations verify the binding and geometry, not installed multimonitor, accessibility or acoustic acceptance.

### Delivered bounded Windows-provider-native speech rate (R10)

The independent schema-1 option **`speech.windows-rate`** admits one canonical
integer **-10 through 10 inclusive**, engine-normal/unsaved default/reset **0**.
These are the Windows `System.Speech.Synthesis.SpeechSynthesizer.Rate` API's
[documented native units](https://learn.microsoft.com/en-us/dotnet/api/system.speech.synthesis.speechsynthesizer.rate?view=net-10.0-pp),
not a percentage, multiplier or universal words-per-minute scale. The
[single domain value](../src/Kora.Core/Configuration/WindowsSpeechRate.cs)
rejects padding, leading zeros, plus signs, negative zero, fractions and suffixes.
There is no common speed mapping or Kokoro speed implementation.

Native **Settings > Speech & audio** exposes an unsaved bounded draft and
explicit Refresh/Save/Reset. Exact typed/current-name **ACTIVATED**
`list rate settings`, `get/status speech.windows-rate`,
`set speech.windows-rate to <canonical integer>` and `reset speech.windows-rate`
use the same [rate workflow](../src/Kora.Application/Configuration/WindowsSpeechRateConfigurationService.cs).
Exact typed/current-name ACTIVATED `speak faster` / `speak slower` use a [domain-owned 1-native-unit step](../src/Kora.Core/Configuration/WindowsSpeechRate.cs#L21) and [revision-bound proposal](../src/Kora.Application/Configuration/WindowsSpeechRateConfigurationService.cs#L118), then the existing audited apply/readback/hold path.
Kokoro remains unsupported; no model inference or provider switch is involved ([rate control](../src/Kora.Application/ViewModels/MainViewModel.WindowsSpeechRate.cs#L99)).
The [result orchestration](../src/Kora.Application/ViewModels/MainViewModel.WindowsSpeechRate.cs#L99) explicitly reports reached and unchanged -10/10 boundaries.
An unchanged boundary creates no preference write or successful-change audit. Failed/stale admission, provider or persistence is not reinterpreted as a default or retried against newer state ([proposal](../src/Kora.Application/Configuration/WindowsSpeechRateConfigurationService.cs#L118)).
Input/control bounds remain 1,024 UTF-8 bytes and complete results 64 KiB.
Discovery/status explicitly includes provider, advertised support, desired/
effective native rate, saved/default/unavailable source, bounds/default,
configuration/provider/call revisions, application timing, reset and recovery.
Only an available installed Windows provider advertising **WindowsNative**
and a qualified owned adapter can mutate. Kokoro is explicitly unsupported:
its synthesis and settings remain unchanged, and a saved Windows rate is not
reported as a Kokoro effect. Unknown provider/capability state fails closed.

The shared atomic preference store and `IApplicationDataPaths` own the
version-1 `speech-windows-rate.txt` file and independent
`speech-windows-rate-unconfirmed.txt` marker. Missing confirmed storage means
normal 0; invalid UTF-8/format/rate, unreadable or unconfirmed evidence does not
activate a default. Source-generated diagnostics and native visual recovery
surface failures. Inspect saved state and required audit/intent receipts before
explicit repair and fresh refresh; ordinary refresh never clears a pending marker.
Reset removes only this Windows rate override.

Genuine common audio-control admission resolves original user input and the
host-owned active session/generation, using the existing consolidated interaction
lease exactly once. Host-held proposals bind source and provider revisions;
the current saved selection and live installed catalogue are revalidated without
accepting caller records as authority. Current name/input generation, protected/
Unknown call policy, ownership/unlocked privacy/topology and the native surface's
expiring visible-lifetime identity remain gates. Hide/reopen/dispose cannot revive
a prior native callback; a voice request cannot become UI-originated authority.
REQUESTED and terminal trusted audits, exact atomic readback and committed-intent
receipt precede confirmation/activation. Late failure retains the durable marker,
so an unconfirmed mutation cannot activate on restart.

Changed rate retires active and queued output generations through the existing
owned cancellation/retirement path. The serialized synthesis lifetime prevents a
new synthesis from overtaking unfinished retirement; stale completions cannot
start playback or claim success. Only future eligible Windows synthesis sets
`Rate` on Kora's own synthesizer before starting synthesis. Set/reset/refresh
does not claim acoustic speed or audibility. Native setter failure explicitly
holds rate output with full visual recovery, without clearing an independent
voice/output preference. Set/reset/refresh
never synthesizes, autoplays, replays, acquires capture, grants consent, changes
provider/voice/output selection, installs/downloads assets or changes global
SAPI/mixer/default-device settings. Existing call/output/zero-volume gates and
complete mandatory visual previews, interrupted responses and recovery remain.

All session/task/question/approval/grant/retention metadata and policies are
unchanged: ordinary SQLite 1–365/default30, audit 30–365/default90 (including
prior-policy configuration receipts), daily files 30/30 and cleanup scheduling.
This is deterministic source/native-seam qualification only, not acoustic,
installed/native accessibility, full R10/A0–A4 or runtime/release acceptance.
The unique R02 speech experiment remains maintained historical evidence; this
rate implementation does not replace wake/model/license/acoustic receipts.

### Delivered bounded future-only audit retention (R10/R04)

The independent schema-1 option `logging.audit-retention-days` accepts one
canonical integer **30–365**, unsaved default/reset **90**. Native
**Settings > Logging** Refresh/Save/Reset and exact typed/current-name ACTIVATED
get/status/set/reset delegate to the same
[audit configuration service](../src/Kora.Application/Configuration/AuditRetentionConfigurationService.cs).
`list logging settings` returns both admitted retention descriptors, including
saved/default/effective/unavailable provenance, bounds, revision, original-input
confirmation, application timing, exclusions and explicit recovery. Complete
input/output bounds remain 1,024 UTF-8 bytes/64 KiB. Apply-now, immediate
deletion, natural-language aliases and model settings tools are unavailable.

The [single audit-days domain](../src/Kora.Core/Configuration/AuditRetentionDays.cs)
owns exact parsing, 30–365 validation and deadline metadata validation.
`IApplicationDataPaths` and the shared atomic preference store own
`audit-retention.txt` (version 1 plus canonical days) and the independent
`audit-retention-unconfirmed.txt` marker. Missing confirmed storage is default
90; malformed UTF-8, invalid days, unknown formats and pending confirmation are
`InvalidDataException`, not default activation.

Original local input uses a separate audit-control session/generation and the
merged common durable-intent admission, never diagnostic/audio/manual-call
authority. Host-held proposals, exact saved-source and effective-policy state,
configuration/call/name/input revisions, ownership/unlocked privacy/topology
and native visible-lifetime identity are revalidated. Protected-call original
voice mutation is denied without UI relabelling. Pending questions, approvals
and complete responses are not answered, retargeted, replaced or replayed.

The REQUESTED preference receipt is committed under the **prior** audit policy.
Atomic save/reset, exact readback, required terminal audit and committed-intent
outcome precede marker confirmation and activation. The terminal preference
receipt also uses the prior policy: it confirms the save, not a fabricated
already-active policy effect. Only later commits use the new days. Lost terminal
evidence, late cancellation/eligibility or confirmation failure holds the policy
and durable marker; restart cannot activate the unconfirmed weaker value.
Explicit recovery requires saved-state and required-receipt inspection before
manual repair and fresh admission. Startup refuses unconfirmed audit policy
before recovery/authority writes, rather than silently using 90. An in-run
failure is shown in native Settings; new required audit/authority commits are
held while passive admitted inspection of preserved rows remains independent.

One confirmed current-run snapshot reaches both genuine NEW
`InteractionStorageV1` required authority audit envelopes and independently
qualified `EvidenceStorageV1` diagnostic audit projections. These are distinct
sources: projections, daily files, arbitrary `SecurityAudit=true` properties,
messages, trace/model/caller identifiers never become permission authority.
Existing serialized audit bytes, IDs, hashes, revisions, relationships,
references/citations and deadlines are untouched. Cold readers, schema-1/2
legacy validation/migration and schema-3 authority readers validate each row's
original integral audit-domain deadline, not the current setting.

**No audit pruning or cleanup acceptance is delivered.** Set/reset triggers no
cleanup, rewrites no old deadline and changes no janitor schedule. Ordinary
SQLite days remain independently configurable **1–365/default-reset 30**;
daily files stay **30 days/30 files**. No session/chat/history
retention/delete/archive, task/question/approval pruning or grant changes are
implemented. Every grant record and its existing validity/scopes survives;
independent Perpetual records have no expiry/retention/eviction. Full
R04/R10/A0–A4, installed/native, acoustic, encryption, forensic/tamperproof,
artifact/backup disposal, runtime and release qualification remain open.

#### Read-only audit shortening impact preview

Native **Settings > Logging** separately offers an initially unselected proposed
audit-days choice and **Preview audit shortening impact only**. The user must
explicitly choose canonical 30–365 days below the confirmed effective saved/default
preference. This is observation, not Save, Reset, confirmation, approval or
apply-now. Existing future-only Set/Reset semantics remain unchanged.

At one fixed UTC cutoff, **newly due** means `proposed due <= cutoff < original
due`, with proposed due equal to original committed UTC plus the exact selected
days. Already-due rows are not newly due. Required-authority audit and independently
qualified diagnostic-audit projections report separate source/status, affected
count, oldest/newest affected committed UTC and resulting due-time range.
Complete zero has no range. No event text, properties, identities, titles,
paths or combined authority count is exposed. Diagnostic lookalikes never gain
typed audit status or authority.

The [portable aggregate policy](../src/Kora.Core/Diagnostics/AuditRetentionImpact.cs)
owns bounds and timing validation. Each source scans at most 4,096 rows and
4 MiB encoded envelopes, plus one bounded sentinel; hitting either bound yields
Incomplete with **no partial count**. Unknown, corrupt, missing, unsupported or
inaccessible sources yield Unavailable, never zero. Sources open only existing
private SQLite stores in query-only mode, with exact schema/integrity checks,
typed envelope/column/deadline checks and ordered audit-prefix/head validation.
Existing SQLite progress and lease deadlines remain; the entire preview also
has a ten-second deadline.

The host-held result binds the exact proposal, fixed cutoff, saved preference/
readback/effective-policy revision, opaque private store identity/revision snapshot,
native visible lifetime, owner/unlocked privacy/desktop/call and request/control
revision. Edits, Refresh, preference/store changes, privacy/lock/owner/call changes,
cancellation, failure, close and a non-renewable thirty-second lifetime retire it;
late read or timer callbacks cannot publish or retire a newer preview. Store
notifications only retire results: they never trigger an automatic scan.

This path creates no durable intent or audit-control session, renews no session
activity, writes no preference, audit receipt, record, deadline or cleanup schedule,
and adds no model/tool/voice/network route or periodic scan. Apply-now, deletion,
pruning, continuation anchors and forensic/recoverable-copy disposal remain
unavailable. This consumes the existing storage/configuration I subset; installed
accessibility and broader R04/R10 E/Q qualification are not claimed.

### Delivered bounded future-only SQLite diagnostic retention (R10/R04)

The diagnostic schema-1 option is `logging.sqlite-diagnostic-retention-days`: canonical
integer **1–365**, unsaved default/reset **30**. Native **Settings > Logging**
Refresh/Save/Reset and exact typed/current-name ACTIVATED
`list logging settings` / get/status/set/reset share one
[configuration service](../src/Kora.Application/Configuration/DiagnosticRetentionConfigurationService.cs).
Discovery reports desired/effective days, saved/default/unavailable provenance,
bounds, reset/timing, configuration/call revisions, independent session admission,
explicit recovery and `applyNowAvailable=false`. No natural-language aliases,
model settings tools or arbitrary patch capability are added.

Original local input, current owner/unlocked privacy/topology/input/call state,
independent diagnostic-control session/generation and the exact host-held
revisioned proposal are revalidated. Native callbacks additionally require the
same live visible Settings surface. Pending exact questions/approvals stay
unchanged; protected-call voice requests cannot relabel themselves through UI,
headers, model text or trace identifiers.

The domain owns canonical bounds/schema/defaults; supplied application paths
and the shared atomic preference store own `sqlite-diagnostic-retention.txt`
and its separate `sqlite-diagnostic-retention-unconfirmed.txt` marker.
Requested/terminal trusted audit, durable atomic save, exact readback and the
committed-intent terminal receipt precede confirmation/activation. Failed or
unconfirmed writes remain unavailable across cold restart; malformed UTF-8,
unknown schemas and invalid saved days throw `InvalidDataException`. Recovery
requires explicit saved-state/evidence inspection and refresh, never a silent
default or automatic marker clearing.

The effective policy reaches genuine newly committed SQLite ordinary log/span/
owned-link transactions, with one coherent deadline for each span and all links.
Semantic evidence schema v2 validates integral 1–365-day committed deadlines.
Only exact valid legacy schema-v1 30-day rows migrate, preserving every row ID,
payload, correlation, reference and deadline. All admitted readers use the same
validation. Already committed records never adopt the new days, including
attempts to replace an old span ID: the current writer is insert-only and
rejects duplicate IDs rather than upserting retention metadata.

#### Read-only ordinary diagnostic shortening impact preview

Native **Settings > Logging**, in the ordinary SQLite diagnostic subsection, has
an initially unselected **proposed** 1-365-day selector and explicit **Preview
diagnostic shortening impact only**, **Cancel**, and **Clear** controls. The
proposal must be shorter than the current confirmed saved/default diagnostic
configuration. Selecting, editing, status reads and source notifications never
start a scan. The future-only Save/Reset controls and exact typed/activated-voice
settings behavior remain separate and unchanged; there is no new model, tool,
voice, approval or apply-now route.

The [portable diagnostic impact policy](../src/Kora.Core/Diagnostics/DiagnosticRetentionImpact.cs)
uses one fixed UTC cutoff for the whole observation. A row is **hypothetically
newly due** only when `committed UTC + proposed days <= cutoff < original due`.
It validates the original integral **1-365-day** diagnostic deadline, not an
audit-domain minimum, observed-event age, trace age or the current policy's
deadline. Already-due rows are excluded. Links count by their owning span's
validated committed/deadline pair, exact ordinal and typed envelope/column
relationship, never by their target span's age or existence. Missing, partial,
or orphan linkage cannot produce a Complete span/link result.

Ordinary logs, spans and owned links report **separate Complete / Incomplete /
Unavailable** qualifications. A Complete result contains only newly-due count,
oldest/newest affected committed UTC and earliest/latest proposed due UTC; a
Complete zero has no timing range. No content, message, property, path, identity
or private store stamp is displayed. Unqualified sources never display a
partial aggregate or false zero. Logs qualify independently from spans/links,
which share an ownership-validation budget and qualification.

The [production segmented adapter](../src/Kora.Windows/Storage/WindowsSegmentedEvidenceSink.DiagnosticImpact.cs)
uses the admitted retained inventory, including legacy and sealed/active segments,
under the existing store gate, inventory/seal checks and unchanged source stamp.
Each [private partition reader](../src/Kora.Windows/Storage/WindowsSqliteEvidenceSink.DiagnosticImpact.cs)
opens only an existing admitted private partition under its existing lease and
query-only SQLite transaction. It validates the exact current schema and
physical integrity and decodes/validates all visited rows, including irrelevant
ones, without populating the sink's writer validation cache. As required by the
actual ordinary janitor's admission, a separate bounded guard validates the
preserved typed security-audit projection's contiguous prefix/head, envelopes,
columns and original audit deadlines. **No security-audit row is counted,
returned, pruned or reclassified**. An ordinary log's `SecurityAudit=true`
property or lookalike message confers no audit authority and does not remove
that ordinary log from the ordinary janitor's impact.

There are three finite scan budgets across the entire retained inventory, never
renewed for each rotated partition: the preserved-audit guard, ordinary logs,
and spans plus their owned links. Each admits at most **4,096 rows / 4 MiB
encoded envelopes**, with one non-decoded overflow sentinel. A guard overflow
makes all sources Incomplete without counts; an unqualified protected prefix or
private/schema state makes them Unavailable. Other row/byte overflows suppress
that source group's entire aggregate. All work shares the fixed cutoff and a
**ten-second TimeProvider cancellation/deadline**, in addition to existing
SQLite progress and lease limits. Cancel/failure/timeout publishes no aggregate.

The [application observation service](../src/Kora.Application/Configuration/DiagnosticImpactPreviewService.cs)
binds the proposal/control generation, exact saved/default provenance and
readback, configuration observation reference, policy generation, request,
private store identity/change stamp and source notification generation, native
visible lifetime and live owning/unlocked privacy/desktop/call state. Edit,
new observation (even identical), configuration/policy ABA, source mutation,
Clear/Cancel, closure/disposal, failure and a non-renewable **30-second
monotonic lifetime** retire the result. Late reader/timer and reentrant
notification/currentness callbacks cannot publish a retired generation or
retire a newer one. Retirement never refreshes or replays a scan.

The observation trace is a content-free, versioned, unparented diagnostic
Activity, not a HostActivity or authority source, and cannot persist an evidence
span. This workflow creates no durable/control session or task, meaningful
session activity, preference/marker/schema/index/cache write, audit receipt,
record/deadline modification, policy activation or cleanup trigger. It does
not promise actual cleanup, complete traces, physical/forensic deletion or
disposal of retained copies, and does not change session retirement or grants.
This is the existing configuration/private-storage **I** subset only; broader
R04/R10, installed/accessibility, hardware/provider and release **E/Q** gates are
not implementation prerequisites or claimed qualification. Delivery requires
the exact published source's maintained full portable/serial Windows suites,
current analyzers, engineering contracts and exact portable line **and** branch
coverage, followed by protected hosted checks. These checks do not qualify any
broader runtime, provider, installed or physical capability.

**Apply-now/immediate deletion is unavailable.** Set/reset never runs pruning
or changes its startup schedule/triggers. Audit remains independently configured
(30–365/default-reset 90, as described above), daily files 30 days/30 files, and every session,
history, approval and grant remains unchanged. Independent Perpetual grants
have no time expiry/retention/eviction; other grants keep existing validity/scope
rules. Suggested future session/history defaults do not implement those options.
Unavailable ordinary policy explicitly reports source-qualified delivery gaps
through the independent file/recovery path; mandatory trusted audit and
authority stores remain admitted under their own rules, never ordinary fallback.
Full R04/R10/D-009, installed/native, acoustic and forensic acceptance remain open.

### Delivered R10 Durable Provider Mode Preference - 2026-10-09

Native **Settings > Providers** offers explicit Inspect/Save/Reset using host-held choices; exact typed/current-name activated input uses `list provider settings`, `get`/`status providers.default-mode`, `set providers.default-mode to LocalOnly|LocalFirst|HostedPreferred` and `reset providers.default-mode` ([native workflow](../src/Kora.Application/ViewModels/MainViewModel.ProviderModeConfiguration.cs), [parser](../src/Kora.Application/Configuration/ProviderModeCommand.cs)).

Only the device-local initial provider mode is delivered. Unsaved/default/reset means **LocalOnly**. Discovery reports schema, enum choices/default, device scope, revision, saved/default/unavailable provenance, desired mode, timing, reset effect and recovery; it does not claim an effective provider or available hosted adapter ([result contract](../src/Kora.Application/Configuration/ProviderModeCommandResult.cs)).

Schema-1 `provider-mode.txt` and `provider-mode-unconfirmed.txt` use `IApplicationDataPaths` and the shared atomic store. Malformed/unknown/noncanonical/invalid-UTF-8/oversized or unconfirmed state fails explicitly with `InvalidDataException`. Reset saves LocalOnly, not file deletion, following the response-mode convention; no unrelated preference is reset ([storage](../src/Kora.Application/Configuration/LocalModelProviderModePreferences.cs), [private-storage tests](../tests/Kora.Application.UnitTests/Configuration/LocalModelProviderModePreferencesTests.cs)).

Original user intent, active session/generation, unchanged host-held choice and call/preference revisions, trusted REQUESTED/terminal audit, exact atomic readback and durable terminal control receipt precede confirmation. Protected/Unknown original activated-voice writes are denied without relabelling. Failed/interrupted writes retain an unconfirmed marker across restart; inspect saved state and receipts, explicitly repair, then refresh ([service](../src/Kora.Application/Configuration/ProviderModeConfigurationService.cs)).

The confirmed device preference seeds the first policy-bound turn of a session only; later edits do not overwrite existing session policies or admitted turns. Existing Default/Local/Hosted turn semantics and exact handoff review remain authoritative. No account, adapter qualification, network/egress authority or actual hosted dispatch is added; HostedPreferred still fails closed ([host consumer](../src/Kora.Application/Dependencies/ModelTurnHost.Policy.cs), [fail-closed tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.ProviderPreference.cs)).

**Review pending exact provider handoff** is a separate original-user native review action, not a preference, setter or permission. It consumes existing audited host-issued offers and displays the complete bounded envelope, identities/revisions, destination, reason and evidence lineage/classification.

Approve/decline/cancel/removal reuse the same workflow; removal requires fresh review, and close/expiry/privacy/ownership changes never approve. Production reports no pending qualified offer. No schema, saved-mode, per-turn, account or egress changes occur ([review contract](Model_Providers_Memory_And_Knowledge.md#delivered-bounded-exact-native-handoff-review---2026-10-10)).

### Delivered bounded device-default response mode (R10)

Schema 1 admits only `responses.default-mode`: the existing `ResponseOutputMode`
enum values `Hybrid`, `VoiceOnly` and `VisualOnly`, default `Hybrid`. Native
Inspect/Save/Reset and [exact typed/activated commands](../docs/commands.md#inspect-or-change-the-device-default-response-mode)
share the [response-mode workflow](../src/Kora.Application/Configuration/ResponseModeConfigurationService.cs).
Discovery reports enum/default/device scope, reset/application timing, revision,
saved/default/unavailable provenance, desired and configured effective mode
(task > queue > device), with current host speech/mandatory-visual policy.
No session, task, queue or in-call option is registered by this slice.

The original `response-output-mode.txt` file, legacy case-insensitive defined-enum
parsing and shared atomic preference paths/replacement remain authoritative.
Missing storage is an unsaved Hybrid default; reset explicitly saves Hybrid,
matching normal set-to-default semantics rather than claiming file deletion.
The muted-output fallback file is untouched. Invalid/unreadable saved state,
unknown authority or failed audit/readback cannot become defaults or success.
A separate atomic `response-output-mode-unconfirmed.txt` marker precedes mode
replacement and is removed only after successful audit, terminal receipt and
exact readback. Interrupted/unconfirmed writes remain unavailable across restart;
inspection cannot silently clear the marker. Explicit saved-state/evidence repair
and fresh inspection are required before a new mutation.

The genuine audio-control admission records original-user intent and resolves
the persisted active session/generation. Host-held choices bind owner, revision,
original channel and live ownership/privacy/call/input admission; supplied enum
values, reconstructed choices, traces or foreign sessions are not authority.
The existing consolidated #83 connection/lease is consumed once; the callback
does not reacquire task/session authority. The call-policy lock rechecks original
channel and observed revision adjacent to audited atomic write and exact readback.
Publication waits for the durable terminal receipt and rechecks live eligibility.
Committed storage followed by audit/receipt failure remains explicitly unavailable
until inspection/recovery; no fake rollback or blind retry is reported.

Mutation invalidates stale queued/in-flight output before replacement and never
autoplays, replays, opens capture or changes consent/permission. The complete
interrupted visual response and mandatory warning/security/question/approval
preview remain available even in VoiceOnly. Pending exact interactions remain
untouched; configuration input is not an answer or approval. Protected/Unknown
calls deny original activated-voice changes, including later UI dispatch.
In-call/privacy and unavailable/muted/failed output suppression remain independent.

This is bounded R10 preference delivery, not full R10, native/acoustic acceptance,
new tools/grants/providers/gain/global mixer, or broader lifecycle authority.
Maintained deterministic grammar/workflow/UI/SQLite tests are not replacements
for unique speech/hardware/runtime/worker/storage experiments or historical receipts.

#### Retained-model response-mode proposal

The [closed registered-setting path](../docs/setting-proposals.md) also accepts
one exact successful retained answer: `set responses.default-mode to Hybrid`,
`set responses.default-mode to VoiceOnly`, or `set responses.default-mode to VisualOnly`.
The model policy advertises those canonical enum values as inert data, never
approval or write authority. Undefined/numeric enums, aliases, batches and
commentary are rejected without a write.

Both expanded and compact conversations reuse **Review setting suggestion...**,
showing current saved/default provenance, desired/configured effective mode,
the exact proposed mode, shared device-local future-output scope and mandatory
visual/privacy/call exceptions. Exact typed/current-name activated **review
response mode suggestion**, **confirm response mode change** and **cancel response
mode review** use the host-pinned successful answer, not a latest-answer lookup.

Separate fresh original-user confirmation consumes the ephemeral 60-second
review once through `ResponseModeConfigurationService`, fresh audio admission,
host-held choice, audited atomic preference write and committed readback.
Source/session/generation, preference revision, ownership, private topology,
original channel or call uncertainty invalidates authority. Activated-voice
source classification survives later UI confirmation and reaches the audio
admission and locked writer unchanged. Cancel, expiry, stale source and duplicate
confirmation cannot write. Failed persistence/audit/readback is explicitly
unconfirmed, possibly persisted, with no automatic retry or rollback claim.

Only the device default changes. Existing narrower overrides and mandatory full
visual warnings/security/questions/approvals and speech-unavailable recovery
remain authoritative even for VoiceOnly. Review/apply never re-executes a model,
synthesizes/replays the selected answer, opens capture, changes consent/grants
or enables an unqualified provider. Deterministic implementation coverage is
separate from hardware, acoustic, installed/native and provider E/Q evidence.

### Delivered bounded assistant display/PTT prefix (R10)

Schema 1 admits only `assistant.name` (spoken: **assistant name**), the already
delivered device-local presentation and explicitly activated PTT command-prefix
name. [The descriptor](../src/Kora.Core/Configuration/AssistantNameOption.cs)
references the existing `AssistantNameRules` defaults and exact bounds:
1-3 words, at most 32 UTF-16 characters after whitespace trimming/collapse,
letters/digits/spaces/apostrophes/hyphens and at least one letter/digit.
Legacy Unicode semantics are preserved, not changed to session-name NFC rules.
The existing command catalogue also rejects routing collisions.

[One host workflow](../src/Kora.Application/Configuration/AssistantNameConfigurationService.cs)
owns typed get/propose/apply/reset, process-local owned configuration revisions,
original host/request/channel and observed call revisions, serialization,
capture retirement, atomic domain persistence, audit and live notification.
Native Apply/reset and [exact typed/activated voice list/get/set/reset](../docs/commands.md#assistant-display--ptt-command-prefix-setting)
use it. The descriptor/current state reports schema, type/default/bounds,
device-local scope, presentation/routing effect, after-save timing, per-option
reset, revision, saved/default provenance and explicit recovery.
No Tools/model settings exposure or arbitrary option/path/patch is added.

The [existing parser](../src/Kora.Application/Configuration/AssistantNameCommand.cs) also admits bounded, non-overlapping natural whole utterances: `what is your name` (optional final `?`), `call yourself <name>`, `change your name to <name>`, and `reset your name`.
Natural set payloads retain raw spelling/case/content until the same service's authoritative name rules run; no generic command normalizer repairs a target.

The configured old prefix admits the rename request; only a successfully committed new prefix admits subsequent prefixed commands. Recognized malformed natural grammar clarifies before pending question/approval or inference fallback ([consumer parity tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.AssistantNameConfiguration.cs)).
Discovery and built-in help expose these aliases without a new registered tool, settings authority, preference store or capture lifecycle ([parser tests](../tests/Kora.Application.UnitTests/Configuration/AssistantNameCommandTests.cs)).

The unchanged legacy `assistant-name.txt` format is read without rewriting.
The existing preference domain and shared atomic preference store retain
normalization and path/temporary-file/replacement ownership. Missing values are
unsaved domain defaults; malformed/conflicting/unknown or unreadable saved
state disables prefix routing and capture visibly, never silently substitutes
Kora. Native and exact unprefixed recovery remains available.

Before a changed value can commit, input/grammar/transcript/completion
generations are retired and capture quiescence confirmed. After the awaited
release, configuration revision and live ownership/privacy/original-channel/
call revision are rechecked; the shared call-policy lock encloses the atomic
write. Concurrent/stale proposals, invalid input, cancelled requests, failed
shutdown/storage and denied host/call state have truthful terminal outcomes.
Cancellation is admitted before synchronous replacement, not reported as
unsaved after commit. Audit failure after replacement leaves routing unavailable
and reports unconfirmed completion until explicit inspection/recovery.

Publication updates all current name surfaces/help/catalogue/session/artifact
prefixes. It never changes stored session names or durable host/session/task/
grant/approval/instance identities, data paths, namespaces or authority.
Exact pending questions/approvals retain their targets; rename is not a reply
or approval. Old prefixes/captured callbacks are not recovery aliases. Listening
remains held until explicit enablement and a new PTT; nothing replays or opens
capture implicitly. Failure starting the new grammar reports capture unavailable
without restoring an old prefix. Stop/cancel/native recovery remain available.
Protected/unknown calls reject original voice set/reset, including a later UI
dispatch; a new eligible UI/typed request remains separately gated.

This slice does not qualify a production wake-name capability, custom profile,
acoustic quality, assets/learning/enrollment, provider download, OS/global
settings, consent/privacy downgrade, general registry or full R10/R09 acceptance.

### Delivered bounded appearance subset (R10)

Twelve independently persisted options are admitted:
`appearance.theme`, `appearance.presence-display`, `appearance.presence-timeout`,
`appearance.response-timeout`, `appearance.presence-size`,
`appearance.dot-size`, `appearance.dot-density`,
`appearance.movement-speed`, `appearance.speech-scaling` and
`appearance.speech-scale-amount`, `appearance.reader-text-scale`, and
`appearance.native-presence-renderer`. The [end-user reference](../docs/settings.md#appearance)
lists their exact types, units, defaults and bounds. Each descriptor declares
device-local scope, appearance-only effect, local-host availability,
immediate-after-save timing and per-option reset. Animation driven by playback
is not a voice-output or call-sensitive option.
The renderer option immediately queues a presentation-only replacement, deferred
through startup, gestures and owned dialogs; its separate status reports actual
application or failure. A saved renderer preference wins over the optional
environment startup default. Neither renderer selection nor switching restarts
voice/provider services. Native remains explicitly experimental.

The host-owned [registry](../src/Kora.Core/Configuration/AppearanceOptionRegistry.cs)
reuses existing domain validation; the
[service](../src/Kora.Application/Configuration/AppearanceConfigurationService.cs)
owns get/propose/apply/reset, a process-local revision, proposal provenance,
serialized revalidation/write/notification and typed audit outcomes.
Revision and proposal identity are not durable cross-process authority.
The instance-owner boundary admits the local host. No arbitrary option,
path, JSON patch, external configuration file or model proposal is accepted.
Saved-format parsing stays in the preference domain; malformed saved state
still throws `InvalidDataException`. Missing preferences use declared defaults
without claiming those defaults were written.

Direct appearance controls and exact deterministic discovery/get/set/reset
use the same service and notify all open surfaces. Cancellation is checked
before the existing synchronous atomic write; cancellation after commit does
not report an unsaved change. Save failure retains the previous value/revision
and produces visible failure; stale proposals require a new operation.
Reset restores one admitted default; no multi-file transaction, whole-profile
reset or undo is implemented. Shared response-window pin/topmost/position
and presence placement remain outside the appearance registry. The two existing
response-window Booleans additionally have [bounded exact typed/current-name command parity](../docs/commands.md#inspect-or-change-koras-response-window-visibility-preferences),
using the same native setters, tuple-preserving atomic path and
`configuration.response-window` audit identity; position remains direct native UX
([commands](../src/Kora.Application/ViewModels/MainViewModel.ResponseWindowCommands.cs),
[setters](../src/Kora.Application/ViewModels/MainViewModel.cs)).
Other voice/audio/call/manual state, grants, models, retention, dependencies and
startup are not registered. No model tools or broader execution authority
are exposed by the appearance slice.

The reader option is the separately bounded native [source/detail, complete serialized Evidence-page and observed speech-caption content typography contract](Information_Display.md#bounded-native-reader-text-scale): absolute integer percent, default 100, inclusive 75–200. Its original input/host eligibility is rechecked at commit; atomic pending evidence stays held until exact readback and successful audit. A failed terminal receipt does not claim disk rollback and new readers reject unconfirmed evidence. Open Evidence readers and captions retire explicitly when preference evidence becomes unavailable; no chrome or global UI scale is added. Caption labels, pin/display controls, accessible names, geometry, enablement, speech and privacy/call/source lifetimes are unchanged. Existing option formats and migrations are preserved ([workflow](../src/Kora.Application/Configuration/AppearanceConfigurationService.cs), [store](../src/Kora.Application/Configuration/LocalReaderTextScalePreferences.cs)).

Natural exact theme and native reader-scale [aliases](../docs/commands.md#natural-exact-app-theme-and-native-reader-text-scale) reuse this admitted service.
Relative percentage-point changes read confirmed current state at execution, use checked arithmetic and registry bounds, and produce existing revision-bound proposals; stale changes refuse.
Typed input and current-name-activated voice carry their original provenance ([parser](../src/Kora.Application/Configuration/NaturalAppearanceCommand.cs), [service](../src/Kora.Application/Configuration/AppearanceConfigurationService.cs)).

### Delivered bounded installed speech choices (R10)

Schema version 1 admits `speech.provider` (spoken name: speech provider) and
`speech.voice` (speech voice), using the delivered Windows SAPI (`windows-sapi`)
and Kokoro adapters only. Descriptors classify both as device-local,
installed-choice, voice-output settings, applied after atomic save to the next
speech operation. Discovery lists only installed catalogue voices and providers
with ready choices; it exposes current desired/effective values, defaults,
saved/default provenance, recovery and a process-local revision.

The default is Windows and its advertised culture-compatible default voice.
An unset voice means that provider's advertised default, not an arbitrary
replacement. Provider set/reset also resets the voice to that provider's
advertised default; provider reset restores Windows. Voice reset affects the
selected provider's voice only. An explicit qualified voice choice
`provider / voice ID` selects that exact pair atomically, including a voice
from another installed provider or one with no compatible default.
An unqualified ID is accepted only when unambiguous. Voice IDs are bounded to
256 characters and reject padding, empty values and controls.

The [typed registry](../src/Kora.Core/Configuration/SpeechOptionRegistry.cs),
[host workflow](../src/Kora.Application/Configuration/SpeechConfigurationService.cs),
[preference domain](../src/Kora.Application/Configuration/LocalTextToSpeechPreferences.cs)
and [exact grammar](../src/Kora.Application/Configuration/SpeechCommand.cs)
share the existing Settings selection/reset controls and typed/activated-voice
list/get/set/reset routes. A versioned single atomic selection file prevents
partial provider/voice writes. Existing separate preferences are read without
rewriting and are shadowed only after a successful explicit coherent save.
Malformed/unknown saved formats and providers remain explicit errors/recovery;
missing selected assets retain the desired choice and disable speech, never
silently download, substitute or repin. Explicit selection/reset or repair
and refresh provides recovery. Native synthesis failure creates a visible
run-only hold without changing the saved selection.

Owned proposals bind configuration revision, observed call revision and original
channel. The existing call-policy lock encloses live ownership/privacy/call
revalidation and the synchronous atomic write; stale/foreign proposals fail,
and reentrant notifications cannot mutate the registry. Cancellation is
admitted before commit, never reported as an unsaved change after commit.
Typed request/terminal audit outcomes precede live notification; failed
storage keeps the prior selection/revision. Audit evidence failure propagates,
does not claim success or activate the unaudited choice, and may require
inspection of the committed file if terminal evidence failed after replacement.

Protected calls reject original voice-channel set/reset even if later dispatched
from UI. A new eligible local UI/typed request remains subject to the existing
host gate; this slice changes no protection downgrade, exact consent or call
override policy. System/pinned output routing, speech privacy and mandatory
visual fallback are unchanged. Asset review/download/removal remains separate
from ready selection; finishing a download does not silently switch output.
No rate, microphone/tray recovery, model tools, provisioning
authority, general registry rewrite or full R10/acoustic acceptance is delivered.
Summary caps are delivered by the separate bounded slice below.
Playback volume is delivered by its separate admitted audio-control slice below.
Windows-native rate is delivered by its separate admitted provider-qualified
slice above; the original provider/voice slice supplies no rate authority.

### Delivered bounded spoken summary limits (R10)

The [closed registered-setting proposal registry](../src/Kora.Core/Configuration/RegisteredSettings/RegisteredSettingRegistry.cs)
now owns bounded inspection and the [shared suggestion parser/model policy](../src/Kora.Application/Configuration/RegisteredSettings/RegisteredSettingSuggestions.cs)
for the existing theme, reader-text-scale, two spoken-summary caps and Kora-only
playback-volume subset. Both native conversation sizes use one exact-answer
Review/Confirm/Cancel path through the existing family validators and audited
writers. No new setting family, model write tool, static command aliases,
provider qualification or authentication exception is introduced.
See the [complete user interaction and recovery](../docs/setting-proposals.md).

The [retained-answer review](../src/Kora.Application/Configuration/SpeechSummarySuggestionReview.cs)
completes the registered-setting model proposal route for these two caps only.
The canonical request policy advertises one inert exact Set, using the existing
parser and domain bounds. Successful retained execution/source provenance is
revalidated through the canonical host; model prose has no apply authority.
Both native conversation sizes and exact typed/current-name activated-voice
review/confirm/cancel routes show complete current/proposed tuples, saved/default
provenance, unchanged companion, shared device-local scope and next-operation
timing. A distinct fresh original-user confirmation consumes a memory-only,
60-second, revision/source-bound review through the existing speech proposal,
atomic call-policy/write and typed audit path. Source or confirmation voice
classification is preserved, including later native confirmation. Appearance
uses the shared surface but remains original-local-UI/native-only. Existing direct setters/resets
are unchanged; no option, cap, batch, provider qualification or approval grant is
added. Uncertain writes require explicit Settings/audit inspection, not retry,
rollback claims or fabricated success. The current answer is never shortened,
regenerated, spoken or replayed by the operation.

The [same speech parser](../src/Kora.Application/Configuration/SpeechCommand.cs) admits exact whole-utterance aliases: `show spoken summary limits`, `set spoken summary sentence limit to <integer>`, `set spoken summary word limit to <integer>`, `reset spoken summary sentence limit` and `reset spoken summary word limit`.

The prefix is the current configured assistant name: optional for typed input, required for activated-voice natural aliases. Provider/installed-voice grammar is unchanged ([dispatch](../src/Kora.Application/ViewModels/MainViewModel.cs)).

Canonical decimal values use the [authoritative domain validation](../src/Kora.Core/Configuration/SpokenSummaryLimits.cs); signs, leading zeroes, fractions, word numerals, suffixes and relative/vague requests are not natural budget commands. Reserved malformed aliases clarify locally before model/question/approval fallback ([early dispatch](../src/Kora.Application/ViewModels/MainViewModel.cs)).

The [shared command action](../src/Kora.Application/ViewModels/MainViewModel.SpeechConfiguration.cs) preserves pending questions/approvals and reports that they must be finished/cancelled first. It uses the existing proposal/apply path and original channel; no model tool, new setter, provider selection or permission is introduced.

Fixed exact phrases and help expose the bounded aliases ([parser](../src/Kora.Application/Configuration/SpeechCommand.cs), [discovery/help](../src/Kora.Application/ViewModels/MainViewModel.cs)).

These settings affect future ordinary-speech admission only: no immediate speech, summary generation, truncation, regeneration or replay ([deterministic alias tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.NaturalSpeechBudgets.cs)).

Reset changes only the requested cap and refuses unknown companion state ([service](../src/Kora.Application/Configuration/SpeechConfigurationService.cs)).

Registry schema version 2 adds `speech.summary-sentences` (positive integer
1-3, default 3) and `speech.summary-words` (1-80, default 80), with exact spoken
names speech summary sentences / speech summary words. One
[validated domain contract](../src/Kora.Core/Configuration/SpokenSummaryLimits.cs)
owns maxima, defaults and independent reset. Both use the existing speech
configuration service, audited/revisioned proposal lifecycle, original-channel
call lock, live host gate and notifications; no parallel registry or policy is
introduced. Native choices and exact typed/ACTIVATED discovery/get/set/reset
have parity. These settings remain usable without installed speech assets and
do not repair, substitute or change an unavailable provider/voice.

A separate versioned atomic device-local limit file preserves provider/voice
formats and legacy migration. Absent limits are unsaved defaults, not a write.
Malformed, unknown, out-of-range or unreadable saved values leave ordinary
speech unavailable with visible recovery. Repair and refresh is explicit;
per-option reset never guesses an unknown companion cap.

At the host's single ordinary-response speech boundary, the complete spoken
title/body/warnings must fit both caps. Current routes lack authoritative safe
omission metadata, so this slice deliberately refuses over-cap speech and
forces truthful full visual recovery rather than truncating or generating a
replacement. The owner approved this bounded admission behavior. Full visual
results/details remain unchanged; no new model request is made.
Exact proposal/approval readback, required question/options and bounded voice
previews retain existing mandatory bounds and privacy rules. No arbitrary
full-content reading tool is added or weakened.

The [Unicode measure](../src/Kora.Core/Voice/SpokenSummaryMeasure.cs) counts
letter/digit runs with specified mark/apostrophe/hyphen joiners and word-bearing
punctuation-delimited sentence segments, including trailing fragments.
Digit-surrounded ASCII dots, initials and a fixed abbreviation list are
nonterminal. [Exact supported semantics](../docs/settings.md#spoken-summary-limits)
include CJK, repeated punctuation, line breaks, decimals and acronym word
boundaries; these are deterministic text counts, not approximate characters,
natural-language inference or acoustic duration.

Configuration changes invalidate pending output before asynchronous UI
notification. The configuration lock encloses counting and policy-locked
provider enqueue, preventing stale limits at start; provider generations and
existing stop/privacy/disposal paths retire queued synthesis/playback without
replay. Cancellation is checked before admission/start and before writes;
post-commit cancellation does not invent an unsaved result.
This completes only bounded caps, not full R10 or speech/acoustic acceptance.
The following complete model-facing contract remains future work.

### Delivered bounded exact input-device preference (R10)

Schema 1 admits only `speech.input-device`: an exact endpoint-ID choice,
device-local, input-preference-only, default `system-default`. Native Settings,
tray and passive recovery-card selection share the existing atomic audio
preference format and the [audited preference workflow](../src/Kora.Application/Configuration/InputDevicePreferenceService.cs)
with typed and **activated** voice commands. Use `list input settings`,
`get speech.input-device`, `set speech.input-device to <exact listed endpoint ID>`
and `reset speech.input-device`; there are no friendly-name, index or
natural-language selector aliases. Option grammar is case-insensitive, endpoint
IDs are ordinal exact. The configured assistant-name prefix is supported.
The original input is limited to the existing 1,024 UTF-8 bytes, without controls;
the complete schema/result is limited to the existing 64 KiB, never truncated.

Discovery deliberately uses the current five-second single-flight metadata
refresh; get reports the recorded snapshot. Both expose desired/effective,
System default, saved/default/unavailable source, metadata/call revisions,
exact choices, availability, readiness and explicit recovery. Endpoint names
are local presentation content only. Duplicate friendly names remain distinct.
A missing pin survives startup/refresh; unknown or failed detection is
unavailable, not System or another same-name replacement. System follows the
Windows multimedia default; selecting/resetting System explicitly removes
only the existing microphone override, including when there is no default.

Commands resolve IDs to host-held catalogue objects before finite revalidation.
Equal-but-not-presented native choices remain rejected. Original host request/
session lineage and observed call/input/recovery revisions survive awaits;
the existing call-policy lock encloses live revalidation and atomic persistence.
Protected or unknown calls deny original voice-channel writes even if later
dispatched through UI. Unknown owner/session/permission, stale metadata,
cancellation, pending question/approval and disposal cannot authorize a write.
Audit/storage failures report not-confirmed; terminal audit failure may follow
a committed file replacement, so inspect before a fresh request, not automatic
retry or an invented rollback. Reentrant writes cannot publish a new choice.

Selection is preference only: changed input invalidates stale capture and
releases it, while manual disablement and run holds remain closed. No operation
grants consent/permission, opens capture, enables listening, tests audio,
downloads assets, changes models/OS privacy, forwards provider text, or answers
a pending question. Separate existing native Enable remains separate. No model
tools, general registry, full R10/R03/R09/R05/A or hardware acceptance is claimed.
### Delivered bounded exact output-device preference (R10)

Schema 1 admits the existing `speech.output-device` preference through shared
[configuration](../src/Kora.Application/Configuration/OutputDeviceConfigurationService.cs)
and [audio-control admission](../src/Kora.Application/Voice/AudioControlAdmission.cs).
Native Settings Refresh/Save/reset and exact typed/ACTIVATED commands share that
workflow: `list output settings`, `get speech.output-device`,
`status speech.output-device`, `set speech.output-device to <exact presented ID>`,
and `reset speech.output-device`. Grammar is case-insensitive; endpoint IDs are
ordinal exact. There are no spoken-name, friendly-name, index or fuzzy aliases.
Original input retains the existing 1,024 UTF-8 byte/control limits; complete
versioned results retain 64 KiB bounds and are rejected, never truncated.

The bridge records original local intent using existing authoritative host
workspace/task services, commits a real active device-control session, and
leases that persisted session/generation during each control operation.
Fresh IDs propose identities only: durable admission, current host ownership,
unlocked privacy, original input and current call/input/recovery revisions
authorize the operation. Trace/provider/user fields cannot select the session
or supply authority. This consumer-focused bridge grants no approval, question,
tool, microphone, task cancellation or provisioning authority.

Five-second single-flight discovery enumerates endpoint metadata only. Host-held
choices bind exact object identity, session/generation, original channel,
captured live eligibility, topology/default and preference revisions.
Cross-session, equal-but-unpresented, stale or changed saved preferences require
a fresh discovery; SET cannot implicitly invent a never-presented choice.
Get/status report desired, saved/default/unavailable source, effective route,
live System default, metadata/call revisions, choices, mute and recovery.
Duplicate friendly names remain distinct. Missing pins remain saved/unavailable;
System follows the live multimedia default and reset removes only Kora's override.
Native speaker availability uses that same observed recovery state after general,
topology and Settings refresh: a removed saved endpoint is never described as
ready for playback merely because its preference is retained. Refreshing a
returned endpoint or explicitly saving another choice never replays a response.

The existing atomic preference store/paths and original-channel call-policy lock
enclose persistence and typed audit. Protected/unknown calls deny voice-originated
writes, including UI-dispatched voice requests. Evidence or lifecycle failures
hold output unavailable rather than claiming rollback or replaying an effect;
a file may already be committed when terminal evidence fails. Inspect and refresh
explicitly. Activation/live notification follows confirmed receipts.

Changes retire stale queued/in-flight output and late provider callbacks without
replaying speech or reopening capture. Selection/reset never plays a trial,
changes Windows defaults/mute/volume, releases run holds, answers a question,
grants consent or changes independent provider/voice/name/input/summary settings.
Ordinary later speech freshly resolves the route. Missing, software-muted,
zero-volume, open/playback or cancellation failures preserve the complete visual
response under existing privacy gates; acoustic audibility is not claimed.
The legacy muted-fallback preference remains stored independently but cannot
suppress mandatory full visual recovery.

This is a bounded preference feature, not full R10/I/A or acoustic acceptance.
Unique speech/acoustic/hardware/provider experiment receipts and executables are
retained: metadata, storage and native-binding fixtures do not supersede them.

### Delivered bounded per-Kora playback volume (R10)

`speech.playback-volume` is a device-local **integer percent, 0-100 inclusive**.
The unsaved/reset default is **100**, preserving the original unscaled engine
output; it is not amplification or a Windows volume percentage. One
[domain scalar](../src/Kora.Core/Configuration/PlaybackVolume.cs) validates exact
canonical decimal input (no sign, padding, fractional value, percent suffix or
leading zeros). A separate version-1 atomic preference file uses the existing
store and application-data paths; no other preference is rewritten.

Native Settings draft/save/reset and exact typed/ACTIVATED
`list volume settings`, `get/status speech.playback-volume`,
`set speech.playback-volume to <0-100>` and `reset speech.playback-volume`
share the [volume workflow](../src/Kora.Application/Configuration/PlaybackVolumeConfigurationService.cs).
Exact typed/current-name ACTIVATED `speak louder` / `speak quieter` take a [domain-owned step of up to 10 percentage points](../src/Kora.Core/Configuration/PlaybackVolume.cs#L21).
They refresh original-channel admission before deriving an exact target from desired state/revision, then use the [existing held proposal and audited apply action](../src/Kora.Application/Configuration/PlaybackVolumeConfigurationService.cs#L86); they never mutate the draft or infer a target through a model.
The [result orchestration](../src/Kora.Application/ViewModels/MainViewModel.PlaybackVolume.cs#L76) explicitly reports a final shorter step reaching 0/100, or unchanged if already there.
No unchanged-boundary write or successful-change audit occurs. Stale revisions/saved state refuse rather than retrying a lost-update read-modify-write ([proposal](../src/Kora.Application/Configuration/PlaybackVolumeConfigurationService.cs#L86)).
The [boundary-result test](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.RelativeAudio.cs) verifies explicit previous/current values, exact desired/effective scalar and saved source, not just an outcome label. A newer committed scalar is preserved when an older relative proposal is rejected; no stale target is silently recomputed.
Relative result envelopes add `adjustment.previousPercent` / `currentPercent` or `adjustment.previousNativeRate` / `currentNativeRate`; each old value is the freshly admitted desired scalar and each new value is the actual result state, not a proposed target ([result tests](../tests/Kora.Application.UnitTests/Configuration/AudioPreferenceRelativeResultTests.cs)).
Numeric setter/status/reset result envelopes remain unchanged ([volume](../src/Kora.Application/Configuration/PlaybackVolumeState.cs), [rate](../src/Kora.Application/Configuration/WindowsSpeechRateState.cs)).
The current assistant-name prefix, 1,024-byte UTF-8/control input bound and
complete 64-KiB result bound apply. Results expose desired/effective percent,
saved/default/unavailable provenance, default/bounds, availability, recovery,
configuration/call revisions, application timing and independent reset.

#### Retained model playback-volume suggestion

The complete successful retained ASSISTANT answer may be exactly
`set speech.playback-volume to <canonical integer 0-100>`. Host review accepts
only this single Set form through the existing command/domain scalar, not model
authority, resets, relative adjustments, commentary or other settings.
Expanded/compact Sessions review the actual selected immutable answer; exact
typed/current-name activated commands use the pinned successful submission
receipt. Both use the shared exact retained-setting double-read validator.

The memory-only review shows desired/effective percent, provenance, proposed
percent, shared device-local scope, recovery and zero-synthesis implications.
It writes nothing. Separate fresh confirmation within 60 seconds revalidates
the original source/session/channel, ownership/privacy/call and preference/review
identities. Voice origin at source, review or confirmation remains restrictive
inside the final call-policy lock. The existing playback settings owner retains
its active-operation guard and uses the existing configuration writer and
AudioControlAdmission; preference, audit and terminal readbacks precede success.
Cancellation, expiry, revocation, changed sources/settings and disposal prevent
late authority revival; cancellation alone is not quiescence. Unconfirmed writes
stay explicitly unavailable with no retry/rollback claim. No provider, synthesis,
replay, OS/device/call gain or other settings authority is introduced.
See [user workflow](../docs/settings.md#review-an-assistant-kora-volume-suggestion).

An atomic unconfirmed-write marker is installed before changing the scalar
file (including reset) and removed only after exact readback and both audit/task
terminal receipts. Failed evidence therefore remains unavailable across restart,
not just in memory. The marker is not authority or a new registry. Inspect the
committed scalar and receipts before explicitly repairing unconfirmed state;
normal refresh cannot silently clear that hold.

The existing persisted audio-control admission resolves original local user
input and an active session/generation. Host-held scalar proposals bind that
authority, original channel, captured ownership/unlocked privacy/call/input
eligibility and preference revision. Requested audit, live call-policy-locked
revalidation, atomic persistence, exact readback and terminal task/audit receipts
precede activation. Unknown/protected calls deny voice mutations; correlation
IDs and lookalike/stale/foreign proposals supply no authority. Changed saved
state or failed storage/readback/evidence leaves output explicitly unavailable;
committed bytes are not falsely described as rolled back. Invalid saved values
throw `InvalidDataException`; inspect/repair and refresh explicitly.

Zero prevents new synthesis/automatic playback and keeps the **complete original
visual response**, including warnings and mandatory question/approval readback.
Changes retire active and queued output; raising/resetting never replays it.
Windows uses gain on Kora's owned `SpeechSynthesizer` instance only. Delivered
Kokoro uses signed PCM16 little-endian attenuation toward zero; unity is
byte-identical and no sample is amplified. Empty/incomplete/unsupported samples
fail visibly. Adapters without qualified owned-gain capability are explicitly
unavailable, not silent-success audio.

No system/call/other-process volume, mixer, mute or default device is written.
Microphone/PTT, consent, permission, run holds, provider/voice/rate/downloads,
summary caps (3 sentences/80 words), output routing, pinned/unavailable/System
behavior, name, grants, approvals and pending previews remain independent.
Stop/recovery stays available. The preference stores no response/audio content
and changes no session, ordinary diagnostic or audit retention.

This completes only bounded per-Kora volume, not full R10, provider/native or
hardware/acoustic acceptance. The speech experiment's file-rendered SAPI
samples, capture/threshold executable, hardware/provider gaps and historical
receipts have no whole-executable maintained equivalent here: retain all;
nothing is deleted or rerun.

### Bounded communications activity source

The device-local `calls.activity-source` defaults/resets to `None`.
Settings > Calls and exact typed/current-name activated `list call observation settings`, `get/status calls.activity-source`, `set calls.activity-source to None|WindowsCommunicationsAudio` and `reset calls.activity-source` share the [host writer](../src/Kora.Application/Configuration/CommunicationActivityConfigurationService.cs), not a caller-selected OS authority.

The [bounded source contract](Call_Aware_Speech.md#bounded-windows-communications-activity-observations---2026-10-26) owns normalization, freshness and limitations.
Native status shows selected source, qualification, automatic evidence, observed UTC time and health.
Production Windows qualification is unavailable: enable selection is disabled/denied, no native subscription is opened, and saved enabled-but-unavailable state remains Unknown.

Original protected-call voice changes are rejected before mutation or relabelled confirmation.
Source changes preserve manual mode, grants, microphone consent and independent spoken-approval policy; disable completes only after prior owned observation release.
Unconfirmed preference/audit/resource evidence remains protective and explicit, never automatic recovery ([preference format](../src/Kora.Application/Configuration/LocalCommunicationActivityPreferences.cs), [regressions](../tests/Kora.Application.UnitTests/Configuration/CommunicationActivityTests.cs)).

### Proposed full configuration contract

Voice and settings UI use the same typed host configuration service.
There are no UI-only preferences or hidden configuration-file edits required for normal use.
An extension cannot introduce a settings screen without registering equivalent verbal discovery/get/set/reset operations.
Common unambiguous settings operations also register exact deterministic
commands, while the model receives typed discovery/get/set/reset actions for
natural-language interpretation. Both routes call the same host configuration
operation and policy gate; model identification is never execution authority.
Any setting that cannot safely support one of these routes documents the
specific interaction, ambiguity, availability, privacy, or origin constraint
in the capability catalogue.

The desktop system-tray menu exposes a single-instance Settings window with
every setting currently implemented by the host. All open settings surfaces
observe the same live state: mouse and validated verbal mutations publish the
same change notifications, so neither surface requires reopening or polling to
show the new effective value.

Bounded R15 now supplies run-scoped manual call controls and truthful automatic
availability in **Settings > Calls**, outside the future generic settings/tool
registry. Original voice provenance, observed call revision and live ownership/
privacy/input/native-lifetime and committed original-user session-generation
admission gate manual changes. Exact `list call settings`, get/status/set-on/
set-off/reset-off for `call.manual-active` share those native controls, not a
saved call tuple or model tool. Passive cached inspection never creates an
effect/approval/activity or replaces a question/security preview. Changed
manual state retires old speech/input/callbacks; off/reset never replay or reopen.
Required correlated authority audits reuse the consolidated lease; lost
evidence is explicit conservative protection, not rollback or less protection.
Protected calls reject voice-originated voice and
in-call option writes, including reset/clear semantics; read-only inspection and
stop/cancel remain eligible. Saved output/activation preferences are retained.
New call-protection downgrades and temporary/speak-once exceptions remain
unavailable pending complete exact trusted review; they are not exposed as
working verbal/model operations. See the [bounded R15 manual parity](Call_Aware_Speech.md#delivered-manual-command-parity---2026-10-08).

Exact whole-utterance `turn on manual call protection`, `turn off manual call protection` and `show manual call status` also use the [authoritative manual parser](../src/Kora.Application/Communication/ManualCallCommand.cs) and its fixed recognition/help phrases, optionally after the current assistant name. Quoted, embedded, fuzzy and conversational call claims are not command aliases.

These are run-only manual-layer operations, not detector controls. Off/reset never assert automatic Clear; Active/Unknown evidence and unavailable receipts retain protection. [Admission and retirement](../src/Kora.Application/ViewModels/MainViewModel.Communication.cs) are unchanged: protected-call voice writes remain denied, and host/privacy/listening gates may make voice unavailable.

Bounded **I - Implementation** uses [deterministic route tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.ManualCallCommands.cs). Automatic detection, new call exceptions, manual persistence and hardware/acoustic/installed **E - Enablement / Q - RC** qualification are not enabled; see [delivery boundaries](Call_Aware_Speech.md#delivered-natural-manual-call-phrases).

Each registered option declares:

- Stable ID/category, description, spoken names/aliases, type, units, allowed values/range, and default.
- Scope: device, conversation/task, source/connector, or bounded temporary override.
- Availability/dependencies, validation, sensitivity, confirmation rule, and application timing.
- Whether it can be reset, and the changes affected by reset.
- Host-assigned voice/in-call effect classification, including whether the protected-call initiating-channel restriction applies.

The model may interpret a request into an option/value proposal; the host validates it against the registry.
It cannot supply a configuration-file path, arbitrary JSON patch, shell command, executable reference, or undocumented option.
Exact basic settings commands remain usable without a model/network.
Changing a supported preference is not permission to alter policy implementation, install software, grant tools, or execute an action.
Model-facing discovery/get/change/reset/undo operations are inventoried in [Internal Model Tools](Internal_Model_Tools.md#grants-settings-and-evidence).
The [in-call settings origin gate](Call_Aware_Speech.md#in-call-settings-origin-gate) rejects voice-initiated changes to voice settings and every in-call-related option while protected call evidence applies.
This includes reset/undo, detector/manual-state changes, spoken-approval enablement and temporary/speak-once call overrides; a new UI-originated request is required, not UI confirmation of the rejected voice request. Call entry does not withdraw applicable grants or session levels.
Deterministic safety controls and read-only inspection remain available; this is an explicit exception to ordinary verbal-setting availability.

## Voice Interaction

Examples after the currently configured assistant name (initially "Kora"):

- "What settings can I change?"
- "What are my speech settings?"
- "What is my queue limit?"
- "Set the queue limit to twenty."
- "Speak a little slower."
- "Keep that setting just for this conversation."
- "Make that my default."
- "Reset speech settings to defaults."
- "Undo the last settings change."

Resolve the option, value, units, target, and duration. Ask if any is ambiguous.
Read back material changes or show the exact proposal when speech is gated.
Apply immediately only when validation and the applicable confirmation succeed.
Report both the saved preference and effective value when capability/policy limits differ.
Never silently clamp an invalid value or use a success-shaped fallback.

Relative changes such as "slower" resolve to a displayed concrete value within the engine's advertised range.
Changes that disable speech are acknowledged visually under the resulting policy.
Use device-local persistence by default; explicit task/conversation/temporary scope does not change the default.
Do not roam preferences, grants, device IDs, or credentials with skill files.
Persisted preferences include the explicit ongoing voice-consent choice, not
live capture or run-scoped recovery holds. Manual listening disablement, manual
call state and temporary overrides do not persist across ordinary restart;
consent withdrawal does. See the
[microphone matrix](Security_Data_Flows.md#microphone-consent-and-enablement-matrix).

## 1. Voice Input and Activation

The following includes intended settings, not an inventory of delivered
controls. The approved [local speech-input design](Speech_Input.md) adds
separate provider/resource choices; production wake, new ASR providers,
learning and enrolled verification remain unavailable until their own
implementation and qualification. No new setting IDs or command syntax are
registered by this design.

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Input device | System by default and follows live Windows multimedia-default changes; a specific endpoint-ID override remains pinned until changed back to System; changing selection after manual disablement does not restart capture | "Use my headset microphone" |
| Listening enabled | Closed until explicit first-launch ongoing consent; saved consent permits automatic ordinary safe startup; manual disablement/recovery holds release capture for the current run | "Stop listening" |
| Assistant display and command name | "Kora" initially; one validated custom-only name of 1-3 words and at most 32 characters | "Change your name to Nova" |
| Optional PTT shortcut | Unassigned until selected; validate conflicts | "Set push-to-talk to Control Shift Space" |
| Activation feedback | Visual always; non-speech cue when output policy permits | "Turn off the activation sound" |
| Command speech-start wait | 5 seconds; 2-10 seconds | "Wait seven seconds after I say Kora" |
| Conversational replies | On after ongoing listening consent; allows prefix-free answers only to the unique question Kora just presented | "Require me to say Kora for every answer" |
| Conversational reply speech-start wait | 15 seconds; 5-60 seconds; expiry returns to wake listening without dismissing the question | "Wait twenty seconds for my answer" |
| Trailing-silence endpoint | 1 second; 0.5-3 seconds | "Allow two seconds of silence before finishing my command" |
| Maximum utterance | 60 seconds; 10-120 seconds | "Limit commands to forty-five seconds" |
| Recognition language/model | Initial supported local English configuration; choose only delivered/ready assets | "Use the more accurate installed recognition model" |
| Local input provider profiles (planned) | Separate qualified wake/name and transcription engine/model/language selections; no hosted audio or automatic provider substitution | "Show my local speech input options" |
| Optional speech assets (bounded preparation delivered) | Windows x64 Settings and unchecked installer preparation share exact PocketSphinx/Silero data, review/prepare/inspect/verify/remove; preparation never enables capture | Native Settings while speech is unavailable; no new verbal syntax registered |
| Speech resource profile (planned) | Measured Low resource, Balanced, Accuracy focused or Custom choices; supported CPU/GPU and explicit keep-loaded/idle-unload policy; no silent protection changes | "Show the speech accuracy and resource trade-offs" |
| Learn my voice (`voice.learnFrequentSpeaker`) | Off until separate explained consent; local Windows-profile/device-scoped adaptation from new activated commands only; non-authorizing; status/test/correct/reset/delete workflows | "Learn my voice to improve recognition" |
| Owner-aware private speech | Optional; On for an explicitly enrolled supported verifier, not baseline voice or learning; while enabled, private content is visual-only for `Uncertain`, `NotOwner`, or `Unavailable`, without silently switching protection Off | "Only read private information when you recognise my voice" |

The [delivered speech-input review](../docs/voice-and-audio.md#speech-input-profile-preference) is narrower than the planned local-provider/resource rows: native controls and exact typed/activated commands review, confirm or cancel the existing compatible Windows activated tuple. The [host workflow](../src/Kora.Application/Configuration/SpeechInputConfigurationService.cs) joins runtime retirement before persistence/publication and preserves prior desired intent on cancellation.

No additional qualified local wake/ASR/resource tuple is offered. Existing unsupported desired preferences remain unavailable until an explicit compatible recovery is confirmed; review does not qualify prepared assets or automatically enable listening ([catalogue](../src/Kora.Core/Voice/SpeechInputProfileCatalogue.cs), [native host](../src/Kora.Application/ViewModels/MainViewModel.SpeechInput.cs)).

The default assistant name is "Kora"; [Assistant and Activation Name](Activation_Name.md)
defines the implemented display/command identity, its device-local persistence,
and the stronger detector requirements that remain future work. The current
name is custom-only: after commit, the previous name is not retained as a
hidden alias. The executable, application-data roots, assemblies, log names,
and trust identity remain Kora.
Device switching revalidates session, capture, and playback-rejection safety.
First run selects System without recording and obtains explicit ongoing voice
consent before enabling listening. Later ordinary startup may automatically
enable after saved consent and fresh gates under the
[microphone matrix](Security_Data_Flows.md#microphone-consent-and-enablement-matrix).
System uses Windows default-device stream routing during enabled capture, so later
multimedia-default changes are applied without creating a Kora endpoint override
or reopening capture. Device-loss
replacement always works by mouse through
[Interaction Fallback](Interaction_Fallback.md); hot-plug/default changes do
not silently replace a specifically selected endpoint. The persistent system tray
context menu lists detected microphones and provides Disable listening while
capture is active plus Enable listening for recovery after manual disablement or
failure; selecting a replacement alone does not restart recording.
Normal authoritative unlock can restore only previously enabled PTT readiness
or a separately qualified wake-only mode after confirmed closure and fresh
microphone-matrix gates. It does not resume interrupted capture or override mute
or withdrawal. Resume, restored permissions, reconnection, asset repair and
selection changes require explicit Enable listening; intervening failures cancel
automatic unlock restoration. Production always-on detection remains unavailable. Without owner-aware protection selected, normal privacy/output/call
policy governs baseline speech; unavailable verification alone does not mute it.
"Start listening" can set the preference only through an already available explicit input channel; a closed microphone cannot receive the utterance.
Do not keep a secret listening path merely to support voice unmute.
Speaker enrollment, replacement, deletion, and verifier threshold policy remain protected biometric workflows with Windows Hello or equivalent OS reauthentication.
That requirement applies to explicitly enrolled verification, not merely optional non-authorizing frequent-speaker learning.
Learning consent/status/reset/deletion follow [Optional Local Frequent-Speaker Learning](Security_Data_Flows.md#optional-local-frequent-speaker-learning); Off stops learning but is not an implicit deletion of derived features.
Voice can initiate verification enrollment but cannot bypass its secure flow. Changes to owner-aware output/approval preferences use explicit exact voice/UI confirmation, not automatic template adaptation.
All voice-profile settings and management changes obey the protected-call origin gate; status inspection remains available.

Performance profiles resolve to exact supported settings; saved/effective
values and unavailable reasons are distinct. Changing model residency affects
memory and next-request startup, not microphone consent or audio retention.
Provider/profile switching retires capture and old callbacks before new
admission and never clears recovery holds. Unsupported personalization is
explicitly unavailable, not a promise that every installed ASR model learns.
An optional explicitly started local setup comparison uses inert phrases,
discards audio and does not execute commands or alter settings until confirmed.
It is separate from the existing no-transcription microphone test.
The [optional component lifecycle](Environment_Setup.md#optional-local-speech-component-lifecycle)
keeps WiX/Burn preparation and runtime Settings consistent. Model install/remove
does not change the selected reasoning provider, output voice, microphone or
speaker-protection policy. Keep installed/verified/selected/effective states
visible and report missing or incompatible retained assets without fallback.

## 2. Spoken and Visual Responses

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Response mode | Hybrid device default; explicit session and transient task/queue overrides; applicable in-call > task > queue > session > device, subject to mandatory policy and explicit permitted speak-once exception | "Use visual responses only for this session" |
| Output device | System by default and follows live Windows multimedia-default changes; a specific endpoint-ID override remains pinned until changed back to System; stale/missing/unselected/muted/zero-volume/open/playback failure forces visual fallback | "Speak through my headphones" |
| Local voice | Supported installed voice; do not silently download on selection | "Use the second installed voice" |
| Speech rate | Engine normal; advertised supported range | "Speak twenty percent slower" |
| Kora playback volume | 100% original/unscaled; integer 0-100%, Kora-owned speech only; zero blocks synthesis with full visual output | Delivered exact: "set speech.playback-volume to 30" |
| Spoken summary length | At most 3 sentences/80 words; user may lower either limit | "Keep spoken summaries under forty words" |
| Detail presentation | Offer by default / Open automatically / Link only; applies to host-classified detail-recommended finalized foreground responses | "Always open detailed results" / "Stop asking about details" |
| Speech text / rich display | Independent optional captions, source/rendered Markdown, diagram and viewer preferences | "Show the words you're saying" |
| Theme | System by default; System follows live Windows appearance, while Light/Dark override every Kora visual surface | "Use the dark theme" |
| Presence timeout | 10 seconds; 1-60 seconds; reset by Kora interaction; automatically hides idle/listening presence without prompts, never work, speech, or required attention | "Hide your presence after ten seconds without interaction" |
| Response timeout | 5 seconds; 1-60 seconds; independent device-local setting for unpinned response-window inactivity; pending prompts/actions retain the response | "Hide your response after fifteen seconds without interaction" |
| Presence size | 360 px; 240-600 px; applies immediately and preserves bottom-right anchoring | "Make your presence 400 pixels wide" |
| Presence dot size | 100%; 50-200%; changes particle diameter without changing particle count | "Make the presence dots 120 percent" |
| Presence movement speed | 100%; 25-200%; scales state-driven particle movement | "Set presence movement speed to 75 percent" |
| Presence placement | Bottom-right working area; validated display/corner/margin | "Put your presence in the top-right of my second monitor" |
| Reduced motion | Follow system; may enable explicitly | "Use reduced motion" |

Visual-only does not close the microphone. Output-device changes never change global system/call volume.
Voice-only still preserves required visual approval/error/fallback surfaces; it cannot suppress safety information.
When speech output is missing, temporarily unavailable, or fails during playback, the response text and window are forced visible regardless of the effective response mode.
Task and queue response-mode overrides expire with their execution scope; an explicitly chosen session mode is persisted with that session until reset/deletion. The device default changes only explicitly.

### Delivered one-task response output

The [native one-task review](../src/Kora/SessionsViewModel.TaskResponseOutput.cs) applies to a new ordinary request in the exact selected Active conversation. Choose Inherit, VisualOnly, Hybrid or VoiceOnly; review the complete unchanged input, destination, original channel and configured output, then separately confirm submission once. Cancel, editing input/choice, replacing the review or private closure retires its controls without submission.

The [shared request review](../src/Kora.Application/Hosting/SessionRequestCommandService.cs) also accepts `session request output <session-id> <generation> <metadata-revision> <Inherit|VisualOnly|Hybrid|VoiceOnly> -- <original body>`, followed by the existing separate confirm/cancel commands. Activated voice keeps the original channel and protected-call preference gate; a later native click cannot relabel it.

The [host-owned output binding](../src/Kora.Application/Hosting/SessionConversationService.TaskResponseOutput.cs) captures the choice once for the newly issued request/task, not provider payloads or model context. Only an exact confirmed retained terminal result reaches output. Queueing never extends original context/review/authority expiry; cancellation, stale identity and unconfirmed retention cannot replay an answer.

Inherit captures the existing ordinary task/queue/device resolution at review; it does not invent a persisted session-mode consumer or decide session-persistent output timing. The [shared output consumer](../src/Kora.Application/ViewModels/MainViewModel.TaskResponseOutput.cs) reports declared/configured/effective output, revalidates mandatory host/privacy/call/output gates, and retires the transient choice when output joins. Required errors, questions, approvals and complete output-loss fallback remain visual; speech uses the existing caps. Later tasks and saved preferences are unchanged.

These controls do not qualify or enable a model/runtime, change model sources/egress/permissions/retention, create listening consent, change the microphone, replay old output or cancel other work ([qualification boundary](Implementation_Roadmap.md#delivery-and-qualification-dependency-policy), [behavioral evidence](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.TaskResponseOutput.cs)).

Specific microphone and output selections are device-local Kora overrides. A
saved override wins on later starts while that stable endpoint exists. Missing
saved or current explicit endpoints are not replaced silently; the user must
select a replacement or System. Selecting System clears the relevant override
immediately and follows the current and subsequent Windows multimedia defaults.
Endpoint presence, active state, Windows software mute/zero volume, open errors, and playback errors are detectable.
Software mute forces visual output; Kora never changes global mute or volume automatically.
Physical audibility beyond Windows (powered-off speakers, disconnected analog paths, unreported hardware mute/volume) is not reliably detectable and is covered by explicit preview/recovery UX.
Unsupported voices/languages/display IDs produce an explicit available-choice response.
Exploratory animation styles and native visual features are configurable only once implemented and capability-tested.
The exact caption, rich rendering, viewer, and text-scale options are defined in [Information Display](Information_Display.md#voice-settings-and-navigation); they use this same configuration contract.

## 3. Calls and Proactive Interaction

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Call speech mode | Suppress automatic requested/proactive speech; one-shot override allowed | "Only suppress unsolicited suggestions during calls" |
| In-call feedback override (`calls.feedback-mode`) | **Bounded delivered** UI default; Voice / UI / Both / Inherit; Active/Suspected only, ahead of ordinary response mode with all hard speech/privacy gates | Exact `set calls.feedback-mode to UI`; natural-language example remains proposed |
| Allow spoken approvals during calls (`calls.allowSpokenApprovals`) | **Delivered native review/confirmation**, Off by default; Boolean device-local setting; UI approval remains available, grants and session levels unchanged; enabling requires fresh native initiation and confirmation | Use Calls native controls; natural-language setter remains proposed |
| Voice activation during calls | On; independently configurable and does not reopen capture without explicit listening consent | "Disable voice activation during calls" |
| Unknown enabled-detector behaviour | Suppress automatic speech | "Use normal speech when detection is unavailable" |
| Detector enablement/account | Explicitly configured supported sources only | "Use Teams presence to decide when to stay quiet" |
| Busy/DND/meeting quieting | Call/Suspected meeting handling as in call policy; additional Busy/DND preference off | "Stay quiet when Teams says do not disturb" |
| Temporary call override | Up to 1 hour or observed call end; never persistent implicitly | "Allow spoken answers for this call for fifteen minutes" |
| Proactive speech consent | Explained and chosen in setup | "You can make suggestions again" |
| Quiet hours | None until chosen; local time/time zone shown | "Don't interrupt me between nine and five on weekdays" |
| Quiet mode | Off normally; conversation or persistent scope explicit | "Use quiet mode for this conversation" |
| Notification categories | Eligible task/setup/update events; categories individually configurable | "Tell me about task failures but not completed tasks" |
| Reminder deferral | 24 hours; 1 hour-7 days | "Remind me about updates tomorrow" |

Detectors need explicit setup/account/network consent; setting a preference does not create Graph credentials.
Only the bounded feedback/manual controls and existing protection settings are
delivered here. Other table entries, including general proactive/quiet-hours/
notification/temporary-exception configuration, remain proposed.
The in-call feedback override and voice-activation setting are device-local and independent: UI-only output does not close the microphone, while disabling call-time voice activation closes active capture and blocks re-enabling it until the call clears.
Inherit restores normal task/queue/session/device feedback precedence; changing the feedback mode does not silently relax separate call speech suppression/privacy rules.
The spoken-approval setting is independent of feedback/listening/speech suppression and does not alter stored grants or their retention, or the selected session permission level.
[Spoken Approvals During Calls](Call_Aware_Speech.md#spoken-approvals-during-calls)
owns answer-acceptance races, migration and approval rules. It supersedes
grant-ignore behavior; call entry preserves applicable grants and levels.
When no detector is configured, Kora reports automatic call detection as unavailable and preserves the ordinary response and listening configuration.
Hard lock/mute rules always outrank call/proactive preferences.
Notification settings cannot hide necessary action approval from the visual interface or turn silence into approval.

## 4. Work and Context

The table includes proposed controls as well as the [delivered future ordinary conversation deadline](#delivered-future-ordinary-conversation-deadline). The [fixed local-version compatibility queue](#delivered-bounded-fixed-local-version-queue-settings-r10r13) remains a separate internal contract; its retired public scheduling/configuration routes are not restored.

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Pending queue capacity | 10; 1-50 entries | "Set the queue limit to twenty" |
| Successful-completion dispatch | Automatic next ready item; manual mode optional | "Ask before starting each queued task" |
| Pending request lifetime | Delivered fixed local-version subset: canonical integer 1-120 minutes, default/reset 30, future newly enqueued entries only; broader routing remains proposed | `set queue.pending-lifetime-minutes to 60` |
| Active-task deadline | Delivered future ordinary conversation subset: default/reset 5, canonical integer 1-60 minutes within earlier context and fixed tool/provider limits; no user-wait turns | `set work.active-deadline-minutes to 10` |
| Session automatic archive inactivity | 24 hours; configurable finite positive duration | "Mark sessions done after two idle days" |
| Session automatic deletion inactivity | 30 days from last meaningful activity; configurable and later than archive | "Delete sessions after sixty idle days" |
| Concurrent session task limit | Proposed default 2; positive limit within verified provider/hardware envelope; one task per session | "Run up to three sessions at once" |
| Model-assisted session routing | On where verified; explicit targeting wins, clear Active match continues, otherwise new session/clarification | "Turn off automatic related-session matching" |
| Owner-voice approval preference | Optional additional confidence; Off by default; available only after verifier proof/enrollment | "Require voice matching for spoken high-risk approvals" |
| Clipboard text limit | 256 KiB maximum; may lower | "Limit clipboard snapshots to sixty-four kilobytes" |
| Model-bound tool-result limit | 64 KiB maximum; may lower | "Limit tool excerpts to thirty-two kilobytes" |

Lowering capacity never evicts existing work; hold admissions until occupancy fits.
Shortening lifetime/expiry shows affected requests/content and requires confirmation before expiring them immediately.
Archive and deletion are independent configurable values using one inactivity clock, not time since creation/Done.
Those session-retention settings, timer, automatic purge and apply-now controls
remain proposed, not registered production options. The delivered native
[logical disposition](Interaction_And_Sessions.md#delivered-bounded-exact-id-logical-disposition---2026-10-08)
is a separate explicit exact-ID preview/confirmation, not a retention setting
or full recoverable-copy deletion. It removes addressed live authority rows
only, preserving independent Perpetual grants, task/audit provenance and
disclosed recoverable copies. Diagnostic/audit setting changes never invoke it.
Passive selection/history queries do not extend it; actual user/work activity and explicit resume do.
Changing retention previews resulting due dates and requires a separate apply-now decision for immediate archive/deletion; otherwise existing due dates remain until subsequent meaningful activity, with new sessions using the new policy.
Normal automatic expiry under the disclosed policy does not require repeated per-session confirmation.
Lowering concurrency affects future admissions, not active-task cancellation; unknown resource effects still require exclusive coordination.
Extending a default deadline affects future tasks; changing the current task requires explicit task scope and revalidation.
Manual dispatch is an additional admission decision, never a substitute for tool approval.
Concurrency is bounded by verified provider/hardware capability and host resource coordination, never a permission to share context/grants or race writes.
Fixed approval lifetimes, process cancellation grace, power confirmations, secret blocking, and bounded raw-audio retention are safety controls, not arbitrary user knobs.

### Delivered future ordinary conversation deadline

In Settings > Sessions, refresh **Future ordinary task deadline**, choose 1-60 whole minutes, then Save.
Readback distinguishes saved/default/unavailable state; Reset restores five minutes.
Typed or activated voice uses `get work.active-deadline-minutes`, `status work.active-deadline-minutes`, `set work.active-deadline-minutes to 10`, or `reset work.active-deadline-minutes` ([shared control](../src/Kora.Application/ViewModels/MainViewModel.OrdinaryTurnDeadline.cs)).

The device-local preference is confirmed through original-user host admission, atomic storage, exact readback, required audit and terminal receipt before future dispatch can consume it.
Stale observations, malformed values and unconfirmed writes fail explicitly; an unconfirmed preference cannot silently become the default
([configuration owner](../src/Kora.Application/Configuration/OrdinaryTurnDeadlineConfigurationService.cs), [preference store](../src/Kora.Application/Configuration/LocalOrdinaryTurnDeadlinePreferences.cs)).

Only newly dispatched queued ordinary conversations, including their ordinary instruction/retry routes, and explicit Copilot/Claude ordinary requests capture this preference.
Their admitted subset has no approval/clarification waits: tool-free answers and already-admitted read-only observation tools.
Specialized source workflows and clarification turns keep their existing fixed limits; no output-text inference or speculative pause/resume clock is added ([dispatch owner](../src/Kora.Application/Hosting/SessionConversationService.Execution.cs)).

Pending work has no active budget. At durable dispatch the host captures one immutable preference and starts monotonic elapsed accounting; pending expiry and the original context timestamps are not renewed.
A later saved default never changes a running turn. The shorter fixed provider/tool maximum and original context expiry always win.
The current local/read-only cap is 15 seconds, and ordinary hosted adapters are capped at two minutes, so choosing five or sixty minutes does not extend those caps ([budget ownership](../src/Kora.Application/Dependencies/ModelTurnHost.OrdinaryDeadline.cs)).

Expiry requests runtime cancellation and suppresses late success; the existing conversation owner retains the session/resource lease and waits for physical quiescence before terminal retention.
Already-published provisional answers also become noncurrent at the captured monotonic deadline, even before a delayed timer callback or physical provider join.
A cancellation-ignoring or unconfirmed provider cannot free the lease or publish success.
No socket-close or physical provider-stop claim is added, and provider qualification remains independently gated
([host execution](../src/Kora.Application/Dependencies/ModelTurnHost.cs), [deterministic regressions](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.OrdinaryDeadline.cs)).

## 5. Providers, Local Models, and Connections

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Local model execution | On; device-local; disabling cancels active local inference while deterministic built-ins remain available | "Disable local models" |
| Hosted model execution | Off until explicitly enabled; still requires at least one independently qualified configured service and signed-in account | "Enable hosted models" |
| Processing mode | Selected explicitly in setup; local-only or remote-enabled | "Use local-only processing" |
| Desired app provider service | Local if supported Ollama installation is observed; otherwise Unset. Explicit Local/Copilot/Claude/Codex selection is retained, without authentication or fallback | `set providers.app.default-service to Claude` |
| App profile model | Exact current service/account model; production catalogue unavailable | `set providers.app.model to <id> for <account> at <revision>`, then confirm the complete tuple |
| App profile reasoning | Exact saved-model option or advertised provider-managed value | `set providers.app.reasoning to <id> for <account> at <revision>`, then confirm |
| App profile context | Exact saved-model budget/preset or advertised provider-managed value | `set providers.app.context to <id> for <account> at <revision>`, then confirm |
| Local model | Verified installed compatible model | "Use the smaller installed model" |
| Ollama endpoint | Validated local endpoint by default | "Use my existing Ollama endpoint" |
| Account/connector selection | Explicit supported signed-in identity | "Use my work account for Teams detection" |
| Connector enablement | Only registered/validated connector | "Disable the GitHub connector" |

The canonical [hosted service/model/compute profile](Model_Providers_Memory_And_Knowledge.md#hosted-service-model-and-compute-profile)
defines catalogue freshness, app-level per-service tuples, field-level session
inheritance, one-turn choices and immutable turn admission. Settings shows
model, reasoning and context controls only from the selected service's current
admitted catalogue. Unsupported controls remain visibly provider-managed or
unavailable; Kora never fabricates a generic value or silently clamps one.

The proposed session integration and Providers settings page use one shared revisioned
draft/save workflow. They show **App default**, **This session**, and the
resulting effective profile separately. Voice uses the same catalogue and
workflow. An unscoped directive such as "Use Codex with low reasoning" defaults
to the addressed session; Kora commits that override before admitting any
remaining action in the same command. "For this request only" selects one-turn
scope, while "make this my app default" explicitly selects application scope.
Other examples include "Use the app context size for this session" and "What
model is this session using?" A session change does not alter app defaults,
and an app-default change does not rewrite explicit session overrides.

Changes increasing remote exposure require deliberate exact voice or UI confirmation in the unlocked session, with any explicitly selected speaker protection and mandatory OS/provider checks.
That confirms the setting only; each new outgoing payload still follows exact egress approval and the selected speaker/privacy and mandatory OS/provider requirements.
Remote endpoints require a validated destination and setup/network decision, not guessing or silent fallback.
Provider changes do not transplant active task context or continue a task under another account automatically.
Credentials/tokens/passwords are not dictatable option values; voice starts supported sign-in/sign-out/configuration flows.

### Delivered app-default provider profiles

The [bounded canonical provider increment](Model_Providers_Memory_And_Knowledge.md#delivered-bounded-app-provider-profile-configuration) is app-scope only. Native Providers, typed and activated commands share original-user audited atomic save/readback/confirmation. `providers.default-mode` remains LocalOnly by default/reset and is not changed by desired-service selection.

`providers.app.default-service` supports explicit Unset/Local/Copilot/Claude/Codex and installation-based reset. `providers.app.profile` saves complete exact model/reasoning/context IDs for the chosen service, bound to one opaque host account and catalogue revision; reset removes that service's tuple only. All other service tuples survive switching/reset.

Native and exact `providers.app.model`, `.reasoning` and `.context` edits review one field while retaining the saved complete tuple's other exact IDs. Separate host-issued confirmation uses the existing audited atomic writer. Compatible model edits retain reasoning/context; missing, stale or incompatible bases require complete tuple draft/save. No defaults, partial persistence or field-only resets occur. Individual edits leave the app default and session overrides unchanged.

No available model is invented when a service catalogue is unavailable. Desired service can be saved unresolved; saved and effective states remain separate. Unknown/stale/expired/cross-service/account-changed tuples fail closed, without nearby values or local/hosted fallback.
Sign-in is visibly Unavailable without a qualified adapter; no credentials, auth/acquisition/network/generation calls or egress authority are added.

Controls expose exact advertised provider-managed choices and context presets/token metadata, not generic effort levels or budget overrides. Bounds and failure recovery are owned by the canonical provider topic and [workflow tests](../tests/Kora.Application.UnitTests/Configuration/ProviderProfileConfigurationServiceTests.cs).

Only subsequent eligible provider-neutral turns consume these app profiles. Existing admitted turns and buffered bootstrap local reasoning are not rerouted. Durable exact-session controls below are separate; generic unscoped directives and one-turn changes remain future work and are never silently promoted to app scope.

Saving a Claude tuple or approving its buffered operation does not enable native streaming. The separate native-only text-stream action additionally requires positive exact tuple/token-ceiling deployment evidence and a current credential/workspace lease. Unknown evidence and the shipped unavailable source fail closed; no setting supplies that authority ([profile contract](Runtime_Provider_Feasibility.md#native-text-stream-implementation-not-enablement), [user operation](../docs/claude-requests.md#separately-qualified-native-text-streaming)).

### Delivered exact-session sparse provider overrides

[Session provider policy](Model_Providers_Memory_And_Knowledge.md#session-overrides-and-effective-resolution) durably stores service/model/reasoning/context as Inherit app or exact current catalogue-bound values in the private consolidated session SQLite authority. Existing/legacy sessions start all-inherit, never a fabricated hosted choice.

Native selected-session status/review/confirmation and exact `providers.session.* for <session-id>` typed/current-name activated commands share original-user admission, session/generation/policy/app/catalogue/account fences. Individual reset clears only one field; explicit **Use app defaults for this session** clears all four. Session writes never change app preferences, and later app edits change inherited values on subsequent turns without rewriting saved session policy.

Saved desired and effective resolved/unavailable state are distinct. Model-dependent controls remain unavailable with the honest production catalogue; desired service selection authenticates/executes nothing. Requested/terminal typed audit, atomic policy/save/readback/control receipt and durable unresolved-outcome holds precede confirmation; malformed/unknown state requires inspection/repair, not inheritance, retry/reset, fallback or rollback claims.

Single-field model/reasoning/context edits use the current whole effective tuple; only that sparse field becomes explicit. Native options belong to its effective model; exact short setters require session/option/account/catalogue IDs and separate confirmation. Other fields retain null inheritance or explicit originals. Incompatible/missing tuples or dependent rebinding require reset/complete-profile recovery; no defaults or app writes.

Session retention/disposition inventory includes the policy row and committed journal. The actual provider-neutral turn consumer captures immutable effective tuple/catalogue/session-policy revision, preserving admitted turns across later edits; pre-dispatch private authority/account/catalogue/installation loss refuses. Buffered local bootstrap reasoning, LocalOnly/handoff/envelope gates, authentication/providers, generic multi-action and one-turn directives remain untouched/unavailable.

#### Exact sparse copy between sessions

In the native provider region, explicitly choose a source from admitted exact session metadata; the selected **active** session is the distinct destination. **Review exact sparse provider copy** discloses both immutable session IDs/generations/policy revisions, complete destination old/source-proposed sparse values, account/catalogue provenance and effective/resolved-or-unavailable status. **Confirm** is a separate one-shot action, expiring after five minutes (or earlier catalogue expiry).

Exact typed/current-name activated syntax is `copy providers.session.profile from <source-session-id> to <destination-session-id>`, followed by the displayed host-issued `confirm providers.session.profile <confirmation-id>`. Neither route infers a destination from a label, selected app profile or source authority. They use the same Application review/confirmation action and existing destination writer.

Copy transfers only `SessionProviderOverrides`: null remains **Inherit app**, explicit fields and their original binding remain exact. It never freezes the source's effective tuple, rewrites app defaults, copies credentials/grants/approvals or session/request/task identities, authenticates, enables a provider or sends a request. All-inherit and unresolved service-only policies remain copyable and visibly unavailable; unavailable/cross-service/account/catalogue-bound dependent choices are rejected, never rebound to matching IDs/labels.

Confirmation separately rereads both policies/lifecycles and current app/account/catalogue/installation/private-control snapshots. Source and destination revisions/generations must still match; changing and restoring values does not restore an old revision. Editing selections/fields, cancelling, closing, privacy/control changes or expiry invalidate review. At each destination transaction boundary the private SQLite writer rereads the exact source on its owned connection; a changed source stops the operation. No source row, source control intent or source authority is mutated. An identical-value copy still uses the normal audited destination save/revision/terminal receipt, not a second no-op writer.

Unknown audit/save/readback outcomes retain the existing destination-only durable hold and explicit inspect-receipts recovery. No automatic retry, rollback claim or source-to-destination execution authority is introduced. Deterministic copy tests cover configuration, not runtime/provider/egress E/Q enablement.

## 6. Definitions and Local Data

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Definition sources and kinds | Explicit registered bounded personal files/roots; target supports declarative editing, current R21 grants local read only | "Use skills and prompts from my Copilot profile" |
| Definition activation/default source binding | Reviewed digest/device-local binding; new/changed content is not automatically activated | "Use the Kora-specific deployment skill when I say deployment" |
| Instruction applicability | Explicit reviewed session/task scope and supported matching rules; no ambient policy injection | "Apply this instruction to this session" |
| Source refresh preference | Common bounded reload; watchers advisory and freshness checked before use; no restart requirement in qualified target | "Reload my definitions"; "Refresh profile definitions every hour" |
| Knowledge source registration | None by default; explicit reviewed file/folder and session/managed scope | "Add my Team TSGs folder as a knowledge source" |
| Knowledge source refresh | Manual initially; later bounded scheduled refresh only after R26 background-work gates | "Refresh Team TSGs" |
| Knowledge retrieval strategy | Lexical initially; verified hybrid/vector retrieval optional later | "Use lexical retrieval for Team TSGs" |
| Knowledge source limits | Host defaults within verified file/source/context maxima; user may lower them | "Limit knowledge files to one megabyte" |
| Knowledge citation detail | Source plus heading/page/line location | "Show detailed knowledge citations" |
| Diagnostic database retention | Delivered canonical integer 1–365; default/reset 30; only future ordinary SQLite commits, existing deadlines unchanged; apply-now unavailable; daily JSON stays 30 files/30 days | `set logging.sqlite-diagnostic-retention-days to 14` |
| Audit retention | Delivered canonical integer 30–365; default/reset 90; only NEW required authority audit and independently qualified diagnostic audit projections; old deadlines unchanged; apply-now/pruning unavailable | `set logging.audit-retention-days to 180` |
| Diagnostic verbosity | Content-minimising normal; bounded metadata-only detail | "Use detailed diagnostics for this session" |

Paths can be spoken or taken from explicitly selected clipboard text, then resolved/read back and validated.
Do not require typing a path, but do not infer one from unrelated context.
Long, ambiguous or low-confidence knowledge paths fall back to the native
picker rather than being guessed. Adding a knowledge source, using its excerpts
with a hosted model and deleting Kora's derived copies are separate decisions.
The complete source, retrieval, settings and voice contract is
[File and Folder Ingestion](File_And_Folder_Ingestion.md).
The Roaming AppData definition stores and protected installation/source layout
are architectural boundaries, not voice-selectable arbitrary write roots.
Supported profile originals have normal UI/voice editing through the dedicated
writer and exact-change confirmation, not a separate edit-enable preference.
Source registration/removal, save/delete, activation, and reload are distinct
operations; reload cannot authorise changed definitions.
See [Definition Management](Definition_Management.md) for all four kinds,
profile formats, external-change conflicts, and channel parity.
Permitted conversation history uses durable standard SQLite under verified private profile permissions and session retention. Copies outside that boundary are readable; database encryption is not required. Raw audio/secret persistence, silent remote diagnostic upload, automatic executable imports and secret logging remain unsupported.
Diagnostic, audit and session retention are independent. Session deletion does
not remove content-minimising audit records, while audit expiry does not remove
perpetual grants. Browsing/search/reasoning never refreshes either diagnostic or
audit retention. The delivered audit option is future-only; apply-now and
existing-deadline reduction are unavailable. Any future affected-record
preview/apply-now flow remains proposed and requires separate approval.
Removing source
enablement explains any immediate destructive/invalidation effect before
confirmation.

## 7. Startup and Updates

### Delivered reviewed Windows logon startup

**Settings > Appearance > Start Kora at Windows user logon** and exact original typed/current-name activated-voice commands share one [reviewed operation](../src/Kora.Application/Configuration/LogonStartupConfigurationService.cs).
Use **Inspect**, **Review enable**, **Review disable**, or **Review reset to Off**, then separately **Confirm startup change** or **Cancel startup review**. Review alone never writes.

The exact phrases are `check start at logon`, `enable start at logon`, `disable start at logon`, `reset start at logon`, `confirm start at logon change`, and `cancel start at logon review`, optionally prefixed with the current assistant name.
Reserved malformed input clarifies without model/approval fallthrough ([grammar](../src/Kora.Application/Configuration/LogonStartupCommand.cs), [routing and native lifetime](../src/Kora.Application/ViewModels/MainViewModel.LogonStartup.cs)).

Review expires after 60 seconds. Changed input, original channel, native visibility/binding, ownership, privacy, call policy, saved intent, installed image or registration requires a fresh review.
Voice confirmation uses a separate freshly admitted capture; native confirmation cannot relabel a voice review. Pending questions and approvals remain unchanged ([workflow](../src/Kora.Application/Configuration/LogonStartupConfigurationService.cs), [host binding](../src/Kora.Application/ViewModels/MainViewModel.LogonStartup.cs)).

The [Windows seam](../src/Kora.Windows/Startup/WindowsLogonStartupRegistration.cs) controls the existing MSI-owned HKCU `Kora` Run value only for the exact running current-user MSI installation.
Developer, older installations lacking install-location metadata, machine-wide, foreign, Windows-disabled and unknown state remain unavailable. Existing unowned entries and Windows approval are never overwritten. Inspect distinguishes last confirmed saved/desired intent, observed registration and unavailable/unconfirmed state.

Save and reset use a [device-local atomic preference with a durable unconfirmed marker](../src/Kora.Application/Configuration/LocalLogonStartupPreferences.cs), requested/terminal audit, committed host intent and exact readback. Reset removes the owned registration and saved intent (default Off), not another installation's entry.
A failed/uncertain write is not rolled back or retried automatically; inspect saved intent, registration and audit before manual recovery ([configuration owner](../src/Kora.Application/Configuration/LogonStartupConfigurationService.cs)).

Registration launches nothing now and grants no audio/provider/network authority. Next logon runs the ordinary entry point and tray/minimal shell with unchanged instance, storage, ongoing-consent, locked-session and microphone gates; startup presentation customization is not added.
See the [entry point](../src/Kora/Program.cs) and [microphone matrix](Security_Data_Flows.md#microphone-consent-and-enablement-matrix). Deterministic fake-platform/native tests are implementation evidence, not installed upgrade/logon or protected-deployment qualification ([delivery policy](Implementation_Roadmap.md#delivery-and-qualification-dependency-policy)).

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Start at logon | Explicit opt-in | "Enable start at logon" |
| Startup presentation | Tray/minimal shell; details optional | "Open the details view when you start" |
| Automatic update checking | Notify-only; enabled only for configured/permitted maintenance networking | "Only check for updates when I ask" |
| Update-check interval | 6 hours; 30 minutes-24 hours with backoff/jitter | "Check for updates twice a day" |
| Release channel | Stable; only trusted published channels | "Use the preview release channel" |
| Update reminder preference | Per-release reminder/suppression through proactive policy | "Don't remind me about this version" |

During the unsigned phase, update installation is external to Kora; no install/stage/download approval or "automatic install" setting exists.
Changing channels does not authorise a downgrade, skip verification, or rewrite the protected feed/trust configuration.
Logon startup uses saved ongoing consent and fresh gates, not a pre-logon
listener or a waiver of locked-session policy. Normal unlock restores only prior
enabled intent after fresh microphone-matrix gates; resume and other failure
recovery still require explicit Enable listening.

## 8. Permissions and Approvals

<a id="proposed-session-permission-levels"></a>

### Session permission levels

[Session Permission Levels](Session_Permission_Levels.md) owns the three levels, separate from Once/Session/Always grant duration and provider mode. The compact/workspace selector names the session and warns that Approve all runs eligible ungranted actions without ordinary review, saves no grants, and cannot bypass independent consent or mandatory boundaries.

New sessions default Review all. Persist levels in Active-session policy across restart; Done resets Review all and resume restores no old level. Changes require revision-safe audited user confirmation, never model self-approval. Show spoken-approval eligibility separately, default disabled during calls, while grants and the level remain effective.

Where action approval and disclosure consent cover the same exact operation, offer one host-owned card with distinct typed authority/audit records and explicit scope. Microphone consent and OS/provider authentication remain separate. Exact-session policy/native controls and call-channel migration are implemented; production Model review has no qualified reviewer and requires user approval. Broader execution routes retain their own gates.

### Grant inventory and existing implementation

Permissions and approvals are host-owned security records, not ordinary preference values.
The current bootstrap provides once/session/always preferences for named
model-suggested built-in actions and host-validated grant-change proposals.
These preferences are not executable/script grants and cannot authorize Lock.
Direct typed/activated-voice/native Lock and model-proposed Lock now share the
[exact package admission owner](../src/Kora/BundledFixedActionController.cs).
Intent or source inspection is not approval: expect fresh exact review unless
an applicable exact grant covers the operation. Retained named Lock preferences
are neither imported nor upgraded; changed resources, implementation or identity
cannot reuse old authority. Once/Session/Always retain the same exact-domain rules.
The mandatory script/native-review boundary applies at every session level;
the original voice channel remains bound even when native controls answer it.
Execution stays unavailable without a qualified contained runtime, with no
native-only fallback. See [execution availability](../docs/skill-and-task-execution-design.md#script-preserving-fixed-action-bridge).
Broader controls and finer-grained observations below remain proposed.
The native Permissions & Approvals page distinguishes single-use, session, and perpetual grants, including applicable, inapplicable, content-revoked, consumed, session-ended, and explicitly removed states.
Show stable ID, capability/action, canonical resource, identity, destination, scope/bound session, creator channel, creation/edit history, last use/use count, policy revision, and reason for inapplicability/revocation/removal.
Perpetual grants have no expiry/retention/eviction; see [Grant Types and Inheritance](Security_Data_Flows.md#grant-types-and-inheritance).
It never needs to display raw sensitive payloads; show content/parameter hashes and safe labels.

Deterministic voice commands may open or filter that page, explain why an action is currently allowed, and request revocation.
Explicit voice or UI confirmation completes removal, scope narrowing, or bulk removal against the exact displayed/read-back scope.
Broadening scope, changing identity/destination, choosing session/perpetual scope, or replacing consumed/ended/revoked/removed access requires newly reviewed action-specific approval through either channel and any mandatory OS/provider checks.
No approvable action category is categorically denied session/always duration; future execution grants bind exact implementation/dependency digests, invocation, and resources, unlike today's named model-action preferences.
Extending beyond a host maximum, granting unspecified effects, or approving a prohibited action is rejected, not overridable by approval.
Revocation blocks subsequent exact-grant consumption. In-flight work is reported as cancelled, completed, or uncertain only when actual evidence supports it; revocation itself never proves physical cancellation or rollback.

### Explain one current pending clipboard read

On the live native **clipboard read** permission card, choose **Why is this allowed / why does it need approval?**.
The [host authorization owner](../src/Kora.Application/Interaction/HostAuthorizationService.Explanation.cs) checks the original pending question, exact built-in descriptor, session generation/level and mandatory gates, using the same exact-grant matcher as normal admission.
The [native presentation](../src/Kora/NativeQuestionViewModel.cs) shows only the safe `clipboard.read` / current plain-text clipboard to volatile native preview label, observed session level, and applicable exact grant scope or independent native permission requirement.
Review all, Model review and Approve all do not replace this path's exact clipboard-read permission; no reviewer/model is consulted.

This is a **read snapshot**, not current effect authority or permission for later execution.
It never reads clipboard contents, creates/reserves/consumes a grant, dispatches, revokes content, completes the separate exact-record review, changes the question or renews session activity ([observation transition](../src/Kora.Application/Interaction/HostAuthorizationService.Explanation.cs), [storage transaction](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.cs)).
Choose **Return to same approval** to recheck the same proposal, question, session policy, gates and exact grant revision; changed evidence invalidates the explanation rather than following another operation.
Then deliberately review/approve or cancel through the unchanged native path. Unknown/stale identity, implementation/dependency binding, storage/audit, private ownership or source state cannot establish an explanation ([native owner](../src/Kora/NativeQuestionHost.Clipboard.cs), [policy regressions](../tests/Kora.Application.UnitTests/Interaction/PendingOperationExplanationTests.cs)).

Original typed/activated-voice clipboard requests still reach the same native permission card.
Explanation adds no spoken disclosure/approval, provider/tool exposure, grant-history/global search, automatic revocation or new clipboard/model consent.
Deterministic [complete-route tests](../tests/Kora.Windows.IntegrationTests/ClipboardExplanationPermissionTests.cs) and [withdrawal/race tests](../tests/Kora.Windows.IntegrationTests/PendingOperationExplanationTests.cs) use fake clipboard/model seams, not real clipboard/network/native effects; they are implementation evidence, not E/Q qualification.

### Delivered native exact operation grants — 2026-10-10

**Settings > Approvals > Permission grants...** opens the shared management window, also reachable from **Settings > Network > Permission grants...**. It reads exact operation records from the initialized private interaction store, not the legacy tab's named-action list. The same typed inventory includes existing device-local URI permission rules without changing their matching, persistence, audit or consumer-specific consent. URI rows cannot join exact revocation batches. Refresh and Next return at most **50 retained exact records / 64 KiB**, with finite stored-row limits and a continuation bound to that store lifetime and unchanged committed snapshot; URI rules are independently bounded to 64 patterns / 16 KiB and are not affected by exact filters/Next. Source, row or cursor failures are explicit; no empty-success fallback occurs.

**Group permissions by** offers **None** (default), **Artefact type**, or **Artefact scope** (Built-in/Kora/User, not Once/Session/Perpetual). This is a cached projection of the displayed page/rules, not a global scan or whole-group selection. Regrouping retires selection, history and confirmations without writes or reads. New host-issued exact records retain validated optional target metadata; absent legacy/unclassifiable metadata and URI artefact ownership remain Unknown. Missing metadata stays omitted from serialization and does not change legacy authority equality. URI Add/Remove/Clear controls now live in this shared window and retain their original host admission; **Clear all URI rules** never clears exact grants.

Select one exact approval ID, then **Inspect selected exact ID** to reread current immutable metadata. It shows the actual revision, capability/source/skill, binding/content SHA-256 identities, recorded effect classification, scope, original session/generation, creator channel/time, status, count, last use and retained revocation reason. Raw parameters, scripts, bodies, credentials and provider content are not displayed or logged.

**Read-only exact inventory filters.** Settings defaults to active Perpetual grants and device URI rules. Session navigation adds only that exact session's Session grants. Explicit **Show retained history** includes terminal records and, only for the selected session, Once records. Native scope/status, canonical ActionId and original-session filters can narrow but never widen that management boundary. Status remains Active outside history; **Clear filters** preserves the current view/history mode. Query digests authenticate the management session/history predicates alongside the existing filters, byte bound and ordinal approval-ID order.

ActionId matches exactly, ordinally and case-sensitively: 1–128 lowercase ASCII letters, digits, dots or hyphens. Origin requires a nonempty canonical lowercase **D GUID** (`xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`). No trimming, fuzzy names, source/skill inference, numeric enum input or invalid-to-unfiltered fallback is allowed. Invalid values require correction and explicit Refresh.

Each request advances at most **50 stored candidate rows**, validates at most **51 candidates including deferred lookahead**, and returns at most **50 matches / 64 KiB serialized record metadata**. Counts distinguish validated candidates from advanced predicate omissions; the advanced-through ID and Next/end are disclosed. A matching record deferred by the byte bound is not omitted or skipped.

An empty matching page can have **Next**: search later storage pages, not just displayed records. The signed cursor binds the typed filter digest, private store lifetime, original committed audit sequence/hash and advanced-through ID. Its predecessor is independently reread/validated. Any audit-head change invalidates Next; Refresh starts a new snapshot, never silently resets it.

Every scanned row is validated before filtering, including duplicates, oversized/unknown rows, partition identity and committed provenance. Filtering does not hide corruption. Origin is recorded correlation only, never old-session authority: independently retained Perpetual records remain filterable after origin retirement. Stored Active remains unrelated to applicability or effect success.

Changing any filter immediately clears results, selection, history, single/batch previews and confirmations, and cancels/suppresses late old queries. Refresh, fresh exact inspection and separate review/confirmation are still required. Filters cannot select a batch, mutate grants/use counts/history membership, create approvals, renew activity or alter scope, consent or retention.

This is native read-only navigation, not an execution descriptor. Model/voice/typed filter mutation, resource/identity/destination/source/skill-name filters, applicability queries, copy/export/network/speech and forensic observations remain unavailable. These filters confer no action-level permission or execution authority.

Active is a stored status, not a current allow decision. Current applicability and already-running work status remain explicitly **unknown**. Missing or removed scoped records are unavailable; independent Perpetual records remain inspectable and revocable after their originating session is retired, without inventing authority for that old session. Perpetual records have no expiry, retention or eviction.

For one active record, review the displayed ID/revision, tick its explicit confirmation and press **Confirm: revoke this exact inspected ID/revision**. A fresh original native-user request and current host-owned control session are committed. The shared store serializes preview/current revision, use, revocation and lifecycle checks with requested/terminal typed audit and the retained revision increment. Changed selection, ownership/privacy/control lifetime or stale/foreign metadata fails closed.

Success means the exact revocation committed and was read back, denying subsequent consumes through the same authorization policy. It does not stop or roll back an existing effect. Lost commit/readback/receipt certainty requires explicit recovery and current inspection, not automatic retry or a false rollback claim. Close/privacy/ownership changes retire private output and pending callbacks.

**Selected exact batch continuation — 2026-10-11.** Use Ctrl/Shift to select explicit stable IDs in the admitted current page, then **Preview exact selected batch**. The complete immutable preview enumerates every ID/revision/scope/action/status and binding/provenance; it is limited to **50 selections / 64 KiB of complete serialized batch**, including its preview identity. Oversized selections are refused, never truncated.

Separately tick **I reviewed EVERY displayed exact batch ID** and press **Confirm: revoke entire reviewed exact batch**. A fresh original native control intent/session supplies current authority, not the grants' old sessions. Every selected record must remain exactly unchanged and Active; one duplicate, foreign, missing, consumed, revoked or changed target rejects the whole batch. Selection order/page, window/privacy/ownership/control changes retire review and late output.

All revocations and content-free requested/terminal audit plus exact before/after revision references commit under one shared-store lease/transaction and admission fence. No loop of single commits or partial-success receipt is used. Subsequent consumes deny every committed target; running work is not cancelled or rolled back. Unrelated grants, named preferences, scope, reasons and use history remain unchanged except each selected grant's canonical retained revocation revision/reason.

**Passive exact-ID history continuation — 2026-10-11.** After inspecting an actual retained record, choose **Read history for this exact grant ID**. The separate read-only list shows retained committed `security_audit_events` envelopes with typed grant-ID membership, including exact grant changes and single/batch requested revision references. It does not use top-level approval filters, names, paths, digests or diagnostic mirrors as membership.

Each page is at most **50 records / 64 KiB of complete serialized output** and scans at most **4096 prefix rows** within a **5-second request**. The signed continuation binds the exact ID, current native viewer, private store lifetime, sequence/digest ceiling and predecessor to one **15-minute** snapshot. Zero-match pages may still have more prefix to scan; source/row replacement, malformed/stale cursors, missing predecessors and corruption fail closed.

Rows disclose original source/schema/sequence, commit SHA-256, request/session/task/invocation and trace provenance, time/deadline, recorded action/outcome, explicit committed event initiator and exact requested versus committed revision/digest references. The committed initiator is distinct from request origin; separately retained Requested-review initiators and reasons remain undisclosed. Unrelated batch change references are omitted with explicit counts. Requested review is not a committed change, and a committed consume is not proof an action executed. Unlinked earlier reviews/failed attempts are unavailable; metadata counts never manufacture missing historical use rows.

The finite observed snapshot distinguishes expired-but-present retained audit deadlines, zero exact matches, scan bounds and unavailable current records. It is not exhaustive, forensic, tamperproof or proof of physical removal. Gaps/retention and current applicability/execution remain unknown. Independent Perpetual origins may be removed; the current native viewer does not borrow the original session's authority.

Native committed-history outcome, exact canonical reason and Any/defined initiator filters compose with AND on the committed event only. They never use request origin or separately retained Requested review details. Core owns exact labels, typed initiator choices and the complete cursor-query digest; unknown/numeric/noncanonical inputs fail explicitly. Edits perform no read and retire rows, continuation and actionable selection/reviews, preserving only the inert explicitly inspected target for fresh explicit Read. See the [history filter controls](../docs/settings.md#exact-operation-grants); no persisted setting, schema/index/cache, egress or authority is added.

Optional exact retained-envelope state (Any/Present/ExpiredButPresent) and inclusive Committed/Due UTC From/Until ranges now compose with those committed-event predicates. Strict canonical UTC text, zero-offset typed bounds and independent range ordering are owned by Core; every field binds the continuation digest. Only complete validated envelopes are filtered, never separately retained Requested review details. These are volatile passive metadata controls, not retention policy or grant/renew/revoke/target/approval/execution authority. The same no-read edits, inert exact-target preservation, retirement and admission ceilings apply.

Optional exact committed audit correlation ID and explicit approval metadata Any/Present/Absent/Exact now AND-compose with all those predicates. Core owns strict nonempty lowercase D GUID admission, Exact-only required approval ID and digest identity. Exact matches only the committed event's approval ID; Absent is validated lack of an ID, not unknown. These IDs are correlation metadata, never authority identity, grant/renew/revoke/retarget, approval or execution authorization. Requested-review details remain undisclosed. See the [native controls and input rules](../docs/settings.md#exact-operation-grants); no new settings persistence or authority route exists.

Optional exact committed `HostRequest.SessionId`, `RequestId` and `TaskId` AND-compose with every existing history predicate.
Empty text means Any; nonempty input uses the same strict lowercase nonempty D GUID parser, without normalization.
Each field participates independently in the versioned authenticated query digest ([query contract](../src/Kora.Core/Authorization/ExactGrantHistoryQuery.cs)).

These fields match only retained committed `ExactGrantHistoryEntry.Request` metadata, never the viewer, original grant proposal/origin, separately retained Requested review or unrelated changes.
Complete envelope, prefix and provenance validation still precedes exclusion. Missing or malformed committed request metadata fails closed, not as an empty match ([reader](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.ExactGrantHistory.cs)).

The [dedicated native history fields](../src/Kora/ExactGrantsWindow.axaml) are volatile passive correlation controls, not session selection, authority, approval, grant, revoke, renew, retarget, replay or execution.
Editing/clearing retires rows/detail/continuation and actionable reviews immediately without reading; explicit Read validates input.
Existing cancellation, privacy, ownership, source and selection fences and scan/page/time limits remain unchanged ([filter lifecycle](../src/Kora.Application/Interaction/ExactGrantsViewModel.HistoryFilters.cs)).

Optional inclusive **Other committed changes Min/Max** and **Other requested grant references Min/Max** AND-compose with every outcome/reason/initiator/retention/Committed/Due/audit-correlation/approval/request-session/request/task predicate ([query contract](../src/Kora.Core/Authorization/ExactGrantHistoryQuery.cs)).
Empty means Any. Nonempty ASCII unsigned decimal input uses the shared parser and must fit the entry's nonnegative Int32 domain (0 through 2147483647); signs, whitespace, overflow and Min > Max fail without repair.
There is no invented count cap or new persisted setting. All four bounds bind the versioned full-query digest and authenticated continuation ([entry](../src/Kora.Core/Authorization/ExactGrantHistoryEntry.cs), [decimal parser](../src/Kora.Core/Storage/OptionalUnsignedDecimal.cs)).

Counts describe collateral change/review references on an already validated committed envelope, not collateral identities/details/disclosure, actual execution, completeness, authority or grant/revoke/approve/replay permission.
Complete envelope/prefix/provenance and count validation precede filtering, including the remaining bounded prefix after the serialized result budget fills; continuation stays at the last delivered position ([reader](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.ExactGrantHistory.cs)).
The explicit accessible [native Min/Max fields](../src/Kora/ExactGrantsWindow.axaml) remain volatile. Edit, Clear and Escape retire rows/detail/selection/continuation/review readiness without reading, retaining only the immutable inspected inert target.
Cancellation/privacy/ownership/control/source/staleness fences remain in force; there are no new sources, schema, logging or audit mutations ([filter lifecycle](../src/Kora.Application/Interaction/ExactGrantsViewModel.HistoryFilters.cs), [read lifecycle](../src/Kora.Application/Interaction/ExactGrantsViewModel.History.cs)).

Reading or selecting history never resumes/renews activity, executes/replays work or supplies approval/cancellation controls. History navigation clears actionable revocation review; inspect afresh before a new single/batch confirmation. Selection/source/privacy/ownership/control/close/cancellation changes retire private output and late callbacks.

This subset has no global/provider/account/resource/skill/capability **revoke all** scan, model/tool execution, approval creation, editing/scope broadening, export, generic cross-grant history/voice/typed command or inferred allow/deny explanation. Named preferences are neither migrated nor promoted, and direct lock/power gates remain unchanged. The separately owned action-level permission design and full R05/A0–A4/release qualification remain open.

Required operations:

- List grants by scope and applicable/consumed/session-ended/inapplicable/content-revoked/removed state.
- Inspect one grant and its use history.
- Revoke one grant.
- Revoke all grants for a provider, account, resource, skill/revision, or capability.
- Narrow resources/capabilities or explicitly change the grant scope; never set retention/eviction for perpetual grants.
- Export content-minimising approval/audit metadata after preview.
- Explain which new approval would be required to restore or broaden access.

## Applying, Resetting, and Undoing

Validate configuration and capability dependencies, stage a typed change, and save atomically through the host store.
On failure, retain the prior valid value and report the failed operation.
Dependent changes show the complete proposal; do not install/download software merely to make a setting appear applied.
Revalidate settings changes against a configuration revision so concurrent voice/UI edits cannot overwrite each other silently.

Reset can target one option or a category. "Reset all settings" requires explicit named confirmation and shows affected registrations and existing grants that must be invalidated.
Reset never deletes skills, databases, or credentials as an implicit side effect.
Undo restores the previous compatible preference value, not a side effect such as reinstalling software or resurrecting a consumed approval.
Reset/undo use normal validation, confirmation, and policy checks; restoring a weaker privacy preference is not exempt from confirmation.
If an external registration/action changed, report that a separate compensating action requires approval.
Never treat undo as reinstating expired grants or microphone consent.

### Delivered single registered-preference undo

The host retains **one run-only last confirmed change** from the [closed registered subset](../src/Kora.Core/Configuration/RegisteredSettings/RegisteredSettingRegistry.cs): registered appearance/presence, spoken-summary caps, Kora playback volume and ordinary response mode. It is not a general settings history, durable journal, undo queue or rollback product ([receipt owner](../src/Kora.Application/Configuration/RegisteredSettings/RegisteredSettingUndo.cs)).

**Undo the last settings change** in Settings opens an inert exact current/prior saved-or-default review, including both summary caps and shared device-local scope. **Confirm settings undo** is a separate fresh request; **Cancel settings undo** writes nothing. Reviews expire after 60 seconds. Appearance retains native-only review; ordinary response settings also support exact typed/current-name activated-voice review/confirm/cancel ([native controls](../src/Kora/SettingsWindow.axaml), [shared routes](../src/Kora.Application/ViewModels/MainViewModel.RegisteredSettingUndo.cs)).

Only exact successful setter readback and required audit completion create a receipt. Confirmation revalidates the committed configuration revision, saved/default provenance, current preference and companion fields, compatible prior value, original channel, private host/lifetime and call policy. External drift, unknown writes and stale reviews cannot infer an inverse or overwrite newer state. A successful undo is a **new audited write through the same authoritative setter**, consumes the receipt and creates no redo. Privacy closure, lifecycle disposal and restart discard the receipt; no audit/history is deleted ([owner](../src/Kora.Application/Configuration/RegisteredSettings/RegisteredSettingUndo.cs), [writer integration](../src/Kora.Application/Configuration/RegisteredSettings/AppearanceConfigurationService.Undo.cs), [summary pair](../src/Kora.Application/Configuration/RegisteredSettings/SpeechConfigurationService.Undo.cs)).

Undo changes future eligible presentation/ordinary response preferences only. It never restores consent, listening, grants, providers, devices, security/retention policy or external effects, and never invokes a model or synthesizes/replays a response. An uncertain setter/readback/audit outcome holds undo explicitly; no automatic retry or rollback is claimed. A weaker prior preference still requires the exact fresh review and confirmation ([operation](../src/Kora.Application/Configuration/RegisteredSettings/RegisteredSettingUndo.cs), [deterministic regressions](../tests/Kora.Application.UnitTests/Configuration/RegisteredSettingUndoTests.cs)).

## Unavoidable Boundaries

All supported preference values have verbal operations while Kora can hear the user.
Closed microphone, locked session, missing recogniser, and signed-out OS prevent receiving speech; an explicit physical/visual activation/setup path is necessary.
This is a physical/security constraint, not a reason to omit voice setters from ready capabilities.
OS sign-in/OAuth/credential entry and required OS elevation remain separate secure workflows, not user-option values dictated to the model.
Kora risk determines review and confirmation specificity, not mandatory mouse use. Optional speaker matching and baseline UI input are not reauthentication.
No option can enable self-modification, disable mandatory lock policy, bypass executable isolation/egress/independent consent, or change "Kora" into a hidden always-transcribing microphone. Proposed session levels may satisfy ordinary action authority under their exact host policy; they never bypass these mandatory boundaries.
