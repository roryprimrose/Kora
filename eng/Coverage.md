# Portable coverage integrity

CI runs all four projects in `Kora.UnitTests.slnx` using Coverlet MTP and the
per-module results layout. `Assert-CodeCoverage.ps1` requires exactly one
`*.coverage.cobertura.*.xml` below each of `Kora.Core.UnitTests`,
`Kora.Application.UnitTests`, `Kora.Tools.UnitTests`, and
`Kora.Definitions.UnitTests` in `-InputDirectory` (default:
`.net-test-artifacts/unit`). Duplicate/stale runs must not be mixed into that
directory. Each report must contain executable evidence for its own assembly.

ReportGenerator 5.5.11 merges those overlapping reports with the existing assembly
filter. Its Cobertura **package names** identify the four required assemblies.
The gate validates their presence, finite rates, nonnegative integer counts,
unique identities and class-line evidence before enforcing 100% line and branch
coverage. Empty assemblies and dishonest aggregate totals fail. Zero branches
with zero covered branches and a rate of one are valid, not missing coverage.
The optional percentage thresholds remain supported; CI keeps both at 100.

Coverlet MTP 10.1.0 truncates rates to four decimal places and can include
compiler-generated branches in root counts that are not represented in class
lines. Raw reports therefore validate root count/rate consistency and represented
line totals, but do not equate their branch totals with represented branches.
ReportGenerator emits full-precision rates and represented branch counts; the
merged report is checked against class-level lines and condition counts.
Method-level lines repeat class evidence and are not summed.

This contract was checked against actual artifacts from CI run `39938915624`,
source `871e56a55c88e0614b2e3286815e8c17e2884c08`, as well as deterministic
fixtures passed through the pinned ReportGenerator. There are no provider,
hardware or product enablement prerequisites.

Run the focused regression suite from the repository root:

```powershell
.\eng\Test-CodeCoverage.ps1
.\eng\Test-PortableTestScheduling.ps1
```

The first script needs the repository's already-restored ReportGenerator tool;
if absent, use `dotnet tool restore`. It exercises the production gate, generates
real merged output from representative raw fixtures, and removes its unique
temporary directory. CI retains its existing raw `test-results` and
`coverage-report` artifacts even when coverage fails.
