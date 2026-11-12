using Kora.Application.Configuration;
using Kora.Application.Configuration.RegisteredSettings;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private ConversationMessage? settingCollectionSource;
    private IReadOnlyList<RegisteredSettingProposal> settingCollectionMembers = [];
    private RegisteredSettingProposal? selectedSettingCollectionMember;
    private Func<bool>? settingCollectionCurrent;

    internal Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, RegisteredSetting?, CancellationToken, Task<AppearanceSuggestionReview>>? ReviewCollectionAppearance { get; set; }
    internal Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, RegisteredSetting?, CancellationToken, Task<SpeechSummarySuggestionReview>>? ReviewCollectionSummary { get; set; }
    internal Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, RegisteredSetting?, CancellationToken, Task<PlaybackVolumeSuggestionReview>>? ReviewCollectionVolume { get; set; }
    internal Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, RegisteredSetting?, CancellationToken, Task<ResponseModeSuggestionReview>>? ReviewCollectionResponseMode { get; set; }
    internal Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, RegisteredSetting?, CancellationToken, Task<WindowsSpeechRateSuggestionReview>>? ReviewCollectionWindowsRate { get; set; }
    internal Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, RegisteredSetting?, CancellationToken, Task<InstalledSpeechVoiceSuggestionReview>>? ReviewCollectionInstalledVoice { get; set; }
    internal Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, RegisteredSetting?, CancellationToken, Task<VoiceCaptureTimingSuggestionReview>>? ReviewCollectionCaptureTiming { get; set; }
    internal Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, RegisteredSetting?, CancellationToken, Task<QuietHoursSuggestionReview>>? ReviewCollectionQuietHours { get; set; }
    internal Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, RegisteredSetting?, CancellationToken, Task<DetailPresentationSuggestionReview>>? ReviewCollectionDetailPresentation { get; set; }
    internal Func<ConversationMessage, Func<bool>, Func<CancellationToken, Task>, RegisteredSetting?, CancellationToken, Task<OrdinaryTurnDeadlineSuggestionReview>>? ReviewCollectionOrdinaryDeadline { get; set; }

    public bool HasRegisteredSettingCollection => settingCollectionCurrent?.Invoke() == true;
    public string RegisteredSettingCollectionText => HasRegisteredSettingCollection ? settingCollectionSource!.AssistantResponse!.Body : string.Empty;
    public IReadOnlyList<RegisteredSettingProposal> RegisteredSettingCollectionMembers => HasRegisteredSettingCollection ? settingCollectionMembers : [];
    public RegisteredSettingProposal? SelectedRegisteredSettingMember
    {
        get => HasRegisteredSettingCollection ? selectedSettingCollectionMember : null;
        set
        {
            if (value is not null && (!HasRegisteredSettingCollection
                || !settingCollectionMembers.Any(member => ReferenceEquals(member, value))))
            { throw new InvalidOperationException("Select only an exact member of the currently inspected original answer."); }
            selectedSettingCollectionMember = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanReviewSelectedRegisteredSetting));
        }
    }

    public bool CanReviewSelectedRegisteredSetting => CanReviewRegisteredSettingSuggestion
        && HasRegisteredSettingCollection && selectedSettingCollectionMember is not null;

    public Task ReviewSelectedRegisteredSettingAsync()
    {
        var source = settingCollectionSource;
        var member = SelectedRegisteredSettingMember;
        if (!CanReviewSelectedRegisteredSetting || source is null || member is null)
        {
            conversationStatus = "Inspect the complete original answer and deliberately choose one current member before review. Nothing changed.";
            OnPropertyChanged(nameof(ConversationStatus));
            return Task.CompletedTask;
        }
        return ReviewRegisteredSettingSuggestionAsync(source, null, member.Setting);
    }

    private void ClearSettingSuggestionCollection()
    {
        settingCollectionCurrent = null;
        settingCollectionSource = null;
        settingCollectionMembers = [];
        selectedSettingCollectionMember = null;
    }

    private void NotifySettingSuggestionCollection()
    {
        if (!HasRegisteredSettingCollection) { ClearSettingSuggestionCollection(); }
        OnPropertyChanged(nameof(HasRegisteredSettingCollection));
        OnPropertyChanged(nameof(RegisteredSettingCollectionText));
        OnPropertyChanged(nameof(RegisteredSettingCollectionMembers));
        OnPropertyChanged(nameof(SelectedRegisteredSettingMember));
        OnPropertyChanged(nameof(CanReviewSelectedRegisteredSetting));
    }
}
