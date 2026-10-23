---
description: "Use when changing C# architecture, project references, service ownership, dependency injection, view models, built-in tools, or workflow lifecycle."
applyTo: "src/**/*.cs,src/**/*.csproj"
---
# Architecture and ownership

- Follow the [layer/platform contract](../../Design/Architecture.md#platform-boundaries-and-support) and [extension placement guide](../../EXTENDING-KORA.md). Do not introduce platform dependencies into portable layers.
- Give each stateful workflow one cohesive owner for its state, cancellation, subscriptions, workers, late completions and teardown. Keep view models focused on presentation state, command availability and delegation to injected collaborators.
- Partial files organize one logical type; adding partials is not responsibility separation. Do not disguise constructor growth with a service locator or dependency bag. Bounded repairs do not require a wholesale refactor.
- Put policy, validation and serialization rules in one authoritative location. Reuse the same admitted action and host policy across command, native UI, typed, voice and model routes; no alternate execution path that bypasses admission.
- Implement built-in actions as one class per registered action in `Kora.Tools/<CapabilityGroup>/<Action>.cs` with matching namespace. Share cohesive brokers, not a broad method-per-tool manager. Follow [action layout and wiring](../../Design/Commands_Tools_And_Skills.md#built-in-tool-source-layout-and-implementation).
- Keep bundled behavior in `Kora.Definitions`, grouped by content kind with each skill's instructions/fixtures/scripts together. Preserve explicit resource IDs, exact bytes and package digests when moving resources. Definitions confer no execution, approval, egress or session authority; agent profiles stay gated until qualified host/runtime support. Follow [definition ownership](../../Design/Commands_Tools_And_Skills.md#bundled-definitions-and-agent-profiles).
- Add interfaces only at real replaceable seams, consumer boundaries or independently testable cohesive workflows: platform, persistence, time, process or network. Do not add speculative interfaces or a matching interface for every concrete class.
- Preserve strong types, nullable analysis, explicit error/recovery paths and deterministic disposal. Do not use `dynamic`, `object` or unsafe casts to evade a proper contract.
- For ownership changes, add [behavioral regressions](testing.instructions.md) for applicable cancellation, late completion, reentrancy, disposal and unchanged authorization. Prove the entire lifecycle, not merely method relocation or constructor shape.
- Find applicable maintained checks through [contributor validation](../../CONTRIBUTING.md#development-checks). Run existing relevant checks and report unavailable ones honestly; instructions do not establish that a proposed architecture guard exists.
