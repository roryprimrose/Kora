# Skill Sources, Profile Reuse, and Roaming Storage

Status: bounded bundled catalogue and explicit shared-profile native inspection delivered.
The separate [reviewed instruction-only workflow](../src/Kora.Application/Skills/SharedSkillInvocationWorkflow.cs) supports volatile exact-revision enablement and one local-only zero-tool invocation, including the explicitly declared, separately consented [v2 Markdown bundle](#explicit-local-markdown-references-v2).
The [reviewed one-tool workflow](../src/Kora.Application/Skills/ReviewedToolSkillInvocationWorkflow.cs) admits a declarative shared revision or a separately authored native v2 owned package declaring only the host's WebPageGet binding, with independent model-context consent and per-action authorization. Owned use has its own private store/control admission, not shared-root consent; the existing shared one-source API is unchanged. [Complete owned operation and exclusions](../docs/owned-skill-authoring.md#author-and-use-one-host-webpageget-skill).

The additive [native v4 owned observation workflow](../docs/owned-skill-authoring.md#author-and-run-an-ordered-host-observation-workflow) uses the same immutable two-file owned store, saves disabled and has its own closed review/execution owner.
It accepts neither shared originals nor copied executable content; native results remain volatile, not conversation history or renewed action authority ([declaration](../src/Kora.Core/Skills/OwnedDeclarativeSkillDocument.cs), [host consumer](../src/Kora.Application/Skills/ObservationSkillWorkflow.cs)).

Related: [Extensibility](Extensibility.md), [Skill Authoring](Skill_Authoring.md), [Bundled Skills](Built_In_Skills.md), [Security and Data Flows](Security_Data_Flows.md).

The [reviewed owned-source deletion](../docs/owned-skill-authoring.md#delete-one-saved-owned-source)
supports only native v1/v2/v3's exact manifest/guidance pair and empty version
directory. It pins native root/parent/file identities and exact bytes, checks
the current catalogue and permissions, and reads back absence. Shared originals,
bundled sources, reference packages and native v4 are not deletion targets;
independent grants/history/audit and immutable admitted tasks are unaffected
([store](../src/Kora.Windows/Skills/WindowsOwnedDeclarativeSkillStore.Deletion.cs)).

[Definition Management](Definition_Management.md) owns the common
source, UI/voice editing, activation, and external-reload contract for skills,
prompts, instructions, and agent profiles. Dedicated reviewed writes do not
expand source-read consent into generic profile editing or execution authority.

## Explicit local Markdown references v2

Native owned package ID rename is a separate [reviewed multi-step writer operation](Definition_Management.md#reviewed-owned-native-skill-package-rename). Its current two-file v1/v2/v3/v4 subset retains complete guidance/declarations, verifies new publication and old-name absence, and reopens disabled.
Private local intent holds affected identities unavailable until separately confirmed recovery; it does not extend the shared Markdown reference profile below or authorize primary-only `SKILL.md` rename ([native owner](../src/Kora.Windows/Skills/WindowsOwnedDeclarativeSkillStore.Rename.cs)).
Hidden affected identities still reserve one logical immutable version and the larger complete old/new byte size for publication/recovery quotas; hiding them is not freed storage capacity. Source retirement and destination readback retain exact non-movable file holds under the [rename contract](Definition_Management.md#reviewed-owned-native-skill-package-rename).

The v1 single-file profile remains unchanged. A primary `SKILL.md` may opt into
the finite local-only v2 profile with these exact additional flat fields:

```text
---
name: review-prose
version: 1.0.0
description: Review original prose using local notes
format: skill-md-instructions-v2
references: notes.md, references\guide.md
---
Use [notes](notes.md) and [guide](references\guide.md) as inert guidance.
```

`references` is an ordered comma-and-space-separated set of one to four distinct
package-relative paths. Each segment is lowercase ASCII letters, digits, `-`
or `_`; the leaf ends in exact `.md`. Maximum path length is 240 characters,
segment length 64, and depth four. Device names, `skill.md`, case/normalization
aliases, traversal, ADS, absolute/UNC paths and slash aliases are refused.
Only declared primary links of the shown inline form are supported. References
cannot reference other files or contain active markup, URLs, executable
references or executable/unknown code fences. No scripts or recursive loading.
Missing or undeclared package entries make the whole operation unavailable.

Discovery still reads only primary files and inventories other entry names.
It pins the source and selected package identities, but does **not** open
reference contents. The user first reviews the exact declaration and separately
authorizes those local reads. Registration/discovery consent is not reference
consent. The native **Authorize only the exact declared local Markdown reads**
button and exact original typed/current-name activated
`shared skill review invocation` command confirm only the displayed pending
declaration. Other invocation actions cannot skip this step.

**Stop and close reviewed use** remains available before read consent and
during dependency capture. Stopping before consent reads no references.
Stopping an admitted read immediately clears the private review and requests
cancellation; the host retains the read until it actually joins, suppresses
late content and requires a fresh exact review before another attempt.

The Windows reader revalidates the registered source, selected package,
canonical ancestors, inventory and exact primary bytes while retaining
non-writable, non-deletable handles. Reparse points, hardlinks, aliases,
replacement, unreadable/busy files and changed bytes fail closed. Each reference
is nonempty strict UTF-8, at most 16 KiB; primary plus references are at most
64 KiB inclusive. The aggregate revision uses domain-separated, big-endian
length-framed exact primary path/bytes followed by declaration-order reference
paths/bytes. BOMs and line endings are not normalized.

Only after capture does the host show the **complete primary and all reference
contents**. The user separately enables that immutable revision in memory,
selects one existing Active conversation, supplies the unchanged original
request, reviews the exact invocation, then confirms it. Declaration/review
authority expires after two minutes, is single-use, and is retired by closure,
withdrawal, source/input/admission change or cancellation. There is no saved
enablement or automatic retry. Revalidation rereads the entire bundle before
context admission and publication; late work is joined, not abandoned.

Primary and separately attributed references enter only the existing LocalOnly,
zero-tool model route. The complete serialized model envelope must still fit
32,768 UTF-8 bytes: the stricter limit wins and nothing is dropped or truncated.
The original request and genuine answer, or truthful failure/cancellation
terminal, are retained in the selected conversation. No hosted transfer,
generic executor, nested authority, tools, installation or provider qualification
is added. The separate one-tool workflow still accepts only its existing v1
single-file contract.

## Source Partitions

| Partition | Location | Kora access |
|---|---|---|
| Bundled | Embedded resources in the first-party `Kora.Definitions` binary | Current: immutable catalogue and read-only inspection only. Invocation unavailable; no user/agent editing, loose-file overrides, or writable extraction. Installed protection remains a separate admission gate. |
| Shared profile | User-selected registered roots below the Windows profile known folder | Bounded native discovery/inspection; separate [zero-tool](../src/Kora.Application/Skills/SharedSkillInvocationWorkflow.cs) and [reviewed one-tool](../src/Kora.Application/Skills/ReviewedToolSkillInvocationWorkflow.cs) paths. Neither admits scripts or hosted disclosure. |
| Kora-specific | `%APPDATA%\Kora\Skills` | Create/update validated declarative skills through the authoring writer |

Resolve Roaming AppData and the user profile through Windows Known Folder APIs, not model-provided paths or hard-coded user names.
The path notation above describes the intended folder, not an instruction to trust arbitrary environment-variable overrides.
The skill store must remain outside protected Kora code/loading roots.
Built-in manifests/instructions/scripts never occupy the roaming store or shared roots; their catalogue and complete definitions follow [Embedded Resource Storage](Built_In_Skills.md#embedded-resource-storage-and-integrity).

Proposed Kora-owned data layout:

```text
%APPDATA%\Kora\Skills\<skill-id>\<version>\manifest.yaml
%APPDATA%\Kora\Skills\<skill-id>\<version>\instructions.md
%APPDATA%\Kora\Skills\<skill-id>\<version>\examples\...
```

The layout and package schema are versioned; they are not executable search paths.
This describes the proposed declarative Slice C store, not a shipped
script runner. Its explicit declarative manifest is distinct from the
implicit folder descriptor for future user-provided executable skills below.
Future stored-script execution requires explicit review and
hash-bound grants under
[skill and task execution design](../docs/skill-and-task-execution-design.md);
merely finding or enabling a skill never grants execution.
Revision metadata can describe source attribution, but cannot carry authoritative grants or choose arbitrary executable implementations.
Permission/enablement records, source registrations, machine-specific tool configuration, and audit SQLite remain local under `%LOCALAPPDATA%\Kora`.
Credentials remain in the protected credential facility, never either skill partition.

## Shared Profile Discovery

### Delivered bounded R21 native inspection

#### Selected displayed identity metadata literal Find - 2026-10-18

The [bundled inspector](../src/Kora/SkillPackagesWindow.axaml.cs) and [shared inspector](../src/Kora/SharedSkillSourcesWindow.axaml.cs) freeze only their already displayed `PackageIdentity` / `SharedIdentity` text after selection. Shared identity includes only the path already explicitly displayed; no additional filename, path or content is disclosed by Find.

The [private controls](../src/Kora/Controls/IdentityMetadataFindPane.axaml) expose query, First/Next/Previous/Clear, outcome with exact UTF-16 half-open location and one inert exact match. Enter finds First; F3/Shift+F3 navigate; Escape clears within Find without closing. Transfer/context gestures are blocked. Source tabs/bodies, uninspected entries, hidden fields, other packages and live files are not searched.

The [authoritative state](../src/Kora.Application/Presentation/AdmittedMetadataFindState.cs) is the attachment state renamed without behavior changes. It consumes unchanged [Core matching](../src/Kora.Core/Context/SnapshotLiteralFind.cs): strict bounded well-formed one-line literals, case/space-sensitive ordinal comparison, no normalization/count/wrap. Invalid input is rejected explicitly; zero match claims only absence within that frozen identity text.

Selection, filter/search/order/catalogue replacement, source changes, operation/busy start, cancellation/failure, admission/privacy/control revision change and close retire private Find state ([native tests](../tests/Kora.Windows.IntegrationTests/SkillIdentityMetadataFindTests.cs)).

Busy completion never readmits an old Find snapshot. A catalogue delivered while busy leaves selection empty for explicit inspection. After rechecking an existing selection, use catalogue **Clear**, then select again to admit Find; retained stale controls alone cannot recover text ([window](../src/Kora/SharedSkillSourcesWindow.axaml.cs), [control](../src/Kora/Controls/IdentityMetadataFindPane.axaml.cs)).

This bounded I increment adds no read/recheck/write, invocation, activation, model/tool/egress/session authority, activity renewal or catalogue filter change. Matches do not establish live validity, integrity, approval or execution readiness. Existing invocation remains unavailable; E/Q and unqualified runtime/provider/wake/profile writers remain unchanged ([inspector controllers](../src/Kora/SharedSkillSourcesWindowController.cs)).

The tray **Skill packages > Shared profile sources (read only)** is a separate
host-only route, not a model tool, slash artifact, import or executable loader.
Opening it loads only registrations. **Choose and register profile root**
explicitly selects one exact local folder through the native folder picker;
**List selected source** performs the bounded read. There is no automatic
profile skill-root scan or candidate probing. The prior ambient
`.copilot/.agents/.claude` skill-to-model startup discovery is removed.
Existing Kora-owned artifact and VS Code prompt/instruction routes are not
expanded or authorized by a shared source registration.

**Withdraw local read consent** unregisters only the explicitly selected saved
source. Native confirmation shows its exact host-issued source ID,
profile-relative root and pinned directory identity and binds the write to the
complete observed registration revision. It removes only Kora's registration
and owned catalogue/text/hex presentation snapshots; user-owned shared files are
never written, deleted or repaired. Unrelated registrations retain their exact
metadata and order. No enablement, execution, grant or model exposure changes.
A missing original directory remains withdrawable using verified saved metadata
without selecting or reading that directory again. Re-registering its path
requires a fresh native folder selection and a **new host-issued source ID**;
old sources and snapshots never inherit the new consent.

`ISharedSkillSourceReader` is the Core platform seam.
`WindowsProfileSkillReader` resolves the Windows profile with
`SHGetKnownFolderPath(FOLDERID_Profile)`; no user name, environment override,
model path or portable-layer Windows API selects it. A root must be a local
drive directory at least two and at most eight unaliased segments below that
profile. UNC/device/traversal/ADS paths and non-fixed/remote volumes are rejected
before opening source handles. All ancestors and discovered
entries are pinned by read handles without write/delete sharing; reparse points,
hard links, changed entry types and final-path aliases fail closed.
The registration pins a volume/file directory identity: removal, replacement or
lost access is unavailable, never silently rebound.

Limits per source: **32 SKILL.md packages, 256 total filesystem entries,
four descendant directory levels, 64 KiB per instruction file and 1 MiB total
instruction bytes**. At most four roots are registered. Exceeding a limit or
losing an inventory entry rejects that discovery, not a partially successful
catalogue. Only exact instruction-file contents are read; additional entries
are named but their contents are not read. Scripts, references, dependencies,
package installers and external files are never executed, fetched or loaded.

The versioned `skill-md-instructions-v1` reader accepts a deliberately narrow
flat YAML subset between exact `---` lines, at most 24 header lines and 4 KiB
UTF-8 front matter. Required `name` is canonical lowercase ASCII kebab case
(64 characters), `version` is three canonical numeric components, and
`description` is nonempty bounded text (1,024 characters).
Optional `allowed-tools` preserves up to 16 canonical tool IDs; this reader
admits no runtime dependencies, so any declared tool remains unavailable.
Plain and simple single/double-quoted scalar text is supported; no escapes,
implicit object types, tags, anchors, aliases, nesting, block scalars, unknown
metadata or duplicate keys are accepted as compatible. Missing or malformed
required metadata remains a visible incompatible row, not a hidden package.

Markdown is bounded untrusted inert source, not rich HTML or interpreted
instructions. Script/executable file references, non-text/unknown fenced code,
links/reference syntax and active markup conservatively disclose incompatibility
or unresolved references. Additional package entries are explicitly uninspected
and incompatible. This is **structural instruction-only compatibility**, not a
claim of semantic harmlessness or complete dependency discovery.
No instruction, metadata field or successful review acquires host authority.

Native review retains exact immutable original bytes, strict UTF-8 text
(including BOM/line endings), source ID/relative file, declared name/version,
reader version and SHA-256 of the exact SKILL.md bytes.
The digest does not claim to hash uninspected scripts or a dependency closure.
Invalid UTF-8 or unsafe control/directional-format text has no lossy/native
text projection; the unavailable row retains the byte digest and displays
the exact original bytes as bounded hexadecimal. Invalid required metadata
never becomes a normalized identity. Duplicate/case-aliased name/version identities are visible but
unavailable within each source; distinct sources remain qualified choices.
**Recheck selected revision** compares live bounded identity/content,
additional-entry disclosure and compatibility without replacing the old
snapshot. A stale snapshot is labelled stale; failures clear the view rather
than silently showing it as current. No content-bearing snapshot is persisted.

**Search/Clear** and the instruction-only compatibility filter navigate only the already admitted catalogue for the exact selected registered source (at most 32 snapshots); they do not read the filesystem, reparse headers, scan on typing or call Recheck/Discover. **All** includes incompatible/unavailable entries, and compatible does not mean invocable ([snapshot query](../src/Kora.Application/Skills/SkillCatalogueSearch.cs)).

Search is a literal ordinal-ignore-case substring over admitted declared name, metadata-qualified identity when name exists, and valid declared description, not bodies, path-fallback identities, files or roots. Queries share strict bounded validation; search exclusion clears private panes and requires explicit reselection, never opening the next row. Refresh/failure/source replacement/withdrawal/private close reset volatile navigation and fence late reads ([native controls](../src/Kora/SharedSkillSourcesWindow.axaml.cs), [controller](../src/Kora/SharedSkillSourcesWindowController.cs)); see [management search semantics](Definition_Management.md#delivered-native-skill-metadata-navigation).

Ordinary bundled/shared digest-filter widgets are removed in [#422](https://github.com/roryprimrose/Kora/pull/422). Internal canonical digests, complete catalogue admission and exact source validation remain; literal name/metadata and meaningful compatibility/availability navigation remain the [ordinary UI](Definition_Management.md#delivered-native-skill-metadata-navigation).

[Labelled native fields](../src/Kora/SharedSkillSourcesWindow.axaml) are volatile. Edits retire selected identity/text/hex and result mappings immediately; Clear restores empty/All without selection, and Escape closes privately. Generation fences reject stale discovery/recheck callbacks, including same-snapshot reselection ([window/controller](../src/Kora/SharedSkillSourcesWindowController.cs), [headless tests](../tests/Kora.Windows.IntegrationTests/SkillMetadataSearchWindowTests.cs)).

Matching observes only a fully admitted immutable catalogue, without new reads, hidden-name inspection, hashing, discovery, recheck, reload, activation, invocation or grants. Digest equality does not establish authority, current filesystem integrity/freshness or a dependency closure. Complete candidate admission and source bounds remain unchanged ([bundled catalogue](../src/Kora.Core/Skills/SkillPackageCatalogue.cs), [shared catalogue](../src/Kora.Core/Skills/SharedSkillCatalogue.cs)).

The volatile **Order** selector defaults to **Source order**, preserving admitted catalogue order. **Name ascending (ordinal)** and **Name descending (ordinal)** sort known names using ordinal comparison; unknown names remain last in both directions, never inferred from paths or empty strings ([ordering policy](../src/Kora.Application/Skills/SkillCatalogueSearch.cs)).

Equal names, including unknown names, use ascending ordinal exact manifest ID (bundled) or `SourceQualifiedIdentity` (shared), even in descending name order. Equal exact keys retain source order. Complete immutable catalogue admission precedes filtering/ordering; incompatible entries remain visible under All ([catalogue query tests](../tests/Kora.Application.UnitTests/Skills/SkillCatalogueSearchTests.cs)).

Ordering AND-composes with literal, availability/compatibility and exact digest predicates. Order edits immediately retire results, selection, private source panes and shared generation callbacks without I/O; Search reapplies the view without selecting. Clear restores Source order/empty/All; Escape/private closure clear volatile state ([native lifecycle tests](../tests/Kora.Windows.IntegrationTests/SkillMetadataSearchWindowTests.cs)).

Order is presentation only, not authority, completeness, live freshness or E/Q runtime qualification. It adds no persistence or resource format and changes no resource/digest bytes; runtime invocation remains unavailable ([snapshot policy](../src/Kora.Core/Skills/SkillPackageSnapshot.cs), [shared policy](../src/Kora.Core/Skills/SharedSkillSnapshot.cs)).

Registration is a validated versioned JSON preference under device-local
`IApplicationDataPaths`, published only through the shared atomic preference
store with bounded strict-UTF-8 read, serialized-byte validation before
publication and exact read-back. Oversized escaped Unicode registrations never
replace existing consent. It stores only source
ID, profile-relative root and directory identity, not content or grants.
Missing preferences mean no consent; malformed/unknown/duplicate/corrupt state
blocks all shared reads and registration, without a default/reset fallback.
Recovery is explicit restoration of a verified registration file or the
original registered source; automatic deletion, rebinding and preference repair
are unavailable. Registration is original-native-input, current-host/privacy
and durable shared-control session/generation admitted, with requested/terminal
typed audit outcomes. Each local operation records its durable intent/outcome
and uses the existing host activity parentage. Logs contain bounded counts or
exception types, never paths, package text, metadata, digest or rendered errors.
Withdrawal uses the same original-native-input, durable session/generation
lease, requested/terminal typed audit, atomic preference publication and exact
read-back. Registration and withdrawal share the preference mutation fence;
changed or invalid saved registrations reject a stale confirmation rather than
dropping unknown entries. Withdrawal clears owned snapshots immediately and
cancels an in-flight discovery/recheck. Before/after registration and window/
selection/lifetime fences prevent late display. Once removed, discovery and
recheck of the old source are unavailable.

Audit, write, read-back, cancellation or admission failure is not a successful
receipt or proof of rollback: publication may already be durable. Unconfirmed
mutations close further reads until a fresh admitted **Refresh registrations**
observes valid preferences; audit/session evidence failures can require host
recovery. The attempted source identity remains read-closed for the owning
host lifetime even if an old registration is restored; refresh permits retrying
withdrawal of saved metadata, not silently regranting that identity. A fresh
native selection with a new source ID is required to read it again.
Corrupt preferences cannot be reset, repaired or unregistered through
this workflow. Privacy closure and window disposal cancel pending work and
destroy native content views, including confirmation metadata.

Registration alone grants no enablement, invocation or model-context exposure. Editing,
Kora-specific authoring, executable imports and general runtime dependency admission
remain unavailable. The separate reviewed consumers below do not derive authority
from registration or Markdown ([source admission](../src/Kora.Application/Skills/SharedSkillDiscoveryService.cs)).

### Reviewed declarative one-tool invocation

The [separately typed review and invocation](../src/Kora.Application/Skills/ReviewedToolSkillReview.cs) bind one registered source identity, exact revision bytes/digest, existing Active target/generation/metadata, original UI or activated-voice channel, two-minute expiry and single use. They cannot enter or widen the existing zero-tool workflow.

The [page consumer](../src/Kora.Application/Skills/ReviewedToolSkillInvocationWorkflow.cs) accepts one single-file declarative `SKILL.md` with only `allowed-tools: network.get_web_page`. Additional declarations, uninspected files, scripts, executable fences, unresolved resources and ambiguous source identities remain refused. The separate exact MCP consumer below does not widen this page operation, bundled definitions, script runners or agents.

Source-read consent, explicit enablement of the exact revision's local model context, confirmation of the original request/target, and the WebPageGet action's own permission are separate boundaries. Guidance is inert untrusted evidence, never system instructions or permission. A model cannot expand the host-owned tool subset or budgets ([workflow](../src/Kora.Application/Skills/ReviewedToolSkillInvocationWorkflow.cs)).

Native approval freshness follows the still-running owning tool operation, source admission, cancellation and expiry, not the incidental activity of a UI review or refresh. Execution still requires exact live host context and the separately committed once-only grant. Finishing the tool retires its presentation callback ([shared page invocation](../src/Kora.Application/Network/LocalWebPageInvocation.cs)).

The native source inspector offers **Review selected one-tool use**. Select an existing Active target, enter the unchanged original request and one or two distinct explicit source URLs, then **Review exact target and tool revision**. Review the finite destinations, exact WebPageGet subset and cumulative budgets, separately enable the revision, review the invocation and confirm ([controller](../src/Kora/SharedSkillInvocationController.ToolSkills.cs)).

Typed commands use `review tool skill <exact name>` and the distinct `tool skill targets`, `target <ID>`, `address <absolute HTTP or HTTPS URL>`, optional `second address <absolute HTTP or HTTPS URL>` / `clear second address`, `request <unchanged prose>`, `review tool revision`, `enable`, `review invocation`, `confirm invocation` and `close` forms. Destinations are explicit original input, never inferred from instructions or model output ([routing](../src/Kora.Application/Skills/SharedSkillInvocationCommand.cs)).

The immutable [finite page scope](../src/Kora.Core/Network/LocalWebPageScope.cs) admits at most two distinct destinations and one call per destination, with at most three serial model rounds, inclusive cumulative 32,768 input / 4,096 output UTF-8 bytes and the existing 15-second deadline. Each inert page reply retains its existing 2,048-byte limit; source text is not persisted beyond existing request/answer retention ([continuation](../src/Kora.Application/Dependencies/BoundedModelTurnAdapter.cs), [page sanitization](../src/Kora.Application/Network/LocalWebPageInvocation.cs)).

Each physical call reuses its own exact canonical once-only page approval and host egress/privacy/session checks. All used approvals remain current through continuation and successful terminal retention. A missing/failed second source cannot produce a successful comparison; an expired/revoked terminal fence remains explicitly unconfirmed, without replay. The original one-source operation and tool-free route remain separate ([finite workflow](../src/Kora.Application/Network/LocalWebPageWorkflow.cs), [retained consumer](../src/Kora.Application/Hosting/SessionConversationService.ToolSkills.cs)).

Input/address/target edits revoke both the invocation and tool revision review; source/privacy/ownership changes fence late results. **Stop and close reviewed use** cancels and joins the owned operation. Comparison methodology stays in the [declarative skill example](../docs/tools-and-built-in-skills.md#two-source-comparison-skill-example), not a native comparison action ([controller lifecycle](../src/Kora/SharedSkillInvocationController.cs)).

Activated voice uses the same commands with the current assistant-name prefix and existing call/privacy admission. Changing the reviewed channel or target requires fresh review. The original instruction-only `shared skill` commands remain distinct ([desktop consumer](../src/Kora/SharedSkillInvocationController.ToolSkills.cs)).

The [canonical retained conversation consumer](../src/Kora.Application/Hosting/SessionConversationService.ToolSkills.cs) selects local-only reasoning and retains its genuine terminal result in that exact session. Source/session/privacy validation remains active before dispatch, continuation and terminal readback. Cancellation, denial or missing runtime readiness must not become a successful answer or automatic retry.

This is an implementation path, not qualification of an installed model or approval of any existing external definition. An unqualified local profile remains explicitly unavailable; no live provider/network/hardware evidence is claimed, and lock/shutdown/restart resources are unchanged ([host admission](../src/Kora.Application/Dependencies/ModelTurnHost.cs), [bundled catalogue](../src/Kora.Definitions/Skills/EmbeddedSkillCatalogue.cs)).

### Reviewed exact read-only MCP skill

The separately admitted [MCP skill consumer](../src/Kora.Application/Skills/ReviewedToolSkillInvocationWorkflow.Mcp.cs) accepts one registered single-file declarative skill using the closed `kora-mcp-read-only-v1` format. Its [reference](../src/Kora.Core/Skills/DeclarativeMcpToolReference.cs) pins one exact source-qualified server ID, full configuration revision, case-sensitive tool name and schema digest under MCP `2025-06-18`. These fields are dependency data, not inclusion, endpoint, account or execution authority.

The original user independently selects an existing Active target, an app-included server and a current delegated account, enters the unchanged request and exact bounded JSON arguments, reviews the complete skill revision and separately enables its local context. Confirmation preserves those exact choices; Save, revision review, enablement and preparation do not connect or approve an effect ([review](../src/Kora.Application/Skills/ReviewedToolSkillReview.cs), [invocation](../src/Kora.Application/Skills/ReviewedMcpSkillInvocation.cs)).

Before enabling invocation confirmation, preparation rejects outer JSON
whitespace that the model protocol cannot represent, preserving all admitted
argument bytes without normalization. It checks complete known local context
using the shared context builder and serialized-input codec, reserving worst-case
JSON-string space for the bounded undiscovered schema. The inclusive 32768-byte
budget includes all guidance, original request, argument/revision evidence,
policy, context and initial model-request framing; the 64 KiB inspection bound is not an invocation budget.
Preflight repeats at the conversation entry before retention or remote effects.
Final context admission still checks the exact discovered schema; scaffolding
and conservative reserved bytes confer no approval and are never sent.

Invocation reuses the [canonical MCP route](../src/Kora.Application/Hosting/SessionConversationService.Mcp.cs): separate native connection/source/account review, independent selection from freshly discovered host-mapped read-only tools, exact schema review, then separate permission/egress approval of the outgoing arguments. The selected tool/schema must equal the declaration, and model-proposed arguments must equal the original reviewed JSON bytes. Skill text and server annotations cannot select substitutes or establish effect mappings ([exchange owner](../src/Kora.Application/Mcp/McpInvocation.cs)).

The unchanged request and skill guidance enter structurally separate local-only evidence fields in the existing `McpReadOnly` turn. One bound tool exchange returns its actual bounded inert result to that original local continuation; the native review also binds exact skill lineage and the original argument digest. Source/privacy/channel/session/task/account/grant currentness remains mandatory across awaited work, dispatch, continuation and terminal readback. The six closed host observation tools and other MCP/skill consumers are unchanged ([context](../src/Kora.Core/Dependencies/ModelContextEnvelope.cs), [native binding](../src/Kora.Application/Mcp/McpReview.cs)).

The single-use [exchange owner](../src/Kora.Application/Mcp/McpInvocation.cs) joins its authenticated connection before releasing a successful result to any consumer, then revalidates the original source/session/grants. A pending or failed disposal cannot enter local continuation, publish the result or retain a successful answer; revocation and cancellation during that join remain fail-closed without another call.

The [native and typed/activated-voice interaction](../docs/mcp-transport.md#reviewed-declarative-mcp-skill) displays the actual result separately from the retained answer. Input/selection changes retire confirmation; Stop closes publication, cancels and joins the same provider/transport owner. No retry, rollback claim, new credential, source authority, hosted disclosure or follow-up-send consent comes from the result. Unqualified production accounts, connectors, effect mappings and local providers remain unavailable; deterministic fixtures do not enable them.

### Future discovery and enablement

The [bounded delivered consumer](../src/Kora.Application/Skills/SharedSkillInvocationWorkflow.cs) covers explicit instruction-only single-file use and separately consented [v2 Markdown bundles](#explicit-local-markdown-references-v2) with empty capabilities, original UI/activated-voice provenance, exact unchanged request text and one existing Active target.
Its [review](../src/Kora.Application/Skills/SharedSkillUseReview.cs) expires after two minutes, is single-use and never persists enablement.
The [canonical conversation consumer](../src/Kora.Application/Hosting/SessionConversationService.SharedSkills.cs) retains actual host results under the unchanged source, cancellation and terminal-readback fences.
Broader catalogue/model selection, tools outside the separately reviewed one-tool path, files outside the explicit v2 profile, executable requirements and hosted transmission below remain future work.

Offer user-approved source registrations, not a recursive scan of the entire profile.
Recognisable candidate locations can be suggested if present, for example `%USERPROFILE%\.agents\skills` or `%USERPROFILE%\.copilot\skills`.
These are candidates only; Kora does not assume every tool uses them or every skill there is compatible.
Users can select another bounded profile skill root.

Registration authorises bounded local discovery/read, not execution, editing, or remote transmission.
Show origin, format, declared tools/capabilities, compatibility, and revision digest.
Do not automatically enable everything discovered.
The runtime discovery catalogue contains source-qualified summaries of enabled
compatible revisions, with purpose/selection guidance, inputs, tool/task
references, and availability. A model selection resolves the host-held pinned
snapshot; it cannot select live files, arbitrary script paths, or an unenabled
revision. Advertising a skill never advertises a grant.
See [Commands, Tools, Skills, and Model Interaction](Commands_Tools_And_Skills.md).
Shared roots and their resolved contents are protected from generic
Kora-originated writes, including tools, scripts, and MCP pathways.
Future in-place declarative editing uses the qualified
[dedicated writer and exact-change confirmation](Definition_Management.md#editing-ownership-and-confirmation);
registration or enablement alone never permits writes.
Reparse/link targets outside approved source scope require explicit source selection; do not follow arbitrary package links.

Example voice flow:

1. "Kora, show the skills in my profile."
2. Select/register a source if none is configured.
3. Kora lists compatible and unsupported packages with reasons.
4. "Kora, enable the deployment investigation skill from my shared profile."
5. Review its exact revision/dependencies; enable only after checks and user approval.

## Format Compatibility

Support the native Kora declarative format and an explicitly versioned `SKILL.md` reader for instruction-only profile skills.
The latter reads bounded YAML front matter such as name/description plus the Markdown instruction body.
Map supported metadata to a normalised internal skill descriptor without rewriting the source.
Reject malformed/unsupported required fields and explain incompatible executable workflows or unavailable tool references.
Do not silently discard required behaviour, fetch external references, or install dependencies.

A shell snippet in Markdown is text, not permission to run it.
Profile packages containing required scripts, custom validators, executable entry points, or installer hooks remain disabled under the MVP executable policy.
Reading such a package cannot promote its scripts into the bundled trust class.
An explicit user request can instead create a reviewed declarative adaptation using already admitted tools.
Skill instructions and descriptions remain untrusted content; policy is enforced at actual tool use.

## Future User-Provided Executable Skills

Status: future feature, outside the MVP declarative authoring/import policy
and not available in the bootstrap. This section defines dependency discovery
requirements, not permission to enable existing profile scripts today.

Unlike a built-in's explicit embedded manifest, a user-provided executable
skill has an **implicit manifest derived from its selected folder**.
The host reads a supported instruction document such as `SKILL.md`, its
bounded metadata, and the folder's script inventory into a normalised
in-memory descriptor. It does not require or write a built-in-style
manifest file beside it. A folder name is not a trustworthy skill ID, task
binding, or approval.

The descriptor records the source-qualified identity, canonical selected
root, Markdown identity/digest, script entry point, complete dependency set,
per-file identities/digests, and invocation contract. Select an entry point
through supported metadata or an explicit user choice; do not guess among
multiple `.ps1` files or execute code fences. Discover `.ps1` files recursively
within the selected folder under size/depth/count limits, without following
unapproved links. Include that inventory in the script set conservatively,
then expand it with statically resolvable transitive script dependencies.
Folder discovery never grants execution.

### PowerShell Dependency Discovery

For each discovered `.ps1`, use the admitted PowerShell parser/AST, not regex
search or evaluation of the script. Attempt to resolve references including:

- Dot-sourcing, for example `. "$PSScriptRoot\helpers\common.ps1"`.
- Call-operator invocation, for example
  `& "$PSScriptRoot\scripts\step.ps1"`.
- Literal `.ps1` command paths and supported static script imports or
  registered child-script launch forms.
- References in other discovered scripts, recursively.

Only literal paths and explicitly supported constant path forms, such as
`$PSScriptRoot` relative to the referencing file, may resolve automatically.
Relative paths must use the approved execution contract's base directory;
if the runtime base is ambiguous, report an unresolved reference rather than
assuming the skill folder. Do not run `Join-Path`, variable assignments,
module initialisers, or any other code to discover a target.

Canonicalise and verify each resolved file identity within approved source
scope. Reject traversal/link escapes, ambiguous casing/aliases, missing or
unreadable required tracked files with the referencing file and reason.
Unsupported forms remain visible discovery gaps. Reading a reference outside
the selected source scope requires explicit selection of an additional bounded
source; do not scan it implicitly or claim the reference was resolved.
This allows a future shared user helper to be included without trusting
an arbitrary sibling directory or the entire profile.

Walk the dependency graph with a visited set so cycles terminate and each
canonical file appears once in the hash inventory. Detect and report cycles;
never recurse indefinitely or infer that a cycle is safe. Do not label an
inventory complete when parsing or resolution failed.
Variable/computed paths, `Invoke-Expression`, generated/downloaded scripts,
dynamic modules, or unregistered child-process script launches are unresolved
discovery gaps, not an empty dependency list. Follow the owner-approved
[best-effort rule](Built_In_Skills.md#best-effort-transitive-dependency-tracking):
the user accepts responsibility for the granted script's transitive actions
within the admitted scope. Unknown references alone do not block that grant,
but do not authorize a new top-level task or relax resource/privacy controls.
No unrestricted-shell fallback or user-script exposure is enabled here.

Static parsing is best-effort discovery, not proof of all possible PowerShell
behaviour. The host must execute the reviewed declared snapshot and verify
required/tracked identities, while retaining discovery gaps. Universal denial
of undeclared scripts/modules/processes is no longer the default grant
requirement; any explicitly exact-dependency profile still needs actual proof.
OS containment remains independently mandatory. Parser success alone is not
containment admission, and known reviewed content still needs race-resistant
identity/snapshot validation.

### Hashing, Review, and Changes

Use the same [`Kora.ScriptSet.v1` combined-content encoding](Built_In_Skills.md#deterministic-script-set-hash)
as built-ins, not just the entry-point hash. Folder-discovered scripts receive
stable root-relative logical names; explicitly selected external dependencies
receive source-qualified relative names under separate host-owned root IDs.
Sort the complete logical names ordinally, preserve exact content bytes, and
retain canonical physical identities separately in the grant key. Reject
name/alias collisions; do not depend on discovery order or machine-specific
absolute paths for the combined-content encoding. Changed physical targets or
root bindings still require reapproval even when their bytes match.

Compute a definition digest using `Kora.SkillDefinition.v1` over the Markdown,
supported metadata files, and a host-generated implicit manifest. That
manifest uses a versioned deterministic serialization of source bindings,
entry point, dependency graph, and invocation contract; its format must be
specified and covered by fixed-vector tests before this feature ships.
The descriptor is host-calculated from actual files, not a checksum supplied
by the skill. Its authoritative grant record remains device-local.

At enablement/review, capture immutable content; immediately before dispatch,
rediscover folder membership, reparse references, and compare current
identities, hashes, and contracts with the approved snapshot. Watchers are
advisory, never the only check. Added/removed scripts, a new import, changed
dependency bytes, changed Markdown, or a moved/retargeted file revoke affected
grants and require fresh review. Changes to a shared external helper invalidate
all dependent skills, just as for embedded shared scripts.
Reverting content does not restore a revoked grant.
Persist revocation and its content-change reason; retain perpetual records
as revoked rather than deleting them. Fresh approval creates a new grant,
never updates the old digest or silently reactivates the record.

Required future tests cover multi-script folders, nested/transitive imports,
shared dependencies, identical basenames, cycles, missing files, source-scope
escapes, unresolved dynamic references, directory membership changes,
review/launch races, honest dynamic-reference gap disclosure and known shared
dependency invalidation without claiming complete transitive tracking.
Until these checks and containment pass, packages requiring
scripts remain disabled under the existing MVP policy.

## Identity and Revision Handling

Internal identity includes source partition, canonical source/package identity, declared skill ID, and content digest.
Display names and IDs from a package do not override bundled or other-source skills.
For duplicates, show source-qualified choices or use a previously explicit user binding; never silently shadow.
Reserved bundled session-control routing applies only to the original verified registration.

At enablement, safely read a bounded immutable package snapshot and compute its digest.
Keep the approved snapshot in memory for invocation; do not read live changing instructions midway through a task.
Include all permitted referenced package content in the revision identity.
Recheck source content/access before new dispatch and reconcile watcher notifications; watchers alone are not authoritative.

Use the [common reconciliation pipeline](Definition_Management.md#consistent-external-change-and-reload-contract)
for owned and profile sources, manual reload, external changes, and saves.
Refresh catalogues, UI/voice lookup, applicable instructions, and agent
dependencies together; changed revisions require review, not automatic enablement.

Changed content suspends new invocations pending review of the new digest.
An active task retains its existing snapshot; queued tasks do not silently switch versions.
Deletion, source removal, or lost access disables new dispatch and prompts for a fresh decision.
On restart, revalidate content and device-local enablement before using a source.
No silent content-bearing cache persistence is introduced for shared sources.

## Editing and Kora-Owned Forks

"Kora, improve this skill" creates a proposal for a Kora-owned copy with a new scoped ID under `%APPDATA%\Kora\Skills`.
Explain that it is a fork, preserve source attribution/digest, and show the diff before saving.
The fork operation does not modify, rename, delete, or place backups in the
shared source. In the target editor design, supported profile originals can
also be edited directly rather than requiring a fork.
The copy does not inherit grants, enablement, bundled executable trust, or priority privileges.
Later shared-source changes are shown as a difference; no automatic merge/update of the fork.

An explicit "edit the original" request may instead enter the future dedicated
profile writer after registered scope validation, compatibility/round-trip
checks, exact diff/base validation, and action-specific confirmation.
This is not available in R21 and never admits executable or protected-file edits.
Dirty drafts and external changes follow the same conflict rules as Kora-owned
definitions; see [Definition Management](Definition_Management.md).

## Roaming Behaviour and Boundaries

Kora-specific skill definitions reside in Roaming AppData so Windows/profile management can roam them.
Kora does not implement cross-device federation or promise that Windows has enabled roaming.
The [common roaming reconciliation contract](Definition_Management.md#windows-managed-roaming-and-local-revision-admission)
owns receiving-machine review, incomplete updates and concurrent editing limits.
Roaming definitions does not roam credentials, approval tokens, enablement, executable scripts, or machine-specific capabilities.
On another machine, discovered definitions start disabled until local validation, dependency checks, and enablement.

Detect conflicting revisions/IDs or incomplete sync and block affected enablement with a reconciliation choice; do not overwrite silently.
Approved revisions are written through a staged, validated save with atomic publication where the filesystem supports it.
If atomic publication cannot be established, report failure and leave the prior revision active.

Roaming/redirected storage may be managed or synchronised externally and can contain private skill text.
Disclose this during setup; do not store secrets, transcripts, or tool results in skill packages by default.
Local-only mode must not initiate reads/writes to network-backed roots; show the unavailable storage/source explicitly rather than silently choosing another directory.
OS-managed synchronisation outside Kora is a separate environment policy and is not guaranteed offline merely by selecting local-only mode.

## Experiment disposition for bounded R21

No experiment implementation, package script or proof dependency was promoted
to production for this slice. Portable parser/preference/admission tests and
owned Windows filesystem fixtures prove only the bounded local read boundary.
Runtime/control/containment experiment witnesses remain retained under their
existing dispositions; they do not qualify skill execution, egress, model
selection, installed-source protection or full Slice B/R19 acceptance.
Executable-profile dependency discovery and authoring remain later gates.
