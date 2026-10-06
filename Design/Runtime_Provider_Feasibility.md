# Runtime and Provider Feasibility: R02 Outcomes and Next Gates

Status: evidence-backed technical direction; production runtime/provider
selection and D-001/D-004 closure remain open.
Reviewed 2026-10-06 UTC against the retained Node proof, separate
[actual .NET RT1 fixture](../experiments/r02-dotnet-control-proof/README.md)
and bounded [RT2 lifecycle observation](../experiments/r02-runtime-lifecycle-proof/EVIDENCE.md).

Related: [Architecture](Architecture.md#copilot-integration-proof),
[Decision Register](Decision_Register.md#r02-runtimeprovider-feasibility-outcomes),
[Roadmap](Implementation_Roadmap.md#r02-runtimeprovider-follow-up-gates),
[Security](Security_Data_Flows.md#runtime-egress-enforcement),
[Management Envelope](Work_Management.md#management-operating-envelope-and-degraded-mode).

## Historical Node Baseline and Interpretation

The [runnable experiment](../experiments/r02-runtime-proof/README.md) exercised
the actual Node Copilot SDK 1.0.16, bundled runtime 1.0.90/protocol 3 and
synthetic HTTP/SSE loopback providers. Sixteen focused tests passed; the
[capability matrix](../experiments/r02-runtime-proof/evidence/results.json)
records 13 PASS, 1 FAIL and 3 BLOCKED outcomes. Passing tests include verifying
an expected unsupported approach; they do not mean Gate 0 passed.

The hook-only FAIL disposes of that integration option; it is not a
requirement to make a rejected architecture pass. The path forward is the
final-request-gated profile, for which the remaining required evidence must
pass. Keep the historical failure intact and record acceptance for the
selected profile separately, rather than hiding it or treating the original
experiment's aggregate nonzero exit as a permanent veto on all Copilot use.

| Observation | Technical outcome / required change |
|---|---|
| Initial context and successful tool results were filtered before HTTP; denied-tool effects and denied markers forwarded were zero. | Retain host proposal/tool gates and initial-context selection. Treat these as measured paths, not complete SDK mediation. |
| Success post-tool hooks do not receive failed results; failure hooks cannot replace the failure payload. | Reject hook-only integration. Sanitize every host result status and require a final gate over the complete serialized model request before any send. |
| Supported experimental request handler blocked the unsafe failed-result continuation. | Continue with this control-point architecture, conditional on equivalent pinned .NET evidence. Do not patch private SDK internals. |
| Three SDK retry attempts occurred despite the error-abort hook; all were blocked by the final gate. | Enforce no automatic management retry at the dispatch/request boundary, not through an error-hook setting alone. |
| Empty mode disabled advertised built-ins/discovery; volatile session FS avoided observed context persistence. Runtime initialization still wrote PowerShell startup metadata. | Use explicit minimal runtime configuration and host-owned storage. Prove all startup/session/auth/error/shutdown network and storage paths; an empty tool list is not process containment or no-write evidence. |
| Two blocked execution conversations and a manager on another endpoint remained independent; manager completed in 177 ms. | Preserve separate lane identities/context/tool sets. Verify the same topology in .NET and on the intended account/provider; this is not a production slot, quota or resource-lease proof. |
| Abort was acknowledged, but an already-admitted non-cooperative test tool completed later. | Cancellation closes new dispatch/egress and suppresses late presentation; admitted effects require observed receipts or Outcome Unknown. Never infer rollback, physical stop or replay safety from SDK acknowledgement. |
| Complete model JSON accepted at 32768 bytes and rejected at 32769; typed UTF-8 JSON accepted at 4096 and overflow rejected. Held inference returned deadline at 15011 ms. | Carry the host envelope to .NET, including protocol overhead and asynchronous deadline handling. Do not substitute token counts, character limits, provider timeout or truncated JSON. |
| No hosted account was approved; all-destination observation and .NET parity were not performed in the Node experiment. | Historical Node rows stay unchanged. The separate .NET RT1 outcome below does not prove RT2 or PV1; no approved provider, price ceiling, terms entitlement or production implementation follows from Node evidence. |

The numerical timings are single recorded observations, not reference-machine
SLO acceptance. No microphone, real model quality, worker containment,
protected deployment, encrypted Kora storage or local inference decision was
validated by this runtime branch.

## Implementation Direction

### RT1 .NET outcome: selected source-built profile passes

The [RT1 disposition](../experiments/r02-dotnet-control-proof/evidence/disposition.json)
records **PASS for the reviewed exact-tag source-built minimal HTTP/stdio
profile**, not released-NuGet byte parity or Gate 0. All 45 actual .NET/native
tests pass: 44 selected-profile PASS rows and one expected rejected hook-only
FAIL. Historical Node evidence remains unchanged and separate.

Public .NET SDK release `v1.0.16`, source
`f8ae645902b74b62cd47aac1fd9b29adaec3aff2`, exposes experimental
`CopilotRequestHandler.SendRequestAsync`/`OpenWebSocketAsync` and public
per-session `SessionFsProvider`. Runtime 1.0.90/protocol 3 uses the same verified
native launcher/payload as the Node witness. SDK 10.0.401/.NET 10.0.12,
source/package/assembly/native/fixture hashes, UTC receipts, separate locks
and dependency/native license review are recorded in the fixture.
Direct NuGet acquisition failed TLS; the user explicitly approved an
unmodified exact-tag source build, distinctly versioned
`1.0.16-rt1.source.f8ae645.1`. A separate clean-source build reproduces identical
package/assembly bytes after ZIP-timestamp-only normalization. Released NuGet
byte conformance remains **Blocked**, not silently substituted.

Measured boundaries:

- Complete final JSON covers initial system/prompt, prior user/assistant
  history and every tested result status: success, failure, denied, rejected,
  unavailable, cancelled, unknown, timeout, blocked, conflict and pending.
  Rich host statuses remain structured receipt facts, not SDK enum claims.
  Denied tool effects and denied markers forwarded are **zero**.
- `rejected` ends the current turn but persists into later history; subsequent
  sends are gated too. Synchronous/faulted tool exceptions both reach final
  denial; their original exception marker is absent from these runtime-formatted
  requests, so omission is not generalized into a privacy guarantee.
- Streaming and scripted 401/429/500/drop/malformed SSE paths are explicit;
  synthetic credentials are in headers, never model bodies. Internal retries
  are observed and blocked after one admitted attempt. Redirects are not followed.
- Two held execution conversations do not block the isolated manager on another
  loopback endpoint. Hostile model IDs/approval fields do not select host identity
  or authorize a tool. This is not a resource-lease/scheduler/account proof.
- SDK abort acknowledgement, request-token cancellation and loopback connection
  termination are separate observations. An admitted non-cooperative owned-file
  effect completes after cancellation and is **Unknown** at cancellation.
  `SendAndWaitAsync` timeout alone does not terminate held inference.
- Empty mode has zero advertised built-ins, failed excluded-tool proposals,
  disabled discovery/collection features and actual volatile session-I/O writes.
  Store write denial produces a real session error. Scoped disk scans show
  zero context-marker persistence; all owned trials clean up. This is not
  all-network/file/diagnostic observation.

Only ephemeral synthetic HTTP/SSE providers and harmless owned scratch were
used. Ambient auth/environment/default routing were excluded; no account,
installed application, provider inference, paid operation, global policy or
other worktree was used. WebSockets, attachments, MCP, built-in agents/tools,
skills/plugins/extensions/canvas, remote export, embedding retrieval, memory,
spill and native SQL/session stores remain unavailable in the tested profile.
The full optional runtime bundle is not admitted for redistribution.

RT1 permits **RT2 and MG1 proof work for these exact bytes**, not production
integration. RT2 must observe complete lifecycle destinations/diagnostics,
optional helper loading, collection and persistence. MG1 still needs its
complete byte/deadline/admission/unknown-termination envelope. PV1 remains
blocked pending intended-account/terms/cost approval. R04 owns durable identity,
audit and trace contracts: SDK IDs/trace headers are correlation only, and an
SDK completion cannot commit host authority or certify durable storage.
No architecture decision is changed, no sidecar is selected and D-001 remains
open. A change to released artifact/transport/runtime/profile requires retesting.

### RT2 outcome: bounded observations pass; all-path gate Blocked

The independent [RT2 fixture/disposition](../experiments/r02-runtime-lifecycle-proof/evidence/disposition.json)
records **Blocked** for the exact approved RT1 source-built minimal profile.
Final primary and independent clean-source package/assembly bytes match RT1's
pins; earlier mismatching builds were rejected and are retained as
reproduction-maintenance evidence. Original RT1/Node sources and historical
evidence are unchanged. The full staged RT1 regressions pass 45/45; RT2 passes
20/20 tests (13 actual runtime trials, six deterministic observer tests and a
live positive control), plus two receipt-locale contracts. None is RT2 closure.

Live scratch notifications, PID/creation-time process/module snapshots,
IPv4/IPv6 TCP/UDP tables, managed network diagnostic event counters and
before-cleanup file inspection cover startup, verified runtime initialization,
session/history/I/O, synthetic authentication/errors/retries, failed/denied
results, cancellation/late effects, disposal and observed quiescence.
The run observes native PowerShell and console-host descendants and transient
PowerShell policy-test files even with zero advertised built-ins. All 52 sampled
owned process identities terminate; provider model bodies have zero denied
markers/credential sentinels. Positive controls see real loopback sockets,
live marker files before deletion and managed diagnostics.

This is **detection plus the original public model/session-I/O mediation**,
not native containment. Directory notifications have no writer PID/content;
socket/module/process snapshots miss short-lived paths. Native stderr/ETW,
DNS/UDP destinations/non-IP transports, transient/deleted/outside-scratch
writes, registry/ADS/crash-dump paths and missed/detached descendants are not
fully observed or controlled. Therefore all-destination and zero unauthorized
native marker persistence/egress claims remain **Blocked**, not inferred from
zero model-handler markers or post-cleanup absence.

The user retained this fail-closed RT2 contract after the separate W2
best-effort transitive dependency discussion. Privileged tracing was deferred
to a dedicated host, not approved/run in the shared parallel environment.
Obtain scoped approval for bounded PID/creation-time-attributed, metadata-only
tracing with loss controls; tracing alone still cannot establish content
prevention. Prove native mediation/isolation for the unchanged approved profile
or return an explicit D-001 decision. R08 remains unavailable; MG1 continues
independently and PV1 still needs technical gates plus intended-account/terms/
cost approval. R04 remains owner of durable correlation/audit admission.

### Host boundaries, not hook-only authority

The candidate architecture is:

```text
Host-selected immutable context / admitted result with provenance
  -> SDK conversation isolated to its task or management request
  -> Runtime constructs serialized model request
  -> Host final request gate checks lane, destination, approved sources,
     byte budget, request generation, cancellation and retry eligibility
  -> Approved provider transport
```

Pre-tool authorization remains a separate boundary before effects. The final
egress gate cannot compensate for an unmediated tool, and tool approval cannot
approve transmission. Result sanitization covers success, failure, denial,
timeout, cancellation, unknown effects and thrown exceptions before they
enter the SDK. A final gate still checks framing, history and runtime-added
content; unknown sources/destinations fail closed rather than relying on
synthetic-marker scanning as a production data classifier.

Use an explicit minimal/empty runtime mode, admitted tool allowlists,
deny-by-default permissions, disabled discovery/memory/export/spill and
host-owned session I/O. Prefer the tested child-process stdio transport for
the next proof: the Node in-process transport does not honor the same
per-client environment controls. Process separation is not a sandbox.
Kora's own permitted durable history remains under its private-profile store; SDK
transcript files are not a replacement or an additional unreviewed copy.

Keep WebSockets, attachments, runtime-built-in tools, MCP, runtime skills,
extension loading, embedding retrieval and remote export unavailable until
their specific control paths pass. Host-bundled skill data and host-gateway
tools remain possible future inputs; the SDK's automatic versions are not
implicitly admitted by those product contracts.

### .NET-first continuation and explicit stop condition

The first isolated .NET conformance proof above is complete for its approved
source-built profile, not R08 production composition. Pin any next actual
.NET artifact and compatible runtime;
do not assume Node's version number or callback inventory applies to .NET.
Reproduce the recorded controls through supported public APIs, including
the final HTTP gate and host-owned session I/O. Record exact versions,
native hashes, installation/deployment implications and API gaps.

If .NET lacks any required control or silently ignores a setting, fail that
candidate. Do not weaken the contract, embed the Node proof as an unreviewed
production bridge, or switch providers. Bring an explicit D-001 decision
back with evidence: a different compatible SDK/runtime, an intentionally
designed contained sidecar, or a separately approved inference adapter using
the host-owned loop. These are decision options, not authorized fallbacks.

### Management remains optional and host bounded

Implement deterministic ledger/status/choice/cancellation services without
model-assisted management. In the isolated .NET envelope proof:

- Bound both selected request/ledger context and the full serialized outbound
  model body to 32768 UTF-8 bytes, including system/framing/history overhead.
  Use a fresh isolated management conversation per request; reuse would need
  separate cumulative-history/budget evidence.
- Bound the complete typed proposal JSON to 4096 UTF-8 bytes. Streaming is
  provisional; overflow/schema failure produces degradation, not a truncated
  proposal or committed ledger mutation.
- Start the 15-second host inference deadline at dispatch, independent of
  provider completion or SDK send/abort acknowledgement. Keep setup/sign-in
  outside inference and separately cancellable; do not hide their latency.
- Admit one management request at a time and at most 30 remote attempts per
  rolling hour per profile, including failures. Forward at most one provider
  inference attempt for that request; block SDK retries and continuation/tool
  calls. Explicit user retry creates a new bounded request.
- Quarantine a request/conversation with unconfirmed termination. Do not
  release its inference slot on abort acknowledgement alone or borrow an
  execution lane. Deterministic local controls remain available.

The experiment's status-only schema is a test fixture, not the production
management protocol. R13 must implement the admitted typed routing/ledger
proposals and revision/target checks without task tools or approval authority.

## Next Work and Go/No-Go Outcomes

The [roadmap follow-up gates](Implementation_Roadmap.md#r02-runtimeprovider-follow-up-gates)
assign owners, prerequisites and exit evidence. Execute in this order:

1. **R02-RT1:** selected exact-tag source-built profile passes; preserve its
   conformance/negative tests and artifact pins. Released NuGet parity remains
   blocked. Passing allows RT2/MG1, not production exposure. Failure triggers
   an explicit D-001 architecture decision; stop that integration path.
2. **R02-RT2:** bounded user-mode trials are complete; all-path observation/
   prevention is **Blocked**. Follow the dedicated-host approval and native
   mediation handoff above. Any unexplained or uncontrollable content path
   keeps the runtime unavailable; do not weaken this gate to W2 best-effort
   script dependency discovery.
3. **R02-MG1:** reproduce the envelope/concurrency/cancellation tests in .NET.
   This can run alongside RT2 after RT1. Passing allows a management-provider
   trial proposal; it does not prove a hosted budget.
4. **R02-PV1:** prepare account/terms/model/region/cost evidence, then obtain
   explicit account and usage-budget approval before live inference or
   provisioning. Test execution eligibility for R08; additionally test the
   two-execution-plus-management profile and budget for R13.

RT1/RT2 and relevant PV1 results feed R08; model-assisted R13 additionally
needs MG1 and the management-specific PV1 results. Local R06/R07 and
deterministic R12/R13 work remain governed by their own prerequisites, not
unperformed hosted trials. Passing this branch does not close other R02
feasibility areas.

The intended user authenticates through the selected service's supported
secure flow. Credentials remain host-only opaque references; never borrow
the developer's ambient CLI account. Record plan/organization policy,
permitted SDK/assistant use, actual concurrency, quotas/rate limits and
hard spending controls. The experiment's [provider notes](../experiments/r02-runtime-proof/PROVIDER.md)
are public-source assumptions, not account authorization. Cancellation
does not guarantee zero provider billing; byte bounds do not bound billable
reasoning. If no defensible usage envelope is available, keep management
inference disabled with deterministic choices, not a guessed price ceiling.

## Acceptance and Maintenance

Partial experimental evidence and this technical direction may merge without
closing D-001/D-004 or enabling the candidate. The
[deferred-validation register](Deferred_Validation.md#runtimeprovider-follow-up)
owns the later-session handoff, including required .NET fixtures,
instrumentation and separate account/usage approval.

Re-run the relevant conformance evidence for any change to SDK/runtime bytes,
transport, authentication method, endpoint/model, result format, storage,
automatic collection or admitted feature. Maintain version-bound capability
availability and explanation; production failures disable the affected path.
R08/R13 integration still needs common host authority, resource coordination
and installed-app acceptance, not just isolated provider tests.

Keep the experiment in source control as a reproducible regression and
failure witness. This page owns the technical interpretation and continuation;
the roadmap owns execution order, the decision register owns selection/
closure, and the experiment owns raw measurements and commands.
