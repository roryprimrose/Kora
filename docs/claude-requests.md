# Claude requests

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
The first native HTTPS profile is stateless text-only with thinking disabled:
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
