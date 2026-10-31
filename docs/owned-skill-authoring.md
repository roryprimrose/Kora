# Create and use a declarative skill

A skill is reusable task guidance. This editor saves a new immutable version
that cannot be overwritten. It does not enable, select, advertise or invoke
the saved skill. You can create and inspect skills
without AI. **Using one for an answer is unavailable in this build**, because
no local model is approved.
The separate [native observation workflow](#author-and-run-an-ordered-host-observation-workflow) below uses no model and does not qualify or enable one.

Start with [a purpose](#create-a-new-skill-from-a-purpose) or [manual create and save](#complete-operation), then
[review a saved version for use](#review-and-invoke-one-saved-revision).
The [file format](#native-v1-format) is needed only if you inspect the saved
files; the native editor prepares them for you.

## Rename one complete owned native package

Rename changes a saved package's canonical ID, not its readable name, version or unsaved draft. It supports the current native v1/v2/v3/v4 two-file formats only, never a primary-only `SKILL.md` move or a shared/profile/reference-package mutation
([rename proposal](../src/Kora.Core/Skills/OwnedSkillRename.cs), [native writer](../src/Kora.Windows/Skills/WindowsOwnedDeclarativeSkillStore.Rename.cs)).

1. Choose **List** in the owned skill editor, or type `owned skill list`. Save a dirty draft as a separate new immutable version, or explicitly discard it first; Rename never discards it.
2. Select the exact saved package, enter a distinct **New owned package ID**, then choose **Rename selected package**. Alternatively, type `owned skill rename example 1.0.0 renamed`. The same exact command with the current assistant-name prefix is available through eligible activated voice.
3. Review the complete original and proposed files, exact old/new destinations and revisions, unchanged version/readable name/guidance/declarations, and reference/authority consequences. Continue, then separately confirm **Rename only this complete package**. Cancel performs no rename.
4. After verified completion, choose **List**, select the new package and **Reopen**, or type `owned skill reopen renamed 1.0.0`. Native v4 uses its existing separate `owned skill workflow reopen renamed 1.0.0` editor.
   The new identity remains inactive and needs fresh independent use review, enablement and any action permission ([native operation](../src/Kora/OwnedSkillEditorController.Rename.cs), [review controls](../src/Kora/OwnedSkillEditorWindow.cs)).

Only the exact manifest ID changes; complete guidance bytes, version, readable name and supported declarations remain unchanged. Other packages and other versions are retained.
If you edit the open draft while the post-rename list refresh is running, your new text and its unsaved-draft prompt remain intact. An unconfirmed presentation does not undo the native rename; inspect the exact names before taking another action.
New simulated-fixture input during that refresh is also retained. After adopting the renamed source, enter a fresh fixture and separately review any simulation; Rename itself never runs or approves the example.
External references stay bound to the old identity and are not rewritten; old reviews cannot follow the new ID or revive when old bytes are restored ([use-authority regression](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.OwnedSkillRename.cs)).

Publication and old-source retirement are **recoverable steps, not an atomic transaction**. After an uncertain result, do not repeat Rename or overwrite either name.
**List** exposes retained pending renames; select one and choose **Recover selected rename**, or type `owned skill recover-rename example 1.0.0` using the original ID/version. Review both complete packages and separately confirm **Resume only retained rename**.
Both identities remain unavailable while intent is retained, including after restart; unrelated packages remain available ([recovery tests](../tests/Kora.Windows.IntegrationTests/Skills/OwnedSkillRenameTests.cs)).
Pending renames still count toward the saved-version and total-byte limits. Resolve retained renames before adding packages when that reserved capacity is full; recovery refuses an already over-limit inventory rather than releasing a broken catalogue.

Changed source components, destination identity/bytes, parents or corrupt retained intent stop recovery without overwrite or deletion of substitutions. Keep both names and the private local intent for explicit inspection; no automatic rollback/retry is claimed.
If no pending intent remains after an unconfirmed terminal receipt, inspect both exact old/new names with List/Reopen rather than retrying.
Rename does not run a model/tool, register or enable a definition, import executable content, archive a package or mutate a profile original ([dedicated writer](../src/Kora.Windows/Skills/WindowsOwnedDeclarativeSkillStore.Rename.cs), [admission and audit](../src/Kora.Application/Skills/OwnedSkillRenameWorkflow.cs)).

## Author and run an ordered host observation workflow

Use the separate **Kora-owned observation workflow** window, typed commands below, or the same commands with the current assistant-name voice prefix.
This native v4 package declares actual ordered actions, not executable guidance inferred from model text ([declaration](../src/Kora.Core/Skills/ObservationWorkflowDefinition.cs), [host owner](../src/Kora.Application/Skills/ObservationSkillWorkflow.cs)).

1. `owned skill workflow create inspection 1.0.0` opens a volatile draft. Set `owned skill workflow name: Inspect host readiness` and `owned skill workflow description: Read the version and recorded local runtime`.
2. Enter this complete literal definition in the native JSON field, or after `owned skill workflow definition: `:

   ```json
   {"steps":[
     {"tool":"application.get_version","input":{}},
     {"tool":"readiness.get","input":{"offset":0,"count":6}},
     {"tool":"runtime.get_status","input":{"id":"local.inference"}}
   ]}
   ```

3. Choose **Validate**, then **Save**, or `owned skill workflow validate` and `owned skill workflow save`. Review both complete files, then separately confirm **Save disabled revision**. The immutable package remains disabled; saving never runs a step.
4. Choose **List** and select the saved source. **Reopen** or `owned skill workflow reopen inspection 1.0.0` reads that exact version without enabling it. Existing versions cannot be overwritten.
5. Choose **Targets** (`owned skill workflow targets`), select one displayed existing Active conversation, and enter the unchanged original request. Exact typed controls are `owned skill workflow target <session-id>` and `owned skill workflow request: <literal>`. **Next targets** pages without creating or resuming a session.
6. Choose **Review**, or `owned skill workflow review inspection 1.0.0`. Review the complete saved source, original request, exact Active target and all host bounds. Separately choose **Enable** (`owned skill workflow enable`); this temporary one-shot review expires after two minutes. Neither the declaration nor enablement approves any action.
7. Choose **Invoke** (`owned skill workflow invoke`). Each step opens **Approve exact observation step**, showing its exact action/input and original request/target. Approve only that step once, or Cancel. While that exact step review is pending, `owned skill workflow approve-step` or `owned skill workflow deny-step` accepts or declines it through the same gate.
8. Inspect the complete source-labelled result and typed state. `owned skill workflow status` reports whether owned work has joined. **Cancel** or `owned skill workflow cancel` closes further dispatch and joins actual work, including cancellation-ignoring source reads. It never claims physical termination merely from a cancellation request.

The only admitted IDs are `capabilities.list`, `capabilities.get`, `application.get_version`, `readiness.get`, `runtime.list`, and `runtime.get_status`. Inputs and complete results use their existing [registry schemas](../src/Kora.Core/Tools/ReadOnlyCapabilityCatalog.cs).
Pages remain explicit; there is no automatic pagination or guessed continuation ([execution tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.ObservationWorkflows.cs)).

The host assigns step numbers and enforces 1-6 steps, 8192 UTF-8 source bytes, 1024 bytes per input, 4096 total input bytes, 4096 bytes per result and 8192 complete labelled result bytes ([bounds](../src/Kora.Core/Skills/ObservationWorkflowDefinition.cs)).
Invocation has a 15-second total budget, including step reviews. Excess input is rejected, never truncated or silently dropped ([exact-threshold tests](../tests/Kora.Core.UnitTests/Skills/ObservationWorkflowDefinitionTests.cs)).
Excess output fails the entire workflow; earlier observations are not presented as partial success ([complete-result threshold tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.ObservationWorkflows.cs)).

Source replacement/change/removal, dependency observation changes, held/changed session profiles, target changes, original channel loss, policy revocation, privacy/call/ownership loss, denial, expiry and cancellation close the next step and suppress a late complete result ([host tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.ObservationWorkflows.cs)).
Failed or cancelled runs require fresh explicit review; no retry or alternate destination occurs ([native regressions](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.ObservationWorkflows.cs)).

Results stay in this private native window. The selected conversation binds the reviewed operation; its history is neither read nor written ([native forbidden-effect tests](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.ObservationWorkflows.cs)).
These actions read already-recorded host state, without setup probes, network, new tool registration, settings writes, arbitrary file access, scripts, references, branches, loops, models or hosted disclosure. Provider/tool-loop E/Q remains unavailable; native deterministic evidence does not enable it ([workflow owner](../src/Kora.Application/Skills/ObservationSkillWorkflow.cs)).

`owned skill workflow editor` opens this separate owner. **Close** (`owned skill workflow close`) offers explicit discard for an unsaved volatile draft; cancel keeps it open.
Exit/restart/handoff likewise cannot silently discard it. Save first to retain the package. Cancellation retires the original owner: Close/discard and open a fresh workflow before another operation. No draft is autosaved ([controller](../src/Kora/ObservationWorkflowController.cs)).

## Create a new skill from a purpose

This bounded operation implements initial **native v1 literal, local, tool-free** guidance generation through the [existing proposal owner](../src/Kora.Application/Skills/OwnedSkillGuidanceRefinementService.cs). Generation is **unavailable in this build** while the local runtime is unqualified; it never falls back to a hosted model, installs a model or claims qualification.

1. Type `create a skill that explains a deployment failure`, or say it with the current activation name, for example **Kora, create a skill that explains a deployment failure**.
2. The same editor retains the unchanged original purpose and assigns a new ID/version and owned destination. Supply only the missing bounded fields: `owned skill expected-input: User supplied failure text` and `owned skill expected-output: An explanation and next questions`. Enter **none** explicitly if no input is needed. Native fields are equivalent; this does not create v3 template parameters.
3. Optionally replace the host's default readable name and description in the existing editor. Choose **Generate initial guidance**, or `owned skill generate`. Review the unchanged purpose and exact input/output fields, then separately confirm **Generate local guidance proposal**. Before that admission, nothing is sent to a model.
4. Review the **complete generated manifest and guidance**, exact host-chosen owned destination, local runtime, tools none and references none. **Use proposal for next Save** stages only that exact source in memory. **Reject proposal** preserves the original fields and draft, with no save.
5. To refine the staged guidance, enter `owned skill critique: Make the generated steps clearer.` and use the existing **Refine** operation below. It compares against the generated guidance, not the initial placeholder. Original purpose/input/output and catalogue bindings remain attached; the model cannot change metadata or destination.
6. Choose **Validate**, then **Save**, review both complete files and the exact destination, and continue to the **separate native publication confirmation**. Only **Publish exact new version** saves the atomic no-overwrite package, **disabled**. Enablement and invocation remain independent exact-revision operations.

Only the purpose and the two explicit fields enter one local tool-free model turn. Model output must be exactly one JSON `body` string; invented IDs, paths, tools, effects, dependencies or extra fields are rejected, not stripped. Guidance containing commands or attempted instructions remains untrusted literal data, never approval or executable metadata ([generation validation](../src/Kora.Application/Skills/OwnedSkillGuidanceRefinementService.cs), [native v1 document](../src/Kora.Core/Skills/OwnedDeclarativeSkillDocument.cs)).

Complete serialized model input must fit **32768 bytes** and the complete two-file source **64 KiB**; there is no truncation. Missing fields, malformed output, provider failure, cancellation or late results do not save or enable anything. Draft/clarification/name/source/catalogue changes retire proposals; occupied or ambiguous names require choosing another name or cancelling, never automatic overwrite. Cancellation retains originals and the volatile draft; the host owns an ignoring model until it joins ([proposal lifecycle](../src/Kora/OwnedSkillEditorController.Refinement.cs), [atomic store](../src/Kora.Windows/Skills/WindowsOwnedDeclarativeSkillStore.cs)).

## Complete operation

Use typed commands, current-name activated voice (for example, **Kora, owned
skill create daily-review 1.0.0**), or the native editor controls:

1. `owned skill create daily-review 1.0.0` opens a volatile new draft.
2. `owned skill name: Daily review` supplies its readable name.
3. `owned skill description: Review the day` supplies its description.
4. `owned skill guidance: Summarize the completed work and list open questions.`
   supplies literal guidance. Native editing supports multiline guidance.
   Commands in guidance remain text, not additional authoring commands.
5. `owned skill validate` validates the complete native two-file package.
6. `owned skill save` opens a complete review of **both files**, the exact fixed
   destination, local-only/tool-free/reference-free declaration and inactive state.
7. Continue to the **separate native publication confirmation**. Only **Publish
   exact new version** authorizes the directory publication. Voice/typed Save
   does not confirm that final write.
8. `owned skill list`, then select a saved version and use **Reopen**, or use
   `owned skill reopen daily-review 1.0.0`. Reopen reads the actual saved package;
   it does not select it for model use.

`owned skill editor` opens the controls without generating content.
`owned skill cancel` retires authoring authority and cancels pending review/I/O.
The controller retains ownership until the actual operation finishes, even if
an I/O provider ignores cancellation.

Closing, exit, restart and handoff review a dirty draft with **Save**, **Discard**
and **Cancel**. Cancel keeps the draft open and blocks the requested departure.
Save still requires Validate and both native review stages. Privacy, call,
ownership or original-input retirement prevents saving; recovery does not renew
old voice or draft authority. A retired draft can only be explicitly discarded
or retained. After privacy recovers, `owned skill editor` can show that draft
under fresh private presentation eligibility for Discard/Cancel, never to renew
Save authority. Drafts are not autosaved.

## Refine the current instruction-only guidance

The bounded refinement operation supports the current Kora-owned **declarative,
instruction-only draft**: native v1 literal guidance or native v3 required
literal inputs. Tool-declaring v2 skills, shared originals, scripts, fixtures and
general package generation are not supported. The bounded initial v1 operation above is separate. This does not complete the entire Builder.

1. Create a draft and supply its metadata and complete guidance as above. If you
   reopened an immutable version, first enter a **new version** in the native
   editor. The saved source is still checked; refinement never replaces it.
2. Enter the original critique in the native field, or use
   `owned skill critique: Make the steps clearer without changing the inputs.`
   Typed/current-name activated voice preserves the literal critique and its
   original channel eligibility.
3. Choose **Refine**, or `owned skill refine`. Explicitly confirm **Generate
   local guidance proposal** after reviewing the local disclosure.
4. Review the **complete current guidance** and **complete proposed guidance**.
   **Reject proposal** keeps the draft and critique unchanged. **Use proposal
   for next Save** stages only that exact replacement in memory; it does not
   edit the draft, validate it, save it, enable it or invoke a skill.
5. Choose **Validate**, then **Save**. Both existing complete two-file reviews
   remain mandatory, including the **separate native publication confirmation**.
   The manifest bytes, format, ID/version, metadata and v3 required input names
   stay unchanged. Publication creates a new immutable **disabled** package.
6. Use the separate saved-revision review, enablement and invocation below.
   Proposal approval and publication are not consent to use the skill.

Only the unchanged critique and complete current guidance go to the existing
admitted **local tools-none** model route. No history, memory, attachments,
reference execution, scripts, nested agents, hosted/bootstrap fallback or model
installation is added. An unqualified provider remains visibly unavailable.
Guidance and model output are inert untrusted content, never publication,
permission or tool authority. Structural validation does not establish semantic
harmlessness. The complete serialized input must fit **32768 bytes** and the
exact two-file package must fit **64 KiB**; nothing is truncated or stripped.
Native v1 input-looking text stays literal. Native v3 input names cannot be added,
removed or renamed by a proposal.

Draft/critique edits, cancellation, expiry (two minutes), changed/missing
reopened source, occupied target version, original channel loss, privacy or
ownership loss invalidate review. `owned skill cancel-refinement` or **Cancel
refinement** cancels only refinement, retaining the draft and critique. Late
output cannot become a review or Save; the host retains the actual model/resource
ownership until it joins. Rejection, cancellation, unavailable and malformed
output never autosave. Ordinary manual authoring and dirty-draft recovery keep
their existing separate authority.

## Simulate one tool-free example

Use the current editor's **native v1 literal, no-tools draft**, or explicitly **Reopen** one saved v1 revision. This subset does not support native v2 WebPageGet, native v3 required inputs, native v4 observation workflows, reference-bearing definitions, shared skills, mocked tool responses or arbitrary validators ([closed fixture rules](../src/Kora.Core/Skills/OwnedSkillFixture.cs)). Input-looking text and workflow-looking JSON in v1 guidance stay literal evidence; they are not bindings or executable steps.

1. Enter one ephemeral fixture in the editor's simulated fixture field, or use `owned skill fixture: <closed-json>`. For example:

   ```json
   {"input":"Say example only.","assertions":[{"kind":"equals","value":"example"},{"kind":"contains","value":"example"}]}
   ```

2. Choose **Test**, or `owned skill test`. Review the complete manifest, guidance, unchanged fixture and complete serialized local tools-none input. Expected assertions stay host-side, not in the model request.
3. Explicitly choose **Start SIMULATED test**. Only this starts one separately admitted local inference. Setup and **Validate** never start inference. No provider is qualified in this build, so the real operation reports unavailable; deterministic test adapters exercise the host flow, not runtime qualification.
4. If available, inspect the **SIMULATED / PASS** or **SIMULATED / FAIL** result, exact complete output in its own read-only field, and each host assertion result. Matching is case-sensitive ordinal, without trimming, normalization, regex or evaluation. Dismiss the volatile result; it is not saved as conversation facts.
5. **Cancel test** / `owned skill cancel-test` cancels only the example. Cancellation, closing and disposal retain ownership until actual source reads and model work join; late output is not disclosed. The draft stays intact. Completion, rejection or failure retires the test review; restoring source bytes does not renew it.

Generation/refinement and simulation share one busy editor slot. **Cancel test** does not close a generation/refinement review; **Cancel refinement** does not close a simulated-test review. A reviewed generated/refined native v1 proposal can be tested, but simulation neither consumes its separate Save approval nor publishes it ([editor ownership](../src/Kora/OwnedSkillEditorController.cs)).

The [workflow](../src/Kora.Application/Skills/OwnedSkillFixtureWorkflow.cs) accepts at most **8192 UTF-8 bytes** of complete fixture JSON, **4096 bytes** of input, **1–8 assertions**, and **1024 bytes** per nonempty assertion value. Only `input` and `assertions` exist; each assertion has exactly `kind` and `value`. Unknown/duplicate fields, unknown assertions, malformed/control text and oversized input/output are denied, never ignored or default-passed.

The complete model envelope must fit **32768 bytes**; output uses the existing model output bound. Nothing is truncated or automatically rewritten. Source, draft, fixture or saved-selection edits, original-channel/privacy/ownership loss, provider configuration changes, stale host generations and two-minute expiry invalidate the pending run/result ([editor owner](../src/Kora/OwnedSkillEditorController.Fixtures.cs)).

This is **SIMULATED structure/example behavior only**, not a real observation, proof of harmlessness, qualification, trust or permission. No actual tools, network, shell, files, MCP, grants, history, memory, attachments, hosted fallback or source expansion are authorized. Fixtures stay in memory, separate from the unchanged skill format. **Save**, **Enable**, **Use** and any independently admitted real trial retain their own reviews and normal permissions; passing a fixture does none of them.

## Review and invoke one saved revision

For an exact readable-name request spanning owned and registered shared instruction-only skills, use the [named invocation operation](commands.md#invoke-an-instruction-only-skill-by-readable-name).
It includes explicit local inspection, source disambiguation and complete canonical review/confirmation, not automatic selection or invocation. The direct ID/version route below remains available ([named host](../src/Kora/NamedSkillInvocationController.cs)).

Saving, listing and reopening never invoke a model. To use one saved package:

1. Select an exact saved ID/version and choose **Use**, or enter
   `owned skill use daily-review 1.0.0`. Review the complete manifest and
   guidance as inert, untrusted content, with **LocalOnly**, **tools none**
   and **references none** scope.
2. Separately choose **Enable exact reviewed revision once**, or
   `owned skill enable`. This creates only a volatile single use, expiring
   two minutes after review. The saved document remains inactive.
3. Choose **Refresh existing Active conversations** (`owned skill targets`).
   Select one displayed exact target, or use `owned skill target <session-id>`.
   `owned skill next-targets` pages the existing conversations; no session is
   created or resumed.
4. Enter the original request, or `owned skill request: <literal>`. Its exact
   whitespace and content are retained unchanged in that conversation.
5. Choose **Preview complete local invocation** (`owned skill review-use`).
   Review the target and complete serialized local context. Only host
   correlation identifiers are freshly stamped at admission; all policy,
   original request and both complete evidence files are shown.
6. Separately choose **Confirm exact invocation once** (`owned skill invoke`).
   The existing admitted local model route retains its answer, or a truthful
   unavailable/failed terminal, in that exact conversation. An unqualified
   provider stays unavailable; there is no hosted fallback or automatic retry.

The native v1 operation admits no tools, MCP, scripts, hooks, references or nested agents ([instruction-only consumer](../src/Kora.Application/Skills/OwnedSkillInvocationWorkflow.cs)).
A valid saved 64 KiB package may exceed the **32768-byte complete serialized
input** limit. Invocation then fails visibly before submission; no content is
truncated. The exact private non-reparse package directory, both file identities
and complete bytes are revalidated at review, admission, asynchronous model
boundaries and final disclosure.

`owned skill close-use` closes and retires use without changing the draft.
`owned skill cancel`, editor edits, closing, expiry, source changes, privacy,
ownership, channel or target loss retire authority and suppress late output.
Changing target/request after preparing an invocation requires a fresh source
review and enablement. Cancelling or closing retains ownership until actual
read/model work joins, even if cancellation is ignored. If retention is
unconfirmed, inspect that exact conversation before starting any fresh review;
an unconfirmed result is not a rollback.

## Native v1 format

The host resolves the owned roaming application root. The fixed layout is
`Skills\<canonical-id>\<canonical-version>\manifest.yaml` and
`Skills\<canonical-id>\<canonical-version>\instructions.md`.
There are exactly two files, with no executable entry point or examples folder.

The complete manifest for the example is:

```yaml
kora-skill-format: 1
id: daily-review
version: 1.0.0
name: Daily review
description: Review the day
runtime: local
tools: none
references: none
```

All eight fields are required exactly once. Unknown or duplicate fields,
executable/dependency/hook metadata, v1 tools and references are rejected. This is
a closed plain-scalar format, not a general YAML object loader. Names and
descriptions use bounded one-line plain text, not quoting or YAML syntax.
The manifest has eight LF-terminated lines.

Both files require strict UTF-8 without BOMs or hidden format/control characters
(ordinary tabs/newlines in guidance are allowed). The aggregate exact file size
is at most 64 KiB, including metadata; the manifest is at most 4096 bytes.
The readable name is at most 128 characters and description at most 1024.
Guidance is nonempty literal text. It is not executed, imported, dereferenced or
sent to a provider by saving.

IDs are lowercase ASCII letters/digits/hyphens, start with a letter, have no
trailing or doubled hyphens and are at most 64 characters. Windows device names,
paths and private staging names are rejected. Versions have three canonical
decimal components, each at most four digits, with no leading zeros except `0`.
Every immutable version counts toward the 32-package catalogue limit.

## Author and use one host WebPageGet skill

Native v2 adds only a declaration of the existing host `network.get_web_page` capability. It does not add another web utility, tool implementation, permission parser, shared-source registration or network grant. Native v1 and its instruction-only use remain unchanged ([closed formats](../src/Kora.Core/Skills/OwnedDeclarativeSkillDocument.cs), [common reviewed consumer](../src/Kora.Application/Skills/ReviewedToolSkillInvocationWorkflow.cs)).

1. Create a new ID/version and supply name, description and literal guidance as above.
2. In **Host capability declaration**, select **Host WebPageGet only (native v2)**, or enter `owned skill tool web-page-get`. `owned skill tool none` selects v1. The selection is host-defined, not an arbitrary model-supplied tool ID.
3. Validate, Save, inspect both complete files and separately confirm publication. The immutable package is saved disabled; nothing runs. V2 has the same eight fields, with exactly `kora-skill-format: 2` and `tools: network.get_web_page`.
4. Use the exact saved ID/version. Inspect its complete manifest and instructions. Refresh/select one existing Active conversation; enter your original request and one explicit public HTTP/HTTPS URL in the native URL field or `owned skill url: https://example.com/`.
5. Choose **Review exact target and tool revision** (`owned skill review-tool`), then separately **Enable exact reviewed revision once**. Review/enablement is volatile, source-bound and at most two minutes; it does not grant web permission.
6. Preview with `owned skill review-use`. Review the Kora-owned source, target, original channel/request, exact URL, local-only evidence and bounds: at most one host WebPageGet action, two model rounds, 32768-byte complete input and the existing 15-second model deadline.
7. Separately confirm `owned skill invoke`. A tool proposal still opens the canonical native **Exact page approval**. Review the exact session/URL/effect and approve once, or deny. Neither saved content nor revision enablement can approve it.
8. After approval, the actual host tool returns bounded inert page evidence to the same local model turn. Only the model's genuine final answer (or truthful terminal) is retained with your original request in the selected conversation. There is no automatic retry, fallback, second destination or page-to-permission promotion.
9. Close/cancel to stop. Cancellation joins actual reads, tool and model work before release; late work cannot publish an answer after admission retires. Inspect an unconfirmed retained request before any fresh attempt.

These routes share the existing native and exact typed/current-name activated controls. Publication confirmation and per-action approval retain their native/authentication/channel exceptions. Changed target/request/URL after preview, draft/revision, ownership, privacy or call state retires pending authority; repeated confirmation cannot replay a use ([native controller](../src/Kora/OwnedSkillInvocationController.cs), [whole-operation regressions](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.OwnedToolSkills.cs)).

The owned reader checks only the selected package's complete members, exact identities and exact bytes, without opening another package or shared root. Source freshness is checked again across asynchronous approval, resolution/tool execution and final publication. Unknown required fields, extra/uninspected files, references, scripts, executable closure, arbitrary effects/tools, hooks, MCP, hosted transfer, model-selected destinations and general YAML remain excluded ([owned store](../src/Kora.Windows/Skills/WindowsOwnedDeclarativeSkillStore.cs), [action admission](../src/Kora.Application/Network/LocalWebPageInvocation.cs)).

Deterministic fake provider/transport tests prove implementation only. Native/provider/hardware/installed enablement and RC qualification remain unqualified; unavailable local composition fails visibly without network/provider fallback ([native regression](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.OwnedToolSkills.cs), [I/E/Q policy](../Design/Implementation_Roadmap.md#delivery-and-qualification-dependency-policy)).

## Required literal guidance inputs

Choose **Required literal guidance inputs (native v3; no tools)** in the editor,
or `owned skill inputs required-literal`, before Validate and Save. Write
`${input:topic}` in guidance to declare a required literal field. For example,
`Compare ${input:topic} with the original task.` declares `topic`. Native v3
requires at least one such field; `owned skill inputs none` restores literal
guidance. WebPageGet and required-input formats cannot be combined.

The v3 manifest uses `kora-skill-format: 3`, `tools: none`, `references: none`
and the additional exact scalar `inputs: required-literal`. All other fields and
the two-file publication/reopen operation are unchanged. Formats 1 and 2,
including any `${input:...}` text, stay literal: existing saved bytes are never
reinterpreted.

On Use, the same private input controls used for owned prompts collect every
declared field. Enter a value natively, or use typed/current-name activated
voice `owned skill input topic: <literal>`. Values preserve whitespace and
Unicode without normalization. Names use the existing required-literal contract:
at most eight distinct names, each 1–64 lowercase ASCII letters, digits or
hyphens, beginning with a letter. Repeated occurrences share one field.
Every value is nonblank, at most 512 UTF-16 units / 2048 strict UTF-8 bytes.
Expanded guidance is at most 4096 UTF-16 units / 16384 UTF-8 bytes, including
every repeated value. Invalid Unicode, missing/extra bindings, unknown
placeholders, defaults, expressions and dynamic file bindings are rejected.
Substitution is single-pass: `${input:other}` inside a supplied value stays
literal and is never expanded again.

Preview shows the exact original manifest and guidance, input bindings and
expanded guidance, alongside the unchanged original task, current policy,
original channel and exact destination conversation. It uses the same canonical
owned-skill workflow for direct ID/version and readable-name selection. Separate
confirmation still invokes only the admitted local tool-free model and retains
its genuine answer in the original selected conversation. Inputs are private
volatile state, not a Prompt artifact or new permission. Only the original task
and final answer/terminal are conversation content; field values are not
separately saved or included in diagnostics, audit or activity metadata.

Changing a value after preview retires confirmation and requires fresh source
review and enablement. Source edits/deletion, privacy withdrawal, channel,
provider or session changes and cancellation retain existing fail-closed checks
and actual model joining. Closing clears fields and preview. Parameterized
guidance cannot be silently used through incompatible shared/agent guidance
references; their existing explicit-format rejection policy is unchanged.

## Publication and recovery

Only the exact validated generation can be reviewed and confirmed. Editing any
metadata or guidance invalidates Validate and pending review. The original
authoring authority is conjoined with current host admission through publication;
a later native click does not replace it.

Under a bounded private lease the Windows store stages and flushes the complete
two files in a uniquely owned private directory, validates their exact bytes,
and publishes the whole version directory with a no-replace native rename
relative to its pinned ID parent. Both files and their identities are read back
before a saved result is reported. Existing IDs/versions are never overwritten.
Choose a fresh version/ID or cancel on collision.

An interrupted stage is not a saved package. A failure after directory
publication is explicitly **unconfirmed**, not a rollback. Do not retry Save or
delete the destination. List/Reopen that exact ID/version to inspect the actual
saved state. If original authority also expired, explicitly close/discard the
volatile draft and open a fresh editor before List/Reopen; this does not delete
any published package. Saving never registers the root as a shared source and never grants
tools, model access, invocation, enablement or execution authority.

The owning design contracts are [Skill storage](../Design/Skill_Storage.md),
[Skill authoring](../Design/Skill_Authoring.md) and
[Definition management](../Design/Definition_Management.md).
