## Use an owned instruction for one conversation request

In **Sessions**, select an Active conversation and write the original request. Expand **Use one enabled owned instruction for one local request**, refresh the available choices, select one, then choose **Review complete instruction and request**.

Review shows the complete pinned native source, original request and one-request scope; it does not submit or enable anything ([native controls](../src/Kora/SessionsWindow.axaml)).

Choose **Submit this one reviewed local request** separately. The ordinary **Submit exact message** action ignores the instruction selection.

A successful local turn retains the original USER request and genuine ASSISTANT answer separately; an unavailable qualified runtime reports an explicit HOST failure without a substitute answer ([instruction submission](../src/Kora.Application/Hosting/SessionConversationService.Instructions.cs), [retained execution](../src/Kora.Application/Hosting/SessionConversationService.Execution.cs)).

Only an already reviewed/enabled exact Kora-owned native instruction is eligible. Review expires after two minutes and is single-use. Editing the request, changing session, changing the source, losing access, or withdrawing use invalidates pending review; Kora never substitutes another revision.

An explicit use-disable permits an already captured answer to finish under the existing definition contract, but never admits another request or continuation. Access loss and source revocation are not use-disable ([review broker](../src/Kora.Application/Hosting/ScopedInstructionUseBroker.cs)).

Instruction text is lower-trust guidance under host policy, not policy, consent, identity or tool authority. This route admits no tools, hosted transfer, history, memory, attachments or persistent instruction applicability.

Closing private presentation clears its selection and review; the next ordinary request remains context-free ([review state](../src/Kora/SessionsViewModel.InstructionContext.cs)).
