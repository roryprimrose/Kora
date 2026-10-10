# Measured W2 evidence

The [fixture commands and owner decision](../README.md) define the scope.
Measured source baseline is `3e8558fbff07943d39721b230599b02062d90d57`,
branch `agents/kora-w2-executable-dependency-proof`; fixture/docs remain
uncommitted by request. No production code or sibling checkout was changed.

## Disposition

**Rejected:** AppContainer scratch ACL projection and child-process mitigation
as exact executable/module admission mechanisms. **Feasible, not admitted:**
separate embedded helper/entry blocks with the fixed typed owned marker.
**Blocked:** native synthetic loading, complete production/fixed Windows
control/installed admission. No actual lock/shutdown/restart was run.

The owner subsequently accepted best-effort transitive dependency tracking for
bundled and future scripts, user responsibility for script actions and all
manifest-listed internal files in read-only review tabs. Exact declared bytes
and known-change revocation remain mandatory. This removes universal dynamic
dependency denial as the default grant condition; it does not convert rejected
strict mechanisms into successful enforcement or waive independent containment.

## New bounded preparation (2026-10-09)

The separate [file-only preparation receipt](preparation-20261009/README.md)
records the current revision/dirty-source distinction, deterministic full-suite
results and both managed payload builds. It does not rerun or supersede the
original live measurements below. **PreparedOnly** is not W2 admission or
W1/W3/W4 qualification; native loading and protected installed checks remain
blocked and unperformed.

## Final safe run

[measured](measured/) is exported from `artifacts\final-03`: **62/62 diagnostic
checks, zero harness errors, exit 2 (rejected/blocked), not OS-profile pass**.
All eight trial trees stopped, with explicit scratch/profile cleanup and no
automatic replay. Environment and source records carry exact UTC times,
Windows build 26300 x64, .NET 10.0.12, SDK 10.0.401, PowerShell 7.6.6, actual
synthetic token identities, source/runtime/native image hashes and complete
declared versus attack-fixture inventories.

| Trial | Observed result |
|---|---|
| Uncontained binary control | All synthetic path/byte/child operations work; eight payload child/grandchild receipts |
| AppContainer ACL | Native error 5 for inaccessible targets and explicit data-file execute denial; readable data assembly bytes execute value 22; four declared/undeclared RX child/grandchild receipts |
| AppContainer no-child | Queried flag 1; native error 367 for declared/undeclared allowed-path children; undeclared managed byte loading still succeeds |
| Uncontained script control | Every fixed script/module/import/autoload/read/managed-byte operation works; actual marker 102 |
| AppContainer complete script | Separate embedded helper/entry writes marker 102; writable script/import/autoload/managed bytes execute; inaccessible source read denies with native error 5; command-not-found remains Unknown |
| Receipt lost | Exit 0 and marker 102, no receipt; effect Unknown |
| Receipt malformed | Exit 0 and marker 102, malformed receipt rejected; effect Unknown |
| Cancel after effect | Cancelled lifecycle, marker 102, no effect receipt; effect Unknown; exact job tree terminated |

Job membership also includes owned Windows `conhost.exe` helpers: 16 tracked
PIDs in the binary baseline (eight payloads plus eight console hosts), eight
in the AppContainer ACL trial, and two in cancellation. Actual image/start
identities are retained, and only job-owned handles were closed/terminated.
These implicit OS helpers are further reason not to describe a declared
apphost list as complete executable admission.

The completed worker/interpreter trials retain image inventories, not just
expected executable names. Identity/hash read failures are explicit; lost/
malformed/cancelled modes do not fabricate a completed image inventory.
Inheritable-host-handle probes under AppContainer raise strict-handle SEH and
remain Unknown; inspected no-inheritance flags are not an object-identity proof.

## Exact inputs and receipts

- [source.json](measured/source.json): per-file source SHA-256, baseline, dirty
  source distinction, SDK and imported OS-native file version/hashes.
- [inputs.json](measured/inputs.json): exact embedded bytes, framed script/
  definition/admission digests and full runtime/declared/attack inventories.
- [acl-inputs.json](measured/acl-inputs.json): actual per-file deny-execute SDDL.
- Trial JSON files: actual process/token identities, native errors/exception
  classification, typed values, owned process trees and stable-handle shutdown.
- [verification.json](measured/verification.json): diagnostic counters and
  independent failed/blocked admission gates, default grant-policy distinction.
- [cleanup.json](measured/cleanup.json): exact owned profile/tree deletion.
- [export.json](measured/export.json): disclosed redactions and exported hashes.

Only the real host SID and owned scratch absolute prefix are redacted in the
published snapshot; synthetic container identities and all byte hashes are
preserved. Raw local evidence remains available, not rewritten or deleted.
SHA-256 pins are local build/review identities, not installed protection,
publisher authentication, native redistribution clearance or universal loader
enforcement.

Failed development runs are retained separately in ignored artifacts:
initial invalid-handle probes prevented worker receipts; a parent-inheritance
only projection did not enforce execute denial on existing copied files; a
PowerShell wrapper initially obscured an inner access-denied exception.
Explicit file ACL stamping, truthful handle Unknown and typed inner-exception
classification corrected the harness. None of those attempts is reported as
passed enforcement.

## Repository and prerequisite validation

- Fixture xUnit/AwesomeAssertions tests: **27 passed**, zero failed/skipped.
- Full root Release solution build including `Kora.Setup`: **0 warnings/errors**.
- Core **251**, Application **744**, Windows **353** tests passed; no failures/
  skips. Latest-only `w2-full-01` Core/Application coverage: **100% lines and
  branches**, using only that run's reports.
- Locked restores, dependency license/notice gate, version policy, simulated
  publication policy, payload rejection tests and x64/x86 publish/hash checks
  passed. Publication tests use synthetic callbacks; no release was published.
- Existing x64 PE/import/resource/native asset inspector ran without launch.
  Its raw-nuspec inspection warns for OpenTK Audio/Core/Mathematics prerelease
  metadata; the separate root license/notice policy gate passed. Static package
  metadata is not legal clearance or native/installed acceptance.
- [x86 static native inspection](measured/x86-static-native.json) examined
  69 PE images with zero native architecture mismatches. No x86 runtime was
  launched and this does not add an offered x86 installer target.
- Non-skipped WiX MSI ICE validation attempted: **WIX1105/system-policy
  blocker**. No elevation, policy change or skipped-ICE substitution. Burn
  completion/inspection downstream of ICE was not reached.
- Native C fixture source and loader/build prerequisites exist; native loading
  **Not run/Blocked** because the existing compiler prerequisite was unsatisfied.
  No tool/runtime/dependency installation or acquisition was attempted for it.
- W1 network, W3 protected deployment, actual Windows computer-control and W4
  production lifecycle/host-death integration were **Not run**, not passed.

## Supported mechanisms considered

Documentation inventory informed the bounded trials, but is not enforcement:

- [Process creation attributes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute):
  security capabilities, child-process restrictions and mitigation attributes.
  The fixture separately queries its applied child policy and native results.
- [Binary signature policy](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-process_mitigation_binary_signature_policy):
  signer classes, not an arbitrary exact per-task hash allowlist. Not trialled.
- [AppLocker DLL rules](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/applocker/dll-rules-in-applocker):
  administrative application-control rule collections, including system DLL
  closure. No global policy was configured or treated as non-elevated isolation.
- [PowerShell language modes](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_language_modes?view=powershell-7.6):
  manual language-mode filtering is not a security boundary; system application
  control brings additional restrictions. No system policy or JEA deployment
  was created. FullLanguage observations are retained as measured.

If a future explicitly strict profile needs administrative application control,
prepare a reviewed disposable lab and obtain specific approval first. The
best-effort grant decision does not approve that setup or silently select a
broker, script extraction or ambient execution.
