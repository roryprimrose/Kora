# MG1 experimental license/provenance closure

Reviewed 2026-10-06 UTC for synthetic research, not production admission,
hosted-account entitlement or redistribution approval.

The user explicitly approved moving this independent fixture to released
GitHub.Copilot.SDK 1.0.16 after the historical source-build reproduction
blocker and successful package acquisition. The package and assembly hashes
in [input verification](evidence/input-verification.json) identify the
released artifact; informational version identifies source
`f8ae645902b74b62cd47aac1fd9b29adaec3aff2`.
The released package declares MIT. It is not impersonated by a locally
versioned build, and no byte equivalence with RT1's source-built package is
claimed.

[Management closure](evidence/dependency-licenses.json) reviews all 23
packages; [host-only closure](evidence/host-dependency-licenses.json) reviews
19 packages. Management has 13 MIT and 10 Apache-2.0 packages; host-only has
9 MIT and 10 Apache-2.0 packages. AwesomeAssertions 9.6.0 and the nine xUnit
packages declare Apache-2.0; SDK/Microsoft/System packages declare MIT.
The xUnit SPDX expression was checked directly in the restored 4.0.1
package nuspec, not inferred from historical metadata.
After explicit approval to update compatible non-SDK dependencies, the
fixture now pins Microsoft.Extensions.AI.Abstractions 10.10.1 and
Microsoft.Extensions.Logging/DependencyInjection.Abstractions 10.0.12.
Microsoft.Testing.Extensions.Telemetry and Microsoft.Testing.Platform.MSBuild
move together to 2.4.1. All retain MIT terms. The central experimental
versions also govern the MG1-owned RT1 derivative; all 45 controls were
repeated before the 22 host and 16 runtime cases on this refreshed closure.
Loaded runtime dependency informational versions and assembly hashes are
recorded in [actual runtime evidence](evidence/runtime-results.json).
Historical RT1 dependencies and approved SDK/native bytes are unchanged.
Separate registry-independent lock files pin actual package content hashes.
Test platform telemetry is explicitly opted out.

The native runtime archive/launcher/payload are unchanged RT1 1.0.90 bytes
and remain subject to the **GitHub Copilot CLI License, not MIT**.
The [original RT1 native review](../r02-dotnet-control-proof/LICENSE-REVIEW.md)
continues to own those terms and the excluded optional asset inventory.
Only launcher/payload/original license are activated; no optional auth,
voice, computer-use, search, Foundry or WebView helpers are admitted.
No native package is committed, included in production notices or published
application/installer payloads.

The root solution license gate excludes experiments. Its passing notice
report cannot admit this closure, make a hosted-service terms claim, or
replace RT2/native/runtime/account/deployment review. Source/archive
acquisition, machine-local package routing and credential-provider state
are not a Kora repository configuration contract.
