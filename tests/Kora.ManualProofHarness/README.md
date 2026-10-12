# Kora Manual Proof Harness

This standalone console harness prepares operator-driven proof cases without
starting the Kora application. It automates safe setup and evidence collection,
shows only the remaining human actions one at a time, collects feedback, and
atomically saves progress after every response.

Do not treat a harness result as broader evidence than the boundary printed for
that case. The harness records observations; it does not supply missing
instrumentation, consent, ownership, isolation, privilege, or production-host
behavior.

## Prepared cases

| ID | Proof area | Why it can run outside Kora |
|---|---|---|
| LI03 | Local-inference quality | The harness generates guarded standalone evidence and human semantic review remains required by the acceptance rubric. |
| STX86 | x86 SQLCipher loading | The harness verifies and invokes the exact retained x86 native asset in the installed 32-bit Windows host; no Kora process or production data is used. |

## Automation boundary

Every active case automatically records the source revision, OS, framework,
process architecture and processor count. LI03 verifies the approved official
Ollama 0.35.1 portable archive, extracts it without installation, verifies and
hard-links the production-pinned model blobs into an isolated model store, and
starts an owned runtime on `127.0.0.1:11435`. The shared runtime on port 11434
is checked before and after the run and is never selected for model residency
changes. The harness records the isolated PID and creation time, builds without
restore, runs deterministic checks, generates 30 cold/warm trials plus
cancellation/recovery evidence, verifies selected-model cleanup, stops only the
owned isolated process, and verifies that port 11435 closes. It then runs
offline factual assessment, deduplicates byte-identical answers and opens a
local HTML review sheet. Timing and resource distributions are observations;
the harness does not invent product budgets.

If automated quality, state or CPU-only checks fail, the harness writes an
automated rejection disposition and stops. Human scoring is unnecessary to
reject that run. The HTML semantic review is generated only after all automated
gates pass; it remains necessary to qualify, not to reject, a candidate.

STX86 is automation-only. It revalidates the retained asset receipt, SHA-256
and x86 PE machine, runs the exact DLL in 32-bit Windows PowerShell, confirms
the loaded module path and process bitness, and requires successful SQLite
open, SQLCipher key, schema/data statement and close calls. It records pre/post
asset hashes and a machine-readable receipt. This closes native-load evidence
for that asset on the current machine only; it does not establish full x86 Kora
startup, installer or clean-machine readiness.

The earlier UX01 result remains in the persistent result file as historical
evidence, but UX01 is retired from the active catalog because generic framework
navigation is not a sufficient Kora proof. C01 is removed because it is wholly
machine-verifiable. SP01/SP02 are removed until a reviewed standalone live-audio
host exists. PK01 is removed until an exact installer artifact, disposable lab,
scope and rollback plan are nominated. These cases must not ask for operator
time while they are blocked, vague or automatable.

## Excluded production-only gates

The catalog intentionally excludes evidence that inherently depends on the
actual Kora host: production startup consent and persistence, real
single-instance handoff, lock/disconnect/suspend handling, production audio
ownership, integrated prompt/tool dispatch, durable approval authority,
production worker admission, and recovery of actual Kora state. Those gates
cannot be certified by this harness.

## Build without running

```powershell
dotnet build .\tests\Kora.ManualProofHarness\Kora.ManualProofHarness.csproj --configuration Release
```

## Run later

```powershell
dotnet run --project .\tests\Kora.ManualProofHarness\Kora.ManualProofHarness.csproj --configuration Release
```

By default, results are stored at
`%LOCALAPPDATA%\Kora\ManualProofHarness\results.json`. Use
`--results <path>` for an explicit file. Existing malformed or unknown-version
files are rejected instead of overwritten.

The harness saves an attempt before showing its first step, then saves each
completed step, note, feedback response and final outcome. Choose `P` to pause
and `R` from the main menu to resume the latest in-progress attempt.
Use `--resume <proof-id>` to resume a specific in-progress case immediately.
Use `--start <proof-id>` to create, persist and immediately start a new attempt.
Use `--automate <proof-id>` to run only automated preparation and evidence
gates. An automated rejection is saved as failed; a candidate that passes every
automated gate remains in progress for later human semantic review.

For non-interactive validation only:

```powershell
dotnet run --project .\tests\Kora.ManualProofHarness\Kora.ManualProofHarness.csproj --configuration Release -- --self-test
```
