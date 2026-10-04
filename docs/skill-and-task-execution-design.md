# Skill and task execution design

This page specifies requirements for future stored skills and user-approved
application and script execution. **These execution features are not available
in the current release.** Today, model grants apply only to named built-in
actions; they do not authorize a file, program, script, or skill to run.
The same content-bound rule applies to a standalone application the user asks
Kora to launch, even when no skill is involved.

## Built-in commands and the trusted host

**Proposed architecture, not current behavior:** implement user-visible,
side-effecting tasks as versioned `.ps1` resources wherever practical.
PowerShell 7 is already tracked as a required, separately consented setup
dependency; installing it does not approve any script or task. The execution
runner and script-bound grants described below are still planned.
Package built-in task scripts and skill/tool scripts with Kora as embedded
resources; store user-created skills and their scripts as local files. A
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
when an unrelated application update would needlessly revoke them.

Before migrating a current side-effecting built-in, identify whether it is
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
syntax highlighting, the exact script bytes to be executed, and full hashes
and invocation details. Reviewing is optional, but the approval card remains
visible until the user explicitly approves or rejects; opening or closing the
review window is not approval. If a script changes while the review is open,
invalidate the card and require a fresh review. Do not log script contents or
sensitive argument values.

Compare actual content, not the Kora version string: a new application
release that changes an embedded `.ps1` invalidates that task's grants, while
an unchanged script with the same task identity, invocation, and dependency
set may retain its always grant. An updated user-created file follows the
same rule. Changes to a manifest, parameters, execution context, or a
dependent executable require reapproval even if the top-level `.ps1` hash is
unchanged. The user can inspect an old grant as invalidated, but cannot
silently transfer it to the replacement content.

## Skill management

A skill must have a stable local identity, a manifest describing its declared
tasks and executable resources, and an explicit list of every program and
script that a task can launch. Installing, importing, editing, updating, or
removing a skill must never carry forward execution permission for changed
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

## Execution grants

Grant keys must identify the precise task, invocation, and executable resource
set, including the SHA-256 digest for **each** executable or script. Once,
session, and always are duration choices, not permission to trust future
versions of the files. An always grant survives a restart only while the same
approved bytes and invocation remain available. A different digest, missing
file, unreadable file, different resolved target, changed manifest or invocation,
or unverifiable dependency makes the previous grant **inapplicable**; Kora must
not silently rebind it to a new digest. A renamed or relocated executable also
requires review even when its bytes match. No grant may be scoped merely to a
directory, file name, extension, publisher, skill name, or script interpreter.
Once Kora observes a mismatch, it must revoke the affected session and stored
grants; reverting the file to its old bytes must not resurrect that approval.
For example, if the user previously allowed Kora to launch an application and
its binary is replaced by an updated build, the old approval must be ignored
and revoked. Kora must ask again for the new binary's hash before launching
it; approving the application name alone is insufficient.

Grant management must show the approved digest (or a readable abbreviation
with access to the complete digest), resource identity, task, scope, and whether
the grant is current or invalidated. A user can remove an obsolete grant, but
cannot edit its digest to approve replacement content without the normal
confirmation flow. If a script in a stored skill is modified, **all grants
covering tasks that execute it become inapplicable**, including session and
always grants. Other tasks' unrelated grants remain valid.

## Task execution gate

Before each execution, the host resolves and validates the complete target
set, computes current hashes, and compares them with the approved grant.
Checks must happen at execution time, not just when the skill is loaded, the
grant is created, or a file watcher reports a change. Missing, unreadable,
invalid, or changed resources fail closed: do not start any part of the task;
show which resource changed and request a fresh, exact approval. Revoke the
cached and persisted grants for the affected task and audit the denial without
recording script contents or sensitive arguments. If revocation cannot be
persisted, continue to deny execution and report the storage failure rather
than allowing a stale grant to authorize a later run.

The execution mechanism must prevent a file being swapped between hashing and
launch: hold stable file handles or run a verified immutable copy in a
Kora-controlled staging area, and launch **those verified bytes**. For
interpreted scripts, stage and verify both the script and any included or
invoked executable files; no unchecked relative imports, child scripts, or
process launches may bypass the gate. If a task can dynamically select an
unlisted executable resource, stop and require a new review rather than
assuming its old grant covers it. An executable's digest alone does not
authorize arbitrary arguments, working directories, privileges, or network
access.

PowerShell is **not a security sandbox**: hashing a top-level `.ps1` does not
prove what arbitrary commands, modules, network calls, or child processes it
may run. The runner must restrict what task scripts can invoke and verify all
permitted dependencies; if that cannot be enforced, present the broader
execution capability to the user instead of claiming a narrow hash grant
covers it. Do not trust a writable extracted copy of an embedded script:
extract into a controlled staging location, verify against the embedded bytes,
and execute only that verified copy.

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
- Editing a shared script invalidates every dependent task grant but not
  unrelated task grants; a manifest or invocation change also triggers review.
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
- Updating an embedded script in a new Kora build revokes its old grants;
  updating unrelated scripts does not revoke unrelated task grants.
