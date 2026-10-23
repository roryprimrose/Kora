---
description: "Use when changing production behavior or writing unit, integration, regression, cancellation, race, privacy, or resource-lifecycle tests."
applyTo: "src/**/*.cs,tests/**/*.cs"
---
# Behavioral regression evidence

- Use [contributor development checks](../../CONTRIBUTING.md#development-checks) for pinned SDK, locked dependencies, MTP/xUnit v3, AwesomeAssertions, commands and scheduling; do not maintain another command block here.
- Add a regression that distinguishes the intended behavior from the defect. Demonstrate failure before a fix when practical; characterize legacy behavior before extracting a new owner.
- Assert exact observable results, typed outcomes/denial reasons, state transitions and audit receipts. Also assert absence of forbidden execution, disclosure, persistence and durable success receipts, not merely that an exception was thrown.
- Cover applicable success, failure, cancellation, invalid persisted data, stale revisions, cross-session input, revocation and late-completion boundaries, including authority changes across awaited work and commit/disclosure.
- Use deterministic fakes at time, I/O, provider and platform seams. Coordinate races with explicit barriers/completion signals, not sleeps, real devices, user settings or network access. Cancellation requested and actual resource quiescence require separate assertions.
- Prefer public behavior/contracts over reflection or private-field mutation; justify unavoidable legacy reflection narrowly. Reflection, source-string and XAML-shape checks are supplemental, not proof of DI wiring, authority or operation completion.
- Preserve the nonparallel `Host tracing` collection for process-wide listeners and serial headless UI dispatcher collections within each process. Separate shard processes do not justify weakening collection isolation.
- Preserve **100% portable line and branch coverage across Core, Application, Tools and Definitions**, including complete module/assembly evidence. Do not weaken assertions, skip tests, add coverage exclusions or lower thresholds to make work pass.
- Run the smallest covering local checks; a filtered run is not a full-suite pass. CI retains complete portable, Windows and installer gates. Report exact commands, results, failures and unrun/skipped checks without transferring evidence between revisions.
- Keep deterministic implementation evidence separate from enablement/RC qualification under the [I/E/Q policy](../../Design/Implementation_Roadmap.md#delivery-and-qualification-dependency-policy); an unavailable capability stays unavailable.
