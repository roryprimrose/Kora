# R02 Windows worker/deployment containment proof

Experimental, Windows x64 only. **No production execution profile is enabled.**
The feasibility investigation has useful OS denial evidence, but the strict
network-denial gate is unproven on this host. This partial research and its
delivery plan may merge without certifying a production profile. Outstanding
capability validation is recorded in [the testing checklist](#outstanding-testing-checklist)
and [shared deferred-validation register](../../Design/Deferred_Validation.md).

**Current disposition:** retain as an opt-in, environment-qualified capability/
RC harness outside default CI. Actual attributable network, token/ACL/child
and lifetime observations reduce risks that deterministic host mocks cannot.
They gate only the affected profile, not unrelated feature merges. The
[three-tier policy](../../Design/Acceptance_Criteria.md#three-tier-qualification-policy)
and [disposition inventory](../../Design/Implementation_Roadmap.md#experiment-disposition-inventory)
preserve fail-closed controls, critical fixtures and separate lab approval.
No executable or original receipt is removed.

## Scope and contracts

R01 commit `7d5e6a3` / PR #19 was present after the initial rebase. Its approved
policy reconciliation is a prerequisite, not runtime containment evidence.
This branch investigates only R02's Windows worker/deployment slice.
The initial proof kept canonical designs unchanged. The subsequent outcome
handoff now records [design consequences and continuation gates](../../Design/Security_Data_Flows.md#r02-windows-containment-outcomes),
[D-013](../../Design/Decision_Register.md#d-013-windows-worker-and-deployment-containment)
and [roadmap W1-W4](../../Design/Implementation_Roadmap.md#r02-windows-containment-follow-up).
Those documents own delivery direction; this experiment retains reproducible
commands and measured evidence without becoming production composition.

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

### W1 filtered buffered-event preparation (2026-10-07)

This opt-in path coordinates the original IPv4/TCP trials with a separate
administrator observer. It does not start a trace, enable auditing, change WFP
options or firewall rules, install anything, or grant elevation to the host/
worker. The operator reported `NETEVENTS = on` on the current machine; that
readiness observation is not proof that a required block event is available.

Preparation was initially limited to focused builds and deterministic tests.
One subsequent filtered observation was separately approved as recorded below;
that approval is not reusable. Before each execution, obtain separate
approval for filtered reads of existing buffered events and the original
owned-scratch/local-network/synthetic-credential effects. Review/pin the actual
compiled EXE and DLL bytes before launching the administrator observer; source
revision alone does not identify dirty-source or compiled bytes. Keep this
observer in a separate terminal under the same user's elevated token, never
start the host from that terminal.

Once separately approved, select a new shared evidence directory. Start the
fixed observer there before starting the non-elevated runner:

```powershell
# Separate administrator terminal; no worker launch in this process.
$proof = '.\experiments\r02-containment-proof\bin\Release\net10.0-windows\ContainmentProof.exe'
& $proof collect-network '<approved new absolute evidence directory>' consent-filtered-buffered-events

# Separate NON-ELEVATED PowerShell 7 terminal.
.\experiments\r02-containment-proof\Invoke-Proof.ps1 `
    -EvidenceDirectory '<same approved new absolute evidence directory>' `
    -PowerShellPath (Get-Command pwsh | Select-Object -First 1).Source `
    -NetworkHandoff
```

The host writes an atomic request after each of the Job-only complete,
AppContainer complete and AppContainer cancelled trials, after the tracked tree
stops but before its images are removed. Each request binds an ID/digest,
observed user/container identity, two exact copied image paths/hashes, UTC
start/end observations and the owned IPv4 endpoint/port. The host waits at most
90 seconds for a matching inspection-only receipt. Missing/malformed/mismatched
or failed collection throws and follows the existing exact cleanup; partial
trial receipts remain available and effects are never replayed.

The observer permits only the fixed trial paths, checks image hashes and rejects
reparse ancestors. It issues four `netsh wfp show netevents` queries per row,
each filtered by application, user, TCP protocol, loopback/owned-interface
destination, destination port and a lookback of at most 120 seconds. There are
no unfiltered fallback queries, broad state dumps, Security-event reads,
`capture`, `set` or audit-enablement commands. Each native query has a ten-second
deadline and each resulting XML is limited to 1 MiB. The CLI's lookback covers
through collection time, not just the exact trial interval: preserve the request
start/end times and distinguish these when later qualifying the event schema.

Raw well-formed XML is **collected for inspection**, not parsed into an OS-denial
claim. The actual XML schema, event completeness, worker/process/token and
blocking-filter/layer attribution remain unqualified until an approved real
run. Empty output, lost buffered events, access failures or unrelated firewall
blocks cannot pass W1. An inspection receipt releases scratch for cleanup;
it has no execution, effect, grant or acceptance authority. The observer returns
`2` even when all queries complete, and the original eight network assertions
and strict host exit semantics are unchanged.

The normal runner defaults to no handoff. IPv6, UDP/DNS and descendant network
probes are still absent; this is observation preparation for the original matrix,
not the full W1 coverage gate. Coordination tests are linked into the existing
W2 test project without adding a dependency. No production profile is enabled.

Focused deterministic validation builds this project and the existing W2 test
project with `--no-restore`, runs this executable's `self-test`, then runs the
built xUnit test application with its documented native runner options:

```powershell
& .\experiments\r02-w2-dependency-proof\tests\bin\Release\net10.0-windows\W2Tests.exe `
    -noColor -result-trx '<new absolute result file path>'
```

This preserves a durable TRX without requiring MTP mode in the built executable.
The direct `dotnet test` preparation invocation was rejected by the SDK before
test execution; invoking the native runner with MTP `--report-trx` also failed.
Those attempts are not counted as tests or evidence of OS behavior.

**First separately approved current-machine observation, 2026-10-07:** the three
handoffs and twelve filtered queries completed, but every output was the empty
`<netEvents/>` document. Both .NET and PowerShell loopback/owned-interface
positive controls worked. The host retained 63/71 assertions, the same eight
unproven network assertions and exit `2`; all five tracked trees stopped and
synthetic credential/profile/scratch cleanup completed. Request digests, source/
compiled-byte pins, raw XML, query arguments and assessment are retained in the
session evidence, not added to or substituted for the historical snapshots.
No auditing, WFP option, firewall-policy or trace-session change was made.

This measures real coordination and successful filtered query execution, not
an attributable network denial. No matching block event was returned, and the
cause (generation, buffer retention, filter matching or another diagnostic gap)
is undetermined. Do not broaden filters, start tracing or replay effects without
a fresh scoped approval. Populated-event parsing/filter attribution and the
remaining network matrix stay unqualified.

### W1 user-filter comparison preparation (2026-10-07)

**Preparation only; not authorized to run.** The next bounded candidate compares
the existing strict query with a query omitting only `userid`. The default
`collect-network` mode is unchanged and cannot accept the comparison consent.
This candidate requires a new, explicit collection-scope approval and a new
live-trial approval; the first observation's approval has been consumed.
Review and pin the newly built EXE/DLL and source inputs, allocate a new evidence
directory, and start the separately elevated same-user observer only after those
approvals:

```powershell
# NOT authorization to execute. Separate administrator terminal after approval.
& $proof collect-network-user-comparison '<approved new absolute evidence directory>' `
    consent-application-endpoint-events-without-user-filter
# The separately approved non-elevated host still uses -NetworkHandoff.
```

The comparison makes eight queries per row (24 across the same three handoffs):
.NET/PowerShell times loopback/owned-interface times strict/application-only.
Every diagnostic query retains the exact copied executable path, TCP protocol,
destination address, ephemeral destination port and at-most-120-second lookback.
Only the user condition is omitted; no application-only-without-endpoint,
endpoint-only, unfiltered, or error-triggered fallback is implemented. Host
ownership, observed user/container identity, image hashes, reparse rejection,
request/digest binding, XML/output limits and cleanup still apply.

**Additional data scope:** diagnostic XML may contain metadata for another user
running those exact disposable images against those exact endpoints during the
lookback. This is a real collection broadening, not merely a parsing change.
Do not share raw XML or reinterpret collection consent as identity/authority.
Store it in the approved local evidence directory. Retain query scope, arguments,
start/end times and outcomes; do not replace the twelve historical empty outputs.
Stop after the fixed batch. Failed queries or an exhausted budget stop collection
with an explicit failed receipt; no retries, extra reads or effect replay.
Native queries share a 60-second per-row elapsed budget in comparison mode,
each capped at ten seconds and shortened by the remaining budget. File validation,
XML inspection and completion I/O are not covered by that native-query deadline;
the host still has its independent 90-second acknowledgment/cleanup boundary.
Timeout/failure cleanup paths are not yet induced in a live comparison run.

The rationale is limited: Microsoft's [netsh WFP reference](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-wfp)
supports both DOS/NT application paths and SID/user-name filters. The
[enumeration template](https://learn.microsoft.com/en-us/windows/win32/api/fwpmtypes/ns-fwpmtypes-fwpm_net_event_enum_template0)
ANDs conditions, while [event-header flags](https://learn.microsoft.com/en-us/windows/win32/api/fwpmtypes/ns-fwpmtypes-fwpm_net_event_header0)
identify which fields are present. These facts justify investigating whether a
user condition excludes available records; they do not establish the cause of
this machine's empty output. Successful connections are positive controls for
the traffic probes, not proof that permitted connections generate buffered
events.

Queries run sequentially and inspect a changing buffer, not a common snapshot.
Their rounded lookbacks cover through their respective collection times. Compare
record timestamps against the request's actual interval; strict-empty/
diagnostic-populated output alone cannot prove the user condition caused the
difference. Missing identity, unrelated traffic or absent blocking-filter/layer
attribution remain unqualified. The [classify-drop record](https://learn.microsoft.com/en-us/windows/win32/api/fwpmtypes/ns-fwpmtypes-fwpm_net_event_classify_drop2)
identifies a filter/layer, but resolving that evidence into attributable
containment denial is separate work, not a broad filter/state dump in this mode.
All completion receipts retain `Unproven`, the observer returns `2`, and the
original eight assertions and W1-W4/R11/R16/R17 gates remain unchanged.

Preparation validation: both focused Release builds completed with zero warnings
and errors, without restore. The combined native xUnit suite passed 85/85 cases
(22 additional consent/scope/budget cases); the original five deterministic
self-tests passed. CLI checks rejected missing consent and the previous strict
consent before creating output or entering the observer. No buffered events,
privileged query, comparison trial, policy change or installation was performed.
Source and newly built binary hashes are recorded separately from the earlier
live observation's pins; the earlier approval/descriptor cannot admit these bytes.

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

## Outstanding Testing Checklist

Use this checklist when the operator returns to an interactive session; it is
a deferred-validation handoff, not permission to run disruptive or privileged
tests. The [shared register](../../Design/Deferred_Validation.md) links the
independent speech and distribution proofs without consuming their unmerged
implementations. All rows below are currently outstanding; existing fixed
scratch trials prove only the narrower [recorded observations](evidence/README.md).

### Safe Existing Checks

The existing runner builds, runs five deterministic assertions and uses only
fixed probes against owned synthetic resources. It creates/deletes a temporary
AppContainer profile and a single synthetic Credential Manager entry, copies
the installed runtime into scratch, opens local listeners and closes its owned
process tree. It does not capture audio, install/launch Kora, alter installed
ACLs, change global policy or request lock/power effects.

Run [Reproduce](#reproduce) under a non-elevated Windows x64 token into a new
evidence directory. No unlock is required for these scratch checks, but the
read-only desktop-handle observation is session-dependent and never establishes
computer-control acceptance. Preserve strict exit `2` and every unmet assertion
when network denial remains unproven; an unexpected error/cleanup failure is
not an acceptable research result.

### Before Interactive or Privileged Trials

- [ ] Identify a named physically present operator, a supported serviced
  Windows release and the intended host/worker identities. Record the current
  source/runtime versions and agree on bounded owned fixtures and cleanup.
- [ ] Select the exact row and obtain separate approval before elevation,
  privileged tracing, a real installation/launch/registry mutation, or any
  lock/shutdown/restart. Returning/unlocking the session is not that approval.
- [ ] Prepare the row's required instrumented worker/host and emergency stop/
  recovery path. The current fixed runner does not implement dependency
  allowlisting, multi-script helpers, a protected installer, typed broker or
  computer controls. Their absence means Blocked, not permission to improvise
  an unrestricted PowerShell action or run the bootstrap as a substitute.
- [ ] Use disposable lab resources and public synthetic canaries, not real
  secrets, private content or existing installation ACLs. Do not change global
  firewall/security policy. Keep unrelated speech/deployment approvals separate.

### Deferred Containment and Deployment Trials

| ID / gate | Status | Required procedure and closure evidence |
|---|---|---|
| C01 - Supported reference repeat / W1-W3 | Not run on the supported reference matrix | Rerun the fixed owned-scratch proof on the selected supported Windows release with exact servicing revision and actual host/AppContainer/descendant token observations. Retain allowed-data/Job-only positive controls, protected before/after identities, native errors and cleanup. The original build 26300 snapshot does not certify the support matrix. |
| C02 - Attributable network denial / W1 | Blocked: current connections timed out | Prepare owned working endpoints and observe actual access-denied or an independent OS block attributable to the worker boundary, token/process and endpoint. Cover applicable loopback/non-loopback, IPv4/IPv6, TCP/UDP/DNS and descendants. A generic unrelated firewall block or timeout is insufficient. Privileged WFP diagnostics require approval; no global policy changes. |
| C03 - Dependency and fixed-action mechanism / W2 | Blocked: no enforced executable/module allowlist | Prepare a bounded fixture testing denied undeclared executable/module/script loads versus admitted dependencies under the actual worker. Ordinary child identity and breakaway tests are not an allowlist. Establish fixed-action feasibility or obtain an explicit typed-adapter/broker versus unavailable decision; do not silently replace the embedded-script contract or broaden ambient authority. |
| C04 - Protected roots, identities and aliases / W3, R17 | Blocked: no independently owned installed fixture | After separate scoped elevation/installation approval, use a distinct disposable versioned payload with independent owner/activation authority. Test the actual medium non-elevated application and contained worker tokens, payload/parent/dependency effective ACLs including delete-child/ownership/DACL rights, selected runtime/native resolution, hard links/reparse/alternate paths and target/parent-swap races. Preserve allowed-data controls and denied mutation evidence; do not modify an existing Kora installation. Follow [the security checklist](../../Design/Security_Data_Flows.md#protected-deployment-identity-and-validation) and distribution's separate runtime-only acceptance plan. |
| C05 - Bundled helper and admission integration / W4, R11 | Blocked: production catalogue/runspace/gateway absent | Prepare immutable separate resource blocks and declared helper initialization/calls/typed parameters under the selected admitted worker. Test denial of undeclared file/module/process resolution, no extraction, common direct/model/skill grants, malformed/oversized/missing/uncorrelated receipts and no duplicate/replayed effects. Fixed encoded input is not evidence for this full contract. |
| C06 - Real approved computer controls / R11, R16 | Not run; requires separately approved disruptive trial | With the instrumented gated implementation and a present operator, test only the exact registered lock/power effect separately approved for that trial, including durable session/grant binding, fresh power confirmation/countdown, safe-work handling, cancellation and observed OS receipts. Do not lock/shutdown/restart to validate this checklist now; desktop-handle denial is not an effect trial. |
| C07 - Host-death and effect/cancellation races / W4 | Blocked: no prepared abrupt-host-death fixture | Terminate only the owned test host by a stable recorded identity and verify all contained descendants stop, no breakaway/handle transfer keeps the job alive, and effect-before-receipt loss remains Unknown without retry. Race cancellation, stale/uncorrelated output and partial effects. Job close was measured; host death/reboot was not. Any reboot requires separate disruptive approval and must not be inferred from host-death results. |

### Evidence and Completion

For each row, retain date/revision/operator, OS/runtime/token identities,
approval scope, fixtures, actual versus expected outcomes, native errors or
attributable enforcement observations, individual timings/receipts and cleanup.
Label the result Pass, Fail, Blocked or Not run. Commit only reviewed/redacted
measurements; preserve original snapshots and add a separately identified run.
Do not upgrade a timeout, inaccessible/missing file or terminated process to a
denial/success/rollback claim. Unknown effects must not cause automatic retry.

Update [D-013](../../Design/Decision_Register.md#d-013-windows-worker-and-deployment-containment),
[W1-W4](../../Design/Implementation_Roadmap.md#r02-windows-containment-follow-up)
and the applicable acceptance gates only when their required evidence passes.
No outstanding row is waived by merging this proof.

### Proof code lifecycle

Retain this opt-in harness for affected W1-W4 capability/RC qualification. Move
its filesystem, credential, process-tree and receipt-classification assertions
into Windows integration tests as the production worker boundary is built.
Remove the standalone harness only after equivalent production tests pass and
the remaining real-boundary evidence is recorded and consumer/reference checks
pass; preserve historical receipts and unique lab procedures. The current
roadmap disposition inventory above owns this lifecycle.

### Merge Versus Acceptance

On 2026-10-05 the user requested rebasing and publishing the PR after recording
deferred validation. The publication scope is partial feasibility evidence,
canonical outcomes and an actionable delivery/testing plan. Unperformed
interactive or privileged trials are capability blockers, not prerequisites for
merging that limited scope. R01 is present; unrelated R02 proof implementations
are not dependencies. Normal required checks/reviews still apply, with squash
auto-merge permitted only through those gates. This does not select a broker,
enable a production worker or change the strict proof outcome to success.
