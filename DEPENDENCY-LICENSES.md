# Dependency license policy

Kora's own source is licensed under PolyForm Shield 1.0.0. Third-party
dependencies remain under their respective licenses.

## Automated NuGet control

The Claude adapter pins the official Apache-2.0 `Anthropic`
13.6.0 package and MIT `Microsoft.Extensions.AI.Abstractions` 10.11.0. The
Anthropic package records upstream source commit
`a4c244aca0a692e4205b4ed4fefec9e021a6d8cc` in its NuGet metadata.
These library licenses do not grant provider/account-use approval or enable
Claude. SDK retries and content telemetry are disabled by the adapter.

Ollama messages use community-maintained, MIT-licensed
`OllamaSharp` 5.5.2, not an official Ollama or Microsoft SDK. Its restored
package records source commit `a1ce0bd5f1021efd8d9dd1818a319575bb7b31a1`.
It brings MIT `Microsoft.Extensions.AI` 10.8.0; the shared MEAI abstractions
are centrally pinned to 10.11.0 for the MCP SDK. Kora uses `IChatClient` for buffered/streaming text and SDK-native `ChatAsync`
for tool/clarification messages, preserving their admitted correlation and
result shape without the lossy MEAI tool mapper. No automatic
function-invocation, approval or telemetry middleware is used. Its source-generator
analyzer is included in the package; no server or model assets are supplied.
Kora retains the fixed endpoint/process gate, strict raw response policy,
context-integrity controls and resource ownership. This dependency change
does not qualify or enable any local model.

External MCP uses the official Apache-2.0 `ModelContextProtocol.Core` 2.4.0
package, whose restored metadata records source commit
`4cbf42ef107763f0bc75c358af8cb2dfe47a769a`. It requires MIT MEAI abstractions
10.11.0 and includes a managed analyzer, not a server executable. The SDK
owns client protocol/session mechanics behind Kora's existing bounded HTTPS
and raw admission gates. No OAuth, automatic tools, sampling, elicitation,
roots, reconnects or content logging is enabled. This package admission
does not qualify a delegated account or widen the supported text-only profile.

The repository pins `nuget-license` as a local .NET tool. The license gate:

1. reads the restored, locked dependency graph for every project in
   `Kora.slnx` (the application, libraries, and their test projects);
2. includes direct and transitive NuGet packages;
3. permits only the reviewed identifiers in
   `eng/licenses/allowed-licenses.json`;
4. fails on missing, unknown, or newly introduced license identifiers;
5. applies only version-specific overrides recorded in
   `eng/licenses/package-overrides.json` and SHA-256-verified file mappings in
   [license-file-identities.json](eng/licenses/license-file-identities.json); and
6. verifies that the generated package report matches
   `THIRD-PARTY-NOTICES.md`.

Run it locally after a locked restore:

```powershell
dotnet restore .\Kora.slnx --locked-mode
dotnet tool restore
.\eng\Test-DependencyLicenses.ps1
```

The CI workflow runs the same command and publishes its JSON report, downloaded
license texts, and package-provided notice files as an audit artifact. Official
publish archives include Kora's license, the generated third-party notice, and
those dependency license and notice files.

### Experimental dependencies

Projects under `experiments/` are not members of `Kora.slnx` and are not covered
by this automated gate or its generated distribution notice. This exclusion
also applies to experimental NuGet dependencies, not just Python and npm
packages. Experimental dependencies and assets must be reviewed separately and
are not approved for production or distribution by a passing solution gate.

In particular, the storage proof's `Microsoft.Data.Sqlite.Core` and
`SQLitePCLRaw.bundle_e_sqlcipher` dependencies are research-only. Their version,
native SQLCipher provenance, license and redistribution gates are recorded in
the [storage proof](experiments/r02-storage-proof/README.md). The other
[R02 proofs](Design/Deferred_Validation.md) retain their own dependency and
asset-review evidence and outstanding gates.

Before moving experimental code into the product, include its production
project in the solution gate, review all direct/transitive packages and native
assets, and regenerate the production notices. Do not copy experimental
binaries, models, or license reports into official publish archives.

## Review requirements

An allowlisted identifier is not blanket approval for every dependency.
Package additions and upgrades still require review of provenance, embedded
assets, license notices, and any non-license obligations. Do not add a license
identifier merely to make CI pass.

Metadata overrides are exceptional. Each override must pin an exact package
version and link to authoritative evidence because it substitutes for missing
or defective NuGet metadata. A package upgrade will not inherit an old
override.

SQLite 3.53.4 supplies a `LICENSE.txt` public-domain declaration pointing to
[SQLite's copyright statement](https://sqlite.org/copyright.html). The
[reviewed file mapping](eng/licenses/license-file-identities.json) admits only
that exact version and file digest as `LicenseRef-SQLite-Public-Domain`.
Changed bytes fail before scanning; other versions require separate review.
The [native payload contract](eng/NativeInspection.Common.ps1) recognizes this
package as the owner of `e_sqlite3.dll`; [source staging](eng/SourceBootstrap.Common.ps1)
still requires its exact identity to match the selected checkout's dependency lock.

The WiX 7 bootstrapper API supplies `OSMFEULA.txt`, which is not a standard SPDX
licence. The gate maps only the SHA-256-pinned file identified in
`eng/licenses/license-file-identities.json` to `LicenseRef-WiX-7-OSMF`, retaining
the actual EULA text rather than substituting a permissive licence. A changed
file fails before scanning. The maintainer selected this non-revenue-generating
proof; other users must assess the EULA's revenue/fee applicability themselves.
See the [installer terms and open redistribution gates](installer/README.md#wix-terms),
including WiX's MS-RL source-provision obligations and self-contained runtime
notice review. The WiX SDK projects are built separately on Windows and are
not themselves members of the solution's NuGet report.

Dependencies outside NuGet, including downloaded executables, models, fonts,
media, and datasets, require separate review before selection or distribution.
Record the exact identity, version or digest, source, license, required notices,
and whether Kora redistributes it. Kora currently installs Ollama 0.35.1 only
after user consent under its MIT license and downloads the pinned Qwen3 1.7B
model under Apache-2.0; neither is bundled into Kora's publish archives.

The hosted adapter pins the MIT-licensed `GitHub.Copilot.SDK` **1.0.26**.
Its managed dependency closure includes
`Microsoft.Extensions.AI.Abstractions` 10.2.0; logging abstractions are supplied
by the .NET 11 shared framework. The package's build/content assets are excluded: restoring,
building or publishing Kora must not implicitly download or redistribute the
separate Copilot runtime. The package declares runtime **1.0.103**; that is
dependency metadata, not installed-runtime or distribution approval.

The public SDK API was researched at release tag `v1.0.26`, commit
`f527e6134f9b38980af8aa796b4c959458a88769`. The actual restored NuGet package
instead identifies `github/copilot-agent-runtime` commit
`131fc866123af5df4451497f183cadef4ddf20f2` in its repository metadata.
Compilation against the released package is verified independently; no
public-source/package byte-parity claim is made. Final-request interception
and typed session-filesystem APIs carry the SDK's experimental `GHCP001`
annotation; the adapter's narrowly scoped suppressions are not a claim of
stable upstream API guarantees.

An approved explicit execution account, current account-bound catalogue,
runtime provenance/terms, usage/cost approval and private host-owned runtime
paths remain live enablement requirements. The default deployment-approval
source remains unavailable until separate native live-evaluation runtime,
terms and charges approval; this is not production qualification. See the
[evaluation contract](docs/copilot-requests.md). Token-entry controls stay
disabled without that approval. The account broker stores an explicitly verified token only
in volatile managed memory and publishes a finite account-bound catalogue;
it does not persist the token or securely zero immutable managed strings.
Kora never treats the developer agent's Copilot login, `gh`
credentials, saved provider preferences or this dependency licence review as
execution-account or hosted-disclosure approval.

This policy is an engineering control, not legal advice. Material licensing
questions or unusual terms require qualified legal review.
