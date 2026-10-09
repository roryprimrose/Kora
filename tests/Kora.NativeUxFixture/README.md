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

## 4. Sign off the observed scope

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
