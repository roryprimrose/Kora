# Voice- and Text-Driven Skill and Instruction Authoring

Status: bounded instruction/prompt editing, declarative owned-skill and agent
authoring, and reviewed Customizations source writes are implemented through
[Definition Management](Definition_Management.md). Broader conversational/package
authoring and executable skills remain gated.

Discovery, source-read consent, editing, enablement and invocation are separate
operations. Registering or inspecting a root grants none of the later
authorities; see the [source contract](Skill_Storage.md#delivered-bounded-r21-native-inspection)
and [reviewed invocation](Skill_Storage.md).

Related: [Extensibility](Extensibility.md), [Security and Data Flows](Security_Data_Flows.md), [Task Lifecycle](Task_Lifecycle.md), [Acceptance Criteria](Acceptance_Criteria.md).

[Definition Management](Definition_Management.md) owns the shared authoring
surface for skills, prompts, instructions, and agent profiles, including equal
UI/voice control, source selection, direct profile edits, and external reload.
This document adds skill-specific workflow/package checks and the single-file
instruction editing experience, not a second writer or lifecycle policy.

## Outcome

The user can say "Kora, create a skill that explains a deployment failure", refine the behaviour through conversation, review it, test it safely, and enable it for later use.
The same voice, typed-composer, and native review workflow creates and edits
standalone Kora or compatible profile instructions. An instruction is a
single-file definition, so its review surface shows metadata, rendered content,
exact source, and diff without the skill package's file tree, dependencies,
fixtures, test workflow, or skill capability-enablement step.
Changed instruction applicability still requires the common revision review.
Creating and improving skills must not require modifying or rebuilding the Kora application.

Skill and instruction authoring change declarative user data, not executable application code.
Kora cannot modify its own source, installed components, runtime adapters, security policy implementation, or updater through this workflow or any other agent capability.

## Workflow

The delivered bounded native v1 owned-package path now includes a separate
exact-ID/version, two-minute volatile single-use review. Save/List/Reopen remain
inactive. Host-owned review/enable/target/request/preview/confirmation submits
through the existing LocalOnly, tools-none conversation route; the package
cannot authorize itself. Complete source identity/byte revalidation and the
32768-byte serialized input limit fail closed, without truncation or provider
fallback. See [the complete user operation](../docs/owned-skill-authoring.md#review-and-invoke-one-saved-revision).
The additive [native v2 one-tool operation](../docs/owned-skill-authoring.md#author-and-use-one-host-webpageget-skill) selects only the existing host WebPageGet binding, saves disabled and separately reviews/enables one exact owned source, Active target, original request/channel and explicit URL. It uses the same [reviewed tool consumer](../src/Kora.Application/Skills/ReviewedToolSkillInvocationWorkflow.cs) as shared one-tool use, with owned-store admission/freshness rather than shared-root consent.
Neither version delivers generated authoring, general YAML, profile editing, arbitrary tools/effects, references, scripts, hooks or agent execution. Tool-capable invocation retains independent per-action permission and actual tool/model joining ([action admission](../src/Kora.Application/Network/LocalWebPageInvocation.cs)).

The additive [native v3 required literal guidance operation](../docs/owned-skill-authoring.md#required-literal-guidance-inputs)
explicitly declares instruction-only inputs, reusing the owned prompt template
parser/bounds and private controls, not Prompt artifact authority. Both direct
and named owned-skill use review original source, exact bindings and single-pass
expansion through the same canonical local workflow. Old v1/v2 guidance bytes
remain literal; tool parameters and shared/agent guidance-reference policy are
not extended.

1. Capture the voice request and admit an authoring task through normal work management.
2. Clarify the skill's purpose, inputs, expected output, tool dependencies, and success conditions.
3. Generate a staged manifest, instructions, and example fixtures in memory.
4. Explain the proposed behaviour and requested capabilities aloud; show the exact files/diff.
5. Accept voice refinements such as "make it read-only" or "ask me which environment first", producing a new proposal.
6. Run built-in structural/dependency checks and simulated examples with mocked tool results.
7. Present exact voice or UI confirmation for the validated revision and save it disabled to the designated user skill store.
8. Separately present exact voice or UI confirmation to enable the saved revision after capability, tool, resource, destination, and instruction-risk review.
9. Confirm the skill name, version, and how to invoke it: for example, "Kora, use Deployment Explanation".

Voice, UI, and mixed interaction can create, refine, explain, test, and explicitly confirm separate exact save/enable proposals under the unlocked-session baseline.
Saving and enabling are separate proposals; a saved revision remains disabled.
Enablement does not grant a missing tool permission or approve future actions.
High-risk tool actions during actual skill use retain their existing approval requirements.
Every changed proposal invalidates any prior approval of that proposal.

## Standalone Instruction Editing

The instruction editor reuses the skill authoring workspace, conversational
refinement, rendered preview, exact source/diff, validation, dirty-state,
conflict handling, and revision-bound Save confirmation. It omits package-only
controls and writes exactly one bounded `*.instructions.md` file. Voice and
typed input can propose the same edits; neither input route bypasses native
review or the exact Save confirmation.

Kora-owned instructions are saved under `%APPDATA%\Kora\Instructions`.
Compatible instructions discovered in an existing VS Code or VS Code Insiders
profile location may be edited in their original file when the user selects
that exact source. This is a narrow profile-instruction writer, not arbitrary
file-write authority. The host retains the source root, file identity, original
byte digest, and reviewed replacement bytes; immediately before replacement it
reopens and verifies the same non-reparse file and unchanged base digest.
Changed, missing or conflicting bases reject ordinary Save and require explicit
Reload/Overwrite/Cancel under the
[accepted optimistic owned-single-file conflict contract](Definition_Management.md#editing-ownership-and-confirmation).
The user accepts that a content edit after the final comparison may be overwritten;
this is not conditional content CAS. Aliased, redirected, inaccessible or
unauthorised sources remain unavailable, not overrideable conflict choices.
The host chooses the existing path, writes through
the shared atomic preference/file replacement mechanism where applicable, and
never accepts a model-supplied destination.

The current native editor supports only Kora-owned files, strict UTF-8 ≤64 KiB,
original BOM/line endings/source preservation and the bounded v1 scalar metadata
subset documented in [Definition Management](Definition_Management.md#bounded-owned-instruction-and-prompt-editor).
Registered profile originals remain read-only in this increment. The native text
editor works without inference/speech; exact typed/activated controls focus its
same actions and reviews. Dirty closing/shutdown offers Save/Discard/Cancel.
Saving holds the new revision; separate review offers explicit selection only.
Bounded [current owned Instruction body refinement](../docs/instruction-editor.md#refine-the-current-owned-instruction-body) uses this editor's unchanged critique, local-only tool-free proposal, complete comparison and separately confirmed normal Save/readback; changed saved revisions remain disabled ([workflow](../src/Kora.Application/Definitions/OwnedInstructionRefinementService.cs)).
Broader conversational/package authoring, body dictation, rename/restore and expanded applicability remain unavailable. The same native editor also supports immediate
Kora-owned `.prompt.md` versioned saved requests: select Prompt, then use the same
Save/Delete, conflict and separate exact-use reviews. Prompts are request content,
not instructions, skill workflows or policy. Native v1 stays literal; owned native v2 supports required `${input:name}` text only, with a private native field form, complete expanded user-request preview and separately admitted Use. No defaults/expressions/reads/recursive substitution; profile prompt writing remains unavailable.
Production generation remains unavailable and rejected local candidates are not fallback providers. See [required native inputs](../docs/instruction-editor.md#prepare-required-native-prompt-inputs).
The same per-kind owned editor now has bounded literal metadata search, observed-use filters and exact stale-safe row inspection; see [the native inventory controls](../docs/instruction-editor.md#search-observed-owned-metadata).
The native **Validate** button and exact `instructions validate` / `prompts validate` observe the open supported owned draft through the same Save preparation, counting BOM/native-marker overhead and checking existing structural/literal/namespace rules.
Bounded body-free findings are historical observations, not approval, semantic harmlessness or enablement. No source I/O/reload, revision preference write/hold or model/reference execution occurs; drafts and existing held/enabled state remain unchanged. Save separately re-prepares and confirms. See [draft validation](../docs/instruction-editor.md#validate-the-open-draft-without-saving); profile-original editing stays deferred.
Only explicit List/Refresh reads/reconciles existing sources. Search/filter/page changes are in-memory, preserve the original/draft and never activate or select another file automatically. Unknown metadata stays unavailable; observed catalogue state is not disk freshness. No profile/bundled inventory or authoring scope is added.
The native **Save as new copy** increment copies only an inspected supported native owned source/current draft (instruction v1; prompt v1/v2) to a different same-kind owned ID.
Full base/draft/proposal review changes only the declared name, preserves all other draft bytes/BOM/metadata, and creates without replacement.
The original draft remains open; the separate copy is disabled until independently inspected/reviewed for explicit use. No profile reads/writes, implicit legacy repair, inherited authority or automatic reference retargeting are added.
**Disable reviewed use** / `instructions disable` or `prompts disable` (also `stop-use`, including current-name activated controls) separately confirms the exact clean inspected supported native owned baseline (instruction v1; prompt v1/v2) and durably holds new selections without writing/deleting/unregistering its source.
Disabled files remain inspectable/editable; dirty drafts are retained with an explicit Save/Reload prerequisite. Old reviews cannot retarget replacements, and reload/restart/restored bytes cannot re-enable a held revision; a fresh exact current-run use review is required.
Already admitted reasoning keeps immutable captured content, while pending unadmitted choices expire and source/access/privacy/action/egress guards remain mandatory. Failed audit/state readback reports held/unavailable recovery, not success or rollback. Profile-original editing stays deferred.

Saving a standalone instruction does not enable a skill, grant a tool
permission, approve an effect, activate changed guidance, or make its content
authoritative. The saved instruction becomes a new digest-pinned catalogue
candidate after successful rediscovery. Applying it to requests requires
review of its exact revision and scope under the common lifecycle.

## Skill Package Boundary

The MVP authoring writer accepts only a validated package:

- A schema-validated manifest with stable ID, version, inputs/outputs, and registered tool references.
- A purpose and selection guidance describing requests it handles and cases
  requiring clarification or another capability; these are data, not permission
  or reserved command registrations.
- Plain-text instructions and prompts.
- Declarative workflow steps interpreted by Kora's bounded workflow engine.
- Data-only example inputs, expected assertions, and mocked tool responses.

Use safe parsing with size/depth/step limits and reject unknown executable directives.
No inline evaluation, shell commands, scripts, assemblies, native libraries, arbitrary executable paths, installer hooks, or custom validators are allowed.
Registered tool references do not allow a skill to install or redefine the tool implementation.
A skill needing a new executable capability can describe the missing dependency, but installing that implementation is an out-of-band developer/operator action.
This restriction applies to user authoring. [Bundled skills](Built_In_Skills.md) may ship protected first-party scripts installed with the application.
Neither bundled script execution nor user-authored executable skills ship in
the current bootstrap. A future execution model for stored user-approved
scripts and content-bound grants is specified separately in
[skill and task execution design](../docs/skill-and-task-execution-design.md);
this Slice C declarative authoring policy does not itself authorise scripts.

The enabled revision's source-qualified summary can be advertised to the
model; a selected skill loads its pinned instructions/workflow and proposes
only admitted tool/task calls through the host. Required tool/task references
do not carry execution grants. Internal Kora tools remain available without a
skill wrapper, and a bundled script remains its own registered implementation.
See [Commands, Tools, Skills, and Model Interaction](Commands_Tools_And_Skills.md).

## Storage and Versioning

Kora owns `%APPDATA%\Kora\Skills` as its dedicated user skill data store outside its installation and source trees, resolved through the Windows Roaming AppData known folder.
The host chooses paths from validated skill IDs and package structure; the model never supplies an arbitrary destination.
Staging and backup locations are subject to the same boundary.
Reject traversal, unexpected files, reparse-point/hard-link escapes, and existing targets outside the store.

Built-in skills are embedded immutable application resources. Improving one creates only a declarative user adaptation with a distinct ID and explicit attribution; it cannot overwrite the embedded original or shadow reserved controls.
Supported shared profile skills are directly editable in the target design;
an improvement can also create a reviewed Kora-specific fork.
In-place declarative editing uses the dedicated definition writer, with
source-preserving format validation and exact mutation confirmation;
it is never generic filesystem access or part of delivered R21 inspection.
See [Skill Sources and Roaming Storage](Skill_Storage.md).
Only its declarative behaviour can be copied/refined; a bundled script remains a registered protected action, not copied executable content or a user-editable implementation.
User skill updates create validated, digest-pinned revisions.
Active tasks retain the revision they started with; enable a new revision for future tasks only.
Disable or restore a prior revision through the same reviewed data-only registry operations.
The registry cannot select arbitrary executable code, modify trust roots, or change tool/security configuration.
External changes use the common reconciliation pipeline. A dirty voice/UI draft
keeps its base and reports a conflict; save cannot overwrite a changed source,
and reload never enables changed content or silently switches queued revisions.

Kora watches only its admitted roaming artifact roots and recognized profile
instruction roots. Notifications are invalidation hints, never authoritative
file content. Each hint triggers bounded rediscovery with the same root,
UTF-8, size, depth, metadata, reparse, identity, and uniqueness validation as
initial discovery. A valid external skill or instruction change publishes a
new immutable catalogue revision without restarting Kora. New invocations use
that revision; an active invocation retains its pinned snapshot. An invalid,
partial, conflicting, missing, or unsafe changed definition becomes visibly
unavailable and is never silently represented by stale bytes as current.
Rename/replace and notification loss are covered by reconciliation scans.

Any permission or execution grant bound to the prior skill, instruction,
definition, script-set, or dependency digest no longer matches the new
revision. Kora never migrates such authority merely because the source path,
artifact ID, display name, or content later returns to earlier bytes. A fresh
review and grant are required. Unrelated grants whose complete binding does not
include the changed artifact remain unaffected. Standalone instruction
selection itself creates no grant.

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
