---
description: "Use when changing CI workflows, engineering scripts, merge admission, test selection, coverage, analyzers, build configuration, or dependency policy."
applyTo: ".github/workflows/*.yml,.github/workflows/*.yaml,eng/**/*.ps1,eng/**/*.cjs,Directory.Build.*,Directory.Packages.props,**/*.csproj,**/packages.lock.json,global.json,dotnet-tools.json,.editorconfig"
---
# Automation evidence and enforcement

- Preserve canonical commands and validation navigation in [CONTRIBUTING](../../CONTRIBUTING.md#development-checks), and queue/check identities and account-safe publication in the [merge admission guide](../../eng/merge-queue/README.md#copilot-and-other-pr-clients). Successfully read live queue mode; workflow files alone do not prove activation.
- Test changed script/workflow behavior with deterministic fixtures for applicable failure, missing/malformed input, cancellation and skipped dependencies. Missing required inputs, modules, reports or shards must fail closed, not produce success-shaped aggregate output.
- Verify complete expected test-module and coverage-assembly evidence, including Core, Application, Tools and Definitions, not only aggregate percentages or process exit. Preserve 100% portable line/branch thresholds; validate report identities, nonempty evidence and consistency as required by the actual format.
- Retain isolated exact-source results for every required module/shard, aggregate all required evidence and preserve failure artifacts. A successful subset or a skipped waiting-PR workflow is not a full candidate pass.
- Do not lower thresholds, disable analyzers globally, suppress advisories or alter required checks to bypass failures. Keep justified framework/analyzer exceptions scoped to the smallest applicable rule, symbol or file.
- Restore locked dependencies. Review central versions, lock files, notices, provenance and [dependency-license evidence](../../CONTRIBUTING.md#test-and-dependency-policies) together when required; do not install tooling or change packages merely to validate documentation.
- Keep Actions commit-pinned and token permissions minimal. Never execute untrusted PR code/artifacts in a privileged workflow; preserve exact-source/head checks and protected admission.
- Keep physical/provider/installed qualification separate from deterministic implementation validation under [I/E/Q](../../Design/Implementation_Roadmap.md#delivery-and-qualification-dependency-policy). Do not dispatch candidates manually or bypass the queue to manufacture evidence.
- Report exact commands, actual results, failures and unrun checks. Find and run applicable maintained gates; proposed completeness, architecture or security checks are not installed merely because these instructions recommend them.
- Instructions guide agents; automated gates enforce. YAML parsing and glob examples establish format/matching evidence only, not actual client discovery or enforcement.
