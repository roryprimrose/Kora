using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using Kora.Application.Hosting.McpResultFollowUp;
using Kora.Application.Mcp;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private HostOperationProposal? mcpCompletedProposal;
    private McpReview? mcpCompletedMapping;
    private HostRevision mcpCompletedGeneration;
    private ActivityLink mcpCompletedCause;
    private McpResultObservation? mcpObservation;
    private McpResultFollowUpWorkflow? mcpFollowUpWorkflow;
    private McpResultFollowUpReview? mcpFollowUpReview;
    private long mcpObservationSelection;
    private long mcpObservationEpoch;
    private long mcpQuestionRevision;
    private bool observingMcpFollowUp;
    private bool retiringMcpFollowUp;
    private string mcpFollowUpQuestion = string.Empty;
    private string mcpFollowUpOutcome = string.Empty;
    internal IMcpResultSourceUse McpResultSourceUse { get; set; } = UnavailableMcpResultSourceUse.Instance;
    internal Task McpFollowUpRetirement { get; private set; } = Task.CompletedTask;

    private bool McpObservationCurrent => mcpObservation is { } source && !closed && access.CanInspect && access.CanControl
        && source.Control == access.ControlRevision && selected == source.Target
        && selectionEpoch == mcpObservationEpoch && mcpSelectionRevision == mcpObservationSelection
        && HasCurrentMcpOptions && ReferenceEquals(mcpResult, source.Result) && mcpResultRequest == source.Request
        && string.Equals(mcpServer?.Id, source.ServerId, StringComparison.Ordinal) && mcpAccount?.Reference == source.Mapping.Account;
    public bool CanEditMcpResultFollowUp => CanCompose && McpObservationCurrent && !IsMcpActive
        && mcpFollowUpWorkflow?.IsRunning != true
        && mcpFollowUpWorkflow?.Held != true;
    public bool CanReviewMcpResultFollowUp => CanEditMcpResultFollowUp && !string.IsNullOrWhiteSpace(mcpFollowUpQuestion);
    public bool CanConfirmMcpResultFollowUp => CanReviewMcpResultFollowUp && mcpFollowUpReview?.CanConfirm == true;
    public string McpFollowUpReviewText => McpObservationCurrent ? mcpFollowUpReview?.Text ?? string.Empty : string.Empty;
    public string McpFollowUpOutcome => McpObservationCurrent ? mcpFollowUpOutcome : string.Empty;
    public string McpFollowUpQuestion
    {
        get => McpObservationCurrent ? mcpFollowUpQuestion : string.Empty;
        set
        {
            if (!CanEditMcpResultFollowUp || string.Equals(value, mcpFollowUpQuestion, StringComparison.Ordinal)) { return; }
            mcpFollowUpReview?.Dispose();
            mcpFollowUpReview = null;
            mcpFollowUpQuestion = value;
            mcpQuestionRevision++;
            mcpFollowUpOutcome = string.Empty;
            NotifyMcpFollowUp();
        }
    }

    private async Task<McpApprovalPresentation> PresentMcpAndCaptureMappingAsync(HostOperationProposal proposal,
        HostRevision generation, McpReview review, Func<bool> admitted, CancellationToken token)
    {
        var cause = new ActivityLink(HostActivity.RequireCurrent().Activity!.Context);
        var presentation = await (mcpPresenter ?? throw new InvalidOperationException("The native MCP presenter was retired."))
            (proposal, generation, review, admitted, token);
        if (review.Stage == McpReviewStage.Invocation && review.IsBoundTo(proposal) && admitted()
            && presentation.Completed.Outcome == Kora.Core.Interaction.HostInteractionOutcome.Approved)
        {
            mcpCompletedProposal = proposal;
            mcpCompletedMapping = review;
            mcpCompletedGeneration = generation;
            mcpCompletedCause = cause;
        }
        return presentation;
    }

    private void CaptureMcpResultFollowUp()
    {
        if (mcpResult is not { } result || mcpResultRequest is not { } request || selected is not { } target
            || mcpCompletedProposal is not { } proposal || mcpCompletedMapping is not { } mapping
            || mcpServer is not { } server || mcpAccount?.Reference != mapping.Account)
        { return; }
        mcpObservation = new(target, request, result, proposal, mapping, mcpCompletedGeneration,
            mcpResultControl, server.Id, server.Name, mcpCompletedCause);
        mcpObservationSelection = mcpSelectionRevision;
        mcpObservationEpoch = selectionEpoch;
        if (!observingMcpFollowUp)
        {
            observingMcpFollowUp = true;
            PropertyChanged += ObserveMcpFollowUp;
        }
        NotifyMcpFollowUp();
    }

    private void ObserveMcpFollowUp(object? sender, PropertyChangedEventArgs args)
    {
        if (retiringMcpFollowUp) { return; }
        if (mcpObservation is not null && !McpObservationCurrent) { RetireMcpResultFollowUp(); }
        else if (args.PropertyName is nameof(McpResultText) or nameof(IsMcpActive) or nameof(CanCompose))
        { NotifyMcpFollowUp(); }
    }

    public Task ReviewMcpResultFollowUpAsync() => RunConversationAsync(async (_, live) =>
    {
        var source = mcpObservation ?? throw new InvalidOperationException("No successful currently presented MCP observation is available.");
        var revision = mcpQuestionRevision;
        bool Current() => live() && McpObservationCurrent && ReferenceEquals(mcpObservation, source) && revision == mcpQuestionRevision;
        mcpFollowUpWorkflow ??= conversation!.CreateMcpResultFollowUpWorkflow(McpResultSourceUse);
        mcpFollowUpReview = await mcpFollowUpWorkflow.ReviewAsync(source, mcpFollowUpQuestion, Current, lifetime.Token);
        mcpFollowUpOutcome = mcpFollowUpReview.Decision.Kind switch
        {
            McpResultSourceUseKind.QualifiedLocalOnly => "Review ALL source data, provenance, question, target and retention. Separately consent to ONE local-only tool-free answer.",
            McpResultSourceUseKind.Denied => "Denied: independent source-use policy does not admit this observation or its original channel. Nothing submitted.",
            _ => "Unavailable: independent source-use classification and qualification are not enabled. Nothing submitted; no model, refresh or fallback.",
        };
        NotifyMcpFollowUp();
    });

    public Task ConfirmMcpResultFollowUpAsync() => RunConversationAsync(async (_, live) =>
    {
        var review = mcpFollowUpReview ?? throw new InvalidOperationException("Review the complete unchanged observation and new question first.");
        var revision = mcpQuestionRevision;
        bool Current() => live() && McpObservationCurrent && revision == mcpQuestionRevision;
        try
        {
            var receipt = await (mcpFollowUpWorkflow ?? throw new InvalidOperationException("The observation workflow is unavailable."))
                .ConfirmAsync(review, Current, lifetime.Token);
            if (!Current()) { throw new OperationCanceledException("The exact observation's private presentation changed."); }
            completedConversationMessage = receipt.Message;
            conversationPage = null;
            mcpFollowUpOutcome = receipt.Message.AssistantResponse is { } answer ? "ASSISTANT\n" + answer.Body
                : "HOST " + receipt.Message.Outcome + "/" + receipt.Message.Reason + "\nNo assistant answer was retained.";
            conversationStatus = "MCP observation follow-up retained in the exact Active conversation. "
                + "No MCP exchange, source refresh, hosted fallback, tool, source copy, memory write or export.";
        }
        catch
        {
            mcpFollowUpOutcome = "Unconfirmed or rejected: no successful answer is inferred. Inspect the exact conversation/task receipts without retry or rollback claims.";
            throw;
        }
        finally
        {
            review.Dispose();
            mcpFollowUpReview = null;
            NotifyMcpFollowUp();
        }
    }, submission: true);

    public async Task CancelMcpResultFollowUpAsync()
    {
        mcpFollowUpReview?.Dispose();
        mcpFollowUpReview = null;
        mcpFollowUpQuestion = string.Empty;
        mcpQuestionRevision++;
        mcpFollowUpOutcome = "Cancelled review/use; no late answer is admitted. Retained work is not rolled back; resources remain owned until physically joined.";
        NotifyMcpFollowUp();
        try
        {
            if (mcpFollowUpWorkflow is { } workflow) { await workflow.CancelAsync(); }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            conversationHeld |= mcpFollowUpWorkflow?.Held == true;
            mcpFollowUpOutcome = "Unconfirmed: inspect exact conversation/task receipts without retry. Cancellation does not roll back processing or clear a hold.";
            NotifyMcpFollowUp();
        }
    }

    private void RetireMcpResultFollowUp()
    {
        if (retiringMcpFollowUp) { return; }
        retiringMcpFollowUp = true;
        try
        {
            mcpObservation = null;
            McpFollowUpRetirement = CancelMcpResultFollowUpAsync();
            mcpFollowUpOutcome = string.Empty;
        }
        finally { retiringMcpFollowUp = false; }
    }

    internal Task CloseMcpResultFollowUpAsync()
    {
        RetireMcpResultFollowUp();
#pragma warning disable VSTHRD003 // Return this owner's exact retained cancellation/join, not an unrelated task.
        return McpFollowUpRetirement;
#pragma warning restore VSTHRD003
    }

    private void NotifyMcpFollowUp()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(NotifyMcpFollowUp);
            return;
        }
        foreach (var name in new[] { nameof(McpFollowUpQuestion), nameof(McpFollowUpReviewText), nameof(McpFollowUpOutcome),
            nameof(CanEditMcpResultFollowUp), nameof(CanReviewMcpResultFollowUp), nameof(CanConfirmMcpResultFollowUp) })
        { OnPropertyChanged(name); }
    }
}
