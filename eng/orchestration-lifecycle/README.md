# Orchestration lifecycle acceptance

## Contract and execution boundary

The [acceptance module](../../.github/skills/kora-orchestrate/scripts/lifecycle-acceptance.test.cjs) preserves legacy preparation cases and exercises public lifecycle contract versions 1.0.0 through 1.1.1. The [fixture](../../.github/skills/kora-orchestrate/scripts/lifecycle-acceptance-fixture.cjs) uses exported APIs, not private fixtures or a second lifecycle engine.

Consumed contract SHA256 values:

- Version 1.0.0: `10770fb587cae3483c1df5b9ab1a7094e537b400b1aa2c673afa1a5756065294`.
- Additive version 1.0.1: `cae49c58ffde2b1d1545c021b063d0c3cf0f61e342a2692aa880b8429e8ac4ac`.
- Additive version 1.0.2: `72441abeffdb258340d0d0735e168a9ce1f450522699fbd5461b0d3e3a8be219`.
- Source-backed recipe companion 1.0.2-recipes.1: `bbc60e6dae4aefbd900cacfb558f87507b22a8ae01cacb79ec2fb37c483a859e`.
- Correction version 1.1.0: `21c4c0a0ba92651af1567d53291bdbdbf8a0d90926000af611348ff69d21943c`.
- Additive version 1.1.1: `c29226298b968c3f61f6cb07c491cae6a7225b020681d7465c1a96b7e041896d`.
- Supplemental public recipe companion 1.1.0-recipes.1: `007c55a96d1f38b230e72880922d8acad724039c687df1ffce504c5b2c89dec4`.

Contracts describe APIs, not implementation availability. The merged core `c561c1480563c34ebf8687fa6dd38c1a23d2dea8` and actual correction `4eeea00526da2acc964e3f9560dee563a8e1e8b5` were incorporated through separately authorized fast-forwards. All authoring and existing dataset bytes were preserved each time. Missing APIs fail loudly.

Version 1.1.0 tightens malformed-v10 preservation and adds explicit `release-unexecuted`; it does not preserve acceptance of previously malformed evidence. Legacy1-9 meaning remains unchanged. Version 1.1.1 clarifies persisted event/host types without further semantic change. Final results below use the actual corrected implementation.

The [registry](../../.github/skills/kora-orchestrate/scripts/registry.cjs), [checkpoint](../../.github/skills/kora-orchestrate/scripts/checkpoint.cjs) and [launch generator](../../.github/skills/kora-orchestrate/scripts/worker-launch.cjs) remain authoritative. Synthetic claims are not human consent, real publication, source permission or OS enforcement.

## Scenario contract

The following obligations define acceptance. Positive transitions consume the actual published public recipes. Independent regressions require source/publication/ref preservation, rejection of new unadmitted manifest paths and explicit local null-grant future-custody release. They do not weaken assertions to match earlier implementation defects.

| Scenario | Required observable result |
| --- | --- |
| A to B to C | Each fresh bounded grant permits synthetic work on the same files; A and B logically close after delivery with their worktrees and bytes retained. Their old grants reject before a later source operation. |
| Partial custody | Transfer only the exact admitted paths, keep unrelated holds, and explicitly dispose of retired holders' remaining paths under fresh authority. |
| Multiple holders | One successor consumes complete current holder proofs; historical participation never becomes a new write grant. |
| Closure and capacity | Publication alone does not close work; valid joined authority retirement and preservation free logical capacity without deletion flags. |
| Same-PR repair | A new bounded execution identity links to the original PR without reviving a retired grant or creating another PR. |
| Revisions and source | Relevant grant, holder, scope, source or authority changes invalidate stale proofs; unrelated bookkeeping preserves otherwise current proof. |
| Cancellation and abandonment | Incomplete preservation or unknown quiescence retains exclusions; no implicit reclamation or automatic expiry. |
| Ambiguous launch and restart | Durable history and the current projection agree after reopen; ambiguous launch does not authorize duplicate execution. |
| Atomic persistence | Stale revisions, lock contention, write/replace failures and malformed authority leave old bytes, history, projection and actions unchanged. |
| Legacy versions 1 to 9 | Preview/import/adoption is opt-in; unresolved exclusions remain held; accepted publication, supersession, recovery, renewal, amendment, retirement and cleanup histories survive unchanged. |
| Missing authority | Missing preservation, unknown pause/join or changed exact human/holder evidence fails closed without launch or source effects. |

The fixture binds human and holder proof digests with `lifecycleProofDigest`, derives expected revisions with `lifecycleRevisions`, then binds synthetic host input to `digest(finalSignedEvent)`. Call sites name the complete sorted holder set. Synthetic flags are explicit structural inputs, not authentication.

Legacy1-9 fixtures are independently constructed through actual `createState`/`applyEvent` operations, including refinement, extension, readmission, supersession, incident recovery, renewal and fenced transactions. No version/history field is manually upgraded. Adoption uses the documented read-only v10 shadow and preserves the exact original legacy state once.

Each later holder proof uses fresh per-grant fence epochs, nonces and acknowledgments. Legitimate unwritten retained files need not equal actual or planned scope lists. Only newly unadmitted actual/planned paths and omitted declared durable source/publication/ref anchors are tested as preservation defects.

Version 1.1.0 leaves ordinary null-grant `release` forbidden. Explicit `release-unexecuted` binds the exact legacy origin, sorted target reservation IDs in host verification, and fresh actual predecessor grants derived from accepted public supersession holders and the public projection.
Missing/changed origin binding, missing/wrong target verification or missing predecessor preservation must refuse without mutation or file effects.

The new positive checks exact affected predecessor work/grant/custody revisions, no actions/source execution, no grant revival or creation, unchanged work/capacity/origin and retained unrelated custody. It does not invent predecessor IDs, imply external/unresolved release authority or require a global owner epoch.

## Scratch and preservation

The [fixture allocator](../../.github/skills/kora-orchestrate/scripts/lifecycle-acceptance-fixture.cjs) creates unique `acceptance-*` directories below a Git-verified ignored `.scratch` root, never system temporary storage or a live checkpoint. Set `KORA_LIFECYCLE_ACCEPTANCE_SCRATCH` to an approved root; otherwise repository-local ignored scratch is used.

Each dataset path is printed. Checkpoints and synthetic retained files are preserved after execution. Fault injection targets only the exported checkpoint's IO seam. The contention test removes only its own simulated lock after closing its handle; no worktree, branch or source dataset is deleted.

Launch-generator inputs use synthetic canonical Windows paths independently of the host-native dataset paths. These inputs are not filesystem operations; Linux policy runners retain their real checkpoints and source fixtures under repository-local ignored scratch without weakening the launch generator's Windows path contract.

Lifecycle source identities also use canonical synthetic Windows paths. The
fixture maps only identities beneath the current dataset's synthetic root to
native files inside that dataset, rejecting other roots, drives and escapes.
Grant validation still consumes the unchanged Windows identity before writes.

The synthetic source driver calls `validateSourceGrant` before creating or writing files and asserts its exact `valid: true, executionAuthorized: false` result. This harness-owned synthetic operation is not live execution authority or a filesystem-wide enforcement mechanism.

## Validation

Executed preparation command at base `09d5aaeb876bf216afed5bafe1715fbabbcc89e0` with these three preparation files present:

```powershell
node --test .\.github\skills\kora-orchestrate\scripts\lifecycle-acceptance.test.cjs
```

Historical result before version-10 authoring: exit 0; 10 tests passed, 0 failed, 0 skipped, 0 todo. Those cases check legacy replay/launch, retained bytes through checkpoint reopen, five atomic refusal/failure cases and automatic discovery. This earlier result is not evidence that the currently expanded module passes.

Executed authoring checks on 2026-11-22:

```powershell
node --check .\.github\skills\kora-orchestrate\scripts\lifecycle-acceptance-fixture.cjs
node --check .\.github\skills\kora-orchestrate\scripts\lifecycle-acceptance.test.cjs
```

Both syntax checks exited 0 during authoring. Final syntax, local documentation links, Markdown line limit, whitespace and exact three-file source footprint are checked separately from behavioral evidence.

Executed against merged core `c561c1480563c34ebf8687fa6dd38c1a23d2dea8` on 2026-11-22, before the later version-1.1.0 authoring changes:

```powershell
node --test .\.github\skills\kora-orchestrate\scripts\registry.test.cjs .\.github\skills\kora-orchestrate\scripts\checkpoint.test.cjs .\.github\skills\kora-orchestrate\scripts\worker-launch.test.cjs .\.github\skills\kora-orchestrate\scripts\lifecycle-acceptance.test.cjs
node --test .\eng\tests\policy-test-selection.test.cjs
node .\eng\run-policy-tests.cjs
```

| Command | Actual result |
| --- | --- |
| Acceptance module command above | Exit 1; 50 tests, 40 passed, 10 failed, 0 skipped/todo. |
| Three core modules plus acceptance | Exit 1; 194 tests, 184 passed, 10 failed, 0 skipped/todo. No core-module test failed. |
| Policy-selection module | Exit 0; 27 tests passed, 0 failed/skipped/todo. |
| Actual policy runner | Exit 1; 14 selected modules, 642 tests, 632 passed, 10 failed, 0 skipped/todo. |

The 10 reported failures are eight failing leaves plus two failed parent groups: arbitrary or missing source/publication anchors, invalid refs, later lost declared refs, new unadmitted planned/actual paths, and rejected explicit local null-grant future-custody release. Every other scenario, including all nine legacy construction/adoption branches and actual checkpoint IO failure injection, passed.

All seven preservation-refusal inputs/assertions remain intact. The original eighth failed ordinary-release input/log remains retained; its source test asserts refusal under the corrected explicit-command contract. Additional explicit unexecuted-release cases now execute against the actual correction.

Initial fixture mistakes in duplicate outcome text, readmission scope retention and recovery source-refresh ordering were corrected without changing the required boundary assertions. Full state/event/context reproducers and console output are retained in the assigned ignored worker scratch. Required regressions remain ordinary tests, not skips, expected-failure flags or weakened checks.

### Final corrected implementation

Executed all four commands above against correction `4eeea00526da2acc964e3f9560dee563a8e1e8b5` with the final acceptance source on 2026-11-22:

| Command | Actual result |
| --- | --- |
| Acceptance module | Exit 0; 56 tests passed, 0 failed/skipped/todo. |
| Three core modules plus acceptance | Exit 0; 203 tests passed, 0 failed/skipped/todo. |
| Policy-selection module | Exit 0; 27 tests passed, 0 failed/skipped/todo. |
| Actual policy runner | Exit 0; 14 selected modules, 651 tests passed, 0 failed/skipped/todo. |

The first corrected acceptance attempt reported 56 tests, 45 passed and 11 failed, including failed parent groups. Fixture inputs had invalid refs containing `:` from imported grant IDs and omitted compatible source-head anchors.
Fixtures now produce canonical refs, retain prior exact head/ref pairs and include declared compatible and accepted legacy source/preservation heads. Assertions were not weakened.

The [existing policy runner](../run-policy-tests.cjs) selects acceptance exactly once without selector or workflow edits. All legacy1-9 branches, explicit unexecuted disposition and authority negatives, retained-worktree scenarios and actual checkpoint IO failures pass.
No provider/hardware trial or .NET build was run for this dependency-free engineering change; no live adoption, release, cleanup, consent or host/OS authority is established.

### Portable launch-fixture repair

PR #954's Ubuntu queue-policy run reported 635 passing and 10 failing tests. All
10 failures passed host-native POSIX dataset paths into the Windows-only launch
generator. Launch inputs now use independent synthetic Windows paths; actual
checkpoint and retained-source operations continue using host-native paths.

Executed locally on Windows on 2026-11-22 after this repair:

| Exact command | Actual result |
| --- | --- |
| `node --test .\.github\skills\kora-orchestrate\scripts\lifecycle-acceptance.test.cjs` | Exit 0; 56 passed, 0 failed/skipped. |
| `node .\eng\run-policy-tests.cjs` | Exit 0; 14 modules, 651 passed, 0 failed/skipped. |
| `.\eng\Test-WindowsIntegrationScheduling.ps1` | Passed; 2258 assertions. |
| `.\eng\Test-CiPreflight.ps1` | Passed; 667 assertions. |

Ubuntu execution and hosted CI after this repair were not run. The production
Windows path validator and all lifecycle boundary assertions remain unchanged.

The next Ubuntu run on `157a418b6` passed launch generation but failed the same
10 scenarios at the lifecycle source validator. Source identities are now
separate from native filesystem paths as well. Local Windows validation on
2026-11-22 after this second repair:

| Exact command | Actual result |
| --- | --- |
| `node .\eng\run-policy-tests.cjs` | Exit 0; 14 modules, 652 passed, 0 failed/skipped. |
| `.\eng\Test-WindowsIntegrationScheduling.ps1` | Passed; 2258 assertions. |
| `.\eng\Test-CiPreflight.ps1` | Passed; 667 assertions. |

This adds one dataset-mapping confinement regression. The production lifecycle
source validator remains unchanged; local Windows results do not establish an
Ubuntu CI pass.
