# Skill Sources, Profile Reuse, and Roaming Storage

Status: proposed. Profile reuse is delivered in Slice B; Kora-specific authoring in Slice C.

Related: [Extensibility](Extensibility.md), [Skill Authoring](Skill_Authoring.md), [Bundled Skills](Built_In_Skills.md), [Security and Data Flows](Security_Data_Flows.md).

## Source Partitions

| Partition | Location | Kora access |
|---|---|---|
| Bundled | Embedded resources in the protected Kora application binary | Read/invoke verified resource snapshots; no user/agent editing, loose-file overrides, or writable extraction |
| Shared profile | User-approved skill roots already in the profile | Read-only discovery and explicitly enabled compatible revisions |
| Kora-specific | `%APPDATA%\Kora\Skills` | Create/update validated declarative skills through the authoring writer |

Resolve Roaming AppData and the user profile through Windows Known Folder APIs, not model-provided paths or hard-coded user names.
The path notation above describes the intended folder, not an instruction to trust arbitrary environment-variable overrides.
The skill store must remain outside protected Kora code/loading roots.
Built-in manifests/instructions/scripts never occupy the roaming store or shared roots; their catalogue and complete definitions follow [Embedded Resource Storage](Built_In_Skills.md#embedded-resource-storage-and-integrity).

Proposed Kora-owned data layout:

```text
%APPDATA%\Kora\Skills\<skill-id>\<version>\manifest.yaml
%APPDATA%\Kora\Skills\<skill-id>\<version>\instructions.md
%APPDATA%\Kora\Skills\<skill-id>\<version>\examples\...
```

The layout and package schema are versioned; they are not executable search paths.
This describes the proposed declarative Slice C store, not a shipped
script runner. Its explicit declarative manifest is distinct from the
implicit folder descriptor for future user-provided executable skills below.
Future stored-script execution requires explicit review and
hash-bound grants under
[skill and task execution design](../docs/skill-and-task-execution-design.md);
merely finding or enabling a skill never grants execution.
Revision metadata can describe source attribution, but cannot carry authoritative grants or choose arbitrary executable implementations.
Permission/enablement records, source registrations, machine-specific tool configuration, and audit SQLite remain local under `%LOCALAPPDATA%\Kora`.
Credentials remain in the protected credential facility, never either skill partition.

## Shared Profile Discovery

Offer user-approved source registrations, not a recursive scan of the entire profile.
Recognisable candidate locations can be suggested if present, for example `%USERPROFILE%\.agents\skills` or `%USERPROFILE%\.copilot\skills`.
These are candidates only; Kora does not assume every tool uses them or every skill there is compatible.
Users can select another bounded profile skill root.

Registration authorises bounded local discovery/read, not execution, editing, or remote transmission.
Show origin, format, declared tools/capabilities, compatibility, and revision digest.
Do not automatically enable everything discovered.
The runtime discovery catalogue contains source-qualified summaries of enabled
compatible revisions, with purpose/selection guidance, inputs, tool/task
references, and availability. A model selection resolves the host-held pinned
snapshot; it cannot select live files, arbitrary script paths, or an unenabled
revision. Advertising a skill never advertises a grant.
See [Commands, Tools, Skills, and Model Interaction](Commands_Tools_And_Skills.md).
Shared roots and their resolved contents are protected from Kora-originated writes, including generic tools, scripts, and MCP pathways.
Reparse/link targets outside approved source scope require explicit source selection; do not follow arbitrary package links.

Example voice flow:

1. "Kora, show the skills in my profile."
2. Select/register a source if none is configured.
3. Kora lists compatible and unsupported packages with reasons.
4. "Kora, enable the deployment investigation skill from my shared profile."
5. Review its exact revision/dependencies; enable only after checks and user approval.

## Format Compatibility

Support the native Kora declarative format and an explicitly versioned `SKILL.md` reader for instruction-only profile skills.
The latter reads bounded YAML front matter such as name/description plus the Markdown instruction body.
Map supported metadata to a normalised internal skill descriptor without rewriting the source.
Reject malformed/unsupported required fields and explain incompatible executable workflows or unavailable tool references.
Do not silently discard required behaviour, fetch external references, or install dependencies.

A shell snippet in Markdown is text, not permission to run it.
Profile packages containing required scripts, custom validators, executable entry points, or installer hooks remain disabled under the MVP executable policy.
Reading such a package cannot promote its scripts into the bundled trust class.
An explicit user request can instead create a reviewed declarative adaptation using already admitted tools.
Skill instructions and descriptions remain untrusted content; policy is enforced at actual tool use.

## Future User-Provided Executable Skills

Status: future feature, outside the MVP declarative authoring/import policy
and not available in the bootstrap. This section defines dependency discovery
requirements, not permission to enable existing profile scripts today.

Unlike a built-in's explicit embedded manifest, a user-provided executable
skill has an **implicit manifest derived from its selected folder**.
The host reads a supported instruction document such as `SKILL.md`, its
bounded metadata, and the folder's script inventory into a normalised
in-memory descriptor. It does not require or write a built-in-style
manifest file beside it. A folder name is not a trustworthy skill ID, task
binding, or approval.

The descriptor records the source-qualified identity, canonical selected
root, Markdown identity/digest, script entry point, complete dependency set,
per-file identities/digests, and invocation contract. Select an entry point
through supported metadata or an explicit user choice; do not guess among
multiple `.ps1` files or execute code fences. Discover `.ps1` files recursively
within the selected folder under size/depth/count limits, without following
unapproved links. Include that inventory in the script set conservatively,
then expand it with statically resolvable transitive script dependencies.
Folder discovery never grants execution.

### PowerShell Dependency Discovery

For each discovered `.ps1`, use the admitted PowerShell parser/AST, not regex
search or evaluation of the script. Attempt to resolve references including:

- Dot-sourcing, for example `. "$PSScriptRoot\helpers\common.ps1"`.
- Call-operator invocation, for example
  `& "$PSScriptRoot\scripts\step.ps1"`.
- Literal `.ps1` command paths and supported static script imports or
  registered child-script launch forms.
- References in other discovered scripts, recursively.

Only literal paths and explicitly supported constant path forms, such as
`$PSScriptRoot` relative to the referencing file, may resolve automatically.
Relative paths must use the approved execution contract's base directory;
if the runtime base is ambiguous, report an unresolved reference rather than
assuming the skill folder. Do not run `Join-Path`, variable assignments,
module initialisers, or any other code to discover a target.

Canonicalise and verify each resolved file identity within approved source
scope. Reject traversal/link escapes, ambiguous casing/aliases, missing or
unreadable required tracked files with the referencing file and reason.
Unsupported forms remain visible discovery gaps. Reading a reference outside
the selected source scope requires explicit selection of an additional bounded
source; do not scan it implicitly or claim the reference was resolved.
This allows a future shared user helper to be included without trusting
an arbitrary sibling directory or the entire profile.

Walk the dependency graph with a visited set so cycles terminate and each
canonical file appears once in the hash inventory. Detect and report cycles;
never recurse indefinitely or infer that a cycle is safe. Do not label an
inventory complete when parsing or resolution failed.
Variable/computed paths, `Invoke-Expression`, generated/downloaded scripts,
dynamic modules, or unregistered child-process script launches are unresolved
discovery gaps, not an empty dependency list. Follow the owner-approved
[best-effort rule](Built_In_Skills.md#best-effort-transitive-dependency-tracking):
the user accepts responsibility for the granted script's transitive actions
within the admitted scope. Unknown references alone do not block that grant,
but do not authorize a new top-level task or relax resource/privacy controls.
No unrestricted-shell fallback or user-script exposure is enabled here.

Static parsing is best-effort discovery, not proof of all possible PowerShell
behaviour. The host must execute the reviewed declared snapshot and verify
required/tracked identities, while retaining discovery gaps. Universal denial
of undeclared scripts/modules/processes is no longer the default grant
requirement; any explicitly exact-dependency profile still needs actual proof.
OS containment remains independently mandatory. Parser success alone is not
containment admission, and known reviewed content still needs race-resistant
identity/snapshot validation.

### Hashing, Review, and Changes

Use the same [`Kora.ScriptSet.v1` combined-content encoding](Built_In_Skills.md#deterministic-script-set-hash)
as built-ins, not just the entry-point hash. Folder-discovered scripts receive
stable root-relative logical names; explicitly selected external dependencies
receive source-qualified relative names under separate host-owned root IDs.
Sort the complete logical names ordinally, preserve exact content bytes, and
retain canonical physical identities separately in the grant key. Reject
name/alias collisions; do not depend on discovery order or machine-specific
absolute paths for the combined-content encoding. Changed physical targets or
root bindings still require reapproval even when their bytes match.

Compute a definition digest using `Kora.SkillDefinition.v1` over the Markdown,
supported metadata files, and a host-generated implicit manifest. That
manifest uses a versioned deterministic serialization of source bindings,
entry point, dependency graph, and invocation contract; its format must be
specified and covered by fixed-vector tests before this feature ships.
The descriptor is host-calculated from actual files, not a checksum supplied
by the skill. Its authoritative grant record remains device-local.

At enablement/review, capture immutable content; immediately before dispatch,
rediscover folder membership, reparse references, and compare current
identities, hashes, and contracts with the approved snapshot. Watchers are
advisory, never the only check. Added/removed scripts, a new import, changed
dependency bytes, changed Markdown, or a moved/retargeted file revoke affected
grants and require fresh review. Changes to a shared external helper invalidate
all dependent skills, just as for embedded shared scripts.
Reverting content does not restore a revoked grant.
Persist revocation and its content-change reason; retain perpetual records
as revoked rather than deleting them. Fresh approval creates a new grant,
never updates the old digest or silently reactivates the record.

Required future tests cover multi-script folders, nested/transitive imports,
shared dependencies, identical basenames, cycles, missing files, source-scope
escapes, unresolved dynamic references, directory membership changes,
review/launch races, honest dynamic-reference gap disclosure and known shared
dependency invalidation without claiming complete transitive tracking.
Until these checks and containment pass, packages requiring
scripts remain disabled under the existing MVP policy.

## Identity and Revision Handling

Internal identity includes source partition, canonical source/package identity, declared skill ID, and content digest.
Display names and IDs from a package do not override bundled or other-source skills.
For duplicates, show source-qualified choices or use a previously explicit user binding; never silently shadow.
Reserved bundled session-control routing applies only to the original verified registration.

At enablement, safely read a bounded immutable package snapshot and compute its digest.
Keep the approved snapshot in memory for invocation; do not read live changing instructions midway through a task.
Include all permitted referenced package content in the revision identity.
Recheck source content/access before new dispatch and reconcile watcher notifications; watchers alone are not authoritative.

Changed content suspends new invocations pending review of the new digest.
An active task retains its existing snapshot; queued tasks do not silently switch versions.
Deletion, source removal, or lost access disables new dispatch and prompts for a fresh decision.
On restart, revalidate content and device-local enablement before using a source.
No silent content-bearing cache persistence is introduced for shared sources.

## Editing Without Changing Shared Sources

"Kora, improve this skill" creates a proposal for a Kora-owned copy with a new scoped ID under `%APPDATA%\Kora\Skills`.
Explain that it is a fork, preserve source attribution/digest, and show the diff before saving.
Do not modify, rename, delete, or place backups in the shared source.
The copy does not inherit grants, enablement, bundled executable trust, or priority privileges.
Later shared-source changes are shown as a difference; no automatic merge/update of the fork.

## Roaming Behaviour and Boundaries

Kora-specific skill definitions reside in Roaming AppData so Windows/profile management can roam them.
Kora does not implement cross-device federation or promise that Windows has enabled roaming.
Roaming definitions does not roam credentials, approval tokens, enablement, executable scripts, or machine-specific capabilities.
On another machine, discovered definitions start disabled until local validation, dependency checks, and enablement.

Detect conflicting revisions/IDs or incomplete sync and block affected enablement with a reconciliation choice; do not overwrite silently.
Approved revisions are written through a staged, validated save with atomic publication where the filesystem supports it.
If atomic publication cannot be established, report failure and leave the prior revision active.

Roaming/redirected storage may be managed or synchronised externally and can contain private skill text.
Disclose this during setup; do not store secrets, transcripts, or tool results in skill packages by default.
Local-only mode must not initiate reads/writes to network-backed roots; show the unavailable storage/source explicitly rather than silently choosing another directory.
OS-managed synchronisation outside Kora is a separate environment policy and is not guaranteed offline merely by selecting local-only mode.
