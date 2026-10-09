# Measured R02 Windows containment evidence

Status: **required network-denial validation unproven; production profiles disabled**.
This is a feasibility snapshot, not release acceptance or a general sandbox claim.

## Provenance / reproducibility

- Final run: 2026-10-05, environment recorded at `00:15:09.6066175 UTC`.
- Compiled harness revision tested: `a7c0144`. The following evidence commit
  changes only exporter whitespace handling, documentation and measured data,
  not the compiled harness or fixed PowerShell probe.
- Approved R01: `7d5e6a3` / PR #19, present before and after both rebases.
- `git fetch origin` and `git rebase origin/main` ran before initial work and
  again immediately before the final build/trials. Main remained at `7d5e6a3`;
  no conflicts or published-history rewrite occurred.
- Windows 11 Enterprise x64, build `10.0.26300.0`. This snapshot does not assert
  that build 26300 satisfies the currently-supported-release reference gate.
- AMD RYZEN AI MAX 385 w/ Radeon 8050S, 16 logical processors;
  physical memory `25,530,408,960` bytes. Speech/hardware acceptance is out of scope.
- .NET SDK `10.0.401`, runtime `.NET 10.0.12`; PowerShell `7.6.6`.
- Host: non-elevated, medium integrity `S-1-16-8192`.
  Workers/children: capability-free AppContainer, low integrity `S-1-16-4096`.
  Worker SID is a temporary synthetic profile SID; personal user SID is redacted.
- Runtime was copied from the existing Microsoft PowerShell installation into
  owned scratch and granted RX only. No download/installation or global change.

Run [the fixed reproduction command](../README.md#reproduce). To produce a
committable snapshot after a run, without committing personal account identity:

```powershell
.\experiments\r02-containment-proof\Export-Evidence.ps1 `
    -Source '.\experiments\r02-containment-proof\artifacts\trial-01' `
    -Destination '.\experiments\r02-containment-proof\evidence\new-snapshot'
```

The source must be a complete runner-produced evidence directory and the
destination must not exist. The exporter preserves native codes, synthetic
container SID, process IDs, binary hashes, timings and actual outcomes.
No raw credential values, external addresses, private files or script arguments
are present in the committed receipts.

## Final validation

| Validation | Actual outcome |
|---|---|
| Focused Release build | Passed; 0 warnings, 0 errors |
| Deterministic receipt/classification assertions | 5/5 passed (supplementary, not OS proof) |
| Actual OS assertions | 63/71 met; 8 network-denial assertions unproven |
| Strict proof exit | **2**; not silently converted to success |
| Cleanup | Synthetic credential, temporary profile and exact scratch tree deleted |
| Production profile certified | **False** |

The eight unmet assertions are the .NET and PowerShell loopback/owned-interface
network-denial assertions for the complete and cancelled AppContainer trials.
Connections timed out, producing `Unknown`, not `Denied`. Baseline connections
succeeded at both owned endpoints. No real network denial code is claimed.

## Actual denial evidence

The correlated [receipts](measured/trials.json) contain these actual observations:

- AppContainer protected read/write/delete and **native `MoveFileExW` rename**
  return Windows access-denied **5**; all protected bytes/entries remain intact.
- Hard-link aliases in the allowed directory and `..` traversal writes also
  return **5**. There is no application path-rejection shim.
- Credential Manager `CredReadW` of the single host-created synthetic target
  returns **5**, whereas the same-user Job-only control reads it successfully.
- `CREATE_BREAKAWAY_FROM_JOB` returns **5** in both profiles. Ordinary contained
  child and grandchild creation succeeds; their actual container SID matches
  the worker's temporary SID and they remain non-elevated.
- Fixed encoded PowerShell executes under the AppContainer and reports protected
  write HRESULT **`-2147024891` / `0x80070005`**. Its network outcomes remain unknown.
- Read-only `OpenInputDesktop` fails with **5** in AppContainer. This is **not**
  a lock/switch/shutdown trial or proof that a computer-control action is usable.
- Managed `File.Move` reports file-not-found **2** for the inaccessible source.
  That result remains `Unknown`; the distinct native rename probe supplies
  actual access-denied evidence instead.

The Job-only control really mutates/deletes/renames its synthetic protected
fixtures and reaches the credential/listener. Thus separating a user process
and tracking its lifetime is demonstrably insufficient for restricted execution.

## Lifecycle and effect outcomes

| Trial | Lifecycle | Effect outcome | Tree stopped | Shutdown (ms) | Protected fixtures unchanged |
|---|---|---|---|---:|---|
| Job-only complete | Exited (0) | Observed | Yes | 27.62 | No (intentional control) |
| AppContainer complete | Exited (0) | Observed | Yes | 15.58 | Yes |
| AppContainer cancellation | Cancelled | Observed | Yes | 14.39 | Yes |
| AppContainer lost receipt | TimedOut | **Unknown** | Yes | 14.58 | Yes |
| AppContainer malformed receipt | Aborted | **Unknown** | Yes | 15.26 | Yes |

Shutdown was measured from closing the last owned Job Object handle, using
stable handles to every process reported in its PID list. Descendant identities
were actually observed, not simulated. The two unknown-outcome trials wrote a
synthetic effect marker before withholding/corrupting their receipt. Process
termination, even with observed exit code zero, never becomes an effect-success
or rollback claim. The malformed receipt records an explicit verification error.
These are individual feasibility trials, not a 30-trial performance acceptance.

## Retained artifacts and remaining gates

- [Environment, actual identities and binary hashes](measured/environment.json)
- [Full correlated trials, native errors and child token observations](measured/trials.json)
- [Strict assertion results, including unmet requirements](measured/verification.json)
- [Build/receipt/proof status](measured/validation.json)
- [Cleanup receipts](measured/cleanup.json)

Use the [identity/deployment recommendations](../README.md#required-identities-and-deployment-assumptions)
and [unsupported profiles](../README.md#recommendations--unsupported-profiles).
An independently protected normal-host deployment, complete protected-resource
aliases/remote identities, auditable network denial, executable dependency
admission and exact controlled computer actions remain required before exposing
the affected capabilities. No existing installation ACL, global firewall policy,
CI, production manifest/composition or canonical design document was changed.
No privileged/disruptive trial, lock, shutdown, arbitrary script, bundled skill
or final grant schema was introduced. These gaps block capability acceptance,
not publication of this partial research with the
[outstanding-testing handoff](../README.md#outstanding-testing-checklist).
Normal repository checks/reviews remain mandatory; no measured failure or
Unknown outcome is relabelled as success to publish the PR.

The snapshot above describes the initial proof scope. Subsequent documentation
changes now record [canonical outcomes and next gates](../../../Design/Security_Data_Flows.md#r02-windows-containment-outcomes),
the [open D-013 decision](../../../Design/Decision_Register.md#d-013-windows-worker-and-deployment-containment)
and [staged delivery work](../../../Design/Implementation_Roadmap.md#r02-windows-containment-follow-up).
They do not alter these measured results or enable production execution.

Later rebases and validation runs are publication follow-ups, not changes to
the original snapshot's source/OS observations or its initial draft disposition.
The [shared deferred-validation register](../../../Design/Deferred_Validation.md)
separates safe reruns from separately approved interactive/privileged acceptance.

## Post-Rebase Publication Validation

On 2026-10-05 the branch was rebased onto `1d6ed77` (merged speech proof #25)
without conflicts. Approved R01 `7d5e6a3` remains an ancestor. The user explicitly
approved one guarded `--force-with-lease` publication of this already-published
branch; that approval does not authorize an unguarded push or future rewrites.
The required final fetch/rebase was repeated before validation and was a no-op.

The existing fixed scratch proof was rebuilt and rerun at source revision
`076e038`, with environment recorded at `04:14:31.0116479 UTC`.
These are separately retained results, not edits to the original snapshot:

| Validation | Post-rebase outcome |
|---|---|
| Isolated proof Release build | Passed; 0 warnings, 0 errors |
| Deterministic receipt/classification assertions | 5/5 passed |
| Actual OS assertions | 63/71 met; the same eight network-denial assertions remain unproven |
| Strict proof exit | **2**, preserved |
| Tree shutdown | All five tracked trees stopped; individual shutdown times 13.56-27.24 ms |
| AppContainer protected fixtures | Unchanged in all four AppContainer trials |
| Effect without valid receipt | Lost/malformed receipt trials remain **Unknown** |
| Cleanup | Synthetic credential, temporary profile and exact scratch directory deleted |
| Embedded documentation build/tests | Release build passed; 9/9 focused tests passed |
| Production/interactive acceptance | Not certified; deferred C01-C07 remain required |

- [Post-rebase identities, runtime and build hashes](post-rebase/environment.json)
- [Post-rebase correlated trials and native observations](post-rebase/trials.json)
- [Post-rebase strict assertion results](post-rebase/verification.json)
- [Post-rebase proof/build classification](post-rebase/validation.json)
- [Post-rebase cleanup receipts](post-rebase/cleanup.json)

Only existing non-elevated owned-scratch checks and focused documentation tests
were repeated. No speech capture/playback, Kora installation/launch, privileged
tracing, installed ACL/global policy changes, lock, shutdown or restart trial
was performed. The [shared register](../../../Design/Deferred_Validation.md)
and [C01-C07 checklist](../README.md#outstanding-testing-checklist) hand those
gates to a later separately approved interactive session. Publication/merge of
this partial evidence through normal repository checks does not close them.

## Unattended preparation, 2026-10-09

This separately retained **file-only preparation** does not update either
measured OS snapshot above or the 2026-10-08 preparation receipts:

- [Clean-source preparation pins](preparation-2026-10-09.json), generated by
  `Invoke-Proof.ps1 -PrepareOnly` at `2026-10-09T11:01:23.6197356+00:00`,
  revision `e0692f438a058de0a20021b3420a981849706dbf`.
- [Bounded validation summary](preparation-validation-2026-10-09.json):
  one focused locked restore for missing assets, Release build with zero
  warnings/errors, five deterministic assertions passed, preparation exit `0`.
- No OS trial, administrator observer, AppContainer/credential creation,
  local-network trial, security-policy change, elevation, installation or
  computer-control operation. Trial cleanup is **Not run**, not measured success.

The preparation receipt is already path-free and is preserved without changing
its raw values. Only source filenames and hashes, not machine paths or binary
contents, are published. Raw build/restore output remains in local session
evidence. Source and compiled-byte pins describe that exact clean-source run;
subsequent documentation commits/rebases do not retarget them.

Historical live results stay `63/71`, exit `2`; network attribution remains
**Unproven** and no production profile is certified. The
[unattended preparation handoff](../README.md#unattended-preparation-repeat-2026-10-09)
separates mergeable partial preparation from the unchanged C01-C07 and
W1-W4/R11/R16/R17 capability gates.
