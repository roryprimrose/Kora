# R02 observed results and candidate recommendation

Original measurements are from 2026-10-05; a bounded 2026-10-09 rerun is
recorded separately below. **Incomplete feasibility evidence; keep D-003 and
reference-hardware inference acceptance open.**

The user selected harness validation and a draft PR, with real-model,
reference-hardware and offline evidence explicitly blocked. No provisioning or
network-policy change was approved or performed.

## 2026-10-09 unattended partial revalidation

The [redacted derived receipt](results/unattended-partial-2026-10-09/receipt.json)
records baseline `e0692f438a058de0a20021b3420a981849706dbf`, a clean initial
checkout, the exact three dirty source/documentation paths at rerun, source
hashes and SHA-256 references to the unchanged raw session artifacts.
Raw commands, machine inventory, failures and observations remain outside Git;
machine-specific checkout/output paths and assets are not committed.
This receipt does not replace or relabel any earlier observation.

| Check | Fresh result | Boundary |
|---|---|---|
| Initial Release `--no-restore` build | Exit 1, `NETSDK1004` missing `project.assets.json` | SDK 10.0.401 was already present; no installation |
| Conditional local restore | Exit 0, experiment `NuGet.Config`, zero feeds, audit disabled | Only after the missing-assets error; no package/tool acquisition |
| Metadata-only endpoint preflight | GET `/api/version`, curl exit 7, connection refused | No proxy, redirect, generation or lifecycle operation |
| Initial external-directory path regression | Failed: `Record-Machine.ps1` could not identify the repository; nested **55/55** self-tests had passed | Failed log and partial qualification receipt retained |
| Fixed external-directory path regression | **7/7 passed**, nested **55/55** self-tests, observation skipped | Source checkout identity no longer depends on the output directory being inside Git |
| Separate `Run-Validation.ps1 -ExpectUnavailable -NoRestore` | Release build: **0 warnings/errors**; **55/55** self-tests; expected observe exit **2** | Wrapper completed successfully; not a full application test suite |
| Source-linked endpoint observation | **Missing / Unavailable**, only `GET /api/version` and `GET /api/tags` | No POST, generation, fallback, model residency action or asset acquisition |

The narrowly coupled fix anchors both machine revision and dirty-status queries
to `$PSScriptRoot`. The added regression compares the machine revision with
the source-linked validation revision while the caller is in an external
evidence directory. Existing output-location and overwrite-refusal checks
still pass. No production source, source-link list, pin, policy or shared
configuration changed.

Observed failure-detection latency was **2,133.1817 ms** for readiness and
**2,081.8377 ms** for the answering workflow. These are refusal timings, not
inference speed. First-token timing is null; model resources are explicitly
**Not observed**, not zero-resource measurements. The deterministic suite's
read-only self-process observer control is not an Ollama observation.

No runtime/model was provisioned, installed, pulled, started, stopped,
unloaded or reloaded; no clipboard was read. No healthy unknown-owned endpoint
was used for generation. Public candidate metadata was not refreshed (the
metadata writer was invoked only to verify early overwrite refusal).

**Still open:** LI01-LI07, D-003/D-007 and A2; reference-floor CPU/context and
resource budgets, real cold/warm performance and human-scored answer quality,
independent offline/egress and server-cessation evidence, timeout/race recovery,
licence/storage qualification, and integrated UI/voice/tool-loop acceptance.
This rerun is publishable partial research under ordinary checks/reviews, not
capability acceptance or permission for a live trial.

## Historical evidence inventory

| Evidence | Result | Boundary |
|---|---|---|
| Initial git status/rebase | Clean; fetched/rebased `origin/main`; R01 `7d5e6a3` present | No pre-existing changes were removed |
| Source-linked build | Release build, zero warnings/errors, no package feeds | Standalone proof, not a product publish/full suite |
| Deterministic tests | **31/31 passed**; [self-test evidence](results/final-validation/self-test.json) | Mocks include a 60-trial orchestration; **not 60 real generations** |
| Real missing-endpoint probe | `Missing` | Initial probe completed in 2,074.824 ms |
| Real synthetic answering request | `Unavailable`, connection refused; no generation/fallback | Initial failure completed in 2,076.6578 ms |
| Actual request destinations | `GET /api/version`, `GET /api/tags`, both `127.0.0.1:11434` | Harness/probe/reasoner trace, not independent OS egress capture |
| Strict performance preflight | Blocked before generation at `/api/version` | No model/runtimes installed, started, replaced or pulled |
| Public pinned manifest | SHA-256 equals production digest | Exact manifest bytes hashed; weights not downloaded/hashed locally |
| Public model licence | Apache-2.0; licence blob digest verified | Not legal signoff for a packaged distribution |
| OS-blocked successful answering | **Missing** | No approved isolated network environment |

Initial unavailable observations are retained in
[unavailable.initial.json](results/unavailable.initial.json) and the blocked
measurement in [measurement-blocked.initial.json](results/measurement-blocked.initial.json).
The original pre-publication repetition is in
[observed-workflow.json](results/final-validation/observed-workflow.json).
Individual timings are failure-detection latencies, **not** inference speed.
No first token or completed answer was produced.

After rebasing onto the merged speech/deferred-validation (#25) and containment
(#21) work at `ca63f73`, the
[new validation receipt](results/post-rebase/validation.json) records another
zero-warning/error Release build and 31 passing deterministic tests.
All 46 original proof-input hashes still match; historical evidence is unchanged.
The [new endpoint observation](results/post-rebase/observed-workflow.json)
again reports `Missing`/`Unavailable`, with failure detection of 2,075.8832 ms /
2,099.4621 ms and only the same two loopback GET attempts. This adds no
real-model, reference-hardware or independent offline qualification.

When the storage proof (#20) subsequently landed, another approved rebase onto
`bcd4b81` preserved its S1-S4 admission checklist and downstream R04/R12 plan.
The [storage-aware validation receipt](results/storage-rebase/validation.json)
again records a zero-warning/error Release build, 31 passing deterministic
tests and the same 46 proof-input hashes. The
[new unavailable observation](results/storage-rebase/observed-workflow.json)
records `Missing`/`Unavailable` at 2,062.335 ms / 2,087.8585 ms, with only the
same two loopback GET attempts. Earlier snapshots remain unchanged; no storage
proof, production source or model/network trial was added by this rerun.

## Machine actually inspected

[Initial inventory](results/machine.initial.json) and
[post-rebase inventory](results/final-validation/machine.json):

- Windows 11 Enterprise x64, version/build `10.0.26300` / `26300`.
  Whether this particular release/build belongs to the supported release matrix
  has not been established.
- AMD Ryzen AI MAX 385 with Radeon 8050S: **8 physical / 16 logical cores**.
- Visible RAM: **25,530,408,960 bytes**, approximately **23.78 GiB**.
- NVMe SSD: **1,024,209,543,168 bytes**. Initial free storage:
  C: **351,822,553,088 bytes**; Q: **114,313,900,032 bytes**.
- High performance power scheme
  `8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c`.
- SDK `10.0.401`, runtime `.NET 10.0.12`, Python was not required by the harness.
- No executable at the expected per-user Ollama location; no runtime listening
  at the fixed endpoint. A machine-wide search was not performed.
- Kora UI/headset/voice were not exercised; other service contention,
  thermals/AC state and model accelerator use were not measured.

This is a development host with more RAM/logical CPUs than the minimum.
It is **not** proof on the exact **8 logical CPU / 16 GiB / SSD / supported
Windows 11 x64** floor. A faster host cannot establish that floor, and the
presence of integrated graphics cannot establish CPU-only execution.
The headset and speech matrix remain outside this inference branch's evidence.

## Exact candidate identity and acquisition requirements

Source: [recorded public metadata](results/candidate-metadata.json), retrieved
2026-10-05; collection commands are in the [README](README.md).

| Item | Value / provenance |
|---|---|
| Runtime candidate | Ollama `0.35.1`, per-user Windows setup |
| Runtime published setup SHA-256 | `2544c6dc60c57866f5cfbd32b8f7c5ffa5e1f7f0579ca59da5ff20bdc53ad3d2` |
| Runtime setup download | **1,580,352,416 bytes** (published release-asset size) |
| Runtime licence | Tagged source **MIT**; exact source licence file SHA-256 retained |
| Installed runtime executable digest | **Not available**; setup asset not downloaded/hashed locally |
| Model | `qwen3:1.7b` |
| Model manifest SHA-256 | `8f68893c685c3ddff2aa3fffce2aa60a30bb2da65ca488b61fff134a4d1730e7` |
| Model weights blob SHA-256 | `3d0b790534fe4b79525fc3692950408dca41171676ed7e21db57af5c65ef6ab6` |
| Model blob download total | **1,359,293,444 bytes** (weights + config/template/licence/parameters) |
| Model blob + manifest storage | **1,359,294,303 bytes** lower bound; excludes filesystem overhead/temporary/cache files |
| Model licence | **Apache-2.0**, manifest-linked 11,338-byte licence blob hashed and checked |
| Fresh setup + model transfer | **2,939,645,860 bytes**, excluding small protocol metadata/retries and any additional setup-time assets |
| Model free-space setup guard | **2,000,000,000 bytes**, existing production guard on the model volume; not a sufficient total-install storage budget |
| Expanded runtime/peak install storage | **Not measured**; may differ substantially from compressed setup size |
| Advertised model context | **32,768 tokens**, upstream model card; not verified as usable with this adapter/runtime/floor |
| Production request/output bounds | **4,096 request characters**, 512 predicted tokens, 64 KiB buffered generation response; system text consumes additional context |

Public sources:
[tagged runtime licence](https://github.com/ollama/ollama/blob/v0.35.1/LICENSE),
[runtime release](https://github.com/ollama/ollama/releases/tag/v0.35.1),
[model manifest](https://registry.ollama.ai/v2/library/qwen3/manifests/1.7b),
[upstream model card](https://huggingface.co/Qwen/Qwen3-1.7B/blob/main/README.md),
[winget package definition](https://github.com/microsoft/winget-pkgs/tree/master/manifests/o/Ollama/Ollama/0.35.1).
Changing public tags are checked against the production digest rather than
trusted blindly. Redistribution needs actual packaged/native-dependency
licence/notice review; MIT/Apache labels alone do not complete that review.

## Measurements deliberately not fabricated

| Required evidence | Status |
|---|---|
| Cold/warm first-token and answer completion, p50/p95/max | **Blocked: runtime/model absent** |
| Real per-fixture semantic quality and critical safety | **Blocked: no real outputs** |
| Loaded model/runner RAM and CPU-only resource contention | **Blocked**; zero sampled model resources during refusal are not low resource usage |
| Effective context/token limits, truncation and marker retention | **Blocked**; advertised size is not observed capacity |
| Real cancellation latency/server cessation/recovery/races | **Blocked**; deterministic client cancellation is supplementary |
| Real unhealthy runtime/missing model/timeout failure fixtures | **Blocked** except actual connection refusal |
| No-egress successful answering, recovery and unavailable trials | **Blocked: no approved OS/hypervisor isolation/capture** |
| Supported Windows and CPU/RAM reference-floor qualification | **Missing** |
| Installed executable/weight hashes, expanded/peak storage, bundled notices | **Missing** |
| UI-open, normal-service contention and speech concurrency | **Missing**, relevant to later integrated acceptance |

The [harness procedure](README.md) exposes each of these gaps and provides
synthetic fixtures and measured-output fields for the next consented trial.
The final validation manifest records revision plus source hashes because a
run's base commit cannot itself contain its newly generated results.

## Recommendation and reconsideration triggers

The [canonical local-inference technical plan](../../Design/Local_Inference.md)
maps these observations to compatibility, context, resource, setup and offline
requirements. The [R02-L1-L6 continuation](../../Design/Implementation_Roadmap.md#r02-local-inference-continuation)
assigns owners/dependencies and starts with environment/budget approval, then
consented provisioning and actual qualification trials before integration.
These documentation outcomes do not change the measurements recorded above.

**Retain Ollama 0.35.1 / the existing digest-pinned qwen3:1.7b as the first
candidate to benchmark; no selected/qualified production recommendation yet.**
Its exact registry identity and model licence are reproducible, and its
approximately 1.36 GB model download is consistent with the bootstrap estimate.
The installer additionally needs approximately 1.58 GB transfer; the current
2 GB model-volume guard must not be described as total provisioning needs.

Do not raise/lower the reference floor, change pins, close D-003/D-007 or claim
local-first A2 based on mocks, licence metadata or connection refusal.
No alternative candidate was installed or benchmarked. If the consented
floor run fails quality, context, cancellation or an agreed UX/resource budget,
request approval for a scoped candidate comparison or a product/hardware
decision rather than silently choosing cloud inference.

This partial-evidence package may merge after its runnable checks,
documentation/hygiene and normal required checks/reviews pass. Later
interactive validation is tracked by
[LI01-LI07](README.md#deferred-inference-trials) and the
[shared deferred-validation register](../../Design/Deferred_Validation.md).
Real hardware, human-quality, distribution and independently network-blocked
results remain qualification/exposure gates, not prerequisites for publishing
the harness and truthful handoff. Historical draft/blocked receipts are not a
current merge restriction. Publication does not close D-003/D-007 or A2 and must
not bypass checks/reviews, change pins or turn an unrun trial into a pass.
