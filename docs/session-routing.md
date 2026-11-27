# Review a request destination

## Reopen after desktop privacy loss

After lock or desktop-owner loss, return to the unlocked session and explicitly choose **Sessions**, **Show Kora** or **Settings**. Private windows stay retired until their owned shutdown has completed successfully and current desktop ownership is available. A gesture made while retirement is pending is not queued; choose again after recovery ([private recovery](../src/Kora.Application/ViewModels/MainViewModel.PrivatePresentation.cs)).

In reopened Sessions, select the conversation and explicitly read its conversation or open its result again. Unlock does not restore a private page, confirmation, or unsent input, and never submits it. Retained conversation history and legacy read-only input recovery keep their separate existing policies ([native transition evidence](../tests/Kora.Windows.IntegrationTests/DesktopLockRecoveryTests.cs)).

## Deliberately ask the foreground conversation

Use `ask this conversation <original body>` while one visible, eligible Active conversation presenter is foreground. For voice, prefix it with your current assistant name, such as `Nova, ask this conversation ...`. This is an explicit original request, not automatic relatedness or a selection routing lock ([grammar](../src/Kora.Core/Commands/SessionCommand.Request.cs)).

Review the readable conversation name, complete unchanged body, original channel, local-only provider and expiry in **Review original request**. Then choose **Confirm original request once**, or type/say `session request confirm` separately. **Cancel original request** or `session request cancel` discards the memory-only review. It expires after 60 seconds ([review service](../src/Kora.Application/Hosting/SessionRequestCommandService.cs)).

Confirmation retains the original USER message in that exact session and consumes the canonical local conversation host. Only its genuine successful answer is retained as ASSISTANT; an unavailable local provider produces a truthful HOST outcome, never Copilot/Claude fallback. Native confirmation of a voice request preserves its original voice channel ([canonical submission](../src/Kora.Application/Hosting/SessionConversationService.cs)).

The body starts after the single space following `ask this conversation`. Subsequent case, spaces, Unicode and line breaks are unchanged within existing text/credential bounds. Words such as `shutdown` in this body are user content, not another static command. No earlier history, memory or instruction source is authorized, and neither this session's unsent buffer nor another session's input is submitted or overwritten ([request regressions](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.ForegroundRequests.cs)).

Missing, competing, hidden, Done, read-only or stale presenters refuse without choosing another target or creating a session. A captured voice-originated review survives unrelated focus and app switches; typed reviews remain focus-bound. Changed selection, target metadata, session generation, control, privacy, lock or original voice/call/name admission requires a fresh review; returning to the old view cannot revive it. Keyboard activation and readable automation values use the same native confirmation path ([presenter owner](../src/Kora/SessionsPresentationOwner.Foreground.cs), [headless evidence](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.ForegroundRequests.cs)).

Auxiliary session views without an original-request review panel retain their existing conversation controls. Opening, hiding or closing such a view does not confirm a request or create a conversation message.

Sessions and compact presenters also retain the separate [exact-answer reply review](reviewed-context.md). Choosing Reply on the retained answer does not submit the foreground request again or consume unsent input. Hiding and reopening that reply presenter invalidates its review rather than sending the follow-up as plain input.

## Approve all voice readback

An activated-voice request to the original foreground chat, including
`Kora, ask this conversation <body>`, or to an exact named chat with
`Kora, in <chat name>, <body>`, skips request confirmation when that
destination's current permission is **Approve all**.

Kora shows the complete unchanged request and named destination without taking
focus, then speaks **Sending to &lt;chat&gt;: &lt;exact text&gt;. Say cancel to stop.**
One bounded **cancel** capture is open during playback. Native Cancel, Escape,
spoken cancel, failed playback/capture or lost original admission stops the
operation. Capture must physically close before submission. This does not depend
on the optional spoken send preference; microphone and speech consent/readiness
are still required.

The exact destination permission generation and revision are checked again in
the same storage transaction that audits automatic approval and retains the
original request. Any policy change returns to normal review without retaining
or submitting the request. Audit failure blocks submission. The existing
Settings-selected provider route and original ActivatedVoice channel remain
unchanged; there is no provider fallback or automatic retry.

Inferred, last-selected, missing and New destinations never skip destination
review or send. Typed/native requests and request-only provider, retry and
task-output reviews are unchanged
([readback host](../src/Kora.Application/ViewModels/MainViewModel.ApproveAllReadback.cs),
[atomic retention](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.Conversation.cs)).

## Optional spoken send or cancel

In **Settings > Speech and audio**, **Spoken send or cancel** is **Off** by
default. Enabling this device-local preference does not grant microphone
consent, enable listening or authorize a hosted transfer
([preference owner](../src/Kora.Application/Configuration/SpokenSendReplyConfigurationService.cs)).

When enabled, a voice-originated foreground or named-conversation request
review, or an acknowledged routing destination, reads back
**Send to &lt;chat&gt;: &lt;exact text&gt;?**. Only after playback finishes does
one bounded reply open. Say exactly **send** or **cancel**, without an activation
name. **send** uses the existing confirmation path and preserves the original
voice channel. **cancel**, a mismatch, no-speech timeout, changed target or lost
privacy/call/ownership admission submits nothing. Capture release is joined
before confirmation. The existing 60-second review expiry is not renewed
([exact reply](../src/Kora.Application/Hosting/SessionRequestVoiceReply.cs),
[capture owner](../src/Kora.Application/Hosting/SessionRouting/SessionRoutingVoiceReply.cs)).

Native confirmation/cancellation and destination correction remain available.
Typed/native-originated reviews never open this automatic reply. Authentication,
provider readiness and separate native approval of each hosted physical send
remain independent. Task-output and Details-offer questions are unchanged
([host admission](../src/Kora.Application/ViewModels/MainViewModel.Sessions.cs),
[native regressions](../tests/Kora.Windows.IntegrationTests/SessionRoutingTests.SpokenSend.cs)).

## Choose a profile for this request only

In **Sessions**, select an existing Active conversation and enter the original
message. Under **Selected-session provider overrides**, use **Refresh**, then
the **This request only** controls to choose **Local**, **Copilot** or **Claude**
and every advertised model,
reasoning and context value. These controls are separate from durable session
override controls ([shared picker](../src/Kora/SessionsViewModel.LocalRequestProfile.cs)).

Choose **Review this request only**, inspect the complete destination, profile
and unchanged input, then **Confirm this request only once**. **Cancel** sends
nothing and changes no preferences. Input, destination, source, profile or
authority changes require a fresh review. Subsequent requests inherit ordinary
session/app settings.

The equivalent exact typed command is:

```text
session request once <session-id> <generation> <metadata-revision> [local|copilot|claude] <account-id> <catalogue-revision> <model-id> <reasoning-id> <context-id> -- <original body>
```

For activated voice, prefix it with the current assistant name. Confirm separately
with `session request confirm`, or discard with `session request cancel`.
The original channel and exact body are preserved. Omitted service means **Local**;
its existing instruction-scope review still applies. Hosted requests remain
tool-free and require [separate native approval of each physical send](hosted-requests.md#separately-approve-each-physical-send);
request confirmation alone grants no hosted transfer, authentication, history or
memory ([host consumer](../src/Kora.Application/Dependencies/ModelTurnHost.LocalRequestProfile.cs)).

Production profiles remain unavailable without their exact independent
qualification. Empty advertised choices mean unavailable, not a default model.
There is no installation, download, automatic switch, hosted fallback or retry
([request admission](../src/Kora.Application/Dependencies/ModelTurnHost.LocalRequestProfile.cs)).

## Untargeted requests

An ordinary untargeted request typed globally first opens
**Review local destination advice**. The same operation applies
with the main conversation collapsed or expanded.

For an activated-voice untargeted request while Kora's chat window is foreground,
the exact Active foreground chat is instead preselected in **Review request destination**.
No advice consent or inference runs for that proposal. **Review this destination**
and **Submit original request to acknowledged destination** are required unless
the exact original foreground chat qualifies for Approve all readback above;
correction to another presented Active chat or New remains available. A changed or
retired target resource invalidates the original review without substitution.
This preserves the foreground-as-proposal decision ([D2a](../Design/Decision_Register.md#voice-to-action-interaction---2026-11-27)).

When no Kora chat is foreground, an activated-voice request uses **Local voice
destination advice** in Settings instead of per-request advice consent. This
device-local preference is **Off** by default. When On, a qualified LOCAL model
receives only the unchanged request and one permitted Active-chat metadata page,
with the last selected Active chat identified as the preferred candidate. It
uses no tools, history or memory. A clear proposal may preselect that chat or a
more related Active chat; the review names this basis as **inferred**.

If advice is Off, unavailable, unqualified, ambiguous, has no match or fails,
the last selected Active chat alone is preselected, when it is still current
and in the permitted page. The review names the **last selected** basis; failure
is shown explicitly. Without an eligible last selection, the normal destination
choices remain. A selection does not survive replacement, retirement or loss
of access merely because its name is remembered. Unrelated focus changes do not
retire it. Typed requests retain per-request LOCAL advice consent.

Neither inferred nor last-selected proposals skip acknowledgment or send,
including in Approve all sessions. Correcting to another Active chat or New is
still possible. Only the original foreground or explicitly named destination
can qualify for Approve all readback.

Advice and destination acknowledgment use supporting tabs in the same expanded workbench, not additional application windows ([advice view](../src/Kora/SessionRouting/SessionRoutingAdviceView.cs), [destination view](../src/Kora/SessionRouting/SessionRoutingView.cs)).
Voice-originated reviews are shown without activating their window or moving keyboard focus.
Unrelated focus/app switches do not cancel a review or its wake evidence; clicking
the review activates its window normally. Target/session/metadata changes, privacy,
lock, ownership, call/owner binding, name/profile changes, expiry and cancellation
still retire it. Typed/LocalUi activation remains unchanged.
Changing or hiding that review retires its consent/acknowledgment; returning cannot submit an old choice. Original typed/voice channel and unchanged input stay bound through separate acknowledgment and submission ([hosted operations and withdrawal](../tests/Kora.Windows.IntegrationTests/SessionRoutingTests.Workbench.cs)).

Exact conversation composers and `session request review "<name>" -- <body>`
keep their existing exact-session route. **Open new request** still opens the
separate fresh-only composer. Built-in commands, foreground Customizations editor
intent, pending questions, approvals and explicitly armed dictation keep precedence.
A denied or ambiguous editor route is never retried as a general request.

## Choose, acknowledge, correct, submit

- For typed requests, review the complete unchanged request, original channel and bounded Active conversation names before any adviser receives them. **Allow this LOCAL advice once** permits only one tools-free local relatedness inference over that exact metadata snapshot. Voice requests use the standing preference described above. Neither form of consent authorizes submission, sources, history or an answering provider ([advice admission](../src/Kora.Application/Hosting/SessionRouting/SessionRoutingService.cs)).
- **Choose destination without advice** skips inference and opens the existing destination review. Choose New or use **Correct destination** for an exact Active conversation. **Cancel request** or Escape sends nothing to an adviser and submits nothing. Changed input, privacy, metadata, session generation or the 60-second disclosure lifetime invalidates this decision ([native disclosure](../src/Kora/SessionRouting/SessionRoutingAdviceView.cs)).
- After that separate decision, **Review request destination** shows any advice.
  An admitted, qualified local provider may propose one related Active conversation
  or several exact candidates. The choice always includes **New conversation (fresh session)**.
- No clear match, or unqualified/unavailable inference, offers fresh **New**.
  Unqualified production providers remain unavailable; composing matching does not
  qualify them. Non-foreground voice instead offers the eligible last-selected
  chat alone as described above; unrelated focus, recent activity and window size
  do not infer a destination.
- Choose **Review this destination** to acknowledge the exact name. Nothing has
  been submitted or created yet.
- **Correct destination** offers the bounded host-presented Active conversations
  and New. Changing the choice clears the acknowledgment and requires review again.
- **Submit original request to acknowledged destination** consumes that review
  once. New creates a fresh empty conversation first; the original request is then
  submitted through the ordinary conversation host. These are separate audited
  operations, not an atomic rollback. An unavailable answering provider remains
  truthfully unavailable, not a fabricated answer. Normal model/provider/source admission remains separate; advice consent cannot be reused for it ([canonical submission](../src/Kora.Application/Hosting/SessionConversationService.cs)).
- **Cancel** or Escape submits nothing. Reviews expire after 60 seconds. Changed
  input authority, privacy, permissions or destination metadata require a fresh
  request; they never silently select another destination.

The destination is revealed by name in the review in either mode, without
switching the previous conversation or disturbing its reading position. Only this
original request is routed. Other unsent input stays in its own memory buffer;
grants, provider state, history, sources and permissions are not moved.

The native confirmation may confirm an activated-voice request but cannot relabel
it as a local-UI request.

### Explicit voice replies in destination review

In **Review request destination**, choose **Answer by voice** to open one bounded
reply capture. Speak exactly a displayed destination number, **New**, or **Cancel**.
Names, number-word aliases, assistant-name prefixes and other commands are not
accepted. The host uses the exact displayed ordinal object, including when names
are duplicated; it does not reconstruct a list or guess a destination.

Capture must physically close before **Review this destination** acknowledges the
exact name. Nothing has been submitted. To continue by voice, choose **Answer by
voice** again and speak exactly **Submit original request**, **Correct destination**,
or **Cancel**. No microphone opens automatically at acknowledgment.

**Correct destination** retires that selection and confirmation, shows the existing
fixed Active inventory, and requires another exact choice and name acknowledgment.
The original review's 60-second expiry is never renewed. Native selection,
correction, submission and cancellation remain available; changing a native choice
retires and joins the current reply before acting.

Submission executes the same one-use original-request operation, preserving its
unchanged USER body and LocalUi/ActivatedVoice channel. Spoken choices are not new
requests, global commands, provider input, source consent or tool approval. The
independent local-advice disclosure still requires its own native decision.

An unavailable or unqualified capture service explicitly leaves the microphone
closed and offers native controls. Invalid replies terminate without fallback or
submission. Original source, private presentation, ownership, name, profile,
privacy, call and capture changes invalidate the operation rather than reviving
it later. Cancellation, close, shutdown and disposal join any opening or delayed
release before late recognition can act. Deterministic fakes establish implementation
behavior, not runtime voice-profile enablement.

## Privacy and unavailable boundaries

The host reads at most one page of 50 metadata entries through the private
workspace reader. Only Active descriptors from that page, with names and exact
host evidence references, may reach a qualified local matcher. It does not fetch
transcripts, answers, tools or definition bodies, and it never sends routing
metadata to a hosted provider. More conversations can still be addressed explicitly
through Sessions or their exact readable names.

Archived conversations are excluded; this operation cannot resume them. Missing,
malformed, stale or failed inference is not interpreted as an unrelated request.
Only the explicit typed **No match** or **Unavailable** outcome offers New.
There is no automatic fallback after denial, cancellation, expiry or uncertain
creation/submission. Inspect a disclosed exact attempt before taking further action.

Matching runs in its own original-channel control session, never a user destination.
Its physical inference resources are joined and its outcome recorded before review
opens. After acknowledgment, a separate ordinary conversation turn receives the exact
original body and channel; only that turn's genuine final answer is retained once.
The routing proposal is never retained as an assistant answer. Closing or revoking
routing while inference is running suppresses late proposals and retains ownership
until actual termination, without submitting New.

Deterministic admitted-provider and headless-native/private-store tests establish implementation behavior,
not qualification of a production inference engine, microphone, installed-native
accessibility profile or answering provider.
