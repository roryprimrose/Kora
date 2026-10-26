# Review a request destination

## Deliberately ask the foreground conversation

Use `ask this conversation <original body>` while one visible, eligible Active conversation presenter is foreground. For voice, prefix it with your current assistant name, such as `Nova, ask this conversation ...`. This is an explicit original request, not automatic relatedness or a selection routing lock ([grammar](../src/Kora.Core/Commands/SessionCommand.Request.cs)).

Review the readable conversation name, complete unchanged body, original channel, local-only provider and expiry in **Review original request**. Then choose **Confirm original request once**, or type/say `session request confirm` separately. **Cancel original request** or `session request cancel` discards the memory-only review. It expires after 60 seconds ([review service](../src/Kora.Application/Hosting/SessionRequestCommandService.cs)).

Confirmation retains the original USER message in that exact session and consumes the canonical local conversation host. Only its genuine successful answer is retained as ASSISTANT; an unavailable local provider produces a truthful HOST outcome, never Copilot/Claude fallback. Native confirmation of a voice request preserves its original voice channel ([canonical submission](../src/Kora.Application/Hosting/SessionConversationService.cs)).

The body starts after the single space following `ask this conversation`. Subsequent case, spaces, Unicode and line breaks are unchanged within existing text/credential bounds. Words such as `shutdown` in this body are user content, not another static command. No earlier history, memory or instruction source is authorized, and neither this session's unsent buffer nor another session's input is submitted or overwritten ([request regressions](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.ForegroundRequests.cs)).

Missing, competing, hidden, Done, read-only or stale presenters refuse without choosing another target or creating a session. Changing focus, selection, target metadata, session generation, control, privacy or original voice/call/name admission requires a fresh review; returning to the old view cannot revive it. Keyboard activation and readable automation values use the same native confirmation path ([presenter owner](../src/Kora/SessionsPresentationOwner.Foreground.cs), [headless evidence](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.ForegroundRequests.cs)).

## Untargeted requests

An ordinary untargeted local request, typed globally or activated with the current
assistant name, opens **Review request destination**. The same operation applies
with the main conversation collapsed or expanded.

Exact conversation composers and `session request review "<name>" -- <body>`
keep their existing exact-session route. **Open new request** still opens the
separate fresh-only composer. Built-in commands, foreground Customizations editor
intent, pending questions, approvals and explicitly armed dictation keep precedence.
A denied or ambiguous editor route is never retried as a general request.

## Choose, acknowledge, correct, submit

- A qualified local matcher may propose one related Active conversation or several
  exact candidates. The choice always includes **New conversation (fresh session)**.
- No clear match, or unqualified/unavailable inference, offers fresh **New**.
  Production relatedness inference is currently **unqualified**: it does not guess
  from a name, current selection, focus, recent activity or window size.
- Choose **Review this destination** to acknowledge the exact name. Nothing has
  been submitted or created yet.
- **Correct destination** offers the bounded host-presented Active conversations
  and New. Changing the choice clears the acknowledgment and requires review again.
- **Submit original request to acknowledged destination** consumes that review
  once. New creates a fresh empty conversation first; the original request is then
  submitted through the ordinary conversation host. These are separate audited
  operations, not an atomic rollback. An unavailable answering provider remains
  truthfully unavailable, not a fabricated answer.
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

Deterministic matcher and headless-native tests establish implementation behavior,
not qualification of a production inference engine, microphone, installed-native
accessibility profile or answering provider.
