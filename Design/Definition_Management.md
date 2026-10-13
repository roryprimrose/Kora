# Skills, Prompts, Instructions, and Agent Profile Management

Status: shared lifecycle contract with a bounded native Kora-owned instruction
and versioned literal/required-parameter prompt editor and revision-held catalogue reload increment. Skill/agent/profile editing,
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
| Prompt | Optional saved request, explicitly selected for one new request. Owned native v1 remains literal; native v2 supports bounded required `${input:name}` text, complete private expanded USER REQUEST preview, and separate fresh request admission. No expressions, workflow engine, policy or actions. |
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

For the bounded owned single-file instruction and prompt writer, the user has explicitly
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

### Bounded owned instruction and prompt editor

Native tray and exact `instructions` or `prompts` followed by
`editor/list/create/edit/inspect/validate/save/copy/reload/delete/activate/disable/stop-use/cancel` control routes share the same admitted actions, native review
and writer. Activated controls focus the same native editor; voice-only approval
and body dictation/refinement are not implemented. No model/network is needed.
Create and inspect use an explicit bounded ID; mutations target the open original.
The closed writer kinds admit only immediate canonical lowercase ASCII `*.instructions.md`
files in `IApplicationDataPaths.RoamingRoot\Instructions` or `*.prompt.md` in
`IApplicationDataPaths.RoamingRoot\Prompts` (separately bounded to 128 files per kind).
The coherent catalogue independently admits those two owned inventories and
retains the existing 128-entry bound for all other artifacts; there is no eviction.
Strict UTF-8 and the original 64 KiB file-byte bound apply. Native format v1
uses scalar `kora-format: 1`, `name`, `description` and optional `attribution`;
the editor preserves original BOM, source text, line endings and supported
metadata. Compatible legacy files lacking `kora-format` retain unchanged
behavior; unsupported scope/metadata cannot be implicitly repaired in place.
Header editing is bounded to 16 fields/4096 UTF-8 bytes. Profile fork, rename, restore,
profile mutation, skill/agent writers, executable content and agent runtime are absent.

**Owned native v2 required literal prompt inputs** extend only the Prompt kind, never instruction v1 or bundled/profile reader semantics.
Use scalar `kora-format: 2` with the same `name`, `description`, optional `attribution` header. Required fields are derived from original-body `${input:name}` markers: 1–64 lowercase ASCII letters/digits/hyphens starting with a letter, at most eight distinct names in first-occurrence order. Repeated names use one value.
No declarations/defaults/optional fields, expressions, environment/files/secrets/clipboard/network/functions or nested/escaped expansion are supported. Malformed or unsupported `${...}` is visibly incompatible. This is not general Copilot prompt-format support.
The shared parser preserves every original literal/body newline/BOM/source byte and original digest. Expansion scans original markers once; marker-looking text in a value stays literal, never commands, tool arguments or system guidance.
Each required native field is nonblank, well-formed Unicode without NUL, at most 512 UTF-16 units / 2048 UTF-8 bytes. The whole expanded request is at most 4096 UTF-16 / 16384 UTF-8 bytes; over-limit text is rejected, never normalized or shortened. The actual serialized model envelope, including host policy/tools overhead, remains separately bounded to 32768 UTF-8 bytes.
Explicit `/prompt <id>`, `/<command>` and current-name activated selection of an enabled exact v2 owned revision focus one private native parameter form; inline/voice parameter values are not accepted. All new/saved/copied native revisions remain disabled until separate current-run exact-template use review.
**Preview complete expanded USER REQUEST** shows the whole bounded expansion as inert user-data text, with an independent expansion digest. It does not replace catalogue/source bytes or original digest and is not tool approval, provider qualification or egress authority.
Any input/template/native file identity/review/catalogue/host/call/privacy change retires the preview and pending Use; native pre-preview and pre-use source checks cover missed watcher events. No same-name or restored-byte retargeting/replay is allowed.
**Use reviewed request** separately checks a fresh admitted request with `ModelTurnHost`, preserving original source/revision and exact preview/value identity. Expanded text occupies only `UserRequest`, not `SystemPolicy` or evidence. No typed-command reparse occurs.
**Review submission to exact Active session** is a separate retention workflow, not a change to Preview or nonpersisting Use. Explicit Refresh/Next reads at most 25 metadata rows per page with a complete 64 KiB bound; no target is selected by default or inferred from name, window focus, voice or form values.
Separate **Submit this expanded request to exact session** confirmation shows the entire frozen expanded USER text, immutable target ID/name/generation/metadata revision and volatile original-source/revision/digest plus independent expansion digest. It discloses private readable SQLite, existing retention, best-effort reject-only screening and unavailable generation; Cancel retains only eligible memory input.
The application [prompt submission workflow](../src/Kora.Application/Definitions/PromptConversationSubmission.cs) repeats actual native `CaptureUse` freshness before review and admission, then calls the existing conversation service once. Its final physical COMMIT callback requires the same private controller/preview/target epoch. Source validation and SQLite COMMIT are not a global filesystem/database transaction.
Only the exact approved expansion is retained as one immutable USER conversation message, with a fresh conversation request/task and required audit. No template, hidden field record, source path or durable source provenance is added; no old Use admission is also invoked. Original template bytes/digest remain unchanged; prior schema/IDs/copies are preserved.
Input/source/native identity/use/catalogue/privacy/ownership/target changes and loss of window focus withdraw pending review. Stale/Done/Removed targets require refresh and explicit reselection, never implicit Resume or a new session. Confirmed receipt clears the form; an unconfirmed attempt is held for exact conversation/task inspection without retry or rollback claims. Saved drafts and unrelated Sessions composer buffers are never imported, overwritten or cleared.
Production adapters/catalogues remain unqualified/unavailable; Ollama 0.35.1/qwen3 and 0.40.2 remain rejected. Use reports the admission outcome without generating, invoking the legacy reasoner, installing/authenticating or falling back. Even controlled qualified admission does not enable production generation in this increment.
Fields and expanded preview buffers exist only for this view lifetime: Cancel/close/private closure clears controls and references immediately, with no autofill/preferences/source autosave/logging/audit text/clipboard export/speech readback. Only separate explicit conversation confirmation retains the approved expansion; Preview and Use write no form body.
Source checks consume view cancellation, and handoff/exit joins pending work before releasing owned resources. A new accepted typed/activated request retires the original preparation even when typed text is unchanged; original input is captured before awaited native source validation.
Already captured immutable requests are not mutated by later source edits; ordinary source/privacy/approval boundaries still apply.

**Owned metadata inventory/search** extends this same native editor, not the future full Definitions source matrix. Explicit List/Refresh reuses admitted per-kind filename enumeration and existing reconciliation; an immutable projection includes only those owned filenames and coherent observed catalogue metadata.
Literal filename/display-name/source-qualified-ID search uses the existing 256 UTF-16 / 512 UTF-8 query ceilings without lexical tokenization, regex or path resolution. Invalid controls/Unicode/oversize input requires correction, never trimming. All/UseEnabled/ReviewRequired/UnavailableOrUnknown reflects only actually known observed use state.
No body, excerpt, description, attribution or full path is projected. Kind, filename ID, available display name, fixed owned source, artifact ID, exact digest/native revision and observed UTC are retained. Complete native label/details payloads fit 64 KiB UTF-8 using deterministic explicit Next pages when necessary; no partial-success truncation or durable index is introduced.
Rows are observations, not disk-current integrity or use authority: native Inspect compares the original row against current admitted catalogue identity and native readback. Missing/unmapped metadata cannot prove a match. Stale/replaced/changed/withdrawn choices require Refresh/reselection instead of silently showing a newer same-name file.
Search/filter/page changes preserve the open original and dirty draft, never auto-select/load the first result or activate/save/discard. Failed reload removes current availability with historical/unavailable disclosure. Source/use/call changes invalidate choices; private ownership/closure clears private controls and fences late callbacks.
Exact typed/current-name activated List/Inspect still targets the same native controls; there is no new search command grammar, model tool, reader/root permission, profile/bundled-as-owned inventory or writer. Profile-original editing remains explicitly deferred behind its existing security qualification.

**Validate open owned draft** / exact `instructions validate` or `prompts validate` observes the captured same-kind canonical original or explicitly created draft without opening sources or switching targets. It reuses ordinary Save preparation, including original BOM and any native v1 marker added for changed/new proposals; unchanged supported legacy bytes stay legacy.
Existing strict Unicode/UTF-8, complete-byte/header/metadata/versioned-prompt and observed catalogue namespace rules apply to prepared Save bytes, not just raw draft text. Bounded text-only findings expose proposal byte count/digest/actual native version and unapproved kind/source/model-context role, never body/excerpts or executable preview.
The result is historical structural compatibility only, not harmlessness, safe execution, disk freshness, approval or enablement. Source/name/kind/baseline/catalogue and private/call/input epochs fence publication and retire findings; Save independently re-prepares and confirms current bytes.
No owned-store enumeration/read/write, source reload/reconciliation, preference hold/write, activation, model/network or reference/dependency access occurs. Normal observational control receipts/traces remain; existing drafts, inventory/selection and enabled or uncertain held source state are unchanged. Unavailable sources retain explicit Reload/reconciliation recovery. Profile-original editing and broader format/writer/runtime qualification remain deferred.

**Save as new copy** / exact `instructions copy <new-id>` or `prompts copy <new-id>` is delivered only for an inspected canonical supported native owned original (instruction v1; prompt v1/v2) and its retained current draft.
It is not an import or profile-original writer. Legacy/unversioned or unsupported originals cannot be implicitly upgraded by copying.
Review freezes source-qualified identity, canonical filename, base revision/digest, current draft exact bytes/BOM, destination same-kind canonical ID/path and full proposed bytes.
Only the declared name is changed from the draft to the chosen destination ID; all other supported source formatting/metadata/attribution/body bytes are retained. Filename-derived instruction names need no metadata rewrite.
Copy reuses the admitted mutation workflow and native store `Write(expected: null)`: final destination absence and atomic create-no-replace are mandatory.
Occupied, concurrent or case-alias destinations require another ID/cancel, never the ordinary Save overwrite loop. Source disk changes do not select a newer base; current draft/name/kind/selection/input and host/privacy/call/session fences invalidate stale copy approval.
Success retains the original open draft/source and reports the separate destination identity/digest. Verified readback enters the common revision reconciliation pipeline without new profile discovery.
The copy is disabled and requires independent exact current-run explicit-use review; it inherits no activation, grants, trust or references. Cancel preserves inventory/draft.
Uncertain publication/audit/catalogue outcomes hold the destination only, never unchanged enabled original bytes, and claim no rollback.

Prompts require `name` and `description` in the editable subset. Unsupported
parameter declarations, required substitution syntax such as `${input:...}`,
tools/runtime metadata and other unsupported scalar fields are rejected rather
than expanded or silently dropped. This is not full Copilot prompt compatibility.
The existing explicit artifact route carries a managed literal prompt body as
user-request content, never system/instruction authority. All original request,
mode/context/provider and action-approval gates remain; saving or reviewing
does not start reasoning, scheduling or execution.
One kind selector operates the same native editor and dirty-close/shutdown review.
Kind plus canonical file/source identity qualifies holds, freshness and audit.
The existing instruction `instruction-revisions.json` v1 schema is unchanged;
the prompt kind has independent strict `prompt-revisions.json` v1 state. No
instruction enablement, revocation or digest migration occurs.

Native file/parent identity and exact bytes are rechecked under durable original
local-input/session admission. Complete private staging is flushed, then renamed
relative to the pinned source directory. Pre-publication cancellation leaves the
base intact; uncertain post-publication readback/audit/catalogue outcomes are held
and explicitly require reconciliation, never rollback. Saved revisions enter
disabled, source-qualified catalogue candidates; activation separately reviews
the exact bytes for explicit instruction or saved-request selection on a new request, not broad
automatic applicability or trust. Source changes/replaced identity, failures,
deletion and restored previously revoked bytes do not auto-activate. Pre-use
native freshness checking complements advisory watchers. Existing unchanged
legacy and bundled behavior is preserved.

**Disable reviewed use** / exact `instructions disable` or `instructions stop-use` (and matching `prompts` controls) is delivered for the clean inspected canonical supported native owned baseline (instruction v1; prompt v1/v2).
Separate native confirmation binds kind, source, filename, digest, native identity-qualified revision and current original-input/ownership/privacy/call/session epoch; dirty drafts remain intact with an explicit Save/Reload prerequisite.
Native source freshness is rechecked before revocation under the existing short admission gate. Old reviews cannot target a replacement, and retired activation preparation/completion or stale reload publication cannot re-enable the held revision.
Requested/terminal trusted typed audit and the control receipt precede visible completion. Strict per-kind v1 preference hold/readback is durable; cancellation, mandatory audit/storage/readback or terminal failure exposes any held/unavailable outcome without rollback, retry or previous-enable restoration.
No source writer, deletion or unregistering occurs: original bytes/BOM/metadata/DACL/filename/digest/inventory stay unchanged and bounded inspection/editing remains available.
Every new explicit selection and catalogue descriptor excludes the held revision. Already admitted reasoning retains its captured immutable content; pending unadmitted question/action choices retire without dispatch or retargeting, and mandatory source/access/privacy/action/egress guards remain.
No-change/watchers/manual/startup/pre-use reconciliation or restored old bytes cannot resurrect use. Repeated exact Disable reports already held; a later fresh exact use review enables only the current revision.
Other kinds with the same filename, unrelated copies/sources, bundled and unchanged legacy compatibility remain isolated. Disabled originals can still produce separate disabled owned copies; no profile writer, new source/grant authority, automatic applicability or full-management qualification is introduced.

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
