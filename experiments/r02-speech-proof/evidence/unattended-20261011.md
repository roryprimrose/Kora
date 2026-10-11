## 2026-10-11 unattended file-only speech proof

**Blocked dependency-backed rerun; dependency-free checks passed. D-002 and D-007 remain open.** The run started from `origin/main` `cf44a5f2bc893a5d19f445b4fc4dd31a204b7d18`. The [machine-readable receipt](unattended-20261011.json) records exact source and reviewed-evidence hashes.

No microphone capture, audible playback, Kora process, native window, device or setting change, live trial, asset acquisition, credential use, or package-registry access occurred. Historical evidence was not changed.

### Observed results

| Check | Result | Observed evidence |
|---|---|---|
| `Validate.ps1` dependency validation, compilation and 27-test suite | **Blocked** | Exit 1 before dependency validation: `Create the experiment-local Python environment first; see README.` |
| Dependency-free source compilation | **Passed** | CPython 3.12.10 compiled all seven experiment source and test files with `compileall`; compilation does not import or exercise the missing dependencies. |
| Privacy receipt deterministic tests | **Passed** | 12 passed, 0 failed in 0.014 s. Ignored local output SHA-256: `91d736a28185ad68224ae6d228379632245a6d1c42accecc2421eafd11fa1476`. |
| 15 benchmark deterministic tests | **Blocked** | Test module import failed before test execution with `ModuleNotFoundError: No module named 'numpy'`. |
| Fresh threshold sweep, 5 warmups / 30 repetitions / 60-second pacing | **Blocked** | The documented `.venv\Scripts\python.exe` was absent; no result JSON or measurements were produced. |
| Optional 1800-second file-only resource pilot | **Blocked** | The same interpreter prerequisite was absent; 0 paced seconds executed and no resource measurements were produced. |
| Dependency licence and notice parity | **Blocked** | The local scanner started but required project `obj\project.assets.json` files were absent. No restore or package acquisition was performed. |

The experiment-local virtual environment, model assets and fixtures were absent from this worktree and the other registered Kora worktrees. Global CPython 3.12.10 did not contain `numpy`, and no existing pinned sherpa archive was found in the checked Kora repositories or local Copilot session storage. Nothing was downloaded or installed to turn the blocked checks into results.

### Candidate and packaged-notice review

The existing [candidate review](../candidates.json) remains unchanged. Its SHA-256 is `51176597879f70f8a6a30b47694d93e119e0b73ccdb64e6e0985f013de9afba2`.

openWakeWord remains blocked without a reviewed Kora model and applicable model clearance. Porcupine remains blocked without an authorized account, AccessKey, evaluation agreement, Kora profile and redistribution clearance. Sherpa remains historical synthetic evidence only because the pinned archive and extracted model were unavailable for fresh byte verification or inference.

The committed [third-party notice](../../../THIRD-PARTY-NOTICES.md) was reviewed by identity only; its SHA-256 is `747fb0c17748c3829d420d8dabd16fccff0a4e0d14eb47e34a8b6f054deacd87`.

The existing [2026-10-10 published-byte evidence](../../../eng/evidence/unattended-20261010/published-bytes.md), SHA-256 `0f3013a46c3bb336376b1b7b5e0646c907db697d97a208dfd824b6fbafee5676`, records its own successful notice-parity run and static package inspection at revision `56df1ee136a687d8cd3e2294ca25e38f9eab75dd`.

That historical result was not reattributed to this source revision. No packaged binary/model contents were acquired or inspected, and per-release redistribution clearance remains unestablished.

### Disposition

No fresh detector measurements, threshold recommendation, integrated resource qualification, packaged-host acceptance or redistribution clearance resulted from this run. The prior synthetic receipts remain the only measured detector evidence, and every acoustic/live gate remains not run.
