# Kora

A Windows voice-first, local-first extensible assistant.

This repository currently contains the first runnable bootstrap: an Avalonia desktop shell using the constellation design, deterministic built-in command routing, Windows microphone discovery, local Windows speech recognition and text-to-speech, dependency readiness probes, and unit tests.

## Requirements

- Windows 10 build 19041 or later; Windows 11 is recommended.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) matching `global.json`.
- An English Windows speech-recognition language.
- An installed Windows text-to-speech voice. Kora prefers a female voice matching the Windows profile culture.
- A microphone capture device.

No model, cloud account, or network connection is used to recognize built-in commands.

## Run

```powershell
dotnet restore .\Kora.slnx --locked-mode
dotnet run --project .\src\Kora\Kora.csproj
```

On first launch:

1. Review the readiness results.
2. Review the microphone and speaker selected from the current Windows defaults,
   or choose Kora-specific overrides in Settings.
3. Kora starts listening automatically when the selected microphone and voice
   activation policy are available.
4. Say **“Kora, what can you do?”** or another phrase in the built-in catalogue.
5. Observe the transcript, matched action, and constellation state.
6. Choose **Disable listening** whenever you want to release the microphone for
   the rest of the current run. With listening disabled, choose **Preview voice**
   to test the selected local voice.

The typed command field drives the same router and can prove command behavior when microphone or speech-language support is unavailable.

Kora also keeps a system tray icon available while the process is running. Its
context menu uses the configured assistant name for **Show**, **Settings**, and
**Exit**, and also provides **Documentation**.
Windows may place the icon under **Show hidden icons** until the user promotes
it to the always-visible notification area.
After successful startup Kora hides its borderless transparent constellation and
continues listening in the background. The constellation appears while the user
is interacting with Kora or when Kora has information/results to provide.
Visual text uses a separate compact response surface; configuration remains in
Settings. Closing visible surfaces returns to the background state, while only
**Exit** stops the process.
The constellation and unpinned response window automatically hide after 5
seconds without interaction by default. Settings or the pinned response-window
controls can change this shared device-local timeout from 1 to 60 seconds
without restarting Kora. Drag the response title area to reposition it; its
controls can keep the current response visible until dismissed and can disable
the default stay-on-top behavior. The visible constellation can also be dragged
to a device-local position that is restored across restarts.
Documentation opens the embedded end-user guide from [`docs/readme.md`](docs/readme.md)
in a single themed Markdown window. Settings opens a single
Windows-style settings window covering the assistant name, speech
and audio devices, listening, local voice, response output defaults and
overrides, detected-call behavior, and dependency readiness. Detecting
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

The Settings, constellation, and response surfaces bind to the same application
state. Changes are reflected immediately across any open surface. Typed setting
mutations also raise the same property notifications, providing the update path
that future validated verbal/model setting commands will use while Settings is
open.

### Appearance

Settings provides **System**, **Light**, and **Dark** application themes.
**System** is the default and follows the effective Windows light/dark
preference, including changes while Kora is running. Light and Dark are explicit
overrides. The selection is stored at
`%LOCALAPPDATA%\Kora\Preferences\appearance-theme.txt`.

Appearance settings also provide live sliders for the constellation's overall
size (240-600 px), dot size (50-200%), and dot movement speed (25-200%).
The defaults are 360 px, 100%, and 100%. These device-local settings are stored
under `%LOCALAPPDATA%\Kora\Preferences`.

The selected effective theme applies immediately to the constellation,
Settings, cards, status and response surfaces, and all shared
native controls. New chat, Markdown, diagram, browser, generated-HTML, approval,
and other visible result surfaces must consume the same application theme.
Embedded web content must receive the effective light/dark variant explicitly;
it must not choose an unrelated browser default or expose a separate hidden
theme setting.

## Assistant name

The assistant name defaults to **Kora**. Settings can change it to a validated
1-3 word name of at most 32 characters; letters, numbers, spaces, apostrophes,
and hyphens are supported. Applying a name updates the constellation, response,
and Settings surfaces, tray labels and tooltip, command catalogue and prefix, visual
responses, spoken responses, and voice preview immediately. If listening is
already enabled, capture is restarted with the new local recognition grammar
without granting new microphone consent.
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
- Bundled session skill: lock the current Windows session.
- Protected power requests: recognize, display, inspect, and cancel shutdown/restart proposals.

Shutdown and computer restart execution is intentionally disabled in this bootstrap. Recognition produces a visible non-destructive proposal and never sends an operating-system power request. Locking is a real local action and disables microphone capture before calling Windows.

The initial recognizer uses the installed English Windows speech engine and a fixed host-owned grammar. It is a command proof, not the final wake-word, endpointing, playback-rejection, or transcription implementation described by the design documents.

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
Voice preview remains disabled while listening until playback rejection is
implemented.

Response output can be configured as hybrid, voice-only, or visual-only. The
device default is persisted locally; current-queue and current-task overrides
are transient, with task taking precedence over queue and queue over the device
default. Voice-only is a preference, not permission to hide failures: Kora
forces the visual response panel visible whenever speech is unavailable,
temporarily blocked, or playback fails.

Typed/local responses are spoken when the effective mode includes voice.
Responses received while microphone capture is active remain visual because
playback rejection is not yet implemented; Kora must not risk recognizing its
own speech.

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
an available effective microphone and voice activation policy. Kora attempts to
start listening automatically during normal startup. Choosing a specific
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
enables general write, process, script, or approval execution, the same events
must also be committed to the host-owned append-only audit store described by
the security design.

## Build and test

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

NuGet versions are owned centrally by `Directory.Packages.props`. Central package
management also enables transitive version pinning and rejects project-level
version overrides. After intentionally changing a package version, refresh the
lock files with:

```powershell
dotnet restore .\Kora.slnx --force-evaluate
```

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
- `src/Kora` — Avalonia composition root and constellation interface.
- `tests/Kora.Core.UnitTests` and `tests/Kora.Application.UnitTests` — portable unit tests and CI coverage.
- `tests/Kora.Windows.IntegrationTests` — non-destructive Windows integration tests.
- `Design` — product, architecture, safety, and interaction specifications.
