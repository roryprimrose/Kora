# R02-RT1 actual .NET public control-point conformance

**RT1 PASS for the explicitly approved exact-tag source-built minimal
HTTP/stdio profile only. Gate 0 remains open; no production adapter is enabled.**
The runnable tests use the actual public .NET SDK and native runtime, not host
truth tables, private reflection, patched internals or the Node SDK.
The historical [Node results](../r02-runtime-proof/evidence/results.json) are
unchanged. Their hook-only FAIL remains a separate regression witness; this
fixture reproduces it with a final gate preventing unsafe forwarding.

**Current disposition:** migrate applicable final-request, all-status,
denied-effect/marker and cancellation assertions into maintained production
runtime integration tests before executable archival. Source-built and released
profiles remain distinct. The
[three-tier policy](../../Design/Acceptance_Criteria.md#three-tier-qualification-policy)
and [disposition inventory](../../Design/Implementation_Roadmap.md#experiment-disposition-inventory)
require exact-profile equivalence, preserved critical/failure fixtures and
consumer/reference checks; no executable is removed here. Affected profile
enablement/RC qualification remains gated, not unrelated merges or default CI.

## Tested identity and dependency admission

Windows x64, .NET SDK **10.0.401**, .NET runtime **10.0.12**; native runtime
**1.0.90**, protocol **3**. SDK public release `v1.0.16`, exact source
`f8ae645902b74b62cd47aac1fd9b29adaec3aff2`; distinctly named local package
`1.0.16-rt1.source.f8ae645.1`.

Direct NuGet, CDN and alternate TLS transports failed acquisition. The user
explicitly approved building the original reviewed release source instead.
No SDK source/project is patched. Build properties select net10.0, the distinct
version, explicit source provenance, normalized source paths and no implicit
runtime download. SourceLink is disabled for the verified ZIP build rather
than accidentally attributing SDK code to Kora's enclosing checkout.
Only package ZIP timestamps are normalized; entry bytes are unchanged.
[Clean-source reproduction](evidence/source-reproduction.json) proves the
resulting SDK package byte-identical in a second source directory.
Released NuGet byte parity remains **BLOCKED**, not inferred from this test.

Exact source/archive/native, observed SDK package/assembly and fixture-source/
assembly SHA-256 identities are in [results](evidence/results.json) and
[disposition](evidence/disposition.json). Runtime bytes are the same verified
launcher/payload as the Node witness, obtained from the official CLI release,
not an ambient installed executable.

Review [licenses/provenance](LICENSE-REVIEW.md) before acquisition.
[Fixture lock](packages.lock.json) and [SDK build lock](evidence/sdk-build.lock.json)
pin separate closures. [Fixture license scan](evidence/dependency-licenses.json)
covers 23 packages; [SDK build scan](evidence/source-build-licenses.json) covers
7. SDK/managed dependencies are MIT except Apache-2.0 AwesomeAssertions.
Native runtime uses the GitHub Copilot CLI License, **not MIT**. Full optional
CLI bundle redistribution/notice admission remains blocked. No native binary
or experimental package is included in the production solution or notices.

## Reproduce from repository root

Use already installed reviewed .NET versions. Do not install/elevate/change
policy or use a live provider as a workaround; obtain separate approval.

```powershell
.\experiments\r02-dotnet-control-proof\Prepare-Candidate.ps1
.\experiments\r02-dotnet-control-proof\Test-Reproduction.ps1
.\experiments\r02-dotnet-control-proof\Run-Conformance.ps1
```

Preparation downloads only public source/runtime archives and repository-local
dependencies after review. It checks archive hashes, every reused source file,
extra compile inputs and locks. Its public Microsoft feed and local source-build
feed have explicit package mappings; no private feed credential is supplied.
`-UpdateFixtureLock` is reserved for an intentionally reviewed manifest or
packaging change, not routine reproduction.

The runner builds Release with warnings as errors, runs **all 45** xUnit v3
tests and rejects zero/filtered/incomplete runs. It records 44 PASS selected
profile rows plus **one expected hook-only FAIL**. Test success means that
failure was reproduced, not that hook-only integration passed. Exit 0 means
RT1 selected-profile conformance, never full Gate 0 readiness.
The normal test runner also works:

```powershell
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet build .\experiments\r02-dotnet-control-proof\ControlProof.csproj --configuration Release --no-restore
dotnet test --project .\experiments\r02-dotnet-control-proof\ControlProof.csproj --configuration Release --no-build --results-directory .net-test-artifacts\rt1 --report-trx
```

## Isolation, bounding and receipts

- Owned `TcpListener` endpoints bind only ephemeral `127.0.0.1` ports.
  Responses are scripted synthetic OpenAI-compatible SSE, not model inference.
- Explicit child stdio connection; empty mode; `UseLoggedInUser=false`.
  Child environment is replaced, not merged. It contains no inherited
  GitHub/Copilot/Azure/provider credentials, proxy configuration or user PATH.
  Home/profile/appdata/temp/base paths are unique owned scratch directories.
  No auth/status-token dump, sign-in, real model endpoint or account is used.
- Only verified launcher/runtime payload and original license are present in
  the active runtime directory; additional assets cause startup rejection.
  This is not OS containment or proof that native background networking is
  absent. No machine-wide firewall/security policy is changed.
- Public `CopilotRequestHandler.SendRequestAsync` sees the entire serialized
  model HTTP body. It binds created session IDs to host-owned lane/destination,
  denies unknown routes, content markers, credential-in-body, cancelled
  generations and bounded retries. Its owned HTTP client disables proxies/
  redirects; `OpenWebSocketAsync` always rejects. The marker filter is a test
  oracle, **not a production source-lineage classifier**.
- Public `SessionFsProvider` is per-conversation volatile storage, bounded to
  64 files/256 KiB per write in a virtual namespace, no native SQLite capability.
  No shared database, durable history, attachment or binary FS is admitted.
- Tools are only explicit harmless owned-scratch reads/writes. Permission
  allows only that advertised tool in a host-bound execution conversation;
  everything else is denied. Pre-tool hooks can deny before its file write.
- Start/session bounds are 15 seconds; provider arrival 10 seconds; completion
  12 seconds; trial 45 seconds; provider lifetime 2 minutes; host HTTP 20
  seconds; abort 5 seconds; teardown 10 seconds before owned SDK force-stop.
  These are fixture bounds, **not MG1's 15-second management envelope**.
- Reports retain UTC timestamps, counts, sizes, digests and fixed synthetic
  outcomes. Captured model bodies remain only in fixture memory. No raw SDK
  diagnostics, credential header values, user text or logs are retained.
- Finally blocks stop only their own SDK client/listeners and clear volatile
  stores. Disk cleanup targets only the exact unique `trial-*` path after
  containment/reparse checks. No name-based process kill, primary checkout,
  other worktree or global session cache is touched. Ignored candidate/build
  caches remain for reproducibility; runtime trial scratch is empty.

  ## Exposed path inventory

  | Surface | Tested profile and disposition |
  |---|---|
  | Model HTTP/SSE | Actual whole serialized request routed through public final handler; allowlisted loopback destination; observed auth/error/history/results/cancellation paths. |
  | Model WebSockets | Environment opt-out plus public handler rejection; no WebSocket trial, therefore disabled/unverified, not parity claimed. |
  | Runtime JSON-RPC stdio | Owned verified child; actual status/session/tool/request/session-I/O callbacks observed. Its IDs are correlation only; no remote runtime server admitted. |
  | Built-in tools including shell/browser/search/computer use | Empty mode, explicit tool allowlist, `builtin:*`/`mcp:*` exclusion and deny-by-default permission. Actual advertised catalogue zero and forced excluded PowerShell tool fails. Other built-in effects are not separately exercised or admitted. |
  | Host custom tools | Only explicitly registered synthetic owned-scratch read/write, execution lane only; pre-effect denial and post-admission Unknown outcome measured. |
  | MCP, skills, plugins, custom agents, extensions, canvas, scheduling | Empty catalogues/directories and explicit feature disables. No server connection, OAuth, helper loading or tool admission trial; remain unavailable. |
  | Context discovery/instructions/git/file hooks | Explicitly disabled; synthetic AGENTS canary absent from every observed model body. No claim of all native file access prevention. |
  | Session history/files | Actual public volatile I/O writes and write-failure behavior observed; no scoped marker disk fallback. Native SQL capability disabled; no SQLite/binary/attachment trial. |
  | Memory, infinite sessions/compaction, spill, embeddings, shared store/export | Explicit disable/in-memory settings; no production durable store or recall admitted. Automatic optional paths not independently exercised. |
  | SDK/test/runtime diagnostics and telemetry | Runtime log None, session telemetry off, child telemetry opt-outs, MTP/.NET CLI opt-outs. No raw logs retained. Full native diagnostic/OS crash/event handling remains RT2-unverified. |
  | Non-model auth/discovery/update/config/initialization networking | No sign-in or live account. Minimal native assets/environment and explicit BYOK routing do not establish absence of background native requests; full lifecycle destination attribution remains RT2-unverified. |
  | Optional bundled native helpers | In acquisition archive only, absent from active profile. Full helper licensing/loading/redistribution not admitted. |

  ## Conformance interpretation and handoffs

See [technical evidence](EVIDENCE.md) and the canonical
[feasibility direction](../../Design/Runtime_Provider_Feasibility.md).
RT1 unblocks **preparation/execution of RT2 and MG1 for these exact bytes**.

- RT2 must independently observe all startup/session/auth/error/shutdown
  network, file and diagnostic paths; check optional helper loading,
  initialization metadata, background services, crash/OS diagnostics and
  prevention, not merely absence of canaries in a scoped scan.
- MG1 must implement complete 32768/32769-byte requests, 4096/4097-byte typed
  proposals, 15000-ms dispatch deadline independent of stalled acknowledgement,
  one in-flight/30 attempts per rolling hour/no retry, manager topology and
  unknown-effect quarantine. RT1 counters do not accept that envelope.
- PV1 remains blocked: intended provider/account terms, secure auth,
  quotas/concurrency/cost and explicit potentially paid-usage approval required.
- R04 owns durable host identities/traces/audit. SDK `SessionId`, request IDs,
  tool arguments and trace headers remain untrusted transport correlation.
  Bind immutable host context, never promote them to authority or fabricate
  a durable commit from an SDK event. Use causal links for deferred callbacks.
- R08/R13 still need integrated host, scheduler and installed-app acceptance.
  No sidecar, provider substitution, Ollama environment change or installer
  acceptance follows from this proof.
