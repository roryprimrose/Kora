# Skills, Prompts, Instructions, and Agent Profile Management

Status: shared lifecycle contract with a bounded native Kora-owned instruction
editor and revision-held catalogue reload increment. Other-kind/profile editing,
agent execution and the full qualification matrix remain unavailable.

This is the authoritative management contract for all four definition kinds.
[Skill Storage](Skill_Storage.md) retains the delivered R21 read-only source
boundary and skill-specific package/execution rules;
[Skill Authoring](Skill_Authoring.md) supplies declarative skill validation.
The existing startup artifact reader and native skill inspection are partial
deliveries, not evidence that this full contract is implemented.

## Product Roles

| Kind | Value and application |
|---|---|
| Skill | Primary reusable capability: selection guidance, inputs, workflow, and references to admitted tools or resources. Explicit voice/UI invocation and qualified model selection resolve the same enabled immutable revision. |
| Prompt | Optional saved-request template, explicitly selected for one request. It is lightweight compatibility/flexibility support, not a parallel workflow engine or a requirement for ordinary voice requests. |
| Instruction | Reusable guidance with an explicit reviewed applicability scope. The host resolves applicable enabled revisions when admitting a request; file discovery alone never inserts guidance into model context. |
| Agent profile | Declarative specialised role referencing instructions, skills, and a bounded admitted tool subset, with input/output, locality, runtime, and budget constraints. Creating or editing the profile does not qualify an executor or start an agent. |

A skill may contain only instructions; do not require an artificial prompt
wrapper or duplicate its content. Prompt and instruction files cannot register
actions, approval rules, executable implementations, or privileged system
policy. Agent profiles cannot widen host limits, create grants, automatically
delegate, or select an unapproved destination.

Keep kind explicit in the common descriptor and apply kind-specific validation
and context roles. A prompt expands into clearly separated user-request
content with bounded explicit parameters; instructions are untrusted guidance,
not host policy. Skills supply the selected workflow; agent profiles supply
the qualified run configuration. File type or a claim of "system" authority
cannot promote text into trusted host instructions.
Missing template parameters require clarification, not expression evaluation,
file reads, secret substitution, or implicit network access.

## Source and Format Matrix

| Source | Skills | Prompts | Instructions | Agent profiles | Mutation boundary |
|---|---|---|---|---|---|
| Bundled `Kora.Definitions` resources | Explicit embedded packages | Actual embedded templates when introduced | Actual embedded scoped guidance when introduced | Actual embedded profiles when qualified | Immutable; inspect and create a distinct declarative adaptation, never overwrite or extract writable executable content. |
| Kora-owned roaming data | `Skills` | `Prompts` | `Instructions` | `Agents` | Create/edit/rename/delete supported declarative definitions through the dedicated writer; save and activation are separate. |
| Explicitly registered personal profile files/roots | Versioned compatible `SKILL.md` packages | Versioned compatible prompt files | Versioned compatible instruction files | Versioned compatible agent/profile files | First-class editable originals through the dedicated declarative writer; normal exact-change confirmation, with an optional Kora-owned fork. |

Resolve Kora data directories through `IApplicationDataPaths` and Windows
Known Folder integration. The intended roots are `%APPDATA%\Kora\Skills`,
`Prompts`, `Instructions`, and `Agents`; these are data, never executable
search/load paths. The existing versioned skill package layout is unchanged.
Specify versioned layouts for the other kinds before shipping their writers;
do not create empty bundled folders or imply resources currently exist.
Device-local registrations, enablement, source bindings,
reconciliation state, and audit live under the verified local application
data boundary. No definition metadata roams authority or credentials.

Profile means the user's personal customization locations, not permission to
scan the whole profile, workspace, repository, or another user's files.
Recognised `.copilot`, `.agents`, `.claude`, VS Code, and VS Code Insiders
locations may be offered as candidates, not automatically trusted or read.
Register exact files or bounded roots by deliberate voice/UI selection and
disclosure of source, kinds, supported reader versions, limits, and local read
scope. A source registered for skill inspection does not authorise reading
its prompt, instruction, or agent files. Additional kinds/scope require a
separate reviewed registration change.
Workspace/repository roots need their own explicit future admission; they are
not implicitly included by personal-profile registration.

Compatibility is reader-version-specific, not inferred from a folder name or
extension. The future format matrix must cover native Kora definitions,
compatible `SKILL.md`, `.prompt.md`, `.instructions.md`, and `.agent.md`
files, and explicitly qualified tool-specific instruction/profile formats.
Do not claim every Copilot/Claude/agent format is compatible. Required
unsupported metadata, scope/glob semantics, tool/runtime references, executable
hooks, or malformed text remain visible as incompatible; never discard them
to manufacture a usable definition.
Before delivery, each reader/writer specifies concrete byte/file/depth/header
and expansion limits, strict UTF-8/schema handling, deterministic exact-byte
digests, fixtures, and round-trip guarantees. R21's existing limits and narrow
reader are unchanged, not silently broadened for other kinds.

Identity includes kind, source ID, canonical file/package identity, reader
version, and complete admitted definition digest. Display/declared names are
not authority. Preserve original bytes, encoding/BOM, line endings, metadata,
and attribution for inspection and source-preserving edits.
Show same-name definitions as source-qualified choices; never shadow another
source, reserved command, or bundled action. A user binding is exact,
device-local, and revision-aware, not a "profile wins" search order.
Referenced definitions resolve by admitted source-qualified identity.
Reject unresolved required references and cycles before activation.

## One Management Surface, Equal UI and Voice Control

Provide a native **Definitions** surface reachable from the tray and workspace,
with kind/source/state filters. The existing Skills view can remain a focused
entry into it. List discovered disabled, incompatible, stale, missing, and
unavailable entries as well as usable ones, with truthful reasons.
Show identity, exact source, revision, compatibility, activation/applicability,
dependencies, requested tools/runtime/destinations, validation, and freshness.
The host-owned file tree and passive exact-source/diff views follow the
[workspace review contract](UI_Workspace_And_Windows.md#skills-and-permission-management).

UI controls, typed commands, and activated voice use the same registered
management actions, host-resolved targets, drafts, validators, and outcomes.
Models may propose edits, never supply an authority-bearing path or approval.
Basic list/inspect/disable/refresh/discard operations do not require a model or
network. Dictation and conversational refinement can use only a qualified
adapter under ordinary context/egress policy; native text editing remains
available when inference or speech is unavailable.

| Operation | Native UI | Equivalent activated voice |
|---|---|---|
| Find and inspect | Kind/source filters, search, exact source/details | "Show my prompts"; "Show instructions from my Copilot profile"; "Explain this agent's tools" |
| Register, narrow, or remove source consent | Source review with exact file/root and allowed kinds; removal preview | "Use prompts from my VS Code profile"; "Stop reading this source" |
| Create and select destination | Kind-specific editor; Kora store or explicitly selected profile scope | "Create a read-only review agent"; "Save this prompt in Kora" |
| Edit or fork | Explicit Edit original/Create Kora copy choices; field/source editor and exact diff | "Edit the original profile prompt"; "Make a Kora copy of this skill" |
| Refine and validate | Shared draft, errors, dependency checks, data-only simulation/template preview | "Change the output to bullet points"; "Read back the changed section"; "Validate this definition" |
| Save, rename, restore, delete | Exact mutation preview and separate confirmation | "Save these changes"; "Rename this instruction"; "Restore the previous revision"; "Delete this profile prompt" |
| Activate or stop applying | Separate revision review, enable/disable and instruction-scope controls | "Enable this skill"; "Apply this instruction to this session"; "Disable this agent profile" |
| Use | Selected prompt/skill action or qualified agent run control | "Use weekly summary to summarise this"; "Run the review agent on this task" |
| Refresh and reconcile | Refresh source/all, freshness status, external diff/conflict choices | "Reload my definitions"; "Show external changes"; "Keep my draft"; "Reload the external version" |

Voice edits can address named fields, headings, or an exact selected bounded
section; ambiguous source, text range, name, scope, or destination requires
clarification. Dictated text is draft content, never a command within that
content. Readbacks summarise the same proposal/diff as the UI, with full details
available on request. Voice confirmation binds the exact visible proposal,
operation, source, file set, base/new digests, and current session generation.
Concurrent voice/UI edits invalidate stale confirmation; commit once.
Source selection may use a reviewed spoken path or known candidate, with
native picker fallback for uncertainty, not a guessed path.

All supported operations are available by mouse/keyboard, voice, or mixed
input under the same session/privacy/approval gates; an unavailable speech
channel retains UI recovery rather than lowering those gates.
Disabled/incompatible definitions remain inspectable/editable when source
access permits, but cannot be applied or invoked. Agent authoring/validation
may be available before agent execution; expose Run as unavailable with the
unqualified-runtime reason, never emulate execution with a prompt.

## Editing, Ownership, and Confirmation

In the target design, Kora is an editor of supported personal profile definitions
as well as Kora-owned definitions. Registered originals are directly editable;
there is no separate read-only-by-default tier or edit-enable switch.
Registration discloses bounded reading and supported editing scope but does not
approve any write. Each create/save/rename/restore/delete needs an exact
user-confirmed mutation proposal. The delivered R21 read-only registration is
not retroactively expanded; moving it to editor scope requires explicit review
of that changed source contract.
Disclose that the original is shared with other applications and external
sync; offer a Kora-owned fork instead. Revocation cancels pending profile writes
and invalidates approvals without deleting originals or Kora copies.

Only the qualified dedicated definition writer may mutate those originals.
Generic filesystem tools, scripts, MCP routes, and agent executors still cannot
write registered shared sources. No executable scripts, hooks, binaries,
application source, policy, credential files, or protected resources become
editable. If a format cannot round-trip supported content without dropping or
changing unrelated metadata, disable in-place editing and explain the fork/
external-editor option. An unsupported original is not "repaired" implicitly.

The host derives targets from registered identity and validated relative
names, never arbitrary model paths. Revalidate registration, session, complete base
file-set identity/digests, source directory binding, and effective target at
commit using race-resistant filesystem operations.
Reject traversal, reparse/hard-link/alias escapes, replaced directories,
protected roots, unexpected package members, and conflicting external writes.
Retain the existing local-only/network-backed storage restrictions.

For the bounded owned single-file instruction writer, the user has explicitly
chosen **optimistic content conflict detection**, not content compare-and-swap.
Immediately before atomic replacement, compare the live original identity and
exact digest with the reviewed base. A detected changed, missing or replaced
base invalidates ordinary Save and offers exactly **Reload from disk (abandon
draft)**, **Overwrite with Kora changes**, or **Cancel (keep draft)**.
Overwrite requires another exact native mutation review of the observed conflict
and proposed Kora bytes; another change before the final comparison prompts again.
An unseen content edit in the narrow final comparison-to-replacement window may
be overwritten (whole-file last-writer-wins). This accepted content race does
not relax source/root ownership, privacy, consent, audit, parent identity,
reparse/hardlink/alias or protected-path checks. Publication is relative to a
pinned native directory handle, never a followed caller/model destination.

Stage and validate a complete immutable proposal; publish atomically where
verified. A multi-file package needs complete revision publication/recovery,
not sequential overwrites presented as an atomic save. If the source cannot
support safe publication, refuse in-place mutation and offer a Kora-owned fork.
Temporary/backup destinations obey the same scope and privacy boundaries.
No silent rollback, partial-success receipt, or automatic detected-conflict
overwrite; only the explicitly accepted optimistic content window above may
overwrite an unseen concurrent edit.
uncertain publication closes mutation/activation until explicit reconciliation.
Record requested and terminal typed audit outcomes for writes, consent, and
activation changes; surface cancellation/failure and any observed durable effect.

Saving creates a validated revision but never activates changed content.
Unchanged enabled bytes keep their current status; changed saved bytes suspend
new use until separate revision-bound activation.
Delete distinguishes deleting originals, deleting owned copies, disabling use,
and unregistering a source. Source removal never deletes originals.
Rename changes file identity/bindings as applicable and invalidates affected
approvals/references; do not silently retarget by display name.
Restoration is a new reviewed write, not restoration of revoked grants.
A fork has distinct identity and attribution, no inherited enablement, grants,
bundled trust, or privileged routing; later source changes never merge into it
automatically. Closing a dirty editor requires Save/Discard/Cancel.

## Consistent External Change and Reload Contract

Apply one reconciliation pipeline to Kora-owned and registered profile files
of every kind, whether the trigger is Save, manual Reload, a bounded watcher,
scheduled discovery, startup, or pre-use validation. Restart is not the intended
way to refresh definitions; the current startup-only artifact reader remains a
documented delivery limitation until this pipeline is qualified.
Embedded definitions change only with a verified application replacement.

1. Revalidate registration/read consent, privacy/session admission, source
   directory identity, complete bounded membership, and exact bytes. Watchers
   are advisory; coalesce notifications and recover overflow with a bounded
   re-enumeration, never a whole-profile scan.
2. Parse/validate off the immutable candidate snapshot. Do not expose partially
   read packages or a partially published source catalogue. Superseded refreshes
   cannot publish late; source withdrawal/privacy closure cancels them.
3. Compare complete identities/digests and reconcile the source catalogue,
   UI, voice lookup, slash completion, instruction applicability, profile
   references, and admitted model descriptors as one revision.
   No-change refresh preserves activation; successful metadata refresh is not
   activation of changed content.
4. New definitions enter disabled. Changed definitions become review-required
   and block new use; invalid, deleted, inaccessible, replaced, or revoked
   sources become incompatible/unavailable with explicit recovery.
   Failed refresh never presents the last snapshot as current or usable.
   Retained old review content is labelled historical and remains subject to
   access/privacy policy; clear it on access withdrawal.
5. Invalidate approvals/drafts based on changed bases and affected dependent
   definitions, instruction bindings, agent profiles, and executable grants.
   Never silently update a reference to the latest/name-matching version.
   Restoring old bytes does not resurrect revoked authorization.
6. Revalidate exact source/reference freshness immediately before admitting
   new use, even without watcher events. Pin the full applicable definition
   set for the task/clarification; queued work retains exact revisions and
   requires review or re-admission if stale, never an automatic version switch.

Active tasks retain their admitted immutable definition set; reload cannot
inject new guidance mid-task. Access/consent revocation, required resource
loss, or grant invalidation still applies at each action/egress boundary and
can block/cancel continuation. Snapshot pinning is not permission to ignore
revocation or make a now-denied new source read.

A clean inspection/editor may update to a newly validated candidate with a
visible revision/change notice, without enabling it.
A dirty editor retains its base and draft and reports a conflict; offer
compare, explicit rebase with validation/review, discard/reload, or save as a
new copy. Never overwrite the draft, auto-merge, or save over external edits.
The same conflict and resolution are addressable by voice.
Passive refresh does not steal focus, speak unsolicited private file contents,
or count as user activity/approval.

## Delivery and Verification

### Bounded owned-instruction editor increment

Native tray and exact `instructions editor/list/create/edit/inspect/save/reload/
delete/activate` control routes share the same admitted actions, native review
and writer. Activated controls focus the same native editor; voice-only approval
and body dictation/refinement are not implemented. No model/network is needed.
Create and inspect use an explicit bounded ID; mutations target the open original.
The writer admits only immediate canonical lowercase ASCII `*.instructions.md`
files in `IApplicationDataPaths.RoamingRoot\Instructions` (maximum 128 files).
Strict UTF-8 and the original 64 KiB file-byte bound apply. Native format v1
uses scalar `kora-format: 1`, `name`, `description` and optional `attribution`;
the editor preserves original BOM, source text, line endings and supported
metadata. Compatible legacy files lacking `kora-format` retain unchanged
behavior; unsupported scope/metadata cannot be implicitly repaired in place.
Header editing is bounded to 16 fields/4096 UTF-8 bytes. Fork, rename, restore,
profile mutation, other kinds, executable content and agent runtime are absent.

Native file/parent identity and exact bytes are rechecked under durable original
local-input/session admission. Complete private staging is flushed, then renamed
relative to the pinned source directory. Pre-publication cancellation leaves the
base intact; uncertain post-publication readback/audit/catalogue outcomes are held
and explicitly require reconciliation, never rollback. Saved revisions enter
disabled, source-qualified catalogue candidates; activation separately reviews
the exact bytes for explicit instruction selection on a new request, not broad
automatic applicability or trust. Source changes/replaced identity, failures,
deletion and restored previously revoked bytes do not auto-activate. Pre-use
native freshness checking complements advisory watchers. Existing unchanged
legacy and bundled behavior is preserved.

Production parser, admission/conflict workflow, native storage and catalogue
fixtures are the maintained assertions for this increment only. They are not
equivalent to installed UI/accessibility, runtime/provider, containment, acoustic
quality, storage-engine/capacity or physical-interruption evidence. The distinct
historical witnesses and consumer assessments retained in the
[historical roadmap](Archive/Implementation_Roadmap_2026-10-11.md#experiment-disposition-inventory)
remain unchanged; no experiment files or consumers are retired by this increment.

Extend R21 source/reconciliation work and R22 authoring incrementally through
the common contract; qualify profile mutation separately from read access.
Prompt support stays lightweight; do not introduce a second execution engine.
Agent run controls stay gated by the qualified host/runtime described in
[definition and agent guidance](Commands_Tools_And_Skills.md#bundled-definitions-and-agent-profiles).
Do not advertise a kind/operation until its actual reader, writer, UI/voice
action, audit, lifecycle, and acceptance coverage exist.

The [definition management acceptance gate](Acceptance_Criteria.md#definition-management-and-external-reload-gate)
requires the full kind/source/channel matrix, exact-byte round trips,
source consent and mutation isolation, dirty-editor conflicts, concurrent
publication, watcher loss, dependency invalidation, and truthful unavailable
runtime/storage states. Existing bounded R21 evidence does not pass that gate.
