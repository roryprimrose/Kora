# Kora Design

Design contracts describe the intended product, not a claim that every
capability is implemented or qualified. Each topic has one policy owner;
supporting pages should link to it rather than reproduce its rules.

## Status, decisions and evidence

| Question | Authoritative document |
|---|---|
| What exists, what is next, and what does it depend on? | [Implementation roadmap](Implementation_Roadmap.md) |
| Which checks gate a merge, capability or release? | [Acceptance criteria](Acceptance_Criteria.md#three-tier-qualification-policy) |
| Which decisions are approved or unresolved? | [Decision register](Decision_Register.md) |
| Which environment trials remain, and what approval do they need? | [Deferred validation](Deferred_Validation.md) |
| Where are dated delivery and trial receipts? | [Archive](Archive/README.md) |
| How are experiments retained or retired? | [Experiment disposition](Implementation_Roadmap.md#experiment-disposition-inventory) |

Normal development needs directly applicable checks. Capability enablement and
final RC qualification are separate tiers; neither CI nor a historical receipt
waives authority, privacy, consent or data-integrity controls.

## Product and architecture

Start with these documents, then follow the topic you need:

1. [Vision](Vision_Statement.md) and [MVP scope](MVP_Scope.md): direction,
   initial-release outcomes and exclusions.
2. [Architecture](Architecture.md): layers, ownership and runtime integration.
3. [Security and data flows](Security_Data_Flows.md): trust boundaries,
   consent, authorization, egress, storage and audit.
4. [Interaction and sessions](Interaction_And_Sessions.md): durable identities,
   routing, voice/UI parity, history and lifecycle.
5. [Acceptance criteria](Acceptance_Criteria.md): required evidence and
   measurable gates.

## Topic owners

| Topic | Contract and supporting documents |
|---|---|
| Commands, actions and definitions | [Interaction and implementation rules](Commands_Tools_And_Skills.md); [canonical tool catalogue](Internal_Model_Tools.md); [invocation contract and reference navigation](Tool_And_Skill_Reference.md) |
| Extension boundaries | [Extensibility](Extensibility.md); contributor [extension guide](../EXTENDING-KORA.md) |
| Bundled skills and execution identity | [Built-in skills](Built_In_Skills.md) |
| Definition sources and authoring | [Definition management](Definition_Management.md) owns UI/voice editing and external reload for skills, prompts, instructions, and agent profiles in Kora-owned/profile files; [skill storage](Skill_Storage.md) and [voice/text skill and instruction authoring](Skill_Authoring.md) supply skill-specific and single-file editing contracts |
| Task execution and recovery | [Task lifecycle](Task_Lifecycle.md); [work management](Work_Management.md) |
| Session action-permission levels | [Permission levels](Session_Permission_Levels.md); proposed, not delivered authority |
| Explicit retained assistant conversation search | [Session search contract](Interaction_And_Sessions.md#explicit-retained-assistant-search); [exact command syntax](../docs/commands.md#search-retained-assistant-answers-explicitly); [native workflow](../docs/windows-and-tray.md#search-retained-assistant-answers) |
| Workspace and windows | [Coordinated workspace](UI_Workspace_And_Windows.md); [information display](Information_Display.md) |
| Shared questions and device recovery | [Interaction fallback](Interaction_Fallback.md) |
| Preferences and setup | [User configuration](User_Configuration.md); [environment setup](Environment_Setup.md); [dependency catalogue](Dependency_Catalogue.md) |
| Local inference | [Qualification and technical plan](Local_Inference.md) |
| Runtime/provider integration | [Runtime feasibility](Runtime_Provider_Feasibility.md) |
| Providers, memory and knowledge | [Provider and memory contract](Model_Providers_Memory_And_Knowledge.md) |
| File/folder ingestion and retrieval | [Ingestion contract](File_And_Folder_Ingestion.md) |
| Calls and proactive behavior | [Call-aware speech](Call_Aware_Speech.md); [proactive interaction](Proactive_Interaction.md) |
| Local speech input and personalization | [Speech-input pipeline, providers and resource profiles](Speech_Input.md); privacy and measurement remain owned by Security and Acceptance Criteria |
| Voice names and phrase design | [Activation name](Activation_Name.md); [proposed phrase catalogue](OOTB_Phrases.md) |
| Instance ownership and build handoff | [Instance coordination](Instance_Coordination.md) |
| Delivery, startup and maintenance | [Distribution and updates](Distribution_And_Updates.md); [installer implementation](../installer/README.md) |
| Branding and visual exploration | [Branding](Branding.md); [ambient UI study](Ambient_UI.md); [offline prototype](Prototypes/ambient-ui.html) |

The [user guide](../docs/readme.md) owns current-release instructions,
[commands](../docs/commands.md) owns exact syntax, and
[settings](../docs/settings.md) owns current preference controls.
Do not copy their availability tables into this index.

## Keeping documentation current

- Change a policy in its topic owner and update affected links or local warnings.
- Update live delivery status in the roadmap, not every introduction.
- Put exact revisions, trial outcomes and failed attempts in evidence records;
  link them from the live register without overwriting old receipts.
- Preserve exact unavailable boundaries. A definition, descriptor, UI selection
  or model proposal never grants execution or transmission authority.
- Label mock, native-seam, physical/provider and installed evidence distinctly.
  Exclusions are not passed capabilities.
