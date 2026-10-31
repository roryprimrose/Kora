# Named device-local knowledge folders

Keep a named private copy of a small folder of text documents, then search that
copy locally. Open **Settings > Knowledge > Manage knowledge sources**.
Managing and searching these copies needs no voice, AI model or network.
Adding a folder does not make it automatic AI context.

## Add a folder

1. Enter a short source name. Immediate files remain the default. To include subfolders, explicitly select **Explicitly include bounded subfolders** before choosing **Choose folder and review metadata**. Use the trusted native picker; conversation text, document instructions and model output cannot select a root.
2. Review the folder location, exact traversal scope, complete root-relative list of directories (including empty ones) and files, file sizes and
   scope. File contents have not been read. Confirm **read of this exact reviewed membership** separately.
3. Inspect the captured files in the selector. Separately confirm
   **save of this exact complete device-local snapshot** within two minutes.
   Kora keeps these copies until you explicitly remove them; deleting a
   conversation does not remove them.

Both scopes require a local fixed drive and strict UTF-8 `.txt`, `.md` or `.markdown`: at most 32 files across the whole source, 256 KiB per file and 1 MiB combined. Descendant capture additionally permits at most 32 directories including the root and empty directories, to depth 4 (root is depth 0). Discovery, capture, save and inspection each have a 30-second work bound ([policy](../src/Kora.Core/KnowledgeSources/KnowledgeSourcePolicy.cs)).

Any unsupported item, forbidden subdirectory, protected/generated/source-control path, hard link, reparse point, hidden/system item, inaccessible or changing member rejects the entire source. Immediate scope still rejects every subdirectory. There are no silent exclusions, partial snapshots or truncation ([native admission](../src/Kora.Windows/Context/WindowsLocalFileInspector.KnowledgeSources.cs)).

Names are unique ignoring case; up to 16 sources are supported. Registration is device-local to the current Windows profile. It never grants model consent, hosted disclosure, automatic session context or permission to treat documents as instructions.

## Explicit inspection and retrieval

Choose **Reload source inventory**, select exactly one source and choose **Inspect selected exact stored snapshot**. Opening management or selecting a row does not inspect content.

Choose an item to read its complete plain text. Enter search words and choose
**Search this exact inspected source locally**. This searches words and lines,
not meaning, and reports the root-relative file and line for each excerpt. Choose **Inspect cited saved item** to select the exact retained item's cited span, not newer disk content. Identical filenames in different subfolders remain distinct. All branches share one ranking and excerpt budget: at most 8 citations and 16 KiB combined excerpt bytes ([shared retrieval](../src/Kora.Core/Context/LocalFileLexicalRetrieval.cs)). Invalid queries,
no matches and limited results are labelled. Kora never searches a different
source or every registered folder instead.

Snapshots are historical admitted revisions, not current filesystem observations. Inspection revalidates original access, every retained physical directory/file identity and exact complete tree membership without rereading original content. Changed or unavailable originals require an explicit new refresh review. Private presentation withdrawal, protected call state or closing the window clears displayed text and retires pending work ([source lifecycle](../src/Kora.Application/KnowledgeSources/KnowledgeSourceManager.cs)).

**AI answers are unavailable here.** Saving or refreshing a source cannot
enable a model or approve model use of your files. The separate
[folder-preview answer review](folder-answers.md) still requires a
[qualified local model](../Design/Local_Inference.md#future-candidate-gate), unavailable in this build.
That separate volatile route uses [reviewed private-LocalOnly risk admission](../Design/Security_Data_Flows.md#reviewed-localonly-source-use-risk-admission);
it does not grant model use of named saved sources.

## Refresh, disable and remove

- **Refresh selected source** first joins revocation of old use, then reviews only the same canonical physical root and stored traversal policy. The add checkbox cannot change refresh scope. Read and save each require a fresh exact confirmation. Failed, cancelled or interrupted refresh leaves the old source unavailable, not silently current. A replaced physical root or scope change requires removing and explicitly adding a new source ([refresh owner](../src/Kora.Application/KnowledgeSources/KnowledgeSourceManager.cs)).
- **Disable selected source** immediately blocks new inspection/retrieval while retaining Kora copies. A fresh reviewed refresh is required to enable use again.
- **Review removal of Kora copies** inventories the exact source registry row, encrypted snapshot and private rollback journal. Confirm removal separately within two minutes. Removal revokes use before cleanup. Kora never deletes or changes original files.

If storage, journal cleanup or deletion is uncertain, no completion is claimed. Reload the inventory and inspect the state; a **Removing** source remains revoked and can be reviewed for removal again. Unknown files, links, changed ownership, corrupt or missing storage hold admission/cleanup; do not delete arbitrary files or reset the partition to regain access.

Copies are stored in Kora's private application data, encrypted for the current
Windows profile, not in preference files. Removal cleans up Kora's stored copy
and its own recovery journal. This is not a promise of forensic erasure from
hardware, backups or copies made by other software.

New snapshots use an explicit version-2 format in the existing private partition, retaining all directory identities, traversal consent and exact saved item/revision/digest identities. Existing version-1 immediate snapshots remain readable with their original file identities and limits; old consent never becomes recursive. Unknown or malformed formats fail closed, not as an empty replacement store ([snapshot format](../src/Kora.Windows/KnowledgeSources/WindowsKnowledgeSourceStore.Snapshot.cs)).

No embeddings, OCR, whole-profile scans, always-on watchers, hosted egress, automatic refresh or original-file writes are included.
