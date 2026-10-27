using System.Collections.Immutable;

using Kora.Core.Artifacts;
using Kora.Core.Conversation;
using Kora.Core.Customizations;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Tools;

namespace Kora.Application.Tools;

public sealed class CustomizationDiscoveryService(ICustomizationMetadataStore sources, ArtifactCatalogue catalogue,
    IArtifactUsePolicy use, ISessionWorkspaceAccess access, TimeProvider time)
{
    public async Task<IReadOnlyList<CustomizationSource>> List(Func<bool> admitted, CancellationToken token)
    {
        var control = access.ControlRevision;
        Require(control, admitted, token);
        var result = await sources.ListMetadataSources(token).ConfigureAwait(false);
        Require(control, admitted, token);
        if (result.Count > 512 || result.Any(source => !CustomizationDiscoveryTools.IsSupported(source.Kind)
            || source.Scope is not (CustomizationScope.Kora or CustomizationScope.User)
            || source.IsReadOnly || source.UnavailableReason is not null)
            || result.Select(source => source.Id).Distinct(StringComparer.Ordinal).Count() != result.Count)
        { throw new InvalidDataException("The supported customization source inventory is invalid. No current catalogue was produced."); }
        return result;
    }

    public async Task<CustomizationDiscoverySnapshot> Review(IReadOnlyList<CustomizationSource> selected,
        SessionWorkspaceEntry target, string originalRequest, RequestOrigin origin, Func<bool> admitted, CancellationToken token,
        bool proposeAgent = false)
    {
        var control = access.ControlRevision;
        Require(control, admitted, token);
        ConversationText.Validate(originalRequest, submission: true);
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || origin != HostActivity.RequireCurrent().Request.Origin || !target.Authority.IsActive
            || selected.Count is < 1 or > CustomizationDiscoveryTools.MaximumRecords
            || selected.Select(source => source.Id).Distinct(StringComparer.Ordinal).Count() != selected.Count)
        { throw new InvalidDataException("Choose one to six exact supported sources and one Active conversation for the original request."); }
        var observations = ImmutableArray.CreateBuilder<CustomizationMetadataObservation>();
        var records = ImmutableArray.CreateBuilder<CustomizationMetadata>();
        var useRevision = use.Revision;
        var reload = catalogue.ReloadFailureRevision;
        foreach (var source in selected)
        {
            if (!CustomizationDiscoveryTools.IsSupported(source.Kind) || source.IsReadOnly || source.UnavailableReason is not null)
            { throw new InvalidDataException("The selected source is not supported current metadata."); }
            CustomizationMetadataObservation observed;
            try { observed = await sources.ReadMetadata(source, token).ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
            { throw new CustomizationDiscoverySourceException(observations.Count + 1, exception); }
            Require(control, admitted, token);
            if (observed.Source != source)
            { throw new InvalidDataException("The metadata observation does not belong to the selected source."); }
            var candidate = catalogue.Candidates.SingleOrDefault(item =>
                string.Equals(item.UseControlId ?? item.Id, source.Id, StringComparison.Ordinal)
                || string.Equals(item.Id, observed.ArtifactId, StringComparison.Ordinal));
            var enabled = !use.IsDisabled(source.Id) && candidate is { IsUseEnabled: true }
                && string.Equals(candidate.Digest, observed.Digest, StringComparison.Ordinal);
            var record = new CustomizationMetadata(Guid.NewGuid().ToString("N"), source.Kind,
                observed.Name, observed.Description, source.Scope, true, enabled, CapabilityAvailability.NotObserved);
            if (!CustomizationDiscoveryTools.IsValid(record))
            { throw new InvalidDataException("The selected metadata exceeds the complete supported field bounds. Nothing was truncated; choose a different source."); }
            observations.Add(observed);
            records.Add(record);
        }
        if (use.Revision != useRevision || catalogue.ReloadFailureRevision != reload)
        { throw new InvalidOperationException("Use control or source availability changed during review. Choose a fresh snapshot."); }
        if (!CapabilityReplyWire.IsWithinLimit(new(CapabilityOutcome.Succeeded, CustomizationDiscoveryTools.ResultReason,
            Customizations: new(records, records.Count, null))))
        { throw new InvalidDataException("The complete reviewed metadata exceeds the result bound. Choose fewer entries."); }
        var snapshot = new CustomizationDiscoverySnapshot(sources, catalogue, use, observations.ToImmutable(), records.ToImmutable(),
            target, originalRequest, origin, () => access.ControlRevision == control && access.CanControl && admitted(), time,
            proposeAgent);
        try { await snapshot.Validate(token).ConfigureAwait(false); return snapshot; }
        catch { snapshot.Dispose(); throw; }
    }

    private void Require(long control, Func<bool> admitted, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var current = HostActivity.Current;
        if (!access.CanControl || !access.CanInspect || access.ControlRevision != control || !admitted()
            || catalogue.IsReloadUnavailable || current is null || current.Activity!.IsStopped
            || current.Outcome != HostOperationOutcome.Unknown
            || current.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || current.Request.InvocationId is not null)
        { throw new InvalidOperationException("Private original-user metadata discovery is unavailable. No cached or empty-success catalogue was returned."); }
    }
}
