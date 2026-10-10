# User Configuration and Verbal Settings

Status: partial. Bounded appearance, installed speech choices, assistant display/PTT
prefix, spoken-summary caps, exact input/output preferences, per-Kora playback
volume, Windows-provider-native rate, device-default response mode, independent in-call feedback and future-only SQLite diagnostic/audit retention are delivered through shared native/
exact typed and activated-voice workflows below. Broader scopes, setup and the
complete verbal preference/model-facing contract remain proposed; native future-only
session archive/deletion preferences are also delivered, subject to protected-call origin gates
and mandatory secure workflows.

Related: [OOTB Phrases](OOTB_Phrases.md), [Environment Setup](Environment_Setup.md), [Call-Aware Speech](Call_Aware_Speech.md), [Security](Security_Data_Flows.md), [Interaction and Sessions](Interaction_And_Sessions.md).

## Configuration Contract

### Delivered bounded fixed local-version queue settings (R10/R13)

Native **Settings > Sessions > Fixed read-only local-version queue** and exact typed/current-name activated input share the [queue configuration service](../src/Kora.Application/Configuration/SessionQueueConfigurationService.cs).
Only device-local `queue.pending-per-session` (integer **1-10**, default/reset **10**), `queue.execution-slots` (integer **1-2**, default/reset **1**) and `queue.pending-lifetime-minutes` (canonical integer **1-120 minutes**, unsaved/default/reset **30**) are registered.
Each option resets independently; resetting all three removes only this override file ([domain validation](../src/Kora.Core/Configuration/SessionQueuePreferences.cs), [atomic preferences](../src/Kora.Application/Configuration/LocalSessionQueuePreferences.cs)).

Use `list queue settings`, `get`/`status <option-id>`, `set <option-id> to <canonical integer>` or `reset <option-id>`; no fuzzy/model setter or whole-profile reset is added ([parser](../src/Kora.Application/Configuration/SessionQueueConfigurationCommand.cs)).
Native Refresh captures the host-held draft revision, call revision, original input and exact visible lifetime; Save/Reset requires that unchanged admitted draft.
Hiding/reopening, concurrent typed edits, stale session/generation/origin or private ownership changes refuse, rather than overwrite a newer value ([native workflow](../src/Kora.Application/ViewModels/MainViewModel.QueueConfiguration.cs)).

Required typed REQUESTED/terminal audit, atomic save/readback, durable original-input control receipt and confirmed readback precede activation.
Protected Active/Suspected/Unknown original-voice writes are denied without UI relabelling, downgrade or delayed application ([configuration service](../src/Kora.Application/Configuration/SessionQueueConfigurationService.cs)).
Schema-2 `session-queue.txt` has four lines: `2`, capacity, slots and pending lifetime in minutes; overrides are canonical integers or exact `default`.
Known schema-1 three-line files validate first, preserve capacity/slots and supply only the newly introduced lifetime default 30; observation never rewrites them. The next explicit confirmed edit writes schema 2.
The file (256-byte ceiling) and durable `session-queue-unconfirmed.txt` marker use `IApplicationDataPaths` and the shared atomic store.
Unknown schema/shape, noncanonical values, invalid UTF-8, inaccessible or unconfirmed state is unavailable, never a successful default or rollback.
Inspect saved state and audit/control receipts, explicitly repair, then refresh; ordinary refresh cannot clear an unconfirmed marker ([storage tests](../tests/Kora.Application.UnitTests/Configuration/LocalSessionQueuePreferencesTests.cs)).

Confirmed limits feed enqueue/fair dispatch ([queue consumer](../src/Kora.Application/Hosting/SessionQueueService.cs)), native/exact work snapshots ([work consumer](../src/Kora.Application/Hosting/SessionWorkspaceService.cs)) and trusted local events ([event consumer](../src/Kora.Application/Interaction/AuthorityLocalEventSource.cs)).
Short admission/observation transactions serialize with edits and revalidate saved state/revisions at consequential boundaries; the gate is not held through an active read or the dispatch batch.
Lowering pending capacity never evicts or reclassifies existing entries; enqueue holds until capacity is available.
Lowering slots never cancels or reinterprets active admissions; subsequent fair admission uses the current limit ([race/capacity tests](../tests/Kora.Application.UnitTests/Hosting/SessionWorkspaceServiceTests.Queue.cs)).

This is only the existing synchronous read-only `application.get_version` profile.
Manual dispatch, FIFO/fairness, one active task per session and no replay remain unchanged.
Pending lifetime affects **only newly enqueued admitted entries after confirmed activation**. Editing or refreshing preferences never enqueues, dispatches, cancels, expires or extends meaningful activity. Existing pending/cancelled/expired/running entries retain their exact deadlines and identities.
New queue payload format 2 captures `RecordVersion=2` and integer `PendingLifetimeMinutes`; exact `ExpiresAt - EnqueuedAt` must match that captured value under the original enqueue intent/revision and committed change digest.
Legacy payloads without either field still require exactly 30 minutes and retain their original serialized bytes/digests; no database schema or historical authority migration occurs. Unknown/partial/downgraded formats and changed raw payload bytes fail closed ([serialization tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionQueueTests.Lifetime.cs)).
Active budget **5 minutes** and separate user-question expiry remain unchanged/unavailable to edit. Apply-now and the broader 1-50 capacity contract are not delivered.
No worker, automatic dispatch, resource lease, model tool, general execution or real two-slot provider/hardware qualification is added ([bounded workflow](../src/Kora.Application/Configuration/SessionQueueConfigurationService.cs)).

Validation and retained experiment reasons are recorded in the [dated bounded delivery receipt](Implementation_Roadmap.md#r10r13-bounded-fixed-queue-settings---2026-10-09). Full R10/R13/A3, installed native/accessibility and affected provider/hardware acceptance remain open under the [three-tier qualification policy](Acceptance_Criteria.md#three-tier-qualification-policy).

### R18 bounded local event suppression

The [local visual broker](Proactive_Interaction.md#delivered-r18-trusted-local-visual-broker---2026-10-09)
adds no speech/network consent or preference editor. Fixed category budgets
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
never an implicit default. No typed, voice, model or apply-now route is added.

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
tick. Status discloses future-only behavior and unavailable apply-now. Ordinary
diagnostic/audit retention, independent Perpetual grants and other preference
files are unchanged. Host-only audited Perpetual session marking is a separate
exemption, with no new marking UI.

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
[dated evidence receipt](Implementation_Roadmap.md#r12-bounded-session-retention-delivered---2026-10-09).

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
[experiment disposition](Implementation_Roadmap.md#bounded-in-call-feedback-experiment-disposition).

### Delivered bounded local speech text (R25)

Schema-1 **`display.speech-text`** supports exact `Off` and `CurrentUtterance`;
unsaved/default/reset is **Off**. Native **Inspect speech text**, explicit
host-held choice/save and reset share the same service as
`list speech text settings`, `get/status display.speech-text`,
`set display.speech-text to Off|CurrentUtterance`, and
`reset display.speech-text`. The current assistant-name prefix is accepted only
on the exact activated route; no fuzzy/model interpretation is advertised.

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
timing claim or new audio pipeline is introduced. Broader natural caption/viewer
commands and native/accessibility/acoustic qualification remain open.
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

Ten independently persisted options are admitted:
`appearance.theme`, `appearance.presence-display`, `appearance.presence-timeout`,
`appearance.response-timeout`, `appearance.presence-size`,
`appearance.dot-size`, `appearance.dot-density`,
`appearance.movement-speed`, `appearance.speech-scaling` and
`appearance.speech-scale-amount`. The [end-user reference](../docs/settings.md#appearance)
lists their exact types, units, defaults and bounds. Each descriptor declares
device-local scope, appearance-only effect, local-host availability,
immediate-after-save timing and per-option reset. Animation driven by playback
is not a voice-output or call-sensitive option.

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
and presence placement remain direct UX outside the registry.
Other voice/audio/call/manual state, grants, models, retention, dependencies and
startup are not registered. No model tools or broader execution authority
are exposed by the appearance slice.

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
The current assistant-name prefix, 1,024-byte UTF-8/control input bound and
complete 64-KiB result bound apply. Results expose desired/effective percent,
saved/default/unavailable provenance, default/bounds, availability, recovery,
configuration/call revisions, application timing and independent reset.

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
This includes reset/undo, detector/manual-state changes, grant-ignore and temporary/speak-once call overrides; a new UI-originated request is required, not UI confirmation of the rejected voice request.
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
| Learn my voice (`voice.learnFrequentSpeaker`) | Off until separate explained consent; local Windows-profile/device-scoped adaptation from new activated commands only; non-authorizing; status/test/correct/reset/delete workflows | "Learn my voice to improve recognition" |
| Owner-aware private speech | Optional; On for an explicitly enrolled supported verifier, not baseline voice or learning; while enabled, private content is visual-only for `Uncertain`, `NotOwner`, or `Unavailable`, without silently switching protection Off | "Only read private information when you recognise my voice" |

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
| Ignore reusable grants during calls (`calls.ignoreReusableGrants`) | On by default; Boolean device-local setting; require fresh single-use approval instead of Session/Perpetual reuse while call protection applies; Off requires exact protection-downgrade confirmation | "Ignore saved grants while I'm in a call" |
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
The grant-ignore setting is independent of feedback/listening/speech suppression and does not alter stored grants or their retention.
Its evidence, dispatch-race, and approval rules are defined in [Ignoring Reusable Grants During Calls](Call_Aware_Speech.md#ignoring-reusable-grants-during-calls).
When no detector is configured, Kora reports automatic call detection as unavailable and preserves the ordinary response and listening configuration.
Hard lock/mute rules always outrank call/proactive preferences.
Notification settings cannot hide necessary action approval from the visual interface or turn silence into approval.

## 4. Work and Context

The table is the broader proposed contract, not the enabled fixed local-version queue catalogue. Its shipped subset is only [pending 1-10/default-reset 10 and read-only slots 1-2/default-reset 1](#delivered-bounded-fixed-local-version-queue-settings-r10r13); deadline editing and automatic dispatch remain unavailable.

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Pending queue capacity | 10; 1-50 entries | "Set the queue limit to twenty" |
| Successful-completion dispatch | Automatic next ready item; manual mode optional | "Ask before starting each queued task" |
| Pending request lifetime | Delivered fixed local-version subset: canonical integer 1-120 minutes, default/reset 30, future newly enqueued entries only; broader routing remains proposed | `set queue.pending-lifetime-minutes to 60` |
| Active-task deadline | 5 minutes excluding user waits; 1-60 minutes within tool/provider limits | "Give tasks ten minutes by default" |
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

## 5. Providers, Local Models, and Connections

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Local model execution | On; device-local; disabling cancels active local inference while deterministic built-ins remain available | "Disable local models" |
| Hosted model execution | Off until explicitly enabled; still requires a configured supported provider and credentials | "Enable hosted models" |
| Processing mode | Selected explicitly in setup; local-only or remote-enabled | "Use local-only processing" |
| Default runtime/provider | Compatible installed/configured choices | "Use my local model by default" |
| Local model | Verified installed compatible model | "Use the smaller installed model" |
| Ollama endpoint | Validated local endpoint by default | "Use my existing Ollama endpoint" |
| Account/connector selection | Explicit supported signed-in identity | "Use my work account for Teams detection" |
| Connector enablement | Only registered/validated connector | "Disable the GitHub connector" |

Changes increasing remote exposure require deliberate exact voice or UI confirmation in the unlocked session, with any explicitly selected speaker protection and mandatory OS/provider checks.
That confirms the setting only; each new outgoing payload still follows exact egress approval and the selected speaker/privacy and mandatory OS/provider requirements.
Remote endpoints require a validated destination and setup/network decision, not guessing or silent fallback.
Provider changes do not transplant active task context or continue a task under another account automatically.
Credentials/tokens/passwords are not dictatable option values; voice starts supported sign-in/sign-out/configuration flows.

## 6. Skills and Local Data

| Option | Default / limits | Example verbal setter |
|---|---|---|
| Shared skill sources | User-selected bounded read-only roots | "Use skills from my Copilot profile" |
| Skill enablement/default source binding | Reviewed digest/device-local binding | "Use the Kora-specific deployment skill when I say deployment" |
| Source refresh preference | Revalidate before dispatch; optional bounded discovery refresh | "Refresh shared skill discovery every hour" |
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
The Roaming AppData skill store and protected installation/source layout are architectural boundaries, not voice-selectable arbitrary write roots.
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

Permissions and approvals are host-owned security records, not ordinary preference values.
The current bootstrap provides once/session/always preferences for named
model-suggested built-in actions and host-validated grant-change proposals.
This is not an executable/script grant, and exact direct lock does not yet
use the same gate. The broader controls and finer-grained applicability observations below remain proposed;
future side-effecting built-ins and skills share the content/version-bound
execution gate in [skill and task execution design](../docs/skill-and-task-execution-design.md).
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

### Delivered native exact operation grants — 2026-10-10

The tray's separate **Exact operation grants** window reads the initialized private interaction store, not the legacy **Approvals** tab or its Markdown named-action list. Refresh and Next return at most **50 retained records / 64 KiB**, with finite stored-row limits and a continuation bound to that store lifetime and unchanged committed snapshot. Source, row or cursor failures are explicit; no empty-success fallback occurs.

Select one exact approval ID, then **Inspect selected exact ID** to reread current immutable metadata. It shows the actual revision, capability/source/skill, binding/content SHA-256 identities, recorded effect classification, scope, original session/generation, creator channel/time, status, count, last use and retained revocation reason. Raw parameters, scripts, bodies, credentials and provider content are not displayed or logged.

Active is a stored status, not a current allow decision. Current applicability and already-running work status remain explicitly **unknown**. Missing or removed scoped records are unavailable; independent Perpetual records remain inspectable and revocable after their originating session is retired, without inventing authority for that old session. Perpetual records have no expiry, retention or eviction.

For one active record, review the displayed ID/revision, tick its explicit confirmation and press **Confirm: revoke this exact inspected ID/revision**. A fresh original native-user request and current host-owned control session are committed. The shared store serializes preview/current revision, use, revocation and lifecycle checks with requested/terminal typed audit and the retained revision increment. Changed selection, ownership/privacy/control lifetime or stale/foreign metadata fails closed.

Success means the exact revocation committed and was read back, denying subsequent consumes through the same authorization policy. It does not stop or roll back an existing effect. Lost commit/readback/receipt certainty requires explicit recovery and current inspection, not automatic retry or a false rollback claim. Close/privacy/ownership changes retire private output and pending callbacks.

This subset has no model/tool execution, approval creation, editing/scope broadening, bulk revoke, export, detailed use-history browser, generic voice/typed command or inferred allow/deny explanation. Named preferences are neither migrated nor promoted, and direct lock/power gates remain unchanged. The wider inventory operations below are not a full R05/A0–A4 or release qualification claim.

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

## Unavoidable Boundaries

All supported preference values have verbal operations while Kora can hear the user.
Closed microphone, locked session, missing recogniser, and signed-out OS prevent receiving speech; an explicit physical/visual activation/setup path is necessary.
This is a physical/security constraint, not a reason to omit voice setters from ready capabilities.
OS sign-in/OAuth/credential entry and required OS elevation remain separate secure workflows, not user-option values dictated to the model.
Kora risk determines review and confirmation specificity, not mandatory mouse use. Optional speaker matching and baseline UI input are not reauthentication.
No option can enable self-modification, disable mandatory lock policy, bypass executable isolation/egress/action approvals, or change "Kora" into a hidden always-transcribing microphone.
