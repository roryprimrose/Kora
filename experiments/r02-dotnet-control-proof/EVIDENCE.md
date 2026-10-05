# RT1 .NET observations and disposition

The final run is recorded in [results](evidence/results.json) and
[disposition](evidence/disposition.json), with exact tested hashes and UTC
timestamps. **45/45 tests pass; 44 selected-profile capability rows PASS,
one rejected hook-only row FAIL.** RT1 PASS is scoped to the explicitly
approved exact-tag source-built minimal profile. No released-NuGet byte parity,
RT2, MG1, PV1 or product acceptance is claimed.

| Actual .NET/runtime path | Result and evidence |
|---|---|
| Initial prompt and system context | Submitted/transformed public hooks run; full final gate sees framing/context. Unfiltered denied initial marker blocks before provider arrival. |
| History | Approved prior user/assistant history appears in final JSON; synthetic denied runtime-added assistant history is blocked on the next send. |
| Pre-effect denial | One actual pre-tool hook; denied handler calls and owned file effects **zero**. |
| Result statuses | Actual owned scratch read returns each of success, failure, denied, rejected, unavailable, cancelled, unknown, timeout, blocked, conflict and pending. Both unsafe-final-denial and truthful sanitized-forwarding paths are tested. Rich host statuses are structured payload facts, not fabricated SDK enum support. |
| Rejected result | Runtime ends current turn without immediate inference. A later user send retains result history; safe receipt forwards, denied marker blocks. Do not drop this path from final mediation. |
| Hook-only failure | Success hook count 0, failure hook count 1; failure hook supplies guidance, cannot replace failed payload. Final gate blocks continuation. Historical Node witness unchanged. |
| Successful post-tool hook | Replaces successful result before final request; not universal mediation. |
| Thrown exception | Both synchronous throw and faulted tool task actually execute once. Their next full model request reaches and obeys the host final denial. Original exception marker count at final boundary is **zero** for these inputs: runtime formatting differs from an explicit failed result. Do not claim raw exception strings were exposed or rely on that omission as a privacy guarantee. |
| Streaming/auth | At least two actual answer delta events plus idle/completed response. Synthetic auth detected in header only, zero body occurrences. No successful real auth tested. |
| Provider 401/429/500 | Explicit SDK errors. One forwarded request/session; internal second attempts blocked by boundary, not an error-hook promise. |
| Dropped/malformed SSE | Explicit errors and no successful answer; retry forwarding bounded. |
| Redirect/destination mismatch | Non-redirecting handler client prevents redirect reaching second listener; wrong session-bound destination forwards zero requests. |
| Built-ins/collection | Advertised built-ins 0; forced excluded PowerShell proposal produces failed receipt, no execution. Discovery canary absent. MCP/skills/agents/extensions/memory/export/embedding retrieval/spill are unavailable in tested profile. |
| Session I/O | Real runtime writes routed to per-session volatile provider. Synthetic write failure produces actual session error (not disk fallback success). Scoped scan finds zero approved/denied context-marker persistence. Only fixture-owned input/canary files and the admitted effect file are counted. |
| Independent conversations | Two execution HTTP requests held while management completes on separate endpoint/session; no execution markers/tools in management. Single observed timing is not SLO/quota/scheduler evidence. |
| Hostile identifiers | Model-supplied target/session/approval values cannot invoke denied tool or select manager identity/context. SDK-minted session binds only to fixture host map, never durable Kora authority. |
| Held request cancellation | Cancellation requested, SDK abort acknowledged, public request token cancellation observed, loopback connection termination observed separately. New request is blocked; no provider-compute/rollback/billing claim. |
| Non-cooperative admitted tool | Outcome **Unknown** at cancellation; abort acknowledged; owned file effect completes afterwards once. Late/new outbound model request is blocked. Production presenter suppression remains separate host evidence. |
| SDK wait timeout | Real held request outlives 500-ms wait timeout; no request-token or connection termination at timeout. Explicit abort is subsequently required. Not MG1 deadline acceptance. |
| Cleanup | All 45 trials report owned cleanup complete; volatile stores cleared, unique scratch paths removed. SDK-owned process teardown only; no unrelated process mutation. |

## Exact public source surfaces

Reviewed public release source:

- [Request handler](https://github.com/github/copilot-sdk/blob/f8ae645902b74b62cd47aac1fd9b29adaec3aff2/dotnet/src/CopilotRequestHandler.cs):
  `SendRequestAsync`, `OpenWebSocketAsync`, cancellation-bearing context.
- [Session filesystem](https://github.com/github/copilot-sdk/blob/f8ae645902b74b62cd47aac1fd9b29adaec3aff2/dotnet/src/SessionFsProvider.cs).
- [Typed configuration/hooks](https://github.com/github/copilot-sdk/blob/f8ae645902b74b62cd47aac1fd9b29adaec3aff2/dotnet/src/Types.cs).
- [Tool/result/exception and abort behavior](https://github.com/github/copilot-sdk/blob/f8ae645902b74b62cd47aac1fd9b29adaec3aff2/dotnet/src/Session.cs).

GHCP001 suppression is confined to files using those reviewed public
experimental APIs. Assembly informational-version inspection only checks
public artifact metadata; it does not invoke private SDK APIs.

## Failures during fixture development

Failures were investigated, not reclassified into candidate acceptance:

1. Initial full run: 39 tests, 13 passed/26 failed. Fixture permission default
   denied **all** custom tools, so expected admitted handlers did not run.
   Fixed admission to only the explicitly registered synthetic tool in a
   host-bound execution conversation. Deny-by-default remains intact.
2. Subsequent trial failures: `rejected` ends the current turn;
   synchronous exception formatting omitted the original marker. Added
   subsequent-history proof and unconditional final-denial tests for both
   exception forms instead of pretending their expected content existed.
3. Expanded session-I/O trial: session-FS error correctly
   failed the runtime; fixed the test's success expectation to assert the
   explicit error and no fallback. Final full runner passes 45/45.
4. Raw clean-source NuGet packages initially differed only by ZIP timestamps;
   every entry's bytes and SDK assembly were identical. Normalize only those
   package timestamps. Clean independent build now produces identical pinned
   package bytes. No private SDK/native change was made.

## Repository validation

Complete root Release build: 0 warnings/errors. Core 251, Application 712,
Windows Integration 174 tests passed, total **1137**, zero skips/failures.
Production license/notice gate and CI 100% Core/Application line/branch
coverage pass. Separate experimental license/lock/source/native checks pass.
Windows x64/x86 CI publishes also pass; PE machine fields and native hashes
were inspected without launching either application. Original license/notice
artifacts are included; experimental runtime/SDK files are absent from both.
[Validation receipt](evidence/repository-validation.json) records commands,
versions, actual failures and non-run boundaries. No installed acceptance,
real speech/device, full runtime lifecycle or live account trial was run.
