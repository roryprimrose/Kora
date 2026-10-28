# Skill and task execution design

**Execution unavailable:** the [fixed bundled implementation](../src/Kora/BundledFixedActionController.cs) is wired, but its [contained runtime](../src/Kora.Windows/Skills/WindowsBundledScriptWorker.cs) is unqualified.
Installing PowerShell, inspecting a package or selecting its instructions does not enable execution. General user-program/script execution remains deferred.

Named model-action preferences do not grant files/programs/scripts or bypass the [fixed package gate](../src/Kora.Application/Skills/BundledFixedActionInvocation.cs).
Voluntary lock/shutdown/restart use that gate without a legacy native fallback. See
[current skills](tools-and-built-in-skills.md#13-built-in-skills) and
[approval settings](settings.md#approvals).

This page distinguishes implemented-but-unqualified behavior from future general execution. The linked design documents
own schemas, algorithms, limits and qualification rather than duplicating them
in the user guide.

## Built-in commands and the trusted host

A command identifies intent, a host action performs a typed operation, a tool
exposes an admitted model contract, and a skill describes a reusable outcome
over those operations. Selecting/enabling a skill is not an execution grant.

The trusted C# host owns identity, routing, validation, review, approval, audit,
hashing, cancellation and process/resource supervision. Essential stop/cancel/
exit controls remain available without PowerShell or inference.
Exact commands, UI requests and model proposals must use the same execution
gate when that capability is delivered.

See [interaction and implementation rules](../Design/Commands_Tools_And_Skills.md)
and [canonical tools](../Design/Internal_Model_Tools.md).

### Script-preserving fixed-action bridge

The approved [D-013 design](../Design/Decision_Register.md#2026-10-28-accepted-script-preserving-host-bridge) retains actual reviewed PowerShell execution in a separate contained worker.
The [worker implementation](../src/Kora.Windows/Skills/WindowsBundledScriptWorker.cs) supplies separate original entry/helper blocks in memory, not writable extraction or silent native substitution. No installed contained profile is qualified, so execution remains unavailable.

The worker can request only an independently authorized existing fixed lock/shutdown/restart effect. Kora checks the exact package and invocation, current grant, mandatory confirmation/authentication, privacy and ownership, audit and required resource joins.
A script message or successful exit is not proof that Windows completed the effect; the host reports the actual observed or unconfirmed outcome.

The [native permission review](../src/Kora/NativeQuestionHost.BundledControl.cs) offers Once, This session and Always, or cancellation without a grant. Exact grants use native controls, including for voice-origin requests; they do not supply separate power consent.
An unaddressed request creates a fresh neutral control session. **Run in last reviewed control session** explicitly addresses the previous binding; the [controller](../src/Kora/BundledFixedActionController.cs) revalidates it instead of automatically recreating retired authority.
The [power owner](../src/Kora/ApplicationLifecycle/ApplicationLifecycleController.cs) requires separate original-channel confirmation, a 30-second visible cancellable countdown, physically joined resources and current one-use authority. Model suggestions only open source inspection; they do not start this workflow.

Bundled, Kora-owned and user/profile content obey the same host governance, but source discovery/enablement does not admit executable code. Skills and agents can propose only admitted operations; approving one does not blanket-approve its calls or other packages sharing a helper.
Model-generated scripts remain proposals, not executable authority. General executable imports remain separately deferred under [R27](../Design/Implementation_Roadmap.md#optional-and-deferred-work).

## App -> model -> app interaction

The future loop is:

```text
User request -> admitted capabilities/context -> model proposal
Host validation and exact admission -> observed structured result
Approved result -> model continuation -> host visual/speech presentation
```

The model cannot choose arbitrary executable text, targets, policy, grants or
destinations. Sending instructions/results to a remote model needs separate
egress admission. Clarification is not approval, and rejecting a proposal
performs no effect.
See [the full interaction contract](../Design/Commands_Tools_And_Skills.md#app---model---app-interaction).

## Script review and versioning

Review must show the exact task, permitted invocation, original entry/helper
bytes, individual hashes, complete script-set/definition identity, tracked
dependencies and disclosed discovery gaps. Opening source tabs does not approve
execution. The review and execution snapshots must be identical.

Changed verified resources retire old proposals and require fresh admission.
A native effect also needs a version-bound implementation, not just an unchanged
action name. See [immutable script admission](../Design/Built_In_Skills.md#script-admission-and-execution).

## Skill management

Bundled manifests/instructions/fixtures/scripts are immutable application
resources. Profile definitions remain separately source-qualified and untrusted;
user authoring is declarative, not permission to install arbitrary executables.
Save, enablement and execution each have distinct decisions.

### Local Markdown-reference skills

For supported single-file instruction-only shared skills and native owned instruction-only packages, the [readable-name invocation operation](commands.md#invoke-an-instruction-only-skill-by-readable-name) uses explicit native inspection, immutable source choices and the existing canonical reviewed local-model workflows.
Names grant no authority; reference/tool/executable routes remain separate ([host](../src/Kora/NamedSkillInvocationController.cs)).

A registered shared-profile skill can explicitly declare up to four local
Markdown references using the [v2 format](../Design/Skill_Storage.md#explicit-local-markdown-references-v2).
Listing a source does not read their contents or authorize use. Review the
displayed declaration, then choose **Authorize only the exact declared local
Markdown reads**. With a pending declaration, original typed
`shared skill review invocation` (or current-name activated voice) confirms
only those displayed local reads, not invocation.

Review the complete primary and reference contents, then separately enable
the revision, select an existing Active conversation, supply the original
request, review the invocation, and confirm. The existing `shared skill enable`,
`shared skill targets`, `shared skill target <conversation ID>`,
`shared skill request <original text>`, `shared skill review invocation` and
`shared skill confirm invocation` commands use the same review as native UI.
Enablement is memory-only, expires in two minutes and permits one local-only,
zero-tool request. Changed files, withdrawal, closed review or lost private
admission require a fresh review. The answer or truthful terminal is retained
with the unchanged request; no automatic replay occurs.

References must remain inside the selected package and be nonempty strict
UTF-8, at most 16 KiB each and 64 KiB including the primary. Missing, changed,
undeclared, nested or executable references are unavailable, not partially
loaded. The existing stricter complete-model-context limit also applies.
No scripts, tools, hosted disclosure, persistent enablement or installation
are authorized. Ordinary single-file skills and the separate reviewed
one-tool route retain their existing rules.

See [source storage](../Design/Skill_Storage.md),
[authoring](../Design/Skill_Authoring.md) and
[bundled package implementation](../src/Kora.Definitions/README.md).

### Combined script identity

The canonical [script-set encoding](../Design/Built_In_Skills.md#deterministic-script-set-hash)
uses ordinal logical names, domain separation, length framing and exact original
bytes. Do not trim, normalize line endings/BOMs or hash re-encoded text.
The separate definition digest covers manifest, instructions and fixtures.
A shared-helper change affects every dependent grant, not unrelated packages.

### Future folder-based dependency discovery

Future executable imports require bounded reviewed source scopes, immutable
snapshots and best-effort direct/transitive tracking. Computed/unresolved paths
are disclosed gaps, not proof of an empty or complete dependency set.
Missing/unverifiable required files block dispatch.

The granting user accepts responsibility within the admitted scope; that does
not waive containment/privacy/resource checks.
See [best-effort dependency policy](../Design/Built_In_Skills.md#best-effort-transitive-dependency-tracking).

## Execution grants

Exact grants bind operation, invocation, content/definition/resource/runtime
identities and applicable scope:

| Scope | Meaning |
|---|---|
| Once | One exact invocation |
| Session | That operation within its bound Active Kora work session |
| Always / Perpetual | Independently retained until explicit removal/edit or revocation; no time/retention/eviction expiry |

Changed observed approved skill/script/definition content permanently revokes
affected authorization; restoring old bytes cannot restore it. Retain the old
record/reason and obtain a new exact approval. Missing or unverifiable resources
remain blocked. History is not live grant authority.

Standalone application rollback is an unresolved deferred R27 decision, not
permission to rebind a changed binary. A name/path/publisher/interpreter alone
never approves arbitrary arguments, privileges, working directories or network.

See [grant scope and inheritance](../Design/Security_Data_Flows.md#grant-types-and-inheritance)
and [standalone lock session binding](../Design/Built_In_Skills.md#standalone-lock-work-session-binding).

## Task execution gate

The host must revalidate current identities, immutable bytes, permission,
privacy/ownership and required audit immediately before dispatch, not merely at
load/review time. Prevent swaps between verification and launch. Bundled scripts
use verified embedded snapshots, not writable extraction; if a safe mechanism
is unavailable, execution stays unavailable.

PowerShell is **not a security sandbox**. Hashes and best-effort dependency
tracking do not prove what code/children/network paths will run. Enforce the
qualified OS/resource boundary separately. Cancellation/process exit is not
rollback or effect certainty; Unknown never authorizes automatic write retry.

The proposed [session permission levels](../Design/Session_Permission_Levels.md)
use exact grants first, then the selected review level without creating grants
or bypassing mandatory controls. Proposed
[spoken-call approvals](../Design/Call_Aware_Speech.md#spoken-approvals-during-calls)
default off while retaining grants/levels. Current call grant suppression remains
until qualified migration.

### Windows worker feasibility and next steps

Historical AppContainer/Job Object and dependency trials qualify only their
recorded scope. Same-user PowerShell plus a Job Object is not restricted
execution; network timeouts are Unknown, not attributable denial.

Follow [containment gates](../Design/Security_Data_Flows.md#windows-containment-continuation-gates)
and [W1-W4 sequencing](../Design/Implementation_Roadmap.md#r02-windows-containment-follow-up).
No experiment or acknowledgement enables the production runner.

## Required verification before enabling execution

[Acceptance Criteria](../Design/Acceptance_Criteria.md) owns mandatory fixtures
and measurable gates. They include:

- exact route convergence and denial of unknown/invented operations;
- changed/missing/unreadable resources and verification-to-launch swaps;
- independent script-set/definition vectors and original published resource bytes;
- grant scope, permanent observed-change revocation and independent Perpetual retention;
- rejection/dismissal, stale review, cancellation/late output and required-audit failures;
- actual containment, identity/egress, native effect receipts and resource quiescence.

Deterministic tests do not substitute for required native/provider/installed
observations. Use the [live validation register](../Design/Deferred_Validation.md)
for remaining approved-trial prerequisites and preserved historical evidence.
