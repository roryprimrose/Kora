# New standalone Autopilot session

The caller creates the genuinely new session in the retained control worktree
and selects Autopilot in the host UI. Do not substitute a nested chat, scheduler,
unrelated autonomous task, or prompt text for this host operation.

Apply [scope/source authority and quiescence](./SKILL.md#scopesource-authority-and-quiescence)
to both versions: human/coordinator posts and blocking `ask_user` answers never
invalidate quiescence or authorize source writes. Never request user silence.

## Opt-in version10 recovery

Read the [version10 contract](./README.md#opt-in-version10-lifecycle) before
choosing a handoff route. Version10 `pause` requires actual joined holder/source
fence/preservation readback, revokes current grants and retains all custody.
`recover` requires separately verified human/current coordinator authority and
stays paused; `resume` does not revive grants or establish missing source.
Refresh source and allocate a fresh bounded identity before any later dispatch.
Genuinely UNKNOWN authority counts remain UNKNOWN with established cooperative revocation,
never fabricated drain or OS enforcement. Preserve every old/new chat, ref,
worktree, publication, checkpoint and evidence record unless separately permitted.

Legacy loading/preview is not adoption. Explicit `adopt-lifecycle` preserves the
complete accepted versions1-9 state once and validates its old journal unchanged.
No live adoption is authorized by implementation/publication of these helpers.
The `handoff`/pending-zero sequence below applies to actual versions1-9 only.

## Old owner freeze

1. Finish the bounded checks and preserve publication/cleanup receipts. A blocked
   cleanup remains blocked, with its exact owner/path/ref; never discard it.
2. The old coordinator applies `pause` to its durable checkpoint before any
   handoff. Stop dispatch, notify the reusable planner and owned workers, and
   clear only this workflow's pending restarting instructions.
3. Verify actual stopped-writing/completed-turn evidence for each owner; message
   acceptance, idle alone, and retained content do not grant new authority.
4. Retain the control worktree/branch, checkpoint and external receipts. Do not
   publish or remove the live control branch as worker cleanup.
5. Give the caller the absolute control path and checkpoint path plus the exact
   old coordinator/owner addresses, publisher, authority, unresolved blockers,
   source freshness, and revision. New work must not start until these checks pass.

## New owner adoption

In the new session select **Kora Coordinator** and use this starter request:

```text
Adopt the paused Kora delivery workflow from its retained checkpoint.
Do not initialize another registry, create a replacement planner, or dispatch
until old-writer and pending-instruction proof has been verified.
Discover this exact new coordinator address and verify the control checkout.
Autopilot was selected manually in this new session; record user confirmation
separately from any effective settings the host exposes.
Apply the repository handoff contract, preserve the verified publisher and
existing publication restrictions, transfer ownership once, then resume finite
message-driven delivery in this exact standalone session.
```

Supply the real absolute control/checkpoint paths and retained receipt links.
The new owner verifies the paused checkpoint/revision and all old owners'
stopped-writing evidence. It applies one `handoff` event through the checkpoint
CLI with:

- `fromCoordinator`: exact old address from the checkpoint.
- `toCoordinator`: its host-discovered exact new address.
- `proof.stoppedOwners`: every old writer's exact address verified from host/
  terminal receipts, including old coordinator, planner, and retained workers.
- `proof.pendingInstructions=0`: zero unconsumed coordinator scope/source-authority
  instructions, verified by the old coordinator's own sent-instruction readback
  plus each owner's consumed-instruction record, with exact IDs/revisions.
  This is not total transport counts, sender enumeration or a user silence
  attestation. Ordinary posts cannot restart old writes.

The transition preserves keys, file scopes, publisher/authority receipts, blocked
work and publication evidence. It stays paused. Unattached reservations or
unproved stopped owners fail closed; do not edit the JSON manually to bypass them.
Then, with the caller's delivery authority confirmed, apply `resume` with the
exact new `owner`, `authorityConfirmed=true`, and explicit `discover=true|false`.
This does not change observe into delivery authority.

Notify the retained stopped planner/workers of the new exact return address
before any fresh assignment. Queue separate turns; do not steer new work into
old active turns. Reconcile stale/duplicate callbacks by original key/pass/event
ID. Do not relaunch an already-owned outcome or claim cleanup for retained
blocked work. Preserve the observer return address supplied by the caller.

Run the focused registry/checkpoint/worker-launch regressions before deployment;
their handoff fixtures establish transition rules, not actual host mode selection.
