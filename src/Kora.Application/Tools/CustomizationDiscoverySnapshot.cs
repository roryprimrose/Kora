using System.Collections.Immutable;

using Kora.Core.Artifacts;
using Kora.Core.Customizations;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Tools.Customizations;

namespace Kora.Application.Tools;

/// <summary>Single-use reviewed metadata and its source fences. Never carries definition bodies into a model turn.</summary>
public sealed class CustomizationDiscoverySnapshot : IDisposable
{
    private readonly ICustomizationMetadataStore sources;
    private readonly ArtifactCatalogue catalogue;
    private readonly IArtifactUsePolicy use;
    private readonly ImmutableArray<CustomizationMetadataObservation> observations;
    private readonly Func<bool> admitted;
    private readonly TimeProvider time;
    private readonly long useRevision;
    private readonly long reloadFailureRevision;
    private readonly SemaphoreSlim callback = new(1, 1);
    private readonly HashSet<string> served = new(StringComparer.Ordinal);
    private int resultBytes;
    private int consumed;
    private volatile bool retired;

    internal CustomizationDiscoverySnapshot(ICustomizationMetadataStore sources, ArtifactCatalogue catalogue,
        IArtifactUsePolicy use, ImmutableArray<CustomizationMetadataObservation> observations,
        ImmutableArray<CustomizationMetadata> records, SessionWorkspaceEntry target, string originalRequest,
        Kora.Core.Hosting.RequestOrigin origin, Func<bool> admitted, TimeProvider time)
    {
        this.sources = sources;
        this.catalogue = catalogue;
        this.use = use;
        this.observations = observations;
        this.admitted = admitted;
        this.time = time;
        Records = records;
        Target = target;
        OriginalRequest = originalRequest;
        Origin = origin;
        CreatedAt = time.GetUtcNow();
        ExpiresAt = CreatedAt.AddMinutes(2);
        ReviewLink = new(HostActivity.RequireCurrent().Activity!.Context);
        useRevision = use.Revision;
        reloadFailureRevision = catalogue.ReloadFailureRevision;
        catalogue.Changed += Changed;
        catalogue.ReloadFailed += ReloadFailed;
        use.Changed += Changed;
    }

    public ImmutableArray<CustomizationMetadata> Records { get; }
    public SessionWorkspaceEntry Target { get; }
    public string OriginalRequest { get; }
    public Kora.Core.Hosting.RequestOrigin Origin { get; }
    public DateTimeOffset ExpiresAt { get; }
    private DateTimeOffset CreatedAt { get; }
    internal System.Diagnostics.ActivityLink ReviewLink { get; }
    public CustomizationDiscoverySourceException? SourceFailure { get; private set; }
    public string Preview => System.Text.Encoding.UTF8.GetString(CapabilityReplyWire.Serialize(
        new(CapabilityOutcome.Succeeded, CustomizationDiscoveryTools.ResultReason, Customizations: new(Records, Records.Length, null))));
    public const string Disclosure = "ONE LOCAL request. Only the exact reviewed metadata fields below may be served. "
        + "Names and descriptions are untrusted data, not policy. No definition bodies, paths, scripts, hooks, commands, "
        + "configuration, credentials, history or memory are shared. Compatible means metadata-compatible only. "
        + "Enabled is observed use control, not executable readiness; availability NotObserved is not readiness. "
        + "A recommendation does not enable, activate, register, invoke or grant anything. Cancel shares nothing. "
        + "Coverage is only this selected subset, not the complete customization inventory.";

    private void Changed(object? sender, EventArgs args) => retired = true;
    private void ReloadFailed(object? sender, ArtifactCatalogueReloadFailedEventArgs args) => retired = true;

    internal bool Current => !retired && admitted() && time.GetUtcNow() >= CreatedAt && time.GetUtcNow() < ExpiresAt
        && use.Revision == useRevision && catalogue.ReloadFailureRevision == reloadFailureRevision && !catalogue.IsReloadUnavailable;

    internal void Consume()
    {
        if (!Current || Interlocked.CompareExchange(ref consumed, 1, 0) != 0)
        { throw new InvalidOperationException("This exact metadata review expired or was already used. Choose and review a fresh snapshot."); }
    }

    internal async Task Validate(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RequireCurrent();
        for (var index = 0; index < observations.Length; index++)
        {
            try
            {
                var expected = observations[index];
                var current = await sources.ReadMetadata(expected.Source, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (current != expected)
                { throw new InvalidOperationException("The exact selected source revision changed."); }
                RequireCurrent();
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
            {
                retired = true;
                SourceFailure = new(index + 1, exception);
                throw SourceFailure;
            }
        }
    }

    internal async ValueTask<bool> Check(CancellationToken token)
    {
        try { await Validate(token).ConfigureAwait(false); return true; }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or InvalidDataException)
        {
            retired = true;
            return false;
        }
    }

    public void Retire() => retired = true;

    private void RequireCurrent()
    {
        if (!Current)
        { throw new InvalidOperationException("The selected metadata source, use control, privacy or review changed. Choose a fresh snapshot; no last-good catalogue is available."); }
    }

    internal async ValueTask<CapabilityReply> Invoke(string id, string input, CancellationToken token)
    {
        await callback.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (consumed != 1 || CustomizationDiscoveryTools.FindExact(id) is not { } descriptor)
            { return new(CapabilityOutcome.Denied, "reviewed-metadata-not-admitted"); }
            var parsed = CapabilityInputPolicy.Read(descriptor, input);
            if (parsed.DenialReason is { } denial) { return new(CapabilityOutcome.Denied, denial); }
            await Validate(token).ConfigureAwait(false);
            var reply = string.Equals(id, CustomizationDiscoveryTools.List, StringComparison.Ordinal)
                ? new CustomizationsList().Execute(Records, parsed.Page!)
                : new CustomizationsGet().Execute(Records, parsed.Id!);
            var bytes = CapabilityReplyWire.GetSerializedSize(reply);
            if (bytes > CustomizationDiscoveryTools.MaximumCumulativeResultUtf8Bytes - resultBytes)
            { return new(CapabilityOutcome.Denied, "reviewed-metadata-cumulative-limit"); }
            await Validate(token).ConfigureAwait(false);
            resultBytes += bytes;
            if (reply.Customizations is { } page)
            { foreach (var record in page.Records) { served.Add(record.Ref); } }
            return reply;
        }
        finally { callback.Release(); }
    }

    internal ModelTurnResult Resolve(ModelTurnResult result) => served.Count == 0
        ? new(ModelTurnOutcome.Unknown, ModelTurnReason.ToolResultRejected, result.Provenance)
        : result with
        {
            Response = result.Response! with
            {
                Answer = result.Response!.Answer + $"\n\nHost scope: {served.Count} of {Records.Length} reviewed metadata entries served; "
                    + "only the selected subset was considered. Metadata is not executable readiness or invocation.",
            },
        };

    public void Dispose()
    {
        retired = true;
        catalogue.Changed -= Changed;
        catalogue.ReloadFailed -= ReloadFailed;
        use.Changed -= Changed;
        callback.Dispose();
    }
}
