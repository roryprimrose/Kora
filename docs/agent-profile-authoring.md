# Author a new owned agent profile

An agent profile describes a role, allowed tools and limits for one task.
This editor saves that description; it does not start an agent.
Manual authoring works without AI. Running the saved profile separately needs
a local model approved by the release, which is **unavailable in this build**.

Use **agent author** in private typed input, or say it with the current assistant
name while voice commands are eligible. This opens a native editor, not a model
request. Create an exact new lowercase owned ID using **agent author create example**.
The ID contains at most 64 ASCII letters, digits and hyphens; no paths or URLs.

Enter the complete source in the editor, or use **agent author source** followed by
the complete source. Kora keeps the original typed/activated-voice channel. If you started by voice,
later on-screen input does not bypass voice consent, call protection or expiry.

## Describe and review a new tool-free agent

Instead of supplying source, type **agent author draft example: Explain only the text I supply**.
The current assistant-name activated-voice prefix addresses the same complete operation.
For native-only entry, open **Tray > Kora Customizations**, choose **Agents** and **Kora**,
enter a new ID and description, then select **Add**. It hands that exact request to the same agent editor,
not the generic source writer ([native Add handoff](../src/Kora/CustomizationWindowController.cs)).
Alternatively create a new volatile draft, enter **Original agent description or refinement**, and select
**Ask Kora: review local authoring disclosure** in the same editor ([shared native owner](../src/Kora/AgentProfileEditorController.Proposal.cs)).

1. Review the exact description, complete current draft (empty initially), fixed local text-only limits and create-only destination.
   The destination is displayed but is not sent to the model. Select **Generate local agent proposal** or **Cancel**.
2. One admitted local tool-free authoring turn receives only that unchanged request and complete draft.
   Its only output fields are a single-line description and complete guidance body. Kora supplies the exact ID,
   v1 format, local text input/output, no references/tools, zero calls, one round, 16000 input bytes,
   2000 output bytes and a 15000ms deadline. The model cannot select a root, worker, dependency, grant or activation
   ([generator and strict proposal parser](../src/Kora.Application/Definitions/CustomizationAuthoringService.cs),
   [host-owned profile construction](../src/Kora.Core/Agents/OwnedAgentProfileDocument.cs)).
3. Compare the complete **BEFORE** and **AFTER** sources and exact destination.
   **Use proposed draft** stages the validated full source in this editor only; **Reject proposal** keeps the draft.
   Text is inert untrusted guidance, not a harmlessness guarantee. Nothing is saved, enabled, hosted or invoked
   ([proposal adoption](../src/Kora/AgentProfileEditorController.Proposal.cs)).
4. Edit the staged source directly, or enter another original refinement using the request field or
   **agent author describe Make the guidance shorter.**, then **agent author ask**.
   Each turn needs fresh disclosure and complete proposal acceptance. Existing tool-free v1 metadata and limits
   remain host-owned; manual v4 and the separate registered-source editor remain independent
   ([commands](../src/Kora.Application/Agents/AgentProfileAuthoringCommand.cs)).
5. Use **agent author save as new** and the two separate native save controls described below.
   The reviewed generated destination must still be the same absent destination; ordinary Save never substitutes a root,
   overwrites an occupied file or falls back to another proposal ([save owner](../src/Kora/AgentProfileEditorController.cs)).

**Cancel proposal or save review** cancels pending disclosure, generation or review without discarding independent manual edits.
Source/request/name edits, catalogue/control changes, privacy/call/ownership loss, original-channel expiry and the two-minute
authoring deadline retire pending work. Late output cannot replace a newer draft; cancellation-ignoring model work remains owned
until it actually joins. Description changes after adoption require a fresh proposal before saving that generated draft
([lifetime fences](../src/Kora/AgentProfileEditorController.Proposal.cs)).

An unqualified local model reports unavailable and retains eligible input; there is no hosted fallback.
No authoring text is added to conversation history, diagnostics or audit. Manual source authoring remains available without AI
([shared local authoring service](../src/Kora.Application/Definitions/CustomizationAuthoringService.cs)).

## Manual source and separate save

For example:

```markdown
---
name: example
description: Explain user text without external resources
kora-agent-format: 1
runtime: local
input: text
output: text
references: none
tools: none
maximum-calls: 0
maximum-rounds: 1
maximum-input-bytes: 16000
maximum-output-bytes: 2000
deadline-ms: 1000
---
Explain only the supplied text. Do not request external resources.
```

**agent author validate** checks the complete v1 or v4 format, UTF-8 size and budgets without writing, resolving references, enabling, selecting or running anything. V1 authoring still requires references none, tools none and zero calls.
V4 authoring adds the combined owned-instruction/observation-tool declaration below; v2/v3 authoring, scripts and hooks remain unsupported ([owned document validation](../src/Kora.Core/Agents/OwnedAgentProfileDocument.cs)).

For a role that follows owned guidance and observes the application version in the same run:

```markdown
---
name: example
description: Explain the observed version using exact owned guidance
kora-agent-format: 4
runtime: local
input: text
output: text
references: disk.kora-profile.instruction.example
tools: application.get_version
maximum-calls: 1
maximum-rounds: 2
maximum-input-bytes: 32768
maximum-output-bytes: 4096
deadline-ms: 15000
---
Follow the separately reviewed guidance. Observe the version only if needed for the original request.
```

Replace the reference with your exact independently enabled Kora-owned instruction ID. Saving is an inert new original, not resolution consent or enablement.
Later [run review](tools-and-built-in-skills.md#run-one-reviewed-local-agent-profile) requires separate source resolution, complete guidance/tools/cumulative-budget review, and exact single-use invocation confirmation ([workflow](../src/Kora.Application/Agents/BoundedAgentWorkflow.cs)).

**agent author save as new** prepares a review showing the **complete proposed
source**, exact new destination and inert status. Review is not consent to write.
Use **Confirm Save as new...**, then the separate native **Save as new** confirmation.
There is no affirmative default, typed confirmation or voice confirmation for the
filesystem mutation. **agent author cancel review** cancels it without writing.
Editing the source/name or changing privacy, call, ownership or original authority
invalidates the review.

Kora creates a new `.agent.md` file in its private agent directory. It never
replaces an existing file: choose a different ID or cancel if the name is taken.
This editor offers no overwrite, delete or rename. If saving is uncertain,
inspect the saved originals before another operation; uncertainty is
**not success or rollback**, and Kora does not retry automatically.

**agent author list** refreshes owned originals. Select one and use **Reopen
selected original read-only**, or **agent author inspect example**. Saving does not
select, enable or invoke the profile, refresh executable discovery or grant tools,
providers, accounts or permissions. Later execution requires the existing separate
explicit source/invocation review and its own readiness and consent.

Drafts stay in memory only, not in conversation composer storage or disk autosave.
Closing with changes offers **Save / Discard / Cancel**. Cancel keeps the editor
and app open; Save still needs native confirmation. Privacy closure clears the
private display and withdraws consent. Kora waits for outstanding storage work
to finish even if it ignores cancellation, before reporting release.
Diagnostics and security records do not
include your profile text or destination.
