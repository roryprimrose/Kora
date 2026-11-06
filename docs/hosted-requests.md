# Explicit Copilot session requests

**Unavailable in this build:** the required Copilot account-use approval is
not enabled. These are review controls, not a working sign-in or a cloud
fallback. Enabling hosted models or signing in to another app cannot enable them.

Use this only when deliberately choosing Copilot for one request. Ordinary
requests do not gain hosted fallback. Local-only policy remains unchanged.
Copilot stays unavailable unless the host has a current authorized Copilot
account/profile, permitted provider mode and ready runtime. Choosing this command
does not set up an account, enable voice or change those settings.

## Review the original request

Use the exact current Active session ID, generation and metadata revision from
the session workspace. No selected-session, title or inferred destination is used.

```text
session request review <session-id> <generation> <metadata-revision> copilot -- <original body>
```

For activated voice, prefix the command with the current assistant name, for
example `Kora, session request review ...`. Existing voice consent, privacy,
name and call restrictions still apply. The original body is everything after
the single separator space in ` -- `, with no normalization.

Review the returned exact body, target, Copilot provider, original channel and
expiry. Review holds this text only in memory; it has not submitted or sent it.
It expires after 60 seconds and can be used only once.

```text
session request confirm <session-id> <review-id>
session request cancel <session-id> <review-id>
```

Confirm and cancel require the exact IDs from that review. A typed confirmation
of a voice request preserves its original channel; it cannot renew stale voice
authority. Changed session state, expired review or lost original admission
requires a fresh review, not a replay.

## Separately approve each physical send

Confirmation retains the original USER request and asks the host to admit the
explicit Copilot operation. It does **not** approve transmission.

When authorized and ready, a separate native **Copilot - exact hosted disclosure**
window shows the destination, account/model, complete noncredential headers and
complete final JSON body. Inspect these bytes, then choose
**Transmit this exact request once**, or **Deny**. Voice request confirmation cannot replace this
native final-send decision. Closing or losing private access never approves.
Each later physical send requires its own disclosure.

Inspect the original request's HOST/task outcome. Only a genuine bounded
successful answer becomes an ASSISTANT response on that same request. Denial,
unavailability or cancellation does not invent an answer or switch providers;
there is no automatic retry. If cancellation is accepted for an exact running
request, the host keeps ownership until its resources join. An uncertain outcome
is not proof of rollback or physical cancellation; inspect before requesting
anything again.

Omit `copilot` to use the existing ordinary request operation without explicitly
selecting Copilot. Do not put credentials in request text.

## Choose a profile for this request only

In **Sessions > Selected-session provider overrides > This request only**, refresh
providers, choose **Copilot** or **Claude**, then explicitly select the advertised
model, reasoning and context values. Review the exact conversation, unchanged
input, complete account-bound profile, original channel and expiry before
confirming once. Changing the service clears its choices and review
([shared picker](../src/Kora/SessionsViewModel.LocalRequestProfile.cs)).

The equivalent exact typed/current-name activated command is:

```text
session request once <session-id> <generation> <metadata-revision> <copilot|claude> <account-id> <catalogue-revision> <model-id> <reasoning-id> <context-id> -- <original body>
```

Use the existing separate `session request confirm` or `session request cancel`.
Omitting the service in `once` retains its existing **Local** meaning
([command syntax](../src/Kora.Core/Commands/SessionCommand.Request.cs)).

This affects only that exact request: app defaults and session overrides stay
unchanged, and the next request uses normal inheritance. Every hosted physical
send still requires the separate native disclosure above. Existing credentials,
model approval, privacy, voice/call admission and provider mode remain mandatory;
the picker cannot make an unavailable account or provider ready
([request admission](../src/Kora.Application/Dependencies/ModelTurnHost.LocalRequestProfile.cs)).

Unavailable catalogues offer no models. A stale, denied or deleted profile, failed
send or cancellation yields no substitute answer or automatic retry. This
tool-free hosted operation does not carry local instructions or earlier history
to another provider ([hosted submission](../src/Kora.Application/Hosting/SessionConversationService.LocalRequestProfile.cs)).

## Review a verified local Unavailable result

In an explicitly configured **Local first** session, the Sessions window offers
**Ask locally; review Copilot only if unavailable**. This is separate from ordinary
Submit and from explicitly requesting Copilot. It starts one tool-free local
request using the exact selected session and original input.

Only the owning local runtime's actual **Unavailable** result, followed by its
confirmed exact physical resource join, can open this handoff. A reason label,
unknown outcome, timeout, cancellation or another request's completion cannot.
Missing runtime qualification or an unavailable configured Copilot account/profile
does not enable the operation.

Read the complete immutable context, reason and exact configured destination in
the handoff window. Approval is single-use. The original turn's expiry still
applies; review does not renew it or include later history. Decline, cancel or
stale review sends nothing. Removing evidence requires a new complete review,
never inherited approval.

Approved current review atomically records the local Unavailable outcome and
one fresh linked request in the same conversation. It does not retry the local
request or approve a send. The separate **Copilot - exact hosted disclosure**
review above must still approve the actual final bytes, destination and account.
Only a genuine successful hosted result is retained as the successor's answer.
Original input/channel, private access, resource ownership and retention rules
remain authoritative; nothing is retargeted to a newly selected conversation.

Generic handoff reviews and hosted/second-opinion reasons without this exact
local outcome and resource proof remain review-only. Production models and
runtime qualification remain unavailable where the host reports them unavailable.
