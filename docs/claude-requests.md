# Claude requests

Claude is an online AI service from Anthropic. **Requests are unavailable in
ordinary builds**, because the required account-use approval is not enabled.
The controls below do not bypass that restriction or sign you in through
another app. In a marked **Development Preview**, explicitly opt in to Claude
through **Settings > Set up development preview capabilities**, then complete
the independent account setup below. This permits testing,
not production qualification; credentials and requests may incur usage charges.

## Availability and independent setup

Claude is a distinct hosted service, not a Copilot account or a local-model
fallback. The ordinary deployment/account-use approval source is **unavailable**.
Implementation tests do not enable or qualify a real account. No sign-in,
inference, paid request or provider qualification is performed automatically.

When an independently approved deployment permits the exact adapter, account-use
terms and cost, open **Configure Claude account (volatile API credential)** in
Settings. Credential entry is private native UI only. Without approval the
credential control stays disabled. An approved host-issued credential/workspace
lease must bind the actual credential before a read-only Models request.
Models success authenticates that credential for the approved workspace; it is
not evidence of a claimed Console login or subscription.

The credential editor uses a private supporting tab in the expanded workbench, not conversation input. Leaving, hiding or minimizing clears its password field and retires setup presentation; an admitted authentication operation remains owned until it actually joins ([account owner](../src/Kora/ClaudeAccountWindowController.cs), [hosted regressions](../tests/Kora.Windows.IntegrationTests/AccountWorkbenchTests.cs)).

Credentials remain in memory only. They are not accepted through voice, chat,
environment variables, Claude CLI, Copilot sign-in or saved settings. Forget,
privacy retirement, expiry and approval revocation retire the volatile catalogue.
A complete, bounded account-bound model list is required; unknown limits,
incomplete pages and unsupported capabilities are unavailable, never guessed.
Refresh the service/model settings, choose the exact current Claude profile and
save it. Selecting a profile is not permission to transmit.

## Ordinary Submit with Claude

Choose the **Hosted preferred** provider mode and Claude as the app default or
this session's service. Then, in a private session, enter or dictate the
original message and choose **Submit**. Kora sends it to the resolved Claude
profile; with any other mode or service, Submit does not select Claude.
The default native HTTPS profile is stateless text-only with thinking disabled:
no tools, files, attachments, memory, agents, delegation or instructions
from other sources are included. Settings-routed Submit includes earlier whole
original USER messages and eligible retained ASSISTANT answers from successful
turns in this same conversation. Newest messages are considered first, then sent
chronologically, within the complete 16-item / 32768-UTF-8-byte envelope limit.
Older non-fitting messages are omitted whole, never shortened. HOST statuses,
failed/denied/cancelled turns without answers, questions, approvals, drafts,
attachments, memory and other sessions are excluded. Local-source-derived and
unknown-source ASSISTANT answers are excluded; existing records establish
eligibility for hosted answers, not local answers. Reviewed LocalOnly evidence
is never promoted ([history contract](../src/Kora.Core/Conversation/HostedConversationHistoryContext.cs),
[source validation](../src/Kora.Application/Hosting/SessionConversationService.HostedHistory.cs)).
When history fits, the complete hosted-history envelope is inert USER input.
Without eligible fitting history, the original new USER body is unchanged.
Physical Count Tokens
and Messages budgets still apply. Kora measures both SDK-generated request bodies
locally before including history and omits additional older whole messages that
cannot fit; preparation itself sends nothing. Changed account/profile or unavailable
preparation fails closed rather than bypassing a limit
([exact input preparation](../src/Kora.Windows/Dependencies/ClaudeMessagesRuntime.HistoryInput.cs)).
Local, Local first's local step, request-only
profiles and customizations keep their existing context
([Claude mapping](../src/Kora.Windows/Dependencies/ClaudeMessagesRuntime.cs)).
Advertised native effort options map directly
to the wire; a rejected combination is not changed or retried.

Buffered text, separately qualified streaming, reviewed observations and clarification requests use the official Anthropic .NET SDK through
Microsoft.Extensions.AI `IChatClient`. Kora still owns credentials, final-request
admission, counted-input admission, strict response policy, budgets and
cancellation/resource ownership. SDK retries and provider telemetry are
explicitly disabled. The host chooses the physical headers; SDK metadata and
trace headers are not forwarded. Tool declarations, proposals and results use
MEAI message types; the SDK never executes a Kora tool or answers a question.
Kora still validates each entire batch and owns all execution and continuation.
Models discovery uses the official SDK's Models/page types behind a bounded
host-owned read-only request gate. Account approval, credential binding and
catalogue publication remain Kora responsibilities. The adapter identity is now
`anthropic-messages-meai-sdk-v5`; earlier adapter evidence does not
qualify this implementation.

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

## One explicitly selected saved definition

In Customizations, load one saved skill or agent, enter the exact existing Active conversation name and original request, then choose **Run selected with Claude**. Save or reload dirty drafts first; no source or target is substituted ([selected operation](../src/Kora/CustomizationWindowController.Claude.cs), [shared input owner](../src/Kora/CustomizationWindowController.Copilot.cs)).

Review these separate decisions in order ([review owner](../src/Kora.Application/Definitions/HostedCustomizationWorkflow.cs)):

1. Approve reading the exact selected primary source, including bounded freshness rereads for this operation only.
2. Affirmatively authorize the displayed complete source, unchanged original USER request and exact Active target as one hosted copy.
3. Separately confirm retention of the original USER request and its answer or truthful HOST outcome.
4. Kora then sends each actual Count Tokens and Messages POST automatically under the current account and profile ([physical gate](../src/Kora.Windows/Dependencies/ClaudeSdkRequestHandler.cs)).

Only a single-file instruction-compatible skill or reference-free/tool-free v1 agent is compatible. The saved definition stays local and unchanged. Claude receives its complete source as lower-trust USER data, followed by the exact original task as the final USER message; Kora's SystemPolicy is unchanged ([compatibility](../src/Kora.Application/Definitions/CopilotCustomizationSelection.cs), [runtime](../src/Kora.Windows/Dependencies/ClaudeMessagesRuntime.cs)).

This does not activate a native Claude skill or agent, discover other definitions, add tools/references, or create another executor. Streaming, clarification and observations are separate operations; unavailable accounts, profiles or selected-copy capability stay unavailable without ordinary-text fallback ([typed admission](../src/Kora.Application/Dependencies/ModelTurnHost.Claude.cs), [adapter](../src/Kora.Application/Dependencies/ClaudeModelTurnAdapter.cs)).

The selected request displays its genuine retained ASSISTANT answer or exact HOST/task receipt after physical work joins. **Stop selected Claude request**, source/input changes, privacy loss and closing cancel the owned operation; cancellation cannot retract transmitted bytes or release cancellation-ignoring work early. Copilot and Claude share one selected-operation slot ([controller](../src/Kora/CustomizationWindowController.cs), [receipt](../src/Kora/HostedCustomizationController.cs)).

## Clarification questions

The separate **Ask Claude with one clarification** button has been removed from
the chat surfaces, which now have a single **Submit**. Asking one clarification
question will return as a permission-governed tool of that Submit in a later
increment ([decision](../Design/Decision_Register.md#settings-routed-single-submit-and-automatic-hosted-disclosure---2026-11-27)).
An already pending Claude question continues to accept its exact inline or voice
reply ([question owner](../src/Kora.Application/Hosting/ConversationQuestionContinuation.cs)).

## Every POST is sent automatically

Choosing Claude in Settings is the decision to send. Token counting is content
egress: `POST https://api.anthropic.com/v1/messages/count_tokens` is sent first,
and its exact counted input is reused for
`POST https://api.anthropic.com/v1/messages`. Kora sends both automatically,
without a byte-review window, at every session permission level
([admission owner](../src/Kora.Application/Dependencies/HostedTransmissionReviewService.cs)).

Before each POST, the host still captures the final bytes and checks the exact
destination, account, profile, session and host authority. Each send writes a
security audit record; credentials are not displayed, logged or audited. Expiry,
privacy loss or changed authority stops the send. Voice or model text cannot
enter a credential. No hidden preflight, automatic retry, redirect, proxy
inheritance, sidecar, SDK-agent or provider substitution exists.

## Retained result and stopping

The original USER message is retained under its original session and channel.
A genuine bounded final text answer is retained separately as ASSISTANT data.
Refusal, unavailable account/profile, unsupported response blocks, token/byte
overflow, stale authority or transport failure produces a truthful HOST/task
terminal or an explicitly unconfirmed outcome, never a synthetic answer.
Inspect an unconfirmed result before making a new deliberate request.

Stop, cancellation, revocation and privacy prevent further sends and suppress
late answer publication. They cannot retract bytes already transmitted.
Outstanding HTTP/account/model work remains owned until it actually joins;
exit does not equate cancellation requested with resource quiescence.

Buffered Count Tokens and Messages also require confirmed reader, response and
HTTP-client disposal before releasing ownership. Failed disposal suppresses the
answer and quarantines that runtime: it remains nonquiescent, refuses later
turns and reports unconfirmed shutdown rather than silently retrying cleanup.
The host retains its existing unconfirmed submission/recovery path; recovering
the retained task does not authorize replay or clear physical-resource quarantine.
No new account approval or provider qualification is granted
([runtime](../src/Kora.Windows/Dependencies/ClaudeMessagesRuntime.cs),
[disposal regressions](../tests/Kora.Windows.IntegrationTests/Dependencies/ClaudeMessagesRuntimeTests.Disposal.cs)).

For a buffered text-only turn, Count Tokens and Messages share one
32768-byte physical request budget and one 4096-byte physical response budget,
including JSON framing and whitespace. Both outbound bodies must fit together
before either POST is sent. A count response that exhausts the output
budget prevents the Messages send; otherwise Messages can read only
the remaining bytes. Declared-length and unknown-length overflow produce no
retained assistant answer, retry or provider fallback. These implementation
limits do not enable or qualify a Claude deployment.

## Native text streaming runtime

The chat surfaces no longer have a separate **Stream explicitly with Claude**
button. Settings-routed Claude answers are buffered in this increment; the
implemented streaming runtime described below remains for a later automatic
selection when the exact profile is qualified for streaming
([decision](../Design/Decision_Register.md#settings-routed-single-submit-and-automatic-hosted-disclosure---2026-11-27)).

In ordinary builds, streaming requires independent positive evidence for the exact model/reasoning/context tuple and its input/output token ceilings, in the current deployment approval and credential/workspace account lease. Existing buffered approval does not admit streaming; missing/new evidence stays unavailable. A current explicit Claude Development Preview opt-in permits this implemented stream without claiming production evidence. The exact account/model/token bounds remain mandatory ([admission](../src/Kora.Windows/Dependencies/ClaudeDeploymentApproval.cs), [default source](../src/Kora.Windows/Dependencies/UnavailableClaudeDeploymentApprovalSource.cs)).

Count Tokens remains a separate physical POST before the Messages body with `stream:true`; each is admitted automatically against current authority. No implicit selection, retry or buffered fallback occurs ([runtime](../src/Kora.Windows/Dependencies/ClaudeMessagesRuntime.Streaming.cs), [physical send](../src/Kora.Windows/Dependencies/ClaudeFinalRequestHandler.cs)).

The official SDK consumes progressive UTF-8 SSE and projects text through MEAI `IChatClient`. A host boundary guard validates the qualified raw event framing and policy before bytes enter the SDK; it does not synthesize a final provider message. Only validated text deltas reach provisional presentation. Normal message/block ordering, cumulative token usage, `end_turn`, one `message_stop` and physical EOF are required; unknown events/fields, tools, thinking, citations, provider errors, truncation and `max_tokens` cannot produce success. The host retains the physical stream until asynchronous disposal finishes, including when SDK enumeration or publication fails ([boundary guard](../src/Kora.Windows/Dependencies/ClaudeAnswerStream.cs), [protocol](https://platform.claude.com/docs/en/build-with-claude/streaming)).

The pinned API version remains `2023-06-01`. Both POST bodies cumulatively fit 32768 UTF-8 bytes; counting response plus complete SSE cumulatively fit 4096 bytes, with at most 128 events, JSON depth 24 and 64 ordered text blocks. Bounds are inclusive. Exact catalogue ceilings remain at most 8192 input/4096 output tokens. Transport shares the two-minute deadline ([runtime](../src/Kora.Windows/Dependencies/ClaudeMessagesRuntime.Streaming.cs), [decoder](../src/Kora.Windows/Dependencies/ClaudeAnswerStream.cs), [version policy](https://platform.claude.com/docs/en/api/versioning)).

Source/session/account/profile/expiry/privacy authority is revalidated before each send, every publication and final commit. Provisional text is volatile inert text, not speech or an ASSISTANT record. Only the completely validated final answer is retained under that same original request; failures retire the preview. Cancellation-ignoring reads stay owned until joined; failed disposal quarantines the runtime rather than claiming quiescence ([host fences](../src/Kora.Application/Dependencies/ModelTurnHost.Questions.cs), [retention](../src/Kora.Application/Hosting/SessionConversationService.Execution.cs), [composed progressive tests](../tests/Kora.Windows.IntegrationTests/HostedClaudeSessionRequestTests.Streaming.cs)).

Ordinary buffered Claude, typed/activated Claude requests and local/hosted defaults are described above. Deterministic fake HTTP tests are implementation evidence, not a live paid-provider/account, installed or hardware qualification ([compatibility tests](../tests/Kora.Windows.IntegrationTests/Dependencies/ClaudeMessagesRuntimeTests.cs), [stream regressions](../tests/Kora.Windows.IntegrationTests/Dependencies/ClaudeMessagesRuntimeTests.Streaming.cs)).

## Explicit read-only observations

For current Kora version, dependency readiness or recorded local runtime metadata, use the typed or voice `claude-tools=` request below. The separate chat button and scope box were removed; folding observations into the single **Submit** as tools is planned.
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

Claude can only propose calls; the canonical host revalidates authority and executes its own read-only capability. The correlated result is inert data, not instructions or approval.
Each continuation has a new Count Tokens POST and a new Messages POST, including the complete tool result; each is admitted automatically against current authority ([host loop](../src/Kora.Application/Dependencies/BoundedModelTurnAdapter.cs), [native transport](../src/Kora.Windows/Dependencies/ClaudeMessagesRuntime.Tools.cs)).

The complete observation turn permits at most six calls and seven exchanges within 15 seconds, with cumulative 32768-byte input and 4096-byte output limits. Both physical request bodies, raw responses, framing and sanitized host results consume the budgets.
Unknown fields/names, duplicate call IDs, mixed prose/tool proposals, unsupported blocks and incomplete or oversized responses terminate without retry.
Only a genuine final text answer is retained as ASSISTANT; intermediate proposals are not answers ([constraints](../src/Kora.Core/Dependencies/ClaudeObservationTools.cs), [MEAI mapping](../src/Kora.Windows/Dependencies/ClaudeToolMessages.cs)).

Changing the selected tool scope invalidates the current operation even if restored later. Account/catalogue/profile/session/privacy revocation and cancellation deny further sends and late publication; actual HTTP work remains owned until joined.
The shipped approval source remains **unavailable**; these controls do not enable a deployment, approve costs or initiate any live trial ([request route](../src/Kora.Application/Hosting/SessionRequestCommandService.cs), [unavailable approval](../src/Kora.Windows/Dependencies/UnavailableClaudeDeploymentApprovalSource.cs)).
