using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly AuditRetentionConfigurationService? auditRetentionConfiguration;
    private int selectedAuditRetentionDays = AuditRetentionDays.DefaultDays;
    private bool auditRetentionControlActive;
    private Func<bool> auditRetentionNativeLifetime = static () => false;

    public void BindAuditRetentionNativeLifetime(Func<bool> lifetime)
    {
        auditRetentionNativeLifetime = lifetime;
        OnPropertyChanged(nameof(CanChangeAuditRetentionNative));
    }

    public IReadOnlyList<int> AuditRetentionChoices { get; } =
        Enumerable.Range(AuditRetentionDays.Minimum, AuditRetentionDays.Maximum - AuditRetentionDays.Minimum + 1).ToArray();
    public int SelectedAuditRetentionDays
    {
        get => selectedAuditRetentionDays;
        set => SetProperty(ref selectedAuditRetentionDays, new AuditRetentionDays(value).Days);
    }
    public AsyncCommand RefreshAuditRetentionCommand { get; }
    public AsyncCommand SaveAuditRetentionCommand { get; }
    public AsyncCommand ResetAuditRetentionCommand { get; }
    public bool CanChangeAuditRetention => auditRetentionConfiguration is not null && IsCallMutationHostEligible
        && !auditRetentionControlActive && !diagnosticRetentionControlActive && !IsResponseInteractionPending;
    public bool CanChangeAuditRetentionNative => CanChangeAuditRetention && auditRetentionNativeLifetime();
    public string AuditRetentionStatus => auditRetentionConfiguration is null
        ? "Audit-retention admission unavailable; no preference change. Apply-now unavailable."
        : AuditRetentionState.Serialize(auditRetentionConfiguration.Get(), CallPolicyRevision, "observed");

    private void OnAuditRetentionChanged(object? sender, EventArgs args) => uiDispatcher.Post(SynchronizeAuditRetention);

    private void SynchronizeAuditRetention()
    {
        if (disposed) { return; }
        SelectedAuditRetentionDays = auditRetentionConfiguration!.Get().Desired.Days;
        OnPropertyChanged(nameof(AuditRetentionStatus));
        OnPropertyChanged(nameof(CanChangeAuditRetention));
        OnPropertyChanged(nameof(CanChangeAuditRetentionNative));
        OnPropertyChanged(nameof(CanChangeDiagnosticRetention));
        OnPropertyChanged(nameof(CanChangeDiagnosticRetentionNative));
    }

    internal async Task ExecuteAuditRetentionCommandAsync(AuditRetentionCommand command,
        SecurityAuditInitiator initiator, CancellationToken cancellationToken = default)
    {
        var nativeLifetime = auditRetentionNativeLifetime;
        if (!CanChangeAuditRetention || initiator == SecurityAuditInitiator.LocalUser && !nativeLifetime())
        {
            if (!disposed) { Transcript = "Audit retention requires the owning unlocked host, available admission and no pending exact question/approval."; }
            return;
        }
        if (command.Operation == AppearanceCommandOperation.Clarify)
        {
            ShowFailure("Clarify audit retention.", command.Error!);
            return;
        }
        var origin = OriginalOrigin(initiator);
        var hostEligible = CaptureConfigurationControlEligibility(origin);
        var nameRevision = assistantNameConfiguration.Get().Revision;
        var generation = voiceRecognition.Generation;
        bool Eligible() => hostEligible() && (initiator != SecurityAuditInitiator.LocalUser
            || ReferenceEquals(nativeLifetime, auditRetentionNativeLifetime) && nativeLifetime())
            && assistantNameConfiguration.Get().Revision == nameRevision
            && (origin != RequestOrigin.ActivatedVoice || voiceRecognition.Generation == generation);
        Func<bool> eligible = Eligible;
        auditRetentionControlActive = true;
        OnPropertyChanged(nameof(CanChangeAuditRetention));
        try
        {
            await auditRetentionConfiguration!.RefreshAsync(origin, eligible, cancellationToken);
            var saved = false;
            if (command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset)
            {
                var proposal = auditRetentionConfiguration.Propose(command.Operation == AppearanceCommandOperation.Reset ? null
                    : command.Value ?? throw new InvalidOperationException("Exact integer days are required."),
                    auditRetentionConfiguration.Get().Revision, CallPolicyRevision);
                saved = await auditRetentionConfiguration.ApplyAsync(proposal, origin, initiator, communicationPolicy, eligible, cancellationToken);
            }
            if (!eligible()) { return; }
            var result = AuditRetentionState.Serialize(auditRetentionConfiguration.Get(), CallPolicyRevision,
                command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset ? saved ? "saved" : "denied" : "observed");
            if (IsSpeaking) { Transcript = result; }
            else { PresentResponse(AssistantState.Information, "Future-only audit retention.", result, refreshOutput: false); }
        }
        catch (ArgumentOutOfRangeException exception)
        {
            PresentAuditRetentionFailure("Choose exact integer audit days from 30 through 365.", exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException
            or OperationCanceledException or TimeoutException)
        {
            auditRetentionConfiguration!.HoldUnavailable();
            ApplicationLog.Error(logger, exception, "Configuring future-only audit retention");
            PresentAuditRetentionFailure("Audit retention not confirmed.", exception.Message);
        }
        finally
        {
            auditRetentionControlActive = false;
            SynchronizeAuditRetention();
        }
    }

    private void PresentAuditRetentionFailure(string title, string error)
    {
        if (disposed) { return; }
        if (IsResponseInteractionPending || IsSpeaking) { Transcript = title + " " + error; }
        else { ShowFailure(title, error); }
    }

    private async Task RefreshLoggingAuditDiscoveryAsync(RequestOrigin origin, Func<bool> eligible, CancellationToken token)
    {
        try { await auditRetentionConfiguration!.RefreshAsync(origin, eligible, token); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            auditRetentionConfiguration!.HoldUnavailable();
            ApplicationLog.Error(logger, exception, "Discovering independent audit-retention availability");
        }
    }
}
