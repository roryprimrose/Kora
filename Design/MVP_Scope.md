# MVP Scope and Non-Goals

Status: proposed release scope, not the set of features in the current bootstrap.
That bootstrap implements consented local-model readiness and verified
inference for unmatched requests, deterministic commands, and initial
model-proposed actions/grants; it does not complete Slice A or close D-003/D-008.

Related: [Vision](Vision_Statement.md), [Architecture](Architecture.md), [Decision Register](Decision_Register.md), [Acceptance Criteria](Acceptance_Criteria.md).

The [Implementation Status and Delivery Roadmap](Implementation_Roadmap.md)
records what the current source actually delivers and the dependency/value-ordered
work remaining. A documented capability, local readiness proof or native window
does not establish acceptance of an entire checkpoint.
The [current merged snapshot](Archive/Implementation_Roadmap_2026-10-11.md#current-merged-snapshot---2026-10-07)
also includes minimal durable session authority/workspace/names/native empty
creation and exact-ID typed/activated-voice session commands, explicit local
clipboard preview, read-only discovery, bounded
appearance/installed speech settings, artifact instruction invocation,
ordinary diagnostic pruning, independent daily evidence and native tray/passive
microphone-card recovery. These foundations do not complete conversations/queues, a model
tool loop, clipboard answering, ambient wake or full A0-A4 acceptance.

## Product Admission and Realignment

This section is the authoritative product-scope decision for the 2026-10-21 realignment. The [vision](Vision_Statement.md#vision) describes direction; [placement](Extensibility.md#placement-rule) decides host versus extension ownership; [qualification](Acceptance_Criteria.md#three-tier-qualification-policy) decides whether an admitted implementation can be enabled.

### Approved core outcomes

- Control AI interaction through voice, with equivalent typed and accessible native interaction.
- Let models reason about and propose changes to registered Kora settings through the same host-owned validation and approval path as native controls.
- Discover and invoke admitted tools, MCP servers, skills, agents and instructions without giving their content host authority.
- Support useful explicit static commands, qualified local models and explicitly admitted hosted models.
- Communicate assistant state through the particle-cloud presence.
- Browse and participate in several independent session conversations.
- Expand results into readable complex data, Markdown and admitted browser content.
- Enforce permissions, grants, consent, ownership and privacy.
- Record inspectable audit outcomes, diagnostics and causal activity.
- Manage concurrent sessions, their work and their retained data.

Capture, source admission, setup, storage, cancellation, recovery, distribution and accessibility are supporting responsibilities when consumed by these outcomes. Their existence does not justify a separate user-facing toolbox. [Feature placement](Extensibility.md#feature-placement) remains authoritative for those supporting seams.

### Feature admission

Before starting a new feature, identify its approved outcome, concrete user scenario, host-versus-extension rationale, complete intended interaction, smallest useful implementation increment and acceptance evidence.
Reference this scope or an explicit product decision in the [decision register](Decision_Register.md). A source layout, available dependency, passing tests or free implementation slot is not product approval.

Materially new product scope requires the product owner's decision. Bug fixes, accessibility, reliability and proportionate usability improvements within an approved outcome do not need a new product decision for every change.
Bounded fail-closed implementation may precede enablement, but must advance the approved outcome rather than invent an unrelated substitute when a provider or runtime is unavailable.

Domain-specific calculations, conversions, format analysis and repeatable workflows belong behind governed tools, MCP or skills when separately selected for delivery. Do not automatically build replacements for retired native features. Static commands are access routes to admitted behavior, not a mandate to add unrelated native utilities.

### Approved retirement and simplification

The following are target changes, not claims that the existing code has already been removed. The linked source and [delivery inventory](Implementation_Roadmap.md#current-delivery) distinguish implemented behavior from the approved destination; each removal PR must update its affected user documentation and tests.

| Area | Approved destination | Existing implementation being retired or simplified |
| --- | --- | --- |
| Native utilities | Remove standalone calculator and date/time-answer commands; do not add conversion, UUID, calendar arithmetic, stopwatch or timer utilities. Keep internal time, numerical and identity mechanisms. | [Calculator](../src/Kora.Application/ViewModels/MainViewModel.Calculator.cs), [clock](../src/Kora.Application/ViewModels/MainViewModel.LocalClock.cs) |
| Clipboard analysis | Remove standalone JSON/XML analysis controls and reject CSV, Base64, URL-syntax and text-statistics expansions. Keep capture, immutable preview, Find, wrapping and governed source use. | [JSON](../src/Kora/ClipboardPreviewWindow.Json.cs), [XML](../src/Kora/ClipboardPreviewWindow.Xml.cs) |
| Conversation input | Replace saved-draft document management with per-conversation in-memory unsent text. No new disk autosave, catalogue, comparison or rebase workflow. | [Composer](../src/Kora/SessionsViewModel.Conversation.cs), [saved-draft catalogue](../src/Kora/SavedDraftCatalogueViewModel.cs) |
| Normal workspace | Remove engineering-only range/digest/provenance query controls from ordinary browsing; retain useful names, content search, lifecycle/state and readable source details. | [Sessions](../src/Kora/SessionsWindow.axaml), [All Work ranges](../src/Kora/AllWorkViewModel.Ranges.cs), [attachments](../src/Kora/AttachmentCatalogueWindow.axaml) |
| Diagnostic queue scaffold | Retire normal fixed-job admission and plan/configuration/reordering surfaces; retain internal scheduling, direct status reads and safe existing-work inspection/cancellation. | [Plans](../src/Kora.Application/Hosting/SessionQueueService.DiagnosticsPlan.cs), [controls](../docs/commands.md#deterministic-local-version-queue) |

Existing saved text must remain recoverable read-only into its exact conversation's unsent buffer under the current retention/privacy rules. Do not silently purge it or break historical schema/audit validation.
Preserve question-answer drafts, definition-editing buffers, submitted history, grants and audit. Legacy compatibility is not permission to continue ordinary saved-draft writes or create a new permanent draft-management product.

Keep technical identity, revision, generation, digest and source validation inside the host. Removing a query widget does not remove its underlying security boundary. Explicit Evidence/audit diagnostics may expose technical filters for that purpose; do not recreate removed normal controls in a new diagnostic framework merely to retain them.

### Delivery order

1. Stop retired work and withdraw affected PRs from automatic admission; do not refill their slots with variants.
2. Merge the product/instruction realignment and implement the bounded removals with truthful current-versus-target documentation.
3. Prioritize useful conversation turns, shared settings proposals, governed tool/result execution and MCP/skill/agent integration, voice interaction, coordinated sessions and readable results.
4. Apply existing implementation, enablement and RC gates independently. No priority or scope decision waives required safety, compatibility, review or validation.

Historical implementation receipts remain historical evidence, not continuing feature approval. Closed or retired work requires a new explicit product decision before recreation, reopening or replacement.

## Product Outcome

A Windows user with current owner presence can say "Kora, explain the clipboard", see the answer, hear a short summary, and interrupt the interaction without typing or pressing an activation control.
When intent is ambiguous or a selected speaker protection is unsatisfied, the same request pauses for explicit clarification/alternate-channel confirmation before clipboard capture or egress.
Owner presence uses the revised unlocked-session/deliberate-input baseline, with optional speaker protection; ordinary confirmation can be completed by voice or UI.
Equivalent UI-only and mixed-channel workflows and persistent concurrent sessions follow [Human Interaction and Persistent Sessions](Interaction_And_Sessions.md).

The first release proves a complete, predictable interaction rather than shipping an incomplete version of every capability in the vision.

## Platform Support Policy

Windows is the only supported application platform for the foreseeable future, not merely the first release target.
Maintain multi-platform extensibility through portable shared logic and narrow first-party native boundaries as defined in [Architecture](Architecture.md#platform-boundaries-and-support).
There is no Linux/macOS implementation, distribution, feature-parity, or porting-roadmap commitment.
Do not build speculative adapters/projects or weaken Windows security requirements for hypothetical portability.
Linux GitHub Actions build/test/package infrastructure does not make Linux a supported Kora runtime.

## Delivery Slices

### Slice A: Voice and Clipboard

**Bounded delivered subset (2026-10-07):** explicit request-triggered Unicode
plain-text snapshot and immutable native preview, exact-ID reuse and
revoke/clear are composed without a model. Reads are bounded to 256 KiB strict
UTF-8 and denied/stale/unsupported/contended states are explicit. There is no
background watcher, clipboard write/history/persistence or model egress.
**Explain the clipboard** currently offers that preview and states that
explanation is unavailable. Qualified local tool-loop/answering, complete
secret/egress envelope controls and live clipboard acceptance remain open;
this is not the product outcome or completion of A0/A2.

Slice A is delivered through independently accepted implementation checkpoints.
A checkpoint may be used for development evaluation but is not advertised as the completed product outcome until all Slice A checkpoints pass.
Do not begin a later checkpoint while an earlier checkpoint has unresolved safety/correctness failures.

This rule governs enabling dependent behavior and checkpoint acceptance, not
the order of all foundational engineering. Durable identity, intent, approval
and storage primitives associated with A3 are implemented early where A0's
context/tool path depends on them. Independent feasibility prototypes can run
in parallel, but cannot bypass a failed prerequisite gate. The
[roadmap dependency graph](Implementation_Roadmap.md#ordered-outstanding-work)
defines that engineering order; sign-off remains A0 through A4.

1. **A0 — Deterministic shell:** native setup/recovery, explicit push-to-talk, local transcription/TTS, clipboard snapshot/preview, one remote runtime, egress approval, streaming display, cancellation, and no model-assisted work management.
2. **A1 — Voice-first activation:** packaged local "Kora" detector, immediate wake-and-command preservation, playback rejection, interruption, locked-session policy, and the required wake quality/resource evidence.
3. **A2 — Local-first answering:** one supported Ollama-backed inference adapter and selected model pass local-only clipboard-answering, cancellation, setup, hardware, licence, and performance gates. Cloud is not a fallback.
4. **A3 — Sessions, managed work and controls:** private-profile standard SQLite session/event/artifact persistence and retention, deterministic session controls, bounded independent routing/management, isolated concurrent scheduler/resource leases, authoritative per-session ledger/queue, structured questions/grants, configuration registry, and protected computer/application controls.
5. **A4 — Session workspace and interaction surfaces:** compact latest interaction, Sessions workspace with session list beside full conversation/history and per-session work/queue, separate immutable detail/script viewing, shared native questions/approvals, supporting tray/settings/grant/setup surfaces, proactive conversations, call-aware output/authorization/origin gates, and bounded presentation details.

Each checkpoint preserves all earlier controls and has a separately reported acceptance result.
The [coordinated window design](UI_Workspace_And_Windows.md) owns A4 layouts and behavior; opening multiple windows is not evidence of A3 task concurrency.
Optional frequent-speaker learning and owner-aware verification remain separately capability-gated under [D-006](Decision_Register.md#d-006-optional-speaker-verification), not mandatory first-release checkpoints. Missing either never blocks baseline voice; advertise them only after their own quality/privacy evidence passes.

1. The user configures a microphone and a supported runtime and consents to local wake-word listening.
2. The user says "Kora, explain the clipboard"; local detection activates command capture without requiring a pause after "Kora".
3. Kora transcribes locally and snapshots clipboard text only after that request.
4. Kora displays the transcript, selected context, and intended processing destination.
5. When the context is going to a remote runtime, Kora obtains transmission approval.
6. The runtime produces a streamed visual answer.
7. Kora speaks a short summary using local text-to-speech.
8. The user can stop speech, cancel the task, or ask a follow-up using the same approved snapshot.

The bounded [clipboard workflow](../src/Kora.Application/Context/ClipboardExplanationWorkflow.cs) implements native review and an explicit unavailable production path. The required clipboard secret/sensitive-content source-use rule has no production implementation; [the composed policy](../src/Kora.Tools/Clipboard/UnavailableClipboardSourceUsePolicy.cs) refuses before model context or durable source-turn admission.
Review/capture is not explanation success or egress approval; no hosted/bootstrap fallback or production model enablement is added. Answers remain volatile native-only text, not speech, transcripts or retained conversation bodies ([presentation](../src/Kora.Application/ViewModels/MainViewModel.Clipboard.cs)).
The [clipboard context builder](../src/Kora.Core/Context/ClipboardExplanationContext.cs) adds structural credential refusals and full-envelope bounds after source-use acceptance. Deterministic fake-policy/provider [workflow tests](../tests/Kora.Application.UnitTests/Context/ClipboardExplanationWorkflowTests.cs) validate execution plumbing, not delivered production explanations or comprehensive sensitive-content admission.

Included:

- A .NET/Avalonia desktop shell with explicit microphone and task status.
- Cross-build single-active-instance coordination, same-build activation, approved different-build handoff, and mouse-based original-version return offer under [Instance Coordination](Instance_Coordination.md).
- Always-available native questions with mouse answers, explicit first-run microphone selection, and device/permission-loss recovery without speech/model/network dependencies.
- Persistent system tray icon/context menu as the primary non-voice recovery route, including detected microphone choice and explicit listening enablement.
- Built-in first-run/re-enterable setup, automatic internal storage/database initialisation, and capability-based dependency detection/configuration.
- Local wake-word activation as the primary interaction ("Kora" by default), with optional push-to-talk and one opt-in validated custom activation name.
- Local command endpoint detection, transcription, and speech output.
- Visible wake-listening/capture/mute states and verbal interruption during speech output.
- Out-of-the-box lock/shutdown/restart skills with definitions/scripts embedded in the protected application binary and local routing; disruptive power actions require action-specific voice or UI confirmation, safe handling of all sessions, mandatory OS checks, and a cancellable countdown.
- Built-in application lifecycle, queue, speech, readiness, and maintenance phrase support as defined in [OOTB Phrases](OOTB_Phrases.md).
- Verbal discovery/get/set/reset through the typed host registry, subject to mandatory secure workflows and the protected-call voice-origin restriction; a new UI request is required for affected changes during calls. See [User Configuration](User_Configuration.md).
- Mandatory microphone shutdown/release while the Windows session is locked, regardless of lock origin.
- Built-in plain-text clipboard capture and preview.
- Bounded concurrent independent sessions, including writes to different resources, with one task per session, shared-resource coordination, and an independently responsive work-management lane.
- Model-assisted contextual queue management, clarification when intent is ambiguous, and local cancellation/status controls.
- Spoken answers to "what are you currently working on?" and "what do you have left to do?", grounded in the work ledger.
- Copilot runtime integration, conditional on the integration proof.
- Local-only and remote-enabled modes with capability-aware routing.
- Explicit error, approval, and cancellation experiences.
- Host-owned proactive voice suggestions, grounded task notifications, and unprompted release-availability conversations.
- Call-aware gating with UI-only feedback and ignored reusable grants by default, manual call state, and exact single-use operation approval; voice-originated voice/in-call changes, including temporary/speak-once exceptions, require new UI initiation while protected. Teams automatic detection remains the first best-effort integration subject to capability proof.
- Encrypted permitted full session history/artifacts and local configuration storage; Active/Done retrieval survives restart without action/approval replay.
- Configurable inactivity policies defaulting to automatic Done after 24 hours and deletion after 30 days from last meaningful activity.
- Structured single-/multi-choice and text questions, concise/full answers, exact grants, and interchangeable voice/UI replies.
- Model-facing bounded session discovery/history/artifact and interaction tools mediated by host policy and context egress.
- Optional playback-bound speech text and bounded basic Markdown answer details, as defined in [Information Display](Information_Display.md); Mermaid/browser/HTML viewers follow their dedicated gates.
- Content-minimising diagnostic and action metadata.

Local-only mode must remain usable for local capture and transcription.
Slice A2 requires a supported Ollama-backed adapter and selected local model so the product's local-first answer path is proven.
On an installation where that runtime/model is absent or unhealthy, answering is explicitly unavailable until setup succeeds; cloud processing is not a fallback.
Installing or using an inference provider is not compulsory for an individual
user. Users who decline local and remote inference retain supported
deterministic built-in commands and native setup/recovery, subject to each
command's own dependencies and policy. This supported reduced mode does not
waive the product's required A2 adapter and acceptance evidence.

See [Work Management and Request Queue](Work_Management.md) for scheduling, context, and status semantics.
See [Out-of-the-Box Skills](Built_In_Skills.md) for bundled executable skills and session policy.
See [Commands, Tools, Skills, and Model Interaction](Commands_Tools_And_Skills.md)
for internal host tools, skill discovery/selection, app -> model -> app result
iteration, and exact local routes. These contracts apply within each delivered
capability; they do not bring general script authoring/import into the MVP.

The Ollama-backed adapter is a Slice A2 requirement, not a post-Slice-A aspiration.
Its exact supported model and hardware floor are resolved through [Decision Register D-003](Decision_Register.md#d-003-local-inference-baseline).
The bootstrap currently pins Ollama 0.35.1 and `qwen3:1.7b` by digest and
checks actual inference; hardware, performance, cancellation, quality, and
offline acceptance evidence is still required. PowerShell 7 setup and
storage/SQLite readiness are independent of this answer path. Script-backed
built-ins and their content-bound grants remain future work under
[skill and task execution design](../docs/skill-and-task-execution-design.md);
the exact direct lock still bypasses the gate that model-suggested lock uses.

### Slice B: Read-Only MCP and Skills

Add:

- One explicitly configured read-only MCP integration.
- MCP connection lifecycle, identity handling, tool discovery, and policy mapping.
- An additional bundled read-only integration skill that references a supported MCP tool by stable identifier.
- Read-only reuse of explicitly selected profile skill roots, with compatibility validation, revision pinning, and explicit enablement.
- Tool-result provenance, bounded output, and visible invocation status.
- Per-action remote transmission review when new tool results would be sent to a model.

Use a controllable test MCP server for acceptance testing. A real connector must separately pass the same permission and identity checks.

No repository writes, arbitrary scripts, or remote mutation tools are enabled in this slice.
The fixed computer-control scripts from Slice A are not a generic script execution capability.

### Slice C: Voice/Text Skill and Instruction Authoring

Add a narrowly scoped Builder workflow that develops usable skills and
standalone instructions through voice, typed, or mixed interaction:

1. Describe the desired skill verbally and clarify behaviour, inputs, and tools.
2. Generate and refine a declarative proposal without modifying installed skills.
3. Review the exact diff and hear a behaviour/capability summary.
4. Run built-in schema/dependency checks and data-only simulated examples.
5. Approve and save the validated revision to the dedicated user skill store.
6. Separately confirm exact enablement through voice or UI, then invoke it through either channel under normal task permissions.

Standalone instructions reuse proposal/refinement, rendered preview, exact
source/diff, validation, conflict handling and revision-bound Save, but use a
single-file editor without package fixtures or dependency tests. Saving does
not activate changed guidance; applicability follows the common definition
revision review rather than the skill's capability-enablement workflow.
Compatible profile instructions may replace only their exact discovered file
after identity and unchanged-base-digest revalidation.

See [Skill and Instruction Authoring](Skill_Authoring.md).
Kora-specific skills and optional copies of shared skills are saved under
`%APPDATA%\Kora\Skills`. The target also supports editing compatible personal
profile originals through the dedicated declarative writer with exact-change
confirmation, not generic filesystem or executable writes.
See [Skill Sources and Roaming Storage](Skill_Storage.md).
Apply [Definition Management](Definition_Management.md) for consistent native/
voice authoring and external reload across all supported kinds and sources.
Deliver kinds/formats incrementally; prompt support is lightweight flexibility,
and agent execution requires separate runtime qualification. Current R21
read-only inspection does not qualify these editing or reload operations.
External build/test commands and Git writes are outside the MVP skill-authoring workflow; it requires neither compiled code nor a commit.

This slice does not enable generic autonomous repository modification.
Kora cannot update its own code, binaries, executable extensions, or security/updater implementation, even with an agent-facing approval.

## Non-Goals for the First Release

- Continuous ambient transcription, speaker authentication, voice isolation, or mandatory biometric enrollment. Continuous local wake-word detection is included.
- Optional local speaker verification may add privacy/approval confidence under explicit user preferences; matching is not authentication or approval itself, and enrollment is not required for general voice use.
- Wake-word-free conversational follow-ups and unlimited/unvalidated activation names. Custom names with explicit custom-only/both choice follow [Custom Activation Names](Activation_Name.md).
- Clipboard monitoring, clipboard images/HTML/file lists, or automatic URL fetching.
- Screen capture, OCR, arbitrary desktop automation, or browser automation.
- General file/folder ingestion, knowledge indexing, embeddings, PDF/Office
  ingestion, or enterprise content caches. These remain staged R26 work under
  [File and Folder Ingestion and Grounded Reasoning](File_And_Folder_Ingestion.md);
  the initial release does not advertise even the narrower text/Markdown
  snapshot foundation.
- Work IQ as a mandatory dependency.
- A skill marketplace, automatic extension updates, or shared repository synchronisation.
- Arbitrary C# scripts, in-process third-party plugins, or unrestricted process execution.
- Agent-driven modification of Kora itself. Application updates are out-of-band maintenance, not skill authoring.
- Exposing Kora as an MCP server.
- Unbounded executors, intra-session parallel/multi-agent task workflows, uncoordinated conflicting writes, or cross-device federation. Bounded isolated concurrent sessions are included.
- Guaranteed offline answering without a compatible installed local runtime.
- Linux/macOS application support, installers, native backends, or multi-platform release matrices; architectural portability is required without implementing those ports.

## Release Boundary

The MVP release includes Slices A-C only after their acceptance gates pass.
Slice A checkpoint results are recorded separately; A0/A1 development demonstrations are not presented as a completed local-first release.
Each slice must remain usable and independently testable before the next is enabled.
If the Copilot integration proof fails, Slice A is blocked for Copilot; it must not ship with weaker controls.
An alternative runtime requires an explicit scope decision and must pass the same applicable gates.

## User Experience Constraints

- Typing is not required for the primary workflow.
- Before local voice prerequisites exist, use a minimal accessible setup UI; after consent and readiness checks, setup can continue by voice.
- After initial microphone consent, verbal activation does not require a click or keypress. Push-to-talk is an alternative, not a prerequisite.
- A complete accessible UI is an equal alternative for every supported workflow; no required spoken acknowledgment.
- Large results have a spoken summary and voice commands to navigate or explain them.
- High-risk confirmations require exact consequence/source/target review and deliberate action-specific voice or UI input; required OS/provider checks remain mandatory. Wake detection and optional speaker matching are not authentication or approval.
- Baseline enabled voice trusts the active unlocked Windows profile, not the speaker's identity; no compulsory biometric/PTT/UI-only authorization is introduced because the user cannot be identified.
- Optional separately consented local frequent-speaker learning may improve recognition/personalization after its quality/privacy gate; unavailable learning never blocks general voice or grants new authority.
- During protected calls, voice-originated voice-setting and in-call-option changes require new UI initiation, while ordinary operation approvals and safety/status controls retain their independent policy. This is the explicit channel-parity exception, not generic mouse superiority.
- With owner-aware private speech enabled, uncertain verifier output uses a neutral visual notice; baseline output otherwise follows configured privacy/call policy, and a match never bypasses that policy.
- Credentials and account sign-in use the provider's supported secure flow, not dictated secrets.

## Distribution Requirements

Provide both a clone/build/publish bootstrap script and CI-produced precompiled framework-dependent Windows binaries.
The binary option runs with the required .NET runtime and documented native dependencies, without Git, SDK, restore, or application compilation.
Both offer optional start-at-logon registration without weakening microphone/session policy.
Delivery does not require a preconfigured AI environment or provider selection.
Once launched, Kora initialises its own stores and retains detection/setup of
optional speech/provider/runtime requirements. An installer may provide extra
consented dependency assistance, but users need not select it or choose packages
upfront. Ollama provisioning remains available with the required Slice A2
adapter; users may instead select remote processing or decline inference
entirely and use the supported deterministic subset. See the
[optional-dependency setup contract](Environment_Setup.md#optional-dependencies-and-built-in-command-only-operation).
Binary deployments check the hosted release feed and proactively offer unsigned/manual update notices; installation remains external and Kora has no install-capable updater during the unsigned phase.
WiX MSI + custom Burn binary packaging and release automation are implemented; source bootstrap, protected deployment and installed acceptance remain open.
Source is public and source-available on GitHub. Build/test/cross-publish and release metadata/publication use Linux GitHub Actions wherever feasible, with explicit Windows jobs for WiX packaging and justified Windows-specific work.
Release notes and installer documentation disclose the known lack of Authenticode publisher identity and provide final-byte hashes/provenance; setup has no unsigned/POC banner.
Installed Windows acceptance evidence comes from a separate approved runtime-only environment; a Windows packaging job is not that evidence.
See [Distribution and Updates](Distribution_And_Updates.md).

## Dependencies and Decisions to Resolve Before Implementation

The authoritative owner, due checkpoint, evidence, and status for these items are maintained in the [Decision Register](Decision_Register.md).

- Copilot SDK control-point proof and runtime version pinning.
- Independently schedulable management inference/session support without sharing execution state or blocking local controls.
- Speech engine packaging, model availability, supported hardware, and distribution licences.
- A locally packaged detector for default "Kora" and advertised custom-name profiles, command endpointing, and playback echo rejection that pass the Slice A activation gates.
- Verified bundled computer-script execution profiles and authoritative Windows session/microphone policy integration.
- A local credential store appropriate for Windows.
- MCP SDK transport/authentication support and cancellation behaviour.
- Supported Windows versions and an acceptance-test reference machine.
- Protected Kora installation/source identities and a skill-store writer whose effective targets cannot escape into them.
- A Linux-first GitHub Actions cross-publish/provenance path with Windows WiX MSI/Burn packaging, actual installed lifecycle/runtime-only Windows evidence, and protected startup/maintenance registration.
- A bounded trusted setup catalogue, safe database migrations, and dependency ownership/health checks.
- Encrypted persistent session/event/artifact/index storage, journal/backup deletion and restart migration proofs.
- Provider session isolation/concurrency, enforced resource coordination, shared interaction schemas, and bounded history/routing tools.

These are implementation gates, not assumptions that the named technologies already satisfy the design.
