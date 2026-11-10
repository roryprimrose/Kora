using Avalonia.Controls;
using Kora.Application.Hosting.SessionRouting;

namespace Kora;

internal sealed class SessionRoutingAdviceWindow : Window
{
    internal SessionRoutingAdviceView View { get; }
    internal bool? Result => View.Result;
    internal SessionRoutingAdviceWindow(SessionRoutingAdvice advice, Func<bool> admitted)
    {
        Title = "Review local destination advice";
        Width = 700; Height = 540; MinWidth = 500; MinHeight = 400;
        View = new(this, advice, admitted);
        Content = View;
        KeyDown += (_, args) => { if (ReferenceEquals(args.Source, this)) { View.ForwardRootKey(args); } };
    }
    internal void Withdraw() => View.Withdraw();
}
