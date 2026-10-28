# Apply an instruction to this Active session

Use this when you want the same reviewed guidance on later ordinary local
requests in one conversation. For just one request, use
[one-request instructions](conversation-instructions.md).
Applying guidance does not enable AI: **this build has no approved local model**.

In **Sessions**, select the exact **Active** session by its readable name.
Choose **Apply or stop an instruction for this Active session**, refresh enabled
owned instructions and select one. Selection does not read, enable or apply it.

Choose **Review complete source**. Inspect the complete pinned source, target
session, ordinary local scope and lifetime. Instruction text is lower-trust
guidance under host policy, never approval or authority. Separately choose
**Confirm apply to this session** within two minutes. Neither review nor
confirmation calls a model or retains a conversation message.

When a local model is available, you can check the one-session scope like this:
Send two ordinary requests using **Submit exact message**. Each request
revalidates the exact source and session, uses local-only guidance with no tools,
and retains its genuine local answer or explicit HOST/task failure. Local runtime
unavailability is reported honestly; there is no hosted fallback or change to
your saved provider profile.

Choose **Stop applying**. The next ordinary request has no session instruction
context. Stop revokes new admission immediately; an already captured answer may
finish under the existing mandatory privacy and ownership guards. Closing the
unconfirmed review discards it. Closing a confirmed review window does not stop
applicability: use **Stop applying**, or inspect **Refresh scope status**.

## Typed and activated voice commands

First select the exact Active session in Sessions. These commands use the same
review and confirmation workflow; source and session identities remain internal.

```text
session instruction apply "example"
session instruction confirm
session instruction status
session instruction stop
```

Use the instruction's complete displayed name. Ambiguous or unavailable names
are rejected, not substituted. For voice, prefix each command with your current
assistant name, for example `Kora, session instruction apply "example"`.
Stale queued voice, changed targets and expired reviews require a fresh review.
No spoken GUID, revision or digest is required. Review content stays in the
private native scope window for inspection; it does not grant model authority.

## Boundaries and recovery

This is volatile, device-local applicability for **ordinary local text requests**
only. It is not saved, restored at startup, global, or inherited by another
session or future generation. Source change, hold, disable, session end/rename,
privacy, call or ownership loss retires applicability. Re-enabling or recovering
does not restore the old binding: review and confirm again.

Explicit hosted requests, questions and their continuations, MCP, tools, skills,
agents, attachments, memory/history and separately reviewed one-shot instruction
requests receive no implicit session instruction. The existing
[one-request instruction operation](conversation-instructions.md) is separate.
Its review and request still need their own confirmation.

If an operation is unconfirmed, inspect scope status and the exact conversation
HOST/task outcome. No automatic retry, successful answer, rollback or provider
readiness is inferred. Stop revocation remains in effect even if its audit
receipt fails. No raw source or conversation content is written to diagnostics
or audit.
