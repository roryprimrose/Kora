# GitHub-hosted merge admission

Kora is a personal-account repository. GitHub's native Merge Queue is not
available for this ownership model. This repository instead provides a bounded
controller running entirely on GitHub-hosted Actions. Its default profile remains
single-candidate; the separately administered batch profile below is dormant.
It needs no local agent, workstation, external queue service or always-on runner.

**The controller is disabled until explicitly configured.** An absent or
`disabled` `KORA_MERGE_QUEUE_MODE` preserves ordinary PR/main/tag/manual CI.
An unknown mode fails CI admission rather than silently enabling another policy.
Do not enable the queue merely by merging its implementation PR.

## Opt-in independent beta delivery

`KORA_MERGE_QUEUE_DELIVERY_PROFILE=independent-v1` is a separate administrator
opt-in, requiring enabled `batch-v1` admission. Absent/empty retains the serial
completed-tip qualification/publication fence below; unknown values fail closed.
Merging the implementation does **not** activate it or change protection.
Activation and rollback require a separately authorized, drained transition:
no active candidate, no outstanding main/publication jobs, and a successfully
qualified current tip. Do not toggle profiles underneath queued publishers.

The independent profile preserves one candidate lane, complete portable
100% line/branch coverage, all twelve Windows partitions/five aggregates,
managed setup compilation/tests, payload inspection and protected App admission.
The reviewed `windows-12-delivery-v1` inventory has 25 identities; its installer
identity is **Validate installer packaging**, not a claim that MSI/Burn ran.
Historical `windows-12-v1` and exact negative-retirement contracts remain intact.
The new workflow has its own exact-blob, twenty-identity failed-preflight
no-fanout retirement proof; a marker, missing job or workflow failure alone is not
physical quiescence. It authorizes retirement only, never successful admission.
Single-candidate/control-path work continues to build physical installer packages.

For combined candidates only, trusted admission compares the complete admitted
base/combined revisions. Installer/shared assets, project/lock/build/toolchain,
workflow/release changes and unknown or capped impact select full MSI/Burn.
Ordinary application/test/documentation changes may omit only the dedicated
MSI/Burn build/inspection; metadata contracts still execute. Rename source paths
and deletions count. A successful policy job reports **not selected**, not executed
packaging success. Main always freshly builds and inspects its own payload and
installer bytes. This intentionally moves detection of application-payload-only
packaging incompatibilities to main; it does not classify failures as flaky.

After observing the actual final squash merge, exact tree/parent/main reachability
and complete first-attempt candidate proof, the App atomically records
`releases/<actual-source>.json` with queue state. Records contain the tested
combined source, workflow base/blob/inventory, run, ticket and batch digest where
applicable. Only the final combined tree receives a record, never intermediate
batch prefixes. Later saves preserve the entire state tree and cannot overwrite a
different existing source record. This is App-owned Git authority, not a PR-uploaded
proof file or reusable build artifact.

The controller reconciles the source's full main run before advancing, covering
the push-versus-record race without blindly redispatching. Once full execution
has started (or the exact reviewed attempt failed before fanout with complete
physical-terminal evidence), its qualification/publication result no longer gates admission.
A failed main run withholds **that source's beta**; it does not revoke candidate
proof or stop later admission. Main functional regressions can therefore leave
main red until repaired; moving a failure to main does not repair it.
Unknown main-admission authority remains fail-closed; a failed admission is not
reclassified as a full run or covered by the new positive-admission preflight rule.

Full main runs remain non-cancelling and keyed by source. Publishers use a
non-cancelling per-source FIFO lock, not the controller lock. At each irreversible
publication boundary they revalidate the cryptographically pinned queue record,
the exact source/tree, protected-main reachability, first-attempt ordinary-main
caller and static full-gate dependencies. Newer main/active candidates do not
revoke an earlier recorded source. Missing, changed or unreachable proof fails
closed. Every release uses fresh artifacts from its own actual-source main run;
candidate artifacts are never relabeled. Independent publication is beta-only,
with `make_latest=false`; semantic channel selection cannot regress merely because
an older beta finishes later. Stable publication still requires the serial profile.

The historical measured 45m41s cycle contained 24m17s of main qualification and
19m46s of candidate validation. Removing the main-result barrier suggests 21m24s
before new scheduling/record overhead, **not a measured post-change cadence**.
Skipping the historical 4m41s installer job saves runner occupancy but zero direct
critical-path time: it finished 10m41s before the Windows aggregate. Parallel main
work may compete for runners; no capacity or speedup guarantee is implied.

Implementation checks use deterministic fake GitHub/publication operations:
`node .\eng\run-policy-tests.cjs`, `.\eng\Test-CiPreflight.ps1` and
`.\eng\Test-GitHubRelease.ps1`. They neither activate this profile nor qualify
hosted concurrency, App permissions or actual release publication.

### Guarded drain, switch and resume

The [administrator transition](recovery.cjs) provides a durable reservation-only hold when normal queue continuation prevents a drained cutover. It does not change mode, withdraw enrollment, cancel candidates/publishers, grant variable permissions to the App, or replace candidate proof.
The [batch controller](batch.cjs) continues already-owned batch/singleton work and actual-tip qualification, but checks the hold before listing or reserving new candidates.

Use the [account-safe CLI preflight](../../.github/skills/kora-github-pr/SKILL.md#preflight-verify-before-mutating) and separately recorded authority for this entire operation. All four operations use the existing protected controller environment and concurrency group.
They require a first-attempt manual invocation on main, live administrator authority, exact state checkpoint and `acknowledge_batch_change=true`; after hold creation they also require the same administrator and exact `recovery_ticket` hold token ([validation](recovery.cjs), [workflow](../../.github/workflows/merge-queue.yml)).

1. Read current main, variables, protection, the state checkpoint and physical CI ownership. If delivery is already independent, do not create another transition. Dispatch `hold-independent-delivery` with the reviewed checkpoint. Read back `deliveryTransition.phase=draining` and its token; an ambiguous dispatch/save is reconciled, not blindly repeated.
2. Let existing candidate merges, reconciliation and publishers complete normally. The hold is retained if qualification fails or owned work is unknown. Follow only identified existing work with a finite read-only join; do not cancel work, dispatch replacement CI or clear the hold to obtain readiness.
3. Read the fresh checkpoint and dispatch `prepare-independent-delivery` with that checkpoint and token. It requires no active candidate, the actual current completed tip, its freshly reobserved successful first-attempt qualification, exact tree and physically quiescent CI/publication.
   It rechecks administrator, main and physical quiescence before checkpointing `phase=ready`, the source/tree and qualification receipt.
4. With the verified CLI principal, reread the ready checkpoint/token/source, live main, variables and physical inventory immediately before the single configuration write. Require unchanged owner and prepared source, no active candidate, no outstanding main/publication work and the exact durable qualification. Then set only the delivery variable and read it back:

   ```powershell
   gh variable set KORA_MERGE_QUEUE_DELIVERY_PROFILE --repo github.com/roryprimrose/Kora --body independent-v1
   gh variable get KORA_MERGE_QUEUE_DELIVERY_PROFILE --repo github.com/roryprimrose/Kora
   ```

5. Dispatch `resume-independent-delivery` with the exact ready checkpoint/token. The newly started controller must observe independent configuration. It rechecks current source, exact qualification and physical quiescence, then atomically checkpoints `deliveryProfile=independent-v1` and transition `phase=completed`.
   Normal events reopen reservations; do not dispatch a candidate to wake the queue. A stale serial controller fails closed against durable independent adoption ([controller](batch.cjs)).
6. Verify actual normal-wake adoption, complete candidate proof, next-candidate/main-publication overlap, actual merged PRs and per-source publication. Configuration readback or a completed transition alone is not measured throughput.

For each operator dispatch, substitute the operation and freshly reviewed values; the initial hold omits `recovery_ticket`:

```powershell
gh workflow run merge-queue.yml --repo github.com/roryprimrose/Kora --ref main -f operation=prepare-independent-delivery -f recovery_checkpoint=<checkpoint> -f recovery_ticket=<hold-token> -F acknowledge_batch_change=true
```

Before switching configuration, `abort-independent-delivery` with the same owner/checkpoint/token safely restores serial reservations without cancelling existing work or erasing qualification. After configuration switches, abort is rejected: preserve the hold and reconcile readback, rather than blindly clearing the variable or revoking source records.
A changed source/checkpoint, failed qualification, pending work or failed API read retains the hold. Rollback after independent publication still requires the separately authorized drained transition above ([transition guards](recovery.cjs), [regressions](batch.test.cjs)).

## Dormant five-PR batch profile

`KORA_MERGE_QUEUE_PROFILE` absent, empty, or `singleton-v1` retains the version-1
single-candidate protocol and four required App checks. Unknown explicit values
fail closed. Merging this implementation does not change repository variables,
protection, App permissions, releases, or production queue state.

The separately authorized `batch-v1` profile consumes only explicitly migrated
version-2 state. It has one lane and takes up to **five already-waiting** eligible
PRs, priority then number, without a fill timer. Active work is never preempted.
Unknown enrollment, reviews or mergeability cannot reserve a member.
Controller, CI-workflow and release-control changes, and isolated group-failure
retries, use the existing fully validated **exact-head singleton path**, even
inside this profile.

A complete zero-file PR also uses that exact-head singleton path: an empty diff
does not attest validation or prove that GitHub merged the PR. File inventories
must match a nonnegative count below GitHub's 3,000-file pagination limit, with
the same PR, same-repository head and main base before and after pagination.
Unavailable, partial, duplicate, malformed or changing observations fail
explicitly; they are never reinterpreted as empty. Normal candidate checks,
merge confirmation and cancellation/join remain required.

### Frozen source and truthful proof

[Batch policy](batch-policy.cjs), [reconciliation](batch.cjs) and the
[GitHub adapter](github.cjs) freeze:

- Protected main, ticket, ordered original PR heads, exact enrollment timestamps
  and review decisions.
- Trusted workflow blob, explicit compatible job schema, and every required job
  identity. Historical singleton inventories remain available for retirement.
- Synthetic prefix commit/tree identities and the final combined commit, bound
  by a SHA-256 `batch-manifest-v1` digest.

The App creates ticket-owned `automation/merge-queue-batch/<ticket>/<prefix>`
refs through GitHub's normal ref/merge APIs. Candidate code never executes with
the App token. Construction reconciles exact parents after interruption rather
than forcing refs or blindly repeating a merge with an ambiguous outcome.

Construction stops at the first conflict instead of discarding a compatible
prefix. Before any digest or dispatch exists, the controller seals a **new
manifest containing only that contiguous prefix**, then requires its complete
combined proof. It never rewrites a sealed/dispatched manifest or reuses proof
from the larger reservation. The conflicting member retains its priority/number
position and receives an exact-head singleton retry; later members stay queued.
If the first member conflicts, only that member is isolated. No original PR is
merged during construction and no member is skipped to fill a batch
([implementation](batch.cjs), [regressions](batch.test.cjs)).

CI is dispatched on trusted protected main and checks out the admitted combined
SHA as unprivileged source. It retains complete portable module/100% line **and**
branch coverage, all Windows producer/partition/catalog evidence, payload
inspection, installer and aggregates. The `windows-12-v1` inventory is not a
substitute for those maintained gates. Missing, duplicate, skipped, wrong-source
or nonterminal required evidence cannot admit a batch. Observation deadlines
never establish success or physical quiescence.

After proof, each **original PR** is protected-squash-merged separately. Before
each boundary the controller rechecks every remaining member's head, enrollment
and reviews, and exact actual main. Expected-head branch updating is recorded
before its API request. A changed head is accepted only with the expected two
parents, prefix tree, verified commit and configured queue-App bot identity;
unknown provenance holds recovery rather than treating an author change as an
App update. This provenance condition requires enablement qualification with
GitHub's actual update behavior; deterministic tests do not establish it.

Each actual merged PR, merge SHA, single main parent, prefix tree and main
reachability is observed before advancing. An affirmative merge response alone
is insufficient. Final main must have the combined tree, **not** its SHA.

Only the existing App-owned **Merge admission** check attests a batch member.
Its output names the combined tested commit/run and admitted prefix policy.
It does not copy green portable/Windows/installer checks or exact-source
coverage comments onto untested member heads. Intermediate main prefixes are
intentionally **not independently validated**. Synthetic artifacts remain
validation-only; they are never relabeled as final-main release artifacts.

### Publication fence and qualification

In version-2 state, an active batch or singleton blocks publication. Only the
verified `completedTip` may qualify/publish, at its **actual source SHA**.
Main/tag/manual admission suppresses prefix full builds. A completed-tip full
run is requested only after reconciling existing exact-source first attempts,
without candidate inputs. An already-running full main run becomes the single
durable qualification owner, so it does not trigger a duplicate dispatch.
Ambiguous dispatch is observed, not repeated. Active batch-profile main runs
are not repeatedly cancelled as prefixes arrive.
See the [ownership reconciliation](batch.cjs#L21).

The dedicated, statically locked batch publish job shares
`merge-queue-controller` concurrency with trusted main mutations, rather than
only per-version concurrency. Both batch lock participants use GitHub's
[`queue: max`](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency)
bounded FIFO waiting policy, so newer controller wakeups do not replace a
waiting publisher. The legacy publish job retains its per-version lock and
cannot publish against v2 state. Stable-tag requests in batch mode qualify and
publish through a fresh trusted-main run at the exact completed/tagged SHA,
not an old tag workflow definition. The shared
[publication script](../Publish-GitHubRelease.ps1) enforces its live-state and
trusted serialized-caller contract as well as existing source/version/asset
safeguards. It rechecks at irreversible publication boundaries, including late
jobs. Missing or unknown state/provenance fails closed.
Run authentication rejection names the failed predicates and a bounded lifecycle
value, never raw API metadata. It retains every caller, workflow, source and
active-job check. A historical composite rejection without the original API
response does not establish which predicate failed or justify relaxing it.

The [release snapshot reader](../GitHubRelease.Batch.ps1) pins queue commit,
tree and blob bytes, then rechecks the queue ref. A valid ref move restarts that
entire read, up to six attempts without sleeps; routine singleton checkpoints
must not turn one transient move into a main-build failure. Only a stable,
fully verified snapshot is evaluated. Exhausted retries, deletion, malformed
identities and API failures still block publication, including at the late tag
and publish boundaries ([regressions](../tests/GitHubRelease.Batch.Tests.ps1)).

The controller does not reserve the next batch until complete actual-tip CI and
the publication join (`Qualify completed batch tip`) succeed. It persists the
exact run/job/source qualification receipt. This deliberately gives publication
a turn under sustained arrivals; no throughput benchmark or fivefold speedup is
claimed. Failed qualification or an unobservable dispatch holds the lane for
repair rather than starving release behind further batches.
An authenticated exact-source first attempt with a failed publication job is
reported as `blocked-publication` / `BLOCKED_PUBLICATION` only after the complete
expected inventory, publication and qualification jobs are physically terminal.
The existing controller warning and Actions summary identify source, run,
attempt, publication job, conclusion and last durable progress (completed tip,
qualification owner and queue checkpoint), and state that authorized recovery is
required. The checkpoint is a revision, not an invented progress timestamp.
Successful reconciliation of this observation is not successful delivery.
Missing, duplicate, nonterminal or unknown evidence remains held without a
terminal-publication claim. Every reconciliation still observes fresh evidence;
a separately authorized successful first attempt can qualify immediately.
This reporting neither retries publication nor bypasses the qualification fence.

The [qualification reader](github.cjs) observes queued/running runs as well as
completed runs. An ordinary push with incomplete admission metadata delays
dispatch without claiming full qualification ownership. A completed push whose
version and qualification jobs were explicitly skipped is not a full owner;
missing records are not treated as that suppression shape. Once full work is
observed, the controller records one exact run/source/first-attempt owner.
Missing run listings are reconciled through that exact run ID, not treated as
permission to dispatch. Owner replacement by an observed fresh run requires the
previous owner's physical terminal job evidence. Only complete successful
actual-source jobs and the unique publication join produce the qualification
receipt ([regressions](github.test.cjs), [state validation](batch-policy.cjs)).

The version-2 additions are optional for existing checkpoints: existing
qualification receipts and boolean dispatch intents remain valid, and
previously sealed manifests without priority fields retain their digest.
New manifests also freeze each admitted member's priority. No state migration,
queue setting or protection change is performed by this implementation
([state validation](batch-policy.cjs)).

### Retrying failed completed-tip qualification

A failed actual-tip run keeps admission paused. The [qualification reader](github.cjs#L268) accepts only a successful first attempt at the exact completed tip, with complete expected jobs and the successful qualification join. Rerunning the failed workflow creates a later attempt and cannot supply that receipt.

An authorized operator can request a **fresh ordinary main run**, using the same empty-input dispatch as the [controller](github.cjs#L287):

1. Recheck live mode/profile, durable state and actual main. Require `batch-v1`, no active candidate, and `completedTip.sha` equal to actual main. Inspect the failed job and confirm all prior qualification and candidate CI is terminal.
2. With authority to retry validation, dispatch once without candidate or comparison inputs:

   ```powershell
   gh workflow run ci.yml --repo github.com/roryprimrose/Kora --ref main
   ```

3. Read back the new run's source SHA, `workflow_dispatch` event and first-attempt identity. An accepted or ambiguous dispatch must be reconciled before another request; do not blindly repeat it.
4. Require the complete ordinary main gates and `Qualify completed batch tip` to succeed. The controller then records the exact receipt and resumes normal selection. A failed retry remains a failure requiring investigation, not permission to skip tests or change admission.

This retry does not dispatch candidate CI, edit queue state, change protection or bypass a check. A source change, active candidate, uncertain owned work or incomplete evidence requires the corresponding reviewed recovery instead ([batch admission](github.cjs#L383), [recovery](#interrupted-batches-repair-and-rollback)).

### Operator cutover: separate administrator authorization required

These are rollout instructions, **not actions performed by the implementation**:

1. Keep profile absent/`singleton-v1`. Deliver the implementation through the
   existing four-check, strict, administrator-enforced singleton profile.
   Qualify the exact GitHub adapter/update provenance and publishing caller
   contract before advertising batch readiness.
2. Pause new queue intent operationally and drain the existing singleton lane.
   Review the exact durable checkpoint and all queued/running CI, including
   main/tag/manual publication and old workflow versions. All must be physically
   terminal. Exclude historical publishing definitions without this fence from
   activation; this code cannot retrofit immutable historical workflows.
3. With separately recorded rollout authority, manually run the trusted
   controller operation `migrate-batch-v1`, providing `recovery_checkpoint` and
   `acknowledge_batch_change=true`. It requires live administrator authority,
   idle v1 state, strict existing protection, unchanged main and quiescent CI.
   Read back the v2 checkpoint. It does not activate settings or dispatch CI.
   During the transition, profile/state mismatches fail closed.
4. Separately change required-check protection to **only Merge admission from
   the configured queue App**, retaining `strict=true`, administrator
   enforcement, required reviews and normal protected squash merges. Do not
   broaden App permissions, enable bypass or manufacture reviews/checks.
5. Separately set `KORA_MERGE_QUEUE_PROFILE=batch-v1` with queue mode still
   `enabled`. Reconcile. The migrated main tip must first complete actual-source
   qualification/publication before any new batch reservation. Inspect exact
   source/run/job receipts, then resume enrollment.

No automatic state migration or profile/protection change occurs on merge.
The existing App identity is configured, not a new hardcoded principal.

### Interrupted batches, repair and rollback

Every irreversible dispatch, branch update and original-PR merge has a durable
intent. Restart observes outcomes before acting. Cancellation acknowledgement,
workflow aggregate success, absent metadata, or a deadline cannot release
unknown owned work. No operation resets main or discards unrelated advances.

Entering batch retirement checkpoints the phase before requesting one trusted controller reconciliation, including a first-member construction conflict with no candidate dispatch ([controller](batch.cjs), [regressions](batch.test.cjs)).
The follow-up still joins owned work before releasing the lane. An uncertain retiring lane does not repeatedly self-dispatch; later workflow events or scheduled reconciliation retain recovery responsibility. A failed continuation is explicit and preserves the checkpoint for recovery.

- A definitive required-job failure/duplicate or live source/base/enrollment/
  priority/review withdrawal requests cancellation of exact ticket-owned work
  during the same reconciliation, without waiting for the workflow aggregate.
  Singleton required-job failures and running review withdrawal use the same
  prompt cancellation policy. Unknown authority/API failures remain explicit;
  missing evidence alone is not a conclusive early failure. Unrelated main/tag
  runs are not cancelled. A cancellation acknowledgement, completed aggregate,
  missing/duplicate jobs or expired deadline never releases the lane; every
  owned run and its exact inventory must be observed physically terminal
  ([batch](batch.cjs), [singleton](controller.cjs), [regressions](controller.test.cjs)).
- Before the first original merge, failed combined evidence retires only after
  exact owned work is terminal. Members receive full singleton retries; group
  failure is not independent member failure. A failed singleton retains the
  existing head/enrollment quarantine policy.
- After partial integration, preserve actual PR Merged states and the recorded
  prefix. A changed/withdrawn/review-blocked suffix, unexpected main, or
  uncertain merge holds `recovery-required` and the publication block.
  A late observed merge may record its verified actual prefix, but does not
  silently clear reviewed recovery.
- `resume-batch` requires separately authorized administrator review, exact
  checkpoint/ticket, `acknowledge_batch_change=true`, terminal owned CI, exact
  recorded main/prefix and an unchanged, still-authorized suffix. It cannot
  repeat or erase an unresolved merge request.
- `repair-batch` has the same checkpoint, authority, actual-main and physical
  quiescence fences. It archives the old prefix/manifest and clears only the
  controller's active record, never main. Remaining current eligible work must
  receive a **new full proof against actual main**; withdrawn intent is not
  replayed. To preserve unrelated main advances, explicitly supply the reviewed
  actual SHA as `recovery_main`; recorded-prefix ancestry and the original
  prefix tree must still verify. Resume cannot reuse combined proof on that
  advanced main. An unresolved merge request or missing/duplicate terminal
  metadata cannot be erased with repair. Reconcile those facts explicitly
  before a fully validated repair/revert.
- Recovery calls are checkpoint-specific; repeating one after its successful
  write fails rather than replaying effects. Inspect checkpoint readback after
  transport uncertainty. The legacy `recover-unobservable-dispatch` operation
  cannot clear a batch.
- Roll back only at an idle, qualified completed tip with quiescent CI. Run the
  separately authorized `rollback-batch-v1` operation with the exact checkpoint
  and acknowledgement. It preserves batch evidence inside v1 state. Restore
  the four configured App checks and `singleton-v1`/absent profile separately.
  Do not simply switch profiles while v2 or partial work remains active.

Local protocol evidence: `node --test eng/merge-queue/*.test.cjs` and
`./eng/Test-GitHubRelease.ps1`. Fake adapters exercise the protocol without
remote ref/merge/dispatch mutations. They are implementation evidence, not live
activation, protection qualification, hardware proof or hosted CI success.

The remaining sections describe the default singleton profile unless explicitly
noted otherwise.

## Contributor workflow

1. Open a non-draft, same-repository PR targeting `main`.
2. Enable GitHub's **Auto-merge**, using squash. This is queue intent, not
   permission to bypass checks or review.
3. The controller selects eligible PRs by priority label (high, normal, low),
   then ascending PR number within each priority. Unlabeled PRs are normal.
   Unmet required reviews and unknown mergeability do not occupy the lane.
4. If needed, GitHub updates **only that PR** from main using the branch-update
   API with an expected head. It does not rebase/force-push every waiting branch.
   The update can add a merge commit on the topic branch; squash merging keeps
   main linear.
5. The selected PR receives full portable/coverage/package, all five Windows
   shards and installer validation. Other PRs get lightweight queue-policy and
   scheduling feedback, not a duplicate full build.
6. The controller attests the exact source and protected base, then requests a
   protected squash merge. GitHub's native auto-merge may perform the same
   authorised merge first; recovery recognises that outcome.

Conflicting or failed validation revisions leave the lane with explicit failed App checks.
Fix them with a new commit, or disable and re-enable Auto-merge for an intentional
retry. Repeated scheduled reconciliations do not rebuild the same failed head.
Enrollment is identified by GitHub's Auto-merge `enabledAt` timestamp, not an
event payload. A scheduled reconciliation can therefore observe an intentional
re-enrollment even if the original event was missed or coalesced. Re-enrollment
also invalidates an active ticket on the same head; unavailable or unknown
enrollment data never grants intent.
Fork PRs are not supported by automatic branch updating in this first version.
They must be brought into a reviewed same-repository branch before admission.

Local clients can push or rebase a PR, but cannot grant admission by doing so.
An update invalidates an active candidate and its previous evidence. Contributors
do not need a queue-specific AI prompt or client-side merge loop.

## PR priority labels

Use GitHub PR labels to reorganize the waiting queue without withdrawing
Auto-merge or editing branches:

| Label | Selection priority |
| --- | --- |
| `queue:priority-high` | First |
| `queue:priority-normal` or no priority label | Default |
| `queue:priority-low` | Last |

Priority is case-insensitive. Other labels have no scheduling effect. Use at
most one priority label: multiple different priorities or an unsupported
`queue:priority-*` label make a waiting PR ineligible, with an explicit warning
in the controller run. Remove or replace those labels to restore eligibility;
this does not quarantine the revision or require a new commit.

For example, once these labels exist in the repository:

```powershell
gh pr edit <number> --repo roryprimrose/Kora --add-label "queue:priority-high" --remove-label "queue:priority-normal" --remove-label "queue:priority-low"
```

Create the three labels in GitHub's repository Labels settings before using
them. Labels are repository metadata, not workflow configuration or App
permissions; this implementation does not create or apply them automatically.
Adding or removing a label wakes the controller, which rereads live PR metadata
rather than trusting the event payload. A priority change observed during
selection requests a fresh reconciliation before reserving that candidate.

Priority affects only the next reservation. It never interrupts an updating,
reserved, validating or retiring candidate, and changing the active PR's labels
does not invalidate its validation. Priority does not enroll a PR, resolve
conflicts, satisfy reviews, retry a quarantined head/enrollment, or bypass any
required check. Existing failed revisions still require a repaired commit or
intentional Auto-merge re-enrollment.

Within each tier, lower PR numbers come first. There is no automatic aging or
fair-share rotation: a continuing supply of high-priority work can delay normal
and low-priority PRs. Raise or lower labels deliberately when priorities change.

## Copilot and other PR clients

Instructions help clients cooperate with the queue; they are not its execution
or permission boundary. GitHub continues reconciling when a Copilot session ends.
The repository instructions and PR clients follow this mode-aware
protocol:

1. Successfully read the live repository variables before remote integration.
   An absent, empty or `disabled` `KORA_MERGE_QUEUE_MODE` means ordinary CI;
   `enabled` means hosted admission. A failed read or another value is unknown:
   stop new integration mutations, report the exact error, and continue only
   safe local work. Do not infer activation from a workflow file or PR label.
   For example, this read emits only the relevant variable:

   ```powershell
   gh api --paginate repos/roryprimrose/Kora/actions/variables --jq '.variables[] | select(.name == "KORA_MERGE_QUEUE_MODE") | .value'
   ```

2. Validate and commit the owned feature, then create or update its single
   same-repository PR targeting main. Use a non-draft PR only when ready;
   preserve an explicit draft request. Under the
   [Kora delivery default](../../.github/copilot-instructions.md#pull-requests-and-merge-admission),
   authorization for ready-PR delivery includes protected squash Auto-merge,
   unless the caller explicitly requests PR-only/no-merge, draft or local-only
   work. Local implementation alone grants no publication authority. Offer
   publication and protected Auto-merge together; do not add a routine second
   permission question after authorized ready-PR publication.
3. When disabled, retain ordinary protected PR CI and the owning workflow's
   integration procedure. Do not dispatch redundant manual CI.
4. When enabled and merging is authorized, verify the current owned remote
   head and request protected squash Auto-merge. For example:

   ```powershell
   gh pr merge <number> --repo roryprimrose/Kora --auto --squash --match-head-commit <tested-head-sha>
   ```

   GitHub owns candidate selection, branch updating and full validation.
   Multiple locally ready PRs can be enrolled without competing local
   rebase/build loops. A waiting PR is not a failed PR.
5. On a supported resume, inspect the current PR head, reviews, enrollment and
   candidate-head checks from the configured queue App. Correlate candidate
   workflow runs by their exact ticket. Skipped ordinary PR jobs and successful
   checks on the main workflow SHA do not establish candidate readiness.
   Monitor to repair failures or report progress, not to drive queue execution.
   Do not claim continuing monitoring after an unsupported session yield.
6. Repair concrete failures on the same PR. Fetch and incorporate its current
   remote head, including App-created branch updates, without discarding
   intervening work. Revalidate changed inputs and push the repair normally.
   Do not rewrite a queued branch just because main advanced, dispatch
   candidate CI manually, or rerun an old ticket. An intentional same-head
   retry after a known failure uses authorized Auto-merge re-enrollment;
   cancellation and lane release remain the controller's responsibility.
7. Verify authoritative merged state, the actual merge SHA and its reachability
   from fresh main before reporting completion or cleaning up.

Use the [Kora PR skill](../../.github/skills/kora-github-pr/SKILL.md#preflight-verify-before-mutating)
to discover and pin the authenticated repository CLI publisher. Compare any
explicit caller/handoff expectation and stop on mismatch or missing access;
successful identity discovery does not require routine account confirmation.
Keep contributor identities out of shared defaults. Store agent-created PR
bodies and receipts under the owning workspace's ignored `.scratch`, following
[repository scratch policy](../../.github/copilot-instructions.md#agent-scratch-and-session-files);
host-managed storage is outside this repository policy's control.

For handoff or resume, retain the verified publisher, source/PR ownership,
publication authority, current remote head, enrollment readback and validation
results as described in the [PR skill](../../.github/skills/kora-github-pr/SKILL.md#handoff-repair-and-uncertainty).
Recheck live state before repair. Pending hosted admission does not require
another publication decision, grant broader authority or prove that an agent
will continue monitoring.

App installation, secrets, environments, expected-source protection, mode
changes and durable state repair require separate administrator authorization.
Do not perform the one-time rollout merely because a user authorized PR
creation, Auto-merge or monitoring. Unknown App/protection health is not
permission for a PAT fallback, bypass or local replacement merge controller.

## Authority and exact evidence

The [controller workflow](../../.github/workflows/merge-queue.yml) checks out only
protected main. Its repository-scoped GitHub App token is never passed to a
candidate build, a PR script or a candidate artifact.

The [CI workflow](../../.github/workflows/ci.yml) is dispatched on **main**, with a
controller-owned PR number, candidate SHA, base SHA and unique ticket. Its
read-only admission job verifies the durable reservation, current PR head,
Auto-merge intent, current main and base ancestry. Test/package jobs check out
the admitted candidate SHA, without persisted checkout credentials.
Payload manifests and source-tool inspection use that SHA, not the main SHA
owning the workflow definition. Candidate version resolution is explicitly
validation-only; the trusted workflow independently prevents release publishing.

CI's built-in job checks for a dispatched build attach to its main workflow
revision. The controller therefore creates the existing three required check
names, plus **Merge admission**, on the **candidate head**, using the queue App.
Only one matching first-attempt workflow run and exact successful required job
names qualify. Missing, duplicate, skipped, neutral, failed or cancelled required
jobs do not qualify. No downloaded artifact, PR text or rendered log grants
merge authority. Coverage comments remain informational.

Workflow completion is not terminal job evidence. A successful completed
workflow with missing, nonterminal, null or unknown required-job metadata keeps
its candidate checks pending and retains the lane. The controller lists the
exact run attempt's jobs with complete, consistent pagination; every observed
job must match that run, attempt and workflow source. Duplicate required jobs
fail validation; repeated job IDs, changed correlation, API/authentication errors
and incomplete pagination fail explicitly, without accepting partial evidence or
falling back to step/workflow success.

The first incomplete observation is persisted on the existing ticket. It gets
the same ten-minute observation budget used for merge confirmation, without
resetting on restart or reconciliation, changing workflow/job timeouts or
redispatching CI. Existing hosted events and the five-minute schedule reobserve
metadata; the controller does not poll, sleep or self-dispatch a pending lane.
Only exact, current, terminal-success required jobs can admit the candidate
before that deadline. A confirmed terminal failure quarantines the revision.
At the deadline, incomplete evidence explicitly fails validation and enters
retirement, not endless pending admission. Late success cannot revive that
ticket. Missing/unknown/nonterminal jobs are not proof of physical quiescence:
cancellation is requested and lane ownership is retained until all required
jobs exist exactly once and all observed jobs are actually terminal. This also
applies to stale, cancelled or already-merged candidates. Unknown resource state
may therefore hold a retired lane for reviewed recovery; it never grants a
second candidate or automatic success.

The expected inventory comes from `.github/workflows/ci.yml` at the reservation's
immutable base, which must match the run's workflow `head_sha`, not current main,
the candidate PR head, dispatch inputs or returned job names. The trusted workflow
declares `# merge-queue-job-schema: windows-12-v1`; its inventory includes both
portable/payload producers, the shared Windows build and all twelve Windows
partitions, as well as the preserved required aggregates and five legacy gates.
The maintained workflow contract compares every candidate job and expanded
matrix identity with that inventory. A producer/matrix change must update the
schema and inventory together; a new unsupported version cannot release a lane.
Known unversioned predecessor workflow blobs have explicit inventories in
[policy.cjs](policy.cjs), including the pre-split five-shard workflow and the
initial unversioned twelve-job workflow and its merged native-fixture timing
revision from [PR #661](https://github.com/roryprimrose/Kora/pull/661).
Old runs therefore retire against their own inventory rather than requiring
jobs that did not exist.

The historical twelve-partition regression uses the frozen
[native-fixture timing workflow](fixtures/ci-native-fixture-timing.yml), whose
Git blob is `d3db4c610d08468c508f09e63b10f7c6568b82ea`. Keep those bytes unchanged:
removing the schema marker from today's workflow does not recreate a trusted
predecessor. Tests cover both the current schema-declared workflow and that exact
historical inventory, and reject modified unversioned bytes without expanding
the production allowlist.

The [admission-rejection fixture](fixtures/ci-admission-rejection.yml) similarly
preserves blob `97e22a8f88bcdc9bbaef32cf9e7200dd6e5c43cd` for the
[exact historical admission-rejection regression](github.test.cjs). Changes to
the current workflow must not change that fixture or expand the
[production allowlist](policy.cjs) to satisfy the regression.

Source bytes are read through the trusted repository Contents API at that exact
base and checked against the returned Git blob identity. Missing source, malformed
metadata, an unknown unversioned blob or an unknown/duplicate schema holds the lane
and requests cancellation, even when the run and every returned job are completed.
API/authentication failures propagate while ownership remains held. No schema
fallback, cancellation acknowledgement or successful aggregate establishes the
physical termination of an absent producer. This applies equally to failed,
cancelled, stale, already-merged and already-retiring tickets, without changing
state version, observation deadlines, pagination, attempt/ticket or App trust.

The [coverage-comment workflow](../../.github/workflows/coverage-comment.yml)
checks the source run's latest-attempt jobs before downloading its report.
A skipped portable job with successful queue-policy feedback identifies a
policy-only PR run: reporting stops with an explicit notice, without downloading
an artifact or changing the existing coverage comment. Full ordinary PR runs and
validated queue candidates require exactly one completed, successful portable
job. A missing report from those runs still fails; unknown, duplicate or
unsuccessful producers are not treated as policy-only skips. This decision uses
the source run's evidence rather than the current queue-mode variable, which may
have changed since that run. Candidate ticket and current-head checks remain
required, and the 100% line/branch coverage gates are unchanged.

Before attestation, the controller rechecks head, base, queue intent, reviews
and ancestry. Its merge request includes the expected head. Strict up-to-date
protection handles a main-advance race at the final GitHub merge boundary.
The App has no main-protection bypass. The controller never creates or approves
PR reviews. GitHub includes review APIs in the App's **Pull requests: read/write**
permission; there is no separate App permission to disable those APIs. Keep its
private key restricted to trusted main workflows rather than treating the
permission name as a review-approval restriction.

## Controller feedback

Open a **Merge queue controller** run and expand **Reconcile selected candidate**
for live progress. The run's job summary retains the same observations and
decisions, with its trigger and final outcome, so `idle` is not the only feedback:

- Protection verification, current main, active phase and blocked-revision counts.
- Inspected PR counts, skip reasons (draft, foreign source, missing squash
  Auto-merge, missing live enrollment, quarantined revision, unknown mergeability
  or unmet reviews), and priority/PR-number selection order.
- Reserved PR/head/base/ticket, branch-update and CI-dispatch requests, matching
  run identity/status and every required job's match count and conclusion.
- Stale-candidate retirement, cancellation and lane-hold reasons, exact-evidence
  revalidation, protected merge requests/confirmation and controller continuation.

Routine progress uses informational log lines; existing exceptional conditions
remain warnings. Failures propagate to Actions, while the summary retains
observations made before the failure and labels the outcome `failed`.
It does not turn partial work into successful admission. Summaries require the
step to reach its finalizer; runner termination or failure before reconciliation
starts may prevent them.

The trace contains scheduling metadata, not PR titles, bodies, arbitrary label
text, event payloads, credentials or API response dumps. Logs and summaries are
diagnostics, never merge authority. Queue state/schema, eligibility, permissions,
required checks and retry/cancellation behavior are unchanged.

## Durable scheduling and cancellation

### Merge confirmation and native Auto-merge

The controller records merge intent before attesting checks or issuing its single protected merge request. Native GitHub Auto-merge can complete concurrently; one unmerged observation after a refused request is not a final outcome ([controller](controller.cjs), [race regressions](controller.test.cjs)).

- An explicit refusal waits up to ten minutes for merge visibility, without repeating the merge API. Confirmed merging preserves the validated check results and releases only after the matching workflow is terminal.
- If the PR remains unmerged at that deadline after an explicit refusal, only **Merge admission** fails and the revision is quarantined. Passed build/test/installer checks remain evidence of their actual results, not permission to bypass admission.
- Lost responses, malformed responses and unobservable or nonterminal validation are uncertain outcomes. At the deadline, merge admission reports uncertainty while the lane stays held; no blind retry, second candidate or automatic success is produced. Inspect the source, actual PR and workflow before deliberate withdrawal or renewal of queue intent.
- Source, base, enrollment and review changes retain their fail-closed behavior. Once merge intent exists, cancellation/refusal reporting changes the admission gate rather than rewriting passed validation jobs. Existing independent protection and expected-head checks remain mandatory.

The version-1 state accepts the previous phases and additive `merging`, `merge-refused` and `merge-unconfirmed` phases with a canonical `mergeStarted` timestamp and complete dispatched check identities ([validation](policy.cjs)). A rollback to an older controller cannot interpret those new phases and fails closed; it is not permission to rewrite durable queue state.

The preserved timestamp on an already-completed GitHub check does not date a later conclusion update. Use the actual controller log and merge record to diagnose a race, not `completed_at` alone. This fix applies to future reconciliation and does not rewrite historical check results.

Only short controller reconciliations are serialised; no runner waits for CI.
Pending wakeups may coalesce safely because each run rereads GitHub state.
PR changes (including label additions/removals), Auto-merge changes, main pushes
and CI completion wake it. A PR merge already wakes the controller through
`pull_request_target: closed` and the push to `main`; merging does not wait for
the timer. Events are hints to reread live state, not authority to select or merge
the PR named in a payload.
A five-minute scheduled reconciliation provides recovery from missed events and
eventual review/mergeability updates; GitHub may delay schedules under load.

### Admission rejection without candidate execution

GitHub may leave a single skipped, unexpanded matrix job when **Admit CI
execution** fails before version resolution. The controller can retire that
first-attempt failed run only against an exact reviewed workflow blob and a
complete, unique terminal job plan. Every candidate-executing producer,
publication job and the literal matrix placeholder must be skipped; only the
known guard-only aggregates may have failed or been cancelled. Missing, extra,
duplicate, nonterminal, wrong-source or wrong-attempt observations retain the
lane ([policy](policy.cjs), [adapter](github.cjs), [regressions](controller.test.cjs)).

This is non-execution evidence, not successful validation. Required producer
inventories, merge checks and coverage thresholds are unchanged. A successful
admission, an expanded matrix, or a new workflow blob cannot use this exception.
Workflow changes require a separate review of their execution gates and an
explicit update of the pinned rejected-admission inventory. Both singleton and
dormant batch retirement use the same classifier; a rejected batch still needs
new full singleton proofs before any member can merge ([batch](batch.cjs)).

The controller loads from protected main. Publishing a retirement repair does
not itself update an already-running controller or authorize editing durable
queue state. An existing held lane can therefore block adoption of its own
repair; that requires a separately reviewed operator decision, not a bypass or
an automatic state rewrite.

One reconciliation quarantines up to ten newly selected conflicting revisions
in priority/PR-number order and continues looking for a runnable candidate,
rather than stopping at the first conflict. Previously blocked head/enrollment pairs do not
consume this selection budget. Each new reservation has its own ticket; at most
one full candidate workflow is dispatched.

After confirming a merge and checkpointing the released lane, the controller
performs exactly one more reconciliation in the same run. It rereads protection,
durable state, current main, PRs, enrollment, priorities and reviews before
reserving the next candidate. This removes a controller-run startup from the
merge-to-next-build path without waiting on a runner for branch updates or CI.
The original reconciliation outcome remains the run outcome; **Post-merge handoff
finished** records the follow-up outcome in the log and summary. An empty queue
stops, an updating candidate waits for events, and at most one candidate build
is dispatched. Follow-up failures propagate with the released lane or next
dispatch intent already persisted, so recovery cannot blindly repeat a merge
or dispatch.

After failed validation or completed retirement, or reaching the selection
budget with another selectable PR (including during a post-merge handoff), the
controller checkpoints state and explicitly dispatches `merge-queue.yml` on
trusted main using its existing App identity. This continuation does not dispatch
candidate CI or carry candidate inputs. It avoids relying on delayed scheduled
wakeups to advance an empty lane. Idle, updating, validating and still-retiring
lanes do not self-dispatch, so an exhausted blocked queue stops rather than
creating an idle run loop. A continuation failure propagates; persisted state
remains authoritative and later events or the schedule can recover it.

### Diagnosing throughput

Compare ticketed **Merge queue PR #...** CI runs, not the short policy-only
**CI .../merge** runs. Measure a candidate's creation-to-completion time,
completion-to-next-candidate creation gap, and merge-to-next-candidate gap
separately. Inspect job timestamps to identify the critical path; the lane
cannot advance while any required candidate work is nonterminal.

A snapshot of 26 completed candidate runs on 26 October 2026
(09:38-20:36 UTC) showed a mean duration of 23m52s, a range of 17m56s-31m03s,
and 10 failed attempts. The 25 intervening gaps were 54s-3m58s, with a median
of 1m23s: they were not five-minute timer waits. For example,
[PR #527's candidate](https://github.com/roryprimrose/Kora/actions/runs/40390027511)
took 26m35s, with its `ui-platform` Windows shard taking 25m52s.
[The next candidate](https://github.com/roryprimrose/Kora/actions/runs/40394163932)
was created 81s after completion, or 61s after #527 merged.
These are pre-change observations, not a benchmark or a promise of future
runner latency. Same-run handoff removes one startup, not the much larger cost
of long-running validation and failed attempts. Main release builds remain
independent and may overlap candidates; their execution is not an idle queue gap.

On 31 October 2026, 12:39-21:39 UTC, 17 completed candidates produced three
merges and 14 cancellations. Twelve cancelled candidates had a confirmed
30-minute UI job timeout; cancelled runs consumed 413 minutes. This is failed
validation occupancy, not a controller handoff delay.

The Windows workflow builds exact-source test modules once, then scales out
to twelve independent standard Windows hosted test jobs. Consumer jobs perform
no restore or compilation; artifact revision/file hashes bind their binaries to
the admitted source. Native-UX alone retains buffering; other jobs are synchronous.
Theory rows stay together, and legacy logical ownership/`HostedShard` bootstrap
assignments remain available while trusted main still uses predecessor scheduling.
The original five Windows job names are lightweight fail-closed aggregates,
not additional test runners. They join the producer and all twelve consumers.
The protected Windows aggregate validates twelve reports against complete
unfiltered source discovery, including exact method and theory-case identities.
The portable required check joins
independent test/coverage and exact-source payload producers. The installer can
start after payload verification while portable tests continue; release still
requires every existing protected check. The portable aggregate requires both
producers to succeed; admission job identities stay compatible with predecessor
workflows so completed runs can still retire during rollout. No test category
is deferred.
See the [development checks](../../CONTRIBUTING.md#development-checks) for
partition discovery, execution-evidence checks and measured timing projections;
shared-runner timing is not guaranteed by those projections.

[The initial repair candidate](https://github.com/roryprimrose/Kora/actions/runs/41255145262)
still exhausted definitions' 30-minute job budget. Portable, installer and four
other Windows jobs succeeded, but only 842 of 857 conversation cases were
recorded before cancellation. The subsequent finer partition retains all
unfinished methods and all protected gates. Partial passing TRX counters do
not establish a complete candidate pass or justify bypassing admission.
[The nine-process same-runner candidate](https://github.com/roryprimrose/Kora/actions/runs/41288388916)
also exhausted both UI jobs. The next repair distributes those same partitions
across four synchronous hosted runners, consuming spare storage-job capacity
without adding check identities or changing the trusted-main CI matrix. Timing
regressions sum serial UI work on each host rather than treating extra processes
as extra CPU capacity. This remains a scheduling repair, not hosted pass proof.
[The cross-host candidate](https://github.com/roryprimrose/Kora/actions/runs/41292927647)
completed definitions and platform but timed out storage-hosted conversation
work. Controlled-clock repairs address isolated approval stalls; the twelve-job
pipeline scales out further rather than requesting bigger runners. Its new
workflow orchestration becomes active only after integration because candidate
dispatch uses trusted main. Keep all existing `JOBS` names compatible with old
runs; producer/consumer completeness is enforced through those aggregates,
not by adding new required names that strand predecessor retirement.
The 12-15 minute end-to-end target remains unverified until complete hosted
results include build, transfer, runner scheduling and slowest-partition time.
Local repair validation also exposed approval scenarios mixing fixed domain
time with wall-clock cancellation or queue expiry. Those deterministic fixtures
now share their clock, and lifecycle review cancellation uses the controller's
injected clock with the same one-minute production budget. The default fixture
and production clocks remain system time; no gate or job deadline is extended.

Controller workflow success means reconciliation completed, not that a PR
merged. Inspect its `Merge queue:` outcome and the durable active/blocked state.
Conflicts still require a contributor repair; the controller does not resolve
them or retry the same failed head/enrollment automatically.

State is a versioned JSON file on `automation/merge-queue-state`, not an artifact,
runner cache or local file. Updates are linear, compare the observed parent and
never force-update that branch. GitHub retains the state commit history.
Unknown/corrupt state and permission errors fail explicitly.

The reservation is persisted before dispatch. A lost dispatch response is not
retried blindly. A run that does not appear within ten minutes fails the
candidate, but does not free the lane: an eventually consistent run listing
cannot prove the dispatch never happened. The lane stays held until its run
appears and terminates, or an administrator performs reviewed recovery.
After such a failure the same head requires explicit re-enrollment.
An interrupted reservation can resume; a reservation with no recorded dispatch
can safely retire without waiting for a run.
Head/base/intent changes cancel only the matching candidate run. The lane stays
occupied until that run terminates, so cancellation cannot accidentally admit a
second expensive build while the first is still running. Reruns/duplicate ticket
runs are not accepted as fresh evidence.
An already-merged PR uses the same quiescence rule: its recorded dispatch must
be observable and terminal before the lane is released. Recovery cancels any
remaining matching run without overwriting previously successful check evidence.

Main release builds and explicit version-tag/manual builds retain their existing
behaviour; candidate dispatch does not cancel them. This initial change does not
reuse feature-version artifacts as main release bytes, batch multiple PRs, or
promise that hosted runner allocation has no latency.

## Relationship to PR #335

[PR #335](https://github.com/roryprimrose/Kora/pull/335) improves the work *inside*
one build. Queue admission controls *which PR* receives that work. They are
complementary, not alternative schedulers.

Preserve its parallel portable test-solution invocation, per-module TRX and
coverage reports, 100% merged line/branch threshold, five disjoint Windows shards,
scoped native-UX buffered/file-copy fixture profile, and existing deadlines.
The queue does not edit those test runners, fixture profiles or portable test
commands. Exact required job/shard names remain unchanged.

When integrating its workflow edits, retain both the candidate concurrency key
and the separate `-test-comparison` suffix for explicit timing comparisons.
Comparison dispatch remains opt-in and outside queue evidence. Admission permits
its explicit comparison inputs, but a candidate ticket cannot also request a
comparison. Candidate source-ref/manifest plumbing must also be retained in any
new job consuming candidate output. No unmerged PR #335 code is copied into this
implementation or treated as an implementation prerequisite.

A three-way workflow rehearsal against #335 at
`b72e929cf76fe7788f171cb7dc572377389b12fe` found three mechanical overlaps:
dispatch inputs, concurrency, and adjacent engineering-contract test steps.
Retaining both sets of inputs and test steps, and combining the candidate key
with the comparison suffix, passes the queue workflow contracts and actionlint.
The rehearsal does not qualify runtime performance or merge #335 into this PR.

## One-time installation and gated rollout

These are administrator operations, **not performed by the implementation PR**:

1. Merge the implementation with the queue disabled, using existing full CI.
   Integrate any intervening PR #335 workflow edits without reverting them.
2. Create a GitHub App, installed on **only this repository**, with:
   - Actions, Checks, Contents, Pull requests: **read/write**.
   - Administration: **read only**, to verify branch protection.
   - Metadata: the required read permission.
   Do not give it bypass rights on main or use it to approve PR reviews.
3. Create a `merge-queue` Actions environment restricted to deployment from
   **main only**. Put the App private key in its environment secret
   `KORA_MERGE_QUEUE_PRIVATE_KEY`, never a repository-wide secret.
   Define repository variables `KORA_MERGE_QUEUE_APP_ID` (numeric App ID) and
   `KORA_MERGE_QUEUE_CLIENT_ID`. Keep `KORA_MERGE_QUEUE_MODE=disabled`.
4. Protect `automation/merge-queue-state` against ordinary updates, force-push
   and deletion. Permit the queue App to create/update this state branch;
   any exception is scoped to **that branch**, never main.
   With repository rulesets, scope the App exception to creation and update.
   Put deletion and non-fast-forward restrictions in a separate active ruleset
   with no bypass actors, so the App cannot bypass those history safeguards.
5. Pause enrollment and let existing expensive PR runs drain. With no ready
   auto-merging PRs, configure the following main checks to require the queue
   App as their expected source, retaining strict freshness/admin enforcement:
   - `Portable build, tests, coverage, and package`
   - `Windows integration tests`
   - `Build WiX MSI and custom Burn setup`
   - `Merge admission`
   Keep review, thread resolution, squash-only and linear-history rules.
   Bootstrap may require GitHub's checks API to register the App check names
   before selecting their expected source. Bootstrap checks must be non-success
   and must not be placed on a candidate intended to merge.
6. Set `KORA_MERGE_QUEUE_MODE=enabled`, then manually dispatch **Merge queue
   controller** on main. It verifies every required App source and strict/admin
   enforcement before performing queue mutations.
7. Enrol one bounded canary PR. Verify its head/base/ticket, all real CI jobs,
   candidate-head App attestations, coverage comment and protected squash merge.
   Verify stale-head/main cancellation and lane release using disposable PRs.
   Only then enroll the remaining backlog.

Local deterministic tests are implementation evidence, not a claim that App
installation, GitHub permission configuration or this hosted canary has happened.

## Recovery and rollback

- Dispatch the controller on main to reconcile current state. Never manually
  make an admission check successful or reuse another ticket/run.
- For API/authentication errors, inspect the failed controller run and repair
  the exact App/environment permission. It does not fall back to a user PAT.
- For corrupt state or duplicate runs, stop enrollment and cancel known candidate
  runs before a reviewed state repair. Do not delete state to make a live lane
  appear idle.
- To roll back, first disable Auto-merge on queued PRs, cancel/drain candidate
  runs, and set the mode to `disabled`. Restore the original three required
  checks with **GitHub Actions** as source and remove the App admission
  requirement. Re-run ordinary PR CI before re-enabling native Auto-merge.
  Changing the mode alone intentionally does not make a PR mergeable.
- If the trusted-main controller itself needs repair, keep the App checks
  required while disabled-mode CI validates the repair PR. Before restoring
  ordinary protection, ensure other open PRs cannot qualify using only skipped
  queue-waiting checks. Merge the tested repair through ordinary protected CI,
  retain the durable state history, then repeat the App-source canary.

### Reviewed unobservable-dispatch recovery

A GitHub dispatch HTTP 500 or lost response can leave a recorded dispatch with
no observable run. The ordinary controller intentionally retains that lane:
elapsed time and an empty run listing do not prove physical quiescence. Do not
retry the dispatch, rerun an old ticket, automatically clear the reservation or
temporarily grant a client access to the state branch.

After an administrator has reviewed the exact failed dispatch, current candidate
activity and state history, the existing **Merge queue controller** can perform
one explicit `recover-unobservable-dispatch` operation. This is a privileged
reviewed exception for unresolved dispatch uncertainty, not automatic proof that
GitHub never created work. The caller must obtain separate administrator
authorization for that exact checkpoint/ticket; unavailable or automatic agent
responses are not consent. Merely having repository write permission, an
Auto-merge request or publication authority is insufficient.

Inspect the [state branch](https://github.com/roryprimrose/Kora/tree/automation/merge-queue-state)
and its commit history, the failed controller log and candidate workflow runs.
Read the exact checkpoint:

```powershell
gh api --hostname github.com repos/roryprimrose/Kora/git/ref/heads/automation/merge-queue-state --jq .object.sha
```

Only after review and explicit authorization, dispatch on main with the reviewed
commit SHA and ticket, not values silently refreshed by a client:

```powershell
gh workflow run merge-queue.yml --repo github.com/roryprimrose/Kora --ref main `
  --field operation=recover-unobservable-dispatch `
  --field "recovery_checkpoint=<reviewed-state-commit>" `
  --field "recovery_ticket=<reviewed-retiring-ticket>" `
  --field acknowledge_uncertain_dispatch=true
```

The operation requires a first-attempt manual run on main, explicit acknowledgement
and live repository **administrator** permission for the dispatching actor,
rechecked before the final run observation and write. The existing main-only
`merge-queue` environment supplies the same App token. Its branch policy is not
a reviewer approval gate; this operation does not install or change environment
reviewers, secrets, App permissions, protection or queue mode.

The workflow shares the ordinary controller's concurrency group. It verifies
main protection, the exact state checkpoint/ticket, `retiring` phase, recorded
dispatch and elapsed ten-minute observation budget. Merge-intent uncertainty is
excluded. Complete, consistent run pagination is required; API errors, duplicate
or invalid run identities, any observable matching run and nonterminal dispatched
work in the observation window reject recovery. Run metadata is reobserved
immediately before committing. If the ticket has an observable run, use ordinary
reconciliation and its existing exact-job/cancellation rules instead.

Recovery does not replace the current twelve-partition, exact-source artifact
and discovery-catalog pipeline or its fail-closed producer joins. The existing
required job names remain compatible with historical tickets; retain both the
recovery-routing and predecessor-retirement workflow regressions when integrating
controller changes. Observable runs with missing or unknown producer metadata
remain held by ordinary reconciliation; the administrator-only recovery operation
still rejects any observable matching ticket.

Recovery retains all blocked revisions, quarantines the old head/enrollment if
needed and clears only the reviewed reservation. A linear App-owned state commit
records the administrator, recovery workflow run, retired ticket and previous
checkpoint. Existing check results are unchanged; no successful admission check,
branch update, merge or candidate dispatch is produced. A late old ticket fails
the existing CI admission check once its reservation is removed or replaced.
This does not assert that previously admitted but unobservable work was absent;
reviewed uncertainty is why this operation remains administrator-only.

After exact checkpoint readback, recovery wakes ordinary reconciliation once.
Inspect the recovery summary and the next candidate's exact ticket/run. If
readback or continuation fails, inspect live state before proceeding: the
checkpoint may already have been committed, and the old inputs cannot replay
recovery. Scheduled reconciliation remains available; do not blindly retry.

## Deterministic validation

```powershell
node --test .\eng\merge-queue\*.test.cjs
.\eng\Test-WindowsIntegrationScheduling.ps1
```

The Node built-in runner needs no npm dependencies. Tests use synthetic GitHub
APIs and a temporary local version fixture; they do not mutate GitHub, install an
App, change branch protection, call providers or exercise physical devices.
Workflow contract tests accept `KORA_QUEUE_WORKFLOW_TEST_PATH` to test a
separately prepared combined workflow without changing the working copy.
Octokit's `paginate` returns normalized arrays for workflow runs, jobs and
check runs. Pagination callbacks and test doubles must use that array contract,
not the named object envelopes returned by individual REST requests. Job-page
callbacks also retain Octokit's normalized `total_count` metadata so partial
or changing page sets cannot qualify as complete observations.

The [historical admission-rejection fixture](fixtures/ci-97e22a8f.yml) retains
the exact reviewed workflow bytes for blob `97e22a8f88bcdc9bbaef32cf9e7200dd6e5c43cd`.
Its positive [adapter regression](github.test.cjs) does not follow edits to the
live workflow. Modified source bytes and schema markers alone still cannot
authorize retirement; the production blob allowlist is unchanged.

### Failed preflight and unexpanded matrix retirement

The [preflight rejection classifier](policy.cjs) separately recognizes the
reviewed workflow blob `c75173721c4131cf29d45482ea2edb92934070f1`, retained as
an [immutable fixture](fixtures/ci-c7517372.yml). It does not add that blob to
the historical admission-rejection allowlist or accept a schema marker as
no-execution authority.

For an exact owned first-attempt candidate whose workflow is physically
completed, the classifier requires successful `Admit CI execution` and failed,
cancelled or timed-out `Resolve build version`. All 20 exact-run job identities
must be observed terminal once: skipped expensive producers and the single
unexpanded Windows partition placeholder; the five Windows shard aggregates,
portable aggregate and Windows aggregate must be failed/cancelled/skipped,
never successful. The [adapter](github.cjs) authenticates the exact protected
workflow bytes and source before applying this no-fanout plan.

Only that complete evidence lets singleton/batch retirement or separately
authorized reviewed recovery join the work without expanded partition jobs.
The candidate still fails admission; no member merge, successful attestation,
qualification receipt or reused partial proof is produced. Running work is
cancelled promptly, but the lane is retained until both the run and every
owned job are physically terminal. Missing/duplicate/extra/wrong-source jobs,
later attempts, unknown conclusions, skipped/missing version evidence,
changed workflow bytes or a successful expensive producer do not match this
plan and cannot release the lane ([singleton regressions](controller.test.cjs),
[batch/recovery regressions](batch.test.cjs), [adapter regressions](github.test.cjs)).

Workflow changes require a separately reviewed exact-byte no-fanout contract;
normal retirement does not modify queue state administratively, dispatch CI
manually or infer quiescence from a deadline or cancellation acknowledgement.
