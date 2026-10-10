# RT2 unattended replay — 2026-10-09 UTC

**Overall qualification: BLOCKED. Fresh .NET tests: NOT RUN.**
The two PowerShell receipt-locale contracts pass; these do not qualify RT2.
The retained 2026-10-06 receipts and their 45/45 RT1 and 20/20 RT2 counts are
historical evidence, not results of this replay.

## Executed scope and source identity

Execution used baseline `e0692f438a058de0a20021b3420a981849706dbf` on
`proof/unattended-20261009-rt2`, installed SDK 10.0.401 and PowerShell 7.6.6.
The checkout was clean before preparation. Later commands had an untracked,
uniquely owned staging directory but no tracked source edits. All 48 checked
original RT1 root/evidence and RT2 evidence files remained byte-identical
through execution. Documentation and this new receipt were added afterwards.

Each shell started in the isolated proof worktree. Process-local `TEMP` and
`TMP` were set to that worktree root before invoking the unmodified recipes,
so staging and NuGet scratch stayed proof-owned. No machine policy changed;
the SDK source, build recipe, package lock and native pins were not edited.
Only missing proof dependencies were restored with the existing locked recipes.
No global tooling, live account/provider, privileged observer, native runtime
trial, elevation or production capability was used.

## Actual commands and outcomes

Scripts below were invoked with `pwsh -NoProfile -NonInteractive -File`.
`<owned-RT1-stage>` denotes the actual unique staging directory, redacted from
the committed receipt; raw logs retain the original path.

| Command | Exit | Fresh result |
|---|---:|---|
| `.\experiments\r02-runtime-lifecycle-proof\Prepare-Fixture.ps1` | 1 | Source restore/pack succeed; fixture locked restore rejects SDK content with NU1403. Preparation does not complete. |
| `.\experiments\r02-runtime-lifecycle-proof\Test-ApprovedInputs.ps1` | 1 | Historical RT1 `README.md` digest differs from the current baseline; guard fails closed. |
| `.\experiments\r02-runtime-lifecycle-proof\Test-ReceiptContracts.ps1` | 0 | **2/2 PASS**, en-AU and en-US ISO UTC receipt contracts. |
| `<owned-RT1-stage>\Test-Reproduction.ps1` | 1 | Separate clean-source restore/pack succeed; package bytes differ from the primary build. |
| `dotnet build <owned-RT1-stage>\ControlProof.csproj --configuration Release --no-restore` | 1 | NU1403, zero warnings and one error; no valid test build. |
| `.\experiments\r02-runtime-lifecycle-proof\Run-Observations.ps1` | 1 | Stops at the same approved-input guard before RT2 build/tests/trials. |

Six top-level validation commands ran. Their nested .NET commands were three
locked restores (two SDK successes, one fixture rejection), two successful SDK
packs and one rejected staged RT1 build. The RT1 test command was not invoked:
there was no approved package or valid build. Zero of its 45 tests and zero of
RT2's 20 tests executed; no TRX was generated. No runtime or positive-control
trial started.

The runner's documented exit **2** means successful tests followed by
**BLOCKED qualification**, never a test failure or production pass. That path
was **not reached** here: actual exit **1** records a prerequisite rejection,
not failed .NET tests.

## Exact blockers

| Artifact | Approved SHA-256 | Fresh SHA-256 |
|---|---|---|
| SDK package | `0b609b73c868099d64e7d5320b927d760d528756ab7c5db0b5692f5dccbf9b3f` | `3bbb5e12c3a48904b36d086296e5f63009f2cbdeceacbdb0937952dc47fcbd92` |
| Primary SDK assembly | `afb0715225f794d1b663ef72da1336b44b665e50b330087e1356380a5eac10d6` | `e4fdff721ef41c49c48ac8a6ad0b066073d8a25d7afe366b098687b260b6d1f1` |

The independent package is
`3aff6e89b2335043d75d1a90986de947689cba0bcb2fdc25d210fdfd4583e74d`;
its assembly is
`b64272d2a30345c76a597b82b0d1dacde836e467d413f561d60090b71e726330`.
Neither matches the approved bytes or the primary build. The root cause is
not established. The existing reproduction-maintenance concern remains open;
there is no claim of equivalent IL, no cache substitution, no pin update and
no use of `-AllowFailedIndependentReproduction` or `-UpdateFixtureLock`.

The native launcher and payload match their existing approved hashes.
The historical approved-input receipt expects RT1 `README.md` hash
`c7f0900a37978a84e49b0f0f2a823a617d8c1fa94985aa1e62ba87aa5859c556`;
the baseline file has
`21c85ab2490dfc8577ee599938c11b5331862fc12aa3ed8efb8751fd1c978fdb`.
This is the only historical-input mismatch found, and it predates execution.
The guard and historical receipt are left intact; this run does not authorize
refreshing them.

## Retention, cleanup and handoff

The [new receipt](evidence/unattended-20261009.json) binds command timings,
exit codes, exact bytes, raw artifact digests and cleanup. Raw unredacted logs,
source/input manifests, failed restore assets, reproduction receipt, staging
metadata and a copy of the historical RT2 evidence were preserved in the
assigned session artifact store under `proofs\rt2`, outside source control.
Committed paths are logical/redacted; raw evidence was not rewritten.

The unique staged RT1 source/cache, RT2 staging metadata/cache and proof-owned
NuGet scratch were removed after preservation. Git status was clean again
before adding these documentation-only deliverables. No temporary code remains.
The worktree remains for the parent to remove.

No runtime observation counters, quiescence assertions, reference-floor SLOs
or fresh production test results are claimed. Existing all-path native
observation/prevention, optional surface and PV1 blockers remain unchanged.
Reproduction and approval provenance must be resolved without relaxing their
checks before a fresh 45-test/20-test lifecycle replay can proceed. The new
evidence is reviewable while qualification remains blocked.
