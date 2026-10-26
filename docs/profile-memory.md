# Device-profile memory

Device-profile memory is optional, explicit local application data for later
conversations on the same Windows profile. It is not model learning, automatic
recall, a provider memory feature or a settings file.

## Create, review and admit

1. Open **Sessions > Memories** and select an **Active** conversation.
2. Check **Device profile**. Unchecked means the existing Session scope.
   Changing scope clears selections, drafts and reviews; it does not move data.
3. Choose **ResponsePreference**, **WorkflowPreference**, **ExplicitFact** or
   **Decision**, and enter one small exact value. For example:
   **ResponsePreference**, `Prefer short explicit examples`.
4. Select **Propose new memory in chosen scope**. The candidate is volatile;
   proposing does not save it, accept it or use it.
5. Select the proposed row and **Inspect**. Read the entire value,
   classification and **DeviceProfile** scope. Accept its review only if all
   three are correct.
6. Separately select **Admit**. Only this step commits the reviewed value to
   Kora's authoritative private durable store with its original user lineage.

Values are bounded to 512 UTF-16 code units, 1,024 UTF-8 bytes and 2,048 bytes
for the complete serialized candidate. Oversize values are rejected, not
truncated. Never enter credentials, secrets, health information, inferred
traits, transient tasks or model claims. Those classifications are forbidden;
choosing a permitted label is not proof that a value contains no secrets.

## Explicitly use in a later conversation

1. Select another **Active** conversation on this same Windows profile.
2. In **Memories**, check **Device profile** and explicitly **List**.
   Listing returns metadata, not values.
3. Select one to sixteen enabled, admitted memories in the optional
   memory-assisted conversation list. Enter the exact question.
4. **Review** the complete selected values, classifications, scope and question.
   Review alone does not call a provider.
5. Separately **Confirm** one local-only, tool-free answer, or **Cancel**.
   Confirmation is single-use and the review expires after two minutes.

Ordinary conversation **Send** never reads these memories. A foreign profile,
stale source, changed memory, privacy/ownership loss, cancelled review or
unconfirmed audit closes use. A disabled or forgotten memory cannot be used.
Source changes invalidate reviews; source state is re-read before use.
Hosted disclosure, Project/Source memory scopes and model-origin profile
proposals remain unavailable.

A qualified local adapter may produce a genuine **ASSISTANT** answer. If no
qualified adapter is available, Kora retains a truthful **HOST Unavailable**
outcome, not a substitute answer. This implementation does not qualify or
enable a provider. The original question and actual answer or terminal outcome
follow conversation retention; selected memory bodies are not copied into
conversation history.

## Inspect, edit, disable and forget

All management works without inference. Explicitly list, select and inspect
the exact current revision before acting:

- **Edit** retains the memory identity and creation time, immediately redacts
  the old durable value and clears its review. The replacement remains volatile
  until new inspection, review and separate admission. After restart, an
  interrupted edit has no candidate or review authority; edit again.
- **Disable** retains the admitted value but closes use. Re-enabling requires
  edit, inspect, review and separate admission.
- **Forget** clears the body and receipt and retains a content-free,
  non-reusable identity tombstone. It is not a promise of forensic erasure.
- Exact individual actions are supported; existing session batch operations
  remain Session-only.

Done, deletion and retention of the originating session do not delete
independently admitted Device-profile memories or transfer any session
permission. A new Active conversation and fresh private control are still
required. Proposed/reviewed but unadmitted values do not survive restart.
Session memories and their deletion behavior are unchanged.

If storage, audit or cancellation reports an uncertain outcome, inspect the
authoritative state before any fresh action. Do not assume rollback, retry
automatically or interpret a missing answer as successful use.
