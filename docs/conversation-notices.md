# Conversation notices

In an already-open **Sessions** window, refresh the selected session's work to see bounded, content-free notices for admitted conversation tasks: completed, failed/unavailable, Unknown, or an exact pending clarification ([source admission](../src/Kora.Application/Interaction/ConversationNotices/AuthorityLocalEventSource.ConversationNotices.cs)).

Select an exact notice and use **Review**, **Dismiss**, or **Defer**. Review inspects that exact task's content-free status without changing the conversation or work selection. Answer a clarification separately in **Questions**; a notice cannot answer, approve, retry, dispatch, speak, open a window or take focus ([native inspection](../src/Kora/ConversationNotices/SessionsViewModel.ConversationNotices.cs)).

Only the authoritative, audited current-run conversation source can create these notices. User/model text, old-run records, unadmitted submissions, absent records and omitted pages are not successful task receipts. Original request, generation, revision, terminal outcome/reason and exact clarification lineage are checked under the existing storage lease ([reader](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.ConversationNotices.cs)).

Task notices expire thirty minutes after the original submission, not thirty minutes after observation. Clarifications keep their own earlier expiry. Privacy, protected-call or host-control epoch changes retire earlier conversation sources; Done, deletion, restart and changed generation also withdraw them. Refresh never renews activity or authority ([source admission](../src/Kora.Application/Interaction/ConversationNotices/AuthorityLocalEventSource.ConversationNotices.cs)).

The existing shared broker applies quiet mode, category spacing and hourly fatigue budgets, coalescing, exact-revision dismissal, fifteen-minute deferral capped at source expiry, suppression storage and no-replay rules. Omitted work remains visible in the work snapshot's gap counts. Unknown is never inferred success. Quiet hides routine completion notices, not Failure or Attention ([broker contract](../Design/Proactive_Interaction.md#delivered-r18-trusted-local-visual-broker---2026-10-09)).

This is selected-session visual inspection, not an ambient multi-session observer, general reminder scheduler, retired diagnostic scheduling control, proactive speech or installed/native qualification claim ([availability boundaries](../Design/Proactive_Interaction.md#delivery-policy)).
