# Conversation notices

In an already-open **Sessions** window, refresh the selected session's work to see bounded, content-free notices for admitted conversation tasks: completed, failed/unavailable, Unknown, or an exact pending clarification ([source admission](../src/Kora.Application/Interaction/ConversationNotices/AuthorityLocalEventSource.ConversationNotices.cs)).

Select an exact notice and use **Review**, **Dismiss**, or **Defer**. These actions inspect or suppress content-free status without changing the conversation, work selection or focus ([native inspection](../src/Kora/SessionsViewModel.Events.cs)).

For an ordinary conversation clarification, **Review** shows the exact session, task, question and original expiry. Then deliberately select **Answer exact question** to activate only its existing canonical **Questions** window. Enter an answer and **Submit**, use the existing eligible question voice controls, or **Cancel** there ([response gesture](../src/Kora/ConversationNotices/SessionsViewModel.ConversationNotices.cs)).

The original task continues only after its current canonical answer has been accepted and revalidated. Opening is not an answer, approval, microphone or transmission grant. Claude retains separate complete-body review and consent for each physical send ([continuation](../src/Kora.Application/Hosting/ConversationQuestionContinuation.cs), [Claude reviews](../Design/Interaction_And_Sessions.md#explicit-hosted-conversational-clarification---2026-10-29)).

Closed, changed, expired, foreign, retired or no-longer-current questions cannot reopen from a notice. A competing question, foreground editor, prompt review or active dictation/reply must be resolved separately; Kora never opens the newest question as a substitute. Refresh and review again after a refusal. Dismiss and Defer are never answers or Cancel ([exact presenter](../src/Kora/QuestionWindowController.Conversation.cs), [source validation](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.QuestionFocus.cs)).

Observation and Review cannot answer, approve, retry, dispatch, speak, open a window or take focus. Only the deliberate response gesture activates the already-existing exact question; no duplicate question is created ([native response regressions](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.NoticeResponse.cs)).

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
