# Inspect and recover a governed agent result

## Run and inspect the actual result

Use the existing [reviewed local-agent operation](tools-and-built-in-skills.md#run-one-reviewed-local-agent-profile): select the exact source, existing Active conversation and original task, review all guidance/tools/budgets, then separately confirm. Opening the review is not execution.

After a genuine final answer, open **Sessions**, select that original conversation and read its messages. Each retained observation has an exact named native link; choose it, or use `open observed result <presented name>` from the foreground conversation.
Duplicate tool names require the full presented message/result name or a native choice ([exact selection](../src/Kora/SessionsViewModel.Observations.cs)).

The reader shows the exact host result bytes and original observation time, not the model's explanation or newer runtime state. An observed unavailable runtime or `ToolLoopQualified=false` does not mean the runtime was enabled. Only the six reviewed read-only capabilities can contribute these records ([receipt policy](../src/Kora.Core/Conversation/RetainedLocalObservation.cs)).

Observation reading works for retained messages after restart or **Done**, but does not resume the session/task, renew retention, run an agent/tool or replace unsent text. Copy, speech, model context, export and clipboard authority remain unavailable for the observation.
Closing/private withdrawal retires its source; re-open through a fresh admitted conversation read ([exact reader](../src/Kora.Application/Hosting/SessionWorkspaceService.Conversation.cs)).

## Unavailable or denied output

The agent window shows the actual retained terminal outcome/reason and says **No assistant answer was produced** when there is no final answer. Inspect the original task in Sessions rather than treating a model or tool attempt as success.
Earlier callbacks from a denied, unavailable, cancelled, unknown or unconfirmed run are not presented as successful retained observations ([terminal policy](../src/Kora.Application/Dependencies/ModelTurnHost.cs)).

Legacy agent answers without retained callback items cannot supply exact tool evidence. No new query or reconstruction from assistant prose substitutes for missing historical evidence ([exact lookup](../src/Kora.Application/Hosting/SessionWorkspaceService.Conversation.cs)).

## Invocation audit outcomes

The content-free agent invocation audit preserves the original task's channel:
an activated-voice review remains **VoiceCommand** even when confirmed on screen;
native or typed input remains **LocalUser**. An unavailable runtime retains that
same channel without contacting a provider.

Requested and terminal records correlate to the exact retained request.
Successful, denied, cancelled and unknown terminals remain distinct; unavailable,
authentication, quota and timeout terminals are failed invocations, not answers.
Cancellation and uncertainty also retain their distinct causal activity outcomes.
An unconfirmed submission or failed terminal audit acknowledgement reports
**Unknown**, not a known failure or returned success. Inspect the original
conversation before starting any fresh operation; no automatic retry is made
([invocation owner](../src/Kora.Application/Agents/BoundedAgentWorkflow.cs)).
Audit contains no task, profile, instruction or answer text.

## Recover without replay

If the original private review window remains eligible, leave its source, task and conversation unchanged and choose **Read source and review exact invocation** again. Recheck the complete source/task/tool/budget review and separately confirm once.
Every declared instruction/skill consent remains independent. An expired, changed or withdrawn source or conversation requires fresh admission ([workflow](../src/Kora.Application/Agents/BoundedAgentWorkflow.cs)).

The new governed run creates a new request/task in the same admitted conversation; it does not rewrite the original failed task or reuse its consent. A consumed confirmation cannot retry automatically. Other unsent conversation text stays intact.
Stop/privacy closure joins owned execution before release; no late provider answer becomes successful recovery ([recovery regression](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.AgentResults.cs)).

Closing during a source read still waits for that read and the model-work join if a cancellation subscriber fails.
Shutdown reports the failure only after those joins; independent shutdown failures are reported together.
This does not authorize a retry or let a late source read produce an invocation or answer.

Unqualified/unavailable runtime remains unavailable. These deterministic implementation paths do not enable a model or waive permissions, source consent, retention or privacy ([delivery policy](../Design/Implementation_Roadmap.md#delivery-and-qualification-dependency-policy)).
