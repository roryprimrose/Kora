# Ask about one diagnostic page

**AI analysis is unavailable in this build.** The required check for using
diagnostic records as model input is unavailable, and no local model is
approved. You can still [inspect diagnostic records directly](privacy-safety-and-logs.md#read-only-evidence-inspection).

Open **Evidence**, select **Log**, and explicitly **Search / refresh**.
Expand **Ask about this exact evidence**, enter an original question (at most
256 characters / 512 UTF-8 bytes), then choose **Review complete page and question**.
Inspect the complete immutable page, its stable record citations, question and
local-use disclosure. This step does not request a model or create a conversation.

Separately choose **Confirm one tool-free local answer in a new conversation**.
Separate permission to use this diagnostic page and an available local model
are both required. Confirmation currently reports **Unavailable** without
calling a model or inventing an answer.

When independently admitted, one local-only, tool-free request creates a fresh
**Local context** conversation and retains the original question and validated
cited final answer. Facts are labelled as model claims, separately from interpretation.
Kora checks references to the selected records, not whether a claim is true.
Unsupported evidence produces
an explicit unsupported answer; malformed or foreign citations never become a
grounded success. Inspect retained results in **Sessions**.

**Show exact inert record** displays only a record already in the reviewed page.
It performs no source read, command replay or historical-body reconstruction.
No remote fallback, answer export, broader search, tools, actions or automatic retries
are available.

## Separately export one evidence page

In the workbench **Evidence** view, explicitly search a permitted bounded page and choose **Review
page export**. Choose a **new** local fixed-drive `.json` destination outside
Kora/protected storage. The separate native review shows every UTF-8 JSON byte,
record/source/gap provenance, the exact destination and independent retention.
The live continuation cursor is omitted (`Cursor` is null); no other page or
missing content is reconstructed. The complete original page remains bounded
to 50 records / 64 KiB, including its original cursor.

Choose **Confirm this one new-file export** once, or cancel. Kora revalidates
the original source snapshot, filters/privacy and held destination identities;
it creates without replacement, flushes and verifies exact readback. Results
distinguish **Saved**, **NotWritten**, **Cancelled** and **Uncertain**. An
uncertain file may exist: inspect the destination before any fresh review;
there is no automatic retry or rollback claim. No file is opened or copied
to the clipboard. This operation does not call a model or grant authority.

Filters, paging, privacy closure, leaving the Evidence view and window closure cancel and clear the
review. Voluntary shutdown joins physical writes and destination disposal,
including work that ignores cancellation. Exported copies leave Kora retention:
deleting evidence or conversations does not delete files, backups or synced
copies. This native file-write exception is separate from diagnostic model-input
admission and from exact retained ASSISTANT answer export.

Filters, paging, question edits, cancellation, privacy/ownership/control changes
and closing the window retire the review and suppress late output. Cancellation
does not claim resource release until actual work joins. Start a fresh explicit
review for another question. Diagnostic expiry is not renewed; conversation,
diagnostic and audit retention remain independent.

If cancellation reports a notification failure, the review and displayed citations
are still cleared. The original request remains owned until its work has actually
joined; do not retry it while cancellation is pending
([retirement behavior](../src/Kora/EvidenceViewModel.EvidenceAnswers.cs)).

Only complete, unexpired ordinary SQLite Log pages qualify. Empty pages report
NoEvidence. Partial, omitted, expired, file, audit and mixed-source pages are
unavailable. The entire page must fit the model's 32 KiB serialized input limit;
nothing is silently selected, ranked or truncated.
