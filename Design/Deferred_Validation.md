# Deferred Proof Validation

Status: live register of outstanding environment evidence, not passed acceptance
or permission to run a trial. Dated results, failed attempts and detailed
operator procedures are preserved in the
[validation archive](Archive/Deferred_Validation_2026-10-11.md).

The [qualification policy](Acceptance_Criteria.md#three-tier-qualification-policy)
owns merge/enablement/RC gates. A remaining trial blocks only its affected
capability/profile and RC inclusion. The
[roadmap disposition inventory](Implementation_Roadmap.md#experiment-disposition-inventory)
owns experiment retention and migration.

## Proof Checklists

| Area | Outstanding evidence | Preparation / procedure | Blocks |
|---|---|---|---|
| Native accessibility | Actual Narrator announcements, rendered readability at approved text scales and physical mixed-DPI observations, followed by operator sign-off | [Maintained UX fixture](../tests/Kora.NativeUxFixture/README.md); [focus-free preparation](#focus-free-preparation-for-interactive-native-accessibility) | Affected native UX qualification, not deterministic feature merges |
| Local inference | CPU-floor/resource budgets, complete context/overflow envelope, human quality, physical server cessation, licence/staging inventory, independently blocked egress and integrated repeat | [L1-L6](Implementation_Roadmap.md#r02-local-inference-continuation); [LI01-LI07](../experiments/r02-local-inference-proof/README.md#outstanding-testing-checklist) | D-003/inference D-007, offered inference profile and A2/R19 |
| Runtime/provider | Exact-profile reproduction/control coverage, attributable all-path RT2 mediation/loss controls and approved live PV1 account/cost evidence | [Runtime plan](Runtime_Provider_Feasibility.md); [runtime register](#runtimeprovider-follow-up) | Affected adapter/R08 and model-assisted R13 |
| Worker containment | Applicable W1-W4 token/ACL/child/network/dependency and fixed-action/quiescence evidence; rejected strict profiles remain rejected | [Containment proof](../experiments/r02-containment-proof/README.md); [W2 receipts](../experiments/r02-w2-dependency-proof/README.md) | Worker/deployment profile exposure and R11 execution |
| Storage | Installed loading/protection, power-loss, managed artifacts/backups/deletion, audit anchors/pruning/rollback and full lifecycle/copy inventory | [Storage register](#storage-admission-follow-up); [retained storage proof](../experiments/r02-storage-proof/README.md) | Affected R04/R12 storage/lifecycle and RC claims |
| Ownership/audio privacy | Complete A01-A07 native matrix and per-trial timing, including active lock/disconnect/suspend, device/permission changes, handoff and crash recovery | [R03 register](#r03-windows-ownership-and-audio-privacy) | Full R03/affected A0/A1/audio profile |
| Speech/wake | Physical headset and speaker/microphone acoustic quality, playback rejection, reference latency and packaged lifecycle | [Speech proof](../experiments/r02-speech-proof/README.md); [speech criteria](Acceptance_Criteria.md#gate-1-voice-and-clipboard-vertical-slice) | Offered speech/wake/provider/device profile; production wake remains unavailable |
| Distribution | Current final installed bytes, runtime-only prerequisites, lifecycle/protection/native loading and supported architecture/profile observations | [Distribution contract](Distribution_And_Updates.md); [installer guide](../installer/README.md) | D-005 and included installed release profile, not static source/build publication |

Passing mechanical/synthetic/native-seam checks does not fill operator-only
rows. The archive distinguishes successful bounded trials from failed, blocked
and unperformed attempts; new evidence must not rewrite those dispositions.

### Focus-free Preparation for Interactive Native Accessibility

Use [Invoke-NativeUxValidation.ps1](../eng/Invoke-NativeUxValidation.ps1) to
prepare isolated synthetic fixtures, hashes and headless evidence without
opening windows or sending input. Desktop launch needs separate explicit
operator approval. Narrator and global display changes remain operator actions.

The maintained [mechanical driver](../eng/Invoke-NativeUxMechanics.ps1) and
[workstation driver](../eng/Invoke-NativeUxWorkstation.ps1) can repeat owned
control/keyboard checks during an approved bounded desktop window. They do not
attest to audible announcements, readability, physical displays or actual
audio/privacy/installed behavior. Preserve every failed run and cleanup result.

## Storage Admission Follow-Up

Use the approved [private standard-SQLite baseline](Architecture.md#windows-durable-storage-direction).
Mandatory encrypted-native/key/rekey qualification is superseded; historical
crypto comparisons remain evidence. Readable copies outside the private profile
and same-user/admin limitations must remain disclosed.

| Scope | Remaining closure evidence |
|---|---|
| S1 - Profile and native deployment | Actual supported installed loading, protected paths/private permissions and servicing behavior; second-account denial is not routine for profile-local storage |
| S2 - Persistence and migration | Remaining supported schema/content/copy migrations and truthful source preservation; corruption/unknown schemas fail without replacement or repair |
| S3 - Interruption and recovery | Managed artifact/backup publication and physical power-loss/native durability beyond maintained task/question/audit hot-journal interruption tests |
| S4 - Retention and deletion | Complete owned-copy inventory/disposal, audit anchors/pruning/rollback and lifecycle/late-append holds; logical row deletion is not forensic erasure |

The [detailed S1-S4 handoff](Archive/Deferred_Validation_2026-10-11.md#storage-admission-follow-up)
and [production equivalence map](../experiments/r02-storage-proof/README.md#production-recovery-migration-and-retention---2026-10-07)
remain available. New shared storage, impersonation/cross-profile mechanisms or
inconsistent effective permissions require separately approved account trials.
Private profile isolation is not same-user worker containment.

## Runtime/Provider Follow-Up

[RT1/RT2/MG1/PV1](Implementation_Roadmap.md#r02-runtimeprovider-follow-up-gates)
retain distinct source-built/released identities and scoped results. RT2
all-path admission remains Blocked; PV1 has no account/live usage approval.
Sampling, file watchers, managed diagnostics, SDK acknowledgements and socket
closure do not prove complete native prevention or physical computation stop.

Start with [Prepare-RuntimeValidation.ps1](../eng/Prepare-RuntimeValidation.ps1)
and its [recorded prerequisites](Archive/Deferred_Validation_2026-10-11.md#runtimeprovider-follow-up).
It prepares evidence, not native launch/account authority. A historical-base
or hash failure must stop; do not weaken the guard or substitute another profile.
Synthetic native, released lifecycle, privileged collection and live provider
trials each need their own reviewed exact-byte and approval scope.

## R03 Windows Ownership and Audio Privacy

Deterministic consent/generation/resource regressions and bounded native trials
are delivered, but do not qualify the full matrix. The exact procedures,
measurements and failed pilots remain in the
[R03 archive](Archive/Deferred_Validation_2026-10-11.md#r03-windows-ownership-and-audio-privacy).

| ID | Required native scope | Closure requirement |
|---|---|---|
| A01 | Clean-profile grant/decline, saved-consent startup, disable/re-enable and withdrawal/restart | Distinguish armed PTT from actual capture; no capture after failed withdrawal/persistence |
| A02 | Mouse/Space/Enter PTT, release/focus loss, early/empty/max-duration/failed-open and duplicate/late results | Exact generation/bounds and one admitted dispatch; no unactivated audio or stale callback use |
| A03 | Separately approved active lock/disconnect/suspend/session transitions | Capture released within **500 ms of the observed lock event in every reference trial**; record OS notification delay separately, clear buffers/output and admit no stale dispatch |
| A04 | Permission polling, pinned/default endpoint loss/change/hot-plug and output rerouting | Native latency and exact route/closure receipts; restored readiness never clears a run hold |
| A05 | Same/different-build activation, approved takeover/decline/expiry/death/lock/return across supported profiles | One authenticated owner, full resource quiescence before transfer and no task/grant/audio/consent transfer |
| A06 | Separately approved owned-host termination and transfer/preparation failures | Unknown effects block automatic crash takeover/return; process death alone is not quiescence |
| A07 | Model-free tray/settings, PTT/Stop, focus/screen reader, locked presentation and late synthesis/rerouting | Exact native accessibility and last-output-sample/generation evidence under supported physical setups |

Normal authoritative unlock restores only prior enabled PTT intent after
confirmed closure and fresh gates, never recording or sensitive presentation.
Reconnect/resume and intervening failure require explicit recovery. A new
activation is required in either case; wake remains unavailable.

## Interactive Session Workflow

1. Select an exact trial and current source/payload/profile. Record operator,
   machine, identities, hooks, expected result and recovery; missing observability
   means Blocked.
2. Obtain fresh approval for duration, participants/bystanders, endpoints,
   resources, effects, retention/cleanup and immediate stop. Separately approve
   acquisition, elevation, audio, process termination, account usage or OS changes.
   Desktop availability and saved voice consent are not trial approval.
3. Run applicable safe checks into fresh owned evidence locations. Do not modify
   retained experiment inputs or overwrite historical receipts.
4. Execute only the approved scope, recording individual results, positive
   controls, loss/timing/resource data, errors and cleanup. Stop on unexpected
   identities/effects; uncertainty remains Unknown.
5. Record Pass/Fail/Blocked/Not run honestly and close only the corresponding
   criterion after complete evidence. Keep recordings local under agreed
   retention; commit only reviewed content-minimizing results.

## Publication Versus Capability Acceptance

Unsigned source/binary publication, build/static inspection and CI are not
installed/native/provider certification. An offered opt-in capability still
needs its applicable enablement evidence. A final RC evaluates its explicit
enabled manifest and exclusions under the
[qualification policy](Acceptance_Criteria.md#three-tier-qualification-policy).

## Historical Results

### 2026-10-05 Bounded Local-Inference Result

The [original result and later continuations](Archive/Deferred_Validation_2026-10-11.md#2026-10-05-bounded-local-inference-result)
remain historical; use the live inference row above for outstanding work.

### 2026-10-08 Local-Only Speech and Deployment Outcomes

The [original scoped results](Archive/Deferred_Validation_2026-10-11.md#2026-10-08-local-only-speech-and-deployment-outcomes)
do not qualify subsequently changed bytes or authorize another trial.
