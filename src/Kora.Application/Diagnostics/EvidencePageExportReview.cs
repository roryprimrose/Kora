using Kora.Application.Auditing;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Diagnostics;

public sealed partial class EvidencePageExportReview : IAsyncDisposable
{
    public const string RetentionDisclosure = "This new file leaves Kora's private retention. Its location may copy, back up or sync it. Deleting Kora evidence or conversations does not delete exported files or their copies.";
    private readonly EvidencePageExportSource source;
    private readonly IEvidencePageExportDestination destination;
    private readonly Func<bool> admitted;
    private readonly ISecurityAuditLog audit;
    private readonly ILogger<EvidencePageExportReview> logger;
    private readonly HostRequest request;
    private int consumed;
    private int disposed;

    public EvidencePageExportReview(EvidencePageExportSource source, IEvidencePageExportDestination destination,
        Func<bool> admitted, ISecurityAuditLog audit, ILogger<EvidencePageExportReview> logger)
    {
        request = HostActivity.RequireCurrent().Request;
        if (request.Origin != RequestOrigin.LocalUi || request.InvocationId is not null)
        { throw new InvalidOperationException("Evidence export requires a fresh original native request, not diagnostic or model authority."); }
        this.source = source;
        this.destination = destination;
        this.admitted = admitted;
        this.audit = audit;
        this.logger = logger;
    }

    public EvidencePageExportSnapshot Snapshot => source.Snapshot;
    public string Destination => destination.Path;

    public async Task ValidateAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("Evidence page/private presentation retired."); }
        await source.ValidateAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("Evidence page/private presentation retired."); }
    }

    public async Task<EvidencePageExportOutcome> ConfirmAsync(CancellationToken token)
    {
        if (Interlocked.Exchange(ref consumed, 1) != 0)
        { throw new InvalidOperationException("Evidence export confirmation is single-use, including uncertain effects."); }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Storage);
        activity.Complete(HostOperationOutcome.Failed);
        var receipt = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ResourceWrite,
            "evidence.export", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
            request.RequestId.Value.ToString("D"));
        var outcome = EvidencePageExportOutcome.NotWritten;
        try
        {
            if (HostActivity.RequireCurrent().Request != request)
            { throw new InvalidOperationException("Evidence export cannot transfer its original native request authority."); }
            await ValidateAsync(token).ConfigureAwait(false);
            await audit.WriteAsync(receipt, token).ConfigureAwait(false);
            await ValidateAsync(token).ConfigureAwait(false);
            outcome = EvidencePageExportOutcome.Uncertain;
            outcome = await destination.PublishAsync(Snapshot, ValidateAsync, admitted, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (outcome != EvidencePageExportOutcome.Uncertain) { outcome = EvidencePageExportOutcome.Cancelled; }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { Failure(exception.GetType().Name); }
        try
        {
            await audit.WriteAsync(receipt.WithOutcome(outcome switch
            {
                EvidencePageExportOutcome.Saved => SecurityAuditOutcome.Succeeded,
                EvidencePageExportOutcome.Cancelled => SecurityAuditOutcome.Cancelled,
                EvidencePageExportOutcome.Uncertain => SecurityAuditOutcome.Unknown,
                _ => SecurityAuditOutcome.Failed,
            }), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(exception.GetType().Name);
            if (outcome == EvidencePageExportOutcome.Saved) { outcome = EvidencePageExportOutcome.Uncertain; }
        }
        activity.Complete(outcome switch
        {
            EvidencePageExportOutcome.Saved => HostOperationOutcome.Completed,
            EvidencePageExportOutcome.Cancelled => HostOperationOutcome.Cancelled,
            _ => HostOperationOutcome.Failed,
        });
        return outcome;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) { return; }
        Interlocked.Exchange(ref consumed, 1);
        await destination.DisposeAsync().ConfigureAwait(false);
    }
}
