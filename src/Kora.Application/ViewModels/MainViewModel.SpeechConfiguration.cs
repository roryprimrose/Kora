using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly SpeechConfigurationService speechConfiguration;
    private string speechSettingStatus = SpeechCommand.Syntax;

    public IReadOnlyList<SpeechOptionDescriptor> SpeechOptions => SpeechOptionRegistry.Options;
    public IReadOnlyList<SpeechVoice> InstalledSpeechVoices => speechConfiguration.Get().Voices;
    public IReadOnlyList<SpeechProvider> InstalledSpeechProviders => speechConfiguration.Get().Providers
        .Where(provider => speechConfiguration.Get().Voices.Any(voice =>
            string.Equals(voice.ProviderId, provider.Id, StringComparison.Ordinal))).ToArray();
    public AsyncCommand ResetSpeechProviderCommand { get; }
    public AsyncCommand ResetSpeechVoiceCommand { get; }
    public AsyncCommand ResetSummarySentencesCommand { get; }
    public AsyncCommand ResetSummaryWordsCommand { get; }
    public IReadOnlyList<int> SummarySentenceChoices { get; } = Enumerable.Range(1, SpokenSummaryLimits.MaximumSentences).ToArray();
    public IReadOnlyList<int> SummaryWordChoices { get; } = Enumerable.Range(1, SpokenSummaryLimits.MaximumWords).ToArray();
    public int? SelectedSummarySentences
    {
        get => speechConfiguration.Get().SummaryLimits?.Sentences;
        set
        {
            if (!suppressVoicePreferenceSave && value is not null)
            {
                ApplySpeechChoice(SpeechOption.SummarySentences, value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }
    public int? SelectedSummaryWords
    {
        get => speechConfiguration.Get().SummaryLimits?.Words;
        set
        {
            if (!suppressVoicePreferenceSave && value is not null)
            {
                ApplySpeechChoice(SpeechOption.SummaryWords, value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }
    public string SpeechSettingStatus
    {
        get => speechSettingStatus;
        private set => SetProperty(ref speechSettingStatus, value);
    }

    public SpeechProvider? SelectedInstalledSpeechProvider
    {
        get => InstalledSpeechProviders.FirstOrDefault(provider => string.Equals(provider.Id, speechConfiguration.Get().Selection?.ProviderId, StringComparison.Ordinal));
        set
        {
            if (!suppressVoicePreferenceSave && value is not null) { ApplySpeechChoice(SpeechOption.Provider, value.Id); }
        }
    }

    private void OnSpeechConfigurationChanged(object? sender, EventArgs args)
    {
        textToSpeech.InvalidateOutput();
        uiDispatcher.Post(SynchronizeSpeechConfiguration);
    }

    private void SynchronizeSpeechConfiguration()
    {
        if (disposed) { return; }
        var current = speechConfiguration.Get();
        suppressVoicePreferenceSave = true;
        try
        {
            Voices.Clear();
            foreach (var voice in current.Voices.Where(voice => string.Equals(voice.ProviderId, current.Selection?.ProviderId, StringComparison.Ordinal)))
            {
                Voices.Add(voice);
            }
            SelectedVoice = current.IsAvailable ? current.EffectiveVoice : null;
            OnPropertyChanged(nameof(InstalledSpeechProviders));
            OnPropertyChanged(nameof(InstalledSpeechVoices));
            OnPropertyChanged(nameof(SelectedInstalledSpeechProvider));
            OnPropertyChanged(nameof(SelectedSummarySentences));
            OnPropertyChanged(nameof(SelectedSummaryWords));
        }
        finally { suppressVoicePreferenceSave = false; }
        SetActiveSpeechVoice(current.IsAvailable ? current.EffectiveVoice : null);
        VoiceAvailabilityMessage = current.Recovery
            ?? $"{current.EffectiveVoice!.Name} ({current.EffectiveVoice.Culture}) is selected.";
        SpeechSettingStatus = DescribeSpeechConfiguration(current);
        UpdateSpeechProviderAvailability();
    }

    private void ApplySpeechChoice(SpeechOption option, string? value,
        SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser, bool reset = false)
    {
        var current = speechConfiguration.Get();
        SpeechProposal proposal;
        try
        {
            proposal = reset
                ? speechConfiguration.ProposeReset(option, current.Revision, CallPolicyRevision, OriginalOrigin(initiator), initiator)
                : speechConfiguration.Propose(option, value, current.Revision, CallPolicyRevision, OriginalOrigin(initiator), initiator);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or InvalidOperationException)
        {
            ShowInformation("Choose an installed speech setting.", exception.Message);
            return;
        }
        ApplySpeechProposal(proposal);
    }

    private bool ApplySpeechProposal(SpeechProposal proposal)
    {
        var result = speechConfiguration.Apply(proposal, communicationPolicy,
            () => IsCallMutationHostEligible && !IsSpeechProviderOperationActive && !IsBusy);
        SynchronizeSpeechConfiguration();
        if (!result.Succeeded)
        {
            SpeechSettingStatus = result.Error!;
            ShowFailure("The speech setting was not changed.", result.Error!);
        }
        return result.Succeeded;
    }

    private Task ResetSpeechAsync(SpeechOption option)
    {
        ApplySpeechChoice(option, null, reset: true);
        return Task.CompletedTask;
    }

    internal Task ExecuteSpeechCommandAsync(SpeechCommand command, SecurityAuditInitiator initiator)
    {
        if (!AdmitAppearanceRequest()) { return Task.CompletedTask; }
        if (command.Operation == AppearanceCommandOperation.Clarify)
        {
            ShowInformation("Clarify the speech setting.", command.Error!);
            return Task.CompletedTask;
        }
        var current = speechConfiguration.Get();
        if (command.Operation == AppearanceCommandOperation.List)
        {
            ShowInformation("Installed speech settings.",
                string.Join(Environment.NewLine, SpeechOptions.Select(item =>
                    $"{item.Id} (spoken: {item.SpokenName}): {item.Type}; default {item.Default}; {item.Scope}; {item.Effect}; {item.Availability}; {item.ApplicationTiming}; {item.Confirmation}; {item.ResetEffect}"))
                + Environment.NewLine + "Installed providers: " + string.Join(", ", InstalledSpeechProviders.Select(item => item.Id))
                + Environment.NewLine + "Installed voices (provider / ID): " + string.Join(", ", current.Voices.Select(item => item.ConfigurationId))
                + Environment.NewLine + DescribeSpeechConfiguration(current) + Environment.NewLine + SpeechCommand.Syntax);
            return Task.CompletedTask;
        }
        var descriptor = command.Descriptor ?? throw new InvalidOperationException("A speech option is required.");
        if (command.Operation == AppearanceCommandOperation.Get)
        {
            ShowInformation(descriptor.SpokenName, DescribeSpeechConfiguration(current));
            return Task.CompletedTask;
        }
        SpeechProposal proposal;
        try
        {
            proposal = command.Operation == AppearanceCommandOperation.Reset
                ? speechConfiguration.ProposeReset(descriptor.Option, current.Revision, CallPolicyRevision, OriginalOrigin(initiator), initiator)
                : speechConfiguration.Propose(descriptor.Option,
                    command.Value ?? throw new InvalidOperationException("A speech value is required."),
                    current.Revision, CallPolicyRevision, OriginalOrigin(initiator), initiator);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or InvalidOperationException)
        {
            ShowInformation("Choose an installed speech setting.", exception.Message);
            return Task.CompletedTask;
        }
        return uiDispatcher.InvokeAsync(() =>
        {
            if (ApplySpeechProposal(proposal))
            {
                ShowInformation("Speech configuration.", SpeechSettingStatus);
            }
            return Task.CompletedTask;
        });
    }

    private static string DescribeSpeechConfiguration(SpeechConfigurationState state) =>
        $"schema {SpeechOptionRegistry.SchemaVersion}; speech.provider = {state.Selection?.ProviderId ?? "invalid"}; speech.voice = {state.Selection?.VoiceId ?? "default"}; "
        + $"effective voice = {state.EffectiveVoice?.Id ?? "unavailable"}; revision {state.Revision}; "
        + (state.IsSaved ? "saved selection. " : "unsaved selection defaults. ") + (state.Recovery ?? "Ready installed assets.")
        + $" speech.summary-sentences = {state.SummaryLimits?.Sentences.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "invalid"}; "
        + $"speech.summary-words = {state.SummaryLimits?.Words.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "invalid"}; "
        + (state.AreSummaryLimitsSaved ? "saved limits. " : "unsaved limit defaults. ")
        + (state.SummaryLimitsRecovery ?? "Ordinary speech must fit both caps; no truncation; full visual results are preserved.");
}
