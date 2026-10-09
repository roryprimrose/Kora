# Tools and built-in skills: current and planned

This guide covers all tool families and built-in skills specified by the
current Kora design. **It is not a list of features already released.**
Each section explains what exists now and what is planned.

- **Current** means the stated behavior is available through an exact built-in
  command or a control in the app.
- **Partial** means a narrower version exists; read the stated limitation.
- **Planned** means the described behavior is not available in this release.

**No script-backed built-in skill executor ships in the current release.**
Bundled skill instructions can now be selected with typed slash commands or
activated voice and applied to a local-model request; their embedded scripts
remain inspection-only. The model also does not yet have the full tool/result
conversation described below. Today, exact commands run C# handlers; other
requests can receive a local answer, a clarification question, or a named
action/grant-change proposal.
See [exact commands](commands.md) for phrases you can use now and
[Settings](settings.md) for controls you can change now.

The provider-neutral host foundation now has typed one-provider turn identity,
bounded context/evidence, host admission, audited outcomes and cancellation/
late-response controls. Its production adapters remain unavailable: it adds
no model-facing tool, hosted destination, provider qualification, memory write
or enabled Local only/Local first/Hosted preferred mode. Existing bootstrap
behavior is unchanged. Provider confidence and supplied provenance never
authorize an action or handoff.

## Asking Kora to do something

Use your configured assistant name for voice requests, for example
**"Kora, what are you currently working on?"** You can enter requests in the
typed command box instead. If you renamed Kora, use the new name; the old
name is not kept as a hidden alias.

You do not need to know a tool ID, skill manifest, or PowerShell filename.
For explicit selection, use `/lock`, `/restart`, `/shutdown`, the
kind-qualified form `/skill <name>`, or activated voice such as
**"Kora, run lock."** See [artifact commands](commands.md#run-skills-and-future-artifacts).
Typing `/` opens a filtered dropdown composed from the same source-qualified
catalogue used by routing. It includes bundled artifacts plus compatible
`SKILL.md`, `.prompt.md`, and `.instructions.md` files from Kora-owned roaming
folders and the existing VS Code prompt/instruction locations. Shared profile
skills are **not** startup artifacts or model context: register/list/inspect
them only through **Skill packages > Shared profile sources (read only)**.
See [the read-only native workflow](commands.md#inspect-shared-profile-skills-locally).
Disk definitions
remain untrusted instruction content and do not inherit bundled execution trust.
In the planned interaction:

1. Kora receives your typed request or transcribes your activated voice request.
2. Exact local commands can run without inference. Other wording is interpreted
   by the model using descriptions of available tools and enabled skills.
3. The model asks Kora to query state, select a skill, or propose a registered task.
4. Kora checks the target, inputs, dependencies and permission. If the request
   is ambiguous, it asks a clarification question.
5. If approval is required, Kora shows its own approval card for the exact
   effect. Choosing a clarification answer is not permission to execute it.
   Ordinary confirmations support deliberate voice or UI equally; mandatory
   Windows/provider authentication remains a separate trusted flow.
6. Kora executes the allowed operation and records what actually happened.
7. Approved result data can return to the model, which explains it in an answer;
   Kora presents the response under your speech and privacy settings.

If the model is unavailable or busy, exact local controls remain available.
Other wording may be unavailable rather than guessed. A read-only query can
still contain private information and may need approval before remote processing.
In the updated design, work belongs to named durable Kora sessions.
Explicitly naming a session or typing in its composer targets it; otherwise
Kora continues only a clear Active-session match or starts a new session.
Selecting a window never grants permission or silently redirects a voice request.

## 1. Application and help

These exact commands are **Current**. Optional category/page selection and the
full tool/skill discovery catalogue are **Planned**.

**Current bounded host registry:** the six canonical IDs `capabilities.list`,
`capabilities.get`, `application.get_version`, `readiness.get`, `runtime.list`,
and `runtime.get_status` have direct read-only handlers and exact local commands.
See [read-only host discovery](commands.md#read-only-host-discovery). Discovery
contains only those admitted descriptors, not all planned tools/skills.
Readiness and local-runtime health come from recorded, timestamped observations;
no probe or inference runs for these queries. Unknown/unavailable state is
explicit, and deployment facts not supplied by the version provider remain unknown.
This does not add tool definitions to the current local JSON selector or qualify
a management/execution runtime. Management may receive only this minimal
read-only subset when a future adapter is independently admitted.

| Ask or choose | What happens |
|---|---|
| "Show Kora" | Show the existing instance, not a duplicate |
| "Hide Kora" | Hide interaction windows; listening and work are not disabled |
| "Exit Kora" | Exit and release the microphone/owned work |
| "Restart Kora" | Restart this application, not Windows |
| "Open settings" | Open the single Settings window; change nothing merely by opening |
| "Open documentation" | Open the embedded user guide |
| "Open setup" | Open readiness/setup choices; install nothing without separate consent |
| "What can you do?" | Currently list supported exact commands; planned discovery also lists available tools, enabled skills, sources and limitations |
| "What version are you running?" | Currently report version/local readiness; fuller deployment/provider details are planned |

The planned exit/restart flow also explains affected pending work and asks for
confirmation where required. Model-suggested disruptive actions already require
an action-specific approval unless granted.
Different-build takeover/return is a separate native host workflow, not a skill
or permission for a model to start another assistant.

## 2. Session, activity, progress, and readiness

Status tools query facts; they do not approve a pending action, change the
session, or run a script. Kora presents those facts separately.

| Ask or choose | Availability and expected response |
|---|---|
| "What is the state of the current session?" | **Planned** full query: listening, activity, current work and pending approval state |
| "What are you currently working on?" | **Partial:** currently setup/activity; planned general task, stage and blocker |
| "What do you have left to do?" | **Partial:** currently setup items; planned known remaining steps and pending requests |
| "What is the current task status?" | **Partial:** currently setup stage and measured download percentage; planned general task progress |
| "Show the queue" / "How many requests are queued?" | **Planned:** pending request order, state, count and capacity; the existing setup list is not that queue |
| "What power action is pending?" | **Partial:** current non-executing proposal; future confirmation/countdown state is planned |
| "What do you need to finish setup?" | **Partial:** current Readiness/status UI; the complete dependency catalogue is planned |
| **Refresh readiness** | **Current** UI: check devices/dependencies again without enabling capture or installing software |

Use the exact current phrases in [Commands](commands.md) when a planned phrase
does not match. Questions such as **"Am I waiting on anything?"** are intended
to use the same observed information once the full tool interaction exists.
Kora should say when progress, remaining steps or an estimated finish time are
unknown, not invent a percentage or treat an old snapshot as current.
Private task or approval details may be withheld or shown visually.

## 3. Work, queue, conversation, and result navigation

The full request queue and management operations below are **Planned**.
Current **cancel task** cancels setup/local reasoning or a pending power
proposal; it is not the complete designed work queue.

| Ask | Planned behavior |
|---|---|
| "Queue this request" / "Do this after the current task" | Admit a separate request with its explicitly selected context |
| "Do the documentation task next" / "Move it after the investigation" | Reorder clearly identified pending work; ask if the target is ambiguous |
| "Remove the queued documentation task" | Pause dispatch and confirm removal of that exact entry |
| "Pause the queue" | Do not start the next task; the active task continues |
| "Resume the queue" | Check dependencies, permission and uncertain effects before continuing |
| "Clear the queue" | Show and confirm the exact pending entries to remove |
| "Cancel the current task" | Pause new dispatch, then confirm destructive cancellation; retain unrelated pending work |
| "Stop all work" | Pause immediately, then confirm cancelling active work and clearing pending work |
| "Stop this and investigate the new exception instead" | Confirm cancellation/replacement; wait for the previous operation to stop or resolve uncertainty |
| "That isn't what I meant" / "Correct that to ..." | Correct the referenced request; changed action approvals become invalid |
| "Delete this session" | Preview and confirm its exact history/data/work deletion; saved skills and independent perpetual grants survive |
| "Explain the next item" / "Go back to the previous item" | Navigate existing results without repeating the original task |
| "Repeat the summary" | Repeat the existing safe summary, not rerun tools |

**Important current difference:** today **stop** means **cancel task**.
Use **stop speaking** to stop playback. The planned **stop** control stops
speech and pauses new dispatch; destructive cancellation remains separate.

The planned default is two independently executing sessions, within verified
configurable provider/hardware limits, with one active task and up to ten pending
requests per session. Conflicting resources are serialized; unknown effects
block dependent work, not unrelated sessions.
Pending requests expire after 30 minutes by default. Queuing something never
approves its tools, scripts, accounts, or remote transmission.
Failed, cancelled or uncertain work pauses automatic dispatch. Kora cannot
promise cancellation of a remote effect it cannot observe.

## 4. Audio, listening, calls, and notifications

| Ask or choose | Availability and behavior |
|---|---|
| "Stop speaking" | **Current:** stop playback; task execution is separate |
| "Read that answer aloud once" | **Planned:** one identified response, subject to privacy/mute/lock rules; not a lasting call-policy override |
| **Refresh devices**, device/voice lists | **Current** UI: show detected microphones, outputs and voices without recording merely to enumerate |
| **Preview** | **Current** UI: preview selected voice/output and report observable failures |
| **Disable listening** | **Current** UI: release the microphone; the verbal "mute Kora"/"stop listening" control is **Planned** |
| **Enable listening** | **Current** UI: explicitly reopen after readiness checks; you cannot verbally unmute a closed microphone |
| "What are my call speech settings?" | **Partial:** current detected-call Settings/status; full source/freshness explanation is **Planned** |
| "I'm in a call" / "My call has ended" | **Planned:** set/clear manual call state; once protected, changes require a new UI request and cannot override other blocking signals |
| Inspect/manage a learned or enrolled voice profile | **Planned, optional after evidence:** show coarse readiness and open the supported trusted workflow; never expose samples or identity scores |
| "Not now" / "Don't remind me about this version" / dismiss a notification | **Planned:** defer/suppress this eligible notification, not approve its proposed action |

Automatic call detection is currently unavailable. Planned detectors must
explain their coverage and whether their observation is stale or Unknown.
Merely opening Teams does not prove a call.
Call visual-output and voice-activation preferences are separate, and some
settings already exist in the app; see [Responses and calls](responses-and-calls.md).
Neither a speak-once request nor a setting can override mandatory lock policy.
Kora cannot guarantee physical speaker audibility beyond what Windows observes.
Planned in-call feedback defaults to **UI-only**, independently of voice input.
During a protected call, voice-originated voice/in-call setting changes,
manual clearance and speak-once exceptions are rejected rather than deferred;
initiate a new UI change instead. Stop speech, mute and cancellation remain
eligible safety controls. Voice input is not globally disabled by UI-only feedback.
Optional frequent-speaker learning is separately consented local personalization,
not owner authentication or permission, and never learns from ambient audio or chat history.

## 5. Clipboard and selected context

The explicit local plain-text [snapshot/preview/reuse/revoke commands](commands.md#explicit-local-clipboard-preview)
are **Current** and model-free. **Explain the clipboard** currently provides
only that preview and an unavailable-inference explanation. No clipboard
tool is exposed to the JSON selector or a remote provider.

The broader context tools and complete clipboard explanation workflow are
**Planned**.

| Ask | Intended behavior |
|---|---|
| "Use the clipboard" / "Explain the clipboard" | Capture a fresh text snapshot from your deliberate scoped voice/UI request |
| "What context is selected for this task?" | List only its permitted source descriptors, scope and freshness |
| "Show the captured context" | Preview the identified existing snapshot and its source/time, not read the clipboard again |
| "Use that result for this task" | Bind the explicitly identified approved item; clarify missing, expired or ambiguous context |

The clipboard is not monitored. If you change it after capture, the old task
keeps its original snapshot. Plain text is limited to 256 KiB UTF-8; oversized
or unsupported input is explained, not silently shortened.
Remote processing requires separate destination/context review.
General files, screenshots and OCR are later capabilities, not automatically
included in "use context".

## 6. Settings and preferences

Bounded native/exact configuration is **Current** for appearance, installed
provider/voice, spoken-summary caps, assistant display/PTT prefix, exact
microphone/output preferences, per-Kora volume and device-default response mode.
See [exact discovery/get/set/reset commands](commands.md) and [Settings](settings.md).
The general verbal/model registry, broader scopes, category/whole-profile reset
and undo remain **Planned**.
Do not assume an example sentence below is an exact current command.

| Planned request | What it means |
|---|---|
| "What settings can I change?" | List available options and missing dependencies |
| "What are my speech settings?" / "What is my queue limit?" | Explain saved/effective values, scope and policy limitations |
| "Set this option to this value" | Validate the named option, units, value and duration; confirm where required |
| "Make that my default" | Promote an identified compatible temporary preference, not an approval |
| "Reset this category to defaults" | Preview/confirm affected preferences; keep skills and credentials |
| "Undo the last settings change" | Restore a compatible prior preference only; never replay effects or expired grants |

The design covers all these preference categories:

| Category | Options and examples | Current availability |
|---|---|---|
| Voice input | Microphone, listening, assistant name, PTT shortcut, cues, speech-start wait, silence/utterance limits, language/model, owner-aware private speech | Exact microphone preference and assistant-prefix native/typed/activated controls; separate listening/PTT UI; production wake and remaining controls planned |
| Responses and appearance | Mode/scope, output, provider/voice, speed/volume, summary length, detail/captions, theme/timeout, presence size/dots/speed/placement, reduced motion | Native/exact device-default mode, output preference, installed provider/voice, Windows-native -10..10/default0 rate (Kokoro unsupported), 0-100 Kora volume, lowerable summary caps and nine appearance options; broader scoped registry and remaining options planned |
| Calls and proactive speech | Visual/activation override, Unknown policy, detectors/accounts, Busy/DND, manual/temporary override, consent, quiet hours/mode, notification categories/reminders | Call visual/activation UI; fuller controls/detectors planned |
| Work and context | Per-session queue capacity/dispatch/lifetime, admitted concurrent-session limit, task deadline, archive/deletion durations, clipboard and tool-result limits | Planned |
| Providers and connections | Processing mode, default provider/model, validated endpoint, supported identity and connector enablement | Local model setup current; general choice/sign-in/connector flows planned |
| Skills and local data | Sources, enabled revisions/default source, refresh, SQLite diagnostic retention, audit retention and diagnostic verbosity | Delivered native/exact independent future-only ordinary SQLite 1–365/default-reset30 and audit 30–365/default-reset90. Only NEW committed authority audit and qualified projections use audit policy; existing deadlines/all grants stay unchanged. Apply-now/audit pruning unavailable; files30/30 and cleanup scheduling unchanged; other registry options planned |
| Startup and maintenance | Logon registration, initial presentation, notify-only checks/interval/channel and release reminders | Native startup/notify-only maintenance plus exact cached status/review/eligible current-run snooze; general controls planned, no command-triggered check or browser opening |
| Rich viewing | Captions/placement/dismissal, text scale, automatic details, browser choice, Markdown source/rendering and diagrams | Exact local caption mode/corner/0-30-second delay and run-only pin delivered; sentence alignment, general typed viewing and rich renderers planned; embedded guide/basic text current |

The existing process-local presentation resolver orders task, queue and device
default; the delivered mode registry admits only the device default, not durable
session/task/queue overrides. Separate in-call policy remains unchanged and
never waives lock/privacy.
Changes that increase remote exposure or weaken privacy require exact trusted
voice/UI confirmation; they do not approve each later outgoing payload.
While a call is protected, voice-originated voice/in-call setting changes,
including reset/undo, are rejected and need a new UI request. A later click
cannot convert the rejected voice request into a UI-originated request.
Kora changes only its own volume, not global Windows or call volume.
Default/reset **100** preserves original unity; **0** prevents synthesis/autoplay
with complete visual recovery. Raising/resetting never replays retired output.
Warnings, exact security readback, questions and approval previews remain visual.
Secure credentials and biometric enrollment cannot be dictated into a setting.
For all current UI values and ranges, use [Settings](settings.md).

## 7. Grants and approval history

| Ask or choose | Availability and behavior |
|---|---|
| "List grants" / "Show my permissions" | **Partial:** current named model-action session/always grants; detailed resource-bound inventory planned |
| Inspect an approval and its use history | **Partial:** current action inventory; exact resources/digests/scope/applicability/use history planned |
| "Why can this skill/provider access that resource?" | **Planned:** explain the matching grant or required new approval |
| "Manage grants" | **Partial:** current confirmed Add/Remove/Move for named actions; exact resource/scope narrowing and expiry changes planned |
| "Revoke this approval" / "Revoke approvals for this provider/resource/skill/account" | **Partial:** current per-action removal; planned exact single/bulk revocation proposals |
| Export approval/audit metadata | **Planned:** preview and approve an explicit export without raw sensitive content |
| "Show the receipt for that action" / "Show its audit events" | **Planned:** read permitted observed outcomes, not rewrite history or invent rollback |

The model cannot approve itself. The initial request to change a grant does
not apply the change, and confirming a grant does not execute the named action.
Kora shows the precise proposal and accepts only its permitted confirmation
channel; additional OS presence or non-voice confirmation remains necessary
where specified.

**Current grants** are keyed to named model-suggested actions. They do not
approve script bytes and do not govern direct exact commands.
**Planned executable grants** bind the precise task, implementation/resources,
inputs and execution context. **Once** is one invocation; **Session** binds that
operation to the named Kora work session; **Always/Perpetual** remains independently
until explicitly removed/edited or its approved content is revoked. Perpetual
records have no expiry, retention or eviction, even when their originating
session is archived/deleted. These scopes never approve changed code.
All declared scripts, including shared helpers, contribute to the combined
identity. A changed approved script or definition permanently revokes affected
authorizations; restoring old bytes does not restore them. A changed dependency
or invocation still requires verification and fresh applicable approval.
Revocation prevents new calls but cannot
erase a remote effect already performed.
Planned **Ignore reusable grants during calls** is On by default: protected
calls require fresh Once approval for each exact operation while retaining
Session/Perpetual records. Ordinary operation approval still supports voice or
UI; changing voice/in-call protection settings during the call requires a new UI request.
See [execution design](skill-and-task-execution-design.md) and
[privacy and logs](privacy-safety-and-logs.md).

## 8. Discovering, selecting, and managing skills

The full skill-registry operations below are **Planned**. Current
[artifact invocation](commands.md#run-skills-and-future-artifacts) provides
bounded source-qualified bundled/profile instruction selection and a slash
dropdown. The tray's **Skill packages (inspection only)** exposes immutable
declared bundled files. Neither is general source registration, enablement,
authoring or a script executor.

| Ask | Intended behavior |
|---|---|
| "Show my skills" | List bundled, shared-profile and Kora-specific packages with their sources and availability |
| "Show the definition of this skill" | Inspect its pinned revision, purpose, required tools/tasks and limitations |
| "Show the skills in my profile" | Select/register a bounded read-only source; no whole-profile scan or automatic enablement |
| "Refresh this skill source" | Discover changed/unsupported revisions without silently trusting them |
| "List registered skill sources" / "Remove this source" | Inspect or confirm removal of a bounded registration, not delete shared original files |
| "Enable this skill from this source" | Review and voice/UI-confirm the exact compatible revision; no execution grant |
| "Disable this skill" | Block new use and report whether active calls can be cancelled |
| "Remove this saved skill" | Confirm exact removal of owned saved data; do not delete bundled/shared original content |
| "Use this skill to investigate this request" | Select its enabled pinned definition, then use admitted tools under normal policy |

Duplicate names require a source-qualified choice. A skill called "Lock the
machine" in your profile cannot replace the privileged bundled registration.
Shared sources remain read-only. Compatible instruction-only profile packages
are not permission to run scripts found in their text.

## 9. Creating and trying skills

The following are **Planned** declarative authoring operations:

| Ask | Intended behavior |
|---|---|
| "Create a skill that explains a deployment failure" | Clarify its purpose/inputs/tools and stage a proposal in memory |
| "Improve this skill" / "Make it read-only" | Refine the exact proposal or create an attributed fork; invalidate old proposal approval |
| "Validate this skill" | Check structure, dependencies and instruction risks without executing code |
| "Test this skill" | Run simulated examples using mocked tools; no real side effects |
| "Save this skill" | Voice/UI-confirm saving the validated revision, disabled, to Kora's skill store |
| "Enable this revision" | Separate reviewed voice/UI enablement |
| "Restore this earlier revision" | Review and restore the exact compatible definition; do not restore revoked execution grants |
| "Try this skill on this request" | Admit a separate real trial with ordinary context/tool/action approval |

Passing a simulated test does not approve a real run or prove universal safety.
Authored skills are declarative; Kora does not generate executable scripts,
install missing tool implementations, edit its own application, or copy a
bundled script into your skill. Improving a bundled skill creates a distinct
adaptation, not a replacement. Saved skills survive conversation clearing.

## 10. Content and viewers

General rich-content and viewer operations are **Planned**, except the noted
current basic response/embedded-guide features.

| Ask or choose | Intended behavior and current limitation |
|---|---|
| "Show the full answer" | Show the identified existing result; basic response text is current, general rich details planned |
| `list speech text settings`; `get/status/set/reset display.speech-text` | Delivered Off/CurrentUtterance native/exact typed/activated control; only matching actual playback reveals text; natural aliases remain planned |
| `get/status/set/reset display.speech-text-placement` / `display.speech-text-dismissal-delay` | Delivered primary-screen corner and canonical 0-30-second delay, default BottomRight/5; same admitted atomic/audited control, no speech or caption enablement |
| Native **Pin / Unpin**; `get/status/set/reset display.speech-text-pin` | Delivered run-only pin of already-observed caption, false/true; previous-speech label after normal completion; source/privacy/stop retirement always wins; natural "that text" and general item pinning remain planned |
| "Show the source" | Show inert source and provenance, not execute it |
| "Show the diagram" | Render a supported validated diagram or explain why only source is available |
| "Preview that HTML" | Static isolated preview; no executable scripts, forms or automatic remote assets |
| "Show this page" | Review the exact destination and chosen embedded/external browser |
| "Go back" / "Reload" / "Zoom in" | Navigate/adjust the identified controlled viewer, not automate page forms |
| "Close the content viewer" | Close Kora's owned viewer; an external browser remains outside Kora's control |
| "Load the images for this page" | Review the exact assets/destinations; not blanket network permission |
| Save/export this content | Review and approve an exact selected write; never automatically run downloaded/exported code |

The current Documentation window displays only embedded guide pages. Its links
are inert text; select pages from its native page list. It does not fetch web
content, run code, or act as a general Markdown/browser tool.
Opening a website is not approval to extract it or send it to the model.
Internet viewing is unavailable in local-only/offline mode, and external
browser cookies/accounts/networking are not controlled by Kora.

## 11. Connections and diagnostics

| Request | Availability and behavior |
|---|---|
| Check a provider/connector's health | **Partial:** current local inference/readiness; general supported identities and connector checks planned |
| List an admitted connector's tools | **Planned:** reviewed schemas and limitations; discovery does not approve calls or install a server |
| List/configure admitted connectors | **Planned:** inspect metadata or propose a supported setup flow; secure sign-in stays with the host |
| List supported runtimes or inspect their health | **Planned:** explain actual locality, capability, identity reference and limits; no silent provider switch |
| Read a Kora diagnostic log excerpt | **Planned:** host-validated daily logs only, bounded output and normal privacy/egress checks |
| Export selected diagnostic excerpts | **Planned:** review exact bounded log IDs and destination; never silently upload |

Daily content-minimising logs already exist. That does not mean the model can
read arbitrary files or automatically upload diagnostics.
Secure sign-in happens through the host/provider's authentication flow; never
paste or dictate credentials into model-visible requests.
No concrete enterprise connector/tool list has been selected by this design.

## 12. Setup, startup, maintenance, and native answers

These are host-owned flows, not installer or approval tools given to the model.

| Ask or choose | Availability and behavior |
|---|---|
| Review required setup; reuse an existing supported runtime | **Partial:** current storage/SQLite, separately consented PowerShell/Ollama setup and optional Kokoro assets; fuller catalogue planned |
| Answer a question, approve once/session/always, or reject | **Current** local-model prompt flows; only the displayed matching proposal is eligible |
| Choose a microphone or recover listening by mouse | **Current** explicit UI flow; stronger production wake requirements remain planned |
| Sign in to a supported provider or enroll/delete a speaker verifier | **Planned** separate secure native flow; the model never receives secrets/biometrics |
| "Enable start at logon" / "Disable start at logon" | **Planned:** change only Kora's own startup registration after scoped consent |
| `maintenance status` / `maintenance review` / `maintenance snooze` | **Current bounded cached parity:** truthful cached observation, exact existing native review, or eligible reviewed notice snooze for this run; no check/consent/browser/download/install/model authority. [Exact bounds and exclusions](commands.md#exact-cached-release-maintenance) |
| "Check for updates" / "Is an update available?" / "What is the update doing?" | **Planned:** bounded metadata check and observed status; failure is Unknown, not "up to date" |
| "What's new in that update?" | **Planned:** show release notes as untrusted information |
| "Show that release" | **Planned:** review the exact canonical release URL before opening it |
| "Not now" / "Don't remind me about this version" / "Cancel the update" | **Planned:** defer/suppress a notification or cancel Kora's metadata check; not an external installer |
| "Update Kora" / "Install the update" | **Planned dialogue:** explain manual replacement; Kora does not download, stage, run or install the update |
| Activate another build or return to the original | **Planned** native identity/handoff confirmation, not another parallel assistant |

Setup consent does not grant a script or task. Installing PowerShell does not
enable the model to run arbitrary PowerShell.
An unsigned release notice cannot authorise automatic application replacement.
If the microphone is closed or unavailable, use the tray, Settings and native
controls; there is no secret listening exception for approvals or unmute.

## 13. Built-in skills

The three fixed computer-control packages are embedded and available for
**inspection only**. Open **Skill packages (inspection only)** from the tray.
Each package shows its manifest, instructions, data-only fixture, entry script
and shared helper in named read-only native source tabs, with per-file,
script-set and definition hashes. The same immutable original bytes are hashed
and reviewed, including UTF-8 BOMs and line endings.

No package can be enabled, approved or invoked here. The worker, protected
deployment, network and real-control proofs remain outstanding; no interpreter
or adapter identity is admitted. Current direct Windows API lock and legacy
action-name grants are unchanged. Inspection is not grant authority or model
exposure. Hashes identify content, not the publisher.

Transitive tracking is best effort, not a complete executable allowlist.
Further code and changes may go undetected. A future granting user accepts
responsibility for the overall actions within separately admitted scope.
Profile discovery, authoring and content-bound execution remain **Planned**.

The bundled resources now ship in `Kora.Definitions.dll`, separate from the
C# actions in `Kora.Tools.dll`. This source organization changes no package
bytes, hashes, availability, approval or execution behavior. Named agent
profiles are a design direction, not a released loader or autonomous runtime;
they cannot expand tools, acquire context or approve effects.

### Lock the machine

**Ask:** "Kora, lock the machine", "lock my computer", or "lock Windows".

**Current:** an exact phrase locks through the native Windows handler, without
a confirmation prompt. A model-suggested lock needs an action-specific approval
unless a session/always action-name grant already permits it.

**Planned skill:** Kora selects the original bundled lock definition and its
registered script-backed task. Exact phrases and other wording use the same
precise execution grant. You can review the exact script and approve the
permitted once/session/always duration. Choosing the skill or opening review
does not approve it.
For a standalone unaddressed request, Kora will create and show a new durable
Active control work session before approval. Session approval is available
only after that binding; it is not the selected chat or Kora's process lifetime.
Later unaddressed locks create new sessions and do not reuse that Session grant;
explicitly addressing its still-Active session can use only the same approved
operation. Failure to save the binding prevents dispatch.
Review includes the entry point and every required/shared helper, their
individual hashes, the combined script-set hash and the definition digest.
Execution uses those exact embedded bytes, not a writable extracted copy.

It targets only your current Windows session: no remote computer, other user,
unlock, timer or elevation. It can use a narrow priority route during other
work, but still needs its applicable grant.
Kora observes the Windows lock event before claiming confirmed success, closes
the microphone and hides sensitive output. Unlock does not silently restore
listening; re-enable explicitly.

### Shut down the computer

**Ask:** "Kora, shut down the computer" or "power off the computer".

**Current:** recognize and display a non-destructive proposal only.
**No operating-system shutdown request is sent.**

**Planned skill:** prepare graceful shutdown of this local computer, warn about
unsaved work, and ask whether active work across all sessions should finish
safely or be cancelled.
Management or execution models may propose it, but Kora's deterministic host
lifecycle controller owns approval, countdown and gated fixed-action dispatch.
Neither model can authorize itself or run the power script independently.
The exact task/script needs its own applicable grant. A fresh **"confirm
shutdown"** or equivalent deliberate UI confirmation is also required for the
specific power proposal, together with any mandatory Windows/provider check;
a reusable grant does not waive this per-request decision.

The spoken confirmation prompt expires after 30 seconds. Kora starts a visible
30-second countdown only after the required decisions, and rechecks approval
and session state before the OS request. **"Cancel shutdown"** cancels Kora's
owned pending proposal/countdown until the final request is accepted.
After OS acceptance, Kora may be unable to cancel. It cannot save other apps'
documents, promise completion, or silently force applications closed.

### Restart the computer

**Ask:** "Kora, restart the computer", "reboot my computer", or "restart Windows".

**Current:** recognize and display a non-destructive proposal only.
**No operating-system restart request is sent.**

**Planned skill:** the same graceful local-machine preparation, work handling
and countdown as shutdown, but a distinct task/grant. Confirm with **"confirm
computer restart"** or the equivalent explicit UI confirmation, with any
mandatory Windows/provider check.
**"Cancel computer restart"** cancels only Kora's owned pending action before
final acceptance.

This is not **restart Kora**. Kora does not promise that interrupted tasks or
other applications' unsaved documents will resume after Windows restarts.

### Read-only integration skill

**Planned, not yet selected:** the design requires one bundled integration skill
using an admitted read-only MCP tool. Its name, connector, inputs and workflow
have not been chosen. No specific GitHub, Azure DevOps or deployment skill is
available merely because those integrations are candidates.

Before use, Kora must identify its source/revision, dependencies, account,
read scope and result-processing destination. "Read-only" does not mean it can
read every resource or send the results remotely without permission.

## 14. Persistent sessions, history, and shared questions

**Current bounded subset:** [Sessions](windows-and-tray.md#minimal-durable-sessions)
opens passive durable ID/name/generation pages, actual typed question/current
task records and selected-session evidence. Native empty Create, revisioned
exact-ID Rename and guarded idle Done/resume use existing host authority;
the same operations are also available through
[exact-ID typed/activated-voice commands](commands.md#bounded-exact-id-session-commands),
not name-based routing or model tools. Selection never
redirects global input, questions or approvals, and reading/rename never resumes.
The separate native local-version question/review route supports actual durable
drafts/answers and exact-record review, not general conversation execution.

The full workflows below remain **Planned**. In that design, the compact view
shows the selected session's latest interaction. A Sessions
workspace places the Active/Done list beside full conversation/history, with
separate read-only detail/script viewers and an All work view.

| Ask or choose | Intended behavior |
|---|---|
| "Show my sessions" / "What is this session doing?" | List permitted state, attention and meaningful activity/due dates; basic status works without a model |
| "Start a new session about this issue" | Create a new durable work stream; do not reuse unrelated context or grants |
| "Rename this session" / "Switch to the documentation session" | Change title/selection, not cancel work, resume a Done session or alter execution priority |
| "Find the session where we investigated that error" | Search permitted history and cite evidence; ambiguous matches need a choice |
| "Show this session's history" / "Show that script or artifact again" | Read retained messages/receipts or exact immutable source without repeating tools or executing old scripts |
| "This session is done" | Resolve live/unknown work and archive it; Done is not proof every task succeeded |
| "Resume that session" | Explicitly reactivate it without rerunning tasks or restoring consumed/revoked approvals |
| "Delete this session" | Preview exact data/work impact and confirm; disclose independent grants, audit, exports and provider copies |
| "What questions are waiting for me?" | Show current session-bound cards; generic spoken replies have only one foreground target |
| Select, deselect, type, review, submit or cancel an answer | Use the same bounded typed draft through voice, mouse or keyboard; selecting a checkbox is not submission |
| "Show the full answer" / "Go back" / "Pin this detail" / close a viewer | Navigate existing identified content without rerunning work or changing session lifecycle |
| "Show the planned steps" / "Check what happened to that uncertain action" | Distinguish plans from receipts and perform only supported read-only reconciliation, not a blind write retry |

You can switch channels during a question and answer separate sessions'
explicitly addressed UI cards. Only one question is the current spoken reply
target. A spoken high-risk confirmation names its action and target; the
equivalent UI identifies the same proposal. Historical/rendered content cannot
answer or approve itself. During protected calls the separate settings-origin
restriction still applies.

Planned history uses standard SQLite/private device-local storage of permitted messages,
decisions, artifacts and observed receipts, not raw audio, secrets or hidden
model reasoning. Source/identity restrictions and remote-egress policy still
apply when reading or summarizing old content.

Private permissions provide the account boundary; exported/copied files are
readable. Database encryption and database key/rekey machinery are not required.

Defaults archive after **24 hours** and delete after **30 days** from the same
last meaningful activity. Both durations are configurable; passive selection,
browsing and history questions do not restart the clock. Live/uncertain work
is not silently discarded. Shorter retention previews immediately affected
sessions and requires a separate apply-now decision.
Restart restores history/selection, not execution: interrupted or unknown work
needs explicit resumption/replanning/reconciliation. Perpetual grant records
are retained independently; readable history is not a live approval token.

## 15. Deferred context, knowledge, and general execution

These named future contracts are **Deferred** or have the narrower exception
stated below. They are not available merely because you can view a file or
install PowerShell.

| Ask or choose | Intended boundary |
|---|---|
| Select/read a file for context | Planned Slice C for reviewed skill revisions; general files require a later admitted resource-scoped adapter |
| Capture a selected window/region | Deferred explicit screen capture; no ambient screenshots or audio |
| Search/read an admitted knowledge source | Deferred permission-checked retrieval with citations, freshness and deletion controls; no chosen enterprise connector is promised |
| Prepare/run a registered application or script task | Deferred general execution under exact resource/invocation/digest review and enforced containment; never arbitrary shell text |
| Inspect/cancel a Kora-owned executable invocation | Only with an admitted execution capability; report observed outcomes and cancellation limits, not arbitrary process killing |

## Not included or not yet specified

- Arbitrary shell/PowerShell, model-generated scripts, general app launching or
  automatic executable imports.
- Kora source/binary/policy self-modification, agent elevation or updater access.
- Sleep, hibernate, sign-out, forced shutdown or remote computer controls.
- Unselected enterprise tools, general file/Git/workspace actions, screen/OCR
  capture, knowledge retrieval or exported MCP server APIs.
- Browser/desktop automation, arbitrary renderer plugins or parallel tasks
  within a single session. Independent-session concurrency is planned.

Some areas are future design candidates; none is implicitly enabled by a grant
to a current named action or bundled skill.
Use [Commands](commands.md), [Settings](settings.md) and
[Troubleshooting](troubleshooting.md) for the current release.
