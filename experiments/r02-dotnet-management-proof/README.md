# R02-MG1 actual .NET management envelope

**Pass for the separately user-approved released NuGet SDK 1.0.16 / native
runtime 1.0.90 minimal HTTP/stdio profile. Not Gate 0 or production admission.**

This experiment uses the actual .NET SDK and native runtime with synthetic
loopback HTTP/SSE providers. It is not a Node sidecar, fake-only proof,
production R13 scheduler, ledger, authority or account-capacity claim.

## Profile and historical separation

The initial instruction selected RT1's source-built
`1.0.16-rt1.source.f8ae645.1` bytes. The documented rebuild failed its locked
hash check; [that blocker](evidence/source-profile-blocker.json) is retained.
After package acquisition succeeded, the user **explicitly approved official
released 1.0.16 and fresh RT1 regressions before MG1**. No byte equivalence
between the local source build and official package is inferred.

- Source: `f8ae645902b74b62cd47aac1fd9b29adaec3aff2`.
- Released package SHA-256:
  `c5518980b71d0ef0abd39ecdec7834898878222290c7fd1099a9040c8c5bf2ef`.
- Released net10 assembly SHA-256:
  `6ed0b19fd2f9cf525074830784bb255245f15b8f08a3d6aa78fb116be5ba668b`.
- Windows x64; .NET SDK 10.0.401 / runtime 10.0.12.
- Native 1.0.90 / protocol 3, identical RT1 launcher/payload; only those two
  files and their original license are present in the active runtime directory.
- [Separate license review](LICENSE-REVIEW.md) and package locks do not admit
  this closure into production.

Original [RT1](../r02-dotnet-control-proof/README.md) and
[Node witness](../r02-runtime-proof/README.md) source/evidence are unchanged.
Preparation derives an ignored MG1-owned RT1 snapshot at repository base
`3e8558fbff07943d39721b230599b02062d90d57`. Only fixture artifact pins,
reviewed dependency metadata and evidence profile labeling change;
**no SDK/native source is patched**. The derived controls use the same
central package versions as MG1, including separately approved compatible
managed/test dependency updates. SDK/native bytes and historical closures
remain distinct and unchanged.
Its 45 tests repeat every RT1 path and retain the expected rejected hook-only
FAIL. Fresh results are [recorded here](evidence/rt1-released-regression.json),
never written into historical RT1.

## Run from repository root

Use the already installed exact toolchain. No elevation, installation,
live credentials, application/installer launch or policy changes are needed.
Review licenses before preparation.

```powershell
.\experiments\r02-dotnet-management-proof\Prepare-Fixture.ps1
.\experiments\r02-dotnet-management-proof\Run-Proof.ps1 -NoRestore
```

Both scripts accept `-PackageConfigPath` for a machine-local configuration
outside the repository. Do not commit machine routing or credentials.
Preparation restores locked reviewed packages and downloads only pinned
public source/runtime archives if no verified local input exists.
It verifies the released package, embedded assembly, native bytes and Node
witness before any runtime starts.
`-NoRestore` reuses preparation's restored assets; omit it only for a required
locked restore after changed manifests or missing dependencies.

The full runner requires **45 released-profile RT1 + 22 host component +
16 actual SDK/native tests**, zero failures/skips/filtered or zero-test runs.
It builds Release with warnings/analyzers as errors and checks complete rows
and owned cleanup. [Disposition](evidence/disposition.json) is profile-specific.

## Measured boundaries

- Host selection counts its complete serialized system/prompt/history
  context. The final public HTTP handler separately counts the complete
  serialized UTF-8 model body, including actual runtime framing/history and
  multibyte text. Fresh isolated conversations are used per request. A
  non-forwarding calibration conversation derives prompt padding from actual
  framing; no body is patched to manufacture a boundary.
- Streaming output is provisional. Complete JSON framing and multibyte text
  count toward 4096 bytes. Overflow, malformed/missing/duplicate/unknown
  fields, invalid operation/target/revision and identity/approval fields fail
  explicitly. No truncation becomes a proposal or ledger mutation.
- The host's 15000-ms timer begins at dispatch; provider/SDK send/abort
  acknowledgement cannot extend it. Real inference is held at a provider.
  Another trial stalls the public HTTP handler. A third suspends **only the
  verified direct owned native child**, using its retained process handle,
  so real SDK abort acknowledgement cannot arrive before the host deadline.
  The exact child is always resumed in `finally`; failure remains explicit.
  No name-based process control or global policy change is used.
- One management request is admitted at once. Thirty actual SDK/native
  failures consume the per-profile rolling hour; 3599999 ms still denies,
  3600000 ms permits a new explicit request. The rolling window uses monotonic
  timestamps, not wall time; forward/backward UTC jumps cannot reset its budget.
  Time advancement is deterministic,
  forwarding counts are real. Each request forwards at most one inference;
  SDK retries and tool/result continuations are blocked at the final gate.
- Two execution conversations remain held while an independent manager
  completes. Session/context/destination/tool separation is observed, not
  production slots, resource leases, account quotas or reference-machine SLOs.
- Cancellation/deadline/retry races cannot reopen the quarantined slot.
  SDK acknowledgement is not rollback: an admitted noncooperative
  execution-fixture scratch effect completes after acknowledgement.
  No management tool is authorized. Physical provider computation termination
  remains **Unknown**, even when connection closure is observed.
- Local fixed choices/status/cancel paths do not require model completion.
  These are fixture API observations, not production UI acceptance.

## Isolation, bounds and cleanup

Child environment replaces inherited credentials/routing/profile state;
empty mode and `UseLoggedInUser=false`, explicit synthetic provider, tool
allowlist/deny-by-default permission and per-session volatile I/O match RT1.
Only a credential sentinel is used. Public HTTP transport allows only owned
ephemeral loopback destinations, with proxies/redirects/WebSockets disabled.
This is not an OS sandbox or all-native-network/file observation.

Setup/session creation: 15 seconds; provider arrival: 10 seconds; individual
real case: 90 seconds; provider lifetime: 3 minutes; owned teardown/observer
join: 10 seconds; host deadline: exactly 15000 ms with recorded scheduling
tolerance 1000 ms. SDK wait bounds are separate from the host deadline.
Reports retain hashes, UTC times, counts and fixed synthetic facts, not raw
model bodies, headers, credentials or diagnostic content.

Finally blocks stop only the owned SDK client/listeners, resume any owned
suspended child, clear volatile stores and remove exact unique scratch paths
after containment/reparse checks. Earlier two failed-trial scratch directories
were separately inspected and removed by exact path. Dependency/input and
regression caches remain ignored for reproducibility.

## Remaining gates / ownership

- RT2: attributable complete lifecycle network/files/diagnostics observation
  for the applicable profile; this proof does not close sibling evidence.
- PV1 / D-004: intended account, supported auth/terms, actual concurrency,
  quotas/rate limits and defensible billed-cost/spending controls. No live
  inference, sign-in, paid usage or account entitlement was tested.
- R13: deterministic host first, then integrated typed proposals/revision/
  target checks, admission, resource leases, fairness and Unknown recovery.
- R04: compose durable host identities, tracing and ordered task/evidence/audit
  commits into the host; prove recovery and lifecycle gates with no automatic
  replay. [D-009's approved private-profile standard SQLite baseline](../../Design/Decision_Register.md#approved-profile-secured-sqlite-baseline---2026-10-06)
  supersedes mandatory encryption/provider/key/rekey admission; optional
  encryption is R30, not a prerequisite. Runtime/provider fields remain
  untrusted correlation, not identity or authority. The
  [R04 foundation](../../Design/Implementation_Roadmap.md#r04-foundation-delivery)
  remains partial, not production admission.
- D-001/D-004/D-010, R08 exposure and production admission remain open.
  Byte limits do not bound billed reasoning, authorize actions or prove
  physical computation stop.

See [EVIDENCE](EVIDENCE.md) for measurements, failures and full root validation.
