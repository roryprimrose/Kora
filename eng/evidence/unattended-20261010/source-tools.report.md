# Maintained source-tool archive/acquisition proof - 2026-10-10

## Result and source identity

The complete maintained [source-tool contract suite](../../Test-SourceToolsContracts.ps1)
passed **123 assertions**, exit code **0**, from a verified clean, distinct worktree
at `56df1ee136a687d8cd3e2294ca25e38f9eab75dd`. The run used PowerShell 7.6.6
(`.NET 10.0.12`) on Windows and a fresh external output directory. No dependency
restore or installation was needed. The checkout root was not used for execution.

No concrete archive/acquisition failure was found; no contract, admission rule,
helper, declared inventory, historical evidence or shared design document was changed.
The new [sanitized receipt](source-tools.receipt.json) records the tested revision,
tool blob IDs, byte lengths, SHA-256 values and raw-artifact digests. It deliberately
does not relabel these baseline bytes as bytes from the later evidence commit.

## Reproduction and retained artifacts

Run from a maintained checkout at the tested revision, with `$ProofRoot` set to a
dedicated external artifact directory:

```powershell
pwsh -NoProfile -NonInteractive -File .\eng\Test-SourceToolsContracts.ps1 `
    -OutputDirectory "$ProofRoot\baseline-20261010-0745"
```

The output child must not already exist. The original full assertion receipt,
log, valid/retry archives, acquisition receipts and negative fixtures remain
external under the parent-assigned raw artifact root. Only this report and its
sanitized receipt are committed; no ZIP, acquired tools, user paths or credentials
are included in the change.

The produced archive is 16,838 bytes with SHA-256
`a95fed344b02e24cd83e16a59933aa7b6690b52dbe62c5b1fb1bae4325da6678`.
The independently constructed retry archive has the identical SHA-256.

## Executed contract coverage

- Canonical HTTPS origin admission and fork/alias refusal; complete eight-tool
  dependency closure, exact Git blobs and digest identity; deterministic retry
  ZIPs and no archive overwrite.
- **26 hostile archive variants**, each rejected before extraction and each
  leaving no extraction root: missing/invalid/changed manifests and fields,
  missing/duplicate tool records, hostile repository and false assurance,
  wrong types, traversal, absolute paths, separator/case/ADS/dot aliases,
  omitted/changed/self-described forged tools, symlink/reparse/directory entries,
  empty/oversized entries, duplicate entries and extra tools.
- Traversal containment and refusal above the exact **16 MiB archive bound**.
  The oversized-entry fixture is **2 MiB + 1 byte**.
- Production/preview channel admission, draft exclusion, numeric beta ordering,
  stable precedence, explicit preview opt-in, exact immutable revision, ambiguity,
  missing/invalid assets, typed release/asset IDs and final-byte sizes.
- Release-record replacement, moved tags, truncated/substituted canonical trees
  and tool omissions are refused; unrelated counters do not change identity.
- Acquisition verifies the fake immutable asset ID and final bytes without
  executing acquired code. A verified rerun neither downloads again nor replaces
  original receipts. Changed tool/manifest bytes (including whitespace-only
  manifest changes), added executables, changed asset identity, hostile downloads,
  incomplete/unowned output, extraction overwrite and junction output are refused.
- The local distributed-bundle pre-build guard admits the complete reviewed
  fixture but rejects absent manifests, different revisions, changed digests and
  incomplete inventories. The guard runs; compilation does not.

## Inventory and qualification bounds

The existing authoritative [inventory](../../Distribution.Common.ps1) remains
exactly these eight tools, in declared order:

1. [Invoke-SourceBootstrap.ps1](../../Invoke-SourceBootstrap.ps1)
2. [SourceBootstrap.Common.ps1](../../SourceBootstrap.Common.ps1)
3. [Test-SourceStage.ps1](../../Test-SourceStage.ps1)
4. [Get-BuildVersion.ps1](../../Get-BuildVersion.ps1)
5. [SourceCheckout.Common.ps1](../../SourceCheckout.Common.ps1)
6. [Distribution.Common.ps1](../../Distribution.Common.ps1)
7. [NativeInspection.Common.ps1](../../NativeInspection.Common.ps1)
8. [Inspect-Publish.ps1](../../Inspect-Publish.ps1)

The ZIP has those eight tool entries plus `source-tools.json`, not a ninth tool.
Bootstrap format `1.1.0` and release fixture `0.1.0-beta9` retain the original
claim: **exact canonical Git blobs; not an independent signature**.
`unsigned=true`, `productionAccepted=false`, and `activation=unavailable`.

All acquisition-related GitHub operations use the suite's maintained function
fakes: `Get-CanonicalReleases`, `Get-GitHubRecord`, `Invoke-Gh`, and the asset
`Read-SourceCommandBytes`. Local Git blob reads occur before the asset-download
fake replaces that function. No real acquisition, release publication or account
access is qualified by this run; PR delivery is separate from these tests.

This is an unattended **static archive/acquisition contract proof**, not live
release qualification, publisher authentication, source-build trust, installer
or application execution, protected deployment, installed-gate qualification
or production acceptance. Acquired scripts were not executed. No GUI, device,
physical audio, elevation, OS policy, new consent, credentials, provisioning,
license acceptance or global dependency installation was required or attempted.
