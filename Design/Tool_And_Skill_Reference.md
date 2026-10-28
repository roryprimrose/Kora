# Kora Tool and Built-In Skill Technical Reference

This page owns the common invocation envelope and directs readers to the
canonical capability contracts. It is not a second tool catalogue or an SDK.

## Scope and Availability

[Internal Model Tools](Internal_Model_Tools.md) owns canonical IDs, schemas,
caller lanes and model availability. [Commands](../docs/commands.md) owns
current exact phrases; the [roadmap](Implementation_Roadmap.md) owns delivery
status. Native controls and C# actions are not automatically model tools.
The current bootstrap JSON proposal protocol is not a generic tool/result loop.

## Common Invocation Contract

Every admitted invocation carries:

- host-owned invocation and request/task lineage;
- logical capability ID/schema, validated typed inputs and target scope;
- deadline and any source-qualified definition revision;
- applicable session/task identities resolved from deliberate addressing,
  never queue position, titles or selected windows.

The host assigns effects/risk and resolves canonical targets. Work sessions,
Windows login sessions and provider conversations are distinct. Model or
definition text supplies no identity, permission or effect authority.

Results distinguish success, pending user decision, blocked/unavailable,
conflict, cancelled, failure and outcome unknown. They contain bounded typed
data, observation time, provenance/classification, evidence references and an
action receipt where applicable. A pending approval is not successful execution.
Validation failures retain prior state and report a bounded reason; do not
clamp invalid input or blindly retry writes after uncertain effects.

Queries return facts without opening a window or speaking. Presentation selects
existing identified content without rerunning the query. The host presenter
applies privacy, call and speech policy.

Each descriptor declares effects/resources, sensitive fields, bounds,
cancellation and confirmation rules. Default model-bound results are at most
64 KiB UTF-8, history pages at most 50 events, and deadlines 60 seconds unless
the registration states otherwise. Complete serialized envelopes, not just
text bodies, must fit. Narrower registered bounds and the independent
[management envelope](Work_Management.md#management-operating-envelope-and-degraded-mode)
take precedence.

Local reads enforce source/identity scope. Read-only does not mean public:
instructions, summaries and results need exact classification/purpose/secret
and destination-egress admission before transmission. Use explicit continuation
and omitted/redacted/truncated markers, never silent loss or unbounded return-all.

Approval replies, choices, sign-in and user-presence evidence bind host-owned
identities; a model cannot submit them to approve itself. Revalidate each
invocation immediately before its effect. A prior result/grant does not create
new intent. Protected-call original-channel restrictions survive model hops
and later UI confirmation.

## 1. Application and Discovery

Use the [canonical discovery/application/readiness schemas](Internal_Model_Tools.md#discovery-application-and-readiness)
and [exact read-only commands](../docs/commands.md#read-only-host-discovery).
Recorded readiness is not a fresh probe or qualification.

## 2. Session, Work, Power, and Readiness Queries

Use [session/history schemas](Internal_Model_Tools.md#sessions-and-history),
[work schemas](Internal_Model_Tools.md#work-scheduling-and-grounded-status)
and [recorded readiness](Internal_Model_Tools.md#discovery-application-and-readiness).

## 3. Work Management and Conversation

[Work Management](Work_Management.md) owns queue limits, resource leases,
concurrency, quarantine and the independent management budget.
[Interaction and Sessions](Interaction_And_Sessions.md) owns targeting/history.
The [canonical work catalogue](Internal_Model_Tools.md#work-scheduling-and-grounded-status)
owns IDs; admission or scheduling never approves execution or egress.

## 4. Audio, Listening, Calls, and Notifications

[Speech/presentation schemas](Internal_Model_Tools.md#structured-interaction-presentation-and-speech)
link to [call policy](Call_Aware_Speech.md),
[microphone consent](Security_Data_Flows.md#microphone-consent-and-enablement-matrix)
and [trusted events](Proactive_Interaction.md). Device selection is not capture
consent, and Kora does not change global volume/mute to force output.

## 5. Context and Clipboard

Use [context schemas](Internal_Model_Tools.md#context-and-artifact-operations),
[clipboard policy](Security_Data_Flows.md) and
[file/source admission](File_And_Folder_Ingestion.md).
Preview/reuse does not authorize provider submission or a fresh source read.

## 6. Typed Configuration

[User Configuration](User_Configuration.md) owns option semantics.
[Configuration schemas](Internal_Model_Tools.md#grants-settings-and-evidence)
admit only registered options, never arbitrary paths, JSON patches, credentials
or policy implementations.

## 7. Grants and Audit Inspection

Use [grant/feedback rules](Internal_Model_Tools.md#grant-and-feedback-rules),
[grant authority](Security_Data_Flows.md#grant-types-and-inheritance) and
[evidence schemas](Internal_Model_Tools.md#grants-settings-and-evidence).
History and file mirrors are not live grant or audit authority.

## 8. Skill Discovery, Registry, and Authoring

[Skill schemas](Internal_Model_Tools.md#skills-and-registered-execution),
[source storage](Skill_Storage.md) and [authoring](Skill_Authoring.md) own
discovery, source-qualified revisions and exact save/enablement boundaries.
Registration, instruction selection and inspection do not execute scripts.
The proposed [definition management contract](Definition_Management.md) owns
direct editing of supported personal profile originals and Kora-owned files,
optional forks, UI/voice parity, and consistent external reload across skills,
prompts, instructions, and agent profiles. The
[canonical catalogue](Internal_Model_Tools.md#skills-and-registered-execution)
owns common `definitions.*` actions and their skill-specific projections;
this reference does not duplicate their schemas or delivery status.

Current host-only `instructions` / `prompts` editor controls and their native
exact Save/Delete, Reload/Overwrite/Cancel and separate use review are documented
in [the native command guide](../docs/commands.md#edit-an-owned-instruction-or-literal-prompt-without-a-model).
These are not model `definitions.*` tool registrations. The shared closed
writer supports Kora-owned instructions and literal saved prompts only;
profile/skill/agent writers, parameters and execution remain unavailable.

## 9. Content Presentation and Viewing

[Information Display](Information_Display.md) owns inert source/rendered
profiles, size/diagram limits and browser/network boundaries.
[Presentation schemas](Internal_Model_Tools.md#structured-interaction-presentation-and-speech)
do not authorize desktop automation or executable renderer installation.

## 10. Connections and Diagnostics

Use [integration/diagnostic schemas](Internal_Model_Tools.md#diagnostics-viewers-and-admitted-integrations).
The proposed bounded daily-log reader admits at most 1,000,000 characters,
not arbitrary paths; the smaller model-result/egress limits still apply.
Sign-in and secrets remain in trusted host/OS flows.

## 11. Host-Only User Flows

[Host-only exclusions](Internal_Model_Tools.md#host-only-operations-never-model-tools)
cover setup, identity, maintenance and ownership flows. A tool may explain or
open a trusted surface but cannot complete its consent/authentication itself.

## 12. Built-In Skill Contracts

[Built-In Skills](Built_In_Skills.md) owns manifests, immutable resources,
script-set/definition digests, content-bound grants and execution admission.
The [Definitions guide](../src/Kora.Definitions/README.md) maps actual resources.
All bundled script invocations remain unavailable.

### Lock the Machine

`kora.session.lock` requests `session.lock`; proposed tool `computer.lock`.
The [standalone work-session binding](Built_In_Skills.md#standalone-lock-work-session-binding)
and [lock workflow](Built_In_Skills.md) own admission. The [composed fixed owner](../src/Kora/BundledFixedActionController.cs) routes direct requests through exact package admission and reports unavailable while its runtime is unqualified; model suggestions open inspection only.

### Shut Down the Computer

`kora.computer.shutdown` / `computer.shutdown`; model operation is proposal-only
`computer.propose_shutdown`. [Power safety](Built_In_Skills.md) owns exact grants,
fresh confirmation, single-use proposal expiry, countdown and pre-effect
revalidation. Current commands open [source review](../src/Kora/SkillPackagesWindowController.cs) and report execution unavailable; no installed contained profile is qualified.

### Restart the Computer

`kora.computer.restart` / `computer.restart`; proposal-only
`computer.propose_restart`. The same [power safety](Built_In_Skills.md) applies
with a distinct task/grant. Computer restart is not application restart.

### Required Read-Only Integration Skill

Slice B requires one admitted read-only MCP integration, but its connector,
skill identity and workflow are unselected. See
[scope](MVP_Scope.md) and [R20](Implementation_Roadmap.md#ordered-outstanding-work).
Bundling never grants remote read or transmission permission.

## 13. Persistent Sessions and Additional Canonical Contracts

### Sessions, Structured Interaction, and Work Evidence

Use [session schemas](Internal_Model_Tools.md#sessions-and-history),
[interaction schemas](Internal_Model_Tools.md#structured-interaction-presentation-and-speech)
and [evidence schemas](Internal_Model_Tools.md#grants-settings-and-evidence).
[Interaction and Sessions](Interaction_And_Sessions.md) owns lifecycle,
activity/retention, shared drafts and foreground voice targeting.

### Discovery, Runtime, Grants, Context, and Supporting Flows

These share the [canonical catalogue](Internal_Model_Tools.md), not a separate
registration namespace. Grouped IDs retain individual effects and gates.

## Deferred and Unspecified Capabilities

[MVP non-goals](MVP_Scope.md) and
[optional/deferred roadmap work](Implementation_Roadmap.md#optional-and-deferred-work)
own scope. No general shell/launch, source self-modification, elevation,
browser/desktop automation, MCP export or unselected connector is admitted
by being mentioned in a design reference.
