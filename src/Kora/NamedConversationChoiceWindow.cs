using Avalonia.Controls;
using Kora.Core.Storage;

namespace Kora;

internal sealed class NamedConversationChoiceWindow : Window
{
    internal NamedConversationChoiceView View { get; }
    internal SessionWorkspaceEntry? Result => View.Result;
    internal NamedConversationChoiceWindow(SessionListSearchPage page, Func<bool> admitted)
    {
        Title = "Choose the conversation to read";
        Width = 580;
        SizeToContent = SizeToContent.Height;
        View = new(this, page, admitted);
        Content = View;
    }
    internal void Withdraw() => View.Withdraw();
}
