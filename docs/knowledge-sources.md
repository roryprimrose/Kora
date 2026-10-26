# Named device-local knowledge folders

Open **Settings > Knowledge > Manage knowledge sources**. This native management surface works without voice, a model or a network connection.

## Add a folder

1. Enter a short source name and choose **Choose folder and review metadata**. Use the trusted native picker; conversation text, document instructions and model output cannot select a root.
2. Review the canonical root, complete immediate-file membership, original bytes and scope. This review has not read content. Confirm **read of this exact reviewed membership** separately.
3. Inspect each captured file in the selector. Confirm **save of this exact complete device-local snapshot** separately, within two minutes. This retains private Kora copies until explicit removal, independently of session history.

The same existing folder policy applies: a local fixed drive, UTF-8 `.txt`, `.md` or `.markdown`, no recursion, at most 32 immediate files, 256 KiB per file and 1 MiB combined. Any unsupported item, subdirectory, protected/generated/source-control path, hard link, reparse point, hidden/system item, inaccessible or changing member rejects the entire source. There are no silent exclusions or partial-success snapshots.

Names are unique ignoring case; up to 16 sources are supported. Registration is device-local to the current Windows profile. It never grants model consent, hosted disclosure, automatic session context or permission to treat documents as instructions.

## Explicit inspection and retrieval

Choose **Reload source inventory**, select exactly one source and choose **Inspect selected exact stored snapshot**. Opening management or selecting a row does not inspect content.

Choose an item to inspect its complete untrusted plain text. Enter a lexical query and choose **Search this exact inspected source locally**. Results use the existing bounded `lexical-lines-v1` policy and exact item/line references. No match, invalid query and truncated result sets are explicit; Kora never substitutes a different source or searches all registered folders.

Snapshots are historical admitted revisions, not current filesystem observations. Inspection revalidates original access, physical root and exact metadata membership without rereading original content. Changed or unavailable originals require an explicit new refresh review. Private presentation withdrawal, protected call state or closing the window clears displayed text and retires pending work.

This increment has no model-answer route. Local model/source-use qualification remains unavailable; adding or refreshing cannot bypass it. The separate volatile folder-preview/answer workflows remain independent.

## Refresh, disable and remove

- **Refresh selected source** first revokes old use, then reviews only the same canonical physical root. Read and save each require a fresh exact confirmation. Failed, cancelled or interrupted refresh leaves the old source unavailable, not silently current. A replaced physical root requires removing and explicitly adding a new source.
- **Disable selected source** immediately blocks new inspection/retrieval while retaining Kora copies. A fresh reviewed refresh is required to enable use again.
- **Review removal of Kora copies** inventories the exact source registry row, encrypted snapshot and private rollback journal. Confirm removal separately within two minutes. Removal revokes use before cleanup. Kora never deletes or changes original files.

If storage, journal cleanup or deletion is uncertain, no completion is claimed. Reload the inventory and inspect the state; a **Removing** source remains revoked and can be reviewed for removal again. Unknown files, links, changed ownership, corrupt or missing storage hold admission/cleanup; do not delete arbitrary files or reset the partition to regain access.

Copies live in the `KnowledgeSourcesV1` private durable partition under the host-provided application-data root, not in preference JSON. Content is encrypted for the current Windows profile; publication atomically binds metadata and the complete snapshot. Cleanup uses secure logical database deletion and clears its owned rollback journal before claiming completion. This is not forensic erasure of hardware, external backups or files copied by other software.

No embeddings, OCR, whole-profile scans, always-on watchers, recursion, hosted egress, automatic refresh or original-file writes are included.
