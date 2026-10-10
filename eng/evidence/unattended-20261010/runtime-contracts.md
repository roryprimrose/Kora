# Maintained runtime receipt/admission contract proof

## Result

On 2026-10-10 local time (2026-10-09 UTC), the complete maintained
[runtime contract suite](../../Test-RuntimeValidationContracts.ps1) passed
**49/49 checks in each of two unattended runs**, under `en-AU` and `en-US`.
That is **98 passing check executions, zero failures, and 49 distinct checks**,
not 98 distinct contracts. Both runs exited successfully; neither was filtered.

The tested repository HEAD was
`56df1ee136a687d8cd3e2294ca25e38f9eab75dd`. Execution used a verified distinct
linked worktree, PowerShell `7.6.6`, and `pwsh -NoProfile -NonInteractive`.
No edits or builds used the root checkout. Each run created a new output
directory outside the checkout; existing receipts were not overwritten.

The [sanitized machine-readable receipt](runtime-contracts.json) contains the
complete check names, run timestamps, raw receipt SHA-256 values, and exact
identities of the six loaded or guard-tested maintained scripts. No production
code, helper rules, historical pins, or historical evidence needed modification.

| Full-suite host culture | Passed / total | Failed | Historical files unchanged | Synthetic files remaining |
|---|---:|---:|---:|---:|
| `en-AU` | 49 / 49 | 0 | 133 / 133 | 0 |
| `en-US` | 49 / 49 | 0 | 133 / 133 | 0 |

Each full run also contains both invariant UTC parsing checks. An earlier
pre-pause `en-AU` pass is retained in raw evidence, but is not counted in the two
noninteractive runs above.

## What the 49 checks establish

| Contract group | Checks | Verified boundary |
|---|---:|---|
| Historical profile and regression receipts | 8 | Source-built and released SDK identities remain distinct even with shared native bytes. Historical controls retain all 45 rows, including the rejected hook-only `FAIL`; stale, filtered, relabelled, and duplicate evidence is rejected. |
| Artifact identity | 8 | Missing inputs stay `Blocked`; explicitly missing or changed inputs fail. Package and embedded assembly hashes are independent; duplicate entries are rejected and supplied absolute paths are omitted from artifact receipts. |
| Output and checkout ownership | 3 | Existing output is not overwritten, preparation output cannot be the repository, and a non-linked checkout is rejected. |
| Stage admission and operator requests | 6 | Passing preparation does not admit native/account/production stages. R13 deterministic readiness stays independent. Generated requests confer no approval/provider selection or invented spending cap. RT2 retains its 60-second bound. |
| UTC parsing | 2 | ISO receipt strings retain the expected date and zero offset under both locales. |
| Coordinator approval | 2 | Synthetic-native and released-bounded coordinators reject absent explicit approval before execution. |
| Released lifecycle receipts | 15 | Complete synthetic bounded receipts preserve all-path `Blocked`. Stale/duplicate/mislabelled identities, substituted SDK/native/fixture bytes, cleanup loss, owned survivors, model-boundary markers, admission promotion, observation loss/gaps, and a missing socket positive control are rejected. |
| Maintained script syntax | 5 | All five scripts selected by the harness parse without errors. The shared distribution helper is additionally loaded and hashed. |

The checked MG1 request bounds are 32,768 serialized input UTF-8 bytes,
4,096 complete typed output UTF-8 bytes, a 15,000 ms dispatch deadline,
one in-flight request, 30 attempts per rolling hour, one forwarded attempt
per request, and quarantine for unknown termination. These are deterministic
request-contract checks, not a fresh runtime implementation qualification.

## Historical preservation and coordinator limits

SHA-256 inventories before execution and after each full run matched for all
133 tracked files across the four historical runtime experiment directories.
The original experiment evidence, source/released separation, preparation
historical guard, and byte pins remain unchanged.

No standalone preparation or invocation coordinator was run. The full suite
does invoke three **negative guard tests**: preparation with repository output,
synthetic invocation without native approval, and released invocation without
its separate bounded-profile approval. These throw at the early output-location
or approval guard, before historical checkout validation, acquisition, staging,
build, native launch, or provider access. Their rejection is not evidence that
current historical inputs are eligible for approved coordinator execution.

## Admission remains closed

This proof validates host-owned deterministic receipt and admission rules using
synthetic bytes and synthetic lifecycle observations. It does **not** freshly
execute the historical 45 RT1 trials, 22 host-component tests, 16 actual-runtime
cases, or native RT2 trials. Historical receipts are inputs, not fresh trials.

Native launch, dedicated-host collection, account/provider execution,
potentially paid usage, and production model assistance remain unqualified.
RT2, execution PV1, management PV1, R08, and model-assisted R13 stay `Blocked`;
the deterministic R13 stage remains `Independent`, with its own production
prerequisites unevaluated.

No package restore/acquisition, SDK/native runtime trial, live account/provider
call, credential acquisition, consent/license acquisition, provisioning,
elevation, installation, GUI/device interaction, trace collection, or
administrative/network policy change was performed. Any slice needing new
human consent or unavailable authority was left unrun; this receipt grants none.
Privacy and all-path runtime acceptance are not claimed.

## Reproduction and evidence retention

From a verified isolated linked worktree, use a different fresh output directory
for each run:

```powershell
pwsh -NoProfile -NonInteractive -Command {
    $ErrorActionPreference = 'Stop'
    [Threading.Thread]::CurrentThread.CurrentCulture =
        [Globalization.CultureInfo]::GetCultureInfo('en-AU')
    [Threading.Thread]::CurrentThread.CurrentUICulture =
        [Globalization.CultureInfo]::GetCultureInfo('en-AU')
    & .\eng\Test-RuntimeValidationContracts.ps1 -OutputDirectory $env:KORA_FRESH_PROOF_OUTPUT
}
```

Supply an absolute, new external directory through `KORA_FRESH_PROOF_OUTPUT`;
repeat with `en-US` and another new directory. Do not substitute the guarded
preparation/native coordinators or relax their historical eligibility checks.
The maintained harness fails by throwing and reports success only after all
49 checks finish.

Raw transcripts, original receipts, per-run records, historical hash inventories,
and tested-script identities are retained outside Git in the assigned session
evidence location. The JSON uses relative raw-evidence names and SHA-256 links,
not machine-local paths. Only this bounded report and sanitized receipt are
published; no user paths, credentials, raw logs, or generated binaries are added.

The harness removed its synthetic binary/package and negative-test checkout.
No temporary code was created. Raw evidence and the worktree are retained;
the parent owns worktree cleanup after merge.
