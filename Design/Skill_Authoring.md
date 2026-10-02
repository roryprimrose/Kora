# Voice-Driven Skill Authoring

Status: proposed required MVP capability, delivered in Slice C.

Related: [Extensibility](Extensibility.md), [Security and Data Flows](Security_Data_Flows.md), [Task Lifecycle](Task_Lifecycle.md), [Acceptance Criteria](Acceptance_Criteria.md).

## Outcome

The user can say "Kora, create a skill that explains a deployment failure", refine the behaviour through conversation, review it, test it safely, and enable it for later use.
Creating and improving skills must not require modifying or rebuilding the Kora application.

Skill authoring changes declarative user data, not executable application code.
Kora cannot modify its own source, installed components, runtime adapters, security policy implementation, or updater through this workflow or any other agent capability.

## Workflow

1. Capture the voice request and admit an authoring task through normal work management.
2. Clarify the skill's purpose, inputs, expected output, tool dependencies, and success conditions.
3. Generate a staged manifest, instructions, and example fixtures in memory.
4. Explain the proposed behaviour and requested capabilities aloud; show the exact files/diff.
5. Accept voice refinements such as "make it read-only" or "ask me which environment first", producing a new proposal.
6. Run built-in structural/dependency checks and simulated examples with mocked tool results.
7. Obtain approval for the exact validated revision and save it to the designated user skill store.
8. Separately ask whether to enable the saved revision after capability review.
9. Confirm the skill name, version, and how to invoke it: for example, "Kora, use Deployment Explanation".

A normal declarative skill save/enable can be confirmed verbally after an unambiguous summary; no typing is required.
Enablement does not grant a missing tool permission or approve future actions.
High-risk tool actions during actual skill use retain their existing approval requirements.
Every changed proposal invalidates any prior approval of that proposal.

## Skill Package Boundary

The MVP authoring writer accepts only a validated package:

- A schema-validated manifest with stable ID, version, inputs/outputs, and registered tool references.
- Plain-text instructions and prompts.
- Declarative workflow steps interpreted by Kora's bounded workflow engine.
- Data-only example inputs, expected assertions, and mocked tool responses.

Use safe parsing with size/depth/step limits and reject unknown executable directives.
No inline evaluation, shell commands, scripts, assemblies, native libraries, arbitrary executable paths, installer hooks, or custom validators are allowed.
Registered tool references do not allow a skill to install or redefine the tool implementation.
A skill needing a new executable capability can describe the missing dependency, but installing that implementation is an out-of-band developer/operator action.
This restriction applies to user authoring. [Bundled skills](Built_In_Skills.md) may ship protected first-party scripts installed with the application.

## Storage and Versioning

Kora owns `%APPDATA%\Kora\Skills` as its dedicated user skill data store outside its installation and source trees, resolved through the Windows Roaming AppData known folder.
The host chooses paths from validated skill IDs and package structure; the model never supplies an arbitrary destination.
Staging and backup locations are subject to the same boundary.
Reject traversal, unexpected files, reparse-point/hard-link escapes, and existing targets outside the store.

Built-in skills are embedded immutable application resources. Improving one creates only a declarative user adaptation with a distinct ID and explicit attribution; it cannot overwrite the embedded original or shadow reserved controls.
Shared profile skills are also read-only to Kora; a voice-requested improvement creates a reviewed Kora-specific fork rather than changing the shared source.
See [Skill Sources and Roaming Storage](Skill_Storage.md).
Only its declarative behaviour can be copied/refined; a bundled script remains a registered protected action, not copied executable content or a user-editable implementation.
User skill updates create validated, digest-pinned revisions.
Active tasks retain the revision they started with; enable a new revision for future tasks only.
Disable or restore a prior revision through the same reviewed data-only registry operations.
The registry cannot select arbitrary executable code, modify trust roots, or change tool/security configuration.

## Validation and Trial Use

Built-in validation checks schema, dependency availability, capability declarations, bounded workflows, and fixture assertions.
Mocked tests never invoke actual tools or cause side effects.
If an example uses model inference, the example payload still follows normal local/remote egress policy.
Passing these tests is evidence of structure/example behaviour, not proof that the skill is universally correct or harmless.

An optional real trial is a separately admitted task with the exact skill revision and selected context.
It uses ordinary tool, egress, resource, and action approvals.
Do not automatically exercise write tools merely because the user asked to test a skill.

## Non-Self-Modification Rule

This is a non-overridable host policy, not a prompt to the model:

- No agent operation can edit Kora's application code or bundled executable components.
- No skill, approval, queued request, script, MCP call, Git operation, or build command can override that prohibition.
- Model-directed mutation of Kora's source repositories, build outputs, installed binaries, executable adapter locations, or updater configuration is denied.
- Application installation/update is a separate user-initiated maintenance channel, inaccessible to the agent.
- Other user repositories can be supported later only if their writes and execution cannot reach protected Kora resources.

See [Application Integrity](Security_Data_Flows.md#application-integrity-and-no-self-modification) for enforcement and limitations.
If a requested skill requires changing Kora, explain the unsupported capability and leave implementation to the developer; do not perform or schedule the change.
