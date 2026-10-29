# Independent local agent runs

**Execution is unavailable in this build:** no local model is approved, and
overlapping local execution is also unavailable by default. Opening two run
panels does not start two agents.

Open **Independent agents** by typing `independent agents`, or say the same
command with the current assistant name. This operation uses at most two
independent runs in **different existing Active conversations**. It does not
create sessions, delegate work, chain agents, or run multiple tasks within one
conversation.

## Review and confirm each run separately

1. Select **Run 1** or **Run 2**.
2. Refresh Active conversations and explicitly select the intended conversation.
   Names are for selection; ambiguous names require native selection.
3. Enter the path to an existing user-owned local text-to-text `.agent.md`
   profile and the original task. Independent runs accept only tool-free
   profiles with `tools: none` and `maximum-calls: 0`.
4. Choose **Consent to read profile and review**. No model is invoked yet.
5. If the profile declares Kora-owned instructions, review the declared IDs and
   enabled candidates, then separately consent to reading those instructions.
6. Read the complete profile, resolved instructions and original task, then
   separately confirm using
   **Confirm this exact invocation** once for this run.

Repeat independently for the other run with a different Active conversation.
No review, source-read consent, invocation confirmation, task or answer is
shared between the runs. Profiles and instructions are inert guidance, never
permissions. See [tools and built-in skills](tools-and-built-in-skills.md).

## Capacity and truthful results

Production concurrent capacity is **unavailable by default**. Existing serial
provider qualification, per-turn transports and configuration do not qualify
concurrency. A second overlapping invocation remains resource-blocked unless
the host has an explicit current local capacity qualification for that exact
provider/profile. Hosted and ordinary requests remain serial. No provider
fallback or automatic retry occurs.

An allocated tab or an invocation request is not proof of provider execution.
Status reports the actual retained outcome or an unavailable, resource-blocked,
revoked, cancelled or unconfirmed operation. A genuine answer is retained only
in its selected conversation; inspect that conversation after an unconfirmed
operation before making any fresh request.

## Select, stop and close

Changing a run's source, task or target revokes only that review. **Stop** joins
that run's workflow and pinned source resources without stopping the other run.
The prior result and terminal status are retired from that run's panel, including
on repeated **Status** requests. Any retained answer stays in its original
conversation; open that conversation explicitly to inspect it. Review and
confirm the edited source, task and target afresh to obtain their own result.
A provider that ignores cancellation keeps its resource slot until it actually
finishes; its late answer cannot restore publication authority.

Closing the window, privacy/call/ownership closure and application exit revoke
both runs and join all actual resources. A cancellation request is not proof of
successful resource release. Closure failures are reported, not treated as
successful cancellation.

Run entries and unsent input are volatile. Closing clears their private
presentation buffers; it does not autosave tasks or create restart recovery.
Retained conversations follow the separate
[privacy and retention policy](privacy-safety-and-logs.md).

## Typed and activated-voice commands

Use the current assistant name for activated voice. Replace `1` with `2` for
the other run:

```text
independent agents
agent run 1 select
agent run 1 targets
agent run 1 next
agent run 1 target Current topic
agent run 1 source C:\Users\owner\existing.agent.md
agent run 1 request Explain this original task.
agent run 1 review
agent run 1 resolve
agent run 1 confirm
agent run 1 status
agent run 1 stop
independent agents close
```

`resolve` is used only for a reviewed profile declaring instructions.
These commands share the native controls' original-input, review and
confirmation guards. They do not grant model-generated commands, ambient
speech, stale queued input or pending approvals a route to execution.
