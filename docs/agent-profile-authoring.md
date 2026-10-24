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

**agent author validate** checks the complete existing v1 format, UTF-8 size and
budgets without writing, selecting or running anything. Requested budgets remain
subject to the existing host clamps. Only local text input/output, references none,
tools none and zero calls are supported here. V2 references, scripts and hooks
are not authoring options.

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
