using System.Text;
using System.Text.Json;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private SessionProviderProfileConfigurationService? sessionProviderConfiguration;
    private SessionProviderProposal? sessionProviderProposal;
    public void BindSessionProviderProfiles(SessionProviderProfileConfigurationService service) => sessionProviderConfiguration = service;

    internal async Task ExecuteSessionProviderProfileCommand(SessionProviderProfileCommand command, SecurityAuditInitiator initiator)
    {
        var service = sessionProviderConfiguration;
        if (disposed) { return; }
        if (service is null) { ShowFailure("Session provider profiles unavailable.", "Select an exact existing session; no app fallback."); return; }
        if (command.Error is not null) { ShowInformation("Exact session provider syntax required.", command.Error); return; }
        var origin = initiator == SecurityAuditInitiator.VoiceCommand ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi;
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand))
        { ShowFailure("Session provider profile denied.", "Original authenticated local input is required."); return; }
        var callRevision = CallPolicyRevision;
        var recoveryRevision = Volatile.Read(ref voiceRecoveryRevision);
        bool Eligible() => !disposed && IsHostInputEligible && IsCallMutationHostEligible && CallPolicyRevision == callRevision
            && !IsResponseInteractionPending && (origin != RequestOrigin.ActivatedVoice || IsVoiceEnabled && HasVoiceConsent
                && CallObservation.AllowActivation && Volatile.Read(ref voiceRecoveryRevision) == recoveryRevision);
        SessionProviderDraft? inspected = null;
        try
        {
            if (!Eligible()) { throw new InvalidOperationException("Current input, privacy, ownership or call admission is closed."); }
            if (command.Confirmation is { } confirmation)
            {
                var proposal = sessionProviderProposal;
                if (proposal is null || proposal.ConfirmationId != confirmation) { throw new InvalidOperationException("No exact live host-issued session review matches that confirmation."); }
                var saved = await service.Confirm(proposal, origin, Eligible, CancellationToken.None);
                sessionProviderProposal = null;
                ShowSessionProviderInformation("Exact session provider policy committed.", new
                {
                    scope = "This exact session only; app preferences unchanged; no execution or authentication",
                    saved, syntax = SessionProviderProfileCommand.Syntax,
                });
                return;
            }
            if (sessionProviderProposal is { } old) { service.Discard(old.Draft); sessionProviderProposal = null; }
            var session = new HostId<SessionIdentity>(command.Session ?? throw new InvalidOperationException("Select/address an exact existing session. No app fallback."));
            var draft = await service.Inspect(session, origin, Eligible, CancellationToken.None);
            inspected = draft;
            if (command.Operation == AppearanceCommandOperation.Get)
            {
                service.Discard(draft);
                ShowSessionProviderInformation("Desired and effective exact-session provider status.", new
                {
                    draft.Saved, draft.App, draft.Effective, draft.Status,
                    catalogue = draft.Catalogue, syntax = SessionProviderProfileCommand.Syntax,
                });
                return;
            }
            if (command.Operation == AppearanceCommandOperation.Reset) { sessionProviderProposal = service.Reset(draft, command.Field); }
            else if (command.Service is { } selected) { sessionProviderProposal = service.SelectService(draft, selected); }
            else
            {
                var current = draft.Catalogue ?? throw new InvalidOperationException("Effective service is Unset; explicitly select a session service first.");
                if (!string.Equals(current.Account?.Value, command.Account, StringComparison.Ordinal) || current.Revision.Value != command.CatalogueRevision)
                { throw new InvalidOperationException("Exact current account/catalogue binding is required."); }
                if (command.Choice is { } choice)
                {
                    sessionProviderProposal = service.SelectField(draft,
                        command.Field ?? throw new InvalidOperationException("An exact model/reasoning/context field is required."), new(choice));
                }
                else
                {
                    var model = current.Models.SingleOrDefault(item => string.Equals(item.Id.Value, command.Model, StringComparison.Ordinal))
                        ?? throw new InvalidOperationException("No current advertised exact model. No substitute or auth flow.");
                    sessionProviderProposal = service.Select(draft, command.Field, model,
                        model.Reasoning.SingleOrDefault(item => string.Equals(item.Id.Value, command.Reasoning, StringComparison.Ordinal))
                            ?? throw new InvalidOperationException("No exact advertised reasoning option."),
                        model.Context.SingleOrDefault(item => string.Equals(item.Id.Value, command.Context, StringComparison.Ordinal))
                            ?? throw new InvalidOperationException("No exact advertised context option."));
                }
            }
            ShowSessionProviderInformation("Review exact session provider change; nothing saved.", new
            {
                scope = "This exact session only", session = draft.Saved.Session, revision = draft.Saved.Revision,
                previous = draft.Saved.Desired, proposed = sessionProviderProposal.Desired, draft.Effective, draft.Status,
                field = sessionProviderProposal.Field?.ToString(), sessionProviderProposal.PreviousEffective, sessionProviderProposal.ResultingEffective, sessionProviderProposal.AppRevision,
                catalogue = draft.Catalogue, confirmation = "confirm providers.session.profile " + sessionProviderProposal.ConfirmationId.ToString("D"),
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            if (sessionProviderProposal is { } old) { service.Discard(old.Draft); sessionProviderProposal = null; }
            ApplicationLog.Error(logger, exception, "Reviewing exact session provider profiles");
            if (!disposed) { ShowFailure("Session provider policy not confirmed.", exception.Message + " Inspect exact saved state and audit/control receipts; no rollback, retry or app fallback."); }
        }
        finally
        {
            if (inspected is not null && !ReferenceEquals(sessionProviderProposal?.Draft, inspected)) { service.Discard(inspected); }
        }

    }

    private void ShowSessionProviderInformation<T>(string title, T state)
    {
        var response = JsonSerializer.Serialize(state, ProviderProfileJsonOptions);
        if (Encoding.UTF8.GetByteCount(response) > 65536) { throw new InvalidDataException("Session provider disclosure exceeds its complete byte bound."); }
        ShowInformation(title, response);
    }
}
