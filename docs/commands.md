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

Native Settings > Providers and exact typed/current-name activated commands:

| Syntax | Result |
|---|---|
| `list provider settings` | Discover `providers.default-mode`, choices/default, provenance and revision |
| `get providers.default-mode` / `status providers.default-mode` | Inspect saved/default/unavailable initial mode |
| `set providers.default-mode to LocalOnly` | Privacy-first initial mode |
| `set providers.default-mode to LocalFirst` | Local-first initial mode |
| `set providers.default-mode to HostedPreferred` | Hosted preference only, no adapter/egress admission |
| `reset providers.default-mode` | Save LocalOnly; existing session policies remain unchanged |

Only first policy-bound session initialization consumes this preference.
Protected/Unknown voice writes refuse; corrupt/unconfirmed state is not a
default. See [Models and provider controls](settings.md#models) and
[preference recovery](settings.md#preference-changes-and-recovery).

## Inspect or change app provider profiles

Native **Settings > Providers** and exact typed/current-name activated commands share app-scoped configuration, not authentication or model execution:

| Syntax | Result |
|---|---|
| `list app provider settings` | Discover desired/saved/effective service, exact current catalogue and scope |
| `get providers.app.profile` / `status providers.app.profile` | Inspect saved and effective tuple/account/catalogue state |
| `set providers.app.default-service to Unset|Local|Copilot|Claude|Codex` | Explicit desired app service only; retain each service's tuple |
| `reset providers.app.default-service` | Local if supported Ollama installation is observed, otherwise Unset; retain tuples |
| `set providers.app.profile to <model-id> <reasoning-id> <context-id> for <account-id> at <catalogue-revision>` | Save the selected service's complete exact advertised tuple, with current opaque account/revision |
| `get/status providers.app.<model|reasoning|context>` | Inspect the same complete app profile and current catalogue |
| `set providers.app.<model|reasoning|context> to <exact-id> for <account-id> at <catalogue-revision>` | Review one field against the current saved complete tuple for the desired app service; retain other exact fields |
| `confirm providers.app.profile <host-issued-confirmation-id>` | Save only that reviewed field and whole complete tuple, with fresh original-user admission |
| `cancel providers.app.profile <host-issued-confirmation-id>` | Discard the review without writing; confirmation cannot be replayed |
| `reset providers.app.profile` | Clear only the selected service's tuple; no automatic replacement |
| `sign in providers.app.service` | Unavailable: no qualified sign-in adapter; no credentials accessed |

IDs are exact, case-sensitive, bounded catalogue values; labels/marketing names are not IDs. The revision is a canonical positive integer. Choose all dependent values explicitly; unsupported/provider-managed values cannot be invented. Complete input is at most 1,024 UTF-8 bytes; discovery output is at most 64 KiB and never truncated.

Individual setters never persist partial profiles or change the desired default service. Model edits preserve saved reasoning/context IDs only when supported by the new model. Missing/incompatible/stale bases require the existing complete tuple draft/save; no default, field reset or silent clearing is offered. Native field review may target another service while retaining the app default. Confirmation IDs are exact lowercase GUIDs issued by this host.

Initially desired Local requires installation observation, not endpoint response, model readiness or generation consent. Missing/unknown installation is Unset. Production services currently advertise no models. Saved desired hosted service does not sign in, enable a provider, approve egress or send a request.

Unscoped and one-turn directives are not app-scope aliases and remain unavailable. Exact session controls below are separate; existing LocalOnly mode/default is unchanged.
Protected/Unknown voice writes, stale previews, ownership/privacy/session changes, cancelled or unconfirmed evidence fail closed. See [app profile settings](settings.md#app-default-service-and-desired-profiles).

## Inspect or change an exact session provider profile

Use **Sessions > Selected-session provider overrides** or exact typed/current-name activated syntax. Session scope is explicit/default for these new mutations, never the app. `<session-id>` and confirmation IDs are canonical lowercase immutable GUIDs, not names or model-supplied destinations.

| Syntax | Result |
|---|---|
| `get providers.session.profile for <session-id>` / `status providers.session.profile for <session-id>` | Saved sparse fields, inherited app values, effective resolved/unavailable state and catalogue provenance |
| `set providers.session.service for <session-id> to Unset|Local|Copilot|Claude|Codex` | Review desired service override; no authentication or execution |
| `set providers.session.<model|reasoning|context> for <session-id> to <exact-option-id> for <account-id> at <catalogue-revision>` | Review one field from the current effective tuple; only this sparse field becomes explicit |
| `set providers.session.<model|reasoning|context|profile> for <session-id> to <model-id> <reasoning-id> <context-id> for <account-id> at <catalogue-revision>` | Review one sparse field or all three dependent fields, bound to exact current advertised model/options/account/revision |
| `reset providers.session.<service|model|reasoning|context> for <session-id>` | Review inheritance for only that field |
| `reset providers.session.profile for <session-id>` | Review **Use app defaults for this session**, clearing all four overrides |
| `confirm providers.session.profile <host-issued-confirmation-id>` | Separately confirm only the exact still-current original-input review |

No exact eligible session means select one; no app fallback. Service resolution precedes inherited tuple resolution, so an overridden service uses that service's app tuple. App preferences are never written. Later app/session edits affect subsequent turns only; stale original input, session/generation/policy/app revision or catalogue/account invalidate review.

Short setters retain the other effective choices and their exact null/explicit sparse state. Review shows both whole effective tuples before separate confirmation. Missing/unresolved tuples, incompatible models or rebinding previous explicit dependents require reset or the complete-profile route; no defaults, dependent clearing or successful-but-ineffective save. Existing full-triple syntax and resets remain available.

Production has no qualified catalogue models/auth adapters, so model-dependent selection remains unavailable; desired service may be saved unresolved. No sign-in, model call, generation, network/egress or envelope increase occurs. Generic “Use Claude and summarize”, ambiguous labels and one-turn language remain deferred. Unconfirmed outcomes require inspection of exact saved policy and audit/control receipts, never automatic retry/reset or a rollback claim.

## Attach one text file to an exact Session (native only)

For a content-free overview of existing retained slots, use **Tray > Retained session attachments > Refresh metadata**. Opening/listing never reads an original path or body. [Catalogue limits and exact navigation](windows-and-tray.md#retained-session-attachments-metadata-only) explain bounded pages, removed/held status, stale continuation and separate full-byte inspection. This adds no typed/voice/model capture command.

Open **Sessions**, select the exact Active session, then **Attach text file to this exact Session**. A trusted native picker opens only metadata; review canonical path/native identity, source and host session, bytes/time, fixed limits and local durable retention/copy disclosure. **Confirm: capture and retain this exact file for this Session** is separate persistence consent, not the existing volatile preview confirmation.

One immutable UTF-8 `.txt`/`.md`/`.markdown` file per session; maximum 262,144 original bytes including BOM; sixteen retained files across the private profile; no eviction or silent overwrite. Attach refuses a second file. No typed/voice/model attachment path or confirmation command is delivered.

**Inspect retained attachment / remove Kora copies** reads full inert historical source and same lexical-lines exact citations after restart, including retained Done sessions. Browsing/search never resume a session, renew activity or trigger cleanup. The snapshot does not watch, refresh or re-read the original file.

Removal reviews the exact session/generation, source/revision/item/digest, storage revision and all owned copies, then requires **Confirm: Remove attachment and Kora copies**. Native use is revoked before secure SQLite body/free-page removal and committed-journal verification. Live/Unknown work and uninventoried copies hold completion; inspect durable state after uncertainty. Original files and unrelated/kept sessions/Perpetual grants remain unchanged.

For an **Active** session with a retained file, choose **Replace retained attachment** from its historical viewer. A fresh picker selects the source; review old identity/digest/captured time/storage and inventory revisions alongside new metadata, persistence and copy-removal consequences.
Use **Confirm: replace this exact retained attachment with the newly selected file** within two minutes. Picking even the same path creates a new source identity; old consent and citations are never reused.
Replacement swaps only this one slot, even at the sixteen-file limit, and shows the new captured historical revision only after verifying old owned copies. Old views/search results are retired; no original path is reread automatically and neither original is modified.
Failure/cancellation before commit preserves the exact old durable record; explicitly inspect it again. A committed but uncertified swap holds both bodies unavailable. Inspect shows metadata-only reviewed removal of the held new snapshot and Kora copies, not refreshed content or restored history.
Live/Unknown work, unregistered copies, stale reviews or uncertain storage/journal block completion. There is no automatic replay or version history. See the [exact replacement and recovery contract](../Design/File_And_Folder_Ingestion.md#delivered-reviewed-retained-attachment-replacement---2026-10-10).

This is not managed knowledge or model ingestion. No body/query/excerpt enters history, preferences, logs, speech, clipboard or provider context. Folder attachments, multiple versions, model use, OCR/vector/egress and installed acceptance remain deferred. [Canonical limits, known formats and lifecycle](../Design/File_And_Folder_Ingestion.md#delivered-durable-single-file-session-attachment---2026-10-10).

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

Native **Sessions > Quiet routine notices for this run** affects Work/Maintenance deliveries and notice rows only, until **Turn routine quiet off**, **Reset routine quiet to Off**, or restart. Default is Off; no choice is saved.
Status separates hidden/omitted counts from `RoutineSuppressedNoReplay` sources; suppressed sources are not Presented and consume no fatigue budget. Clear authorizes future new notices, not a backlog.

Failures, Attention, mandatory output, required questions/approvals and authoritative work/status/citations/recovery remain unchanged. There is no quiet typed/voice grammar or model tool in this bounded increment.
The exact event commands below still share their existing broker; routine defer cannot revive quiet-suppressed sources. Scheduled quiet hours, proactive speech and multi-session reminders remain unavailable.

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

In native **Sessions**, use **Optional prerequisite**: keep initial **None**, or explicitly select an exact same-session task from at most 50 already-observed metadata rows. Rows show task/session IDs, observed generation/state/revision and UTC host source; no task body or history is read. Work/question selection never supplies a prerequisite.

Each of the six **Enqueue** buttons applies that one choice through the same route as `[after <task-id>]`. Only exact Succeeded permits later dispatch; pending, failed, cancelled or Unknown can block the FIFO head. Enqueue never runs automatically.
After enqueue, explicitly choose **None** or select a task again. Refresh/session/private/source changes revoke a choice; stale/missing task revisions require refresh/reselection, never silent None fallback.

Typed syntax and arbitrary exact after-task semantics below are unchanged; no revision token is added to typed enqueue. The native picker is a bounded same-session subset, not cross-session search or existing dependency editing.

| Exact syntax | Result |
|---|---|
| `queue help` | Bounded syntax and unavailable scope |
| `queue list <session-id>` | Current entries/revision plus bounded atomic work snapshot, recent receipts, pending question identities, capacity and eligibility reasons |
| `queue status <session-id> <task-id>` | Exact retained work receipt and successful fixed-page snapshot; passive, never reruns the read |
| `queue enqueue <session-id> <generation> <queue-revision> <request-id> <task-id> version [after <task-id>]` | Add one fixed local application-version read; dependency requires an existing exact task |
| `queue enqueue <session-id> <generation> <queue-revision> <request-id> <task-id> capabilities.list [after <task-id>]` | Add the fixed capability-metadata first-page read |
| `queue enqueue <session-id> <generation> <queue-revision> <request-id> <task-id> readiness.get [after <task-id>]` | Add the first page of already-recorded dependency observations, not a fresh check |
| `queue enqueue <session-id> <generation> <queue-revision> <request-id> <task-id> runtime.list [after <task-id>]` | Add the first page of recorded runtime metadata; no probe, inference or setup |
| `queue enqueue <session-id> <generation> <queue-revision> <request-id> <task-id> capabilities.get <exact-current-descriptor-id> [after <task-id>]` | Add exactly one current catalogue descriptor read; exact case-sensitive ID required, no arbitrary JSON |
| `queue enqueue <session-id> <generation> <queue-revision> <request-id> <task-id> runtime.get_status [after <task-id>]` | Add fixed recorded `local.inference` status; no runtime-ID input, probe, inference or setup |
| `queue cancel <session-id> <generation> <queue-revision> <task-id> <entry-revision>` | Cancel only that pending entry, not an admitted worker/effect |
| `queue remove <session-id> <generation> <queue-revision> <task-id> <entry-revision>` | Remove only that pending entry from dispatch, retaining history |
| `queue clear <session-id> <generation> <queue-revision> confirm` | Explicitly confirm clear of this exact pending snapshot; current work/history remain |
| `queue dispatch <session-id> <generation> <queue-revision>` | Manually process up to 32 already enqueued ready fixed reads fairly across eligible sessions |
| `queue pause-status <session-id>` | Passive exact-session current-run pause/hold receipt, reason and work eligibility; no effects |
| `queue pause <session-id> <generation> <queue-revision> <run-id> <pause-revision>` | Block new admissions for this exact session in the current host run; committed reads drain normally |
| `queue resume <session-id> <generation> <queue-revision> <run-id> <pause-revision>` | Withdraw only this exact run's explicit pause after current private authority revalidation; never dispatch |
| `queue move-up\|move-down <session-id> <generation> <queue-revision> <run-id> <pause-revision> <task-id> <entry-revision> <neighbor-id> <neighbor-revision>` | Review exactly one displayed adjacent pending pair; no mutation yet |
| `queue move-confirm <session-id> <generation> <queue-revision> <run-id> <pause-revision> <task-id> <entry-revision> <neighbor-id> <neighbor-revision> <review-id> confirm` | Separately confirm that exact current review through the audited atomic queue service |
| `queue move-cancel <session-id> <review-id>` | Revoke only the exact review; order and draft unchanged |
| `queue requeue-expired-review <session-id> <generation> <queue-revision> <run-id> <pause-revision> <task-id> <entry-revision>` | Review removal of one exact expired current-run pending source and a separately host-issued fresh same-profile tail decision |
| `queue requeue-expired-confirm <session-id> <generation> <queue-revision> <run-id> <pause-revision> <task-id> <entry-revision> <fresh-request-id> <fresh-task-id> <review-id> confirm` | Confirm all and only the complete host-held proposal atomically; fresh IDs must match the review |
| `queue requeue-expired-discard <session-id> <review-id>` | Revoke the review and unadmitted fresh IDs without queue mutation |
| `queue cancel-selected-review <session-id> <generation> <queue-revision> <run-id> <pause-revision> <task-id>:<entry-revision>[,<task-id>:<entry-revision>...]` | Review the complete exact pending subset, in displayed dispatch order |
| `queue cancel-selected-confirm <session-id> <generation> <queue-revision> <run-id> <pause-revision> <task-id>:<entry-revision>[,...] <review-id> confirm` | Cancel all and only that host-reviewed exact pending subset atomically |
| `queue cancel-selected-discard <session-id> <review-id>` | Revoke review without writes; pending selection and draft unchanged |

**Review selected pending cancellation** uses a separate pending-only multi-selection, never the current work/question selection. Select one to ten current-run pending entries explicitly; no hidden/default select-all. Review shows every ID/revision/profile in current dispatch order with original deadlines and full count; **Cancel these pending entries only** is the separate confirmation.

New selected-set syntax uses canonical lowercase D GUIDs and canonical decimal revisions. Repeat the full ordered member list and every displayed authority revision plus the host-issued review ID when confirming.
Empty/duplicate/over-bound/unlisted/stale members, changed source/private epochs or queue mutations refuse the entire set; refresh and review again. Existing `cancel`, `remove` and `clear` syntax is unchanged.

Input remains 1024 UTF-8 bytes; complete review/result is bounded to 64 KiB, with explicit refusal rather than partial projection. All six fixed profiles and exactly expired pending rows are supported. Lowering configured capacity does not reduce the maximum existing selectable set or evict entries. Cancel review keeps selection and draft, without mutation or deadline/activity renewal.

Only selected rows/tasks become retained Cancelled history. Unselected relative dispatch order and original metadata/results remain unchanged. Dependents are retained and block if their prerequisite was cancelled; no descendant scan/cascade is claimed. Pause/hold, slots and current callbacks remain unchanged; no automatic dispatch/resume, worker/effect termination or deletion authority is added.

Required requested/terminal typed audit, exact member receipt and readback precede successful confirmation under the existing short physical commit fence. Audit/storage/cancellation/commit uncertainty holds admission for recovery; known commit is not undone by failed delivery. Closure/selection changes revoke proposals. Previous-run/Unknown/nonpending entries refuse, with no restart replay.

Use canonical nonempty D GUIDs and the exact displayed positive session/entry
revisions. Queue revision is the displayed committed snapshot revision; zero
is valid only before the first queue mutation. For typed enqueue, supply fresh
request/task GUIDs; the six explicit native **Enqueue** buttons generate them. Duplicate
IDs, names, unknown/stale revisions, Done/disposed sessions and full queues
fail closed. The selected session's ID is visible and never inferred from a
name, model response or window title.

Only these six closed profiles are accepted. The original four inputs remain host-owned `{}`,
first page/default count six. `capabilities.get` requires one exact current catalogue descriptor ID;
native selection has no implicit target and resets after success/stale selection. `runtime.get_status`
always selects recorded `local.inference`; supplying any runtime ID is invalid. No arbitrary JSON,
parameters, count/offset or refresh controls are accepted. Each result is limited to six records and 4096 serialized
UTF-8 bytes. **Inspect selected work** or exact `queue status` shows a retained
successful page snapshot, not current health. Large manual batches may omit page
data with `resultAvailableByExactStatus: true`; inspect that exact task instead.
Work/All work remain metadata-only. Failures, denial, cancellation, stale private
admission or expired active budgets never deliver successful data. The two targeted reads return only one exact matching descriptor or local recorded runtime, never live qualification. Requeue preserves that exact target with fresh IDs, not replay or retargeting.

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
Dispatch is manual, arrival FIFO unless explicitly reordered within a session, and fair across ready sessions;
the invoking session gains no priority. Default capacity is ten pending entries
per session and one admitted task per session. The shipped host uses one
global slot; only the fixed local-read implementation permits a host
limit of one or two.

**Pause this session's new dispatch** and **Resume this session's dispatch eligibility** use the displayed exact generation, queue revision, run ID and pause revision from **Refresh selected work**/`queue list`/`queue pause-status`.
Pause does not suspend/cancel active work, mark Done, clear entries, consume grants, change FIFO/fairness or deadlines, or renew meaningful activity/retention. It is not a saved preference; fresh runs start unpaused.
Pause can be acknowledged while an admitted read is still running: no later admission from that session can commit after acknowledgement, while unrelated eligible sessions remain fair candidates. Resume does not start a batch; use a separate manual dispatch decision.
Unknown/unconfirmed audit, storage, cancellation or private-epoch state holds admission with an explicit recovery reason, not a claimed rollback/unpause. A private epoch change cannot restore old queued eligibility; inspect/remove/requeue explicitly. Previous-run pending work remains Interrupted, never revived by resume.
Quiet routine notices and queue pause are independent. General workers/providers, autoscheduling and current-task cancellation remain unavailable; a pause receipt is not a physical-stop receipt.

**Review Move up/down** selects only the displayed previous/next pending neighbor in this exact session. The first/last entry has no move in that direction, with an explicit reason. Review shows both profiles, immutable IDs/revisions, current/proposed order and original deadlines.
**Confirm exact adjacent move** is separate; **Cancel move review** leaves order and draft unchanged. Typed/activated confirmation must repeat every exact binding plus the returned review ID and `confirm`. A fresh review replaces the prior volatile review, never accumulates replayable authority.

Concurrent admission/removal/enqueue/reorder, changed session/run/private control, selection or closed authority makes confirmation stale: refresh and review again. No name-based retargeting, cross-session shuffle, arbitrary position or automatic reprioritization is accepted.
Pending prerequisite inversion and expired/private-retired/previous-run/nonpending pairs refuse; remove/requeue requires a separate decision. Unmet cross-session dependencies still block the chosen head.

Only pending dispatch order changes. Original arrival, enqueue/expiry, active deadlines, results, history and fairness remain intact; new enqueue follows the current pending order. No pause/resume/dispatch or activity renewal follows a move.
Running callbacks remain independent; previous-run Interrupted/Unknown work never replays. Audit/storage/delivery uncertainty holds admission with explicit recovery, never an optimistic success-shaped list.
Queue snapshots include at most two latest order lineages with `omittedOrders`; older exact move receipts remain in authoritative audit/history, not fabricated current ordering.

## Fixed queue settings

Expired replacement is available in native Sessions as **Review expired removal and NEW tail task**, followed by separate exact confirmation or cancellation. The review shows original IDs/profile/dependency/deadlines and fresh host-issued IDs, captured pending lifetime and deadline. No arguments, name inference, automatic dispatch or probes are accepted.

Only expired stored Pending entries with no dispatch order in this active session/current run and unchanged original private admission qualify. Stale queue/order/pause/settings/source/entry/session revisions reject the whole proposal. One replacement frees one slot atomically, never evicts, and cannot exceed lowered capacity. Original history/deadlines remain; old task is Cancelled with Removed queue outcome, new task is IntentRecorded/Pending at tail.

Prerequisite IDs are preserved and revalidated; dependents on the removed task remain blocked, without cascade or retarget. Cancel admits nothing. Unknown commit/delivery holds admission and requires exact inspection, not retry. Restart never replays either entry; new active budget is captured only at normal later manual dispatch. See [bounded replacement contract](../Design/Work_Management.md#delivered-reviewed-expired-pending-removal-and-fresh-tail-decision---2026-10-12).

Native Settings > Sessions and exact typed/current-name activated input share
these future-only controls:

| Option | Range | Default/reset | Takes effect |
|---|---|---|---|
| `queue.pending-per-session` | Canonical integer 1-10 | 10 | Future enqueues; no eviction |
| `queue.execution-slots` | Canonical integer 1-2 | 1 | Future fixed synchronous read admissions; no cancellation |
| `queue.pending-lifetime-minutes` | Canonical integer 1-120 minutes | 30 | New enqueues only; existing deadlines unchanged |
| `queue.active-budget-minutes` | Canonical integer 1-60 minutes | 5 | Future admissions, including old pending entries; existing active deadlines unchanged |

Use `list queue settings`, `get <id>`, `status <id>`,
`set <id> to <integer>` or `reset <id>`. List reports schema 3 and all four
descriptors. No signs, leading zeros, fractions, extra words or apply-now.
Native Refresh is required before each unchanged visible draft Save/Reset.

Protected/Unknown original-voice writes and stale/held preferences refuse.
Inspect/repair receipts before fresh Refresh; it cannot clear unconfirmed writes.
See [queue settings and recovery](settings.md#sessions-fixed-read-only-local-version-queue).

The [queue workflow](#deterministic-local-version-queue) remains manual.
Expired heads block later work until removed; a failed batch needs a fresh
dispatch decision. At/equal-to active deadlines, late results cannot be success.
Pre-dispatch questions consume neither slot nor active budget. Privacy/call/
ownership retirement requires inspect/remove/requeue; restart yields
Interrupted/Unknown status, never replay. General workers/effects/resource
leases and real two-slot provider/hardware qualification remain unavailable.

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
| `preview file` | Native picker then metadata-only review of one local UTF-8 text/Markdown file |
| `clear file preview` | Discard volatile review/revision, never the original |
| `search file` / `inspect file` | Focus admitted native query/inspection; no arguments or implicit source |
| `preview folder` | Native picker and complete immediate-file inventory review |
| `search folder` / `inspect folder` | Focus exact admitted folder retrieval |
| `clear folder preview` | Discard volatile folder review/revision |
| `refresh file` / `refresh folder` | Retire old preview, fresh exact metadata review and separate native confirmation |

Only separate native confirmation authorizes content reads; commands, paths,
IDs and document instructions cannot confirm. Reviews expire after two minutes.
Refresh requires the same original physical identity; unsafe/replaced/missing
sources need the picker again, never silent rebinding.

| Limit | Value |
|---|---|
| File | Fixed drive; strict UTF-8 `.txt`, `.md`, `.markdown`; 256 KiB original bytes including BOM |
| Path | 240 characters / 32 components |
| Folder | 1-32 immediate files, 1 MiB combined; no recursion or partial admission |
| Native query | 256 UTF-16 characters / 512 UTF-8 bytes; 32 unique terms, 64 characters/term |
| Results | Eight exact excerpts / 16 KiB UTF-8; each at most 2,048 UTF-16 characters / 128 lines |

Queries belong only in native search, not conversation. Results carry exact
source/revision/item/digest and end-exclusive line/column/range citations.
Search never rereads the path. Privacy/ownership/call/source/query retirement
suppresses late results; clear/cancel/close never modifies originals.
No clipboard, speech/history, model/egress, watcher, vector index or durable
folder attachment is added. Durable single-file attachments have separate
consent. See [native file workflow](windows-and-tray.md#native-local-file-inspection)
and [privacy](privacy-safety-and-logs.md#local-file-inspection).

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

### Retrieve an explicit web page

Say or type:

- `get web page https://example.com/page`

This is an original-user request for that exact absolute HTTP or HTTPS address.
Kora retrieves only public-network UTF-8 plain text or HTML, follows at most
five redirects, and rechecks DNS and authorization for every redirect. It does
not send cookies, ambient credentials, proxy credentials, referrers, or a model
conversation. HTML is returned as bounded normalized text; scripts, styles,
markup and external resources are not executed or loaded.

After success, select **Open exact web-result details** in the actual response. This opens the same immutable, volatile native plain-text result, not the website. Search, exact source, reopen and close perform no additional network request or navigation.
The viewer shares the existing eight-window limit and private copy requires explicit disclosure confirmation.

Complete source provenance shows requested/final addresses, media type, redirect count, retrieval time, truncation and the SHA-256 of **exact returned normalized text**, not original HTML or current server content.
Limits are 256 KiB raw content / 60 KiB normalized text / 64 KiB serialized tool result; complete native source including metadata is bounded to 256 KiB UTF-8 with explicit failure, not further truncation. Empty successful text stays empty.

Only the current result is retained in memory. A later response, dismiss/response close, cancellation, privacy/ownership/call/control change or disposal retires its reference and viewer/copy access; an old button never selects a newer page.
Closing details alone changes only presentation and allows reopening while that source is still current. No page content is saved, spoken, captioned, logged, exported or sent to a model.

The canonical future model tool is `network.get_web_page`, but its model-facing
descriptor remains unavailable until Kora's parameterized tool/result loop and
durable approval resumption are qualified. Preapproved address patterns will
apply through the normal grant model when that route becomes available.

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

Canonical integer 0-100 (no signs, padding, fractions, percent suffix or leading zeros); default/reset 100. Zero prevents synthesis and retains full visual output. Changes never alter global/call volume, test audio, reopen capture or replay output.
Current-name activated input is supported; protected/Unknown original-voice
writes refuse. Input is at most 1,024 UTF-8 bytes including the prefix and
rejects controls; complete result at most 64 KiB.
See [controls and option-specific recovery](settings.md#kora-playback-volume) and
[shared preference admission](settings.md#preference-changes-and-recovery).

### Inspect or change Windows-native speech rate

- **list rate settings**
- **get speech.windows-rate**
- **status speech.windows-rate**
- **set speech.windows-rate to -10**
- **set speech.windows-rate to 0**
- **set speech.windows-rate to 10**
- **reset speech.windows-rate**

Canonical integer **-10..10**, normal/default/reset **0**. Negative nonzero
signs only; no plus, negative zero, padding, fractions or suffix. Windows
provider-native rate, not percentage or words/minute.
**Kokoro is unsupported and unchanged**; no provider switch, trial or replay.
Current-name activated input is supported; protected/Unknown original-voice
writes refuse. Input is at most 1,024 UTF-8 bytes including the prefix and
rejects controls; complete result at most 64 KiB.
See [controls and option-specific recovery](settings.md#windows-native-speech-rate) and
[shared preference admission](settings.md#preference-changes-and-recovery).

### Inspect or change the device-default response mode

- **list response settings**
- **get responses.default-mode**
- **status responses.default-mode**
- **set responses.default-mode to Hybrid**
- **set responses.default-mode to VoiceOnly**
- **set responses.default-mode to VisualOnly**
- **reset responses.default-mode**

Exact case-insensitive Hybrid / VoiceOnly / VisualOnly; default/reset Hybrid. Save changes only the device default, not queue/task overrides or mandatory call/privacy/full-visual recovery. No autoplay, replay or capture.
Current-name activated input is supported; protected/Unknown original-voice
writes refuse. Input is at most 1,024 UTF-8 bytes including the prefix and
rejects controls; complete result at most 64 KiB.
See [controls and option-specific recovery](settings.md#device-default) and
[shared preference admission](settings.md#preference-changes-and-recovery).

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

**Tray > Return to current question** returns to that same still-live original version wait after Close/Escape, restoring only its committed draft and current revision. Unsaved edits/review clear on close. The original five-minute expiry continues while hidden; return creates no new query/question/grant or dispatch and never extends activity.

An already-open exact card activates; Sessions selection cannot retarget it.

Storage/audit failure never reports a successful query. Recovery becomes Unavailable after expiry, privacy/ownership loss, changed authority, cancellation, original completion or restart: inspect read-only status and start a fresh review after resolving the blocker. Kora never automatically retries or rehydrates old work.
Explicit cancellation atomically commits the original task and question as Cancelled before dispatch. Unfinished intent-only work recovers as Interrupted; dispatched uncertain work remains Unknown, without replay. This bounded route does not
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
## Run skills and artifacts

### Edit an owned instruction or literal prompt without a model

Type `instructions editor` or `prompts editor`, or say the same control prefixed
with the current assistant name. Both focus the same native editor with explicit
Instruction/Prompt kind selection. `list`, `create <id>`, `edit <id>`,
`inspect <id>`, `save`, `copy <new-id>`, `reload`, `discard`, `delete`, `activate`, `disable`, `stop-use` and `cancel`
are the exact controls after either plural kind name. Only create/edit/inspect/copy
accept one canonical file ID; other controls target the open original.
**Disable reviewed use** / `instructions disable` or `prompts stop-use` separately confirms the clean inspected canonical native v1 owned source/kind/digest/native revision. It durably withdraws new selections without writing/deleting/unregistering the source.
Disabled files stay inspectable/editable. Dirty drafts remain intact with an explicit Save/Reload prerequisite; current-name activated controls focus the same native confirmation, never voice-only approval.
Already captured reasoning keeps immutable content; pending unadmitted questions/action choices expire without dispatch or retargeting. Mandatory access/privacy/action/egress checks remain. Reload/restart/restored bytes cannot re-enable use: require fresh exact review. Failed audit/state readback reports held/unavailable recovery, not success or rollback.
No paths or body dictation are accepted. Save/Delete and separate exact-use review
remain native; detected conflicts offer Reload/Overwrite/Cancel. See
[native editor and limits](instruction-editor.md).
`copy <new-id>` reviews a same-kind supported native v1 owned original and its current draft, then creates a separate disabled owned file without overwriting. The original source/draft stay open and untouched; independently inspect/review the destination before explicit use. Profile-original editing and profile forks remain unavailable.

### Explicitly select a saved revision

Kora can apply a bundled or disk-backed skill, instruction, prompt, or agent
artifact to a local-model request from the command box or activated voice.

**Type a slash command**

Type `/` in the command box to open a dropdown of every available bundled and
disk-backed artifact. Continue typing to filter by command name, or type a
kind such as `/skill `, `/prompt `, `/instruction `, or `/agent `. Use Up/Down and Enter
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
/agent review-changes inspect this patch
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

Kora loads compatible files from its own roaming `Skills`,
`Instructions`, `Prompts`, and `Agents` folders and the existing VS Code or VS
Code Insiders customization locations. In **Settings > Artifacts**, you can
maintain up to sixteen additional absolute directories for each artifact kind.
These settings are separate from the read-only shared skill source
registrations below; registering a shared source does not add it to the model
artifact catalogue.

Disk skills use `SKILL.md`; prompts use `*.prompt.md`; instructions use
`*.instructions.md`; agents use `*.agent.md`. Default and additional
directories use the same bounded UTF-8, YAML frontmatter, traversal, naming,
digest, and catalogue-conflict rules. Skills marked `user-invocable: false` do
not appear. Conflicting command names, IDs, or spoken names fail closed instead
of choosing one source silently. Kora watches default and configured locations
and automatically publishes a complete valid replacement catalogue after
changes. Invalid or partially written content reports reload failure; retained
historical catalogue data does not keep managed owned instructions/prompts usable.
New/changed managed revisions require separate current-run exact review and native
freshness before explicit selection; old bytes do not restore withdrawn use.
Managed prompts are literal saved user-request content, not system guidance.
No restart is needed. Loading an agent
artifact applies its declarative instructions; it does not start an autonomous
agent runtime.

If a selected artifact changes while local reasoning, a clarification, or an
action approval is pending, Kora retires that interaction. Start a new request
to use the new revision. Action proposals originating from artifact
instructions require a fresh single-use approval for the current content
digest. Existing session/always action grants are not reused, and an
artifact-originated proposal cannot create a reusable grant.

**MCP server configuration files**

Choose `McpServerConfiguration` in **Settings > Artifacts** to add an absolute
JSON file rather than a directory. Kora also inspects its roaming
`Mcp\mcp.json`, VS Code/VS Code Insiders `User\mcp.json`, and Claude Desktop
`claude_desktop_config.json` when present. Supported files contain exactly one
`servers` or `mcpServers` object. Each entry must declare either a non-empty
stdio `command` or an HTTP/HTTPS `url`.

The settings page shows only the server name, transport, source file name, and
whether it is included at application level. It never displays or sends
commands, arguments, URLs, headers, environment values, or credentials to a
model. Include/exclude is audited and fail-closed, but does not connect to a
server or authorize any capability.

Live MCP capability discovery and invocation are not delivered yet. When they
are added, app inclusion will be intersected with each session's explicit
server/capability selection. Voice, typed, and UI requests will expose only
freshly discovered and admitted capability descriptions to the selected
model; every proposed call will still require the ordinary host permission,
approval, egress, audit, cancellation, and provenance checks.

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
