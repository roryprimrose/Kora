# Device-profile memory

Device-profile memory is optional, explicit local application data for later
conversations on the same Windows profile. It is not model learning, automatic
recall, a provider memory feature or a settings file.

You can create, inspect, edit and forget memories without AI. **Using them for
an AI answer is unavailable in this build**. Saving a memory does not make
Kora recall it automatically or enable a model.

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

## Keep one reviewed Session memory for later sessions

The [native copy review](../src/Kora/MemoryProfile/SessionsViewModel.SessionMemoryCopy.cs) completes a separate scope-change operation, not a move or automatic promotion:

1. Select the original **Active** conversation. Leave **Device profile** unchecked, explicitly **List**, select ONE enabled admitted Session memory, and **Inspect exact memory**.
2. Select **Keep for later sessions in this device profile**. Read the complete current source value, original classification and lineage, and the independent scope/lifetime disclosure.
3. Choose a **fresh classification** for this copy. No classification is preselected or inherited. Treat the original as lower-trust candidate content; the same forbidden-content and size rules above apply.
4. Select **Propose NEW unadmitted profile copy**. Read the complete new candidate, classification and DeviceProfile scope. This creates a fresh identity and lineage, not a durable value or permission.
5. Select **Accept complete copy review**, then separately **Admit NEW profile memory**, or **Cancel scope copy**. Acceptance alone saves no value.
6. Check **Device profile**, explicitly **List**, select the new row and **Inspect**. The new record can be reopened after restart. Its later one-shot local use still requires the separate review and confirmation below.

Keyboard activation of **Keep**, **Propose** or **Accept** returns focus to the
beginning of the complete private review, never to an affirmative decision.
Admission, cancellation and held/error outcomes focus the adjacent read-only
scope-copy status. A stale source yields the held/error outcome, not its old
review. Changed selection, lost private control or a hidden or inactive original
surface prevents a late focus handoff. Returning after native deactivation or
ancestor visibility loss does not renew that handoff; the actual outcome remains
readable in the adjacent status without moving your current focus. Focus does not
accept, save, list or inspect a different memory; readback still requires step 6.

The [source-bound service](../src/Kora.Application/Memory/DeviceProfile/MemoryManagementService.SessionCopy.cs) re-reads the exact original before proposing, reviewing and admitting; the [canonical transaction](../src/Kora.Windows/Storage/WindowsSqliteHostInteractionStore.Memory.cs) compares it again at the actual destination operation.
Changed, edited, disabled, forgotten, deleted or replaced sources cannot authorize the pending copy. Selection/scope changes, privacy/control loss, cancellation, restart and the sixty-second review expiry retire pending work. Start a fresh review rather than retrying a held result.

The original Session memory is unchanged. No grant, receipt, review or retention deadline is copied. Once independently admitted, the new Device-profile memory follows its own lifetime: forgetting the original or deleting its session does **not** delete the profile copy. Disable or forget the new record separately.
Nothing automatically reads a model, recalls memory, sends content, starts speech or activates a tool. These copy controls require fresh native input; existing typed/activated Session commands and model Session proposals do not gain a copy/promotion command.

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
