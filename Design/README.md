# Kora Design

Status: proposed design, not an implemented capability statement.

[Vision Statement](Vision_Statement.md) describes the long-term product direction.
The documents below turn that direction into an initial delivery scope and architectural decisions.
Where the vision is broader or less specific, these documents define the proposed implementation constraints.

## Implementation Status and Next Work

[Implementation Status and Delivery Roadmap](Implementation_Roadmap.md) compares
the design with source and checked-in tests, distinguishes delivered bootstrap
behavior from partial/outstanding features and missing release proof, and orders
remaining work by safety, user value and explicit dependencies.
It is the current delivery baseline; the numbered A0-A4/B/C checkpoints remain
acceptance milestones, not a claim that the bootstrap has completed any slice.
R01's initial-release policy reconciliation is
[approved and recorded](Decision_Register.md#r01-accepted-policy-reconciliation):
R02 feasibility and R03 ownership/privacy work can start. Runtime acceptance
remains open; standalone application rollback is deferred R27 work.

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
4. [Built-In Features and Extensibility](Extensibility.md): feature placement and extension boundaries; [Commands, Tools, Skills, and Model Interaction](Commands_Tools_And_Skills.md) defines capability discovery, app -> model -> app execution/result flow, and local command routing.
5. [Security and Data Flows](Security_Data_Flows.md): consent, identities, execution, and retention.
6. [Task Lifecycle and Recovery](Task_Lifecycle.md): voice interaction, cancellation, and failure behaviour.
7. [Work Management and Request Queue](Work_Management.md): contextual scheduling, concurrent management, and grounded status.
8. [Voice-Driven Skill Authoring](Skill_Authoring.md): creating usable skills without modifying application code.
9. [Out-of-the-Box Skills and Session Policy](Built_In_Skills.md): explicit embedded manifests, Markdown and shared multi-script packaging, deterministic grant hashes, and mandatory microphone lock policy.
10. [Skill Sources and Roaming Storage](Skill_Storage.md): read-only profile reuse, Kora-specific partitions, and future folder-based executable-skill dependency discovery.
11. [Distribution and Updates](Distribution_And_Updates.md): source bootstrap, compiled binaries, startup, and maintenance boundaries.
12. [Proactive Voice Interaction](Proactive_Interaction.md): Kora-initiated conversations and trusted prompt routing.
13. [Environment Setup and Readiness](Environment_Setup.md): Kora-owned first-run storage and capability-based dependency provisioning.
14. [Call-Aware Speech](Call_Aware_Speech.md): communication detection limits and voice-configurable output gating.
15. [OOTB Phrase Catalogue](OOTB_Phrases.md): computer, application, queue, speech, skill, and maintenance commands.
16. [User Configuration](User_Configuration.md): verbally discoverable settings, defaults, scopes, and safety boundaries.
17. [Information Display](Information_Display.md): optional speech text, browser/static HTML viewing, Markdown and Mermaid.
18. [Shared Questions and Voice Recovery](Interaction_Fallback.md): equal voice/UI question paths, first-run microphone choice, and device-loss recovery.
19. [Custom Voice Activation Names](Activation_Name.md): local renaming with explicit custom-only or default-plus-custom choice.
20. [Single Active Instance and Version Handoff](Instance_Coordination.md): duplicate activation, approved release/debug takeover, and original-version return.
21. [Acceptance Criteria](Acceptance_Criteria.md): evidence required before release.
22. [Internal Model Tool Catalogue](Internal_Model_Tools.md): complete current action/proposal inventory, proposed internal tools, caller lanes, capability gates, and host-only exclusions.
23. [Session Workspace and Coordinated Windows](UI_Workspace_And_Windows.md): compact interaction, session list plus full conversation workspace, detail/script review, native cards, concurrent work UX, and supporting windows.

## Human Interaction and Sessions

[Human Interaction and Persistent Sessions](Interaction_And_Sessions.md) is the canonical contract for voice/UI parity, structured questions, concise/full responses, risk-based grants, concurrent work streams, routing, durable history, and configurable inactivity policies.
It replaces the earlier single-executor, memory-only-conversation direction and distinguishes Kora work sessions from Windows and provider sessions.
Read it before the task/work-management and presentation documents.
Read [Session Workspace and Coordinated Window Design](UI_Workspace_And_Windows.md) alongside it for the primary UI design: the session list sits beside full conversation/history, with a lightweight compact view and separate details.

## Accepted Security Direction

[Security and Data Flows](Security_Data_Flows.md#accepted-controls-and-verification-boundary) records the accepted active-Windows-profile trust boundary, scoped grants and call restrictions, and host control requirements.
Optional [frequent-speaker learning](Security_Data_Flows.md#optional-local-frequent-speaker-learning) is separately consented local personalization, not authentication or authorization.
Design-level concerns are resolved within that boundary; implementation evidence remains required before capabilities are advertised or released.

## Capability References

- [Tool and Built-In Skill Technical Reference](Tool_And_Skill_Reference.md):
  complete design-defined logical operations, inputs/results, policy lanes,
  dependencies, bundled skill contracts, and current implementation labels.
- [Tools and built-in skills user guide](../docs/tools-and-built-in-skills.md):
  request examples, expected behavior, approvals and current/planned limitations.

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
- Commands identify user intent; internal host tools expose Kora functionality; skills describe outcomes and compose admitted tools/tasks. Registered executable tasks, not skill names, are the unit of content-bound execution permission.
- Kora advertises relevant tool definitions and enabled skill summaries; the model proposes, the host validates/authorises/executes, and approved structured results return for model continuation. Exact local controls use the same services and applicable gates without inference.
- A replaceable agent-runtime adapter may own its model/tool loop only when Kora can enforce the required controls.
- Copilot SDK support requires an integration proof; SDK capabilities are not assumed.
- Local wake activation is primary from Slice A, defaulting to "Kora"; renaming asks custom-only or default-plus-custom, with custom-only recommended for shared offices. Push-to-talk is optional.
- Independent persistent sessions execute concurrently within verified limits; the model proposes routing/queue intent, while the host enforces isolation, resource coordination, scheduling, and approvals.
- Clipboard capture is built in. Interpretation and downstream workflows are extensible.
- Local-only mode never silently falls back to a remote service.
- Optional local speaker verification is a per-profile privacy confidence
  signal, never authentication or approval; private speech falls back to a
  neutral visual notice on absent/uncertain confidence only while owner-aware
  protection is enabled. Baseline voice uses normal output/privacy/call policy
  without compulsory verification.
- External integrations normally use MCP; repeatable instructions use skills.
- Arbitrary code is not made safe merely by running it out of process.
- Users develop declarative skills by voice; agent capabilities cannot modify Kora's own code or executable components.
- Built-in skill manifests, Markdown, and all required scripts are embedded resources in the protected application binary, without loose-file overrides or writable extraction. Scripts may be shared; grants bind a deterministic combined-content hash and the exact skill definition/invocation. Microphone use is prohibited while Windows is locked.
- Shared profile skills are read-only references; Kora-specific skills and reviewed edited copies live in `%APPDATA%\Kora\Skills`, with device-local enablement.
- Source-bootstrap and precompiled runtime-only binary deployment are both supported; package format is not yet selected.
- The expected repository is public/open-source on GitHub; Linux Actions build/package/publish initially unsigned Windows releases, with explicit unsigned-artifact disclosure, final-byte hashes/provenance, and Windows validation outside the required build pipeline.
- Delivery installs Kora; the running application sets up its stores and approved requirements for selected capabilities, reusing existing installations.
- Kora initiates eligible conversations, including unsigned/manual update notices; initial update handling is notify-only and cannot download, stage, execute, or install replacement code.
- Automatic speech defaults to visual-only during calls or uncertain enabled-detector state; users configure this verbally and can request a single spoken response.
- Every workflow supports voice, mouse/keyboard, or mixed interaction; explicit voice/UI approvals share Windows-session trust, with optional speaker confidence and mandatory OS/provider checks preserved.
- OOTB computer/app/queue controls have explicit scopes; shutdown/restart require fresh action-specific confirmation through either channel, safe work handling, and a cancellable countdown.
- Management power tools propose only; the deterministic host lifecycle
  controller owns approval/countdown and gated fixed-action dispatch.
- Ongoing microphone consent is explicit on first launch and persists separately
  from run-scoped disablement/recovery holds; ordinary safe restart can use it,
  but unlock/resume and loss recovery require explicit enablement within the run.
- An unaddressed standalone lock creates a durable Active control work session;
  Session approval is offered only after its binding is committed and presented,
  never from the selected window.
- General requests create a new session unless clearly related to an Active session; explicit targeting wins and archived sessions resume only explicitly.
- Full permitted session history survives restart and Done; defaults archive after 24 inactive hours and delete after 30 inactive days, with both settings configurable.
- Every supported preference has verbal discovery/get/set/reset operations through the same host service as the UI; mandatory safety rules are not configurable bypasses.
- Optional speech text, rich answer details, and browser/HTML viewing are separate host-owned surfaces; rendered content cannot act as trusted controls.
- Native questions always accept mouse input; first-run microphone selection and device-loss recovery do not depend on speech, a model, or a network.
- A persistent system tray icon/context menu provides the primary non-voice route to choose a microphone, enable listening, and reach pending questions while Kora is hidden.
- Delivery starts with a voice-to-clipboard vertical slice, then read-only MCP, then an approved write workflow.

## Document Maintenance

Changes to scope, permission semantics, runtime ownership, or retention must update the affected documents and acceptance criteria together.
Technology names in the vision are candidates until their integration, distribution, and licensing requirements have been verified.
