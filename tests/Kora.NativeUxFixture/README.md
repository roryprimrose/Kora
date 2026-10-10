# Interactive native UX validation

This fixture uses production Kora windows/styles with synthetic ownership and
privacy, isolated scratch SQLite stores and preferences. It never starts the
production host, reads normal profile sources, captures audio, runs models,
performs network requests, or uses the shared clipboard.

The pending proof is the **screen-reader/text-scale/physical-display slice of
R05/R12/R14/A4**, not a repeat of the already recorded
[bounded keyboard observations](evidence/2026-10-09-native-ux.json). This does not
qualify actual privacy/ownership, speech targeting, installed behavior, numeric
rendered contrast, or complete all-surface accessibility.

## 1. Focus-free preparation

From the repository root in non-elevated PowerShell 7.5+, select a **new absolute
directory outside the repository on a fixed local drive**:

```powershell
$proof = Join-Path $env:TEMP ('kora-native-ux-validation-' + [guid]::NewGuid().ToString('N'))
.\eng\Invoke-NativeUxValidation.ps1 -OutputDirectory $proof
```

Preparation builds/publishes without running the native host, runs six directly
applicable headless test classes, and records exact source/payload identities
and test counts in `automated.json`. It refuses failed/skipped/zero/missing-class
results and source changes during preparation. It does not focus windows,
send keys, start Narrator, take screenshots or change Windows settings.

If dependencies are missing, supply `-PackageSource` with your machine-approved
NuGet feed; restore is locked. No feed configuration is written to the repository.
Output directories are never overwritten. Keep raw TRX and local console output
private: they may contain profile/scratch paths. Do not publish the payload,
bulk logs, account details or screenshots.

## 2. Begin the interactive trial only when ready

This phase **does open/focus windows**. Finish other desktop work first. Use the
same non-elevated profile and prepared bundle. Record your original Windows
Accessibility > Text size, Display > Scale, monitor count, and Narrator state.
Do not install tools, elevate, enable listening, or change privacy/network policy.
If Narrator would disturb a call or other people, defer this phase.

```powershell
.\eng\Invoke-NativeUxValidation.ps1 -Stage Launch -OutputDirectory $proof -ApproveInteractiveLaunch
```

The runner checks every payload hash before/after launch, reports the exact PID,
waits for you to stop the fixture, and records startup, exit and scratch cleanup.
It does not send input or manipulate focus for you. Wait for **READY** in the
synthetic launcher. A startup failure or unclean exit is not a pass.

## 3. Perform and record the five observations

Edit `operator.json` in the proof directory. For each row replace `Pending`
with `Pass`, `Fail` or `Blocked` and replace the instruction note with the actual
observation. Record failures even if another surface works. An unavailable
screen reader, monitor or approved setting change is **Blocked**, not Pass.
Use only synthetic fixture content. Record percentages/counts, not device
serials, user names, profile paths or unrelated desktop content.

1. **SR01: names, roles, state and focus.** Start Narrator yourself
   (Windows+Ctrl+Enter). Using Tab/Shift+Tab and Enter/Space, open **Appearance**,
   **Sessions**, **Guide and owned immutable details**, and **Review local version**.
   On each surface verify that the title, control names/roles, selected values,
   disabled state, and focused item are announced understandably. Open a guide
   detail, close the detail with Escape, and verify focus returns to its owner.
   A visible label alone is not a screen-reader pass.
2. **SR02: truthful question/status announcements.** In **Review local version**,
   choose the available choice, review, save a draft, review again and submit.
   Verify review/draft focus, announced terminal result, disabled answer actions
   and Close focus. Wait through passive refresh; it must not repeat disruptive
   announcements or lose the terminal status. Then use **Open synthetic
   stale-revision question**, return to the launcher and **Advance exact synthetic
   question revision without retargeting**. Attempt the old question's action:
   refusal must be visible/announced, no stale answer accepted, Close reachable.
   If announcement timing cannot be established, record Blocked.
3. **TXT01: theme and enlarged text.** In fixture Appearance select Light and
   Dark; inspect descriptions, inactive navigation, focus and long guide/detail
   content. Record the current Windows text-size percentage. If you approve the
   temporary change, increase Windows Accessibility > Text size (for example
   from 100% to 150%), Apply, and repeat both themes on Settings, Sessions,
   Guide/detail and Question windows. Resize windows, scroll and tab to all
   actions. Pass requires readable text, visible focus and reachable actions
   without clipping/overlap/lost labels at **both** recorded text sizes. This is
   an operator readability observation, not a measured WCAG contrast ratio.
4. **DPI01: current physical display.** Record Display > Scale for the primary
   monitor. Move/resize those native windows within that monitor; inspect
   wrapping, scrolling, focus indicators and reachable actions. Verify selected
   session/question content does not change merely because a window moves.
5. **DPI02: mixed-DPI physical monitors.** If two physical monitors with different
   scales are already available, record both scales and move the open Settings,
   Sessions, Guide/detail and Question windows between them and back. Check
   readability, scaling, keyboard focus, owner/detail placement, stable question
   and selection, and reachable actions. Do not change display topology just to
   manufacture a pass. With only one monitor or equal scales, record **Blocked**.

Restore the original Windows text/display settings and Narrator state yourself.
Set the corresponding restoration flags to `true` only after verifying this.
Use **Stop fixture and clean up scratch state** in the launcher. Wait for the
runner to report a clean exit. Do not close the terminal or kill the process;
forced termination fails the trial and can leave scratch evidence for inspection.
If a display change requires relaunch, stop cleanly and repeat Stage Launch
against the same bundle; all trials are retained.

### Automated native mechanics instead of repetitive manual input

The separate [mechanical driver](../../eng/Invoke-NativeUxMechanics.ps1) opens
only the isolated fixture, finds controls/windows within its exact PID and
uses native UI Automation for inspection and guarded Space for button actions.
Keyboard input requires both the foreground PID and exact window handle to
match; the driver activates only its own verified visible windows.
Appearance cycles use Tab/Shift+Tab. It checks maintenance's disabled/off consent,
two complete forward/reverse Appearance cycles, exact question review/draft/
submit and terminal refresh/focus, stale-revision refusal and synthetic private
window closure/reopening. It preserves every failed attempt and attempts normal
owned cleanup, recording any forced termination.

Leave the desktop unused for this run. Approval has an explicit future deadline
of at most one hour; the driver checks it at operation/wait boundaries. This is
not a hard real-time watchdog for an unresponsive OS automation API.

```powershell
$mechanics = Join-Path $env:TEMP ('kora-native-mechanics-' + [guid]::NewGuid().ToString('N'))
.\eng\Invoke-NativeUxMechanics.ps1 -PreparedDirectory $proof -OutputDirectory $mechanics `
    -DeadlineUtc ([DateTimeOffset]::UtcNow.AddMinutes(10)) -ApproveDesktopAutomation
```

Use `-ValidateOnly` without desktop approval for prerequisite checks that open
no windows. A passing `mechanics.json` is machine-observed mechanics only.
It does not edit `operator.json`, pretend Narrator spoke, measure numerical
contrast, verify readability or manufacture text-scale/mixed-monitor passes.
The five human observations above remain independently scoped. Do not run this
driver alongside a manually launched fixture or another desktop automation.

### Extended workstation automation

The [workstation driver](../../eng/Invoke-NativeUxWorkstation.ps1) executes the
maintained five-row baseline, then a fresh owned fixture for ten additional
scoped observations. It reuses the baseline's PID/HWND, keyboard and deadline
admission helpers; it does not extract source or invent a separate input path.
Both processes must exit cleanly and delete only their own scratch child.

```powershell
$workstation = Join-Path $env:TEMP ('kora-native-workstation-' + [guid]::NewGuid().ToString('N'))
.\eng\Invoke-NativeUxWorkstation.ps1 -PreparedDirectory $proof -OutputDirectory $workstation `
    -DeadlineUtc ([DateTimeOffset]::UtcNow.AddMinutes(15)) -ApproveDesktopAutomation
```

Run from the checkout root. From another working directory, invoke the script
by its absolute checkout path using PowerShell's `&` call operator; a relative
`.\eng` command resolves against the terminal's directory, not VS Code's repo.

The extended rows cover:

| Row | Machine observation |
|---|---|
| W01 | Create/rename a metadata-only session, preserving its exact ID/generation and advancing its metadata revision |
| W02 | Alternating exact-ID bounded history reads, metadata-only ordered records, unchanged repeated snapshots and passive field focus |
| W03 | Three guide/detail lifetime cycles, read-only source bytes matching the immutable digest, retained owner and refused closed-generation rendering |
| W04 | Every default bundled package file tab's source digest, original byte count and read-only value; execution unavailable |
| W05 | Native retained/missing explicit trace segments and session-confined removal of cross-session navigation authority |
| W06 | Revoke the native window referencing a changed session generation, then explicitly reopen and read the same immutable subject |
| W07 | Resize/move only an owned Settings HWND across enumerated work areas; actual native DPI/bounds, exact selector focus and original placement restoration |
| W08 | Exact pending queue inspection/removal and retained receipt, passive focused work without dispatch, deliberate two-session local-version dispatch and exact terminal observations |
| W09 | Discover the exact newly added pending task by native scrolling after retained terminal rows; synthetic private-window clearing/no replay and zero dispatch of old-epoch work after reopening; explicitly clear its sole pending entry |
| W10 | Deliberate metadata-only session disposition after exact preview, native revocation/reopening, retained tombstone/history citations and unaffected unrelated subject |

Native history completions require the newly addressed ID, not retained JSON
from a prior read. Window/descendant retirement permits only bounded discovery
retries, with counters; consequential actions are never automatically retried.
Native display enumeration and window DPI are mechanical evidence, not proof
of readable rendering on physical monitors. No global display/text settings
are changed. No copy/disclosure control, audio, screenshot, real profile source,
model, network or installation operation is admitted.

The fixture now composes the real bounded local-version queue service, using
its existing native version capability and scratch stores. Its queue lifecycle
is drained before scratch deletion. Stable exact work-row containers preserve
focus during passive observation; changed observation time alone does not
reannounce unchanged row fields or renew inspected task cancellation authority.
The work list forwards native ScrollPattern to its current template's real
scroll viewer. This preserves virtualization and passive selection while making
off-viewport rows discoverable through the list's bounded native scroll range.
The durable-session and cited-evidence record lists use the same provider
forwarding after separate synthetic overflow observations. Scrolling does not
select a session, navigate evidence, disclose content or renew authority.

Inspect `baseline\mechanics.json` and `expanded\workstation.json`. The latter
binds the expanded driver, baseline driver and prepared payload receipt hashes.
Keep failed attempts unchanged. Earlier development trials did not discover a
new pending row below retained terminal rows because the stock list peer
advertised no native scroll range. W09 now requires discovery of that exact row
through the list's ScrollPattern before synthetic gate closure. Discovery is
not an observation that Narrator announced it; actual announcement and
readability remain operator-only.

These results reduce repetitive manual input; **none populates the human
observation template or signs off Narrator/readability/physical displays**.

### Separate native retention automation

The [retention driver](../../eng/Invoke-NativeUxRetention.ps1) reuses the
five-row mechanical baseline and opens another fresh default scratch fixture.
It needs separate fixed-deadline desktop approval, not operator control input:

```powershell
$retention = Join-Path $env:TEMP ('kora-native-retention-' + [guid]::NewGuid().ToString('N'))
.\eng\Invoke-NativeUxRetention.ps1 -PreparedDirectory $proof -OutputDirectory $retention `
    -DeadlineUtc ([DateTimeOffset]::UtcNow.AddMinutes(15)) -ApproveDesktopAutomation
```

Seven rows cover passive exact-ID retention and isolation; preview/cancel
without commit; Keep with newer audit revision and fresh-window readback;
subject/refresh review invalidation; ordinary retention without clock reset;
actual live/unresolved scratch work-hold observation; and synthetic gate
revocation without replay. Exact-ID inspection must not create selected lifecycle
authority. Other sessions, immutable identities and recorded activity/archive/
delete clocks remain unchanged. This uses the real retention service, not
direct SQL mutation or an effect-capable fixture.

Inspect `baseline\mechanics.json` and `retention\retention.json`. Preserve failed
attempts. Window reopening is not process-restart persistence. The hold row
observes metadata; due timer cleanup/hold enforcement is separate storage-test
evidence. No native due-date, power-loss, copy-disposal, forensic-erasure,
installed, audio, real OS-transition or human accessibility acceptance is inferred.

### Separate silent caption fixture

The default fixture still refuses all speech. A separately approved launch may
append the exact `--silent-caption-fixture` flag after its scratch-parent argument.
This mode uses an in-memory Windows-provider contract with a synthetic voice and
output ID, not System.Speech, a device, a synthesis engine or a render endpoint.
It leaves microphone consent denied, models disabled and real effects refused.
Never describe these observations as audible playback or native provider acceptance.

The additional launcher actions arm one ordinary typed `show your window`
response, observe its synthetic playback identity, complete normally, stop/retire,
inspect metadata, and set one of the four caption placements through the admitted
preference commands. The real caption policy and window controller receive these
frames. Queueing alone must show no caption; only an exact admitted playback
frame reveals text. Pinning may retain already-observed text after normal
completion, but stop/privacy/source retirement must clear it without replay.
Structured snapshots include the completed fixture operation and a monotonic
snapshot revision. An unchanged placement or absent queued window alone is
not proof that an async Save or Stop completed; wait for its fresh exact receipt.

Prepare a new payload after changing fixture code; never overwrite a previously
qualified preparation or reuse its result for the new bytes. Native focus,
read-only value, placement, expiry, pin and synthetic gate measurements are
mechanical scopes only. Actual Narrator, rendered clipping/readability and
physical/acoustic acceptance remain human or separately approved live trials.

### Separate synthetic list-overflow fixture

An independently approved launch may append the exact `--list-overflow-fixture`
flag after the scratch-parent argument. It cannot be combined with the silent
caption flag. Default preparation and launch do not enable this mode.

Initialization explicitly enqueues and dispatches nine scratch local-version
tasks through the ordinary bounded queue, then leaves one exact pending task.
The real local-event broker admits eight visible observations and reports two
omitted observations; trailing ineligible work is not fabricated into events.
The mode also records twelve missing synthetic trace links through the host
activity path and adds twelve non-executable `/overflow-01` through
`/overflow-12` catalogue entries. It retains denied voice consent and the normal
fixture's refused external effects.

Select the exact synthetic session in Sessions to inspect local events. In
Evidence, filter by the trace ID from **Inspect synthetic list overflow state**,
search and select the source span to expose the missing links. Open the typed
slash window and enter `/overflow` to expose the catalogue. Native scrolling
must make every admitted event ID, missing-link trace ID and command name
discoverable without event controls, link navigation, command application,
Run/Enter or dispatch of the remaining pending task.

All six bounded lists now forward native scrolling to their current template
scroll viewers: sessions, work, local events, cited evidence, trace links and
artifact commands. Limits, virtualization, passive selection and execution
boundaries are unchanged. Exact native discovery is machine evidence, not a
claim that Narrator announced the rows or that their rendering is readable.

### Exact-grant inspection and single-record revocation preparation

The [exact-grant driver](../../eng/Invoke-NativeUxExactGrants.ps1) is a separate
scratch-only proof. Its default `Prepare` stage builds/publishes the fixture and
runs the focused headless contracts. It never starts a native window, sends
desktop input, starts an interactive hour, or supplies approval:

```powershell
.\eng\Invoke-NativeUxExactGrants.ps1 -Stage Prepare -OutputDirectory <new-fixed-local-proof-directory>
.\eng\Test-NativeUxExactGrantsContracts.ps1 -OutputDirectory <new-fixed-local-contract-directory>
```

Only the parent/operator may run the frozen prepared worktree later, after all
unattended children finish and the next fixed one-hour interactive authority is
separately recorded. **Do not fill or derive the deadline during preparation.**

```powershell
.\eng\Invoke-NativeUxExactGrants.ps1 -Stage Run `
    -PreparedDirectory <prepared-directory> -OutputDirectory <new-native-trial-directory> `
    -DeadlineUtc <UNFILLED-PARENT-RECORDED-UTC-DEADLINE> -ApproveDesktopAutomation
```

Run refuses missing/expired/over-one-hour deadlines, missing approval, changed
source/build-input fingerprints, changed driver/helpers and changed payloads.
`-ValidateOnly` checks Run prerequisites without launch/input, but still requires
the operator's explicit deadline. It is not an approval or acceptance receipt.
The mechanics driver's internal `-LoadHelpersOnly` path reuses its existing
PID/HWND/foreground/deadline helpers without launching a baseline fixture.
Original mechanics defaults and refusals remain unchanged.
No-launch driver contracts also reject direct or selection-mediated page-discovery
recursion and inventory resets inside discovery. Extracted helper tests use local
fakes to check bounded viewport discovery, exact/missing lookup and one-step
pagination; they do not execute the driver launch path or qualify native scrolling.

The optional `--exact-grants-fixture` launch flag cannot be combined with other
fixture modes. It seeds 51 synthetic grants via genuine host-owned committed
intent, proposal, trusted snapshot, approval question and LocalUi answer paths.
No raw SQL, fabricated grant records, real permission, dispatcher or effect
execution is used. Exact IDs are generated at **later fixture initialization**,
not at Prepare; the later native receipt retains IDs/revisions, audit head,
bounded inventory and grant digests, origin observations and recorded retention
clocks. Passive reads do not issue approval/control intent or renew clocks.
Launcher observation sequence numbers are presentation-only completion markers,
not authority revisions.
The production exact inspection and outcome text use the existing
`NamedTextBlock` peer so native automation exposes the displayed ID/revision
and current outcome together with their labels; this adds no control authority.
The retained inventory uses the existing `ScrollableListBox` peer to expose its
current native viewport. Discovery reads exact IDs only from displayed row text,
not the stock container's domain-record name, which contains other correlation IDs.
Native admission resolves window visibility and privacy on the UI thread even
when storage revalidates the confirmation after asynchronous I/O. Failure
presentation retains a live host diagnostic activity without granting control.
If an enumerated owned HWND retires before UIA resolves its provider, discovery
records the retirement only after Windows confirms that the handle has no owner.
Provider failures for live handles still abort; consequential actions are never retried.

| Row | Machine assertion |
| --- | --- |
| G01 | All 51 exact IDs through bounded native pages; current inspection, passive audit/grant/clock invariance and no inferred applicability |
| G02 | Genuine overflowing native ScrollPattern extent, viewport and endpoint readback; selection preserved; Blocked rather than simulated if unavailable |
| G03 | Selection and refresh retire the displayed review |
| G04 | One authority-only synthetic use advances revision/use count; stale displayed revoke conflicts without effect dispatch |
| G05 | Genuine origin lifecycle change invalidates stale displayed inspection |
| G06 | Synthetic privacy closes the old window; a fresh window has no replayed review |
| G07 | Explicit reviewed revoke of target only, immutable readback and subsequent genuine consume denial |
| G08 | 48 unrelated grants including Perpetual unchanged; target/Perpetual clocks unchanged; fresh-window readback |

There are no automatic action retries, bulk removal, approvals from inspection,
or promises that revocation stops or rolls back running effects. The native
receipt is synthetic mechanics only, not Narrator/readability, real OS privacy,
real effect containment, process restart or installed qualification. G02 can
remain Blocked if this provider does not expose a genuine overflowing list.
Failed attempts and local output remain durable; stop normally via the exact
owned fixture. Forced termination fails the trial and can leave scratch state.
Do not edit historical receipts or relabel preparation as native acceptance.

### Minimum operator walkthrough after automated mechanics

Do not manually repeat the machine-only queue, history, source-hash, stale-ID
or disposition assertions. Keep the five observation rows, but group the
remaining human work into three passes:

1. **Listen:** launch the prepared fixture using Stage Launch, turn Narrator on
   yourself and perform SR01/SR02 above. Only a person listening can judge the
   names/roles, understandable outcome/refusal and non-disruptive refresh.
2. **Look:** perform TXT01/DPI01/DPI02 above together, using the same open
   windows. Inspect both themes at the original text size, move the windows
   between the already configured displays, then repeat at an independently
   approved enlarged text size. Do not change topology. Mark an unperformed
   enlarged-text or physical-monitor scope Blocked rather than Pass.
3. **Restore and attest:** restore the original text/display and Narrator
   settings, stop the fixture cleanly, enter factual outcomes in `operator.json`
   and run Stage SignOff below. A machine-only trial is not a replacement for
   the clean Stage Launch receipt required for this human sign-off.

## 4. Sign off the observed scope

The assistant-name editor now has the native label
"Display and push-to-talk command-prefix name" and help bound to its authoritative
configuration description. Machine trials check native Name/HelpText across
default, normalized save and reset. In the Listen pass, still confirm that
Narrator presents the field and its description understandably; UIA property
equivalence does not substitute for listening.

Fill the integer display fields and set `operatorConfirmed` to `true` only after
reviewing all five factual notes. `secondDisplayScalePercent` may remain `null`
when DPI02 is Blocked. The enlarged text value must exceed the original for a
TXT01 Pass; mixed-DPI Pass needs at least two monitors with different scales.
If text enlargement was not performed, leave `enlargedTextScalePercent` as `null`
and record TXT01 as Blocked; do not invent an unobserved display setting.

```powershell
.\eng\Invoke-NativeUxValidation.ps1 -Stage SignOff -OutputDirectory $proof
Get-Content -LiteralPath (Join-Path $proof 'signoff.json')
```

Sign-off requires unchanged payload/test evidence, at least one clean READY/exit-0
trial, no failed trials, explicit outcomes, and restoration confirmation.
It produces **ScopedPass**, **Partial** (blocked observations) or **Failed**;
it never converts preparation into native acceptance. It refuses Pending or
malformed observations and never overwrites a sign-off. Preserve failed bundles;
prepare a fresh bundle after a fix rather than relabeling a failed trial.

Review the final receipt's scope and hashes. A ScopedPass signs off **only the
listed synthetic observations on these payload bytes and recorded display
conditions**. Full qualification and actual privacy/audio/installed boundaries
remain open. Share only a reviewed, sanitized receipt if it is later needed in
the deferred-validation register; publishing or committing evidence is separate.
