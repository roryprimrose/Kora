using System.ComponentModel;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private MarkdownDisplayConfigurationService? markdownDisplay;
    private MarkdownDisplayReview? markdownDisplayReview;
    private Func<bool> markdownDisplayNativeLifetime = static () => false;
    private bool markdownDisplayActive;
    private bool markdownDisplayAvailable;
    private bool markdownDisplayNativeAvailable;
    public AsyncCommand InspectMarkdownDisplayCommand { get; private set; } = null!;
    public AsyncCommand RenderedMarkdownDisplayCommand { get; private set; } = null!;
    public AsyncCommand SourceMarkdownDisplayCommand { get; private set; } = null!;
    public AsyncCommand ResetMarkdownDisplayCommand { get; private set; } = null!;
    public AsyncCommand ConfirmMarkdownDisplayCommand { get; private set; } = null!;
    public AsyncCommand CancelMarkdownDisplayCommand { get; private set; } = null!;
    public string MarkdownDisplayStatus => markdownDisplay?.Description ?? "Markdown display preferences are not composed; no saved default is applied.";
    public string MarkdownDisplayReviewText => markdownDisplayReview?.Description ?? "No Markdown display change is awaiting confirmation.";
    public bool CanControlMarkdownDisplay => markdownDisplay is not null && !disposed && !markdownDisplayActive
        && IsCallMutationHostEligible && CanRevealPrivatePresentation && !IsResponseInteractionPending
        && !IsGrantEditorVisible && !IsBusy && CanEnterRetainedAnswerRead;
    public bool CanControlMarkdownDisplayNative => CanControlMarkdownDisplay && markdownDisplayNativeLifetime();

    public void BindMarkdownDisplayNativeLifetime(Func<bool> lifetime)
    {
        markdownDisplayNativeLifetime = lifetime;
        CancelMarkdownDisplayReview();
        NotifyMarkdownDisplay();
    }

    private void InitializeMarkdownDisplay(MarkdownDisplayConfigurationService? service, Func<Func<Task>, AsyncCommand> create)
    {
        markdownDisplay = service;
        PropertyChanged += OnMarkdownDisplayHostChanged;
        if (service is not null) { service.Changed += OnMarkdownDisplayChanged; }
        InspectMarkdownDisplayCommand = create(() => ExecuteMarkdownDisplay(new(MarkdownDisplayCommand.OperationType.Inspect), SecurityAuditInitiator.LocalUser, null));
        RenderedMarkdownDisplayCommand = create(() => ExecuteMarkdownDisplay(new(MarkdownDisplayCommand.OperationType.Review, MarkdownDisplayMode.Rendered), SecurityAuditInitiator.LocalUser, null));
        SourceMarkdownDisplayCommand = create(() => ExecuteMarkdownDisplay(new(MarkdownDisplayCommand.OperationType.Review, MarkdownDisplayMode.Source), SecurityAuditInitiator.LocalUser, null));
        ResetMarkdownDisplayCommand = create(() => ExecuteMarkdownDisplay(new(MarkdownDisplayCommand.OperationType.Reset), SecurityAuditInitiator.LocalUser, null));
        ConfirmMarkdownDisplayCommand = create(() => ExecuteMarkdownDisplay(new(MarkdownDisplayCommand.OperationType.Confirm), SecurityAuditInitiator.LocalUser, null));
        CancelMarkdownDisplayCommand = create(() => ExecuteMarkdownDisplay(new(MarkdownDisplayCommand.OperationType.Cancel), SecurityAuditInitiator.LocalUser, null));
    }

    private void CancelMarkdownDisplayReview()
    {
        markdownDisplayReview = null;
        markdownDisplay?.Cancel();
        OnPropertyChanged(nameof(MarkdownDisplayReviewText));
    }

    private void OnMarkdownDisplayChanged(object? sender, EventArgs args) =>
        uiDispatcher.Post(() => { if (!disposed) { NotifyMarkdownDisplay(); } });

    private void NotifyMarkdownDisplay()
    {
        markdownDisplayAvailable = CanControlMarkdownDisplay;
        markdownDisplayNativeAvailable = CanControlMarkdownDisplayNative;
        OnPropertyChanged(nameof(MarkdownDisplayStatus));
        OnPropertyChanged(nameof(MarkdownDisplayReviewText));
        OnPropertyChanged(nameof(CanControlMarkdownDisplay));
        OnPropertyChanged(nameof(CanControlMarkdownDisplayNative));
    }

    private void OnMarkdownDisplayHostChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (CanControlMarkdownDisplay != markdownDisplayAvailable
            || CanControlMarkdownDisplayNative != markdownDisplayNativeAvailable)
        { NotifyMarkdownDisplay(); }
    }

    private async Task ExecuteMarkdownDisplay(MarkdownDisplayCommand command, SecurityAuditInitiator initiator,
        ManualCallInput? originalInput)
    {
        var origin = OriginalOrigin(initiator);
        var native = markdownDisplayNativeLifetime;
        if (!CanControlMarkdownDisplay || initiator is not
                (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || initiator == SecurityAuditInitiator.LocalUser && !native())
        {
            Transcript = "Markdown preference unavailable: use original input in the current owning unlocked host with no active task, dictation or pending question/approval. Nothing was answered or cancelled.";
            return;
        }
        if (command.Operation == MarkdownDisplayCommand.OperationType.Invalid)
        { Transcript = "Markdown preference input not accepted. " + MarkdownDisplayCommand.Syntax; return; }
        if (command.Operation == MarkdownDisplayCommand.OperationType.Cancel)
        {
            CancelMarkdownDisplayReview();
            Transcript = "Markdown preference review cancelled. No preference write or other interaction was cancelled.";
            return;
        }
        var service = markdownDisplay!;
        var host = CaptureConfigurationControlEligibility(origin);
        var input = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        var owner = settingsOwnershipRevision();
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var name = assistantNameConfiguration.Get().Revision;
        var admittingInput = true;
        bool Eligible() => !disposed && host()
            && (!admittingInput || input.Eligible() && input.Origin == origin && input.Revision == CallPolicyRevision
                && input.AssistantNameRevision == name && input.OwnershipBindingRevision == binding
                && input.NavigationOwnerRevision == auditImpactOwnerRevision()
                && input.InputRevision == Interlocked.Read(ref workspaceSurfaceInputRevision))
            && CanRevealPrivatePresentation && !IsResponseInteractionPending && !IsGrantEditorVisible
            && settingsOwnershipRevision() == owner && Interlocked.Read(ref settingsOwnershipBindingRevision) == binding
            && assistantNameConfiguration.Get().Revision == name
            && (initiator != SecurityAuditInitiator.LocalUser || ReferenceEquals(native, markdownDisplayNativeLifetime) && native());
        markdownDisplayActive = true;
        NotifyMarkdownDisplay();
        try
        {
            if (command.Operation == MarkdownDisplayCommand.OperationType.Inspect)
            {
                CancelMarkdownDisplayReview();
                await service.InspectAsync(origin, Eligible, CancellationToken.None);
            }
            else if (command.Operation == MarkdownDisplayCommand.OperationType.Confirm)
            {
                var candidate = markdownDisplayReview ?? throw new InvalidOperationException("Review this exact change first; Save is separate.");
                markdownDisplayReview = null;
                if (!await service.ConfirmAsync(candidate, origin, initiator, communicationPolicy, Eligible, CancellationToken.None))
                { throw new InvalidOperationException("Original call/channel policy denied Markdown preference confirmation."); }
            }
            else
            {
                var value = command.Operation == MarkdownDisplayCommand.OperationType.Reset ? MarkdownDisplayPreference.Default
                    : command.Value ?? markdownDisplayReview?.Value ?? throw new InvalidOperationException("Choose Rendered or Source first.");
                CancelMarkdownDisplayReview();
                markdownDisplayReview = await service.ReviewAsync(value, origin, CallPolicyRevision, communicationPolicy, Eligible, CancellationToken.None);
            }
            if (!Eligible()) { throw new InvalidOperationException("Markdown preference host admission changed before presentation."); }
            Transcript = markdownDisplayReview?.Description ?? service.Description;
            admittingInput = false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or InvalidDataException
            or UnauthorizedAccessException or OperationCanceledException)
        {
            CancelMarkdownDisplayReview();
            ApplicationLog.Error(logger, exception, "Controlling reviewed Markdown display preference");
            if (!disposed)
            { Transcript = "Markdown preference not confirmed. " + exception.Message + " Inspect saved state and audit before explicit recovery; no automatic retry."; }
        }
        finally { markdownDisplayActive = false; if (!disposed) { NotifyMarkdownDisplay(); } }
    }
}
