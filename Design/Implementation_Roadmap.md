# Implementation Status and Delivery Roadmap

Status: source-backed implementation inventory and proposed delivery order;
R01 policy reconciliation approved; R02 Windows storage direction and
local-inference/runtime partial outcomes and distribution delivery plan recorded;
full profile-storage/session integration, runtime admission, reference-floor
inference qualification and complete production/acceptance proof remain open.
The current merged inventory below supersedes older unversioned gap statements;
the dated original/rebase receipts remain evidence of their own exact snapshots.
Bootstrap inventory reviewed on 2026-10-05 against repository revision `e4688c3`;
R02 storage evidence reviewed against `59d1eb9` and incorporated into the contracts below.
Local-inference follow-up reviewed against `1bb6dc3` and its recorded evidence.
Supplemented on 2026-10-05 with the R02 Windows containment snapshot at `a74bb3a`;
runtime feasibility evidence updated against `e0af3ea`;
none of these experiments is composed into the application.
Distribution-only addendum reviewed against R01 revision `7d5e6a3` and the
[R02 distribution experiment](../experiments/r02-distribution-proof/README.md);
this does not reclassify unrelated R02 branches or claim release acceptance.
Update this baseline and the evidence below when implementation changes.

## Delivery and Qualification Dependency Policy

### R14 authoritative selected-session work increment - 2026-10-09

After merged durable history #114 and deterministic scheduler #116, the
[native Sessions workspace](UI_Workspace_And_Windows.md#delivered-authoritative-sessions-work-surface---2026-10-09)
now coordinates a bounded All/Active/Done list with selected authoritative
queued/current/waiting/blocked/cancelled/Unknown work and exactly bound pending
question metadata. One `SessionWorkSnapshot` read transaction exposes stable
IDs/generations/revisions/FIFO order, observation time, exact admission deadlines,
pending capacity, shared-policy dispatch eligibility and explicit gaps/recovery.
Native inspect/cancel/remove/confirmed-clear/manual-dispatch routes reuse the
already admitted exact services; stale revision/selection/session/epoch changes
refuse. Background refresh remains passive, focus-preserving and cancelled/
detached on close. No activity extension, priority change, retargeting, grant
consumption, dispatch or execution authority arises from browsing.

Maintained core policy, Application snapshot/command admission and Windows
storage/headless-native tests cover refresh, concurrent queue revision conflicts,
question/history coexistence, terminal/restart/Unknown records, disposed subjects,
cancellation/privacy closure, focus/accessibility names and no passive writes/
egress/content logging. General conversation/composer/artifacts, model work
management, Ask Evidence, export, effects/workers/providers/scripts and installed
screen-reader/DPI acceptance remain open; this does not close R12/R13/R14/A3/A4
or qualify concurrent inference/effects. No schema migration or replay is added.

Experiment disposition: **retain all R02 experiments unchanged**. These
maintained authority/native tests supplement, not replace, storage recovery/
capacity/copy/crypto consumers and runtime/lifecycle/containment/speech/
distribution or real installed-native evidence. No harness is promoted,
deleted, archived or reclassified as a production executor by this increment.

### R13 deterministic local-version queue increment - 2026-10-09

The bounded deterministic host core now adds exact-ID/native per-session
pending queues over the already qualified, synchronous `application.get_version`
action only. Schema v5 retains schema-v4 ordered history and consolidated
v3 authority; queue/task transitions and typed authority audit commit under
one private lease/transaction. No schema migration or restart dispatches work.

Delivered scope: capacity 10 pending entries per session; host-configured
1 slot by default (the fixed read-only implementation accepts 1–2);
one admitted current task per session; FIFO heads with deterministic
least-recent-admission fairness; exact request/task/session IDs, session generation,
entry revision and queue snapshot revision; exact enqueue/list/status,
pre-admission cancel/remove, confirmed pending clear and explicit manual
bounded fair dispatch. Dependencies require the exact prerequisite's durable
Succeeded receipt. Unknown/unclassified current effects quarantine their
session and dependent work, not unrelated ready sessions.

The shipped policy is **manual dispatch**, not an ambient background executor:
one explicit dispatch processes at most 32 already enqueued current-run reads.
Pending eligibility expires after 30 minutes; expiry is visible and blocks
the head until explicitly removed/cleared, never silently evicts history.
The active five-minute budget begins at admission and rejects a late successful
read receipt. This synchronous fixed action creates no runtime question wait;
existing admitted pre-dispatch question waits acquire no queue slot or active
budget and retain their separate gateway/expiry. No effect-worker termination,
general wait continuation or provider/hardware deadline claim follows.

Native selection, typed input and current-name activated input share
`SessionQueueService` and the durable store. No model/network/audio is needed.
Privacy, call and ownership epochs, exact revisions/generations, existing
session resolution and commit-boundary admission remain mandatory. Cancel
after admission, stale callbacks, Done/Removed subjects, full queues, unknown
profiles, missing storage/audit and changed admission fail closed. Storage
uncertainty is not a successful receipt or permission to retry.

Restart projects pending work as Interrupted and dispatched work as Unknown;
startup task recovery persists those outcomes without queue replay. Fresh
explicit enqueue is required after interruption. Passive reads do not extend
activity; independent Perpetual grants, ordered citations/disposition redaction
and volatile preview/skill/caption boundaries remain unchanged.

This increment does **not** close full R12/R13/A3 acceptance or enable management
inference, external/local reasoning, scripts, power, write connectors, names as
authority, arbitrary resource leases or effects. Two fixed local reads in tests
are not two-slot worker/provider/hardware qualification. All broader dispatch
descriptors remain unavailable under their existing capability gates.
Experiment disposition: retain every R02 experiment, including unique
runtime/lifecycle and containment evidence; no experiment is promoted into
production or deleted by this scheduler increment.

The [canonical three-tier policy](Acceptance_Criteria.md#three-tier-qualification-policy)
separates normal repository merges, capability/profile qualification and final
RC qualification. No experiment or aggregate R02/A0-A4 completion is a
repository-wide prerequisite. Bounded feature work can implement, test and merge
independently of unrelated inference, speech, containment or hosted-account
trials. Directly applicable security/privacy/data-integrity checks and critical
fixtures remain mandatory; unavailable capabilities cannot gain authority.

### Reading Needs

**Needs** is scoped to the behavior being delivered, not completion of every
feature and proof in a referenced package. Distinguish:

- **I - Implementation dependency:** a consumed contract, service or policy
  foundation must exist and have its applicable maintained checks. A delivered
  bounded subset can satisfy I without closing the entire Rxx package.
- **E - Enablement evidence:** exact-capability/profile proof required before
  enabling, advertising, packaging as available or materially changing that
  path. It does not block scaffolding, disabled-path implementation or unrelated
  bounded feature merges.
- **Q - RC evidence:** integrated/final-byte/environment sign-off for the
  explicit enabled-capability manifest. It does not block normal development.

Unless explicitly labelled E/Q, package references denote I for the consumed
subset. Actual proof references (RT1/RT2/MG1/PV1, L1-L6, W1-W4, hardware/
acoustic or installed acceptance) denote E for their profile and Q when included
in the RC. Remaining proof substep tables sequence qualification, not general
implementation. Completing one experiment never qualifies another branch.
Examples: exact local commands/read-only discovery do not wait for L1-L5 or
RT2/PV1; clipboard preview does not wait for clipboard answering; deterministic
session controls do not wait for hosted management; speech preferences do not
wait for production wake. Only their actually consumed contracts and directly
affected safety/regression checks constrain those bounded deliveries.

### R12 Bounded Session Retention Delivered - 2026-10-09

Delivered independently of blocked R11: schema-v6 session retention on one
durable last-meaningful-activity clock; default **24-hour archive / 30-day
deletion** and typed, atomic, device-local future-only preferences in native
Settings > Sessions. Archive/delete use the same timestamp, not creation or
archive time. Accepted original-user work, final answers, real task progress and
explicit resume advance it monotonically; drafts, rename, passive retrieval and
configuration/control bookkeeping do not. Older schemas receive an explicit
conservative migration baseline, not invented activity chronology.

Host-only maintenance runs before startup presentation, before session reads and
on an owned one-minute timer, at most 32 candidates per batch. Live, dispatched,
Unknown/unresolved work and current-run control authorities hold rather than
being abandoned; holds cannot crowd idle content out of the batch. An audited,
exact-generation host-only Perpetual session exemption has no marking UI.
Independent Perpetual grants and unrelated sessions/independent artifact owners
survive. Sources and matching native/response presentation are revoked before
rewrite; inactive/Removed authorities reject late publication and appends.

Deletion removes owned task/event/wait/run/question/observation/grant/metadata
and ordered-history/terminal queue content, legacy task copies, authenticated artifacts and
staging. SQLite secure-delete clears row/index/free-page content and committed
PERSIST journals are verified empty, including a legacy no-op journal. Only
content-free exact-ID tombstones/redacted gaps and independent required audit
remain. Final inventory acceptance is separate from authority revocation;
interrupted acceptance stays unaccepted and retries storage maintenance, never
execution. Unknown/corrupt ownership, uninventoried files/backup directories and
uncertain key publication fail closed. No delivered managed-backup publisher
exists; this does not guess ownership or claim removal of arbitrary backups.
No forensic/media erase, provider copy, user export or full A3 claim is made.

Validation after integration onto `33b90f21` (#116): locked solution restore and
root Release build, **0 warnings / 0 errors**; no dependency or repository feed
configuration change was necessary. Core **1,026**, Application **2,854**,
Tools **69**, Definitions **6**, Windows **1,205** tests passed, all **0 failed /
0 skipped**. The unchanged portable
coverage gate passes **100% lines / 100% branches**. Focused tests prove actual
plaintext sentinel absence from SQLite/journal and actual owned artifact/staging
removal, independent-owner/Perpetual preservation, cancellation, malformed saved
state, audit/receipt/confirmation failures, migration, interrupted acceptance
and bounded hold fairness, v5 queue migration, queued/Unknown holds,
terminal queue inventory deletion, passive/control clock stability and
late-callback/no-replay restart rejection. This is maintained deterministic evidence, not
installed/native accessibility, filesystem crash/power-loss or RC qualification.

Full R12 remains partial: full composer/conversation bodies, search/model
reasoning, general execution/effect cancellation and broader per-session work/queue
integration remain open. R11's embedded skill execution/runner remains blocked
and was neither consumed nor awaited. The unrelated stale call-policy branch
was not merged/rebased or used. See [configuration](User_Configuration.md#r12-bounded-session-retention-delivered---2026-10-09)
and [storage/lifecycle contract](Interaction_And_Sessions.md#r12-bounded-session-retention-delivered---2026-10-09).

Integration with #114/#116 preserves the delivered ordered history and fixed
local-version scheduling receipts above. Authoritative migration order is
v3 task authority → v4 history → v5 queue → v6 retention. Pending/running/Unknown
work holds maintenance; terminal queue rows join the inventoried deletion and
required audits remain independently retained against exact Removed tombstones.
Accepted queue work and real dispatch/final receipts renew meaningful activity;
queue controls and passive reads do not. Restart still projects Interrupted/
Unknown work without replay and rejects stale callbacks. This supersedes only
older statements that the bounded fixed local-version queue was absent; broader
effects, workers, full conversation, managed backups and RC acceptance remain open.
Confirmed retention preferences are loaded before storage clock migration;
invalid/unconfirmed saved state cannot seed clocks with fallback defaults.

### R12/R14 bounded ordered interaction history - 2026-10-09

Delivered subset: one authoritative typed event projection and schema-v4
per-session sequence/history store over existing task/question/required-audit
authority. Question snapshots/final answers, decision metadata and task-state
receipts are atomically projected from their real host commits. No bootstrap
message or response body is invented. Immutable source/event/session IDs,
revisions/generation and provenance digests survive restart; migrated current
snapshots have explicit baseline/gap semantics rather than fabricated chronology.
Exact native and typed/current-name activated history/get reads share one host
service, require private admission, preserve bounded snapshot paging and write
no intent, activity extension, lifecycle transition or replay. Done stays
readable; logical disposition atomically redacts history content while keeping
exact citations readable for a known Removed ID. Late/disposed work is denied.

This consumes only the already delivered R04/R05 authority and #102 disposition
subset under the three-tier policy. It does not depend on inference, queues,
scheduler or remote/runtime qualification. R04/R05/R12/R14 remain partial:
full composer/conversation bodies, search/model history reasoning, Ask Evidence,
broad export, general effects/work scheduling, managed-copy deletion/retention
and real native/accessibility acceptance remain unavailable/open.
Shared-profile skill inspection and immutable local file previews remain
volatile control/inspection data; their text/paths/provenance are never admitted
to history or model context. History cannot reconstruct captions or authorize
playback; only fresh independently admitted actual playback creates captions.

Maintained deterministic tests cover ordering/restart, exact cross-session
retrieval, generation/snapshot paging, concurrent/stale answers, cancellation,
v3 migration interruption, unknown/corrupt schemas/projections, passive
admission and disposition redaction/provenance. Existing lower-schema and
process-kill tests remain applicable. This is not installed/power-loss or full
R12/A3 acceptance. Storage experiments retain unique recovery/capacity/copy/
native evidence; no experiment file is deleted or archived.
See [the authoritative contract](Interaction_And_Sessions.md#delivered-bounded-ordered-interaction-history---2026-10-09)
and [native user workflow](../docs/windows-and-tray.md#bounded-passive-interaction-history).

### Experiment Disposition Inventory

This inventory owns current disposition; dated delivery/equivalence receipts
below and in the deferred register remain historical evidence. **No executable
experiment is deleted or archived by this documentation-policy change.**
Experiments are not default-CI suites or production contracts. Maintained
production/integration assertions belong in normal affected-change validation;
consented environment qualification remains opt-in outside default CI.

| Experiment | Disposition and unique risk reduction | Migration/archive or rerun trigger |
|---|---|---|
| [Local inference](../experiments/r02-local-inference-proof/README.md) | Retain as opt-in, environment-qualified capability/RC harness. Actual answer quality, CPU-floor/context/resource budgets, server cessation and independently blocked egress are not replaced by client mocks. | Run for offered inference profiles and affected model/runtime/envelope/hardware claims. Move reusable deterministic policy assertions into maintained tests; keep unique hardware/offline trials and exact receipts. |
| [Containment](../experiments/r02-containment-proof/README.md) | Retain as opt-in, environment-qualified capability/RC harness. Actual token/ACL/child/network/lifetime behavior and attributable denial distinguish OS enforcement from requested policy or timeouts. | Run only for enabled or materially changed worker/deployment profiles and their RC inclusion, with separate lab/privilege approval. Preserve rejected profiles and unknown effects; no network timeout becomes denial. |
| [Speech](../experiments/r02-speech-proof/README.md) | Retain as opt-in, environment-qualified capability/RC harness. Physical acoustics, playback rejection, reference-floor latency and packaged-host capture/privacy transitions are unique. | Run for affected offered speech/wake/provider/device profiles, not unrelated settings or session work. Maintain deterministic consent/generation/cancellation regressions in production tests; do not infer acoustic acceptance from them. |
| [Runtime/provider (Node)](../experiments/r02-runtime-proof/README.md) | Migrate applicable all-status mediation, denial, cancellation and serialization assertions into maintained production adapter/integration tests; retain hook-only failure witnesses and candidate receipts. Node comparisons do not qualify .NET. | Archive executable harness only after applicable assertions have exact maintained coverage and remaining candidate comparisons are explicitly disposed. Unproved native/provider paths stay unavailable. |
| [Runtime lifecycle (RT2)](../experiments/r02-runtime-lifecycle-proof/README.md) | Migrate lifecycle observation, event-loss/short-lived-path controls, diagnostic/persistence and quiescence assertions into maintained runtime integration tests. Its bounded pass with all-path admission Blocked is not acceptance. | Archive executable harness after exact maintained equivalents cover required controls and observer limitations are resolved or the unsupported profile is explicitly excluded. Preserve actual native observation procedures/receipts, not a fake-only replacement. |
| [Storage](../experiments/r02-storage-proof/README.md) | Migrate applicable recovery, migration/source preservation, capacity, retention/deletion and managed artifact/backup-copy integrity assertions into maintained tests against the approved standard-SQLite adapters. Existing interruption/metadata/retention equivalence maps are partial, not a whole-harness replacement. | Archive executable harness only after applicable gaps are covered and shared candidate/crypto/native consumers are resolved. Superseded encryption/rekey comparisons are historical unless opt-in R30 is pursued; they never restore encryption as an R04 prerequisite. |
| [.NET control (RT1)](../experiments/r02-dotnet-control-proof/README.md) | Migrate exact-profile final-request, all-status result, denied side-effect/marker and cancellation assertions into maintained production runtime integration tests. Source-built and released bytes remain distinct evidence. | Archive executable harness after exact production-profile equivalence and failure-witness coverage; changed SDK/native/request mechanisms repeat affected tests/observations before exposure. |
| [.NET management (MG1)](../experiments/r02-dotnet-management-proof/README.md) | Migrate byte/deadline, independent identity/admission, no-retry and Unknown/quarantine assertions into maintained management/runtime integration tests. A synthetic envelope pass is not scheduler, durable authority or account eligibility. | Archive executable harness after exact maintained equivalence for applicable management behavior; real account/cost and native-lifecycle gaps remain scoped enablement/RC evidence. Deterministic local management does not depend on hosted trials. |
| [Distribution](../experiments/r02-distribution-proof/README.md) | Historical receipts; superseded executables already retired with maintained `eng`/installer equivalents. NSIS receipts are not WiX or current installed acceptance. | Reopen research only when distribution mechanisms materially change. Current packaging regressions remain maintained; final installed-byte qualification applies to the manifest's delivered profiles, not a rerun of old NSIS. |
| [W2 dependency mechanism](../experiments/r02-w2-dependency-proof/README.md) | Historical decision/failure receipts for rejected strict dependency mechanisms and approved best-effort transitive tracking; retain remaining code without promoting it to a recurring suite. | Reopen if dependency discovery, fixed-action/helper scope, protected runtime resolution or worker mechanisms materially change. Maintain production manifest/review/hash/invalidation/receipt regressions as implemented; unresolved W1/W3/W4/control-API proofs remain separate. |

Before archiving/removing any executable, map every applicable assertion to its
maintained test and production owner, preserve mandatory critical fixtures and
failure witnesses, run exact equivalence checks, and verify code/build/CI/script/
documentation consumers and references. Preserve historical receipts, identities
and unique lab procedures. If equivalence is partial or shared consumers remain,
record migration/archive triggers rather than delete code; obsolete comparisons
need explicit historical disposition, not invented production equivalence.

### Cost Versus Unique Risk Reduction

Repeated pinned-runtime acquisition, model residency changes, physical audio,
privileged network/containment observation and installed-machine resets impose
time, hardware, privacy/consent and maintenance cost without reducing risk in an
unrelated bounded feature. Maintained deterministic tests cheaply catch current
authority, lifecycle, serialization and storage regressions on every affected
change. Opt-in lab harnesses remain valuable where they uniquely observe native
enforcement, actual quality/performance or final installed behavior. Reuse valid
exact-profile receipts and repeat only affected evidence on material changes;
neither duplicate prototype suites nor stale receipts replace current checks.

Current inventory reconciled on **2026-10-08** against merged main
`9b8fccf9d130b024e8deaae984e9b12b5032ff1c`: exact input/output preferences
([#80](https://github.com/roryprimrose/Kora/pull/80),
[#79](https://github.com/roryprimrose/Kora/pull/79)), opt-in ordinary CombinedLog
([#82](https://github.com/roryprimrose/Kora/pull/82)), consolidated v3 authority
and exact task controls ([#83](https://github.com/roryprimrose/Kora/pull/83)),
per-Kora volume ([#84](https://github.com/roryprimrose/Kora/pull/84)),
committed AuthorityAudit inspection ([#87](https://github.com/roryprimrose/Kora/pull/87)),
device-default response mode ([#88](https://github.com/roryprimrose/Kora/pull/88))
and exact cached maintenance controls ([#89](https://github.com/roryprimrose/Kora/pull/89)).
These are bounded delivered subsets, not full Rxx/A0-A4 or native/accessibility/
acoustic/runtime/installed qualification. Dated validation receipts below retain
their original snapshots. External #81 is runtime-validation preparation only;
external presence fade #85 is merged, while pointer hide-timer #86 is still open
at this reconciliation. Neither is coordinator-owned delivery.

### R26.1b selected immutable revision lexical retrieval - 2026-10-09

Delivered on the merged R26.1a local preview foundation (#108):
`Kora.Tools.Files.LocalFileSearch`, Core's typed exact reference/citation/result
contracts and `lexical-lines-v1` policy, and native exact selected-source search.
Fixed `search file` / `inspect file` commands only focus that native control;
queries do not enter conversation, history, speech or inference routing.
The existing preview broker owns original session/task/source authority,
privacy/ownership generations, cancellation and quiescence; required requested/
terminal audit and exact-generation validation share its revocation boundary.

The bounded in-memory scan uses deterministic paragraph/ATX-heading/line-aware
chunks (2,048 UTF-16 characters / 128 lines), Unicode Form C/invariant
tokenization, unique-term OR ranking with saturated frequency and source-offset
tie-breaks. Query limits are 256 UTF-16 characters / 512 UTF-8 bytes / 32 unique
terms / 64 characters per term. At most eight excerpts / 16 KiB excerpt UTF-8
are returned with total match count and explicit truncation. Exact excerpts
bind source/revision/item/digest/display identity, UTF-16 offsets, one-based
line/column locations and query observation time. Stale/revoked/replaced,
invalid, no-match, denied, busy, cancelled and unavailable are truthful
noninterchangeable outcomes.

No current-path reread, silent refresh, folder expansion, watching,
persistent enterprise/derived cache, embeddings/vector index, model reasoning
or result egress is added. The current six-ID R06 registry has no safe
unavailable content-bearing model descriptor; it remains unchanged. See
[detailed behavior and limits](File_And_Folder_Ingestion.md#delivered-selected-revision-lexical-retrieval).
This does not complete broader R26/R04/R05/R06/R07/R12/R14, grounded reasoning
or installed/native accessibility acceptance.

Experiment disposition: maintained production tests cover this exact
deterministic retrieval/citation/lifecycle/no-egress slice. **All experiments
remain retained.** Inference quality, actual offline/resource/cessation,
RT1/RT2/MG1 and durable storage/cache proofs have no exact equivalence here;
lexical retrieval does not qualify or retire the inference proof.

Validation on latest main including #111: locked dependencies, root Release
build/analyzers (zero warnings/errors), Core 945/945, Application 2716/2716,
Tools 69/69, Definitions 6/6 and Windows integration 1160/1160 pass without
skips. Fresh-only combined portable coverage is 100% line and branch; no
coverage exclusions, thresholds or dependency manifests changed. Targeted
file/lexical/native-authority tests preceded the full gate. Bounds include
exact maximum query/output bytes and a maximum-size source with 87,382
matching paragraphs, while only eight candidates are retained. Unicode
astral-letter/emoji/punctuation chunk boundaries are tested separately.
After #107 advanced main, the documentation-only rebase preserves its complete
provider/memory/knowledge staged strategy and #111's inference boundary.
Production/test trees are byte-identical to the full validation snapshot;
root Release, all portable suites/100% coverage and 14/14 Windows file
integration tests passed again on the rebased head.

### R10/R15 bounded device-local in-call feedback - 2026-10-08

The [bounded in-call feedback contract](User_Configuration.md#delivered-bounded-device-local-in-call-feedback-r10r15)
is delivered independently on baseline **`c9bcf92`**. Schema-1
**`calls.feedback-mode`** has **Voice/UI/Both/Inherit**, default **UI**.
Native Settings and exact typed/current-name ACTIVATED discovery/get/status/
set/reset share authoritative original-input audio-control admission,
host-held choices, typed audit, atomic preferences/readback and durable intent.
No general model settings tool or automatic detector is exposed.

Only effective Active/Suspected (including manual Active) applies it ahead of
task/queue/session/device response mode; Inherit restores ordinary precedence.
Unknown/invalid evidence never selects an override or authorizes speech,
including with legacy suppression-Off state. Clear/Unavailable use ordinary
output and truthful detector unavailability. Independent speech suppression,
ownership/unlocked privacy, capture/consent, mute/zero, native retirement/
synthesis lifetime and mandatory complete visual safety/interruption/question/
approval gates remain. The session resolver seam adds no new durable session
or narrower-scope UI control.

Configuration/call/session/input/name revisions and the native visible lifetime
invalidate stale choices/callbacks. Writes retire active/queued output, never
capture/synthesize/autoplay/replay or edit grants. Required evidence and exact
atomic readback precede activation. Corrupt/unconfirmed storage refuses across
restart; a late evidence failure truthfully holds state rather than inventing
rollback. Reset removes only this override. Original protected-call voice
mutations cannot be relabelled by a UI approval or deferred until clearance.

Full R10/R15, qualified automatic detectors/source freshness, production
acoustic leakage/stop timing, native accessibility, proactive configuration,
speak-once/downgrade review and A0-A4 acceptance remain open. The dated manual,
rate, response-mode and other independent delivery receipts below are unchanged.

Root locked restore used the machine-local feed override without changing
repository feed/lock configuration. Release build passed with zero warnings/
errors. Targeted precedence/admission/native/shared-store suites and complete
required suites passed: Core **793**, Application **2,561**, Tools **38**,
Definitions **6**, Windows **1,109** (**4,507 total**, no skipped tests).
Latest-only portable coverage passed the unchanged exact **100% line/branch**
gates: **13,107/13,107 lines**, **7,366/7,366 branches**, no new exclusions.
Initial compile/test/coverage failures were corrected; a legacy suppression
test that waited for newly withheld speech was stopped and changed to explicit
Inherit, then the entire application suite passed. These receipts are automated
source/native-seam evidence, not acoustic or installed/native acceptance.

#### Bounded in-call feedback experiment disposition

All ten experiment areas were assessed for maintained equivalent coverage and
executable consumers. **No experiment content is removed or rerun by this
slice.** Generic original-input/call/generation and atomic persistence cases
now have maintained feedback-specific coverage, but no standalone executable
or unique receipt is fully superseded:

| Experiment | Retained unique evidence / executable consumers |
|---|---|
| [Speech](../experiments/r02-speech-proof/README.md) | Candidate/model/license inventory, synthetic wake/threshold fixtures and hashed acoustic/packaged-host receipts; feedback tests do not measure wake or physical leakage/stop timing. |
| [Storage](../experiments/r02-storage-proof/README.md) | Intertwined WAL/rollback engine, SQLCipher/envelope/artifact/key/rekey/DPAPI/capacity/leakage proof and historical measurements; ordinary atomic preference/shared-SQLite tests are not those consumers. |
| [Local inference](../experiments/r02-local-inference-proof/README.md) | Production source-linked reasoner, actual model/context/quality/performance/cancellation/offline trial harness and unavailable-path evidence; no inference trial is replaced. |
| [Node runtime/provider](../experiments/r02-runtime-proof/README.md) | Actual SDK/native loopback and hook-only failure witness, provider control-point and envelope observations; no production runtime integration is added. |
| [.NET control RT1](../experiments/r02-dotnet-control-proof/README.md) | Exact-tag source-built public SDK/native conformance, byte reproduction and expected hook-only failure; source consumers in RT2/MG1 remain. |
| [Runtime lifecycle RT2](../experiments/r02-runtime-lifecycle-proof/README.md) | Actual SDK lifecycle/file/socket/diagnostic observers and positive controls with distinct blocked isolation receipts; feedback generation tests are not OS/process observations. |
| [.NET management MG1](../experiments/r02-dotnet-management-proof/README.md) | Released-profile SDK/native envelope/deadline/retry/late-effect and independent identity evidence, including retained RT1 derivation; no scheduler/runtime envelope is qualified here. |
| [Containment W1](../experiments/r02-containment-proof/README.md) | Job/AppContainer native boundary, attributable network-denial observation tooling and blocked evidence; no worker/network profile is replaced or enabled. |
| [Dependency W2](../experiments/r02-w2-dependency-proof/README.md) | Fixed scripts/typed effect, rejected dependency mechanisms and unresolved native-module/control boundaries; feedback has no executable worker authority. |
| [Distribution](../experiments/r02-distribution-proof/README.md) | Historical exact-revision package/native/source receipts; prior maintained migration already retired six equivalents and NSIS paths. This slice replaces no remaining historical evidence or maintained installer/eng consumer. |

### R12 bounded exact-ID logical disposition - 2026-10-08

The [native two-step contract](Interaction_And_Sessions.md#delivered-bounded-exact-id-logical-disposition---2026-10-08)
delivers explicit logical disposition, **not full R12 deletion**. The host
holds a single-use exact existing-ID/generation/metadata/live-record preview;
fresh native confirmation revalidates admission, all addressed record/task
revisions and idle certainty at the authoritative writer. Unknown/stale IDs,
pending questions, live/Unknown work, missing/corrupt storage and changed
privacy/ownership/call state fail closed, never cancel or abandon work.

One shared-lease transaction removes addressed live name/metadata, questions/
drafts/answers, observations, admitted wait bindings and scoped grants, advances
generation, and commits the Removed tombstone, control terminal success and
required typed audit. New task intents, late outcomes and interaction appends
cannot revive it. Unrelated sessions and independent Perpetual grants survive.
No model/voice/typed-delete route, inactivity timer, retention setting or
automatic purge is introduced.

**Still open:** conversations/history, managed artifacts/source snapshots/
summaries/indexes/caches, inventoried backup/copy deletion and journal/free-page
rewrite. Task/event and content-minimising audit provenance, independent
diagnostics, Perpetual provenance, inert migration storage and copied databases
remain disclosed. No forensic erasure, physical power-loss, installed/live
native accessibility or full R12/A3 acceptance is claimed.

Focused portable host tests and actual Windows store/native-state tests cover
success, stale/unknown identity and revision, pending/live/Unknown blockers,
completed intervening work, cancellation/audit/gate rollback, corrupt/missing
authority, competing append admission, cancelled wait removal and preservation.
Owned-process kill tests exercise both sides of the production PERSIST/FULL
COMMIT: rollback leaves live records intact and recovery Interrupted; committed
disposition reopens without a pending recovery control or execution replay.
The existing source-generated failure log and host activity/typed audit paths
are reused without content-bearing tags or logging.

Validation on the isolated `c9bcf92` baseline: locked restore through the
machine-local feed override (no repository feed changes), root Release build
with zero warnings/errors, Core **785**, Application **2,472**, Tools **38**,
Definitions **6**, Windows **1,116** passing, and fresh-only combined portable
**100% line/branch coverage** at unchanged thresholds. The initial Application
run had one unrelated maintenance-disposal cancellation-versus-admission
assertion failure; that test passed in isolation and the complete Application
rerun passed without changing maintenance code. Focused lifecycle/authority/
disposition **67** and workspace host **61** tests passed. No user profile
data, live microphone, clipboard, OS-session effect or experiment was exercised.
Final deletion-coupled recovery hardening adds latest-audit session-row
validation: missing or rolled-back Removed tombstones cannot reopen as empty
authority or permit identity reuse. The focused disposition **9** tests and
complete Windows **1,116** rerun pass after that change; portable sources and
their passing coverage are unchanged.
After fetching/rebasing onto `4cf8034` (peer documentation preserved), the root
Release build again passed with zero warnings/errors; all four portable suites
passed at the same counts with newly generated **100% line/branch coverage**,
and affected Windows authority/disposition/interruption **68** tests passed.
While PR checks ran, main advanced to documentation-only `fdaa90e`. The second
rebase preserved both its capability-scoped qualification/dependency wording
and this delivered R12 status. No production/test sources changed. Root Release
build again passed with zero warnings/errors; host **61** and affected Windows
**68** reruns passed before the lease-guarded PR update.
Subsequent rebases preserved independent runtime, speech, W1 and inference
proof continuations and the production device-local in-call feedback increment.
The desktop workspace now consumes `IsProtected`, including uncertain
manual-call evidence with Clear/Unavailable automatic states. Combined full
Release validation passed with zero warnings/errors and Core **793**,
Application **2,572**, Tools **38**, Definitions **6**, Windows **1,121**;
fresh-only portable line/branch coverage remained **100% / 100%**.
After rebasing onto inference-proof main `a1febb7`, the root Release build,
host **61** and affected Windows **86** tests passed again. Inference and
speech live/consent evidence remains separate and was not exercised.
The production rebase onto bounded local-file-preview main `64b3a52`
preserved that independent feature. Root Release build again passed with zero
warnings/errors; all required suites passed at **850 / 2,584 / 60 / 6 / 1,135**
(Core / Application / Tools / Definitions / Windows), with fresh-only portable
coverage still exactly **100% line / 100% branch**.
The subsequent rebase onto shared-profile discovery main `d79973b` also
preserved that independent feature and its existing session/control authority
paths; it adds no new session-owned persistence table. Full Release validation
passed at **931 / 2,618 / 60 / 6 / 1,159**, zero build warnings/errors and
fresh-only **100% line / 100% branch** portable coverage.

#### Experiment disposition for logical disposition

Every checked-in experiment directory was evaluated against this increment's
maintained scope. **No further experiment file is removed or rerun**; historical
receipts and provenance are unchanged. Existing production store atomicity and
new row-removal/race tests do not replace unique engine/copy/runtime/native or
measurement contracts:

| Experiment | Maintained equivalence and retained reason |
|---|---|
| [Storage](../experiments/r02-storage-proof/README.md) | Production exact logical row removal, isolation and interruption now have maintained tests. Shared `Program.cs`/`ScratchStore.cs`/`Crypto.cs` still execute encrypted/enveloped sample and FTS removal, `secure_delete`/checkpoint/VACUUM, recoverable backup, artifact/key/rekey/authentication and native comparisons. Those unique consumers are not superseded by live-authority row disposition. |
| [Node runtime](../experiments/r02-runtime-proof/README.md) | Hook-only failed-result mediation witness and actual SDK/loopback request boundary are unrelated to store deletion. Runtime-validation source staging still consumes this directory. |
| [.NET control RT1](../experiments/r02-dotnet-control-proof/README.md) | Actual SDK/native control-point, volatile-session-filesystem and source-build reproduction/regression evidence remain unique; lifecycle/MG1 fixtures and maintained runtime-validation scripts consume these sources. Tombstones are not provider-state erasure. |
| [Runtime lifecycle RT2](../experiments/r02-runtime-lifecycle-proof/README.md) | Native file/socket/diagnostic observation, sampled quiescence and timeout-not-termination evidence remain unique. Runtime-validation preparation/released lifecycle scripts consume its fixture and receipt contracts. |
| [Management MG1](../experiments/r02-dotnet-management-proof/README.md) | Actual released-SDK management deadline/topology/late-output/runtime-pause proof remains unique and consumed by maintained runtime-validation scripts. Idle database admission is not runtime resource quiescence. |
| [Containment](../experiments/r02-containment-proof/README.md) | OS token/resource/filesystem/child/network denial and deployment boundary evidence is not covered by session-row tests; its standalone runner and deferred W1/W3 procedures remain. |
| [W2 dependency](../experiments/r02-w2-dependency-proof/README.md) | Fixed worker/interpreter/helper-scope/native dependency/cancellation procedures remain executable consumers. Logical disposition adds no maintained worker equivalence; prior declared-resource identity migration remains independently documented. |
| [Speech](../experiments/r02-speech-proof/README.md) | Offline fixtures, detection/verifier/acoustic/packaged-host measurements and deferred live procedures are unrelated; no capture or speech experiment is run. |
| [Local inference](../experiments/r02-local-inference-proof/README.md) | Candidate quality/budget/cancellation/offline qualification and LI01-LI07 measurement procedures remain unique; no inference/runtime deletion equivalence is established. |
| [Distribution](../experiments/r02-distribution-proof/README.md) | Superseded executables were already retired by its separate maintained-tool migration. Remaining historical source/native/installer receipts and provenance are not superseded or relabelled by session disposition. |

### R10 bounded Windows-provider-native rate - 2026-10-08

The owner-approved [Windows rate slice](User_Configuration.md#delivered-bounded-windows-provider-native-speech-rate-r10)
was implemented independently on merged main **`fe0fedf2f566bc537f7a0e532e3a1e2b58a245ce`**
([audit retention #93](https://github.com/roryprimrose/Kora/pull/93)).
It adds the explicit `speech.windows-rate` integer **-10..10**, default/reset
engine-normal **0**, to native Settings and exact typed/current-name ACTIVATED
discovery/get/status/set/reset. Windows advertises its native support and
future synthesis consumes the owned engine setter. Kokoro is unsupported and
unchanged; unknown state fails closed, without a fake common scale.

The distinct rate action reuses common original-input audio session/generation
and committed-intent admission, atomic preferences and typed audits. Current
provider/source/name/input/call/native-lifetime/ownership/privacy revisions are
revalidated. Unconfirmed values remain held across restart. Changes retire
active/queued speech; future synthesis waits behind the existing lifetime and
no operation captures, autoplays/replays, changes providers/assets or touches
global SAPI/mixer/default-device state. Existing output/zero-volume gates,
mandatory visual previews and all grants/history/session/retention rules remain.

Direct synchronous Release validation passed with **zero warnings/errors** and
all **4,214** tests: Core **769**, Application **2,396**, Tools **38**,
Definitions **6**, Windows **1,005**; none skipped. Latest-only existing portable
coverage reports have exact line/branch rates **1 / 1**:
**12,539/12,539 lines**, **7,034/7,034 branches**; no new exclusions or weakened
thresholds. Deterministic tests exercise native setter ordering/failure, no
Kokoro setter, active/queued retirement, cancellation/disposal, stale sessions/
generation/provider/source, corrupt/pending preferences/restart, admission parity
and cross-session correlated receipts. This is not acoustic/installed/native
accessibility, full R10/A0–A4, runtime or release acceptance.

No experiment was removed. `experiments/r02-speech-proof` retains unique
candidate/model/license inventories, synthetic wake/threshold fixture scripts,
hashed `validation.json`, `unit-tests.txt`, `recorded-results.json` and
`recorded-summary.md` receipts, and deferred acoustic/packaged-host consumers.
The rate feature replaces none of those executables or observations. Storage,
runtime/provider, containment and distribution experiment dispositions remain
unchanged; no lab trial was run. The #90 inventory reconciliation and distinct
#91/#92/#93 diagnostic/manual-call/audit receipts below remain historical.

### Bounded R04/R14 Committed Audit Inspection

The native evidence inspector now has an explicit **AuthorityAudit** source
for committed typed schema-v3 interaction-store security audit rows, independent
of diagnostic projections and file mirrors. It reuses the native admission,
query/citation/cursor/page services with a consumer-focused read-only store seam,
the shared lease and immutable sequence ceiling. It does not widen **All** or
**CombinedLog**, expose model tools/export, reconstruct historical payloads or
grant execution/approval authority. See
[the exact delivered source contract](Information_Display.md#delivered-bounded-native-evidence-inspection).
Tamper-resistant checkpoints, encryption, general conversation history and
installed/native acceptance remain unimplemented/unaccepted.

Validation rebased onto merged presence-fade #85 (`363ed6e`) passed Release
with zero warnings/errors and all **3,485** tests: Core 675, Application 1,820,
Tools 38, Definitions 6 and Windows 946. Fresh portable coverage, with the
existing thresholds/exclusions unchanged, has exact line/branch rates of
**1 / 1 (100% / 100%)**. Thirteen current-user, hardware-free source fixtures
cover committed version-wait cancellation/audio admission, question answer/
cancel, native/source/citation parity, snapshot paging/appends, corrupt/obsolete
schema, migration/frozen-source isolation, replacement/permissions, current
host/origin/session, tampered/expired/foreign cursors, cancellation/concurrent
shared-lease reads and byte-for-byte no-write observation. This is automated
source evidence, not installed/native or forensic acceptance.

The merged playback-volume #84 follow-up (`b34c077`) preserved its admission,
zero/no-replay recovery and native configuration behavior. Release again had
zero warnings/errors; fresh portable suites passed Core 700, Application 1,903,
Tools 38 and Definitions 6, with exact line/branch coverage **1 / 1**.
One initial Windows invocation failed at the existing authority-loss fixture's
`Directory.Move` with access denied. Its isolated **1/1** and complete
**965/965** rerun passed with the same binaries and no source/test suppression.
The complete combined passing suite count is **3,612**; the original #85
validation receipt above is retained unchanged.

No experiment is retired by this slice. It supplies no maintained executable
equivalence for the unique storage-encryption/engine/artifact/key, containment,
runtime/worker/provider or speech proofs. Their historical evidence and
remaining executable consumers are unchanged; no cross-tree experiment result
is treated as production authority.

R17-D01's source **preview/build/staging/verification** slice was implemented
on 2026-10-06 in isolated `agents/kora-r17-source-bootstrap-20261006`, based on
`d1fc77f8083985c5d86ed0ef3496ac68c4a150ed`. The exact-base canonical source
publish and no-build rerun passed with SDK 10.0.401; root Release, all 1,685
tests and portable 100% line/branch coverage passed. See the
[source interface and verification snapshot](Distribution_And_Updates.md#source-bootstrap).
This records local tooling/evidence, not source activation, official
publication, installed protection or D-005 sign-off. CI repair, R04 composition
and R05 foundation remain independently owned; no sibling changes were merged.

R17's actionable proof migration followed on 2026-10-07 in isolated
`agents/kora-r17-proof-migration-20261006`, based on exact main
`c5dffabf8f4fa767147be06dd8b296238ea97da0`. Source interface **1.1.0** and
maintained `eng` checkout/path/publish/native/runtime/import/resource helpers
remove all executable distribution-experiment dependencies. Ownership/stage/
both-RID contracts, exact canonical source build/static verification/rerun,
root Release (zero warnings/errors), all **1,882** tests and fresh portable
**100% line/branch** coverage passed before targeted retirement of six
superseded distribution proof scripts. All seven historical receipt/inventory
digests are unchanged. See the [migration receipt and limits](Distribution_And_Updates.md#maintained-proof-migration-verification-2026-10-07)
and [archival disposition](../experiments/r02-distribution-proof/README.md).
Delivery remains uncommitted; no sibling code was merged. Release-publication
tag/draft correctness remains independently owned, as do R04/R05 and R14.

The paragraph above records the original pre-PR snapshot. The subsequent
[merge/CI follow-up](Distribution_And_Updates.md#merge-and-ci-follow-up-2026-10-06)
records rebasing onto merged publication #49, viewer #47 and interaction #48,
all **2,041** combined tests passing, **82** source-bootstrap contracts and
the corrected workflow-identity fixture. Main run **37536638457** also
establishes actual unsigned POC publication of `v0.1.0-beta43`; installed
protection, source activation and native/architecture acceptance remain open.

Current-assessment reconciliation on 2026-10-06 after #34/#40: held PTT and
cross-build ownership are implemented, with bounded production-host voice,
x64 handoff and Ollama simple/long-answer/cancellation observations recorded
in the [deferred-validation register](Deferred_Validation.md#2026-10-05-bounded-local-inference-result).
Startup enables gated voice readiness, not ambient capture. The separately
approved [released-profile MG1 proof](../experiments/r02-dotnet-management-proof/EVIDENCE.md)
passes; historical source-built RT1 hashes/blockers remain distinct.
These narrow results do not accept A0/A2/Gate 0, native privacy/permission
polling, CPU-floor/offline qualification, RT2/PV1 or production admission.
Original-baseline and rebase receipts below remain historical.

R04 implementation started on 2026-10-06 in the isolated
`agents/kora-r04-durable-storage-implementation` branch, based on
`d3d2296387e30d2a55850b71d4b8d80be7594ffd` (also its initial `origin/main`
merge base). The worktree was clean. Other inference/design and installer
worktrees were not merged, modified, or used as test evidence.
See [R04 foundation delivery](#r04-foundation-delivery) for the delivered,
partial and blocked boundaries; this is not completion of R04.

On 2026-10-06 the owner approved a local R04 checkpoint and rebase onto
current main. The checkpoint was rebased as `7473902` on
`3e8558f` (WiX/versioning #35), including startup greeting #32, RT1 #33 and
proof/lifecycle #34. The single catalogue conflict was reconciled by
preserving main's WiX/VC++ rows and R04's unavailable-content/native-admission
rows. The original-baseline receipts below remain historical; the
[rebased receipt](#r04-rebase-validation-receipt---2026-10-06) records fresh
combined-source validation. No sibling worktree was modified or unpublished
work imported, and no push or PR was authorized by this rebase.

Related: [MVP Scope](MVP_Scope.md), [Decision Register](Decision_Register.md), [Acceptance Criteria](Acceptance_Criteria.md), [Canonical Tool Catalogue](Internal_Model_Tools.md), [Technical Capability Reference](Tool_And_Skill_Reference.md).

## R10/R04 bounded future-only SQLite diagnostic retention - 2026-10-08

The subsequent independent [audit option](User_Configuration.md#delivered-bounded-future-only-audit-retention-r10r04)
is separately described below; this diagnostic slice's original scope/receipts
remain historical and unchanged.

The owner-approved [single diagnostic-days option](User_Configuration.md#delivered-bounded-future-only-sqlite-diagnostic-retention-r10r04)
is implemented with shared native and exact typed/current-name ACTIVATED
discovery/get/status/set/reset. Canonical integer 1–365, default/reset30, affects
only newly committed ordinary SQLite logs/spans/owned links. Existing due dates
never change; apply-now/immediate deletion is unavailable and set/reset does not
run pruning. Audit default90/existing30–365 domain, files30/30, history/session,
all grants/approvals and the owner-startup janitor schedule stay unchanged.

[Core domain/policy](../src/Kora.Core/Configuration/DiagnosticRetentionDays.cs),
[configuration/admission](../src/Kora.Application/Configuration/DiagnosticRetentionConfigurationService.cs)
and the [genuine SQLite writer](../src/Kora.Windows/Storage/WindowsSqliteEvidenceSink.cs)
bind original-user independent diagnostic-control session/generation, host-held
proposal and fresh revisions with live native/owner/privacy/topology/input/call
conditions. Native visibility is cached by UI events with an expiring revision:
storage-thread admission never reads Avalonia properties, and hide/reopen or
close/dispose cannot revive an earlier binding. Required typed audit, durable
atomic save/readback, terminal intent
receipt and the unconfirmed marker precede activation. Invalid UTF-8/format or
unconfirmed writes cannot silently become active defaults on restart. Ordinary
policy unavailability explicitly reports independent delivery gaps without
waiving required audit or authority; no configuration gains effect authority.

Semantic evidence schema v2 only migrates exact validated legacy30 rows,
preserving every ID/envelope/trace/reference/deadline. Writer/reader validation
accept integral committed 1–365-day ordinary lifetimes independently of the
current preference. The current completed-span writer remains insert-only:
duplicate-ID update attempts are rejected and never refresh an old deadline.
Bounded janitor limits and retained-link/citation behavior are unchanged.

Maintained deterministic portable tests cover bounds/default/reset, provenance,
native/exact/ACTIVATED parity, current original-source/host/session-generation/
revision admission, late/cancel/dispose isolation, audit/atomic/readback/
unconfirmed restart failures and separate audit authority. Hardware-free real
private SQLite fixtures cover actual new deadlines, immutable old rows,
cold variable-policy reads, schema migration/refusal, link/citation integrity,
existing bounded pruning and preserved authority/Perpetual rows.
Release warning-as-error build, complete portable suites and exact existing
100% line/branch coverage pass without new exclusions/suppressions. Targeted
Windows storage coverage includes the affected writer/reader/consolidated
authority/recovery/pruning and static native-binding contracts.

**Experiment disposition:** no executable, historical receipt or consumer is
deleted or rerun. These maintained standard-SQLite preference/deadline cases do
not supersede unique engine/encryption/DPAPI/rekey/artifact/backup/native,
worker/runtime/acoustic/hardware or released-profile proofs. Existing storage
[equivalence map](../experiments/r02-storage-proof/README.md#production-recovery-migration-and-retention---2026-10-07)
and [deferred retention decision](Deferred_Validation.md#2026-10-05-safe-revalidation-and-proof-code-disposition)
remain authoritative. No full R04/R10, A0–A4, installed/native/acoustic,
encryption/forensic/runtime or release acceptance is claimed.

## R10/R04 bounded future-only audit retention - 2026-10-08

The owner selected canonical **30–365/default-reset90**, future-only audit
metadata, not grant lifetime. Shared native/exact typed/current-name ACTIVATED
discovery/get/status/set/reset uses the separate
[audit configuration/admission](../src/Kora.Application/Configuration/AuditRetentionConfigurationService.cs)
with the merged common original-input/durable-intent mechanism. Saved/default/
effective provenance, bounded output, exact proposals/source/policy revisions,
owner/privacy/topology/call/input and native-lifetime gates remain explicit.

The [authoritative audit-days domain](../src/Kora.Core/Configuration/AuditRetentionDays.cs)
and confirmed snapshot reach actual NEW committed
[schema-3 required authority audit](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.cs)
and independently qualified [diagnostic audit projections](../src/Kora.Windows/Storage/WindowsSqliteEvidenceSink.cs).
No nested shared lease or projection-to-authority promotion is introduced.
Prior-policy REQUESTED/terminal preference receipts, atomic exact readback and
durable intent outcome precede marker confirmation/activation. Lost evidence/
corrupt/unknown/unconfirmed preferences hold required new commits and refuse
startup authority writes; explicit inspection/repair/refresh is required,
never a default90 fallback, replay or success-shaped failure.

Maintained [portable failure/admission tests](../tests/Kora.Application.UnitTests/Configuration/AuditRetentionConfigurationServiceTests.cs),
[native/typed/activated parity](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.AuditRetention.cs)
and [real private authority/projection/lease fixtures](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteAuditRetentionConfigurationTests.cs)
exercise exact30/365/90, reset/source/revision provenance, immutable old bytes/
deadlines/commit digests/citations, cold/legacy validation, held terminal evidence,
independent ordinary policy and concurrent original-host trace isolation.

**Preserved scope:** ordinary SQLite1–365/default-reset30; files30days/30files;
all session/history/task/question/approval/grant records and validity/scopes,
independent Perpetual without expiry/retention/eviction; existing janitor schedule
and #92 run-only manual-call/resource-retirement behavior. Apply-now, immediate
deletion and audit pruning remain unavailable. No full R04/R10/A0–A4 or
forensic/tamperproof/encryption/installed/native/acoustic/runtime/release
qualification is claimed.

**Experiment assessment:** maintained equivalence now includes the bounded
future-only audit preference, deadline metadata and private-store reopen/
migration cases. It does not supersede unique storage-engine/encryption/DPAPI/
rekey/artifact/backup, worker/runtime/containment/acoustic/hardware or release/
source-tool receipts and consumers. No executable experiment, historical
receipt or consumer is edited, rerun, retired or deleted for this delivery.

Current availability adds this bounded audit option to R10/R04, not a general
retention/deletion registry. The R10 inventory's delivered diagnostic option
remains independent; remaining session/history and other-retention controls,
apply-now, audit pruning/continuation and full acceptance stay open.

Local source qualification against actual merged baseline
`7c0429e8d430856ad24a21569ca38434db6f7fe3` (#92): Release solution build,
zero compiler/analyzer warnings/errors; Core **750**, Application **2,289**,
Tools **38**, Definitions **6**, and full source-covered Windows **998** tests
passed, no skips. Current-source portable line and branch rates are each
**exactly 100%**, using unchanged threshold/filter tooling. The fresh no-restore
probe first reported missing assets; locked restore then used only the approved
Networking-AAA feed, with no dependency/lock/config changes. These are maintained
hardware-free/source-fixture receipts, not a live Kora/audio/provider/privileged
trial or installed/native/acoustic/storage-erasure qualification.

## Current Merged Snapshot - 2026-10-07

Refreshed on 2026-10-08 after the session-command and native recovery merges.
Source reviewed at exact main
[`90146f405ea9236a23ee2a95cd159efcd5fbf84f`](https://github.com/roryprimrose/Kora/commit/90146f405ea9236a23ee2a95cd159efcd5fbf84f).
The required first-wave merges are ancestors:
[#70](https://github.com/roryprimrose/Kora/pull/70)
(`0b667e91746e94c8157bc9ae90faf57be3b6d3b9`),
[#69](https://github.com/roryprimrose/Kora/pull/69)
(`c0c15ac2a74a865bbd7540a7a0cf5c00c2d3a21b`),
[#72](https://github.com/roryprimrose/Kora/pull/72)
(`640f28b2f91c106f2d84cb62193beca9961ca739`) and
[#71](https://github.com/roryprimrose/Kora/pull/71)
(`cf0057bef2858998ea4e893cee7f4372780b7f25`).
[#74](https://github.com/roryprimrose/Kora/pull/74)
(`e3280388a4f6c2ce2dd1e6a7adaf925a3e6f3c7d`) adds independent daily JSON inspection;
[#73](https://github.com/roryprimrose/Kora/pull/73)
(`3bf1951ad6517cc2d3fa0b713e0a7fd23d4b18c8`) adds bounded exact-ID session
commands, and [#77](https://github.com/roryprimrose/Kora/pull/77) adds passive
native microphone recovery at the reviewed main SHA. Only merged main is included; this is a
documentation/source review, not a new build, trial or acceptance receipt.

| Merged foundation | Delivered boundary and maintained source | Still outside that boundary |
|---|---|---|
| [#61](https://github.com/roryprimrose/Kora/pull/61), #70, #73: R12/R13/R14 sessions | [Shared workspace service](../src/Kora.Application/Hosting/SessionWorkspaceService.cs), [deterministic command path](../src/Kora.Application/Hosting/SessionWorkspaceService.Commands.cs) and [private interaction store](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.cs): passive bounded ID pages, typed questions/current durable tasks, guarded idle Done/resume, durable names and empty Create/revisioned exact-ID Rename. Exact typed/activated-voice help/list/status/inspect/create/rename/done/resume share guarded host transactions and fresh lineage. [User syntax](../docs/commands.md#bounded-exact-id-session-commands). | Names/creation are not conversations, context restoration, queues, a scheduler or name-based authority. No automatic archive/retention/delete/export, model session tools or approval retargeting. Typed commands can run while bootstrap work is busy without cancelling it; protected-call voice mutations remain unavailable. |
| [#63](https://github.com/roryprimrose/Kora/pull/63): R07 context | [Tools clipboard broker](../src/Kora.Tools/Clipboard/ClipboardSnapshotBroker.cs) and three per-action classes: explicit immutable local plain-text preview, same-ID reuse and revoke, at most 256 KiB UTF-8. [User commands](../docs/commands.md#explicit-local-clipboard-preview). | No watcher, write, persistence, explanation or provider submission. Local tool-loop/answering and complete secret/egress-envelope gates remain open. |
| [#56](https://github.com/roryprimrose/Kora/pull/56), [#64](https://github.com/roryprimrose/Kora/pull/64): R06/tools/definitions | [Application registry gateway](../src/Kora.Application/Tools/ReadOnlyCapabilityRegistry.cs) admits six read-only host descriptors; [Kora.Tools](../src/Kora.Tools/README.md) owns portable per-action implementations; [Kora.Definitions](../src/Kora.Definitions/README.md) owns immutable bundled resources. | Cached discovery is not model execution, fresh probing or runtime admission. No remote SDK/MCP adapter, mediated result loop, bundled script executor or agent runtime is composed. |
| [#57](https://github.com/roryprimrose/Kora/pull/57), #72, [#78](https://github.com/roryprimrose/Kora/pull/78): R10 configuration | [Appearance service](../src/Kora.Application/Configuration/AppearanceConfigurationService.cs), [speech service](../src/Kora.Application/Configuration/SpeechConfigurationService.cs) and [assistant display/PTT prefix service](../src/Kora.Application/Configuration/AssistantNameConfigurationService.cs): shared native/exact typed/activated-voice discovery/get/set/reset, bounded revisions, atomic persistence and audit. [Settings guide](../docs/settings.md). | Only nine appearance options, installed provider/voice choices and the existing display/PTT prefix are registered. Name mutation retires capture and preserves all authority identities; it does not qualify production wake. No general model settings tools, whole-profile reset/undo, new speech maxima, provisioning authority or acoustic acceptance. |
| [#65](https://github.com/roryprimrose/Kora/pull/65): artifact invocation | [Explicit bundled catalogue](../src/Kora.Definitions/ArtifactInvocation/EmbeddedArtifactCatalogue.cs), [bounded disk discovery](../src/Kora.Windows/ArtifactDiscovery/WindowsDiskArtifactDiscovery.cs) and [router](../src/Kora.Core/ArtifactInvocation/ArtifactCommandRouter.cs): source-qualified slash/activated-voice instruction selection into the existing verified local-model request, with a filtered slash dropdown. [User syntax](../docs/commands.md#run-skills-and-future-artifacts). | Instruction selection is not execution of embedded/profile scripts, generic skill enablement/authoring, a tool loop or agent delegation. Existing action/grant gates still apply. |
| [#68](https://github.com/roryprimrose/Kora/pull/68), #69, #74: diagnostics/evidence | Source-generated class-companion diagnostics preserve typed audit authority. [Owner startup](../src/Kora/Program.cs) composes recovery then one [ordinary retention batch](../src/Kora.Windows/Storage/WindowsSqliteDiagnosticRetention.cs). [Evidence reader routing](../src/Kora.Windows/Storage/WindowsEvidenceReader.cs) supplies SQLite and independent DailyLog inspection. [User evidence limits](../docs/privacy-safety-and-logs.md#logs). | Retention preserves audits/tasks/sessions/grants; it is not session deletion or forensic erasure. All remains SQLite-only; no combined file/database ranking, authoritative file audit, Ask Evidence/model tools or export. |
| #71, #77: R03/R05/R09 native recovery | [Native tray](../src/Kora/SystemTrayController.cs) and [passive microphone card](../src/Kora.Application/ViewModels/MicrophoneRecoveryViewModel.cs): generic input state, bounded metadata refresh, revision-bound System/pinned preference selection, explicit PTT enable/disable and Stop speaking. Tray/Settings share exact displayed choices, unsaved highlight, Save preference only and a separate fresh endpoint-bound Enable. [User recovery](../docs/windows-and-tray.md#microphone-and-listening-recovery). | Enable arms readiness with the microphone closed; actual capture requires held PTT. The card is not a durable R05 question/task/session bridge, combined consent/selection/enable, microphone test, ambient/wake capture or Windows permission change. Full hardware/native acceptance remains separate. |

This snapshot does not close full R04/R06/R07/R10/R12/R13/R14/R19 or A0-A4.
The [bounded per-Kora playback volume](User_Configuration.md#delivered-bounded-per-kora-playback-volume-r10)
adds native/exact typed/ACTIVATED discovery/get/status/set/per-option reset:
domain-owned 0-100 integer percent, original unscaled default 100, shared genuine
audio admission, host-held proposals, revisions, atomic readback and audit/task
receipts. Zero prevents synthesis and retains full visual output; owned Windows
instance gain/Kokoro PCM attenuation never writes global volume or replays
retired output. Independent speech, output, input, name, consent, approval and
retention behavior remains unchanged. This is not full R10 or native/acoustic
acceptance. No speech experiment is retired: unique SAPI sample/rendering,
capture, hardware/provider and acoustic receipts/executables are not superseded
by these scalar, fake orchestration and generation tests.
Validation on consolidated authority baseline `77deef4`: root Release build
has zero warnings/errors; 700 Core, 1,897 Application, 38 Tools, 6 Definitions
and 952 Windows fixture tests pass without skips, including cancellation
callbacks reentering state after atomic volume/generation retirement without
holding the native callback state lock. The unchanged portable gate
is exactly 100% lines and branches. Real SQLite volume controls run through the
shared task/interaction lease before and after validated frozen-ledger migration,
with native/typed/activated provenance and correlated audit receipts. The
evidence-provider fixtures use the existing nonparallel composition collection
so their process-wide activity listener cannot outlive another fixture's paths.
No Kora launch, capture, audible playback, installed-provider trial, elevation,
account or network-policy operation was performed.
The [current bounded audio output feature](User_Configuration.md#delivered-bounded-exact-output-device-preference-r10)
shares working native/exact discovery/get/status/select/reset through persisted
audio session/generation and host-held-choice admission. Original channel,
ownership/privacy/call/input revisions, audited atomic persistence and receipt-safe
activation remain fail-closed; metadata-only configuration never starts audio.
Saved/System routing and independent installed speech/summary/input/name settings
are preserved. No experiment is retired: deterministic metadata/storage/binding
tests do not replace unique speech/acoustic/hardware/provider evidence, and no
historical receipt or full R10/I/A acceptance is rewritten.
Safe independent foundations can continue once their actual prerequisites
are satisfied; downstream model/effect exposure remains gated by the relevant
R02 qualification, host authority, privacy, resource and installed boundaries.
The existing [Needs graph](#ordered-outstanding-work), not PR numbering or
experiment success alone, controls that dependency order.

**Experiment disposition at this snapshot:** the distribution-only NSIS path
and six superseded distribution executables are already retired, with
[maintained equivalents and original receipts](../experiments/r02-distribution-proof/README.md#executable-disposition-after-maintained-equivalence).
Maintained production tests now cover applicable storage atomicity/recovery,
session lifecycle/metadata and ordinary retention semantics; the
[storage equivalence map](../experiments/r02-storage-proof/README.md#production-recovery-migration-and-retention---2026-10-07)
and [retention assessment](../experiments/r02-storage-proof/README.md#bounded-ordinary-retention-equivalence-assessment---2026-10-07)
retain shared executables for unique encrypted-engine/FTS/DPAPI/rekey/artifact/
backup/native comparisons and their consumers. Clipboard preview, read-only
discovery, speech choices and DailyLog inspection do not supersede actual
inference, RT1/RT2/MG1/provider, speech/acoustic or containment/W2 proofs.
No additional executable retirement or experiment rerun is claimed. The
[deferred proof register](Deferred_Validation.md#2026-10-05-safe-revalidation-and-proof-code-disposition)
continues to distinguish unique proof from production and installed acceptance.

## How to Read Status

### Bounded R10 exact input-device configuration

The existing microphone preference now has typed and activated-voice exact
discovery/get/status/set/per-option reset via `speech.input-device`, sharing
native Settings/tray/#77 card atomic persistence and typed audit outcomes.
Five-second single-flight metadata discovery, host-held exact objects,
request/session lineage, input/recovery/call revisions and serialized live
host/privacy/original-channel gates bound writes. System follows multimedia
default; unavailable explicit pins stay pinned. Reset selects System only,
never consent/permission, listening enablement or a reopened run hold.
The [owning contract](User_Configuration.md#delivered-bounded-exact-input-device-preference-r10)
and embedded commands/settings/voice/tray guidance state complete input/output
limits and explicit failure recovery. This is not full R10/R03/R09/R05/A or
native/hardware acceptance; no model tools or broader runtime authority.

Initial prerequisite base: `90146f405ea9236a23ee2a95cd159efcd5fbf84f`.
After #76, #75 and #78 merged, the owned input-device branch was immediately
rebased onto `2e54c892c3b63f2e8c96d7f89dbcb03a4f447015`, preserving both exact
command grammars, the new assistant-name workflow and sibling documentation.
Fresh combined validation: Release solution build **0 warnings / 0 errors**;
Core **648**, Application **1,692**, Tools **38**, Definitions **6**, Windows
integration **865** passed,
**0 skipped**. Portable aggregate line/branch gates remain exact **100% / 100%**;
existing coverage exclusions are unchanged. New deterministic coverage includes
parser/output boundaries, native/typed/activated parity, missing pins/default
changes/duplicate names, foreign/equal/stale context, pending interactions,
call/owner/privacy/recovery races, cancellation/disposal/late metadata,
atomic-format restart and failed storage/request/terminal audit evidence,
no auto-arm, release failure and hostile incoming trace isolation. No live
Kora, capture/playback/wake, setup, OS effects or hardware acceptance was run.

Experiment disposition: retained all executable/evidence files. Inspected
microphone consumers in `r02-speech-proof`, local-inference and storage proof
references; none implements this exact metadata/preference command workflow.
Speech fixture/wake/recognition, capture lifecycle, containment and historical
receipts are unique evidence, not executable equivalents superseded by
deterministic preference tests. No broad deletion or experiment execution.

### Bounded R03/R05 passive native microphone recovery card

Tray **Choose microphone** and speech Settings now share a passive native card
with real endpoint IDs, System/default availability, retained unavailable pins,
unsaved local highlight, explicit revision-bound Save and separate Enable,
metadata Refresh, Disable and Stop speaking. The existing audited audio host
services own all changes; native input rechecks current ownership, Windows
privacy/permission, consent, readiness and original-channel/call revision.
No capture/test, automatic replacement, model/network or OS permission write
is introduced. Closing creates no durable answer, consent or session/task
decision. The [exact availability and authority boundary](Interaction_Fallback.md#delivered-bounded-native-microphone-recovery-card---2026-10-07)
leaves genuine R05 device-question orchestration, combined consent/enable,
first-run onboarding, production wake and native/hardware acceptance open.
All experiment executables/evidence are preserved: the maintained card tests
are not equivalent to historical synthetic speech proof or its live consumers.

### Bounded R17/R18 canonical maintenance foundation

The app now composes metadata discovery/state and an explicit Settings/Tray
native review/check/open/snooze route. It validates canonical unsigned
stable/beta release identity, immutable tag source, nine published assets and
bounded manifest digests, with ETag re-verification, numeric version ordering,
timestamps/staleness, per-run network consent, six-hour+jitter cadence,
backoff and ownership/privacy/protected-call invalidation.
This is notify-only: no binary/source download, updater, source mutation,
general proactive broker, model tool or unsolicited voice/focus. Full R17
installed/protected/native acceptance and the general R18 broker remain
open. Exact typed/activated `maintenance status`, `maintenance review` and
`maintenance snooze` now share the guarded native cached workflow. These
original-user requests record durable intent in a dedicated maintenance
session through the consolidated host gateway, revalidate generation/current
host and exact cache identity, and retain typed snooze request/terminal audit.
They never check/refresh metadata, open a browser, enable network consent,
answer a question/approval, or create model/runtime/wake authority. Responses
are complete bounded visual output; review opens the existing native surface.
The external source-bootstrap/publisher and all unique historical R02
evidence are preserved; no further experiment retirement is justified.
The new cached command/admission tests supersede no experiment executable:
release/protection/runtime/source receipts still cover distinct boundaries
and remain necessary evidence, not portable-command acceptance.
See the [distribution boundary](Distribution_And_Updates.md#delivered-bounded-r17r18-native-foundation)
and [native user workflow](../docs/settings.md#release-maintenance-notify-only).

The design describes the intended product; the repository currently implements a runnable Windows bootstrap.
None of A0, A1, A2, A3, A4, B, or C is established as an accepted, complete delivery by this review.
In particular, verified Ollama setup is not the local-first clipboard slice, opening several windows is not concurrent session execution, and installing PowerShell is not a script runner.

- **Delivered bootstrap:** the stated narrow behavior is wired into the application, with source and checked-in test evidence where available.
- **Partial:** an existing component can be reused, but required behavior or enforcement is missing.
- **Outstanding:** the required capability has no complete implementation.
- **Proof outstanding:** implementation or a candidate exists, but the required real-provider, hardware, containment, deployment, or acceptance evidence has not been established.
- **Optional/deferred:** not a prerequisite for baseline voice or the initial release; a separate capability gate applies.

The original inventory review inspected source and test definitions; it did not
perform new microphone, audible playback, real model, OS power or installer
trials. The supplemental R02 evidence below performs narrowly scoped real
Windows containment trials, not installed-application or full release acceptance.
The separate R02 Windows storage investigation performed the synthetic encryption, DPAPI and process-kill trials recorded below; these do not establish production persistence.
The original separate R02 local-inference follow-up added actual
missing-endpoint/unavailable-path observations and public
identity/licence/download metadata, not successful real model generations.
It performed no reference-floor or independently network-blocked model trials.
The later bounded production-host trial adds real setup, simple/long answers
and cancellation, not reference-floor or independently blocked offline proof;
see the [recorded result](Deferred_Validation.md#2026-10-05-bounded-local-inference-result).
The R02 runtime branch subsequently exercised an actual pinned SDK/runtime
against synthetic loopback providers, not a real hosted model/account.
Its partial results and explicit failures/blockers are recorded below.
Test-project names, mocks, helper truth tables, configured coverage thresholds, and publish jobs are not substitutes for those trials.
An implemented feature may therefore still have outstanding release proof.
The [acceptance criteria](Acceptance_Criteria.md), not this inventory, determine release readiness.

## R05/R14 Bounded Native Shared Question - 2026-10-07

The explicit tray **Review local version (native question)** entry composes
the existing durable local-version query, production question/authorization
services and private SQLite interaction store in an owned native presenter.
Ordinary typed/voice version dispatch remains unchanged. The shared component
supports single/multiple/text, explicit local edits/save draft/submit/cancel,
exact original-key/session targeting, immutable exact-record review and
visible conflict/expiry/privacy/audit failure recovery. Approval is a separate
authorization-service path, not ordinary question submit or grant consumption.
Complete native review must be displayed before approval; neither passive text
nor native input satisfies missing content/containment/deployment authority.

This delivered slice changes no task/interaction/evidence schemas, R04
recovery internals, scheduler, worker scripts, privacy observer, remote models
or effect dispatch. It does not migrate legacy action-name grants. The native
version entry admits no operation proposal and keeps mandatory-effect gates
closed. Exact operation/script bytes are not present in the existing proposal
contract; review honestly displays its complete actual binding record instead
of inventing bytes/digests. Full source/resource acquisition/review, generic
voice targeting, Sessions/history, effect gateways and D-001/D-005/D-008/D-009/
D-013 acceptance remain open. Native desktop/speech/accessibility trials need
separate approval and were not run.

Maintained [portable review tests](../tests/Kora.Application.UnitTests/Interaction/HostQuestionReviewServiceTests.cs)
and [native production-store tests](../tests/Kora.Windows.IntegrationTests/NativeQuestionTests.cs)
cover immutable review, single/multiple/text state/accessibility contracts,
real drafts/answers/reopen, stale/foreign/generation/expiry/raced replies,
live privacy/ownership narrowing, protected origin, missing mandatory gates,
audit rollback and the actual durable query/store/evidence path. Cancellation
uses existing Unknown no-replay recovery, not a fabricated successful receipt.

**Experiment disposition:** no executable experiment is wholly superseded.
RT1's actual SDK/runtime control witnesses, RT2 lifecycle/all-path Blocked
evidence, MG1's management/runtime envelope, containment/W2 and storage
interruption/protection proofs are unique and retained unchanged. New native
state/review tests are maintained production tests; they do not replace those
provider, worker, deployment or storage acceptance gates. Historical receipts
and distinct source/released artifact provenance remain intact.

### Bounded Native Question Local Validation

On main base `3d965770b06fecac9b265f747796cd26f252a8fe`, the complete Release
build passed with zero warnings/errors. Core/Application/Windows full suites
passed **365 / 1,086 / 627**, zero failed/skipped; the 22 native focused cases
are included, not added again. Fresh Core+Application coverage passed the
unchanged **100% line and branch** gates. Locked existing dependencies,
dependency licences, version/fake-release/payload policy tests and static
framework-dependent win-x64/win-x86 publishes passed. Results use unique
ignored `r05-qualified-*` test roots and `r05-native-win-*` publish roots.
These are local source/test/static-publish results, not installer/desktop,
screen-reader, speech, OS-transition or real-effect acceptance. Required
remote CI and actual PR merge are independent publication evidence.

After R03 #52 merged, the native slice rebased cleanly onto `ba1dc3d` without
changing its disposal/input guards or privacy documentation. Combined Release
and licence checks passed; full Core/Application/Windows suites passed
**365 / 1,095 / 647**, zero failed/skipped, with fresh unchanged **100% line
and branch** portable coverage under `r05-r03-rebase-*`.
The subsequent clean rebase onto R17 #55 (`3fc1417`) retained its immutable
source/publisher changes and all R03 privacy guards. Fresh combined Release,
licence and full-suite validation again passed **365 / 1,095 / 647** with
zero warnings/errors/failures/skips and **100% line/branch** portable coverage
under `r05-r17-rebase-*`. Publication ordering remains a coordinator-owned
hold until actual corrected main publication is verified, not a waived gate.

## R14 Native Passive Detail Slice - 2026-10-06

Implemented on isolated `agents/kora-r14-native-details-20261006`, based on
`c5dffabf8f4fa767147be06dd8b296238ea97da0` (main including #41-46 and #44's
detail design). The source checkout and completed sibling trees were not
edited, built, restored or reused. This remains an uncommitted handoff; no
commit, push, PR, merge or native real-effect trial was authorized.

**Delivered narrow path:** Documentation > selected embedded page > native
Open details. The exact page opens in an immutable reference/revision-bound
native passive viewer with provenance/sensitivity/digest chrome, source,
search and Unicode exact-source copy. Same-reference opens activate the
existing viewer; new revisions are distinct and conflicting same-reference
content is rejected. The guide, grants and viewer share one bounded local
Markdig/Avalonia pipeline. Native-text-v1 bounds are 256 KiB UTF-8 / 512 blocks /
4,096 nodes / depth 32 / 8 open viewers / 256 search characters; no silent
truncation or unbounded rendered tree.

**Stable handoff:** Core owns portable immutable classification/reference
validation; Application owns deduplication, search, generation-bound state and
privacy cleanup; desktop owns native presentation and explicit routing.
Finalized-response admission requires existing host session/request/task IDs.
No persisted interaction/question/grant schema, authority composition or
`MainViewModel` action dispatcher was changed. Embedded page references remain
process-local and explicitly not durable session authority.

**Still outstanding:** Sessions list/full conversation/history, work/queue and
Evidence workspace, durable response/artifact access/revocation, automatic
offers and verbal question targeting, shared native approvals, skill/script/diff
review, rich clipboard serializer, syntax grammars, HTML/browser/diagrams/assets
and scoped export. Closing, viewing and copying cannot approve/cancel/delete,
mark Done or refresh session activity. The compact response, pin/timeout and
privacy holds remain unchanged.

Validation is recorded with the final local receipt below; native visual,
screen-reader, contrast/text-scale, DPI, multimonitor, clipboard-platform and
live Kora/audio observations remain **pending separate scoped approval**.
See the [exact delivered profile](Information_Display.md#delivered-native-profile---2026-10-06)
and [window boundary](UI_Workspace_And_Windows.md#delivered-passive-details-boundary).
This does not complete R14 or accept A4.

### R14 Local Validation Receipt

Verified on 2026-10-07 local time in the same isolated uncommitted tree:

| Check | Actual result |
|---|---|
| Locked restore and root Release | Passed; 0 warnings / 0 errors |
| Core suite | 365 passed, 0 skipped |
| Application suite | 1,071 passed, 0 skipped |
| Windows integration suite | 559 passed, 0 skipped |
| Latest-only portable coverage | 100% line / 100% branch, Core + Application; only the fresh `native-details-qualified` pair aggregated |
| Native-detail targeted tests | 64 passed; actual parser/native-control/fake controller and keyboard/XAML contract evidence, not native visual acceptance |
| Dependency licences | Passed; existing notices current, no runtime package/grammar/renderer acquisition |
| Static win-x64 / win-x86 publish and payload checks | Passed; 201 / 197 exact files with reviewed licence texts. Existing Markdig/Avalonia present; no browser/diagram/generated-content or test runtime assets introduced |
| Diff whitespace | Passed |

The payload manifests explicitly label the source as
`c5dffabf8f4fa767147be06dd8b296238ea97da0+uncommitted-native-details`,
not a committed exact-revision release or official publication.
Portable TRX/coverage and Windows TRX are retained under the ignored
`.net-test-artifacts/native-details-qualified` directory; payloads/licence
evidence are under ignored `artifacts/native-details-*` directories.
An initial broad asset scan also matched the scanner's reviewed HTML licence
texts; the corrected runtime scan excludes only the canonical licence-text
directories, not executable application assets.

Tests cover immutable reference/revision conflicts, bounded capacity,
generation/late-render rejection, privacy cleanup, complete UTF-8 source,
native selection versus whole-source copy, disclosure/access races and platform
failure through a fake clipboard. Parser cases include exact/exceeded
byte/block/node/depth limits, hostile HTML/images/URI/diagram content, unknown
nodes, unavailable renderer, definition-only empty projection, and repeated
reference-link expansion bounded during projection building.
The existing host shutdown test fake was extended to hold final speech
completion, deterministically verifying that a real host exit still waits for
response/approval work; no `MainViewModel` production dispatch was edited.

Adding the existing desktop project as a Windows-test reference updated only
that test lock's transitive graph. The installer theme test uses its explicitly
qualified existing theme type to avoid the resulting namespace ambiguity;
installer/release/bootstrap production files were not modified.

No application/audio launch, OS/account/privileged operation, real clipboard
write, installation, sibling merge or publication occurred. The native visual,
assistive-technology/DPI/multimonitor and real platform-copy gates above remain
pending.

## R06 bounded read-only foundation - 2026-10-07

The production registry composes only `capabilities.list/get`,
`application.get_version`, `readiness.get`, and `runtime.list/get_status`.
Authoritative descriptors and exact local routes share canonical IDs; existing
help/version/status behavior is preserved. Readiness uses timestamped existing
probe/setup observations without fresh I/O or inference; deployment is explicitly
unobserved by the version provider. Runtime status reports only the existing
local adapter and does not imply permission or tool-loop qualification.
Inputs reject unknown/duplicate fields, IDs and ranges. Callers bind to the
registry instance and live current host request; unknown lanes, foreign traces,
replayed/completed contexts, lost ownership and late cancellation fail closed.
Management gets only the six minimal read-only descriptors, not execution
catalogues or instructions. Input/output complete UTF-8 limits are 1,024/4,096
bytes; list record limits are six with explicit page continuation.

Maintained descriptor/handler/native-route, hostile-input, isolation,
cancellation, structured-log/trace and exact-byte tests pass. Root Release
build/analyzers, all required suites and fresh portable 100% line/branch coverage
pass. This is not full R06, R02 qualification or A0-Gate0 acceptance.

Experiment disposition: strict typed/hostile authority input, isolated host
context and complete-byte boundary cases now have maintained production
equivalents. No experiment executable is retired: RT1 SDK all-status/egress
controls, RT2 blocked native/lifecycle observations, MG1 32 KiB model envelope,
deadline/ack/quota/quarantine proofs, and Node volatile filesystem/provider
comparisons are not equivalent to this read-only host registry and remain.

## Delivered and Partial Implementation

The identifiers in this table are inventory references, not new capability or model-tool IDs.

| Ref | Status | What exists and its boundary | Source and test evidence |
|---|---|---|---|
| I01 | Delivered bootstrap; deployment proof outstanding | .NET 10/Avalonia Windows composition with portable Core, Tools, Definitions and Application projects and first-party Windows services. Tools/Definitions depend on Core, not Application/Windows/desktop. No Copilot SDK adapter or MCP runtime is composed. | [Composition](../src/Kora/Program.cs), [desktop project](../src/Kora/Kora.csproj), [tool layout](../src/Kora.Tools/README.md), [definitions](../src/Kora.Definitions/README.md), [dependency versions](../Directory.Packages.props) |
| I02 | Delivered bootstrap and bounded read-only registry | All 25 built-in actions retain normalized whole-phrase routing, configured-name prefixes and C# dispatch. Six canonical read-only descriptors compose per-action Kora.Tools implementations behind the Application gateway and exact local discovery before inference; help points to that catalogue. Separate clipboard/settings/session controls and artifact instruction selection do not extend this six-ID registry or admit a model tool/result loop. Existing lock/power handlers remain unchanged. | [Actions](../src/Kora.Core/Commands/BuiltInAction.cs), [command catalogue](../src/Kora.Core/Commands/BuiltInCommandCatalog.cs), [read-only descriptors](../src/Kora.Core/Tools/ReadOnlyCapabilityCatalog.cs), [tool implementations](../src/Kora.Tools/README.md), [registry/tests](../tests/Kora.Application.UnitTests/Tools/ReadOnlyCapabilityRegistryTests.cs), [native wiring/tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.Capabilities.cs) |
| I03 | Delivered bootstrap and bounded native surfaces; A4 partial | Presence, Settings, compact response, embedded documentation/passive details, legacy grant editor, durable native version question/review, minimal Sessions workspace, clipboard preview, read-only Evidence and notify-only maintenance. Tray includes metadata-only refresh/selection, explicit listening enable/disable and playback stop; Tray/Settings share a passive exact-endpoint microphone recovery card with separate Save and Enable. Full conversation/history, work/queue and durable generic recovery questions remain open. | [Desktop composition](../src/Kora/App.axaml.cs), [tray](../src/Kora/SystemTrayController.cs), [passive card](../src/Kora.Application/ViewModels/MicrophoneRecoveryViewModel.cs), [response surface](../src/Kora/ResponseWindow.axaml), [application view model](../src/Kora.Application/ViewModels/MainViewModel.cs), [tray tests](../tests/Kora.Windows.IntegrationTests/TrayRecoveryTests.cs), [embedded-guide tests](../tests/Kora.Application.UnitTests/Documentation/EmbeddedUserDocumentationProviderTests.cs) |
| I04 | Implemented PTT/ownership foundation; deterministic privacy lifecycle regressions; native acceptance partial | Microphone/output enumeration, device preferences, consent/readiness and Enable/Disable listening are wired. Startup may enable readiness after fresh gates; capture remains closed until held PTT. Settings supports mouse/Space/Enter PTT with release/focus-loss closure. A per-SID global owner coordinates cross-build activation/takeover/return; only Owner composes services. Existing WTS/power/MMDevice observation and the one-second permission polling fallback have deterministic observer/capture regression coverage, including negative closure before session requery and query/disposal ordering. Complete native lock/disconnect/suspend/device and polling acceptance remains unproved. | [Application orchestration](../src/Kora.Application/ViewModels/MainViewModel.cs), [PTT controls](../src/Kora/SettingsWindow.axaml.cs), [owner coordinator](../src/Kora.Windows/Coordination/WindowsInstanceCoordinator.cs), [Owner-only composition](../src/Kora/Program.cs), [observer regressions](../tests/Kora.Windows.IntegrationTests/Session/WindowsPrivacyObservationServiceTests.cs), [bounded R03 evidence and remaining trials](Deferred_Validation.md#r03-windows-ownership-and-audio-privacy) |
| I05 | Partial voice proof; deterministic lifecycle coverage | Windows phrase grammar and assistant-name-prefixed dictation produce local transcripts during an explicit activated capture, not automatically at startup. Held PTT, bounded capture and stale-generation rejection are implemented. Observer-to-capture tests cover negative sessions, permission polling, pending native opens, System versus pinned endpoint loss and held-activation shutdown; application disposal retires already queued transcripts/completions and cancellation-ignoring opens. This is not the designed wake-only ambient pipeline; production wake/pre-roll/acoustic quality and complete packaged-native acceptance remain outstanding. | [Recognition service](../src/Kora.Windows/Audio/WindowsVoiceRecognitionService.cs), [activation/privacy orchestration](../src/Kora.Application/ViewModels/MainViewModel.VoicePrivacy.cs), [audio stream](../src/Kora.Windows/Audio/BlockingAudioStream.cs), [composed capture regressions](../tests/Kora.Windows.IntegrationTests/Audio/ActivatedVoiceRecognitionTests.cs), [application privacy regressions](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.VoicePrivacy.cs) |
| I06 | Delivered bootstrap; deterministic privacy/output ordering; acoustic proof outstanding | Windows TTS plus separately offered optional Kokoro assets/provider; local installation/hash checks, voice/device selection, preview, playback stop and visual fallback for unavailable/muted output. PTT stops output before opening command capture. An observed unavailable System output invalidates active speech before queued endpoint enumeration; pinned output and eligible default reroutes are preserved. Queued recovery presentation is rejected after disposal. These regressions do not prove acoustic echo/playback rejection or optional owner-aware privacy. | [Speech service](../src/Kora.Windows/Audio/WindowsTextToSpeechService.cs), [Kokoro](../src/Kora.Windows/Audio/KokoroTextToSpeechProvider.cs), [privacy coordination/tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.VoicePrivacy.cs), [speech tests](../tests/Kora.Windows.IntegrationTests/Audio/WindowsTextToSpeechServiceTests.cs), [Kokoro tests](../tests/Kora.Windows.IntegrationTests/Audio/KokoroTextToSpeechProviderTests.cs) |
| I07 | Delivered bootstrap and bounded durable milestones; full R04 partial | Bootstrap setup ledger remains separate. Standalone exact local version-query input composes private standard-SQLite intent/dispatch/evidence/terminal records and Interrupted/Unknown no-replay startup recovery. Log/audit/span/link due dates are independent; bounded ordinary startup pruning is delivered, not audit/session/copy deletion acceptance. | [Storage probe](../src/Kora.Core/Dependencies/StorageDependencyProbe.cs), [actual task store](../src/Kora.Windows/Storage/WindowsSqliteHostTaskStore.cs), [composed fixtures](../tests/Kora.Windows.IntegrationTests/Storage/DurableStorageCompositionTests.cs), [bounded retention](#r04-bounded-ordinary-diagnostic-retention---2026-10-07) |
| I08 | Delivered bootstrap | Capability readiness and separate consented setup orchestration; PowerShell 7.4+ probing/install/re-probe is independent of local inference. Open Setup installs nothing. No general PowerShell task worker or executable grant is supplied by readiness. | [Application setup](../src/Kora.Application/ViewModels/MainViewModel.cs), [PowerShell setup/tests](../tests/Kora.Windows.IntegrationTests/Dependencies/WindowsPowerShellSetupServiceTests.cs), [dependency bootstrap](../src/Kora.Core/Dependencies/DependencyBootstrapper.cs) |
| I09 | Delivered bootstrap; bounded production evidence, D-003 open | Consented per-user Ollama 0.35.1 setup and pinned `qwen3:1.7b` download; loopback runtime/model/digest checks, no automatic cloud fallback. R02 identity/licence/unavailable-path evidence and 31 deterministic harness tests remain distinct from the later actual production-host setup, simple/long answers and active model/speech cancellation without stale completion. CPU-floor quality, latency/resource/context budgets, repeated race/computation-cessation timing, installer provisioning and independently network-blocked offline qualification remain open. | [Ollama setup](../src/Kora.Windows/Dependencies/WindowsOllamaSetupService.cs), [inference probe](../src/Kora.Windows/Dependencies/LocalInferenceDependencyProbe.cs), [setup tests](../tests/Kora.Windows.IntegrationTests/Dependencies/WindowsOllamaSetupServiceTests.cs), [probe tests](../tests/Kora.Windows.IntegrationTests/Dependencies/LocalInferenceDependencyProbeTests.cs), [bounded production result](Deferred_Validation.md#2026-10-05-bounded-local-inference-result), [R02 technical plan](Local_Inference.md) |
| I10 | Partial model interaction; bounded host discovery separate | Unmatched requests use one local reasoning operation with bounded status context, optionally explicitly selected artifact instructions/source identity, and exactly one answer, question, action or grant-change response. User request length is 4,096 characters; inference deadline is two minutes; question prompts/options and three follow-ups are bounded. Generation is buffered, not streamed. Busy guards reject competing requests; handler results are not returned for tool-loop continuation. The six-ID native read-only registry is not advertised to this unqualified selector, remote SDK or MCP. | [Reasoner](../src/Kora.Windows/Dependencies/WindowsOllamaReasoner.cs), [reasoner tests](../tests/Kora.Windows.IntegrationTests/Dependencies/WindowsOllamaReasonerTests.cs), [application/tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.cs), [current protocol](Internal_Model_Tools.md#implemented-bootstrap-surface), [selected instructions](../docs/commands.md#run-skills-and-future-artifacts) |
| I11 | Bounded durable questions/authorization foundation; general execution partial | Legacy model-action Once/Session/Always preferences remain separate: Session is an in-memory action-name set; Always is JSON storage. The production interaction store additionally persists exact proposals/grants, typed questions/drafts/answers and session generations with atomic typed audit, through the native local-version review route. This does not migrate legacy grants or admit general content-bound effect/script execution. | [Legacy preferences](../src/Kora.Application/Configuration/LocalModelApprovalPreferences.cs), [question service](../src/Kora.Application/Interaction/HostQuestionService.cs), [authorization service](../src/Kora.Application/Interaction/HostAuthorizationService.cs), [actual interaction store](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.cs), [native production-store tests](../tests/Kora.Windows.IntegrationTests/NativeQuestionTests.cs) |
| I12 | Partial computer controls | Exact lock stops owned audio then calls the Windows lock API without the model-action gate; model-proposed lock uses that gate. API acceptance is not independent observation of lock completion. Shutdown/restart create inspectable, cancellable proposals only; no OS power request is sent. No embedded lock/power scripts or all-session power coordination are implemented. | [Host action handlers](../src/Kora.Application/ViewModels/MainViewModel.cs), [Windows session controller](../src/Kora.Windows/Session/WindowsSessionController.cs), [application tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.cs), [session-helper tests](../tests/Kora.Windows.IntegrationTests/Session/WindowsSessionControllerTests.cs) |
| I13 | Partial lifecycle/work controls with bounded durable recovery | Show/hide/exit/current-app restart, stop speaking and cancellation of current setup/inference. Cross-build handoff/return and controlled shutdown have bounded x64 observations; x86 installed-runtime and complete failure/privacy acceptance remain open. Exact local version queries and session control intents use the composed durable ledger/recovery; intent-only becomes Interrupted, dispatch without verified receipt becomes Unknown, without replay. Bootstrap status is not a general work ledger; model/OS effects, queues and concurrent scheduling are not promoted to that durable path. | [Host controls](../src/Kora.Application/ViewModels/MainViewModel.cs), [durable query](../src/Kora.Application/Hosting/DurableVersionQuery.cs), [recovery](../src/Kora.Application/Hosting/DurableHostRecovery.cs), [process controller](../src/Kora/DesktopApplicationProcessController.cs), [owner coordinator](../src/Kora.Windows/Coordination/WindowsInstanceCoordinator.cs), [bounded lifecycle evidence](../docs/voice-and-audio.md#r03-validation-evidence) |
| I14 | Bounded manual mode and native/exact parity delivered; full R15 partial | Native/typed/activated current-run manual on/off/reset-off and passive cached discovery/get/status preserve independent automatic Active/Suspected/Unknown/unavailable evidence. Genuine original-user intent/session generation, own live context, current source revision and captured native lifetime/ownership/privacy/input gates protect transitions. Required requested/outcome audits reuse the consolidated SQLite lease; lost evidence holds conservative protection without fake rollback. Changed state retires old speech/input/callbacks without off/reset replay or capture reopening. Reserved routes preserve full pending questions/approvals. Saved flags, reusable-grant records and unavailable exact relaxation/exceptions are unchanged. Automatic detection and native/acoustic/call acceptance remain outstanding. | [Communication policy](../src/Kora.Application/Communication/CallCommunicationPolicy.cs), [shared control](../src/Kora.Application/Communication/ManualCallControl.cs), [native controls](../src/Kora/SettingsWindow.axaml), [route tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.ManualCallCommands.cs), [real required audits](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteManualCallControlTests.cs), [bounded parity](Call_Aware_Speech.md#delivered-manual-command-parity---2026-10-08) |
| I15 | Delivered bounded diagnostics/evidence and ordinary pruning; full history/audit acceptance partial | Source-generated structured diagnostics, daily JSON and private SQLite log/audit/span/link projections; typed read-only query/native inspector with authenticated pagination, stable citations and explicit gaps. Independent DailyLog reads bounded immutable prefixes; All stays SQLite-only, file audit mirrors are unsupported and no file span graph is invented. At most 50 records / 64 KiB serialized output. Ordinary startup pruning preserves audits/authority and rejects removed/reused snapshot ceilings. No combined source ranking, model evidence tools, atomic interaction-audit projection, full history, audit pruning, export or Ask Evidence. | [Query service](../src/Kora.Application/Diagnostics/DurableEvidenceQuery.cs), [reader routing](../src/Kora.Windows/Storage/WindowsEvidenceReader.cs), [daily reader](../src/Kora.Windows/Storage/WindowsDailyEvidenceReader.cs), [native viewer](../src/Kora/EvidenceViewModel.cs), [daily tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsDailyEvidenceReaderTests.cs), [bounded retention](#r04-bounded-ordinary-diagnostic-retention---2026-10-07) |
| I16 | Delivered binary installer, source build-only/tool distribution and notify-only maintenance; installed/production acceptance outstanding | WiX 7 MSI/custom Avalonia Burn packages win-x64 with scoped startup, completion launch, preflight and required/optional dependency handling. Linux builds/cross-publishes; Windows packages exact transferred bytes. Recorded main publication proves an unsigned POC release at its own exact revision, not acceptance of every later main. Immutable source-tool acquisition/channel resolution and native canonical metadata review are delivered, without activation/updater authority. Maintained source/native tooling supersedes distribution-only executables; NSIS stays retired and receipts unchanged. Source activation, silent related-bundle upgrades, beta numeric upgrade ordering, external-asset and protected/runtime-only/resource acceptance remain open; x86 static output lacks ONNX native inference closure and is not x86 installer acceptance. | [Installer](../installer/README.md), [CI workflow](../.github/workflows/ci.yml), [publication receipt](Distribution_And_Updates.md#merge-and-ci-follow-up-2026-10-06), [archival disposition](../experiments/r02-distribution-proof/README.md), [source acquisition](Distribution_And_Updates.md#immutable-tool-acquisition-and-channel-resolution), [native maintenance](#bounded-r17r18-canonical-maintenance-foundation) |
| I17 | Experimental containment evidence; production admission blocked | Fixed AppContainer/Job Object/PowerShell proof: 63/71 OS assertions met; protected stand-ins/credential denied, descendant identity/lifetime observed, lost/malformed receipts remain Unknown. Eight network-denial assertions unproven; executable dependency allowlisting and normal-host deployment protection not established. No production worker or bundled catalogue. | [Measured snapshot](../experiments/r02-containment-proof/evidence/README.md), [canonical outcomes](Security_Data_Flows.md#r02-windows-containment-outcomes), [continuation gates](Security_Data_Flows.md#windows-containment-continuation-gates) |
| I18 | Experimental Node/.NET RT1, bounded RT2 observations and released MG1; production Gate 0 incomplete | Historical Node/source-built RT1 unchanged; 45/45 RT1 controls with rejected hook-only FAIL. RT2 final source reproduction and staged controls pass; 20/20 tests plus two locale contracts pass, but native helpers/transient writes and observer limits keep all-path admission BLOCKED. Separately approved released SDK 1.0.16 / unchanged native 1.0.90 MG1 repeats 45 controls, 22 host and 16 actual runtime cases: complete bytes, stalled-ack dispatch deadline, monotonic admission/races/no retry, held topology and Unknown quarantine pass. MG1's initial local source reproduction blocker retained; no artifact equivalence claimed. PV1 and production adapter/scheduler remain gated. | [Historical Node evidence](../experiments/r02-runtime-proof/evidence/results.json), [historical .NET RT1](../experiments/r02-dotnet-control-proof/README.md), [RT2 evidence/handoffs](../experiments/r02-runtime-lifecycle-proof/EVIDENCE.md), [MG1 fixture/disposition](../experiments/r02-dotnet-management-proof/evidence/disposition.json), [technical outcome](Runtime_Provider_Feasibility.md) |
| I19 | Bounded sessions workspace/metadata/exact commands delivered; full R12/R13/R14 partial | Durable Active/Done IDs/generations, typed questions/current task pages, selected-session evidence, guarded idle Done/resume, bounded names and empty Create/exact-ID Rename with validated v1/v2 interaction migration. Native and exact typed/activated-voice operations share host authority; pending decisions, live/Unknown work and stale revisions remain blockers. No conversations/queues, name routing, scheduler, automatic lifecycle/deletion or model session tools. | [Shared service](../src/Kora.Application/Hosting/SessionWorkspaceService.cs), [command path](../src/Kora.Application/Hosting/SessionWorkspaceService.Commands.cs), [workspace tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionWorkspaceTests.cs), [metadata tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionMetadataTests.cs), [command tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionCommandTests.cs), [user syntax](../docs/commands.md#bounded-exact-id-session-commands) |
| I20 | Bounded appearance/installed speech registries delivered; full R10 partial | Nine appearance preferences plus installed provider/voice choices share native/exact typed/activated-voice discovery/get/set/reset, domain bounds, revisions, atomic save and typed audit. Desired/effective/default speech state and invalid/missing selection recovery remain explicit; no settings model tools or general configuration authority. | [Appearance service](../src/Kora.Application/Configuration/AppearanceConfigurationService.cs), [speech service](../src/Kora.Application/Configuration/SpeechConfigurationService.cs), [user configuration](User_Configuration.md), [user settings](../docs/settings.md) |
| I21 | Bounded clipboard preview and artifact invocation delivered; full R07/R11 partial | Explicit immutable plain-text preview/same-ID reuse/revoke is model-free. Separately, source-qualified slash/activated-voice artifact selection supplies instructions to the existing local JSON selector. Neither path enables scripts, arbitrary effects, a mediated tool loop, clipboard explanation/egress or agents. | [Clipboard broker](../src/Kora.Tools/Clipboard/ClipboardSnapshotBroker.cs), [artifact router](../src/Kora.Core/ArtifactInvocation/ArtifactCommandRouter.cs), [embedded catalogue](../src/Kora.Definitions/ArtifactInvocation/EmbeddedArtifactCatalogue.cs), [disk discovery](../src/Kora.Windows/ArtifactDiscovery/WindowsDiskArtifactDiscovery.cs), [user invocation](../docs/commands.md#run-skills-and-future-artifacts) |

### Most Important Design-to-Code Gaps

1. **Privacy and ownership acceptance:** startup arms gated PTT without ambient capture. External WTS/power/MMDevice observation and a one-second permission polling fallback already exist; deterministic production observer/capture/application tests cover closure, run holds, stale callbacks and disposal. The remaining gap is complete native event/detection/release timing, routing, ownership and hardware acceptance, including every reference lock release within 500 ms. Production wake remains unavailable; regression tests do not certify continuous listening.
2. **Authority:** exact lock and model lock use different gates. Existing reusable preferences are not complete execution grants. Future direct, UI, model and skill paths must converge on the same host-owned gateway; do not migrate action-name preferences into broader script authority implicitly.
3. **Useful model tools:** the six-ID read-only host registry, explicit clipboard broker and bounded appearance/installed speech registries are delivered foundations, not model admission. Mediated tool/result iteration, qualified clipboard answering/egress, remote runtime and model-result streaming remain absent. A selector answer, action enum or read-only catalogue is not this contract.
4. **Managed sessions:** consolidated durable task/question/audit authority, minimal workspace/lifecycle, bounded names/native creation and exact/native task observation plus current-run local-version pre-dispatch cancellation exist. Full conversations/history/artifacts, general queues, resource-leased scheduling, automatic lifecycle/deletion and independent management remain open. Passive pages and bounded task status are not a concurrent work ledger or proposed model session tools.
5. **Skills and integrations:** immutable bundled package/native file review and source-qualified bundled/profile instruction invocation exist. No admitted multi-script executor, MCP connector, general skill enablement/source-management registry, declarative authoring or agent runtime is delivered. Inspection/selection never promotes script content to execution trust.
6. **Release proof:** unit/fake-backed tests, endpoint enumeration, local digest/readiness checks and downloadable CI archives do not close the real Windows, runtime, containment, installation or performance gates.
   The next local-inference action is to assign/approve a reference test owner,
   environment and budgets, then consent to pinned provisioning; it is not to
   implement a tool loop or choose a larger model while evidence is absent.
   Follow the [R02 local-inference continuation](#r02-local-inference-continuation).
7. **Runtime path after R02:** I18 rejects hook-only integration, not Copilot
   outright. Preserve the scoped source-built .NET RT1 pass, then prove global
   runtime observation, the full .NET management envelope and approved provider
   eligibility. Do not count Node
   loopback passes as production authority, account capacity or .NET parity.

## Ordering Rules

The order below is an implementation work-package order, not a renumbering of the product's A0-A4 acceptance checkpoints.
Some A3 primitives, notably session identity, immutable intent, approvals and storage, must be implemented early so A0's context/tool path cannot accumulate incompatible transient authority.
That does not mean the complete A3 product is accepted before A0.

- **P0:** privacy, authority, data integrity or feasibility blocker. Complete the relevant gate before exposing dependent functionality.
- **P1:** core user value and required initial-release capability.
- **P2:** integration/authoring expansion after the core interaction is accepted.
- **P3:** independently optional or deferred expansion.

Earlier numbered work is the preferred start order. The **Needs** column is the
scoped dependency graph described in [Reading Needs](#reading-needs); a higher
number does not imply all earlier packages are hard prerequisites.
Independent bounded production features, prototypes and tests can proceed and
merge in parallel once their implementation dependencies and affected checks
are satisfied; unrelated enablement/RC evidence need not be complete.
Do not enable a dependent capability with an unresolved earlier safety/correctness failure.
Use the current router, setup, speech services, preferences, native surfaces and tests as migration foundations; do not discard working bootstrap behavior to replace everything at once.
Keep current/planned labels and unavailable-tool exclusion until the exact replacement path is verified.

## Design Reconciliation Before Expansion

R01's initial-release choices were approved on 2026-10-05 in the reconciliation
session. The [decision register](Decision_Register.md#r01-accepted-policy-reconciliation)
records that approval and separates accepted contracts from implementation
evidence. No runtime behavior was changed or acceptance trial performed by R01.

| Issue | Affected contract | Required resolution and affected work |
|---|---|---|
| Management power authority | [Management boundary](Security_Data_Flows.md#management-power-proposal-authority) and [power proposal tools](Internal_Model_Tools.md#computer-controls-and-notify-only-maintenance) now agree. | Resolved: M/E submit proposals only; host lifecycle owns approval/countdown and gateway/worker dispatch. No M task tools or self-approval. R06/R13/R16 exposure still requires implementation evidence. |
| Capture after restart/unlock/resume | [Canonical matrix](Security_Data_Flows.md#microphone-consent-and-enablement-matrix), [lifecycle](Task_Lifecycle.md), [configuration](User_Configuration.md) and acceptance are aligned. | Resolved direction: explicit first-launch ongoing consent; saved consent permits fresh-gated ordinary startup/restart. Normal authoritative unlock restores only the previously enabled mode after confirmed closure and fresh gates; PTT never resumes capture and production wake remains gated. Resume/manual-disable/other loss recovery requires explicit Enable listening; withdrawal persists. R03/R09/R17 native acceptance remains separate. |
| Optional private-speech fallback | [Design index](README.md), [lifecycle](Task_Lifecycle.md), [MVP scope](MVP_Scope.md) and [security](Security_Data_Flows.md#explicit-verification-and-output-privacy) condition owner-aware fallback on enablement. | Resolved: baseline voice needs no verifier; selected owner-aware protection falls back visually on uncertain/unavailable confidence and never silently switches Off. R09/R15 and optional R24 use this boundary. |
| Acceptance placement | Earlier additional-capability wording classified Ollama/independent executors outside MVP despite A2/A3 requirements. | The [additional gates](Acceptance_Criteria.md#additional-capability-evidence) are reclassified with this roadmap; retain concrete A2/A3 proof rather than treating it as a later enhancement. |
| Standalone lock Session grants | [Bundled skill binding](Built_In_Skills.md#standalone-lock-work-session-binding) defines durable identity before approval/priority dispatch. | Resolved: create a new Active control work session for an unaddressed standalone request; commit and present binding before offering Session. Deliberate Active-session addressing is preserved; no selected-window authority. R04/R05/R11 prove it. |
| Deferred application rollback | [Execution design](../docs/skill-and-task-execution-design.md) distinguishes permanent bundled-content revocation from standalone application-binary inapplicability. | Before R27, specify whether restoration of old standalone application bytes can restore applicability. This deferred decision does not block read-only/initial fixed bundled capabilities; do not generalize bundled revocation to it without a decision. |

## Ordered Outstanding Work

Every completion condition below also requires directly related documentation,
negative-path tests and applicable [acceptance evidence](Acceptance_Criteria.md).
Full package/capability completion is distinct from merging a tested bounded
delivery under tier 1; E/Q conditions constrain exposure/qualification, not
unrelated work.
Opening an interface or passing a fake-only happy path is not completion.
Every body of work must drive an actionable change to implementation or canonical design/technical documentation.
For feasibility work, record the selected direction or rejection, material limitations, remaining gates and concrete downstream requirements here and in the owning contracts.
A checked-in experiment or report alone is supporting evidence, not the outcome or completion of a work package.

R02 contains independently closable runtime/provider, local inference, speech/hardware,
Windows worker/deployment, storage/key and distribution proof branches.
An R02 dependency names the relevant branch below, not a requirement to finish
unrelated vendor/packaging investigations before safe local work can proceed.
R03's existing-host privacy/ownership work can start after R01 and runs its own
actual Windows trials; it need not wait for a Copilot integration decision.
R01's required policy decisions are now resolved: R02 feasibility branches and
R03 ownership/privacy implementation are unblocked at the design dependency.
Their technical and real-boundary acceptance gates remain outstanding.

### R02 Windows Containment Follow-Up

The [measured worker proof](../experiments/r02-containment-proof/evidence/README.md)
narrows the next work rather than closing R02: reject Job-only restricted
execution and retain capability-free AppContainer as a partial candidate.
[D-013](Decision_Register.md#d-013-windows-worker-and-deployment-containment)
owns the unresolved mechanism/admission decision; the
[security contract](Security_Data_Flows.md#windows-containment-continuation-gates)
defines the exact gates without weakening the existing bundled-script design.

The labels below are follow-up work within the existing packages, not new tool
IDs or replacements for the R01-R29 dependency graph.

| Follow-up | Package / owner | Current state and next deliverable | Closure criterion |
|---|---|---|---|
| W1 - Attribute network denial | R02 / Windows and security leads | Eight timed-out assertions remain unproven. Repeat fixed trials on a supported reference OS with positive controls and attributable OS enforcement observations; cover applicable protocols/address families and descendants. | Required paths are actually denied by the worker boundary. Timeouts/unknown diagnostics do not pass; request approval before privileged/disruptive trials, never substitute global policy changes. |
| W2 - Resolve fixed-action and dependency mechanism | R02 / Windows and security leads | [Owned W2 fixture](../experiments/r02-w2-dependency-proof/README.md) exercised real ACL/child-policy denials and undeclared code bypasses; exact candidates rejected. Separate embedded helper/entry fixed marker and Unknown/cancellation trials measured. Owner accepts best-effort transitive tracking for bundled/future scripts, user responsibility and all manifest files in review tabs; declared bytes remain exact. Native fixture loading blocked by missing compiler; actual OS controls not run. | Dependency/review policy reconciled under D-013, not strict admission success. Prove the actual fixed-control worker/receipt contract and protected runtime resolution under remaining W1/W3 gates; separately decide a typed broker only if needed. No ambient-shell fallback or implicit script replacement. |
| W3 - Prove identity, protected roots and aliases | R02 deployment feasibility, then R17 installed acceptance / release and security leads | Independently owned payload/parents and actual normal-app/worker tokens UNTESTED. Use [the security identity checklist](Security_Data_Flows.md#protected-deployment-identity-and-validation); distribution packaging/runtime-only follow-up is separate. | Approved Windows fixtures demonstrate effective app/worker denial, protected dependency resolution and alias/TOCTOU handling. Build/inspect-only constraints leave these trials blocked, not waived. |
| W4 - Integrate and accept only the admitted profile | R11 / application and Windows leads; R16 exact effects; R17 installed launch | No production worker/catalogue. After applicable W1-W3 gates pass, wire immutable snapshots, common grants, bounded supervision/receipts; test host death and effect/cancellation races. | R11 fixed-profile admission, R16 actual approved controls and R17 installed evidence pass separately. General executable imports remain R27 work. |

W1/W2 investigation and W3 build/inspection may proceed independently after
R01. No R11 restricted dispatch is exposed from partial I17 evidence. Pure
catalogue/gateway development may remain disabled while proof is outstanding;
R16 cannot expose new script-backed controls until the applicable R11 gate passes.
R17 packaging work can proceed without an unmerged worker dependency, but
absent workers/catalogues and unperformed deployment trials remain explicit
acceptance blockers. The current bootstrap's direct C# lock behavior is unchanged
and still lacks the future common content-bound gate.

The [deferred-validation register](Deferred_Validation.md) and
[containment testing checklist](../experiments/r02-containment-proof/README.md#outstanding-testing-checklist)
make the remaining W1-W4 interactive/privileged trials runnable as separately
approved future work. Merging partial research does not close those gates or
enable the affected profiles.

### R02 Storage/Key Outcome and Follow-On Work

The 2026-10-05 [storage investigation](../experiments/r02-storage-proof/README.md) now drives
the [Windows durable-storage contract](Architecture.md#windows-durable-storage-direction) and
[D-009](Decision_Register.md#d-009-session-persistence-and-retention), rather than leaving recommendations only in an experiment.
Recorded evidence: passing automated assertions, 14 actual terminated/recovered child processes, same-user DPAPI/ACL checks, Windows x64/x86 native publication, and measured small-record read/write performance. Historical snapshots and revised profile-integration results are distinguished in the owning proof.
No production store, schema, grants, lifecycle or dispatch implementation was changed.

| Work / owner | Outcome or remaining action | Status / dependency consequence |
|---|---|---|
| R02 storage/key direction - storage and security leads | Historical page-encryption/DPAPI candidate findings retained; owner-approved D-009 now selects standard SQLite with private supplied-profile permissions and readable-copy disclosure. | Encryption/key/rekey admission superseded, not a current R04 prerequisite |
| Standard storage integration - storage, security and release leads | Existing pinned provider/native closure, actual private ACLs, durable transactions/recovery and ordinary package/licence/loading checks. | Actual bounded task store implemented/tested; host/evidence composition and lifecycle remain partial. Installed loading is separate R17 evidence. |
| R04 durable foundation - storage and application leads | Host identity/state, tracing, evidence envelopes and ordered persistence/recovery compose the exact version query. The subsequent durable interaction slice persists bounded typed questions/grants/session authority and atomic typed audit under the approved standard-SQLite baseline. Windows optional key/artifact primitives remain separate. | Partial integrated delivery; broader content/copy/retention/deletion and installed recovery remain open; see the delivery/handoffs below |
| R12 lifecycle/deletion - storage, security and application leads | Inventory/remove or rewrite managed recoverable copies, prevent late appends, preserve unrelated sessions/independent grants, and integrate source revocation, retention/apply-now/live/unknown-work policy. Disclose exported/provider/forensic limits. | Outstanding; deleting rows/current keys alone cannot satisfy D-009 |

Windows is the only supported product OS. Linux runtime support and local Linux-host cross-build validation are outside this storage proof, not additional gates.
Existing Linux-hosted Windows CI/distribution requirements are unchanged.
Ordinary cross-profile isolation is a trusted Windows boundary for this profile-local architecture; a second-account OS-denial trial is optional, not a routine application/merge gate.
The [profile-boundary contract](Architecture.md#profile-boundary-and-validation-responsibility) requires application integration checks and defines changes that would trigger actual multi-account validation.
The [storage deferred-validation checklist](Deferred_Validation.md#storage-admission-follow-up) makes remaining native/deployment and R04/R12 work actionable without requiring an unlocked console for safe scratch reruns.
Other R02 branches remain independently outstanding; the storage results do not close provider, speech, worker or distribution investigations.

### R04/R14 Bounded Durable Evidence Inspection - 2026-10-07

Implemented in isolated `agents/bounded-durable-evidence-query-implementation`,
after fetching/rebasing exact `origin/main` `3bc915bb2f67cda20340b005f6edcd12b9d19c18`.
The slice owns query/decoding/native inspection only; no unpublished sibling
code, model exposure, store migration or runtime/grant change was consumed.

Delivered: typed actual log/audit/span/link filters and cited exact reads,
session/task/request/invocation/approval/audit/W3C correlation, deterministic
committed-time/source/ID/link-ordinal ordering, authenticated query/viewer/
expiry-bound snapshot pages, 50-record/64-KiB serialized-output limits,
redacted exact property/safe-text filtering, parent/link navigation, explicit
expired-but-present/missing-or-removed/unavailable states and a composed native
tray inspector. Read-only admission reuses the sink's authoritative envelope,
projection, schema and private-file/ACL/reparse/journal validation.
Query logging/completed spans cannot expand an in-progress snapshot.

The current-user native host admits access; query filters and record text
never select a session's authority. The evidence projection is not the atomic
interaction audit. Due dates do not prove pruning or complete retained history.
Session/conversation sources, model tools/Ask Evidence, exact combined
daily-file/database queries, export, deletion and richer history/artifacts
remain unavailable. Independent daily diagnostic inspection is delivered below.
No live application/installer/audio/provider/privileged trial was performed.

Experiment disposition: no executable is retired by this slice.
`r02-storage-proof`'s FTS/HMAC equality queries measure encrypted-candidate
leakage/performance and are not equivalent to this standard-SQLite projection;
its DPAPI, encryption, rekey, native, backup and child-kill cases stay unique.
Node/.NET RT1, RT2 and management proofs concern actual provider mediation,
diagnostic observation, volatile session files and causal runtime boundaries,
not this production read-only evidence API. Existing consumers and historical
receipts remain intact; preserving them is intentional, not a completeness
claim.

Local validation: root Release/analyzers passed with zero warnings/errors;
Core/Application/Windows suites passed **394 / 1,145 / 697**, zero failed or
skipped, with fresh-only portable **100% line / 100% branch** coverage.
The final private-partition change additionally passed all 12 real-store
query tests. Licence notices, version policy, 381 publication assertions plus
56 process/15 interrupted retry cases, payload contracts, 123 immutable
source-tool contracts and 82 source-bootstrap checks passed. These are owned
scratch/app-free tests, not a live application trial. Actual required remote
CI and merge/ancestry remain the completion gate. Native visual/screen-reader,
installed, hardware/provider and power-loss acceptance remains separate.

After discovery #56 merged, this slice rebased cleanly onto main `614e891`.
Combined root Release and full Core/Application/Windows suites passed
**398 / 1,206 / 704**, zero warnings/errors/failures/skips, with fresh-only
portable **100% line/branch** coverage. The native structured result now uses
readable source/retention/value-kind labels; actual serialized-byte limits were
retested with the full Application suite and all **65** evidence-focused
Windows tests. No sibling worktree or unpublished source was consumed.

The subsequent rebase onto merged calls #59 (`1a117ae`) preserves that owner's
I14/R15 inventory and native privacy/call composition. Combined root Release,
full suites **409 / 1,257 / 709** and fresh portable **100% line/branch**
coverage passed. Cancellation now clears/closes immediately but retires its
token source only after the in-flight bounded read ends; the maintained
view-model test verifies both wait-handle lifetime and rejected late content.

The final clean rebase onto settings #57 (`cc9d79c`) retains all three merged
batch deliveries. Root Release/analyzers and full suites
**411 / 1,304 / 710** passed with no warnings/errors/failures/skips and fresh
portable **100% line/branch** coverage. Evidence remains a native read-only
host service, not a registry/model tool or an appearance/call mutation.

### R14 Opt-in Combined Ordinary Diagnostic Inspection - 2026-10-08

The bounded **CombinedLog** source adds native selection and the existing
host-owned list/search/source-qualified cited read service over SQLite ordinary
logs and independent DailyLog ordinary records. **All** remains SQLite-only;
individual sources, audit queries/citations and old cursor layouts remain
unchanged. A selector is not a record source: results keep their original Log
or DailyLog citations, IDs, exact provenance and retention semantics.

Ordering is explicitly source-major: SQLite commit time/evidence ID, followed
by exact daily name/byte offset. The existing SQLite ceiling and independently
captured daily immutable prefix form a truthful snapshot pair, not an atomic
cross-sink snapshot or causal/chronological ranking. Observation time does not
become commit time. Overlapping text/timestamps/IDs/trace IDs do not deduplicate
or equate records. Both readers re-admit and verify their source on every page,
including after the source boundary. Failure of either source cannot return
the other as success or an authoritative empty result; source failures/gaps are
explicit and fresh search is an explicit recovery, never an automatic restart.

The implementation reuses source parsing/validation/retention/access policies,
source-qualified positions, signed query/viewer/expiry-bound cursors and eight
15-minute host-held daily manifests. No extra cache, renewal, file write,
permission repair, pruning or database schema is introduced. Existing 50-record/
64-KiB complete serialized output and daily 32-file/8-MiB-prefix/4,096-line/
256-KiB-line/five-second/16-MiB-verification bounds remain authoritative.
Byte-limited pages continue exactly from the last emitted source position.

Architecture, evidence contracts and embedded privacy/evidence inspection
guidance describe actual opt-in availability and independent snapshot/time/
retention semantics. This does not deliver full R14, model evidence tools/
Ask Evidence/provider submission/export, generic session/history sources,
authoritative file audit, invented file spans/links, or native/installed
acceptance. No live application/audio/OS/provider/elevation/account trial occurs.

Experiment assessment: no maintained executable equivalence supersedes a unique
experiment contract or consumer. Storage/engine/encryption/DPAPI/rekey/backup/
interruption, artifact, speech and runtime/worker receipts remain distinct;
all experiment executables and historical receipts are retained. The production
reader is not qualification evidence for those contracts. Older dated delivery
receipts below describe their own snapshots and are intentionally not rewritten.

Local validation on merged base `c6d4449`: root no-restore Release/analyzers
passed with zero warnings/errors; full Core/Application/Tools/Definitions/
Windows suites passed **648 / 1,700 / 38 / 6 / 886**, zero failures/skips.
Fresh-only combined portable coverage enforced the unchanged exact
**100% line / 100% branch** thresholds and exclusions. Nineteen new real-store/
native composition cases plus reused scanner/call-time-capture cases cover
source boundaries, same-time/ID/text provenance, exact citations, full pages,
serialized-byte trimming, source failure/change/expiry/access, retention and
old continuations. Portable tests additionally exercise the exact 65,536-byte
complete result and one-byte-over shape and truthful failure activity status.
Required remote CI, review and actual merged ancestry remain lifecycle gates;
these app-free deterministic tests do not claim installed/native acceptance.

### R14 Bounded Independent Daily JSON Diagnostic Inspection - 2026-10-07

Implemented in `agents/bounded-daily-json-inspection-slice`, rebased onto
`c0c15ac2a74a865bbd7540a7a0cf5c00c2d3a21b`; #69 ordinary retention and #70
session metadata are verified ancestors. The native evidence source selector
now admits independently useful **DailyLog** list/search/exact cited reads and
trace filtering through the existing host-owned query service. `All` remains
SQLite-only with unchanged record counts. There is no speculative merged rank
or atomic cross-source snapshot.

The actual file writer and reader share the exact daily-name policy and
version-1 diagnostic envelope serialization/validation. Existing files only,
supplied application-data roots, current-user ownership/ACL/reparse admission
and opened-handle final-path/volume/file identity are required. No storage,
lease, pruning, schema, policy, protection, audit-chain or retention mutation
occurs. Eight 15-minute host-held prefix manifests are bound by existing signed
query/viewer cursors. Reopened identity/prefix hashes reject replacement,
mutation, rotation and pruning; append/new-day events do not extend a snapshot.

Bounds: 32 files, 8-MiB earliest complete-line prefix, 4,096 physical lines,
256-KiB lines excluding LF, five seconds per request and the existing
50-record/64-KiB serialized page. Capture and final prefix verification each
read at most 8 MiB. Scan ceilings, corruption, truncation, changed/missing/
expired/unavailable/timed-out sources are explicit. Unsupported legacy/activity
copies, ingestion gaps and audit mirrors have separate reported counts and
partial status. No audit mirror/lookalike acquires audit authority. Typed
observation/session/W3C fields are preserved; file commit/due times and span
graph are unavailable. Stable source-specific citations retain exact
file/offset/digest provenance and original envelope evidence IDs.

Experiment disposition: no executable removal is justified. The maintained
production source supersedes no exact experimental daily-envelope consumer:
storage proof FTS/HMAC/encryption/DPAPI/rekey/backup/child-kill paths and runtime
RT1/RT2/management diagnostic capture remain different contracts with unique
and historical receipts. Reference consumers and those receipts remain intact.
Only this slice's design and user evidence/privacy guides are updated.

Validation uses synthetic actual-writer files, private SQLite and native
view-model fixtures, not the app, audio, accounts, install, elevation, network
policy or live trials. Complete R14, combined search, session/conversation
contents, file audit/graph, Ask Evidence, export and native/installed acceptance
remain outstanding. Exact final test/coverage and CI outcomes are recorded in
the pull request, not inferred from implementation.

### R04 Bounded Ordinary Diagnostic Retention - 2026-10-07

Dispatch baseline: `d0a8e82` (#68), clean isolated
`agents/bounded-sqlite-retention-slice`. The no-restore baseline first reported
missing assets; locked restore used the machine-required Azure Artifacts source
without repository configuration/credential or dependency changes. The restored
Release baseline passed with zero warnings/errors.

[Production retention](../src/Kora.Windows/Storage/WindowsSqliteDiagnosticRetention.cs)
runs one existing-partition batch after owner-only startup storage admission and
durable recovery, before the UI lifetime. No genuine new prerequisite is unmet:
the existing policy-assigned `due_utc`, due indexes, exact bounded envelopes and
shared actual writer/reader lease support safe pruning. The batch holds that
lease and atomically removes at most 128 due ordinary logs and 32 due spans
with their at-most-1,024 owned links. Inclusive due cutoff, five-second admission/
SQLite progress deadline, cancellation before COMMIT and exact-schema/ACL/
reparse/integrity checks remain enforced. Full-store validation is not bypassed
and may fail the bounded deadline on a large store.

The committed counts and due-backlog flag are structured generated diagnostics
after lease release, under typed host/Windows `retention.run` context. There is
no timer, unbounded drain, passive retention refresh, new setting, schema rewrite,
storage replacement or permission repair. Next startups can continue backlog.
Audit rows/due dates/sequences, interaction audit/hash heads and every task,
interaction, session and grant/Perpetual record remain outside this operation.
Retained citations truthfully distinguish due-but-present from missing targets.
Signed snapshot ceilings now bind original evidence identities; pruning/reuse
invalidates affected continuations explicitly instead of admitting replacement
rows. A fresh query is required.

[Deterministic real private fixtures](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteDiagnosticRetentionTests.cs)
cover exact boundaries, all batch limits, audit/authority/Perpetual preservation,
restart/backlog/idempotence, rollback/cancellation, concurrent actual writers/
readers, malformed/missing/ACL failures, passive read bytes and retained citations.
[Owned-process interruption](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteEvidenceInterruptionTests.cs)
covers pre/post retention COMMIT and actual hot-journal reopen. These are not
live-app, installed, account, device, OS-effect or hardware power-loss trials.

The [specific experiment disposition](../experiments/r02-storage-proof/README.md#bounded-ordinary-retention-equivalence-assessment---2026-10-07)
retires no executable: encrypted sample/FTS/rekey/native/artifact/backup/deletion
paths still have consumers and unique behaviors absent from ordinary pruning.
Original receipts remain unchanged. This is logical row pruning, not forensic
erasure, full R04/D-009 acceptance, audit pruning/anchors, session retention/
deletion, configured preview/apply or artifact/backup disposal.

#### Bounded Retention Local Validation Receipt

Validated the working slice on dispatch/main `d0a8e82`, Windows/x64 and SDK
10.0.401, without a dependency, lockfile, native-provider or CI-policy change.
Release solution build: zero warnings/errors. Storage selection: 246 passed.
Full maintained suites: Core 520, Application 1,389, Tools 38, Definitions 6,
Windows 791; **2,744 passed, zero failed/skipped**. Fresh reports for only the
four portable assemblies cover **8,635/8,635 lines, 4,340/4,340 branches and
1,076/1,076 methods, all 100%**. Dependency-license policy, Git whitespace and
all 13 immutable embedded definition-resource byte checks passed. Owned private
fixtures were disposed. Early fixture/selector errors were corrected; a
zero-test selector run is not counted as validation.

Commands used the repository's direct Release `--no-restore` build and
`dotnet test --project` / `--no-build` paths; portable tests added `--coverlet
--coverlet-output-format cobertura`, and the existing report generator and
`eng/Assert-CodeCoverage.ps1` enforced unchanged 100% line/branch thresholds.
No app/device/OS-effect, install/elevation, account, network/security-policy,
hardware power-loss or forensic-erasure trial was performed. This local
receipt is distinct from subsequent PR CI/merge evidence.

### R04 Foundation Delivery

The first bounded **durable** milestone now composes one actual exact local
version-query request/task with correlated diagnostic/audit evidence,
committed terminal state and interrupted-run recovery on private SQLite.
The owner-approved baseline removes encrypted-native/key/rekey gates.
The [composed continuation](#r04-composed-durable-version-query---2026-10-06)
records its precise source, automated interruption evidence and validation;
it is not completion of R04 or an OS-effect receipt.
The existing `kora.db` setup probe remains the
bootstrap-only `setup_tasks` schema; it is neither the new host store
nor a legacy-content migration. No content, grants, receipts or evidence
are added to that database by R04.

| Boundary | State and source/test evidence | Remaining gate |
|---|---|---|
| Provider/native baseline | Existing pinned Microsoft.Data.Sqlite / SQLitePCLRaw e_sqlite3 is the owner-approved standard route. Historical exact 2.4.0 encrypted-candidate evaluation passed basic tests but was rejected; [D-009](Decision_Register.md#approved-profile-secured-sqlite-baseline---2026-10-06) supersedes codec/key admission. No manifests changed. | Normal licence/servicing/native packaging/loading evidence remains; no encrypted-native replacement or owned source-build is required. |
| Host identity and truthful state | Implemented portable [typed identities](../src/Kora.Core/Hosting/HostId.cs), [host request](../src/Kora.Core/Hosting/HostRequest.cs), [task transition/recovery rules](../src/Kora.Core/Hosting/HostTaskRecord.cs), unknown/exclusive resource descriptors; [contract tests](../tests/Kora.Core.UnitTests/Hosting/HostContractTests.cs). The subsequent [durable authority slice](#r04r05-durable-interaction-and-minimal-session-authority---2026-10-06) adds minimal session generations and persisted questions/proposals/grants. | Full session registry/routing/history UX and concurrent resource leases remain open. Bootstrap IDs and minimal authority are not full Sessions UX. |
| Causal and business correlation | Implemented versioned four-source [host activities](../src/Kora.Core/Diagnostics/HostActivity.cs); request routing and typed audit boundaries; ordinary async child context and explicit deferred links; [trace/isolation/spoof tests](../tests/Kora.Application.UnitTests/Diagnostics/EvidenceLoggerProviderTests.cs) | Runtime/tool/queue/presentation/evidence/retention implementations must add their actual boundaries as delivered; operation completion is not proof of OS effect. |
| Independent diagnostic/audit contracts | Implemented [formatter-independent capture](../src/Kora.Application/Diagnostics/EvidenceLoggerProvider.cs), bounded typed properties/scopes, call-time context, spans/links and gaps; production composes [private SQLite evidence](../src/Kora.Windows/Storage/WindowsSqliteEvidenceSink.cs) and the [bounded read-only query/native inspector](#r04r14-bounded-durable-evidence-inspection---2026-10-07). [Daily JSON](../src/Kora/FileEvidenceSink.cs) preserves trusted categories/IDs independently. Only internal typed audit state routes audits; file copies/lookalikes confer no authority. Required delivery/capture failures report and propagate after independent sink attempts. | Audit tamper checkpoints/pruning, daily-file cross-source queries and full history/evidence UI remain. No authorization is inferred from SQLite, file copies or correlation metadata; interaction-audit transactions are not this projection. |
| Intent, dispatch marker, terminal receipt and recovery | Implemented [coordinator](../src/Kora.Application/Hosting/HostTaskCoordinator.cs), actual [standard SQLite store](../src/Kora.Windows/Storage/WindowsSqliteHostTaskStore.cs), [bounded query runner](../src/Kora.Application/Hosting/DurableVersionQuery.cs) and [startup recovery](../src/Kora.Application/Hosting/DurableHostRecovery.cs). Exact typed/activated-voice version input uses required correlated evidence and revision-checked state/event commits. Intent-only becomes Interrupted; dispatched/no receipt becomes Unknown; no executor/replay callback. [Production interruption/reopening](#r04-production-store-interruption-and-reopening---2026-10-07) tests pre/postcommit task/evidence/interaction writes and interrupted audited recovery with actual hot private journals. | The receipt proves only that this local query returned. General dispatch/grants/session registry, managed copies, broader migration/retention/deletion and installed/power-loss acceptance remain. Existing model/OS routes are not promoted to durable effect executors. |
| Retention semantics | Implemented 30-day diagnostic/daily-file and 90-day audit defaults, audit 30-365 validation, UTC due-date and explicit apply-now calculation; [tests](../tests/Kora.Core.UnitTests/Diagnostics/EvidenceRetentionPolicyTests.cs). Composed SQLite evidence receives independent effective due dates transactionally. [Bounded ordinary startup pruning](#r04-bounded-ordinary-diagnostic-retention---2026-10-07) removes at most 128 logs and 32 spans with owned links, reports backlog and preserves all audits/authority. Queries distinguish expired-but-present from missing-or-removed and reject replaced snapshot ceilings. | Configured diagnostic schema, preview/apply UI, audit pruning/anchors and session/copy deletion remain R04/R12 work. Logical row pruning is not forensic erasure or proof of complete history. |
| Profile/artifact primitives | Actual supplied-root/owner/ACL/reparse checks compose the standard task/evidence partitions. Earlier uncomposed [DPAPI keys](../src/Kora.Windows/Storage/WindowsStorageKeyStore.cs) and [encrypted artifacts](../src/Kora.Windows/Storage/WindowsEncryptedArtifactStore.cs) retain historical scratch evidence. | Database key/rekey/encrypted conversion is no longer required. Standard managed backup/artifact provisioning/composition, their process-interruption and deletion boundaries remain; optional crypto primitives do not establish those workflows. |

Artifact reconciliation is observation-only: at most 256 entries/references,
64 MiB examined bytes and 4 MiB plaintext per artifact. It reports staged,
orphan, missing and corrupt items without deleting or promoting them. These
primitive bounds do not specify the future product's large-artifact policy
and do not waive lifecycle/deletion ownership.

Concrete handoffs:

- **R05:** use host-resolved identity, live matching activity and typed audit
  paths; require successful admitted intent/approval commits before new
  consequential dispatch. Legacy action-name preferences gain no authority.
- **R06:** keep provider labels/trace headers as untrusted observations;
  proposals/results use host IDs and truthful Unknown/Unavailable states.
  Do not expose nonexistent session/evidence tools.
  Runtime documentation and experiment evidence from
  [RT1 PR #33](https://github.com/roryprimrose/Kora/pull/33) are now included
  through main. Its original local receipt is against `d3d2296`; the R04
  rebase validation does not rerun that separate fixture or establish NuGet
  release-byte parity, RT2/MG1/PV1 closure or production runtime admission.
- **R07:** bind approved context/artifacts to host session/request identities;
  do not persist clipboard content until private storage composition and
  source/consent/revocation gates pass.
- **R12:** implement actual lifecycle/deletion, late-append holds, recoverable
  copy inventory and retention checkpoints; no browsing refresh or automatic
  replay. Primitive artifact reconciliation is not deletion acceptance.
- **R17:** package the pinned standard SQLite provider/native closure
  for every offered RID; independently prove installed loading, missing/
  corrupted-native fail-closed behavior, CurrentUser/profile/ACL and recovery.
  Current publishes include the bounded standard-SQLite task/evidence
  composition; the bootstrap `kora.db` remains setup-only.
  The WiX implementation is now included through main's #35, targeting
  Windows 11 `win-x64`; R04's x86 static publish does not add a supported
  installer target or establish D-007 acceptance.
  A future reviewed provider/engine handoff must specify exact managed/native
  versions, source/licences/notices, hashes, ABI/exports, transitive runtime
  closure, protected load paths and initialization requirements. Assets must
  flow through normal locked application publish and the installer's exact-file
  manifest, never machine-local DLL copying. Initialization/migration stays
  host-owned, not installer-executed. Neither current-user deployment nor
  Program Files/elevation alone establishes native/deployment admission.
  The installer pins x64 prerequisites of .NET Desktop/base 10.0.12+ and
  VC++ 14.51.36247.0+; R04 changes none of them. Report any
  additional/minimum requirement to that owner before shared-contract edits.
  Installer lifecycle/protection acceptance remains separate and open; no
  historical sibling POC result substitutes for this branch's validation.

  ### R04 Validation Receipt - 2026-10-06

  This is the original `d3d2296`-baseline receipt, not validation of the
  later rebased source. The recorded NSIS-era negative-suite command is
  historical: main retired that tooling; the maintained payload contracts
  used after the rebase are recorded separately below.

  Execution environment: Windows 10.0.26300, SDK 10.0.401/MSBuild 18.9.11,
  .NET runtime 10.0.12, x64 process. Tested this worktree's uncommitted source
  on the `d3d2296` base, not another session/branch. No application, installer,
  real audio/device, elevation, account or security-policy trial was performed.

  | Validation | Actual result |
  |---|---|
  | Initial no-restore Release build | Failed `NETSDK1004` because the fresh worktree lacked assets; this authorized the locked restore, not a dependency upgrade. |
  | `dotnet restore .\Kora.slnx --locked-mode` | Passed; all seven projects; production manifests/locks unchanged. |
  | `dotnet build .\Kora.slnx --configuration Release --no-restore` | Passed; zero warnings/errors with repository analyzers/warnings-as-errors. Intermediate new-code analyzer and assertion/owned-junction cleanup failures were corrected and full suites rerun. |
  | Core full suite | 269 succeeded, zero failed/skipped. |
  | Application full suite | 748 succeeded, zero failed/skipped. |
  | Windows full suite | 212 succeeded, zero failed/skipped; includes actual CurrentUser DPAPI/ACL owned-scratch checks, not installed-lab acceptance. |
  | CI coverage/report threshold | Passed 100% line (5,241/5,241), 100% branch (2,171/2,171), 100% method (599/599), Core/Application only, selecting only the latest final full-suite report from each project. A latest-only intermediate run caught a redundant null branch after live-context tightening; it was corrected and all three suites rerun. ReportGenerator notes absent generated source files but successfully generates the report; no thresholds/suppressions were weakened. |
  | Actual desktop-adapter synthetic check | Four standalone in-memory assertions passed against the compiled desktop adapter: category-level filtering, admitted warning, rejection of category lookalikes, and authoritative envelope identity/scalar-kind projection. Session-artifact fixture, not an added repository test suite or real file-I/O acceptance. Initial PowerShell compilation attempts failed on forwarded runtime types; using the existing .NET 10 reference pack resolved them without package/tool installation. |
  | `.\eng\Test-DependencyLicenses.ps1` | Passed pinned licence policy and notice parity; publish directories receive `licenses` and `package-notices` as in CI. Existing version-specific OpenTK overrides remain unchanged. |
  | Locked framework-dependent publishes | `win-x64` and `win-x86` passed. Neither was launched/installed. |
  | Existing x64 publish inspector and negative-contract suite | Inspected 200 published files; 9 static checks passed. Raw OpenTK nuspec metadata warnings remain (reviewed version-specific solution-gate overrides exist); static inspection still reports release/installed gates blocked. |
  | Both-RID static PE/native closure | Final-source refresh: x64 has 7 native PE files and 10 declared native-asset entries; x86 has 5 files and 6 entries. Matching native machines and declared assets present; framework declarations inspected. `e_sqlite3.dll` remains bootstrap-only; no encrypted engine is shipped. Hash/import receipt is generated locally. |
  | Whitespace and fixture cleanup | `git diff --check` passed; owned test fixtures removed. Uncommitted work is preserved; no commit/push/PR or worktree removal. |

  Exact full-suite commands (Microsoft.Testing.Platform syntax; no zero-test
  result was accepted):

  ```powershell
  dotnet test --project .\tests\Kora.Core.UnitTests\Kora.Core.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\verified-core --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix core
  dotnet test --project .\tests\Kora.Application.UnitTests\Kora.Application.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\verified-application --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix application
  dotnet test --project .\tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\verified-windows --report-trx
  $core = Get-ChildItem -LiteralPath .net-test-artifacts\verified-core -Filter '*.coverage.cobertura.*.xml' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
  $application = Get-ChildItem -LiteralPath .net-test-artifacts\verified-application -Filter '*.coverage.cobertura.*.xml' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
  dotnet reportgenerator "-reports:$($core.FullName);$($application.FullName)" "-targetdir:.net-test-artifacts\coverage-verified" "-reporttypes:Cobertura;TextSummary" "-assemblyfilters:+Kora.Core;+Kora.Application"
  .\eng\Assert-CodeCoverage.ps1 -ReportPath .net-test-artifacts\coverage-verified\Cobertura.xml -MinimumLine 100 -MinimumBranch 100
  dotnet publish .\src\Kora\Kora.csproj --configuration Release --runtime win-x64 --self-contained false --property:RestoreLockedMode=true --output artifacts\Kora-win-x64
  dotnet publish .\src\Kora\Kora.csproj --configuration Release --runtime win-x86 --self-contained false --property:RestoreLockedMode=true --output artifacts\Kora-win-x86
  ```

  The two original static inspection/contract invocations used the
  distribution experiment's now-retired x64 inspector and then-existing
  `MakeNsis` inert-path parameter. This is archived historical procedure,
  not an active command or a new maintained-tool receipt. The
  [immutable pre-migration procedure](https://github.com/roryprimrose/Kora/tree/c5dffabf8f4fa767147be06dd8b296238ea97da0/experiments/r02-distribution-proof)
  and [current maintained commands](Distribution_And_Updates.md#maintained-proof-migration-verification-2026-10-07)
  are distinct; no old hashes/source/profile have been rewritten.

  The static negative suite resolves its supplied tool path before rejecting
  tampered bytes; an initial nonexistent placeholder failed path resolution.
  The verified run supplies the existing dotnet executable as an **inert path
  only**: tampered-payload rejection occurs before any tool invocation. No NSIS
  installation, compiler execution or setup construction is claimed. The
  historical x64-only inspector was not changed to invent x86 acceptance;
  the additional read-only PE/declared-asset check generated
  `artifacts\r04-native-closure.json` for both RIDs using its existing helpers;
  final managed-assembly hashes were captured after the last publish refresh.
  Disposable negative-suite payload copies were removed after verification;
  inspection/test receipts and the two published payloads were retained.

### R04 Composed Durable Version Query - 2026-10-06

This continuation implements the **first bounded composed milestone**, not
complete R04/D-009 acceptance. It runs solely in the collision-checked isolated
`agents/kora-r04-durable-composition-20261006` worktree, created clean from
`origin/main` at `d1fc77f8083985c5d86ed0ef3496ac68c4a150ed` after #39/#40.
The source checkout remained on `feature/personalized-startup-greeting`
`a7bbc04`; no edits/builds/restores ran there. All deliverables remain
uncommitted; publish manifests identify the base HEAD, not a committed release.

**Admitted scope:** standalone exact typed or activated-voice `ShowVersion`
requests with no pending question/grant/action-approval interaction. The
application pins the matched command before asynchronous persistence, rechecks
current host/privacy/interaction eligibility before dispatch, and never
redirects a stale version request into model interpretation. The callback
preserves presentation synchronization context. Subsequent optional speech is
outside the query receipt. Model suggestions and other bootstrap/OS routes
retain their existing behavior and are not promoted to durable effect executors.

[Program](../src/Kora/Program.cs) composes the actual
[task store](../src/Kora.Windows/Storage/WindowsSqliteHostTaskStore.cs) and
[evidence sink](../src/Kora.Windows/Storage/WindowsSqliteEvidenceSink.cs).
`HostStorageV1/host.db` and `EvidenceStorageV1/evidence.db` are distinct private
standard-SQLite partitions; `kora.db/setup_tasks` is unchanged and never
promoted or migrated to content storage. New files have protected current-user
ownership/DACLs before contents are written. Existing missing/corrupt data,
partial initialization, unsupported/altered schemas, reparse paths, permissive
ACLs and missing journals fail without repair, replacement or shared fallback.
Connections select exclusive locking before native hot-journal reads and
`PERSIST`, retaining the privately preowned journal through rollback/reopen;
transactions use FULL synchronization and memory-only temporary storage.
Storage admission/execution is bounded, not an unbounded asynchronous queue.

[DurableVersionQuery](../src/Kora.Application/Hosting/DurableVersionQuery.cs)
orders intent, typed requested audit/diagnostic evidence, dispatch, local query
return, required terminal evidence and terminal receipt. Its Succeeded receipt
establishes only that this bounded local query returned. It is not an OS effect,
API acceptance, abort acknowledgment or process/socket-close receipt.
Required capture/file/database failures propagate after independent delivery
attempts; the daily-file error latch prevents Serilog self-reporting from
silently authorizing admission. Error presentation survives error-logger failure
without swallowing that failure.

Independent log/audit/span/link tables retain formatter-independent typed
envelopes and promoted host/W3C/business fields. Audit request and terminal
events share the host request's business correlation. Ordinary hostless logs
retain explicit capture-owned MissingHostContext/`bootstrap=false` gaps and
null trusted identity columns, not manufactured bootstrap/session authority.
Invalid host-bearing records are never downgraded, and only the trusted typed
audit route writes audit rows.

[DurableHostRecovery](../src/Kora.Application/Hosting/DurableHostRecovery.cs)
admits correlated recovery evidence before committing intent-only Interrupted
or dispatched/unverified Unknown. Fresh recovery traces retain durable host
IDs; there is no executor callback or automatic replay. Startup handles at most
100 incomplete records and fails explicitly if further recovery remains.
Cancellation and disposed-context late callbacks cannot publish success.

First-use greeting, persistent Settings note and version response disclose
readable copies outside the profile boundary, same-user/admin access,
independent diagnostic/audit due dates and **unimplemented database pruning/
deletion**. No transcript, response body, audio, credential or secret is newly
stored. Credentials remain Windows-protected; no database key/rekey is required.

#### Stable R05 Handoff

- Existing `HostId`, `HostRequest`, `HostTaskRecord`, `IHostTaskStore` and
  `HostTaskCoordinator` signatures/transition rules remain unchanged.
  Host/state/correlation metadata still grants no authority.
- New concrete Application services are `DurableVersionQuery.RunAsync(
  RequestOrigin, Func<Task>, CancellationToken)` returning
  `Task<HostTaskRecord>`, and `DurableHostRecovery.RecoverAsync(
  CancellationToken)` returning `Task<IReadOnlyList<HostTaskRecord>>`.
  The query callback is admitted application-owned local-query code, not a
  general tool/effect executor or an R05 authorization gateway.
- Windows adds `WindowsSqliteEvidenceSink(IApplicationDataPaths,
  EvidenceRetentionPolicy? = null, TimeProvider? = null)`, `Initialize()` and
  existing `IEvidenceSink` writes. The task implementation adds concrete
  `InitializeAsync(CancellationToken)` and bounded `ReadTaskAsync(
  HostId<TaskIdentity>, CancellationToken)`; Core store interfaces are unchanged.
- Four host sources remain `Kora.Core`, `Kora.Application`, `Kora.Windows`,
  `Kora.Desktop`, versioned from the Core assembly; this feature build captures
  **0.1.0.0**, not a hard-coded 1.0.0. Reserved context-gap markers cannot be
  selected by caller properties. W3C/correlation IDs never select a grant.
- Attempts to contact orchestration, R05 and reconciliation via `send_message`
  were rejected by the session tool's process-wide message limit. No sibling
  branch or unpublished changes were imported. This durable handoff records
  the integration boundary; it is not a claim that peer agreement was delivered.

#### Composed Milestone Validation Receipt

Final validation is against this **uncommitted working copy on `d1fc77f`**,
Windows 10.0.26300.0/x64, SDK 10.0.401/runtime 10.0.12, feature version 0.1.0.
It is not a union of earlier/sibling receipts. Initial missing restore assets,
an incorrectly scoped test filter, an absolute-path payload check and related
analyzer/test compilation failures were corrected before these final results.

| Check | Actual final result |
|---|---|
| Root eight-project Release build | **0 warnings, 0 errors**, analyzers enabled |
| Core suite | **307 passed**, 0 failed/skipped |
| Application suite | **964 passed**, 0 failed/skipped |
| Windows suite | **494 passed**, 0 failed/skipped; **116 Storage cases** |
| Total | **1,765 passed**, 0 failed/skipped |
| Latest-only portable coverage | **5,742/5,742 lines; 2,311/2,311 branches; 674/674 methods**, all 100%. Exactly the latest terminal Core/Application reports were merged; no old/sibling coverage was used. |
| Real application/storage composition | Both UI/activated-voice cases pass with the actual runner, audit bridge, provider and both private SQLite stores: intent/dispatch/success, matching host/W3C/business fields and nine completed spans without sink recursion. Actual journal failures before requested/terminal audit admission preserve incomplete state, attempt the independent sink and never return a success receipt. |
| Interruption/no replay | Exact fixture-owned test executables are killed after intent, dispatch and an uncommitted native transaction; Interrupted/Unknown and hot-journal rollback pass. Composed runner-child intent/dispatch kills recover with typed evidence and no execution callback/replay. Only fixture-created child PIDs are terminated. |
| Failure/concurrency bounds | Atomic state/event and span/link rollback, concurrent revisions/sink instances, altered schema/data, corrupt/missing database/journal, partial initialization, ACL/reparse, bounded contention/progress, typed spoofing, context gaps/property/scope/byte limits, cancellation, late-context rejection and logging-error visibility pass. |
| Licences/locked closure | Approved/current. No production dependency/notice changes; Windows test reference/lock adds Application project metadata only, no new package. |
| Version/fake release/payload policy | All existing contracts pass; fake GitHub/owned version fixtures only, no real publication or repository commit. |
| Final locked x64/x86 publishes | Both pass. Exact manifests verify **201/197 files**, including licence texts, for base HEAD `d1fc77f`; payloads contain uncommitted compiled changes. |
| Actual native inspection | x64 **7 PE files/10 declarations**, x86 **5/6**; correct machine, normal imports and exact SHA-256 equality to declared pinned package assets, including `e_sqlite3.dll`. No experimental runtime or fixture payload leaked. Static inspection only, no app/installer launch. |
| Bounded effects / still unsupported | Owned scratch file/database/ACL fixtures, fixture child processes and non-disruptive existing integration tests only. No real user storage migration, live app/audio/model, installer, elevation, lock/power or machine-policy trial; no installed or power-loss certification. |

Final structured test/coverage artifacts are under
`.net-test-artifacts\r04-terminal-core`,
`.net-test-artifacts\r04-terminal-application`,
`.net-test-artifacts\r04-terminal-windows` and
`.net-test-artifacts\r04-terminal-coverage`. Retained publish payloads are
`artifacts\r04-composed-win-x64` and `artifacts\r04-composed-win-x86`.
Native inspection JSON and the read-only two-RID inspector are retained in
the session's `files` artifacts; they do not enter production or the repository.
Owned storage fixtures clean up their exact scratch directories; the deliverable
worktree is deliberately retained for uncommitted handoff.

Reproduction commands (run **only in the verified isolated worktree**):

```powershell
dotnet restore .\Kora.slnx --locked-mode
dotnet build .\Kora.slnx --configuration Release --no-restore
dotnet test --project .\tests\Kora.Core.UnitTests\Kora.Core.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\r04-terminal-core --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix core
dotnet test --project .\tests\Kora.Application.UnitTests\Kora.Application.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\r04-terminal-application --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix application
dotnet test --project .\tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\r04-terminal-windows --report-trx
# Select exactly the fresh Core/Application coverage reports; do not merge historical directories.
dotnet reportgenerator "-reports:<fresh-core-report>;<fresh-application-report>" "-targetdir:.net-test-artifacts\r04-terminal-coverage" "-reporttypes:Cobertura;TextSummary" "-assemblyfilters:+Kora.Core;+Kora.Application"
.\eng\Assert-CodeCoverage.ps1 -ReportPath .net-test-artifacts\r04-terminal-coverage\Cobertura.xml -MinimumLine 100 -MinimumBranch 100
.\eng\Test-DependencyLicenses.ps1
.\eng\Test-BuildVersion.ps1
.\eng\Test-GitHubRelease.ps1
.\eng\Test-InstallerPayloadContracts.ps1
dotnet publish .\src\Kora\Kora.csproj --configuration Release --runtime win-x64 --self-contained false --no-restore --property:RestoreLockedMode=true --output artifacts\r04-composed-win-x64
dotnet publish .\src\Kora\Kora.csproj --configuration Release --runtime win-x86 --self-contained false --no-restore --property:RestoreLockedMode=true --output artifacts\r04-composed-win-x86
```

For each payload, copy the approved `licenses`/`package-notices` directories
from `artifacts\license-compliance`, then run `eng\Test-InstallerPayload.ps1`
with an **absolute** payload path, `-Version 0.1.0`,
`-SourceRevision d1fc77f8083985c5d86ed0ef3496ac68c4a150ed`, first
`-WriteManifest` and then verification. The base revision plus dirty-worktree
qualification is intentional; no commit/push/PR/merge is authorized or performed.

#### Composed Milestone PR Rebase Receipt - 2026-10-06

The user subsequently authorized commit, push, PR creation, squash auto-merge,
and synchronization with main. [PR #45](https://github.com/roryprimrose/Kora/pull/45)
publishes this bounded milestone; it does not close the broader R04 gates.
Initial commit `abe6197d1f0cca1744836f3dde012506cc0a6fa2` matched all 33
hashed delivery files. It was cleanly rebased onto main
`d6545a483a8e9612e0685250dc2ebe4cadd3d92d` (#43, #44 and #42), producing
validated code checkpoint `57c136bb748622059f7c238481d37a3709ae9d5d`.
`git range-diff` reports the milestone commit unchanged by the rebase.
This receipt is a subsequent documentation-only update, not a claim that the
earlier uncommitted payloads were built from the new checkpoint.

| Check | Actual rebased result |
|---|---|
| Root eight-project Release build | Zero warnings/errors; existing locked assets remained sufficient, no dependency-manifest changes or new restore required. |
| Core / Application suites | **312 / 989 passed**, zero failed/skipped. |
| Windows suite | First attempt **493 passed, 1 failed**, zero skipped: unrelated native-window hit-testing assertion. After explicit approval, one full rerun **494 passed**, zero failed/skipped; no test/product change or disabled test. Both TRX files retained. |
| Latest completed full-suite total | **1,795 passed**, zero failed/skipped on the completed set; the first-attempt Windows failure is not erased or counted as first-attempt success. |
| Fresh portable coverage | **5,893/5,893 lines; 2,393/2,393 branches; 686/686 methods**, all 100%. Only fresh PR Core/Application reports were merged; the initial timestamped-filename selector was corrected before merging. |
| Licence/version/fake-release/payload policy | All passed; release tests use fake GitHub and owned fixtures, not publication. |
| Locked framework-dependent publishes | x64/x86 passed; exact manifests verify **201/197 files** at validated code checkpoint `57c136b`. |
| Actual native inspection | x64 **7 PE files/10 declarations**, x86 **5/6**; expected machines/imports and pinned package SHA-256 equality passed. Static only, no installed/dynamic loading claim. |
| Effects/ownership | Original orchestration checkout untouched; no sibling branch merged. Only approved Git/PR mutations and existing automated fixtures; no installer execution, elevation, live app/audio/model, real-user migration or machine-policy effects. |

Fresh evidence is retained under `.net-test-artifacts\r04-pr-core`,
`r04-pr-application`, `r04-pr-windows`, `r04-pr-windows-retry` and
`r04-pr-coverage`. Published payloads are retained under
`artifacts\r04-pr-win-x64` and `artifacts\r04-pr-win-x86`; their manifests
identify the validated code checkpoint. The earlier receipt and artifacts
remain historical evidence. Auto-merge remains subject to normal up-to-date
branch and required CI gates, without administrative bypass.

Final main integration includes #41 and #46 on base
`6897d77cfc73a331f3ccf09646fbbcb009fbdcf2`. Validated checkpoint
`bc6014a385ab7f62100d5358e4e5b20a78af332b` retains the reconciled R03/R05
rows and the delivered-but-partial R04 row. The earlier apparent native
hit-test failures occurred at **desktop cleanup**, not a hit-test assertion:
switching back can fail while the worker retains implicit IME windows/hooks.
The user-authorized worker-exit cleanup fix passed twenty separate native
runs and the 494-case Windows suite, but #46 concurrently merged a canonical
thread-local IME/desktop regression fix. With explicit user approval, the
redundant test commit was dropped; #46's test source is retained unchanged.
Historical failed and successful TRX files remain available.

| Final combined check | Actual result |
|---|---|
| Root Release build | Zero warnings/errors. |
| Core / Application / Windows | **338 / 1,049 / 495 passed**, **1,882 total**, zero failed/skipped on the first combined run. Includes main's sixteen-iteration private-desktop restoration/release regression. |
| Fresh portable coverage | **6,246/6,246 lines; 2,685/2,685 branches; 742/742 methods**, all 100%, using only the fresh combined portable reports. |
| Licence/release/payload policy | Passed; #41's ten runner-process exit-code cases and fake-release regression passed. Version policy was also revalidated after #41, before #46; #46 does not alter that policy. |
| Locked publishes / exact native payloads | x64/x86 passed at checkpoint `bc6014a`; **201/197 files** verified. Native machines/imports/package SHA-256 passed, **7/10** and **5/6** PE/declaration counts. Static inspection only. |

Final outputs use `.net-test-artifacts\r04-combined-core`,
`r04-combined-application`, `r04-combined-windows`, `r04-combined-coverage`
and `artifacts\r04-combined-win-x64`/`r04-combined-win-x86`. This final
receipt update is documentation-only. R05 remains a bounded service/contract
foundation; this R04 PR does not activate its durable grant/question adapter
or shared production dispatch, and neither package is falsely closed.

#### Remaining R04 Gates

| Boundary | Explicitly still open |
|---|---|
| Migrations | Supported existing host v1 is validated without conversion. Broader version migrations, legacy content import and migration recovery/backups are not implemented. |
| Managed copies/artifacts | No content-bearing artifact or managed backup is needed/created by this metadata-only query milestone. Standard managed artifact staging/publication/reference and backup-generation interruption/deletion acceptance remains. Historical optional encrypted primitives do not satisfy it. |
| Retention/lifecycle | Independent 30-day diagnostic/span/link and 90-day audit due dates are assigned transactionally. Automatic database pruning, audit continuation anchors, query-visible expired segments, configurable preview/apply, session lifecycle/deletion and late-append/copy revocation remain R04/R12. |
| Audit authority | Typed routing and ordered persistence are implemented; full D-008 tamper checkpoints/rollback guarantees and durable authorization/grant consumption are not. SQLite, ACLs, file copies and query receipts do not provide new authorization. |
| Deployment/recovery | Owned fixture process kills and native rollback are automated evidence. Installed loading/effective ACLs, actual power-loss, all-users/cross-account/shared-storage claims and hardware/OS timing remain separately gated/approved. |
| Other runtime surfaces | General session registry, history/queue/workspace UI, model/tool/worker adapters and consequential receipts are not added; no automatic replay, grant migration, installer execution or optional encryption overhaul. |

### R04 Rebase Validation Receipt - 2026-10-06

Validated code checkpoint `74739023458bae7022cabc72ce1c4b34d8e602e1`
on main `3e8558fbff07943d39721b230599b02062d90d57`, not a union of sibling
test counts. Environment remains Windows build 26300, SDK 10.0.401,
runtime 10.0.12 and x64 test process. Subsequent roadmap changes are
documentation-only. The request wrapper remains after host-input eligibility;
main's readiness/cancellation, presentation-only Dismiss, speech containment,
startup/versioning and installer source were retained.

| Validation | Actual rebased result |
|---|---|
| Locked restore / complete Release solution build | Passed all eight projects, including the setup application; zero warnings/errors. Main changed manifests, justifying this locked restore. R04 adds no dependency/lock changes relative to main. |
| Core / Application / Windows full suites | 269 / 780 / 391 passed: **1,440 total**, zero failed or skipped. Separate TRX/coverage outputs under `.net-test-artifacts\rebased-*`. |
| Core/Application coverage | Passed 100% line (5,404/5,404), branch (2,209/2,209) and method (617/617), using only the latest rebased reports. Missing generated logging-source notices do not invalidate the generated report. |
| Actual desktop adapter | The four session-artifact in-memory assertions were rerun against rebased binaries and passed: category filtering, warning admission, category lookalike rejection and envelope identity/scalar projection. This is not actual file-I/O failure acceptance or an added repository test suite. |
| Licence/notice gate | `eng\Test-DependencyLicenses.ps1` passed against the new main closure. |
| Maintained CI policy contracts | `eng\Test-BuildVersion.ps1`, `eng\Test-GitHubRelease.ps1` and `eng\Test-InstallerPayloadContracts.ps1` passed. Publication tests use fake GitHub calls; their printed publication messages are not actual tags/releases. Payload rejection tests invoke neither compiler nor installer. |
| Locked framework-dependent publishes / exact-file manifests | x64 and x86 passed at feature version 0.1.0. `eng\Test-InstallerPayload.ps1` verified 201 x64 / 197 x86 files, excluding the receipt itself, against the checkpoint revision. Outputs are `artifacts\R04-rebased-win-x64` and `artifacts\R04-rebased-win-x86`. |
| Static native closure | x64: 7 native PE files / 10 native declarations; x86: 5 / 6. Expected machines and declared assets present; managed/native hashes and imports recorded in `artifacts\r04-rebased-native-closure.json`. Bootstrap `e_sqlite3.dll` still ships; no encrypted engine is admitted. |
| Cleanup / scope | Owned storage fixtures removed; whitespace passed. No app launch, real audio/device trial, registry mutation, elevation/install, live account, real GitHub publication or sibling-worktree mutation was performed. MSI/Burn assembly, ICE and installed acceptance were not rerun by this rebase. |

Full-suite commands:

```powershell
dotnet restore .\Kora.slnx --locked-mode
dotnet build .\Kora.slnx --configuration Release --no-restore
dotnet test --project .\tests\Kora.Core.UnitTests\Kora.Core.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\rebased-core --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix core
dotnet test --project .\tests\Kora.Application.UnitTests\Kora.Application.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\rebased-application --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix application
dotnet test --project .\tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\rebased-windows --report-trx
```

**Remaining R04 work, in delivery order:**

1. **Encrypted-provider/source-build prerequisite superseded:** use the existing
   pinned standard SQLite closure under the owner-approved profile baseline.
   Maintain ordinary licence/publish/native-loading checks; no codec admission
   or database key/rekey pipeline is required.
2. Compose the actual version-1 transactional task store into request routing
   with truthful outcomes, then implement separate diagnostic/audit/span/link
   persistence. The evidence sink and current routing store remain unavailable.
3. Complete supplied-profile provisioning, managed artifact/backup publication,
   interrupted recovery and explicitly supported schema migrations. Verify
   private effective permissions and readable-copy disclosure, without replacing
   corrupt/missing existing data or repairing permissions silently.
4. Complete audit ordering/checkpoints, retention assignment/configuration/
   pruning/gap semantics and bounded artifact lifecycle; validate actual sink
   failure/backpressure and interrupted-process behavior.
5. Prove the first on-disk identified request/task/terminal/recovery milestone,
   without replay, then qualify the applicable installed/protection gates.
   Real runtime/tool callback attribution and late-result reconciliation follow
   the R05/R06 handoffs, not the synthetic SDK event schema.

The WiX merge removes source-integration work, not these storage gates.
This remains a reviewable **safe gated foundation**, not complete R04 or
permission to enable clipboard/tool/content persistence.

### R04 Native Evaluation Continuation - 2026-10-06

Production source/dependencies are unchanged from the rebased checkpoint.
Flat-container acquisition still failed with validated OpenSSL transport;
the official v2 endpoint succeeded without disabling certificate validation.
All four packages and Windows release archives are retained in session
artifacts, not production publish output. Normal NuGet repository-signature
verification passed; unsigned native DLLs were not relabelled publisher-signed.

The native fixture's 44 assertions are real x64/.NET 10 owned-scratch
observations, not additions to the 1,440 repository-test receipt or installed
acceptance. Initial fixture assumptions about the codec version's display
string and live-WAL file sharing were corrected before the successful run;
its scratch files were removed. The separate ADO probe did not complete:
temporary `Add-Type` compilation reported CS1701 for .NET 8/10 framework
references. No blanket .NET 10 incompatibility is inferred from that warning.

Most importantly, public source review confirmed post-2.4.0 VFS read-error
and temporary-file fixes, plus notice/permission ambiguities. Exact 2.4.0
must not be enabled merely because basic encryption/tamper tests passed.
See D-009 for immutable fixes and component obligations. An owned native
build would be a delivery/servicing choice, not permission to invent crypto,
ask end users to compile, bypass installed gates or adopt commercial binaries.
That encrypted-build choice is now **historical/superseded**, not a prerequisite
for the approved standard-SQLite implementation below.

### R04 Approved Standard-SQLite Continuation - 2026-10-06

The owner explicitly chose standard SQLite under supplied LocalApplicationData
with verified private permissions, accepting readable copies outside that
boundary. [D-009](Decision_Register.md#approved-profile-secured-sqlite-baseline---2026-10-06)
and the canonical architecture/security/acceptance/lifecycle documents now
reflect this decision. Existing encryption experiments remain historical.
No native build, package upgrade, new dependency or database key is required.

[WindowsSqliteHostTaskStore](../src/Kora.Windows/Storage/WindowsSqliteHostTaskStore.cs)
implements a separate private `HostStorageV1` partition without Keys, actual
owner/ACL/reparse checks, version-1 database identity/schema and integrity checks,
FULL-synchronous transactions, optimistic revisions, immutable request identity
and atomic current-state/ordered-event updates. Existing missing/corrupt data,
unsupported schemas and unexpected permissions fail without replacement/repair.
Recovery reads are bounded to 100 and use the existing Interrupted/Unknown
domain transitions without an executor or replay callback.

[Actual SQLite/ACL tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteHostTaskStoreTests.cs)
cover reopen, ordered receipts, stale identities/revisions, cancellation before
I/O, corrupt/missing databases, unsupported versions, invalid persisted
identity/origin/event projections and permissive files. These are unique owned
scratch tests, not user-data, process-kill, power-loss or installed acceptance.
The store remains **uncomposed**; transcript routing, durable authoritative
evidence, migrations beyond v1, backups, retention and managed-copy deletion
remain the concrete next work. The complete R04 milestone is not claimed.

#### Standard-SQLite Validation Receipt

Validated the **uncommitted continuation on `7473902`, main base `3e8558f`**,
not a newly committed/published revision. Windows build 26300/x64, .NET SDK
10.0.401/runtime 10.0.12, feature version 0.1.0. No dependency manifests or
locks changed; existing locked-restored assets were reused with `--no-restore`.
Earlier analyzer/test-compilation errors were corrected before this final run.

| Check | Final actual result |
|---|---|
| Full eight-project Release solution | Zero warnings/errors, analyzers enabled |
| Core full suite | 269 passed, zero failed/skipped |
| Application full suite | 780 passed, zero failed/skipped |
| Windows full suite | 405 passed, zero failed/skipped; includes 14 actual private SQLite cases |
| Total | **1,454 passed** |
| Fresh Core/Application coverage | 5,404/5,404 lines; 2,209/2,209 branches; 617/617 methods, all 100%. Only latest reports from this continuation were merged. |
| Licence/notices | Approved/current; no notice or lock changes |
| Version/fake-release/payload contracts | All passed. Release tests use fake GitHub; no real publication. |
| x64/x86 framework-dependent publishes | Both passed with existing locked assets; exact manifests verify 201/197 files including licence texts. Manifests identify base HEAD; this working copy remains dirty. |
| Native closure | x64 7 PE files/10 declarations, x86 5/6; exact SHA-256 and machine/declaration match to prior inspected closure. Initial inspection-script property enumeration was corrected; no native asset change. |
| Scope not performed | No real application launch, installer/MSI/Burn/ICE rerun, installed loading, process-kill, power-loss, backup/deletion or user-data mutation. |

Exact build/test/publish and policy commands:

```powershell
dotnet build .\Kora.slnx --configuration Release --no-restore
dotnet test --project .\tests\Kora.Core.UnitTests\Kora.Core.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\profile-sqlite-core --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix core
dotnet test --project .\tests\Kora.Application.UnitTests\Kora.Application.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\profile-sqlite-application --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix application
dotnet test --project .\tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\profile-sqlite-windows --report-trx
.\eng\Test-DependencyLicenses.ps1
.\eng\Test-BuildVersion.ps1
.\eng\Test-GitHubRelease.ps1
.\eng\Test-InstallerPayloadContracts.ps1
.\eng\Assert-CodeCoverage.ps1 -ReportPath .net-test-artifacts\profile-sqlite-coverage\Cobertura.xml -MinimumLine 100 -MinimumBranch 100
dotnet publish .\src\Kora\Kora.csproj --configuration Release --runtime win-x64 --self-contained false --no-restore --property:RestoreLockedMode=true --output artifacts\R04-profile-sqlite-win-x64
dotnet publish .\src\Kora\Kora.csproj --configuration Release --runtime win-x86 --self-contained false --no-restore --property:RestoreLockedMode=true --output artifacts\R04-profile-sqlite-win-x86
```

Both publish directories were passed through `eng\Test-InstallerPayload.ps1`
with `-Version 0.1.0 -SourceRevision 74739023458bae7022cabc72ce1c4b34d8e602e1`,
first `-WriteManifest`, then verification. Tests use unique owned directories
and dispose them; no leaked storage fixture directories remain.

#### PR Integration Rebase - 2026-10-06

Main advanced during publication preparation to `fd71c80` (RT2 PR #37).
Both R04 commits rebased cleanly; combined production source at `3019a2b`
retains the merged bounded RT2 findings/all-path Blocked status and the
owner-approved standard-SQLite baseline with optional future R30 encryption.
The final full Release build has zero warnings/errors; Core 269, Application
780 and Windows 405 all pass (1,454 total, zero failed/skipped).
Fresh Core/Application coverage remains 5,404/5,404 lines, 2,209/2,209 branches
and 617/617 methods. Build/test commands are the same as the standard-SQLite
receipt above, with results under `.net-test-artifacts\r04-pr-*`.
This is this branch's root-solution evidence, not a rerun or enlargement of
the separate RT2 experiment's admission. R04 integration remains partial.

#### PR #39 Windows File-Ownership Correction - 2026-10-06

Both Windows CI jobs at `d39f411` (runs `37432278634` and `37432272073`)
failed **43 of 405 tests**; 362 passed, none skipped. The common first failure
was the exact-user owner check on a newly created `operation.lock`. Ordinary
file creation did not set an owner; an elevated token can default to
Administrators ownership even when the inherited DACL is private. The actual
runner owner SID was not logged, and local elevation was not performed.
This is a creation-policy defect, not evidence that a package upgrade is needed.

The correction supplies current-user ownership and protected user-only ACLs
at atomic `CreateNew`, shared by leases, staging, the initial database and
its rollback journal. Existing files are opened without implicit creation;
no existing ACL/owner is normalized and no existing content is overwritten.
SQLite connections reuse the pre-created journal with `PERSIST`,
`synchronous=FULL` and memory temporary storage. Missing/permissive journals
fail explicitly before database access; missing journals are not recreated.
Earlier uncomposed prototype databases without the journal require explicit
recovery/migration. Hot-journal/process-kill acceptance remains open; this
change does not claim it or expand production composition.

Local validation of the **uncommitted correction on `d39f411`, main base
`b302b86`**: Windows build 26300/x64, SDK 10.0.401/runtime 10.0.12,
feature version 0.1.0. No dependency manifest, lock, native provider or CI
workflow change; existing locked-restored assets were reused.

| Check | Actual result |
|---|---|
| Full Release solution | Zero warnings/errors, analyzers enabled |
| Focused storage suite | 59 passed, zero failed/skipped |
| Full Core / Application / Windows | 269 / 780 / 412 passed; **1,461 total**, zero failed/skipped |
| Fresh Core/Application coverage | 5,404/5,404 lines; 2,209/2,209 branches; 617/617 methods, all 100%; only the two new reports were merged |
| Licence/notices, version, fake-release, payload contracts | Passed; no dependency/notice changes or real release |
| Fresh framework-dependent x64/x86 publishes | Passed; exact manifests verify 201/197 files including licence texts, identifying base HEAD rather than a future commit |
| Native closure | x64 7 and x86 5 native PE files, including app hosts, match the previously inspected publish hashes and expected machine type; no native bytes changed |
| Scope not performed | No elevation, token/security-policy change, real user-data mutation, installer/app launch, installed loading, process-kill or power-loss trial |

Seven new Windows cases cover explicit owner/DACL creation, async handles,
existing-file/no-repair/no-overwrite behavior, permissive leases, path escape,
database/journal ownership across commits/reopens and missing/permissive
journals. Corruption-test connections explicitly retain the same journal
policy, so the intended schema/identity/projection failures are tested rather
than a missing-journal proxy. Synthetic artifact-budget files are created
privately before testing the byte/count limits. Owned fixtures were disposed.

Exact commands:

```powershell
dotnet build .\Kora.slnx --configuration Release --no-restore
dotnet test --project .\tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj --configuration Release --no-build --filter-class 'Kora.Windows.IntegrationTests.Storage.*' --results-directory .net-test-artifacts\r04-ci-storage --report-trx
dotnet test --project .\tests\Kora.Core.UnitTests\Kora.Core.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\r04-ci-core --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix core
dotnet test --project .\tests\Kora.Application.UnitTests\Kora.Application.UnitTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\r04-ci-application --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix application
dotnet test --project .\tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj --configuration Release --no-build --results-directory .net-test-artifacts\r04-ci-windows --report-trx
dotnet reportgenerator '-reports:.net-test-artifacts\r04-ci-core\*.coverage.cobertura.*.xml;.net-test-artifacts\r04-ci-application\*.coverage.cobertura.*.xml' '-targetdir:.net-test-artifacts\r04-ci-coverage' '-reporttypes:Cobertura;TextSummary' '-assemblyfilters:+Kora.Core;+Kora.Application'
.\eng\Assert-CodeCoverage.ps1 -ReportPath .net-test-artifacts\r04-ci-coverage\Cobertura.xml -MinimumLine 100 -MinimumBranch 100
.\eng\Test-DependencyLicenses.ps1
.\eng\Test-BuildVersion.ps1
.\eng\Test-GitHubRelease.ps1
.\eng\Test-InstallerPayloadContracts.ps1
dotnet publish .\src\Kora\Kora.csproj --configuration Release --runtime win-x64 --self-contained false --no-restore --property:RestoreLockedMode=true --output artifacts\R04-ci-fix-win-x64
dotnet publish .\src\Kora\Kora.csproj --configuration Release --runtime win-x86 --self-contained false --no-restore --property:RestoreLockedMode=true --output artifacts\R04-ci-fix-win-x86
```

Both publishes were passed through `eng\Test-InstallerPayload.ps1` with
`-Version 0.1.0 -SourceRevision d39f411fdacf2bf8e7b5a2ed78b72a2b93982df7`,
first `-WriteManifest`, then exact verification after copying the approved
licence texts. Fresh GitHub checks on the pushed correction are separate from
this local receipt; the earlier failures are not called green or rerun evidence.
R04 remains partial and R30 optional encryption remains deferred.

#### Current-Main Refresh After CI Correction - 2026-10-06

The correction `7795674` passed fresh push CI `37436375408` (Windows 412)
and PR CI `37436381184` (Windows 437), including portable coverage/packaging
and WiX jobs. The differing counts reflect GitHub's merge candidate including
new main, not different test selection or skipped failures. Main advanced to
`90d8f48` (presence PR #36); strict up-to-date protection still marked #39
BEHIND. Merge `8be4f9d` incorporates that approved upstream work cleanly
without rewriting the published correction or changing other worktrees.
The transcript tracing wrapper and upstream presentation changes both remain.

Full local validation at **`8be4f9d`, main base `90d8f48`** uses the same
SDK 10.0.401/runtime 10.0.12, Windows build 26300/x64 and exact commands
above, with result/coverage prefixes `r04-ci-main-*` and publish directories
`artifacts\R04-ci-main-win-x64` / `artifacts\R04-ci-main-win-x86`.
Release has zero warnings/errors; Core **307**, Application **941** and
Windows **437** all pass: **1,685 total**, zero failed/skipped.
Fresh latest-only coverage is **5,633/5,633 lines**, **2,285/2,285 branches**
and **664/664 methods**, all 100%. Licence/notices, version, fake-release and
payload-contract gates pass. Both fresh publishes verify **201/197 files**
including licence texts with source revision
`8be4f9dc26c961d5f0b1b413a2a8729239cfe10d`; all **7/5 native PE files**
retain the prior exact hashes and expected architecture. No new dependencies,
native bytes, privilege/token changes, real app/installer launch or expanded
storage/interruption acceptance are implied. Fresh checks after publishing
this refresh remain distinct from the earlier green runs.

## R02 Runtime/Provider Follow-Up Gates

These are sub-gates of R02, not new acceptance milestones or a claim that
other R02 feasibility branches are complete. The
[technical continuation](Runtime_Provider_Feasibility.md) specifies the
candidate boundaries and unsupported-control decision path.
No gate below is closed by merging the retained Node experiment.
The [deferred runtime/provider checklist](Deferred_Validation.md#runtimeprovider-follow-up)
records preparation and evidence to collect. The separate
[actual .NET RT1 fixture](../experiments/r02-dotnet-control-proof/README.md)
now passes its explicitly approved source-built minimal profile; the separately
approved released-profile MG1 envelope also passes. RT2 all-path admission,
PV1 and Gate 0 remain open. Partial research does not enable production exposure.

| Gate / owner | Current state | Needs / next action | Exit evidence and downstream effect |
|---|---|---|---|
| R02-RT1 - .NET control-point parity; runtime engineering lead | PASS, scoped source-built profile, 2026-10-05 UTC; 45/45 tests; released NuGet byte parity BLOCKED | Preserve [fixture/disposition](../experiments/r02-dotnet-control-proof/evidence/disposition.json): public v1.0.16 exact source, runtime 1.0.90/protocol 3, Windows x64, .NET 10.0.12/SDK 10.0.401. User explicitly approved unmodified source build after NuGet TLS failure; separate locks/license/native hashes and byte reproduction recorded. | Final serialized initial/history/all-status/exception paths, actual pre-effect denial, streaming/errors, session I/O and cancellation/isolation measured; denied effects/forwarded markers zero. Hook-only FAIL retained, Node witness unchanged. Unblocks RT2/MG1 proof for these bytes only; no production adapter, sidecar or D-001 closure. Retest a different artifact/profile. |
| R02-RT2 - Runtime lifecycle observation; runtime and security leads | BLOCKED, 2026-10-06 UTC; 20/20 bounded tests and two locale contracts pass for exact RT1 bytes; P0 | [Independent fixture/receipts](../experiments/r02-runtime-lifecycle-proof/EVIDENCE.md): final source reproduction and full 45/45 staged RT1 regressions pass; PowerShell/conhost startup descendants and transient policy-test files observed. Live watcher/IP Helper/Toolhelp/managed diagnostics are incomplete and do not prevent native paths. User retained fail-closed contract; privileged tracing deferred to separately approved dedicated host. | Obtain attributable all-file/all-destination/native-diagnostic observation with loss controls and actual native mediation/prevention, not model counters/scans or W2 best-effort dependency policy. No denied-marker native egress/recoverable persistence claim until observable/controlled. Repeat account-specific paths in PV1. Keeps R08/runtime unavailable; does not close worker/deployment/storage or durable host authority. |
| R02-MG1 - .NET host management envelope; runtime engineering lead | PASS for separately user-approved released SDK 1.0.16 / unchanged RT1 native 1.0.90 minimal HTTP/stdio, 2026-10-06 UTC; historical source-built/Node witnesses unchanged | [Independent fixture](../experiments/r02-dotnet-management-proof/README.md): released hashes and exact source pinned; 45 RT1 regressions repeated before 22 host + 16 actual runtime cases. Complete 32768/32769 input and 4096/4097 typed JSON, 15000-ms dispatch deadline with real held inference and stalled native abort ack, single admission/30 monotonic rolling failures/no forwarded retry, independent manager and Unknown quarantine pass. Initial source-built reproduction blocker retained, not inferred away. | Supports R13 envelope implementation and a management PV1 proposal after applicable RT2 lifecycle admission. Host controls remain local while quarantined; SDK ack/socket closure never certify rollback or physical computation stop. R04 owns durable identity/audit/no-replay. No account capacity/cost, production protocol, task slots/resource leases or scheduler acceptance; retest any changed artifact/profile. |
| R02-PV1 - Approved account/provider trial; runtime lead with account owner and security/legal review | BLOCKED; no account/live usage approved | RT1/RT2 for live runtime trials; MG1 additionally for management. First prepare intended provider/model/region, user-owned supported auth, permitted assistant/SDK use, plan/policies, published limits and explicit spending controls. Ask for account and usage-budget approval before potentially paid inference/provisioning. | Record real auth/error behavior, destination/content isolation, terms eligibility, quota/rate limits and billed-cost assumptions/limits. Execution profile acceptance feeds D-001/R08; management-specific two-execution-plus-manager capacity and usage envelope additionally feed D-004/R13. Failed/incompatible/unapproved service stays disabled with deterministic local choices. |

**Next sequence:** scoped source-built RT1 and separately approved released
RT1/MG1 controls passed; distinct profile identities remain mandatory.
RT2 safe observations are now recorded but all-path admission is Blocked.
Follow its [dedicated-host/R08/MG1/PV1 handoff](../experiments/r02-runtime-lifecycle-proof/EVIDENCE.md#handoffs).
PV1 live trials follow the relevant technical gates and explicit approval.
If RT1 fails, bring the supported-runtime/sidecar/inference-adapter options
back as a D-001 decision with evidence; do not weaken Gate 0.
Local R06/R07, R03 and the deterministic R12/R13 core retain their own
prerequisites and need not wait for hosted-model trials.
Downstream packages listing R13 require its deterministic core and admitted
scheduler/concurrency evidence; its optional model-assisted stage is not an
implicit prerequisite unless that dependent capability uses management inference.
Neither I18 nor these runtime follow-ups close D-003/local inference,
speech/hardware, worker containment, deployment or R04 private-profile
storage integration/recovery/lifecycle gates. D-009 supersedes mandatory
encrypted-storage admission; R30 remains optional.

### R04/R05 Durable Interaction and Minimal Session Authority - 2026-10-06

**Partial integrated slice, not package closure.** One owner implemented the
mutually dependent authority schema/transactions on clean isolated branch
`agents/kora-durable-interaction-20261006`, based on latest fetched main
`c5dffabf8f4fa767147be06dd8b296238ea97da0`, tree
`ce31623ffbcb555b5a940fe2389221c480e97c0d` (#41-46 included).
The orchestration checkout stayed on clean stale
`feature/personalized-startup-greeting` at `a7bbc04`; no edit/build/restore ran
there or in completed worktrees. Path and branch collisions were checked before
the canonical helper created the new tree. Delivery remains uncommitted;
no commit/push/PR/merge or real consequential effect was performed.

| Boundary | Actual delivery | Still gated |
|---|---|---|
| Production adapter/composition | [WindowsSqliteHostInteractionStore](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.cs) implements the existing `IHostInteractionStore`; [Program](../src/Kora/Program.cs) initializes it and registers the existing question/authorization services against it. Windows tests use those exact services with the production adapter, not an in-memory substitute. | Native presenters/input and arbitrary/model/tool/OS dispatch are not activated. Direct lock/power and legacy named-action preferences are unchanged. |
| Exact schema/privacy | Private `InteractionStorageV1/interaction.db`, application ID `1263489587`, exact version-1 STRICT schema: `work_sessions`, `host_observations`, `host_questions`, `scoped_grants`, `perpetual_grants`, `security_audit_events`, `authority_head`. Typed bounded JSON preserves question spec/options/draft/answer/channel and exact host proposals/grants; indexed identities/revisions are validated against the typed payloads. Existing owner/ACL/reparse checks, privately preowned PERSIST journal, FULL synchronous writes, memory temporary storage and no database key apply. | No general conversation/context/audio/secret store, legacy-grant migration, managed backup or broader schema migration. Existing schema/journal/audit/permission failures are explicit; no repair/replacement/shared fallback. Readable-copy and same-user/admin limits remain disclosed. |
| Atomicity and task prerequisite | Every authority write holds the task partition's existing exclusive admission lease, reads matching committed nonterminal intent, then acquires the interaction lease and commits changed records plus typed audit/head in one interaction SQLite transaction. Task cancellation/terminal commits use the same task lease. Lock order is always task then interaction; passive reads use only interaction. No cross-database write, SQLite ATTACH, diagnostic sink fallback or eventual authority write exists. | General pre-effect worker/adapter admission and effect-specific receipts remain separate. The version-query receipt still proves only a returned offline query. Lost COMMIT certainty throws storage error, never a cancelled/success-shaped receipt or automatic retry. |
| Typed audit authority | Append-only ordered event hashes/head and per-record digest references bind current projections to typed audit commits; envelopes carry live host/W3C IDs, committed intent revision, session generation, typed outcome/references, audit due date and state digests, not question/answer content. Audit write failure rolls back the decision. Independent file/evidence projections are not authority. | Anchors/pruning and coherent whole-store replacement/rollback protection remain D-008/R04 work. Local sequencing/ACLs do not establish same-user/admin tamper prevention. |
| Current trusted snapshots | Host-only optimistic snapshot publication requires a live matching request and committed intent/session. Exact operation/content/identity/destination/policy snapshots, observed-content revocation and pending-approval invalidation commit together before exposing changed content. An adapter lifetime identifier blocks previous-run observations until freshly resolved/re-admitted. | Actual native immutable byte/resource review, source acquisition/revocation and foreground voice targeting are not supplied by persistence. Provider/model labels and viewer text confer no authority. |
| Minimal R12 authority | Explicit create, Active/Done/resume and authority-removal tombstone. Active restart retains generation; Done/resume/removal advance it, close pending questions and revoke Once/Session grants. Independent Perpetual table has no session FK, grant due date, expiry/retention/eviction; minimal exact provenance survives authority removal. Reading does not refresh anything. | No Sessions UI, history/search/queues, inactivity clock, timer purge, live/uncertain-work disposition or recoverable-copy deletion. Removing authority rows is not complete session content deletion; journals/free pages/backups remain part of the later R12 contract. |
| Stable viewer/service handoff | Existing host IDs, `HostQuestionKey` revisions, exact proposals/bindings and services are unchanged. Passive typed `ReadQuestionsAsync(sessionId)` supplies persisted question/spec/options/draft/channel/status/proposal for a future trusted presenter. [Interaction handoff](Interaction_And_Sessions.md#durable-authority-and-typed-presenter-handoff) defines exact-key replies and no rendering-derived authority. | No passive viewer chrome or sibling release/bootstrap/native-helper scripts were edited or merged. R14 rendering does not satisfy immutable approval review. |

#### Durable Interaction Validation Receipt

Validated uncommitted source on the stated base, Windows x64 and SDK 10.0.401.
Fresh isolated assets required locked restore after the expected initial
NETSDK1004; no package/lock/notice changes. Initial fixture assertions were
corrected for the explicit UnauthorizedAccessException ACL contract and
foreign-key-protected corruption setup. A multi-wildcard test selector ran
zero tests and was rejected; the supported storage namespace selector then
passed. An initial concurrent RID publish collided on the shared build-version
output; final RID publishes are sequential. These attempts are not counted
as passed gates or reasons to weaken thresholds.

| Gate | Actual result |
|---|---|
| Root Release | Zero warnings/errors, repository analyzers and warnings-as-errors unchanged. |
| Full suites | Core **338**, Application **1,049**, Windows **541**; **1,928 passed**, zero failed/skipped on the completed final set. Windows includes **46 new interaction/interruption cases** (including the storage-only child entry); earlier storage-targeted run passed 154 cases before the final negative additions. |
| Latest-only portable coverage | **6,246/6,246 lines, 2,685/2,685 branches, 742/742 methods**, all 100%. Exactly one fresh terminal report per portable project; no historical/sibling inputs, exclusions or threshold changes. |
| Real disk/race negatives | Reopen typed choices/text/drafts/answers/proposals/grants; duplicate/concurrent approvals and Once use; exact changed digests/source/revision/current policy/expiry; permanent observed-content revocation; revoke/edit/use and Done/use races; task cancellation waits through approval/use COMMIT and blocks subsequent decisions; root optional invocation vs wrong origin/invocation; recovered task cannot restore Once authority; Active generation and independent Perpetual authority survive restart/removal. |
| Atomic interruption/failure | Cancellation after staged audit/question/grant writes rolls all back. Actual in-transaction audit-insert failure admits no decision. Only owned storage child PIDs are killed at uncommitted/committed checkpoints; real hot-journal recovery preserves ACLs and yields all-or-none question/grant/audit state, without replay or automatic consume. Existing full suite retains truthful Interrupted/Unknown query/task recovery. |
| Corruption/private boundary | Missing database/journal/admission lease/schema/audit head, unsupported schema, malformed typed/null payloads, altered projections/content/audit chain, corrupt bytes and bad file ACL fail explicitly without replacement/repair. Out-of-snapshot question identity collision cannot overwrite another owner and rolls back its staged audit. Existing shared private-directory tests retain owner/reparse and bounded admission coverage. |
| Licences/locked payloads | Licence/notice policy and payload-negative contracts passed. Final locked framework-dependent x64/x86 publishes and exact manifests passed: **201/197 files**, at base HEAD with uncommitted compiled source. Static native PE machines/imports and pinned package-asset SHA-256 equality passed: **7 PE files/10 declarations** for x64, **5/6** for x86, including bundled `e_sqlite3.dll`. Receipt: `artifacts/durable-terminal-native.json`; no app/installer launch or installed loading/power-loss acceptance. |

Terminal TRX/coverage artifacts are under
`.net-test-artifacts/durable-verified-{core,application,windows,coverage}`;
final unlaunched payloads are
`artifacts/durable-terminal-win-x64` and `artifacts/durable-terminal-win-x86`.
The verified tree is retained for explicit later commit/PR approval.
No transcript/audio/model/installer, real user-store migration, lock/power,
elevation/account/security-policy or live consequential trial was performed.

### R04 Production-Store Interruption and Reopening - 2026-10-07

**Bounded process-interruption proof, not R04/D-009 closure.** The production
standard-SQLite provider, exact version-1 schemas, PERSIST/FULL policy,
committed-intent lease and atomic interaction audit remain unchanged.
Internal fault checkpoints are available only to maintained Windows tests;
normal composition supplies no observer.

| Boundary | Maintained evidence | Limit |
|---|---|---|
| Task adapter writes | [Task interruption](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteTaskInterruptionTests.cs) uses actual `CommitAsync` for uncommitted intent/dispatch/terminal and committed intent/dispatch/terminal; interrupted and completed audited recovery are also killed/reopened. [Deterministic task regressions](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteHostTaskStoreTests.cs) assert staged projection/event rollback on failure/cancellation and success after committed cancellation. | No raw-SQL write proxy, automatic replay or executor callback. Previously committed success is preserved, never inferred for unfinished work. |
| Evidence adapter writes | [Evidence interruption](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteEvidenceInterruptionTests.cs) kills only owned children before/after actual diagnostic, typed audit and activity-with-32-links transactions; exact prior envelopes survive, links are all-or-none, audit sequence remains contiguous and repeat reopen adds nothing. Deterministic precommit faults roll back staged writes. | Evidence does not authorize a grant or certify an OS effect. Interrupted audited recovery may retain a truthful Unknown attempt before a later recovery receipt. |
| Interaction authority | [Interaction interruption](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteInteractionInterruptionTests.cs) covers approval/question/grant/audit, Once-use count and Done/question cancellation/grant revocation/generation transactions before/after COMMIT while the task lease remains held. Restart requires fresh observations; stale generation/question keys cannot restore grants. Audited task recovery leaves interaction records unchanged and prevents late decisions. | No generic effect dispatch, new native input route or schema change. Full session lifecycle/copy deletion remains R12 work. |
| Private reopening | Tests observe the actual hot rollback header after real pager writes; retained PERSIST length alone is insufficient. Reopen preserves protected owner/journal ACLs. Missing/permissive hot journals, held admission lease/database access and existing schema/corruption failures remain explicit, without replacement/repair. [Owner policy](../tests/Kora.Windows.IntegrationTests/Storage/RestrictedStorageDirectoryTests.cs) rejects a foreign-owner descriptor without changing an OS owner or requiring privilege. [Fixture release tests](../tests/Kora.Windows.IntegrationTests/Storage/OwnedStorageChildProcessTests.cs) require exclusive database/journal/admission-handle quiescence after child exit, retry only sharing violations under the existing deadline, and prove cancellation/path/missing-file refusal without mutation. | Only GUID-owned disposable roots/helper PIDs are used; no application/installer launch, existing user data, elevation or shared policy changes. Process exit is not assumed to prove resource release; no timeout increase or weakened storage assertion. Process kill does not certify physical power-loss/fsync hardware or installed acceptance. |
| Experiment disposition | Applicable transaction/journal/intent assertions are maintained against the actual production adapters. [Specific retained-proof map](../experiments/r02-storage-proof/README.md#production-recovery-migration-and-retention---2026-10-07) explains why no shared executable is retired: both experiment candidates use the SQLCipher engine and retain unique encrypted/unkeyed WAL, crypto/DPAPI/rekey, migration, artifact/backup/deletion and native comparison cases. Historical receipts remain unchanged. | No blanket deletion, encryption admission reversal or claim that PERSIST production tests supersede encrypted/WAL/native comparison evidence. |

Required validation remains the locked Release/analyzer build, complete Core,
Application and Windows suites, unchanged portable 100% line/branch gates and
PR CI. General artifacts/backups, retention/deletion, generic dispatch, UI,
source tooling, installed/power-loss guarantees and full R04 closure are outside
this slice.

**Combined local validation receipt:** based on merged R03/R17/R05 main `40427e6`; locked
solution dependencies unchanged; root Release build with zero warnings/errors;
Core **365**, Application **1,095**, Windows **682** passed (**2,142 total**,
zero failed/skipped). The focused storage run passed **197**, including the
resource-release barrier regressions. Only fresh Core/Application reports
were aggregated: **6,475/6,475 lines, 2,839/2,839 branches and 777/777 methods**,
all 100%, with unchanged thresholds. Dependency-license/notice policy passed.
Results are in this isolated worktree's
`.net-test-artifacts/r04-cohort-final-{core,application,windows,coverage}`; no
disposable storage root remained after the completed run. Initial PR CI exposed
post-kill fixture sharing violations before journal inspection; explicit
exclusive-handle quiescence replaces the process-exit assumption without
increasing deadlines or weakening assertions. PR CI is independently required
before merge; this receipt is not installed/native-load or power-loss evidence.
Combined version/channel tests, publication contracts (**381 assertions,
56 Actions process cases and 15 partial-write/hard-interruption retries**) and
**123 immutable source-tool contracts** also passed against the rebased tree.
Publisher/source-tool files were not edited by R04; no release, downloaded
code, installer or application was executed by these fixture checks.

### R05 Bounded Authorization/Question Foundation

**Historical foundation receipt - 2026-10-06.** The subsequent
[durable continuation](#r04r05-durable-interaction-and-minimal-session-authority---2026-10-06)
supplies production persistence/minimal session authority, not the remaining
native-input/dispatch gates. The original portable core/application
services are implemented and tested against the merged R04 host contracts.
This parallel partition changes no MainViewModel dispatch, action handler,
UI composition or production storage schema. It enables no new execution.

| Delivered boundary | Source and test evidence | Remaining integration gate |
|---|---|---|
| Host-owned typed questions | [Question service](../src/Kora.Application/Interaction/HostQuestionService.cs), bounded single/multiple-choice/text [specification](../src/Kora.Core/Interaction/QuestionSpec.cs), existing HostId/HostRequest/HostRevision; [contract tests](../tests/Kora.Core.UnitTests/Interaction/InteractionContractTests.cs) and [service tests](../tests/Kora.Application.UnitTests/Interaction/HostQuestionServiceTests.cs) | Durable questions/drafts/answers, native exact review/readback, trusted UI/voice input and foreground spoken-target ownership; typed forms/secure flows are not added. |
| Exact host operation and grant applicability | [Binding/proposal contracts](../src/Kora.Core/Authorization/ExactOperationBinding.cs), [authorization service](../src/Kora.Application/Interaction/HostAuthorizationService.cs) and [tests](../tests/Kora.Application.UnitTests/Interaction/HostAuthorizationServiceTests.cs); exact source/content/implementation/invocation/resource/identity/destination/transformation/policy/effect checks, origin preservation and unknown/prohibited denial | Admitted registry/catalogue, actual canonical byte/resource snapshots, immutable review and action-specific ownership/privacy/containment/deployment proofs; no named-action preference migration. |
| Scope, lifecycle and retention separation | Once consumed atomically; Session bound to durable work-session ID/generation; Perpetual record has no expiry/retention/eviction. Tests reject ended/resumed/cross-session authority and preserve independent records across history removal/recreated services. | R04/R12 adapter must advance lifecycle generations, preserve Active-session generations on restart and retain Perpetual records outside session deletion. Synthetic history removal/recreated services are not disk restart/deletion acceptance. |
| Race/revoke/change and audit certainty | [Atomic store seam](../src/Kora.Core/Storage/IHostInteractionStore.cs), matching live HostActivity and typed SecurityAuditEvent; concurrent duplicate approval/Once consumption, revoke/use, stale revisions, observed-content permanent revocation and [storage/audit/cancellation tests](../tests/Kora.Application.UnitTests/Interaction/InteractionCommitTests.cs) | Verified R04 durable task/evidence handoff and agreed adapter/schema ownership; atomic authoritative audit failures must block consequential dispatch. A use receipt is not a reusable dispatch token or effect receipt. |

**Validation receipt:** root Release build passed with zero warnings/errors;
Core 333/333 and Application 1001/1001 passed. Fresh-only combined portable
coverage passed the exact 100% line/branch gate, including named transition
methods (not only callback wrappers). No coverage exclusions or thresholds
were changed. Dependency manifests, native payloads and installer/bootstrap
tooling are unchanged.

Earlier full Windows runs and a user-approved rerun exposed intermittent
private-desktop cleanup failure in the
[native presence hit-testing test](../tests/Kora.Windows.IntegrationTests/Presentation/WindowsPresenceWindowInputTests.cs)
at `SetThreadDesktop`, not the subsequent `CloseDesktop`. The user-authorized
test-only fix disables IME initialization on its disposable native thread
before creating windows, preventing hidden text-service resources from
blocking desktop restoration. It preserves the actual click-through/Ctrl
assertions, checks cleanup errors, waits for worker completion and adds a
16-cycle private-desktop restoration/release regression. Six fresh targeted
runs passed both tests; three fresh full Windows runs each passed 438/438,
with no skips or exclusions. Final root Release and fresh-only portable
coverage also passed again. Failure and passing TRX evidence is retained
under `.net-test-artifacts/r05-native-test-fix`; the final portable report is
under `.net-test-artifacts/r05-coverage-final`. Production Windows/UI code and
system-wide input settings are unchanged. **The automated all-suite gate now
passes; production integration remains pending the handoff below.**

**Required handoff before production wiring:**

1. R04 supplies verified admitted intent/task/evidence composition. Agree the
   durable interaction/grant adapter partition and schema ownership before
   persistence changes; do not alter its dispatch or store schemas in parallel.
2. Resolve current host operation bytes, source access, identity/account,
   destination and mandatory policy inside the atomic admission boundary.
   Complete declared identity remains exact; W2 transitive tracking is best
   effort and neither a containment profile nor broader authority.
3. Serialize task cancellation, session Done/delete/resume, grant revoke/edit,
   observed content changes and policy generations with use. Successful
   intent/approval/audit commits are mandatory before any consequential effect.
4. Compose trusted native review/input, exact foreground voice targeting and
   immediate pre-effect revalidation; repeat real durable restart/deletion/
   interruption and action-specific receipt/containment trials.

This does not close all R05, D-008/D-009/D-013 or A0-A4. General script/worker/
tool/remote adapters, real lock/power, install/account/credential/security
policy changes and microphone/app execution remain unexposed by this slice.
No dependency/profile architecture or sibling merge is introduced.

### Core Foundations and First Useful Interaction

### R07 Bounded Clipboard Preview - 2026-10-07

Delivered host-only explicit plain-text snapshot/native preview, exact-ID
reuse/revoke/clear through the same deterministic request workflow. The
portable `Kora.Tools.Clipboard` grouping has one action class per supported
tool (`ClipboardRead`, `ClipboardReuse`, `ClipboardRevoke`) and one immutable
snapshot broker. [Implementation guidance](Commands_Tools_And_Skills.md#built-in-tool-source-layout-and-implementation)
is also linked from repository instructions; existing six R06 implementations
are documented migration candidates, not refactored in this delivery.
Core owns strict Unicode/UTF-8/provenance contracts; Windows owns the
request-owned STA/native borrowed-handle seam; desktop owns inert preview.

Explicit admission and presentation/reuse revalidation reject unknown
privacy/ownership, nonhuman origin, stale call/generation and late results.
Whole-text 256 KiB UTF-8 and malformed-surrogate/terminator/format/version/
contention/access failures are explicit, without truncation-success. Native
release finishes before exit; a pending read blocks handoff and exposes
cancellation. Clear/revoke/close/privacy/owner/call/exit/disposal suppress the
selected content; clipboard changes never silently replace it.
No watcher, clipboard write, URL/HTML/image/file acquisition, content history,
persistence or model submission is added. Preview is neither approval nor
proof that secrets were detected/redacted. Explanation is unavailable until
qualified local tool-loop, clipboard-answering and complete secret/destination
envelope gates pass.

Maintained tests cover exact command/native action parity, private native
format/contention/change/Unicode/size and release/cancellation seams,
immutable reuse/revocation, stale callbacks and original origin/privacy,
host tracing and content noncapture. Root Release/analyzers and all four
suites pass; fresh Core/Application/Tools portable coverage is 100% line and
branch. Tests never read/write the shared clipboard or launch Kora.
Actual native clipboard/accessibility/latency trials and A0/A2 acceptance
remain open, not inferred from deterministic tests.

R02 experiment disposition: its six synthetic clipboard source cases are
carried into maintained host-preview tests for exact bytes and non-dispatch/
non-provider/non-persistence behavior. No executable experiment is removed:
the original quality rubric, candidate identity, CPU-floor/resource,
offline/provider transport and real-answer/cancellation proofs are not
equivalent to a model-free preview and remain useful unique evidence.

| ID and work package | Starting state | Priority/value | Needs (I; proof references E/Q) | Completion condition |
|---|---|---|---|---|
| R01 - Reconcile policy, scope and checkpoint contracts | Design reconciliation complete; approved 2026-10-05; no runtime changes | P0 - prevent incompatible authority and consent implementations | None | Initial-release authority, consent, optional privacy and standalone-lock binding recorded and aligned above; A2/A3 evidence remains required. Standalone application rollback remains deferred R27 work. Runtime/enforcement proof is not claimed by this package. |
| R02 - Qualify capability-specific feasibility profiles | Partial candidates in I01/I06/I09/I16; storage/inference/distribution outcomes recorded; containment I17/runtime I18 retain open gates | P0 - discover runtime/hardware/containment limits | I: R01; E/Q: independently scoped proof branches | Complete runtime RT1/RT2/MG1/PV1 as applicable, retaining rejected hook-only and blocked account/global paths as unavailable. Qualify local model/licence/CPU floor, wake, worker containment and deployment only for affected enablement/manifest scope. D-009 now selects standard SQLite/private profile permissions; encrypted-native/key admission is superseded. Complete [local L1-L5/L6](#r02-local-inference-continuation), W1-W3 and R02-D01/D02/D03 evidence as applicable; historical NSIS assembly is not D-005 closure. Use actual SDK/provider/OS evidence, not aggregate R02 success. Unrelated feature merges do not depend on these experiments. |
| R03 - Establish Windows/audio ownership and privacy foundation | PTT/cross-build ownership, existing external observation/one-second permission polling and deterministic lifecycle/disposal regressions delivered; bounded x64/audio trials pass; full native acceptance open in I03-I06/I12/I13 | P0 - stop unauthorized capture and overlapping owners | R01 | Complete acceptance of cross-build single-owner activation/handoff/return, consent/enablement generations, explicit PTT, bounded audio/transcript buffers and stale-callback rejection. Enforce wake-only versus activated-transcription separation; never label activated grammar capture as production wake. Retain deterministic observer/capture/application tests while proving native lock/disconnect/suspend/permission-polling/device observation and capture/audio/output closure on every required event. Release capture within 500 ms of observed lock in every reference trial. Provide native/tray recovery without model/network/speech. |
| R04 - Introduce durable identities, Activity tracing and authoritative host contracts | **Partial integrated delivery.** Exact version-query intent/evidence/receipt/no-replay recovery, durable interaction/session authority and [consolidated schema-v3 task/question/required-audit transactions with validated frozen-ledger migration and shared lease](#r12r13-bounded-authoritative-task-controls-and-atomic-wait-cancellation), production-store interruption/reopening proof, bounded SQLite/DailyLog/CombinedLog inspection, [committed AuthorityAudit reads](#bounded-r04r14-committed-audit-inspection) and [ordinary startup pruning](#r04-bounded-ordinary-diagnostic-retention---2026-10-07); [inventory](#r04-foundation-delivery) and [baseline](#r04-approved-standard-sqlite-continuation---2026-10-06). | P0 - stable attribution and crash-safe intent | R01, R03; approved D-009 standard SQLite/profile baseline | Complete broader supported migrations, backup/artifact publication/interruption, audit checkpoints/pruning/rollback, session lifecycle/deletion and relevant runtime/tool boundaries. Bounded v3 migration, atomic pre-dispatch wait cancellation, ordinary pruning and read-only query-gap semantics are delivered, not full history, audit/copy deletion, forensic tamper resistance or model tools. Preserve committed-intent and atomic authority/audit prerequisites and truthful receipts. No encryption prerequisite or automatic replay. R04 remains open; installed/power-loss evidence is not inferred. |
| R05 - Build the shared authorization/question gateway | **Partial integrated delivery**, including [bounded native question/exact-record review](#r05r14-bounded-native-shared-question---2026-10-07), [typed foundation](#r05-bounded-authorizationquestion-foundation) and [production durable adapter/service registration](#r04r05-durable-interaction-and-minimal-session-authority---2026-10-06). | P0 - one authority path for direct/UI/model/skill requests | R03, R04 | Complete generic foreground voice targeting and canonical source/resource review beyond the delivered native record review; shared direct/model/tool/worker pre-effect revalidation, action-specific ownership/privacy/containment/deployment proof, effect receipt certainty and full lifecycle/deletion policy. Durable atomic use/audit and Session/independent Perpetual records exist; a consumption receipt is not an execution token. Legacy action-name preferences confer no new authority. Direct lock/power remain unchanged, not complete admission. |
| R06 - Implement the admitted tool registry and local tool/result loop | Bounded read-only host registry delivered in I02; JSON selector in I09/I10 remains partial; no runtime adapter qualification | P1 - natural requests can discover and use Kora capabilities | I: consumed R04/R05 contracts; E: R02 local L1-L5 for model loop only; Q: integrated offered profile | Delivered: six canonical versioned typed read-only handlers and exact local discovery, current-host/lane/request isolation, strict inputs, six-record and complete 4 KiB UTF-8 output bounds, recorded version/readiness/local-runtime facts with unavailable/unobserved reasons, trace and cancellation tests. Remaining: qualified adapters, per-destination admission/egress, skill summaries, approved model invocation/result loop and continued reasoning under the R02-L5 envelope. No settings/evidence/session tools or new execution authority; preserve exact offline lock/power behavior. |
| R07 - Deliver explicit clipboard context and local-first explanation | **Partial integrated delivery:** explicit bounded local plain-text snapshot/native preview, same-ID reuse/revoke; [receipt](#r07-bounded-clipboard-preview---2026-10-07). Inference unavailable; no R02 real answer/offline-success proof | P1 - first useful private vertical slice | I: consumed R03/R04/R05 contracts; R06 loop for explanation, not preview; E/Q: R02 local L5/L6 for answering | Delivered: composed exact command/tray/native workflow, immutable source/snapshot/request/version/time provenance, 256 KiB strict UTF-8 whole-text bound, explicit format/contention/denied/change states, privacy/origin/call-generation cancellation and no content logging/persistence/provider submission. Remaining: qualified local tool/result loop, purpose/secret/destination classification, complete approved-envelope budgeting and actual offline clipboard-answering quality/cancellation/no-egress/native acceptance. Preview/reuse grants no egress or execution authority; no remote fallback. |
| R08 - Integrate the controlled remote runtime and streaming path | Outstanding production adapter; I18 candidate only; RT2 all-path admission BLOCKED despite passing bounded tests; hook-only path rejected; local production inference remains buffered | P1 - complete A0 and provider-neutral interaction | I: consumed R04/R05/R06/R07 contracts; E/Q: R02-RT1/RT2 and execution PV1 for remote profile, local L5 for local streaming | Carry the [RT2 handoff](../experiments/r02-runtime-lifecycle-proof/EVIDENCE.md#handoffs), resolve native observation/prevention before exposure, and integrate the proved .NET/runtime profile through pre-effect authorization, all-status host result sanitization and a final serialized-request egress gate. Disable unverified built-ins/collection/storage/transports; keep credentials host-only and cancellation truthful. Pass Gate 0 including actual account/destination/diagnostic evidence, streamed output/backpressure and zero denied effects/markers. Reuse host contracts and the qualified local envelope for local streaming/iteration; measure user-visible first output and cancellation, not experimental token timing alone. No production Node bridge or alternate provider without an explicit D-001 decision. |
| R09 - Complete production wake, endpointing and speech lifecycle | Partial I04-I06; bounded native tray recovery below, not wake/native acceptance | P0 - enable reliable voice-first use only after quality/privacy proof | I: consumed R03/R05/R06 contracts; E/Q: R02 speech/hardware for affected speech/wake profile, not passive recovery | Package selected licensed detector/VAD/transcription assets; preserve immediate wake-and-command with at most two seconds of overwritten pre-roll and no unrelated pre-activation transcription. Bound command/audio lifetimes; prove playback/echo rejection, voice interruption, TTS stop/shutdown and device recovery. Implement configured activation-name profiles and lifecycle matrix. Meet actual A1 speech/CPU/memory/latency targets; optional learning/verifier is not required. |

### R03/R09 bounded native tray recovery - 2026-10-07

Delivered on baseline `d0a8e82ef34b82c4d888803083050c2e9dff43cd` in isolated
`agents/bounded-microphone-recovery-slice`: non-sensitive truthful input/PTT
status; five-second single-flight metadata-only refresh; native saved-selection
marks and available/System/retained-unavailable endpoints; revision-bound
selection through existing audited preferences; explicit Enable/Disable
listening and existing Stop speaking/Settings recovery. Selection never opens
capture or grants consent. Timeout, stale revision, unknown/locked/disconnected
privacy, lost ownership and retired callbacks fail closed. A saving failure
retains the prior preference; no endpoint is silently substituted.

Existing tray click, Settings, Sessions, maintenance, documentation, skill
inspection, evidence and exit behavior is retained. See the
[delivered boundary and remaining native gates](Interaction_Fallback.md#delivered-bounded-r03r09-tray-recovery)
and [mouse workflow](../docs/windows-and-tray.md#microphone-and-listening-recovery).
Tests use deterministic clocks/catalogues and owned synthetic menu items,
without app launch, microphone capture or playback. This does not close R03,
R09, production wake, acoustic quality, permission/event latency, Explorer
restart/tray failure, overflow/accessibility or installed/native acceptance.

Experiment disposition: retain every executable and historical receipt in
`r02-speech-proof`. Its annotation-driven pre-roll/endpointing, licensed
synthetic audio, candidate benchmarks and acoustic gates are not equivalent
to metadata/PTT tray recovery. `benchmark.py`, `test_benchmark.py` and
`Validate.ps1` still consume `capture_probe.py`; canonical dependency/deferred
validation documents still refer to this proof. No path is specifically
superseded by equivalent maintained checks plus consumer/reference verification,
so no removal is justified. This slice reruns none of its audio/model trials.

Validation receipt on the stated baseline: root Release/no-restore build
passed with zero warnings/errors after the new worktree's missing assets
were restored in locked mode through the machine-required feed. Core **520**,
Application **1,413**, Tools **38**, Definitions **6** and authorized Windows
fixtures **751** passed (**2,728** total). Local Windows validation excluded
`WindowsVoiceRecognitionServiceTests` and `WindowsTextToSpeechServiceTests`
to avoid installed endpoint/voice and hardware-dependent trials; owned
activated-capture and native-menu fixtures remain included. The latest
per-suite coverage reports, merged with the repository tool and enforced by
`Assert-CodeCoverage.ps1`, meet **100% portable line and branch coverage**.
Earlier iterative reports are not validation evidence for the final build.

PR #71 merge follow-up: after the first complete push/PR CI run passed, main
advanced through session-metadata #70. Rebase onto
`0b667e91746e94c8157bc9ae90faf57be3b6d3b9` was clean; no sibling branch was
merged. Fresh combined root Release/no-restore and coverage validation passed:
Core **530**, Application **1,415**, Tools **38**, Definitions **6**, authorized
Windows fixtures **785** (**2,774** total), zero warnings/errors and **100%
portable line/branch coverage**. The same local hardware/voice exclusions
apply; this is not native acceptance or closure of R03/R09.

Main subsequently advanced through ordinary diagnostic retention #69 while
the rebased CI run passed. Rebase onto
`c0c15ac2a74a865bbd7540a7a0cf5c00c2d3a21b` was also clean. Fresh combined
validation passed Core **530**, Application **1,415**, Tools **38**,
Definitions **6**, authorized Windows fixtures **806** (**2,795** total),
root Release/no-restore with zero warnings/errors, and **100% portable
line/branch coverage**. No retention behavior was changed by this slice.

After installed speech choices #72 merged, rebase onto
`640f28b2f91c106f2d84cb62193beca9961ca739` preserved both adjacent Design README
delivery summaries; shared source auto-merged. Fresh combined validation passed
Core **539**, Application **1,469**, Tools **38**, Definitions **6** and
authorized Windows fixtures **807** (**2,859** total), root Release/no-restore
with zero warnings/errors, and **100% portable line/branch coverage**. Speech
provider/voice settings remain owned by that separate slice; tray recovery
adds no speech registry, provisioning or acoustic trial.

### R02 Local-Inference Continuation

The [three-tier policy](Local_Inference.md#three-tier-admission-policy) scopes
L1-L6 to local-inference enablement/advertising and RC manifests including it.
Unrelated feature work does not wait for this proof. The
[2026-10-08 preparation](../experiments/r02-local-inference-proof/results/continuation-2026-10-08/preparation.json)
passes current deterministic checks but finds no supported installed/listening
runtime or default pinned manifest; it supplies no live trial consent.

The [technical outcomes and qualification contract](Local_Inference.md) turn
the partial proof into the steps below. **First action: assign a test owner,
approve the floor/isolation environments and agree budgets (L1).** No trial
failed model quality; it was blocked by missing runtime/model and consent.
R01 is satisfied. Provisioning, independent network enforcement and actual
runtime/hardware evidence are not satisfied by the harness or its merge.

These are substeps of the existing local-inference branch, not new top-level
roadmap packages, model tools or requirements on unrelated R02 branches.
Owners are accountable roles until individuals are assigned.

| Step | State / owner | Depends on | Next action and completion receipt |
|---|---|---|---|
| R02-L1 - Approve environments and budgets | Outstanding; product and test leads with runtime lead | R01; test-machine ownership and approval | Select supported Windows 11 x64, named reference CPU/8-logical-core/16-GiB/SSD floor and approved disposable isolation environment. Record power profile, UI/services/contention and CPU-only verification plan; agree numerical cold/warm latency, memory/CPU headroom and cancellation/recovery budgets before trials. Record operator/provisioning/isolation consent; a faster host or VM alone is not qualification. |
| R02-L2 - Provision and verify the pinned baseline | Blocked on consent/runtime; runtime and release leads, test owner | L1; separate asset acquisition/startup consent | Reuse or provision only the pinned Ollama/qwen baseline. Verify installed identities and licences/native notices; record download, expanded/peak staging storage and free-space requirements on runtime/model volumes. No changed production pins, automatic replacement or task-time download. Receipt: reproducible version/digest/licence/storage inventory. |
| R02-L3 - Run actual CPU-floor inference trials | Blocked; runtime and test leads with human quality reviewer | L2; L1 budgets | Run at least 30 cold + 30 paired warm synthetic trials and production-default answers; human-score fixture quality. Measure effective context/oversize handling, output/thinking bounds, process RAM/CPU, repeated completion/cancel races, actual timeout, server cessation and recovery. Retain raw outputs/counters, individual failures and p50/p95/max comparisons to agreed budgets; mocks do not count. |
| R02-L4 - Prove independently blocked egress | Blocked on approved isolation; test and security leads | L2; approved whole-environment remote-network block/capture from L1 | Repeat relevant successful answering, cancellation/recovery and missing/unhealthy trials under verified IPv4/IPv6/proxy/NAT denial with loopback retained. Retain approval, effective controls, interval/capture and process correlation covering Kora and Ollama/runner. Receipt: no remote payloads, no fallback and 100% critical policy/cancellation passes; loopback-only code is insufficient. Can run alongside L3 once its environment is ready. |
| R02-L5 - Record candidate and hardware disposition | Open; runtime lead for D-003, product/test leads for D-007, release/security review as applicable | L3 and L4; distribution review | Record selected/rejected candidate, tested compatibility/context/resource envelope, agreed budgets, supported hardware/OS, residual limitations and reconsideration triggers. Update decision/architecture/setup/acceptance documentation with evidence. A failed gate requests a consented alternative trial or explicit hardware/scope decision; never silent cloud substitution or floor revision. Candidate qualification does not accept A2. |
| R02-L6 - Apply and repeat evidence on the host | Pending integration; R06/R07/R08/R10 owners, R19 test lead | L5; each dependent package's existing prerequisites | R06 uses the qualified envelope/digest and unavailable states; R07 budgets immutable selected context and repeats offline quality/cancellation on the clipboard workflow; R08 implements/proves streaming and late-output handling; R10 supplies measured per-volume setup budgets and tested compatibility. R19 compiles actual host/UI/voice evidence. Receipt: applicable integrated conformance and A2 sign-off, not a synthetic harness result. |

The immediate disposition is to **retain the pinned candidate for L2-L4
trials**, not select a new model or claim CPU-floor support. L1-L5 are the
local-runtime qualification dependency for exposure; L6 is the handoff to
implementation and integrated acceptance. R03 and independent R02 branches can
continue while this branch is blocked. Draft proof/documentation review does
not change decision or capability acceptance status.

The [shared deferred-validation register](Deferred_Validation.md) and
[LI01-LI07 interactive checklist](../experiments/r02-local-inference-proof/README.md#outstanding-testing-checklist)
track each unperformed inference trial's prerequisites, approvals, instruments
and completion evidence alongside the independent speech/storage/containment/distribution
proofs. The harness/outcomes/handoff may merge as partial research; L1-L6 and
dependent capability acceptance remain outstanding. Do not hold publication of
that limited scope for live validation or inherit qualification from another
proof's measurements.

### Complete the Required Slice A Product

#### Tools and Definitions Structural Follow-Up - 2026-10-07

The six R06 implementations are separated into per-action C# classes in the
portable `Kora.Tools` capability folders, retaining the existing Application
gateway for host/caller/lane admission, strict JSON and complete output bounds,
cancellation/tracing and content-minimizing logs. IDs, schema/effect descriptors,
six-record/4 KiB limits, cached observations, unsupported inputs and unavailable
tool-loop qualification are unchanged. The portable version-observation
interface moves to Core to avoid a reverse Tools-to-Application dependency.
No model tool exposure or action authority is added.

`Kora.Definitions` is the single bundled-content project for skills and, when
actually implemented, prompt templates, shared instructions and agent profiles.
The existing skill catalogue and all 13 declared lock/shutdown/restart/helper
resources move here without changing logical IDs, resource bytes, manifests
or independent golden package/script-set/definition digests. Core retains
validation/digest policy; desktop retains the same native immutable inspection.
Agent profiles are documented as future bounded declarative task definitions,
not a loader, scheduler or permission system. No empty scaffolding or model
prompt/runtime changes are introduced.

Repository instructions, contributing guidance and the authoritative
[implementation convention](Commands_Tools_And_Skills.md#bundled-definitions-and-agent-profiles)
cover both project responsibilities and folder/namespace alignment.
Moved tests preserve original R06 hostile-input, bounds, caller/session,
cancellation/trace and R11 golden/resource/error/shared-helper proofs.
CI covers all four portable assemblies at unchanged 100% line/branch thresholds
and checks final x64/x86 `Kora.Definitions.dll` resource bytes. This is
structural organization, not closure of R06/R11 runtime/worker/agent gates.
No experiment evidence is retired or relabelled by this move.

#### R11 Bounded Embedded Package Catalogue and Native Review - 2026-10-07

The fixed lock, graceful shutdown and restart packages now have explicit
first-party embedded JSON manifests, Markdown, data-only fixtures, separate
entry scripts and one shared definition-only helper. The immutable catalogue
validates exact logical-name/resource-ID maps, strict UTF-8 and original-byte
size/count bounds; hashes use the canonical full-name ordinal framing.
Git preserves the resources without text conversion so their original BOMs and
line endings survive Windows/Linux checkout and publish unchanged.

The composed tray **Skill packages (inspection only)** entry shows every
declared file in read-only native source tabs, including manifest and shared
helper. It discloses unresolved dynamic adapter calls, best-effort transitive
gaps and future granting-user responsibility. Catalogue/inspection and read-only
declared-content comparison with an existing R05 exact record never create/use
grants, enable skills or expose unqualified model tools. Existing direct native
lock, legacy grant state and R06 caller lanes are unchanged.

Maintained tests migrate R02-W2's immutable-input, shared-helper change,
domain-separated framing, canonical golden vector, alias/duplicate, byte-limit
and concatenation-ambiguity cases, adding strict manifest/resource-map and
native exact-tab coverage. **No experiment executable is retired**: W2 still
has unique interpreter/helper-scope, native dependency, containment,
cancellation and truthful receipt evidence. W1-W4 remain blocked where their
separate network/deployment/real-control proofs are absent; historical receipts
and experiment provenance remain intact.

All package/action descriptors remain explicitly unavailable. No executor,
worker, broker, writable extraction, PATH/assembly override, grant consumption,
fabricated runtime identity or OS-control test is introduced. Full R11/R16
execution acceptance remains outstanding. Required CI additionally compares
all 13 final x64/x86 PE manifest-resource bytes with declared source bytes.

| ID and work package | Starting state | Priority/value | Needs | Completion condition |
|---|---|---|---|---|
| R10 - Implement typed configuration and capability-scoped setup | **Bounded native/exact configuration delivered**: nine appearance options, installed provider/voice pair, assistant display/PTT prefix, spoken-summary caps, exact microphone/output preferences, [per-Kora 0-100 volume](User_Configuration.md#delivered-bounded-per-kora-playback-volume-r10), [device-default Hybrid/VoiceOnly/VisualOnly](User_Configuration.md#delivered-bounded-device-default-response-mode-r10) and [future-only ordinary SQLite diagnostic days](User_Configuration.md#delivered-bounded-future-only-sqlite-diagnostic-retention-r10r04). Diagnostic integer1–365/default-reset30 preserves old deadlines, audit90/domain, files30/30 and all authority; apply-now and cleanup-trigger changes unavailable. Shared domain validation, revisions, atomic save/audit/readback, live invalidation and explicit recovery retain original-channel admission. Volume default/reset 100 is unity; zero prevents synthesis/autoplay with full visual recovery, and raising/resetting never replays. Mode delivery adds no session/task/queue override or fallback/call-policy change; mandatory visual previews remain. Full R10/I03/I04/I08/I11/I14 and native/acoustic acceptance remain open; [dependency inventory](Dependency_Catalogue.md) is not a complete executable catalogue or provisioning budget | P1 - consistent voice/UI controls without unsafe mutations | I: consumed R04/R05/R06/R09 contracts; E/Q: R02-L2/L5 for local-model setup claims, affected speech profile evidence only | Retain delivered descriptors and workflows; register remaining speech/input/output options, queue/deadline/other-retention/concurrency, runtime, grants/calls, appearance/startup and admitted extension settings. Broader scopes, general verbal/model settings, whole-profile reset/undo and safe recovery/conflicts remain open. Turn admitted dependency entries into versioned source/identity/verification/ownership/probe/consent/refusal records; experimental/unimplemented adapters stay unavailable. Distinguish transfer/model storage from expanded runtime/staging/per-volume headroom and use the tested compatibility envelope; the 2 GB model guard is not a total provisioning budget. Retain app-led dependency detection/installation with or without installer assistance, optional-provider refusal without repeated prompts/downloads or cloud fallback, host-owned installation/sign-in/secure workflows and device-local protected-call origin gates. |
| R11 - Deliver registered embedded multi-script skills and containment | **Bounded catalogue/identity/native review delivered**; runner unavailable; I17 partial proof is not admission; partial readiness/lock I08/I12 | P0 - finish lock without admitting arbitrary execution | I: consumed R03/R04/R05/R06/R10 contracts; E/Q: applicable R02 W1-W4/D-013 for worker exposure, not catalogue/review work | Embed lock manifest/instructions/fixtures, entry script and shared helper; verify `Kora.ScriptSet.v1`/`Kora.SkillDefinition.v1` complete framed identities and dependent-grant revocation. Direct/model/skill routes use the same pinned task exactly once with immutable source review. Complete W4: admit only fixed profiled workers with exact declared-resource review, best-effort transitive tracking/gap disclosure, protected required runtime/adapter admission, attributable network denial, bounded output/cancellation/Unknown receipts and real OS filesystem/child-process/credential/Kora-resource isolation. Prove observed lock outcome. Reject unsupported profiles or an unapproved broker substitution. Prepare power packages but do not enable OS power until R16. |
| R12 - Complete session lifecycle, history and per-session work/queues | **Bounded authority workspace, metadata, exact task controls, native logical disposition, [ordered interaction history](#r12r14-bounded-ordered-interaction-history---2026-10-09), fixed local-version queue and [session retention](#r12-bounded-session-retention-delivered---2026-10-09) delivered**. Schema v6 preserves v5 queue, v4 ordered history and v3 task/question/required-audit authority while adding a meaningful-activity clock, future-only 24-hour archive/30-day deletion, source revocation, live/uncertain holds and inventoried content/artifact/staging/journal removal. Independent Perpetual records and unrelated content survive; authority revocation alone is not deletion acceptance. Done is readable; Removed IDs allow only redacted citations. Full R12/A3 remains partial. | P1 - durable, inspectable long-running work | Consumed R04/R05/#102/#114/#116 and R10 preference subset; blocked R11 is not needed by delivered non-executing retention or fixed local-version queue, but remains required for general execution integration | Implement full conversation composer/bodies, search/model history reasoning, broader immutable artifacts and task lifecycle. Names never select authority; passive browse never resumes or extends activity. General effect queues/leases/cancellation remain outstanding. Future managed-backup formats require explicit ownership/inventory and acceptance; uninventoried copies hold. Preserve independent Perpetual records, actual copy-removal tests and no replay authority; no forensic erase or full deletion/RC acceptance is claimed. |
| R13 - Add bounded independent management and concurrent execution | Bounded exact controls plus [deterministic fixed local-version queues, revisioned atomic admission/receipts, FIFO/fair manual dispatch and no-replay recovery](#r13-deterministic-local-version-queue-increment---2026-10-09) delivered in schema v5. Host default one slot; limits 1–2 apply only to fixed synchronous local reads, not effect/provider qualification. Released-profile MG1 proof is not production management integration. | P1 - remain responsive while useful work runs | I: consumed R05/R06/R10/R12/#114 contracts; R08 only for model assistance; E/Q: local concurrency budgets only for local inference, MG1/management PV1 only for hosted assistance | Retain bounded exact deterministic core; complete broader contextual routing/status/choices, in-task wait/deadline and effect-specific cancel without hosted inference. Carry MG1's serialized 32 KiB input/4 KiB typed output, independent 15-second host deadline, one in-flight, rolling 30 attempts/hour/profile, fresh conversations and no forwarded retry into the host. Reject unknown targets/revisions and late output; Unknown needs observed receipts, not SDK acknowledgement. Add model assistance only after applicable runtime/account admission. Independently qualify real two-slot isolated task contexts/grants, resource leases, termination and reconciliation; fixed version tests and synthetic conversation counts are not provider/hardware evidence. No management task tools or approval authority; power follows R01. |
| R14 - Build the coordinated Sessions workspace and interaction surfaces | Bounded Sessions list/details/lifecycle, durable names, exact controls and native question review remain delivered. [Authoritative selected-session work](#r14-authoritative-selected-session-work-increment---2026-10-09) now coordinates bounded atomic work/queue/questions, shared-policy eligibility/deadlines/capacity/gaps and exact revision-bound native controls with passive focus-preserving refresh. [Ordered history](#r12r14-bounded-ordered-interaction-history---2026-10-09) and independent evidence remain separate sources. | P1 - make sessions, decisions and results understandable | Consumed R05/R12/#114/R13/#116 subsets; R09 and broader task contracts for remaining coordination | Full conversation composer/bodies, history search/model reasoning, general effect/provider work and immutable artifact/script integration remain unavailable. AuthorityAudit reads committed rows, not diagnostic lookalikes or forensic proof. Ask Evidence/model tools/export need separate admission. Browsing never retargets approvals, extends activity or creates replay; names never resolve authority. Native installed visual/screen-reader/DPI and full R14/A4 acceptance remain open. |
| R15 - Complete call-aware feedback, authorization and request-origin gates | Bounded manual mode and independent [device-local Voice/UI/Both/Inherit feedback](#r10r15-bounded-device-local-in-call-feedback---2026-10-08), shared native/typed/activated parity and truthful status, conservative speech/origin and legacy reuse checks delivered; full R15 partial, detector unavailable I14 | P0 - prevent call leakage and reusable-authority surprises | R03, R05, R09, R10, R13, R14 | Maintained deterministic/manual/durable tests cover mixed evidence, Unknown, feedback precedence, current configuration/call/session/input/name revisions, original input/native lifetime, required-audit/shared-lease/atomic failures, restart markers, pending previews, output/input retirement and reuse/dispatch/disposal races. Saved legacy suppression/activation remains independent; Unknown always withholds speech. New protection downgrades and exceptions remain unavailable pending complete exact review. Remaining: qualified real detectors/source freshness, broader voice/call/proactive registry, production exact effect dispatch/generation wiring and native/acoustic/call acceptance. No automatic detector, authority migration or A0-A4 completion is claimed. |
| R16 - Enable graceful protected power and all-session app controls | Partial proposals and current-app lifecycle I12/I13 | P0 - make disruptive actions safe and truthful | R01, R05, R11, R12, R13, R15 | Register/verify fixed shutdown/restart packages and helpers. Implement all-session impact review, fresh action-specific voice or equivalent UI confirmation, 30-second foreground prompt, two-minute single-use approval and 30-second cancellable host countdown. Perform mandatory real OS/provider checks; no extra UI click solely because risk is high, no forced close and no unrelated OS cancellation. Coordinate exit/restart and resource ownership; reconcile observed receipts rather than claiming success from a proposal. |
| R17 - Finish supported distribution, setup and startup behavior | Binary MSI/custom Burn, scoped logon/completion, release automation, managed-source build-only tooling, immutable source-tool distribution/channel resolution and bounded native notify-only release discovery implemented; source activation, upgrade limitations and installed/protection acceptance outstanding | P1 - users can install/run safely without a development checkout | I: delivered distribution direction and consumed R03/R09/R10/R11/R16 subsets; E: D01/D03 and offered profile proof; Q: installed final manifest scope only | Complete R17-D01/D02/D03 below without another standalone feasibility project. Retain the delivered binary/CI/version/prerequisite/optional-consent, exact-source-tool and native metadata slices; finish separately approved source activation, supported upgrades/recovery, protected deployment and per-release native/licence qualification. Risk-based installed trials must cover logon/removal, repair/uninstall/all-users/completion and runtime-only launch against exact hashes after applicable resource/privacy/worker gates exist. Current per-user installs do not establish independent protection. Existing win-x86 output is not x86 acceptance; certify each offered architecture. NSIS-only paths are retired; historical receipts remain, with no executable experiment dependency. No in-app updater/download/install authority is added; reviewed dependency setup remains available. |
| R18 - Implement trusted proactive and notify-only maintenance flows | **Partial delivered**: canonical metadata/status and native Settings/Tray Check/review/Open/per-run snooze, plus [exact native/typed/activated cached maintenance status/review/snooze parity](Distribution_And_Updates.md#delivered-bounded-r17r18-native-foundation). Commands bind immutable cached identity/revision/channel/age and admit only eligible existing current-run snooze; general proactive broker and unsolicited/targeted voice replies remain open | P1 - useful feedback without model-created prompts or updates | R05, R10, R13, R14, R15, R17 for remaining proactive integration | Retain the [bounded native foundation](#bounded-r17r18-canonical-maintenance-foundation): separate per-run network consent, numeric stable/beta ordering, bounds, ETag re-verification, age/staleness, six-hour+jitter/backoff and ownership/privacy/protected-call invalidation. Cached commands add no check/HTTP/browser/download/install or consent grant/renewal; Check/Open remain native-only. Production excludes drafts/prereleases; explicit Preview includes only published canonical beta releases; CI/default-branch outputs are not releases. Only a reviewed host-constructed canonical release page may open. Remote notes are not rendered. Models cannot trigger checks, invent availability, choose feeds/navigation or download/stage/activate updates. Complete separately the general trusted-event broker, shared question/voice targeting, deduplication, deferral/deadline/rejection/fatigue controls and native/acoustic acceptance. |
| R19 - Qualify manifest-scoped RCs and report Slice A milestones | Proof outstanding | P0 - prevent a bootstrap/demo being released as the designed product | Q: applicable enabled R07-R18 scope and shared/transitive controls; full A0-A4 claim requires its complete scope; not an I prerequisite | Review exact enabled-capability manifest and exclusions; qualify final bytes with applicable reference-machine, provider, offline/egress, resource/cancellation, containment and installer/lock/call/device evidence. All required critical fixtures pass. Report A0-A4 separately without claiming excluded outcomes; repeat affected earlier controls after integration. Resolve relevant release decisions before sign-off. Unrelated experiments and excluded capabilities are not RC passes or repository merge blockers. |

### Bounded R10 exact output-device preference

The [exact output preference slice](User_Configuration.md#delivered-bounded-exact-output-device-preference-r10)
adds working native and typed/ACTIVATED discovery/get/status/select/per-option
reset for the existing output override. Real persisted host audio-control
session/generation admission, original-channel call/privacy/input revisions,
host-held exact choices, bounded metadata, audited atomic writes and live output
invalidation are shared; selection never starts audio or grants other authority.
Missing/muted/open/playback failures retain full visual recovery and saved pins.
Independent name/input/provider/voice/summary behavior and unique experiments
remain maintained. Metadata/storage/binding fixtures are not native acoustic
acceptance or full R10/I/A completion; no historical receipts are rewritten.

### R02 Distribution Follow-Up and R17 Delivery

This is the actionable distribution branch of R02, not additional platform
scope or a requirement to finish unrelated R02 investigations. Windows remains
the only deployed runtime; keep Linux for build/test/cross-publish and release
work wherever feasible, with Windows WiX packaging and portable architecture
seams. The
[distribution outcomes](Distribution_And_Updates.md#r02-distribution-outcomes-and-direction)
are the canonical technical input; the experiment is reproducible supporting
evidence, not the production implementation.

The IDs below subdivide existing R02/R17 work and retain their dependencies;
they do not renumber or bypass the parent packages. Private packaging and
boundary investigations can proceed now. The owner approved unsigned
beta/stable POC publication with front-loaded/ad-hoc/risk-based installed
validation, not exhaustive manual trials for every MSI. Production R17
acceptance still needs the parent prerequisites, including later real resources
and workers. A blocked trial is pending work, not completed evidence.

| Step / current state | Owner | Needs / next action | Completion condition and consequence |
|---|---|---|---|
| R02-D01 - Clear redistribution and declare launch inputs; repository controls delivered, release review open | Product owner and release engineering lead | Apply PolyForm Shield 1.0.0, the reviewed NuGet gate/version-specific overrides and notices. Audit actual native/font/text/model assets and the selected WiX build-tool terms separately. Use runtimeconfig/import inventory to declare launch requirements apart from optional capability assets. | Per-release redistribution/notices and supported runtime/native policy are reviewed. Binary delivery requires supported .NET 10 x64 Desktop Runtime and the observed VC++ x64 prerequisites, not Git/SDK. A passed NuGet gate or successful build does not clear every external asset. |
| R02-D02 - Select Windows installer direction; selected | Release engineering lead | Adopt WiX MSI + Burn, preserve historical NSIS evidence and hand off reviewed/pinned tooling plus a bounded Windows packaging job to R17. No separate WiX feasibility project or mandatory native Linux NSIS trial. | The direction is selected, not installed acceptance. Linux remains preferred wherever feasible; WiX packaging/lifecycle/protection must pass R17-D01/D02/D03. Retiring the old Linux NSIS gate does not waive Windows/runtime/resource gates or introduce Wine. |
| R02-D03 - Establish the independent Windows deployment boundary; blocked approval | Release and security engineering leads, Windows lab owner | Allocate an approved disposable Windows 11 x64 lab. Validate deployment parent/version protection, independent activation authority, actual non-elevated app token, protected runtime/native load roots and source identities; coordinate worker identity requirements with containment. | Actual identity/effective-access/link/replacement tests prove the boundary or leave affected capabilities disabled. An ACL request or user-writable staging is not protection. Missing workers are recorded as untested and must pass their later gates; do not modify an existing user installation to obtain evidence. |
| R17-D01 - Implement WiX source and binary delivery; binary, maintained source build-only and immutable source-tool distribution/channel-resolution slices implemented, activation/installed acceptance open | Release and application engineering leads | Maintain WiX 7 MSI/custom Avalonia Burn behavior. External source interface v1.1.0 previews and explicitly trusted-builds an exact canonical detached revision with pinned/locked restore, owned versioned staging, maintained native/resource/hash/receipt and bounded static-child verification, non-destructive retries and no-build reruns. Its complete eight-tool closure is packaged from exact Git blobs with sizes/digests/provenance. The maintained static resolver defaults to published stable production releases; preview explicitly admits published prereleases, never drafts. Acquisition verifies final asset ID/bytes, exact canonical commit/tree, tag and manifest before safe closed-path extraction; unchanged reruns are read-only, hostile/changed/partial output is retained/refused. Acquired builds bind to that same revision and still require explicit trust. Separately approve protected activation/installation/data-retention recovery. Silent old-BA related-bundle upgrades and same-numeric-version beta ordering remain unsupported; retain external uninstall/reinstall guidance. | Metadata preview and acquisition never execute downloaded code; checksums/Git identity are not independent signatures or production acceptance. Build-only output reports activation unavailable, never installs/launches/registers/elevates. Actual protection/unprivileged launch and lifecycle/runtime trials remain required, preserving local edits/earlier output/user data. Binary users never build/restore; local-source provenance grants no activation or official publication authority. Retired distribution proofs have no executable consumers; historical receipts remain unchanged and no further retirement is needed. Re-probe optional setup without removing unowned dependencies; startup/completion remain separate from ownership/voice/storage admission. |
| R17-D02 - Integrate Linux-first builds and Windows WiX packaging; source-tool packaging and draft-ID correctness implemented, protected publication and qualification require actual receipts | Release engineering lead | Shared GitVersion resolves feature 0.1.0, main betaN and stable-tag versions. Configured Linux build/test/cross-publish transfers exact x64 payloads to Windows WiX packaging and the complete immutable source-tool ZIP to the existing publisher; canonical main/tag releases include notes/hashes/manifests, serialized non-overwriting publication and early already-published checks. The [draft/tag/retry contract and verification limits](Distribution_And_Updates.md#draft-tag-and-retry-publication-contract) retain a validated supported-API creation ID with authenticated exact source/channel/body readback and ID-addressed uploads, even when an immediate draft listing is missing/stale; visible conflicts and uncertain writes still fail closed, without blind retries. Complete exact final assets are verified before creating the exact-SHA tag. Feature/PR setup is built but not published and has no release-write identity. Both approved RIDs retain maintained native/runtime/import/resource inspection/contracts in portable CI; source ownership/recovery and hostile static source-tool contracts are configured. Require full non-skipped ICE, licence/coverage/tests and per-release native/tool/notice review; resolve explicit x86 ONNX native absence before claiming that capability. | Passing real Actions receipts and final hashes must identify one exact revision; configured automation/fake CLI/process-exit tests are not actual publication. Main run 37547030794 at ba1dc3d passed build/tests/full-ICE packaging but failed draft rediscovery, leaving empty beta47 draft 405222742; beta45/beta46 empty drafts are also historical partial states, not published releases or permission to delete/edit/publish them. The ID-readback fix must demonstrate a successful new exact-source main publication, not merely merge or synthetic tests. Local full ICE remains blocked by WIX1105 without bypass/elevation. Matching published versions remain no-ops; mismatches/uncertainty fail visibly. POC notes disclose unsigned status, architecture/upgrade limitations and remaining installed/native/durable gates. Production protection/resources/SBOM/native qualification remain separate; source revision is provenance, not host authority. |
| R17-D03 - Accept installed final bytes on Windows; blocked later integration/lab | Windows test lead with release and security leads | Use approved R17-D01/D02 candidates after applicable R03/R09/R10/R11/R16 and reference-environment preparation. Risk-based trials cover runtime-only launch, wrong/missing prerequisites, UAC/SmartScreen, effective protection/native loading, privacy/resources/workers, scope/logon/repair/uninstall/completion and external replacement recovery. Do not mutate a user's installation to obtain evidence. | Attach actual receipts to final setup/payload hashes and observed runtime/native identities. Only passing required installed/resource/worker gates signs off D-005/R19 production acceptance. Approved public unsigned POC releases do not close those gates and do not require exhaustive manual installation of every MSI; R18 remains notify-only. |

Immediate handoff: product/release owners complete D01's release-specific
review; release engineering maintains the implemented binary/CI slice and
maintains the source fixture/stage/static-acquisition gates and nine-asset
non-overwriting publication, and completes separately approved activation/upgrades;
release/security and the lab owner arrange D03
approval. Production design can proceed in parallel, but those assignments
and the later integrated Windows evidence cannot be replaced by passing
fixture tests, configured publication or merging installer source.

### R17-D02 Publication Correctness Local Receipt

The independent uncommitted repair is based on current main
`c5dffabf8f4fa767147be06dd8b296238ea97da0`, not the stale orchestration
checkout or completed sibling branches. Publication-owned code/tests and the
canonical publication sections only are changed; source/native migration,
storage/authorization and passive viewer work remain separately owned.

| Gate | Local outcome and limit |
|---|---|
| Root Release build | Passed, zero warnings/errors after locked restore |
| Core / Application / Windows suites | 338 / 1,049 / 495 passed; zero skipped/failed |
| Latest-only portable coverage | 6,246/6,246 lines; 2,685/2,685 branches; 742/742 methods, all 100%; only the two fresh reports were merged |
| Version / payload rejection / licence gates | Passed; no threshold, notice-policy or receipt weakening |
| Release lifecycle and Actions exit policy | 265 assertions, 30 child-process cases and 15 partial-write/abrupt-exit retries pass; fake CLI only. Missing tag/release, paginated draft-only state, source/channel mismatches, exact final-byte/provenance/checksum conflicts, immutable matching no-op and failed API/upload reconciliation are covered |
| Real static application payloads | x64/x86 publishes and exact payload/licence checks pass for 201/197 files; no app launch, installer execution or native-loading acceptance implied |
| Full local packaging | Attempted with full ICE; WIX1105 system-policy block. No suppression, elevation or machine-policy change; Burn/package inspection not reached |
| Real protected publication | Not performed or claimed. Read-only latest main evidence is publication FAILURE with an eight-asset, exact-SHA beta41 draft and no tag |

Handoff remains uncommitted for user review. A later explicitly authorized
protected-main run must prove the new exact-SHA tag, final asset digests and
successful publication. Local fake-CLI messages and earlier main packaging
receipts do not close that gate or installed/native/durable acceptance.

### Read-Only Integration and Declarative Authoring

| ID and work package | Starting state | Priority/value | Needs | Completion condition |
|---|---|---|---|---|
| R20 - Deliver one admitted read-only MCP integration and bundled integration skill | Outstanding; connector/task choice still open | P2 - first safe external-source value | I: consumed R06/R10/R11 contracts; E: actual connector/profile proof; Q: R19 for RC inclusion, not implementation | Select one supported tool/task; implement host-owned setup/sign-in/credential references, connection lifecycle, capability discovery and exact identity/policy mapping. Pin and enable the bundled read-only skill; enforce bounded provenance, fresh source permission and per-result model egress. Pass a controllable MCP-server suite plus the same tests on the real connector. No arbitrary installation, writes or generic call-anything escape hatch. |
| R21 - Add explicit shared-source skill discovery and enablement | **Bounded read-only registration/list/immutable inspection/recheck delivered**; enable/disable/invoke/model exposure and authoring unavailable | P2 - reuse existing skills safely | I: consumed R05/R06/R10/R11 contracts; E: source/skill profile proof; Q: R19 for RC inclusion, not discovery implementation | Explicit native registration grants only a bounded local read. Known-folder/handle identity, strict versioned YAML/UTF-8, count/depth/byte limits and incompatible/unavailable disclosure are implemented and tested; no ambient personal-skill model ingestion. Remaining separately confirmed enable/disable/invoke and Kora-specific fork/authoring work stays gated. Shared roots are never written or promoted to bundled trust. This slice does not close R20/R21/R23 or full Slice B. |
| R22 - Deliver voice/UI declarative skill authoring | Outstanding | P2 - create useful workflows without executable imports | R05, R10, R12, R14, R20, R21 | Implement Builder clarification/shared draft, exact diff/capability summary, schema/dependency checks and data-only simulated examples. Stage/save/restore/delete only owned revisions with exact confirmation; save to the Kora user store and confirm enablement separately. Add bounded skill-revision file selection/read, not general filesystem access. Invoke only admitted tools under normal grants/egress; save/tests/enablement confer no execution permission. No compilation, external test/build commands, Git writes or application-code modification. |
| R23 - Accept Slice B/C and manifest-scoped release regression | Proof outstanding | P0 - integration/authoring must not weaken the core | Q: R19 and enabled R20/R21/R22 scope; complete B/C claims require their full scope | Record applicable connector/account/access-revocation, hostile-source, source-revision, draft/save/enable and simulated-test results. Repeat affected A0-A4/privacy/cancellation/egress/grant tests and update capability/reference/user documentation to exact delivered availability and explicit exclusions. The full initial A/B/C scope is complete only after its gates, not after a catalogue or authoring UI exists; partial manifest-scoped releases must not claim that outcome. |

## Optional and Deferred Work

These packages are ordered after core value by priority, not added to the mandatory initial-release critical path.
An optional branch can proceed once its stated dependencies pass, without requiring unrelated later packages, but must not displace unresolved core safety work.
Do not advertise any deferred capability solely because an interface/schema is documented.

| ID and work package | State/priority | Needs | Separate delivery gate |
|---|---|---|---|
| R24 - Local frequent-speaker learning and enrolled verification | Optional; P3 | R03, R04, R05, R09, R10, R15 | Separate learning consent and verifier enrollment; protected per-SID/device storage, minimization/reset/delete, drift/playback/predominant-speaker tests and verifier FAR/FRR/anti-spoof/secure-OS proof. Learning is personalization, never identity/authority; missing either never blocks baseline voice. Close D-006 only for the advertised capability. |
| R25 - Optional speech captions and richer browser/static HTML/diagram results | Bounded disabled-by-default local current-utterance captions plus run-only pinning, primary-screen corner placement and 0-30-second normal-completion delay delivered; sentence alignment/richer rendering/broader UX remain optional/separately gated P3 | R05, R08, R09, R14, R15 for remaining integration/proof, not blanket implementation prerequisites | Exact native/typed/activated local discovery/get/set/reset, actual matching host-admitted playback identity/generation/segment and immediate stop/cancel/response/privacy/call/ownership retirement even when pinned. Normal completion retains only observed text labelled previous speech; default delay 5 seconds, unpin preserves original deadline. Atomic typed placement/delay preferences never enable captions. No caption content persistence/logging/model egress or speech/capture/authority changes. Sentence alignment remains unavailable without admitted sentence boundaries; display selection/arbitrary placement and broader natural caption/viewer commands remain separate. Rich viewers still require immutable content, renderer isolation, disabled bridges/active content, finite approved assets/navigation and resource bounds. R10 acoustic proof and installed/native accessibility acceptance remain open; neither is claimed or retired by this slice. |
| R26 - File/folder/screen/image context and knowledge retrieval/indexing | R26.1a local inspection and R26.1b selected immutable revision lexical retrieval delivered; broader stages deferred/P3; [exact retrieval boundary](File_And_Folder_Ingestion.md#delivered-selected-revision-lexical-retrieval); [provider/memory/knowledge direction](Model_Providers_Memory_And_Knowledge.md) and [file/folder staged plan](File_And_Folder_Ingestion.md#r26-file-and-folder-ingestion-delivery-plan) remain specified | R03, R04, R05, R06, R07, R08, R10, R12, R14; connector-backed retrieval also R20 | Native picker + metadata-only review + exact confirmation admit one immutable volatile strict-UTF-8 text/Markdown preview (256 KiB), source/revision/item identity and original-byte digest. Host-only deterministic bounded lexical scan/native exact citations revalidate original session/task/privacy/ownership/generation and required terminal audit. Fixed-drive canonical verified handles still deny reparse/hard-link/protected/generated/source-control/unstable paths. No folder, durable attachment/registry, refresh, persistent/vector index, model/egress, clipboard or execution authority. Beyond this bounded foundation, deliver reviewed immutable UTF-8 text/Markdown file/folder source revisions, broader scoped lexical retrieval/citations, qualified local reasoning and separately admitted hosted egress. Admit later formats, OCR/vision and hybrid/vector indexing independently. Preserve explicit source/session/destination scope, Windows reparse/access controls, provenance, context budgets, refresh/revocation/deletion and bounded citations. No ambient collection, direct view-model path reads, whole-file prompt stuffing, blanket enterprise cache, model-chosen arbitrary paths or silent context reuse. All experiments retained; inference/storage/runtime proofs are not exactly superseded. Installed native/accessibility acceptance and broader R26.1–5 gates remain outstanding. |
| R27 - General executable imports and standalone application execution | Deferred; P3 | I: consumed R01/R05/R10/R11/R12/R13/R21 contracts; E/Q: applicable R02 execution profiles only | Resolve standalone-binary rollback policy; prove complete dependency discovery and immutable folder snapshots, registered execution profiles, real OS containment and content-bound applicability/revocation. Do not extend fixed bundled scripts into arbitrary shell strings or user-supplied executable authority. |
| R28 - Write-capable connectors, repository/Git or broader desktop automation | Deferred; P3 | R05, R08, R12, R13, R20, R26 | Add explicit versioned tools and per-domain policy/resource/identity/recovery proofs. Revalidate external changes and uncertain writes; no self-modification, model-selected executable handlers or silent automatic write retries. Declarative authoring is not authorization for these capabilities. |
| R29 - Kora MCP server, install-capable updates, custom executable/render extensions or intra-session parallel agents | Deferred; P3; distinct proposals, not one combined release | I: consumed R05/R08/R11/R13/R17 contracts per proposal; E: applicable R02/new profile proof; Q: R23 only for included RC scope | Require a recorded scope/decision and dedicated proofs per proposal: authenticated per-client scopes, future trusted signed update roots/activation path, extension identity/containment, renderer isolation or isolated subtask budgets/leases. Initial notify-only maintenance, fixed renderers and one-task-per-session remain unchanged until that proposal is accepted. |
| R30 - Optional database encryption at rest | Future enhancement; P3; revisit when a suitable supported provider becomes available | R04, R12, R17; maintained, redistributable Windows encryption provider/package | Evaluate packaged authenticated SQLite encryption when a maintained, compatible and licence-acceptable distribution avoids a Kora-owned native build. Before offering opt-in encryption, verify migration/source preservation, OS-protected key custody and key-loss recovery, journals/backups and interrupted operations. Keep private-profile standard SQLite supported; this enhancement does not block R04 or the initial release, select a provider now, authorize a commercial dependency or restore superseded encryption admission gates. |

## Acceptance Checkpoint Mapping

Implementing a foundation earlier does not accept its eventual milestone.
Full Slice A checkpoint sign-off remains A0, then A1, then A2, then A3, then
A4; later testing repeats applicable earlier controls. This orders full-milestone
claims, not feature merges or RCs with explicitly excluded unfinished outcomes.
R19/R23 qualify only the reviewed manifest's included scope and dependencies.

| Checkpoint | Current assessment | Roadmap evidence required to close it |
|---|---|---|
| Platform/Gate 0 | Not accepted. Shared projects/CI partial; I18 Node/source-built RT1 retained with hook-only FAIL and historical released-byte parity blocker. Separately approved released-profile RT1 regressions/MG1 pass; no source/released byte equivalence inferred. RT2 all-path admission and PV1 remain open. | Preserve distinct exact profiles or repeat for changed bytes; applicable R02-RT2/R02-PV1, then R03/R04/R05/R08/R11/R17 actual integration/OS/provider/deployment evidence; carry scoped MG1 into production only for admitted model-assisted management |
| A0 deterministic shell | Partial shell/setup/transcription/TTS with implemented held PTT and cross-build ownership plus bounded native trials; clipboard, controlled remote path, streaming and complete native privacy/permission-polling acceptance outstanding | R03-R08 foundation/vertical-slice evidence; no dependence on model-assisted management; native setup/recovery and cancellation measured |
| A1 voice-first activation | Grammar proof, not production wake or complete lock/event policy | R02/R03/R09 actual wake, endpointing, privacy and interruption trials |
| A2 local-first answering | Partial: R02 identity/licence/unavailable-path evidence plus actual production Ollama setup, simple/long answers and cancellation. Full clipboard/streaming, CPU-floor quality/resource/context and independently blocked offline qualification remain open. | R02-L1-L5 candidate/floor disposition and L6 handoff; R06/R07/R08 actual local adapter/clipboard/streaming conformance and independently network-blocked host trials. D-003 and applicable D-007 evidence remain open until owner-reviewed passes; bounded host observations, harness tests and merge do not close them |
| A3 sessions/work/controls | Bootstrap ledger, named approvals and lock/proposals only | R04/R05/R06/R10/R11/R12/R13/R16 durability, grants, registry, management/concurrency and protected-control evidence |
| A4 workspace/interaction | Compact/native windows and output preferences partial | R09/R14/R15/R18 shared interaction, workspace/history/detail, call and proactive/maintenance evidence; optional R24/R25 only if advertised |
| Complete Slice A | Not accepted | R19 compiles the preceding sign-offs and required installed-app evidence |
| B read-only integration/skills | Outstanding | R20/R21/R23 evidence; controllable server tests alone do not accept a real connector |
| C declarative authoring | Outstanding | R22/R23 evidence; save, enable and execution remain separate decisions |

## Capability Coverage and Keeping the Roadmap Current

The [canonical catalogue](Internal_Model_Tools.md) remains the owner of tool IDs and lanes; this roadmap introduces none.
Track each admitted descriptor and its tests against the package that implements it:

| Capability family | Primary work packages |
|---|---|
| Discovery/application/readiness/runtime; deterministic host-only setup/recovery | R03, R06, R10, R16, R17 |
| Session lifecycle/history/search/artifacts, cross-source evidence query and grounded work/status/queue/routing | R04, R06, R12, R13, R14 |
| Structured questions/presentation/details/navigation/speech, approvals/grants and receipt/audit evidence/export | R04, R05, R09, R14, R15; optional R24/R25 |
| Typed configuration, call feedback/origin/reusable-grant controls | R10, R15 |
| Delivered nine-option typed appearance inventory: direct UI/exact local list/get/set/per-option reset, domain bounds/defaults, revision/proposal provenance and audit/live notifications; no Tools catalogue or model authority | R10; [bounded receipt](#r10-bounded-appearance-registry---2026-10-07) |
| Delivered installed speech provider/voice schema: shared native/exact local discovery/get/set/reset, coherent atomic provider/voice selection, desired/effective/default/recovery, owned revisions and protected-call original-channel revalidation; no provisioning/model/call-override authority | R10; [bounded speech receipt](#r10-bounded-installed-speech-choices---2026-10-07) |
| Explicit context and destination egress | R07, R08; deferred R26 |
| Bundled skills/execution/computer controls | R11, R16; deferred R27/R28 |
| Dual file/database logging, dedicated audit table and storage recovery | R04 |
| Optional database encryption at rest, when a suitable provider becomes available | R30 |
| Evidence/diagnostic viewers | R14; optional R25 |
| Host-only maintenance/proactive events and read-only maintenance snapshots | R17, R18; deferred update authority in R29 |
| Connector discovery/configuration/tools, shared skills and declarative authoring | R20, R21, R22 |
| Voice-profile workflows, knowledge and separately gated future integrations | R24, R26, R28, R29 |

For each delivery, update this inventory, the [decision register](Decision_Register.md), the [technical reference](Tool_And_Skill_Reference.md), the [user guide](../docs/tools-and-built-in-skills.md) and actual advertised schemas together.
Record source/test/provider/hardware evidence and the exact acceptance result, not just a merged PR.
Remove proposed labels only for the admitted behavior actually delivered; preserve unavailable/unknown states and the distinction between current host equivalents and model tools.
Keep dependencies explicit and re-run affected earlier gates when a later capability changes shared authority, storage, audio, egress or resource coordination.
## R10 Bounded Device-Default Response Mode - 2026-10-08

`responses.default-mode` exposes only the existing Hybrid/VoiceOnly/VisualOnly
device default. [Canonical configuration](User_Configuration.md#delivered-bounded-device-default-response-mode-r10)
and embedded Settings/voice/commands describe native Inspect/Save/Reset and exact
typed/activated discovery/get/status/set/reset. One workflow reuses genuine
original-user audio intent, persisted active session/generation, host-held
revisioned choices, current owner/privacy/call/input admission, typed audit,
atomic legacy preference save/readback and live notification. The consolidated
#83 session connection/lease is consumed once without reentrant acquisition.
Default/reset semantics, task/queue precedence, independent call/privacy
suppression and required full visual warning/security/question/approval recovery
are retained. Mutation invalidates stale output but never plays/replays speech,
opens capture, answers a question or changes grants/consent/global settings.

Maintained [grammar](../tests/Kora.Application.UnitTests/Configuration/ResponseModeCommandTests.cs),
[workflow](../tests/Kora.Application.UnitTests/Configuration/ResponseModeConfigurationServiceTests.cs),
[native/typed/activated presentation](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.ResponseModeConfiguration.cs),
[real migrated SQLite/atomic preference](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteResponseModeConfigurationTests.cs)
and [static native bindings](../tests/Kora.Windows.IntegrationTests/ResponseModeConfigurationUiContractTests.cs)
cover bounds, provenance/restart, corrupt/readback/audit/receipt failures,
foreign/stale/session/generation/origin/call/ownership races, cancellation,
disposal, exact pending previews and no autoplay/global or microphone mutation.
These fake/private-storage/source contracts do not claim live native/audio
acceptance or completion of full R10.

Experiment assessment: repository-local search found no response-mode enum,
preference file or VoiceOnly/VisualOnly executable in `experiments`. No
specifically superseded maintained equivalent with no consumers was identified.
The speech experiment retains unique acoustic/asset/capture trials and blocked
packaged-host acceptance; runtime executables still have maintained `eng`
consumers. Hardware/runtime/worker/containment/storage executables and historical
receipts remain unchanged. Nothing is retired or rerun by this slice.

Initial validation on merged `363ed6e` passed zero-warning Release, Core **675**,
Application **1,884**, Tools **38**, Definitions **6**, Windows **935**, no skips,
and fresh exact **100% line/branch** portable coverage with unchanged exclusions/
thresholds. An earlier existing task-store post-commit cancellation test failed
once, passed isolated recheck, and passed the fresh full Windows run; no control,
test or exclusion was disabled. No live Kora/audio/OS/install trial was performed.

After #84 merged as `b34c077`, immediate rebase preserved volume's shared audio
admission, unity default/reset, zero/full-visual recovery, native controls,
grammar/dispatcher and stale-output/no-replay behavior. A combined parity test
proves response-mode set/reset cannot alter zero volume or replay when unity is
restored. A separate atomic unconfirmed-mode marker now retains failed apply
evidence across restart without changing the legacy mode or mute-fallback files.
Fresh combined Release passed zero warnings/errors: Core **700**, Application
**1,974**, Tools **38**, Definitions **6**, Windows **954**, no skips, exact
**100% line / branch** portable coverage and unchanged exclusions/thresholds.

## R10 Bounded Assistant Display/PTT Prefix - 2026-10-08

The already delivered assistant name now has schema-1 typed
`assistant.name` discovery/get/set/reset and native Apply/reset parity through
one audited atomic host workflow. It reuses existing name validation and
legacy preference bytes, command-collision validation and atomic preference
storage. Discovery reports bounds/default/type/scope/effect/timing/reset,
process-local revision, saved/default provenance and explicit recovery.
Original host/channel/call and configuration revisions, confirmed capture
quiescence and terminal audit precede publication. Name mutation retires
captured/queued grammar/transcript/completion generations without reopening
capture, clearing a run hold or replay. The next explicit PTT uses only the
committed prefix, including merged exact session and artifact command routes.
Native surfaces/help remain consistent; stop/cancel/recovery and exact pending
question/approval targets remain independent.

Deterministic portable and native-fake regressions cover legacy at/over bounds,
Unicode semantics, invalid/corrupt state, storage/audit/cancellation failures,
host/call/revision races, stale generations, per-option reset and no authority
identity changes. No live audio, application launch, OS effect, install,
elevation, account or security/network-policy trial is part of this receipt.
Full R10 and R09/acoustic/packaged-host acceptance remain open.
See [the exact contract](User_Configuration.md#delivered-bounded-assistant-displayptt-prefix-r10)
and [wake distinction](Activation_Name.md).

Direct synchronous validation on the combined #77 base
`90146f405ea9236a23ee2a95cd159efcd5fbf84f`:
Release no-restore build, zero warnings/errors; Core **610**, Application
**1,578**, Tools **38**, Definitions **6**, Windows deterministic integration
**865**, all passed with zero skips. The native activated-capture subset
contains **51** passing tests and uses synthetic capture/privacy fixtures,
not live devices. Merged portable coverage is exactly **9,536/9,536 lines**
and **5,007/5,007 branches** (both unrounded **100%**); the CI threshold and
coverage inclusion rules are unchanged. Initial no-restore validation found
missing assets; locked restore used the explicitly approved feed only.
No feed configuration, secrets or dependency manifest changed.

Rebased immediately onto merged current-status documentation #76
(`632f416f0b371dcf0fc7652edc32fcfbeb8db3ff`) while #78 CI ran.
The combined Release build and all **1,578** Application tests were rerun.
The admitted-input/speech-stop race fixture now explicitly awaits the preview
command leaving its busy scope before requesting mutation, while the transcript
remains blocked on its separate stop gate; no timing-dependent admission is
assumed. The bounded delivered-name entry above supplements, rather than
overwrites, #76's dated source-review snapshot.

Final combined rebase also preserves merged spoken-summary caps #75 at
`cd99241516b782525732fc2a4627af5b6af54f95`. Release no-restore build remains
zero warnings/errors. All five suites were rerun directly: Core **648**,
Application **1,623**, Tools **38**, Definitions **6**, Windows **865**
(**3,180** total, zero failures/skips). Fresh combined portable coverage is
exactly **9,702/9,702 lines** and **5,190/5,190 branches**, both unrounded
**100%**, with no threshold/inclusion change. Prior counts above are dated
snapshots, not a claim that the separately merged speech-cap scope was absent.

**Experiment disposition:** retain all experiment executables and historical
receipts. The speech proof's keyword/acoustic, synthesis-to-file, candidate and
hardware measurements are not executable equivalents of typed preference or
PTT generation tests. This slice adds no production detector/assets/enrollment;
there is no specifically superseded name-configuration executable with verified
equivalence and no consumers to retire. Unrelated experiment paths are unchanged.

## R10 Bounded Appearance Registry - 2026-10-07

The [fixed descriptor registry](../src/Kora.Core/Configuration/AppearanceOptionRegistry.cs)
and [host service](../src/Kora.Application/Configuration/AppearanceConfigurationService.cs)
admit nine existing independent-file appearance preferences. Existing direct
controls and exact local typed/activated-voice list/get/set/reset share the
service, domain validation, revision check, one-file atomic persistence,
typed audit outcomes and live notifications. Get identifies saved/default
provenance; reset affects only one option. Particle playback scaling is
appearance-only, not audio/call policy.

[Maintained domain tests](../tests/Kora.Core.UnitTests/Configuration/AppearanceOptionRegistryTests.cs),
[service tests](../tests/Kora.Application.UnitTests/Configuration/AppearanceConfigurationServiceTests.cs),
[grammar tests](../tests/Kora.Application.UnitTests/Configuration/AppearanceCommandTests.cs)
and [actual presentation/command tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.Appearance.cs)
cover bounded discovery, validation/defaults, saved-format round trips,
stale/race/reload/provenance, cancellation before write versus after commit,
per-option reset, foreign proposals/origins, invalid persisted state, failure
recovery and parented truthful persistence activities.
All tests use owned paths/fakes; no Kora launch, live capture/playback or
existing user-preference mutation is acceptance evidence.

Shared response pin/topmost/position and presence placement remain direct UX
outside this registry. Full verbal preferences, model tools, temporary scopes,
whole-profile reset/undo, call/voice settings and capability-scoped setup remain
open R10 work; this receipt does not close R15, hardware/acoustic/privacy,
provider, installed-app or power-loss acceptance.

Experiment disposition: no executable appearance/preference registry cases
were found in the existing proof harnesses. No experiment is retired and no
unrelated evidence is relabelled as production acceptance. Independent
model/control/management/lifecycle, speech, containment, storage, dependency,
runtime and distribution proofs and historical receipts are retained.

Local validation: root Release build with zero warnings/errors; Core 367,
Application 1,142 and Windows 683 tests passed. Fresh merged portable coverage
is exactly 100% line and branch without exclusions or threshold changes.
Maintained version/publication/source-tool contract tests and both Windows
x64/x86 publishes passed without application launch. Remote CI additionally
owns dependency-license and publication gates; a local publish is not
installed-app acceptance.

## R10 Bounded Installed Speech Choices - 2026-10-07

Follow-on delivered slice: [bounded spoken summary caps](User_Configuration.md#delivered-bounded-spoken-summary-limits-r10)
adds schema-2 native/exact typed/ACTIVATED get/set/reset parity for independently
lowerable 1-3 sentence / 1-80 word device-local limits. The original speech
workflow owns atomic preferences, provenance, revisions, audits and host/call
gates. Complete ordinary output must fit both caps; owner-approved over-cap
refusal preserves full visual results/warnings instead of unsafe shortening or
extra inference. Exact readback/questions retain existing bounds. No full R10,
acoustic, OS-effect, install or live-network acceptance is claimed.

Local validation after immediate rebase onto merged tray recovery #71
(`cf0057bef2858998ea4e893cee7f4372780b7f25`), with prerequisite #72
(`640f28b2f91c106f2d84cb62193beca9961ca739`) verified in ancestry:
root no-restore Release build, zero warnings/errors; Core **577**,
Application **1,514**, Tools **38**, Definitions **6** and the hardware-free
native summary/installed-speech UI contract pass. Fresh same-build portable
reports merge to exact **100% line / 100% branch**, enforced with the unchanged
CI gate and no new exclusions. Missing initial assets were restored in locked
mode from the explicitly authorized feed, with no local feed settings committed.
Full remote Windows/portable CI remains the merge gate.

Before first push, daily JSON sibling #74 merged as
`e3280388a4f6c2ce2dd1e6a7adaf925a3e6f3c7d`. The owned branch was immediately
rebased without conflicts. Fresh combined Release validation passed with zero
warnings/errors: Core **585**, Application **1,515**, Tools **38**, Definitions
**6**, one static speech UI contract and exact **100% line/branch** portable
coverage. No sibling implementation was modified.

While #75 checks ran, session commands #73 merged as
`3bf1951ad6517cc2d3fa0b713e0a7fd23d4b18c8`. Immediate rebase retained both
session and summary documentation contracts; the sole conflict was their
section-filter/count assertion. Fresh combined validation again passed with
zero warnings/errors: Core **647**, Application **1,548**, Tools **38**,
Definitions **6**, the static native speech contract and exact **100% line /
branch** portable coverage. Only the owned summary branch is pushed with lease.

External native microphone-card #77 subsequently merged as
`90146f405ea9236a23ee2a95cd159efcd5fbf84f`. The owned summary branch was
immediately rebased without conflicts and freshly revalidated: zero-warning
Release build, Core **647**, Application **1,573**, Tools **38**, Definitions
**6**, one static native speech contract and exact **100% line/branch**
portable coverage. The card's behavior is unchanged by this slice.

Experiment supersession assessment for this slice: the per-path executable
speech harness (`prepare.py`, `fixtures.py`, `benchmark.py`, `capture_probe.py`,
`Render-Fixtures.ps1`, `Validate.ps1` and `test_benchmark.py`) proves asset/fixture/model/acoustic properties,
not summary counting or audited preferences. No equivalent summary-limit
executable or production consumer was identified. Maintained portable domain,
workflow, provider-fake output and static UI tests are new evidence, not
equivalence for those unique trials. Preserve all speech executables,
historical receipts and outstanding acceptance; no experiment is removed or
rerun. Other experiment paths do not implement this slice and remain unchanged.

Implemented in isolated `agents/bounded-r10-speech-provider-setup`, initially
fetched/rebased on `d0a8e82ef34b82c4d888803083050c2e9dff43cd` (#68).
Schema version 1 admits only delivered installed Windows SAPI/Kokoro
provider/voice choices. [Canonical configuration](User_Configuration.md#delivered-bounded-installed-speech-choices-r10)
and [native/exact user controls](../docs/settings.md#speech-provider) share one
host-owned workflow, coherent atomic selection file, process-local revisions,
desired/effective/saved/default state, explicit unavailable recovery, typed
request/terminal audit and live notifications. Legacy files are read without
rewrite and shadowed only by a successful explicit coherent save.

Provider set/reset selects its advertised default; provider reset restores
Windows. Voice reset affects the current provider. Explicit provider-qualified
voice selection supports installed voices even without a compatible default;
ambiguous/unknown/missing choices cannot become substitutions. Asset review
remains separate, and download completion no longer silently changes output.
The original channel and observed call revision are checked under the existing
policy lock adjacent to persistence. Privacy/ownership, System/pinned output,
mandatory visual fallback and unavailable protection downgrades are preserved.

Maintained [domain/schema tests](../tests/Kora.Core.UnitTests/Configuration/SpeechConfigurationTests.cs),
[workflow tests](../tests/Kora.Application.UnitTests/Configuration/SpeechConfigurationServiceTests.cs),
[grammar tests](../tests/Kora.Application.UnitTests/Configuration/SpeechCommandTests.cs),
[actual UI/typed/activated-voice tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.SpeechConfiguration.cs)
and [static native UI contracts](../tests/Kora.Windows.IntegrationTests/SpeechConfigurationUiContractTests.cs)
cover limits/invalid/ambiguous choices, cancellation, atomic persistence,
missing/invalid/unreadable saved state, audit failure, ownership/call/catalogue
races and live synchronization. Validation uses fakes/owned temporary paths;
no application launch, capture/playback, provisioning, elevation, account,
OS/policy or acoustic trials are acceptance evidence.

This completes only the bounded installed speech-choice registry slice.
Rate/volume/summary caps, microphone/tray recovery, diagnostics/metadata sibling
work, general registry/tools, consent changes, call overrides and full
R10/D-002/D-007/acoustic/installed-host acceptance remain outside it.

Experiment disposition: the existing
[speech proof](../experiments/r02-speech-proof/README.md) executables concern
model/fixture/capture/acoustic measurements and their reproducible historical
evidence, not provider/voice configuration equivalents. No maintained
configuration test supersedes those unique measurements or outstanding trials;
all executables and receipts are preserved. No experiment retirement is claimed.

Local validation on this baseline: root Release build, zero warnings/errors;
Core **529**, Application **1,443**, Tools **38** and Definitions **6** tests
passed, plus two hardware-free native UI contracts and the pure Windows-default
provider isolation test. Merged current-build portable coverage is exactly
**100% line and branch** (rates `1` / `1`), enforced with unchanged CI thresholds
and no new exclusions. Full remote Windows/portable CI remains the publication
gate; this receipt does not claim hardware trials or acoustic acceptance.

Before publication the branch was fetched/rebased onto merged session metadata
#70 at `0b667e91746e94c8157bc9ae90faf57be3b6d3b9`, without conflicts or unpublished
sibling imports. Fresh combined Release, Core **539**, Application **1,445**,
Tools **38**, Definitions **6**, the same three hardware-free native contracts,
and exact portable **100% line/branch** coverage passed. The initial-baseline
receipt above remains historical; session metadata and its independent proof
remain owned by #70.

## R12/R13 Bounded Exact-ID Session Entry Points - 2026-10-07

Extends merged #70's durable names/native Create/Rename foundation, not full
work routing or scheduler acceptance. One portable typed grammar/result contract
and the shared host workspace service now admit exact typed/activated-voice
help/list/status/inspect and explicit create/rename/Done/resume. Exact IDs,
generations and metadata revisions remain authority; names/window selection
never supply command or approval targets. Fresh original-user lineage and
durable control intent/receipt accompany every accepted command. Existing
private storage, host ownership/unlocked presentation, voice consent/origin/
call/recovery revisions, required audit and live-work/Unknown/question
blockers remain enforced. Protected-call voice mutations stay unavailable.
Errors, unsupported grammar and recovery are explicit; committed work is not
described as rolled back by later receipt failure.

Bounds: 1,024-byte whole UTF-8 input, domain-owned NFC names (120 scalars/
480 bytes), default 25/max 50 keyset records, 64 KiB complete structured JSON
results. Task/question pages are durable observations, not atomic runtime
progress. No transcript persistence, queue/executor/scheduler/cancellation
claims, inference/model exposure, deletion/retention, audio capture or
name-based inference is added. See [exact user commands](../docs/commands.md#bounded-exact-id-session-commands).

Experiment equivalence: the control/runtime proof tests SDK isolation and
authority boundaries; management proof tests provider byte/deadline/retry/
quarantine limits; storage scratch explicitly uses opaque feasibility records,
not the production session/task contracts. These are not executable equivalents
of this local exact grammar/native workspace slice. No experiment is retired;
unique and historical receipts and consumers remain intact.

Maintained validation on refreshed main `c0c15ac`: root no-restore Release
build, zero warnings/errors; Core 592, Application 1,423, Tools 38,
Definitions 6 and Windows 829 tests. Fresh latest-only portable reports meet
the unchanged exact 100% line/branch gate. Missing initial assets alone
required locked restore using the owner-specified per-command source; no
NuGet configuration or credentials changed. Tests use deterministic fakes/
owned SQLite/native fixtures, not app/audio/OS-effect/install/account trials.
Remote CI remains the source/publication/license/packaging gate.

After #72 merged, immediate rebase onto `640f28b` preserved both installed-speech
and session documentation/contracts, reconciling only the added section-count
test and appended receipt. Fresh combined Release and all five suites passed:
Core **601**, Application **1,477**, Tools **38**, Definitions **6**, Windows
**831**; latest-only portable coverage remained exact **100% line/branch**.
The earlier receipt remains historical, not current-base merge evidence.

Final entry-point correction keeps the typed deterministic session namespace
available while bootstrap work is busy, without cancellation or model dispatch.
Fresh Application **1,478**, three focused native-session tests, root Release
and latest-only exact **100% line/branch** coverage passed on the same base;
the full five-suite base qualification above remains valid.

After #71 merged, immediate rebase onto `cf0057b` retained the native recovery
boundaries. Qualification exposed an existing disposal-order race: cancelling
the awaited microphone refresh could release its caller before input was held.
With owner approval, disposal now closes lifecycle admission and holds input
before releasing refresh waiters; the maintained disposed-callback regression
passes without weakening its privacy assertions. Fresh root no-restore Release
build has zero warnings/errors; Core **601**, Application **1,502**, Tools
**38**, Definitions **6**, Windows **835** all pass. Latest-only portable
coverage meets the unchanged exact **100% line/branch** gate.

After #74 merged during checks, immediate rebase onto `e328038` required no
conflict or unpublished-source integration. Fresh combined root no-restore
Release build and all suites pass: Core **609**, Application **1,503**, Tools
**38**, Definitions **6**, Windows **863**, with zero build warnings/errors
and unchanged exact **100% line/branch** latest-only portable coverage.

## R12/R13 Bounded Authoritative Task Controls and Atomic Wait Cancellation

The owner explicitly approved the required tightly coupled task/question/audit
consolidation, not two-database pseudo-atomic cancellation. Existing task/event
IDs/order, session generations, questions, names, grants and exact audit
history are preserved in interaction schema v3. The complete legacy ledger is
validated and frozen before one destination schema transaction. The retired
source remains an inert required handoff receipt. Interrupted migration
revalidates and completes storage only; missing/corrupt authority, missing/
unfrozen handoff or mismatched task/question provenance is explicit recovery,
never partial copying, an empty default or automatic execution replay.

One existing grammar/workspace admits `task help`, exact task `status/inspect`
and explicit session/task/revision/generation/question-bound `cancel`.
Native fresh selection-bound Inspect and separate Cancel share that host
workflow with typed/activated voice, original-user durable control intent,
current ownership/privacy/channel/call revisions and complete output bounds.
Read/selection does not extend meaningful activity or route future input.
Only the admitted current-run native local-version question waits before
dispatch. Its exact answered-key gateway alone records dispatch. Task terminal
cancellation, question cancellation/revision, target observation revocation
and required trusted typed audit have one COMMIT. Grants, independent Perpetual,
unrelated work and the always-available stop/recovery path are untouched.
Stale/foreign/expired/prior-run/answered/dispatched/Unknown targets refuse;
late callbacks and generic coordinator writes cannot bypass the wait gateway.
No SDK acknowledgement or cancellation request claims physical termination.
Receipt uncertainty requires inspection, not rollback or retry.

Maintained hardware-free coverage includes portable grammar/UTF-8 complete
bounds, shared native/text/activated routes, protected-call cancellation,
late/disposed/private presentation, missing/stopped/foreign context, original
control restrictions, store/audit/gate/token failures, answer/cancel/admission
races, current-run/revision/source bindings and no replay. Actual private
SQLite migration and owned-process before/after-COMMIT interruption fixtures
cover complete migration and atomic cancellation/reopen.
See [the authoritative contract](Interaction_And_Sessions.md#bounded-authoritative-task-observation-and-pre-dispatch-cancellation)
and [exact commands](../docs/commands.md#bounded-exact-id-session-commands).

No scheduler/queues/deadlines, workers/termination, model management/task tools,
new grants/approval authority, content retention/deletion or full R12/R13/A
acceptance is claimed. No live Kora/audio/OS-effect, installation, elevation
or account/network-policy trial is part of this qualification.

**Experiment disposition:** no executable or historical receipt is removed.
Maintained production grammar and transaction/reopen fixtures supersede only
generic local fixed-choice/atomicity assertions. MG1's actual SDK/provider
envelope, deadline/retry/quarantine and concurrency measurements are distinct.
RT1/RT2 and linked original-source consumers retain unique runtime/isolation/
late-effect observations; W2 retains worker containment. Speech retains unique
file/hardware feasibility evidence. Storage's intertwined opaque engine/WAL,
encryption/artifact/backup/rekey/capacity/leakage/DPAPI measurements are not
equivalent to standard-SQLite task/question cancellation or handoff migration.
Retaining their fixtures, historical bytes/receipts and consumers is necessary;
none is a production authority contract or a bounded-control acceptance shortcut.

Before publication the owned branch immediately rebased verified merged #82
(`1c4e4aa`) and then working audio-output #79 (`433e5c8`), without unpublished
imports. The existing audio-control bridge uses the consolidated task callback's
shared SQLite connection, with no nested interaction lease/Open; exact live
request, active session generation, callbacks and file verification remain.
Maintained migration/reopen/audio callback tests prove the actual shared-lease
consumer completes without reentrant acquisition; native/typed/activated
output admission, source invalidation and audits remain intact.

Fresh combined root no-restore Release has zero warnings/errors. Core **675**,
Application **1,814**, Tools **38**, Definitions **6**, Windows **933** all pass,
with zero failures/skips. Latest-only portable coverage is exactly **100% line
and branch**: raw rates `1` / `1`, **10,454 / 10,454 lines** and **5,698 /
5,698 branches**. Unchanged thresholds/exclusions enforce the gate. Full
current-head remote CI and actual merge remain the publication gates;
these receipts do not claim acoustic, installed, effect or broader acceptance.

During current-head checks, #81 staged runtime-validation automation merged.
The branch immediately rebased verified `d297c3a`, preserved its source/docs/
automation and reran root Release plus all five suites and fresh portable
coverage: the same **675 / 1,814 / 38 / 6 / 933** passing counts, zero
warnings/errors and raw line/branch rates **1 / 1**. Its preparation scripts
were not run; no provider/account/runtime acceptance or effect trial is inferred.

## R15 Bounded Run-Only Manual Call Command Parity - 2026-10-08

The existing manual layer now shares native and exact typed/activated discovery,
cached get/status, on/off and reset-off routes for `call.manual-active`. Original
input is captured before asynchronous dispatch; committed host control intent,
current session generation, original initiating channel, owner/privacy/input,
native window lifetime and source/policy revision are revalidated around the
retirement fence. Required requested and truthful terminal authority audits use
one shared SQLite connection/lease, retained across awaited resource closure.
Failed/cancelled closure cannot acquire a successful terminal receipt.
See [the bounded contract](Call_Aware_Speech.md#delivered-manual-command-parity---2026-10-08).

Passive reads create no effect, approval, unrelated activity or automatic
evidence. Pending questions, security previews and approvals remain complete
and unbound by reserved commands. Manual changes fence queued/in-flight speech,
recognition and late callbacks; old results remain visual and never synthesize,
reopen capture or replay after clear/reset. The flag is process memory only:
off/reset preserve automatic Active/Unknown/Unavailable observations and all
saved call flags, and restart remains manual-off/detector-unavailable. Lost
required evidence or unconfirmed closure holds explicit conservative protection,
not rollback, an automatic Clear observation or an invented persisted setting.

Owned pre-publication validation against merged main `1bb95ad` (#90): no-restore
Release build has **zero warnings/errors**; Core **719**, Application **2,136**,
Tools **38**, Definitions **6**, Windows **978** pass with zero failures/skips.
Focused native/policy/real private-SQLite call compositions pass **14** tests.
Latest-only portable coverage is exactly **100% line and branch**, raw rates
**1 / 1**, **11,524 / 11,524 lines** and **6,406 / 6,406 branches**, with unchanged
thresholds/exclusions and no analyzer suppressions. Current-head remote CI and
actual merge remain publication gates; these receipts are not broader acceptance.

The branch then rebased actual merged #91, `bf307a004b8ef250bd794faca4360b37aef026a6`.
Manual control now uses its common original-input/committed-intent
`HostControlAdmission`, rather than a duplicate admission pipeline; the manual,
audio and diagnostic-retention session actions remain distinct. Its synchronous
consumers retain their domains, while the manual callback awaits audited
retirement on the same shared lease. Both native lifetimes share UI-observed
visibility invalidation without reading Avalonia properties on storage workers.
Schema-2 public/private evidence validation, future-only ordinary SQLite
retention (1–365/default 30), immutable prior deadlines and audit/file/session/
chat/grant rules remain unchanged.

Fresh rebased root no-restore Release again has **zero warnings/errors**.
Core **732**, Application **2,210**, Tools **38**, Definitions **6**, Windows
**989** all pass with zero failures/skips. Latest-only portable coverage is
exactly **100% line and branch**, raw rates **1 / 1**, **11,830 / 11,830 lines**
and **6,572 / 6,572 branches**, with unchanged thresholds/exclusions. Stopped
activities retained in another asynchronous flow and attempted reinstallation
cannot restore control authority. No live effects or acceptance are inferred.

Before current-head publication, the branch rebased actual external #86 merge
`423f2013bcb8bc009dfb2cbe847c227ae31e01d1`. Its pointer movement/button/wheel
callbacks reset native presentation hide timers only; they do not supply
meaningful session, approval or work authority. A fresh root no-restore Release
has **zero warnings/errors**; all five suites pass **732 / 2,210 / 38 / 6 / 990**
with zero failures/skips. Fresh latest-only portable coverage remains exactly
**100% line and branch**, **11,830 / 11,830 lines** and **6,572 / 6,572 branches**.
The #91 schema/retention/admission behavior and bounded manual scope are unchanged.

No automatic detector, persistent call state, relaxation review, speak-once,
model tool, saved-default change, output mode/volume, microphone/consent/privacy
change, new audio/capture or grant-record mutation is delivered. The existing
`ExactReviewUnavailable` refusal is preserved. Full R15, native/accessibility,
acoustic, runtime, installed and A0-A4 qualification remain open. No live
application/provider/account/audio/OS-effect or installation/elevation trial ran.

**Experiment disposition:** no experiment or historical receipt is removed or
rerun. Acoustic/hardware/provider, actual runtime/worker/containment/late-effect,
storage-engine/encryption/artifact/release/source evidence and consumers remain
unique; deterministic grammar/policy and standard-SQLite admission/audit
compositions do not establish full maintained equivalence.

## R21 bounded shared-profile read-only inspection - 2026-10-08

Delivered only explicit native root registration, selected-source bounded
discovery, exact immutable SKILL.md inspection and stale-revision recheck.
The Core platform seam resolves the Windows profile through Known Folder APIs;
the Windows reader pins ancestor/entry handles and directory identity,
rejecting link/reparse/hard-link/alias/replacement escapes. The strict
`skill-md-instructions-v1` reader keeps compatible and incompatible packages
visible under count/depth/byte/UTF-8/header limits, preserves declared tool
references and discloses uninspected extra files/unresolved executable workflows.
Names/revisions remain source-qualified; no same-name shadowing occurs.

Registration uses the shared atomic preference store, bounded validated
versioned read/read-back, independent durable native control admission and
typed requested/terminal audit. Unknown/corrupt state fails closed without a
reset or root-rebinding fallback. Logs contain only counts/type codes; views
are local inert text and close/cancel under privacy. Legacy ambient
personal-skill startup model ingestion is removed; bundled catalogue bytes and
existing unrelated prompt/instruction routes remain unchanged.

Mechanism evidence is maintained in
[portable parser tests](../tests/Kora.Core.UnitTests/Skills/SharedSkillTests.cs),
[atomic registration tests](../tests/Kora.Application.UnitTests/Configuration/LocalSharedSkillPreferencesTests.cs),
[session/audit workflow tests](../tests/Kora.Application.UnitTests/Skills/SharedSkillDiscoveryServiceTests.cs)
and [owned Windows boundary tests](../tests/Kora.Windows.IntegrationTests/Skills/WindowsProfileSkillReaderTests.cs).
See [the exact scope/limits/recovery](Skill_Storage.md#delivered-bounded-r21-native-inspection)
and [native user workflow](../docs/commands.md#inspect-shared-profile-skills-locally).

No execution, egress, enable/disable/invoke, dependency installation, source
editing, model exposure, trust promotion or Kora-specific authoring is admitted.
R05/R06/R10/R11 are consumed contracts, not a claim those complete road-map
rows passed. R19 is conditional RC-inclusion qualification, not a prerequisite
to this local read implementation. Full Slice B/R23, real installed/native/
accessibility and runtime/security acceptance remain separate evidence gates.

**Experiment disposition:** no experiment code, script, runtime adapter or
historical proof was promoted or retired. Deterministic parser and owned
filesystem fixtures are the maintained evidence for this bounded mechanism;
existing runtime/containment/installed proof witnesses retain their prior
dispositions and cannot authorize skill execution.

Local validation for this bounded mechanism: locked restore and root Release
build (zero warnings/errors), all five root test suites after preserving
concurrent call-feedback and file-preview work (Core 931, Application
2,607, Tools 60, Definitions 6, Windows 1,147) and the enforced portable
**100% line / 100% branch** coverage gate. These are deterministic/source
mechanism results, not real-user/native-accessibility or complete release
qualification.

## R25 Bounded Local Caption UX Delivered - 2026-10-09

This follows merged #109's disabled-by-default current-utterance slice after
fetching/rebasing onto #114 main, `e5b89ba`, preserving merged #107 and #110-#114
work without changing call feedback, session retention/deletion or scheduling.
Delivered only primary-screen working-area corner placement, a canonical
integer **0-30-second** normal-completion dismissal delay (default/reset **5**),
and run-only current-caption **Pin / Unpin**. The existing **Off** default and
mode-file bytes are unchanged. Native/exact typed/current-name ACTIVATED
discovery/get/status/set/reset expose exact IDs, types/units/choices, defaults,
saved/effective source, revision, scope, timing, reset and recovery.

Placement/delay reuse the existing host-held admitted session/generation,
original-channel/call/privacy/ownership gates and requested/terminal typed audit,
atomic write/exact readback, completed intent receipt and unconfirmed marker.
The independent version-1 caption option tuple contains only corner/delay,
through the shared atomic store and `IApplicationDataPaths`; corruption,
unknown schema/corner, noncanonical/out-of-range delay, unreadable state and
pending evidence refuse rather than defaulting. Reset preserves the companion
and mode; no option change enables captions or starts speech/capture.

Only observed matching response/playback identity/generation/utterance segment 0
can be retained after normal successful completion, labelled **PREVIOUS SPEECH**.
Pin state is ephemeral, not a durable setting or effect authority; unpin uses
the original completion deadline. Stop/cancel/failure, replacement, configuration
revision, call, lock, privacy/ownership/input recovery and disposal retire
immediately even when pinned; no late frame or pin command can revive retired
or reveal queued/unplayed text. Caption content is never stored, logged,
admitted to history or sent to a model. Required native recovery stays separate.

**Still open:** sentence-level alignment needs real host-admitted sentence
boundaries; the composed playback adapter reports only utterance segment 0.
This slice adds no heuristic timing, splitting or audio pipeline. Display
selection/arbitrary positions, broad natural caption/navigation aliases,
word alignment, rich browser/static HTML/diagram rendering, installed/native
accessibility and acoustic qualification remain separately gated. Existing
ordinary-speech **3-sentence/80-word** caps and over-limit full visual refusal
are unchanged. A brief spoken offer to show an over-limit detailed result is
separate open R09/R10 work, not delivered or inferred from captions.

Maintained domain, parser, admitted configuration/audit, private atomic storage,
fake-clock/playback and native source-binding tests cover exact defaults/bounds,
restart/reset/companions, stale/foreign selections, corruption and evidence
failure, original protected-call voice denial, observed-only pin/expiry,
completion versus interruption and late/unknown playback identity.
See [the authoritative option contract](User_Configuration.md#delivered-bounded-caption-ux-options-r25---2026-10-09),
[speech/privacy contract](Call_Aware_Speech.md#delivered-bounded-local-caption-ux---2026-10-09)
and [native user workflow](../docs/settings.md#local-speech-text).

Direct root validation on the combined #114 base: locked restore through the
machine-required feed (no feed configuration, credentials or dependency changes
committed), **zero-warning/zero-error Release build**, Core **972**, Application
**2,773**, Tools **69**, Definitions **6**, Windows **1,177** tests passed
(**4,997** total, **zero failures/skips**). Fresh latest-only portable reports
enforce exactly **14,736/14,736 lines** and **8,568/8,568 branches**, raw rates
**1/1**, with unchanged 100% thresholds and no new exclusions.
These deterministic/source checks are not installed, live-device/audio/call,
accessibility, acoustic or A0-A4 acceptance.

**Experiment disposition:** all experiment executables and historical receipts
remain intact. Fake time/playback, local preference and native-binding evidence
do not replace hardware/acoustic/provider/native runtime or privacy-stop proof.
No experiment is promoted, rerun, retired or deleted.
