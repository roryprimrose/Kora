# Voice and typed commands

**Product realignment:** standalone utility commands and fixed diagnostic-queue product surfaces are approved for retirement. This guide continues to describe current implementation until each code removal lands; retirement is not permission to bypass existing checks. See the [approved scope](../Design/MVP_Scope.md#approved-retirement-and-simplification).

## Prepare an exact Evidence trace filter

Type `prepare evidence trace 0123456789abcdef0123456789abcdef`, or use the
current assistant-name prefix for activated voice, for example
`Kora, prepare evidence trace 0123456789abcdef0123456789abcdef`.
The trace value must be exactly 32 lowercase ASCII hexadecimal characters and
not all zero. It is not a GUID, span ID, traceparent header, URI or link.
There is no trimming, case folding, punctuation repair or inferred context
([exact parser](../src/Kora.Core/Commands/EvidenceTraceCommand.cs)).

This opens/focuses the **same native Evidence window** and prepares only
**Trace correlation filter**, with exact native value and focus confirmation.
**All other filters and the selected source remain unchanged and AND-composed**,
even if they restrict or conflict with the trace. A new window keeps its usual
defaults. Old rows, Details/selection, Find, actions and next-page cursors retire,
including a pending page; preparation does not search, scan, read or count.
Click **Search / refresh** separately to query the existing bounded retained
reader. **Next page** remains a separate action over that exact snapshot.

`evidence trace help` returns constant syntax/disclosure only; it reads no
current trace, source, metadata or evidence. Malformed reserved commands do not
answer pending questions or fall through to a model. Missing/stale native
binding, original private/owner/input/call/voice admission or failed focus means
explicit unavailable, never an unfiltered/latest/source fallback.

Trace correlation is untrusted diagnostic data, not Kora session/task/user
identity, ownership, permission, approval or authority. Preparation cannot select
an authoritative audit source, promote diagnostics to audit, set activity parents,
baggage or host context, copy/export/persist, renew session activity, capture a
source, run retention maintenance or contact a model/provider/network.
See the [Evidence guide](privacy-safety-and-logs.md#read-only-evidence-inspection)
and [native contract](../Design/Information_Display.md#exact-native-evidence-trace-preparation).

## Open an exact Settings tab

Use one complete case-insensitive phrase below, optionally prefixed by the current assistant name when typed. Activated voice requires that current-name prefix. Outer spaces are accepted; punctuation, extra words, control characters and input over 256 characters are not ([exact grammar](../src/Kora.Application/Configuration/SettingsSectionCommand.cs)).

| Exact phrase | Existing tab |
| --- | --- |
| `open appearance settings` | Appearance |
| `open speech settings`, `open audio settings`, `open microphone settings` | Speech & audio |
| `open response settings` | Responses |
| `open session settings` | Sessions |
| `open call settings` | Calls |

These routes open/activate the same singleton Settings window and select a named native tab, retaining unsaved state. Generic `open settings`, setup/readiness and voice recovery remain separate existing routes ([native controller](../src/Kora/SettingsWindowController.cs), [tab selection](../src/Kora/SettingsWindow.axaml.cs)).

Navigation never chooses a microphone or endpoint, changes Windows defaults, starts listening/capture, grants permission, refreshes providers/network, saves a setting or confirms an action.
Calls is not manual-call activation; Responses does not restore retired/private content. Native visibility/lifetime observation remains authoritative ([navigation admission](../src/Kora.Application/ViewModels/MainViewModel.SettingsSectionNavigation.cs), [native tests](../tests/Kora.Windows.IntegrationTests/SettingsSectionNavigationTests.cs)).

Malformed reserved section requests clarify in the transcript before model/question/approval routing. Pending questions, approvals and security previews are not answered or replaced.
Missing/revoked/disposed presenters and stale original host, privacy, ownership or voice generations report unavailable, never a default page or success fallback ([routing](../src/Kora.Application/ViewModels/MainViewModel.cs), [deterministic tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.SettingsSectionNavigation.cs)).

## Open existing workspace surfaces

| Exact typed phrase | Existing native surface |
|---|---|
| `open all work` | Bounded All work / attention overview |
| `open compact conversation`, `collapse conversation` | Original compact interaction in the primary response window; shared session selection/input remain intact |
| `open sessions`, `show sessions`, `show me the current sessions`, `expand conversation` | Expand that same window into a bounded session list and selected conversation/activity |
| `select the top session`, `select the first session`, `select the third session`, `select the last session` | Select one exact row from the displayed session page; first through twenty-fifth supported |

Activated voice requires the current available assistant name, for example `Kora, open all work`. Renaming replaces that prefix in the recognition grammar.
These phrases accept no extra words, body queries or trailing punctuation; malformed reserved navigation input is refused, not sent to a model.

Opening activates the same controller/window as tray and UI. It does not create/select/resume a session, discard a dirty buffer, reload a saved draft, submit work, answer a question or confirm an approval.
No selection means no conversation target, not a new conversation. Expanded Sessions
loads a bounded metadata page when none is present, initially filtered to Active.
It never loads conversation bodies automatically. All work opening still reads
nothing and remains a bounded observer, not an atomic global snapshot or all-clear.

Ordinal selection requires the session list to remain presented. Top/first is
position one; Last is the last item on the loaded page, not in the whole catalogue
or scroll viewport. Background work does not reorder it. Changed/retired pages,
privacy/ownership changes and out-of-range positions refuse rather than selecting
a replacement. Selection preserves unsent input and never resumes a Done session.
Use the named composer for explicitly addressed messages. Automatic voice
relatedness matching and model-visible session-list context remain unavailable
in both modes. See [expanded interaction](windows-and-tray.md#expand-and-collapse-the-interaction-window).

Native composition and original/current private ownership, input, call/privacy epochs and compact source must remain admitted through queued opening; otherwise initiate a fresh command. Protected calls refuse these routes.
Model navigation is explicitly unavailable: these window-focus actions are not added to bootstrap or the six read-only model descriptors.
See [native windows](windows-and-tray.md#workspace-surface-navigation) and the [deterministic evidence](../Design/UI_Workspace_And_Windows.md#workspace-surface-command-evidence).

## Focus the exact pending native question

Use `open pending question <session-id> <task-id>` with two nonempty canonical
lowercase D-format GUIDs. Typed input may use the current assistant-name prefix;
activated voice requires it. `pending question help` shows constant syntax only,
without reading a question or revealing current IDs.

This focuses only the **same already-open native question window** for the exact
currently host-owned admitted wait matching both IDs. The current production
host owns at most one local-version question; Sessions and All work question
metadata cannot select, recover or create another presenter. Missing, closed,
expired, answered, cancelled, stale, foreign or uncertain targets explicitly
report unavailable. There is no latest-question fallback or historical recovery.

Navigation preserves question/options, saved and unsaved answers, original
expiry, revision and session generation. It does not answer, approve, cancel,
resume, replay, submit work, select a session, replace a dirty composer, read
question bodies from storage, refresh the source, speak or copy. Answer separately
through the existing native one-shot controls. Protected calls refuse focus.
Malformed reserved commands are refused before question-answer/model routing.
The separate navigation status does not replace the transcript, current response,
question status or security preview.

## Read the exact selected retained assistant answer

Use `read selected answer`, optionally with the current assistant-name prefix when typed, or `Kora, read selected answer` through activated voice. First explicitly select a retained ASSISTANT answer in the open Sessions or compact conversation ([exact reserved grammar](../src/Kora.Application/Voice/ResponseReadCommand.cs)).

Missing/stale selection clarifies without using the current response or sending anything to a model. It uses the same one-shot operation as **Read selected ASSISTANT answer once** ([workflow and source checks](../src/Kora.Application/Hosting/SessionAnswerReadWorkflow.cs)).

It does not read USER text, drafts, questions or HOST/audit status, change speech settings, shorten an over-cap answer or bypass Text only/protected calls. Cancel, close or source/channel retirement prevents queued replay ([playback guide](responses-and-calls.md#read-one-retained-assistant-answer)).

## Unsent conversation input and legacy recovery

Sessions are ongoing topic conversations with Active/Done lifecycle. Unsent input stays separately in memory for each exact session while switching topics; Sessions and compact views share the same buffer. There is no ordinary draft Save/Reload/Clear, catalogue, search or saved-buffer comparison workflow ([session buffers](../src/Kora/SessionsViewModel.Unsent.cs)).

After selecting an exact session, **Recover existing legacy text into empty input** reads only that session's existing saved text, independently of ordinary conversation reads. It cannot overwrite nonempty unsent input, write/remove legacy data, submit or renew activity ([admission](../src/Kora.Application/Hosting/SessionWorkspaceService.Conversation.cs)).

Legacy data retains existing privacy/retention; no silent migration or purge occurs. Missing or unavailable legacy text leaves history and unsent input unchanged ([regressions](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.Unsent.cs)).

Explicit discard and closing the last view/application protect nonempty unsent input with Discard/Cancel, not Save-as-document. Switching topics preserves text without prompting. Privacy/ownership/session retirement clears affected volatile text without saving ([owner](../src/Kora/SessionsPresentationOwner.cs)).

Voice and typed input use the same exact built-in phrases, which always take
precedence. Voice input may use the configured assistant name as a prefix.
Punctuation and capitalization do not matter for built-in phrases. Once the
verified local model is ready, unmatched requests can receive a local answer;
free-form voice requests must start with the active assistant name.
The alternatives below are explicit phrases, not fuzzy matching: additional
words beyond the phrase do not trigger a built-in action.

If the assistant is renamed, replace Kora with the configured name. The old
name is not retained as a hidden alias.

For the full design-defined catalogue, including capabilities not yet shipped,
see [Tools and built-in skills: current and planned](tools-and-built-in-skills.md).
The exact phrases on this page remain the current-release command reference.

Provider-neutral host controls add no new command or qualified model tool loop.
Existing bootstrap reasoning and unavailable messages remain unchanged;
hosted inference is not enabled by this host-only foundation.
Device-local initial provider mode settings are delivered separately below.
Clipboard and file preview/search commands still perform no model submission.

The separate native [exact-session composer](windows-and-tray.md#exact-session-conversation-and-composer)
retains deliberately submitted original user messages and unavailable HOST/task
outcomes through configured provider-neutral admission. It is not a new typed/
voice command, question reply, approval, fixed-read queue profile, model tool or
bootstrap fallback. Draft saves are explicit; reading/restoring never submits.

## Dismiss or inspect the exact current response

Use `dismiss current response`, `open current response details`, or `current response help`.
Typed input accepts the optional current assistant name. Activated voice requires
that current name, for example `Kora, open current response details`. Extra words,
sources, controls and incomplete forms are refused, not sent to a model.

These commands share the compact window's **Dismiss** and **Current response
details** native controllers. They capture the original ordinary response before
command feedback, never choose a later response, and report success only after
the native window acknowledges hiding or opening that exact revision.
Details contain only that ordinary response's title and body as inert native text;
this route does not copy content or open web, clipboard, file or retained sources.

Questions, approvals, security previews, protected calls, missing/hidden/replaced
responses and revoked privacy, input or ownership are refused without cancelling
or replacing a pending decision. A queued action is rechecked through native
completion. Dismissal, replacement, timeout, privacy closure and disposal retire
current-response details; recovery never resurrects them.
There is no per-response pin/unpin, preference write, model or speech action.
Uncomposed native hosts report unavailable; this does not qualify a model route.

## Read the current response aloud once

Use **Read current response** in the compact response window, `read that aloud`, or `read current response`.
Typed and activated voice input accept the optional current assistant-name prefix, for example `Kora, read that aloud`; extra words are not an exact command ([parser](../src/Kora.Application/Voice/ResponseReadCommand.cs), [native controls](../src/Kora/ResponseWindow.axaml)).
The `read that` and `read current` families are reserved before question/approval and model dispatch. Incomplete, overlong, control-character or extra-argument forms report exact-command recovery without answering/cancelling a pending decision, synthesizing text or invoking inference ([boundary tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.ResponseRead.cs)).

All routes invoke the same [one-shot action](../src/Kora.Tools/Speech/SpeechReadOnce.cs) for the exact current ordinary response identity/revision and observed call policy.
This is not arbitrary TTS, history-row playback, raw clipboard/file/web reading, a question/approval readback, or an automatic replay queue ([host admission](../src/Kora.Application/ViewModels/MainViewModel.ResponseRead.cs)).

Normal speech admission still applies, including VisualOnly refusal, protected Active/Suspected/Unknown calls, owner/privacy/lock, voice/provider, both summary caps, Kora volume and the selected unmuted available output.
The full visual answer is retained on length/output failure. Replacement (even identical text), source close, stop/cancellation, call/privacy/owner retirement and disposal invalidate the invocation; it has a two-minute cancellation bound.
Refresh, unlock, call end and output recovery never retry it ([playback](../src/Kora.Application/ViewModels/MainViewModel.cs), [boundary tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.ResponseRead.cs)).

Push-to-talk preserves an existing eligible current answer while showing capture progress separately; it does not select an older answer. **Stop speaking** remains available during playback.
The model lane remains unavailable with `current-response-model-tool-loop-not-qualified`; the six read-only descriptors and model transport are unchanged ([capture](../src/Kora.Application/ViewModels/MainViewModel.VoicePrivacy.cs), [action boundary](../src/Kora.Tools/Speech/SpeechReadOnce.cs)).

## Open existing skill reviews

| Whole typed utterance, or current-name activated voice | Existing native surface |
|---|---|
| `open skill packages` / `show skill package review` | Verified bundled skill-package review |
| `open shared skill sources` / `show shared skill sources` | Registered shared-skill source review |

Use exactly one phrase, ignoring case, optionally prefixed by the current assistant name for typing; activated voice requires that prefix. Outer spaces are accepted; punctuation, extra words, selectors, paths, controls and input over 1,024 UTF-8 bytes are not. Reserved malformed input clarifies, without selecting another surface ([grammar](../src/Kora.Application/Skills/SkillReviewCommand.cs)).

Reopening activates the existing singleton and preserves its unsaved search/filter state. These commands are distinct from artifact `run`/`use` invocation. They do not answer pending questions or replace security previews; unavailable native binding or retired ownership/privacy/call/voice state reports unavailable ([routing](../src/Kora.Application/ViewModels/MainViewModel.SkillReview.cs)).

Shared sources retain registration-metadata refresh with its own status. Register/folder selection, Discover, Verify and Unregister remain separate native gestures. Navigation grants no consent, enablement, reload, execution, agent-profile qualification or model authority ([controllers](../src/Kora/SkillPackagesWindowController.cs), [refresh](../src/Kora/SharedSkillSourcesWindowController.cs)).

Activated voice opens the shared inspector without becoming native `LocalUi` authority. Its automatic metadata refresh is refused by the existing local-input-only admission; use the inspector's separate native Refresh control. No synthetic local-input origin or successful metadata/discovery claim is substituted ([shared admission](../src/Kora.Application/Skills/SharedSkillAdmission.cs)).

## Inspect or change the device-local provider mode

Native Settings > Providers and exact typed/current-name activated commands:

| Syntax | Result |
|---|---|
| `list provider settings` | Discover `providers.default-mode`, choices/default, provenance and revision |
| `get providers.default-mode` / `status providers.default-mode` | Inspect saved/default/unavailable initial mode |
| `set providers.default-mode to LocalOnly` | Privacy-first initial mode |
| `set providers.default-mode to LocalFirst` | Local-first initial mode |
| `set providers.default-mode to HostedPreferred` | Hosted preference only, no adapter/egress admission |
| `reset providers.default-mode` | Save LocalOnly; existing session policies remain unchanged |

Only first policy-bound session initialization consumes this preference.
Protected/Unknown voice writes refuse; corrupt/unconfirmed state is not a
default. See [Models and provider controls](settings.md#models) and
[preference recovery](settings.md#preference-changes-and-recovery).

## Inspect or change app provider profiles

Native **Settings > Providers** and exact typed/current-name activated commands share app-scoped configuration, not authentication or model execution:

| Syntax | Result |
|---|---|
| `list app provider settings` | Discover desired/saved/effective service, exact current catalogue and scope |
| `get providers.app.profile` / `status providers.app.profile` | Inspect saved and effective tuple/account/catalogue state |
| `set providers.app.default-service to Unset|Local|Copilot|Claude|Codex` | Explicit desired app service only; retain each service's tuple |
| `reset providers.app.default-service` | Local if supported Ollama installation is observed, otherwise Unset; retain tuples |
| `set providers.app.profile to <model-id> <reasoning-id> <context-id> for <account-id> at <catalogue-revision>` | Save the selected service's complete exact advertised tuple, with current opaque account/revision |
| `get/status providers.app.<model|reasoning|context>` | Inspect the same complete app profile and current catalogue |
| `set providers.app.<model|reasoning|context> to <exact-id> for <account-id> at <catalogue-revision>` | Review one field against the current saved complete tuple for the desired app service; retain other exact fields |
| `confirm providers.app.profile <host-issued-confirmation-id>` | Save only that reviewed field and whole complete tuple, with fresh original-user admission |
| `cancel providers.app.profile <host-issued-confirmation-id>` | Discard the review without writing; confirmation cannot be replayed |
| `reset providers.app.profile` | Clear only the selected service's tuple; no automatic replacement |
| `sign in providers.app.service` | Unavailable: no qualified sign-in adapter; no credentials accessed |

IDs are exact, case-sensitive, bounded catalogue values; labels/marketing names are not IDs. The revision is a canonical positive integer. Choose all dependent values explicitly; unsupported/provider-managed values cannot be invented. Complete input is at most 1,024 UTF-8 bytes; discovery output is at most 64 KiB and never truncated.

Individual setters never persist partial profiles or change the desired default service. Model edits preserve saved reasoning/context IDs only when supported by the new model. Missing/incompatible/stale bases require the existing complete tuple draft/save; no default, field reset or silent clearing is offered. Native field review may target another service while retaining the app default. Confirmation IDs are exact lowercase GUIDs issued by this host.

Initially desired Local requires installation observation, not endpoint response, model readiness or generation consent. Missing/unknown installation is Unset. Production services currently advertise no models. Saved desired hosted service does not sign in, enable a provider, approve egress or send a request.

Unscoped and one-turn directives are not app-scope aliases and remain unavailable. Exact session controls below are separate; existing LocalOnly mode/default is unchanged.
Protected/Unknown voice writes, stale previews, ownership/privacy/session changes, cancelled or unconfirmed evidence fail closed. See [app profile settings](settings.md#app-default-service-and-desired-profiles).

## Inspect or change an exact session provider profile

Use **Sessions > Selected-session provider overrides** or exact typed/current-name activated syntax. Session scope is explicit/default for these new mutations, never the app. `<session-id>` and confirmation IDs are canonical lowercase immutable GUIDs, not names or model-supplied destinations.

| Syntax | Result |
|---|---|
| `get providers.session.profile for <session-id>` / `status providers.session.profile for <session-id>` | Saved sparse fields, inherited app values, effective resolved/unavailable state and catalogue provenance |
| `set providers.session.service for <session-id> to Unset|Local|Copilot|Claude|Codex` | Review desired service override; no authentication or execution |
| `set providers.session.<model|reasoning|context> for <session-id> to <exact-option-id> for <account-id> at <catalogue-revision>` | Review one field from the current effective tuple; only this sparse field becomes explicit |
| `set providers.session.<model|reasoning|context|profile> for <session-id> to <model-id> <reasoning-id> <context-id> for <account-id> at <catalogue-revision>` | Review one sparse field or all three dependent fields, bound to exact current advertised model/options/account/revision |
| `reset providers.session.<service|model|reasoning|context> for <session-id>` | Review inheritance for only that field |
| `reset providers.session.profile for <session-id>` | Review **Use app defaults for this session**, clearing all four overrides |
| `confirm providers.session.profile <host-issued-confirmation-id>` | Separately confirm only the exact still-current original-input review |

No exact eligible session means select one; no app fallback. Service resolution precedes inherited tuple resolution, so an overridden service uses that service's app tuple. App preferences are never written. Later app/session edits affect subsequent turns only; stale original input, session/generation/policy/app revision or catalogue/account invalidate review.

Short setters retain the other effective choices and their exact null/explicit sparse state. Review shows both whole effective tuples before separate confirmation. Missing/unresolved tuples, incompatible models or rebinding previous explicit dependents require reset or the complete-profile route; no defaults, dependent clearing or successful-but-ineffective save. Existing full-triple syntax and resets remain available.

Production has no qualified catalogue models/auth adapters, so model-dependent selection remains unavailable; desired service may be saved unresolved. No sign-in, model call, generation, network/egress or envelope increase occurs. Generic “Use Claude and summarize”, ambiguous labels and one-turn language remain deferred. Unconfirmed outcomes require inspection of exact saved policy and audit/control receipts, never automatic retry/reset or a rollback claim.

## Attach one text file to an exact Session (native only)

For a content-free overview, use **Tray > Retained session attachments > Search / refresh metadata**. Default Browse or volatile ExactId observes retained slots, never original paths or bodies.
[Catalogue query rules and exact navigation](windows-and-tray.md#retained-session-attachments-metadata-only) cover canonical IDs, explicit Enter/Clear/Next, bounded empty pages and separate full-byte inspection. No typed/voice/model search or capture command is added.

Exact typed or current-name activated `prepare attachment catalogue <session-id>`
opens/focuses that same singleton native catalogue and prepares **ExactId** and
the exact query ID only. Use one nonempty canonical lowercase D-format GUID;
names, current/latest, corpus queries, extra clauses and malformed IDs refuse
without model/question fallback. `attachment catalogue help` is constant,
read-only syntax help and does not disclose IDs or current filters.
This is **preparation, not search**: no metadata query/count/probe, session
resolution/selection, body/path/source read, capture, inspection or task action
runs. Other native filter drafts remain **AND-composed**, including invalid
drafts. Review all visible filters, then separately choose **Search / refresh
metadata**. Preparation retires prior pages, continuation, selection/details,
Find and open readiness, cancelling/rejecting old callbacks. The supplied ID
grants no ownership, read authority or proof a retained copy exists. Native
focus/preparation must acknowledge the original private host request; stale,
unbound or unavailable presentation refuses, never falls back to Browse.

Open **Sessions**, select the exact Active session, then **Attach text file to this exact Session**. A trusted native picker opens only metadata; review canonical path/native identity, source and host session, bytes/time, fixed limits and local durable retention/copy disclosure. **Confirm: capture and retain this exact file for this Session** is separate persistence consent, not the existing volatile preview confirmation.

One immutable UTF-8 `.txt`/`.md`/`.markdown` file per session; maximum 262,144 original bytes including BOM; sixteen retained files across the private profile; no eviction or silent overwrite. Attach refuses a second file. No typed/voice/model attachment path or confirmation command is delivered.

**Inspect retained attachment / remove Kora copies** reads full inert historical source and same lexical-lines exact citations after restart, including retained Done sessions. Browsing/search never resume a session, renew activity or trigger cleanup. The snapshot does not watch, refresh or re-read the original file.

### Inspect one retained attachment by exact session address

| Exact typed command | Result |
|---|---|
| `session attachment help` | Discover the bounded grammar |
| `session attachment inspect <session-id>` | Open the addressed session's currently admitted retained historical attachment in the same private native inert viewer used by Sessions |

For voice, use the **current** assistant-name prefix, for example
`Kora, session attachment inspect 12345678-1234-1234-1234-123456789012`.
The address must be a complete nonempty canonical lowercase D GUID.
Names (including same-name sessions), selected windows, paths, URIs, digest-only
identity, model targets, extra arguments and mutation verbs are not admitted.
Invalid reserved input never answers a pending question or falls back to a model.

The host observes exact source/revision/item/digest, session generation and
storage revision, then re-resolves that same provenance before native disclosure.
A swap or revision change refuses; it never opens a newer/same-name source.
Active and retained Done sessions are inspectable; Done stays Done.
Private ownership, original input/call/name admission and source lifetime remain
live gates. Protected/Unknown voice input, missing/held/corrupt/stale sources or
current-work/storage holds report unavailable with native recovery, never
recapture or read-original fallback. Finish or discard an existing native file
review explicitly first; inspection cannot silently cancel it.

This command opens an **inspection-only** view: no capture, attach, replacement
or removal controls/consent are granted. Those remain separate reviewed genuine
native gestures through Sessions. Bytes stay in the native inert viewer, never
the command response, transcript, speech/readback, model, log or clipboard.
Opening does not renew meaningful activity, resume, trigger retention/cleanup,
enqueue work or replay a request. No provider, hardware or installed-native
qualification is claimed. See the [source-bound route and evidence](../Design/File_And_Folder_Ingestion.md#delivered-exact-session-retained-attachment-inspection-command---2026-10-20).

Removal reviews the exact session/generation, source/revision/item/digest, storage revision and all owned copies, then requires **Confirm: Remove attachment and Kora copies**. Native use is revoked before secure SQLite body/free-page removal and committed-journal verification. Live/Unknown work and uninventoried copies hold completion; inspect durable state after uncertainty. Original files and unrelated/kept sessions/Perpetual grants remain unchanged.

For an **Active** session with a retained file, choose **Replace retained attachment** from its historical viewer. A fresh picker selects the source; review old identity/digest/captured time/storage and inventory revisions alongside new metadata, persistence and copy-removal consequences.
Use **Confirm: replace this exact retained attachment with the newly selected file** within two minutes. Picking even the same path creates a new source identity; old consent and citations are never reused.
Replacement swaps only this one slot, even at the sixteen-file limit, and shows the new captured historical revision only after verifying old owned copies. Old views/search results are retired; no original path is reread automatically and neither original is modified.
Failure/cancellation before commit preserves the exact old durable record; explicitly inspect it again. A committed but uncertified swap holds both bodies unavailable. Inspect shows metadata-only reviewed removal of the held new snapshot and Kora copies, not refreshed content or restored history.
Live/Unknown work, unregistered copies, stale reviews or uncertain storage/journal block completion. There is no automatic replay or version history. See the [exact replacement and recovery contract](../Design/File_And_Folder_Ingestion.md#delivered-reviewed-retained-attachment-replacement---2026-10-10).

This is not managed knowledge or model ingestion. No body/query/excerpt enters history, preferences, logs, speech, clipboard or provider context. Folder attachments, multiple versions, model use, OCR/vector/egress and installed acceptance remain deferred. [Canonical limits, known formats and lifecycle](../Design/File_And_Folder_Ingestion.md#delivered-durable-single-file-session-attachment---2026-10-10).

## Create and manage reviewed session memories

Native **Sessions > Session memories** and exact typed/current-name activated commands share the [original-user workflow](../src/Kora.Application/Memory/MemoryManagementService.cs). Select an exact active session, choose an allowed classification, enter the exact value, then choose **New session memory from draft**. No existing memory ID is required; Kora issues the identity, lineage and initial revision.

| Exact syntax | Result |
|---|---|
| `memory help` / `list memories` | Discover exact syntax; no inventory or body |
| `memory propose <session-id> <class> "<exact value>"` | Create only a volatile Proposed/Pending user candidate |
| `memory list <session-id>` | Content-free identity/revision/scope/review/retention/creation metadata |
| `memory inspect/get <session-id> <memory-id> <revision>` | Explicit exact body, classification and lineage inspection |
| `memory review <session-id> <memory-id> <revision> accept|reject` | Review the same inspected revision; accept does not admit |
| `memory admit <session-id> <memory-id> <revision>` | Separately admit only the exact Reviewed/Pending revision |
| `memory edit/set <session-id> <memory-id> <revision> <class> "<exact value>"` | Replace, clear review and redact earlier durable body; new inspect/review/admit required |
| `memory disable/forget <session-id> <memory-id> <revision>` | Disable use, or forget content while retaining a non-reusable tombstone |

Use canonical nonempty D GUIDs and displayed positive revisions. Classes are `ExplicitFact`, `ResponsePreference`, `WorkflowPreference`, or `Decision`; classification is not secrecy detection. Values are bounded to 512 UTF-16 code units, 1,024 strict UTF-8 bytes and 2,048 serialized candidate bytes. Double a quote inside the final quoted value. No normalization, truncation or eviction occurs.

After creation, list/select the metadata row, **Inspect exact memory**, explicitly accept/reject its exact content/classification, then separately **Admit reviewed revision**. Proposals and reviewed unadmitted bodies remain volatile; no body reaches disk before admission. The 128-identity bound includes durable shells/tombstones and current volatile proposals. Drafts and unadmitted bodies clear on native closure, session/ownership/privacy changes or restart.

After **List selected session memories**, native filters narrow only that frozen exact active-session metadata snapshot: a complete nonempty canonical D memory GUID (case-insensitive, empty for Any), **Scope** (Any, Unknown, Session, DeviceProfile, Project, Source), **Review** (Any, Proposed, Reviewed, Admitted, Rejected) and **Retention** (Any, Pending, Enabled, Disabled, Forgotten). Any is visibly selected initially and after Clear. Filters combine with AND; invalid GUIDs or undefined enum values show no results with Clear recovery guidance. Invalid row metadata invalidates the entire result and requires explicit List refresh. Scope is metadata, not permission or provider authority: non-session kinds and Unknown truthfully match no valid rows in the current session-only inventory. There is no content or classification search: classification is available only through explicit inspection, not list metadata.

**Exact memory scope identity GUID** optionally matches the listed row's scope identity, independently of memory ID. Enter a complete nonempty canonical D GUID (case-insensitive), or leave empty for Any; partial GUIDs, alternate formats and surrounding whitespace are invalid. It combines with every other predicate, including inclusive revision and Created UTC ranges. Scope identity is snapshot metadata, not permission, device/provider ownership, scope authority, content or mutation authority; it cannot navigate to another scope.

**Snapshot order** starts at **Source order (default)**, preserving the returned list. Choose **Created oldest**, **Created newest**, **Revision lowest** or **Revision highest** with the arrow keys; Tab/Shift+Tab moves between controls.
Ties use ascending canonical lowercase D memory GUID text, compared ordinally, even for newest/highest. Ordering is snapshot metadata, not proof of freshness, completeness or current authority ([ordering contract](../Design/Model_Providers_Memory_And_Knowledge.md#delivered-native-content-free-session-memory-filters---2026-10-14)).

Typing, changing order or clearing filters does not read storage, inspect content, renew activity or authorize an action. Each edit retires selection, inspected content, detail, replacement draft and mutation eligibility, even if the same row remains visible. Select and inspect again before mutation ([native implementation](../src/Kora/SessionsViewModel.Memory.cs)).

**Clear memory filters** resets all filters and Source order, returning focus to the memory ID field. Escape in any memory filter or order control does the same but keeps control focus. **List** retains filters/order while refreshing explicitly and retiring prior action state.
Session/private closure discards these volatile choices ([native controls](../src/Kora/SessionsWindow.axaml.cs)). No preference or typed-command syntax is added.

These controls are visual-only; values are not speech, transcript/history, preferences, diagnostics or provider context. There is no model-origin/conversational **Remember this** trigger, automatic review/admit/use/recall, context attachment, hosted transmission or broader memory scope. Stale/foreign/missing original intent and changed admission refuse; inspect durable state after a receipt failure rather than assuming rollback or retrying automatically.

## Native exact handoff review

Open **Settings > Providers > Review pending exact provider handoff**. This original-user local surface consumes only actual pending host-issued offers; production currently shows unavailable/no pending qualified offer. There is no typed/voice/model command that supplies an arbitrary envelope or fabricates an offer.

Select an exact offer and choose **Read complete exact envelope**. The inert complete preview includes session/task/question/offer identities/revisions, destination/model/catalogue revision, typed reason and every evidence item's lineage/disclosure.

**Approve this exact context only**, **Decline**, **Cancel** and **Remove selected evidence** use the existing workflow. Removal retires the old question/offer and requires a fresh complete read; there is no affirmative preselection or inherited approval. Closing/expiry/cancellation/privacy loss never approves.

Review is local and volatile, with no content logging/persistence, copy/export or speech. Typed outcomes state that runtime/account/final-request-egress gates are unavailable and nothing was sent or executed. No provider, adapter, account, send, fallback, retry or grant is enabled. LocalOnly defaults and per-turn choices are unchanged.

## Trusted local events

Native **Sessions > Quiet routine notices for this run** affects Work/Maintenance deliveries and notice rows only, until **Turn routine quiet off**, **Reset routine quiet to Off**, or restart. Default is Off; no choice is saved.
Status separates hidden/omitted counts from `RoutineSuppressedNoReplay` sources; suppressed sources are not Presented and consume no fatigue budget. Clear authorizes future new notices, not a backlog.

Failures, Attention, mandatory output, required questions/approvals and authoritative work/status/citations/recovery remain unchanged. There is no quiet typed/voice grammar or model tool in this bounded increment.
The exact event commands below still share their existing broker; routine defer cannot revive quiet-suppressed sources. Scheduled quiet hours, proactive speech and multi-session reminders remain unavailable.

An already-open **Sessions** selected work surface shows bounded trusted
local-version queue/question observations and already-verified maintenance
availability. No window opens, focus changes or speech occurs automatically.
Native **Review exact event**, **Dismiss exact event**, and **Defer exact event
15 minutes** share the same broker as these exact typed/current-name activated
commands:

| Exact syntax | Result |
|---|---|
| `event status <event-id> <revision>` | Passive current metadata and explicit delivery/stale/unavailable reason |
| `event review <event-id> <revision>` | Same bounded passive review; not a question answer or maintenance Open |
| `event dismiss <event-id> <revision>` | Reject only that exact event revision |
| `event defer <event-id> <revision>` | Defer fifteen minutes, capped at the original source expiry |

Copy the exact canonical D GUID and positive revision from the current native
row. Names, arbitrary IDs, stale revisions, extra words and old/current-source
changes refuse. Refresh and reselect after a mutation; its revision advances.
Activated input still needs normal current-name activation/voice consent, but
this response is always visual. Pending foreground questions/approvals remain
unchanged; answer them separately. No ambient microphone, model, network,
dispatch, deadline extension, grant or maintenance Check/Open is added.

PresentedNoReplay is status, not another notification. Category-limited,
dismissed, deferred and expired rows do not repeat their summary. Fixed
hourly limits are Work 3, Failure 2, Attention 2, Maintenance 1, with one-minute
spacing. At most eight rows are visible, with omitted counts. Corrupt,
unconfirmed, unavailable or full suppression storage holds the broker; work
inspection remains independent. Restart never reconstructs current events or
replays presented/dismissed/expired effects. Fresh native observation may admit
an unexpired explicit deferral under its original deadline.

## Deterministic local-version queue

Diagnostic-only queue productisation is retired.
Normal Sessions, Settings and command input cannot enqueue or dispatch the fixed metadata reads, reorder records, configure capacity/deadlines, attach prerequisites or create expired replacements.
The six direct read-only capability observations remain available; internal scheduler/store/domain contracts are not general task qualification ([admission boundary](../src/Kora.Application/Hosting/SessionWorkspaceService.Commands.cs), [catalogue](../src/Kora.Core/Tools/ReadOnlyCapabilityCatalog.cs)).

Existing records retain exact IDs, state, receipts and original deadlines. They are not dropped, replayed, automatically dispatched or reinterpreted as success. Shared actual task lifecycle/cancellation controls remain separate ([queue service](../src/Kora.Application/Hosting/SessionQueueService.cs), [task controls](../src/Kora.Application/Hosting/SessionWorkspaceService.Commands.cs)).

| Retained exact syntax | Result |
|---|---|
| `queue help` | Current bounded inspection/cancellation syntax |
| `queue list <session-id>` | Passive bounded exact-session observation |
| `queue status <session-id> <task-id>` | Inspect an existing retained queue receipt; never rerun it |
| `queue pause-status <session-id>` | Inspect current pause/hold metadata; no change |
| `queue cancel` / `queue remove` | Operate only on an exact eligible pending record with displayed generation, queue and entry revisions |
| `queue clear <session-id> <generation> <queue-revision> confirm` | Explicitly clear only that exact pending snapshot, retaining history |

Use `queue help` for the complete exact syntax and existing selected-subset review/confirm/discard operations. IDs and conflict tokens are still validated, not inferred from names or the selected window ([grammar](../src/Kora.Core/Commands/SessionCommand.Queue.cs)).

`session queue cancellation review <session-id> <task-id>[,<task-id>...]` prepares the existing private native review of an explicit bounded pending subset.
It does not commit cancellation; inspect the complete original records and separately confirm **Cancel these pending entries only**.
Missing, stale, previous-run or otherwise ineligible records refuse the whole request without selecting replacements ([preparation](../src/Kora.Application/Hosting/SessionWorkspaceService.QueueCancellationPreparation.cs), [native cancellation](../src/Kora/SessionsViewModel.QueueCancellation.cs)).

Native cancellation-review opening preserves the original session's unsent input in memory, without saving or a discard decision ([session buffers](../src/Kora/SessionsViewModel.Unsent.cs)).

## Prepare an exact local diagnostics plan

This workflow is retired. `session diagnostics plan` is rejected as reserved session input, not sent to a model, and opens no native plan. Use direct version, capability, readiness or runtime status queries instead ([command parser](../src/Kora.Core/Commands/SessionCommand.cs), [retirement tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.DiagnosticRetirement.cs)).

## Fixed queue settings

Diagnostic-only capacity, slots and deadline settings are no longer normal Settings or command controls.
Former fixed-read settings phrases report retirement without changing saved preferences, queued records or captured deadlines.
Internal configuration parsing and scheduler policy remain fail-closed ([input boundary](../src/Kora.Application/ViewModels/MainViewModel.cs), [internal configuration](../src/Kora.Application/Configuration/SessionQueueConfigurationService.cs)).

## In-call feedback override

Native **Settings > Calls** and exact typed/current-name ACTIVATED commands
share one device-local, original-input host workflow:

| Exact command | Result |
|---|---|
| `list call feedback settings` | One admitted descriptor: choices/UI default, scope, saved/default/unavailable provenance, desired/effective/applied mode, revisions, timing, hard gates and recovery |
| `get calls.feedback-mode` | Current preference and independent output policy |
| `status calls.feedback-mode` | Same bounded status |
| `set calls.feedback-mode to Voice` | Save only the in-call feedback preference |
| `set calls.feedback-mode to UI` | Save UI-only preference |
| `set calls.feedback-mode to Both` | Save combined preference |
| `set calls.feedback-mode to Inherit` | Restore ordinary response selection during calls |
| `reset calls.feedback-mode` | Remove only this override; unsaved UI default |

Use the current configured prefix, not an old alias. Complete input/result
bounds are 1,024 UTF-8 bytes/64 KiB. Extra words, unknown targets, controls and
numeric/ordinary-mode aliases clarify locally; no model settings tool is added.
Active/Suspected (including manual Active) alone applies this before ordinary
output. Unknown/invalid evidence withholds speech and keeps full visual
recovery; Clear/Unavailable uses ordinary selection. Voice/Both never bypass
independent call speech suppression, privacy/lock/mute/capture/lifetime/safety
gates, or enable input/consent/grants.

Protected-call original-voice set/reset is refused, never relabelled by a later
click or deferred until clearance. Initiate a new eligible UI change. Host-held
choices and current name/input/call/configuration/session/native lifetime are
revalidated. Audits, atomic save/readback and durable intent outcome precede
activation; stale/corrupt/unconfirmed state stays unavailable with explicit
recovery, including across restart. No capture, detector, autoplay or replay.
See [Settings recovery](settings.md#in-call-feedback-override).

## Explicit local file preview

| Exact command | Result |
|---|---|
| `preview file` | Native picker then metadata-only review of one local UTF-8 text/Markdown file |
| `clear file preview` | Discard volatile review/revision, never the original |
| `search file` / `inspect file` | Focus admitted native query/inspection; no arguments or implicit source |
| `preview folder` | Native picker and complete immediate-file inventory review |
| `search folder` / `inspect folder` | Focus exact admitted folder retrieval |
| `prepare file search for "<query>"` / `prepare folder search for "<query>"` | Prepare the exact current preview's native lexical query only; **REVIEW NEEDED**, then click native Search separately ([raw grammar](../src/Kora.Core/Context/PreparedLocalSearchCommand.cs)) |
| `clear folder preview` | Discard volatile folder review/revision |
| `refresh file` / `refresh folder` | Retire old preview, fresh exact metadata review and separate native confirmation |

Only separate native confirmation authorizes content reads; commands, paths,
IDs and document instructions cannot confirm. Reviews expire after two minutes.
Refresh requires the same original physical identity; unsafe/replaced/missing
sources need the picker again, never silent rebinding.

| Limit | Value |
|---|---|
| File | Fixed drive; strict UTF-8 `.txt`, `.md`, `.markdown`; 256 KiB original bytes including BOM |
| Path | 240 characters / 32 components |
| Folder | 1-32 immediate files, 1 MiB combined; no recursion or partial admission |
| Native query | 256 UTF-16 characters / 512 UTF-8 bytes; 32 unique terms, 64 characters/term |
| Results | Eight exact excerpts / 16 KiB UTF-8; each at most 2,048 UTF-16 characters / 128 lines |

Queries execute only in native search, not conversation. Results carry exact
source/revision/item/digest and end-exclusive line/column/range citations.
Search never rereads the path. Privacy/ownership/call/source/query retirement
suppresses late results; clear/cancel/close never modifies originals.
No clipboard, speech/history, model/egress, watcher, vector index or durable
folder attachment is added. Durable single-file attachments have separate
consent. See [native file workflow](windows-and-tray.md#native-local-file-inspection)
and [privacy](privacy-safety-and-logs.md#local-file-inspection).

Preparation accepts original typed input or exact current-name ACTIVATED voice, before shared normalization. Double `"` in the final quoted literal (`"A ""quote"""` means `A "quote"`); case, punctuation, Unicode and interior spaces stay exact.
Existing lexical/single-line bounds apply; malformed reserved `prepare file/folder` input refuses without question/model/approval fallback ([parser](../src/Kora.Core/Context/PreparedLocalSearchCommand.cs)).

An explicit already-confirmed singleton file or complete folder preview must match the requested kind. Admission freezes its immutable reference, including every folder item, and rechecks source, current name, input, privacy, ownership, call and voice eligibility at queued UI delivery.
No path, name or selected-window guessing occurs; stale/closed/replaced previews refuse ([host handoff](../src/Kora.Application/ViewModels/MainViewModel.PreparedLocalSearch.cs)).

The existing inspection window opens/focuses with **PREPARED / REVIEW NEEDED**, lexical mode and no result claim. Typing, focus and preparation execute nothing. Only the separate native Search button reaches the existing canonical audited executor; ActivatedVoice is never relabelled LocalUi.
No read, metadata capture, refresh, Attach, source-use consent, model/provider, clipboard, retention or query persistence is granted ([native hook](../src/Kora/LocalFilePreviewWindow.PreparedSearch.cs), [executor guard](../src/Kora.Application/ViewModels/MainViewModel.Files.cs)).

The query is volatile native input, never a response, transcript, speech, log/audit field or Activity tag. Late/reused handoffs expire and repeated preparation clears old citations without searching. Retained attachments, native Literal Find and existing invalid `search file <query>` / `search folder <query>` forms are unchanged
([deterministic host tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.PreparedLocalSearch.cs), [native tests](../tests/Kora.Windows.IntegrationTests/PreparedLocalSearchWindowTests.cs)).

**Literal Find is native-only**, not a typed/PTT command or model tool. In the inspected original file use **Find first / Find next / Find previous / Clear literal Find**. **Match case** defaults checked (ordinal); unchecked uses ordinal ignore case in this original-file Find only. Changing it clears the result without I/O and requires a separate **Find first**. Exact spaces/punctuation, nonoverlapping steps and no wrap apply; only one original-source match is disclosed. Existing inspect/search phrases still focus ranked inspection, without accepting a query or selecting a new file; model retrieval is unchanged.

See [native Find](windows-and-tray.md#find-a-literal-in-the-currently-inspected-original-file). Shared 256 UTF-16 / 512 strict UTF-8 bounds apply without lexical token rules; unsafe controls, malformed Unicode or empty input reject. Retained Find revalidates the stored original, never its filesystem path. Queries remain native-only, not transcripts/history/model/log/clipboard data.

## SQLite diagnostic retention

Native **Settings > Logging** and exact typed/current-name ACTIVATED commands
share one independent host-admitted workflow:

| Exact command | Result |
|---|---|
| `list logging settings` | Both independent diagnostic/audit options' integer schema/default/bounds, saved/effective provenance, revisions, timing, exclusions and recovery |
| `get logging.sqlite-diagnostic-retention-days` | Inspect the confirmed preference or explicit unavailable recovery |
| `status logging.sqlite-diagnostic-retention-days` | Same bounded current status |
| `set logging.sqlite-diagnostic-retention-days to 14` | Canonical integer 1–365; applies only to newly committed ordinary SQLite records after required audit and atomic save/readback/receipt |
| `reset logging.sqlite-diagnostic-retention-days` | Remove only this override; future commits use default 30 |

Use **Kora,** or the current configured prefix, not an old alias. The complete
input is at most 1,024 UTF-8 bytes and output 64 KiB; controls, ambiguous/extra
words, noncanonical integers and unrelated logging targets clarify/reject
locally. No natural-language/model mutation or guessed setting is admitted.
Original-channel host/session/generation/privacy/call/input and expected
revisions are fresh-checked. Pending exact questions/approvals remain intact.
Protected-call original voice mutation is denied, never deferred or relabelled.

**Existing deadlines stay exactly unchanged. Apply-now/immediate deletion is
unavailable.** No set/reset triggers pruning. Independent audit retention,
daily files 30 days/30 files, session/chat/history, grants/approvals and cleanup
schedule are unchanged. Perpetual grants have no time expiry/retention/eviction.
See [settings and explicit recovery](settings.md#sqlite-diagnostic-retention).

## Future-only audit retention

Native **Settings > Logging** and exact typed/current-name ACTIVATED input
share the independent audit workflow:

| Exact command | Result |
|---|---|
| `get logging.audit-retention-days` | Complete saved/default/effective policy, 30–365 bounds/default 90, revision, timing/exclusions and explicit held recovery |
| `status logging.audit-retention-days` | Same bounded current status |
| `set logging.audit-retention-days to 180` | Canonical integer 30–365; changes NEW required authority audit and independent diagnostic audit projections after required prior-policy receipts and atomic save/readback/intent outcome |
| `reset logging.audit-retention-days` | Removes only the audit override; future audit commits use 90 |

Use the current assistant prefix, not an old alias. `list logging settings`
includes this option alongside independent ordinary SQLite days. Inputs remain
bounded to 1,024 UTF-8 bytes and complete output to 64 KiB; malformed reserved
commands stay local and never route to inference. Original owner/session/
generation/privacy/topology/input/call/native-lifetime and proposal revisions
must remain eligible; protected-call original voice mutation is denied.

Existing deadlines/payloads/citations are unchanged. Apply-now, immediate
deletion, pruning and cleanup triggers are unavailable. This is not a grant
lifetime control: every grant and its validity/scopes remain, and Perpetual
records have no expiry/retention/eviction. No session/history/approval/task/
question pruning or ordinary/file policy changes. See
[held evidence and restart recovery](settings.md#future-only-audit-retention).

## Assistant display / PTT command-prefix setting

Typed input and explicitly activated voice use the same host configuration
workflow as **Settings > Speech & audio > Apply name / Reset name to Kora**:

| Exact command | Result |
|---|---|
| `list assistant settings` | Schema/default/bounds/scope/effect/timing/reset and current revision/provenance/recovery for the one admitted setting |
| `get assistant.name` | Current display/PTT prefix and saved/default provenance, or explicit unavailable recovery |
| `set assistant.name to Nova Prime` | Validate with the existing name rules and atomically save one device-local value |
| `reset assistant.name` | Restore Kora only; no durable identity, session-name, grant, approval, data-path or namespace reset |

The [same parser](../src/Kora.Application/Configuration/AssistantNameCommand.cs) also admits these whole-utterance natural commands, case-insensitively with an optional current-name prefix:

| Natural command | Same existing operation |
|---|---|
| `what is your name` or `what is your name?` | Get |
| `call yourself <name>` | Set |
| `change your name to <name>` | Set |
| `reset your name` | Reset |

Use the old **currently configured** prefix for the rename, for example **Kora, call yourself Nova Prime**; only after a successful commit use **Nova Prime, what is your name?**. There is no fuzzy, substring, old-prefix or generic agent-identity rename route ([parser and consumer tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.AssistantNameConfiguration.cs)).

Natural set grammar uses the literal spaces shown above and preserves the entire raw proposed name, including case, Unicode and whitespace, until the [existing service](../src/Kora.Application/Configuration/AssistantNameConfigurationService.cs) applies authoritative normalization/validation.
Empty proposals or malformed recognized grammar clarify locally; invalid domain names are audited denials, never repaired or sent to inference. Extra punctuation is not accepted for get/reset except the one optional query `?` ([grammar tests](../tests/Kora.Application.UnitTests/Configuration/AssistantNameCommandTests.cs)).

The spoken target **assistant name** is equivalent to `assistant.name`.
The active configured prefix is optional: **Kora, set assistant name to Nova**.
After success use **Nova, get assistant name**; Kora is no hidden alias.
Input is bounded to 320 characters after prefix removal, and names retain
their existing exact case/Unicode validation with whitespace trimming/collapse.
Unknown targets and malformed grammar clarify locally; invalid names are
audited denials, never inference or arbitrary configuration execution.

Set/reset require current host ownership/privacy and configuration/call
revisions. Protected/unknown calls reject original voice requests. These
commands do not answer, replace or approve a pending exact question/approval.
Mutation retires stale capture/transcript/completion generations and does not
replay input or re-enable listening. Explicit **Enable listening** and a new PTT
are required. Grammar-start failure is visible with no old-prefix fallback.
Corrupt saved state disables prefix routing; native recovery and unprefixed
get/set/reset, stop/cancel and Settings remain available.
This is **not production wake-name capability**, a model settings tool, an
acoustic acceptance result or completion of all R10.

## Exact cached release maintenance

Typed input and explicitly activated voice use the same guarded native
maintenance workflow:

| Exact command | Result |
|---|---|
| `maintenance status` | Cached channel/status, last verification/staleness and review/snooze readiness; missing or failed checks remain Unknown/error, not "up to date" |
| `maintenance review` | Review the exact fresh host-held canonical record/source/architecture/digest/unsigned disclosure and reveal the existing native review window |
| `maintenance snooze` | Snooze only that fresh, reviewed Available notice for 24 hours within this run; no persistent reminder policy or other prompt change |

The optional **Kora,** prefix uses the current configured name. The complete
original input is at most 128 characters; phrases are case-insensitive with
outer whitespace trimming, not fuzzy punctuation/extra-word matching.
Complete visual results are at most 8,192 characters; oversized results fail
explicitly, not by truncation. No command speaks, probes devices, activates
capture, invokes a model/provider or treats release text/URLs as authority.

Use native Maintenance to grant **public metadata checks for this run**, check
or explicitly open an already reviewed canonical page. These cached commands
never check/refresh, grant/renew consent, open a browser, download, install,
elevate or activate source. Stale or failed metadata requires a deliberate
native check after admission recovery, not automatic command fallback.
Commands preserve the complete current exact question/approval and cannot
answer it. Unknown ownership/privacy, protected/unknown calls, stale/foreign
records, changed original voice generation/session or failed required audit/
receipt deny success and require a fresh explicit request. This is bounded
command parity, not a general notification broker, production wake or
installed/native/runtime acceptance.

## Exact current-run manual call control

Native **Settings > Calls** and typed/already **activated** voice share:

| Exact command | Result |
|---|---|
| `list call settings` | Complete schema, default-off, run-only scope, timing, reset and cached policy/source revision |
| `get call.manual-active` / `status call.manual-active` | Manual flag, independent automatic observation/availability, effective conservative protection and saved flags |
| `set call.manual-active to on` | Enable only the current-run manual layer |
| `set call.manual-active to off` | Clear only that layer; never assert detector Clear |
| `reset call.manual-active` | Same manual-off effect; no saved preference reset |

The optional prefix is the current installed display/PTT name, not a hidden
old-name alias or production wake. Grammar is case-insensitive with outer
whitespace trimming. `on`/`off` are the only set choices; extra words, controls,
unknown call targets and inputs over **1,024 UTF-8 bytes** clarify locally.
Complete versioned JSON is limited to **64 KiB**, never truncated.
These reserved routes run before pending-question input. Inspection is passive
and cached: no control intent, meaningful activity, approval, detector probe,
model, speech, capture or unrelated effect. Results appear in the transcript/
native cached status without replacing the full question, approval or preview.
Mutation is refused while a question/approval is pending; it cannot answer,
approve, rebind or defer a change until clearance.

Set/reset require original input captured before asynchronous work, genuine
host-created committed intent/session generation, own live request context,
current source/policy revision and current ownership/privacy/input gates.
Protected/Unknown calls deny original voice mutations, including off/reset;
a later mouse confirmation cannot relabel that voice input. Start a new
eligible native/typed request instead.
Changed manual state fences old synthesis/playback, input and callbacks;
old requests may finish visually but cannot synthesize/replay after off/reset.
Capture stays closed until a separate fresh **Enable listening** and PTT.
No consent, microphone, saved call flags, grant record, output mode or volume
is changed. Mandatory full visual information remains intact.

Required typed authority audits precede the fenced process-memory transition
and record its truthful outcome on the existing shared SQLite lease. This is
not atomic persistence of the manual flag. A lost audit/receipt or unconfirmed
retirement reports **not-confirmed** and retains explicit conservative
evidence-unavailable protection; it never claims rollback, retries, or uses
unknown evidence to authorize less protection. Startup remains manual-off
with honest detector-unavailable status. Automatic detection, speak-once,
protection relaxation and full R15/native/acoustic acceptance remain absent.

## Read-only host discovery

These exact local commands require the active, unlocked Kora host and do not
invoke a model, install anything, or refresh probes:

| Exact command | Result |
|---|---|
| **list capabilities** / **capabilities.list** | Six admitted read-only descriptors with schema version, typed input/output shape, read-only effect, caller lanes, availability and limits |
| **describe capability** followed by a canonical ID / **capabilities.get** followed by that ID | The single admitted descriptor; unknown IDs are denied |
| **show registry version** / **application.get_version** | Actual running version; deployment information is explicitly not observed by the current provider |
| **show dependency readiness** / **readiness.get** | Recorded dependency observations and timestamps; unobserved dependencies are explicit |
| **list runtimes** / **runtime.list** | The existing local inference adapter's recorded status, not a catalogue of planned providers |
| **show local runtime status** / **runtime.get_status** | The same `local.inference` observation; tool-loop qualification remains false |

Optional configured-name prefixes and normal exact-command punctuation/case
handling apply. For example, **Kora, describe capability runtime.get_status**.
The complete serialized UTF-8 response is at most 4,096 bytes; lists contain
at most six records. The host API accepts only `{}` for version,
`{"offset":0,"count":6}` (both fields optional) for lists, and a required
`{"id":"..."}` for descriptor/runtime lookup. Unknown fields, duplicate fields,
invalid ranges, foreign/expired host context and unknown lanes are denied.
The native commands supply those inputs deterministically, rather than accepting
arbitrary JSON from the command box.

These results do not contain raw probe details, paths, endpoints, credentials,
model content, skill instructions, or execution tools. Observations are cached:
use the existing Readiness UI for a deliberate fresh check and recovery details.
An observed ready dependency does not grant execution permission. No hosted
provider, MCP adapter, or model tool/result loop is delivered by this registry.
Existing help, version, setup/status, lock and power phrases retain their behavior.

## Exact input-device preference

Typed input and **activated** voice use:

- **list input settings** — metadata-only discovery with the existing
  five-second single-flight deadline;
- **get speech.input-device** — recorded desired/effective choice, source,
  metadata/call revisions, availability/readiness and recovery;
- **set speech.input-device to {exact listed endpoint ID}** — preference only;
- **reset speech.input-device** — explicitly selects `system-default` (System).

The configured-name prefix is supported. Option grammar is case-insensitive;
endpoint IDs are exact and case-sensitive. Friendly names, indices, fuzzy
targeting and natural-language aliases are not selectors. Duplicate friendly
names are distinct IDs. System follows the Windows multimedia default;
unavailable pins remain pinned. Complete versioned JSON results are at most
64 KiB; original commands are at most 1,024 UTF-8 bytes without controls.
Oversize results are rejected, not partially presented.

These routes share the native Settings/tray/recovery-card preference workflow.
Selection/reset never enables listening, grants consent/permission, tests audio
or changes model/OS settings. Changed selection closes stale input; manual
disablement and recovery holds remain closed. Protected/unknown calls reject
original voice mutations; stale metadata, unknown ownership/privacy/permission,
pending questions/approvals and failures require a new explicit operation.
Terminal audit failure may follow a file replacement: inspect before retrying,
not automatic rollback/replay. There is no model tool or pending-question bridge.

## Explicit local clipboard preview

These exact commands and the tray's **Preview clipboard (local plain text)**
entry use the same request workflow without a model:

| Exact command | Result |
|---|---|
| **preview clipboard** / **preview the clipboard** / **snapshot clipboard** | Read one fresh bounded Unicode plain-text snapshot and open its immutable native preview |
| **explain clipboard** / **explain the clipboard** | The same local preview, with explanation explicitly unavailable |
| **reuse clipboard snapshot {exact snapshot ID}** | Reopen/select that same snapshot only; never reread or silently substitute changed clipboard text |
| **clear clipboard preview** / **revoke clipboard snapshot** | Discard Kora's snapshot and preview, not the Windows clipboard |

**clipboard snapshot status {snapshot-id}** reports only whether that exact
currently admitted immutable snapshot is present, its host snapshot ID and its
existing UTF-8 byte count. **clipboard snapshot help** shows syntax without
revealing the current ID. These commands require single ASCII spaces; status
requires a nonempty lowercase `D` GUID (`xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`), without
braces, punctuation repair, extra arguments or trailing whitespace. Command
words are case-insensitive. Voice requires the current assistant-name prefix.
Malformed reserved `clipboard snapshot` input is rejected locally before
questions, approvals or model routing.

Status is transcript-only, with no automatic speech or response/pending-panel
replacement. Missing, replaced, revoked or ineligible snapshots are explicitly
unavailable, not an empty-clipboard claim. Status never reads or refreshes the
system clipboard, reuses/revokes a snapshot, extends review lifetime, or admits
source use. No source text, excerpt, fingerprint, capture time, native read
version, source ID or inferred sensitivity is reported.

Configured-name prefixes and the normal exact-command case/punctuation rules
apply to the older preview/reuse/revoke commands. The preview shows the host source/snapshot IDs, `CF_UNICODETEXT` format,
read version, capture time and exact UTF-8 byte count. Its **Reuse this exact
snapshot ID** button uses the displayed ID; **Revoke and clear**, closing the
preview, cancel, privacy/ownership loss, call-policy change and host exit clear
or suppress it. A new capture replaces the prior selection with a new ID.

**Literal Find is native-only**, not a typed/voice command or model tool. Use **Find first / Find next / Find previous / Clear literal Find** in the already captured preview; no clipboard reread, reuse/revoke action or activity renewal occurs.
Exact case/spaces, one-line strict Unicode, 256 UTF-16 / 512 UTF-8 limits and visible invalid-input rejection apply. See [native clipboard Find](windows-and-tray.md#local-clipboard-preview) and its [contract](../Design/Security_Data_Flows.md#delivered-r07-local-clipboard-preview---2026-10-07).

The whole text must fit 256 KiB UTF-8, with valid paired Unicode surrogates.
Empty, unsupported, oversize, busy, denied, changed-version and malformed
reads are explicit; there is no truncation, queued retry, background watcher,
URL fetch, HTML/image/file capture, clipboard write, history or persistence.
Whitespace and line endings are preserved. Text is untrusted and may contain
secrets; preview/reuse is neither execution authority nor transmission approval.
Clipboard explanation is unavailable until qualified local tool-loop and
clipboard-answering gates pass. Nothing reaches the current JSON selector,
Ollama or a remote provider. See [privacy](privacy-safety-and-logs.md#clipboard-snapshots).

## Window and application tasks

### Retrieve an explicit web page

Say or type:

- `get web page https://example.com/page`

This is an original-user request for that exact absolute HTTP or HTTPS address.
Kora retrieves only public-network UTF-8 plain text or HTML, follows at most
five redirects, and rechecks DNS and authorization for every redirect. It does
not send cookies, ambient credentials, proxy credentials, referrers, or a model
conversation. HTML is returned as bounded normalized text; scripts, styles,
markup and external resources are not executed or loaded.

After success, select **Open exact web-result details** in the actual response. This opens the same immutable, volatile native plain-text result, not the website. Search, exact source, reopen and close perform no additional network request or navigation.
The viewer shares the existing eight-window limit and private copy requires explicit disclosure confirmation.

Complete source provenance shows requested/final addresses, media type, redirect count, retrieval time, truncation and the SHA-256 of **exact returned normalized text**, not original HTML or current server content.
Limits are 256 KiB raw content / 60 KiB normalized text / 64 KiB serialized tool result; complete native source including metadata is bounded to 256 KiB UTF-8 with explicit failure, not further truncation. Empty successful text stays empty.

Only the current result is retained in memory. A later response, dismiss/response close, cancellation, privacy/ownership/call/control change or disposal retires its reference and viewer/copy access; an old button never selects a newer page.
Closing details alone changes only presentation and allows reopening while that source is still current. No page content is saved, spoken, captioned, logged, exported or sent to a model.

The canonical future model tool is `network.get_web_page`, but its model-facing
descriptor remains unavailable until Kora's parameterized tool/result loop and
durable approval resumption are qualified. Preapproved address patterns will
apply through the normal grant model when that route becomes available.

### Open the existing New request composer

- Typed: **open new request**, optionally prefixed by the current assistant name.
- Activated voice: **Kora, open new request**, replacing Kora with the configured name.

This is navigation into the existing [singleton native composer](windows-and-tray.md#new-request-with-a-fresh-session), not a new conversation or submission. Reopening preserves its exact memory, reviews, held state and active operation; it never selects a target, transfers text, saves, submits, resumes work or renews session activity ([controller](../src/Kora/NewRequestWindowController.cs)).

Use the exact phrase without punctuation or trailing arguments. Malformed reserved input does not become a question answer or model request. A conflicting assistant name such as **New Request** requires explicit renaming ([parser](../src/Kora.Core/Commands/NewRequestOpenCommand.cs), [admission](../src/Kora.Application/ViewModels/MainViewModel.NewRequest.cs)).

Resolve the current question or approval first. Privacy, ownership, call activation and original-input restrictions apply; stale queued opening is refused. Later Create/Submit require their own native gesture/review. Model-facing opening is unavailable: this event is not a registered model tool ([navigation](../src/Kora.Application/ViewModels/MainViewModel.NewRequest.cs)).

### Inspect existing minimal durable sessions

- **open sessions**

The configured-name prefix is supported. This expands the same primary
[interaction window](windows-and-tray.md#expand-and-collapse-the-interaction-window) as the tray
and compact response's **Ctrl+Shift+S**. It does not change a pending question
or approval target, create a conversation, resume a session or call a model.
Done/resume are explicit selected-ID native actions, not inferred from words
in history or from selecting a row.
The separate **Session management** workspace offers **Create empty Active session** and **Rename
selected ID** with bounded durable names and optimistic revisions. These are
also available through the bounded exact commands below, but not model tools. A name never selects authority,
and the selected window never redirects global commands. Creation grants no
execution permission; rename/browse never resumes or changes approvals.
Native Sessions additionally offers separate
[preview/confirmation for logical disposition](windows-and-tray.md#logical-session-disposition).
It removes addressed live authority rows, not recoverable copies or full
history. No typed/voice `session delete` or model deletion tool is admitted.

### Review one exact session's logical disposition

`review session disposition <canonical-D-guid> <positive-generation> <nonnegative-metadata-revision>` opens the existing private native review for that exact observed identity and revisions. Typed input or voice activated with the **current** assistant name may launch it; names, selected rows and the current question are never selectors ([grammar](../src/Kora.Core/Commands/SessionDispositionReviewCommand.cs)).

The complete native preview displays the exact ID, label, generation, metadata revision, all eight inventory counts and the existing logical scope, retained tombstone/provenance and exclusions. The response only reports whether review opened; private inventory is not spoken or sent to a model ([shared preview](../src/Kora/SessionsViewModel.cs), [routing](../src/Kora.Application/ViewModels/MainViewModel.SessionDispositionReview.cs)).

Nothing is removed by the command. **Confirm logical disposition** remains a separate deliberate native action with a fresh native request. `confirm session disposition ...` explains this boundary and never confirms; `session delete` and model deletion tools remain unavailable ([native-only one-shot confirmation](../src/Kora.Application/Hosting/SessionWorkspaceService.Disposition.cs)).

Stale generations/revisions, lost private/native ownership, busy or unbound presentation, cancellation and replaced previews require a fresh review. Review navigation preserves unsent input; the command never saves, submits or discards it ([native launch](../src/Kora/SessionsWindowController.cs), [tests](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.DispositionReview.cs)).

### Bounded exact-ID session commands

Typed input and **activated** voice share one grammar, with the configured
assistant-name prefix supported. Use **session help** for the full syntax:

| Command | Actual bounded result |
|---|---|
| `session list [after <exact-id>] [limit <1-50>]` | Active/Done IDs, names, authorization generations and metadata revisions |
| `session status <exact-id>` | Exact authority and optional durable name; not inferred runtime progress |
| `session inspect <exact-id> [tasks\|questions] [after <exact-id>] [limit <1-50>]` | A page of existing task IDs/request IDs/states/revisions/origins or question IDs/revisions/states |
| `session create "<name>"` | Empty named Active session with a fresh host-owned ID |
| `session rename <exact-id> <generation> <metadata-revision> "<name>"` | Rename only, including a Done session |
| `session done <exact-id> <generation>` | Guarded idle lifecycle transition; not cancellation or proof of success |
| `session resume <exact-id> <generation>` | Explicit Active transition; never reruns work or revives approvals |
| `session history <exact-id> [after <generation>:<snapshot>:<sequence>] [limit <1-50>]` | Passive ordered host interaction history with stable exact citations; no control intent or activity extension |
| `session get <exact-id> <event-id>` | One exact history event belonging to that session, or explicit unknown; never name lookup or replay |
| `session search <exact-id> [after <generation>:<snapshot>:<sequence>:<query-digest>] [limit <1-50>] "<query>"` | Passive lexical search of committed retained fields, exact receipt citations and explicit bounded-scan gaps/omissions |

IDs must be nonempty canonical hyphenated GUIDs. Revisions are unsigned decimal
integers (generation positive, metadata revision zero for absent legacy metadata).
Copy the exact observation; a stale revision fails and requires a fresh request.
Names are NFC single-line Unicode, at most 120 scalars and 480 UTF-8 bytes,
without surrounding whitespace or control/format characters. Names must be
quoted; double an interior quote, e.g. `session create "A ""quoted"" label"`.
No punctuation stripping, name lookup, ordinal/window selection or fuzzy matching
applies to this grammar. Whole input is limited to 1,024 UTF-8 bytes, pages
default to 25/max 50, and the complete structured JSON result is at most 64 KiB.
Overflow fails explicitly rather than truncating. List/inspect cursors are exact IDs, not
saved snapshots; refresh for concurrent changes. History continuations instead
use the returned generation, snapshot ceiling and last sequence, separated by
colons. They remain bound to that exact session and exclude later appends.
Lifecycle changes require a fresh history read. Lifecycle results omit
unobserved metadata; request status to observe it.

Each accepted command has fresh original-user lineage. Except for passive
`history/get/search`, legacy controls retain a durable host control
intent/terminal receipt; reads do not change lifecycle, metadata, question or
grant authority. Existing partitions must be present. Errors are explicit:
refresh after conflict, resolve live/Unknown work or pending questions, or recover
private storage/ownership before a new deliberate request. A receipt failure
after a commit is not rollback; inspect current state before retrying.
The typed Run entry remains available for this deterministic namespace while
bootstrap work is busy; it does not cancel that work. Mutations still pass the
same exact-subject live-work and current host gates, not a new executor lane.

History records only committed host question snapshots/final answers, decision
metadata and task-state receipts. Gaps/current migration baselines, metadata-only,
over-budget unavailable content and disposition-redacted events are explicit.
Bootstrap user/model messages and response bodies are unavailable, not recovered
from logs. A task success is not proof of an external effect. Done histories
are readable; an exact Removed ID returns redacted citations only. This receipt
lane has no composer, model history reasoning, Ask Evidence, broad export, attachment,
queue, scheduler, automatic resume or playback. History does not import volatile
file previews/shared skills or reconstruct captions. See the
[native workflow](windows-and-tray.md#bounded-passive-interaction-history).

The separate [exact-conversation search](#search-one-explicitly-addressed-retained-conversation) and native [selected-conversation search](windows-and-tray.md#search-one-exact-retained-conversation) search actual retained USER bodies and typed HOST outcomes.
This older `session search` command remains Question/Answer/task receipt `HistorySearch`; it is not implicitly remapped to body search.

Native transcript search additionally accepts optional inclusive observed message Generation (positive Int64) and Revision (1-2) min/max, AND with its existing predicates. Empty means Any; strict decimal input is not trimmed or repaired. This adds no command syntax, routing, submission or hidden body read ([range contract](../src/Kora.Core/Conversation/ConversationRangeFilter.cs)).

Search uses the [shared lexical query rules](../src/Kora.Core/Context/LocalFileRetrievalPolicy.cs): 1-256 UTF-16 characters, at most 512 strict UTF-8 bytes, 1-32 distinct words of at most 64 characters, NFC/invariant case and literal whole-word OR matching. Punctuation separates words, not operators, wildcards or regular expressions. Quotes inside the final quoted query are doubled.

### Search one explicitly addressed retained conversation

Typed input and current-name activated voice share the distinct `conversation search` family:

```text
conversation search <session-id> -- <verbatim query>
conversation search next <session-id>
conversation search cancel <session-id>
Kora, conversation search abcdef12-1234-1234-1234-123456789012 -- café
```

Use a complete nonempty canonical lowercase D-format session GUID, never a name, selected window, partial ID or model-supplied destination. Voice requires the current assistant-name prefix.
The query is everything after the single space following `--`, including any additional leading/trailing spaces, case, quotes, Unicode or later `--`. It is not trimmed or repaired.
The canonical lexical limits above apply; empty/invalid input refuses before body reads, never Browse, a question answer or model fallback.

One original-user action calls the same [canonical search service](../src/Kora.Application/Hosting/SessionConversationService.cs) as native search. Active and retained Done are readable without Resume.
Native Sessions and compact search additionally retain inclusive submitted-date bounds and an observed task-state choice with explicit Any. These AND-compose with the query and role, bind the continuation, and require explicit fresh Search after editing; typed/voice syntax is unchanged ([native search](../src/Kora/SessionsViewModel.ConversationSearch.cs)).
Results are inert chronological original USER messages and separate typed HOST outcomes with exact session/generation/request/task/sequence/revision provenance, not generated snippets.
Each page returns at most 25 messages, scans at most the existing 100-message session bound and preserves the complete serialized 64 KiB limit, counters and source-qualified cursor.

`next` accepts only the same exact session ID, not a caller-supplied cursor or JSON. The host holds one memory-only cursor, original query/channel, private control revision, exact lifecycle/generation/metadata snapshot and original admission, expiring 60 seconds after the first page without renewal.
Zero matches do not imply Browse or unavailable content. Changed source, snapshot, origin, privacy, call revision, voice/name readiness or expiry requires a fresh search. Cancel clears the exact host-held search and cancels its in-flight read.

Search results are visual-only, including in Voice Only mode. No query/body is spoken, captioned, logged, persisted, sent to a model or added to transcript chrome.
No session navigation occurs: unsent buffers, existing legacy saved text, question/approval/voice targets and retention activity remain unchanged ([conversation search](../src/Kora/SessionsViewModel.ConversationSearch.cs)).
There is no draft/history/all-session body search, model descriptor, submission, replay or automatic resume.

Deterministic evidence: [grammar and query bytes](../tests/Kora.Core.UnitTests/Commands/ConversationSearchCommandTests.cs) and [source-bound continuation/cancellation](../tests/Kora.Application.UnitTests/Hosting/SessionWorkspaceServiceTests.ConversationCommands.cs).
Also see [typed/voice and decision isolation](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.ConversationSearch.cs) and [private SQLite/headless dirty-composer checks](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.ConversationSearch.cs).
Hardware/provider/wake/acoustic/installed qualification is not enabled or required by this bounded implementation.

The native receipt-history controls additionally offer exact kind, task state,
decision, question status and answer-channel choices, each with explicit Any.
They AND-compose with the lexical query and bind the continuation digest; changed
choices require fresh Search. This does not add typed/voice predicate syntax.
See [native receipt search](windows-and-tray.md#bounded-passive-interaction-history)
for no-read editing, Clear recovery and passive-only authority boundaries.

The [bounded scan](../src/Kora.Application/Hosting/SessionWorkspaceService.Search.cs) reads only actual retained question/option labels, final answer/choices and typed kind/task-state/decision/question-status fields. Results stay in ascending session-local sequence, not relevance order. Each call scans at most 200 receipts and returns the requested 1-50 complete receipts within 64 KiB, including JSON overhead. A query-bound SHA-256 continuation preserves the original session/generation/ceiling; equivalent normalized term sets may continue, changed terms require a fresh search.

`scanned`, `gaps`, `omittedMatches` and `next` are explicit [result fields](../src/Kora.Core/Storage/SessionHistorySearchPage.cs). Baseline/gap/redacted/unavailable receipts count as gaps; oversized matching receipts are omitted explicitly, not truncated. An empty page with `next` is an incomplete scan, not proof of no matches. Continue until `next` is absent. A fresh search includes later appends. Removed content never matches; Done reads never resume. Query text is volatile and is not stored, logged or sent to a provider. No indexes, schema changes or persistent query cache are added.

The native history page can open immutable **receipt details** for an exact
event belonging to the selected session. This freshly resolves the same
authoritative record as `session get` into the shared native plain-text viewer;
it adds no new typed/voice/model command, artifact persistence or execution
route. Availability/redaction/baseline and provenance remain visible.
Search/source and explicit disclosure-confirmed copy reuse existing viewer
policy. Work/queue/questions, lexical file citations and skill inspections
remain distinct; browsing never extends activity, reprioritizes or resumes.
Privacy closure and pre-deletion retention/disposition retire owned viewers.

Activated voice uses the existing enablement/consent/capture/privacy boundary
and retains its originating channel and observed call/recovery revision through
commit. During protected calls, reads require permitted activation and private
presentation; voice mutations are explicitly unavailable under the existing
workspace clear/unavailable-call gate. Nothing is queued for later. Unknown
ownership/privacy fails closed. Pending bootstrap questions/approvals block
session commands without changing their targets; resolve them explicitly first.

**Exact task controls** use the same typed/activated-voice grammar and limits:

| Command | Actual bounded result |
|---|---|
| `task help` | Syntax and availability |
| `task status <session-id> <task-id>` | Exact durable state/revision, session generation, source/current-run and pending/terminal question distinctions |
| `task inspect <session-id> <task-id>` | The same complete bounded authoritative record, not inferred progress or remaining steps |
| `task cancel <session-id> <task-id> <task-revision> <generation> <question-id> <question-revision>` | Atomically cancel only admitted current-run local-version work still waiting before dispatch |

Copy exact IDs and all tokens from a fresh inspection. Only the tray's native
local-version question currently admits that wait. Its question is now before
dispatch; cancellation commits the terminal task, revised cancelled question,
target capability revocation and required audit together. It never creates or
consumes a grant, deletes a task, replays work or terminates a worker.
Unknown/foreign IDs, stale/different questions or revisions, expiry, previous
runs, answered/committed/dispatched/Unknown work and lost host authority refuse.
Already terminal work stays terminal; inspect its real outcome. A confirmed
pre-dispatch cancellation is not a claim that an already invoked effect stopped.
A confirmed cancellation claims no effect termination. This safe cancellation is also available during a protected call with private
ownership and eligible original activation; call/recovery revisions still gate
commit. Pending legacy bootstrap questions/approvals remain unchanged.

This is not conversation/transcript persistence, a queue/executor/scheduler,
general effect cancellation, deletion/retention, routing inference, model tools, or a
session-name inference feature. Native selected-ID Create/Rename/Done/resume
continue to use the same host workspace service and guarded storage transaction.

### Show the Kora window
- **show Kora**
- **open Kora**
- **show your window**
- **open your window**
- **show yourself**
- **bring up Kora**

### Hide the Kora window
- **hide Kora**
- **hide your window**
- **hide yourself**
- **close your window**
- **hide the Kora window**

### Exit Kora
- **exit Kora**
- **quit Kora**
- **close the Kora application**
- **close Kora**
- **exit the application**
- **quit the application**

### Restart the Kora application
- **restart Kora**
- **restart your application**
- **restart the app**
- **restart yourself**
- **relaunch Kora**

Closing or hiding the window leaves Kora running; exiting the application
releases the microphone. Application restart is different from restarting
Windows.

## Settings and guidance tasks

### Inspect or change an admitted appearance option

- **list appearance settings**
- **get appearance.theme**
- **status appearance.reader-text-scale**
- **set appearance.reader-text-scale to 200**
- **set appearance.theme to dark**
- **reset appearance.theme**

Substitute one of the twelve exact IDs in [Appearance settings](settings.md#appearance).
The configured assistant-name prefix is supported for typed input and activated
voice. Exact spoken names also work: replace dots/hyphens in the ID with
spaces, for example **"Kora, set appearance theme to dark"**.
Capitalization is ignored, but this typed value grammar deliberately
preserves signs and decimal punctuation: `-10` and `1.5` are rejected, never
normalized into valid integers. Use numeric whole numbers without appended
units, `system`/`light`/`dark`, or `true`/`false`. Extra words, invented IDs,
relative changes in this ID grammar and invalid ranges produce local clarification, without
model/network interpretation. Changes and per-option reset share the direct
UI service, domain validation, revision check, atomic persistence, audit and
live notifications. There is no whole-profile reset or undo, model tool
exposure, arbitrary JSON patch or configuration-file editing authority.

### Natural exact app theme and native reader text scale

Original typed input also accepts these whole-utterance commands; voice requires the **current configured assistant-name prefix**, for example **"Kora, use dark mode"** ([parser](../src/Kora.Application/Configuration/NaturalAppearanceCommand.cs)):

- **use dark mode**
- **use light mode**
- **use system theme**
- **get reader text scale**
- **status reader text scale**
- **reset reader text scale**
- **set reader text scale to &lt;integer&gt; percent**
- **increase reader text scale by &lt;positive integer&gt; percentage points**
- **decrease reader text scale by &lt;positive integer&gt; percentage points**

Reader scale is inclusive 75–200 percent; reset restores 100. An increase from 150 by 25 percentage points requests 175, not 187.5.
Relative resolution reads fresh confirmed state at execution, uses checked arithmetic and the registry's validation, then submits the existing revision-bound proposal.
A concurrent revision refuses instead of overwriting or recalculating ([registry](../src/Kora.Core/Configuration/AppearanceOptionRegistry.cs), [proposal](../src/Kora.Application/Configuration/AppearanceConfigurationService.cs)).

Capitalization and outer whitespace are ignored. Number words, signs, decimals, vague pronouns, selected-window inference, extra clauses and substring commands are not accepted as changes.
Invalid results clarify without clamping. Missing/corrupt/unconfirmed evidence is never guessed; an absent preference uses only the service-confirmed declared default
([parser](../src/Kora.Application/Configuration/NaturalAppearanceCommand.cs), [confirmed state](../src/Kora.Application/Configuration/AppearanceConfigurationService.cs)).

These aliases use the same admitted service, atomic preference/audit path and live notifications as exact IDs and native controls.
They change only Kora's app theme or bounded native reader content typography: no OS/global UI scale, new content access, permission, speech/caption enablement or model-derived authority
([executor](../src/Kora.Application/ViewModels/MainViewModel.Appearance.cs), [reader scope](../Design/Information_Display.md#bounded-native-reader-text-scale)).

### Inspect or change an exact output choice

Audio **output endpoint** preference changes are separate:

- **list output settings**
- **get speech.output-device**
- **status speech.output-device**
- **set speech.output-device to &lt;exact presented endpoint ID&gt;**
- **reset speech.output-device**

Discover first, then use one exact ID; no name/index/fuzzy alias is accepted.
Native Settings uses the same admitted choice/save/reset workflow. System is
`system-default`; reset removes only Kora's output override. These commands never
play audio, change Windows defaults/volume, grant consent or answer an approval.
Protected/unknown calls block original voice-channel mutations. Stale choices,
ownership/privacy changes and failed evidence require explicit refresh/recovery.

### Inspect or change Kora playback volume

- **list volume settings**
- **get speech.playback-volume**
- **status speech.playback-volume**
- **set speech.playback-volume to 30**
- **reset speech.playback-volume**
- **speak louder**
- **speak quieter**

Canonical integer 0-100 (no signs, padding, fractions, percent suffix or leading zeros); default/reset 100. Zero prevents synthesis and retains full visual output. Changes never alter global/call volume, test audio, reopen capture or replay output.
The exact aliases move the freshly admitted desired volume by **up to 10 percentage points** ([domain step](../src/Kora.Core/Configuration/PlaybackVolume.cs#L21)).
A final shorter step explicitly reports `saved-upper-boundary` (100) or `saved-lower-boundary` (0); already at that bound reports `unchanged-upper-boundary` or `unchanged-lower-boundary` without writing or auditing a successful change ([shared action](../src/Kora.Application/Configuration/PlaybackVolumeConfigurationService.cs#L86)).
The complete result includes `adjustment.previousPercent` and `adjustment.currentPercent` alongside exact desired/effective percent and saved source: for example, 95 becomes 100, not 105; 5 becomes 0, not -5. [Boundary-result tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.RelativeAudio.cs) check both old/new values and the unchanged follow-up.
Current-name activated input is supported; protected/Unknown original-voice
writes refuse. Input is at most 1,024 UTF-8 bytes including the prefix and
rejects controls; complete result at most 64 KiB.
See [controls and option-specific recovery](settings.md#kora-playback-volume) and
[shared preference admission](settings.md#preference-changes-and-recovery).

### Inspect or change Windows-native speech rate

- **list rate settings**
- **get speech.windows-rate**
- **status speech.windows-rate**
- **set speech.windows-rate to -10**
- **set speech.windows-rate to 0**
- **set speech.windows-rate to 10**
- **reset speech.windows-rate**
- **speak faster**
- **speak slower**

Canonical integer **-10..10**, normal/default/reset **0**. Negative nonzero
signs only; no plus, negative zero, padding, fractions or suffix. Windows
provider-native rate, not percentage or words/minute.
**Kokoro is unsupported and unchanged**; no provider switch, trial or replay.
The exact aliases move the freshly admitted desired Windows rate by **1 native unit** ([domain step](../src/Kora.Core/Configuration/WindowsSpeechRate.cs#L21)).
The complete relative result includes `adjustment.previousNativeRate` and `adjustment.currentNativeRate`; no-op and denied results disclose the actual unchanged value, not an uncommitted target ([result](../src/Kora.Application/Configuration/WindowsSpeechRateState.cs)).
Reaching -10/10 explicitly reports `saved-lower-boundary`/`saved-upper-boundary`; already at that bound reports `unchanged-lower-boundary`/`unchanged-upper-boundary`, with no write or successful-change audit ([shared action](../src/Kora.Application/Configuration/WindowsSpeechRateConfigurationService.cs#L118)).
Current-name activated input is supported; protected/Unknown original-voice
writes refuse. Input is at most 1,024 UTF-8 bytes including the prefix and
rejects controls; complete result at most 64 KiB.
See [controls and option-specific recovery](settings.md#windows-native-speech-rate) and
[shared preference admission](settings.md#preference-changes-and-recovery).

### Inspect or change Kora's response-window visibility preferences

- **list response window settings**
- **get response-window.always-show** / **status response window always show**
- **get response-window.topmost** / **status response window topmost**
- **set response-window.always-show to true** / **set response-window.always-show to false**
- **set response-window.topmost to true** / **set response-window.topmost to false**
- **reset response-window.always-show** / **reset response-window.topmost**
- **keep responses visible** / **allow responses to hide**
- **keep response window on top** / **stop keeping response window on top**

These are complete exact utterances, optionally prefixed with the current assistant name: **"Kora, keep responses visible"**. Canonical targets also accept the exact spoken names **response window always show** and **response window topmost**.
Boolean set accepts only **true** or **false**, not relative/fuzzy targets, implicit selected windows or compound instructions ([grammar](../src/Kora.Application/Configuration/ResponseWindowCommand.cs)).

Results appear in the transcript without speech, reopening a response, replacing a pending question/approval or sending text to a model. Malformed reserved commands clarify before question/approval/model routing.
Changes require the owning unlocked host and no pending question/approval or competing preference write ([dispatcher](../src/Kora.Application/ViewModels/MainViewModel.ResponseWindowCommands.cs)).

Only Kora's device-local addressed Boolean changes. Reset restores **AlwaysShow=false** or **Topmost=true**, preserving the other Boolean, saved position and response timeout. AlwaysShow prevents inactivity hiding of an already admitted response; it cannot reveal dismissed, private, revoked or retired content.
Topmost is not a Windows-wide setting or foreground/activation authority ([native window](../src/Kora/ResponseWindow.axaml.cs), [defaults](../src/Kora.Core/Configuration/ResponseWindowSettings.cs)).

Missing preference files use declared defaults without claiming a save. Malformed, externally changed or uncertain saved evidence is explicitly unavailable: inspect the saved state and audit receipts before repair/restart and initiate a fresh request.
No automatic rewrite, default-save, rollback or retry is claimed ([confirmation and commands](../src/Kora.Application/ViewModels/MainViewModel.ResponseWindowCommands.cs), [audited writer](../src/Kora.Application/ViewModels/MainViewModel.Preferences.cs)).

### Inspect or change the device-default response mode

- **list response settings**
- **get responses.default-mode**
- **status responses.default-mode**
- **set responses.default-mode to Hybrid**
- **set responses.default-mode to VoiceOnly**
- **set responses.default-mode to VisualOnly**
- **reset responses.default-mode**

Natural exact aliases use the same SET operation, not a toggle or playback command:

| Exact phrase | Equivalent |
|---|---|
| **use visual responses** | `set responses.default-mode to VisualOnly` |
| **use spoken responses** | `set responses.default-mode to VoiceOnly` |
| **use spoken and visual responses** | `set responses.default-mode to Hybrid` |
| **check response mode** | `get responses.default-mode` (desired/effective mode, availability and live output policy) |

Aliases are case-insensitive with an optional current assistant-name prefix, but no extra words, punctuation or internal whitespace repair. Reserved malformed variants clarify locally; they never answer a pending decision or reach the model. **be quiet** and **stop speaking** remain stop-playback commands; **mute** is not a response-mode alias.
These aliases do not claim the separate `speak louder`, `speak quieter`, `speak faster` or `speak slower` controls.
See the [exact parser and conflict tests](../tests/Kora.Application.UnitTests/Configuration/ResponseModeCommandTests.cs).

Exact case-insensitive Hybrid / VoiceOnly / VisualOnly; default/reset Hybrid. Save changes only the device default, not queue/task overrides or mandatory call/privacy/full-visual recovery. No autoplay, replay or capture.
Mode changes affect future eligible responses only: no sample speech, provider/output-device change or global/call volume change. VoiceOnly never overrides call/privacy suppression, speech caps or required visual recovery, and does not establish speaker identity.
Native selection, enum syntax and aliases retain the same fresh choice/revision, original-origin admission, audit and atomic save/readback. Results disclose desired/effective/outcome; stale or unconfirmed state refuses rather than reporting success ([route and refusal tests](../tests/Kora.Application.UnitTests/ViewModels/MainViewModelTests.NaturalResponseMode.cs)).
Current-name activated input is supported; protected/Unknown original-voice
writes refuse. Input is at most 1,024 UTF-8 bytes including the prefix and
rejects controls; complete result at most 64 KiB.
See [controls and option-specific recovery](settings.md#device-default) and
[shared preference admission](settings.md#preference-changes-and-recovery).

### Inspect or change an installed speech choice

- **list speech settings**
- **get speech.provider**
- **get speech.voice**
- **set speech.provider to windows-sapi**
- **set speech.voice to kokoro / af_heart**
- **reset speech.provider**
- **reset speech.voice**

Use the installed IDs actually listed on your device. Exact spoken option
names, such as **speech provider**, and the configured assistant-name prefix
also work. Voice choices use `provider / ID`, or an unambiguous exact voice ID;
selecting a voice explicitly selects that provider too. IDs are not guessed,
translated or case-normalized, and unknown or unavailable choices do not
trigger a model, download or substitute. The command bound is 320 characters
after the optional name prefix; voice IDs are at most 256 characters.

Provider set/reset also restores that provider's advertised default voice;
provider reset chooses Windows. Voice reset restores only the current
provider's advertised default. An unavailable default requires an explicit
installed voice choice. Status reports saved/desired/effective values,
revision and recovery. Protected calls reject original voice-channel set/reset,
including a later UI confirmation; a new eligible Settings/typed request is
required. Rate and model settings tools are not added; volume uses the
independent exact commands above.
See [the shared native controls](settings.md#speech-provider).

### Inspect or lower spoken summary caps

- **show spoken summary limits**
- **set spoken summary sentence limit to 2**
- **set spoken summary word limit to 40**
- **reset spoken summary sentence limit**
- **reset spoken summary word limit**
- **list speech settings**
- **get speech.summary-sentences**
- **get speech.summary-words**
- **set speech.summary-sentences to 2**
- **set speech.summary-words to 40**
- **reset speech.summary-sentences**
- **reset speech.summary-words**

Natural aliases are exact whole utterances. Typed input accepts an optional current-name prefix; activated voice requires it ([dispatch](../src/Kora.Application/ViewModels/MainViewModel.cs)).

Use canonical decimal integers, without signs, leading zeroes, fractions or word numerals; no relative "be brief" interpretation is added. Malformed reserved aliases clarify locally ([parser](../src/Kora.Application/Configuration/SpeechCommand.cs)).

These commands change future ordinary-speech admission budgets, not the current answer or an instruction to speak now. Pending questions/approvals remain untouched; finish or cancel them first. Discovery and acknowledgements remain visual, with no new speech, regeneration or replay ([shared dispatch](../src/Kora.Application/ViewModels/MainViewModel.SpeechConfiguration.cs)).

The exact spoken names **speech summary sentences** and **speech summary words**
also work, with the configured-name prefix for activated voice.
Values are exact positive integers: sentences **1-3**, words **1-80**.
Defaults are **3 sentences / 80 words**. Reset changes only that cap.
These device-local settings share the installed-speech workflow, original-channel
call/privacy/ownership gate, revisions, audit and live Settings notifications.
Neither command permits a model interpretation, extra model call or asset change.
Ordinary speech includes the title and retained warnings and must fit both caps.
Over-cap results remain fully visual with an explicit refusal status, never
truncated. Exact approval/question readback retains its own mandatory bounds.
See [counting and recovery semantics](settings.md#spoken-summary-limits).

### Review one retained assistant speech limit suggestion

- **review speech limit suggestion**
- **confirm speech limit change**
- **cancel speech limit review**

These exact commands bind only the host-pinned successful answer from your
explicit completed Active-session `session request`, never the newest message,
current selection or model-claimed identity. Activated voice requires the current
assistant-name prefix. Review shows both complete current/proposed caps,
saved/default provenance, unchanged companion, shared device-local scope and
next-ordinary-speech timing; confirmation must be a separate fresh input.
No review, expired/changed source, pending question/approval or denied original
channel means no write. The host retains original voice classification even
after typed/native confirmation. A generic "yes" does not confirm.

Alternatively use **Review speech limit suggestion...** on an exact answer in
Sessions/compact view and its separate **Confirm exact speech limit change** or
**Cancel speech limit review** buttons. This changes only one existing cap through
the shared audited settings service; it never speaks/replays the current answer,
reruns a model, installs a provider or grants model write authority.
See [the full review and recovery rules](settings.md#review-an-assistant-speech-limit-suggestion).

### Open settings
- **open settings**
- **show Kora settings**
- **show settings**
- **open preferences**
- **change settings**
- **settings**

### Open the user guide
- **open documentation**
- **show documentation**
- **show the user guide**
- **open the user guide**
- **open the manual**
- **show me the instructions**

### Open setup and readiness
- **open setup**
- **configure Kora**
- **show what you need**
- **set up local models**
- **check setup**
- **show readiness**
- **check dependencies**
- **review local model setup**

Opening setup shows readiness; it does not install a model without approval.

### Show supported commands
- **what can you do**
- **help**
- **show supported commands**
- **list commands**
- **what commands do you know**
- **what can I say**
- **show me what you can do**

## Information and task-control tasks

### Show the running version
- **what version are you running**
- **show your version**
- **what version is this**
- **tell me your version**
- **which version of Kora is this**

The tray also offers **Review local version (native question)**, an explicit
mouse/keyboard-only route using the same durable local version-query context.
It does not need voice consent, a microphone, a model or network access.
The native window names the original session/task/question, revision and
expiry. Choose **Show local version**, then **Submit answer** to read the
running version and private-storage disclosure. No choice is preselected.
**Save draft** records the current answer without submitting. **Review exact
record** is passive inspection, not approval or execution. **Cancel question**
is explicit; Close/Escape only closes presentation. Stale, expired or
privacy/ownership-unavailable targets cannot be answered.

**Tray > Return to current question** returns to that same still-live original version wait after Close/Escape, restoring only its committed draft and current revision. Unsaved edits/review clear on close. The original five-minute expiry continues while hidden; return creates no new query/question/grant or dispatch and never extends activity.

An already-open exact card activates; Sessions selection cannot retarget it.

Storage/audit failure never reports a successful query. Recovery becomes Unavailable after expiry, privacy/ownership loss, changed authority, cancellation, original completion or restart: inspect read-only status and start a fresh review after resolving the blocker. Kora never automatically retries or rehydrates old work.
Explicit cancellation atomically commits the original task and question as Cancelled before dispatch. Unfinished intent-only work recovers as Interrupted; dispatched uncertain work remains Unknown, without replay. This bounded route does not
enable general effect approvals, change legacy grants or replace the ordinary
version phrases above. Desktop/screen-reader/speech acceptance is not yet
claimed from the automated tests.

### Show activity and the setup queue
- **what are you currently working on**
- **what are you doing**
- **what do you have left to do**
- **what are you working on**
- **what tasks are left**
- **show the task queue**
- **show setup status**
- **what is your status**

### Show current-task progress
- **what is the current task status**
- **what is the current task progress**
- **how far along is the current task**
- **show task progress**
- **show current task status**
- **how is the current task going**
- **what's the progress of the current task**
- **how much of the current task is done**
- **how is setup progressing**

Current-task status reports the active setup stage and any measured download
percentage, or the next setup item requiring action when nothing is running.
It does not invent a completion percentage when the operation provides only
stage updates.

### Cancel the active task
- **cancel task**
- **cancel current task**
- **stop**
- **cancel the current task**
- **stop the current task**
- **stop current task**
- **cancel the download**
- **stop generating**

While a local model request is generating or any response is being spoken, the
response window also shows a separate **Cancel task** button. It stops the
current model request or speech playback; press **Esc** for the same action
while the button is visible. **Dismiss** hides the response without cancelling
model work or stopping speech. **Stop speaking** remains the explicit voice
command for stopping only speech playback.

### Stop speech playback
- **stop speaking**
- **stop talking**
- **be quiet**
- **stop reading aloud**
- **stop the voice**

**Stop** cancels the active task; it does not mean stop speaking. Use one of
the speech-playback phrases to stop the voice instead.

### Disable listening for this run

- **stop listening**
- **disable listening**

The current assistant-name prefix is optional for typed/already activated input. These exact phrases reserve a privacy-reducing stop before pending answers/approvals and use the same action as native/tray Disable listening ([stop action](../src/Kora.Application/ViewModels/MainViewModel.VoicePrivacy.cs)).
Native release is awaited; uncertain release is explicit. No task is cancelled or decision approved. Saved consent is retained. A new deliberate PTT gesture can capture one command after confirmed release and current gates; native Enable listening restores readiness separately ([stop semantics and evidence](voice-and-audio.md#exact-stop-listening)).

## Windows session task

### Lock Windows
- **lock the machine**
- **lock my computer**
- **lock windows**
- **lock this computer**
- **lock my PC**
- **lock my screen**
- **lock this workstation**

Locking is a real local action. Kora closes microphone capture before asking
Windows to lock. This exact built-in command currently calls a C# handler
without a confirmation prompt. A model-suggested lock requires approval
unless that named action already has a session or always grant.

## Protected power-proposal tasks

### Propose a computer shutdown
- **shut down the computer**
- **shut down this machine**
- **power off the computer**
- **turn off my computer**
- **shut down my PC**
- **power down this computer**

### Propose a computer restart
- **restart the computer**
- **reboot this machine**
- **restart windows**
- **reboot my computer**
- **restart my PC**
- **reboot the computer**

### Cancel a pending power proposal
- **cancel shutdown**
- **cancel that shutdown**
- **cancel computer restart**
- **cancel that reboot**
- **don't shut down the computer**
- **abort shutdown**
- **cancel the reboot**
- **abort restart**

### Show pending power-proposal status
- **what power action is pending**
- **are you about to restart the computer**
- **is a shutdown pending**
- **is a restart pending**
- **show pending power action**

Shutdown and Windows restart are proposals in the current release. Kora shows
the recognized request but does not send a power operation to Windows.

## Grant-management tasks

### List and view grants
- **list grants**
- **show grants**
- **view grants**
- **what grants are active**
- **list my approvals**
- **show my permissions**

The live session and persistent grants appear in a Markdown document window.
Listing never changes permissions and works without a local model.

### Prepare a grant change
- **manage grants**
- **add a grant**
- **edit a grant**
- **remove a grant**
- **change grants**
- **manage approvals**

Select the exact built-in action and scope in the response window: **Add**
creates a session or always grant, **Remove** revokes an existing grant, and
**Move** edits its scope using the destination after "to". The destination
selector is used only for Move. Kora repeats the exact action, operation and
scope; confirm with the button or say "approve once" (prefixed with the
assistant name by default), or reject. "Always allow this" does not confirm
a grant edit: the proposed scope is already fixed. A removal with no specified
scope only proceeds automatically when exactly one grant exists for that
action; otherwise select the existing scope. If a ready model identifies the
grant from a more specific request, it still cannot apply the change without
confirmation. Confirming a grant never executes the named action.

## Model execution settings

These exact commands read or change the same device-local choices shown on the
Settings **Models** tab. They work without invoking a model and are available
through typed input or activated voice.

### Show enabled model locations
- **which models are enabled**
- **show model settings**
- **show model configuration**
- **what models can you use**
- **are local models enabled**
- **are hosted models enabled**

### Enable local model execution
- **enable local models**
- **turn on local models**
- **use local models**
- **allow local models**

### Disable local model execution
- **disable local models**
- **turn off local models**
- **stop using local models**
- **block local models**

Disabling local models cancels an in-flight Ollama request. Built-in commands
continue to work.

### Enable hosted model execution
- **enable hosted models**
- **turn on hosted models**
- **use hosted models**
- **allow hosted models**
- **enable cloud models**

### Disable hosted model execution
- **disable hosted models**
- **turn off hosted models**
- **stop using hosted models**
- **block hosted models**
- **disable cloud models**

Hosted execution is disabled by default. The current build has no hosted
provider or credentials; enabling hosted models records permission but does
not send a request or provide a cloud fallback.

## When a command does not match

When local model execution is enabled and the selected local model is ready,
Kora sends only the current unmatched
request text, the built-in action descriptions, and a limited snapshot of
readiness, task state/progress, listening state, and pending power-proposal
status to it. The model can return an answer, ask a clarification question
with two to four options, propose a grant change, or request one named built-in
action; Kora rejects unknown actions
and runs allowed ones through its existing command handler. Hiding or exiting
Kora, restarting it, cancelling a task, locking Windows, or proposing a power
action requires approval unless you have already granted that specific action
for this session or always. When asked, say **"Kora, approve once"**,
**"Kora, approve for this session"**, **"Kora, always allow this"**, or
**"Kora, reject"**. You may also use the visible scope buttons; the request
remains visible even when voice-only responses are selected. The configured
assistant name is required for spoken approval by default, but can be made
optional in Settings. Session grants end on exit, restart, or locking Windows through Kora.
Always grants persist until revoked under **Settings > Approvals**. Reject
or dismiss the suggestion to discard it. These grants are keyed to action
names, not executable or script hashes, and do not change how direct exact
commands are dispatched. The model cannot
directly access arbitrary files, services, or tools. Without a ready local
model, Kora explains that other requests are unavailable. Use **what can you
do** or open this Documentation window to review exact built-in phrases.

When Kora needs direction before answering, the response window shows the
question and numbered choices, even in VoiceOnly mode. Choose an option by
mouse or say its number or label (for example, **"Kora, option one"**); the
assistant name is required for spoken choices by default, as with spoken
approvals. Say **"Kora, cancel question"** or use **Cancel question** to
dismiss it. The chosen option and the original request go back to the local
model; selecting an option is **not** permission to run an action or create a
grant. A later action proposal still requires its own approval, unless a
specific grant already covers that action. Kora limits consecutive questions
to three; start a new request with more details if it reaches that limit.
## Run skills and artifacts

### Edit an owned instruction or literal prompt without a model

Type `instructions editor` or `prompts editor`, or say the same control prefixed
with the current assistant name. Both focus the same native editor with explicit
Instruction/Prompt kind selection. `list`, `create <id>`, `edit <id>`,
`inspect <id>`, `validate`, `compare`, `rebase`, `save`, `copy <new-id>`, `reload`, `discard`, `delete`, `activate`, `disable`, `stop-use` and `cancel`
are the exact controls after either plural kind name. Only create/edit/inspect/copy
accept one canonical file ID; other controls target the open original.
`instructions validate` / `prompts validate` observes the currently open same-kind supported owned draft/new draft using exact Save preparation, including BOM/native-marker overhead and observed structural/namespace checks. It never opens/switches sources, saves or enables.
The bounded body-free result is historical structural compatibility, not harmlessness, current disk integrity or approval. Source/private/input changes retire it; Save separately revalidates and exactly confirms. No store I/O/reload, revision-preference write/hold, model/network or reference execution is added. See [draft validation](instruction-editor.md#validate-the-open-draft-without-saving).
`instructions rebase` / `prompts rebase` explicitly observes latest for an already open supported same-kind source and reviews complete old/latest/raw bodies. Confirmation rereads the exact observed revision and adopts only that baseline in memory, preserving the raw draft; it neither merges nor saves/enables.
Cancel preserves the prior baseline/draft ([source-preserving Rebase](instruction-editor.md#rebase-the-open-draft-without-saving)).
**Disable reviewed use** / `instructions disable` or `prompts stop-use` separately confirms the clean inspected canonical native v1 owned source/kind/digest/native revision. It durably withdraws new selections without writing/deleting/unregistering the source.
Disabled files stay inspectable/editable. Dirty drafts remain intact with an explicit Save/Reload prerequisite; current-name activated controls focus the same native confirmation, never voice-only approval.
Already captured reasoning keeps immutable content; pending unadmitted questions/action choices expire without dispatch or retargeting. Mandatory access/privacy/action/egress checks remain. Reload/restart/restored bytes cannot re-enable use: require fresh exact review. Failed audit/state readback reports held/unavailable recovery, not success or rollback.
No paths or body dictation are accepted. Save/Delete and separate exact-use review
remain native; detected conflicts offer Reload/Overwrite/Cancel. See
[native editor and limits](instruction-editor.md).
`copy <new-id>` reviews a same-kind supported native v1 owned original and its current draft, then creates a separate disabled owned file without overwriting. The original source/draft stay open and untouched; independently inspect/review the destination before explicit use. Profile-original editing and profile forks remain unavailable.
`instructions list` / `prompts list` also exposes the selected kind's owned metadata search and observed-use filters in that same native editor. There is no typed/voice search grammar: use the native query/filter controls after List, or the existing exact `inspect <id>` route.
Filters/search/page changes are in-memory and preserve the open original/draft. Row inspection refuses stale snapshot/digest/native identity instead of following a same-name replacement. Observed catalogue use state is not current disk freshness; explicit Refresh/reselect is the recovery path. See [inventory limits and controls](instruction-editor.md#search-observed-owned-metadata).

### Explicitly select a saved revision

Enabled native owned-v2 prompts open the existing private required-input form through exact typed/current-name activated selection. Parameter values and retention consent are native-only, never inline command/PTT text. Preview and old Use remain ephemeral; separate exact Active-session review/confirmation can retain the whole expanded USER body through the real conversation service, with unavailable generation. See [explicit prompt submission](instruction-editor.md#submit-an-expanded-request-to-one-exact-session).

Kora can apply a bundled or disk-backed skill, instruction, prompt, or agent
artifact to a local-model request from the command box or activated voice.

**Type a slash command**

Type `/` in the command box to open a dropdown of every available bundled and
disk-backed artifact. Continue typing to filter by command name, or type a
kind such as `/skill `, `/prompt `, `/instruction `, or `/agent `. Use Up/Down and Enter
or select an item with the pointer; Escape closes the dropdown.

Use a direct command:

```text
/lock
/restart
/shutdown
```

You can also include the artifact kind:

```text
/skill lock
/skill restart
/skill shutdown
```

An artifact that accepts request text uses the rest of the line as its input:

```text
/prompt explain why this setup failed
/agent review-changes inspect this patch
```

The current release bundles only the three skills listed above. An unknown,
incomplete, or wrong-kind slash command shows an error and is not sent to the
model as an ordinary question.

**Say an artifact command**

Start with your configured assistant name, then say **run** or **use**:

- "Kora, run lock."
- "Kora, use the restart skill."
- "Kora, run the shut down the machine skill."
- "Kora, use explain to summarize this result." when a future `explain`
  artifact is available.

If you renamed Kora, use the configured name. Voice artifact requests without
the activation name are rejected in the same way as other free-form voice
requests.

**Where disk artifacts are loaded from**

Kora loads compatible files from its own roaming `Skills`,
`Instructions`, `Prompts`, and `Agents` folders and the existing VS Code or VS
Code Insiders customization locations. In **Settings > Artifacts**, you can
maintain up to sixteen additional absolute directories for each artifact kind.
These settings are separate from the read-only shared skill source
registrations below; registering a shared source does not add it to the model
artifact catalogue.

Disk skills use `SKILL.md`; prompts use `*.prompt.md`; instructions use
`*.instructions.md`; agents use `*.agent.md`. Default and additional
directories use the same bounded UTF-8, YAML frontmatter, traversal, naming,
digest, and catalogue-conflict rules. Skills marked `user-invocable: false` do
not appear. Conflicting command names, IDs, or spoken names fail closed instead
of choosing one source silently. Kora watches default and configured locations
and automatically publishes a complete valid replacement catalogue after
changes. Invalid or partially written content reports reload failure; retained
historical catalogue data does not keep managed owned instructions/prompts usable.
New/changed managed revisions require separate current-run exact review and native
freshness before explicit selection; old bytes do not restore withdrawn use.
Managed prompts are literal saved user-request content, not system guidance.
No restart is needed. Loading an agent
artifact applies its declarative instructions; it does not start an autonomous
agent runtime.

If a selected artifact changes while local reasoning, a clarification, or an
action approval is pending, Kora retires that interaction. Start a new request
to use the new revision. Action proposals originating from artifact
instructions require a fresh single-use approval for the current content
digest. Existing session/always action grants are not reused, and an
artifact-originated proposal cannot create a reusable grant.

**MCP server configuration files**

Choose `McpServerConfiguration` in **Settings > Artifacts** to add an absolute
JSON file rather than a directory. Kora also inspects its roaming
`Mcp\mcp.json`, VS Code/VS Code Insiders `User\mcp.json`, and Claude Desktop
`claude_desktop_config.json` when present. Supported files contain exactly one
`servers` or `mcpServers` object. Each entry must declare either a non-empty
stdio `command` or an HTTP/HTTPS `url`.

The settings page shows only the server name, transport, source file name, and
whether it is included at application level. It never displays or sends
commands, arguments, URLs, headers, environment values, or credentials to a
model. Include/exclude is audited and fail-closed, but does not connect to a
server or authorize any capability.

**HTTPS read-only operation:** in the selected Sessions conversation, use
**Review HTTPS MCP options**, then explicitly choose a server and delegated
account. Enter the task and choose **Use reviewed MCP tool locally**. Separate
native reviews cover the exact destination/account, discovered schema/tool, and
outgoing arguments. Only host-mapped read-only tools are eligible; a server's
`readOnlyHint` is not approval. The actual inert tool result is displayed
separately from the retained assistant answer. **Stop MCP operation** closes
new work and waits for owned resources before disconnect; it does not undo an
external call.

With that exact conversation in the foreground and explicit choices already
made, typed or activated-voice `use selected mcp tool <task>` follows the same
operation. `stop mcp` requests cancellation. Account and permission reviews
remain native; commands never choose a default server, account or session.

The shipped HTTPS account/transport broker remains **unavailable until
qualified**. There is no anonymous or ambient-credential fallback, stdio
launcher, default integration, or hosted-model result disclosure. Ordinary app
inclusion still does not connect or authorize execution.

### Search admitted skill package metadata

In **Tray > Skill packages**, type a native metadata query, choose **All / Runtime unavailable / Runtime available**, then click **Search**. Search covers bundled declared names, IDs and descriptions only, across the admitted catalogue (at most eight packages). Runtime availability is not instruction compatibility; every current bundled package remains invocation-unavailable ([bundled controls](../src/Kora/SkillPackagesWindow.axaml.cs), [bounded catalogue](../src/Kora.Core/Skills/SkillPackageCatalogue.cs)).

In **Shared profile sources (read only)**, first explicitly **List selected source**, then use **Search** with **All / Instruction-compatible / Instruction-incompatible**. Matching covers admitted declared name, metadata-qualified source ID/name/version identity when the name is valid, and valid declared description, across at most 32 snapshots from that exact source. Missing metadata is not guessed; files/roots, path-fallback IDs, SKILL.md bodies, manifests and helpers are not searched ([query policy](../src/Kora.Application/Skills/SkillCatalogueSearch.cs)).

Queries are literal ordinal-ignore-case substrings: spaces are significant, with no trimming, wildcards, ranges or Unicode normalization. The bound is 256 UTF-16 units / 512 strict UTF-8 bytes; malformed Unicode, controls, directional/format characters and line/paragraph separators are explicitly rejected. Rejection empties results/selection rather than falling back to All. Typing or changing the filter only edits the draft; click Search to apply it. **Clear** restores empty/All without opening another result ([shared query validation](../src/Kora.Core/Context/LiteralMetadataQuery.cs), [native search](../src/Kora/SharedSkillSourcesWindow.axaml.cs)).

All includes incompatible/unavailable rows. Shared compatibility is structural instruction-only, not harmlessness, trust, runtime qualification or invocation availability. Search/filter never performs a read, recheck, registration change or withdrawal. An exact surviving selection can remain open; exclusion clears private source/identity panes with an explanation and requires explicit result selection. Initial open/list still selects the first package; refresh/source replacement/withdrawal/failure/privacy/ownership/call change/close clears navigation and retires late callbacks. Queries are not saved or indexed, and no model/tool/typed/voice search route is added ([controllers](../src/Kora/SharedSkillSourcesWindowController.cs), [native behavior tests](../tests/Kora.Windows.IntegrationTests/SkillMetadataSearchWindowTests.cs)).

### Inspect shared profile skills locally

Open the tray **Skill packages**, then **Shared profile sources (read only)**.
Choose **Choose and register profile root (read only)** and select an exact
bounded local directory below your Windows profile, such as `.agents\skills`.
Kora resolves the profile through Windows Known Folder APIs, not environment
variables or a typed/model-supplied profile override. It never scans the whole
profile. Select a registered root and choose **List selected source**.

Review every compatible or unavailable package with its source ID/file,
declared name/version, exact SHA-256 byte digest, immutable inert SKILL.md text,
tool references, additional uninspected entries and reasons.
**Recheck selected revision** labels a changed live revision stale without
changing the displayed old snapshot; list again for a new review.

To stop Kora reading one source, select its registered metadata and choose
**Withdraw local read consent**. Review and confirm the exact source ID,
profile-relative root and directory identity in the native **Unregister source**
confirmation. This removes only Kora's registration and local inspection
snapshots, not your shared files. It changes no enablement, execution or grants;
other source registrations remain intact. Even a missing original folder can
be unregistered without reading or restoring it. A changed registration list
requires refreshing and confirming again. Re-registering the path requires a
fresh folder selection and receives a new source ID; old snapshots do not
regain consent.

The narrow reader requires flat YAML `name`, `version` (for example `1.0.0`)
and `description`. Unsupported metadata/YAML, scripts or extra package files,
executable/unknown fenced code and unresolved references remain unavailable,
not silently omitted. Invalid UTF-8 is unavailable without lossy conversion.
Limits are four roots, 32 packages/256 entries/four directory levels per source,
64 KiB per SKILL.md and 1 MiB instruction bytes per source. Links/reparse points,
hard links, path escapes and removed/replaced/busy sources fail closed.

Registration persists only root consent and directory identity in device-local
atomic preferences. Corrupt/unknown saved registrations block discovery; restore
a verified registration file or the original source explicitly. No silent reset,
repair or rebinding occurs. Closing privacy cancels reads and clears the view.
Withdrawal cancels in-flight inspection/recheck and clears text/hex snapshots
immediately; late results cannot redisplay a removed source. A write, read-back,
audit or admission failure does not prove rollback: the removal may already be
saved. Read authority and the view remain closed after an unconfirmed mutation
until **Refresh registrations** freshly observes valid saved state (or the
host's audit/session evidence boundary is recovered). No successful receipt is
claimed on failure.
For the attempted source, refreshing does not revive its old identity in the
current host: retry unregistering any remaining saved metadata, then select the
folder afresh to obtain a new source ID if you want to grant local reading again.

Registration and inspection alone grant no invocation, egress, approval or bundled trust.
Kora never writes the shared source, fetches references, installs dependencies or executes its scripts.
This is not a harmlessness or complete dependency-inventory certification
([inspection contract](../src/Kora.Core/Skills/SharedSkillSnapshot.cs)).

### Reviewed instruction-only shared skill invocation

For a compatible single-file skill with no declared tools, select **Review selected instruction-only use**.
Review its complete inert instructions, source and version, then separately choose **Enable exact reviewed revision**.
Refresh and explicitly select one existing Active conversation, enter your original request, choose **Review exact invocation** and confirm the exact skill/request/target.
Only a retained answer or a truthful unavailable/failure/cancelled outcome completes use
([native controller](../src/Kora/SharedSkillInvocationController.cs), [workflow](../src/Kora.Application/Skills/SharedSkillInvocationWorkflow.cs)).

Typed and current-name activated voice can perform the same review, enablement and confirmation:

```text
Kora, review shared skill concise-review
Kora, shared skill enable
Kora, shared skill targets
Kora, shared skill target <exact displayed Active conversation ID>
Kora, shared skill request <original prose>
Kora, shared skill review invocation
Kora, shared skill confirm invocation
Kora, shared skill close
```

Use **shared skill next targets** for the next bounded metadata page.
The exact case-sensitive skill name resolves only within the explicitly listed native source; use `name from source <displayed source ID>` when source qualification is needed.
No fuzzy match, model selection, reserved alias registration or implicit conversation creation is admitted.
Initial root registration and discovery remain native-only
([command parser](../src/Kora.Application/Skills/SharedSkillInvocationCommand.cs), [source selection](../src/Kora/SharedSkillSourcesWindow.Invocation.cs)).

Enablement is memory-only, exact-revision, single-use and expires after two minutes.
Close, privacy/control/call/ownership changes, changed registration or stale source revoke the review; restored old bytes do not revive an observed revoked review.
The complete serialized context must fit the existing 32768-byte envelope; nothing is truncated.
The unchanged original request is retained separately from typed untrusted local-only skill evidence; all tools and hosted fallback are denied.
Unqualified local providers truthfully report unavailable, without installation, network fallback or replay
([review lifetime](../src/Kora.Application/Skills/SharedSkillUseReview.cs), [workflow](../src/Kora.Application/Skills/SharedSkillInvocationWorkflow.cs), [host regression tests](../tests/Kora.Application.UnitTests/Dependencies/ModelTurnHostTests.SharedSkills.cs)).

### Reviewed one-tool shared skill invocation

This is a separate operation from instruction-only use. It supports a registered, single-file declarative `SKILL.md` that declares only `allowed-tools: network.get_web_page`. Scripts, additional files, unresolved references and other tool declarations remain unavailable. Kora does not supply a new bundled page skill; choose and review your own compatible source explicitly ([host workflow](../src/Kora.Application/Skills/ReviewedToolSkillInvocationWorkflow.cs)).

In the shared-source inspector, select **Review selected one-tool use**. Refresh existing Active conversations, select exactly one target and choose **Review exact target and tool revision**. Separately enable that revision, enter the explicit page address and original request, then **Review exact invocation** and confirm. Loading instructions or enabling a revision is not permission to access the page.

Typed input and current-name activated voice use the distinct `tool skill` commands:

```text
Kora, review tool skill <exact listed declarative skill name>
Kora, tool skill targets
Kora, tool skill target <exact displayed Active conversation ID>
Kora, tool skill review tool revision
Kora, tool skill enable
Kora, tool skill address https://example.com/
Kora, tool skill request <unchanged original prose>
Kora, tool skill review invocation
Kora, tool skill confirm invocation
Kora, tool skill close
```

Use **tool skill next targets** for the next metadata page. Native confirmation and activated-voice commands preserve the reviewed original input channel; changing channel or target requires fresh review. Registration and source discovery remain native-only ([routing](../src/Kora.Application/Skills/SharedSkillInvocationCommand.cs)).

The host admits at most one call to the existing WebPageGet action, with its own exact-destination, once-only approval. Review the page operation and approve or cancel it separately in the native approval window; a skill or model cannot approve it. The bounded local model loop can then use that untrusted page observation to produce a genuine retained answer in the original conversation.

The exact source/revision/digest, original channel, target and enablement must stay current. The review expires after two minutes and is single-use; privacy, ownership, source or target changes revoke it. The existing model byte limits and 15-second turn deadline apply. No script, MCP, agent or hosted disclosure is admitted. An unqualified local model remains explicitly unavailable; no automatic fallback or replay is attempted ([retention boundary](../src/Kora.Application/Hosting/SessionConversationService.ToolSkills.cs)).

**What “run” means**

Kora sends the current request and the exact selected bundled instructions
only to the configured local model. The artifact is source- and
version-qualified, and it remains selected if Kora asks a clarification
question.

Selecting an artifact does **not** run its embedded PowerShell, approve a
protected operation, create a grant, or mean an action succeeded. If the model
proposes a registered action, Kora still applies the ordinary host validation,
approval, privacy, audit, and execution rules. The current embedded
session-control scripts remain inspection-only.
