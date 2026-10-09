# Kora Design

Status: design index; proposed contracts and source-backed bounded deliveries
are labelled separately. The vision and complete release scope are not an
implemented capability statement.

[Vision Statement](Vision_Statement.md) describes the long-term product direction.
The documents below turn that direction into an initial delivery scope and architectural decisions.
Where the vision is broader or less specific, these documents define the proposed implementation constraints.

## Authoritative Qualification Policy

[Acceptance Criteria: Three-Tier Qualification Policy](Acceptance_Criteria.md#three-tier-qualification-policy)
owns the boundary: normal feature development merges with its directly
applicable unit/integration/security/compatibility/regression checks, independent
of unrelated experiments; capability/profile proofs gate only enabling,
advertising, packaging as available or materially changing that path; final RC
qualification evaluates an explicit enabled-capability manifest. Exclusions are
recorded as exclusions, never passed capabilities or a claim of full A0-A4/B/C.
Hard fail-closed authority/privacy/data-integrity gates and all affected mandatory
critical fixtures remain unchanged.

The roadmap's [I/E/Q Needs interpretation](Implementation_Roadmap.md#reading-needs)
distinguishes implementation dependencies from enablement and RC evidence,
explicitly permitting unrelated bounded feature work in parallel. Its
[experiment disposition inventory](Implementation_Roadmap.md#experiment-disposition-inventory)
retains inference, containment and speech as opt-in environment-qualified
harnesses outside default CI; migrates applicable runtime/lifecycle/storage/
.NET control/management assertions to maintained tests before executable
archival; and treats distribution/W2 as historical receipts unless mechanisms
change. No experiment code is deleted by this policy change. The
[cost/risk rationale](Implementation_Roadmap.md#cost-versus-unique-risk-reduction)
explains why unique native/hardware proof is retained without imposing duplicate
prototype suites on unrelated delivery.

## Implementation Status and Next Work

[Implementation Status and Delivery Roadmap](Implementation_Roadmap.md) compares
the design with source and checked-in tests, distinguishes delivered bootstrap
behavior from partial/outstanding features and missing release proof, and orders
remaining work by safety, user value and explicit dependencies.
It is the current delivery baseline; the numbered A0-A4/B/C checkpoints remain
acceptance milestones, not a claim that the bootstrap has completed any slice.
Its [current merged snapshot](Implementation_Roadmap.md#current-merged-snapshot---2026-10-07)
identifies the exact reviewed main SHA, merged PRs, maintained source and
experiment disposition separately from original/rebase validation receipts.
R01's initial-release policy reconciliation is
[approved and recorded](Decision_Register.md#r01-accepted-policy-reconciliation):
R02 feasibility and R03 ownership/privacy work can start. Runtime acceptance
remains open; standalone application rollback is deferred R27 work.
R02's storage/key investigation now informs the
[Windows durable-storage direction](Architecture.md#windows-durable-storage-direction),
[D-009 status and remaining gates](Decision_Register.md#d-009-session-persistence-and-retention),
and [concrete R02/R04/R12 follow-on work](Implementation_Roadmap.md#r02-storagekey-outcome-and-follow-on-work).
The runnable experiment is supporting evidence, not production admission:
the approved baseline is now bundled standard SQLite with verified private
profile permissions, not an unresolved encrypted-native/key selection.
Bounded task/evidence/interaction persistence, no-replay recovery, ordinary
diagnostic pruning and minimal session authority/workspace/metadata are
composed. Full conversation/artifact/backup lifecycle and deletion, audit
anchors/pruning and installed Windows loading/protection remain open.
Windows supplies ordinary
cross-profile isolation; routine second-account OS-denial trials are not
required for this profile-local application or for merging its research outcome.

[Deferred Proof Validation](Deferred_Validation.md) is the interactive-session
handoff for outstanding speech, storage/key, local-inference, runtime/provider,
containment and distribution validation. It links
safe reruns and proof-specific checklists while keeping consent/privilege
requirements and capability blockers distinct from merging partial research.
Its [proof-code disposition](Deferred_Validation.md#2026-10-05-safe-revalidation-and-proof-code-disposition)
also records what must migrate into production tests before an experiment can
be retired.

[R02 Local Inference Qualification and Technical Plan](Local_Inference.md)
records partial proof outcomes and their technical consequences. Candidate
identity/licence/download metadata and missing-runtime behavior are evidenced;
real model, CPU-floor and no-egress success are not. The
[R02-L1-L6 continuation](Implementation_Roadmap.md#r02-local-inference-continuation)
starts with test-owner/environment/budget approval, then consented provisioning,
actual trials, candidate disposition and integration. The proof remains
supporting evidence, not the only place this work is tracked.
The [LI01-LI07 checklist](../experiments/r02-local-inference-proof/README.md#outstanding-testing-checklist)
provides the later interactive-session procedures; merging partial research or
unrelated features does not close inference qualification, nor do independent
proof gates block those unrelated merges.

R02's [runtime/provider outcomes and technical path](Runtime_Provider_Feasibility.md)
now record actual Node candidate evidence, reject hook-only failed-result
mediation and require a .NET final-request/storage proof next.
The [runtime follow-up gates](Implementation_Roadmap.md#r02-runtimeprovider-follow-up-gates)
separate SDK parity, full lifecycle observation, the host management envelope
and approved-account trials. D-001/D-004 remain open; deterministic local
work is not held behind unapproved hosted-model tests.

## Current Bootstrap Boundary

The merged bootstrap also delivers a bounded
[Sessions workspace and native metadata controls](Interaction_And_Sessions.md#delivered-bounded-session-metadata-and-explicit-creation---2026-10-07),
[explicit local clipboard preview](../docs/commands.md#explicit-local-clipboard-preview),
six-ID [read-only host discovery](../docs/commands.md#read-only-host-discovery),
and nine-option [typed appearance controls](../docs/settings.md#appearance).
The [bounded exact-ID session command path](../docs/commands.md#bounded-exact-id-session-commands)
now shares native host lifecycle/metadata authority for typed and activated
voice help/list/status/inspect/create/rename/done/resume. It adds no model
session tools, name-based routing or independent scheduler. Sessions
names/empty creation are not conversations or queues; clipboard
preview/reuse never submits content to a model. The read-only catalogue does
not qualify model execution.

[Kora.Tools](../src/Kora.Tools/README.md) owns portable per-action C# effects
behind Core contracts and the Application gateway;
[Kora.Definitions](../src/Kora.Definitions/README.md) owns immutable bundled
resources. [Slash/activated-voice artifact invocation](../docs/commands.md#run-skills-and-future-artifacts)
selects bundled or bounded compatible profile instructions for the existing
local-model selector; it is not a script executor or agent runtime.
Source-generated companion logging preserves typed audit authority.
Native [evidence inspection](../docs/privacy-safety-and-logs.md#logs) supports
SQLite and independent DailyLog; All remains SQLite-only, and file mirrors
never become audit authority. Ordinary startup pruning is not audit/session
deletion, and combined-source/model/export workflows remain unavailable.

R10 also delivers a bounded [installed speech-provider/voice configuration
slice](User_Configuration.md#delivered-bounded-installed-speech-choices-r10):
shared typed discovery/current/default/recovery and exact UI/typed/activated-voice
set/reset, coherent atomic selection persistence, revisions and existing
protected-call original-channel gates. Asset provisioning, other speech options,
model settings tools and acoustic acceptance are not established by this slice.

R10/R04 now additionally registers
[future-only SQLite diagnostic days](User_Configuration.md#delivered-bounded-future-only-sqlite-diagnostic-retention-r10r04):
native/exact typed/ACTIVATED discovery/get/status/set/reset, canonical integer
1–365/default-reset30, independent original-user/session admission and required
audit/atomic readback/receipt activation. Existing deadlines never change;
apply-now is unavailable. Audit90/domain, daily files30/30, history, every grant/
approval and pruning schedule remain unchanged; full R04/R10 remains open.

The [bounded R03/R09 native tray recovery](Interaction_Fallback.md#delivered-bounded-r03r09-tray-recovery)
provides truthful generic input state, five-second single-flight metadata
refresh, revision-bound microphone preference selection (including System
and retained unavailable pins), explicit PTT enable/disable and existing
playback Stop speaking. It adds no capture, wake, microphone test, automatic
selection or generic question workflow. Full native/audio acceptance remains
open; existing tray/navigation/maintenance/exit behavior is preserved.
The subsequent [passive native microphone recovery card](Interaction_Fallback.md#delivered-bounded-native-microphone-recovery-card---2026-10-07)
shares Tray/Settings exact displayed choices, local draft highlight, revision-bound
Save preference only and separate fresh endpoint-bound Enable. It does not
create durable R05 question/session/task authority, combine consent with
selection/enable, test/capture audio or change Windows permission. Enable still
only arms existing PTT; first-run/full matrix and native/hardware acceptance
remain open.

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

The [Dependency Catalogue](Dependency_Catalogue.md) inventories current optional
software, speech/model assets and setup-path tools alongside known future
requirements. It separates bundled/launch prerequisites from user-selected
capabilities and experimental candidates; installing a dependency is not
adapter admission or release acceptance.

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
24. [Local Inference Qualification and Technical Plan](Local_Inference.md): R02 outcomes, candidate/compatibility/context/resource consequences and the path to D-003/D-007 qualification and A2 integration.
25. [File and Folder Ingestion and Grounded Reasoning](File_And_Folder_Ingestion.md): deliberate path selection, immutable source revisions, retrieval/citations, local and hosted model boundaries, voice/settings behavior, and the staged R26 delivery plan.
26. [Model Providers, Memory, and Grounded Knowledge](Model_Providers_Memory_And_Knowledge.md): explicit Ollama/Copilot session modes and handoff, user-governed durable memory, provider-independent local retrieval, image treatment, provenance, and delivery sequencing.

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
- Kora supports qualified Ollama and Copilot adapters through explicit Local only, Local first, and Hosted preferred session modes. One provider handles one model turn; local-to-hosted handoff is reviewed and never triggered solely by model-reported confidence.
- Models do not learn user facts from ordinary inference. Cross-session memory is Kora-owned, deliberate, scoped, inspectable, editable, and deletable; models may propose memories but cannot persist them directly.
- Large knowledge sources are indexed and retrieved locally. Both providers receive only bounded cited evidence; hosted use additionally requires the exact destination and outbound envelope to pass current egress policy.
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
- Source-bootstrap and precompiled runtime-only binary deployment are both required; [WiX MSI + custom Burn binary packaging and release automation](Distribution_And_Updates.md#selected-windows-installer-direction) are implemented, with source/installed/protection acceptance still open. NSIS executable paths are retired; historical receipts and remaining source/native checks are retained.
- The repository is public and source-available on GitHub. Use Linux Actions for build/test/cross-publish and release work wherever feasible, with explicit Windows WiX packaging jobs, unsigned-artifact disclosure, final-byte hashes/provenance and separate installed Windows acceptance.
- Delivery installs Kora; the running application sets up its stores and approved requirements for selected capabilities, reusing existing installations.
- Kora initiates eligible conversations, including unsigned/manual update notices; initial update handling is notify-only and cannot download, stage, execute, or install replacement code.
- Automatic speech defaults to visual-only during calls or uncertain enabled-detector state; users configure this verbally and can request a single spoken response.
- Every workflow supports voice, mouse/keyboard, or mixed interaction; explicit voice/UI approvals share Windows-session trust, with optional speaker confidence and mandatory OS/provider checks preserved.
- OOTB computer/app/queue controls have explicit scopes; shutdown/restart require fresh action-specific confirmation through either channel, safe work handling, and a cancellable countdown.
- Management power tools propose only; the deterministic host lifecycle
  controller owns approval/countdown and gated fixed-action dispatch.
- Ongoing microphone consent is explicit on first launch and persists separately
  from run-scoped disablement/recovery holds; ordinary safe restart can use it,
  normal unlock restores only previously enabled readiness after fresh gates,
  while resume and other loss recovery require explicit enablement within the run.
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
