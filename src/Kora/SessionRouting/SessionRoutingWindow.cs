using Avalonia.Controls;
using Kora.Application.Hosting.SessionRouting;
using Kora.Core.Conversation.SessionRouting;

namespace Kora;

internal sealed class SessionRoutingWindow : Window
{
    internal SessionRoutingView View { get; }
    internal SessionRoutingSelection? Result => View.Result;
    internal Exception? Failure => View.Failure;
#pragma warning disable VSTHRD003 // The native adapter exposes the actual operations retained by its own view.
    internal Task ChoiceOperation => View.ChoiceOperation;
    internal Task VoiceOperation => View.VoiceOperation;
#pragma warning restore VSTHRD003
    internal SessionRoutingWindow(SessionRoutingService routing, SessionRoutingReview review,
        Func<bool> admitted, Action<Exception> reportFailure,
        Func<SessionRoutingReview, IEnumerable<SessionRouteDescriptor>, SessionRoutingSelection?, Func<bool>, SessionRoutingVoiceReply?>? createVoiceReply = null,
        Action<SessionRoutingVoiceReply>? releaseVoiceReply = null)
    {
        Title = "Review request destination";
        Width = 700; Height = 540; MinWidth = 500; MinHeight = 400;
        View = new(this, routing, review, admitted, reportFailure, createVoiceReply, releaseVoiceReply);
        Content = View;
        KeyDown += (_, args) => { if (ReferenceEquals(args.Source, this)) { View.ForwardRootKey(args); } };
    }
    internal Task JoinAsync() => View.JoinAsync();
    internal void Withdraw() => View.Withdraw();
}
