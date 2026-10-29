using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SessionRequestCommandService? localRequestCommands;
    private SessionRequestReview? localRequestReview;
    private ProviderCatalogueModel? localRequestModel;
    private ProviderProfileOption? localRequestReasoning;
    private ProviderProfileOption? localRequestContext;
    private long localRequestEpoch;

    public IReadOnlyList<ProviderCatalogueModel> LocalRequestModels =>
        providerDraft is { Observation.Installation: LocalProviderInstallation.Installed } draft
            ? draft.Observation.For(ProviderService.Local).Models : [];
    public ProviderCatalogueModel? LocalRequestModel
    {
        get => localRequestModel;
        set
        {
            CancelLocalRequestProfile();
            localRequestModel = value;
            localRequestReasoning = null;
            localRequestContext = null;
            NotifyLocalRequestProfile();
        }
    }
    public IReadOnlyList<ProviderProfileOption> LocalRequestReasonings => localRequestModel?.Reasoning ?? [];
    public IReadOnlyList<ProviderProfileOption> LocalRequestContexts => localRequestModel?.Context ?? [];
    public ProviderProfileOption? LocalRequestReasoning
    {
        get => localRequestReasoning;
        set { CancelLocalRequestProfile(); localRequestReasoning = value; NotifyLocalRequestProfile(); }
    }
    public ProviderProfileOption? LocalRequestContext
    {
        get => localRequestContext;
        set { CancelLocalRequestProfile(); localRequestContext = value; NotifyLocalRequestProfile(); }
    }
    public bool CanReviewLocalRequestProfile => CanSubmitConversation && !assistantReply && CanReviewProviders
        && localRequestModel is not null && localRequestReasoning is not null && localRequestContext is not null;
    public bool CanConfirmLocalRequestProfile => CanCompose && localRequestReview is not null;
    public string LocalRequestProfileText => !closed && access.CanInspect && localRequestReview is { } review
        ? "This request only - Local. No session or app preference write.\nConversation: "
            + (review.Target.Metadata?.Name.Value ?? "Unnamed conversation")
            + "\nExact destination: " + review.Target.Authority.SessionId.Value.ToString("D")
            + "\nGeneration: " + review.Target.Authority.Generation.Value
            + "\nComplete profile: " + review.LocalRequestProfile
            + "\nOriginal channel: " + review.Origin + "\nExpires UTC: " + review.ExpiresAt.ToString("O")
            + "\nComplete unchanged input:\n" + review.Body
            + "\nConfirm separately once, or Cancel without submission. Existing instruction-scope review still applies."
        : "Choose all three advertised LOCAL values after Refresh providers. Production catalogue/runtime remains unavailable. No default choices or fallback.";

    public Task ReviewLocalRequestProfileAsync() => RunConversationAsync(async (target, live) =>
    {
        CancelLocalRequestProfile();
        var draft = providerDraft ?? throw new InvalidOperationException("Refresh providers for this exact Active session.");
        var current = draft.Observation.For(ProviderService.Local);
        if (draft.Observation.Installation != LocalProviderInstallation.Installed
            || localRequestModel is not { } model || !current.Models.Any(item => ReferenceEquals(item, model))
            || localRequestReasoning is not { } reasoning || !model.Reasoning.Any(item => ReferenceEquals(item, reasoning))
            || localRequestContext is not { } context || !model.Context.Any(item => ReferenceEquals(item, context)))
        { throw new InvalidOperationException("Explicitly select one complete advertised local tuple. Nothing was submitted."); }
        var body = composerBody;
        var buffer = composerBufferRevision;
        var proposal = localRequestEpoch;
        var providersRevision = providerReviewRevision;
        bool SourceLive() => live() && access.CanControl && providerSurfaceOpen && localRequestEpoch == proposal
            && composerBufferRevision == buffer && providerReviewRevision == providersRevision;
        localRequestCommands ??= conversation!.CreateRequestCommands();
        var result = await localRequestCommands.ExecuteAsync(new(SessionCommandOperation.RequestReview,
            target.Authority.SessionId.Value, target.Authority.Generation.Value, target.Metadata?.Revision.Value ?? 0)
        {
            RequestBody = body,
            RequestLocalProfile = new(ProviderService.Local, current.Account!, current.Revision, model.Id, reasoning.Id, context.Id),
        }, RequestOrigin.LocalUi, SourceLive, lifetime.Token);
        if (!SourceLive()) { throw new OperationCanceledException("The original input, selection or tuple changed."); }
        localRequestReview = result.RequestReview;
        conversationStatus = "Review This request only, exact conversation, local profile and original input. Nothing submitted or saved.";
    });

    internal Func<Task> CaptureLocalRequestProfileDecision(bool confirm)
    {
        var review = localRequestReview;
        return () => confirm ? ConfirmLocalRequestProfileAsync(review) : CancelExactLocalRequestProfile(review);
    }

    private Task CancelExactLocalRequestProfile(SessionRequestReview? review)
    {
        if (ReferenceEquals(review, localRequestReview)) { CancelLocalRequestProfile(); }
        return Task.CompletedTask;
    }

    public Task ConfirmLocalRequestProfileAsync() => ConfirmLocalRequestProfileAsync(localRequestReview);

    private Task ConfirmLocalRequestProfileAsync(SessionRequestReview? expected) => RunConversationAsync(async (_, _) =>
    {
        var review = localRequestReview ?? throw new InvalidOperationException("Review this exact original request first.");
        if (!ReferenceEquals(review, expected))
        { throw new InvalidOperationException("The selected request-only review changed. Confirm its current exact input separately."); }
        localRequestReview = null;
        var result = await localRequestCommands!.ExecuteAsync(new(SessionCommandOperation.RequestConfirm,
            review.Target.Authority.SessionId.Value) { RequestReviewId = review.Id },
            RequestOrigin.LocalUi, () => !closed && access.CanInspect && access.CanControl, lifetime.Token);
        var receipt = result.RequestSubmission!;
        conversationPage = null;
        completedConversationMessage = receipt.Message;
        if (string.Equals(composerBody, review.Body, StringComparison.Ordinal))
        { composerBody = string.Empty; Interlocked.Increment(ref composerBufferRevision); }
        conversationStatus = "This request only: HOST " + receipt.Message.Outcome + " / " + receipt.Message.Reason
            + " | task " + receipt.Task.State
            + (receipt.Message.AssistantResponse is null ? ". No answer retained." : ". Genuine ASSISTANT answer retained.")
            + " Session/app preferences unchanged; subsequent requests inherit normally. No fallback or automatic retry.";
    }, submission: true);

    public void CancelLocalRequestProfile()
    {
        localRequestEpoch++;
        localRequestCommands?.Retire();
        localRequestReview = null;
        NotifyLocalRequestProfile();
    }

    private void NotifyLocalRequestProfile()
    {
        foreach (var name in new[] { nameof(LocalRequestModels), nameof(LocalRequestModel), nameof(LocalRequestReasonings),
            nameof(LocalRequestContexts), nameof(LocalRequestReasoning), nameof(LocalRequestContext),
            nameof(CanReviewLocalRequestProfile), nameof(CanConfirmLocalRequestProfile), nameof(LocalRequestProfileText) })
        { OnPropertyChanged(name); }
    }
}
