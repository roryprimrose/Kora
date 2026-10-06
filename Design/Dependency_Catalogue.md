# Dependency Catalogue: Implemented, Experimental and Planned

Status: source-backed design inventory, not a machine-readable setup manifest
or a claim of release acceptance. Windows is the only supported application OS.
Linux build/packaging tools are not application dependencies on users' machines.

This is the canonical inventory of external capability dependencies and known
future requirements. [Environment Setup](Environment_Setup.md) owns detection,
consent, installation/configuration, cancellation, readiness and refusal policy.
[Distribution](Distribution_And_Updates.md) owns launch prerequisites and
protected installation; the [roadmap](Implementation_Roadmap.md) owns delivery.

## Meaning of Optional

- **Launch prerequisite:** required before the current framework-dependent
  process can run; not something a running Kora setup handler can repair first.
- **Bundled implementation:** library, native asset or first-party adapter
  delivered with Kora. Declining its capability does not currently remove its
  files or establish that its loader requirements can be omitted.
- **User-optional dependency:** external software, model/voice data, device,
  account or permission needed only for a selected capability. Users may decline
  it and keep the supported dependency-qualified deterministic command subset.
- **Experimental/planned requirement:** not composed into the current product.
  An experiment's versions, language bindings and tools are not selected
  production prerequisites.

Optional for a user does not mean optional for required product acceptance:
for example, A2 still needs a proved local inference adapter. Missing or declined
dependencies must not trigger cloud fallback, hidden downloads or repeated
installation attempts. Full durable refusal/re-entry and installer-assistance
reconciliation remain R10/R17 work; existing setup handlers are not proof of
that complete policy.

## Launch, Bundled and Build Requirements

| Requirement | Current evidence and boundary |
|---|---|
| .NET runtime | R02 win-x64 output declares `Microsoft.NETCore.App` and `Microsoft.WindowsDesktop.App` 10.0.0. Require a supported patched .NET 10 x64 **Desktop Runtime**, not just the base runtime or an SDK. See the [launch baseline](Distribution_And_Updates.md#launch-prerequisite-baseline). |
| Native VC++ runtime | Published ONNX DLL imports include `VCRUNTIME140.dll`, `VCRUNTIME140_1.dll`, `MSVCP140.dll` and `MSVCP140_1.dll`. The WiX/Burn POC pins VC++ v14 x64 Redistributable 14.51.36247.0 as its candidate delivery prerequisite; production qualification, actual loader closure and runtime-only Windows trials remain open. Declining Ollama/Kokoro is not evidence that these launch dependencies disappear. |
| Bundled libraries | [Central versions](../Directory.Packages.props) include KokoroSharp 0.8.4, MisakiSharp 2.2.0, ONNX Runtime 1.30.0, NAudio 3.1.0, System.Speech 10.0.12, Microsoft.Data.Sqlite 10.0.12 and Markdig 1.4.0, alongside Avalonia, logging and application libraries. These are package/build inputs, not separate optional user installations. In particular, current `Microsoft.Data.Sqlite` transitively supplies `SQLitePCLRaw.bundle_e_sqlite3` and its architecture-specific native asset. R04 has not replaced that unkeyed bootstrap-only pairing or enabled content persistence. The complete restored/published closure and licences must be inventoried per release, including transitive and native assets. |
| Storage and OS facilities | Current storage/SQLite initialisation is app-owned; no SQLite server is installed. SQLite is launch-critical built-in application infrastructure, not an optional capability: its startup entry is a health/migration check, not an offer to install it. Every admitted durable-storage binary package must include the reviewed managed provider and matching native engine; missing/unloadable assets are a packaging failure. Windows known folders, session notifications and audio APIs are platform facilities. [R04's safe foundation](Implementation_Roadmap.md#r04-foundation-delivery) implements internal, uncomposed CurrentUser DPAPI/ACL/key/artifact primitives, not production profile/storage integration. SQLite3MC 2.4.0 is approved for evaluation only; exact provider/native/licence closure and installed loading/protection remain unverified, and the durable store/authoritative evidence sink explicitly remain unavailable. These are required host-storage gates, not a user-optional database download, ambient system-library lookup or silent plaintext fallback; see [Windows durable storage](Architecture.md#windows-durable-storage-direction), [D-009](Decision_Register.md#lifecycle-and-integration-closure) and R04/R12. |
| Source build and packaging | Managed-source installation needs the selected .NET SDK and Git/build tooling; binary users do not. NSIS/SCons acquisition/build code has been retired; its receipts and the remaining source/native checks belong to the [historical distribution experiment](../experiments/r02-distribution-proof/README.md), not routine Kora launch. The [WiX 7.0.0 MSI/custom Burn installer](../installer/README.md) uses the pinned SDK, Avalonia and self-contained bootstrapper runtime with explicit prerequisite chains. Licence/notice controls run during packaging; external-asset and installed closure qualification remain separate R17 work. Use Linux for portable build/cross-publish and Windows for WiX packaging, not another application OS. |

The OS probes currently require Windows 10 build 19041 or later for speech.
That check is not an accepted Windows support matrix or hardware floor; D-007
still requires actual supported-version, device and packaged-host evidence.

## Implemented User-Optional Dependencies

These handlers/probes are registered in the current
[desktop composition](../src/Kora/Program.cs). Detection and scoped setup exist
where stated; production quality, containment and installer assistance are
separate gates.

| Dependency / baseline | Capability and current implementation | When absent or declined; setup/ownership |
|---|---|---|
| PowerShell **7.x, 7.4 or later** | [Setup service](../src/Kora.Windows/Dependencies/WindowsPowerShellSetupService.cs) executes a bounded no-profile version probe at supported locations. [Installation runner](../src/Kora.Windows/Dependencies/PowerShellProcessRunner.cs) requests per-user `Microsoft.PowerShell` from winget and re-probes; no exact installation version is pinned. This is interpreter readiness, **not** a shipping script/worker runner. | Future PowerShell-backed commands are unavailable without an admitted interpreter. Native commands do not acquire an interpreter requirement merely by being built in. Reuse supported existing installations; consented setup does not grant execution authority. R11 still owns protected dependency admission and contained execution. |
| Ollama; installation pin **0.35.1**, `Ollama.Ollama` | [Setup service](../src/Kora.Windows/Dependencies/WindowsOllamaSetupService.cs) offers per-user winget installation and can start its owned server at `127.0.0.1:11434`. [Readiness probe](../src/Kora.Windows/Dependencies/LocalInferenceDependencyProbe.cs) checks runtime metadata, the pinned model and functional inference. Existing endpoint identification is not exact-version certification. | Local inference is unavailable; deterministic commands remain. No automatic remote substitute. Preserve existing installations/settings; do not overwrite an unknown/unresponsive endpoint. The install pin is the bootstrap baseline, not a claim that every existing version is supported or D-003 is closed. |
| Ollama model **`qwen3:1.7b`**, approximately **1.36 GB** | Separately downloaded model for current local answers, clarifying questions and bounded registered-action proposals. Its expected SHA-256 model digest is pinned in the [setup service](../src/Kora.Windows/Dependencies/WindowsOllamaSetupService.cs); readiness requires the matching digest and a completed inference response. | An installed server alone does not make inference ready. Explain model download/network/storage before consent; mismatched existing model identity is rejected, not silently replaced. Model licence, CPU/RAM floor, quality, cancellation and network-blocked A2 evidence remain D-003 work. |
| Windows English speech recognition resources, microphone and permission | [Voice probe](../src/Kora.Windows/Dependencies/WindowsVoiceDependencyProbe.cs) checks an installed English Windows recognizer, active capture endpoints/default selection and microphone access. Windows manages speech/language components; no speech-pack downloader is implemented here. | Recognition/listening is unavailable until resources/device/permission and saved consent plus fresh host gates are satisfied. Native typed/mouse recovery remains available. Selecting/installing a pack or device never authorises capture. Resource identities/OS provisioning and actual compatibility must be reviewed before adding automated installation. Current grammar recognition is not the accepted production wake front-end. |
| Windows SAPI TTS voices/language resources and output device | [TTS probe](../src/Kora.Windows/Dependencies/WindowsTextToSpeechDependencyProbe.cs) checks enabled installed voices, culture/default selection and output readiness. [Speech service](../src/Kora.Windows/Audio/WindowsTextToSpeechService.cs) uses installed voices; installing additional OS voices is not a current Kora installer chain. | Missing voice/output, muted output or incompatible selection is reported; visual responses remain. An available voice can be selected explicitly or a compatible Windows speech pack obtained through the OS workflow. Do not equate a recognition language pack with a usable TTS voice. |
| Optional **Kokoro v2.0.0 assets**: FP16 ONNX model and voice archive | [Provider](../src/Kora.Windows/Audio/KokoroTextToSpeechProvider.cs) downloads/verifies model and voices from the pinned KokoroSharpBinaries release into the local Kora speech store. Total download is **229,449,998 bytes**; staging/extracted disk use is additional. This enables local neural TTS, not recognition, wake detection or identity verification. | No download unless selected/approved. Windows voices remain a separate selectable option when ready, and visual output remains available. Preserve user/provider selection; do not claim automatic fallback from a failed selected voice. Code/model/voice redistribution review remains distinct from hash verification. |
| Windows Package Manager **winget** and network access | Current PowerShell/Ollama installation runners depend on the user's WindowsApps `winget.exe` and its winget source. No winget version is certified here. Model/Kokoro acquisition also needs its declared network sources. | These are setup-path dependencies, not requirements to launch Kora or reuse ready installed components. Missing tool, unavailable source, policy denial or network failure is an explicit setup failure/block, not permission to install winget, change sources or run an arbitrary alternative installer. A verified manual/alternative setup route is future catalogue work. |

### Kokoro Asset Identities

The [WiX/Burn POC](../installer/README.md#selectable-optional-components) exposes
the implemented PowerShell, Ollama/Qwen, and Kokoro dependencies as unchecked
install-time selections, reusing these app services and identities. It also
chains hash-pinned .NET Desktop 10.0.12 and VC++ 14.51.36247.0 x64 prerequisites
before Kora, reusing compatible newer installations. This does not close the
installed loader/ownership, model quality, or redistribution gates.

The implementation currently pins the following downloads, separate from the
KokoroSharp NuGet version. These are reproducible observed identities, not
publisher authentication or redistribution clearance.

| Asset | Source | Download bytes | SHA-256 |
|---|---|---|---|
| `kokoro-fp16.onnx` | [KokoroSharpBinaries v2.0.0 model](https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro-fp16.onnx) | 163,636,560 | `027A25B14AEF7D3AE57FD09301EBEFBEC868E79D55213D07E4F3AF442F5BA352` |
| `voices.zip` | [KokoroSharpBinaries v2.0.0 voices](https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/voices.zip) | 65,813,438 | `313D823EE9EA8828C14B28042B933BBC03955D402AFB785CBE0B877827D3EA1D` |

The current [optional speech offer](../src/Kora.Application/ViewModels/MainViewModel.cs)
records its initial handling/missing-provider notification; that limited
behaviour is not evidence of a universal provider/dependency refusal registry.
Installer-provisioned assets are not trusted merely because the installer said
they were installed.

The [local-inference qualification plan](Local_Inference.md) records additional
candidate metadata: the published Ollama installer is **1,580,352,416 bytes**
and the model blobs total **1,359,293,444 bytes**. A fresh acquisition is roughly
**2.94 GB** before overhead, not merely the 1.36 GB model; expanded runtime,
staging, per-volume disk headroom and inference RAM remain unmeasured. The
current 2 GB model-volume guard is not a total provisioning budget. Tagged
Ollama source MIT and manifest-linked model Apache-2.0 licence evidence is
recorded, but installed executable/weight integrity, native notices and
distribution clearance remain open. Public metadata does not qualify an
installed runtime or close D-003/D-007.

## Experimental Speech Candidates, Not Production Installs

The [R02 speech inventory](../experiments/r02-speech-proof/candidates.json)
records these reviewed candidates. All require separate code/model licence,
Windows native/.NET packaging and acoustic/privacy acceptance. The experiment's
CPython 3.12 environment and hash-pinned wheels are research tooling, not a
decision to require Python on end-user machines.

| Candidate | Actual evidence / unresolved requirements |
|---|---|
| sherpa-onnx/core **1.13.8**, English GigaSpeech **3.3M KWS 2024-01-01** assets | Windows Python file-based synthetic KORA keyword trial only; no production threshold, packaged .NET admission, acoustic corpus or playback rejection acceptance. Account-free local candidate; model/data-rights review remains separate from the engine licence. |
| openWakeWord **0.6.0** | Not installed/run; no cleared Kora classifier. Apache-2.0 code does not make supplied **CC BY-NC-SA 4.0** pretrained models permissive production assets. Custom-model/data rights and dependency closure are unresolved. |
| Picovoice Porcupine **4.0.3** | Not installed/run; no approved account/AccessKey, custom Windows Kora profile or agreement. Public binding licence does not grant proprietary engine/model redistribution. Cold disconnected licensing and usage metadata are unverified. |

None replaces current recognition or proves D-002/D-007. See the
[speech proof](../experiments/r02-speech-proof/README.md) for actual versus blocked
measurements rather than treating candidate availability as acceptance.

## Known Future Capability Dependencies

Selections/versions below are deliberately unresolved unless already identified
above. Setup must register a reviewed first-party handler only after the
corresponding implementation and decision are admitted; acquiring a package
cannot add missing adapter code to Kora.

| Capability and requirements | Status / delivery gate | Absence, decline and setup boundary |
|---|---|---|
| Controlled Copilot execution: pinned SDK/adapter, any required external runtime/CLI, supported authentication/account/entitlement and approved endpoints | Not composed into this bootstrap. D-001 / R02 / R08 require the [Copilot control-point proof](Architecture.md#copilot-integration-proof); external package identity/version and account tier are not selected here. | No mandatory generic "Copilot install" package is assumed. Declining disables that remote provider only; a ready local provider or deterministic subset remains. Authentication/credentials/egress are host-owned and must not enable SDK tools outside mediation. |
| Independent management inference: supported provider session/concurrency, quota and cost envelope | D-004 / R13; provider-specific runtime/account requirements follow the admitted adapter, not a second blanket AI installation. | Deterministic routing/status fallback remains; enabling management does not approve additional accounts, subscriptions or task context disclosure. |
| Other model adapters: Foundry/OpenAI or additional local engines/models | [Extensibility](Extensibility.md#feature-placement) names directions, not implemented alternatives or pinned packages. New provider admission/egress decisions are required. | Explicit provider selection, credentials, terms/cost and network approval; no automatic installation/sign-in or cloud fallback when a selected provider fails. |
| Production wake/custom activation-name profiles, VAD/endpointing, activated transcription and playback/echo handling | D-002 / D-007 / R09; experimental candidates above do not choose the full stack. Model files, native dependencies, hardware floor, custom-profile training/data rights and any licensed account requirements remain selection work. | Missing ready speech stack disables affected voice activation; typed/native controls remain. Voice assets are optional per user, but required advertised A1 voice must pass its gates. No ambient training/transcription to obtain missing resources. |
| Frequent-speaker learner and derived local profiles; separately enrolled verifier/anti-spoof assets and native reauthentication | D-006 / R24; [optional learning and verification](Extensibility.md#feature-placement). No engine/model version selected. Windows Hello or equivalent protected OS enrollment workflow is a verifier requirement, not compulsory baseline voice biometrics. | Separate consent/enrollment; profile-local protected data, reset/delete and quality/privacy proofs. Missing either does not block baseline voice. Learning is personalization, not identity or grants; no remote biometric processing. |
| Communication detectors: tested Teams local signals; optional Graph own-presence account/API; later Zoom/Slack/Discord/browser adapters | R15; [call-aware contract](Call_Aware_Speech.md#teams-detection). Current composition uses `UnavailableCallStateService`, not a working Teams/Graph detector. Graph requires work/school account and delegated `Presence.Read`; package/adapter versions remain unselected. | Do not install communication apps just to run Kora. Manual call mode remains; disclose no automatic detection. Failed enabled detectors become Unknown, not Clear. Graph requires explicit account/network consent and is unavailable under local-only policy; no tenant-wide/write permission. |
| MCP servers/connectors, chosen transports, server runtime, identity/account and endpoint permissions | R20/R21/R23; [MCP boundary](Extensibility.md#mcp-external-system-functionality). Server-specific packages and supported versions are selected per connection. Node/Python/container tooling is needed only if the admitted selected server actually requires it, not as a blanket Kora dependency. | Missing/declined server disables its tools. Local servers still need executable identity/containment review; remote servers retain service-side responsibility. Kora's possible server exposure is separate deferred R29 work, disabled by default. |
| Enterprise sources such as Work IQ, GitHub, Azure DevOps and SharePoint; later Git/write or desktop/browser automation tools | R20/R26/R28; [feature placement](Extensibility.md#feature-placement). Selected connector may need tenant/service entitlement, authentication and API scopes; no universal credential/package set exists. | Not core prerequisites. Obtain narrow account/source/resource grants per connector and preserve local-only policy. Read support is not write/automation admission, nor permission to mutate Kora repositories. |
| Isolated embedded browser integration, potentially WebView2-backed, and any renderer runtime | R25; [browser contract](Information_Display.md#browser-content) leaves the package/version unselected. Native embedded-guide Markdown already ships and is not general web/result rendering. | WebView2 is a candidate, not a current required runtime or automatic download. If no admitted embedded viewer is ready, disclose unavailability and offer an explicit external-browser action; retain bounded native/source output. |
| Mermaid and later math/rich renderers; OCR, visual extraction, embeddings/ranking and indexing models | R25/R26; [rich-content rules](Information_Display.md#mermaid) and [feature placement](Extensibility.md#feature-placement). Engine/model/asset identities remain unselected; fixed trusted renderers should be pinned/bundled after admission, not fetched from document URLs/CDNs. | Unsupported content remains labelled text/source. Image/screen/source collection and model downloads require their own consent; no ambient collection, arbitrary plugin loading or silent remote assets. |
| Registered protected workers/interpreters and future executable-skill/application dependency closures | R11/D-013 for fixed bundled actions; R27 for broader executables. Current PowerShell readiness is not containment admission. Future [executable skill requirements](Skill_Storage.md#future-user-provided-executable-skills) derive from actual reviewed imports/profiles. | Unsupported/missing closure disables affected execution, with no PATH/shell substitution or tool-directed installation. Native deterministic controls do not all require PowerShell. Bundled resources/workers are application release content, not writable user dependency setup. |

## Version Hosting and Future Maintenance Requirements

**GitHub Releases is the sole current version host**, for both prereleases and
production releases of `roryprimrose/Kora`. It is not a dependency on a GitHub
CLI, Git checkout or GitHub account for a public binary user's ordinary launch.
Discovery is a trusted maintenance network operation; offline policy may defer
it without disabling local commands or claiming the installation is current.

Production discovery excludes drafts and prereleases. An explicitly chosen
preview channel may discover published prereleases; drafts remain excluded.
GitHub CI artifacts, `main` commits and this experiment's setup EXEs are not
released versions. The [release discovery contract](Distribution_And_Updates.md#hosted-release-feed-and-notify-only-interaction)
owns exact origin/channel/version/architecture/byte identity and failure handling.
Production candidates are built from approved protected version tags, with an
early already-published check and no overwriting released versions; see
[tag and publication rules](Distribution_And_Updates.md#version-tags-and-publication-pre-check).

Current maintenance is **notify-only**, not an implemented install-capable
updater or a new skill. A future in-app "Install update" action needs R29 scope
approval and independent authenticated signed metadata/root trust, protected
maintenance configuration, exact host-owned approval, OS-authorised protected
installation and quiescence/verification/recovery evidence. Release notes,
skill instructions, a release tag or a hash from the same GitHub response
cannot supply independent update trust or installation authority.
Any installer/helper or metadata-verification library is a future packaging
choice, not a selected mandatory dependency. See
[update ownership](Distribution_And_Updates.md#update-ownership-and-deployment-mode).

## Turning This Inventory into Setup and Acceptance

R10 must turn admitted entries into the versioned host-owned catalogue with
capability IDs, platform/architecture/version ranges, reviewed source and
licence identities, hash/signature verification, storage/download/resource
estimates, bounded probes, consent/cancellation, durable refusal and removal
ownership. Do not parse this Markdown into executable installation plans.

R17's optional installer assistance uses those same entries and may be skipped
without any provider choice. Application setup must still detect/reconcile and
verify actual state. Test each absent, declined, incompatible, failed and
pre-existing dependency against advertised command availability, preserving
previous user installations/data and avoiding automatic cloud fallback or
retries. [Environment acceptance](Acceptance_Criteria.md#environment-setup-gate)
and D-001 through D-007/D-013 still apply; this inventory closes no release gate.
