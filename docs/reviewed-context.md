# Review history, an instruction and session memories for one answer

This explicit **Sessions** operation combines selected sources for one local-only,
tool-free conversation answer. It never assembles context or recalls memories
automatically. Ordinary **Submit exact message** and all separate source reviews
remain unchanged.

## Select and inspect

1. Select the exact **Active** session. Choose **Read exact conversation** and
   explicitly **List** its admitted session memories first.
2. Choose **Review history, instruction and session memory together**. The new
   private window offers only the already observed whole USER/ASSISTANT messages,
   observed session-memory metadata and available enabled owned instructions.
   Empty lists require a fresh explicit source read and reopening the window.
3. Select whole earlier messages, exactly one enabled instruction and at least
   one admitted, enabled **SESSION** memory. Enter the complete new request.
   Its whitespace and text are preserved unchanged; unsent text stays in memory.
4. Choose **Review complete sources and request**. Inspect every complete source,
   original history roles/order, instruction guidance, memory classifications and
   exact session scope, and the unchanged new request. Review shows the total
   serialized UTF-8 input budget, including host policy and envelope overhead.

Selection and review do not call a model or retain a new conversation request.
The complete envelope admits at most **16 evidence items** and **32768 UTF-8
bytes** across all sources together. Nothing is silently dropped or shortened.
History keeps its original chronological USER/ASSISTANT order, followed by the
instruction and explicitly selected memories.

## Confirm one answer or cancel

Choose **Confirm ONE local-only tool-free answer** separately. Consent is
single-use and expires after two minutes. Only a genuinely successful final
provider answer is retained as ASSISTANT, separately from the original USER
request. Unavailable or failed qualification produces an explicit HOST outcome,
not a substitute answer. Production provider and hardware qualification remains
unavailable; deterministic fake-transport tests are implementation evidence only.

Choose **Cancel review or use**, or close the window, to retire the private
review. Changes to input/selection, source revision, instruction enablement,
memory eligibility, session generation, original channel, privacy or ownership
invalidate use. Confirmation cannot renew stale authority or substitute sources.
An already retained request is not rolled back: inspect its HOST/task receipts.
Cancellation is not proof of resource release; the admitted host retains ownership
until a late or cancellation-ignoring provider actually becomes quiescent.

## Boundaries

All selected text remains lower-trust evidence or instruction guidance under
host policy, never consent, grants, execution authority or policy itself.
This operation admits no tools, hosted transmission, attachments, automatic
context assembly/recall, source promotion, model memory writes or future
instruction applicability. It uses existing SESSION memories independently of
device-profile memory and model-proposed-memory workflows.

Selected source bodies are not copied into newly retained conversation history,
logs or audit payloads. The new original request and genuine answer/HOST outcome
follow conversation retention; existing memory and definition retention remain
independent.

See the [context policy](../src/Kora.Core/Conversation/ReviewedContext/MixedConversationContext.cs),
[shared source workflow](../src/Kora.Application/Hosting/ReviewedContext/MixedContextWorkflow.cs)
and [private native operation](../src/Kora/ReviewedContext/SessionsViewModel.ReviewedContext.cs).
