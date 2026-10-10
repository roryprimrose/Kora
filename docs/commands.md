# Voice and typed commands

Voice and typed input use the same exact built-in phrases, which always take
precedence. Voice input may use the configured assistant name as a prefix.
Punctuation and capitalization do not matter for built-in phrases. Once the
verified local model is ready, unmatched requests can receive a local answer;
free-form voice requests must start with the active assistant name.
The alternatives below are explicit phrases, not fuzzy matching: additional
words beyond the phrase do not trigger a built-in action.

If the assistant is renamed, replace Kora with the configured name. The old
name is not retained as a hidden alias.

For the full design-defined catalogue, including capabilities not yet shipped,
see [Tools and built-in skills: current and planned](tools-and-built-in-skills.md).
The exact phrases on this page remain the current-release command reference.

Provider-neutral host controls add no new command or qualified model tool loop.
Existing bootstrap reasoning and unavailable messages remain unchanged;
hosted inference is not enabled by this host-only foundation.
Device-local initial provider mode settings are delivered separately below.
Clipboard and file preview/search commands still perform no model submission.

## Inspect or change the device-local provider mode

Native **Settings > Providers** Inspect/Save/Reset and exact typed/current-name activated commands share the [provider-mode workflow](../src/Kora.Application/Configuration/ProviderModeConfigurationService.cs).

| Exact syntax | Result |
|---|---|
| `list provider settings` | Discover the single device-local option, choices, default, revision, timing and recovery |
| `get providers.default-mode` / `status providers.default-mode` | Confirmed saved/default/unavailable provenance and desired initial mode |
| `set providers.default-mode to LocalOnly` | Save the privacy-first initial mode |
| `set providers.default-mode to LocalFirst` | Save local-first semantics for subsequent session initialization |
| `set providers.default-mode to HostedPreferred` | Save hosted preference only; no adapter or egress permission is enabled |
| `reset providers.default-mode` | Explicitly save LocalOnly, preserving existing session policies and other preferences |

Unsaved/default/reset is **LocalOnly**. Only a session's first policy-bound turn consumes this device preference; existing volatile policies and per-turn Default/Local/Hosted choices are unchanged. HostedPreferred remains fail-closed with no composed adapter and no egress authority ([consumer](../src/Kora.Application/Dependencies/ModelTurnHost.Policy.cs), [tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.ProviderPreference.cs)).

Original user intent, current owning unlocked host, active control session/generation, unchanged choice/call/preference revisions, required audit, atomic readback and completed control receipt precede confirmation. Protected/Unknown original voice writes refuse. Corrupt or unconfirmed storage never becomes a default; inspect saved state and receipts, explicitly repair, then refresh ([workflow](../src/Kora.Application/Configuration/ProviderModeConfigurationService.cs), [storage](../src/Kora.Application/Configuration/LocalModelProviderModePreferences.cs)).

## Create and manage reviewed session memories

Native **Sessions > Session memories** and exact typed/current-name activated commands share the [original-user workflow](../src/Kora.Application/Memory/MemoryManagementService.cs). Select an exact active session, choose an allowed classification, enter the exact value, then choose **New session memory from draft**. No existing memory ID is required; Kora issues the identity, lineage and initial revision.

| Exact syntax | Result |
|---|---|
| `memory help` / `list memories` | Discover exact syntax; no inventory or body |
| `memory propose <session-id> <class> "<exact value>"` | Create only a volatile Proposed/Pending user candidate |
| `memory list <session-id>` | Content-free identity/revision/scope/review/retention/creation metadata |
| `memory inspect/get <session-id> <memory-id> <revision>` | Explicit exact body, classification and lineage inspection |
| `memory review <session-id> <memory-id> <revision> accept|reject` | Review the same inspected revision; accept does not admit |
| `memory admit <session-id> <memory-id> <revision>` | Separately admit only the exact Reviewed/Pending revision |
| `memory edit/set <session-id> <memory-id> <revision> <class> "<exact value>"` | Replace, clear review and redact earlier durable body; new inspect/review/admit required |
| `memory disable/forget <session-id> <memory-id> <revision>` | Disable use, or forget content while retaining a non-reusable tombstone |

Use canonical nonempty D GUIDs and displayed positive revisions. Classes are `ExplicitFact`, `ResponsePreference`, `WorkflowPreference`, or `Decision`; classification is not secrecy detection. Values are bounded to 512 UTF-16 code units, 1,024 strict UTF-8 bytes and 2,048 serialized candidate bytes. Double a quote inside the final quoted value. No normalization, truncation or eviction occurs.

After creation, list/select the metadata row, **Inspect exact memory**, explicitly accept/reject its exact content/classification, then separately **Admit reviewed revision**. Proposals and reviewed unadmitted bodies remain volatile; no body reaches disk before admission. The 128-identity bound includes durable shells/tombstones and current volatile proposals. Drafts and unadmitted bodies clear on native closure, session/ownership/privacy changes or restart.

These controls are visual-only; values are not speech, transcript/history, preferences, diagnostics or provider context. There is no model-origin/conversational **Remember this** trigger, automatic review/admit/use/recall, context attachment, hosted transmission or broader memory scope. Stale/foreign/missing original intent and changed admission refuse; inspect durable state after a receipt failure rather than assuming rollback or retrying automatically.

## Native exact handoff review

Open **Settings > Providers > Review pending exact provider handoff**. This original-user local surface consumes only actual pending host-issued offers; production currently shows unavailable/no pending qualified offer. There is no typed/voice/model command that supplies an arbitrary envelope or fabricates an offer.

Select an exact offer and choose **Read complete exact envelope**. The inert complete preview includes session/task/question/offer identities/revisions, destination/model/catalogue revision, typed reason and every evidence item's lineage/disclosure.

**Approve this exact context only**, **Decline**, **Cancel** and **Remove selected evidence** use the existing workflow. Removal retires the old question/offer and requires a fresh complete read; there is no affirmative preselection or inherited approval. Closing/expiry/cancellation/privacy loss never approves.

Review is local and volatile, with no content logging/persistence, copy/export or speech. Typed outcomes state that runtime/account/final-request-egress gates are unavailable and nothing was sent or executed. No provider, adapter, account, send, fallback, retry or grant is enabled. LocalOnly defaults and per-turn choices are unchanged.

## Trusted local events

An already-open **Sessions** selected work surface shows bounded trusted
local-version queue/question observations and already-verified maintenance
availability. No window opens, focus changes or speech occurs automatically.
Native **Review exact event**, **Dismiss exact event**, and **Defer exact event
15 minutes** share the same broker as these exact typed/current-name activated
commands:

| Exact syntax | Result |
|---|---|
| `event status <event-id> <revision>` | Passive current metadata and explicit delivery/stale/unavailable reason |
| `event review <event-id> <revision>` | Same bounded passive review; not a question answer or maintenance Open |
| `event dismiss <event-id> <revision>` | Reject only that exact event revision |
| `event defer <event-id> <revision>` | Defer fifteen minutes, capped at the original source expiry |

Copy the exact canonical D GUID and positive revision from the current native
row. Names, arbitrary IDs, stale revisions, extra words and old/current-source
changes refuse. Refresh and reselect after a mutation; its revision advances.
Activated input still needs normal current-name activation/voice consent, but
this response is always visual. Pending foreground questions/approvals remain
unchanged; answer them separately. No ambient microphone, model, network,
dispatch, deadline extension, grant or maintenance Check/Open is added.

PresentedNoReplay is status, not another notification. Category-limited,
dismissed, deferred and expired rows do not repeat their summary. Fixed
hourly limits are Work 3, Failure 2, Attention 2, Maintenance 1, with one-minute
spacing. At most eight rows are visible, with omitted counts. Corrupt,
unconfirmed, unavailable or full suppression storage holds the broker; work
inspection remains independent. Restart never reconstructs current events or
replays presented/dismissed/expired effects. Fresh native observation may admit
an unexpired explicit deferral under its original deadline.

## Deterministic local-version queue

Native **Sessions** queue controls and exact typed/current-name activated input
share one host service. No model, network or audio is required for management
or the fixed read. Voice commands still require normal activation/consent;
the queue cannot open a microphone or speak a response.

| Exact syntax | Result |
|---|---|
| `queue help` | Bounded syntax and unavailable scope |
| `queue list <session-id>` | Current entries/revision plus bounded atomic work snapshot, recent receipts, pending question identities, capacity and eligibility reasons |
| `queue status <session-id> <task-id>` | Exact retained work receipt; unknown IDs provide no authority |
| `queue enqueue <session-id> <generation> <queue-revision> <request-id> <task-id> version [after <task-id>]` | Add one fixed local application-version read; dependency requires an existing exact task |
| `queue cancel <session-id> <generation> <queue-revision> <task-id> <entry-revision>` | Cancel only that pending entry, not an admitted worker/effect |
| `queue remove <session-id> <generation> <queue-revision> <task-id> <entry-revision>` | Remove only that pending entry from dispatch, retaining history |
| `queue clear <session-id> <generation> <queue-revision> confirm` | Explicitly confirm clear of this exact pending snapshot; current work/history remain |
| `queue dispatch <session-id> <generation> <queue-revision>` | Manually process up to 32 already enqueued ready local-version reads fairly across eligible sessions |

Use canonical nonempty D GUIDs and the exact displayed positive session/entry
revisions. Queue revision is the displayed committed snapshot revision; zero
is valid only before the first queue mutation. For typed enqueue, supply fresh
request/task GUIDs; native **Enqueue local version** generates them. Duplicate
IDs, names, unknown/stale revisions, Done/disposed sessions and full queues
fail closed. The selected session's ID is visible and never inferred from a
name, model response or window title.

Enqueue does not start work. Selecting an exact session passively reads work;
**Refresh selected work** refreshes that observation. Choose **Inspect selected
work ID**, **Cancel selected eligible work**, **Remove pending ID** or
**Confirm clear displayed pending queue** only for displayed exact revisions.
The native controls call the same exact command workflows. Stale controls refuse
instead of following another selection or revision. Only the separate genuine
pre-dispatch local-version question wait can use exact task cancellation;
admitted/effect work is not cancellable in this delivered slice.
All/Active/Done filtering affects only the current bounded session page.
Five-second passive work refresh never dispatches, reprioritizes, extends
meaningful activity, moves focus or retargets voice/questions/approvals.
Dispatch is manual, FIFO within a session and fair across ready sessions;
the invoking session gains no priority. Default capacity is ten pending entries
per session and one admitted task per session. The shipped host uses one
global slot; only the fixed local-read implementation permits a host
limit of one or two.

## Fixed queue settings

Native **Settings > Sessions > Fixed read-only local-version queue** and exact typed/current-name activated commands use the same [admitted configuration workflow](../src/Kora.Application/Configuration/SessionQueueConfigurationService.cs).

| Exact syntax | Result |
|---|---|
| `list queue settings` | All three supported option IDs, ranges/defaults, saved/effective values, minutes unit, revisions and unavailable scope; advertised schema 2 |
| `get queue.pending-per-session` / `status queue.pending-per-session` | Inspect capacity; integer 1-10, default/reset 10 |
| `get queue.execution-slots` / `status queue.execution-slots` | Inspect fixed synchronous read-only slots; integer 1-2, default/reset 1 |
| `set queue.pending-per-session to <integer 1-10>` | Audited future pending-admission capacity; existing entries are never evicted |
| `set queue.execution-slots to <integer 1-2>` | Audited future fixed-read admission limit; existing active reads are not cancelled |
| `get queue.pending-lifetime-minutes` / `status queue.pending-lifetime-minutes` | Inspect future pending lifetime; canonical integer 1-120 minutes, unsaved/default/reset 30 |
| `set queue.pending-lifetime-minutes to <integer 1-120>` | Capture only in newly enqueued admitted fixed reads after confirmed activation; all existing deadlines remain exact |
| `reset queue.pending-per-session` | Reset only pending capacity; preserve slots/lifetime |
| `reset queue.execution-slots` | Reset only slots; preserve capacity/lifetime |
| `reset queue.pending-lifetime-minutes` | Reset only future pending lifetime to 30 minutes; preserve capacity/slots |

Integers are canonical: no signs, leading zeroes, fractions, extra words or apply-now.
Native **Refresh** captures a current admitted draft; each **Save**/**Reset** requires that unchanged revision and visible lifetime, then another Refresh.
Concurrent native/typed edits and hide/reopen retire the old draft; they cannot overwrite a newer preference ([parser](../src/Kora.Application/Configuration/SessionQueueConfigurationCommand.cs), [native tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.QueueConfiguration.cs)).

Confirmed limits actually feed enqueue, fair dispatch and native/exact/event observation.
Reducing capacity never evicts pending work or changes existing entry deadlines; reducing slots never cancels active work or changes its admission budget.
Pending lifetime defaults to **30 minutes**; its **1-120 integer minutes** option affects future new enqueues only. Active budget stays **5 minutes**, unavailable to edit. No apply-now or existing-deadline shortening/extension is available.
Automatic dispatch, general execution, workers/resource leases, model tools and real two-slot provider/hardware qualification remain unavailable ([queue consumer](../src/Kora.Application/Hosting/SessionQueueService.cs), [deadline tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionQueueTests.cs)).

Protected Active/Suspected/Unknown original-voice writes deny without downgrade or deferred application.
Malformed/unconfirmed/inaccessible preferences or failed required audit/readback/control receipts hold new admissions, not fabricated defaults or rollback.
Inspect saved state and receipts, explicitly repair, then Refresh; ordinary refresh never clears the unconfirmed marker ([recovery contract](../Design/User_Configuration.md#delivered-bounded-fixed-local-version-queue-settings-r10r13)).

Pending eligibility expires at each entry's recorded deadline; an expired head remains visible
until explicitly removed/cleared, and blocks later work at that position.
The five-minute active budget starts at admission; late read results cannot
be successful receipts. Existing pre-dispatch user questions have their own
expiry and consume neither slot nor active budget; this fixed profile creates
no in-task questions. It provides no effect-worker cancellation or forced
termination. A failed batch stops; make a fresh explicit dispatch decision.

Lock/privacy/ownership or call-policy changes retire eligibility even after
unlock: inspect, remove and explicitly requeue rather than resuming stale work.
Unknown outcomes quarantine the addressed session and dependent tasks, while
unrelated sessions remain eligible. Restart restores status/history only:
pending work is Interrupted and prior dispatch is Unknown, never replayed.
Do not blindly retry a failed audit/storage/receipt operation; a commit may
already exist. Inspect exact durable IDs/revisions before a new decision.

Known preference schema 1 validates and preserves capacity/slots, introducing
only lifetime default 30 without rewriting on observation; explicit saves use
schema 2. Legacy queue payloads remain exact fixed-30 with original bytes/digests;
new format 2 captures integer minutes under committed enqueue authority. Unknown,
partial, downgraded or unbound payloads refuse without replacing history/authority.

No script, power, write connector, arbitrary resource/effect, hosted provider
or local-model reasoning descriptor is schedulable. This is a deterministic
read-only core, not full two-slot worker/provider/hardware acceptance.

## In-call feedback override

Native **Settings > Calls** and exact typed/current-name ACTIVATED commands
share one device-local, original-input host workflow:

| Exact command | Result |
|---|---|
| `list call feedback settings` | One admitted descriptor: choices/UI default, scope, saved/default/unavailable provenance, desired/effective/applied mode, revisions, timing, hard gates and recovery |
| `get calls.feedback-mode` | Current preference and independent output policy |
| `status calls.feedback-mode` | Same bounded status |
| `set calls.feedback-mode to Voice` | Save only the in-call feedback preference |
| `set calls.feedback-mode to UI` | Save UI-only preference |
| `set calls.feedback-mode to Both` | Save combined preference |
| `set calls.feedback-mode to Inherit` | Restore ordinary response selection during calls |
| `reset calls.feedback-mode` | Remove only this override; unsaved UI default |

Use the current configured prefix, not an old alias. Complete input/result
bounds are 1,024 UTF-8 bytes/64 KiB. Extra words, unknown targets, controls and
numeric/ordinary-mode aliases clarify locally; no model settings tool is added.
Active/Suspected (including manual Active) alone applies this before ordinary
output. Unknown/invalid evidence withholds speech and keeps full visual
recovery; Clear/Unavailable uses ordinary selection. Voice/Both never bypass
independent call speech suppression, privacy/lock/mute/capture/lifetime/safety
gates, or enable input/consent/grants.

Protected-call original-voice set/reset is refused, never relabelled by a later
click or deferred until clearance. Initiate a new eligible UI change. Host-held
choices and current name/input/call/configuration/session/native lifetime are
revalidated. Audits, atomic save/readback and durable intent outcome precede
activation; stale/corrupt/unconfirmed state stays unavailable with explicit
recovery, including across restart. No capture, detector, autoplay or replay.
See [Settings recovery](settings.md#in-call-feedback-override).

## Explicit local file preview

| Exact command | Result |
|---|---|
| `preview file` | Open the trusted native picker for one local UTF-8 `.txt`, `.md` or `.markdown` file, then metadata-only review |
| `clear file preview` | Discard this volatile review/revision; never delete the original |
| `search file` / `inspect file` | Focus the admitted revision's native lexical search/inspection control; no arguments, path or implicit source selection |
| `preview folder` | Native picker and complete metadata review of 1–32 immediate UTF-8 text/Markdown files; no subdirectories |
| `search folder` / `inspect folder` | Focus native lexical search across only the exact complete admitted folder revision |
| `clear folder preview` | Discard the volatile folder review/revision; never delete originals |
| `refresh file` / `refresh folder` | Fresh metadata review of only the exact already-admitted physical file/folder; separate new native confirmation required |

The tray's **Preview file (local inspection only)** uses the same host service.
Current-name activated input can open selection; spoken/typed paths and IDs
cannot confirm a read. Use **Confirm: read this exact selected file locally**
in the native review, before its two-minute expiry. The review discloses the
canonical path, original host session/origin, identity, exact bytes and bounds.
The immutable plain-text result shows source/revision/item IDs and SHA-256 of
original bytes (including BOM). There is no automatic refresh.

Native **Refresh this file preview** / **Refresh this folder preview** and exact `refresh file` / `refresh folder` use the same bounded action. No path, ID, extra words or document instruction can authorize refresh or confirmation. The exact original canonical path and physical file/directory identity must still be valid; missing, replaced, aliased or unsafe roots require a fresh native picker selection, never silent rebinding.

Refresh starts by retiring the old immutable preview and citations. It reads **metadata only** into a new review, with a fresh two-minute deadline; confirm that new review separately before content reads. Source identity is preserved but successful revision/item IDs and digests are new, and old exact references become stale. Folder review shows the complete new inventory and added/removed/metadata-changed members; unchanged metadata does not prove unchanged bytes. Failure/cancel leaves no admitted preview: fix the refusal and use the native picker again. Kora never modifies the original files.

Folders have at most 32 immediate files and 1 MiB combined original bytes, with the same 256 KiB per-file limit and supported formats. Empty folders, subdirectories, unsupported/unsafe items and any capture or strict-decoding failure reject the whole candidate; no recursive or partial admission. Separate confirmation covers every reviewed item.

Maximum: one fixed-drive file, 256 KiB source bytes, 240 path characters,
32 components; strict UTF-8 only. Unsupported, inaccessible, unstable,
reparse/hard-link, protected, hidden/system and source-control/generated
inputs fail closed without truncation or silent exclusions. Close, clear,
Cancel task, lock/privacy/ownership or origin/call-generation changes discard
the preview. File content never becomes a command or approval.

Enter query text only in the native search control, not in a command or
conversation. This deliberately keeps queries/excerpts out of transcripts,
history, speech, models and clipboard. Limits: 256 UTF-16 characters,
512 UTF-8 bytes, 32 unique terms, 64 characters per term; invalid input is
rejected, never silently shortened. Ranking is deterministic OR matching:
distinct normalized terms, frequency capped at 16 per term, then source offset.
Terms are Unicode letters/numbers with attached combining marks, canonical
Form C and invariant uppercase; punctuation separates terms. Duplicate terms
do not boost ranking. Oversized source words are skipped whole.
Adding arguments to `search file` / `inspect file` is rejected locally rather
than falling through to inference; it neither selects a path nor performs a search.

At most eight exact excerpts / 16 KiB excerpt UTF-8 are returned. Each is at
most 2,048 UTF-16 characters / 128 lines. Results display the query observation
time, source/revision/item IDs, original-byte SHA-256 digest, display filename,
heading, exact UTF-16 offsets, and 1-based line/column range (exclusive end;
CRLF is one newline). Truncation is explicit; no match differs from stale,
cancelled, busy, invalid or unavailable. Search does not re-read the current
path or refresh the revision. Closing/revoking/replacing/discarding the source,
Cancel task and privacy/ownership changes invalidate late results. This is a
volatile local inspection, not a durable workspace session attachment.

**Unavailable:** recursive folders, automatic refresh/watchers, managed source registry, UNC/removable drives, durable attachments,
knowledge sources, persistent/vector indexing, local/hosted reasoning or file-model
tools. Preview neither submits content nor authorizes egress. It never reads
or changes the clipboard, executes content, logs content/paths or saves the
revision. [Privacy details](privacy-safety-and-logs.md#local-file-inspection).

## SQLite diagnostic retention

Native **Settings > Logging** and exact typed/current-name ACTIVATED commands
share one independent host-admitted workflow:

| Exact command | Result |
|---|---|
| `list logging settings` | Both independent diagnostic/audit options' integer schema/default/bounds, saved/effective provenance, revisions, timing, exclusions and recovery |
| `get logging.sqlite-diagnostic-retention-days` | Inspect the confirmed preference or explicit unavailable recovery |
| `status logging.sqlite-diagnostic-retention-days` | Same bounded current status |
| `set logging.sqlite-diagnostic-retention-days to 14` | Canonical integer 1–365; applies only to newly committed ordinary SQLite records after required audit and atomic save/readback/receipt |
| `reset logging.sqlite-diagnostic-retention-days` | Remove only this override; future commits use default 30 |

Use **Kora,** or the current configured prefix, not an old alias. The complete
input is at most 1,024 UTF-8 bytes and output 64 KiB; controls, ambiguous/extra
words, noncanonical integers and unrelated logging targets clarify/reject
locally. No natural-language/model mutation or guessed setting is admitted.
Original-channel host/session/generation/privacy/call/input and expected
revisions are fresh-checked. Pending exact questions/approvals remain intact.
Protected-call original voice mutation is denied, never deferred or relabelled.

**Existing deadlines stay exactly unchanged. Apply-now/immediate deletion is
unavailable.** No set/reset triggers pruning. Independent audit retention,
daily files 30 days/30 files, session/chat/history, grants/approvals and cleanup
schedule are unchanged. Perpetual grants have no time expiry/retention/eviction.
See [settings and explicit recovery](settings.md#sqlite-diagnostic-retention).

## Future-only audit retention

Native **Settings > Logging** and exact typed/current-name ACTIVATED input
share the independent audit workflow:

| Exact command | Result |
|---|---|
| `get logging.audit-retention-days` | Complete saved/default/effective policy, 30–365 bounds/default 90, revision, timing/exclusions and explicit held recovery |
| `status logging.audit-retention-days` | Same bounded current status |
| `set logging.audit-retention-days to 180` | Canonical integer 30–365; changes NEW required authority audit and independent diagnostic audit projections after required prior-policy receipts and atomic save/readback/intent outcome |
| `reset logging.audit-retention-days` | Removes only the audit override; future audit commits use 90 |

Use the current assistant prefix, not an old alias. `list logging settings`
includes this option alongside independent ordinary SQLite days. Inputs remain
bounded to 1,024 UTF-8 bytes and complete output to 64 KiB; malformed reserved
commands stay local and never route to inference. Original owner/session/
generation/privacy/topology/input/call/native-lifetime and proposal revisions
must remain eligible; protected-call original voice mutation is denied.

Existing deadlines/payloads/citations are unchanged. Apply-now, immediate
deletion, pruning and cleanup triggers are unavailable. This is not a grant
lifetime control: every grant and its validity/scopes remain, and Perpetual
records have no expiry/retention/eviction. No session/history/approval/task/
question pruning or ordinary/file policy changes. See
[held evidence and restart recovery](settings.md#future-only-audit-retention).

## Assistant display / PTT command-prefix setting

Typed input and explicitly activated voice use the same host configuration
workflow as **Settings > Speech & audio > Apply name / Reset name to Kora**:

| Exact command | Result |
|---|---|
| `list assistant settings` | Schema/default/bounds/scope/effect/timing/reset and current revision/provenance/recovery for the one admitted setting |
| `get assistant.name` | Current display/PTT prefix and saved/default provenance, or explicit unavailable recovery |
| `set assistant.name to Nova Prime` | Validate with the existing name rules and atomically save one device-local value |
| `reset assistant.name` | Restore Kora only; no durable identity, session-name, grant, approval, data-path or namespace reset |

The spoken target **assistant name** is equivalent to `assistant.name`.
The active configured prefix is optional: **Kora, set assistant name to Nova**.
After success use **Nova, get assistant name**; Kora is no hidden alias.
Input is bounded to 320 characters after prefix removal, and names retain
their existing exact case/Unicode validation with whitespace trimming/collapse.
Unknown targets and malformed grammar clarify locally; invalid names are
audited denials, never inference or arbitrary configuration execution.

Set/reset require current host ownership/privacy and configuration/call
revisions. Protected/unknown calls reject original voice requests. These
commands do not answer, replace or approve a pending exact question/approval.
Mutation retires stale capture/transcript/completion generations and does not
replay input or re-enable listening. Explicit **Enable listening** and a new PTT
are required. Grammar-start failure is visible with no old-prefix fallback.
Corrupt saved state disables prefix routing; native recovery and unprefixed
get/set/reset, stop/cancel and Settings remain available.
This is **not production wake-name capability**, a model settings tool, an
acoustic acceptance result or completion of all R10.

## Exact cached release maintenance

Typed input and explicitly activated voice use the same guarded native
maintenance workflow:

| Exact command | Result |
|---|---|
| `maintenance status` | Cached channel/status, last verification/staleness and review/snooze readiness; missing or failed checks remain Unknown/error, not "up to date" |
| `maintenance review` | Review the exact fresh host-held canonical record/source/architecture/digest/unsigned disclosure and reveal the existing native review window |
| `maintenance snooze` | Snooze only that fresh, reviewed Available notice for 24 hours within this run; no persistent reminder policy or other prompt change |

The optional **Kora,** prefix uses the current configured name. The complete
original input is at most 128 characters; phrases are case-insensitive with
outer whitespace trimming, not fuzzy punctuation/extra-word matching.
Complete visual results are at most 8,192 characters; oversized results fail
explicitly, not by truncation. No command speaks, probes devices, activates
capture, invokes a model/provider or treats release text/URLs as authority.

Use native Maintenance to grant **public metadata checks for this run**, check
or explicitly open an already reviewed canonical page. These cached commands
never check/refresh, grant/renew consent, open a browser, download, install,
elevate or activate source. Stale or failed metadata requires a deliberate
native check after admission recovery, not automatic command fallback.
Commands preserve the complete current exact question/approval and cannot
answer it. Unknown ownership/privacy, protected/unknown calls, stale/foreign
records, changed original voice generation/session or failed required audit/
receipt deny success and require a fresh explicit request. This is bounded
command parity, not a general notification broker, production wake or
installed/native/runtime acceptance.

## Exact current-run manual call control

Native **Settings > Calls** and typed/already **activated** voice share:

| Exact command | Result |
|---|---|
| `list call settings` | Complete schema, default-off, run-only scope, timing, reset and cached policy/source revision |
| `get call.manual-active` / `status call.manual-active` | Manual flag, independent automatic observation/availability, effective conservative protection and saved flags |
| `set call.manual-active to on` | Enable only the current-run manual layer |
| `set call.manual-active to off` | Clear only that layer; never assert detector Clear |
| `reset call.manual-active` | Same manual-off effect; no saved preference reset |

The optional prefix is the current installed display/PTT name, not a hidden
old-name alias or production wake. Grammar is case-insensitive with outer
whitespace trimming. `on`/`off` are the only set choices; extra words, controls,
unknown call targets and inputs over **1,024 UTF-8 bytes** clarify locally.
Complete versioned JSON is limited to **64 KiB**, never truncated.
These reserved routes run before pending-question input. Inspection is passive
and cached: no control intent, meaningful activity, approval, detector probe,
model, speech, capture or unrelated effect. Results appear in the transcript/
native cached status without replacing the full question, approval or preview.
Mutation is refused while a question/approval is pending; it cannot answer,
approve, rebind or defer a change until clearance.

Set/reset require original input captured before asynchronous work, genuine
host-created committed intent/session generation, own live request context,
current source/policy revision and current ownership/privacy/input gates.
Protected/Unknown calls deny original voice mutations, including off/reset;
a later mouse confirmation cannot relabel that voice input. Start a new
eligible native/typed request instead.
Changed manual state fences old synthesis/playback, input and callbacks;
old requests may finish visually but cannot synthesize/replay after off/reset.
Capture stays closed until a separate fresh **Enable listening** and PTT.
No consent, microphone, saved call flags, grant record, output mode or volume
is changed. Mandatory full visual information remains intact.

Required typed authority audits precede the fenced process-memory transition
and record its truthful outcome on the existing shared SQLite lease. This is
not atomic persistence of the manual flag. A lost audit/receipt or unconfirmed
retirement reports **not-confirmed** and retains explicit conservative
evidence-unavailable protection; it never claims rollback, retries, or uses
unknown evidence to authorize less protection. Startup remains manual-off
with honest detector-unavailable status. Automatic detection, speak-once,
protection relaxation and full R15/native/acoustic acceptance remain absent.

## Read-only host discovery

These exact local commands require the active, unlocked Kora host and do not
invoke a model, install anything, or refresh probes:

| Exact command | Result |
|---|---|
| **list capabilities** / **capabilities.list** | Six admitted read-only descriptors with schema version, typed input/output shape, read-only effect, caller lanes, availability and limits |
| **describe capability** followed by a canonical ID / **capabilities.get** followed by that ID | The single admitted descriptor; unknown IDs are denied |
| **show registry version** / **application.get_version** | Actual running version; deployment information is explicitly not observed by the current provider |
| **show dependency readiness** / **readiness.get** | Recorded dependency observations and timestamps; unobserved dependencies are explicit |
| **list runtimes** / **runtime.list** | The existing local inference adapter's recorded status, not a catalogue of planned providers |
| **show local runtime status** / **runtime.get_status** | The same `local.inference` observation; tool-loop qualification remains false |

Optional configured-name prefixes and normal exact-command punctuation/case
handling apply. For example, **Kora, describe capability runtime.get_status**.
The complete serialized UTF-8 response is at most 4,096 bytes; lists contain
at most six records. The host API accepts only `{}` for version,
`{"offset":0,"count":6}` (both fields optional) for lists, and a required
`{"id":"..."}` for descriptor/runtime lookup. Unknown fields, duplicate fields,
invalid ranges, foreign/expired host context and unknown lanes are denied.
The native commands supply those inputs deterministically, rather than accepting
arbitrary JSON from the command box.

These results do not contain raw probe details, paths, endpoints, credentials,
model content, skill instructions, or execution tools. Observations are cached:
use the existing Readiness UI for a deliberate fresh check and recovery details.
An observed ready dependency does not grant execution permission. No hosted
provider, MCP adapter, or model tool/result loop is delivered by this registry.
Existing help, version, setup/status, lock and power phrases retain their behavior.

## Exact input-device preference

Typed input and **activated** voice use:

- **list input settings** — metadata-only discovery with the existing
  five-second single-flight deadline;
- **get speech.input-device** — recorded desired/effective choice, source,
  metadata/call revisions, availability/readiness and recovery;
- **set speech.input-device to {exact listed endpoint ID}** — preference only;
- **reset speech.input-device** — explicitly selects `system-default` (System).

The configured-name prefix is supported. Option grammar is case-insensitive;
endpoint IDs are exact and case-sensitive. Friendly names, indices, fuzzy
targeting and natural-language aliases are not selectors. Duplicate friendly
names are distinct IDs. System follows the Windows multimedia default;
unavailable pins remain pinned. Complete versioned JSON results are at most
64 KiB; original commands are at most 1,024 UTF-8 bytes without controls.
Oversize results are rejected, not partially presented.

These routes share the native Settings/tray/recovery-card preference workflow.
Selection/reset never enables listening, grants consent/permission, tests audio
or changes model/OS settings. Changed selection closes stale input; manual
disablement and recovery holds remain closed. Protected/unknown calls reject
original voice mutations; stale metadata, unknown ownership/privacy/permission,
pending questions/approvals and failures require a new explicit operation.
Terminal audit failure may follow a file replacement: inspect before retrying,
not automatic rollback/replay. There is no model tool or pending-question bridge.

## Explicit local clipboard preview

These exact commands and the tray's **Preview clipboard (local plain text)**
entry use the same request workflow without a model:

| Exact command | Result |
|---|---|
| **preview clipboard** / **preview the clipboard** / **snapshot clipboard** | Read one fresh bounded Unicode plain-text snapshot and open its immutable native preview |
| **explain clipboard** / **explain the clipboard** | The same local preview, with explanation explicitly unavailable |
| **reuse clipboard snapshot {exact snapshot ID}** | Reopen/select that same snapshot only; never reread or silently substitute changed clipboard text |
| **clear clipboard preview** / **revoke clipboard snapshot** | Discard Kora's snapshot and preview, not the Windows clipboard |

Configured-name prefixes and the normal exact-command case/punctuation rules
apply. The preview shows the host source/snapshot IDs, `CF_UNICODETEXT` format,
read version, capture time and exact UTF-8 byte count. Its **Reuse this exact
snapshot ID** button uses the displayed ID; **Revoke and clear**, closing the
preview, cancel, privacy/ownership loss, call-policy change and host exit clear
or suppress it. A new capture replaces the prior selection with a new ID.

The whole text must fit 256 KiB UTF-8, with valid paired Unicode surrogates.
Empty, unsupported, oversize, busy, denied, changed-version and malformed
reads are explicit; there is no truncation, queued retry, background watcher,
URL fetch, HTML/image/file capture, clipboard write, history or persistence.
Whitespace and line endings are preserved. Text is untrusted and may contain
secrets; preview/reuse is neither execution authority nor transmission approval.
Clipboard explanation is unavailable until qualified local tool-loop and
clipboard-answering gates pass. Nothing reaches the current JSON selector,
Ollama or a remote provider. See [privacy](privacy-safety-and-logs.md#clipboard-snapshots).

## Window and application tasks

### Inspect existing minimal durable sessions

- **open sessions**

The configured-name prefix is supported. This opens the same bounded native
[Sessions workspace](windows-and-tray.md#minimal-durable-sessions) as the tray
and compact response's **Ctrl+Shift+S**. It does not change a pending question
or approval target, create a conversation, resume a session or call a model.
Done/resume are explicit selected-ID native actions, not inferred from words
in history or from selecting a row.
The native workspace also offers **Create empty Active session** and **Rename
selected ID** with bounded durable names and optimistic revisions. These are
also available through the bounded exact commands below, but not model tools. A name never selects authority,
and the selected window never redirects global commands. Creation grants no
execution permission; rename/browse never resumes or changes approvals.
Native Sessions additionally offers separate
[preview/confirmation for logical disposition](windows-and-tray.md#logical-session-disposition).
It removes addressed live authority rows, not recoverable copies or full
history. No typed/voice `session delete` or model deletion tool is admitted.

### Bounded exact-ID session commands

Typed input and **activated** voice share one grammar, with the configured
assistant-name prefix supported. Use **session help** for the full syntax:

| Command | Actual bounded result |
|---|---|
| `session list [after <exact-id>] [limit <1-50>]` | Active/Done IDs, names, authorization generations and metadata revisions |
| `session status <exact-id>` | Exact authority and optional durable name; not inferred runtime progress |
| `session inspect <exact-id> [tasks\|questions] [after <exact-id>] [limit <1-50>]` | A page of existing task IDs/request IDs/states/revisions/origins or question IDs/revisions/states |
| `session create "<name>"` | Empty named Active session with a fresh host-owned ID |
| `session rename <exact-id> <generation> <metadata-revision> "<name>"` | Rename only, including a Done session |
| `session done <exact-id> <generation>` | Guarded idle lifecycle transition; not cancellation or proof of success |
| `session resume <exact-id> <generation>` | Explicit Active transition; never reruns work or revives approvals |
| `session history <exact-id> [after <generation>:<snapshot>:<sequence>] [limit <1-50>]` | Passive ordered host interaction history with stable exact citations; no control intent or activity extension |
| `session get <exact-id> <event-id>` | One exact history event belonging to that session, or explicit unknown; never name lookup or replay |
| `session search <exact-id> [after <generation>:<snapshot>:<sequence>:<query-digest>] [limit <1-50>] "<query>"` | Passive lexical search of committed retained fields, exact receipt citations and explicit bounded-scan gaps/omissions |

IDs must be nonempty canonical hyphenated GUIDs. Revisions are unsigned decimal
integers (generation positive, metadata revision zero for absent legacy metadata).
Copy the exact observation; a stale revision fails and requires a fresh request.
Names are NFC single-line Unicode, at most 120 scalars and 480 UTF-8 bytes,
without surrounding whitespace or control/format characters. Names must be
quoted; double an interior quote, e.g. `session create "A ""quoted"" label"`.
No punctuation stripping, name lookup, ordinal/window selection or fuzzy matching
applies to this grammar. Whole input is limited to 1,024 UTF-8 bytes, pages
default to 25/max 50, and the complete structured JSON result is at most 64 KiB.
Overflow fails explicitly rather than truncating. List/inspect cursors are exact IDs, not
saved snapshots; refresh for concurrent changes. History continuations instead
use the returned generation, snapshot ceiling and last sequence, separated by
colons. They remain bound to that exact session and exclude later appends.
Lifecycle changes require a fresh history read. Lifecycle results omit
unobserved metadata; request status to observe it.

Each accepted command has fresh original-user lineage. Except for passive
`history/get/search`, legacy controls retain a durable host control
intent/terminal receipt; reads do not change lifecycle, metadata, question or
grant authority. Existing partitions must be present. Errors are explicit:
refresh after conflict, resolve live/Unknown work or pending questions, or recover
private storage/ownership before a new deliberate request. A receipt failure
after a commit is not rollback; inspect current state before retrying.
The typed Run entry remains available for this deterministic namespace while
bootstrap work is busy; it does not cancel that work. Mutations still pass the
same exact-subject live-work and current host gates, not a new executor lane.

History records only committed host question snapshots/final answers, decision
metadata and task-state receipts. Gaps/current migration baselines, metadata-only,
over-budget unavailable content and disposition-redacted events are explicit.
Bootstrap user/model messages and response bodies are unavailable, not recovered
from logs. A task success is not proof of an external effect. Done histories
are readable; an exact Removed ID returns redacted citations only. There is no
composer, model history reasoning, Ask Evidence, broad export, attachment,
queue, scheduler, automatic resume or playback. History does not import volatile
file previews/shared skills or reconstruct captions. See the
[native workflow](windows-and-tray.md#bounded-passive-interaction-history).

Search uses the [shared lexical query rules](../src/Kora.Core/Context/LocalFileRetrievalPolicy.cs): 1-256 UTF-16 characters, at most 512 strict UTF-8 bytes, 1-32 distinct words of at most 64 characters, NFC/invariant case and literal whole-word OR matching. Punctuation separates words, not operators, wildcards or regular expressions. Quotes inside the final quoted query are doubled.

The [bounded scan](../src/Kora.Application/Hosting/SessionWorkspaceService.Search.cs) reads only actual retained question/option labels, final answer/choices and typed kind/task-state/decision/question-status fields. Results stay in ascending session-local sequence, not relevance order. Each call scans at most 200 receipts and returns the requested 1-50 complete receipts within 64 KiB, including JSON overhead. A query-bound SHA-256 continuation preserves the original session/generation/ceiling; equivalent normalized term sets may continue, changed terms require a fresh search.

`scanned`, `gaps`, `omittedMatches` and `next` are explicit [result fields](../src/Kora.Core/Storage/SessionHistorySearchPage.cs). Baseline/gap/redacted/unavailable receipts count as gaps; oversized matching receipts are omitted explicitly, not truncated. An empty page with `next` is an incomplete scan, not proof of no matches. Continue until `next` is absent. A fresh search includes later appends. Removed content never matches; Done reads never resume. Query text is volatile and is not stored, logged or sent to a provider. No indexes, schema changes or persistent query cache are added.

The native history page can open immutable **receipt details** for an exact
event belonging to the selected session. This freshly resolves the same
authoritative record as `session get` into the shared native plain-text viewer;
it adds no new typed/voice/model command, artifact persistence or execution
route. Availability/redaction/baseline and provenance remain visible.
Search/source and explicit disclosure-confirmed copy reuse existing viewer
policy. Work/queue/questions, lexical file citations and skill inspections
remain distinct; browsing never extends activity, reprioritizes or resumes.
Privacy closure and pre-deletion retention/disposition retire owned viewers.

Activated voice uses the existing enablement/consent/capture/privacy boundary
and retains its originating channel and observed call/recovery revision through
commit. During protected calls, reads require permitted activation and private
presentation; voice mutations are explicitly unavailable under the existing
workspace clear/unavailable-call gate. Nothing is queued for later. Unknown
ownership/privacy fails closed. Pending bootstrap questions/approvals block
session commands without changing their targets; resolve them explicitly first.

**Exact task controls** use the same typed/activated-voice grammar and limits:

| Command | Actual bounded result |
|---|---|
| `task help` | Syntax and availability |
| `task status <session-id> <task-id>` | Exact durable state/revision, session generation, source/current-run and pending/terminal question distinctions |
| `task inspect <session-id> <task-id>` | The same complete bounded authoritative record, not inferred progress or remaining steps |
| `task cancel <session-id> <task-id> <task-revision> <generation> <question-id> <question-revision>` | Atomically cancel only admitted current-run local-version work still waiting before dispatch |

Copy exact IDs and all tokens from a fresh inspection. Only the tray's native
local-version question currently admits that wait. Its question is now before
dispatch; cancellation commits the terminal task, revised cancelled question,
target capability revocation and required audit together. It never creates or
consumes a grant, deletes a task, replays work or terminates a worker.
Unknown/foreign IDs, stale/different questions or revisions, expiry, previous
runs, answered/committed/dispatched/Unknown work and lost host authority refuse.
Already terminal work stays terminal; inspect its real outcome. A confirmed
pre-dispatch cancellation is not a claim that an already invoked effect stopped.
A confirmed cancellation claims no effect termination. This safe cancellation is also available during a protected call with private
ownership and eligible original activation; call/recovery revisions still gate
commit. Pending legacy bootstrap questions/approvals remain unchanged.

This is not conversation/transcript persistence, a queue/executor/scheduler,
general effect cancellation, deletion/retention, routing inference, model tools, or a
session-name inference feature. Native selected-ID Create/Rename/Done/resume
continue to use the same host workspace service and guarded storage transaction.

### Show the Kora window
- **show Kora**
- **open Kora**
- **show your window**
- **open your window**
- **show yourself**
- **bring up Kora**

### Hide the Kora window
- **hide Kora**
- **hide your window**
- **hide yourself**
- **close your window**
- **hide the Kora window**

### Exit Kora
- **exit Kora**
- **quit Kora**
- **close the Kora application**
- **close Kora**
- **exit the application**
- **quit the application**

### Restart the Kora application
- **restart Kora**
- **restart your application**
- **restart the app**
- **restart yourself**
- **relaunch Kora**

Closing or hiding the window leaves Kora running; exiting the application
releases the microphone. Application restart is different from restarting
Windows.

## Settings and guidance tasks

### Inspect or change an admitted appearance option

- **list appearance settings**
- **get appearance.theme**
- **set appearance.theme to dark**
- **reset appearance.theme**

Substitute one of the nine exact IDs in [Appearance settings](settings.md#appearance).
The configured assistant-name prefix is supported for typed input and activated
voice. Exact spoken names also work: replace dots/hyphens in the ID with
spaces, for example **"Kora, set appearance theme to dark"**.
Capitalization is ignored, but this typed value grammar deliberately
preserves signs and decimal punctuation: `-10` and `1.5` are rejected, never
normalized into valid integers. Use numeric whole numbers without appended
units, `system`/`light`/`dark`, or `true`/`false`. Extra words, invented IDs,
relative changes and invalid ranges produce local clarification, without
model/network interpretation. Changes and per-option reset share the direct
UI service, domain validation, revision check, atomic persistence, audit and
live notifications. There is no whole-profile reset or undo, model tool
exposure, arbitrary JSON patch or configuration-file editing authority.

### Inspect or change an exact output choice

Audio **output endpoint** preference changes are separate:

- **list output settings**
- **get speech.output-device**
- **status speech.output-device**
- **set speech.output-device to &lt;exact presented endpoint ID&gt;**
- **reset speech.output-device**

Discover first, then use one exact ID; no name/index/fuzzy alias is accepted.
Native Settings uses the same admitted choice/save/reset workflow. System is
`system-default`; reset removes only Kora's output override. These commands never
play audio, change Windows defaults/volume, grant consent or answer an approval.
Protected/unknown calls block original voice-channel mutations. Stale choices,
ownership/privacy changes and failed evidence require explicit refresh/recovery.

### Inspect or change Kora playback volume

- **list volume settings**
- **get speech.playback-volume**
- **status speech.playback-volume**
- **set speech.playback-volume to 30**
- **reset speech.playback-volume**

Use one canonical integer 0-100 (no `%`, fraction, sign, padding or leading
zero). The current assistant-name prefix works; old names are not aliases.
Native Settings shares the admitted, revisioned atomic/audited workflow.
Default/reset **100** is original unscaled output; **0** blocks synthesis and
keeps the complete visual response. Changes stop stale active/queued speech;
raising/resetting never replays it. Original voice mutations are denied during
protected/unknown calls. These commands never play a trial, change global/call
volume, microphone/consent, other options, retention or pending approvals.
Discovery is metadata only, not a model tool or acoustic test. Input is bounded
to 1,024 UTF-8 bytes without controls; the complete result is bounded to 64 KiB.
See [availability and recovery](settings.md#kora-playback-volume).

### Inspect or change Windows-native speech rate

Exact typed or already **ACTIVATED** input uses the current assistant prefix:

- **list rate settings**
- **get speech.windows-rate**
- **status speech.windows-rate**
- **set speech.windows-rate to -10**
- **set speech.windows-rate to 0**
- **set speech.windows-rate to 10**
- **reset speech.windows-rate**

Canonical integer **-10..10**, normal/default/reset **0**, means Windows
provider-native engine rate, not percent or words per minute. Signs are accepted
only for negative nonzero values; padding, plus signs, negative zero, fractions,
suffixes and leading zeros are refused locally. Discovery/status includes
provider-qualified support and desired/effective/source/revisions/recovery.
**Kokoro is unsupported and unchanged**; no common speed scale is applied.

Native Settings uses the same admitted action with explicit draft/save/reset.
Original-channel audio session/generation, current name/provider/source/call/
privacy/ownership and native-lifetime conditions remain required. Protected/
Unknown calls deny voice-originated writes, even if later dispatched through UI.
Confirmed changes retire active/queued output and reach future eligible Windows
synthesis only. They never capture, synthesize/autoplay/replay, switch provider,
alter assets or change global Windows audio settings. Pending exact approvals/
questions and full interrupted visual responses remain intact. Unconfirmed
persistence/evidence stays unavailable across restart; inspect and repair before
fresh refresh. Existing call/output/zero-volume gates and retention rules stay.

### Inspect or change the device-default response mode

- **list response settings**
- **get responses.default-mode**
- **status responses.default-mode**
- **set responses.default-mode to Hybrid**
- **set responses.default-mode to VoiceOnly**
- **set responses.default-mode to VisualOnly**
- **reset responses.default-mode**

Only these exact enum names (case-insensitive) are accepted; numbers, lists,
invented options, fuzzy aliases and extra words clarify locally before inference.
Typed and explicitly activated voice use the same grammar, optionally prefixed
with the current assistant name. Input is bounded to 1024 UTF-8 bytes including
the prefix and rejects controls; complete JSON output is bounded to 64 KiB,
never truncated into a success-shaped partial result.
The existing device preference is saved atomically and read back under real
host/session/generation/original-channel/call admission. Reset saves Hybrid only.
Protected/Unknown calls reject original voice mutations; a fresh eligible
typed/native request is separate. Pending exact questions/approvals are not
answered or replaced. Results remain visual, never autoplay/replay or capture.
Task/queue precedence and mandatory call/privacy/full-visual recovery remain
unchanged; no narrower override or model settings tool is added.

### Inspect or change an installed speech choice

- **list speech settings**
- **get speech.provider**
- **get speech.voice**
- **set speech.provider to windows-sapi**
- **set speech.voice to kokoro / af_heart**
- **reset speech.provider**
- **reset speech.voice**

Use the installed IDs actually listed on your device. Exact spoken option
names, such as **speech provider**, and the configured assistant-name prefix
also work. Voice choices use `provider / ID`, or an unambiguous exact voice ID;
selecting a voice explicitly selects that provider too. IDs are not guessed,
translated or case-normalized, and unknown or unavailable choices do not
trigger a model, download or substitute. The command bound is 320 characters
after the optional name prefix; voice IDs are at most 256 characters.

Provider set/reset also restores that provider's advertised default voice;
provider reset chooses Windows. Voice reset restores only the current
provider's advertised default. An unavailable default requires an explicit
installed voice choice. Status reports saved/desired/effective values,
revision and recovery. Protected calls reject original voice-channel set/reset,
including a later UI confirmation; a new eligible Settings/typed request is
required. Rate and model settings tools are not added; volume uses the
independent exact commands above.
See [the shared native controls](settings.md#speech-provider).

### Inspect or lower spoken summary caps

- **list speech settings**
- **get speech.summary-sentences**
- **get speech.summary-words**
- **set speech.summary-sentences to 2**
- **set speech.summary-words to 40**
- **reset speech.summary-sentences**
- **reset speech.summary-words**

The exact spoken names **speech summary sentences** and **speech summary words**
also work, with the configured-name prefix for activated voice.
Values are exact positive integers: sentences **1-3**, words **1-80**.
Defaults are **3 sentences / 80 words**. Reset changes only that cap.
These device-local settings share the installed-speech workflow, original-channel
call/privacy/ownership gate, revisions, audit and live Settings notifications.
Neither command permits a model interpretation, extra model call or asset change.
Ordinary speech includes the title and retained warnings and must fit both caps.
Over-cap results remain fully visual with an explicit refusal status, never
truncated. Exact approval/question readback retains its own mandatory bounds.
See [counting and recovery semantics](settings.md#spoken-summary-limits).

### Open settings
- **open settings**
- **show Kora settings**
- **show settings**
- **open preferences**
- **change settings**
- **settings**

### Open the user guide
- **open documentation**
- **show documentation**
- **show the user guide**
- **open the user guide**
- **open the manual**
- **show me the instructions**

### Open setup and readiness
- **open setup**
- **configure Kora**
- **show what you need**
- **set up local models**
- **check setup**
- **show readiness**
- **check dependencies**
- **review local model setup**

Opening setup shows readiness; it does not install a model without approval.

### Show supported commands
- **what can you do**
- **help**
- **show supported commands**
- **list commands**
- **what commands do you know**
- **what can I say**
- **show me what you can do**

## Information and task-control tasks

### Show the running version
- **what version are you running**
- **show your version**
- **what version is this**
- **tell me your version**
- **which version of Kora is this**

The tray also offers **Review local version (native question)**, an explicit
mouse/keyboard-only route using the same durable local version-query context.
It does not need voice consent, a microphone, a model or network access.
The native window names the original session/task/question, revision and
expiry. Choose **Show local version**, then **Submit answer** to read the
running version and private-storage disclosure. No choice is preselected.
**Save draft** records the current answer without submitting. **Review exact
record** is passive inspection, not approval or execution. **Cancel question**
is explicit; Close/Escape only closes presentation. Stale, expired or
privacy/ownership-unavailable targets cannot be answered.

Storage/audit failure never reports a successful query. Close and start a new
review after correcting the blocker; Kora does not automatically retry uncertain
work. Explicit cancellation may leave an incomplete query dispatch record,
which startup recovers as Unknown without replay. This bounded route does not
enable general effect approvals, change legacy grants or replace the ordinary
version phrases above. Desktop/screen-reader/speech acceptance is not yet
claimed from the automated tests.

### Show activity and the setup queue
- **what are you currently working on**
- **what are you doing**
- **what do you have left to do**
- **what are you working on**
- **what tasks are left**
- **show the task queue**
- **show setup status**
- **what is your status**

### Show current-task progress
- **what is the current task status**
- **what is the current task progress**
- **how far along is the current task**
- **show task progress**
- **show current task status**
- **how is the current task going**
- **what's the progress of the current task**
- **how much of the current task is done**
- **how is setup progressing**

Current-task status reports the active setup stage and any measured download
percentage, or the next setup item requiring action when nothing is running.
It does not invent a completion percentage when the operation provides only
stage updates.

### Cancel the active task
- **cancel task**
- **cancel current task**
- **stop**
- **cancel the current task**
- **stop the current task**
- **stop current task**
- **cancel the download**
- **stop generating**

While a local model request is generating or any response is being spoken, the
response window also shows a separate **Cancel task** button. It stops the
current model request or speech playback; press **Esc** for the same action
while the button is visible. **Dismiss** hides the response without cancelling
model work or stopping speech. **Stop speaking** remains the explicit voice
command for stopping only speech playback.

### Stop speech playback
- **stop speaking**
- **stop talking**
- **be quiet**
- **stop reading aloud**
- **stop the voice**

**Stop** cancels the active task; it does not mean stop speaking. Use one of
the speech-playback phrases to stop the voice instead.

## Windows session task

### Lock Windows
- **lock the machine**
- **lock my computer**
- **lock windows**
- **lock this computer**
- **lock my PC**
- **lock my screen**
- **lock this workstation**

Locking is a real local action. Kora closes microphone capture before asking
Windows to lock. This exact built-in command currently calls a C# handler
without a confirmation prompt. A model-suggested lock requires approval
unless that named action already has a session or always grant.

## Protected power-proposal tasks

### Propose a computer shutdown
- **shut down the computer**
- **shut down this machine**
- **power off the computer**
- **turn off my computer**
- **shut down my PC**
- **power down this computer**

### Propose a computer restart
- **restart the computer**
- **reboot this machine**
- **restart windows**
- **reboot my computer**
- **restart my PC**
- **reboot the computer**

### Cancel a pending power proposal
- **cancel shutdown**
- **cancel that shutdown**
- **cancel computer restart**
- **cancel that reboot**
- **don't shut down the computer**
- **abort shutdown**
- **cancel the reboot**
- **abort restart**

### Show pending power-proposal status
- **what power action is pending**
- **are you about to restart the computer**
- **is a shutdown pending**
- **is a restart pending**
- **show pending power action**

Shutdown and Windows restart are proposals in the current release. Kora shows
the recognized request but does not send a power operation to Windows.

## Grant-management tasks

### List and view grants
- **list grants**
- **show grants**
- **view grants**
- **what grants are active**
- **list my approvals**
- **show my permissions**

The live session and persistent grants appear in a Markdown document window.
Listing never changes permissions and works without a local model.

### Prepare a grant change
- **manage grants**
- **add a grant**
- **edit a grant**
- **remove a grant**
- **change grants**
- **manage approvals**

Select the exact built-in action and scope in the response window: **Add**
creates a session or always grant, **Remove** revokes an existing grant, and
**Move** edits its scope using the destination after "to". The destination
selector is used only for Move. Kora repeats the exact action, operation and
scope; confirm with the button or say "approve once" (prefixed with the
assistant name by default), or reject. "Always allow this" does not confirm
a grant edit: the proposed scope is already fixed. A removal with no specified
scope only proceeds automatically when exactly one grant exists for that
action; otherwise select the existing scope. If a ready model identifies the
grant from a more specific request, it still cannot apply the change without
confirmation. Confirming a grant never executes the named action.

## Model execution settings

These exact commands read or change the same device-local choices shown on the
Settings **Models** tab. They work without invoking a model and are available
through typed input or activated voice.

### Show enabled model locations
- **which models are enabled**
- **show model settings**
- **show model configuration**
- **what models can you use**
- **are local models enabled**
- **are hosted models enabled**

### Enable local model execution
- **enable local models**
- **turn on local models**
- **use local models**
- **allow local models**

### Disable local model execution
- **disable local models**
- **turn off local models**
- **stop using local models**
- **block local models**

Disabling local models cancels an in-flight Ollama request. Built-in commands
continue to work.

### Enable hosted model execution
- **enable hosted models**
- **turn on hosted models**
- **use hosted models**
- **allow hosted models**
- **enable cloud models**

### Disable hosted model execution
- **disable hosted models**
- **turn off hosted models**
- **stop using hosted models**
- **block hosted models**
- **disable cloud models**

Hosted execution is disabled by default. The current build has no hosted
provider or credentials; enabling hosted models records permission but does
not send a request or provide a cloud fallback.

## When a command does not match

When local model execution is enabled and the selected local model is ready,
Kora sends only the current unmatched
request text, the built-in action descriptions, and a limited snapshot of
readiness, task state/progress, listening state, and pending power-proposal
status to it. The model can return an answer, ask a clarification question
with two to four options, propose a grant change, or request one named built-in
action; Kora rejects unknown actions
and runs allowed ones through its existing command handler. Hiding or exiting
Kora, restarting it, cancelling a task, locking Windows, or proposing a power
action requires approval unless you have already granted that specific action
for this session or always. When asked, say **"Kora, approve once"**,
**"Kora, approve for this session"**, **"Kora, always allow this"**, or
**"Kora, reject"**. You may also use the visible scope buttons; the request
remains visible even when voice-only responses are selected. The configured
assistant name is required for spoken approval by default, but can be made
optional in Settings. Session grants end on exit, restart, or locking Windows through Kora.
Always grants persist until revoked under **Settings > Approvals**. Reject
or dismiss the suggestion to discard it. These grants are keyed to action
names, not executable or script hashes, and do not change how direct exact
commands are dispatched. The model cannot
directly access arbitrary files, services, or tools. Without a ready local
model, Kora explains that other requests are unavailable. Use **what can you
do** or open this Documentation window to review exact built-in phrases.

When Kora needs direction before answering, the response window shows the
question and numbered choices, even in VoiceOnly mode. Choose an option by
mouse or say its number or label (for example, **"Kora, option one"**); the
assistant name is required for spoken choices by default, as with spoken
approvals. Say **"Kora, cancel question"** or use **Cancel question** to
dismiss it. The chosen option and the original request go back to the local
model; selecting an option is **not** permission to run an action or create a
grant. A later action proposal still requires its own approval, unless a
specific grant already covers that action. Kora limits consecutive questions
to three; start a new request with more details if it reaches that limit.
## Run skills and future artifacts

Kora can apply a bundled skill's instructions to a local-model request from
the command box or activated voice. Future bundled instructions and prompts
use the same command format.

**Type a slash command**

Type `/` in the command box to open a dropdown of every available bundled and
disk-backed artifact. Continue typing to filter by command name, or type a
kind such as `/skill `, `/prompt `, or `/instruction `. Use Up/Down and Enter
or select an item with the pointer; Escape closes the dropdown.

Use a direct command:

```text
/lock
/restart
/shutdown
```

You can also include the artifact kind:

```text
/skill lock
/skill restart
/skill shutdown
```

An artifact that accepts request text uses the rest of the line as its input:

```text
/prompt explain why this setup failed
```

The current release bundles only the three skills listed above. An unknown,
incomplete, or wrong-kind slash command shows an error and is not sent to the
model as an ordinary question.

**Say an artifact command**

Start with your configured assistant name, then say **run** or **use**:

- "Kora, run lock."
- "Kora, use the restart skill."
- "Kora, run the shut down the machine skill."
- "Kora, use explain to summarize this result." when a future `explain`
  artifact is available.

If you renamed Kora, use the configured name. Voice artifact requests without
the activation name are rejected in the same way as other free-form voice
requests.

**Where disk artifacts are loaded from**

Kora loads compatible files at startup from its own roaming `Skills`,
`Instructions`, and `Prompts` folders and the existing VS Code or VS Code
Insiders prompt/instruction locations. Personal skill roots under `.copilot`,
`.agents`, `.claude`, or another selected profile folder **are not loaded into
the model or artifact command catalogue**. Use the separate read-only native
inspection below. A shared root registration does not authorize any existing
prompt/instruction route.

Disk skills use `SKILL.md`; prompts use `*.prompt.md`; instructions use
`*.instructions.md`. Files require bounded UTF-8 content and YAML frontmatter.
Skills marked `user-invocable: false` do not appear. Conflicting command names,
IDs, or spoken names fail closed instead of choosing one source silently.
Restart Kora after adding or changing an artifact.

### Inspect shared profile skills locally

Open the tray **Skill packages**, then **Shared profile sources (read only)**.
Choose **Choose and register profile root (read only)** and select an exact
bounded local directory below your Windows profile, such as `.agents\skills`.
Kora resolves the profile through Windows Known Folder APIs, not environment
variables or a typed/model-supplied profile override. It never scans the whole
profile. Select a registered root and choose **List selected source**.

Review every compatible or unavailable package with its source ID/file,
declared name/version, exact SHA-256 byte digest, immutable inert SKILL.md text,
tool references, additional uninspected entries and reasons.
**Recheck selected revision** labels a changed live revision stale without
changing the displayed old snapshot; list again for a new review.

To stop Kora reading one source, select its registered metadata and choose
**Withdraw local read consent**. Review and confirm the exact source ID,
profile-relative root and directory identity in the native **Unregister source**
confirmation. This removes only Kora's registration and local inspection
snapshots, not your shared files. It changes no enablement, execution or grants;
other source registrations remain intact. Even a missing original folder can
be unregistered without reading or restoring it. A changed registration list
requires refreshing and confirming again. Re-registering the path requires a
fresh folder selection and receives a new source ID; old snapshots do not
regain consent.

The narrow reader requires flat YAML `name`, `version` (for example `1.0.0`)
and `description`. Unsupported metadata/YAML, scripts or extra package files,
executable/unknown fenced code and unresolved references remain unavailable,
not silently omitted. Invalid UTF-8 is unavailable without lossy conversion.
Limits are four roots, 32 packages/256 entries/four directory levels per source,
64 KiB per SKILL.md and 1 MiB instruction bytes per source. Links/reparse points,
hard links, path escapes and removed/replaced/busy sources fail closed.

Registration persists only root consent and directory identity in device-local
atomic preferences. Corrupt/unknown saved registrations block discovery; restore
a verified registration file or the original source explicitly. No silent reset,
repair or rebinding occurs. Closing privacy cancels reads and clears the view.
Withdrawal cancels in-flight inspection/recheck and clears text/hex snapshots
immediately; late results cannot redisplay a removed source. A write, read-back,
audit or admission failure does not prove rollback: the removal may already be
saved. Read authority and the view remain closed after an unconfirmed mutation
until **Refresh registrations** freshly observes valid saved state (or the
host's audit/session evidence boundary is recovered). No successful receipt is
claimed on failure.
For the attempted source, refreshing does not revive its old identity in the
current host: retry unregistering any remaining saved metadata, then select the
folder afresh to obtain a new source ID if you want to grant local reading again.

**Enable, disable, invoke, Kora-specific authoring and model exposure are
unavailable here.** Registration/review grants no execution, egress, approval
or bundled trust; Kora never writes the shared source, fetches references,
installs dependencies or executes its scripts. This is not a harmlessness or
complete dependency-inventory certification.

**What “run” means**

Kora sends the current request and the exact selected bundled instructions
only to the configured local model. The artifact is source- and
version-qualified, and it remains selected if Kora asks a clarification
question.

Selecting an artifact does **not** run its embedded PowerShell, approve a
protected operation, create a grant, or mean an action succeeded. If the model
proposes a registered action, Kora still applies the ordinary host validation,
approval, privacy, audit, and execution rules. The current embedded
session-control scripts remain inspection-only.
