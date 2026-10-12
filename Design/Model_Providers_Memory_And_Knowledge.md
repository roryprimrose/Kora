# Model Providers, Memory, and Grounded Knowledge

Status: accepted product direction, bounded provider-neutral host controls and volatile provider-policy/review workflow
implemented, reviewed user-memory admission, bounded session-only durable storage and native/exact memory management implemented, and remaining proposed architecture. Provider
qualification, broader memory recall, knowledge ingestion, retrieval ranking, and hosted
handoff described here are not complete or advertised by the current
bootstrap.

Related: [Architecture](Architecture.md),
[Local Inference](Local_Inference.md),
[Runtime and Provider Feasibility](Runtime_Provider_Feasibility.md),
[Human Interaction and Persistent Sessions](Interaction_And_Sessions.md),
[File and Folder Ingestion and Grounded Reasoning](File_And_Folder_Ingestion.md),
[Security and Data Flows](Security_Data_Flows.md), and
[Implementation Roadmap](Implementation_Roadmap.md).

## Native conversation admission consumer — 2026-10-12

The [exact-session composer](Interaction_And_Sessions.md#delivered-exact-session-conversationcomposer-foundation--2026-10-12)
now consumes configured `ModelTurnHost` admission with fresh session/app policy,
qualification and immutable tuple capture, never the buffered bootstrap
reasoner. Only the newly frozen original user body plus trusted versioned host
policy enters a complete ≤32768-byte envelope; no retained conversation,
memory, attachment, evidence or tool-result assembly is implied.

Production no-model/no-qualified-adapter resolution retains original input
with a separate truthful HOST Unavailable terminal and matching task receipt.
Even a controlled future qualified admission reports GenerationNotEnabled
without executing a provider. No authentication, egress, live provider,
rejected Ollama candidate, send-once override or provider-output parser is
enabled. Installed-local desired initial service is not inference consent.

## Product Decision

Kora supports qualified local inference through Ollama and separately
qualified hosted inference through GitHub Copilot, Anthropic Claude, and
OpenAI Codex. It does not require every user or session to use local and hosted
inference, or to configure every hosted service.

Provider choice is explicit and host-owned:

| Session mode | Initial provider | Hosted handoff |
|---|---|---|
| Local only | Qualified Ollama model | Prohibited |
| Local first | Qualified Ollama model | Kora may offer an exact, reviewed handoff; the user decides |
| Hosted preferred | Qualified configured Copilot, Claude, or Codex destination | Used for admitted turns under the configured remote-processing policy |

**Local first** is the recommended default after both providers are qualified.
**Local only** remains a complete supported mode rather than a degraded
hosted session. A user may also choose a provider explicitly for a session or
new task.

One provider processes one model turn. Kora does not silently blend providers,
race them, or switch midway through a request. A local-to-hosted transition is
a new admitted turn with a new context envelope, destination decision, and
visible provenance.

Provider selection never changes tool authority. Ollama, Copilot, Claude, and
Codex receive the same admitted tool descriptions and return proposals through
the same host authorization, approval, execution, result-sanitization, audit,
cancellation, and session-ownership boundaries. A service's native agent,
tool, memory, workspace, shell, file, connector, or subagent features remain
disabled unless Kora can route them through the corresponding host-owned
control point.

## Hosted Service, Model, and Compute Profile

Kora treats **Copilot**, **Claude**, and **Codex** as distinct hosted services,
not as interchangeable names for a model. Each has its own adapter,
authentication/account state, terms and usage boundary, capability catalogue,
qualification evidence, and availability. Enabling or signing in to one never
enables another.

The application-level hosted profile contains:

| Field | Meaning |
|---|---|
| Hosted service | Stable service ID: `copilot`, `claude`, or `codex` |
| Model | Exact provider model ID from that service's current admitted catalogue |
| Reasoning level | Exact provider-defined reasoning/effort option for that model, when selectable |
| Context size | Exact provider-supported context-budget option for that model, when selectable |

These are request-shaping choices, not authority. They do not enable hosted
execution, sign in an account, approve egress, increase a paid quota, grant a
tool, or override session ownership, privacy, call, lock, or permission policy.

### Provider-advertised capability catalogue

Every adapter publishes a host-normalized, immutable capability snapshot with:

- stable service ID, adapter/runtime version, account reference and region or
  tenancy classification where the service exposes them;
- catalogue revision, observation time, expiry/refresh rule, and truthful
  unavailable or stale state;
- exact model IDs and display names available to the signed-in account;
- for each model, the exact selectable reasoning levels and context sizes, or
  an explicit **provider managed/fixed** value when the service does not expose
  that control;
- model retirement/deprecation, modality, tool/streaming, quota and request
  constraints required for admission; and
- the provider-native values to send for each normalized choice, retained
  inside the adapter rather than accepted from UI or model text.

Kora does not maintain a product-wide enum of model names, infer capabilities
from marketing names, or assume that two services use equivalent reasoning
levels. A normalized choice therefore has a stable opaque option ID, provider
display label and optional provider-supplied description. Labels such as
**Low**, **Medium**, or **High** are shown only when the applicable adapter
maps them to exact supported provider values for the selected model.

Context size is the maximum model-input budget Kora may assemble for that
turn, including system instructions, user content, history, evidence and tool
catalogue overhead. The picker shows exact token counts or provider-defined
presets. Kora may send less, but never more. Output limits and Kora's own
smaller security/resource envelope remain independent hard ceilings and cannot
be raised by selecting a larger provider context.

Catalogue refresh occurs on sign-in/account change, explicit refresh,
adapter/runtime update, provider expiry guidance, and an availability rejection
that indicates catalogue drift. Refresh is read-only: it never silently changes
a saved selection. A missing, retired, no-longer-entitled, stale, partially
described, or internally inconsistent choice is **Unavailable**. Kora shows the
saved and effective states and requires a new explicit selection; it does not
substitute a nearby model, reasoning level, context size, service, or local
provider.

### Application defaults and per-service selections

The initial desired application service is **Local if a supported Ollama installation is observed; otherwise Unset**. Unknown installation is Unset, never Copilot or another hosted service.
This is independent of the unchanged **LocalOnly** safety-mode default. Installation does not establish runtime/model readiness, generation consent, account identity or egress authorization.

The delivered application default is `providers.app.default-service`: Unset, Local, Copilot, Claude or Codex. Each service retains a complete desired model/reasoning/context tuple. The aggregate `providers.app.profile` route saves or resets that tuple atomically. Exact `providers.app.model`, `.reasoning` and `.context` edits review a complete resulting tuple before separate confirmation.

The broader hosted-field catalogue remains the proposed logical layout:

- `providers.hosted.default-service`;
- `providers.hosted.<service-id>.model`;
- `providers.hosted.<service-id>.reasoning`; and
- `providers.hosted.<service-id>.context`.

Selecting a service changes which service-specific tuple is edited; it does
not erase the tuples for the other services. Model selection refreshes the
available reasoning and context choices. If the existing dependent choice is
not valid for the newly selected model, Save remains unavailable until the
user chooses an advertised value or the explicit provider-managed value. UI
pickers and verbal discovery are populated from the same admitted catalogue
snapshot and configuration draft.

A confirmed save binds the account reference, catalogue revision and complete
tuple. Account changes never carry a tuple forward merely because model labels
match. Credentials and tokens remain in the service's secure sign-in flow and
are never settings values.

### Delivered bounded app provider-profile configuration

[Settings > Providers](../src/Kora/SettingsWindow.axaml) and [exact typed/current-name activated commands](../src/Kora.Application/Configuration/ProviderProfileCommand.cs) use [one audited workflow](../src/Kora.Application/Configuration/ProviderProfileConfigurationService.cs).
They expose discovery/status, explicit desired-service selection, complete exact tuple save, app-service reset and selected-service tuple reset.

Individual app field edits use the same current saved complete tuple, host-issued preview and audited writer, not a sparse preference or second authority. Exact typed/current-name activated setters bind the ID/account/catalogue; separate `confirm providers.app.profile <host-issued-id>` or native confirmation saves the reviewed whole tuple. Cancel writes nothing; no field-only reset exists.

Native review shows the selected service, saved and proposed complete tuples, field, dependent validity, preference revision and inert catalogue provenance. Reasoning/context edit the saved model; a model edit retains both exact dependent IDs only if still supported. A missing/stale base or incompatible result explicitly requires the existing complete tuple draft/save. Nothing selects a default or substitutes a provider-managed value.

Field edits preserve the desired default service, all other service tuples and session overrides. Native selection changes and current host/privacy/session/generation/call, app preference or account/catalogue/expiry changes reject confirmation. Existing complete-profile, desired-service and selected-service reset routes remain available and compatible.

The [portable immutable catalogue](../src/Kora.Core/Providers/ProviderCapabilityCatalogue.cs) contains typed opaque IDs, exact model-specific reasoning/context choices, explicitly advertised provider-managed values, account/catalogue revisions and validity.
Bounds are four services, 64 models/service, 16 choices/control, 64 outstanding host previews, 128-character ASCII IDs, 256-byte inert labels, 8,192-byte preferences and complete 64 KiB discovery output. Oversized output fails, never truncates; preview capacity requires explicit refresh without eviction.

The [production catalogue](../src/Kora.Application/Configuration/UnavailableProviderCapabilityCatalogue.cs) honestly supplies **no models** for unavailable services. Hosted sign-in is **Unavailable**: no qualified auth adapter exists.
Selection neither authenticates nor uses machine credentials. No adapter, authentication, acquisition, network/model call, egress approval, grant, memory or context-budget override is implemented.

The installation-only [Windows observation](../src/Kora.Windows/Dependencies/WindowsOllamaInstallationObservation.cs) reuses the supported per-user executable path from setup but not `InspectAsync`: that existing method queries version/tags and can describe an unknown responding endpoint.
Regular-file metadata establishes only installation observation; missing, inaccessible or reparse state cannot establish readiness. Settings does not start a server or probe an endpoint.

Saved selections are never silently replaced after installation removal, model retirement, account change, catalogue drift or expiry. A service may persist as desired while unresolved.
A tuple is effective only when every exact ID matches the current account/revision/catalogue; dependent choices are never substituted. Resetting one service's tuple retains all others; app-service reset re-observes installation and retains all tuples.

Schema-1 `provider-profiles.json` and its unconfirmed-write fence use `IApplicationDataPaths` and the shared atomic store. Malformed, unknown, noncanonical, oversized or unconfirmed saved state throws `InvalidDataException`.
Original intent, current owning unlocked host, session/generation, privacy, channel/call revision, requested/terminal typed audit, save/readback and completed control receipt precede confirmation. Interrupted outcomes require explicit inspection/repair, not retry or rollback claims.

The [provider-neutral host consumer](../src/Kora.Application/Dependencies/ModelTurnHost.Profile.cs) re-resolves the app tuple on subsequent eligible policy-bound turns, retaining the existing mode gates.
Admitted turns capture immutable profiles and the original live issuing request, not a completed child activity. App edits do not reroute/cancel/retry them; account/catalogue/installation loss before dispatch fails closed without substitution.

This does not reroute the existing buffered local bootstrap reasoner. No real provider-neutral adapter is composed; qualification and final-request egress gates stay closed, including with resolved fake hosted tuples.
Durable exact-session inheritance/overrides are delivered below. Generic unscoped model directives and explicit one-turn changes remain deferred, never app-scope aliases.

- [Domain/persistence tests](../tests/Kora.Core.UnitTests/Providers/ProviderProfilePreferencesTests.cs) and [audit/failure/tuple matrix](../tests/Kora.Application.UnitTests/Configuration/ProviderProfileConfigurationServiceTests.cs) exercise exact state.
- [Actual UI routing](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.ProviderProfiles.cs) and [subsequent-turn consumer tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.ProviderProfiles.cs) exercise admission and immutable capture.
- [Individual field failures/atomic save](../tests/Kora.Application.UnitTests/Configuration/ProviderProfileFieldConfigurationTests.cs) and [real native/exact/current-name activated review](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.ProviderFields.cs) cover compatible retention, incompatible-base recovery, cancellation, stale confirmation and no provider execution.
- [Private SQLite/composition tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteProviderProfileConfigurationTests.cs) use deterministic installation/catalogue boundaries, not live endpoints/accounts/devices.

Runtime/provider experiment disposition remains **retain** because
`eng/RuntimeValidation.Common.ps1` consumes its profiles and RT1/RT2/PV1 retain
unique native/account evidence. This does not retain the retired manual-proof
harness or require more trials for rejected local-inference candidates.

### Session overrides and effective resolution

#### Delivered exact-session sparse policy

**Sessions > Selected-session provider overrides** and [exact typed/current-name activated syntax](../src/Kora.Application/Configuration/SessionProviderProfileCommand.cs) use [one original-user review service](../src/Kora.Application/Configuration/SessionProviderProfileConfigurationService.cs). Native review and separate confirmation name the immutable session ID, current generation/policy revision, saved sparse fields, proposed fields and current account/catalogue provenance.

Each field defaults to **Inherit app**. Explicit service selection may remain desired/unresolved; model/reasoning/context selections require exact current advertised objects and their service/account/catalogue/model binding. Effective resolution first chooses the session service or app service, then that service's app tuple and sparse fields. Cross-service/account/revision/model-dependent choices fail closed; missing effective fields never substitute another tuple.

Single-field model/reasoning/context review derives a complete candidate from the current resolved effective tuple, retaining both other exact choices. Native dependent pickers use the effective model, independently of the complete-profile recovery draft. The short exact setter supplies only the addressed field ID, exact session, account and catalogue revision; separate confirmation reviews old/new whole effective tuples and saved/proposed sparse policy.

Only the addressed field becomes explicit; all other null/explicit values and service remain unchanged. Incompatible models, unresolved effective tuples or rebinding previous explicit dependents require reset or complete-profile review, never first/default choices or silent clearing. The sparse result must resolve to the reviewed candidate. Retained null fields continue to inherit later app edits rather than freezing preview values.

Individual reset clears only that field; **Use app defaults for this session** reviews clearing all four. Session writes never change app preferences. App edits affect inherited fields on later turns without rewriting session records. No exact/selected eligible session means select one, not an app fallback. Existing `providers.app.*` routes stay explicitly app-only.

[Interaction schema 9](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.ProviderProfiles.cs) stores bounded canonical schema-1 sparse policy in the existing private consolidated SQLite authority, with original-user intent, host-issued revision, exact generation, typed requested/terminal audit, save/readback and terminal control receipt.
The requested receipt precedes a provisional held save. Exact readback precedes atomic publication of policy authority, terminal audit and successful control receipt. Unresolved requested/save outcomes hold the old/new desired policy across restart; cancellation after known completed COMMIT never claims rollback.

Legacy sessions have no policy row and inherit; malformed/unknown/partial/oversized or missing audited rows do not inherit. Migration 8→9 validates old authority and preserves immutable history/source bytes and audit digests. Disposition inventories and hashes the policy row; disposition and retention deletion remove session overrides from owned SQLite cells/journal, never app preferences or independent sessions.

Native selection epochs, original activated input/call/privacy/private ownership, generation/policy/app revision and catalogue/account drift invalidate confirmation. Unknown storage/audit/receipt outcomes remain unavailable with explicit inspection/repair guidance, not retry/reset, rollback or substitution claims. Passive displayed drafts do not supply policy/dispatch authority.

[ModelTurnHost](../src/Kora.Application/Dependencies/ModelTurnHost.Profile.cs) actually reads the addressed session policy for subsequent provider-neutral admissions and captures immutable effective tuple/catalogue binding and session-policy revision. Admission rechecks the policy observation; later session/app edits never reroute/retry/cancel an admitted turn. Before dispatch, current private session/task/control authority and newly observed catalogue/account/installation are revalidated without re-resolving desired policy.

Production catalogues still advertise **no models**; model-dependent native save controls stay unavailable. There is no authentication, adapter composition, network/inference, egress, general execution, recall/context assembly or larger-resource permission. LocalOnly, LocalFirst/manual exact handoff and independent envelope ceilings remain unchanged. The buffered local bootstrap reasoner remains untouched, not rerouted by these controls.

[Portable sparse rules](../tests/Kora.Core.UnitTests/Providers/SessionProviderOverridesTests.cs), [shared review tests](../tests/Kora.Application.UnitTests/Configuration/SessionProviderProfileConfigurationTests.cs), [real private SQLite migration/failure tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteSessionProviderProfileTests.cs), [native consumer](../tests/Kora.Windows.IntegrationTests/SessionsProviderProfileTests.cs) and [turn capture tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.ProviderProfiles.cs) qualify this bounded feature, not provider qualification.

[Single-field workflow tests](../tests/Kora.Application.UnitTests/Configuration/SessionProviderFieldConfigurationTests.cs) and actual native/private composition and typed/current-name activated routes retain the same schema, writer, audit/readback, preview bound and subsequent-turn consumer. Retained experiments are unchanged; these deterministic assertions do not establish RT1/RT2/PV1, offline CPU/inference, containment, hardware, installation or accessibility qualification.

The following broader natural-language/one-turn examples remain proposed, not interpreted commands. In particular, “Use Claude and summarize” does not execute a combined directive or silently change the app profile.

A Kora work session may store a sparse override of hosted service, model,
reasoning level, and context size. Each field is either **Inherit app setting**
or an exact catalogue-backed value. The effective profile for a new hosted turn
is resolved in this order:

1. explicit admitted turn choice, when the interaction intentionally requests
   a one-turn change;
2. the addressed session's override for each field;
3. the application-level service and that service's saved tuple.

**Session is the default mutation scope.** An unscoped model directive such as
"Use Claude" or "Use high reasoning and summarize this" updates the addressed
session. The host validates and commits that session-policy revision before it
admits the remaining requested action, which is then bound to the resulting
effective profile. Failure, cancellation, stale authority, or uncertain audit/
storage outcome while applying the override prevents the action from running.

One-turn scope requires explicit language such as **for this request only** or
**once**. Application scope requires explicit language such as **make this my
app default**. A command that does not address an existing eligible session
cannot silently fall back to application scope; Kora asks the user to select or
create a session.

An inherited model/reasoning/context value always comes from the effective
service. Changing only the session service therefore uses that service's saved
application tuple unless the session also overrides dependent fields. Invalid
cross-service combinations are rejected; IDs and labels from one catalogue
cannot be reinterpreted in another.

**Use app defaults for this session** clears the complete session override.
Clearing one field restores inheritance only for that field. Session overrides
are durable session policy, visible in session details, included in session
retention/deletion, and never promoted back to application defaults implicitly.
Application-setting changes affect inherited fields on later turns but do not
rewrite session records.

Turn admission captures one immutable effective profile and catalogue revision.
Changing application defaults or session overrides never mutates, retries,
reroutes, or cancels an admitted turn. The next turn resolves again. If the
captured service becomes unavailable before dispatch, that turn fails
explicitly and requires a new choice; no fallback occurs.

### Voice and UI interaction

The shared configuration workflow provides equivalent UI, exact-command and
activated-voice operations. Examples include:

- "Use Claude by default."
- "Which models can I use with Copilot?"
- "Use `<advertised model label>` with high reasoning and the 128K context."
- "For this request only, use `<advertised model label>`."
- "For this session, use Codex."
- "Use the app model for this session, but set reasoning to low."
- "Use app defaults for this session."
- "What model is this session using?"

Natural language may identify a displayed option, but the host resolves it to
one exact option ID in the current draft catalogue. Ambiguous labels produce a
question; unavailable values produce an explanation and the currently
available choices. The model cannot invent or self-select a service, model,
reasoning level, context size, scope, account, or catalogue revision.

Before confirmation, UI and voice feedback show the scope (**This session** by
default, **App default**, or explicit **Next turn only**), service, account label safe for
display, model, reasoning, context, inherited fields, remote-processing effect,
and catalogue freshness. After a change, Kora reports the saved override and
the effective profile separately. Session headers and every hosted response
show service/model provenance; reasoning and context configuration are
inspectable in turn details without exposing hidden reasoning content.

Remote-enabling changes and account/service changes follow the existing exact
confirmation and egress rules. Protected-call restrictions may require a fresh
UI-originated change. Read-only discovery remains available when mutation is
blocked.

### Adapter and qualification boundary

Copilot, Claude, and Codex adapters implement the same provider-neutral turn
protocol and catalogue contract, but qualify independently. Each must prove:

- final outbound request/context mediation, host-only tool authorization and
  sanitized tool-result continuation;
- no unmediated provider tools, automatic repository/workspace collection,
  durable provider memory, hidden retries, or cross-session conversation reuse;
- exact mapping and rejection behavior for model, reasoning and context
  options, including catalogue drift between review and dispatch;
- streaming, cancellation, timeout, quota/rate-limit, authentication, cost/
  usage reporting, diagnostics, storage and endpoint behavior; and
- isolation across accounts, sessions, concurrent turns and management work.

Failure or missing evidence disables only that adapter or affected capability.
It never weakens the common host contract or causes another hosted service to
be selected. Provider-specific SDK/CLI limitations may make reasoning or
context read-only, provider-managed, or the whole adapter unavailable.

## Local-First Handoff

Kora does not use a model's self-reported confidence as authority to disclose
content or select a hosted provider. Model confidence is not reliably
calibrated and can be confidently wrong.

Kora may offer hosted handoff only after an observable, typed condition such
as:

- the selected local runtime or required capability is unavailable;
- the complete admitted request cannot fit the qualified local context budget;
- required structured output repeatedly fails host validation within a fixed
  retry budget;
- a host-owned acceptance check establishes that the local result is
  insufficient;
- the task requires an explicitly hosted-only capability; or
- the user asks to use Copilot or requests a hosted second opinion.

A handoff offer states why local processing could not continue, identifies the
hosted destination, and previews the exact content classes and knowledge
excerpts proposed for transmission. The user can approve, decline, remove
items, continue locally with reduced scope, or start a separate hosted
session.

Provider unavailability, authentication failure, quota, timeout, cancellation,
and denied egress are explicit outcomes. Kora never converts them into a
success-shaped local or hosted fallback.

## Context Is Not Learning

Kora distinguishes four mechanisms that are often described as "memory":

| Mechanism | Lifetime and authority |
|---|---|
| Turn context | The bounded request sent to one provider for one model turn |
| Session history | Durable user-visible messages, tasks, decisions, and evidence associated with one Kora session |
| User memory | Small, deliberate, inspectable facts or preferences retained by Kora for later sessions |
| Knowledge sources | User-configured files or folders indexed locally and retrieved as cited evidence |

Ollama inference does not update model weights from conversations. Restarting
Ollama does not preserve conversation knowledge unless Kora retained and
selected it. Continuous training or fine-tuning is not the mechanism for
personal facts, preferences, project decisions, or source documentation.

Kora owns context selection for every provider. Provider conversation state,
SDK transcripts, caches, and native memory features cannot become a second
authority or an unreviewed durable store.

## User-Governed Durable Memory

Durable user memory is optional application data, not hidden model learning.
The model may propose a candidate memory, but only Kora can admit it.

Examples include:

- a preferred response style;
- an explicitly named project or role;
- a stable user-approved workflow preference; or
- a user instruction to remember a decision across sessions.

Kora does not automatically convert ordinary conversation, inferred traits,
credentials, health information, secrets, transient tasks, or model-generated
claims into durable memory.

Every memory records a Kora-owned identity, value or typed payload, source
lineage, creation time, scope, review state, and retention/deletion state.
Users can:

- say or select **Remember this**;
- review the exact proposed memory before admission;
- list, inspect, edit, disable, and forget memories without a model;
- see when a memory was used for a request; and
- prevent a local memory from being sent to a hosted provider.

Memory scopes are explicit:

- **Session**: remains part of that session and follows session deletion.
- **Device profile**: may be selected for later sessions under the active
  Windows profile.
- **Project or knowledge-source scope**: applies only when that exact
  Kora-owned scope is active.

Memory retrieval uses host policy and typed scope before relevance. A
semantically similar memory from another session, profile, project, or
revoked source is not eligible. Unknown scope or ownership fails closed.

Memory content lives under paths supplied by `IApplicationDataPaths` and uses
the durable storage/lifecycle boundary, not provider storage or preference
JSON intended for simple settings. Security-sensitive configuration continues
to use its authoritative domain policy and audit path rather than becoming a
free-form memory.

### Delivered Native Memory Management Surface - 2026-10-09

The [shared management service](../src/Kora.Application/Memory/MemoryManagementService.cs) composes the existing admission workflow and `IMemoryStore`, without adding a model tool or proposal trigger.
Native **Sessions > Session memories** and exact typed/activated commands use the same service and fresh original-user [control intent](../src/Kora.Application/Hosting/SessionWorkspaceService.cs).

The [exact grammar](../src/Kora.Core/Commands/MemoryCommand.cs) provides `memory help`, `memory list <session-id>`, `memory inspect/get <session-id> <memory-id> <revision>`,
`memory review <session-id> <memory-id> <revision> accept|reject`, `memory admit/disable/forget <session-id> <memory-id> <revision>`, and `memory edit/set <session-id> <memory-id> <revision> <class> "<exact value>"`.

- [List results](../src/Kora.Core/Commands/MemorySummary.cs) contain identity, revision, exact session scope, review/retention state and creation time; no candidate or lineage.
  Only explicit exact inspection returns content, classification, lineage and receipt. Results are bounded to 64 KiB by the [serializer](../src/Kora.Core/Commands/MemoryCommandResult.cs).
- [Review](../src/Kora.Application/Memory/MemoryManagementService.cs) requires inspection of the same current candidate, classification and control revision. Accept remains Reviewed/Pending; a separate admission enables it.
  Edit clears review and returns to Proposed/Pending, immediately redacting the earlier durable body. Disable closes use; re-enabling requires edit, inspect, review and admit. Forget retains a content-free non-reusable tombstone, not a claim of forensic erasure.
- [Session observation](../src/Kora.Application/Memory/MemoryAdmissionService.Observation.cs) and each mutation revalidate exact active session/generation, ownership, privacy/control revision, cancellation and lifecycle.
  Lifecycle, disposition, retention and privacy closure revoke cached inspection/presentation. Invalid persisted state propagates `InvalidDataException`, never an empty successful inventory or cached-body fallback.
- [Typed/activated routing](../src/Kora.Application/ViewModels/MainViewModel.Memory.cs) is visual-only and precedes inference; inspected values are not spoken, attached to history or sent to a provider.
  Model/HostSystem and completed host callbacks cannot relabel themselves as fresh original input. Failed receipts explicitly require inspecting durable state; no rollback or automatic retry is claimed.

The [command-level transition matrix and mutation-boundary tests](../tests/Kora.Application.UnitTests/Memory/MemoryAdmissionServiceTests.ManagementMatrix.cs) cover each state/action, model-origin rejection and session/ownership/privacy changes.
[SQLite surface tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteMemoryTests.Management.cs) exercise the authoritative round-trip, redaction/tombstones and corrupt saved state without cached fallback.

Provider ranking/prompt attachment, cross-session/global/device-profile/project/source recall, hosted disclosure, and model-facing **Remember this**/proposal triggers remain separate, undelivered workstreams.
The original 2026-10-09 surface had no create/propose route. The bounded original-user extension below supersedes that gap only; arbitrary conversation and model responses still cannot trigger creation.

### Delivered Original-User Session Memory Proposals - 2026-10-10

Native **Sessions > Session memories > New session memory from draft** and exact `memory propose <session-id> <class> "<exact value>"` typed/activated input reuse the [same management workflow](../src/Kora.Application/Memory/MemoryManagementService.cs) and authoritative admission policy. No existing memory ID is supplied. The host issues identity, initial revision and exact request/session/source lineage.

Creation returns only content-free metadata for a volatile Proposed/Pending candidate. No automatic inspection, review, admission, use, recall, context attachment or provider transmission follows. The existing list → exact inspect → explicit accept/reject → separate admit sequence remains required. Only Session scope and the four allowed classes are exposed; an original-user class label is not secrecy detection.

The unchanged [domain bounds](../src/Kora.Core/Memory/MemoryPolicy.cs) reject invalid/control/ill-formed/oversized text: 512 UTF-16 code units, 1,024 strict UTF-8 value bytes and 2,048 serialized candidate bytes, without normalization/truncation. Capacity counts all durable identities, including shells/tombstones in other sessions, plus current unpersisted identities; 128 is a denial, never implicit eviction.

Original intent, live host context, exact active session/generation, private owner/control revision, storage and required typed audit remain authority. Stopped/replaced/completed/model callbacks cannot mint fresh user authority. Late proposal callbacks re-resolve the boundary before volatile publication. Native closure/selection changes, privacy/control changes, lifecycle and restart discard unadmitted bodies; durable edits/disable/forget and managed-copy semantics are unchanged.

[Application proposal tests](../tests/Kora.Application.UnitTests/Memory/MemoryAdmissionServiceTests.Proposals.cs), [typed/activated visual-only routing](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.MemoryProposals.cs), [private SQLite native/exact usability and discard tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteMemoryTests.Proposals.cs) and [native binding contract](../tests/Kora.Windows.IntegrationTests/SessionsViewModelTests.cs) are maintained evidence for this slice only.

No live database/device/model proof or installed trial is claimed. Full R12/D-014/release acceptance, cross-session/global/profile/project/source memories and all provider-memory consumers remain open.

### Delivered Reviewed Memory Admission - 2026-10-09

This first package's volatile/storage gaps are historical. The [durable increment below](#delivered-session-only-durable-memory-storage---2026-10-09) supersedes those gaps; the [management surface](#delivered-native-memory-management-surface---2026-10-09) adds native/exact controls.
Broader scopes and provider integration remain unavailable.

The first dependency-safe memory package delivers a provider-independent
[Core domain and policy](../src/Kora.Core/Memory/MemoryPolicy.cs) and an internal
[Application admission workflow](../src/Kora.Application/Memory/MemoryAdmissionService.cs).
It is a bounded production-code foundation exercised by deterministic doubles,
**not composed durable memory or a newly exposed command/tool/UI feature**.
It does not modify the active R14 immutable artifact/detail implementation,
desktop presentation, SQLite schemas, storage paths, providers or model turns.

The domain has strong Kora-issued memory/profile/project/source identities,
explicit Session/DeviceProfile/Project/Source scopes, request and session-generation
lineage, exact optional source revision, proposal origin, creation time, memory
revision, review receipt and Pending/Enabled/Disabled/Forgotten retention.
Core is the single authority for candidate validation, scope/lineage eligibility
and exact review-receipt applicability. These records are data, not executable
capabilities or persistence/egress approvals.

The host workflow implements these exact transitions:

| Operation | Required state and result |
|---|---|
| Propose | Host resolves scope/lineage and issues identity/revision 1; User or Model candidate remains Proposed/Pending, without review, use or persistence |
| Review | Original local user input reviews the exact current Proposed revision; accept creates a Reviewed receipt bound to that revision and complete current boundary; reject records Rejected |
| Admit | Exact current Reviewed/Pending revision, receipt and unchanged review boundary become Admitted/Enabled |
| Edit | Exact non-forgotten revision replaces the value and request lineage; clears the receipt and returns to Proposed/Pending, immediately closing prior use |
| Disable | Exact Enabled revision becomes Disabled; neither read nor Admit implicitly re-enables it; explicit edit/review/admit is required |
| Forget | Exact non-forgotten revision removes candidate and review receipt; a content-free identity/lineage tombstone is retained only in this volatile workspace; it cannot be edited/reused |
| Use | Re-resolves exact identity/revision and checks eligibility before any relevance operation; returns exact IDs, revision, scope, candidate, source/request lineage, review receipt and consuming host request for Local only |

Model candidates cannot supply memory identity, scope, lineage, a review receipt,
retention, destination or authority. They carry only an untrusted typed content
class and value. HostSystem/model callbacks cannot review, admit, edit, disable
or forget. The dependent native/control routes must capture fresh explicit
original user input through the existing host control-intent boundary; they
must never convert a model response, apparent approval or candidate class into
a user review. There is no model-facing mutation descriptor in this package.

Only explicitly reviewed facts, response preferences, workflow preferences and
decisions are eligible content classes. Unknown/undefined classes, Credential,
Secret, Health, InferredTrait, TransientTask and ModelClaim have explicit denial
outcomes. Candidate labels are not sensitivity proof: the user must review the
exact content and classification against known lineage. No heuristic scanner
or model self-classification is claimed to establish that text is safe.

Values are never truncated or normalized. The ceilings are **512 UTF-16 code
units**, **1,024 strict UTF-8 value bytes** and **2,048 bytes for the complete
serialized candidate including class, field names and JSON escaping**.
Null/blank/control-bearing/ill-formed Unicode values fail closed. The volatile
workspace has **128 total identities**, including forgotten tombstones, with
no implicit eviction or identity reuse. Capacity is an explicit outcome,
not a durable-retention policy.

All operations require live host-resolved Activity/request identity, current
host ownership and control/privacy revision, exact authoritative active session
and generation, and a host-resolved profile/source observation. Unknown owner,
privacy, scope, session, lineage or destination is denied. An async authoritative
read is followed by cancellation/lifecycle/control/source revalidation and a
serialized in-process publication fence. Concurrent late callbacks re-resolve
the current revision at that fence. Pending proposals/reviews cannot move to
another session/generation; admitted profile memories can be considered in
later sessions only within the same exact profile and still-current source
lineage. Project/source memories require their exact active scope/revision.

The lifecycle callback clears session-scoped candidate/receipt content and
invalidates in-flight publication; device/project/source entries remain
independent. The dependent package must bind this callback to authoritative
session retirement/deletion and resolve scope observations under the existing
native ownership/privacy/session fences. Inactive/changed-generation sessions
already fail admission. Disposal clears the workspace and suppresses late
publication. These are conceptual/process-local lifecycle semantics, not
durable deletion, forensic erasure or removal of snapshots already returned.
Every later use still revalidates the current record; old snapshots confer no
authority.

Required typed requested/terminal audit precedes state/use publication.
Authority/cancellation changes during an audit callback suppress publication
and append a truthful denial/cancellation terminal observation. Audit/read
failures are explicit exceptions, not defaults or successful admissions.
Source-generated diagnostics include only operation/outcome/reason/host memory
ID and fixed failure type. Existing versioned policy Activities preserve host
request/session/task correlation; retirement-related audit/diagnostics use causal
links, not a fabricated live parent. No memory content enters logs, tags or
Baggage.

**Dependent delivery remains open:** durable store/schema/migration/restart,
atomic storage/audit and native authority leases, supplied application-data
paths, lifecycle inventory and copy disposal, original-input CRUD/review/list
UI/tools, current-use/provenance visibility and retention controls. Retrieval
ranking, prompts/context attachment, hosted handoff, knowledge ingestion,
embeddings and providers are not implemented here. Local retention and even a
successful local use receipt never authorize Hosted or an unknown destination.
There is no persistence adapter and no implicit read/write to preferences,
session history, provider memory or a model context.

### Delivered Session-Only Durable Memory Storage - 2026-10-09

The [memory storage seam](../src/Kora.Core/Memory/IMemoryStore.cs), [private session resolver](../src/Kora.Application/Memory/SessionMemoryScopeAccess.cs) and [SQLite adapter](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.Memory.cs) extend the existing interaction authority, not a parallel database or ownership service. The application admission workflow can use this seam; the desktop registers the shared store but exposes no memory command, tool or UI.

[Schema v7](../src/Kora.Windows/Storage/HostInteractionSchema.cs) adds a private partition identity and bounded reviewed-memory rows. Validated v6 migration preserves existing v3 authority, v4 history, v5 queue and v6 retention. Missing, hostile, downgraded or unknown saved state is refused without replacement. The profile identity binds this private storage partition; it is not global/profile selection authority. DeviceProfile, Project and Source durable scopes stay closed.

Only exact reviewed admission persists a candidate body. Proposals and first reviews remain volatile. A persisted edit immediately replaces the previously admitted body/receipt with a content-free Proposed/Pending revision; its replacement and review stay volatile until readmission. Restart loads that shell without a candidate or receipt: it cannot be reviewed/admitted without a new edit. Disabled admitted values remain retained but ineligible. Forget leaves a durable, non-reusable content-free identity/lineage tombstone.

The [domain limit](../src/Kora.Core/Memory/MemoryPolicy.cs) is 128 durable identities across the private store, including shells and forgotten tombstones. There is no implicit eviction, truncation or identity reuse; capacity denial publishes neither a new row nor an admitted cache record.

Each durable mutation rechecks the exact original row, revision, session generation, scope and lineage under the existing private storage lease. Initial/readmission also binds the exact original reviewed revision, candidate and current boundary. A fresh original-user committed intent, current host/control/privacy fence and required typed authority audit share the data transaction. Audit failure, cancellation or pre-COMMIT revocation rolls back both data and authoritative audit; a later callback cannot overwrite a newer committed cache revision. Stored data and review receipts never supply use, dispatch or egress authority.

[Session retirement](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.cs), [retention](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.Retention.cs) and [exact disposition](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.Disposition.cs) atomically invalidate owned memory bodies/receipts and retain tombstones. Disposition hashes include the exact memory inventory. The existing shared-database retention/copy workflow owns cleanup; memory creates no provider cache, export or second database. Uninventoried interaction-store copies hold explicit forget/edit redaction and retention deletion acceptance rather than being discarded or ignored.

[Actual-store tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteMemoryTests.cs) cover migration, exact reopen, hostile state, atomic audit/commit failure, cancellation/revocation, stale retirement writes, redacted edits, disabling, forgetting, disposition, unrelated-session retention and uninventoried copies. Secure deletion and a truncated owned rollback journal are exercised for memory mutations; subsequent legitimate writes/reopen cannot restore forgotten content or review/use authority. This is not forensic erasure, power-loss qualification, encryption or removal of externally returned/exported/provider copies.

Native original-input CRUD/review/list controls and lifecycle presentation integration, provider ranking/prompt attachment, cross-session/global/project/source recall and hosted disclosure remain separate dependent work. No model, SDK-native memory/session I/O, hardware trial or R04/R12/A0-A4/profile qualification is enabled or inferred.

Experiment disposition: reviewed storage, Node/.NET final-request/runtime
lifecycle and local-inference evidence remains retained. Deterministic admission
tests do not supersede unique actual SQLite atomicity/recovery/capacity/copy
disposal, SDK/native memory/session I/O, failed-result serialization, egress,
quiescence, account or offline/quality measurements. No experiment or historical
receipt is removed, relabelled or claimed wholly migrated.

## Folder-Backed Grounded Knowledge

Users can configure one or more named files or folders as managed knowledge
sources and can attach a source revision to one session. The detailed
admission, identity, refresh, citation, lifecycle, and Windows path contract is
owned by
[File and Folder Ingestion and Grounded Reasoning](File_And_Folder_Ingestion.md).

Large data sets are not copied wholesale into a model prompt. Kora builds and
uses a provider-independent local retrieval pipeline:

```text
Reviewed source roots
    -> bounded local discovery and extraction
    -> immutable admitted source revision
    -> structure-aware chunks and local indexes
    -> lexical and optional semantic retrieval
    -> host reranking, diversity, and context budgeting
    -> immutable cited evidence envelope
    -> selected local or hosted provider
```

The original files remain authoritative. Snapshots, extracted text,
embeddings, and indexes are derived data that retain source and revision
identity and can be rebuilt or deleted.

### Retrieval

The initial useful delivery uses deterministic lexical search over UTF-8 plain
text and Markdown. Lexical retrieval remains available for exact identifiers,
versions, error messages, acronyms, and code-like terms.

Optional semantic retrieval adds a qualified local embedding model and vector
index. Hybrid retrieval merges lexical and semantic candidates, deduplicates
them, applies source and scope filters, and reranks them before enforcing the
runtime context budget. The generation model and embedding model are separate
qualified dependencies.

Embeddings are sensitive derived content, not anonymous metadata. Kora records
the embedding model identity and digest, dimensions, normalization, chunking
and index schema versions. An incompatible change causes an explicit bounded
rebuild; it never silently mixes vectors from different models or schemas.

For broad questions, Kora may perform bounded iterative retrieval: an initial
search, a host-validated request for more evidence, and a constrained
follow-up search. The model never receives an unrestricted filesystem or index
handle.

### Markdown and Images

Markdown extraction preserves headings, lists, tables, code fences, links,
image references, and human-meaningful locations. Structure-aware chunking
uses section boundaries with bounded overlap rather than arbitrary prompt-size
splits.

The delivered R26.1g local-only slice is narrower: native exact-source viewers default to unchanged `lexical-lines-v1`; explicitly choosing **Markdown structure-aware** selects `markdown-structure-v2` for that run/view only. It scans bounded top-level ATX/standalone Setext and backtick/tilde fences, retaining exact inert lists/tables/links/image references/authored alt text and original locations.

It is not full CommonMark, rendering, OCR, an index or a model context envelope. There is no overlap/dedup inflation; oversized/incomplete structures are explicitly labelled fragments within the unchanged 2,048-character/128-line and eight-citation/16 KiB global budgets. Plain `.txt` uses original line chunks, unsupported formats/modes/versions deny, and reopening restores only the v1 default.

The exact [supported subset and authority boundary](File_And_Folder_Ingestion.md#delivered-opt-in-markdown-structure-aware-retrieval---2026-10-11) is canonical.
[Maintained golden tests](../tests/Kora.Core.UnitTests/Context/LocalFileMarkdownRetrievalTests.cs) prove only pure lexical original-snapshot retrieval; optional overlap, managed indexes, semantic ranking and provider-grounded reasoning above remain proposed.

Images are admitted separately from surrounding Markdown:

- initial support retains authored alt text, captions, and nearby text;
- later OCR may extract text from screenshots and scans;
- later qualified local vision processing may produce a clearly labelled
  machine-derived description; and
- optional image embeddings require their own model, quality, privacy,
  lifecycle, and retrieval evidence.

OCR and generated descriptions are derived evidence and can be wrong. Kora
does not present them as authored document text. Embedded resources, links,
scripts, macros, and remote images are never executed or automatically
fetched during indexing.

### Incremental Indexing

Indexing is asynchronous, cancellable, resource-bounded, and separate from
interactive reasoning. Kora:

- reuses unchanged content by digest;
- reprocesses changed or newly admitted items;
- removes deleted, excluded, inaccessible, or revoked items from active
  retrieval;
- atomically publishes only internally consistent revisions;
- reports partial or failed indexing instead of plausible empty results;
- records parser, chunking, embedding, and index versions; and
- reconciles registered sources because file-watcher notifications, if added,
  are hints rather than authoritative state.

## Local and Hosted Knowledge Use

The local index and retrieval service are shared by both providers. Provider
native uploads, vector stores, conversation attachments, or retrieval plugins
do not replace Kora's source identity, scope, citation, retention, egress, and
deletion controls.

For Ollama, selected excerpts remain on-device:

```text
Local sources -> local extraction/index/retrieval -> Ollama
```

For Copilot, Kora still retrieves locally and transmits only the admitted
evidence envelope:

```text
Local sources -> local retrieval -> destination/egress review -> Copilot
```

Each source has an egress classification:

- **Local only**: no source content or derived content may be sent remotely.
- **Ask for this request**: Kora previews the exact revision/excerpts before
  hosted use.
- **Hosted eligible**: the source may participate in a hosted proposal, but
  every outbound envelope still passes destination, purpose, sensitivity,
  session, and current-policy checks.

Registering a folder, enabling Copilot, or using a source locally is not
standing permission to upload it. If required evidence is local-only while a
session uses Copilot, Kora offers a local turn or reports the policy conflict;
it does not omit the evidence and produce an apparently grounded answer.

## Evidence and Answer Contract

Evidence is structurally separate from instructions and chat history. Each
selected item carries:

- Kora evidence, source, revision, item, and excerpt identities;
- a human-meaningful relative location such as heading, page, or line;
- extracted content and authored-versus-derived classification;
- freshness and access observations;
- hosted disclosure eligibility; and
- the content digest and extraction/index versions.

The model cites Kora evidence IDs. Kora validates cited IDs against the exact
request envelope and renders safe links to admitted local sources. A provider
cannot invent a local path, widen a source, or decide which file Kora opens.

Answers visibly show the provider used, sources searched, citations, and any
stale, excluded, inaccessible, partially indexed, or insufficient-evidence
state. Kora distinguishes source-backed statements from model inference and
does not expose raw similarity scores as answer confidence.

## Component Responsibilities

- `Kora.Core` owns provider-mode policy, provider-neutral request/response and
  evidence contracts, memory/source identities and scopes, eligibility,
  citation validation, context budgets, and lifecycle rules.
- `Kora.Tools` owns one action per admitted memory/source/search/read operation
  over consumer-focused Core contracts. It owns no arbitrary filesystem or
  provider escape hatch.
- `Kora.Definitions` owns bundled provider-neutral grounding instructions and
  prompt resources after their runtime support is qualified. It owns no
  execution, egress, memory, or session authority.
- `Kora.Application` owns provider selection, handoff workflow, context
  assembly, memory and evidence retrieval, indexing orchestration, egress,
  audit, cancellation, and presentation state.
- `Kora.Windows` owns Windows picker/path/file identity, reparse and access
  observations, bounded native reads, and platform-specific parser/OCR
  mechanisms.
- `Kora` composes providers and presents session mode, provider provenance,
  memory review, knowledge management, indexing health, handoff, excerpt
  review, citations, and recovery.

Provider adapters implement qualified mechanisms. They do not decide whether a
request, memory, tool result, or knowledge excerpt may be disclosed.

## Security Boundaries

Memory and retrieved content are untrusted data. Text such as "ignore previous
instructions", an apparent approval, or a tool command cannot become system
policy, user intent, a grant, or execution authority.

Kora applies:

- current Windows profile, session ownership, privacy, source, and provider
  revalidation before selection and transmission;
- strict separation of system policy, user intent, evidence, memory, tool
  results, and model output;
- secret/sensitivity detection as a warning or denial control, never proof
  that remaining content is safe;
- content-minimizing structured diagnostics and trusted typed security audit
  events;
- deterministic deletion inventories for memories, snapshots, chunks,
  embeddings, indexes, temporary files, and cached envelopes; and
- truthful remote outcomes when transmission may have occurred before
  cancellation or failure.

Provider provenance, model confidence, a document statement, or a previous
answer cannot establish identity, permission, approval, or destination
authority.

## Delivery Sequence

This direction is delivered through existing roadmap packages rather than a
parallel model subsystem:

1. **Provider-neutral host controls**: R04-R08 establish durable identity,
   authority, tool/result mediation, context/egress control, qualified Ollama,
   and independently qualified Copilot, Claude, and Codex adapters.
2. **Explicit session modes and handoff**: R08/R10/R12 expose Local only,
   Local first, and Hosted preferred with provider provenance and no silent
   fallback.
3. **User-governed memory**: R10/R12/R14 add proposal, review, scoped storage,
   retrieval visibility, editing, and deletion.
4. **Text knowledge foundation**: R26.1-R26.3 add reviewed source revisions,
   Markdown/plain-text extraction, lexical retrieval, citations, and qualified
   local reasoning.
5. **Hosted grounded reasoning**: R26.4 applies exact excerpt/destination
   review through the admitted selected hosted-service adapter.
6. **Richer retrieval and media**: R26.5 independently qualifies hybrid/vector
   retrieval, additional formats, OCR, and later vision support.

Each slice remains unavailable until its own implementation, dependency,
licence, privacy, resource, real-provider or real-device, failure-path,
deletion, and acceptance evidence passes.

### Delivered R04-R08 Host Controls - 2026-10-09

The first dependency-safe package is a composed, **adapter-unavailable** host
boundary, not a second model subsystem or provider qualification.
[ModelTurnHost](../src/Kora.Application/Dependencies/ModelTurnHost.cs) reuses
the existing host-resolved Activity/request, authoritative session workspace
and task observations, current ownership/privacy/control revisions, trusted
typed security audit, six-ID read-only registry and `LocalModelResponse`
contract. Core owns its provider/model/turn identities, provenance, typed
outcomes and immutable context/evidence envelope. Application owns admission,
single-use dispatch, revalidation and late-completion observation. No new
Windows mechanism, provider package, credential, UI command or product mode is
enabled.

Implemented boundaries:

- A host-issued turn binds exactly one provider, opaque catalogue model ID and
  exact profile revision to one request/session/task. A caller cannot replace
  the envelope or provider, replay the capability, borrow it across hosts or
  sessions, or reuse its expired/completed issuing Activity.
- Admission requires a live host context, current host/privacy/control state,
  an active matching session generation and exact nonterminal current-source
  task authority. Dispatch, tool requests and response publication revalidate
  those boundaries. Missing, foreign, expired, future-dated or revoked context
  is denied; required audit/storage failures are surfaced, not defaults.
- Context separates host policy, user request, evidence and the existing
  admitted read-only descriptors. Evidence has a unique host evidence ID,
  exact request and revision, immutable text and explicit disclosure class.
  Unknown disclosure fails closed. At most **16** evidence items and
  **32,768 complete serialized host-envelope UTF-8 bytes** are admitted;
  JSON escaping and catalogue overhead count. Nothing is silently truncated.
  The provisional structured response is bounded to **4,096 serialized UTF-8
  bytes**, reusing the existing result limit. These are host transport ceilings,
  not a qualified token window or permission to send a 256 KiB clipboard
  snapshot. Native-provider protocol overhead/final-request mediation still
  needs its own exact-profile qualification.
- A single-use capability dispatches one adapter once. There is no retry,
  alternate-provider lookup, blending, confidence-based handoff, memory write
  or destination parameter. Provider-reported identity never establishes
  provenance. The initial response subset admits bounded answers only;
  action/grant/question payloads cannot acquire authority through this new
  boundary. Existing bootstrap proposal handling is unchanged.
- During an admitted dispatch, tool proposals use the **same** six-ID registry,
  strict input checks, typed handlers and bounded all-status result mediation.
  No new tool, lane authority, content-reading descriptor or effect is added.
  Tool access closes when dispatch completes or cancellation/authority changes.
- Unavailable, authentication-required, quota-exceeded, timed-out, cancelled,
  denied-egress, denied and unknown are explicit typed outcomes without a
  success-shaped response or fallback.
- The host signals a **15-second** dispatch deadline and promptly closes output
  and tool authority on timeout/cancellation. An uncooperative adapter keeps
  the single dispatch slot occupied until actual completion; new dispatch is
  unavailable and disposal waits for quiescence. Late output is suppressed,
  faults are observed and completion uses a causal Activity link, not a retired
  parent. Cancellation is not rollback, provider cessation or remote deletion.
- Requested/terminal typed audit and source-generated structured diagnostics
  carry host turn/provider/model/revision identity, not content, paths,
  provider error text or incoming trace authority. Policy and runtime
  Activities contain only admitted host identifiers and provider class.

**Exact enablement gates remain open.** Production registers Ollama and Copilot
with no adapter and no qualification evidence. The internal mechanism seam is
exercised only by deterministic fakes. Local admission requires the exact
R02-L1-L5 owner-reviewed candidate selection **and** R06/R07/L6 integrated-host
proof for that catalogue revision. Hosted admission requires exact .NET
final-request profile conformance, RT2 all-path lifecycle/egress proof,
execution PV1 account/destination admission and integrated-host proof. Expired
or partial evidence is unavailable. Even a fully attested hosted profile is
denied here because exact destination/content egress approval is not implemented;
`HostedEligible` evidence is not transmission consent.

This package preserves current bootstrap readiness/unavailable messages,
buffered local reasoner and exact offline controls. It does not upgrade that
bootstrap to a qualified neutral adapter. Native Local only, Local first, Hosted
preferred settings, hosted disclosure, durable memory and local/hosted grounded answering
remain disabled planned product modes; the policy/workflow increment below does not enable them.

Experiment disposition: the Node runtime, actual .NET RT1, RT2 lifecycle,
MG1 management and local-inference proofs were reviewed against this boundary.
Maintained tests exercise production host-envelope bytes, identity/isolation,
typed results, audit, registry reuse, revocation, deadlines and late-response
links. They do not exercise SDK-native final JSON, failed-result/history
serialization, runtime/session storage, retry transports, provider accounts,
real answer quality, offline network denial or server cessation. Those unique
assertions, recorded artifacts and rejected hook-only witness remain retained;
no experiment assertion is claimed migrated and no experiment is removed.

### Delivered Provider Policy and Exact Handoff Workflow - 2026-10-09

The [Core policy](../src/Kora.Core/Dependencies/ModelProviderPolicy.cs) and
[host consumer](../src/Kora.Application/Dependencies/ModelTurnHost.Policy.cs)
implement volatile session-bound LocalOnly, LocalFirst and HostedPreferred
semantics, with explicit Default/Local/Hosted choices for each new turn.
LocalOnly never offers hosted disclosure; LocalFirst defaults local and requires
exact review for a hosted choice; HostedPreferred defaults hosted but still
fails closed without independent egress authority.

An original LocalUi/ActivatedVoice host request with a matching active session
and committed nonterminal task can publish the next policy revision. Replacing
policy invalidates old review bindings and admitted turns. Unknown
mode/choice/provider, empty model/session IDs, invalid revisions and unknown
qualification bits are rejected by the
[shared selection validation](../src/Kora.Core/Dependencies/ModelProviderSelection.cs),
[policy](../src/Kora.Core/Dependencies/ModelProviderPolicy.cs) and
[qualification gate](../src/Kora.Application/Dependencies/ModelProviderRegistration.cs).

The [composed workflow](../src/Kora.Application/Dependencies/ModelProviderHandoffWorkflow.cs)
reuses HostQuestionService and its atomic original-intent/question/audit
transaction. A [host-issued offer](../src/Kora.Application/Dependencies/ModelHandoffOffer.cs)
exposes the exact immutable context, destination/catalogue revision, typed
observable reason, question key/revision, session generation, task revision and
privacy/ownership control revision. Approve, decline, cancel, expired and stale
outcomes are typed; removing exact evidence IDs retires the old offer and creates
a reduced envelope requiring fresh review. No confidence field, default answer,
grant or model-selected Kora identity is accepted.

The workflow can offer review after the
[audited local Unavailable terminal result](../src/Kora.Application/Dependencies/ModelTurnHost.Policy.cs)
of a host-issued policy-bound turn; it does not automatically dispatch another
provider. Other [typed reasons](../src/Kora.Core/Dependencies/ModelHandoffReason.cs)
must come from host-owned capability/budget/acceptance or explicit-user decisions,
not model text. One-use review is bound to the original user/request, exact
context object including evidence lineage, exact policy and destination revision,
validity interval and live authority observations. Changes before confirmation,
after question commit, during terminal audit or before hosted admission fail
closed ([focused tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.Policy.cs)).

The complete serialized envelope, including JSON escaping, evidence provenance
and tool catalogue overhead, remains bounded to 32,768 UTF-8 bytes and 16 evidence
items; no truncation occurs
([envelope](../src/Kora.Core/Dependencies/ModelContextEnvelope.cs),
[exact-limit tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.Policy.cs)).
HostedEligible is classification, not permission; LocalOnly evidence cannot
enter an offer. Required audit failures throw and return no new policy/review
capability; cancellation and late callbacks cannot acquire one
([workflow tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.Policy.cs)).

**Boundary:** confirmation is neither runtime qualification nor a final-request
egress receipt. Production still composes no adapters or qualification evidence,
and even qualified test registrations plus approved review return DeniedEgress
at the [turn host](../src/Kora.Application/Dependencies/ModelTurnHost.cs).
No account/model acquisition, network send, RT1/RT2/PV1 proof, memory
storage/retrieval or prompt ranking is delivered by that increment. The generic
native question surface cannot itself issue this workflow's review capability;
the separate exact native consumer below now closes that review-only gap.
Durable session policy awaits dependent
native/persistence integration in that increment; the durable preference delivery
below closes only the device-local persistence/settings gap. Bootstrap behavior
is unchanged ([initial-policy consumer](../src/Kora.Application/Dependencies/ModelTurnHost.Policy.cs)).

Experiment disposition remains **retain**, with no executable or receipt
removal. The [RT1 final HTTP boundary](../experiments/r02-dotnet-control-proof/RequestBoundary.cs),
[RT2 actual native lifecycle observations](../experiments/r02-runtime-lifecycle-proof/LifecycleTests.cs)
and [MG1 actual-runtime envelope/admission](../experiments/r02-dotnet-management-proof/Envelope.cs)
have different subjects from this host policy workflow. The
[runtime preparation consumer](../eng/RuntimeValidation.Common.ps1) still
verifies and derives the historical profiles; RT2/MG1 share RT1 fixtures, and
Node remains the rejected hook-only witness. Unique native/session-I/O/retry/
account/local-floor/offline procedures and outstanding gates remain in the
[inventory](Implementation_Roadmap.md#experiment-disposition-inventory).
Deterministic policy tests do not establish exact SDK/native equivalence or
close R08/D-014 acceptance.

### Delivered R10 Durable Provider Mode Preference - 2026-10-09

Native **Settings > Providers** and exact typed/current-name activated discovery/get/set/reset share the [provider-mode workflow](../src/Kora.Application/Configuration/ProviderModeConfigurationService.cs). Only `providers.default-mode` is registered: LocalOnly, LocalFirst and HostedPreferred. Unsaved/default/reset is **LocalOnly**; no hosted handoff is offered without explicit opt-in ([domain rules](../src/Kora.Core/Dependencies/ModelProviderModePreference.cs)).

Schema-1 `provider-mode.txt` and `provider-mode-unconfirmed.txt` use `IApplicationDataPaths` and the shared atomic preference store. Unknown schema/shape, noncanonical modes, invalid UTF-8, oversized or unconfirmed state throws `InvalidDataException`, never a default. Reset explicitly saves LocalOnly, following response-mode set-to-default semantics; other preferences are untouched ([storage](../src/Kora.Application/Configuration/LocalModelProviderModePreferences.cs)).

Host-held choices bind owner, preference revision, original channel, admitted active session/generation and live ownership/privacy/call eligibility. Original intent, REQUESTED/terminal trusted audit, atomic save/readback and durable completed control receipt precede confirmation. Protected/Unknown original voice writes are denied; interrupted writes remain unavailable across restart and require explicit saved-state/evidence repair ([workflow tests](../tests/Kora.Application.UnitTests/Configuration/ProviderModeConfigurationServiceTests.cs)).

At the first policy-bound turn, the [host](../src/Kora.Application/Dependencies/ModelTurnHost.Policy.cs) seeds revision 1 from confirmed device state after existing session/task admission. Subsequent device edits do not replace an initialized session's volatile policy. Default/Local/Hosted choices, LocalOnly refusal, LocalFirst exact handoff review and revision invalidation retain their existing semantics ([consumer tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.ProviderPreference.cs)).

Preference publication adds no adapter, qualification, account, destination, network, grant or egress path. HostedPreferred remains unavailable without an adapter and DeniedEgress even with fully qualified fake registrations. Bootstrap reasoning is unchanged; the native review-only consumer below adds no real hosted inference ([fail-closed tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.ProviderPreference.cs)).

Deterministic fake/private-storage, shared SQLite lease and native binding tests cover this bounded increment; no experiment evidence is removed or claimed migrated ([storage integration](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteProviderModeConfigurationTests.cs), [native contract](../tests/Kora.Windows.IntegrationTests/ProviderModeConfigurationUiContractTests.cs)).

### Delivered Bounded Exact Native Handoff Review - 2026-10-10

**Settings > Providers > Review pending exact provider handoff** consumes the existing workflow's volatile source of successfully audited, host-issued `ModelHandoffOffer` references. At most sixteen pending offers are retained. Text, paths, serialized offers, model output, cache records and trace headers cannot create an entry.

Production has no qualified adapters and honestly reports **Unavailable: no pending qualified host-issued offer**. No sample offer is manufactured.

The [Application lifetime](../src/Kora.Application/Dependencies/ModelHandoffReviewSession.cs) freshly revalidates original request/session/task, active generation, exact policy/destination/catalogue/control revisions and the pending question through `HostQuestionReviewService`. Deferred operations retain the original request and causal Activity links; selecting another session cannot retarget a card.

The [native surface](../src/Kora/ModelHandoffWindow.axaml) shows offer ID/revision, question ID/revision, original request/origin, session/generation, task revision, policy/control revisions, typed reason, destination/model/catalogue revision and validity.

The complete exact serialized envelope is inert plain text: original request, system policy, every evidence item's identity/revision/request lineage/content/disclosure and the tool catalogue/output bound. Existing complete 32,768-byte and sixteen-evidence limits are reused without truncation. Typed disclosure labels accompany exact evidence-removal controls.

There is no affirmative preselection, copy/export, content logging/persistence, speech, link activation or arbitrary-context preview route.

Explicit approve/decline/cancel call the **same** workflow. Selected evidence removal cancels the old question, retires the old offer and exposes a new reduced exact envelope. **Read complete exact envelope** is required again, with no inherited approval.

Close/disposal cancel when authority is available and always retire volatile content. Expiry, task/session retirement, changed policy/control/privacy/ownership, cancellation and late callbacks fail closed. Native refresh does not answer unrelated pending questions or select a work/voice session.

Instance handoff is held until actual native review reads/decisions/closure finish. Cleanup retains its owned cancellation resources until operations terminate; closure does not turn unfinished I/O into a quiescence claim.

Confirmation means **exact context review only**. The typed outcome displays unavailable runtime/account/final-request-egress gates and **nothing sent or executed**. No runtime/egress adapter, provider/account enablement, grant, send, execution, fallback or retry is implemented. LocalOnly preference/default and per-turn Default/Local/Hosted semantics remain unchanged.

Deterministic qualified fakes exercise actual offer-to-consumer wiring and exact-limit/lineage/lifecycle failures. [Private SQLite tests](../tests/Kora.Windows.IntegrationTests/Storage/WindowsSqliteNativeHandoffReviewTests.cs) exercise genuine original-intent/question/required-audit transaction and rollback boundaries. XAML/composition contracts are not installed keyboard/screen-reader/DPI acceptance.

RT1 source-built final bytes, RT2 all-path native lifecycle, MG1 released-runtime envelope/admission, execution-account, local-floor/offline and unique native procedures have different subjects. All experiments and receipts remain **retain**, with no equivalence, retirement or gate-closure claim.
