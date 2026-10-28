---
title: Kora features
description: What Kora can do, what needs a separate review, and what is unavailable.
reviewed: 2026-10-28
---

## What you can use today

Kora is a Windows desktop assistant with typed commands, push-to-talk voice input,
local speech and on-screen results. Start with the [user guide](docs/readme.md)
or [first-launch instructions](docs/getting-started.md).

**AI answers are unavailable in this build.** The app includes model settings
and reviewed request workflows, but this release approves no local model for
use and its hosted account approvals are unavailable. Installing a model,
saving a profile or turning on a permission does not enable answers.
Built-in commands, editors and local inspection remain useful without AI.

### How to read the status

- **Delivered**: the specific operation exists, subject to its stated requirements and limits. It does not imply every optional model or device is ready.
- **Partly delivered**: something useful works, but an important part of the intended feature is still missing.
- **Not delivered**: the feature is designed or planned, but is not available to use.
- **Not planned for the first release**: the feature is outside the initial release scope; this is not a delivery promise.

Follow each guide for the actual controls, required confirmations and recovery.
Where a review window exists but the operation cannot complete in this build,
the row says so. Planned behavior is not a promise of availability.

## Getting started and staying in control

| Feature | Status | What this means for you |
|---|---|---|
| Windows desktop app | Delivered | Use Kora on Windows 10 build 19041 or later; Windows 11 is recommended. See [requirements](docs/getting-started.md#requirements). |
| Use without AI | Delivered | Built-in commands and visual responses need no model or AI account. Web retrieval and release checks still need a network connection and their own permission. See [command-only use](docs/readme.md). |
| Readiness and setup help | Delivered | Inspect missing devices or software. Local-model and PowerShell setup need separate consent; opening setup installs nothing. See [first launch](docs/getting-started.md#first-launch). |
| Help inside the app | Delivered | Open the user guide from the tray or with `open documentation`; use `what can you do` to see commands. See [opening the guide](docs/readme.md#open-this-guide). |
| Tray controls | Delivered | Show Kora, open settings and sessions, recover microphone access, or exit without using voice. Hiding a window is not the same as disabling listening or exiting. See [tray controls](docs/windows-and-tray.md#tray-icon). |
| One active Kora instance | Delivered | Another Release launch activates the existing app rather than starting a second assistant. Debug builds have a separate reviewed build-switching flow. See [launching another build](docs/windows-and-tray.md#launching-another-build-and-returning). |
| Theme and appearance | Delivered | Choose light, dark, or system appearance, adjust the animated presence, and change response-window visibility and timeout. See [appearance settings](docs/settings.md#appearance). |
| Rename the assistant | Delivered | Change the displayed name and the name used before activated voice commands. This does not create an always-listening wake word. See [assistant-name setting](docs/commands.md#assistant-display--ptt-command-prefix-setting). |

## Voice, speech, and calls

| Feature | Status | What this means for you |
|---|---|---|
| Push-to-talk voice commands | Delivered | After voice consent and readiness checks, hold Push to talk, speak, and release. Audio is not continuously transcribed. See [voice consent and push-to-talk](docs/voice-and-audio.md#voice-consent-and-explicit-push-to-talk). |
| Stop listening | Delivered | Disable listening to release the microphone. A device change alone does not restart capture after you disabled it. See [microphone recovery](docs/windows-and-tray.md#microphone-and-listening-recovery). |
| Microphone and speaker selection | Delivered | Follow Windows defaults or choose specific devices. Missing devices require explicit recovery rather than silently substituting a saved choice. See [microphone selection](docs/voice-and-audio.md#microphone-selection) and [speaker selection](docs/voice-and-audio.md#audio-output-selection). |
| Local spoken responses | Delivered | Use installed Windows voices or separately download the optional Kokoro voice provider. Preview a voice and stop playback. See [speech providers](docs/voice-and-audio.md#speech-providers). |
| Speech controls | Delivered | Change Kora's playback volume, Windows speech rate, and spoken-summary limits. Long ordinary answers stay visual rather than being silently cut short. See [speech and audio settings](docs/settings.md#speech-and-audio) and [speech rate](docs/settings.md#windows-native-speech-rate). |
| Text, speech, or both | Delivered | Choose visual-only, audible-only, or both. Required safety and recovery information stays visible, including when speech is unavailable. See [response modes](docs/responses-and-calls.md#response-modes). |
| Interrupt speech | Delivered, with limits | Use Stop speaking or Cancel reading. Push-to-talk interruption of retained-answer playback is unavailable in the shipped speech profile. See [speech interruption](docs/voice-and-audio.md#exact-session-speech-interruption). |
| Optional speech captions | Delivered | Turn on text for the current spoken response. Set its corner and dismissal delay, pin it, or choose a display for this run. Captions are off by default and do not become saved transcripts. See [speech text](docs/settings.md#local-speech-text). |
| Manual call mode | Delivered | Tell Kora that you are on a call for this run and choose call-specific feedback. Call protection can still prevent speech even if you prefer audible replies. See [manual call mode](docs/responses-and-calls.md#manual-call-mode-and-call-settings). |
| Automatic call detection | Not delivered | Kora does not yet automatically detect your calls. Use manual call mode instead. See [current call limits](docs/readme.md#important-limitations). |
| Hands-free wake word | Not delivered | Saying the assistant name without pressing an activation control does not start production voice capture. The intended hands-free workflow still needs delivery and quality checks. See [voice activation status](Design/Implementation_Roadmap.md#current-delivery). |
| Learn or verify a speaker's voice | Not delivered | Optional speaker learning and verification are future work, not a requirement for current push-to-talk use. See [optional features](Design/Implementation_Roadmap.md#optional-and-deferred-work). |

## AI answers and approvals

| Feature | Status | What this means for you |
|---|---|---|
| Local AI answers | Not available in this build | The local answer workflow exists, but this release approves no model for use. Setup and user consent alone cannot enable it. See [model settings](docs/settings.md#models). |
| Clarification choices | Delivered | Choose an answer on screen or through newly activated voice input. Answering a question never approves a separate action. See [questions and approvals](docs/settings.md#approvals). |
| Approve model-suggested disruptive actions | Delivered | Choose Once, This session, Always, or Reject for the named action, and revoke saved approvals. These approvals do not cover scripts; direct exact commands have different rules. See [approval limits](docs/settings.md#approvals). |
| Inspect and revoke approval records | Delivered | In Settings, inspect one saved permission or review and separately confirm revoking a selected set. Revoking permission is not stopping a running action. See [exact operation grants](docs/settings.md#exact-operation-grants). |
| Read a saved approval's history | Delivered | Inspect the security records associated with that approval. Results disclose missing or unavailable records; they do not prove every effect completed or that the history is exhaustive. See [grant history](docs/settings.md#exact-operation-grants). |
| Filter saved approvals | Delivered | Filter by the supported scope, status, action or original session. Pages are limited; continue with Next even when a filtered page is empty. Filtering changes no permission. See [filters](docs/settings.md#exact-operation-grants). |
| Choose a session's approval policy | Delivered | Review all, Model review and Approve all affect only that session. Model review still asks you because its reviewer is unavailable. Mandatory privacy, authentication and transmission reviews always apply. See [session permission levels](docs/session-permission-levels.md). |
| Review a suggested setting change | Partly delivered, model unavailable | Supported retained suggestions have a separate review and confirmation; model text never changes settings by itself. Direct settings remain available. See [setting suggestions](docs/setting-proposals.md). |
| Choose a service and model | Partly delivered | Save app choices and per-session overrides. Model choices remain unavailable where the service is not ready; saved choices never approve transmission. See [provider settings](docs/settings.md#app-default-service-and-desired-profiles). |
| Cloud AI answers | Not available in this build | Explicit Copilot and Claude workflows exist, but their required account-use approvals are unavailable. There is no automatic cloud fallback. See [Copilot](docs/hosted-requests.md) and [Claude](docs/claude-requests.md). |
| Review a switch to another AI provider | Partly delivered | A local review window exists, but production has no qualified pending offers. Reviewing context alone sends nothing and enables no provider. See [handoff review](docs/commands.md#native-exact-handoff-review). |
| AI with reviewed tools | Partly delivered, runtime unavailable | Limited local workflows can use reviewed read-only tools or one approved public-page retrieval. They are not general automation, and this build's unavailable model cannot run them. See [tools and skills](docs/tools-and-built-in-skills.md). |
| Answers appearing as they are generated | Partly delivered, runtime unavailable | Provisional-answer presentation and a separately reviewed Claude streaming path exist. They do not enable a model or save incomplete text as an answer. See [Claude streaming](docs/claude-requests.md#separately-qualified-native-text-streaming). |

## Sessions, history, work, and memory

A session holds a conversation and its work records. Names can repeat, so check
the selected target; some advanced commands require its full identifier.
Selecting a session never silently redirects a voice request or approves work.
See [session controls](docs/windows-and-tray.md#expand-and-collapse-the-interaction-window).

| Feature | Status | What this means for you |
|---|---|---|
| Create and manage saved sessions | Delivered | Create an empty session, rename it, inspect it, mark it Done when eligible, or explicitly resume it. Resuming does not rerun tasks or restore approvals. See [session commands](docs/commands.md#bounded-exact-id-session-commands). |
| Find a session | Delivered | Search saved names by a case-sensitive substring or enter the full session identifier. Results can span multiple pages; this is not conversation search. See [session-list search](docs/windows-and-tray.md#search-the-session-list). |
| Saved conversations and activity | Delivered, with limits | Explicit session submissions retain your original message and Kora's outcome; only a genuine successful model answer is an ASSISTANT message. General command responses are not automatically saved chat. Activity separately shows questions, decisions and work records. See [conversations](docs/windows-and-tray.md#exact-session-conversation-and-composer). |
| New request | Delivered; AI unavailable | Open a fresh request, create its conversation, then separately submit. Closing protects unsent text with Discard/Cancel. Submitting cannot enable an unavailable model. See [New request](docs/windows-and-tray.md#new-request-with-a-fresh-session). |
| Unsent conversation input | Delivered | Each conversation keeps its own text in memory while you switch topics. Sessions and compact views share it. There is no disk autosave; old saved text can be recovered read-only into empty input. See [composer](docs/windows-and-tray.md#exact-session-conversation-and-composer). |
| Submit a prepared prompt | Delivered submission; AI unavailable | Fill required inputs, review the complete expanded request and separately confirm submission to an existing Active conversation. The template and other unsent input stay unchanged. See [prompt submission](docs/instruction-editor.md#submit-an-expanded-request-to-one-exact-session). |
| Search saved messages | Delivered | Search one conversation or explicitly search retained messages across conversations, then open the original match. Reading never replays work or sends history to a model. See [conversation search](docs/windows-and-tray.md#find-a-message-across-conversations). |
| Search and inspect saved receipts | Delivered | Search the supported retained history and open a matching record in a separate read-only detail window. Reading it does not replay work. See [history search and details](docs/windows-and-tray.md#bounded-passive-interaction-history). |
| Selected-session work panel | Delivered | See supported task and queue states, pending questions, capacity, and blockers. Refreshing or browsing does not start work; answer questions in their original window. See [Sessions work](docs/windows-and-tray.md#authoritative-sessions-work). |
| Return to a dismissed version question | Delivered, current run only | Reopen that still-live question and its saved answer draft, not unsaved edits. Its five-minute expiry continues. Restart or completion makes recovery unavailable. See [limits](docs/windows-and-tray.md#return-to-the-current-version-question). |
| All work / attention overview | Delivered | Manually page supported work observations, including sessions needing an answer. Missing data and omissions stay visible; an empty page is not proof that all work is clear. See [overview](docs/windows-and-tray.md#all-work--attention-overview) and [waiting questions](docs/waiting-questions.md). |
| Older diagnostic work | Inspection and cancellation only | Existing records can still be inspected or cancelled where offered. Creating and dispatching new diagnostic-only queues is retired. See [limits](docs/commands.md#deterministic-local-version-queue). |
| Cancel selected pending diagnostic entries | Delivered, limited | Review one to ten selected entries in one current-run session, then confirm cancelling the complete selected set. This neither terminates running work nor starts other entries. See [queue controls](docs/commands.md#deterministic-local-version-queue). |
| Independent agent runs | Partly delivered, runtime unavailable | Two separately reviewed local runs can target different existing conversations. Overlapping execution is unavailable by default; this is not general background scheduling or script execution. See [independent agents](docs/independent-agent-runs.md). |
| Keep a session | Delivered | Review and confirm Keep to hold automatic archive/delete, or restore ordinary retention. Ordinary dates can already be due; simply browsing does not postpone them. See [retention controls](docs/settings.md#per-session-retention-status-and-hold). |
| Remove a session from the workspace | Delivered | Review and separately confirm removal of its live records and redaction of supported history. This is not a guarantee that every recoverable or exported copy is erased. See [session removal](docs/windows-and-tray.md#logical-session-disposition). |
| Create and manage reviewed session memories | Delivered | Deliberately draft a fact or preference, inspect it, accept its review, then separately save it through admission. Edit, disable, or forget an exact memory. Unsaved proposals clear on closure or restart. See [memory controls](docs/commands.md#create-and-manage-reviewed-session-memories). |
| Explicit memory for later conversations | Delivered management; answers unavailable | Review and save device-profile memories, then separately select and review them for one local request. There is no automatic recall. See [device-profile memory](docs/profile-memory.md) and [session-memory proposals](docs/model-memory-proposals.md). |
| Earlier messages as answer context | Partly delivered, runtime unavailable | Deliberately review selected history, reply to an exact answer, or combine history, an instruction and session memories. Ordinary submission does not silently include them. See [reviewed context](docs/reviewed-context.md). |
| Dictate an unsent message | Not available in this build | The review controls exist, but literal dictation is not enabled by the shipped Windows capture adapter. Type instead. See [dictation](docs/session-dictation.md). |

## Clipboard, files, web pages, and viewing

| Feature | Status | What this means for you |
|---|---|---|
| Local clipboard preview | Delivered | Use `preview clipboard` to capture one temporary plain-text snapshot, up to 256 KiB. Inspect, reuse that same snapshot, or clear it; Kora does not watch your clipboard. See [clipboard preview](docs/commands.md#explicit-local-clipboard-preview). |
| Explain the clipboard | Partly delivered | The request currently offers a preview and states that explanation is unavailable. It does not send the snapshot to an AI model. See [clipboard limits](docs/commands.md#explicit-local-clipboard-preview). |
| Preview a text file | Delivered | Pick one supported local UTF-8 text or Markdown file, review its details, then confirm reading. The temporary preview is limited to 256 KiB and is not AI context. See [file preview](docs/commands.md#explicit-local-file-preview). |
| Preview a small folder | Delivered | Review 1-32 immediate text/Markdown files, up to 1 MiB combined and 256 KiB each. Subfolders or unsupported items reject the selection; there is no recursive scan. See [folder inspection](docs/windows-and-tray.md#native-local-file-inspection). |
| Search or refresh a preview | Delivered | Search the captured text for exact excerpts and source references. Refresh requires a new review and confirmation; it is not an automatic watcher. See [file search and refresh](docs/windows-and-tray.md#native-local-file-inspection). |
| Follow a file citation | Delivered | Select a search result and **Show exact cited source span** to read the original captured passage. This does not reopen a live file, copy text or send model context. See [source navigation](docs/windows-and-tray.md#show-an-exact-cited-source-span). |
| Find text in an inspected file | Delivered | Find first, next or previous exact case-sensitive text in the displayed original. Search does not wrap at the end or grant copy permission. See [Find](docs/windows-and-tray.md#find-a-literal-in-the-currently-inspected-original-file). |
| Search Markdown by structure | Delivered, limited | Optionally search captured headings and code blocks instead of ordinary lines. The choice is not saved and sends nothing to a model. See [search modes and limits](docs/windows-and-tray.md#opt-in-markdown-search-in-an-exact-file-view). |
| Retain one text file in a session | Delivered | Confirm saving an immutable copy: one file per session, 256 KiB maximum, sixteen retained files across Kora's private store. Inspect it after restart or review removal of Kora's copies; the original is unchanged. See [session attachments](docs/commands.md#attach-one-text-file-to-an-exact-session-native-only). |
| Browse retained attachment details | Delivered | Inspect attachment records or search by exact session identifier, then explicitly open a current source. This catalogue does not search filenames or file contents. See [rules and limits](docs/windows-and-tray.md#retained-session-attachments-metadata-only). |
| Replace a retained session file | Delivered | In an Active session's file viewer, pick a new file, review both identities and confirm separately. Kora replaces only that slot after old-copy verification; uncertainty holds disclosure for reviewed removal. Originals stay unchanged. See [replacement and recovery](Design/File_And_Folder_Ingestion.md#delivered-reviewed-retained-attachment-replacement---2026-10-10). |
| Fetch an explicitly requested web page | Delivered | Use `get web page https://example.com/page` for a public text/HTML page. Kora returns bounded plain text, not a browser, and sends no model conversation. See [web retrieval](docs/commands.md#retrieve-an-explicit-web-page). |
| Inspect the fetched web result | Delivered | Open exact web-result details to search that temporary snapshot and inspect its source information. Opening or searching it makes no new request. Page text is not saved or sent to an AI model. See [web details](docs/responses-and-calls.md#visual-response-window). |
| Separate read-only detail windows | Delivered | Inspect supported guide pages and receipts without running their content. Private copying needs explicit disclosure confirmation. Unsupported rich content uses labelled source text. See [detail viewer](docs/windows-and-tray.md#passive-document-details). |
| AI answers grounded in files or other inspected information | Partly delivered, unavailable in this build | Separate answer reviews exist for files, folders, attachments and diagnostic pages. Their required source-use permission checks and/or model runtime remain unavailable. Viewing is never model-use consent. See [folder answers](docs/folder-answers.md), [attachment answers](docs/responses-and-calls.md#answer-from-a-retained-session-attachment) and [evidence answers](docs/evidence-answers.md). |
| Named knowledge folders | Delivered | Review and save private local snapshots of small text folders; explicitly inspect, search, refresh, disable or remove Kora's copies. This does not enable model answers, automatic refresh or meaning-based search. See [knowledge sources](docs/knowledge-sources.md). |
| PDF, Office, images, or screen reading | Not delivered | Current file inspection is text/Markdown only. Broader formats, image text recognition, and screen context are deferred. See [future context formats](Design/Implementation_Roadmap.md#optional-and-deferred-work). |
| Rich browser-style answers | Not delivered | Interactive HTML, diagrams, and browser automation are not delivered by the plain-text viewers. See [viewer limits](docs/windows-and-tray.md#passive-document-details). |

## Skills, integrations, and computer actions

A skill is a reusable set of instructions for a task. Selecting its instructions is not the same as running its included scripts or approving an action. See [what "run" currently means](docs/commands.md#run-skills-and-artifacts).

| Feature | Status | What this means for you |
|---|---|---|
| Select reusable instructions | Delivered | Use the slash-command dropdown or activated voice to choose supported bundled skills, prompts, or instructions for the local-model request. Included scripts are not executed. See [instruction selection](docs/commands.md#run-skills-and-artifacts). |
| Edit Kora-owned instructions and saved prompts | Delivered | Create, validate, compare and save reviewed changes. Saving never enables use. Separately review an enabled instruction for one request or an Active session; generating answers still needs an available model. See [editor](docs/instruction-editor.md), [one-request use](docs/conversation-instructions.md) and [session use](docs/session-instructions.md). |
| Prepare required owned prompt inputs | Delivered, native v2 only | Required literal `${input:name}` fields, exact whole expanded USER REQUEST preview and separate fresh model-admission check. Private ephemeral values; no defaults/expressions/reads/recursive expansion. V1 remains literal. Generation stays unavailable with no qualified production adapter or rejected-runtime fallback. See [native inputs](docs/instruction-editor.md#prepare-required-native-prompt-inputs). |
| Inspect bundled skill packages | Delivered | Read the declared lock, shutdown, and restart package files in the native package viewer. Inspection does not enable their script execution. See [bundled-package status](Design/Archive/Implementation_Roadmap_2026-10-11.md#r11-bounded-embedded-package-catalogue-and-native-review---2026-10-07). |
| Inspect shared profile skills | Delivered | Explicitly register a supported local skill folder, list and inspect its contents, recheck revisions, or withdraw read consent. Registration alone does not expose instructions to a model or authorize an action. See [shared-source inspection](docs/commands.md#inspect-shared-profile-skills-locally). |
| Invoke an explicitly reviewed declarative shared revision | Delivered, qualified local profile required | Separate exact-revision, single-use zero-tool and one-WebPageGet paths retain results in the selected original conversation. The one-tool path requires its own exact page approval; no scripts or hosted disclosure. Unqualified local models remain unavailable. See [reviewed one-tool invocation](docs/commands.md#reviewed-one-tool-shared-skill-invocation). |
| Search listed skills | Delivered | Search names, identifiers and descriptions in the already listed catalogue. Searching neither reads skill bodies nor runs anything, and unavailable entries remain labelled. See [skill search](docs/commands.md#search-admitted-skill-package-metadata). |
| Execute skill scripts | Not delivered | Script-backed built-in skills and general PowerShell task workers are unavailable. Having PowerShell installed or an approval saved does not enable them. See [execution status](docs/tools-and-built-in-skills.md). |
| Create reusable skills and agent profiles | Delivered editors; model use unavailable | Use native, typed or supported activated-voice controls to prepare declarative guidance. Saving new files needs separate native confirmation. There is no general script builder. See [skills](docs/owned-skill-authoring.md) and [agents](docs/agent-profile-authoring.md). |
| Discover useful customizations | Partly delivered, runtime unavailable | Explicitly choose and review a small set of descriptions for one local recommendation. Recommendations never enable or invoke anything automatically. See [customization discovery](docs/customizations.md#discover-which-selected-customizations-may-help). |
| Connect external services through MCP | Partly delivered, account unavailable | Reviewed HTTPS read-only workflows exist, including a model-free route, but no delegated MCP account is enabled in the shipped host. Adding a server is not consent or authentication. See [MCP transport](docs/mcp-transport.md). |
| Lock Windows | Delivered | An exact built-in lock phrase requests Windows locking directly; a model-suggested lock uses action approval. A request being accepted is not independent proof of lock completion. See [Windows session task](docs/commands.md#windows-session-task). |
| Shut down or restart Windows | Partly delivered | These commands create visible, cancellable proposals only. They do not shut down or restart the computer. See [power-proposal tasks](docs/commands.md#protected-power-proposal-tasks). |
| Restart or exit Kora | Delivered | Restart the current app or exit it; this is different from restarting Windows. See [application tasks](docs/commands.md#window-and-application-tasks). |
| Launch arbitrary apps or automate the desktop | Not delivered | General application launching, executable imports, repository writes, and broader desktop automation are deferred. See [deferred execution](Design/Implementation_Roadmap.md#optional-and-deferred-work). |

## Privacy, troubleshooting, and updates

| Feature | Status | What this means for you |
|---|---|---|
| Explicit microphone consent and privacy recovery | Delivered | Voice requires consent and current privacy checks. Lock, ownership, permission, or device problems can stop capture and private presentation; recover explicitly. See [microphone consent](docs/privacy-safety-and-logs.md#microphone-consent) and [troubleshooting](docs/troubleshooting.md). |
| Inspect diagnostic logs | Delivered | Use the Evidence window for supported local database or daily-file records, including optional combined inspection. Filter by supported identities, time, severity, or outcome. This is not AI analysis or an export tool. See [logs and filters](docs/privacy-safety-and-logs.md#logs). |
| Inspect committed approval/audit records | Delivered | A separate evidence source reads actual committed security records, not similar-looking diagnostic messages. It is not proof that every external effect completed. See [audit inspection](docs/privacy-safety-and-logs.md#committed-authority-audit-inspection). |
| Retention settings | Delivered | Set supported future diagnostic/audit deadlines and session retention. Changes do not rewrite old deadlines or add "delete everything now". See [diagnostic](docs/settings.md#sqlite-diagnostic-retention), [audit](docs/settings.md#future-only-audit-retention), and [session retention](docs/settings.md#per-session-retention-status-and-hold). |
| Limited local status notices | Delivered | An already-open Sessions work panel can show supported conversation, question or maintenance notices. Review, dismiss or defer them; this does not speak or start work. See [conversation notices](docs/conversation-notices.md). |
| Quiet routine notices for this run | Delivered | Sessions can mute Work/Maintenance notices and rows until clear/reset or restart. It starts Off; no quiet preference is saved. Failures, questions, work/status and recovery stay available; no muted backlog replay. See [run-only quiet](docs/settings.md#routine-notice-quiet-mode-is-run-only). |
| General proactive conversations | Not delivered | The limited status panel is not an assistant that starts arbitrary conversations, reminders, or targeted voice interactions. See [remaining proactive work](Design/Archive/Implementation_Roadmap_2026-10-11.md#complete-the-required-slice-a-product). |
| Windows installer and source-build routes | Partly delivered | Windows binary packaging and separate source build tools exist. Full installed-release, upgrade, and protection checks remain open; source tools build/stage rather than install or launch Kora. See [distribution status](Design/Archive/Implementation_Roadmap_2026-10-11.md#delivered-and-partial-implementation). |
| Check for available releases | Delivered | Separately allow public metadata checks for this run, review a verified release, open its canonical page, or snooze the notice. This does not download or install an update. See [release maintenance](docs/settings.md#release-maintenance-notify-only). |
| Automatic in-app updates | Not delivered | Kora does not download, install, elevate, or replace itself through maintenance. Installation and replacement remain outside that workflow. See [maintenance limits](docs/settings.md#release-maintenance-notify-only). |

## Outside the first-release plan

| Feature | Status | What this means for you |
|---|---|---|
| Linux or macOS apps | Not planned for the first release | Windows is the only supported application platform, with no Linux/macOS delivery commitment. See [platform policy](Design/MVP_Scope.md#platform-support-policy). |
| Continuous transcription or clipboard monitoring | Not planned for the first release | The design does not include recording all surrounding conversation or watching clipboard changes. Future local wake detection is a separate capability. See [non-goals](Design/MVP_Scope.md#non-goals-for-the-first-release). |
| Skill marketplace or automatic shared-skill sync | Not planned for the first release | Marketplace distribution, automatic extension updates, and shared repository synchronization are outside the initial scope. See [non-goals](Design/MVP_Scope.md#non-goals-for-the-first-release). |
| Autonomous changes to Kora itself | Not planned for the first release | Skill authoring is not permission for an AI agent to change Kora's code, binaries, or security/update behavior. See [authoring boundary](Design/MVP_Scope.md#slice-c-voicetext-skill-and-instruction-authoring). |
| Cross-device shared execution | Not planned for the first release | Cross-device coordination and unlimited multi-agent task workflows are not part of the initial product. See [non-goals](Design/MVP_Scope.md#non-goals-for-the-first-release). |

## Try a supported workflow

1. Open Kora and review [first-launch readiness](docs/getting-started.md#first-launch).
2. Enter `what can you do` in the typed command box, or use push-to-talk after [voice consent](docs/voice-and-audio.md#voice-consent-and-explicit-push-to-talk).
3. Try `preview clipboard` for [local text inspection](docs/commands.md#explicit-local-clipboard-preview), or `preview file` for a [reviewed local file preview](docs/commands.md#explicit-local-file-preview).
4. Open [Sessions](docs/windows-and-tray.md#authoritative-sessions-work) to inspect supported saved records; do not expect a full saved AI conversation.
5. Use [Settings](docs/settings.md) to choose devices, speech, appearance, and privacy preferences.

For exact wording and limits, use the [command reference](docs/commands.md). For help with unavailable devices, setup, or saved state, use [Troubleshooting](docs/troubleshooting.md).
