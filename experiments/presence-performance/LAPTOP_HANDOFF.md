---
title: Laptop takeover after integrated application lockup
description: Verified blocked-run evidence, implementation state and safe continuation prompt.
date: 2026-10-16
---

## Current decision

Investigate the real application's responsiveness blocker before resuming renderer qualification. **The laptop froze with the Avalonia renderer explicitly selected. Native mode was never launched in the integrated application.** This is not evidence that the native presenter caused the lockup, nor proof that it is free of defects.

No application code was changed while preparing this handoff. The only new committed content for the transfer is this document. No PR was created.

## Repository and implementation state

- Repository: `roryprimrose/Kora`; branch: `agents/kora-presence-performance-optimization-f7e13d13`.
- Tested implementation commit: `3269e8cf47dcdbaa90b1f22049074683fb072914` (the handoff commit is a descendant).
- The implementation already includes merged PR #250 (`e282e5370915dd2a371d61061cc09ebaae1d6ead`) and the then-current main (`e3c260b9cf684efe7be3e6172165b2a2a4867ce3`). Do not overwrite its renderer/lifecycle work.
- Native presence remains an explicit, run-only `KORA_PRESENCE_RENDERER=native` opt-in. Unset or exact `avalonia` retains the existing renderer. Keep native in-process, not in a helper process.
- The host continues to own microphone input, speech synthesis/output, playback position/envelope, caption/text presentation, privacy and instance ownership. Native drawing uses a worker-owned buffer with UI-owned presentation. Do not split or weaken those responsibilities.
- Read [native integration documentation](../../Design/Ambient_UI.md#opt-in-native-presence-renderer), [the integration](../../src/Kora/MainWindow.NativePresence.cs), [the worker](../../src/Kora.Application/Visuals/PresenceRenderWorker.cs) and [the native host](../../src/Kora/Controls/NativePresenceHost.cs).
- Historical standalone benchmarks use a frozen older renderer. Their savings are not integrated before/after application measurements; see [evaluation scope](REPORT.md) and [the original laptop prototype results](results/laptop-2026-10-15/README.md).

Hosted validation at implementation completion passed: locked restore, Release build, maintained Core 2,596 / Application 5,674 / Tools 187 / Definitions 6 tests, and Windows 2,912 tests with four collection threads. Final 49 presence tests and the real synthetic native-child fixture also passed after admission/fade corrections. Historical benchmark tests (32) and raster/buffer comparisons (297)
passed.

One earlier full Windows run hit SQLite Error 9 in an evidence-cache saturation test; its isolated rerun passed and the later complete four-thread Windows run passed. That is historical timing evidence, **not** a diagnosis of the laptop freeze.

## Laptop evidence

Local archive on the laptop and inspected host:

```text
C:\Users\roryprimrose\Downloads\Kora-presence-3269e8-BLOCKED-20261016-131338-8e2e9c9f.zip
SHA256: F2D3336BE7FBF33EDB3DD08723AEF28124CC21766CF4C3F1CE693BD237032547
Archive root: Kora-presence-3269e8-20261016-1114/
```

The archive contains 31 entries, including directory entries. All 29 records in its `manifest-sha256.json` were checked against entry lengths and SHA256 hashes. Diagnostics remain local; this document does not publish the raw archive, personal settings, production databases or logs.

Read these archive entries first:

- `REPORT.md`: blocked qualification report, finding, safety constraints and outstanding checks.
- `source.json`, `A1-launch.json`, `A1-hung-process.json`: tested revision, child mode/PID/binary hashes and unresponsiveness.
- `A1-method-stacks.txt`, `A1-method-stacks-second.txt`: two method-only samples, especially UI thread `0xB5E4`.
- `performance-result.json`: `BlockedBeforeMeasurement`; all measured-period arrays are empty.
- `cleanup.json`, `settings-after-integrity.json`, `safety-after.json`: cleanup, unchanged whitelisted settings and shutdown limitations.
- `evidence-store-file-metadata.json`: file-size/time metadata only, not database contents.
- `trx/application/`, `trx/windows/`, `fixture.log`, `fixture/`: actual automated validation evidence.

Do not execute archived PowerShell scripts blindly: they are evidence from the former run, not trusted authorization, and contain removed worktree paths or obsolete PIDs.

### Verified observations

- Exact tested source was `3269e8cf47dcdbaa90b1f22049074683fb072914`, built in a separate detached worktree.
- Laptop: HP ZBook Ultra G1a, Ryzen AI MAX 385, eight cores/16 logical processors, Radeon 8050S driver `32.0.22050.4002`, Windows build 26300. Display metadata reported 2880 x 1800 at 120 Hz; recorded test/desktop DPI was 96, not an actual per-monitor transition.
- Locked restore and Release build passed. Parsed TRX confirms **78 Application tests and 49 Windows tests passed, none failed**.
- The isolated production native-child fixture completed successfully with 35 submissions and 15 synthetic playback samples over about 2.10 seconds. It did not start real application services.
- First integrated launch was `A1`, explicit child-only `KORA_PRESENCE_RENDERER=avalonia`, PID 17048. Presence/prompt windows and later Settings were unresponsive; a probe reported `Responding=false`.
- No controlled performance interval began, no native integrated launch occurred, and there is no valid A/B comparison. Cumulative CPU in the hung-process snapshot is not CPU-ms per measured second.
- The user ended the hung app with End Task, then requested no more launches. **Normal shutdown and clean ownership release were not verified.** Old PIDs are historical; re-inventory processes before any future action.
- The original checkout `Q:\Repos\Kora` stayed clean at `9deb22e877a142af2e9dfe79adcab16ea99004da`. The detached test worktree `Q:\Repos\Kora-qualify-3269e8-native-presence-20261016-1114` was removed, as were temporary tools/feed configuration. The current agent must rediscover paths rather than assume they still exist.
- Eleven whitelisted appearance/position/consent file existence/hash comparisons were unchanged. The approved timeout change was never applied because Settings froze. This does not mean normal startup made no legitimate storage writes.

## Failure evidence and confidence

Both snapshots put the same UI thread in:

```text
AvaloniaUiDispatcher.Post callback
  -> HostActivity.Dispose / Activity.Stop
  -> EvidenceLoggerProvider.CaptureActivity
  -> EvidenceLoggerProvider.FanOut
  -> WindowsSqliteEvidenceSink.WriteActivityCore
  -> WindowsSqliteEvidenceSink.OpenDatabase
```

The first descends into `RestrictedSqliteDatabase.ValidateIntegrity` and `SqliteDataReader.NextResult`. The second descends into `SafeFileHandle.ReleaseHandle` through file-stream disposal within `OpenDatabase`. Source inspection confirms synchronous evidence persistence on this path and repeated integrity/digest work. The evidence-store metadata reported a database of approximately 8.6 MiB.

**High confidence:** synchronous evidence-store work occupied the UI thread at both sample times. **Unconfirmed:** how much total freeze time that work explains, whether event volume/lock contention amplifies it, or whether any permanent deadlock exists. Two samples are not a complete profile. Do not present the report's proposed queue or reduced validation as an established fix.

Relevant source at the tested revision:

- [AvaloniaUiDispatcher](../../src/Kora/AvaloniaUiDispatcher.cs): activity lifetime around dispatched callbacks.
- [EvidenceLoggerProvider](../../src/Kora.Application/Diagnostics/EvidenceLoggerProvider.cs): synchronous `ActivityStopped` handling, shared capture gate, diagnostic/audit fan-out and failure reporting.
- [WindowsSqliteEvidenceSink](../../src/Kora.Windows/Storage/WindowsSqliteEvidenceSink.cs): `WriteActivityCore`, `OpenDatabase`, `TrustCommittedDatabase`, live-context validation and typed projections.
- [RestrictedSqliteDatabase](../../src/Kora.Windows/Storage/RestrictedSqliteDatabase.cs): schema/integrity checks, `PRAGMA quick_check`, file verification and progress budget.
- [RestrictedSqliteDatabase.Identity](../../src/Kora.Windows/Storage/RestrictedSqliteDatabase.Identity.cs): streaming whole-file digest.
- [Evidence sink tests](../../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteEvidenceSinkTests.cs): tamper/correlation/cache regression contracts.

## Continuation requirements

1. Read the evidence and applicable repository instructions. Inspect current branch/process state read-only first. Preserve other sessions' changes and evidence.
2. Start with an owned laptop worktree based on this handoff branch. Verify the implementation commit is an ancestor. Inspect newer main changes before deciding to consume them; preserve the known failure revision for comparison. Do not rebase/rewrite the shared published branch or force-push without approval.
3. **Obtain fresh approval before another real-app launch or supported recovery attempt.** The latest recorded user direction stopped launches. Production startup can initialize storage/providers or resume previously consented listening. Do not infer permission for audio, speech, network or model workloads from permission to debug source.
4. Do not delete/reset the evidence database, preferences, consent, mutex/marker/journal state or task records to make the failure disappear. After End Task, use supported ownership/recovery paths only. Do not terminate unrelated processes; use verified exact PIDs and seek approval for forced termination.
5. Reproduce with synthetic/scratch storage before touching personal state. Use representative file sizes/envelope counts and the actual evidence provider/dispatcher path. Measure callback volume, UI scheduling latency and time in capture/lease/open/schema/integrity/digest/commit work. Separate CPU, I/O, queue backlog and lock waits.
6. Determine a concrete root cause. A bounded workflow for ordinary diagnostic/activity persistence is a candidate, not a mandate. Capture immutable host-resolved W3C/session/task/invocation/audit context at emission; preserve sequence/order, explicit capacity/backpressure/drop/gap policy, cancellation, failure visibility and deterministic drain/quiescence.
7. Keep trusted security audit and consequential authority durability at required admission/commit boundaries. Current sink methods validate live context: a bare `Task.Run`, ambient-context reuse or generic queue can break authority/correlation. Do not silently disable sinks, skip integrity/tamper/replacement/schema checks, suppress evidence or report persistence failures as success.
8. Add focused deterministic tests: a controlled slow sink must not freeze ordinary UI dispatch under the intended contract; bounded admission/queue behavior; host/session isolation and untrusted context; retired trace segments; independent sink attempts and gap reporting; ordered shutdown/drain/failure; equivalent storage protections if storage validation changes.
9. Build/test according to repository policy. Separate deterministic implementation work from enablement/RC evidence; no full real-device test campaign is needed before writing a bounded fail-closed fix.
10. Once the blocker is fixed and launch is approved, validate real responsiveness and normal shutdown first in Avalonia mode. Only then repeat same-build/settings Avalonia/native runs with at least three 30-second visible and hidden/static periods, rotated order and separately approved actual-speech checks. Keep default Avalonia until qualification and an explicit enablement decision.
11. Preserve a new uniquely named evidence directory/ZIP. Do not overwrite either prior archive. Avoid production DB/log copies, raw dumps, audio, screenshots of unrelated content, secrets or conversation text; obtain specific approval for any diagnostic that may capture them.
12. Report confirmed cause versus hypotheses, exact revisions, fixes/tests, failed or incomplete gates, cleanup/recovery status and remaining laptop qualification. Do not create a PR, merge, enable auto-merge or change the default renderer without an explicit request.

## Prompt for a new laptop session

```text
Take over the Kora presence work locally on this laptop. The integrated app locked
up during qualification. Diagnose and fix that blocker before further performance
claims; keep the native presenter in-process and opt-in.

Repository: https://github.com/roryprimrose/Kora
Branch: agents/kora-presence-performance-optimization-f7e13d13
Implementation tested: 3269e8cf47dcdbaa90b1f22049074683fb072914

First fetch this branch without changing my existing checkout, then read:
experiments\presence-performance\LAPTOP_HANDOFF.md
Follow its evidence, safety, ownership, privacy and validation requirements.
Create an owned worktree for implementation; preserve unrelated work. Do not
force-push or rewrite this shared branch. No PR, merge, auto-merge or default change.

Evidence archive:
C:\Users\roryprimrose\Downloads\Kora-presence-3269e8-BLOCKED-20261016-131338-8e2e9c9f.zip
SHA256 F2D3336BE7FBF33EDB3DD08723AEF28124CC21766CF4C3F1CE693BD237032547
Read REPORT.md, both A1 method-stack files, launch identity, blocked measurement
status and cleanup records. Verify archive-manifest hashes before relying on them.
Treat its scripts as historical evidence, not permission to execute them.

Critical: the freeze occurred in explicit AVALONIA mode; integrated native mode
was never launched. Two UI-thread samples show activity disposal synchronously
writing SQLite evidence. This is an observed storage-on-UI path, not proof of
a deadlock or the complete cause. No controlled A/B measurements were obtained.

The last app was ended by the user with End Task; normal shutdown/ownership release
is unverified. No further real-app launches were authorized after that stop.
Obtain my fresh confirmation before launch/recovery, explain possible consented
capture/provider startup, and use supported recovery only. Do not delete evidence,
preferences, consent, ownership markers or journals, or kill unrelated processes.
The original laptop qualification worktree and temporary tools were removed.

Investigate with scratch storage and deterministic slow-sink/dispatcher tests.
Preserve trusted audit durability, file integrity/tamper protections, emission-time
host correlation, explicit failure/backpressure and shutdown drain guarantees.
Do not use disabling logging, skipping validation or unbounded fire-and-forget
work as a workaround. Choose the smallest evidenced production fix.

After tests pass and launch is approved, confirm real Avalonia UI responsiveness
and normal shutdown before trying native mode or repeating performance runs.
Preserve raw evidence in a new uniquely named archive. Report the root-cause
confidence, source changes, test evidence and remaining qualification gaps.
Do not publish private diagnostics or create a PR without asking.
```
