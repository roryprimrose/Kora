# Security and Data Flows

Status: proposed full security contract. The current bootstrap implements host-validated
model proposals and once/session/always model-action approvals, not the
complete grant taxonomy or script/executable execution gate below. Controls
must be demonstrated before the associated capability is enabled.

Related: [Architecture](Architecture.md), [Extensibility](Extensibility.md), [Acceptance Criteria](Acceptance_Criteria.md).

## Trust Boundaries

Cross-build startup/handoff uses authenticated user-scoped local coordination under [Instance Coordination](Instance_Coordination.md).
No second assistant may capture/dispatch during transfer; takeover approval does not approve code installation, production migrations, or transfer of grants/credentials.

1. User input and captured content are data, not trusted system instructions.
2. Kora's host services enforce policy independently of model decisions.
3. Runtime adapters and bundled code have an explicit installation trust boundary.
4. External MCP servers, connectors, and models are separate processing destinations.
5. Arbitrary executable extensions can have ambient OS access unless actually contained.

The policy protects against accidental overreach and untrusted content influencing tool use.
It does not claim to protect against a compromised Windows account, malicious trusted host code, or administrator-level malware.
The active unlocked Windows session is the baseline trust boundary for deliberate voice and UI input; a click is not stronger authentication.
Optional owner-voice verification adds confidence, not a guarantee. Kora addresses acoustic playback, stale/misdirected approvals, prompt injection, and scope expansion without claiming to secure an unattended unlocked account.
Channel parity, execution-risk classification, and persistent session semantics are canonical in [Human Interaction and Persistent Sessions](Interaction_And_Sessions.md).

## Data Modes

### Local Only

- Audio, transcription, context processing, and model execution remain local.
- Remote models, remote MCP calls, cloud speech, and automatic remote fallback are blocked.
- Remote destinations advertised by a selected runtime make that runtime unavailable in this mode.
- Installer/model downloads are separate explicit setup operations, not hidden task-time network activity.
- Offline acceptance tests run with network access blocked.

### Remote Enabled

- Remote processing is permitted only for configured destinations and approved content/actions.
- "Remote enabled" is not blanket consent to transmit every available source.
- Model requests, remote tool calls, and their respective outgoing payloads are reviewed separately.
- Audio remains local in the MVP.
- Adding a destination or sensitive context requires a new decision.

The UI always shows the effective processing mode and runtime.

## Wake-Listening Privacy

"Kora" activation is included from Slice A and runs locally in both data modes.
It is the default name, not a permanently enabled alias: users explicitly choose custom-only or default-plus-custom under [Custom Activation Names](Activation_Name.md).
Name changes never authenticate a speaker, modify embedded skills/code, expand ambient capture, or bypass output/action policy.
Setup requires explicit microphone/wake-listening consent; once enabled, verbal activation requires no click or keypress.
Distinguish Wake Listening, Capturing Command, Muted, and Unavailable visibly, including when the app is in the background.

Ambient audio goes only to the local wake detector and a rolling in-memory buffer of at most 2 seconds.
It is not continuously transcribed, logged, persisted, transmitted, or exposed to skills, MCP servers, or model adapters.
Only activated command audio reaches local transcription; exclude the wake word and unrelated pre-activation audio.
Empty activations expire locally without acquiring clipboard or other context.

Mute, lock, sign-out, suspend, and app exit close microphone capture and clear buffered audio.
An ordinary unlocked restart applies the automatic listening policy after fresh
readiness checks. Unlock/resume require explicit re-enabling for the current run.
Microphone enumeration is not recording consent; first-run and failure recovery use native mouse-selectable questions under [Interaction Fallback](Interaction_Fallback.md).
Device reconnection/default changes cannot reopen or switch capture silently, and stale pre-failure input cannot approve actions.
Detection of "Kora" is neither authentication nor approval; all commands still pass existing policy and approval checks.
Playback echo rejection must prevent spoken answers and activation cues from becoming user input.

## Shared-Space Privacy and Optional Speaker Verification

Trust an unlocked Windows profile as the baseline session condition, not proof of the physical identity of a speaker or person clicking.
Nearby people may activate Kora, overhear output, replay recorded speech, or use synthetic speech.
Wake detection, transcription confidence, a named phrase, and optional speaker matching are not independent authorisation factors.

Kora may offer local speaker verification as an optional privacy signal after a supported verifier passes its capability and anti-spoofing gates.
It answers only whether the current utterance resembles the voice voluntarily enrolled for the current Windows user SID; it is not general speaker identification.
The host exposes only `LikelyOwner`, `Uncertain`, `NotOwner`, or `Unavailable` to policy.
Models, skills, tools, and remote providers never receive embeddings, match scores, enrollment phrases, or raw enrollment audio.
No match by itself grants permission, satisfies an approval, reveals a credential, weakens an egress decision, or bypasses mandatory Windows/UAC/provider authentication.
An explicit owner-voice preference may require a current match for designated spoken approvals; default channel parity does not require enrollment.

Enrollment and replacement require an unlocked interactive session plus Windows Hello or an equivalent native OS reauthentication.
Use multiple randomized prompted phrases; never enroll automatically from ordinary conversations.
Derive and store only the minimum local template under per-user protected storage, bound to the Windows SID and device by default.
Do not place templates or enrollment audio in roaming storage, logs, telemetry, crash reports, model context, backups by default, or skill-accessible data.
Discard raw enrollment audio after successful derivation unless the user explicitly opts into a separately disclosed diagnostic capture.
Provide native view-status, test, delete, and re-enroll controls that work without voice.

Speaker verification must fail closed to `Unavailable` on missing assets, stale enrollment, changed user/device binding, microphone changes requiring revalidation, verifier failure, uncertain audio, suspected replay/synthesis, or policy denial.
Use playback echo rejection, local replay/synthetic-speech detection where supported, virtual/loopback-device awareness, bounded attempts, and rate limiting.
A randomized spoken challenge may raise confidence but is not strong authentication. Explicit action-specific spoken approval is a supported intent channel under the Windows-session trust baseline.

Apply speaker confidence primarily to output privacy:

| Situation | Required behavior |
|---|---|
| Safety-preserving stop speech, mute, or pause new dispatch | Accept from any speaker under normal intent checks |
| Current-session lock | Apply the future exact version-bound action grant gate through either channel; optional owner matching does not replace it |
| Cancel task, clear queue/session, or stop all work | Pause affected dispatch immediately, then require exact affected-work voice or UI confirmation before destructive cancellation/deletion |
| General non-sensitive request | Process under normal context and egress policy; do not infer owner identity |
| Private clipboard, task, account, message, or queue content with `LikelyOwner` | Speech is permitted only when the content's normal output policy also permits it |
| Private content with `Uncertain`, `NotOwner`, or `Unavailable` when owner-aware privacy is enabled | Use a neutral visual notice; do not speak sensitive resource details or silently disable the selected protection |
| Remote transmission, security/privacy changes, permitted setup activation, or power action | Exact scoped voice or UI approval under host risk policy; preserve mandatory OS/provider checks and capability limits |
| Credential/account workflow | Initiate/navigate through either channel; secrets and required sign-in remain in the supported secure flow |
| Credential, token, secret, or biometric material | Never reveal through speech or model context regardless of speaker result |

Headset/private-output mode and a short recent Windows Hello/unlock presence window may reduce unnecessary visual fallback, but neither creates a reusable grant.
Changing output privacy policy requires explicit scoped confirmation through either channel; biometric enrollment/replacement/deletion retains protected native OS verification.
For access decisions, `owner presence` is baseline deliberate activated voice or UI input in the active unlocked session, strengthened by any explicitly enabled speaker policy.
It is an intent/presence condition, not an identity credential. Passive audio, wake detection alone, PTT activation alone, and historical voice matches do not approve an action.
Absent/uncertain intent or an unsatisfied optional speaker requirement pauses the proposal and explains the explicit confirmation/alternate-channel route.

## Locked-Session Microphone Policy

`microphone.disabled-while-session-locked` is mandatory host policy, independent of any skill.
While Locked, Disconnected, or Unknown, Kora must not open or retain microphone capture or run wake/command recognition on new audio.
Use authoritative Windows session state at startup and session-change notifications at runtime; a model/UI assertion is not sufficient.
On lock, immediately block new capture, invalidate in-flight audio callbacks/transcripts, clear buffers, and release microphone devices/workers.
No PTT key, user skill, bundled script, runtime, or approval can override the denial.
Unlock requires explicit re-enabling; late pre-lock results cannot reopen listening or initiate actions.
See [Out-of-the-Box Skills and Session Policy](Built_In_Skills.md#mandatory-locked-session-microphone-policy).

## Call-Aware Output Privacy

Call state and detector preferences are device-local; collect only metadata needed for speech gating.
Do not record communications audio, meeting contents, participants, or transcripts for detection.
An optional Graph presence source uses explicitly approved account/network access with least privilege; it cannot operate remotely under local-only mode.
Current state and speech suppression reasons are not silently sent to models.
Call settings can be changed by an explicit user voice request through typed host preference operations; skill instructions cannot manufacture that request.
Configurable call overrides do not weaken locked-session microphone policy, user mute, egress, or action approval rules.
See [Call-Aware Speech](Call_Aware_Speech.md).

## Context Flow

Rich content is untrusted presentation data under [Information Display](Information_Display.md).
Optional speech text inherits spoken-content classification and lock/retention rules.
Markdown/HTML/SVG cannot replace native approval controls or access tools, credentials, microphone, or local files.
Local-only/offline mode blocks Kora-initiated internet navigation and remote render assets; generated previews and bundled Markdown/Mermaid renderers require no network.
Approved remote viewing is a distinct browser operation, never automatic model-context capture.
Embedded network/file enforcement must be proven; external browsers are explicitly user-managed, not claimed to be sandboxed by Kora.

```text
Explicit user selection
  -> Capture snapshot
  -> Assign provenance, identity, classification, and expiry
  -> Preview and secret-risk checks
  -> Select task context
  -> Evaluate destination/payload
  -> Obtain any required approval
  -> Runtime processing
  -> Treat outputs as untrusted derived content
  -> Display and optionally speak
```

Every outgoing envelope includes the user request, selected history, context, tool results, and any derived summaries the runtime would send.
Selected skill instructions and referenced package content are also outgoing context when sent to a remote model, including read-only profile skills.
Consent covers the actual payload and destination, not just its most visible source.
Derived content inherits source restrictions unless an explicit approved transformation justifies a change.

Secret detection is best-effort assistance, not proof that content is safe.
Likely secrets block transmission pending removal/redaction and review of the new payload.
Credential-store values are never available as model context, even through an override.
Re-detect and re-approve after a transformation changes the outgoing content.

## Grants and Approvals

Permissions combine capability, canonical resource scope, identity, destination where relevant, and expiry.
Examples:

- `clipboard.read`: a requested snapshot, not continuous monitoring.
- `filesystem.read`: files under an explicitly selected canonical workspace.
- `filesystem.write`: the reviewed patch against a known base.
- `process.execute`: a registered executable, code digest, arguments, working directory, and permitted resources.
- `git.write`: a specific repository and operation; commit does not imply push.
- `workiq.read`: the configured account and admitted remote tools.
- `session.lock`: one explicit lock of the current interactive user's Windows session via the registered bundled action.
- `computer.shutdown` / `computer.restart`: the named, confirmed graceful local-machine action; no remote target or forced-close authority.

Path checks account for traversal, links/reparse points, and actual resolved targets.
Executable extensions need real containment or explicit trust; a path-scoped gateway alone cannot constrain their direct filesystem access.

| Action | Default treatment |
|---|---|
| Requested local clipboard capture | Deliberate request in the unlocked session, subject to selected speaker/privacy policy; authorises one snapshot, not monitoring |
| Explicit current-session lock request | Under the future common gate, approve the exact version-bound lock action (once/session/always) through voice or UI; the phrase identifies intent but is not a grant. Today exact direct lock is ungated, while model-suggested lock asks for approval |
| Local shutdown/restart | Named second confirmation of exact graceful action, warning, and bounded host countdown |
| Local read within a user-selected scope | Default single-use; a bounded process-session grant requires native review and visible source selection |
| New remote context transmission | Review destination and payload; explicitly confirm immutable context and transformation class for this task through voice or UI |
| Remote read tool | Single-use exact parameters/destination, or a separately explicitly confirmed preconfigured read grant |
| Local write | Approve exact target and change |
| External write | Approve exact account, destination, parameters, and effect |
| Executable code or destructive operation | Separate high-risk review; no generic "allow everything" |

### Grant Types and Inheritance

`Single-use` is the default action approval and is consumed by one exact invocation.
A `task` grant may cover repeated use of named immutable source items, a declared transformation class, and one destination for one task.
A `conversation` grant is non-persistent execution eligibility scoped to one Kora session; retained history survives its expiry but does not approve new tools, egress sources, writes, or queued tasks.
A `process-session` grant expires on process exit/restart and, unless explicitly documented otherwise, on lock, sign-out, or account/provider change.
A `time-bound` grant has an explicit expiry no longer than one hour for interactive read access.
A `persistent-device` or `preconfigured` grant requires an explicit exact
voice or UI approval with mandatory OS/provider checks; no category of approvable actions is categorically denied a session
or always duration. A broad, unspecified effect cannot become a reusable
grant: identify its task, resources, invocation, and relevant dependencies
first. A future `always` execution grant may persist only for an exact
task/implementation identity, invocation and complete digest-bound
executable/script resource set; any changed or unverifiable bytes,
dependencies, or invocation invalidate it. This is a future capability, not
the current model-action `Always` preference, which authorises named
registered actions rather than executable content.
See [skill and task execution design](../docs/skill-and-task-execution-design.md).

No grant implicitly inherits across a new task, queued task, follow-up with new sources, conversation, provider, account, destination, skill/revision, Windows user SID, or device.
A process restart preserves only explicitly persistent exact grants whose declared identity/invocation/resources remain valid under fresh policy checks; single-use and process-session tokens are never replayed.
Child/follow-up operations must cite the exact parent user request and remain within its sources, transformation class, effects, and destination.
Changing any of those dimensions requires reassessment and, where applicable, a new approval.

Every reusable grant records a stable grant ID, capability/action, canonical resources, authenticated resource/account identity, destination, source/content hashes, allowed transformation class, creator channel, deliberate-input/optional-speaker/required-OS evidence class, creation/expiry, last use, use count, policy/schema revision, parent session/task/request, and non-inheritance flags.
Store hashes and canonical identifiers rather than raw sensitive content.

Provide a native Permissions & Approvals surface and deterministic host commands to list active/recent/expired grants, explain why an action is allowed, inspect exact scope and use, revoke one grant, revoke all grants for a provider/resource/skill/account, and export content-minimising audit evidence.
Narrowing scope or shortening expiry can edit a grant in place.
Broadening scope, changing identity/destination, converting to persistence, or re-enabling a revoked grant always creates a newly reviewed exact approval through the host-owned prompt, confirmed through either channel.
Revocation blocks new dispatch immediately; in-flight remote work is reported as cancellable, cancelled, or uncertain rather than silently claimed revoked.

An immutable-context task grant may cover repeated use of the same approved items, declared transformation class, and destination.
New user utterances are visible task input; follow-ups are previewed before remote submission.
New sources, changed snapshots, derived payloads, tool results, destinations, or increased effects require reassessment.

Approval tokens bind task/invocation IDs, identity, resource and parameter/content hashes, destination, expiry, and use count.
They are invalid after cancellation, expiry, relevant policy change, or changed action.
Do not approve an action based only on a model-written description.

Approval risk is host-classified:

| Risk | Examples | Required channel |
|---|---|---|
| Informational | Show help, inspect non-sensitive status | No action approval |
| Safety interruption | Stop speech, mute, pause new dispatch | Immediate deterministic control; no reusable grant |
| Low | Registered bounded non-sensitive local read with no egress | Explicit scoped request/confirmation through voice or UI |
| Medium | Private-context capture, egress/remote read, exact reversible local write, privacy preference, skill save/enable | Exact proposal/source/destination/change review and deliberate voice or UI confirmation |
| High | Broad/irreversible deletion, production/shared-system effects, unverified arbitrary code, credentials/security, grant persistence/broadening, software installation | Consequence/source/target/recovery review and action-specific voice or UI confirmation; mandatory OS/UAC/provider checks where applicable |

The host taxonomy, not a model/skill/tool declaration, assigns risk.
Unknown or mixed effects use the highest applicable class.
Assess execution effects, blast radius, reversibility, environment, exposure, privileges, and enforced constraint confidence; model/static script review cannot prove arbitrary code safe.
Registered constrained execution can be classified by its enforced effects; unknown code is high risk or rejected when containment is unsupported.
High-risk action confirmation names the action/target verbally or identifies that exact proposal in a deliberate UI gesture; a click is not reauthentication and speech need not be followed by a click.
Fixed local-machine shutdown/restart uses distinct action-specific voice or UI confirmation as specified, safe-work handling, and a cancellable countdown.
An assistant-name prefix is not speaker authentication. Make the action and chosen duration visible/readable; neither a match nor a click establishes physical identity.
Lock is side-effecting and retains the future exact version-bound grant gate for both direct and model-suggested requests; priority routing does not waive it. Today's direct Windows-API lock path does not yet enforce that gate, unlike model-suggested lock.
During the initial unsigned phase, application update checks are notify-only and cannot create an installation approval.
These rules do not permit arbitrary script/code installation or generic destructive commands; power actions warn about unsaved work and never force application termination.
See [OOTB Phrases](OOTB_Phrases.md#shutdown-and-restart-safety).
No speaker recognition claim substitutes for user identity.

### Approval Fatigue and Coercion

A denial or expiry suppresses an identical proposal for the current task/session unless the user explicitly reopens it.
Models, tools, providers, skills, and maintenance events cannot loop, rephrase, escalate, or split an unchanged denied request to solicit another approval.
Prompt attempts are rate-limited and deduplicated by canonical proposal hash.
Do not combine distinct resources, destinations, identities, or effects into a broad approval merely to reduce prompt count.
Scope escalation or changed content produces a visibly new proposal and never preserves the prior approval.
Newly shown native approval controls are unarmed for at least 500 ms and ignore key/mouse events that began before presentation.

### Intent Lineage and Prompt-Injection Controls

Untrusted skill instructions, MCP/tool results, retrieved session history, rendered content, and derived summaries occupy structurally separated data fields with provenance and non-instruction semantics; they never occupy host policy, approval, system/developer, or workflow-stage control fields.
Every model-proposed tool, context, queue, or egress operation cites deliberate user input or a previously explicitly approved host plan step that requires it.
An existing grant cannot authorise a new resource, destination, tool, workflow stage, or effect introduced only by untrusted content.
Derived content preserves the restrictions and taint of every source unless a separately approved transformation explicitly changes them.
The host compares each outbound envelope to approved source IDs and transformation classes; protocol framing need not be byte-identical, but every new content-bearing source or materially broader derivation requires delta review.

## Application Integrity and No Self-Modification

Kora may write validated declarative user skills, but cannot modify its own application implementation.
This prohibition is non-overridable through agent approvals, instructions, skills, or extensions.
Enforce it in host admission, tool execution, storage layout, and OS access controls, not only in model prompts.

Protected resources include:

- Installed Kora binaries, bundled skills/engines, and executable adapter locations.
- Embedded built-in skill resources and their host-owned catalogue, covered by application-binary integrity; no file overrides or writable script extraction.
- Kora source repositories, their Git metadata/remotes, build outputs, and deployment/update definitions.
- Security policy implementation, executable loading/trust configuration, updater configuration, and application launch hooks.

Maintain canonical protected-root identities outside agent-editable configuration.
Installed paths are resolved by the host; development deployments must register source/worktree roots before enabling writes.
Repository protection also uses configured repository identity, including remote/API identifiers, so an alternate clone or remote Git tool is not a bypass.
If protected identities or effective targets cannot be established, fail closed for the affected write/execution capability.

The skill writer accepts a validated data package and host-derived destination under the dedicated skill store, not arbitrary filesystem arguments.
Resolve target identity and reject traversal, link/reparse-point/hard-link aliases, renamed/replaced targets, and writes through protected ancestors.
Revalidate at application time using race-resistant filesystem operations; string-prefix path checks are insufficient.
Do not permit overwrite/delete/rename of protected resources or staging/backups within protected locations.
Registered shared profile skill sources are also read-only across Kora's write-capable pathways.
Only the dedicated Roaming AppData skill store admits authoring writes; it cannot be an alias into shared sources or protected code roots.
Skill-source enablement is device-local and digest-bound, never granted by roaming package metadata.
See [Skill Storage](Skill_Storage.md) for source scope and redirected/network-backed storage policy.

Install executable components where the normal application/worker identity has no modify rights.
Separate user data from executable search/load locations; never load code from the writable skill store.
In development, the host blocks registered source writes and agent-controlled code must have OS-enforced denial of source access, or remain disabled.
A per-user writable installation alone is not an integrity boundary; either establish protected deployment rights or disable paths that could bypass host mediation.

Unrestricted local scripts or MCP servers running with the user's ambient write rights could bypass the gateway.
They cannot be enabled as agent-executable components if they can mutate protected Kora resources: use verified containment, a constrained service identity/API, or refuse the integration.
Code trust, approval, signatures, or process separation alone do not satisfy this requirement.
Remote connectors must similarly lack agent-usable mutation/deployment rights over protected Kora repositories and artifacts.
If arbitrary remote execution could reach them and isolation cannot be verified, disable that capability.

The agent has no elevation, installer, updater, or executable-extension activation capability.
The built-in environment controller is a separate trusted host path for exact catalogue-defined dependency setup, not a model/skill installer capability.
It may initialise Kora data and offer approved external prerequisites, but cannot alter Kora code or use arbitrary supplied installer paths.
See [Environment Setup](Environment_Setup.md).
During the unsigned phase, application maintenance is notify-only with no model-callable or host-install-capable entry point.
Any future updater requires an independently authenticated metadata trust root, native secure per-release approval, and separate acceptance evidence.
Source-bootstrap installation and precompiled deployment follow the same protected-code boundary; discovering `.git` does not grant update authority.
Automatic checks may produce proactive verbal suggestions, but cannot install without that scoped approval.
See [Distribution and Updates](Distribution_And_Updates.md) and [Proactive Interaction](Proactive_Interaction.md) for protected feed, dialogue, and event boundaries.
Skill revisions affect declarative behaviour only and cannot change policy or enable executable loading.

The guarantee covers supported agent operations and contained extensions, not a malicious administrator, compromised host application, or manually launched external program.
Embedding removes editable built-in skill files; it does not prevent an owner patching the whole application or deliberately rebuilding changed source.
Application provenance must be enforced by the strongest available deployment/launch boundary outside any replaced binary; a resource digest stored in that same binary is not sufficient authentication.
Initial unsigned releases cannot provide Authenticode publisher authentication.
Their canonical release origin, exact revision, external final-byte hashes/build provenance, and protected installation permissions provide narrower traceability and tamper-detection guarantees that must not be described as equivalent to code signing.
Do not claim immunity against those excluded threats.

## Work-Management Boundary

Management inference uses a separate session and only the minimum approved request/ledger context.
Queue labels, planned steps, and status summaries can disclose private content; remote management follows the same preview and egress policy as task inference.
Do not copy the executor's full context or tool results into the manager automatically.
Local cancellation and basic factual ledger status need no remote request.

The manager proposes scheduling operations, not tools or action approvals.
Kora validates task targets and ledger revisions, and revalidates policy/context when dispatching.
Queued tasks do not inherit another task's approval token.
Queue confirmations and clarification prompts have distinct IDs from action approvals.

## Tool Flow

The gateway resolves and validates an action, checks policy, obtains approval, revalidates, and only then invokes it.
Tool outputs are bounded, labelled with provenance, and treated as untrusted content.
Reading content does not authorise executing embedded instructions or accessing related resources.

Changed MCP tool schemas or discovered destinations suspend affected tools until policy mappings are reviewed.
Remote reads can disclose queries and identifiers; "read-only" does not mean "no data leaves the machine."

## Executable Extensions and Containment

Two explicit execution profiles are allowed:

- Trusted user code: approved executable code runs under the user's account with its real ambient rights clearly disclosed. Gateway restrictions do not claim to sandbox it.
- Restricted worker: available only after OS-level filesystem/network/process containment has been implemented and tested.

The MVP skill workflow launches no arbitrary external commands; its validation is built in.
OOTB lock/shutdown/restart skills use fixed protected scripts admitted only after their containment and exact OS-control tests pass.
Future trusted commands must also satisfy protected-resource isolation. Ambient user rights that permit Kora mutation are not an acceptable agent execution profile.
Untrusted downloaded scripts and plugins remain disabled.
The built-in skill validator is not an arbitrary script runner.

Workers use a controlled working directory, allowlisted environment, minimal credential exposure, output limits, deadlines, and tracked process trees.
Windows Job Objects can support process-tree lifetime management; they are not a filesystem or network sandbox.
Restricted tokens/AppContainer or equivalent containment are candidates requiring an implementation proof.
If containment cannot enforce a requested restriction, reject that profile rather than silently downgrade it.

## Identity and Credentials

- Account identity is bound to each connector and context item.
- Use supported OAuth/delegated authentication with least privilege.
- Never ask the model to handle raw sign-in secrets.
- Store credentials in an OS-protected facility, not SQLite or extension manifests.
- Pass a credential only to the approved destination-specific component.
- Sign-out and account switching invalidate relevant sessions, grants, and cached content.
- A server using user identity is still an external data processor.

## Retention Defaults

| Data | Default |
|---|---|
| Ambient wake audio | Memory-only rolling buffer of at most 2 seconds; overwritten continuously and cleared when listening stops |
| Activated command audio | Memory only; released after transcription/cancellation |
| Selected context snapshots and command transcripts | Encrypted session history when source/security policy permits; no background clipboard history; raw command audio remains ephemeral |
| Session messages, answers, questions, decisions, scripts/artifacts, and permitted tool content | Durable encrypted session-linked history; restored for reading, never automatic execution or unreviewed egress |
| Queue requests, labels, context references, and content-bearing work ledger | Persist as session evidence; pending execution eligibility expires after 30 minutes by default; readable evidence is not fresh context/approval |
| Session inactivity lifecycle | Archive after 24 hours and delete after 30 days from last meaningful activity; both configurable; browsing/search do not reset the clock; live/uncertain work is protected |
| Task/action/approval audit metadata | Local SQLite, at most 30-day expiry; stable IDs, canonical action/resource/destination identifiers, parameter/content hashes, scope, creator channel, presence/confirmation class, policy/schema revision, creation/expiry/use/revocation events, outcome, and error codes; no raw parameters/content |
| Application diagnostic logs | Daily structured JSON under `%LOCALAPPDATA%\Kora\Logs`, at most 30 files and 30 days; lifecycle, readiness, state, counts, and failures only; no transcripts, response bodies, synthesized speech text, raw audio, credentials, or secrets |
| Configuration and grants | Persist only for their declared grant type until revoked/expired; inventoried in Permissions & Approvals; no secrets in configuration |
| Kora-specific skill definitions/revisions | Persist in `%APPDATA%\Kora\Skills` until explicitly removed; not cleared with conversation history |
| Shared profile skill snapshots | Approved in-memory revision snapshots; re-read/revalidate on restart; source files remain untouched |
| Approved export | User-selected location with an explicit content preview |

Persistent permitted session history is required, with first-use storage/retention disclosure and explicit deletion controls; it is separate from content-free diagnostics.
Retention preferences are described in [User Configuration](User_Configuration.md); they cannot enable raw audio/secret storage, restore consumed grants, or silently delete affected sessions when changed.
Encrypt history, artifacts, and indexes with OS-protected keys; deletion must cover caches, indexes, blobs, journals/recoverable copies, and invalidate session-scoped authority.
Source revocation may remove restricted content before normal session expiry. Mark omissions/redactions explicitly.
Independent content-minimising security/diagnostic records retain their disclosed lifetimes and do not reconstruct deleted chats; local deletion cannot erase user exports or provider copies.
Approval/audit records are writable only through the host security service, denied to models/skills/tools/workers, and use append-only sequencing or equivalent tamper evidence so deletion/rewrite is detectable within the supported non-administrator threat model.
Crash reports and diagnostics omit raw context, speech, tool arguments, credentials, and answer content by default.
Future model-assisted diagnostics may access application logs only through a
host-owned reader that validates Kora daily-log names, rejects arbitrary paths,
and bounds each tail read to 1,000,000 characters.
Content-bearing diagnostic export requires explicit preview/consent; OS or third-party crash dumps remain a deployment concern.
In-memory disposal is best-effort, not a guarantee of forensic erasure from OS paging.

Provider/server-side retention is disclosed separately and cannot be erased by clearing Kora's local history.

## Security Audit Events

Every security-relevant write, application execution, script execution,
protected operation, and security-approval transition uses a host-owned typed
audit contract. The execution boundary emits a `Requested` event before the
effect and a terminal `Succeeded`, `Failed`, `Denied`, or `Cancelled` event
when the outcome is known. Both events use the same correlation ID. Approval
flows additionally carry an opaque approval ID across request, decision,
execution, cancellation, and expiry events.

Audit events contain only:

- Category and stable canonical action ID.
- Stable canonical target identity or content digest, never a raw path.
- Initiator class such as local UI, typed command, voice command, or system.
- Correlation ID, optional approval ID, outcome, and bounded reason code.

They exclude command/transcript text, raw parameter values, process arguments,
script source, setting values, file contents, exception text, credentials, and
secrets. A target requiring content identity uses a host-computed digest and
canonical resource identity; model- or skill-supplied labels are not audit
identity.

The initial implementation emits these structured events into the retained
daily JSON `ILogger` stream for configuration writes, Kora restart, Windows
session lock, and protected power proposals. This stream is useful operational
evidence but is user-modifiable and is neither authorization evidence nor the
tamper-evident approval/audit store required before general write,
application/script execution, or security approval is enabled. A log entry,
including a claimed success, never substitutes for policy enforcement,
immediate pre-execution revalidation, an action receipt, or authoritative
effect observation.

## Future Knowledge Indexing

Before indexing enterprise or local sources:

- Partition content by source/account and store source version, hash, freshness, and access-check information.
- Revalidate effective access before retrieval; fail closed when required checks are unavailable.
- Exclude revoked or deleted content immediately and queue local cache/index deletion.
- Treat embeddings as sensitive derived data, not anonymous metadata.
- Track embedding model/index versions and rebuild explicitly on incompatibility.
- Provide source citations and retrieval timestamps.

Content freshness and access freshness are separate checks.
Offline access is allowed only under an explicitly defined source policy; it must not bypass required permission revalidation.
