# RT1 experimental dependency admission

Reviewed 2026-10-05 UTC, synthetic research only; not production admission,
hosted-service entitlement or permission to redistribute experimental binaries.
The user approved exact-tag unmodified .NET SDK source-build acquisition after
released NuGet downloads failed TLS. This is **source-build conformance**, not
byte parity with the released NuGet artifact.

## SDK and managed dependencies

GitHub Copilot SDK release `v1.0.16`, source
`f8ae645902b74b62cd47aac1fd9b29adaec3aff2`, declares MIT in its root LICENSE
and .NET project. Source ZIP SHA-256:
`a73f4f2d8cb1eb9fd5353d3867707094e1efb7581599669afb55c2dcf48a53c3`.
Use original source/project, no private patches/reflection. Build a distinctly
named `1.0.16-rt1.source.f8ae645.1` package; do not impersonate NuGet's 1.0.16.
Retain original source notices in the ignored acquisition cache.

net10.0 runtime dependencies reviewed before restore:

- Microsoft.Extensions.AI.Abstractions 10.2.0: Microsoft/dotnet/extensions,
  source `7c82ecb60925d76e53b65e290a33bef46f285cae`, MIT, no net10 dependencies.
- Microsoft.Extensions.Logging.Abstractions 10.0.2 and
  Microsoft.Extensions.DependencyInjection.Abstractions 10.0.2:
  Microsoft/dotnet/dotnet, MIT; no additional net10 runtime closure.
- xUnit v3 4.0.1, AwesomeAssertions 9.6.0 and MTP TRX 2.4.1:
  existing repository-reviewed versions/licenses; their exact fixture closure
  scanned separately in [fixture closure](evidence/dependency-licenses.json):
  23 packages, all MIT except Apache-2.0 AwesomeAssertions.
- SourceLink/Common/Build.Tasks.Git 10.0.111 and ILLink.Tasks 10.0.12:
  Microsoft, MIT; [source-build closure](evidence/source-build-licenses.json)
  covers 7 packages, with no version substitution.

[packages.lock.json](packages.lock.json) records fixture closure/integrities;
[sdk-build.lock.json](evidence/sdk-build.lock.json) pins the original-source
build closure. Source ZIP is verified before every preparation. Build recipe
disables VCS/SourceLink discovery for the archive, supplies its reviewed commit,
normalizes source paths and ZIP timestamps only. Independent clean-source
[reproduction](evidence/source-reproduction.json) confirms identical package
and assembly bytes. Native runtime bytes are never modified.
The solution license gate excludes this directory; passing it cannot admit
these dependencies into production.

## Native runtime

Official GitHub Copilot CLI `v1.0.90` Windows x64 release archive:
`https://github.com/github/copilot-cli/releases/download/v1.0.90/github-copilot-1.0.90-win32-x64.tgz`.
SHA-256 `2b2c23816461f7616eb16c7836af7701d6683367029405d51054c4366d1d41c0`
matches GitHub's asset digest. Package build identity is `ccf052b4`;
the full proprietary runtime source is not published/reviewed.

The bundled `LICENSE.md` is **GitHub Copilot CLI License**, not SDK MIT.
It permits installation/use and conditional unmodified redistribution as part
of an independently licensed application providing material functionality.
Do not modify the runtime or infer hosted-account rights. Keep original
licenses/notices; no binaries are committed or distributed by this proof.

The full archive additionally contains OneAuthInterop (Microsoft Software
License Terms, including data-collection and redistribution restrictions),
CLI native payload, computer-use executables, ripgrep/tgrep, Foundry,
webview, voice/Picovoice and grammar assets. They are inventoried, not admitted
tools/features. Only the verified launcher/runtime payload is copied into the
active runtime directory. No CLI app bundle, optional native helper, auth
interop, plugins, webview, voice, ripgrep/tgrep or Foundry assets are copied
there. Redistribution/license completeness for the full CLI bundle is
**blocked**; RT2 must observe implicit runtime paths. A MIT SDK does not
relicense those assets.

Active native pins (same bytes as the historical Node witness):

- `copilot-runtime.exe` SHA-256
  `7021cf1f25eb6b75e64c05e8f805747c62dd420dc8760659660291809e92603a`.
- `runtime.node` SHA-256
  `41ebb48367f96c984babde61afb68f22a85ab8fd9c037fdccf1778881c4bba05`.

No installation, account login, service use, model download or paid inference
is authorized or performed by dependency admission.
