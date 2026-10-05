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
or final grant schema was introduced. Keep the evidence PR draft and do not
enable squash auto-merge while these required validations are blocked.
