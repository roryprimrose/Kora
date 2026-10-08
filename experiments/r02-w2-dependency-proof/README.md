# R02-W2 fixed-action and dependency mechanism proof

Independent Windows x64 experiment, not production executor code. Based on
main `3e8558fbff07943d39721b230599b02062d90d57` (merged #33/#34/#35).
The original [containment evidence](../r02-containment-proof/evidence/README.md)
is unchanged: Job-only restricted execution is rejected; AppContainer is
partial; ordinary children are allowed; eight network timeouts do not prove
attributable denial. Installer implementation does not prove this boundary.

**Disposition:** scratch ACL projection and a no-child mitigation are rejected
as exact executable/module dependency mechanisms. Fixed embedded multi-script
input and a harmless typed effect are feasible in this fixture. Full worker/
fixed Windows control admission remains **Blocked**, not production-proved.
Native synthetic loading is blocked by the absent C compiler, not replaced by
a managed-module test.

## Owner decision and downstream direction

The owner explicitly decided on 2026-10-06 that tracking other scripts and
binaries used by a granted `.ps1` is **best effort**, for both bundled and
future user-provided scripts. The granting user accepts responsibility for the
script's overall actions within the separately admitted scope. Internal scripts
must be viewable/approvable, with all manifest-listed files in review tabs.
The [authoritative rule](../../Design/Built_In_Skills.md#best-effort-transitive-dependency-tracking)
preserves exact declared resources and observed-change revocation, and requires
honest disclosure of dynamic/unresolved references and possibly undetected
transitive changes. Tracking references for revocation is not OS enforcement.

This reconciles the previously absolute dependency requirement; it does not
erase the strict candidates' failed results or weaken privacy/ownership,
protected resources, network-profile, approval or receipt boundaries.
No typed broker was chosen and no embedded-script replacement was authorized.
No general user-script, downloaded-code or extension executor is enabled.

Handoffs:

- **R11/W4:** exact manifest resources/review tabs, complete declared set and
  definition digests, immutable parameter/invocation inputs, best-effort
  dependency/gap/invalidation records; protected required runtime/adapter
  identities are mandatory, not best effort. Prove full helper scope/call/
  cancellation, bounded untrusted IPC and lifecycle/effect races before exposure.
- **W1:** attributable network denial remains independently unproved. This
  fixture makes no network observations and must not qualify W1.
- **W3/R17:** independent installed ownership/ACL/token/alias/TOCTOU/runtime
  resolution still needs separately approved lab setup. Host-owned scratch and
  publish/native inspection do not qualify installed protection. Windows 11
  x64 is the installer target; x86 publishing is not offered installer acceptance.
- **W2/R16:** synthetic marker effects do not prove lock/shutdown/restart APIs.
  Obtain separately scoped consent before any real effects or privileged lab
  preparation. If the contained script cannot perform the fixed control,
  explicitly decide typed adapter/broker versus unavailable; no ambient shell.
- **RT2/MG1/R04:** pass host-resolved session/task/invocation/approval identity
  with immutable review inputs and independent truthful terminal observations.
  Fixture correlation is not a production storage/audit/trace schema or authority.
  No sibling code, resources or uncommitted documentation was consumed.

## Reproduce safely

Prerequisites: non-elevated Windows x64, local NTFS temporary storage, pinned
.NET SDK 10.0.401, existing reviewed PowerShell 7.4+ major 7. The measured engine
is PowerShell 7.6.6 on Windows build 26300 x64 / .NET 10.0.12. This developer host
is not a supported-reference-OS matrix or hardware-floor qualification.
No dependency/tool/runtime installation is performed.

From this isolated worktree, using a **new** output directory:

```powershell
.\experiments\r02-w2-dependency-proof\Invoke-Proof.ps1 `
    -EvidenceDirectory "$PWD\experiments\r02-w2-dependency-proof\artifacts\reproduction-01" `
    -PowerShellPath (Get-Command pwsh).Source `
    -ConsentOwnedScratch
$LASTEXITCODE
```

The runner builds only the fixed source payloads and fixture, runs all fixture
unit tests, then launches fixed worker modes. No free-form command, script input,
model text, native power API or production action is accepted by this workflow.
The private native helpers are linked from the prior experiment without editing
its source or evidence. The fixture is not in the production solution.

Exit **1** means harness/runtime/cleanup failure; **2** means rejected/blocked
admission, even when all diagnostic checks pass. There is no production-pass
exit for this partial profile. Do not translate 2 into enforcement success.
Build/receipt/cleanup failures are explicit; every attempt gets a fresh directory.

The original measured runner used locked test packages reviewed in that root
closure: xUnit v3 4.0.1, AwesomeAssertions 9.6.0 and TRX 2.4.1. All 19 resolved
test package/version/content hashes matched those historical root test locks.
Current reruns use the reconciled test lock described below; no new runtime
NuGet dependency enters the worker. Existing PowerShell is copied into scratch
only, not redistributed or production-admitted by this trial.

### Current-checkout test dependency reconciliation (2026-10-07)

Preparation at `d0a8e82ef34b82c4d888803083050c2e9dff43cd` exposed
`NU1004`: the historical test lock still requested TRX 2.4.1, while the current
central declarations require TRX 2.5.0 and centrally pinned Telemetry 2.5.0.
The operator approved regenerating only this fixture's test lock against the
existing central versions. Production package declarations and worker behavior
are unchanged. Subsequent restore/build validation retains locked mode.

The historical measured evidence is unchanged. New attempts must record the
actual base revision, dirty-source distinction and exact source/lock hashes
separately; this dependency reconciliation is not a containment result or
W1-W4/D-013 admission.

### Optional native fixture prerequisites (not run here)

[payload.c](native/payload.c) implements only `W2Observe()` returning 11 or 22.
The fixed loader uses `LoadLibraryExW` with DLL-load-directory/System32 search,
invokes that one export and frees the library. Only if an existing separately
reviewed x64 Visual C toolchain is available:

```powershell
.\experiments\r02-w2-dependency-proof\Compile-Native.ps1 `
    -VcVarsPath '<existing approved absolute vcvars64.bat path>'
```

Build the managed payloads first. The compiler uses `/W4 /WX /LD /MT /Brepro`
and never installs/elevates or modifies security policy. Compile both synthetic
variants, retain compiler/source/final-byte provenance and license review, then
run a new proof. Native results are distinct from managed byte-loading results.
Tool acquisition/privileged lab policy needs separately scoped approval.
On this host no `cl`, `clang`, `gcc`, standard Visual Studio `vcvars64.bat` or
Windows SDK compiler was available in the bounded prerequisite checks.
This is a blocker, not a claim that no compiler can exist anywhere on the machine.

## Actual boundary trials

1. Create one GUID-named owned temporary tree and synthetic current-user
   AppContainer profile; reject elevated/AppContainer hosts. No real credential
   or private-content enumeration occurs.
2. Copy only the built fixture, fixed declared/undeclared payload variants and
   explicit existing PowerShell runtime. Grant RX to candidate runtime, no
   container access to denied fixtures, Modify to owned work data, and explicit
   per-file deny-execute ACEs to existing data payloads. Record resulting SDDL.
   Parent inheritance alone did not stamp existing copied files as intended;
   the corrected trial explicitly stamps them. No installed/Kora ACL is changed.
3. Capture exact script bytes from embedded resources, canonical framed
   `Kora.ScriptSet.v1` and definition hashes, complete runtime/declared closure
   and invocation digest. Resource snapshots clone mutable inputs. These review
   pins are **not** the tested OS-denial mechanism.
4. Compare uncontained controls, AppContainer ACL projection, and AppContainer
   with a queried `ProcessChildProcessPolicy` (`NoChildProcessCreation`).
   Attempt declared/undeclared RX, denied and read/write-no-execute children,
   assembly path loads and independent assembly byte loads. The two payload
   DLLs differ in bytes/effect (11 versus 22); apphost bytes alone do not identify
   their different managed dependency closures.
5. Declared and undeclared children spawn grandchildren, publish actual image/
   token/value receipts and remain bounded by the job. Suspended root processes
   are assigned to a non-breakaway kill-on-close job before resume.
6. PowerShell loads the verified helper/entry as **separate** in-memory blocks;
   helper initialization is definition-only. The typed entry writes `102` only
   to a host-bound owned marker. No embedded script is extracted. Independent
   uncontained interpreter controls exercise the same bypass inputs.
7. Under AppContainer, attempt a denied script invocation and native read, a
   writable script, explicit module import, module-path autoload, and managed
   byte loading. Module-path changes are process-local, not machine configuration.
8. Produce an actual marker before lost/malformed receipts or cancellation.
   Keep lifecycle and effect state separate; never retry/replay Unknown.
9. Host environment sentinel is deliberately set then removed from launch
   environment; no ambient PATH/profile/startup-hook variables are forwarded.
   An owned host file handle is deliberately made inheritable, while native
   process creation uses `bInheritHandles=false`. Invalid-handle probes under
   AppContainer raise strict-handle SEH; that observation stays **Unknown**.
   Inspected launch flags are not a complete inherited-object identity proof.
10. Query actual owned process images, file hashes and token SIDs; hold stable
    handles for job-owned descendants. Close the exact owned job and verify
    tracked shutdown within five seconds; delete the exact profile/tree in
    `finally`. Cleanup failure is an error, not success.

Workers have a 30-second deadline; synthetic descendants independently have a
120-second cap and are job-terminated earlier. Receipts are bounded to 64 KiB.
No unrelated PID is terminated and no broad process-name cleanup is used.
Scratch-only lifetime supervision is not full production host-death/race proof.

## Evidence and validation

The [measured evidence](evidence/README.md) records exact inputs/source/native/
image hashes, environment, UTC observations, diagnostic counters, rejected/
blocked gates and cleanup. Raw attempts remain in ignored `artifacts`; failed
development attempts are not presented as successful final enforcement.

| Real observation | Meaning |
|---|---|
| Uncontained binary/module/script controls work | Valid fixtures and independent allowed operations |
| Inaccessible EXE/assembly/native script read returns error 5 | Genuine scratch ACL denial, not a preflight allowlist |
| Explicit data-file execute denial stops child/path loading, but assembly byte loading returns 22 | File-execute denial is not in-process code admission |
| Undeclared RX child/grandchild runs with container token | AppContainer/job membership is not exact executable admission |
| No-child policy denies declared/undeclared children with error 367, but managed code still runs | All-child restriction, not a selective dependency or module mechanism |
| Writable script/module explicit import/autoload/managed bytes run | Exact transitive admission candidate rejected |
| Denied script invocation reports command-not-found | Unknown interpreter error; separate error-5 read is the attributable denial |
| Distinct helper/entry blocks write marker 102 | Harmless fixed multi-script feasibility, not actual Windows control success |
| Effect survives lost/malformed receipt and cancellation | Unknown effect; termination is not rollback or replay permission |
| Handle probe raises SEH | Unknown; preserve independent inherited-object admission work |
| No compiler / no installed fixture / no network trial | Native Blocked; W3/W1 Not run, never passed |

Repository validation uses a fresh `w2-full-01` result directory only:
root Release build including setup, Core 251/251, Application 744/744, Windows
353/353; latest-only Core/Application line and branch coverage 100%.
Dependency license/notice, version, publication-policy and payload-negative
checks pass. Both x64/x86 locked publishes and 201-file payload inspection pass.
Normal WiX/Burn packaging was attempted without installation: MSI ICE fails
with **WIX1105, validation blocked by system policy**. No elevation, policy
change or silent skipped-ICE success is substituted; production release gates
remain open. No commits, pushes, PRs, merges or worktree removal are performed.
