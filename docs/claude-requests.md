# Claude requests

Claude is an online AI service from Anthropic. **Requests are unavailable in
this build**, because the required account-use approval is not enabled.
The controls below do not bypass that restriction or sign you in through
another app.

## Availability and independent setup

Claude is a distinct hosted service, not a Copilot account or a local-model
fallback. The shipped deployment/account-use approval source is **unavailable**.
Implementation tests do not enable or qualify a real account. No sign-in,
inference, paid request or provider qualification is performed automatically.

When an independently approved deployment permits the exact adapter, account-use
terms and cost, open **Configure Claude account (volatile API credential)** in
Settings. Credential entry is private native UI only. Without approval the
credential control stays disabled. An approved host-issued credential/workspace
lease must bind the actual credential before a read-only Models request.
Models success authenticates that credential for the approved workspace; it is
not evidence of a claimed Console login or subscription.

Credentials remain in memory only. They are not accepted through voice, chat,
environment variables, Claude CLI, Copilot sign-in or saved settings. Forget,
privacy retirement, expiry and approval revocation retire the volatile catalogue.
A complete, bounded account-bound model list is required; unknown limits,
incomplete pages and unsupported capabilities are unavailable, never guessed.
Refresh the service/model settings, choose the exact current Claude profile and
save it. Selecting a profile is not permission to transmit.

## Complete explicit operation

In the private Sessions window, choose the current Active conversation, enter
the original message and use **Submit explicitly with Claude (separate
disclosure reviews)**. Ordinary Submit does not silently select Claude.
The default native HTTPS profile is stateless text-only with thinking disabled:
no tools, files, history, attachments, memory, agents, delegation or instructions
from other sources are included. Advertised native effort options map directly
to the wire; a rejected combination is not changed or retried.

Typed or current-name activated voice input uses the canonical request review
and separate confirmation without speaking internal identifiers:

```text
session request review active claude -- <original body>
session request review "Readable topic" claude -- <original body>
session request confirm
session request cancel
```

Use the current assistant-name prefix for voice. `active` requires exactly one
Active conversation; an exact readable name must resolve unambiguously to an
Active conversation. Ambiguity or an incomplete inventory refuses without
submission. Confirmation consumes only the one host-held request review, never
a question, permission or another pending operation. The host pins internal
identity, generation and metadata revision before presenting review.

The existing internal exact-address grammar remains compatible for diagnostics:

```text
session request review <session-id> <generation> <metadata-revision> claude -- <original body>
session request confirm <session-id> <review-id>
session request cancel <session-id> <review-id>
```

Use host-resolved current session identity; do not invent or guess another
session's identifiers. The original body and original channel survive review
and confirmation. A later native confirmation cannot convert original voice
eligibility into native authority. Review confirmation is single-use.

## Ask one clarification explicitly

In private Sessions or compact view, enter your original message and select **Ask Claude with one clarification (separate disclosure reviews)**. This separate operation allows `ask_user` once, with bounded text, radio-button choices or checkboxes ([native control](../src/Kora/SessionsWindow.axaml)).
Reply in the existing session-owned question window, or use its exact voice reply when microphone/privacy/call admission permits ([question owner](../src/Kora.Application/Hosting/ConversationQuestionContinuation.cs)).

**Answering does not send your answer.** The host rereads the canonical stored reply, then offers a new complete native Count Tokens disclosure and a separate Messages disclosure.
Each physical POST needs fresh affirmative native consent, even when the answer was spoken. Deny/close/expiry/cancellation or changed authority stops continuation; earlier approvals are not reused ([send reviews](../src/Kora.Application/Dependencies/HostedTransmissionReviewService.cs), [transport](../src/Kora.Windows/Dependencies/ClaudeMessagesRuntime.Tools.cs)).

The original request is unchanged. No observation tools, files, web, MCP, memory, history, attachments or agents enter this operation. The model may answer immediately instead of asking, but cannot repeat a question or mix it with prose/tool calls ([dedicated submission](../src/Kora.Application/Hosting/SessionConversationService.Claude.cs)).
Reviews, reply and sends share the two-minute hosted deadline and cumulative 32768-byte input/4096-byte output bounds; actual resources join before release ([bounded loop](../src/Kora.Application/Dependencies/BoundedModelTurnAdapter.cs)).

Ordinary text-only, observation and streaming actions are unchanged. This does not enable Claude, approve an account or support Copilot/Codex questions; the shipped deployment source remains unavailable ([availability](../Design/Runtime_Provider_Feasibility.md#native-claude-messages-profile---2026-10-24)).

## A separate native disclosure for every POST

Every content-bearing physical POST requires its **own** private native final-byte
review and affirmative one-send consent. Token counting is content egress:
`POST https://api.anthropic.com/v1/messages/count_tokens` is reviewed first.
Its exact counted input is reused for
`POST https://api.anthropic.com/v1/messages`, which requires a **new** review.
Each review displays method, exact destination, all noncredential headers and
the complete actual UTF-8 JSON body, without truncation. Credentials are not
displayed, logged or audited. There is no affirmative default; closing, Deny,
missing presentation, expiry, privacy or changed authority denies.

Only private native input can approve physical sends. Voice/model text cannot
approve a send or enter a credential. Request review, saved profile, account
setup and earlier consent never authorize another POST. No hidden preflight,
automatic retry, redirect, proxy inheritance, sidecar, SDK-agent or provider
substitution exists.

## Retained result and stopping

The original USER message is retained under its original session and channel.
A genuine bounded final text answer is retained separately as ASSISTANT data.
Refusal, unavailable account/profile, unsupported response blocks, token/byte
overflow, failed review or transport failure produces a truthful HOST/task
terminal or an explicitly unconfirmed outcome, never a synthetic answer.
Inspect an unconfirmed result before making a new deliberate request.

Stop, cancellation, revocation and privacy prevent further sends and suppress
late answer publication. They cannot retract bytes already transmitted.
Outstanding HTTP/account/model work remains owned until it actually joins;
exit does not equate cancellation requested with resource quiescence.
Human reviews are included in the bounded request lifetime.

## Separately qualified native text streaming

In private Sessions, **Stream explicitly with Claude (text only, separately qualified)** submits only the original USER body, without history, memory, tools or source instructions. It uses the same provisional conversation pane, labelled **Provisional answer - not retained**, not a new renderer ([submission](../src/Kora.Application/Hosting/SessionConversationService.Claude.cs), [presentation](../src/Kora/SessionsViewModel.ConversationCancellation.cs)).

This native-only operation requires independent positive evidence for the exact model/reasoning/context tuple and its input/output token ceilings, in the current deployment approval and credential/workspace account lease. Existing buffered approval does not admit streaming; missing/new evidence stays unavailable. The shipped approval source remains unavailable ([profile evidence](../src/Kora.Windows/Dependencies/ClaudeDeploymentApproval.cs), [default source](../src/Kora.Windows/Dependencies/UnavailableClaudeDeploymentApprovalSource.cs)).

Count Tokens remains a separately reviewed physical POST. A second native final-byte review shows the actual Messages body with `stream:true`; earlier request review, voice input or counting consent cannot approve it. No implicit selection, retry or buffered fallback occurs ([runtime](../src/Kora.Windows/Dependencies/ClaudeMessagesRuntime.Streaming.cs), [physical send](../src/Kora.Windows/Dependencies/ClaudeFinalRequestHandler.cs)).

The decoder consumes actual progressive UTF-8 SSE, including split code points and escaped JSON strings. Only validated text deltas reach provisional presentation. Normal message/block ordering, cumulative token usage, `end_turn`, one `message_stop` and physical EOF are required; unknown events/fields, tools, thinking, citations, provider errors, truncation and `max_tokens` cannot produce success ([decoder](../src/Kora.Windows/Dependencies/ClaudeAnswerStream.cs), [protocol](https://platform.claude.com/docs/en/build-with-claude/streaming)).

The pinned API version remains `2023-06-01`. Both POST bodies cumulatively fit 32768 UTF-8 bytes; counting response plus complete SSE cumulatively fit 4096 bytes, with at most 128 events, JSON depth 24 and 64 ordered text blocks. Bounds are inclusive. Exact catalogue ceilings remain at most 8192 input/4096 output tokens. Reviews and transport share the two-minute deadline ([runtime](../src/Kora.Windows/Dependencies/ClaudeMessagesRuntime.Streaming.cs), [decoder](../src/Kora.Windows/Dependencies/ClaudeAnswerStream.cs), [version policy](https://platform.claude.com/docs/en/api/versioning)).

Source/session/account/profile/expiry/privacy authority is revalidated before each send, every publication and final commit. Provisional text is volatile inert text, not speech or an ASSISTANT record. Only the completely validated final answer is retained under that same original request; failures retire the preview. Cancellation-ignoring reads stay owned until joined; failed disposal quarantines the runtime rather than claiming quiescence ([host fences](../src/Kora.Application/Dependencies/ModelTurnHost.Questions.cs), [retention](../src/Kora.Application/Hosting/SessionConversationService.Execution.cs), [composed progressive tests](../tests/Kora.Windows.IntegrationTests/HostedClaudeSessionRequestTests.Streaming.cs)).

Ordinary buffered Claude, reviewed Claude observations, typed/activated Claude requests and local/hosted defaults retain their existing behavior. Deterministic fake HTTP tests are implementation evidence, not a live paid-provider/account, installed or hardware qualification ([compatibility tests](../tests/Kora.Windows.IntegrationTests/Dependencies/ClaudeMessagesRuntimeTests.cs), [stream regressions](../tests/Kora.Windows.IntegrationTests/Dependencies/ClaudeMessagesRuntimeTests.Streaming.cs)).

## Explicit read-only observations

For current Kora version, dependency readiness or recorded local runtime metadata, use **Submit with reviewed Claude observation subset**, not ordinary Claude Submit. Edit the comma-separated exact scope before submission.
Only these six original host capabilities are selectable; no files, web, MCP, settings changes, memory, history or provider-native tools are included ([scope policy](../src/Kora.Core/Dependencies/ClaudeObservationTools.cs), [catalogue](../src/Kora.Core/Tools/ReadOnlyCapabilityCatalog.cs)).

```text
capabilities.list
capabilities.get
application.get_version
readiness.get
runtime.list
runtime.get_status
```

Typed or current-name activated voice requests can select the same bounded subset. Review the displayed original body, channel, conversation and exact tools, then confirm separately. A native confirmation never relabels original voice input ([request route](../src/Kora.Application/Hosting/SessionRequestCommandService.cs)).

```text
session request review active claude-tools=application.get_version,readiness.get,runtime.get_status -- What version and readiness does Kora report?
session request confirm
```

The native send review shows the actual advertised schemas. Claude can only propose calls; the canonical host revalidates authority and executes its own read-only capability. The correlated result is inert data, not instructions or approval.
Each continuation has a new Count Tokens POST and a new Messages POST: **each needs fresh native consent**, including the complete tool result. Denying either prevents that send and any final answer ([host loop](../src/Kora.Application/Dependencies/BoundedModelTurnAdapter.cs), [native transport](../src/Kora.Windows/Dependencies/ClaudeMessagesRuntime.Tools.cs)).

The complete observation turn permits at most six calls and seven exchanges within 15 seconds, including reviews, with cumulative 32768-byte input and 4096-byte output limits. Both physical request bodies, raw responses, framing and sanitized host results consume the budgets.
Unknown fields/names, duplicate call IDs, mixed prose/tool proposals, unsupported blocks and incomplete or oversized responses terminate without retry.
Only a genuine final text answer is retained as ASSISTANT; intermediate proposals are not answers ([constraints](../src/Kora.Core/Dependencies/ClaudeObservationTools.cs), [translation](../src/Kora.Windows/Dependencies/ClaudeObservationWire.cs)).

Changing the selected tool scope invalidates the current operation even if restored later. Account/catalogue/profile/session/privacy revocation and cancellation deny further sends and late publication; actual HTTP work remains owned until joined.
The shipped approval source remains **unavailable**; these controls do not enable a deployment, approve costs or initiate any live trial ([native scope](../src/Kora/SessionsViewModel.Claude.cs), [unavailable approval](../src/Kora.Windows/Dependencies/UnavailableClaudeDeploymentApprovalSource.cs)).
