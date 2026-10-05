# Runtime and Provider Feasibility: R02 Outcomes and Next Gates

Status: evidence-backed technical direction; production runtime/provider
selection and D-001/D-004 closure remain open.
Reviewed 2026-10-05 against the R02 proof recorded in `e0af3ea`.

Related: [Architecture](Architecture.md#copilot-integration-proof),
[Decision Register](Decision_Register.md#r02-runtimeprovider-feasibility-outcomes),
[Roadmap](Implementation_Roadmap.md#r02-runtimeprovider-follow-up-gates),
[Security](Security_Data_Flows.md#runtime-egress-enforcement),
[Management Envelope](Work_Management.md#management-operating-envelope-and-degraded-mode).

## Evidence Baseline and Interpretation

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
| No hosted account was approved; all-destination observation and .NET parity were not performed. | Keep those gates BLOCKED. No approved provider, price ceiling, terms entitlement or production implementation follows from this experiment. |

The numerical timings are single recorded observations, not reference-machine
SLO acceptance. No microphone, real model quality, worker containment,
protected deployment, encrypted Kora storage or local inference decision was
validated by this runtime branch.

## Implementation Direction

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
Kora's own permitted durable history remains under its encrypted store; SDK
transcript files are not a replacement or an additional unreviewed copy.

Keep WebSockets, attachments, runtime-built-in tools, MCP, runtime skills,
extension loading, embedding retrieval and remote export unavailable until
their specific control paths pass. Host-bundled skill data and host-gateway
tools remain possible future inputs; the SDK's automatic versions are not
implicitly admitted by those product contracts.

### .NET-first continuation and explicit stop condition

The next implementation task is **an isolated .NET conformance proof**, not
R08 production composition. Pin an actual .NET SDK and compatible runtime;
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

1. **R02-RT1:** pin and test .NET public control-point parity. Passing allows
   continuation of this candidate, not production exposure. Failure triggers
   an explicit D-001 architecture decision; stop that integration path.
2. **R02-RT2:** observe the candidate runtime's full network/storage lifecycle
   and establish a verified minimal processing profile. Any unexplained or
   uncontrollable content path keeps the runtime unavailable.
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
