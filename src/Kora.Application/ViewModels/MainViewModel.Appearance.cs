using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly AppearanceConfigurationService appearanceConfiguration;
    private string appearanceSettingStatus = AppearanceCommand.Syntax;
    private AppearanceOptionDescriptor selectedAppearanceOption = AppearanceOptionRegistry.Options[0];
    private int readerTextScalePercent = ReaderTextScaleSettings.DefaultPercent;

    public int ReaderTextScalePercent
    {
        get => readerTextScalePercent;
        set
        {
            try
            {
                ApplyAppearance(AppearanceOption.ReaderTextScale, new AppearanceValue.Number(value),
                    SecurityAuditInitiator.LocalUser, nameof(ReaderTextScalePercent));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
                or InvalidOperationException or ArgumentOutOfRangeException)
            {
                ApplicationLog.Error(logger, exception, "Native reader text scale preference");
                AppearanceSettingStatus = "Native reader text scale was not confirmed. Inspect saved preference evidence before explicit repair.";
                ShowFailure("Native reader text scale is unavailable.", AppearanceSettingStatus);
                OnPropertyChanged(nameof(ReaderTextScalePercent));
            }
        }
    }

    public void EnsureReaderTypographyAvailable()
    {
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
    public AsyncCommand ResetAppearanceOptionCommand { get; }

    public AppearanceOptionDescriptor SelectedAppearanceOption
    {
        get => selectedAppearanceOption;
        set => SetProperty(ref selectedAppearanceOption, value);
    }

    public string AppearanceSettingStatus
    {
        get => appearanceSettingStatus;
        private set => SetProperty(ref appearanceSettingStatus, value);
    }

    private void OnAppearanceChanged(object? sender, EventArgs eventArgs) => uiDispatcher.Post(SynchronizeAppearance);

    private void SynchronizeAppearance()
    {
        if (disposed)
        {
            return;
        }
        foreach (var descriptor in AppearanceOptions)
        {
            switch (descriptor.Option, appearanceConfiguration.Get(descriptor.Option).Value)
            {
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

    private async Task ResetSelectedAppearanceOptionAsync()
    {
        if (!AdmitAppearanceRequest())
        {
            return;
        }
        var optionState = appearanceConfiguration.Get(SelectedAppearanceOption.Option);
        await ApplyAppearanceProposalAsync(appearanceConfiguration.ProposeReset(
            optionState.Descriptor.Option, optionState.Revision, SecurityAuditInitiator.LocalUser,
            RequestOrigin.LocalUi, () => IsHostInputEligible));
    }

    internal async Task ExecuteAppearanceCommandAsync(AppearanceCommand command, SecurityAuditInitiator initiator)
    {
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
                + Environment.NewLine + AppearanceCommand.Syntax);
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
        uiDispatcher.InvokeAsync(() =>
        {
            if (AdmitAppearanceRequest())
            {
#pragma warning disable VSTHRD103 // Keep the existing synchronous atomic write adjacent to UI-thread host admission; no queued worker may outlive that admission.
                var result = appearanceConfiguration.Apply(proposal);
#pragma warning restore VSTHRD103
                SynchronizeAppearance();
                PresentAppearanceResult(result);
            }
            return Task.CompletedTask;
        });

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
        AppearanceSettingStatus = result.Succeeded ? DescribeAppearanceState(result.State) : result.Error!;
        if (result.Succeeded)
        {
            ShowSuccess(result.Outcome == AppearanceApplyOutcome.Unchanged
                ? "Appearance setting unchanged." : "Appearance setting saved.", AppearanceSettingStatus);
        }
        else
        {
            ShowFailure("The appearance setting was not saved.", AppearanceSettingStatus);
        }
    }

    private static string DescribeAppearanceState(AppearanceOptionState state) =>
        $"{state.Descriptor.Id} = {AppearanceCommand.Format(state.Value)} {state.Descriptor.Units}; revision {state.Revision}. "
        + (state.IsSaved ? "Saved preference is effective on this device." : "Domain default is effective; no saved preference.")
        + $" {state.Descriptor.ApplicationTiming}.";
}
