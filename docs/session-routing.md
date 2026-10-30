# Review a request destination

## Deliberately ask the foreground conversation

Use `ask this conversation <original body>` while one visible, eligible Active conversation presenter is foreground. For voice, prefix it with your current assistant name, such as `Nova, ask this conversation ...`. This is an explicit original request, not automatic relatedness or a selection routing lock ([grammar](../src/Kora.Core/Commands/SessionCommand.Request.cs)).

Review the readable conversation name, complete unchanged body, original channel, local-only provider and expiry in **Review original request**. Then choose **Confirm original request once**, or type/say `session request confirm` separately. **Cancel original request** or `session request cancel` discards the memory-only review. It expires after 60 seconds ([review service](../src/Kora.Application/Hosting/SessionRequestCommandService.cs)).

Confirmation retains the original USER message in that exact session and consumes the canonical local conversation host. Only its genuine successful answer is retained as ASSISTANT; an unavailable local provider produces a truthful HOST outcome, never Copilot/Claude fallback. Native confirmation of a voice request preserves its original voice channel ([canonical submission](../src/Kora.Application/Hosting/SessionConversationService.cs)).

The body starts after the single space following `ask this conversation`. Subsequent case, spaces, Unicode and line breaks are unchanged within existing text/credential bounds. Words such as `shutdown` in this body are user content, not another static command. No earlier history, memory or instruction source is authorized, and neither this session's unsent buffer nor another session's input is submitted or overwritten ([request regressions](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.ForegroundRequests.cs)).

Missing, competing, hidden, Done, read-only or stale presenters refuse without choosing another target or creating a session. Changing focus, selection, target metadata, session generation, control, privacy or original voice/call/name admission requires a fresh review; returning to the old view cannot revive it. Keyboard activation and readable automation values use the same native confirmation path ([presenter owner](../src/Kora/SessionsPresentationOwner.Foreground.cs), [headless evidence](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.ForegroundRequests.cs)).

Auxiliary session views without an original-request review panel retain their existing conversation controls. Opening, hiding or closing such a view does not confirm a request or create a conversation message.

Sessions and compact presenters also retain the separate [exact-answer reply review](reviewed-context.md). Choosing Reply on the retained answer does not submit the foreground request again or consume unsent input. Hiding and reopening that reply presenter invalidates its review rather than sending the follow-up as plain input.

## Choose a local profile for this request only

In **Sessions**, select an existing Active conversation and enter the original
message. Under **Selected-session provider overrides**, use **Refresh**, then
the **This request only - LOCAL** controls to choose every advertised model,
reasoning and context value. These controls are separate from durable session
override controls.

Choose **Review this request only**, inspect the complete destination, profile
and unchanged input, then **Confirm this request only once**. **Cancel** sends
nothing and changes no preferences. Input, destination, source, profile or
authority changes require a fresh review. Subsequent requests inherit ordinary
session/app settings.

The equivalent exact typed command is:

```text
session request once <session-id> <generation> <metadata-revision> <local-account-id> <catalogue-revision> <model-id> <reasoning-id> <context-id> -- <original body>
```

For activated voice, prefix it with the current assistant name. Confirm separately
with `session request confirm`, or discard with `session request cancel`.
The original channel and exact body are preserved. Existing instruction-scope
review still applies; this grants no history, memory, tools or hosted transfer.

Production local profiles remain unavailable without their exact independent
qualification. Empty advertised choices mean unavailable, not a default model.
There is no installation, download, automatic switch, hosted fallback or retry.

## Untargeted requests

An ordinary untargeted local request, typed globally or activated with the current
assistant name, first opens **Review local destination advice**. The same operation applies
with the main conversation collapsed or expanded.

Exact conversation composers and `session request review "<name>" -- <body>`
keep their existing exact-session route. **Open new request** still opens the
separate fresh-only composer. Built-in commands, foreground Customizations editor
intent, pending questions, approvals and explicitly armed dictation keep precedence.
A denied or ambiguous editor route is never retried as a general request.

## Choose, acknowledge, correct, submit

- Review the complete unchanged request, original channel and bounded Active conversation names before any adviser receives them. **Allow this LOCAL advice once** permits only one tools-free local relatedness inference over that exact metadata snapshot. It does not authorize submission, sources, history or an answering provider ([advice admission](../src/Kora.Application/Hosting/SessionRouting/SessionRoutingService.cs)).
- **Choose destination without advice** skips inference and opens the existing destination review. Choose New or use **Correct destination** for an exact Active conversation. **Cancel request** or Escape sends nothing to an adviser and submits nothing. Changed input, privacy, metadata, session generation or the 60-second disclosure lifetime invalidates this decision ([native disclosure](../src/Kora/SessionRouting/SessionRoutingAdviceWindow.cs)).
- After that separate decision, **Review request destination** shows any advice.
  An admitted, qualified local provider may propose one related Active conversation
  or several exact candidates. The choice always includes **New conversation (fresh session)**.
- No clear match, or unqualified/unavailable inference, offers fresh **New**.
  Unqualified production providers remain unavailable; composing matching does not
  qualify them. The host never substitutes a guess from current selection, focus,
  recent activity or window size.
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
it as a local-UI request. This feature does not add hands-free routing replies.

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
