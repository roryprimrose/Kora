# Kora Tool and Built-In Skill Technical Reference

Status: design-defined capability reference, with current implementation labels.
Tool names and input/result shapes are proposed documentation contracts,
not a published SDK, MCP API, or claim that these tools are registered today.

Exception: the six canonical read-only discovery/version/readiness/runtime IDs
now have a [composed bounded host registry](Internal_Model_Tools.md#delivered-bounded-read-only-host-foundation).
This is not a model tool/result loop or general registration of this reference.

Related: [Canonical Tool Catalogue](Internal_Model_Tools.md), [Interaction Contract](Commands_Tools_And_Skills.md), [Sessions](Interaction_And_Sessions.md), [Architecture](Architecture.md), [Phrase Catalogue](OOTB_Phrases.md), [Bundled Skills](Built_In_Skills.md), [User Guide](../docs/tools-and-built-in-skills.md), [Acceptance Criteria](Acceptance_Criteria.md).

## Scope and Availability

This catalogue covers the internal operations and named bundled skills
specified by the current design. It does not invent tools for unspecified
connectors, providers, or future integrations.
The [Internal Model Tool Catalogue](Internal_Model_Tools.md) owns canonical
IDs, schema families and caller lanes. This companion explains those contracts
and their current host equivalents, not a second registration namespace.
Rows can describe several user behaviors of the same canonical tool.
The `host.*` labels identify UI/control flows only, never model tools.

**No formal model/tool-result loop or script-backed bundled skills ship in the
current bootstrap.** Exact commands dispatch C# handlers; unmatched requests
can produce one local-model answer, question, action, or grant-change proposal.
The current model cannot call the proposed logical tools below directly.

See the [Implementation Status and Delivery Roadmap](Implementation_Roadmap.md)
for source/test evidence, delivered-versus-outstanding scope, dependencies and
the acceptance gates required before these planned contracts are advertised.

For local-runtime availability and setup, the
[R02 technical outcomes](Local_Inference.md) distinguish identified/ready from
qualified: public metadata and missing-runtime tests do not prove answer
quality, CPU-floor resources, usable context or OS-enforced offline operation.
`runtime.*` queries must describe actual tested capabilities/budgets, not
advertised model limits or experimental streaming. Setup remains host-only;
R10 consumes measured per-volume requirements and compatibility from the
[R02 continuation](Implementation_Roadmap.md#r02-local-inference-continuation).
No new tool or production capability is introduced by that proof.

The [R02 runtime/provider outcomes](Runtime_Provider_Feasibility.md) establish
an experimental control-point candidate, not new registered tools. Host
authorization, all-status result sanitization and final serialized-request
egress checks are required; a success-only SDK tool hook cannot mediate
failed results. Runtime availability must retain the pending .NET,
global-observation and account/provider gates.

The **Current host behavior** column means:

- **Current:** the listed host behavior exists through an exact command or UI.
  The proposed model-facing tool contract remains planned.
- **Partial:** a narrower host behavior exists; the row describes the difference.
- **Planned:** the described operation is not available in the current release.

The **Lane** column identifies the intended entry boundary:

- **E:** eligible session execution-runtime tool/proposal, mediated by the host.
- **M:** minimal management/routing query or proposal, never task execution
  or unrestricted source access. **M/E** means both admitted lanes, each
  constrained to its permitted content and effects.
- **H:** host-only UI/control/service flow, not a model-executable capability.

Some functions are host-only even though users can ask for them verbally.
Opening or explaining a host flow never grants a model its execution authority.
An operation's presence in this reference is not its enablement or permission.
M/E power proposals have the same proposal-only authority:
[the deterministic host lifecycle controller](Security_Data_Flows.md#management-power-proposal-authority)
owns approval/countdown and dispatch through the admitted gateway/worker.

## Common Invocation Contract

Every admitted invocation carries an invocation ID, user-request/task lineage,
logical capability ID and schema version, validated typed inputs, target scope,
deadline, and any selected source-qualified skill revision.
The host assigns effects/risk and resolves identities; neither is accepted as
authority from model or skill text.
Session/task IDs are mandatory where applicable and resolved from deliberate
addressing, never inferred from queue position or the selected window.
Work sessions, Windows login sessions and provider conversations are distinct.

Responses distinguish `success`, `failure`, `unknown`, `denied`, and `cancelled`,
and carry bounded structured data, observation time, provenance/classification,
and an action receipt when applicable. Approval-required work first produces a
separate host proposal/prompt identity; it is not a successful action result.
Pending user decisions, blocked/unavailable capabilities and revision conflicts
are also explicit outcomes, not successful completion.
Validation failures leave the previous state unchanged and report a bounded
reason. No implicit clamping, arbitrary execution, or write retry after
uncertain effects is allowed.

For example, a proposed state query can return:

```json
{
  "invocationId": "query-17",
  "capability": "sessions.get",
  "sessionId": "session-12",
  "status": "success",
  "observedAt": "2026-10-04T12:00:00Z",
  "data": {
    "activity": "waiting_for_approval",
    "activeTaskId": null,
    "pendingApprovalId": "approval-9",
    "listening": true
  },
  "provenance": { "source": "host-state" }
}
```

Queries return data without opening a window or speaking. Explicit
presentation actions change a surface without claiming to query or execute
unrelated work. The host presenter supplies visual/speech equivalents under
privacy and call policy.

All local reads enforce source/identity scope. Every remote transmission,
including skill summaries/instructions and tool results, follows egress policy.
Read-only does not mean public or automatically approved for a remote model.
Default model-bound tool data is capped at 64 KiB UTF-8 and labelled if bounded.
Tool deadlines default to 60 seconds unless the registration specifies otherwise.

Approval replies, prompt choices, sign-in, and user-presence evidence bind to
host-owned identities. They are not tools the model can call to approve itself.
Every subsequent invocation is revalidated; a prior result or grant does not
introduce new user intent.
History pages contain at most 50 events within the model-result bound.
Deliberate voice and UI support ordinary review/confirmation equally; mandatory
OS/provider authentication remains host-owned. Protected calls additionally
reject voice-originated voice/in-call configuration changes, even if a later
confirmation uses UI.

## 1. Application and Discovery

Dependencies: the running host and native surfaces, not PowerShell or inference.
Model-selected disruptive lifecycle operations retain applicable approval and
active-work checks; essential direct controls remain locally reachable.

| Logical operation | Inputs | Result / behavior | Lane | Current host behavior |
|---|---|---|---|---|
| `application.show` | None | Show/activate the existing instance; return surface state | M/E | Current: `ShowApplication` |
| `application.hide` | None | Hide interaction surfaces; retain listening/tray/work state | M/E | Current: `HideApplication` |
| `application.request_exit` | None | Graceful teardown; disclose affected work and uncertain effects; never wait for a final model answer after exit | M/E | Current: `ExitApplication`; full designed work confirmation is planned |
| `application.request_restart` | None | Restart this version, not Windows or an update; no queued-work replay | M/E | Current: `RestartApplication`; full ledger semantics are planned |
| `application.open_settings` | Optional registered category | Open/activate native Settings; change no preference | M/E | Current: `OpenSettings`; category routing is planned |
| `readiness.show` | None | Open readiness and supported setup choices; install nothing | M/E | Current: `OpenSetup` |
| `application.open_documentation` | Optional known page ID | Open embedded user guide; never execute document content | M/E | Current: `OpenDocumentation`; model-selected page routing is planned |
| `capabilities.list`, `capabilities.get` | Bounded page / canonical ID | Six versioned read-only descriptors with shapes/effects/lanes/limits; no grants | M/E | Delivered bounded host registry and exact native commands; no model adapter/skills catalogue |
| `application.get_version` | None | Actual running version/build string; deployment explicitly unobserved | M/E | Delivered bounded host handler; existing `ShowVersion` behavior preserved |

Same-build activation and cross-build takeover/return belong to the host's
[instance-coordination flow](Instance_Coordination.md), not a tool for another
agent to acquire assistant ownership.

## 2. Session, Work, Power, and Readiness Queries

Dependencies: authoritative session state, work/setup ledger, dependency probes.
Sensitive task labels and approval details may be withheld. A query does not
cancel work, grant an action, or imply that a plan is complete.

| Logical operation | Inputs | Result / behavior | Lane | Current host behavior |
|---|---|---|---|---|
| `sessions.get` | Explicit Kora session ID | Observed work-session state, current work and pending references; host listening state remains separately scoped | M/E | Partial: snapshots accompany local reasoning; no durable session query contract |
| `work.status` | Optional task ID | Observed task/stage/blocker and receipt references | M/E | Partial: `ShowStatus` describes setup/activity |
| `work.status` | Optional task ID | Known remaining steps and queue; unknown steps/ETA explicit | M/E | Partial: setup queue only through `ShowStatus` |
| `work.status` | Optional task ID | Observed stage and measured percentage, if available | M/E | Partial: `ShowCurrentTaskProgress` covers setup tasks |
| `work.list` | Optional state filter | Ordered task IDs/safe labels/states, capacity, pause reason | M/E | Planned: current setup list is not the general request queue |
| `computer.power_status` | None | Owned power proposal/confirmation/countdown and cancellation availability | M/E | Partial: `ShowPowerStatus` reports non-executing proposals |
| `readiness.get` | Bounded page | Timestamped recorded probe/setup observations with safe missing/blocked/unobserved reasons | M/E | Delivered bounded host handler, no reprobe; full designed catalogue remains planned |
| `readiness.refresh` | Optional registered requirement ID | Repeat bounded probes without capture or installation | M/E | Current: Refresh/device discovery; typed scoped contract is planned |

Status snapshots carry observation time. Use a fresh host query when a model
answer depends on newer state. Management gets the minimal approved ledger
envelope, not task tools, clipboard, or skill instruction bodies.
See [Work Management](Work_Management.md) and [Environment Setup](Environment_Setup.md).

## 3. Work Management and Conversation

Inputs resolve against stable ledger IDs, not queue positions or ambiguous
pronouns. Mutating management proposals include an expected ledger revision.
Deliberate voice/UI confirmation and mandatory OS/provider checks apply as
specified by work management;
admission or scheduling never approves task execution or egress.

| Logical operation | Inputs | Result / behavior | Lane | Current host behavior |
|---|---|---|---|---|
| `work.enqueue` | Request, explicit context references, optional dependencies | Admission/clarification decision and task ID/position | M/E | Planned |
| `work.reorder` | Task ID, before/after task ID, expected revision | Validated new order, not concurrent execution | M/E | Planned |
| `work.remove` | Pending task ID, expected revision | Pause and confirm exact removal; unrelated work retained | M/E | Planned |
| `work.pause` | None | Immediately prevent new dispatch; active operation continues | M/E | Planned |
| `work.resume` | Expected revision | Revalidate dependencies, permission and uncertain effects before dispatch | M/E | Planned |
| `work.clear` | Session/queue ID, expected revision | Pause; voice/UI-confirm displayed removal set | M/E | Planned |
| `work.cancel` | Current/resolved task ID | Pause dispatch; confirm destructive cancellation; return certainty | M/E | Partial: `CancelTask` cancels setup/local reasoning and proposals |
| `speech.stop`, `work.pause` | Exact session/dispatch scope | Compound stop control stops speech and pauses dispatch; cancellation remains separate | M/E | Partial: current exact `stop` means `CancelTask`, not this future compound control |
| `work.cancel` | Explicit all-session scope and expected revisions | Pause; voice/UI-confirm active cancellation and pending clear | M/E | Planned |
| `work.replace` | Task ID, new request/context, expected revision | Confirm cancel/replacement; wait for quiescence/uncertainty decision | M/E | Planned |
| `work.replace` | Request/task ID, correction | Clarified revised request; changed approvals invalidated | M/E | Planned |
| `sessions.delete` | Session ID/revision and affected work/data set | Confirm exact deletion; resolve live/unknown work; independent perpetual grants and saved skills retained | M/E | Planned |
| `presentation.navigate` | Response ID, item ID or next/previous | Existing result selection; do not rerun tools | M/E | Planned |
| `speech.read_once` | Response ID | Existing safe summary under speech policy; no new task | M/E | Planned |

One task executes per session; independent sessions may execute concurrently,
initially two slots within verified configurable provider/hardware limits.
Resource leases prevent conflicting read/write effects; incomplete declarations
require an enforced exclusive domain or rejection. Defaults: 10 pending entries
per session, 30-minute pending lifetime, five-minute active deadline excluding
user waits. Full queues never evict silently. Failed/cancelled/unknown work
pauses affected dispatch without cancelling unrelated sessions.
Remote management is independently bounded to one request, 32 KiB input,
4 KiB output, 15 seconds, and 30 calls per rolling hour per profile.
These are UTF-8 byte bounds on the complete outbound model body and typed
proposal JSON, with a host dispatch deadline and at most one forwarded
inference attempt; SDK retries do not enlarge the allowance. See the
[management envelope](Work_Management.md#management-operating-envelope-and-degraded-mode).

## 4. Audio, Listening, Calls, and Notifications

The host owns capture consent, device lifetime, playback and notification
events. No runtime receives ambient audio or enrollment material.
Device selection alone never authorises microphone acquisition.
The [microphone matrix](Security_Data_Flows.md#microphone-consent-and-enablement-matrix)
governs ongoing consent, ordinary startup and current-run recovery; models
cannot open capture or treat endpoint selection/testing as consent.

| Logical operation | Inputs | Result / behavior | Lane | Current host behavior |
|---|---|---|---|---|
| `speech.stop` | None | Stop current/queued playback; task continues | M/E | Current: `StopSpeaking` and UI Stop |
| `speech.read_once` | Existing response ID | Request one identified response aloud; bypass call gate only where allowed | M/E | Planned |
| `settings.list` | Capture/render/voice category | Stable device/voice IDs, default/selection/readiness facts; no recording | M/E | Current: device/voice discovery UI |
| `host.audio.preview` | Selected provider/voice/output IDs | Explicit local preview and observed output failures | H | Current: voice Preview UI |
| `speech.mute` | None | Release capture, clear audio and stop speech as specified | M/E | Current: Disable listening UI; verbal mute control is planned |
| `host.listening.enable` | Selected device and explicit consent | Revalidate unlocked state and readiness before acquisition | H | Current: Enable listening UI; closed microphones cannot receive voice unmute |
| `calls.get_status` | None | Aggregated observations, sources, freshness, limitations, effective suppression | M/E | Partial: call gate/status exists; automatic Windows detection is unavailable |
| `calls.set_manual_state` | In-call/ended and trusted initiating channel | Set manual source; never overrule automatic blocking evidence; protected-call changes require a new UI request | M/E | Planned |
| `host.notifications.respond` | Current event ID, defer/suppress/dismiss, bounded duration | Modify this eligible notification/reminder, not its proposed effect | H | Planned |

Audio/voice selection, speech rate/volume, response mode, quiet hours, detector
preferences and temporary overrides use the configuration operations below.
Kora never changes global Windows mute/volume to force output.
Optional speaker enrollment/deletion is a separate secure host workflow.
Automatic Teams/Graph and other detectors require their own proof and consent;
Graph is unavailable for local-only operation.
See [Call-Aware Speech](Call_Aware_Speech.md) and [Proactive Interaction](Proactive_Interaction.md).

## 5. Context and Clipboard

| Logical operation | Inputs | Result / behavior | Lane | Current host behavior |
|---|---|---|---|---|
| `context.capture_clipboard` | Explicit current-user snapshot request | Immutable text/context ID, size, source/time and classification; not monitoring | E | Partial host-only R07 local snapshot/native preview delivered; model tool unavailable pending qualified local loop/answering and secret/egress gates |
| `context.inspect` | Existing approved context ID | Policy-filtered captured content and provenance, not a fresh read | E | Host native preview/same-ID reuse only; model tool and general context store remain planned |
| `context.select` | Existing context/result reference and task ID | Bind explicit approved input; clarify expired/missing/ambiguous references | E | Planned |

Clipboard capture requires deliberate scoped voice/UI intent before read,
not compulsory speaker authentication. Limit plain text to 256 KiB UTF-8; reject unsupported/empty/locked/oversized
cases distinctly. Never silently truncate or recapture at queue dispatch.
"Explain the clipboard" combines capture, approved model processing, and
presentation; it is not a separately named bundled skill in this design.
General file/workspace selection, OCR and screen capture remain later
capabilities without final tool contracts.

## 6. Typed Configuration

These operate only on registered options. No arbitrary file path, JSON patch,
credentials, policy implementation or executable reference is accepted.
Option schemas provide type, units, ranges, scope, defaults, dependencies,
confirmation, and application timing.

| Logical operation | Inputs | Result / behavior | Lane | Current host behavior |
|---|---|---|---|---|
| `settings.list` | Optional category | Available option descriptors and unsupported dependencies | M/E | Partial host-only native/exact discovery: appearance, installed speech, assistant prefix, input/output, summary caps, volume and device-default mode; model tool and remaining categories planned |
| `settings.get` | Registered option/category ID | Saved/effective value, scope and limitation | M/E | Partial host-only exact reads of admitted IDs report revision/provenance/recovery; audio/mode status is observational and scoped, not broader task/queue authority |
| `settings.propose_change` | Option ID, typed value, scope, expected revision | Validate/stage/confirm/apply atomically; uncertain committed outcomes require inspection | M/E | Partial: admitted native/exact setters share host-owned proposals, revision rechecks, atomic persistence/audit/readback and original-channel gates where applicable; failed terminal evidence is not a rollback claim; no model tool |
| `settings.propose_change` | Identified temporary preference, target default scope | Exact compatible default-change proposal; no grant promotion | M/E | Planned |
| `settings.reset` | Option/category, expected revision | Preview exact defaults/affected state; confirm; do not delete skills/credentials | M/E | Partial: native/exact per-option reset retains declared affected selection, revision checks and applicable original-channel gates; volume resets to unity 100, device mode to Hybrid, without replay; category/whole-profile reset and model tool planned |
| `settings.undo` | Compatible prior change ID, expected revision | Revalidate prior preference only; never restore consumed grants or effects | M/E | Planned |

The complete option registry is specified in [User Configuration](User_Configuration.md):

The delivered appearance descriptors declare stable ID/type/units/default/bounds,
device-local scope, appearance-only effect, local-host availability,
immediate-after-save timing and per-option reset. They are not call-sensitive,
including visual playback scaling. This is separate from the Tools registry
and adds no model invocation authority. Response pin/topmost/position retain
direct UX outside this independent-file registry; full verbal preferences,
temporary scopes and undo remain open.

The delivered schema-version-1 installed speech registry separately admits
`speech.provider` and `speech.voice`, with installed choices, advertised
defaults, desired/effective values, saved provenance, explicit recovery,
process-local revision and voice-output effect. Exact `list speech settings`,
`get`, `set` and `reset` use the same host workflow as native selection/reset.
Explicit provider-qualified voice choices atomically select the provider/voice
pair; ambiguous, missing or uninstalled choices cannot be selected. The existing
protected-call gate preserves original channel and observed call revision.
This remains host-only: none of the proposed `settings.*` model tools is
registered or executable. Rate remains planned; separately delivered per-Kora
volume and spoken-summary caps do not extend provider/voice authority.
See the [bounded contract](User_Configuration.md#delivered-bounded-installed-speech-choices-r10).

The other delivered host-only subsets and exact IDs are authoritative in
[User Configuration](User_Configuration.md#configuration-contract). Input/output
selection is preference-only, not capture, consent, a trial or global mixer
control. Volume 0 withholds synthesis/autoplay with complete visual recovery;
raising/resetting never replays. `responses.default-mode` admits only the device
default, not session/task/queue overrides, fallback or call-policy changes.
The legacy muted-output fallback remains independently stored outside this registry.

| Category | Covered options | Current host subset |
|---|---|---|
| Voice input | Device, listening, assistant name, PTT shortcut, activation cue, start wait, endpoint silence, utterance limit, language/model, owner-aware private speech | Exact input preference and assistant-prefix native/exact controls; separate listening/PTT UI; production wake profiles and remaining controls planned |
| Output/appearance | Mode/scope, output device, local voice, rate, Kora volume, summary length, detail, captions, theme, timeout, presence size/dot size/speed/placement, reduced motion | Native/exact device-default mode, output preference, installed provider/voice, Windows-native -10..10/default0 rate (Kokoro unsupported), 0-100 Kora volume, summary caps and nine appearance options; broader scoped registry/provider rate/detail controls planned |
| Calls/proactive | Speech mode, visual override, activation, Unknown handling, sources/accounts, Busy/DND, temporary override, consent, quiet hours/mode, categories, deferral | Detected-call visual/activation UI; automated/manual sources and richer preferences planned |
| Work/context | Per-session queue capacity/dispatch/lifetime, admitted concurrency, active deadline, archive/deletion durations, clipboard/result limits | General work/context registry planned |
| Providers/connections | Processing mode, default provider/model, endpoint, identity, connector enablement | Pinned local setup exists; general selection and secure connector flows planned |
| Skills/local data | Shared sources, revision/default binding, refresh, independent SQLite diagnostic/audit retention, diagnostic verbosity | Native/exact future-only diagnostic 1–365/default-reset30 and audit 30–365/default-reset90 are delivered; old deadlines/all grants stay unchanged; apply-now/audit pruning unavailable. General skill registry/verbosity controls remain planned |
| Startup/updates | Logon registration, startup presentation, notify-only checks/interval/channel/reminders | Native logon/notify-only maintenance and exact cached status/review/eligible current-run snooze; general registry planned, Check/Open remain native-only, no install-capable updater |
| Content viewing | Captions/dismissal/placement, text scale, automatic detail, browser target, Markdown/source, Mermaid | Embedded documentation/basic response UI exists; general typed viewers planned |

The existing presentation resolver orders task then queue then device; this is
not delivered durable session/task/queue override configuration. Manual listening disablement
and temporary call state do not persist across restart. Remote/privacy expansion
requires exact voice/UI confirmation; secure enrollment retains mandatory
host/OS checks. In-call feedback defaults to UI-only and is separate from
input eligibility and default-On `calls.ignoreReusableGrants`.
Protected calls require fresh Once approval instead of Session/Perpetual reuse;
the reusable records remain unchanged. Voice-originated voice/in-call settings
changes, reset/undo and speak-once exceptions are rejected, not deferred.

## 7. Grants and Audit Inspection

The model can request information or propose a change, never issue the
confirmation callback or assert its own permission.
Security changes are host-classified and visible, with additional OS presence
checks where required. Broadening/restoring grants requires a new exact approval.

| Logical operation | Inputs | Result / behavior | Lane | Current host behavior |
|---|---|---|---|---|
| `approvals.list` | Optional state/capability/source filter | Safe grant IDs, exact scope/identity/status, not sensitive payloads | M/E | Partial: `ListGrants` shows session/always action-name grants |
| `approvals.inspect` | Grant ID | Scope, origin, applicability/use history and exact resources/digests; no perpetual expiry | M/E | Partial: action-name inventory only; detailed resource receipts planned |
| `approvals.explain` | Capability and resolved resource/provider/skill | Matching grant/reason or required new approval | M/E | Planned |
| `approvals.propose_edit` | Exact grant/action/resource set, revision and permitted scope edit | Separate host proposal; never execute named action, add perpetual expiry or commit silently | M/E | Partial: `ManageGrants`/model grant proposals support confirmed Add/Remove/Move for action-name grants |
| `evidence.list` | Required log/audit/session/span/all source, bounded metadata/time/trace/session/task/invocation/approval/correlation filters and cursor | Ordered source-labelled records with authority/retention/gap status | E; M minimal status | Planned; deterministic app capability |
| `evidence.search` | Bounded source/time/trace/span/correlation/session/task/invocation/approval/action/admitted-property/safe-text filters and cursor | Cited log, audit, span and session records with typed fields/properties and source/authority/gap status; no arbitrary SQL | E; M minimal status | Planned; deterministic app capability |
| `evidence.get`, `evidence.get_receipt`, `evidence.list_audit` | Stable evidence ID or exact trace/invocation/session/action filter | Content-minimising log/audit/span/session record preserving structured value kinds, or observed outcome with stable citation | E; M minimal status | Planned |
| `evidence.read_trace` | Trace ID, optional root/span and cursor | Bounded parent/child and explicit-link graph with cited log/audit/span records and visible expired segments | E; M minimal status | Planned; trace metadata grants no authority |
| `evidence.export` | Selected metadata set and explicit destination | Preview and separately approved content-minimising export | E | Planned |

The app's planned **Ask Evidence** experience orchestrates these read-only
operations from a visible source/filter/record selection. It is not a
model-callable recursive tool: the context broker supplies only the selected
bounded evidence to the chosen eligible runtime, applies remote-egress review,
and requires cited answers that distinguish observed records from inference.

Bulk revocation resolves provider/account/resource/skill-revision/capability
to a displayed exact set. Revocation blocks new dispatch immediately; report
in-flight remote work truthfully.
Current model-action Once/Session/Always grants are not script or hash grants.
Future Once is consumed once; Session binds an operation to the identified
durable Kora work session and ends with it. Always/Perpetual records survive
restart/archive/deletion independently, without expiry, retention or eviction.
Inapplicable or content-revoked records remain visible until explicitly removed.
The future gate binds each executable resource, invocation and context; any
observed mismatch revokes affected grants and cannot be undone by restoring bytes.
See [Security](Security_Data_Flows.md) and [Execution Design](../docs/skill-and-task-execution-design.md).

## 8. Skill Discovery, Registry, and Authoring

Model-facing entries in this section remain **Planned**. The local native tray
now offers **Skill packages (inspection only)** for the fixed bundled
lock/shutdown/restart catalogue; it is not registered as an unqualified
model tool and leaves R06 caller lanes unchanged. Exact embedded bytes and
hashes are discoverable locally without enablement or approval authority.
All package invocation descriptors remain unavailable pending worker,
deployment, network and real-control admission.
R21's separate **Shared profile sources (read only)** native window admits
explicit bounded registration/list/immutable inspection/recheck and exact
**Withdraw local read consent** confirmation. Withdrawal binds source ID,
directory identity, profile-relative root and observed registration revision;
it removes only Kora's registration and owned snapshots, even when the original
root is missing. Shared files and unrelated registrations are untouched.
Audit/write/read-back failures are unconfirmed, not rollback or success.
This host-only affordance is not a `skills.*` tool, enable/disable/invoke,
model exposure or grant change. See
[the delivered boundary and recovery](Skill_Storage.md#delivered-bounded-r21-native-inspection).
Source roots are bounded, explicit,
read-only registrations; discovery is not execution, enablement, or remote egress.
Skill identity includes partition/source, declared ID and digest-pinned revision.
The host owns paths under the Kora Roaming AppData store.

| Logical operation | Inputs | Result / behavior | Lane |
|---|---|---|---|
| `skills.list` | Optional source/state filter | Bundled/shared/Kora-specific summaries, compatibility and availability | E; M descriptors-only |
| `skills.inspect` | Source-qualified skill/revision | Definition, requirements, selection guidance and registered task identities | E; M descriptors-only |
| `skills.propose_source` | Explicit bounded source selection | Reviewed read-only source registration; no recursive whole-profile scan | E |
| `skills.refresh` | Registered source ID | Bounded discovery and changed/incompatible revision report | E |
| `skills.enable` | Exact source-qualified revision | Separate voice/UI-confirmed enablement; no executable grant | E |
| `skills.disable` | Exact skill/revision | Block new calls; cancel supported active calls truthfully | E |
| `skills.delete` | Exact owned saved skill/revision | Confirm removal; never delete bundled/shared source bytes | E |
| `skills.invoke` | Enabled skill/revision, typed inputs and request lineage | Resolve pinned instructions/workflow and admitted references, not script dispatch | E |
| `skills.stage_revision` | Desired behavior/inputs/outcomes | Staged declarative manifest/instructions/examples in memory | E |
| `skills.stage_revision` | Proposal ID/revision, explicit refinement | New proposal/diff; prior proposal approval invalidated | E |
| `skills.validate` | Exact staged revision | Built-in schema/dependency/instruction-risk findings; no code launch | E |
| `skills.test_simulated` | Exact revision and data-only fixtures | Assertions with mocked results; zero real tool effects | E |
| `skills.save_revision` | Validated proposal/revision | Voice/UI-confirmed save, disabled, to host-derived store location | E |
| `skills.invoke` | Exact revision, separately admitted request/context | Real task under ordinary tool/egress/action checks; not simulation approval | E |

User authoring is declarative in Slice C. A fork of a bundled/shared skill has
a distinct ID and attribution; it cannot copy executable trust, replace scripts,
shadow reserved controls, or claim priority. Instruction-only profile
`SKILL.md` compatibility does not admit script snippets.
See [Skill Authoring](Skill_Authoring.md) and [Skill Storage](Skill_Storage.md).

## 9. Content Presentation and Viewing

Model outputs are typed data or presentation proposals. They cannot force
topmost windows, auto-navigate, replace approval chrome or execute page code.
These are not general browser/desktop automation tools.

| Logical operation | Inputs | Result / behavior | Lane | Current host behavior |
|---|---|---|---|---|
| `interaction.show_details` | Existing response/item ID | Show selected full answer/details; no rerun | M/E | Partial: basic response text/document windows only |
| `settings.propose_change` | Sentence/utterance/off; existing playback identity | Playback-bound captions, not transcript or suppressed speech | M/E | Planned |
| `presentation.pin` | Existing item/surface ID, pin/unpin | Retain labelled visible item under presentation/privacy policy; durable history has its own retention | M/E | Partial: response-window Always show; general captions/items planned |
| `interaction.show_details` | Existing item ID | Exact inert source and provenance | M/E | Planned |
| `interaction.show_details` | Existing item ID, admitted profile | Validated diagram or labelled source/error; no renderer install | M/E | Planned |
| `content.preview` | Approved immutable HTML item/source scope | Sanitised static snapshot; no scripts/forms/network by default | E | Planned |
| `content.preview` | Explicit validated URL, embedded/external choice | Approved navigation; neither extraction nor model-context capture | E | Planned |
| `content.navigate` | Owned viewer ID, back/forward/reload/scroll/zoom | Displayed content only; changed network scope rechecked | E | Planned |
| `presentation.close` | Owned viewer ID | Close controlled viewer; do not claim to close external browser | M/E | Planned |
| `content.load_assets` | Existing item and finite explicit asset/destination set | Separate scoped asset approval; no blanket network permission | E | Planned |
| `artifacts.export` | Existing item and explicit target | Preview exact user-selected write; no automatic executable opening | E | Planned |

General Markdown/diagram/HTML viewing remains distinct from the current
embedded-documentation renderer. That renderer accepts only bundled user pages,
uses native controls, and displays links inertly.
Local Markdown needs no cloud renderer. Initial rich-document limit is
256 KiB UTF-8; Mermaid uses 32 KiB/block, 500 nodes, 1,000 edges, two seconds.
Internet navigation is unavailable in local-only/offline mode. Embedded
profiles are initially ephemeral with no borrowed cookies/credentials;
external browser identity/networking is explicitly outside Kora containment.
See [Information Display](Information_Display.md).

## 10. Connections and Diagnostics

| Logical operation | Inputs | Result / behavior | Lane | Current host behavior |
|---|---|---|---|---|
| `connectors.inspect` | Registered provider/connector ID | Health, supported capabilities, selected identity reference, limitations | E; M minimal status | Partial: local inference/dependency readiness only |
| `connectors.discover_tools` | Admitted connector ID | Reviewed tool/schema metadata; no implicit trust or installation | E; M minimal status | Planned: read-only MCP in Slice B |
| `diagnostics.read` | Host-validated Kora daily-log identity, bounded tail request | Content-minimising file excerpt; reject arbitrary paths | E | Planned: current daily logs contain the full `ILogger` stream; model-reader contract does not exist |

The future diagnostic reader permits at most 1,000,000 characters per tail.
Normal model-bound result limits and egress controls still apply; the larger
reader bound is not permission to transmit a megabyte to the model.
Secure provider/connector sign-in and secrets stay in host/OS facilities.
No account substitution or credential content appears in results.

## 11. Host-Only User Flows

These complete the user catalogue but are **not model-executable tools**.
Tool calls may open a relevant existing surface or report its state; only the
trusted host completes the bounded operation after required consent.

| Host flow | Inputs / output | Current behavior |
|---|---|---|
| Dependency setup/reuse/removal | Exact protected catalogue plan, ownership and consent; verified readiness/partial-change receipt | Partial: storage/SQLite, consented PowerShell/Ollama setup and optional Kokoro assets; broader catalogue planned |
| Prompt answer/approval/rejection | Current prompt ID, exact choice/scope, expiry and presence checks; correlated decision | Current for local-model questions/actions/grant changes; broader task protocol planned |
| Listening recovery | Detected endpoint selection and explicit consent; acquisition/readiness result | Current UI/device recovery; stronger production wake proof planned |
| Secure account/enrollment flows | Native OAuth/OS/Windows Hello interaction; opaque identity/enrollment state only | Planned supported remote/biometric integrations |
| Logon startup enable/disable | Kora-only registration and scoped consent; registration receipt | Planned |
| Update check/status/release notes | Protected origin/channel; bounded metadata check, observation time and availability/Unknown | Planned notify-only maintenance |
| Release-page navigation | Exact canonical release URL, native review and user request | Planned; no download/staging/install authority |
| Update defer/suppress/cancel | Existing host event/check ID; notification/check state | Planned; cannot cancel an external installer |
| Same/different-build activation/return | Verified instance/build identities and native handoff prompts | Planned complete coordination contract; not agent ownership transfer |

Maintenance is outside the agent tool/task graph. "Update Kora" explains manual
replacement during the unsigned phase; it never invokes Git/build/install.
Setup cannot alter Kora, its embedded scripts, protected roots or policy.
See [Environment Setup](Environment_Setup.md), [Distribution](Distribution_And_Updates.md),
[Mouse Interaction](Interaction_Fallback.md), and [Instance Coordination](Instance_Coordination.md).

## 12. Built-In Skill Contracts

The three script-backed packages below have a current immutable inspection
catalogue, explicit manifests and exact source tabs. Invocation and content-bound
execution grants remain **Planned**. Package IDs and requested actions are
host-owned declarations, not permission to call a worker.

### Lock the Machine

- **Skill:** `kora.session.lock`; **task:** `session.lock`;
  **model tool:** `computer.lock`.
- **Embedded resources:** `Kora.Scripts.Session.Lock` entry point
  `scripts\session\lock.ps1` and `Kora.Scripts.Shared.SessionControl` helper
  `scripts\shared\session-control.ps1`, as declared by the fixed JSON manifest.
- **Select for:** an explicit request to lock the current Windows session,
  such as "lock my computer"; exact aliases select the original locally.
- **Do not select for:** quoted/retrieved instructions, a how-to question,
  another user/session, unlock, remote target, or timer.
- **Inputs:** none; host derives the current interactive user/session.
- **Dependencies:** supported Windows adapter, verified embedded resource and
  runtime/dependencies, proven containment and session-event observation.
- **Grant:** exact definition digest, complete combined script-set hash,
  individual resource/dependency digests and invocation; Once/Session/Perpetual
  scope where applicable; direct, UI and model routes share the gate.
  An unaddressed standalone request creates a new durable Active control work
  session; commit/present its identity before approval or priority dispatch.
  Session is offered only after [that binding](Built_In_Skills.md#standalone-lock-work-session-binding);
  new control sessions cannot reuse another Session grant.
- **Execution/result:** priority host-admitted route; fixed snapshot execution,
  observed Windows lock receipt; failed/unknown is not confirmed success.
  Microphone/sensitive output policy applies to every lock origin.
- **Current:** exact `LockMachine` calls the native API without this common
  gate; a model-selected lock requires an action-name grant/approval.

### Shut Down the Computer

- **Skill:** `kora.computer.shutdown`; **requested task:** `computer.shutdown`;
  **model tool (proposal only):** `computer.propose_shutdown`.
  `Kora.Scripts.Session.Shutdown` and the single
  `Kora.Scripts.Shared.SessionControl` resource are embedded and digest-bound
  for inspection. Invocation is unavailable.
- **Select for:** explicit graceful shutdown of this local computer.
  Do not infer it from ambiguous "close it", advice, remote targets or quotes.
- **Inputs:** none; no forced-close flag, remote target or arbitrary delay.
- **Dependencies/grants:** protected script/runtime/containment and exact
  content-bound grant, plus per-request power safety confirmations.
- **Execution:** warn about unsaved work, resolve active work/quiescence,
  pause dispatch across all sessions; require fresh "confirm shutdown" within
  30 seconds or an equivalent explicit UI confirmation, plus any mandatory
  OS/provider check. Bind the proposal approval to a two-minute single-use expiry,
  not expiry of a perpetual grant record.
  Start a host-owned 30-second countdown and revalidate before graceful OS request.
  M/E submit a pending proposal only; the host lifecycle controller owns these
  decisions and dispatch through the admitted execution gateway/worker.
- **Result:** proposal/countdown state, OS acceptance/blocker and receipt;
  acceptance is not proof the computer completed shutdown.
- **Current:** `ProposeShutdown` creates a visible non-executing proposal.

### Restart the Computer

- **Skill:** `kora.computer.restart`; **requested task:** `computer.restart`;
  **model tool (proposal only):** `computer.propose_restart`.
  `Kora.Scripts.Session.Restart` and the single
  `Kora.Scripts.Shared.SessionControl` resource are embedded and digest-bound
  for inspection. Invocation is unavailable.
- **Select for:** explicit graceful restart of this local computer/Windows,
  not a Kora application restart.
- **Inputs/dependencies/grants:** same fixed-local constraints as shutdown;
  this is a distinct task/implementation grant, not inherited from shutdown.
- **Execution:** the same work/quiescence/countdown rules, with fresh
  "confirm computer restart" or equivalent explicit UI confirmation, plus
  any mandatory OS/provider check.
- **Result:** proposal/countdown, OS acceptance/blocker and truthful receipt;
  do not promise task resumption, application document saving or completed reboot.
- **Current:** `ProposeRestart` creates a visible non-executing proposal.

### Required Read-Only Integration Skill

Slice B requires one additional bundled skill referencing an admitted
read-only MCP tool by stable identifier. **Its name, ID, connector, inputs,
workflow and concrete outputs have not been selected.**
Document them before enablement; do not advertise a GitHub, Azure DevOps or
deployment skill merely because those integrations are candidates.
It needs a reviewed connector/account/schema, pinned instructions, scoped read
permission, bounded provenance-bearing results and result-egress review.
Bundling never grants remote read/transmission.

For all executable skills, the host computes the complete `Kora.ScriptSet.v1`
identity over ordinally sorted logical names and exact length-framed bytes,
including transitive/shared helpers, plus the separate
`Kora.SkillDefinition.v1` digest over manifest, instructions and fixtures.
Every task in a script-backed skill binds the complete declared set, not just
scripts used on that path. A changed shared helper permanently revokes every
dependent grant, not unrelated grants; restoring old bytes never restores it.
Review and execution use the same immutable bytes without writable extraction.
The host also computes and verifies every script,
interpreter, native adapter and transitive executable dependency identity,
permitted parameters and execution context immediately before dispatch.
Changed/unverifiable resources invalidate affected grants. A review window
shows exact script bytes but cannot approve them by opening.
Power cancellation (`CancelPowerAction`) is a host tool/control for Kora's
owned proposal/countdown, not a separate skill:

| Logical operation | Inputs | Result / behavior | Lane | Current host behavior |
|---|---|---|---|---|
| `computer.cancel_power_action` | Owned pending proposal ID | Cancel before final OS acceptance; report inability afterward, not rollback | M/E | Partial: `CancelPowerAction` cancels non-executing proposals |

## 13. Persistent Sessions and Additional Canonical Contracts

These complete the canonical inventory introduced by the concurrent-session
design. All are **Planned** unless explicitly marked **Deferred** or optional
after evidence. Grouped IDs share a schema family but retain separate effect
descriptors and host gates; they do not introduce generic executors.

### Sessions, Structured Interaction, and Work Evidence

| Canonical tool IDs | Inputs | Bounded result / boundary | Lane |
|---|---|---|---|
| `sessions.list` | Lifecycle/work-state filter and cursor | Permitted metadata, activity/due times and grounded summary; M sees minimal routing descriptors | M/E |
| `sessions.search` | Query, lifecycle/date filter and cursor | Evidence-cited permitted matches; M searches metadata only | E; M metadata-only |
| `sessions.read_history` | Session/event/task/time filter and cursor | Ordered retained messages, decisions and receipts; at most 50 events/page; no implicit resume | E |
| `sessions.read_artifact` | Session/artifact ID, digest and range | Immutable approved source/provenance; historic scripts never execute | E |
| `sessions.create`, `sessions.rename`, `sessions.select` | Topic or exact session ID/title and revision | Committed identity/selection; selection is not resume, grant or execution priority | M/E |
| `sessions.resume`, `sessions.mark_done` | Session ID/revision and work-resolution decision | Explicit lifecycle transition; resolve live/unknown work; no tool/grant replay | M/E |
| `interaction.ask` | Session/task IDs, typed question/options/fields, constraints and consequences | Correlated question and pending state; shared voice/UI draft; submission is trusted host input | M/E |
| `interaction.get_pending` | Exact session/task or permitted metadata filter | Current question/proposal IDs, revisions and deadlines; not another session's generic reply target | M/E |
| `interaction.present` | Session/task IDs, summary and typed full-content items/provenance | Response/artifact identity under shared presentation policy; M cannot expose task source content | M/E |
| `work.propose_steps` | Task ID/revision and intended steps | Labelled plan revision, not evidence of completed effects | E |
| `work.reconcile` | Exact unknown invocation/receipt | Supported read-only reconciliation; no write retry or invented rollback | E |
| `evidence.list` | Required log/audit/session/span/all source, bounded metadata/time/trace/session/task/invocation/approval/correlation filters and cursor | Ordered permitted records plus source/authority/retention/gap status | E; M minimal status |
| `evidence.search` | Bounded source/time/trace/span/correlation/session/task/invocation/approval/action/admitted-property/safe-text filters and cursor | Cited log, audit, span and session matches with typed fields/properties plus source/retention/gap status; no arbitrary SQL | E; M minimal status |
| `evidence.get`, `evidence.get_receipt`, `evidence.list_audit` | Stable evidence ID or trace/invocation/session/action filter and cursor | Permitted structured log/audit/span/session record preserving value kinds, or observed outcome; M receives minimal status, not secret arguments | E; M minimal status |
| `evidence.read_trace` | Trace ID, optional root/span and cursor | Bounded parent/child and explicit-link graph with cited log/audit/span records and visible expired segments; no authority | E; M minimal status |

Work sessions are durable Active/Done streams, not execution slots. Ordinary
voice and UI can ask, choose/deselect options, edit a shared draft, review,
submit or cancel against the same question revision. Only one spoken question
is foreground; other sessions' explicitly addressed UI cards remain usable.
Closing a window changes presentation, not lifecycle or microphone consent.

Persist permitted full history, immutable artifacts and receipts in standard
SQLite/private device-local storage; never raw audio, credentials or
hidden reasoning. Defaults archive after 24 hours and delete after 30 days
from the same meaningful-activity clock. Both durations are configurable;
passive browsing/search does not refresh them. Live/unknown work blocks silent
cleanup. Shortening retention requires a separate apply-now decision for
immediately affected records. Deletion discloses independently retained grants,
audit records, user exports and provider-side copies.
Restart recovers history/selection and Interrupted/Unknown work, not dispatch.

### Discovery, Runtime, Grants, Context, and Supporting Flows

| Canonical tool IDs | Inputs | Bounded result / boundary | Lane |
|---|---|---|---|
| `capabilities.get` | Admitted capability ID | Descriptor, schema, limitations and unavailable reason; no protected paths or implicit enablement | M/E |
| `readiness.propose_setup` | Supported capability IDs | Host-owned dependency/setup plan; installation/sign-in remain trusted host flows | M/E |
| `runtime.list`, `runtime.get_status` | Bounded page / `local.inference` | Delivered host-only locality and recorded health/time/reason; qualification false. No credentials/provider substitution; qualified runtime budgets remain outstanding | M/E |
| `voice_profile.get_status`, `voice_profile.request_manage` | Current-profile status or explicit learned/enrolled workflow and operation | Optional after evidence: coarse readiness/trusted workflow reference only; no samples, scores or inferred identity | M/E |
| `approvals.show` | Grant/proposal/filter ID | Trusted inventory/editor surface; opening it creates no permission | M/E |
| `approvals.request` | Exact invocation/resources/digests/identity/destination, lineage and scope/bound session | Host-classified decision-required proposal; M proposes management approval only | M/E |
| `approvals.revoke` | Exact grant IDs/revisions and removal preview | Trusted voice/UI-confirmed removal; not arbitrary expiry or retention cleanup | M/E |
| `context.list` | Task/source IDs and approved range | Permitted descriptors and freshness; no scan across all sessions/sources | E |
| `context.propose_transmission` | Exact source/derivation IDs and destination | Reviewed outbound envelope; adapter waits; model cannot approve it | E |
| `context.select_file`, `context.read_file` | User-selected canonical scope, immutable revision and range | Proposed C for skill revisions; general user files use the narrower R26 source/revision/search/excerpt contract; not arbitrary filesystem access | E |
| `context.sources_list`, `context.source_inspect` | Exact permitted source/revision ID | Deferred R26: content-free source state, scope, freshness, formats, exclusions and recovery; no raw path/content in management inference | E; M content-free readiness only |
| `context.propose_source`, `context.refresh_source` | Deliberate user lineage and reviewed file/folder proposal, or exact registered source/revision | Deferred R26: host picker/review confirms scope; the model cannot confirm or expand a root | E |
| `context.disable_source`, `context.remove_source` | Exact source/revision and reviewed disable/deletion scope | Deferred R26: immediate new-use revocation and inventoried Kora-copy cleanup; never deletes originals | E |
| `context.search`, `context.read_excerpt` | Exact admitted source/revision set plus bounded query/budget, or citation ID | Deferred R26: permission-checked citations/excerpts with freshness, retention, prompt-injection and egress gates | E |
| `context.capture_screen` | Explicit selected window/region and intent | Deferred: bounded snapshot/provenance; no ambient collection or audio | E |
| `skills.sources_list`, `skills.remove_source` | Registered bounded source ID and revision | Source metadata or confirmed registration removal; no deleting shared original bytes | E |
| `skills.restore_revision` | Exact skill/base revision and digest | Confirmed compatible revision restoration; never restores content-revoked execution grants | E |
| `execution.prepare`, `execution.invoke` | Registered implementation, complete digests, typed invocation/resources | Deferred for general scripts/apps: review proposal/admitted receipt; fixed computer actions use dedicated tools | E |
| `execution.status`, `execution.cancel` | Exact Kora-owned invocation ID | Available only with admitted execution: observed receipt/cancellation certainty; no arbitrary PID killing | E |
| `maintenance.get_status`, `maintenance.get_release` | Host-published check/selected release ID | Read-only trusted maintenance snapshot and untrusted release notes; no check, navigation or updater authority | M/E |
| `diagnostics.list`, `diagnostics.export` | Host-enumerated daily-log IDs; explicit destination for export | File metadata or separately approved export; structured retained evidence uses `evidence.search`/`evidence.export`; no silent upload | E |
| `connectors.list`, `connectors.propose_configuration` | Registered connector/config schema and selected identity reference | Proposed B: admitted metadata or trusted setup proposal; secure sign-in stays host-only | E; M minimal status for list |

Frequent-speaker learning is separately consented local personalization, not
authentication, ambient/history training or authority. Protected verification
enrollment and its deletion/replacement remain separate secure host flows.
Maintenance tools inspect published snapshots only; deterministic update
checks and canonical release-page navigation remain outside model tools.
External connector tools retain their own reviewed IDs and schema contracts;
none of these entries authorizes arbitrary servers, commands or endpoints.

## Deferred and Unspecified Capabilities

| Area | Boundary |
|---|---|
| Standalone application/user-script execution | Future explicit content-bound resource/invocation review; no generic launch/shell tool is defined or available |
| File/workspace/Git operations | Later separately admitted resource-scoped adapters; no Kora source mutation |
| Screen capture/OCR/knowledge retrieval | Later explicit capture/source/retrieval contracts; no ambient collection |
| Other providers/enterprise MCP tools | Adapter/server-specific catalogues after proof; candidate names are not available tools |
| MCP server export | Later, disabled by default; no external API catalogue is promised here |
| Browser/desktop automation | Not provided by content-viewer navigation; separate future decision |
| Renderer extensions | Admitted presentation profiles, not model-installed executable packages |
| Intra-session parallel tasks | Deferred; independent sessions already have proposed bounded concurrent execution |
| Application installation/updater/elevation | No agent capability; out-of-band maintenance only |

Before exposing any entry, finalise its versioned schema, exact registration,
policy mapping, user help, dependency evidence and acceptance tests.
Help/discovery must report actual runtime availability, not this proposed
catalogue as if every entry were enabled.
