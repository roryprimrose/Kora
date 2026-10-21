# Kora.Tools

Portable, host-owned C# action implementations. Start with
[Extending Kora](../../EXTENDING-KORA.md) to choose between an action and a
behavior definition, and [Contributing](../../CONTRIBUTING.md) for checks and terms.
The authoritative implementation policy is
[Built-In Tool Source Layout and Implementation](../../Design/Commands_Tools_And_Skills.md#built-in-tool-source-layout-and-implementation).

## Project structure

[Kora.Tools.csproj](Kora.Tools.csproj) targets `net10.0` and references
[Kora.Core](../Kora.Core). It does not reference Application, Windows, desktop,
Avalonia or provider SDKs. Application consumes Tools; native APIs remain behind
Core contracts implemented in Windows, and desktop supplies DI and presentation.

Folders mirror capability namespaces (`Kora.Tools.<CapabilityGroup>`).
Each actionable tool has its own C# class and typed execution entry point;
helpers and brokers are not separately advertised actions.

| Folder / namespace suffix | Action classes | Shared collaborators |
|---|---|---|
| [Application](Application) | [ApplicationGetVersion](Application/ApplicationGetVersion.cs) | Core `IApplicationVersion`; running-assembly implementation in Application. |
| [Capabilities](Capabilities) | [CapabilitiesList](Capabilities/CapabilitiesList.cs), [CapabilitiesGet](Capabilities/CapabilitiesGet.cs) | [ReadOnlyPage](Capabilities/ReadOnlyPage.cs) and Core descriptor catalogue. |
| [Readiness](Readiness) | [ReadinessGet](Readiness/ReadinessGet.cs) | [RecordedDependencyObservation](Readiness/RecordedDependencyObservation.cs). |
| [Runtime](Runtime) | [RuntimeList](Runtime/RuntimeList.cs), [RuntimeGetStatus](Runtime/RuntimeGetStatus.cs) | [RecordedRuntimeObservation](Runtime/RecordedRuntimeObservation.cs), reusing the readiness projection. |
| [Clipboard](Clipboard) | [ClipboardRead](Clipboard/ClipboardRead.cs), [ClipboardReuse](Clipboard/ClipboardReuse.cs), [ClipboardRevoke](Clipboard/ClipboardRevoke.cs) | [ClipboardSnapshotBroker](Clipboard/ClipboardSnapshotBroker.cs) owns the snapshot and lifecycle policy. |
| [Files](Files) | [LocalFilePreview](Files/LocalFilePreview.cs), [LocalFileSearch](Files/LocalFileSearch.cs) | The existing preview owns admission/revocation and quiescence; Core `ILocalFileRetrieval` / `LocalFileLexicalRetrieval` own the bounded deterministic lexical policy. |
| [Network](Network) | `WebPageGet`, `PreapprovedUriList`, `PreapprovedUriAdd`, `PreapprovedUriRemove`, `PreapprovedUriClear` | `WebPageGet` owns bounded redirect-aware retrieval over a pinned-address transport. Core owns URI/network validation and matching; Application owns configuration, native routing and authorization integration. |

Do not add a broad capability class with one method per tool, duplicate broker
policy across actions, or add classes for speculative unavailable operations.
There is no delivered clipboard-write action.

## Delivered contracts and routes

### R06 read-only observations

[ReadOnlyCapabilityCatalog](../Kora.Core/Tools/ReadOnlyCapabilityCatalog.cs) owns
the six IDs and schema; [the canonical contract](../../Design/Internal_Model_Tools.md#delivered-bounded-read-only-host-foundation)
owns lane/wire bounds and unavailable adapter behavior.

Actions' internal `Execute` methods are called only by
[ReadOnlyCapabilityRegistry](../Kora.Application/Tools/ReadOnlyCapabilityRegistry.cs)
through assembly friendship. Do not make them public SDK/reflection bypasses.
The gateway owns live host/caller admission, strict JSON, cancellation/trace,
logging and complete serialized bounds. Readiness/runtime actions project
recorded observations, not fresh probes.

For example, `describe capability application.get_version` resolves to
`capabilities.get` with `{"id":"application.get_version"}`.
See [presentation wiring](../Kora.Application/ViewModels/MainViewModel.Capabilities.cs)
and [exact commands](../../docs/commands.md#read-only-host-discovery).
This is not a public callable SDK.

The [closed queue profiles](../../Design/Work_Management.md#delivered-closed-targeted-metadata-reads---2026-10-12) invoke these same actions through freshly admitted Native registry callers. Four inputs remain `{}`; one exact current descriptor target or fixed recorded `local.inference` uses typed `CapabilityIdInput`.
Single-record results share the registry wire/shape validation. No probe, inference, arbitrary parameters or model tool-loop qualification is added.

### R07 clipboard actions

The three actions delegate to [ClipboardSnapshotBroker](Clipboard/ClipboardSnapshotBroker.cs).
Core owns [snapshot validation](../Kora.Core/Context/ClipboardSnapshot.cs) and
[origin rules](../Kora.Core/Context/ClipboardCommand.cs); the
[Windows reader](../Kora.Windows/Context/WindowsPlainTextClipboardReader.cs)
implements the native seam. [Application routing](../Kora.Application/ViewModels/MainViewModel.Clipboard.cs)
and desktop controls use the same actions.

[Clipboard policy](../../Design/Security_Data_Flows.md#delivered-r07-local-clipboard-preview---2026-10-07)
owns immutable reuse/revoke, exact bounds and privacy/quiescence requirements.
No clipboard write or model submission is delivered; preview is not transmission
consent. Do not create separate provider or view-model implementations.

### Preapproved network addresses

The Network actions converge settings-window, typed-command and activated-voice
changes on one audited configuration service. A pattern is an absolute HTTP or
HTTPS URI; host wildcards must occupy a complete DNS label, while path and query
wildcards are supported. Credentials and fragments are rejected. The policy is
fail-closed for malformed persistence and exposes `IsPreapproved(Uri)` for an
admitted retrieval boundary.

The delivered `WebPageGet` action is available to the exact typed/activated
voice command `get web page <absolute HTTP-or-HTTPS-address>`. That original
user request is the retrieval authority for its explicit address. The action
resolves DNS, rejects the complete result if any address is non-public, pins
the selected public address into the connection, disables proxies, cookies,
credentials, decompression and automatic redirects, and reauthorizes and
reresolves every redirect. It accepts only unencoded UTF-8 `text/plain` or
`text/html`, strips HTML active/non-text content, reads at most 256 KiB and
returns at most 60 KiB of normalized text within a 64 KiB complete-result
budget. Network outcomes are audited without logging addresses or content.

The host authorization foundation now accepts destination-bound web-page access
requests through its normal interaction transaction. A matching preapproved
address satisfies only the per-address grant requirement after the existing
session, ownership, expiry, effect and mandatory gates pass. An unmatched
address presents the ordinary Once/Session/Perpetual grant question. The raw
host-resolved address must match the proposal's canonical SHA-256 destination
digest. Redirects are new destinations and require a newly published proposal
and another policy/grant decision before any redirected request. The canonical model descriptor is `network.get_web_page` schema 1. It remains
explicitly unavailable with reason `parameterized-model-tool-loop-not-qualified`
because the production local model protocol cannot yet make parameterized tool
calls or resume a pending durable approval. When that loop is qualified, it
must call the same action with `RequestWebPageAccessAsync` as its per-hop
authorization callback; it must not use the direct-user callback.

### R26 immutable local file actions

R26 file actions are host/native-only: one explicitly admitted immutable
volatile UTF-8 revision, exact local search/citations, no current-path reread,
derived persistence, model registry entry or egress. Native query entry and
fixed `search file` / `inspect file` focus commands share the preview broker's
session/task/privacy/generation/cancellation boundary. See
[the exact delivered slice](../../Design/File_And_Folder_Ingestion.md#delivered-selected-revision-lexical-retrieval).

## Contribute a new tool

Apply [product admission](../../Design/MVP_Scope.md#feature-admission) first: name the approved user outcome and why the host owns this capability rather than an extension. This implementation checklist does not approve new native utilities or require exposing helper methods as tools.

1. Consult the [canonical tool inventory](../../Design/Internal_Model_Tools.md)
   and [roadmap](../../Design/Implementation_Roadmap.md). Define an explicit
   ID/schema version, typed inputs/results, host-assigned effect, availability,
   caller lanes and exact limits. Class and method names do not register IDs.
   The current read-only catalogue is not a generic mutation registry.
2. Put one concrete action in `<CapabilityGroup>/<Action>.cs`, using namespace
   `Kora.Tools.<CapabilityGroup>` and a typed execution entry point. Inject
   consumer-focused collaborators; reuse/extract a cohesive broker only for
   genuinely shared behavior. Keep contracts/domain validation in Core and
   OS mechanisms/exceptions in Windows.
3. Wire the applicable Application gateway: explicit descriptor/dispatcher
   binding, validation, host request and ownership admission, original origin,
   privacy, required approval and immediate pre-effect revalidation. Preserve
   the existing R06 internal-entry-point pattern for read-only gateway actions.
   Do not put effect implementations or a second policy engine in the dispatcher.
4. Register actions and collaborators in desktop [Program.cs](../Kora/Program.cs).
   Add every applicable exact command, native control and qualified model route
   over the same action/gate. Document a concrete exception when a route is
   unsuitable or unqualified. DI alone does not advertise or enable a model tool;
   legacy `MainViewModel` commands and the JSON selector are not generic tools.
5. Return content-minimizing bounded observations or truthful outcomes, separate
   from window/speech presentation. Preserve structured host correlation/audit,
   cancellation/deadline handling and deterministic disposal/quiescence. No
   raw user content in logs/traces, silent success defaults, automatic provider
   fallback or weakened unavailable gates.
6. Add focused tests, then update the canonical inventory and relevant user
   commands/status documentation. Keep the declared deterministic and model
   routes consistent; test that unknown, ambiguous, unauthorized and unavailable
   requests fail closed without effects.

Use the [action exposure rule](../../Design/Commands_Tools_And_Skills.md#action-exposure-is-the-default)
and [gateway policy](../../Design/Architecture.md#tool-gateway), with
[Security and Data Flows](../../Design/Security_Data_Flows.md) as the policy
source rather than copying authorization rules into this guide or tool metadata.

## Tests and validation

- Add action/broker tests under
  [Kora.Tools.UnitTests](../../tests/Kora.Tools.UnitTests)/`<CapabilityGroup>`.
  Existing [read-only action tests](../../tests/Kora.Tools.UnitTests/Capabilities/ReadOnlyActionTests.cs)
  prove per-action classes and no public R06 gateway bypass.
- Add domain/catalogue tests in [Core tests](../../tests/Kora.Core.UnitTests)
  and gateway/route tests in [Application tests](../../tests/Kora.Application.UnitTests).
  Start with [registry tests](../../tests/Kora.Application.UnitTests/Tools/ReadOnlyCapabilityRegistryTests.cs)
  for caller isolation, strict fields, exact serialized byte limits and tracing;
  use [clipboard tests](../../tests/Kora.Tools.UnitTests/Clipboard) for lifecycle.
- Cover errors, cancellation, privacy/origin/ownership loss, hostile inputs,
  stale results, resource release and content minimization using deterministic
  fakes. Native tests belong in [Windows integration tests](../../tests/Kora.Windows.IntegrationTests);
  unit tests must not require devices, user data, inference or live effects.
- Run [development checks](../../CONTRIBUTING.md#development-checks).
  [CI](../../.github/workflows/ci.yml) retains the unchanged **100% portable
  line/branch coverage gate** for Core, Application, Tools and Definitions.
