# Folder-grounded local answers

## Ask across one explicitly selected folder snapshot

Use **Preview folder** and the native folder picker. Review the complete immediate-file inventory, then confirm **read every exact reviewed immediate file locally**. Nothing is read before this confirmation. The existing limits are 32 immediate files, 1 MiB combined original bytes and 256 KiB per file, strict UTF-8 text or Markdown.

Subdirectories, unsupported, protected, generated, source-control, hidden/system, reparse/link or unstable items reject the complete selection; they are not silently skipped.

The preview is an immutable, volatile snapshot. It is not a named knowledge source, a persistent index or a current filesystem observation. Closing, clearing, privacy or ownership changes retire it. This operation does not register sources or retain folder content.

1. Enter one question in **Question across the exact immutable folder snapshot**. The shared lexical query limits apply: 256 UTF-16 characters, 512 UTF-8 bytes and 32 unique terms of at most 64 UTF-16 characters.
1. Select **Review folder excerpts for this question**. This searches only the already admitted snapshot; it does not invoke a model or reread files.
1. Review every selected excerpt and exact filename, immutable revision, original-byte digest and end-exclusive line/column location. At most eight excerpts and 16 KiB of excerpt UTF-8 bytes are selected. Total matching chunks and whether bounded retrieval omitted other matches are explicitly displayed; selected excerpts are not silently truncated.
1. Review the separate local-only source-use and fresh **Local context** session disclosure. Select **Confirm: answer locally using only these reviewed folder excerpts** only when the exact question and evidence are correct. This consumes that review once. Editing the question or cancellation invalidates it; another attempt requires another review.
1. For a successful answer, select a citation and **Show exact folder answer citation**. Kora selects that same immutable file and shows/focuses its exact original span. It never rescans a directory, opens the old path, refreshes content or accepts a model-supplied location.

## Availability and honest outcomes

The folder operation uses the existing local context admission and local model runtime. Each evidence-bearing file requires independent sensitive-content/source-use policy admission. **The composed source-use policy currently remains unavailable**, so ordinary production confirmation reports that unavailable boundary without sending source content or inventing an answer. A missing or unqualified local runtime also remains unavailable. Deterministic platform/provider fakes prove the implemented flow, not hardware, provider quality or runtime enablement.

No lexical evidence means no model invocation and no answer. A model may explicitly report that the reviewed evidence does not support an answer. Supported claims require host-issued evidence IDs that resolve to those exact reviewed citations. Citation identity is verified, not semantic truth: check each source span yourself. Invalid, foreign, uncited, executable or hosted responses do not become claims.

The complete serialized context has a separate 32,768-byte limit, including JSON escaping and host provenance. Exceeding it refuses the whole request; evidence is never dropped to make it fit. Documents and their instructions remain untrusted data, not execution or approval authority. No tools, hosted fallback, network source search, speech, clipboard, automatic capture/refresh or durable source/answer storage is added.

## Cancellation and privacy

**Cancel folder answer and clear excerpts**, question edits, source retirement and window closure cancel and suppress late output. The operation retains ownership until the actual provider work and asynchronous lease disposal finish, rather than treating a cancelled wait as released resources. A busy operation cannot be replaced by a second reader or provider operation.

Source bodies, questions, excerpts and answers remain in the native volatile view, outside conversation history, transcript, diagnostics and audit payloads. Only the existing host-owned session/task receipts and content-free requested/terminal audit outcomes are retained.

See the [ingestion contract](../Design/File_And_Folder_Ingestion.md#asking-questions) and [privacy guide](privacy-safety-and-logs.md).
