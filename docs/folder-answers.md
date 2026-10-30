# Folder-grounded local answers

**An unqualified local model remains unavailable.** Preview, search and chosen-evidence
review do not approve a model or bypass the [shared LocalOnly source-use policy](../Design/Security_Data_Flows.md#reviewed-localonly-source-use-risk-admission).
For saved, named folder copies instead, see [Knowledge sources](knowledge-sources.md).

## Ask across one explicitly selected folder snapshot

Use **Preview folder** and the native folder picker. Review the complete immediate-file inventory, then confirm **read every exact reviewed immediate file locally**. Nothing is read before this confirmation. The existing limits are 32 immediate files, 1 MiB combined original bytes and 256 KiB per file, strict UTF-8 text or Markdown.

Subdirectories, unsupported, protected, generated, source-control, hidden/system, reparse/link or unstable items reject the complete selection; they are not silently skipped.

The preview is an immutable, volatile snapshot. It is not a named knowledge source, a persistent index or a current filesystem observation. Closing, clearing, privacy or ownership changes retire it. This operation does not register sources or retain folder content.

1. Enter one question in **Question across the exact immutable folder snapshot**. The shared lexical query limits apply: 256 UTF-16 characters, 512 UTF-8 bytes and 32 unique terms of at most 64 UTF-16 characters.
1. Select **Review folder excerpts for this question**. This searches only the already admitted snapshot; it does not invoke a model or reread files.
1. Choose a nonempty set of whole existing citations from the retrieved list. None is selected for model context by default. Ordinary text highlighting and Find matches do not authorize context. You cannot supply an arbitrary path, edit an excerpt or replace its original span.
1. Select **Review only chosen folder excerpts**. Review the complete unchanged question and every chosen excerpt, filename, immutable revision, original-byte digest and end-exclusive UTF-16/line/column location. At most eight excerpts and 16 KiB of excerpt UTF-8 bytes may be chosen; their combined byte count and the separate complete-envelope ceiling are shown.
1. Check **User-selected scope only**: the review separately counts retrieved excerpts you omitted and discloses total matching chunks and bounded-retrieval omissions. The answer uses only your chosen excerpts, never silently restores omitted evidence and cannot claim review of the full folder. Chosen excerpts are never silently shortened.
1. Review the separate local-only source-use and fresh **Local context** session disclosure. Select **Confirm: answer locally using only these reviewed folder excerpts** only when the question and chosen evidence are correct. This consumes that review once. Selection changes revoke the previous review; question edits or cancellation retire it. Another attempt requires another explicit search/review.
1. For a successful answer, select a citation and **Show exact folder answer citation**. Kora selects that same immutable file and shows/focuses its exact original span. It never rescans a directory, opens the old path, refreshes content or accepts a model-supplied location.

## Availability and honest outcomes

Each evidence-bearing file independently passes the [shared LocalOnly source-use policy](../src/Kora.Application/Context/LocalSourceUsePolicy.cs).
It screens the whole immutable file, unchanged question and chosen excerpts:
omitting a credential-bearing section cannot hide its risk. Choosing evidence is not
risk approval and does not replace separate one-shot confirmation.

Unknown screening or a missing/unqualified local runtime remains unavailable,
without sending content or inventing an answer. Deterministic platform/provider
fakes prove the implemented flow, not runtime enablement or answer quality.

No lexical evidence means no model invocation and no answer. A model may explicitly report that the reviewed evidence does not support an answer. Kora checks that a successful answer's citations refer to the reviewed excerpts.
Citation identity is verified, not semantic truth: Kora checks the source
reference, not whether the answer is true. Read the cited passage yourself.
Invalid or missing citations cannot become a supported answer.

The complete serialized context has a separate 32,768-byte limit, including JSON escaping and host provenance. Exceeding it refuses the whole request; evidence is never dropped to make it fit. Documents and their instructions remain untrusted data, not execution or approval authority. No tools, hosted fallback, network source search, speech, clipboard, automatic capture/refresh or durable source/answer storage is added.

## Cancellation and privacy

**Cancel folder answer and clear excerpts**, selection/question changes, source retirement and window closure cancel and suppress late output and revoke exact-span navigation.
Selection does not renew the original 30-second retrieval-context deadline or retention.
Kora waits for actual provider work and asynchronous lease disposal before
reporting resources released. A busy operation cannot be replaced by another
read or answer attempt.

Source bodies, questions, excerpts and answers remain in the native volatile view, outside conversation history, transcript, diagnostics and audit payloads. Only the existing host-owned session/task receipts and content-free requested/terminal audit outcomes are retained.

See the [ingestion contract](../Design/File_And_Folder_Ingestion.md#asking-questions) and [privacy guide](privacy-safety-and-logs.md).
