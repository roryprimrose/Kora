# Skills, Prompts, Instructions, and Agent Profile Management

Status: categorized Customizations editing, reviewed source writes/reload,
persisted runtime exclusions and bounded authoring/invocation workflows are
implemented. Built-in sources remain read-only. General executors, automatic
registration/applicability and complete source/runtime qualification remain gated.
The [roadmap](Implementation_Roadmap.md#current-delivery) owns delivery status;
[Customizations](../docs/customizations.md) owns current user operations.

This is the authoritative management contract for all four definition kinds.
[Skill Storage](Skill_Storage.md) owns source-read and skill package/execution rules;
[Skill Authoring](Skill_Authoring.md) supplies declarative skill validation.
Transactions reuse the native definition-file access owner, not a second file
writer. Independent local agent runs consume the same exclusion policy;
disable/re-enable cannot revive old reviews. Foreground-editor activated voice
uses bounded intent/native clarification, not speech/model-profile qualification.

## Catalogue freshness

The existing supported-source watcher observes the nearest existing ancestor
when a recognized root has not yet been created, including when several parent
directories are absent. External creation then refreshes the same catalogue
without a restart or configuration toggle. Directory rename, move and deletion
within discovery depth also refresh it, including dotted directory names.
Notifications are filtered by exact source ancestry, file patterns and existing
depth bounds before discovery; watching an ancestor grants no broader content
read or source registration. Reparse exclusions, file budgets, debounce and
truthful reload failures remain unchanged. Reload retires stale selected
requests, questions and approvals through the existing consumers; owned
revisions still require separate exact-use review and enablement. Disk events
never invoke a definition or adopt an approval.

## Selected metadata Find

The owned editor's **Find in selected metadata** is a separate, ephemeral native
literal Find over exactly one current, selected inventory row's displayed
metadata details. It reuses admitted metadata state and the shared bounded
literal/count policy; count/ordinal cover only that string's complete
nonoverlapping sequence, with no wrap. The source editor/draft, hidden fields,
other rows/kinds and resource bodies are excluded. Query edits/Clear are
read-free and do not retarget inventory or modify drafts. Native match focus
must succeed; failure is explicit, not a success claim. Selection/page/filter,
source/control/host/privacy epochs and review/window retirement invalidate
private state and late callbacks. Metadata Find has no discovery, file-read,
mutation, clipboard, model, execution, trust, saved-revision or use authority.
It adds no Ctrl+F binding and confines Enter/F3 to its own controls.
See the [editor guide](../docs/instruction-editor.md#find-within-selected-displayed-metadata-details).

## Product Roles

The [bounded owned skill editor](../src/Kora/OwnedSkillEditorWindow.cs) supports native v1 instruction-only packages and additive native v2 selection of exactly the existing host WebPageGet capability. This is immutable disabled publication followed by separate source/target review, enablement, exact request/URL confirmation, independent action approval and genuine retained model answer, not field preparation or automatic Customizations registration ([complete operation](../docs/owned-skill-authoring.md#author-and-use-one-host-webpageget-skill)). General package executors, arbitrary tools/effects and executable resources remain excluded.

| Kind | Value and application |
|---|---|
| Skill | Primary reusable capability: selection guidance, inputs, workflow, and references to admitted tools or resources. Explicit voice/UI invocation and qualified model selection resolve the same enabled immutable revision. |
| Prompt | Optional saved request, explicitly selected for one new request. Owned native v1 remains literal; native v2 supports bounded required `${input:name}` text, complete private expanded USER REQUEST preview, and separate fresh request admission. No expressions, workflow engine, policy or actions. |
| Instruction | Reusable guidance with an explicit reviewed applicability scope. The host resolves applicable enabled revisions when admitting a request; file discovery alone never inserts guidance into model context. |
| Agent profile | Declarative specialised role referencing instructions, skills, and a bounded admitted tool subset, with input/output, locality, runtime, and budget constraints. Creating or editing the profile does not qualify an executor or start an agent. |

A skill may contain only instructions; do not require an artificial prompt
wrapper or duplicate its content. Prompt and instruction files cannot register
actions, approval rules, executable implementations, or privileged system
policy. Agent profiles cannot widen host limits, create grants, automatically
delegate, or select an unapproved destination.

Keep kind explicit in the common descriptor and apply kind-specific validation
and context roles. A prompt expands into clearly separated user-request
content with bounded explicit parameters; instructions are untrusted guidance,
not host policy. Skills supply the selected workflow; agent profiles supply
the qualified run configuration. File type or a claim of "system" authority
cannot promote text into trusted host instructions.
Missing template parameters require clarification, not expression evaluation,
file reads, secret substitution, or implicit network access.

### Bounded local agent owned-instruction references (v2)

The existing selected-source v1 agent operation remains unchanged, including its
`references: none` contract and independently permissioned observation tools.
For one original native, typed or current-name activated request, v2 adds
`references: disk.kora-profile.instruction.<exact-id>[,...]`: one to four distinct,
source-qualified existing catalogue IDs. All other required v1 metadata fields
remain required, with `kora-agent-format: 2`, local text input/output,
`tools: none` and `maximum-calls: 0`. Display names, paths, URLs, searches and
"latest" matches are not identifiers. Declaration never enables an instruction.

The host first reads the explicitly selected existing agent profile and reviews
the declared IDs and exact enabled owned candidates without new instruction
reads. A separate original-user **resolution consent** permits only those
bounded owned-store reads. Missing, disabled, held, stale, wrong-kind,
replacement or reload-unavailable sources fail closed without substitution.
The immutable bundle pins exact source bytes/revisions and control epochs.
Profile plus instruction source bytes may not exceed 64 KiB; the existing
complete serialized input and model budget limits can be stricter.

The next review shows the COMPLETE profile and every instruction source,
original request, exact Active target, locality and budgets. Separate single-use
original-user **invocation confirmation** runs one tool-free local model turn.
Each source is inert separate LocalOnly evidence, never system policy or
authority. The original request and genuine answer or truthful terminal are
retained through the existing conversation path with content-free correlated
audit. No persistent applicability, history or durable consent is created.

Changed input, source, session, ownership, privacy, expiry, disable/hold or
reload retires this bundle. This stricter bundle lifetime does not change
independent instruction one-shot captured-answer semantics elsewhere.
Cancellation and withdrawal suppress late output; reads and model work remain
owned until actual quiescence, even if they ignore cancellation. Local model
unavailability remains explicit, not fallback or qualification.

Composition is confined to `App.Agents.cs`; catalogue/store/revision and
conversation contracts are consumed unchanged. Skills, arbitrary file/resource
references, personal-profile instruction originals, recursive agents, scripts,
hooks, tools, delegation/concurrency/scheduling, hosted transfer, automatic
enablement and persistent policies are excluded. Caption and skill-reference
operations remain independent; this increment does not change their contracts
or the v1 contract in [Commands, Tools, and Skills](Commands_Tools_And_Skills.md).

### Bounded local agent owned instructions with observation tools (v4)

Explicit `kora-agent-format: 4` combines the v2 exact owned-instruction bundle with a nonempty declared subset of the six original read-only observation IDs in the same serial run.
It retains every required scalar metadata field, local text input/output and host-clamped budgets. V1 remains reference-free; v2/v3 remain tool-free. No shared skill references are accepted in v4 ([parser](../src/Kora.Core/Agents/BoundedAgentProfile.cs)).

One to four distinct `disk.kora-profile.instruction.<exact-id>` references and one to six distinct canonical tool IDs are required, with positive `maximum-calls`. Unknown/duplicate metadata, unknown kinds, paths, URLs, agent references/cycles, extra tools and unbounded budgets fail closed.
Tool descriptors/effects come only from the host catalogue; no profile description grants authority ([reference validation](../src/Kora.Core/Agents/AgentInstructionReference.cs), [constraints](../src/Kora.Core/Dependencies/ModelTurnConstraints.cs)).

The existing original native/typed/current-name voice review first shows declared IDs, exact enabled candidates, tool subset and cumulative budgets without reading instructions. Separate resolution consent pins complete source bytes, revision and control epochs.
Full guidance, original request and exact Active conversation review precedes separate single-use invocation confirmation. Editing, saving and independently enabling an instruction never supplies either consent ([workflow](../src/Kora.Application/Agents/BoundedAgentWorkflow.cs), [native review](../src/Kora/AgentRunController.cs)).

The canonical retained local model/tool loop receives separate immutable LocalOnly evidence and only the reviewed tool subset. Its consumer source fence revalidates the profile and each exact instruction at admission, each tool/continuation boundary and final publication, alongside host/session/generation/privacy authority.
Source edits or revocation reject further dispatch and late answers without substituting newer content. Stop retains ownership until source/provider/callback work actually joins ([source fence](../src/Kora.Application/Dependencies/ModelTurnHost.Execution.cs), [dispatch](../src/Kora.Application/Dependencies/ModelTurnHost.cs)).

Native owned authoring accepts v4 through the existing volatile source editor, exact create-only destination review, separate native save confirmation and durable readback. Saved originals remain inert and unselected; no original-replacement or automatic registration is added ([authoring](../src/Kora.Core/Agents/OwnedAgentProfileDocument.cs)).
Fake-backed end-to-end tests establish implementation, not provider E or RC Q; unavailable local models remain unavailable ([consumer tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.AgentInstructionsAndTools.cs), [native tests](../tests/Kora.Windows.IntegrationTests/AgentProfileEditorUiTests.cs)).

## Source and Format Matrix

The native owned Instruction/Prompt editor's [live source-buffer size feedback](../docs/instruction-editor.md#live-source-buffer-size)
measures only the exact displayed buffer through the source document's strict
UTF-8 encoder, without parsing or I/O. The existing `ArtifactFileDocument.Parse`
whole-source bound is `ArtifactDefinition.MaximumContentLength` bytes, including
the retained file BOM; the displayed buffer excludes that separately retained
BOM. UTF-16 and scalar counts have no invented whole-source limits. Malformed
Unicode has unavailable byte/scalar values, never replacement encoding.
This passive observation neither retires reviews nor changes drafts or
admission. Full Validate and exact save/use reviews remain independent.

| Source | Skills | Prompts | Instructions | Agent profiles | Mutation boundary |
|---|---|---|---|---|---|
| Bundled `Kora.Definitions` resources | Explicit embedded packages | Actual embedded templates when introduced | Actual embedded scoped guidance when introduced | Actual embedded profiles when qualified | Immutable; inspect and create a distinct declarative adaptation, never overwrite or extract writable executable content. |
| Kora-owned roaming data | `Skills` | `Prompts` | `Instructions` | `Agents` | Create/edit/rename/delete supported declarative definitions through the dedicated writer; save and activation are separate. |
| Explicitly registered personal profile files/roots | Versioned compatible `SKILL.md` packages | Versioned compatible prompt files | Versioned compatible instruction files | Versioned compatible agent/profile files | First-class editable originals through the dedicated declarative writer; normal exact-change confirmation, with an optional Kora-owned fork. |

Resolve Kora data directories through `IApplicationDataPaths` and Windows
Known Folder integration. The intended roots are `%APPDATA%\Kora\Skills`,
`Prompts`, `Instructions`, and `Agents`; these are data, never executable
search/load paths. The existing versioned skill package layout is unchanged.
Specify versioned layouts for the other kinds before shipping their writers;
do not create empty bundled folders or imply resources currently exist.
Device-local registrations, enablement, source bindings,
reconciliation state, and audit live under the verified local application
data boundary. No definition metadata roams authority or credentials.

Profile means the user's personal customization locations, not permission to
scan the whole profile, workspace, repository, or another user's files.
Recognised `.copilot`, `.agents`, `.claude`, VS Code, and VS Code Insiders
locations may be offered as candidates, not automatically trusted or read.
Register exact files or bounded roots by deliberate voice/UI selection and
disclosure of source, kinds, supported reader versions, limits, and local read
scope. A source registered for skill inspection does not authorise reading
its prompt, instruction, or agent files. Additional kinds/scope require a
separate reviewed registration change.
Workspace/repository roots need their own explicit future admission; they are
not implicitly included by personal-profile registration.

Compatibility is reader-version-specific, not inferred from a folder name or
extension. The future format matrix must cover native Kora definitions,
compatible `SKILL.md`, `.prompt.md`, `.instructions.md`, and `.agent.md`
files, and explicitly qualified tool-specific instruction/profile formats.
Do not claim every Copilot/Claude/agent format is compatible. Required
unsupported metadata, scope/glob semantics, tool/runtime references, executable
hooks, or malformed text remain visible as incompatible; never discard them
to manufacture a usable definition.
Before delivery, each reader/writer specifies concrete byte/file/depth/header
and expansion limits, strict UTF-8/schema handling, deterministic exact-byte
digests, fixtures, and round-trip guarantees. R21's existing limits and narrow
reader are unchanged, not silently broadened for other kinds.

Identity includes kind, source ID, canonical file/package identity, reader
version, and complete admitted definition digest. Display/declared names are
not authority. Preserve original bytes, encoding/BOM, line endings, metadata,
and attribution for inspection and source-preserving edits.
Show same-name definitions as source-qualified choices; never shadow another
source, reserved command, or bundled action. A user binding is exact,
device-local, and revision-aware, not a "profile wins" search order.
Referenced definitions resolve by admitted source-qualified identity.
Reject unresolved required references and cycles before activation.

## One Management Surface, Equal UI and Voice Control

### Customizations window and source categories

The product owner's 2026-10-24 direction is one **Customizations** window for
Instructions, Skills, Agents and MCP Servers, with the existing Prompts editor
retained. This advances the admitted governed-definition outcome, not a general
file editor or permission to execute newly authored content. Discovery, source
editing, complete proposal review and runtime exclusion are host responsibilities;
domain-specific generated behavior remains definition content.

Use artifact-type navigation at the left in the order **Instructions**, **Skills**,
**Agents**, **MCP Servers**, then **Prompts**, with Instructions initially selected.
An explicit artifact-type command selects its requested category.
Use a searchable catalogue in the middle,
and a selected-artifact viewer/editor at the right. Group each type into
**Built-in**, **Kora** and **User**, in that order, including empty groups.
Kora means Kora-owned roaming profile definitions; User means explicitly
identified supported Copilot/VS Code personal-profile locations, not the
current repository or an arbitrary workspace. Display the actual source/profile
and availability reason. These labels do not establish precedence: duplicate
names require source-qualified selection, never automatic shadowing.

Built-in content is immutable. Inspection can show its complete packaged file
set, but editing requires **Create Kora copy** with a distinct destination,
attribution and initially disabled state. Neither copying nor saving transfers
bundled action authority.

Selecting an editable source opens a private working draft. A skill exposes its
primary `SKILL.md` and bounded component tree; instructions and agents start
with their primary document. MCP editing targets one server definition, not
permission to replace unrelated entries in a shared configuration. Add, rename
and remove component controls operate on the draft only; primary documents
cannot accidentally be removed. Unsupported/binary components remain explicitly
unavailable, not silently omitted from the package or a save.

**Add** selects kind and writable destination, then either starts with a
template or a conversational description. **Ask Kora** accepts typed or activated
voice refinements against the same draft; model output is a proposal, never
approval, an authoritative file path, a source registration or an action grant.
Before any model disclosure show the exact supplied source/request, destination
and locality; ordinary provider policy remains authoritative. Native text editing
and local validation remain available without a model. Editing a source or request
retires an outstanding generation/review; late model output cannot replace a
newer draft.

Review covers every added, modified and removed file, its complete proposed
source, the selected destination and current base. Save, Discard and Cancel are
distinct, including on selection changes and dirty close. Changed source bytes
are saved disabled; activation requires a separate current revision review.
Errors and external conflicts retain the draft and offer explicit recovery.
Do not claim unsupported reader/runtime compatibility after a successful write.

The same operations are addressed by exact original typed input or current-name
activated voice: "Show skills", "Show me XYZ instruction", "Edit XYZ skill" and
"Add a skill". Ambiguous names open the filtered source-qualified choices.
Commands route to the same native workflow; source content is not recursively
interpreted as a command. A command opening an editor is not completion of an
edit or an approval.

### Bounded Kora-owned HTTPS MCP entry editing

The [existing management window](../src/Kora/CustomizationWindowController.Mcp.cs) owns the complete setup operation:
explicitly Add one named Kora entry or select its source-qualified ID/name, consent to the private selected-source read,
edit native URL/optional HTTP-type fields, validate, review the complete original/proposed entry and replacement diff,
separately confirm Save, then verify exact atomic-file readback and disabled catalogue status.
Original typed and current-name activated commands enter that same native workflow; they never approve Save.

The writable destination is exclusively host-selected `IApplicationDataPaths.RoamingRoot\Mcp\mcp.json`.
New files use `{"servers":{}}`; existing `servers` or `mcpServers` JSON/JSONC is writable only when that collection is the sole root property.
The selected entry accepts only `url` and optional `"type":"http"` under the [first HTTPS endpoint validation](../src/Kora.Core/Customizations/McpDefinitionDocument.cs).
No stdio, commands, arguments, environment, headers, credentials, account references or authentication editing is admitted.
Unknown root fields and unsupported/sensitive selected-entry fields remain undisclosed and cannot be dropped or downgraded.

The [scoped native publication path](../src/Kora.Windows/ArtifactDiscovery/WindowsCustomizationStore.Mcp.cs) reuses the existing source writer.
It preserves unrelated bytes, root collection spelling, comments, BOM and compatible source/security metadata without reserializing the whole file.
Missing managed directories/files require explicit creation consent; parent binding, exact case, ownership, reparse/hard-link/stream exclusions,
source stability, complete identity/base bytes and security are revalidated.
Final native comparison is optimistic, not compare-and-swap: an external same-name replacement can still race the atomic rename.
Exact complete-file identity/bytes must pass readback before success.

Before publication, the [audited management service](../src/Kora.Application/Definitions/CustomizationManagementService.cs)
disables the source identity, retires previous use authority and independently removes MCP inclusion.
Save does not register/reinclude customization roots or alter other source paths. Cancellation before final consent writes nothing.
Unconfirmed publication remains excluded, retains the draft and requires explicit inspection and a fresh review;
it never promises rollback or automatically replays a transaction. User/profile/additional files remain metadata-only here.
Authentication/account/connector qualification and separate reviewed inclusion/discovery/use remain unavailable or governed by their existing routes, not enabled by setup.

Raw endpoints stay inside explicit native own-source fields/reviews, never model authoring, metadata tools, diagnostics or audit.
The [complete native regressions](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/CustomizationWindowTests.Mcp.cs)
and [fixture writer regressions](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/WindowsCustomizationStoreTests.Mcp.cs)
use isolated fixture roots, not workstation user configuration.

### Foreground editor voice targeting

**Status: bounded implementation with exact native/spoken clarification and native disclosure/proposal review.** The product owner selected
foreground editor plus model-interpreted editing intent, with clarification
when ambiguous. The delivered route requires neither an "in this editor"
preface nor a separate editor capture mode. This advances the approved
voice interaction and governed-definition outcomes: while editing an
instruction, "make this more concise" can refine that draft without entering
another Active session or starting a conversation. Target admission belongs to
the host because it crosses window, capture, privacy and session boundaries;
definition content and models cannot assign that authority.
The current target-choice surface offers refinement/discussion of the exact
captured artifact or Cancel. Another artifact/session/New request requires
cancellation and its explicit admitted route; there is no automatic transfer
of the held utterance. **Answer by voice** opens one bounded local successor
capture for this editor question only. General model-selected session
relatedness remains unavailable.

An open background editor is not a target. At admitted wake/PTT activation,
before asynchronous capture or UI dispatch, the host snapshots the eligible
foreground editor instance, selected source, complete draft revision, selected
component and original voice channel. Bind the capture generation and current
ownership/privacy/call admission to that snapshot. Show a visible, accessible
label such as **Voice context: Instructions / Kora / Writing style** in the
editor and capture feedback. This is one utterance's routing context, not
permission to read source bytes, run inference, edit or keep listening. Do not
recompute its target from whichever window is foreground when recognition ends.
Internal identities and revisions stay out of normal labels.

The native editor provides **Hold to speak**, including pointer hold and
Space/Enter hold while the button is focused. It shares the response window's
push-to-talk gesture and host capture lifecycle, but owns only its own gesture:
another window closing or losing focus cannot release this capture. The held
button remains enabled to receive release while capture makes new activation
unavailable. Blur, deactivation, hiding and closing release its capture;
presentation loss also retires the original target. No focus exception or
ambient wake enablement is required to reach the editor route.

#### Input ownership and route selection

| Original input context | Admission and routing |
|---|---|
| Dedicated question reply, approval response or literal dictation capture | Its existing exact owner consumes the utterance exclusively. The editor cannot intercept it; text does not become a general command if that owner expires or rejects it. Existing cancellation and safety controls retain their own contracts. |
| Explicit editor, session, New request or reserved local command, outside dedicated capture | Resolve the explicit target through its existing admitted route, before inferred foreground routing. Opening or naming another artifact retains source-qualified selection and dirty-draft decisions. A conflicting or mixed editor/session request requires clarification, not partial execution. |
| Foreground editor snapshot and editing/discussion intent | A bounded local, tool-free interpretation proposes **edit this draft**, **discuss this draft**, or **clarify**. The host admits only the original editor snapshot, never a model-selected path or Active session. |
| Ambiguous editor reference, several possible targets, or an inferred different destination while editor context is present | Hold the utterance privately and ask a target-choice question. Do not send it to a session or create one while waiting. |
| No editor context, addressing or possible editor reference | Keep the ordinary admitted command/session route. An editor merely remaining open in the background does not change that route. |

Routing interpretation receives only the original text and the minimum
admitted editor metadata needed to distinguish these intents: kind, displayed
source/name, selected component and read-only/availability state. It receives
no definition bodies, other sessions' messages, hidden drafts, tool results or
capability catalogue. Even this local inference requires ordinary model
admission and an explicit review of the exact text/metadata disclosure; capture
or focus alone is not disclosure consent. Preserve the recognized text and
voice origin; interpretation is not an LLM transcript-cleanup stage.

The model returns a bounded typed proposal against host-issued candidate
references. The host checks the original target, lifecycle and revisions before
acting. Model confidence is not authority; malformed, unresolved or unavailable
interpretation has an explicit outcome and a native recovery path. No generic
session-routing retry follows a rejected editor proposal. Local inference
failure never selects a hosted model.

Clarification offers only actual eligible, source-qualified targets, with
**Cancel**. The delivered choices are the captured artifact's refinement or
discussion; separate explicit routes select another destination. A later
cross-target chooser may include **New request** or sessions only when relevant.
For example: **Did you mean this instruction, or a session request?** An admitted spoken answer belongs to this exact question and original
utterance, not a fresh command. Choice expiry, cancellation or invalidation discards the pending
handoff. Selecting a different destination is a new explicit routing decision,
not replay through the global parser. It preserves the original voice channel
and uses that destination's ordinary review/admission; choosing an editor does
not itself approve disclosure or an edit. No choice may silently substitute a
different source after the original becomes stale.

The delivered editor question is a volatile, single-consumption continuation of
the original foreground request, not a persisted session question. It binds the
exact editor target, source/baseline, draft/component, original request/channel
and host/privacy/call admission. Native buttons and the separately selected
**Answer by voice** share the same typed Refine/Discuss/Cancel outcome and
75-second question expiry; neither extends the original operation's deadline.
Only that question owner can hand over its original capture generation to its
acknowledged successor and confirmed release. Global voice-generation fences
and persisted-question admission are unchanged. The reply is parsed against
this question's whole-utterance schema, never sent to a command/session parser.
Invalid, duplicate, foreign or late replies cannot resume another operation.
Withdrawal, expiry, close and disposal retain ownership until opening and
capture release join. No held request/reply/question is saved or restored.
Source freshness is rechecked before resumption, local authoring and draft
adoption; deletion or revision changes require explicit reload/new input.
Spoken choice is not disclosure, proposal adoption, Save or enablement consent.

#### Complete editing interaction

1. The user activates voice with one eligible Customizations editor foreground
   and says, for example, "make the error-handling section clearer". Kora shows
   the captured artifact context and the exact recognized request.
2. Route interpretation or a target-choice answer resolves the original editor.
   The editor's request field receives the request through a revision-checked
   handoff, not literal insertion into the component text. The user can correct
   it using native text input. "Type this text" requires a separately qualified
   literal-dictation route; it cannot silently become model refinement.
3. **Review model authoring disclosure** shows the exact request, complete
   supplied draft files and local destination. This is separate from the
   metadata-only routing review. Reuse the existing one-turn, tool-free local
   authoring service; it can explain or return a complete bounded file-set
   proposal, including component additions, edits, renames and removals.
   Selected-component context is explicit, but does not hide the rest of the
   supplied package or grant access to other files. Carry the captured component
   and resolved edit/discussion mode into that authoring turn as separately
   reviewed context, without rewriting the recognized request. Discussion mode
   requires unchanged files even when the original utterance uses edit wording.
4. **Review proposed draft** shows every changed component and complete
   before/after content. **Use proposed draft** changes only the working draft.
   Discussion leaves files unchanged; neither output nor spoken assent is
   automatically a save or approval.
5. **Review and save** applies the existing exact-source transaction and
   conflict/recovery contract. Changed content is saved disabled. **Allow Kora
   use** remains a separate current-revision decision, not part of generation.

Subsequent "also remove the old example" turns bind anew to the current draft.
Do not search other conversations or infer a hidden previous proposal to
resolve "it" or "that version"; clarify if the currently displayed context is
insufficient. Built-ins remain read-only and offer a separately chosen Kora
copy, never automatic copying or mutation. Disabled drafts retain the existing
explicit authoring-only disclosure exception and stay excluded from runtime
context/options/capabilities. MCP source edits never connect, authenticate,
start a server or invoke its tools.

#### Retirement, privacy and implementation evidence

Focus leaving the editor's admitted native presentation, source/draft/component
or request edits, selection changes, closure, privacy/ownership/call revocation
and capture replacement retire the original routing snapshot. Returning focus
or selecting the same source again does not revive it. Host-opened disclosure,
target-choice and proposal windows require an explicit ownership handoff;
their expected activation is not permission to rebind to another window.
Intentional request/draft updates advance the bound revision through that same
checked handoff rather than invalidating themselves.

After editor intent is selected, stale/rejected/unavailable/cancelled work is
terminal for that utterance: it must never answer another pending question,
append to another session, create a New request, execute a command or retry a
different model. Late or duplicate recognition/model/choice callbacks cannot
apply text or revive a retired target. Keep owned capture/inference resources
until actual quiescence. Ordinary cancellation preserves independent native
draft edits; privacy retirement clears private controls, request/choice/review
bodies and pending model output under the existing editor privacy contract.
Audio, transcripts, definition text and model output remain excluded from
diagnostics/audit. This adds no saved conversation or transcript history.

The implementation boundary requires deterministic regressions for activation-time binding
versus final-result focus; background and competing sessions; exclusive
question/dictation capture; explicit addressing and ambiguous/mixed intent;
source-qualified duplicate names; draft/component/source/focus ABA changes;
owned modal handoffs; built-in and disabled sources; model/recognition failure;
privacy/call revocation; late and duplicate callbacks; and cancellation with
delayed resource release. Assert forbidden effects as well as outcomes:
unresolved or stale editor speech produces zero conversation submissions,
conversation-session creation, component mutation, source writes, enablement or tool calls.
Accepted refinement changes the draft only after proposal acceptance; save and
use control retain separate reviews. Fakes qualify these boundaries, not real
acoustic/provider readiness.

The [speech admission contract](Speech_Input.md#editor-targeted-text-admission)
and [session routing exception](Interaction_And_Sessions.md#editor-targeting-precedence)
must preserve this exclusive ownership. Native text editing and the delivered
exact customization commands remain available when the selected speech/model
profile is unavailable. [Application routing regressions](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.Customizations.cs)
and [native review regressions](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/CustomizationWindowTests.cs)
exercise the bounded implementation without qualifying real audio or models.

### Persisted exclusion from Kora use

Every artifact, including an immutable built-in and an MCP server, has a
persisted Kora use-disable state. This is Kora's device-local control state,
separate from roaming content and from VS Code/Copilot's own settings. Disabling
does not mutate the original definition, stop unrelated tools, unregister a
source or delete content. Disabled entries remain inspectable and editable where
source access permits.

Disabled artifacts must be excluded from new model context, instruction
applicability, completion/selection options, advertised capabilities and new
invocation admission. Enforce this at the common catalogue and at independent
selected-source agent/skill/MCP admission seams, not only in the window.
The product owner separately approved an explicit authoring-only exception:
an original-user **Ask Kora to edit this** request may disclose a disabled draft
only after a complete source/request/locality review. This is inert authoring
input, never capability activation; runtime use stays disabled.
Revalidate before model disclosure or tool execution; pending reviews cannot
re-enable a disabled artifact. Enabling removes only Kora's exclusion: it does
not approve changed bytes, resolve missing dependencies, grant tools, authenticate
an MCP account, start a process or qualify a runtime. Missing/corrupt/unconfirmed
control state fails closed and is reported.

### Reviewed transactions for User package originals

The product owner explicitly selected recoverable multi-file writes to User
originals on 2026-10-24 rather than a mandatory Kora-copy-only restriction.
Single-file saves retain the existing optimistic conflict contract. A package
transaction must retain its exact complete base, stage and validate every
proposed component, and compare original identity/membership/bytes immediately
before publication. Its exact-change review warns that external consumers such
as VS Code may observe intermediate files; never present sequential publication
as an atomic package replacement.

Interrupted or uncertain transactions remain recovery-required and unavailable
to Kora until explicitly reconciled. Recovery itself is reviewed, bounded to the
original source and complete file set, and cannot overwrite a subsequent external
edit without another conflict decision. Backup/staging content is definition
data, private and transient, never diagnostics/audit content. Clean temporary
data after confirmed publication/recovery; never silently roll back, claim no
effect after cancellation, or release ownership while publication is active.
This decision does not relax reparse/hardlink/parent-owner checks, private
presentation, source consent, audit or per-file protected-target restrictions.

Provide a native **Definitions** surface reachable from the tray and workspace,
with kind/source/state filters. The existing Skills view can remain a focused
entry into it. List discovered disabled, incompatible, stale, missing, and
unavailable entries as well as usable ones, with truthful reasons.
Show identity, exact source, revision, compatibility, activation/applicability,
dependencies, requested tools/runtime/destinations, validation, and freshness.
The host-owned file tree and passive exact-source/diff views follow the
[workspace review contract](UI_Workspace_And_Windows.md#skills-and-permission-management).

UI controls, typed commands, and activated voice use the same registered
management actions, host-resolved targets, drafts, validators, and outcomes.
Models may propose edits, never supply an authority-bearing path or approval.
Basic list/inspect/disable/refresh/discard operations do not require a model or
network. Dictation and conversational refinement can use only a qualified
adapter under ordinary context/egress policy; native text editing remains
available when inference or speech is unavailable.

| Operation | Native UI | Equivalent activated voice |
|---|---|---|
| Find and inspect | Kind/source filters, search, exact source/details | "Show my prompts"; "Show instructions from my Copilot profile"; "Explain this agent's tools" |
| Register, narrow, or remove source consent | Source review with exact file/root and allowed kinds; removal preview | "Use prompts from my VS Code profile"; "Stop reading this source" |
| Create and select destination | Kind-specific editor; Kora store or explicitly selected profile scope | "Create a read-only review agent"; "Save this prompt in Kora" |
| Edit or fork | Explicit Edit original/Create Kora copy choices; field/source editor and exact diff | "Edit the original profile prompt"; "Make a Kora copy of this skill" |
| Refine and validate | Shared draft, errors, dependency checks, data-only simulation/template preview | "Change the output to bullet points"; "Read back the changed section"; "Validate this definition" |
| Save, rename, restore, delete | Exact mutation preview and separate confirmation | "Save these changes"; "Rename this instruction"; "Restore the previous revision"; "Delete this profile prompt" |
| Activate or stop applying | Separate revision review, enable/disable and instruction-scope controls | "Enable this skill"; "Apply this instruction to this session"; "Disable this agent profile" |
| Use | Selected prompt/skill action or qualified agent run control | "Use weekly summary to summarise this"; "Run the review agent on this task" |
| Refresh and reconcile | Refresh source/all, freshness status, external diff/conflict choices | "Reload my definitions"; "Show external changes"; "Keep my draft"; "Reload the external version" |

Voice edits can address named fields, headings, or an exact selected bounded
section; ambiguous source, text range, name, scope, or destination requires
clarification. Dictated text is draft content, never a command within that
content. Readbacks summarise the same proposal/diff as the UI, with full details
available on request. Voice confirmation binds the exact visible proposal,
operation, source, file set, base/new digests, and current session generation.
Concurrent voice/UI edits invalidate stale confirmation; commit once.
Source selection may use a reviewed spoken path or known candidate, with
native picker fallback for uncertainty, not a guessed path.

All supported operations are available by mouse/keyboard, voice, or mixed
input under the same session/privacy/approval gates; an unavailable speech
channel retains UI recovery rather than lowering those gates.
Disabled/incompatible definitions remain inspectable/editable when source
access permits, but cannot be applied or invoked. Agent authoring/validation
may be available before agent execution; expose Run as unavailable with the
unqualified-runtime reason, never emulate execution with a prompt.

## Delivered native skill metadata navigation

Exact original typed input and current-name activated voice can open the existing bundled or registered shared-source singleton through `open skill packages` / `show skill package review` and `open shared skill sources` / `show shared skill sources`. No package/name/path selector or fallback surface is admitted ([whole-utterance grammar](../src/Kora.Application/Skills/SkillReviewCommand.cs)).

The consumer-focused [Application navigation seam](../src/Kora.Application/Skills/ISkillReviewNavigation.cs) binds only to the [existing desktop controllers](../src/Kora/SkillPackagesWindowController.cs).

The [queued route](../src/Kora.Application/ViewModels/MainViewModel.SkillReview.cs) retains original origin and rechecks captured owner, binding, privacy, call, name and activated-voice lifetime; questions/security previews remain intact.

Unknown call observations and unconfirmed manual-call evidence deny both native and command review opening. A deliberate unavailable automatic detector remains distinct from unknown state; this increment does not qualify detection or reopen capture ([shared review eligibility](../src/Kora.Application/ViewModels/MainViewModel.SkillReview.cs)).

Opening shared sources retains only registration-metadata refresh, whose failures remain separate native recovery. Registration, folder selection, discovery, recheck and withdrawal remain explicit native gestures ([shared controller](../src/Kora/SharedSkillSourcesWindowController.cs)).

Embedded review still requires verified resource bytes/digests. Neither surface adds enablement, invocation, source consent, catalogue reload or model context ([bundled controller](../src/Kora/SkillPackagesWindowController.cs)).

Voice keeps `ActivatedVoice` origin. Local-input-only admission refuses automatic metadata refresh; the open window reports native recovery, not invented `LocalUi` authority. Native Refresh remains explicit ([admission](../src/Kora.Application/Skills/SharedSkillAdmission.cs), [tests](../tests/Kora.Windows.IntegrationTests/SkillReviewNavigationTests.cs)).

This is an I-only review-navigation increment, not the full Definitions management surface or E/Q qualification. Executors, agent profiles and unsupported definition kinds remain gated; no physical/acoustic/provider/installed proof or broader source authority is claimed ([delivery policy](Implementation_Roadmap.md#delivery-and-qualification-dependency-policy)).

The separate [bundled](../src/Kora/SkillPackagesWindow.axaml.cs) and [registered shared-source](../src/Kora/SharedSkillSourcesWindow.axaml.cs) inspectors now provide explicit **Search/Clear** over already admitted immutable snapshots, not the broader Definitions management surface or voice/tool search grammar. Initial open/list still selects the first admitted package; subsequent search/filter/Clear never opens a different result.

[Snapshot search](../src/Kora.Application/Skills/SkillCatalogueSearch.cs) uses literal `OrdinalIgnoreCase` substring matching without trimming, wildcard syntax or Unicode normalization. Bundled fields are declared name, native manifest ID and description; shared fields are validated declared name, source-qualified metadata identity when that name exists, and a valid declared description. Missing metadata is not invented; path-fallback identities and roots/files/bodies are not query fields.

[Query validation](../src/Kora.Core/Context/LiteralMetadataQuery.cs) shares the owned-definition policy: at most 256 UTF-16 units and 512 strict UTF-8 bytes, valid Unicode without control/format/line/paragraph separators. Invalid input is rejected explicitly with empty results/selection, never truncated or interpreted as All.
Drafts apply only through Search; edits immediately retire results/selection/details without reads ([native controls](../src/Kora/SharedSkillSourcesWindow.axaml.cs)).

Native bundled/shared digest-query widgets were removed in [#422](https://github.com/roryprimrose/Kora/pull/422). Literal names/metadata and meaningful compatibility/availability choices remain. Source digests and full catalogue/resource validation remain internal admission rules, not ordinary search fields.

Shared **All / Instruction-compatible / Instruction-incompatible** classifies structural instruction-only compatibility; every invocation remains unavailable. Bundled **All / Runtime unavailable / Runtime available** uses existing invocation availability, not an invented instruction-compatibility claim; runtime admission remains outstanding. All includes unavailable/incompatible observations ([filter policy](../src/Kora.Application/Skills/SkillCatalogueSearch.cs)).

Filtering keeps exact original objects and only an unchanged exact surviving selection. Query/filter edits clear results and private identity/text/hex/source tabs; Search never selects a different result ([bundled window](../src/Kora/SkillPackagesWindow.axaml.cs), [shared window](../src/Kora/SharedSkillSourcesWindow.axaml.cs)).
Clear restores empty/All without selection, even if filters were already empty; Escape closes and clears private drafts. Nothing is persisted ([native tests](../tests/Kora.Windows.IntegrationTests/SkillMetadataSearchWindowTests.cs)).

Source selection/reload/failure/withdrawal, privacy/ownership closure, call-policy change and close retire private navigation. Catalogue generations fence late discovery and recheck publication even after same-snapshot reselection; cancellation remains host-owned ([controller](../src/Kora/SharedSkillSourcesWindowController.cs)).
No reader call, Discover/Recheck/withdrawal, source switch, draft replacement, registration extension or new execution authority occurs on edits/Search/Clear ([native tests](../tests/Kora.Windows.IntegrationTests/SkillMetadataSearchWindowTests.cs), [read-free service tests](../tests/Kora.Application.UnitTests/Skills/SharedSkillDiscoveryServiceTests.cs)).

## Editing, Ownership, and Confirmation

The bounded personal Instruction-original implementation reuses Customizations and its native file owner. One exact existing personal Code/Code - Insiders native v1 instruction receives a window-lifetime registration review before content reads, then separate complete mutation confirmation. Previous read-only registrations grant no editing scope; unsupported formats retain explicit refusal and the compatible owned-fork alternative ([workflow](../src/Kora/CustomizationWindowController.InstructionOriginal.cs), [native owner](../src/Kora.Windows/ArtifactDiscovery/WindowsCustomizationStore.InstructionOriginal.cs)).

The native owner pins/revalidates the registered ancestry and exact original, retains stable write-denying sharing through staging/final comparison, preserves owner/group/DACL and ordinary attributes, and checks exact published identity/bytes. Named streams/unsupported native attributes are refused ([publication](../src/Kora.Windows/ArtifactDiscovery/WindowsCustomizationStore.InstructionOriginal.cs), [native access](../src/Kora.Windows/ArtifactDiscovery/WindowsOwnedDefinitionFileAccess.InstructionOriginal.cs)).

Source replacement requires renewed registration; comparison/rebase never writes or merges the draft. Uncertain publication revokes registration and remains disabled pending explicit inspection. This is single-file optimistic publication, not content compare-and-swap or broader writer/enablement qualification ([recovery](../src/Kora.Windows/ArtifactDiscovery/WindowsCustomizationStore.InstructionOriginal.cs)).

In the target design, Kora is an editor of supported personal profile definitions
as well as Kora-owned definitions. Registered originals are directly editable;
there is no separate read-only-by-default tier or edit-enable switch.
Registration discloses bounded reading and supported editing scope but does not
approve any write. Each create/save/rename/restore/delete needs an exact
user-confirmed mutation proposal. The delivered R21 read-only registration is
not retroactively expanded; moving it to editor scope requires explicit review
of that changed source contract.
Disclose that the original is shared with other applications and external
sync; offer a Kora-owned fork instead. Revocation cancels pending profile writes
and invalidates approvals without deleting originals or Kora copies.

Only the qualified dedicated definition writer may mutate those originals.
Generic filesystem tools, scripts, MCP routes, and agent executors still cannot
write registered shared sources. No executable scripts, hooks, binaries,
application source, policy, credential files, or protected resources become
editable. If a format cannot round-trip supported content without dropping or
changing unrelated metadata, disable in-place editing and explain the fork/
external-editor option. An unsupported original is not "repaired" implicitly.

The host derives targets from registered identity and validated relative
names, never arbitrary model paths. Revalidate registration, session, complete base
file-set identity/digests, source directory binding, and effective target at
commit using race-resistant filesystem operations.
Reject traversal, reparse/hard-link/alias escapes, replaced directories,
protected roots, unexpected package members, and conflicting external writes.
Retain the existing local-only/network-backed storage restrictions.

For the bounded owned single-file instruction/prompt writer and selected compatible profile-original instruction writer, the user has explicitly
chosen **optimistic content conflict detection**, not content compare-and-swap.
Immediately before atomic replacement, compare the live original identity and
exact digest with the reviewed base. A detected changed, missing or replaced
base invalidates ordinary Save and offers exactly **Reload from disk (abandon
draft)**, **Overwrite with Kora changes**, or **Cancel (keep draft)**.
Overwrite requires another exact native mutation review of the observed conflict
and proposed Kora bytes; another change before the final comparison prompts again.
An unseen content edit in the narrow final comparison-to-replacement window may
be overwritten (whole-file last-writer-wins). This accepted content race does
not relax source/root ownership, privacy, consent, audit, parent identity,
reparse/hardlink/alias or protected-path checks. Publication is relative to a
pinned native directory handle, never a followed caller/model destination.

Stage and validate a complete immutable proposal; publish atomically where
verified. A multi-file package needs complete revision publication/recovery,
not sequential overwrites presented as an atomic save. If the source cannot
support safe publication, refuse in-place mutation and offer a Kora-owned fork.
Temporary/backup destinations obey the same scope and privacy boundaries.
No silent rollback, partial-success receipt, or automatic detected-conflict
overwrite; only the explicitly accepted optimistic content window above may
overwrite an unseen concurrent edit.
uncertain publication closes mutation/activation until explicit reconciliation.
Record requested and terminal typed audit outcomes for writes, consent, and
activation changes; surface cancellation/failure and any observed durable effect.

Saving creates a validated revision but never activates changed content.
Unchanged enabled bytes keep their current status; changed saved bytes suspend
new use until separate revision-bound activation.
Delete distinguishes deleting originals, deleting owned copies, disabling use,
and unregistering a source. Source removal never deletes originals.
Rename changes file identity/bindings as applicable and invalidates affected
approvals/references; do not silently retarget by display name.
Restoration is a new reviewed write, not restoration of revoked grants.
A fork has distinct identity and attribution, no inherited enablement, grants,
bundled trust, or privileged routing; later source changes never merge into it
automatically. Closing a dirty editor requires Save/Discard/Cancel.

## Consistent External Change and Reload Contract

### Windows-managed roaming and local revision admission

Windows/profile management owns propagation of Kora roaming definitions and
supported User originals included by that environment's policy. Kora owns
safe discovery, exact local review, source revalidation and editor reconciliation;
it implements no sync transport, remote lock, account enrolment or distributed
assistant ownership. `.copilot` / `.agents` user-profile roots are not assumed
to have AppData roaming coverage. Existing native fixed-local-volume restrictions
remain; network-redirection support is not implied by the Roaming known folder.

The approved scenario is a user opening the same definition on two computers:
an incoming file is a candidate, never transferred enablement or execution
authority. This is host-owned reliability within governed definition use,
not an extension or a new cross-device conversation/memory capability.

`artifact-use.json` remains in local Preferences. Version 2 preserves explicit
exclusions and adds bounded source-qualified primary-digest reviews; version 1
reads preserve exclusions without manufacturing reviews. Existing
entries without a previous digest review require a one-time local review after
upgrade; no automatic migration marks discovered content approved. General file-backed
catalogue entries require their current primary digest to have been separately
reviewed through the existing native/exact typed/activated use-control route.
The complete current supported source is displayed and re-read before the
audited local allow. A primary review is not script/package execution authority.
Owned instructions/prompts retain their independent native revision gate;
first discovery no longer automatically enables an unmarked legacy file.
Previously saved exact local revision state remains valid only under its
existing checks. No change to source content or authority is inferred from a
filename, Windows account or profile-copy event.

Startup and reload reconcile the bounded local review inventory. Observed
changes/deletions retire matching general primary reviews durably; restoration
does not revive them. Reload failure withholds every file-backed catalogue entry,
including captured-use flags, while bundled resources remain independent.
Uncertain local preference publication fails closed. Recovery requires successful
reload and fresh reviews where retired, not last-good source fallback.
Direct file-backed slash/activated invocation re-reads the bounded registered
catalogue before model admission, even without a watcher notification. Existing
exact-source skill/agent/instruction workflows keep their independent reads,
reviews, package validation and revocation gates.

Multi-file sources must pass their existing complete-set validation; filesystem
atomic publication on one computer does not make Windows propagation atomic.
No new format, package runner, companion-file authority or automatic merge is
introduced. Unsupported/incomplete formats remain unavailable.

Editor base/draft conflict handling below remains authoritative. Local optimistic
checks cannot detect a competing revision which Windows overwrote before this
host observed it. Kora does not promise lossless offline concurrent editing,
remote conflict discovery, automatic recovery/version history or sync status.
Users should avoid simultaneous edits of the same definition. Windows-managed
sync is independent of model LocalOnly mode.

Conversations, submitted history, attachments, Device-profile memories, grants,
consent, pending work, diagnostics, recovery journals and runtime/device settings
stay local in this increment. Portable personal preferences, shared history,
account-wide memory and task handoff require separate product decisions.
The simple user-facing explanation is [Data storage](../docs/data-storage.md).

### Common reconciliation rules

Apply one reconciliation pipeline to Kora-owned and registered profile files
of every kind, whether the trigger is Save, manual Reload, a bounded watcher,
scheduled discovery, startup, or pre-use validation. The existing bounded
watcher/reload owner is reused, not a second synchronization pipeline; watcher
notifications are advisory and do not establish remote convergence.
Embedded definitions change only with a verified application replacement.

1. Revalidate registration/read consent, privacy/session admission, source
   directory identity, complete bounded membership, and exact bytes. Watchers
   are advisory; coalesce notifications and recover overflow with a bounded
   re-enumeration, never a whole-profile scan.
2. Parse/validate off the immutable candidate snapshot. Do not expose partially
   read packages or a partially published source catalogue. Superseded refreshes
   cannot publish late; source withdrawal/privacy closure cancels them.
3. Compare complete identities/digests and reconcile the source catalogue,
   UI, voice lookup, slash completion, instruction applicability, profile
   references, and admitted model descriptors as one revision.
   No-change refresh preserves activation; successful metadata refresh is not
   activation of changed content.
4. New definitions enter disabled. Changed definitions become review-required
   and block new use; invalid, deleted, inaccessible, replaced, or revoked
   sources become incompatible/unavailable with explicit recovery.
   Failed refresh never presents the last snapshot as current or usable.
   Retained old review content is labelled historical and remains subject to
   access/privacy policy; clear it on access withdrawal.
5. Invalidate approvals/drafts based on changed bases and affected dependent
   definitions, instruction bindings, agent profiles, and executable grants.
   Never silently update a reference to the latest/name-matching version.
   Restoring old bytes does not resurrect revoked authorization.
6. Revalidate exact source/reference freshness immediately before admitting
   new use, even without watcher events. Pin the full applicable definition
   set for the task/clarification; queued work retains exact revisions and
   requires review or re-admission if stale, never an automatic version switch.

Active tasks retain their admitted immutable definition set; reload cannot
inject new guidance mid-task. Access/consent revocation, required resource
loss, or grant invalidation still applies at each action/egress boundary and
can block/cancel continuation. Snapshot pinning is not permission to ignore
revocation or make a now-denied new source read.

A clean inspection/editor may update to a newly validated candidate with a
visible revision/change notice, without enabling it.
A dirty editor retains its base and draft and reports a conflict; offer
compare, explicit rebase with validation/review, discard/reload, or save as a
new copy. Never overwrite the draft, auto-merge, or save over external edits.
The same conflict and resolution are addressable by voice.
Passive refresh does not steal focus, speak unsolicited private file contents,
or count as user activity/approval.

## Delivery and Verification

### Bounded owned instruction and prompt editor

Native tray and exact `instructions` or `prompts` followed by
`editor/list/create/edit/inspect/validate/compare/rebase/save/copy/reload/delete/activate/disable/stop-use/cancel` control routes share the same admitted actions, native review
and writer. Activated controls focus the same native editor. One already completely
display-reviewed existing canonical owned Instruction normal same-source Save now
supports a separate explicit [exact spoken answer](../docs/instruction-editor.md#confirm-one-reviewed-instruction-save-by-voice)
through the shared host question owner; native confirmation remains default/recovery.
Create/copy/delete/conflict overwrite, prompts, profile originals and use/grant
reviews stay native. Save approval and literal body dictation have distinct exclusive capture owners; dictation text cannot answer the Save question ([native editor](../src/Kora/OwnedInstructionEditorController.cs)).
Manual editing needs no model/network.
The explicit [literal owned Instruction body operation](../docs/instruction-editor.md#dictate-literal-instruction-body-text) freezes one foreground canonical owned draft/body caret or range before asynchronous source admission and local capture.
It reuses the single exclusive literal capture service, reviews the complete proposed source after joined release, and accepts only into volatile draft memory. Normal exact Save and separate use review remain mandatory; metadata, prompts, other definition kinds and profile originals are excluded ([target owner](../src/Kora/OwnedInstructionEditorController.Dictation.cs), [native lifecycle](../src/Kora/OwnedInstructionBodyDictation.cs)).
Source/draft/caret/input/control/channel/call/privacy/ownership/name/topology/generation changes retire the original target without command/session/question fallback. Transcript words are inert, with no model or tool dispatch, audio/transcript archive, autosave or enablement. Existing consent/device/local-ASR bounds and unavailable qualification remain unchanged ([shared capture](../src/Kora.Application/Voice/ComposerDictationService.cs), [deterministic native regressions](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.Dictation.cs)).
The bounded [current owned Instruction body refinement](../docs/instruction-editor.md#refine-the-current-owned-instruction-body) now adds native and exact `instructions refine` entry points on this existing editor, not a second profile writer.
The host freezes the exact draft/body/baseline and unchanged critique; only a qualified local, tool-free volatile context turn may propose a complete replacement body. Metadata/source/encoding remain host-owned, and source preparation/freshness precede review and confirmed normal Save ([workflow](../src/Kora.Application/Definitions/OwnedInstructionRefinementService.cs)).
The complete comparison leaves both draft and source untouched. Accepted proposals remain volatile until separately confirmed ordinary Save/readback, which holds changed revisions disabled. Draft/source/critique/private/channel/ownership/expiry changes revoke proposals; no fence stripping, truncation, remote fallback or reference execution is admitted ([native owner](../src/Kora/OwnedInstructionEditorController.Refinement.cs), [actual operation regressions](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.Refinement.cs)).
Real provider enablement remains unavailable; this is not semantic harmlessness or full conversational/package authoring qualification ([registration](../src/Kora/Program.cs)).
Create and inspect use an explicit bounded ID; mutations target the open original.
The closed writer kinds admit only immediate canonical lowercase ASCII `*.instructions.md`
files in `IApplicationDataPaths.RoamingRoot\Instructions` or `*.prompt.md` in
`IApplicationDataPaths.RoamingRoot\Prompts` (separately bounded to 128 files per kind).
The coherent catalogue independently admits those two owned inventories and
retains the existing 128-entry bound for all other artifacts; there is no eviction.
Strict UTF-8 and the original 64 KiB file-byte bound apply. Native format v1
uses scalar `kora-format: 1`, `name`, `description` and optional `attribution`;
the editor preserves original BOM, source text, line endings and supported
metadata. Compatible legacy files lacking `kora-format` retain unchanged
behavior; unsupported scope/metadata cannot be implicitly repaired in place.
Header editing is bounded to 16 fields/4096 UTF-8 bytes. The separately reviewed
[owned Instruction rename](../docs/instruction-editor.md#rename-one-owned-instruction)
adds only `instructions rename <new-id>` / `instructions recover-rename` and the
same native controls. Restore, prompt/profile-original rename, other profile
mutations, executable content and agent runtime remain absent.

#### Bounded recoverable owned Instruction rename

One original user may rename the exact clean inspected canonical native v1
Kora-owned Instruction within its existing owned root. The host prepares a
distinct nonreserved canonical ID and changes only the declared name scalar
(or filename-derived name when absent); it preserves original BOM, formatting,
line endings, attribution, metadata and body bytes. Full old/new identity and
source review includes observed exact declared agent dependents, invocation
changes and unchanged old reference bindings. A separate native confirmation
is mandatory for native, typed and current-name activated entry alike
([shared actions](../src/Kora.Application/Definitions/OwnedInstructionEditorActions.Rename.cs),
[native review](../src/Kora/OwnedInstructionEditorController.Rename.cs)).

This is explicitly a multi-step, recoverable operation, not an atomic rename
transaction or content CAS. A bounded private local intent contains exact
original source and native root/parent/file/stage bindings. The writer moves
the pinned original by native handle into its private same-root recovery slot,
publishes the exact staged new source with native no-replace semantics, verifies
identity/bytes and old-name absence, then removes retained original and intent.
No ordinary copy-then-delete shortcut, arbitrary path, executable/dependency
rewrite, replacement or silent rollback is admitted. Another writer, alias,
reparse/hardlink, parent/root replacement, changed source, occupied destination
case-sensitive/unknown native directory semantics or unconfirmed readback refuses or holds the operation
([native protocol](../src/Kora.Windows/ArtifactDiscovery/WindowsOwnedDefinitionFileAccess.Rename.cs)).

Cancellation after possible intent publication and interrupted/unconfirmed
publication remain explicit recovery-required. Startup/list/reload never
replay that intent; owned source reads/use/writes are withheld until separately
reviewed **Recover rename** resumes that exact proposal and observed phase.
Unexpected membership/identity/bytes or corrupt recovery data remain held;
recovery cannot overwrite subsequent external edits. Recovery data is private
definition content, not diagnostics, conversation retention or a grant, and is
removed only after confirmed completion. Process-interruption fixture evidence
is not distributed sync, power-loss, provider or installed qualification
([phase regressions](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/WindowsOwnedInstructionStoreTests.Rename.cs)).

Both use identities are held before publication. Coherent reconciliation removes
the old candidate and exposes the new exact source disabled, requiring fresh
independent revision review. No grant, activation, applicability, dependent
declaration or approval is transferred or rewritten. Existing source-digest
reference resolution remains authoritative; old IDs cannot select the new
source even after its independent activation. Captured content is never
rebound mid-task; existing access/source-freshness checks and the stricter
[agent-bundle lifetime](#bounded-local-agent-owned-instruction-references-v2)
still apply. Exact requested/terminal audit records contain only content-free
operation hashes and truthful failed/cancelled/unknown/succeeded outcomes
([reconciliation](../src/Kora/ArtifactCatalogueReloadService.cs),
[complete editor fixtures](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.Rename.cs)).

**Open registered profile original** additionally selects only the exact existing personal native v1 instruction already registered/read-consented by the dedicated Customizations source owner. No source registration or new discovery is performed by this editor. The same source-preserving writer remains authoritative for identity, local owner/root/file binding, non-reparse admission, final optimistic comparison, atomic replacement and exact readback ([writer](../src/Kora.Windows/ArtifactDiscovery/WindowsCustomizationStore.InstructionOriginal.cs)).
The complete source is opened in this instruction editor, with supported metadata/BOM/line endings retained. Validate/Compare are memory-only; Save requires a separate complete native old/new confirmation. Detected content conflicts offer Reload/Overwrite/Cancel, with a fresh complete confirmation for every overwrite and another decision after a further detected change. Registration replacement/withdrawal and original input/privacy/call/ownership retirement invalidate pending work. Changed revisions remain disabled and uncertain publication requires explicit registration review plus Reload/reconciliation ([workflow](../src/Kora.Application/Definitions/ProfileInstructionEditorWorkflow.cs), [native operation regressions](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.ProfileOriginal.cs)).
Only strict UTF-8 native `kora-format: 1` canonical Code/Insiders personal User prompts/profile prompts instructions are admitted, under the existing 64 KiB/16-field/4,096-header-byte limits. Unknown metadata, scope/globs, executable kinds, arbitrary paths/roots, repository/workspace discovery, new originals and automatic applicability remain unavailable. This is bounded implementation, not general profile-format or installed-security qualification ([supported subset and recovery](../docs/instruction-editor.md#edit-one-registered-profile-instruction-original)).

**Create Kora copy from profile instruction** is a separate native-only operation for one explicitly selected personal native v1 instruction original, not a general import or profile writer ([delivered workflow](../src/Kora.Application/Definitions/ProfileInstructionForkWorkflow.cs)).
Exact-file memory-only read consent precedes content capture; the later complete original/proposed-source review separately confirms a fresh absent disabled owned destination. Existing editor drafts remain untouched ([native reviews](../src/Kora/NativeProfileInstructionCopyReview.cs)).
The same strict UTF-8/native metadata/header limits apply. The host admits only immediate canonical personal Code/Code - Insiders User prompt files, pins source identity, and bounds final exact-byte checks to the disclosed consent budget; skill registration and compatibility bits grant no fork authority ([reader](../src/Kora.Windows/ArtifactDiscovery/WindowsProfileInstructionReader.cs)).
The distinct copy retains supported bytes and original attribution, adds source attribution and a fresh name, and inherits no enablement, grants, model context or egress. Failed/withdrawn/stale/uncertain outcomes require explicit recovery, never overwrite or automatic retry ([user contract](../docs/instruction-editor.md#create-a-disabled-kora-copy-of-a-personal-instruction)).

**Owned native v2 required literal prompt inputs** extend only the Prompt kind, never instruction v1 or bundled/profile reader semantics.
Use scalar `kora-format: 2` with the same `name`, `description`, optional `attribution` header. Required fields are derived from original-body `${input:name}` markers: 1–64 lowercase ASCII letters/digits/hyphens starting with a letter, at most eight distinct names in first-occurrence order. Repeated names use one value.
No declarations/defaults/optional fields, expressions, environment/files/secrets/clipboard/network/functions or nested/escaped expansion are supported. Malformed or unsupported `${...}` is visibly incompatible. This is not general Copilot prompt-format support.
The shared parser preserves every original literal/body newline/BOM/source byte and original digest. Expansion scans original markers once; marker-looking text in a value stays literal, never commands, tool arguments or system guidance.
Each required native field is nonblank, well-formed Unicode without NUL, at most 512 UTF-16 units / 2048 UTF-8 bytes. The whole expanded request is at most 4096 UTF-16 / 16384 UTF-8 bytes; over-limit text is rejected, never normalized or shortened. The actual serialized model envelope, including host policy/tools overhead, remains separately bounded to 32768 UTF-8 bytes.
Explicit `/prompt <id>`, `/<command>` and current-name activated selection of an enabled exact v2 owned revision focus one private native parameter form; inline parameter values are not accepted. All new/saved/copied native revisions remain disabled until separate current-run exact-template use review.
The [prompt input voice controller](../src/Kora.Application/Definitions/PromptInputVoiceController.cs) accepts one literal utterance only after the original user explicitly selects **Speak input** for one exact declared name in the focused form. Current activated recognition, consent/readiness and exclusive capture apply; no new backend, command reparse, question/approval reply reuse or synthesized UI origin is introduced.
Arming retires preview/review; edits, source/session/channel/focus/privacy changes, cancel or expiry retire that exact voice target. Native release and fresh source validation precede field publication. Whole-request preview and separate exact-session native submission remain mandatory through the [existing consumer](../src/Kora.Application/Definitions/PromptConversationSubmission.cs); unavailable capture preserves native input.
**Preview complete expanded USER REQUEST** shows the whole bounded expansion as inert user-data text, with an independent expansion digest. It does not replace catalogue/source bytes or original digest and is not tool approval, provider qualification or egress authority.
Any input/template/native file identity/review/catalogue/host/call/privacy change retires the preview and pending Use; native pre-preview and pre-use source checks cover missed watcher events. No same-name or restored-byte retargeting/replay is allowed.
**Use reviewed request** separately checks a fresh admitted request with `ModelTurnHost`, preserving original source/revision and exact preview/value identity. Expanded text occupies only `UserRequest`, not `SystemPolicy` or evidence. No typed-command reparse occurs.
**Review submission to exact Active session** is a separate retention workflow, not a change to Preview or nonpersisting Use. Explicit Refresh/Next reads at most 25 metadata rows per page with a complete 64 KiB bound; no target is selected by default or inferred from name, window focus, voice or form values.
Separate **Submit this expanded request to exact session** confirmation shows the entire frozen expanded USER text, immutable target ID/name/generation/metadata revision and volatile original-source/revision/digest plus independent expansion digest. It discloses private readable SQLite, existing retention, best-effort reject-only screening and unavailable generation; Cancel retains only eligible memory input.
The application [prompt submission workflow](../src/Kora.Application/Definitions/PromptConversationSubmission.cs) repeats actual native `CaptureUse` freshness before review and admission, then calls the existing conversation service once. Its final physical COMMIT callback requires the same private controller/preview/target epoch. Source validation and SQLite COMMIT are not a global filesystem/database transaction.
Only the exact approved expansion is retained as one immutable USER conversation message, with a fresh conversation request/task and required audit. No template, hidden field record, source path or durable source provenance is added; no old Use admission is also invoked. Original template bytes/digest remain unchanged; prior schema/IDs/copies are preserved.
Input/source/native identity/use/catalogue/privacy/ownership/target changes and loss of window focus withdraw pending review. Stale/Done/Removed targets require refresh and explicit reselection, never implicit Resume or a new session. Confirmed receipt clears the form; an unconfirmed attempt is held for exact conversation/task inspection without retry or rollback claims. Saved drafts and unrelated Sessions composer buffers are never imported, overwritten or cleared.
Production adapters/catalogues remain unqualified/unavailable; Ollama 0.35.1/qwen3 and 0.40.2 remain rejected. Use reports the admission outcome without generating, invoking the legacy reasoner, installing/authenticating or falling back. Even controlled qualified admission does not enable production generation in this increment.
Fields and expanded preview buffers exist only for this view lifetime: Cancel/close/private closure clears controls and references immediately, with no autofill/preferences/source autosave/logging/audit text/clipboard export/speech readback. Only separate explicit conversation confirmation retains the approved expansion; Preview and Use write no form body.
Source checks consume view cancellation, and handoff/exit joins pending work before releasing owned resources. A new accepted typed/activated request retires the original preparation even when typed text is unchanged; original input is captured before awaited native source validation.
Already captured immutable requests are not mutated by later source edits; ordinary source/privacy/approval boundaries still apply.

**Native displayed owned-buffer Find** is separate from catalogue metadata navigation and passive Compare.
The [native toolbar](../src/Kora/OwnedInstructionEditorWindow.Find.cs) searches only the exact currently displayed
owned Instruction/Prompt text, including dirty or structurally invalid drafts, without disk reads or source discovery.
It reuses [SnapshotLiteralFind.FindCounted](../src/Kora.Core/Context/SnapshotLiteralFind.cs) for bounded,
well-formed one-line, case-sensitive ordinal literals, complete nonoverlapping counts/ordinals and First/Next/Previous without wrap.
Only the exact UTF-16 occurrence in the existing source control is selected; an edge clears Find's selection
but retains the cursor for reverse navigation. Query/source edits retire match/count/navigation; independent native
user selection is not Find-owned. Native focus/selection failure reports unavailable, never a successful match.
The [controller binding](../src/Kora/OwnedInstructionEditorController.Find.cs) captures the exact owned kind/ID,
inspected source identity (or explicit new draft), raw buffer revision, current private host eligibility and ownership lifetime.
Loading, chosen kind/ID/file changes, hide/close/private/control retirement and reentrant callbacks fence all publication/focus;
callbacks cannot select an undisplayed file or retarget a replacement editor. Dirty drafts and existing discard reviews survive.
No body mutation, inventory selection, Save/Reload/Validate/Rebase, use enablement, execution, dictation,
clipboard/export, transcript, speech or content logging is added; existing source editing/undo/accessibility remains native.
Profile/bundled sources acquire no edit/use authority. This consumes only the existing editor and literal-policy I subset;
no provider, hardware, installed-native, experiment or E/Q evidence is an implementation prerequisite.
See the [user workflow](../docs/instruction-editor.md#find-in-the-displayed-owned-buffer) and
[headless lifetime tests](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.BufferFindLifetime.cs).

**Passive owned baseline/draft Compare** is an explicit native and exact `instructions compare` / `prompts compare` memory-only observation of the currently open same-kind inspected baseline or created draft. Complete inert source panes preserve raw metadata/attribution/whitespace; BOM/digests and shared changed-field rules disclose byte truth. Prepared Save validation is separately labelled, never substituted for raw text.
Independent [pane-local literal Find](../src/Kora/SnapshotComparisonFindPane.cs) reuses the authoritative [Core snapshot policy](../src/Kora.Core/Context/SnapshotLiteralFind.cs): bounded private queries, ordinal case-sensitive First/Next/Previous without wrap, exact UTF-16 locations and explicit invalid/no-match outcomes. Missing baselines are labelled absences, never searchable fabricated source.
Query edits retire cursor/match; Clear resets that pane. Close, owner/private/eligibility retirement and replacement release queries, matches, source and callbacks. Keyboard/automation stays inert; Find grants no clipboard, persistence or other authority ([lifecycle tests](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.ComparisonFindLifetime.cs)).
No source read/reload, mutation, use-state change, approval, clipboard/export/model authority or line-diff patch is added. Epoch changes retire the display; private closure clears buffers. Structural errors remain explicit, over-budget/invalid Unicode is rejected without truncation. Existing independently confirmed Save/conflict races are unchanged. See the [bounded Compare contract](../docs/instruction-editor.md#compare-the-open-draft-without-saving).

**Explicit source-preserving owned draft Rebase** adds native and exact `instructions rebase` / `prompts rebase` observation/review of an already open supported baseline.
The [admitted workflow](../src/Kora.Application/Definitions/OwnedInstructionEditorActions.Rebase.cs) explicitly reads latest, validates exact kind/owned/artifact identity with the existing Save parser, and freezes old/latest/raw independently. It never merges or writes a definition.
The [three-body review](../src/Kora/OwnedInstructionRebaseReviewWindow.cs) shows complete inert source and exact names, kind, revisions, digests, BOM/byte counts and shared change/equality truth.
Shared prepared-Save validation is separately labelled; structurally invalid bounded raw drafts remain verbatim, while over-budget/invalid Unicode is rejected rather than shortened ([observation](../src/Kora.Application/Definitions/OwnedDraftRebase.cs)).
Confirmation explicitly rereads latest and requires the frozen exact revision before adopting only that baseline in memory. Cancel retains the previous baseline/raw draft; source, control, private, ownership, input and editor generations fence both review and late adoption.
Old editor validation/comparison and use-review eligibility retire; no use is enabled or revision preference rewritten ([editor lifecycle](../src/Kora/OwnedInstructionEditorController.Rebase.cs)).
Deleted/missing, wrong-ID/kind or incompatible latest sources fail closed. An observed catalogue/control ABA invalidates review; an unobserved change restoring identical revision cannot prove history. Uncertain publication is not reconciled by Rebase.
Future Save remains separately admitted, fully confirmed and optimistic, and can conflict again ([deterministic cases](../tests/Kora.Application.UnitTests/Definitions/InstructionEditorTests.Rebase.cs)).
This consumes only the existing owned reader/admission/parser/editor I subset. No profile-original writer, grant, runtime/provider/egress enablement or E/Q qualification is added; Reload/Overwrite/Compare/SaveCopy retain their existing semantics ([user contract](../docs/instruction-editor.md#rebase-the-open-draft-without-saving)).

**Owned metadata inventory/search** extends this same native editor, not the future full Definitions source matrix. Explicit List/Refresh reuses admitted per-kind filename enumeration and existing reconciliation; an immutable projection includes only those owned filenames and coherent observed catalogue metadata.
Literal filename/display-name/source-qualified-ID search uses the existing 256 UTF-16 / 512 UTF-8 query ceilings without lexical tokenization, regex or path resolution. Invalid controls/Unicode/oversize input requires correction, never trimming. All/UseEnabled/ReviewRequired/UnavailableOrUnknown reflects only actually known observed use state.
Explicit **Literal** (default), **ExactOwnedId** and **ExactArtifactId** navigation modes reuse the same admitted snapshot and [inventory matcher](../src/Kora.Application/Definitions/OwnedDefinitionInventory.cs).
ExactOwnedId reuses canonical [OwnedInstructionNames.WriteName](../src/Kora.Core/ArtifactInvocation/OwnedInstructionNames.cs), matching only the owned name ID without extension/path.
ExactArtifactId reuses [ArtifactDefinition.ValidateId](../src/Kora.Core/ArtifactInvocation/ArtifactDefinition.cs), matching only an existing mapped, revisioned observed artifact ID by ordinal case-sensitive equality.
Unknown/unmapped rows cannot match exact artifact identity; display/name/correlation metadata never substitutes for it. Exact modes reject empty or malformed input without normalization.
Ordinary digest/revision/observed-time query widgets were removed in [#422](https://github.com/roryprimrose/Kora/pull/422). Name/identity modes and observed use-state browsing remain. Complete filenames and mapped metadata are still validated before exclusion; displayed digests/revisions never grant use authority.
No body, excerpt, description, attribution or full path is projected. Kind, filename ID, available display name, fixed owned source, artifact ID, exact digest/native revision and observed UTC are retained. Complete native label/details payloads fit 64 KiB UTF-8 using deterministic explicit Next pages when necessary; no partial-success truncation or durable index is introduced.
Internal native revision and inventory observation-time semantics remain unchanged. They describe admitted source/snapshot identity, not freshness, trust, ownership or permission ([current native browsing](../docs/instruction-editor.md#search-observed-owned-metadata)).
Rows are observations, not disk-current integrity or use authority: native Inspect compares the original row against current admitted catalogue identity and native readback. Missing/unmapped metadata cannot prove a match. Stale/replaced/changed/withdrawn choices require Refresh/reselection instead of silently showing a newer same-name file.
Search/filter/page changes preserve the open original and dirty draft, never auto-select/load the first result or activate/save/discard. Failed reload removes current availability with historical/unavailable disclosure. Source/use/call changes invalidate choices; private ownership/closure clears private controls and fences late callbacks.
Mode/query/use edits synchronously retire prior page/selection/details/continuation and pending inspection/review/use callbacks without disk/catalogue reads ([native browsing contract](../docs/instruction-editor.md#search-observed-owned-metadata)).
Explicit **FileNameAscending** (default), **FileNameDescending**, **DisplayNameAscending** and **DisplayNameDescending** order the complete validated matching metadata before bounded pagination. Comparisons are ordinal, culture-independent and non-natural. Absent display names are last in either direction; ties always use canonical filename ascending ordinal ([ordering tests](../tests/Kora.Application.UnitTests/Definitions/OwnedDefinitionInventoryTests.Ordering.cs)).
Filename keys/ties use exact extensionless owned filename IDs, preserving existing prefix ordering (`alpha` before `alpha-more`) and admitted legacy casing/Unicode without changing lowercase ASCII write/exact-owned-ID rules. Complete validation precedes exclusion/ordering; the 64 KiB complete-page budget and explicit identity/source failures remain ([inventory](../src/Kora.Application/Definitions/OwnedDefinitionInventory.cs)).
Continuations bind the inventory and complete order/mode/query/use/digest/revision/time state; changing order rejects an old continuation. Accessible Clear and toolbar Escape restore FileNameAscending and Literal and reset every predicate; private closure/ownership/kind retirement clears the new fields too ([native controls](../src/Kora/OwnedInstructionEditorWindow.cs)).
Order edits use the same no-read retirement of rows/details/selection/cursor/validation/review readiness while retaining the exact raw draft and frozen baseline; separately admitted historical Compare remains available. No saved preference/schema, file/body read, refresh, mutation, activation or navigation authority is introduced ([native ordering tests](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.InventoryOrdering.cs)).
Mode edits retain raw unsaved drafts and the frozen baseline; an open Compare retires, while separately admitted historical Compare remains available without reading or claiming current source state ([native lifecycle tests](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.Inventory.cs)).
Exact reinspection restores eligible review controls. Predicates are metadata/correlation only, never use authority, revision admission, content identity override or current-disk integrity proof.
No source addition, reload on edit, persistence/schema/index/cache, logging/tags/audit input, activity renewal or model/tool/speech/clipboard/export route is added.
Exact typed/current-name activated List/Inspect still targets the same native controls; there is no new search command grammar, model tool, reader/root permission, profile/bundled-as-owned inventory or writer. Selected profile originals use their separate [bounded operation](../docs/instruction-editor.md#edit-one-registered-profile-instruction-original), not this owned inventory.

**Validate open owned draft** / exact `instructions validate` or `prompts validate` observes the captured same-kind canonical original or explicitly created draft without opening sources or switching targets. It reuses ordinary Save preparation, including original BOM and any native v1 marker added for changed/new proposals; unchanged supported legacy bytes stay legacy.
Existing strict Unicode/UTF-8, complete-byte/header/metadata/versioned-prompt and observed catalogue namespace rules apply to prepared Save bytes, not just raw draft text. Bounded text-only findings expose proposal byte count/digest/actual native version and unapproved kind/source/model-context role, never body/excerpts or executable preview.
The result is historical structural compatibility only, not harmlessness, safe execution, disk freshness, approval or enablement. Source/name/kind/baseline/catalogue and private/call/input epochs fence publication and retire findings; Save independently re-prepares and confirms current bytes.
No owned-store enumeration/read/write, source reload/reconciliation, preference hold/write, activation, model/network or reference/dependency access occurs. Normal observational control receipts/traces remain; existing drafts, inventory/selection and enabled or uncertain held source state are unchanged. Unavailable sources retain explicit Reload/reconciliation recovery. The selected-original operation has its own [bounded comparison](../docs/instruction-editor.md#edit-one-registered-profile-instruction-original); broader format/writer/runtime qualification remains deferred.

**Save as new copy** / exact `instructions copy <new-id>` or `prompts copy <new-id>` is delivered only for an inspected canonical supported native owned original (instruction v1; prompt v1/v2) and its retained current draft.
It is not an import or profile-original writer. Legacy/unversioned or unsupported originals cannot be implicitly upgraded by copying.
Review freezes source-qualified identity, canonical filename, base revision/digest, current draft exact bytes/BOM, destination same-kind canonical ID/path and full proposed bytes.
Only the declared name is changed from the draft to the chosen destination ID; all other supported source formatting/metadata/attribution/body bytes are retained. Filename-derived instruction names need no metadata rewrite.
Copy reuses the admitted mutation workflow and native store `Write(expected: null)`: final destination absence and atomic create-no-replace are mandatory.
Occupied, concurrent or case-alias destinations require another ID/cancel, never the ordinary Save overwrite loop. Source disk changes do not select a newer base; current draft/name/kind/selection/input and host/privacy/call/session fences invalidate stale copy approval.
Success retains the original open draft/source and reports the separate destination identity/digest. Verified readback enters the common revision reconciliation pipeline without new profile discovery.
The copy is disabled and requires independent exact current-run explicit-use review; it inherits no activation, grants, trust or references. Cancel preserves inventory/draft.
Uncertain publication/audit/catalogue outcomes hold the destination only, never unchanged enabled original bytes, and claim no rollback.

Prompts require `name` and `description` in the editable subset. Unsupported
parameter declarations, required substitution syntax such as `${input:...}`,
tools/runtime metadata and other unsupported scalar fields are rejected rather
than expanded or silently dropped. This is not full Copilot prompt compatibility.
The existing explicit artifact route carries a managed literal prompt body as
user-request content, never system/instruction authority. All original request,
mode/context/provider and action-approval gates remain; saving or reviewing
does not start reasoning, scheduling or execution.
One kind selector operates the same native editor and dirty-close/shutdown review.
Kind plus canonical file/source identity qualifies holds, freshness and audit.
The existing instruction `instruction-revisions.json` v1 schema is unchanged;
the prompt kind has independent strict `prompt-revisions.json` v1 state. No
instruction enablement, revocation or digest migration occurs.

Native file/parent identity and exact bytes are rechecked under durable original
local-input/session admission. Complete private staging is flushed, then renamed
relative to the pinned source directory. Pre-publication cancellation leaves the
base intact; uncertain post-publication readback/audit/catalogue outcomes are held
and explicitly require reconciliation, never rollback. Saved revisions enter
disabled, source-qualified catalogue candidates; activation separately reviews
the exact bytes for explicit instruction or saved-request selection on a new request, not broad
automatic applicability or trust. Source changes/replaced identity, failures,
deletion and restored previously revoked bytes do not auto-activate. Pre-use
native freshness checking complements advisory watchers. Existing unchanged
legacy and bundled behavior is preserved.

**Disable reviewed use** / exact `instructions disable` or `instructions stop-use` (and matching `prompts` controls) is delivered for the clean inspected canonical supported native owned baseline (instruction v1; prompt v1/v2).
Separate native confirmation binds kind, source, filename, digest, native identity-qualified revision and current original-input/ownership/privacy/call/session epoch; dirty drafts remain intact with an explicit Save/Reload prerequisite.
Native source freshness is rechecked before revocation under the existing short admission gate. Old reviews cannot target a replacement, and retired activation preparation/completion or stale reload publication cannot re-enable the held revision.
Requested/terminal trusted typed audit and the control receipt precede visible completion. Strict per-kind v1 preference hold/readback is durable; cancellation, mandatory audit/storage/readback or terminal failure exposes any held/unavailable outcome without rollback, retry or previous-enable restoration.
No source writer, deletion or unregistering occurs: original bytes/BOM/metadata/DACL/filename/digest/inventory stay unchanged and bounded inspection/editing remains available.
Every new explicit selection and catalogue descriptor excludes the held revision. Already admitted reasoning retains its captured immutable content; pending unadmitted question/action choices retire without dispatch or retargeting, and mandatory source/access/privacy/action/egress guards remain.
No-change/watchers/manual/startup/pre-use reconciliation or restored old bytes cannot resurrect use. Repeated exact Disable reports already held; a later fresh exact use review enables only the current revision.
Other kinds with the same filename, unrelated copies/sources, bundled and unchanged legacy compatibility remain isolated. Disabled originals can still produce separate disabled owned copies; no profile writer, new source/grant authority, automatic applicability or full-management qualification is introduced.

Production parser, admission/conflict workflow, native storage and catalogue
fixtures are the maintained assertions for this increment only. They are not
equivalent to installed UI/accessibility, runtime/provider, containment, acoustic
quality, storage-engine/capacity or physical-interruption evidence. The distinct
historical witnesses and consumer assessments retained in the
[historical roadmap](Archive/Implementation_Roadmap_2026-10-11.md#experiment-disposition-inventory)
remain unchanged; no experiment files or consumers are retired by this increment.

Extend R21 source/reconciliation work and R22 authoring incrementally through
the common contract; qualify profile mutation separately from read access.
Prompt support stays lightweight; do not introduce a second execution engine.
Agent run controls stay gated by the qualified host/runtime described in
[definition and agent guidance](Commands_Tools_And_Skills.md#bundled-definitions-and-agent-profiles).
Do not advertise a kind/operation until its actual reader, writer, UI/voice
action, audit, lifecycle, and acceptance coverage exist.

The [definition management acceptance gate](Acceptance_Criteria.md#definition-management-and-external-reload-gate)
requires the full kind/source/channel matrix, exact-byte round trips,
source consent and mutation isolation, dirty-editor conflicts, concurrent
publication, watcher loss, dependency invalidation, and truthful unavailable
runtime/storage states. Existing bounded R21 evidence does not pass that gate.
