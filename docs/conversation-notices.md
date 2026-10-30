# Conversation notices

In an already-open **Sessions** window, refresh the selected session's work to see bounded, content-free notices for admitted conversation tasks: completed, failed/unavailable, Unknown, or an exact pending clarification ([source admission](../src/Kora.Application/Interaction/ConversationNotices/AuthorityLocalEventSource.ConversationNotices.cs)).

Select an exact notice and use **Review**, **Dismiss**, or **Defer**. Review inspects that exact task's content-free status without changing the conversation or work selection. Answer a clarification separately in **Questions**; a notice cannot answer, approve, retry, dispatch, speak, open a window or take focus ([native inspection](../src/Kora/ConversationNotices/SessionsViewModel.ConversationNotices.cs)).

Notices come from Kora's actual work records for this run, not from claims in
user or model text. A missing notice, an older record or an omitted page is
not proof that work succeeded.

Task notices expire thirty minutes after the original submission, not after
you read them. Questions may expire sooner. Privacy or protected-call changes,
loss of control, marking a session Done, deletion or restart can withdraw a
notice. Refresh never renews activity or authority; it does not extend a notice's lifetime.

Kora spaces and limits routine notices to reduce interruptions. **Defer** hides
the current notice for up to fifteen minutes, never beyond its original expiry.
Quiet hides routine completion notices, not Failure or Attention. Omitted work
is reported in the work panel's gap counts. Unknown is never inferred success.

## Optional completion voice notices for this run

In the already-open **Sessions** window, review the disclosure beside local events and choose **Enable completion voice notices for this run**. This separate choice starts Off and does not follow Speech only/VoiceOnly or saved listening consent ([native controls](../src/Kora/SessionsViewModel.CompletionNotices.cs)).

An explicitly enabled voice conversation must already be active this run. Startup/unlock-restored push-to-talk readiness does not count: explicitly disable then enable listening first, and keep the exact active conversation foreground. If the required voice/session/output profile is unavailable, the operation stays visual ([host admission](../src/Kora.Application/ViewModels/MainViewModel.CompletionNotices.cs)).

For a new successfully committed ordinary conversation task, Kora may say only **Your conversation task is complete.** once during a gap. It never reads titles or answers, announces failures/questions, changes focus or activates the microphone. The normal passive visual notice remains ([delivery owner](../src/Kora.Application/Interaction/ConversationCompletionVoiceNotices.cs)).

Choose **Turn completion voice notices Off** at any time. Quiet, capture, prompts, changed input and expired/stale sources suppress without replay. Call changes, listening disable, lock/privacy loss and restart remove the choice; unlock never restores it. Enabling excludes already-current completions. Queued is not delivered; the terminal distinguishes joined completion from cancellation/unconfirmed output ([lifecycle](../src/Kora.Application/Interaction/ConversationCompletionVoiceNotices.cs)).

An independently reviewed [speak-once call exception](responses-and-calls.md) applies only to its exact requested response. Completion notices never use or consume that exception and remain suppressed during protected calls.

This remains a bounded selected-session surface, not a general reminder scheduler or a watcher over every conversation. No live audio/provider qualification or new detector enablement is implied ([scope](../Design/Proactive_Interaction.md#run-consented-conversation-completion-speech)).
