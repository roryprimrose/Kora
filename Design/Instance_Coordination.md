# Single Active Instance and Version Handoff

Status: required from Slice A. Only one Kora instance may interact as the assistant, listen, or dispatch work within a Windows interactive-user scope.

Related: [Architecture](Architecture.md), [Lifecycle](Task_Lifecycle.md), [Distribution](Distribution_And_Updates.md), [Mouse Interaction](Interaction_Fallback.md).

## Scope and Identity

Use one host-owned coordinator namespace across installed releases, developer checkouts, architectures, and activation names.
Scope coordination to the Windows user SID across that user's interactive sessions; another user's instance cannot accept or control this user's handoff.
Only the selected authoritative unlocked interactive session may capture/interact; unsupported multi-session ambiguity denies capture and requests explicit resolution.
Do not key exclusivity on executable path, assembly version, checkout, or custom wake name.
Non-interactive installer/hooks must not acquire assistant ownership or start microphone/UI work.

Identify an incoming build using application version, source revision/build ID, binary content identity, release/debug mode, deployment identity, and canonical executable location.
Show a readable label such as "Kora 0.3.0, Debug, commit abc123, checkout Q:\Repos\Kora" with expandable exact details.
Never treat caller-supplied label/path/version as authenticated identity: the Windows coordinator validates the peer process and obtains image identity independently.
"Same version" means the same compatible build/content identity, not merely identical assembly version text.
Debug/release or rebuilt developer binaries with the same semantic version but different content follow the different-build path.
Unknown identity/protocol incompatibility reports an explicit error and prevents takeover.

## Same Build Launch

Before opening databases for migration, registering tray icons, starting providers/workers, or acquiring audio, the new process checks ownership.
If the same build is active, send a bounded authenticated Activate Existing request.
The existing process reveals its status/questions window, subject to Windows foreground restrictions and lock privacy.
The new process exits after acknowledgement; it does not replay startup/task arguments, enable listening, duplicate tray icons, or execute work.
If locked, acknowledge presence without displaying sensitive content; expose details only after unlock and user activation.
Timeout is not proof the owner is dead: report an unreachable instance and exit without competing capture/execution.

## Different Build Launch

The incoming process remains a waiting startup candidate, without assistant services, recording, task tools, migrations, or a second assistant tray.
The active instance asks:

> "Kora 0.3.0 Debug (commit abc123) wants to run instead of this release. Shut down this instance and allow that build to run?"

Use a native question with exact validated incoming/original identities, consequences, Accept/Decline, and normal prompt expiry.
Speech is optional and follows current call/private-output rules; no microphone/model is needed to answer.
When the active assistant can hear the user, the same exact host proposal accepts deliberate voice confirmation; the waiting candidate never acquires its own microphone to obtain approval.
Default is no change. Lock, expiry, rejection, candidate exit, or stale identity leaves the original active.
The question offers "Wait until current work finishes" or explicit cancellation of affected work; switching cannot silently kill work.
This host handoff grants a running local candidate ownership only, not trust in arbitrary executable content, permission to install code, or tool/egress grants.
The developer has already chosen to launch the debug executable externally; Kora does not build or launch it from a model request.

After approval:

1. Bind a single-use handoff ticket to original/candidate process creation identities, validated image digests, user, coordinator epoch, and expiry.
2. Hold queue admission/dispatch and resolve active work explicitly through existing cancellation/quiescence rules.
3. Revoke pending grants, withdraw dispatch eligibility/ephemeral context, durably record interrupted/unknown outcomes for all sessions, stop playback/capture, invalidate audio callbacks, and release workers/mutable-store handles. History remains in its original data partition.
4. Stop the original assistant host. A minimal lifecycle-only supervisor may remain for handoff/restart monitoring; it is not an active Kora assistant.
5. Transfer exclusive ownership through the coordinator only after actual quiescence; the candidate acquires ownership and acknowledges startup readiness.
6. Candidate starts normally with its own verified configuration/readiness and
   applies the [saved-consent microphone matrix](Security_Data_Flows.md#microphone-consent-and-enablement-matrix).
   Handoff approval does not supply missing ongoing consent. No grants, tasks,
   owner-confidence caches, or voice-session state transfer.

One candidate/handoff may be pending; competing launches receive a truthful pending/busy response, not independent approvals.
Ownership transfer, app/power restart, update activation (when available), and source/deployment maintenance share a lifecycle barrier.
Do not broaden the current unsigned-release notification-only update policy through a handoff.
The original installation is not uninstalled or replaced by the debug session.

## Return to the Original

An out-of-band, host-owned lifecycle supervisor observes the candidate's actual process lifetime, not a model callback or PID alone.
After candidate exit/crash and verified release of ownership/resources, it presents:

> "Kora 0.3.0 Debug has exited. Start the original Kora 0.2.0 release again?"

This is a minimal native lifecycle prompt, not a second assistant: no wake detection, TTS, model/tools, queue processing, or background task work.
Voice cannot answer while no assistant owns capture; this physical availability limitation uses the explicit mouse/keyboard return route rather than creating a hidden second listener.
When locked/disconnected, defer sensitive display until eligible; never start or listen automatically.
Accept revalidates the original canonical launch identity/digest and availability, launches that exact original unprivileged with no task arguments, and waits for normal exclusive-owner/readiness acknowledgement.
If another assistant has become active, do not launch a competing original; explain the changed state.
Decline closes the return offer/supervisor and leaves no assistant running.
Dismissal does not restart; the non-sensitive tray return offer may remain until accepted/declined or sign-out.
No launch loop, automatic timeout acceptance, or crash-triggered automatic restart.
Changed/deleted original executable, incompatible settings/schema, lost access, or failed startup yields an actionable error, not a different executable fallback.
Nested handoffs are initially refused until the existing return transaction is completed or explicitly abandoned.

The supervisor stores only bounded transaction/launch identity and status metadata, not conversations, credentials, approvals, or biometric data.
It has no general process-launch API: only the exact original launch identity in the user-approved transaction.
Restart is performed by the user-level supervisor, never an elevated installer identity.
If supervisor monitoring fails, report the loss of automatic return offer where possible and retain normal manual launch; never claim the offer is guaranteed after sign-out/reboot.

## Coordinator and Data Safety

Provide a portable coordination contract with a Windows implementation using OS-backed exclusive ownership and authenticated bounded local IPC.
Use restricted per-user object/pipe permissions, validated peer process/user/session identities, protocol versions, generation counters, and handle-based lifetime checks to avoid PID reuse and stale tickets.
Named mutexes alone do not authenticate IPC or prove workers stopped.
No remote IPC listener, arbitrary command-line forwarding, executable payloads, unrestricted path launching, or model-visible handoff ticket.
Same-user compromised programs and administrators remain outside the existing host-integrity threat boundary.

Owner death invalidates its tickets, but does not prove orphaned workers/remote effects are quiescent.
Use existing owned-worker lifetime containment and reconcile uncertainty before enabling overlapping execution; never kill unrelated processes by name.
Release failure or unknown worker effects blocks takeover and reports a blocker.
The lifecycle supervisor cannot reserve assistant ownership permanently after decline/failure.

Debug and installed builds share the coordination domain but must not mutate the same data concurrently.
Developer runs default to an isolated device-local development data partition, not production credentials/database/schema or enablement.
Any explicit shared-store experiment requires compatibility checks and normal data-mutation consent; handoff consent alone never authorises production migrations.
Resume the original only when its store remains compatible; keep original data outside the candidate's default write scope.

## Test Requirements

### R03 implementation boundary

`Program.Main` enters the Windows coordinator before application paths, log
creation, service composition, migrations, tray or audio. Core contracts live
in `Kora.Core.Coordination`; Windows process authentication, restricted
SID-scoped global mutexes, bounded current-user-only pipes and native questions
live in `Kora.Windows.Coordination`. The desktop binds
`DesktopInstanceOwnershipBridge.BindCallbacks` for reveal and safe quiescence.
The original retains exclusive ownership until the desktop lifetime has ended
and the service provider is disposed; a callback alone never releases it.
The bridge also requires an admission-only `abortHandoff` callback before it
will prepare a different-build switch. Failed/cancelled preparation awaits the
actual callback's completion before undoing its own handoff hold; a timed-out
IPC waiter cannot race a late preparation. Abort does not restore audio,
playback, revoked approvals or another lifecycle hold. An abort failure blocks
future preparations and requires explicit recovery.

Authentication obtains SID, session, process creation time and canonical image
from retained OS handles. Build identity hashes the apphost, application
metadata, managed DLL closure, dependency and runtime configuration files,
retaining read-only image handles. Unsupported images (including `dotnet.exe`
launches/single-file layouts), elevated processes, unknown debug/release
metadata, cross-session peers and unproven identities deny explicitly.
Same-build acknowledgement never forwards startup arguments or grants.

The desktop callback must refuse active tasks, setup, model or speech-install
work, uncertain remote effects or owned workers that cannot actually stop.
The native Yes action requests an idle, safely quiescent switch; it does not
silently cancel active work. No leaves ownership unchanged so the user can retry
after current work finishes. Prompt expiry, secure desktop, disconnect, ambiguity or candidate
death revoke the proposal. Only one proposal/local restart is pending.
Application restart is deferred through the same lifecycle controller rather
than launching a secondary that merely activates an owner about to exit.
Host cleanup captures the first exception and rethrows it only after cleanup and
coordinator disposal; a shutdown failure cannot mask an earlier host failure or
claim verified clean disposal.

After disposal, the original process may remain only as an out-of-band native
lifecycle supervisor, with logging closed and no composed services. It retains
the replacement process handle and the exact original launch identity; return
requires a fresh native acceptance, unchanged original files, eligible session
and free ownership, followed by normal startup acknowledgement. No automatic
restart, arbitrary executable fallback or nested replacement is permitted.
Closing/expiring the return offer ends supervision; a locked offer is deferred.

Debug local and preference data default to device-local `Kora\Development`
(even the roaming-path contract resolves locally), separate from release
`Kora`. The verified entry build, not a possibly mixed Core assembly's debug
flag, selects that partition. Coordination alone uses the shared device-local
`Kora\Coordination` directory. Its restricted `unclean-owner` file is one
non-sensitive dirty bit (no sessions, identities, tickets or grants), cleared
only after verified clean disposal. It persists when the final mutex handle
vanishes after a crash: mutex disappearance cannot prove orphaned workers or
remote effects stopped. Crash/uncertain disposal therefore blocks takeover and
return pending explicit manual reconciliation/removal of that marker; it never
authorises killing or automatic recovery.

Focused portable transaction/framing and uniquely named OS-object tests do not
launch the assistant, display questions, capture audio, change session state or
take over the production coordinator namespace. Actual simultaneous desktop
activation, lock/takeover/return, worker-crash and deployment/architecture trials
remain separate user-confirmed validation gates.

- Simultaneous same-build launches yield one assistant/tray/capture owner and reveal the existing window; only acknowledged secondary processes exit successfully.
- Different semantic version, debug/release, and changed binary under unchanged version text yield the exact-identity handoff question.
- Rejection, expiry, lock, missing microphone, incompatible protocol, candidate death, stale/forged tickets, and unreachable owner never create a competing assistant.
- Approved transfer cannot start new capture/tools until old host/owned workers are quiescent; remote uncertain effects remain explicit blockers.
- Test multiple competing candidates and lifecycle/update/power races; at most one owner performs assistant work.
- Return prompt appears after replacement normal exit/crash, works entirely by mouse, and cannot restart under lock or without explicit acceptance.
- PID reuse, original binary replacement, supervisor failure, lost IPC, startup failure and another-owner acquisition produce truthful recovery without arbitrary launches.
- Debug data is isolated; no production migration/grant/task/microphone-consent reuse across handoff.
- Restarted original acquires ownership normally and never resumes ephemeral
  work or transferred capture. After explicit return acceptance, its own saved
  ongoing consent may permit ordinary fresh-gated startup listening under the
  microphone matrix; the supervisor never listens or transfers consent.
