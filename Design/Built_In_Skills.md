# Out-of-the-Box Skills and Session Policy

Status: bounded R11 embedded package catalogue and immutable native source review
delivered; no skill runner. The current bootstrap retains exact direct
Windows-API lock and model-suggested action-name lock approval unchanged.
Lock/shutdown/restart manifests, Markdown, fixtures, entry scripts and one shared
helper are embedded in `Kora.Definitions`. All package actions are explicitly
unavailable for invocation. The tray's **Skill packages (inspection only)**
entry opens every declared file in read-only native tabs.
See [future execution design](../docs/skill-and-task-execution-design.md).

The structural migration preserves all 13 resource IDs/bytes and package
digest golden vectors. `Kora.Definitions.Skills` owns the explicit embedded
catalogue; Core retains the package validation/digest rules and desktop
retains native inspection. This assembly move enables no runner or new model
capability. Shared prompt/instruction and future agent-profile organization
follows [definition guidance](Commands_Tools_And_Skills.md#bundled-definitions-and-agent-profiles).

Related: [Extensibility](Extensibility.md), [Skill Authoring](Skill_Authoring.md), [Skill Storage](Skill_Storage.md), [Security and Data Flows](Security_Data_Flows.md), [Acceptance Criteria](Acceptance_Criteria.md).

## Skills Versus Internal Kora Tools

Per-skill selection, inputs, dependencies, effects and current/planned
availability are catalogued in the
[technical reference](Tool_And_Skill_Reference.md#12-built-in-skill-contracts)
and [user guide](../docs/tools-and-built-in-skills.md#13-built-in-skills).

Use [Commands, Tools, Skills, and Model Interaction](Commands_Tools_And_Skills.md)
for the common terminology and app -> model -> app flow.
Internal functionality such as querying session state is a host action exposed
as a typed tool; it does not need a skill package or `.ps1`.
A bundled computer-control skill describes an outcome, selection guidance,
inputs, and a workflow/task reference. Its registered script implements the
effect and participates in content-bound execution grants.
The task may also have a tool interface; skill, tool, and script are distinct,
complementary layers.

The host advertises the skill's source-qualified ID/revision, purpose,
selection guidance, inputs, and dependencies to the model. It resolves selected
instructions and tasks itself, rather than sending script source by default or
accepting an executable path from the model.
Exact phrases can select the original registration without inference; ordinary
language can select it through a validated model proposal. Both routes use the
same implementation and grant gate, and neither selection nor enablement is
approval. Execution produces a structured result/receipt; model-mediated work
can return that result to the runtime under egress and session-privacy policy.

## Package Classes and Sources

Kora ships useful skills without requiring users to author or download them.

| Package class | May contain | Installation and modification |
|---|---|---|
| Bundled first-party skill | Explicit manifest, selection guidance, Markdown instructions/workflow, optional fixtures, and any registered PowerShell script set, all embedded resources in a protected Kora application binary; scripts may be shared between skills | No user/agent editing or file replacement; updated only with the application through verified out-of-band maintenance |
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

This YAML is conceptual design, not a supported loader format. The bounded R11
catalogue uses a strict explicit JSON schema with no YAML/plugin loader.
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
twice under aliases in one script set. Every host-snapshot helper-load or
script-call reference must resolve to that set; every non-entry script must
have a declared role. This catalogue guarantee is distinct from
[best-effort discovery of runtime transitive code](#best-effort-transitive-dependency-tracking).
Helper initialisation order is separate from hashing order: an
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

Build every built-in skill's manifest, selection guidance, Markdown instructions, fixtures, and all required scripts into the Kora application assembly as embedded resources.
Use stable host-owned resource IDs and a build-generated catalogue binding skill ID/version, manifest/instruction resource IDs, complete script set, per-file digests, script-set hash, definition digest, and permitted tool/task references.
Resolve resources from the explicitly identified application assembly, never by scanning arbitrary assemblies, folders, PATH, profile roots, or plugins.
The assembly is part of the protected application binary deployment; this does not require choosing single-file publishing or a self-contained runtime.

At build time, reject unsupported schemas, duplicate identities, absent
resources, wrong resource kinds, unlisted host-snapshot references, and invalid
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

For manifest-listed internal scripts, ordinary disk-based dot-sourcing,
`&` invocation of a `.ps1` path, and imports
from a profile, working directory, or `$PSScriptRoot` are not a fallback for
built-ins. Shared scripts must use the admitted in-memory contract.
Intentional transitive use of other code follows the separately reviewed
best-effort tracking rule and admitted resource/capability scope.
Before admitting the runner, prove helper scope, parameter binding,
host-snapshot call resolution, cancellation and the actual required resource
containment. PowerShell runspaces and command filtering alone are not a
security sandbox. Failure of that containment proof leaves the affected
action unavailable; it does not justify extraction or ambient PowerShell
execution. Do not confuse best-effort transitive tracking with an enforced
exact executable/module allowlist.

R02's original proof demonstrated fixed embedded interpreter input and partial
filesystem/credential/lifetime isolation. The separate
[W2 fixture](../experiments/r02-w2-dependency-proof/README.md) demonstrates a
definition-only helper plus typed entry-point effect from separate in-memory
blocks, and rejects ACL/no-child policies as exact dependency mechanisms.
It does not establish complete helper/runtime admission, network denial,
installed protection or actual Windows control effects. Follow
[the continuation gates](Security_Data_Flows.md#windows-containment-continuation-gates)
before R11 admission; no native broker is selected.

### Best-Effort Transitive Dependency Tracking

Owner decision, 2026-10-06: this rule applies to **both bundled and future
user-provided scripts**. The user granting execution accepts responsibility
for the overall actions of the approved script, including code it calls,
within the separately admitted capability/resource scope. Kora attempts to
identify and track other scripts, modules and binaries, but does not promise
complete discovery or detection of every transitive change.

- The complete manifest-listed internal script set remains mandatory,
  byte-exact, immutable and grant-bound. All manifest-listed files are
  available through named read-only review tabs; each script, including every
  shared helper, has its own tab. Approval binds the same bytes that run.
- Discover supported static references without executing code or scanning
  outside selected source scopes. Show identified dependencies, their tracked
  identities/digests and unresolved/dynamic references. The review explicitly
  explains that further code and changes may go undetected; do not label a
  partial inventory complete.
- Observed changes to declared or tracked approval-relevant content revoke
  affected grants under the existing rules. A known required missing,
  unreadable, changed or unverifiable resource still blocks dispatch.
  Undiscoverable transitive references alone are not a blanket refusal or an
  implicit grant for a new top-level task.
- The protected host-selected runtime, native adapter identity, fixed task,
  parameters, grant scope and declared embedded resources are not best effort.
  Source discovery cannot install code, broaden approved read/egress access,
  change policy, mint approvals or turn model text into executable input.
- Universal denial of all undeclared code is **not** the default script-grant
  requirement. An explicitly narrower exact-dependency profile still needs
  actual enforcement evidence; an inventory, hash or user's acknowledgement
  cannot certify it.
- This changes dependency/revocation guarantees, not protected-resource,
  privacy, ownership, network-profile, power-approval or truthful-receipt
  boundaries. No ambient-rights fallback, production exposure or general
  user-script capability is enabled by this design decision.

Embedding prevents ordinary skill-file editing, not patching/replacing an entire binary.
Protected deployment permissions and release-origin/provenance checks are still required; an embedded checksum alone cannot authenticate an assembly whose code/catalogue was also changed.
Initial official artifacts are unsigned, so canonical release origin, final-byte hashes, build provenance, and protected installed-file permissions provide traceability and tamper detection rather than Authenticode publisher authentication.
Modified or mismatched application provenance must be rejected by the trusted deployment/launch mechanism where it can be established, not merely checked by code inside an already compromised binary.
Source builds identify their separately trusted local build provenance and never claim to be official release binaries.
A user deliberately changing source and building another application, or an administrator bypassing deployment trust, is outside the integrity guarantee.
Kora must not claim that a locally owned source-available application is impossible for its owner to modify.

## Deterministic Script-Set Hash

Permission grants for a script-backed skill bind a SHA-256 hash of the
**combined bytes of every `.ps1` declared by its manifest**, including shared
helpers and explicitly declared transitive scripts. Retain each file's SHA-256 as well for review,
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
- Separately verified required interpreter/adapter identities and digests,
  and identified approval-relevant module/executable dependencies tracked
  best effort. The `.ps1` hash does not cover these binaries or undiscovered code.
- Relevant grant/policy schema revision and user/identity scope.

Once, session, and always describe duration, not trust in future script
versions. Markdown-only or manifest-only edits leave the script-set hash
unchanged but invalidate the definition-bound grant and require review.
The bounded R05 [exact binding](../src/Kora.Core/Authorization/ExactOperationBinding.cs)
and [authorization service](../src/Kora.Application/Interaction/HostAuthorizationService.cs)
now test these equality/revocation rules against host-resolved snapshots.
The declared-resource digest must bind the complete declared set, resource
mappings and per-file identities/digests, not just the entry point.
Tracked-content identity describes the observed best-effort inventory, not
a claim of complete transitive discovery or an exact executable allowlist.
Catalogue/byte verification, actual script-set/definition encoding and immutable
native source tabs are delivered by the bounded R11 foundation.
`Kora.DeclaredResources.v1` frames every declared file including the manifest.
It uses the same ordinal-name framing, inserting `UInt32BE(resource-ID UTF-8
byte length)` and the exact UTF-8 resource ID after each logical name and before
the content length/bytes. This binds the complete logical-name/resource-ID map
(including the catalogue's manifest registration) and per-file contents.
The read-only `HasSameDeclaredContent` comparison checks only declared content
against an existing R05 exact record; it never creates or uses a grant, or
asserts interpreter/invocation/identity validity. Execution remains unavailable.
These services run no scripts and do not weaken W2's separate containment/privacy boundaries.
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
2. Resolve every declared manifest, Markdown and script, and required runtime/
   adapter identity. Build the bounded snapshot, compute hashes and validate
   contracts; discover transitive dependencies best effort and retain gaps.
3. Compare the complete grant key. An observed content-hash mismatch
   permanently revokes affected once/session/always authorizations, marking
   persistent records revoked rather than deleting them; returning to old
   bytes must not resurrect them. A new approval creates a new grant.
   Unverifiable resources block dispatch. Failed revocation persistence blocks
   execution and reports the storage error.
4. With no matching grant, show the action, origin, instruction/definition
   identity, entry point, ordered script inventory, full combined hash,
   individual hashes, dependencies, invocation, and scope choices. Review
   opens all manifest-listed files in read-only tabs, each exact script with
   syntax highlighting; shared helpers are marked as shared. Disclose
   best-effort transitive tracking and user responsibility. No script review or skill enablement itself approves
   execution.
5. Bind approval to that complete declared snapshot, tracked identities and
   invocation, not a claim of complete runtime discovery. An observed new
   approval-relevant definition/dependency while review is open invalidates the proposal. Rejection,
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
Canonical phrases and reserved aliases live in the host intent catalogue.
Selection guidance assists model interpretation but cannot establish permission
or change host policy.

Behaviour:

1. Recognise an exact direct request locally after voice activation/transcription or typed input; no model or connector is required. For other wording, a model may propose this skill/task using its advertised description, clarifying an uncertain intent or target.
2. Resolve the original pinned bundled skill and registered `session.lock` task; validate the proposal's lineage to the explicit user request.
3. Verify the interactive user/session, complete script-set and definition identities, and permission for this request.
4. Invoke the entry point from the verified embedded script-set snapshot with fixed validated parameters to lock the current Windows session.
5. Observe the Windows session-lock notification; an accepted API request alone does not prove the session locked.
6. Enforce microphone policy immediately on lock, regardless of how the lock occurred. Produce a correlated structured result/receipt for host presentation or permitted runtime continuation; do not expose private results on the lock screen.

An unambiguous direct request identifies the intended lock, but is not
itself an execution grant. Under the future common gate, direct and
model-suggested lock both require action-specific approval for the exact
version-bound implementation, with once/session/always duration choices;
no separate named spoken confirmation is required as for shutdown/restart.
Session is offered only under the durable binding rule below; priority dispatch
does not waive identity, persistence, grant, or call-policy checks.
Today direct exact lock is still ungated, while model-suggested lock is
approval-gated. Do not present the current discrepancy as the intended policy.
Ambiguous or quoted/retrieved mentions of locking are not an invocation.
The skill cannot select another user/session, unlock Windows, obtain credentials, elevate, or lock repeatedly on a timer.

This narrow host-admitted session control can bypass the task queue, like cancellation, so it remains available during long-running work.
It is not a second general task executor and does not give the work-management model script/tool access.
The host owns the priority allowlist; neither a user skill nor a modified manifest can claim it.

### Standalone Lock Work-Session Binding

An unaddressed standalone lock request creates a new durable Active Kora
control work session through the deterministic host, without routing inference
or waiting for a general task slot. Use a neutral lock-control title, not
private labels copied from another session. Never attach it to the selected
window, foreground voice question, currently running task, or an inferred
related session. A request deliberately addressed to an Active work session
(including its composer) uses that exact session after host validation;
ambiguous or Done targets require clarification/explicit resume.

Before presenting approval or dispatching in any scope, atomically commit the
work-session identity and request/task/proposal lineage, then show/read back
the bound session and exact lock action. Session approval may be offered only
after that committed Active binding exists. Creation/selection is not approval;
Once and Perpetual still bind the exact implementation/invocation and retain
their own rules. If persistence fails, report the failure and do not dispatch
or silently fall back to process-local Session authority.

The priority host path records grant use and the correlated observed lock
receipt in that same session, even when all general execution slots are busy.
A terminal lock task does not automatically mark the work session Done.
Later unaddressed standalone requests create new control sessions and cannot
reuse the first session's grant; deliberate addressing of the still-Active
control session permits only its exact approved operation after revalidation.
Done/deletion ends Session authority, resume never restores it, and an ordinary
restart preserves only still-applicable Active-session grants without replay.
Protected calls may require Once instead of reusable scope under their
independent grant-ignore policy.

A failure or missing confirmation produces an explicit failure/unknown result with an action receipt.
Do not announce success solely on script exit code and do not automatically retry an uncertain lock.
The script must not be able to clear policy, reopen the microphone, or modify Kora.

## Script Admission and Execution

- Embed and verify the manifest, Markdown, complete script inventory, resource identities, individual digests, combined hash, expected parameters, effect, and runtime.
- Load skill/script bytes only from the identified protected application assembly; never substitute a similarly named user skill, loose extracted script, or PATH-resolved script.
- Expose a typed tool/task contract and structured result for the registered effect, not a generic shell or script runner, to agent workflows.
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
M/E may submit proposals only. The deterministic host lifecycle controller
owns approval and dispatch through the admitted execution gateway/worker under
[Management Power Proposal Authority](Security_Data_Flows.md#management-power-proposal-authority);
management inference never runs these scripts.
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
  saved-consent startup policy and fresh gates in the
  [microphone matrix](Security_Data_Flows.md#microphone-consent-and-enablement-matrix).

No model instruction, bundled script, user setting, or approval may override this policy.
It does not independently disable Microsoft integrations or cancel all background tasks.
Already admitted background work remains governed by its existing permissions; interactive approvals cannot be accepted while locked, and existing deadlines still apply.
The lock-skill receipt must not expose private results on the lock screen.
