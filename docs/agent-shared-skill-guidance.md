# Use a shared skill as guidance for one local agent task

**Answer generation is unavailable in this build.** No local model is approved
for release use. The review controls below do not change that.

This advanced workflow uses a skill's written guidance to help an agent answer
one task. It does not run the skill as a program or start another agent.

An existing user-owned agent profile in format 3 can reference one exact skill
from an already registered shared source. Kora runs **one tool-free local agent
turn**, not the skill's executable artifacts or another agent. An unavailable
local model remains unavailable; there is no hosted fallback.

## Complete operation

1. Open **review agent** by typing it or speaking it with the current activation
   name. The native window uses the same original-input host admission.
2. Refresh existing Active conversations and explicitly select your destination.
   No conversation is created or resumed. Supply your existing agent file and
   original task. The task and genuine result or truthful terminal are retained
   only in that selected conversation.
3. **Refresh registered shared skill sources**, select a source, then **Read
   selected source catalogue** and select the declared skill by name and version.
   This window cannot register sources, write definitions, or resolve a name to a
   different source. An ambiguous or changed item is unavailable.
4. **Read source and review exact invocation**, then **Review selected shared
   skill declaration**. Review the complete profile, primary skill and declared
   file list. No referenced Markdown has been read at this stage.
5. **Consent to read declared skill Markdown**. This distinct consent permits
   only the finite, pinned local Markdown closure. Review the complete primary
   and every reference, exact original task, destination, locality and host-clamped
   budgets. Nothing is truncated or silently omitted.
6. **Enable this complete skill review once**. Registration, discovery and
   reference-read consent are not enablement. This independently owned review
   cannot adopt an earlier skill window's approval.
7. **Consent to this exact bounded invocation**. This is separate agent invocation
   confirmation, not reuse of skill enablement. Kora consumes the review once as
   inert local evidence and returns a genuine answer or truthful terminal in the
   selected conversation. It never invokes a nested skill executor.

Typed/current-name voice commands for the same controls are `agent targets`,
`agent skill sources`, `agent skill source <displayed source>`,
`agent skill catalogue`, `agent skill select <displayed name and version>`,
`agent conversation <unambiguous displayed conversation name>` (or `Active`
for one unnamed Active conversation), `agent review invocation`,
`agent skill review declaration`, `agent skill consent references`,
`agent skill enable`, `agent confirm invocation`, and `agent stop`.
Source registration and catalogue inspection retain their native local-input
requirement: use those native controls before continuing by voice. Native lists
support explicit selection when names are ambiguous. Kora tracks which source and version you reviewed; you do not need to speak
internal identifiers or checksums.

## Supported profile and skill

The existing profile fields and host budget clamps still apply. Format 3 requires
`kora-agent-format: 3`, `runtime: local`, `input: text`, `output: text`,
`tools: none`, `maximum-calls: 0`, and exactly one canonical
`shared-profile:<registered-source-id>:<skill-name>@<version>` in `references`.
This is a declaration in the existing file, not a command to register that source.
No mixed owned-instruction references, unknown metadata, arbitrary paths or URLs,
latest-version substitution, scripts, nested agents, MCP or tool execution are
supported. Format 1 and format 2 retain their existing semantics.

The skill must use the compatible `skill-md-instructions-v2` finite declared
Markdown format, with a pinned registered package identity. Existing
[skill storage](../Design/Skill_Storage.md) and
[definition review and approval](../Design/Definition_Management.md) contracts
remain authoritative. The complete profile plus skill closure must fit 64 KiB;
the stricter complete serialized model input and profile budgets also apply,
including metadata. All profile, primary and reference evidence is separate,
inert and LocalOnly. Retained text is not new authority.

## Retirement and unavailable states

Each review expires after at most two minutes. **Stop / revoke and join owned
execution**, source/profile/input/target changes, source withdrawal or replacement,
privacy/call/ownership loss, expiry or changed bytes retire the operation.
Freshness is checked again through model admission and continuation without
substituting a new source. Old approval cannot be revived by re-registering a root.
Cancellation closes authority immediately, but the host retains actual read and
provider ownership until completion joins. A late answer cannot publish to another
conversation. Inspect Sessions after an unconfirmed terminal; there is no automatic
replay or claimed rollback.

Hosts without the shared-skill resolver explicitly refuse format 3. In particular,
independent run-set factories remain format 1/2 only. This operation adds no hosted
or concurrent agent capability and does not qualify an unavailable runtime.
