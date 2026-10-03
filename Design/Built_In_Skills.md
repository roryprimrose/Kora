# Out-of-the-Box Skills and Session Policy

Status: proposed. Initial bundled computer-management skills are included in Slice A.

Related: [Extensibility](Extensibility.md), [Skill Authoring](Skill_Authoring.md), [Security and Data Flows](Security_Data_Flows.md), [Acceptance Criteria](Acceptance_Criteria.md).

## Package Classes and Sources

Kora ships useful skills without requiring users to author or download them.

| Package class | May contain | Installation and modification |
|---|---|---|
| Bundled first-party skill | Manifest, instructions, fixtures, and a registered script, all embedded resources in a protected Kora application binary | No user/agent editing or file replacement; updated only with the application through verified out-of-band maintenance |
| User-authored skill | Declarative manifest/instructions/workflows referencing admitted tools | Created/refined by voice in the dedicated user skill store; cannot create or modify executable scripts |
| Shared profile skill | Supported declarative/instruction content; executable requirements are not admitted in the MVP | Read-only source reference with explicit revision enablement; edited copies go to Kora's roaming store |

All sources obey capability checks, action policy, task identity, and auditing.
See [Skill Storage](Skill_Storage.md) for profile discovery and `%APPDATA%\Kora\Skills`.
A bundled package is not a blanket permission grant.
Its complete definition and script are embedded application resources, not separately deployed or writable skill files.
User skills may reference an admitted action but cannot change its executable implementation or acquire its special routing privileges.

## Embedded Resource Storage and Integrity

Build every built-in skill's manifest, instructions, fixtures, and script into the Kora application assembly as embedded resources.
Use stable host-owned resource IDs and a build-generated catalogue binding skill ID, resource IDs, version, digest, and permitted action.
Resolve resources from the explicitly identified application assembly, never by scanning arbitrary assemblies, folders, PATH, profile roots, or plugins.
The assembly is part of the protected application binary deployment; this does not require choosing single-file publishing or a self-contained runtime.

Read bounded resource bytes into an immutable invocation snapshot and verify the registered identity/digest before use.
User preferences can select an allowed skill or disable optional skills, but cannot supply replacement resource bytes, resource mappings, or script paths.
Inspect/help may display a read-only definition. Voice authoring cannot edit/delete a built-in skill.
A separately named declarative user adaptation never replaces the embedded original, acquires its executable trust, or shadows reserved controls.
Embedded skill changes require rebuilding and replacing the application through its verified maintenance channel; there is no independent built-in skill updater.

Do not deploy or extract editable manifest/script copies for discovery or execution.
Use an interpreter/worker that accepts the verified script snapshot directly through a controlled in-memory/input mechanism with fixed typed parameters.
If an engine requires a loose script file, choose a compatible execution implementation or mark the action unavailable; do not weaken this rule with a writable extraction cache.
Read-only display/exported text is never a source for subsequent execution.
Runtime engine binaries remain separately protected application prerequisites, not model-selectable executables.

Embedding prevents ordinary skill-file editing, not patching/replacing an entire binary.
Protected deployment permissions and release-origin/provenance checks are still required; an embedded checksum alone cannot authenticate an assembly whose code/catalogue was also changed.
Initial official artifacts are unsigned, so canonical release origin, final-byte hashes, build provenance, and protected installed-file permissions provide traceability and tamper detection rather than Authenticode publisher authentication.
Modified or mismatched application provenance must be rejected by the trusted deployment/launch mechanism where it can be established, not merely checked by code inside an already compromised binary.
Source builds identify their separately trusted local build provenance and never claim to be official release binaries.
A user deliberately changing source and building another application, or an administrator bypassing deployment trust, is outside the integrity guarantee.
Kora must not claim that a locally owned open-source application is impossible for its owner to modify.

## Initial Skill: Lock the Machine

User invocation: "Kora, lock the machine".

Conceptual manifest:

```yaml
id: kora.session.lock
version: 1.0.0
name: Lock the machine
tool: session.lock
execution: bundled-script
scriptId: kora.lock-session
```

This is a required schema concept, not a published manifest API.
The host resolves `scriptId` through its immutable registration to the embedded script resource and verified digest.
The manifest cannot provide an arbitrary path, executable, or command line.

Behaviour:

1. Recognise the direct request locally after wake activation/transcription; no cloud model or connector is required.
2. Resolve the original bundled skill and registered `session.lock` action.
3. Verify the interactive user/session, script identity, and permission for this request.
4. Invoke the verified embedded script snapshot with fixed validated parameters to lock the current Windows session.
5. Observe the Windows session-lock notification; an accepted API request alone does not prove the session locked.
6. Enforce microphone policy immediately on lock, regardless of how the lock occurred.

An unambiguous direct user request is consent to lock this session; no second confirmation is required.
Ambiguous or quoted/retrieved mentions of locking are not an invocation.
The skill cannot select another user/session, unlock Windows, obtain credentials, elevate, or lock repeatedly on a timer.

This narrow host-admitted session control can bypass the task queue, like cancellation, so it remains available during long-running work.
It is not a second general task executor and does not give the work-management model script/tool access.
The host owns the priority allowlist; neither a user skill nor a modified manifest can claim it.

A failure or missing confirmation produces an explicit failure/unknown result with an action receipt.
Do not announce success solely on script exit code and do not automatically retry an uncertain lock.
The script must not be able to clear policy, reopen the microphone, or modify Kora.

## Script Admission and Execution

- Embed and verify script ID, version, resource identity, digest, expected parameters, effect, and runtime.
- Load skill/script bytes only from the identified protected application assembly; never substitute a similarly named user skill, loose extracted script, or PATH-resolved script.
- Expose the registered action, not a generic shell or script runner, to agent workflows.
- Use a narrowly scoped worker/profile with tested denial of writes to Kora resources and no unnecessary filesystem/network/credential access.
- Verify Windows session-control API access under that actual profile before selecting the script engine.
- Containment and protected-resource tests are Slice A release gates for this script, not deferred general plugin work.
- Reject tampered scripts or unavailable containment explicitly; do not fall back to unrestricted PowerShell or other ambient-rights execution.

General script import, generation, installation, and execution remain outside the MVP.
Shipping fixed scripts does not enable arbitrary user scripts.

## Additional Bundled Computer Controls

Ship protected shutdown and restart skills alongside lock, mapped to fixed `computer.shutdown` and `computer.restart` actions.
The same script identity, containment, non-self-modification, and local routing requirements apply.
No generic shell, remote target, forced-close parameter, or user-defined privileged command is exposed.
Their named voice proposal/confirmation, required native secure confirmation, active-work handling, host countdown, and cancellation are defined in [OOTB Phrases](OOTB_Phrases.md#shutdown-and-restart-safety).
App exit/hide/settings and queue controls are built-in lifecycle intents, not scripts required to control the host.

## Mandatory Locked-Session Microphone Policy

Policy ID: `microphone.disabled-while-session-locked`.

This is host policy, not behaviour implemented by the lock skill.
It applies when Windows locks via Kora, Win+L, timeout, remote session changes, or any other mechanism.
Kora must release microphone capture, not merely stop forwarding recognised commands.

- Resolve the current interactive session at startup and subscribe to authoritative Windows session changes.
- Do not open the microphone while session state is Locked, Disconnected, or Unknown.
- On lock/disconnect, synchronously block new capture/activation and invalidate the audio generation before asynchronously disposing devices/workers.
- Stop wake detection, command capture, and microphone-consuming recognition; discard in-flight audio/transcripts and clear pre-roll.
- Stop speech playback and hide sensitive interactive content while locked.
- Reject attempts to enable listening from skills, runtime adapters, queued requests, shortcuts, or stale callbacks.
- Unlock does not re-enable listening automatically; require explicit user
  re-enabling for the current run. A later ordinary application restart uses the
  default automatic startup policy.

No model instruction, bundled script, user setting, or approval may override this policy.
It does not independently disable Microsoft integrations or cancel all background tasks.
Already admitted background work remains governed by its existing permissions; interactive approvals cannot be accepted while locked, and existing deadlines still apply.
The lock-skill receipt must not expose private results on the lock screen.
