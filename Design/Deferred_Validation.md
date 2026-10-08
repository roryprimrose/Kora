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

The [bounded Windows-native rate slice](Implementation_Roadmap.md#r10-bounded-windows-provider-native-rate---2026-10-08)
adds deterministic owned-setter, future-synthesis retirement and native/typed/
ACTIVATED admission tests only. -10..10/default0 are Windows engine units, not
acoustic speed measurements or Kokoro support. It runs no capture/playback/
provider/account/network lab trial and closes no full R10/A0–A4, runtime or
release gate. Retain the R02 speech candidate/model/license, hashed synthetic
fixture/threshold receipts and remaining acoustic/packaged-host consumers:
this preference slice is not a maintained replacement for that experiment.

The [R05/R14 bounded native question delivery](Implementation_Roadmap.md#r05r14-bounded-native-shared-question---2026-10-07)
adds automated trusted-UI state, exact review and production-store query tests,
not native desktop acceptance. Still obtain separate scoped approval for
keyboard/focus-restoration, screen-reader announcements, contrast/text scale,
DPI/multimonitor, mixed speech targeting and live privacy/ownership transitions.
It admits no effect proposal/dispatcher or missing source/containment/
deployment capability and closes none of D-001/D-005/D-008/D-009/D-013.

The [2026-10-07 automated UX/accessibility result](#2026-10-07-bounded-automated-ux-and-accessibility-result)
is completed evidence, not a future trial action. It records the distinction
between passing automated checks and still-unperformed native acceptance.

| Proof | Existing evidence / runnable checks | Deferred validation and preparation | Gates still open |
|---|---|---|---|
| R02 speech/hardware | [Merged synthetic proof and safe file-only reruns](../experiments/r02-speech-proof/README.md#safe-to-rerun-remotely-including-while-locked) | [Before a live test session](../experiments/r02-speech-proof/README.md#before-a-live-test-session), then [live/instrumented acceptance](../experiments/r02-speech-proof/README.md#live--instrumented-acceptance-work-still-outstanding). Obtain participant/bystander consent and an instrumented host with R03/R09 ownership/privacy controls; the current scripts cannot run live trials. | D-002/D-007; packaged acoustics, playback rejection, latency, reference floor and capture/recovery acceptance |
| R02 storage/key | [Historical encrypted storage proof](../experiments/r02-storage-proof/README.md#reproduce); DPAPI/ACL evidence retained; [bounded composed R04 receipt](Implementation_Roadmap.md#composed-milestone-validation-receipt) adds actual standard task/evidence, owned process interruption and no-replay recovery | [Approved standard-SQLite follow-up](#storage-admission-follow-up): broader migration/copy publication, retention/pruning/checkpoints and lifecycle/deletion. Mandatory encrypted-native/key/rekey gates superseded; installed loading remains R17 evidence. | D-009/R04 remain partial despite the first composed metadata-only query milestone. R12, managed backup/artifact/deletion, installed and power-loss acceptance remain open; no production transcript/content store enabled. |
| R02 local inference | [Safe deterministic reruns](../experiments/r02-local-inference-proof/README.md#safe-deterministic-reruns), plus the [bounded production-host trial](#2026-10-05-bounded-local-inference-result): clean one-approval Ollama/model setup, exact digest verification, real answers, loopback-only observation, visible cancellation and repeatable session-controlled teardown | [Prepare an interactive inference session](../experiments/r02-local-inference-proof/README.md#before-an-interactive-inference-session), then complete the remaining [LI01-LI07 deferred trials](../experiments/r02-local-inference-proof/README.md#deferred-inference-trials). Assign reference/isolation owners and agree budgets; separately approve exclusive model residency changes and whole-environment network blocking. Installer provisioning, CPU-floor measurements, server-cessation/races, attributable offline egress and repeated instrumented cancellation remain outstanding. | D-003 and inference D-007; [R02-L1-L6](Implementation_Roadmap.md#r02-local-inference-continuation), actual CPU-floor quality/context/resource budgets, installer distribution and independent offline success; R06/R07/R08/R10 and A2/R19 integration remain gated |
| R02 runtime/provider | Historical Node and [source-built RT1](../experiments/r02-dotnet-control-proof/README.md) unchanged. [RT2 bounded observations](../experiments/r02-runtime-lifecycle-proof/EVIDENCE.md) pass; all-path admission Blocked. [Separate released-profile MG1](../experiments/r02-dotnet-management-proof/README.md) passes: released SDK 1.0.16 with unchanged native bytes; 45 repeated RT1 regressions, 22 host and 16 actual runtime cases | [Runtime/provider follow-up](#runtimeprovider-follow-up): preserve distinct artifact identities, RT2 final source reproduction and MG1 initial local reproduction blocker without inferred byte equivalence. Qualify affected RT2 lifecycle paths for the refreshed released profile; separately approve account/usage trials. MG1 does not establish all-native observation, physical computation termination, durable authority or live eligibility. | D-001/D-004/D-010 remain open; RT2/PV1, R08 and model-assisted R13 production admission remain gated. MG1 envelope is scoped Pass; deterministic local management does not wait for hosted trials |
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

### 2026-10-07 Bounded Automated UX and Accessibility Result

**Disposition: automated pass and scoped native observations; native acceptance
remains incomplete. The initial readable-text regression was corrected and
retested; artifact input/discovery labeling findings were corrected and
verified through headless tests and native Windows UIA. Continued keyboard
checks found question focus-restoration and terminal-status persistence
defects; both were corrected and verified through automated and native
rechecks. A further safe native batch found unnamed Appearance timeout
controls and keyboard-boundary defects; those were subsequently corrected
and verified through automated and native rechecks. An earlier unexplained
guide disappearance was not reproduced; no cause or product fix is claimed.**
The isolated worktree's actual base is
`d0a8e82ef34b82c4d888803083050c2e9dff43cd`. The source fixes and maintained
tests are worktree changes, not evidence that the previously published beta
contains them. Release solution build passed with zero warnings/errors; the
updated Windows integration suite passed **836/836, zero skipped** after the
extended corrections (previously 828/828 after the question corrections).
The focused question suite passed **30/30**, including eight new window
regressions. The combined accessibility runtime suite passed **28/28**,
including eight new bounded-surface regressions.
The later question, extended bounded-surface rechecks and nine-option
Appearance/Evidence completion were recorded on **2026-10-08 UTC**; the
heading retains the initial result date.

Subsequent fixture-only hooks advance valid scratch lifecycle generations and
question revisions, seed completed cross-session explicit links plus a missing
correlation target, and verify refusal of a deferred render after closing its
immutable viewer. Six added cases bring the focused fixture contracts to
**18/18 passed**. The latest full integration attempt was **841/842**, with
`Cancellation_after_commit_does_not_turn_a_durable_receipt_into_a_cancelled_result`
failing with `OperationCanceledException`; the earlier 836/836 pass remains
historical evidence, not a pass for this later attempt. The fresh hook fixture
initialized and exited 0 before native hook measurements; its exact scratch
child was absent. Native stale/link qualification remains pending. Work paused
for the operator-requested rebase onto newer `origin/main`; these observations
remain tied to the original base and do not qualify newly delivered upstream
surfaces.

The operator-requested rebase completed onto actual upstream base
`c78e81318fb3c5b279275f9ab8ef14748bfeb952`. Conflict resolution retained the
newer upstream session/task controls, maintenance snooze eligibility,
call-policy status and mouse-timeout contracts. The test-only fixture now
uses upstream assistant-name/speech configuration services and selects
`SessionWorkspaceEntry` records while retaining its denied effect adapters.
The rebased Release solution build passed with zero warnings/errors, and the
Windows integration suite passed **1077/1077, zero skipped**. The earlier
cancellation-test failure did not reproduce; no cause or storage fix is
claimed. Historical native observations above remain tied to the original
base; this automated recheck alone does not qualify newer upstream native
surfaces or the still-unmeasured stale/link hooks.

| Area | Completed observation | Not established by this result |
|---|---|---|
| Accessibility names and readable content | The initial native probe found label-only static text with no separate TextPattern/ValuePattern. The corrected [NamedTextBlock](../src/Kora/Controls/NamedTextBlock.cs) exposes label plus displayed content and publishes complete name changes. [Runtime regressions](../tests/Kora.Windows.IntegrationTests/AccessibilityRuntimeContractTests.cs) cover fallback, empty/duplicate labels, inline text, change notifications and realized artifact labels. Native Windows UIA then exposed actual Maintenance version/disclosure/status, evidence JSON, package identities and synthetic-closure status. After correction, a fresh native recheck exposed the input Name `Typed command or artifact slash input`, with its value still `/`, and exactly one synthetic dropdown item/button named `/inspect-fixture`. The later corrected question retained its exact successful receipt/disclosure Name in a settled snapshot 35 seconds after submission. | Spoken announcement behavior or acceptance of every converted window. Persistence of one bounded terminal message does not qualify all announcements or surfaces. |
| Keyboard and DPI | Maintained headless checks exercise Maintenance Tab reachability and rendering at 1.0/1.5/2.0/3.0 scale. | Complete Tab/Shift+Tab order, focus restoration, clipping, text scaling, native per-monitor transitions or physical-display usability. Reachability and non-throwing rendering are not substitutes. |
| Scoped native keyboard | After operator foreground activation, guarded native input measured Maintenance's two enabled focusable controls in complete forward/reverse cycles (six steps each) and the unselected question's six enabled focusable controls in two forward cycles (12 steps). A later selected question completed two exact seven-control cycles in each direction (14 steps each). Every Tab step emitted an owned focus event; disabled controls were skipped. Synthetic artifact Down/Up selected its sole option without changing `/`; Enter completed `/inspect-fixture` and dismissed discovery; Escape dismissed fresh discovery while preserving `/`. Both retained actual input focus. Corrected question native snapshots verified enabled initiating-button focus after Review and Save, and enabled Close focus after submission. | Unmeasured focus paths and physical usability. Foreground acquisition was intermittently denied; those attempts sent no keys. Earlier unsuccessful draft attempts are not counted; later focused-button trials independently established draft saving. See the extended correction row for additional measured cycles and owner restoration. |
| Native bounded surfaces | A fresh exact question record was readable through the read-only ValuePattern; no choice was preselected. Explicit draft advanced the same question from revision 1 to 2, preserved its session/question IDs and exposed the saved `show` choice in read-only exact review. Corrected submission displayed the local version, storage disclosure and `Verified durable receipt: Succeeded`, with the exact terminal Name unchanged 35 seconds later. A naturally expired older target cleared prompt/choices/review and disabled Submit; it was closed without answering, then a fresh explicit question was opened. Scratch Sessions showed passive detail and explicit Done/resume for the same idle ID, advancing generations 1 to 2 to 3 without replay. Evidence returned a bounded structured snapshot. Guide/details exposed embedded/public provenance; skill inspection exposed manifest identity, digest, source and shared-file tabs. Appearance's owning UIA tree exposed exactly nine options. Typing `/` revealed only the synthetic non-executable fixture entry. | Stale-generation/revision refusal, complete session-page/explicit-link navigation and native stale-detail refusal. Expiry refusal is not a substitute for stale-target refusal. Artifact selection only completed input; Run, copy, external link, script, executor and model routes were not invoked. |
| Extended safe native batch | Busy scratch Done was refused with the nonterminal/Unknown-work reason and no abandonment; refresh retained all three IDs at Active generation 1. Scratch theme changed System to Light, then the selected registry reset restored System, also read from the scratch preference file. Evidence pagination returned 49 then 15 records with disjoint citation identities and terminal cursor exhaustion; a selected span trace returned 15 records. Evidence measured 14 owned focus events in each direction. Immutable guide details exposed a 4,255-character read-only reader and current-revision search match, with two exact nine-control Tab cycles in each direction (18 events each). A keyboard-origin detail Escape initially restored the exact guide OpenDetails button. A fresh guide measured 20 named focus events in each direction. Shared package helper source was inspected read-only. | This batch initially failed Appearance timeout names and Appearance/package keyboard boundaries; the subsequent correction row records their recheck. Native stale-state injection, physical usability, speech, clipboard and external maintenance remain unqualified. The later registry/Evidence completion row records all nine mutation/reset pairs and retained-parent navigation. |
| Extended correction recheck | Native UIA exposed exact response/presence timeout Names on both spinners and their actual editors, plus descriptive Increase/Decrease button Names. Appearance completed two exact 14-control named cycles in each direction, 28 owned focus events each. Package inspection completed two exact three-control cycles in each direction, six events each, both on the initial manifest and after selecting the current read-only shared helper. Three keyboard-origin immutable detail closures retained the same guide HWND and restored actual OpenDetails focus in settled checks; the guide remained after activating package/settings surfaces. | Immediate window-transition focus transport is not qualified: missing opening events and transient post-Escape focus gaps aborted probes without sending a follow-on key. Settled checks establish the reported outcomes, not reliability of those immediate probes. The earlier guide disappearance remains unclassified and was not reproduced. |
| Registry/Evidence completion | Native UIA mutation followed by selected registry reset persisted exact scratch values for all nine options: theme Dark to System; presence/response timeout 12 to 10 and 7 to 5 seconds; presence size 400 to 360 pixels; dot size 120 to 100, density 125 to 100, movement speed 125 to 100 and speech scale amount 125 to 100 percent; speech scaling false to true. Exact-file commit events guarded asynchronous reset verification. Read-only native Audit-source pagination returned 26 then 14 disjoint records, exhausted the cursor and established correlated Requested/Succeeded pairs for every canonical action, with at least two pairs per action. Extra theme retry pairs were retained, not hidden or counted as additional options. Selecting an admitted audit record and its Present segment opened the exact cited span; selecting that span's Present parent opened the exact retained parent citation with matching trace/span identity. An unfiltered native Link-source query returned Available, zero records and no cursor. Synthetic gate closure cleared every private owned window; graceful stop exited 0 and removed the exact scratch child. | These parameter writes did not invoke speech/audio or touch real preferences. One mutation/reset pair per option does not qualify every allowed value, invalid input or stale revision. Retained-parent navigation does not qualify explicit links: the fixture contained none. Session paging, stale-state hooks, spoken screen-reader output, rendered contrast/text scale, physical DPI/multimonitor usability and actual OS privacy/ownership transitions remain unqualified. |
| Contrast | [Palette contracts](../tests/Kora.Windows.IntegrationTests/ThemeContrastContractTests.cs) require 4.5:1 for declared opaque normal, muted, accent-foreground and status/warning text pairs. Reference-ratio checks pass; unsupported/uncomposited transparent colours are refused. | Rendered pixels, hover/focus/disabled states, gradients, translucent composition or component-boundary contrast. A muted caption is not automatically exempt normal text, and a border's exemption requires a usage assessment. |
| Native fixture preparation and initial trial | The [test-only launcher](../tests/Kora.NativeUxFixture/NativeUx/NativeUxFixtureHost.cs) builds; [off-screen contracts](../tests/Kora.Windows.IntegrationTests/NativeUx/NativeUxFixtureContractTests.cs) verify production XAML theme loading without production composition, scratch-service initialization/cleanup, clipboard guards and explicit rejection of external/effect operations. The compiled executable's no-argument path exits 2 without native startup. After separate approval, the isolated host launched and Windows UIA inspected only its identity-verified launcher and Maintenance window. Maintenance consent was disabled. Synthetic gate closure emitted the owned window's WindowClosed event, removed its HWND and disabled the version action. Graceful stop exited 0 and removed its exact scratch child. | Remaining native surface paths, keyboard order/focus restoration, live ownership/handoff/lock timing, microphone/TTS, real clipboard or maintenance egress acceptance. Synthetic window clearing does not qualify R03 or actual OS transitions. |

The native fixture reuses actual window/controller/view-model classes and the
production XAML styles, but never runs `Kora.Program` or the production
`App.OnFrameworkInitializationCompleted` composition. It creates one uniquely
named, restricted child under an explicitly supplied existing absolute scratch
parent on a fixed local drive. UNC/device paths, mapped-drive parents and a
reparse-point parent are refused. Real task/interaction/evidence stores and preference files are confined
to that child; voice consent is declined and model execution disabled.
Two idle and one busy synthetic session seed the bounded inspection paths.
Typed audit/evidence correlation remains in use; the fixture owns and disposes
its evidence activity listener before deleting scratch state.

The first native launch stopped on the label-only baseline before keyboard
input. After correction and a separately approved fresh continuation, the
scoped checks above ran against identity-verified fixture processes only.
Native question UIA Invoke returned an ambiguous error on one review attempt;
a fresh keyboard review exposed the exact record, but this does not qualify
the earlier failed transport or subsequent draft attempts. One corrected
fixture exited 0 before its planned review was complete; no result was inferred.
The final synthetic closure cleared all owned private fixture windows and
exposed its changed status text. All fixture processes exited 0 and their exact
scratch children were removed.

The artifact-label recheck used a fresh identity-verified fixture and only
opened typed discovery and set the synthetic `/` input. It did not invoke the
catalogue item or Run. Graceful stop exited 0 and removed its exact scratch
child.

Continued native checks used another fresh identity-verified fixture. The
artifact keyboard sequence passed after an earlier separated sequence lost
its target window and aborted without sending the next key; aborted probes
are not acceptance results. Exact focused-button question review, draft and
submission then established the bounded results above.

The two question findings were corrected after separate approval:

- After Review and Save draft, actual UIA focus fell back to the non-focusable
  window rather than returning to an enabled control. The shared asynchronous
  action/activation path now restores lost focus only while active, visible
  and still admitted; it preserves meaningful moved focus and selects enabled
  Close after terminal completion. [Window regressions](../tests/Kora.Windows.IntegrationTests/NativeQuestionWindowContractTests.cs)
  exercise actual keyboard actions, moved focus and privacy closure. Fresh
  native snapshots verified Review/Save focus and terminal Close focus.
- Submission initially exposed the successful receipt and storage disclosure,
  but a later settled snapshot contained only `Revision 2: answer-recorded`.
  [QuestionWindow](../src/Kora/QuestionWindow.axaml.cs) now delegates terminal
  outcomes to the authoritative [view-model status](../src/Kora/NativeQuestionViewModel.cs),
  so eligibility refreshes retain them. Closure clears the status control,
  and privacy/closed state refuses late outcome presentation. Automated
  lifecycle tests and the native 35-second exact-Name comparison passed.

During the corrected recheck, foreground guards refused some attempts and a
wait for operator foreground timed out; no keys were sent for those attempts.
One UIA Invoke returned an unclassified generic transport error and did not
establish submission. The older question naturally expired and was not
retargeted or answered. A fresh explicit question was placed within the
primary display and completed through the guarded keyboard path. Both its
initial and revision-2 immutable reviews retained the same question/session
IDs. All observed windows remained at 96 DPI; placement is not per-monitor
acceptance.

The final synthetic closure again cleared every private owned window. This
fixture exited 0 and its exact scratch child was removed.

No audio, shared clipboard access, external metadata fetch, browser navigation,
display-setting change or real OS transition was performed. Maintenance's
consent checkbox remained disabled; invoking Check without consent exposed the
refusal reason. Native windows were observed at 96 DPI only; moving/observing
them does not qualify per-monitor transitions or rendered contrast/text scale.
No native R05/R12/R14 or corresponding A4 gate is qualified by this partial
trial. The two artifact-label findings are closed by the focused automated
and native recheck; the question focus/status findings are also closed by
their automated and native rechecks. Preserve the remaining acceptance
requirements.

The extended safe batch initially exposed these findings:

- The Appearance response/presence timeout spinners have empty native Names.
  Actual Tab focus reached their `PART_TextBox` children with empty Names and
  values 5 and 10; increment/decrement peers expose `Avalonia.Controls.PathIcon`.
  Adjacent static captions do not establish accessible input labels.
- Appearance's 16 forward steps produced ten focus events before remaining on
  the final speech-scale slider; its 16 reverse steps produced two events
  before remaining on the first registry selector. Package forward navigation
  produced 12 events but later alternated selector/reader, while 12 reverse
  steps produced only one event and remained on the immutable source reader.
  Those initial sequences did not establish complete bidirectional cycles
  or access to the tab strip.
- The guide initially regained its exact OpenDetails focus after detail
  Escape, but was absent from a later owned-window lookup. No cause was
  inferred. A fresh explicit guide completed the measured 20-step forward
  and reverse reachability checks; that does not explain the earlier
  disappearance.

After approval, the timeout and keyboard-boundary defects were corrected:

- [Settings](../src/Kora/SettingsWindow.axaml) labels the spinner and actual
  template editor with the timeout's seconds unit and labels both stepping
  buttons, including their derived repeat-button types.
- [NavigableTabControl](../src/Kora/Controls/NavigableTabControl.cs) is shared
  by Settings and package inspection. Avalonia's inherited ItemsControl
  focus handler remembers content inputs/readers as the Tab entry point;
  wrapping can therefore return to the already focused control. The shared
  control clears that entry memory and makes only the selected header a
  Tab stop, preserving native arrow-key selection. An additional changed-file
  regression verifies that reverse Tab returns to the current selected
  header, not the first file's header.
- [Bounded runtime regressions](../tests/Kora.Windows.IntegrationTests/BoundedSurfaceAccessibilityTests.cs)
  verify the actual editor/button peers, two repeated complete named cycles
  in both directions, header arrow selection and repeated guide/detail
  lifetime. Fresh native measurements established the correction row above.
  All eight new regressions and all 836 integration cases passed.
- No guide production code was changed. Three headless repetitions and
  three native closures retained the guide; native settled snapshots restored
  its exact opening button, and activating other bounded surfaces retained
  the same HWND. Missing opening focus events and transient immediate
  post-Escape focus gaps were recorded as probe timing limitations, not
  successful immediate-event measurements or an inferred product cause.

No copy buttons were invoked. Detail copy controls were enabled/focusable in
the fixture, but its adapters and framework clipboard guards still refuse
clipboard operations. Tab reachability does not qualify clipboard behavior.
Both extended fixtures' final synthetic gate closures cleared all remaining
private owned windows; each exited 0 and its exact scratch child was removed.

The prepared launcher exposes local-version/native exact review, minimal
Sessions, read-only Evidence, guide/details, bundled package inspection,
scratch Appearance and a synthetic typed artifact dropdown. Maintenance
network consent is disabled. External metadata/browser, device/audio,
model/setup, OS control and shared clipboard adapters reject operations
explicitly. Fixture-wide Copy/Cut/Paste keyboard/text-box and context-menu
guards prevent framework clipboard shortcuts; detail copy is also refused.
These fixture-only refusals are not production behavior acceptance results.
Clipboard preview is not exposed by this launcher pending separate exact
synthetic-fixture approval; the earlier headless preview is not real clipboard
acceptance. Spoken invocation remains outside this phase.

**Launch remains blocked on separate operator approval.** Only afterward use
the built `Kora.NativeUxFixture` executable with
`--launch-native-fixtures --scratch-parent <approved-existing-absolute-directory>`.
The command-line switch is a deliberate launch guard, not an authorization
grant. It must not be included in automatic test/CI execution. Agents target
only the fixture PID/owned windows, stop on a target/foreground mismatch or
unexpected effect, and never enumerate/capture unrelated desktop content.
Stop closes private fixture windows, awaits tracked operations, disposes
controllers and evidence observation, then removes only the created child.
Forced process termination can leave that exact child for inspected cleanup;
never delete the scratch parent or unrelated Kora data.

Remaining native measurements retain their scoped approvals: complete
keyboard/focus behavior, Windows UIA/actual announcements, rendered contrast
and text scale, physical DPI/multimonitor behavior, and live privacy/ownership
transitions. Narrator/audio, clipboard manipulation, maintenance network
checks and real OS transitions require separate consent. Synthetic gate
closure cannot qualify R03. Full conversations/queues, executor, clipboard
explanation, Ask Evidence and generalized approvals remain unavailable.
Native R05/R12/R14 and corresponding A4 gates remain open; this does not
block unrelated development or replace existing historical proof receipts.

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

The later [2026-10-06 MG1 evidence](../experiments/r02-dotnet-management-proof/EVIDENCE.md)
is separate from this dated historical revalidation. It measures the
released-profile envelope and native acknowledgement stalls after fresh RT1
regressions, not native all-path lifecycle observation or account acceptance.
Full root Release/Core/Application/Windows/100% coverage and publish checks
pass; local full MSI ICE validation is Blocked by WIX1105/system policy.
The explicitly ICE-skipped package build/inspection is only non-release
inspection, not a substitute for that gate or installed acceptance.

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
| Storage | Standard-SQLite task/evidence/interaction process-interruption and hot-journal cases now have maintained production tests; see the [specific migration/retention map](../experiments/r02-storage-proof/README.md#production-recovery-migration-and-retention---2026-10-07). Retain the shared executable because its SQLCipher/envelope WAL/journal comparisons, encryption/DPAPI/rekey, migration, artifacts/backups/deletion and native receipts are not equivalent to the PERSIST/FULL production baseline. No blanket retirement or relabelled receipt; retire an individual path only after its unique proof and consumers are superseded. |
| Local inference | Retain through LI01-LI07 and R06-R08/R10 adapter delivery; it owns the exact candidate rubric and deferred measurement procedure. Remove it only after those cases are covered by production adapter/integration tests and final evidence. |
| Runtime/provider | Retain and rerun on every SDK/runtime pin change until the isolated .NET fixture and production host-envelope tests supersede it. The known hook-only failure must remain executable until the unsupported path is impossible in production composition. |
| Containment | Retain through W1-W4 and protected deployment integration. Migrate filesystem, credential, process-tree and receipt-classification assertions into Windows integration tests before deleting the standalone harness. |
| Distribution | NSIS acquisition/authoring/build code and Linux recipe remain retired. The six remaining distribution-only executable proof files are superseded by maintained [source orchestration/checkout](../eng/SourceBootstrap.Common.ps1), [publish/native/runtime/import/resource inspection](../eng/Inspect-Publish.ps1) and their ownership/stage/publish contracts; equivalent checks and exact-revision x64/x86 static publishes passed before targeted retirement. All seven [historical receipts/inventories and their original procedure context](../experiments/r02-distribution-proof/README.md) remain byte-for-byte unchanged. CI configures maintained gates; release-state/publication correctness is independently owned. This does not retire any storage/native comparison, runtime/provider/Node witness, containment, speech or inference harness. Installed protection/native loading/runtime-only and D-005 gates remain open. |
| R03 ownership/audio privacy | These are production implementation and regression tests, not disposable proof code. Retain them normally; add separately instrumented acceptance fixtures rather than replacing unit/integration coverage with manual receipts. |

## Storage Admission Follow-Up

The [profile-boundary contract](Architecture.md#profile-boundary-and-validation-responsibility)
trusts Windows per-user isolation but requires Kora to demonstrate correct
use of it. A profile-local path alone does not cover permissive ACLs,
shared staging or readable backup/export copies outside that boundary.
The storage research may merge with these application/deployment gates open:

| Follow-up | Owner / package | Required evidence and scope |
|---|---|---|
| S1 - Standard native closure | Storage/release leads, R17 | Existing pinned Microsoft.Data.Sqlite / e_sqlite3 is the approved standard-SQLite route. Review notices/servicing and offered architecture packaging/loading. Encrypted-native selection/authentication is superseded, not a current storage blocker. Installed loading remains separate evidence. |
| S2 - Integrate the profile boundary | Storage/application leads, R04 | Supplied LocalApplicationData paths and effective folder/file ACLs now compose the bounded task/evidence partitions. First-use greeting/settings/version disclosure covers readable copies and same-user/admin access. No shared fallback, silent permission repair or replacement. Managed artifacts/backups and installed effective permissions remain separate open acceptance. |
| S3 - Integrate recovery and migration | Storage/application leads, R04 | Exact local version-query intent/dispatch/terminal/evidence and Interrupted/Unknown no-replay startup recovery are composed. [Production-store interruption/reopening](Implementation_Roadmap.md#r04-production-store-interruption-and-reopening---2026-10-07) now covers pre/postcommit task, evidence/link and interaction approval/use/Done writes, interrupted recovery, hot private journals and unsafe reopen refusal. The bounded receipt is not an OS effect. Backup/artifact publication, broader supported migrations, retention/checkpoints and installed/physical power-loss boundaries remain open. No database key/rekey or encrypted legacy conversion is required. |
| S4 - Integrate deletion and lifecycle | Storage/security/application leads, R12 | Exercise source revocation, late appends, live/unknown-work holds and configured lifecycle; remove or rewrite managed recoverable copies while preserving unrelated sessions and independent grants. Disclose exported/provider/forensic limits. |

The [2026-10-06 durable interaction continuation](Implementation_Roadmap.md#r04r05-durable-interaction-and-minimal-session-authority---2026-10-06)
adds real question/grant/audit transactions and minimal durable Active/Done/
resume/authority-removal generations. Owned-scratch tests cover reopened typed
records, duplicate approval/Once consumption, stale exact snapshots, revoke/
use and task cancellation serialization, cancellation after staged writes,
actual audit-write failure, corrupt/schema/journal/ACL failures and owned-child
hot-journal rollback. Perpetual records survive authority removal; restart
never replays decisions or dispatch. This is partial S2/S3/S4 progress, not
full R04/R05/R12 acceptance.

The [2026-10-07 recovery continuation](Implementation_Roadmap.md#r04-production-store-interruption-and-reopening---2026-10-07)
extends that disposable proof to actual production task writes (rather than a
raw-SQL transaction proxy), diagnostic/audit/span-with-links transactions,
approval/Once consumption/Done generation changes, and interruption during
audited recovery. Both old committed state and exact all-or-none writes are
checked after reopening. Hot headers are observed before killing only the
fixture's child; missing/permissive journals and held ownership/access fail
without repair or replacement. A foreign-owner descriptor is tested against
the same production permission policy without changing OS ownership.
Committed cancellation, fresh-run authority refusal, stale generations and
repeated no-replay recovery remain maintained regressions. This closes only
those process-interruption proof gaps, not hardware fsync/power-loss or
installed acceptance, artifact/copy recovery or the remaining gates below.

Remaining bounded-integration gates: immutable native operation review and
trusted UI/foreground voice input; actual host snapshot acquisition and
immediate adapter/worker pre-effect revalidation; source revocation/late append
and live/uncertain-work holds; full lifecycle/meaningful-activity policy,
history/search/queues and recoverable-copy inventory/deletion; audit
anchors/pruning/coherent whole-store rollback and installed/power-loss proof.
The authority-removal primitive is not forensic erasure or complete session
content deletion. No direct lock/power route, model tool, installer, audio or
hosted provider was newly admitted.

No second-account denial result is claimed. Optional actual-account
corroboration becomes required if introducing shared storage, service or
impersonated identities, cross-profile import/migration or custom cross-user
authorization, or investigating inconsistent effective permissions.
Such trials need approved real accounts and explicit fixture/effect scope;
the retained [optional handoff protocol](../experiments/r02-storage-proof/README.md#optional-real-cross-user-handoff-protocol)
does not create an account or grant authority to test another profile.
Private profile permissions are not same-user worker containment; D-013/W1-W4 remain separate.

R04 now has independently safe source/test contracts as recorded in the
[foundation inventory](Implementation_Roadmap.md#r04-foundation-delivery).
The owner-approved [D-009 baseline](Decision_Register.md#approved-profile-secured-sqlite-baseline---2026-10-06)
supersedes mandatory encryption/key/native-codec admission. The actual
[standard task store](../src/Kora.Windows/Storage/WindowsSqliteHostTaskStore.cs)
uses the existing provider and private ACL-verified authority, with transactional
versioned intent/dispatch/terminal records and a bounded incomplete query.
It has no database key dependency and rejects missing/corrupt existing data.
It is now composed for exact typed/activated-voice version queries and required
independent typed evidence. The bounded task-control continuation consolidates
its exact ordered ledger with question/session/grant/typed-audit authority in
interaction schema v3; `HostStorageV1` remains an inert frozen handoff receipt.
Validated migration, task/question/audit cancellation and owned-process
interruption fixtures do not qualify general workers or content deletion.
The bootstrap `kora.db` remains setup-only, not
a host-content migration. The composed-milestone receipt in the roadmap owns
the exact automated process-interruption results and fixture scope; no power-loss,
installed loading, backup or deletion acceptance is inferred from scratch tests.

**Historical encrypted-route evaluation:** SQLite3MC 2.4.0 acquisition and
basic encryption tests succeeded but source/licensing findings rejected it.
That outcome and earlier DPAPI/artifact tests remain evidence, not a
replacement/source-build decision required for the approved standard store.
Credentials still require Windows-protected secret storage.

## Runtime/Provider Follow-Up

Status: **historical source-built RT1 passes; separately approved released
RT1/MG1 profile passes; RT2 bounded observations pass but all-path admission
is Blocked; PV1 and production admission remain open**.
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

- For offline automation, run
  `.\eng\Prepare-RuntimeValidation.ps1 -OutputDirectory <new-directory-outside-repository>`
  in a linked isolated worktree based on main
  `d0a8e82ef34b82c4d888803083050c2e9dff43cd` (2026-10-07; CI run
  `37597127017` succeeded). The runner checks retained historical identities,
  runs deterministic preparation/receipt contracts, and emits redacted
  `readiness.json` and unapproved `operator-requests.json`. Optional
  `-SourceBuiltPackagePath`, `-ReleasedPackagePath` and
  `-NativeRuntimeDirectory` verify explicitly supplied bytes without loading
  them; no ambient package/credential discovery or acquisition occurs.
  Exit **2** means preparation completed but runtime/provider admission is
  still Blocked; a terminating error means preparation failed. This does not
  launch native trials, trace, sign in, call a provider or update historical
  evidence. With `-RunHostComponentTests`, it also builds and runs all 22
  existing MG1 host-component tests (including synthetic loopback HTTP, no
  Copilot SDK/native launch), requires exact SDK/runtime versions and writes
  current TRX outside the repository. Missing restored assets/packages require
  an explicit external `-PackageConfigPath` for locked restore. On the current
  machine use a temporary Networking-AAA-only source/mapping configuration;
  do not encode machine routing in repository NuGet configuration or locks.
  Feed/authentication failures stop without alternate-feed fallback. Explicitly
  supplied missing/mismatched artifacts fail preparation, not just admission.
  Existing fixture runners must later run in private staged copies
  with separately approved launch/collection/account scope and machine-local
  Networking-AAA feed routing, not directly over retained receipts. Collector,
  native mediation, live-account transport and enforceable billed-spend hooks
  remain missing, not implemented by the offline runner. Billing alerts are
  not hard caps; SDK cancellation is not physical or billing termination.
- After explicit pinned-acquisition/native-launch approval,
  `.\eng\Invoke-SyntheticRuntimeValidation.ps1 -ApproveSyntheticNativeTrials -OutputDirectory <new-external-directory> -PackageConfigPath <external-approved-feed-config>`
  coordinates separate source-built RT1/RT2 and released RT1/MG1 trials in
  exact-base disposable copies beneath the isolated worktree's ignored test
  artifacts, with short uniquely owned temporary source/management fixture
  roots to avoid long-path failures without host policy changes. Only private
  preparation-script config and source-repository metadata references change;
  SDK/native/control code and original fixtures remain
  unchanged. Short uniquely owned source staging retains the original clean
  reproduction recipe and strict byte/lock gates. A source-profile blocker
  never selects released bytes as a substitute; the independently approved
  released lane has its own receipts. Failed/blocked candidates do not launch
  their trials. Staging is retained for diagnosis/reproduction; individual
  trials must confirm owned cleanup. This coordinator does not implement or
  approve the privileged collector, native prevention or live PV1.
  `-ReleasedOnly` permits an independent released-profile continuation without
  resolving or relabelling a source-built blocker.
  `-VerifiedArchiveDirectory` reuses only explicitly supplied source/runtime
  archives after exact hash checks, never ambient caches. Fresh released
  case receipts and TRX are copied into the external output directory.
  The 2026-10-07 approved synthetic continuation retained the historical
  source-built Pass but rejected its newly rebuilt candidate at NU1403:
  package and assembly hashes differed from the approved pins, so source
  RT1/RT2 trials did not start. The independent released-profile continuation
  passed all 45 RT1 regressions, 22 host components and 16 actual MG1 cases
  after shortening owned staging paths (no policy change or suppression).
  Original fixture/evidence bytes remain unchanged. These fresh released
  results do not resolve source reproduction or run PV1; they did not include
  released RT2 lifecycle observations. All-path RT2 and production gates remain
  Blocked.
  Subsequent owned-input diagnosis reproduced the approved package/assembly
  once, then another distinct rejected identity. Package-entry and portable
  PDB comparisons localized the variation to generated `LoggerMessage.g.cs`:
  the same 129-line multiset was emitted in different class/method order.
  The algorithmic trigger remains unproved. Do not infer equivalence,
  normalize/reorder generated SDK code, repin, or launch a mismatching
  candidate. Historical source-built evidence and independently approved
  released-profile evidence remain separate.
- With separate released-profile bounded RT2 approval,
  `.\eng\Invoke-ReleasedRuntimeLifecycle.ps1 -ApproveReleasedBoundedTrials -PreparedReleasedFixtureRoot <explicit-qualified-released-MG1-copy> -OutputDirectory <new-external-directory> -PackageConfigPath <external-approved-feed-config>`
  derives a short uniquely owned fixture without modifying historical
  experiments. It verifies exact released SDK/native bytes and completed
  released RT1/MG1 prerequisites, reuses the five byte-qualified released RT1
  control files and approved central package metadata, and requires locked
  restore against that profile's existing regression lock. Only the private
  observer receipt profile label and project/package binding change; lifecycle
  assertions and observer logic do not. The 2026-10-07 released continuation
  passed **20/20** (13 actual native lifecycle cases, six deterministic observer
  cases and one live synthetic file/socket/managed-diagnostic positive control)
  with zero build warnings/errors. It recorded 14 synthetic provider requests,
  zero denied-model/credential markers, zero watcher overflows/query gaps,
  zero sampled owned survivors and empty owned trial scratch. Its 52 observed
  process records included 39 descendant observations; these are per-trial
  observations, not proof of complete descendant coverage. The initial and
  finalized coordinator runs both passed 20/20; their maximum sample gaps were
  92.8 ms and 143.0 ms respectively. Two initial-run unknown/unreviewed helper
  observations remain unqualified (none sampled in the finalized run).
  Fresh explicitly labelled JSON/TRX, fixture/lock/deployed dependency hashes
  and byte-verified evidence exports remain outside the repository. Staging
  stays available for diagnosis; no privileged collector, live account,
  provider inference or native containment change is performed.
  Exit **2** retains all-path RT2 Blocked and PV1 Not run. Sampling, file
  notifications without writer attribution and managed diagnostic events
  cannot establish all-native privacy, write/egress prevention or physical
  computation termination. Applicable released-profile tracing/prevention,
  historical source reproduction, R08 and model-assisted R13 gates remain open;
  deterministic R13 core remains independent.
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
  network/file/diagnostic observation and native prevention.
  The separate [MG1 runner](../experiments/r02-dotnet-management-proof/README.md)
  now repeats RT1 and the complete envelope on separately approved released
  bytes. Preserve the source-built reproduction blocker and distinct pins,
  not inferred equivalence. RT2 still needs attributable network/file/
  diagnostic observation for the applicable profile. Neither fixture's scoped
  model handler or status payload proves all-path prevention or production
  authority.
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
| MG1 - .NET management envelope / runtime lead | PASS for separately user-approved released SDK 1.0.16 / unchanged native 1.0.90; 45 repeated RT1, 22 host and 16 actual MG1 cases; original source-built reproduction Blocked | [Measured evidence](../experiments/r02-dotnet-management-proof/EVIDENCE.md) proves complete serialized UTF-8 input at 32768/32769 bytes and complete typed output at 4096/4097 bytes, counting framing/history and multi-byte text. Actual held inference and stalled native acknowledgement preserve the host 15000-ms dispatch deadline. One in-flight request, 30 failures per monotonic rolling hour/profile, no forwarded SDK retries, independent manager with two held executions, hostile fields and cancellation/deadline races pass. Unknown remains quarantined; SDK acknowledgement/socket closure do not certify physical stop, rollback or resource release. RT2/PV1 and integrated R13/R04 authority/leases remain deferred. |
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

### 2026-10-07 Deterministic Privacy Lifecycle Regression Slice

The existing production observer already subscribes to WTS, power and MMDevice
events and uses a one-second microphone-permission polling fallback. This slice
adds a `TimeProvider` seam at that existing timer, not another observer.
[Observer regressions](../tests/Kora.Windows.IntegrationTests/Session/WindowsPrivacyObservationServiceTests.cs)
execute polling without wall-clock sleeps and verify queued ticks/native callbacks
after disposal and source lifetime during an in-flight query. Disposal now closes
observation admission before waiting for the query to finish and disposing its
native source.

[Observer-to-capture regressions](../tests/Kora.Windows.IntegrationTests/Audio/ActivatedVoiceRecognitionTests.cs)
exercise the production observer and recognition service together with synthetic
platform/capture boundaries: Unknown/lock/disconnect/suspend/sign-out close the
generation, clear buffered audio and release the recorder before the following
query; denied/Unknown permission and failed polling queries fail closed.
Restoration does not restart capture. Pending opens, queued native callbacks,
System versus pinned endpoint loss and held-activation stop/disposal are covered.
[Application regressions](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.VoicePrivacy.cs)
retain explicit run holds and now retire input admission on disposal, reject
already queued transcript/state/completion/topology/failure presentation, and
invalidate an observed unavailable System output before queued enumeration without
interrupting a still-available pinned route.

No real capture, playback, installed launch, session transition, device/permission
change or security-policy trial was performed. These deterministic checks do not
establish the 500 ms native lock-release target, acoustic behavior, reference
hardware or complete A03/A04 acceptance.

Experiment disposition: retain `r02-speech-proof` because its candidate/licence,
file-based keyword benchmark, acoustic/playback and reference-hardware gates are
not superseded by privacy lifecycle tests. Retain the containment and runtime
fixtures: their native token/ACL/network/descendant/effect receipts, failed
hook-only mediation witness and all-native/runtime observation gaps are distinct
from microphone generation/disposal proof. No executable or historical receipt
is retired or relabelled by this slice.

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
| A03 - External session privacy | Existing WTS/power observation, synchronous negative closure before requery and stale-callback/disposal boundaries have deterministic regression coverage, not native timing acceptance. Separately approve Win+L/idle lock, disconnect, suspend/resume and applicable session transitions while an owned activation/output is active. Capture closes, buffers clear, output stops and sensitive presentation hides. Unlock/reconnect/resume requires explicit recovery and cannot replay audio/approvals. | **Capture released within 500 ms from the observed lock event in every reference trial** remains unproved. Record OS event-to-notification delay separately, plus each observed-event-to-release duration, buffer clearing, last output sample and zero stale dispatch; no averages/p95 substitution for this target. |
| A04 - Permission and device changes | Existing one-second permission polling and MMDevice observation have deterministic timer/capture/route/run-hold regression coverage. With separately approved capture/output fixtures, revoke/restore desktop microphone permission; remove/disable pinned endpoints; change System-default input/output; hot-plug and refresh. Active System-selected WASAPI streams reroute to available new defaults; pinned endpoints and streams are unaffected by unrelated device/default changes. Missing or muted effective endpoints close affected audio without substituting a same-name device. Restored readiness never removes a run hold. | Native polling detection latency, endpoint/permission revisions, successful eligible rerouting and pinned-route continuity, native failure/closure receipts, individual timings, bounded audio clearing, stopped unavailable output without replay and explicit input recovery after closure remain outstanding. The one-second polling interval is not a measured native closure bound. Do not change global privacy settings without separately scoped approval. |
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
