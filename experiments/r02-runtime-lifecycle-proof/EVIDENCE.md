# RT2 observations, blockers and handoffs

## Disposition

**RT2 BLOCKED for the exact approved RT1 profile.** Passing observer and
public-boundary tests is not all-path isolation acceptance. On 2026-10-06 UTC
the final guarded run passes **20/20 tests**: 13 real lifecycle trials, six
deterministic ownership/native-table tests and one live positive control.
Two receipt-locale contracts pass separately. Final identities, UTC intervals,
source/test hashes and counters are in
[disposition](evidence/disposition.json), [preparation](evidence/preparation.json)
and the [per-trial receipts](evidence/trials).

There are 14 synthetic provider requests, eight blocked model requests, zero
denied markers/credential sentinels in provider model bodies, 52 sampled owned
process identities (13 roots and 39 descendants), zero observed survivors and
zero watcher overflows. Target sampling is 20 ms; final maximum sample gap is
**121.961 ms**, not a continuous 20-ms observation guarantee. No live account,
provider computation, real credentials or charges occur.

The primary SDK and final separate clean-source build match the approved
RT1 package/assembly exactly; native pins match. Full staged, unchanged RT1
regressions pass **45/45**, preserving 44 PASS selected-profile rows and the
expected hook-only FAIL. Original RT1 and historical Node bytes remain unchanged.
Earlier source-reproduction mismatches were rejected and retained below.

## Lifecycle and inventory

| Actual path | Measured result | Disposition / limit |
|---|---|---|
| Before-startup through shutdown | Observer active before child launch, runtime/status/protocol verified, session actions bounded, observation continues through owned shutdown | Scoped lifecycle trial PASS; complete observation BLOCKED |
| Native loading/descendants | Every sampled root loads pinned `copilot-runtime.exe`/`runtime.node`. Each trial observes Windows `powershell.exe` and two `conhost.exe` descendants; PID/parent/creation times and image hashes retained | Empty mode does not eliminate helpers; unloaded/short-lived/escaped images/descendants remain unproved |
| Startup scratch writes | Live notifications include `home\AppData\Roaming` and transient `__PSScriptPolicyTest_*.ps1`/`*.psm1` create/change/delete paths | Not writer-attributed; transient contents are not retained/mediated. No assertion that all startup metadata/writes are safe |
| Environment/config paths | Original RT1 replacement environment: owned HOME/USERPROFILE/APPDATA/LOCALAPPDATA/TEMP/TMP/workspace/BaseDirectory; only Windows/system PowerShell PATH. AGENTS discovery canary is fixture input | No inherited developer auth/proxy/PATH; configuration settings are not OS enforcement |
| Destination inventory | IP Helper v4/v6 TCP/UDP snapshots show host-owned loopback listeners/connections; no native-owned socket caught in final samples. Provider is an owned ephemeral loopback endpoint | No all-destination claim. Native short connections, DNS, UDP destinations, ICMP, non-IP traffic and external content are unobserved/uncontrolled |
| Initial/history/streaming/auth | Actual final JSON and deltas; history retained; sentinel in auth header only; denied initial marker blocked before any provider arrival | Public model transport prevention PASS; synthetic auth only |
| 401/429/500/retry | Truthful session errors; at most one forwarded request; observed retries denied by final request boundary | PASS for tested HTTP route; not hosted-auth or all-runtime retry policy |
| Failed/denied tool results | Owned read executes once; unsafe receipt continuation denied before provider arrival | Public model-result prevention PASS; rich production host receipts/lineage remain R04/R08 |
| Pre-effect tool denial | Actual hook denies proposal; owned write count zero | PASS for explicit harmless custom tool only |
| Excluded built-in proposal | Empty advertised catalogue; forced PowerShell tool unavailable/failed; zero custom effect | PASS for exclusion; startup PowerShell helper still runs |
| Volatile session I/O | Actual session writes use original host in-memory provider; synthetic write denial produces session error and no success-shaped disk fallback | PASS for session provider; outside-provider native persistence BLOCKED |
| Recoverable files before cleanup | Hash/marker inspection sees fixture read input, deliberate discovery canary and the one admitted synthetic effect; no denied/credential marker in those runtime-trial files | Detection only; transient/deleted/ADS/outside-root/registry/pagefile/crash-dump paths are not a zero-persistence proof |
| Cancellation/late effect | Held request abort, request-token cancellation and loopback connection termination separately observed; cancelled later request blocked. Already admitted effect completes after abort, Unknown at cancellation | PASS for truthful bounded behavior; no rollback/billing/physical-stop claim |
| SDK wait timeout | Held connection survives wait timeout; explicit abort required | PASS regression; not MG1 host deadline/slot acceptance |
| Diagnostics | Managed System.Net event IDs/counts and runtime event-type counts only; no raw diagnostic payloads, auth headers or model content persisted | Host diagnostics detection PASS; native stderr/ETW/file channels BLOCKED |
| Disposal/shutdown | All 52 sampled PID/creation-time identities terminate; volatile stores clear, scratch removes and listeners/snapshot handles/diagnostic listeners dispose | PASS for observed set; missed/detached descendants are not certified absent |

[Positive controls](evidence/positive-controls.json) demonstrate a real held
loopback socket visible under the emitter PID, live file notifications,
recoverable marker/credential detection **before deletion**, and real managed
socket diagnostic events. That control intentionally permits synthetic marker
storage in its own named file, then deletes it; it demonstrates detection,
**not native prevention**. Provider-boundary negative trials show prevention
only on their mediated path. Zero HTTP markers plus a cleaned disk does not
prove zero unauthorized native persistence/egress.

Optional WebSockets/MCP/agents/plugins/tools/memory/spill/export remain
Not run/unavailable. No real sign-in, hosted inference, installed application,
installer launch, global policy change, machine-wide instrumentation, elevation
or dependency installation is used.

## Precise observation blocker and approval

The current effective token is not elevated. Built-in `logman.exe`/`wpr.exe`
are present, but no trace session is started. User-mode Toolhelp/IP Helper
queries and ReadDirectoryChangesW cannot establish writer-attributable
all-file/all-destination/native-diagnostic observation or prevention.
Missing paths remain **Blocked**, never accepted from absence in snapshots.

After the user asked about W2's best-effort dependency discussion, the user
explicitly selected **retain the RT2 fail-closed contract; finish Blocked
evidence and handoff**. W2's transitive dependency policy does not authorize
runtime collection, secret transmission or unmediated content persistence.
Privileged tracing is deferred, not approved here.

Next scoped approval request: a dedicated test host/operator, existing reviewed
Windows ETW tooling, at most 60 seconds per trial, only exact RT2-owned
PID/creation-time identities and descendants retained, metadata-only receipts,
no raw content/credentials, no account, no installation/policy changes, named
owned trace-session cleanup and event-loss positive controls. Kernel capture
is machine-wide before filtering; do not compete with parallel/shared tracing
or retain unrelated events. Implement/review the privileged collector before
execution. Metadata ETW alone cannot prove content prevention: separately
prove runtime native egress/write mediation or isolation under an approved
unchanged-profile mechanism, or bring an explicit D-001 decision. No
AppContainer/Job/firewall change is silently applied to this fixture.

## Failures encountered, not hidden

1. Initial root no-restore build: eight NETSDK1004 missing-assets errors.
   Locked restore then complete Release build succeeds, zero warnings/errors.
2. Nested private staging: source pack emits MSB3106 for a long dependency path;
   package fails NU1403 against the approved lock. It is rejected, not used.
   A uniquely owned temporary source cache avoids changing long-path policy.
3. First independent clean-source repeat yields package
   `e21936ed045680a6e6d240ac6c75c47d3a61763fb220a690dc3d66169bd5dea3`,
   assembly `5475b874904a5354ef6ca98e2759b114eef5556ee003005a0793dd38e2c44eeb`;
   [failed receipt](evidence/failed-long-path-reproduction.json) retained.
   A shorter fresh primary also fails NU1403. Binary inspection finds DLL
   differences, not permission to treat it as equivalent. Later unchanged
   preparation/reproduction in the verified primary staging produces both
   approved hashes. No package/source/pin/lock correction or private patch
   is made. The intermittent build-identity discrepancy has no established
   root cause and remains a reproduction-maintenance concern.
4. Fixture development builds reject xUnit1051 cancellation omissions; fixed
   explicit test cancellation. A misplaced using causes CS1529; fixed.
   Final fixture build has zero warnings/errors.
5. First guarded receipt aggregation rejects otherwise-passing tests because
   PowerShell converts JSON UTC strings to DateTime then locale parsing
   swaps month/day under en-AU. Fixed with string-preserving JSON parsing;
   two deterministic en-AU/en-US contracts pass. No freshness gate weakened.
6. Validation-receipt script initially inherits StrictMode into the existing
   DTD-bearing coverage helper's XML member projection. The new script now
   reads the document element's exact attributes; the original root coverage
   gate already passed. Coverage remains 100%/100%.
7. Full current WiX packaging reaches WIX1105: ICE validation cannot run under
   system policy without elevation. No elevation or validation suppression
   is performed. MSI/Burn release validation remains Blocked.
8. Coordinator/R04/MG1/W2 `send_message` attempts are refused by the server's
   50-message limit. Handoffs are recorded here for coordinator pickup; no
   silent claim of successful delivery or sibling uncommitted-file reuse.

## Full repository validation

[Validation receipt](evidence/repository-validation.json) binds the actual
root source/test/CI/installer bytes and TRX/coverage digests. No sibling branch
counts, Node results or installed acceptance substitute for these checks.

| Check | Actual result |
|---|---|
| Locked root restore and complete Release solution including setup | PASS; build 0 warnings/errors |
| Core / Application / Windows suites | PASS: **251 / 744 / 353**, total **1348**; zero failures/skips |
| Latest-only Core/Application line/branch coverage | PASS: **100% / 100%**, only this run's two reports |
| Production license/notice and separate RT2 lock/license/native/source checks | PASS; notices unchanged; RT2 closure 23 packages |
| Build-version, fake-release policy and installer payload rejection contracts | PASS; fake gh publication test is not real publication |
| Root win-x64 / win-x86 framework-dependent publish | PASS; exact license-bearing payloads **201 / 197** files |
| PE architecture/native hashes and no experimental candidate leakage | PASS; five binaries each RID, no application launch; [inspection](evidence/publish-inspection.json) |
| Bootstrapper self-contained win-x64 publish | PASS; no setup launch |
| Full WiX MSI ICE / subsequent Burn and installer inspection | **BLOCKED WIX1105** / **Not run** after blocking error |
| Privileged ETW, installed/provider/reference-hardware acceptance | Not run; separate approval/prerequisites required |

Runtime scratch and intentionally persisted control markers are gone. All
observed identities have exited. Verified ignored acquisition/build caches
are retained for reproduction; failed named caches are cleaned separately.
During the trials, no primary/sibling checkout mutation, commit/push/PR,
worktree removal, process-name kill or unowned resource cleanup was performed.
The source/evidence/Design deliverables may subsequently be committed and
reviewed without changing the recorded technical disposition.

## Handoffs

- **R08/D-001:** carry exact SDK/native/fixture pins, original pre-effect and
  whole-request gates, volatile I/O and negative/error/cancellation tests.
  Do not compose a production adapter while RT2 is Blocked. Resolve native
  paths, transient write contents, all-destination/diagnostic attribution and
  actual prevention, then repeat integrated host/source-lineage/streaming/
  backpressure/late-presentation evidence with R04/R05 authority contracts.
- **MG1:** independent byte/deadline/admission envelope remains its owner.
  SDK timeout is not abort, and already admitted effects can complete after
  acknowledgement. Keep unknown-termination slot/conversation quarantine;
  zero sampled survivors or loopback termination is not provider-compute or
  rollback evidence. Do not consume RT2's generic bounds as MG1 acceptance.
- **PV1:** no account/terms/model/region/cost approval. First clear RT2 and
  relevant MG1, prepare intended-account eligibility/spending controls, then
  request explicit approval. Repeat real authentication, destination/
  diagnostics/persistence and cancellation/account-capacity paths. Synthetic
  401/header tests do not authorize live use.
- **R04:** experimental activity/SDK IDs are causal correlation only. R04 owns
  durable host session/task/invocation/audit identity, admitted Activity context,
  authoritative audit commits and encrypted-storage admission. No storage gate
  or R04 dependency is added/closed by this experiment.
- **W2/coordinator:** RT2 observes runtime helpers, not exact dependency
  enforcement for granted scripts. W2 best-effort transitive tracking is a
  separate explicit product decision; protected-resource/runtime-egress gates
  remain intact. Reconcile only RT2-specific canonical paragraphs when merging
  parallel work; never copy sibling uncommitted files or share mutable fixtures.
