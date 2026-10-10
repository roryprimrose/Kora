# Internal Kora Tools Available to Models

Status: current bootstrap inventory plus proposed versioned tool catalogue. Proposed names below are design contracts, not implemented SDK APIs.

Related: [Architecture](Architecture.md), [Interaction and Sessions](Interaction_And_Sessions.md), [Security](Security_Data_Flows.md), [Configuration](User_Configuration.md), [Extensibility](Extensibility.md), [Execution Design](../docs/skill-and-task-execution-design.md).

This is the canonical inventory of Kora-owned model-facing operations.
Internal means implemented/mediated by Kora, not every method or UI command in the application.
External MCP tools, provider-native tools, and skill-defined workflows are separately admitted capabilities, not automatically internal tools.
Being listed here does not enable a capability before its delivery, adapter, containment, and authorization gates pass.

### Delivered bounded read-only host foundation

The [authoritative production catalogue](../src/Kora.Core/Tools/ReadOnlyCapabilityCatalog.cs)
and [direct registry handlers](../src/Kora.Application/Tools/ReadOnlyCapabilityRegistry.cs)
compose exactly `capabilities.list`, `capabilities.get`, `application.get_version`,
`readiness.get`, `runtime.list`, and `runtime.get_status`. These canonical IDs
are now host contracts, not a live model SDK/MCP catalogue. Exact local native
commands are documented in [Commands](../docs/commands.md#read-only-host-discovery).
The descriptor schema is version 1: None/Page/Id typed inputs, corresponding
typed result shapes, ReadOnlyObservation effects, handler availability,
Native/Management/Execution lane classification, 1,024-byte input / six-record /
4,096-byte complete serialized UTF-8 result limits. Pages have total count and
explicit next offset; invalid fields/duplicates/IDs/ranges are denied.

Management receives only these minimal read-only descriptors, never the current
action enum, executable-task catalogue, source instructions, or new authority.
Lane selection is host-owned, not an input field. Callers are registry-instance
and live host-request bound; foreign, completed, disposed or unavailable host
contexts fail closed. Only the Native route is presently composed; adapter
admission/egress and full model tool/result iteration remain outstanding.
Raw dependency names/details are not serialized. Readiness and `local.inference`
runtime observations reuse the existing bootstrap/setup records, with timestamps
and explicit NotObserved/Unavailable reasons; queries do not reprobe or generate.
Version uses the existing running-version provider, which does not observe
deployment mode. Runtime health is not permission or R02 qualification;
ToolLoopQualified remains false. No settings/evidence/session/side-effect entries
are added, and existing exact lock/power behavior is unchanged.

R26 selected-source lexical search is **host/native-only**. The six-ID schema
has no unavailable model-only content-operation descriptor or admitted
source/citation result shape. It is not extended to pretend retrieval is
available. Exact `search file` / `inspect file` focus native query entry over
the current explicitly admitted immutable preview; no source/query/excerpt
is serialized to models. Model retrieval, tool/result iteration and all
source-context egress remain unavailable.

`network.get_web_page` schema 1 is a delivered Kora action and exact
typed/activated-voice route, but its model-facing descriptor is **Unavailable**
with reason `parameterized-model-tool-loop-not-qualified`. The current local
model JSON protocol cannot supply an arbitrary URI tool argument or suspend and
resume around a durable approval question. Native `get web page <URI>` is an
original-user request for that exact address. Retrieval rejects non-public or
mixed DNS answers, pins the admitted address, disables ambient credentials,
cookies, proxies, decompression and automatic redirects, and reruns DNS and
authorization for every redirect. Only unencoded UTF-8 text/plain and text/html
are admitted; HTML is reduced to bounded normalized text. The model descriptor
must not become Available until its execution lane invokes the same action with
the host grant callback and has passed the tool-loop qualification gates.

The [Implementation Status and Delivery Roadmap](Implementation_Roadmap.md)
maps the current bootstrap and every catalogue family to dependency-ordered
implementation and acceptance work. Roadmap inventory IDs are not tool IDs.

## Availability and Caller Lanes

- **Current:** verified local Ollama bootstrap accepts a bounded JSON proposal, not an SDK tool-call loop.
- **Proposed A:** required host tools for Slice A, including revised A3/A4 sessions and interaction.
- **Proposed B/C:** connector/skill discovery in B and declarative authoring in C.
- **Deferred:** outside initial release scope; never advertise as available before its dedicated proof.

**M** is management/routing inference; **E** is a session's execution runtime.
Each table states the allowed lane. M can retrieve minimal permitted status and propose management changes, but cannot execute task tools, acquire task source content, or grant authority.
M/E power entries are proposal-only: the
[host lifecycle controller](Security_Data_Flows.md#management-power-proposal-authority)
owns approval, countdown and gated fixed-action dispatch, not either model lane.
Host-owned deterministic voice/UI controls remain available when either model lane is unavailable.
The same registry serves local inference and approved remote/agent adapters, but each receives only its admitted subset.

## Model-facing coverage rule

Model discovery is a normal entry point for user-visible Kora actions, not a
special integration added only to complex workflows. Each new action must be
evaluated for both an exact deterministic command and a typed model-facing
descriptor, alongside any UI surface. Implement every applicable route over
the same registered host action and policy gate. This allows common requests
to work without inference while allowing a model to identify less exact
natural-language intent and propose the same action.

An advertised descriptor is proposal authority only. The host still validates
the action and inputs, applies origin/privacy/ownership policy, obtains any
required approval, revalidates availability, performs the effect, and returns
a structured result. Models cannot invent actions, handlers, command lines, or
parameters outside the admitted schema.

The capability inventory must state and justify any missing deterministic or
model-facing route. Valid exceptions include actions that inherently require
interactive visual selection, have no safe unambiguous static phrase, are
unavailable in the current build, or have not passed their security and
containment gates. Delivery tests verify command/action resolution, shared
policy behavior, unavailable and unauthorized failures, and rejection of
unknown model proposals.

## Implemented Bootstrap Surface

Evidence: [action enum](../src/Kora.Core/Commands/BuiltInAction.cs), [command catalogue](../src/Kora.Core/Commands/BuiltInCommandCatalog.cs), [local reasoner](../src/Kora.Windows/Dependencies/WindowsOllamaReasoner.cs), and [host dispatch](../src/Kora.Application/ViewModels/MainViewModel.cs).

The reasoner advertises all 25 catalogue actions by their exact case-sensitive enum names.
It accepts exactly one response kind per inference:

| Response | Input/result shape and host behavior |
|---|---|
| `answer` | Non-empty text; display local-model attribution and apply the effective response policy |
| `question` | `prompt` (at most 500 characters), 2-4 distinct unambiguous option labels (at most 80 characters each); host presents choices and feeds the selected reply into bounded follow-up reasoning |
| `action` | One exact action name from the table below; host validates/dispatches or requests approval |
| `grantChange` | `operation: Add/Remove/Move`, registered `action`, `scope: Session/Always` where required; Move requires `targetScope`; omission of an existing scope requires host ambiguity checks |

Grant-change confirmation never executes the named action. Question selection never substitutes for action approval.
Current action proposals have no arbitrary parameters or command lines.
The model receives a bounded status snapshot: assistant name, listening flag, pending power action, dependency readiness, and setup task states/progress.
It does not receive unrestricted conversation storage, files, clipboard, accounts, or internet access.
The current request limit is 4,096 characters, inference deadline two minutes, buffered generation response 64 KiB, and clarification depth at most three follow-ups.
These are current protocol limits, not proof of future tool-loop behavior.

| Current `action` value | Actual host effect/result | Current model-action approval | Proposed equivalent |
|---|---|---|---|
| `ShowApplication` | Reveal existing Kora window | No | `application.show` |
| `HideApplication` | Hide UI, preserve listening/work | Yes unless an applicable named grant exists | `application.hide` |
| `ExitApplication` | Exit Kora and release audio | Yes unless an applicable named grant exists | `application.request_exit` |
| `RestartApplication` | Restart current application, not Windows | Yes unless an applicable named grant exists | `application.request_restart` |
| `OpenSettings` | Open settings | No | `application.open_settings` |
| `OpenDocumentation` | Open embedded user guide | No | `application.open_documentation` |
| `OpenSetup` | Show setup/readiness; install nothing | No | `readiness.show` |
| `ShowHelp` | Show built-in command catalogue | No | `capabilities.list` |
| `ShowVersion` | Report running version | No | `application.get_version` |
| `ShowStatus` | Report current activity and setup queue | No | `work.list` |
| `ShowCurrentTaskProgress` | Report observed setup task progress | No | `work.status` |
| `CancelTask` | Cancel active setup/inference under current host rules | Yes unless an applicable named grant exists | `work.cancel` |
| `StopSpeaking` | Stop speech playback | No | `speech.stop` |
| `LockMachine` | Real Windows lock through the current C# handler | Yes unless an applicable named grant exists; exact direct command currently differs | `computer.lock` |
| `ProposeShutdown` | Create proposal; bootstrap sends no OS shutdown request | Yes unless an applicable named grant exists | `computer.propose_shutdown` |
| `ProposeRestart` | Create proposal; bootstrap sends no OS restart request | Yes unless an applicable named grant exists | `computer.propose_restart` |
| `CancelPowerAction` | Cancel Kora-owned pending power proposal | No | `computer.cancel_power_action` |
| `ShowPowerStatus` | Report pending proposal and disabled execution | No | `computer.power_status` |
| `ListGrants` | Open current model-action grant document | No | `approvals.list` |
| `ManageGrants` | Open grant-change editor; not a grant mutation itself | No | `approvals.show` |
| `ShowModelExecution` | Report whether local and hosted model execution are enabled and available | No | `models.execution.get` |
| `EnableLocalModels` | Persist permission to use ready local models | Yes unless an applicable named grant exists | `models.local.enable` |
| `DisableLocalModels` | Persistently block local model use and cancel active local inference | Yes unless an applicable named grant exists | `models.local.disable` |
| `EnableHostedModels` | Persist hosted-model permission; current build still has no hosted provider | Yes unless an applicable named grant exists | `models.hosted.enable` |
| `DisableHostedModels` | Persistently block hosted model use | Yes unless an applicable named grant exists | `models.hosted.disable` |

The current host stores named model-action Session/Always preferences and supports Once approval.
This is not the proposed content/invocation-bound grant store with durable session identity and independently retained perpetual grants, nor proof that current actions have script-bound execution grants.
Current-versus-proposed differences must remain visible in documentation and capability negotiation.

## Shared Proposed Contract

Every tool descriptor declares its stable ID, schema version, availability/gates, caller lanes, effects/resources, sensitive fields, result bounds, cancellation behavior, and confirmation rule.
Mutations accept an expected revision and direct user-request/approved-plan lineage; the host resolves canonical targets and rejects stale or unauthorized proposals.
Session/task IDs are mandatory where applicable; IDs are never inferred from queue position, a title, or "the currently selected window".
Confirmation-required calls return a typed pending proposal/question ID, not a fictitious completed result; execution happens only after trusted input and immediate revalidation.
Retries use invocation/operation identity and observed receipts, not blind repetition of writes.
Results distinguish success, pending user decision, blocked/unavailable, conflict, cancelled, failure, and outcome unknown, with observation time and evidence references.
Tool names/arguments cannot select an executable handler, provider identity, security policy implementation, arbitrary SQL, or a storage path.

Reads are not universally harmless: enforce source/account access, classification, purpose, secret filtering, and destination-specific egress before sending results to a model.
Model-bound pages/excerpts fit the 64 KiB tool-result limit; history pages contain at most 50 events.
Management additionally obeys its narrower 32 KiB input/4 KiB output envelope and provider/deadline budgets from [Architecture](Architecture.md#independent-management-and-concurrent-sessions).
Use continuation/range references and explicit omitted/redacted/truncated markers; no silent loss or unbounded "return all".
Discovery returns only admitted schemas/capabilities, not protected paths, credentials, biometric data, or private-source existence without access.
Missing capabilities return unavailable with next steps; never install them or silently switch providers.

### Grant and Feedback Rules

Grants have three scopes: single-use (Once), the approved operation for an identified Kora work session (Session), or perpetual (Always).
The canonical [grant-scope contract](Security_Data_Flows.md#grant-types-and-inheritance) defines consumption, session end, restart, and applicability.
Only perpetual grants have no expiry, retention, inactivity cleanup, or eviction; they remain independently until explicitly removed or edited.
Grant creation/edit/removal requires trusted user confirmation; host single-use consumption and session-end enforcement do not require a new user decision.
Host revocation for a changed approved skill/script hash or definition digest
also requires no new user decision. Preserve the record as revoked, never
restore it when old bytes return, and require fresh approval for a new grant.
History can reference a grant but is neither its storage owner nor live authority.
Content, invocation, resource, account/identity, destination, and policy checks still determine applicability at every use; changed effects cannot inherit old approval.
Retain each perpetual grant's minimal scope, implementation digests, creator/evidence class, and applicability reason independently of the originating chat, without retaining that entire deleted chat.
Operational audit/use events and consumed/ended grant evidence may have separate disclosed retention; perpetual records cannot be swept up in that cleanup.
Approval proposals declare `grantScope: Once/Session/Perpetual` and a bound session ID for Session; the current protocol's `Always` maps to Perpetual.
Short-lived question/proposal deadlines and one-invocation dispatch receipts do not impose expiry on perpetual grants.
The separate default-On `calls.ignoreReusableGrants` setting temporarily ignores Session/Perpetual reuse during protected calls and requires fresh single-use approval per exact operation.
Device-local preapproved HTTP/HTTPS URI patterns participate in this same grant
policy when a host-bound `network.get-web-page` proposal is evaluated. Host
wildcards occupy entire DNS labels and require at least one literal host label;
wildcard-only hosts such as `https://*/` and `https://*.*/*` are rejected. A
matching canonical destination can satisfy only the per-address grant; session,
ownership, expiry, effect, content, identity, policy and egress gates still
apply. The host compares the raw resolved URI with its proposal destination
digest. Every redirect is a new destination requiring a fresh proposal and
policy decision. Preapproval is reusable authority, so
`calls.ignoreReusableGrants` ignores it during protected calls and forces a
fresh Once review. This authorization foundation does not advertise or deliver
a web-fetch action.
The host owns call/setting revalidation at dispatch; models cannot self-confirm the operation or switch this protection Off.
`approvals.inspect`/`approvals.explain` distinguish "ignored during call" from expired/removed/inapplicable and permanently content-revoked grants, while `approvals.request` reports effective required scope Once and the blocker.
See [Ignoring Reusable Grants During Calls](Call_Aware_Speech.md#ignoring-reusable-grants-during-calls); this changes applicability, not storage or perpetual retention.
The [in-call settings origin gate](Call_Aware_Speech.md#in-call-settings-origin-gate) rejects voice-originated voice/in-call configuration mutations, including call-protection downgrades, manual clearance, reset/undo, and temporary/speak-once exceptions.
Host-recorded origin survives model/tool hops and cannot be replaced by a later approval click; the user must start a new UI request. Models cannot choose the registry's protected-option classification.

Feedback has a separate in-call override, default **UI-only**, with admitted Voice/UI/Both/Inherit choices.
The call override takes precedence over ordinary task/queue/session/device output settings; mandatory privacy, lock, mute, and playback safety still win.
An explicitly permitted single-response speak-once request is a distinct bounded exception, not a permanent grant or blanket call-policy change.
Input eligibility is independent: UI-only feedback does not disable voice capture or provide playback rejection.
Models request presentation through the host policy service; they cannot force TTS, reopen a microphone, or suppress required trusted controls.
The [accepted Windows-session trust boundary](Security_Data_Flows.md#trust-boundaries) does not require speaker authentication for baseline enabled voice.
Optional frequent-speaker learning remains local personalization, never model-supplied identity or permission. Profile tools expose capability/status and trusted workflow references only, not samples, embeddings, scores, or inferred speaker identity.

## Proposed Tool Catalogue

Grouped rows enumerate distinct tool IDs sharing a schema family; each ID still has its own effect/authorization descriptor.
All mutating entries below are host-validated proposals or admitted invocations, not permission for the model to perform their effects directly.

### Discovery, Application, and Readiness

| Tool IDs | Inputs and bounded results | Lanes | Availability / boundary |
|---|---|---|---|
| `capabilities.list`, `capabilities.get` | Bounded page or admitted capability ID; versioned minimal read-only schemas/limits | M/E | Delivered host-only bounded foundation above; broader discovery/model adapters remain proposed |
| `application.get_version` | No fields; actual running version/build string, explicit unobserved deployment | M/E | Delivered host-only bounded foundation; no fabricated deployment facts |
| `application.show`, `application.hide` | Exact existing surface ID; visibility outcome | M/E | Proposed A; no implicit mute, cancellation, or Done |
| `application.open_settings`, `application.open_documentation` | Registered category/page ID; host surface reference | M/E | Proposed A; guide IDs, not file paths |
| `application.request_exit`, `application.request_restart` | Exact current-app action; affected-session plan/proposal and outcome | M/E | Proposed A; confirm live-work consequences; never computer restart or update |
| `readiness.get` | Bounded page of admitted dependency observations, timestamps, unavailable/unobserved reasons | M/E | Delivered host-only recorded observations; no refresh or inference |
| `readiness.refresh`, `readiness.show` | Registered capability/dependency ID; fresh probes or setup surface | M/E | Proposed typed tools; existing setup/refresh UI remains separate |
| `readiness.propose_setup` | Selected supported capability IDs; host-owned prerequisite/setup plan | M/E | Proposed A; installation/sign-in/consent completed by trusted setup, not a model installer tool |
| `runtime.list`, `runtime.get_status` | Bounded page / `local.inference` ID; locality, recorded readiness/time/reason, qualification false | M/E | Delivered host-only existing local adapter observations; broader provider/qualification contracts remain proposed |
| `voice_profile.get_status`, `voice_profile.request_manage` | Current-profile capability/status or explicit Learned/Enrolled workflow and test/correct/reset/delete/replace/setup operation; coarse readiness and trusted workflow reference | M/E | Optional after evidence; no raw features/identity/sample arguments; independent learning consent, protected verification flow, and in-call origin gate |

### Sessions and History

| Tool IDs | Inputs and bounded results | Lanes | Availability / boundary |
|---|---|---|---|
| `sessions.list`, `sessions.get` | Lifecycle/work-state filter or session ID; metadata, due dates, grounded status and permitted summary | M/E | Proposed A3; M receives minimal routing descriptors only |
| `sessions.search` | Query, lifecycle/date filter, cursor; permitted matches and evidence references | E; M metadata-only | Proposed A3; full-text content is not unrestricted routing context |
| `sessions.read_history` | Session/event/task/time filter and cursor; ordered permitted messages, decisions, receipts and grant references | E | Proposed A3; no implicit resume or retention refresh |
| `sessions.read_artifact` | Session/artifact ID, digest and bounded range; immutable source/provenance | E | Proposed A3/A4; never execute historic scripts |
| `sessions.create`, `sessions.rename`, `sessions.select` | New topic or exact ID/title plus revision; committed identity/selection | M/E | Proposed A3; selection is not resume, and route creation is acknowledged |
| `sessions.resume`, `sessions.mark_done` | Session ID/revision; work-resolution proposal and lifecycle result | M/E | Proposed A3; no grant or tool replay |
| `sessions.delete` | Exact ID/revision and deletion preview; decision-required/result with disclosed exclusions | M/E | Proposed A3; preserve independent grants; prevent late appends |

### Structured Interaction, Presentation, and Speech

| Tool IDs | Inputs and bounded results | Lanes | Availability / boundary |
|---|---|---|---|
| `interaction.ask` | Session/task IDs, question/option/field schema, constraints, consequences; question ID and pending state | M/E | Proposed A3; shared voice/UI draft; answers are trusted input events |
| `interaction.get_pending` | Exact session/task or permitted metadata filter; current questions/proposals, revisions and deadlines | M/E | Proposed A3; prevent generic replies targeting another session |
| `interaction.present` | Session/task ID, concise text and typed full-content items/provenance; response/artifact IDs | M/E | Proposed A4; M cannot launder disallowed task content into presentation |
| `interaction.show_details` | Exact response/artifact ID and source/rendered preference; detail/history surface reference | M/E | Proposed A4; displaying content is not model retrieval or execution |
| `presentation.navigate`, `presentation.pin`, `presentation.close` | Exact viewer/item and bounded next/back/zoom/pin state; presentation result | M/E | Proposed A4; no webpage form automation, workflow denial, or focus theft |
| `speech.stop`, `speech.mute` | Current host-owned playback/input scope; actual stopped/muted status | M/E | Proposed A; mute releases capture; no hidden voice-unmute channel |
| `speech.read_once` | Exact response/prompt ID and policy revision; eligible one-response playback or explicit blocker | M/E | Proposed A; shared call/privacy gate, protected-call exception requires UI initiation; no arbitrary raw TTS |
| `calls.get_status`, `calls.set_manual_state` | Detector metadata or explicit manual Active/Clear request; current evidence/effective policy | M/E | Proposed A; manual preference cannot fabricate observations; manual-state mutation while protected requires UI initiation |

### Work Scheduling and Grounded Status

| Tool IDs | Inputs and bounded results | Lanes | Availability / boundary |
|---|---|---|---|
| `work.list`, `work.status` | Session/task IDs/filter; observed states, blockers, resource holds, receipts and known steps | M/E | Proposed A3; no invented percentage or success |
| `work.enqueue` | Session ID, user request, selected context references and dependencies; admitted task/queue position | M/E | Proposed A3; M cannot acquire source content; queuing is not execution approval |
| `work.propose_steps` | Task ID/revision and intended steps; labelled planned-step revision | E | Proposed A3; host receipts alone establish completed effects |
| `work.reorder`, `work.remove`, `work.replace` | Exact affected task IDs/revision/order/replacement; reviewed change/result | M/E | Proposed A3; resolve cancellation/unknown effects before replacement |
| `work.pause`, `work.resume`, `work.cancel`, `work.clear` | Exact session/task/queue or explicit all-session scope; dispatch/cancellation decision and certainty | M/E | Proposed A3; no removal of history/grants; do not cancel unrelated work |
| `work.reconcile` | Exact unknown invocation/receipt reference; supported bounded read-only reconciliation result | E | Proposed A3; no blind write retry or invented rollback |

### Grants, Settings, and Evidence

The R04/R14 native evidence slice delivers the deterministic host-service
semantics behind list/get/search/read_trace for actual SQLite log/audit/span/link
sources, independent DailyLog ordinary diagnostics and explicit opt-in
CombinedLog ordinary list/search/source-qualified cited reads. **All** remains
SQLite-only. CombinedLog pairs independent bounded SQLite/daily snapshots in
source-major order (SQLite commit time/ID, then daily name/offset), without
deduplication, causal rank, audit mirrors or invented graph records. Each
source retains its original time/retention/citation semantics; failure of either
source cannot become a SQLite-only or empty success.
It does **not** register model-facing evidence tools or add them
to the existing action selector. One typed query service supports exact cited
record reads, bounded filters and trace navigation under live local-UI
ownership/privacy admission. Session/conversation and interaction-receipt
sources are unavailable. The proposed model lanes/catalogue below,
Ask Evidence and export still require separate delivery/admission.
Safe text searches only admitted templates and redacted structured values;
properties use exact typed equality, not rendered-message parsing or SQL.
Output uses the service's authoritative serialized page, including metadata
and authenticated continuation, at most 50 records / 64 KiB. Due-but-present
and absent segments never imply a complete history or pruning.

| Tool IDs | Inputs and bounded results | Lanes | Availability / boundary |
|---|---|---|---|
| `approvals.list`, `approvals.inspect`, `approvals.explain` | Grant ID/scope/capability/resource filter or exact action proposal; hashes, applicability/use summary or why approval is needed | M/E | Proposed A3; distinguish consumed single-use, ended session, content-revoked grants, and independently retained perpetual records |
| `approvals.show` | Grant/proposal/filter ID; trusted inventory/editor reference | M/E | Proposed A3; opening editor creates no permission |
| `approvals.request` | Typed exact invocation/resources/identity/digests/destination, grant scope/bound session ID and lineage; host-classified approval proposal | M/E | Proposed A3; M proposes management approval only, not task execution |
| `approvals.propose_edit`, `approvals.revoke` | Exact grant IDs/revisions and scoped edit/remove preview; trusted decision-required/result | M/E | Proposed A3; user action required, no arbitrary expiry or bulk retention deletion |
| `settings.list`, `settings.get` | Category/option ID and scope; registered choices, current/effective value and blockers | M/E | Proposed A3; includes session retention, concurrency, normal/session/in-call feedback, and default-On in-call grant-ignore |
| `settings.propose_change`, `settings.reset`, `settings.undo` | Typed option/value/scope/revision or identified prior change, trusted initiating-channel lineage; reviewed atomic proposal/result or rejected voice-origin request | M/E | Proposed A3; enforce protected-call option classification at request/apply; no file/JSON patch, consent bypass, restored grants or replayed side effects |
| `evidence.list` | Required source kind (`log`, `audit`, `session`, `span`, or `all`), bounded time/trace/session/task/invocation/approval/correlation filters and cursor; ordered permitted summaries plus retention/source/gap status | E; M minimal status | Proposed A3; deterministic app capability, at most 50 records/64 KiB, no model required |
| `evidence.get`, `evidence.get_receipt` | Stable evidence ID or exact trace/session/invocation/action identity; one permitted content-minimising log, audit, span, session or receipt record with typed structured fields/properties and related evidence references | E; M minimal status | Proposed A3; read-only, preserves source kind/authority/value kinds and never exposes secret parameters |
| `evidence.search` | Bounded time/source/trace/span/severity/event/category/correlation/session/task/invocation/approval/action/outcome/admitted-property/safe-text filters and cursor; cited log, audit, span and session matches plus source/gap status | E; M minimal status | Proposed A3; typed filters use structured columns/properties, safe text uses the rendered projection; at most 50 records/64 KiB, no arbitrary SQL |
| `evidence.read_trace` | Trace ID, optional root/span and cursor; bounded parent/child and explicit link graph with cited log/audit/span records | E; M minimal status | Proposed A3; expired segments are gaps, trace context is never authority |
| `evidence.list_audit` | Audit-specific compatibility view over `evidence.list` with typed category/action/outcome/initiator filters | E; M minimal status | Proposed A3; does not read ordinary logs or grant additional authority |
| `evidence.export` | Exact selected permitted evidence and user destination; preview/scoped export outcome | E | Proposed A3; export is a separate write, not an uncontrolled log dump |

The application-level **Ask Evidence** flow is not another recursive model tool.
It creates a user-visible read-only reasoning request, uses the context broker
to call `evidence.list`/`evidence.search`/`evidence.get` against the explicit
source selection, and supplies only the bounded cited results to the chosen
eligible runtime. Returned claims distinguish observation from inference and
retain stable evidence references. The model cannot widen the source/time
scope, approve remote egress, treat record text as instructions, or mutate,
retry or authorize an evidenced operation.

### Context and Artifact Operations

| Tool IDs | Inputs and bounded results | Lanes | Availability / boundary |
|---|---|---|---|
| `context.capture_clipboard` | Explicit user-request lineage and text format; bounded immutable snapshot/provenance | E | Host-only explicit local snapshot/native preview delivered in R07; model tool unavailable pending qualified tool-loop/clipboard-answering/secret-egress gates; not in JSON selector; no polling |
| `context.list`, `context.inspect`, `context.select` | Task/source IDs and approved range; permitted descriptors/selection and freshness | E | Proposed A; selection is not egress consent; no scan of every session/source |
| `context.propose_transmission` | Exact source/derivation IDs and destination; reviewed outbound envelope/proposal | E | Proposed A; host adapter waits; model cannot approve transmission itself |
| `context.select_file`, `context.read_file` | User-selected canonical scope, immutable revision and range; permitted snapshot | E | Proposed C for skill revisions; general user files are replaced by the narrower R26 source/revision/search/excerpt contract; no arbitrary filesystem root |
| `context.sources_list`, `context.source_inspect` | Exact permitted source/revision ID; content-free state, scope, freshness, formats, exclusions and recovery | E; M content-free readiness only | Deferred R26; no filesystem enumeration, raw path disclosure to models or content in management inference |
| `context.propose_source`, `context.refresh_source` | Deliberate user lineage plus reviewed file/folder proposal, or exact registered source/revision; proposal/progress/result | E | Deferred R26; host picker/review confirms path and scope, model cannot confirm or expand the root |
| `context.disable_source`, `context.remove_source` | Exact source/revision and reviewed disable/deletion scope; state/cleanup receipt | E | Deferred R26; removal deletes Kora-owned derivatives, never original files; failures remain visible |
| `context.search`, `context.read_excerpt` | Exact admitted source/revision set, bounded query/budget or citation ID; ranked citations and exact bounded excerpts | E | Deferred R26; host-owned retrieval, prompt-injection separation, access/retention/egress revalidation and citation enforcement |
| `artifacts.export` | Exact retained artifact/digest and user-selected destination; preview/write receipt | E | Proposed A4; show source/classification; no automatic opening/execution |
| `context.capture_screen` | Explicit selected window/region and user intent; bounded snapshot/provenance | E | Deferred; no ambient screenshots or microphone/audio content |

### Skills and Registered Execution

| Tool IDs | Inputs and bounded results | Lanes | Availability / boundary |
|---|---|---|---|
| `skills.list`, `skills.inspect` | Registered source/skill/revision ID; source-qualified descriptor, digest and declared dependencies | E; M descriptors-only | Proposed B; instructions are untrusted and egress-controlled |
| `skills.sources_list`, `skills.propose_source`, `skills.remove_source`, `skills.refresh` | Approved bounded source ID or explicit source selection; registration/discovery proposal/result | E | Proposed B; shared profile sources remain read-only; no whole-profile scan |
| `skills.stage_revision`, `skills.validate`, `skills.test_simulated` | Exact declarative proposal/base digest and bounded fixtures; staged revision, diagnostics/assertions | E | Proposed C; models propose data, never validator code; simulated tests cause no tool effects |
| `skills.save_revision`, `skills.enable`, `skills.disable`, `skills.restore_revision`, `skills.delete` | Exact skill/revision/base digest and host-chosen store target; separately confirmed mutation/result | E | Proposed C (enable/disable in B); save/enable never grants future execution; no deleting bundled/shared files |
| `skills.invoke` | Pinned admitted skill/revision, schema-valid inputs and context IDs; new task/invocation reference | E | Proposed B/C; normal scheduler, egress and action approvals |
| `execution.prepare`, `execution.invoke` | Registered task/implementation ID, complete dependency digests, declared invocation/resources; review proposal/admitted receipt | E | Deferred for general scripts/apps; fixed bundled actions use dedicated computer tools; no arbitrary shell strings |
| `execution.status`, `execution.cancel` | Exact Kora-owned invocation ID; observed receipt/cancellation certainty | E | With admitted execution capability; never arbitrary process-ID/name killing |

### Computer Controls and Notify-Only Maintenance

| Tool IDs | Inputs and bounded results | Lanes | Availability / boundary |
|---|---|---|---|
| `computer.lock` | Fixed current Windows session action/version, host-bound Kora work-session/request lineage; exact approval/invocation receipt and authoritative lock observation | E | Proposed A; common gate and durable standalone control-session binding for direct/model/UI/skill routes; Session offered only after committed Active binding; no other-user or unlock capability |
| `computer.propose_shutdown`, `computer.propose_restart` | Fixed local graceful action and direct-request lineage; pending host proposal with all-session work impact | M/E | Proposed A; proposal-only in both lanes; host lifecycle controller owns approval/countdown and dispatch through the admitted gateway/worker; no M task tools, forced-close or remote target |
| `computer.cancel_power_action`, `computer.power_status` | Exact owned proposal or current factual status; cancellation/stage receipt | M/E | Proposed A; cannot cancel unrelated OS actions |
| `maintenance.get_status`, `maintenance.get_release` | Host-published check/selected release ID; observed availability/state and permitted untrusted notes | M/E | Proposed A; read-only snapshots of trusted maintenance; no triggering checks, changing feeds or invoking updater |

Update checking and canonical release-page opening remain deterministic trusted maintenance commands, outside the model-tool/work-queue channel.
The model may summarize an admitted snapshot, not manufacture a release or authorize browser navigation.
The deterministic priority lock route needs no model lane; its identity and
scope rules are [canonical in Bundled Skills](Built_In_Skills.md#standalone-lock-work-session-binding).
An E designation does not require inference to execute an exact local control.

### Diagnostics, Viewers, and Admitted Integrations

| Tool IDs | Inputs and bounded results | Lanes | Availability / boundary |
|---|---|---|---|
| `diagnostics.list`, `diagnostics.read` | Host-enumerated Kora daily-log ID, bounded range; content-minimising file metadata | E | Daily files retain the full permitted `ILogger` stream alongside the SQLite projection. Reader maximum 1,000,000 characters, model excerpts at most 64 KiB; no arbitrary paths |
| `diagnostics.export` | Selected permitted daily-log IDs and explicit user destination; preview/export receipt | E | Proposed A; structured cross-source records use `evidence.export`; no silent upload or credential/crash-dump harvesting |
| `content.preview`, `content.navigate`, `content.load_assets` | Exact immutable HTML/Markdown/artifact or approved destination/finite assets; isolated viewer/proposal/result | E | Text/Markdown in A4; browser/static HTML/diagrams after renderer gates; display is not scrape/automation |
| `connectors.list`, `connectors.inspect`, `connectors.discover_tools` | Registered connector/account/tool IDs; admitted schemas, health and capabilities | E; M minimal status | Proposed B; no arbitrary MCP-server installation or credential exposure |
| `connectors.propose_configuration` | Registered connector/config schema and user-selected identity reference; trusted setup proposal | E | Proposed B; supported secure sign-in remains host-only |
| `knowledge.search`, `knowledge.read` | Explicit admitted source/query or document ID/range; permission-checked bounded citations/content | E | Deferred; freshness/access/index deletion gate, no blanket enterprise cache |

External connector tool invocations use their own admitted versioned IDs through the same gateway; there is no generic model-selected `connectors.call_anything` escape hatch.
Registered extension settings appear through `settings.*`, not an unrestricted integration-config writer.
Future Git/repository/desktop automation tools require separate explicit catalogue additions and release gates; they are not implicitly supplied by `execution.invoke`.

## Host-Only Operations: Never Model Tools

- Submit a user's question answer, approve/consume authorization, create/edit/remove a grant without trusted confirmation, or assert user identity/speaker confidence.
- Assign host risk, override mandatory policy, select protected trust roots, mint dispatch tokens, forge receipts, or mark an observed effect successful.
- Open/retain microphone capture or access raw ambient audio, enrollment samples/templates, passwords, tokens, or credential-store values.
- Change resource leases/concurrency gates directly, replay queued work on restart, run retention SQL, or delete grant records as session cleanup.
- Download/install/update Kora, mutate its application/source/executable configuration, or select arbitrary installers/assemblies/worker executables.
- Invoke the separate maintenance channel, including update checks, release-page navigation, staging, or activation; read-only published metadata is not access to that channel.
- Direct database/filesystem/shell/process access, unrestricted model-generated code execution, browser host bridges, or renderer/plugin installation.
- Windows UAC/Hello/OAuth/secure-desktop decisions, cross-build ownership tickets, or automatic second-instance listening.
- Frequent-speaker sample selection/adaptation, verification enrollment/template writes, match/identity claims, or granting permissions from a learned profile; profile-management proposals cannot perform these trusted steps.

Models may request a supported setup/presentation/approval workflow; these exclusions prevent completing its trusted step on the user's behalf.
Proactive notifications come from trusted host events. A model can phrase eligible content but cannot manufacture release availability or repeatedly reopen denied approvals.

## Catalogue Maintenance and Acceptance

Every new internal model capability must add an explicit descriptor/table entry and associated acceptance evidence before exposure.
Adapter aliases map to these IDs without changing semantics; unknown names fail validation, not fuzzy dispatch.
Test registry-to-catalogue coverage, all caller-lane restrictions, schema bounds, resource/lineage/revision checks, and truthful pending/unavailable results.
Exercise each admitted operation through equivalent voice/UI intent, including local-only and UI-only in-call feedback.
Verify models cannot self-answer/self-approve, expire/evict perpetual grants, bypass revoked source access, or gain tools via rendered/history instructions.
Optional voice-profile workflows prove separate consent, protected local storage, data minimization, no hidden ambient/history learning, and no biometric data in model results; unsupported learning never disables baseline voice.
Each proposed/deferred capability remains absent from advertised runtime tools until verified; a document table is not an implementation or authorization grant.
