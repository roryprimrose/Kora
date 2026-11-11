---
name: kora-ui-iterate
description: 'Start a local Kora UI design session in the VS Code Agents window; build Debug hot reload once, inspect the real window, then edit/save/inspect XAML without rebuilding on every change.'
---

# Kora UI Design Iteration

## Start in the Agents window

Use a **local Windows Agent session** with access to the interactive desktop,
not a cloud session. Select the intended Kora checkout/worktree and verify the
session's actual working directory; do not assume it inherited the editor's
folder. Keep one session responsible for the running app and UI files.

Paste this prompt, replacing the window and design issue:

```text
Use the repo's kora-ui-iterate skill to start a live UI design session for
<window or screen>. If the skill is not listed, read
.github\skills\kora-ui-iterate\SKILL.md and follow it.

The first design issue is: <what is wrong and what should improve>.
You may build and launch this checkout's Debug hot-reload app, inspect that
window, edit its UI, and gracefully stop/build/relaunch it when required.
Keep the app open between design iterations.

Use non-sensitive content. Do not submit requests, activate voice, change
privacy/model preferences, discard input/work, force-kill, commit or publish.
If existing startup settings would activate voice, resolve that before launch.
For each clear follow-up, apply the change, save and inspect the actual window
before reporting back. Do not rebuild for every XAML-only adjustment.
```

Mention `kora-ui-iterate` in a new session or explicitly attach/read this file if
customization discovery has not indexed it. A file on disk does not prove that
the current host loaded the skill or has desktop-control permissions.

## 1. Establish the scenario and access

- Name the target window, visible state and concrete improvement. Use the
  [window contract](../../../Design/UI_Workspace_And_Windows.md) and applicable
  repository instructions. Improve existing interactions; do not add unrelated
  features or revive retired screens.
- Discover the host's local Computer Use tool and follow its returned
  documentation and approval flow before launching or inspecting UI. After
  context loss, recover its documentation before continuing. Once using it for
  app UI, keep UI launch, navigation, focus, resizing and observation on that
  server; do not substitute shell/Win32 UI automation.
- If desktop tools or permission are unavailable, say so. The user can launch
  and navigate manually and provide non-sensitive observations/screenshots.
  Source inspection or headless tests alone are not a live visual check.
- Check the exact running executable and owning checkout before any stop.
  Kora instance ownership is per user; another worktree/installed copy is not
  an isolated design host. Do not stop or take over another owner's app.
- Confirm stop/build/launch authority. The starter prompt covers those actions
  for this checkout, not native reviews, discarding work or force termination.
  If a required decision receives an automatic/unavailable-user response,
  stop the dependent action; do not treat it as approval.

This is the **real application**, with normal services, saved preferences and
local data, not a sandbox or mockup. Startup may automatically listen under
existing voice settings. Do not promise microphone-free operation, change those
settings or invent a no-audio flag. Resolve any conflict with the user's limits
before launch. Never trigger PTT, speech, model work or approvals merely to
populate a design state. Use an empty/non-sensitive state or obtain specific
authority for a representative interaction.

## 2. Prepare once and launch

Reuse a verified current hot-reload host from this checkout when suitable.
Otherwise stop the intended owner with the [exit skill](../kora-exit/SKILL.md)
and verify actual process exit before rebuilding. A timeout or `NO_OWNER` alone
is not proof that the intended binaries are unpinned.

Use the pinned SDK from the checkout root. Do not use `run.ps1` for this loop:
it can pull upstream and does not opt into hot reload.

When entering/switching hot-reload mode or restoring missing assets:

```powershell
dotnet restore .\src\Kora\Kora.csproj --locked-mode -p:KoraHotReload=true
```

Build once before launch, or after a change that requires rebuilding:

```powershell
dotnet build .\src\Kora\Kora.csproj --configuration Debug --no-restore -p:KoraHotReload=true
```

Follow current machine feed/authentication instructions with per-command source
overrides where required; do not commit machine-specific feed configuration.
Run finite commands in the host's visible terminal, read their final result,
and stop on failure. Do not build over running pinned images.

Resolve this executable to an absolute path under the owning checkout and
launch it through the approved desktop tool:

```text
src\Kora\bin\Debug\net10.0-windows10.0.19041.0\Kora.exe
```

Verify the intended process and completed startup (usable window/tray controls),
then open the requested screen through its existing UI. A process briefly
appearing, or a stale tray icon, is not readiness. Do not repeatedly launch a
tray-only app or reuse a previous launch's window identifier.

For manual startup without desktop automation, the
[contributor workflow](../../../CONTRIBUTING.md#live-xaml-design-iteration)
also documents `dotnet run`. Do not launch both routes.

## 3. Edit, save, inspect

1. Observe the actual target first: hierarchy, spacing, clipping, reading order,
   primary actions, focus and the user's reported problem. Keep any design
   proposal short and concrete; act directly on clear requested changes.
2. Locate the owning view/styles rather than patching every window separately.
   Common starting points:

   | Surface | Source |
   | --- | --- |
   | Compact chat and response shell | [ResponseWindow.axaml](../../../src/Kora/ResponseWindow.axaml) |
   | Expanded conversation/session workspace | [SessionInteractionView.axaml](../../../src/Kora/SessionInteractionView.axaml) |
   | Settings content and shell | [SettingsView.axaml](../../../src/Kora/SettingsView.axaml), [SettingsWindow.axaml](../../../src/Kora/SettingsWindow.axaml) |
   | New-request content and shell | [NewRequestView.axaml](../../../src/Kora/NewRequestView.axaml), [NewRequestWindow.axaml](../../../src/Kora/NewRequestWindow.axaml) |
   | Shared button appearance | [ButtonStyles.axaml](../../../src/Kora/Controls/ButtonStyles.axaml) |
   | Application resources | [App.axaml](../../../src/Kora/App.axaml) |

   Compact/expanded mode is one primary window, not two independent app launches.
   For other screens, trace the observed control to its view/controller; do not
   guess from a similarly named historical window.
3. Make one cohesive visual change, **save the XAML**, then inspect the same
   running window. HotAvalonia reloads `.axaml` and shared styles/resources;
   there is no compile/exit/relaunch step for each layout-only edit.
4. Verify that the requested change actually appeared and that adjacent content
   remains usable. Check a narrow and normal window where resizing is supported;
   preserve keyboard focus, accessible names, readable text and action reachability.
   For shared styles, inspect affected representative screens and Light/Dark
   states through permitted UI routes. Do not change system display settings.
5. Report briefly what changed and what was visually verified. Keep the host
   open for the next instruction. Apply clear follow-ups in the same loop;
   ask only when a consequential choice, authority limit or failure needs input.

Do not capture private conversation/settings content in receipts. Use only
non-sensitive screenshots when authorized and useful. Agent-created captures
or notes belong under the owning checkout's ignored `.scratch` directory;
follow repository retention/ownership rules.

## 4. Know when to restart

| Change/outcome | Required action |
| --- | --- |
| XAML spacing, alignment, typography, colours or resources | Save and inspect; no rebuild by default. |
| Structural XAML, named controls, handlers or bindings | Inspect appearance live, but restart before relying on behavior: constructors do not rerun and cached control references/handlers can be stale. |
| C#, view-model/controller logic, DI, project/dependency changes | Graceful exit, appropriate restore/build, relaunch. This workflow does not provide C# hot reload. |
| Invalid XAML or no visible reload | Inspect development output, correct the specific error and verify again; if still stale, perform an authorized restart. Do not claim success from a saved file. |
| Permission, privacy, ownership or resource quarantine blocks UI | Report the blocker. Do not weaken admission, clear recovery state or repeatedly relaunch to bypass it. |

Hot reload is opt-in **Debug only**, with remote reload disabled. Ordinary Debug
and all Release configurations exclude it. A `--no-build` launch can still run
an earlier enabled binary; restore/rebuild when changing modes.

## 5. Finish a design pass

- Record what was actually observed separately from source/test inspection.
  Visual reload is not proof of command behavior, privacy or resource cleanup.
- Before functional validation, stop through the exit skill and restore/build
  ordinary Debug without `KoraHotReload=true`. Launch only with applicable
  authority; never use the live-reloaded view as functional/security proof.
- For layout fixes, add or update focused rendered/headless geometry/interaction
  regressions where appropriate. Follow the
  [development checks](../../../CONTRIBUTING.md#development-checks) and
  [behavioral testing guidance](../../instructions/testing.instructions.md).
  Run covering checks after a cohesive pass, not a full suite for every pixel
  adjustment; preserve all maintained gates and report exact results/unrun checks.
- State whether Kora is left running or stopped and whether edits are
  uncommitted. Do not implicitly commit, publish, discard work or leave a
  claimed background monitor. Agree on any subsequent operation using the
  host's required decision mechanism.
