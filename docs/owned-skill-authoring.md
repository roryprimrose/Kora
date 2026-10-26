# Create and use a declarative skill

Kora-owned skill authoring creates a new immutable native package. It does not
enable, select, advertise or invoke the saved skill. Shared-profile discovery,
reference consent and reviewed shared-skill invocation are separate operations ([authoring workflow](../src/Kora.Application/Skills/OwnedSkillSaveWorkflow.cs)).

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

## Review and invoke one saved revision

For an exact readable-name request spanning owned and registered shared instruction-only skills, use the [named invocation operation](commands.md#invoke-an-instruction-only-skill-by-readable-name).
It includes explicit local inspection, source disambiguation and complete canonical review/confirmation, not automatic selection or invocation. The direct ID/version route below remains available ([named host](../src/Kora/NamedSkillInvocationController.cs)).

Saving, listing and reopening never invoke a model. To use one saved package:

1. Select an exact saved ID/version and choose **Use**, or enter
   `owned skill use daily-review 1.0.0`. Review the complete manifest and
   guidance as inert, untrusted content, with **LocalOnly**, **tools none**
   and **references none** scope.
2. Separately choose **Enable exact reviewed revision once**, or
   `owned skill enable`. This creates only a volatile single use, expiring
   two minutes after review. The saved document remains inactive.
3. Choose **Refresh existing Active conversations** (`owned skill targets`).
   Select one displayed exact target, or use `owned skill target <session-id>`.
   `owned skill next-targets` pages the existing conversations; no session is
   created or resumed.
4. Enter the original request, or `owned skill request: <literal>`. Its exact
   whitespace and content are retained unchanged in that conversation.
5. Choose **Preview complete local invocation** (`owned skill review-use`).
   Review the target and complete serialized local context. Only host
   correlation identifiers are freshly stamped at admission; all policy,
   original request and both complete evidence files are shown.
6. Separately choose **Confirm exact invocation once** (`owned skill invoke`).
   The existing admitted local model route retains its answer, or a truthful
   unavailable/failed terminal, in that exact conversation. An unqualified
   provider stays unavailable; there is no hosted fallback or automatic retry.

The native v1 operation admits no tools, MCP, scripts, hooks, references or nested agents ([instruction-only consumer](../src/Kora.Application/Skills/OwnedSkillInvocationWorkflow.cs)).
A valid saved 64 KiB package may exceed the **32768-byte complete serialized
input** limit. Invocation then fails visibly before submission; no content is
truncated. The exact private non-reparse package directory, both file identities
and complete bytes are revalidated at review, admission, asynchronous model
boundaries and final disclosure.

`owned skill close-use` closes and retires use without changing the draft.
`owned skill cancel`, editor edits, closing, expiry, source changes, privacy,
ownership, channel or target loss retire authority and suppress late output.
Changing target/request after preparing an invocation requires a fresh source
review and enablement. Cancelling or closing retains ownership until actual
read/model work joins, even if cancellation is ignored. If retention is
unconfirmed, inspect that exact conversation before starting any fresh review;
an unconfirmed result is not a rollback.

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
executable/dependency/hook metadata, v1 tools and references are rejected. This is
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

## Author and use one host WebPageGet skill

Native v2 adds only a declaration of the existing host `network.get_web_page` capability. It does not add another web utility, tool implementation, permission parser, shared-source registration or network grant. Native v1 and its instruction-only use remain unchanged ([closed formats](../src/Kora.Core/Skills/OwnedDeclarativeSkillDocument.cs), [common reviewed consumer](../src/Kora.Application/Skills/ReviewedToolSkillInvocationWorkflow.cs)).

1. Create a new ID/version and supply name, description and literal guidance as above.
2. In **Host capability declaration**, select **Host WebPageGet only (native v2)**, or enter `owned skill tool web-page-get`. `owned skill tool none` selects v1. The selection is host-defined, not an arbitrary model-supplied tool ID.
3. Validate, Save, inspect both complete files and separately confirm publication. The immutable package is saved disabled; nothing runs. V2 has the same eight fields, with exactly `kora-skill-format: 2` and `tools: network.get_web_page`.
4. Use the exact saved ID/version. Inspect its complete manifest and instructions. Refresh/select one existing Active conversation; enter your original request and one explicit public HTTP/HTTPS URL in the native URL field or `owned skill url: https://example.com/`.
5. Choose **Review exact target and tool revision** (`owned skill review-tool`), then separately **Enable exact reviewed revision once**. Review/enablement is volatile, source-bound and at most two minutes; it does not grant web permission.
6. Preview with `owned skill review-use`. Review the Kora-owned source, target, original channel/request, exact URL, local-only evidence and bounds: at most one host WebPageGet action, two model rounds, 32768-byte complete input and the existing 15-second model deadline.
7. Separately confirm `owned skill invoke`. A tool proposal still opens the canonical native **Exact page approval**. Review the exact session/URL/effect and approve once, or deny. Neither saved content nor revision enablement can approve it.
8. After approval, the actual host tool returns bounded inert page evidence to the same local model turn. Only the model's genuine final answer (or truthful terminal) is retained with your original request in the selected conversation. There is no automatic retry, fallback, second destination or page-to-permission promotion.
9. Close/cancel to stop. Cancellation joins actual reads, tool and model work before release; late work cannot publish an answer after admission retires. Inspect an unconfirmed retained request before any fresh attempt.

These routes share the existing native and exact typed/current-name activated controls. Publication confirmation and per-action approval retain their native/authentication/channel exceptions. Changed target/request/URL after preview, draft/revision, ownership, privacy or call state retires pending authority; repeated confirmation cannot replay a use ([native controller](../src/Kora/OwnedSkillInvocationController.cs), [whole-operation regressions](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.OwnedToolSkills.cs)).

The owned reader checks only the selected package's complete members, exact identities and exact bytes, without opening another package or shared root. Source freshness is checked again across asynchronous approval, resolution/tool execution and final publication. Unknown required fields, extra/uninspected files, references, scripts, executable closure, arbitrary effects/tools, hooks, MCP, hosted transfer, model-selected destinations and general YAML remain excluded ([owned store](../src/Kora.Windows/Skills/WindowsOwnedDeclarativeSkillStore.cs), [action admission](../src/Kora.Application/Network/LocalWebPageInvocation.cs)).

Deterministic fake provider/transport tests prove implementation only. Native/provider/hardware/installed enablement and RC qualification remain unqualified; unavailable local composition fails visibly without network/provider fallback ([native regression](../tests/Kora.Windows.IntegrationTests/SessionsConversationTests.OwnedToolSkills.cs), [I/E/Q policy](../Design/Implementation_Roadmap.md#delivery-and-qualification-dependency-policy)).

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
