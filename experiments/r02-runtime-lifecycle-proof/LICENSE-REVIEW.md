# RT2 experimental dependency review

Reviewed 2026-10-06 UTC for synthetic research only. No new dependency version,
production admission, hosted entitlement or binary redistribution is approved.

The [RT2 lock](packages.lock.json) has the identical 23-package closure and
content integrities as the [RT1 lock](../r02-dotnet-control-proof/packages.lock.json).
The runner verifies that identity. A fresh
[scan](evidence/dependency-licenses.json) records **13 MIT and 10 Apache-2.0**
packages (AwesomeAssertions and the nine xUnit packages are Apache-2.0).
This agrees with RT1's machine-readable scan; its narrative shorthand
"all MIT except AwesomeAssertions" is not the authoritative license inventory.
Historical RT1 source/evidence is left intact.

The original MIT SDK source and unmodified project/build closure are reviewed
under [RT1's source/native review](../r02-dotnet-control-proof/LICENSE-REVIEW.md)
and [source-build scan](../r02-dotnet-control-proof/evidence/source-build-licenses.json).
No SDK patch, package-version substitution, private reflection or Node bridge
is used. The source/archive and actual package/assembly hashes are verified;
timestamps alone are normalized by the unchanged RT1 package recipe.
Released NuGet byte parity is still Blocked.

Native runtime 1.0.90 is governed by the **GitHub Copilot CLI License**, not SDK
MIT. Only the approved launcher, payload and original license are active.
No optional CLI bundle/helper assets are admitted or distributed. Windows
PowerShell/console-host and Windows DLL loading are observed OS dependencies,
not new bundled assets or proof of an exact executable dependency allowlist.
Their image hashes are recorded in each trial; platform licensing and full
optional bundle redistribution remain separate admission work.

Instrumentation uses installed .NET/Windows APIs, not a new tracing package.
The root production license/notice gate passes separately. Both root publish
payloads exclude the experimental SDK/runtime/fixture. Do not add these
experimental packages or native bytes to the production solution/notices as a
consequence of passing fixture tests.
