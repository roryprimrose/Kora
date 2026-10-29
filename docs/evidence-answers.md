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
No remote fallback, export, broader search, tools, actions or automatic retries
are available.

Filters, paging, question edits, cancellation, privacy/ownership/control changes
and closing the window retire the review and suppress late output. Cancellation
does not claim resource release until actual work joins. Start a fresh explicit
review for another question. Diagnostic expiry is not renewed; conversation,
diagnostic and audit retention remain independent.

Only complete, unexpired ordinary SQLite Log pages qualify. Empty pages report
NoEvidence. Partial, omitted, expired, file, audit and mixed-source pages are
unavailable. The entire page must fit the model's 32 KiB serialized input limit;
nothing is silently selected, ranked or truncated.
