# Commands, Tools, Skills, and Model Interaction

Status: interaction and implementation contract. Proposed tool loops, skill
runners and agent profiles are not enabled by this document.

The [canonical model-tool catalogue](Internal_Model_Tools.md) owns IDs, schemas,
lanes and unavailable descriptors. The [roadmap](Implementation_Roadmap.md)
owns delivered status; [Commands](../docs/commands.md) owns current exact syntax.
Native delivery or instruction selection never grants model, execution,
approval, session or egress authority.

For capability-specific limits, use [configuration](User_Configuration.md),
[source storage](Skill_Storage.md), [file ingestion](File_And_Folder_Ingestion.md),
[providers/memory](Model_Providers_Memory_And_Knowledge.md) and
[security](Security_Data_Flows.md), rather than repeating their delivery
narratives here.

## Delivered Artifact Invocation

Kora has one source-qualified artifact invocation route for bundled skills and
compatible skill, instruction, prompt, and agent definitions. The route is
available from the typed composer and activated voice:

- direct slash command: `/lock`, `/restart`, or `/shutdown`;
- kind-qualified slash command: `/skill lock`;
- activated voice: `Kora, run lock`, `Kora, use the restart skill`, or
  `Kora, use <artifact> to <request>`.

`ArtifactDefinition` is the portable Core contract for the artifact identity,
kind (`Skill`, `Instruction`, `Prompt`, or `Agent`), display metadata, slash command,
spoken names, source, version, definition digest, and bounded instructions.
`Kora.Definitions` owns the fixed mapping from embedded resources to those
definitions. Command and spoken-name conflicts fail catalogue construction.
The same `ArtifactCommandRouter` parses typed and voice input, so adding future
instruction or prompt registrations does not create another presentation-only
dispatcher.

Selection produces a `LocalModelArtifact` that is passed separately from user
text to the qualified local-model adapter. The adapter puts source-qualified
artifact instructions in the system context and keeps the user's request in
the request prompt. Clarification turns retain the exact selected artifact.
Unknown or incomplete slash commands fail closed and are not reinterpreted as
free-form model requests.

The response composer binds its `/` dropdown to that same catalogue. A bare
slash lists all available artifacts; command text and optional
`/skill `, `/prompt `, `/instruction `, or `/agent ` qualification filter it. Pointer
selection and Up/Down/Enter insert the canonical direct command; Escape closes
the list. Each entry shows command, name, description, and source.

At composition, the embedded catalogue is combined with bounded compatible
disk definitions. Kora-owned roaming `Skills`, `Instructions`, `Prompts`, and
`Agents` folders and recognized VS Code and VS Code Insiders customization
locations are read without whole-profile scanning or reparse traversal.
Settings can register up to sixteen additional absolute directories for each
artifact kind. Additional directories use the same patterns and processing
pipeline as default roots. Strict UTF-8, byte/file/depth bounds,
frontmatter, skill folder/name agreement, `user-invocable`, and catalogue
uniqueness are validated. Invalid or conflicting discovery fails closed.
Kora watches default and configured roots, debounces disk notifications, builds
a complete candidate catalogue, and atomically publishes it only after every
definition and cross-artifact uniqueness rule passes. Invalid intermediate or
saved content leaves the last known-valid catalogue active and surfaces a
reload failure. Added, changed, renamed, and removed artifacts become available
without restarting Kora.

Artifact identity includes its exact content digest. A reload retires any
in-flight reasoning, clarification, or action approval whose selected
ID/digest is no longer current. Consequential actions proposed from artifact
instructions never reuse legacy action grants and can receive only a fresh
single-use approval for that exact current revision; they cannot create
session or always grants. This prevents changed instructions from inheriting
authority established for an earlier or unrelated proposal.

### Owned metadata inventory navigation

The delivered owned native editor also provides literal metadata search, observed-use filters and exact stale-safe row inspection for its existing Instruction/Prompt filename inventories only.
Explicit List/Refresh uses existing admission/reconciliation; filtering remains in-memory and model-free, with no source writes, activation or first-result loading. Typed/current-name activated List/Inspect opens the same native route; no new model tool or typed search grammar is registered.
Observed catalogue state is not current disk integrity. Unavailable/stale choices require Refresh/reselection while the original/draft stays open. See [owned inventory controls](../docs/instruction-editor.md#search-observed-owned-metadata); profile-original editing and the full definition source matrix stay deferred.

### Proposed Instruction Authoring

The existing native owned Instruction/literal Prompt editor delivers only [open-draft structural Validate](../docs/instruction-editor.md#validate-the-open-draft-without-saving), through its button and exact plural-kind `validate` controls. It shares Save proposal preparation, not a model tool or conversational authoring engine.
Validation observes captured bytes and existing catalogue metadata without store I/O, reload, preference/use mutation or network/model activity. Findings grant no approval or execution authority; Save independently confirms the current prepared bytes. Profile-original and broader authoring remain deferred.

Standalone Kora and compatible profile instructions will use the same
conversational proposal, rendered preview, exact source/diff, validation,
conflict and native Save-confirmation experience as declarative skill
authoring. The instruction variant edits one `*.instructions.md` file and omits
the skill package tree, fixtures, dependency tests and skill
capability-enablement workflow. Changed instruction applicability still
requires exact revision/scope review. Voice and typed input may propose edits.
Profile saves may replace only the exact discovered source file after
file-identity and base-digest revalidation; they do not provide a general
model-directed file writer. This authoring workflow remains proposed; live
read-only rediscovery above is delivered.

Running an artifact means applying its declarative instructions to the current
model request. It does not execute a packaged script, approve an operation,
create a grant, or prove an outcome. A selected artifact may lead the model to
propose an existing registered host action, but that proposal still converges
on the normal host-owned validation, approval, audit, privacy, and execution
path. The embedded session-control scripts remain inspection-only until their
separate runtime and containment work is admitted.

### MCP configuration sources and future request use

The same Settings source list also accepts additional MCP server configuration
**files**. Kora reads its own roaming `Mcp\mcp.json`, VS Code and VS Code
Insiders `User\mcp.json`, and Claude Desktop
`claude_desktop_config.json` when present. Additional entries must be absolute
file paths. Both the VS Code `servers` shape and the `mcpServers` shape are
accepted under strict UTF-8, JSON depth, byte, server-count, transport, and
identity bounds. A configured missing or malformed file fails closed.
These configuration files are watched under the same debounced reload
lifecycle, so server metadata and app-level selection surfaces refresh without
an application restart.

Configuration inspection retains only a source-qualified server ID, name,
transport class (`stdio` or HTTP), source file name, and app-level
included/excluded state. Commands, arguments, URLs, headers, environment
values, and credentials are not copied into presentation or model context.
Inclusion is an audited device-local preference and is not connection,
capability, account, egress, tool, or execution approval.

Live MCP use remains gated by R20. When that runtime is delivered, every
voice, typed, and native-UI request must resolve MCP availability through one
host-owned catalogue:

1. begin with app-included servers only;
2. intersect that set with the session's explicit included server and
   capability selection;
3. perform fresh connection/capability discovery and policy admission;
4. expose only admitted capability names and bounded descriptions to the
   selected model for that request;
5. route every proposed call through the normal permission, approval, egress,
   audit, cancellation, and result-provenance boundaries.

Unknown session selection, stale discovery, unavailable identity, or missing
policy state excludes the server and its capabilities. Model awareness never
comes from raw configuration JSON and cannot itself include a server, create a
grant, connect, or invoke a capability.

## Responsibility and Terminology

The [canonical catalogue](Internal_Model_Tools.md) owns tool IDs and caller lanes;
the [user guide](../docs/tools-and-built-in-skills.md) explains current behavior.
This page owns shared interaction semantics; [Interaction and Sessions](Interaction_And_Sessions.md)
owns durable work-session identity, routing, history and concurrency.

"Built in" describes a capability's product ownership and source, not a single execution mechanism.
Kora exposes its own functionality as host tools and packages repeatable outcomes as skills.
The model selects or proposes; the trusted application validates, authorises, executes, and presents.

| Concept | Responsibility | Example |
|---|---|---|
| Command / intent | User-facing entry point identifying a request; exact phrases belong to a host-owned routing catalogue | "What is the state of the current session?" |
| Host action | A registered operation with typed inputs, effect classification, and an implementation owned by Kora | Query session state, open settings, stop speech |
| Tool | A model-facing invocation contract for an admitted capability, with an input schema and structured result | `sessions.get` |
| Skill | A source-qualified, versioned package describing an outcome, selection guidance, inputs, instructions/workflow, and required tools/tasks | "Lock the machine" |
| Executable task | A registered effect implemented by exact approved resources and a permitted invocation | `session.lock` backed by an embedded `.ps1` |
| Prompt | Optional explicitly invoked saved-request template; not an execution engine or ongoing policy | Summarise this document as decisions, risks, and next steps |
| Instructions | Reviewed scoped guidance applied to relevant requests; never host policy or execution authority | Use Australian English in this session |
| Agent profile | Declarative specialised role and bounded references/constraints; not a running agent or grant | Read-only review role using admitted skills/tools |
| Agent / runtime | The model/tool iteration that requests capabilities, consumes results, and continues the admitted task | Ask for status, receive observed state, explain the blocker |
| Grant | Host-owned permission for an exact operation and scope; executable grants bind implementation bytes and invocation | Once/session/always approval for the registered lock task |

A host action can be exposed as a tool and invoked directly from a command or UI.
A skill can use several tools, or describe one simple executable task.
A registered skill task can itself have a tool interface; skill and tool are complementary layers.
Do not require a skill or PowerShell process for every internal Kora operation.
Conversely, selecting a skill does not execute a script or create a grant.

### Action exposure is the default

For every new user-visible Kora action, design review must consider three
entry points backed by the same registered host action:

1. a deterministic UI control where visual interaction is appropriate;
2. one or more exact host-owned commands for common, unambiguous requests; and
3. a typed model-facing action/tool descriptor so a model can identify the
   action from natural language and propose it through the normal host gate.

Implement all applicable entry points rather than making ordinary actions
UI-only or requiring model inference for an exact common command. Exact
commands and model proposals must converge on the same validation,
authorization, execution, audit, cancellation, and result implementation;
they must not grow independent effect handlers with different safety rules.
The model-facing description includes when to use and not use the action, its
typed inputs, availability, effects, and result semantics. Advertising an
action lets a model identify and propose it; it never grants authority to
execute it.

Omit an entry point only for a concrete reason, such as no meaningful static
phrase, required interactive/visual selection, excessive parameter ambiguity,
privacy or origin restrictions, an unavailable dependency, or a capability
that has not passed its containment and authorization proof. Record that
exception in the capability catalogue with the supported alternatives. Do not
use implementation convenience, a missing screen, or the assumption that a
model can paraphrase everything as the reason to omit deterministic access.

Capability work is incomplete until focused tests prove that every declared
exact phrase and admitted model proposal resolves to the intended registered
action, that both routes apply the same policy, and that unavailable,
unauthorized, ambiguous, or invented actions fail closed. Catalogue tests must
also detect conflicting normalized phrases and descriptors that advertise an
action with no registered dispatcher.

An executable task here means a registered effect; a scheduled user task in the work ledger can contain several such invocations.
Keep their identities distinct in proposals, approvals, and receipts.
Identifiers and payloads below are illustrative contract concepts, not a published SDK or manifest schema.

## Registries and Model Discovery

### Built-In Tool Source Layout and Implementation

Built-in tool action implementations belong in the portable `Kora.Tools`
project. Group actions by capability in source control and mirror the grouping
in the C# namespace:

```text
src/Kora.Tools/Clipboard/ClipboardRead.cs       -> Kora.Tools.Clipboard.ClipboardRead
src/Kora.Tools/Clipboard/ClipboardReuse.cs      -> Kora.Tools.Clipboard.ClipboardReuse
src/Kora.Tools/Clipboard/ClipboardRevoke.cs     -> Kora.Tools.Clipboard.ClipboardRevoke
src/Kora.Tools/Clipboard/ClipboardSnapshotBroker.cs
```

Each registered action has its own concrete class with one typed execution
entry point, consumer-focused dependencies, explicit outcomes and focused
tests under `tests/Kora.Tools.UnitTests/<CapabilityGroup>`. Do not put every
action in one `Clipboard` class with a method per tool. Do not create action
classes for unavailable speculative capabilities merely to populate folders;
`ClipboardWrite` is a separate effect and is not delivered by this read-only
snapshot slice.

An action class may delegate to a shared cohesive broker or domain service.
Snapshot bounds, identity, origin, freshness, revocation and admission policy
have one authoritative implementation, not copied policy in each action.
Exact commands, native controls and any qualified model adapter converge on
the same action class. Registries/gateways own dispatch and shared wire
validation, not a growing set of capability-specific implementations.

`Kora.Tools` targets portable .NET and references Core, not Application,
Windows, Avalonia or model/provider SDKs. Portable contracts and authoritative
domain rules remain in `Kora.Core`; native mechanisms and OS exceptions
remain behind injected seams in `Kora.Windows`; Application owns request
orchestration/presentation state, and the desktop host supplies composition
and native controls. Application may reference Tools, never the reverse.
Keep provider-specific schema binding separate from the host action.

Canonical tool IDs (for example `context.capture_clipboard`) and schema
versions are explicit catalogue contracts, not derived by reflecting class
or method names. Having an implementation or action class does not enable
model exposure or confer authority: unsupported caller lanes, missing
qualification, privacy/egress and other mandatory gates still fail closed.
Document the concrete unavailable reason and supported deterministic route.
Tools, Definitions, Core and Application are all subject to the existing 100% portable
line/branch coverage gate; moving code cannot remove it from coverage.

**Delivered R06 layout:** `ReadOnlyCapabilityRegistry` retains the common
caller/current-host admission, strict JSON parsing, complete serialized-output
bounds, trace/cancellation and structured logging gateway. Its six typed
implementation targets are now separate classes:

| Canonical ID | Source class in `Kora.Tools` |
|---|---|
| `capabilities.list` | `Capabilities/CapabilitiesList.cs` |
| `capabilities.get` | `Capabilities/CapabilitiesGet.cs` |
| `application.get_version` | `Application/ApplicationGetVersion.cs` |
| `readiness.get` | `Readiness/ReadinessGet.cs` |
| `runtime.list` | `Runtime/RuntimeList.cs` |
| `runtime.get_status` | `Runtime/RuntimeGetStatus.cs` |

Those R06 execution entry points are internal to the admitted host gateway,
not public SDK/reflection shortcuts. DI registration does not register a tool
with a model. Canonical IDs, descriptors, schema/effects, limits, lane admission,
recorded observations and unavailable tool-loop status are unchanged.
The application-version observation contract belongs in Core, with the
assembly-version implementation in Application; Tools has no reverse dependency.
Legacy built-in command handlers in `MainViewModel`
are not yet generic model tools; migrate only a capability whose actual
registered tool contract and shared authority path are in scope, rather than
reclassifying all methods or helper services as tools.

### Bundled Definitions and Agent Profiles

Use one portable `Kora.Definitions` project for bundled behavior content:
skills, reusable prompt templates, shared scoped instructions and future
agent profiles. These content kinds share explicit catalogue registration,
immutable embedding, bounds, version/digest handling and passive source review;
they do not need separate projects solely because they have different file
extensions. `Kora.Tools` remains separate because it implements executable
C# host actions.

[Definition Management](Definition_Management.md) defines their common
file/source lifecycle, kind-specific application, equal UI/voice management,
editing, and external reload. Prompts remain optional reusable-content support
for a voice-first product; they do not warrant a parallel capability engine.

The delivered owned instruction/versioned prompt editor uses one closed
Instruction/Prompt writer and admitted save/conflict/review machinery.
Exact `instructions copy <new-id>` / `prompts copy <new-id>` and native **Save as new copy** review the current supported native owned draft (instruction v1; prompt v1/v2), create a distinct disabled same-kind owned file without replacement, and retain the original draft.
Separate inspection/use review is mandatory; no profile fork, inherited authority, model route or reference retargeting is added.
Native **Disable reviewed use** and exact `instructions disable` / `instructions stop-use` (with matching `prompts` controls and current-name activation) use the same admitted revision/audit workflow, not the file writer or a new tool/grant authority.
Confirmation binds the clean inspected supported native owned source/kind/digest/native revision (instruction v1; prompt v1/v2). Durable per-kind holds exclude new selections/descriptors but retain inspectable source bytes and already captured immutable reasoning; pending unadmitted choices retire without retargeting.
Dirty drafts and failed/unconfirmed revocation retain explicit recovery; reload/restart/restored bytes never re-enable use, and a fresh exact current-run review is required. Profile-original editing and runtime gates remain unchanged.
Only host-derived immediate `.instructions.md` / `.prompt.md` targets beneath
the corresponding Kora data roots are writable. Prompt selection remains
explicit user-request content through the existing request/provider/approval
route; it does not register tools or policy. Owned native v2 alone supports required literal `${input:name}` parameters through a private bounded native field form, complete expanded USER REQUEST preview and separate fresh `ModelTurnHost` admission. Values are never commands/system instructions, recursively expanded or persisted.
V1 reader semantics remain unchanged. Provider qualification, authentication and egress are independent: production generation remains unavailable, without a rejected-runtime fallback. See the
[bounded management increment](Definition_Management.md#bounded-owned-instruction-and-prompt-editor).

The source layout is grouped by content kind, with C# loader/catalogue
namespaces mirroring their actual folders:

```text
Kora.Definitions/
  Skills/
    EmbeddedSkillCatalogue.cs        -> Kora.Definitions.Skills
    Lock/                            -> manifest, instructions, fixtures, entry script
    Shutdown/
    Restart/
    session-control.ps1              -> declared shared skill helper
  Prompts/                           -> when actual prompt resources are introduced
  Instructions/                      -> when shared scoped resources are introduced
  Agents/                            -> when supported agent definitions are introduced
```

Only the delivered Skills files currently exist. Do not create empty folders,
placeholder loaders or fictitious advertised capabilities. Keep each skill's
own instructions, fixtures and executable-resource closure together; reserve
`Instructions` for shared guidance rather than splitting every skill package.
Resource files have explicit stable IDs, not C# namespaces. Relocating an
assembly or folder must preserve IDs, original bytes, manifests and package
digests unless a separately reviewed content/version change is intended.
The three built-ins retain their existing `Kora.Skills.*`/`Kora.Scripts.*`
resource IDs and independent golden digest vectors in `Kora.Definitions.dll`.

Definitions references Core for authoritative schemas/domain validation, not
Tools, Application, Windows, Avalonia or model/provider SDKs. Consumers resolve
the embedded first-party catalogue explicitly; there is no directory/assembly
scan that turns arbitrary files into enabled skills. Scripts are inert
packaged resources until a separately qualified host runner admits execution.
Do not move protocol enforcement, authorization or security checks into
Markdown, templates or model instructions. Existing model-selector framing
and runtime code are unchanged by this resource migration.

**Agent execution direction, not delivered execution:** compatible agent
Markdown can now be loaded as a declarative instruction artifact. A future
agent runtime may support named task profiles that reference instructions,
skills and a bounded subset of admitted tools, with typed input/output and
runtime/locality/budget constraints.
A profile is not a running agent, scheduler lane or new permission system.
The host owns each run's session/task/request identity, original origin,
resource leases, cancellation, budgets and approval/egress decisions.
Profile text cannot approve effects, acquire arbitrary context, expand the
tool subset or select an unapproved processing destination. No recursive
delegation, concurrent worker creation, profile auto-enablement or provider
fallback follows from loading a definition. Qualification of the actual
model/tool loop and host runtime remains mandatory; loading an agent artifact
does not introduce an agent executor.

The host maintains related but distinct registries:

- A command/intent catalogue maps canonical phrases and aliases to host actions or original bundled skill/task identities.
- A tool catalogue binds stable IDs and schema versions to trusted implementations, host-assigned effects, and policy.
- A skill catalogue binds source-qualified IDs to enabled, digest-pinned revisions, descriptions, selection guidance, and required tool/task references.
- An executable-task catalogue resolves admitted task IDs to exact scripts/native adapters, dependencies, validated parameters, targets, and execution profiles.

These registries are separate views over admitted capabilities, not permission
to implement the same effect repeatedly. A capability inventory records which
UI, exact-command, model-action/tool, and skill surfaces exist and why any
normally applicable surface is absent.

The application supplies the relevant available tool definitions and skill summaries to the runtime before asking it to interpret a request.
Each tool definition includes its ID/version, purpose, input schema, and result semantics.
Each skill summary includes origin/revision, purpose, when to use it or not use it, required inputs, and dependency availability.
The model must know these capabilities exist; it must not infer executable access from product names or invent tool IDs.

Load a selected skill's bounded, pinned instructions/workflow only after host resolution.
Keep skill content separate from host policy and approval controls under the existing instruction-trust rules.
The task runtime receives the approved instruction data and required admitted
tool definitions to reason about the next step; deterministic declarative
workflow steps are interpreted by the host's bounded engine and use the same
invocation gate. Sending summaries or selected instructions to a remote runtime
follows context-egress policy; local discovery is not transmission permission.
The model does not need the lock script's source to select or invoke its registered task.
Script source is available to the user through read-only review, not automatically added to model context.
Do not expose a generic shell, model-selected executable path, or arbitrary PowerShell text in place of a registered task.

Discovery/enablement is not permission to invoke.
Missing or disabled dependencies are reported explicitly, without automatic installation or substitution.
The host revalidates availability and permission at dispatch, even if the model saw an earlier catalogue snapshot.
User skills may describe matching examples but cannot register reserved aliases, shadow originals, or assign themselves priority.
Ambiguous skill names or targets require a source-qualified choice or clarification, not arbitrary selection.

## App -> Model -> App Interaction

Voice activation and local transcription produce request text; typed input supplies text directly.
Both enter the same request-routing and capability contracts.
Input channel remains attached for approval, privacy, and audit policy.
Deliberate activated speech and native UI express equivalent ordinary intent;
mandatory OS/provider checks remain separate. During protected calls, the
host rejects voice-originated voice/in-call settings changes and requires a
new UI request rather than relabeling the origin after confirmation.

```text
User voice / typed input
          |
          v
Kora: request identity, local controls, intent routing, approved context
          |
          +-- Exact reserved/local command ----------------------+
          |                                                    |
          v                                                    |
Model: tool definitions, skill summaries, request/context        |
          |                                                    |
          +-- Answer / clarification -> host presentation        |
          |                                                    |
          +-- Skill selection -> host resolves pinned workflow   |
          |                   -> approved instructions         |
          |                   -> runtime tool/task proposal ---+
          +-- Tool/task proposal -------------------------------+
                                                               |
                                                               v
                         Kora: resolve, validate intent/inputs,
                               evaluate grants, ask if required,
                               revalidate, execute, record result
                                                               |
          +----------------------------------------------------+
          |
          +-- Model path: approved bounded result -> model continues
          |                                            |
          |                                            v
          |                                      Kora presents answer
          +-- Direct path: Kora presents result without inference
```

1. Bind the request to its trusted input lineage, durable session/task identity, and applicable processing destination. Explicit session targeting wins; otherwise continue only a clear Active-session match or create a new session. Route exact local controls before inference.
2. For unmatched natural language, supply only approved request/context and the relevant capability definitions. The management lane may propose scheduling or clarification; executable tool use remains in the task runtime or the narrow host-owned priority route.
3. Accept a typed answer, clarification, source-qualified skill selection, or tool/task invocation proposal. Display text is not an operation. Resolve a selected skill to its pinned workflow and permitted references; return approved instructions to the runtime or interpret declarative steps in the bounded host engine. Selection alone never dispatches its script, and subsequent tool/task proposals must not execute the effect twice.
4. Validate IDs, schemas, parameters, targets, intent lineage, current dependencies, and host policy. The model's claim that an action is safe or already approved has no authority.
5. Reuse an applicable exact grant first. Otherwise apply the proposed [session level](Session_Permission_Levels.md#levels-and-decision-order): Review all asks, Model review uses a tools-disabled reviewer, and Approve all skips ordinary review. Disclosure consent stays explicit and may share the action card. Replies bind to the host proposal, not model interpretation.
6. Revalidate immediately before execution and dispatch through the admitted implementation. Exact phrases, model proposals, UI task invocations, and skill workflows share the applicable action gate.
7. Return a correlated structured result: observed data or success/failure/unknown/denied/cancelled outcome, observation time, provenance, and any action receipt. An accepted proposal or script exit alone is not proof of the requested effect.
8. For model-mediated work, assess result egress before returning bounded result data to the same task runtime. It may answer or propose another permitted step, within lifecycle limits. Each step retains its own checks; denial never authorises retries or alternate tools.
9. Kora renders the response under speech, privacy, and locked-session policy. Cancellation invalidates pending calls/approvals and suppresses late model responses.

Native SDK function/tool calling and typed inference responses are alternative transports for this contract.
An agent SDK can own iteration only if every invocation and result transmission remains mediated.
An inference adapter uses the shared Kora-owned loop.
In neither case does the model directly execute application code.

The [bounded read-only continuation implementation](Internal_Model_Tools.md#bounded-provider-neutral-continuation-implementation---2026-10-20)
now supplies that host-owned loop over a provider-neutral portable transport.
It consumes only the existing six read-only capabilities through the admitted
host callback. Complete-message validation precedes serial one-use calls;
genuine correlated results remain inert data, and a final answer uses the
existing response contract. Live host revalidation, complete cumulative byte,
call/round and deadline limits, no retries and actual transport termination
remain mandatory. This is not a running provider, a skill/agent executor or
permission to use the legacy action selector as a tool loop. Production
qualification and default enablement remain unavailable.

Safe/Approve-all admissions create no grants. Commands/UI, MCP, generated/stored scripts and skill steps share the host decision; unsupported profiles remain unavailable. [Call approval eligibility](Session_Permission_Levels.md#voice-approval-and-protected-calls) restricts spoken approval by default, not grants or the level. These are proposed additions, not implemented transports.

## Internal Tool Example: Current Session State

"What is the state of the current session?" naturally selects a read-only host tool, not a script-backed skill.
For example, `sessions.get` with an explicitly resolved Kora session ID returns
a policy-filtered work snapshot. Host listening/Windows-session facts are
separately scoped observations, not another user's session state:

```json
{
  "invocationId": "status-17",
  "sessionId": "session-12",
  "status": "success",
  "observedAt": "2026-10-04T12:00:00Z",
  "data": {
    "activity": "waiting_for_approval",
    "activeTask": null,
    "pendingApproval": { "action": "session.lock" },
    "listening": true
  }
}
```

The query reads authoritative host state and does not open a window, speak, approve the pending action, or change the session.
The model can use the returned facts to answer "Am I waiting on anything?" without inventing task progress.
An exact status command uses the same query service and a deterministic presenter, with no model dependency.
Privacy may withhold sensitive fields; the result must identify withheld/unavailable data without disclosing it through speech.
Read-only does not imply unrestricted data access or remote transmission.

Kora may proactively supply a small approved status snapshot instead of requiring a tool call for every question.
Snapshots carry observation time and are context, not commands or proof that state is still current.
A fresh query is needed when the answer depends on newer observations.
Management inference uses its minimal authoritative ledger snapshot and typed management protocol, not general execution-tool access.

## Built-In Skill Example: Lock the Machine

The bundled lock skill describes the user outcome and its selection rules.
Its manifest references the registered `session.lock` task and embedded script identity; the host chooses the implementation, not the model.
The skill requires an explicit request to lock the current Windows session.
Quoted text, instructions found in a document/tool result, or a question about how locking works do not establish that intent.

- Exact "lock the machine" routing selects the original bundled registration locally, without inference.
- A natural-language variation such as "Please lock my computer" can be interpreted using the advertised skill summary, with clarification if the target or intent is unclear.
- Both routes resolve the same task, complete embedded script set and definition digest, dependencies, parameters, and execution context under the common grant gate. Shared helpers are included, not only the entry point.
- Once applies to one invocation; Session binds the operation to its identified Kora work session; Always/Perpetual is independently retained without expiry or eviction. None approves another task or changed implementation.
- The user can review every exact script and the combined script-set hash before approving through deliberate voice or UI. Selecting/enabling the skill or opening review does not authorise execution.
- A changed script, manifest/invocation, adapter, or executable dependency invalidates affected grants; the host computes identity and never accepts a model-supplied hash.
- Kora observes the Windows session event, records confirmed/failed/unknown outcome, and enforces microphone shutdown regardless of lock origin.

The original lock registration has narrowly host-admitted priority during other work.
It is not another general executor and does not give management inference script access.
Independent Kora sessions can otherwise execute concurrently under verified
limits, one task per session and resource-conflict leases; lock/power lifecycle
coordination applies across all sessions.
Lock-screen privacy can prevent any spoken/visible completion; retain the receipt without exposing private results while locked.
See [Bundled Skills](Built_In_Skills.md) and [Execution Grants](../docs/skill-and-task-execution-design.md) for containment and power-action rules.

## Local Controls and User Experience

The app -> model -> app path is for semantic interpretation, not a dependency for every interaction.
Exact help, basic status, stop-speech, cancellation/pause controls, essential lifecycle controls, and registered direct computer intents remain locally routable when a model is unavailable or busy.
Local routing removes inference, not effect-specific approval or active-work confirmation.
Kora must never run a script merely to show settings or stop playback.

Help/discovery distinguishes internal tools, enabled skills and their sources, dependency limitations, and grants.
A skill detail surface explains what it does, required inputs, registered tasks, and whether execution needs approval.
Grant inspection remains a separate host-owned surface.
Users ask for outcomes in ordinary language; they need not speak tool IDs, read a manifest, or name a script file.
An unavailable model leaves exact local routes usable and reports that semantic interpretation is unavailable rather than guessing.

## Current Bootstrap and Migration

Today the exact command catalogue selects C# handlers before local reasoning.
The Ollama request includes action names/descriptions and a limited status snapshot; the model returns one JSON answer, question, action, or grant-change proposal.
Kora validates it and may dispatch the handler, but does not return a structured tool result to a continuing model/tool loop.
There is no formal skill discovery/selection protocol, embedded script runner, or content-bound execution grant.
Direct exact lock remains ungated while model-suggested lock requires approval; this is not the intended common policy.

Migrate capability by capability: separate state queries from presentation, register typed tools and results, add mediated iteration/discovery, and resolve bundled skills to verified tasks.
Preserve existing exact aliases, offline controls, input-channel policy, and truthful errors.
Keep a script-backed task unavailable until its runner, containment, content-bound grants, and both entry paths pass acceptance.
User authoring remains declarative in Slice C; these contracts do not enable arbitrary script creation/import or close runtime/grant decisions D-001/D-008.
