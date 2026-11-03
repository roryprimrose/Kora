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
    private DetailPresentationConfigurationService? detailPresentation;
    private DetailPresentationReview? detailPresentationReview;
    private Func<bool> detailPresentationNativeLifetime = static () => false;
    private bool detailPresentationActive;
    private bool detailPresentationAvailable;
    private bool detailPresentationNativeAvailable;
    public AsyncCommand InspectDetailPresentationCommand { get; private set; } = null!;
    public AsyncCommand OfferDetailPresentationCommand { get; private set; } = null!;
    public AsyncCommand LinkOnlyDetailPresentationCommand { get; private set; } = null!;
    public AsyncCommand ResetDetailPresentationCommand { get; private set; } = null!;
    public AsyncCommand ConfirmDetailPresentationCommand { get; private set; } = null!;
    public AsyncCommand CancelDetailPresentationCommand { get; private set; } = null!;
    public string DetailPresentationStatus => detailPresentation?.Description ?? "Detail preference controls are not composed.";
    public string DetailPresentationReviewText => detailPresentationReview?.Description ?? "No detail preference change is awaiting confirmation.";
    public bool CanControlDetailPresentation => detailPresentation is not null && !disposed && !detailPresentationActive
        && IsCallMutationHostEligible && CanRevealPrivatePresentation && !IsResponseInteractionPending
        && !IsGrantEditorVisible && !IsBusy && CanEnterRetainedAnswerRead;
    public bool CanControlDetailPresentationNative => CanControlDetailPresentation && detailPresentationNativeLifetime();

    public void BindDetailPresentationNativeLifetime(Func<bool> lifetime)
    {
        detailPresentationNativeLifetime = lifetime;
        CancelDetailPresentationReview();
        NotifyDetailPresentation();
    }

    private void InitializeDetailPresentation(DetailPresentationConfigurationService? service, Func<Func<Task>, AsyncCommand> create)
    {
        detailPresentation = service;
        PropertyChanged += OnDetailPresentationHostChanged;
        if (service is not null)
        {
            try { service.Observe(); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            { ApplicationLog.Error(logger, exception, "Reading detail presentation preference"); }
            service.Changed += OnDetailPresentationChanged;
        }
        InspectDetailPresentationCommand = create(() => NativeDetailPresentation(new(DetailPresentationOperation.Inspect)));
        OfferDetailPresentationCommand = create(() => NativeDetailPresentation(new(DetailPresentationOperation.Review, DetailPresentationMode.Offer)));
        LinkOnlyDetailPresentationCommand = create(() => NativeDetailPresentation(new(DetailPresentationOperation.Review, DetailPresentationMode.LinkOnly)));
        ResetDetailPresentationCommand = create(() => NativeDetailPresentation(new(DetailPresentationOperation.Reset)));
        ConfirmDetailPresentationCommand = create(() => NativeDetailPresentation(new(DetailPresentationOperation.Confirm)));
        CancelDetailPresentationCommand = create(() => NativeDetailPresentation(new(DetailPresentationOperation.Cancel)));
    }

    private Task NativeDetailPresentation(DetailPresentationCommand command) =>
        ExecuteDetailPresentation(command, SecurityAuditInitiator.LocalUser, null);

    private void CancelDetailPresentationReview()
    {
        detailPresentationReview = null;
        detailPresentation?.Cancel();
        OnPropertyChanged(nameof(DetailPresentationReviewText));
    }

    private void OnDetailPresentationChanged(object? sender, EventArgs args)
    {
        uiDispatcher.Post(() =>
        {
            if (disposed) { return; }
            if (Volatile.Read(ref ordinaryResponse) is { ModelAnswer: { } answer }
                && detailPresentation?.IsOfferCurrent(answer.OfferEpoch) == false)
            { DeclineCurrentDetailsOffer(); }
            NotifyDetailPresentation();
            OnPropertyChanged(nameof(IsDetailsOfferPending));
        });
    }

    private void NotifyDetailPresentation()
    {
        OnPropertyChanged(nameof(DetailPresentationStatus));
        OnPropertyChanged(nameof(DetailPresentationReviewText));
        NotifyDetailPresentationAvailability();
    }

    public void NotifyResponseReadDecisionAvailability() => NotifyDetailPresentationAvailability();

    private void OnDetailPresentationHostChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (CanControlDetailPresentation != detailPresentationAvailable
            || CanControlDetailPresentationNative != detailPresentationNativeAvailable)
        { NotifyDetailPresentationAvailability(); }
    }

    private void OnDetailPresentationDecisionChanged(object? sender, EventArgs args) =>
        uiDispatcher.Post(() =>
        {
            if (!disposed) { NotifyDetailPresentationAvailability(); }
        });

    private void NotifyDetailPresentationAvailability()
    {
        detailPresentationAvailable = CanControlDetailPresentation;
        detailPresentationNativeAvailable = CanControlDetailPresentationNative;
        OnPropertyChanged(nameof(CanControlDetailPresentation));
        OnPropertyChanged(nameof(CanControlDetailPresentationNative));
    }

    private async Task ExecuteDetailPresentation(DetailPresentationCommand command, SecurityAuditInitiator initiator,
        ManualCallInput? originalInput)
    {
        var origin = OriginalOrigin(initiator);
        var native = detailPresentationNativeLifetime;
        if (!CanControlDetailPresentation || initiator is not
                (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || initiator == SecurityAuditInitiator.LocalUser && !native())
        {
            Transcript = "Detail preference unavailable: use original input in the current owning unlocked host with no active task, dictation or pending question/approval. Nothing was answered or cancelled.";
            return;
        }
        if (command.Operation == DetailPresentationOperation.Invalid)
        { Transcript = "Detail preference input not accepted. " + DetailPresentationCommand.Syntax; return; }
        if (command.Operation == DetailPresentationOperation.Cancel)
        {
            CancelDetailPresentationReview();
            Transcript = "Detail preference review cancelled. No preference write or other interaction was cancelled.";
            return;
        }
        var service = detailPresentation!;
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
            && CanRevealPrivatePresentation && !IsBusy && CanEnterRetainedAnswerRead
            && settingsOwnershipRevision() == owner && Interlocked.Read(ref settingsOwnershipBindingRevision) == binding
            && assistantNameConfiguration.Get().Revision == name
            && (initiator != SecurityAuditInitiator.LocalUser || ReferenceEquals(native, detailPresentationNativeLifetime) && native());
        detailPresentationActive = true;
        NotifyDetailPresentation();
        try
        {
            if (command.Operation == DetailPresentationOperation.Inspect)
            {
                CancelDetailPresentationReview();
                await service.InspectAsync(origin, Eligible, CancellationToken.None);
            }
            else if (command.Operation == DetailPresentationOperation.Confirm)
            {
                var candidate = detailPresentationReview ?? throw new InvalidOperationException("Review this exact change first; confirmation is separate.");
                detailPresentationReview = null;
                if (!await service.ConfirmAsync(candidate, origin, initiator, communicationPolicy, Eligible, CancellationToken.None))
                { throw new InvalidOperationException("Original call/channel policy denied detail preference confirmation."); }
            }
            else
            {
                CancelDetailPresentationReview();
                detailPresentationReview = await service.ReviewAsync(command.Operation == DetailPresentationOperation.Reset
                    ? DetailPresentationPreference.Default : command.Value ?? throw new InvalidOperationException("Choose Offer or LinkOnly."),
                    origin, CallPolicyRevision, communicationPolicy, Eligible, CancellationToken.None);
            }
            if (!Eligible()) { throw new InvalidOperationException("Detail preference host admission changed before presentation."); }
            Transcript = detailPresentationReview?.Description ?? service.Description;
            // A separate voice confirmation must supply its own freshly admitted capture generation.
            admittingInput = false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or InvalidDataException
            or UnauthorizedAccessException or OperationCanceledException)
        {
            CancelDetailPresentationReview();
            ApplicationLog.Error(logger, exception, "Controlling reviewed detail presentation preference");
            if (!disposed)
            { Transcript = "Detail preference not confirmed. " + exception.Message + " Inspect saved state and audit before explicit recovery; no automatic retry."; }
        }
        finally { detailPresentationActive = false; NotifyDetailPresentation(); }
    }
}
