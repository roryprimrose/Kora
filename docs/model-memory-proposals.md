# Reviewed session-memory proposals

## Request and inspect

There is **no automatic learning**. Ask for one memory proposal explicitly, for example: "Propose a short-answer response preference for this session." A qualified model may return one inert object; ordinary answers are never converted ([proposal format](../src/Kora.Core/Memory/ModelProposals/ModelMemoryProposal.cs)).

```json
{"version":1,"class":"responsePreference","value":"Prefer short answers."}
```

The only supported classes are `explicitFact`, `responsePreference`, `workflowPreference` and `decision`. Unknown/duplicate fields, scope selectors, batches, malformed text and oversized values are rejected without saving a memory body. Values are bounded to 512 characters and 1024 UTF-8 bytes; both the complete object and the existing serialized candidate must fit 2048 UTF-8 bytes ([parser](../src/Kora.Core/Memory/ModelProposals/ModelMemoryProposal.cs), [memory policy](../src/Kora.Core/Memory/MemoryPolicy.cs)).

In an Active conversation, explicitly select **Review session-memory proposal...** on the actual retained ASSISTANT answer. The private review shows the complete original USER, exact ASSISTANT answer, execution/turn lineage, original channel, value, classification and exact session scope. Verify the value is explicitly yours and contains no inferred traits, credentials, secrets, health details, transient tasks or unsupported model claims. Reject inappropriate content; classification is not proof of truth ([review](../src/Kora.Application/Memory/ModelProposals/ModelMemoryProposalReview.cs)).

## Accept, admit or reject

1. **Accept exact session candidate** only after inspecting the complete source and value. Acceptance remains volatile, pending and unused.
2. Separately select **Admit accepted session memory** to save this exact session-only memory through the existing admission, audit and durable transaction. This does not use memory, grant tools, invoke a model or approve hosted disclosure.
3. **Reject candidate** records rejection without saving a body. **Cancel without saving** withdraws an unadmitted review, including after acceptance. It cannot undo an already committed admission ([shared workflow](../src/Kora.Application/Memory/ModelProposals/MemoryManagementService.ModelProposals.cs)).

Review expires after 60 seconds. Refresh, reselection, source withdrawal, privacy/control changes, session lifecycle changes and closure retire the old review. Each consequential action requires fresh original-user input and rechecks the exact retained source; stale or replayed approval is not reusable. Failures may leave an uncertain committed result: inspect Session memories and audit receipts, never automatically retry or infer rollback ([workflow](../src/Kora.Application/Memory/ModelProposals/MemoryManagementService.ModelProposals.cs), [native lifecycle](../src/Kora/ModelMemoryProposals/SessionsViewModel.ModelMemoryProposals.cs)).

## Boundaries and availability

The candidate remains lower-trust MODEL data. Its durable lineage points to the actual source request, task, original channel and session generation; inspection includes full retained model-turn provenance. Existing session memory policy, retention/deletion and manual memory management remain authoritative. No device-profile, project, source-scope promotion, background learning, automatic recall or hosted egress is added ([memory contract](../Design/Model_Providers_Memory_And_Knowledge.md#user-governed-durable-memory), [memory admission](../src/Kora.Application/Memory/MemoryAdmissionService.cs)).

This review does not enable an unavailable provider. Without a genuinely admitted retained ASSISTANT answer the operation remains unavailable; deterministic platform fakes prove implementation, not provider qualification ([provider qualification](../Design/Runtime_Provider_Feasibility.md)).
