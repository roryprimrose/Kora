using System.Globalization;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly AuditImpactPreviewService? auditImpactPreview;
    private int? proposedAuditImpactDays;
    private Func<bool>? auditImpactEligibility;
    private long auditImpactControlRevision;
    private Func<long> auditImpactOwnerRevision = static () => 0;
    public AsyncCommand PreviewAuditImpactCommand { get; }

    public int? ProposedAuditImpactDays
    {
        get => proposedAuditImpactDays;
        set
        {
            if (value is { } days) { _ = new AuditRetentionDays(days); }
            if (SetProperty(ref proposedAuditImpactDays, value))
            {
                auditImpactControlRevision = checked(auditImpactControlRevision + 1);
                auditImpactPreview?.Retire("Proposal edited; preview retired. Preview the exact selected value again.");
                NotifyAuditImpactPreview();
            }
        }
    }

    public bool CanPreviewAuditImpact => auditImpactPreview is not null && CanChangeAuditRetentionNative
        && ProposedAuditImpactDays is { } days && auditRetentionConfiguration!.Get().Effective is { } effective
        && days < effective.Days;

    public string AuditImpactPreviewStatus
    {
        get
        {
            var preview = auditImpactPreview?.Get();
            if (preview is null) { return "Security record preview unavailable. No settings or records were changed."; }
            var heading = preview.Status;
            if (preview.ProposedDays is { } days)
            {
                heading += $" Proposed storage period: {days.ToString(CultureInfo.InvariantCulture)} days.";
            }
            return heading + DescribeAuditImpact(preview.RequiredAuthority) + DescribeAuditImpact(preview.DiagnosticProjection);
        }
    }

    private static string DescribeAuditImpact(AuditImpactResult? result)
    {
        if (result is null) { return string.Empty; }
        var source = result.Source == AuditImpactSource.RequiredAuthority ? "Security records" : "Security record diagnostic copies";
        if (result.Aggregate is not { } aggregate) { return $" {source}: {result.Status}; no count available."; }
        if (aggregate.AffectedCount == 0) { return $" {source}: Complete; 0 newly due records."; }
        return $" {source}: Complete; {aggregate.AffectedCount.ToString(CultureInfo.InvariantCulture)} newly due records."
            + $" Recorded between {aggregate.OldestCommittedUtc:u} and {aggregate.NewestCommittedUtc:u}."
            + $" Proposed deletion dates: {aggregate.EarliestDueUtc:u} to {aggregate.LatestDueUtc:u} (UTC).";
    }

    private void OnAuditImpactPreviewChanged(object? sender, EventArgs args) => uiDispatcher.Post(() =>
    {
        if (!disposed) { NotifyAuditImpactPreview(); }
    });

    private void NotifyAuditImpactPreview()
    {
        OnPropertyChanged(nameof(AuditImpactPreviewStatus));
        OnPropertyChanged(nameof(CanPreviewAuditImpact));
        PreviewAuditImpactCommand.NotifyCanExecuteChanged();
    }

    private void RetireIneligibleAuditImpact()
    {
        if (auditImpactEligibility is not null && !auditImpactEligibility())
        {
            auditImpactEligibility = null;
            auditImpactPreview!.Retire("Private/owner/desktop/call state changed; preview retired.");
        }
        NotifyAuditImpactPreview();
    }

    private async Task PreviewAuditImpactAsync()
    {
        var lifetime = auditRetentionNativeLifetime;
        var host = CaptureConfigurationControlEligibility(RequestOrigin.LocalUi);
        var control = auditImpactControlRevision;
        var owner = auditImpactOwnerRevision();
        var days = ProposedAuditImpactDays!.Value;
        bool Eligible() => host() && ReferenceEquals(lifetime, auditRetentionNativeLifetime) && lifetime()
            && control == auditImpactControlRevision && ProposedAuditImpactDays == days
            && owner == auditImpactOwnerRevision();
        auditImpactEligibility = Eligible;
        try
        {
            await auditImpactPreview!.PreviewAsync(days.ToString(CultureInfo.InvariantCulture), Eligible);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidDataException or InvalidOperationException or OperationCanceledException or TimeoutException
            or ArgumentOutOfRangeException)
        {
            ApplicationLog.Error(logger, exception, "Observing audit-retention impact");
        }
        finally { if (!disposed) { NotifyAuditImpactPreview(); } }
    }
}
