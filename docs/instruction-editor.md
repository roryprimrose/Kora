# Kora-owned instruction and prompt editor

## Open and select

Use the tray's **Owned instructions and prompts (native editor)** entry or type `instructions editor` / `prompts editor`.
After ordinary push-to-talk admission, `Kora, instructions editor` focuses the same native surface.
All write and use reviews remain native; voice-only confirmation and body dictation are unavailable.
No AI model, hosted account or network is involved in editing.

| Kind/source/channel | This increment |
|---|---|
| Kora-owned instructions | Native single-file create/list/inspect/edit/Validate/Rebase/Save/Save as new copy/reload/Delete, separate explicit-use review and exact native v1 Disable reviewed use |
| Kora-owned prompts | The same editor/reviews; v1 literal requests and v2 bounded required `${input:name}` text with private expanded preview and separate request admission; no instruction/system authority |
| Registered profile instructions | Existing read/selection only; no original writer or fork |
| Skills and agents | Existing bounded readers only; authoring is a separate follow-on |
| Bundled resources | Immutable; no overwrite, extraction-to-execution or inherited writer authority |
| Typed/activated controls | Focus the same native actions and exact reviews; mutation targets the open owned ID |
| Voice-only confirmation/body dictation/model refinement | Unavailable; native text editing is the recovery path |

Use **List**, select an owned ID, then **Inspect**. For a new file enter a lowercase ASCII ID
(letters, digits and hyphens, at most 64 characters; no paths or device names) and select **Create**.
Equivalent controls are `instructions list`, `instructions create example`,
`instructions edit example` and `instructions inspect example`.
`instructions cancel` dismisses the current native review, keeping the draft;
it does not undo an already-started publication. `instructions discard` opens
the same deliberate discard/reload workflow as `instructions reload`.
The editor writes only `%APPDATA%\Kora\Instructions\<id>.instructions.md`, using Kora's application-data paths.
Profile originals and bundled resources remain read-only here.
Select **Prompt** in the same editor for `%APPDATA%\Kora\Prompts\<id>.prompt.md`.
Each supported kind has its own 128-file bound and separate revision state;
changing kind asks before discarding an unsaved draft. The corresponding exact
controls use `prompts` in place of `instructions`, for example `prompts create
example`, `prompts edit example`, `prompts save` and `prompts activate`.
Noncanonical legacy filenames remain visible/inspectable and retain unchanged
legacy selection behavior, but are read-only rather than silently renamed by Save.

## Search observed owned metadata

**List** / **Refresh** explicitly enumerates the selected kind's existing bounded owned directory and runs the existing source-reconciliation pipeline. That refresh can read source files and reconcile revision preferences; typing a search or changing a use-state filter cannot.
The inventory contains at most 128 filenames for the explicit Instruction or Prompt kind, defaulting to ordinal filename order. Its source is fixed to Kora-owned roaming data. No profile, bundled, skill, agent or additional-directory inventory is added.

**Order** explicitly selects **FileNameAscending** (default), **FileNameDescending**, **DisplayNameAscending** or **DisplayNameDescending** before bounded pagination. All comparisons are ordinal and culture-independent, never natural-number sorting. Absent observed display names sort last in both directions; ties always use canonical filename ascending ordinal ([ordering](../src/Kora.Application/Definitions/OwnedDefinitionInventory.cs)).
Filename keys and display-name ties use the exact extensionless owned filename ID, preserving existing prefix behavior (`alpha` before `alpha-more`), not the appended kind extension. They preserve admitted original casing and Unicode without relaxing lowercase ASCII write/exact-owned-ID admission ([name rules](../src/Kora.Core/ArtifactInvocation/OwnedInstructionNames.cs)). Display names confer no freshness or authority.

Use **Navigation mode** to choose **Literal** (default), **ExactOwnedId** or **ExactArtifactId** over the existing snapshot ([inventory matcher](../src/Kora.Application/Definitions/OwnedDefinitionInventory.cs)).

- **Literal** retains ordinal-ignore-case substring matching over the filename (including extension), available display name or artifact ID. Spaces and punctuation are literal, not wildcards, regex, paths or executable input.
- **ExactOwnedId** matches only the canonical owned **name ID**, not the filename extension, display name, artifact ID or path.
  It reuses [owned name admission](../src/Kora.Core/ArtifactInvocation/OwnedInstructionNames.cs): at most 64 lowercase ASCII letters/digits/hyphens, no leading/trailing hyphen or reserved device name.
  Legacy noncanonical/Unicode names remain available in Literal and existing explicit inspection, not exact canonical-name navigation.
- **ExactArtifactId** matches only a mapped, revisioned row's existing validated artifact ID with ordinal case-sensitive equality.
  It reuses [artifact identity admission](../src/Kora.Core/ArtifactInvocation/ArtifactDefinition.cs): nonempty, at most 128 lowercase ASCII letters/digits/dots/hyphens.
  Unknown/unmapped rows cannot match, and names, display labels or correlation fields are not identity authority.

Exact modes require a complete nonempty ID; invalid, uppercase, partial-grammar or whitespace-padded input is rejected, never repaired. A valid but absent ID gives zero matches. Neither exact mode opens a source or grants use authority ([mode tests](../tests/Kora.Application.UnitTests/Definitions/OwnedDefinitionInventoryTests.QueryModes.cs)).
Queries accept at most 256 UTF-16 units / 512 UTF-8 bytes, with valid single-line Unicode and no control/format characters. Invalid queries show an error and no rows; they are never trimmed or truncated.

**Exact SHA-256** is an optional artifact-digest predicate for both owned kinds. Empty means **Any**; otherwise enter exactly 64 lowercase hexadecimal characters. No whitespace, uppercase, prefixes or partial digests are accepted, and input is never trimmed, case-folded or repaired. It matches only the observed artifact digest with ordinal equality, not the native revision. Unmapped/unknown digests match only Any; an unknown use state can still have a known digest.

**Exact native revision** optionally matches the entire observed native revision with ordinal equality: `64.48.48.64` lowercase hexadecimal characters, separated by three literal dots (artifact digest, directory identity, file identity, filename digest). This is not an artifact digest or a numeric revision. Empty means **Any**; malformed, partial, uppercase or whitespace-padded input is rejected without repair. Only mapped, validated candidates with a native revision can match; unknown/unmapped rows acquire no invented revision.

**Observed UTC From (inclusive)** and **Observed UTC Until (inclusive)** optionally bound the snapshot observation time. Empty means **Any** for that bound; nonempty input must be exact canonical UTC, for example `2026-10-12T00:00:00.0000000+00:00`. `Z`, nonzero offsets, missing fractional digits, whitespace and reversed ranges are rejected. Equal bounds include that exact instant. All rows in one inventory share its observation time, including unmapped rows; this is not source modification time, freshness, trust, approval, ownership or use authority.

The selected query mode, use state, exact digest, exact native revision and both observed-time bounds are AND predicates.
**Clear search**, or **Escape** while focus is in the metadata toolbar, restores **FileNameAscending**, **Literal**, the empty query, **All** use filter and **Any** digest, revision and time bounds.
Mode and query controls have explicit accessible labels and are keyboard focusable ([native controls](../src/Kora/OwnedInstructionEditorWindow.cs)).

**All**, **UseEnabled**, **ReviewRequired** and **UnavailableOrUnknown** filter only observed catalogue metadata. Held/changed revisions share ReviewRequired; the inventory cannot infer why an approval is absent. Unmapped or unrevisioned files are unknown, never implicitly enabled.
Rows disclose kind, filename ID, available display name, fixed source, artifact ID, digest, native revision and observed UTC. They contain no body/excerpt, description, attribution or full path.
The complete visible label/details payload is at most 64 KiB UTF-8. Every enumerated filename and mapped metadata row, including native revision syntax, is validated before any predicate or offset can exclude it.
**Next** exposes any omitted complete rows in deterministic pages bound to the original inventory and complete order/mode/query/use/digest/revision/time state; editing any predicate or order restarts the visible page. A changed order rejects the previous continuation ([paging and ordering tests](../tests/Kora.Application.UnitTests/Definitions/OwnedDefinitionInventoryTests.Ordering.cs)).
Zero matches means zero matches, not unavailable or a fallback. No row is silently truncated ([paging](../src/Kora.Application/Definitions/OwnedDefinitionInventory.cs)).

**Observed catalogue use state; inspect revalidates disk and exact revision.** This is not disk-current freshness, compatibility or integrity proof. Select a row and explicitly **Inspect**: admission rechecks the original snapshot/kind/file ID/digest/native revision, not a newly looked-up same-name replacement.
Changed, replaced, disappeared or expired choices refuse with Refresh/reselect recovery. Unknown rows cannot prove an exact match; explicitly enter their current filename ID for the existing admitted compatibility inspection. Exact `instructions inspect <id>` / `prompts inspect <id>` deliberately inspects the current ID, rather than claiming a historical row match.

Search/filter/page changes never open the first result or save/discard/replace the open original or dirty draft. Editing any predicate immediately retires selection/details, the prior page/continuation, validation and pending review/use controls, even when the selected row would still match. Reselect and explicitly Inspect to restore exact use-review controls; late inspection/refresh/review callbacks cannot restore retired choices. The draft stays open. **Reload** still deliberately reloads the open original after its dirty-draft review.
Changing mode or order follows the same retirement path without List/Refresh/Inspect, save/activation/delete/copy or draft loss. Order edits also retain the exact frozen baseline, retire open Compare and never confer navigation/use authority or persist a preference ([native ordering tests](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.InventoryOrdering.cs)).
An open Compare retires, but a separately requested **Compare** can still show the admitted frozen historical baseline and exact unsaved raw draft; it does not establish current source state ([native lifecycle tests](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.Inventory.cs)).
Catalogue/use/source/call changes invalidate choices. Failed refresh clears availability and labels historical observations unavailable; it does not offer a last-good inventory as current. Privacy/ownership/closure clears query, rows, selection and private editor text, and late callbacks cannot repopulate it.
Typed/current-name activated `list` focuses this same native filter route; no typed search grammar or voice-only approval is advertised. Filtering creates no index, persistence, model/network call, watcher restart, source write or activation. Predicate input remains private ephemeral metadata/correlation only: no logging, audit payload, activity renewal, speech, clipboard or export; it never grants use authority, admits a revision, overrides content identity or proves current disk integrity.

## Edit, review and save

The native source text box edits the complete supported file. Maximum original/replacement size is 64 KiB,
strict UTF-8. Original UTF-8 BOM and source line endings are retained. The supported scalar header has
at most 16 fields/4096 UTF-8 bytes: `kora-format: 1` (instructions/literal prompts) or `kora-format: 2` (owned prompts only), `name`, `description`, optional `attribution`.
Compatible legacy headers without the version marker can round-trip unchanged.
A changed Save explicitly includes the v1 marker in the reviewed proposed source, retaining unrelated metadata/BOM/line endings.
Unknown metadata, scopes or
unsupported formats are not silently removed; use an external editor. Copying an unsupported source is unavailable.
The editable prompt subset requires `name` and `description`. New prompt drafts explicitly use native v2; existing v1 bodies and readers are not silently changed.
Native v2 supports only required `${input:name}` literal markers in the body; unsupported markers/declarations/defaults, tools/model/agent references and other metadata remain incompatible. This is
not full Copilot prompt-format support. Original unchanged legacy reader behavior is retained.

## Prepare required native prompt inputs

Create/edit a Prompt with `kora-format: 2`, for example this source:

```text
---
kora-format: 2
name: compare
description: Compare two explicit topics.
---
Compare ${input:first} with ${input:second}.
Return to ${input:first}.
```

Save exactly reviewed source, then separately **Review use** (`prompts activate`). Saving or copying never enables the new revision.
Explicit `/prompt compare`, `/compare` or `Kora, use compare` selects that exact enabled owned revision and opens the private native required-input form. Typed/voice inline parameters are refused; selection does not generate an answer.
Names are lowercase ASCII letters/digits/hyphens, start with a letter, and have at most 64 characters. At most eight distinct required inputs appear in first-occurrence order; repeated markers share one field. Blank/whitespace-only input requires correction.
Fields preserve exact native text/newlines/case/Unicode, bounded to 512 UTF-16 units / 2048 UTF-8 bytes each. **Preview complete expanded USER REQUEST** displays the entire expansion, bounded to 4096 UTF-16 / 16384 UTF-8 bytes. Oversize/invalid values are rejected, not normalized; no defaults or hidden reads exist.
Values containing `${input:name}` remain literal. No expression/environment/file/secret/clipboard/network/function evaluation or recursive expansion occurs. The preview is inert plain text, not Markdown evaluation, system instructions, command dispatch, model qualification or approval.
Editing any field clears the old preview and disables Use until another explicit preview. Source/review/catalogue/native identity/input/call/private ownership changes retire the form; missed watcher changes are checked again immediately before preview and use.
**Use reviewed request (separate admission)** sends only the captured full `UserRequest` to a fresh host model-admission check. Original source bytes/digest/native revision remain separate from the expansion digest; no body is replaced in the catalogue.
Generation is currently unavailable: the existing artifact control task has no qualified model-task source classification (`Denied / HostAdmissionClosed`), and there is no qualified production adapter/model. Neither the rejected Ollama 0.35.1/qwen3 nor 0.40.2 is a recovery path. Use reports actual admission without HTTP/inference, installation, authentication fallback or action execution.
Cancel/window close/private closure clears fields and expansion immediately; pending admitted source work is cancelled and joined before handoff/exit releases owned resources. Original-input fencing includes a new activated request even when typed command text is unchanged, and captures before awaited disk validation.
Values are never autofilled, logged, audited as content, separately stored as form values, saved into preferences/templates or automatically copied/exported/spoken. Preview and Use write no body. Only the separate explicit conversation confirmation below retains the approved expanded text. Existing v1 literal prompt selection remains on its existing route.

## Submit an expanded request to one exact session

1. Select an enabled exact owned-v2 prompt, enter native fields and **Preview complete expanded USER REQUEST**.
1. **Refresh session metadata** and explicitly select one existing Active session. There is no default target, name/focus/voice routing, implicit Resume or new-session fallback. **Next metadata page** reads another bounded page: 25 rows, complete 64 KiB, metadata only.
1. **Review submission to exact Active session** displays the entire expanded USER text and immutable target ID/name/generation/metadata revision. Original source ID/kind/native revision/digest and independent expanded digest are volatile observations, not authority.
1. Separately confirm **Submit this expanded request to exact session** only if you want that exact body persisted as one USER conversation message in the chosen session's private readable SQLite authority. This is not an encryption claim. Existing retention and local best-effort reject-only credential screening apply; arbitrary secrets may be missed.

Cancel retention review writes no conversation message and keeps eligible memory input. Input, template/native identity/digest/use/catalogue, private/call/ownership, target/revision or focus-lifetime changes withdraw review; refresh/reselect/review rather than retargeting silently.
Submission repeats native source freshness and uses the existing conversation service once, not old Use as well. Only approved expanded body is retained: no hidden field record, original template/path or durable template provenance. No source bytes are overwritten and no existing saved draft or unrelated Sessions composer buffer is saved/cleared.
Known completion says **Message retained; model generation unavailable**, with exact message sequence/request/task IDs for **Sessions > Read conversation** and task inspection. An unconfirmed attempt may already be retained: inspect those exact receipts, never automatically retry or infer rollback. The submitted form is retired after a known receipt.
No assistant answer, generation, model/network/authentication/tool execution or rejected-local runtime fallback is enabled. Source checking and SQLite publication are not a global filesystem/database transaction.

**Save** (or `instructions save`) shows full original/proposed source and both exact digests.
Confirm only after reviewing metadata, body and attribution. **Delete** / `instructions delete`
has its own destructive exact-original review; source removal is not deletion.
No rename, restore, profile fork, script, agent-execution or generic file-editor controls are offered.

## Validate the open draft without saving

**Validate**, `instructions validate` or `prompts validate` (also with the current assistant-name prefix) observes only the currently open same-kind owned draft. Explicitly Inspect a supported canonical original or Create a new draft first. Validation never opens a named file, switches kind or follows text in the ID box to another path.

Validation uses the exact ordinary Save preparation: original BOM, complete source/newlines/attribution, strict Unicode/UTF-8, supported scalar metadata, literal prompt rules and current observed command/spoken-name collisions. Unchanged compatible legacy source stays legacy; changed/new legacy proposals include the native v1 marker. The 65536-byte and 16-field/4096-byte header limits therefore apply to the prepared proposal, including marker/BOM overhead, not merely the raw text box.

The bounded text-only summary reports structural compatibility, fixed owned ID/kind/source, intended model-context role, unapproved review state and, when supported, exact prepared byte count/digest/BOM/format. Errors contain a local reason, never body excerpts. No Markdown/HTML rendering, expression execution, substitutions, reference/dependency reads, model or network is involved.

This is a historical structural observation, not semantic harmlessness, safe execution, permission, current disk integrity or activation. Input/baseline/kind/catalogue/call/private-ownership changes clear or retire it; late callbacks cannot restore a closed/private result. Save independently prepares the current proposal and requires its own exact confirmation; the summary is not an approval token.

Validation makes no owned-store List/Read/Write, source reload, revision-preference write/hold, source mutation or use-state change. Ordinary nonmutating host control intent/terminal receipts and correlated diagnostics remain. Original bytes, draft/dirty state, inventory/search/selection and existing enablement or uncertain-publication holds are retained. Source-unavailable observations disclose that Reload/reconciliation is still required before Save or use. Profile-original editing remains unavailable.

## Compare the open draft without saving

**Compare**, `instructions compare` or `prompts compare` (including the current assistant-name prefix) uses only the open same-kind inspected owned baseline and current memory draft, or an explicitly created new draft. No named argument, source opening, kind switch, draft discard, disk reload or conflict flow is supported.

The [passive comparison](../src/Kora/OwnedInstructionEditorWindow.cs) shows the whole frozen historical original on the left and exact raw memory draft on the right in inert read-only native source panes. Missing/new baselines are labelled explicitly. BOM is separate metadata, not a visible inserted character; all original metadata, attribution, whitespace and newlines remain in the panes.

Each [pane-local Find](../src/Kora/SnapshotComparisonFindPane.cs) has its own private query, **Find first**, **Find next**, **Find previous** and **Clear Find**. Missing/new baselines have no searchable source; their Find controls are disabled, not backed by placeholder text. An empty but present raw draft is searchable and truthfully yields no match for a valid query.

[Literal Find policy](../src/Kora.Core/Context/SnapshotLiteralFind.cs): case-sensitive ordinal text with exact spaces; no regex, normalization, case folding, forward overlap, wrap or count. Queries require nonempty, well-formed one-line text without unsafe controls, at most 256 UTF-16 characters and 512 UTF-8 bytes. Invalid input is reported without truncation or repair.

One inert match shows zero-based end-exclusive UTF-16 `[start, end)`. No-match and direction-end outcomes differ. At an end, the cursor permits reversing but no old match stays visible. Query edits retire outcome/match/cursor; restart with **Find first** ([tests](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.ComparisonFind.cs)).

Within each pane: **Ctrl+F** focuses query, **Enter** finds first, **F3** next, **Shift+F3** previous; **Escape** closes Compare. Tab reaches native controls and the exact match. Full source is not selected, shortened or rewritten. Query/status keeps native typography; the match follows source monospace, not global scaling ([controls](../src/Kora/SnapshotComparisonFindPane.cs)).

The [shared comparison rules](../src/Kora.Core/ArtifactInvocation/ArtifactSourceComparison.cs) report exact UTF-8 digest/byte equality and changed parsed fields, not a line diff or patch. The raw draft uses the baseline's inherited BOM policy. Each complete pane is bounded to 65536 UTF-8 bytes including BOM. Invalid Unicode and over-budget drafts are rejected without shortening or replacing the editor buffer.

Raw text is **not prepared Save bytes**. A separately labelled structural summary uses [the same Validate/PrepareSave policy](../src/Kora.Application/Definitions/OwnedInstructionEditorActions.Validation.cs), including any legacy native marker. Structurally invalid bounded text remains visible verbatim with an explicit preparation error; unavailable parsed fields are not presented as successful comparison or Save validation.

Only literal Find and Close are available: no save/copy/delete/activation, clipboard/export, model/egress, approval/use token, persisted mutation or meaningful activity renewal. This historical observation proves no disk integrity or semantic safety. Save/conflicts remain separately admitted and reviewed ([comparison](../src/Kora.Application/Definitions/OwnedDraftComparison.cs)).

Input, draft, baseline, ID/kind/selection, catalogue/source/call and ownership changes retire Compare. Close, owner/private/eligibility retirement and replacement clear queries, matches, source and callbacks. Find logs/persists no query or source ([lifetime checks](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.ComparisonFindLifetime.cs)).

[Focused native tests](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.Comparison.cs) cover this bounded control; profile-original editing, model generation, algorithmic diff and full matrix/installed/hardware/accessibility qualification remain unavailable.

## Save an owned draft as a new copy

Inspect a supported canonical **native owned** source (instruction v1; prompt v1/v2). Edit its draft, enter a different canonical ID in **new-copy-owned-id**, then choose **Save as new copy**.
`instructions copy new-id` and `prompts copy new-id` (also prefixed with the current assistant name) open the same native review. The command kind must match the open original; it never switches/discards the draft.
Review the exact source ID/path/base revision and digest, frozen current draft, destination ID/path, BOM and complete proposed bytes.
Only the draft's declared `name` is replaced with the chosen new ID; supported metadata, attribution, body, BOM and line endings are retained. Missing instruction names remain filename-derived. No attribution or provenance is invented.
Legacy files without `kora-format: 1`, noncanonical originals and unsupported metadata remain unavailable for copy rather than being implicitly upgraded or imported.

The destination must be absent at final comparison and publication uses atomic create-no-replace. Occupied, concurrently created or case-aliased destinations cannot be overwritten: choose another new ID or cancel. Ordinary Save's accepted narrow replacement race does not apply to copy creation.
Cancel writes nothing and keeps the original draft. Success keeps the original source and open draft intact and shows the new ID/digest receipt. Explicitly List/Inspect the destination when ready, acknowledging any dirty original before switching, and independently **Review use**.
Copy does not save, discard, rename or delete the source, inherit its activation/grants, or retarget references. Disk changes do not silently replace the frozen reviewed source/draft.
Changing draft, kind, selection, name, current command input, ownership or call/privacy/session admission invalidates copy review.
A failed or unconfirmed publication can leave a durable destination; inspect/reconcile that ID explicitly. No rollback is claimed and the original's unchanged reviewed use is not held by the copy attempt.

## Resolve a conflict

Kora checks disk immediately before publication. A detected changed, replaced or missing base presents:

- **Reload from disk (abandon changes)**: explicitly discard the current draft and inspect disk.
- **Overwrite with Kora changes**: explicitly review replacing the observed conflict with Kora's exact bytes.
- **Cancel (keep draft)**: leave the draft intact and write no owned definition file.

Use the separate **Rebase** action when you want to retain the complete raw draft while explicitly changing its inspected baseline, rather than discarding it with Reload ([rebase lifecycle](../src/Kora/OwnedInstructionEditorController.Rebase.cs)).

Another detected change before the final comparison prompts again. There is no automatic merge or retry.
You have chosen optimistic saving: an edit arriving after that final comparison but before complete-byte
atomic replacement may be overwritten. This narrow content race does not allow writes through links,
replaced source directories, protected roots or arbitrary paths.

**Reload** / `instructions reload` also asks before abandoning a dirty draft.
Closing the dirty editor or exiting Kora offers **Save / Discard / Cancel**.
Privacy closure clears private editor text and retires pending actions; cancelled or failed ordinary publication
may already have changed disk. In that case Kora holds further writes/use until explicit Reload/reconciliation,
does not claim rollback, and reports recovery instead of false success.

## Rebase the open draft without saving

**Rebase**, `instructions rebase` or `prompts rebase` (including the current assistant-name prefix) acts only on the already open same-kind supported owned source.
It accepts no ID/body argument, does not open a source implicitly, and rejects new/missing baselines, changed ID/kind and uncertain publication or unavailable source state ([controller](../src/Kora/OwnedInstructionEditorController.Rebase.cs)).

The explicit action reads the latest exact owned source and freezes three separate complete bodies: the old inspected baseline, the newly observed source, and the exact raw unsaved draft.
The [native review](../src/Kora/OwnedInstructionRebaseReviewWindow.cs) labels them separately and discloses filenames, kind, owned/artifact IDs, revisions, source digests/byte counts/BOMs, exact revision/byte equality and shared changed-field observations.

The draft is never merged, trimmed, canonicalized or replaced by prepared Save bytes. Structurally invalid, empty or whitespace-only raw text remains reviewable within the existing complete comparison bound; invalid Unicode or over-budget text is rejected without truncation.
Shared Save preparation provides a separately labelled structural observation against the latest baseline, not semantic approval ([typed observation](../src/Kora.Application/Definitions/OwnedDraftRebase.cs)).

**Adopt latest baseline only** explicitly rereads the source and requires the exact observed revision, including native file/directory identity and byte digest. Missing/deleted, incompatible, wrong-kind or mismatched-ID sources fail closed.
A same-ID/same-format external edit or replacement still changes revision; observed catalogue/control ABA retires review. A change and restoration of an identical revision with no observed invalidation cannot be detected as historical change ([workflow](../src/Kora.Application/Definitions/OwnedInstructionEditorActions.Rebase.cs)).

Confirmation adopts only the latest baseline in editor memory and preserves the entire raw draft. It clears prior editor validation/comparison and use-review eligibility; it grants no use, permission, approval or runtime authority. Cancel, Escape or closing the review preserves the prior baseline and draft.
Source/control/catalogue, draft/ID/kind/selection/input, ownership, call/privacy and editor-lifetime changes withdraw the proposal; retired windows clear bodies and reject late confirmation ([native regression cases](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.Rebase.cs)).

Rebase writes no source, revision preferences or use state and performs no automatic catalogue reload. Ordinary correlated host control intent/terminal receipts remain. It cannot resolve an uncertain Save by assumption: use the existing explicit exact reread/reconciliation recovery contract.
**Save** remains a separate ordinary full exact confirmation and optimistic comparison against the adopted baseline, and can conflict again ([portable regression cases](../tests/Kora.Application.UnitTests/Definitions/InstructionEditorTests.Rebase.cs)).

## Saving is not applying

Saved/changed instructions and prompts remain disabled. **Review use** /
`instructions activate` or `prompts activate` separately reviews
the exact clean current revision and permits explicit selection for a new request.
That reviewed use is limited to the current unlocked host run; privacy closure or restart withholds it again.
This does not make the text host policy, grant a tool, execute scripts, broaden a source's consent or
inject new guidance into an existing task. A later external edit, replaced source identity, invalid file,
deletion or failed reload withholds managed use; restoring old bytes does not resurrect approval.

## Disable reviewed use without editing the source

Inspect the exact canonical supported native owned instruction (v1) or prompt (v1/v2), then choose **Disable reviewed use**, `instructions disable` / `instructions stop-use`, or the corresponding `prompts` control.
Current-assistant-name activated controls focus the same native confirmation; they do not accept body approval, a name-based replacement target or an arbitrary path. No model/network is required.
The open inspected baseline must be clean: a dirty draft is retained with an explicit Save/Reload requirement, never silently saved or discarded.

The review shows kind, owned source/file, digest, native identity-qualified revision and actual enabled/held status. Confirming holds only that exact revision and returns its control receipt.
Cancel retains the source, draft and reviewed-use state. Changes to kind, selection, name, draft, original command input, ownership, session generation or privacy/call admission invalidate review. A changed/replaced source requires Reload; an old receipt cannot select its replacement.

Disable never calls the definition writer or deletes/unregisters the source. Bytes, UTF-8 BOM, supported metadata, DACL, filename, digest and inventory remain unchanged; the disabled source stays inspectable/editable through existing bounded reads.
Typed/slash/native command selection, current-name activated use and admitted catalogue/model descriptors no longer offer it for new requests. Already admitted reasoning retains captured immutable content; pending unadmitted questions/action choices expire without dispatch, reroute or replay.
Existing source/access/consent, privacy and action/egress checks remain mandatory; disable is not permission to keep using a denied source.

The held revision is durable in the existing per-kind v1 revision preferences. Watcher/manual/no-change reload, restart, pre-use checking or restored old bytes cannot re-enable it. Repeating exact Disable truthfully reports already held; only a fresh exact **Review use** can enable the current revision for a later request.
Same filenames in another kind, unrelated sources/copies and immutable bundled definitions are unaffected. Copies of disabled originals still enter independently disabled.
Mandatory audit, preference publication/readback or terminal-bookkeeping failure reports held/unavailable recovery, never success or rollback. A revocation may already be durable: explicitly Reload/read back before a fresh review; no automatic retry or previous-enable restoration.
Legacy/profile original editing and broader applicability remain deferred; this control does not create grants, change source consent or qualify the full Definitions matrix.

After review, `/instruction <declared-command>` or activated explicit **use** resolves the same catalogue revision.
For a prompt, use `/prompt <declared-command>` or the same activated explicit
**use** route. A managed literal prompt is sent as explicitly selected user-request
content, never system instructions. Creation/review/save do not start a request;
actual use retains ordinary provider readiness, context/mode and action-approval checks.
Names cannot shadow other catalogue entries or reserved editor controls. Broader automatic instruction scopes,
profile/skill/agent editing, broader prompt formats and model dictation remain future increments. Native v2 required literal input preparation is delivered above; generation remains unavailable.
