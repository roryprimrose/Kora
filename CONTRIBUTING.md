# Contributing to Kora

Thank you for helping improve Kora. Issues, design discussion, documentation,
tests, and focused pull requests are welcome.

## Contribution terms

Kora is source-available under the
[PolyForm Shield License 1.0.0](LICENSE), not an OSI-approved open-source
license. By submitting a contribution, you confirm that:

- you have the right to submit it;
- your contribution is provided under the repository's PolyForm Shield 1.0.0
  terms; and
- it does not knowingly introduce code, assets, models, or other dependencies
  whose terms conflict with Kora's license or intended distribution.

Public forks may be used to develop and propose contributions, subject to the
license. Do not market or provide a fork as a practical substitute for Kora.

## Development checks

Restore, build, and test with the pinned SDK and locked dependencies:

```powershell
dotnet restore .\Kora.slnx --locked-mode
dotnet tool restore
dotnet build .\Kora.slnx --configuration Release --no-restore
dotnet test --project .\tests\Kora.Core.UnitTests\Kora.Core.UnitTests.csproj --configuration Release --no-build
dotnet test --project .\tests\Kora.Application.UnitTests\Kora.Application.UnitTests.csproj --configuration Release --no-build
dotnet test --project .\tests\Kora.Windows.IntegrationTests\Kora.Windows.IntegrationTests.csproj --configuration Release --no-build
```

Run the dependency-license gate before adding or upgrading a package:

```powershell
.\eng\Test-DependencyLicenses.ps1
```

The gate covers projects in `Kora.slnx`, not isolated experiments. Experimental
dependencies require separate recorded review and cannot enter production or
publish archives without the admission steps in the
[dependency license policy](DEPENDENCY-LICENSES.md#experimental-dependencies).

For changes to source delivery, also run the
[managed-source ownership/failure and stage-inspection tests](Design/Distribution_And_Updates.md#verification-snapshot-2026-10-06)
with fresh dedicated scratch directories outside all repositories. These
script checks do not install or launch Kora; a real exact-revision source
publish/static verification is a separate permitted build check, not
protected/installed acceptance.

If an intentional dependency change alters the notice report, review the new
license and provenance first. Add an approved license identifier or a
version-specific metadata override only with evidence, then regenerate:

```powershell
.\eng\Test-DependencyLicenses.ps1 -UpdateNotice
```

Commit the resulting `THIRD-PARTY-NOTICES.md` change with the package and lock
file changes.
