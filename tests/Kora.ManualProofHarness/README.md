# Kora Manual Proof Harness

This standalone console harness prepares operator-driven proof cases without
starting the Kora application. It automates safe setup and evidence collection,
shows only the remaining human actions one at a time, collects feedback, and
atomically saves progress after every response.

Do not treat a harness result as broader evidence than the boundary printed for
that case. The harness records observations; it does not supply missing
instrumentation, consent, ownership, isolation, privilege, or production-host
behavior.

## Active cases

There are no active manual proof cases. The evaluated local-inference
candidates are rejected, the x86 SQLCipher load was a one-time historical
diagnostic, and the remaining environment observations are release/profile
qualification rather than a manual development queue. Persisted historical
attempts and evidence remain readable.

## Automation boundary

Historic cases recorded the source revision, OS, framework, process architecture
and processor count. LI03 verified the approved official Ollama 0.35.1 portable
archive, ran an owned isolated runtime on `127.0.0.1:11435`, generated
cold/warm and cancellation/recovery evidence, verified cleanup, and retained
automated dispositions. Timing and resource distributions were observations;
the harness did not invent product budgets.

If automated quality, state or CPU-only checks fail, the harness writes an
automated rejection disposition and stops. Human scoring is unnecessary to
reject that run. The HTML semantic review is generated only after all automated
gates pass; it remains necessary to qualify, not to reject, a candidate.

The earlier UX01 result remains in the persistent result file as historical
evidence, but UX01 is retired from the active catalog because generic framework
navigation is not a sufficient Kora proof. C01 is removed because it is wholly
machine-verifiable. SP01/SP02 are removed until a reviewed standalone live-audio
host exists. PK01 is removed until an exact installer artifact, disposable lab,
scope and rollback plan are nominated. These cases must not ask for operator
time while they are blocked, vague or automatable.

LI03 is retired because the pinned and comparison candidates failed automated
gates; no human scoring is needed. STX86 is retired because repeating native
loader/toolchain behavior does not reduce a current Kora implementation risk.

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
