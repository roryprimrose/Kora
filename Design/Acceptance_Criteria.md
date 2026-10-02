# Acceptance Criteria

Status: proposed release gates. Targets are not claims of measured performance.

Related: [MVP Scope](MVP_Scope.md), [Architecture](Architecture.md), [Extensibility](Extensibility.md), [Security and Data Flows](Security_Data_Flows.md), [Task Lifecycle](Task_Lifecycle.md).

## Test Environment and Evidence

Before implementation is accepted, record an exact reference machine:

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

## Platform Boundary Gate

- Shared domain/policy/task/skill/configuration code builds and runs portable tests on Linux without Windows-only API references.
- Real native dependencies/handles/path conventions are confined to Windows integrations and platform composition, not scattered through shared logic.
- Contract tests cover device identity/cancellation, locked/disconnected/unknown denial, credential opacity, protected execution, and maintenance approval semantics.
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

## Gate 1: Voice and Clipboard Vertical Slice

Report Gate 1 by Slice A checkpoint:

- A0 proves the deterministic PTT/clipboard/remote path without claiming wake-word or local-first completion.
- A1 adds and proves wake activation, playback rejection, interruption, and session policy.
- A2 adds and proves the supported Ollama-backed local answer path with no remote network access.
- A3 adds bounded work management, configuration, and protected lifecycle/computer controls.
- A4 adds proactive, call-aware, owner-aware privacy, maintenance dialogue, and bounded presentation.

Later-checkpoint testing repeats applicable earlier gates.
No A0/A1 demonstration or release note may claim the completed voice-first/local-first Slice A outcome.

### Functional

- A user completes the clipboard explanation workflow by saying "Kora, explain the clipboard", without typing, clicking, or pressing push-to-talk after setup consent.
- Both immediate commands ("Kora, explain...") and wake-then-command with a pause preserve the command's first words.
- Optional push-to-talk uses the same command pipeline without requiring "Kora".
- Empty activation returns to Wake Listening after 5 seconds without a model call, clipboard read, or other tool invocation.
- Trailing-silence endpointing and the 60-second limit produce explicit, bounded capture transitions.
- A snapshot is captured only after an explicit request.
- Changes to the OS clipboard after capture do not change task input.
- Empty, locked, unsupported, and oversized clipboard cases are distinguished.
- Visual answer streaming and a spoken summary use the same task/result identity.
- Spoken output is at most 3 sentences and 80 words.
- Correction, follow-up, "stop", "stop speaking", and "cancel task" match lifecycle semantics.
- TTS audio is not ingested as a new user request.
- "Kora, stop" and "Kora, stop speaking" work during TTS; playback mentioning "Kora" and activation cues never self-trigger.
- A second task request does not start concurrent execution.
- Unsupported playback echo rejection disables TTS explicitly, not wake activation; the reference setup must support spoken responses and verbal interruption.
- A new snapshot/destination triggers a new policy assessment.
- The A2 local-only workflow produces the clipboard answer through the pinned Ollama adapter/model with remote network access blocked; missing/unhealthy local inference reports unavailable and never falls back to Copilot.
- "Kora, lock the machine" invokes the original bundled skill/script without a model/network call, including while another task is busy.
- The script's application-assembly/resource identity, version, digest, and fixed parameters are verified; tampering or a same-name user package cannot substitute its implementation.
- Confirm lock by the actual Windows session event; API acceptance/script exit without that event is not reported as confirmed success.

### Privacy and Policy

- Local-only mode performs no task-time network calls under network-blocked testing.
- With no local model, answering is unavailable with an explicit explanation.
- Remote-enabled mode shows the actual destination and context before transmission.
- Rejecting transmission sends none of the rejected context.
- Secret-risk fixtures block transmission pending reviewed redaction.
- Raw audio, clipboard text, tool content, and answers are absent from default logs and SQLite.
- Before activation, synthetic ambient audio reaches neither transcription nor any model, tool, persisted store, or network destination.
- Wake pre-roll never exceeds 2 seconds and is overwritten; unrelated pre-activation audio is excluded from command transcription.
- Mute/lock/sign-out/suspend close capture and clear buffers; restart/unlock/resume do not silently reopen capture.
- Wake Listening, Capturing Command, Muted, Session Locked, and Unavailable are distinguishable, including background-app status.
- Closing/clearing the conversation and the 30-minute idle expiry clear ephemeral context references.
- Restart does not restore default ephemeral history.

### Mouse-Based Questions and Device Recovery

- With no model/network/microphone/TTS and optional captions disabled, first launch asks which detected microphone to use and accepts a complete mouse-only response/consent path.
- Zero/one/multiple devices, duplicate names, Windows privacy denial, disabled/missing endpoints, muted input, and missing recogniser each have distinct actionable states.
- Enumerating/selecting devices records no audio; test/enable opens only the explicitly selected endpoint after consent and an authoritative unlocked-session check.
- A microphone test lasts at most 5 seconds, stores/transmits no audio, performs no transcription/model call, and cannot grant ongoing listening.
- Input selection is endpoint-ID/topology-revision bound; stale choices never select another same-name/default endpoint.
- Removal/capture failure invalidates audio generations, clears incomplete input, and offers mouse recovery without cancelling unrelated task/queue state.
- Plug-in/reconnection/system-default changes do not switch/open capture automatically; explicit user enablement and playback-rejection revalidation are required.
- Device-open timeout/cancel/lock does not block status/queue UI; late success is closed and cannot enable listening.
- Generic open failure is not falsely diagnosed as busy; privacy help opens only the registered Windows settings destination with no self-elevation.
- Hidden presence/voice-only mode exposes questions/recovery from tray controls without requiring voice or stealing focus.
- Native tray context menu lists detected endpoints, saved selection and actual availability distinctly; selection changes do not start recording, and explicit Enable listening uses normal consent/readiness checks.
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
- Voice/PTT/shortcut/skill/runtime requests cannot reopen capture while locked; unlock requires explicit user re-enabling.
- The lock script cannot access protected Kora resources, arbitrary commands, remote endpoints, credentials, or elevation under its actual execution profile.
- Missing containment or session-control support is an explicit failed gate, not permission to run an unrestricted fallback.
- Denied, failed, and unconfirmed lock attempts have truthful receipts and no automatic uncertain retry.
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
- Verify command-prefix preservation for immediate wake-and-command utterances, and no duplicate task from one activation.
- False activations must never bypass context consent, remote egress review, or action approvals.
- Measure wake-listening-only overhead for 30 minutes: mean CPU <= 5% of total reference-machine capacity and incremental working set <= 200 MiB over muted idle.

Apply the same measurements to every advertised custom profile and combined active-name set, as defined in [Custom Activation Names](Activation_Name.md).
Test custom-only rejects removed/default activation, dual-name accepts both without duplicate tasks, and another rename retires the prior custom alias.
Rename always asks custom-only/both, validates spelling/pronunciation and readiness, and confirms the exact resulting active set.
Failed preparation/calibration/persistence or stale prompt/config revision leaves the old set active; late old-generation callbacks cannot start commands after cutover.
Renaming while muted/locked never opens capture; restart retains the name preference without restoring listening consent.
Missing/corrupt committed custom-only profile reports Unavailable with tray recovery, not silent default-name or cloud activation.
Rename prompts/TTS mentioning every active or proposed name produce zero self-activations; names never enrol a speaker or authorise a tool/action.
Custom profile preparation uses only protected data-only setup, with bounded local calibration and no Kora code/resource mutation.
First-run mouse setup can select a validated custom-only profile before ongoing listening; dual-name mode is explicit and explains office cross-activation.
Test neighbouring instances with distinct names, similar-sounding names, background/default-name speech, and crosstalk; custom-only must not retain "Kora" as a hidden recovery alias.

These are proposed release targets, not measured detector capabilities.
Wake activation is mandatory for Slice A: shipping PTT alone does not pass this gate.
If the detector fails, change/tune the local implementation or explicitly revisit supported hardware; do not defer activation or switch to cloud listening.

### Shared-Space and Optional Speaker-Verification Gate

- With speaker verification absent, disabled, unhealthy, stale, or uncertain, general voice interaction and safety-preserving stop/mute/cancel/lock controls remain available, while private clipboard/task/account/message/queue content defaults to a neutral visual notice rather than spoken disclosure.
- `LikelyOwner` permits private speech only when normal content/output policy also permits it; it never satisfies remote-egress, tool, update, setup, power, account, credential, or security-setting approval.
- `NotOwner`, `Uncertain`, and `Unavailable` never disclose whether a sensitive resource exists through spoken wording.
- Shutdown/restart, speaker enrollment changes, reduced privacy policy, and other designated high-risk operations require their native secure confirmation outside the speech/model path regardless of match result.
- Enrollment/replacement requires Windows Hello or equivalent native reauthentication, multiple randomized prompted phrases, explicit consent, and a native non-voice completion path; ordinary command audio never enrolls or updates a template.
- Verify that raw enrollment audio is discarded after derivation and that templates, scores, phrases, and biometric diagnostics are absent from roaming storage, model/tool/skill context, logs, telemetry, crash reports, and default backups.
- Bind the protected template to the Windows SID and device; changed SID/device binding, stale enrollment, unsupported microphone transition, missing assets, verifier error, and policy denial yield `Unavailable`, never owner.
- Test genuine-owner false rejection across time, quiet/office noise, supported microphones, illness/voice variation fixtures, and playback conditions.
- Test false acceptance using at least unrelated speakers, similar voices, household/nearby-speaker fixtures, recordings, Kora TTS, speaker playback, virtual/loopback devices, and representative synthetic/cloned speech.
- Record false-accept and false-reject rates, thresholds, verifier/anti-spoof model versions, supported hardware, and residual limitations; failing the approved risk target leaves verification unavailable rather than weakening policy.
- Repeated mismatches and suspected replay/synthesis are rate-limited and auditable without retaining biometric audio.
- Delete/re-enroll removes the prior protected template and invalidates cached confidence; no voice-only workflow can enroll, replace, delete, or relax the privacy policy.

### Work-Management Gate

- While an execution runtime/tool is deliberately blocked for 60 seconds, management accepts new requests, resolves clear contextual queue operations, and asks on ambiguous targets without waiting for that execution call.
- Actual pinned runtime sessions prove independent management progress and no cross-session context, tools, or approvals.
- The pinned provider/account tier and SDK/API terms permit the tested independent sessions; record version, tier, rate limits, estimated maximum cost, and any unsupported deployment class.
- Enforce one in-flight management request, 32 KiB input, 4 KiB output, 15-second deadline without automatic retry, and 30 remote calls per rolling hour per profile.
- Timeout, sign-out, offline, throttle, quota/cost cap, malformed proposal, and hourly-cap fixtures enter deterministic degraded mode without blocking cancellation, factual status, direct queue controls, or task execution.
- Local cancellation and basic status still work when management inference is blocked, offline, or timed out.
- At most one task owns the execution slot across admission, dispatch, cancellation, and completion races.
- The fixed priority session-lock control can run without waiting for the task queue; it does not admit another general executor or grant script access to management inference.
- A scripted dialogue adds two tasks, reorders one, removes one, and replaces the active task; acknowledge every change with the correct task identity.
- Status questions report the observed stage, blocker, known remaining steps, and queue. Missing step/ETA evidence is stated as unknown.
- A planned step cannot be reported complete without supporting events/receipts.
- Full queues do not silently drop work; 30-minute expiry releases pending content and notifies the user.
- Cancellation preserves pending entries and pauses dispatch; "stop all work" clears them.
- Failed/unknown prerequisites and unresolved remote effects prevent automatic dependent/replacement dispatch.
- Approval replies cannot apply to queue clarification or another task, including when status questions interrupt an approval.
- Clipboard references remain bound to the admission snapshot; expired/missing context requires a fresh selection.
- Remote management receives only reviewed context; local-only mode makes no remote management call.
- Conversation clearing/restart does not restore pending request bodies or content-bearing ledger records.

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
- Tool output containing hostile instructions cannot grant permissions or trigger unapproved actions.
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

- A user creates, refines, tests with mocks, saves, enables, and invokes a skill using voice, without editing code or typing.
- Spoken capability summaries and the displayed diff identify the same proposed revision.
- The proposal phase creates no installed-skill writes; staged proposals remain data only.
- Save approval binds exact package files, base hashes, and new content; later refinement invalidates approval.
- A changed base, traversal path, hard-link alias, or disallowed reparse-point target prevents application.
- Denial, expiry, and cancellation prevent pending writes.
- The skill validator rejects invalid manifests, executable directives, scripts, assemblies, hooks, arbitrary paths, and custom validators without launching code.
- Simulated examples cause zero real tool invocations or side effects; model-based examples retain egress controls.
- A real trial is a separate task with normal tool/identity/resource/action approvals.
- Missing tool dependencies are reported; Kora does not install or rewrite their implementation.
- Validation failure does not save/enable the proposal.
- Partial write and worker crash fixtures preserve evidence and never overwrite unrelated edits during recovery.
- Changed skill content is not activated before policy/dependency checks and explicit enablement.
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

- Inspect source-build and precompiled publish artifacts: every built-in manifest/instruction/fixture/script is an embedded application resource, with no loose built-in skill files or writable execution extraction.
- Test resource catalogue identity/digest validation and missing/corrupt resource failures; reject rather than searching profiles, PATH, other assemblies, or caches for a substitute.
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

Any successful protected-resource mutation fails the release gate; a prompt warning or log entry is not a substitute for enforcement.

## Cross-Cutting Failure Tests

Exercise timeout, lost connection, runtime crash, microphone removal, credential expiry, approval expiry, oversized input, and app restart in every relevant state.

Verify:

- No write retries after unknown outcomes.
- At most one eligible read-only transient retry.
- Late answer events cannot overwrite cancelled/failed terminal states.
- Unknown remote side effects remain labelled unknown until reconciled.
- Restart does not resume tools or reuse approvals.
- Executable trust disclosures match actual OS rights.
- Disabling an extension blocks new invocations.
- Default audit metadata expires after 30 days.

## Distribution and Startup Gate

- The source bootstrap succeeds from a clean reference environment with declared build prerequisites and a pinned revision.
- Missing prerequisites, authentication failure, restore/build failure, and interrupted installation yield explicit actionable errors; no unrelated checkout is changed.
- Bootstrap rerun does not duplicate installation/startup registration or overwrite local edits.
- The CI-produced framework-dependent artifact runs on a clean runtime-only machine with no Git, .NET SDK, build tooling, or source checkout.
- Launch setup successfully with no speech models, database files, or Ollama present; initialise selected capabilities from the running app without Git/SDK.
- Validate launch-critical packaged libraries and embedded lock-script resources, plus speech readiness after Kora-led explicit asset setup.
- Missing/incompatible runtime and architecture cases identify the actual requirement; no SDK install or source-build fallback occurs.
- Source and binary deployments preserve the same skill/data partitions and application-integrity guarantees.
- Start-at-logon is opt-in, runs published binaries as the interactive user, starts only one instance, and never builds or elevates.
- Logon/restart/locked-session tests preserve the existing explicit microphone re-enabling rules.
- Uninstall removes startup entries and offers data retention without deleting shared profile skills.
- Installation metadata, not `.git` presence, determines maintenance mode; developer checkouts are not automatically pulled/reset.
- A staged failed update leaves the current deployment runnable; activation waits for quiescence and respects settings/schema compatibility.
- Binary update checks use the configured hosted release feed without Git/SDK, respect channel/architecture/runtime, and exclude drafts/prereleases on stable.
- Failed/offline checks are not reported as current; repeated checks respect cadence/backoff and maintenance network policy.
- Eligible release metadata produces an unprompted verbal suggestion when voice delivery is permitted; rejection/deferral installs nothing.
- Downloads/staging require explicit per-release acceptance; wrong origins, tampered artifacts, changed digests, or expired approval prevent activation.
- Voice acceptance is bound to the exact foreground maintenance proposal; task/skill prompts and model-written text cannot authorise it.
- Update activation waits for task/worker quiescence, does not cancel user work automatically, and cannot begin while locked/disconnected.
- Agent tools cannot invoke maintenance, change update origins, choose executable payloads, or fabricate release-availability events.
- Package-level auto-update settings cannot bypass per-release approval.

Installer/package/updater technology selection requires additional integration evidence before activation is enabled.

### Public GitHub and Linux Release Gate

- Build/package/publish the unsigned Windows reference release on clean Linux Actions runners without required Windows build jobs or hidden Windows-only tools.
- Verify Windows RID/native dependency completeness, runtime-only packaging, embedded resources, licences, and final-byte hashes/provenance.
- Application, maintenance, and setup artifacts contain no Authenticode signature during the initial unsigned phase; release/setup surfaces disclose this and document the expected Unknown Publisher/SmartScreen behavior.
- Published SHA-256 values match the downloadable final bytes, and the UI/documentation never presents those hashes or build attestations as Windows publisher authentication.
- Fork/PR checks cannot access release-write permissions or production identity; approved release workflows use pinned actions and protected revisions/environments.
- Public binary acquisition/update metadata needs no user GitHub token, Git, or SDK; rate-limit/cache/offline/feed failures remain truthful.
- Official release lookup accepts only the configured canonical repository, approved release record, immutable source revision, expected artifact identity, and matching digest; it cannot substitute fork artifacts, generic workflow outputs, default-branch commits, or mutable tag names.
- A single setup EXE contains the intended payload; any companion updater metadata/packages refer to the same exact approved release identity.
- External Windows integration evidence binds the final artifact digest and covers runtime/native assets, install/UAC/ACLs, unprivileged launch, device/tray/lock, and protected update/recovery.
- Missing mandatory Windows evidence leaves the candidate draft/unreleased despite successful Linux builds.
- NSIS/Velopack adoption passes Linux packaging and actual protected Windows update tests; a per-user writable install does not pass the no-self-modification gate by itself.
- MSIX/App Installer is not an initial unsigned-package option.

## Environment Setup Gate

- Source and binary delivery provision only Kora/build prerequisites, not user databases, model downloads, provider accounts, or Ollama.
- First launch resolves known folders, creates its expected stores, and initialises SQLite without an external database installer.
- Rerun reuses valid data/schema; migration failure does not delete user data or silently create an alternative database.
- The setup UI works with no model/provider/speech recogniser configured and explains the initial visual setup exception.
- Capability probes distinguish absence, incompatibility, failed health checks, and blocked setup; installer exit code alone cannot mark Ready.
- Selecting a remote-only provider does not install Ollama; selecting a delivered local adapter offers only its required runtime/model steps.
- A pre-existing healthy Ollama installation/model is reused without changed ownership, settings, startup, deletion, or unrelated process termination.
- Approved Ollama setup verifies endpoint/version and real model inference; insufficient disk/hardware, network failure, or rejection leaves an explicit readiness state.
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
- Computer shutdown/restart requires a second distinct named confirmation bound to the current foreground proposal plus native secure confirmation outside the speech/model path; generic "yes", speaker matching alone, stale/wrong-action confirmation, and TTS playback cannot satisfy it.
- Confirmation prompts expire after 30 seconds; accepted power grants are single-use with a 2-minute expiry and no silent extension.
- Power countdown is host-owned, visible, and cancellable; the final simulated OS call has no forced-close/remote-target flags.
- A blocker/failure never triggers forced termination, an unrestricted shell fallback, or a false completion receipt.
- Active work is explicitly resolved before power dispatch; queue holds, app restart, power actions, and update activation cannot race.
- Cancelling an owned power proposal restores prior dispatch policy unless another blocker requires pause; an unrelated Windows operation is not cancelled.
- Hide preserves work and listening visibility; exit/restart releases audio/owned workers, does not stop user-owned services, and requires affected-work confirmation.
- App restart and computer restart never substitute for one another; restart does not replay queue entries or microphone consent.
- Pause queue leaves active execution untouched; clear queue, cancel current, and stop all have distinct tested effects.
- Check-for-updates installs nothing; update approval is exact-release maintenance and cannot become a Git/shell action.
- Call-gated confirmation uses visible presentation or an explicit permitted readback; suppression is not implicit approval.
- Reserved control registration cannot be shadowed by a shared skill name, user copy, or manifest priority declaration.

Use a non-destructive OS-action test double for shutdown/restart routing and cancellation tests.
Validate the production invocation's flags and actual restricted worker admission separately; do not reboot a shared development/test machine to validate phrase recognition.

## Verbal Configuration Gate

- Every available option, including extension options, registers a type, bounds/choices, default, scope, aliases, validation, dependency, application timing, and confirmation rule.
- Enumerate the registry and exercise discovery/get/set/reset for every option through voice and UI against the same effective configuration; no UI-only or manual-file-only preferences.
- Exact basic settings commands work without a model/network after local speech is ready.
- Spoken absolute/relative values, units, source/device labels, and scopes resolve to concrete proposals; ambiguity or invalid values never mutate state.
- Validate exact boundary and just-outside-boundary values for every numeric range; reject rather than silently clamp.
- Changes disabling speech/call permission are acknowledged visually without speaking through the resulting policy.
- Persisted preferences survive restart; listening consent, grants that expired, and temporary call/task overrides do not become active merely because settings were saved.
- Simulated persistence failure and concurrent voice/UI changes retain a valid configuration and report failure/conflict without silent lost updates.
- Lowering queue capacity does not evict entries; shortened request lifetime identifies affected entries before immediate expiry.
- Default deadline changes do not silently alter active tasks; provider/account changes do not silently transfer their context.
- Increased remote exposure receives exact named setting confirmation and still requires normal payload-specific egress approval.
- Reset/undo follow the same sensitivity and validation rules as set; neither restores grants nor repeats external side effects.
- Enabled extension option schemas cannot expose arbitrary code/config paths, protected trust roots, disabled lock policy, or security bypasses.
- Missing devices/assets/providers explain unavailable effective settings and do not trigger silent download/cloud fallback.
- Verify quiet-hour boundaries, weekdays, time-zone changes, overnight intervals, and daylight-saving transitions; do not infer a permanent UTC offset.
- Closed microphone/locked session/missing recogniser tests offer the explicit activation/setup alternative and never open a hidden listener.
- Voice-triggered sign-in uses the secure account flow; passwords/tokens are never dictated into configuration or model context.

## Information Display Gate

- Speech-text off/sentence/utterance preferences work verbally and through UI; hiding captions leaves required approvals/errors available.
- Captions match final TTS segments and playback generation, not full answers or queued/suppressed text; test interruption, failure, replacement, and call gating.
- Missing word alignment never claims word-accurate highlighting; pinning labels previous speech and does not extend conversation retention.
- Passive captions/details do not steal focus or intercept desktop input; verify contrast, keyboard/screen-reader access, reduced motion, multiple displays, DPI and working-area changes.
- Lock/disconnect clears sensitive host surfaces and closes/suspends owned browsing; external browser behaviour is explicitly outside host control.
- Typed content updates preserve task/provenance/revision identity; late renders and content styling cannot overwrite native approval panels.
- Markdown tables/code/task lists render without execution; raw HTML, unsafe schemes, remote images and incomplete streamed fences remain inert.
- Mermaid uses pinned offline assets; hostile directives, HTML labels, links, script/event/foreignObject SVG payloads and external resources are blocked.
- Exercise document/diagram byte bounds, 500-node/1,000-edge limits and the 2-second diagram budget at/over each boundary; controls remain responsive during cancelled/failed rendering.
- Static HTML blocks scripts, handlers, forms, frames, CSS network URLs, plugins, file/UNC references, arbitrary loopback access, and storage/host bridges.
- Local asset snapshots cannot escape selected roots through traversal/reparse points; generated HTML never receives neighbouring-file access.
- Browser adapter tests cover redirects/subresources/service workers/popups/downloads/protocol handlers, permissions, profile isolation, background activity and renderer crashes, not just top-level URL filtering.
- Local-only/offline operation generates no renderer/browser remote traffic; unavailable enforcement disables embedded capability rather than pretending to be safe.
- External browser launch requires an explicit validated destination/target; no silent fallback or shell invocation, and no claim to manage its identity/network permissions.
- Missing renderer/runtime, invalid source and resource-limit failure produce labelled source/plain-text fallback without hidden downloads.
- Web/Markdown content cannot invoke a skill, approve an action, open the microphone, access credentials, or automatically become outbound model context.

## Future Capability Gates

Before adding capabilities outside the MVP:

| Capability | Additional required evidence |
|---|---|
| Ollama/inference adapter | Shared-loop conformance, offline answering, tool mediation, context limits |
| Knowledge indexing | Access revocation, deletion, freshness, identity partitioning, citation correctness, reindex behaviour |
| Restricted execution | Adversarial filesystem, network, child-process, credential, and protected Kora-resource access tests against actual OS containment |
| Screen/image context | Explicit capture, region/source provenance, secret handling, no ambient collection |
| Kora MCP server | Authenticated clients, per-client scopes, no unattended reuse of interactive grants |
| Extension updates | Digest/version changes invalidate affected grants and policy mappings |
| Multiple task executors | Resource conflict scheduling, isolated task/approval contexts, per-task cancellation, dependency gates, fair provider budgets, and truthful aggregated status |

No roadmap feature inherits release approval solely because it uses an existing extension interface.
