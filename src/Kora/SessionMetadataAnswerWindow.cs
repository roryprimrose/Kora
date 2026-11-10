using Avalonia.Controls;

namespace Kora;

internal sealed class SessionMetadataAnswerWindow : Window
{
    internal SessionMetadataAnswerControls Fields { get; } = new();

    internal SessionMetadataAnswerWindow()
    {
        Title = "Review one session metadata answer";
        Width = 760;
        Height = 740;
        Content = new ScrollViewer { Content = Fields };
        Fields.Status.Text = "Review admits the original caller's local model before inspecting the subject. A separate confirmation is required before model disclosure. No fallback or hosted disclosure.";
    }
}
