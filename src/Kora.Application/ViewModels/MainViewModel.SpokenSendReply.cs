using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Hosting.SessionRouting;
using Kora.Application.Infrastructure;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private SpokenSendReplyConfigurationService? spokenSendReplies;
    private Func<bool> spokenSendNativeLifetime = static () => false;
    private Func<Func<bool>, (Func<bool> Current, Action Close)>? presentSpokenSendReview;
    private bool spokenSendSettingActive;
    private bool spokenSendReadback;
    private Task sessionRequestNativeDecision = Task.CompletedTask;
    public string SessionRequestVoiceReplyStatus => sessionRoutingVoiceReply?.Status ?? "Microphone closed.";
    public bool AreSpokenSendRepliesEnabled => spokenSendReplies?.Get()?.Enabled == true;
    public string SpokenSendReplyStatus => spokenSendReplies?.Get() is { } state
        ? state.Enabled ? "On. Voice-originated request reviews are read back before one exact send or cancel reply."
            : "Off (default). Confirm requests using native controls or an explicit command."
        : "Spoken send replies unavailable. Inspect saved preference and audit; native confirmation remains available.";
    public bool CanChangeSpokenSendReplies => spokenSendReplies?.Get() is not null && !spokenSendSettingActive
        && spokenSendNativeLifetime() && IsCallMutationHostEligible && !IsResponseInteractionPending;
    public AsyncCommand EnableSpokenSendRepliesCommand { get; private set; } = null!;
    public AsyncCommand DisableSpokenSendRepliesCommand { get; private set; } = null!;

    public void BindSpokenSendReplyConfiguration(SpokenSendReplyConfigurationService service)
    {
        spokenSendReplies = service;
        try { service.Observe(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { ApplicationLog.Error(logger, exception, "Reading spoken send reply preference"); }
        void Failed(Exception exception)
        {
            ApplicationLog.Error(logger, exception, "Changing spoken send reply preference");
            ShowFailure("Spoken send reply setting not confirmed.", "Inspect saved state and audit before recovery. No automatic reply was authorized.");
        }
        EnableSpokenSendRepliesCommand = new(() => SetSpokenSendRepliesAsync(true), Failed);
        DisableSpokenSendRepliesCommand = new(() => SetSpokenSendRepliesAsync(false), Failed);
        NotifySpokenSendReplyChanged();
    }

    public void BindSpokenSendReplyNativeLifetime(Func<bool> lifetime)
    {
        spokenSendNativeLifetime = lifetime;
        OnPropertyChanged(nameof(CanChangeSpokenSendReplies));
    }

    public void BindSpokenSendReview(Func<Func<bool>, (Func<bool> Current, Action Close)> present) =>
        presentSpokenSendReview = present;

    private void NotifySpokenSendReplyChanged()
    {
        sessionRoutingVoiceReply?.Revalidate();
        OnPropertyChanged(nameof(AreSpokenSendRepliesEnabled));
        OnPropertyChanged(nameof(SpokenSendReplyStatus));
        OnPropertyChanged(nameof(CanChangeSpokenSendReplies));
    }

    private async Task SetSpokenSendRepliesAsync(bool enabled)
    {
        if (spokenSendReplies!.Get() is not { } state || !CanChangeSpokenSendReplies)
        {
            ShowFailure("Spoken send reply setting unavailable.", "Use the current unlocked owning Settings view with no pending approval.");
            return;
        }
        var native = spokenSendNativeLifetime;
        var origin = OriginalOrigin(SecurityAuditInitiator.LocalUser);
        var admitted = CaptureAudioControlEligibility(origin);
        bool Eligible() => !disposed && admitted() && ReferenceEquals(native, spokenSendNativeLifetime) && native();
        spokenSendSettingActive = true;
        NotifySpokenSendReplyChanged();
        try
        {
            if (!await spokenSendReplies.SetAsync(enabled, state.Revision, CallPolicyRevision, origin,
                SecurityAuditInitiator.LocalUser, communicationPolicy, Eligible, CancellationToken.None))
            { ShowFailure("Spoken send reply setting denied.", "Current call/channel policy refused the change. Inspect before retrying."); }
        }
        finally
        {
            spokenSendSettingActive = false;
            NotifySpokenSendReplyChanged();
        }
    }

    private async Task SpeakSendReviewAsync(string text, Func<bool> eligible, CancellationToken token)
    {
        spokenSendReadback = true;
        try { await SpeakExactQuestionAsync(text, eligible, token); }
        finally { spokenSendReadback = false; }
    }

    private Func<string, SessionRoutingVoiceRequest>? CaptureSessionRequestVoiceInput(ManualCallInput? input)
    {
        if (input?.Origin != RequestOrigin.ActivatedVoice || HostActivity.Current is not { } current) { return null; }
        var request = current.Request;
        var eligible = CaptureSkillReviewEligibility(input with { Eligible = input.RoutingStableEligible });
        return body => new SessionRoutingVoiceRequest(body, request,
            () => eligible() && input.AssistantNameRevision == assistantNameConfiguration.Get().Revision
                && input.OwnershipBindingRevision == Interlocked.Read(ref settingsOwnershipBindingRevision)
                && input.RoutingVoiceOwnerRevision == Interlocked.Read(ref voiceOwnershipBindingRevision),
            voiceRecognition as IQuestionVoiceRecognitionService, input.RoutingWakeContinuation?.Invoke(), routingCaptureRelease);
    }

    private async Task AnswerSessionRequestByVoiceAsync(SessionRequestReview review, SessionRoutingVoiceRequest source, Func<bool> admitted)
    {
        if (presentSpokenSendReview is not { } present || !ReferenceEquals(foregroundRequestReview, review)) { return; }
        var automaticPermission = await sessionRequests!.ReadReadbackPermissionAsync(review, CancellationToken.None);
        if (automaticPermission is null && !AreSpokenSendRepliesEnabled) { return; }
        var preference = spokenSendReplies?.Get();
        bool SourceCurrent() => source.IsLive && admitted()
            && (automaticPermission is not null || spokenSendReplies?.Get() == preference && preference?.Enabled == true)
            && !disposed && IsHostInputEligible && !IsProtectedCall;
        bool ReviewCurrent() => SourceCurrent() && ReferenceEquals(foregroundRequestReview, review);
        SetApproveAllReadback(automaticPermission is not null);
        var presentation = present(ReviewCurrent);
        bool Exact() => ReviewCurrent() && presentation.Current();
        try
        {
            var reply = SessionRequestVoiceReply.Create(source, review.Target.Metadata?.Name.Value ?? "Unnamed conversation",
                Exact, () => ObserveSessionRoutingVoiceAdmission(source), SpeakSendReviewAsync, logger,
                submitAfterReadback: automaticPermission is not null);
            sessionRoutingVoiceReply = reply;
            reply.Changed += OnSessionRequestVoiceReplyChanged;
            SessionRoutingVoiceReply.SessionRoutingVoiceAnswer? answer;
            try
            {
                answer = await reply.StartAsync();
                await reply.DisposeAsync();
                ReleaseSessionRoutingVoiceReply(reply);
            }
            finally
            {
                reply.Changed -= OnSessionRequestVoiceReplyChanged;
                OnSessionRequestVoiceReplyChanged(this, EventArgs.Empty);
            }
            if (!Exact()) { return; }
            if (answer is null && automaticPermission is null) { return; }
            var command = new SessionCommand(answer is { Choice: SessionRoutingVoiceReply.SessionRoutingVoiceChoice.Submit }
                ? SessionCommandOperation.RequestConfirm : SessionCommandOperation.RequestCancel,
                review.Target.Authority.SessionId.Value) { RequestReviewId = review.Id };
            SetApproveAllReadback(false);
            await ExecuteSessionCommandAsync(command, SecurityAuditInitiator.VoiceCommand, SourceCurrent,
                readbackPermission: command.Operation == SessionCommandOperation.RequestConfirm ? automaticPermission : null);
        }
        finally { SetApproveAllReadback(false); presentation.Close(); }
    }

    private void OnSessionRequestVoiceReplyChanged(object? sender, EventArgs args) =>
        uiDispatcher.Post(() => OnPropertyChanged(nameof(SessionRequestVoiceReplyStatus)));
}
