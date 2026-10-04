# MVP Scope and Non-Goals

Status: proposed release scope, not the set of features in the current bootstrap.
That bootstrap implements consented local-model readiness and verified
inference for unmatched requests, deterministic commands, and initial
model-proposed actions/grants; it does not complete Slice A or close D-003/D-008.

Related: [Vision](Vision_Statement.md), [Architecture](Architecture.md), [Decision Register](Decision_Register.md), [Acceptance Criteria](Acceptance_Criteria.md).

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

Slice A is delivered through independently accepted implementation checkpoints.
A checkpoint may be used for development evaluation but is not advertised as the completed product outcome until all Slice A checkpoints pass.
Do not begin a later checkpoint while an earlier checkpoint has unresolved safety/correctness failures.

1. **A0 — Deterministic shell:** native setup/recovery, explicit push-to-talk, local transcription/TTS, clipboard snapshot/preview, one remote runtime, egress approval, streaming display, cancellation, and no model-assisted work management.
2. **A1 — Voice-first activation:** packaged local "Kora" detector, immediate wake-and-command preservation, playback rejection, interruption, locked-session policy, and the required wake quality/resource evidence.
3. **A2 — Local-first answering:** one supported Ollama-backed inference adapter and selected model pass local-only clipboard-answering, cancellation, setup, hardware, licence, and performance gates. Cloud is not a fallback.
4. **A3 — Sessions, managed work and controls:** encrypted session/event/artifact persistence and retention, deterministic session controls, bounded independent routing/management, isolated concurrent scheduler/resource leases, authoritative per-session ledger/queue, structured questions/grants, configuration registry, and protected computer/application controls.
5. **A4 — Ambient interaction surfaces:** compact session interaction, session manager/history/detail UI, proactive task/update conversations, call-aware speech, optional owner-aware private speech, and bounded presentation details.

Each checkpoint preserves all earlier controls and has a separately reported acceptance result.

1. The user configures a microphone and a supported runtime and consents to local wake-word listening.
2. The user says "Kora, explain the clipboard"; local detection activates command capture without requiring a pause after "Kora".
3. Kora transcribes locally and snapshots clipboard text only after that request.
4. Kora displays the transcript, selected context, and intended processing destination.
5. When the context is going to a remote runtime, Kora obtains transmission approval.
6. The runtime produces a streamed visual answer.
7. Kora speaks a short summary using local text-to-speech.
8. The user can stop speech, cancel the task, or ask a follow-up using the same approved snapshot.

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
- Verbal discovery/get/set/reset of all supported user preferences through a typed host registry, as defined in [User Configuration](User_Configuration.md).
- Mandatory microphone shutdown/release while the Windows session is locked, regardless of lock origin.
- Built-in plain-text clipboard capture and preview.
- Bounded concurrent independent sessions, including writes to different resources, with one task per session, shared-resource coordination, and an independently responsive work-management lane.
- Model-assisted contextual queue management, clarification when intent is ambiguous, and local cancellation/status controls.
- Spoken answers to "what are you currently working on?" and "what do you have left to do?", grounded in the work ledger.
- Copilot runtime integration, conditional on the integration proof.
- Local-only and remote-enabled modes with capability-aware routing.
- Explicit error, approval, and cancellation experiences.
- Host-owned proactive voice suggestions, grounded task notifications, and unprompted release-availability conversations.
- Call-aware speech gating with voice-configurable preferences, manual call mode, and explicit speak-once override; Teams automatic detection is the first best-effort integration subject to capability proof.
- Encrypted permitted full session history/artifacts and local configuration storage; Active/Done retrieval survives restart without action/approval replay.
- Configurable inactivity policies defaulting to automatic Done after 24 hours and deletion after 30 days from last meaningful activity.
- Structured single-/multi-choice and text questions, concise/full answers, exact grants, and interchangeable voice/UI replies.
- Model-facing bounded session discovery/history/artifact and interaction tools mediated by host policy and context egress.
- Optional playback-bound speech text and bounded basic Markdown answer details, as defined in [Information Display](Information_Display.md); Mermaid/browser/HTML viewers follow their dedicated gates.
- Content-minimising diagnostic and action metadata.

Local-only mode must remain usable for local capture and transcription.
Slice A2 requires a supported Ollama-backed adapter and selected local model so the product's local-first answer path is proven.
On an installation where that runtime/model is absent or unhealthy, answering is explicitly unavailable until setup succeeds; cloud processing is not a fallback.

See [Work Management and Request Queue](Work_Management.md) for scheduling, context, and status semantics.
See [Out-of-the-Box Skills](Built_In_Skills.md) for bundled executable skills and session policy.

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

### Slice C: Voice-Driven Skill Authoring

Add a narrowly scoped Builder workflow that develops usable skills through voice:

1. Describe the desired skill verbally and clarify behaviour, inputs, and tools.
2. Generate and refine a declarative proposal without modifying installed skills.
3. Review the exact diff and hear a behaviour/capability summary.
4. Run built-in schema/dependency checks and data-only simulated examples.
5. Approve and save the validated revision to the dedicated user skill store.
6. Separately confirm exact enablement through voice or UI, then invoke it through either channel under normal task permissions.

See [Voice-Driven Skill Authoring](Skill_Authoring.md).
Kora-specific skills and edited copies of shared skills are saved under `%APPDATA%\Kora\Skills`; shared profile sources are never edited.
See [Skill Sources and Roaming Storage](Skill_Storage.md).
External build/test commands and Git writes are outside the MVP skill-authoring workflow; it requires neither compiled code nor a commit.

This slice does not enable generic autonomous repository modification.
Kora cannot update its own code, binaries, executable extensions, or security/updater implementation, even with an agent-facing approval.

## Non-Goals for the First Release

- Continuous ambient transcription, speaker authentication, voice isolation, or mandatory biometric enrollment. Continuous local wake-word detection is included.
- Optional local speaker verification may add privacy/approval confidence under explicit user preferences; matching is not authentication or approval itself, and enrollment is not required for general voice use.
- Wake-word-free conversational follow-ups and unlimited/unvalidated activation names. Custom names with explicit custom-only/both choice follow [Custom Activation Names](Activation_Name.md).
- Clipboard monitoring, clipboard images/HTML/file lists, or automatic URL fetching.
- Screen capture, OCR, arbitrary desktop automation, or browser automation.
- Full knowledge indexing, embeddings, PDF/Office ingestion, or enterprise content caches.
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
- With owner-aware private speech enabled, uncertain verifier output uses a neutral visual notice; baseline output otherwise follows configured privacy/call policy, and a match never bypasses that policy.
- Credentials and account sign-in use the provider's supported secure flow, not dictated secrets.

## Distribution Requirements

Provide both a clone/build/publish bootstrap script and CI-produced precompiled framework-dependent Windows binaries.
The binary option runs with the required .NET runtime and documented native dependencies, without Git, SDK, restore, or application compilation.
Both offer optional start-at-logon registration without weakening microphone/session policy.
Delivery provides Kora, not a preconfigured environment. Once launched, Kora initialises its own stores and offers setup of the chosen speech/provider/runtime requirements.
Ollama provisioning is available with the required Slice A2 adapter, but installation remains optional for users who select only remote processing.
See [Environment Setup](Environment_Setup.md).
Binary deployments check the hosted release feed and proactively offer unsigned/manual update notices; installation remains external and Kora has no install-capable updater during the unsigned phase.
Package format remains a separate decision.
Source is expected to be public/open-source on GitHub, with Linux GitHub Actions building, packaging, and publishing initially unsigned official Windows releases.
Release and setup surfaces disclose the lack of Authenticode publisher identity and provide final-byte hashes/provenance.
Windows-specific acceptance evidence comes from an external Windows environment, not an assumed required Windows build job.
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
- A Linux GitHub Actions cross-publish/package/provenance path for unsigned artifacts, external Windows acceptance evidence, runtime-only deployment verification, and protected startup/maintenance registration.
- A bounded trusted setup catalogue, safe database migrations, and dependency ownership/health checks.
- Encrypted persistent session/event/artifact/index storage, journal/backup deletion and restart migration proofs.
- Provider session isolation/concurrency, enforced resource coordination, shared interaction schemas, and bounded history/routing tools.

These are implementation gates, not assumptions that the named technologies already satisfy the design.
