# Extending Kora

This is a contributor guide to choosing and wiring an extension, not an SDK or
permission contract. Read [Contributing](CONTRIBUTING.md) for contribution terms
and development checks, and the [repository instructions](.github/copilot-instructions.md)
for implementation conventions. The linked design pages remain authoritative
for policy; a guide, catalogue entry or content file cannot enable a gated feature.

## Choose the right extension

| Need | Choose | Contribution surface |
|---|---|---|
| A new typed operation that observes state or performs an effect | **Tool / host action** | One C# action class in a capability folder; follow the [Tools guide](src/Kora.Tools/README.md). |
| A repeatable outcome with selection guidance, inputs and a workflow over admitted operations | **Skill** | A versioned definition package; follow the [Definitions guide](src/Kora.Definitions/README.md). A tool does not require a skill wrapper. |
| Reusable task or response framing with explicit inputs | **Prompt** | A bounded template and real consumer in Definitions when implemented; not a replacement for runtime protocol validation. |
| Guidance applying to a named task, capability or context scope | **Scoped instructions** | Shared guidance in Definitions when implemented. Instructions belonging only to one skill stay with that skill. |
| A named task configuration combining guidance, skills, tool subset and runtime constraints | **Agent profile** | A declarative profile in Definitions only when its host/runtime support is qualified; see [Agent profiles](#agent-profiles). |

For example, a new authoritative state query is a C# action, not a PowerShell
skill. A workflow explaining that query's results may be a skill; common
explanation framing may be a prompt. None of these forms grants tool, approval,
settings or egress authority.

## What exists today

| Surface | Delivered | Planned or gated |
|---|---|---|
| Built-in C# tools | Six R06 read-only actions behind the admitted Application gateway; R07 clipboard capture/reuse/revoke actions behind the shared broker and local host workflow. | Generic model tool/result iteration and qualified adapters remain open. An action implementation or DI registration is not automatic model exposure. |
| Bundled skills | Fixed catalogue of Lock, Shutdown and Restart packages; 13 immutable embedded resources and passive native inspection. | Every package action is unavailable for invocation. No enabled skill runner exists. |
| Prompts / shared Instructions | No separate content catalogue or folders for these kinds yet. Existing JSON selector/system framing remains runtime code. | Introduce actual resources, schemas and consumers together; do not create placeholder loaders or empty folders. |
| Agents | No agent-profile loader or agent runtime is delivered. | Named profiles require qualified host/runtime support, not just content loading. |
| User/profile definitions | Bundled first-party content is explicitly resolved from its own assembly. | User/profile discovery, revision enablement and authoring are separate proposed work, not features of the bundled catalogue. |

Check the [current/proposed tool inventory](Design/Internal_Model_Tools.md) and
[implementation roadmap](Design/Implementation_Roadmap.md) before assigning IDs
or claiming availability. Readiness observations are not runtime qualification.
Clipboard preview is not model context selection or transmission consent.

## Put code and content in the right layer

| Layer | Owns | Dependency direction |
|---|---|---|
| [Kora.Core](src/Kora.Core) | Portable contracts, canonical IDs/schemas and authoritative domain validation/policy rules. | No native or presentation dependencies. |
| [Kora.Tools](src/Kora.Tools/README.md) | Host-owned C# actions in capability folders with matching namespaces, plus cohesive shared brokers. | References Core, never Application, Windows, desktop or provider SDKs. |
| [Kora.Definitions](src/Kora.Definitions/README.md) | One portable project for bundled content/resources, grouped by implemented content kind. | References Core, not Tools, Application, Windows, desktop or provider SDKs. |
| [Kora.Application](src/Kora.Application) | Admitted gateways, request orchestration and presentation state. | Currently references Core and Tools; actions do not reference it back. |
| [Kora.Windows](src/Kora.Windows) | Native mechanisms behind injected portable seams. | Keeps native handles, Windows APIs and OS-specific exceptions out of portable layers. |
| [Kora desktop](src/Kora) | DI/composition and desktop controls/presentation, including bundled-definition inspection. | Wires portable collaborators to native implementations and host ownership; consumes Definitions explicitly. |

See [Architecture](Design/Architecture.md#platform-boundaries-and-support),
[tool implementation](Design/Commands_Tools_And_Skills.md#built-in-tool-source-layout-and-implementation)
and [definition organization](Design/Commands_Tools_And_Skills.md#bundled-definitions-and-agent-profiles).
Legacy command handlers are not all registered tools; migrate only a capability
whose actual contract and common authority path are in scope.

## Contribution workflow

1. **Describe the capability and its status.** Consult the canonical inventory
   and roadmap. Specify inputs, bounded outputs, effects, caller lanes,
   dependencies and supported entry points. Assign explicit canonical IDs and
   schema versions; never derive them from class names, filenames or reflection.
   Record concrete reasons for unavailable or omitted routes.
2. **Implement at the real seam.** Use the [Tools checklist](src/Kora.Tools/README.md#contribute-a-new-tool)
   for executable C# behavior or the
   [Definitions checklist](src/Kora.Definitions/README.md#contribute-a-new-definition)
   for content. Reuse Core validation and shared brokers; do not reproduce a
   policy in each action, template or view model.
3. **Wire every applicable route.** Exact commands, native UI and admitted model
   proposals must converge on the same registered action and applicable gateway.
   Add DI, dispatch, descriptors and presentation deliberately. Model route
   omissions need a documented qualification/privacy/origin or UX reason, not
   implementation convenience. The existing JSON action selector is not a
   generic tool-call API.
4. **Preserve the host boundary.** Carry host-resolved identity and original
   origin through authorization, privacy/ownership checks, approval,
   pre-effect revalidation, cancellation, deadlines and resource release.
   Unknown state fails closed. Return bounded, content-minimizing facts and
   truthful outcomes; keep presentation separate. Do not leak source content,
   clipboard bodies, paths or secrets into logs, traces or provider envelopes.
   Use the trusted structured audit/correlation path where applicable.
5. **Test and document the declared behavior.** Prove route convergence,
   unavailable/denied/error paths, hostile inputs, cancellation and lifecycle
   invariants with fakes. Check exact byte/record limits and serialized output,
   not just a convenient proxy. Update the relevant capability inventory,
   user reference and availability/prerequisite links without advertising
   unfinished execution support.

The [action exposure rule](Design/Commands_Tools_And_Skills.md#action-exposure-is-the-default),
[tool gateway](Design/Architecture.md#tool-gateway) and
[Security and Data Flows](Design/Security_Data_Flows.md) own these requirements.
Definitions guide interpretation; they never replace enforcement.

## Agent profiles

**Design direction, not delivered:** an agent is a named declarative task profile
referencing instructions, skills and a bounded subset of admitted tools, with
typed input/output and runtime, locality and budget constraints. A profile is
not a running worker, a permission system or a scheduling lane.

The host owns each run's identity, original origin, leases, budgets,
cancellation, approvals and egress. Loading content does not authorize recursive
delegation, concurrency, auto-enablement, extra tools or provider fallback.
Profile constraints can narrow admitted behavior, never expand its authority.

Start with the [profile design](Design/Commands_Tools_And_Skills.md#bundled-definitions-and-agent-profiles),
[runtime ownership](Design/Architecture.md#runtime-ownership-decision) and
[runtime/provider gates](Design/Implementation_Roadmap.md#r02-runtimeprovider-follow-up-gates).
The [local-inference continuation](Design/Implementation_Roadmap.md#r02-local-inference-continuation)
and [work-management design](Design/Work_Management.md) are prerequisites where
iteration or scheduling is involved; loading a profile does not satisfy them.

## Validation and source ownership

- Action tests belong in [Kora.Tools.UnitTests](tests/Kora.Tools.UnitTests);
  catalogue/resource tests in [Kora.Definitions.UnitTests](tests/Kora.Definitions.UnitTests).
  Core tests cover domain rules; Application tests cover gateway and route
  admission; Windows tests cover non-destructive native seams.
- Use xUnit v3 and AwesomeAssertions. Follow the existing deterministic fixtures
  and nonparallel `Host tracing` collection for process-wide activity listeners.
- Follow [development checks](CONTRIBUTING.md#development-checks). The
  [CI workflow](.github/workflows/ci.yml) still requires **100% line and branch
  coverage across Core, Application, Tools and Definitions**. Moving code or
  adding a content kind does not relax or exclude that gate.
- Resource changes also require independent digest vectors and final
  `Kora.Definitions.dll` PE resource checks for **both `win-x64` and `win-x86`**;
  see the [Definitions validation guide](src/Kora.Definitions/README.md#validation).
- Documentation-only changes need link, heading, example and status checks, not
  a dependency restore, app launch or live trust trial. Code/resource changes
  require their focused checks and existing CI gates; manual device, execution
  or model qualification is separate, explicitly authorized evidence.

Bundled definitions are immutable application resources, not writable profile
packages. Future [profile discovery](Design/Skill_Storage.md#shared-profile-discovery)
and [declarative authoring](Design/Skill_Authoring.md) require their own admission,
save and enablement paths. They cannot replace bundled resources or create
executable trust. Consult [script admission](Design/Built_In_Skills.md#script-admission-and-execution)
before proposing any runner.
