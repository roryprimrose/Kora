---
title: Kora features
description: A plain-language guide to delivered, partly delivered, and planned Kora features.
reviewed: 2026-10-10
source-revision: 2f33199
---

## What you can use today

Kora is a Windows desktop assistant with typed commands, push-to-talk voice input, local spoken responses, and optional local AI answers. Many built-in commands need no AI model or internet connection. See [Getting started](docs/getting-started.md#requirements) and [Voice and typed commands](docs/commands.md).

This guide describes the implementation reviewed on **10 October 2026**, starting from `origin/main` at `2f33199`. It separates working features from the larger product design. Delivered does **not** mean the complete product has passed every real-device or release check; see the [delivery and qualification policy](Design/Acceptance_Criteria.md#three-tier-qualification-policy).

### How to read the status

- **Delivered**: you can use the specific feature described, subject to its stated requirements and limits.
- **Partly delivered**: something useful works, but an important part of the intended feature is still missing.
- **Not delivered**: the feature is designed or planned, but is not available to use.
- **Not planned for the first release**: the feature is outside the initial release scope; this is not a delivery promise.

Each row links to instructions or supporting design and implementation evidence. The [implementation roadmap](Design/Implementation_Roadmap.md) is the detailed engineering record; the descriptions here are intended for everyday users.

## Getting started and staying in control

| Feature | Status | What this means for you |
|---|---|---|
| Windows desktop app | Delivered | Use Kora on Windows 10 build 19041 or later; Windows 11 is recommended. See [requirements](docs/getting-started.md#requirements). |
| Use without AI | Delivered | Exact built-in commands and visual responses work without a model, cloud account, or network connection. Each command still has its own requirements. See [command-only use](docs/readme.md). |
| Readiness and setup help | Delivered | Inspect missing devices or software. Local-model and PowerShell setup need separate consent; opening setup installs nothing. See [first launch](docs/getting-started.md#first-launch). |
| Help inside the app | Delivered | Open the user guide from the tray or with `open documentation`; use `what can you do` to see commands. See [opening the guide](docs/readme.md#open-this-guide). |
| Tray controls | Delivered | Open settings and sessions, recover microphone access, stop speech, or exit without using voice. Hiding a window is not the same as disabling listening or exiting. See [tray controls](docs/windows-and-tray.md#tray-icon). |
| One active Kora instance | Delivered | Another launch can activate the existing app; switching between builds uses a separate takeover and return flow. See [launching another build](docs/windows-and-tray.md#launching-another-build-and-returning). |
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
| Interrupt speech | Delivered | Use Stop speaking or push-to-talk to interrupt playback. New voice replies need a new deliberate activation. See [interrupting responses](docs/responses-and-calls.md#interrupting-spoken-responses). |
| Optional speech captions | Delivered | Turn on text for the current spoken response. Set its corner and dismissal delay, pin it, or choose a display for this run. Captions are off by default and do not become saved transcripts. See [speech text](docs/settings.md#local-speech-text). |
| Manual call mode | Delivered | Tell Kora that you are on a call for this run and choose call-specific feedback. Call protection can still prevent speech even if you prefer audible replies. See [manual call mode](docs/responses-and-calls.md#manual-call-mode-and-call-settings). |
| Automatic call detection | Not delivered | Kora does not yet automatically detect your calls. Use manual call mode instead. See [current call limits](docs/readme.md#current-limitations). |
| Hands-free wake word | Not delivered | Saying the assistant name without pressing an activation control does not start production voice capture. The intended hands-free workflow still needs delivery and quality checks. See [voice activation status](Design/Implementation_Roadmap.md#delivered-and-partial-implementation). |
| Learn or verify a speaker's voice | Not delivered | Optional speaker learning and verification are future work, not a requirement for current push-to-talk use. See [optional features](Design/Implementation_Roadmap.md#optional-and-deferred-work). |

## AI answers and approvals

| Feature | Status | What this means for you |
|---|---|---|
| Local AI answers | Delivered | With the verified local Ollama model installed and allowed, an unmatched request can receive an answer, a clarification question, or a built-in action suggestion. This is not a full multi-step assistant. See [model settings](docs/settings.md#models) and [current answer flow](src/Kora.Windows/Dependencies/WindowsOllamaReasoner.cs). |
| Clarification choices | Delivered | Choose an answer on screen or through newly activated voice input. Answering a question never approves a separate action. See [questions and approvals](docs/settings.md#approvals). |
| Approve model-suggested disruptive actions | Delivered | Choose Once, This session, Always, or Reject for the named action, and revoke saved approvals. These approvals do not cover scripts; direct exact commands have different rules. See [approval limits](docs/settings.md#approvals). |
| Inspect and revoke exact approval records | Delivered | A separate tray window lists retained, specifically bound approval records. Inspect one and confirm its revocation; this does not enable a script executor. See [exact operation grants](docs/settings.md#exact-operation-grants). |
| Initial local/cloud preference | Partly delivered | You can save LocalOnly, LocalFirst, or HostedPreferred for a session's initial policy. HostedPreferred does not connect a cloud provider or permit an upload. See [provider-mode settings](docs/commands.md#inspect-or-change-the-device-local-provider-mode). |
| Cloud AI answers | Not delivered | There is no connected hosted provider or automatic cloud fallback. Turning on the hosted-model permission does not make one available. See [model settings](docs/settings.md#models). |
| Review a switch to another AI provider | Partly delivered | A local review window exists, but production has no qualified pending offers. Reviewing context alone sends nothing and enables no provider. See [handoff review](docs/commands.md#native-exact-handoff-review). |
| AI that runs tools and reasons over their results | Not delivered | The current local model can suggest a named action, but cannot continue a general tool-and-result conversation or read your previews as context. See [current model limits](docs/tools-and-built-in-skills.md). |
| Answers appearing as they are generated | Not delivered | Current local AI answers are buffered rather than displayed word by word. See [answer implementation](src/Kora.Windows/Dependencies/WindowsOllamaReasoner.cs). |

## Sessions, history, work, and memory

A session is a named place for Kora's saved records and supported work. Names are labels, not unique addresses: actions use the exact session identifier shown by Kora. Selecting a session does not silently redirect a voice request or approve work. See [session controls](docs/commands.md#bounded-exact-id-session-commands).

| Feature | Status | What this means for you |
|---|---|---|
| Create and manage saved sessions | Delivered | Create an empty session, rename it, inspect it, mark it Done when eligible, or explicitly resume it. Resuming does not rerun tasks or restore approvals. See [session commands](docs/commands.md#bounded-exact-id-session-commands). |
| Find a session | Delivered | Search saved names by a case-sensitive substring or enter the full session identifier. Results can span multiple pages; this is not conversation search. See [session-list search](docs/windows-and-tray.md#search-the-session-list). |
| Saved interaction history | Partly delivered | Read committed questions, final question answers, decision records, and task-status receipts. Ordinary typed/model conversations and response bodies are not a saved chat history. See [history limits](docs/windows-and-tray.md#bounded-passive-interaction-history). |
| Search and inspect saved receipts | Delivered | Search the supported retained history and open a matching record in a separate read-only detail window. Reading it does not replay work. See [history search and details](docs/windows-and-tray.md#bounded-passive-interaction-history). |
| Selected-session work panel | Delivered | See supported task and queue states, pending questions, capacity, and blockers. Refreshing or browsing does not start work; answer questions in their original window. See [Sessions work](docs/windows-and-tray.md#authoritative-sessions-work). |
| Queue local version checks | Delivered | Queue only the fixed, harmless Kora-version read, then explicitly dispatch ready checks. Cancel or remove pending entries. This is not a queue for arbitrary user requests. See [local-version queue](docs/commands.md#deterministic-local-version-queue). |
| Queue limits and deadlines | Delivered | Set capacity, fixed-read slots, pending lifetime, and active budget for future admissions. Existing entries and deadlines are not silently rewritten. See [queue settings](docs/commands.md#fixed-queue-settings). |
| General background work and parallel tasks | Not delivered | Useful independent AI or script tasks, general scheduling, and cancellation of running external effects are not enabled by the version-check queue. See [remaining work-management scope](Design/Implementation_Roadmap.md#complete-the-required-slice-a-product). |
| Keep a session | Delivered | Review and confirm Keep to hold automatic archive/delete, or restore ordinary retention. Ordinary dates can already be due; simply browsing does not postpone them. See [retention controls](docs/settings.md#per-session-retention-status-and-hold). |
| Remove a session from the workspace | Delivered | Review and separately confirm removal of its live records and redaction of supported history. This is not a guarantee that every recoverable or exported copy is erased. See [session removal](docs/windows-and-tray.md#logical-session-disposition). |
| Create and manage reviewed session memories | Delivered | Deliberately draft a fact or preference, inspect it, accept its review, then separately save it through admission. Edit, disable, or forget an exact memory. Unsaved proposals clear on closure or restart. See [memory controls](docs/commands.md#create-and-manage-reviewed-session-memories). |
| AI recall of saved memories | Not delivered | Saved session memories are not automatically attached to AI requests. Cross-session recall and conversational "Remember this" are unavailable. See [memory privacy and limits](docs/privacy-safety-and-logs.md#reviewed-session-memory-controls). |
| Full ongoing conversations | Not delivered | There is no complete per-session chat composer, saved model conversation, automatic context recall, or name-based voice routing. See [workspace gaps](Design/Implementation_Roadmap.md#complete-the-required-slice-a-product). |

## Clipboard, files, web pages, and viewing

| Feature | Status | What this means for you |
|---|---|---|
| Local clipboard preview | Delivered | Use `preview clipboard` to capture one temporary plain-text snapshot, up to 256 KiB. Inspect, reuse that same snapshot, or clear it; Kora does not watch your clipboard. See [clipboard preview](docs/commands.md#explicit-local-clipboard-preview). |
| Explain the clipboard | Partly delivered | The request currently offers a preview and states that explanation is unavailable. It does not send the snapshot to an AI model. See [clipboard limits](docs/commands.md#explicit-local-clipboard-preview). |
| Preview a text file | Delivered | Pick one supported local UTF-8 text or Markdown file, review its details, then confirm reading. The temporary preview is limited to 256 KiB and is not AI context. See [file preview](docs/commands.md#explicit-local-file-preview). |
| Preview a small folder | Delivered | Review 1-32 immediate text/Markdown files, up to 1 MiB combined and 256 KiB each. Subfolders or unsupported items reject the selection; there is no recursive scan. See [folder inspection](docs/windows-and-tray.md#native-local-file-inspection). |
| Search or refresh a preview | Delivered | Search the captured text for exact excerpts and source references. Refresh requires a new review and confirmation; it is not an automatic watcher. See [file search and refresh](docs/windows-and-tray.md#native-local-file-inspection). |
| Retain one text file in a session | Delivered | Confirm saving an immutable copy: one file per session, 256 KiB maximum, sixteen retained files across Kora's private store. Inspect it after restart or review removal of Kora's copies; the original is unchanged. See [session attachments](docs/commands.md#attach-one-text-file-to-an-exact-session-native-only). |
| Fetch an explicitly requested web page | Delivered | Use `get web page https://example.com/page` for a public text/HTML page. Kora returns bounded plain text, not a browser, and sends no model conversation. See [web retrieval](docs/commands.md#retrieve-an-explicit-web-page). |
| Inspect the fetched web result | Delivered | Open exact web-result details to search that temporary snapshot and inspect its source information. Opening or searching it makes no new request. Page text is not saved or sent to an AI model. See [web details](docs/responses-and-calls.md#visual-response-window). |
| Separate read-only detail windows | Delivered | Inspect supported guide pages and receipts without running their content. Private copying needs explicit disclosure confirmation. Unsupported rich content uses labelled source text. See [detail viewer](docs/windows-and-tray.md#passive-document-details). |
| AI answers grounded in files or web results | Not delivered | Preview, search, attachment, and web retrieval do not yet make that content available for model answers. See [file stages](Design/File_And_Folder_Ingestion.md#r26-file-and-folder-ingestion-delivery-plan) and [web limits](docs/commands.md#retrieve-an-explicit-web-page). |
| Managed knowledge libraries | Not delivered | Saved folder collections, multiple document versions, persistent search indexes, and meaning-based retrieval remain future work. See [knowledge stages](Design/File_And_Folder_Ingestion.md#r26-file-and-folder-ingestion-delivery-plan). |
| PDF, Office, images, or screen reading | Not delivered | Current file inspection is text/Markdown only. Broader formats, image text recognition, and screen context are deferred. See [future context formats](Design/Implementation_Roadmap.md#optional-and-deferred-work). |
| Rich browser-style answers | Not delivered | Interactive HTML, diagrams, and browser automation are not delivered by the plain-text viewers. See [viewer limits](docs/windows-and-tray.md#passive-document-details). |

## Skills, integrations, and computer actions

A skill is a reusable set of instructions for a task. Selecting its instructions is not the same as running its included scripts or approving an action. See [what "run" currently means](docs/commands.md#run-skills-and-future-artifacts).

| Feature | Status | What this means for you |
|---|---|---|
| Select reusable instructions | Delivered | Use the slash-command dropdown or activated voice to choose supported bundled skills, prompts, or instructions for the local-model request. Included scripts are not executed. See [instruction selection](docs/commands.md#run-skills-and-future-artifacts). |
| Inspect bundled skill packages | Delivered | Read the declared lock, shutdown, and restart package files in the native package viewer. Inspection does not enable their script execution. See [bundled-package status](Design/Implementation_Roadmap.md#r11-bounded-embedded-package-catalogue-and-native-review---2026-10-07). |
| Inspect shared profile skills | Delivered | Explicitly register a supported local skill folder, list and inspect its contents, recheck revisions, or withdraw read consent. Kora does not change shared files or give these skills to the model. See [shared-source inspection](docs/commands.md#inspect-shared-profile-skills-locally). |
| Execute skill scripts | Not delivered | Script-backed built-in skills and general PowerShell task workers are unavailable. Having PowerShell installed or an approval saved does not enable them. See [execution status](docs/tools-and-built-in-skills.md). |
| Create skills by talking to Kora | Not delivered | The planned Builder will support clarification, draft review, simulated checks, saving, and separate enablement. It is not available now. See [skill authoring](Design/MVP_Scope.md#slice-c-voice-driven-skill-authoring). |
| Connect external services through tools | Not delivered | The planned first connection is read-only and explicitly configured. There is no delivered general external-service connector. See [integration scope](Design/MVP_Scope.md#slice-b-read-only-mcp-and-skills). |
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
| Limited local status notices | Delivered | An already-open Sessions work panel can show trusted version-check, question, or cached maintenance notices. Review, dismiss, or defer them; this does not speak or start work. See [local events](docs/commands.md#trusted-local-events). |
| Quiet routine notices for this run | Delivered | In Sessions, turn Work/Maintenance notices and their rows off together until explicitly cleared/reset or restart. It starts Off and saves no quiet preference. Failures, required questions, authoritative work/status and recovery stay available; clear never replays a muted backlog. See [run-only quiet](docs/settings.md#routine-notice-quiet-mode-is-run-only). |
| General proactive conversations | Not delivered | The limited status panel is not an assistant that starts arbitrary conversations, reminders, or targeted voice interactions. See [remaining proactive work](Design/Implementation_Roadmap.md#complete-the-required-slice-a-product). |
| Windows installer and source-build routes | Partly delivered | Windows binary packaging and separate source build tools exist. Full installed-release, upgrade, and protection checks remain open; source tools build/stage rather than install or launch Kora. See [distribution status](Design/Implementation_Roadmap.md#delivered-and-partial-implementation). |
| Check for available releases | Delivered | Separately allow public metadata checks for this run, review a verified release, open its canonical page, or snooze the notice. This does not download or install an update. See [release maintenance](docs/settings.md#release-maintenance-notify-only). |
| Automatic in-app updates | Not delivered | Kora does not download, install, elevate, or replace itself through maintenance. Installation and replacement remain outside that workflow. See [maintenance limits](docs/settings.md#release-maintenance-notify-only). |

## Outside the first-release plan

| Feature | Status | What this means for you |
|---|---|---|
| Linux or macOS apps | Not planned for the first release | Windows is the only supported application platform, with no Linux/macOS delivery commitment. See [platform policy](Design/MVP_Scope.md#platform-support-policy). |
| Continuous transcription or clipboard monitoring | Not planned for the first release | The design does not include recording all surrounding conversation or watching clipboard changes. Future local wake detection is a separate capability. See [non-goals](Design/MVP_Scope.md#non-goals-for-the-first-release). |
| Skill marketplace or automatic shared-skill sync | Not planned for the first release | Marketplace distribution, automatic extension updates, and shared repository synchronization are outside the initial scope. See [non-goals](Design/MVP_Scope.md#non-goals-for-the-first-release). |
| Autonomous changes to Kora itself | Not planned for the first release | Skill authoring is not permission for an AI agent to change Kora's code, binaries, or security/update behavior. See [authoring boundary](Design/MVP_Scope.md#slice-c-voice-driven-skill-authoring). |
| Cross-device shared execution | Not planned for the first release | Cross-device coordination and unlimited multi-agent task workflows are not part of the initial product. See [non-goals](Design/MVP_Scope.md#non-goals-for-the-first-release). |

## Try a supported workflow

1. Open Kora and review [first-launch readiness](docs/getting-started.md#first-launch).
2. Enter `what can you do` in the typed command box, or use push-to-talk after [voice consent](docs/voice-and-audio.md#voice-consent-and-explicit-push-to-talk).
3. Try `preview clipboard` for [local text inspection](docs/commands.md#explicit-local-clipboard-preview), or `preview file` for a [reviewed local file preview](docs/commands.md#explicit-local-file-preview).
4. Open [Sessions](docs/windows-and-tray.md#authoritative-sessions-work) to inspect supported saved records; do not expect a full saved AI conversation.
5. Use [Settings](docs/settings.md) to choose devices, speech, appearance, and privacy preferences.

For exact wording and limits, use the [command reference](docs/commands.md). For help with unavailable devices, setup, or saved state, use [Troubleshooting](docs/troubleshooting.md).
