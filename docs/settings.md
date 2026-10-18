# All settings

Open Settings by:

- right-clicking the tray icon and selecting **Kora Settings**;
- double-clicking the tray icon;
- saying **"Kora, open settings"**; or
- entering **open settings** as a typed command.

The Settings window is single-instance. Its title includes the running
application version as a suffix. Source builds default to version `0.1.0`;
release builds can override that version during publishing. Changes use the
same live application state as the presence and response surfaces.

## Preference Changes and Recovery

Admitted exact/native preference workflows show saved/default/unavailable
provenance, desired/effective values and revisions. Highlighting or opening
a control is not Save. Writes require current original-user/host/session/
privacy/call admission, required audit, atomic persistence/readback and
completed control evidence. Protected/Unknown original-voice mutations need
a new eligible typed/native request; later UI confirmation cannot relabel them.

Stale choices need fresh inspection. Corrupt/inaccessible/unconfirmed saved
state is unavailable, not a default. A write can precede failed terminal
evidence: inspect saved state and receipts, explicitly repair, then refresh.
Do not retry blindly, claim rollback or use ordinary Refresh to clear an
unconfirmed marker. Reset affects only the named option. No preference change
grants consent/approval, replays retired speech or enables a gated capability.
Legacy process-local overrides are labelled separately below.

Appearance controls persist preferences and requested/terminal audits on background workers, with confirmed values published back to the UI. Rapid edits retain only the latest pending absolute value for each option ([appearance workflow](../src/Kora.Application/ViewModels/MainViewModel.Appearance.cs)).

Pending changes recheck their original private-host admission before persistence; handoff refuses during an active write and normal exit awaits it. A queued value is not a saved preference ([commit admission](../src/Kora.Application/Configuration/AppearanceConfigurationService.cs), [exit](../src/Kora.Application/ViewModels/MainViewModel.cs)).

During an owned save, new reader admission waits for confirmation. Existing Evidence content keeps its last confirmed typography with private-content checks still active ([Evidence](../src/Kora/EvidenceWindow.axaml.cs)).

Captions temporarily hide without reviving a retired source. Unavailable or failed preference evidence still retires presentation ([captions](../src/Kora/SpeechCaptionWindowController.cs)).

Window placement, response-window options, muted-output fallback and legacy model-preference writes now await worker-side atomic persistence and required audits. Native changes coalesce by control, so a queued absolute position replaces an older queued position, not an unrelated option ([preference workflow](../src/Kora.Application/ViewModels/MainViewModel.Preferences.cs)).

Model execution and reusable grants remain unavailable during a model-preference commit ([model gates](../src/Kora.Application/ViewModels/MainViewModel.Communication.cs)).

An unconfirmed write holds model admission for the run; inspect saved state and receipts before restarting. No rollback or automatic retry is claimed ([preference admission](../src/Kora.Application/ViewModels/MainViewModel.Preferences.cs)).

Response text and settings remain responsive while bounded audio endpoint discovery runs on a worker ([output discovery](../src/Kora.Application/ViewModels/MainViewModel.VoicePrivacy.cs)).

Playback waits for discovery and rechecks privacy and response identity. Voice-only window reveal waits for the observed output fallback; a late result cannot reveal a newer or locked response ([response workflow](../src/Kora.Application/ViewModels/MainViewModel.cs)).

Grant and power-proposal preparation awaits requested audit persistence before exposing an actionable proposal. Retirement removes UI authority immediately; grant publication and consequential dispatch still await successful terminal audit ([approval workflow](../src/Kora.Application/ViewModels/MainViewModel.cs)).

New input, Hide, cancellation, concurrent revocation or changed admission can retire a preparation or continuation while its receipt is pending. A completed receipt cannot restore a newer revocation ([race tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.ApprovalResponsiveness.cs)).

Voice-consent persistence runs on a worker. Withdrawal invalidates capture and starts device release before waiting for disk; a later explicit withdrawal is serialized after an earlier grant and cannot be overwritten by its late UI continuation ([consent workflow](../src/Kora.Application/ViewModels/MainViewModel.VoicePrivacy.cs)).

## Read-only audit shortening preview

In **Settings > Logging**, explicitly select **Proposed audit retention days for
read-only preview**, then **Preview audit shortening impact only**. The value
must be 30–365 days and shorter than the current confirmed saved/default value.
Selecting or previewing does not Save or change existing records.

The result separately reports required-authority audit and diagnostic-audit
projection status, newly-due count and affected committed/proposed-due UTC
ranges. Newly due means the proposed deadline is at or before the fixed preview
cutoff while the original deadline is still later. Already-due records are not
counted again. A complete zero has no range. Incomplete or Unavailable is not
zero; no truncated count is shown. No event content or identities are displayed.

Edits, Refresh, saved preference/store changes, private/lock/owner/desktop/call
changes, cancellation, failure and closing Settings retire the preview. It also
expires after 30 seconds; explicitly Preview again. There is no automatic scan.
**Apply-now, audit deletion and pruning remain unavailable.** Existing future-only
Save/Reset controls are separate and unchanged.

## Routine notice quiet mode is run-only

In the already-open **Sessions** window, select an exact session and use **Quiet routine notices for this run**. This one choice suppresses first Work/Maintenance notices before fatigue accounting and hides their notice rows. **Turn routine quiet off** and **Reset routine quiet to Off** clear it. Default is Off on every Kora restart; there is no saved setting or scheduled duration.

Status reports On/Off, omitted routine rows and suppression/no-replay reasons separately. Failures, Attention, mandatory safety/recovery output, questions/approvals and authoritative work/queue/status/citations remain unchanged.
Clear admits future new notices only; old muted records can be passive `RoutineSuppressedNoReplay` status, never deferred backlog or speech. Unknown/private/closed/stale/audit/storage state refuses with explicit recovery.

This does not change speech/listening/captions, call/privacy policy, approval authority or work. Quiet-hour schedules, multi-session reminders, proactive speech and broad model/typed quiet settings remain unavailable. Existing [exact event commands](commands.md#trusted-local-events) remain separate; a suppressed routine event cannot be deferred into a replay.

## Per-session retention status and hold

These are **Sessions** controls, not global preferences:

| Control | Scope |
|---|---|
| Refresh retention / Read exact ID retention | Recorded activity/due/kept/purged/hold metadata only; never resumes, renews activity or cleans up |
| Keep this session / Use ordinary retention | Exact ID/generation/date review followed by separate native confirmation; ordinary retention retains original due dates |
| Attach / inspect / replace / remove retained attachment | One immutable UTF-8 text/Markdown file per session, 256 KiB including BOM, sixteen retained profile files, no eviction |

Keep stops automatic archive/delete only, not execution or egress checks.
Unknown/removed/purged state refuses; stale or uncertain commits need fresh
inspection/review. Original files are never modified. Attachment replacement
has a new source identity; uncertified committed copies remain held.
See [Sessions and lifecycle](windows-and-tray.md#minimal-durable-sessions),
[attachment commands](commands.md#attach-one-text-file-to-an-exact-session-native-only)
and [retained metadata](windows-and-tray.md#retained-session-attachments-metadata-only).
No typed/voice/model attachment capture, global keep, apply-now or forced cleanup
is delivered.

## Local speech text

Speech text is **Off** by default. In response settings select **Inspect speech
text**, choose **CurrentUtterance**, then **Save speech text only**. Reset saves
**Off**. The native caption shows selectable exact approved text only during
matching actual response playback, not while synthesis is pending or speech is
suppressed. It does not steal focus when shown.

Exact typed or current-name activated commands are `list speech text settings`,
`get display.speech-text`, `status display.speech-text`,
`set display.speech-text to Off`, `set display.speech-text to CurrentUtterance`,
and `reset display.speech-text`. These only inspect/save the device-local
preference; they never speak, replay or open the microphone.

Captions clear immediately on interruption, replacement, lock,
ownership/privacy/call change or unconfirmed configuration. Corrupt or pending
preferences keep captions off; inspect saved state and audit receipts before
explicit repair. Required approval/error panels remain visible independently.
After **normal successful completion**, already-observed text remains for
**5 seconds** by default and is labelled **PREVIOUS SPEECH**. The caption's
**Pin / Unpin** button keeps that one caption visible after completion. Unpin
uses the original deadline, not a new delay. Pinning never bypasses the immediate
retirement rules above, and is never saved across responses or restart.

In Settings choose **Placement** or **DismissalDelay**, inspect, choose an exact
value, then **Save caption option only**. **Reset selected caption option**
resets only that option. These device-local options do not enable captions:

| ID | Choices / bounds | Default and reset |
|---|---|---|
| `display.speech-text-placement` | `BottomRight`, `BottomLeft`, `TopRight`, `TopLeft` on the chosen working area, 24-DIP margin | `BottomRight`; primary display unless explicitly selected this run |
| `display.speech-text-dismissal-delay` | Canonical integer seconds, 0-30 | 5 |
| `display.speech-text-pin` | `true` / `false`, current observed caption only; run-only | `false` |

Each has exact `get/status <id>`, `set <id> to <value>` and `reset <id>`.
`list speech text settings` discovers the persisted options; exact pin status
shows its run-only scope. Invalid saved options are rejected, not defaulted.
Sentence alignment remains unavailable because playback supplies no admitted
sentence boundaries. Broad natural caption commands and rich browser/HTML/
diagram rendering remain unimplemented.

**Caption display — this run only** is available in **Settings > Speech & audio** and the caption. Choose an available native display and press **Use selected display**; **Return to primary** explicitly resets to the current primary.
Labels show ordinal, primary designation and safe working-area geometry; they are not durable display identities. No monitor choice is saved; restart returns to primary.

Choosing while speech text is Off applies only to the next eligible caption; it does not enable captions or speak/capture. Choosing while eligible text is visible moves only that observed caption, keeping its original completion deadline and pin. The saved corner remains relative to the chosen display's current working area/DPI; tiny areas clip rather than spill.

Display changes expire old choices. If the explicit display disappears or is unknown, the caption immediately hides and retires, even when pinned; it does not move private text to another monitor.
Inspect the display status, then explicitly select a current display or **Return to primary** when the owning unlocked/private/call gates allow it. Retired text never returns. This recovery does not repair caption preferences, clear privacy/ownership/call holds, or replace required response/question/recovery panels.

## Windows-native speech rate

**Settings > Speech & audio** provides a bounded draft, **Refresh Windows rate
preference only**, **Save Windows-native rate only** and **Reset Windows rate to
normal (0)**. The independent `speech.windows-rate` accepts canonical integer
**-10 through 10**, default/reset **0**. These are native Windows engine units,
not percent, a multiplier or universal words per minute. See the
[Microsoft API](https://learn.microsoft.com/en-us/dotnet/api/system.speech.synthesis.speechsynthesizer.rate?view=net-10.0-pp).

Discovery/status explicitly qualifies provider support. Save/reset requires
the installed available **Windows** provider and qualified Kora-owned adapter.
**Kokoro is unsupported and unchanged**; a saved Windows rate is retained but
never reported as a Kokoro speed effect. Unknown state stays unavailable.
[Exact typed/current-name ACTIVATED commands](commands.md#inspect-or-change-windows-native-speech-rate)
use the same admitted host workflow, not model settings tools.

Saving/resetting retires active and queued speech. Only future eligible Windows
synthesis consumes the owned engine rate, after the previous synthesis lifetime
quiesces; changing it never starts or replays speech. Full interrupted responses
and mandatory previews stay visual. No capture/consent, provider/voice/output
selection, asset install/download, global SAPI/mixer/default-device, zero-volume/
protected-call policy or retention/grant/history setting changes.

Original input, host-owned audio session/generation, current name/provider/source/
policy/privacy/topology and visible native-lifetime identity must remain current.
Required requested/terminal audit, atomic save/exact readback and committed intent
precede activation. Corrupt or unconfirmed files cannot silently default on restart.
Inspect saved state and audit/intent receipts before explicit repair and refresh;
refresh does not clear an unconfirmed marker. Reset removes only the rate override.
Deterministic source/native-seam tests do not establish acoustic or installed
acceptance.

## SQLite diagnostic retention

**Settings > Logging** provides Refresh, Save future retention only and Reset to
30 days. The single `logging.sqlite-diagnostic-retention-days` option accepts
an exact integer **1–365**, default/reset **30**. Discovery/status shows saved or
default provenance, desired/effective days, fresh revisions and explicit recovery.
The [exact typed/current-name activated commands](commands.md#sqlite-diagnostic-retention)
call the same host workflow; configuration is not a model tool or effect grant.

Only newly committed **ordinary SQLite** logs, completed spans and their owned
links use the confirmed policy. Existing deadlines never change, including
across restart. **Apply-now and immediate deletion are unavailable.** Saving
or resetting does not run pruning or change cleanup scheduling.

Independently configured audit retention, daily files **30 days/30 files**,
session/chat/history, every approval and every grant are unchanged by this option.
Perpetual grants never expire or undergo time retention/eviction. Future
session/history suggestions are not current configuration options.

Writes require original local input, owning unlocked host/privacy/call/input
revalidation, independent host-resolved diagnostic session/generation, exact
proposal/revisions, required typed audit and atomic durable save/readback.
Closing/replacing the native surface invalidates its callback; no pending exact
question/approval is answered or replaced. Corrupt or unconfirmed preferences
stay unavailable across restart: inspect saved state and audit/intent receipts,
explicitly repair and refresh. No silent default or automatic marker clearing.
Ordinary SQLite delivery reports an explicit gap while independent file and
required audit paths remain separate.

## Future-only audit retention

**Settings > Logging** also provides independent audit Refresh, Save future
audit retention only and Reset to **90**. `logging.audit-retention-days` admits
canonical integers **30–365**, default/reset **90**; typed/current-name
ACTIVATED get/status/set/reset use this same workflow. `list logging settings`
reports both complete admitted options and saved/default/effective provenance.

Only NEW committed required authority audit and independently qualified SQLite
diagnostic audit projections use the confirmed days. Their source identities
remain distinct; neither diagnostic projections nor daily files become
permission authority. Existing audit deadlines, payloads, hashes and citations
stay exactly unchanged across restart and migration. **Apply-now, immediate
deletion and audit pruning are unavailable.** Save/reset never runs cleanup.

REQUESTED and terminal preference receipts use the prior policy; activation
follows required audit, atomic save/exact readback and durable intent outcome.
The separate durable unconfirmed marker prevents activation after lost
evidence or late admission/confirmation failure. Corrupt/unknown/pending data
refuses, never silently falls back to 90. Inspect saved state and audit/intent
receipts before explicit repair and refresh. At startup, unconfirmed audit
policy refuses authority recovery/writes; in-run failure shows held native
status without replay. Native hide/reopen/dispose invalidates old callbacks.

Audit days are not grant lifetime. All grant records and existing
validity/scopes remain; Perpetual records have no expiry/retention/eviction.
Session/chat/history, approvals/tasks/questions, ordinary SQLite
**1–365/default-reset 30**, daily files **30 days/30 files**, resource/call/audio
behavior and cleanup schedules are unchanged. No full retention-cleanup,
forensic/encryption or installed/native/acoustic qualification is claimed.
See the [canonical bounded contract](../Design/User_Configuration.md#delivered-bounded-future-only-audit-retention-r10r04).

## Release maintenance (notify-only)

Open **Settings > Maintenance** or **Tray > Release maintenance (notify-only)**.
This is metadata review/browser navigation, not an installer or updater.

| Choice/control | Behavior |
|---|---|
| Production / Preview | Stable only / explicit published-beta opt-in; no drafts |
| Public metadata checks for this run | Off by default, unsaved; separate network permission |
| Check canonical releases | Initial check, then six hours plus jitter and failure/rate-limit backoff; respects the current deadline |
| Review this exact verified release | Displays version/source/architecture/digest/canonical page and unsigned disclosure |
| Open reviewed canonical release page | Requires fresh exact review; browser navigation is not download/install approval |
| Snooze this version (24h) | Current run and exact fresh Available notice only, never safety/approval prompts |

Failed/expired/changed/private/call-held results retire review and cannot open.
Unavailable/Unknown/RateLimited are not verified current; a prior success stays
historical and becomes stale after six hours. Recovery does not replay checks.
Only bounded metadata/manifest bytes are fetched, not application/source/setup.
Hashes are not publisher signatures; x86 has no installer qualification.
See [cached command/status reference](commands.md#exact-cached-release-maintenance).
Those commands cannot grant network permission or perform Check/Open.

## Models

The **Models** tab controls which model locations Kora may use for free-form
commands:

- **Allow local models** is on by default. When enabled and readiness has
  verified Ollama and the pinned model, unmatched requests can run locally.
  Turning it off cancels an in-flight local request and prevents later
  requests from reaching Ollama.
- **Allow hosted models** is off by default. The current build has no hosted
  provider or credentials, so enabling this permission does not send data or
  provide a cloud fallback. The saved opt-in will gate hosted execution when a
  provider is implemented and configured.

These device-local choices are applied immediately and retained across
restarts. Exact built-in commands remain available when both choices are off.
Use **which models are enabled**, **enable/disable local models**, or
**enable/disable hosted models** as typed or activated voice commands to read
or change the same settings. Model-setting writes are security audited.

The new provider-neutral host controls are an internal, adapter-unavailable
foundation. They do not qualify Ollama or Copilot, change these existing
bootstrap settings/readiness paths, add credentials, or select a cloud
destination. The separate **Settings > Providers** device-local initial-mode preference and exact native handoff review are delivered without qualifying inference.
Durable cross-session recall and grounded model answering remain gated.
Enabling a model location is not permission to upload clipboard/file
previews, persist memory or bypass action approval. There is no automatic
provider fallback or confidence-based handoff.

### App default service and desired profiles

**Settings > Providers > App default service and per-service desired profile** is configuration only. Initially the desired service is **Local if a supported Ollama installation is observed; otherwise Unset**. Unknown installation is Unset. A detected executable is not a running runtime, an available model, consent to generate, or account/egress authorization.

1. **Inspect app provider settings and current catalogue**.
2. Explicitly choose Unset, Local, Copilot, Claude or Codex. Picking a service changes its tuple draft without changing saved settings or erasing other services.
3. **Save desired app service only**, or choose all three exact advertised model/reasoning/context values and **Save selected app service and exact model/reasoning/context tuple**. Provider-managed values must themselves be advertised. Inspect again after a completed save.
4. With a current saved complete tuple, **Review only model** from the model draft, or pick an exact reasoning/context field for the saved model and review that field. Inspect the field, saved/resulting whole tuples, dependent validity, preference revision and catalogue provenance; **Confirm reviewed app field and whole complete tuple** or **Cancel reviewed app field; no write**.

Individual edits retain the saved other fields, app default service and other service tuples. A model edit never replaces incompatible reasoning/context IDs: use the complete draft/save above to choose all dependents explicitly. Missing/stale complete bases and changed selection/ownership/privacy/call/session/generation/app/account/catalogue state reject confirmation. No individual app field reset exists; selected-service tuple reset remains unresolved, not a substitute.

Current production catalogues have **no available models** because no qualified capability/auth adapter is composed. Desired service may be saved while its model details remain Unavailable. **Sign in** is disabled/Unavailable, not a fake login or use of machine credentials. Nothing is authenticated, acquired, sent or generated by these controls.

Status separates saved, desired and effective state. Unknown, expired, retired, cross-service, changed-account or changed-catalogue values do not select replacements. **Reset app service** re-observes Local-if-installed/otherwise-Unset and retains all service tuples. **Reset selected service tuple** removes only that tuple and leaves it unresolved.

This is independent of the unchanged **LocalOnly** `providers.default-mode` safety default and existing bootstrap local-model switches. Only subsequent eligible provider-neutral policy turns consume profiles; app edits never reroute/cancel/retry an admitted turn or change the buffered local bootstrap reasoner.

Durable session inheritance/overrides use the separate Sessions controls below. Generic unscoped “Use Claude” and one-turn directives remain deferred; existing [app-scoped commands](commands.md#inspect-or-change-app-provider-profiles) never alias session changes. Larger context choices cannot override Kora's limits. Failed/unconfirmed writes require inspecting saved state and audit/control receipts before explicit repair and refresh, not automatic retry.

### Exact session provider overrides

In **Sessions**, select an existing exact session, expand **Selected-session provider overrides**, then **Refresh exact session provider status**. Status separately shows saved sparse values, inherited flags/app values, effective resolved/unavailable tuple and current catalogue/account provenance.

Service/model/reasoning/context each start as **Inherit app**. Review one exact desired field, then use **Confirm exact selected-session proposal** separately. **Inherit app service/model/reasoning/context** resets just one field; **Use app defaults for this session** reviews clearing all four. Cancel/close saves nothing. Session overrides never write app preferences.

Choose only one model, reasoning or context option in the independent field pickers; the other two choices come from the current effective tuple, not the separate full-profile draft. Review shows old/new whole effective tuples and sparse changes. Only the selected field becomes explicit. Missing/incompatible state requires complete-profile recovery; retained nulls inherit later app edits. Changing a picker invalidates confirmation.

Service can remain desired/unresolved. Model-dependent saves require the current exact advertised catalogue; production has no models, so these save controls stay unavailable. A service override inherits that service's app tuple, not another service's IDs. Stale session/selection/generation/policy/app revision or catalogue/account changes refuse confirmation without substitution.

Later edits affect future admitted provider-neutral turns only. Existing LocalOnly/handoff/envelope boundaries and the buffered local bootstrap reasoner are unchanged; no authentication, network/model call or egress is enabled. [Exact commands and recovery](commands.md#inspect-or-change-an-exact-session-provider-profile) share the same private durable session-policy workflow.

**Review pending exact provider handoff** opens local volatile original-user review of actual audited host-issued offers, never sample/model-supplied envelopes. Production has no qualified offers and reports unavailable. Choose an offer, then **Read complete exact envelope** before explicitly approving context only, declining, cancelling or removing evidence.

Removal requires fresh review; close/expiry/privacy/ownership changes never approve. Complete context, evidence lineage/classification, identities/revisions, reason and destination are inert and bounded. No copy/export, content persistence/logging, speech, grant, provider enablement, network send or execution is added.

## Approvals

Model-suggested actions that need approval offer **Once**, **This session**,
**Always**, and **Reject** on a visible response card. Kora also accepts newly activated push-to-talk
replies after it finishes speaking the approval question. By default, say the
assistant name first: **"Kora, approve once"**, **"Kora, approve for this
session"**, **"Kora, always allow this"**, or **"Kora, reject"**. The
**Require the assistant name before a verbal approval** setting is on by
default; turn it off to accept those replies without the name while an
approval is pending.
The same name-prefix setting applies to verbal answers to a model's
clarification question. A clarification choice is not an approval; the
response card shows numbered options separately from grant/action controls.

Approvals are specific to the named built-in action. The **Approvals** tab
lists active session and always grants separately and lets you revoke either
kind per action. Session grants end when Kora exits, restarts, or locks
Windows through Kora. Always grants are stored in Kora's device-local
Preferences folder and remain until revoked. These grants are action-name-only:
they do not authorize a script, executable, or particular implementation
version. They govern model-suggested disruptive actions, not direct exact
built-in phrases (including lock). Any model-suggested action that requires
approval can use any of the three scopes; the model cannot choose the scope
for you.

## Exact operation grants

Open **Exact operation grants** from the tray menu. This is a separate native window for retained host-issued exact approval records, not the **Approvals** tab or the Markdown named-action preferences. It does not import or promote those preferences.

Optional native **Stored scope** and **Stored status** filters default to **Any**. Empty **Canonical ActionId** and **Original session GUID** mean any recorded value. **Clear filters** explicitly restores these defaults; Refresh is still required. All specified predicates must match; they search successive bounded storage pages, not merely the displayed page.

ActionId is exact ordinal/case-sensitive lowercase ASCII (letters/digits/dots/hyphens, 1–128 characters). Origin is an exact nonempty canonical lowercase D GUID. Do not add spaces, uppercase, shortened GUIDs or names: invalid values are explicitly refused, not normalized or replaced with an unfiltered read. Source and skill labels are not authority or inferred ActionIds.

The inventory summary shows matches, validated candidates, advanced predicate omissions, advanced-through ID and Next/end. Each request advances at most **50 stored rows**, validates at most **51 candidates including deferred lookahead**, and returns **50 matches / 64 KiB serialized record metadata** at most. Byte-deferred matches are not skipped; empty pages can have Next.

Next is signed for the exact filter digest, current private store lifetime, original committed audit sequence/hash and predecessor. Any audit-head change or invalid/foreign cursor requires explicit Refresh, never a silent reset. Restart needs a fresh query. Malformed/oversized/duplicate rows and invalid provenance fail even when excluded by your filters.

Changing any filter retires current results/selection/history and single/batch previews/confirmations immediately, cancelling and suppressing late old queries. Fresh inspection and separate explicit confirmation remain mandatory. Original-session filtering is correlation only; independent Perpetual records remain searchable after origin removal. Stored Active does not mean allowed or executed.

1. **Refresh inventory** reads a bounded page (at most **50 records / 64 KiB**) from the current private store. **Next bounded page** continues only that unchanged store snapshot; if it changes, refresh instead.
2. Select the exact approval ID and choose **Inspect selected exact ID**. Review the current ID/revision, capability/source/skill, scope/original session/generation, creator/time/status, use count/last use/reason and SHA-256 binding identities. No raw parameters, scripts, credentials or provider bodies are shown.
3. For an Active record only, tick **I reviewed the exact displayed ID and revision** and press **Confirm: revoke this exact inspected ID/revision**. Only that unchanged exact record is revoked, audited and read back. Selection/close/privacy/ownership/control changes retire the confirmation; stale or already terminal records require fresh inspection.

For an explicit batch, select exact IDs from the current page with Ctrl/Shift, then choose **Preview exact selected batch**. Review EVERY enumerated ID/revision/scope/action/status and its safe binding/provenance. The complete serialized preview, including its identity, must fit **50 records / 64 KiB**; excess is refused, not truncated.

After inspecting one actual retained record, **Read history for this exact grant ID** opens a separate read-only list. **Next bounded history page** scans the same finite committed snapshot, including zero-match pages. Limits are **50 envelopes / 64 KiB complete output**, **4096 scanned prefix rows / 5 seconds per request**, and **15 minutes** for the signed exact-ID/viewer/store continuation. Source, record, cursor or predecessor failures are explicit; refresh and inspect, never assume empty success.

History has its own **Committed event outcome** filter: **Any**, Requested, Succeeded, Failed, Denied, Cancelled or Unknown. **Committed event initiator** offers **Any**, System, LocalUser, TypedCommand, VoiceCommand or ModelSuggestion. Empty **Exact ReasonCode** means any; otherwise it must be an exact case-sensitive canonical lowercase ASCII audit identifier (letters/digits/dots/hyphens, 1-128 characters). Unknown enums/labels, numeric enum input, whitespace/case repairs and overlong reasons are refused, never normalized or treated as Any. All three predicates are AND and match only the authoritative committed `SecurityAuditEvent`; they do not search formatted rows, request origin or separately retained Requested review details. A Requested review paired with a Denied terminal event matches Denied, not Requested. Every loaded entry and its provenance/prefix are validated before exclusion.

Editing or clearing history filters performs no read. It immediately cancels old work and retires rows, Next, actionable inventory selection and single/batch review. Only the previously explicitly inspected inert exact target is retained; choose **Read history for this exact grant ID** again. **Clear history filters** restores Any/empty without reading. The summary distinguishes scanned prefix rows and nonmatching scanned rows; zero matches can still have Next. Continuation authenticates the outcome/reason/initiator digest, exact inspected grant digest/store identity, complete native viewer, read-admission identity/control revision, finite ceiling/digest and predecessor. Filter values stay volatile, never entering logs, preferences, database rows, activity tags, audit payloads, model/tool/speech context, clipboard or export. No review, approval or activity lifetime is renewed.

Only authoritative retained audit entries with exact typed grant membership are listed, including each atomic batch member's requested and committed revision/digest references. Rows show safe IDs, source/schema/sequence, time/deadline, action/outcome, explicit committed event initiator and original provenance, not scripts, parameters or provider/user bodies. The committed initiator is separate from request origin; separately retained Requested-review initiators and reasons are not displayed. Unrelated batch references are omitted with counts. Original sessions are provenance, not your current read authority.

**Retained envelope state** adds **Any**, **Present** or **ExpiredButPresent**; missing/unknown/unavailable segments are not complete committed envelopes. Optional **Committed from/until UTC** and **Due from/until UTC** bounds are inclusive and AND-composed with every other history predicate. Empty bounds mean Any. Nonempty bounds must be exactly `yyyy-MM-ddTHH:mm:ss.fffffff+00:00` (for example `2026-10-16T00:00:00.0000000+00:00`), with From <= Until independently for each range. `Z`, alternate offsets/precision, whitespace and repaired input are refused. Times come from the complete validated committed envelope, never Requested review or current grant creation time. Retention describes the retained deadline at the page's observation time, not deletion or current applicability. All five fields participate in the authenticated continuation query digest and the same passive edit/clear/cancellation/privacy rules above; they grant no grant, renewal, revocation, target, approval or execution authority.

**Exact committed audit correlation ID** is optional; empty means Any. **Committed approval metadata** explicitly selects **Any**, **Present**, **Absent** or **Exact**. Exact requires **Exact committed approval ID**; that field must be empty for all other choices. Both IDs require a nonempty canonical lowercase hyphenated D GUID, for example `12345678-1234-1234-1234-123456789abc`. Zero GUIDs, alternate formats, uppercase, whitespace and repaired values are refused. These predicates AND-compose with outcome/reason/initiator/retention/time on complete validated committed envelopes, and every choice/ID binds the continuation digest. Exact approval matches only the committed event carrying that exact ID, not requested grant references or separately retained Requested review. Absent means a validated committed event with no approval ID, not unknown/unavailable metadata. The same passive edit/clear/retirement/cancellation/privacy rules apply. Audit correlation and approval IDs are committed correlation metadata only: they cannot identify authority by themselves, approve, grant, renew, revoke, retarget or authorize execution.

Requested review is not a committed grant change, and committed use is not proof an action executed. Stored creation/last-use/count metadata does not create missing rows. Unlinked reviews/failed attempts, exhaustive history, current applicability and execution remain unavailable or unknown. Expired-but-present is a retained deadline, not physical removal; the observed snapshot is not forensic or tamperproof evidence.

**Exact committed request session ID**, **Exact committed request ID** and **Exact committed request task ID** are optional independent history fields. Empty means Any.
Each nonempty value must be a nonzero canonical lowercase D GUID (exactly 36 characters); uppercase, whitespace, alternate forms, malformed and overlong values are refused without repair ([authoritative parser](../src/Kora.Core/Authorization/ExactGrantHistoryQuery.cs)).

All three AND-compose with outcome/reason/initiator/retention/time/audit-correlation/approval filters and bind the complete continuation digest.
They match only retained committed envelope `HostRequest` metadata, not your current viewer, the original grant proposal/origin, separately retained Requested review or other changes.
Validation precedes exclusion; unavailable request metadata is not guessed ([committed reader](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.ExactGrantHistory.cs)).

Typing or **Clear history filters** performs no read and immediately retires old rows/detail/Next and single/batch review.
Clear restores all three to empty/Any; choose explicit **Read history for this exact grant ID** to validate and read again. Errors use the operation status, without echoing entered IDs.
These are passive historical correlation, never session selection or approve/grant/revoke/renew/retarget/replay/execute authority ([history lifecycle](../src/Kora.Application/Interaction/ExactGrantsViewModel.HistoryFilters.cs)).

**Other committed changes Min/Max** and **Other requested grant references Min/Max** are independent inclusive count ranges. Empty means Any; all four bounds AND-compose with every existing history predicate and bind the versioned authenticated full-query digest ([query](../src/Kora.Core/Authorization/ExactGrantHistoryQuery.cs)).
Enter ASCII nonnegative integers from 0 through 2147483647, with Min <= Max per range. Negative/sign/whitespace/decimal/separator/undefined/overflow input fails explicitly without repair; the Int32 entry domain, not a new arbitrary cap, owns the upper limit ([entry](../src/Kora.Core/Authorization/ExactGrantHistoryEntry.cs), [shared parser](../src/Kora.Core/Storage/OptionalUnsignedDecimal.cs)).
These are counts of collateral change/review references on the complete validated committed envelope, not their identities/details/disclosure, execution, completeness, authority or grant/revoke/approve/replay permission ([disclosure](../src/Kora.Core/Authorization/ExactGrantHistoryPage.cs)).

Typing, **Clear history filters** or **Escape** retires rows/detail/selection/Next and review readiness without reading. Clear/Escape restore empty/Any, retaining only the inert inspected target; explicit Read is required.
Complete envelope/audit-prefix/provenance/count validation runs before exclusion, including beyond a full result budget within the same bounded prefix. Deferred matching rows are not skipped by continuation ([reader](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.ExactGrantHistory.cs)).
Existing cancellation/privacy/owner/control/source/stale fences remain; no persisted setting, new schema/source reads/logging or audit mutation is added ([filter lifecycle](../src/Kora.Application/Interaction/ExactGrantsViewModel.HistoryFilters.cs)).

Reading/selecting history does not execute, replay, export, copy, speak, approve or cancel anything, or renew session activity. It clears single/batch revocation review; **Inspect selected exact ID** again before a new explicit confirmation. Closing/privacy/ownership/control/selection changes clear held rows and suppress late output.

Tick the separate **I reviewed EVERY displayed exact batch ID** confirmation and press **Confirm: revoke entire reviewed exact batch**. All unchanged Active targets and requested/terminal audit commit atomically, or NONE are revoked by the batch. A stale, missing, foreign, terminal or changed target requires fresh inventory/review. Reordering selection, changing pages or current control, privacy/ownership loss and closing the window invalidate the batch and late output.

Stored Active is **not** proof that an operation is currently allowed. Current applicability and already-running work are **unknown** in this window. Revocation denies subsequent exact-grant consumes; it does **not** stop or roll back an already-running effect. Missing/inapplicable data is not guessed. Failure or lost receipt certainty requires closing/reopening and fresh current inspection before any retry; no rollback is promised.

Perpetual records have no expiry/retention/eviction and remain inspectable/revocable independently after origin-session retirement, using a fresh CURRENT native control session. Removed scoped records are unavailable. No global **revoke all** scan, creation/edit/broadening, revival, export, current allow-decision explanation, tool/model invocation or generic voice/typed management is available. Direct lock/power and named-action gates are unchanged.

Filters are read-only native controls, not new tool execution descriptors or model/voice/typed commands. Resource/identity/destination/source/skill-name and current-applicability filters remain unavailable. Reads cannot mutate use counts or raw rows, change history membership, select/revive approvals, copy/export/speak/send content, renew session activity or trigger retention cleanup.

## Readiness and required tools

The setup queue checks local storage, SQLite, PowerShell 7 (`pwsh.exe`), and
Ollama/model inference on startup and refresh even when local model execution
is disabled, so readiness remains visible before it is re-enabled. A missing or unhealthy PowerShell
runtime shows a PowerShell setup task. The Readiness page displays the detected
PowerShell and Ollama/model status without requiring an approval prompt; review
buttons are shown only when the corresponding dependency needs action.
**Review PowerShell 7 setup** explains
the per-user winget installation and asks for explicit consent; installation
and a no-profile health check run only after approval. **Review local model
setup** separately asks permission to install Ollama and download the pinned
qwen3:1.7b model; Kora checks its digest and actual inference before using
it. Setup shows an indeterminate progress bar while Winget and the runtime are
starting, then a percentage bar with readable MB/GB transfer totals while the
model downloads. After transfer, the same area explicitly reports local
finalization, digest verification, inference testing and readiness refresh.
A newly installed package gets a bounded grace period to start its own runtime;
transient startup timeouts are retried before Kora starts a fallback owned
server. Terminal success or failure replaces the current phase in the same
status area. Missing local
inference opens this tab on startup. PowerShell readiness
does not gate local reasoning or the current C# built-ins. Neither setup button
grants permission to run an arbitrary script.
The optional Kokoro speech provider is offered separately and is not a
required queued setup task.

## Appearance

The existing controls and exact typed/activated-voice commands share one
host-owned, typed appearance service. No model or network is needed. Enter
**list appearance settings** to inspect the eleven admitted options, units,
defaults, bounds, scope and application timing.

| ID | Default | Values / units |
|---|---|---|
| `appearance.theme` | `system` | `system`, `light`, `dark` |
| `appearance.presence-display` | `true` | `true`, `false` |
| `appearance.presence-timeout` | `10` | 1-60 seconds |
| `appearance.response-timeout` | `5` | 1-60 seconds |
| `appearance.presence-size` | `360` | 240-600 pixels |
| `appearance.dot-size` | `100` | 50-200 percent |
| `appearance.dot-density` | `100` | 25-200 percent |
| `appearance.movement-speed` | `100` | 25-200 percent |
| `appearance.speech-scaling` | `true` | `true`, `false` (visual sizing only) |
| `appearance.speech-scale-amount` | `100` | 0-200 percent |
| `appearance.reader-text-scale` | `100` | 75-200 integer percent; native source/detail and complete serialized Evidence bodies only ([domain](../src/Kora.Core/Configuration/ReaderTextScaleSettings.cs)) |

Use **get appearance.theme**, **set appearance.theme to dark**, or
**reset appearance.theme**; substitute another listed ID and its typed value.
Voice input uses the currently configured assistant name, for example
**"Kora, set appearance.presence-timeout to 15"**.
The listed spoken names replace dots and hyphens with spaces:
**"Kora, set appearance theme to dark"** or
**"Kora, set appearance presence timeout to 15"**.
Integer values are numeric
and use the listed units; relative changes, number words and appended units
are not interpreted. Ambiguous, unknown and out-of-range appearance commands
show clarification instead of reaching a model or silently clamping.

The **Show presence** control and `appearance.presence-display` setting make
the animated presence optional. It is displayed by default. Turning it off
keeps the presence hidden across restarts while response, settings, approval
and other interactive surfaces remain available.

The Appearance tab also offers **Reset selected option**. Reset affects only
that option, not a whole profile or undo history. Updates revalidate the
typed value and current host revision before one atomic preference-file write.
A stale proposal requires a fresh inspection/proposal. A save failure retains
the previous saved/effective value and revision and shows the error.
Get distinguishes a saved preference from an unsaved domain default.
Successful changes update all open visual surfaces without reopening them.
Speech scaling changes only animation, not audio output or call policy.

**Native reader text scale** in Settings > Appearance applies an absolute integer percentage immediately; select its exact ID for **Reset selected option** (100). Exact commands include `get/status appearance.reader-text-scale`, `set appearance.reader-text-scale to 75` (or 100/200), and `reset appearance.reader-text-scale`. Relative changes and number words are not inferred ([registry](../src/Kora.Core/Configuration/AppearanceOptionRegistry.cs), [parser](../src/Kora.Application/Configuration/AppearanceCommand.cs), [editor](../src/Kora/SettingsWindow.axaml)).

It scales only shared native DetailWindow Rendered/Source/continuous text, file/folder/retained attachment source bodies and exact cited/literal-match spans, the native clipboard snapshot source body, admitted complete serialized Evidence page bodies and their inert focused literal-Find spans, and the currently observed native speech-caption content text. New windows and live confirmed changes use original typography, never cumulative scaling. Trusted chrome, search inputs/result navigation, identity/location labels, consent/approval/question/error controls and caption labels, Pin/Unpin and display selector/status retain their own typography. Strings, bytes, digests and offsets are unchanged; the preference grants no content access/copy/export or persistence consent ([detail](../src/Kora/DetailWindow.axaml.cs), [file](../src/Kora/LocalFilePreviewWindow.cs), [span](../src/Kora/LocalFilePreviewWindow.Citations.cs), [clipboard](../src/Kora/ClipboardPreviewWindow.cs), [Evidence](../src/Kora/EvidenceWindow.axaml.cs), [caption](../src/Kora/SpeechCaptionWindow.axaml.cs)).

Unlike the older appearance options, reader-scale writes retain an unconfirmed marker until exact readback and the success audit complete. Invalid or unconfirmed state blocks new scaled readers with an explicit error; inspect saved evidence and audit receipts before repair, never blindly retry. The prior confirmed UI value is not evidence of successful persistence ([workflow](../src/Kora.Application/Configuration/AppearanceConfigurationService.cs), [store](../src/Kora.Application/Configuration/LocalReaderTextScalePreferences.cs)).

Open Evidence readers and captions retire explicitly on unavailable preference evidence; source/private/call/window closure, replacement, interruption and failure/cancellation clear stale scaled content without reading or replaying again. Caption scaling changes neither caption enablement nor speech, exact playback identity, pin lifetime, native geometry/display choice or accessible labels. This is not global text scaling: inline guide/grant previews, compact responses/conversations, Evidence filters/query fields/navigation/provenance/status and incomplete pages, search/catalogues, caption chrome and browser/HTML/diagram/image/PDF readers are excluded. Full accessibility, hardware and Windows DPI acceptance remain open; see the [bounded contract](../Design/Information_Display.md#bounded-native-reader-text-scale).

Response pinning, topmost behavior and window placement retain their existing
direct controls but are not registered: their shared-file writes are outside
this bounded registry. Voice/audio/call, model, grants, retention, setup and
startup settings are not admitted here. Full verbal preferences and
model-facing settings tools remain future work.

### Application theme

- **System** - default; follows live Windows light or dark appearance.
- **Light** - always use Kora's light palette, with darker secondary text for
  descriptions and inactive navigation labels.
- **Dark** - always use Kora's dark palette.

The theme applies immediately to Settings, the presence, response surface,
documentation, and other Kora-owned visual surfaces.

### Independent visibility timeouts

- **Presence timeout** - default **10 seconds**; range **1 to 60 seconds**,
  under **Appearance > Presence appearance**.
- **Response timeout** - default **5 seconds**; range **1 to 60 seconds**,
  under **Appearance > Visual feedback**.

Presence hides automatically and without asking when it is idle or listening
and no prompt needs attention. New Kora interaction restarts its timer.
Active work, speech, approval/question prompts, recovery actions, grant editing,
and unacknowledged failures prevent presence auto-hide. Once these finish,
a fresh inactivity period starts. Listening/capture is not required for the
timer to run, and hiding presence does not stop listening or work.

The response window uses its own timer. **Always show** disables only response
auto-hide; it does not pin presence. Question/approval prompts and response
action links keep the response visible until resolved or dismissed.
Changing one timeout does not change the other. Previously saved response
timeout values are retained when upgrading.

### Presence appearance

Under **Appearance > Presence appearance**:

| Setting | Range | Default |
|---|---|---|
| Presence size | 240-600 px | 360 px |
| Dot size | 50-200% | 100% |
| Dot density | 25-200% (38-300 dots) | 100% (150 dots) |
| Movement speed | 25-200% | 100% |
| Speech sizing | On/Off | On |
| Speech scale amount | 0-200%; retained while sizing is off | 100% |

Speech scale 100% spans 90-112% of normal size; 200% spans 80-124%; zero
holds normal size. It follows actual output, not microphone input, and does
not change state, volume or playback policy. For movement, Ctrl-drag,
click-through and saved-position behavior, see [Presence](windows-and-tray.md#koras-presence).

### Response window

Under **Appearance > Visual feedback**:

| Control | Default / behavior |
|---|---|
| Always show | Off; retains the response until Dismiss |
| Stay on top | On |
| Response timeout | 1-60 seconds, independent of Presence timeout |

Position is saved on this device. Enter runs a typed command; Esc/Cancel task
stops eligible active work, while Dismiss hides only.
See [response window behavior](responses-and-calls.md#visual-response-window).

## Speech and audio

### Assistant display / PTT command-prefix name

- Default: **Kora**
- Length: 1 to 3 words and no more than 32 UTF-16 characters after whitespace
  trimming/collapse (the existing name rules, not session-name NFC rules)
- Allowed: letters, numbers, spaces, apostrophes, and hyphens

**Apply name** and **Reset name to Kora** use the same typed, audited atomic
workflow as [exact assistant settings commands](commands.md#assistant-display--ptt-command-prefix-setting).
Discovery/get show schema 1, `assistant.name` (spoken: **assistant name**),
string type, bounds/default, device-local scope, effect/timing, process-local
revision and saved-versus-unsaved-default provenance. Reset affects this option
only; a missing file remains an unsaved domain default until a changed choice
is explicitly saved.

The name updates window titles, tray labels, command/help prefixes, visual
responses, spoken identity and voice preview. The next explicitly activated
PTT uses the new grammar; the old prefix is not an alias, including session
and artifact commands. A mutation retires capture and queued transcript/
completion generations and leaves listening held. Use **Enable listening**
explicitly before a new PTT; rename/reset never opens the microphone or clears
an existing run hold. Failed capture shutdown or persistence retains the prior
name and reports failure. A later grammar-start failure keeps the saved name
but capture unavailable; there is no old-grammar fallback.

Invalid/corrupt/unknown saved state is visible and disables prefix routing and
capture instead of silently using Kora. Native recovery and exact unprefixed
get/set/reset remain available. Repair storage access, explicitly set/reset or
refresh; an audit-completion failure after replacement requires inspection and
explicit recovery, not a reported successful mutation.
Protected/unknown call state rejects original voice-channel set/reset even
when later delivered through a button; initiate a new eligible local request.

This is presentation/routing, **not an authority identity or a qualified
production wake name**. Reset does not change session/task/grant/approval/
instance IDs or stored session names. The executable remains `Kora.exe`;
namespaces and application data paths remain fixed. No wake detector, custom
profile, learning/enrollment, assets, download or OS/global setting is added.

### Microphone

| Choice/control | Behavior |
|---|---|
| System | Follow the live Windows multimedia default |
| Exact endpoint | Pinned device-local override; a missing pin is not replaced by name |
| Choose microphone (native recovery) / Refresh | Bounded metadata only, including unavailable saved pins and Allowed/Blocked/Unknown Windows access |
| Save preference only | Save the exact highlighted endpoint; no capture |
| Enable listening (PTT readiness only) | Separate fresh-gated action over the saved endpoint, not the highlight |

The exact option is `speech.input-device`; reset selects System without
granting consent/permission or clearing a run hold.
See [input commands](commands.md#exact-input-device-preference) and
[microphone recovery](voice-and-audio.md#microphone-selection).
Windows access needs both Microphone access and desktop-app access; open
Windows microphone settings separately, then refresh.

### Voice consent and activation

| Control/state | Behavior |
|---|---|
| Ongoing voice consent | Required before voice enablement; saved consent can arm PTT at ordinary startup without capture |
| Push to talk | Hold mouse/Space/Enter, speak, then release; production wake is unavailable |
| Disable / Enable listening | Current-run release / explicit fresh-gated re-arm; disablement is not saved |
| Withdraw voice consent | Persist closed consent across restart |
| Normal authoritative unlock | Restore only prior enabled readiness after confirmed closure/fresh gates; never resume recording |
| Resume/reconnect, restored permission/device or intervening failure | Explicit Enable required; selection/refresh does not release the hold |

Status explains privacy, permission, call or manual holds.
See [consent and PTT workflow](voice-and-audio.md#voice-consent-and-explicit-push-to-talk)
for the full lifecycle and recovery rules.

### Speech provider

| Provider ID | Preparation |
|---|---|
| `windows-sapi` | Built-in Windows provider; installed SAPI voices, no download |
| `kokoro` | Optional local assets, about 219 MiB; separate reviewed download and exact size/SHA-256 validation |

**Speech voice** has one selector containing ready installed voices from all
providers. Each entry identifies its provider; choosing a voice selects that
provider automatically, without a separate provider selector.
**Speech provider assets (review only)** never changes selection; completing
Download makes choices available immediately but does not select one.
**Remove downloaded model** removes Kokoro assets only.
See [provider workflow](voice-and-audio.md#speech-providers).

### Speech voice

Provider and voice are one atomic device-local choice:
the paired option IDs are `speech.provider` and `speech.voice`.

| Control | Behavior |
|---|---|
| Select `provider / ID` | Exact installed voice; explicitly selects that provider |
| No explicit voice | That provider's advertised compatible default; otherwise choose an exact voice |
| Reset speech voice | Selected provider's advertised default only |
| Preview / Stop | Explicit local identity phrase / cancel active speech |

Provider selection/reset remains available through the exact `speech.provider`
commands; the redundant provider selector and provider-reset button are not
shown in this card.
Reset never downloads or substitutes an unrelated voice. Invalid saved state
or missing assets disables speech with recovery, not a silent provider fallback.
Status reports desired/effective/default/saved/revision; stale/failed saves
need fresh inspection. See [exact speech commands](commands.md#inspect-or-change-an-installed-speech-choice)
and [voice workflow](voice-and-audio.md#speech-voice-selection).
Rate and volume are independent options.

### Kora playback volume

Under **Speech & audio**, select **Kora playback volume (0-100%)** then **Save
Kora volume only**. Highlighting is an unsaved draft. **Reset volume to 100%**
removes only the volume override; **Refresh volume preference only** reads
saved state without testing audio. Default **100** preserves original unscaled
output. **0** prevents synthesis/automatic playback and retains the complete
visual response, warnings and required prompts. Raising/resetting never replays
stopped or queued output.

Exact typed/ACTIVATED commands share these controls: `list volume settings`,
`get/status speech.playback-volume`, `set speech.playback-volume to 30`,
`reset speech.playback-volume`. Use canonical integers 0-100, without signs,
padding, fractions, `%` or leading zeros. The current assistant-name prefix
works. Results show saved/default/unavailable source, desired/effective percent,
bounds/default, revisions and recovery. Protected/unknown calls deny original
voice mutations; use a new eligible typed/Settings request.

Only Kora-owned Windows speech instance gain and Kokoro PCM attenuation change.
No Windows/system/call volume, mute, microphone, consent, provider, output
endpoint, summary, name, approval or retention setting changes. Unknown adapter
capability, invalid saved data, failed atomic save/readback/audit or stale
admission is explicit visual unavailability, never a silent default. A write
may precede failed terminal evidence: inspect/repair saved state and explicitly
refresh. An atomic unconfirmed-write marker keeps ambiguous writes unavailable
across restart until explicit evidence inspection/repair; refresh cannot silently
clear it. No automatic retry, test playback or acoustic audibility claim.

### Spoken summary limits

Under **Speech & audio**, choose **Maximum sentences (1-3)** and **Maximum
words (1-80)**. Defaults are **3 sentences / 80 words**; either cap can be
lowered independently. **Reset sentence cap** restores 3 while preserving
words; **Reset word cap** restores 80 while preserving sentences.

The registry IDs are `speech.summary-sentences` and `speech.summary-words`;
spoken names are **speech summary sentences** and **speech summary words**.
Discovery/get/set/per-option reset use the same native/typed/activated-voice
workflow. For example, **set speech.summary-words to 40**.
Status distinguishes unsaved defaults from saved limits and reports revision
and recovery. The host checks original channel, live privacy/ownership and call
revision adjacent to audited atomic persistence. Protected calls reject
voice-originated changes, including reset. No provider, voice, output routing,
voice consent or call protection is changed.

Ordinary finalized speech is admitted only if the complete spoken text
(title, answer and retained warnings) fits **both** configured caps. The host
does not infer safe omissions from model prose: over-cap results get a truthful
**Speech withheld** status and forced full visual recovery, even in Audible
only mode. No truncation, success paraphrase, extra model call or queued retry
occurs. Full visual text/details remain unchanged. Exact proposal/approval
readback, required questions/options and voice previews are not ordinary
summaries; their existing exact/bounded/privacy rules still apply.
No arbitrary full-content reading command is added.

Counting is deterministic Unicode text counting, not acoustic duration or
linguistic segmentation:

- A word is a run of Unicode letters/digits. Combining marks and ASCII
  apostrophe, right apostrophe, ASCII hyphen, U+2010 and U+2011 may join a run
  (including repeated joiners). Other punctuation/whitespace splits runs.
  Decimal dots, acronym dots, underscores, slashes and `@` split word runs.
  Emoji/symbols alone are not words; unspaced CJK text is one word run.
- A sentence is a segment containing a word, ended by `.`, `!`, `?`, `。`,
  `！`, `？` or `．`. Repeated terminators/closing quotes do not add empty
  sentences. A trailing word-bearing fragment counts as a sentence.
  Line breaks alone do not end sentences.
- ASCII dots between digits, single ASCII-letter initials and these case-insensitive
  abbreviations do not end sentences: `Mr.`, `Mrs.`, `Ms.`, `Dr.`, `Prof.`,
  `Sr.`, `Jr.`, `e.g.`, `i.e.`, `etc.`, `U.S.`, `U.K.`. Abbreviations must
  end at a non-letter/digit boundary. Other abbreviations and punctuation use
  the rules above; no dictionary/model guess is made.

Limits are a versioned atomic device-local preference independent of the
existing provider/voice file. Upgrades with no limit file use unsaved defaults
without rewriting legacy speech choices. Corrupt/unreadable/unknown formats
disable ordinary speech with visible recovery; they are not defaults.
Repair the local preference file and refresh. A per-option mutation cannot
silently default an unknown companion cap. Mandatory readback and visual output
remain governed by their existing rules. Changes retire pending speech before
UI dispatch; reset or call clearance never replays retired output.

Push-to-talk stops current speech before opening command capture.
**Stop speaking** is also available from the tray without speech or a model.

### Audio output

**Refresh output metadata only**, select an exact endpoint, then **Save output
preference only**. **Reset output to System** removes only Kora's override.
System follows the live Windows multimedia default; a saved pin remains pinned.
Draft/highlight changes nothing.

Status shows desired/saved/default/effective/unavailable route, mute and recovery.
Discovery has a five-second deadline; stale/foreign choices require fresh reads.
Changing/resetting retires speech but never plays a trial, replays output,
changes global defaults/volume/mute, opens capture or grants consent.
Missing/muted/failed output retains full visual recovery.
See [output workflow](voice-and-audio.md#audio-output-selection) and
[exact output commands](commands.md#inspect-or-change-an-exact-output-choice).

### Refresh devices and readiness

Re-enumerates microphones, voices, speakers, Windows defaults, mute state, and
dependency readiness. Refresh does not enable listening.

## Responses

### Device default

Persistent `Hybrid` (**both audible and visual**), `VoiceOnly` (**audible only**)
or `VisualOnly` (**visual only**), default `Hybrid`. Choose **Inspect response
mode**, select a presented mode and **Save device-default mode**.
**Reset device default to Hybrid** saves only this default; it does not clear
queue/task overrides or change call/mute fallback, speech, microphone or consent.
Status reports saved/default/unavailable provenance, revision, desired/configured
effective mode and live output policy. The [exact commands](commands.md#inspect-or-change-the-device-default-response-mode)
use the same admitted audited atomic save/readback workflow.
Stale choices and failed storage/evidence require a fresh inspection; corrupt
saved state is unavailable, never silently Hybrid. Save/reset never plays or
replays speech or opens capture. Required full visual response/preview remains
available in VoiceOnly, including interrupted output, warnings and approvals.
Unconfirmed write evidence survives restart; inspect saved state/audit receipts
and explicitly repair it before refreshing. Inspection never silently clears
an unconfirmed marker or reports success after failed apply evidence.

### Current queue

Process-local presentation override, not durable queue authority.
Choose Inherit to remove it. See [output precedence](responses-and-calls.md#output-precedence).

### Current task

Process-local presentation override, not task execution/configuration authority.
Choose Inherit to remove it. See [output precedence](responses-and-calls.md#output-precedence).

### Muted speaker fallback

Default: **on**. This independently stored legacy preference is retained for
compatibility; it cannot suppress mandatory complete visual recovery when the
effective output is missing, software-muted, zero-volume, unavailable or failed.
The configured response mode is unchanged. A fresh eligible route can restore
normal voice-only presentation; no recovery or setting change replays speech.
Pending questions and approvals retain their exact visual preview.

### Effective output

Read-only explanation of the currently resolved mode and any forced visual
fallback.

## Calls

### Call detection

Read-only status keeps manual state and automatic evidence separate. Automatic
detection remains unavailable in this build; no Teams/account/network detector
is implemented.

### Manual call mode

**I'm in a call** enables protection for this run.
**Clear manual call mode** / **Reset manual off** clear only this layer,
never enabled automatic Active/Suspected/Unknown evidence.
**Cached call status** is passive. Manual state is not saved.

Original channel, current host/session/call revisions and required audited
intent remain mandatory. Stale/failed changes retain conservative protection.
Off/reset never replays speech or opens capture.
See [manual call workflow](responses-and-calls.md#manual-call-mode-and-call-settings)
and [exact commands](commands.md#exact-current-run-manual-call-control).

### In-call feedback override

**Refresh call feedback**, choose **Voice / UI / Both / Inherit**, then
**Save call feedback only**. **Reset call feedback to UI** removes this
override; UI is the confirmed missing/default choice. Highlighting is a draft.

Applied only to manual/observed Active or enabled Suspected, ahead of ordinary
output preferences. Inherit restores ordinary selection; Unknown/invalid
evidence still withholds speech. Voice/Both never bypass call suppression,
privacy/lock/capture, mute/zero or mandatory visual previews.

Status shows saved/default/unavailable, desired/effective/applied mode,
revisions and recovery. Protected original-voice writes refuse even after
a later UI click; start a fresh UI request. Changes retire output without replay.
Corrupt/unconfirmed state remains held across restart until explicit repair/
inspection; Refresh cannot clear the marker.
See [output precedence](responses-and-calls.md#output-precedence) and
[exact feedback commands](commands.md#in-call-feedback-override).

### Call speech suppression (independent of feedback)

Default: on. Manual Active and enabled Active/Suspected suppress all automatic
speech, including previews/readbacks, with complete visual output. Unknown/invalid
enabled evidence always withholds speech. Existing saved
choices are retained. New disabling is unavailable pending complete exact
trusted review; enabling protection is supported.
The separate UI feedback default still selects visual output if an existing
saved suppression preference is Off. Choose feedback explicitly; it never
silently edits this suppression preference.

### Voice activation during calls

Default: on. Turning it off closes capture when a call is detected and blocks
re-enabling until protection clears. Clearance never automatically reopens
capture. New re-enabling of this preference is unavailable pending exact review.

During protection, all voice-originated voice and in-call preference mutations
are rejected, including manual clear/reset and ordinary output options masked
by the call override. Later UI confirmation cannot change voice lineage; start
a new UI request. Stop speech, disable listening, cancel and read-only status
remain usable. Protected calls ignore Session/Always reusable model-action
grants without changing storage; new grant-ignore disabling, temporary overrides
and speak-once exceptions are unavailable. This is bounded manual behavior, not
full call/provider/native/audio acceptance.

[The proposed session permission-level design](../Design/Session_Permission_Levels.md#voice-approval-and-protected-calls) will instead keep grants and levels effective during calls and add default-disabled configurable spoken approvals. That migration and selector are not current settings behavior.

## Readiness

Shows each dependency as Ready, Unavailable, or needing attention, with an
explanation. Status labels use readable text such as **Needs configuration**.
**Refresh readiness** repeats device and dependency discovery without opening
the microphone.

Select **Open Windows microphone settings** to manage Kora's Windows microphone
permission without navigating through Windows Settings manually.

## Sessions: fixed read-only local-version queue

**Settings > Sessions > Fixed read-only local-version queue** provides pending
capacity (1-10, default/reset 10), fixed synchronous read-only slots (1-2,
default/reset 1), **future pending lifetime** (1-120 integer minutes,
default/reset 30) and **future-admission active budget** (1-60 integer minutes,
default/reset 5). Highlighting a choice does not save or enqueue work.
Use **Refresh fixed queue settings**, then the option's **Save** or **Reset**.
Each option resets independently; another Refresh is required before a new edit.
The [exact typed/current-name commands](commands.md#fixed-queue-settings) use
the same original-input host admission, revisions and audited confirmation.

The displayed legacy settings-group name also covers the three additional
[fixed first-page profiles](commands.md#deterministic-local-version-queue).
Their captured deadlines and slots follow the same future-only rules;
settings never supply page controls, probes or dispatch authority.

Only newly enqueued admitted fixed reads after confirmed activation capture
the chosen pending lifetime. Existing pending/cancelled/expired/running entries
keep their exact pending deadlines and identities. The chosen active budget
is captured once at future admission, including from old pending entries.
Already-admitted/running/Unknown/terminal records keep their original deadline;
legacy admissions retain five minutes. Separate question/model/provider clocks
do not change. No apply-now/current-task extension,
automatic dispatch, worker/model/provider/effect scheduler or expanded slot
qualification is available. Refresh and preference edits never dispatch,
cancel, expire old work or extend meaningful activity.

Protected original activated-voice mutations remain denied without UI
relabelling. Hidden/reopened settings, stale revisions, changed ownership/privacy,
failed required audit/control receipt or mismatched readback cannot activate
a draft. Unconfirmed writes remain unavailable across restart: inspect saved
state and receipts, explicitly repair, then refresh; no automatic retry or false
rollback. Existing queue authority remains independently inspectable.

Schema-3 `session-queue.txt` contains capacity/slots and both minutes overrides.
Known valid schemas 1/2 preserve prior fields and supply new defaults (active 5;
pending 30 for schema 1); observation never rewrites source bytes. Unknown/malformed/noncanonical,
oversize or invalid UTF-8 data never becomes a default. Legacy queue records
remain exact fixed-30 with original payloads/digests; format-2 entries capture
the configured minutes and exact expiry under committed enqueue authority,
without a database schema or historical authority migration. Explicit format-3
admissions bind active minutes and precise UTC deadline to their original
dispatch audit/digest; actual callbacks enforce the same captured monotonic
budget. Equal/late results suppress output without physical termination claims.

## Where settings are stored

Device-local settings are written under:

`%LOCALAPPDATA%\Kora\Preferences`

They do not roam with documentation or user content. Stored skills are not yet
available.
