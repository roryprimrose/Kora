# Proactive Voice Interaction

Status: proposed core MVP capability.

Related: [Task Lifecycle](Task_Lifecycle.md), [Work Management](Work_Management.md), [Distribution and Updates](Distribution_And_Updates.md), [Security and Data Flows](Security_Data_Flows.md).

## Product Behaviour

Kora can start a conversation without a preceding user utterance.
Voice interaction is bidirectional, not solely request/response.

Examples:

- "A new Kora release is available. Would you like me to install it when your current task finishes?"
- "The explanation is ready. Would you like the short summary?"
- "I need you to choose an environment before I can continue."
- "The next request is blocked because its prerequisite failed."

Proactive speech is a suggestion or report, not permission to perform the proposed action.
It does not authorise background collection, new model calls, tools, or application maintenance.

## Host-Owned Interaction Broker

The broker accepts typed events from authorised components, such as a verified update checker or the current task ledger.
Each event has an ID, originating session/task where applicable, source, deduplication key, creation/expiry time, relevance, sensitivity, and permitted response actions.
Events derived from task content retain the task's provenance and restrictions.
Untrusted tool output cannot manufacture a trusted maintenance prompt or bypass an approval.

Kora renders a short grounded statement and, when needed, opens a prompt bound to that event.
A model may phrase eligible task content only under normal egress rules; it cannot invent system events, release availability, or approval destinations.
Update suggestions use host-verified release metadata and need no model.

## Delivery Policy

- Explain and obtain consent for proactive speech during setup, with mute, quiet hours, and visual-only preferences.
- Speak only while the session is unlocked, speech is permitted, and the user has explicitly enabled the voice interaction session.
- Apply [Call-Aware Speech](Call_Aware_Speech.md): the default suppresses proactive and requested speech during calls or Unknown enabled-detector state; configurable changes use the shared registry, but while protected voice-originated voice/in-call changes are rejected and require new UI initiation.
- Never speak over user capture or another spoken prompt.
- Routine suggestions wait for a conversational gap; ongoing task execution alone need not prevent a status/clarification message.
- Update suggestions wait until the user is not capturing/responding and do not displace an action approval.
- Coalesce duplicate events and avoid repeated reminders; an ignored routine suggestion does not repeat in the same voice session.
- "Not now" defers a suggestion, "don't remind me about this version" suppresses that release, and quiet mode keeps eligible notifications visual.
- Expired or invalid events are removed, not spoken later as though still current.
- Suppressed events remain eligible for visual presentation; do not replay a speech backlog when a call ends.
- Lock/disconnect stops speech and suppresses sensitive presentation; unlock does not silently enable listening or proactive speech.

Initial suggested reminder deferral is 24 hours, configurable.
Do not wake/activate the microphone to deliver a notification or force the user out of mute.
Local-only mode can deliver local task events; remote update checks require an explicitly permitted maintenance network operation.

## Reply Routing

Only one spoken foreground prompt is eligible for a response.
Several sessions can have explicitly addressed native question/approval cards; background events do not change session selection or that voice target.
The user responds with "Kora, ..." or optional push-to-talk; unsolicited speech does not leave command transcription continuously open.
Bind a reply to the exact current prompt ID, action, and expiry.
Ambiguous "yes" or a reply after another prompt took focus requires clarification, not inferred approval.
Wake detection is not authentication, and playback cannot approve its own prompt.

Management acknowledgements and task approvals use distinct scopes.
Unsigned-phase update interaction is notify-only and creates no maintenance approval.
Interrupting a prompt preserves truthful task state and follows existing prompt withdrawal/re-presentation rules.

## Update Example

1. The configured release checker discovers an eligible release from the canonical repository whose identity and digest match protected maintenance configuration, and emits a host event.
2. Kora says which version is available, that it is unsigned, and that replacement is manual.
3. "Kora, not now" defers; "Kora, show that release" opens an exact canonical release-page proposal.
4. Native browser navigation may open that page after normal URL review; it is not download, staging, execution, or installation approval.
5. Changed release identity/origin invalidates the proposal and requires a fresh request.

No release is downloaded, staged, executed, or installed by Kora during the unsigned phase.
The model and skill runtimes never receive an installer tool or authority to select executable payloads.
See [Distribution and Updates](Distribution_And_Updates.md) for the maintenance boundary.
