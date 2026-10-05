# Dependency license policy

Kora's own source is licensed under PolyForm Shield 1.0.0. Third-party
dependencies remain under their respective licenses.

## Automated NuGet control

The repository pins `nuget-license` as a local .NET tool. The license gate:

1. reads the restored, locked dependency graph for every project;
2. includes direct and transitive NuGet packages;
3. permits only the reviewed identifiers in
   `eng/licenses/allowed-licenses.json`;
4. fails on missing, unknown, or newly introduced license identifiers;
5. applies only version-specific overrides recorded in
   `eng/licenses/package-overrides.json`; and
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

## Review requirements

An allowlisted identifier is not blanket approval for every dependency.
Package additions and upgrades still require review of provenance, embedded
assets, license notices, and any non-license obligations. Do not add a license
identifier merely to make CI pass.

Metadata overrides are exceptional. Each override must pin an exact package
version and link to authoritative evidence because it substitutes for missing
or defective NuGet metadata. A package upgrade will not inherit an old
override.

Dependencies outside NuGet, including downloaded executables, models, fonts,
media, and datasets, require separate review before selection or distribution.
Record the exact identity, version or digest, source, license, required notices,
and whether Kora redistributes it. Kora currently installs Ollama 0.35.1 only
after user consent under its MIT license and downloads the pinned Qwen3 1.7B
model under Apache-2.0; neither is bundled into Kora's publish archives.

This policy is an engineering control, not legal advice. Material licensing
questions or unusual terms require qualified legal review.
