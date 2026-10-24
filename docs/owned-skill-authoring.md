# Create an instruction-only skill

Kora-owned skill authoring creates a new immutable native package. It does not
enable, select, advertise or invoke the saved skill. Shared-profile discovery,
reference consent and reviewed shared-skill invocation are separate operations.

## Complete operation

Use typed commands, current-name activated voice (for example, **Kora, owned
skill create daily-review 1.0.0**), or the native editor controls:

1. `owned skill create daily-review 1.0.0` opens a volatile new draft.
2. `owned skill name: Daily review` supplies its readable name.
3. `owned skill description: Review the day` supplies its description.
4. `owned skill guidance: Summarize the completed work and list open questions.`
   supplies literal guidance. Native editing supports multiline guidance.
   Commands in guidance remain text, not additional authoring commands.
5. `owned skill validate` validates the complete native two-file package.
6. `owned skill save` opens a complete review of **both files**, the exact fixed
   destination, local-only/tool-free/reference-free declaration and inactive state.
7. Continue to the **separate native publication confirmation**. Only **Publish
   exact new version** authorizes the directory publication. Voice/typed Save
   does not confirm that final write.
8. `owned skill list`, then select a saved version and use **Reopen**, or use
   `owned skill reopen daily-review 1.0.0`. Reopen reads the actual saved package;
   it does not select it for model use.

`owned skill editor` opens the controls without generating content.
`owned skill cancel` retires authoring authority and cancels pending review/I/O.
The controller retains ownership until the actual operation finishes, even if
an I/O provider ignores cancellation.

Closing, exit, restart and handoff review a dirty draft with **Save**, **Discard**
and **Cancel**. Cancel keeps the draft open and blocks the requested departure.
Save still requires Validate and both native review stages. Privacy, call,
ownership or original-input retirement prevents saving; recovery does not renew
old voice or draft authority. A retired draft can only be explicitly discarded
or retained. After privacy recovers, `owned skill editor` can show that draft
under fresh private presentation eligibility for Discard/Cancel, never to renew
Save authority. Drafts are not autosaved.

## Native v1 format

The host resolves the owned roaming application root. The fixed layout is
`Skills\<canonical-id>\<canonical-version>\manifest.yaml` and
`Skills\<canonical-id>\<canonical-version>\instructions.md`.
There are exactly two files, with no executable entry point or examples folder.

The complete manifest for the example is:

```yaml
kora-skill-format: 1
id: daily-review
version: 1.0.0
name: Daily review
description: Review the day
runtime: local
tools: none
references: none
```

All eight fields are required exactly once. Unknown or duplicate fields,
executable/dependency/hook metadata, tools and references are rejected. This is
a closed plain-scalar format, not a general YAML object loader. Names and
descriptions use bounded one-line plain text, not quoting or YAML syntax.
The manifest has eight LF-terminated lines.

Both files require strict UTF-8 without BOMs or hidden format/control characters
(ordinary tabs/newlines in guidance are allowed). The aggregate exact file size
is at most 64 KiB, including metadata; the manifest is at most 4096 bytes.
The readable name is at most 128 characters and description at most 1024.
Guidance is nonempty literal text. It is not executed, imported, dereferenced or
sent to a provider by saving.

IDs are lowercase ASCII letters/digits/hyphens, start with a letter, have no
trailing or doubled hyphens and are at most 64 characters. Windows device names,
paths and private staging names are rejected. Versions have three canonical
decimal components, each at most four digits, with no leading zeros except `0`.
Every immutable version counts toward the 32-package catalogue limit.

## Publication and recovery

Only the exact validated generation can be reviewed and confirmed. Editing any
metadata or guidance invalidates Validate and pending review. The original
authoring authority is conjoined with current host admission through publication;
a later native click does not replace it.

Under a bounded private lease the Windows store stages and flushes the complete
two files in a uniquely owned private directory, validates their exact bytes,
and publishes the whole version directory with a no-replace native rename
relative to its pinned ID parent. Both files and their identities are read back
before a saved result is reported. Existing IDs/versions are never overwritten.
Choose a fresh version/ID or cancel on collision.

An interrupted stage is not a saved package. A failure after directory
publication is explicitly **unconfirmed**, not a rollback. Do not retry Save or
delete the destination. List/Reopen that exact ID/version to inspect the actual
saved state. If original authority also expired, explicitly close/discard the
volatile draft and open a fresh editor before List/Reopen; this does not delete
any published package. Saving never registers the root as a shared source and never grants
tools, model access, invocation, enablement or execution authority.

The owning design contracts are [Skill storage](../Design/Skill_Storage.md),
[Skill authoring](../Design/Skill_Authoring.md) and
[Definition management](../Design/Definition_Management.md).
