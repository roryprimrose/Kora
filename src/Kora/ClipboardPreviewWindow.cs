using Avalonia.Controls;
using Kora.Application.ViewModels;
using Kora.Core.Context;

namespace Kora;

internal sealed class ClipboardPreviewWindow : Window, IClipboardPreviewView
{
    internal ClipboardPreviewView View { get; }
    internal ClipboardPreviewWindow(Action<Exception> reportFailure, MainViewModel? typographyHost = null)
    {
        Title = "Local clipboard snapshot (untrusted plain text)";
        Width = 760; Height = 600;
        View = new(this, reportFailure, typographyHost);
        Content = View;
        KeyDown += (_, args) => { if (ReferenceEquals(args.Source, this)) { View.ForwardRootKey(args); } };
    }

    public void Show(ClipboardSnapshot snapshot, Func<Task> reuse, Func<Task> revoke, Func<bool> isCurrent,
        Func<Task>? explain = null, ClipboardQuestionActions? questions = null) =>
        View.Show(snapshot, reuse, revoke, isCurrent, explain, questions);
    public void SetExplanation(string? text) => View.SetExplanation(text);
    public void ClearAndClose() => View.ClearAndClose();
}
