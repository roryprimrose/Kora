# Out-of-the-Box Skills and Session Policy

Status: proposed bundled-script design, not a shipped skill runner. The current
bootstrap implements exact direct Windows-API lock and model-suggested lock approval,
but not embedded `.ps1` skills, script review, or hash-bound execution grants.
See [future execution design](../docs/skill-and-task-execution-design.md).

Related: [Extensibility](Extensibility.md), [Skill Authoring](Skill_Authoring.md), [Skill Storage](Skill_Storage.md), [Security and Data Flows](Security_Data_Flows.md), [Acceptance Criteria](Acceptance_Criteria.md).

## Package Classes and Sources

Kora ships useful skills without requiring users to author or download them.

| Package class | May contain | Installation and modification |
|---|---|---|
| Bundled first-party skill | Explicit manifest, Markdown instructions, optional fixtures, and one or more registered PowerShell scripts, all embedded resources in a protected Kora application binary; scripts may be shared between skills | No user/agent editing or file replacement; updated only with the application through verified out-of-band maintenance |
| User-authored skill | Declarative manifest/instructions/workflows referencing admitted tools | Created/refined by voice in the dedicated user skill store; cannot create or modify executable scripts |
| Shared profile skill | Supported declarative/instruction content; executable requirements are not admitted in the MVP | Read-only source reference with explicit revision enablement; edited copies go to Kora's roaming store |

All sources obey capability checks, action policy, task identity, and auditing.
See [Skill Storage](Skill_Storage.md) for profile discovery and `%APPDATA%\Kora\Skills`.
A bundled package is not a blanket permission grant.
Its complete definition and every required script are embedded application resources, not separately deployed or writable skill files.
User skills may reference an admitted action but cannot change its executable implementation or acquire its special routing privileges.

## Built-In Skill Manifest

A built-in skill consists of a Markdown instruction document and its explicit
manifest, plus the complete set of `.ps1` files needed for a script-backed
action. One script is the entry point; other scripts can provide shared
functions or additional registered script operations. An instruction-only
skill may reference admitted tools without containing scripts; it has no
script execution grant of its own.

The manifest identifies:

- Schema version, stable skill ID, skill version, name, and description.
- The Markdown instruction resource and any data-only fixture resources.
- The host-registered action, entry-point script, typed parameter contract,
  and required capabilities.
- Every required `.ps1` resource, including transitive helpers and scripts
  shared with other skills. The list is explicit, not a folder search.
- Any helper initialisation order and permitted calls between scripts.
- Admitted interpreter, module, native-adapter, and program dependencies by
  host registration, not arbitrary executable paths or command lines.

Conceptual manifest for the lock skill:

```yaml
schemaVersion: 1
id: kora.session.lock
version: 1.0.0
name: Lock the machine
description: Lock the current interactive Windows session.
instructions:
  resourceId: Kora.Skills.Session.Lock.Instructions
  name: skills\session-lock\skill.md
execution:
  kind: bundled-script
  action: session.lock
  entryPoint: scripts\session\lock.ps1
  helperLoadOrder:
    - scripts\shared\session-control.ps1
  parameterContract: session.lock.v1
  runtime: powershell7
  dependencies:
    - kora.windows.session-control.v1
scripts:
  - name: scripts\session\lock.ps1
    resourceId: Kora.Scripts.Session.Lock
  - name: scripts\shared\session-control.ps1
    resourceId: Kora.Scripts.Shared.SessionControl
```

This is the proposed schema contract, not an implemented manifest API.
`parameterContract`, `runtime`, dependencies, and action names resolve through
host-owned registrations. Declarations request capabilities; none grant them.
The model receives the Markdown and admitted action description, not authority
to execute its code fences or choose a process.

`name` is a stable logical file name relative to the built-in catalogue,
not an installation path or an assembly manifest-resource name. Script names
use lower-case ASCII segments and `\` separators; reject absolute paths,
empty segments, `.`/`..`, alternate separators, and case-insensitive
collisions rather than silently normalising aliases. `resourceId` is the
explicit assembly resource identifier. Neither identity depends on a source
checkout, machine path, assembly enumeration order, or display name.

The entry point must occur exactly once in `scripts`. A resource cannot appear
twice under aliases in one script set. Every helper-load or script-call
reference must resolve to that set; every non-entry script must have a declared
role. Helper initialisation order is separate from hashing order: an
alphabetical hash must not determine execution order. Any execution-order or
call-contract change changes the approved definition.

### Sharing Scripts Between Built-In Skills

Embed a shared `.ps1` once in the application assembly and reference its same
logical name and resource ID from every dependent manifest. For example,
another session-control skill can reference
`scripts\shared\session-control.ps1` alongside its own entry point.
Do not copy shared bytes into per-skill resources or select helpers from the
user skill store.

The catalogue maintains a reverse index from resource identity to every skill
and task whose declared script set includes it. An edit to the shared helper
changes each dependent skill's script-set hash and revokes all grants bound
to those sets; skills with no dependency on it retain their grants.
Approving one skill does not approve another skill that happens to use the
same helper. Skill/task identity and permitted invocation remain part of the
grant key.

## Embedded Resource Storage and Integrity

Build every built-in skill's manifest, Markdown instructions, fixtures, and all required scripts into the Kora application assembly as embedded resources.
Use stable host-owned resource IDs and a build-generated catalogue binding skill ID, manifest/instruction resource IDs, complete script set, per-file digests, script-set hash, definition digest, and permitted action.
Resolve resources from the explicitly identified application assembly, never by scanning arbitrary assemblies, folders, PATH, profile roots, or plugins.
The assembly is part of the protected application binary deployment; this does not require choosing single-file publishing or a self-contained runtime.

At build time, reject unsupported schemas, duplicate identities, absent
resources, wrong resource kinds, unlisted script references, and invalid
entry points or dependency registrations. Inspect the published assembly, not
only project item declarations, to prove every manifest, Markdown file,
fixture, entry point, and shared helper is embedded and the catalogue agrees
with its final bytes. These skill resources are separate from the existing
embedded end-user documentation catalogue.

Read bounded resource bytes into an immutable invocation snapshot and verify the registered identity/digest before use.
The host recomputes digests from the actual embedded bytes; a manifest's
self-declared checksum is never evidence of approval or integrity.
User preferences can select an allowed skill or disable optional skills, but cannot supply replacement resource bytes, resource mappings, or script paths.
Inspect/help may display a read-only definition. Voice authoring cannot edit/delete a built-in skill.
A separately named declarative user adaptation never replaces the embedded original, acquires its executable trust, or shadows reserved controls.
Embedded skill changes require rebuilding and replacing the application through its verified maintenance channel; there is no independent built-in skill updater.

Do not deploy or extract editable manifest/script copies for discovery or execution.
Use an interpreter/worker that accepts the verified script snapshot directly through a controlled in-memory/input mechanism with fixed typed parameters.
If an engine requires a loose script file, choose a compatible execution implementation or mark the action unavailable; do not weaken this rule with a writable extraction cache.
Read-only display/exported text is never a source for subsequent execution.
Runtime engine binaries remain separately protected application prerequisites, not model-selectable executables.

For multiple scripts, the worker receives the verified set as separate
script blocks with stable logical identities, not one concatenated PowerShell
source string. A candidate in-memory runspace implementation initialises
declared helper definitions in `helperLoadOrder`, then invokes the entry-point
block with host-validated typed parameters. Helpers loaded before entry-point
execution must be definition-only; any side-effecting helper call occurs
after the common execution gate. Additional script calls resolve only through
the worker's host-controlled snapshot map and declared call contract.

Ordinary disk-based dot-sourcing, `&` invocation of a `.ps1` path, and imports
from a profile, working directory, or `$PSScriptRoot` are not a fallback for
built-ins. Shared scripts must use the admitted in-memory contract.
Before admitting the runner, prove helper scope, parameter binding, call
resolution, cancellation, and denial of undeclared file/process execution
under the actual worker containment. PowerShell runspaces and command
filtering alone are not a security sandbox. Failure of that proof leaves
the affected action unavailable; it does not justify extraction or ambient
PowerShell execution.

Embedding prevents ordinary skill-file editing, not patching/replacing an entire binary.
Protected deployment permissions and release-origin/provenance checks are still required; an embedded checksum alone cannot authenticate an assembly whose code/catalogue was also changed.
Initial official artifacts are unsigned, so canonical release origin, final-byte hashes, build provenance, and protected installed-file permissions provide traceability and tamper detection rather than Authenticode publisher authentication.
Modified or mismatched application provenance must be rejected by the trusted deployment/launch mechanism where it can be established, not merely checked by code inside an already compromised binary.
Source builds identify their separately trusted local build provenance and never claim to be official release binaries.
A user deliberately changing source and building another application, or an administrator bypassing deployment trust, is outside the integrity guarantee.
Kora must not claim that a locally owned open-source application is impossible for its owner to modify.

## Deterministic Script-Set Hash

Permission grants for a script-backed skill bind a SHA-256 hash of the
**combined bytes of every `.ps1` declared by its manifest**, including shared
and transitive scripts. Retain each file's SHA-256 as well for review,
resource verification, and explaining which dependency changed. A hash of
only the entry point, or of just the selected helper branch, is insufficient.
All script-backed tasks in that skill bind the complete set, even if one
invocation uses only a subset. Independent grant scopes require independent
skill definitions; they cannot silently omit declared scripts.

### Canonical Encoding: `Kora.ScriptSet.v1`

1. Validate the complete set and read each resource into the immutable
   snapshot. Reject missing/unreadable resources, duplicate identities, and
   configured size/count-limit violations before hashing.
2. Sort by full logical script name using ordinal comparison
   (`StringComparer.Ordinal`), not current culture, basename, manifest list
   order, or filesystem/assembly enumeration order. Built-in names already
   satisfy the lower-case ASCII rules above.
3. Encode the following binary sequence:

   ```text
   ASCII("Kora.ScriptSet.v1") followed by one zero byte
   UInt32BE(number of scripts)
   for each script in ordinal name order:
       UInt32BE(length of UTF-8 logical name in bytes)
       UTF8(logical name), without a BOM
       UInt64BE(length of script content in bytes)
       exact script content bytes
   ```

4. Apply SHA-256 to the whole sequence. Store the encoding version and
   lowercase hexadecimal digest with the grant.

Integers are unsigned, big-endian byte lengths/counts. Length framing makes
file boundaries unambiguous: naive concatenation could make `ab` + `c`
indistinguishable from `a` + `bc`. Hash original bytes, not decoded/re-encoded
text, AST output, or a concatenation of hexadecimal per-file hashes.
Do not trim whitespace, strip a script BOM, normalise line endings, or remove
comments. Renaming, adding, removing, or changing one byte of a declared
script changes the hash. The hash uses the same byte snapshot as review and
execution; text decoding for PowerShell must preserve the content or reject
an unsupported encoding, never silently substitute characters.

The manifest above hashes `scripts\session\lock.ps1` before
`scripts\shared\session-control.ps1`, regardless of YAML list order or helper
initialisation order. A second skill including the shared helper hashes it
with that skill's own complete set; it does not reuse the first skill's grant.
Instruction-only skills do not acquire a script grant by hashing an empty set.

Fixed encoding test vector (synthetic content, not executable PowerShell):

| Logical name | Exact content bytes (hex) |
|---|---|
| `scripts\a.ps1` | `61 62` |
| `scripts\b.ps1` | `63` |

The framed input is 75 bytes and its SHA-256 is:

```text
57e8d8ad0c7a993252c17a068caecc526f06e48d8ae9be7f24ce974f334ff9b7
```

### Definition Identity and Grant Key

The script-set hash deliberately excludes Markdown and manifest bytes.
They are nevertheless approval-relevant: the instructions explain behaviour
and the manifest defines entry point, mappings, ordering, and invocation.
Compute a separate definition digest from the exact manifest, Markdown, and
declared fixture bytes. Use the same sorted, length-framed file encoding,
with domain tag `Kora.SkillDefinition.v1` instead of `Kora.ScriptSet.v1`.
The catalogue supplies the manifest's own stable logical name; the remaining
definition-file names come from its validated resource declarations.
Keep script resource mappings in that definition, so remapping unchanged
bytes to a different resource is not silently approved.

The host-owned grant key binds at least:

- Source partition, stable skill ID, task/action ID, and definition digest.
- Script-set encoding version and combined hash, plus the canonical per-file
  resource identities and digests.
- Entry point, parameter/call contract, allowed arguments, execution context,
  and required capabilities.
- Separately verified interpreter, module, adapter, and executable identities
  and digests. The `.ps1` hash does not cover these binaries.
- Relevant grant/policy schema revision and user/identity scope.

Once, session, and always describe duration, not trust in future script
versions. Markdown-only or manifest-only edits leave the script-set hash
unchanged but invalidate the definition-bound grant and require review.
These scopes follow the [work-session grant contract](Security_Data_Flows.md#grant-types-and-inheritance):
Session binds the identified Kora work session, not the process lifetime.
Revocation removes authorization, not the independently retained perpetual
record; that record must show its revoked state and content-change reason.
Reordering YAML script declarations preserves the script-set hash but still
changes the exact manifest bytes and therefore requires definition review.
A manifest version bump is not a substitute for checking content.
An unrelated Kora release can retain a grant only when all of these bound
identities, bytes, dependencies, and contracts remain unchanged.
An existing single-file or action-name grant cannot be automatically upgraded
to this combined-hash contract.

## Review, Dispatch, and Invalidation

1. Resolve the source-qualified skill and registered task from the immutable
   catalogue. Exact phrases and model suggestions use the same host gate.
2. Resolve every manifest, Markdown, script, and executable dependency and
   build the bounded snapshot; compute current hashes and validate contracts.
3. Compare the complete grant key. An observed content-hash mismatch
   permanently revokes affected once/session/always authorizations, marking
   persistent records revoked rather than deleting them; returning to old
   bytes must not resurrect them. A new approval creates a new grant.
   Unverifiable resources block dispatch. Failed revocation persistence blocks
   execution and reports the storage error.
4. With no matching grant, show the action, origin, instruction/definition
   identity, entry point, ordered script inventory, full combined hash,
   individual hashes, dependencies, invocation, and scope choices. Review
   opens each exact script read-only with syntax highlighting; shared helpers
   are marked as shared. No script review or skill enablement itself approves
   execution.
5. Bind approval to that complete snapshot and invocation. A new definition
   or dependency while review is open invalidates the proposal. Rejection,
   dismissal, expiry, or cancellation starts no script.
6. Immediately before dispatch, revalidate the grant and executable targets;
   execute only the reviewed immutable script set in the admitted worker.
   A queued task never silently switches snapshots or uses an outdated
   approval. Audit the outcome without storing source or sensitive arguments.

On application replacement/restart, reconcile persisted grants against the
new catalogue before making them usable. Use the reverse dependency index
to invalidate every skill/task covering a changed shared resource, not all
skills indiscriminately. Missing or corrupt resources disable affected
actions explicitly; never substitute a loose file or similarly named skill.

## Initial Skill: Lock the Machine

User invocation: "Kora, lock the machine".

Use the manifest contract above: the host resolves the Markdown, entry point,
and shared helper through immutable resource registrations and verifies both
the individual digests and combined script-set hash. The manifest cannot
provide an arbitrary path, executable, or command line.

Behaviour:

1. Recognise the direct request locally after wake activation/transcription; no cloud model or connector is required.
2. Resolve the original bundled skill and registered `session.lock` action.
3. Verify the interactive user/session, complete script-set and definition identities, and permission for this request.
4. Invoke the entry point from the verified embedded script-set snapshot with fixed validated parameters to lock the current Windows session.
5. Observe the Windows session-lock notification; an accepted API request alone does not prove the session locked.
6. Enforce microphone policy immediately on lock, regardless of how the lock occurred.

An unambiguous direct request identifies the intended lock, but is not
itself an execution grant. Under the future common gate, direct and
model-suggested lock both require action-specific approval for the exact
version-bound implementation, with once/session/always duration choices;
no separate named spoken confirmation is required as for shutdown/restart.
Today direct exact lock is still ungated, while model-suggested lock is
approval-gated. Do not present the current discrepancy as the intended policy.
Ambiguous or quoted/retrieved mentions of locking are not an invocation.
The skill cannot select another user/session, unlock Windows, obtain credentials, elevate, or lock repeatedly on a timer.

This narrow host-admitted session control can bypass the task queue, like cancellation, so it remains available during long-running work.
It is not a second general task executor and does not give the work-management model script/tool access.
The host owns the priority allowlist; neither a user skill nor a modified manifest can claim it.

A failure or missing confirmation produces an explicit failure/unknown result with an action receipt.
Do not announce success solely on script exit code and do not automatically retry an uncertain lock.
The script must not be able to clear policy, reopen the microphone, or modify Kora.

## Script Admission and Execution

- Embed and verify the manifest, Markdown, complete script inventory, resource identities, individual digests, combined hash, expected parameters, effect, and runtime.
- Load skill/script bytes only from the identified protected application assembly; never substitute a similarly named user skill, loose extracted script, or PATH-resolved script.
- Expose the registered action, not a generic shell or script runner, to agent workflows.
- Use a narrowly scoped worker/profile with tested denial of writes to Kora resources and no unnecessary filesystem/network/credential access.
- Verify Windows session-control API access under that actual profile before selecting the script engine.
- Containment and protected-resource tests are Slice A release gates for this script, not deferred general plugin work.
- Reject tampered scripts or unavailable containment explicitly; do not fall back to unrestricted PowerShell or other ambient-rights execution.

These are release conditions for enabling scripts, not effects of installing
PowerShell 7. Future side-effecting native built-ins also require the common
version-bound gate; read-only commands and safety interruptions remain usable
without PowerShell or a script grant.

General script import, generation, installation, and execution remain outside the MVP.
Shipping fixed scripts does not enable arbitrary user scripts.
The future folder-based dependency discovery model is described in
[Skill Storage](Skill_Storage.md#future-user-provided-executable-skills);
it cannot be used to add or override built-in catalogue entries.

## Required Packaging and Hash Verification

Before enabling bundled execution, prove:

- A published application contains every manifest, Markdown document,
  fixture, entry point, and helper as an embedded resource; a shared helper
  has one resource identity and multiple manifest references.
- Multi-script review and execution consume the same verified bytes without
  a writable extraction step. A missing resource disables every dependent
  action, with no directory/PATH/assembly fallback.
- Independent hash implementations agree on fixed byte-vector tests,
  including length framing, UTF-8 name lengths, big-endian integers, and
  exact script bytes.
- Reordering catalogue enumeration or script declarations does not change
  the script-set hash; changing helper execution order changes the definition
  digest and requires reapproval.
- Identical basenames in different logical directories sort by full name;
  path aliases, case collisions, and duplicate resources are rejected.
- Adding/removing/renaming a script, changing a BOM or line ending, and
  changing one byte invalidate every grant bound to that set.
- Byte pairs `ab` + `c` and `a` + `bc` cannot yield the same encoded input;
  the empty-set case cannot authorise a script-backed action.
- Changing a shared helper invalidates every dependent skill/task, but not
  unrelated skills; approval of one dependent skill never transfers to another.
- Manifest/Markdown-only changes require fresh review despite an unchanged
  script-set hash. Unchanged skill definitions/resources can retain always
  grants across an otherwise unrelated application update.
- Review-time changes, queued stale proposals, interpreter/dependency
  changes, restart reconciliation, and failed revocation writes all deny
  unapproved dispatch for every grant duration.

## Additional Bundled Computer Controls

Ship protected shutdown and restart skills alongside lock, mapped to fixed `computer.shutdown` and `computer.restart` actions.
The same script identity, containment, non-self-modification, and local routing requirements apply.
No generic shell, remote target, forced-close parameter, or user-defined privileged command is exposed.
Their action-specific voice or UI confirmation, mandatory OS checks, all-session active-work handling, host countdown, and cancellation are defined in [OOTB Phrases](OOTB_Phrases.md#shutdown-and-restart-safety).
App exit/hide/settings and queue controls are built-in lifecycle intents, not scripts required to control the host.

## Mandatory Locked-Session Microphone Policy

Policy ID: `microphone.disabled-while-session-locked`.

This is host policy, not behaviour implemented by the lock skill.
It applies when Windows locks via Kora, Win+L, timeout, remote session changes, or any other mechanism.
Kora must release microphone capture, not merely stop forwarding recognised commands.

- Resolve the current interactive session at startup and subscribe to authoritative Windows session changes.
- Do not open the microphone while session state is Locked, Disconnected, or Unknown.
- On lock/disconnect, synchronously block new capture/activation and invalidate the audio generation before asynchronously disposing devices/workers.
- Stop wake detection, command capture, and microphone-consuming recognition; discard in-flight audio/transcripts and clear pre-roll.
- Stop speech playback and hide sensitive interactive content while locked.
- Reject attempts to enable listening from skills, runtime adapters, queued requests, shortcuts, or stale callbacks.
- Unlock does not re-enable listening automatically; require explicit user
  re-enabling for the current run. A later ordinary application restart uses the
  default automatic startup policy.

No model instruction, bundled script, user setting, or approval may override this policy.
It does not independently disable Microsoft integrations or cancel all background tasks.
Already admitted background work remains governed by its existing permissions; interactive approvals cannot be accepted while locked, and existing deadlines still apply.
The lock-skill receipt must not expose private results on the lock screen.
