# MG1 measured evidence and disposition

Final fixture run: **2026-10-06 08:47:31 UTC**, after the compatible dependency
refresh, rebase onto main `90d8f48` (#37/#38/#36) and monotonic timer-wake fix.
[Disposition](evidence/disposition.json), [actual runtime rows](evidence/runtime-results.json),
[released RT1 regressions](evidence/rt1-released-regression.json),
[input identities](evidence/input-verification.json) and
[root validation](evidence/repository-validation.json) retain exact UTC
timestamps, hashes, counts and configured limits.

**MG1 Pass: separately user-approved released SDK 1.0.16 / unchanged RT1
native 1.0.90 minimal HTTP/stdio only.** Original source-built-profile
reproduction is Blocked; historical RT1 and Node remain unchanged.
No production, account, runtime all-path observation or physical computation
termination acceptance follows.

## Actual .NET observations

| Trial / requirement | Result | Actual observation |
|---|---|---|
| Selected complete context | Pass | Host tests accept 32768 bytes and reject 32769, counting serialized system/prompt/history and framing, not tokens/characters. |
| Complete actual runtime request | Pass | 32768 bytes forwarded once; 32769 forwarded zero times. Both public final receipts contain system/history and raw multibyte UTF-8. Calibration is non-forwarding; final request uses a fresh conversation and actual runtime framing, not an edited model body. |
| Complete typed output | Pass | Full JSON with framing and multibyte text: 4096 accepted; 4097 explicitly degraded with no proposal. Streaming remains provisional; no truncated proposal or ledger mutation. |
| Schema / hostile fields | Pass | Missing/malformed/duplicate/unknown fields, operation/target/revision/type errors rejected in host tests. Actual runtime/model approval, identity/correlation and foreign-target outputs degrade; effects zero. |
| Held inference deadline | Pass | Host returns at **15012 ms** from dispatch, configured 15000 ms with 1000-ms scheduling tolerance. Actual provider inference request is held. Abort request, acknowledgement and connection observations remain distinct; computation termination Unknown. |
| Stalled public send acknowledgement | Pass | Actual public HTTP callback is held; host returns at **15008 ms**. Provider arrivals zero; request quarantined. No acknowledgement wait extends the deadline. |
| Stalled native abort acknowledgement | Pass | Real inference held; only verified direct owned native child suspended using retained handle. Host returns at **15006 ms**, native abort receipt absent and connection still open. Child resumed in finally; actual SDK acknowledgement then observed, but quarantine retained. |
| Rolling hour including failures | Pass | **30 actual SDK/native 401/429/500 failures**, 30 provider arrivals, 61 final receipts overall: automatic subsequent attempts blocked. Attempt 31 denied at 3599999 ms; new explicit request allowed at 3600000 ms. Final arrivals **31**, all in distinct fresh management conversations. Deterministic time seam, real forwarding counters. |
| Single admission / cancel/retry races | Pass | 64 simultaneous host contenders admit exactly one in component test. Actual held management cancellation races 64 retry admissions: new admissions **zero**, provider request **one**, cancellation outcome at **107 ms**. Acknowledgement does not reopen the quarantined slot. |
| Deadline/cancel/retry race | Pass | Real held request; 64 contenders near deadline; new admissions zero. One terminal outcome **Cancelled at 14980 ms**, not a successful late proposal; one provider arrival. |
| Retry / tool continuation | Pass | Two distinct actual admitted requests forward exactly one each; four final receipts. Retry and forced tool continuation attempts blocked; management effects zero. |
| Two held executions + independent manager | Pass | Execution requests remain held while independent manager completes in **67 ms**. No execution markers/context/tools in manager or management context in execution; separate sessions/destinations. Not task slots, leases, account capacity or a reference SLO. |
| Admitted noncooperative effect | Pass | Execution-only owned scratch effect is Unknown at cancellation; abort acknowledged before write, then exactly **one** write observed. No second inference forwarded. No rollback/physical-stop inference or management tool authority. |
| Cleanup | Pass | Every final actual runtime row and all 45 regression rows report owned cleanup complete. Volatile stores cleared, owned listeners/SDK stopped, suspended child resumed and exact runtime scratch removed. Earlier failed-trial scratch separately cleaned by exact inspected paths. |

Socket closure is connection evidence only. A synthetic provider cannot
certify physical model computation, provider rollback or billing termination.
Unknown effect/computation state is not permission to replay, release a
quarantined inference slot or borrow an execution lane.
Local fixed choices/status/cancel are fixture API measurements, not production
UI/accessibility acceptance.

## Exact profile

Windows x64, OS `Microsoft Windows NT 10.0.26300.0`,
.NET SDK **10.0.401**, .NET runtime **10.0.12**.
SDK informational version:
`1.0.16+f8ae645902b74b62cd47aac1fd9b29adaec3aff2`.
Native runtime **1.0.90**, protocol **3**, minimal owned child stdio,
empty mode and synthetic loopback HTTP/SSE.

| Input | SHA-256 |
|---|---|
| Exact source archive | `a73f4f2d8cb1eb9fd5353d3867707094e1efb7581599669afb55c2dcf48a53c3` |
| Released SDK package | `c5518980b71d0ef0abd39ecdec7834898878222290c7fd1099a9040c8c5bf2ef` |
| Released net10 SDK assembly | `6ed0b19fd2f9cf525074830784bb255245f15b8f08a3d6aa78fb116be5ba668b` |
| RT1 native archive | `2b2c23816461f7616eb16c7836af7701d6683367029405d51054c4366d1d41c0` |
| RT1 native launcher | `7021cf1f25eb6b75e64c05e8f805747c62dd420dc8760659660291809e92603a` |
| RT1 native payload | `41ebb48367f96c984babde61afb68f22a85ab8fd9c037fdccf1778881c4bba05` |
| Historical Node results | `c1a40817bf37a125c08abe1eaa1dd31f5ec04fb959cc8ce3890cbfe95b9f0331` |

Fixture assembly/source/script/lock hashes are generated in the runtime
report; regression assembly/source hashes are in its separate report.
No SDK/native patch or Node bridge is used.

## Compatible package refresh

The user separately selected root solution and MG1 non-SDK updates in this
worktree, retaining vendor-selected native and major-version families.
[Package changes and retained candidates](evidence/package-updates.json)
record 15 unique updated identities across root, experiment and local tools:
Coverlet 10.1.0, SystemEvents 10.0.12, seven configuration/file-provider
packages 10.0.12, test-platform telemetry/MSBuild 2.4.1, MG1 AI abstractions
10.10.1 and logging/dependency-injection abstractions 10.0.12, and
ReportGenerator 5.5.11 (Apache-2.0).

All direct packages and local tools are current stable versions within scope.
Final full direct/transitive audits report zero known vulnerability and
deprecation findings. Nineteen distinct solution transitive candidates remain
intentionally at their parent-selected native/major/pre-1.0 families;
MG1 retains the two ApplicationInsights/Bcl.AsyncInterfaces major candidates.
No claim that every transitive package is newest is made. A supplemental
`--highest-minor` solution scan hit NuGet's internal "Sequence contains no
matching element" error; the full stable audit succeeded, and compatibility
selection used the recorded versions and parent metadata instead.

The independent experimental central package file also supplies the
MG1-owned RT1 derivative, so the 45 controls qualify the refreshed managed
closure rather than an old dependency graph. SDK/native bytes remain pinned.
Loaded managed assembly hashes are captured in the actual runtime report;
licenses and registry-independent locks were regenerated and checked.

## Full testing and CI checks

| Validation | Result | Count / boundary |
|---|---|---|
| Fresh released-profile RT1 controls | Pass | **45/45** actual SDK/native tests, zero skips. 44 selected-profile PASS rows and one expected rejected hook-only FAIL. Original historical evidence not overwritten. |
| MG1 host tests | Pass | **22/22**, including negative/hostile/exact-byte/admission/race, forward/backward UTC jumps and actual 31-request loopback forwarding. Rolling-hour admission uses monotonic timestamps, independent of wall-clock changes. These are not counted as SDK/native runtime cases. |
| MG1 actual SDK/native cases | Pass | **16/16**, zero skips/failures, with exact byte/deadline/window/isolation/effect evidence above. |
| Complete root Release solution | Pass | Includes managed setup; **0 warnings / 0 errors**, analyzers enabled. |
| Core | Pass | **289/289** |
| Application | Pass | **905/905** |
| Windows integration | Pass | **378/378** |
| Core/Application coverage | Pass | **100% line / 100% branch**, configured minimum 100/100. Only fresh final-run reports included. |
| Root dependency licenses / notices | Pass | Approved changed package versions reviewed and production notices regenerated; experiment excluded and separately reviewed. |
| Experimental closure | Pass | Management 23 packages (13 MIT / 10 Apache-2.0); host 19 (9 MIT / 10 Apache-2.0); exact locks and SDK/native review separate. |
| Version, publication policy, payload rejection | Pass | Existing CI scripts; publication uses isolated stub fixture, no remote release. |
| Windows x64/x86 publish | Pass | Exact payload manifests: **201 / 197 files**. Apphost/native SQLite machine fields **0x8664 / 0x014c**; hashes in validation receipt. Experimental SDK/runtime assets absent. Applications not launched. |
| Full local MSI ICE validation | **Blocked** | **WIX1105: system policy**. No elevation or policy change performed. |
| Explicitly ICE-skipped MSI/Burn build/inspection | Pass, inspection only | Exact MSI/UI/application digests, dual-scope/runtime/startup contracts and 201 application paths inspected. Not full release validation; no installation or app launch. NSIS not used. |

Total actual tests: **1572 root + 83 fixture = 1655**, zero final failures/skips.
The earlier dependency-batch checkpoint had 1348 root tests; the upstream
presence changes add tests, not sibling runtime counts imported as MG1 proof.
No Node, sibling or zero-test counts contribute.

## Failures retained, not reclassified

1. Original source-built recipe's locked restore failed NU1403:
   rebuilt package `3aff6e89b2335043d75d1a90986de947689cba0bcb2fdc25d210fdfd4583e74d`
   and first assembly `b64272d2a30345c76a597b82b0d1dacde836e467d413f561d60090b71e726330`
   differ from RT1's approved `0b609b73...` / `afb07152...`.
   A separate clean-source build also differed; cause not established.
   Original source/evidence/pins were preserved. The user separately approved
   released bytes and fresh RT1 tests; this is not inferred equivalence.
2. Host assertion initially required exact `JsonException` rather than its
   subtype; fixed the assertion and reran the whole host suite.
3. First 13-case runtime run passed 11 and failed two **teardown observer
   joins**, not the measured host deadline assertions. Added an owned
   observation-lifetime cancellation for SDK wait observers, separate from
   inference/abort/termination receipts; all 16 final cases pass. Two exact
   failed-trial scratch directories were inspected and removed.
4. Preparation initially called `git archive` from the fixture subdirectory
   with a root-relative pathspec; corrected own-root resolution and reran.
5. Full local MSI ICE remains Blocked by WIX1105. The explicit skipped
   inspection build is not a passing substitute.
6. Final inspection found that wall-clock jumps could advance the rolling
   attempt window. Admission now uses monotonic timestamps; added forward/
   backward wall-time tests and reran all 45/22/16 fixture tests successfully.
7. First post-rebase run passed all root/coverage and 45/22 controls, but one
   of 16 runtime cases failed: stalled-send timer returned at **14999 ms**.
   `Task.Delay` can wake early relative to Stopwatch. The host now rechecks
   its monotonic dispatch elapsed time and waits only the remaining duration;
   configured 15000 ms and 1000-ms upper tolerance are unchanged. Full
   45/22/16 rerun passes, including all three actual held/stalled cases.

## Handoffs and not-run gates

- **RT2:** qualify applicable released-profile startup/session/auth/error/
  shutdown destinations, diagnostics, collection and storage paths. Model
  callbacks and owned scoped trials are not all-native observation/prevention.
- **PV1 / D-004:** obtain intended-account/auth/terms/concurrency/quota/
  billed-cost and spending-control approval before live or paid inference.
  Byte bounds do not bound billable reasoning.
- **R13:** integrate selected/final bounds, fresh conversations, single
  admission, monotonic per-profile rolling failures, one forwarded inference, host
  dispatch timer, typed revision/target checks, provisional streaming and
  truthful Unknown quarantine. Prove task slots/resource leases/fairness
  independently; deterministic local core first.
- **R04:** host-owned durable identities/tracing/audit/no-replay and encrypted
  storage admission remain its ownership. Runtime/provider IDs are correlation
  only; this fixture does not add duplicate production host types or commit
  durable authority.
- **D-001/D-004/D-010 and production gates remain open.** No live sign-in,
  account credentials/default hosted routing, paid operation, real model,
  full RT2 observation, production app/installer launch, install/elevation,
  global policy change or sibling checkout mutation occurred.

This worktree initially matched main `3e8558f` with merged #33/#34/#35.
The user later requested publication and rebase; main `90d8f48` now includes
#37 RT2, #38 W2 and #36 presence. Canonical conflict resolution preserves
RT2's bounded observations/all-path Blocked gate, W2's approved best-effort
policy and MG1's separately pinned Pass. Full rebased root/fixture gates
were rerun; no sibling or historical fixture changes occur in this branch.
Coordinator/RT2/R04 message attempts were refused by the session messaging
service's message-limit error; this written handoff does not claim delivery.
Owned acquisition-probe/configuration and compile/repack scratch were removed
by exact inspected paths; reproducibility inputs/regression caches remain.
The worktree and ignored reproducibility inputs remain. The later explicit
publication request authorizes own-branch commit/rebase, lease-protected push
and a checks-gated auto-merge PR; no worktree removal or bypass is authorized.
