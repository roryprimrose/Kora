---
name: kora-exit
description: 'Gracefully stop the owned Kora instance through authenticated named-pipe IPC before an approved exit, restart or rebuild; verify actual process exit without tray automation or implicit force-kill.'
---

# Kora Exit

## Runtime and authority

Local-only, Windows. Use the owning Kora checkout, pinned .NET SDK and current
interactive Windows user. No credentials, elevation, installer launch or package
maintenance reservation are required.

Invoke only for a user-authorized stop or a restart/rebuild whose authority
explicitly includes stopping the running app. Loading this skill does not grant
permission to discard unsent input, cancel submitted work, force-kill a process
or restart another checkout's app.

Prefer this supported operational IPC command over tray/window automation.
It is a command-line branch before desktop startup, not a way to drive UI
through a shell. Respect IPC authentication, rejection and timeout results;
do not bypass them through another identity or transport.

## Preflight

1. Resolve the owning repository/worktree and intended configuration (normally
   Debug). Do not use another session's checkout or blindly choose a process
   named `Kora`.
2. Ensure the compiled client and running owner support `--exit` and match the
   intended executable and build. A source file containing the flag is not proof
   that a stale binary implements it. Use current build evidence.
3. If this is the first deployment of the exit feature, obtain a one-time normal
   exit/rebuild/relaunch. Never try to build over running pinned application
   images or rename another executable to impersonate `Kora`/`Kora.Setup`.
4. If checking process state, select by exact verified executable path and retain
   the PID/lifetime. Other users, sessions, checkouts, installed copies and
   lifecycle supervisors are not interchangeable targets.

## Invoke

From the exact owning checkout, use the already built first-party apphost:

```powershell
dotnet run --project .\src\Kora\Kora.csproj --configuration Debug --no-build --no-restore -- --exit
```

For an explicitly selected Release owner, replace Debug with Release. Do not
silently switch configuration because authentication or target matching failed.
`--no-build --no-restore` is intentional: stopping the app must not replace its
running binaries or contact package feeds.

The client uses Kora's existing per-user named-pipe namespace. Both sides verify
the current user/session, canonical executable, exact build and retained process
lifetime. The request has no task, launch, ticket or ownership-transfer payload.
It invokes normal Exit, not installer maintenance or forced termination.

Idle/empty input exits without an extra confirmation. Existing unsent-input,
definition-editor and submitted-work reviews remain authoritative. Do not
automatically accept those reviews. If a human must decide and is unavailable,
report the unresolved review and stop.

## Verify the result

- `EXITED: processId=...; processExitCode=...` means the authenticated target
  process actually exited. Acknowledgement or pipe closure alone is insufficient.
- `NO_OWNER` means no assistant ownership was observed at the probe. It does not
  prove that every Kora supervisor/process exited or that prior cleanup was
  clean. Before replacing binaries, verify the intended executable has no live
  process or pinned images.
- Command exit code `0` accompanies an observed no-owner state or target exit
  code zero. Report which result actually occurred.
- Command exit code `1` means graceful exit was not confirmed; `2` means invalid
  arguments; `3` means the target exited with a failure code. Do not relabel any
  of these as clean success.
- A timeout may leave a native review or shutdown operation pending. Do not
  launch duplicate requests, assume cancellation, or build over the owner.
- Clean ownership/resource release is separate from process exit. Never remove
  unclean-owner, recovery or installer-maintenance markers to manufacture it.

After shutdown, a later launch still needs the applicable user authority.
This skill does not monitor the app in the background.

## Failure handling

An older owner can reject the new operation. Report incompatibility and request
a normal one-time exit/update; do not send `InstallerStop` as a generic client,
fake a setup executable, downgrade the protocol or clear ownership state.

If normal Exit cannot complete, report the exact outcome and current target
state. Force termination requires separate explicit user authorization. If
authorized, revalidate the executable and process lifetime immediately before
using `Stop-Process -Id <literal-verified-PID>`; never kill by process name.

Keep captured receipts under the owning checkout's ignored `.scratch` directory
according to the repository scratch policy. Do not capture audio, transcripts,
conversation bodies, credentials or private review contents.

## Examples

Correct: the user approves restarting this worktree's app; invoke its current
`--exit` client, let required reviews complete, verify the target exited, then
perform the separately authorized build/relaunch.

Incorrect: invoke the installer to close an app, accept its consent on the
user's behalf, treat `StopRequested` as success, or force-kill after a timeout.
