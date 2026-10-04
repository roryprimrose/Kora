# Kora Design

Status: proposed design, not an implemented capability statement.

[Vision Statement](Vision_Statement.md) describes the long-term product direction.
The documents below turn that direction into an initial delivery scope and architectural decisions.
Where the vision is broader or less specific, these documents define the proposed implementation constraints.

## Current Bootstrap Boundary

The current Windows bootstrap independently checks/initialises Kora storage and SQLite,
checks PowerShell 7 readiness, and offers consented PowerShell setup without requiring
local inference. For local reasoning it offers a separately consented per-user
Ollama 0.35.1 installation and `qwen3:1.7b` download, verifies the pinned model
digest and a completed inference response on the loopback endpoint, and reports
readiness and setup progress. Exact built-in commands remain deterministic;
unmatched requests can use the verified local model for answers, clarifying
questions, and validated proposals for registered actions and grant changes.
Model-suggested side effects, including lock, require host approval; the
current model-action duration choices are once, session, and always.
The exact direct lock phrase still calls the Windows API without that common
approval gate. A PowerShell readiness check is not a script runner: embedded
`.ps1` skills, script review, and content/hash-bound execution grants are not
implemented. [Skill and task execution design](../docs/skill-and-task-execution-design.md)
specifies that future gate for both direct and suggested side-effecting tasks.
None of this closes the hardware/performance/offline proof in D-003 or the
broader approval-policy evidence in D-008.

## Reading Order

1. [MVP Scope and Non-Goals](MVP_Scope.md): what ships first and what does not.
2. [Architecture and Delivery Decision Register](Decision_Register.md): owners, due checkpoints, evidence, and status for unresolved blocking choices.
3. [Architecture and Contracts](Architecture.md): ownership, components, and runtime integration.
4. [Built-In Features and Extensibility](Extensibility.md): feature placement and extension boundaries.
5. [Security and Data Flows](Security_Data_Flows.md): consent, identities, execution, and retention.
6. [Task Lifecycle and Recovery](Task_Lifecycle.md): voice interaction, cancellation, and failure behaviour.
7. [Work Management and Request Queue](Work_Management.md): contextual scheduling, concurrent management, and grounded status.
8. [Voice-Driven Skill Authoring](Skill_Authoring.md): creating usable skills without modifying application code.
9. [Out-of-the-Box Skills and Session Policy](Built_In_Skills.md): protected scripted skills and mandatory microphone lock policy.
10. [Skill Sources and Roaming Storage](Skill_Storage.md): read-only profile reuse and Kora-specific skill partitions.
11. [Distribution and Updates](Distribution_And_Updates.md): source bootstrap, compiled binaries, startup, and maintenance boundaries.
12. [Proactive Voice Interaction](Proactive_Interaction.md): Kora-initiated conversations and trusted prompt routing.
13. [Environment Setup and Readiness](Environment_Setup.md): Kora-owned first-run storage and capability-based dependency provisioning.
14. [Call-Aware Speech](Call_Aware_Speech.md): communication detection limits and voice-configurable output gating.
15. [OOTB Phrase Catalogue](OOTB_Phrases.md): computer, application, queue, speech, skill, and maintenance commands.
16. [User Configuration](User_Configuration.md): verbally discoverable settings, defaults, scopes, and safety boundaries.
17. [Information Display](Information_Display.md): optional speech text, browser/static HTML viewing, Markdown and Mermaid.
18. [Mouse-Based Interaction and Voice Recovery](Interaction_Fallback.md): first-run microphone choice, visual questions, and device-loss recovery.
19. [Custom Voice Activation Names](Activation_Name.md): local renaming with explicit custom-only or default-plus-custom choice.
20. [Single Active Instance and Version Handoff](Instance_Coordination.md): duplicate activation, approved release/debug takeover, and original-version return.
21. [Acceptance Criteria](Acceptance_Criteria.md): evidence required before release.

## Visual Exploration

[Branding](Branding.md) defines the proposed vertical infinity-loop identity,
its assistant-state gradient, monochrome fallbacks, and small-size samples.

[Ambient UI study](Ambient_UI.md) explores a hidden-by-default, transparent desktop
presence and three animated visual directions. The [interactive browser prototype](Prototypes/ambient-ui.html)
is an offline design mockup, not an implemented application or a change to release scope.

## Key Decisions

- One assistant is active per Windows user across builds/checkouts; identical-build launches reveal it, while different-build takeover and return require explicit approval.

- Windows is the only supported application platform for the foreseeable future; portable shared logic and trusted platform boundaries preserve extensibility without committing to Linux/macOS ports.
- Kora owns task lifecycle, context selection, policy, approvals, and presentation.
- A replaceable agent-runtime adapter may own its model/tool loop only when Kora can enforce the required controls.
- Copilot SDK support requires an integration proof; SDK capabilities are not assumed.
- Local wake activation is primary from Slice A, defaulting to "Kora"; renaming asks custom-only or default-plus-custom, with custom-only recommended for shared offices. Push-to-talk is optional.
- Work management runs alongside one task executor; the model interprets queue intent and asks when ambiguous, while the host enforces scheduling and approvals.
- Clipboard capture is built in. Interpretation and downstream workflows are extensible.
- Local-only mode never silently falls back to a remote service.
- Optional local speaker verification is a per-profile privacy confidence signal, never authentication or approval; private speech falls back to a neutral visual notice when owner confidence is absent.
- External integrations normally use MCP; repeatable instructions use skills.
- Arbitrary code is not made safe merely by running it out of process.
- Users develop declarative skills by voice; agent capabilities cannot modify Kora's own code or executable components.
- Built-in skill definitions and scripts are embedded resources in the protected application binary, without loose-file overrides or writable extraction; microphone use is prohibited while Windows is locked.
- Shared profile skills are read-only references; Kora-specific skills and reviewed edited copies live in `%APPDATA%\Kora\Skills`, with device-local enablement.
- Source-bootstrap and precompiled runtime-only binary deployment are both supported; package format is not yet selected.
- The expected repository is public/open-source on GitHub; Linux Actions build/package/publish initially unsigned Windows releases, with explicit unsigned-artifact disclosure, final-byte hashes/provenance, and Windows validation outside the required build pipeline.
- Delivery installs Kora; the running application sets up its stores and approved requirements for selected capabilities, reusing existing installations.
- Kora initiates eligible conversations, including unsigned/manual update notices; initial update handling is notify-only and cannot download, stage, execute, or install replacement code.
- Automatic speech defaults to visual-only during calls or uncertain enabled-detector state; users configure this verbally and can request a single spoken response.
- OOTB computer/app/queue controls have explicit scopes; shutdown/restart require a second named spoken confirmation plus native secure confirmation outside the speech/model path.
- Every supported preference has verbal discovery/get/set/reset operations through the same host service as the UI; mandatory safety rules are not configurable bypasses.
- Optional speech text, rich answer details, and browser/HTML viewing are separate host-owned surfaces; rendered content cannot act as trusted controls.
- Native questions always accept mouse input; first-run microphone selection and device-loss recovery do not depend on speech, a model, or a network.
- A persistent system tray icon/context menu provides the primary non-voice route to choose a microphone, enable listening, and reach pending questions while Kora is hidden.
- Delivery starts with a voice-to-clipboard vertical slice, then read-only MCP, then an approved write workflow.

## Document Maintenance

Changes to scope, permission semantics, runtime ownership, or retention must update the affected documents and acceptance criteria together.
Technology names in the vision are candidates until their integration, distribution, and licensing requirements have been verified.
