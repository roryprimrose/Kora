using Kora.Application.Auditing;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Presentation;

public sealed partial class ExactAnswerExportReview : IAsyncDisposable
{
    public const string RetentionDisclosure = "This new file leaves Kora's private retention. The selected location may copy, back up or sync it. Deleting the conversation or Kora data does not delete exported files or their copies.";
    private readonly AdmittedDetailContent source;
    private readonly IExactAnswerExportDestination destination;
    private readonly Func<bool> admitted;
    private readonly Func<CancellationToken, Task<AdmittedDetailContent>> resolve;
    private readonly ISecurityAuditLog audit;
    private readonly ILogger<ExactAnswerExportReview> logger;
    private readonly HostRequest request;
    private int consumed;
    private int disposed;

    public ExactAnswerExportReview(AdmittedDetailContent source, IExactAnswerExportDestination destination,
        Func<bool> admitted, Func<CancellationToken, Task<AdmittedDetailContent>> resolve,
        ISecurityAuditLog audit, ILogger<ExactAnswerExportReview> logger)
    {
        request = HostActivity.RequireCurrent().Request;
        if (source.Origin != DetailContentOrigin.RetainedAssistant
            || request.Origin != RequestOrigin.LocalUi || request.InvocationId is not null
            || request.SessionId != source.ConversationMessage!.Request.SessionId)
        { throw new InvalidOperationException("Export requires one exact retained ASSISTANT answer and original local-UI authority."); }
        this.source = source;
        this.destination = destination;
        this.admitted = admitted;
        this.resolve = resolve;
        this.audit = audit;
        this.logger = logger;
    }

    public AdmittedDetailContent Source => source;
    public string Destination => destination.Path;
    public string Format => Destination.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
        ? "UTF-8 plain text, no BOM; original text unchanged"
        : "UTF-8 Markdown, no BOM; original text unchanged";

    public async Task ValidateAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("The original private export presentation was retired."); }
        var current = await resolve(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!admitted() || !source.IsSameSnapshot(current))
        { throw new InvalidOperationException("The exact retained answer changed; no newer answer was substituted."); }
    }

    public async Task<ExactAnswerExportOutcome> ConfirmAsync(CancellationToken token)
    {
        if (Interlocked.Exchange(ref consumed, 1) != 0)
        { throw new InvalidOperationException("Export confirmation is single-use; uncertain writes must never be replayed."); }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Storage);
        activity.Complete(HostOperationOutcome.Failed);
        var receipt = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ResourceWrite,
            "result.exact-answer.export", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
            source.Reference.ItemId.Value.ToString("D"));
        var outcome = ExactAnswerExportOutcome.NotWritten;
        try
        {
            if (HostActivity.RequireCurrent().Request != request)
            { throw new InvalidOperationException("Export confirmation cannot transfer its original request authority."); }
            await ValidateAsync(token).ConfigureAwait(false);
            await audit.WriteAsync(receipt, token).ConfigureAwait(false);
            await ValidateAsync(token).ConfigureAwait(false);
            outcome = ExactAnswerExportOutcome.Uncertain;
            outcome = await destination.PublishAsync(source, ValidateAsync, admitted, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (outcome != ExactAnswerExportOutcome.Uncertain) { outcome = ExactAnswerExportOutcome.Cancelled; }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(exception.GetType().Name);
        }
        try
        {
            await audit.WriteAsync(receipt.WithOutcome(outcome switch
            {
                ExactAnswerExportOutcome.Saved => SecurityAuditOutcome.Succeeded,
                ExactAnswerExportOutcome.Cancelled => SecurityAuditOutcome.Cancelled,
                ExactAnswerExportOutcome.Uncertain => SecurityAuditOutcome.Unknown,
                _ => SecurityAuditOutcome.Failed,
            }), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(exception.GetType().Name);
            if (outcome == ExactAnswerExportOutcome.Saved) { outcome = ExactAnswerExportOutcome.Uncertain; }
        }
        activity.Complete(outcome switch
        {
            ExactAnswerExportOutcome.Saved => HostOperationOutcome.Completed,
            ExactAnswerExportOutcome.Cancelled => HostOperationOutcome.Cancelled,
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
