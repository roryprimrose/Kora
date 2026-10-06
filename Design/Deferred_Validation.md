# Deferred Proof Validation

Status: outstanding validation register, not passed acceptance or permission
to run a test. Partial feasibility evidence and scoped implementation may merge while
unproven capabilities remain gated and their decisions/acceptance gates stay
open. Merge is not certification for production release.

Use this page to plan remaining validation. Safe file/database-only scratch
reruns may run remotely while the physical console is locked; only rows
requiring live input, actual effects or protected setup need the corresponding
interactive/privileged preparation.
Read the linked proof's prerequisites and consent boundaries before preparing
any trial. Being back at the machine is not approval to install, elevate,
capture audio, change security policy, lock, shutdown or restart it.

Related: [Acceptance Criteria](Acceptance_Criteria.md),
[Implementation Roadmap](Implementation_Roadmap.md),
[Decision Register](Decision_Register.md).

## Proof Checklists

| Proof | Existing evidence / runnable checks | Deferred validation and preparation | Gates still open |
|---|---|---|---|
| R02 speech/hardware | [Merged synthetic proof and safe file-only reruns](../experiments/r02-speech-proof/README.md#safe-to-rerun-remotely-including-while-locked) | [Before a live test session](../experiments/r02-speech-proof/README.md#before-a-live-test-session), then [live/instrumented acceptance](../experiments/r02-speech-proof/README.md#live--instrumented-acceptance-work-still-outstanding). Obtain participant/bystander consent and an instrumented host with R03/R09 ownership/privacy controls; the current scripts cannot run live trials. | D-002/D-007; packaged acoustics, playback rejection, latency, reference floor and capture/recovery acceptance |
| R02 storage/key | [Synthetic storage proof and safe Windows reruns](../experiments/r02-storage-proof/README.md#reproduce); authenticated content, DPAPI/key-file ACLs and transaction/artifact interruption evidence | [Storage admission follow-up](#storage-admission-follow-up): maintained native selection, installed x64/x86 loading, production profile-path/CurrentUser/permission integration, and integrated recovery/deletion. Safe proof reruns require a loaded Windows profile, not an unlocked console. Routine second-account OS-denial trials are optional for profile-local storage. | D-009; R02 native admission, R04 integration and R12 lifecycle/deletion remain open; no production store is enabled |
| R02 local inference | [Safe deterministic reruns](../experiments/r02-local-inference-proof/README.md#safe-deterministic-reruns), plus the [bounded production-host trial](#2026-10-05-bounded-local-inference-result): clean one-approval Ollama/model setup, exact digest verification, real answers, loopback-only observation, visible cancellation and repeatable session-controlled teardown | [Prepare an interactive inference session](../experiments/r02-local-inference-proof/README.md#before-an-interactive-inference-session), then complete the remaining [LI01-LI07 deferred trials](../experiments/r02-local-inference-proof/README.md#deferred-inference-trials). Assign reference/isolation owners and agree budgets; separately approve exclusive model residency changes and whole-environment network blocking. Installer provisioning, CPU-floor measurements, server-cessation/races, attributable offline egress and repeated instrumented cancellation remain outstanding. | D-003 and inference D-007; [R02-L1-L6](Implementation_Roadmap.md#r02-local-inference-continuation), actual CPU-floor quality/context/resource budgets, installer distribution and independent offline success; R06/R07/R08/R10 and A2/R19 integration remain gated |
| R02 runtime/provider | [Historical Node checks](../experiments/r02-runtime-proof/README.md#reproduce-on-windows) unchanged; separate [actual .NET RT1](../experiments/r02-dotnet-control-proof/README.md) source-built profile passes 45/45 tests, with rejected hook-only FAIL retained | [Runtime/provider follow-up](#runtimeprovider-follow-up): preserve exact RT1 pins; instrument RT2 lifecycle paths and implement MG1 complete .NET envelope. Released NuGet byte parity blocked; prepare separately approved account/usage trials. RT1 commands do not establish full process observation, management envelope or live eligibility. | D-001/D-004/D-010; RT2/MG1/PV1, R08 exposure and model-assisted R13 remain gated; deterministic local management does not wait for hosted trials |
| R02 Windows containment | [Original owned-scratch proof](../experiments/r02-containment-proof/README.md#reproduce) retains 63/71 assertions and eight unproven network denials. [Independent W2 fixture](../experiments/r02-w2-dependency-proof/README.md) observes native ACL/child-policy denials, undeclared execution bypasses, separate embedded helper/entry fixed effect and Unknown/cancellation receipts; strict candidates rejected | Owner accepts best-effort transitive tracking/user responsibility for bundled/future scripts, with all manifest files in review tabs and exact declared hashes. Implement honest gap/invalidation tests, not universal undeclared-code denial. Native fixture loading blocked: supply an existing reviewed x64 C compiler or separately approve tool acquisition; no installation performed. Then independently prove protected runtime resolution, actual Windows control APIs, inherited object identity and complete helper/lifecycle/effect-race contracts. W1 attributable network and W3 installed ownership/ACL/alias trials retain separate consent/evidence. | D-013 technical admission and [W1-W4](Implementation_Roadmap.md#r02-windows-containment-follow-up); R11/R16/R17 worker/installed exposure remains gated; no broker selected |
| R02 distribution | [Proof #24](https://github.com/roryprimrose/Kora/pull/24) supplies historical receipts; [WiX MSI/custom Burn implementation](../installer/README.md) supplies binary packaging/CI and managed regressions, not a containment implementation dependency | NSIS acquisition/build and Linux recipe are retired; source/native checks remain. Unsigned beta/stable POC publication uses approved front-loaded/risk-based validation, not exhaustive manual installation of each MSI. Obtain separate scoped approval and disposable lab deployments for installed lifecycle/logon/all-users/upgrade, effective ACL/token/native loading and recovery. Silent related-bundle upgrades and numeric-beta upgrade ordering are unsupported. | D-005/R17 managed-source, installed/protection/resource acceptance remains open; no worker/catalogue/storage admission from package inspection, publication or process creation |
| R03 Windows ownership/audio privacy | [Implementation PR #26](https://github.com/roryprimrose/Kora/pull/26), portable policy/race tests, non-disruptive Windows object/enumeration tests and x64/x86 builds/publishes, plus the [2026-10-05 bounded interactive results](#2026-10-05-bounded-interactive-result); not complete lifecycle acceptance | [R03 interactive checklist](#r03-windows-ownership-and-audio-privacy). Prepare an instrumented, non-elevated test host and obtain separate approval for each remaining capture/playback, launch/handoff and OS-transition trial. Production wake is not selected or enabled by R03. | Complete A01/A02/A05/A07 coverage; capture release within 500 ms of the observed lock event in every A03 reference trial; A04 device/permission changes; A06 unclean recovery; hardware matrices, native timing and installer-provisioned x86 runtime evidence remain open |

Each proof-specific checklist owns its detailed procedures; this register
does not replace them or weaken their separate consent requirements. Local
inference isolation proves the answering environment's remote-egress boundary,
not the containment worker's attributable network denial. Speech, storage,
containment and distribution receipts cannot qualify an untested inference runtime or
authorise its provisioning/network changes. The
distribution entry records published proof/design coordination, not a claim
of production installer acceptance. Preserve both distribution and
containment roadmap plans when integrating that branch.

### W2 Safe Trial and Validation Disposition

The 2026-10-06 W2 fixture targets experiments only, based on
`3e8558fbff07943d39721b230599b02062d90d57` (merged #33/#34/#35).
It uses Windows x64 owned scratch, a synthetic current-user AppContainer and
fixed compiled/embedded stand-ins, with no network probe, production computer
control, installation, elevation or global policy change. Actual image hashes,
immutable script/definition/closure inputs, UTC receipts and cleanup are retained
in [W2 evidence](../experiments/r02-w2-dependency-proof/README.md#evidence-and-validation).

Full root Release solution build (including setup) and all Core/Application/
Windows suites pass with latest-only Core/Application 100% line/branch coverage.
License/notice/version/publication-policy and payload-negative checks pass;
x64/x86 publish and payload inspection are not installed acceptance, and x86
is not the offered installer target. Normal non-skipped WiX MSI ICE validation
was attempted and **Blocked/failed with WIX1105 by system policy**. Do not
elevate or disable policy to pass; no release packaging gate is waived.

## 2026-10-05 Safe Revalidation and Proof-Code Disposition

The documented non-interactive checks were rerun on Windows with the pinned
.NET 10.0.401, Node 24.16.0 and CPython 3.12.10 toolchains. No microphone,
audible playback, application/installer launch, protected deployment, model
provisioning/generation, elevation, network-policy change or disruptive
lifecycle action was performed.

| Proof | Revalidation outcome | Design consequence |
|---|---|---|
| R02 speech/hardware | Dependency/source validation and all 15 deterministic tests passed. The 60-second file-only benchmark reproduced the existing broad result: the small synthetic corpus changes materially with threshold and still admits synthetic TTS wake events. | No production threshold, candidate or hardware floor is selected. D-002/D-007 and every acoustic/packaged-host row remain open. |
| R02 storage/key | Release build and all 152 automated checks passed; win-x64/win-x86 native assets published and were inspected without execution. | The D-009 direction remains feasible but not production-admitted. Maintained-native, installed-load, integrated recovery and lifecycle gates remain open. |
| R02 local inference | Release build and all 31 deterministic self-tests passed. A later bounded production-host trial installed the exact Ollama/model pins through Kora, verified the digest and real inference, exercised simple and long answers, rejected malformed/empty structured output, and cancelled active model/speech work without a stale completion. | The candidate remains provisional. Production provisioning/reasoning/cancellation feasibility is now real rather than synthetic, but CPU-floor quality, latency/resource/context budgets, installer provisioning, repeated race timing and independent offline/egress evidence remain open. |
| R02 runtime/provider | All 16 host/runtime tests passed. The evidence command truthfully returned `2`: 13 rows passed, the hook-only failed-result path failed, and three real-boundary rows remain blocked. | Keep the final request boundary mandatory; hook-only integration remains disabled. D-001/D-004/D-010 and .NET/live-provider parity stay open. |
| R02 Windows containment | Build and deterministic checks completed; the OS matrix retained 63 of 71 passing assertions and returned `2` for the same eight unproven network-denial assertions. | Keep AppContainer plus job control as a filesystem/credential/lifetime candidate only. Network-denied execution and W1-W4 remain unavailable. |
| R02 distribution | The historical source published successfully from a short dedicated root; its 81-file payload inspection, unsigned NSIS build, 17 orchestration checks and 9 static publish/packaging checks passed. Strict hashing also exposed and rejected a SourceForge HTML response before extraction. | Production remains WiX MSI + Burn under R17. Managed proof roots must be short for the historical SDK/MSBuild graph; official redirected downloads use `curl.exe` and remain hash-pinned. Static inspection does not clear redistribution, runtime-only, protection or lifecycle gates. |
| R03 Windows ownership/audio privacy | Locked restore, Debug/Release builds, 251 Core tests, 742 application tests, 188 Windows tests, win-x64/win-x86 publishes and the unchanged 100% line/branch coverage gate passed. Bounded HyperX input/playback and Debug/Release ownership trials also passed after the defects below were corrected. | The implementation regression surface and tested PTT, playback, clean-exit and x64 handoff paths are healthy. Production wake remains unavailable. The bounded runs contribute partial A01/A02/A05/A07 evidence only; their unexercised cases and A03/A04/A06 acceptance remain open. Framework-dependent x86 launch is blocked until the installer provisions the x86 Desktop Runtime. |

### 2026-10-05 Bounded Local-Inference Result

The operator separately approved the running application's setup workflow and
required teardown to remain session-controlled rather than app-owned. The
following production-host evidence passed:

- A clean baseline had no Kora/Ollama process, package, executable, endpoint,
  selected model or continuity marker. Kora then installed Ollama `0.35.1`,
  downloaded `qwen3:1.7b`, accepted Ollama's equivalent unprefixed SHA-256
  representation, verified the exact pinned digest, ran real inference and
  refreshed readiness in one approval.
- Targeted teardown removed only the selected model, identified test-owned
  process and `Ollama.Ollama` package. A second clean setup reproduced the
  result after startup polling was changed to tolerate Winget returning before
  the package-started server became responsive.
- Existing PowerShell `7.6.6` satisfied the `7.4` minimum and passed the
  no-profile, noninteractive health check. It predated the trial and was
  neither installed nor removed.
- Settings startup displayed Ollama/model and PowerShell readiness without
  requiring review-button selection or mutation consent. Installation still
  required explicit approval.
- A simple unmatched request returned a relevant local answer. During the
  observed request, Kora connected only to Ollama on `127.0.0.1:11434`;
  Ollama had no remaining external connection after provisioning.
- A long structured request exposed an empty `response` caused by hidden
  Qwen3 thinking consuming the bounded output. Production now sends
  `think: false`, rejects empty structured responses and reports malformed
  model contracts without exposing raw parser errors. The corrected request
  produced a spoken answer.
- **Cancel task** remained visible while model work or response speech was
  active. Both the button and **Esc** stopped the active work; no stale answer
  or action appeared, and a following built-in command completed normally.
  **Dismiss** remained presentation-only and did not stop speech. Enter in the
  typed response prompt dispatched the existing Run command.

This closes the bounded production setup/reuse and basic integrated
reasoning/cancellation feasibility gaps only. It does not establish the LI01
reference environment, LI03 quality/performance/resource budgets, LI04 full
context envelope, repeated LI05 race/computation-cessation timing, LI06
independent offline capture, installer provisioning or final LI07 disposition.
The local-inference proof therefore remains required.

The proof code is retained only while it owns evidence that has not yet moved
to the production implementation:

| Proof code | Retention decision and removal gate |
|---|---|
| Speech | Retain the deterministic capture/benchmark harness through wake-candidate selection and R09 packaged acoustic validation. Migrate reusable bounds/race assertions into production tests, then remove the Python/model-specific harness when its historical receipts are sufficient. |
| Storage | Retain through maintained native selection and R04/R12 integration because it is the only repeatable crypto, interruption, migration and deletion comparison. Remove candidate-specific prototype paths after equivalent production recovery/native-load tests pass. |
| Local inference | Retain through LI01-LI07 and R06-R08/R10 adapter delivery; it owns the exact candidate rubric and deferred measurement procedure. Remove it only after those cases are covered by production adapter/integration tests and final evidence. |
| Runtime/provider | Retain and rerun on every SDK/runtime pin change until the isolated .NET fixture and production host-envelope tests supersede it. The known hook-only failure must remain executable until the unsupported path is impossible in production composition. |
| Containment | Retain through W1-W4 and protected deployment integration. Migrate filesystem, credential, process-tree and receipt-classification assertions into Windows integration tests before deleting the standalone harness. |
| Distribution | NSIS acquisition/authoring/build code and Linux recipe are retired; reviewed historical receipts remain unchanged. WiX now owns exact payload/provenance checks and CI tests that reject transferred-payload tampering before compilation/staging. Retain managed-source orchestration and native/runtime/import inspection with their tests/helpers until equivalent maintained R17 tooling supersedes them; binary setup does not close those source/native gates. |
| R03 ownership/audio privacy | These are production implementation and regression tests, not disposable proof code. Retain them normally; add separately instrumented acceptance fixtures rather than replacing unit/integration coverage with manual receipts. |

## Storage Admission Follow-Up

The [profile-boundary contract](Architecture.md#profile-boundary-and-validation-responsibility)
trusts Windows per-user isolation but requires Kora to demonstrate correct
use of it. A profile-local path alone does not cover permissive ACLs,
LocalMachine wrapping, shared staging or unkeyed backup/export copies.
The storage research may merge with these application/deployment gates open:

| Follow-up | Owner / package | Required evidence and scope |
|---|---|---|
| S1 - Admit maintained native assets | Storage/release leads, R02 then R17 | Review engine/provider provenance, notices and servicing; rerun synthetic authentication/recovery tests on the candidate and demonstrate installed Windows x64/x86 loading. Package publication alone is insufficient. Installation/protected setup needs separate bounded approval. |
| S2 - Integrate the profile boundary | Storage/application leads, R04 | Verify production CurrentUser wrapping without machine/shared fallback, every managed path/copy under the intended local profile, and effective directory/key-file ACLs. Exercise unavailable profile/key/permissions without replacement data or success-shaped fallback. Use owned synthetic fixtures, not the user's existing database. |
| S3 - Integrate recovery and migration | Storage/application leads, R04 | Interrupt key-wrapper/backup-generation and artifact publication, verify legacy-source preservation and rekey recovery, and demonstrate real durable intent/receipt recovery without automatic dispatch. Define final host types independently of the private prototype fixtures. |
| S4 - Integrate deletion and lifecycle | Storage/security/application leads, R12 | Exercise source revocation, late appends, live/unknown-work holds and configured lifecycle; remove or rewrite managed recoverable copies while preserving unrelated sessions and independent grants. Disclose exported/provider/forensic limits. |

No second-account denial result is claimed. Optional actual-account
corroboration becomes required if introducing shared storage, service or
impersonated identities, cross-profile import/migration or custom cross-user
authorization, or investigating inconsistent effective permissions.
Such trials need approved real accounts and explicit fixture/effect scope;
the retained [optional handoff protocol](../experiments/r02-storage-proof/README.md#optional-real-cross-user-handoff-protocol)
does not create an account or grant authority to test another profile.
Per-user DPAPI is not same-user worker containment; D-013/W1-W4 remain separate.

R04 now has independently safe source/test contracts as recorded in the
[foundation inventory](Implementation_Roadmap.md#r04-foundation-delivery).
Re-run the final integrated provider/key/schema on the selected native
closure; the old R02 provider results cannot qualify a different engine.
The production encrypted sink/store deliberately remains unavailable.
S1 native/licence/installed admission, S2 composed production-path custody,
S3 actual SQLite/backup/key rotation/legacy conversion and process interruption,
and S4 lifecycle/deletion are not closed by fake-store ordering tests,
key/artifact scratch tests or publish inspection. Prepare a separately
approved disposable installed x64/x86 lab for S1, and a bounded owned-data
fixture for integrated S2/S3 before enabling content persistence.
The selected **evaluation** route is SQLite3MC.PCLRaw 2.4.0, not a shipping
closure; [D-009](Decision_Register.md#lifecycle-and-integration-closure)
records the source-backed identity/licence findings and exact-package TLS
blocker. Retain and verify all actual provider/native payloads before
compatibility, authenticated-configuration and installed trials. Synthetic
key/artifact tests exercise CurrentUser DPAPI and actual owned-directory/file
ACLs, but no production-profile permission repair, key rotation, database
backup/conversion, actual process-kill or installed security result is claimed.

## Runtime/Provider Follow-Up

Status: **RT1 selected source-built profile passes; RT2 bounded observations
recorded, all-path gate Blocked; MG1/PV1 remain independently open**.
The [actual .NET fixture](../experiments/r02-dotnet-control-proof/README.md)
passes 45/45 tests, with 44 selected-profile PASS rows and one expected
rejected hook-only FAIL. Released NuGet byte parity remains Blocked after
TLS acquisition failures and an explicitly approved exact-tag source-build
alternative. This does not close Gate 0. The Node experiment has 13 PASS,
1 FAIL and 3 BLOCKED outcomes, not production acceptance. Hook-only
failed-result mediation is rejected; the final-request-gated candidate
continues only through the [technical plan](Runtime_Provider_Feasibility.md).
Merging the experiment closes no remaining gate or production acceptance.

### Preparation and Approval

- Start with the existing [no-account reproduction](../experiments/r02-runtime-proof/README.md#reproduce-on-windows)
  on the exact pinned Windows/Node/runtime versions. Use only synthetic context,
  credential sentinels and harmless tools. These checks do not need hosted
  credentials or an unlocked interactive console; they are not proof of global
  runtime network/storage containment.
- The [RT1 fixture](../experiments/r02-dotnet-control-proof/README.md)
  now supplies actual supported .NET APIs, source/native hashes, separate
  locks/license closure, UTC counters and owned cleanup. Reproduce its
  preparation, clean-source package and all 45 tests without installs/accounts.
  The separate [RT2 fixture](../experiments/r02-runtime-lifecycle-proof/README.md)
  records 20/20 safe tests and two receipt-locale contracts, exact final
  source reproduction and all 45 staged RT1 regressions without rewriting
  the historical evidence. RT2 still needs complete attributable
  network/file/diagnostic observation and native prevention; MG1 needs
  the corresponding .NET host envelope. Neither the Node fixture nor RT1's
  generic trial timeouts/status payloads implement those gates.
- Record source, SDK/native-runtime hashes, Windows version and fixture/
  instrumentation identity. Agree operator, reference environment, scope,
  deadline tolerances and stop/cleanup procedures before a trial. Obtain
  separate approval for installs, elevation, policy/network changes or
  protected setup; never disrupt unrelated processes or global policy.
- Before live provider inference, name the intended service/model/region,
  supported user authentication, plan/organization restrictions, permitted
  assistant/SDK use, concurrency and enforceable usage/spending budget.
  Obtain explicit account and potentially paid-usage approval. Merge approval
  and an unlocked console authorize neither sign-in nor billable calls.
  Never borrow ambient developer credentials or include tokens, account names,
  raw sensitive payloads or credential-bearing diagnostics in evidence.

### Deferred Trials and Closure Evidence

| Trial / owner | Current state and prerequisite | Required trial and evidence |
|---|---|---|
| RT1 - .NET public control points / runtime lead | PASS, scoped exact-tag source build; 45/45 actual-runtime tests; released NuGet byte parity Blocked | [Disposition/receipts](../experiments/r02-dotnet-control-proof/evidence/disposition.json): actual SDK v1.0.16 source/runtime 1.0.90, final initial/history/all-result/exception paths, tool denial, streaming/auth/errors, volatile I/O and failure, cancellation and lane/provider isolation. Denied effects/markers forwarded zero; hook-only FAIL retained. No public controls missing in tested profile; no private patch/Node bridge. Changed artifact/profile must repeat RT1; D-001 remains open. |
| RT2 - Full runtime lifecycle / runtime and security leads | BLOCKED, 2026-10-06 UTC; 20/20 bounded actual/control/observer tests + two locale contracts pass; exact final SDK reproduction and 45/45 staged RT1 regressions pass | [Observed inventory/limits](../experiments/r02-runtime-lifecycle-proof/EVIDENCE.md): native PowerShell/conhost descendants and transient policy-test writes; 52 sampled identities terminate, zero denied model markers. Watcher lacks writer/content attribution, snapshots miss native traffic/images/descendants, native diagnostic/outside-scratch content paths unproved. User retained fail-closed RT2 contract and deferred privileged tracing to a dedicated host. Obtain scoped approval/collector review and loss controls, then prove complete paths and actual native prevention/mediation; metadata tracing alone is insufficient. Repeat approved account paths in PV1. Unknown/uncontrollable paths stay Failed/Blocked; W2 best-effort transitive tracking does not relax runtime privacy. |
| MG1 - .NET management envelope / runtime lead | Not run; scoped RT1 prerequisite satisfied; may run alongside RT2 | Prove complete serialized UTF-8 input at 32768/32769 bytes and complete typed output at 4096/4097 bytes, counting framing/history and multi-byte text. Hold real inference across the host 15000 ms dispatch deadline and stall SDK acknowledgements; record actual elapsed timing without extending the configured deadline. Verify one in-flight request, 30 attempts/rolling hour including failures, at most one forwarded inference/no SDK retries, manager responsiveness with two execution conversations held, no lane/context leakage, uncertain-termination quarantine and deterministic fallback. RT1 SDK wait-timeout/non-cooperative-tool evidence reinforces that acknowledgement cannot certify rollback or release an uncertain slot. |
| PV1 - Live account/provider / runtime lead, account owner and security/legal review | Not run; no hosted account/usage approval; RT1/RT2, plus MG1 for management | Use the intended user's supported secure authentication; test expiry/denial/throttling/errors, exact destination/content isolation and truthful cancellation. Record approved terms/plan/model scope, actual concurrency/quotas/rates, billed-cost assumptions and hard spending controls. Execution eligibility feeds D-001/R08; management additionally needs actual two-execution-plus-manager capacity/budget evidence for D-004/R13. Incompatible/unapproved or unbounded service stays disabled; do not infer hosted allowance from three loopback conversations or byte counts. |

Keep the hook-only FAIL as a regression witness; accepting a different proved
profile does not require making that rejected approach pass. For each deferred
trial record Pass/Fail/Blocked/Not run, exact tested profile, UTC timestamp,
observer, synthetic counters/receipts and unresolved limitations. Do not replace
failure with a timeout or a successful fake. Retest affected paths whenever the
SDK/runtime, endpoint/model/auth, transport, storage or admitted feature changes.
R08/R13 still need their own integrated host, scheduler and installed-app
acceptance; this checklist does not enable production adapters or tools.

RT2's [handoff](../experiments/r02-runtime-lifecycle-proof/EVIDENCE.md#handoffs)
specifies the dedicated-host metadata-only tracing scope, exact
PID/creation-time attribution, bounded duration, loss/positive controls and
owned trace cleanup. No privileged collection is authorized in the shared
parallel environment. R04 owns durable host correlation/audit admission;
fixture/SDK IDs and sampled quiescence do not establish authority or release
an MG1 unknown-termination slot. Root build/tests/100% coverage pass, while
full local WiX ICE validation is separately Blocked by WIX1105; publish
inspection or the RT2 fixture does not replace installed acceptance.

## R03 Windows Ownership and Audio Privacy

Status: **bounded A01/A02/A05/A07 evidence recorded; no row is fully closed**.
An approved non-elevated physical-headset trial exercised application launch,
armed-idle behavior, held PTT, local Windows recognition, spoken output and
ordinary tray exit. A later approved continuation exercised Space/Enter PTT,
focus-loss closure, empty speech, Preview, Stop speaking, exit during playback,
same-build activation and x64 Debug/Release decline/takeover/return. It did not
exercise the complete consent/startup matrix, screen-reader behavior,
maximum/duplicate activations, device or permission changes,
lock/disconnect/suspend, active-work handoff refusal, intentional termination
or crash recovery. Tests using fakes and native object/device enumeration
remain supplementary only. Publishing x86 does not prove x86 execution when
the required framework runtime is absent.

The scoped implementation has saved device/profile-local microphone consent,
fresh-gated ordinary startup, run-scoped explicit recovery, held PTT, bounded
generation-tagged capture, external privacy observation and native recovery.
It does not implement/select production wake, durable sessions, general grants,
model adapters or script execution. Do not exercise those future capabilities
or infer that the R02 containment experiment is part of this host.

### 2026-10-05 Bounded Interactive Result

The operator confirmed physical presence and bystander consent for a bounded
trial using the HyperX Cloud Alpha Wireless microphone and headphones. The only
approved phrase was "Kora, what can you do?". No lock, suspend, disconnect,
installation, elevation, network-policy or power action was approved.

| Scope | Result and evidence | Remaining gate |
|---|---|---|
| A01 subset - ordinary launch and armed-idle state | Kora launched as the non-elevated interactive owner. Enabling listening armed PTT without ambient capture; no response without PTT was expected because production wake is unavailable. | Clean-profile grant/decline, persistence/withdrawal, disable/re-enable and build-partition cases remain unrun. |
| A02 subset - explicit physical PTT | Held mouse, Space and Enter PTT opened the selected HyperX endpoint and admitted one command per activation. Windows SAPI recognized the initial approved command at confidence `0.82509285`; later keyboard trials also dispatched exactly once. Focus loss closed capture without dispatch, silent release reported no command, and release during native open cancelled fail-closed and required explicit re-enable. | Screen-reader behavior, maximum/failed activations, delayed/duplicate callbacks, detailed queue/sample bounds and a wider hardware matrix remain unrun. |
| A05 subset - ownership and return | A second identical Release x64 launch activated the authenticated existing owner and exited `0`. A Debug x64 candidate was declined without changing ownership, then accepted with exactly one active tray/UI; its clean exit offered and completed exact-original Release x64 return. Both former processes exited and only the exact original path remained. A framework-dependent Release x86 candidate failed before handoff with the standard missing x86 .NET Desktop Runtime dialog; it exited without disturbing the x64 owner. | Active-work refusal, expiry, candidate death, lock during approval, abort, an installed/runtime-complete x86 candidate and unclean replacement recovery remain unrun. Installer acceptance must prove the required x86 runtime before cross-architecture handoff can close. |
| A07 subset - native output and exit | Spoken responses and explicit voice Preview completed through the selected HyperX headphones. Stop speaking halted active playback promptly. Exit during active speech released playback, disposed text-to-speech, terminated the process and deleted the continuity marker after the shutdown defects below were fixed. | Screen-reader navigation, System-default rerouting, unavailable/muted output, privacy closure during queued playback and acoustic playback rejection remain unrun. |
| A03/A04/A06 | Not run; no approval was given for disruptive session/device/permission/failure trials. | All specified closure evidence remains open, including the 500 ms lock-release target. |

The trial exposed defects that deterministic tests had not represented:

- generated XAML members were unavailable during settings startup;
- native buttons consumed ordinary routed PTT handlers;
- host teardown attempted an invalid Avalonia lifetime mutation;
- SAPI required no-op seek compatibility and reads spanning short WASAPI
  packets;
- audio cleanup failures could escape asynchronous UI boundaries;
- endpoint property notifications caused refresh storms, and native selector
  reset could transiently clear a selected microphone during capture;
- Preview could execute with no explicitly selected voice; and
- native cleanup could wait indefinitely before acquiring its lifecycle lock;
- Avalonia Exit synchronously disposed asynchronous services on its UI context;
- speech continuations and provider disposal could target Avalonia's retired
  synchronization context; and
- output invalidation stopped WASAPI while holding the lock needed by its
  synchronous completion callback.

The implementation now resolves named controls explicitly, observes handled PTT
events in the tunnel route, uses verified host completion, provides a bounded
SAPI-compatible stream adapter, contains audio failures visibly, preserves
selection across topology refresh, ignores non-topology endpoint property
noise, revalidates Preview inputs and bounds lifecycle-lock acquisition.
Avalonia now disposes UI controllers only, host-finally starts provider disposal
off the retired UI context, text-to-speech avoids capturing UI synchronization,
and WASAPI stop runs outside the completion-state lock. Unconfirmed cleanup
fails closed and requires restart. These fixes are retained as production code
and regression coverage; they do not broaden the evidence above into production
wake or complete R03 acceptance.

### Preparation and Approval

- Record exact source/artifact hashes, supported Windows servicing build, CPU,
  memory, power profile, physical endpoint IDs, runtime/recognizer versions,
  operator and participants. Use disposable profiles/data and synthetic commands.
- Obtain fresh, bounded test approval covering participants/bystanders,
  microphone/playback endpoint and volume, duration, retention/deletion and a
  visible immediate stop path. Saved microphone consent is not trial approval.
- Obtain separate explicit approval before app launch/ownership transfer,
  process termination or lock/disconnect/suspend. Being unlocked, approving this
  documentation or merging the PR authorizes none of those actions.
- Prepare monotonic timestamp hooks for OS notification, capture handle release,
  buffer clearing, last output sample and dispatch receipts. If hooks, native
  recovery or stable process identities are missing, mark the row Blocked.
  Measure only the owned host; never terminate unrelated applications by name.
- Start capture only as the verified non-elevated owner in an authoritative
  unlocked/connected session. Record RDP/redirection/virtual endpoints separately;
  they do not certify physical headset or speaker/microphone behavior.

### Interactive Trials and Closure Evidence

| ID | Procedure and required result | Closure evidence |
|---|---|---|
| A01 - Consent and ordinary startup | On a clean disposable profile, enumerate without capture; exercise grant/decline, ordinary startup with saved consent, run disable/re-enable and persistent withdrawal/restart. Distinguish armed PTT from actual recording. Permission/device availability alone grants no capture. Debug/release partitions do not copy consent. | Consent/profile/build identities, individual capture-open/close observations and fresh-gate decisions. Withdrawal or persistence failure never keeps capture open. |
| A02 - Explicit command and stale generations | Hold mouse/Space/Enter PTT and use harmless commands such as help/open settings; release or lose focus/close the control. Exercise early result during open, early release, empty speech, maximum duration, failed open and delayed/duplicate callbacks. Verify first command words, bound receipts and exactly one admitted dispatch. | Per-activation generation, sample/queue/transcript bounds and timestamps; no unactivated audio in command transcription and no retired callback dispatch. Hardware/offline Windows ASR behavior is measured, not inferred from fakes. |
| A03 - External session privacy | Separately approve Win+L/idle lock, disconnect, suspend/resume and applicable session transitions while an owned activation/output is active. Capture closes, buffers clear, output stops and sensitive presentation hides. Unlock/reconnect/resume requires explicit recovery and cannot replay audio/approvals. | **Capture released within 500 ms from the observed lock event in every reference trial**. Record OS event-to-notification delay separately, plus each observed-event-to-release duration, buffer clearing, last output sample and zero stale dispatch; no averages/p95 substitution for this target. |
| A04 - Permission and device changes | With approved capture/output fixtures, revoke/restore desktop microphone permission; remove/disable pinned endpoints; change System-default input/output; hot-plug and refresh. Active System-selected WASAPI streams reroute to available new defaults; pinned endpoints and streams are unaffected by unrelated device/default changes. Missing or muted effective endpoints close affected audio without substituting a same-name device. Restored readiness never removes a run hold. | Endpoint/permission revisions, successful eligible rerouting and pinned-route continuity, native failure/closure receipts, individual timings, bounded audio clearing, stopped unavailable output without replay and explicit input recovery after closure. Do not change global privacy settings without separately scoped approval. |
| A05 - Cross-build owner, handoff and return | Launch validated same-build and different-build candidates across approved paths/versions/x64/x86. Verify activation without startup argument dispatch, one assistant owner/tray, inactive candidate, native default-deny approval, active-work refusal and full release before transfer. Exercise decline, expiry, candidate death, lock during approval, abort and explicit exact-original return. | OS-authenticated process/SID/session/creation/content identities, approvals, held-handle/owner epochs, actual desktop/service/capture quiescence and zero simultaneous owners. Return is lifecycle-only before explicit acceptance; no task/grant/audio/consent transfer. |
| A06 - Unclean ownership and failure recovery | In a disposable instrumented host only, separately approve stable-identity process termination and preparation/transfer failure. Unknown/orphaned effects must block automatic crash takeover/return. Changed/elevated/cross-session/unknown identities deny. | Correlated process/job/resource outcomes, continuity marker and explicit blocker/reconciliation receipts; process death alone is not proof of worker quiescence or permission to delete a marker. |
| A07 - Native fallback and private output | Without model/network/optional speech dependencies, exercise tray/settings refresh, revision-bound endpoint selection, enable/disable, PTT, Stop speaking, keyboard/focus/screen-reader paths and locked presentation denial. Test in-flight/queued synthesis through eligible System-default rerouting and unavailable-route/privacy closure so retired audio cannot start late. | Native UI/accessibility observations and last-output-sample/generation receipts under supported headset and speaker/microphone setups; keyboard focus loss ends held PTT, and stale menus do not authorize a substitute. Production wake, acoustic playback rejection and interruption quality remain owned by the separate speech gates. |

For every row, retain approval scope, artifact/revision and machine identities,
trial ID/expected versus actual result, individual timing/dispatch/resource
receipts, positive controls, native errors and cleanup. Keep raw recordings
local under agreed retention; commit only reviewed, content-minimizing/redacted
measurements. Add a new dated run, not a replacement for historical evidence.
Mark Pass/Fail/Blocked/Not run and close only the corresponding acceptance gate
when its complete evidence passes. No claim of overall R03 or A0/A1 acceptance
is made by this handoff.

## Interactive Session Workflow

1. Select the proof and exact row to validate. Record current source revision,
   supported Windows servicing build, machine/resources, runtime/native assets,
   endpoint or token identities and a named operator. Confirm that the required
   measurement hooks and recovery path exist; otherwise leave the row Blocked.
2. Agree on resource scope, bounded duration, owned synthetic inputs, output/
   retention, cleanup and a visible stop path. Record any required privileges
   and exact requested effects before obtaining approval. Do not treat approval
   of one proof as authority for another.
3. Rerun applicable safe deterministic/build checks into new ignored evidence
   directories. Existing measured snapshots remain historical; do not overwrite
   them or relabel simulation, timeout, package inspection or absence as a pass.
4. Execute only the separately approved trial. Record individual results,
   positive controls, native errors, correlated receipts and cleanup. Stop on
   unexpected identities/rights/effects; uncertain effects remain Unknown and
   must not trigger automatic replay.
5. Attach redacted evidence to the owning checklist, update its row and the
   applicable canonical decision/roadmap/acceptance records. Close a capability
   gate only when all its required real-boundary evidence passes; merging
   partial research or this register does not close it.

## Publication Versus Capability Acceptance

The containment proof's strict exit `2` and eight unproven network assertions
remain truthful research findings, not passing runtime acceptance. The PR may
be published/merged as partial evidence with completed build/hygiene checks,
this actionable deferred-testing handoff and normal repository checks/reviews.
It must not enable a worker, grant broader authority or change the result to
success to make the PR mergeable.

The storage proof's historical blocked second-account result remains in its
original snapshot. Its revised automated run verifies application-controlled
scope and permissions and returns success only for that bounded proof;
optional OS-boundary corroboration is not relabelled as passed.
Neither merging its design direction nor a successful synthetic run closes
S1-S4 or D-009.

Likewise, local inference may merge as a source-linked harness, truthful partial
results, technical outcomes and an actionable LI01-LI07/R02-L1-L6 handoff after
normal validation/checks/reviews. Missing runtime, operator/hardware trials and
independent offline capture remain capability blockers, not prerequisites for
merging that limited scope. Keep historical observations unchanged and
D-003/D-007 open; no inference pin, production tool loop or remote fallback is
enabled by publication.

On 2026-10-05 the user requested rebasing R03 onto main and adding its deferred
interactive testing alongside the other proofs so the scoped PR can merge.
The authorized merge scope is implementation plus this actionable evidence
handoff, **not completion of interactive acceptance or a production release**.
The incomplete A01-A07 rows remain capability/release blockers, not
prerequisites for merging that limited scope. The bounded A01/A02/A07 result
above does not disable the explicitly consented PTT implementation or mislabel
it as proven wake/audio privacy; deployment/release acceptance still requires
the remaining real-boundary evidence. Production wake remains unavailable.
Normal build/test/100% line-and-branch coverage checks and required reviews
must pass; documenting a bounded result or deferred trials cannot waive CI
failures or authorize disruptive testing. Overall roadmap/decision-register
acceptance consolidation remains with integration review.

No live speech, protected installation, privileged diagnostics or disruptive
computer-control validation was performed by adding this register. Its inference
entry performs no installation/model download, model-residency change,
network-policy mutation or successful real-model trial.
