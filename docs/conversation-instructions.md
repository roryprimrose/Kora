# Use an owned instruction for one conversation request

An instruction supplies guidance, not access or permission. This operation
applies it to **one** request; [session instructions](session-instructions.md)
are a separate choice. **AI answers are unavailable in this build** even after
you review the instruction.

In **Sessions**, select an Active conversation and write the original request. Expand **Use one enabled owned instruction for one local request**, refresh the available choices, select one, then choose **Review complete instruction and request**.

Review shows the complete pinned native source, original request and one-request scope; it does not submit or enable anything ([native controls](../src/Kora/SessionsWindow.axaml)).

Choose **Submit this one reviewed local request** separately. The ordinary **Submit exact message** action ignores the instruction selection.

A successful local turn retains the original USER request and genuine ASSISTANT answer separately; an unavailable qualified runtime reports an explicit HOST failure without a substitute answer ([instruction submission](../src/Kora.Application/Hosting/SessionConversationService.Instructions.cs), [retained execution](../src/Kora.Application/Hosting/SessionConversationService.Execution.cs)).

Only an already reviewed/enabled exact Kora-owned native instruction is eligible. Review expires after two minutes and is single-use. Editing the request, changing session, changing the source, losing access, or withdrawing use invalidates pending review; Kora never substitutes another revision.

An explicit use-disable permits an already captured answer to finish under the existing definition contract, but never admits another request or continuation. Access loss and source revocation are not use-disable ([review broker](../src/Kora.Application/Hosting/ScopedInstructionUseBroker.cs)).

Instruction text is lower-trust guidance under host policy, not policy, consent, identity or tool authority. This route admits no tools, hosted transfer, history, memory, attachments or persistent instruction applicability.

Closing private presentation clears its selection and review; the next ordinary request remains context-free ([review state](../src/Kora/SessionsViewModel.InstructionContext.cs)).

## Typed and activated-voice requests

The same one-shot operation is also available through an original typed command or current-name activated voice:

```text
instructions request review "brief-style" in "Project planning" -- Summarize the next three steps.
```

Use the readable name of an existing enabled owned instruction and an existing **Active** conversation. Each name must match uniquely (ignoring case); ambiguous names are refused, not guessed. Rename an ambiguous conversation in Sessions first. Names containing a quote use two quotes inside the quoted name. No path, selected-window fallback, new conversation or automatic enablement is used.

The private review window shows the **complete pinned instruction**, **complete original request**, exact named target, original typed/voice channel, local-only one-request scope and expiry. Internal source/session identities and revisions are host bindings; you do not need to recite them.

After reviewing, separately say/type **instructions request confirm** (voice: **Kora, instructions request confirm**, using your current assistant name), or choose **Submit this one reviewed local request** in that window. Confirmation binds only the host's current pending review and cannot change the original channel. A voice confirmation queued before that review was presented cannot confirm it.

Use **instructions request cancel**, **instructions request stop**, or close the window to retire the review and join any owned work, including a late cancellation-ignoring completion. Stop is not rollback: inspect the original conversation for any already retained USER request and HOST/task terminal. No automatic retry or replacement answer is produced. Exit and restart join owned work before proceeding.

Starting another review requires cancelling the previous one. Privacy, call protection, ownership or original voice admission changes retire the pending operation; fresh UI confirmation cannot renew it. Existing Sessions controls and ordinary Submit are unchanged ([shared workflow](../src/Kora.Application/Hosting/InstructionUseWorkflow.cs)).
