# Kora - Local First Voice Agent Platform

This document describes the long-term vision, not the initial release scope.
See the [design document index](README.md) for implementation decisions, extensibility boundaries, delivery scope, and acceptance criteria.

## Vision

Kora is a voice-first, local-first agent platform for Windows that enables users to interact with knowledge, tools, enterprise systems, code repositories, and AI models without typing.
Windows remains the only supported application platform for the foreseeable future.
Implementation preserves multi-platform extensibility through portable shared logic and first-party native integration boundaries, without promising future ports.
See [Platform Boundaries and Support](Architecture.md#platform-boundaries-and-support).

Primary goals:

- Voice-first convenience with complete voice, mouse/keyboard, and mixed-channel workflow parity
- Persistent concurrent work sessions with retrievable conversation, decisions, artifacts, actions, and grant history
- Every supported user preference discoverable and configurable verbally through host-owned settings
- Configurable local voice activation name, with an explicit custom-only/default-plus-custom choice and custom-only recommended in shared offices
- Local processing wherever practical
- User-owned knowledge and configuration
- Pluggable AI providers
- MCP-native architecture
- Extensible skills and scripts
- Enterprise integration through Work IQ and other connectors
- Strong security and approval boundaries

---

# Product Principles

## Voice First, Not Voice Only

Users should be able to accomplish every supported Kora workflow without compulsory typing, or entirely through accessible UI without compulsory speech.
Mandatory OS/provider authentication and unavailable microphone constraints remain explicit.
During protected calls, voice-initiated voice-setting and in-call-option changes are rejected and require a new UI request; this is the explicit privacy/security exception, not a general restriction on voice operation.
The compact latest-interaction UI, Sessions workspace with a session list beside full conversation/history, and separate immutable detail/script viewer share host-owned questions and approvals.
See [Human Interaction and Persistent Sessions](Interaction_And_Sessions.md) and [Coordinated Window Design](UI_Workspace_And_Windows.md).
Kora can also initiate conversation, for example to offer an available update or ask for clarification on current work.
Suggestions are not authority to execute actions; see [Proactive Voice Interaction](Proactive_Interaction.md).
Speech respects the shared configurable call policy, with Teams detection where supported and UI-only feedback by default during calls; call-time configuration follows the initiating-channel restriction.
See [Call-Aware Speech](Call_Aware_Speech.md).

Examples:

- "Kora, summarize my meetings today"
- "Kora, use the clipboard and explain this exception"
- "Kora, create a new skill"
- "Kora, investigate yesterday's deployment"

## Local First

The application should execute locally whenever possible.

Local capabilities:

- Microphone capture
- Speech recognition
- Text-to-speech
- Knowledge search
- Skill execution
- Script execution
- MCP server hosting
- Context management
- Security policy enforcement

Remote services are used only when required.

The implementation makes this explicit through local-only and remote-enabled modes.
Local-only mode never silently falls back to remote processing.
See [Security and Data Flows](Security_Data_Flows.md).

## Model Agnostic

Kora should not depend on a single model vendor.

Supported providers:

- GitHub Copilot
- Local Ollama models
- Azure AI Foundry
- OpenAI-compatible endpoints
- Future providers

---

# High Level Architecture

```text
Voice Input
     |
     v
Speech Recognition
     |
     v
Conversation Engine
     |
     +-------------------+
     |                   |
     v                   v
Knowledge Engine     Intent Router
     |                   |
     +---------+---------+
               |
               v
        Agent Orchestrator
               |
   +-----------+-----------+
   |           |           |
   v           v           v
 Skills      MCP       Scripts
 Runtime     Layer     Runtime
   |           |           |
   +-----------+-----------+
               |
               v
         Response Engine
         /            \
        /              \
       v                v
Visual Response    Voice Response
```

---

# Core Components

## Speech Layer

### Responsibilities

- Microphone capture
- Local "Kora" wake-word activation from the first delivery slice
- Optional push-to-talk
- Local speech recognition
- Noise suppression
- Voice activity detection
- Playback-aware wake interruption without self-activation

Verbal activation is the primary interaction after explicit microphone consent.
Ambient audio remains local and is not continuously transcribed.
See [Task Lifecycle](Task_Lifecycle.md#wake-listening-and-command-capture).

### Preferred Technologies

- NAudio
- whisper.cpp
- Whisper.Net

Future:

- Speaker recognition
- Voice isolation

---

## Conversation Engine

Maintains:

- Conversation history
- Active task
- Pending requests and an authoritative work ledger
- Current context
- Clipboard references
- Working memory

Responsibilities:

- Context management
- Intent classification
- Clarification prompts
- Voice-friendly interactions

Work management remains responsive alongside task execution, interpreting queue changes in context and asking when intent is ambiguous.
The design supports bounded concurrent work sessions, with isolated context/approvals and coordinated shared-resource access.
Session selection is presentation focus, not cancellation or permission to merge work.
Status answers distinguish observed progress from planned or unknown work.
See [Work Management and Request Queue](Work_Management.md).

---

## Knowledge Engine

### Supported Sources

- Files
- Folders
- Git repositories
- PDFs
- Office documents
- Markdown
- User notes
- MCP resources
- Work IQ

### Retrieval Strategy

Hybrid search:

- Full text search
- Embedding search
- Metadata filtering
- Security filtering

### Stored Metadata

- Source
- Author
- Ingestion date
- Permissions
- Tags
- Content hash

---

# Model Provider Abstraction

The following is a conceptual vision-level sketch, not the implementation contract.
Agent-oriented SDKs and inference APIs have different responsibilities.
Kora owns lifecycle, context, policy, approvals, and presentation; replaceable runtime adapters may own their model/tool loop only when those controls can be enforced.
See [Architecture and Contracts](Architecture.md) for streaming, capability negotiation, mediation, and the Copilot integration proof.

```csharp
public interface IModelProvider
{
    Task<ModelResponse> ExecuteAsync(
        AgentRequest request,
        CancellationToken cancellationToken);
}
```

Implementations:

- GitHubCopilotProvider
- OllamaProvider
- FoundryProvider
- OpenAiProvider

---

# MCP Platform

Kora should be an MCP host and MCP client.

### Supported Uses

- Connect to external MCP servers
- Host local MCP servers
- Expose Kora capabilities through MCP

### Example Integrations

- Work IQ
- GitHub
- Azure DevOps
- SharePoint
- Filesystem
- Local tools

### MCP Responsibilities

- Discovery
- Authentication
- Permission enforcement
- Health monitoring
- Tool invocation

---

# Skills System

Skills describe repeatable behavior.
Users can develop and refine skills through voice without changing the application.
See [Voice-Driven Skill Authoring](Skill_Authoring.md).
Kora also ships out-of-the-box skills, including "Kora, lock the machine", backed by a protected bundled script.
Graceful shutdown/restart controls use distinct action-specific voice or UI confirmation and retain any mandatory OS checks; application and queue controls have separate reserved intents.
See [OOTB Phrase Catalogue](OOTB_Phrases.md).
User-authored declarative skills and immutable first-party scripted skills have distinct installation/trust boundaries.
See [Out-of-the-Box Skills](Built_In_Skills.md).

Example:

```yaml
id: investigate-deployment
version: 1.0.0
name: Deployment Investigation
```

A skill may contain:

- Instructions
- Prompts
- Workflows
- MCP tool references
- Validation rules

### Skill Sources

- Protected out-of-the-box packages
- Existing skills in user-approved profile directories, referenced read-only
- Kora-specific skills and edited copies in `%APPDATA%\Kora\Skills`
- Local directories
- Git repositories
- Shared enterprise repositories

See [Skill Sources and Roaming Storage](Skill_Storage.md) for compatibility, partitioning, enablement, and roaming boundaries.

---

# Script Runtime

Scripts provide deterministic execution.

Supported types:

- PowerShell
- C# scripts
- Compiled plugins
- External processes

All scripts execute inside controlled boundaries.

Those boundaries must be stated accurately: process separation and timeouts are not a security sandbox.
Executable extensions require explicit code trust or proven OS-level containment.
See [Security and Data Flows](Security_Data_Flows.md#executable-extensions-and-containment).

Controls:

- Timeouts
- Permission checks
- Logging
- Output capture

---

# Builder Mode

Builder Mode allows Kora to create and modify:

- Skills
- MCP configurations
- Scripts
- Documentation
- External source repositories

Preferred workflow:

```text
Voice Request
      |
      v
Generate Proposal
      |
      v
Show Diff
      |
      v
User Approval
      |
      v
Build/Test
      |
      v
Commit Changes
```

Kora must not modify its own source, binaries, executable components, or security/updater implementation.
Skills are user-owned declarative data, not a mechanism to update application code.
This is an enforced boundary, not an action that an agent approval can override.
Application maintenance occurs outside the agent through a separate user-initiated channel.
See [Application Integrity](Security_Data_Flows.md#application-integrity-and-no-self-modification).

Proposal, apply, external build/test execution, commit, and push are separate actions.
The initial Builder workflow is narrower than this long-term diagram: it authors, validates, saves, and enables data-only skills, with no external builds or commits.
Any future external repository functionality must continue to exclude protected Kora resources.
See [Task Lifecycle and Recovery](Task_Lifecycle.md#builder-lifecycle).

---

# Voice-Only Interaction Model

Typing is optional and not required.

Primary input:

- Voice

Secondary context channels:

- Clipboard
- Files
- Screen context
- Selected content

Examples:

"Use the clipboard"

"Explain this"

"Compare this with the previous result"

---

# Clipboard Integration

Clipboard content becomes structured context.

Clipboard capture, snapshot consent, and preview are built-in app responsibilities.
Interpretation and downstream workflows can be supplied through extensibility.
The MVP supports explicitly requested plain-text snapshots only.
See [Built-In Features and Extensibility](Extensibility.md#clipboard-decision).

Supported content:

- Text
- Images
- File lists
- URLs
- HTML

Safety rules:

- Snapshot before processing
- Detect secrets
- Require approval before remote transmission

---

# Security Model

Every capability is permissioned.
Microphone use is prohibited while the Windows session is locked.
This is mandatory host policy regardless of whether Kora, the user, or Windows initiated the lock; it cannot be overridden by a skill.
See [Locked-Session Microphone Policy](Security_Data_Flows.md#locked-session-microphone-policy).

Examples:

```text
filesystem.read
filesystem.write
clipboard.read
workiq.read
git.write
process.execute
```

Execution categories:

1. Read-only
2. Local modification
3. External read
4. External write
5. High-risk operations

Approvals are required for risky actions.

---

# Enterprise Integration

## Work IQ

Work IQ acts as the primary enterprise intelligence layer.

Potential capabilities:

- Meetings
- Email
- People
- Documents
- Teams content
- Search

All requests execute with user identity and existing permissions.

---

# User Experience

## Typical Workflow

User:

"Kora, summarize my active pull requests"

Kora:

1. Retrieves repository data.
2. Summarizes findings.
3. Shows results visually.
4. Provides a short spoken summary.

## Response Modes

### Voice Only

Short answers.

### Visual Only

Large results.

### Hybrid

Recommended default.

---

# Technology Stack

Users can obtain Kora through an optional source clone/build bootstrap or precompiled binaries requiring only the supported runtime and native prerequisites.
Neither installation path requires the other.
Delivery provides Kora; the running app detects and configures its storage, databases, and selected capability dependencies.
Local runtimes such as Ollama are optional and configured when needed, not installed unconditionally.
See [Environment Setup](Environment_Setup.md).
See [Distribution and Updates](Distribution_And_Updates.md).

## Desktop

- .NET
- Avalonia

## AI

- GitHub Copilot SDK
- Ollama
- Azure AI Foundry

## Speech

- Whisper.cpp
- Piper

## Integration

- MCP SDK
- Work IQ

## Storage

- SQLite
- Local vector index

---

# Future Roadmap

These phases describe the long-term direction.
The initial delivery uses the smaller, gated slices in [MVP Scope and Non-Goals](MVP_Scope.md).

Phase 1

- Voice input
- Local "Kora" wake-word activation, with optional push-to-talk
- Clipboard support
- Copilot integration
- Local skills
- MCP support

Phase 2

- Advanced knowledge indexing
- Work IQ integration
- Skill marketplace

Phase 3

- Multi-agent workflows
- Shared skill repositories
- Advanced automation
- Federation across devices

---

# Product Definition

Kora is a local-first, voice-first agent platform that combines conversational AI, knowledge retrieval, MCP integrations, enterprise connectivity, skills, and automation into a single extensible assistant designed for developers and knowledge workers.
