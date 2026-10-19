using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

/// <summary>One exact-session original-user review workflow; never app, account, generation or egress authority.</summary>
public sealed partial class SessionProviderProfileConfigurationService(
    SessionWorkspaceService workspace, ISessionProviderProfileStore store, IProviderProfilePreferences preferences,
    IProviderCapabilityCatalogue catalogue, TimeProvider time, ILogger<SessionProviderProfileConfigurationService> logger,
    ProviderProfileConfigurationService appConfiguration)
{
    private readonly Lock gate = new();
    private readonly HashSet<SessionProviderDraft> drafts = [];
    private readonly HashSet<SessionProviderProposal> proposals = [];
    private bool applying;
    private SessionProviderProposal? applyingProposal;

    public async Task<SessionProviderDraft> Inspect(HostId<SessionIdentity> session, RequestOrigin origin,
        Func<bool> eligible, CancellationToken token)
    {
        var result = await workspace.InspectProviderProfile(session, origin, eligible, async (originalOrigin, admitted) =>
        {
            var saved = await store.ReadProviderProfile(session, token).ConfigureAwait(false);
            if (saved.Session.SessionId != session) { throw new InvalidDataException("Provider policy observation belongs to another exact session."); }
            var current = await catalogue.ObserveAsync(token).ConfigureAwait(false);
            var app = preferences.Load() ?? new ProviderProfilePreferences(
                current.Installation == LocalProviderInstallation.Installed ? ProviderService.Local : ProviderService.Unset, []);
            if (!admitted() || !eligible() || token.IsCancellationRequested) { throw new InvalidOperationException("Provider inspection lost private session/input admission."); }
            var effective = saved.Held ? null : ResolveEffective(saved.Desired, app, current);
            return new SessionProviderDraft(saved, app, current, effective, originalOrigin, admitted, appConfiguration.Get().Revision);
        }, token).ConfigureAwait(false);
        lock (gate)
        {
            if (drafts.Count + proposals.Count >= 64) { throw new InvalidOperationException("Provider preview capacity reached. Close the native review or explicitly discard previews."); }
            drafts.Add(result);
        }
        return result;
    }

    public void Discard(SessionProviderDraft draft)
    {
        lock (gate)
        {
            drafts.Remove(draft);
            foreach (var copy in proposals.Where(proposal => ReferenceEquals(proposal.Draft, draft) || ReferenceEquals(proposal.Source, draft)).ToArray())
            {
                if (copy.Source is { } source) { drafts.Remove(source); }
            }
            proposals.RemoveWhere(proposal => ReferenceEquals(proposal.Draft, draft) || ReferenceEquals(proposal.Source, draft));
        }
    }

    public void Retire(SessionProviderProposal proposal)
    {
        lock (gate)
        {
            proposals.Remove(proposal);
            if (ReferenceEquals(applyingProposal, proposal)) { applyingProposal = null; }
            if (proposal.Source is { } source) { Discard(source); }
        }
    }

    /// <summary>Reviews exact sparse configuration data, never the source's effective tuple or authority.</summary>
    public async Task<SessionProviderProposal> ReviewCopy(HostId<SessionIdentity> source, HostId<SessionIdentity> destination,
        RequestOrigin origin, Func<bool> eligible, CancellationToken token)
    {
        if (source == destination) { throw new InvalidOperationException("Choose distinct exact source and destination sessions."); }
        SessionProviderDraft? from = null;
        SessionProviderDraft? to = null;
        var admission = workspace.CaptureProviderCopyAdmission(eligible);
        try
        {
            from = await Inspect(source, origin, admission, token).ConfigureAwait(false);
            to = await Inspect(destination, origin, admission, token).ConfigureAwait(false);
            if (from.Saved.Held || from.Origin != to.Origin || from.AppRevision != to.AppRevision
                || !string.Equals(from.App.Serialize(), to.App.Serialize(), StringComparison.Ordinal)
                || !SameObservation(from.Observation, to.Observation)
                || from.Saved.Desired.Binding is not null && ResolveEffective(from.Saved.Desired, to.App, to.Observation) is null)
            { throw new InvalidOperationException("Source binding, app, account or catalogue is unavailable or changed. Nothing was rebound or substituted."); }
            return Issue(to, from.Saved.Desired, null, from);
        }
        catch
        {
            if (from is not null) { Discard(from); }
            if (to is not null) { Discard(to); }
            throw;
        }
    }

    public SessionProviderProposal SelectService(SessionProviderDraft draft, ProviderService service) =>
        Issue(draft, new(service, draft.Saved.Desired.Model, draft.Saved.Desired.Reasoning, draft.Saved.Desired.Context, draft.Saved.Desired.Binding), SessionProviderField.Service);

    public SessionProviderProposal Reset(SessionProviderDraft draft, SessionProviderField? field = null) =>
        Issue(draft, field is { } selected ? draft.Saved.Desired.Reset(selected) : SessionProviderOverrides.Inherit, field);

    public SessionProviderProposal SelectField(SessionProviderDraft draft, SessionProviderField field, ProviderChoiceId id)
    {
        var profileField = field switch
        {
            SessionProviderField.Model => ProviderProfileField.Model,
            SessionProviderField.Reasoning => ProviderProfileField.Reasoning,
            SessionProviderField.Context => ProviderProfileField.Context,
            _ => throw new InvalidDataException("Select a model, reasoning or context field; service has its own review."),
        };
        var current = draft.Catalogue;
        var effective = draft.Effective;
        if (current is null || ProviderProfileRules.Resolve(effective, current, time.GetUtcNow()) is null)
        { throw new InvalidOperationException("No complete current effective session tuple. Reset unavailable overrides or review the complete profile; no choice was fabricated."); }
        var candidate = ProviderProfileRules.ReplaceField(effective!, profileField, id);
        var model = ProviderProfileRules.Resolve(candidate, current, time.GetUtcNow())
            ?? throw new InvalidOperationException("The exact field choice is unavailable or incompatible with retained reasoning/context. Review the complete profile; no dependent choice was cleared.");
        return Select(draft, field, model, model.Reasoning.Single(option => option.Id == candidate.Reasoning),
            model.Context.Single(option => option.Id == candidate.Context));
    }

    public SessionProviderProposal Select(SessionProviderDraft draft, SessionProviderField? field,
        ProviderCatalogueModel model, ProviderProfileOption reasoning, ProviderProfileOption context)
    {
        var current = draft.Catalogue;
        if (current is null || !current.IsCurrent(time.GetUtcNow())
            || !current.Models.Any(item => ReferenceEquals(item, model))
            || !model.Reasoning.Any(item => ReferenceEquals(item, reasoning))
            || !model.Context.Any(item => ReferenceEquals(item, context))
            || current.Service == ProviderService.Local && draft.Observation.Installation != LocalProviderInstallation.Installed)
        { throw new InvalidOperationException("Choose exact options from the current effective service/account/model catalogue."); }
        var binding = new ProviderProfileTuple(current.Service, current.Account!, current.Revision, model.Id, reasoning.Id, context.Id);
        var desired = draft.Saved.Desired.Select(binding, field);
        if (desired.ResolveDesired(draft.App) != binding)
        { throw new InvalidOperationException("The sparse result does not resolve to the reviewed tuple. Reset dependent overrides or review the complete profile; inherited fields were not frozen."); }
        return Issue(draft, desired, field);
    }

    private SessionProviderProposal Issue(SessionProviderDraft draft, SessionProviderOverrides desired, SessionProviderField? field,
        SessionProviderDraft? source = null)
    {
        lock (gate)
        {
            if (applying || !drafts.Contains(draft) || source is not null && !drafts.Contains(source)
                || !draft.RemainsAdmitted() || source is not null && !source.RemainsAdmitted()
                || !drafts.Contains(draft) || source is not null && !drafts.Contains(source)
                || draft.Saved.Held || !draft.Saved.Session.IsActive || drafts.Count + proposals.Count >= 64)
            { throw new InvalidOperationException("Inspect an exact active session under current original-user admission before reviewing changes."); }
            var now = time.GetUtcNow();
            var expiresAt = new[]
            {
                now.AddMinutes(5), draft.Observation.Catalogues.Min(item => item.ValidUntil),
                source?.Observation.Catalogues.Min(item => item.ValidUntil) ?? DateTimeOffset.MaxValue,
            }.Min();
            if (expiresAt <= now) { throw new InvalidOperationException("The provider observation expired. Refresh before reviewing changes."); }
            var proposal = new SessionProviderProposal(draft, desired, field, ResolveEffective(desired, draft.App, draft.Observation),
                expiresAt, source);
            proposals.Add(proposal);
            return proposal;
        }
    }

    private ProviderProfileTuple? ResolveEffective(SessionProviderOverrides policy, ProviderProfilePreferences app, ProviderCatalogueObservation observation)
    {
        var service = policy.EffectiveService(app);
        var tuple = policy.ResolveDesired(app);
        return service != ProviderService.Unset
            && (service != ProviderService.Local || observation.Installation == LocalProviderInstallation.Installed)
            && ProviderProfileRules.Resolve(tuple, observation.For(service), time.GetUtcNow()) is not null ? tuple : null;
    }

    public async Task<SessionProviderProfile> Confirm(SessionProviderProposal proposal, RequestOrigin origin,
        Func<bool> eligible, CancellationToken token)
    {
        lock (gate)
        {
            var issued = proposals.Remove(proposal);
            if (applying || !issued || proposal.Draft.Origin != origin || !ProposalLive(proposal))
            {
                if (issued)
                {
                    Discard(proposal.Draft);
                    if (proposal.Source is { } source) { Discard(source); }
                }
                throw new InvalidOperationException("Provider confirmation is stale, foreign or from another input origin.");
            }
            applying = true;
            applyingProposal = proposal;
        }
        try
        {
            return await workspace.ProviderProfileControl(proposal.Draft.Saved.Session.SessionId, origin, eligible, async (request, admitted) =>
            {
                using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
                try
                {
                    var current = await catalogue.ObserveAsync(token).ConfigureAwait(false);
                    var app = preferences.Load() ?? new ProviderProfilePreferences(
                        current.Installation == LocalProviderInstallation.Installed ? ProviderService.Local : ProviderService.Unset, []);
                    var destination = await store.ReadProviderProfile(request.SessionId, token).ConfigureAwait(false);
                    if (!SameProfile(proposal.Draft.Saved, destination))
                    { throw new InvalidOperationException("The exact destination policy or lifecycle changed; review again."); }
                    if (proposal.Source is { } source)
                    {
                        var latest = await store.ReadProviderProfile(source.Saved.Session.SessionId, token).ConfigureAwait(false);
                        if (!SameProfile(source.Saved, latest))
                        { throw new InvalidOperationException("The exact source policy or lifecycle changed; nothing was copied."); }
                    }
                    bool Live() => admitted() && eligible()
                        && string.Equals(app.Serialize(), proposal.Draft.App.Serialize(), StringComparison.Ordinal)
                        && string.Equals((preferences.Load() ?? new ProviderProfilePreferences(
                            current.Installation == LocalProviderInstallation.Installed ? ProviderService.Local : ProviderService.Unset, [])).Serialize(),
                            app.Serialize(), StringComparison.Ordinal)
                        && SameObservation(proposal.Draft.Observation, current)
                        && (proposal.Desired.Binding is null || ProviderProfileRules.Resolve(proposal.Desired.Binding,
                            current.For(proposal.Desired.Binding.Service), time.GetUtcNow()) is not null)
                        // Preference/admission callbacks may synchronously retire the review.
                        && ProposalLive(proposal) && IsApplying(proposal) && !token.IsCancellationRequested
                        && appConfiguration.Get().Revision == proposal.Draft.AppRevision;
                    if (!Live()) { throw new InvalidOperationException("App, account, catalogue, installation or original session review changed; no choice was substituted."); }
                    var result = await store.CommitProviderProfile(request, proposal.Draft.Saved, proposal.Desired, Live, token,
                        proposal.Source?.Saved).ConfigureAwait(false);
                    activity.Complete(HostOperationOutcome.Completed);
                    return result;
                }
                catch (OperationCanceledException)
                {
                    activity.Complete(HostOperationOutcome.Cancelled);
                    throw;
                }
                catch (Exception exception)
                {
                    Failure(logger, exception.GetType().Name);
                    activity.Complete(HostOperationOutcome.Unknown);
                    throw;
                }
            }, token).ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                applying = false;
                applyingProposal = null;
                Discard(proposal.Draft);
                if (proposal.Source is { } source) { Discard(source); }
            }
        }
    }

    private bool IsApplying(SessionProviderProposal proposal)
    {
        lock (gate) { return ReferenceEquals(applyingProposal, proposal); }
    }

    private bool ProposalLive(SessionProviderProposal proposal)
    {
        if (time.GetUtcNow() >= proposal.ExpiresAt || !proposal.Draft.RemainsAdmitted()
            || proposal.Source is { } source && !source.RemainsAdmitted()) { return false; }
        lock (gate)
        {
            return drafts.Contains(proposal.Draft) && (proposal.Source is null || drafts.Contains(proposal.Source));
        }
    }

    private static bool SameProfile(SessionProviderProfile expected, SessionProviderProfile current) =>
        !expected.Held && !current.Held && expected.Session == current.Session && expected.Revision == current.Revision
        && string.Equals(expected.Desired.Serialize(), current.Desired.Serialize(), StringComparison.Ordinal);

    private static bool SameObservation(ProviderCatalogueObservation before, ProviderCatalogueObservation after) =>
        before.Installation == after.Installation && before.Catalogues.All(previous =>
            after.For(previous.Service) is { } current && previous.Service == current.Service
            && previous.Account == current.Account && previous.Revision == current.Revision
            && previous.Status == current.Status
            && (previous.Status != ProviderCatalogueStatus.Current
                || previous.ObservedAt == current.ObservedAt && previous.ValidUntil == current.ValidUntil)
            && previous.Models.Count == current.Models.Count && previous.Models.Zip(current.Models).All(pair =>
                pair.First.Id == pair.Second.Id && pair.First.Selection == pair.Second.Selection
                && string.Equals(pair.First.Label, pair.Second.Label, StringComparison.Ordinal)
                && pair.First.Reasoning.SequenceEqual(pair.Second.Reasoning) && pair.First.Context.SequenceEqual(pair.Second.Context)));
}