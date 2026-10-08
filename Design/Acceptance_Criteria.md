# Acceptance Criteria

Status: canonical three-tier qualification policy and capability acceptance
criteria. Targets are not claims of measured performance.

Related: [MVP Scope](MVP_Scope.md), [Architecture](Architecture.md), [Extensibility](Extensibility.md), [Security and Data Flows](Security_Data_Flows.md), [Task Lifecycle](Task_Lifecycle.md).

The [Implementation Status and Delivery Roadmap](Implementation_Roadmap.md)
maps these criteria to current source/test evidence and outstanding work packages.
Delivered bootstrap behavior does not mean capability qualification has passed;
actual provider, hardware, containment and installation evidence remains required
where applicable to the enabled capability/profile.

## Three-Tier Qualification Policy

This section is authoritative for the scope of merge, enablement and final
release-candidate (RC) qualification. It supersedes aggregate repository-wide
interpretations of R02, Gate 0, A0-A4 and experiment/deferred-validation
checklists; it does not lower their applicable criteria or relabel evidence.

| Tier | Required evidence | What an outstanding proof blocks |
|---|---|---|
| 1 - Normal repository feature development | Directly applicable unit, integration, security, compatibility and regression checks for the changed behavior and its consumers; directly related documentation and normal review/CI requirements. Documentation-only changes need documentation checks where present, not hardware/provider trials. | The affected change when its required checks fail. Unrelated experiments, unapproved lab environments and unimplemented capabilities are not repository-wide merge blockers. |
| 2 - Capability/profile qualification | Applicable critical fixtures and actual boundary evidence before enabling, advertising, packaging as available, or materially changing that capability/profile. Evidence binds to exact implementation/runtime/model/native bytes, permissions, destinations and supported environment. | Only the affected capability/profile and consumers that actually require it. Partial implementation and maintained tests may merge with an explicit unavailable boundary; a materially changed enabled path must be requalified or disabled before delivery. |
| 3 - Final RC qualification | Integrated regression and final-byte/environment evidence for an explicit, reviewed enabled-capability manifest, including shared controls and actual transitive dependencies. | Qualification of that RC's enabled scope, not unrelated feature development or every capability in the design vision. Excluded capabilities are recorded as exclusions, never passed, waived or silently inherited from a different profile. |

Source inclusion or an unsigned development/POC artifact is not a capability
claim or final RC qualification. A capability packaged as available, including
an offered opt-in capability, must meet tier 2; a disabled implementation must
remain unreachable and clearly unavailable through UI, commands, model tools,
setup and documentation. Existing CI, review, licence and coverage requirements
remain in force; this policy does not change workflow or branch protections.

### Non-Waivable Safety and Critical Fixtures

Authority, consent, privacy, instance ownership, egress, required durable intent/
audit, data integrity and resource-quiescence checks remain hard fail-closed
boundaries on every affected admitted path, at every tier. Unknown, missing,
corrupt, stale or unobservable state cannot authorize work. An exclusion cannot
remove a shared control needed by an enabled path; missing implementation or
evidence leaves that path unavailable, not optimistically admitted.

All mandatory critical fixtures for the affected capability must pass, including
denial/no-side-effect, hostile content and incoming context, cross-session
isolation, cancellation/late output, required audit and interruption/recovery
cases as applicable. Aggregate scores or passing averages cannot hide a critical
failure. Fakes provide deterministic coverage, not substitutes for required
native/provider, acoustic, containment, offline or installed observations.
Consent/privilege and test-machine ownership requirements are unchanged.

### Enabled-Capability Manifest and Exclusions

For each RC, retain a versioned qualification artifact alongside its exact
source revision and final payload/setup digests. Release/test owners review it;
it records qualification scope, not runtime permission or model-supplied
authority. No new manifest parser or enablement mechanism is implied here.

- List each available default and offered opt-in capability, its exact profile/
  implementation and dependency identities, supported OS/architecture/hardware,
  caller routes, permissions/destinations and configuration boundaries.
- Map each entry and its shared/transitive controls to applicable criteria,
  mandatory critical fixtures, maintained tests and actual environment receipts,
  with result, limitations and accountable owner. Reuse valid exact-profile
  receipts; repeat affected evidence when mechanisms, bytes, environments or
  compatibility/availability claims materially change.
- List excluded capabilities/profiles separately, with reason, dependent
  exclusions, unavailable routing/packaging/advertising boundary and the evidence
  needed for future inclusion. Missing consent or an unavailable lab is an
  exclusion/blocker for that scope, not a successful result.
- Evaluate final installed bytes and integrated behavior only for included
  scope, plus shared platform/security/storage controls it depends on. No model
  runtime means no unrelated hosted-account gate; no script executor means no
  unrelated worker-profile qualification. Their denied/unavailable boundaries
  still require regression coverage.
- Reject an RC with a failed/missing required fixture or boundary receipt for an
  included capability. Narrowing release scope requires an explicit reviewed
  manifest and truthful product/user documentation, not an implicit waiver.

A0-A4/B/C remain milestones for their stated full outcomes. A manifest may
explicitly exclude an unfinished outcome; that RC must not claim the complete
milestone or original full initial-release scope. Future capability gates apply
only when that capability is in scope.

## Test Environment and Evidence

For applicable environment-qualified capability and RC trials, record an exact
reference machine; this is not a prerequisite for unrelated repository merges:

- Windows 11 x64 on a currently supported release.
- At least 8 logical CPU cores, 16 GiB RAM, and SSD storage.
- No GPU required for the initial speech workflow.
- Wired headset microphone for repeatable speech tests.
- Exact CPU, OS build, memory, speech models, runtime versions, and power profile recorded.

Select speech/model versions explicitly and verify distribution licences.
Measure with the UI open and normal local services running; record resource contention.
Cloud tests record region, network round-trip latency, provider, and pinned runtime version separately.
Use synthetic data and test accounts, never real secrets or private enterprise documents.

Evidence includes test cases, actual results, timings, action receipts, and redacted diagnostic references.
Critical policy/cancellation tests require 100% pass; averages must not hide individual unauthorised actions.

### Local Inference Evidence

The [R02 local-inference outcomes](Local_Inference.md) are partial evidence,
not a waiver of these gates. Public candidate metadata, 31 deterministic proof
tests and a real missing-endpoint/unavailable response establish neither
successful answering nor CPU-floor/offline acceptance.

Before D-003/local-inference D-007 qualification, follow
[R02-L1-L5](Implementation_Roadmap.md#r02-local-inference-continuation):

- Name/approve the supported Windows/reference CPU and isolated environment;
  test the proposed 8-logical-core/16-GiB/SSD floor with verified CPU-only
  inference, recorded power profile and UI/local-service contention. A faster
  development host or a constrained VM alone does not qualify the floor.
- Agree numeric local-answer latency/resource/cancellation budgets before
  trials. Record exact installed runtime/model identities, compatibility,
  licence/notice review, transfer, expanded/peak storage and per-volume needs.
- Measure at least 30 unloaded-model cold and 30 paired warm trials; retain
  individual outputs/failures, first generated versus response token and
  completion timing, p50/p95/max, process memory/CPU and runtime counters.
  Buffered production and experimental streaming results stay separate.
- Use expected-answer fixtures plus the
  [automated/human rubric](../experiments/r02-local-inference-proof/README.md#answer-quality-rubric).
  Human review must verify meaning, grounding/uncertainty and safety, not just
  matching keywords. Every critical fixture passes; aggregate scores cannot
  hide quoted-content authority, invented execution or remote-fallback failures.
- Verify the effective full-envelope context limit and explicit oversize
  behavior; advertised model tokens and the 4,096-character bootstrap cap are
  different bounds. Observe repeated cancellation/completion races, actual
  timeout, late-output rejection, server cessation/residual work and recovery.
- Under approved whole-environment remote-network denial with loopback
  retained, prove successful answering, cancellation/recovery and
  missing/unhealthy unavailable behavior. Retain independent effective-control
  and process-correlated egress evidence covering Kora and Ollama/runner;
  loopback code, connection refusal and client mocks alone are insufficient.

R02 candidate qualification still does not accept A2. R06/R07/R08/R10 apply
the tested envelope and R19 compiles integrated host, request-triggered
clipboard, voice/UI and streaming evidence under R02-L6. Changed runtime/model,
options, context or compatibility claims repeat affected proofs.

Use the [shared deferred-validation register](Deferred_Validation.md) and
[LI01-LI07](../experiments/r02-local-inference-proof/README.md#deferred-inference-trials)
when preparing a later separately approved interactive inference session.
They distinguish runnable synthetic commands from missing server/egress/host
instrumentation. Unperformed trials block qualification and A2 acceptance,
not merging the partial research and testing handoff under normal checks/reviews.

## Platform Boundary Gate

- Shared domain/policy/task/skill/configuration code builds and runs portable tests on Linux without Windows-only API references.
- Real native dependencies/handles/path conventions are confined to Windows integrations and platform composition, not scattered through shared logic.
- Contract tests cover device identity/cancellation, locked/disconnected/unknown denial, credential opacity, protected execution, unsigned-phase notify-only maintenance, and the absence of any install-capable path.
- A fake or future adapter cannot weaken shared policy through capability claims, unknown state, or failed probes; affected capabilities fail closed.
- Windows end-to-end tests verify the real adapters independently of portable fakes.
- Unsupported-OS startup reports the unsupported platform without acquiring capture, executing controls, or implying partial support.
- No Linux/macOS application artifacts/backend projects/feature-parity promises are required by this gate; Linux CI is infrastructure only.
- A .NET 11 migration reruns relevant runtime/native/packaging/security compatibility gates rather than being assumed compatible.

## Gate 0: Runtime Integration

The Copilot proof in [Architecture](Architecture.md#copilot-integration-proof) must pass before it is enabled:

- Denied tools produce zero side effects.
- Denied synthetic context markers appear in zero outbound model payloads.
- Initial context and subsequent tool results both obey transmission decisions.
- Unmediated built-in tools, collection, and persistence are disabled or proven compliant.
- Streaming, errors, authentication, and cancellation have explicit observed behaviour.
- Any limitation produces a disabled capability and an actionable UI explanation.

Test the actual pinned SDK, not only a fake adapter.
Mocks are supplementary for deterministic negative-path coverage.
An unobservable or uncontrollable outbound path is a failed gate, not an assumption of safety.

The [R02 evidence and continuation](Runtime_Provider_Feasibility.md) are a
partial candidate result, not Gate 0 acceptance. Candidate/integrated tests
must now explicitly cover:

- Final serialized-request mediation after success, failure, denied, timeout,
  cancelled and unknown tool results, including thrown exceptions and
  runtime-added history/context; do not rely on the success-only post-tool hook.
- Equivalent supported controls in the pinned production-language SDK;
  Node-only proof does not accept Kora's .NET adapter.
- Runtime startup/session/auth/error/shutdown destinations and
  storage/diagnostic paths under actual observation, not just model HTTP.
- Management input at 32768/32769 serialized UTF-8 bytes and typed output at
  4096/4097 bytes; actual 15-second deadline independent of stalled SDK
  acknowledgement; no forwarded automatic retry despite SDK retry attempts.
- Independent admitted execution/management identities, tools and context on
  the approved account/provider, with its verified concurrency/cost envelope.
- Cancellation that blocks new dispatch/egress and late presentation but
  reports admitted unconfirmed effects as unknown, never as rolled back.

Unperformed or blocked trials keep the relevant capability disabled and
D-001/D-004 open. Deterministic local management does not depend on hosted
model-assisted management acceptance.

The [actual .NET RT1 evidence](../experiments/r02-dotnet-control-proof/EVIDENCE.md)
passes only its explicitly approved exact-tag source-built minimal profile.
It does not accept released-NuGet bytes, RT2 full lifecycle observation, MG1's
complete envelope, PV1 account eligibility, host authority/audit integration
or installed/scheduler acceptance. The requirements above are unchanged.
The [independent MG1 evidence](../experiments/r02-dotnet-management-proof/EVIDENCE.md)
subsequently repeats RT1 on separately approved released 1.0.16 bytes and
passes its synthetic .NET byte/deadline/admission/Unknown-quarantine envelope.
This is not source-built byte equivalence, RT2 lifecycle observation, PV1
account/cost eligibility, production management protocol or scheduler/lease
acceptance. Gate 0 and D-001/D-004/D-010 remain open.

The [RT2 lifecycle evidence](../experiments/r02-runtime-lifecycle-proof/EVIDENCE.md)
passes 20 bounded tests plus two locale contracts but leaves RT2 **Blocked**.
Live positive controls, sampled native helpers/quiescence, model-request
denials and before-cleanup recoverable-file checks do not qualify full native
egress/persistence or diagnostic coverage. Require writer-attributable
all-file/all-destination observation, native diagnostic inventory, event-loss/
short-lived-path controls and actual prevention/mediation before claiming
zero unauthorized native markers. Dedicated-host privileged observation needs
separate approval; a successful trace alone is not prevention. W2 best-effort
script dependency tracking does not relax this runtime contract. Gate 0,
D-001/D-004, PV1 and integrated R04/R08 acceptance remain open.

## Command, Tool, and Skill Interaction Gate

Apply [Commands, Tools, Skills, and Model Interaction](Commands_Tools_And_Skills.md)
to each enabled capability in its delivery slice. Use deterministic host/runtime
fixtures plus actual pinned-provider evidence for advertised model-mediated
selection and iteration; a bootstrap action-selector test alone is not proof
of the proposed tool loop.

- Equivalent typed and activated/transcribed voice requests resolve the same
  capability and inputs while retaining their channel-specific privacy/approval
  checks. Users need not supply a tool ID, manifest, or script filename.
- The model receives the relevant admitted tool schemas and enabled,
  source-qualified skill summaries before interpretation. Unknown IDs,
  unavailable dependencies, disabled revisions, stale catalogue entries, and
  invalid parameters cannot dispatch an implementation.
- Skill selection loads only the resolved pinned instructions/workflow and
  admitted references. Remote summary/instruction transmission obeys context
  policy; local discovery does not imply egress permission or execute a script.
- A session-state query returns authoritative structured data, invocation ID,
  observation time, and provenance without opening a window, speaking, changing
  state, or approving a pending action. A deterministic status presenter uses
  the same query service without a model.
- Non-exact status questions such as "Am I waiting on anything?" use approved
  context or request fresh state through a tool. Answers reflect observed
  blockers/unknowns and cannot claim stale snapshots are current.
- The runtime receives an approved bounded tool result and can answer or
  propose another checked step. Test success, failure, denied, cancelled, and
  unknown results; display text and action names never become execution.
- Tool-result egress denial sends zero rejected markers to the model and leaves
  the observed action receipt truthful. Locked-session/output policy still
  prevents private spoken or visible completion.
- Exact lock phrases and natural-language lock requests resolve the original
  bundled skill/task, its fixed parameters, and the same verified embedded
  script/dependency snapshot. Skill selection followed by tool dispatch never
  executes twice; arbitrary script text/paths cannot replace registration.
- Quoted lock instructions, questions about locking, hostile tool/skill content,
  and ambiguous computer/application targets do not establish execution intent.
  Ambiguous intent or source-qualified skill selection asks for clarification.
- Selecting/enabling a skill, choosing a clarification answer, opening script
  review, or separately approved PowerShell setup does not execute the selected
  task or create an implicit execution grant. Setup retains its own consent
  and observed installation effects.
- Direct commands, UI task invocations, model proposals, and skill workflows
  reach the same applicable action gate. A denied or dismissed proposal runs
  nothing; once/session/always grants cover only the resolved task/implementation,
  permitted invocation, and exact resources.
- Changed script, invocation, manifest, or executable dependency invalidates
  affected grants; unaffected tasks keep theirs. Model-supplied hashes, risk
  classifications, or permission claims cannot authorise a run.
- A tool invocation cannot broaden an approval or attach a clarification reply
  to another proposal. Cancellation rejects late proposals/results, and
  uncertain or denied effects never trigger an automatic write retry.
- Model offline/busy/timeout fixtures preserve exact help, basic status,
  stop-speech, cancellation/pause, and essential lifecycle routes without
  weakening active-work or side-effect approval.
- Management inference retains its minimal ledger/proposal contract; it cannot
  inherit execution tools, skill instruction bodies, or script access.

## Gate 1: Voice and Clipboard Vertical Slice

Report Gate 1 by Slice A checkpoint:

- A0 proves the deterministic PTT/clipboard/remote path without claiming wake-word or local-first completion.
- A1 adds and proves wake activation, playback rejection, interruption, and session policy.
- A2 adds and proves the supported Ollama-backed local answer path with no remote network access.
- A3 adds persistent sessions/retention, isolated concurrent scheduling/resource coordination, structured questions/grants, bounded routing/history tools, configuration, and protected lifecycle/computer controls.
- A4 adds compact/manager/history interaction surfaces, proactive, call-aware, optional owner-aware privacy, maintenance dialogue, and bounded presentation.

Later-checkpoint testing repeats applicable earlier gates.
No A0/A1 demonstration or release note may claim the completed voice-first/local-first Slice A outcome.

### Functional

- A user completes clipboard explanation by activated voice without compulsory typing/clicking under the unlocked-session baseline, and repeats it using only mouse/keyboard and mixed-channel input.
- Ambiguous intent or an unsatisfied explicitly selected speaker preference captures nothing pending clarification/alternate-channel confirmation; denial leaves clipboard untouched.
- Both immediate commands ("Kora, explain...") and wake-then-command with a pause preserve the command's first words.
- Optional push-to-talk uses the same command pipeline without requiring "Kora".
- Empty activation returns to Wake Listening after 5 seconds without a model call, clipboard read, or other tool invocation.
- Trailing-silence endpointing and the 60-second limit produce explicit, bounded capture transitions.
- A snapshot is captured only after a deliberate explicit request in the unlocked session and any selected speaker/privacy protection; wake activation alone is not a capture request.
- Changes to the OS clipboard after capture do not change task input.
- Empty, locked, unsupported, and oversized clipboard cases are distinguished.
- Visual answer streaming and a spoken summary use the same task/result identity.
- Spoken output is at most 3 sentences and 80 words.
- Speech uses the selected active Windows render endpoint by stable endpoint ID; no endpoint, no selection, endpoint removal, open failure, and reported playback failure each force the response text/window visible without silently switching endpoints.
- Windows software mute or zero endpoint volume suppresses playback and forces text/window visibility; Kora does not change global endpoint mute or volume, and speech recovers only after the user unmutes and readiness is refreshed.
- Output preview identifies failures Windows can observe; physical audibility beyond the endpoint (powered-off speakers, disconnected analog paths, hardware mute/volume) is not claimed as detectable.
- Correction, follow-up, "stop", "stop speaking", and "cancel task" match lifecycle semantics.
- TTS audio is not ingested as a new user request.
- "Kora, stop" and "Kora, stop speaking" work during TTS; playback mentioning "Kora" and activation cues never self-trigger.
- A second independent-session task can execute within the admitted budget/resource policy; a second task in the same session remains ordered, and UI selection does not change execution ownership.
- Unsupported playback echo rejection disables TTS explicitly, not wake activation; the reference setup must support spoken responses and verbal interruption.
- A new snapshot/destination triggers a new policy assessment.
- The A2 local-only workflow produces the clipboard answer through the pinned Ollama adapter/model with remote network access blocked; missing/unhealthy local inference reports unavailable and never falls back to Copilot.
- The future "Kora, lock the machine" route invokes the original bundled skill/script without a model/network call, including while another task is busy, after the same action-specific gate as model-suggested lock. The current direct Windows-API route is not that skill.
- The script's application-assembly/resource identity, version, digest, and fixed parameters are verified; tampering or a same-name user package cannot substitute its implementation.
- Confirm lock by the actual Windows session event; API acceptance/script exit without that event is not reported as confirmed success.

### Privacy and Policy

- With verbal input enabled in the active unlocked Windows profile, otherwise valid requests/approvals from different speakers follow the same intent/grant policy when no optional speaker restriction is selected; no compulsory verifier, push-to-talk, or UI-only consequential authorization is introduced.
- Absent system-output correlation discloses the residual enabled-voice limitation without blanket voice blocking; self-TTS rejection and supported playback discrimination still meet their advertised quality gates.
- Lock/disconnect/suspend, prohibited effects, OS/provider checks, untrusted-content separation, and the explicit protected-call origin gate remain enforced regardless of speaker or learned confidence.
- Local-only mode performs no task-time network calls under network-blocked testing.
- With no local model, answering is unavailable with an explicit explanation.
- Remote-enabled mode shows the actual destination and context before transmission.
- Rejecting transmission sends none of the rejected context.
- Secret-risk fixtures block transmission pending reviewed redaction.
- Raw audio/secrets are absent from all history/log stores. Clipboard/tool/answer content is absent from diagnostics and content-minimising audit, while permitted session content is retained under verified private profile permissions.
- Outside an activated command or eligible host-opened conversational reply
  generation, synthetic ambient audio reaches neither transcription nor any
  model, tool, persisted store, or network destination.
- Wake pre-roll never exceeds 2 seconds and is overwritten; unrelated pre-activation audio is excluded from command transcription.
- After Kora speaks a unique ordinary yes/no question, `Yes` and `No` without
  the activation name are accepted only within the configured 15-second
  default conversational reply window and bind to that exact question/revision.
  The activation name remains optional within the turn.
- Exercise conversational reply speech-start waits at 5, 15, and 60 seconds
  and just beyond each bound. Timeout captures nothing further, returns to Wake
  Listening, leaves the question answerable, and requires a fresh activation
  or explicit Answer by voice to answer later.
- One accepted reply, cancel, mute, lock, call-policy loss, endpoint loss,
  target/session change, question expiry, or replacement invalidates the
  capture/transcript generation and clears buffered audio. Late speech or
  callbacks cannot answer the old, newly selected, or latest question.
- Prefix-free input is parsed only against the foreground question schema.
  Invalid/free speech cannot become a new command or tool request; multiple or
  background questions cannot receive generic replies. A fresh host
  clarification gets a new bounded turn rather than silently extending the
  previous one.
- Detail offers accept prefix-free `Yes`/`No`; high-risk confirmation fixtures
  still require their exact action/target wording and reject generic `Yes`.
  Conversational routing never weakens policy, grant, speaker, OS, provider,
  call, or privacy checks.
- During TTS, Kora playback and activation cues produce zero conversational
  answers. Prefix-free barge-in is enabled only with verified playback
  rejection; otherwise capture starts after TTS completes.
- Disabling conversational replies requires the activation name/PTT for every
  answer without disabling ordinary wake listening. First-run consent and live
  state explain and distinguish Wake Listening, Awaiting Reply, and Capturing
  Reply.
- Frequent-speaker learning fixtures exclude conversational reply audio even
  when learning is enabled; only separately consented newly wake-activated
  command samples remain eligible under the initial learning policy.
- Mute/lock/sign-out/suspend close capture and clear buffers. Normal authoritative
  unlock restores only the previously enabled mode after confirmed closure and
  fresh gates: PTT readiness or separately qualified fresh wake-only detection,
  never interrupted capture/reply/audio or sensitive presentation. Manual mute,
  withdrawal, resume, sign-out and other failures require explicit recovery.
  Ordinary restart may auto-enable
  only with saved ongoing consent and fresh gates under the microphone matrix.
- Wake Listening, Capturing Command, Muted, Session Locked, and Unavailable are distinguishable, including background-app status.
- Closing/dismissing UI preserves session history and work. Done archives; explicit confirmed deletion and configured inactivity purge remove retained session content under the dedicated lifecycle gate.
- Restart restores readable Active/Done history and interrupted/unknown evidence, never tools, queued dispatch, provider memory, or consumed approval tokens. Explicit always grants remain subject to exact identity/digest/invocation and fresh policy validation.
- Exercise all three grant scopes: single-use, the approved operation for an identified Kora work session, and perpetual. Test future executable grants separately against exact version/digest/invocation identity; current named model-action `Always` is not such a grant.
- Verify default single-use consumption, session isolation/end, and explicitly approved perpetual reuse without broadening operation/resource/provider/account/skill/device scope; lock/sign-out/policy changes enforce applicability without deleting perpetual records.
- The native Permissions & Approvals surface lists canonical scope/bound session, identity, destination, hashes, creator channel, creation/edit history, last use/use count, policy revision, and consumed/session-ended/inapplicable/removal reason without raw sensitive content or a perpetual expiry.
- Narrowing edits in place; permitted broadening, scope conversion, identity/destination change, and replacing consumed/ended/removed access require a newly reviewed exact voice/UI-confirmed grant.
- Revoke-versus-dispatch races block new calls immediately and report in-flight remote effects truthfully.
- Clipboard/private-context capture, egress, remote reads, privacy expansion, persistent preferences, and skill enablement use equivalent exact voice/UI approval rules; ambiguous intent/unsatisfied optional speaker policy pauses rather than inventing approval.
- Denied canonical proposals are deduplicated/rate-limited and cannot be rephrased, split, or escalated repeatedly by models/tools/providers/skills; distinct resources/effects are never hidden in a broad bundled approval.
- Newly displayed native approval controls ignore pre-existing key/mouse input and remain unarmed for at least 500 ms.
- Verify that model answers, clarifying questions, and action/grant proposals cannot execute an unregistered action, invent a grant scope, or bypass host validation; model-suggested lock must require approval. For the future shared execution gate, an exact direct lock request must require the same action-specific grant; the current direct Windows-API lock remains ungated and does not pass this future test.

### Mouse-Based Questions and Device Recovery

- With no saved Kora device override, first launch selects System for microphone
  and speaker without recording or persisting endpoint snapshots; ongoing
  capture requires explicit voice consent under the
  [microphone matrix](Security_Data_Flows.md#microphone-consent-and-enablement-matrix).
  A complete mouse-only consent/disable/change/recovery path remains available.
- Exercise every matrix row with actual Windows/device events and deterministic
  race fixtures: first-launch decline/consent, ordinary restart/logon,
  lock/disconnect/unknown/unlock, suspend/resume, manual disablement,
  permission loss/restoration, endpoint loss/reconnection and asset failure.
  Fresh-gated ordinary startup may use saved consent; consent withdrawal
  persists across restart, while run-scoped holds do not.
- Test normal unlock restoration and its negative controls: prior manual
  disablement, withdrawn consent, closure/capture failure, unavailable permission,
  endpoint/assets/call policy, ownership loss and intervening uncertain sessions
  cannot restore readiness. PTT needs a new press; qualified wake uses a new
  wake-only generation. Resume/restored permission/device or asset repair needs
  explicit Enable listening; tests, selection, PTT, reset/undo and stale callbacks
  cannot release those recovery holds.
- Zero/one/multiple devices, duplicate names, Windows privacy denial, disabled/missing endpoints, muted input, and missing recogniser each have distinct actionable states.
- Enumerating/selecting devices records no audio; automatic startup, test, and
  recovery opens only the effective endpoint after an authoritative
  unlocked-session check.
- A microphone test lasts at most 5 seconds, stores/transmits no audio, performs no transcription/model call, and cannot grant ongoing listening.
- Input selection is endpoint-ID/topology-revision bound; stale choices never select another same-name/default endpoint.
- Explicit microphone and speaker selections persist as device-local Kora overrides and win over later Windows default changes while those exact endpoints remain active.
- System microphone and speaker selections follow live Windows multimedia-default changes, including automatic WASAPI stream routing during active capture or playback; selecting System clears the corresponding Kora endpoint override.
- A missing saved/current endpoint becomes visibly unavailable and never silently switches to a newly-default, same-name, or first enumerated endpoint; replacement requires an explicit UI or validated verbal/model setting change.
- Removal/capture failure invalidates audio generations, clears incomplete input, and offers mouse recovery without cancelling unrelated task/queue state.
- Plug-in/reconnection changes do not reopen capture after manual disablement.
  Windows default changes reroute active capture only while System is selected;
  explicit endpoint overrides remain pinned.
- Device-open timeout/cancel/lock does not block status/queue UI; late success is closed and cannot enable listening.
- Generic open failure is not falsely diagnosed as busy; privacy help opens only the registered Windows settings destination with no self-elevation.
- Hidden presence/voice-only mode exposes questions/recovery from tray controls without requiring voice or stealing focus.
- Native tray context menu lists detected endpoints, saved selection and actual
  availability distinctly; selection changes do not restart recording after
  manual disablement, and Enable listening recovery uses normal readiness and
  session checks.
- Left-click opens status/questions and right-click opens the context menu without implicitly toggling capture; mute/stop-speaking/exit/update retain their existing distinct host semantics.
- Explorer restart re-registers the icon without reopening capture; notification-area overflow and tray API failure retain accessible recovery through the existing single-instance window/launcher.
- Stale menu/device selections, lock races and delayed clicks cannot switch to another endpoint or acquire the microphone.
- Mouse replies share voice prompt identity/expiry/validation; double/stale clicks or recovery focus changes cannot approve unrelated or expired actions.
- Locked/disconnected/unknown sessions capture/test/approve nothing; sensitive recovery prompts remain hidden.

### Locked-Session and Bundled-Script Gate

- Test lock by the bundled skill, Win+L, and OS idle timeout during wake listening, command capture, transcription, TTS, and approval waits.
- Startup while locked, session disconnect, and unknown session state never acquire the microphone.
- Lock immediately closes the host audio-generation gate: no post-event samples enter detection/transcription and no late transcript starts an action.
- Device/worker capture is released within 500 ms of the observed Windows lock event in every reference-machine trial; record OS notification delay separately.
- Audio/pre-roll is cleared and TTS/sensitive interactive presentation stops on lock.
- Voice/PTT/shortcut/skill/runtime requests cannot reopen capture while locked.
  Only a normal authoritative unlock may restore previously enabled readiness
  or separately qualified wake-only detection after confirmed closure and fresh
  microphone-matrix gates, without replaying pre-lock input or actions.
- The lock script cannot access protected Kora resources, arbitrary commands, remote endpoints, credentials, or elevation under its actual execution profile.
- Missing containment or session-control support is an explicit failed gate, not permission to run an unrestricted fallback.
- Denied, failed, and unconfirmed lock attempts have truthful receipts and no automatic uncertain retry.
- Future script-backed lock and other side-effecting built-ins require an exact, version-bound approval on both direct and suggested routes; PowerShell installation alone grants no execution permission. Verify once/session/always hash-bound execution grants, invalidation on changed implementation/invocation/dependencies, and that read-only commands remain available without the runner. See [skill and task execution design](../docs/skill-and-task-execution-design.md).
- An unaddressed standalone lock creates/commits a new Active control work
  session and request/task/proposal lineage without routing inference or a
  general task slot. Show the binding before approval/dispatch; offer Session
  only after durable binding. Persistence failure dispatches nothing.
- Test explicit Active-session addressing, ambiguous/Done targets,
  selected-window changes, concurrent locks, and all-slots-busy priority
  dispatch. New standalone sessions cannot reuse another Session grant;
  receipts/grant use stay bound to the original request. Done/deletion ends
  authority, resume does not restore it, and restart never replays the lock.
- Ordinary background-task permissions are not confused with microphone policy; no interactive approval is accepted while locked.

Use real Windows session events and device handles on the reference setups, with synthetic audio and disposable protected-resource fixtures.
Run at least 30 lock/unlock cycles per relevant capture state and verify rejection of stale callback generations.

### Performance Targets

Run at least 30 measured trials after 5 warm-up trials. Also report cold start separately.

| Metric | Proposed p95 target | Measurement boundary |
|---|---|---|
| Push-to-talk feedback | <= 150 ms | Button/key activation to visible listening state |
| Wake activation feedback | <= 500 ms | End of spoken "Kora" to visible capture state; audible cue only when speech policy permits |
| Local transcript readiness | <= 2 seconds | End of a <= 10-second utterance to displayed final transcript |
| Clipboard preview | <= 250 ms | Request execution to preview of supported <= 32 KiB text |
| Local cancellation acknowledgement | <= 250 ms | Cancel activation to cancelling state and blocked new calls |
| TTS stop | <= 250 ms | Stop activation to no further playback samples |
| Verbal interruption | <= 500 ms | End of user-spoken "Kora" during TTS to stopped playback and command capture |
| Local worker shutdown | <= 5 seconds after cancellation | Cancellation to tracked process tree exit |
| Local TTS first audio | <= 1 second | Approved summary enqueue to first playback sample |
| Host streaming overhead | <= 100 ms | Adapter answer event to displayed text |

End-to-end provider first-token latency is recorded, with a provisional p95 goal of <= 10 seconds on the recorded network.
Report provider and host contributions separately; failing the goal requires an explicit release decision, not a claim that cloud timing is guaranteed.
Policy and cancellation safety gates are not waivable through that performance decision.

For speech usability, use 30 fixed utterances across at least 3 speakers in quiet and recorded office-noise conditions.
At least 90% must resolve to the intended non-destructive command without correction in quiet conditions.
Report noise performance separately. Ambiguous action/approval fixtures must never execute the wrong action.

If CPU-only speech misses targets, adjust the engine/model or explicitly revise the supported hardware/scope; do not silently enable cloud speech.

### Wake-Word Quality Gate

Test the packaged local detector and playback handling on the reference headset and a supported speaker/microphone setup.
Record models, thresholds, distances, volume levels, and playback-rejection configuration.

- For each setup, use at least 100 "Kora" activations across at least 5 speakers in quiet and recorded office-noise conditions, plus 100 during TTS.
- Require at least 95% activation recall in quiet and 90% in office noise and during TTS, reporting each condition separately.
- Use at least 10 hours of negative audio per setup, including similar words, background conversation, and media; require no more than 1 false activation per hour.
- Replay at least 100 Kora-generated outputs containing "Kora" and activation cues; require zero self-activations and zero playback-derived commands.
- Where supported, replay wake/control phrases through each active Windows output/loopback device and require zero accepted commands attributable to local media or conference playback; unsupported system-output correlation is disclosed and headset/PTT guidance is verified.
- Verify command-prefix preservation for immediate wake-and-command utterances, and no duplicate task from one activation.
- False activations must never bypass context consent, remote egress review, or action approvals.
- Measure wake-listening-only overhead for 30 minutes: mean CPU <= 5% of total reference-machine capacity and incremental working set <= 200 MiB over muted idle.

Apply the same measurements to every advertised custom profile, as defined in [Assistant and Activation Name](Activation_Name.md).
Test that the configured custom name rejects the removed/default name and that another rename retires the prior custom name.
Rename validates spelling/pronunciation and readiness and confirms the exact resulting name.
Failed preparation/calibration/persistence or stale prompt/config revision leaves the old set active; late old-generation callbacks cannot start commands after cutover.
Renaming while muted/locked never opens capture; restart retains the name
preference without creating consent or bypassing the microphone matrix.
Missing/corrupt committed custom-only profile reports Unavailable with tray recovery, not silent default-name or cloud activation.
Rename prompts/TTS mentioning every active or proposed name produce zero self-activations; names never enrol a speaker or authorise a tool/action.
Custom profile preparation uses only protected data-only setup, with bounded local calibration and no Kora code/resource mutation.
First-run mouse setup can select a validated custom-only profile before ongoing listening.
Test neighbouring instances with distinct names, similar-sounding names, background/default-name speech, and crosstalk; custom-only must not retain "Kora" as a hidden recovery alias.

These are proposed release targets, not measured detector capabilities.
Wake activation is mandatory for Slice A: shipping PTT alone does not pass this gate.
If the detector fails, change/tune the local implementation or explicitly revisit supported hardware; do not defer activation or switch to cloud listening.

### Shared-Space and Optional Speaker-Verification Gate

- With verification absent/disabled, baseline deliberate voice/UI interaction remains available under normal output/privacy policy. With owner-aware privacy enabled but unhealthy/stale/uncertain, private content uses a neutral visual notice rather than silently disabling protection.
- `LikelyOwner` permits private speech only when normal content/output policy also permits it; it never satisfies remote-egress, tool, update, setup, power, account, credential, or security-setting approval.
- With owner-aware protection enabled, `NotOwner`, `Uncertain`, and
  `Unavailable` never disclose whether a sensitive resource exists through
  spoken wording. With it absent/disabled, missing confidence alone does not
  force visual-only output; normal content/output/call policy still applies.
- Shutdown/restart, reduced privacy policy, and other permitted high-risk operations accept exact action-specific voice or UI confirmation subject to the protected-call origin gate; preserve actual required OS/provider verification. Explicit verification enrollment changes retain protected OS reauthentication.
- Explicit verifier enrollment/replacement requires Windows Hello or equivalent native reauthentication, multiple randomized prompted phrases, explicit consent, and a native non-voice completion path; ordinary command/learned-profile data never silently enrolls or updates that verification identity.
- Verify that raw enrollment audio is discarded after derivation and that templates, scores, phrases, and biometric diagnostics are absent from roaming storage, model/tool/skill context, logs, telemetry, crash reports, and default backups.
- Bind the protected template to the Windows SID and device; changed SID/device binding, stale enrollment, unsupported microphone transition, missing assets, verifier error, and policy denial yield `Unavailable`, never owner.
- Test genuine-owner false rejection across time, quiet/office noise, supported microphones, illness/voice variation fixtures, and playback conditions.
- Test false acceptance using at least unrelated speakers, similar voices, household/nearby-speaker fixtures, recordings, Kora TTS, speaker playback, virtual/loopback devices, and representative synthetic/cloned speech.
- Record false-accept and false-reject rates, thresholds, verifier/anti-spoof model versions, supported hardware, and residual limitations; failing the approved risk target leaves verification unavailable rather than weakening policy.
- Repeated mismatches and suspected replay/synthesis are rate-limited and auditable without retaining biometric audio.
- Delete/re-enroll removes the prior protected verification template and invalidates cached confidence; chat history never silently enrolls/adapts identity. Privacy settings use explicit voice/UI confirmation subject to the protected-call origin gate; explicit verification changes cannot bypass OS verification.

### Optional Frequent-Speaker Learning Gate

- Learning defaults Off and requires separate explained consent; wake/microphone consent, model text, prior history, or first use does not enable it. Decline/unavailable assets/failure preserves baseline voice and safety controls.
- Enable through equivalent trusted voice/UI workflows outside protected calls, without mandatory verifier enrollment; during protected calls, reject voice-originated learning/profile mutations and require new UI initiation.
- Use only newly activated bounded command audio after consent. Ambient audio, archived/session recordings, Kora output, known playback, and mixed/uncertain sample fixtures never update the profile; disabling stops learning immediately.
- Derived features are local, OS-protected, SID/device-scoped and isolated from history/grants; raw samples are discarded and samples/features/templates/match scores/inferred identity never enter model results, roaming, logs, telemetry, or remote processing.
- Establish quality targets before advertising under D-006; test predominant-speaker attribution, genuine recognition improvement, uncertainty, multi-speaker/drift/voice-variation/microphone changes, and correction without claiming authenticated ownership.
- A few different-speaker samples cannot silently replace an established primary profile; replacement requires explicit confirmation. Learning cannot update a separately enrolled verifier, satisfy an approval, create grants, or relax call/egress/privacy policy.
- Status/test/correct/reset/delete disclose actual readiness. Off does not falsely claim deletion; reset/delete removes derived profile/cached confidence, stops adaptation until renewed consent, and preserves unrelated history/grants/verification enrollment.
- Profile management/model tools accept no sample/embedding/identity payloads, carry trusted user-request origin, and return only coarse capability/progress and trusted workflow references.

### Work-Management Gate

- While an execution runtime/tool is deliberately blocked for 60 seconds, management accepts new requests, resolves clear contextual queue operations, and asks on ambiguous targets without waiting for that execution call.
- Actual pinned runtime sessions prove independent management progress and no cross-session context, tools, or approvals.
- The pinned provider/account tier and SDK/API terms permit the tested independent sessions; record version, tier, rate limits, estimated maximum cost, and any unsupported deployment class.
- Enforce one in-flight management request, 32 KiB input, 4 KiB output, 15-second deadline without automatic retry, and 30 remote calls per rolling hour per profile.
- Timeout, sign-out, offline, throttle, quota/cost cap, malformed proposal, and hourly-cap fixtures enter deterministic degraded mode without blocking cancellation, factual status, direct queue controls, or task execution.
- Local cancellation and basic status still work when management inference is blocked, offline, or timed out.
- Enforce the admitted configurable execution limit and one task per session across admission, dispatch, cancellation, and completion races, with isolated context/runtime/grants.
- The fixed priority session-lock control can run without waiting for the task queue; it does not admit another general executor or grant script access to management inference.
- A scripted dialogue adds two tasks, reorders one, removes one, and replaces the active task; acknowledge every change with the correct task identity.
- Status questions report the observed stage, blocker, known remaining steps, and queue. Missing step/ETA evidence is stated as unknown.
- A planned step cannot be reported complete without supporting events/receipts.
- Full queues do not silently drop work; 30-minute expiry withdraws pending execution eligibility and notifies the user without deleting retained request/ledger history.
- Cancellation preserves pending entries and pauses dispatch; "stop all work" clears them.
- Failed/unknown prerequisites and unresolved remote effects prevent automatic dependent/replacement dispatch.
- Approval replies cannot apply to queue clarification or another task, including when status questions interrupt an approval.
- Clipboard references remain bound to the admission snapshot; expired/missing context requires a fresh selection.
- Remote management receives only reviewed context; local-only mode makes no remote management call.
- Session deletion removes its pending/history content; restart restores readable request/ledger evidence as interrupted/unknown, never automatic pending dispatch.

Measure local ledger-status rendering at p95 <= 250 ms from final transcript/control recognition, excluding speech recognition and TTS synthesis.
Measure management acknowledgement independently of the blocked executor; model inference has a 15-second deadline and an explicit clarification/error path.
Use at least 30 measured trials and concurrent completion/cancellation fixtures.

## Gate 2: Read-Only MCP and Skills

- One real configured connector and a deterministic test server pass admitted-tool tests.
- Unregistered and write-effect tools cannot be invoked in this slice.
- Authentication failure never causes account substitution.
- Tool schema/endpoint changes suspend affected tools.
- Read grants are resource- and identity-scoped and revoke immediately for new calls.
- Tool output exceeding 64 KiB is bounded and visibly marked.
- Tool results retain source, identity, retrieval time, and classification.
- Tool/skill/MCP/rendered content containing hostile instructions cannot grant permissions, change host stages, or trigger actions merely because an existing/preconfigured grant could cover them.
- Every proposed action cites the authenticated user request or native-approved host plan step; actions/resources/destinations introduced only by untrusted content are rejected or freshly reviewed.
- Derived summaries retain source taint/restrictions, and every new content-bearing source or materially broader transformation triggers delta egress review.
- New results require egress assessment before submission to a remote model.
- A skill with missing tools or incompatible schemas is disabled with a dependency explanation.
- User-approved profile roots are discovered read-only; no whole-profile scan or implicit enablement occurs.
- Native declarative and supported instruction-only `SKILL.md` fixtures normalise correctly without rewriting their sources.
- Required unsupported scripts/workflows and malformed metadata disable the package with a reason; no dependency installation or executable trust promotion occurs.
- Duplicate IDs/names across bundled, shared, and Kora-specific sources require source-qualified selection; shared packages cannot shadow the original lock skill.
- Content change/deletion/access loss blocks new dispatch; active tasks retain snapshots and queued tasks never silently change revisions.
- Shared skill instructions sent to a remote model pass the same reviewed egress policy as other selected context.
- Cancellation and disconnect tests leave no falsely successful task state.

## Gate 3: Voice-Driven Skill Authoring and Application Integrity

- A user creates, refines, tests with mocks, and separately confirms exact save/enable through voice, UI, or mixed input with no manual file editing.
- Spoken capability summaries and the displayed diff identify the same proposed revision.
- The proposal phase creates no installed-skill writes; staged proposals remain data only.
- Native save approval binds exact package files, base hashes, and new content; later refinement invalidates approval and the saved revision remains disabled.
- A changed base, traversal path, hard-link alias, or disallowed reparse-point target prevents application.
- Denial, expiry, and cancellation prevent pending writes.
- The skill validator rejects invalid manifests, executable directives, scripts, assemblies, hooks, arbitrary paths, and custom validators without launching code.
- Simulated examples cause zero real tool invocations or side effects; model-based examples retain egress controls.
- A real trial is a separate task with normal tool/identity/resource/action approvals.
- Missing tool dependencies are reported; Kora does not install or rewrite their implementation.
- Validation failure does not save/enable the proposal.
- Partial write and worker crash fixtures preserve evidence and never overwrite unrelated edits during recovery.
- Changed skill content is not activated before policy/dependency checks and separate exact voice/UI enablement confirmation covering tools, resources, destinations, and instruction-risk summary.
- Active tasks retain their original digest-pinned skill revision while new invocations use the enabled revision.
- A request to improve a bundled skill creates only a distinct declarative user adaptation; it never mutates embedded resources, copies executable scripts, or shadows reserved controls.
- Editing a shared profile skill creates a reviewed copy under `%APPDATA%\Kora\Skills` with attribution and a distinct scoped ID; source bytes, names, and timestamps remain unchanged.
- Actual Roaming AppData resolution works with a non-default profile location; no hard-coded user path or arbitrary model-selected destination is used.
- Skills remain partitioned from `%LOCALAPPDATA%\Kora` enablement/audit records and the credential facility.
- Roamed definitions on a second simulated device do not inherit enablement, credentials, action approvals, script trust, or priority routing.
- Incomplete/conflicting roaming revisions are disabled for reconciliation; interrupted saves do not silently replace the last valid revision.
- Local-only mode rejects network-backed skill roots explicitly; Kora does not silently move storage or claim control of OS-managed synchronisation.
- Removing a shared source registration leaves source files intact; clearing conversation history does not delete authored skills.
- Skills cannot change policy, register arbitrary executable loading locations, or grant permissions.

### No-Self-Modification Gate

Use disposable protected fixtures and actual deployment identities/ACLs, not only mocked permission checks.

The [R02 snapshot](../experiments/r02-containment-proof/evidence/README.md)
provides partial denial/lifetime evidence, not a passed gate. The
[continuation plan](Security_Data_Flows.md#windows-containment-continuation-gates)
and [R17 deployment checklist](Security_Data_Flows.md#protected-deployment-identity-and-validation)
define the remaining real-boundary trials.

- Inspect source-build and precompiled publish artifacts: every built-in manifest/instruction/fixture/script is an embedded application resource, with no loose built-in skill files or writable execution extraction.
- Test resource catalogue identity/digest validation and missing/corrupt resource failures; reject rather than searching profiles, PATH, other assemblies, or caches for a substitute.
- Verify explicit built-in manifests identify their Markdown document, entry point, and complete multi-script set; shared helpers have one embedded identity referenced by multiple skills. Native review exposes all manifest-listed files in named read-only tabs, with a separate syntax-highlighted tab per script/helper; displayed and executed bytes agree.
- Prove the [combined script-set encoding](Built_In_Skills.md#deterministic-script-set-hash) against fixed byte vectors: ordinal full-name order, length framing, original bytes, and independence from catalogue/list enumeration order.
- Shared-helper changes revoke every dependent skill/task grant for once/session/always, not unrelated skills; manifest/Markdown changes require review even with an unchanged script-set hash. No grant transfers between skills or upgrades automatically from a single-script/action-name grant.
- Revoked hash-bound authorizations never return when old bytes are restored; perpetual records retain the revoked state/reason through restart and session/audit cleanup. Fresh approval creates a new grant rather than changing the old digest or reactivating it.
- User voice/UI/authoring/import operations cannot edit/delete/override embedded skills; same-name profile skills and exported resource text never become privileged originals.
- Execute verified resource snapshots without loose script files; runtime incompatibility fails explicitly, without a temporary-file or unrestricted-shell fallback.
- Tampered/replaced application fixtures are rejected where protected deployment identity, external release metadata, or installed-file permissions can detect them; changing both embedded bytes and internal checksums does not satisfy the unsigned release's external origin/digest checks.
- Built-in skill updates occur only through a verified whole-application replacement; test that ordinary skill refresh/updates cannot replace resources or catalogue registrations.
- Source builds report local build provenance, not an unsupported official publisher-signing claim; excluded administrator/custom-build threats are documented truthfully.
- Attempts to write/delete/rename Kora source, binaries, bundled components, Git metadata, build outputs, trust configuration, and updater settings are denied before mutation.
- Test direct paths, traversal, alternate clones, API repository identifiers, hard links, reparse points, and target replacement between approval and application.
- User/model/skill instructions and an otherwise valid approval cannot override the protected-resource denial.
- Model-directed Git, build, process, MCP, remote deployment, and updater paths cannot mutate protected Kora resources.
- Agent-executable workers/servers cannot directly write protected fixtures with their actual OS identity; if isolation cannot be proven, admission rejects them.
- Remote connectors with unbounded mutation/execution access to protected Kora resources are not admitted.
- The normal application identity cannot modify installed executable components; the writable skill store is not searched for executable code.
- Development deployments with unknown source/worktree protection disable affected writes/execution explicitly.
- No model-callable elevation, executable installer/update, or application-maintenance entry point exists.
- The authorised out-of-band maintenance path is tested separately and does not receive an agent-issued approval token.
- Record actual non-elevated application and contained worker/descendant tokens, protected payload/parent ownership and effective ACLs; user ownership, inherited write/delete-child or DACL-changing rights fail the boundary even when an individual Users ACE is RX.
- Verify selected shared runtimes/native dependencies and executable-resolution paths, not merely the main EXE location or an installer recipe. Build/setup assembly without installed Windows trials leaves deployment/runtime-only acceptance blocked.
- Network-denied profiles require attributable OS enforcement evidence with working uncontained positive controls for applicable address families/protocols and descendants. Unexplained timeouts, absent capability declarations and generic unrelated firewall blocks do not pass.
- Test the owner-approved [best-effort transitive tracking rule](Built_In_Skills.md#best-effort-transitive-dependency-tracking) for both bundled and future scripts: identify supported references without execution/out-of-scope scanning, show unresolved/dynamic gaps and user responsibility, revoke on observed declared/tracked changes, and do not claim complete transitive discovery/change detection. Required declared/runtime identities still fail closed. Unknown transitive references alone do not create a new top-level task or relax containment.
- If an explicitly exact-dependency profile is offered, test undeclared executable/module/script denial separately from descendant identity/job breakaway; an inventory or best-effort grant is not enforcement. The [W2 ACL/no-child candidates](../experiments/r02-w2-dependency-proof/README.md) are rejected for that guarantee, not passed by the revised default policy.
- Verify the synthetic W2 helper/entry effect is not promoted to full built-in/helper/power acceptance. Native loading, inherited object identity, real lock/power effects, production lifecycle/races and installed protection require their own evidence.
- Missing, malformed or uncorrelated effect receipts remain Unknown after cancellation/termination; actual effect-before-receipt-loss fixtures produce no automatic side-effect replay.

Any successful protected-resource mutation fails the release gate; a prompt warning or log entry is not a substitute for enforcement.

## Cross-Cutting Failure Tests

Exercise timeout, lost connection, runtime crash, microphone removal, credential expiry, approval expiry, oversized input, and app restart in every relevant state.

Verify:

- No write retries after unknown outcomes.
- At most one eligible read-only transient retry.
- Late answer events cannot overwrite cancelled/failed terminal states.
- Unknown remote side effects remain labelled unknown until reconciled.
- Restart does not resume tools or replay consumed approvals/dispatch tokens;
  still-applicable Active-session and Perpetual grants require fresh host
  revalidation under their existing scope rules.
- Executable trust disclosures match actual OS rights.
- Disabling an extension blocks new invocations.
- Audit metadata defaults to 90-day retention and accepts only configured values from 30 through 365 days. Diagnostic database retention defaults to 30 days under its independent bounded setting; the daily file sink always enforces both 30-file and 30-day limits.
- Every permitted `ILogger` event is independently delivered to daily structured JSON under `%LOCALAPPDATA%\Kora\Logs` and private local SQLite. Ordinary records use `application_log_events`; trusted typed audit records use `security_audit_events` and are not mixed into the ordinary table. Each database row receives the due time from its own effective policy; lookalike properties confer no authority.
- For both tables, emit fixtures containing named/numeric event IDs, categories, levels, message templates, null/Boolean/integer/real/string/GUID/timestamp properties and nested scopes. Verify the original template and value kinds round-trip independently of rendered text and current culture; no supported property is silently flattened to a string or lost.
- Verify admitted W3C trace/span/parent, activity source/name/kind, correlation/session/task/invocation/approval fields are captured at the log call and promoted/indexed from trusted structured state, while bounded residual properties remain queryable through the schema-versioned typed property representation. Unsupported or oversized values fail/report their safe projection rather than invoking arbitrary `ToString()` or storing an unbounded object graph.
- Database and file diagnostics contain no recognized transcript text, response body, synthesized speech text, raw audio, credentials, secrets, decrypted SQL parameters, database keys, or raw security/tool arguments.
- Evidence search filters and paginates permitted session, diagnostic and audit records by bounded time/source/severity/event/category/correlation/session/task/invocation/approval/action/outcome/safe text, preserves stable source citations and authority kind, and reports retention expiry, redaction, unavailable sources and known ingestion gaps.
- Logs, Audit and All Evidence views independently list, read and search their retained records without a model or network. Audit reads expose typed immutable fields and related request/terminal/approval/invocation links; log reads expose permitted structured state and daily-file provenance.
- Instrument startup, UI and voice request acceptance, session routing, queue admission/dispatch, approval, runtime/provider calls, tool execution, storage, presentation, evidence query/reasoning, retention and recovery with stable `ActivitySource` operations. All activities terminate with truthful status on success, failure, cancellation and unknown outcome; activity names/tags contain no user text, path, title, credential or response content.
- Verify nested async work preserves W3C parentage, queued/fan-out/reconciliation work uses explicit `ActivityLink`, and restart creates a new trace joined only by durable Kora IDs. No Kora session is represented by one long-running activity.
- Model-facing evidence access returns at most 50 events and 64 KiB per page. Daily-log access rejects arbitrary paths and malformed log names and returns no more than the configured 1,000,000-character tail bound.
- Database-open/migration/key/commit and diagnostic-buffer failures remain visible in the daily file without recursively entering the SQLite provider. Recovery records a query-visible sink/gap marker; failure never silently drops required audit/session evidence or turns a file audit copy into authorization evidence.
- Every security-relevant write, application execution, script execution, protected operation, and approval transition emits a correlated request and terminal audit event at the policy/execution boundary.
- Audit fixtures distinguish requested, succeeded, failed, denied, and cancelled outcomes and preserve initiator, canonical action/target identity, optional approval identity, and bounded reason codes.
- Verify the SQLite logger routes the common structured logging envelope and fixed audit fields into typed `security_audit_events` columns without parsing rendered text, while the same structured event remains present in the daily JSON file. Ordinary logs preserve their envelope in `application_log_events` and cannot acquire audit authority by supplying a lookalike template, message or property name.
- For one session spanning multiple requests, queued work and a restart, verify every session-bound log, span and audit row carries the same host-owned session ID while traces remain distinct. From the session UI, list Logs/Audit/All Evidence; from any row, pivot to the correct trace tree, task, invocation, approval and audit request/terminal pair without cross-session records.
- Expire 30-day diagnostics while retaining 90-day audit rows. The session/audit views preserve trace/span/session/correlation IDs and show missing diagnostic/span segments explicitly; they do not invent expired nodes or recover deleted session titles/content.
- Suppress ordinary execution-context flow and exercise queue/IPC adapters; missing context is restored or represented with an explicit link/new root according to contract. Session-bound audit commits without valid host activity/session context fail visibly before consequential dispatch; explicitly classified pre-host bootstrap diagnostics are the only allowed uncorrelated records.
- Inject hostile incoming W3C headers, model-supplied session labels and logging properties. They may be retained only as untrusted bounded observations and cannot select a Kora session, overwrite host trace/session columns, answer/approve, or grant authority. No trace baggage containing Kora identity/content is emitted to providers.
- Audit events contain no transcript/command text, raw parameters, paths, process arguments, script source, setting values, content, exception text, credentials, or secrets.
- Kora restart, Windows session lock, device-local configuration writes, and protected power proposals produce the expected current audit sequences, including failed, cancelled, and superseded outcomes.
- Before general write, application/script execution, or security approval is enabled, tests prove the authoritative audit store is host-owned, append-only or equivalently tamper-evident, and cannot be bypassed by a model, skill, tool, worker, or runtime adapter.
- Advance clocks around 30/90/365-day audit boundaries and different diagnostic/session policies. Search/browse/export/reasoning does not refresh due times; session deletion preserves only independently retained content-minimising audit rows; audit expiry preserves unrelated diagnostics and perpetual grants.
- Reject audit retention below 30 or above 365 days. The [delivered bounded option](User_Configuration.md#delivered-bounded-future-only-audit-retention-r10r04) defaults/resets to 90 and applies only to NEW committed required authority audit and independently qualified diagnostic audit projections; prior-policy requested/terminal receipts, atomic readback and durable intent outcome precede activation. Existing deadlines, bytes, grants and relationships remain unchanged. Apply-now/immediate deletion/audit pruning are unavailable in that delivery. The broader proposed shortening preview/affected count/time range and separately confirmed apply-now flow remains unimplemented; extending retention cannot resurrect purged rows.
- Audit expiry leaves a verifiable retention continuation/checkpoint and latest minimal chain anchor without retained action/content payload. Removing, editing, or forging `application_log_events` or daily JSON cannot authorize an action, satisfy an approval, alter a receipt, or conceal an unknown effect in retained `security_audit_events`.

## Distribution and Startup Gate

- [Instance Coordination](Instance_Coordination.md) tests cover simultaneous duplicates, exact-build identity, approved/refused/expired takeover, worker quiescence, authenticated/stale IPC, and at-most-one capture/task owner.
- Return-offer tests cover replacement exit/crash, explicit mouse acceptance/decline, changed original executable, supervisor loss, locked session and competing owner; no automatic restart/listening or shared production-store mutation.

- The source bootstrap succeeds from a clean reference environment with declared build prerequisites and a pinned revision.
- Missing prerequisites, authentication failure, restore/build failure, and interrupted installation yield explicit actionable errors; no unrelated checkout is changed.
- Bootstrap rerun does not duplicate installation/startup registration or overwrite local edits.
- The CI-produced framework-dependent artifact runs on a clean runtime-only machine with no Git, .NET SDK, build tooling, or source checkout.
- Launch setup successfully with no speech models, database files, or Ollama present; initialise selected capabilities from the running app without Git/SDK.
- Validate launch-critical packaged libraries and embedded lock-script resources, plus speech readiness after Kora-led explicit asset setup.
- Missing/incompatible runtime and architecture cases identify the actual requirement; no SDK install or source-build fallback occurs.
- Derive pre-launch prerequisites from the exact published runtime configuration and native imports. For the current R02 baseline, test missing/base-only/wrong-major/wrong-architecture .NET against both declared .NET 10 shared frameworks, then record the actual supported x64 Desktop Runtime patch selected. Test absent/incompatible VC++ x64 runtime dependencies and delayed/dynamic native loads; registry or DLL presence alone does not pass runtime-only launch.
- Source and binary deployments preserve the same skill/data partitions and application-integrity guarantees.
- Start-at-logon is opt-in, runs published binaries as the interactive user, starts only one instance, and never builds or elevates.
- Ordinary unlocked logon/restart tests automatically begin listening when ready;
  locked-session startup acquires nothing and does not invent prior enabled
  intent. Test fresh-gated normal unlock restoration separately from explicit
  recovery after manual disablement, resume, disconnect or failure.
- Uninstall removes startup entries and offers data retention without deleting shared profile skills.
- Installation metadata, not `.git` presence, determines maintenance mode; developer checkouts are not automatically pulled/reset.
- Binary update checks use the canonical GitHub Releases feed without Git/SDK and respect channel/architecture/runtime. Fixtures exclude drafts in every channel and prereleases in production; explicit preview discovery can find published prereleases without depending on the latest-production endpoint. Reject other hosts/repositories, CI artifacts and branch builds as released versions.
- Failed/offline checks are not reported as current; repeated checks respect cadence/backoff and maintenance network policy.
- Eligible release metadata produces an unprompted unsigned/manual-update notice when voice delivery is permitted.
- "Update/install Kora" never downloads, stages, executes, mutates source, or activates an artifact during the unsigned phase; it can only show details or propose the exact canonical release page.
- Browser navigation to the release page is bound to the canonical URL and is not installation approval.
- Agent tools cannot invoke maintenance, change update origins, choose executable payloads, or fabricate release-availability events.
- No package-level or source auto-update authority exists during the unsigned phase.

An install-capable updater remains unavailable until independent signed-metadata trust, rollback/freeze protection, key rotation, exact host-owned approval/mandatory OS checks, and separate acceptance evidence are added.

### Public GitHub and Linux-First Release Gate

- Build/test/cross-publish and publish release metadata on clean Linux Actions runners wherever feasible; assemble the WiX MSI and single Burn setup EXE in an explicit pinned Windows job. Verify payload digests/revision/provenance across the job boundary; justify other Windows-specific work rather than moving portable stages by default. No Wine or Linux-runtime support claim.
- Version-tag builds select production candidates from exact protected tag revisions; routine branch/main builds do not become production releases. Exercise an absent version, matching published version, provenance/channel/artifact conflict, draft/prerelease, interrupted candidate, concurrent publication and failed/ambiguous GitHub lookup. Already-published matching versions skip build/publication; no case overwrites released assets or moves a tag, and publication still requires all licence/Windows/approval gates.
- Verify Windows RID/native dependency completeness, runtime-only packaging, embedded resources, licences, and final-byte hashes/provenance.
- Close the [distribution follow-ups](Implementation_Roadmap.md#r02-distribution-follow-up-and-r17-delivery): NSIS-only acquisition/build code and the Linux recipe are retired; retain historical receipts and source/native checks until equivalent maintained tooling supersedes them. Apply repository licence/NuGet-notice controls and separately clear WiX build-tool terms and external/native/model assets; cross-publishing does not prove installed behavior.
- Application, maintenance, and setup artifacts contain no Authenticode signature during the initial unsigned phase; release notes and installer documentation disclose this and expected Unknown Publisher/SmartScreen behavior. Setup has no unsigned/POC banner.
- Published SHA-256 values match the downloadable final bytes, and the UI/documentation never presents those hashes or build attestations as Windows publisher authentication.
- Hash each final setup assembly independently and bind its provenance and Windows trial receipts to that digest, even when the source/payload/name is unchanged. A prior assembly's receipt cannot certify later bytes.
- Fork/PR checks cannot access release-write permissions or production identity; approved release workflows use pinned actions and protected revisions/environments.
- Public binary acquisition/update metadata needs no user GitHub token, Git, or SDK; rate-limit/cache/offline/feed failures remain truthful.
- Official release lookup accepts only the configured canonical repository, approved release record, immutable source revision, expected artifact identity, and matching digest; it cannot substitute fork artifacts, generic workflow outputs, default-branch commits, or mutable tag names.
- A single setup EXE contains the intended payload; any companion updater metadata/packages refer to the same exact approved release identity.
- External Windows integration evidence binds the final artifact digest and covers runtime/native assets, install/UAC/ACLs, unprivileged launch, device/tray/lock, and protected update/recovery.
- Missing mandatory Windows evidence leaves production-acceptance candidates draft/unreleased despite successful Linux builds. The owner-approved unsigned beta/stable POC publication policy is separate: front-loaded/ad-hoc/risk-based installed validation, not exhaustive manual trials of every MSI. Every CI release still needs locked dependencies/licences, tests/coverage, exact payload checks and non-skipped MSI ICE; publication does not close production gates.
- Static embedded-documentation/resource inventories do not pass future bundled-skill/interpreter/worker gates. Repeat packaging and installed Windows checks when those components exist; blocked or absent components remain explicitly unaccepted.
- WiX MSI/Burn implementation passes fresh install, upgrade, repair, failed/interrupted operations, previous-version recovery and uninstall on approved disposable Windows deployments. Verify prerequisite/bootstrapper closure, optional setup refusal/re-entry, preservation of unrelated/pre-existing dependencies and user data, unprivileged app launch and actual independent protection. Standard MSI behavior or a per-user writable install does not establish the no-self-modification boundary. No separate WiX feasibility project is required, but these implementation acceptance gates remain mandatory.
- MSIX/App Installer is not an initial unsigned-package option.

## Environment Setup Gate

- The admitted host-owned catalogue and [design inventory](Dependency_Catalogue.md) agree on actual dependency identity/version/source/verification and enabled capability. Test missing/declined dependencies per capability; bundled code, launch prerequisites and experimental/unimplemented candidates are not represented as optional installable substitutes for missing application code.
- Source and binary delivery provide a complete Kora-only path with launch/build prerequisites as applicable; no provider/package selection is compulsory. Optional installer dependency assistance is independently consented, shares the app's reviewed dependency contract and never replaces in-app detection/setup or initialises user databases.
- First launch resolves known folders, creates its expected stores, and initialises SQLite without an external database installer.
- Rerun reuses valid data/schema; migration failure does not delete user data or silently create an alternative database.
- The setup UI works with no model/provider/speech recogniser configured and explains the initial visual setup exception.
- Storage and SQLite initialisation plus the independently queued PowerShell 7 readiness/setup task remain available when Ollama is absent or inference fails; none is marked blocked solely by unavailable inference.
- The currently supported local plan offers separately consented per-user Ollama 0.35.1 and `qwen3:1.7b`; verify the pinned digest and a completed nonempty response from loopback inference, not only endpoint/model metadata. Malformed metadata yields an incompatible readiness state without aborting startup.
- Capability probes distinguish absence, incompatibility, failed health checks, and blocked setup; installer exit code alone cannot mark Ready.
- Selecting a remote-only provider does not install Ollama; selecting a delivered local adapter offers only its required runtime/model steps.
- Declining all optional providers/dependencies completes basic onboarding and preserves the supported deterministic commands, native help/settings/readiness and recovery. Test the actual dependency-qualified subset, not an assertion that every built-in works without speech/interpreter assets.
- With no ready selected provider, free-form/model-mediated requests report unavailable and invoke no model or automatic cloud fallback. The user's reduced configuration does not close or waive the product's A2/provider acceptance gates.
- Declined setup remains declined across restart without repeated setup windows, downloads or installation retries; explicitly reopening/selecting the capability can start a fresh consented plan.
- With installer assistance skipped, failed or cancelled, Kora detects/reconciles current dependency state and can complete later approved setup. With assistance successful, Kora reuses and functionally verifies dependencies without duplicate installation or ownership transfer. Both paths preserve pre-existing user components and cannot imply account, microphone or execution consent.
- A pre-existing healthy Ollama installation/model is reused without changed ownership, settings, startup, deletion, or unrelated process termination.
- Approved Ollama setup verifies endpoint/version and real model inference; insufficient disk/hardware, network failure, or rejection leaves an explicit readiness state.
- Keep D-003 open until reference CPU-only latency, answer/cancellation quality, licence/storage evidence, and network-blocked offline proof are recorded; a successful bootstrap inference check is insufficient.
- Downloads/machine changes display exact sources, sizes, destinations, ownership, and any elevation before consent.
- No task/skill/model-supplied URL, command, or executable can enter the protected setup catalogue or modify Kora code.
- Lock/mute/unknown-session policy still gates microphone setup tests; no setup helper reopens capture against policy.
- Offline/local-only tasks never trigger implicit dependency download or cloud fallback; setup network access is separate and explicit.
- Interrupted setup resumes via probes and owned staging, not blind replay or broad cleanup; pre-existing user components are never removed by rollback.
- Provider setup cannot install a missing Kora adapter; that needs verified application maintenance.
- Startup registration is configured from the running app, not by provisioning an AI stack in the delivery script.

## Proactive Interaction Gate

- A trusted task/update event causes Kora to speak without an initiating utterance; no unnecessary model call is made for system notifications.
- The broker suppresses speech during lock, user capture, mute/quiet settings, or a foreground approval that would be displaced.
- Ignored/deferred/suppressed releases do not produce repeated same-session prompts; expired events are not spoken as current.
- Suggestions do not start tasks, read new context, execute tools, or install updates.
- User replies require wake/PTT and a matching current prompt; TTS cannot activate or approve its own proposal.
- A suggestion interrupted by another prompt loses response eligibility; a stale "yes" cannot approve either action.
- Task-derived proactive content retains provenance and egress restrictions; hostile tool output cannot impersonate a trusted system event.
- Visual-only delivery remains available without opening the microphone or overriding explicit listening consent.

## Call-Aware Speech Gate

- All requested/proactive/setup/maintenance speech and activation cues use the central gate; no alternate TTS path bypasses it.
- Active/Suspected call and Unknown enabled-detector fixtures suppress automatic speech by default while preserving visual results and task progress.
- Active/Suspected observations apply the persisted visual-text override by default, independently of task/queue/session/device response mode; opting out restores that ordinary precedence.
- Voice activation remains enabled during calls by default and is independently configurable; disabling it closes active capture, blocks activation while the call remains detected, and never silently reopens capture when the call clears.
- A blocking observation received during playback stops output within 250 ms on the reference machine; report source detection/transport delay separately.
- A stale sample, failed probe, auth expiry, or local-only network block never becomes a false Clear result.
- Manual call mode works without Microsoft credentials or remote access.
- No-detector setup exposes its limitation rather than falsely claiming protection or permanently disabling ordinary speech.
- Voice commands change scoped/persistent preferences, clarify ambiguous duration, and display acknowledgement without speaking through the policy being set.
- A speak-once request targets one response, expires after 2 minutes, and cannot drain a backlog or persist a weaker policy.
- Temporary call overrides expire and are revoked on lock/sign-out/restart; manual call end does not clear an automatic Active source.
- Lock, mute, explicit listening consent, egress, and action approvals remain effective under every call override.
- Shared skill names/instructions, background caller speech without a valid command, and tool output cannot directly edit host call preferences.
- Leaving a call does not replay suppressed answers/prompts or accept expired approvals.
- Optional Graph detection uses only the configured account's delegated presence permission, requests no presence-write/tenant-wide access, and discloses unsupported personal-account coverage.
- Tests distinguish calendar-derived meetings, generic Busy/DND, actual call signals, multi-device/account presence, and delayed data; none is silently treated as authoritative local attendance.
- Detector data excludes communication content/audio and is absent from outbound model context by default.

For every automatic detector offered as supported, record the tested client/API versions, account types, scope, polling/event latency, false positives, false negatives, and unsupported cases.
Exercise call join/leave, muted and output-only calls, browser/desktop clients, device switch, and disconnect/reconnect.
A failed integration proof leaves that detector explicitly unavailable with manual mode offered; do not advertise universal Teams/communication detection.

## OOTB Phrase and Lifecycle Gate

- Every catalogue entry maps to a host intent or pinned admitted skill with explicit availability and help text; aliases preserve the same target/effect.
- Exact local lifecycle/power/status/cancel controls work with the model/network unavailable after local speech setup.
- Negation, quotation, explanation requests, hostile tool content, and ambiguous "restart" do not execute a disruptive action.
- Computer shutdown/restart requires a second distinct action-specific spoken confirmation or equivalent deliberate UI confirmation bound to the exact proposal; generic "yes", speaker matching alone, stale/wrong-action input, and TTS playback cannot satisfy it. Mandatory OS checks are preserved.
- Confirmation prompts expire after 30 seconds; accepted power grants are single-use with a 2-minute expiry and no silent extension.
- Power countdown is host-owned, visible, and cancellable; the final simulated OS call has no forced-close/remote-target flags.
- A blocker/failure never triggers forced termination, an unrestricted shell fallback, or a false completion receipt.
- Active work is explicitly resolved before power dispatch; queue holds, app restart, and power actions cannot race. Unsigned-phase update checks have no activation path.
- M/E shutdown/restart calls produce pending host proposals only. The
  deterministic host lifecycle controller owns all-session review, native
  approval, countdown and gated fixed-worker dispatch; exercise the same path
  with direct voice/UI and unavailable management/execution inference.
- M cannot call task execution/skill tools, obtain script/task context,
  self-approve, consume authority or turn `approvals.request` into power
  dispatch. E proposals confer no different approval authority. Test denied,
  expired, stale, cancelled and call-policy-racing proposals with zero effects.
- Cancelling an owned power proposal restores prior dispatch policy unless another blocker requires pause; an unrelated Windows operation is not cancelled.
- Hide preserves work and listening visibility; exit/restart releases audio/owned workers, does not stop user-owned services, and requires affected-work confirmation.
- App restart and computer restart never substitute for one another; restart
  never replays queue entries, approvals or audio. Saved ongoing voice consent
  permits only fresh-gated ordinary startup under the microphone matrix.
- Pause queue leaves active execution untouched; clear queue, cancel identified work, delete session, replacement, and stop-all pause affected dispatch first and require exact voice/UI confirmation before destructive mutation.
- Check-for-updates installs nothing; unsigned-phase update interaction remains notify-only and cannot become download, staging, Git/shell, or installer authority.
- Call-gated confirmation uses visible presentation or an explicit permitted readback; suppression is not implicit approval.
- Reserved control registration cannot be shadowed by a shared skill name, user copy, or manifest priority declaration.

Use a non-destructive OS-action test double for shutdown/restart routing and cancellation tests.
Validate the production invocation's flags and actual restricted worker admission separately; do not reboot a shared development/test machine to validate phrase recognition.

## Verbal Configuration Gate

- The system tray exposes a Settings command that opens and activates one settings window; closing it permits a fresh instance without duplicating application settings state.
- One tray-icon left-click shows/activates the configured assistant after the configured Windows double-click interval; two clicks within that interval cancel the pending show and open/activate Settings without creating a second settings window.
- The settings window presents the assistant name and every currently implemented speech/audio, response-output, detected-call, and readiness setting through mouse-accessible controls.
- Settings presents System, Light, and Dark appearance choices; System is the default, persists as a device-local preference, follows live Windows light/dark changes, and Light/Dark remain explicit overrides.
- Changing the theme updates the presence, main and Settings windows, chat/answer windows, speech text, trusted approvals/errors, Markdown/diagram results, browser chrome, and generated-HTML surfaces immediately without recreating content or losing focus, selection, scroll position, task identity, or approval identity.
- Every native surface uses shared theme resources. Controlled HTML/Markdown renderers receive the resolved effective Light/Dark variant and accessible host CSS; internet author styling cannot restyle trusted Kora chrome, and no renderer keeps an independent hidden theme preference.
- Light and Dark fixtures keep text, controls, status, focus, disabled states, presence particles, links, code, tables, warnings, and approvals readable at the applicable accessibility contrast target; a renderer that cannot do so falls back to a readable native/source surface.
- Mouse and simulated verbal mutations update the same typed state, and an already-open main/settings window receives the effective value immediately without polling or reopening.
- The default assistant name is Kora. Applying a valid custom name immediately updates window titles and branding, Settings descriptions, tray labels and tooltip, command catalogue and prefix, visual responses, spoken responses, and voice preview.
- A committed custom name is the only accepted assistant prefix; the previous name and Kora are not retained as hidden aliases. Invalid input or persistence failure leaves the prior name active and reports a visible failure.
- Name changes are device-local and audited without the chosen name value. `Kora.exe`, assemblies/namespaces, `%LOCALAPPDATA%\Kora`, icon resources, diagnostic file names, and internal product/trust identifiers remain unchanged.
- If listening is active during a successful rename, capture is stopped and restarted with the new exact grammar under the existing consent; a rename while capture is closed does not open the microphone.
- Every available option, including extension options, registers a type, bounds/choices, default, scope, aliases, validation, dependency, application timing, and confirmation rule.
- Enumerate the registry and exercise discovery/get/set/reset through equivalent voice/UI configuration outside protected calls; during protected calls enforce the explicit voice/in-call origin restriction and fresh UI-initiation path. No unexplained UI-only or manual-file-only preferences.
- Exact basic settings commands work without a model/network after local speech is ready.
- Spoken absolute/relative values, units, source/device labels, and scopes resolve to concrete proposals; ambiguity or invalid values never mutate state.
- Validate exact boundary and just-outside-boundary values for every numeric range; reject rather than silently clamp.
- Changes disabling speech/call permission are acknowledged visually without speaking through the resulting policy.
- Persisted preferences survive restart; manual listening disablement does not,
  and ordinary safe startup attempts listening again. Grants that expired and
  temporary call/task overrides do not become active merely because settings
  were saved.
- Simulated persistence failure and concurrent voice/UI changes retain a valid configuration and report failure/conflict without silent lost updates.
- Lowering queue capacity does not evict entries; shortened request lifetime identifies affected entries before immediate expiry.
- Default deadline changes do not silently alter active tasks; provider/account changes do not silently transfer their context.
- Increased remote exposure requires exact voice/UI confirmation for the setting, and every outgoing payload still requires its applicable egress approval.
- Call-wide, one-hour, persistent, and Unknown-to-normal speech privacy downgrades require exact confirmation, with new UI initiation while protected; single-response exceptions remain exact, single-use, and subject to the same origin and selected privacy policy.
- Reset/undo follow the same sensitivity and validation rules as set; neither restores grants nor repeats external side effects.
- Enabled extension option schemas cannot expose arbitrary code/config paths, protected trust roots, disabled lock policy, or security bypasses.
- Missing devices/assets/providers explain unavailable effective settings and do not trigger silent download/cloud fallback.
- Verify quiet-hour boundaries, weekdays, time-zone changes, overnight intervals, and daylight-saving transitions; do not infer a permanent UTC offset.
- Closed microphone/locked session/missing recogniser tests offer the explicit activation/setup alternative and never open a hidden listener.
- Voice-triggered sign-in uses the secure account flow; passwords/tokens are never dictated into configuration or model context.

## Information Display Gate

- Speech-text off/sentence/utterance preferences work verbally and through UI; hiding captions leaves required approvals/errors available.
- Captions match final TTS segments and playback generation, not full answers or queued/suppressed text; test interruption, failure, replacement, and call gating.
- Missing word alignment never claims word-accurate highlighting; pinning labels previous speech and does not extend session retention.
- Passive captions/details do not steal focus or intercept desktop input; verify contrast, keyboard/screen-reader access, reduced motion, multiple displays, DPI and working-area changes.
- Lock/disconnect clears sensitive host surfaces and closes/suspends owned browsing; external browser behaviour is explicitly outside host control.
- Typed content updates preserve task/provenance/revision identity; late renders and content styling cannot overwrite native approval panels.
- Exercise the versioned detail-routing policy at, below, and above every initial threshold: required content kind; 5 versus 6 code lines; 8 versus 9 list items; 3 versus 4 citations; one versus two titled sections; 120 versus 121 words; 3 versus 4 paragraphs; accessibility/layout constraint; and exact/provenance-bearing representation. Provider/model hints cannot force or suppress a detail window.
- Classification waits for finalization: streamed structural changes do not repeatedly open, focus, or prompt. Corrected/replaced output gets a new item/revision/offer and stale callbacks cannot open the previous or latest unrelated response.
- Every detail-recommended compact result exposes a native keyboard/screen-reader Open details action bound to its exact session/item/revision. Click/Enter/Space opens or activates it without rerunning work; later compact updates and duplicate titles cannot retarget it.
- Under default Offer, a voice-origin request with no subsequent request/session UI interaction and a voice-first/voice-only request receive one permitted spoken offer after the concise summary. UI-origin or already UI-interacted requests retain the link without a redundant spoken question.
- Accepting the unique bound offer by voice or UI opens and focuses that item with no model round trip. Not now, silence, dismissal, or expiry opens nothing and leaves History/Open details available. Speech unavailable/suppressed/private-policy cases do not claim an offer was spoken.
- Detail offers are lower priority than approvals, required clarification, errors, and device recovery. They cannot steal a generic yes target; conflicting/multiple offers require an exact session/item command and never open whichever result happens to be selected/latest.
- Verify Offer, Open automatically, and Link only through typed settings and equivalent verbal changes. One offer answer never mutates the preference; automatic mode does not open background results or bypass lock, access, privacy, renderer fallback, or trusted approval UI.
- Detail admission is exercised for embedded documentation, host-generated reports, model/tool/skill output, immutable skill files, an explicitly selected local document, and a remote URL; each receives its declared profile and no source is promoted to trusted controls.
- Reopening one immutable detail reference activates the existing viewer, explicit comparison preserves distinct item identities, and a new revision never silently replaces the open item.
- Long admitted content uses measured virtualization/paging/source fallback and reports renderer limits without silent truncation, unbounded native control creation, focus loss, or queue starvation.
- Markdown uses the native bounded presenter rather than an implicit Markdown-to-WebView conversion; selecting the separate static-HTML adapter requires an admitted static-HTML item.
- Markdown tables/code/task lists render without execution; raw HTML, unsafe schemes, remote images and incomplete streamed fences remain inert.
- Markdown fences, standalone scripts/source, manifests/configuration, generated code, diffs, and code in skill/evidence review use one bundled versioned highlighter. Inline code remains readable monospace without requiring tokenization.
- Verify deterministic language resolution precedence for host metadata, immutable filename/extension, allowlisted fence alias, and plain-text fallback. Conflicting, unknown, spoofed, and unsupported identifiers cannot load a grammar or change execution/approval policy.
- Exercise the initial PowerShell, C#, JSON, YAML, XML/XAML, Markdown, SQL, JavaScript, TypeScript, HTML, CSS, shell, batch, INI/properties, and unified-diff grammars in Light and Dark themes with highlighting enabled and disabled.
- Highlighted selection/copy/export/search/digest and line numbers preserve the exact admitted source, whitespace, Unicode, and required line-ending identity. Diff highlighting retains added/removed/context semantics without relying on color.
- Grammar assets work offline and cannot be supplied by documents, skills, models, selected files, packages, CDNs, or renderer plugins. Highlighting never executes, compiles, formats, validates, invokes a language server, or sends code to a model.
- Exercise highlighter byte/line/token/nesting/time/memory limits, cancellation, rapid tab/revision changes, pathological grammar input, invalid encoding, binary input, and renderer exceptions. The UI remains responsive and exposes selectable plain-text/hex fallback without stale spans or source loss.
- Syntax themes meet applicable contrast targets; keyboard selection, focus, search matches, diagnostics, whitespace, punctuation, line numbers, and diff markers remain understandable without color. Screen readers receive original code and a useful language label rather than token-by-token noise.
- Detailed responses and textual skill files expose keyboard/screen-reader reachable Copy all. It copies the complete current immutable item or selected file, including non-visible/virtualized content, and never silently truncates or concatenates sibling skill files.
- In rendered Markdown/static HTML, Copy all atomically publishes complete semantic Unicode plain text plus sanitized HTML Clipboard Format for the same immutable revision. Supported structure and safe syntax styling survive rich paste without active content, hidden text, trusted chrome, or controls; admitted Markdown also exposes its exact source through the registered format.
- Source/code/diff copy always preserves admitted Unicode characters, tabs, line breaks, and required source identity without line numbers, search decorations, or soft wraps. An additional sanitized rich fragment may preserve syntax/diff styling but cannot alter the canonical plain source.
- Partial mouse/keyboard selection supports `Ctrl+C` and Copy selection across rendered blocks and virtualized source lines. `Ctrl+A` targets content rather than window chrome. Empty/stale/revoked/redacted selections cannot copy a previous range.
- Switching Rendered/Source makes the Copy all representation explicit; Copy source remains separately named when applicable. Successful feedback identifies the representation/revision without logging content.
- Clipboard writes always include Unicode plain text and publish every rich/source format as one logical update for the same revision. They revalidate item/file revision, access, unlocked Windows session, and privacy state, and report platform/size/format failure without changing the previous clipboard. Private-content disclosure explains clipboard history/sync/other-application exposure.
- Rich clipboard HTML is host-serialized from the admitted semantic model with valid fragment boundaries and allowlisted self-contained markup/style. Tests reject scripts, handlers, forms, frames, objects, hidden content, beacons, external/local resources, CSS URLs/imports, sensitive paths/IDs, privileged schemes, and paste-triggered network retrieval.
- Static HTML/browser content cannot call clipboard APIs, initiate copy, provide or mutate formats, read the previous clipboard, or observe write results. The host mediates bounded selection/semantic extraction, format serialization, and the atomic OS write.
- Copy selection/all does not approve, execute, enable, save, refresh inactivity, or authorize model/tool/skill clipboard access or later clipboard capture. Lock/revocation/deletion blocks new copy and invalidates current selection.
- Mermaid uses pinned offline assets; hostile directives, HTML labels, links, script/event/foreignObject SVG payloads and external resources are blocked.
- Exercise document/diagram byte bounds, 500-node/1,000-edge limits and the 2-second diagram budget at/over each boundary; controls remain responsive during cancelled/failed rendering.
- Static HTML blocks scripts, handlers, forms, frames, CSS network URLs, plugins, file/UNC references, arbitrary loopback access, and storage/host bridges.
- Static HTML cannot acquire script through local origin, a skill, a model result, an "enable scripts" preference, encoded CSS URLs, redirects, popovers/fullscreen/dialogs, or a renderer fallback; dynamic sites remain separately labelled browser content.
- Local asset snapshots cannot escape selected roots through traversal/reparse points; generated HTML never receives neighbouring-file access.
- Browser adapter tests cover redirects/subresources/service workers/popups/downloads/protocol handlers, permissions, profile isolation, background activity and renderer crashes, not just top-level URL filtering.
- Local-only/offline operation generates no renderer/browser remote traffic; unavailable enforcement disables embedded capability rather than pretending to be safe.
- External browser launch requires an explicit validated destination/target; no silent fallback or shell invocation, and no claim to manage its identity/network permissions.
- Missing renderer/runtime, invalid source and resource-limit failure produce labelled source/plain-text fallback without hidden downloads.
- Web/Markdown content cannot invoke a skill, approve an action, open the microphone, access credentials, or automatically become outbound model context.
- Sessions, Skills, and Permissions surfaces work with rich rendering unavailable. They reuse passive presenters only for read-only item/file/evidence content; native lifecycle, revision/file-set, save/enable, grant edit/revoke, validation, conflict, and confirmation controls retain authority.
- Multi-file skill review preserves the complete immutable revision, manifest-listed file set, digests, dependencies and unresolved references while tabs change; viewing/editing/testing/saving remains distinct from enablement and execution approval.
- Permission edits target stable grant identity/revision and reject or re-resolve stale concurrent use/revocation. Rendered explanations, links, rows, and checkboxes never submit or approve a grant.
- Admission/render activities correlate by safe host IDs/profile/generation and end with truthful status without logging content, titles, paths, sensitive URLs, or rendered text. Rendering diagnostics cannot masquerade as security audit.
- Close, replacement, revocation, lock, renderer crash, and shutdown cancel stale generations and dispose snapshots/browser resources; no hidden renderer retains file/network access.
- Every top-level `/docs/*.md` page is embedded in the application, `/docs/readme.md` is the required start page, and the same single-instance themed Documentation window opens from the tray or the exact built-in voice/typed documentation phrases without filesystem or network dependency.

## Human Interaction and Persistent Session Gate

Use the canonical [Interaction and Sessions](Interaction_And_Sessions.md) contract and disposable private-profile store/resource fixtures.

### Channel Parity and Evidence

- Complete new/select/question/detail/approval/Done/resume/delete/settings workflows entirely by voice, entirely by mouse/keyboard, and with input channels changed mid-workflow; UI never requires spoken acknowledgment.
- Single-choice, checkbox multi-choice, custom text, and constrained form cases preserve one shared draft; spoken select/deselect/list/review and UI edits submit the same typed answer.
- Validate required fields, min/max selection, custom-answer permission, cancelled/expired questions, duplicate submissions, stale revisions, and simultaneous voice/UI input; exactly one accepted decision/event.
- Two sessions can await answers with identical option labels. Background output, switching, generic "yes", and delayed callbacks cannot answer/approve the wrong question; explicit valid UI cards remain usable while only one voice prompt is foreground.
- The compact selected-session view shows its latest interaction and truthful state, retains Details/History links, and never loses a pending question to feedback auto-hide or a passive completion.
- Detail offers and Open details links survive ordinary compact auto-hide/history navigation without extending session inactivity. Explicit opening may focus the viewer; classification/background completion never does.
- Concise/full Markdown, static HTML, and `.ps1` source views retain immutable artifact/digest/provenance; expanding/copying/history navigation never executes scripts or reruns tools.
- Every finalized message, correction, question/answer, decision, reviewed artifact, action/receipt, grant transition, error, and cancellation/unknown outcome is retrievable in order, subject to explicitly reported redaction/source restrictions.
- A click is not represented as reauthentication, a voice match is not represented as guaranteed authentication, and either channel can deliberately approve a permitted high-risk proposal.
- Risk fixtures vary effects, targets, reversibility, environment, exposure, privileges, and actual constraints for identical script text. Unknown arbitrary code cannot be downgraded by model prose; prohibited actions remain prohibited.
- Changed script/dependency digest, parameters, resources, identity, destination, policy, or expiry invalidates approval. Mandatory OS/provider checks cannot be bypassed through either channel.

### Routing, Concurrency, and History Access

- Verify the [coordinated window design](UI_Workspace_And_Windows.md): compact latest interaction, Sessions workspace with list beside full conversation/history, separately bound detail/script review, and reachable settings/grant/setup/help surfaces.
- With two independent sessions progressing and another queued/waiting, switch/read history/edit an answer without stopping unrelated work or changing scheduling priority. Per-session drafts, streaming, questions, grants, queue and unread status never cross session identity.
- Compact/workspace share explicit UI selection; an open detail viewer stays bound to its original artifact. Back to conversation selects the exact event; expansion/closing never executes or approves a script.
- Exercise native single/multi-choice, custom replies and typed forms through voice-only, UI-only and mixed input; duplicate visible card hosts share one draft and exactly-once submission, and ordinary progress cannot replace a pending card.
- Search/filters, tail-follow versus historical scroll, meaningful-activity/due-date display, All work/resource blockers, Done/resume/delete and draft/restart recovery match original-requirement coverage; basic controls remain usable without model/network/audio.
- Background updates do not steal keyboard/voice focus. Explicit question presentation revalidates a unique voice target; resolution/expiry/ineligible presentation clears it rather than automatically targeting another session's card.
- Explicit composer/named-session targeting wins. One clear Active match is acknowledged; multiple plausible matches ask; unrelated or unavailable-inference general requests start a new session.
- Archived sessions are searchable/readable without resume or timer refresh; explicit resume updates activity but restores no executable grants/tasks.
- Run at least two independent session tasks simultaneously, including writes to different disposable resources. Verify isolation, budget limits, per-session ordering/cancellation, and fair progress with the foreground session idle.
- Race conflicting reads/writes and competing session mutations; leases prevent conflicting effects, base changes invalidate reviewed writes, and unknown resource effects serialize within an enforced domain or fail admission.
- Unresolved remote outcomes block dependent/conflicting dispatch, not unrelated work. App/power lifecycle preparation coordinates every session and shared TTS never interleaves spoken prompts.
- Session tools paginate/filter to exact IDs/revisions and bound output; full history remains traversable rather than silently truncated/replaced by summaries.
- Questions about decisions/scripts/actions across Active/Done sessions cite event/artifact evidence and distinguish observed receipts from plans/inference.
- Evidence mode and `evidence.list`/`evidence.get`/`evidence.search` correlate retained session, diagnostic and audit records by stable IDs without treating diagnostics as authorization/receipt proof; deterministic list/read/search works without a model.
- Ask Evidence answers questions over log-only, audit-only and mixed selections; every factual claim cites exact retained records, observed facts are separated from inference, conflicts/gaps/expired sources remain explicit, and follow-ups cannot silently broaden the source/time selection.
- With local inference unavailable, list/read/search still work and reasoning reports unavailable. With remote inference selected, preview/approve the exact bounded evidence payload; denial makes no remote call. Hostile log/audit text cannot become instructions, tool calls, approvals or authority, and Ask Evidence cannot mutate, retry or execute the evidenced operation.
- Historical prompts, scripts, grants, and malicious tool text never become current instructions or authority. Revoked source/account access is filtered/removed before retrieval.
- Local-only routing/search/history makes no remote call; remote-enabled queries review the actual selected history/summary payload, not a blanket "all sessions" upload.

### Persistence, Configurable Lifecycle, and Deletion

R04 partial source/tests are tracked in the
[implementation inventory](Implementation_Roadmap.md#r04-foundation-delivery).
They do not mark the following installed/native, durable-store, audit-chain,
integrated recovery, lifecycle or deletion criteria passed in full. The first
bounded durable request/task milestone now composes exact local version-query
intent, dispatch, required typed evidence and terminal state on private SQLite.
Its own roadmap receipt records the specific automated interruption boundaries;
it does not certify broader R04, installed, power-loss or OS-effect acceptance.
Synthetic/fake-store and safe key/artifact scratch tests are not installed
acceptance, and file copies cannot satisfy authoritative audit requirements.

- Accepted intent/approval is durable before consequential dispatch; crash at each commit/dispatch/receipt boundary preserves truthful interrupted/unknown evidence without replay.
- Restart restores Active/Done history/artifacts and selection; queued requests require explicit fresh dispatch/revalidation, and old grant records never become tokens.
- Simulated defaults stay Active just before 24 inactive hours, archive at 24 hours when safe, retain content just before 30 inactive days, and purge at 30 days when safe.
- The clock starts at last meaningful activity, not creation/Done; automatic archive, browsing/search, history questions, polling, and reminders do not reset it. Accepted substantive input/decisions, actual work progress, and explicit resume do.
- Configure both durations by voice and UI, including values different from defaults; reject non-positive/non-finite/reversed durations. Shortening previews affected due dates and never immediately purges without separate apply-now confirmation, including on the next timer tick; absent it, existing due dates remain until meaningful activity.
- Normal eligible automatic purge uses the disclosed retention policy without recurring per-session approval; explicit deletion still requires exact action-specific confirmation.
- Test offline startup/access after deadlines, clock rollback, time-zone/DST changes, concurrent resume-versus-expiry, blocked live work, and unresolved effects; no early deletion or silent abandonment.
- Exact session deletion removes messages/artifacts/snapshots/summaries/indexes/caches and recoverable journal/backup content under the proven store contract and rejects late appends; independent perpetual grant records survive, with applicability revalidated and minimal provenance retained separately.
- Deletion preserves unrelated sessions/saved skills and discloses independent content-minimising audit, user exports, provider copies, and lack of forensic-erasure guarantees.
- Inspect history/artifact/index storage for supplied LocalApplicationData placement and verified private permissions; verify raw audio/credentials/biometrics are absent, known secret fixtures are redacted and retained content never leaks into ordinary diagnostics. Credentials use Windows-protected storage, not database fields.
- Inspect diagnostic/audit indexes and managed temporary files for the same private-profile boundary; retention of one stream does not extend another. Disclose that database, journal, artifact and backup copies outside that boundary are readable.
- Use the [approved Windows storage strategy](Architecture.md#windows-durable-storage-direction): maintained standard SQLite with pinned provider/native closure and reviewed licences. Encrypted-codec selection, database keys and rekey tests are no longer storage prerequisites.
- Inspect every offered publish/installer architecture for the pinned provider and matching native engine. Installed runtime-only loading/read/write remains R17 evidence; RID publication alone is not execution or filesystem protection proof.
- Missing/corrupt packaged native assets fail explicitly without runtime replacement download, ambient-library fallback or replacement of existing data.
- Verify restrictive effective folder/file ACLs and profile-local placement of content, staging and managed backups. Test profile/permission failure without data replacement or silent permission repair. Profile-local paths alone do not prove correct integration.
- For profile-local storage, trust Windows cross-profile isolation rather than require a second-account OS-denial trial for routine application acceptance. Reassess before shared storage, service/impersonated identities, cross-profile migration or custom authorization; any resulting cross-user claim needs approved actual-account evidence, not mock SIDs.
- Exercise private WAL/rollback journals, backups and identity-bound artifacts; use memory-only SQLite temporary storage and prohibit SQL-content tracing. ACLs do not protect against same-user/admin access, copied files, paging or dumps.
- Interrupt artifact staging/publication/reference and backup-generation commits; verify recovered identity/digests, reconcile orphan/missing/corrupt files and preserve originals on failure. Reject unknown schemas and preserve legacy sources during any explicitly supported migration.
- Demonstrate cleanup or rewrite of managed recoverable copies after targeted deletion while unrelated records and independent grants survive. Include old managed backups; unlink is not forensic erasure or protection against valid-history rollback.
- Disk-full, profile/permission failure, corrupted storage, migration failure and artifact-size admission failures are explicit; no success-shaped fallback, silent history eviction or consequential dispatch without required durable evidence.

## Future Capability Gates

### Delivered Bounded In-Call Feedback Evidence (R10/R15)

Maintained deterministic tests exercise all Voice/UI/Both/Inherit choices,
unsaved UI/reset/restart provenance and complete task/queue/session/device
precedence for Active/Suspected versus Clear/Unavailable/Unknown. Unknown/invalid
evidence and corrupt/unconfirmed storage retain speech refusal/complete visual
recovery, independently of legacy suppression and feedback selection.

Application/native-source and current-user shared-SQLite composition tests cover
original voice refusal (including UI relabelling), fresh UI admission, exact
current-name discovery and grammar, call/configuration/session/input/native
lifetime revisions, stale callbacks, audit/readback/intent failure, cancellation,
pending previews and active-output retirement without replay or implicit capture,
consent, permission or grant changes. See
[the delivered contract](User_Configuration.md#delivered-bounded-device-local-in-call-feedback-r10r15).

This is automated source/native-seam evidence, not acceptance of automatic
detectors/source-age handling, native keyboard/screen-reader interaction,
real-call/acoustic leakage or measured native output-stop timing. Full R10/R15,
proactive configuration, temporary/speak-once/downgrade review, internal model
tool exposure and A0-A4 remain open; the future gates below still apply.

### Internal Model Tool Exposure

- Verify every current advertised action/proposal against [Internal Model Tools](Internal_Model_Tools.md), including all 20 registered actions, the four mutually exclusive response kinds, current approval rules, and disabled OS power execution.
- Each future registered tool has a stable ID/schema, supported caller lane, bounds, canonical effect/resources, lineage, authorization, cancellation, and truthful pending/unknown/unavailable behavior; unavailable or deferred tools are not advertised.
- Exercise voice/UI equivalence, stale revisions, session targeting, secret filtering, bounded pagination, local-only egress, and hostile retrieved/tool/rendered instructions.
- Models cannot submit user answers, confirm/grant themselves authority, forge receipts, access credentials/raw audio, or invoke the maintenance/updater channel.
- Consume a single-use grant exactly once; allow only the approved operation in the bound Active session under a session grant, end eligibility on Done/deletion, and never restore it by resume.
- Advance clocks beyond session/audit retention, archive/delete sessions, simulate grant-store pressure, and restart; perpetual grants remain without expiry/retention/eviction until explicit user edit/removal, while changed identity/content/invocation/policy blocks inapplicable use.
- Restart an Active persisted session; revalidate its session grant without replaying consumed authorizations or interrupted work.
- Verify the separate in-call feedback override defaults to UI-only, precedes ordinary task/queue/session/device output, remains configurable through voice/UI, and does not disable input or bypass mandatory speech/privacy rules.
- Verify `calls.ignoreReusableGrants` defaults On and is voice/UI configurable: manual Active, enabled Active/Suspected, and conservative enabled Unknown require fresh single-use approval for every grant-dependent operation, including background steps; no configured detector discloses unavailable protection.
- Session/Perpetual records remain unchanged while ignored; explicit voice/UI single-use approval authorizes only its exact invocation once. Call clearance restores only still-applicable reuse, never answers a pending prompt or replays work; grant-free safety/status controls remain available.
- Race call entry, new call-policy generations, setting changes, approval, and dispatch; pre-call/obsolete approvals cannot authorize a protected-call dispatch. Already dispatched effects retain truthful observed outcomes.
- Feedback/speak-once/speech overrides do not disable grant-ignore. Turning it Off requires exact trusted confirmation and preserves all other authorization gates; models/tool text cannot perform the downgrade.
- During manual Active or enabled Active/Suspected/Unknown call evidence, reject voice-originated voice-setting and all in-call-related mutations, including detector/Unknown policy changes, manual clearance, grant-ignore, scoped feedback, reset/undo, and temporary/speak-once exceptions.
- Carry trusted origin through direct commands, model interpretation, management/task hops, and tool proposals; absent origin cannot act as UI. A later UI confirmation cannot authorize a rejected voice request; require new UI initiation.
- Race call entry with request/apply; pending voice mutations are invalidated and never queued for application after call clearance. Verify fresh UI changes remain ordinarily validated/confirmed, unrelated settings/read-only inspection stay available, and stop/mute/cancel remain immediate safety controls without implicit unmute or protection downgrade.

### Additional Capability Evidence

Required initial-release evidence, not optional post-MVP enhancements:

| Capability | Required checkpoint evidence |
|---|---|
| Ollama/inference adapter | A2: [local-inference qualification](#local-inference-evidence), then shared-loop conformance, independently network-blocked offline clipboard answering, tool/result mediation, context/egress limits, quality/cancellation and integrated reference-hardware proof; R02 metadata/harness/unavailable observations alone do not pass |
| Fixed bundled execution | A3: complete embedded multi-script identity/review, common direct/model/UI/skill grant gate, truthful cancellation/lock receipts, and actual OS filesystem/network/child-process/credential/protected-Kora-resource containment |
| Concurrent independent session tasks | A3: two-slot baseline on reference hardware, one task per session, resource conflict scheduling, isolated task/approval/provider contexts, per-task cancellation, fair budgets and truthful grounded/aggregated status |

Before adding capabilities outside the initial release:

| Capability | Additional required evidence |
|---|---|
| File/folder ingestion and knowledge indexing | Complete the staged [R26 file/folder acceptance contract](File_And_Folder_Ingestion.md#acceptance-criteria): reviewed canonical roots, bounded immutable snapshots, Windows traversal/reparse/access/change handling, hostile-content instruction separation, source/session isolation, exact citations, local-only no-egress, hosted payload approval, refresh/revocation and inventoried deletion/rebuild |
| General applications/user-provided executable skills | Deferred R27: resolve standalone application rollback/applicability before admission; complete dependency discovery/immutable snapshots, content-bound applicability/revocation, and adversarial filesystem, network, child-process, credential, and protected Kora-resource access tests against actual OS containment |
| Screen/image context | Explicit capture, region/source provenance, secret handling, no ambient collection |
| Kora MCP server | Authenticated clients, per-client scopes, no unattended reuse of interactive grants |
| Extension updates | Digest/version changes invalidate affected grants and policy mappings |
| Intra-session parallel or multi-agent execution | Separate scope approval, isolated subtask/approval/provider contexts, resource conflict scheduling, cancellation/dependency gates, fair budgets and truthful aggregated status beyond the initial one-task-per-session rule |

No roadmap feature inherits release approval solely because it uses an existing extension interface.
