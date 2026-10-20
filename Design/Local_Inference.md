# Local Inference Disposition and Future Qualification

Status: the evaluated Ollama 0.35.1/qwen3:1.7b profile is rejected. The
Ollama 0.40.2 comparison is also rejected. Automated quality failures are
terminal for those candidates; no human scoring, reference-floor run or
offline lab can qualify them.

Production remains fail closed: local inference is unavailable unless the
pinned runtime/model identity is present and healthy, and there is no automatic
remote fallback. Provider-neutral contracts, UI, orchestration and replacement
candidate work continue without reopening these results.

Related: [Decision Register](Decision_Register.md#d-003-local-inference-baseline),
[Architecture](Architecture.md#local-inference-qualification),
[Setup](Environment_Setup.md#local-inference-provisioning-budget),
[Acceptance](Acceptance_Criteria.md#local-inference-evidence),
[Roadmap](Implementation_Roadmap.md#r02-local-inference-continuation), and
[Deferred Validation](Deferred_Validation.md).

## Unattended Provisioning Admission - 2026-10-09

The original absent-runtime and installer-admission observations are preserved
in the [validation archive](Archive/Deferred_Validation_2026-10-11.md) and
[experiment results](../experiments/r02-local-inference-proof/results/).
They are historical context, not current work or ongoing consent.

Later isolated evaluation used the official portable Ollama 0.35.1 bytes on a
dedicated loopback endpoint without changing the shared runtime. That candidate
failed automated answer-quality gates. Ollama 0.40.2 failed comparison gates.
Production pins were not changed.

## Three-Tier Admission Policy

1. **Development:** deterministic production boundaries and unrelated features
   continue. No local-inference qualification evidence is required.
2. **Capability:** a materially changed runtime/model/profile must pass the
   applicable identity, quality, bounds, cancellation and offline gates before
   it can be enabled or advertised.
3. **Release candidate:** an RC including local inference must identify the
   qualified profile and evidence. An RC explicitly excluding local inference
   is not blocked by rejected or unperformed candidate trials.

Environment evidence never grants installation, process, network, clipboard,
audio or tool authority. Each consequential trial retains its own approval and
ownership boundary.

## Outcomes and Their Consequences

| Outcome | Engineering consequence |
|---|---|
| Pinned 0.35.1/qwen3:1.7b failed automated quality gates | Keep unavailable; do not spend operator time on semantic scoring or hardware qualification |
| 0.40.2 comparison failed automated quality gates | Do not change the production pin to this version |
| Exact request/response, pinned identity, cancellation and timeout behavior have maintained production tests | Continue provider-neutral and replacement-candidate development under normal CI |
| Advertised model context, physical server cessation and independently blocked egress were not qualified | Reassess only for a future candidate that first passes automated quality gates |
| No candidate is accepted | D-003/local-inference D-007 and A2 remain unavailable-profile decisions, not general feature-development blockers |

## Future Candidate Gate

A proposed replacement must:

1. define exact runtime/model/options and licence/installation ownership;
2. pass automated expected-answer, authority, unavailable-path and recovery
   gates before human review;
3. establish the effective prompt/output envelope and explicit oversize
   behavior;
4. pass repeated cancellation/timeout/late-output recovery;
5. meet pre-agreed reference-hardware latency/resource budgets; and
6. prove offline success under independently attributable egress denial if an
   offline profile is claimed.

Use the retained
[experiment procedure](../experiments/r02-local-inference-proof/README.md#outstanding-testing-checklist)
only after a materially changed candidate is selected for enablement. Do not
repeat it for the rejected candidates.

## Platform Boundary Gate

Shared domain, policy, task, skill and configuration code remains portable.
Windows runtime/process/device mechanisms stay in Windows integration and
composition. Missing, malformed or changed runtime/model identity fails closed.

## Native Tool-Message Transport Boundary

The Windows [native transport](../src/Kora.Windows/Dependencies/WindowsOllamaToolTransport.cs)
implements the portable [IModelToolTransport](../src/Kora.Core/Dependencies/IModelToolTransport.cs)
seam for native Ollama `api/chat` messages. Desktop [composition](../src/Kora/Program.cs)
replaces the legacy `api/generate` registration with
[explicit unavailability](../src/Kora.Application/Dependencies/UnavailableLocalModelReasoner.cs):
bootstrap readiness cannot bypass exact host/session/task admission. Transport
implementation is not candidate acceptance, qualification evidence, permission
to start a server, or permission to install/download/select another model.

Desktop composition constructs the bounded Application adapter over this
transport, but its candidate registration retains `None` evidence and an
expired qualification. The endpoint resolver also returns no binding by
default. Host admission therefore remains explicitly unavailable. No existing
rejected runtime/model is qualified, no readiness observation sets
`ToolLoopQualified`, and no legacy/global reasoner fallback is used.
`IModelTurnAdmission` and `IModelTurnExecution` resolve the same host singleton.
That host supplies the live continuation guard around each exchange/tool and
retains the session consumer's current-source fence; native composition does
not call the engine's deliberately refused unguarded entry.

### Shared execution seam for original-request consumers

Voice requests, clipboard explanation, file answers and web summaries consume
the same [admission port](../src/Kora.Application/Dependencies/IModelTurnAdmission.cs)
and [execution port](../src/Kora.Application/Dependencies/IModelTurnExecution.cs)
on one host. Their owning workflows supply the original host request/activity,
exact admitted session/task, immutable [context envelope](../src/Kora.Core/Dependencies/ModelContextEnvelope.cs)
and current-source fences. These ports do not infer a session or authorize a source.
No consumer may route around qualification, relabel its origin as native input,
or use transport correlation as host authority.

For a source-qualified **local, tool-free** turn, the owning request/task bridge
constructs the [immutable envelope](../src/Kora.Core/Dependencies/ModelContextEnvelope.cs)
with the explicit final argument `toolMode: ModelToolMode.None` and calls
`AdmitAsync(ModelTurnChoice.Local, envelope, null, token)`. It then passes the
issued turn to `IModelTurnExecution.RunAsync(turn, admitted, token, validateSource)`
under the same live original host activity. The bridge owns durable task intent,
terminal task/audit receipts and source eligibility; these APIs do not save a
conversation body or create source/session/task admission.

The execution port attaches the consumer's supplemental source-current/revocation
predicate and optional asynchronous exact-source validator to the single-use
turn; another call cannot replace that binding. The
[host](../src/Kora.Application/Dependencies/ModelTurnHost.cs) rechecks around
asynchronous source/profile/workspace reads, immediately before adapter dispatch,
on each continuation/tool boundary and after output. The predicate is not
request/session/task/tool authority and cannot retarget the captured envelope.
[Deterministic source-fence tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.SourceAdmission.cs)
cover revocation during awaited reads/provider output, continuation/handler
boundaries, cancelled late output and missing durable task authority, without
persisting a source in conversations.

After awaiting its run attempt, the owner uses
`IModelTurnExecution.WaitForQuiescenceAsync(cancellationToken)` before releasing
owned volatile source/runtime resources. The [host wait](../src/Kora.Application/Dependencies/ModelTurnHost.Execution.cs)
observes the same physical completion gate held by cancelled/noncooperative
adapter work, including late faults; cancelling only this wait cannot cancel the
provider, release the gate or claim quiescence. Use a cleanup token independent
of the cancelled request when cleanup must await termination. The observation
grants no admission, prevents no future turn and is not a successful task receipt.
Post-dispatch cancellation/timeout remains Unknown task certainty under the
existing task invariant; do not wait by replaying or disposing the shared host.

[ModelToolMode.None](../src/Kora.Core/Dependencies/ModelToolMode.cs) fixes the
complete canonical catalogue to empty and rejects all tool history. Missing,
unknown or mismatched wire mode/catalogue fails closed; text instructions alone
never disable tools. The [native request](../src/Kora.Windows/Dependencies/OllamaToolWire.cs)
omits the `tools` field, and [transport](../src/Kora.Windows/Dependencies/WindowsOllamaToolTransport.cs)
rejects unsolicited tool calls. The [bounded adapter](../src/Kora.Application/Dependencies/BoundedModelTurnAdapter.cs)
refuses tool-call messages before callback dispatch, including fake transports.
The [host](../src/Kora.Application/Dependencies/ModelTurnHost.cs) independently
refuses every tool callback and suppresses an adapter's claimed answer after
a forbidden attempt (`Denied / ToolUseNotPermitted`). Existing default
`ReadOnly` turns retain their six-tool behaviour. Qualification, exact local
selection, byte/deadline limits and cancellation/quiescence fences still apply.

The [retained native conversation workflow](../src/Kora.Application/Hosting/SessionConversationService.Execution.cs)
owns its existing original USER/dispatch/terminal transaction and exact readback;
it is not a generic voice or selected-source context broker. Source-policy owners
retain source consent, classification, provenance and expiry. The
[host continuation validator](../src/Kora.Application/Dependencies/ModelTurnHost.cs)
rechecks live session-profile authority and the captured tuple's catalogue and
installation around exchanges and tool callbacks. Later preference edits still
affect later turns, not the captured tuple. Deterministic
[integrated execution tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.IntegratedExecution.cs)
cover retained original input through the real bounded loop and registry, plus
authority loss before and after a tool; this is not provider qualification.

A future enablement owner must supply an exact, unexpired host-created binding
for the admitted provenance and complete account/catalogue/model/options
profile. The native profile implemented here fixes `stream=false` and
`think=false`; those settings and the server's context behavior must match its
qualified profile rather than silently overriding a different selection.
The binding's live fence covers original session, generation, control, privacy,
qualification and resource ownership. The host revokes its binding at turn
closure; revocation/caller cancellation also releases idle continuation data.
Terminal completion and disposal release the retained volatile context.
Provider/user/tool data cannot construct
that fence or supply an endpoint, headers, credentials or an account.

Only `http://127.0.0.1:11434` is addressed. Before metadata and chat dispatch,
and after receiving an answer, Windows checks the expected PID, creation time,
current user/session, executable path/hash and exact IPv4 loopback listener.
The binding must come from the host's admitted process-ownership receipt;
same-user identity or endpoint discovery alone is not ownership.
Version and selected model digest are checked through bounded metadata reads;
missing, inaccessible, changed or ambiguous identity is a terminal refusal,
not discovery or a substitute model. The HTTP client disables proxies,
redirects, cookies, ambient credentials and decompression, and is owned by the
transport. No process is started, killed or installed by this path.

The entire native request, including framing, tool declarations and serial
history, obeys the Core input limit both individually and cumulatively.
Protocol revision 2 supplies the host's remaining `ModelToolRequest.Budget`.
Before any HTTP send, the greater of the native body and complete canonical
request must fit that remaining input budget. Every successful reply echoes
the host provenance and carries actual `ModelToolUsage`: input is the greater
of those complete request sizes; output is the greater of the entire raw
assistant response and its canonical message size. Missing/stub counters are
not a successful integration.

Declared-length and unknown-length responses are bounded by the remaining
output budget before parsing, not just the global maximum; the canonical
assistant charge must also fit before any call is returned. Assistant charges
plus canonical host tool-result messages obey the cumulative Core output
limit. The Application engine already charges each canonical tool result
once; the transport does not report it again as response usage. Native
tool-result framing remains part of the complete outbound request charge.
Metadata observations are separately bounded by the same remaining raw
response limit and are not model/tool messages. Oversize, partial, malformed UTF-8/Unicode,
unknown/duplicate fields, wrong roles/model/IDs/order and abnormal termination
never produce a partial answer. The fully validated native message is mapped
through the authoritative Core parser before any tool call can be returned.
Only the six read-only catalogue actions are declared; the Application
callback, not this transport or provider output, owns their dispatch.

Native calls without an ID receive a turn-local round/index correlation ID.
Explicit provider IDs are validated correlation only. Native assistant
messages are replayed exactly in order; host results use the `tool` role,
exact tool name and result bytes as inert content, with `tool_call_id` only
when the native call supplied an ID. Results are never promoted to system
instructions or caller identity. Prior exchanges cannot be replaced or
replayed as new invocations.

Cancellation, revocation, expiry, failed HTTP and deadline completion have
typed terminal outcomes without retries or fallback. Cancellation/disposal
waits for actual owned HTTP/read work to terminate, including a
non-cooperative platform fake, before disposing the client. Closing HTTP is
not proof that a server's model computation physically stopped; actual-runtime
cessation and enablement qualification remain separate and unclaimed.
Maintained tests use fake HTTP, process observations and in-memory listener
tables only; they do not contact a model/server, invoke Windows process
observation, install software or establish runtime qualification.
