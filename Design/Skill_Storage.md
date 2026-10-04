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
script runner. Future stored-script execution requires explicit review and
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
