# Kora user guide

Kora is a Windows-only, local-first assistant with typed commands, explicit
push-to-talk, local speech and visual responses.

Start with typed commands; a microphone or AI account is not required.
Commands such as `help` and `open settings` work locally. Commands that fetch a
web page or check releases need a network connection and their own permission.

**AI answers are unavailable in this build.** Model-review and configuration
windows exist, but no local model is approved for use by this release, and hosted
account use is unavailable. Installing Ollama or enabling a model setting does
not change that. See [model availability](settings.md#models).

## Start here

Follow [Getting started](getting-started.md) for requirements, first launch and
setup. To try a command:

1. Enter **help** in the typed command box; or
2. Review voice consent, hold **Push to talk**, say **"Kora, what can you do?"**,
   then release.

If you renamed the assistant, use the configured name. Saved consent can arm
push-to-talk at startup, but does not record ambient audio. **Disable listening**
releases the microphone; device selection alone does not undo that choice.

## Everyday tasks

| Topic | Read |
|---|---|
| Requirements and first launch | [Getting started](getting-started.md) |
| Microphone, voices and output | [Voice and audio](voice-and-audio.md) |
| Output modes and manual call protection | [Responses and calls](responses-and-calls.md) |
| Preference controls, defaults and limits | [Settings](settings.md) |
| Exact phrases and parameters | [Voice and typed commands](commands.md) |
| What actions and skills can do now | [Tools and built-in skills](tools-and-built-in-skills.md) |
| Browse and manage reusable instructions, skills and agents | [Customizations](customizations.md) |
| Sessions, windows and tray recovery | [Windows and tray](windows-and-tray.md) |
| Accessible presence states and speech playback | [Presence accessibility](presence-accessibility.md) |
| Consent, grants, retained data and diagnostics | [Privacy, safety, and logs](privacy-safety-and-logs.md) |
| Problems and recovery | [Troubleshooting](troubleshooting.md) |

## Conversations and saved information

A **session** is a named conversation and its work records. Selecting a session
does not send your input, answer a question or approve work. Unsent text stays
in memory separately for each session; it is not saved when Kora exits.

| I want to… | Read |
|---|---|
| Choose where a new request goes | [Request destinations](session-routing.md) |
| Find sessions waiting for my answer | [Sessions needing an answer](waiting-questions.md) |
| Understand task-status notices | [Conversation notices](conversation-notices.md) |
| Review which actions need approval | [Session permission levels](session-permission-levels.md) |
| Review earlier messages for a follow-up | [Reviewed context](reviewed-context.md) |
| Dictate an unsent message — currently unavailable | [Session dictation](session-dictation.md) |
| Keep an explicit preference for later conversations | [Device-profile memory](profile-memory.md) |
| Review a model's proposed session memory | [Memory proposals](model-memory-proposals.md) |
| Keep a named local copy of a small text folder | [Knowledge folders](knowledge-sources.md) |
| Ask about a folder — currently unavailable | [Folder answers](folder-answers.md) |
| Ask about a diagnostic page — currently unavailable | [Evidence answers](evidence-answers.md) |

## Reusable guidance, models and connected tools

These pages explain the available editors and the separate reviews needed for
use. Saving guidance never runs it. Any step requiring an AI answer is subject
to the model availability above.

| I want to… | Read |
|---|---|
| Write or edit an instruction or reusable request | [Instruction and prompt editor](instruction-editor.md) |
| Use an instruction for just one request | [One-request instructions](conversation-instructions.md) |
| Apply an instruction until I stop it in this session | [Session instructions](session-instructions.md) |
| Create a reusable skill | [Skill authoring](owned-skill-authoring.md) |
| Create an agent profile | [Agent profile authoring](agent-profile-authoring.md) |
| Review a shared skill as agent guidance | [Shared skill guidance](agent-shared-skill-guidance.md) |
| Understand separate local agent runs and their limits | [Independent agent runs](independent-agent-runs.md) |
| Review a suggested preference change | [Setting suggestions](setting-proposals.md) |
| Ask about confirmed settings | [Setting inspection](setting-inspection.md) |
| Understand explicit Copilot requests | [Copilot requests](hosted-requests.md) |
| Understand explicit Claude requests | [Claude requests](claude-requests.md) |
| Understand an external tool connection | [MCP transport and availability](mcp-transport.md) |
| Ask about a reviewed external-tool result | [MCP result follow-up](mcp-result-follow-up.md) |
| Understand reviewed session-name sharing with a model | [Model session discovery](model-session-discovery.md) |
| Understand future script execution, not a current feature | [Execution design](skill-and-task-execution-design.md) |

## Reading results

| Topic | Read |
|---|---|
| Code colors, plain code and local selection | [Code highlighting](code-highlighting.md) |
| Diagram source and unavailable rendering | [Mermaid diagrams](diagrams.md) |
| Crossed-out wording and source fallback | [Markdown strikethrough](markdown-strikethrough.md) |
| Separately review opening a result in your browser | [External browser](external-browser.md) |

## Words used in this guide

- **Model**: the AI software that generates an answer. A local model runs on your
  device; a hosted model sends the reviewed request to another company's service.
- **Context**: extra material supplied with a question, such as earlier messages,
  an instruction or selected file excerpts. Viewing something does not
  automatically include it in a request.
- **Instruction / prompt / skill / agent**: guidance for how to answer, a reusable
  request, reusable task guidance, or a task profile with limits. None is permission
  to run code or access your information.
- **MCP**: Model Context Protocol, a way to connect tools offered by another
  service. Adding a server does not sign in, connect or approve a tool call.
- **USER / ASSISTANT / HOST**: your submitted words, a genuine model answer, or a
  status reported by Kora. A HOST failure is not an AI answer.
- **Unavailable / Unknown / Unconfirmed**: the feature cannot currently run, the
  result is not known, or completion could not be verified. Do not treat these
  as success or automatically repeat an action; inspect its status first.

## Open this guide

Choose **Help > Documentation** from the tray, type **open documentation**, or use
push-to-talk and say **"Kora, open documentation"**.

## Important limitations

- Exact commands are not fuzzy matching. See [Commands](commands.md) for accepted
  variants and the different approval behavior of direct and model-suggested
  actions.
- Shutdown and computer-restart commands display proposals only; they do not
  request Windows power changes.
- Automatic call detection is unavailable. Use [manual call mode](responses-and-calls.md).
- Ordinary responses during microphone capture are visual. Spoken previews and
  approval prompts close capture; a new voice request needs a new PTT activation.
  The separately enabled [conversational reply](settings.md#conversational-voice-replies)
  option applies only to eligible clarification questions, never approvals.
- File/clipboard inspection does not select model context or grant transmission
  consent. See [Privacy](privacy-safety-and-logs.md) before using retained content.
