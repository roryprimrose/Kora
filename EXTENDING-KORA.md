# Extending Kora

This contributor guide helps choose and wire an extension. It is not an SDK
or permission contract. [Contributing](CONTRIBUTING.md) owns repository checks;
the linked design owners define policy and availability.

## Choose the Right Extension

First apply [feature admission](Design/MVP_Scope.md#feature-admission). This table chooses the mechanism for approved behavior; it does not approve a feature or require a native action for every useful domain operation. Do not commission replacements for retired utilities without a new product decision.

| Need | Choose | Implementation guide |
|---|---|---|
| An approved host-owned observation or effect | Host action/tool; a skill wrapper is optional | [Tools](src/Kora.Tools/README.md) |
| A reusable outcome and workflow over admitted operations | Versioned skill definition | [Definitions](src/Kora.Definitions/README.md) |
| Reusable task/response framing with explicit inputs | Prompt with a real bounded consumer | [Definitions](src/Kora.Definitions/README.md) |
| Scoped guidance shared by tasks or profiles | Instructions; skill-only guidance stays with the skill | [Definitions](src/Kora.Definitions/README.md) |
| Named guidance, skill/tool subset and runtime constraints | Compatible declarative agent profile through the bounded host workflow | [Agent profiles](#agent-profiles) |

Content guides interpretation; it never grants execution, approval, settings,
session or egress authority.

## What Exists Today

Consult the [roadmap](Design/Implementation_Roadmap.md#current-delivery) for
delivery and the [canonical catalogue](Design/Internal_Model_Tools.md) for
model descriptors. Current consumers include owned prompts/instructions,
reviewed declarative skills, local agent profiles and bounded MCP requests.
C# actions/DI are not automatic model exposure; bundled script resources remain
inert. Implemented workflows do not qualify their provider, connector or worker.

## Put Code and Content in the Right Layer

[Architecture](Design/Architecture.md#platform-boundaries-and-support) owns
dependency direction. Portable contracts/policy belong in Core, actions in
Tools, bundled resources in Definitions, orchestration/presentation state in
Application, native mechanisms in Windows, and DI/desktop controls in Kora.
Tools and Definitions reference Core, never Application/Windows/desktop.

Use the [action layout](Design/Commands_Tools_And_Skills.md#built-in-tool-source-layout-and-implementation)
and [definition organization](Design/Commands_Tools_And_Skills.md#bundled-definitions-and-agent-profiles).
Legacy command handlers are not all registered tools; migrate only an actual
capability and its common authority path.

## Contribution Workflow

State the approved user outcome and complete interaction first. An intermediate field-preparation command must be labelled preparation, not completion. Preserve shared voice/UI confirmation and mandatory privacy/authentication exceptions.

1. Define the explicit capability ID/schema, bounded inputs/results, effects,
   caller lanes, dependencies, supported routes and unavailable reasons.
2. Implement at the real seam using the [Tools checklist](src/Kora.Tools/README.md#contribute-a-new-tool)
   or [Definitions checklist](src/Kora.Definitions/README.md#contribute-a-new-definition).
   Reuse Core validation and shared brokers.
3. Wire applicable exact commands, native controls and admitted model proposals
   through the same action/gateway. Follow the
   [exposure rule](Design/Commands_Tools_And_Skills.md#action-exposure-is-the-default);
   document concrete omissions, not implementation convenience.
4. Preserve host identity/original origin, privacy/ownership, required approval,
   pre-effect revalidation, audit/correlation, cancellation and resource release
   under [security policy](Design/Security_Data_Flows.md). Unknown state fails
   closed; keep content/paths/secrets out of logs, traces and unauthorized envelopes.
5. Test declared routes, exact bounds, denial/failure/stale/cancellation paths
   and lifecycle invariants with deterministic fixtures. Update the canonical
   contract, current user syntax and roadmap row, not every introduction.

## Agent Profiles

A compatible profile can enter the implemented reviewed local-agent workflow.
The host resolves its exact source, supported references, tool subset and
bounded runtime constraints before a separately confirmed exact-session run.
Authoring/saving a profile does not activate or run it. The shipped local-model
qualification authority accepts no profile, so implementation is not production
inference availability.

A profile is not a worker, permission system or scheduling lane. Its constraints
may narrow host authority, never expand it.

[Profile design](Design/Commands_Tools_And_Skills.md#bundled-definitions-and-agent-profiles)
owns the definition; [runtime ownership](Design/Architecture.md#runtime-ownership-decision)
and [provider gates](Design/Implementation_Roadmap.md#r02-runtimeprovider-follow-up-gates)
own admission. Loading content does not authorize recursive delegation,
concurrency, provider fallback or extra tools; broader profiles remain gated.

## Validation and Source Ownership

Follow [development and documentation checks](CONTRIBUTING.md#development-checks).
Project guides identify action/broker, domain, gateway and native test locations.
Content changes also require independent digest vectors and final published
resource-byte checks for both Windows RIDs; see
[Definitions validation](src/Kora.Definitions/README.md#validation).

Bundled resources are immutable, not writable profile packages. Source
discovery and [declarative authoring](Design/Skill_Authoring.md) need separate
admission/save/enablement paths and cannot copy bundled executable trust.
Consult [script admission](Design/Built_In_Skills.md#script-admission-and-execution)
before proposing a runner.
