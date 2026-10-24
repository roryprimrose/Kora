# Tools and built-in skills: current and planned

Use [Commands](commands.md) for exact phrases and parameters,
[Settings](settings.md) for preferences, and the linked feature guides for
workflows. This page explains capability boundaries rather than duplicating
those references.

**No script-backed built-in skill executor ships in the current release.**
Exact commands run host-owned C# actions. A verified local model can answer,
ask a clarification question or propose an existing action/grant change; this
does not grant a general execution or browsing capability. Selecting artifact instructions does
not execute their scripts or grant permission.

### Retrieve and summarize one public page locally

In **Sessions** or the compact conversation window, select an exact Active
session and enter `get web page https://example.com/` in the conversation
composer. Choose **Retrieve and summarize one page locally** (not ordinary
Submit). The address must be an absolute HTTP or HTTPS address without
credentials or a fragment.

If the local model is admitted, its one-page request opens the existing native
approval window. Review the exact destination and action, choose **Approve
once**, and submit the approval. Closing/cancelling, expiry, revoked authority,
or changed ownership/private presentation prevents continuation. Ordinary
questions and model text cannot approve the operation. No preapproved address
or reusable grant automatically authorizes this workflow.

Kora retrieves bounded inert text through its existing public-address/DNS
policy. A redirect to another address is denied, not silently followed or
approved. At most one page action is dispatched. The result includes the exact
requested/final address, retrieval time, content type, local-only provenance
and truthful truncation; the complete result is limited to 2 KiB. Page text
cannot enable tools or supply instructions. The same local turn summarizes
that result, and the genuine final answer is retained in the original session.
Cancel uses the exact running conversation task and waits for owned resources;
no late response is presented as success.

**Unavailable boundary:** this requires a separately admitted usable local model.
Missing runtime/profile/approval composition reports unavailable or denied;
it never fetches a page as a fallback. The existing finite turn deadline and
cumulative byte budgets still apply, including approval time. There are no
live-provider or hardware qualification claims here. Hosted providers cannot
consume this tool or its result. This is not arbitrary browsing, SDK-native
network access, or a change to the separate local-only web-result summary policy.

## Run one reviewed local agent profile

Type **review agent**, or say your current assistant name followed by **review agent**. This opens a separate bounded-run review; loading an ordinary agent artifact is not execution. No default or bundled agent is selected.

1. Enter the full canonical path to an **existing user-owned `.agent.md`** file on a local fixed drive. Protected, hidden/system, reparse and hard-linked sources are unavailable. Only the explicit [v1 compatibility subset](../Design/Commands_Tools_And_Skills.md#bundled-definitions-and-agent-profiles) is accepted; missing or unsupported required metadata is rejected.
1. Refresh the existing Active conversations and explicitly select one. Enter the exact original task.
1. Choose **Read source and review exact invocation**. This consents to bounded local source reading, not invocation. Review the complete inert profile, original input, exact target, local text input/output, no references, explicit observation-tool subset, requested budgets and host clamps.
1. Separately choose **Consent to this exact bounded invocation**. One serial local model/tool turn runs under independent per-tool permissions. A real final answer or truthful terminal failure is retained in that exact conversation; inspect it in **Sessions**. Unavailable runtime is not a successful prompt submission.
1. **Stop / revoke and join owned execution** (or **agent stop**) closes authority. Closing the window, privacy/ownership changes or source replacement also invalidate use. Resources remain owned until the actual provider terminates; cancellation does not imply physical completion or rollback.

Equivalent typed/current-name activated voice controls are **agent source &lt;full path&gt;**, **agent request &lt;original text&gt;**, **agent targets**, **agent next targets**, **agent target &lt;displayed session ID&gt;**, **agent review invocation**, **agent confirm invocation**, and **agent stop**. Changing the input or target requires a fresh review. Review expires after two minutes and cannot be replayed.

Only the six original recorded read-only observation IDs can be requested, and only the exact reviewed subset is advertised or dispatched. There are no instruction/skill/file imports, hooks, scripts, child agents, concurrent workers, scheduling, MCP, web/power tools, hosted fallback or provider-native facilities. Profile text and tool output cannot grant authority.

**Availability:** the execution path remains fail-closed when the separately configured local model is unqualified or unavailable. Deterministic implementation tests do not establish live-model enablement or release qualification.

## Asking Kora to do something

Type a command or hold push-to-talk and use the configured assistant name.
Exact phrases take precedence; unmatched requests require a verified local
model. Models propose, while Kora validates and applies approval/privacy policy.
A clarification answer is not approval. See
[Getting started](getting-started.md#use-typed-commands).

`/lock`, `/restart`, `/shutdown`, `/skill <name>` and activated **run lock**
select source-qualified instructions for a local-model request. Compatible
disk instructions remain untrusted. Shared-profile skills use separate native
registration/inspection, not startup model exposure. See
[artifact commands](commands.md#run-skills-and-artifacts).

## 1. Application and help

**Current:** show/hide/exit/restart Kora, Settings, documentation, setup/help and
local-version queries. Hide does not mute or cancel. Application restart is not
Windows restart. See [application commands](commands.md).

The six-ID [read-only host registry](commands.md#read-only-host-discovery)
returns bounded recorded facts; it does not run fresh probes, qualify a runtime
or expose the complete planned model catalogue.

## 2. Session, activity, progress, and readiness

**Current bounded:** exact-ID sessions, observed setup/task status and native
selected/all-session work surfaces. Names are labels, not selectors; browsing
never resumes, dispatches or retargets approvals.
See [Sessions](windows-and-tray.md#authoritative-sessions-work) and
[exact-ID syntax](commands.md#bounded-exact-id-session-commands).

**Planned:** general model session/status tools and natural-language history
reasoning. Existing receipts do not establish those capabilities.

## 3. Work management and conversation

Sessions retain real work observation, exact lifecycle controls and scoped cancellation.
Diagnostic-only fixed-read queue admission, dispatch and management product controls are retired; existing records remain inspectable/cancellable with original authority and deadlines.
Useful direct status queries and internal scheduler/store tests remain ([queue boundary](commands.md#deterministic-local-version-queue), [work controls](../src/Kora/SessionsViewModel.Work.cs)).

General concurrent workers/provider scheduling remain unqualified; retiring diagnostic product surfaces does not enable them ([admission boundary](../src/Kora.Application/Hosting/SessionWorkspaceService.Commands.cs)).

## 4. Audio, listening, calls, and notifications

[Voice and audio](voice-and-audio.md) explains explicit consent/PTT, exact
device selection, speech providers, previews, Stop and failure recovery.
[Responses and calls](responses-and-calls.md) explains output policy and
run-only manual call protection.

**Unavailable:** production wake, automatic call detectors and general
proactive spoken/reminder workflows. Device selection and opening Settings
never grant capture consent.

## 5. Context and clipboard

**Current:** explicit local immutable clipboard preview, same-ID reuse and
revoke; no polling, write, history or model submission. **Explain the clipboard**
reports explanation unavailable.
See [clipboard commands](commands.md#explicit-local-clipboard-preview).

## 6. Typed configuration

**Current bounded:** admitted native/exact preference controls listed in
[Settings](settings.md). Invalid/stale/unconfirmed state reports recovery
instead of silently defaulting. No arbitrary preference patch or generic
model settings tool is delivered.

## 7. Grants and audit inspection

[Privacy and safety](privacy-safety-and-logs.md) distinguishes legacy named
model-action preferences from exact operation grants and committed audit.
Inspecting a grant or reading history never creates permission.

Current call protection applies the **Ignore reusable grants during calls**
behavior to reusable model-action grants without deleting them. The proposed
permission-level migration instead retains grants/levels and defaults spoken
approvals off; it is not current behavior. Protected voice-originated preference
changes require a new eligible UI request, not a later approval click.

## 8. Skill discovery, registry, and authoring

**Current:** bundled immutable package inspection, explicit artifact instruction
selection, and bounded native shared-profile registration/inspection/recheck/
withdrawal. See [shared-profile commands](commands.md#inspect-shared-profile-skills-locally).

The passive inspectors retain name/description search, availability/compatibility and ordinary loaded metadata order.
Digest query fields are retired.
Source identity, exact digests, provenance and integrity are still displayed and validated internally; browsing does not grant execution or trust ([query policy](../src/Kora.Application/Skills/SkillCatalogueSearch.cs), [bundled inspector](../src/Kora/SkillPackagesWindow.axaml.cs), [shared inspector](../src/Kora/SharedSkillSourcesWindow.axaml.cs)).

**Order** defaults to **Source order**. Choose **Name ascending (ordinal)** or **Name descending (ordinal)** for ordinal names, unknown names last in both directions, with exact canonical identity ascending as the tie-break. All existing filters still apply; incompatible rows are not hidden by ordering ([ordering contract](../Design/Skill_Storage.md#delivered-bounded-r21-native-inspection)).

Order edits clear selection/private source panes without reading, discovering or rechecking; choose **Search** and explicitly reselect. **Clear** also restores Source order. Ordering is volatile presentation, not authority, catalogue completeness or runtime enablement ([native tests](../tests/Kora.Windows.IntegrationTests/SkillMetadataSearchWindowTests.cs)).

**Current bounded:** [native owned-instruction editing](instruction-editor.md),
exact Save/Delete review, Reload/Overwrite/Cancel conflicts and separate reviewed
explicit use of the saved revision. Profile originals and other kinds remain read-only.

**Current bounded:** [reviewed instruction-only shared skill invocation](commands.md#reviewed-instruction-only-shared-skill-invocation) separately enables one exact compatible single-file revision for a volatile local-only, zero-tool request in one existing Active conversation. Registration/inspection alone grants no invocation authority ([workflow](../src/Kora.Application/Skills/SharedSkillInvocationWorkflow.cs)).

**Planned:** general tool-dependent skill enablement/invocation, conversational skill/instruction
authoring and agent profiles. Saving or loading a definition is not executable
trust. [Execution design](skill-and-task-execution-design.md) describes the
future boundary.

## 9. Content presentation and viewing

**Current bounded:** native full/source details, web-result details and
playback-bound local captions. Source is inert; opening it never executes,
refetches or grants model access.
See [windows](windows-and-tray.md) and [speech text settings](settings.md#local-speech-text).

**Planned:** general rich Markdown/diagram/HTML rendering and browser workflows.
Document viewing is not desktop automation.

## 10. Connections and diagnostics

**Current bounded:** local readiness and read-only native evidence inspection.
SQLite, DailyLog, CombinedLog and committed AuthorityAudit have distinct
authority/retention boundaries; [Logs](privacy-safety-and-logs.md#logs) explains
them. A file mirror never becomes authoritative audit.

**Unavailable:** generic model diagnostics, unselected enterprise connectors
and automatic credential/account substitution.

## 11. Host-only user flows

Setup, voice consent, secure sign-in, build takeover/return and release
maintenance remain trusted host workflows. Opening or explaining them does
not authorize downloads, installs, captures, execution or network use.
See [Getting started](getting-started.md), [Settings](settings.md) and
[build handoff](windows-and-tray.md#launching-another-build-and-returning).

## 12. Notify-only maintenance

Native Check/Open and separately consented network use remain distinct from
exact cached status/review/snooze. Nothing here installs an update.
See [maintenance commands](commands.md#exact-cached-release-maintenance).

## 13. Built-in skills

The three bundled packages are inspectable and their instructions selectable,
but every script action remains unavailable. Future review binds original
entry/helper bytes, individual digests, the combined script-set hash and
definition digest; a package name or model choice is not approval.

### Lock the machine

**Current:** an exact lock command calls Windows directly without the planned
script-bound approval gate. Model-suggested lock requires its existing
action-specific approval/grant. See [exact lock behavior](commands.md#lock-windows).

**Planned:** shared content-bound admission for direct/UI/model routes, a durable
control-session binding and an observed lock receipt. It targets the current
Windows session only, not another user, remote computer, unlock or timer.

### Shut down the computer

**Current:** a visible non-executing proposal only.
**No operating-system shutdown request is sent.**

**Planned:** exact script/grant review, active-work handling, fresh confirmation
within 30 seconds and a 30-second host countdown with pre-effect revalidation.
An OS-accepted request may no longer be cancellable; Kora cannot save other
applications' documents or promise completed shutdown.

### Restart the computer

**Current:** a visible non-executing proposal only.
**No operating-system restart request is sent.**

**Planned:** the same power safeguards with a distinct restart task/grant.
This is not **restart Kora** and does not promise interrupted work will resume.

### Read-only integration skill

**Planned and unselected:** one admitted read-only MCP integration skill.
No GitHub, Azure DevOps or deployment connector is available merely because
it is a candidate. Source/account/read scope and result transmission need
their own admission.

## 14. Persistent sessions, history, and shared questions

**Current bounded:** durable metadata, ordered host interaction receipts,
native exact questions/recovery, retention/Keep controls, exact lifecycle
and fixed read-only work. See [Sessions](windows-and-tray.md).
Only retained approved records are history; bootstrap response bodies and
hidden model reasoning are not automatically captured.

**Planned:** full conversation execution, generic shared questions/routing and
model history reasoning. Selection never approves, resumes or retargets work.
Perpetual operation grants remain independent of session retention.

## 15. Deferred context, knowledge, and general execution

**Current bounded:** reviewed local file/folder previews and lexical search,
one retained text/Markdown attachment per Session, exact replacement/removal,
metadata catalogue and citation-to-source navigation.
See [file inspection](windows-and-tray.md#native-local-file-inspection).

**Unavailable:** managed knowledge sources, grounded model file answering,
screen/OCR/image capture, vector indexes, arbitrary shell/application launch,
source self-modification, elevation and general desktop automation.
Local inspection and PowerShell installation do not enable these capabilities.
