# Troubleshooting

## Local storage or model setup needs attention

Open Settings > Readiness or use **what do you have left to do** to see the
current setup task and blocker. Bootstrap readiness can recreate missing setup
directories and initialize the setup-only SQLite schema. If SQLite fails its
integrity check, the existing database is retained; restore a known-good
backup rather than deleting it. The local Ollama probe checks only
`127.0.0.1:11434`. A responding runtime or installed model does not mean
Kora can use it for reasoning until the pinned digest and actual model
inference check pass. Select **Review local model setup** and approve the pinned
Ollama 0.35.1 / qwen3:1.7b plan to install and verify them. Setup can be
cancelled with **cancel task**. A model with the selected name but a
different digest is not replaced automatically; inspect it before taking
further action. Built-in commands remain deterministic after inference passes;
other requests are answered by the verified local model. If a running request
fails, Kora marks inference unavailable until you refresh readiness. There is
no cloud fallback. Missing inference opens the Readiness tab on startup.
PowerShell has its own setup task, but an unhealthy PowerShell installation
does not block local reasoning or the C# built-in commands.

## Private durable storage cannot reopen after interruption

The consolidated private task/question/authority-audit database and independent evidence database retain their
rollback journals. A valid interrupted transaction is rolled back when the
production store reopens; committed records remain committed. Startup recovery
marks intent-only work **Interrupted** and dispatched work without a verified
receipt **Unknown**, without rerunning it or treating an approval as an effect.

Missing databases/journals, unsupported or corrupt schemas, invalid private
permissions and unavailable storage ownership/access are explicit blockers.
Kora does not recreate a missing journal or silently repair its ACL. Do not
delete or replace database/journal files to clear the error: retain the related
files and review the reported storage failure with support. Do not copy a
journal from another database. Daily JSON diagnostics remain an independent
source when SQLite is unavailable. These recovery checks are not a guarantee
against physical power loss or a complete backup/restore workflow.

The validated upgrade freezes and retains the legacy task ledger before
consolidating complete IDs/events with existing questions, generations,
metadata, grants and audit. An interrupted migration can revalidate and
complete storage maintenance, never replay work. A missing consolidated store
cannot be rebuilt from that retired snapshot; retain both partitions for
explicit support recovery, rather than deleting files to force initialization.

For **task cancellation unavailable**, use `task inspect <session-id> <task-id>`
or native **Inspect exact selected task**, then copy every current conflict
token. Only admitted current-run local-version work still waiting for its
native question before dispatch is cancellable. Already answered/dispatched,
terminal, Unknown, expired or prior-run work is not reported stopped. Resolve
privacy/ownership/channel failures and initiate a fresh action; uncertain
commit/receipt failure is inspected, never automatically retried.

## PowerShell 7 is missing or failed its check

Open **Settings > Readiness** or ask **what are you currently working on**.
The PowerShell 7 setup task reports whether `pwsh.exe` is missing, too old,
or failed its no-profile version check. Select **Review PowerShell 7 setup**
to approve a per-user winget installation, or **cancel task** to stop a
running install. Refresh readiness afterward. A package-manager success is
not sufficient: Kora must verify PowerShell 7.4 or later before reporting
the task complete. If a corrupt installation or package-manager failure
persists, review the reported error and repair the installation manually.
Kora does not run user-created scripts during this check.

## Kora cannot hear commands

1. Confirm an English Windows speech recognizer is installed.
2. Open Settings, then **Speech and audio**.
3. Confirm the microphone card says Windows desktop-app microphone access is
   allowed. If blocked, select **Open Windows microphone settings** in Kora,
   then enable microphone and desktop-app access.
4. Open **Choose microphone (native recovery)** from the tray or speech Settings.
   Refresh devices, confirm **System** has an active Windows default microphone,
   or highlight a specific active endpoint and choose **Save preference only**.
   Duplicate names are distinguished by exact endpoint IDs; unavailable pins
   are retained, never automatically replaced. This does not test or record.
5. Read the Voice activation status. It identifies a locked-session safety
   pause, call-policy pause, or manual disablement.
6. Select **Refresh microphones** (no model/network/speech dependency).
7. Review saved voice consent. After manual disablement, lock, disconnect,
   suspend or device/permission loss, select **Enable listening** explicitly.
8. Hold **Push to talk**, use an exact phrase, then release.

The recovery card cannot grant Windows permission or combine new consent with
selection/enable. Missing/unknown permission, ownership or session state fails
closed. Review the explanation and existing Settings consent separately. A stale
choice or refresh timeout requires a fresh refresh/input; late results cannot
restore closed card authority. Closing/Escape grants nothing and cancels no task.

Production wake is unavailable in this build. Safe startup with saved consent
arms push-to-talk; it never opens an ambient command recognizer.

## The Windows default microphone changed

Leave the Kora microphone on **System**. System follows Windows default changes,
including changes during active capture. A specifically named microphone stays
pinned until you select System or another endpoint.

## Kora does not speak

Check:

- the effective response mode is not **Visual only**;
- listening is disabled;
- a compatible Windows voice is selected;
- System has an active Windows default output, or a specific output is selected;
- Windows output is not muted and volume is above zero; and
- **Kora playback volume** is available and above zero; and
- detected-call policy is not forcing visual responses.

Use **Preview voice** to test the selected voice and output. Kora cannot always
detect powered-off speakers, disconnected analog cables, or unreported hardware
mute.

`status speech.playback-volume` reports the separate Kora-only percent and
saved/default/unavailable source. Default/reset is original unscaled 100;
zero intentionally prevents synthesis and keeps the full result visual.
Use Settings > Speech & audio > **Refresh volume preference only** after
repairing invalid/unreadable saved state or failed audit/readback evidence.
A file may already be committed when terminal evidence fails; do not infer
rollback or automatically retry. Save/reset never tests playback, replays
stopped speech, opens input or changes Windows/call volume. If a playback
adapter lacks qualified owned-gain support, continue visually. These software
checks do not establish physical audibility or complete acoustic acceptance.

## The Windows default speaker changed

Leave Kora audio output on **System**. System follows Windows default changes,
including changes during active playback. A specifically named output remains
pinned.

## A saved device disappeared

Kora does not replace a pinned device silently. Microphone recovery can choose:

- **System** to return to Windows default routing; or
- another explicit endpoint.

For audio **output**, reconnect the exact saved endpoint and refresh, or explicitly
choose a fresh presented endpoint and **Save output preference only**. **Reset output
to System** removes Kora's override; it does not change the Windows default.
No alternative is silently substituted. `list output settings` and
`status speech.output-device` show saved/effective/unavailable state and exact IDs.
Stale choices, changed owner/privacy/call/input revisions, detection/persistence/
audit failures require explicit refresh/recovery. A file may be committed before
terminal evidence fails: inspect before a fresh request, not automatic retry.
Continue visually if output cannot be confirmed; recovery never starts a trial,
replays speech or opens a microphone.

## No speech voice is available

For the Windows provider, install a Windows text-to-speech voice or language
speech pack, then refresh readiness. For Kokoro, select the provider and choose
**Download**. It becomes available after validation and preparation without a
restart. Typed commands and visual responses remain available.

Kokoro is optional and is not queued automatically. The first-run offer opens
Settings only if you choose to review it; downloading still requires selecting
**Download**. If a previously selected provider disappears, Kora offers a
recovery prompt once per loss episode rather than downloading it silently.

If a Kokoro download fails, Kora leaves it unavailable and removes temporary
asset files. Check network access and free space, then try Download again.
Kora will not activate an asset that has the wrong size or SHA-256 digest.
Validation errors identify the affected asset and report both the expected and
received digests so a release-pin error can be distinguished from a modified
or corrupted download.

## VoiceOnly still shows text

This is expected when speech is unavailable, playback fails, capture is active,
a detected call forces visual output, or Kora must show safety or recovery
information.

## Call settings appear inactive

Automatic call detection is not implemented in the current Windows bootstrap.
The policy is ready for a supported detector, but an open communications
application is not treated as proof of a call.

## Open Kora after hiding it

Single-click the tray icon or use **Show Kora** from its right-click menu.
Double-click opens Settings.

## Find diagnostic logs

Open:

`%LOCALAPPDATA%\Kora\Logs`

Logs are structured JSON, roll daily, and are retained for up to 30 days.
The tray's **Evidence (read-only)** source **DailyLog** can inspect an independent
bounded prefix of existing daily diagnostic envelopes even when SQLite is
unavailable. **All** remains SQLite-only. A scan limit is not a complete-file
search; missing/changed/expired snapshots require a fresh search, while corrupt
or truncated data is reported without an empty-success fallback. Audit mirrors
and activity/legacy copies are unsupported and counted explicitly. See
[privacy and evidence limits](privacy-safety-and-logs.md) for exact bounds.
