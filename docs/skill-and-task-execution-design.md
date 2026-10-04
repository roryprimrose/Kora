# Skill and task execution design

This page specifies requirements for future stored skills and user-approved
application and script execution. **These execution features are not available
in the current release.** Today, model grants apply only to named built-in
actions; they do not authorize a file, program, script, or skill to run.
The same content-bound rule applies to a standalone application the user asks
Kora to launch, even when no skill is involved.

## Built-in commands and the trusted host

**Proposed architecture, not current behavior:** implement suitable
user-visible, side-effecting tasks as versioned embedded `.ps1` resources.
PowerShell 7 is already tracked as a required, separately consented setup
task, independent of local inference and the current C# built-in handlers;
installing it does not approve any script or task. The execution
runner and script-bound grants described below are still planned.
Package each built-in skill's manifest, Markdown instructions, and complete
script set with Kora as embedded resources. A skill can require multiple
`.ps1` files, and several skills can reference the same embedded helper.
Store future user-provided skills and their scripts as local files. A
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
descriptor of its tasks and complete executable resource set. For built-ins,
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
For a task that uses several files, include every directly or transitively
executed script, launcher, interpreter, and executable in the task's approved
resource set. A changed manifest or task definition also requires a fresh
review of the affected task, even if its script bytes did not change. Do not
accept a skill-provided digest as proof: Kora must calculate it from the local
files itself. Skill identity, task identity, resource identity, digest, and
the permitted invocation (including arguments and execution context) must be
visible to the user before approval.

### Combined script identity

A script-backed skill's grants bind the combined content of **all** declared
`.ps1` files, including transitive and shared helpers, not just its entry
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

Missing files, cycles unsupported by the runner, computed paths, dynamic
evaluation, unsupported imports, and unregistered child-script launches block
execution with an explanation; they cannot produce a success-shaped partial
hash. Static parsing cannot prove arbitrary PowerShell behaviour, so the
runner must also deny undeclared script/module/process access. Recheck folder
membership, references, current bytes, and physical targets before each
dispatch. This feature does not make executable user skills available in the
current release or through the declarative authoring workflow.

## Execution grants

Grant keys must identify the precise task, invocation, and executable resource
set, including the SHA-256 digest for **each** executable or script.
Scopes are **Once** (one exact invocation), **Session** (that operation within
the identified Kora work session), and **Always/Perpetual** (until explicitly
removed/edited or revoked by a changed approved content identity).
These scopes never authorize future versions of the files.
See [Grant Types and Inheritance](../Design/Security_Data_Flows.md#grant-types-and-inheritance)
for consumption, session end, and Active-session restart behavior.
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

Before each execution, the host resolves and validates the complete target
set, computes current hashes, and compares them with the approved grant.
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
demonstrated. For interpreted scripts, verify included and invoked resources
as well; no unchecked relative imports, child scripts, or process launches
may bypass the gate. If a task can dynamically select an unlisted executable
resource, stop and require a new review rather than assuming its old grant
covers it. An executable's digest alone does not authorize arbitrary
arguments, working directories, privileges, or network access.

PowerShell is **not a security sandbox**: hashing a top-level `.ps1` does not
prove what arbitrary commands, modules, network calls, or child processes it
may run. The runner must restrict what task scripts can invoke and verify all
permitted dependencies; if that cannot be enforced, present the broader
execution capability to the user instead of claiming a narrow hash grant
covers it. Do not trust a writable extracted copy of an embedded script; the
verified embedded snapshot must be the source of execution.

The local model may propose a registered task and explain it, but it cannot
declare a hash, choose its own grant scope, modify a stored grant, or invoke an
unchecked process. Kora must resolve the task and resources, present the
specific action and scope to the user, enforce the approval gate, and dispatch
only through its host-owned task runner. These rules also apply when a task is
invoked through an exact built-in command, not just through the model.

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
- Exact built-in voice/typed phrases and model suggestions reach the same
  approval gate for side-effecting tasks; read-only commands still work without
  PowerShell or an approval.
- The review window shows the exact script to be run with syntax highlighting,
  does not modify it, and never confirms an approval simply by opening.
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
- Future folder imports prove transitive discovery, source-scope checks,
  dynamic-reference denial, and race-free snapshot execution before enablement.
