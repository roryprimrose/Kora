using System.ComponentModel;
using System.Globalization;
using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly DiagnosticImpactPreviewService? diagnosticImpactPreview;
    private int? proposedDiagnosticImpactDays;
    private long diagnosticImpactControlRevision;
    private Func<bool>? diagnosticImpactEligibility;
    public AsyncCommand PreviewDiagnosticImpactCommand { get; }
    public AsyncCommand ClearDiagnosticImpactCommand { get; }
    public AsyncCommand CancelDiagnosticImpactCommand { get; }

    public int? ProposedDiagnosticImpactDays
    {
        get => proposedDiagnosticImpactDays;
        set
        {
            if (value is { } days) { _ = new DiagnosticRetentionDays(days); }
            if (proposedDiagnosticImpactDays == value) { return; }
            proposedDiagnosticImpactDays = value;
            Interlocked.Increment(ref diagnosticImpactControlRevision);
            diagnosticImpactPreview?.Retire("Proposal edited; preview retired. Explicitly Preview the exact selection.");
            OnPropertyChanged(nameof(ProposedDiagnosticImpactDays));
            NotifyDiagnosticImpactPreview();
        }
    }

    public bool CanPreviewDiagnosticImpact => diagnosticImpactPreview is not null && CanChangeDiagnosticRetentionNative
        && !diagnosticImpactPreview.Get().Scanning && ProposedDiagnosticImpactDays is { } days
        && diagnosticRetentionConfiguration!.Get().Effective is { } effective && days < effective.Days;
    public bool CanCancelDiagnosticImpact => !disposed && diagnosticImpactPreview?.Get().Scanning == true;
    public bool CanClearDiagnosticImpact => !disposed && diagnosticImpactPreview is not null;

    public string DiagnosticImpactPreviewStatus
    {
        get
        {
            var preview = diagnosticImpactPreview?.Get();
            if (preview is null) { return "Diagnostic record preview unavailable. No settings or records were changed."; }
            var status = preview.Status;
            if (preview.ProposedDays is { } days)
            {
                status += $" Proposed storage period: {days.ToString(CultureInfo.InvariantCulture)} days. Preview time: {preview.CutoffUtc:u} (UTC).";
            }
            if (preview.Result is { } result)
            {
                status += DescribeDiagnosticImpact(result.Logs) + DescribeDiagnosticImpact(result.Spans)
                    + DescribeDiagnosticImpact(result.Links);
            }
            return status + " Preview only; excludes security records and sessions. Nothing is deleted. This does not guarantee complete conversations or permanent removal from storage.";
        }
    }

    internal static string DescribeDiagnosticImpact(DiagnosticImpactResult result)
    {
        var source = result.Source switch
        {
            DiagnosticImpactSource.OrdinaryLogs => "Diagnostic records",
            DiagnosticImpactSource.Spans => "Activity records",
            DiagnosticImpactSource.OwnedLinks => "Activity connections",
            _ => throw new InvalidDataException("Unknown diagnostic impact source."),
        };
        if (result.Aggregate is not { } aggregate) { return $" {source}: {result.Status}; no count available."; }
        if (aggregate.NewlyDueCount == 0) { return $" {source}: Complete; 0 newly due records."; }
        return $" {source}: Complete; {aggregate.NewlyDueCount.ToString(CultureInfo.InvariantCulture)} newly due records."
            + $" Recorded between {aggregate.OldestCommittedUtc:u} and {aggregate.NewestCommittedUtc:u}."
            + $" Proposed deletion dates: {aggregate.EarliestProposedDueUtc:u} to {aggregate.LatestProposedDueUtc:u} (UTC).";
    }

    private void OnDiagnosticImpactPreviewChanged(object? sender, EventArgs args) => uiDispatcher.Post(() =>
    {
        if (!disposed) { NotifyDiagnosticImpactPreview(); }
    });

    private void OnDiagnosticImpactHostPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!string.Equals(args.PropertyName, nameof(IsResponseInteractionPending), StringComparison.Ordinal)) { return; }
        Interlocked.Increment(ref diagnosticImpactControlRevision);
        diagnosticImpactPreview?.Retire("Foreground interaction control changed; preview retired.");
    }

    private void NotifyDiagnosticImpactPreview()
    {
        OnPropertyChanged(nameof(DiagnosticImpactPreviewStatus));
        OnPropertyChanged(nameof(CanPreviewDiagnosticImpact));
        OnPropertyChanged(nameof(CanCancelDiagnosticImpact));
        OnPropertyChanged(nameof(CanClearDiagnosticImpact));
        PreviewDiagnosticImpactCommand?.NotifyCanExecuteChanged();
        CancelDiagnosticImpactCommand?.NotifyCanExecuteChanged();
        ClearDiagnosticImpactCommand?.NotifyCanExecuteChanged();
    }

    private void RetireIneligibleDiagnosticImpact()
    {
        if (diagnosticImpactEligibility is not null && !diagnosticImpactEligibility())
        {
            diagnosticImpactEligibility = null;
            diagnosticImpactPreview!.Retire("Host or control state changed; preview retired.");
        }
        NotifyDiagnosticImpactPreview();
    }

    private async Task PreviewDiagnosticImpactAsync()
    {
        var native = diagnosticRetentionNativeLifetime;
        var host = CaptureConfigurationControlEligibility(RequestOrigin.LocalUi);
        var control = Interlocked.Read(ref diagnosticImpactControlRevision);
        var owner = auditImpactOwnerRevision();
        var days = ProposedDiagnosticImpactDays!.Value;
        bool Eligible() => host() && ReferenceEquals(native, diagnosticRetentionNativeLifetime) && native()
            && control == Interlocked.Read(ref diagnosticImpactControlRevision) && ProposedDiagnosticImpactDays == days
            && owner == auditImpactOwnerRevision() && CanChangeDiagnosticRetentionNative;
        diagnosticImpactEligibility = Eligible;
        try { await diagnosticImpactPreview!.PreviewAsync(days.ToString(CultureInfo.InvariantCulture), Eligible); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or InvalidOperationException or OperationCanceledException or TimeoutException or ArgumentOutOfRangeException)
        {
            // The service exposes its content-free failure/retirement status. Logging here would write to the observed store.
            NotifyDiagnosticImpactPreview();
        }
        finally { if (!disposed) { NotifyDiagnosticImpactPreview(); } }
    }

    private void FailDiagnosticImpactPreview(Exception _) =>
        diagnosticImpactPreview!.Retire("Preview unavailable or failed; no result. Nothing saved or removed.");

    private Task ClearDiagnosticImpactAsync()
    {
        Interlocked.Increment(ref diagnosticImpactControlRevision);
        diagnosticImpactEligibility = null;
        ProposedDiagnosticImpactDays = null;
        diagnosticImpactPreview!.Retire("Preview cleared; nothing saved or removed.");
        return Task.CompletedTask;
    }

    private Task CancelDiagnosticImpactAsync()
    {
        Interlocked.Increment(ref diagnosticImpactControlRevision);
        diagnosticImpactEligibility = null;
        diagnosticImpactPreview!.Retire("Preview cancelled; no result. Nothing saved or removed.");
        return Task.CompletedTask;
    }
}
