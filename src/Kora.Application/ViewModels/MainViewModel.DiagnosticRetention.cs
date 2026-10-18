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
    private readonly DiagnosticRetentionConfigurationService? diagnosticRetentionConfiguration;
    private int selectedDiagnosticRetentionDays = DiagnosticRetentionDays.DefaultDays;
    private bool diagnosticRetentionControlActive;
    private Func<bool> diagnosticRetentionNativeLifetime = static () => false;

    public void BindDiagnosticRetentionNativeLifetime(Func<bool> lifetime)
    {
        diagnosticRetentionNativeLifetime = lifetime;
        Interlocked.Increment(ref diagnosticImpactControlRevision);
        diagnosticImpactPreview?.Retire("Native Settings lifetime changed; preview retired.");
        OnPropertyChanged(nameof(CanChangeDiagnosticRetentionNative));
        NotifyDiagnosticImpactPreview();
    }

    public IReadOnlyList<int> DiagnosticRetentionChoices { get; } =
        Enumerable.Range(DiagnosticRetentionDays.Minimum, DiagnosticRetentionDays.Maximum).ToArray();
    public int SelectedDiagnosticRetentionDays
    {
        get => selectedDiagnosticRetentionDays;
        set
        {
            var days = new DiagnosticRetentionDays(value).Days;
            if (selectedDiagnosticRetentionDays == days) { return; }
            selectedDiagnosticRetentionDays = days;
            Interlocked.Increment(ref diagnosticImpactControlRevision);
            diagnosticImpactPreview?.Retire("Future-only diagnostic draft edited; preview retired.");
            OnPropertyChanged(nameof(SelectedDiagnosticRetentionDays));
        }
    }
    public AsyncCommand RefreshDiagnosticRetentionCommand { get; }
    public AsyncCommand SaveDiagnosticRetentionCommand { get; }
    public AsyncCommand ResetDiagnosticRetentionCommand { get; }
    public bool CanChangeDiagnosticRetention => diagnosticRetentionConfiguration is not null && IsCallMutationHostEligible
        && !diagnosticRetentionControlActive && !auditRetentionControlActive && !IsResponseInteractionPending;
    public bool CanChangeDiagnosticRetentionNative => CanChangeDiagnosticRetention && diagnosticRetentionNativeLifetime();
    public string DiagnosticRetentionStatus => diagnosticRetentionConfiguration is null
        ? "SQLite diagnostic-retention admission unavailable; no preference change. Apply-now unavailable."
        : DiagnosticRetentionState.Serialize(diagnosticRetentionConfiguration.Get(), CallPolicyRevision, "observed");

    private void OnDiagnosticRetentionChanged(object? sender, EventArgs args)
    {
        diagnosticImpactPreview?.Retire("Confirmed configuration changed; preview retired.");
        uiDispatcher.Post(SynchronizeDiagnosticRetention);
    }

    private void SynchronizeDiagnosticRetention()
    {
        if (disposed) { return; }
        if (diagnosticRetentionConfiguration!.Get().Desired is { } days) { SelectedDiagnosticRetentionDays = days.Days; }
        OnPropertyChanged(nameof(DiagnosticRetentionStatus));
        OnPropertyChanged(nameof(CanChangeDiagnosticRetention));
        OnPropertyChanged(nameof(CanChangeDiagnosticRetentionNative));
        NotifyDiagnosticImpactPreview();
    }

    internal async Task ExecuteDiagnosticRetentionCommandAsync(DiagnosticRetentionCommand command,
        SecurityAuditInitiator initiator, CancellationToken cancellationToken = default)
    {
        var nativeLifetime = diagnosticRetentionNativeLifetime;
        if (!CanChangeDiagnosticRetention || initiator == SecurityAuditInitiator.LocalUser && !nativeLifetime())
        {
            if (!disposed) { Transcript = "SQLite diagnostic retention requires the owning unlocked host and no pending exact question/approval."; }
            return;
        }
        if (command.Operation == AppearanceCommandOperation.Clarify)
        {
            ShowFailure("Clarify SQLite diagnostic retention.", command.Error!);
            return;
        }
        var origin = OriginalOrigin(initiator);
        var hostEligible = CaptureConfigurationControlEligibility(origin);
        bool Eligible() => hostEligible() && (initiator != SecurityAuditInitiator.LocalUser
            || ReferenceEquals(nativeLifetime, diagnosticRetentionNativeLifetime) && nativeLifetime());
        Func<bool> eligible = Eligible;
        Interlocked.Increment(ref diagnosticImpactControlRevision);
        diagnosticImpactPreview?.Retire("Retention control request changed; preview retired.");
        diagnosticRetentionControlActive = true;
        OnPropertyChanged(nameof(CanChangeDiagnosticRetention));
        try
        {
            await diagnosticRetentionConfiguration!.RefreshAsync(origin, eligible, cancellationToken);
            if (command.Operation == AppearanceCommandOperation.List && auditRetentionConfiguration is not null)
            {
                await RefreshLoggingAuditDiscoveryAsync(origin, eligible, cancellationToken);
            }
            var saved = false;
            if (command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset)
            {
                var proposal = diagnosticRetentionConfiguration.Propose(command.Operation == AppearanceCommandOperation.Reset ? null
                    : command.Value ?? throw new InvalidOperationException("Exact integer days are required."),
                    diagnosticRetentionConfiguration.Get().Revision, CallPolicyRevision);
                saved = await diagnosticRetentionConfiguration.ApplyAsync(proposal, origin, initiator, communicationPolicy, eligible, cancellationToken);
            }
            if (!eligible()) { return; }
            var result = DiagnosticRetentionState.Serialize(diagnosticRetentionConfiguration.Get(), CallPolicyRevision,
                command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset ? saved ? "saved" : "denied"
                    : command.Operation == AppearanceCommandOperation.List && auditRetentionConfiguration?.Get().Available == false ? "unavailable" : "observed",
                command.Operation == AppearanceCommandOperation.List ? auditRetentionConfiguration?.Get() : null);
            if (IsSpeaking) { Transcript = result; }
            else { PresentResponse(AssistantState.Information, "SQLite diagnostic retention.", result, refreshOutput: false); }
        }
        catch (ArgumentOutOfRangeException exception)
        {
            PresentDiagnosticRetentionFailure("Choose exact integer diagnostic days from 1 through 365.", exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException
            or OperationCanceledException or TimeoutException)
        {
            diagnosticRetentionConfiguration!.HoldUnavailable();
            ApplicationLog.Error(logger, exception, "Configuring SQLite diagnostic retention");
            PresentDiagnosticRetentionFailure("SQLite diagnostic retention not confirmed.", exception.Message);
        }
        finally
        {
            diagnosticRetentionControlActive = false;
            SynchronizeDiagnosticRetention();
        }

    }

    private void PresentDiagnosticRetentionFailure(string title, string error)
    {
        if (disposed) { return; }
        if (IsResponseInteractionPending || IsSpeaking) { Transcript = title + " " + error; }
        else { ShowFailure(title, error); }
    }
}
