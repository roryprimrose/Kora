# Kora-owned instruction and prompt editor

## Open and select

Use the tray's **Owned instructions and prompts (native editor)** entry or type `instructions editor` / `prompts editor`.
After ordinary push-to-talk admission, `Kora, instructions editor` focuses the same native surface.
All write and use reviews remain native; voice-only confirmation and body dictation are unavailable.
No AI model, hosted account or network is involved in editing.

| Kind/source/channel | This increment |
|---|---|
| Kora-owned instructions | Native single-file create/list/inspect/edit/Save/reload/Delete and separate explicit-use review |
| Kora-owned prompts | The same editor and reviews for literal saved user requests; no parameters/substitutions or instruction/system authority |
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

## Edit, review and save

The native source text box edits the complete supported file. Maximum original/replacement size is 64 KiB,
strict UTF-8. Original UTF-8 BOM and source line endings are retained. The supported scalar header has
at most 16 fields/4096 UTF-8 bytes: `kora-format: 1`, `name`, `description`, optional `attribution`.
Compatible legacy headers without the version marker can round-trip unchanged.
A changed Save explicitly includes the v1 marker in the reviewed proposed source, retaining unrelated metadata/BOM/line endings.
Unknown metadata, scopes or
unsupported formats are not silently removed; use an external editor. Fork is not implemented.
The editable prompt subset requires `name` and `description` and a literal body.
Parameter declarations, `${...}` substitutions, tools/model/agent references and
other unsupported metadata are incompatible, not silently interpreted. This is
not full Copilot prompt-format support. Original unchanged legacy reader behavior is retained.

**Save** (or `instructions save`) shows full original/proposed source and both exact digests.
Confirm only after reviewing metadata, body and attribution. **Delete** / `instructions delete`
has its own destructive exact-original review; source removal is not deletion.
No rename, restore, backup-copy, script, agent-execution or generic file-editor controls are offered.

## Resolve a conflict

Kora checks disk immediately before publication. A detected changed, replaced or missing base presents:

- **Reload from disk (abandon changes)**: explicitly discard the current draft and inspect disk.
- **Overwrite with Kora changes**: explicitly review replacing the observed conflict with Kora's exact bytes.
- **Cancel (keep draft)**: leave the draft intact and write no owned definition file.

Another detected change before the final comparison prompts again. There is no automatic merge or retry.
You have chosen optimistic saving: an edit arriving after that final comparison but before complete-byte
atomic replacement may be overwritten. This narrow content race does not allow writes through links,
replaced source directories, protected roots or arbitrary paths.

**Reload** / `instructions reload` also asks before abandoning a dirty draft.
Closing the dirty editor or exiting Kora offers **Save / Discard / Cancel**.
Privacy closure clears private editor text and retires pending actions; cancelled or failed publication
may already have changed disk. In that case Kora holds further writes/use until explicit Reload/reconciliation,
does not claim rollback, and reports recovery instead of false success.

## Saving is not applying

Saved/changed instructions and prompts remain disabled. **Review use** /
`instructions activate` or `prompts activate` separately reviews
the exact clean current revision and permits explicit selection for a new request.
That reviewed use is limited to the current unlocked host run; privacy closure or restart withholds it again.
This does not make the text host policy, grant a tool, execute scripts, broaden a source's consent or
inject new guidance into an existing task. A later external edit, replaced source identity, invalid file,
deletion or failed reload withholds managed use; restoring old bytes does not resurrect approval.

After review, `/instruction <declared-command>` or activated explicit **use** resolves the same catalogue revision.
For a prompt, use `/prompt <declared-command>` or the same activated explicit
**use** route. A managed literal prompt is sent as explicitly selected user-request
content, never system instructions. Creation/review/save do not start a request;
actual use retains ordinary provider readiness, context/mode and action-approval checks.
Names cannot shadow other catalogue entries or reserved editor controls. Broader automatic instruction scopes,
profile/skill/agent editing, prompt parameters and model dictation are future increments, not current features.
