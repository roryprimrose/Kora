# D-001 / D-004 evidence and disposition

**Disposition: experimental candidate only; production Gate 0 incomplete.
D-001 and D-004 remain open. Draft PR; no auto-merge while blocked.**

[Machine-readable observations](evidence/results.json) contain the actual run
timestamp, Windows/CPU/runtime identity, counters, timings and statuses.
The [reproduction commands](README.md#reproduce-on-windows) regenerate them.
No payloads, real credentials, account names or raw SDK error dumps are
included. The pinned provider is a scripted loopback endpoint, not a live
language model. These are real SDK/runtime boundary tests, not provider
quality or hosted service acceptance.

## Evidence matrix

| Requirement / approach | Result | Observed evidence and boundary |
|---|---|---|
| Actual pinned SDK/runtime | PASS | SDK 1.0.16, bundled runtime 1.0.90/protocol 3; metadata and launcher/payload byte hashes checked before each trial. |
| Initial context egress | PASS | Submitted/transformed hooks ran once; approved marker reached captured HTTP, denied marker reached zero payloads. |
| Denied tool effects | PASS | Pre-tool denial ran once; counter handler executed zero times. Explicit advertised test-tool subset only. |
| Successful subsequent tool-result egress | PASS | One admitted synthetic read and one post-tool hook; truthful approved receipt retained, denied result marker absent from both outbound requests. |
| Hook-only failed-result interception | **FAIL** | Successful-result hook ran zero times; failure hook ran once but cannot replace failure output. It supplied guidance only. Do not integrate using hooks alone. |
| Final request interception of failed results | PASS | Experimental supported handler observed and blocked the unredacted continuation before HTTP; denied marker reached zero provider payloads. |
| Disabled built-ins/discovery | PASS, scoped | Zero advertised built-ins/MCP tools; forced excluded PowerShell proposal returned failed tool receipt. Discovered instruction canary absent from model requests. |
| Disabled session persistence / collection | PASS, scoped | Volatile host session FS observed writes and was cleared; no context marker on scoped disk scan. Discovery/memory/store/skills/export/spill disabled. Not a global filesystem or OS collection claim. |
| Streaming and terminal state | PASS | Multiple actual SDK SSE answer deltas, completed answer and idle event. Provisional output is not a receipt. |
| Authentication transport | PASS, synthetic | Synthetic auth only in HTTP header, never model body; no actual service login tested. |
| Auth/throttle/server errors and fallback | PASS, synthetic | Actual SDK error events for scripted HTTP 401/429/500, explicit fallback, exactly three forwarded HTTP calls across three sessions. Three automatic SDK retry attempts were intercepted and blocked. |
| Independent execution/management | PASS, loopback | Two execution conversations blocked (tool/inference), management completed independently with no execution context/tool leakage. No production scheduling/resource lease claim. |
| Provider isolation | PASS, loopback | Host-bound distinct endpoints and session histories; management has no execution tool catalogue. Real account identity isolation is BLOCKED. |
| Truthful cancellation | PASS, scoped | SDK abort acknowledgement recorded, host cancellation immediate, no late forwarded request. One admitted non-cooperative tool completed after cancellation: outcome unknown at cancellation, not rollback. |
| Exact 32 KiB input | PASS | Actual complete outbound JSON body accepted at 32768 bytes; 32769 rejected before HTTP. Host UTF-8 admission also tested with multibyte input, not character counts. |
| Exact 4 KiB output | PASS | Typed multibyte JSON accepted at exactly 4096 UTF-8 bytes; next-byte streamed overflow rejected with no accepted proposal. |
| Real 15-second deadline | PASS | Real held provider response returned deadline fallback at approximately 15 seconds; exact measured value in JSON. No waiting for send/abort/provider acknowledgement. 750 ms test-run timing tolerance is not an increased configured deadline. |
| Host admission / quotas | PASS, deterministic | One in-flight, 30 calls/rolling hour, exact one-hour expiry, local-only rejection and quarantine after unconfirmed termination. Not upstream quota evidence. |
| All-destination egress, global diagnostics/persistence | **BLOCKED** | Only model HTTP and scoped trial disk are observed. PowerShell initialization metadata exists. No all-network/file trace, containment, crash-dump or system-wide collection evidence; unknown paths cannot be assumed compliant. |
| Hosted auth/account/terms/concurrency/quotas/cost | **BLOCKED** | User chose no-account loopback only. No live provider calls, provisioning or model charges. Account-specific permitted use and costs unresolved; [restrictions and assumptions](PROVIDER.md). |
| Production .NET adapter parity | **BLOCKED** | Node control-point candidate exercised; no production adapter implemented, and equivalent pinned .NET surfaces are not yet verified. |

PASS means the specifically named measured path passed, not its blocked
broader capability. [results.json](evidence/results.json) aggregates related
measurements; it deliberately returns nonzero readiness when any FAIL or
BLOCKED row remains. Focused test success includes verifying the expected
hook-only failure; it does not reclassify that failure as a passed gate.

## Recommendations

1. Retain SDK 1.0.16 / runtime 1.0.90 as a **candidate**, not an approved
   production runtime. Prefer explicit empty mode and child-process stdio;
   in-process transport does not honor the same environment isolation
   options. Do not monkey-patch undocumented internals.
2. Initial prompt hooks and successful-result hooks alone are insufficient.
   Require the supported final model-request boundary and host-sanitized
   success/error/denied/timeout results. Every new transport/input surface
   needs its own evidence; WebSockets, attachments, MCP, skills, memory,
   external extensions and remote export remain disabled here.
3. Before R08 exposure, verify equivalent pinned .NET callbacks, account/auth
   paths, all destinations and diagnostics/storage under real observation.
   Failure or lack of observation keeps the capability unavailable, with an
   actionable explanation, never an automatic provider fallback.
4. Before R13 model-assisted management, validate the user-authenticated
   provider's terms, actual three-conversation permission/capacity, account
   quotas and billed token/reasoning behavior under an explicitly approved
   trial budget. Retain deterministic local management regardless.
5. Cancellation must distinguish admitted effects, abort acknowledgement,
   physical stop, late callbacks and unknown outcomes. Quarantine uncertain
   inference; do not infer rollback or replay timed-out work.
6. Keep R01 authority boundaries: management status/proposals are not task
   approvals, script execution or power dispatch. This experiment proves no
   production grants, ledger transactions, resource leases or OS workers.

## Publication and validation

The branch is rebased onto `origin/main` before work and again before final
validation/first push, with R01 ancestry checked. The proof/tests are repeated
after the latter rebase. Only this experimental directory is staged.

The repository's existing required checks are **Portable build, tests,
coverage, and package** and **Windows integration tests** (strict
up-to-date main). They do not run these manual experimental Node tests.
No existing workflow is changed and no required check/review is bypassed.
When all blocking proofs/prerequisites are actually resolved, mark ready and
enable squash auto-merge subject to those checks and any applicable reviews;
this incomplete draft deliberately does not enable it.
