# Deferred Proof Validation

Status: outstanding validation register, not passed acceptance or permission
to run a test. Partial feasibility evidence and scoped implementation may merge while
unproven capabilities remain gated and their decisions/acceptance gates stay
open. Merge is not certification for production release.

Scope follows the authoritative
[three-tier qualification policy](Acceptance_Criteria.md#three-tier-qualification-policy):
this register is not an aggregate repository-wide merge/RC checklist. Each
outstanding trial blocks only its affected capability/profile exposure and RC
inclusion; final RC qualification uses an explicit enabled-capability manifest
and records exclusions, not assumed passes. Directly applicable critical
fixtures and fail-closed shared controls remain mandatory. The roadmap's
[current disposition inventory](Implementation_Roadmap.md#experiment-disposition-inventory)
owns retain/migrate/archive policy; dated rerun and equivalence receipts below
are preserved, not permission to delete partially replaced harnesses.

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

### Focus-free preparation for interactive native accessibility

The maintained [native UX validation runner](../eng/Invoke-NativeUxValidation.ps1)
and [operator walkthrough](../tests/Kora.NativeUxFixture/README.md) target the
remaining screen-reader, text-scale and physical mixed-DPI observations, using
the existing isolated synthetic fixture. Default preparation opens no windows,
sends no input and changes no Windows settings. It binds focused headless test
results, source fingerprints and complete payload hashes in an external local
evidence bundle. Actual native launch requires a separate explicit operator
switch when desktop focus is available; Narrator and display changes remain
manual operator decisions.

Preparation is not native acceptance. Sign-off requires clean exact-payload
interactive trials and explicit operator outcomes, records Blocked/Fail honestly,
and does not close full R05/R12/R14/A4 or real privacy/audio/installed gates.
Previously completed scoped observations and historical receipts below remain
unchanged. No experiment harness is replaced or retired.

The [PID-scoped mechanical driver](../eng/Invoke-NativeUxMechanics.ps1) can now
replace repetitive native control and Tab/Shift+Tab input during a separately
approved, deadline-bounded desktop window. It records disabled maintenance
consent, complete repeated Appearance cycles, exact question/draft/submit,
stale-revision refusal and synthetic window closure as separate machine
observations, without filling operator-only Narrator/readability/display rows.
Every failed native attempt and cleanup disposition remains evidence.

### 2026-10-10 Automated Interactive Proof Batch

**Disposition: available bounded automated scopes completed; full qualification
and the protected live trials remain open.** The operator made the desktop
available for a sixty-minute automation window. The maintained mechanical
driver ran against a new exact-payload preparation bundle from source base
`53064f7`, including uncommitted automation changes. The local receipts retain
the complete build-input fingerprint, separate driver SHA-256, payload inventory,
test hashes, timestamps, individual outcomes and cleanup. This is not an
observation of subsequently changed application bytes or a release certification.

Two final complete native repetitions passed all five mechanical rows each
using identical driver/preparation bytes: disabled/off maintenance network
consent; all fifteen Appearance cycle entries forward and reverse, each repeated
twice; immutable same-request/question review, one-revision draft and explicit
submit with terminal focus/status persistence; stale-question refusal preserving
the original review; and synthetic private-window closure/reopening without
replay. Both exited 0, cleaned their exact scratch children and required no
forced termination. No Narrator, audio, screenshot, model, browser or global
Windows setting operation was performed by the driver.

| Automated scope | Fresh outcome |
|---|---|
| Native driver admission/receipt contracts | 38 passed, including explicit desktop approval, expired/unbounded deadline refusal and no-launch prerequisite mode |
| Focused native fixture/accessibility tests | 77 passed, zero skips; Release build/publish completed with zero warnings/errors |
| Windows scratch storage, coordination and privacy-policy tests | 548 passed, zero skips; these do not replace actual audio/OS-transition acceptance |
| W1/W2 deterministic dependency/containment controls | 97 passed, zero skips |
| Actual W2 owned-scratch boundary trials | 62/62 diagnostic assertions, zero harness errors, exit 2: strict ACL/no-child admission remains **Rejected**, not certified; exact temporary AppContainer and scratch cleanup recorded |
| Runtime file-only and receipt-locale contracts | 49 plus 2 passed; no Copilot SDK/native/runtime/account trial admitted |
| MG1 account-free host-envelope tests | 22 passed, zero skips; temporary external feed mapping resolved the initial NU1100 restore failure without repository package configuration changes |
| Inference deterministic self-tests | 55 passed without endpoint observation, generation or residency changes |
| Inference worktree-relative path regressions | 6 passed in the documented ignored checkout scratch layout; the separate external-output attempt failed repository discovery and remains an unsuccessful receipt, not an external-path pass |
| Installer payload refusal contracts | Passed without compiler invocation, installer staging, installation or elevation |

Nine earlier native driver-development attempts remain failed local receipts,
not qualifying application findings or relabelled passes. The initial launcher
discovery failure required owned-PID termination; its synthetic scratch was
archived locally and the exact leftover child removed afterward. The original
failure/cleanup receipt remains unchanged. Subsequent harness repairs addressed
native title/provider readiness, supported textbox value inspection, authoritative
draft-record validation and guarded exact-window keyboard actions.

Runtime preparation still refused its historical-baseline guard because four
historical README files differ from its recorded base; that guard was not
weakened. Approved native SDK/runtime artifact inputs were absent from the
documented fixture locations. Native loading in W2 remains blocked by the
missing reviewed compiler/native fixture. An already running Ollama process
was not started or owned by this session and was left untouched; exclusive
generation/residency, agreed budgets/reference floor and offline attribution
were not inferred from desktop availability. Raw receipts, payloads, TRX,
failure archives and the consolidated summary remain local.

**Minimum remaining operator work:** actual Narrator announcement observation,
rendered readability at the approved text scales and physical mixed-DPI display
conditions, followed by one scoped receipt review. Repetitive mechanical control
input above need not be performed manually to reproduce its recorded machine
outcomes. Actual privacy/audio/lock/device/handoff/crash and installed trials
still need prepared, separately scoped boundaries; no full R03/R05/R12/R14/A4,
runtime/inference/containment or release gate is closed by this batch.

### 2026-10-10 Extended Workstation Proof

The [extended driver](../eng/Invoke-NativeUxWorkstation.ps1) reuses the maintained
five-row native mechanics driver, then launches a separate exact-owned fixture
for ten additional scratch-only scopes. The first batch was merged in #157;
these expanded application and fixture changes were validated separately.

Two final repetitions passed all five baseline and ten narrowed expanded rows
each using identical driver/preparation bytes. All four owned fixture processes
exited 0, cleaned their exact scratch children and required no forced termination;
their PIDs were absent after completion. The final worktree build-input digest
matched its frozen preparation bundle. Release solution build/publish completed
with zero warnings/errors after a locked machine-feed restore resolved missing
unit-project assets. Admission contracts passed 43 checks, focused preparation
passed 79 tests, and the combined eight relevant regression classes passed 86
tests with zero skips. These overlapping test sets are not additive totals.

The expanded rows passed native exact-ID session create/rename,
alternating bounded passive history, guide/detail lifetime and immutable source
digest checks, all bundled default-package file tabs, explicit retained/missing
evidence links with cross-session navigation confinement, stale displayed
generation refusal, owned-window native display placement, exact queue
removal/manual dispatch, synthetic gate closure with old-epoch dispatch refusal,
and metadata-only logical disposition with retained tombstone citations.

The native display trial observed two OS-enumerated work areas at native DPI 96
and 192. It verified actual owned-window containment, exact selector focus and
original placement restoration without global display changes or screenshots.
This is not readable-text, physical-rig or all-window mixed-DPI acceptance.
The queue uses the real bounded local-version service in fixture scratch stores,
not a model/effect/provider action. Post-reopen dispatch returned zero receipts
for the exact still-pending old-epoch entry; it was then deliberately cleared.
The disposable metadata-only session retained bounded exact-ID history at its
tombstone generation, while the unrelated session remained unchanged.

Native passive work observation exposed a real presentation-focus defect:
rebuilding every work-row object/list moved focus off the exact control during
the periodic refresh. The fix retains collection membership and exact
task/source row identity, updates only changed displayed fields, and preserves
selection without renewing inspected task cancellation authority. Focused
headless tests cover stable realized containers, meaningful row text, no
unchanged-field notifications, expiry-state changes, stale queue revisions,
selection isolation and closed/disposed content clearing. The successful native
run also preserved the exact focused control through a passive queued refresh.

**Unqualified boundary:** after retained terminal queue rows, a newly enqueued
pending row appeared in truthful native structured receipts/work status but was
not discovered in the Windows UIA realized list descendants. Headless peer
descendants exposed it. Failed native development trials remain failed; a custom
child-event peer and a collection-rebinding experiment did not repair the native
result and were removed. W09 checks exact structured queue/dispatch receipts
and clearing only; it does not claim that new row was discoverable or announced.
This limitation required separate diagnosis before qualifying that native route.
The later work-list scroll-provider correction below records the new native
results; it does not relabel these earlier observations.

The operator's minimum remaining human work is documented as three passes:
listen to actual Narrator output, inspect rendered themes/text sizes/physical
displays, then restore settings and attest to the five independently scoped
observations. Machine mechanics never populate the operator template.
No actual lock/privacy transition, audio, model, hosted account, normal profile
source, installer, clipboard/disclosure or global Windows setting is admitted.

### 2026-10-10 Additional File-Only Native Speech Slice

After completing the workstation mechanics, the operator requested another safe
workstation proof without publishing the expanded changes. The retained
[speech fixture renderer](../experiments/r02-speech-proof/Render-Fixtures.ps1)
was copied byte-for-byte into two new local evidence directories outside the
checkout. It rendered authored synthetic text only through installed SAPI file
streams: no microphone, render endpoint, production host, optional model,
network request, installation or global setting was used.

Two installed English desktop voices each rendered six phrases at native rates
-1 and +1, yielding 24 WAVs per repetition. All 48 files matched their provenance
digests and contained nonempty/nonzero mono PCM16 at 16 kHz. Every faster-rate
utterance had fewer frames than its corresponding slower-rate utterance.
All 24 corresponding waveform SHA-256 values matched between repetitions on
this profile; no cross-machine/voice-version bit identity is claimed.

A separate local .NET harness then used the repository's exact Windows
RID-specific System.Speech 10.0.12 implementation with the installed en-US
recognizer. It loaded bounded authored command choices plus dictation and
supplied only provenance-bound WAV files, never the production capture factory
or a default audio device. All 32 positive synthetic file trials matched their
normalized expected text; confidence ranged from approximately 0.6481 to 0.9894.
The all-zero PCM silence control returned no result; malformed WAV input raised
an explicit FormatException. These are native file-boundary observations, not
production confidence-threshold, command-authorization or dispatch acceptance.

Two earlier harness failures remain retained: PowerShell's assembly-version
conflict, then platform refusal from the neutral System.Speech API stub.
The dependency manifest identified the actual Windows runtime implementation;
correcting that isolated harness reference required no application changes.
The corrected harness built with zero warnings/errors. The speech output
policy/rate/volume/generation slice passed 32 tests and the fake-boundary
activation/shutdown/bounded-buffer slice passed 67 tests, with zero skips.
Raw synthetic WAVs, native recognition receipts, source hashes and logs remain
local; no recordings or profile content were captured or published.

**Disposition: scoped file-only native speech evidence, not D-002/D-007,
R03/R09/R10 or acoustic acceptance.** Physical first-command-word behavior,
recognition across real speakers/noise, wake/false-activation quality, live
playback/stop timing, endpoint routing/privacy transitions and packaged-host
integration still need separately approved fixtures and participants.

### 2026-10-10 Native Ownership Presentation Slice

The exact production
[ownership prompt policy](../src/Kora.Windows/Coordination/NativeLifecyclePrompt.cs)
was linked unchanged into a local isolated test host. A read-only preflight
passed three current-profile/session eligibility checks and denied an invalid
WTS session in all three repetitions. It displayed no dialog and did not start
an assistant or transfer ownership. Current-user protocol/continuity tests
passed 24 cases with zero skips, including real restricted test mutex exclusion,
current-user-only pipe OS peer-PID queries and held process-token/creation checks.
Their names are GUID-scoped test names, not live coordinator names; the pipe
peer test is within one process, not a cross-account/cross-build owner trial.

The operator then separately approved bounded synthetic native dialogs. Two
fresh-process repetitions used identical built harness bytes, each exercising
three expiry and three predicate-invalidation dialogs plus a denied-predicate
no-display control. Native window discovery admitted only the host's own PID
and dialog class. Read-only GUI-thread/ancestor checks observed focus on the
exact owned No control (native control ID 7) in all twelve displayed dialogs.
No keyboard/mouse input, screenshots, executable-identity disclosure or
ownership action was performed.

All one-second expiry cases returned denial after approximately 1080-1106 ms;
all invalidation cases returned denial and closed their dialog before the
separate fifteen-second timeout, with total trial times approximately
100-126 ms. These durations include setup/observation and are not measurements
of a lock-notification-to-audio-release boundary or a claimed 500 ms privacy
target. Denied predicates displayed nothing. Every exact dialog was gone after
return, and the native policy retained default No. Both hosts exited cleanly;
source/receipt/harness hashes and individual observations remain local.

**Disposition: scoped current-profile native ownership presentation evidence.**
Actual same/different-build handoff, authenticated candidate content identity,
approval/expiry races during real work, device/service resource quiescence,
crash takeover/return, installed protection, lock transitions and screen-reader
announcements remain separate A05/A06/accessibility acceptance. No global
session setting, alternate account, production host, audio, model, normal
profile content or live ownership marker was used.

### 2026-10-10 Additional Native Typed-Slash Presentation

An unpublished local driver reused the maintained five-row baseline and the
unchanged frozen native fixture, then opened a fresh owned process for three
additional native presentation rows. Two final repetitions passed all three
rows each with identical driver/preparation bytes; both extra processes and
their baseline processes exited 0 with exact scratch cleanup and no forced
termination. No production application change was needed for these rows.

The response window's nonactivating show retained focus in the exact launcher
HWND rather than moving it into the new response HWND. The launcher command's
temporary disabled state can move focus from its button to its own window;
that is not activation of the response window. Eleven native input cases
covered plain/empty input, slash and case-insensitive prefixes, skill-kind
qualification, nonmatching template/unknown inputs and argument-bearing text.
Only the single declared non-executable synthetic `/inspect-fixture` option
appeared in matching cases.

Three independent open/select/fill/close cycles per repetition verified that
passive list selection did not fill input, explicit invocation of its exact
owned candidate button filled `/inspect-fixture `, hid the dropdown and returned
focus to the exact native typed field. The driver activated only the verified
owned response window for that deliberate interaction. Run and Enter were
never invoked; no artifact, clipboard, model, audio, network or application
process action was admitted. Six deterministic artifact-route/dropdown tests
also passed with zero skips, covering the shared typed/activated-voice route,
model-action gate, unknown commands, stale options and kind filtering.

Two earlier probes remain failed receipts: an overstrong launcher-button focus
expectation, then PowerShell's automatic `$input` enumerator shadowing a test
field variable. Corrected native results are not retroactive passes for those
attempts. The current fixture has no synthetic caption playback-state hooks;
the separate caption's native rendering/placement/lifecycle remains unqualified,
not inferred from response-window nonactivation or static caption contracts.
Actual Narrator and rendered readability observations remain human-scoped.

### 2026-10-10 Additional Native Assistant-Name Preferences

An unpublished local driver reused the unchanged frozen fixture in private
scratch storage. Two final repetitions passed four additional native rows each:
unsaved Kora default without an unnecessary Apply; explicit invalid `bad/name`
refusal without persistence or identity change; domain-normalized `Nova Ray`
save with exact native title/editor updates; and explicit reset to Kora with
same-process settings reopen/readback and no remaining atomic-write temporary.
All four baseline/name fixture processes exited 0, cleaned their exact scratch
children and required no forced termination. Identical driver/preparation
bytes, preference digests, source fingerprints and native receipts remain local.

The name rules/options passed 14 tests, command/service/preferences passed 43,
and directly related view-model lifecycle tests passed 12, all with zero skips.
The latter use deterministic fake capture boundaries; native prefix-retirement
acceptance is not implied. The consent-denied native fixture truthfully retained
its closed-microphone consent explanation and never invoked Enable listening.
No production profile write, audio endpoint, model, network or Windows setting
change was admitted.

Two earlier attempts remain failed receipts. The first driver expected an
invalid draft to remain after refusal, although the UI restores authoritative
state. The second expected the prefix-change hold explanation, although missing
voice consent correctly takes precedence. Correcting those observations does
not relabel their original results.

**Disposition: scoped native preference mechanics, not accessibility or live
capture acceptance.** The editor's native accessible Name was empty in both
final repetitions and remains an unqualified screen-reader discovery boundary.
Settings reopening is not process-restart persistence acceptance; actual
Narrator, rendered readability and live capture retirement remain separate.

### 2026-10-10 Silent Native Caption Presentation

The operator explicitly approved a separate silent synthetic-playback fixture
and fresh local payload, with all changes unpublished. Its exact additional
launch flag leaves the ordinary fixture's effect refusals unchanged. A test-only
in-memory Windows-provider contract supplies an authored utterance, exact
playback ID and frame; it has no synthesis engine, native audio endpoint or
capture implementation. The ordinary typed response path, caption policy,
admitted preference commands and production caption window/controller are used.

Two final identical-byte repetitions passed five additional native rows each:
queued text without a visible caption; exact read-only utterance and
nonactivating show at all four primary-display corners; native pin plus truthful
PREVIOUS SPEECH retention beyond the one-second unpinned delay; explicit active
Stop and completed-caption typed Stop without replay; and synthetic privacy
hide/pin retirement with fail-closed gate reopening and no automatic replay.
Each repetition admitted eight distinct synthetic playback identities.
At native DPI 96, all four measured caption bounds were inside the primary
5120-by-1392 working area at the expected 24-pixel margins, within the stated
two-pixel coordinate tolerance. No secondary-display visual acceptance is implied.

Fresh preparation passed 83 tests with zero skips and zero build/publish
warnings/errors. Separate caption-domain, preference/service, view-model
transition and UI groups passed 4, 92, 39 and 1 tests respectively, all with zero
skips. These 136 related tests are not added to the preparation count as a
repository-wide total. Both final native processes and their baseline processes
exited 0, cleaned their exact scratch children and required no forced
termination. The new and original frozen payload manifests remain unchanged;
the old workstation results apply to their original bytes, not the new fixture.

Failed preparations, targeted diagnosis reports and four earlier native attempts
remain retained. The synthetic provider initially conflated arming with active
output; the fixture now separates them so ordinary prior-source retirement does
not cancel a future reservation. Native waits initially accepted stale
unchanged-placement/absent-window observations before async Save/Stop completion;
exact completed-operation and monotonic snapshot-revision receipts now gate
progress. A retention timer also shadowed a shared helper's local variable.
No production permission or caption policy was weakened to repair these probes.

The fourth native attempt deliberately tried fresh playback after gate-only
reopening and was refused by the held private-presentation boundary. The final
row verifies that refusal state/no replay, not successful recovery. Real
transition/recovery, acoustic playback/Stop/resource timing, physical rendered
readability and Narrator output remain separately approved/unqualified scopes.
Raw local receipts are research evidence, never security audit or release
authority. No audio, real Windows transition, installation, normal profile
content, model, network, clipboard or screenshot operation was admitted.

### 2026-10-10 Assistant-Name Native Accessibility Correction

Following the empty-name observation above, the operator approved a narrowly
scoped label correction and fresh native validation, unpublished. The
[settings editor](../src/Kora/SettingsWindow.axaml) now exposes
"Display and push-to-talk command-prefix name" and binds its HelpText to the
existing authoritative assistant-name configuration description. Name limits,
normalization, persistence and consent semantics are unchanged; no duplicate
validation or description rules were introduced.

Fresh preparation passed 84 focused tests with zero skips, including stable
headless Name/HelpText after save/reset; build and publish had zero warnings or
errors. Both final native repetitions passed the four name-preference rows,
using identical driver/preparation bytes. Windows UIA discovered the field by
its exact label, and its HelpText exactly matched the visible authoritative
description for default Kora, normalized Nova Ray and reset/reopened Kora.
The 43 maintained automation contracts also passed without native launch.
All four baseline/name processes exited 0, removed their exact scratch children
and required no forced termination. Local source/payload/test/receipt identities
remain preserved; prior empty-name receipts remain historical failures on their
original bytes, not overwritten.

**Disposition: corrected and machine-verified native editor label/help.**
Actual Narrator understanding, rendered readability, live capture retirement
and process-restart persistence remain unqualified. No normal profile, audio,
model, network, global Windows setting or publication operation was admitted.

### 2026-10-10 Native Work-List Scroll Provider Correction

The operator approved a bounded new-work-row investigation and another
one-hour interactive window, keeping all expanded changes unpublished.
A temporary scratch-only diagnostic fixture compared authoritative items,
realized containers, viewport/extent/offset and actual Windows UIA descendants.
It reproduced a pending task at index 2 below the first two retained terminal
rows: the host had a nonempty scroll range but the native list reported no
vertical scrolling. The pending row was not lost by collection reconciliation;
it was not realized within the viewport.

Avalonia 12.1.3's stock items peer advertises ScrollPattern while reading an
uninitialized scroll-provider field. The scoped
[work-list control](../src/Kora/Controls/ScrollableListBox.cs) now delegates that
interface to the current template's real scroll viewer. Native provider discovery
may run off-thread, so it returns the peer without touching controls; delegated
members resolve the template only when the bridge invokes them on the UI thread.
Virtualization, collection ordering, stable row identity, passive selection and
inspected cancellation authority are unchanged. No custom child-event peer or
collection rebinding was retained. The temporary diagnostic launcher action was
removed from the final payload.

The separate diagnostic payload passed three native rows: terminal population,
new exact pending-row discovery through the list's truthful native scroll range,
and all five 0/25/50/75/100-percent positions with exact focused-container and
scroll-position preservation through passive refresh. Its original payload
identity remains local; it is not relabeled as the later final source.

Final preparation passed 86 focused tests, zero skips, with zero build/publish
warnings or errors. Added contracts cover off-thread provider discovery,
template replacement, unavailable/disabled/invalid scroll operations, unchanged
selection and bounded realization of new pending work. The four supporting
work/queue/authority classes passed 32 tests with zero skips. All 43 maintained
admission contracts passed without native launch. These are scoped test groups,
not a repository-wide qualification total.

Two final identical-driver/preparation repetitions passed all five baseline and
ten maintained workstation rows each. W09 now requires native discovery of the
exact new pending task after terminal-populated work before synthetic gate
closure; it then retains the old-epoch dispatch refusal/no-replay and deliberate
clear checks. W08 again preserved exact native focus without passive dispatch.
All four final owned fixture PIDs were absent afterward, exited 0, cleaned their
exact scratch children and required no forced termination. The current
build-input fingerprint matched final preparation; original workstation,
caption and name-label payload/test manifests were independently verified
unchanged.

Earlier failed builds and native probes remain retained, including driver
selection/readiness/retired-HWND corrections and the first adapter's off-thread
provider-discovery failure. One early readiness failure interrupted dispatch
completion and ended with an evidence-file cleanup error, exit 1 and retained
exact scratch residue; it is not relabeled as a clean pass. Raw diagnostics,
receipts, payloads, TRX and the strict final summary remain local.

**Disposition: native discovery of this exact bounded work-list route is
corrected and machine-verified.** Narrator announcement/understanding,
rendered readability, physical-display and full all-surface accessibility
qualification remain open. Other stock list surfaces were not qualified by this
scoped correction; the later approved two-surface extension below records its
own fresh native outcomes. No audio, real Windows transition, ownership transfer,
installation, model/provider/network, normal profile, clipboard, screenshot or
publication operation was admitted.

### 2026-10-10 Extended Native List Overflow Validation

The operator next approved checking other bounded native lists without initially
changing behavior. The scratch-only audit created nine metadata-only sessions
and read one authoritative bounded evidence page. Windows UIA realized six
session rows and three of 43 evidence records while both stock lists reported
no native vertical scroll range. The local-event and evidence-link lists had
no overflow content and were explicitly not treated as passing overflow trials.
The original two findings and clean audit receipts remain unchanged locally.

After separate approval to fix the confirmed surfaces, the durable-session and
cited-evidence record lists reused the
[same scroll-provider control](../src/Kora/Controls/ScrollableListBox.cs).
No extra provider abstraction, altered ordering/selection, disabled
virtualization, native event workaround or authority change was introduced.
The focused headless contract checks overflowing real window controls and
unchanged exact session entries/selection and evidence-page state after scroll.

Two final identical-byte native repetitions each discovered all nine exact
new synthetic session IDs and all 43 exact citations in the bounded evidence
page, using overlapping native scroll positions. Both lists had demonstrated
overflow and a truthful nonempty ScrollPattern range; merely advertising the
pattern or testing an entirely realized list was not sufficient. Variable-height
evidence realization changes estimated extent and reported percentages, so
receipts preserve requested and actual positions instead of requiring an
unchanged percentage. Discovery of every exact citation is the acceptance
check, not a frozen scroll-geometry proxy.

Fresh preparation passed 87 focused tests, zero skips, with zero build/publish
warnings or errors. Five supporting work/session/evidence classes passed 35
tests with zero skips. All 43 admission contracts passed without native launch.
Two more identical-driver/preparation full workstation repetitions passed all
five baseline and ten workstation rows, including exact newly added pending
work discovery and passive focus/no-dispatch, on this final combined payload.
All eight final owned fixture PIDs were absent afterward, exited 0, cleaned
their exact scratch children and required no forced termination. The current
source matched the sealed preparation identity; prior work-list, workstation,
caption and name-label payload/test manifests remained unchanged.

The initial new headless test incorrectly required a filtered session projection
to retain its array reference; the corrected contract compares exact unchanged
entries. Two driver attempts incorrectly waited for Create before filling its
required draft and for a frozen percentage during variable-height realization.
Their failed original receipts remain retained, not relabeled as final passes.
Raw scripts, payloads, detailed snapshots, TRX and strict summary remain local.

**Disposition: all three demonstrated bounded list scroll routes are corrected
and machine-verified.** Local-event, evidence-link and typed-command-catalogue
overflow are not qualified by these tests; no oversized synthetic content was
injected for those lists. The protected microphone-recovery device surface was
not exercised. Actual Narrator output, rendered readability, physical displays,
real privacy/ownership/audio and installed/provider/model acceptance remain
separate. All expanded automation and corrections remain unpublished.

### 2026-10-10 Remaining Synthetic Small-List Overflow Validation

The operator separately approved synthetic overflow coverage for local events,
trace links and the typed-command catalogue, then approved fixing all three
confirmed scroll gaps. The new exact `--list-overflow-fixture` mode is separate
from default native launch and cannot combine with silent captions. It uses
nine successful scratch local-version dispatch receipts and one pending task,
the real event source/broker with its eight-visible limit and two-omitted status,
twelve missing host activity links, and twelve non-executable catalogue entries.
No event eligibility, fatigue, queue capacity or navigation/execution authority
was bypassed. Normal voice consent and external-effect refusals remain intact.

The initial native audit realized only two of eight event rows, three of twelve
link rows and three of twelve command rows, with no native vertical scroll
range on any list. All audit processes exited cleanly. After approval, these
three controls reused the existing
[scroll-provider correction](../src/Kora/Controls/ScrollableListBox.cs).
The focused real-window test checks scrolling without changed event selection,
status, missing-link records, evidence state, command selection/draft or the
exact pending queue snapshot.

Two final repetitions on identical driver/preparation bytes each discovered
**8/8 exact event IDs, 12/12 missing-link trace IDs and 12/12 slash command names**
through truthful native scroll ranges on genuinely overflowing lists. Each
trial retained the eight-visible/two-omitted broker result, missing/non-navigable
link authority, `/overflow` draft and exact sole pending task. No event control,
link navigation, catalogue application or Run/Enter operation was invoked.
Requested and actual scroll percentages remain in the local receipts; every
exact token's discovery, not frozen estimated geometry, is the native check.

Fresh preparation passed 90 focused tests without skips or build/publish
warnings/errors. Four supporting application classes passed 1,244 tests;
six event/work/session/evidence integration classes passed 38 tests; all 43
admission contracts passed without native launch. These are scoped test groups,
not an aggregate repository-wide result. Two more final-payload workstation
repetitions each passed five baseline and ten expanded rows, including exact
new-work discovery, passive focus and old-epoch no-dispatch.

All eight final owned fixture processes exited 0, cleaned their exact scratch
children, needed no forced termination and were absent when the strict summary
was sealed. Current build inputs matched the final preparation fingerprint.
Prior extended-list, work-list, workstation, caption and name-label payload/test
manifests remained unchanged. Raw scripts, snapshots, payloads, TRX and the
source-bound summary remain local and unpublished.

The earlier overflow preparation with ten pending tasks legitimately produced
only one eligible ready-head event. Its failed test receipt remains unchanged;
the replacement fixture explicitly dispatches nine scratch tasks instead of
manufacturing trailing-head observations. The initial analyzer/build failure
and original three native findings are likewise retained, not relabeled.

**Disposition: all six demonstrated bounded native list scroll routes are
corrected and machine-verified.** No further manual repetition of their exact
discovery mechanics is required. Actual Narrator understanding, rendered
readability/text scales/physical displays and restoration/attestation still
require the three operator passes in the
[walkthrough](../tests/Kora.NativeUxFixture/README.md#minimum-operator-walkthrough-after-automated-mechanics).
The protected microphone-recovery device list, real audio/privacy transitions,
ownership transfer and installed provider/model/network scopes remain
unqualified and separately gated. No release/full-accessibility certification
or publication is implied.

### 2026-10-10 Current-Main Publication Revalidation

After the operator requested commit and PR publication, the validated batch was
replayed onto main at `d3c0dc5`, excluding the already squash-merged #157 work.
Earlier byte-bound receipts above remain historical, not evidence for the
integrated source. Main's stale session-list search guard was preserved during
work-row reconciliation. The silent fixture now composes the current explicit
caption window and display source, without changing caption targeting policy.

Native revalidation exposed a lifecycle callback threading defect: storage
completion invokes session revocation from a worker thread, while the Sessions
controller accessed its Avalonia window there. With separate operator approval,
the controller now synchronously marshals revocation to the UI thread before
reporting lifecycle completion. Two focused cases verify closing a matching
window and revoking list metadata while preserving unrelated exact work.
Disposed callbacks cannot reopen or alter a window.

Current main intentionally closes a Sessions window referencing a changed or
retired session. W06/W10 therefore require exact owned-window revocation,
explicit reopening, and fresh same-ID generation/history observations.
They do not retain stale native controls to repeat the old refusal experiment.
Exact session selection now discovers off-viewport IDs through bounded native
scrolling. The pending-row test likewise requires scroll-assisted discovery,
not immediate realization in main's larger Sessions layout. Historical native
and deterministic old-generation refusal receipts remain separate.

The final integrated preparation passed **105 focused tests**, zero skips,
with zero build/publish warnings/errors. The full Release solution build also
passed with zero warnings/errors. Eight relevant session/search/caption/event/
evidence integration classes passed **70 tests**, including both worker-thread
revocation cases; four supporting application classes passed **1,359 tests**.
All **43** admission checks passed without native launch. These scoped groups
are not summed into repository-wide or release acceptance.

Two final identical-byte workstation repetitions each passed five baseline and
ten expanded rows. Two strict small-list repetitions each discovered all
8 event IDs, 12 missing-link IDs and 12 non-executable command names. Additional
fresh native repetitions passed five silent-caption rows, four assistant-name
preference/label rows, and exact 9/9 session-ID plus 43/43 evidence-citation
overflow discovery. Those last three scopes were each repeated once after
integration; their earlier two-repetition receipts remain unchanged.

All fourteen final owned fixture processes exited 0, cleaned their exact scratch
children and needed no forced termination. The strict local publication summary
verified every final PID absent, driver/preparation/source identities and the
fixed approval cutoff. Prior small-list, extended-list, work-list, workstation,
caption and name-label bundle manifests remained unchanged.

The initial caption-constructor build failure, pending-row realization test
failure, lifecycle callback native failure and two stale-window/discovery driver
failures are preserved locally. A misplaced using directive and an incorrect
test reference comparison were corrected before the final passing runs; failed
attempts are not relabeled. Raw scripts, payloads, account/path-bearing logs,
snapshots and TRX remain local and are excluded from publication.

**Disposition: the operator approved publication of these scoped changes,
not full accessibility or release qualification.** Actual Narrator,
rendered text/physical-display acceptance and restoration/attestation remain
the three operator passes. Real audio, OS transitions, ownership transfer,
installed-provider/model/network and protected device-list proofs remain
separately gated. No auto-merge permission was included in this publication
request.

### 2026-10-10 PR 162 Latest-Main Queue Revalidation

The operator requested updating #162 against newly advanced main. Main
`1c5bd17` was merged without rewriting the published branch. Its future-only
captured active admission deadlines, work-row text, queue preferences and
authoritative event eligibility remain intact.

Fresh Release solution build and fixture publish passed with zero warnings/
errors. Preparation passed 105 focused tests; targeted changed-queue groups
passed 66 Core, 1,639 Application and 81 Windows tests with zero skips. All
43 admission contracts passed without native launch. One new final-payload
repetition passed all five baseline and ten workstation rows. A separate
fresh repetition discovered all 8 event IDs, 12 missing links and 12 catalogue
names while preserving the exact pending task and eight-visible/two-omitted
event limit.

All four new final fixture PIDs were absent, exited 0, cleaned their exact
scratch children and required no forced termination. Native operations
finished within the original fixed approval cutoff. The latest source-bound
local summary verifies those receipts and targeted TRX counts; the prior
publication bundle remains unchanged. Earlier caption/name/other-list
observations above retain their earlier fingerprints and are not reattributed
to this latest queue update. No additional protected or human proof is claimed.

### 2026-10-10 PR 162 Retention-Main Auto-Merge Revalidation

The operator enabled protected squash auto-merge for #162, then approved
another main update and a new fixed one-hour synthetic desktop-validation
window. Main `fae6428` was merged without rewriting published history. Its
per-session retention inspection, reviewed control fences and live presentation
state remain intact; no retention policy or grant boundary was changed.

Fresh Release solution build/publish passed with zero warnings/errors.
Preparation passed 106 focused tests, supporting Application classes passed
1,598 tests and seven retention/storage/work/list/event Windows classes passed
57 tests, all without skips. All 43 admission contracts passed without native
launch. One final-payload workstation repetition passed five baseline plus ten
expanded rows. Fresh overflow repetitions discovered exact 8/8 event IDs,
12/12 missing links, 12/12 command names, 9/9 new session IDs and 43/43 citations.
These are scoped groups and repetitions, not repository-wide certification.

All six final native processes exited 0, cleaned their exact scratch children,
required no force and were absent when the source-bound summary was sealed.
Native operations used the newly recorded fixed cutoff; previous queue-update
and publication bundles remain unchanged. No new live retention-control trial
or caption/name/audio/Narrator/readability/physical-display acceptance is
attributed to these bytes. The renewed window retains the original synthetic
ownership/privacy boundaries. Auto-merge does not bypass required checks,
approvals or up-to-date branch rules.

### 2026-10-10 Automated Native Session-Retention Controls

After #162 merged, the operator separately approved one fixed hour of
scratch-only retention UI automation and supporting checks. The maintained
[retention driver](../eng/Invoke-NativeUxRetention.ps1) uses the default
synthetic fixture's real retention service and the existing exact PID/HWND,
foreground keyboard and deadline guards. No production policy, private gate,
normal profile data or fixture effect boundary was changed.

Fresh preparation passed 106 focused tests, zero skips, with zero Release
build/publish warnings or errors. The no-launch admission suite passed 48
checks. Supporting Windows retention and owned-process interruption/recovery
classes passed 94 tests; Application timer/configuration/preferences classes
passed 54; Core settings/evidence-retention policy classes passed 22. All had
zero skips. These are scoped groups, not a repository-wide qualification total.

Two final identical-driver/payload repetitions passed all five baseline and
seven retention rows each: passive exact-ID clocks/isolation without selected
lifecycle authority; preview/cancel without commit; Keep with newer audit
revision and fresh-window readback; subject/refresh review invalidation;
ordinary retention preserving activity/archive/delete clocks and unrelated
sessions; actual live/unresolved scratch-work hold observation; and synthetic
gate revocation of an uncommitted review without replay. All four final fixture
PIDs exited 0, had empty stderr, cleaned their exact scratch children, required
no force and were absent when the strict source-bound summary was sealed.

Earlier attempts remain distinct local evidence. Two header-discovery attempts
stopped before retention actions: the native expander exposes a ToggleButton
with TogglePattern and a duplicate TextBlock label, not ExpandCollapsePattern.
A read-only owned diagnostic established that shape and exited cleanly.
Another attempt truthfully refused private fixture setup after synthetic gate
reopen; setup now precedes revocation without weakening the hold. A repetition
hit a retired native descendant during observation and exited cleanly; the
final driver uses the established bounded observation-only retry, never an
automatic confirmation/action retry. The earlier different-driver complete
pass and every failed receipt remain unchanged, not relabelled as final bytes.

**Disposition: this bounded native retention-control workflow is machine-
verified without operator control input.** Window reopen is not a UI
process-restart proof. Native hold metadata is not due timer cleanup evidence;
actual due/Unknown/pending/copy holds and interrupted recovery belong to the
separate storage tests. Native already-due dates, general managed-copy disposal,
physical power-loss, forensic erasure, installed behavior, real privacy/audio
and human Narrator/readability/display acceptance remain unqualified by this
batch. Raw snapshots, receipts, payloads, TRX, immutable approval and sealed
summary remain local. Publication of this driver/documentation does not
broaden the recorded qualification or relabel historical receipts.

### 2026-10-10 Automated Native Exact-Grant Inspection and Revocation

After the unattended child batch completed, the operator-authorized parent
recorded a fixed scratch-only interactive window from 10:57:18.6723718 UTC to
11:57:18.6723718 UTC. The initial accepted runs applied the slightly earlier
11:57:18 UTC cutoff; neither preparation nor a retry renewed that authority.
The [exact-grant driver](../eng/Invoke-NativeUxExactGrants.ps1) uses the optional
fixture's 51 genuinely host-issued scratch grants, exact owned PID/HWND and
foreground-input checks, current displayed IDs/revisions and explicit review.
It never dispatches effects or reads the normal application profile.

Two identical-byte native runs passed all eight rows: bounded passive discovery
of all 51 IDs, genuine native overflow with endpoint readback, selection/refresh
review retirement, stale use-revision refusal, stale origin-lifecycle refusal,
synthetic privacy retirement and fresh-window review, single-record revoke with
revision-2 readback and subsequent consume denial, and unchanged 48 unrelated
grants including Perpetual with preserved recorded retention clocks.
The accepted source build-input digest was
`3ba358359482aa2b0daa4b05a6709ebef9e3d3df61d0769229422746dde28214`
over 1,550 files at base `f1478b5`; the driver digest was
`730b4527b02852416872db2d8f2e4e1de82eaed68bd125c7b1272719c58aec81`.
Both fixture processes exited 0, had empty stderr, cleaned their exact scratch
children, required no force and were absent when the source-bound summary sealed.

Fresh preparation passed 109 focused headless tests, the exact-grant no-launch
suite passed 20 checks, and the baseline admission suite passed 48 checks.
Supporting application presentation/failure tests passed 34 and Windows exact
store/window tests passed 20, with no skips. The root Release build had zero
warnings/errors. These groups overlap and are not a full-repository pass total.

After merging current main (#164) without rewriting the implementation history,
fresh preparation passed 110 tests and the application/store/window and no-launch
checks passed again. Two further identical-byte native runs passed all eight
exact-grant rows; the unchanged default fixture passed its five native baseline
rows. All three final processes exited 0, had empty stderr, cleaned their exact
scratch children, required no force and were absent at sealing. The final
build-input digest was
`9076a676d89a45bac6638ff599eaab41f26fe252b24d5d55e87fbee154ac93a9`
over 1,555 files at merge `baf7cf5`. These runs applied the original precise
11:57:18.6723718 UTC cutoff; no additional hour was started. The earlier
accepted source-bound summary remains unchanged rather than repinned.

At published HEAD `448a78f`, pull-request CI passed but feature-push Windows CI
failed in two headless fixture cases with an authoritative SQLite evidence-sink
`IOException`; the gap reporter did not preserve its lower-level cause.
The identical-head full Windows suite subsequently passed all 1,550 tests
locally, zero failures/skips. This is an unreproduced CI failure, not a confirmed
root-cause fix; the failed run remains historical evidence.

After merging current main (#165), fresh preparation passed 110 tests, the
application and SQLite/window checks passed 34 and 20 respectively, and the
root Release build again had zero warnings/errors. Updated build-input digest
`322f7313a7dd6f2953f612e479a4c5a41b1b059475091e85e9c348ede4b5e5f0`
over 1,569 files belongs to merge `03a1c84`. The original desktop cutoff had
expired before this preparation completed; no native trial was launched with
these bytes and no hour was renewed. The operator explicitly approved
publication with this fresh headless evidence and the earlier source-scoped
native proof kept separate. These later bytes do not inherit native acceptance.

Earlier preparation and failed native attempts remain unchanged local evidence.
Native trials exposed stock row names containing multiple correlation IDs, an
uninitialized stock list scroll provider, cross-thread native visibility
revalidation and a missing failure-report activity. Discovery now reads displayed
approval-ID text, uses the existing scroll-provider control, resolves current
native visibility/privacy on the UI thread and retains correlated failure
diagnostics. An enumerated HWND that retires before provider resolution is
recorded only after Windows confirms no remaining owner; failures for live
handles still abort and no consequential action is retried.

**Disposition: bounded native exact-grant mechanics are machine-verified without
operator control input.** Native revocation denies later consumes, not already-
running effect execution or rollback. Window reopen is not process-restart
persistence. Real privacy/ownership transitions, acoustic/Narrator/readability,
physical displays, installed behavior and full runtime/authorization
qualification remain open. Raw IDs, payloads, logs, TRX, approval and sealed
receipts remain local; publishing the implementation does not broaden acceptance.

### 2026-10-09 Bounded Native UX and Light-Text Continuation

**Disposition: scoped native observations recorded; light-mode secondary-text
readability approved by the operator. Full R05/R12/R14/A4 qualification remains
open.** The [sanitized receipt](../tests/Kora.NativeUxFixture/evidence/2026-10-09-native-ux.json)
records the actual uncommitted source base, separate tested payload identities,
completed observations, failed/inconclusive probes, cleanup and exclusions.
It contains no profile paths, account identity, screenshots, bulk logs or binaries.
Raw trial scripts, snapshots and receipts remain local research evidence.

The corrected scratch-only native fixture produced bounded observations for
Maintenance's disabled consent and keyboard cycles, complete repeated Appearance
keyboard cycles, exact question review/draft/submit and focus/status persistence,
stale session-generation and question-revision refusal, guide/detail lifetime,
native session Create/Rename and passive exact-ID history, and synthetic
privacy closure/reopening. Later stale-session probes were inconclusive, not
an every-trial repeatability pass. Fresh named-session history correctly had
four metadata-only Gap/Task records rather than an invented zero-row result;
retained version history exposed one exact question and one committed answer.
All native attempts ended with exit 0 and exact scratch-child cleanup.

The first question attempt was blocked by missing fixture workspace wiring,
then corrected in the test host without changing authority. Corrected mechanical
attempts passed 76 focused tests against matching source/payload identities.
The later light-text change used different application bytes, passed 29 directly
affected checks, and obtained a separate native Light selection and operator
approval for the darker descriptions/inactive navigation labels. Earlier native
observations are not relabeled as observations of the changed palette profile.
Historical receipts and incomplete aggregate metadata remain explicitly scoped.

Do not schedule these completed bounded observations as wholly unperformed,
or turn remaining full acceptance into an unrelated feature-development gate.
Remaining qualification includes complete all-surface/native repeatability,
screen-reader announcements, mixed speech targeting, rendered contrast/text
scale, physical DPI/multimonitor and real privacy/ownership/audio/installed
boundaries. No D-001/D-005/D-008/D-009/D-013 gate is closed by this receipt.
The native fixture and maintained regressions remain; no unique experiment
harness is superseded or retired. Temporary orchestration scaffolding was removed.

The [2026-10-09 unattended inference preflight](Local_Inference.md#unattended-provisioning-admission---2026-10-09)
records conditional host approval but rejects the delivered acquisition path
**before installation**: pinned tagged Inno source declares a broad
user-environment PATH update, desktop-app launch and image-name process-kill
hooks outside the authorised scope. Public release/model/licence metadata was
refreshed; no installer/weights were downloaded and no runtime started.
Non-exclusive observation therefore remains unperformed. A reviewed,
explicitly approved isolated acquisition/lifecycle path is the next decision;
exclusive residency/resource trials, blocked-egress capture and L3/L4/L6 remain
separate approvals, not implied by the conditional provisioning consent.

The [2026-10-08 inference preparation receipt](../experiments/r02-local-inference-proof/results/continuation-2026-10-08/preparation.json)
passes 55 current deterministic checks and six relative-path regressions.
Supported runtime/model locations and the loopback listener were absent.
No live trial or environmental change ran. Under the
[three-tier policy](Local_Inference.md#three-tier-admission-policy), LI01-LI07/
R02-L1-L6 block only affected local-inference exposure/advertising and RC
manifests including it; unrelated development remains independent.

The [bounded Windows-native rate slice](Implementation_Roadmap.md#r10-bounded-windows-provider-native-rate---2026-10-08)
adds deterministic owned-setter, future-synthesis retirement and native/typed/
ACTIVATED admission tests only. -10..10/default0 are Windows engine units, not
acoustic speed measurements or Kokoro support. It runs no capture/playback/
provider/account/network lab trial and closes no full R10/A0–A4, runtime or
release gate. Retain the R02 speech candidate/model/license, hashed synthetic
fixture/threshold receipts and remaining acoustic/packaged-host consumers:
this preference slice is not a maintained replacement for that experiment.

The [R05/R14 bounded native question delivery](Implementation_Roadmap.md#r05r14-bounded-native-shared-question---2026-10-07)
initially added automated trusted-UI state, exact review and production-store
query tests. The [2026-10-09 continuation](#2026-10-09-bounded-native-ux-and-light-text-continuation)
now records bounded native keyboard/focus/state observations and separate
light-text approval, not full desktop acceptance. Remaining screen-reader,
contrast/text-scale, DPI/multimonitor, mixed speech and live privacy/ownership
trials still require their own scoped preparation and approval.
It admits no effect proposal/dispatcher or missing source/containment/
deployment capability and closes none of D-001/D-005/D-008/D-009/D-013.

The [2026-10-07 automated UX/accessibility result](#2026-10-07-bounded-automated-ux-and-accessibility-result)
is completed evidence, not a future trial action. It records the distinction
between passing automated checks and still-unperformed native acceptance.

| Proof | Existing evidence / runnable checks | Deferred validation and preparation | Gates still open |
|---|---|---|---|
| R02 speech/hardware | [Merged synthetic proof and safe file-only reruns](../experiments/r02-speech-proof/README.md#safe-to-rerun-remotely-including-while-locked); [local-only boundary candidates rejected](#2026-10-08-local-only-speech-and-deployment-outcomes), not physical acceptance; [2026-10-08 stdlib-only privacy receipt inspector and fake-native regressions](../experiments/r02-speech-proof/README.md#2026-10-08-bounded-readiness-and-consent-checkpoint) | [SP01 exact PTT/privacy scope](../experiments/r02-speech-proof/README.md#sp01-exact-first-live-scope-blocked-pending-named-rig-and-fresh-consent) is blocked before capture consent: no named physical operator/room, already selected headset, installed visual-only build/ownership or appointment verified. The inspector reads copied typed metadata, not audio, and performs no live trial. Separately prepare [live acoustic acceptance](../experiments/r02-speech-proof/README.md#live--instrumented-acceptance-work-still-outstanding): scripts still lack a packaged wake front-end/AEC/last-acoustic-sample instrumentation; no assets/tools acquired or thresholds weakened. | D-002 and speech D-007 remain open for production wake/acoustic claims and RC manifests including them, not PTT/typed/settings or unrelated development; their own privacy/ownership acceptance remains required |
| R02 storage/key | [Historical encrypted storage proof](../experiments/r02-storage-proof/README.md#reproduce); DPAPI/ACL evidence retained; [bounded composed R04 receipt](Implementation_Roadmap.md#composed-milestone-validation-receipt) adds actual standard task/evidence, owned process interruption and no-replay recovery | [Approved standard-SQLite follow-up](#storage-admission-follow-up): broader migration/copy publication, retention/pruning/checkpoints and lifecycle/deletion. Mandatory encrypted-native/key/rekey gates superseded; installed loading remains R17 evidence. | D-009/R04 remain partial despite the first composed metadata-only query milestone. R12, managed backup/artifact/deletion, installed and power-loss acceptance remain open; no production transcript/content store enabled. |
| R02 local inference | [Safe deterministic reruns](../experiments/r02-local-inference-proof/README.md#safe-deterministic-reruns), plus the [bounded production-host trial](#2026-10-05-bounded-local-inference-result): clean one-approval Ollama/model setup, exact digest verification, real answers, loopback-only observation, visible cancellation and repeatable session-controlled teardown | [Prepare an interactive inference session](../experiments/r02-local-inference-proof/README.md#before-an-interactive-inference-session), then complete the remaining [LI01-LI07 deferred trials](../experiments/r02-local-inference-proof/README.md#deferred-inference-trials). Assign reference/isolation owners and agree budgets; separately approve exclusive model residency changes and whole-environment network blocking. Installer provisioning, CPU-floor measurements, server-cessation/races, attributable offline egress and repeated instrumented cancellation remain outstanding. | D-003 and inference D-007; [R02-L1-L6](Implementation_Roadmap.md#r02-local-inference-continuation), actual CPU-floor quality/context/resource budgets, installer distribution and independent offline success; R06/R07/R08/R10 and A2/R19 integration remain gated |
| R02 runtime/provider | Historical Node and [source-built RT1](../experiments/r02-dotnet-control-proof/README.md) unchanged. [RT2 bounded observations](../experiments/r02-runtime-lifecycle-proof/EVIDENCE.md) pass; all-path admission Blocked. [Separate released-profile MG1](../experiments/r02-dotnet-management-proof/README.md) passes: released SDK 1.0.16 with unchanged native bytes; 45 repeated RT1 regressions, 22 host and 16 actual runtime cases | [Runtime/provider follow-up](#runtimeprovider-follow-up): preserve distinct artifact identities, RT2 final source reproduction and MG1 initial local reproduction blocker without inferred byte equivalence. Qualify affected RT2 lifecycle paths for the refreshed released profile; separately approve account/usage trials. MG1 does not establish all-native observation, physical computation termination, durable authority or live eligibility. | D-001/D-004/D-010 remain open; RT2/PV1, R08 and model-assisted R13 production admission remain gated. MG1 envelope is scoped Pass; deterministic local management does not wait for hosted trials |
| R02 Windows containment | [Original owned-scratch proof](../experiments/r02-containment-proof/README.md#reproduce) retains 63/71 assertions and eight unproven network denials. [Independent W2 fixture](../experiments/r02-w2-dependency-proof/README.md) observes native ACL/child-policy denials, undeclared execution bypasses, separate embedded helper/entry fixed effect and Unknown/cancellation receipts; strict candidates rejected | Owner accepts best-effort transitive tracking/user responsibility for bundled/future scripts, with all manifest files in review tabs and exact declared hashes. Implement honest gap/invalidation tests, not universal undeclared-code denial. Native fixture loading blocked: supply an existing reviewed x64 C compiler or separately approve tool acquisition; no installation performed. Then independently prove protected runtime resolution, actual Windows control APIs, inherited object identity and complete helper/lifecycle/effect-race contracts. W1 attributable network and W3 installed ownership/ACL/alias trials retain separate consent/evidence. | D-013 technical admission and [W1-W4](Implementation_Roadmap.md#r02-windows-containment-follow-up); R11/R16/R17 worker/installed exposure remains gated; no broker selected |
| R02 distribution | [Proof #24](https://github.com/roryprimrose/Kora/pull/24) supplies historical receipts; [WiX MSI/custom Burn implementation](../installer/README.md) supplies binary packaging/CI and managed regressions, not a containment implementation dependency; [local-only backup/setup inspection](#2026-10-08-local-only-speech-and-deployment-outcomes) stopped before installation | NSIS acquisition/build and Linux recipe are retired; source/native checks remain. Unsigned beta/stable POC publication uses approved front-loaded/risk-based validation, not exhaustive manual installation of each MSI. Obtain separate scoped approval and disposable lab deployments for installed lifecycle/logon/all-users/upgrade, effective ACL/token/native loading and recovery. Silent related-bundle upgrades and numeric-beta upgrade ordering are unsupported. | D-005/R17 managed-source, installed/protection/resource acceptance remains open; no worker/catalogue/storage admission from package inspection, publication or process creation |
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

### 2026-10-08 W1 File-Only Preparation

The [W1 safe preparation and consent boundary](../experiments/r02-containment-proof/README.md#w1-safe-preparation-and-consent-boundary-2026-10-08)
adds a file-only runner mode, explicit independent live-effects consent and
evidence-local owned scratch/request binding. Two focused Release builds passed
without warnings/errors, 97/97 synthetic xUnit cases and five deterministic
self-tests passed, and four no-effect CLI/runner rejection smokes passed.
Fresh-worktree assets were missing; focused locked restores were performed
only after those build failures. No compiler/runtime installation, AppContainer
profile or credential creation, local-network trial, elevated query, protected
root change or disruptive control was performed.
Receipt-only child PIDs cannot authorize termination: unmatched/invalid reports
leave tracked shutdown unproven instead of risking an unrelated reused PID.

Windows x64 build 26300 is the observed developer OS, not a selected supported
reference build. Next resolve that reference machine/build and obtain exact
scoped consent, or choose developer-only diagnostic triage, or stop. The
prepared user-filter comparison retains the 24 fixed IPv4/TCP metadata queries,
positive-control and cleanup contracts; it enables no tracing or policy change.
The new scratch layout needs fresh live positive controls. Event schema,
blocking filter/layer and process/token attribution, IPv6/UDP/DNS and descendant
network coverage are still unqualified. Historical 63/71 OS assertions and
eight unproven denials are unchanged; W1/D-013 remain open for affected
worker/script/protected-control capabilities and release manifests including
them, not unrelated read-only/package/UI/session work.

### 2026-10-08 Local-Only Speech and Deployment Outcomes

**Disposition: historical negative or partial evidence, not new trials or
acceptance.** This consolidation records conclusions from separately approved
operator sessions. Exact candidate identities, synthetic fixture hashes and
raw receipts remain private; no prototype, model, audio, backup, profile data
or session runner is imported into the repository.

| Track | Recorded outcome | Limits and retained gates |
| --- | --- | --- |
| Speech boundary candidates | File-only synthetic PCM probes used two Windows voices, each with no pause and an 800 ms pause between wake and command audio. The SAPI splice candidate retained wake audio in all four cases, although it preserved the first 200 ms of command audio. The known-name CTC alignment candidate had confidence below its evaluation-only 0.5 threshold in all four cases; three retained wake audio, and the remaining case lost the command prefix. Both candidate receipts report `Qualified=false` and physical acceptance `NOT_RUN`. | Annotation was used only for evaluation. No microphone or playback was opened; the CTC probe decoded no free-form transcript. These exact candidates are rejected, not SAPI or CTC generally. The evaluation threshold is not a production selection. Synthetic confidence or file-processing speed does not establish a correct acoustic boundary, hardware floor, packaged-host latency or physical wake acceptance. D-002/D-007 remain open. |
| Existing-profile deployment preparation | The historical `v0.1.0-beta63` candidate's six assets were verified. A backup attempt blocked on an open file; a separately approved retry produced a private byte-verified local copy without changing original content. The verified setup UI opened, and the operator observed both required runtimes presented as installed. The later approved per-user installation attempt stopped at pre-apply checks when another Kora fixture became active; Install was not invoked and no Plan/Apply start was observed. | Backup verification proves that local copy's bytes, not off-device preservation, an atomic SQLite online snapshot, power-loss recovery or restore acceptance. Setup presentation does not prove runtime loading. Installation, repair, uninstall, logon, upgrade and installed privacy/ownership acceptance were not performed. The SDK/Git-equipped machine cannot establish runtime-only/no-SDK deployment. This old candidate does not qualify a later consolidated build; D-005/R17 remain open. |

Do not repeat either rejected boundary candidate unchanged or treat a blocked
deployment as a failed installation. Any continuation needs a bounded candidate
decision, fresh artifact/data checks, exclusive coordination of this profile
and separate approval for the actual effects. Previous consent and an earlier
backup are not a continuing lease or current-data equivalence. Retain failed
and partial receipts alongside later observations rather than replacing them.
All physical trials remain stopped; this documentation grants no permission
to capture, play audio, install, elevate, change settings or restart a trial.

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
**18/18 passed**. The pre-rebase full integration attempt was **841/842**, with
`Cancellation_after_commit_does_not_turn_a_durable_receipt_into_a_cancelled_result`
failing with `OperationCanceledException`; the earlier 836/836 pass remains
historical evidence, not a pass for this later attempt. The fresh hook fixture
initialized and exited 0 before native hook measurements; its exact scratch
child was absent. Native stale/link qualification was still pending at that
point. Work paused
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
surfaces or native stale/link behavior.

The separately approved stale/link/detail continuation then ran on
**2026-10-08 UTC** against an identity-verified rebased fixture, using only
valid scratch-domain advances and completed activity links. Its scoped native
results appear below; earlier native keyboard and registry observations were
not repeated or relabeled as rebased acceptance. Synthetic gate closure left
only the launcher; graceful stop exited 0 and removed the exact scratch child.

| Area | Completed observation | Not established by this result |
|---|---|---|
| Rebased native stale/link/detail hooks | An exact displayed idle session at Active generation 1 was advanced through the trusted hook to Done generation 2; stale Done visibly refused. After refresh, the displayed Done generation 2 was advanced to Active generation 3; stale resume visibly refused. Refresh independently retained the newer durable state without replay or selection retargeting. A reviewed synthetic question at revision 1 was advanced to Pending revision 2; the original window displayed `Revision 1: question-conflict`, retained its original prompt/review, disabled answer/draft/review/cancel controls and focused Close. Passive Sessions inspection independently showed the same question Pending revision 2 with no draft/answer. Selecting an actual retained explicit Link record and its Present segment opened the exact cited foreign-session span, with separate host provenance and no audit/approval authority. A missing target visibly failed and cleared the structured result. Restricting the query to the linked owner session removed the foreign target record; opening that explicit segment also visibly failed and cleared results without substituting content. Closing an actual immutable guide detail refused its captured generation-1 deferred render at closed generation 3; the viewer HWND disappeared and private content remained cleared. | Question Submit Invoke returned an ambiguous generic UIA error; the later settled window and independent durable inspection establish refusal, not clean transport. One transient UIA tree-read E_FAIL established nothing; a fresh settled read supplied the observation. Closed-generation render refusal does not qualify changed-source revisions, stale copy/clipboard behavior or all deferred paths. These hooks do not qualify newer upstream controls, spoken announcements, rendered contrast/text scale, physical DPI/multimonitor usability or actual OS privacy/ownership transitions. No copy, audio, execution or external egress was invoked. |
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

The experiment now has an [operator-approved automated runner](../experiments/r02-local-inference-proof/README.md#automated-operator-approved-qualification)
for paired streamed/buffered CPU-only trials, maximum timing comparisons,
repeated client cancellation/recovery and incremental receipts. A changed
runtime needs an explicit, separately labelled comparison; production pins
remain unchanged. Automation leaves human scoring, resource-budget approval,
exact context accounting, server cessation/actual timeout, full licence/staging
inventory, physical-floor support, independent offline proof and admitted-host
repeat open. Deterministic-only mode neither observes nor generates at the
endpoint; its native positive control observes only the self-test process.
Future live invocations also require an explicitly selected server PID, pinned
process creation identity and revalidated loopback listener. The observer follows
sampled descendants across native-runner names and discloses snapshot/identity
gaps; this is not complete lifecycle, per-request attribution or cessation proof
and does not repair earlier name-only resource receipts. Scoped residency/
exclusive-use consent remains required.
An optional two-request native-observer positive control uses an empty runtime,
records matched buffered/streamed signals and confirms owned-model cleanup; it
does not replace the repeated trials or close any qualification gate.

**Local-inference staged integration receipt - 2026-10-08:** after #97 and #98,
the experiment commits were rebased onto actual main
`5b43aa23264807db56d2d67100f2c0317cbaac4d` without conflicts. Range-diff retained
both patches unchanged, and the explicitly source-linked contracts did not
change across the base update. At rebased head
`98c1993a2877f6d2e1e153bd6c79f9acdd1f0f03`, the existing validator ran with
`-SkipObserve -NoRestore`: Release build passed with zero warnings/errors,
all 55 deterministic self-tests passed, and all 56 recorded source-input hashes
matched. The local `li-staged-integration-validation-001/validation.json`
receipt SHA-256 is
`4FA3AB4DA61160113886AF42680B163BFFB9E3130EE4D340114BDFC5C9B53085`.
No endpoint observation, real transport/generation, restore or runtime change
was performed. This documentation-only follow-up is not a compiled proof input.

The earlier `li-post-rebase-validation-002` receipt remains historical evidence
for the source reviewed at `8527855`: its `BaseRevisionAtRun` names preceding
commit `90eced3`, while its 56 input hashes matched the committed compatibility
fix. Neither receipt is relabelled as validation at another head; root CI does
not execute these experiment self-tests. Production Ollama remains pinned to
`0.35.1`; separately approved `0.40.0` comparison receipts are not production
acceptance. Their timing passes did not clear observed factual failures or
human-review requirements. Partial polling/native observer receipts do not
establish server cessation, and static tokenizer/template evidence does not
establish runtime parity or the complete context/overflow envelope. Physical
floor/resource budgets, full licence/staging inventory, independent offline
egress, integrated-host repeat, D-003/inference D-007 and A2/R19 remain open.
All upstream UX/privacy receipts and their gates remain unchanged.

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

#### 2026-10-08 File-Only Runtime Preparation

A new isolated worktree at main
`4cf8034fd3a73408aa3dfa6d24b0fc04c98b3f6c` ran
`eng\Prepare-RuntimeValidation.ps1` at 2026-10-08 12:07 UTC without artifact
inputs or host-test opt-in. **49/49 deterministic preparation contracts and
2/2 receipt-locale contracts passed**. Exit **2** correctly retained blocked
runtime/provider admission. Current sanitized receipts remain outside the
repository; historical Node, RT1, RT2 and MG1 fixture/evidence bytes were
verified unchanged. This is fresh file-only evidence, not fresh native trials.

Exact-profile inventory retained the distinct source-built package/assembly
pins `0b609b73c868099d64e7d5320b927d760d528756ab7c5db0b5692f5dccbf9b3f` /
`afb0715225f794d1b663ef72da1336b44b665e50b330087e1356380a5eac10d6`
and released pins
`c5518980b71d0ef0abd39ecdec7834898878222290c7fd1099a9040c8c5bf2ef` /
`6ed0b19fd2f9cf525074830784bb255245f15b8f08a3d6aa78fb116be5ba668b`.
Both profiles retain runtime 1.0.90/protocol 3 launcher/payload pins
`7021cf1f25eb6b75e64c05e8f805747c62dd420dc8760659660291809e92603a` /
`41ebb48367f96c984babde61afb68f22a85ab8fd9c037fdccf1778881c4bba05`.
Neither package, the native directory nor a qualified released fixture was
supplied to this continuation; artifact availability remains **Blocked**, not
proof that approved bytes do not exist elsewhere. No ambient artifact or
credential search, acquisition, source rebuild or byte-equivalence inference
was performed.

Installed tools report PowerShell 7.6.6 and .NET SDK 10.0.401 with runtime
10.0.12 present. A separate host-only `--no-restore` build failed NETSDK1004
because this fresh worktree lacks assets. The subsequent locked restore with
all configured feeds/fallback folders cleared and NuGet audit disabled failed
NU1101 for xUnit v3, AwesomeAssertions and the TRX extension. No authenticated
or alternate-feed fallback followed; the 22 host tests were **Not run**, not
failed assertions. No tooling installation, native runtime launch, privileged
trace, account operation, live provider call or policy change occurred.

| Remaining observation | Precise disposition after the safe continuation |
|---|---|
| Fresh source-built RT1/RT2 reproduction | Not run. Historical exact-byte Pass and subsequent generated-logger ordering/hash blockers stay separate. Any new source build must match both approved pins before native launch; no normalization, repinning or released substitution is approved. |
| Fresh released RT1/MG1/bounded RT2 | Not run. Previously approved 45 RT1, 22 host, 16 MG1 and 20 bounded RT2 results remain historical evidence. Supply reviewed exact inputs and locked dependencies plus scoped native-trial approval before a new account-free loopback run; do not invoke historical runners over retained receipts. |
| All-path RT2 | Blocked for both applicable profiles. Still require dedicated-host/operator approval, a reviewed loss-controlled collector, PID/creation-time-attributed startup/session/auth/error/shutdown coverage, native diagnostics/helpers, short-lived/detached descendants, DNS/UDP/non-IP destinations and transient/outside-scratch/registry/ADS/crash-dump writes. Writer/content attribution, positive/loss controls and actual native write/egress mediation or isolation are missing; metadata-only tracing does not prove prevention. |
| Execution PV1 / D-001 / R08 | Not run. After applicable RT2, select the provider/model/region, supported authentication, account tier and terms/organization entitlement; separately approve account use and a defensible enforceable spend ceiling. Observe expiry/revocation/denial/throttling/error paths, allowed destinations/content isolation, quotas/concurrency, no forwarded automatic retries and truthful cancellation. Never put credentials in chat or receipts. |
| Management PV1 / D-004 | Not run. Additionally prove two actual independent execution conversations plus management under the intended account, quota and cost envelope, complete byte/deadline/rolling-attempt limits, and quarantine while physical/billing termination remains Unknown. Byte bounds, SDK abort acknowledgement and billing alerts are not spending or termination proof. |
| Integrated lifecycle / D-010 / R04 / R13 | Still open. Fixture sessions and sampled termination do not prove admitted durable host identity/audit, leases, fair scheduling, outside-change revalidation, cancellation/unknown-effect races, recovery or installed composition. Deterministic local management and unrelated capabilities continue through their own gates; only affected runtime/model-assisted capabilities and an RC manifest including them remain blocked by these proofs. |

Maintained production code/tests have no Copilot SDK or equivalent MG1
envelope consumer to absorb these exact assertions today. The local inference
adapter is not an equivalent subject. No experiment was migrated or archived;
all existing evidence, reproduction scripts and design consumers remain intact.
The next safe intervention is reviewed local artifact/dependency inputs and
scoped approval for new account-free bounded released trials, or stop with the
affected capabilities disabled. Dedicated-host collection/prevention and live
provider/account/cost approval remain separate later decisions.

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

### 2026-10-07 Current-Profile A01 Subset and Measurement Preparation

The operator approved a bounded Debug consent-state trial on reference machine `REF-R03-01`,
Windows build `26300.9457`, using the current Windows profile rather than creating
another profile. The source base was
`d0a8e82ef34b82c4d888803083050c2e9dff43cd`; the Debug apphost SHA-256 was
`15433A49D281C2DD91F877D604533496BDC2FECAECFDDE6C0200D9E39FC7E234`, and the
desktop assembly SHA-256 was
`A0729C02FCB6E4D2E5AF107502B79BD071DA60D0A169363B774C7FC4051ADD78`.
This source/artifact receipt predates the instrumentation changes below.
The operator accepted the single-machine/current-profile scope; untested
profile, runtime, architecture and hardware combinations are not qualified by
that scope decision.

An initial launch was blocked by the shared `Coordination/unclean-owner` marker
from an earlier Release run. Read-only reconciliation found the earlier startup,
UI shutdown and a later OS boot; the operator confirmed no unresolved previous
work and separately approved removal of only that stale marker and relaunch.
An error notice incorrectly used Yes/No buttons; selecting Yes acknowledged the
notice but did not authorize takeover or restart. This is an ownership recovery
observation, not A06 failure-trial acceptance.

| A01 subset | Bounded receipt |
|---|---|
| No prior app consent | Debug consent file absent; operator observed microphone enumeration, closed/no-consent status and no Kora microphone-use indication. |
| Grant and disable/re-enable | `granted-v1` persisted; readiness armed without observed recording; disable closed readiness and re-enable restored readiness without PTT. |
| Ordinary saved-grant startup | Normal tray exit cleared the ownership marker; identical-build restart retained the grant and armed readiness without observed recording. |
| Withdrawal and restart | `declined-v1` persisted across normal exit/restart; operator observed closed/no-consent status and no automatic re-arming. |
| Fresh-state decline | After verified clean exit, only Debug `Preferences/voice-consent.txt` was reset; Continue without voice persisted `declined-v1` and retained closed input. |
| Cleanup | Final normal exit cleared the ownership marker; no Kora process remained visible; only Debug consent was reset to its original absent state. Logs, databases, other preferences and Release consent were not reset. |

No PTT, recording, Preview/playback or disruptive native privacy trial was
requested in this subset. UI/Windows indicator observations are not instrumented
capture-handle proof. The retained local Debug daily log contained zero
Error/Fatal records and 176 `MissingHostContext` diagnostic gaps. Preserve this
receipt separately from the 2026-10-05 held-PTT/output observations; it does not
close all of A01 or any A03/A04/A05/A06 gate.

The subsequently approved code-only preparation changes acknowledgement notices
to OK-only while keeping actual ownership questions default-No. Host activities
are established at desktop startup, native UI commands, deferred presentation
and shutdown boundaries; deferred work uses new traces linked to admitted
causes. Voice origin remains voice and cannot become UI approval through a
command wrapper. Missing-context reporting and typed audit admission remain
unchanged.

Measurement receipts use the process monotonic `Stopwatch` clock, observation
IDs, capture/output generations and typed structured diagnostics, without
retaining audio or transcript text:

- Native WTS/power/MMDevice notification timestamps are captured before state
  queries. Permission-poll observations are timestamped when the query completes;
  query start/end and topology/permission revisions are separate receipts.
- Capture release is timestamped only after native recorder disposal completes,
  not when Stop is requested or when recognition eventually disposes. Receipts
  include buffer-clear time/remaining bytes and release failure or pending-open
  disposition. Recorder presence is separate from recording admission: lock
  during an existing cleanup still measures the retiring recorder's release.
  Each measured lock release compares exactly with 500 ms; 500 ms passes and
  any greater value fails. Idle, previously released or unmeasured resources
  cannot become active-lock timing passes.
- Retired audio/transcript callbacks, transcript admission and the separate
  application dispatch gate have generation receipts. Admission is not an
  assertion that an OS command effect occurred.
- Output receipts distinguish native PlaybackStopped, output-buffer/resource
  clearing and observed Stop completion. None substitutes for the actual last
  acoustic output sample.

WTS and the existing power/MMDevice callbacks do not supply the underlying OS
event time: OS-event-to-notification delay is explicitly unavailable, not zero.
Likewise, permission-setting-to-poll-detection latency, successful native reroute
and last-output-sample timing require independent native/operator measurement.
Full A03/A04 acceptance remains open. A03's instrumented
observed-lock-to-recorder-release subset can be separately scoped without
misrepresenting those missing measurements. No application launch or native
trial is authorized by building/testing this instrumentation.

Code-only Debug validation completed with zero warnings/errors in the final
Core-test and Windows-test builds; the latter also rebuilt the desktop
application. Focused results:

| Coverage | Result |
|---|---|
| Portable privacy snapshots and exact release-boundary receipts | 23 passed, including exactly 500 ms, greater than 500 ms, already-closing native handles and unavailable measurements. |
| Application presentation/voice/diagnostic regressions | Original selected run: 732 passed, one new test assertion failed because untyped `default` meant null rather than `default(ActivitySpanId)`. After correction, all three request-boundary tests passed in the targeted rerun. This is not a single clean 733-case final invocation. |
| Windows observer/capture/output, ownership continuity and notice flags | Final selected run: 101 passed, including lock during existing recorder cleanup, delayed/faulted release, stale audio and failure callbacks retaining their original host cause, and default-No questions versus OK-only errors. |

TRX receipts from the original and targeted runs are retained separately outside
the repository. These tests use controlled boundaries; they do not establish
native capture cessation, zero diagnostic gaps in a real desktop run, or complete
R03 acceptance.

The source remains uncommitted in the isolated
`agents/interactive-validation-session-prep` worktree, whose HEAD/base remains
`d0a8e82ef34b82c4d888803083050c2e9dff43cd`. The parent CI/publication receipt does
not cover these changes. Selected rebuilt Debug binary SHA-256 fingerprints
(not a whole-package digest) are:

| Binary | SHA-256 |
|---|---|
| `Kora.exe` | `15433A49D281C2DD91F877D604533496BDC2FECAECFDDE6C0200D9E39FC7E234` |
| `Kora.dll` | `F711E474BA8419C74C603EE29EF367E5B51F63529C50B31865E8BCFC46BE6F73` |
| `Kora.Windows.dll` | `F7FA828CD7EB50CF435D086171DC467D31CB8FD0C16B80FC9AE27BA208122813` |
| `Kora.Core.dll` | `E9A0E2941E437C6AF66E825876C9E35F248C1195B448218B907815F83F9DB9B9` |
| `Kora.Application.dll` | `293030667939AB2A65FEE063D1516756D6724BC3D46E3C6B066CDE029194B8C0` |

No Kora application launch or live trial occurred during code-only preparation.
The final read-only check found no Kora process, shared owner marker or Debug
consent file; no further profile reset or ownership recovery was performed.
Fresh bounded operator approval remains required before another launch or trial.

#### Separately Approved Instrumented Startup/Decline Recheck

The operator subsequently approved a non-recording recheck on the same physical
machine/current profile. Preflight verified the five rebuilt fingerprints above,
a non-elevated launch, absent Debug consent, no Kora process and no shared owner
marker. Debug PID `33248` started at `2026-10-07T10:56:51.8384124Z` and was verified
responsive. The operator chose Continue without voice, observed closed input and
no Kora microphone-use indication; the exact persisted value was `declined-v1`.

Only daily-log records appended after the captured prelaunch byte offset were
counted. Through normal tray exit, the recheck produced 937 records, zero
`MissingHostContext` records, zero diagnostic-gap-template records and zero
Error/Fatal/Critical records. The earlier 176 gaps remain preserved as historical
evidence. This validates the observed startup/decline/shutdown subset, not every
presentation path or native dialog.

Normal exit cleared the shared marker without manual recovery. After verifying
no Kora process and no marker, the operator separately approved removal of only
this trial's Debug consent file to restore its original absent state. Logs,
databases, other preferences and Release consent were retained. Launch/completion
receipts are retained outside the repository.

No PTT, Preview/playback, lock/disconnect/suspend, device/OS-privacy change, setup
install or automatic ownership recovery was authorized or performed. These
observations remain partial A01 evidence; they do not establish native
capture-handle cessation, execute the 500 ms A03 target, complete A03/A04/A05/A06,
or qualify other machine/profile/runtime combinations. The operator requested
recording this result and stopping; no further trial is authorized.

#### Automated Audio Baseline Blocked Before Capture

The operator subsequently approved two PTT activations of at most five seconds
using only "Kora, what can you do?", plus one headphone Preview/Stop check.
They confirmed physical presence, headset use at a comfortable existing volume
and consent from everyone within microphone range. Read-only enumeration found
the HyperX Cloud Alpha Wireless microphone/headphones were the unmuted
Multimedia defaults. No Windows route, volume or privacy change, lock, install
or process termination was authorized.

After fingerprint/ownership checks, Debug PID `33508` started at
`2026-10-07T12:01:15.9941022Z`. The operator verified HyperX routing, granted app
consent (`granted-v1`) and reported PTT-ready input still closed. The automated
pre-activation check found two evidence-ingestion gaps. The trial stopped before
PTT/Preview: the completed append-only byte range contains 409 records, two
`MissingHostContext` gaps, zero Error/Fatal/Critical records, zero voice
activations and zero speech starts. This is **Blocked**, not audio acceptance.

The source diagnostics identify microphone-override save and clear operations
from Settings bindings. The ingestion sink label `capture` refers to evidence
capture, not microphone recording. Normal tray exit cleared ownership; after
verification, the separately approved cleanup removed only this trial's Debug
consent file and restored its original absent state.

The operator approved code-only repair and focused tests. The existing shared
preference-write boundary now owns a storage activity through the write and
audit outcomes, preserving an existing request/origin rather than relabeling
voice as UI. Five new regression cases cover microphone/output save and clear,
write failures and original voice lineage. All 701 selected MainViewModel
cases passed; Application-test and desktop Debug builds completed with zero
warnings/errors. No live recheck of this repair has occurred.

The rebuilt `Kora.Application.dll` SHA-256 is now
`8F94629463B356D710F175B4DBC191204AC6D5DAED52CD61638D6FE72CCE2B1F`;
the other four fingerprints above are unchanged. The earlier fingerprint
table and native receipts describe the pre-binding-fix artifact.

The local receipt collector decodes numeric gap reasons using the Core enum,
not rendered-message matching. Completed runs use recorded byte-range endpoints
so later runs cannot contaminate historical results. Controls reproduce the
earlier 937-record/zero-gap subset and this 409-record/two-gap blocked attempt.
Collector, launch/completion/analysis receipts and TRX remain outside the
repository; no raw recording was collected. PTT/playback, native lock timing
and the broader R03 gates remain unproved and require fresh scoped approval.

#### Repaired Binding Recheck and Physical PTT Boundary

With fresh participant/endpoint approval, the binding-fixed artifact launched
as Debug PID `47292` at `2026-10-07T12:34:26.8273013Z`. The operator selected the
HyperX microphone and returned to System default. No consent decision was yet
saved; disabled PTT was correctly retained. After explicit Enable voice (save
consent) and any required Enable listening, `granted-v1` persisted. The
pre-activation receipt had 286 records, zero gaps/errors and no capture/output
starts. This natively exercises the repaired microphone save/clear path without
extending the earlier zero-gap receipt to all UI paths.

The first approved mouse-held activation opened once. The operator reported
closed input after release but no recognized command/help response. No
transcript admission or dispatch was logged. A new context gap attached to
`VoiceActivationStarted`: Settings' physical PTT handlers called the view model
directly, bypassing the traced command route. The sequence stopped before the
second activation or Preview. The final byte-bounded receipt contains 415
records, one `MissingHostContext` gap, one voice activation, no transcript or
dispatch admissions, no speech starts and no Error/Fatal/Critical records.
Recognition is not a pass, and operator-observed closure is not independently
measured native handle-release evidence.

Normal exit cleared ownership. Separately approved cleanup verified no Kora
process/marker and removed only this trial's Debug consent. The operator then
approved code-only physical-boundary repair: mouse/key begin/end, capture loss,
focus loss and settings deactivation/closure now use the existing scoped,
exception-handled Begin/End commands. Release remains a distinct command and
can cancel pending native open; pointer release does not wait on asynchronous
capture disposal.

Two new command-route regressions cover live native begin/end context and
release during pending begin. All 703 selected MainViewModel cases passed;
Application-test and desktop Debug builds completed with zero warnings/errors.
The new `Kora.dll` SHA-256 is
`59D3C7F539244CB4122F3160CBE85DECEF5297A674CBFB49EC96104959141C89`;
`Kora.Application.dll` retains the binding-fixed fingerprint above, and the
other three fingerprints are unchanged. No live test of this physical-boundary
repair has occurred. The absence of a recognized command has not been
diagnosed as a separate native ASR defect or relabeled as a pass.

#### 2026-10-08 Physical PTT Recheck and Active Preview Stop

With fresh approval for the same two bounded PTT activations and one headphone
Preview/Stop, Debug PID `47896` started at
`2026-10-07T21:36:45.0367042Z` (2026-10-08 local date). The operator reaffirmed
physical presence, headset use, comfortable existing volume and participant
consent on `REF-R03-01`, Windows build `26300.9457`, using the current
profile. The base remained `d0a8e82ef34b82c4d888803083050c2e9dff43cd`, with
uncommitted source and the five fingerprints identified above, including the
physical-PTT-fixed desktop and binding-fixed Application assemblies. This is
not a parent-CI-qualified artifact or supported-build matrix qualification.

Read-only preflight identified the unmuted Multimedia defaults:

- Capture: Microphone (HyperX Cloud Alpha Wireless); exact endpoint identity is
  retained only in the private local receipt.
- Output: Headphones (HyperX Cloud Alpha Wireless); exact endpoint identity is
  retained only in the private local receipt.

The operator saved `granted-v1`; readiness contained 296 records, zero
gaps/errors and no audio starts. Two activations were logged eight seconds
apart. The operator explained that they began speaking before the first slow
microphone open and missed the initial "Kora" prefix. This is an operator
explanation, not measured open latency or a proved recognition diagnosis.
Generation 2 had exactly one transcript admission and one dispatch admission,
with no rejected dispatch; the operator observed the help response and closed
input after release. The first activation is not a successful recognition
receipt, and both activations consumed the authorized PTT budget.

The operator clarified that their earlier report of hearing Preview referred
to the existing spoken response rather than a separate Preview. They then
clicked Preview and Stop and reported speech starting and stopping as expected.
The completed receipts contain two speech starts (help and Preview), native
PlaybackStopped and resource/buffer-clearing receipts for each, and an active
Stop with `HadOutput=true`. For that Stop, the monotonic clock frequency was
10,000,000 ticks/second:

| Active Preview Stop measurement | Receipt |
|---|---|
| Stop request | `840786420388` ticks |
| Native PlaybackStopped | `840787375454` ticks; 95.5066 ms after request |
| Observed Stop completion | `840789592611` ticks; 317.2223 ms after request |
| Output resources released | `840790085469` ticks; 366.5081 ms after request; 141824 audio-buffer bytes cleared |

Resource-release observation followed Stop completion; neither observation
measures the last acoustic sample. These output timings do not exercise the
500 ms observed-lock-to-recorder-release requirement.

Normal tray exit was followed by verification of no Kora process and no shared
owner marker. Only the exact trial Debug `granted-v1` consent file was removed,
restoring its original absent state; cleanup was verified at
`2026-10-07T21:52:01.4798449Z`. Logs, databases, other preferences and Release
consent were retained. No third activation, extra Preview, lock, disconnect,
suspend, OS route/volume/privacy change, install or process termination was
performed.

The immutable completed receipt spans bytes 0 through 1919988 (exclusive) of
the newly created `kora-20261008.log`; older logs are capped at their prelaunch
lengths. The completed range contains 1026 records, zero `MissingHostContext`
or diagnostic-ingestion gaps and zero Error/Fatal/Critical records. Launch,
completed, cleanup and analysis receipts remain outside the repository. The
earlier 409-record/two-gap and 415-record/one-gap attempts remain blocked
historical evidence, not retroactive passes.

This establishes the observed physical PTT command-context path, one help
admission/dispatch and operator-guided active Preview Stop on this setup. It
does not establish capture sample/queue bounds, measured microphone-open
latency, native handle-release timing on PTT release, last acoustic output,
full A02/A07 acceptance or complete R03. A03's exact per-reference-trial lock
target is still unrun; OS-notification delay remains unavailable. A04 and the
advanced A05/A06 ownership trials remain separately gated. Existing A01 and
ownership receipts and the R04/R05/R09/R15 dependency gates remain unchanged.
Any further native trial requires fresh scope-specific approval.

#### Separately Approved A03 Pilot Blocked Before Recording

The operator approved one idle manual lock/unlock control, followed by one
silent physical PTT hold of at most five seconds total with immediate Win+L
once recording, on the same current-profile HyperX setup. They affirmed normal
unlock ability and participant consent. Playback, automatic retries, installs,
OS-setting changes and forced termination were excluded.

Focused no-build prerequisites passed: 13 Core exact-boundary receipt cases
and 79 combined Windows observation/activated-capture cases. The SDK help query
initially rejected misplaced `--help`; corrected SDK-level help succeeded.
PowerShell could not load the app's System.Speech assembly because of an
assembly-identity conflict; read-only file/registry inventory instead confirmed
the exact speech library and x64 recognizer metadata. Neither tooling issue
was treated as a native Kora pass or repaired through dependency changes.

Preflight confirmed non-elevated session 1, no Kora process/owner marker/Debug
consent, the unchanged five selected binary fingerprints and the actual base
`d0a8e82ef34b82c4d888803083050c2e9dff43cd`. The reference machine has an AMD
RYZEN AI MAX 385, 8 cores/16 logical processors, 25530408960 bytes physical
memory and the existing High performance power profile. Windows remains
`26300.9457`/`26H2`; this does not qualify a supported servicing-build matrix.
The speech assembly is `10.0.0.12`, SHA-256
`94ADAA46084BEF72461C65E38DEC2AFF3C7AFD48ABC1A27976AE87B2B9FFD37F`;
the x64 recognizer token is `MS-1033-80-DESK`, version `8.0`, English - US.

Debug PID `42088` started at `2026-10-07T22:24:50.5163985Z`. The operator granted
consent and confirmed ready-but-closed input and HyperX routing. The process
identity guard initially compared a formatted string to PowerShell's parsed
DateTime; exact UTC tick comparison confirmed matching creation time/path and
a responsive process. No identity check was bypassed.

The collected range contains an idle Locked observation at local 09:30:39 and
Unlocked at 09:32:49, with two output-topology observations while locked. No
capture or output started. Each locked capture receipt has `HadRecorder=false`,
zero remaining buffer bytes and null release duration/lock-target result. These
are native observation receipts, not active-recorder timing passes or evidence
that an output stream stopped under lock.

A new `MissingHostContext` gap occurred earlier, at
`2026-10-08T09:27:58.6154217+11:00`, for `Kora.MainWindow`'s
"Hiding the main window after its transition" diagnostic. The window-action
callback emits that diagnostic without its own host boundary. The pilot stopped
before PTT; no native capture-release failure or 500 ms result was measured.
The new gap remains a blocker rather than being suppressed or relabeled as
successful native acceptance. No production repair was authorized in this run.

The outside-repository collector now joins existing typed privacy observations
and release receipts by observation ID, preserving their monotonic clocks and
using the Core receipt's authoritative exact-boundary rule. Four explicitly
synthetic schema controls passed: exactly 500 ms, greater than 500 ms, already
released and idle. Synthetic results are not native evidence.

Normal tray exit cleared ownership. After verification, only the exact trial
Debug `granted-v1` file was removed, restoring absent consent at
`2026-10-07T22:48:51.8913708Z`. The immutable completed log range is
`kora-20261008.log` bytes 1919988 through 4814536 (exclusive): 1535 records, one
context/ingestion gap, zero Error/Fatal/Critical records, zero activations and
zero speech starts. Other logs/data/preferences and Release consent were
retained; launch/completion/analysis and TRX receipts remain outside the
repository.

The pilot is **Blocked**, not an active A03 timing pass. The every-reference-trial
500 ms target, OS notification delay, output-under-lock evidence and complete
A03/A04/A05/A06 acceptance remain open. The earlier gap-free PTT/Preview receipt
is preserved independently. Repair, further tests or another live trial require
separate scope-specific approval.

#### Code-Only Window-Action Context Repair

After the blocked A03 pilot, the operator separately approved repair and
focused build/tests only, without a launch or native retry. The MainWindow
window-action event now owns a desktop presentation activity throughout its
asynchronous transition. It preserves a live request/session/origin, including
ActivatedVoice, or establishes a host-system root for an uncorrelated framework
callback. Cancellation and failures have explicit terminal outcomes; failures
are logged and routed to the existing user-facing recovery state within the
live activity. Further presence-show requests are refused after a window-action
failure to prevent recursive reopening; native tray exit remains available.
Existing privacy show guards, animation cancellation and shutdown routes remain.
Shared command-runner semantics and evidence-gap reporting are unchanged.

Four new focused regressions exercise this actual event-routing boundary without
creating a native window: context through await and termination, original voice
identity/parentage and caller isolation, cancellation, and failure notification
with the original live context. The first build caught a missing logger-enabled
guard in the test fixture; it was corrected without analyzer suppression.
The final Windows-test/desktop Debug build has zero warnings/errors. The
combined new boundary, presence-input and observer/capture run passed all 100
cases. TRX is retained outside the repository.

The rebuilt `Kora.dll` SHA-256 is
`407125FA5486D2AA24A1CBE3C9DDC880AD989EAE27C39C0C20B2E8CF660FB213`;
the other four selected fingerprints remain unchanged. HEAD/base is still
`d0a8e82ef34b82c4d888803083050c2e9dff43cd`, and source remains uncommitted.
Verification at `2026-10-07T23:02:24.9816408Z` found no Kora process, owner marker
or Debug consent file. No live recheck occurred during this repair. The 1535-record
blocked receipt remains intact; native gap elimination and active A03 timing
remain unproved and require fresh bounded approval.

#### 2026-10-08 Idle Lock/Unlock Retry and Code-Only Status Repair

The operator separately approved the same bounded A03 pilot on the current
machine/profile and HyperX endpoints. PID `44452` started at
`2026-10-08T03:45:35.7105912Z`, with the window-action-fixed desktop fingerprint
above. Readiness and the operator's manual idle lock/unlock produced native
Unlocked, Locked, then Unlocked observations with no capture or playback.
The operator reported that the closed-microphone status still said Windows was
Locked after unlock. The observer had delivered Unlocked; the view model retained
the earlier present-tense Locked pause reason. The explicit recovery hold was
intentional, but that wording was misleading. The active PTT lock stage was not
performed.

The operator approved ending the pilot normally, restoring only trial Debug
consent, and a code-only repair with focused tests; no automatic native retry.
After normal tray exit, the byte-bounded final receipt contains 739 records,
zero context/diagnostic gaps, zero errors, zero voice activations and zero speech
starts. The lock receipt has no recorder or pending open, zero buffered bytes
after clearing, and null recorder-release time/target result. This is an idle
control, not a pass for capture release within 500 ms in every reference trial.
OS-event-to-notification delay remains unavailable. No process or owner marker
remained, and only the exact trial Debug `granted-v1` was removed to restore
original absent consent. Other preferences, Release consent, logs and databases
were retained. Launch, completed byte-range and final receipts are retained
outside the repository; the earlier 1535-record blocked pilot and 1026-record
audio-subset receipt remain unchanged.

The repair tracks the session-related pause state separately from the sticky
recovery reason. An Unlocked observation updates its status without clearing the
privacy hold, enabling readiness, reopening capture or revealing presentation.
Non-session recovery reasons clear that presentation state, so permission and
later failure blockers are not replaced by unlock wording. Existing native
output invalidation and topology handling still precede the status dispatch.
Explicit native recovery and Enable listening remain necessary; enabling
readiness itself does not open capture.

Ten new deterministic cases cover recovery from all five negative/unknown
session states, permission blockers on unlock, a later enumeration failure,
explicit native recovery, and repeated transitions. The first selected run
passed 711 of 713 cases: it caught an early status dispatch preceding output
invalidation and a new test omitting the required native recovery action. Both
were corrected without weakening the recovery gate. The final desktop and
Application-test Debug builds passed with zero warnings/errors, and all 713
MainViewModel cases passed in the separate final invocation. Both TRX results
are retained. Builds used `--no-restore`; no package operation or live launch
occurred during repair.

The rebuilt desktop SHA-256 is
`94196131A29D607FADBBCE7CB41432092606F2475A2F4179413E41C695DB60C8`;
the Application assembly SHA-256 is
`B6110BA443F869B7DADCFB87CD7FD330B11C6F5E18045F67A304899CD2ED5CA8`.
These selected fingerprints do not supersede the completed native trial's
artifact receipt or qualify a package. Source remains uncommitted at base
`d0a8e82ef34b82c4d888803083050c2e9dff43cd`. At
`2026-10-08T04:01:28.7475498Z`, no Kora process, owner marker or Debug consent
remained. The repaired status is deterministically verified but not natively
rechecked. A03 active timing and dependent acceptance gates remain open;
execution is blocked pending fresh bounded approval.

#### 2026-10-08 Status Recheck and Revised Normal-Unlock Policy

Before the policy revision, the operator approved one fresh non-recording
status recheck on the same machine/profile and HyperX routes. PID `39188` started
at `2026-10-08T04:12:35.7491972Z` using the status-fixed fingerprints above.
The operator confirmed that one manual idle lock/unlock displayed Unlocked
while the microphone stayed closed and explicit Enable listening remained
required under the then-current policy. The completed byte-bounded receipt has
242 records, zero context/diagnostic gaps or errors, zero activations and zero
speech starts. Native observations are Unlocked, Locked, Unlocked; no recorder
or pending open existed, buffers were empty, and lock-release timing remains
null. Normal exit cleared ownership; only the exact trial Debug grant was
removed, restoring original absent consent at `2026-10-08T04:15:08.6248202Z`.
The launch/completed/final receipts remain separate from the earlier 739-record
blocked idle control and other historical native evidence.

The operator subsequently approved a code-only normal-lock/unlock policy change
and deterministic tests, then clarified the same prior-enabled-mode rule for
future qualified always-on detection without enabling ambient capture in this
build. The [canonical microphone matrix](Security_Data_Flows.md#microphone-consent-and-enablement-matrix)
and its dependent lifecycle, settings, acceptance, skill and decision documents
now distinguish normal unlock restoration from explicit recovery after other
privacy failures. The 242-record native recheck proves the earlier status-only
repair, not the revised automatic-restoration behavior.

Normal lock still immediately invalidates activation/audio and hides private
presentation. Unlock restoration waits for owned capture/output closure, checks
the current consent, selected microphone, Windows permission/session, call
policy, exclusive-owner gate and capture quiescence, and commits only if the
recovery revision is still current. It restores PTT readiness without opening
capture, showing a private window, replaying an old transcript or resuming an
interrupted activation. Manual disablement, withdrawal, input/privacy changes,
closure/capture failure, ownership loss and other negative/unknown sessions
prevent automatic restoration. Output-only topology observations cannot unmute
or choose another input. Genuine capture failures remain distinct from typed
privacy retirement, including when the native recognition callback precedes
the view-model session callback. Deferred recovery preserves host request
identity with a linked activity rather than a retired parent.
The consumer-specific voice ownership binding defaults closed and uses the
desktop bridge's capability-admission gate, including handoff quiescence and
failed recovery, rather than treating desktop/call readiness as capture admission.

The initial build stopped on five analyzer errors: explicit enum-zero naming,
the required `System.Threading.Lock`, and awaiting an already-owned transition.
These were corrected with typed comparisons and a single narrowly justified
asynchronous-wait suppression. Final affected Debug builds passed with zero
warnings/errors; selected tests passed 24 Core, 739 Application and 79 Windows
cases. This includes 26 additional Application cases for restoration, failure
and closure races, manual intent, native callback ordering, locked startup,
endpoint disappearance and linked recovery context. TRX receipts are retained
outside the repository. No restore, package operation or live launch occurred
during this policy repair.

Selected rebuilt SHA-256 fingerprints (not a package digest) are:

| Binary | SHA-256 |
|---|---|
| `Kora.dll` | `EB7DDCE4AE989EAADDCB58F893B2E45FEC840F94F28E2BB35458B0B86176157B` |
| `Kora.Core.dll` | `1D37A55A761CFFB9E4D0D5ECB220D61D63738EF2BBDAFFE234964FC2548B0932` |
| `Kora.Application.dll` | `7E48BA14E2F5BD03EA19594EFCF83EF2DE3B4E40C5A8239E632D6018EFE62F05` |
| `Kora.Windows.dll` | `149441114C496BBD11D2CB62E7645EF7E3B07A913E585E386A82B216516D116F` |

The apphost fingerprint is unchanged. Source remains uncommitted at base
`d0a8e82ef34b82c4d888803083050c2e9dff43cd`; at
`2026-10-08T04:46:06.2332877Z`, no Kora process, owner marker or Debug consent
remained. New native restoration trials require fresh bounded approval.
Production always-on/wake remains gated by separate runtime/acoustic acceptance;
documenting its restoration rule does not implement or qualify it. The exact
every-reference-trial 500 ms A03 release target, OS notification delay and full
A01-A07/dependent capability acceptance remain open.

##### Native Idle Automatic-Restoration Recheck

The operator then separately approved one non-recording recheck of the revised
policy on the current machine/profile and unchanged HyperX routes. PID `47280`
started at `2026-10-08T04:50:19.1377321Z` with the final fingerprints above.
After granting consent and enabling PTT readiness, the operator performed one
manual idle lock/unlock and confirmed automatic return to Push-to-talk ready,
microphone closed and Disable listening, without using Enable listening again.
No PTT, recording, playback or ambient listener was approved or used.

The completed byte-bounded log receipt contains 304 records, zero context/
diagnostic gaps or errors, zero activations and zero speech starts. Native state
was Unlocked, Locked, Unlocked. The frozen range `6659016`-`7232063` contains
exactly one structured `Operation` value, "Previously enabled voice readiness
was restored after Windows unlock", at `2026-10-08T15:52:18.8289668+11:00`.
This agrees with the operator's observation, rather than inferring enablement
from absence of an error or a later manual recovery.

No recorder/pending open existed at lock; cleared-buffer bytes were zero and
the recorder-release/500 ms result remains null. This is a successful scoped
native idle restoration control, not active capture-release timing, proof of
always-on detection, repeated-transition/matrix acceptance or complete R03.
The normal exit cleared ownership and only the exact Debug grant was removed,
restoring original absent consent at `2026-10-08T04:54:32.0150714Z`. Other
preferences, Release consent, logs and databases were retained. Launch,
readiness, completed/final and structured restoration receipts remain outside
the repository alongside, not instead of, prior evidence. No automatic retry
or further trial was conducted.

##### Native Retiring-Recorder Lock Pilot and Exit Boundary Repair (2026-10-08)

After separate bounded approval, operator `OP-R03-01` performed one silent
physical mouse-held PTT activation on the same current machine/profile and
HyperX routes, manually locked immediately after visible recording, released
within five seconds total including opening, and unlocked normally. PID `39860`
started at `2026-10-08T05:30:54.7232429Z`, at the same uncommitted base revision
and selected binary fingerprints recorded above. No speech, playback, ambient
capture, automatic retry or OS preference changes were approved or used.

The final frozen byte range `7232063`-`8008378` contains 412 records, zero
context/diagnostic gaps or errors, one activation, zero transcript/dispatch
admissions and zero speech starts. For generation 1, lock observation
`01d97a6d-c2ad-4451-8775-369988c5911b` was stamped at `1119535834270` ticks and
confirmed native recorder release at `1119536968912`, with frequency `10000000`.
The individual observed-lock-to-release duration was **113.4642 ms**, passing
the exact 500 ms target for this trial. Capture admission was already closed
(`WasRecording=false`), but the retiring native recorder remained owned
(`HadRecorder=true`, `HadPendingOpen=false`) at observation. This is an eligible
retiring-recorder measurement, not an idle pass or proof of continuously
admitted recording until the WTS notification. Buffered bytes after clearing
were zero; OS event-to-notification delay and last acoustic sample remain
unmeasured. Full A03/every-reference-trial and dependent acceptance remain open.

The operator confirmed the microphone closed after unlock and exited normally.
No Kora process or ownership marker remained, and only the exact trial Debug
grant was removed to restore original absent consent, verified at
`2026-10-08T05:37:15.0855706Z`. Other preferences, Release consent, logs and
databases were retained. Launch, initial/ready, observed, completed and final
receipts remain outside the repository alongside all prior evidence.

During Exit, the operator reported that the native context menu and tray stayed
visible for a few seconds. This is an operator observation, not measured menu
dismissal latency. A separately approved code-only trace found Exit starting
inline in the native menu callback, unlike the existing delayed window actions.
Exit now uses the same 50 ms Background-priority Avalonia dispatch boundary so
the callback can unwind before shutdown starts. UI mutations and shutdown stay
on the UI thread; awaited capture/output/clipboard closure, exception reporting
and final safe ownership release remain unchanged. Deferred actions retain
host identity through a fresh linked activity. The frozen shutdown logs span
approximately six seconds across request, closure and disposal phases; that
span does not establish the cause or duration of menu retention. No native
relaunch was authorized during this repair, and visible responsiveness remains
unverified pending a separately approved non-recording recheck.

The affected Debug desktop/Windows test graph built with zero warnings/errors.
Focused deterministic validation passed 9 Windows boundary/window/capture
shutdown tests and 8 Application Exit/audio/clipboard/model lifecycle tests.
These include three new native-menu scheduling, thread-preservation, linked
context and failure-termination cases. Fresh TRX results are retained outside
the repository; they do not measure a real native menu. The rebuilt `Kora.dll`
SHA-256 is `D4C69BAA9FD8BF97CE39F025EF7C036E43AC12B6F917C56A9D2D595E497D0722`;
the other selected fingerprints above are unchanged. The Application test
dependency matches the rebuilt Application binary. At
`2026-10-08T05:47:23.5499959Z`, no Kora process, owner marker or Debug consent
remained. Changes are uncommitted at the same recorded base.

##### Native Non-Recording Exit Recheck (2026-10-08)

The operator separately approved one launch/Exit recheck on the same current
machine/profile, with consent declined and no listening, PTT, playback, lock,
device changes, forced termination or automatic retry. PID `13128` started at
`2026-10-08T05:48:58.3391477Z` with the repaired desktop fingerprint above.
The initial 36-record and ready 245-record receipts were clean, and the exact
Debug decline was verified as `declined-v1`. The operator confirmed Kora
responsive with the microphone closed before selecting tray Exit once.

The operator reported: the menu closed immediately; Settings remained visible
for a few seconds, and the tray remained visible for a few seconds after
Settings closed. This confirms the scoped native-menu dismissal improvement,
not zero-latency shutdown or resolution of the later window/tray delay.
Those durations are operator observations, not instrumented measurements.
The final frozen receipt contains 378 records, zero context/diagnostic gaps or
errors, zero activations, zero transcript/dispatch admissions and zero speech
starts. No lock/capture-release trial occurred, and this does not expand the
earlier A03 timing proof.

Normal process exit and marker absence were verified. Only the exact trial
Debug decline was removed to restore original absent consent at
`2026-10-08T05:54:35.2609191Z`; all other preferences, Release consent, logs and
databases were retained. Launch, initial/ready and completed/final receipts
remain outside the repository. Later shutdown presentation responsiveness
remains a separate open finding; no further relaunch was performed.

##### Code-Only Shutdown Evidence Hot-Path Repair (2026-10-08)

The operator separately approved investigating the remaining Settings/tray
delay, then explicitly approved extending the code-only repair to the
evidence-validation hot path without buffering, deleting evidence, weakening
checks or relaunching. The frozen PID `13128` diagnostic phases were Exit
request at `05:53:22.3075322Z`, desktop shutdown at `05:53:25.8272728Z`,
Settings close at `05:53:27.4224716Z`, tray disposal at
`05:53:29.3277694Z` and TTS disposal at `05:53:32.6836776Z`.
These are structured log timestamps, not native UI-dismissal measurements.
Repeated completed no-op presentation activities had approximately 0.01 ms
operation durations but publications often spaced 200-270 ms apart.

The trace found synchronous required diagnostic/activity fan-out reopening
and validating the complete SQLite evidence history for every commit,
including a separate links query for each activity. The repair preserves
synchronous durable delivery and all live-authority, schema, private
storage/journal, integrity, stored-column, retention, sequence and link checks.
A bounded cache reuses only validated envelope projections/activity metadata,
keyed by table and SHA-256 of the exact serialized payload; changed payloads
are decoded again and current persisted projections/dates/links are always
checked. The cache retains no raw diagnostic properties/scopes, is capped at
16,384 entries and 32 MiB cumulative encoded-payload accounting, and
saturation revalidates misses. Activity links now use one ordered scan rather
than one SQL query per activity. Read-only validation remains uncached.
No commit is deferred, no failed write becomes success and no ownership
release or capture/privacy boundary was moved ahead of verified cleanup.

The affected Debug graph built with zero warnings/errors. Validation passed
83 Windows storage/integrity/query/interruption/presentation tests and 31
Application evidence/context/failure-fan-out tests, with no failures/skips.
The deterministic parsing-work regression proves that 130 unchanged envelopes
are decoded once; repeated validation and the next append opening add no
decodes, and the following validation decodes only its one new envelope.
Both cache limits and warm-cache payload/projection/retention/link tampering
were tested, including preservation of rejected database bytes. This proves
the optimized work shape, not real UI latency or complete R03 acceptance.

Selected rebuilt SHA-256 fingerprints (not a package digest) are now
`ADAF32AD3A92EA7BF628C948FE9A22774CF757544B972D7D56C1111B2150909E` for
`Kora.dll` and
`8F75FF3A0185EC2310B333DE32589EBBED3FD102D66149B2C3FA3F37BF97B1BC` for
`Kora.Windows.dll`; the apphost/Core/Application fingerprints above are
unchanged. Fresh TRX and the hot-path validation receipt are retained outside
the repository alongside earlier failures and native trials. At
`2026-10-08T06:17:14.8965758Z`, no Kora process, owner marker or Debug consent
remained. Changes are uncommitted at the same recorded base. No native
Settings/tray latency recheck of this optimized build has been approved or run.

##### Optimized-Build Recheck Stopped Without Responsiveness Confirmation

The operator subsequently approved one non-recording optimized-build recheck
on the same machine/profile. PID `41028` started at
`2026-10-08T07:29:16.4261568Z` with the fingerprints above. Its initial receipt
contained 42 clean records and no activation/output. The expected saved-decline
ready check failed because the Debug consent file was absent. A read-only
176-record receipt was clean; the operator clarified that voice was left
disabled without making a saved choice, rather than an explicit decline.
The operator separately approved continuing with that legitimate absent-consent
baseline. No preference was created to simulate a choice or reinterpret saved
data, and this is not evidence of saved-decline behavior.

The process had exited before the revised ready check could complete.
No relaunch or retry was performed. The frozen final receipt contains
547 records, zero context/diagnostic gaps or errors, zero activations,
zero transcript/dispatch admissions and zero speech starts. At
`2026-10-08T07:39:07.2883277Z`, no Kora process or ownership marker remained,
and original absent Debug consent was unchanged. No consent/marker deletion
or forced termination occurred.

Asked to confirm the Exit action and visible menu/Settings/tray behavior, the
operator selected **Stop here - leave native responsiveness unconfirmed**.
Accordingly this run does not establish operator-confirmed normal Exit or a
native responsiveness pass; process absence, marker clearance and clean logs
alone are not such confirmation. Original launch scope, missing-decline and
revised-ready failures, clarification, and completed/final receipts remain
outside the repository. The earlier immediate-menu observation and deterministic
validation remain valid in their own scope. Full R03 and optimized-build
Settings/tray latency remain open; further work was stopped as requested.

##### Source Integration After the Recorded Trials

On separate instruction to commit, rebase and stop, the session changes were
checkpointed locally and rebased onto `origin/main` at
`c78e81318fb3c5b279275f9ab8ef14748bfeb952`, 26 commits beyond the trials'
original `d0a8e82ef34b82c4d888803083050c2e9dff43cd` base. Conflict resolution
retained upstream microphone recovery/catalog and native-lifetime controls,
manual-call dispatch context, playback volume/synthesis-rate retirement,
presence fade behavior and future-only diagnostic/audit retention/migration.
Session tracing, unlock policy and evidence parsing optimization were adapted
to those current contracts. The new retention corruption fixture uses an
out-of-range deadline rather than rejecting a now-valid 31-day policy.

Resolved Debug desktop/Windows and Application test graphs compile with zero
warnings/errors after correcting one duplicate import from the merge.
At that handoff, no tests or native trials were rerun and no branch was pushed. Prior native
artifact fingerprints and test receipts remain historical evidence for their
recorded source, not qualification of the rebased source or rebuilt binaries.

##### Bounded Automated Consolidation (2026-10-08)

A later authorization permits local Release build, deterministic/scratch-only
tests and draft PR publication, not microphone, playback, provider activation,
real session transitions or profile/device mutations. The reviewed source was
`75bf83596a825d82d1b4885b120fb18311727daa` on the unchanged
`c78e81318fb3c5b279275f9ab8ef14748bfeb952` base. The first no-restore Release
solution build passed with zero warnings/errors. Core passed 783/783; Application
passed 2429/2444, with 15 failures in the new privacy/correlation regressions;
the approved Windows subset passed 1022/1023, with one stale source-contract
assertion. Both test runs had zero runner skips. Ten live provider/device test
methods were explicitly excluded after operator approval, not silently skipped.

The branch-coupled repair initializes the observed topology revision from loaded
metadata so a first session-only lock does not trigger an ineligible catalog
refresh and cancel normal-unlock intent. While locked, an explicitly output-only
revision may retain validated input metadata only when current/previous input
endpoints, default, permission and catalog revision still match. It grants no
input admission; unlock still waits for closure and rechecks all fresh gates.
Changed input state or ambiguous signals cannot use that path. Preference-context
tests now exercise the current admitted input save and explicit output choice/
Save paths, without restoring obsolete device-property write authority. The
tray contract checks the deferred continuation behind the existing disposed
guard. The targeted combined repair run passed 43/43 with zero skips after
correcting two ordinal-comparison analyzer errors; no suppression was added.

On separate publication approval, machine hostname and real endpoint GUIDs are
omitted from publishable documentation and original exact evidence is retained
privately. The reviewed history remains local; publication uses a sanitized
consolidation on the same base. Historical native receipts are not rebased
acceptance, and no new native trial is authorized.

Final validation used committed source
`10d9bb459583164ac6c1bc02766e1ccece764763`, directly based on
`c78e81318fb3c5b279275f9ab8ef14748bfeb952`. The subsequent result-recording
commit changes only this document, not the validated production or test trees.

| Validation | Final result | Runner skips | Explicit exclusions |
| --- | --- | --- | --- |
| Release solution build, `--no-restore` | Passed; zero warnings/errors | Not applicable | None; setup compiled but was not executed |
| Core | 783/783 passed | 0 | None |
| Application | 2448/2448 passed | 0 | None |
| Windows bounded automated subset | 1023/1023 passed | 0 | Ten live provider/device methods below |

All 4254 executed tests passed. Final TRX counters and named results confirm
the exact 500 ms receipt boundary, unlock/closure races, hostile ambient and
deferred activity correlation, admitted device preference routes, synthetic
capture callbacks, native menu/window boundaries, warm-cache payload/column/
retention/link rejection, cache limits and retention/migration coverage.
Neither Tools nor Definitions suites were run in this bounded phase; their
projects compiled with the solution. No restore or package operation was needed.

The commands below reproduce the approved selection; `$resultsRoot` represents
the private per-run output directory, not a publishable profile path. Exact
expanded commands, build/test logs, TRX files and checksummed final receipt are
retained privately alongside the initial failed runs:

```powershell
dotnet build .\Kora.slnx --configuration Release --no-restore
dotnet test --project .\tests\Kora.Core.UnitTests\Kora.Core.UnitTests.csproj --configuration Release --no-build --report-trx --results-directory (Join-Path $resultsRoot 'CoreFinal')
dotnet test --project .\tests\Kora.Application.UnitTests\Kora.Application.UnitTests.csproj --configuration Release --no-build --report-trx --results-directory (Join-Path $resultsRoot 'ApplicationFinal')
$excluded = @(
    'Kora.Windows.IntegrationTests.Audio.BlockingAudioStreamTests.Installed_SAPI_recognizer_can_activate_against_the_live_stream'
    'Kora.Windows.IntegrationTests.Audio.WindowsVoiceRecognitionServiceTests.Enumerated_microphones_have_stable_endpoint_ids_and_include_the_Windows_default'
    'Kora.Windows.IntegrationTests.Audio.WindowsVoiceRecognitionServiceTests.StartAsync_rejects_a_device_that_is_not_present'
    'Kora.Windows.IntegrationTests.Audio.WindowsTextToSpeechServiceTests.Provider_catalog_contains_the_builtin_Windows_provider'
    'Kora.Windows.IntegrationTests.Audio.WindowsTextToSpeechServiceTests.Enumerated_voices_have_stable_metadata'
    'Kora.Windows.IntegrationTests.Audio.WindowsTextToSpeechServiceTests.Default_voice_is_female_when_available'
    'Kora.Windows.IntegrationTests.Audio.WindowsTextToSpeechServiceTests.Enumerated_output_devices_have_stable_endpoint_ids_and_names'
    'Kora.Windows.IntegrationTests.Audio.WindowsTextToSpeechServiceTests.SpeakAsync_rejects_an_output_endpoint_that_does_not_exist'
    'Kora.Windows.IntegrationTests.Dependencies.WindowsVoiceDependencyProbeTests.ProbeAsync_returns_a_documented_readiness_state'
    'Kora.Windows.IntegrationTests.Dependencies.WindowsTextToSpeechDependencyProbeTests.ProbeAsync_returns_a_documented_readiness_state'
)
dotnet test --project .\tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj --configuration Release --no-build --report-trx --results-directory (Join-Path $resultsRoot 'WindowsFinal') --filter-not-method $excluded
```

Each excluded method was confirmed present exactly once in discovery and absent
from final results. Exclusions are not runner skips or passes. No application
launch, live capture/playback/provider inference, real lock/unlock, elevation,
installer execution or profile/device-setting mutation occurred in this phase.
Native R03 A03/A04/A05/A06, every-reference-trial release within 500 ms of observed
lock, OS notification delay, last acoustic sample and optimized native latency
remain outstanding. Automated synthetic receipts do not close those gates.
Privacy is second in the recommended merge order after accessibility;
combined-source revalidation is separately coordinated. Draft publication
does not authorize merge, auto-merge or a new interactive experiment.

##### Portable Coverage Repair (2026-10-08)

PR #98 workflow `37761751218` retained a coverage report bound to published
source `6fc92bce0021645a9423e334af1599858e5081f8`: portable tests passed, but
the exact gate rejected 12702/12711 lines and 7124/7134 branch outcomes.
The publish-inspection upload failure followed the aborted publication steps.
The parent confirmed that the same source's hosted Windows run passed
1033/1033 with zero failures/skips; this does not authorize those ten live
methods locally or qualify the later repaired source as native acceptance.

The test-only repair at `830f227f321ee86e2384e8f339e2f05c245ea142` adds 15
deterministic cases. The originally missing paths, using line numbers from
the retained source-bound report, are:

| Production path | Previously uncovered path | Added evidence |
| --- | --- | --- |
| `HostRequestRunner.cs:18-21` | Synchronous cancellation | Preserve the exact cancellation exception, admitted origin, cancelled terminal outcome and restored ambient context, both root and nested |
| `MainViewModel.cs:3037-3040` | Final explicit readiness rejection after admission | Changed authoritative permission cannot commit readiness or open capture |
| `MainViewModel.ResponseModeConfiguration.cs:86` | Voice initiation without ambient host context | Exact synthetic voice-ready save and protected-call refusal retain original voice provenance, durable control records and denial audit without recording or autoplay |
| `MainViewModel.VoicePrivacy.cs:419-427,457-460` | Final ownership denial, stale revision, disposal, handoff, changed permission or missing unlock intent; cancelled recovery | Public unlock races preserve privacy hold and terminate as cancelled; independently exercise the final atomic guard with existing reflection patterns, reading rather than rewriting its fields |
| `PrivacyObservation.cs:28` | Both factory clock inputs | Unspecified time is bounded by monotonic reads; supplied time is preserved with fresh identities and the same clock frequency; neither invents OS delay |

No production source, analyzer policy, threshold, assembly filter or coverage
exclusion changed. No production hook was added. The final full no-restore
Release solution build passed with zero warnings/errors. The 13 new Application
cases passed together before the full portable selection. Two initial
response-mode fixture assertions were corrected to use a synthetic ready
microphone and the durable audio-control store, not the unrelated task store;
no production defect was demonstrated.

| Final portable suite | Passed / total | Failures | Runner skips |
| --- | --- | --- | --- |
| Core | 785/785 | 0 | 0 |
| Application | 2461/2461 | 0 | 0 |
| Tools | 38/38 | 0 | 0 |
| Definitions | 6/6 | 0 | 0 |

All 3290 final portable tests passed. The unchanged gate passed exactly
**12711/12711 lines and 7134/7134 branches**, with both raw rates equal to
`1`. An intermediate result had one missing clock-input branch despite
rounding to "100.0%"; it was rejected, the missing assertion was added and Core
coverage rerun. The final report combines only the four selected final suite
reports. Nonfatal report-generator diagnostics about unavailable generated
logging source text were retained; no measurements or filters were altered.

The repository's CI portable commands were used with private output directories:

```powershell
dotnet build .\Kora.slnx --configuration Release --no-restore
foreach ($suite in @('Core', 'Application', 'Tools', 'Definitions')) {
    $prefix = $suite.ToLowerInvariant()
    dotnet test --project ".\tests\Kora.$suite.UnitTests\Kora.$suite.UnitTests.csproj" --configuration Release --no-build --results-directory (Join-Path $resultsRoot $prefix) --report-trx --coverlet --coverlet-output-format cobertura --coverlet-file-prefix $prefix
}
dotnet reportgenerator "-reports:$finalReports" "-targetdir:$coverageReport" "-reporttypes:Cobertura;MarkdownSummaryGithub;TextSummary" "-assemblyfilters:+Kora.Core;+Kora.Application;+Kora.Tools;+Kora.Definitions"
.\eng\Assert-CodeCoverage.ps1 -ReportPath (Join-Path $coverageReport 'Cobertura.xml') -MinimumLine 100 -MinimumBranch 100
```

`$finalReports` selects the final Core rerun and unchanged final Application,
Tools and Definitions reports. Exact expanded commands, source/test blob
identities, original CI report, attempted/final TRX and coverage checksums
remain private. The following result-recording commit changes documentation
only; its production/test trees match the validated repair. No restore,
license runner, local Windows rerun or native/profile/device action occurred.
The previous ten local exclusions and every outstanding native gate remain
unchanged. Main remains `c78e81318fb3c5b279275f9ab8ef14748bfeb952`; no rebase
or merge is part of this repair. The parent owns #97-first integration,
combined-source rebase authorization and required CI review.

##### Combined Accessibility/Privacy Integration (2026-10-08)

After the parent's separately authorized squash merge of reviewed #97, the
actual fetched main was `d145ace1ae59016d524c9b2ec7def9f192e0c6e6`.
The published sanitized privacy branch at
`22061a1f5e33eff4c08c8409702fb45c05270e2a` rebased onto that exact main
without conflicts. All four privacy commits remained patch-equivalent in
`git range-diff`; no source or fixture/API repair was required. Both the UX
and privacy outcomes above remain dated historical receipts, not replacement
claims. The local-only unsanitized reviewed commit is not in branch ancestry.

Combined validation used committed source
`1c3670be05f092a63bb84fae8e7d5af521a7e6e9` on the new main. The following
result-recording commit changes only this document; its production/test trees
match the validated combined source. The rebased counterpart of the test-only
coverage repair `830f227` is `be31f16af7eec29602ef57225d0a5f8aa54b90ae`.
All 15 cases remain meaningful and passed in the combined full suites,
including original voice provenance/call refusal, cancellation/context
termination, final readiness/ownership/privacy/revision/lifecycle/intent
checks and both observation-clock inputs.

The initial Release `--no-restore` build failed because the newly introduced
native-UX fixture project had no assets and the integration project had no
resolved `Avalonia.Headless` references. A locked solution restore through the
machine-required Networking-AAA feed succeeded, without tracked manifest,
lockfile, feed-setting or credential changes. The subsequent Release solution
build with `--no-restore` passed with zero warnings/errors. The native fixture
and installer projects compiled but were not launched.

| Combined Release validation | Passed / total | Failures | Runner skips | Explicit exclusions |
| --- | --- | --- | --- | --- |
| Core, with CI coverlet flags | 785/785 | 0 | 0 | None |
| Application, with CI coverlet flags | 2461/2461 | 0 | 0 | None |
| Tools, with CI coverlet flags | 38/38 | 0 | 0 | None |
| Definitions, with CI coverlet flags | 6/6 | 0 | 0 | None |
| Windows bounded automated/headless subset | 1095/1095 | 0 | 0 | Same exact ten live methods listed above |

All 4385 executed tests passed. The normal four-assembly portable report and
unchanged 100% thresholds passed exactly **12711/12711 lines and 7134/7134
branches**, with both raw rates `1`; no exclusions, thresholds or analyzer
policy were changed. Report-generator diagnostics about unavailable generated
logging source text remained nonfatal and were retained in the private log.

Windows results explicitly include 20 `AccessibilityRuntimeContractTests`,
8 `BoundedSurfaceAccessibilityTests` and 18 `NativeUxFixtureContractTests`,
plus the privacy observer/capture, menu/window and warm-cache regressions.
These 46 headless/support cases use an isolated off-screen Avalonia test
application, synthetic denied audio/clipboard/network/effect boundaries and
GUID-owned scratch children that are cleaned up. They neither run production
composition nor launch the native fixture executable. Their use of real window
types off-screen does not establish native UIA, focus, Narrator, latency,
clipboard, provider, ownership or OS-transition acceptance. Final TRX confirms
that none of the ten excluded live methods ran; exclusions are neither runner
skips nor passes.

The portable build/test/report/gate commands in the preceding repair receipt
and Windows command in the consolidation receipt were rerun against this
combined source, using fresh private output directories. The conditional
restore command was:

```powershell
dotnet restore .\Kora.slnx --locked-mode --source https://msazure.pkgs.visualstudio.com/One/_packaging/Networking-AAA/nuget/v3/index.json
```

The feed override is machine-local execution routing, not repository
configuration. Exact expanded commands, source/tree hashes, TRX totals/named
cases and coverage checksums are retained privately. No raw/profile data was
uploaded. No production app/native fixture launch, real microphone/provider/
device activation, playback, real clipboard access, elevation, installation,
real lock/unlock or profile/device-setting mutation occurred.

The parent authorized marking #98 ready only after this combined local
validation and retains merge authority pending strict updated-head hosted
CI. Publication uses an explicit expected-old-head force-with-lease; no blind
force or auto-merge is authorized. Historical native receipts are not
combined-source acceptance. R03 A03/A04/A05/A06, every-reference-trial 500 ms
release, OS notification delay, last acoustic sample, optimized native latency
and all remaining native UX/accessibility gates remain open.

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
| A03 - External session privacy | Existing WTS/power observation, synchronous negative closure before requery and stale-callback/disposal boundaries have deterministic regression coverage, not complete native timing acceptance. Separately approve Win+L/idle lock, disconnect, suspend/resume and applicable session transitions while an owned activation/output is active. Capture closes, buffers clear, output stops and sensitive presentation hides. Normal unlock restores only prior enabled intent after confirmed closure and fresh gates; reconnect/resume and other failures require explicit recovery. Neither path can replay audio/approvals or reveal sensitive presentation automatically. Production wake remains gated. | One native retiring-recorder pilot on 2026-10-08 passed at **113.4642 ms** with zero stale dispatch. **Capture released within 500 ms from the observed lock event in every reference trial** remains unproved. Record OS event-to-notification delay separately, plus each observed-event-to-release duration, buffer clearing, last output sample and zero stale dispatch; no averages/p95 substitution for this target. |
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
