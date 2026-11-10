# Named device-local knowledge folders

Keep a named private copy of a small folder of text documents, then search that
copy locally. Open **Settings > Knowledge > Manage knowledge sources**.
Managing and searching these copies needs no voice, AI model or network.
Adding a folder does not make it automatic AI context.

Management uses a supporting tab in the expanded workbench, not another application window ([source view](../src/Kora/KnowledgeSources/KnowledgeSourcesView.cs)).
Leaving, hiding or minimizing retires displayed sources and temporary reviews. Saved copies are not removed; any pending work stays owned until it joins, without late private output ([hosted operations and teardown](../tests/Kora.Windows.IntegrationTests/KnowledgeSources/KnowledgeSourceAnswerWindowTests.cs)).

## Add a folder

1. Enter a short source name. Immediate files remain the default. To include subfolders, explicitly select **Explicitly include bounded subfolders** before choosing **Choose folder and review metadata**. Use the trusted native picker; conversation text, document instructions and model output cannot select a root.
2. Review the folder location, exact traversal scope, complete root-relative list of directories (including empty ones) and files, file sizes and
   scope. File contents have not been read. Confirm **read of this exact reviewed membership** separately.
3. Inspect the captured files in the selector. Separately confirm
   **save of this exact complete device-local snapshot** within two minutes.
   Kora keeps these copies until you explicitly remove them; deleting a
   conversation does not remove them.

Saving also builds and atomically retains the exact source's lexical index. It does not scan other folders or grant model use ([publication owner](../src/Kora.Windows/KnowledgeSources/WindowsKnowledgeSourceStore.cs)).

Both scopes require a local fixed drive and strict UTF-8 `.txt`, `.md` or `.markdown`: at most 32 files across the whole source, 256 KiB per file and 1 MiB combined. Descendant capture additionally permits at most 32 directories including the root and empty directories, to depth 4 (root is depth 0). Discovery, capture, save and inspection each have a 30-second work bound ([policy](../src/Kora.Core/KnowledgeSources/KnowledgeSourcePolicy.cs)).

Any unsupported item, forbidden subdirectory, protected/generated/source-control path, hard link, reparse point, hidden/system item, inaccessible or changing member rejects the entire source. Immediate scope still rejects every subdirectory. There are no silent exclusions, partial snapshots or truncation ([native admission](../src/Kora.Windows/Context/WindowsLocalFileInspector.KnowledgeSources.cs)).

Names are unique ignoring case; up to 16 sources are supported. Registration is device-local to the current Windows profile. It never grants model consent, hosted disclosure, automatic session context or permission to treat documents as instructions.

## Lower the next capture limits for one source

Reload the inventory and select exactly one registered source. **Get selected source capture limits** shows its saved desired limits (or hard defaults) separately from the limits captured in its current immutable snapshot.
Enter three positive integers: maximum file count, maximum original bytes per file and maximum original bytes across the complete source. Each must stay within the unchanged hard maxima of **32 / 262144 / 1048576** respectively. The byte limits are independent; a smaller total may constrain a file more than its per-file limit.

Choose **Review selected source lower capture limits**, inspect the exact source/root and complete old/proposed tuple, then separately **Confirm this exact source capture limits preference** within two minutes, or cancel. Editing fields, selecting another source, source/preference drift or loss of private admission retires that review. **Review reset selected source capture limits** followed by confirmation removes only that source's saved override.

The exact original typed commands, optionally prefixed by the current assistant name during deliberate activated voice, use the same private review:

```text
source capture limits get "Team notes"
source capture limits lower "Team notes" 8 65536 262144
source capture limits confirm
source capture limits reset "Team notes"
source capture limits cancel
```

Unknown or ambiguous names fail without selecting another source. These are original-user commands, not model proposals or generic `set` settings. Source paths still require the existing native picker.

Saving is audited, atomic and read back; malformed or unconfirmed saved state explicitly blocks a new refresh, never defaults or clamps it. Preferences contain only the stable source identity and numeric tuple, separately from session/history retention. Saving/resetting does **not** refresh, revoke a pinned answer, change an in-flight capture, widen traversal or grant model use. Only the next separately reviewed explicit refresh freezes the then-confirmed tuple into complete metadata and its new snapshot. Any excess rejects the entire source without truncation or partial publication. Existing snapshots and their answer-use checks keep their own captured tuple.

## Explicit inspection and retrieval

Choose **Reload source inventory**, select exactly one source and choose **Inspect selected exact stored snapshot**. Opening management or selecting a row does not inspect content.

Choose an item to read its complete plain text. Enter search words and choose
**Search this exact inspected source locally**. This searches words and lines,
not meaning, and reports the root-relative file and line for each excerpt. Choose **Inspect cited saved item** to select the exact retained item's cited span, not newer disk content. Identical filenames in different subfolders remain distinct. All branches share one ranking and excerpt budget: at most 8 citations and 16 KiB combined excerpt bytes ([shared retrieval](../src/Kora.Core/Context/LocalFileLexicalRetrieval.cs)). Invalid queries,
no matches and limited results are labelled. Kora never searches a different
source or every registered folder instead.

Snapshots are historical admitted revisions, not current filesystem observations. Inspection revalidates original access, every retained physical directory/file identity and exact complete tree membership without rereading original content. Changed or unavailable originals require an explicit new refresh review. Private presentation withdrawal, protected call state or closing the window clears displayed text and retires pending work ([source lifecycle](../src/Kora.Application/KnowledgeSources/KnowledgeSourceManager.cs)).

## One reviewed local answer

Explicitly select one or two enabled source rows (Ctrl+click adds a second), enter one new question in **New named-source answer question**, and choose **Review selected named source excerpts for this question**. No source is selected by default. Review the unchanged question, every selected source's exact retained revision, source-qualified root-relative locations and complete retrieved excerpts. Two selected sources produce one combined answer, not two independent model calls. This does not invoke a model ([review owner](../src/Kora/KnowledgeSources/KnowledgeSourcesView.Answers.cs)).

For example, select **Team notes** and **Release notes** and ask how their alpha guidance differs. Both retained revisions must still pass original access, membership and exact retained-revision checks; one unavailable source rejects the whole use, without falling back to the other. The review labels sources with no shown supporting excerpt. Ranking and the global evidence budget are shared across both sources, not multiplied. Inspection, refresh, disable and removal still require exactly one selected row.

Separately choose **Confirm one private local-only no-tools named-source answer** within the displayed deadline. Consent is one-shot, private LocalOnly and limited to the shown question and excerpts. Saving, enabling or refreshing never grants it. Every evidence-bearing complete file passes the [shared risk policy](../src/Kora.Application/Context/LocalSourceUsePolicy.cs); this is not a secret-free guarantee.

This separate review also covers retaining the unchanged original question and validated final answer in a new **Local context** conversation. Session-history retention applies independently of named-source copies. Nothing is submitted or retained by merely opening the review ([native consent](../src/Kora/KnowledgeSources/KnowledgeSourcesView.Answers.cs)).

To answer from fewer of the retrieved sections, deliberately select whole excerpts in
**Choose whole retrieved named-source excerpts; none selected by default**, then choose
**Review only chosen named-source excerpts**. Review the unchanged question and complete
chosen excerpts before the same separate confirmation. Unchosen excerpts do not enter
model context; each chosen excerpt's complete file still passes risk screening.
The review distinguishes user-omitted retrieved excerpts from matches omitted by the
bounded search. Changing the selection clears the previous confirmation and requires
another chosen-set review. It does not renew the original deadline or one-shot consent.
Highlighting preview text is not context selection. The original complete-retrieval
review remains available when this optional selection is unchanged.

The review expires at its 30-second deadline, including time spent revalidating the source.
If it expires during confirmation, no answer is claimed and consent is not extended.
Wait for any pending work to finish, then explicitly review the question and excerpts again
before confirming a fresh one-shot answer ([answer workflow](../src/Kora.Application/KnowledgeSources/KnowledgeSourceAnswerWorkflow.cs)).

The limits remain 8 citations, 16 KiB combined excerpts and 32 KiB complete serialized model input, including source labels and overhead. Excess input is rejected without dropping reviewed evidence. For two-source answers the reviewed source name, exact revision and relative location accompany each excerpt as inert data and pass shared risk screening before model admission. No match invokes no model. Missing [qualified local runtime](../Design/Local_Inference.md#future-candidate-gate) returns unavailable; there is no hosted fallback, tool use or unselected-source search ([answer workflow](../src/Kora.Application/KnowledgeSources/KnowledgeSourceAnswerWorkflow.cs)).

Operational failure diagnostics record only the failure type, not the question, source content or exception message. A failed review or source-use check does not dispatch a model or return answer claims.

The deliberate typed command `answer using source "Team notes": What does alpha mean?` and current-name activated voice request open the same review. Replace the example name with an exact enabled source name; an unmatched or ambiguous name requires explicit inventory selection, never an all-source scan. Existing channel, privacy, ownership and call restrictions remain applicable ([entry route](../src/Kora.Application/ViewModels/MainViewModel.KnowledgeSources.cs)).

Supported answers show host-bound citations. **Inspect exact answer citation** selects only the retained exact item's span, without disk recapture. The original question and validated final answer are read back from the canonical private conversation before success is reported; they are not speech, effect authority or renewed consent ([answer owner](../src/Kora.Application/KnowledgeSources/KnowledgeSourceAnswerWorkflow.cs)).

If admission, execution or retained readback fails, no supported retained answer is claimed. The original question or terminal outcome may already be committed: inspect the **Local context** conversation instead of automatically retrying ([canonical retention](../src/Kora.Application/Hosting/SessionConversationService.cs)).

Refresh, disable, removal, session retirement and private window closure revoke source use and join cancellation-ignoring model resources before releasing native ownership. Retained conversation history follows its separate retention policy; source removal does not delete that history or renew access to removed source copies ([native lifecycle](../src/Kora/KnowledgeSources/KnowledgeSourcesView.Answers.cs)).

## Refresh, disable and remove

- **Refresh selected source** first joins revocation of old use, then reviews only the same canonical physical root and stored traversal policy. The add checkbox cannot change refresh scope. Read and save each require a fresh exact confirmation. Failed, cancelled or interrupted refresh leaves the old source unavailable, not silently current. A replaced physical root or scope change requires removing and explicitly adding a new source ([refresh owner](../src/Kora.Application/KnowledgeSources/KnowledgeSourceManager.cs)).
- **Disable selected source** immediately blocks new inspection/retrieval while retaining Kora copies. A fresh reviewed refresh is required to enable use again.
- **Review removal of Kora copies** inventories the exact source registry row, encrypted snapshot/index, numeric source preferences and private rollback journal. Confirm removal separately within two minutes. Removal revokes use before cleanup. Kora never deletes or changes original files.

Explicit refresh reuses unchanged original-byte digest/index-version entries, rebuilds changed or new items and retires deleted items from the new index. Cancellation never makes an older index current again. Disable retains both snapshot and index; removal retires both together ([index lifecycle](../src/Kora.Windows/KnowledgeSources/WindowsKnowledgeSourceStore.cs)).

If storage, journal or numeric-preference cleanup or deletion is uncertain, no completion is claimed. Reload the inventory and inspect the state; a **Removing** source remains revoked and can be reviewed for removal again. Its metadata is not deleted until numeric cleanup succeeds, so a reopened removal can retry. Other sources' preferences are not removed. Unknown files, links, changed ownership, corrupt or missing storage hold admission/cleanup; do not delete arbitrary files or reset the partition to regain access.

Copies are stored in Kora's private application data, encrypted for the current
Windows profile, not in preference files. Removal cleans up Kora's stored copy
and its own recovery journal. This is not a promise of forensic erasure from
hardware, backups or copies made by other software.

New registry metadata uses version 3 and new snapshots use version 4 in the existing private partition, retaining the captured numeric tuple, directory identities, traversal consent, exact item/revision/digest identities and the unchanged versioned lexical index together.
Cold inspection validates the index against the exact saved text before use. Storage/index corruption is an explicit failure, never a plausible empty result.
Valid version-1 immediate, version-2 directory-aware and version-3 indexed snapshots remain readable at their historical hard capture maxima; the first two retain their original lexical behavior until an explicit refresh publishes their index. Old consent never becomes recursive, and missing or invalid new-format tuples fail explicitly ([snapshot format](../src/Kora.Windows/KnowledgeSources/WindowsKnowledgeSourceStore.Snapshot.cs)).

No embeddings, OCR, whole-profile scans, always-on watchers, hosted egress, automatic refresh or original-file writes are included.
