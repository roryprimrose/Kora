# MG1 unattended repetition — 2026-10-09 UTC

**Pass: released NuGet SDK 1.0.16 / unchanged native runtime 1.0.90,
Windows x64, minimal HTTP/stdio, synthetic loopback only.**

The proof ran on branch `proof/unattended-20261009-mg1`, base
`e0692f438a058de0a20021b3420a981849706dbf`, with dirty experiment-local
preparation/README changes, not a clean baseline. The runtime receipt
identifies every tested fixture source/script/lock by SHA-256. The final
documentation additions are not a claim that a later commit or rebase was
executed. .NET SDK 10.0.401, runtime 10.0.12 and PowerShell 7.6.6 were already
installed; nothing was installed or elevated.

## Commands and complete results

From the isolated repository root:

```powershell
.\experiments\r02-dotnet-management-proof\Prepare-Fixture.ps1 -PackageConfigPath <ignored-local-package-config>
.\experiments\r02-dotnet-management-proof\Run-Proof.ps1 -NoRestore
```

The local configuration is an uncommitted package-routing input, not a
repository contract. No credentials are present in it.

| Suite | Total | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| Complete released-profile RT1 derivative | 45 | 45 | 0 | 0 |
| MG1 host components | 22 | 22 | 0 | 0 |
| Actual .NET SDK / native runtime | 16 | 16 | 0 | 0 |
| **Total** | **83** | **83** | **0** | **0** |

All three Release builds reported zero warnings/errors. The runner checked
complete TRX counts, all 16 runtime rows and owned cleanup in every runtime
and regression row. RT1 retains exactly one **expected rejected hook-only
FAIL witness**, within a passing test; it is not an unexpected test failure.
Final disposition was written at **2026-10-09 11:10:48 UTC**.

[Input verification](evidence/unattended-20261009/input-verification.json),
[released regressions](evidence/unattended-20261009/rt1-released-regression.json),
[runtime observations](evidence/unattended-20261009/runtime-results.json),
[disposition](evidence/unattended-20261009/disposition.json) and
[run identity/counts](evidence/unattended-20261009/run-summary.json) are fresh.
Original MG1/RT1/Node evidence and the source-build blocker were preserved,
not rewritten or superseded. Raw transcripts, TRX, original/fresh receipts
and the pre-run source diff are retained outside git in the assigned session
artifact store. The committed JSON copies contain no machine-specific paths;
raw artifacts were not edited to produce these copies.

## Narrow preparation fixes and acquisition observations

- MG1 previously restored into a sibling RT1 cache. Its cache now lives in
  MG1's ignored `.inputs\packages`, including the independently derived
  regression. No other proof's source, evidence or cache is written.
- The derivative previously used `--force-evaluate`. It now copies the
  reviewed MG1 lock, with the same direct references and central versions,
  and uses `--locked-mode`. The successful restore verified that graph
  without changing either committed lock or central package versions.
- Initial repository-config restore failed **NU1900**, because the NuGet.org
  V3 service index was unreachable (TLS handshake failure). Public NuGet V2
  acquisition succeeded for the official SDK package; its SHA-256 was
  exactly `c5518980b71d0ef0abd39ecdec7834898878222290c7fd1099a9040c8c5bf2ef`.
  A local SDK feed and the existing `dotnet-public` source then restored all
  three missing project asset sets in locked mode. A caller initially
  resolved a relative configuration path against the host process directory;
  retrying with PowerShell's worktree-resolved path corrected that invocation.
- Package/content hashes, native pins, warnings-as-errors and lock versions
  were not relaxed. Local routing exposes no advisory service; **no fresh
  vulnerability audit is claimed**. The existing license review is retained,
  not a new production-license or service-entitlement approval.

## Observations, limits and cleanup

The request boundaries remained 32768 accepted / 32769 rejected complete
wire bytes; output boundaries remained 4096 accepted / 4097 degraded.
Held inference, stalled send and stalled native abort acknowledgement
returned at 15003, 15005 and 15001 ms respectively, against the existing
15000-ms deadline and 1000-ms scheduling tolerance. The deadline/cancel race
terminated as Cancelled at 15011 ms with no new admission. Thirty actual
failures still counted toward the rolling hour; 3599999 ms denied and
3600000 ms admitted a new explicit request. Two held executions remained
separate while the synthetic manager completed.

These are concurrently executed fixture observations, **not reference-machine
performance or account-capacity qualification**. No live provider, account,
sign-in or paid inference was used. No elevation, policy changes, shared
process-name termination, SDK/native patch or production change occurred.
The retained process handle controlled only the verified owned native child,
which was resumed in `finally`. All 61 final cleanup indicators (45 regression
and 16 runtime rows) passed; inspection found no matching fixture-owned native
survivors or residual trial scratch. Generated derivative code and ignored
build/input/test artifacts were removed after raw receipt preservation.

Physical computation termination remains **Unknown**; abort acknowledgement
does not imply rollback. Source-built byte equivalence is not claimed.
RT2 lifecycle qualification, PV1 account/usage approval, integrated durable
authority and production admission remain outside this proof. Root solution
tests, coverage and packaging were not rerun locally; the PR's normal required
checks and up-to-date branch protection must still decide integration.
