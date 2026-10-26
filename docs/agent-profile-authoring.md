# Author a new owned agent profile

Use **agent author** in private typed input, or say it with the current assistant
name while voice commands are eligible. This opens a native editor, not a model
request. Create an exact new lowercase owned ID using **agent author create example**.
The ID contains at most 64 ASCII letters, digits and hyphens; no paths or URLs.

Enter the complete source in the editor, or use **agent author source** followed by
the complete source. Source entry preserves the original typed/activated-voice
authority; later native input cannot renew stale voice authority.

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

The host creates only a new `.agent.md` original in its private `OwnedAgents-v1`
directory. It privately stages and flushes exact bytes, atomically publishes
without replacement and verifies pinned durable identity and bytes. A collision
requires a different new ID or cancellation; no overwrite, delete or rename
is provided. An uncertain publication is **not success or rollback**: inspect
the exact destination before another operation. There is no automatic retry.

**agent author list** refreshes owned originals. Select one and use **Reopen
selected original read-only**, or **agent author inspect example**. Saving does not
select, enable or invoke the profile, refresh executable discovery or grant tools,
providers, accounts or permissions. Later execution requires the existing separate
explicit source/invocation review and its own readiness and consent.

Drafts stay in memory only, not in conversation composer storage or disk autosave.
Dirty close offers **Save / Discard / Cancel**; Cancel keeps the editor/app open
and aborts shutdown. Save still requires exact native confirmation. Privacy closure
clears private presentation, withdraws consent and joins outstanding storage work,
including work that ignores cancellation. Audit and diagnostics record content-free
typed outcomes, never the source or destination text.
