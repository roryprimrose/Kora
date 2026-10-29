# Named device-local knowledge folders

Keep a named private copy of a small folder of text documents, then search that
copy locally. Open **Settings > Knowledge > Manage knowledge sources**.
Managing and searching these copies needs no voice, AI model or network.
Adding a folder does not make it automatic AI context.

## Add a folder

1. Enter a short source name and choose **Choose folder and review metadata**. Use the trusted native picker; conversation text, document instructions and model output cannot select a root.
2. Review the folder location, complete list of immediate files, file sizes and
   scope. File contents have not been read. Confirm **read of this exact reviewed membership** separately.
3. Inspect the captured files in the selector. Separately confirm
   **save of this exact complete device-local snapshot** within two minutes.
   Kora keeps these copies until you explicitly remove them; deleting a
   conversation does not remove them.

The same existing folder policy applies: a local fixed drive, UTF-8 `.txt`, `.md` or `.markdown`, no recursion, at most 32 immediate files, 256 KiB per file and 1 MiB combined. Any unsupported item, subdirectory, protected/generated/source-control path, hard link, reparse point, hidden/system item, inaccessible or changing member rejects the entire source. There are no silent exclusions or partial-success snapshots.

Names are unique ignoring case; up to 16 sources are supported. Registration is device-local to the current Windows profile. It never grants model consent, hosted disclosure, automatic session context or permission to treat documents as instructions.

## Explicit inspection and retrieval

Choose **Reload source inventory**, select exactly one source and choose **Inspect selected exact stored snapshot**. Opening management or selecting a row does not inspect content.

Choose an item to read its complete plain text. Enter search words and choose
**Search this exact inspected source locally**. This searches words and lines,
not meaning, and reports the file and line for each excerpt. Invalid queries,
no matches and limited results are labelled. Kora never searches a different
source or every registered folder instead.

Snapshots are historical admitted revisions, not current filesystem observations. Inspection revalidates original access, physical root and exact metadata membership without rereading original content. Changed or unavailable originals require an explicit new refresh review. Private presentation withdrawal, protected call state or closing the window clears displayed text and retires pending work.

**AI answers are unavailable here.** Saving or refreshing a source cannot
enable a model or approve model use of your files. The separate
[folder-preview answer review](folder-answers.md) has its own unavailable
source-use checks and local-model requirements.

## Refresh, disable and remove

- **Refresh selected source** first revokes old use, then reviews only the same canonical physical root. Read and save each require a fresh exact confirmation. Failed, cancelled or interrupted refresh leaves the old source unavailable, not silently current. A replaced physical root requires removing and explicitly adding a new source.
- **Disable selected source** immediately blocks new inspection/retrieval while retaining Kora copies. A fresh reviewed refresh is required to enable use again.
- **Review removal of Kora copies** inventories the exact source registry row, encrypted snapshot and private rollback journal. Confirm removal separately within two minutes. Removal revokes use before cleanup. Kora never deletes or changes original files.

If storage, journal cleanup or deletion is uncertain, no completion is claimed. Reload the inventory and inspect the state; a **Removing** source remains revoked and can be reviewed for removal again. Unknown files, links, changed ownership, corrupt or missing storage hold admission/cleanup; do not delete arbitrary files or reset the partition to regain access.

Copies are stored in Kora's private application data, encrypted for the current
Windows profile, not in preference files. Removal cleans up Kora's stored copy
and its own recovery journal. This is not a promise of forensic erasure from
hardware, backups or copies made by other software.

No embeddings, OCR, whole-profile scans, always-on watchers, recursion, hosted egress, automatic refresh or original-file writes are included.
