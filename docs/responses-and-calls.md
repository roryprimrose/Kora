# Responses and manual call mode

Choose [text, speech or both](#response-modes), learn to
[stop speech](#interrupting-spoken-responses), or set
[manual call protection](#manual-call-mode-and-call-settings).
Long results can have [a brief and full Details](#brief-answers-and-full-details).

**AI answers are unavailable in this build.** Model-dependent reading and answer
workflows below describe their reviews and limits; their controls do not enable
a model. Built-in command responses and local speech still work.

## Inspect context submitted for a retained answer

Select one exact retained ASSISTANT answer in Sessions or the conversation pane, then choose **Context submitted for this answer**. The private native Details view shows historical host metadata, not a new answer or source-body recall ([native route](../src/Kora/ConversationPresentation.axaml), [inspection owner](../src/Kora.Application/Hosting/SessionWorkspaceService.Conversation.cs)).

The supported subset is one explicitly reviewed, same-session history request that completed successfully through a qualified local adapter. It shows original host-admitted local model/reasoning/context option IDs, locality, handoff time, complete canonical byte count and ordered source kinds; account/revision bindings stay internal ([record](../src/Kora.Core/Conversation/SubmittedAnswerContext.cs)).

The evidence means **canonical bytes handed to the admitted adapter**, not physical provider delivery, processing, reasoning or proof that the model used a source. Merely reviewing/selecting input and a model claiming a source prove neither. Memory, instruction, attachment, tool and hosted context are not supported by this recorder; legacy/unsupported answers explicitly say **Unknown / not recorded** ([projection](../src/Kora.Core/Conversation/AnswerContextInspection.cs)).

Current source-row metadata may report the exact revision retained, or missing/changed and unavailable. That does not validate or admit its body. Opening a body requires a separate existing fresh source-admission operation; this inspector has no body-opening, refresh, automatic transfer or fallback action ([metadata-only reader](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.AnswerContext.cs)).

Use **Back to conversation** in the conversation pane to return to the exact originating answer; a separate Details window can be closed to return to its owner. No model call, source-body read, speech, clipboard copy, grant, history write or retention renewal occurs. Source/private/session changes retire stale presentation. The metadata follows its answer's existing session deletion and retention, not audit retention ([native return](../src/Kora/SessionInteractionView.axaml.cs), [passive inspection](../src/Kora.Application/Hosting/SessionWorkspaceService.Conversation.cs)).

## Ask another question about the same reviewed file snapshot

For an already admitted single-file preview, `answer file` focuses its native question control; it does not read a file or invoke a model.
**Use this exact snapshot once to answer locally** consumes one explicit request, even if the question is invalid, denied, cancelled or the runtime is unavailable.

After it finishes, choose **Review another question on this exact snapshot** to clear the old answer/citations and review a fresh one-shot local request. Check the source and question, then separately choose **Use this exact snapshot once to answer locally**. Recovery never rereads the file, refreshes the snapshot, reuses a consumed request or automatically retries. Source-use screening and qualified runtime admission still apply each time; these controls do not enable AI answers.

**Cancel file answer and clear claims** or editing a pending question cancels that request and suppresses late claims. The view says that no answer is claimed and keeps fresh review unavailable until actual work has finished. Revocation, source replacement, privacy loss or closing retires the view; select and review a source again rather than using an old control. A successful answer's **Show exact answer citation** still opens only its original immutable source span, not the current file at that path. Nothing in this operation saves the source, question or answer to conversation history.

## Answer from a retained session attachment

In **Sessions**, select the exact Active conversation and choose **Answer from this session's retained attachment**. The existing native source viewer opens only that session's already retained immutable text/Markdown copy; it never opens a picker or the original live file ([native route](../src/Kora/SessionsViewModel.AttachmentAnswer.cs)).

Enter a bounded lexical question, choose **Review exact evidence for this question**, and inspect the complete admitted excerpts and question. Separately choose **Consent once: answer locally and retain in this conversation**. Saving or reading an attachment is not permission to send it to a model; source-use policy and qualified local runtime independently admit the turn ([review workflow](../src/Kora.Application/Hosting/SessionAttachmentAnswerWorkflow.cs)).

This explicit operation uses one local-only, tool-free turn. It does not add history, memory, other attachments, folders, tools or hosted fallback. Changed/deleted/replaced sources, revoked consent, incomplete lexical selection and over-budget input are rejected rather than silently truncated. Inspection does not renew retention or Keep ([source and budget fences](../src/Kora.Application/Hosting/SessionAttachmentAnswerWorkflow.cs)).

The host binds reviewed evidence to the exact submitted request through the [shared reviewed-context lifecycle](../src/Kora.Application/Hosting/SessionConversationService.cs). Citation resolution runs before the assistant response is committed, after the owning turn's terminal identity and cancellation checks ([execution boundary](../src/Kora.Application/Hosting/SessionConversationService.Execution.cs)).

Refusal before conversation retention remains an explicit refusal, not an uncertain saved request. Unconfirmed submission or later audit failure identifies the actual submitted conversation request, never the source-review request ([attachment completion boundary](../src/Kora.Application/Hosting/SessionConversationService.AttachmentAnswer.cs)).

An admitted result retains the question and genuine ASSISTANT answer in the same conversation. Each supported claim has host-resolved retained session/source/revision/digest and exact UTF-16 span references, not model-invented locations. Citation identity is verified, not semantic truth: inspect the retained source to check each claim. Unsupported evidence is explicit ([response admission](../src/Kora.Core/Context/SessionAttachmentAnswerResponse.cs), [retained commit](../src/Kora.Application/Hosting/SessionConversationService.AttachmentAnswer.cs)).

The [shared local-use risk policy](privacy-safety-and-logs.md#reviewed-local-source-use-risk-admission) screens the whole retained source, question and excerpts. Content remains private LocalOnly, not proven non-sensitive; detected risks are refused without an override. Unqualified runtimes remain unavailable; no generation or provider qualification is enabled by these controls. Cancellation clears pending review; late results cannot be published as success. If retention/commit is unconfirmed, inspect exact conversation/task receipts without repeating the turn. Close/privacy retirement clears private source-review controls ([native lifecycle](../src/Kora/LocalFilePreviewWindow.AttachmentAnswer.cs), [execution fences](../src/Kora.Application/Hosting/SessionConversationService.Execution.cs)).

## Response modes

Kora supports three response modes:

- **Text and speech** - show text and speak when speech is available.
- **Speech only** - prefer speech without ordinary response text.
- **Text only** - show text and do not speak.

Safety and recovery information remains visible even in VoiceOnly mode.

### Choose output for one new task

In the selected Active conversation, use **Response output - this new task only** in the expanded, compact or response-window composer. Choose **Inherit**, **VisualOnly**, **Hybrid** or **VoiceOnly**, then **Review this task's input and output**. Inspect the complete original input, conversation, original channel and configured mode. Separately choose **Confirm and submit this task once**, or cancel without submitting ([native workflow](../src/Kora/SessionsViewModel.TaskResponseOutput.cs)).

Inherit captures the ordinary configured mode at review. This choice belongs only to the new submitted task, including its queue wait; it never changes the session or saved default. Editing input or output choice retires the review. The completed status distinguishes configured/effective output and completed speech. Mandatory privacy, call, output-device and speech-limit gates still win; VoiceOnly cannot hide required visuals ([output consumer](../src/Kora.Application/ViewModels/MainViewModel.TaskResponseOutput.cs)).

Typed input can use `session request output <session-id> <generation> <metadata-revision> VoiceOnly -- <original body>`, then separately `session request confirm` or `session request cancel`. Activated voice requires the current assistant-name prefix and retains its original voice authority; native confirmation cannot bypass protected-call or revoked voice consent ([shared review](../src/Kora.Application/Hosting/SessionRequestCommandService.cs)).

Choosing a mode never submits, speaks, listens, replays a previous answer or enables a model. Expired/changed authority and unconfirmed retention withhold output without retry. Later requests inherit normally ([one-task binding](../src/Kora.Application/Hosting/SessionConversationService.TaskResponseOutput.cs)).

Ordinary speech is additionally admitted only when its complete title/body
fits the device-local spoken summary caps (default **3 sentences / 80 words**).
Either cap can be lowered in Speech & audio. Over-cap results retain their full
visual text, with an explicit **Speech withheld** status even in Speech only
mode; there is no truncation, extra model call or replay.
Required questions/options and exact security-sensitive readback keep their
existing mandatory bounds and privacy gates, not ordinary-summary truncation.
See [the exact counting contract](settings.md#spoken-summary-limits).

The independently stored **Earlier muted-speaker preference** remains
outside the delivered mode registry. Its default and storage are unchanged;
it cannot suppress mandatory complete visual recovery for missing, muted,
zero-volume, unavailable or failed output. The original response remains available,
not just a warning. A later eligible response uses the freshly resolved output;
unmuting or changing configuration never replays retired speech. This applies
to **System** and a selected speaker. See [the existing fallback boundary](settings.md#muted-speaker-fallback).

## Brief answers and full details

For complex local answers, Kora requests a short plain-text brief and a complete
Markdown answer. The response UI and ordinary speech use the brief plus the host's
offer: **Would you like to view the detailed response?** The complete original
answer is kept unchanged for Details. If no valid brief is supplied for a complex
answer, Kora says a detailed response is available rather than inventing a summary
or silently cutting off the answer. Speech still requires the complete brief,
host warnings and offer to fit the existing sentence/word caps.

Choose **Yes, show details** or type `show me the detailed results` to expand the
same interaction window and open the exact result. Activated voice uses the
current assistant name, for example `Kora, show detailed results`.
**Not now** declines only this result's offer; it does not delete the full answer
or change a saved preference. Generic prefix-free Yes is not a new reply channel.
Questions, approvals and security-sensitive previews keep their original native
controls and cannot be accepted through a details offer.

Retained conversation entries show the brief and a separate **Open full detailed
answer** link. **Message details** still opens the exact original USER message.
The [integrated pane](windows-and-tray.md#details-beside-the-conversation)
keeps the composer visible while reading either source. Provider/runtime readiness
remains independent: these presentation controls do not enable a model.

### Reading headings and final tables

In rendered Markdown **Details**, use **Go to heading** buttons for local
document links. Click, Enter or Space focuses the exact heading in this result.
Missing or invalid targets report a status; external links require separate review
and confirmation as described below, never document-local activation.

When an independently available table profile renders a final Markdown table,
use arrow keys between its headers and cells, Home/End across a row, or
Ctrl+Home/End for the first header/last cell. Activate a header to sort ascending
ordinal text, then again for descending. Equal values keep original order.
**Reset table order** restores original order and the last focused cell.
Reopening starts in original order. Sorting never edits the answer.

**Continuous text** and **Exact source** retain their existing selection and
literal Find behavior. No table action copies content or grants clipboard
access. Closing or retiring the original result retires its navigation too.
An unavailable table profile still shows the complete source, not an incomplete
table; these controls do not enable an unqualified profile.

### Review a final-answer external link

An available final-answer Markdown profile can show **Review external link**
beside a parsed destination. Pointer activation or Enter/Space requests a
separate native review of the original selected source token and exact HTTPS
destination. It does not retrieve or open anything. Only **Open this exact
destination once** in that review requests the registered external browser.
Cancel leaves the source unchanged.

Generated links remain untrusted, not retrieved-page provenance. Existing
destination, network-mode, privacy and browser-permission checks still apply;
offline/local-only mode never changes automatically. Source replacement,
retirement or withdrawal invalidates the review. No automatic fetch, model
call, new grant or clipboard permission is introduced. Unavailable renderer
profiles retain their labelled source fallback without active links.

## Exact current-response window actions

`dismiss current response` and `open current response details` use the same native
controllers as **Dismiss** and **Current response details**. `current response help`
lists the exact grammar. Activated voice requires the current assistant-name prefix.
Only the original visible ordinary response is eligible: never a question,
approval, preview, raw source or a newer response selected while work was queued.
Native acknowledgement is required; privacy/ownership/input/call changes, source
retirement or an unavailable native host fail closed. Details retire with that
response and grant no clipboard authority. These actions do not change topmost,
always-show or any other saved preference, speak, invoke a model or pin content.
See [exact commands](commands.md#dismiss-or-inspect-the-exact-current-response).

## Static HTML result boundary

An explicitly host-admitted static HTML result uses a separate `static-html-v1` detail route. This profile is **not qualified or enabled by default**: the existing native viewer shows the complete admitted source with a qualification-unavailable status, not a claimed rich preview.
Ordinary text and Markdown are not reclassified as HTML ([route and default](../src/Kora/StaticHtmlDetailRenderer.cs), [viewer dispatch](../src/Kora/DetailWindowController.cs)).

The implementation supports only a strict inert fragment subset: headings, paragraphs, lists, quotations, preformatted text and basic inline text. It rejects attributes, document styling, links, forms, scripts, resources and unsupported or malformed markup as a whole; it does not repair arbitrary HTML or silently strip unsafe parts ([parser](../src/Kora.Core/Presentation/StaticHtmlParser.cs)).
Exact source remains available, subject to the original source/privacy lifetime. Opening or closing details grants no new clipboard, execution, network or file access ([profile limits](../src/Kora.Core/Presentation/StaticHtmlProfile.cs), [viewer lifecycle](../src/Kora/DetailWindowController.cs)).

## Deliberate current-response playback

**Read current response**, `read that aloud` and `read current response` share one exact current ordinary-response action.
It retains the complete answer, uses normal speech admission and both summary caps, and refuses VisualOnly and all protected calls. During protection, use the separate [reviewed call exception](#speak-one-current-response-during-call-protection), not this read command ([native controls](../src/Kora/ResponseWindow.axaml), [host action](../src/Kora.Application/ViewModels/MainViewModel.ResponseRead.cs)).

The action has no raw-text input and cannot select conversation USER/HOST rows, raw source bodies, questions or approvals. Successful push-to-talk leaves an existing eligible answer intact so an activated read command can target that same answer.
Every replacement, including identical text, gets a new identity; close, cancellation, stop and privacy/call/ownership retirement prevent late playback.
Recovery never replays a retired request ([source and capture lifecycle](../src/Kora.Application/ViewModels/MainViewModel.VoicePrivacy.cs), [deterministic tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.ResponseRead.cs)).

## Speak one current response during call protection

In the visible response, choose **Review speak once during call**. Inspect the complete **EXACT RESPONSE TO SPEAK** text and destination/output state in Details. Then separately choose **Confirm speak once**, or **Cancel call speech**. The review expires after two minutes, including playback; any source, call, privacy, ownership or output change retires it. **Call speech status** shows whether a current review or owned playback remains ([host operation](../src/Kora.Application/ViewModels/MainViewModel.CallSpeech.cs)).

The [exact typed commands](commands.md#review-one-call-speech-exception) share this operation. Voice cannot initiate or confirm the exception, including through a later native click inside that voice request; status and cancellation remain available. No setting or call protection is disabled. Normal VisualOnly, speech limits, mute, capture, privacy and speech availability still apply. Only this current ordinary response is eligible, not a pending question/approval, arbitrary retained answer or backlog ([single-use authority](../src/Kora.Application/Communication/CallSpeechReview.cs)).

## Read one retained assistant answer

In Sessions or the compact conversation, explicitly read a conversation page, select a row with a retained **ASSISTANT** answer, then choose **Read selected ASSISTANT answer once** or request `read selected answer`. Selection alone never speaks ([native selection and cancellation](../src/Kora/SessionsViewModel.AnswerRead.cs)).

The host reads only that exact assistant body, not its original USER message or separate HOST status. It re-reads the retained record and revision under the same session/privacy/retention authority immediately before playback; a missing or changed source never falls back to the latest/current answer ([source workflow](../src/Kora.Application/Hosting/SessionAnswerReadWorkflow.cs)).

Normal output admission, Text only, protected-call restrictions and both spoken-summary caps still apply. An over-cap answer is withheld in full, never silently shortened; its full visual source remains available. Changing speech settings cannot start or replay this operation ([shared playback admission](../src/Kora.Application/ViewModels/MainViewModel.ResponseRead.cs)).

Use **Cancel answer reading** or the existing speech Stop control to retire playback. Closing its surface, changing selection, privacy/source retirement or cancellation suppresses late completion; another reading always needs a fresh deliberate request ([retirement tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.RetainedAnswerRead.cs)).

Unqualified conversation generation remains unavailable; this operation does not add a model or speech backend ([native tests](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.AnswerRead.cs)).

Typing a command while a prompt review is open still closes that prompt on the first edit. If the submitted typed command is `read selected answer`, that reading is refused once; make a fresh deliberate request. Native and activated-voice reads do not inherit or consume this typed-input refusal ([input guard](../src/Kora/PromptParameterController.AnswerRead.cs)).

## Reviewed earlier conversation context

In Sessions, explicitly read the selected conversation, then choose **Review earlier messages for a follow-up**. Select whole earlier USER and/or finalized ASSISTANT messages from that observed page, write the new request, and choose **Review complete selection and request**. Read the complete review before separately confirming one local-only tool-free follow-up ([native workflow](../src/Kora/SessionsViewModel.HistoryContext.cs)).

Only selected messages from that exact conversation are admitted, in original order. Shared limits are 16 selected messages and 32,768 UTF-8 bytes for the complete model envelope, including policy, request and framing. Oversized selections fail explicitly; nothing is silently shortened or dropped. Prior text is inert evidence, not instructions, approval or hidden learning ([history policy](../src/Kora.Core/Conversation/ConversationHistoryContext.cs)).

The review expires after 60 seconds. Editing the new request or selection, closing review, privacy/control loss, changed or deleted source, or a changed conversation invalidates the operation. Successful admitted local execution retains a genuine ASSISTANT answer separately; unavailable runtime returns an explicit HOST failure without a provider call. Unknown commits remain held for inspection, never automatic retry ([review and submission](../src/Kora.Application/Hosting/SessionConversationService.History.cs)).

Ordinary **Submit** remains unchanged: it does not implicitly include history, memory or attachments. This opt-in operation adds no tools, hosted transfer, saved drafts or memory creation ([ordinary submission policy](../src/Kora.Application/Hosting/SessionConversationService.cs)).

## Return focus to a pending question

[`open pending question <session-id> <task-id>`](commands.md#focus-the-exact-pending-native-question)
only focuses the exact still-open, current host-owned native question.
It cannot reopen dismissed or historical questions, select a different session,
replace a draft answer or answer/approve by command. `pending question help`
displays constant syntax, not question content or current IDs.
Navigation status is separate from the current response and transcript;
question controls retain their existing explicit one-shot answer behavior.

## Conversation clarification

A conversation sent using **Submit with one clarification** in Sessions or
its compact view can use the existing native question window for **one**
structured clarification during that exact model turn. Ordinary **Submit exact
message** keeps its existing final-answer/read-only-tool behavior; it does not
automatically acquire question continuation. The clarification route is a
local request with only `ask_user`: it does not silently include
history, memory, attachments or instructions. Models and profiles that are not
qualified remain explicitly unavailable; this implementation does not enable
either local inference or hosted egress.

For an ambiguous observation request, use **Ask locally with observations and one question** instead. For example, ask "Which version or readiness information applies here?", answer the model's question, and receive the answer from the actual admitted host observations. The model may observe first, ask once, then observe again before answering ([submission policy](../src/Kora.Application/Hosting/SessionConversationService.Questions.cs)).

This separate local path admits only the six existing read-only observation tools and `ask_user` once. Web/MCP calls, effectful actions, settings writes and hosted providers remain excluded. The clarification-only button and ordinary read-only Submit remain separate ([bounded protocol](../src/Kora.Core/Dependencies/ModelToolProtocol.cs), [native controls](../src/Kora/SessionsWindow.axaml)).

The model calls the actual `ask_user` tool, not prose that looks like a question
or approval. Both local clarification paths support:

- **Single answer:** two to four labels shown as radio buttons.
- **Multiple answers:** two to four labels shown as checkboxes, with explicit
  minimum and maximum selections.
- **Text input:** an input field bounded to 500 characters.

The session-owned question window opens from either Sessions view. Its input is
separate from the unsent conversation composer. One call asks one question, not
a multi-field form. Legacy structured questions remain supported.

Review the question, then explicitly **Submit answer**, or use **Answer by
voice** / **Hear then answer** when the one exact foreground question and
current voice, microphone, privacy and call gates permit it. Saving a draft
does not submit it. The host retains the original request channel and the
actual reply channel; voice input is never relabelled as native typed input.
Clarification cannot approve an action, grant tool permission or collect a
credential.

Spoken readback includes the option labels and selection bounds. For multiple
answers, say **option 1 and option 2** or the exact labels separated by **and**.
Duplicates, ambiguous or incomplete lists and out-of-bounds selections are
refused; the question remains available through its native controls. Questions
allowing zero selections also accept **none**.

The original turn's fixed 15-second deadline is not renewed by opening,
reviewing or drafting an answer. Only a fresh submitted answer for the same
session, generation, task and question revision can resume that turn, once.
**Refresh running work** and **Cancel this running conversation task** inspect
and cancel that exact turn in Sessions or its compact view, including while
the clarification is pending. Cancellation acceptance is not terminal success:
the host joins the question/voice/provider work before retaining a cancelled
outcome. Once terminal storage owns the outcome, cancellation cannot roll back
the answer ([canonical lifecycle](../src/Kora.Application/Hosting/SessionConversationService.Execution.cs),
[exact cancellation controls](../src/Kora/SessionsViewModel.ConversationCancellation.cs)).
Cancellation, expiry, changed ownership, an unavailable audit/store, a foreign
answer or any uncertain state prevents continuation. There is no replay,
automatic retry or provider fallback. Inspect the original conversation/task
outcome and explicitly start a fresh request when recovery is needed.

The observation path keeps the same original request, turn, complete tool history, unique call IDs and cumulative byte/call/round budgets across the answer. Its 15-second deadline includes waiting for you. Answer data is sent as a lower-trust user message, never system policy, tool-input authority, action approval, a grant, source/context permission or hosted transmission consent ([continuation engine](../src/Kora.Application/Dependencies/BoundedModelTurnAdapter.cs), [native wire](../src/Kora.Windows/Dependencies/OllamaToolWire.cs)).

After a successful continuation, the genuine final ASSISTANT answer is retained
separately from the original USER message. A refusal or provider failure has an
explicit HOST terminal outcome, not a fabricated answer. If a privacy/source
change prevents terminal confirmation, the original request remains
unconfirmed for inspection; no successful retention is inferred.

## Cancel an exact running conversation

In Sessions, **Refresh running work** observes only the selected exact active conversation.
When an admitted turn is running, its actual task appears beside **Cancel this running conversation task**.
This separate control remains available during submission; it does not select work by message text or cancel another session
([native controls](../src/Kora/SessionsWindow.axaml), [presentation](../src/Kora/SessionsViewModel.ConversationCancellation.cs)).

Choosing Cancel requests cancellation once. **Cancelling** means the request was accepted, not that the provider has stopped.
Kora joins its owned provider work and cancellation callbacks before confirming terminal retention; a non-cooperative provider keeps that operation pending.
Late answers and faults cannot replace the cancelled conversation with success
([lifecycle](../src/Kora.Application/Hosting/SessionConversationService.Execution.cs), [lease](../src/Kora.Application/Hosting/ConversationCancellationLease.cs)).

After a confirmed join and readback, the conversation reports **Cancelled** with no assistant answer.
Its durable dispatched task remains **Unknown**, not a claimed rollback.
If terminal storage, audit or private admission cannot be confirmed, inspect the exact original task without retrying.
If terminal storage wins first, cancellation is refused and the actual committed answer remains
([task rules](../src/Kora.Core/Conversation/ConversationMessage.cs), [race and failure tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.ConversationCancellation.cs)).

Cancellation requires fresh original-user control, exact task/session/generation ownership and trusted audit.
Private closure clears the displayed target and cannot authorize later controls.
This does not add a global stop command, provider retry, history deletion or production runtime qualification; bundled unqualified providers remain unavailable
([cancellation admission](../src/Kora.Application/Hosting/SessionConversationService.Cancellation.cs), [provider gates](../src/Kora.Application/Dependencies/ModelTurnHost.cs)).

## Output precedence

Response selection has this precedence, before all mandatory policy gates:

1. **In-call feedback** - independent device-local UI default, applied only to
   effective Active/Suspected (including manual Active), unless Inherit.
2. **Current task** - temporary.
3. **Current queue** - temporary and used when no task override exists.
4. **Session** - shared resolver seam; no new session control is delivered.
5. **Device default** - persisted locally.

Choose **Use default** for the task or queue choice to return to the next broader
scope. These presentation controls are outside the admitted device-default
registry, not delivered durable session/task/queue configuration or execution.
The device default and independent in-call preference have shared admitted
native/exact workflows; neither changes call policy or the legacy fallback.

In-call **Speech only / Text only / Text and speech** map to the internal
Voice/UI/Both preferences, never speech permission. **Use normal response settings**
(Inherit in typed commands) restores ordinary selection. Unknown/invalid call
evidence does not activate feedback but always withholds speech and requires
full visual recovery; Clear/Unavailable uses ordinary output. A Voice/Both choice
cannot bypass independent call suppression, privacy/lock/capture, mute/zero,
native output lifetime or mandatory safety/question/approval/recovery previews.
See [Settings and explicit recovery](settings.md#in-call-feedback-override) and
[exact commands](commands.md#in-call-feedback-override).

## Visual response window

Successful [explicit web retrieval](commands.md#retrieve-an-explicit-web-page) offers **Open exact web-result details** only for that actual current host-issued result.
Its separate native reader displays inert exact normalized text and complete untrusted provenance, never a browser/live page, embedded guide or durable conversation record. Long addresses stay complete in bounded source rather than trusted chrome.

Open/reopen/search/source/close makes no network call and cannot answer questions, consume approval or change tasks/sessions. Private copy requires the existing explicit disclosure control and unchanged original source admission.
One current volatile web snapshot is discarded on response replacement/close/dismiss, cancellation, privacy/ownership/call/control retirement or disposal; older controls/viewers cannot retarget new results. Closing details alone is presentation-only.

The retrieval's raw/text/serialized bounds are unchanged; complete metadata plus exact text must fit native-text-v1's 256 KiB source or report an explicit failure. An empty successful body stays honestly empty.
Opening details does not create speech/captions, persistence/export, diagnostic content or model/provider transmission. Ordinary response speech and mandatory question/approval cards remain independent. As a privacy-coupled exception, retiring a web snapshot also clears only its still-matching compact response body, not an unrelated later response.

Optional [local speech text](settings.md#local-speech-text) is separate from the
answer panel. It defaults to Off and shows only exact host-admitted current
utterance playback, never queued/failed/suppressed text. It clears immediately
when playback is stopped/cancelled or its response/privacy/call/ownership
generation is retired, even when pinned. Normal completion can retain only
already-observed text, labelled **PREVIOUS SPEECH**, for a bounded default
5-second delay (configurable 0-30), or until unpinned. Unpin preserves the
original deadline. Primary working-area corner placement is device-local.
Visual-only and call-gated responses use the existing full answer panel, not a
fictitious playback caption. Captions cannot answer questions, grant approval,
replay speech or authorize any action; required native recovery is independent.
No caption content is saved, logged or sent to a model.
If the native caption surface fails, captions stop until restart; the complete
answer and required recovery remain visual. Diagnostics record only the failure
type, never caption content.

Drag the response title area to reposition the window. Its last position is
stored on this device and restored when that position remains on a connected
display.

Configure these controls in the **Appearance** group under **Settings > Appearance** ([settings layout](../src/Kora/SettingsWindow.axaml)):

- **Always show** - bypass the response timeout until **Dismiss** is selected;
- **Stay on top** - keep the response above other windows; on by default;
- **Response timeout** - set the response window's 1-60 second inactivity
  interval, default **5 seconds**.

Presence has a separate **Presence timeout** setting in the **Presence** group under **Settings > Appearance**, default **10 seconds** ([settings layout](../src/Kora/SettingsWindow.axaml)). It automatically hides after
inactivity while idle or listening without a pending prompt, but remains
visible for work, speech, and required attention. **Always show** applies only
to the response window.

Responses can include underlined action links below their message. These links
invoke only the specific in-app action attached to the current response, such
as opening the relevant Settings tab. They are separate from question choices
and approval controls, disappear when the response is dismissed or replaced,
and keep the response visible until selected or dismissed.

The bounded passive **Open details** viewer is currently available from
Documentation for an exact embedded page. It is not yet general compact
response routing or durable response history. Model prose cannot open/focus
that viewer, and viewer close does not cancel or approve the current request.
See [passive document details](windows-and-tray.md#passive-document-details).

## Forced visual output

Kora always displays text when:

- no compatible speech voice is available;
- no usable audio output is available;
- the output is muted, at zero volume or otherwise unavailable;
- synthesis or playback fails;
- microphone capture is active;
- a response reports failure or safety information; or
- detected-call policy requires visual output.

These mandatory recovery paths override VoiceOnly; the separately stored legacy
preference cannot disable complete visual recovery or exact pending previews.

## Interrupting spoken responses

No ambient command recognizer runs during playback. Explicit push-to-talk stops
Kora speech before opening capture. Use the existing native speech controls or
the [exact stop command](commands.md#stop-speech-playback); Stop speaking is not a tray entry. Production wake and acoustic
barge-in/playback-rejection quality require separate real-hardware proof.

## Manual call mode and call settings

In **Settings > Calls**, choose **I'm in a call** to protect this run. Choose
**Clear manual call mode** when it ends. Manual mode is not saved across
restart. The status keeps automatic availability separate: this build still
reports **automatic call detection unavailable**, including while manual mode
is active. It does not detect Teams or any other communication provider.
Clearing manual mode does not clear an enabled automatic Active, Suspected or
Unknown observation, nor fabricate detector Clear.

**Reset manual off** has the same narrow clearing effect. **Cached call status**
shows the current run flag, independent automatic observation/availability and
immutable policy/source revision, without probing or starting work. The same
[exact manual commands](commands.md#exact-current-run-manual-call-control) use
the current prefix and genuine original-user admission. Results do not speak
or replace an existing question, approval or security preview; mutation is
refused while those interactions are pending.

When manual mode or an enabled Active, Suspected or Unknown observation applies:

- **Call speech suppression** defaults to on. Kora suppresses automatic
  response speech and shows text.
- **Voice activation during calls** defaults to on. Turn it off to close active
  capture and prevent listening until the call clears.

These settings are independent. You can allow listening while forcing visual
responses, or disable listening during calls. Existing saved output/activation
choices are retained. New protection downgrades are disabled pending complete
exact trusted review; call-wide temporary exceptions remain unavailable. The
[separate current-response speak-once review](#speak-one-current-response-during-call-protection)
does not change those preferences.
Enabling visual protection or disabling call-time activation remains supported.
The new **In-call feedback override** is independent of both. UI is its default;
even a legacy suppression-Off preference does not disable that UI selection.
Unknown/invalid detector evidence always suppresses speech. Feedback changes
never enable input, clear calls, grant consent/permission or edit reusable grants.

Protected-call entry invalidates pending synthesis/playback before slower UI
work. Results and questions stay visual; clearing protection does not replay
old speech, answer prompts, or automatically reopen the microphone.
Every changed manual layer also retires already admitted input/callback and
output generations, including old requests finishing after off/reset. Complete
late results remain visual; enable listening explicitly before a fresh PTT.
Required audit/receipt failure leaves the cached manual flag truthful and
holds conservative evidence-unavailable protection instead of claiming
rollback or authorizing speech/activation/reuse from unknown state.

Voice-originated voice/in-call setting changes, including manual clear/reset,
are rejected while protected. A later mouse confirmation does not change their
original voice origin. Start a **new UI change** instead. Manual controls
recheck call revision, ownership and Windows privacy before applying; stale or
denied changes are not deferred. Status, stop speech, disable listening and
cancellation remain available without a call-option mutation.

Applicable grants and [session action levels](session-permission-levels.md)
remain effective during calls. Spoken approvals default Off; rejected speech
does not answer the pending native card. Already accepted decisions are not
revoked solely because the call changed. Mandatory privacy, ownership, content
and execution gates remain independent.

The current Windows bootstrap reports automatic call detection as unavailable.
An open communications application alone is not treated as proof of a call.
Native accessibility, real-call detection, acoustic leakage and output-stop
timing still require separately approved acceptance.

## Reviewed session memory conversation

In **Sessions > Session memories**, explicitly list the exact active session, select one to sixteen enabled reviewed memories in the conversation subset, and enter a request. Selection reads metadata only.
**Review selected memory and request** shows every complete value, classification, session scope and request; it does not authorize model use ([native route](../src/Kora/SessionsViewModel.MemoryContext.cs)).

**Confirm ONE local-only tool-free turn** separately authorizes that exact reviewed request. The full serialized input must fit 32768 UTF-8 bytes; nothing is truncated. Reviews expire after two minutes.
A genuine final answer is displayed as ASSISTANT and retained through the existing conversation path; unavailable or denied HOST status is not an answer. Unqualified providers remain unavailable ([workflow](../src/Kora.Application/Memory/SessionMemoryContext.cs), [submission](../src/Kora.Application/Hosting/SessionConversationService.MemoryContext.cs)).

Changed, disabled, forgotten, foreign, stale or unreviewed memory cannot be used. Memory management, private-access loss, navigation, cancellation and expiry retire pending review/use and clear its source bodies; late provider results cannot become a successful answer.
Inspection/use never changes retention or remembers a new fact ([source admission](../src/Kora.Application/Memory/SessionMemoryContextSource.cs), [review lifecycle](../src/Kora.Application/Memory/SessionMemoryContextReview.cs)).

Private source reads use their own host-owned control intent and confirmed terminal receipt before disclosure. This does not remember content or alter a selected memory's revision ([read admission](../src/Kora.Application/Hosting/SessionWorkspaceService.MemoryContext.cs)).
The retained turn uses the shared execution/cancellation lifecycle; an accepted running-turn cancellation waits for provider quiescence and retains Cancelled, never a late answer ([canonical execution](../src/Kora.Application/Hosting/SessionConversationService.Execution.cs)).

Ordinary conversation Send remains no-memory. This route does not recall/rank memory automatically, combine other context, promote scopes, invoke tools or transmit to hosted providers.
Selected source bodies are volatile input, not copied into saved conversation history; retained requests and answers follow normal conversation retention ([default and explicit admission](../src/Kora.Application/Hosting/SessionConversationService.cs)).
