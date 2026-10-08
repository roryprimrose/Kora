# R02 runtime/provider feasibility proof

Experimental evidence for D-001 and D-004, **not production integration or
decision closure**. Gate 0 is not passed: hook-only failed-result mediation
fails, and global observation, hosted-account trials and .NET parity are
blocked. The user approved merging these partial findings with
[outstanding proofs deferred](../../Design/Deferred_Validation.md#runtimeprovider-follow-up).
Remote capabilities remain disabled; merge is not production certification.

The user approved **no-account loopback validation only**. No hosted inference,
paid operation, resource provisioning, account sign-in or credential reading
was performed. GitHub authentication used to publish this branch is not model
provider approval. All prompts, tool arguments/results and provider responses
are synthetic.

The [canonical technical outcomes and next gates](../../Design/Runtime_Provider_Feasibility.md)
and [updated roadmap](../../Design/Implementation_Roadmap.md#r02-runtimeprovider-follow-up-gates)
now turn these measurements into delivery work. This experiment remains the
failure witness and reproducible test source, not the owner of design policy.

**Current disposition:** migrate applicable mediation, all-status denial,
serialization and cancellation assertions into maintained production runtime
integration tests before archiving executable harnesses. Keep failure witnesses,
exact candidate receipts and unresolved native/provider observations distinct;
Node comparison is not .NET qualification. This is not a default-CI or aggregate
merge gate. The [three-tier policy](../../Design/Acceptance_Criteria.md#three-tier-qualification-policy)
and [disposition inventory](../../Design/Implementation_Roadmap.md#experiment-disposition-inventory)
own affected enablement/RC scope and exact-equivalence/consumer checks. No
executable is removed by this policy.

## Prerequisite and scope

R01 was verified after rebasing: merged `roryprimrose/Kora#19`, commit
`7d5e6a352261dce48f2ca4d3048650ee13f51705`, is an ancestor of both the session
branch and `origin/main`.

Read contracts:

- [Runtime ownership, independent lanes and integration proof](../../Design/Architecture.md).
- [Interaction and session identities](../../Design/Interaction_And_Sessions.md).
- [Security and work-management boundary](../../Design/Security_Data_Flows.md).
- [Management envelope and deterministic degradation](../../Design/Work_Management.md).
- [Gate 0](../../Design/Acceptance_Criteria.md) and [D-001/D-004](../../Design/Decision_Register.md).

Existing bootstrap inspection:

- [Composition](../../src/Kora/Program.cs) contains no Copilot adapter.
- [Ollama reasoner](../../src/Kora.Windows/Dependencies/WindowsOllamaReasoner.cs)
  uses one non-streaming JSON selection response, a 4096-character request
  bound, host-parsed proposals and a two-minute inference timeout. It is not a
  shared tool loop or a 15-second management provider.
- [Inference probe](../../src/Kora.Windows/Dependencies/LocalInferenceDependencyProbe.cs)
  validates the digest-pinned local model and a completed inference response.
  This is setup readiness, not remote control-point or concurrency evidence.

The executable proof changes no registry, shared production loop, clipboard
broker, application adapter, production manifest/composition or existing CI.
Experimental dependencies remain local to this directory. The follow-up
documentation records outcomes and the path forward in the canonical design,
decision register and roadmap; it does not enable production capabilities.

## Exact candidate

| Component | Pin |
|---|---|
| Node.js | 24.16.0, Windows x64 |
| npm used | 11.13.0 |
| TypeScript | 5.9.3 |
| Node typings | 24.19.0 |
| Actual SDK | `@github/copilot-sdk` 1.0.16 |
| Bundled platform package | `@github/copilot-sdk-win32-x64` 1.0.16 |
| Actual runtime | 1.0.90, JSON-RPC protocol 3 |
| Transport | Bundled child process / stdio, not ambient CLI or in-process FFI |
| Provider | Scripted OpenAI-compatible HTTP/SSE on ephemeral `127.0.0.1` ports |

[package-lock.json](package-lock.json) records registry integrity hashes for
transitive dependencies and optional platform packages. [candidate.ts](src/candidate.ts)
checks the SDK metadata and both Windows runtime byte hashes before starting.
An ambient `copilot` installation or `COPILOT_CLI_PATH` is not selected.

- Runtime launcher SHA-256:
  `7021cf1f25eb6b75e64c05e8f805747c62dd420dc8760659660291809e92603a`
- Runtime payload SHA-256:
  `41ebb48367f96c984babde61afb68f22a85ab8fd9c037fdccf1778881c4bba05`

The SDK package declares MIT. This is not a complete redistribution/legal
review of every bundled native component, nor permission to use hosted
services under an arbitrary account. See [provider restrictions](PROVIDER.md).

## Reproduce on Windows

Run from this worktree, using the pinned Node version:

```powershell
Set-Location experiments\r02-runtime-proof
node --version
npm --version
npm ci --ignore-scripts --no-audit --no-fund
npm test
npm run proof
$LASTEXITCODE
```

`npm test` builds and runs focused host tests plus real SDK/runtime boundary
tests. A passing test verifies an expected observed limitation too; it does
**not** mean all capability gates pass.

`npm run proof` regenerates [evidence/results.json](evidence/results.json).
Exit **2** means the evidence matrix contains FAIL/BLOCKED rows and is the
expected result for this partial proof. Exit 0 is reserved for an all-PASS matrix;
other nonzero results must be investigated. Never interpret exit 2 as
production success or suppress it in release validation.

Individual cases can be selected without a second test runner:

```powershell
npm run build
node --test --test-name-pattern="initialContext|toolResult|failedToolResult" dist\runtime.test.js
node --test --test-name-pattern="managementEnvelope|exactManagementBounds" dist\runtime.test.js
node --test --test-name-pattern="independentLanes" dist\runtime.test.js
```

No running service or paid model is required. The proof starts its own
loopback listeners and pinned SDK child processes, verifies listener arrival
and runtime status, then closes them. Trial directories are generated only
under ignored `.runtime`; they and volatile session files are cleared in
`finally`. `node_modules` and compiled `dist` are ignored build caches.
The fixture deliberately starts outside repository context.

## What the boundary actually observes

[runtime.ts](src/runtime.ts) uses supported SDK options: `mode: "empty"`,
explicit tool allowlists, excluded built-in/MCP tools, deny-by-default
permissions, no discovery, custom instructions, skills, file hooks, git
context, shared session store, memory, compaction, embedding retrieval,
remote export, extension/canvas tools or large-output file spill.

Session I/O is routed to the host's [volatile FS](src/memory-fs.ts), with
SQLite/todo capability disabled. The runtime's environment is allowlisted;
its home/profile/temp directories are trial-local, stored/ambient account
authentication is disabled, and PowerShell/OTel telemetry opt-out settings
are supplied. Settings are not themselves proof of global enforcement.

The supported, **experimental** `CopilotRequestHandler` is the final
model-request gate. It:

- Requires a host-bound session/destination and blocks unexpected destinations.
- Inspects the actual serialized HTTP body, including subsequent tool results.
- Blocks denied synthetic markers and the synthetic credential sentinel.
- Applies the complete management request body's 32 KiB limit.
- Allows only one forwarded HTTP request per management conversation, so SDK
  retry attempts cannot become automatic provider requests.
- Blocks further egress after host cancellation and rejects WebSockets.

[provider.ts](src/provider.ts) independently records only requests that
actually crossed that gate. No SDK internals are patched.

This observes model-layer HTTP to the synthetic endpoint. It is **not** a
packet trace, OS sandbox, all-destination audit, production data classifier,
or proof about global service/telemetry/crash paths. The runtime initializes
PowerShell and writes `StartupProfileData-NonInteractive` metadata despite
having no model-callable shell tools. No context marker was found in that
scoped disk scan; global persistence remains unproved.

## Management and cancellation semantics

[envelope.ts](src/envelope.ts) is a disposable test envelope, not Kora's
scheduler or interaction service:

- Input admission uses UTF-8 bytes, not characters. The final serialized
  model body is separately limited, including SDK framing/context overhead.
- Complete typed output, including JSON fields, must fit 4096 UTF-8 bytes.
  Overflow is rejected, never truncated into a success-shaped proposal.
- The 15-second host timer starts at inference dispatch, after session setup.
  It does not await SDK send/abort acknowledgement or provider completion.
  The real held HTTP case tests 15 seconds; a short supplementary fake test
  also verifies stalled SDK acknowledgements cannot extend the host timer.
- One request is admitted at a time; remote attempts are capped at 30 per
  rolling hour, including failures. Unconfirmed termination quarantines
  inference; deterministic local status/choices remain available.
- The synthetic schema accepts status proposals only. No ledger mutation,
  action approval, grant consumption or power execution is implemented.

Two execution conversations and one independent management conversation
made progress on the same SDK client. Management used a different provider
endpoint and inherited no execution tools or private-context markers.
This is not a verified production resource budget or hosted account quota.

Cancellation reports host cancellation, **not rollback**. A harmless,
already-admitted, non-cooperative tool completed after cancellation;
its outcome was unknown at cancellation, and no later provider request
was forwarded. `abort()` acknowledgement alone is not proof all effects
stopped. No claim is made that late-result callbacks were generated when
the recorded late-event count is zero.

## Evidence and recommendation

See [evidence report](EVIDENCE.md) and [machine-readable results](evidence/results.json).
Use an approved live account only after separate account/terms and budget
approval. D-001/D-004 remain open. Do not enable production remote execution
or model-assisted management from these local passes.

## Proof code lifecycle

Rerun affected assertions for material SDK/runtime/request-path changes, not
unrelated merges. Migrate applicable cases to maintained production integration
tests with exact-profile coverage. Keep the known hook-only failed-result case executable
until production composition makes that unsupported path impossible. Remove
the standalone Node harness only after parity, lifecycle and request-boundary
coverage has migrated and consumer/reference checks pass; preserve reviewed
historical receipts. Remaining candidate comparisons need explicit disposition
under the current roadmap inventory above.
