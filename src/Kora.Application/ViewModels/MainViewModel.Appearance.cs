using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Hosting;
using Kora.Core.Diagnostics;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public async ValueTask DisposeAsync()
    {
        Dispose();
        if (completionNotices is not null) { await completionNotices.DisposeAsync().ConfigureAwait(false); }
        // A forced desktop close need not pump UI continuations to join the owned storage write.
#pragma warning disable VSTHRD003 // The owned persistence task runs entirely on the thread pool.
        await Task.WhenAll(appearancePersistence, preferencePersistence, consentPersistence,
            appearanceSuggestionPersistence, speechSummaryPersistence, volumeSuggestionPersistence, volumeSuggestionReviewWork,
            responseModeSuggestionWork,
            AuditPersistence, outputDiscoveryPersistence,
            outputCatalog.WaitForQuiescenceAsync()).ConfigureAwait(false);
        if (communicationActivity is not null)
        { await communicationActivity.JoinAsync().ConfigureAwait(false); }
#pragma warning restore VSTHRD003
    }

    private readonly AppearanceConfigurationService appearanceConfiguration;
    private string appearanceSettingStatus = AppearanceCommand.Syntax;
    private int readerTextScalePercent = ReaderTextScaleSettings.DefaultPercent;
    private readonly Dictionary<AppearanceOption, PendingAppearance> pendingAppearance = [];
    private bool applyingAppearance;
    private Task appearancePersistence = Task.CompletedTask;
    private Task appearanceSuggestionPersistence = Task.CompletedTask;

    public async Task<AppearanceApplyResult> ConfirmAppearanceSuggestionAsync(AppearanceSuggestionReview review, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (disposed || !appearanceSuggestionPersistence.IsCompleted)
        { throw new InvalidOperationException("Appearance confirmation is unavailable while the host is closing or another confirmation is pending."); }
        var work = review.ConfirmAsync(token);
        appearanceSuggestionPersistence = work;
        try { return await work; }
        finally { appearanceSuggestionPersistence = Task.CompletedTask; }
    }

    public Task<AppearanceSuggestionReview> ReviewAppearanceSuggestionAsync(
        Kora.Core.Conversation.ConversationMessage message, Func<bool> sourceCurrent,
        Func<CancellationToken, Task> validateSource, CancellationToken token)
    {
        var privacy = privacyObservation.Current.TopologyRevision;
        var call = CallPolicyRevision;
        var ownership = settingsOwnershipRevision();
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        bool Eligible() => !disposed && IsCallMutationHostEligible && !IsProtectedCall
            && !IsBusy && !IsResponseInteractionPending && !applyingAppearance
            && privacyObservation.Current.TopologyRevision == privacy && CallPolicyRevision == call
            && settingsOwnershipRevision() == ownership
            && Interlocked.Read(ref settingsOwnershipBindingRevision) == binding && sourceCurrent();
        return AppearanceSuggestionReview.ReviewAsync(appearanceConfiguration, message, Eligible, validateSource, token);
    }

    public Task AppearanceUpdate { get; private set; } = Task.CompletedTask;
    public bool IsReaderTypographyUpdatePending => applyingAppearance || !appearanceSuggestionPersistence.IsCompleted;

    private sealed record PendingAppearance(AppearanceValue Value, SecurityAuditInitiator Initiator,
        string PropertyName, Func<bool> Eligible, Func<HostActivity> Begin);

    public int ReaderTextScalePercent
    {
        get => readerTextScalePercent;
        set => QueueAppearance(AppearanceOption.ReaderTextScale, new AppearanceValue.Number(value), nameof(ReaderTextScalePercent));
    }

    public void EnsureReaderTypographyAvailable()
    {
        if (IsReaderTypographyUpdatePending)
        {
            throw new InvalidOperationException("Native reader text scale is being saved. Wait for the preference result before opening a reader.");
        }
        try
        {
            var confirmed = appearanceConfiguration.Get(AppearanceOption.ReaderTextScale).Value.GetNumber();
            SetProperty(ref readerTextScalePercent, confirmed, nameof(ReaderTextScalePercent));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ApplicationLog.Error(logger, exception, "Native reader typography admission");
            throw new InvalidOperationException(
                "Native reader text scale is unavailable. Inspect saved preference evidence and audit receipts before explicit repair.",
                exception);
        }
    }

    public int ReaderTextScaleMinimum => ReaderTextScaleSettings.MinimumPercent;
    public int ReaderTextScaleMaximum => ReaderTextScaleSettings.MaximumPercent;

    public IReadOnlyList<AppearanceOptionDescriptor> AppearanceOptions => AppearanceOptionRegistry.Options;
    public AsyncCommand ResetThemeCommand { get; }
    public AsyncCommand ResetReaderTextScaleCommand { get; }
    public AsyncCommand ResetResponseTimeoutCommand { get; }
    public AsyncCommand ResetResponseAlwaysVisibleCommand { get; }
    public AsyncCommand ResetResponseWindowTopmostCommand { get; }
    public AsyncCommand ResetPresenceDisplayCommand { get; }
    public AsyncCommand ResetPresenceRendererCommand { get; }
    public AsyncCommand ResetPresenceTimeoutCommand { get; }
    public AsyncCommand ResetPresenceSizeCommand { get; }
    public AsyncCommand ResetPresenceDotSizeCommand { get; }
    public AsyncCommand ResetPresenceDotDensityCommand { get; }
    public AsyncCommand ResetPresenceMovementSpeedCommand { get; }
    public AsyncCommand ResetPresenceSpeechScalingCommand { get; }
    public AsyncCommand ResetPresenceSpeechScaleAmountCommand { get; }

    public string AppearanceSettingStatus
    {
        get => appearanceSettingStatus;
        private set => SetProperty(ref appearanceSettingStatus, value);
    }

    private void OnAppearanceChanged(object? sender, EventArgs eventArgs) => uiDispatcher.Post(SynchronizeAppearance);

    private void SynchronizeAppearance()
    {
        if (disposed || applyingAppearance)
        {
            return;
        }
        foreach (var descriptor in AppearanceOptions)
        {
            switch (descriptor.Option, appearanceConfiguration.Get(descriptor.Option).Value)
            {
                case (AppearanceOption.NativePresenceRenderer, AppearanceValue.Toggle boolean):
                    nativePresenceRendererObserved = true;
                    if (isNativePresenceRenderer != boolean.Value)
                    {
                        PresenceSwitchTiming.Mark(PresenceSwitchPhase.RendererNotification);
                    }
                    if (SetProperty(ref isNativePresenceRenderer, boolean.Value, nameof(IsNativePresenceRenderer)))
                        OnPropertyChanged(nameof(SelectedPresenceRenderer));
                    break;
                case (AppearanceOption.ReaderTextScale, AppearanceValue.Number integer):
                    SetProperty(ref readerTextScalePercent, integer.Value, nameof(ReaderTextScalePercent)); break;
                case (AppearanceOption.Theme, AppearanceValue.Theme theme):
                    SetProperty(ref themeMode, theme.Value, nameof(ThemeMode)); break;
                case (AppearanceOption.PresenceDisplay, AppearanceValue.Toggle boolean):
                    if (SetProperty(ref isPresenceDisplayEnabled, boolean.Value, nameof(IsPresenceDisplayEnabled)))
                        OnPropertyChanged(nameof(PresenceDisplayDescription));
                    break;
                case (AppearanceOption.PresenceTimeout, AppearanceValue.Number integer):
                    SetProperty(ref presenceTimeoutSeconds, integer.Value, nameof(PresenceTimeoutSeconds)); break;
                case (AppearanceOption.ResponseTimeout, AppearanceValue.Number integer):
                    SetProperty(ref responseTimeoutSeconds, integer.Value, nameof(ResponseTimeoutSeconds)); break;
                case (AppearanceOption.PresenceSize, AppearanceValue.Number integer):
                    if (SetProperty(ref presenceSizePixels, integer.Value, nameof(PresenceSizePixels)))
                        OnPropertyChanged(nameof(PresenceSizeDescription));
                    break;
                case (AppearanceOption.DotSize, AppearanceValue.Number integer):
                    if (SetProperty(ref presenceDotSizePercent, integer.Value, nameof(PresenceDotSizePercent)))
                        OnPropertyChanged(nameof(PresenceDotSizeDescription));
                    break;
                case (AppearanceOption.DotDensity, AppearanceValue.Number integer):
                    if (SetProperty(ref presenceDotDensityPercent, integer.Value, nameof(PresenceDotDensityPercent)))
                        OnPropertyChanged(nameof(PresenceDotDensityDescription));
                    break;
                case (AppearanceOption.MovementSpeed, AppearanceValue.Number integer):
                    if (SetProperty(ref presenceMovementSpeedPercent, integer.Value, nameof(PresenceMovementSpeedPercent)))
                        OnPropertyChanged(nameof(PresenceMovementSpeedDescription));
                    break;
                case (AppearanceOption.SpeechScaling, AppearanceValue.Toggle boolean):
                    if (SetProperty(ref isPresenceSpeechScalingEnabled, boolean.Value, nameof(IsPresenceSpeechScalingEnabled)))
                        OnPropertyChanged(nameof(PresenceSpeechScalingDescription));
                    break;
                case (AppearanceOption.SpeechScaleAmount, AppearanceValue.Number integer):
                    if (SetProperty(ref presenceSpeechScaleAmountPercent, integer.Value, nameof(PresenceSpeechScaleAmountPercent)))
                        OnPropertyChanged(nameof(PresenceSpeechScaleAmountDescription));
                    break;
            }
        }
    }

    private void QueueAppearance(AppearanceOption option, AppearanceValue value, string propertyName)
    {
        var timeRenderer = option == AppearanceOption.NativePresenceRenderer;
        if (timeRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.Selection); }
        if (option != AppearanceOption.ReaderTextScale) { AppearanceOptionRegistry.Validate(option, value); }
        if (disposed || !AdmitAppearanceRequest())
        {
            if (timeRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.SelectionDenied); }
            OnPropertyChanged(propertyName);
            return;
        }
        var privacy = privacyObservation.Current.TopologyRevision;
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        var pending = new PendingAppearance(value, SecurityAuditInitiator.LocalUser, propertyName,
            () => !disposed && IsHostInputEligible && privacyObservation.Current.TopologyRevision == privacy
                && Interlocked.Read(ref voiceRecoveryRevision) == recovery,
            HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Storage, RequestOrigin.LocalUi));
        // A slider may change faster than storage can commit. Retain only its latest absolute value.
        pendingAppearance[option] = pending;
        if (timeRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.Queued); }
        if (!applyingAppearance) { AppearanceUpdate = DrainAppearanceAsync(); }
    }

    private async Task DrainAppearanceAsync()
    {
        applyingAppearance = true;
        var properties = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            while (pendingAppearance.Count > 0)
            {
                var next = pendingAppearance.First();
                pendingAppearance.Remove(next.Key);
                properties.Add(next.Value.PropertyName);
                if (disposed) { pendingAppearance.Clear(); return; }
                try
                {
                    using var activity = next.Value.Begin();
                    var work = Task.Run(() =>
                    {
                        var timeRenderer = next.Key == AppearanceOption.NativePresenceRenderer;
                        if (timeRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.WorkerStarted); }
                        if (!next.Value.Eligible())
                        {
                            throw new InvalidOperationException("The original appearance input is no longer eligible. Initiate a fresh change.");
                        }
                        if (timeRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.ConfigurationReadStarted); }
                        var optionState = appearanceConfiguration.Get(next.Key);
                        if (timeRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.ConfigurationReadReturned); }
                        var applied = appearanceConfiguration.Apply(appearanceConfiguration.Propose(next.Key, next.Value.Value,
                            optionState.Revision, next.Value.Initiator, RequestOrigin.LocalUi, next.Value.Eligible));
                        if (timeRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.ApplyReturned); }
                        return applied;
                    });
                    appearancePersistence = work;
                    var result = await work;
                    if (next.Key == AppearanceOption.NativePresenceRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.UiContinuation); }
                    activity.Complete(result.Succeeded ? Kora.Core.Diagnostics.HostOperationOutcome.Completed
                        : Kora.Core.Diagnostics.HostOperationOutcome.Failed);
                    if (!disposed && next.Value.Eligible())
                    {
                        AppearanceSettingStatus = result.Succeeded ? Presentation.SettingsText.Describe(result.State) : result.Error!;
                        if (!result.Succeeded) { ShowFailure("The appearance setting was not saved.", result.Error!); }
                    }
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
                    or InvalidOperationException or ArgumentOutOfRangeException or AggregateException)
                {
                    if (next.Key == AppearanceOption.NativePresenceRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.AppearanceFailed); }
                    if (disposed) { return; }
                    try { ApplicationLog.Error(logger, exception, "Saving an appearance preference"); }
                    finally
                    {
                        AppearanceSettingStatus = "Appearance preference not confirmed. Inspect saved preference evidence before explicit repair.";
                        ShowFailure(next.Key == AppearanceOption.ReaderTextScale
                            ? "Native reader text scale is unavailable." : "The appearance setting was not saved.", AppearanceSettingStatus);
                    }
                }
                finally
                {
                    appearancePersistence = Task.CompletedTask;
                }
            }
        }
        finally
        {
            applyingAppearance = false;
            if (!disposed)
            {
                try { SynchronizeAppearance(); }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    ApplicationLog.Error(logger, exception, "Reading the confirmed appearance state");
                    AppearanceSettingStatus = "Appearance preference not confirmed. Inspect saved preference evidence before explicit repair.";
                    ShowFailure("Native reader text scale is unavailable.", AppearanceSettingStatus);
                }
                foreach (var propertyName in properties) { OnPropertyChanged(propertyName); }
            }
        }
    }

    private bool ApplyAppearance(AppearanceOption option, AppearanceValue value,
        SecurityAuditInitiator initiator, string propertyName)
    {
        if (!AdmitAppearanceRequest())
        {
            OnPropertyChanged(propertyName);
            return false;
        }
        var optionState = appearanceConfiguration.Get(option);
        var proposal = appearanceConfiguration.Propose(option, value, optionState.Revision, initiator,
            OriginalOrigin(initiator), () => IsHostInputEligible);
        var result = appearanceConfiguration.Apply(proposal);
        SynchronizeAppearance();
        if (!result.Succeeded)
        {
            OnPropertyChanged(propertyName);
            AppearanceSettingStatus = result.Error!;
            ShowFailure($"The {optionState.Descriptor.Description.ToLowerInvariant()} could not be saved.", result.Error!);
        }
        return result.Succeeded;
    }

    private async Task ResetAppearanceOptionAsync(AppearanceOption option)
    {
#pragma warning disable VSTHRD003 // Serialize against the owned appearance workflow without blocking the dispatcher.
        await AppearanceUpdate;
#pragma warning restore VSTHRD003
        if (!AdmitAppearanceRequest())
        {
            return;
        }
        var optionState = appearanceConfiguration.Get(option);
        await ApplyAppearanceProposalAsync(appearanceConfiguration.ProposeReset(
            optionState.Descriptor.Option, optionState.Revision, SecurityAuditInitiator.LocalUser,
            RequestOrigin.LocalUi, () => IsHostInputEligible));
    }

    internal async Task ExecuteAppearanceCommandAsync(AppearanceCommand command, SecurityAuditInitiator initiator)
    {
        try { await ExecuteAdmittedAppearanceCommandAsync(command, initiator); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ReportUnconfirmedAppearance(exception);
        }
    }

    private async Task ExecuteAdmittedAppearanceCommandAsync(AppearanceCommand command, SecurityAuditInitiator initiator)
    {
#pragma warning disable VSTHRD003 // Serialize against the owned appearance workflow without blocking the dispatcher.
        await AppearanceUpdate;
#pragma warning restore VSTHRD003
        if (!AdmitAppearanceRequest())
        {
            return;
        }
        if (command.Operation == AppearanceCommandOperation.Clarify)
        {
            ShowInformation("Clarify the appearance setting.", command.Error!);
            return;
        }
        if (command.Operation == AppearanceCommandOperation.List)
        {
            ShowInformation("Appearance settings on this device.",
                string.Join(Environment.NewLine, AppearanceOptions.Select(item =>
                    $"{item.Id} (spoken: {item.SpokenName}): {item.Description}; {item.Type}, {item.Units}; default {AppearanceCommand.Format(item.Default)}"
                    + (item.Minimum is { } minimum ? $"; range {minimum}-{item.Maximum}" : string.Empty)
                    + $"; {item.Scope}, {item.Effect}, {item.Availability}, {item.ApplicationTiming}."))
                + Environment.NewLine + AppearanceCommand.Syntax
                + Environment.NewLine + NaturalAppearanceCommand.Syntax);
            return;
        }
        if (command.ReaderTextScaleAdjustment is { } percentagePoints)
        {
            AppearanceProposal adjustment;
            try
            {
                adjustment = appearanceConfiguration.ProposeReaderTextScaleAdjustment(percentagePoints, initiator,
                    OriginalOrigin(initiator), () => IsHostInputEligible);
            }
            catch (Exception exception) when (exception is OverflowException or ArgumentOutOfRangeException)
            {
                ShowInformation("Clarify the appearance setting.",
                    "The reader text scale adjustment is outside the admitted range. " + NaturalAppearanceCommand.Syntax);
                return;
            }
            await ApplyAppearanceProposalAsync(adjustment);
            return;
        }
        var descriptor = command.Descriptor ?? throw new InvalidOperationException("An appearance option is required.");
        var optionState = appearanceConfiguration.Get(descriptor.Option);
        if (command.Operation == AppearanceCommandOperation.Get)
        {
            ShowInformation(descriptor.Description, DescribeAppearanceState(optionState));
            return;
        }
        var proposal = command.Operation == AppearanceCommandOperation.Reset
            ? appearanceConfiguration.ProposeReset(descriptor.Option, optionState.Revision, initiator,
                OriginalOrigin(initiator), () => IsHostInputEligible)
            : appearanceConfiguration.Propose(descriptor.Option,
                command.Value ?? throw new InvalidOperationException("An appearance value is required."), optionState.Revision, initiator,
                OriginalOrigin(initiator), () => IsHostInputEligible);
        await ApplyAppearanceProposalAsync(proposal);
    }

    private Task ApplyAppearanceProposalAsync(AppearanceProposal proposal) =>
        AppearanceUpdate = uiDispatcher.InvokeAsync(async () =>
        {
            if (AdmitAppearanceRequest())
            {
                applyingAppearance = true;
                try
                {
                    var work = appearanceConfiguration.ApplyAsync(proposal, CancellationToken.None);
                    appearancePersistence = work;
                    var result = await work;
                    if (!disposed && IsHostInputEligible) { PresentAppearanceResult(result); }
                }
                finally
                {
                    appearancePersistence = Task.CompletedTask;
                    applyingAppearance = false;
                    if (!disposed)
                    {
                        try { SynchronizeAppearance(); }
                        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                        {
                            ReportUnconfirmedAppearance(exception);
                        }
                    }
                    if (pendingAppearance.Count > 0) { AppearanceUpdate = DrainAppearanceAsync(); }
                }
            }
        });

    private void ReportUnconfirmedAppearance(Exception exception)
    {
        ApplicationLog.Error(logger, exception, "Confirming an appearance command");
        AppearanceSettingStatus = "Appearance preference not confirmed. Inspect saved preference evidence and audit receipts before explicit repair.";
        ShowFailure("Appearance settings are unavailable.", AppearanceSettingStatus);
    }

    private bool AdmitAppearanceRequest()
    {
        if (IsHostInputEligible)
        {
            return true;
        }
        ApplicationLog.Information(logger, "Rejected appearance request outside the active unlocked host");
        AppearanceSettingStatus = "Appearance settings require the active, unlocked local host.";
        ShowFailure("Appearance settings are unavailable.", AppearanceSettingStatus);
        return false;
    }

    private void PresentAppearanceResult(AppearanceApplyResult result)
    {
        AppearanceSettingStatus = result.Succeeded ? Presentation.SettingsText.Describe(result.State) : result.Error!;
        if (result.Succeeded)
        {
            ShowSuccess(result.Outcome == AppearanceApplyOutcome.Unchanged
                ? "Appearance setting unchanged." : "Appearance setting saved.", DescribeAppearanceState(result.State));
        }
        else
        {
            ShowFailure("The appearance setting was not saved.", AppearanceSettingStatus);
        }
    }

    private static string DescribeAppearanceState(AppearanceOptionState state) =>
        $"{state.Descriptor.Id} = {AppearanceCommand.Format(state.Value)} {state.Descriptor.Units}; revision {state.Revision}. "
        + (state.IsSaved ? "Saved preference is effective on this device."
            : state.Descriptor.Option == AppearanceOption.NativePresenceRenderer
                ? "Startup choice is effective; no saved preference." : "Domain default is effective; no saved preference.")
        + $" {state.Descriptor.ApplicationTiming}.";
}