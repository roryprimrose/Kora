using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private ConversationExecutionConfigurationService? conversationExecution;
    private bool conversationExecutionControlActive;
    public IReadOnlyList<ConversationExecutionLimit> ConversationExecutionLimits { get; } = [new(1), new(2)];
    private ConversationExecutionLimit selectedConversationExecutionLimit = ConversationExecutionLimit.Default;
    public ConversationExecutionLimit SelectedConversationExecutionLimit
    {
        get => selectedConversationExecutionLimit;
        set => SetProperty(ref selectedConversationExecutionLimit, value);
    }
    public string ConversationExecutionStatus => conversationExecution is null
        ? "Ordinary-conversation execution preferences unavailable."
        : conversationExecution.Description + (conversationExecution.Review is { } review ? "\n" + review.Description : string.Empty);
    public bool CanChangeConversationExecution => conversationExecution is not null && !disposed
        && IsCallMutationHostEligible && !conversationExecutionControlActive && !IsResponseInteractionPending;
    public bool CanConfirmConversationExecution => CanChangeConversationExecution && conversationExecution!.Review is not null;
    public AsyncCommand RefreshConversationExecutionCommand { get; private set; } = null!;
    public AsyncCommand ReviewConversationExecutionCommand { get; private set; } = null!;
    public AsyncCommand ConfirmConversationExecutionCommand { get; private set; } = null!;
    public AsyncCommand CancelConversationExecutionCommand { get; private set; } = null!;

    private void InitializeConversationExecution(ConversationExecutionConfigurationService? service, Func<Func<Task>, AsyncCommand> create)
    {
        conversationExecution = service;
        if (service is not null) { service.Changed += OnConversationExecutionChanged; }
        RefreshConversationExecutionCommand = create(() => ExecuteConversationExecutionCommandAsync(new(ConversationExecutionOperation.Get), SecurityAuditInitiator.LocalUser));
        ReviewConversationExecutionCommand = create(() => ExecuteConversationExecutionCommandAsync(
            new(ConversationExecutionOperation.Review, SelectedConversationExecutionLimit), SecurityAuditInitiator.LocalUser));
        ConfirmConversationExecutionCommand = create(() => ExecuteConversationExecutionCommandAsync(new(ConversationExecutionOperation.Confirm), SecurityAuditInitiator.LocalUser));
        CancelConversationExecutionCommand = create(() => ExecuteConversationExecutionCommandAsync(new(ConversationExecutionOperation.Cancel), SecurityAuditInitiator.LocalUser));
    }

    private void OnConversationExecutionChanged(object? sender, EventArgs args) => uiDispatcher.Post(NotifyConversationExecution);

    private void NotifyConversationExecution()
    {
        OnPropertyChanged(nameof(ConversationExecutionStatus));
        OnPropertyChanged(nameof(CanChangeConversationExecution));
        OnPropertyChanged(nameof(CanConfirmConversationExecution));
    }

    internal async Task ExecuteConversationExecutionCommandAsync(ConversationExecutionCommand command, SecurityAuditInitiator initiator)
    {
        if (!CanChangeConversationExecution)
        {
            ShowFailure("Conversation execution settings unavailable.", "Use the current owning unlocked host with no pending question or approval.");
            return;
        }
        if (command.Operation == ConversationExecutionOperation.Clarify)
        { ShowFailure("Clarify the conversation execution setting.", ConversationExecutionCommand.Syntax); return; }
        var service = conversationExecution!;
        var origin = OriginalOrigin(initiator);
        if (initiator == SecurityAuditInitiator.VoiceCommand && HostActivity.Current is null) { origin = RequestOrigin.ActivatedVoice; }
        var captured = CaptureAudioControlEligibility(origin);
        bool Eligible() => !disposed && captured();
        conversationExecutionControlActive = true;
        NotifyConversationExecution();
        try
        {
            var outcome = "No setting write or dispatch requested.";
            switch (command.Operation)
            {
                case ConversationExecutionOperation.Get:
                    await service.RefreshAsync(origin, Eligible, CancellationToken.None);
                    break;
                case ConversationExecutionOperation.Review:
                    await service.ReviewAsync(command.Limit!, CallPolicyRevision, origin, Eligible, CancellationToken.None);
                    break;
                case ConversationExecutionOperation.Confirm:
                    var review = service.Review ?? throw new InvalidOperationException("Review an exact maximum before confirming.");
                    outcome = await service.ConfirmAsync(review, origin, initiator, communicationPolicy, Eligible, CancellationToken.None)
                        ? "Future conversation execution maximum saved. Existing executing work is unchanged."
                        : "Conversation execution preference denied by current call/channel policy. Nothing was saved.";
                    break;
                case ConversationExecutionOperation.Cancel:
                    service.CancelReview();
                    break;
            }
            if (!Eligible()) { throw new InvalidOperationException("Original preference admission changed before presentation."); }
            PresentResponse(AssistantState.Information, "Conversation execution settings.", outcome + "\n" + ConversationExecutionStatus, refreshOutput: false);
            forceVisualResponse = true;
            NotifyOutputPolicyChanged();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or OperationCanceledException)
        {
            ApplicationLog.Error(logger, exception, "Inspecting or saving ordinary conversation execution maximum");
            ShowFailure("Conversation execution preference not confirmed.", exception.Message);
        }
        finally
        {
            conversationExecutionControlActive = false;
            NotifyConversationExecution();
        }
    }
}
