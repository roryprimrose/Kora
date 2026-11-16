using System.Collections.ObjectModel;

using Kora.Application.Infrastructure;
using Kora.Core.Configuration;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly ReadOnlyCollection<ConversationSideChoice> SideChoices =
        Array.AsReadOnly<ConversationSideChoice>([new("Left", false), new("Right", true)]);
    private bool isConversationOnRight;

    public IReadOnlyList<ConversationSideChoice> ConversationSides => SideChoices;

    /// <summary>Whether the expanded window shows the conversation right of supporting reviews.</summary>
    public bool IsConversationOnRight
    {
        get => isConversationOnRight;
        set => QueueAppearance(AppearanceOption.ConversationOnRight, new AppearanceValue.Toggle(value),
            nameof(IsConversationOnRight));
    }

    public ConversationSideChoice SelectedConversationSide
    {
        get => SideChoices[IsConversationOnRight ? 1 : 0];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            IsConversationOnRight = value.IsRight;
            OnPropertyChanged(nameof(SelectedConversationSide));
        }
    }

    public AsyncCommand ResetConversationSideCommand { get; }

    private void SynchronizeConversationSide(bool value)
    {
        if (SetProperty(ref isConversationOnRight, value, nameof(IsConversationOnRight)))
        {
            OnPropertyChanged(nameof(SelectedConversationSide));
        }
    }
}
