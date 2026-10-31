# Instruction and prompt editor

An **instruction** is reusable guidance for an answer; a **prompt** is a reusable
request. You can edit either without AI. Saving is separate from enabling or
using it, and every write needs exact confirmation after the complete native source review.

Start with [open and select](#open-and-select), [edit and save](#edit-review-and-save),
[required prompt inputs](#prepare-required-native-prompt-inputs), or
[resolve a conflict](#resolve-a-conflict). **Local generation/refinement is unavailable in
this build** because no local model is approved.

## Open and select

Use **Settings > Custom content > Owned instructions and prompts...** or type `instructions editor` / `prompts editor`.
After ordinary push-to-talk admission, `Kora, instructions editor` focuses the same native surface.
Write and use reviews remain native. One already reviewed existing owned Instruction Save can use the [exact spoken answer](#confirm-one-reviewed-instruction-save-by-voice); other mutations remain native.
One foreground owned Instruction also has an explicit [literal body dictation route](#dictate-literal-instruction-body-text), separate from Save approval; selected prompt input values retain their separate [literal voice route](#prepare-required-native-prompt-inputs).
Manual editing needs no model, hosted account or network. Explicit [local body refinement](#refine-the-current-owned-instruction-body) uses the separately admitted local-only model route; production qualification remains unavailable ([composition](../src/Kora/Program.cs)).

| Kind/source/channel | What this editor supports |
|---|---|
| Kora-owned instructions | Native single-file create/list/inspect/edit/Validate/Rebase/Refine/Save/Save as new copy/reload/Delete, separate [reviewed rename/recovery](#rename-one-owned-instruction), explicit-use review and exact native v1 Disable reviewed use ([refinement](../src/Kora/OwnedInstructionEditorController.Refinement.cs)) |
| Kora-owned prompts | The same editor/reviews; v1 literal requests and v2 bounded required `${input:name}` text with private expanded preview and separate request admission; no instruction/system authority |
| Personal profile instructions | Open one already registered/read-consented native v1 original in this editor; manually edit, compare, separately confirm Save, resolve content conflicts or Reload. Independent disabled Kora copy remains available ([original workflow](../src/Kora.Application/Definitions/ProfileInstructionEditorWorkflow.cs), [copy workflow](../src/Kora.Application/Definitions/ProfileInstructionForkWorkflow.cs)) |
| Skills and agents | Use the separate [skill editor](owned-skill-authoring.md) or [agent profile editor](agent-profile-authoring.md); this editor does not write them |
| Bundled resources | Immutable; no overwrite, extraction-to-execution or inherited writer authority |
| Typed/activated controls | Focus the same native actions and exact reviews; mutation targets the open owned ID |
| Spoken save confirmation | One complete display-reviewed existing canonical owned Instruction, same-source normal Save only; [exact answer](#confirm-one-reviewed-instruction-save-by-voice), not generic assent |
| Literal instruction-body dictation | Explicit hold, frozen body caret/range, complete native review, volatile acceptance and separate normal Save; requires existing qualified local capture ([owner](../src/Kora/OwnedInstructionBodyDictation.cs)) |

Use **List**, select an owned ID, then **Inspect**. For a new file enter a lowercase ASCII ID
(letters, digits and hyphens, at most 64 characters; no paths or device names) and select **Create**.
Equivalent controls are `instructions list`, `instructions create example`,
`instructions edit example` and `instructions inspect example`.
`instructions cancel` dismisses the current native review, keeping the draft;
it does not undo an already-started publication. `instructions discard` opens
the same deliberate discard/reload workflow as `instructions reload`.
Owned instruction writes use only `%APPDATA%\Kora\Instructions\<id>.instructions.md`, using Kora's application-data paths.
The [registered original operation](#edit-one-registered-profile-instruction-original) additionally writes only its exact existing profile file; bundled resources remain read-only. The separate [profile copy operation](#create-a-disabled-kora-copy-of-a-personal-instruction) never changes an original ([source owner](../src/Kora.Windows/ArtifactDiscovery/WindowsCustomizationStore.InstructionOriginal.cs)).
Select **Prompt** in the same editor for `%APPDATA%\Kora\Prompts\<id>.prompt.md`.
Each supported kind has its own 128-file bound and separate revision state;
changing kind asks before discarding an unsaved draft. The corresponding exact
controls use `prompts` in place of `instructions`, for example `prompts create
example`, `prompts edit example`, `prompts save` and `prompts activate`.
Noncanonical legacy filenames remain visible/inspectable and retain unchanged
legacy selection behavior, but are read-only rather than silently renamed by Save.

Supported source files refresh automatically when changed outside Kora.
This also works if a supported editor source tree is created after Kora starts,
even when its parent folders were missing, or a supported profile folder is
renamed or deleted (including folder names containing dots). Existing source
and discovery-depth limits still apply; Kora does not scan unrelated profile
content or create missing folders. No restart or configuration toggle is needed.
A changed source retires stale requests, questions and approvals. Discovery
never invokes a definition; changed owned instructions and prompts still need
separate review and enablement before fresh explicit use. Malformed sources
report a reload failure rather than claiming the catalogue is current.

## Rename one owned instruction

Inspect one supported saved **Instruction** and leave its draft clean.
Enter a distinct lowercase ID in **new-instruction-id (rename)**, then choose
**Rename**, or type `instructions rename new-id`. After ordinary push-to-talk
admission, `Kora, instructions rename new-id` enters the same review. Paths,
reserved controls, case-only aliases, case-sensitive/unconfirmed directories, prompts, legacy/unsupported files,
profile originals and unsaved draft edits are not rename targets
([native owner](../src/Kora/OwnedInstructionEditorController.Rename.cs)).

Review the old/new file and invocation identities, complete old/new source,
and affected observed agent declarations. Choose **Reviewed exact rename**,
then separately **Confirm rename**. Only the canonical filename and necessary
declared name change; BOM, line endings, formatting, attribution and unrelated
body/metadata/file bytes stay unchanged. Destination replacement and automatic
reference rewrites are never offered
([proposal](../src/Kora.Core/ArtifactInvocation/OwnedInstructionRename.cs)).

The old ID becomes unavailable; the new source opens **disabled/held** and
needs a fresh, separate **Review use**. Explicitly Inspect/Reopen the new ID
to check its current resolution status. Old dependent references remain old
and cannot resolve to the new source by name. Saving or allowing the new source
does not transfer grants, applicability or dependent approval. Admitted content
is never switched mid-task; existing source/access and stricter agent-bundle
checks can still retire continuation
([admitted operation](../src/Kora.Application/Definitions/OwnedInstructionEditorActions.Rename.cs)).

Rename is a recoverable multi-step operation, **not an atomic transaction**:
private intent, native move of the original into a recovery slot, no-replace
publication, exact readback and cleanup. Another application can observe
intermediate absence. Changed source/destination, replaced parents/aliases,
withdrawn privacy/input admission and uncertain publication do not authorize
overwrite, automatic retry or rollback
([writer](../src/Kora.Windows/ArtifactDiscovery/WindowsOwnedDefinitionFileAccess.Rename.cs)).

After an interruption, choose **Recover rename** or
`instructions recover-rename`. Inspect the original proposal and exact
observed phase, then separately confirm continuation. No recovery replays
at startup; ordinary owned Instruction use/writes remain withheld while intent
is unfinished. Changed external bytes, identities or corrupt recovery data
remain held rather than being overwritten. If no intent remains, explicitly
Inspect the old/new IDs and Reload/reconcile; this does not imply rollback
([recovery contract](../Design/Definition_Management.md#bounded-recoverable-owned-instruction-rename)).

## Edit one registered profile instruction original

First select one existing personal instruction in **Customizations** and explicitly review its exact-source registration/read consent. Keep that window open. Registration is memory-only for that source owner's lifetime and is not write approval. In the **Instruction** editor choose **Open registered profile original**, check the displayed shared source and choose **Open exact registered original**; cancellation reads no content. This editor cannot discover or register a new source, choose an arbitrary path or consent on your behalf ([source selection](../src/Kora/OwnedInstructionEditorController.ProfileOriginal.cs)).

Only canonical immediate `*.instructions.md` files in Code or Code - Insiders `User\prompts` or `User\profiles\<profile>\prompts` are admitted. The existing writer accepts strict UTF-8, at most 65,536 bytes including the inherited BOM, native `kora-format: 1` and bounded scalar `name`, `description` and optional `attribution` (at most 16 header fields / 4,096 header bytes). Unknown metadata, `applyTo`/scope/globs, hooks, legacy/unversioned and other formats are explicitly refused, not dropped, upgraded or rewritten. Unsupported profiles remain unavailable; use an external editor or the independent compatible Kora-copy operation ([format validation](../src/Kora.Core/ArtifactInvocation/ArtifactFileDocument.ProfileCopy.cs), [native source admission](../src/Kora.Windows/ArtifactDiscovery/WindowsCustomizationStore.InstructionOriginal.cs)).

Edit the complete exact source directly; original BOM and unedited text/metadata/line endings are retained. **Validate**/**Compare** show a memory-only full old/new comparison. **Save** (or `instructions save` on this open source) separately shows the complete old/new source and changed fields, then requires **Confirm save original**. The host chooses the same existing file; the dedicated Customizations writer stages, flushes, atomically replaces relative to its pinned directory and checks exact readback. Saving changed content disables it; it does not infer harmlessness, applicability, activation, grants or model disclosure. This route does not create/delete/rename originals, refine through a model, or enable them; owned editing and Save as new copy remain separate ([native review](../src/Kora/NativeProfileInstructionMutationReview.cs), [authoritative writer](../src/Kora.Windows/ArtifactDiscovery/WindowsCustomizationStore.InstructionOriginal.cs)).

A detected content conflict offers **Reload from disk (abandon changes)**, **Overwrite with Kora changes**, or **Cancel (keep draft)**. Overwrite opens another complete exact Save review against the observed external base; a further detected change prompts again. Final comparison is optimistic, not conditional content CAS: a concurrent edit or same-name replacement after the final check can be overwritten at the pinned directory name, never followed to another destination. Replaced identities detected before that check, aliases, hardlinks, reparse points, changed roots, inaccessible or missing originals are unavailable, not overrideable conflicts ([conflict workflow](../src/Kora.Application/Definitions/ProfileInstructionEditorWorkflow.cs), [native boundary regressions](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/WindowsCustomizationStoreTests.InstructionOriginal.cs)).

Cancelled reviews retain the draft without approving a replacement. Dirty closing/shutdown still offers **Save/Discard/Cancel**. Registration withdrawal/renewal, original input/channel changes, privacy, call or host-ownership retirement invalidate pending source authority. Unconfirmed publication remains held and blocks more saves; explicitly review the exact registration again in Customizations, then **Reload**, deliberately discarding the retained draft when prompted, to reconcile the actual file. No rollback or automatic retry is claimed ([same-editor lifecycle regressions](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.ProfileOriginal.cs)).

The owned-instruction exact spoken Save question is never offered for a registered profile original, even when both operations are available in the same editor. Use **Confirm save original** for that separate native review.

## Dictate literal instruction body text

Create or Inspect one canonical Kora-owned **Instruction** and place the caret, or an entire selected range, after the frontmatter delimiter. In that foreground editor, focus **Hold to dictate body** and hold Space/Enter or the pointer. This explicit gesture freezes the exact editor, source, body, draft and caret/range before source verification or capture; it does not intercept ordinary command/PTT gestures ([target admission](../src/Kora/OwnedInstructionEditorController.Dictation.cs)).

Release the hold to finish. After confirmed microphone release and another exact-source check, expand **Review literal body insertion** to inspect the frozen body, unchanged literal transcript and complete proposed source. **Accept literal body insertion** inserts at the frozen caret or replaces only the selected body range in volatile draft memory. **Cancel body dictation**, Escape or no final text leaves the draft unchanged ([native lifecycle](../src/Kora/OwnedInstructionBodyDictation.cs)).

Then use normal **Save** and independently **Confirm save** for the exact complete source, or explicitly choose the separate [exact spoken Save question](#confirm-one-reviewed-instruction-save-by-voice) after complete display review. Dictation never saves, enables, invokes a model/tool, answers a question or interprets spoken command words ([combined-owner regressions](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.SpokenSave.cs)).
Original metadata, source encoding/BOM and all text outside the frozen range are retained; normal validation, conflicts and dirty-close decisions still apply ([native operation and save regressions](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.Dictation.cs)).

Foreground loss, source/body/caret/range/name/kind changes, input/control/ownership changes, privacy, call/channel/device/name/topology or capture-generation changes retire the target. Native review cannot renew the original voice authority. Rejected or late text is discarded, never redirected to another field, question, session or command. Cancellation retains exclusive capture ownership until native release joins ([shared capture owner](../src/Kora.Application/Voice/ComposerDictationService.cs)).

This uses the existing consented microphone and qualified bounded local literal-ASR route, not a second recognizer, background capture or system-wide dictation. Missing qualification remains explicitly unavailable; this implementation does not enable a production speech profile or qualify microphone/acoustic behavior. Prompts, metadata, skills, agents, profile originals and model-selected destinations are excluded. Review text is volatile, cleared on retirement, and not archived or content-logged ([composition](../src/Kora/App.axaml.cs), [capture admission](../src/Kora.Application/ViewModels/MainViewModel.ComposerDictation.cs)).

Final transcripts retain the existing 4096-character local voice-input bound. The complete strict UTF-8 proposed source, including the original BOM when present, must still fit 65536 bytes; invalid or oversized results are rejected, not truncated ([body admission](../src/Kora/OwnedInstructionEditorController.Dictation.cs), [voice bound](../src/Kora.Core/Voice/VoiceInputLimits.cs)).

## Describe a new owned instruction

Select **Instruction**, enter a fresh canonical lowercase owned ID, then **Create** (or `instructions create example`, including current-name activated voice).
Enter the original description in **New Instruction description / existing body critique**, then select **Ask Kora: new Instruction / refine body** or the existing `instructions refine` control.
This is the existing single-file editor, not another writer or a new format ([native owner](../src/Kora/OwnedInstructionEditorController.Refinement.cs)).

Review the exact original description, fixed destination-kind/native-v1 descriptor, local tools-none disclosure and host-chosen owned destination.
Only that description and fixed descriptor go to the admitted local model; no draft, editable metadata, path, source ID, references, history or memory is sent.
Credential screening is best-effort reject-only assistance, not a secret-free guarantee ([proposal service](../src/Kora.Application/Definitions/OwnedInstructionRefinementService.cs)).

The model may return only a complete literal **body**. Kora retains the supported native-v1 header, metadata, ID, destination and format. The complete-source comparison shows all proposed bytes.
**Use proposal for next Save** stages only that source in this editor; it does not save, activate, grant permissions or execute references ([review and staging](../src/Kora/OwnedInstructionEditorController.Refinement.cs)).

Separately select **Save**, review the full source and destination again, then **Confirm save**.
The new target must remain absent: occupied or concurrently created targets reject the operation without an Overwrite fallback.
Publication uses the existing atomic create-only writer and exact readback. The saved instruction is disabled ([save workflow](../src/Kora.Application/Definitions/InstructionSaveWorkflow.cs), [native store](../src/Kora.Windows/ArtifactDiscovery/WindowsOwnedInstructionStore.cs)).

Use **List**, then explicitly **Inspect** the saved ID to reopen its exact source-qualified revision and disabled state. The original description is not retained as conversation/history or authoring authority.
Future guidance use still needs the independent exact revision/context review; authoring output is never current-turn instructions or host policy ([complete native regressions](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.Refinement.cs)).

Changed description/draft/destination/kind/source/catalogue, original input during generation, call/privacy/ownership/channel retirement, cancellation and the two-minute proposal expiry revoke outstanding work.
Late output cannot replace a newer draft. Retired staged source remains unsaved; generate/review again or explicitly Create a manual draft, never silently save a stale proposal ([lifecycle](../src/Kora/OwnedInstructionEditorController.Refinement.cs), [forbidden-effect regressions](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.NewInstruction.cs)).

Production local qualification remains unavailable. Native manual editing still works; generation reports unavailable rather than installing, sending to a hosted model, reading references or falling back. Prompt, skill, agent and personal-profile authoring retain their separate operations ([registration](../src/Kora/Program.cs)).

## Refine the current owned instruction body

Inspect one supported Kora-owned **Instruction**, keep the desired draft open, and enter a critique in **New Instruction description / existing body critique**.
Select **Ask Kora: new Instruction / refine body**, or use `instructions refine` / current-name activated `Kora, instructions refine`.
These controls target the open editor, not inline body text ([routing](../src/Kora.Application/ViewModels/MainViewModel.InstructionEditor.cs)).

Review the complete current draft and critique, then explicitly choose **Generate local body proposal**. The model receives only the exact body as untrusted LocalOnly evidence and the unchanged critique as the request. Tools, history, memory, attachments, remote fallback and reference execution are excluded. The host retains metadata, kind, name, source, BOM, encoding and writer authority ([local-context workflow](../src/Kora.Application/Definitions/OwnedInstructionRefinementService.cs)).

The next comparison shows the unchanged current draft and the **complete prepared replacement**, including host-owned metadata. **Use proposal for next Save** keeps only a volatile reviewed proposal: it does not change the editor buffer or write a file. Structural validation is not semantic harmlessness; review the entire proposed body ([native comparison](../src/Kora/OwnedInstructionEditorController.Refinement.cs)).

Select normal **Save** and separately **Confirm save** after reviewing the complete source again. Only that confirmed operation uses the existing authoritative writer and readback. A changed revision is disabled until independent exact-use review; saving never enables it. A stale base cannot be overwritten through a refinement proposal ([save workflow](../src/Kora.Application/Definitions/InstructionSaveWorkflow.cs), [save/readback regressions](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.Refinement.cs)).

Edits to the draft, critique, source identity, name or kind; cancellation; privacy/call/ownership/original-channel retirement; and expiry revoke the proposal. **Cancel refinement** and closing during generation request cancellation but retain ownership until the model actually stops. Manual work is not overwritten. A source conflict requires explicit Reload/reconciliation; retry refinement only after recovery ([lifecycle](../src/Kora/OwnedInstructionEditorController.Refinement.cs), [boundary regressions](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.OwnedInstructionRefinement.cs)).

Unavailable, denied or malformed generation leaves the current draft and unsent critique unchanged and performs no definition write. Invalid output is not fence-stripped or truncated. Unsent content and proposals remain volatile, not conversation/history/cache or diagnostic data; privacy closure clears the private controls ([implementation](../src/Kora.Application/Definitions/OwnedInstructionRefinementService.cs), [native lifecycle tests](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.Refinement.cs)).

This bounded implementation does not qualify a real provider or model. Production local registrations remain unqualified, so generation reports unavailable until independent enablement requirements are met. Prompts, skills, agents, registered profile originals, executable/hooks and arbitrary paths are outside this operation ([production registration](../src/Kora/Program.cs), [owned-source guard](../src/Kora/OwnedInstructionEditorController.Refinement.cs)).

## Create a disabled Kora copy of a personal instruction

Select **Instruction**, enter a fresh **new-copy-owned-id**, and choose **Create Kora copy from profile instruction**. Select exactly one original through the native picker. Existing editor text, its baseline and unsaved changes are not replaced by this workflow ([native integration](../src/Kora/OwnedInstructionEditorController.ProfileCopy.cs)).

This bounded operation supports immediate canonical `*.instructions.md` files in the current user's roaming `Code\User\prompts`, `Code - Insiders\User\prompts`, or `User\profiles\<profile>\prompts` beneath either editor.
It does not scan a profile, workspace or repository, register a root, or reuse skill registration consent. Other personal locations remain explicitly unavailable ([source admission](../src/Kora.Windows/ArtifactDiscovery/WindowsProfileInstructionReader.cs)).
The exact host-resolved roaming ancestry may be Hidden (such as AppData); hidden descendants/files and System or linked/reparse sources remain unavailable. Ordinary file preview keeps its separate stricter policy ([verified reader](../src/Kora.Windows/Context/WindowsLocalFileInspector.cs)).

**Allow bounded local source read** discloses the exact source and byte length before reading its body. Memory-held consent covers one capture and at most five bounded freshness checks during this copy, including at publication;
no storage, activation, model use or other read authority is granted ([read disclosure](../src/Kora/NativeProfileInstructionCopyReview.cs)).

**Cancel profile copy**, review Cancel, privacy closure, ownership loss or changed editor input retires private review and the source lease ([lifecycle](../src/Kora/OwnedInstructionEditorController.ProfileCopy.cs)).

Only strict UTF-8 native `kora-format: 1` instructions are supported: at most 65,536 whole-file bytes including BOM, 16 header fields and 4,096 header bytes. Only `name`, `description`, `kora-format` and optional `attribution` metadata are admitted.
Legacy files, unsupported metadata/versions, prompt/agent files and `SKILL.md` packages are rejected, never flattened or repaired into instructions ([shared parser](../src/Kora.Core/ArtifactInvocation/ArtifactFileDocument.cs), [profile validation](../src/Kora.Core/ArtifactInvocation/ArtifactFileDocument.ProfileCopy.cs)).

Separately review the entire original and entire proposed copy, fresh name, exact destination and disabled state. The copy preserves original BOM, body, line endings and other supported metadata, changes its name, and adds explicit source attribution while retaining original attribution.
**Confirm disabled Kora copy** writes only a new absent Kora-owned destination; original files are never edited ([copy preparation](../src/Kora.Core/ArtifactInvocation/ArtifactFileDocument.ProfileCopy.cs), [write review](../src/Kora/NativeProfileInstructionCopyReview.cs)).

Success offers the existing **List**, select-new-ID and **Inspect** path, not Enable or execution. Saving grants no applicability, inherited approval, permissions, grants, model context or egress. Any later use still requires the existing independent exact-revision review ([publication](../src/Kora.Application/Definitions/OwnedInstructionEditorActions.cs)).

Changed/replaced/busy/unreadable sources, withdrawn consent, collisions and cancellation fail explicitly. No overwrite or automatic retry occurs. If publication or later audit/catalogue completion is unconfirmed, inspect the exact destination before retrying; no rollback or success is implied.
Verified source handles are released when the operation retires ([source lease](../src/Kora.Windows/ArtifactDiscovery/WindowsProfileInstructionReader.cs), [workflow](../src/Kora.Application/Definitions/ProfileInstructionForkWorkflow.cs)).

This is an implementation-only native fork increment. Voice-only read/write confirmation, profile mutation, general import, skill/prompt/agent copying and provider/runtime qualification are not enabled ([native-only admission](../src/Kora.Application/Definitions/ProfileInstructionForkWorkflow.cs)).

## Live source-buffer size

Both owned kinds show passive live **Current displayed source buffer** counts:
actual strict UTF-8 bytes, UTF-16 code units, and Unicode scalars (not grapheme
clusters). Combining marks count separately; an astral character uses two UTF-16
units but one scalar. Metadata, spaces, displayed BOM characters and exact line
endings count. A separately retained file BOM is not in the displayed buffer.
These are not persisted-file bytes, expanded prompt/body-only counts or a
resource-package budget.

The existing whole-source file ceiling is 65,536 bytes, including any retained
file BOM. It is shown as a ceiling, not an assurance that the current draft can
be saved. There is no added whole-source UTF-16 or scalar limit. An unpaired
surrogate explicitly makes byte and scalar counts unavailable; its UTF-16 units
remain countable. Oversized, malformed and schema-invalid drafts remain editable
without truncation, normalization or repair.

Counts update with displayed-source changes and retire on private clearing or
close. They do not parse, validate, read files, refresh discovery, change reviews
or use state, write, send content, announce through speech or renew activity.
**Validate** remains a separate full structural/schema check; counts grant no
save, trust, approval or execution authority. Read-only sources stay read-only.

## Go to a line in the displayed source

Choose **Go to line** or press **Ctrl+G** in the native editor. The accessible
line input refers only to the exact currently displayed owned Instruction or
Prompt source, including an unsaved or structurally invalid draft. It shows
the current total logical line count. Enter a one-based ASCII positive integer
and choose **Go** explicitly. Typing does not navigate; **Cancel** or **Escape**
dismisses the input without changing the source selection.

Only canonical input is accepted: no whitespace, signs, leading zeros,
non-ASCII digits, overflow or numbers beyond the actual line count. No input
is trimmed, repaired or truncated. CRLF is one separator; lone CR and LF also
separate lines. An empty buffer has one empty line, and a trailing separator
creates a final empty line. Unicode characters are not line separators.
Navigation is unavailable above the existing 65,536-character owned source
bound; an oversized draft is retained unchanged, not shortened.

**Go** focuses the same native source, moves its caret to the exact line start
and brings it into view. Native focus/caret failure is reported, not treated
as success. It does not edit text, add an undo edit, load/read a file, validate,
save, rebase, activate a catalogue row or enable use. Opening/typing in the
input preserves independent source selection until explicit **Go**. Input
and line metrics are volatile, not logged, transcribed, spoken by Kora or
copied to the clipboard; the line input has no clipboard transfers.

An edit, source/ID/kind/file/copy change, editor operation, privacy/ownership
revocation, native source detachment, close or disposal retires the request.
Choose **Go to line** again explicitly for the new current buffer. Each request
has its own native Go control; a delayed click on a retired control cannot
navigate a newer request even after explicit reopening. Unknown/unavailable or read-only
sources do not gain edit, copy or use authority. Profile and bundled sources
remain outside this owned-editor workflow.

## Search observed owned metadata

### Find within selected displayed metadata details

After explicitly selecting a current inventory row, **Find in selected metadata**
focuses a separate private query. It searches only the exact displayed details
string for that selected owned Instruction or Prompt row. It never searches the
source editor, unsaved draft, other rows, hidden paths, descriptions,
declarations, resource bodies or another kind. It does not open or inspect a file.

Enter a case- and space-sensitive one-line literal, at most 256 UTF-16 units
and 512 UTF-8 bytes. Invalid Unicode, control/format characters and oversized
queries are rejected without trimming or truncation. **First** (or Enter in
this query), **Next match** (F3) and **Previous match** (Shift+F3) navigate the
complete nonoverlapping sequence without wrapping. Count and ordinal describe
only these selected displayed metadata details, not the inventory or files.
An exact inert match receives native focus; focus failure explicitly clears
the match/count and offers First to retry. Matches cannot copy, save, select
files, enable use or grant read, execution, permission, freshness or integrity
authority.

**Clear Find**, Escape in these Find controls, or editing this query clears
its match/navigation state without changing the inventory search or draft.
Selection, inventory page/filter/kind/source/control changes, review retirement,
privacy/ownership loss and editor closure retire the private Find state; reselect
a current row explicitly. Find is local and ephemeral: query, match and count
are not logged, persisted, copied, sent to a model or spoken. Ctrl+F is not
assigned by metadata Find; its keyboard handling is confined to these controls.

### Search and filter the observed inventory

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

The selected name query mode and observed use state are the browsing predicates.
Digest, native-revision and observation-time query fields are retired; their admitted values remain displayed and internally validated.
**Clear search** or Escape restores FileNameAscending, Literal, empty query and All use state without reading ([native controls](../src/Kora/OwnedInstructionEditorWindow.cs), [inventory policy](../src/Kora.Application/Definitions/OwnedDefinitionInventory.cs)).

**All**, **UseEnabled**, **ReviewRequired** and **UnavailableOrUnknown** filter only observed catalogue metadata. Held/changed revisions share ReviewRequired; the inventory cannot infer why an approval is absent. Unmapped or unrevisioned files are unknown, never implicitly enabled.
Rows disclose kind, filename ID, available display name, fixed source, artifact ID, digest, native revision and observed UTC. They contain no body/excerpt, description, attribution or full path.
The complete visible label/details payload is at most 64 KiB UTF-8. Every enumerated filename and mapped metadata row, including native revision syntax, is validated before any predicate or offset can exclude it.
**Next** exposes any omitted complete rows in deterministic pages bound to the original inventory and complete order/mode/query/use state; editing any predicate or order restarts the visible page. A changed order rejects the previous continuation ([paging and ordering tests](../tests/Kora.Application.UnitTests/Definitions/OwnedDefinitionInventoryTests.Ordering.cs)).
Zero matches means zero matches, not unavailable or a fallback. No row is silently truncated ([paging](../src/Kora.Application/Definitions/OwnedDefinitionInventory.cs)).

**Observed catalogue use state; inspect revalidates disk and exact revision.** This is not disk-current freshness, compatibility or integrity proof. Select a row and explicitly **Inspect**: admission rechecks the original snapshot/kind/file ID/digest/native revision, not a newly looked-up same-name replacement.
Changed, replaced, disappeared or expired choices refuse with Refresh/reselect recovery. Unknown rows cannot prove an exact match; explicitly enter their current filename ID for the existing admitted compatibility inspection. Exact `instructions inspect <id>` / `prompts inspect <id>` deliberately inspects the current ID, rather than claiming a historical row match.

Search/filter/page changes never open the first result or save/discard/replace the open original or dirty draft. Editing any predicate immediately retires selection/details, the prior page/continuation, validation and pending review/use controls, even when the selected row would still match. Reselect and explicitly Inspect to restore exact use-review controls; late inspection/refresh/review callbacks cannot restore retired choices. The draft stays open. **Reload** still deliberately reloads the open original after its dirty-draft review.
Changing mode or order follows the same retirement path without List/Refresh/Inspect, save/activation/delete/copy or draft loss. Order edits also retain the exact frozen baseline, retire open Compare and never confer navigation/use authority or persist a preference ([native ordering tests](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.InventoryOrdering.cs)).
An open Compare retires, but a separately requested **Compare** can still show the admitted frozen historical baseline and exact unsaved raw draft; it does not establish current source state ([native lifecycle tests](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.Inventory.cs)).
Catalogue/use/source/call changes invalidate choices. Failed refresh clears availability and labels historical observations unavailable; it does not offer a last-good inventory as current. Privacy/ownership/closure clears query, rows, selection and private editor text, and late callbacks cannot repopulate it.
Typed/current-name activated `list` focuses this same native filter route; no typed search grammar or voice-only approval is advertised. Filtering creates no index, persistence, model/network call, watcher restart, source write or activation. Predicate input remains private ephemeral metadata/correlation only: no logging, audit payload, activity renewal, speech, clipboard or export; it never grants use authority, admits a revision, overrides content identity or proves current disk integrity.

## Find in the displayed owned buffer

The separate **Displayed owned buffer Find** toolbar searches only the complete source text
currently displayed in this editor, including unsaved changes. It does not search the
metadata inventory, disk, other files, profile sources or bundled definitions.
Explicitly **Inspect** or **Create** an owned Instruction or Prompt first; Find never opens
or chooses a source for you.

- **Ctrl+F** focuses the native query without copying the selection into it.
- **Find first**, or **Enter** in that query, selects the first exact occurrence in the source editor.
- **Find next** / **F3** and **Find previous** / **Shift+F3** follow the same nonoverlapping sequence.
  The outcome gives the complete count, current ordinal and exact UTF-16 range. At either
  edge nothing is selected and **no wrap** occurs; the last cursor remains for reverse navigation.
- **Clear Find** clears the private query, count, cursor and Find-owned selection.
  **Escape** in the Find controls also returns focus to the source, without closing the editor.
  A selection you made independently is not cleared or acquired by Find.

Matching is **case-sensitive ordinal literal**, not regex, wildcard or whole-word matching.
Spaces and punctuation are exact; Unicode is not normalized. Queries must be nonempty,
well-formed, one-line Unicode without control/format characters, at most **256 UTF-16 units /
512 UTF-8 bytes**. Invalid input is reported without trimming, repair or truncation.
`aa` in `aaaaa` counts two complete nonoverlapping matches, not four overlapping alternatives.

Query or source edits retire the old match, count and navigation; use **Find first** again.
Loading, chosen kind/ID/file changes, closing/hiding, control retirement or loss of private
host ownership also retire pending Find callbacks. A changed choice never retargets Find
to an undisplayed file. Dirty draft text stays intact; deliberate discard/save/reload
reviews retain their existing semantics. If native focus/selection is unavailable, no match
or count is claimed; use Tab to reach the source and retry Find first.

Find never changes source bytes, inventory selection, read-only status, saved preferences,
validation, baseline, applicability or reviewed use. It does not save, reload, refresh,
rebase, enable, execute, dictate or submit a request. Source editing, undo and accessibility
remain native. The Find query has no clipboard/context transfer or new copy/export action;
queries and body text are not logged, transcribed or spoken. Shared-profile and bundled
read-only sources gain no edit or use authority.

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

## Confirm one reviewed instruction save by voice

For an existing canonical native Kora-owned Instruction, **Save** still opens the complete original/replacement source review. **Confirm save** remains the default.
After reading every changed field and body line, choose **Reviewed complete source: exact save question** for a separate host-owned question ([review](../src/Kora/NativeInstructionMutationReview.cs)).

Choose **Answer by voice**, or **Hear question then answer**, explicitly. Merely showing either review never starts recording. Say the complete displayed phrase, such as **Save instruction example** or **Cancel saving instruction example**.
Generic yes, approve, option numbers, another name and extra actions do not confirm ([exact matching](../src/Kora.Core/Interaction/QuestionVoiceAnswer.cs)).

The optional readback describes Save, owned kind/name, same roaming source, byte count/BOM and changed fields. It explicitly covers the entire display-reviewed replacement; it is not a digest or a reading of the source.
If that summary is insufficient, cancel and use the complete native source review; this increment does not read a 64 KiB source aloud ([question](../src/Kora/QuestionWindowController.InstructionSave.cs)).

The two-minute review binds the original request/channel, exact baseline, prepared bytes and destination. Changed draft, caret, control input, ownership, private session, call or voice readiness invalidates it.
Protected calls, unavailable speech, detected source risk or noncanonical/ambiguous names require native recovery; no answer is followed to another question ([owner](../src/Kora/NativeQuestionHost.InstructionSave.cs)).

Accepted Save uses the same writer, complete-byte readback and disabled-revision reconciliation. Cancel before publication leaves the source unchanged. An uncertain publication is held for inspection/reconciliation, not rolled back or retried.
Create, copy, delete, conflict overwrite, prompts, profile originals, grants and enablement retain their existing native reviews ([writer](../src/Kora.Application/Definitions/OwnedInstructionEditorActions.cs)).

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
For an already selected prompt, choose **Speak input _declared-name_** beside exactly one required field in the focused private form. Supply one utterance to replace that field, or **Cancel spoken input**. The [prompt input controller](../src/Kora.Application/Definitions/PromptInputVoiceController.cs) uses current consented activated recognition, not a new speech backend or the conversation-composer dictation profile.
Starting spoken input retires the previous expanded preview and retention review. Capture must be released before literal text reaches the field; edits, focus/source/session/channel/privacy changes, cancellation or the 75-second lifetime discard late text. No name aliases, fuzzy name matching, command parsing, question answers or approval replies are accepted by this [exact target](../src/Kora/PromptParameterController.cs).
Use native fields if activated capture is unsupported or unavailable; speaking does not enable listening, change consent or select a device. After voice or native edits, explicitly preview the **complete** expanded request, select the exact session, review and separately confirm with the existing native controls. Spoken values never auto-submit or authorize a model call ([native form](../src/Kora/PromptParameterWindow.cs)).
Failed voice-turn setup retires its exact target without accepting text. If microphone cleanup is unconfirmed, further voice input stays blocked; restart Kora before trying voice again. Cancellation still joins outstanding publication and native release; no successful cleanup or automatic retry is claimed.
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
No rename, restore, script, agent-execution or generic file-editor controls are offered. Profile-original writes use only the separately selected [registered-original operation](#edit-one-registered-profile-instruction-original).
The separately consented [native v1 profile-instruction copy](#create-a-disabled-kora-copy-of-a-personal-instruction) creates only a distinct disabled owned file.

## Validate the open draft without saving

**Validate**, `instructions validate` or `prompts validate` (also with the current assistant-name prefix) observes only the currently open same-kind owned draft. Explicitly Inspect a supported canonical original or Create a new draft first. Validation never opens a named file, switches kind or follows text in the ID box to another path.

Validation uses the exact ordinary Save preparation: original BOM, complete source/newlines/attribution, strict Unicode/UTF-8, supported scalar metadata, literal prompt rules and current observed command/spoken-name collisions. Unchanged compatible legacy source stays legacy; changed/new legacy proposals include the native v1 marker. The 65536-byte and 16-field/4096-byte header limits therefore apply to the prepared proposal, including marker/BOM overhead, not merely the raw text box.

The bounded text-only summary reports structural compatibility, fixed owned ID/kind/source, intended model-context role, unapproved review state and, when supported, exact prepared byte count/digest/BOM/format. Errors contain a local reason, never body excerpts. No Markdown/HTML rendering, expression execution, substitutions, reference/dependency reads, model or network is involved.

This is a historical structural observation, not semantic harmlessness, safe execution, permission, current disk integrity or activation. Input/baseline/kind/catalogue/call/private-ownership changes clear or retire it; late callbacks cannot restore a closed/private result. Save independently prepares the current proposal and requires its own exact confirmation; the summary is not an approval token.

Validation makes no owned-store List/Read/Write, source reload, revision-preference write/hold, source mutation or use-state change. Ordinary nonmutating host control intent/terminal receipts and correlated diagnostics remain. Original bytes, draft/dirty state, inventory/search/selection and existing enablement or uncertain-publication holds are retained. Source-unavailable observations disclose that Reload/reconciliation is still required before Save or use. Selected profile originals have their own [bounded comparison](#edit-one-registered-profile-instruction-original).

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

[Focused native tests](../tests/Kora.Windows.IntegrationTests/ArtifactDiscovery/OwnedDefinitionEditorWindowTests.Comparison.cs) cover this bounded owned control; model generation, algorithmic diff and full matrix/installed/hardware/accessibility qualification remain unavailable. Selected profile originals use the separate [source-preserving operation](#edit-one-registered-profile-instruction-original).

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
Opening-time withdrawal clears private content immediately and completes closure after Avalonia finishes showing the window, so reentrant opening callbacks cannot leave the retired review visible.

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
Already captured use requires the exact revision and capture epoch in current catalogue authority, not a retained display flag. Repeated Disable preserves only the original capture epoch; failed hold/reload recovery retires it. Queued use still needs separate successful request admission ([authority check](../src/Kora.Application/Definitions/InstructionRevisionCatalogue.cs)).

The held revision is durable in the existing per-kind v1 revision preferences. Watcher/manual/no-change reload, restart, pre-use checking or restored old bytes cannot re-enable it. Repeating exact Disable truthfully reports already held; only a fresh exact **Review use** can enable the current revision for a later request.
Same filenames in another kind, unrelated sources/copies and immutable bundled definitions are unaffected. Copies of disabled originals still enter independently disabled.
Mandatory audit, preference publication/readback or terminal-bookkeeping failure reports held/unavailable recovery, never success or rollback. A revocation may already be durable: explicitly Reload/read back before a fresh review; no automatic retry or previous-enable restoration.
Legacy original editing and broader applicability remain deferred; this owned-use control does not create grants, change source consent or qualify the full Definitions matrix. Compatible selected [profile-original editing](#edit-one-registered-profile-instruction-original) remains a separate operation.

After review, `/instruction <declared-command>` or activated explicit **use** resolves the same catalogue revision.
For a prompt, use `/prompt <declared-command>` or the same activated explicit
**use** route. A managed literal prompt is sent as explicitly selected user-request
content, never system instructions. Creation/review/save do not start a request;
actual use retains ordinary provider readiness, context/mode and action-approval checks.
Names cannot shadow other catalogue entries or reserved editor controls. Broader automatic instruction scopes,
broader profile formats and model dictation remain future increments. Native v2 required literal input preparation is delivered above; generation remains unavailable. Skills and agents use their separate editors ([kind/source support](#open-and-select)).
