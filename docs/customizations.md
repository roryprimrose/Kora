# Instructions, skills, agents and MCP servers

Open **Tray > Kora Customizations** immediately below **Sessions**, type `show customizations`, or use the current
assistant-name prefix with activated voice, such as `Kora, show skills`.
The same native window serves all these routes.

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
It does not bypass source registration, instruction revision/applicability
review, skill compatibility, agent runtime qualification, MCP inclusion,
authentication or other existing admission. Newly edited User sources are not
automatically registered for Kora runtime use. An allowed artifact can therefore
still be unavailable; the window displays that distinction.

Use `disable example agent` or `enable example MCP server` to reach the same
source-qualified review. Files are retained when disabled.
