# R02 Windows worker/deployment containment proof

Experimental, Windows x64 only. **No production execution profile is enabled.**
The feasibility investigation has useful OS denial evidence, but the strict
network-denial gate is unproven on this host. Keep the PR draft until that
required evidence and independently protected deployment are resolved.

## Scope and contracts

R01 commit `7d5e6a3` / PR #19 was present after the initial rebase. Its approved
policy reconciliation is a prerequisite, not runtime containment evidence.
This branch investigates only R02's Windows worker/deployment slice.

Read contracts:

- [Execution gate](../../docs/skill-and-task-execution-design.md#task-execution-gate)
- [Protected resources and deployment](../../Design/Security_Data_Flows.md#application-integrity-and-no-self-modification)
- [OS containment](../../Design/Security_Data_Flows.md#executable-extensions-and-containment)
- [Bundled snapshots](../../Design/Built_In_Skills.md#embedded-resource-storage-and-integrity)
- [Real-boundary acceptance](../../Design/Acceptance_Criteria.md#no-self-modification-gate)
- [Windows boundaries](../../Design/Architecture.md#platform-boundaries-and-support)

The existing [PowerShell readiness probe](../../src/Kora.Windows/Dependencies/WindowsPowerShellSetupService.cs)
checks a 7.4+ major-7 runtime using no-profile execution. The existing
[process runner](../../src/Kora.Windows/Dependencies/PowerShellProcessRunner.cs)
uses user-scope winget for separately approved setup and process-tree kill for
cancellation. Neither is an execution sandbox. Native integration belongs in
Windows adapters, not portable domain logic.

No bundled skill, grant schema, tool registry, broker, installer or general
script executor is implemented here. The one embedded experimental
[PowerShell probe](FixedProbe.ps1) is fixed at build time and passed as
`-EncodedCommand`; no script file is extracted or selected by model input.
This does not prove multi-script runspace/helper contracts.

## Reproduce

Prerequisites: non-elevated Windows x64, repository-pinned .NET SDK, existing
PowerShell 7.4+ (major 7), local NTFS scratch storage, and an active local IPv4
interface. Do not run on a production service host. The runner rejects elevation.
No dependency installation/download is performed.

From this worktree, in PowerShell 7:

```powershell
.\experiments\r02-containment-proof\Invoke-Proof.ps1 `
    -EvidenceDirectory "$PWD\experiments\r02-containment-proof\artifacts\trial-01" `
    -PowerShellPath (Get-Command pwsh).Source
$LASTEXITCODE
```

Use a **new** evidence directory on each run. Generated `bin`, `obj` and
`artifacts` remain ignored by the existing repository ignore rules.
The experiment-local build props deliberately avoid production analyzer/package
references; there are no NuGet package dependencies and no solution/CI changes.

Equivalent focused commands:

```powershell
dotnet build .\experiments\r02-containment-proof\ContainmentProof.csproj -c Release
$proof = '.\experiments\r02-containment-proof\bin\Release\net10.0-windows\ContainmentProof.exe'
& $proof self-test
& $proof run "$PWD\experiments\r02-containment-proof\artifacts\trial-02" (Get-Command pwsh).Source
```

Exit codes: `0` = measured assertions passed (not production certification);
`1` = harness/build/runtime error; `2` = required OS assertion unsupported or
unproven. **Do not translate exit 2 into success.** Five deterministic assertions
test receipt correlation and error classification; they are not denial evidence.
The real trials enforce expected access errors, token identity, protected bytes,
descendant shutdown, interpreter receipts and truthful lifecycle states.

## What the trials actually do

1. Create a GUID-named owned scratch tree under the resolved temporary directory
   and a temporary current-user AppContainer profile.
2. Copy the built worker and the already-installed PowerShell runtime into
   scratch. Grant the container SID RX to runtime files, Modify only to each
   allowed work directory, and **no grant** to the protected fixture directory.
   No installed runtime/Kora ACL is changed.
3. Create synthetic protected file/delete/rename fixtures and a hard-link alias
   in the allowed directory. The worker attempts direct, `..` traversal and
   hard-link access without an application path-denial check.
4. Create one GUID-targeted generic Credential Manager credential containing
   public synthetic text; verify host access. Read only that target in workers.
   Never enumerate real credentials or serialize credential contents.
5. Listen on an ephemeral local TCP port; connect to loopback and one owned
   non-loopback IPv4 interface. No external endpoint is contacted.
6. Compare same-user **Job Object only** against a classic capability-free
   AppContainer plus the same kill-on-close Job Object.
7. Launch suspended, assign the job **before** resume, inherit no host handles,
   use an allowlisted environment and absolute dependency paths. Ordinary child
   and grandchild creation is tested. `CREATE_BREAKAWAY_FROM_JOB` is attempted;
   an unexpectedly successful breakaway is created suspended and terminated
   immediately, never allowed to leave a running untracked process.
8. Record actual worker/descendant token SIDs and integrity/elevation observations.
   Complete normally, cancel after a receipt, and time out after a real
   synthetic effect with its receipt deliberately withheld. Also abort after
   an explicitly malformed receipt following a real effect. Closing the job
   must stop all tracked processes within 5 seconds. Stable process handles,
   not later PID lookups, verify shutdown.
9. Delete the exact synthetic credential, temporary profile and owned scratch
   directory in `finally`. Retain environment, receipts, cleanup and validation
   evidence. Cleanup failures are errors, not silent success.

This is a destructive control only for **owned synthetic files**: the Job-only
baseline intentionally mutates/deletes/renames its protected stand-ins to prove
the tests can detect ambient access. No actual Kora source/install data is probed
with writes. The fixture represents a protected-root ACL, not a comprehensive
test of every source/Git/build/deployment alias.

## Findings

The final measured snapshot and environment are in [evidence](evidence/README.md).

| Boundary | Job-only control | Capability-free AppContainer |
|---|---|---|
| Allowed file read/write | Allowed | Allowed |
| Protected read/write/delete/rename | Allowed; fixture changed | OS error 5; bytes and entries unchanged |
| Hard-link and traversal write | Allowed | OS error 5 |
| Synthetic Credential Manager read | Allowed | OS error 5 |
| Ordinary child / grandchild | Allowed | Allowed; same container SID |
| Job breakaway | OS error 5 | OS error 5 |
| Fixed in-memory PowerShell | Runs with ambient rights | Runs; protected write HRESULT `0x80070005` |
| Loopback / owned-interface connects | Connected | Deadline expired; **Unknown**, not Denied |
| Tree shutdown | Observed | Observed for completion/cancellation/timeout |
| Effect without receipt | Not applicable | **Unknown** despite observed process termination |

The managed `File.Move` convenience API reported file-not-found (2) for an
inaccessible source. That observation stays `Unknown`; the separate direct
`MoveFileExW` probe establishes the actual OS rename-denial result. Never
classify file-not-found, connection failure or timeout as access-denied.

No network success was observed in the AppContainer trials. That does **not**
establish an auditable network denial: packet loss, firewall policy, timeout or
an isolation mechanism can produce the same observation. Empty capabilities
and network documentation are not a substitute for that missing observation.

## Required identities and deployment assumptions

- **Host:** trusted, non-elevated interactive user process; its credentials stay
  outside the worker. The proof host owns scratch and can change its ACLs.
  This identity separation excludes a malicious/compromised host, as the design
  does, but it is **not** proof that normal production Kora cannot modify code.
- **Worker:** host-created per-profile AppContainer SID, no capabilities, low
  integrity, no inherited privileged handles, no reusable credentials. Explicit
  grants admit only the required data/runtime surface; never grant protected
  roots or blanket `ALL APPLICATION PACKAGES` write access.
- **Deployment/maintenance:** an independently controlled installer/admin/service
  identity must own application, adapter, interpreter, launch/trust/update files
  and their parent directories. Normal host and workers need RX, not write,
  delete-child, ownership or DACL-edit rights. Replacement authority must not
  be callable by model/worker code. No privileged deployment fixture was created.
- **Development:** registered source/worktree, Git metadata, output and remote
  repository identities still require host admission plus OS isolation.
  This proof does not establish protection for alternate clones, Git APIs,
  reparse/target-swap races or remote deployment. Disable affected capabilities
  when those identities/rights cannot be established.
- **Unsigned artifacts:** external origin/provenance and final-byte hashes give
  narrower traceability, not publisher authentication. Internal checksums and a
  per-user writable installation cannot anchor trust outside a replaced binary.
  Protect dependencies as well as the host. The copied runtime here is a
  disposable experiment, not a production installation recommendation.

## Recommendations / unsupported profiles

1. Reject same-user PowerShell plus Job Objects as restricted execution. It
   demonstrably reaches protected files and user credentials. A restricted token
   that merely removes privileges is not a network/filesystem allowlist either;
   restricted-token/service-identity variants were **not** trialled here.
2. Retain capability-free AppContainer plus a non-breakaway kill-on-close job as
   a candidate **filesystem/credential/lifetime** mechanism, not a certified
   filesystem allowlist. Classic AppContainer may read OS objects exposed to
   application packages; LPAC and protected-resource aliases need separate proof.
3. Keep required **network-denied** execution unavailable. Follow up on a clean
   supported reference OS with independently observable WFP/network-denial
   evidence, or an approved application-scoped isolation mechanism. Any privileged
   or disruptive trial needs prior approval; do not change global policy.
4. AppContainer does not enforce an executable dependency allowlist: ordinary
   children are allowed. A process-denied profile, dynamic/imported PowerShell,
   downloaded code and general script execution remain unsupported. Neither
   command filtering nor the fixed encoded input changes that conclusion.
5. Fixed lock/shutdown/restart remain unavailable through this candidate pending
   exact approved OS-control and deployment tests. The read-only desktop handle
   probe is diagnostic only; it does not execute or prove computer controls.
   Consider a narrowly typed trusted host broker only under a separately approved
   design; do not relax the worker into ambient execution.
6. Preserve `Unknown` for missing/malformed/uncorrelated receipts even after a
   successful kill. Cancellation is a lifecycle observation, not effect rollback.
   Never retry side effects automatically. Unexpected creation/assignment,
   identity or cleanup failures leave the profile unavailable.

Missing production gates include independent deployment ownership, normal-host
write denial, full alias/TOCTOU/repository protection, external provenance,
child dependency admission, actual network denial, controlled lock/power effects,
and abrupt host-death/reboot recovery. Job close is measured; a host crash/reboot
was not trialled. None is replaced by a mock, application path check or warning.
