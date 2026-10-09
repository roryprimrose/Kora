# Kora.Definitions

One portable content/resource project for bundled behavior definitions. Start
with [Extending Kora](../../EXTENDING-KORA.md) and
[Contributing](../../CONTRIBUTING.md). The authoritative organization and agent
design is [Bundled Definitions and Agent Profiles](../../Design/Commands_Tools_And_Skills.md#bundled-definitions-and-agent-profiles);
[Built-In Skills](../../Design/Built_In_Skills.md) owns package integrity and
execution admission policy.

## Project structure and definition types

[Kora.Definitions.csproj](Kora.Definitions.csproj) targets `net10.0` and references
Core for portable validation/digest rules. It references neither Tools nor
Application, Windows, desktop, Avalonia or provider SDKs. It is **not** one
project per definition type, and it does not implement C# tool effects.

| Kind | Purpose | Current source/status |
|---|---|---|
| Skills | Versioned outcome, selection guidance, instructions, fixtures and registered task/tool references. | [Skills](Skills): fixed embedded package inspection plus explicit instruction selection for local-model requests; scripts remain unavailable. |
| Prompts | Reusable bounded task/response templates with explicit inputs. | Planned category when real resources and consumers are implemented; no folder/loader yet. |
| Instructions | Shared scoped guidance reused by tasks or profiles. | Planned category; a skill's own instructions remain in its package. |
| Agents | Named declarative task profiles referencing guidance, skills, admitted tools and runtime/locality/budget constraints. | Planned/gated; no profile loader or agent runtime exists. |

Do not create empty `Prompts`, `Instructions` or `Agents` folders or placeholder
loaders. Existing JSON selector/system framing in
[WindowsOllamaReasoner](../Kora.Windows/Dependencies/WindowsOllamaReasoner.cs)
remains runtime code; it was not moved to this project. A template cannot
replace protocol enforcement, authorization or host policy.

The implemented tree is:

```text
Skills/
  EmbeddedSkillCatalogue.cs
  Lock/       manifest.json, skill.md, fixtures.json, entry.ps1
  Shutdown/   manifest.json, skill.md, fixtures.json, entry.ps1
  Restart/    manifest.json, skill.md, fixtures.json, entry.ps1
  session-control.ps1
```

Keep each skill's instructions, fixtures and scripts together. The shared
helper is embedded once and declared by all three packages. C# catalogue
namespace `Kora.Definitions.Skills` follows its folder; resource IDs do not
follow namespaces or automatically change with source paths.

## Fixed catalogue and identities

[EmbeddedSkillCatalogue](Skills/EmbeddedSkillCatalogue.cs) resolves its own
assembly's explicitly registered resources into Core
[SkillPackageCatalogue](../Kora.Core/Skills/SkillPackageCatalogue.cs). It checks
the `Kora.Skills.*`/`Kora.Scripts.*` resource map against the fixed registration
set, then reads immutable snapshots. This is not arbitrary assembly/directory
discovery or a generic definition loader.

There are **13 distinct immutable embedded resources**: four per package
(manifest, instructions, fixture, entry script) plus one shared helper.

| Folder | Skill ID | Declared action | Resource ID prefixes |
|---|---|---|---|
| [Lock](Skills/Lock) | `kora.session.lock` | `session.lock` | `Kora.Skills.Session.Lock.*`, `Kora.Scripts.Session.Lock` |
| [Shutdown](Skills/Shutdown) | `kora.computer.shutdown` | `computer.shutdown` | `Kora.Skills.Session.Shutdown.*`, `Kora.Scripts.Session.Shutdown` |
| [Restart](Skills/Restart) | `kora.computer.restart` | `computer.restart` | `Kora.Skills.Session.Restart.*`, `Kora.Scripts.Session.Restart` |

The `*` suffixes above summarize explicit `.Manifest`, `.Instructions` and
`.Fixtures` IDs, not wildcard embedding. The helper ID is
`Kora.Scripts.Shared.SessionControl`.

Use [Lock's JSON manifest](Skills/Lock/manifest.json) as an **existing fixed
inspection-schema example**, not a universal skill/plugin format. It currently
has schema version `1` and package version `1.0.0`. Its logical entry name
`scripts\session\lock.ps1` maps to source `Skills\Lock\entry.ps1` and explicit
resource ID `Kora.Scripts.Session.Lock`. Logical names, source paths, resource
IDs, skill IDs and action IDs are distinct identities.

Core [SkillPackageManifest](../Kora.Core/Skills/SkillPackageManifest.cs) requires
exact fields/roles, one instruction document and entry point, declared helpers
and matching helper-load order. Every manifest reference must resolve to the
exact name/resource mapping, and the catalogue must reject unlisted resources,
duplicate skill/action IDs and aliases. Nothing is discovered or enabled merely
by adding a file.

## Bytes, bounds and versioning

Use the existing Core validators; do not add a second set of parsing or hashing
rules in a catalogue. Current implementation limits include:

- [SkillResourceSnapshot](../Kora.Core/Skills/SkillResourceSnapshot.cs):
  1-65,536 bytes per resource; strict UTF-8 with optional UTF-8 BOM; reject
  malformed encoding, NUL/UTF-16 and invalid names/IDs. Logical names use bounded
  lowercase ASCII segments and backslashes; aliases/traversal are rejected.
- [SkillPackageCatalogue](../Kora.Core/Skills/SkillPackageCatalogue.cs):
  at most eight packages and 32 unique resources.
- [SkillPackageDigest](../Kora.Core/Skills/SkillPackageDigest.cs):
  at most 16 files and 256 KiB total per digest set; reject empty/aliased sets
  and unknown encoding versions.

Preserve **original bytes**, including BOM, whitespace and line endings.
Review and hashes use the immutable byte snapshot, not decoded/re-encoded or
normalized text. A source-folder/assembly move must preserve explicit logical
IDs, manifest mappings, resource bytes and golden digests unless a separately
reviewed content/version change is intended.

The domain-separated, ordinal-name-sorted, length-framed SHA-256 encodings are:

| Encoding version | Covers |
|---|---|
| `Kora.ScriptSet.v1` | Complete declared entry/helper script set, not just the selected branch or entry point. |
| `Kora.SkillDefinition.v1` | Exact manifest, instructions and declared fixtures. Manifest mappings/load order participate here. |
| `Kora.DeclaredResources.v1` | Entire package closure, including logical names, explicit resource IDs and exact bytes. |

Follow the [canonical digest rules](../../Design/Built_In_Skills.md#deterministic-script-set-hash)
and [definition/grant identity](../../Design/Built_In_Skills.md#definition-identity-and-grant-key).
Content changes need reviewed package version/digest updates; schema changes
need explicit compatibility handling, not silent reinterpretation. A shared
helper change affects all dependent script sets. An instructions/manifest-only
change still changes the definition and requires fresh review, even when scripts
are unchanged. A self-declared checksum or content match is not execution trust.

## Contribute a new definition

1. Choose the kind using [Extending Kora](../../EXTENDING-KORA.md#choose-the-right-extension).
   Check the [roadmap](../../Design/Implementation_Roadmap.md) and distinguish
   passive content delivery from proposed execution/authoring/runtime work.
2. For a bundled skill, keep its manifest, instructions, data-only fixtures and
   complete declared scripts together under `Skills/<Name>`. Assign explicit
   skill/action/schema/package versions and logical names/resource IDs.
   Reuse Core validation; the fixed schema is not a user/profile importer.
3. Add explicit `EmbeddedResource` items with `LogicalName` in the project and
   explicit catalogue registrations. List the full manifest closure and shared
   dependencies; no globs, writable extraction, loose-file overrides or ambient
   path fallback. A deliberate catalogue expansion must update its exact closure
   expectations and tests, including the current 13-resource PE check; do not
   weaken it to accept unspecified extras.
4. For a new prompt, shared instruction or agent category, introduce only actual
   content with a bounded, versioned Core contract, explicit catalogue and real
   consumer. Add its resource/digest/review tests and namespace matching its
   folder. Do not imply that the fixed skill manifest loads arbitrary types.
5. Wire consumption through the host: current passive inspection uses desktop
   [SkillPackagesWindowController](../Kora/SkillPackagesWindowController.cs).
   Any new Application orchestration and desktop DI/consumer wiring must retain
   ownership/privacy, original origin, cancellation and content-minimizing
   outcomes. Admitted execution, if separately qualified, uses the same tool
   gateway for applicable command/UI/model routes, never an effect in Markdown.
6. Add independent vectors and negative tests, update availability and
   prerequisite links, and run the existing validation gates. Do not advertise
   execution because content parses, a model selects it or a fixture passes.

## Execution and discovery boundaries

**Delivered:** read-only native inspection of all declared files and
[explicit bundled artifact instruction selection](ArtifactInvocation/EmbeddedArtifactCatalogue.cs)
through [slash/activated-voice routing](../../docs/commands.md#run-skills-and-future-artifacts)
into the existing verified local-model request.
**Unavailable:** execution of every bundled package script action.
[SkillPackageSnapshot](../Kora.Core/Skills/SkillPackageSnapshot.cs) explicitly
reports no admitted interpreter/adapter identity and outstanding worker,
protected-deployment, network and real-control gates. Script resources are inert;
neither installation of PowerShell nor package review enables a runner.
Existing direct native lock/power behavior is not execution of these resources.

Follow [script admission](../../Design/Built_In_Skills.md#script-admission-and-execution)
and the [R11 delivery/prerequisites](../../Design/Implementation_Roadmap.md#r11-bounded-embedded-package-catalogue-and-native-review---2026-10-07).
Definitions confer no tool, approval, settings, session or egress authority.

A future agent profile is not a worker, permission system or scheduling lane.
The host retains identity, leases, budgets, cancellation, approvals and egress;
loading a profile authorizes no recursive delegation, concurrency,
auto-enablement or provider fallback. See the
[agent guide and runtime prerequisites](../../EXTENDING-KORA.md#agent-profiles).

Bundled definitions are distinct from delivered bounded
[compatible disk instruction discovery](../Kora.Windows/ArtifactDiscovery/WindowsDiskArtifactDiscovery.cs),
the broader planned [source management](../../Design/Skill_Storage.md#shared-profile-discovery)
and [declarative authoring](../../Design/Skill_Authoring.md). Disk instructions
remain untrusted content, not first-party execution trust. Full source
registration/enablement and agent profiles remain gated; discovery cannot
overwrite embedded originals or bypass existing action/grant gates.

## Validation

- [Definitions tests](../../tests/Kora.Definitions.UnitTests/Skills/EmbeddedSkillCatalogueTests.cs)
  prove fixed resources, independent golden package digests, shared-helper
  identity, exact original bytes and unavailable actions.
- [Core skill tests](../../tests/Kora.Core.UnitTests/Skills/SkillPackageTests.cs)
  cover independent canonical vectors, strict schema/encoding, BOM/line endings,
  limits, order, aliases and approval-relevant changes. Expected hashes must not
  be calculated by the same production implementation being tested.
- Resource changes require inspecting the final published
  `Kora.Definitions.dll` with [Test-EmbeddedSkillResources.ps1](../../eng/Test-EmbeddedSkillResources.ps1)
  for **both `win-x64` and `win-x86`**. It reads raw PE resources without loading
  app/interpreter code and compares the exact declared source bytes. Project
  declarations and unit-test assemblies alone are insufficient.
- Follow [development checks](../../CONTRIBUTING.md#development-checks).
  [CI](../../.github/workflows/ci.yml) runs both-RID checks and the unchanged
  **100% portable line/branch coverage gate** across Core, Application, Tools
  and Definitions. No new content kind is grounds to skip those gates.
