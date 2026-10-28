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

This is an on-screen status panel for the selected session, not a general reminder scheduler,
a watcher over every conversation or automatic speech.
