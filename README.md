# Kora

A Windows voice-first, local-first assistant.

The runnable bootstrap includes an Avalonia desktop shell with an ambient
particle-cloud presence, deterministic C# built-in handlers, Windows speech
and optional local Kokoro speech output, local storage and SQLite, and a
consented, verified Ollama model for unmatched requests. Script-backed skills
and general application launching are not yet available.

## Requirements

- Windows 10 build 19041 or later; Windows 11 is recommended.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) matching `global.json`.
- An English Windows speech-recognition language.
- An installed Windows text-to-speech voice for Windows spoken output, or the
  optional downloaded Kokoro provider.
- A microphone capture device for voice input.

Typed built-in commands and visual responses work without speech hardware. No
model, cloud account, or network connection is required for exact built-in
commands. Unmatched requests require a verified local Ollama model.

Exact **list capabilities**, **describe capability application.get_version**,
**show registry version**, **show dependency readiness**, **list runtimes**,
and **show local runtime status** expose the bounded read-only host registry
without inference. Readiness/runtime results are timestamped recorded probe
observations, not fresh checks or runtime qualification. See
[read-only discovery](docs/commands.md#read-only-host-discovery).

The requirements and commands here describe developer source use. Precompiled
framework-dependent binary users do not need Git or an SDK; see the
[distribution/runtime contract](Design/Distribution_And_Updates.md).
The external [managed-source bootstrap](Design/Distribution_And_Updates.md#source-bootstrap)
offers reviewable preview and explicitly trusted exact-revision build/staging
verification. It does not install, activate, register or launch Kora.

## Run

```powershell
dotnet restore .\Kora.slnx --locked-mode
dotnet run --project .\src\Kora\Kora.csproj
```

On first launch:

1. Review the readiness results. Kora initializes local storage and SQLite
   automatically. If local inference is missing, Settings opens on Readiness.
   PowerShell 7 is tracked as a separate setup task, not a prerequisite for
   built-in commands or local reasoning.
2. Review the microphone and speaker selected from the current Windows defaults,
   or choose Kora-specific overrides in Settings.
3. Review explicit ongoing voice consent in Speech & audio, or continue without
   voice. Safe startup with saved consent arms push-to-talk without recording.
4. Hold **Push to talk**, say **“Kora, what can you do?”** or another phrase,
   then release. Production wake is unavailable; ambient audio is not transcribed.
5. Observe the transcript, matched action, and presence state.
6. Choose **Disable listening** whenever you want to release the microphone for
   the rest of the current run. **Preview** can test the selected local voice
   even while listening is enabled.

The typed command field drives the same router and can prove command behavior when microphone or speech-language support is unavailable.
Exact built-in phrases take precedence over the model. To enable unmatched
questions, choose **Review local model setup** on Readiness and explicitly
consent to the per-user Ollama 0.35.1 installation (if needed) and the
qwen3:1.7b download. Kora checks the pinned model digest and an actual
inference response before using it on loopback. **Review PowerShell 7 setup**
separately requests consent to install or reuse PowerShell 7.4 or later; it
does not authorize a script or gate local inference. Optional Kokoro speech is
offered separately, never added to the required setup queue.

Kora also keeps a system tray icon available while the process is running. Its
context menu uses the configured assistant name for **Show**, **Settings**, and
**Exit**, and also provides **Documentation**.
Windows may place the icon under **Show hidden icons** until the user promotes
it to the always-visible notification area.
After successful startup Kora hides its borderless transparent presence and
continues listening in the background. The presence appears while the user
is interacting with Kora or when Kora has information/results to provide.
Visual text uses a separate compact response surface; configuration remains in
Settings. Closing visible surfaces returns to the background state, while only
**Exit** stops the process.
The presence automatically hides after **10 seconds** of inactivity while idle
or listening, when no prompt needs attention. Active work, speech, approval or
question prompts, recovery actions, and unacknowledged failures keep it visible.
The unpinned response window has its own timeout, default **5 seconds**.
Change **Presence timeout** and **Response timeout** independently under
Settings > Appearance; each accepts 1-60 seconds and applies without restarting.
Hiding the presence does not stop listening or ongoing work.
Drag the response title area to reposition it; its
controls can keep the current response visible until dismissed and can disable
the default stay-on-top behavior. The presence is click-through by default,
so mouse events reach the window underneath. Hold **Ctrl**, then left-click
and drag the visible presence to reposition it; release the mouse button and
Ctrl to restore click-through. Its device-local position is restored across restarts.
Documentation opens the embedded end-user guide from [`docs/readme.md`](docs/readme.md)
in a single themed Markdown window. Its explicit **Open details** action opens
the exact page in a separate bounded native viewer with source/search and
exact-source Unicode copy. This is not durable conversation history or general
response routing; see the
[delivered passive profile](Design/Information_Display.md#delivered-native-profile---2026-10-06).
Settings opens a single
settings window covering the assistant name, speech
and audio devices, listening, local voice, response output defaults and
overrides, detected-call behavior, model-action approvals, and dependency readiness. Detecting
microphones refreshes readiness without enabling capture; Exit releases
listening before closing the application.

A single left-click on the tray icon shows and activates Kora after the
Windows-configured double-click interval. A double-click cancels that pending
single-click action and opens or activates the single Settings window.

The complete end-user guide is maintained under [`docs`](docs). Every
top-level `docs/*.md` page is embedded into `Kora.Application.dll` at build
time, so installed copies do not depend on repository files or network access.
The same Documentation window can be opened from the tray or with the built-in
phrases **open documentation**, **show documentation**, and
**show the user guide**.

The Settings, presence, and response surfaces bind to the same application
state. Changes are reflected immediately across any open surface. Typed setting
mutations also raise the same property notifications, providing the update path
for future validated verbal/model setting commands; those setting commands are
not currently available.

### Appearance

Settings provides **System**, **Light**, and **Dark** application themes.
**System** is the default and follows the effective Windows light/dark
preference, including changes while Kora is running. Light and Dark are explicit
overrides. The selection is stored at
`%LOCALAPPDATA%\Kora\Preferences\appearance-theme.txt`.

Appearance settings also provide live sliders for the presence's overall
size (240-600 px), dot size (50-200%), dot density (25-200%, or 38-300 dots),
and dot movement speed (25-200%). The defaults are 360 px and 100% for each
particle setting (150 dots). Speech sizing follows Kora's actual playback
rhythm, with an on/off toggle and an amount slider (0-200%, default 100%).
At the default amount, the presence contracts to 90% and expands to 112%
of its resting size; switching it off leaves colours and dot motion unchanged.
These device-local settings are stored under `%LOCALAPPDATA%\Kora\Preferences`.

Presence is the feature name; particle cloud describes its current visual
treatment.

The selected effective theme applies immediately to Kora's existing
presence, Settings, response, documentation, and approval surfaces.
Browser, generated-HTML, and diagram result surfaces are not implemented.

## Assistant name

The assistant name defaults to **Kora**. Settings can change it to a validated
1-3 word name of at most 32 characters; letters, numbers, spaces, apostrophes,
and hyphens are supported. Applying a name updates the presence, response,
and Settings surfaces, tray labels and tooltip, command catalogue and prefix, visual
responses, spoken responses, and voice preview immediately. If listening is
already enabled, current capture is invalidated and requires explicit Enable
listening. The next push-to-talk uses the new grammar without new consent.
Names that would collide with a built-in command phrase are rejected.

The configured name is stored at
`%LOCALAPPDATA%\Kora\Preferences\assistant-name.txt`. A rename is custom-only:
the previous name, including **Kora**, is not retained as a hidden command
alias. The product and executable identity remain fixed: the process is still
`Kora.exe`, assemblies and namespaces remain Kora, local data remains under
`%LOCALAPPDATA%\Kora`, and daily logs remain `kora-YYYYMMDD.log`. Name writes
produce content-minimizing security audit events without recording the chosen
name.

## Implemented voice proof

- Application actions: show, hide, exit, restart, settings, setup, help, version, and status.
- Task controls: cancel task and stop speaking.
- Windows session action: lock the current Windows session through a C# handler.
- Protected power requests: recognize, display, inspect, and cancel shutdown/restart proposals.

Shutdown and computer restart execution is intentionally disabled in this bootstrap. Recognition produces a visible non-destructive proposal and never sends an operating-system power request. Locking is a real local action and disables microphone capture before calling Windows.
Direct exact built-in lock commands do not currently request a model-action
approval; model-suggested lock and other disruptive actions do, unless an
action-name session or persistent grant exists. These grants do not bind to a
script or executable hash. The proposed common execution gate, embedded
`.ps1` tasks, content-bound grants, and script-review UI are future work; see
the [skill and task execution design](docs/skill-and-task-execution-design.md).
The verified model can answer, ask bounded questions, propose grant changes,
or suggest a registered built-in action, never an arbitrary command.

The recognizer uses the installed English Windows speech engine only for bounded,
explicitly activated commands. The previous ambient grammar is not production
wake and is disabled on unactivated audio. Production wake selection and
acoustic playback-rejection proof remain outside this package.

The initial text-to-speech adapter uses installed Windows SAPI voices and does
not download voice assets or use a network service. An explicitly selected
voice is stored in device-local application data and takes precedence. Without
a saved choice, Kora prefers a female voice matching the exact Windows profile
culture, then a female voice from the same language family. If no compatible
female voice exists, Kora applies the same locale rules to male voices, followed
by neutral and unspecified-gender voices. It never silently selects an unrelated
language. If no compatible voice is installed, the user can explicitly choose
another installed voice. If no speech pack is available, Kora keeps typed and
visual commands working, reports speech output as unavailable, directs the user
to install a Windows voice, and forces the response panel visible even when
voice-only output was configured.
No ambient recognizer runs during previews or spoken responses. Push-to-talk
stops playback before opening capture; tray Stop speaking needs no recognizer.

Response output can be configured as hybrid, voice-only, or visual-only. The
device default is persisted locally; current-queue and current-task overrides
are transient, with task taking precedence over queue and queue over the device
default. Voice-only is a preference, not permission to hide failures: Kora
forces the visual response panel visible whenever speech is unavailable,
temporarily blocked, or playback fails. **Settings > Responses > Muted speaker
fallback** is enabled by default and shows the original response as text when
the selected speaker is muted or at zero volume. It can be disabled for ordinary
voice-only responses without hiding safety or recovery information. Unmuting
restores the configured mode on the next response.

Typed and local-model responses use the effective output mode. When microphone
capture is active, Kora uses visual output to avoid recognizing its own speech;
preview and spoken approval prompts temporarily manage capture separately.

Call-aware response policy is detector-neutral. A detected Active or Suspected
call uses a persisted visual-text override by default, which suppresses
automatic response speech while leaving Kora voice activation enabled. The
setup panel independently allows the user to use the normal response mode
during calls or disable voice activation during calls. If voice activation is
disabled, detection of a call closes active capture and prevents it from being
re-enabled until the call clears. These settings are stored in device-local
application data at
`%LOCALAPPDATA%\Kora\Preferences\call-aware-settings.txt`.

The current Windows bootstrap does not claim automatic call detection and
reports that capability as unavailable. Ordinary response policy remains in
effect until a reliable detector supplies an Active or Suspected observation;
an open communication-app process alone is not treated as proof of a call.

Without a Kora-specific device preference, Kora selects **System** for both the
microphone and speaker. System follows the live Windows multimedia-default
endpoint, including default changes while capture or playback is active.
Enumeration alone does not open the microphone; listening still requires
saved explicit consent, an available effective microphone and fresh ownership,
Windows-session, permission and voice-policy gates. Normal startup arms
push-to-talk without opening ambient capture. Choosing a specific
microphone or speaker in Settings persists its stable Windows Core Audio endpoint
ID as a device-local Kora override.
Selecting System again removes that override. Future validated verbal/model
setting changes use these same observable properties and persistence path.

A saved specific-device override takes precedence on later starts, but if that
endpoint disappears Kora does not silently switch to another same-name or
newly-default device. It marks the selection unavailable and requires either an
explicit replacement or selection of System. If the selected route has no active
endpoint, Windows cannot open it, or WASAPI reports a capture/playback failure,
Kora disables the affected voice capability and forces the text panel visible.
A Windows software-muted output or zero endpoint volume is also treated as
unavailable: Kora does not unmute or change global volume.
Windows cannot reliably prove that sound was physically audible—for example,
downstream speakers may be powered off, an analog cable may be disconnected, or
a hardware mute may not be reported by the endpoint.

## Diagnostics

Kora writes structured JSON diagnostic events to one daily file under
`%LOCALAPPDATA%\Kora\Logs`, using the name `kora-YYYYMMDD.log`. It retains no
more than 30 daily files and removes files older than 30 days. The application
uses `ILogger` throughout its portable, Windows, and desktop layers; Serilog is
confined to desktop composition as the production file provider.

Default diagnostics record lifecycle, readiness, state transitions, counts,
and failures. They do not record recognized transcript text, response bodies,
synthesized speech text, raw audio, credentials, or secrets. Kora exposes a
bounded application-log reader for future model-assisted diagnostics. That
reader can enumerate only Kora's validated daily log names and can read at most
1,000,000 trailing characters; it cannot read an arbitrary path.

Security-relevant activity is emitted through a dedicated structured audit
contract carried by the same `ILogger` pipeline. Each audit event records a
correlation ID, category, stable action and target IDs, initiator, outcome,
optional approval ID, and a bounded reason code. It never includes command
text, setting values, paths, process arguments, script content, or exception
text. Request and terminal events share a correlation ID so attempted,
successful, failed, denied, and cancelled actions can be distinguished.

The current bootstrap audits device-local configuration writes, Kora process
restart, Windows session-lock requests, and protected power proposals. The
schema also reserves an explicit script-execution category, but Kora does not
currently execute scripts and therefore does not emit successful script events.
Any future write tool, application or script runner, or security-approval
service must use this contract at its policy/execution boundary.

The daily JSON stream is operational audit evidence, not a tamper-evident
security ledger and not proof that an action was authorized. Before Kora
enables general write, process, script, or approval execution, the same typed
`ILogger` event must also be committed to the dedicated host-owned
`security_audit_events` table described by the security design.

The target instrumentation design does not make SQLite the only failure
diagnostic path. Every permitted `ILogger` event goes to both the retained
daily JSON files and encrypted SQLite. Ordinary records use
`application_log_events`; records marked `SecurityAudit=true` use the separate
authoritative `security_audit_events` table and also appear in the JSON file.
Both tables preserve structured `ILogger` data: event identity, level/category,
original message template, typed named properties and scopes. Audit rows add
fixed typed audit columns. Rendered text is only a display/full-text projection,
not the database record model or a source of audit authority.
The target codebase uses versioned `System.Diagnostics.ActivitySource`
instrumentation with W3C trace/span IDs throughout request, session/task,
approval, runtime/tool, storage and evidence boundaries. Every diagnostic and
audit row captures its activity plus host-owned session/task/invocation/
approval/correlation IDs. Sessions span many traces; session evidence can show
all related Logs and Audit entries, while any row can open its parent/linked
trace graph.
Diagnostic database retention defaults to 30 days under its independent
configurable setting. Audit retention defaults to 90 days and can be configured
from 30 through 365 days. Session deletion does not remove content-minimising
audit rows, audit expiry does not remove perpetual grants, and viewing/searching
evidence does not extend either retention clock.
The file sink remains independent for bootstrap, database/key/migration
failure, fatal crash, and evidence-store recovery. The planned Evidence mode
has Logs, Audit and All Evidence views that list, read and search retained
records without a model. Its Ask Evidence flow reasons over an explicit bounded
selection, cites exact records and separates observations from inference;
remote reasoning requires preview/approval of the selected payload. Ordinary
diagnostics, file audit copies and retrieved record text never become
authorization, instructions or receipt evidence.

SQLite itself is built-in application infrastructure, not an optional external
dependency. Binary releases must include Kora's pinned managed provider and
architecture-matched admitted native encrypted engine; the startup probe checks
that packaged storage and schema rather than offering a SQLite installation.
Kora does not require a SQLite server, download SQLite at runtime, use an
ambient machine installation, or silently fall back to plaintext storage.

## Build and test

Developer builds require the pinned .NET SDK, Git and PowerShell 7 (`pwsh`).
On main, restore the pinned local GitVersion tool with `dotnet tool restore`
before building. Feature builds do not invoke GitVersion.

```powershell
dotnet restore .\Kora.slnx --locked-mode
dotnet build .\Kora.slnx --configuration Release --no-restore
dotnet test --solution .\Kora.slnx --configuration Release --no-build --report-trx --results-directory .net-test-artifacts
```

Tests use xUnit v3 through Microsoft Testing Platform and AwesomeAssertions.
Tests that exercise logging use Neovolve.Logging.Xunit so `ILogger` output is
included in xUnit diagnostics. The GitHub Actions workflow restores locked
dependencies, builds and tests on Linux, and cross-publishes
framework-dependent Windows x64 and x86 artifacts.

The unsigned [WiX MSI/custom Burn installer](installer/README.md) packages the x64
application with an animated, reduced-motion-aware Avalonia setup UI.
Run `.\eng\Build-Installer.ps1` on Windows; local feature builds use `0.1.0`.
Feature/PR CI builds setup without uploading it. Main uses GitVersion:
untagged builds are `<major>.<minor>.<patch>-beta<increment>`, stable-tagged
builds are `<major>.<minor>.<patch>`, consistently across binaries and setup.
Main CI publishes application/installer artifacts and unsigned POC GitHub
releases with notes and checksums (beta prereleases or stable-tag releases).
See the [versioning and validation policy](installer/README.md#versioning-and-github-actions);
publication does not imply installed/protected/native-storage acceptance.

NuGet versions are owned centrally by `Directory.Packages.props`. Central package
management also enables transitive version pinning and rejects project-level
version overrides. After intentionally changing a package version, refresh the
lock files with:

```powershell
dotnet restore .\Kora.slnx --force-evaluate
```

Every direct and transitive NuGet dependency is checked against the reviewed
license policy in [`DEPENDENCY-LICENSES.md`](DEPENDENCY-LICENSES.md). Run
`.\eng\Test-DependencyLicenses.ps1` after restoring local tools and before
proposing a package change.

Builds treat compiler and analyzer warnings as errors. The baseline combines
the latest recommended .NET analyzers, enforced code-style diagnostics,
Meziantou.Analyzer, Visual Studio threading analyzers, and the xUnit analyzers
provided by the test framework. Narrow `.editorconfig` exceptions document
framework or test patterns where a rule is not applicable.

The automated test boundary is:

- `Kora.Core.UnitTests` and `Kora.Application.UnitTests` are portable unit tests. Linux CI collects coverage for `Kora.Core` and `Kora.Application`, requires 100% line and branch coverage, uploads the Cobertura report, and posts the Markdown summary to pull requests.
- `Kora.Windows.IntegrationTests` runs on a GitHub-hosted Windows runner. It covers native dependency probing, safe microphone enumeration/error boundaries, and the audio stream adapter without opening a real capture session or invoking session lock.
- Physical microphone capture, recognition quality, device removal during capture, Windows session-lock notification, and real lock behavior require a controlled Windows machine and remain manual/end-to-end acceptance evidence. CI must never lock or restart its runner.

## Project layout

- `src/Kora.Core` — portable command, dependency, voice, and platform contracts.
- `src/Kora.Application` — portable application orchestration and view models.
- `src/Kora.Windows` — Windows microphone, speech-recognition, text-to-speech, readiness, and session integrations.
- `src/Kora` — Avalonia composition root and presence interface.
- `tests/Kora.Core.UnitTests` and `tests/Kora.Application.UnitTests` — portable unit tests and CI coverage.
- `tests/Kora.Windows.IntegrationTests` — non-destructive Windows integration tests.
- `Design` — product, architecture, safety, and interaction specifications.

## Contributing

Issues and contributions are welcome. Read [`CONTRIBUTING.md`](CONTRIBUTING.md)
for development checks and the terms that apply to submitted contributions.

## License

Kora is **source-available**, not OSI-approved open source. It is licensed
under the [`PolyForm Shield License 1.0.0`](LICENSE). The license allows use,
modification, and distribution for permitted purposes, including personal and
internal commercial use, but does not permit providing a product that competes
with Kora. That restriction applies to competing products whether they are
sold or provided free of charge.

Third-party components remain under their own terms. See
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) and
[`DEPENDENCY-LICENSES.md`](DEPENDENCY-LICENSES.md).
