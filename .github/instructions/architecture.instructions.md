---
description: "Use when implementing or refactoring C# code, applying SOLID or clean-code practices, or changing architecture, project references, service ownership, dependency injection, view models, built-in tools, or workflow lifecycle."
applyTo: "src/**/*.cs,src/**/*.csproj"
---
## Architecture and clean implementation

### Ownership and boundaries

- Follow the [layer/platform contract](../../Design/Architecture.md#platform-boundaries-and-support) and [extension placement guide](../../EXTENDING-KORA.md). Do not introduce platform dependencies into portable layers.
- Give each stateful workflow one cohesive owner for its state, cancellation, subscriptions, workers, late completions and teardown. Keep view models focused on presentation state, command availability and delegation to injected collaborators.
- Partial files organize one logical type; adding partials is not responsibility separation. Do not disguise constructor growth with a service locator or dependency bag. Bounded repairs do not require a wholesale refactor.
- Put policy, validation and serialization rules in one authoritative location. Reuse the same admitted action and host policy across command, native UI, typed, voice and model routes; no alternate execution path that bypasses admission.
- Implement built-in actions as one class per registered action in `Kora.Tools/<CapabilityGroup>/<Action>.cs` with matching namespace. Share cohesive brokers, not a broad method-per-tool manager. Follow [action layout and wiring](../../Design/Commands_Tools_And_Skills.md#built-in-tool-source-layout-and-implementation).
- Keep bundled behavior in `Kora.Definitions`, grouped by content kind with each skill's instructions/fixtures/scripts together. Preserve explicit resource IDs, exact bytes and package digests when moving resources.
- Definitions confer no execution, approval, egress or session authority; agent profiles stay gated until qualified host/runtime support. Follow [definition ownership](../../Design/Commands_Tools_And_Skills.md#bundled-definitions-and-agent-profiles).

### Apply SOLID pragmatically

- **Single responsibility:** give a type one cohesive responsibility and reason to change, including the lifecycle it owns. Extract a named responsibility when it improves understanding or independent testing, not merely to move methods into another file.
- **Open/closed:** extend through established seams when real variation exists. Do not build speculative frameworks, extension points or polymorphic hierarchies where a straightforward conditional expresses the current requirement.
- **Liskov substitution:** preserve the consumer's behavioral contract across implementations: preconditions, results, failure/denial outcomes, cancellation, ownership and side effects. Matching a signature is insufficient; do not strengthen preconditions or weaken promised guarantees.
- **Interface segregation:** prefer small, consumer-focused contracts; do not make consumers or fakes implement unrelated capabilities. Narrow interfaces may share one cohesive implementation.
- **Dependency inversion:** keep policy independent of platform mechanisms and inject dependencies at real replaceable seams or independently testable workflow boundaries. Do not add speculative interfaces or a matching interface for every concrete class.

### Write readable, explicit implementations

- Choose intention-revealing names, cohesive methods and a consistent abstraction level within each method. Use explicit control flow and guard clauses where they clarify the normal path; avoid clever expressions, deep nesting and arbitrary method/type line-count targets.
- Apply KISS/YAGNI: choose the simplest complete implementation for the approved requirement. Prefer composition over inheritance used only for code reuse; avoid speculative configurability, generic managers and unrelated refactoring.
- Apply DRY to shared knowledge and invariants, not merely similar-looking code. Reuse existing helpers; extract a common owner only when the callers genuinely share semantics and change together.
- Prefer immutable values where practical; keep mutation local and make state transitions, I/O, resource ownership and consequential side effects apparent. Separate decisions from effects when it clarifies a real boundary, not to add layers.
- Preserve strong domain types and nullable analysis. Replace opaque boolean mode flags, magic values and stringly typed states with meaningful contracts when they obscure intent or allow invalid combinations; do not use `dynamic`, `object` or unsafe casts to evade a proper contract.
- Keep failure and recovery explicit, preserve cancellation and dispose owned resources deterministically. Follow the existing [security boundary guidance](security.instructions.md); concision is not a reason to hide failure or turn unavailable work into success.
- Comment only non-obvious rationale, invariants or constraints. Prefer clear names and structure to comments that narrate the code.

### Verify behavior, not structure alone

- For ownership changes, add [behavioral regressions](testing.instructions.md) for applicable cancellation, late completion, reentrancy, disposal and unchanged authorization. Prove the entire lifecycle, not merely method relocation or constructor shape.
- For interchangeable implementations, test the applicable shared behavioral guarantees, including failure and forbidden effects, with deterministic fakes. Do not treat interface count, class size or source shape as clean-code evidence.
- Find applicable maintained checks through [contributor validation](../../CONTRIBUTING.md#development-checks). Run existing relevant checks and report unavailable ones honestly; instructions do not establish that a proposed architecture guard exists.
