# Review history, an instruction and session memories for one answer

This explicit **Sessions** operation combines selected sources for one local-only,
tool-free conversation answer. It never assembles context or recalls memories
automatically. Ordinary **Submit exact message** and all separate source reviews
remain unchanged.

## Answer the original question after a model history advisory

An ordinary retained ASSISTANT answer can contain exactly
`{"version":1,"advisory":"HISTORYREVIEW"}`. This bounded, versioned advisory is
untrusted data, not permission to read history, select messages or replace your
question. Merely receiving or selecting it reads no additional history and
starts no model turn.

In **Sessions**, explicitly read the conversation, select that retained answer
and choose **Review context for selected model advisory**. The private window
offers only already observed whole USER/ASSISTANT messages strictly before the
advisory turn. Nothing is preselected. If the required earlier messages are not
on the observed page, cancel and explicitly read the intended sources; Kora
does not silently fetch or fold more history.

Select your subset, then choose **Review complete selection and original
question**. Inspect every complete selected source and the unchanged original
USER question, its originating channel, and the LOCAL ONLY, NO TOOLS destination.
The advisory itself is not included as evidence. Your unrelated unsent composer
is neither used nor changed. The complete envelope still has the shared
16-item / 32768-UTF-8-byte limit; nothing is truncated.

Separately choose **Confirm ONE local-only tool-free answer**. This consumes one
source-bound confirmation and submits the host's byte-exact original question in
the same Active conversation, preserving LocalUi or ActivatedVoice origin.
The actual shared local model lifecycle retains a genuine ASSISTANT answer or a
truthful HOST unavailable/failure outcome. Opening the window and reviewing are
not completion. Existing qualification remains authoritative; this feature
does not enable an unavailable provider or fall back to a hosted model.

The advisory binding expires after 60 seconds; reviewing again does not renew
it. Changed/deleted advisory or selected sources, session generation/metadata,
input, answer/page selection, channel authority, privacy, ownership or
presentation invalidate stale use. Closing cancels consent without retracting
an already retained request. Ignoring outstanding model/storage work remains
owned until physically joined; late results cannot renew consent. There is no
spoken ordinal shortcut, automatic speech, memory creation, source-body copy
into new history, raw-content diagnostics, tool use or hosted disclosure.
Manual history follow-ups, exact-answer replies and per-session unsent input
remain separate ([strict advisory](../src/Kora.Core/Conversation/ModelHistoryContextProposal.cs),
[host binding](../src/Kora.Application/Hosting/SessionConversationService.ModelHistoryContext.cs),
[native interaction](../src/Kora/ModelHistoryContext/ModelHistoryContextWindow.axaml)).

## Reply deliberately to one exact assistant answer

In the native conversation or Sessions window, explicitly read the conversation, then choose **Reply to this answer** on the retained ASSISTANT answer you mean. Reply mode shows that exact complete answer and **Cancel reply**. Write your new follow-up (for example, `expand the second point`) in the session's existing memory-only composer; choosing Reply never pastes old text into your input ([native reply operation](../src/Kora/ReviewedContext/SessionsViewModel.AssistantReply.cs)).

Choose **Review exact reply**, or use the ordinary Send/Submit button while Reply mode is active. Inspect the complete earlier ASSISTANT evidence separately from the unchanged NEW USER request and the complete serialized budget. Then choose **Confirm ONE local-only reply** separately. This single-use review expires after **60 seconds**; each further reply needs its own review ([source-bound review](../src/Kora.Application/Hosting/ConversationHistoryReview.cs)).

Only that one exact answer is included, not its original USER message, another answer, memories, instructions or attachments. The same Active conversation retains your unchanged new USER message and a genuine ASSISTANT answer if local model admission succeeds, otherwise an explicit HOST outcome. Source text is inert LocalOnly evidence, never approval, tool instructions or system authority ([shared workflow](../src/Kora.Application/Hosting/ReviewedContext/MixedContextWorkflow.cs), [context policy](../src/Kora.Core/Conversation/ReviewedContext/MixedConversationContext.cs)).

Editing the new follow-up retires its review. Changed/deleted/expired sources, page or session selection changes, Done state, privacy and ownership changes refuse stale sends without silently sending the follow-up alone. Switching away preserves unsent text under the existing per-session policy, but returning requires choosing and reviewing the source again or explicitly cancelling Reply. **Cancel reply** sends nothing, reads no new history and leaves the follow-up unchanged; privacy clearing remains authoritative ([reply state and fences](../src/Kora/ReviewedContext/SessionsViewModel.AssistantReply.cs), [unsent policy](../src/Kora/SessionsViewModel.Unsent.cs)).

This native operation has no spoken/typed ordinal Reply command. Existing voice dictation, history reads, Details and original-USER reuse remain separate. No implicit Copilot/Claude routing, hosted egress, tool use, grants, source promotion, future history consent or draft autosave is added. Runtime availability still depends on existing qualified local admission; deterministic fake-model tests do not enable a provider ([interaction evidence](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.AssistantReply.cs)).

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
