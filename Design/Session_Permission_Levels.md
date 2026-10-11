---
title: Session Permission Levels
status: Proposed feature; product decisions agreed, implementation and qualification outstanding
date: 2026-10-10
---

## Purpose and Authority

Let the user choose how Kora authorizes otherwise ungranted actions in one identified Kora work session: **Review all**, **Model review**, or **Approve all**. Apply the same decision path to direct commands, native UI actions, model tools, MCP calls, generated scripts, stored scripts, and script-backed skills. A permission level is session policy, not a permission grant.

This document owns the proposed level semantics, reviewer protocol, and call-time approval-channel behavior. [Grant types and inheritance](Security_Data_Flows.md#grant-types-and-inheritance) continues to own exact grant applicability/lifetimes; [interaction and sessions](Interaction_And_Sessions.md#structured-questions-and-mixed-channel-replies) owns question routing and shared voice/UI drafts.

The existing [exact authorization service](../src/Kora.Application/Interaction/HostAuthorizationService.cs) and [operation binding](../src/Kora.Core/Authorization/ExactOperationBinding.cs) are implementation seams, not evidence that these levels exist. This design does not enable scripts, MCP transports, or model execution that have not passed their independent admission gates.

## Agreed Product Decisions

- New sessions start at Review all. Never inherit a level from another session, selected window, provider conversation, skill, or parent task.
- Persist an explicitly selected level for the same Active work session across application restart. Done atomically resets it to Review all; resuming that session does not restore its former level. Deletion removes its policy state, not independent Always grants.
- Use a separate, tools-disabled reviewer call for Model review, not the executing model's self-assessment. Concern, uncertainty, invalid responses, and reviewer failures require user approval.
- Outside calls, deliberate voice and native UI can approve Once, This session, or Always. Mandatory authentication remains independent of input channel.
- During protected calls, disable spoken approvals by default, configurable through settings. Keep existing grants and the selected level unchanged; calls do not force fresh approval or downgrade the level.
- Combine exact action approval and associated data-disclosure consent in one host-owned card when both describe the same operation. Record their authorities separately.
- Approve all skips discretionary action safety analysis and ordinary approval. It does not disable mandatory privacy, consent, identity, ownership, containment, authentication, audit, or prohibited-effect boundaries.

## Levels and Decision Order

An applicable grant takes precedence over the level. A stored Active record alone is not applicable: resolve current action/content, parameters, targets, identity, destination, transformation, grant scope, session generation, and host policy before considering it.

| Situation after mandatory validation | Review all | Model review | Approve all |
|---|---|---|---|
| Exact applicable grant covers the operation | Use grant; no action review | Use grant; no reviewer call | Use grant; no action review |
| No applicable grant; no independent consent is missing | Ask user | Review exact operation; Safe allows, Concern/Uncertain asks | Allow without discretionary safety analysis or ordinary prompt |
| Independent disclosure consent is missing | Combined approval/consent card | Combined card, even if reviewer says Safe | Combined card; level cannot supply disclosure consent |
| Mandatory gate fails or action is prohibited/unavailable | Deny or block with recovery | Deny or block with recovery | Deny or block with recovery |
| Approval required while spoken approval is disabled by call policy | UI approval | UI approval | UI consent/mandatory confirmation where required |

1. Resolve original-user intent and durable session/task/invocation identity. Clarify ambiguity before authorization; untrusted content cannot introduce new intent.
2. Resolve an immutable host-owned operation proposal and its implementation, resources, effect, parameters, identity, destination, transformation, deadlines, and policy revisions. Validate the execution profile and independent boundaries.
3. Observe relevant content changes and revoke affected exact authorization under the existing grant contract. Never restore old authorization merely because old bytes return.
4. Look up current applicable grants and separate required consent. Use a complete applicable grant without review or inference; partial authority cannot authorize the entire operation.
5. For missing action authority, apply the selected session level. A required consent/mandatory confirmation card takes precedence; do not make a redundant reviewer call when user confirmation is already unavoidable.
6. If asking, present one exact host-owned card and bind the answer to its proposal/question revision. A rejection, cancellation, expiry, ambiguous reply, or unavailable approval channel produces no authority.
7. Immediately before dispatch, atomically revalidate session/control generation, level revision, grant/consent revisions, proposal/content, accepted decision lineage, cancellation, and mandatory policy; commit required authorization/use audit and a single-invocation admission receipt. Call-based voice eligibility is checked at answer acceptance, not used to revoke a committed decision.
8. Dispatch only through the admitted host implementation; record observed success, failure, cancellation, denial, or unknown outcome. Return bounded results through the separate result-egress gate.

Read-only help, non-sensitive policy inspection, and deterministic stop/mute/pause controls retain their fixed grant-free policies. All effectful actions, including requested reads/capture, use the applicable gateway. An ordinary command, skill selection, or UI invocation expresses intent, not implicit approval; explicit host approval controls are distinct.

Host effect classification remains necessary to enforce prohibited effects, data handling, and action-specific OS/lifecycle constraints. Approve all does not invoke a discretionary safety classifier or model reviewer; it still validates those constraints. It accepts eligible high-risk actions without ordinary review, not arbitrary unregistered execution or a global grant.

## Grant and Consent Semantics

When user approval is needed, offer **Once**, **This session**, **Always**, and **Reject**, with no affirmative choice preselected:

- Once authorizes exactly one invocation and is consumed atomically.
- This session creates an exact operation-bound grant for the identified Active work session; Done/deletion ends it and resume does not restore it.
- Always creates an independent persisted exact grant with no expiry, retention, or eviction; changed approved content permanently revokes authorization without silently deleting its record.

Reuse the existing [exact grant contract](Security_Data_Flows.md#grant-types-and-inheritance); do not promote legacy action-name preferences to script/content-bound grants. Offer reusable scopes only when the actual operation can be represented exactly; never grant unspecified future files, effects, scripts, accounts, or destinations.

Automatic Safe and Approve-all admissions create **no grant** and do not edit, extend, or reactivate grants. Each produces a one-invocation admission receipt and audit only. Existing grant use retains normal consumption/use-count bookkeeping. Model-review concern followed by explicit user approval can create the chosen grant, exactly as in Review all.

The combined action/disclosure card shows exact sources or snapshots, transformation, destination/account, effect, parameters, script/dependency identities where relevant, and proposed duration. One explicit answer can authorize both, but action authorization and disclosure consent have separate typed bindings and audit identities.

Do not broaden disclosure merely because Always was selected. Show the exact reusable disclosure scope where policy supports it; otherwise identify disclosure as once-only even if the action grant is reusable. A later changed payload, source, or destination must pass consent again. An existing action grant with missing disclosure consent needs only the consent card, not a replacement action grant.

Microphone enablement, credential entry, OS/UAC/provider authentication, memory admission, and privileged lifecycle confirmations keep their independently required workflows. Action policy never supplies those authorities. [Runtime egress enforcement](Security_Data_Flows.md#runtime-egress-enforcement) still applies to reviewer/model, MCP requests, redirects, and every result status.

Grant creation/edit/removal and permission-level or call-approval-setting changes are protected control operations. They require explicit original-user confirmation and cannot be auto-approved by the level being changed or a reviewer verdict. Combined execution approval is the explicit authorized grant-creation path, not model self-authorization.

## Model Reviewer

Kora creates a separate review request with tools, MCP, shell access, skill loading, approval callbacks, and action execution disabled. The reviewer cannot continue the task or create requests/grants. Do not supply the executing conversation wholesale, use its instruction stack, or expose hidden model reasoning.

Supply only the bounded exact host-resolved operation and relevant allowed evidence:

- Canonical action/source/version and invocation; targets, affected data/destination/account, privileges, reversibility, and enforced execution constraints.
- Generated/stored script snapshots and complete declared script set, plus identified tracked modules/executables and unresolved dependencies.
- Minimal original-user intent and permitted source provenance; not ambient audio, credentials, grant-store contents, or unrelated sessions.

Treat script comments, tool descriptions, external content, and model-provided explanations as untrusted evidence, not reviewer instructions. Host framing and parsing own the protocol. Keep content outside instruction fields; no prompt text is itself a security boundary.

The proposed typed result is `Safe`, `Concern`, or `Uncertain`, plus bounded user-visible reason codes/summary and the host-issued review request identity. Kora binds the result to its own immutable proposal, not echoed model hashes or IDs. Reject unknown verdicts, oversized output, invalid schema, mismatched identity, expired results, or stale policy/content.

Only a valid **Safe** result can satisfy missing ordinary action authority, and only for that exact invocation. Safe is an advisory model judgment used by the host-selected policy, not proof of safety, a lower host risk class, independent consent, or a reusable approval. Display that distinction and the relevant review evidence in session history.

Concern and Uncertain lead to the normal approval card with the bounded concern. Reviewer unavailable, error, timeout, insufficient context, privacy-restricted evidence, or malformed response also leads to approval with an explicit failure/unavailability reason. Cancellation terminates the attempt instead of opening a late prompt.

Use the existing admitted provider selection, bounded inference/deadline/cancellation budgets, and local-only policy. No automatic hosted fallback, context truncation disguised as complete review, installation, quota escalation, or retry to seek a Safe answer. If the required evidence cannot fit the admitted review budget, report incomplete review and ask the user.

The reviewer uses separately admitted inference/context consent, not recursive action Model review. Obtain new disclosure consent before sending review evidence. A hosted reviewer cannot inspect an upload's file contents just to decide whether uploading them needs consent.

Do not cache Safe verdicts across invocations or reuse them for retries, child actions, changed scripts, or another session. Record reviewer/provider/version and protocol revision with the verdict, but never infer that review is guaranteed safe or authentication.

## Scripts, Commands, Tools, and MCP

All admitted routes converge on the same authorization decision; adapters cannot translate Approve all into an SDK permission callback that bypasses host controls. Compound workflows authorize each distinct effectful operation. Grants or verdicts for a parent step do not authorize a new tool, changed parameters, or expanded sources/destination.

For scripts, review the exact host-held bytes, not a skill name, generated explanation, or old editor version. Bind declared/identified dependencies, interpreter, arguments, working directory, resources, privilege, and destination. Changed bytes invalidate pending authority in every level; Approve all may authorize a freshly resolved eligible proposal, never the stale one.

Use immutable verified snapshots or protected handles to prevent replacement between resolution and execution. Follow the admitted runner's [dependency and execution constraints](../docs/skill-and-task-execution-design.md#task-execution-gate); best-effort transitive discovery is not universal mediation of every OS command a script can construct.

Model review cannot prove arbitrary scripts safe. Unknown effects/dependencies produce Concern/Uncertain or a mandatory admission denial. Approve all does not enable an unsupported runner. Native/resource/privacy restrictions remain enforced even when ordinary review is skipped.

For MCP, bind server/transport, tool/schema/version, canonical arguments, account, resources, destinations, and effect. Server text cannot assert grants or verdicts. Redirects, schema changes, new targets and follow-up calls require fresh resolution. Discovery, startup, authentication, transport traffic and result transmission keep their own admission boundaries.

## Voice Approval and Protected Calls

Propose the device-local Boolean setting **`calls.allowSpokenApprovals`**, default **Off**, displayed as **Allow spoken approvals during calls**. It controls spoken Once/Session/Always approval and associated disclosure consent, not voice activation, ordinary commands, feedback, or speech output.

A protected call includes manual I'm in a call mode, supported enabled Active/Suspected evidence, or enabled Unknown/stale/failed evidence. Teams is an example, not the only supported communication context. With no configured detector/manual state, report detection unavailable and retain normal input rules; do not claim an undetected call is known clear.

When Off during protection:

- Reject spoken approval submissions with a visible explanation; retain the exact pending card for UI approval. Do not consume a grant, change its duration, defer the rejected answer, or reinterpret it through the executing model.
- Continue using applicable existing grants and the selected session level. Safe/Approve-all admissions may run without user approval; blocking speech is not a global execution pause.
- Preserve normal voice commands where independently eligible. This setting does not disable listening, mute speech, or enable call-time capture.
- Reject voice-originated permission-level relaxation and voice/in-call-settings changes, including enabling this setting. Require a fresh UI request, not a click that relabels rejected voice origin.

When explicitly enabled, allow deliberate voice approval under the normal exact-target, readback, expiry, and action-specific confirmation rules. Enabling during a protected call requires fresh UI initiation and explicit downgrade confirmation. Changing feedback to Voice/Both or a speak-once exception does not enable spoken approvals.

Recheck call evidence and setting revision atomically at voice answer acceptance. Call entry invalidates an unaccepted voice answer when the setting is Off; it does not revoke an already committed exact grant or invalidate an already accepted user decision solely because the channel is now unavailable. Other content/policy/cancellation checks still apply before dispatch.

Call clearance never accepts rejected speech, answers a card, reopens capture, restores an ended grant, or replays work. UI approval remains available subject to independent unlocked/ownership/presentation policy. While locked, neither channel can approve.

This deliberately supersedes the proposed `calls.ignoreReusableGrants` behavior: calls restrict the approval channel instead of forcing Once or ignoring reusable grants. The [current manual-call implementation](Call_Aware_Speech.md#delivered-bounded-manual-mode---2026-10-07) remains unchanged until a separately tested migration is enabled.

## Session UX, Lifecycle, and Races

Show the effective level persistently in the compact session view and Sessions workspace, alongside the exact session name/ID. Show protected-call voice eligibility separately: **Spoken approvals disabled during call; UI available**. Never display a fourth effective level or silently replace Approve all with Review all.

The session selector offers three choices with explanations. Approve all carries a prominent warning: **Actions without grants will run without safety review or ordinary approval in this session. No grants will be saved. Independent consent and mandatory protections still apply.** Do not embed an affirmative control in model-authored prose.

Changes name the exact session and old/new level, require explicit original-user confirmation, and commit against its current revision. Allow deterministic voice selection outside restricted call conditions and equivalent native UI. Switching windows never changes another session's policy. Provider mode and permission level remain separate controls.

Keep policies in authoritative versioned session storage, not a profile-wide default or provider configuration. Validate known enum/schema and Active-session identity. A known older schema without the feature migrates explicitly to Review all; an unknown/corrupt saved value raises an explicit storage/policy error and blocks dispatch instead of silently selecting any level.

Level changes atomically increment the policy revision and retire undispatched admission receipts, reviewer results, and pending approval cards bound to the previous revision. Report stale interactions and re-evaluate when work is explicitly continued. Do not turn a previously rejected/expired/cancelled proposal into an automatic run merely because the user changes level.

Queued/background steps resolve the originating session's current policy at dispatch, never the foreground session's policy. Done/delete cancels pending reviews/approvals and blocks further admission; Done resets the level in the lifecycle transaction. Already dispatched effects use truthful cancellation/unknown handling, not claims of rollback.

Restart restores validated Active-session policy and applicable exact grants, not volatile verdicts, pending dispatch receipts, consumed Once grants, audio, or interrupted execution. Fresh user continuation re-resolves work. Grant revocation blocks subsequent matching use; Model review/Approve all may still authorize a fresh eligible ungranted proposal under their selected semantics.

Explain revocation versus execution policy clearly. **Revoke grant** is not **stop this action** while an automatic level remains selected. Keep deterministic pause/cancel accessible and offer Review all when the user wants future ungranted actions to require approval; never create a hidden deny-list from grant removal.

## Audit and Correlation

Full audit means every authorization decision and lifecycle outcome is correlated and inspectable, not that raw script, audio, payloads, secrets, or hidden reasoning are recorded. Extend the [trusted typed audit path](../src/Kora.Application/Interaction/HostAuthorizationService.cs) and versioned store schema; ordinary diagnostic properties or file mirrors cannot mint authority.

Record requested and terminal outcomes for:

- Level changes/reset, call spoken-approval setting changes, and rejected/stale control requests.
- Grant matching/use/miss/inapplicability/revocation and explicit Once/Session/Always decisions.
- Reviewer start/completion/failure, validated verdict or rejection, and exact proposal identity.
- Automatic level-based admissions, required consent decisions, denials, cancellation, and dispatch/outcome reconciliation.
- Voice/UI presentation and answer channel, call/setting revision and eligibility, conflicts/expiry, and associated consent authorities.

Use host-owned session/task/request/invocation/proposal IDs and generation/revisions, exact binding digest, decision basis (`existing-grant`, `user-once`, `user-session`, `user-always`, `model-safe`, `approve-all`, `mandatory-denial`), grant/approval/consent IDs when present, level/policy revision, reviewer identity/version and bounded reason codes.

Never invent a grant ID for an automatic admission or infer physical identity from voice/UI. Keep safe labels and digests rather than raw sensitive arguments, paths, scripts, transcripts, tool outputs, or credentials in diagnostic/audit fields. Authorized reviewed artifacts live in the separately governed session artifact store.

Use the layer ActivitySource and source-generated companion logging for implementation. Propagate admitted W3C context through asynchronous review/dispatch; use ActivityLink for deferred work and host-owned durable IDs for business correlation. Model/provider trace fields cannot select a session or grant authority.

Commit policy/grant/consent changes and required admission audit atomically before reporting success or permitting dispatch. Audit/store failure blocks admission; uncertain commits require inspection/recovery without replay. A committed dispatch decision is not an effect receipt; loss of terminal certainty remains Unknown.

## Architecture and Delivery

| Layer | Proposed responsibility |
|---|---|
| Core | Typed level/session policy, decision basis, immutable review request/result and approval eligibility; reuse exact binding and grant validation |
| Application | Shared authorization orchestration, protected level changes, reviewer adapter invocation, combined approval/consent, lifecycle and audit coordination |
| Tools / Definitions | Existing per-action effects and immutable definitions; no policy/grant/reviewer authority |
| Windows | Existing protected store/atomic transactions, admitted script/native mechanisms, device/call observations; no Windows APIs in portable policy |
| Desktop | Session selector/badges, exact approval cards/readbacks, settings and recovery; view models delegate policy decisions |

Implement at the existing authorization/inference/storage seams; do not build a second grant engine or duplicate domain validation. Production automatic admission needs a typed one-invocation authorization basis, not fake OperationGrant records or ordinary diagnostic success messages.

Delivery remains under D-008/R05 (policy/grants/audit), R10 (controls), R12 (session lifecycle), R08/R13 (admitted reviewer transport/budget), R15 (call channel policy), and R11/R16 (execution). Sequence:

1. Add deterministic policy/domain and session-storage migration with atomic lifecycle/control audit; keep new controls gated.
2. Wire Review all and exact grant lookup into every admitted route, including direct command/UI paths; add combined consent and channel-aware voice/UI approval.
3. Add level-based one-invocation admission and separate reviewer with bounded, egress-controlled protocol; exercise tools-disabled and failure behavior.
4. Migrate call approval policy deliberately, update native controls/explanations, and test old protection versus the newly enabled contract. Do not enable partial behavior with stale UI or old grant-ignore enforcement.
5. Enable each supported route only after its affected maintained acceptance tests and independent runtime/execution/consent gates pass. Document unsupported routes; no experiment becomes production authority.

## Required Acceptance

The maintained [session permission-level gate](Acceptance_Criteria.md#session-permission-level-gate) is normative. Cover the level/grant matrix for commands, UI, model tools, MCP and generated/stored/multi-script workflows; exact authority and mandatory denials; reviewer isolation and privacy; voice/call combinations; combined consent; races and lifecycle; truthful audit and unknown effects.

Tests use xUnit v3, AwesomeAssertions, fake reviewers/call observations/time/execution and disposable private stores. Real Teams/acoustic/runtime/runner acceptance remains separate from deterministic policy tests. Do not execute destructive scripts, reboot, upload private content, or make unapproved provider calls just to qualify this design.
