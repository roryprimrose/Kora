# Ask about one diagnostic page

Open **Evidence**, select **Log**, and explicitly **Search / refresh**.
Expand **Ask about this exact evidence**, enter an original question (at most
256 characters / 512 UTF-8 bytes), then choose **Review complete page and question**.
Inspect the complete immutable page, its stable record citations, question and
local-use disclosure. This step does not request a model or create a conversation.

Separately choose **Confirm one tool-free local answer in a new conversation**.
Qualified source-use and local-runtime admission are both required. The default
source-use/sensitivity service is unavailable: confirmation truthfully reports
Unavailable, with no model request or fabricated answer.

When independently admitted, one local-only, tool-free request creates a fresh
**Local context** conversation and retains the original question and validated
cited final answer. Facts are labeled model claims; inference is separate.
Citation identity is checked, not semantic truth. Unsupported evidence produces
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
