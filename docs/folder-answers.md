# Folder-grounded local answers

**Answering is unavailable in this build.** You can preview and search a small
text folder, but the separate permission check for using its contents with AI
is unavailable, and no local model is approved.
For saved, named folder copies instead, see [Knowledge sources](knowledge-sources.md).

## Ask across one explicitly selected folder snapshot

Use **Preview folder** and the native folder picker. Review the complete immediate-file inventory, then confirm **read every exact reviewed immediate file locally**. Nothing is read before this confirmation. The existing limits are 32 immediate files, 1 MiB combined original bytes and 256 KiB per file, strict UTF-8 text or Markdown.

Subdirectories, unsupported, protected, generated, source-control, hidden/system, reparse/link or unstable items reject the complete selection; they are not silently skipped.

The preview is an immutable, volatile snapshot. It is not a named knowledge source, a persistent index or a current filesystem observation. Closing, clearing, privacy or ownership changes retire it. This operation does not register sources or retain folder content.

1. Enter one question in **Question across the exact immutable folder snapshot**. The shared lexical query limits apply: 256 UTF-16 characters, 512 UTF-8 bytes and 32 unique terms of at most 64 UTF-16 characters.
1. Select **Review folder excerpts for this question**. This searches only the already admitted snapshot; it does not invoke a model or reread files.
1. Review every selected excerpt, its filename and exact source location.
   Kora also records which captured version it came from. At most eight excerpts
   and 16 KiB of excerpt text are selected. The total matches and any omitted
   matches are shown; selected excerpts are never silently shortened.
1. Review the separate local-only source-use and fresh **Local context** session disclosure. Select **Confirm: answer locally using only these reviewed folder excerpts** only when the exact question and evidence are correct. This consumes that review once. Editing the question or cancellation invalidates it; another attempt requires another review.
1. For a successful answer, select a citation and **Show exact folder answer citation**. Kora selects that same immutable file and shows/focuses its exact original span. It never rescans a directory, opens the old path, refreshes content or accepts a model-supplied location.

## Availability and honest outcomes

Each file needs a separate check that its contents may be used for the answer.
**The composed source-use policy currently remains unavailable**, so confirmation reports
Unavailable without sending content or inventing an answer. Installing a model,
previewing files or choosing excerpts cannot bypass this check.

No lexical evidence means no model invocation and no answer. A model may explicitly report that the reviewed evidence does not support an answer. Kora checks that a successful answer's citations refer to the reviewed excerpts.
Citation identity is verified, not semantic truth: Kora checks the source
reference, not whether the answer is true. Read the cited passage yourself.
Invalid or missing citations cannot become a supported answer.

The complete serialized context has a separate 32,768-byte limit, including JSON escaping and host provenance. Exceeding it refuses the whole request; evidence is never dropped to make it fit. Documents and their instructions remain untrusted data, not execution or approval authority. No tools, hosted fallback, network source search, speech, clipboard, automatic capture/refresh or durable source/answer storage is added.

## Cancellation and privacy

**Cancel folder answer and clear excerpts**, question edits, source retirement and window closure cancel and suppress late output.
Kora waits for actual provider work and asynchronous lease disposal before
reporting resources released. A busy operation cannot be replaced by another
read or answer attempt.

Source bodies, questions, excerpts and answers remain in the native volatile view, outside conversation history, transcript, diagnostics and audit payloads. Only the existing host-owned session/task receipts and content-free requested/terminal audit outcomes are retained.

See the [ingestion contract](../Design/File_And_Folder_Ingestion.md#asking-questions) and [privacy guide](privacy-safety-and-logs.md).
