# Built-In Features and Extensibility

Status: proposed feature placement. The current PowerShell readiness probe is
not an executable skill runner; the current exact lock is a native API call,
and model-suggested lock uses a separate approval path. Future side-effecting
built-ins share the gate in
[skill and task execution design](../docs/skill-and-task-execution-design.md).

Related: [Architecture](Architecture.md), [Security and Data Flows](Security_Data_Flows.md), [MVP Scope](MVP_Scope.md).

## Placement Rule

Build into Kora anything that establishes consent, controls the task lifecycle, owns OS capture, or must work consistently across providers.
Use extension points for replaceable engines, external systems, specialised interpretation, and repeatable workflows.

"Built in" describes product responsibility, not a requirement to write every algorithm from scratch.
A built-in service can wrap a maintained library or use a bundled first-party adapter.
"Extensible" does not mean unrestricted, loaded into the UI process, or available in the MVP.
Platform extensibility follows [Architecture](Architecture.md#platform-boundaries-and-support): Windows alone is supported, but shared domain/policy logic must not depend directly on Windows APIs.
Native mechanisms live behind narrow trusted host contracts; platform adapters do not grant user plugins permission to replace security policy.
No speculative Linux/macOS backends or automatic platform parity are included.

## Feature Placement

| Feature | Built-in responsibility | Replaceable or external implementation | Initial scope |
|---|---|---|---|
| Desktop UI | Task state, previews, approvals, accessible controls | No arbitrary third-party UI code initially | Slice A |
| Rich information display | Speech text, typed surfaces, provenance, profile/asset/network policy, native controls | Admitted Markdown/Mermaid renderers and isolated browser adapters; never document-supplied plugins | Speech text/basic Markdown in Slice A; other viewers after proof |
| User configuration | Typed settings registry, voice/UI parity, validation, scope, reset/undo | Admitted adapters declare bounded options; no UI-only/private file configuration | Core in Slice A; feature options with their capability |
| Non-voice questions/recovery | Native typed mouse replies, microphone enumeration/consent/recovery, tray access, prompt identity | Audio device API beneath host-owned selection; no model/skill dependency | Slice A; always available |
| Microphone/activation | Device selection, wake-listening consent, capture, mute, visible status, optional push-to-talk | Audio library beneath host-owned capture | Slice A |
| Speech recognition | Transcript lifecycle, local-only policy, engine selection | Bundled local engine adapter; other engines later | Slice A |
| Text-to-speech | Playback queue, interruption, summary selection | Local speech engine/voice adapters | Slice A |
| Call-aware speech | Central speech gate, voice preferences, source aggregation/freshness, visual fallback, one-shot override | Narrow communication detectors; Teams first, Graph optional and consented | Gate/manual mode in Slice A; automatic detection only after proof |
| Wake word and endpointing | Default "Kora"/one validated custom name, bounded pre-roll, command boundaries, playback-aware interruption | Local detector/profile and VAD adapters; no connector installation required | Slice A; custom profiles need quality proof |
| Clipboard | Explicit OS snapshot, format/size checks, preview, provenance, permission | Parsers, OCR, explanations, and workflows | Text in Slice A |
| File/selection context | Explicit source selection, canonical paths, access checks | IDE/app bridges and format extractors | User skill revision selection in Slice C; general workspaces later |
| Screen capture | Capture consent, region/window scope, indicator, context lifecycle | OCR and visual analysis adapters | Later |
| Session/task state | Encrypted history/artifacts, Active/Done/retention, context budget, cancellation, structured questions and model history tools | Skills may guide a task, not replace lifecycle or self-submit answers | Slice A3/A4 |
| Work management/queue | Per-session ledger, bounded concurrent scheduling, resource leases, status grounding, session/prompt routing | Replaceable model adapter proposes contextual decisions; cannot execute tasks | Slice A3 |
| Proactive interaction | Event eligibility, consent/quiet preferences, speech scheduling, prompt binding | Admitted task integrations can supply scoped events, not maintenance authority | Slice A |
| Application maintenance | Notify-only release discovery during unsigned phase; future signed-metadata trust, exact host-owned approval and OS-gated staging/activation | Initial external manual replacement; future verified updater never exposed as an agent tool | Notify-only initially; install-capable updater deferred |
| Environment setup | Storage/schema initialisation, dependency catalogue, probes, consent, readiness, ownership | Registered first-party setup handlers; external engines installed only when selected/supported | Internal setup in Slice A; provider-specific setup with its adapter |
| Model execution | Capability negotiation, policy, task protocol | Copilot/Ollama/Foundry/OpenAI adapters | Copilot proof in Slice A |
| Tool execution | Schemas, policy mapping, approvals, results, audit | Registered bundled session/power actions; MCP tools later | Computer controls in Slice A; MCP in Slice B |
| MCP client | Transports, connection health, identity references, mediated calls | External servers supply domain functionality | Slice B |
| MCP server exposure | Authentication, export policy, user-session boundaries | Versioned exported Kora capabilities | Later, disabled by default |
| Knowledge retrieval | Query/filter rules, provenance, freshness, identity boundaries | Source connectors, parsers, embedding/ranking engines | Later |
| Enterprise integrations | Generic identity and data-handling controls | Work IQ, GitHub, Azure DevOps, SharePoint via MCP where supported | Later; not core dependencies |
| Skills | Schema, embedded built-in resource catalogue, discovery, version pinning, dependency checks | Built-ins embedded in the protected app binary; user-authored declarative packages | Lock skill in Slice A; integration skills in Slice B; authoring in Slice C |
| Scripts/processes | Trust checks, containment, approval, worker supervision, protected-resource enforcement | Fixed protected lock/shutdown/restart scripts; broader workers only after integrity gates | Computer scripts in Slice A; general scripts later |
| Session/microphone policy | Observe Windows session state; prohibit/release microphone use while locked | OS notification/device libraries beneath host-owned enforcement | Slice A; non-overridable |
| Builder | Voice/UI refinement and data-only proposal/diff/test; separate exact-confirmed save/enable with stale-content checks | Model generates skills; built-in validator checks them | Slice C |
| Git operations | Generic resource/action controls and protected-repository enforcement | Explicit Git tool adapter or MCP server | Later; cannot mutate Kora repositories |
| Browser/desktop automation | Policy and consent if ever supported | Dedicated external tool services | Later |
| Credentials/policy/audit | Protected storage, decisions, grant revocation, content-minimising records | Integrate supported OS/enterprise facilities | Slice A |

## Wake-Word Decision

Verbal activation with "Kora" is required from Slice A, not a later extension.
Users may change effective activation names through [Custom Activation Names](Activation_Name.md); engine/profile replaceability does not permit ambient transcription or untrusted executable installation.
Kora owns continuous local wake-listening consent, capture boundaries, mute state, and interruption.
The detector algorithm can be replaced behind a narrow local engine contract, but a working detector is bundled with the app.
An external MCP server or skill must not be required to activate Kora or receive ambient microphone audio.
Optional push-to-talk uses the same command pipeline and remains available when the user chooses not to enable wake listening.
See [Task Lifecycle](Task_Lifecycle.md#wake-listening-and-command-capture) for activation and playback behaviour.

## Clipboard Decision

Clipboard capture is a built-in context service, not a required external MCP server.

Reasons:

- It is part of the primary voice workflow and must work without installing a connector.
- Windows clipboard access has platform-specific threading and format semantics.
- Consent, capture time, and immutable snapshot identity must be consistent across runtimes.
- An extension should receive approved context rather than gain ambient clipboard visibility.
- Capturing only when requested avoids creating a background clipboard collection service.

MVP behaviour:

1. The user says "use the clipboard" or activates the equivalent control.
2. Kora reads plain text once and creates an immutable context item.
3. Empty, unavailable, unsupported, or oversized content produces a visible actionable result.
4. Kora previews the snapshot and handles egress approval before external processing.
5. Follow-ups refer to the snapshot; later clipboard changes are not automatically incorporated.

Clipboard write is a separate future permission and action. Read permission does not authorise it.
The MVP does not follow clipboard URLs, read listed files, render active HTML, or monitor changes.

Later, an OCR extension can process an explicitly captured image, and a skill can explain a stack trace.
Neither extension needs direct clipboard access.

## Extension Forms

### Skills: Behaviour, Not Authority

Skills contain versioned instructions, tool references, parameter schemas, and validation rules.
They cannot add permissions, suppress approvals, or turn source content into trusted instructions.
MVP user-authored skills are explicit user-selected declarative workflows, not executable code.
Bundled first-party skills may include fixed protected scripts, such as "lock the machine"; see [Out-of-the-Box Skills](Built_In_Skills.md).
Users can create and improve them by voice through [Skill Authoring](Skill_Authoring.md).
The dedicated skill store is writable data; bundled skills and executable components remain protected.
Shared profile skills can be referenced read-only after compatibility review and explicit enablement.
Kora-specific skills and edited copies live under `%APPDATA%\Kora\Skills`; see [Skill Sources and Roaming Storage](Skill_Storage.md).

Suggested manifest fields:

- Stable ID, version, description, and entry point.
- Required tool IDs and compatible schema versions.
- Required capabilities, requested resource scopes, and input/output schemas.
- Package origin and content digest.

Declared capabilities are requirements, not grants. Missing dependencies disable the skill with an explanation.

### MCP: External System Functionality

Prefer MCP for services that already have domain-specific APIs or independently managed integrations.
Kora owns connection configuration and tool admission, but an external server owns its implementation.

An MCP server is not trusted merely because it runs locally.
Transport, identity, allowed tools, destinations, and server-code trust must be reviewed.
Remote servers execute outside Kora's containment and retain their own service-side data responsibilities.

### Engine and Runtime Adapters

Speech, model, and future retrieval engines implement narrow versioned contracts.
Bundled first-party adapters may be loaded in process where justified; they are trusted application code.
Third-party executable adapters use a worker protocol and explicit installation trust.

Do not build a general in-process plugin loader for the MVP.

### Scripts

Scripts provide deterministic execution when a dedicated tool or MCP integration is insufficient.
They require approved code/version, resources, arguments, and execution trust.
Use narrowly scoped registered commands rather than an unrestricted shell exposed to the model.
The proposed lock skill uses an immutable bundled script through a fixed
registration, not arbitrary script execution. The current direct lock instead
uses a Windows API call without the model-suggestion approval gate; both
routes must share a version-bound action gate before the scripted capability
is advertised.
Approval alone is insufficient when a command could modify Kora: require enforceable protected-resource isolation or disable it.

## Installation and Updates

- No automatic discovery-and-execution from arbitrary repositories.
- Review origin, requested capabilities, and dependencies before enabling a package.
- Pin installed content by version and digest.
- Material changes to code, tool schemas, endpoints, or requested permissions invalidate affected grants.
- Disabling an extension blocks new calls and cancels active calls where supported.
- Removing an extension removes its registration and grants, not user-owned source repositories.
- Signatures and digests establish identity/integrity, not harmlessness.
- Agent-facing capabilities cannot install/update executable adapters, modify Kora, or invoke its maintenance/updater channel.

Marketplace distribution and automatic update policies are deferred.

## What Extensions Cannot Replace

Extensions cannot replace or bypass context classification, outbound consent, approval UI, credential isolation, task cancellation, permission evaluation, or audit receipts.
An extension requesting such access is outside the supported extension model.
