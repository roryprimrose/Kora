# Conversation notices

In an already-open **Sessions** window, refresh the selected session's work to see bounded, content-free notices for admitted conversation tasks: completed, failed/unavailable, Unknown, or an exact pending clarification ([source admission](../src/Kora.Application/Interaction/ConversationNotices/AuthorityLocalEventSource.ConversationNotices.cs)).

Select an exact notice and use **Review**, **Dismiss**, or **Defer**. These actions inspect or suppress content-free status without changing the conversation, work selection or focus ([native inspection](../src/Kora/SessionsViewModel.Events.cs)).

For an ordinary conversation clarification, **Review** shows the exact session, task, question and original expiry. Then deliberately select **Answer exact question** to activate only its existing canonical **Questions** window. Enter an answer and **Submit**, use the existing eligible question voice controls, or **Cancel** there ([response gesture](../src/Kora/ConversationNotices/SessionsViewModel.ConversationNotices.cs)).

The original task continues only after its current canonical answer has been accepted and revalidated. Opening is not an answer, approval, microphone or transmission grant. Claude retains separate complete-body review and consent for each physical send ([continuation](../src/Kora.Application/Hosting/ConversationQuestionContinuation.cs), [Claude reviews](../Design/Interaction_And_Sessions.md#explicit-hosted-conversational-clarification---2026-10-29)).

Closed, changed, expired, foreign, retired or no-longer-current questions cannot reopen from a notice. A competing question, foreground editor, prompt review or active dictation/reply must be resolved separately; Kora never opens the newest question as a substitute. Refresh and review again after a refusal. Dismiss and Defer are never answers or Cancel ([exact presenter](../src/Kora/QuestionWindowController.Conversation.cs), [source validation](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.QuestionFocus.cs)).

Observation and Review cannot answer, approve, retry, dispatch, speak, open a window or take focus. Only the deliberate response gesture activates the already-existing exact question; no duplicate question is created ([native response regressions](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.NoticeResponse.cs)).

## Read one exact terminal result

Select one current terminal conversation notice in the already-open **Sessions** management window, then deliberately choose **Read exact result**. This native-only gesture opens that task's complete retained **ASSISTANT** answer in the existing integrated **Details** reader, with **Historical** provenance and its original request/task/session identity ([exact route](../src/Kora/ConversationNotices/SessionsViewModel.ConversationNotices.cs)).

**Back to notice** returns focus to that exact still-selected notice in its existing window, not a similarly named conversation or newer message. No conversation-page read, composer replacement, speech, model invocation, continuation or Resume occurs. Reading a result does not review or answer a pending question. The existing Find, reader scale, inert rendering and blocked copy/export policy still apply ([origin navigation](../src/Kora/SessionInteractionView.axaml.cs), [retained reader](../src/Kora/DetailContentView.axaml.cs)).

No retained answer means explicit **Unavailable**, never an empty success or a body invented from a status or model claim. Inspect the separate typed **HOST** outcome in Work or ordinary history. Done, deleted, expired, revoked or changed notice sources also require fresh ordinary history inspection; reading history does not Resume a Done conversation ([canonical resolver](../src/Kora.Application/Hosting/SessionWorkspaceService.ConversationNoticeResult.cs)).

Unavailable native controls, a full reader limit, or failed window display/activation fail the read operation rather than recording completion. Unconfirmed private reader state is cleared; refresh and deliberately select the exact source before retrying. Successful content rendering alone does not confirm that the reader opened ([publication acknowledgment](../src/Kora/DetailWindowController.cs)).

Each body access is freshly resolved under the existing private read policy and rechecks the exact broker receipt/source. Losing the source cannot recover an old body from a cache, logs or the newest task. Notice metadata and completion-voice consent grant no body-disclosure authority by themselves ([broker admission](../src/Kora.Application/Interaction/LocalEventBroker.cs), [reader revalidation](../src/Kora/DetailWindowController.cs)).

Notices come from Kora's actual work records for this run, not from claims in
user or model text. A missing notice, an older record or an omitted page is
not proof that work succeeded.

Task notices expire thirty minutes after the original submission, not after
you read them. Questions may expire sooner. Privacy or protected-call changes,
loss of control, marking a session Done, deletion or restart can withdraw a
notice. Refresh never renews activity or authority; it does not extend a notice's lifetime.

If source validation takes time, **Dismiss** and **Defer** recheck the original
deadline before applying the choice. Defer starts from the fresh action time
and still ends no later than the original expiry. An expired notice or a clock
behind Kora's confirmed watermark cannot be acknowledged as current. Refresh
selected work and select again after the clock recovers; a held/unconfirmed
write is not a confirmed dismissal or deferral.

Kora spaces and limits routine notices to reduce interruptions. **Defer** hides
the current notice for up to fifteen minutes, never beyond its original expiry.
The one-minute category spacing starts from fresh authoritative delivery time,
including when refresh waited for the source. A clock rollback during that wait
holds delivery without spending its notice budget; refresh after the clock recovers.
Quiet hides routine completion notices, not Failure or Attention. Omitted work
is reported in the work panel's gap counts. Unknown is never inferred success.

## Optional completion voice notices for this run

In the already-open **Sessions** window, review the disclosure beside local events and choose **Enable completion voice notices for this run**. This separate choice starts Off and does not follow Speech only/VoiceOnly or saved listening consent ([native controls](../src/Kora/SessionsViewModel.CompletionNotices.cs)).

An explicitly enabled voice conversation must already be active this run. Startup/unlock-restored push-to-talk readiness does not count: explicitly disable then enable listening first, and keep the exact active conversation foreground. If the required voice/session/output profile is unavailable, the operation stays visual ([host admission](../src/Kora.Application/ViewModels/MainViewModel.CompletionNotices.cs)).

For a new successfully committed ordinary conversation task, Kora may say only **Your conversation task is complete.** once during a gap. It never reads titles or answers, announces failures/questions, changes focus or activates the microphone. The normal passive visual notice remains ([delivery owner](../src/Kora.Application/Interaction/ConversationCompletionVoiceNotices.cs)).

Choose **Turn completion voice notices Off** at any time. Quiet, capture, prompts, changed input and expired/stale sources suppress without replay. Call changes, listening disable, lock/privacy loss and restart remove the choice; unlock never restores it. Enabling excludes already-current completions. Queued is not delivered; the terminal distinguishes joined completion from cancellation/unconfirmed output ([lifecycle](../src/Kora.Application/Interaction/ConversationCompletionVoiceNotices.cs)).

An independently reviewed [speak-once call exception](responses-and-calls.md) applies only to its exact requested response. Completion notices never use or consume that exception and remain suppressed during protected calls.

This remains a bounded selected-session surface, not a general reminder scheduler or a watcher over every conversation. No live audio/provider qualification or new detector enablement is implied ([scope](../Design/Proactive_Interaction.md#run-consented-conversation-completion-speech)).

## Scheduled proactive speech quiet hours

In **Settings > Speech & audio**, enter ordered days, 24-hour start/end times and an explicit installed time-zone ID. Choose **Review weekly quiet hours**, read the interval, then **Confirm quiet hours**. The cog menu's **Reset** prepares Off for separate confirmation; it does not save immediately ([native controls](../src/Kora/SettingsWindow.axaml), [review owner](../src/Kora.Application/ViewModels/MainViewModel.QuietHours.cs)).

Exact typed commands use the same review and save operation. Activated voice requires the current assistant-name prefix on both requests, existing consent/readiness and clear call/channel admission ([command grammar](../src/Kora.Application/Configuration/QuietHoursCommand.cs), [host admission](../src/Kora.Application/ViewModels/MainViewModel.cs)):

```text
get notices.quiet-hours
set notices.quiet-hours to weekly Mon,Tue,Wed,Thu,Fri 09:00 17:00 UTC
confirm notices.quiet-hours
set notices.quiet-hours to Off
confirm notices.quiet-hours
reset notices.quiet-hours
confirm notices.quiet-hours
cancel notices.quiet-hours
```

Use distinct days in `Mon,Tue,Wed,Thu,Fri,Sat,Sun` order. A zone such as `Australia/Sydney` replaces `UTC` explicitly; changing the Windows local zone does not change this choice. The starting day owns an overnight interval, start is inclusive and end is exclusive. Equal times are rejected. Repeated DST hours stay quiet when their local clock is inside the interval; skipped times create no replay ([schedule semantics](../src/Kora.Core/Configuration/QuietHoursSchedule.cs)).

Off is the default. One confirmed device-local schedule survives restart, but it never enables completion-speech consent or listening. Quiet hours affect only otherwise-admitted proactive completion speech, checked again at synthesis and playback start. Visual completion notices and their fatigue receipts remain; suppressed speech is consumed and cannot replay when quiet ends ([completion admission](../src/Kora.Application/Interaction/ConversationCompletionVoiceNotices.cs), [platform boundaries](../src/Kora.Windows/Audio/WindowsTextToSpeechService.cs)).

The separate run-only **Quiet routine notices** choice still hides routine rows; scheduled quiet hours never toggle it. Ordinary requested answers, mandatory questions/approvals and existing call/privacy restrictions are unchanged ([independent policies](../Design/Proactive_Interaction.md#scheduled-proactive-speech-quiet-hours)).

Reviews expire after sixty seconds and cannot survive changed original channel, settings visibility, private ownership, assistant name, consent or call policy. Malformed/unreadable saved intent or unconfirmed writes hold proactive speech unavailable rather than selecting Off. Inspect the displayed saved/effective status and audit evidence, explicitly repair the preference, then inspect again; there is no automatic retry ([configuration and recovery](../src/Kora.Application/Configuration/QuietHoursConfigurationService.cs), [atomic persistence](../src/Kora.Application/Configuration/LocalQuietHoursPreferences.cs)).
