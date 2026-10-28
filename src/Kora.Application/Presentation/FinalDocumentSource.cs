using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.Presentation;

public sealed class FinalDocumentSource
{
    private readonly DetailViewerState owner;

    internal FinalDocumentSource(DetailViewerState owner, AdmittedDetailContent content, Guid renderToken)
    {
        this.owner = owner;
        Reference = content.Reference;
        OriginalSource = content.SessionSource ?? new(content.ConversationMessage!.Request.SessionId,
            content.ConversationMessage.Request.RequestId, content.ConversationMessage.Request.TaskId);
        SourceGeneration = content.ConversationMessage?.Generation ?? new(content.Reference.Revision);
        RenderGeneration = owner.Generation;
        RenderToken = renderToken;
        Origin = content.Origin;
    }

    public DetailContentReference Reference { get; }
    public DetailSessionSource OriginalSource { get; }
    public HostRevision SourceGeneration { get; }
    public long RenderGeneration { get; }
    public Guid RenderToken { get; }
    public DetailContentOrigin Origin { get; }
    public bool IsCurrent => owner.IsFinalDocumentCurrent(this);
    public Task<bool> ValidateAsync(CancellationToken token) => owner.ValidateFinalDocumentAsync(this, token);

    public FinalDocumentNavigationRequest CaptureNavigation(int sourceStart, int sourceLength, string destination)
    {
        if (owner.RequiresSourceValidation)
        { throw new InvalidOperationException("The retained final source requires fresh asynchronous validation before review."); }
        return CaptureNavigationCore(sourceStart, sourceLength, destination);
    }

    public async Task<FinalDocumentNavigationRequest> CaptureNavigationAsync(
        int sourceStart, int sourceLength, string destination, CancellationToken token)
    {
        var action = RequireNativeAction();
        if (!await ValidateAsync(token))
        { throw new InvalidOperationException("The exact retained final source is no longer available for navigation review."); }
        return CaptureNavigationCore(sourceStart, sourceLength, destination, action);
    }

    private static HostRequest RequireNativeAction()
    {
        var host = HostActivity.RequireCurrent();
        if (host.Activity!.IsStopped || host.Outcome != HostOperationOutcome.Unknown
            || host.Request.Origin != RequestOrigin.LocalUi)
        { throw new InvalidOperationException("A fresh original native user action is required to review a final-document destination."); }
        return host.Request;
    }

    private FinalDocumentNavigationRequest CaptureNavigationCore(
        int sourceStart, int sourceLength, string destination, HostRequest? originalAction = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var action = RequireNativeAction();
        var content = owner.Content;
        if (content is null || !IsCurrent || sourceStart < 0 || sourceLength <= 0
            || sourceStart > content.Source.Length - sourceLength)
        { throw new InvalidOperationException("The exact final-document link token or source is no longer available."); }
        return new(this, originalAction ?? action, sourceStart, content.Title, content.Source.Substring(sourceStart, sourceLength), destination);
    }
}
