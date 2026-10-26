# GitHub-hosted merge admission

Kora is a personal-account repository. GitHub's native Merge Queue is not
available for this ownership model. This repository instead provides a bounded,
single-candidate controller running entirely on GitHub-hosted Actions.
It needs no local agent, workstation, external queue service or always-on runner.

**The controller is disabled until explicitly configured.** An absent or
`disabled` `KORA_MERGE_QUEUE_MODE` preserves ordinary PR/main/tag/manual CI.
An unknown mode fails CI admission rather than silently enabling another policy.
Do not enable the queue merely by merging its implementation PR.

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
The repository instructions and roadmap coordinator follow this mode-aware
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
   integration procedure. The roadmap coordinator retains its single remote
   lane in this mode. Do not dispatch redundant manual CI.
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

For coordinated agents, follow the [routine continuation rule](../../.github/agents/roadmap-coordinator.agent.md#routine-continuation-without-user-input):
publish the handoff through the registered [shared file mailbox](../../.github/agents/roadmap-coordinator.agent.md#shared-file-mailbox-protocol)
and release execution without asking the user whether to stop, wait or check
again. Atomic event publication/readback replaces routine native messages;
status alone is not notification. Retain source/PR ownership and the recorded
administrative inbox continuation or budgeted wake for admitted repair.
Pending hosted admission is not a user decision.

The coordinator, unlike a released feature worker, keeps [bounded scheduling ticks](../../.github/agents/roadmap-coordinator.agent.md#continuous-scheduling)
running while its host supports continuation. Before each refill pass, an
asynchronous assessment consults repository-wide open and recently landed PRs,
including work outside the coordinator, to avoid duplicate feature delivery.
Concrete conflicts route to the same admitted PR owner; unknown mergeability
does not authorize mutation. Parent questions never block this loop.
Multiple orchestration sessions join the same [repository dataset](../../.github/agents/roadmap-coordinator.agent.md#shared-dataset-ownership-and-recovery),
located under the registered coordinator workspace's ignored `.scratch` and
carried in launch/recovery handoffs, with one fenced scheduling leader and
observing standbys. Retain that workspace while referenced; do not silently
migrate a live legacy dataset. A successor adopts all nonclosed runs, owners,
capacity, pending dispatches and PRs; expired coordinator leadership does not
prove child or remote work stopped. [Transport retention/cleanup](../../.github/agents/roadmap-coordinator.agent.md#shared-dataset-retention-and-cleanup)
uses checkpointed, reference-aware bounded plans, not deletion when a chat ends
or a PR merges. New chats/runs do not reset capacity or authority.
These are agent instructions, not an installed scheduler or queue-controller
change: filesystem writes do not wake idle sessions, unavailable continuation
must be disclosed, and repair/merge authority is not expanded.

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
and CI completion wake it.
A five-minute scheduled reconciliation provides recovery from missed events and
eventual review/mergeability updates; GitHub may delay schedules under load.

One reconciliation quarantines up to ten newly selected conflicting revisions
in priority/PR-number order and continues looking for a runnable candidate,
rather than stopping at the first conflict. Previously blocked head/enrollment pairs do not
consume this selection budget. Each new reservation has its own ticket; at most
one full candidate workflow is dispatched.

After releasing a candidate lane (merge, failed validation or completed
retirement), or reaching the selection budget with another selectable PR, the
controller checkpoints state and explicitly dispatches `merge-queue.yml` on
trusted main using its existing App identity. This continuation does not dispatch
candidate CI or carry candidate inputs. It avoids relying on delayed scheduled
wakeups to advance an empty lane. Idle, updating, validating and still-retiring
lanes do not self-dispatch, so an exhausted blocked queue stops rather than
creating an idle run loop. A continuation failure propagates; persisted state
remains authoritative and later events or the schedule can recover it.

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
not the named object envelopes returned by individual REST requests.
