using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.Configuration;

/// <summary>Explicit app-default configuration only. Selection never supplies authentication, egress or execution authority.</summary>
public sealed class ProviderProfileConfigurationService(
    IProviderProfilePreferences preferences, IProviderCapabilityCatalogue catalogue,
    AudioControlAdmission admission, ISecurityAuditLog auditLog, TimeProvider time)
{
    private const int MaximumIssuedChoices = 64;
    private readonly Lock gate = new();
    private readonly Guid owner = Guid.NewGuid();
    private ProviderProfilePreferences? saved;
    private ProviderProfilePreferences? desired;
    private ProviderCatalogueObservation? observation;
    private IReadOnlyList<ProviderProfileChoice> choices = [];
    private readonly HashSet<ProviderProfileChoice> issued = [];
    private WorkSessionAuthorization? session;
    private RequestOrigin origin;
    private Func<bool>? remainsAdmitted;
    private long revision;
    private bool held = true;
    private bool applying;
    private string? recovery = "Inspect provider profiles in the owning unlocked host.";

    public event EventHandler? Changed;
    public IReadOnlyList<ProviderProfileChoice> Choices { get { lock (gate) { return choices; } } }
    public IReadOnlyList<ProviderCatalogueModel> Models
    {
        get
        {
            lock (gate)
            {
                return !held && desired is { Service: not ProviderService.Unset } && observation is not null
                    && (desired.Service != ProviderService.Local || observation.Installation == LocalProviderInstallation.Installed)
                    && observation.For(desired.Service).IsCurrent(time.GetUtcNow()) ? observation.For(desired.Service).Models : [];
            }
        }
    }

    public ProviderCapabilityCatalogue? CatalogueFor(ProviderProfileChoice choice)
    {
        lock (gate)
        {
            var service = choice.Tuple?.Service ?? choice.Service;
            return !held && issued.Contains(choice) && choice.Revision == revision && choice.RemainsAdmitted()
                && service != ProviderService.Unset ? observation!.For(service) : null;
        }
    }

    public IReadOnlyList<ProviderCatalogueModel> ModelsFor(ProviderProfileChoice choice)
    {
        lock (gate)
        {
            var current = CatalogueFor(choice);
            return current is not null && current.IsCurrent(time.GetUtcNow())
                && (current.Service != ProviderService.Local || observation!.Installation == LocalProviderInstallation.Installed)
                ? current.Models : [];
        }
    }

    public ProviderProfileConfigurationState Get()
    {
        lock (gate)
        {
            var tuple = desired is null ? null : desired.For(desired.Service);
            var effective = !held && desired is { Service: not ProviderService.Unset } && observation is not null
                && (desired.Service != ProviderService.Local || observation.Installation == LocalProviderInstallation.Installed)
                && ProviderProfileRules.Resolve(tuple, observation.For(desired.Service), time.GetUtcNow()) is not null ? tuple : null;
            var status = held ? "Unavailable: configuration/evidence unconfirmed"
                : desired!.Service == ProviderService.Unset ? "Unset: explicitly choose a service"
                : effective is not null ? "Catalogue tuple resolved; runtime qualification and egress are independent"
                : observation!.For(desired.Service).Status == ProviderCatalogueStatus.AuthenticationRequired
                    ? "AuthenticationRequired: no qualified sign-in action is composed"
                    : "Unavailable: desired service/tuple retained; no current complete account-bound catalogue";
            return new(revision, saved, desired, effective, status, recovery,
                observation?.Installation ?? LocalProviderInstallation.Unknown, !held);
        }
    }

    private static bool Same(ProviderProfilePreferences? left, ProviderProfilePreferences? right) => string.Equals(left?.Serialize(), right?.Serialize(), StringComparison.Ordinal);
    private ProviderProfilePreferences? Read(bool readBack = false) => readBack ? preferences.ReadBack() : preferences.Load();
    private static ProviderService Initial(LocalProviderInstallation installation) =>
        installation == LocalProviderInstallation.Installed ? ProviderService.Local : ProviderService.Unset;

    public async Task RefreshAsync(RequestOrigin requestOrigin, Func<bool> eligible, CancellationToken token)
    {
        Enter();
        var expected = 0L;
        try
        {
            await admission.RunObservedAsync(requestOrigin, eligible, catalogue.ObserveAsync, (request, authority, current) =>
            {
                lock (gate)
                {
                    saved = Read();
                    desired = saved ?? new ProviderProfilePreferences(Initial(current.Installation), []);
                    observation = current;
                    revision = checked(revision + 1);
                    expected = revision;
                    session = authority;
                    origin = request.Origin;
                    remainsAdmitted = eligible;
                    choices = Array.AsReadOnly(Enum.GetValues<ProviderService>().Select(service =>
                        new ProviderProfileChoice(desired.Select(service), revision, owner, authority, request.Origin, eligible)).ToArray());
                    issued.Clear();
                    issued.UnionWith(choices);
                    return true;
                }
            }, token).ConfigureAwait(false);
            lock (gate)
            {
                if (expected != revision || !eligible() || token.IsCancellationRequested || !Same(Read(), saved))
                { throw new InvalidOperationException("Provider discovery or saved state changed before its completed receipt."); }
                held = false;
                recovery = null;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch { HoldUnavailable(); throw; }
        finally { Exit(); }
    }

    public ProviderProfileChoice CreateTupleChoice(ProviderCatalogueModel model, ProviderProfileOption reasoning, ProviderProfileOption context,
        ProviderProfileChoice? serviceChoice = null)
    {
        lock (gate)
        {
            RequireDraft();
            var selected = serviceChoice ?? choices.Single(item => item.Service == desired!.Service);
            if (!ModelsFor(selected).Any(item => ReferenceEquals(item, model))
                || !model.Reasoning.Any(item => ReferenceEquals(item, reasoning)) || !model.Context.Any(item => ReferenceEquals(item, context)))
            { throw new InvalidOperationException("Choose exact model, reasoning and context IDs from the current service catalogue."); }
            var current = observation!.For(selected.Service);
            var tuple = new ProviderProfileTuple(selected.Service, current.Account!, current.Revision, model.Id, reasoning.Id, context.Id);
            var choice = new ProviderProfileChoice(selected.Value.Select(tuple), revision, owner, session!, origin, remainsAdmitted!, tuple);
            Issue(choice);
            return choice;
        }
    }

    public ProviderProfileChoice CreateResetChoice(bool tupleOnly, ProviderProfileChoice? serviceChoice = null)
    {
        lock (gate)
        {
            RequireDraft();
            if (serviceChoice is not null && !issued.Contains(serviceChoice))
            { throw new InvalidOperationException("Selected service reset preview is stale or foreign."); }
            var value = tupleOnly ? desired!.Reset(serviceChoice?.Service ?? desired.Service) : desired!.Select(Initial(observation!.Installation));
            var choice = new ProviderProfileChoice(value, revision, owner, session!, origin, remainsAdmitted!,
                resetInstallation: tupleOnly ? null : observation!.Installation);
            Issue(choice);
            return choice;
        }
    }

    public ProviderCatalogueModel? SavedModelFor(ProviderProfileChoice choice)
    {
        lock (gate)
        {
            var current = CatalogueFor(choice);
            return current is null || current.Service == ProviderService.Local && observation!.Installation != LocalProviderInstallation.Installed
                ? null : ProviderProfileRules.Resolve(saved?.For(choice.Service), current, time.GetUtcNow());
        }
    }

    public ProviderProfileChoice CreateFieldChoice(ProviderProfileField field, ProviderChoiceId id,
        ProviderProfileChoice? serviceChoice = null)
    {
        lock (gate)
        {
            RequireDraft();
            var selected = serviceChoice ?? choices.Single(item => item.Service == desired!.Service);
            if (SavedModelFor(selected) is null || !Same(Read(), saved))
            { throw new InvalidOperationException("An exact current saved complete tuple is required. Use the complete providers.app.profile draft/save to choose all dependent fields."); }
            var current = observation!.For(selected.Service);
            var baseTuple = saved!.For(selected.Service)!;
            var tuple = ProviderProfileRules.ReplaceField(baseTuple, field, id);
            if (ProviderProfileRules.Resolve(tuple, current, time.GetUtcNow()) is null)
            { throw new InvalidOperationException("The proposed field and retained reasoning/context IDs are not a supported complete tuple. Use the complete providers.app.profile draft/save; no defaults or substitutes were selected."); }
            // Editing another service's tuple must not change the desired default service.
            var choice = new ProviderProfileChoice(desired!.Select(tuple), revision, owner, session!, origin, remainsAdmitted!,
                tuple, field: field, baseTuple: baseTuple);
            Issue(choice);
            return choice;
        }
    }

    private void RequireDraft()
    {
        // Only completed discovery/confirmation clears held after atomically populating the complete draft.
        if (held || applying || !remainsAdmitted!())
        { throw new InvalidOperationException("Inspect provider profiles again under current original-user admission."); }
    }
    private void Issue(ProviderProfileChoice choice)
    {
        if (issued.Count >= MaximumIssuedChoices)
        { throw new InvalidOperationException("Provider draft capacity is reached. Explicitly refresh; no preview is silently evicted."); }
        issued.Add(choice);
    }

    public async Task<bool> SelectAsync(ProviderProfileChoice choice, long callRevision, RequestOrigin requestOrigin,
        SecurityAuditInitiator initiator, CallCommunicationPolicy policy, Func<bool> eligible, CancellationToken token)
    {
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || initiator == SecurityAuditInitiator.VoiceCommand && requestOrigin != RequestOrigin.ActivatedVoice)
        { throw new InvalidOperationException("Provider profile writes require original user initiation and explicit app scope."); }
        Enter();
        var expected = 0L;
        try
        {
            var accepted = await admission.RunObservedAsync(requestOrigin, eligible, catalogue.ObserveAsync, (request, authority, current) =>
            {
                lock (gate)
                {
                    if (held || !issued.Contains(choice) || choice.Owner != owner || choice.Session != authority || choice.Revision != revision
                        || choice.Origin != request.Origin || !choice.RemainsAdmitted() || !Same(Read(), saved)
                        || choice.Tuple is { } tuple && ProviderProfileRules.Resolve(tuple, current.For(tuple.Service), time.GetUtcNow()) is null)
                    { throw new InvalidOperationException("Provider preview, account, catalogue, saved state or session/generation changed. Inspect and explicitly choose again."); }
                    if (choice.Tuple?.Service == ProviderService.Local && current.Installation != LocalProviderInstallation.Installed)
                    { throw new InvalidOperationException("Local installation is missing or unknown; retain desired selections without activating a tuple."); }
                    if (choice.ResetInstallation is { } reset && reset != current.Installation)
                    { throw new InvalidOperationException("Installation observation changed since the app-default reset preview."); }
                    var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
                        choice.Field switch
                        {
                            ProviderProfileField.Model => "configuration.provider-profile.model",
                            ProviderProfileField.Reasoning => "configuration.provider-profile.reasoning",
                            ProviderProfileField.Context => "configuration.provider-profile.context",
                            _ => "configuration.provider-profile",
                        }, SecurityAuditOutcome.Requested,
                        request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator, "preferences.device-local");
                    auditLog.Write(audit);
                    var denied = policy.CommitVoiceSetting(request.Origin, callRevision,
                        () => ReferenceEquals(HostActivity.RequireCurrent().Request, request) && eligible() && !token.IsCancellationRequested
                            && choice.Revision == revision && choice.RemainsAdmitted() && Same(Read(), saved)
                            && (choice.Tuple is null || ProviderProfileRules.Resolve(choice.Tuple, current.For(choice.Tuple.Service), time.GetUtcNow()) is not null),
                        () =>
                        {
                            held = true;
                            try
                            {
                                preferences.BeginWrite();
                                preferences.Save(choice.Value);
                                if (!Same(Read(readBack: true), choice.Value)) { throw new InvalidDataException("Provider profile readback does not match the exact preview."); }
                            }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                            {
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed,
                                    exception is UnauthorizedAccessException ? "access-denied" : "invalid-or-io"));
                                throw;
                            }
                            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                            if (revision != choice.Revision) { throw new InvalidOperationException("Provider profile changed during terminal audit."); }
                            saved = desired = choice.Value;
                            observation = current;
                            revision = checked(revision + 1);
                            expected = revision;
                            choices = [];
                            issued.Clear();
                        });
                    if (denied is { } reason)
                    { auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Denied, reason.ToString().ToLowerInvariant())); return false; }
                    return true;
                }
            }, token).ConfigureAwait(false);
            if (!accepted) { return false; }
            if (!eligible() || token.IsCancellationRequested) { throw new InvalidOperationException("Provider profile completion admission changed."); }
            var completed = await catalogue.ObserveAsync(token).ConfigureAwait(false);
            lock (gate)
            {
                if (expected != revision || !eligible() || token.IsCancellationRequested || !Same(Read(readBack: true), choice.Value)
                    || choice.ResetInstallation is { } reset && reset != completed.Installation
                    || choice.Tuple?.Service == ProviderService.Local && completed.Installation != LocalProviderInstallation.Installed
                    || choice.Tuple is { } tuple && ProviderProfileRules.Resolve(tuple, completed.For(tuple.Service), time.GetUtcNow()) is null)
                { throw new InvalidOperationException("Provider profile/account/catalogue changed before confirmation."); }
                preferences.ConfirmWrite();
                try
                {
                    if (!Same(Read(), choice.Value) || expected != revision || !eligible() || token.IsCancellationRequested)
                    { throw new InvalidDataException("Provider profile confirmation is no longer exact/current."); }
                }
                catch { preferences.BeginWrite(); throw; }
                observation = completed;
                held = false;
                recovery = null;
                Changed?.Invoke(this, EventArgs.Empty);
            }
            return true;
        }
        catch { HoldUnavailable(); throw; }
        finally { Exit(); }
    }

    // The turn consumer observes again; settings snapshots are never dispatch or account authority.
    internal async Task<(ProviderProfileTuple? Tuple, ModelProviderSelection? Selection, Func<bool> Current)> ResolveTurnAsync(
        Func<bool> eligible, SessionProviderOverrides overrides, CancellationToken token,
        ProviderService? reviewedService = null, ProviderProfileTuple? localRequest = null)
    {
        if (!eligible()) { throw new InvalidOperationException("Provider profile host admission is closed."); }
        var current = await catalogue.ObserveAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (applying || !eligible() || token.IsCancellationRequested) { throw new InvalidOperationException("Provider profile admission changed during observation."); }
            var value = Read();
            var resolved = value ?? new ProviderProfilePreferences(Initial(current.Installation), []);
            if (localRequest is not null && localRequest.Service != ProviderService.Local)
            { throw new InvalidOperationException("Request-only selection is local only."); }
            var service = localRequest?.Service ?? reviewedService ?? overrides.EffectiveService(resolved);
            var tuple = localRequest ?? (reviewedService is null || overrides.EffectiveService(resolved) == service
                ? overrides.ResolveDesired(resolved) : resolved.For(service));
            var model = service == ProviderService.Unset
                || service == ProviderService.Local && current.Installation != LocalProviderInstallation.Installed
                ? null : ProviderProfileRules.Resolve(tuple, current.For(service), time.GetUtcNow());
            var expected = revision;
            bool Current() => !applying && revision == expected && eligible() && Same(Read(), value)
                && model is not null && current.For(service).IsCurrent(time.GetUtcNow());
            return (model is null ? null : tuple, model?.Selection, Current);
        }
    }

    private void Enter()
    {
        lock (gate)
        {
            if (applying) { throw new InvalidOperationException("Provider profile operation is already committing."); }
            applying = true;
        }
    }
    internal async Task<bool> ValidateTurnAsync(ProviderProfileTuple tuple, CancellationToken token)
    {
        var current = await catalogue.ObserveAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return (tuple.Service != ProviderService.Local || current.Installation == LocalProviderInstallation.Installed)
            && ProviderProfileRules.Resolve(tuple, current.For(tuple.Service), time.GetUtcNow()) is not null;
    }
    private void Exit() { lock (gate) { applying = false; } }
    public void HoldUnavailable()
    {
        lock (gate)
        {
            held = true;
            recovery = "Provider profile state/evidence is unconfirmed. Inspect saved selections and audit/control receipts before explicit repair and refresh; no fallback or retry.";
            choices = [];
            issued.Clear();
            revision = checked(revision + 1);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
