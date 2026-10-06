# Skill and task execution design

This page specifies requirements for future stored skills and user-approved
application and script execution. **These execution features are not available
in the current release.** Today, model grants apply only to named built-in
actions; they do not authorize a file, program, script, or skill to run.
The same content-bound rule applies to a standalone application the user asks
Kora to launch, even when no skill is involved.

The [tools and built-in skills guide](tools-and-built-in-skills.md) lists the
design-defined capabilities and clearly labels current versus planned behavior.

## Built-in commands and the trusted host

Commands, internal tools, skills, and executable tasks are distinct:

- A **command** is a voice/typed entry point identifying what the user wants.
- An **internal tool** exposes a registered Kora operation, such as querying
  session state, with typed inputs and structured results. It need not be a
  skill or script.
- A **skill** describes an outcome, when to select it, required inputs, and
  instructions/workflow referencing admitted tools and tasks.
- An **executable task** implements an effect through exact registered scripts
  or native adapters and a permitted invocation. This is what an execution
  grant authorises; selecting or enabling its skill does not.

"Built in" describes ownership/source, not a requirement to implement every
capability as PowerShell. Internal state queries remain in host services and
return data independently of window/speech presentation. The bundled lock
skill instead describes an explicit current-session lock request and
references its registered script-backed task.

**Proposed architecture, not current behavior:** implement suitable
user-visible, side-effecting tasks as versioned embedded `.ps1` resources.
PowerShell 7 is already tracked as a required, separately consented setup
task, independent of local inference and the current C# built-in handlers;
installing it does not approve any script or task. The execution
runner and script-bound grants described below are still planned.
Package each built-in skill's manifest, Markdown instructions, and complete
script set with Kora as embedded resources. A skill can require multiple
`.ps1` files, and several skills can reference the same embedded helper.
Slice C user authoring remains declarative; local user-created
scripts belong only to a separately admitted future execution capability,
not the current release or MVP authoring permission. A
built-in command can select a registered task, but does not bypass the same
grant gate used for a model suggestion or a user-created skill. Read-only
commands (such as help, version, status, and viewing grants) do not require
execution approval. A direct change made through Settings is itself an
explicit user interaction; it is not silently treated as a blanket script
grant.

Keep the **trust boundary and essential application infrastructure in C#**:
voice and typed input, speech, windows and response controls, the command/task
registry, local-model request parsing, task queues and cancellation, dependency
readiness, persistence/SQLite, audit logging, grant decisions, hash
calculation, review-window content, process supervision, and the final
execution gate. C# must resolve task identities and dependencies, gather
user consent, and launch PowerShell only after checking an exact grant. A
script must never declare itself approved, update its own grant, select a
weaker scope, or be invoked directly from unvalidated model text. Emergency
controls such as stopping speech, cancelling a task, and exiting Kora must
remain available even if PowerShell, a skill file, or the task runner fails.
Purely in-process UI changes should stay in C# rather than launch a new
PowerShell process just to show or hide a window. Use a small audited native
adapter where a Windows API must be called directly (for example, locking the
session); it still needs an action-specific approval when it is a
side-effecting task. A native side-effecting task must also have a
version-bound implementation identity (for example, the SHA-256 of a dedicated
adapter assembly plus the task's invocation contract), so an always grant
cannot silently authorize changed C# behavior solely because the action enum
name stayed the same. Avoid binding all native grants to Kora's entire binary
when an unrelated application update would needlessly make them inapplicable.

The current built-in actions use C# handlers, not embedded `.ps1` skill
definitions. Model-suggested disruptive actions have only action-name grants;
there is no script or executable hash binding and no script-review window
today. Before migrating a current side-effecting built-in, identify whether it is
currently direct and unconfirmed. For example, the existing exact lock
command calls the Windows API directly, whereas model-suggested locking
requires approval. The future common execution gate must apply to **both**
paths; a built-in phrase must not become an approval bypass. An explicit
first-time approval may be granted once, for the session, or always for the
exact task version. Rejecting or dismissing it performs no effect. Safety
controls (cancel, stop, exit) remain available without a script dependency.

## App -> model -> app interaction

**Proposed, not current behavior:** voice is transcribed locally and typed input
supplies text directly; both enter the same request-routing contract.
For semantic interpretation, Kora provides the model with the relevant
available tool definitions and enabled skill summaries, including purpose,
selection guidance, inputs, source/revision, and dependency availability.
The model proposes a typed tool invocation or skill selection. Kora resolves
the selected pinned workflow and registered tasks; it does not execute script
text produced by the model.
Approved selected instructions/tool references return to the task runtime for
next-step reasoning, or declarative steps run through the bounded host workflow
engine and the same invocation gate. Sending skill summaries/instructions to a
remote model obeys context-egress policy; discovery alone does not permit it.

```text
User request -> Kora request/context and capability definitions -> Model
Model tool/task proposal -> Kora validation and exact grant/approval gate
Kora execution -> Observed structured result -> Model continuation
Model answer -> Kora visual/speech presentation under privacy policy
```

For example, a session-status tool returns observed listening/activity/work
state for the model to explain; querying does not approve a pending action.
A lock skill identifies the intended outcome and references its exact
registered task/script. Kora, not the model, determines the executable bytes,
targets, parameters, effect classification, and applicable grant.
Script source is available for user review but is not required in model
context to select the skill.

Approval is a separate host-owned interaction bound to the proposal.
Clarification is not approval, and approval replies are not passed to the
model as permission to invent or broaden grants. Rejection or dismissal
performs no effect. Selection, dependency setup, and opening script review
never implicitly approve execution.

Tool/task results distinguish success, failure, denied, cancelled, and unknown
outcomes and include invocation identity, observation time, and any receipt.
Return only policy-approved bounded data to the task runtime; remote result
transmission requires its own egress assessment. The model may then answer or
propose another checked step. A returned action name or successful script exit
is not proof that Windows performed the requested effect.

Exact registered phrases and essential host controls retain a local route
without model inference. A direct phrase or UI task invocation still reaches
the same applicable task/grant gate and can present the result deterministically.
An unavailable model disables semantic interpretation, not exact local status,
stop-speech, cancellation, or essential lifecycle controls.

Today Kora instead uses a one-response JSON action selector with a limited
status snapshot and C# handlers; it does not feed structured tool results back
into a continuing model/tool loop. This interaction contract does not advertise
that loop, skill discovery, or script execution as available today.

## Script review and versioning

An approval card for a script-backed task should show the task name,
origin (embedded or user-created), requested effect, script identity, full
SHA-256 digest, argument and dependency summary, and requested grant scope.
Offer **Review script** to open a separate read-only window with PowerShell
syntax highlighting, every script's exact execution snapshot, the ordered
script inventory, individual hashes, combined script-set hash, and invocation
details. Mark shared helpers explicitly. Reviewing is optional, but the
approval card remains visible until the user explicitly approves or rejects; opening or closing the
review window is not approval. If a script changes while the review is open,
invalidate the card and require a fresh review. Do not log script contents or
sensitive argument values.

Compare actual content, not the Kora version string: a new application
release that changes an embedded `.ps1` invalidates that task's grants, while
an unchanged complete script set with the same skill definition, task identity,
invocation, and dependencies may retain its always grant. An updated
user-created file follows the same rule. Changes to a manifest, Markdown,
parameters, execution context, or a
dependent executable require reapproval even if the top-level `.ps1` hash is
unchanged. The user can inspect an old grant as invalidated, but cannot
silently transfer it to the replacement content.

## Skill management

A skill must have a stable source-qualified identity and a host-validated
descriptor of its tasks and complete declared executable resource set, with
best-effort tracked transitive dependencies and discovery gaps. For built-ins,
an embedded manifest explicitly identifies the Markdown resource, entry point,
all required `.ps1` resources, and admitted dependencies; shared scripts are
embedded once and referenced by each dependent manifest. For future
user-provided executable skills, the manifest is implicit in the selected
folder and normalised by the host, not a required extra file. The existing
declarative authoring design remains separate from that future import feature.
Installing, importing, editing, updating, or removing a skill must never
carry forward execution permission for changed
content. A skill document or model suggestion cannot grant itself permission.

For each executable resource, record the canonical location, its role (program,
script, or interpreter), and a SHA-256 digest of the **bytes to be executed**.
Include every manifest-listed script and required launcher/interpreter/adapter
in the task's approved resource set. Track other scripts/modules/binaries best
effort; do not claim the declared set includes every runtime-transitive action.
Show known identities and unresolved references for informed user approval.
A changed manifest or task definition also requires a fresh
review of the affected task, even if its script bytes did not change. Do not
accept a skill-provided digest as proof: Kora must calculate it from the local
files itself. Skill identity, task identity, resource identity, digest, and
the permitted invocation (including arguments and execution context) must be
visible to the user before approval.

### Combined script identity

A script-backed skill's grants bind the combined content of **all** declared
`.ps1` files, including explicitly declared transitive and shared helpers, not just its entry
point. Keep individual digests as well. All script-backed tasks in a skill
bind that complete set even when an invocation uses only some of its scripts.
Hashing order is independent of helper execution order.

The proposed `Kora.ScriptSet.v1` encoding sorts full canonical logical file
names ordinally. Built-in script names use lower-case ASCII, `\` separators,
and no absolute paths, traversal segments, or aliases. Hash this sequence
with SHA-256:

```text
ASCII("Kora.ScriptSet.v1") followed by one zero byte
UInt32BE(script count)
for each script in ordinal logical-name order:
    UInt32BE(UTF-8 name byte length), then UTF-8 name bytes without a BOM
    UInt64BE(script byte length), then exact script bytes
```

Length prefixes distinguish file boundaries; plain concatenation is not
sufficient. Do not normalise line endings, strip script BOMs, trim whitespace,
or hash re-encoded text. Any file addition, removal, rename, or byte change
changes the combined identity. Review and execution use those same immutable
bytes. Resource IDs, invocation, and separately verified interpreter/program
digests remain part of the grant key, not covered by the `.ps1` content alone.

Manifest, Markdown, and fixture bytes have a separate definition digest using
the same framed encoding with domain tag `Kora.SkillDefinition.v1`; their
changes also require review even if script content is unchanged.
An application-version change alone does not revoke unchanged definitions,
resources, and contracts. A shared helper change revokes every dependent
skill/task grant, not unrelated skills. Grants never transfer between skills
just because they share a helper or script-set hash.

### Future folder-based dependency discovery

For future user-provided executable skills, derive the initial script set
from the bounded selected folder, then attempt PowerShell AST parsing to find
direct/transitive `.ps1` references, including dot-sourcing, call-operator
invocation, and supported static import/launch forms. Resolve only supported
literal/constant paths without executing code. An external helper requires
explicit approval of its bounded source scope. Include each canonical file
once; use stable source-qualified logical names for external dependencies.

Missing required declared/tracked files or unverifiable approved identities
block dispatch. Computed paths, dynamic evaluation, unsupported imports and
unresolved launches are disclosed discovery gaps, not an empty or complete
dependency list. Under the owner-approved
[best-effort tracking rule](../Design/Built_In_Skills.md#best-effort-transitive-dependency-tracking),
the granting user accepts responsibility for the script's overall actions
within its admitted scope; Kora cannot promise to detect every transitive
change. Recheck declared folder membership and tracked identities/bytes before
dispatch. This does not admit user scripts in the current release or through
declarative authoring, and cannot fall back to ambient-rights execution.

## Execution grants

Grant keys identify the precise task, invocation, complete declared script
set, required runtime/adapter identities and best-effort tracked dependencies.
Each declared or tracked code file has its own SHA-256; undiscovered code is
not represented as covered by that hash. Bundled review provides read-only
tabs for every manifest-listed file, including all scripts and shared helpers.
Scopes are **Once** (one exact invocation), **Session** (that operation within
the identified Kora work session), and **Always/Perpetual** (until explicitly
removed/edited or revoked by a changed approved content identity).
These scopes never authorize future versions of the files.
See [Grant Types and Inheritance](../Design/Security_Data_Flows.md#grant-types-and-inheritance)
for consumption, session end, and Active-session restart behavior.
For initial fixed lock, use the
[standalone control-session binding](../Design/Built_In_Skills.md#standalone-lock-work-session-binding):
commit/present a new Active session for an unaddressed request before
approval/dispatch; Session cannot mean process lifetime or selected window.
Perpetual grant records have no expiry, retention, or eviction policy and remain
independently of session/audit cleanup, even when inapplicable or revoked.
A different digest, missing
file, unreadable file, different resolved target, changed manifest or invocation,
or unverifiable dependency makes the previous grant **inapplicable**; Kora must
not silently rebind it to a new digest. A renamed or relocated executable also
requires review even when its bytes match. No grant may be scoped merely to a
directory, file name, extension, publisher, skill name, or script interpreter.
Once Kora observes a changed approved skill/script hash or definition digest,
it must permanently revoke every affected grant's authorization, including
Once, Session, and Always/Perpetual scopes. Persist the revoked state and
reason; reverting to the old bytes must not restore it. A new exact approval
creates a new grant, never rebinds or reactivates the revoked one.
Missing, unreadable, or unverifiable resources still block dispatch pending
verification and review. Perpetual records remain visible as revoked or
inapplicable until explicit user removal; record retention is not authority.
For example, if the user previously allowed Kora to launch an application and
its binary is replaced by an updated build, the old approval must be ignored
and shown as inapplicable. Kora must ask again for the new binary's hash before launching
it; approving the application name alone is insufficient.
Whether restoring old standalone application bytes can restore applicability
is an unresolved **deferred R27 decision**, due before that capability is
admitted. It does not block initial read-only or fixed bundled capabilities.
Do not apply this open question to bundled skill/script content: its observed
content change permanently revokes authorization even if old bytes return.

Grant management must show the approved digest (or a readable abbreviation
with access to the complete digest), resource identity, task, scope, and whether
the grant is current, inapplicable, or revoked. A user can remove an obsolete grant, but
cannot edit its digest to approve replacement content without the normal
confirmation flow. If a script in a stored skill is modified, **all grants
covering tasks that execute it are revoked**, including session and
always grants. Other tasks' unrelated grants remain valid.

## Task execution gate

The host also applies the default-On
[in-call grant-ignore policy](../Design/Call_Aware_Speech.md#ignoring-reusable-grants-during-calls).
During protected calls, Session/Perpetual grants are preserved but cannot
authorize execution; require fresh single-use approval for each exact invocation.
Revalidate call/setting policy generation at dispatch, including every queued
or background step; changing feedback/speech preferences does not bypass it.

Before each execution, the host resolves and validates the complete declared
target set and required/tracked identities, computes current hashes, and
compares them with the approved grant.
Checks must happen at execution time, not just when the skill is loaded, the
grant is created, or a file watcher reports a change. Missing, unreadable,
invalid, or changed resources fail closed: do not start any part of the task;
show which resource changed and request a fresh, exact approval. Revoke
authorization for changed approved skill/script content or definition hashes;
otherwise block unverifiable grant applicability. Audit the denial without
recording script contents or sensitive arguments. If the revocation or blocker
cannot be persisted, continue to deny execution and report the storage failure rather
than allowing a stale grant to authorize a later run. This does not delete or evict
the perpetual grant record.

The execution mechanism must prevent a file being swapped between hashing and
launch. For filesystem-sourced programs or user-created scripts, hold stable
file handles or launch an immutable, verified snapshot under host control.
Bundled scripts must instead run from their verified embedded-resource
snapshot through a controlled interpreter input mechanism: do not extract
editable copies for execution. If the selected interpreter requires a loose
script file, leave that task unavailable until a safe execution mechanism is
demonstrated. For interpreted scripts, verify the complete declared set and identified
approval-relevant dependencies. Transitive discovery/tracking is best effort,
not a promise to deny every runtime-selected dependency. Observed changes
invalidate applicable grants; disclose unresolved references and the user's
responsibility for transitive script actions. This is not permission to select
a different top-level task or bypass host capability/resource controls.
An executable's digest alone does not authorize arbitrary
arguments, working directories, privileges, or network access.

PowerShell is **not a security sandbox**: hashing even the complete declared
script set does not prove all commands, modules, network calls or children it
may run. The 2026-10-06 decision accepts best-effort dependency tracking, not
universal exact-dependency enforcement, for both bundled and future user
scripts. The runner must still enforce its separately required OS/resource/
privacy boundaries. If those cannot be enforced, leave the affected profile
unavailable; acknowledgement of script responsibility is not a substitute.
An explicitly exact-dependency profile needs real additional enforcement
proof and may not describe these best-effort grants as its admission evidence.
Do not trust a writable extracted copy of an embedded script; the
verified embedded snapshot must be the source of execution.

The local model may propose a registered task and explain it, but it cannot
declare a hash, choose its own grant scope, modify a stored grant, or invoke an
unchecked process. Kora must resolve the task and resources, present the
specific action and scope to the user, enforce the approval gate, and dispatch
only through its host-owned task runner. These rules also apply when a task is
invoked through an exact built-in command, not just through the model.

### Windows worker feasibility and next steps

The [R02 proof](../experiments/r02-containment-proof/evidence/README.md)
demonstrates that a fixed embedded PowerShell input can execute without a loose
script file under a capability-free AppContainer. It does not implement the
bundled catalogue, multi-script helper contract or grant gate above.

Same-user PowerShell with only a Job Object is rejected as restricted execution:
it actually reached protected stand-ins and the host's synthetic credential.
AppContainer plus a kill-on-close job is the partial filesystem/credential/
lifetime candidate, not an executable allowlist. Normal children were allowed;
required network-denial probes timed out and remain Unknown.

The [canonical continuation gates](../Design/Security_Data_Flows.md#windows-containment-continuation-gates)
and [roadmap W1-W4](../Design/Implementation_Roadmap.md#r02-windows-containment-follow-up)
require attributable OS network denial, a reviewed fixed-control mechanism,
protected required runtime resolution and independently protected app/worker deployment before R11
restricted dispatch. A typed native broker is only an alternative for an
explicit decision; it is not a selected implementation or permission to replace
the embedded-script contract silently. The current direct C# lock remains
bootstrap behavior, not acceptance of the future script-backed path.

The [W2 fixture](../experiments/r02-w2-dependency-proof/README.md) separately
demonstrates fixed embedded helper/entry execution and an owned marker, while
rejecting ACL/no-child policies as exact transitive dependency controls.
The owner-approved best-effort tracking rule applies to both bundled and
future scripts; complete manifest resources remain exact, but discovery gaps
and possibly undetected changes are disclosed. This amendment does not pass
network, installed protection, real Windows effects or complete runner admission.

Cancellation, deadline, process exit and effect certainty are separate facts.
Retain Unknown when an effect may have occurred without a valid correlated
receipt, including worker loss or malformed output; terminating its tree does
not prove rollback or authorize an automatic retry.

## Required verification before enabling execution

- Editing one byte of a granted script or application denies the next run for
  once, session, and always scopes; reapproval applies only to the new digest.
- Replacing a previously approved standalone application's binary denies its
  next launch even if its name and path have not changed.
- Editing a shared script invalidates every skill/task grant bound to a set
  containing it, but not unrelated skills' grants; a manifest, Markdown, or
  invocation change also triggers review.
- A missing file, changed resolved path, symlink swap, unreadable resource, or
  hash failure denies execution without starting a process.
- An update between verification and launch cannot execute unverified bytes.
- Restarting Kora reloads an always grant only for the original digest and
  invocation; a grant cannot be restored by silently changing its stored hash.
- The confirmation UI names the exact task and changed resource; rejection and
  dismissal leave execution blocked, including for voice-only interactions.
- Exact built-in voice/typed phrases, UI task invocations, skill workflows,
  and model suggestions reach the same approval gate for side-effecting tasks;
  read-only commands still work without
  PowerShell or an execution grant, subject to data-access/privacy policy.
- Model discovery describes tools and enabled skill revisions without granting
  them; missing or unknown task/tool identities never become arbitrary script
  or process execution.
- Status queries return authoritative structured data without UI side effects;
  model-mediated results are returned only after the applicable egress checks.
- Skill selection and tool/task dispatch cannot execute an effect twice;
  cancellation, denial, and unknown outcomes never trigger an automatic write retry.
- The review window exposes every manifest-listed file in named read-only
  tabs, with a separate syntax-highlighted tab per script/shared helper.
  It never modifies source or confirms approval merely by opening. Show
  best-effort transitive inventory/gaps and user responsibility.
- Updating an embedded script revokes authorization for every grant bound
  to its old script set, without deleting perpetual records; unrelated grants
  remain unaffected. Reverting the bytes never restores a revoked grant.
- A single-use grant authorizes exactly one invocation; a session grant
  authorizes only that operation in its bound Active session and ends on Done/deletion.
- Perpetual records survive elapsed time, session/audit cleanup, restart, and
  storage pressure without retention/eviction. Changed skill/script hashes
  permanently revoke authorization while preserving the old record and reason;
  only a new exact approval grants access again.
- Every built-in manifest, Markdown document, and required script is present
  in the published embedded-resource catalogue; shared helpers are embedded
  once and cannot resolve from writable files.
- Combined hashes are stable under enumeration/list-order changes and differ
  for file additions/removals/renames or byte changes; length-framing and
  exact-byte test vectors agree across implementations.
- Future folder imports prove best-effort transitive discovery, source-scope
  checks, honest dynamic-reference gaps, observed-change revocation and
  race-resistant declared/tracked snapshot execution before enablement.
