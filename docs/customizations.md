# Instructions, skills, agents and MCP servers

Open **Tray > Kora Customizations** immediately below **Sessions**, type `show customizations`, or use the current
assistant-name prefix with activated voice, such as `Kora, show skills`.
The same native window serves all these routes.

**Choose a task:** [browse](#browse-by-type-and-source), [edit or add](#edit-or-add),
[disable or enable use](#disable-or-enable-kora-use), or open the dedicated
[instruction](instruction-editor.md), [skill](owned-skill-authoring.md) or
[agent](agent-profile-authoring.md) editor.

Instructions guide an answer; prompts are reusable requests; skills provide
reusable task guidance; agents add a role and limits. MCP servers describe
external tool connections. Reading or saving any of these never runs it.
Manual editing works without AI; **model-generated proposals and recommendations
are unavailable in this build**.

## Browse by type and source

Categories appear in this order: **Instructions**, **Skills**, **Agents**,
**MCP Servers**, then **Prompts**. Instructions is selected when the window first
opens; an explicit artifact-type command selects its requested category.
Each type has three source groups:

- **Built-in**: bundled binary resources. Inspect their complete files; originals
  are read-only. A Kora copy contains declarative guidance, not a copied executable
  implementation.
- **Kora**: definitions beneath Kora's roaming-profile data directory.
- **User**: supported Copilot skill folders and VS Code/VS Code Insiders user
  profile sources. The source path distinguishes identical names and profiles.

Search matches names and source paths, not artifact bodies. Built-in groups can
be empty for a type. Selecting an artifact reads its complete bounded file set as
plain source; it does not execute scripts, invoke agents or connect MCP servers.
Binary, oversized, unsafe-path or unsupported packages report an error rather
than silently omitting components.

MCP entries are the native-only exception: selecting a Kora entry first asks consent to read that exact entry into private HTTPS fields.
User/profile and additional MCP files remain metadata-only here.
Unsupported or sensitive selected-entry fields are not shown or silently removed ([source-read workflow](../src/Kora/CustomizationWindowController.Mcp.cs)).

## Discover which selected customizations may help

Type `discover customizations <your question>` or use the current assistant-name activated-voice prefix. With no question, enter it in the private native window. This is a separate metadata-only operation, not the complete-file editing or authoring route below ([original routes](../src/Kora.Application/ViewModels/MainViewModel.CustomizationDiscovery.cs), [native window](../src/Kora/CustomizationDiscoveryWindow.cs)).

1. **Refresh supported source choices** lists the supported Kora roaming and User profile skill, agent and instruction choices without reading definition bodies or MCP configuration. Nothing is selected automatically. Private source-path labels distinguish choices but are never shared with the model.
2. Deliberately select one to six exact entries across the kinds and one existing Active conversation. Enter or review your original question. No conversation is created, resumed or inferred.
3. **Read selected primary metadata and preview exact shared fields** reads only those primary definitions, not skill-package components. Review the original question and the complete bounded metadata reply: opaque per-snapshot `ref`, `kind`, readable `name`, bounded `description`, safe `sourceCategory`, `compatible`, `enabled` and `availability`.
4. **Share only this reviewed metadata for ONE local answer** submits the unchanged question through the canonical conversation route. The local model can call only `customizations.list` and `customizations.get` over the frozen selection, then produce a genuine recommendation. The original question and genuine answer or truthful terminal outcome are retained in the selected conversation; host-added scope text records how many reviewed entries were actually served.

These steps share one [reviewed snapshot owner](../src/Kora.Application/Tools/CustomizationDiscoverySnapshot.cs), [read-only native metadata projection](../src/Kora.Windows/ArtifactDiscovery/WindowsCustomizationStore.Metadata.cs) and [canonical retained submission](../src/Kora.Application/Hosting/SessionConversationService.CustomizationDiscovery.cs). Choosing sources or reviewing metadata never enables a definition, loads instructions, grants authority, registers effects or invokes a recommendation.

`compatible` means supported metadata parsing, **not executable compatibility**. `enabled` reflects current admitted catalogue/use control for the exact observed digest; a missing, held, disabled or unmatched candidate is not reported as enabled. `availability: NotObserved` explicitly means executable readiness was not established ([projection](../src/Kora.Application/Tools/CustomizationDiscoveryService.cs)).

The snapshot expires after two minutes and is single-use. Input, selection, session, privacy, ownership, source or use-control changes require a fresh choice/review. Invalid, missing, changed, interrupted or revoked sources report failure rather than an empty/current or last-good catalogue. Cancel/close retires disclosure immediately; model work remains owned until actual termination, and late answers cannot publish ([freshness owner](../src/Kora.Application/Tools/CustomizationDiscoverySnapshot.cs), [retention lifecycle](../src/Kora.Application/Hosting/SessionConversationService.Execution.cs)).

The limits are six selected entries, 128 UTF-8 bytes per readable name, 256 UTF-8 bytes per description, 1,024-byte complete callback input, 4,096-byte complete callback reply and 8,192 cumulative reply bytes. Paging stays inside the selected snapshot; `get` accepts only its opaque refs. Unknown fields, queries, paths, forged refs and invalid pages are rejected; oversized fields/results are not truncated ([wire contract](../src/Kora.Core/Tools/CustomizationDiscoveryTools.cs), [input policy](../src/Kora.Core/Tools/CapabilityInputPolicy.cs)).

The operation is **LocalOnly** and requires the existing qualified local model profile; unavailable readiness remains unavailable, without a service fallback. Ordinary turns do not automatically share this metadata. Built-in definitions, additional registered roots outside the supported chooser, prompts, MCP configuration/accounts, source bodies, scripts/hooks, commands/environment, unsaved buffers, history/memory, automatic instruction loading and hosted disclosure are excluded ([model policy](../src/Kora.Application/Hosting/SessionConversationService.CustomizationDiscovery.cs)).

### Review and run one model-recommended local agent

Before reviewing metadata, select **Ask for one agent recommendation (never invocation consent)**.
The same bounded local metadata request may recommend exactly one enabled registered
Agent from the entries actually returned to it. A recommendation runs nothing and
is labelled as a proposal, not an answer from that agent.

1. Select **Review proposed agent in its original conversation**. This opens the
   existing private agent review with the exact source, unchanged original question,
   original channel and original conversation fixed. It cannot redirect to another
   conversation or substitute model-generated input.
2. Select **Read source and review exact invocation**. Review the complete inert
   profile, source, original question, conversation, local destination, tools and
   cumulative budgets. Only local v1 instruction-only profiles with `tools: none`
   and `references: none` are eligible for this route.
3. Separately confirm that exact invocation. The existing bounded agent workflow
   runs once and retains its genuine answer or truthful terminal in the original
   conversation. The native agent review displays that actual result. Stop/close
   joins owned execution; cancellation-ignoring work cannot publish a late answer.

The recommendation expires with its two-minute metadata review. Source, use control,
input, session, privacy or ownership changes require a fresh request and review;
neither a retained recommendation nor profile guidance grants authority.
The parent recommendation has finished before the agent review opens: no fabricated
tool reply or parent continuation is sent, and no private profile or child answer
is transmitted to a hosted model. Hosted parents, tools, referenced profiles,
automatic invocation, nested agent chains and provider fallback remain unavailable.

## Edit or add

Use `show me example instruction` or `edit example skill`, or select an exact
source in the window. Ambiguous names require native source selection.

Choose a new lowercase ID and **Kora** or **User** destination, then **Add**.
`add an instruction named example` prepares that same new draft. An unnamed
`add a skill` opens the creation controls; choose its ID before adding.
For Instructions, **Add** supports Kora destinations only; User originals use the separately reviewed registration below ([destination policy](../src/Kora.Windows/ArtifactDiscovery/WindowsCustomizationStore.cs)).

### Edit one registered personal instruction original

Select one exact **User / Instructions** row, then **Review original registration**. Before source content is read, the native review discloses the exact personal file, bounded reading and supported editing scope. Registration lasts only for this Customizations window, not across restarts; previous read-only registrations are not edit consent ([registration workflow](../src/Kora/CustomizationWindowController.InstructionOriginal.cs)).

Only an existing canonical lowercase `*.instructions.md` in a personal Code or Code - Insiders User prompts/profile-prompts location is supported. This increment admits strict UTF-8 native `kora-format: 1`, at most 65,536 file bytes including a retained BOM, and the existing bounded scalar header. Unsupported metadata, scope/globs, hooks, other formats and unsafe/nonlocal targets are refused, never silently rewritten ([native binding](../src/Kora.Windows/ArtifactDiscovery/WindowsCustomizationStore.InstructionOriginal.cs), [format](../src/Kora.Core/ArtifactInvocation/ArtifactFileDocument.ProfileCopy.cs)).

After registering, edit the complete exact source in the existing editor. **Review and save** separately displays the source-qualified shared original and complete original/proposed source, including BOM/byte metadata. **Save disabled** confirms that precise mutation, not a fork: other applications and external sync use the same original ([save owner](../src/Kora.Windows/ArtifactDiscovery/WindowsCustomizationStore.InstructionOriginal.cs)).

BOM, proposed line endings, supported metadata/attribution, owner/group/DACL, ordinary file attributes and creation time are preserved. Named streams or unsupported native attributes are refused rather than dropped. Changed bytes require a separate current-content **Enable** review ([native preservation](../src/Kora.Windows/ArtifactDiscovery/WindowsOwnedDefinitionFileAccess.InstructionOriginal.cs), [use review](../src/Kora.Application/Definitions/CustomizationManagementService.cs)).

Conflicts retain the previous base and dirty draft. **Compare external** offers **Keep draft**, **Rebase complete draft**, **Discard and reload**, and **Cancel**. Rebase adopts only the exact reviewed external base, keeping the complete draft unchanged without merging or saving it. Another external change requires another comparison; replacement of the native original requires renewed registration first ([conflict workflow](../src/Kora/CustomizationWindowController.InstructionOriginal.cs)).

Single-file publication uses the existing optimistic conflict policy, not content compare-and-swap. Native identity and exact bytes are compared after staging immediately before atomic replacement relative to the pinned directory. A same-name replacement can still race that last comparison. Uncertain publication remains held and requires explicit registration/inspection; there is no rollback claim or automatic retry ([policy](../Design/Definition_Management.md#editing-ownership-and-confirmation), [writer](../src/Kora.Windows/ArtifactDiscovery/WindowsCustomizationStore.InstructionOriginal.cs)).

**Withdraw original registration** clears this window's source/draft and revokes pending access/writes without deleting originals or independent Kora copies. Close/privacy/ownership retirement also retires the registration. For a compatible original you do not want to edit in place, the [owned instruction editor's existing **Create Kora copy from profile** route](instruction-editor.md#create-a-disabled-kora-copy-of-a-personal-instruction) remains independent; unsupported content is not automatically adapted ([retirement](../src/Kora/CustomizationWindowController.cs)).

This bounded original editor adds no agent/skill/prompt/package writer, filesystem tool, model-selected path, external editor launch, inherited use/grant or runtime qualification. Existing typed/activated-voice navigation reaches this same native surface; registration and write consent remain native reviews ([entry routes](../src/Kora/CustomizationWindowController.cs), [authority boundary](../Design/Security_Data_Flows.md#application-integrity-and-no-self-modification)).

### Complete draft review

Edit the source directly. Skills can also **Add file**, **Rename file**, and
**Remove file**, including bounded relative components such as
`references\notes.md`. The primary file cannot be removed or renamed.
Standalone instructions, agents, prompts and selected MCP entries have one
editable source. MCP edits preserve unrelated entries and configuration bytes.
Component filenames support case-only renames. A file cannot also be another
component's directory; conflicting file sets are rejected before publication.

**Review and save** shows the complete original/proposed file sets and exact
destination. Confirmation writes and reads back the files, leaving the artifact
disabled in Kora. Saving never enables a runtime, grants permissions, starts a
process, authenticates or makes a connection.

User edits change the originals used by other applications. Multi-file writes
are **recoverable, not atomic**: VS Code or Copilot can observe intermediate
files. Interrupted writes retain a private local recovery transaction. Use
**Review recovery**, inspect original/current/proposed complete sets, then
separately confirm the exact staged save. Unexpected external edits block
recovery; they are not overwritten. Keep the recovery record until the conflict
is reconciled. Recovery drafts cannot be independently edited or retargeted.

Changed sources require Reload and renewed review. Dirty navigation or closing
offers **Save / Discard / Cancel**. Privacy/ownership withdrawal closes private
presentation and suppresses late results, without autosaving draft text.

## Discuss or generate a proposal

### Author one reusable prompt request

In **Customizations > Prompts**, choose **Kora**, enter a new ID and select **Add**, or reopen one existing Kora prompt. New prompt templates use native v2; generation never upgrades existing metadata or edits profile originals ([template](../src/Kora.Core/Customizations/CustomizationLayout.cs), [validation](../src/Kora.Application/Definitions/PromptTemplateAuthoring.cs)).

1. Describe the repeatable **request**, not a skill workflow or system instruction, in the request field. Enter the required literal names in the separate host-owned field, for example `topic,audience`, in their intended first-use order. Empty means no inputs; otherwise declare at most eight distinct native names without spaces, defaults or expressions.
2. Select **Ask Kora (review local disclosure)**. Review the exact description, complete current source, destination and declared literals. Only **Send for local authoring** authorizes this local, tool-free model disclosure. The existing typed or activated-voice refinement route reaches the same draft and independent native reviews.
3. Inspect **Review proposed draft**: the exact complete template/source and required literals. Model output may replace only the request body; metadata, format, attribution, filename and BOM stay host-owned. Missing, additional, malformed or oversized literals/proposals are rejected without stripping or truncation.
4. **Use proposed draft** changes memory only. Separately select **Review and save**, inspect the complete original/proposed source and destination, then **Save disabled**. Cancellation retains the draft; changed sources and occupied new destinations require explicit recovery, not an automatic overwrite.
5. Reopen the saved prompt. **Enable** removes the Customizations exclusion only; the existing owned prompt review is still independent: `prompts inspect <id>`, then `prompts activate` and its native exact-template review. Select `/prompt <id>`, enter every required literal, inspect **Preview complete expanded USER REQUEST**, and separately review/confirm submission to one explicitly chosen Active session ([existing use operation](instruction-editor.md#prepare-required-native-prompt-inputs)).

The [host validator](../src/Kora.Application/Definitions/PromptTemplateAuthoring.cs) uses the existing native parser: names start with a lowercase ASCII letter and contain only lowercase letters, digits or hyphens, at most 64 characters. Each value is nonblank and bounded to 512 UTF-16 units / 2,048 UTF-8 bytes; the expanded request is bounded to 4,096 / 16,384. Substitution visits original markers once: marker-looking input stays literal. No reads, secrets, defaults, expressions, recursive expansion, tools, automatic activation or submission are added.

**Cancel prompt authoring**, edits to the description/declaration/draft, changed input/source/catalogue/provider, privacy/channel/call/ownership loss, close or the two-minute proposal lifetime retire pending work. Late output cannot replace the draft; cancellation retains model ownership until actual termination. Unknown capabilities and unavailable local models report refusal without hosted fallback. Production model qualification remains unavailable; deterministic implementation tests do not enable a provider ([lifecycle](../src/Kora/CustomizationWindowController.cs), [complete-operation regressions](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/CustomizationWindowTests.PromptAuthoring.cs)).

### Refine other definitions

Enter a request in **Describe or discuss a change with Kora**, then choose
**Ask Kora (review local disclosure)**. The typed or activated-voice equivalent is
`refine this artifact <your request>`. Typed input uses the current editable
draft; voice binds the foreground draft at activation, not whichever editor is
open when recognition finishes.

First review the exact request and complete draft that would go to the admitted
local model. This is a separate explicit authoring disclosure even when runtime
use is disabled. No tools or hosted-provider fallback are allowed. If local-model
admission is unavailable or rejected, the operation reports that outcome and
keeps the draft.

Review the explanation and complete proposed files. **Use proposed draft** only
changes the in-memory draft; **Review and save** and runtime enablement remain
separate. Editing the draft/request while a review or generation is pending
retires that proposal. There is no automatic generation, save, execution or
editor-body dictation binding.

### Speak naturally to the foreground editor

With one editable artifact foreground, hold **Hold to speak** in that editor,
say your request, and release. You can also focus the button and hold
Space or Enter. The control uses Kora's shared push-to-talk consent, microphone,
privacy and ownership checks; it does not enable ambient listening.
For example, say
**make this instruction more concise** or **explain this skill's error handling**.
The editor displays **Voice context** with the type, source and name. An editor
open in the background does not claim general speech.

First, **Review editor voice routing disclosure** shows the exact recognized
request and minimal editor metadata for local, tool-free intent interpretation.
No artifact files or session conversations are supplied at this stage.
An uncertain or unrelated request opens **Choose voice target**: explicitly
choose refinement/discussion of this artifact, or **Cancel**. For another
artifact, a session or a New request, cancel and use its explicit route; Kora
never forwards a rejected editor utterance automatically.

You can answer this exact clarification with its buttons, or select **Answer by
voice** and say **Refine this artifact**, **Discuss this artifact**, or
**Cancel** (the displayed option number also works). This is one bounded local
reply, not another general command; an unmatched reply cancels without forwarding
it. The question expires after 75 seconds and never extends the original
request's deadline. Capture must close completely before the original operation
continues. If voice is unavailable, use the native choices.

After routing, the request appears in **Describe or discuss a change with Kora**.
The separate complete-draft disclosure shows the captured component and
resolved edit/discussion mode as well as every supplied file. That same scope
reaches authoring without rewriting your request, so **this file** keeps its
component and choosing discussion remains discussion even for edit-worded
speech. The proposed-draft review above still applies. Discussion cannot change files.
The spoken clarification is not approval of disclosure, a proposal, Save or
enablement: those separate native reviews remain required. "Yes" or "save" is
not an answer to this clarification.

Focus/presentation, component/source, request/draft or host/privacy/call changes
retire the original target. Returning to it does not revive old speech or late
model output. A stale, cancelled, unavailable or failed editor operation never
answers another question or starts a session. Existing dedicated question
replies and literal dictation keep their own exclusive input ownership.
Explicit supported local/session commands keep their ordinary routes.

This uses the selected admitted speech and local-model capabilities; it does not
enable ambient wake, qualify a provider, add hosted fallback or dictate into the
source body. If capture or inference is unavailable, use native text. Built-ins
require an explicitly chosen Kora copy before authoring; pending recovery must
be reconciled first.

## Set up one Kora HTTPS MCP server

In **Customizations > MCP Servers**, choose **Kora**, enter a new lowercase ID and select **Add**.
Alternatively type `add an MCP server named example`, or use the current assistant-name activated prefix.
To edit an existing entry, select its exact Kora source or use `edit example MCP server`; an exact source-qualified ID is also accepted.
Ambiguous names require native source selection ([shared routing](../src/Kora/CustomizationWindowController.cs)).

1. For an existing entry, separately consent to **Read selected HTTPS entry**. This discloses only the selected supported entry into the private native editor, never to a model.
2. Enter the exact non-loopback HTTPS endpoint without user-info, whitespace, query or fragment (maximum 2,048 characters).
   Private IP literals are rejected, but Save does not resolve DNS or qualify connectivity.
   Optionally select **Include explicit type: http**. There are no command, environment, header, credential, account or authentication controls.
3. Select **Review and save**. Review the destination, exact entry name/identity, complete original and proposed entry, replacement diff and independent exclusion. **Continue to separate Save confirmation** is not Save consent.
4. Separately select **Save disabled and excluded**. Only verified atomic publication and exact readback report success.
   The entry is catalogued but disabled and independently excluded; no connection, discovery, account, tool or model operation occurs
   ([native operation](../src/Kora/CustomizationWindowController.Mcp.cs), [audited save](../src/Kora.Application/Definitions/CustomizationManagementService.cs)).

The host chooses only its roaming `Mcp\mcp.json`, never a supplied path. A missing managed file is created with a `servers` object.
Existing `servers` or `mcpServers` JSON/JSONC is writable only with that sole root property;
compatible BOM/comments/format and unrelated entry bytes remain unchanged.
The selected entry must contain only `url` and optional `"type":"http"`.
Other root fields or unsupported/sensitive selected fields report an error without disclosure, truncation or downgrade.
Personal-profile/additional files and importing/copying servers are not supported
([bounded parser](../src/Kora.Core/Customizations/McpDefinitionDocument.cs), [scoped writer](../src/Kora.Windows/ArtifactDiscovery/WindowsCustomizationStore.Mcp.cs)).

**Cancel** before final confirmation writes neither configuration nor use-control preferences.
Changed source, occupied destination, missing/replaced parent, path alias or unsafe file identity requires fresh inspection/review.
Comparison is optimistic, not content compare-and-swap; another application's last-instant same-name change can race atomic replacement.
An unconfirmed write stays excluded: inspect the actual current entry, retain/compare the draft and start a fresh review.
Kora does not claim rollback or automatically retry ([publication checks](../src/Kora.Windows/ArtifactDiscovery/WindowsCustomizationStore.Mcp.cs)).

Save does not reinclude a removed customization root. Separate existing current-source inclusion, discovery and use reviews are still required;
disabling/re-enabling cannot revive an earlier review or grant.
Authentication, delegated-account and connector qualification remain unavailable.
**Ask Kora**, foreground model-interpreted refinement and endpoint disclosure to a model are unavailable for this native-only editor
([authoring boundary](../src/Kora.Application/Definitions/CustomizationAuthoringService.cs),
[MCP qualification](../Design/Commands_Tools_And_Skills.md#first-https-read-only-execution-profile)).

## Disable or enable Kora use

Any listed artifact, including a built-in, can be **Disabled**. The choice
persists in device-local Kora preferences across restarts without changing
definition content or another application's settings.

Disabled artifacts remain inspectable and editable but are excluded from
ordinary Kora model context, options, capabilities and invocation. Pending
use reviews retire. Explicitly reviewed local authoring is the exception above;
it does not re-enable runtime use.
Disabling and re-enabling does not revive an earlier MCP connection, tool or
invocation review; start a new workflow.

**Enable** reviews the exact current source and removes only Kora's exclusion.
For file-backed catalogue entries it also records the reviewed primary revision
on this computer. New or externally changed entries need a fresh review;
restoring an observed retired version does not restore its previous permission.
It does not bypass source registration, instruction revision/applicability
review, skill compatibility, agent runtime qualification, MCP inclusion,
authentication or other existing admission. Newly edited User sources are not
automatically registered for Kora runtime use. An allowed artifact can therefore
still be unavailable; the window displays that distinction.

Use `disable example agent` or `enable example MCP server` to reach the same
source-qualified review. Files are retained when disabled.

Windows may roam definition content, not these local reviews. See
[Data storage and roaming profiles](data-storage.md) before editing on multiple
computers.
