---
description: "Use when changing trust boundaries, host authority, approvals, sessions, privacy, audit, providers, tools, persistence, retention, files, processes, or network access."
applyTo: "src/**/*.cs"
---
# Trust-boundary changes

The source glob intentionally includes presentation and composition: authority and disclosure boundaries are not confined to policy classes.

- Identify the untrusted input, trusted host authority source, protected resource and final irreversible or disclosing operation. Link the applicable [security/data-flow contract](../../Design/Security_Data_Flows.md) and state residual assumptions.
- Preserve host-owned session, permission, approval, device and instance ownership. Unknown, stale or revoked authority fails closed. Revalidate resource identity, revision, scope and authority across awaited work and immediately before commit, execution or disclosure; proposal-time approval is not perpetual authority.
- Provider/model text, tool output, retained content, incoming trace headers, logging fields and scopes are untrusted data/correlation, not authorization. They cannot select a Kora session, create approval, establish ownership or promote a diagnostic into a durable receipt.
- Preserve truthful requested and terminal outcomes through the trusted typed audit path, correlated to host-owned context. Ordinary logs, audit-shaped properties and file mirrors cannot replace authoritative audit commits or grant authority; audit delivery failure must not become success.
- Distinguish cancellation requested, rollback confirmed, uncertain commit and actual resource quiescence. Retain ownership until workers/resources terminate; suppress unauthorized late output and do not release a slot or claim no effect merely because a wait was cancelled.
- Preserve destination/redirect admission and binding to the exact authorized network endpoint; do not let subsequent resolution or transport defaults bypass policy. Treat file/process/native boundaries with the same identity and ownership discipline.
- Reject corrupt persisted state explicitly rather than treating it as an authorized default. Preserve independent [retention domains](../../Design/Security_Data_Flows.md#retention-defaults), protected live/uncertain work, private storage and deletion semantics; retained evidence is not fresh approval.
- Keep user content, audio, secrets and credentials out of diagnostics, audit payloads and activity names/tags; preserve [trusted correlation and baggage restrictions](../../Design/Architecture.md#activity-tracing-and-evidence-correlation).
- Keep unqualified capabilities explicitly disabled or unavailable. Deterministic fakes prove implementation behavior, not provider/hardware/native enablement. This guidance does not authorize a new security product or live security/settings rollout.
- Add [adversarial behavioral tests](testing.instructions.md) for revocation, stale/cross-session inputs, uncertain outcomes and absence of forbidden effects. Do not substitute reflection/source checks or reassuring exception text for boundary behavior.
